# SIXEL_PLAN.md — Sixel graphics in the purrTTY in-game terminal

## Context

purrTTY now renders **kitty graphics** (see `KITTY_PLAN.md`): the libghostty-vt engine parses and
stores kitty images/placements inside `Terminal.VTWrite`, purrtty enumerates visible placements
each tick, decodes the bytes to RGBA, uploads them to GPU textures, and composites them over the
cell grid. That pipeline — seam types (`ImagePlacement`/`TerminalImage` on `TerminalFrame`),
`KittyImageDecoder`, `ImageTextureCache`, `KittyImageRenderer` — is renderer-neutral and
**id-keyed**, i.e. it doesn't care where an image came from.

This document asks: can we also show **sixel** graphics (`DCS <params> q … ST`), the older raster
protocol still emitted by `img2sixel`/libsixel, `chafa -f sixel`, `timg -p sixel`, `lsix`, mpv
`--vo=sixel`, notcurses, and the sixel-preview paths in `yazi`/`lf`/`ranger`?

This is a feasibility-validated build plan grounded in the ghostty source
(`C:\Users\Alex\repos\github\ghostty`), the vendored `Ghostty.Vt` binding, the existing kitty
implementation, and the KSA ImGui/Vulkan stack.

---

## The decisive finding — the engine does NOT support sixel

Unlike kitty graphics, **libghostty-vt has no sixel support whatsoever.** This was verified against
the ghostty source:

1. **The DCS dispatcher omits sixel.** Sixel is `DCS <params> q` — final byte `q`, **no
   intermediates**. ghostty's `src/terminal/dcs.zig` `tryHook()` switches on the intermediate
   count: with zero intermediates it only handles `p` (tmux control mode) and otherwise returns
   `null` (unknown/ignored). With one intermediate it handles `+ q` (XTGETTCAP) and `$ q`
   (DECRQSS). **A bare `q` falls straight through to "unknown" — the sixel payload is parsed by the
   generic DCS state machine and silently discarded.** No image is produced, no error, no effect.

2. **`sixel` exists only as a capability enum value, never used.** `src/terminal/device_attributes.zig`
   defines `Feature.sixel = 4`, but the default DA1 response advertises only `ansi_color` (`;22`),
   and nothing in the engine ever emits or acts on `Feature.sixel`.

3. **No parser, decoder, storage, or C API.** A full-tree search of `src/terminal` finds zero sixel
   implementation. The only graphics storage/decoding/C-API surface in the engine is **kitty**
   (`src/terminal/kitty/*`, `src/terminal/c/kitty_graphics.zig`, APC-framed). Sixel was **removed
   from ghostty's TODO list** (commit `41d64d8`, "doc: remove sixels from the TODO list",
   ref discussion #2496) — it is a deliberate non-goal upstream, not "coming soon."

**Consequence:** the kitty "capability triangle" (engine does the hard part → restore a binding →
draw textures) **does not apply to sixel.** There is nothing in the engine to bind to. We must
either (a) add sixel to the native engine ourselves, or (b) handle sixel entirely in the managed
layer, *before* the bytes reach the engine.

---

## Feasibility verdict — YELLOW (feasible, via managed transcode; not a binding restore)

Sixel is achievable **without a native rebuild and without forking the engine**, by intercepting
sixel sequences in purrtty's managed write path and **transcoding them into equivalent kitty
graphics commands** that the engine already fully supports. This reuses ~90% of the kitty work
(engine storage, placement-geometry resolution, scroll/resize/scrollback/delete lifecycle, the
neutral seam, the texture cache, and the renderer) and confines the new code to three small,
well-bounded, pure-managed pieces. The cost is one genuinely new component we don't have today — a
**managed sixel raster decoder** — plus careful stream framing and cursor-semantics calibration.

It is **not** as clean as kitty (where the engine did everything). It is a real, bounded project
with one moderate-complexity decoder and some fiddly stream/cursor edge cases. Hence YELLOW, not
GREEN.

---

## Recommended architecture — transcode sixel → kitty in the write path

```
shell → PTY → Surface.Write → inbox → (tick) BuildFrame:
   span = next ≤1 MiB of buffered PTY bytes
   ┌─────────────────────────────────────────────────────────────────────────┐
   │ SixelTranscoder.Process(span):    [NEW — stateful, runs on tick thread]   │
   │   • pass non-sixel bytes through unchanged                                │
   │   • on `ESC P … q`: buffer the DCS body until ST (`ESC \`)                 │
   │   • decode sixel body → RGBA + width/height   [NEW SixelDecoder]          │
   │   • emit synthesized kitty APC in its place:                              │
   │       ESC _ G a=T,f=32,s=W,v=H,i=<id>,q=2[,m=1…] ; <base64 RGBA> ESC \     │
   │   • (optional) append cursor-normalizing bytes to match sixel end-pos     │
   └─────────────────────────────────────────────────────────────────────────┘
                          │ transcoded bytes
   _terminal.VTWrite(transcoded)   → engine stores it as a KITTY image+placement
                          │
   …then the EXISTING kitty path runs unchanged:
   GhosttyTerminalSurface.PopulateImages → TerminalFrame.{ImagePlacements,NewImages}
   ImageTextureCache.Upload + KittyImageRenderer.Draw  (z-banded, scroll-aware, clipped)
```

The engine never knows the image was sixel — it sees a kitty `f=32` (RGBA) transmit-and-display.
**Placement geometry, viewport resolution, scrolling, resize, scrollback pruning, `clear`/ED
removal, and z-ordering are therefore handled by the engine for free**, exactly as for native kitty
images. No `ImagePlacement`/`TerminalImage`/`TerminalFrame`/texture-cache/renderer changes are
required for Phase 1.

### Why transcode-to-kitty beats the alternatives

| Approach | Engine changes | New managed code | Placement lifecycle (scroll/resize/scrollback/clear) | Verdict |
|---|---|---|---|---|
| **Transcode sixel→kitty (recommended)** | none (pin preserved) | stream extractor + sixel decoder + kitty encoder | **free** (engine treats it as a kitty image) | **Chosen** |
| Parallel managed sixel pipeline (own seam/placement tracking) | none | extractor + decoder + **own** placement model with tracked grid-ref anchors, scroll resolution, scrollback-prune removal, ED/`clear` handling, new seam/renderer paths | must re-solve all of it by hand (fragile — this is exactly the hard part gotcha 17 / the engine give us for free) | Rejected — much more code, worse correctness |
| Fork the native engine: implement sixel in Zig | **forks** the pinned lib; new DCS hook + decoder + image storage + C API; rebuild 3 RIDs; maintain divergence across every pin bump; re-verify `RawCellLayout` | a restored binding (like kitty) | free | Rejected — violates the project's central *pinned-not-forked* discipline (CLAUDE.md, `vendor/Ghostty.Vt/README.md`); upstream deliberately dropped sixel, so the divergence is permanent and unupstreamable; highest maintenance burden |

The transcode approach is the only one that adds sixel **without touching the native lib** and
**without re-implementing placement lifecycle**.

---

## What we must build (and what we get for free)

**Build (all pure-managed, cross-platform, in `purrTTY.Terminal`):**

1. **`SixelTranscoder`** — a stateful scanner over the PTY byte stream that frames `ESC P … q … ST`
   DCS sixel sequences (handling split across the inbox catch-up chunking and across ticks, like
   `OscSidecar` frames OSC), passes everything else through untouched, and substitutes a kitty APC
   for each completed sixel. Modeled on `OscSidecar`'s state machine, but it **rewrites** the stream
   instead of teeing it read-only.
2. **`SixelDecoder`** — the one genuinely new capability: decode sixel raster data → packed RGBA +
   width/height. StbImageSharp cannot do this (PNG/JPG/BMP/TGA/GIF/PSD/HDR/PIC only); sixel needs a
   dedicated decoder. The format is well-specified and compact (see Decoder section).
3. **`KittyTransmitEncoder`** — RGBA + W×H → a kitty `a=T,f=32` transmit-and-display APC, base64
   payload, chunked at ≤4096 bytes with `m=1`/`m=0`. ~100 lines.
4. **(Phase 2) capability advertisement** — inject `;4` (sixel) into the engine's DA1 reply and
   synthesize an XTSMGRAPHICS response so auto-detecting tools choose sixel. Confined to the
   `PtyReply` flush path.

**Free (reused from the kitty implementation, unchanged):**

- Engine image storage + placement geometry/viewport/scroll/resize/scrollback/delete lifecycle.
- `KittyPlacementCursor` enumeration (`vendor/Ghostty.Vt/src/KittyGraphics.cs`).
- Seam types `ImagePlacement` / `TerminalImage` / `TerminalFrame.{ImagePlacements,NewImages}`.
- `GhosttyTerminalSurface.PopulateImages` / `DecodeImage` (sixel images arrive as `f=32` RGBA →
  the existing `KittyImageDecoder.Rgba` path decodes them, or they're already RGBA pre-decode).
- `ImageTextureCache` (GPU upload, `AddTexture`/`RemoveTexture`, LRU eviction, descriptor budget).
- `KittyImageRenderer` (z-banded `AddImage`, cell-metric sizing, grid clip).
- `StbImageSharp` dependency wiring + native-lib bundling (no change).

---

## Work breakdown by layer

### 1. Stream transcoder — `purrTTY.Terminal/Ghostty/SixelTranscoder.cs` (new)

Injection point: `GhosttyTerminalSurface.BuildFrame`, the existing
`_osc.Feed(span); _terminal.VTWrite(span);` at the drain site (around
`GhosttyTerminalSurface.cs:594`). Replace the direct `VTWrite(span)` with:

```csharp
_osc.Feed(span);                              // OSC tee — sees the original stream, ignores DCS
ReadOnlySpan<byte> toEngine = _sixel is null
    ? span
    : _sixel.Process(span);                    // returns the original span when no sixel present
_terminal.VTWrite(toEngine);
```

State machine (mirrors `OscSidecar`, but emits a rewritten buffer):

- **Ground:** vectorized `IndexOf(ESC)` fast-path; copy through to the output buffer. The common
  (no-sixel) case returns the input span by reference — **zero copy, zero alloc** — so text-only
  and kitty-only workloads pay nothing.
- On `ESC P`: enter DCS-collect. Buffer params + body bytes. We only care that the **final byte is
  `q` with no `$`/`+` intermediate** → it's sixel; any other DCS (`$q` DECRQSS, `+q` XTGETTCAP,
  `1000p` tmux) is passed through verbatim so the engine still handles it.
- Collect the sixel body until **ST** (`ESC \`) or **BEL**; honor **CAN/SUB abort** (same rationale
  as `OscSidecar` — the inbox-overflow heal sequence is CAN+ST, gotcha 18).
- On terminator: decode (Decoder section) and append the synthesized kitty APC to the output
  buffer. On decode failure or oversize: drop the sequence silently (logged once) and continue.
- **Split across ticks:** a sixel can exceed `MaxBytesPerTick` (1 MiB) or land on a chunk boundary;
  the transcoder keeps its partial-DCS buffer across `Process` calls, exactly as `OscSidecar` keeps
  `_payload`. Cap the buffer (e.g. 16 MiB) and abandon→pass-through on overflow so a malformed
  unterminated DCS can't grow without bound.
- **Suppress vs. pass-through:** the raw sixel DCS bytes are **not** forwarded to the engine (it
  would ignore them anyway); only the synthesized kitty APC is. Pre-sixel and post-sixel bytes are
  forwarded in order, so the engine's cursor is correctly positioned when the kitty `a=T` lands.

Output buffering: reuse a growable `byte[]` scratch on the surface (the engine consumes
synchronously inside the same `VTWrite`, so a single reusable buffer is safe — no lifetime escape).

### 2. Sixel decoder — `purrTTY.Terminal/Ghostty/SixelDecoder.cs` (new)

Sixel is a band-based raster format; a complete, correct decoder is ~300–500 lines. Inputs: the DCS
body after `q`; outputs: width, height, `byte[]` RGBA (or `null` on failure/oversize). Elements to
handle:

- **DCS macro/aspect params** before `q` (`P1;P2;P3 q`): `P2` = background-handling (0/2 = pixels
  outside the image are transparent vs. opaque-background — map to RGBA alpha). Aspect params are
  effectively ignored by modern emulators (1:1).
- **Raster attributes** `" Pan;Pad;Ph;Pv` (DECGRA): pixel aspect + explicit `Ph×Pv` image size —
  use it to pre-size the canvas when present (libsixel always emits it).
- **Color introducer** `#Pc;Pu;Px;Py;Pz`: define color register `Pc`. `Pu=2` → RGB with components
  in **0–100 percent** (scale ×255/100); `Pu=1` → HLS. `#Pc` alone selects the active register.
- **Sixel data bytes** `?`..`~` (0x3F–0x7E): each encodes a vertical strip of **6 pixels** (bit 0 =
  top). Set pixels of the current band to the active color.
- **Repeat introducer** `!Pn <sixel>`: emit the next sixel byte `Pn` times (RLE).
- **Graphics carriage return** `$`: return to the band's left edge (overlay next color pass on the
  same 6-row band — sixel composites multiple color passes per band).
- **Graphics newline** `-`: advance to the next 6-row band.
- **Canvas growth:** width = max column reached; height = bands×6 (or `Pv` from raster attrs). Bound
  total pixels (reuse the kitty `KittyImageDecoder.MaxPixels = 4096×4096` guard); reject/drop
  oversize. Default palette = the VT340 16-color palette for registers referenced before definition.

Output is straight RGBA8888 (top-left origin), feeding the kitty `f=32` transmit. Transparent
"unset" pixels get `a=0` so they composite correctly over the cell grid.

> Build-vs-buy note: there is no de-facto-standard pure-managed sixel decoder on NuGet that's worth
> a supply-chain dependency. Writing a compact decoder is the safe, cross-platform choice (the
> format is small and fully specified) and keeps parity with the "pure-managed, no native deps"
> property of the kitty decode path. Reuse `KittyImageDecoder`'s size/truncation guard discipline.

### 3. Kitty transmit encoder — `purrTTY.Terminal/Ghostty/KittyTransmitEncoder.cs` (new)

`(byte[] rgba, int w, int h, uint imageId) → byte[]` producing:

```
ESC _ G a=T,f=32,s=<w>,v=<h>,i=<id>,q=2,m=1 ; <base64 chunk 1> ESC \
ESC _ G m=1 ; <base64 chunk 2> ESC \
…
ESC _ G m=0 ; <base64 chunk N> ESC \
```

- `a=T` transmit-and-display (creates image **and** a placement at the cursor in one shot).
- `f=32` = RGBA (matches our decoded buffer; the engine stores it and our existing
  `KittyImageDecoder` round-trips `f=32` — proven by the kitty path already decoding RGBA).
- `q=2` suppress the engine's kitty OK/`EAGAIN` responses (we synthesized this; the app must not see
  a kitty reply it never asked for).
- Base64 payload chunked at ≤4096 bytes with the `m=1`(more)/`m=0`(last) continuation, per the kitty
  protocol; the engine reassembles.
- **Image IDs:** allocate from a high reserved range (e.g. `0x40000000 +` a per-surface counter) so
  synthesized sixel ids never collide with ids a co-resident kitty app chooses. Sixel has no image
  identity, so each sixel gets a fresh id (the texture cache LRU-evicts old ones; optionally emit a
  kitty delete `a=d,i=<id>` for ids whose placements have scrolled out, to bound engine image
  memory — Phase 3 polish).

### 4. Cursor / scroll semantics calibration

Sixel (in the now-standard scrolling mode) draws at the cursor, scrolls the page as the image
extends past the bottom, and leaves the cursor **below** the image (column behavior is
terminal-dependent; xterm leaves it at the start of the line after the image when sixel scrolling +
`DECSDM` defaults apply). Kitty `a=T` defaults to **moving the cursor to the first cell to the
right of, and below, the image** unless `C=1` (do-not-move) is set.

Because we control the injected bytes, normalize the end position to match sixel by appending an
explicit cursor move after the APC if needed (e.g. CR + the right number of LFs, or a CUP), rather
than relying on kitty's default. This is a **calibration task** to tune against real tools
(`img2sixel`, `chafa -f sixel`, `lsix`) — flagged as the main behavioral risk. Also handle
**DECSDM** (mode `?80`, "sixel scrolling"): when an app sets sixel display mode, adjust whether the
image anchors at cursor vs. top-left. Most modern tools assume scrolling mode on; start there.

### 5. Capability advertisement — `PtyReply` path (Phase 2)

Apps that auto-detect (rather than being forced with `-f sixel`) probe via:

- **DA1** (`CSI c`) → look for `;4` in `CSI ? … c`. The engine omits it. Intercept the reply in the
  `_replies`/`PtyReply` flush (`GhosttyTerminalSurface.cs:602–606`): scan for the DA1 response and
  splice `;4` into the feature list before invoking `PtyReply`.
- **XTSMGRAPHICS** (`CSI ? Pi ; Pa ; Pv S`) → graphics geometry/color-register query. The engine
  won't answer; synthesize a plausible reply (report supported, with a max geometry tied to the
  grid pixel size and ≥256 color registers).
- `$TERM`/terminfo cannot easily claim sixel without shipping a custom terminfo entry; document this
  gap. Many tools fall back to DA1/XTSMGRAPHICS or a forcing flag, so terminfo is not required for
  Phase 2.

Note the interaction: if both kitty and sixel are advertised, kitty-capable tools (icat, chafa,
timg, yazi) **prefer kitty** — which already works natively. Sixel mainly unlocks **sixel-only**
tools and explicit `-f sixel` use. Phase 1 (forced-sixel tools) delivers the bulk of the value with
no reply-path changes; Phase 2 adds auto-detection.

---

## Phasing

- **Phase 0 — decoder spike (de-risk the one new capability).** Stand up `SixelDecoder` + unit
  tests against known sixel fixtures (`img2sixel` output of tiny images, hand-authored bands,
  RLE/`$`/`-`/color-register cases). No engine wiring yet. This is the only piece with real
  algorithmic risk; prove it first.
- **Phase 1 — MVP transcode + render.** `SixelTranscoder` + `KittyTransmitEncoder` wired at the
  `BuildFrame` drain site; sixel images appear via the existing kitty render path. Verify with
  forced-sixel tools (`img2sixel x.png`, `chafa -f sixel`, `timg -p sixel`, `lsix`). Calibrate
  cursor/scroll end-position. Backend integration test: feed a tiny sixel DCS, assert a kitty
  placement + decoded RGBA appear in the frame.
- **Phase 2 — capability advertisement.** DA1 `;4` injection + XTSMGRAPHICS reply so auto-detecting
  tools (`chafa` without `-f`, `yazi`/`lf` sixel previews, mpv `--vo=sixel`) pick sixel.
- **Phase 3 — polish.** Proactive kitty-delete of scrolled-off synthesized ids (bound engine image
  memory), HLS color conversion, DECSDM edge cases, decode offload for very large sixels if tick
  cost shows in the perf HUD, palette-selection edge cases, animation/streamed-sixel handling.

---

## Risks & mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| **Sixel decoder correctness** (the one new capability) | **High** | Phase-0 spike + fixture-based unit tests before any wiring; well-specified format; bound by `MaxPixels`. The single biggest item. |
| Cursor/scroll end-position mismatch vs. real sixel | Med | We control injected bytes — append explicit cursor-normalizing moves; calibrate against `img2sixel`/`chafa -f sixel`/`lsix`; handle DECSDM. |
| Sixel split across inbox catch-up chunks / ticks | Med | Stateful transcoder buffers partial DCS across `Process` calls (proven pattern: `OscSidecar`); cap buffer + abandon on overflow (CAN+ST heal honored). |
| Auto-detecting tools don't emit sixel (no DA1 `;4`) | Med | Phase 1 targets forced-sixel tools (the common case); Phase 2 adds DA1/XTSMGRAPHICS advertisement. |
| Decode cost on the tick thread (large sixels) | Med | `MaxPixels` clamp + once-logged drop; offload decode to a task in Phase 3 if the perf HUD shows it. The MaxBytesPerTick chunking already prevents one giant `VTWrite` from stalling render. |
| Synthesized kitty ids collide with a real kitty app's ids | Low | Reserve a high id range (`0x40000000+`) for transcoded images. |
| Engine image memory grows (fresh id per sixel) | Low | Texture cache LRU-evicts on the GPU side; Phase 3 emits kitty `a=d` for scrolled-off ids; engine has its own image-storage limits. |
| HLS / percent-RGB / default-palette corner cases | Low | Implement RGB-percent + VT340 default palette first (what libsixel emits); HLS in Phase 3. |
| `q=2` not honored / app sees stray kitty reply | Low | `q=2` suppresses kitty responses; verify in the integration test that no `\x1b_G…` reply reaches `PtyReply`. |
| Pin bump changes kitty transmit/`f=32` semantics | Low | Same guard as kitty (`RawCellLayout.Validate` + the kitty binding tests); the transcoder only uses the **public kitty protocol**, not new native symbols, so no binding surface is added. |

**No native rebuild and no new P/Invoke surface is required** — the transcoder speaks the public
kitty protocol the engine already implements.

---

## Verification

- **Decoder unit tests** (`purrTTY.Terminal.Tests`, NUnit): hand-authored sixel bodies — single
  band, multi-band (`-`), RLE (`!Pn`), graphics-CR color overlay (`$`), RGB-percent color register
  (`#0;2;100;0;0`), raster-attrs sizing (`"1;1;W;H`), transparent background (`P2=1`), oversize
  reject, truncated/garbage reject. Assert width/height + spot-checked RGBA pixels.
- **Transcoder unit tests:** sixel split across two `Process` calls reassembles; a non-sixel DCS
  (`$q`/`+q`) passes through byte-for-byte; CAN/SUB mid-sixel abandons; the no-sixel fast path
  returns the input span unchanged (identity, zero-copy).
- **Backend integration test** (mirrors `GhosttyKittyGraphicsTests`): write a minimal sixel DCS for
  a 2×2 image, `BuildFrame()`, assert `frame.ImagePlacements` has one entry with the expected
  cell/pixel geometry and `NewImages` carries the decoded RGBA — i.e. the transcode→engine→kitty
  round-trip works — and that **no** kitty reply leaked to `PtyReply` (`q=2`).
- **Capability test** (Phase 2): a DA1 request yields a reply containing `;4`; XTSMGRAPHICS query
  yields a synthesized reply.
- **In-game manual:** WSL shell → `img2sixel <png>`, `chafa -f sixel <img>`, `timg -p sixel <img>`,
  `lsix <dir>`, and (Phase 2) `chafa` auto-detect + a `yazi`/`lf` sixel preview. Verify: image
  appears at the cursor, scrolls with content, survives resize, disappears on `clear`, and that
  text-only / kitty workloads show **no regression** (perf HUD: unchanged when no sixel present;
  `img:` counter ticks for sixel images just like kitty).

---

## Touch list (primary files)

**New (all `purrTTY.Terminal`, pure-managed, cross-platform):**
- `purrTTY.Terminal/Ghostty/SixelDecoder.cs` — sixel raster → RGBA (the one new capability)
- `purrTTY.Terminal/Ghostty/SixelTranscoder.cs` — stateful DCS framing + stream rewrite (models `OscSidecar`)
- `purrTTY.Terminal/Ghostty/KittyTransmitEncoder.cs` — RGBA → chunked kitty `a=T,f=32` APC

**Modified:**
- `purrTTY.Terminal/Ghostty/GhosttyTerminalSurface.cs` — instantiate the transcoder; run it between
  `_osc.Feed` and `_terminal.VTWrite` at the drain site (`~:594`); (Phase 2) splice `;4` into the
  DA1 reply + synthesize XTSMGRAPHICS in the `PtyReply` flush (`~:602`)
- Tests: `purrTTY.Terminal.Tests/SixelDecoderTests.cs`, `SixelTranscoderTests.cs`,
  `GhosttySixelGraphicsTests.cs`

**Unchanged (reused from kitty — the payoff of transcode-to-kitty):**
- `vendor/Ghostty.Vt/*` (no native rebuild, no new P/Invoke) — engine already does kitty
- `purrTTY.Terminal/Rendering/{ImagePlacement,TerminalImage,TerminalFrame}.cs`
- `purrTTY.Terminal/Ghostty/{GhosttyTerminalSurface.PopulateImages,KittyImageDecoder}.cs`
- `purrTTY.Display/Ghostty/{ImageTextureCache,KittyImageRenderer}.cs` + `TerminalWindow.Render`
- `StbImageSharp` / native-lib bundling in `purrTTY.GameMod`

**Docs (on completion, per the Instruction Maintenance Mandate):** add a sixel gotcha to
`CLAUDE.md` (the transcode-to-kitty seam; sidecar-style framing; the engine has no native sixel) and
note in `vendor/Ghostty.Vt/README.md` that sixel is handled in the managed layer, **not** the
pinned engine.

---

## Bottom line

Sixel is **feasible but not free**. The engine gives us nothing here (the opposite of kitty), so the
win comes from **not building a second graphics pipeline**: transcode sixel into the kitty commands
the engine already implements, and the existing storage/placement/render stack carries it the rest
of the way. The real work is one well-specified managed decoder plus careful stream framing and
cursor calibration — bounded, pure-managed, cross-platform, and **with the pinned native lib left
untouched**. Recommend proceeding with the Phase-0 decoder spike to retire the only high risk before
committing to the wiring.
