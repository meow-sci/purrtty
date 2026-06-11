# Direct-Vulkan Terminal Renderer — Feasibility

> Status: feasibility analysis (future exploration, not a committed build) · Date: 2026-06-07
> Companion to [`LIBGHOSTTY_ANALYSIS.md`](./LIBGHOSTTY_ANALYSIS.md). If greenlit, see [`LIBGHOSTTY_VULKAN_PLAN.md`](./LIBGHOSTTY_VULKAN_PLAN.md).
> Question: what would it take to render the terminal grid in **Vulkan** (an instanced glyph pipeline), instead of ImGui draw lists?
>
> **Design context:** the main plan establishes a **renderer-neutral seam** — the backend (`purrTTY.Terminal`) produces a `TerminalFrame` snapshot and accepts an `ITerminalSurface` command/event sink, with **no ImGui or Vulkan types crossing the boundary**. A Vulkan frontend is therefore just *another consumer of `TerminalFrame`* — no backend changes. This doc assumes that seam exists.

---

## 1. TL;DR

- **The grid pipeline itself is straightforward and a great fit.** libghostty's `RenderState` hands us, per cell, a `Grapheme` plus **pre-resolved** `FgColor`/`BgColor` and style flags — exactly the inputs for an instanced "one quad per cell" shader. BRUTAL has a complete Vulkan stack and KSA already ships working instanced shaders (`Sprite.vert`, `Star.vert`) and a full embedded-SPIR-V ImGui Vulkan backend (`KSA/ImGuiBackendVulkanImpl.cs`, ~68 KB) to model after.
- **The hard part is not drawing — it's *where* a mod is allowed to draw.** KSA gives mods **no handle to the active command buffer or render pass**. The terminal reaches the screen today only because `[StarMapAfterGui]` emits **ImGui** draw-lists that `ImGui.Render()` packages into KSA's frame. A native Vulkan pass has no such free ride.
- **There is no glyph rasterizer in BRUTAL.** `Brutal.Stb` is stb_**image** (decode), not stb_truetype. You either reuse ImGui's existing font atlas, or bake/ship your own atlas.
- **purrtty's UI is mostly *chrome*, not grid.** Tabs, menus, the settings modal, selection UI, and window management are all ImGui. Replacing the grid with Vulkan does **not** free you from ImGui; it splits rendering across two systems.
- **Recommended shape: a *hybrid*.** Render the grid to an **offscreen `VkImage`** (using the device/queue/allocator you *can* reach), then present it as a textured quad via `ImGui.Image()` inside the existing terminal window. This sidesteps the command-buffer problem entirely and keeps 100% of the ImGui chrome. "No ImGui at all" is not realistic without rebuilding the whole terminal UI.

---

## 2. How the terminal is drawn today

- purrtty renders the grid through `ITerminalDrawTarget` → `ImGuiDrawTarget` → `ImDrawListPtr.AddRectFilled/AddText/AddLine`, inside an ImGui window, from `[StarMapAfterGui]` (`TerminalMod.OnAfterUi` → `_controller.Render()`).
- KSA's frame (from the decompiled `KSA/Program.cs`): build all ImGui UI → **`ImGui.Render()`** packages every mod's draw-lists into vertex/index buffers → KSA records the frame into a command buffer and, inside the final composite render pass, calls **`ImGuiBackend.Vulkan.RenderDrawData(cmd)`** which replays those draw-lists (see `ImGuiBackendVulkanImpl.cs`).
- **Consequence:** `[StarMapAfterGui]` is *not* a GPU hook. It runs while ImGui draw-lists are still being assembled. Anything a mod draws must go through ImGui to be composited. A raw Vulkan pass recorded by the mod has nowhere to attach by default.

---

## 3. What BRUTAL/KSA give you to build a Vulkan renderer

**Reachable from a mod (the good news):**
- `IGlobalRenderSystem` exposes a `DeviceContext` (logical device, queues, memory allocator/VMA) and `Globals` (descriptor pools, transient buffers). Enough to **create your own** pipelines, buffers, images, descriptor sets, and to record into **your own** command buffers / offscreen targets.
- **Pipeline creation:** `RenderCore.Pipelines` (`SimpleGraphicsPipeline`, `SimplePipelineCreator`) — vertex input, input assembly, raster, blend, depth/stencil, dynamic state.
- **Buffers/images:** `Brutal.VulkanApi.Abstractions` (`BufferEx`, image creation, `IBufferAllocator`/`IImageAllocator` over VMA). Texture-upload precedent (staging buffer → `CopyBufferToImage` → barrier to `ShaderReadOnlyOptimal`) is spelled out in `ImGuiBackendVulkanImpl.UpdateTexture`.
- **Shaders:** GLSL 450 → SPIR-V via `Brutal.ShaderCompiler`/`RenderCore.Shaders` (with include resolution), **or** embed precompiled SPIR-V as `uint[]` — which is exactly what the ImGui backend does (`ShaderSource.Vert/Frag` in `ImGuiBackendVulkanImpl.cs`). Embedded SPIR-V is the lower-risk choice (no runtime compiler dependency).
- **Instanced-quad precedent:** `Content/Core/Shaders/Sprite.vert` and `Star.vert` show the pattern — per-instance position + packed color/scale, `gl_VertexIndex` to expand the quad corners, NDC via push-constant scale/translate.

**Not reachable from a mod (the hard news):**
- The **per-frame command buffer** KSA records into (a private local; the decompiled source shows the composite pass calling `ImGuiBackend.Vulkan.RenderDrawData(commandBuffer2)`).
- The **composite `VkRenderPass`** / swapchain framebuffer and its sample count (needed to build a pipeline that can record *into KSA's pass*).
- Any "inside the composite pass" callback. `IGlobalRenderSystem` deliberately exposes device-level handles, not frame-level ones.

---

## 4. The glyph atlas problem

A terminal renderer needs glyph coverage → texture + per-glyph UVs. BRUTAL has **no truetype rasterizer** (`Brutal.Stb` = `Stb.cs`/`StbImage8/16/F.cs`, i.e. stb_image decode only). Options:

| Option | How | Trade-off |
|---|---|---|
| **A. Reuse ImGui's atlas** | purrtty already builds an ImGui font atlas (`PurrTTYFontManager`); ImGui uploads it as an RGBA32 `VkImage` with a descriptor set. Sample it from your own shader using ImGui's per-glyph UV rects. | No new rasterizer; but extracting clean per-glyph UVs from ImGui for a *foreign* pipeline is awkward, and you're coupled to ImGui's atlas lifecycle. |
| **B. Pre-baked monospace atlas** | Bake a fixed-cell atlas offline from the terminal fonts you ship in `TerminalFonts/`; UV = simple codepoint→cell lookup. | Cleanest for a terminal (fixed grid, edge-to-edge box drawing). Needs a bake step and a fallback/coverage strategy (Nerd Font ranges, CJK, emoji). |
| **C. Add a rasterizer** | P/Invoke stb_truetype or a managed TTF rasterizer; build the atlas at load, repack on font/size change. | Most flexible (dynamic fonts/sizes, grapheme shaping) but the most new code and the only option that needs a new native/managed dependency. |

For a first cut, **B** pairs perfectly with `RenderState` (per-cell `Grapheme` → glyph index, pre-resolved colors → instance attributes). **C** is the long-term answer if you want arbitrary fonts/ligatures.

---

## 5. The instanced-cell pipeline (the easy, fun part)

One instance per (non-empty) cell; attributes pulled straight from `RenderState`:

```
per-instance: ivec2 cellXY · uint glyphIndex · uint fgRGBA · uint bgRGBA · uint flags(inverse/bold/underline…)
vertex:  expand quad via gl_VertexIndex; screenPos = cellXY * cellSize; NDC via push-const scale/translate
fragment: a = sample(atlas, glyphUV).r;  out = mix(bg, fg, a);  apply underline/strike from flags
```
- Background can be a second instanced pass or packed into the same instance (draw bg quad always, glyph quad when `Grapheme != null`).
- Cursor = one extra quad at `RenderState.CursorViewportX/Y` (shape from `CursorStyle`, blink from `CursorBlinking`).
- Selection = highlight quads from the per-row selection range (`ROW_DATA_SELECTION`, once the binding exposes it — see `LIBGHOSTTY_ANALYSIS.md` §5/G1).
- Source of truth: the backend's renderer-neutral **`TerminalFrame`** (the *same* contract the ImGui frontend draws) — the Vulkan frontend is just another consumer, with no `Ghostty.Vt` or `Cell[][]` coupling. Maps directly: `FrameRow.cells → instances`, `CursorState → cursor quad`, `FrameRow.SelectionRange → highlight quads`. This is the GhostlingDotNet `Renderer.Draw(...)` pattern emitting Vulkan instances instead of Raylib calls.

This part is ~a few hundred LOC of C# + ~200–300 LOC GLSL and carries little risk.

---

## 6. Where to actually draw — three integration options

### (i.a) Harmony-inject into KSA's composite pass — *true overlay, fragile*
Patch `Program.RenderMain`/`RenderEditor` (or `PostRender`) to capture the composite command buffer + render pass around the `ImGuiBackend.Vulkan.RenderDrawData(...)` call, then record your instanced draws there.
- **Pros:** true GPU overlay; can composite over the 3D scene (e.g. real terminal transparency).
- **Cons:** depends on **private, decompiled internals** (locals like `commandBuffer2`); your pipeline must be **render-pass-compatible** with KSA's composite pass (format + sample count); **breaks on KSA render-path changes**. Highest maintenance.

### (i.b) Offscreen image → `ImGui.Image()` — *recommended hybrid*
Render the grid into your **own** offscreen `VkImage` (create device/queue/allocator from `IGlobalRenderSystem.DeviceContext`; your own render pass; your own command buffer submitted to the graphics queue). Register the image as an ImGui texture and draw it with `ImGui.Image(...)` inside the existing terminal ImGui window in `[StarMapAfterGui]`.
- **Pros:** **no frame-hook hacks**; KSA's ImGui pass composites it for you; keeps **all** ImGui chrome; resilient to KSA updates. You still get Vulkan-quality glyph rendering (subpixel placement, ligatures, custom effects, no per-glyph `AddText`).
- **Cons:** one extra full-window blit per frame; you manage an offscreen target + sync; DPI/cell-metric parity with the ImGui path needs care.

### (ii) Full custom UI — *not recommended*
Rebuild tabs/menus/settings/selection/window chrome in raw Vulkan. Reimplements a mature ImGui UI for no user-visible gain, and **still** faces the command-buffer-injection problem for every widget. Months of work.

---

## 7. The realism check: purrtty is mostly chrome

The grid is a small fraction of `purrTTY.Display`. Everything else is ImGui and would have to be kept or rebuilt:
- window chrome + opacity; **tabs** (`TerminalUiTabs`); **menu bar + ~10 submenus** (`Menus/*`); **settings modal** (`TerminalUiSettingsPanel`); font/theme pickers; **selection overlay** (`TerminalUiSelection`); toggle-hotkey modal; scrollbar UI; performance panels.

So a "direct Vulkan renderer" realistically means **Vulkan grid + ImGui chrome** (option i.b), not "drop ImGui." Dropping ImGui entirely (option ii) is a separate, much larger project.

---

## 8. Effort & biggest unknowns

| Path | Rough effort | Biggest unknowns |
|---|---|---|
| **i.b hybrid (offscreen → `ImGui.Image`)** | ~1.5–3 weeks | glyph atlas/UV pipeline (no truetype in BRUTAL); DPI/cell-metric parity; offscreen target lifecycle + sync; wide-grapheme rendering. |
| **i.a true overlay (Harmony into composite pass)** | +1–2 weeks on top, plus ongoing fragility | extracting `commandBuffer2` + composite `VkRenderPass` reliably from a private/obfuscated method; pipeline render-pass compatibility (format/samples); breakage on KSA updates. |
| **ii full custom UI** | months | reimplementing the entire terminal UI; input/IME; not worth it. |

**Dependencies:** all paths assume the emulator swap has landed and the backend exposes the renderer-neutral `TerminalFrame`/`ITerminalSurface` seam. Do the swap first. (Because the binding is vendored/owned, if a Vulkan-specific data need arises — e.g. a packed per-cell instance struct — it can be added in our own `TerminalFrame` builder without touching the C library.)

---

## 9. Recommendation

If a Vulkan grid is desired, build **option i.b** on top of the RenderState-native engine: pre-baked atlas → instanced pipeline (embedded SPIR-V) → offscreen `VkImage` → `ImGui.Image()` into the terminal window. It delivers the rendering-quality upside (custom glyph layout, effects, performance for large grids, proper grapheme rendering) at a fraction of the risk, and keeps the entire ImGui UI intact. Reserve **option i.a** for the specific case where you need the terminal to composite *over the 3D scene* (true transparency); otherwise i.b dominates. Avoid **option ii**.

See [`LIBGHOSTTY_VULKAN_PLAN.md`](./LIBGHOSTTY_VULKAN_PLAN.md) for the phased build if greenlit.

---

## 10. Key references
- `thirdparty/ksa/KSA/ImGuiBackendVulkanImpl.cs` — embedded-SPIR-V pipeline, vertex/index buffers, texture upload, per-frame draw (the template).
- `thirdparty/ksa/RenderCore.Pipelines/*`, `RenderCore.Systems/RenderGlobals.cs` — pipeline/descriptor/buffer creation.
- `thirdparty/ksa/Content/Core/Shaders/{Sprite,Star}.vert` — instanced-quad pattern.
- `thirdparty/ksa/Brutal.ShaderCompilerApi/*`, `RenderCore.Shaders/ShaderCompilerResolve.cs` — GLSL→SPIR-V.
- `purrTTY.Display/Rendering/{ITerminalDrawTarget,ImGuiDrawTarget}.cs` — current ImGui draw seam.
- `purrTTY.GameMod/TerminalMod.cs` — `[StarMapAfterGui]` render hook; Harmony patch site for option i.a.
- libghostty `examples/GhostlingDotNet/Renderer.cs` — `Draw(RenderState, Terminal)` reference (Raylib analog of the instance emitter).
