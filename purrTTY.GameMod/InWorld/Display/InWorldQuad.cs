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
///     The per-instance half of the in-world terminal quad: the descriptor set that
///     binds <b>this</b> instance's off-screen color image + sampler, plus the live
///     model/MVP math. It draws through a <see cref="SharedQuadResource"/> (the two
///     pipelines, pipeline layout, descriptor-set layout, vertex input, and unit-quad
///     geometry — all identical across instances), so N quads cost N descriptor sets
///     rather than N× the full pipeline/geometry set.
///     <para>
///         Two anchor modes share the shared pipeline pair, selected per-frame from
///         <see cref="InWorldTerminalRecord"/>:
///         <list type="bullet">
///             <item><b>Part</b> — ego-space MVP anchored to a Part/SubPart pose
///                   (rotation + position, excluding the part's own scale), drawn with
///                   reverse-Z <c>DepthTestWrite</c> so it occludes / is occluded.</item>
///             <item><b>Billboard</b> — a view-space MVP (camera-locked HUD panel),
///                   <c>NoDepthTest</c> (always-on-top) or <c>DepthTestWrite</c> per
///                   <see cref="InWorldTerminalRecord.BillboardAlwaysOnTop"/>.</item>
///         </list>
///     </para>
///     <para>
///         Main thread only. The constructor allocates the descriptor pool/set after
///         the renderer is live. It reads the record live each frame, so editing the
///         placement updates the quad instantly.
///     </para>
/// </summary>
public sealed class InWorldQuad : IDisposable
{
    private readonly Renderer              _renderer;
    private readonly OffscreenRenderTarget _target;
    private readonly InWorldTerminalRecord _settings;
    private readonly SharedQuadResource    _shared;

    private readonly DescriptorPoolEx _descriptorPool;
    private readonly VkDescriptorSet  _descriptorSet;

    private bool _disposed;

    // Part-follow tracking: once a specifically-targeted part resolves, hold the Part
    // object and follow it by identity so the anchor survives a vessel decouple/dock —
    // KSA moves the same Part instance into the new vehicle. Session-only (the object is
    // per-run; a fresh run re-resolves from the persisted vehicle/part id), and invalidated
    // when the target ids change (live edit in the Configure panel). See gotcha 31.
    private Part? _anchoredPart;
    private string? _anchoredForVehicleId;
    private string? _anchoredForPartId;
    private string? _anchoredForSubPartId;

    /// <summary>
    ///     True when the live model matrix can be composed this frame. Billboard mode
    ///     needs only a camera; part mode also needs a controlled vessel and a
    ///     resolvable anchor part. Mirrors <see cref="RecordDraw"/>'s predicate.
    /// </summary>
    public bool IsAnchored => TryComputeModel(out _, out _);

    public unsafe InWorldQuad(
        Renderer renderer, OffscreenRenderTarget target, InWorldTerminalRecord settings, SharedQuadResource shared)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(shared);

        _renderer = renderer;
        _target   = target;
        _settings = settings;
        _shared   = shared;

        var device = renderer.Device;

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

        // Allocate this instance's set from the shared layout the shared pipeline
        // layout was built against.
        _descriptorSet = device.AllocateDescriptorSet(_descriptorPool, _shared.DescriptorSetLayout);

        // ---- Write this instance's sampler + image-view into the set ----
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

        VkPipeline pipeline = useNoDepth ? _shared.PipelineNoDepth : _shared.PipelineDepthTest;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, pipeline);
        VkDescriptorSet descSetCopy = _descriptorSet;
        cmd.BindDescriptorSets(
            VkPipelineBindPoint.Graphics,
            _shared.PipelineLayout,
            0,
            new ReadOnlySpan<VkDescriptorSet>(ref descSetCopy),
            default(Span<ByteSize32>));

        // The offscreen pass uses a dynamic viewport set by Program.SetViewport.
        Program.SetViewport(cmd);

        cmd.PushConstants(_shared.PipelineLayout, VkShaderStageFlags.VertexBit, ByteSize.Zero, mvp);

        VkBuffer   vb       = _shared.VertexBuffer.VkBuffer;
        ByteSize64 vbOffset = (ByteSize64)_shared.VertexBuffer.BindOffset;
        cmd.BindVertexBuffers(
            0,
            new ReadOnlySpan<VkBuffer>(ref vb),
            new ReadOnlySpan<ByteSize64>(ref vbOffset));
        cmd.BindIndexBuffer(_shared.IndexBuffer.VkBuffer, (ByteSize64)_shared.IndexBuffer.BindOffset, VkIndexType.Uint16);
        cmd.DrawIndexed(6, 1, 0, 0, 0);
    }

    /// <summary>
    ///     Ego-space ray vs. quad pick (click-to-focus). Valid in part mode (ego-space
    ///     model + ego-space <c>Cursor.InputRay</c>); billboard picking is menu-driven.
    ///     The local corners MUST match the shared vertex buffer.
    /// </summary>
    public bool TryRaycast(Ray ray, out double t, out float2 uv)
    {
        t = double.MaxValue;
        uv = default;
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

        if (!any)
        {
            return false;
        }

        // Texture UV of the hit: project the hit point onto the quad's two edge
        // vectors. v0 = local(-0.5,-0.5) with uv(0,1); the +X edge (v0→v1) runs
        // u 0→1, the +Y edge (v0→v3) runs v 1→0 (V flipped to match the texture).
        double3 hit = new double3(
            ray.Origin.X + t * ray.Direction.X,
            ray.Origin.Y + t * ray.Direction.Y,
            ray.Origin.Z + t * ray.Direction.Z);
        double3 ex = v1 - v0;
        double3 ey = v3 - v0;
        double3 d = hit - v0;
        double s = double3.Dot(d, ex) / double3.Dot(ex, ex);
        double w = double3.Dot(d, ey) / double3.Dot(ey, ey);
        uv = new float2(
            (float)Math.Clamp(s, 0.0, 1.0),
            (float)Math.Clamp(1.0 - w, 0.0, 1.0));
        return true;
    }

    /// <summary>
    ///     Composes the model matrix for the active anchor mode. Returns false when it
    ///     cannot be composed this frame. <paramref name="useNoDepth"/> selects the
    ///     always-on-top pipeline variant.
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

        // A live edit of the anchor (Configure panel) must take effect: drop a stale tracked
        // part when any target id changed since it was cached.
        if (_anchoredPart != null && (
                !string.Equals(_anchoredForVehicleId, _settings.TargetVehicleId, StringComparison.Ordinal) ||
                !string.Equals(_anchoredForPartId, _settings.TargetPartId, StringComparison.Ordinal) ||
                !string.Equals(_anchoredForSubPartId, _settings.TargetSubPartId, StringComparison.Ordinal)))
        {
            _anchoredPart = null;
        }

        // Follow a specifically-targeted part by object identity so the anchor tracks it
        // when its vessel decouples/docks (KSA moves the same Part into the new vehicle).
        // The current owning vehicle (found by the search) drives the transform below, so a
        // decoupled part is placed in its new vessel's frame automatically.
        if (_anchoredPart != null && !string.IsNullOrEmpty(_settings.TargetPartId))
        {
            Vehicle? owner = VehicleLookup.FindContaining(_anchoredPart);
            if (owner != null)
            {
                return ComputePartTransform(camera, owner, _anchoredPart, out model);
            }

            // The tracked part is gone from every vehicle (destroyed) — drop it, re-resolve.
            _anchoredPart = null;
        }

        Vehicle? vehicle = VehicleLookup.Resolve(_settings.TargetVehicleId);
        if (vehicle == null) return false;

        Part? part = ResolvePart(vehicle);
        if (part == null) return false;

        // Cache for follow-tracking only when a specific part was targeted; the empty-target
        // "first part" default keeps re-resolving so it stays with the controlled/target
        // vehicle rather than chasing a decoupled chunk.
        if (!string.IsNullOrEmpty(_settings.TargetPartId))
        {
            _anchoredPart = part;
            _anchoredForVehicleId = _settings.TargetVehicleId;
            _anchoredForPartId = _settings.TargetPartId;
            _anchoredForSubPartId = _settings.TargetSubPartId;
        }

        return ComputePartTransform(camera, vehicle, part, out model);
    }

    private bool ComputePartTransform(Camera camera, Vehicle vehicle, Part part, out float4x4 model)
    {
        // Pull the part's ego-space rotation + position separately rather than its
        // combined MatrixAsmb2Ego: the combined matrix bakes in the part's own scale,
        // which we exclude so Width/Height are the sole size source. The transform flows
        // through whichever vehicle currently owns the part.
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
        // NOTE: the forward-axis sign (-distance) is the single most likely first-run
        // surprise — if the panel renders behind the camera, flip the sign.
        float4x4 scaleMat = float4x4.CreateScale(_settings.BillboardWidthMeters, _settings.BillboardHeightMeters, 1f);
        float4x4 placeMat = float4x4.CreateTranslation(
            new float3(_settings.BillboardOffsetX, _settings.BillboardOffsetY, -_settings.BillboardDistance));

        model = scaleMat * placeMat;
        useNoDepth = _settings.BillboardAlwaysOnTop;
        return true;
    }

    private Part? ResolvePart(Vehicle vehicle)
    {
        Part? topLevel = ResolveTopLevelPart(vehicle);
        if (topLevel == null)
        {
            return null;
        }

        // A selected sub-part anchors to it instead of the whole part.
        if (!string.IsNullOrEmpty(_settings.TargetSubPartId))
        {
            foreach (Part sub in topLevel.SubParts)
            {
                if (sub.Id == _settings.TargetSubPartId)
                {
                    return sub;
                }
            }
        }

        return topLevel;
    }

    private Part? ResolveTopLevelPart(Vehicle vehicle)
    {
        if (!string.IsNullOrEmpty(_settings.TargetPartId))
        {
            foreach (Part p in vehicle.Parts.Parts)
            {
                if (p.Id == _settings.TargetPartId)
                {
                    return p;
                }
            }
        }

        // Fallback: the first top-level part so the quad is visible before a pick.
        foreach (Part p in vehicle.Parts.Parts)
        {
            return p;
        }

        return null;
    }

    private static double3 ToDouble3(float3 v) => new double3(v.X, v.Y, v.Z);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Only the per-instance descriptor pool (which frees its set). The shared
        // pipeline/layout/geometry belong to SharedQuadResource (coordinator-owned).
        if (_descriptorPool != null)
        {
            try { _renderer.Device.DestroyDescriptorPool(_descriptorPool, null); } catch { /* best-effort */ }
        }
    }
}
