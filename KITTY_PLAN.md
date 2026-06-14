# KITTY_PLAN.md — Kitty Graphics Protocol in the purrTTY in-game terminal

## Context

purrTTY renders a real terminal inside KSA by delegating VT emulation to libghostty-vt and
drawing the resulting cell grid through ImGui. Today it draws **text only**. Modern TUIs and CLI
tools (`kitten icat`, `timg`, `chafa -f kitty`, `yazi`, image previews in `lf`/`ranger`,
matplotlib-in-terminal, neovim image plugins) use the **kitty graphics protocol** to display real
bitmaps. We want those images to appear in the in-game terminal.

This document is a feasibility-validated build plan. The headline finding from research across
libghostty-vt, the vendored `Ghostty.Vt` binding, the purrTTY layers, and the KSA decompiled
sources (ImGui + Vulkan): **this is plausible and well-supported, and requires no native rebuild.**

---

## Feasibility verdict — GREEN

The capability triangle is satisfied end to end:

1. **Engine (libghostty-vt) already does the hard part.** `Terminal.VTWrite` consumes APC
   graphics commands automatically — image storage, decoding bookkeeping, placement pinning to
   scrollback, deletion, z-ordering, and Unicode virtual placeholders are all handled internally.
   We do **not** write a protocol parser.

2. **The full kitty-graphics C API is already exported in our pinned native libs.** All 16
   functions (`ghostty_kitty_graphics_*` — placement iterator with layer filtering, per-placement
   fields, computed rect/pixel-size/grid-size/viewport-pos/source-rect, and image getters incl.
   `data_ptr`/`data_len`/`format`/`compression`) are present in `vendor/Ghostty.Vt/native/*` at
   pin `7092b39445bebfd3178f562eb9e5fa9a95a32332`. Verified: `grep` finds the symbols in
   `native/win-x64/ghostty-vt.dll` and `native/linux-x64/libghostty-vt.so`. **No zig rebuild.**

3. **The managed binding for those symbols already existed** and was deleted only as dead-code
   cleanup in commit `91fedcf` ("project cleanup"). It is restorable verbatim from `91fedcf^`:
   `src/KittyGraphics.cs` (414 lines: `KittyGraphicsAccessor`, `KittyImage`,
   `KittyGraphicsPlacementIterator`), `src/Types/KittyGraphics*.cs`, `src/Types/KittyPlacementRect.cs`,
   and the `Enums/KittyImage{Format,Compression}.cs` / `KittyPlacementLayer.cs`, plus the
   `NativeMethods.cs` P/Invoke block.

4. **KSA's ImGui/Vulkan stack can draw arbitrary textures.** `ImTextureID` is an arbitrary `nint`
   (a Vulkan descriptor-set handle), **not** hardwired to the font atlas. The backend is reachable
   from a mod via the public static `KSA.ImGuiBackend.Vulkan` →
   `ImGuiBackendVulkanImpl.AddTexture(VkSampler, VkImageView)` → `ImTextureRef`, with a matching
   `RemoveTexture(ImTextureRef)` for cleanup (descriptor pool size 1000). Textures are created from
   raw RGBA via `RenderCore.SimpleVkTexture` + `UploadData(stagingPool, cmdBuffer, rawData, mipSizes)`;
   staging via `renderer.Allocator.CreateStagingPool(renderer.Graphics, 1)` (pattern proven in
   `KSA.BiomeMapsExporter`). `ImDrawListPtr.AddImage(ImTextureRef, pMin, pMax, …)` draws it. No
   Harmony/IL injection and no render-to-texture quad needed — straight ImGui draw-list calls.

### What we must build ourselves
- **Restore + verify the kitty binding** against the pinned headers (drifted-enum risk — see Risks).
- **A managed image decoder.** The C API hands us the image bytes **as transmitted** (possibly
  zlib-compressed, and PNG/raw-RGB/raw-RGBA per the `format` field) — it does **not** return
  decoded RGBA. We decode client-side: zlib via built-in `System.IO.Compression.ZLibStream`; PNG
  via a small managed decoder (**StbImageSharp**, pure-managed, cross-platform, shipped like Tomlyn).
- **A renderer-neutral image-placement seam** + per-image **GPU texture cache** with eviction.
- **Draw integration** in `FrameGridRenderer` (z-ordered, scroll-aware, clipped to the grid).

---

## End-to-end data flow (target)

```
shell → PTY → Surface.Write → (tick) Terminal.VTWrite        [engine stores image + placement]
                                   │
GhosttyTerminalSurface.BuildFrame ─┤
   PopulateFrame: enumerate placements via restored binding
     for each visible placement: image_id, viewport (col,row), cell span, pixel size,
       source rect, z, is_virtual; pull raw bytes (data_ptr/len)+format+compression for new ids
   → fill TerminalFrame.ImagePlacements[]  (renderer-neutral: ids + geometry + raw/decoded bytes)
                                   │
TerminalWindow.Render (render thread, OnAfterUi) ─┤
   ImageTextureCache: decode (zlib/PNG → RGBA) + SimpleVkTexture upload + ImGuiBackend.Vulkan.AddTexture
   FrameGridRenderer.Render: draw AddImage quads at canvasPos + cell*metrics, z-ordered, grid-clipped
```

Key property preserved: **no ImGui/Vulkan/engine types cross `ITerminalSurface`/`TerminalFrame`.**
The seam carries ids + geometry + CPU pixel bytes; the texture cache (ImGui/Vulkan) lives entirely
in the Display layer.

---

## Work breakdown by layer

### 1. Binding — `vendor/Ghostty.Vt` (restore + verify)
- Restore from `91fedcf^`: `git show 91fedcf^:vendor/Ghostty.Vt/src/KittyGraphics.cs` etc. Re-add
  `KittyGraphics.cs`, the `Types/KittyGraphics*` + `KittyPlacementRect`, and the
  `Enums/KittyImage{Format,Compression}` + `KittyPlacementLayer`.
- Re-add the P/Invoke block to `src/Native/NativeMethods.cs` (the `ghostty_kitty_graphics_*`
  declarations). Consider also binding the `*_get_multi` batch getters (present at pin) to cut
  per-placement P/Invoke chatter.
- **Verify every enum against the pinned C headers** before trusting it — drifted enums are the
  documented re-vendor hazard (`vendor/Ghostty.Vt/README.md`). Cross-check
  `KittyImageFormat`/`KittyImageCompression`/`KittyPlacementLayer` and the `*_get` data-index enums
  against `git show 7092b39…:src/terminal/c/kitty_graphics.zig`.
- Expose an accessor off `Terminal` (e.g. `Terminal.KittyGraphics` returning a
  `KittyGraphicsAccessor` over the active screen), mirroring how `Terminal.TrackGridRef` /
  selection are surfaced. Add image-bytes access: `KittyImage` already exposes
  `Format`/`Compression`/`Width`/`Height`; add `data_ptr`/`data_len` reads (image data indices 7/8)
  returning a `ReadOnlySpan<byte>` valid only for the current tick (single-threaded engine access).

### 2. Seam — `purrTTY.Terminal/Rendering` + `ITerminalSurface`
- New neutral type `Rendering/ImagePlacement.cs`:
  ```csharp
  public struct ImagePlacement {
      public int ImageId;            // identity for the texture cache
      public long ContentVersion;    // bump when image bytes for this id change (re-transmit)
      public int Col, Row;           // viewport cell anchor (top-left), already scroll-resolved
      public int WidthCells, HeightCells;
      public int PixelWidth, PixelHeight;       // rendered size
      public int SrcX, SrcY, SrcW, SrcH;        // source crop in the decoded image
      public int Z;                  // <0 below text, >=0 above text
      public bool IsVirtual;         // U+10EEEE Unicode placeholder (phase 2)
  }
  ```
- New neutral type `Rendering/TerminalImage.cs` carrying decoded-or-raw CPU pixels for an id the
  frontend hasn't cached yet: `int ImageId; long ContentVersion; ImagePixelFormat Format; int
  Width, Height; byte[] Pixels;` (Format ∈ Rgba/Rgb/Png/… so the frontend can decode, or the
  backend pre-decodes to Rgba — see Decode strategy).
- Extend `TerminalFrame`: `ImagePlacement[] ImagePlacements`, `TerminalImage[] NewImages`
  (images first seen this frame, to upload), and `bool ImagesChanged`. Default empty so the
  text-only path is unaffected and zero-cost when no images exist.
- `ITerminalSurface`: no new methods required — images ride on the existing `BuildFrame()` snapshot.

### 3. Frame production — `purrTTY.Terminal/Ghostty/GhosttyTerminalSurface.cs`
- In `PopulateFrame` (or a sibling `PopulateImages`), gate on a cheap "any kitty images present"
  check to keep the common case free (e.g. skip if the accessor reports zero
  images/placements; the per-row `KittyVirtualPlaceholder` flag and `Cell.KittyPlacementId`
  already surfaced can also gate).
- Enumerate placements with `KittyGraphicsPlacementIterator` filtered by layer; for each, read
  `viewport_pos` (skip `no_value` = scrolled off / virtual), `pixel_size`, `grid_size`,
  `source_rect`, `z`. Emit one `ImagePlacement`. Because we re-enumerate every frame and the engine
  re-reports viewport positions, **scroll/scrollback/resize tracking is free** — no client-side
  tracked-grid-refs needed.
- Maintain a small set of "known image ids." For any id referenced by a placement but not yet
  known, read its bytes (`data_ptr`/`len`)+format+compression, **copy them out immediately**
  (engine owns the buffer only for this tick), and add a `TerminalImage` to `NewImages`. Track a
  `ContentVersion` so a re-transmitted id can invalidate the frontend texture (best-effort: hash
  the bytes or bump on first-unseen; document the same-id-replace edge case).
- Single-threaded discipline (gotcha 1): all of this runs inside `BuildFrame` on the tick thread.

### 4. Decode strategy — `purrTTY.Terminal` (CPU, renderer-neutral)
- Decode in the **backend** so the seam only ever carries RGBA (keeps the frontend dumb and avoids
  re-decoding per frontend). Pipeline per new image:
  1. If `compression == zlib`: inflate via `System.IO.Compression.ZLibStream` (built-in).
  2. If `format == png`: decode via **StbImageSharp** (`ImageResult.FromMemory(bytes,
     ColorComponents.RedGreenBlueAlpha)`).
  3. If `format == rgb`: expand to RGBA (insert opaque alpha).
  4. If `format == rgba`: use as-is.
- Add the StbImageSharp NuGet to `purrTTY.Terminal` and copy its DLL in `purrTTY.GameMod`'s
  `CopyCustomContent` (same mechanism as Tomlyn — see CLAUDE.md "Deploying the game mod").
- Enforce a sane max decoded size (clamp/skip oversize images) to bound memory; log once on skip.

### 5. GPU texture cache — `purrTTY.Display/Ghostty` (render thread only)
- New `ImageTextureCache` owned per `TerminalWindow` (or per controller, shared):
  `Dictionary<int imageId, Entry>` where `Entry { ImTextureRef Tex; SimpleVkTexture Vk; long
  ContentVersion; int LastUsedGeneration; }`.
- On each `TerminalWindow.Render` (already on the render thread, in `OnAfterUi`):
  - For each `frame.NewImages` (or id whose `ContentVersion` changed): create
    `SimpleVkTexture(name, renderer.Device, w, h, 1, VkFormat.R8G8B8A8UNorm, mipLevels:1,
    flags: TransferDst|Sampled)`, upload RGBA via a `StagingPool`
    (`renderer.Allocator.CreateStagingPool(renderer.Graphics, 1)`) + `UploadData(...)` + `Submit()`,
    then register `ImGuiBackend.Vulkan.AddTexture(renderer.LinearSampler, tex.ImageView)`.
  - LRU/age eviction keyed on `LastUsedGeneration` to stay well under the 1000-slot descriptor
    pool: on evict call `ImGuiBackend.Vulkan.RemoveTexture(entry.Tex)` then `entry.Vk.Dispose()`.
  - Dispose all entries on window/session close.
- `Program.GetRenderer()` for `Device`/`LinearSampler`/`Graphics`/`Allocator`. Uploads + AddTexture
  must be on the render thread — satisfied because `controller.Render()` runs in `OnAfterUi`.

### 6. Draw integration — `purrTTY.Display/Ghostty/FrameGridRenderer.cs` + `TerminalWindow.cs`
- Split image drawing into two z-bands around the existing passes:
  - **z < 0** placements: draw after background fills (Pass 1), before glyphs (Pass 2).
  - **z >= 0** placements: draw after glyphs (Pass 2), before/with decorations (Pass 3).
- For each placement: `pMin = canvasPos + (Col*cellWidth, Row*cellHeight)`,
  `pMax = pMin + (PixelWidth, PixelHeight)` (or `WidthCells*cellWidth × HeightCells*cellHeight` —
  prefer the engine's pixel size, falling back to cell span). UVs from `Src{X,Y,W,H}/imageSize`.
  Call `drawList.AddImage(cache[ImageId].Tex, pMin, pMax, uvMin, uvMax)`.
- Pass the cache + the foreground/background opacity multipliers into `Render` (extend its
  signature; it already takes opacity args). Tint with white@opacity via the `ImColor8` arg.
- Clip to the grid rect (placements partially scrolled in): rely on engine viewport coords +
  ImGui clip rect / manual UV clamp so images don't bleed over chrome.
- Keep layout untouched (gotcha 8): images are draw-list only, never affect window/grid sizing.

---

## Phasing

- **Phase 0 — spike (de-risk): DONE.** Restored binding (`KittyPlacementCursor`), enums verified
  vs pinned headers, direct binding tests drive the real native lib (`KittyGraphicsBindingTests`).
- **Phase 1 — MVP: DONE.** Classic (pinned) placements, raw RGB/RGBA/gray + PNG + zlib decode,
  GPU texture cache (`ImageTextureCache`), z-banded draw (`KittyImageRenderer`), scroll/resize via
  engine-resolved geometry, perf-HUD `img:` counter. Backend + decoder tests green; frontend
  compiles against the real KSA assemblies. **In-game visual verification still pending** (run
  `kitten icat`, `timg`, `chafa -f kitty`, `yazi`/`lf` over WSL).
- **Phase 2 — Unicode virtual placeholders (U+10EEEE):** the `is_virtual` placements that are laid
  out by placeholder cells in the grid (used by some image plugins). More involved layout; gated on
  the per-row `KittyVirtualPlaceholder` flag already surfaced.
- **Phase 3 — polish:** animation frames, robust same-id re-transmit invalidation, memory caps +
  eviction tuning, perf-HUD counters (images uploaded / drawn / cache size), opacity/blend with the
  terminal background-opacity setting.

---

## Risks & mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Drifted kitty enums in restored binding vs pinned headers | Med | Verify each enum against `git show 7092b39…:src/terminal/c/kitty_graphics.zig` before use (documented re-vendor hazard). Phase-0 test catches mismatches. |
| C API returns raw/compressed bytes, not RGBA | Med | Decode client-side: ZLibStream (built-in) + StbImageSharp (PNG). Ship the DLL like Tomlyn. |
| Descriptor pool exhaustion (1000 shared slots) | Med | LRU eviction + `RemoveTexture`; cap concurrent cached images; one texture per id, not per placement. |
| Same image id re-transmitted with new content | Low | `ContentVersion` (byte hash) invalidates the cached texture; document residual edge cases. |
| Upload must be render-thread; decode is CPU-heavy | Low | Decode in backend `BuildFrame` (tick thread); upload in Display `Render` (render thread, OnAfterUi). Clamp max image size. |
| `data_ptr` lifetime (engine owns buffer) | Low | Copy bytes out within the same tick before any further engine mutation. |
| Cross-platform decode (win/linux/mac mod) | Low | StbImageSharp is pure-managed, no native deps. zlib is BCL. |
| Pin bump changes the C API/layout | Low | All gated behind the binding; `RawCellLayout.Validate` precedent + the Phase-0 test guard a bump. |

No blocker was identified in any of the three layers.

---

## Verification

- **Backend integration test** (`purrTTY.Terminal.Tests`, NUnit): feed a minimal kitty APC
  transmit+place sequence with a tiny **raw RGBA** payload (`\x1b_Ga=T,f=32,s=2,v=2,...;<base64>\x1b\\`),
  `BuildFrame()`, then assert `frame.ImagePlacements` has one entry with the expected
  cell/pixel geometry and that `NewImages` carries the decoded 2×2 RGBA. Add a zlib-compressed and
  a PNG variant. (Mirrors the existing OSC/DSR integration-test style; engine VT behavior itself is
  trusted, per CLAUDE.md.)
- **Decode unit tests:** zlib round-trip; RGB→RGBA expansion; a known small PNG → expected pixels.
- **Texture-cache logic test** (headless, no GPU): eviction order + version invalidation against a
  fake registrar, keeping GPU calls behind an interface so the cache policy is testable without
  Vulkan (mirrors the Display.Tests "pure-logic" approach).
- **In-game manual:** launch KSA, open the terminal on a WSL shell, run `kitten icat <png>`,
  `timg <img>`, `chafa -f kitty <img>`, and a `yazi`/`lf` image preview. Verify: image appears at
  the cursor, scrolls with content, survives resize, disappears on `clear`/delete, and that text-only
  workloads show no regression (perf HUD: zero image draws, unchanged draw-call counts).

---

## Touch list (primary files)

- `vendor/Ghostty.Vt/src/KittyGraphics.cs` (+ `Types/KittyGraphics*`, `Types/KittyPlacementRect.cs`,
  `Enums/KittyImage*`, `Enums/KittyPlacementLayer.cs`) — restored from `91fedcf^`, enums re-verified
- `vendor/Ghostty.Vt/src/Native/NativeMethods.cs` — `ghostty_kitty_graphics_*` P/Invoke block
- `vendor/Ghostty.Vt/src/Terminal.Selection.cs` (or new `Terminal.KittyGraphics.cs`) — accessor
- `purrTTY.Terminal/Rendering/ImagePlacement.cs`, `Rendering/TerminalImage.cs` (new)
- `purrTTY.Terminal/Rendering/TerminalFrame.cs` — `ImagePlacements`/`NewImages`/`ImagesChanged`
- `purrTTY.Terminal/Ghostty/GhosttyTerminalSurface.cs` — placement enumeration + image extraction + decode
- `purrTTY.Terminal/*.csproj` + `purrTTY.GameMod` `CopyCustomContent` — StbImageSharp dependency/ship
- `purrTTY.Display/Ghostty/ImageTextureCache.cs` (new) — SimpleVkTexture + AddTexture/RemoveTexture
- `purrTTY.Display/Ghostty/FrameGridRenderer.cs` — z-ordered `AddImage` passes
- `purrTTY.Display/Ghostty/TerminalWindow.cs` — own/drive the cache; thread cache into `Render`
- Update `CLAUDE.md` (engine binding surface, gotchas) and `vendor/Ghostty.Vt/README.md`
  (un-prune kitty graphics) on completion, per the Instruction Maintenance Mandate.
