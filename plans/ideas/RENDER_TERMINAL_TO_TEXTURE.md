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

## Executive summary

The best implementation path is to treat this as two related but separate systems:

1. Render purrTTY terminal content into a Vulkan image that can be sampled.
2. Consume that sampled image either through ImGui, a world-space quad/billboard, or a material override on existing game meshes/SubParts.

The decompiled KSA/BRUTAL sources show that the core pieces already exist:

- BRUTAL ImGui renders draw data through `KSA.ImGuiBackendVulkanImpl.RenderDrawData(ImDrawDataPtr, CommandBuffer)` and stores ImGui texture IDs as Vulkan descriptor set handles.
- KSA already creates sampled render targets through `KSA.RenderTarget`, `KSA.OffscreenTarget`, and `KSA.Framebuffer`.
- KSA already exposes texture registration for ImGui via `ImGuiBackend.Vulkan.AddTexture(VkSampler, VkImageView, VkImageLayout)`.
- KSA already exposes bindless material texture registration through `Program.Instance.TextureSystem.BindTexture(SimpleVkTexture)` and material creation through `Program.Instance.MaterialSystem.CreateObject(AssetName, MaterialData)`.
- KSA already exposes mesh submission through `Program.Instance.SuperMeshRenderSystem` and `MeshBucketSystem<InstanceData>`.

The most direct prototype is not a full custom render graph. It is a small KSA-facing rendering service in `purrTTY.Display` that:

1. Allocates a `RenderTarget` with a color attachment whose final layout is `ShaderReadOnlyOptimal`.
2. Creates a render pass and framebuffer for that target.
3. Captures terminal UI draw commands into an ImGui draw list or draw data payload.
4. Renders those draw commands into the target with `ImGuiBackend.Vulkan.RenderDrawData(drawData, commandBuffer)` or, if draw-data capture proves blocked, renders purrTTY's own recorded terminal draw commands into a custom graphics pipeline.
5. Registers the target color image view both as an ImGui texture (`ImTextureRef`) and as a game bindless texture handle.
6. Uses the same GPU image for two consumers: `ImGui.Image(...)` preview/debug UI and a world-space quad/SubPart material path.

The world-space quad/billboard and the SubPart material override should be implemented as two parallel display adapters over the same `TerminalRenderTexture` resource. Do not make SubPart replacement the first dependency of render-to-texture. SubPart rendering is possible, but it is more coupled to KSA's part/material systems and may require Harmony for targeted replacement on existing meshes.

## Verified source map

### purrTTY render extension points

- `purrTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs`
	- Gets `ImGui.GetWindowDrawList()` and computes terminal origin/size in `RenderTerminalContent`.
	- Builds `TerminalRenderKey` from buffer revision, viewport offset, theme version, font metrics, terminal dimensions, and window position.
	- Delegates grid drawing to `ITerminalRenderStrategy.RenderGrid(...)`.
- `purrTTY.Display/Rendering/ITerminalBackingStore.cs`
	- Existing abstraction for cached terminal output.
	- Methods: `BeginCapture(width, height)`, `GetDrawTarget()`, `EndCapture()`, `Draw(drawList, position, size)`, `IsReady`.
- `purrTTY.Display/Rendering/CommandBufferBackingStore.cs`
	- Current backing-store implementation records purrTTY draw commands into a managed list and replays to an ImGui draw list.
	- Useful as the model for `TextureBackingStore`, but it currently replays on screen rather than to a GPU texture.
- `purrTTY.Display/Rendering/TerminalViewportRenderCache.cs`
	- Handles cache invalidation and dimensions. It computes pixel size from `Cols * CharWidth` and `Rows * LineHeight`.
- `purrTTY.Display/Rendering/CachedRenderStrategy.cs`
	- Cache miss path renders terminal grid into the backing store, then draws the backing store.
- `purrTTY.Display/Controllers/TerminalControllerBuilder.cs`
	- Current wiring creates `CommandBufferBackingStore`, `TerminalViewportRenderCache`, and `CachedRenderStrategy`.

### BRUTAL ImGui backend

- `decomp/ksa/Brutal.ImGuiApi/ImGui.cs`
	- Main generated ImGui API wrapper.
	- Exposes internal APIs such as `RegisterUserTexture`, `UnregisterUserTexture`, context, window, viewport, and draw-data APIs.
- `decomp/ksa/Brutal.ImGuiApi/ImDrawData.cs`
	- `ImDrawData` is a 72 byte struct containing `CmdLists`, `DisplayPos`, `DisplaySize`, `FramebufferScale`, `OwnerViewport`, and `Textures`.
- `decomp/ksa/Brutal.ImGuiApi/ImTextureID.cs`
	- `ImTextureID` wraps an `nint`; KSA uses that pointer-sized value as a Vulkan descriptor set handle.
- `decomp/ksa/KSA/ImGuiBackend.cs`
	- Static access point: `ImGuiBackend.Vulkan` returns the initialized `ImGuiBackendVulkanImpl`.
	- Initialized with KSA renderer device, graphics queue, main render pass, and descriptor pool size.
- `decomp/ksa/KSA/ImGuiBackendVulkanImpl.cs`
	- `RenderDrawData(CommandBuffer commandBuffer)` calls `RenderDrawData(ImGui.GetDrawData(), commandBuffer)`.
	- `RenderDrawData(ImDrawDataPtr drawData, CommandBuffer commandBuffer)` is public and usable if the caller can provide an active render pass compatible with the backend pipeline.
	- The private draw loop binds `new VkDescriptorSet((nint)imDrawCmd.GetTexID())`, so any texture used by ImGui must be represented as an ImGui texture ID that is actually a Vulkan descriptor set.
	- `AddTexture(VkSampler sampler, VkImageView imageView, VkImageLayout imageLayout = ShaderReadOnlyOptimal)` allocates a descriptor set and returns `ImTextureRef` whose `_TexID` is that descriptor set handle.
	- `RemoveTexture(ImTextureRef texture)` frees the descriptor set.

### KSA render target and image infrastructure

- `decomp/ksa/KSA/Framebuffer.cs`
	- Base class for framebuffer attachments.
	- `CreateAttachment(...)` creates a GPU image through `_renderer.Allocator.CreateImage(...)`, creates an image view, and automatically adds `SampledBit | InputAttachmentBit` unless the usage contains `TransientAttachmentBit`.
	- `BuildFramebuffer(VkRenderPass)` creates `VkFramebuffer` from attachment image views.
	- `Dispose()` destroys framebuffer, image views, and images.
- `decomp/ksa/KSA/RenderTarget.cs`
	- Creates a color and/or depth target.
	- Color image usage includes `TransferSrcBit | TransferDstBit | StorageBit | ColorAttachmentBit`, and `Framebuffer.CreateAttachment` adds `SampledBit`.
	- `CreateRenderPass(...)` sets color final layout to `ShaderReadOnlyOptimal`, which is exactly what the texture consumer needs.
	- `CreateRenderPassWithOptions(VkImageLayout finalColorLayout = ShaderReadOnlyOptimal)` allows explicit final layout control.
- `decomp/ksa/KSA/OffscreenTarget.cs`
	- KSA's main off-screen viewport target. It respects `GameSettings.GetSampleCount()` and creates MSAA resolve attachments.
	- Its `CreateRenderPass(...)` final color layout is `ColorAttachmentOptimal`, not shader-read-only. Use with care if the result is sampled without an explicit transition.
- `decomp/ksa/Brutal.VulkanApi.Abstractions/ImageEx.cs`
	- Low-level allocated image wrapper with `CreateImageView(...)` and `Dispose()`.
- `decomp/ksa/KSA/Viewport.cs`
	- `BuildRenderTarget()` builds off-screen and main render targets, then registers `MainTarget.ColorImage.ImageView` as an ImGui texture via `ImGuiBackend.Vulkan.AddTexture(...)`.
	- This is the strongest proof that KSA expects off-screen render target image views to be shown through ImGui textures.
- `decomp/ksa/KSA/Program.cs`
	- `Program.GetRenderer()` returns the private static renderer.
	- `Program.Instance.TextureSystem` and `Program.Instance.MaterialSystem` are public readonly fields.
	- `Program.Instance.SuperMeshRenderSystem` exposes mesh/material render systems.

### KSA texture, material, and mesh infrastructure

- `decomp/ksa/KSA/GpuTextureSystem.cs`
	- `BindTexture(SimpleVkTexture texture)` registers a `SimpleVkTexture.ImageView` with the bindless texture library.
	- `SamplerClampHandle`, `SamplerRepeatHandle`, `SamplerPointClampHandle` provide material sampler indices.
	- `DefaultWhiteTexture` and `DefaultBlackTexture` are valid fallback textures for PBR material channels.
- `decomp/ksa/RenderCore.Systems/BindlessTextureLibrary.cs`
	- Bindless texture descriptor set layout uses binding 0 for `SampledImage[]` and binding 1 for `Sampler[]`.
	- `AddTexture(VkImageView)` writes `VkDescriptorType.SampledImage` with `ImageLayout = ShaderReadOnlyOptimal`.
	- `FreeTexture(int handle)` replaces the slot with the empty texture and returns the index to the free list.
- `decomp/ksa/RenderCore/SimpleVkTexture.cs`
	- Wraps `ImageEx`, `ImageViewEx`, and upload helpers.
	- Has constructor for raw GPU texture creation with custom usage flags.
	- `UploadData(...)` uploads byte data through a `StagingPool`.
- `decomp/ksa/KSA/GpuMaterialSystem.cs`
	- `CreateAsset(...)` builds `MaterialData` from `PbrMaterialReference`.
	- Inherited `GpuObjectSystem<MaterialData>.CreateObject(AssetName, MaterialData)` can create custom runtime material records.
- `decomp/ksa/KSA/MaterialData.cs`
	- PBR material payload fields: `AlbedoTexture`, `NormalTexture`, `RoughMetallicAOTexture`, `Sampler`, `AlbedoColor`, `RoughnessMetalScale`, `ExtraData`, `EmissiveTexture`.
- `decomp/ksa/KSA/SuperMeshRenderSystem.cs`
	- Exposes `MeshIndirectSystem`, `GltfSystem`, `TextureSystem`, `MaterialSystem`, `MeshBucketSystem`, `OpaquePrePassBucketSystem`, `ShadowBucketSystem`, and renderer techniques.
- `decomp/ksa/KSA/MeshIndirectSystem.cs`
	- `AddMesh(AssetName, MeshAsset)` registers runtime mesh data and returns it through `GetOrLoad(AssetName)` as a `MeshIndirectRef`/`IMeshAsset`.
- `decomp/ksa/KSA/StaticMeshRenderable.cs`
	- Shows the normal mesh render pattern: register mesh buckets in the constructor, then each frame submit `InstanceData` with `data.X = materialHandle` and `model = Transform`.
- `decomp/ksa/KSA/InstanceData.cs`
	- World mesh instance payload: `float4 data` and `float4x4 model`.

### SubPart findings

- `decomp/ksa/KSA.Rendering.Raytracing/RaytracingRenderer.cs`
	- SubPart raytracing data is built from `PartModel.InstancesRayTrace`.
	- It reads `partModel.Template.Mesh` and `partModel.Template.Material`, then stores `SubPartRefs`, `SubPartOffsets`, and `SubPartMaterials` arrays.
	- This indicates SubPart meshes/materials are loaded from templates and are not obviously mutable through a public runtime API.
- `decomp/ksa/KSA/PartModel.cs`, `PartModelRenderer.cs`, `PartModelModule.cs`, and `PartModelDynamicModule.cs`
	- These are the part rendering integration points, but there is no obvious public method for replacing one SubPart material after the part model is created.
- Conclusion: a SubPart material path is feasible, but should be treated as either:
	- create a new runtime part/template that references a dynamic material, if template injection is viable; or
	- Harmony patch a specific material resolution/binding path for selected SubPart IDs.

## Non-goals and guardrails

- Do not try to modify BRUTAL or KSA assemblies on disk.
- Do not start with a custom Vulkan renderer for the full terminal grid unless the ImGui backend path proves blocked.
- Do not force this into StarMap APIs. StarMap only provides lifecycle hooks.
- Do not patch KSA rendering until the direct API path is exhausted.
- Do not make KSA rendering references leak into `purrTTY.Core`; keep all KSA/BRUTAL rendering integration in `purrTTY.Display` or `purrTTY.GameMod`.
- Keep the existing ImGui terminal path as a fallback so the mod remains usable if the texture path fails.

## Phase 0: Clarify and expose runtime renderer access

### Goal

Create a narrow purrTTY abstraction that gives display-layer code access to the KSA renderer and texture systems only when running inside the game.

### Why this matters

`purrTTY.Display` already references several KSA/BRUTAL assemblies, but it is also used by playground/tests. The texture path must not make every environment require a live KSA `Program.Instance`.

### Tasks

1. Add a runtime capability object in `purrTTY.Display/Rendering/TerminalTexture/TerminalRenderServices.cs`.

	 Suggested shape:

	 ```csharp
	 namespace purrTTY.Display.Rendering.TerminalTexture;

	 public sealed class TerminalRenderServices
	 {
			 public static TerminalRenderServices? Current { get; private set; }

			 public required Core.Renderer Renderer { get; init; }
			 public required KSA.GpuTextureSystem TextureSystem { get; init; }
			 public required KSA.GpuMaterialSystem MaterialSystem { get; init; }
			 public required KSA.SuperMeshRenderSystem MeshRenderSystem { get; init; }
			 public required Brutal.VulkanApi.VkSampler ImGuiSampler { get; init; }

			 public static void Install(TerminalRenderServices services) => Current = services;
			 public static void Clear() => Current = null;
	 }
	 ```

	 Notes:
	 - This file intentionally lives in `purrTTY.Display` because the renderer/cache/backing-store types live there.
	 - If direct `Core.Renderer` or KSA types require additional references in `purrTTY.Display.csproj`, add those references there, not in `purrTTY.Core`.
	 - `Program.GetRenderer()` is the verified source for the `Renderer` instance.
	 - `Program.Instance.TextureSystem`, `MaterialSystem`, and `SuperMeshRenderSystem` are public fields.

2. Install the services from `purrTTY.GameMod/TerminalMod.cs` during `OnFullyLoaded` or `InitializeTerminal` before constructing `TerminalController`.

	 Suggested code:

	 ```csharp
	 using KSA;
	 using purrTTY.Display.Rendering.TerminalTexture;

	 private static void InstallTerminalRenderServices()
	 {
			 var program = Program.Instance;
			 if (program == null)
			 {
					 ModLog.Log.Debug("purrTTY texture rendering unavailable: Program.Instance is null");
					 return;
			 }

			 TerminalRenderServices.Install(new TerminalRenderServices
			 {
					 Renderer = Program.GetRenderer(),
					 TextureSystem = program.TextureSystem,
					 MaterialSystem = program.MaterialSystem,
					 MeshRenderSystem = program.SuperMeshRenderSystem,
					 ImGuiSampler = Program.LinearClampedSampler
			 });
	 }
	 ```

3. Clear the services on unload.

	 ```csharp
	 TerminalRenderServices.Clear();
	 ```

4. Add conservative logging when services are absent and keep the current direct/cached ImGui path active.

### Acceptance criteria

- Game mod still starts with no behavior change when texture rendering is disabled.
- Playground/tests can still construct display classes without a live `Program.Instance`.
- A debug log identifies whether KSA renderer services were installed.

## Phase 1: Implement a GPU terminal render target resource

### Goal

Create a reusable `TerminalRenderTexture` that owns the Vulkan render target, render pass, framebuffer, ImGui texture descriptor, and optional bindless texture registration.

### Preferred render target choice

Use `KSA.RenderTarget`, not `KSA.OffscreenTarget`, for the first terminal texture resource.

Reason:

- `RenderTarget.CreateRenderPass(...)` sets color final layout to `ShaderReadOnlyOptimal`.
- It uses 1x samples, which keeps the first implementation simple.
- `OffscreenTarget` follows the viewport MSAA path and leaves the color target in `ColorAttachmentOptimal`; that is correct for KSA's main off-screen pass but less convenient for immediate sampling.

### New file

`purrTTY.Display/Rendering/TerminalTexture/TerminalRenderTexture.cs`

### Responsibilities

- Allocate or resize a `KSA.RenderTarget` for terminal pixel dimensions.
- Create a render pass with final color layout `ShaderReadOnlyOptimal`.
- Build a framebuffer.
- Register `ColorImage.ImageView` with `ImGuiBackend.Vulkan.AddTexture(...)`.
- Optionally expose a bindless texture handle for material rendering.
- Dispose in the reverse order, waiting for device idle before destroying GPU resources until a frame-safe defer queue exists.

### Suggested shape

```csharp
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.VulkanApi;
using KSA;

namespace purrTTY.Display.Rendering.TerminalTexture;

internal sealed class TerminalRenderTexture : IDisposable
{
		private readonly TerminalRenderServices _services;
		private RenderTarget? _target;
		private VkRenderPass _renderPass;
		private ImTextureRef? _imguiTexture;
		private int? _bindlessTextureHandle;
		private int _width;
		private int _height;

		public int Width => _width;
		public int Height => _height;
		public bool IsReady => _target != null && _imguiTexture.HasValue;
		public ImTextureRef ImGuiTexture => _imguiTexture ?? default;
		public int? BindlessTextureHandle => _bindlessTextureHandle;
		public RenderTarget Target => _target ?? throw new InvalidOperationException("Terminal texture is not allocated");
		public VkRenderPass RenderPass => _renderPass;

		public TerminalRenderTexture(TerminalRenderServices services)
		{
				_services = services;
		}

		public void EnsureSize(int width, int height)
		{
				width = Math.Max(1, width);
				height = Math.Max(1, height);

				if (_target != null && _width == width && _height == height)
				{
						return;
				}

				DisposeGpuResources(waitIdle: true);

				var renderer = _services.Renderer;
				_target = new RenderTarget(
						renderer,
						"purrTTY Terminal Texture",
						new VkExtent2D(width, height),
						renderer.ColorFormat,
						VkFormat.Undefined);

				_renderPass = _target.CreateRenderPassWithOptions(VkImageLayout.ShaderReadOnlyOptimal);
				_target.BuildFramebuffer(_renderPass);

				_imguiTexture = ImGuiBackend.Vulkan.AddTexture(
						_services.ImGuiSampler,
						_target.ColorImage.ImageView,
						VkImageLayout.ShaderReadOnlyOptimal);

				// Register for game materials only if this target will be used in 3D.
				// This requires a SimpleVkTexture-like wrapper or a small adapter because
				// GpuTextureSystem.BindTexture currently accepts SimpleVkTexture.
				_width = width;
				_height = height;
		}

		public void Dispose()
		{
				DisposeGpuResources(waitIdle: true);
		}

		private void DisposeGpuResources(bool waitIdle)
		{
				if (waitIdle)
				{
						_services.Renderer.Device.WaitIdle();
				}

				if (_imguiTexture.HasValue)
				{
						ImGuiBackend.Vulkan.RemoveTexture(_imguiTexture.Value);
						_imguiTexture = null;
				}

				if (_bindlessTextureHandle.HasValue)
				{
						_services.TextureSystem.Free(_bindlessTextureHandle.Value);
						_bindlessTextureHandle = null;
				}

				if (_renderPass.IsNotNull())
				{
						_services.Renderer.Device.DestroyRenderPass(_renderPass, null);
						_renderPass = default;
				}

				_target?.Dispose();
				_target = null;
				_width = 0;
				_height = 0;
		}
}
```

### Important correction for bindless registration

`GpuTextureSystem.BindTexture(...)` accepts `SimpleVkTexture`, but a `RenderTarget` owns a `FramebufferAttachment`, not a `SimpleVkTexture`. There are three implementation options:

1. Add a small helper to `TerminalRenderTexture` that writes directly to the bindless library using reflection or a new method on `GpuTextureSystem` if available at runtime. This is not preferred unless the public method exists in the binary.
2. Create a companion `SimpleVkTexture` instead of `RenderTarget`, using `SimpleVkTexture.CreateInfo` with `ColorAttachmentBit | SampledBit | TransferSrcBit | TransferDstBit | StorageBit`, then create a custom framebuffer from `simpleTexture.ImageView`. This makes bindless registration trivial because `BindTexture(simpleTexture)` works.
3. Keep `RenderTarget` for ImGui preview first, then implement the 3D path by adding a `GpuTextureSystem.BindImageView(VkImageView)` extension in purrTTY using reflection only if direct access to the underlying `BindlessTextureLibrary` is possible.

Preferred implementation for both ImGui and 3D consumers: use a custom `SimpleVkTexture`-backed render target wrapper so the same image can be passed to `ImGuiBackend.Vulkan.AddTexture(...)` and `GpuTextureSystem.BindTexture(...)`.

### Acceptance criteria

- Allocating, resizing, and disposing the terminal render texture does not crash the game.
- The texture can be shown in an ImGui debug window with `ImGui.Image(texture.ImGuiTexture, size)`.
- Resize destroys and recreates descriptors/render pass/framebuffer safely.
- If bindless registration is enabled, the material handle samples the same image view.

## Phase 2: Render ImGui terminal content into the off-screen target

### Goal

Render the existing terminal UI into `TerminalRenderTexture` without rewriting terminal glyph/layout code.

### Main technical challenge

KSA's public `ImGuiBackendVulkanImpl.RenderDrawData(ImDrawDataPtr drawData, CommandBuffer commandBuffer)` can draw any `ImDrawData`, but it uses the backend pipeline that was created against the render pass passed to `ImGuiBackendVulkanImpl.CreateInfo`. KSA initializes that backend with `renderer.MainRenderPass`. Vulkan graphics pipelines are render-pass compatible, not universally portable.

Because of that, Phase 2 has two implementation tracks:

1. Track A: reuse the KSA ImGui backend if its pipeline is compatible with the terminal render pass.
2. Track B: clone the ImGui backend pipeline setup for a terminal-specific render pass if compatibility fails.

Track A should be attempted first because it is small. Track B is the robust path.

### Track A: Direct `RenderDrawData` into a terminal render pass

1. Add `TerminalImGuiTextureRenderer`.

	 New file:

	 `purrTTY.Display/Rendering/TerminalTexture/TerminalImGuiTextureRenderer.cs`

2. Create a command buffer using the renderer's graphics command pool.

	 Verified source:

	 - `Core.Renderer.GraphicsAndComputeCommandPool`
	 - `Core.Renderer.GraphicsAndCompute`
	 - `Core.Renderer.TrySubmitFrame(...)` submits the main frame command buffer, but for this prototype use a one-shot command buffer and fence, matching patterns in `ImGuiBackendVulkanImpl.Helpers.CreateOrResizeWindow(...)` and `BiomeMapsExporter`.

3. Record:

	 ```csharp
	 var cmd = renderer.Device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo
	 {
			 CommandPool = renderer.GraphicsAndComputeCommandPool,
			 Level = VkCommandBufferLevel.Primary
	 });

	 cmd.Begin(new VkCommandBufferBeginInfo
	 {
			 Flags = VkCommandBufferUsageFlags.OneTimeSubmitBit
	 });

	 var clear = new VkClearValue
	 {
			 Color = new VkClearColorValue { Float32 = new float4(0f, 0f, 0f, 0f) }
	 };

	 var begin = new VkRenderPassBeginInfo
	 {
			 RenderPass = texture.RenderPass,
			 Framebuffer = texture.Target.FrameBuffer,
			 RenderArea = new VkRect2D
			 {
					 Extent = new VkExtent2D(texture.Width, texture.Height)
			 },
			 ClearValueCount = 1,
			 ClearValues = &clear
	 };

	 cmd.BeginRenderPass(in begin, VkSubpassContents.Inline);
	 ImGuiBackend.Vulkan.RenderDrawData(drawData, cmd);
	 cmd.EndRenderPass();
	 cmd.End();
	 ```

4. Submit and wait for the prototype.

	 ```csharp
	 var fence = renderer.Device.CreateFence(new VkFenceCreateInfo(), null);
	 var cmdHandle = cmd.Handle;
	 var submit = new VkSubmitInfo
	 {
			 CommandBufferCount = 1,
			 CommandBuffers = &cmdHandle
	 };

	 renderer.GraphicsAndCompute.Submit(new Span<VkSubmitInfo>(ref submit), fence);
	 renderer.Device.WaitForFences(new ReadOnlySpan<VkFence>((VkFence)fence), true, -1);
	 renderer.Device.DestroyFence(fence, null);
	 renderer.Device.FreeCommandBuffers(renderer.GraphicsAndComputeCommandPool, new ReadOnlySpan<CommandBuffer>((CommandBuffer)cmd));
	 ```

5. If validation or runtime errors show render pass incompatibility, stop Track A and implement Track B.

### Track B: Terminal-specific ImGui draw-data renderer

If Track A fails, clone the relevant `ImGuiBackendVulkanImpl` behavior into a terminal-specific renderer.

Required backend behavior from `ImGuiBackendVulkanImpl`:

- Upload `ImDrawVert` and index data to per-frame host-visible buffers.
- Bind the ImGui pipeline.
- Bind descriptor set from each draw command's `TexID`.
- Set scissor from `ClipRect`, adjusted by `drawData.DisplayPos` and `drawData.FramebufferScale`.
- Push scale/translate constants for ImGui's orthographic transform.
- Draw indexed ranges.

Implementation outline:

1. New file: `purrTTY.Display/Rendering/TerminalTexture/TerminalImGuiDrawDataRenderer.cs`.
2. Create a pipeline against `TerminalRenderTexture.RenderPass`.
3. Reuse the shader source strategy from `ImGuiBackendVulkanImpl` if accessible. If private static shader arrays are inaccessible, use the game's shader asset pipeline only if an equivalent ImGui shader exists; otherwise clone the SPIR-V arrays into purrTTY under a clear note that they are generated backend shader bytecode from the decompiled binding and should be replaced by loaded shader assets if possible.
4. Match descriptor layout: one `CombinedImageSampler` descriptor set at set 0, same as the ImGui backend.
5. Keep buffer lifetime per `renderer.SwapchainImageCount` or `renderer.MaxFramesInFlight`.

### Capturing only the terminal window

There are three ways to obtain draw data for the terminal:

1. Full current ImGui frame capture, simplest but renders every visible ImGui window into the texture. This is only acceptable for a debug proof.
2. Secondary ImGui context for terminal-only rendering. More isolated, but it requires duplicating font atlas and IO setup.
3. purrTTY command capture into a fake draw target, then convert recorded commands into an ImGui draw list/draw data or render them directly.

Preferred for implementation:

- Use existing `ITerminalBackingStore` capture path and create a `TextureBackingStore`.
- `CachedRenderStrategy` already calls `_gridRenderer.Render(session, captureTarget, drawPos, charWidth, lineHeight, default(TextSelection))` on cache misses.
- The key change is that `TextureBackingStore.GetDrawTarget()` should capture terminal commands in texture-local coordinates, not screen coordinates.

### Required coordinate change

The current render path passes `drawPos` as an absolute screen position. For off-screen rendering, pass `float2.Zero` when capturing to texture.

Modify `CachedRenderStrategy` or add a new strategy so texture capture uses:

```csharp
var captureOrigin = context.IsTextureCapture ? float2.Zero : drawPos;
_gridRenderer.Render(session, captureTarget, captureOrigin, charWidth, lineHeight, default(TextSelection));
```

Do not include window position in the render cache key for texture content. Window movement should not invalidate an off-screen terminal texture.

Suggested change:

- Add a flag or separate key factory so `TerminalRenderKey.WindowX` and `WindowY` are zero for texture-backed caching.
- Keep the existing key behavior for the command-buffer/screen-backed cache if it still needs absolute positions.

### Acceptance criteria

- Terminal texture contains only the terminal grid background/text/decorations, not the whole game UI.
- Moving the ImGui terminal window does not force re-rendering the off-screen texture.
- Selection and cursor can be controlled independently:
	- Base texture: terminal grid without selection.
	- Optional overlay: cursor and selection rendered to the visible ImGui window as before, or included in texture if using the in-world terminal as the primary display.
- If Track A fails, the failure is logged once and Track B is selected automatically or behind a config flag.

## Phase 3: Add `TextureBackingStore` to purrTTY

### Goal

Plug the texture render path into purrTTY's existing cache/strategy system.

### New file

`purrTTY.Display/Rendering/TerminalTexture/TextureBackingStore.cs`

### Responsibilities

- Implement `ITerminalBackingStore`.
- Own a `TerminalRenderTexture`.
- During `BeginCapture(width, height)`, resize the texture.
- During capture, record terminal draw commands in texture-local coordinates.
- During `EndCapture()`, render the recorded commands into the texture.
- During `Draw(...)`, draw `ImGui.Image(...)` or `drawList.AddImage(...)` into the current ImGui window for debug/normal UI preview.
- Expose a `TerminalRenderTexture` property for 3D display adapters.

### Design details

The first implementation can reuse `CommandBufferBackingStore`'s managed command list to avoid immediate Vulkan text rendering. The rendering sequence becomes:

1. `TerminalGridRenderer.Render(...)` writes `DrawCommand` values into a command list.
2. `TextureBackingStore.EndCapture()` replays those commands into an off-screen ImGui draw list/draw data, or into a custom direct renderer.
3. The resulting texture is drawn on screen through `drawList.AddImage(texture.ImGuiTexture, ...)`.

If constructing a standalone `ImDrawData` is too awkward with BRUTAL's generated wrappers, use the direct terminal renderer fallback:

- Rectangles: simple colored quads.
- Lines/underlines: line list or triangulated strokes.
- Text: harder because it needs ImGui font atlas sampling. Use ImGui draw-data renderer if at all possible for text.

Because text is the hard part, the recommended first path is to render the terminal UI through ImGui draw data, not through a custom glyph renderer.

### Minimal `Draw` preview

Use ImGui backend texture IDs directly:

```csharp
public void Draw(ImDrawListPtr drawList, float2 position, float2 size)
{
		if (!IsReady)
		{
				return;
		}

		drawList.AddImage(
				_texture.ImGuiTexture,
				position,
				position + size,
				new float2(0f, 0f),
				new float2(1f, 1f),
				0xFFFFFFFF);
}
```

If `ImDrawListPtr.AddImage` overload differs in BRUTAL, use `ImGui.Image(_texture.ImGuiTexture, size)` in a local preview window first, then adapt to draw-list placement.

### Wiring

Change `TerminalControllerBuilder.BuildUiSubsystems(...)` to select the backing store based on runtime services/config:

```csharp
var services = TerminalRenderServices.Current;
ITerminalBackingStore backingStore = services != null
		? new TextureBackingStore(services)
		: new CommandBufferBackingStore();

var renderCache = new TerminalViewportRenderCache(backingStore);
_renderStrategy = new CachedRenderStrategy(renderCache, gridRenderer);
```

Keep try/catch fallback to `DirectRenderStrategy`.

### Acceptance criteria

- Existing ImGui terminal window can be rendered from the texture-backed cache.
- If texture setup fails, terminal falls back to existing direct/cache rendering.
- The backing store exposes the texture to world display code without coupling world rendering into `TerminalUiRender`.

## Phase 4A: Render texture on a world-space quad/billboard

### Goal

Display the terminal texture in the 3D world without depending on existing SubPart material replacement.

### Why this is first-class, not a fallback

The world-space quad path is the least invasive true 3D path. It avoids mutating existing part templates and avoids patching part renderer internals. It can be used for debug, IVA overlay, or an eventual attach-to-part transform.

### Implementation strategy

Use KSA's existing `SuperMeshRenderSystem` and PBR material path:

1. Register the terminal render texture with the game's bindless texture system.
2. Create a `MaterialData` whose `AlbedoTexture` is the terminal texture handle.
3. Create/register a quad mesh in `SuperMeshRenderSystem.MeshIndirectSystem`.
4. Register that mesh with a `MeshRenderTechnique` bucket.
5. Each frame, submit an `InstanceData` with `data.X = materialHandle` and `model = world transform`.

### Task 4A.1: Bind the terminal image as a material texture

If using a `SimpleVkTexture`-backed terminal texture, this is direct:

```csharp
int bindlessTexture = services.TextureSystem.BindTexture(simpleVkTexture);
```

If using `RenderTarget`, implement a wrapper or switch to `SimpleVkTexture` as described in Phase 1.

Then create a material:

```csharp
var materialName = new AssetName("purrTTY.Terminal.Material");

services.MaterialSystem.CreateObject(materialName, new MaterialData
{
		AlbedoTexture = bindlessTexture,
		NormalTexture = services.TextureSystem.DefaultWhiteTexture.BindlessHandle,
		RoughMetallicAOTexture = services.TextureSystem.DefaultWhiteTexture.BindlessHandle,
		Sampler = services.TextureSystem.SamplerClampHandle,
		AlbedoColor = float4.One,
		RoughnessMetalScale = new float4(1f, 0f, 1f, 1f),
		EmissiveTexture = bindlessTexture,
		ExtraData = float4.Zero
});

int materialHandle = services.MaterialSystem.GetOrLoad(materialName).Handle;
```

Notes:

- If the PBR shader treats emissive strongly, using the terminal texture as `EmissiveTexture` can make the terminal readable in low light.
- If the shader expects `RoughMetallicAOTexture` channels, use default white/neutral material textures from `GltfPbrSystem` if available. Otherwise use `DefaultWhiteTexture` as a first pass.

### Task 4A.2: Create/register a quad mesh

The default static PBR renderer expects `InterleavedVertex`, not just positions. `MeshIndirectSystem<InterleavedVertex>.AddMesh(...)` requires a `MeshAsset` containing both `MeshAttribute.Interleaved` and `MeshAttribute.Position`.

Implementation options:

1. Preferred: create a tiny GLTF quad asset in purrTTY content and load it through `GltfSystem`. This avoids manually constructing `MeshAsset` internals.
2. Direct runtime mesh: build a `MeshAsset` with four vertices, UVs, normals, tangents, and indices, then call `services.MeshRenderSystem.MeshIndirectSystem.AddMesh(assetName, meshAsset)`.
3. Custom renderer: build a simpler quad pipeline that does not use `InterleavedVertex` or PBR. This is clean for a terminal screen but requires shader work.

Preferred first implementation: content GLTF quad.

Content tasks:

- Add `purrTTY.GameMod/Content/...` or the appropriate mod content folder entry for a quad mesh if StarMap/KSA content loading supports mod assets from purrTTY.
- Add a material placeholder. The runtime material handle will replace or override it.
- Verify the asset appears in `ModLibrary` by ID.

If mod content loading is uncertain, use a runtime `MeshAsset` and validate through `MeshIndirectSystem.AddMesh(...)`.

### Task 4A.3: Implement `TerminalWorldQuad`

New file:

`purrTTY.Display/Rendering/TerminalTexture/TerminalWorldQuad.cs`

Suggested shape:

```csharp
internal sealed class TerminalWorldQuad : IDisposable
{
		private readonly TerminalRenderServices _services;
		private readonly TerminalRenderTexture _texture;
		private MeshBucketHandle _meshHandle;
		private int _materialHandle;
		private bool _registered;

		public bool Visible { get; set; }
		public float4x4 Transform { get; set; } = float4x4.Identity;

		public void EnsureRegistered()
		{
				// Create material if needed.
				// Register quad mesh with services.MeshRenderSystem.MeshRendererStaticPbr.MeshBucketSystem.
		}

		public void Draw()
		{
				if (!Visible || !_registered)
				{
						return;
				}

				var instance = new InstanceData
				{
						model = Transform,
						data = new float4(_materialHandle, 0f, 0f, 0f)
				};

				_services.MeshRenderSystem.MeshRendererStaticPbr.MeshBucketSystem.DrawMeshInstance(_meshHandle, instance);
		}
}
```

### Task 4A.4: Submit the quad every frame

Call `TerminalWorldQuad.Draw()` from `TerminalMod.OnBeforeUi` or `OnAfterUi` depending on when KSA collects mesh bucket instances.

Important timing note:

- KSA mesh renderables like `StaticMeshRenderable.Draw()` enqueue mesh bucket instances during game update/render prep, not inside command-buffer recording directly.
- Start by calling `TerminalWorldQuad.Draw()` in `[StarMapBeforeGui]`, because KSA's main `Program.OnPreRender` and render-frame setup happen before/around UI in the game loop. If the quad does not appear, use a Harmony postfix on a known per-frame draw/update point such as `Program.OnPreRender` or a method that already calls other `IModelDrawer.Draw()` implementations.

### Task 4A.5: Transform options

Provide at least these placement modes:

1. Camera-facing debug billboard in front of the main camera.
2. Vehicle-relative fixed panel using `Program.ControlledVehicle.GetWorldMatrix(viewport.GetCamera())` as seen in `PbrSpheres.Draw(...)`.
3. Part-relative transform if a target `Part` is selected/found.

Debug billboard sketch:

```csharp
var viewport = Program.MainViewport;
var camera = viewport.GetCamera();
var distance = 8f;
var sizeMeters = new float2(2.4f, 1.35f);

// Pseudocode: use actual camera forward/up/right APIs from KSA Camera after verifying names.
var position = camera.PositionEgo + camera.ForwardEgo * distance;
var model = float4x4.CreateScale(sizeMeters.X, sizeMeters.Y, 1f) *
						CreateBillboardMatrix(position, camera);
```

Part-relative sketch:

```csharp
Vehicle? vehicle = Program.ControlledVehicle;
Part? targetPart = FindPartById(vehicle, configuredPartId);

if (vehicle != null && targetPart != null && vehicle.GetWorldMatrix(Program.MainViewport.GetCamera()).HasValue)
{
		float4x4 vehicleWorld = vehicle.GetWorldMatrix(Program.MainViewport.GetCamera()).Value;
		float4x4 panelLocal = BuildPanelLocalTransform(offset, rotation, scale);
		quad.Transform = panelLocal * vehicleWorld;
}
```

The exact part transform APIs must be verified against runtime reflection because decompiled KSA field names can differ from the runtime binary.

### Acceptance criteria

- Terminal texture appears on a world-space quad.
- Quad can be toggled independently from the normal ImGui terminal window.
- Quad updates when terminal content changes.
- Device/resource cleanup happens on mod unload.
- If mesh bucket submission timing is wrong, document the verified hook or patch used.

## Phase 4B: Render texture on an existing SubPart/material

### Goal

Replace or augment an existing SubPart material so the terminal texture appears on a real game object surface.

### Reality check

The decompiled code does not show a public API for mutating an existing SubPart material after model creation. SubPart rendering data is derived from templates and part model instances. Therefore this path is likely to need one of:

1. Runtime template/material creation before the part model is instantiated.
2. A custom part/subpart asset in purrTTY content whose material is known and can be created with the terminal texture.
3. Harmony patching of material resolution or renderer submission for a specific SubPart/material ID.

### Strategy 4B.1: Runtime material with purrTTY-owned part/subpart asset

This is the cleanest SubPart path if KSA mod content loading supports purrTTY asset XML/GLTF.

Tasks:

1. Add a purrTTY terminal screen part or SubPart asset with a simple rectangular mesh and a stable material ID.
2. During mod initialization, create a runtime `MaterialData` with that material ID using `Program.Instance.MaterialSystem.CreateObject(...)` before the asset is rendered.
3. Use the terminal texture bindless handle as `AlbedoTexture` and `EmissiveTexture`.
4. Add/attach the part through the existing part system, or require it to exist in a vehicle design.

Pros:

- Avoids patching existing renderer internals.
- The material is owned by purrTTY and stable.

Risks:

- Requires exact KSA content/mod asset format.
- The material may be loaded before purrTTY creates the runtime material; ordering must be verified.

### Strategy 4B.2: Existing material ID replacement through `GpuMaterialSystem`

If the target SubPart uses a known material ID, attempt a runtime replacement by creating a material object with the same `AssetName` before it is loaded.

Tasks:

1. Use runtime reflection/debug UI to print the target part's `PartModel.Template.Material.Id`.
2. Before the material is first requested, call:

	 ```csharp
	 services.MaterialSystem.CreateObject(targetMaterialName, terminalMaterialData);
	 ```

3. Let normal `MaterialSystem.GetOrLoad(targetMaterialName)` return the already-added asset.

Pros:

- No renderer patch if ordering works.

Risks:

- If the material is already loaded, `CreateObject` returns false because `AssetManager.TryAdd` fails.
- Replacing a shared material ID will affect every mesh using that material.
- This is not suitable for arbitrary existing vehicle materials unless scoped to a purrTTY-owned material ID.

### Strategy 4B.3: Harmony targeted material handle override

Use Harmony only when a direct material registration path cannot target the desired SubPart instance.

Patch candidates to investigate and verify at runtime:

- `PartModel` construction or draw submission where `Template.Material` is converted to a material handle.
- `PartModelRenderer` color/prepass draw submission if it passes `InstanceData.data.X` as material handle.
- `RaytracingRenderer` material array population for raytracing/IVA, where `SubPartMaterials[i] = materialSystem.GetOrLoad(partModel.Template.Material?.Id).Handle` is visible in the decompiled code.

Patch design:

1. Keep a static registry mapping target SubPart template/material IDs to replacement material handles.
2. Patch the smallest method that resolves material handle for a mesh draw.
3. In postfix, replace only when all of these match:
	 - part or SubPart ID matches user config;
	 - material ID matches expected original;
	 - terminal texture/material is ready;
	 - replacement has not been applied to unrelated parts.
4. Do not patch descriptor set binding globally. Patch data selection for one target.

Patch sketch:

```csharp
[HarmonyPatch]
internal static class PartModelMaterialOverridePatch
{
		static MethodBase? TargetMethod()
		{
				// Resolve via AccessTools after runtime reflection confirms the exact method.
				return AccessTools.Method(typeof(PartModel), "...");
		}

		static void Postfix(object __instance, ref int __result)
		{
				if (!TerminalMaterialOverrideRegistry.TryGetReplacement(__instance, out int materialHandle))
				{
						return;
				}

				__result = materialHandle;
		}
}
```

Use the repository's Harmony conventions from `purrTTY.GameMod/Patcher.cs` and the existing patch folder.

### Runtime reflection task for SubPart path

Because KSA decompiled field names can differ from runtime, add a debug ImGui button before implementing a patch:

```csharp
if (ImGui.Button("Dump purrTTY target part render data"))
{
		foreach (var part in Program.ControlledVehicle?.Parts.Parts ?? [])
		{
				DumpObjectGraph(part, maxDepth: 4);
		}
}
```

Dump at minimum:

- part ID/display name;
- `SubParts` IDs;
- modules/components containing `PartModel`, `PartModelDynamic`, or render-related names;
- template mesh ID;
- template material ID;
- current material handle if visible.

### Acceptance criteria

- A purrTTY-owned SubPart/material can show the terminal texture without affecting unrelated materials.
- If patching is used, the patch is scoped by part/material ID and fails closed when the target cannot be identified.
- Raytracing/IVA and non-raytracing paths are documented separately if they use different material arrays.

## Phase 5: Input mapping for 3D terminal interaction

### Goal

Allow mouse interaction with the 3D terminal surface after it is visible.

### Tasks

1. For world quad, implement ray-plane intersection from camera cursor ray to panel transform.
2. Convert hit point to terminal pixel coordinates.
3. Convert terminal pixel coordinates to row/column using current `charWidth` and `lineHeight`.
4. Feed existing purrTTY mouse input paths rather than duplicating terminal protocol logic.

Relevant purrTTY files:

- `purrTTY.Display/Controllers/TerminalUi/TerminalUiInput.cs`
- `purrTTY.Display/Controllers/TerminalUi/TerminalUiMouseTracking.cs`
- `purrTTY.Display/Controllers/TerminalUi/TerminalUiSelection.cs`

Implementation note:

- Current purrTTY input assumes ImGui invisible button hover/active state and screen-space coordinates from `TerminalUiRender`.
- Extract a coordinate-based input method such as `HandleTerminalPointer(pixelPosition, buttons, wheel, modifiers)` so both ImGui-window and 3D-surface input can call the same logic.

Acceptance criteria:

- Clicking the 3D terminal focuses purrTTY input.
- Selection and mouse tracking can be toggled/tested.
- Normal game input is suppressed only while the 3D terminal interaction is active.

## Phase 6: Configuration and UX

### Goal

Expose the feature without destabilizing the existing terminal window.

### Tasks

1. Add settings under the existing purrTTY settings panel:
	 - Enable terminal texture backing store.
	 - Show ImGui texture preview.
	 - Show world quad.
	 - Show on SubPart/material target.
	 - Texture width/height override or auto-size from terminal cells.
	 - World placement mode: camera billboard, vehicle-relative, part-relative.
	 - Target part ID/material ID.
2. Persist settings through existing theme/config infrastructure where purrTTY UI settings already live.
3. Add debug readouts:
	 - texture size;
	 - ImGui texture ID/descriptor set handle;
	 - bindless texture handle;
	 - material handle;
	 - current world quad visibility;
	 - last render/update failure.

Acceptance criteria:

- Feature defaults off unless the texture path is known stable.
- Existing F12 terminal window behavior is unchanged by default.
- User can enable a preview and see whether the texture is updating before enabling 3D placement.

## Phase 7: Validation and diagnostics

### Build/test commands

Use the repo's required test runner:

```powershell
.\scripts\dotnet-test.ps1
```

If game DLL deployment is locked while KSA is running, compile the game mod with a safe dist override:

```powershell
dotnet build purrTTY.GameMod/purrTTY.GameMod.csproj /p:DistDir=C:\Users\Alex\repos\meow-sci\purrtty\purrTTY.GameMod\dist-verify\
```

### Runtime validation checklist

1. Start game with texture feature disabled. Existing terminal works.
2. Enable ImGui texture preview. Texture allocates and displays transparent/blank clear color before terminal draw capture is enabled.
3. Trigger terminal text output. Texture updates only when `TerminalRenderKey` changes.
4. Resize terminal. Texture is recreated, old descriptors are freed, no crash.
5. Enable world quad. Quad appears in front of camera or near vehicle.
6. Toggle terminal visibility. Decide whether hidden terminal still updates the 3D texture; document chosen behavior.
7. Switch scenes/vehicles. Resources survive or cleanly reinitialize.
8. Unload mod. Device resources are destroyed without validation errors/crash.

### Diagnostic logs to add

- Service installation success/failure.
- Texture allocation size/format.
- Render pass creation success/failure.
- ImGui descriptor creation/removal.
- Bindless handle creation/removal.
- Material creation success/failure and asset name.
- Quad mesh registration success/failure.
- SubPart target resolution success/failure.

## Risk register

### Render pass compatibility

Risk: `ImGuiBackend.Vulkan.RenderDrawData` uses a pipeline created for KSA's main render pass, and Vulkan may reject or misrender against the terminal render pass.

Mitigation: Attempt Track A only as a prototype. Build Track B terminal-specific pipeline if needed.

### GPU synchronization

Risk: One-shot command buffers with immediate waits are simple but can stall the frame.

Mitigation: Use waits for the prototype. Later integrate with KSA's frame resources or submit texture rendering before the main pass samples the texture. Avoid destroying resources until the GPU is idle or a per-frame retirement queue confirms safe destruction.

### Texture image layout

Risk: Texture is rendered as a color attachment but sampled as shader read.

Mitigation: Prefer `RenderTarget.CreateRenderPassWithOptions(ShaderReadOnlyOptimal)`. If using `OffscreenTarget`, add an explicit image barrier to `ShaderReadOnlyOptimal` before sampling.

### Font atlas and ImGui texture IDs

Risk: Rendering terminal text via ImGui draw data requires font atlas texture IDs to be valid for the renderer.

Mitigation: Use KSA's initialized `ImGuiBackend.Vulkan`; it already handles `drawData.Textures` and texture updates. Avoid secondary context until necessary.

### Coordinate contamination

Risk: Current cache key and draw commands include screen/window coordinates.

Mitigation: Texture capture must use texture-local coordinates and zero window position in the render key.

### SubPart material coupling

Risk: Existing SubPart material replacement can affect unrelated meshes or fail due to load order.

Mitigation: Prefer purrTTY-owned material IDs. If patching, scope by part ID and material ID, and fail closed.

## Recommended implementation order

1. Add `TerminalRenderServices` and install it from `TerminalMod`.
2. Add `TerminalRenderTexture` and prove it can be previewed in an ImGui window as a clear-color texture.
3. Add `TextureBackingStore` and render terminal content into the texture.
4. Add texture-backed preview in the normal terminal window while keeping fallback rendering.
5. Register the texture as a bindless material texture and create a runtime `MaterialData`.
6. Add world-space quad display and placement controls.
7. Add SubPart/material integration with purrTTY-owned asset or tightly scoped Harmony patch.
8. Add 3D input mapping after the display path is stable.

## Open questions for implementation agents

- Does the runtime KSA binary expose the same `ImGuiBackendVulkanImpl.AddTexture`, `RenderDrawData`, `Program.GetRenderer`, and `Program.LinearClampedSampler` signatures as the decompiled source?
- Does `ImDrawListPtr` in the runtime binding expose `AddImage` overloads that accept `ImTextureRef`, or should preview use `ImGui.Image`?
- Is purrTTY mod content loaded early enough to add a quad GLTF/material asset through KSA's normal `ModLibrary` path?
- Which game lifecycle hook reliably runs before mesh bucket lists are consumed for world rendering? Start with `[StarMapBeforeGui]`, then patch a verified KSA method if needed.
- For SubPart display, is the desired target a purrTTY-owned screen part, a specific existing cockpit/surface, or arbitrary user-selected part material?
