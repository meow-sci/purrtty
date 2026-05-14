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
///     Owns the GPU resources for a flat textured quad that samples the
///     off-screen color image (the in-world terminal "screen") and gets drawn
///     into the game's main render pass via the <c>SuperMeshRenderSystem.RenderMainPass</c>
///     Harmony postfix in <see cref="purrTTY.GameMod.InWorld.Patches.FramePatches"/>.
///     <para>
///         Reuses KSA's <c>UnlitMesh.{vert,frag}</c> shaders (loaded via
///         <c>ModLibrary.Get&lt;ShaderReference&gt;</c>): vec3 pos + vec2 uv vertex
///         input, single mat4 push constant, and one combined-image-sampler.
///     </para>
///     <para>
///         The quad is anchored to a SubPart on the controlled vessel. Each frame
///         we read the SubPart's current ego-space pose (rotation + position; we
///         deliberately exclude its scale so the user's Width/Height settings are
///         the sole scale source) and compose it with the user's local offset and
///         Euler rotation from <see cref="Settings.InWorldSettings"/>. KSA renders
///         parts in ego space and provides the view-projection in
///         <c>Camera.MVP.viewProjection</c>; we multiply that with our composed
///         ego-space model matrix every frame. KSA's main pass uses reverse-Z, so
///         this pipeline matches
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

    private bool _disposed;

    /// <summary>
    ///     True when the live model matrix can be computed this frame (anchor
    ///     SubPart selected and resolvable on the controlled vessel, and a
    ///     camera is active). Mirrors the predicate used by <see cref="RecordDraw"/>
    ///     so the manager and UI can show an honest "anchored" status without
    ///     duplicating the resolve logic.
    /// </summary>
    public bool IsAnchored => TryComputeModelEgo(out _);

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
        // Cull none: a single quad, both sides visible (the user may orient the
        // quad until they see its back face and we don't want that to render as
        // invisible).
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
        // Quad is laid out in the XY plane with +Z as its normal. UV mapping:
        // local +X → U=0, local +Y → V=1 (top-left of the texture is the quad's
        // upper-left corner when viewed from +Z). V is flipped so the texture's
        // (0,0) top-left aligns with ImGui's top-left origin.
        //
        // UVs are STATIC and sample the full off-screen texture. The "UV
        // offset/size" sliders in the settings window are implemented as a
        // render-rect on the offscreen target (see TerminalMod.SetBuildUi).
        Span<QuadVertex> verts = stackalloc QuadVertex[4]
        {
            new QuadVertex { Pos = new float3(-0.5f, -0.5f, 0f), Uv = new float2(0f, 1f) },
            new QuadVertex { Pos = new float3( 0.5f, -0.5f, 0f), Uv = new float2(1f, 1f) },
            new QuadVertex { Pos = new float3( 0.5f,  0.5f, 0f), Uv = new float2(1f, 0f) },
            new QuadVertex { Pos = new float3(-0.5f,  0.5f, 0f), Uv = new float2(0f, 0f) },
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
    ///     Compose the current ego-space model matrix from:
    ///       (1) the user's Width/Height (scale),
    ///       (2) the user's local Euler rotation about the quad's own axes,
    ///       (3) the user's local position offset (in the SubPart's frame),
    ///       (4) the anchor SubPart's ego-space rotation + position.
    ///     Returns false when any of {camera, controlled vehicle, anchor part}
    ///     is missing — caller should skip the draw / pick in that case.
    /// </summary>
    private bool TryComputeModelEgo(out float4x4 model)
    {
        model = float4x4.Identity;
        if (_disposed) return false;

        string targetName = _settings.TargetPartName;
        if (string.IsNullOrEmpty(targetName)) return false;

        var camera = Program.GetMainCamera();
        if (camera == null) return false;

        Vehicle? vehicle = Program.ControlledVehicle;
        if (vehicle == null) return false;

        Part? part = FindPart(vehicle, targetName);
        if (part == null) return false;

        // Pull the SubPart's ego-space rotation + position separately rather
        // than using its combined MatrixAsmb2Ego: that combined matrix bakes in
        // the part's own scale, which we deliberately want to exclude so the
        // user's Width/Height sliders are the sole source of quad size.
        double4x4 vehMat    = vehicle.GetMatrixAsmb2Ego(camera);
        double3   partPos   = part.PositionEgo(in vehMat);
        doubleQuat partRotD = part.Asmb2Ego(vehicle.Asmb2Ego);

        float4x4 partRotMat   = float4x4.CreateFromQuaternion(floatQuat.Pack(in partRotD));
        float4x4 partTransMat = float4x4.CreateTranslation(float3.Pack(in partPos));
        // Row-vector convention (matches existing code: `mvp = model * viewProjection`):
        // left-to-right multiplication is the ordering of transforms applied to v.
        // Rotate first, then translate — that places verts at the part's origin
        // in ego space.
        float4x4 partEgo = partRotMat * partTransMat;

        // User-controlled pose in subpart-local frame.
        const float deg2rad = MathF.PI / 180f;
        float4x4 userRotX = float4x4.CreateRotationX(_settings.AnchorRotationX * deg2rad);
        float4x4 userRotY = float4x4.CreateRotationY(_settings.AnchorRotationY * deg2rad);
        float4x4 userRotZ = float4x4.CreateRotationZ(_settings.AnchorRotationZ * deg2rad);
        float4x4 userRot  = userRotX * userRotY * userRotZ;
        float4x4 userTrans = float4x4.CreateTranslation(new float3(
            _settings.AnchorOffsetX, _settings.AnchorOffsetY, _settings.AnchorOffsetZ));

        float4x4 scaleMat = float4x4.CreateScale(
            _settings.QuadWidthMeters, _settings.QuadHeightMeters, 1f);

        // Full chain (row-vector / left-to-right):
        //   v_local → scale → userRot → userTrans → partEgo → world (ego)
        model = scaleMat * userRot * userTrans * partEgo;
        return true;
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


    /// <summary>
    ///     Ego-space ray vs. quad pick. Returns true and the nearer positive
    ///     <c>t</c> when the ray hits one of the two triangles, false
    ///     otherwise. No-op when the live model cannot be composed.
    ///     <para>
    ///         The local-space verts/indices here MUST match the buffers uploaded
    ///         in the constructor: 4 corners in the XY plane, two triangles
    ///         (0,1,2) and (0,2,3) — keep them in sync if either ever changes.
    ///     </para>
    /// </summary>
    public bool TryRaycast(Ray ray, out double t)
    {
        t = double.MaxValue;
        if (!TryComputeModelEgo(out float4x4 modelEgo)) return false;

        // Local-space corners mirror the QuadVertex array in the ctor. We only
        // need positions for the raycast; UVs are irrelevant for hit-vs-no-hit.
        float3 v0Local = new float3(-0.5f, -0.5f, 0f);
        float3 v1Local = new float3( 0.5f, -0.5f, 0f);
        float3 v2Local = new float3( 0.5f,  0.5f, 0f);
        float3 v3Local = new float3(-0.5f,  0.5f, 0f);

        // float3.Transform(p, M) computes M * vec4(p,1) using KSA's mat-vector
        // convention (same one RecordDraw relies on when it composes the MVP).
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

    private static double3 ToDouble3(float3 v) => new double3(v.X, v.Y, v.Z);

    /// <summary>
    ///     Record bind + draw commands for the quad into the supplied (live,
    ///     inside-render-pass) command buffer. No-op when the live model cannot
    ///     be composed (no camera / no vessel / SubPart not on the vessel).
    /// </summary>
    public unsafe void RecordDraw(CommandBuffer cmd)
    {
        if (_disposed) return;
        if (!TryComputeModelEgo(out float4x4 modelEgo)) return;

        var camera = Program.GetMainCamera();
        if (camera == null) return;

        // MVP = model * viewProjection (row-vector convention). Camera.MVP is a
        // ViewProjection struct with a precomputed view*projection matrix.
        float4x4 mvp = modelEgo * camera.MVP.viewProjection;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        VkDescriptorSet descSetCopy = _descriptorSet;
        cmd.BindDescriptorSets(
            VkPipelineBindPoint.Graphics,
            _pipelineLayout,
            0,
            new ReadOnlySpan<VkDescriptorSet>(ref descSetCopy),
            default(Span<ByteSize32>));

        // Match the rest of KSA's main-pass draws (which all rely on a dynamic
        // viewport set by Program.SetViewport).
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
