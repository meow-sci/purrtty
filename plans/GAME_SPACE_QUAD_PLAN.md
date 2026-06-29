# GAME_SPACE_QUAD_PLAN — Render the purrTTY terminal onto a 3D in-world quad

> **Status:** design/implementation plan. Nothing here is implemented yet in the current tree.
> **Target engine:** KSA build `2026.6.9.4750` (decomp at `C:\Users\Alex\repos\meow-sci\ksa-game-assemblies\current\decomp`). Every KSA symbol referenced below was verified present in this build.
> **Author of record:** distilled from (a) the proven implementation on branch `feature/render2text-opus-plan` of `C:\Users\Alex\repos\meow-sci\purrtty_ingame`, (b) the current post-libghostty-vt codebase, and (c) the `ksa` skill topic `quad.md`.

---

## 0. Goal & scope (what we are building)

Render the purrTTY terminal into a GPU texture each frame and draw that texture on a **flat quad in 3D game space** that participates correctly in the scene's depth / occlusion / clipping (a part can occlude it; it occludes parts behind it).

This restores the capability proven on the old `feature/render2text-opus-plan` branch, **adapted to the current three-layer architecture** (Display frontend / Terminal backend / Ghostty.Vt binding) and **re-scoped per the product owner's decisions**:

| Decision | Choice | Consequence for design |
|---|---|---|
| **2D window visibility** | The on-screen ImGui terminal window must be **hidden** in this mode — only the 3D quad is visible. | The in-world terminal is a **self-contained subsystem**, not a mirror of an on-screen window. |
| **Content source** | **Dedicated in-world session** (its own shell, ticked by the in-world manager). | Removes the old branch's "the 2D window must stay rendered" constraint — that constraint came from mirroring a shared session, not from the offscreen mechanism. |
| **Interactivity** | **Click-to-focus in 3D** + keyboard forwarding. Mouse→cell mapping is an **optional, scoped-in stretch** (we already raycast the quad, so barycentric→UV→cell is tractable). | Phase 8 is "must"; Phase 9 (cell mapping) is "optional, cuttable". |
| **Anchoring** | **Two modes**: (a) anchored to a vehicle **Part/SubPart** with offset/rotation/scale, and (b) **camera-locked billboard**. Chosen via a **launch popup → mode buttons → mode-tailored config form**. | The quad pipeline is shared; only the per-frame model matrix and the depth state differ between modes. |

Non-goals for v1: in-world clipboard UX, multiple simultaneous in-world quads, kitty-graphics-on-quad (deferred — see §13), VR.

---

## 1. The two halves (and why the engine ships them separately)

Every "terminal on a quad" implementation is two independent halves that KSA ships but never combines:

1. **Source — render content into a sampleable texture.** Make a `KSA.RenderTarget` (color `R8G8B8A8UNorm`), render the terminal UI into it via a **second, isolated ImGui context + second Vulkan ImGui backend**, on a **mod-owned command buffer + fence**. Output: a `VkImageView` whose layout is `ShaderReadOnlyOptimal` (the render pass's `FinalLayout`).
2. **Destination — draw that texture on a world-space quad.** A hand-built `VkPipeline` (reusing KSA's stock `UnlitMesh.{vert,frag}`) draws a unit quad sampling that image, injected into the game's opaque scene pass via a **Harmony postfix on `SuperMeshRenderSystem.RenderMainPass`**, with an **ego-space MVP** anchored to a Part/SubPart (or a view-space MVP for billboard).

The `ksa` skill file `.claude/skills/ksa/quad.md` documents the destination half almost verbatim (it was distilled from the old branch). Treat it as the canonical companion to §6 below.

---

## 2. Architecture overview

```
purrTTY.GameMod  (StarMap lifecycle + Harmony + KSA render refs)
└── InWorld/                                   ← NEW subsystem (mirrors old branch layout)
    ├── InWorldTerminalManager                 coordinator: lifecycle, toggle, per-frame drive
    ├── OffscreenRenderTarget                  KSA RenderTarget (color R8G8B8A8UNorm + depth) + sampler
    ├── OffscreenImGuiContext                  2nd ImGui context (shares main font atlas) + With() guard
    ├── OffscreenImGuiBackend                  2nd ImGuiBackendVulkanImpl bound to the offscreen pass
    ├── PerFrameRenderer                       cmd-buffer+fence ring; NewFrame→BuildUi→Render→record→submit
    ├── InWorldTerminalRenderer                dedicated session + FrameGridRenderer draw into the 2nd ctx
    ├── Display/InWorldQuad                    UnlitMesh pipeline + quad mesh + descriptor set + RecordDraw
    ├── Input/QuadPicker                       Cursor.InputRay × quad (Möller-Trumbore) → focus (+opt. cell)
    ├── Input/InWorldFocus                     focus state holder
    ├── Patches/RenderMainPassPatch            Harmony postfix → InWorldQuad.RecordDraw(cmd)
    ├── Settings/InWorldSettings               TOML persistence (anchor mode, transform, shell, hotkey)
    └── UI/InWorldLaunchUI                     launch popup (mode buttons) + mode-tailored config forms
        (reuses)
purrTTY.Display    FrameGridRenderer, FrameFonts, PurrTTYFontManager, ImageTextureCache (pattern ref)
purrTTY.Terminal   SessionManager, TerminalSession, GhosttyTerminalSurface, TerminalFrame (the seam)
```

**Key architectural point that makes this clean:** the current frontend's grid drawer, `FrameGridRenderer.Render(TerminalFrame frame, ImDrawListPtr drawList, float2 origin, …)` (`purrTTY.Display/Ghostty/FrameGridRenderer.cs:46`), is **already render-target-neutral** — it draws into whatever `ImDrawListPtr` you pass. Today `TerminalWindow.Render` passes it `ImGui.GetWindowDrawList()` (`TerminalWindow.cs:413`); the in-world renderer passes it the secondary context's window draw list instead. The grid/glyph/cursor/decoration code is reused verbatim.

---

## 3. Source material — the proven old branch

The working implementation is **not** in the checked-out worktree (`feature/render-in-game` has only plan markdown). It lives on branch **`feature/render2text-opus-plan`** of the repo at `C:\Users\Alex\repos\meow-sci\purrtty_ingame` (tip `527269e`; implementation dated 2026-05-14/15). A competing branch `feature/render2text-gpt-plan` used a CPU software rasterizer — **ignore it** (lower fidelity, not the distilled path).

Access the reference code (do **not** merge — port deliberately into the current architecture):

```bash
# from C:\Users\Alex\repos\meow-sci\purrtty_ingame
git worktree add ../purrtty_opus feature/render2text-opus-plan
# or, one file at a time:
git show feature/render2text-opus-plan:purrTTY.GameMod/InWorld/Display/QuadDisplay.cs
```

### Old-branch file map (all under `purrTTY.GameMod/InWorld/` on `feature/render2text-opus-plan`)

| Concern | Old class | Old file | Port to |
|---|---|---|---|
| Coordinator / lifecycle | `InWorldTerminalManager` | `InWorld/InWorldTerminalManager.cs` | `InWorldTerminalManager` (§5.9) |
| Offscreen RT (color+depth+FB+sampler) | `OffscreenRenderTarget` | `InWorld/OffscreenRenderTarget.cs` | `OffscreenRenderTarget` (§5.1) — port ~as-is |
| 2nd ImGui context | `OffscreenContext` | `InWorld/OffscreenContext.cs` | `OffscreenImGuiContext` (§5.2) — port ~as-is |
| 2nd ImGui Vulkan backend | `OffscreenImGuiBackend` | `InWorld/OffscreenImGuiBackend.cs` | `OffscreenImGuiBackend` (§5.3) — port ~as-is |
| Per-frame render loop (cmd ring, fences) | `PerFrameRenderer` | `InWorld/PerFrameRenderer.cs` | `PerFrameRenderer` (§5.4) — port ~as-is |
| The 3D quad (pipeline/mesh/MVP/draw) | `QuadDisplay` | `InWorld/Display/QuadDisplay.cs` | `InWorldQuad` (§5.6) — port + add billboard mode |
| Harmony injection + GLFW input fwd | `FramePatches` (+4 patch classes) | `InWorld/Patches/FramePatches.cs` | `RenderMainPassPatch` (§5.7) + input gating (§8) |
| Ray-vs-quad picking → focus | `QuadPicker` | `InWorld/Input/QuadPicker.cs` | `QuadPicker` (§5.8) |
| Focus state | `InWorldFocus` | `InWorld/Input/InWorldFocus.cs` | `InWorldFocus` (§8) — port ~as-is |
| Settings | `InWorldSettings` | `InWorld/Settings/InWorldSettings.cs` | `InWorldSettings` (§5.10) — extend for 2 modes |
| Settings UI | `InWorldSettingsWindow` | `InWorld/UI/InWorldSettingsWindow.cs` | `InWorldLaunchUI` (§7) — redesign for launch popup |
| Terminal content into the offscreen window | `TerminalController.RenderContentOnly` | `purrTTY.Display/Controllers/TerminalController.cs:1173` | **GONE** — replaced by `FrameGridRenderer.Render` over a dedicated session's `TerminalFrame` (§5.5) |

> The one part that does **not** port directly is the content draw: the old branch's bespoke-emulator `TerminalController.RenderContentOnly` no longer exists. In the current codebase the content is a `TerminalFrame` snapshot drawn by `FrameGridRenderer`. This is the central adaptation — see §5.5.

---

## 4. Verified KSA API surface (all present in `2026.6.9.4750`)

Paths are relative to `…\ksa-game-assemblies\current\decomp`. These are the symbols the implementation binds to; each was checked against the current decomp.

### Renderer / passes / device
| Symbol | Location |
|---|---|
| `KSA.Program.GetRenderer() → Core.Renderer` | `KSA\Program.cs:450` |
| `KSA.Program.OffScreenPass → Core.RenderPassState` (the **scene** pass — bind the quad pipeline here) | `KSA\Program.cs:375` |
| `KSA.Program.MainPass → Core.RenderPassState` (swapchain, 1-bit — **do NOT bind the quad here**) | `KSA\Program.cs:373` |
| `Core.RenderPassState.Pass` (`VkRenderPass`), `.SampleCount` (`VkSampleCountFlags`) | `Core\RenderPassState.cs:11,26` |
| `KSA.Program.GetMainCamera() → KSA.Camera` | `KSA\Program.cs:489` |
| `KSA.Program.SetViewport(CommandBuffer)` (sets dynamic viewport+scissor) | `KSA\Program.cs:3781` |
| `KSA.Program.ControlledVehicle` (`Vehicle?`) | `KSA\Program.cs:254` |
| `Core.Renderer.Device` (`DeviceEx`), `.Allocator` (`KsaVmaAllocator`), `.Graphics` (`Queue`), `.DepthFormat`, `.DynamicStateInfo`, `.ViewportState`, `.LinearSampler` | `Core\Renderer.cs` (+ base `Core\KSADeviceContextEx.cs:51,53,55`) |

### Offscreen target / texture / staging
| Symbol | Location |
|---|---|
| `KSA.RenderTarget(Renderer, string, VkExtent2D, VkFormat color, VkFormat depth, int depthSlices=1, int mipLevels=1)` | `KSA\RenderTarget.cs:22` |
| `RenderTarget.CreateRenderPass(...)` → color `FinalLayout = ShaderReadOnlyOptimal` (no manual barrier needed) | `KSA\RenderTarget.cs:65,75` |
| `RenderTarget.BuildFramebuffer(VkRenderPass)`; `.ColorImage.ImageView`; `.FrameBuffer` | `KSA\Framebuffer.cs:51,30,28` |
| `RenderCore.SimpleVkTexture` (alt. manual texture; `.ImageView`, `.Image`, `UploadData(...)`) | `RenderCore\SimpleVkTexture.cs:16` |
| `RenderCore.VkUtils.StageAndUploadToBuffer(StagingPool, VkBuffer, ByteSize, Span<T>, CommandBuffer)` | `RenderCore\VkUtils.cs:80,99` |
| `IBufferAllocator.CreateStagingPool(Queue, int count)` (ext); `StagingPool.NextCommandBuffer()`, `.Submit().Wait()` | `Brutal.VulkanApi.Abstractions\StagingPoolExtensions.cs` |

### ImGui (multi-context + backend) — verified this run
| Symbol | Location |
|---|---|
| `Brutal.ImGuiApi.ImGui.CreateContext(ImFontAtlasPtr shared=default) → ImGuiContextPtr` | `Brutal.ImGuiApi\ImGui.cs:5401` |
| `ImGui.DestroyContext / GetCurrentContext / SetCurrentContext(ImGuiContextPtr)` | `…\ImGui.cs:5406,5411,5416` |
| `ImGui.NewFrame() / EndFrame() / Render() / GetDrawData() → ImDrawDataPtr` | `…\ImGui.cs:5436,5441,5446,5451` |
| `KSA.ImGuiBackendVulkanImpl(in CreateInfo)` — **public** ctor; `CreateInfo` has `required RenderPass, required SampleCount, required DescriptorPoolSize (≥256)` plus Device/GraphicsQueue/SubPass/MinImageCount/ImageCount/MinAllocationSize | `KSA\ImGuiBackendVulkanImpl.cs:18,618` |
| `ImGuiBackendVulkanImpl.RenderDrawData(CommandBuffer)` **and** `RenderDrawData(ImDrawDataPtr, CommandBuffer)` | `…\ImGuiBackendVulkanImpl.cs:652,657` |
| `KSA.ImGuiBackend.Vulkan` (singleton) `.AddTexture(VkSampler, VkImageView) → ImTextureRef`, `.RemoveTexture(ImTextureRef)` (only needed if we ever show the RT in a 2D ImGui window) | `KSA\ImGuiBackend.cs`; impl `…ImpL.cs:1052,1060` |

> `required` fields will fail compilation if omitted — so the implementer is forced to fill the full `CreateInfo`. Crib the exact field set from the decomp (`KSA\ImGuiBackendVulkanImpl.cs:18`) and from the old branch's `OffscreenImGuiBackend.cs:49-60`.

### Shaders / pipeline presets / vertex input
| Symbol | Location |
|---|---|
| `KSA.ModLibrary.Get<ShaderReference>("UnlitMeshVert" / "UnlitMeshFrag")`; `ShaderReference → VkShaderModule` (implicit) | `KSA\ShaderReference.cs` |
| `KSA.UnlitMeshRenderTechnique` — engine's **own** texture+mesh+MVP primitive (NavBall uses it). Reference for the contract; we hand-build to control depth state. | `KSA\UnlitMeshRenderTechnique.cs` |
| `Brutal.VulkanApi.Abstractions.VertexInput(bindings, attrs).AddBinding(...).AddAttribute(...).Check()` | `Brutal.VulkanApi.Abstractions\VertexInput.cs` |
| `Presets.InputAssembly.TriangleList`, `Presets.Rasterization.Fill.CullNone`, `Presets.BlendState.BlendNone`, `Presets.Sampler.SamplerLinearClamped` | `Brutal.VulkanApi.Abstractions\Presets.cs:167,219,30,321` |
| `KSA.RenderingPresets.ReverseZDepthStencil.DepthTestWrite` (occluding) and `.NoDepthTest` (always-on-top) | `KSA\RenderingPresets.cs:14` |
| Device extension methods: `CreateDescriptorSetLayout`, `CreateDescriptorPool`, `AllocateDescriptorSet`, `CreatePipelineLayout`, `CreateGraphicsPipeline`, `UpdateDescriptorSets`, `CreateSampler` | `Brutal.VulkanApi.Abstractions\*Extensions.cs` |

### Camera / anchoring / picking
| Symbol | Location |
|---|---|
| `KSA.Camera.MVP → ViewProjection { float4x4 view, projection, viewProjection }` (row-vector: `viewProjection = view * projection`) | `KSA\Camera.cs:53`, `KSA\ViewProjection.cs` |
| `KSA.Vehicle.GetMatrixAsmb2Ego(Camera) → double4x4`; `Vehicle.Asmb2Ego`; `Vehicle.Parts.Parts` | `KSA\Vehicle.cs:833` |
| `KSA.Part.PositionEgo(in double4x4) → double3`; `Part.Asmb2Ego(doubleQuat) → doubleQuat`; `Part.Id`; `Part.SubParts` | `KSA\Part.cs:677,682` |
| `KSA.Cursor.InputRay` (`Ray`, ego-space, rebuilt each frame from `Camera.ScreenToEgoRay`) | `KSA\Cursor.cs:25` |
| `KSA.Ray.RaycastMollerTrumbore(double3 v0,v1,v2, out double t)` | `KSA\Ray.cs:54` |

### Injection point
| Symbol | Location |
|---|---|
| `KSA.SuperMeshRenderSystem.RenderMainPass(CommandBuffer)` — **instance** method, runs inside the already-begun offscreen scene pass; Harmony **postfix** target | `KSA\SuperMeshRenderSystem.cs:329` (called `KSA\Program.cs:4104`) |

---

## 5. Component design (detailed)

All new types live in `purrTTY.GameMod/InWorld/…` unless noted. Namespace: `purrTTY.GameMod.InWorld`. Follow the existing purrTTY.GameMod conventions (single project, `Console.WriteLine($"purrtty: …")` logging, try/catch around every lifecycle/render entry point). **Do not** import the generic meow-sci `<name>.lib` two-project split — purrTTY uses a single `purrTTY.GameMod`.

### 5.1 `OffscreenRenderTarget`
Owns the GPU image the terminal renders into and that the quad samples.

- Construct in `InWorldTerminalManager.Initialize()` after the renderer is live (`OnFullyLoaded`). Size: configurable, default **1024×1024** (old branch value). Color format **`VkFormat.R8G8B8A8UNorm`** (see §12 gamma gotcha). Depth = `renderer.DepthFormat`.
- ```csharp
  Target     = new RenderTarget(renderer, "purrtty-inworld", Extent, R8G8B8A8UNorm, renderer.DepthFormat, 1, 1);
  RenderPass = Target.CreateRenderPass();          // FinalLayout = ShaderReadOnlyOptimal
  Target.BuildFramebuffer(RenderPass);
  var ci = Presets.Sampler.SamplerLinearClamped;
  Sampler = renderer.Device.CreateSampler(in ci, null);
  ```
- Expose `VkImageView ColorImageView => Target.ColorImage.ImageView` and `VkSampler Sampler`.
- `Resize(w,h)` (idempotent dispose+recreate) exists but is **not** wired to runtime UI in v1 — texture size is fixed; world size/UV are the live knobs. (Match old branch.)
- Dispose: destroy sampler, dispose `Target`.

### 5.2 `OffscreenImGuiContext`
A second ImGui context, **sharing the main font atlas** so the existing `FrameFonts`/`ImFontPtr` handles are valid inside it (the main game ImGui backend always runs and uploads atlas glyphs, so a hidden 2D window does not starve glyph uploads — this is what makes the dedicated-session design work without a visible window).

- ```csharp
  var prev = ImGui.GetCurrentContext();
  ImFontAtlasPtr shared = prev.IsNull() ? default : ImGui.GetIO().Fonts;   // main atlas
  Native = ImGui.CreateContext(shared);
  ImGui.SetCurrentContext(Native);
  var io = ImGui.GetIO();
  io.DisplaySize     = new float2(width, height);
  io.DeltaTime       = 1f/60f;
  io.IniFilename     = default;
  io.MouseDrawCursor = false;
  ImGui.SetCurrentContext(prev);                                           // restore
  ```
- `With(Action body)`: save current ctx → `SetCurrentContext(Native)` → `try { body(); } finally { SetCurrentContext(prev); }`. **Every** access to the secondary context (NewFrame/Render/IO writes/draws) goes through `With`.
- Dispose: if current == ours, `SetCurrentContext(default)`/main; then `ImGui.DestroyContext(Native)`.

### 5.3 `OffscreenImGuiBackend`
A second `ImGuiBackendVulkanImpl` whose render pass is the **offscreen terminal target** (NOT the scene pass, NOT the swapchain).

- Construct **while the secondary context is current** (`ctx.With(() => …)`), because the ctor mutates the current context's IO/backend user-data.
- ```csharp
  Impl = new ImGuiBackendVulkanImpl(new ImGuiBackendVulkanImpl.CreateInfo {
      Device = renderer.Device, GraphicsQueue = renderer.Graphics,
      RenderPass = offscreen.RenderPass,            // the terminal target's pass
      SubPass = 0,
      MinImageCount = 2, ImageCount = 2,
      SampleCount = VkSampleCountFlags._1Bit,       // terminal target is 1-sample (no MSAA)
      DescriptorPoolSize = 256,                     // ≥256 asserted by the backend
      /* fill any other `required` CreateInfo fields the compiler demands */
  });
  ```
- `Render(ImDrawDataPtr drawData, CommandBuffer cmd) => Impl.RenderDrawData(drawData, cmd);`
- Dispose under `ctx.With`.

> **SampleCount distinction (do not conflate):** the terminal **offscreen target** is 1-sample (`_1Bit`) — ImGui text needs no MSAA. The **quad pipeline** in §5.6 uses `Program.OffScreenPass.SampleCount` (scene MSAA, e.g. 4×/8×). Two different sample counts for two different passes.

### 5.4 `PerFrameRenderer`
Records + submits the offscreen ImGui pass on a mod-owned command buffer each frame.

- Resources: a **ring of 2** `CommandBuffer`s + **2** `VkFence`s (create fences **signaled** so frame 0 doesn't stall). Allocate from a mod-owned command pool (`renderer.GraphicsAndComputeCommandPool` or a fresh pool).
- `Frame(double dt)`:
  1. `WaitForFences(slotFence)` + `ResetFences(slotFence)`.
  2. `ctx.With(() => { io.DeltaTime = dt; io.DisplaySize = texSize; ImGui.NewFrame(); BuildUi(); ImGui.Render(); drawData = ImGui.GetDrawData(); });`  (wrap `BuildUi`/`Render` so a throw still calls `ImGui.Render()`/`EndFrame` — never leave a context mid-frame.)
  3. `cmd.Reset(); cmd.Begin(OneTimeSubmit); RecordRenderPass(cmd, drawData); cmd.End();`
  4. `renderer.Graphics.Submit(<none>, <none>, [cmd], <none>, slotFence);`  (`Queue.Submit` is internally locked vs. the game's own submissions — safe.)
  5. advance ring index.
- `RecordRenderPass(cmd, drawData)`: `BeginRenderPass(offscreen.FrameBuffer, offscreen.RenderPass, 2 clear values: color (0,0,0,1) + depth 1.0)` → `ctx.With(() => backend.Render(drawData, cmd))` → `EndRenderPass()`.
- **Shared-atlas hack (port from old branch — keep it):** for the duration of the secondary `Render`, set `drawData.Textures = null` so the secondary backend does **not** run the texture create/destroy loop on `ImTextureData` owned by the **main** backend's descriptor pool (that caused `DescriptorPoolInvalidOperationException`). Also wrap the per-draw `GetTexID` path so an `ImException` (glyph added this frame but not yet uploaded by the main backend) **skips that one frame** instead of disabling the feature. (Old branch `PerFrameRenderer.cs:233-260`.)
- `BuildUi` is supplied by `InWorldTerminalRenderer` (§5.5) via a delegate, exactly like the old branch's `SetBuildUi`.
- Dispose: wait all fences, free command buffers, destroy fences/pool.

### 5.5 `InWorldTerminalRenderer` — the content half (the main adaptation)
Owns the **dedicated terminal session** and supplies `BuildUi` (the per-frame draw into the secondary context).

- **Session ownership.** Reuse `purrTTY.Display/Ghostty/GhosttySessionManagerFactory` → one `SessionManager` with a single `TerminalSession` (the in-world shell). Default shell = the configured default (`Auto`) or a user choice from the launch form (§7). This is the exact infra a `TerminalWindow` uses, minus the window chrome.
- **Fonts.** Build a `FrameFonts` from `PurrTTYFontManager` exactly as `TerminalWindow.Fonts.cs` does. Because the secondary context **shares the main atlas**, these `ImFontPtr` handles are valid inside the secondary context.
- **Per tick (called from inside `ctx.With` by `PerFrameRenderer.BuildUi`):**
  1. Compute `cols/rows` from the texture's usable rect and the cell metrics (`avail / cellW`, `avail / cellH`) — push to the surface via `Surface.Resize(cols, rows, (int)cellW, (int)cellH)` when changed (same as `TerminalWindow.Render:373-374`).
  2. `var frame = session.Surface.BuildFrame();`  **This is the tick** — it drains the PTY inbox and advances the engine. Because the in-world manager runs in `OnAfterUi` (the render/tick thread), this satisfies the single-thread invariant (gotcha 1/19).
  3. Open a borderless, padding-less ImGui window in the secondary context at the target sub-rect (use the UV-rect knobs, default full texture). Mirror the old branch's `RenderContentOnly`: push zero window padding/border, `Begin` with `NoTitleBar|NoResize|NoMove|NoScrollbar|NoBackground` (or a configured background), then:
     ```csharp
     var drawList = ImGui.GetWindowDrawList();
     FrameGridRenderer.Render(frame, drawList, ImGui.GetCursorScreenPos(),
         cellW, cellH, fonts, fontSize, selectionColor, cursorOn,
         fgOpacity, cellBgOpacity, hasFocus: InWorldFocus.IsFocused);
     ```
  4. `End()`.
- **Generation gate (perf).** `TerminalFrame.Generation` bumps only on real content change. The in-world renderer should **skip re-recording the offscreen pass** when `Generation` is unchanged **and** the cursor-blink phase is unchanged — otherwise the quad re-renders the terminal every game frame regardless of activity. (Track `_lastGeneration` + `_lastCursorOn`; if both unchanged, reuse the existing texture — just skip steps 2-4's redraw, or skip the whole `PerFrameRenderer.Frame`.) The texture persists between frames (it's `ShaderReadOnlyOptimal` and untouched), so the quad keeps sampling the last good image.
- **Input encoding** when focused — see §8.
- **(Optional) kitty graphics** — deferred to a later phase (§13).

> Why no `TerminalWindow` reuse: `TerminalWindow` is inseparable from its own `ImGui.Begin/End`, chrome, tabs, geometry, hot-zone, and main-context focus model. The in-world renderer needs only (session + frame + fonts + grid draw). Reuse the **pieces** (`FrameGridRenderer`, `FrameFonts`, `GhosttySessionManagerFactory`, the input-encoding logic from `TerminalWindow.Input.cs`), not the window.

### 5.6 `InWorldQuad` (in `InWorld/Display/`) — the destination half
Hand-built pipeline + quad mesh + descriptor set; records the world-space draw. Port `QuadDisplay.cs` and **add the billboard model path**.

- **Shaders:** `ModLibrary.Get<ShaderReference>("UnlitMeshVert"/"UnlitMeshFrag")` → `VkShaderModule` (implicit op). **Do not destroy** these modules on dispose (owned by `ModLibrary`).
- **Vertex format / mesh** (centered unit quad, +Z normal, V-flipped so texture (0,0) = upper-left):
  ```csharp
  [StructLayout(LayoutKind.Sequential, Pack=1)] struct QuadVertex { public float3 Pos; public float2 Uv; }
  // verts: (-.5,-.5,0)uv(0,1) (.5,-.5,0)uv(1,1) (.5,.5,0)uv(1,0) (-.5,.5,0)uv(0,0)
  // indices: 0,1,2, 0,2,3
  var vin = new VertexInput(1,2)
      .AddBinding(0, ByteSize.Of<QuadVertex>(), VkVertexInputRate.Vertex)
      .AddAttribute(0,0, R32G32B32SFloat, ByteSize.Zero)
      .AddAttribute(1,0, R32G32SFloat,    ByteSize.Of<float3>())
      .Check();
  ```
  Upload buffers once at construction via a staging pool (`renderer.Allocator.CreateStagingPool(renderer.Graphics, 1)` → `StageAndUploadToBuffer` ×2 → `Submit().Wait()`), mirroring `SimpleVkMesh`.
- **Descriptor set:** one `CombinedImageSampler` (binding 0, fragment stage); pool MaxSets=1; written `{ ImageView = offscreen.ColorImageView, ImageLayout = ShaderReadOnlyOptimal, Sampler = offscreen.Sampler }`.
- **Pipeline layout:** the descriptor-set layout + one `VkPushConstantRange { VertexBit, 0, ByteSize.Of<float4x4>() }`.
- **Pipeline — the critical bits (this is the z-order fix from old commit `5be1aad`):**
  ```csharp
  RenderPass        = Program.OffScreenPass.Pass,                 // NOT Program.MainPass
  RasterizationState= Presets.Rasterization.Fill.CullNone,        // double-sided
  MultisampleState  = { RasterizationSamples = Program.OffScreenPass.SampleCount }, // match scene MSAA
  ColorBlendState   = Presets.BlendState.BlendNone,               // (or BlendColorAlpha if quad opacity wanted)
  DynamicState      = renderer.DynamicStateInfo,  ViewportState = renderer.ViewportState,
  ```
  **Build TWO pipeline variants differing only in depth state**, selected per anchor mode at draw time:
  - `DepthTestWrite` = `RenderingPresets.ReverseZDepthStencil.DepthTestWrite` (reverse-Z, `GreaterOrEqual`, write on) → **part-anchored** mode (occludes / is occluded correctly).
  - `NoDepth` = `RenderingPresets.ReverseZDepthStencil.NoDepthTest` → **camera-billboard** mode (always-on-top HUD panel). (Make this a per-mode setting; default billboard→NoDepth, part→DepthTestWrite.)
- **`RecordDraw(CommandBuffer cmd)`** (called from the postfix, §5.7):
  ```csharp
  if (!TryComputeModelEgo(out float4x4 model)) return;     // mode-specific, §6
  var cam = Program.GetMainCamera(); if (cam == null) return;
  float4x4 mvp = (AnchorMode == Billboard)
      ? model * cam.MVP.projection                          // billboard: model already in view space
      : model * cam.MVP.viewProjection;                     // part: model in ego space
  cmd.BindPipeline(Graphics, AnchorMode == Billboard ? _pipelineNoDepth : _pipelineDepthWrite);
  cmd.BindDescriptorSets(Graphics, _layout, 0, [_set], default);
  Program.SetViewport(cmd);                                 // offscreen pass uses a dynamic viewport
  cmd.PushConstants(_layout, VertexBit, ByteSize.Zero, mvp);
  cmd.BindVertexBuffers(0, [vb], [vbOffset]);
  cmd.BindIndexBuffer(ib, ibOffset, Uint16);
  cmd.DrawIndexed(6,1,0,0,0);
  ```
- Dispose (reverse order): pipelines, pipeline layout, descriptor pool, descriptor-set layout, vertex/index buffers, (NOT shader modules).

### 5.7 `RenderMainPassPatch` (Harmony postfix)
```csharp
[HarmonyPatch(typeof(SuperMeshRenderSystem), nameof(SuperMeshRenderSystem.RenderMainPass))]
internal static class RenderMainPass_Patch {
    static void Postfix(CommandBuffer commandBuffer) {
        if (!InWorldTerminalManager.Active) return;          // static flag, main-thread only
        try { InWorldTerminalManager.Instance?.Quad?.RecordDraw(commandBuffer); }
        catch (Exception ex) { Console.WriteLine($"purrtty: inworld draw failed: {ex}"); InWorldTerminalManager.Active = false; }
    }
}
```
- Register via the existing `Patcher.cs` mechanism (`CreateClassProcessor(type).Patch()` with its own try/catch — classify as **optional**, gotcha 21). The postfix runs on the main thread inside the begun offscreen scene pass (after all opaque part draws, before `EndRenderPass`), so the quad depth-tests against the full opaque scene.
- `Active`/`Instance` are plain statics flipped on the main thread — no locks (same-thread as the postfix).

### 5.8 `QuadPicker`
```csharp
public void Tick(InWorldFocus focus) {
    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
        if (_quad.TryRaycast(Cursor.InputRay, out double t, out float2 uv)) { focus.State = Focused; /* opt: store uv */ }
        else if (!ImGui.GetIO().WantCaptureMouse) focus.State = NotFocused;
    }
    if (focus.IsFocused && ImGui.IsKeyPressed(ImGuiKey.Escape)) focus.State = NotFocused;
}
```
- `InWorldQuad.TryRaycast`: transform the same 4 local corners by the current ego/view model matrix (must use the **same** matrix as `RecordDraw` — keep in sync or picking lands off-target), two `Ray.RaycastMollerTrumbore` tests. For **billboard** mode, build the picking corners in ego space from the view-space model (`viewModel * cam.MVP.view⁻¹`, or pick in screen space against the projected quad — simpler for billboard: test the cursor against the quad's screen-space rect). **Optional cell mapping (§9):** also return barycentric→UV; the `out float2 uv` above is the hook.

### 5.9 `InWorldTerminalManager`
The coordinator (port + extend the old branch's). Holds `Instance` (static) and `Active` (static, read by the postfix). Responsibilities:
- `Initialize()` (from `OnFullyLoaded`): build, in order, `OffscreenRenderTarget` → `OffscreenImGuiContext` → (under `ctx.With`) `OffscreenImGuiBackend` → `PerFrameRenderer` → `InWorldTerminalRenderer` (creates the dedicated session) → `InWorldQuad` → `QuadPicker`. Any throw → log, tear down in reverse, leave feature disabled.
- `OnAfterGui(dt)` (from `TerminalMod.OnAfterUi`): if enabled → `PerFrameRenderer.Frame(dt)` (render failure → disable), then `QuadPicker.Tick(focus)` (pick failure non-fatal). Drains the dedicated session every frame (the `BuildFrame` inside `BuildUi`) — required so its PTY inbox stays bounded (gotcha 18).
- `Enable(anchorConfig)` / `Disable()`: validate (part mode requires the target part resolves on `Program.ControlledVehicle`); flip `_settings.Enabled` + statics (`Active`, `Instance`, focus). On disable, **clear `Active` first**, then dispose GPU resources, so an in-flight postfix can't dereference freed handles.
- `Dispose()` (from `OnUnload`): clear statics, dispose in reverse construction order.

### 5.10 `InWorldSettings`
TOML via the existing `Configuration/AtomicFile` + Tomlyn (the project already uses this for themes). Persist under the same config dir. Fields:
- `Enabled : bool`, `AnchorMode : "part" | "billboard"`, `ToggleHotkey`.
- `TextureWidth/Height : int` (default 1024).
- Part mode: `TargetPartId : string`, `OffsetX/Y/Z`, `RotX/Y/Z` (deg), `WidthMeters`, `HeightMeters`.
- Billboard mode: `Distance`, `ScreenOffsetX/Y`, `WidthMeters`, `HeightMeters`, `AlwaysOnTop : bool`.
- Dedicated session: `ShellType`/launch config (default `Auto`).
- `LoadOrDefault()` / `Save()` mirroring `ThemeConfiguration`.

---

## 6. Anchor-mode math (unambiguous)

KSA renders the scene in **ego space** (float32, camera-centered; `camera.MVP.viewProjection` is built for ego-space verts) and uses **reverse-Z** (depth cleared 0, compare `GreaterOrEqual`). Row-vector convention: `A * B` applies A then B; `MVP = model * viewProjection`.

### 6.1 Part-anchored (`TryComputeModelEgo`, port from old branch)
```csharp
var cam = Program.GetMainCamera();        if (cam == null) return false;
var veh = Program.ControlledVehicle;      if (veh == null) return false;
Part? part = FindPart(veh, settings.TargetPartId);  // walk veh.Parts.Parts + p.SubParts by Id
if (part == null) return false;

double4x4 vehMat = veh.GetMatrixAsmb2Ego(cam);
double3   pPos   = part.PositionEgo(in vehMat);             // pose WITHOUT the part's own scale
doubleQuat pRot  = part.Asmb2Ego(veh.Asmb2Ego);

float4x4 partEgo = float4x4.CreateFromQuaternion(floatQuat.Pack(in pRot))   // rotate…
                 * float4x4.CreateTranslation(float3.Pack(in pPos));        // …then translate
const float d2r = MathF.PI/180f;
float4x4 userRot = float4x4.CreateRotationX(rx*d2r) * float4x4.CreateRotationY(ry*d2r) * float4x4.CreateRotationZ(rz*d2r);
float4x4 userT   = float4x4.CreateTranslation(new float3(ox, oy, oz));
float4x4 scale   = float4x4.CreateScale(widthMeters, heightMeters, 1f);
// v_local → scale → userRot → userT → partEgo → ego
model = scale * userRot * userT * partEgo;                 // then mvp = model * cam.MVP.viewProjection
return true;
```
- **Crucial:** pull `PositionEgo` + `Asmb2Ego` separately — do **not** use `part.MatrixAsmb2Ego` (it bakes in the part's scale, which would corrupt your width/height knobs).
- Depth: `DepthTestWrite` → occludes correctly against the opaque scene.

### 6.2 Camera-locked billboard (new)
Ego space is camera-centered; the view matrix applies camera rotation. To pin the quad in the camera's view, build the model directly in **view space** and skip `view`:
```csharp
// view space: camera at origin looking down ±Z (verify sign in-engine; KSA projection handedness).
float4x4 scale  = float4x4.CreateScale(widthMeters, heightMeters, 1f);
float4x4 place  = float4x4.CreateTranslation(new float3(screenOffsetX, screenOffsetY, -distance));
model = scale * place;                                      // then mvp = model * cam.MVP.projection
```
- The quad faces the camera by construction (it lies in a view-space plane). `mvp = model * cam.MVP.projection` (NOT `viewProjection`).
- Depth: default `NoDepthTest` (always-on-top HUD). If `AlwaysOnTop == false`, use `DepthTestWrite` and the quad will be occluded by anything nearer than `distance`.
- **Implementation note:** verify the forward-axis sign (`-distance` vs `+distance`) empirically the first time — flip if the quad renders behind the camera. This is the single most likely first-run surprise for billboard mode.

---

## 7. Launch UX (popup → mode buttons → tailored form)

These dialogs render in the **main** ImGui context (the product-owner objection is to the *terminal* window being visible, not to a config dialog). Implement in `InWorldLaunchUI`, driven from the purrTTY menu (§10) and/or a hotkey.

1. **Launch popup** (`ImGui.OpenPopup` modal): title "In-World Terminal", two buttons:
   - `[ Anchor to Vehicle Part ]` → opens the **Part form**.
   - `[ Camera Billboard ]` → opens the **Billboard form**.
   - (Optional third: a shell picker, or default to the configured default shell.)
2. **Part form:** target-part selector (combo over `Program.ControlledVehicle.Parts.Parts` + each `p.SubParts`, showing `Part.Id`/`DisplayName`); `Offset X/Y/Z` (DragFloat, meters); `Rotation X/Y/Z` (DragFloat, degrees); `Width/Height` (DragFloat, meters); `[Apply]` / `[Cancel]`. Apply → `InWorldTerminalManager.Enable(partConfig)`.
3. **Billboard form:** `Distance` (meters); `Screen offset X/Y`; `Width/Height` (meters); `Always on top` (checkbox); `[Apply]` / `[Cancel]`.
4. **Live preview:** because the model matrix is recomputed every frame from live settings, editing the form updates the quad **instantly** (no re-anchor / no GPU recreation). Keep the form open for tweaking; "Apply" just persists + enables.
5. Persist on Apply via `InWorldSettings.Save()`. A "Reconfigure" menu item re-opens the popup for an already-enabled quad.

---

## 8. Input & focus (must-have)

- **`InWorldFocus`**: `{ NotFocused, Focused }`, `IsFocused` convenience. Port from old branch.
- **Click-to-focus:** `QuadPicker` (§5.8).
- **Keyboard forwarding (recommended approach — reuse existing encoder):** when `InWorldFocus.IsFocused`, read key/char events from the **main** `ImGui.GetIO()` and encode to the dedicated session, reusing the exact logic in `purrTTY.Display/Ghostty/TerminalWindow.Input.cs` (named-key encode via `Surface.EncodeKey`; printable text via the ImGui character queue with surrogate pairing; Ctrl/Alt chords via the engine encoder; AltGr handling). **Refactor** that logic out of `TerminalWindow.Input.cs` into a reusable `TerminalInputEncoder` that takes an `ITerminalSurface` + the ImGui IO and emits PTY bytes — then both the 2D window and the in-world terminal call it. This is the clean-reuse path and avoids the old branch's GLFW-postfix forwarding into the secondary context.
  - *Alternative (old branch technique, documented for completeness):* postfix `ImGuiBackendGlfwImpl.OnKey/OnChar/UpdateKeyModifiers` to also push events into the **secondary** context IO, and run the input-encode inside the secondary context. More moving parts; prefer the main-IO reuse path.
- **Game-key gating:** extend the existing `Patch01` (prefix on `KSA.Program.OnKey`, today gated on `GhosttyTerminalController.IsAnyTerminalActive`) to also suppress when `InWorldFocus.IsFocused` — so keystrokes typed at the quad never leak to vehicle/camera controls. Preserve the existing **press-gated release-forwarding** model (gotcha 21): a key pressed while the in-world terminal was focused must have its release swallowed too (no F-key toggle leak), while a key pressed game-side then released after focus moved is still released (no stuck controls).
- **Hotkey guard:** the in-world terminal has no ImGui text field, so `Patch03_HotkeyGuard`'s `WantTextInput` check won't fire for it. Extend the guard (or the toggle-hotkey suppression) to also consider `InWorldFocus.IsFocused`, so game hotkeys don't fire while typing at the quad.
- **Focus arbitration:** in-world focus is mutually exclusive with 2D-window focus and with the game world (clicking elsewhere unfocuses; Esc unfocuses). Since the 2D window is hidden in this mode, in practice only the quad competes for the keyboard.

---

## 9. Optional: mouse → terminal cell mapping (scoped-in stretch)

We already compute the ray-quad intersection; extending it to cell coordinates is bounded:
1. In `InWorldQuad.TryRaycast`, return the hit's **barycentric → local UV** (the quad spans local `[-0.5,0.5]²`; map hit point to `u,v ∈ [0,1]`).
2. Map `u,v` through the terminal's rendered sub-rect within the texture (the UV-rect knobs) to **pixel coords**, then divide by cell metrics → `(col, row)`.
3. Feed to `Surface.EncodeMouse` (neutral `TerminalMouseEvent` with the cell + button/motion), reusing `TerminalWindow.Input.cs`'s app-mouse path (gotchas 10/11: neutral button remap, integer-cell-center synthesis, Shift bypass, press-gated release).
**Cut criterion:** if the UV→cell mapping or the app-mouse plumbing balloons, ship click-to-focus only (§8) and defer this. It is explicitly optional.

---

## 10. Menu & toggle integration

- Add an **"In-World Terminal"** section to the existing purrTTY menu content in `purrTTY.GameMod/TerminalMenus.cs` (`DrawMenuContent()`): `Enable / Disable In-World Terminal`, `Reconfigure…` (re-opens the launch popup), and a `New In-World Session` if we later allow re-spawning the dedicated shell.
- The menu is already surfaced two ways (the `[ModMenuEntry("purrTTY")]` attribute and the `Program.DrawProgramMenusHook()` postfix fallback). **Use these** — do **not** resurrect the old branch's fragile `DrawMenuBar` IL transpiler (`Patch02` on the old branch). The current codebase already uses the supported `DrawProgramMenusHook` hook; this was the old branch's single real drift risk and it's already solved here.
- Optional dedicated toggle hotkey (default distinct from the 2D terminal's), wired in `TerminalMod.OnAfterUi` like the existing toggle.

---

## 11. Lifecycle & threading (hard rules)

- **Build GPU resources in `OnFullyLoaded`** (`[StarMapAllModsLoaded]`) or later — the renderer is live there; it is **not** in `[StarMapImmediateLoad]` (`Program.GetRenderer()` unsafe; `Program.Instance` null).
- **Everything is main-thread.** `BuildFrame()` (tick), GPU resource creation, command recording, and the `RenderMainPass` postfix all run on the single game/render thread (`OnAfterUi` + the render loop). No cross-thread sharing; no locks needed for the postfix statics.
- **Toggle-off ordering:** clear the postfix-read `Active` flag **before** disposing GPU resources; an in-flight frame must not dereference freed handles.
- **Fail-safe:** wrap `PerFrameRenderer.Frame` and `InWorldQuad.RecordDraw` in try/catch and **disable the feature on first failure** — a render-loop exception otherwise spams every frame and can corrupt Vulkan state.
- **Bounded session:** the dedicated session is ticked every frame via `BuildFrame` (gotcha 18 — an unticked surface grows its PTY inbox unbounded).
- **Frames in flight = 2** (`Renderer.MaxFramesInFlight`); the `PerFrameRenderer` fence ring of 2 matches this.

---

## 12. Risks & gotchas (the ones that bite)

1. **Bind the quad pipeline to `Program.OffScreenPass`, NOT `Program.MainPass`.** MainPass is the 1-bit swapchain pass; the scene runs in the MSAA offscreen pass. Wrong pass passes pipeline-creation validation but makes depth silently misbehave (quad always paints on top). *(Old commit `5be1aad` was exactly this fix.)*
2. **`RasterizationSamples = Program.OffScreenPass.SampleCount`** on the quad pipeline — must match the scene framebuffer's MSAA. (Separate from the terminal **offscreen target**, which is `_1Bit`.)
3. **Reverse-Z.** Use `RenderingPresets.ReverseZDepthStencil.DepthTestWrite` for occluding (part) mode; forward-Z presets look right only when the quad is nearest.
4. **Texture format `R8G8B8A8UNorm`, never `*Srgb`.** `UnlitMesh.frag` calls `gammaToLinear()` and ImGui writes gamma-encoded bytes; an SRGB source double-decodes and renders visibly dark.
5. **Shared-atlas hack** in the secondary backend: null `drawData.Textures` during its `Render`, and catch `ImException` from `GetTexID` (skip that one frame). Without it: `DescriptorPoolInvalidOperationException` and/or a crash on a glyph rasterized-but-not-yet-uploaded.
6. **`required` CreateInfo fields** — fill every `required` field of `ImGuiBackendVulkanImpl.CreateInfo` (compiler-enforced) and keep `DescriptorPoolSize ≥ 256`.
7. **Keep `RecordDraw` and `TryRaycast` model matrices identical** — divergence makes picking land off-target silently.
8. **Billboard forward-axis sign** — verify `±distance` in-engine on first run (§6.2).
9. **Menu hook** — use `DrawProgramMenusHook` postfix (already in the codebase), not the old `DrawMenuBar` transpiler.
10. **Version drift** — the old branch targeted ~2026-05-15; current is `2026.6.9.4750` (2026-06-27, ~6 weeks). All render-path symbols re-verified present this run (§4). The only thing that drifted was the menu approach (already handled) and the ImGui wrapper rename `Brutal.ImGui` → `Brutal.ImGuiApi` (multi-context API confirmed intact).
11. **Generation gate** (§5.5) — skip re-rendering the offscreen texture when neither content (`TerminalFrame.Generation`) nor blink phase changed, or the quad needlessly re-renders the terminal every game frame.

---

## 13. Deferred / future

- **Kitty graphics on the quad.** The 2D path uses `ImageTextureCache` (render-thread `SimpleVkTexture` + `ImGuiBackend.Vulkan.AddTexture` → `ImTextureRef`) + `KittyImageRenderer` drawing into the draw list. To support images on the quad, give `InWorldTerminalRenderer` its own `ImageTextureCache`/`KittyImageRenderer` pass into the secondary context. Bounded but additive — defer past v1.
- **Multiple in-world quads / per-quad sessions.** v1 is a single quad + single dedicated session.
- **Resize the offscreen texture at runtime** (`OffscreenRenderTarget.Resize` exists but is unwired) — only needed if we expose a texture-resolution control.

---

## 14. Phased implementation plan (ordered, each phase builds)

Mirror the old branch's proven phasing; each phase compiles and is independently verifiable.

- **Phase 0 — Project setup.** Add `purrTTY.GameMod/InWorld/` folder. Add the KSA **render** DLL references to `purrTTY.GameMod.csproj` (`<Private>false</Private>`, `HintPath` via `$(KSAFolder)`), mirroring the block in `purrTTY.Display.csproj`: `Brutal.VulkanApi`, `Brutal.VulkanApi.Abstractions`, `Planet.Render.Core` (→ `Core`/`RenderCore` namespaces: `Renderer`, `RenderPassState`, `SimpleVkTexture`, `VkUtils`), `Brutal.Core.Memory` (`ByteSize`), `Brutal.Core.Numerics`, `Brutal.GlfwApi` (for `GlfwKey` in input gating), and ensure `KSA`, `Brutal.ImGuiApi` are present. Confirm `dotnet build purrTTY.GameMod` still succeeds.
- **Phase 1 — Offscreen target.** `OffscreenRenderTarget`; build it in a throwaway init path; assert it constructs and the color image view is non-null. (Old phases 1-2.)
- **Phase 2 — Secondary ImGui context + backend.** `OffscreenImGuiContext` + `OffscreenImGuiBackend`; under `ctx.With`, run a trivial `NewFrame`/`Render` and confirm no validation errors. (Old phases 3-4.)
- **Phase 3 — Per-frame offscreen render.** `PerFrameRenderer` with the cmd/fence ring + shared-atlas hack; `BuildUi` draws a placeholder `ImGui.Text("hello")`. **Verify the texture has content** by temporarily showing it in a 2D ImGui window via `ImGuiBackend.Vulkan.AddTexture(offscreen.Sampler, offscreen.ColorImageView)` + `ImGui.Image(...)` (this is the `Viewport.DrawImGui`/`CanvasRenderer` pattern — handy debug, then remove). (Old phase 5.)
- **Phase 4 — Dedicated terminal content.** `InWorldTerminalRenderer`: create the dedicated `SessionManager`+session, build `FrameFonts`, and make `BuildUi` call `FrameGridRenderer.Render(session.Surface.BuildFrame(), …)`. Verify a live shell renders into the texture (still viewed via the temporary 2D image).
- **Phase 5 — The quad (part-anchored).** `InWorldQuad` + `RenderMainPassPatch`; bind to `OffScreenPass`, reverse-Z `DepthTestWrite`, MSAA-matched, cull-none; compute the part-anchored ego MVP (§6.1). **Verify occlusion**: fly the camera so a vehicle part passes in front of the quad — the part must occlude it, and the quad must occlude parts behind it. (Old phases 6A/6B + the `5be1aad` z-fix.)
- **Phase 6 — Hide the 2D window / self-contained.** Confirm no on-screen ImGui terminal window is required: the quad shows the dedicated session with all 2D terminal windows closed. Remove the temporary debug 2D image.
- **Phase 7 — Billboard mode + launch UX.** Add the `NoDepth` pipeline variant + the view-space billboard MVP (§6.2); build `InWorldLaunchUI` (popup → mode buttons → tailored forms, §7) and `InWorldSettings` persistence. Verify both anchor modes and live-edit.
- **Phase 8 — Input & focus (must).** `InWorldFocus` + `QuadPicker` (click-to-focus); refactor `TerminalInputEncoder` out of `TerminalWindow.Input.cs` and drive it from in-world focus; extend `Patch01`/hotkey guard for in-world focus (§8). Verify typing at the quad reaches the shell and never leaks to game controls.
- **Phase 9 — (Optional) mouse → cell mapping** (§9). Ship only if it stays small.
- **Phase 10 — Lifecycle hardening + menu.** Wire `InWorldTerminalManager` into `TerminalMod` (`OnFullyLoaded`/`OnAfterUi`/`OnUnload`); add menu items (§10); verify toggle-off clears `Active` before dispose, and StarMap reload re-patches cleanly. Run the full test suite (`dotnet test purrtty.slnx --nologo -v quiet`) — must stay quiet/green.

---

## 15. Documentation to update when implementing (Instruction Maintenance Mandate)

This plan changes project structure and the backend/frontend seam usage, so on implementation update **in the same work item**:
- **`CLAUDE.md`** — add the in-world quad to the architecture/feature status; note `purrTTY.GameMod` now carries render-pass code + Vulkan refs.
- **`docs/code-navigation.md`** — add the `purrTTY.GameMod/InWorld/` file map; note the extracted `TerminalInputEncoder`; note `FrameGridRenderer` is now driven by two presentations (2D window + in-world offscreen).
- **`docs/gotchas.md`** — add the load-bearing render gotchas (OffScreenPass binding, MSAA SampleCount match, reverse-Z, R8G8B8A8UNorm gamma, shared-atlas hack, clear-flag-before-dispose, generation gate).
- **`docs/how-to.md`** — add a "render the terminal to a 3D quad" recipe pointing at `InWorldTerminalManager`.
- Keep the `ksa` skill `quad.md` as the canonical destination-half reference (already accurate; cite it rather than duplicating).

---

## 16. Quick reference — old-branch commit roadmap (the proven phase order)

```
fcc97ce phase 1 scaffolding for render-to-texture terminal
33e0266 phase 2 off-screen render target
b6e1685 phase 3 secondary ImGui context
e51b710 phase 4 secondary ImGui Vulkan backend
229bb19 phase 5 per-frame off-screen render loop
4c20b6b phase 6A in-world quad with Harmony injection
233c1b9 phase 6B SubPart material override
1c4ef2d phase 7A click-to-focus and keyboard routing
5be1aad fix(render): bind to OffScreenPass for proper depth testing   ← the z-order fix
527269e quad details
```
(Branch `feature/render2text-opus-plan` at `C:\Users\Alex\repos\meow-sci\purrtty_ingame`.)
