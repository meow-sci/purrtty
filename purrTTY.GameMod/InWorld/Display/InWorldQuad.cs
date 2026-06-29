using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using purrTTY.GameMod.InWorld.Settings;
using RenderCore;

namespace purrTTY.GameMod.InWorld.Display;

/// <summary>
///     Owns the GPU resources for a flat, double-sided textured quad that samples
///     the off-screen color image (the in-world terminal "screen") and is drawn
///     into the game's scene pass via the Harmony postfix on
///     <c>SuperMeshRenderSystem.RenderMainPass</c>.
///     <para>
///         Reuses KSA's <c>UnlitMesh.{vert,frag}</c> shaders (vec3 pos + vec2 uv,
///         a single mat4 push constant, one combined-image-sampler). Two anchor
///         modes share one pipeline pair, selected per-frame from
///         <see cref="InWorldSettings"/>:
///         <list type="bullet">
///             <item><b>Part</b> — ego-space MVP anchored to a Part/SubPart pose
///                   (rotation + position, excluding the part's own scale), drawn
///                   with reverse-Z <c>DepthTestWrite</c> so it occludes / is
///                   occluded by the scene.</item>
///             <item><b>Billboard</b> — a view-space MVP (camera-locked HUD panel),
///                   drawn with <c>NoDepthTest</c> (always-on-top) or
///                   <c>DepthTestWrite</c> per <see cref="InWorldSettings.BillboardAlwaysOnTop"/>.</item>
///         </list>
///     </para>
///     <para>
///         Main thread only. The constructor allocates GPU resources and must run
///         after the renderer is live (the manager builds it from <c>OnFullyLoaded</c>
///         or a later Enable). It reads <see cref="InWorldSettings"/> live each
///         frame, so editing the settings updates the quad instantly.
///     </para>
/// </summary>
public sealed class InWorldQuad : IDisposable
{
    private readonly Renderer              _renderer;
    private readonly OffscreenRenderTarget _target;
    private readonly InWorldSettings       _settings;

    private readonly DescriptorPoolEx      _descriptorPool;
    private readonly DescriptorSetLayoutEx _descriptorSetLayout;
    private readonly VkDescriptorSet       _descriptorSet;
    private readonly VkPipelineLayout      _pipelineLayout;
    private readonly VkPipeline            _pipelineDepthWrite;  // part mode / occluding billboard
    private readonly VkPipeline            _pipelineNoDepth;     // always-on-top billboard
    private readonly VertexInput           _vertexInput;
    private readonly BufferEx              _vertexBuffer;
    private readonly BufferEx              _indexBuffer;

    private bool _disposed;

    /// <summary>
    ///     True when the live model matrix can be composed this frame. Billboard
    ///     mode needs only a camera; part mode also needs a controlled vessel and a
    ///     resolvable anchor part. Mirrors <see cref="RecordDraw"/>'s predicate.
    /// </summary>
    public bool IsAnchored => TryComputeModel(out _, out _);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct QuadVertex
    {
        public float3 Pos;
        public float2 Uv;
    }

    public unsafe InWorldQuad(Renderer renderer, OffscreenRenderTarget target, InWorldSettings settings)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(settings);

        _renderer = renderer;
        _target   = target;
        _settings = settings;

        // The unlit mesh shader pair KSA already ships. Get<T> throws if either id
        // is missing, which the manager catches to disable the feature cleanly. The
        // modules are owned by ModLibrary — we must NOT destroy them.
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

        // ---- Write sampler + image-view into the descriptor set ----
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

        // ---- Pipeline layout: one descriptor set layout + mat4 push constant (vertex) ----
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

        // ---- Pipeline state — the load-bearing bits (z-order fix from commit 5be1aad) ----
        // Bind to Program.OffScreenPass, NOT Program.MainPass: the scene (parts +
        // our postfix) runs inside the offscreen MSAA pass; MainPass is the 1-bit
        // swapchain pass. RasterizationSamples must match the scene framebuffer's
        // MSAA. Cull none: a user-orientable single quad should never be invisible
        // from its back face. Two pipelines differ only in depth state:
        //   DepthTestWrite — reverse-Z (clear 0, GreaterOrEqual), occluding (part
        //                    mode, or an occluding billboard).
        //   NoDepthTest    — always-on-top HUD billboard.
        var multisample = new VkPipelineMultisampleStateCreateInfo
        {
            RasterizationSamples = Program.OffScreenPass.SampleCount,
        };

        var pipelineInfo = new VkGraphicsPipelineCreateInfo
        {
            Layout             = _pipelineLayout,
            RenderPass         = Program.OffScreenPass.Pass,
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

        try
        {
            _pipelineDepthWrite = device.CreateGraphicsPipeline(default(VkPipelineCache), pipelineInfo, null);

            pipelineInfo.DepthStencilState = RenderingPresets.ReverseZDepthStencil.NoDepthTest;
            _pipelineNoDepth = device.CreateGraphicsPipeline(default(VkPipelineCache), pipelineInfo, null);

            // ---- Vertex / index buffers ----
            // Centered unit quad in the XY plane, +Z normal. V is flipped so the
            // texture's (0,0) top-left aligns with the ImGui-written content.
            Span<QuadVertex> verts = stackalloc QuadVertex[4]
            {
                new QuadVertex { Pos = new float3(-0.5f, -0.5f, 0f), Uv = new float2(0f, 1f) },
                new QuadVertex { Pos = new float3( 0.5f, -0.5f, 0f), Uv = new float2(1f, 1f) },
                new QuadVertex { Pos = new float3( 0.5f,  0.5f, 0f), Uv = new float2(1f, 0f) },
                new QuadVertex { Pos = new float3(-0.5f,  0.5f, 0f), Uv = new float2(0f, 0f) },
            };
            Span<ushort> indices = stackalloc ushort[6] { 0, 1, 2, 0, 2, 3 };

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

            // Mirror SimpleVkMesh's upload idiom: one staging cmd buffer, both
            // copies, Submit (which fences) + Wait. Synchronous is fine — runs once.
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
            DestroyGpu();
            throw;
        }
    }

    /// <summary>
    ///     Records bind + draw commands for the quad into the supplied (live,
    ///     inside-render-pass) command buffer. No-op when the live model cannot be
    ///     composed (e.g. no camera, or part mode with no anchor part).
    /// </summary>
    public unsafe void RecordDraw(CommandBuffer cmd)
    {
        if (_disposed) return;
        if (!TryComputeModel(out float4x4 model, out bool useNoDepth)) return;

        var camera = Program.GetMainCamera();
        if (camera == null) return;

        // Part mode: model is in ego space → multiply by view*projection. Billboard
        // mode: model is already in view space → skip view, multiply by projection.
        float4x4 mvp = _settings.IsBillboard
            ? model * camera.MVP.projection
            : model * camera.MVP.viewProjection;

        VkPipeline pipeline = useNoDepth ? _pipelineNoDepth : _pipelineDepthWrite;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, pipeline);
        VkDescriptorSet descSetCopy = _descriptorSet;
        cmd.BindDescriptorSets(
            VkPipelineBindPoint.Graphics,
            _pipelineLayout,
            0,
            new ReadOnlySpan<VkDescriptorSet>(ref descSetCopy),
            default(Span<ByteSize32>));

        // The offscreen pass uses a dynamic viewport set by Program.SetViewport.
        Program.SetViewport(cmd);

        cmd.PushConstants(_pipelineLayout, VkShaderStageFlags.VertexBit, ByteSize.Zero, mvp);

        VkBuffer   vb       = _vertexBuffer.VkBuffer;
        ByteSize64 vbOffset = (ByteSize64)_vertexBuffer.BindOffset;
        cmd.BindVertexBuffers(
            0,
            new ReadOnlySpan<VkBuffer>(ref vb),
            new ReadOnlySpan<ByteSize64>(ref vbOffset));
        cmd.BindIndexBuffer(_indexBuffer.VkBuffer, (ByteSize64)_indexBuffer.BindOffset, VkIndexType.Uint16);
        cmd.DrawIndexed(6, 1, 0, 0, 0);
    }

    /// <summary>
    ///     Ego-space ray vs. quad pick (Phase 8 click-to-focus). Valid in part mode
    ///     (ego-space model + ego-space <c>Cursor.InputRay</c>); billboard picking
    ///     uses a screen-space test added in Phase 8. The local corners MUST match
    ///     the vertex buffer.
    /// </summary>
    public bool TryRaycast(Ray ray, out double t)
    {
        t = double.MaxValue;
        if (_settings.IsBillboard) return false;
        if (!TryComputeModel(out float4x4 modelEgo, out _)) return false;

        float3 v0Local = new float3(-0.5f, -0.5f, 0f);
        float3 v1Local = new float3( 0.5f, -0.5f, 0f);
        float3 v2Local = new float3( 0.5f,  0.5f, 0f);
        float3 v3Local = new float3(-0.5f,  0.5f, 0f);

        double3 v0 = ToDouble3(float3.Transform(v0Local, modelEgo));
        double3 v1 = ToDouble3(float3.Transform(v1Local, modelEgo));
        double3 v2 = ToDouble3(float3.Transform(v2Local, modelEgo));
        double3 v3 = ToDouble3(float3.Transform(v3Local, modelEgo));

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

    /// <summary>
    ///     Composes the model matrix for the active anchor mode. Returns false when
    ///     it cannot be composed this frame. <paramref name="useNoDepth"/> selects
    ///     the always-on-top pipeline variant.
    /// </summary>
    private bool TryComputeModel(out float4x4 model, out bool useNoDepth)
    {
        model = float4x4.Identity;
        useNoDepth = false;
        if (_disposed) return false;

        var camera = Program.GetMainCamera();
        if (camera == null) return false;

        if (_settings.IsBillboard)
        {
            return TryComputeBillboardModel(out model, out useNoDepth);
        }

        // Part mode always depth-writes (it lives in the scene and should occlude).
        return TryComputePartModel(camera, out model);
    }

    private bool TryComputePartModel(Camera camera, out float4x4 model)
    {
        model = float4x4.Identity;

        Vehicle? vehicle = Program.ControlledVehicle;
        if (vehicle == null) return false;

        Part? part = ResolvePart(vehicle);
        if (part == null) return false;

        // Pull the part's ego-space rotation + position separately rather than its
        // combined MatrixAsmb2Ego: the combined matrix bakes in the part's own
        // scale, which we exclude so Width/Height are the sole size source.
        double4x4 vehMat    = vehicle.GetMatrixAsmb2Ego(camera);
        double3   partPos   = part.PositionEgo(in vehMat);
        doubleQuat partRotD = part.Asmb2Ego(vehicle.Asmb2Ego);

        float4x4 partRotMat   = float4x4.CreateFromQuaternion(floatQuat.Pack(in partRotD));
        float4x4 partTransMat = float4x4.CreateTranslation(float3.Pack(in partPos));
        // Row-vector convention (mvp = model * viewProjection): rotate first, then
        // translate — places verts at the part's origin in ego space.
        float4x4 partEgo = partRotMat * partTransMat;

        const float deg2rad = MathF.PI / 180f;
        float4x4 userRot = float4x4.CreateRotationX(_settings.PartRotationX * deg2rad)
                         * float4x4.CreateRotationY(_settings.PartRotationY * deg2rad)
                         * float4x4.CreateRotationZ(_settings.PartRotationZ * deg2rad);
        float4x4 userTrans = float4x4.CreateTranslation(
            new float3(_settings.PartOffsetX, _settings.PartOffsetY, _settings.PartOffsetZ));
        float4x4 scaleMat = float4x4.CreateScale(_settings.PartWidthMeters, _settings.PartHeightMeters, 1f);

        // v_local → scale → userRot → userTrans → partEgo → ego
        model = scaleMat * userRot * userTrans * partEgo;
        return true;
    }

    private bool TryComputeBillboardModel(out float4x4 model, out bool useNoDepth)
    {
        // View space: the camera sits at the origin looking down its forward axis.
        // Place the quad `distance` in front (offset in screen X/Y), so mvp =
        // model * projection (skip view) keeps it pinned in the camera's frame.
        //
        // NOTE: the forward-axis sign (-distance) is the single most likely first-
        // run surprise — if the panel renders behind the camera, flip the sign.
        float4x4 scaleMat = float4x4.CreateScale(_settings.BillboardWidthMeters, _settings.BillboardHeightMeters, 1f);
        float4x4 placeMat = float4x4.CreateTranslation(
            new float3(_settings.BillboardOffsetX, _settings.BillboardOffsetY, -_settings.BillboardDistance));

        model = scaleMat * placeMat;
        useNoDepth = _settings.BillboardAlwaysOnTop;
        return true;
    }

    private Part? ResolvePart(Vehicle vehicle)
    {
        if (!string.IsNullOrEmpty(_settings.TargetPartId))
        {
            return FindPart(vehicle, _settings.TargetPartId);
        }

        // Fallback: anchor to the first top-level part so the quad is visible even
        // before a target is picked.
        foreach (Part p in vehicle.Parts.Parts)
        {
            return p;
        }

        return null;
    }

    private static Part? FindPart(Vehicle vehicle, string id)
    {
        foreach (Part p in vehicle.Parts.Parts)
        {
            if (p.Id == id) return p;
            foreach (Part sub in p.SubParts)
            {
                if (sub.Id == id) return sub;
            }
        }

        return null;
    }

    private static double3 ToDouble3(float3 v) => new double3(v.X, v.Y, v.Z);

    private void DestroyGpu()
    {
        var device = _renderer.Device;
        // Reverse construction order. VkShaderModules are owned by ModLibrary — do
        // NOT destroy them here.
        if (_pipelineNoDepth.VkHandle != 0)
        {
            try { device.DestroyPipeline(_pipelineNoDepth, null); } catch { /* best-effort */ }
        }
        if (_pipelineDepthWrite.VkHandle != 0)
        {
            try { device.DestroyPipeline(_pipelineDepthWrite, null); } catch { /* best-effort */ }
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
        try { _vertexInput?.Bindings.Dispose();   } catch { /* best-effort */ }
        try { _vertexInput?.Attributes.Dispose(); } catch { /* best-effort */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyGpu();
    }
}
