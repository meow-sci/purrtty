using System;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

namespace purrTTY.GameMod.InWorld.Display;

/// <summary>
///     Phase 6A — owns the GPU resources for a flat textured quad that samples
///     the off-screen color image (the in-world terminal "screen") and gets drawn
///     into the game's main render pass via the <c>SuperMeshRenderSystem.RenderMainPass</c>
///     Harmony postfix in <see cref="purrTTY.GameMod.InWorld.Patches.FramePatches"/>.
///     <para>
///         Reuses KSA's <c>UnlitMesh.{vert,frag}</c> shaders (loaded via
///         <c>ModLibrary.Get&lt;ShaderReference&gt;</c>): vec3 pos + vec2 uv vertex
///         input, single mat4 push constant, and one combined-image-sampler.
///     </para>
///     <para>
///         The model matrix is computed ONCE in <see cref="Anchor"/> when the user
///         toggles the in-world quad ON, then persists in ego space — the quad
///         "rides" with the vessel. KSA renders parts in ego space and provides the
///         view-projection in <c>Camera.MVP.viewProjection</c>; we multiply that
///         with our cached ego-space model matrix every frame to produce the MVP
///         push constant. KSA's main pass uses reverse-Z, so this pipeline matches
///         (<c>RenderingPresets.ReverseZDepthStencil.DepthTestWrite</c>).
///     </para>
///     <para>
///         All methods must be called on the main thread (the only thread that
///         owns the Vulkan device + ImGui contexts). The constructor allocates
///         GPU resources and must run after the renderer is live (the manager
///         calls it from <c>OnFullyLoaded</c>).
///     </para>
/// </summary>
public sealed class QuadDisplay : IDisposable
{
    private readonly Renderer              _renderer;
    private readonly OffscreenRenderTarget _target;
    private readonly Settings.InWorldSettings _settings;

    private readonly DescriptorPoolEx       _descriptorPool;
    private readonly DescriptorSetLayoutEx  _descriptorSetLayout;
    private readonly VkDescriptorSet        _descriptorSet;
    private readonly VkPipelineLayout       _pipelineLayout;
    private readonly VkPipeline             _pipeline;
    private readonly VertexInput            _vertexInput;
    private readonly BufferEx               _vertexBuffer;
    private readonly BufferEx               _indexBuffer;

    private float4x4 _modelEgo = float4x4.Identity;
    private bool     _anchored;
    private bool     _disposed;

    public bool IsAnchored => _anchored;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    private struct QuadVertex
    {
        public float3 Pos;
        public float2 Uv;
    }

    public unsafe QuadDisplay(Renderer renderer, OffscreenRenderTarget target, Settings.InWorldSettings settings)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        if (target   == null) throw new ArgumentNullException(nameof(target));
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        _renderer = renderer;
        _target   = target;
        _settings = settings;

        // The unlit mesh shader pair KSA already ships and uses for non-PBR mesh
        // draws. Get<T> throws NullReferenceException if either id is missing,
        // which the manager catches and uses to disable the feature cleanly.
        ShaderReference vertRef = ModLibrary.Get<ShaderReference>("UnlitMeshVert");
        ShaderReference fragRef = ModLibrary.Get<ShaderReference>("UnlitMeshFrag");
        VkShaderModule  vertModule = vertRef;  // implicit operator
        VkShaderModule  fragModule = fragRef;

        var device = renderer.Device;

        // ---- Descriptor set layout: one combined-image-sampler at binding 0 ----
        var bindingDesc = new VkDescriptorSetLayoutBinding
        {
            Binding         = 0,
            DescriptorType  = VkDescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags      = VkShaderStageFlags.FragmentBit,
        };
        var dslCreateInfo = new DescriptorSetLayoutEx.CreateInfo
        {
            Bindings = new Span<VkDescriptorSetLayoutBinding>(ref bindingDesc),
        };
        _descriptorSetLayout = device.CreateDescriptorSetLayout(dslCreateInfo, null);

        // ---- Descriptor pool: one set, one combined-image-sampler ----
        var poolSize = new VkDescriptorPoolSize
        {
            Type            = VkDescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
        };
        var poolCreateInfo = new DescriptorPoolEx.CreateInfo
        {
            MaxSets   = 1,
            PoolSizes = new Span<VkDescriptorPoolSize>(ref poolSize),
        };
        _descriptorPool = device.CreateDescriptorPool(poolCreateInfo, null);
        _descriptorSet  = device.AllocateDescriptorSet(_descriptorPool, _descriptorSetLayout);

        // ---- Write sampler+image-view into the descriptor set ----
        // The off-screen render pass finalizes the color attachment to
        // ShaderReadOnlyOptimal, so no manual layout transition is needed before
        // sampling. We reuse the target's sampler (linear-clamped, defined in
        // OffscreenRenderTarget) rather than creating a second one.
        var imageInfo = new VkDescriptorImageInfo
        {
            ImageView   = _target.ColorImageView,
            ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
            Sampler     = _target.Sampler,
        };
        VkWriteDescriptorSet write = new VkWriteDescriptorSet
        {
            DstSet          = _descriptorSet,
            DstBinding      = 0,
            DescriptorCount = 1,
            DescriptorType  = VkDescriptorType.CombinedImageSampler,
            ImageInfo       = &imageInfo,
        };
        device.UpdateDescriptorSets(
            new ReadOnlySpan<VkWriteDescriptorSet>(ref write),
            default(ReadOnlySpan<VkCopyDescriptorSet>));

        // ---- Pipeline layout: one descriptor set layout + mat4 push constant (vertex stage) ----
        VkPushConstantRange pushRange = new VkPushConstantRange
        {
            StageFlags = VkShaderStageFlags.VertexBit,
            Offset     = ByteSize.Zero,
            Size       = ByteSize.Of<float4x4>(),
        };
        VkDescriptorSetLayout dslHandle = _descriptorSetLayout;
        _pipelineLayout = device.CreatePipelineLayout(
            new ReadOnlySpan<VkDescriptorSetLayout>(ref dslHandle),
            new ReadOnlySpan<VkPushConstantRange>(ref pushRange),
            null);

        // ---- Vertex input: single binding stride 20, locations 0=pos (vec3), 1=uv (vec2) ----
        _vertexInput = new VertexInput(1, 2)
            .AddBinding(0, ByteSize.Of<QuadVertex>(), VkVertexInputRate.Vertex)
            .AddAttribute(0, 0, VkFormat.R32G32B32SFloat, ByteSize.Zero)
            .AddAttribute(1, 0, VkFormat.R32G32SFloat,   ByteSize.Of<float3>())
            .Check();

        // ---- Shader stages ----
        var stagesArr = stackalloc VkPipelineShaderStageCreateInfo[2];
        stagesArr[0] = new VkPipelineShaderStageCreateInfo
        {
            Stage  = VkShaderStageFlags.VertexBit,
            Module = vertModule,
            Name   = Presets.Entrypoint.Main,
        };
        stagesArr[1] = new VkPipelineShaderStageCreateInfo
        {
            Stage  = VkShaderStageFlags.FragmentBit,
            Module = fragModule,
            Name   = Presets.Entrypoint.Main,
        };

        // ---- Pipeline state ----
        // Reverse-Z to match KSA's main pass: the depth attachment is cleared to
        // 0 and the compare op is GreaterOrEqual. We bind to Program.MainPass.Pass
        // so we MUST follow that convention or the quad will Z-fight / vanish.
        // Cull none: a single quad, both sides visible (the user may rotate the
        // vessel until they see the back of the quad on first toggle and we don't
        // want that to render as invisible).
        var multisample = new VkPipelineMultisampleStateCreateInfo
        {
            RasterizationSamples = Program.MainPass.SampleCount,
        };

        var pipelineInfo = new VkGraphicsPipelineCreateInfo
        {
            Layout             = _pipelineLayout,
            RenderPass         = Program.MainPass.Pass,
            Subpass            = 0,
            StageCount         = 2,
            Stages             = stagesArr,
            DynamicState       = renderer.DynamicStateInfo,
            ViewportState      = renderer.ViewportState,
            VertexInputState   = _vertexInput,
            InputAssemblyState = Presets.InputAssembly.TriangleList,
            RasterizationState = Presets.Rasterization.Fill.CullNone,
            DepthStencilState  = RenderingPresets.ReverseZDepthStencil.DepthTestWrite,
            ColorBlendState    = Presets.BlendState.BlendNone,
            MultisampleState   = &multisample,
        };
        _pipeline = device.CreateGraphicsPipeline(default(VkPipelineCache), pipelineInfo, null);

        // ---- Vertex / index buffers ----
        // TODO Phase 6A iteration: winding and UV orientation are finicky.
        //     The off-screen image is rendered in ImGui coordinates (origin
        //     top-left, +Y down) while Vulkan's clip space has +Y down post-
        //     projection. Tried: CCW front face with UVs mapped (0,0)→top-left,
        //     (1,1)→bottom-right per ImGui convention. If the terminal appears
        //     mirrored or upside-down on first run, flip the V coordinate or
        //     reverse the index winding here rather than touching the camera math.
        // UV mapping: Camera.LookAtRotation(-forward, up) (see Anchor) negates the
        // Z row of the basis matrix, so local +Z ends up pointing in +forward —
        // i.e. AWAY from the camera. With CullMode.None we still draw the quad,
        // but the side facing the camera is the geometric back face, which sees
        // local +X mirrored to the visual left. To compensate we flip the U axis
        // on the source texture: local +X (U was 1) now samples the texture's
        // left edge, so the readable orientation appears upright to the camera.
        // V remains flipped (top-left = (0,0)) to match ImGui's top-left origin.
        Span<QuadVertex> verts = stackalloc QuadVertex[4]
        {
            new QuadVertex { Pos = new float3(-0.5f, -0.5f, 0f), Uv = new float2(1f, 1f) },
            new QuadVertex { Pos = new float3( 0.5f, -0.5f, 0f), Uv = new float2(0f, 1f) },
            new QuadVertex { Pos = new float3( 0.5f,  0.5f, 0f), Uv = new float2(0f, 0f) },
            new QuadVertex { Pos = new float3(-0.5f,  0.5f, 0f), Uv = new float2(1f, 0f) },
        };
        Span<ushort> indices = stackalloc ushort[6] { 0, 1, 2, 0, 2, 3 };

        try
        {
            _vertexBuffer = renderer.Allocator.CreateBuffer(new BufferEx.CreateInfo
            {
                Name                    = "purrTTY-Quad-VB",
                BufferUsage             = VkBufferUsageFlags.VertexBufferBit | VkBufferUsageFlags.TransferDstBit,
                BufferSize              = ByteSize.Of(verts),
                AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
                BufferSharingMode       = VkSharingMode.Exclusive,
            });
            _indexBuffer = renderer.Allocator.CreateBuffer(new BufferEx.CreateInfo
            {
                Name                    = "purrTTY-Quad-IB",
                BufferUsage             = VkBufferUsageFlags.IndexBufferBit | VkBufferUsageFlags.TransferDstBit,
                BufferSize              = ByteSize.Of(indices),
                AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
                BufferSharingMode       = VkSharingMode.Exclusive,
            });

            // Mirror SimpleVkMesh's upload idiom: open ONE staging cmd buffer,
            // record both copies, end, Submit (which fences) + Wait. Synchronous
            // upload is fine here: this runs once at construction.
            using var staging = renderer.Allocator.CreateStagingPool(renderer.Graphics, 1);
            var stagingCmd = staging.NextCommandBuffer();
            stagingCmd.Begin();
            VkUtils.StageAndUploadToBuffer(staging, _vertexBuffer.VkBuffer, _vertexBuffer.BindOffset, verts, stagingCmd);
            VkUtils.StageAndUploadToBuffer(staging, _indexBuffer.VkBuffer,  _indexBuffer.BindOffset,  indices, stagingCmd);
            stagingCmd.End();
            staging.Submit().Wait();
        }
        catch
        {
            // If buffer creation/upload throws after we've already created the
            // pipeline objects, free what was allocated so the caller doesn't
            // leak GPU resources.
            DestroyGpu();
            throw;
        }
    }

    /// <summary>
    ///     Compute the ego-space model matrix ONCE based on the camera's current
    ///     forward/up, then persist it. Subsequent camera motion will not
    ///     re-orient the quad — it stays where it was placed at toggle time, in
    ///     ego space, so it rides with the vessel as the camera rotates.
    /// </summary>
    public void Anchor(Camera camera)
    {
        if (camera == null) throw new ArgumentNullException(nameof(camera));

        // Ego-space placement: the camera sits at the ego origin (0,0,0).
        // The position to put the quad: distance forward of the camera.
        double3 forward = camera.GetForward();
        double3 up      = camera.GetUp();
        double3 posEgo  = forward * _settings.QuadDistanceMeters;

        // Quad's local +Z is its normal (we built it in the XY plane). To face
        // the camera, the quad's normal should point at the camera, i.e. opposite
        // the camera's forward. Use KSA's LookAtRotation helper (mirrors what
        // Camera.LookAt does internally), which builds a quaternion from a forward
        // vector + up vector.
        doubleQuat lookAtCam = Camera.LookAtRotation(-forward, up);

        // Compose. Brutal.Numerics matrices left-multiply column vectors (M * v),
        // so the natural composition is T * R * S — but UnlitMeshRenderTechnique
        // pushes a single `world` matrix and the shader does
        // `worldViewProjMatrix * vec4(inPos,1)`, so this matches the standard
        // SRT convention used everywhere else in KSA.
        float4x4 scale = float4x4.CreateScale(_settings.QuadWidthMeters, _settings.QuadHeightMeters, 1f);
        float4x4 rot   = float4x4.CreateFromQuaternion(floatQuat.Pack(in lookAtCam));
        float4x4 trans = float4x4.CreateTranslation(float3.Pack(in posEgo));
        _modelEgo = scale * rot * trans;
        _anchored = true;
    }

    /// <summary>
    ///     Phase 7A — ego-space ray vs. quad pick. Returns true and the nearer
    ///     positive <c>t</c> when the ray hits one of the two triangles, false
    ///     otherwise. No-op if not yet anchored.
    ///     <para>
    ///         The local-space verts/indices here MUST match the buffers uploaded
    ///         in the constructor: 4 corners in the XY plane, two triangles
    ///         (0,1,2) and (0,2,3) — keep them in sync if either ever changes.
    ///     </para>
    /// </summary>
    public bool TryRaycast(Ray ray, out double t)
    {
        t = double.MaxValue;
        if (_disposed || !_anchored) return false;

        // Local-space corners mirror the QuadVertex array in the ctor. We only
        // need positions for the raycast; UVs are irrelevant for hit-vs-no-hit.
        float3 v0Local = new float3(-0.5f, -0.5f, 0f);
        float3 v1Local = new float3( 0.5f, -0.5f, 0f);
        float3 v2Local = new float3( 0.5f,  0.5f, 0f);
        float3 v3Local = new float3(-0.5f,  0.5f, 0f);

        // float3.Transform(p, M) computes M * vec4(p,1) using KSA's mat-vector
        // convention (same one RecordDraw relies on when it composes the MVP).
        double3 v0 = ToDouble3(float3.Transform(v0Local, _modelEgo));
        double3 v1 = ToDouble3(float3.Transform(v1Local, _modelEgo));
        double3 v2 = ToDouble3(float3.Transform(v2Local, _modelEgo));
        double3 v3 = ToDouble3(float3.Transform(v3Local, _modelEgo));

        bool any = false;
        if (ray.RaycastMollerTrumbore(v0, v1, v2, out double t0))
        {
            t = t0;
            any = true;
        }
        if (ray.RaycastMollerTrumbore(v0, v2, v3, out double t1))
        {
            if (!any || t1 < t)
            {
                t = t1;
            }
            any = true;
        }
        return any;
    }

    private static double3 ToDouble3(float3 v) => new double3(v.X, v.Y, v.Z);

    /// <summary>
    ///     Record bind + draw commands for the quad into the supplied (live,
    ///     inside-render-pass) command buffer. No-op if not yet anchored.
    /// </summary>
    public unsafe void RecordDraw(CommandBuffer cmd)
    {
        if (_disposed || !_anchored) return;

        var camera = Program.GetMainCamera();
        if (camera == null) return;

        // MVP = view * projection * model, all in ego space. Camera.MVP is a
        // ViewProjection struct with a precomputed view*projection matrix.
        float4x4 mvp = _modelEgo * camera.MVP.viewProjection;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        VkDescriptorSet descSetCopy = _descriptorSet;
        cmd.BindDescriptorSets(
            VkPipelineBindPoint.Graphics,
            _pipelineLayout,
            0,
            new ReadOnlySpan<VkDescriptorSet>(ref descSetCopy),
            default(Span<ByteSize32>));

        // Match the rest of KSA's main-pass draws (which all rely on a dynamic
        // viewport set by Program.SetViewport). The patched call into
        // SuperMeshRenderSystem.RenderMainPass already set this for the prior
        // draws but it costs nothing to re-state and it future-proofs us against
        // KSA changing the call order.
        Program.SetViewport(cmd);

        cmd.PushConstants(_pipelineLayout, VkShaderStageFlags.VertexBit, ByteSize.Zero, mvp);

        VkBuffer    vb       = _vertexBuffer.VkBuffer;
        ByteSize64  vbOffset = (ByteSize64)_vertexBuffer.BindOffset;
        cmd.BindVertexBuffers(
            0,
            new ReadOnlySpan<VkBuffer>(ref vb),
            new ReadOnlySpan<ByteSize64>(ref vbOffset));
        cmd.BindIndexBuffer(_indexBuffer.VkBuffer, (ByteSize64)_indexBuffer.BindOffset, VkIndexType.Uint16);
        cmd.DrawIndexed(6, 1, 0, 0, 0);
    }

    private void DestroyGpu()
    {
        var device = _renderer.Device;
        // Reverse construction order. VkShaderModules are owned by ModLibrary —
        // we must NOT destroy them here.
        if (_pipeline.VkHandle != 0)
        {
            try { device.DestroyPipeline(_pipeline, null); } catch { /* best-effort */ }
        }
        if (_pipelineLayout.VkHandle != 0)
        {
            try { device.DestroyPipelineLayout(_pipelineLayout, null); } catch { /* best-effort */ }
        }
        try { _vertexBuffer.Dispose(); } catch { /* best-effort */ }
        try { _indexBuffer.Dispose();  } catch { /* best-effort */ }
        // DescriptorSet is freed implicitly when the pool is destroyed.
        if (_descriptorPool != null)
        {
            try { device.DestroyDescriptorPool(_descriptorPool, null); } catch { /* best-effort */ }
        }
        if (_descriptorSetLayout != null)
        {
            try { device.DestroyDescriptorSetLayout(_descriptorSetLayout, null); } catch { /* best-effort */ }
        }
        try { _vertexInput?.Bindings.Dispose();  } catch { /* best-effort */ }
        try { _vertexInput?.Attributes.Dispose(); } catch { /* best-effort */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyGpu();
    }
}
