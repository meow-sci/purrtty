# ImGui DrawList Caching / Reuse Plan

> **Created:** January 6, 2026  
> **Status:** Planning  
> **Goal:** Avoid re-building per-cell ImGui draw commands on frames where the terminal viewport is unchanged.

## Feasibility Analysis

### What ImGui actually does
ImGui is **immediate-mode**: every frame, UI code re-submits draw primitives into an `ImDrawList`, and ImGui produces a fresh `ImDrawData` for the backend to render. The per-window draw list is **not persistent** across frames.

**Implication:** we generally **cannot “reuse the window draw list” between frames** in the literal sense (the list is rebuilt and cleared every frame).

### What *is* possible (and can be a massive win)
We can cache **the render result** of terminal content and re-display it cheaply:

1. **Best win (preferred): cache as an offscreen texture**
   - When the terminal viewport changes, render the terminal content into a GPU texture.
   - Every frame, draw that texture via a single `AddImage`/`Image` call.
   - Overlay truly-dynamic elements (cursor blink, selection rectangle while dragging, IME caret) as normal drawlist primitives.

2. **Fallback: cache draw geometry (vertex/index buffers) and memcpy into the current drawlist**
   - Build and store a “terminal mesh” (ImGui vertices/indices + draw commands) when terminal changes.
   - Each frame, copy the cached buffers into the current `ImDrawList` rather than re-running cell loops and `AddText` per character.
   - This is still O(n) copying, but avoids high-overhead per-cell C# calls.

### Why this is relevant to caTTY
`caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs` currently emits many draw calls (`AddRectFilled`, `AddText`, etc.). This is *already* reduced when viewing the live screen via:
- dirty row tracking (`IScreenBuffer.IsRowDirty`)
- early exits for empty/default cells

However, when viewing **scrollback** (or any state where dirty tracking can’t be used), the viewport can be stable for long stretches but we still rebuild draw primitives every frame.

## Proposed Architecture

### A. Cache the terminal content into a texture (preferred)

**High-level idea:**
- Render the terminal grid (backgrounds + glyphs + decorations that are “content”) into an offscreen framebuffer **only when invalidated**.
- Every frame: draw the cached texture and overlay cursor/selection.

**Dynamic overlays (draw every frame):**
- cursor (blink, style)
- selection (while dragging and/or when selection exists, depending on UX)
- hover effects / highlights (if any)

**Static content baked into texture (update only on invalidate):**
- cell backgrounds
- glyphs
- underline/strikethrough decorations

### B. Invalidation / cache key
Introduce a small “render key” struct to decide when the cached texture must be regenerated.

**Cache invalidation triggers (minimum):**
- Terminal viewport contents changed
  - live screen: `ScreenBuffer.HasAnyDirtyRows()`
  - scrollback: scroll offset changed OR viewport source changed
- Dimensions changed (cols/rows) or font metrics changed (font/size/DPI)
- Theme/color configuration changed (palette, opacity)
- Alternate screen toggled / buffer switched

**Overlays can avoid invalidating content texture:**
- cursor move/blink changes → overlay only
- selection changes → overlay only (unless we decide to bake it)

## Implementation Plan

### Phase 0 — Discovery / hard constraints (1–2 small PRs)
1. Confirm backend support for rendering a texture and presenting it through ImGui
   - Identify how `Brutal.ImGuiApi` / `ImGuiBackend` expects textures to be registered (often via a descriptor-set-like handle).
   - Check how this differs between:
     - `caTTY.TestApp` / `caTTY.Display.Playground` standalone Vulkan renderer
     - `caTTY.GameMod` inside KSA render loop
2. Decide the abstraction point for “render-to-texture” so the terminal UI code doesn’t depend on Vulkan specifics.

**Deliverable:** a short spike implementation or documented API choice.

### Phase 1 — Introduce a render cache abstraction
1. Add `TerminalViewportRenderCache` (location suggestion: `caTTY.Display/Rendering/`)
   - Owns:
     - cached texture handle (ImGui texture id)
     - cached size in pixels
     - last `TerminalRenderKey`
2. Add `TerminalRenderKey`
   - `(cols, rows, charWidth, lineHeight)` or derived pixel size
   - theme version/hash
   - scrollback offset / viewport mode
   - “content revision” source (dirty flags or scrollback generation)

**Deliverable:** cache object + plumbing (no rendering change yet).

### Phase 2 — Render terminal content into texture on invalidation
1. Refactor `TerminalUiRender.RenderTerminalContent` to have two paths:
   - **Cached path:** `ImGui.Image(cachedTexture, size)` + overlay draws
   - **Legacy path:** current per-cell drawlist rendering (fallback)
2. Implement the “content render” function that draws into offscreen target
   - Reuse existing glyph/background logic where possible.
   - If the backend cannot render ImGui primitives into an offscreen pass, render via the engine’s text renderer (still fine; goal is caching).

**Deliverable:** measurable drop in per-frame CPU when viewport unchanged.

### Phase 3 — Overlays and correctness
1. Cursor overlay
   - Ensure cursor renders correctly over the cached texture.
2. Selection overlay
   - Ensure selection highlight renders correctly (and remains interactive).
3. Edge cases
   - resizing terminal window
   - switching alternate screen
   - scrolling into/out of scrollback
   - theme changes

**Deliverable:** correctness parity with legacy rendering.

### Phase 4 — Optional fallback: geometry caching (only if texture path blocked)
If offscreen texture integration is not viable in KSA/Brutal backend, implement a geometry cache:
- Build cached vertex/index buffers for the terminal once per invalidation.
- Each frame, append the cached geometry to the window drawlist with a minimal memcpy.

## Validation / Success Criteria
- When terminal viewport is unchanged, **terminal rendering CPU time** in the ImGui loop drops to near-zero (only overlays remain).
- When terminal updates (a few times per second), cost is paid only on those frames.
- No visual regressions in:
  - glyph positioning (grid-aligned rendering)
  - background runs
  - underline/strikethrough
  - selection
  - cursor

## Notes / Known Constraints
- `IScrollbackManager` explicitly warns that viewport row references must not be stored beyond the current frame; caching must use either:
  - a revision/generation number, or
  - a copied snapshot (only when invalidated), or
  - a hash computed at invalidation time.
- Dirty row tracking already exists for the live screen; the biggest win is likely **static scrollback views** and other “unchanged but still rendered every frame” cases.
