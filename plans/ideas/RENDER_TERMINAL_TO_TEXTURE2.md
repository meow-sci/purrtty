# problem

this repo is purrTTY, a KSA (kitten space agency) game mod which provides an ImGui based frontend ontop of a custom terminal emulator.

this is all working perfectly fine in this repo right now

what I want to create is the ability to render an ImGui window to an off-screen target/texture and then render that texture preferably to an in-game 3d surface, or, just projected in 3d space if possible.

i am led to believe this should be possible from developers of the game

note that the ImGui in-game is a 1:1 with real ImGui, but uses a custom C# binding so the syntax varies slightly.  You can use the imgui skill for vanilla C based ImGui references, but you will need to look at the mods existing code to see how the in-game "BRUTAL ImGui" syntax looks

Also the entire KSA game source code which is C# based is decompiled and available under `decomp/ksa` folder

The `decomp/ksa/Content/Core` folder holds non-DLL game assets like XML data files and fragment and vertex shaders, texture files, game audio, etc.

The game considers all content to be a form of "mod" and "Core" is the built-in mod with all the game code.

The game is not based on any game engine, KSA itself is a bespoke game engine purpose built for the game to be a completely custom and bespoke space flight simulator game

It uses a "framework" called "BRUTAL" which is primarily a custom high-level language C# binding to Vulkan, which is the underlying rendering technology used exclusively.  The BRUTAL decompiled sources are included under `decomp/ksa` as well.

# goals

do a very deep dive and thorough analysis of KSA/BRUTAL, looking into the BRUTAL Vulkan rendering and BRUTAL ImGui bindings in great details

determine how to take a given ImGui window and render it to an off-screen texture and then render that texture in-game preferably on a game object surface (e.g. on a SubPart mesh, if possible)

this can be done any way necessary that you can determine, including:

- preferably using game code and APIs directly from our mod
- using harmony if necessary for runtime patching/interception (avoid if possible)
- modifying or adding shaders (also avoid if possible, but if absolutely required, that is acceptable)

# instructions

make a detailed implementation plan with highly specific, unambiguous detail about the solution and each task should contain fine very detailed information, examples, code references, code sampples etc such that future coding agents can be provided the task largely in isolation and have everything it needs to unambiguously implement the task.  this may include references back to decompiled sources etc if it would be necessary for the task to have fine details of it.

# plan

## 0. Executive Summary

The end-to-end pipeline we will build:

```
[Terminal state]
  → (secondary ImGuiContext)
     ImGui.NewFrame → BuildUi → ImGui.Render → ImDrawData
  → (off-screen ImGuiBackendVulkanImpl instance)
     RenderDrawData(drawData, ourCmdBuf) into ourRenderPass + ourFramebuffer
  → off-screen color attachment VkImage (VkImageView)
  → transition COLOR_ATTACHMENT_OPTIMAL → SHADER_READ_ONLY_OPTIMAL
  → [PATH A] custom Vulkan quad pipeline (UnlitMesh.vert/frag) draws our texture as a flat 3D quad
  → [PATH B] SubPart material override: register imageView as bindless texture, swap MaterialData.AlbedoTexture for a target Part
```

Critical enablers discovered in the decomp:

- **`ImGuiBackendVulkanImpl.RenderDrawData(ImDrawDataPtr, CommandBuffer)`** (`decomp/ksa/KSA/ImGuiBackendVulkanImpl.cs:657`) — the existing BRUTAL Vulkan ImGui backend accepts **explicit `ImDrawData` and a target command buffer**. We can instantiate a **second** `ImGuiBackendVulkanImpl` with our own `VkRenderPass` and reuse the same pipeline machinery to render into our off-screen framebuffer. No new shaders are required for the ImGui draw path.
- **`ImGui.CreateContext()`** (`decomp/ksa/Brutal.ImGuiApi/ImGui.cs:5401`) — multiple ImGui contexts are supported. We isolate our off-screen UI in a secondary context so we don't interfere with the game's main UI draw list and so the input state of our terminal doesn't fight with the game's screen-space ImGui.
- **`RenderTarget` / `Framebuffer`** (`decomp/ksa/KSA/RenderTarget.cs:22`, `decomp/ksa/KSA/Framebuffer.cs:79`) — these BRUTAL helpers create VMA-allocated color/depth attachments and a `VkFramebuffer` bound to a `VkRenderPass`. No new low-level Vulkan code required; we reuse this.
- **`GpuTextureSystem.BindTexture(SimpleVkTexture)`** (`decomp/ksa/KSA/GpuTextureSystem.cs:80`) — returns a `bindlessHandle` we can stuff into `MaterialData.AlbedoTexture` (`decomp/ksa/KSA/MaterialData.cs:1-30`) to make the texture sampleable from any KSA mesh shader.
- **`UnlitMesh.vert/frag`** (`decomp/ksa/Content/Core/Shaders/Mesh/UnlitMesh.{vert,frag}`) — a 30-line vertex shader and 18-line fragment shader, push constants only (`mat4 worldViewProjMatrix`), single `sampler2D` at binding 0. Perfect for both the quad fallback (Path A) and the SubPart approach (Path B). **No shader authoring is required.**
- **StarMap mod lifecycle** — `[StarMapImmediateLoad]`, `[StarMapAllModsLoaded]`, `[StarMapBeforeGui]`, `[StarMapAfterGui]`, `[StarMapUnload]` give us early Harmony patching, late init when game is ready, and a per-frame hook that runs **after** `ImGui.Render()` in the main loop (see `Program.OnFrame` in `decomp/ksa/KSA/Program.cs:1896-1943`, line 1921 is the main `ImGui.Render()`).
- **Cursor + Ray** — `Cursor.InputRay` (`decomp/ksa/KSA/Cursor.cs:40-42`, populated each frame in `Program.OnFrame` line 1934) and `Ray.RaycastMollerTrumbore` (`decomp/ksa/KSA/Ray.cs:54`) provide everything we need to pick a quad/SubPart hit in world space and compute a UV.

Decisions baked into the plan from user clarifications:

- **Primary surface**: SubPart mesh on a vessel (Path B). Quad in 3D world space (Path A) is the fallback **and** the dev/test target — we build Path A first so we have a working render-to-texture pipeline before we touch the part/material system.
- **Interaction**: display-only is the MVP, but keyboard input MUST work. We provide a click-to-focus mechanism (mouse picking via raycast) as the minimum interactive path, and document a full mouse+keyboard plan as an optional Phase 7B.
- **Coexistence**: keep the screen-space ImGui terminal. Add a toggle so the in-world surface can be enabled/disabled independently. Both can be on at once and share the same underlying terminal session.
- **Shaders**: do **not** add or modify shaders. `UnlitMesh.{vert,frag}` are reused via their existing compiled SPIR-V already shipped with the game. If we discover at integration time that the existing pipeline cannot be reused via the public KSA API, we **do not** fall back to adding shaders; we instead reuse `ImGuiBackendVulkanImpl`'s internal pipeline for the quad (its SPIR-V is embedded in the class — `ShaderSource.Vert` and `ShaderSource.Frag` in `decomp/ksa/KSA/ImGuiBackendVulkanImpl.cs:157-200`) or accept the SubPart-only path.

---

## 1. Phase Map

| Phase | Goal | Risk | Reversible? |
|---|---|---|---|
| 1. Project scaffolding & feature flag | Add new namespace, build wiring, dev hotkey, gated init | Low | Yes |
| 2. Off-screen render target | A `VkImage` + `VkImageView` + `VkFramebuffer` + `VkRenderPass` we can draw into and sample from | Low | Yes |
| 3. Secondary ImGui context | A separate `ImGuiContext` running the terminal UI without polluting the main context | Medium | Yes |
| 4. Secondary ImGui Vulkan backend | A second `ImGuiBackendVulkanImpl` instance bound to our render pass | Medium | Yes |
| 5. Per-frame off-screen render loop | Wire 2,3,4 together every frame; image transitions; verify with screen-space `ImGui.Image()` debug viewer | Medium | Yes |
| 6A. In-world quad (PRIMARY DEV PATH) | Custom Vulkan pipeline using `UnlitMesh` shaders, draws a textured quad in world space | High | Yes |
| 6B. SubPart material override | Swap a chosen SubPart's material at runtime so it samples our texture | High | Mostly — needs a careful save/restore |
| 7A. Click-to-focus + keyboard routing | Minimum required interaction: mouse picking sets focus, keyboard goes to terminal | Medium | Yes |
| 7B. Full mouse routing into ImGui | Mouse pos + buttons + scroll routed via UV into secondary ImGui | Medium | Yes |
| 8. Settings, polish, cleanup | Hotkeys, modal config, lifecycle, dispose, resize | Low | Yes |

A future coding agent picking up any one phase should be able to work from that phase's section in isolation.

---

## 2. Required reading map (for every future agent)

Decompiled file references that **every** agent in any phase below will need to consult. Treat these as read-only canonical references.

| File | Why |
|---|---|
| `decomp/ksa/KSA/ImGuiBackendVulkanImpl.cs` | Render backend used by KSA itself; we'll mirror its API surface for our secondary instance. Critical methods: ctor (`CreateInfo` struct at line 18), `RenderDrawData(ImDrawDataPtr, CommandBuffer)` at line 657, `AddTexture` at line 1052. Embedded SPIR-V at lines 157-200. |
| `decomp/ksa/KSA/Framebuffer.cs` | Base framebuffer class with VMA-allocated attachments. `BuildFramebuffer(VkRenderPass)` line 51, `CreateAttachment` line 79, `Dispose` line 142. |
| `decomp/ksa/KSA/RenderTarget.cs` | Concrete off-screen target, ctor line 22, `BuildAttachments` (builds color + depth) referenced from ctor. |
| `decomp/ksa/KSA/OffscreenTarget.cs` | Reference for MSAA off-screen with multiple attachments; shows how the engine's own off-screen pass is built. |
| `decomp/ksa/KSA/Program.cs` | Game loop. `OnFrame` line 1896, `OnPreRender` line 1974, `Render` line 1996, `RenderGame` line 3768. Static accessors: `OffscreenTarget` line 356, `MainPass` line 354, `OffScreenPass` line 355. Camera: `GetMainCamera` line 470, `GetHoveredCamera` line 473, `Viewports` line 384. `ControlledVehicle` line 239. |
| `decomp/ksa/KSA/Camera.cs` | `MVP` line ~80 (`ViewProjection MVP`), `PositionEcl`, `WorldRotation`, `GetForward`, `ScreenToEgoRay`. |
| `decomp/ksa/KSA/Cursor.cs` | `InputRay` static, `UpdateInputRay(Camera)` line 40. |
| `decomp/ksa/KSA/Ray.cs` | `RaycastMollerTrumbore` line 54 (triangle pick). |
| `decomp/ksa/KSA/Part.cs` | `IsSubPart` line 564, `MatrixAsmb2ParentAsmb` line 381, `Scale` line 403. `Modules.Get<MeshViewModule>()` enumerator. |
| `decomp/ksa/KSA/MeshViewModule.cs` | Holds `MeshReference MeshView`. |
| `decomp/ksa/KSA/MeshReference.cs` | Holds `SimpleVkMesh DeviceMesh`, `MeshAsset HostMesh`, `BoundingSphereRadius`. |
| `decomp/ksa/KSA/PbrMaterialReference.cs` | The XML-bound material reference type. `DiffuseReference`, `NormalReference`, `PBRMap`, `EmissiveMap`. |
| `decomp/ksa/KSA/MaterialData.cs` | GPU-side material struct. Layout: `int AlbedoTexture; int NormalTexture; int RoughMetallicAOTexture; int Sampler; float4 AlbedoColor; float4 RoughnessMetalScale; float4 ExtraData; int EmissiveTexture; ...` |
| `decomp/ksa/KSA/GpuMaterialSystem.cs` | `SendToBuffer(MaterialData)` line ~75 returns an int handle into a GPU SSBO. |
| `decomp/ksa/KSA/GpuTextureSystem.cs` | `BindTexture(SimpleVkTexture)` line 80, `SamplerClampHandle` line 24, `SamplerRepeatHandle` line 26. |
| `decomp/ksa/Content/Core/Shaders/Mesh/UnlitMesh.vert` | 30 lines. Vertex layout: `vec3 inPos` (location 0), `vec2 inUV` (location 1). Push constants: `mat4 worldViewProjMatrix`. |
| `decomp/ksa/Content/Core/Shaders/Mesh/UnlitMesh.frag` | 18 lines. `layout (binding = 0) uniform sampler2D samplerColorMap`. Single texture sample, gamma-to-linear, alpha 1.0. **NOTE: alpha is hardcoded to 1.0 — fine for opaque terminal.** |
| `decomp/ksa/KSA/ImGuiBackendGlfwImpl.cs` | How GLFW input events become `ImGuiIO.AddKeyEvent`/`AddMousePosEvent` (lines 436, 461, 507). We mirror this pattern when routing input into our secondary context. |
| `purrTTY.GameMod/TerminalMod.cs` | Existing StarMap entry point. We will add our new feature inside the same class (lifecycle hooks already exist). |
| `purrTTY.GameMod/Patcher.cs` | Pattern for Harmony patches (transpiler at line 30, prefix at line 86). |
| `purrTTY.Display/Controllers/TerminalController.cs` | The screen-space terminal renderer. `Render()` at line 404, `ImGui.Begin("Terminal", ...)` at line 487. Phase 5/6 wraps this in our off-screen context. |

---

## 3. New file layout in `purrTTY.GameMod/`

We isolate every new piece under a new namespace `purrTTY.GameMod.InWorld` so a future agent can grep one directory.

```
purrTTY.GameMod/
├── InWorld/
│   ├── InWorldTerminalManager.cs        // Top-level coordinator: lifecycle, toggle, frame loop
│   ├── OffscreenContext.cs              // Secondary ImGuiContext + font atlas sharing
│   ├── OffscreenRenderTarget.cs         // VkRenderPass + RenderTarget + VkImageView + barriers
│   ├── OffscreenImGuiBackend.cs         // Wraps our own ImGuiBackendVulkanImpl instance
│   ├── PerFrameRenderer.cs              // The per-frame orchestrator (NewFrame/Render/Submit)
│   ├── Display/
│   │   ├── IWorldDisplay.cs             // Interface: Update, RecordDraw, Dispose, HitTest
│   │   ├── QuadDisplay.cs               // Path A: custom quad pipeline using UnlitMesh shaders
│   │   ├── SubPartDisplay.cs            // Path B: material swap on a chosen SubPart
│   │   └── DisplayPicker.cs             // Raycasts; converts world hit → UV
│   ├── Input/
│   │   ├── InWorldFocus.cs              // Which surface (if any) currently has focus
│   │   └── InputRouter.cs               // Routes events into the secondary ImGui IO
│   ├── Patches/
│   │   ├── FramePatches.cs              // Harmony patches that give us a command-buffer hook (if needed)
│   │   └── InputCapturePatches.cs       // Optional: suppress original input when focused
│   └── Settings/
│       └── InWorldSettings.cs           // Toggle key, surface position, target part name, etc.
```

A single integration site in `TerminalMod.cs` constructs `InWorldTerminalManager` in `OnFullyLoaded()` and calls into it from the existing `[StarMapAfterGui]` callback.

---

## 4. Phase 1 — Project scaffolding & feature flag

**Goal**: end this phase with a no-op `InWorldTerminalManager` that is constructed, hot-keyed, and gated behind a feature flag, but does no rendering. Verifies that the new code is reachable without breaking the existing terminal.

### 1.1 Files to create

- `purrTTY.GameMod/InWorld/InWorldTerminalManager.cs`
- `purrTTY.GameMod/InWorld/Settings/InWorldSettings.cs`

### 1.2 `InWorldSettings`

```csharp
namespace purrTTY.GameMod.InWorld.Settings;

public sealed class InWorldSettings
{
    public bool Enabled { get; set; } = false;                       // Master toggle
    public string TargetPartName { get; set; } = "";                 // If set, try SubPart path; else quad path
    public int TextureWidth  { get; set; } = 1024;
    public int TextureHeight { get; set; } = 1024;
    public float QuadWidthMeters  { get; set; } = 1.6f;
    public float QuadHeightMeters { get; set; } = 1.0f;
    public float QuadDistanceMeters { get; set; } = 2.0f;            // For quad path: distance in front of camera at spawn
    public ImGuiKey ToggleKey { get; set; } = ImGuiKey.F11;          // separate from main terminal toggle
}
```

Persist via the same `ThemeConfiguration` (or a sibling `InWorldConfiguration`) mechanism used by `ToggleHotkeyBinding` (`purrTTY.GameMod/ToggleHotkeyBinding.cs` for the persistence pattern, lines around 326-342 of `TerminalMod.cs`).

### 1.3 `InWorldTerminalManager` skeleton

```csharp
namespace purrTTY.GameMod.InWorld;

public sealed class InWorldTerminalManager : IDisposable
{
    private readonly InWorldSettings _settings;
    private bool _initialized;
    private bool _disposed;

    public InWorldTerminalManager(InWorldSettings settings) { _settings = settings; }

    // Called once when the game is fully loaded and the renderer is live.
    public void Initialize() { /* phase 2+ populates this */ _initialized = true; }

    // Called every frame from [StarMapAfterGui].
    // dt is the same dt the game passes to its own AfterGui callbacks.
    public void OnAfterGui(double dt)
    {
        if (!_settings.Enabled || !_initialized || _disposed) return;
        // phase 5 populates this
    }

    public void Toggle() => _settings.Enabled = !_settings.Enabled;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // phase 2+ disposes resources
    }
}
```

### 1.4 Wire into `TerminalMod`

In `purrTTY.GameMod/TerminalMod.cs`:

- Add a private field: `private InWorldTerminalManager? _inWorld;`
- In `OnFullyLoaded()` (around line 161, after `InitializeTerminal()`):
  ```csharp
  _inWorld = new InWorldTerminalManager(InWorldSettings.LoadOrDefault());
  _inWorld.Initialize();
  ```
- In `OnAfterUi(dt)` (line 93), at the bottom, after the existing render:
  ```csharp
  _inWorld?.OnAfterGui(dt);
  ```
- In `Unload()` (line 182):
  ```csharp
  _inWorld?.Dispose();
  _inWorld = null;
  ```
- Inside the existing hotkey check region (lines 107-117), add a parallel check for `_settings.ToggleKey` that calls `_inWorld?.Toggle()`.

### 1.5 Acceptance test for Phase 1

- Game launches normally.
- Pressing F11 logs "in-world terminal enabled/disabled" via `ModLog`.
- No visual changes yet.
- No crashes when toggling.

---

## 5. Phase 2 — Off-screen render target

**Goal**: own a `VkRenderPass` + `VkFramebuffer` (color + depth) + a sampleable `VkImageView`. After this phase the asset exists in memory but nothing renders into it yet.

### 2.1 `OffscreenRenderTarget.cs`

```csharp
namespace purrTTY.GameMod.InWorld;

using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Brutal.Numerics;
using KSA;

public sealed class OffscreenRenderTarget : IDisposable
{
    public VkExtent2D Extent { get; private set; }
    public VkFormat   ColorFormat { get; }
    public VkFormat   DepthFormat { get; }
    public VkRenderPass RenderPass { get; private set; }
    public RenderTarget Target { get; private set; }              // BRUTAL/KSA helper

    public VkImage     ColorImage     => Target.Attachments[0].Image;
    public VkImageView ColorImageView => Target.Attachments[0].ImageView;

    private readonly Renderer _renderer;
    private bool _disposed;

    public OffscreenRenderTarget(Renderer renderer, int width, int height,
                                 VkFormat colorFormat, VkFormat depthFormat)
    {
        _renderer = renderer;
        ColorFormat = colorFormat;
        DepthFormat = depthFormat;
        Resize(width, height);
    }

    public void Resize(int width, int height)
    {
        // Idempotent: dispose old, create new.
        DisposeGpu();
        Extent = new VkExtent2D { Width = width, Height = height };
        RenderPass = CreateRenderPass();
        Target = new RenderTarget(_renderer, "purrTTY-Offscreen",
                                  Extent, ColorFormat, DepthFormat,
                                  depthSlices: 1, inMipLevels: 1);
        Target.BuildFramebuffer(RenderPass);
    }

    private VkRenderPass CreateRenderPass()
    {
        // Two attachments: color + depth.
        // Color: loadOp=Clear, storeOp=Store, initialLayout=Undefined,
        //        finalLayout=ColorAttachmentOptimal  (we will manually transition to ShaderReadOnlyOptimal
        //        after the pass; that gives us flexibility for re-rendering)
        // Depth: loadOp=Clear, storeOp=DontCare, both layouts DepthStencilAttachmentOptimal
        // One subpass, single color, single depth.
        // See decomp/ksa/KSA/RenderTarget.cs:CreateRenderPass() for the exact pattern KSA uses
        // (it builds equivalent VkAttachmentDescription / VkSubpassDescription / VkSubpassDependency).
        // Use the unsafe Vulkan-direct construction shown in decomp/ksa/KSA/Renderer.cs CreateRenderPass examples.
        // Pseudocode:
        var color = new VkAttachmentDescription
        {
            Format = ColorFormat,
            Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Clear,
            StoreOp = VkAttachmentStoreOp.Store,
            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.Undefined,
            FinalLayout = VkImageLayout.ColorAttachmentOptimal,
        };
        var depth = new VkAttachmentDescription
        {
            Format = DepthFormat,
            Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Clear,
            StoreOp = VkAttachmentStoreOp.DontCare,
            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.Undefined,
            FinalLayout = VkImageLayout.DepthStencilAttachmentOptimal,
        };

        // Subpass: 1 color (attachment 0), 1 depth (attachment 1).
        // Use the same shape as Brutal/KSA construct elsewhere - see decomp/ksa/KSA/Renderer.cs
        // for a worked example (search for `VkRenderPassCreateInfo`).
        // Return the created VkRenderPass.
        // ... (concrete unsafe code: allocate attachments[], subpasses[], dependency[],
        //      VkRenderPassCreateInfo, call _renderer.Device.CreateRenderPass)
        throw new NotImplementedException("See decomp/ksa/KSA/Renderer.cs for VkRenderPass build code");
    }

    public void DisposeGpu()
    {
        Target?.Dispose();
        Target = null!;
        if (RenderPass.VkHandle != 0)
        {
            _renderer.Device.DestroyRenderPass(RenderPass, null);
            RenderPass = default;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeGpu();
    }
}
```

**Notes for the implementing agent**:
- The `VkRenderPass` construction pattern is fully shown in `decomp/ksa/KSA/Renderer.cs` (search for `VkRenderPassCreateInfo` — there are multiple such call sites). The exact one to copy is the one that creates `_offscreenPass`. Mirror its attachment/subpass shape but with our formats and single sample.
- **Format choice**: `ColorFormat = VkFormat.R8G8B8A8Srgb` (matches `UnlitMesh.frag`'s gamma-to-linear). `DepthFormat = VkFormat.D32SFloat` (matches `_renderer.DepthFormat` default; cheaper than D24S8 on most GPUs).
- **Resize semantics**: when the user moves the surface, scales it, or we discover a different terminal grid sizing, call `Resize`. Idempotent: it must dispose old GPU resources first.
- **Threading**: `Resize` and `Dispose` must run on the main thread (the only thread that owns the Vulkan device).

### 2.2 Accessing the `Renderer`

The active `Renderer` is `Program.GetRenderer()` (`decomp/ksa/KSA/Program.cs:429`). Cache the result in `InWorldTerminalManager.Initialize()`; it is stable for the game session.

### 2.3 Acceptance test for Phase 2

- Calling `new OffscreenRenderTarget(renderer, 1024, 1024, R8G8B8A8Srgb, D32SFloat)` succeeds without throwing.
- The Vulkan validation layer (KSA enables it in debug builds — see `decomp/ksa/KSA/Renderer.cs`) prints zero errors.
- Disposing the manager releases the resources.

---

## 6. Phase 3 — Secondary ImGui context

**Goal**: a separate `ImGuiContext` we can `SetCurrentContext` to, drive `NewFrame`/`Render` on, and not interfere with the game's main UI.

### 3.1 Context strategy

**Important quirk discovered**: `ImGui.CreateContext(ImFontAtlasPtr sharedFontAtlas)` accepts a shared font atlas. The KSA main context already loaded our terminal `.iamttf` fonts (via `PurrTTYFontManager.LoadFonts()` — `purrTTY.Display/Rendering/PurrTTYFontManager.cs:34-118`). **Share the same atlas** so we don't duplicate font upload memory and so the same `ImFontPtr` handles continue to work.

```csharp
var mainCtx = ImGui.GetCurrentContext();
var mainIO  = ImGui.GetIO();
var sharedAtlas = mainIO.Fonts;                       // pointer-equivalent
_ctx = ImGui.CreateContext(sharedAtlas);
ImGui.SetCurrentContext(_ctx);
var myIO = ImGui.GetIO();
myIO.DisplaySize = new float2(width, height);
myIO.DeltaTime = 1f / 60f;
myIO.IniFilename = default;                          // disable layout persistence per-context
ImGui.SetCurrentContext(mainCtx);                    // always restore!
```

### 3.2 `OffscreenContext.cs`

```csharp
public sealed class OffscreenContext : IDisposable
{
    public ImGuiContextPtr Native { get; private set; }
    public Vector2i Size { get; private set; }

    public OffscreenContext(int width, int height)
    {
        var mainCtx = ImGui.GetCurrentContext();
        try
        {
            var sharedAtlas = ImGui.GetIO().Fonts;
            Native = ImGui.CreateContext(sharedAtlas);
            ImGui.SetCurrentContext(Native);
            var io = ImGui.GetIO();
            io.DisplaySize  = new float2(width, height);
            io.DeltaTime    = 1f / 60f;
            io.IniFilename  = default;                            // no .ini file
            io.MouseDrawCursor = false;
            // (Optional) Enable keyboard nav so cursor keys etc. still work even without focus signal
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            Size = new Vector2i(width, height);
        }
        finally { ImGui.SetCurrentContext(mainCtx); }
    }

    /// <summary>Runs <paramref name="action"/> with this context active and restores the previous on return.</summary>
    public void With(Action action)
    {
        var prev = ImGui.GetCurrentContext();
        ImGui.SetCurrentContext(Native);
        try { action(); }
        finally { ImGui.SetCurrentContext(prev); }
    }

    public void Resize(int width, int height)
    {
        With(() =>
        {
            ImGui.GetIO().DisplaySize = new float2(width, height);
            Size = new Vector2i(width, height);
        });
    }

    public void Dispose()
    {
        if (Native.VkHandle == 0) return;
        ImGui.DestroyContext(Native);
        Native = default;
    }
}
```

### 3.3 Hazards & rules

- **Never call ImGui functions without first setting the context.** This is the #1 footgun. Every per-frame action must use `With`.
- **Never `NewFrame` on a context that hasn't seen `EndFrame`/`Render` since the last `NewFrame`.** Track state defensively.
- **Font atlas building**: building the atlas (`atlas.Build()`) must happen exactly once per atlas, and the resulting font texture must be uploaded to GPU exactly once per backend. Since we share with the main context's atlas, the atlas is already built; we **do not** rebuild. Our backend will read the atlas's existing texture via `ImGuiIO.Fonts.TexID`.

### 3.4 Acceptance test for Phase 3

- Construct + dispose `OffscreenContext` repeatedly; no leaks, no crashes.
- Inside `.With()`, `ImGui.GetCurrentContext().VkHandle` equals `Native.VkHandle`. After exit, it equals the prior value.

---

## 7. Phase 4 — Secondary ImGui Vulkan backend

**Goal**: an `ImGuiBackendVulkanImpl` instance we control, bound to our `VkRenderPass`, which can consume an `ImDrawDataPtr` and record draw commands into a command buffer.

### 4.1 What we know

From `decomp/ksa/KSA/ImGuiBackendVulkanImpl.cs`:

- **`CreateInfo` struct** (line 18-37). Required fields: `DeviceEx Device`, `Queue GraphicsQueue`, `VkRenderPass RenderPass`, `int MinImageCount`, `int ImageCount`, `VkSampleCountFlags SampleCount`, `int DescriptorPoolSize`.
- **ctor** populates: a sampler (line 642), descriptor pool + layout (line 643), pipeline layout (line 644), shader modules from embedded SPIR-V (line 645), pipeline (line 646), command pool/buffer for texture uploads (line 647), main viewport renderer user data (line 648), multi-viewport support (line 649).
- **Per-call**: `RenderDrawData(ImDrawDataPtr, CommandBuffer)` at line 657 uploads vertex+index data into per-frame ring buffers, sets viewport/scissor, binds pipeline, then iterates draw commands.

### 4.2 `OffscreenImGuiBackend.cs`

```csharp
public sealed class OffscreenImGuiBackend : IDisposable
{
    public ImGuiBackendVulkanImpl Impl { get; }

    public OffscreenImGuiBackend(Renderer renderer, VkRenderPass renderPass,
                                 int minImageCount, int imageCount)
    {
        Impl = new ImGuiBackendVulkanImpl(new ImGuiBackendVulkanImpl.CreateInfo
        {
            Device              = renderer.Device,
            GraphicsQueue       = renderer.Graphics,
            RenderPass          = renderPass,
            SubPass             = 0,
            MinImageCount       = minImageCount,
            ImageCount          = imageCount,
            SampleCount         = VkSampleCountFlags._1Bit,
            DescriptorPoolSize  = 64,                       // plenty for our single window
            MinAllocationSize   = default,                  // accept default
        });
    }

    public void Render(ImDrawDataPtr drawData, CommandBuffer cmd) =>
        Impl.RenderDrawData(drawData, cmd);

    public void Dispose() => Impl.Dispose();
}
```

### 4.3 Hazards & rules

- **The backend installs itself into the current ImGui context.** `ImGuiBackendVulkanImpl`'s ctor sets `ImGui.GetMainViewport().RendererUserData` (line 648 of decomp) — meaning it writes into **whichever context is current at construction time**. **Therefore: construct the backend while the secondary context is active.**
  ```csharp
  _ctx.With(() =>
  {
      _backend = new OffscreenImGuiBackend(renderer, _target.RenderPass,
                                           minImageCount: 1, imageCount: 1);
  });
  ```
- **ImageCount = 1** is intentional. The backend allocates per-frame vertex/index ring buffers of size `ImageCount`. Since we render synchronously into our own command buffer and never present off-screen output to a swapchain, one set of buffers suffices and we always rotate to index 0. If we ever observe artifacts that smell like a write-while-read race we can raise to `_renderer.MaxFramesInFlight`.
- **Font atlas texture**: the backend's `RenderDrawData` walks `drawData.Textures` and calls `UpdateTexture` for any with non-OK status (lines 670-679 of decomp). The shared atlas was built+uploaded by the **main** backend, so its status is `OK` and our backend will not re-upload. We can rely on that. **If validation complains** ("descriptor set bound to a stale image view" on first frame), then build a private atlas with `atlas.Build()` and let our backend upload it; trade-off is ~atlas-bytes of duplicated GPU memory.

### 4.4 Acceptance test for Phase 4

- Construct + dispose the backend without throwing.
- Run with Vulkan validation enabled; expect zero errors.

---

## 8. Phase 5 — Per-frame off-screen render loop (debug viewer)

**Goal**: every frame, render an ImGui window into our off-screen color attachment. Verify by sampling it back into the **main** context as `ImGui.Image(textureRef, ...)` shown in a small debug window. After this phase we don't have anything in 3D yet, but we can SEE the texture, which proves the whole left-hand side of the pipeline works.

### 5.1 Frame algorithm

Pseudocode for the `OnAfterGui(dt)` body:

```text
1. (in secondary context)
   a. set io.DeltaTime = dt
   b. set io.DisplaySize = (W, H)
   c. ImGui.NewFrame()
   d. BuildUi()   // for now: ImGui.Begin("OffScreen Test"); ImGui.Text("Hello"); ImGui.End();
   e. ImGui.EndFrame()
   f. ImGui.Render()
   g. var drawData = ImGui.GetDrawData()
2. (back in main context)
   a. acquire a primary CommandBuffer for our work (see 5.2)
   b. cmd.Begin(OneTimeSubmit)
   c. transition color attachment Undefined → ColorAttachmentOptimal
   d. cmd.BeginRenderPass( renderPass=_target.RenderPass,
                           framebuffer=_target.Target.FrameBuffer,
                           renderArea=(0,0,W,H),
                           clearValues=[ {0,0,0,1}, {depth=1,stencil=0} ],
                           contents=Inline )
   e. ctx.With(() => _backend.Render(drawData, cmd))
   f. cmd.EndRenderPass()
   g. transition color attachment ColorAttachmentOptimal → ShaderReadOnlyOptimal
   h. cmd.End()
   i. submit to graphics queue, signal a fence
3. (debug only)
   register our color image view + sampler with the MAIN backend via
   AddTexture(sampler, view, ShaderReadOnlyOptimal) once; cache the returned ImTextureRef.
   In OnBeforeUi or OnAfterUi, draw ImGui.Image(_dbgRef, new float2(W, H)) in the main context.
```

### 5.2 Where do we get a command buffer?

**Two viable strategies**, listed in order of preference.

**Strategy A — submit a one-off short-lived command buffer per frame** (recommended for MVP).

- Allocate our own `VkCommandPool` (transient, resettable) once in `Initialize()`.
- Each frame: allocate (or reset) a primary command buffer from that pool, record, submit, fence-wait. We can keep `MaxFramesInFlight` (e.g. 2 or 3) command buffers and ring them with fences to avoid stalls.
- Submission must happen on the same graphics queue as the main render (`renderer.Graphics`); Vulkan requires that.
- The off-screen render must complete **before** the main render reads from the texture in the same frame. The easiest sync is: submit our cmdbuf with a `VkFence`, wait on the fence **synchronously** at the start of the next-frame's record. Better: use a semaphore that the next main-pass submission waits on. The simplest correctness-first approach is **fence-wait at start of the next frame**; the main pass naturally has no read hazard within the same frame because Vulkan submit ordering on the same queue is preserved.
- Hazard: we cannot mix queue submissions with anything KSA does in `OnAfterUi` if KSA itself is holding the queue lock. Inspect `Program.OnFrame` ordering — `[StarMapAfterGui]` is invoked **after** `ImGui.Render()` (line 1921) but **before** `OnPreRender` (line 1936). The acquire+submit of the main frame happens at line 1937, so submitting our cmdbuf in `OnAfterGui` is **before** the main submission. Good. We just need to be reentrant-safe with the queue (`Queue.Submit` is thread-safe with internal locking in BRUTAL; verify in `decomp/ksa/Brutal.VulkanApi.Abstractions/Queue.cs`).

**Strategy B — Harmony-patch `Program.RenderGame`** (`decomp/ksa/KSA/Program.cs:3768`) to inject our recording into the same primary command buffer the game uses.

- Pros: no extra queue submission, perfect ordering.
- Cons: more invasive; brittle across KSA updates.
- Only adopt if Strategy A surfaces sync issues that can't be resolved with semaphores.

**Decision for the plan**: implement Strategy A first. Only fall back to Strategy B if profiling shows the extra submission stalls the frame.

### 5.3 Image layout transitions

Use the `ImageTransition` helper (`decomp/ksa/KSA/CommandBufferEx.cs:25-40`, presets in `ImageBarrierInfo.cs:7-62`).

```csharp
cmd.TransitionImages2(new ReadOnlySpan<ImageTransition>(new ImageTransition(
    _target.ColorImage,
    ImageBarrierInfo.Presets.Undefined,
    ImageBarrierInfo.Presets.ColorAttachmentWrite,
    ImageTransition.Subresource(VkImageAspectFlags.ColorBit))));

// ... render pass ...

cmd.TransitionImages2(new ReadOnlySpan<ImageTransition>(new ImageTransition(
    _target.ColorImage,
    ImageBarrierInfo.Presets.ColorAttachmentWrite,
    ImageBarrierInfo.Presets.SampledReadFragment,
    ImageTransition.Subresource(VkImageAspectFlags.ColorBit))));
```

Use `ImageBarrierInfo.Presets.Undefined` for the first transition only on frame 0; subsequent frames can use a `ColorAttachmentRead` → `ColorAttachmentWrite` no-op or just `Undefined → ColorAttachmentWrite` (the `LoadOp=Clear` in the render pass means we don't care about prior contents).

### 5.4 Sampler for our texture

```csharp
_sampler = renderer.Device.CreateSampler(Presets.Sampler.SamplerLinearClamped, null);
```

Preset defined at `decomp/ksa/KSA/Presets.cs:324-348`. Linear + clamp-to-edge is correct for a UI-on-a-surface.

### 5.5 Debug viewer (sanity check)

In `OnBeforeUi(dt)` (the existing main-context callback):

```csharp
if (_debugTex.IsNotNull())
{
    if (ImGui.Begin("In-World Terminal Debug"))
        ImGui.Image(_debugTex, new float2(_target.Extent.Width, _target.Extent.Height));
    ImGui.End();
}
```

`_debugTex` is obtained from the **main** backend (which is the game's `_imguiBackend`, accessible via... let me note this for the implementing agent: the main backend is not exposed as a public static. Two ways to get an `ImTextureRef` for our image view in the main context:

1. **Easy & officially intended**: use `ImTextureRef._TexID` directly. The Vulkan backend's `AddTexture` (line 1052) builds a descriptor set and returns its `VkHandle` as the `ImTextureID`. If we replicate that work for the main context's descriptor pool, we need access to the main backend instance. Search `decomp/ksa/KSA/Program.cs` for the field that holds the backend — it is stored as a private field and called e.g. `_imguiBackendVulkan`. **Use Harmony AccessTools** or `Traverse` to read it once at init.
2. **Pragmatic alternative**: skip the debug viewer entirely and validate Phase 5 by running with the Vulkan validation layer + RenderDoc capture. Step the capture, inspect the off-screen image bytes. This is preferred because it doesn't add coupling.

Recommend approach 2 for the agent — validate with RenderDoc / nsight rather than building a debug viewer that requires private-field access.

### 5.6 Acceptance test for Phase 5

- RenderDoc capture shows our off-screen render pass on the frame timeline.
- The captured `_target.ColorImage` after the pass contains visible ImGui pixels (white background + black text "Hello").
- The image is in `SHADER_READ_ONLY_OPTIMAL` at frame end.
- Zero Vulkan validation errors over a 60-second run.

---

## 9. Phase 6A — In-world quad (custom Vulkan pipeline)

**Goal**: a flat textured quad rendered in 3D world space, sampling our off-screen texture. This is the **first thing the user sees**. Built before Phase 6B because it's lower risk (no game-data manipulation).

### 6A.1 Architecture choice

We will **not** route through `GpuMaterialSystem` for the quad — that system is tied to the game's `Part` rendering and assumes a `MeshReference` in a `PartTree`. Instead we register our own pipeline + draw call.

**Reuse existing shaders**: load the precompiled SPIR-V from `decomp/ksa/Content/Core/Shaders/Mesh/UnlitMesh.{vert,frag}` (the game ships these as `.spv` files in `Content/Core/Shaders/Mesh/` at runtime — verify path by grepping for `UnlitMesh` in `decomp/ksa/KSA/`; the runtime path is typically `Content/Core/Shaders/Mesh/UnlitMesh.vert.spv`).

### 6A.2 `QuadDisplay.cs`

Responsibilities:
1. Own a `VkPipelineLayout` + `VkPipeline` matching `UnlitMesh.{vert,frag}`:
   - Vertex input: `vec3 pos` + `vec2 uv`, stride 20, two locations.
   - Push constant: `mat4` (64 bytes), vertex stage.
   - Descriptor set 0, binding 0: `combinedImageSampler` (our off-screen color view + sampler).
   - Render pass: **the game's main pass** (we draw INTO the swapchain so the quad shows up in the final image). The handle is `Program.MainPass.Pass` (`decomp/ksa/KSA/Program.cs:354`).
   - Cull: back-face. Depth test: enabled, write enabled, compareOp LessOrEqual (same as KSA's part pipeline — see how `PartModelRenderer.cs` builds its pipeline).
2. Own a vertex+index buffer for one quad (4 verts, 6 indices), uploaded once at init via `StagingPool` (see `decomp/ksa/KSA/SimpleVkMesh.cs:32-97`).
3. Each frame, **record** the quad draw into the appropriate command buffer (see 6A.3).
4. Provide a world transform: `Transform Transform { get; set; }` with `Position (double3)`, `Rotation (doubleQuat)`, `Scale (float2)`. Default: 2m in front of the active camera, facing the camera, sized per `InWorldSettings`.
5. Compute MVP each frame: `MVP = camera.MVP * world` where `world` is our quad transform in ego space.

### 6A.3 Where to record the quad draw

This is the hard part. Two options, in order of preference:

**Option A — append into the game's primary command buffer via Harmony**:
- Harmony postfix on `Program.RenderGame` (`decomp/ksa/KSA/Program.cs:3768`), or better, on a specific point inside the main render pass after the scene is drawn but before ImGui. We need the cmdbuf to be inside the main pass when we record.
- Inspect `RenderGame` at lines 3770-3791 to find a clean injection site. The agent should look for the point where the main scene draws are complete (just before `_imguiBackendVulkan.RenderDrawData` is called) and add a post-call patch.
- Acquire the live `CommandBuffer` via `Traverse(__instance).Field("_currentCmd")` or by reading the patch arg.

**Option B — second subpass / separate primary cmdbuf**:
- Submit a separate primary cmdbuf that does its own `BeginRenderPass` into the swapchain framebuffer.
- Problem: Vulkan doesn't let two primary submissions share a render pass instance cleanly without semaphores carrying the framebuffer layout.
- Use only if Option A is too brittle.

**Decision**: Option A. The Harmony patch sits in `purrTTY.GameMod/InWorld/Patches/FramePatches.cs`. Even though our project goal "avoid Harmony", we already use Harmony elsewhere (`purrTTY.GameMod/Patcher.cs`), and rendering integration without a public render-callback hook genuinely requires it.

### 6A.4 World placement & transforms

```csharp
public void UpdateTransform(Camera cam)
{
    // 1. Compute world position: camera position + forward * distance, in ego frame.
    double3 forward = cam.GetForward();
    double3 right   = cam.GetRight();
    double3 up      = cam.GetUp();
    double3 posEgo  = forward * _settings.QuadDistanceMeters;   // ego-relative

    // 2. Build a quaternion that orients the quad to face the camera.
    //    Quad's local +Z is its normal; camera looks along -forward.
    //    Use Brutal.Numerics.doubleQuat.LookRotation or compose from basis vectors.
    doubleQuat lookAtCam = doubleQuat.LookRotation(-forward, up);

    // 3. Compose model matrix.
    var scale  = double4x4.CreateScale(_settings.QuadWidthMeters, _settings.QuadHeightMeters, 1);
    var rot    = double4x4.CreateFromQuaternion(lookAtCam);
    var trans  = double4x4.CreateTranslation(posEgo);
    _modelEgo  = scale * rot * trans;

    // 4. MVP push constant: model in ego × viewProj from camera (camera.MVP IS view*proj here).
    _mvpPushConst = (float4x4)(cam.MVP * _modelEgo);
}
```

Notes:
- `Camera.MVP` (`decomp/ksa/KSA/Camera.cs`) is the **view-projection** for ego-space rendering (KSA's parts are rendered in ego space). Verify by reading `Camera.cs:MVP` and the call sites — there's a `ViewProjection` struct with both VP and inverse.
- Optionally let the user pick "fixed in world" mode where the quad's world transform is set once and persists relative to the vessel; for that, we transform the ego matrix using `Camera`'s world rotation and position rather than `forward * distance`.

### 6A.5 Pipeline creation reference

Mirror the KSA part pipeline shape. The vertex input description for `UnlitMesh.vert`:

```csharp
var bindings = new VkVertexInputBindingDescription[]
{
    new() { Binding = 0, Stride = 20, InputRate = VkVertexInputRate.Vertex },
};
var attrs = new VkVertexInputAttributeDescription[]
{
    new() { Location = 0, Binding = 0, Format = VkFormat.R32G32B32SFloat,    Offset = 0  },  // pos
    new() { Location = 1, Binding = 0, Format = VkFormat.R32G32SFloat,       Offset = 12 },  // uv
};
```

Pipeline rasterization: `PolygonMode=Fill`, `CullMode=Back`, `FrontFace=CounterClockwise` (match KSA convention; verify via `PartModelRenderer.cs`). Color blend: opaque (alpha is 1.0 from the frag shader anyway). Dynamic state: viewport + scissor.

Push constant range: `Stage=Vertex, Offset=0, Size=64`.

Descriptor set layout (set 0, binding 0): `CombinedImageSampler`, stage fragment, count 1.

### 6A.6 Acceptance test for Phase 6A

- A flat opaque quad shows up 2m in front of the camera, oriented to face it, sized 1.6m × 1.0m.
- The quad's surface shows the rendered terminal pixels (white background, black "Hello" text).
- Moving the camera (mouselook) makes the quad re-orient (we recompute every frame).
- Toggling the feature off removes the quad from the frame.
- No Vulkan validation errors.

---

## 10. Phase 6B — SubPart material override

**Goal**: pick a SubPart on the active vessel (by name from `InWorldSettings.TargetPartName`) and make its existing mesh sample our off-screen texture instead of its normal diffuse map. This is the **preferred final destination** when a suitable mesh exists.

### 6B.1 Approach: bindless texture swap

KSA renders parts using `MaterialData` (`decomp/ksa/KSA/MaterialData.cs`) — a GPU SSBO entry whose `int AlbedoTexture` field is a **bindless** index. Swapping the texture for a part is, in theory, three steps:

1. Register our off-screen color image view as a bindless texture, getting a `bindlessHandle`.
2. Find the `MaterialData` index used by the chosen SubPart.
3. Patch the `AlbedoTexture` field of that entry to our handle (and ideally save the old one so we can restore on unload).

### 6B.2 Register our texture

```csharp
// In InWorldTerminalManager.Initialize, after target+sampler exist:
var texSys = Program.Instance.TextureSystem;            // KSA.Program.Instance (or via reflection)
// Path A — easiest:
var simpleVkTex = new SimpleVkTexture(...); // wrap our ColorImage + ColorImageView in a SimpleVkTexture
                                            //   ctor from decomp/ksa/KSA/SimpleVkTexture.cs:210
_bindlessHandle = texSys.BindTexture(simpleVkTex);
```

`SimpleVkTexture` has a "wrap existing image" ctor signature shown at `decomp/ksa/KSA/SimpleVkTexture.cs:210-244`. Future-agent task: confirm the public ctor accepts pre-existing `ImageEx` (the VMA allocation). If not, we re-create the image with the same VMA allocator and use it directly as the framebuffer color attachment too — single allocation serves both roles. The most likely interface adjustment is to use `ImageEx` directly with `_bindlessTextureLib.AddTexture(imageView)` if there's a public surface; if no public surface, use `Traverse` to access `_bindlessTextureLib`.

### 6B.3 Find the target SubPart and its MaterialData index

```csharp
Vehicle? vehicle = Program.ControlledVehicle;
if (vehicle == null) return; // no vessel; cannot bind

Part? hit = null;
foreach (Part p in vehicle.Parts.Parts)
{
    if (p.Id == _settings.TargetPartName) { hit = p; break; }
    foreach (Part sub in p.SubParts)
        if (sub.Id == _settings.TargetPartName) { hit = sub; goto found; }
}
found:
if (hit == null) { /* fallback to QuadDisplay */ return; }
```

Notes:
- `Part.SubParts` is a `ReadOnlySpan<Part>` on `Part.cs` (search around line 564 for `IsSubPart`); confirm field name.
- "Part name" semantics: prefer matching on a stable identity (`Id` is the asset name; `IName`/`DisplayName` may exist). Agent should pick whichever is most stable from the runtime data — log all parts on first run for the user to choose from.

Finding the **MaterialData index** is harder. KSA's `PartModelRenderer` (`decomp/ksa/KSA/PartModelRenderer.cs:1-99`) issues indirect draws; each draw record references a material index. The most robust approach is:

- Patch the part's material reference rather than the GPU buffer entry. Each `Part` has an associated `PbrMaterialReference` (or similar) somewhere reachable from the `MeshViewModule`. Walk from `Part → Modules → MeshViewModule → MeshView` and inspect the asset chain in `decomp/ksa/KSA` until you find the material assignment. Update there before the first frame draws.

Alternative: **don't replace, override**:
- Add a **new** `MaterialData` entry via `GpuMaterialSystem.SendToBuffer` (`decomp/ksa/KSA/GpuMaterialSystem.cs:67-84` shows the existing call pattern) populated with our `AlbedoTexture = _bindlessHandle`, copying all other fields from the original.
- Track the new material handle.
- Harmony-patch the part's draw call to use our new handle instead of the original. This requires intercepting whatever `PartModelRenderer` uses to determine the material index for that part — likely a Harmony postfix or transpiler on the per-part code path.

**Decision**: implement the **registered-override** approach (new entry + harmony patch on the part's draw selection). It avoids mutating the original `MaterialData` and gives us a trivial restore (remove the patch / mark our entry unused).

### 6B.4 Restore on unload

Critical: on `Unload()`, restore the original material so the vessel doesn't end up with a stale handle. The Harmony patch is the natural ownership boundary — unpatching restores original behavior.

### 6B.5 Caveats

- **The shader used for the part determines whether our texture shows up correctly**. If the part is rendered with `MeshIndirect.frag` (full PBR with lighting + normal mapping), our terminal will be lit, possibly dimmed, and tinted by `AlbedoColor`. To get a 1:1 readable image:
  - Set the new `MaterialData.AlbedoColor = float4.One`.
  - Stuff a white pixel into `NormalTexture` and `RoughMetallicAOTexture` (KSA already exposes `DefaultWhite`/`DefaultBlack` textures — verify exact field names in `GpuTextureSystem.cs`).
  - Set `MaterialData.EmissiveTexture = _bindlessHandle` as well. Many KSA shaders emit `emissive` unlit; emissive will dominate in dim lighting. (Confirm by reading `MeshIndirect.frag` — search for `emissive` to see whether it's additive.)
  - The cleanest path: ensure the chosen SubPart uses the `UnlitMesh` shader. If the game has a way to override the material's pipeline (e.g., a "shader id" field on the material reference XML), use it.
- **UV mapping on the SubPart mesh** determines how our texture lays out. If the SubPart's UVs are not a clean 0..1 rectangle, the terminal will distort. The user must pick a SubPart known to have a planar UV island that covers 0..1 (e.g., a flat panel on a cockpit). The plan supports a debug option: render UV-checkerboard into the off-screen target temporarily to visually verify.

### 6B.6 Acceptance test for Phase 6B

- With `TargetPartName` set to a valid SubPart on the active vessel, that mesh shows our terminal pixels.
- With `TargetPartName` empty or invalid, we fall back to `QuadDisplay`.
- Unloading the mod restores the original material.

---

## 11. Phase 7A — Click-to-focus + keyboard routing (required minimum interaction)

**Goal**: the user can click on the in-world surface (quad or SubPart) to give the in-world terminal keyboard focus; subsequent typing goes into the terminal. This is the **minimum interaction the user explicitly required**.

### 7A.1 Focus model

```csharp
public enum InWorldFocusState
{
    NotFocused,                 // input goes to game / screen-space terminal as normal
    Focused                     // input is captured into the off-screen terminal
}
```

`InWorldFocus` is a single instance owned by `InWorldTerminalManager`.

Focus changes:
- Mouse click hits the surface → set `Focused`.
- Mouse click misses the surface AND lands somewhere that wants focus (game world, another ImGui window) → set `NotFocused`.
- Pressing `Esc` while Focused → `NotFocused`.

### 7A.2 Mouse picking

Each frame, **before** the focus check, build a ray from the cursor and test it against the surface.

- For **quad**: triangle-test the two triangles that make up the quad. The quad's 4 vertices in ego space are computable from `_modelEgo`. Use `Ray.RaycastMollerTrumbore` (`decomp/ksa/KSA/Ray.cs:54`) twice; take the closer hit. Compute barycentric UV interpolation manually (Möller-Trumbore returns barycentric coords as part of its math — the helper currently only returns `t`; the agent should write a small `RaycastTriangleUV` helper that returns `(t, u, v)`).
- For **SubPart**: triangle-test the mesh from `MeshReference.PositionCompare` (`decomp/ksa/KSA/MeshReference.cs:20`) which is the **decompressed double-precision vertex array** specifically intended for raycasting. Walk the triangles, MT-test each, find nearest `t`. UV interpolation requires the mesh's per-vertex UVs; access via `HostMesh.GetVertexSpan<float2>(MeshAttribute.UV)` (see `decomp/ksa/KSA/MeshReference.cs:58-68` for the index-buffer + vertex-buffer access pattern).

Ray source: `Cursor.InputRay` (`decomp/ksa/KSA/Cursor.cs:40`) — already populated by KSA each frame in `Program.OnFrame:1934`.

### 7A.3 Click detection

The main ImGui context already has mouse state. We read it without consuming it (it's still allowed to fall through to the rest of ImGui):

```csharp
bool leftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);    // main context
if (leftClicked && hitSurface)
    _focus.State = InWorldFocusState.Focused;
else if (leftClicked && !hitSurface && !ImGui.GetIO().WantCaptureMouse)
    _focus.State = InWorldFocusState.NotFocused;
```

### 7A.4 Keyboard routing into the secondary context

Once `Focused`, every key event must reach the secondary ImGui context's IO. The main context's IO already has the events; we just **also** forward them to the secondary.

Two viable techniques:

**Technique A — read main IO, copy to secondary IO (per-frame poll)**:
```csharp
if (_focus.State == InWorldFocusState.Focused)
{
    var mainIO = ImGui.GetIO(); // main
    _ctx.With(() =>
    {
        var io = ImGui.GetIO();
        // Position not relevant for typing-only minimum, but copy modifiers.
        io.AddKeyEvent(ImGuiKey.Mod_Ctrl,  mainIO.KeyCtrl);
        io.AddKeyEvent(ImGuiKey.Mod_Shift, mainIO.KeyShift);
        io.AddKeyEvent(ImGuiKey.Mod_Alt,   mainIO.KeyAlt);
        io.AddKeyEvent(ImGuiKey.Mod_Super, mainIO.KeySuper);
        // We need a way to enumerate key events that happened this frame.
        // Easiest: ImGui exposes io.KeysDown[] — iterate ImGuiKey values and forward state.
        for (var k = ImGuiKey.NamedKey_BEGIN; k < ImGuiKey.NamedKey_END; k++)
        {
            bool down = ImGui.IsKeyDown(k);  // main context
            // Only forward edges by comparing to a tracked prev-state cache.
        }
        // Text input characters:
        foreach (var c in mainIO.InputQueueCharactersThisFrame)
            io.AddInputCharacter(c);
    });
}
```

This is fine but lossy at frame boundaries.

**Technique B — Harmony-patch the GLFW callbacks** in `decomp/ksa/KSA/ImGuiBackendGlfwImpl.cs` (the call sites at lines 436, 461, 507). For each `iO.AddKeyEvent(...)` / `iO.AddCharEvent(...)`, **prefix-patch** to also issue the event to our secondary context when focus is on.

Technique B is lossless. Recommend Technique B once Technique A is shown to lose events.

### 7A.5 Stopping game input when focused

The existing terminal already does this via `Patcher.cs:86-101` — a prefix patch on `Program.OnKey` returning false. Extend it: when in-world is focused, also return false. Cleanest path is to OR the existing condition with `_inWorld.IsFocused`.

### 7A.6 Acceptance test for Phase 7A

- Open game, enable in-world terminal, see quad/SubPart with terminal pixels.
- Move mouse over the quad, click — terminal becomes focused (e.g., its cursor blinks; we can add a focus border visible in the off-screen render).
- Type — text appears in the off-screen terminal. Game does not receive the keystrokes.
- Press Esc — focus released. Typing now goes back to the game.

---

## 12. Phase 7B — Full mouse routing (optional)

**Goal**: cursor + click + scroll work on the in-world surface. The user can click buttons, drag scrollbars, select text in the terminal — all from inside the 3D scene.

### 7B.1 UV → ImGui mouse pos

Once we have `(u, v)` from the raycast, mouse position in the secondary context's coordinates is:

```csharp
float2 mouseInOffscreen = new float2(u * _ctx.Size.X, v * _ctx.Size.Y);
_ctx.With(() => ImGui.GetIO().AddMousePosEvent(mouseInOffscreen.X, mouseInOffscreen.Y));
```

When not hovering the surface, send `float.MinValue` to indicate "no mouse" (the convention BRUTAL backend follows — see `decomp/ksa/KSA/ImGuiBackendGlfwImpl.cs:461`).

### 7B.2 Buttons + wheel

Same Technique A/B as keyboard: forward per-frame button state changes and `MouseWheel` from main to secondary.

### 7B.3 Cursor visibility

The OS cursor remains on the screen at screen-space coordinates; the ImGui-drawn cursor in the off-screen terminal can be enabled via `io.MouseDrawCursor = true` for the secondary context only, giving the user a visible cursor that lives on the surface.

### 7B.4 Acceptance test for Phase 7B

- Hovering a button in the off-screen terminal highlights it.
- Clicking executes the button.
- Mouse wheel scrolls the terminal's scrollback when hovering the surface.

---

## 13. Phase 8 — Settings, polish, cleanup

### 8.1 Settings UI

Extend the existing settings modal (see `TerminalMod.cs:371-527` for the pattern):

- Toggle key (defaults F11).
- Resolution (combo: 512×512, 1024×1024, 1024×768, 1280×720, 1920×1080).
- Mode (combo: "Floating Quad", "SubPart" with a text-box for `TargetPartName`).
- Quad placement: position offset, distance, scale (only shown for Floating Quad).
- Persist via the existing configuration save/load.

### 8.2 Resize / dynamic resolution

If the user changes resolution, call:
```csharp
_target.Resize(w, h);
_ctx.Resize(w, h);
// no need to recreate the backend — its pipeline is render-pass-bound, render pass unchanged.
```

If the format changes, we **do** need to recreate the render pass and the backend.

### 8.3 Frame-rate independence

Pass the real `dt` (the same `dt` provided to `OnAfterUi`) into `io.DeltaTime`. This drives ImGui's animations (caret blink, etc.) at the right speed regardless of frame rate.

### 8.4 Lifecycle

`InWorldTerminalManager.Dispose()` order:
1. Stop submitting frames (set a flag, swallow `OnAfterGui` until disposed).
2. `vkDeviceWaitIdle` (or block on our fences).
3. Unpatch all Harmony patches.
4. Dispose `_backend` (Vulkan resources).
5. Dispose `_ctx` (ImGui resources).
6. Dispose `_target` (image, view, framebuffer, render pass).
7. Destroy sampler.

### 8.5 Loud-failure mode

If any GPU resource creation fails:
- Log loudly via `ModLog`.
- Disable `InWorldSettings.Enabled` for this session.
- Continue running the rest of the mod normally (screen-space terminal still works).

### 8.6 Compatibility with the screen-space terminal

The existing screen-space terminal continues to build its UI in the main context. The in-world terminal builds the **same** terminal session in the secondary context. To share state, both UIs must call into a single `TerminalController` and respect a "render-only" mode in the off-screen path so input that doesn't reach it goes through the same buffer/cursor logic.

Concretely: `TerminalController.Render()` (`purrTTY.Display/Controllers/TerminalController.cs:404-608`) is already context-agnostic — it calls `ImGui.Begin` / `ImGui.End` against whichever context is current. So:

```csharp
_ctx.With(() =>
{
    // Same TerminalController instance as the main UI — just rendered in our context.
    _terminalController.Render();
});
```

Caveats:
- `TerminalController` calls `ImGui.PushFont(...)`. The fonts must exist in the **shared** atlas of the secondary context. They do, because we shared the main context's atlas.
- Window sizing: in our context we want the terminal to fill the whole display. Wrap the controller's `ImGui.Begin` call site so that, in off-screen mode, we set window position (0,0) and size = DisplaySize, with flags `NoTitleBar|NoResize|NoMove|NoBringToFrontOnFocus|NoCollapse|NoScrollbar`. Simplest implementation: add a `bool FullscreenMode` flag on `TerminalController` and respect it in the existing flag-setting code (`TerminalController.cs:475`).
- Cursor blink, selection, scrollback — all of these are properties of the controller and will work identically in both contexts.

---

## 14. Risk register & contingencies

| Risk | Mitigation |
|---|---|
| `ImGuiBackendVulkanImpl` is not constructible by mods (internal ctor / missing dependency) | Use `System.Reflection` to invoke the constructor; if that fails, build a minimal in-house ImGui Vulkan backend using the embedded SPIR-V at `ImGuiBackendVulkanImpl.cs:157-200` |
| `GpuTextureSystem.BindTexture` requires a `SimpleVkTexture` constructed in a way the mod can't replicate | Use `Traverse(...)._bindlessTextureLib.AddTexture(_target.ColorImageView)` reflectively. This is the *actual* underlying call (`GpuTextureSystem.cs:80-83`) |
| Quad orientation math wrong on first attempt | Build a UV-checkerboard test texture and toggle to it temporarily — if it shows up upside-down or mirrored, flip the quad's UV layout |
| `Camera.MVP` is per-viewport and we pick the wrong viewport | Use `Program.GetHoveredCamera()` for the picking ray; use `Program.GetMainCamera()` for the quad rendering. Both at `Program.cs:470-473` |
| Off-screen submit races with main submit on the queue | Use a per-frame semaphore that the next frame's main pass waits on, OR fence-wait synchronously at the start of `OnAfterGui` |
| StarMap `[StarMapAfterGui]` is called inside an ImGui frame that hasn't been `Render`ed yet | Verify by logging `ImGui.GetCurrentContext()` and the IO's `WantSaveIniSettings` (a marker of mid-frame state). The decomp suggests `[StarMapAfterGui]` fires after `ImGui.Render()`; if not, move our render work to a different hook |
| Vulkan validation errors we don't understand | Capture with RenderDoc; the markers and full layout chain make root-causing trivial |
| User has no vessel loaded (main menu, scene transitions) when in-world is enabled | `QuadDisplay` requires only a camera — works on the main menu. `SubPartDisplay` should auto-disable until `Program.ControlledVehicle != null` |
| SubPart name unknown to the user | Add a debug menu "List Parts on Active Vessel" that logs every part Id + SubPart Id |

---

## 15. Task list (one per agent run)

These are sized so a single agent run can complete one, with the relevant section of this plan + the listed decomp files as input.

1. **Phase 1 scaffolding** — read sections 4. Create `InWorldSettings`, `InWorldTerminalManager` skeleton. Wire into `TerminalMod.cs`. Add toggle hotkey. Verify the F11 toggle logs a message and the game still runs normally.
2. **Phase 2 render target** — read section 5. Implement `OffscreenRenderTarget` with `VkRenderPass` creation (mirror `decomp/ksa/KSA/Renderer.cs` patterns), color+depth attachments via `RenderTarget` helper, dispose. Acceptance: build, run, verify zero validation errors with RenderDoc.
3. **Phase 3 secondary context** — read section 6. Implement `OffscreenContext` with shared font atlas and `With(Action)` scope helper. Acceptance: create+dispose 100× in a hot-reload, no leaks.
4. **Phase 4 secondary backend** — read section 7. Implement `OffscreenImGuiBackend`. Note: construct under `_ctx.With(...)`. Acceptance: create+dispose, no validation errors.
5. **Phase 5 frame loop + RenderDoc validation** — read section 8. Implement `PerFrameRenderer`. Build a one-off cmdbuf, transitions, render pass begin/end, queue submit, fence wait next frame. Build a trivial "Hello" ImGui UI in the secondary context. Acceptance: RenderDoc capture shows the off-screen render pass with the expected pixels.
6. **Phase 6A quad** — read section 9. Load `UnlitMesh.{vert,frag}.spv` at runtime, build pipeline against `Program.MainPass.Pass`, build quad vertex/index buffers, compute MVP, Harmony-patch `Program.RenderGame` to inject a draw call into the live cmdbuf. Acceptance: a textured quad appears 2m in front of the camera, rotates to face it, sized 1.6×1.0m.
7. **Phase 6B SubPart override** — read section 10. Implement bindless registration, target-part lookup, material override via Harmony. Acceptance: the chosen SubPart's mesh shows terminal pixels. Toggle off restores the original material.
8. **Phase 7A click-to-focus + keyboard** — read section 11. Implement raycast hit-test (quad first), focus state, keyboard event forwarding (Technique A — per-frame poll; ship as MVP). Acceptance: clicking the quad focuses it; typing reaches the terminal.
9. **Phase 7B full mouse** — read section 12. Forward mouse pos/buttons/wheel. Acceptance: hovering buttons highlights them, clicks fire.
10. **Phase 8 settings + polish** — read section 13. Build the settings modal entries; persist config; handle resize; build the "List Parts" debug menu. Acceptance: ship-quality UX.

Each task above should be picked up with the file ranges in section 2 ("Required reading map") plus the section in this plan that owns it. No task requires knowledge of any other task's implementation; later tasks only depend on earlier ones via the public surface defined in section 3 (file layout).
