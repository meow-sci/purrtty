# Direct-Vulkan Terminal Renderer — Build Plan (if greenlit)

> Status: committed phased plan, **conditional on go-ahead** · Date: 2026-06-07
> Read [`LIBGHOSTTY_VULKAN_FEASIBILITY.md`](./LIBGHOSTTY_VULKAN_FEASIBILITY.md) first. Depends on the emulator swap in [`LIBGHOSTTY_ANALYSIS.md`](./LIBGHOSTTY_ANALYSIS.md).
> Chosen shape: **hybrid** — a Vulkan instanced-glyph grid rendered to an **offscreen `VkImage`**, presented via `ImGui.Image()` inside the existing terminal window. All ImGui chrome (tabs, menus, settings, selection UI) is retained. An optional later phase adds a true composite-pass overlay.

---

## 1. Why this shape

The feasibility doc establishes: (a) the instanced grid pipeline is easy and a perfect fit for `RenderState`; (b) KSA exposes device/queue/allocator but **no per-frame command buffer**, so a mod cannot natively record into KSA's composite pass without Harmony-patching private internals; (c) purrtty's UI is mostly ImGui chrome. The offscreen→`ImGui.Image()` hybrid gets the rendering-quality upside with none of the frame-hook fragility, and is resilient to KSA updates. The true-overlay path is isolated to an **optional** final phase for the transparency use-case only.

---

## 2. Prerequisites (hard dependencies)

1. **Emulator swap landed** (`LIBGHOSTTY_ANALYSIS.md`): libghostty is the engine, the binding is vendored/owned, and the backend produces a renderer-neutral `TerminalFrame`.
2. **`TerminalFrame` / `ITerminalSurface` seam** — the Vulkan frontend consumes `TerminalFrame` and drives `ITerminalSurface` (same contract as the ImGui frontend; no `Ghostty.Vt`/ImGui types).
3. **Selection wired in the backend** (binding selection extension + per-row ranges in `TerminalFrame`) — needed for Phase E highlight/copy.
4. **Native loader** working in KSA's plugin ALC (already required by the swap).
5. Decision on glyph atlas approach (default: **pre-baked monospace atlas** from `TerminalFonts/`; see Phase A).

---

## 3. Phases

### Phase A — Glyph atlas & metrics
- Build a fixed-cell **monospace atlas** from the shipped terminal fonts: rasterize the needed coverage (ASCII/Latin, box-drawing `U+2500–257F`, block elements, powerline/Nerd-Font PUA ranges — mirror `GhostlingDotNet.Renderer.GlyphRanges`), pack into a single texture, emit a `codepoint → UV rect` table + cell advance/line-height.
  - Rasterization: offline bake step committed as an asset, **or** a managed/stb_truetype rasterizer at load (decision per feasibility §4). Start with an offline-baked atlas to de-risk.
- Define cell metrics to match the ImGui path (advance width, line height, baseline) so switching renderers doesn't reflow the grid.
- **Deliverable:** `purrTTY.Display/Rendering/Vulkan/GlyphAtlas.cs` (+ baked atlas asset) and a metrics struct.
- **Verify:** dump atlas to PNG; assert coverage vs the codepoints emitted by `GameConsoleShell` output (reuse the GhostlingDotNet "missing codepoints" diagnostic idea).

### Phase B — Offscreen target + instanced pipeline
- Create, from `IGlobalRenderSystem.DeviceContext`: an offscreen color `VkImage` (terminal-window-sized, resizable), its own `VkRenderPass` + framebuffer, a command buffer, and sync (fence/semaphore).
- Upload the atlas `VkImage` (staging buffer → `CopyBufferToImage` → barrier to `ShaderReadOnlyOptimal`), modeled on `ImGuiBackendVulkanImpl.UpdateTexture`.
- Build the graphics pipeline (model after `RenderCore.Pipelines/SimpleGraphicsPipeline` + the ImGui backend): instanced vertex input `{ivec2 cellXY, uint glyphIndex, uint fgRGBA, uint bgRGBA, uint flags}`, atlas sampler descriptor, push constants `{cellSize, viewportOffset, gridDims}`.
- Shaders: author GLSL (`Terminal.vert`/`Terminal.frag`) and **embed as SPIR-V `uint[]`** (repo precedent), or compile via `Brutal.ShaderCompiler`. Fragment: `a = sample(atlas).r; out = mix(bg, fg, a)` + underline/strike from flags.
- **Deliverable:** `purrTTY.Display/Rendering/Vulkan/{TerminalOffscreenTarget.cs, TerminalPipeline.cs, Terminal.vert, Terminal.frag}`.
- **Verify:** render a hardcoded test grid to the offscreen image; save to PNG; confirm glyphs/colors.

### Phase C — Per-frame instance upload from `TerminalFrame`
- Each changed frame (gate on `TerminalFrame.Generation`): walk `FrameRow.cells`; pack instances `{cellXY, glyphIndex(from Grapheme), fgRGBA/bgRGBA, flags(Inverse/Bold/Underline/Strike)}`; skip `CellWidth.Spacer*`; handle `Wide` as 2-column advance.
- Upload to a persistent instance buffer (triple-buffered to avoid in-flight hazards); record the draw into the offscreen command buffer; submit.
- **Deliverable:** `purrTTY.Display.Vulkan/TerminalInstanceBuilder.cs` + frame loop wiring.
- **Verify:** live `GameConsoleShell` output renders to the offscreen image; matches the ImGui renderer pixel-approximately.

### Phase D — Composite via `ImGui.Image()` + input parity
- Register the offscreen `VkImage` as an ImGui texture; in `[StarMapAfterGui]`, draw it with `ImGui.Image(...)` filling the terminal window content region (replacing the `TerminalGridRenderer` draw-list path; chrome/menus/tabs/settings unchanged).
- Keep input capture in ImGui (the window still owns focus/mouse/keyboard); the grid is just a texture now.
- Resize: when the ImGui window resizes, recompute cols/rows, `Terminal.Resize`, and reallocate the offscreen target + grid metrics.
- **Deliverable:** integration in `purrTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs` (Vulkan path behind a feature flag), offscreen resize handling.
- **Verify:** end-to-end in KSA — output, colors/themes, resize, DPI scaling, cursor; compare against the ImGui renderer side by side via the flag.

### Phase E — Cursor, selection, scrollbar quads
- Cursor: extra quad at `RenderState.CursorViewportX/Y`, shape from `CursorStyle`, blink from `CursorBlinking`.
- Selection highlight: quads from `FrameRow.SelectionRange` (backed by the backend's selection support). Until that's wired, keep selection highlight in the ImGui overlay on top of the `ImGui.Image`.
- Scrollbar: keep as ImGui chrome driven by `Terminal.Scrollbar` (no need to render in Vulkan).
- **Verify:** cursor styles/blink, drag-select highlight, copy still works (text from `selection_format`).

### Phase F (optional) — True composite-pass overlay
- Only if terminal transparency over the 3D scene is required. Harmony-patch `Program.RenderMain`/`RenderEditor` to obtain the composite command buffer + `VkRenderPass` around `ImGuiBackend.Vulkan.RenderDrawData(...)`; build a pipeline compatible with that pass (format + sample count); record the grid draws there instead of (or in addition to) the offscreen path.
- **Risks:** private/obfuscated internals; render-pass compatibility; breakage on KSA updates. Gate behind a flag; fall back to the offscreen path on any failure.
- **Verify:** transparency over the scene; robustness across a KSA update.

### Phase G — Parity, performance, polish
- A/B vs the ImGui renderer: glyph fidelity (box-drawing edge-join, powerline, wide/CJK, emoji), color/theme correctness, cursor, selection, resize/reflow.
- Performance: large grids and output floods; confirm the Vulkan path beats per-glyph `AddText`; verify no main-thread stalls (instance build gated on dirty; native `Terminal` single-threaded).
- Mod load/unload: all Vulkan resources (image, buffers, pipeline, descriptor pool, atlas) released cleanly on `[StarMapUnload]`.

---

## 4. File map (new)
```
purrTTY.Display/Rendering/Vulkan/
  GlyphAtlas.cs                 // atlas + UV table + metrics (Phase A)
  TerminalOffscreenTarget.cs    // VkImage + render pass + framebuffer + sync (B,D)
  TerminalPipeline.cs           // instanced pipeline + descriptors + push constants (B)
  TerminalInstanceBuilder.cs    // RenderState → instance buffer (C)
  Terminal.vert / Terminal.frag // GLSL → embedded SPIR-V (B)
  VulkanTerminalRenderer.cs     // orchestrates B–E; presents via ImGui.Image (D)
purrTTY.GameMod/                // (optional) Harmony patch for composite-pass overlay (F)
```
Consumes the `IGhosttyRenderSource` seam from the emulator swap. Feature-flagged alongside the ImGui renderer for safe rollout and A/B.

---

## 5. Risks (renderer-specific; see feasibility §8 for detail)
- **No truetype rasterizer in BRUTAL** → atlas bake/ship strategy (Phase A); coverage gaps for CJK/emoji.
- **DPI / cell-metric parity** with the ImGui path (Phase A/D).
- **Offscreen target lifecycle & sync** (triple-buffer instances; fence on resize).
- **Phase F fragility** — private KSA internals, render-pass compatibility, KSA-update breakage; keep optional + flagged with offscreen fallback.
- **Dependency ordering** — must follow the emulator swap; selection-in-Vulkan needs the binding selection extension (G1).

---

## 6. Recommendation
Build **A → E** for a production-quality Vulkan grid inside the existing ImGui shell. Treat **F** as opt-in for the transparency use-case only. This delivers the rendering upgrade with minimal risk and zero loss of existing UI, and consumes the same renderer-neutral **`TerminalFrame`** seam as the ImGui frontend — so both frontends can coexist behind a flag indefinitely, and the backend never changes.
