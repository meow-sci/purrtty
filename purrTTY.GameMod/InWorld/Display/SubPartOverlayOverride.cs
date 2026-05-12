using System;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.VulkanApi;
using Core;
using HarmonyLib;
using KSA;
using KSA.Rendering;
using purrTTY.Logging;
using RenderCore.Systems;

namespace purrTTY.GameMod.InWorld.Display;

/// <summary>
///     Per-instance overlay SubPart override. Leaves the original per-template
///     draw alone and appends a SECOND draw covering ONLY the chosen
///     <see cref="Part"/> instance with our texture.
///     <para>
///         <see cref="OnUpdateRenderData"/> records the index our part instance
///         was just appended at inside the per-PartModel
///         <see cref="PartModel.ViewportData.InstanceList"/>.
///         <see cref="OnWriteInstancesToGpu"/> uses the device-vector
///         <c>PerInstanceDataVectors[frameIndex]</c> offset captured by the
///         dispatcher's Prefix to locate the original slice, copies the chosen
///         instance's <see cref="PartModel.PerInstanceData"/> back to the end of
///         the device vector, appends an override <see cref="PartModel.PerDrawData"/>
///         pointing at our texture, and finally pushes a new
///         <see cref="VkDrawIndexedIndirectCommand"/> referencing both.
///     </para>
///     <para>
///         Depth interaction: KSA's PartModel pipeline uses
///         <see cref="RenderingPresets.ReverseZDepthStencil.DepthTestWrite"/>
///         (DepthCompareOp = GreaterOrEqual, DepthWriteEnable = true). Our overlay
///         is drawn in the same indirect dispatch AFTER the original draw, so the
///         equal-depth test passes and our color wins. No Z-fight under typical
///         conditions.
///     </para>
/// </summary>
public sealed class SubPartOverlayOverride : SubPartOverrideBase
{
    private readonly OffscreenRenderTarget _target;
    private readonly BindlessTextureLibrary _bindlessLib;
    private bool _disposed;

    // Index of our part instance in the per-PartModel InstanceList for the most
    // recent UpdateRenderData call. -1 means "our part wasn't appended this
    // frame in this viewport" — Internal/ShadowProxy/non-IVA paths inside
    // PartModel.AddInstance can skip the Add. Reset to -1 after every consume so
    // the overlay does not double-fire for a stale frame.
    [ThreadStatic] private static int s_lastInstanceIndex;

    static SubPartOverlayOverride()
    {
        s_lastInstanceIndex = -1;
    }

    public override OverrideMode Mode => OverrideMode.PerInstanceOverlay;

    public SubPartOverlayOverride(Renderer renderer, OffscreenRenderTarget target, Part targetPart)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (targetPart == null) throw new ArgumentNullException(nameof(targetPart));

        _target = target;
        Target = targetPart;

        Span<PartModelModule> modelModules = targetPart.Modules.Get<PartModelModule>();
        if (modelModules.Length == 0)
        {
            throw new InvalidOperationException(
                $"Part '{targetPart.Id}' has no PartModelModule; cannot override its texture.");
        }
        if (modelModules.Length > 1)
        {
            ModLog.Log.Debug(
                $"purrTTY in-world: part '{targetPart.Id}' has {modelModules.Length} PartModelModules; "
                + "using the first.");
        }
        TargetPartModel = modelModules[0].PartModel
            ?? throw new InvalidOperationException(
                $"Part '{targetPart.Id}' PartModelModule.PartModel is null.");

        GpuTextureSystem texSys = Program.Instance.TextureSystem;
        BindlessTextureLibrary? lib = Traverse.Create(texSys)
            .Field("_bindlessTextureLib")
            .GetValue<BindlessTextureLibrary>();
        if (lib == null)
        {
            throw new InvalidOperationException(
                "purrTTY in-world: failed to access GpuTextureSystem._bindlessTextureLib via Traverse.");
        }
        _bindlessLib = lib;

        BindlessHandle = _bindlessLib.AddTexture(_target.ColorImageView);
    }

    public override void OnUpdateRenderData(
        PartModelModule module, Viewport viewport, int frameIndex, int instanceListCountBefore)
    {
        // Filter to our exact target Part. The dispatcher only checked we exist;
        // the patch fires for every PartModelModule on the vessel.
        if (!ReferenceEquals(module.Parent, Target)) return;
        if (!ReferenceEquals(module.PartModel, TargetPartModel)) return;

        var perPartModelVp = PartModel.ViewportData.Get(TargetPartModel, viewport);
        int after = perPartModelVp.InstanceList.Count;
        if (after != instanceListCountBefore + 1)
        {
            // PartModel.AddInstance skipped the Add (Internal in non-IVA, or
            // ShadowProxy raytracing). Leave _lastInstanceIndex alone — if the
            // previous frame index is still set it will be cleared in
            // OnWriteInstancesToGpu either way.
            return;
        }

        s_lastInstanceIndex = instanceListCountBefore;
    }

    public override unsafe void OnWriteInstancesToGpu(
        PartModel partModel, Viewport viewport, int frameIndex, int deviceInstanceOffsetBefore)
    {
        int idx = s_lastInstanceIndex;
        s_lastInstanceIndex = -1;
        if (idx < 0) return;

        PartModel.Shared.ViewportData? sharedVp = PartModel.Shared.ViewportData.TryGet(viewport);
        if (sharedVp == null) return;

        DeviceVector instanceVec  = sharedVp.PerInstanceDataVectors[frameIndex];
        DeviceVector perDrawVec   = sharedVp.PerDrawDataVectors[frameIndex];
        DeviceVector drawCmdVec   = sharedVp.DrawCommandVectors[frameIndex];

        // The original WriteInstancesToGpu just appended N instances starting at
        // device-vector slot deviceInstanceOffsetBefore. Our part's instance went
        // in at relative-list-index `idx`, so its device slot is offsetBefore+idx.
        int sourceInstanceSlot = deviceInstanceOffsetBefore + idx;
        if (sourceInstanceSlot >= instanceVec.ElementCount) return;

        // Mirror PartModel.WriteInstancesToGpu's append idiom exactly: stack-alloc
        // the value, take a Span, AsBytes, then DeviceVector.Add. Read-back uses
        // DeviceHostSharedMemory.Read against the same MemoryPair.
        ByteSize stride = instanceVec.ElementSizeInBytes;
        Span<byte> instanceBytes = stackalloc byte[(int)stride];
        if (!Program.DeviceHostSharedMemory.Read(instanceVec.MemoryPair, stride * sourceInstanceSlot, instanceBytes))
        {
            return;
        }

        // Capture the new instance's device-vector slot BEFORE Add — Add bumps
        // ElementCount by exactly one entry, so this is also FirstInstance for
        // the indirect command we emit below.
        int newInstanceSlot = instanceVec.ElementCount;
        instanceVec.Add(instanceBytes);

        int handle = BindlessHandle;
        var template = partModel.Template;
        int normalIdx = template.Material?.NormalReference?.BindlessHandle   ?? -1;
        int pbrIdx    = template.Material?.PBRMap?.BindlessHandle            ?? -1;
        int tfiIdx    = template.Material?.ThinFilmMap?.BindlessHandle       ?? -1;

        // The overlay PerDrawData mirrors the original layout with diffuse +
        // emissive swapped to our bindless handle. Normal/PBR/TFI carry the
        // template's own values so the lit shader still has plausible inputs.
        Span<PartModel.PerDrawData> drawData = stackalloc PartModel.PerDrawData[1]
        {
            new PartModel.PerDrawData
            {
                DiffuseTextureIndex  = handle,
                NormalTextureIndex   = normalIdx,
                PbrTextureIndex      = pbrIdx,
                EmissiveTextureIndex = handle,
                TfiTextureIndex      = tfiIdx,
            }
        };
        ReadOnlySpan<byte> drawDataBytes = MemoryMarshal.AsBytes(drawData);
        perDrawVec.Add(drawDataBytes);

        // Mesh draw geometry mirrors the original — same index range, same vertex
        // offset; only the instance count drops to 1 and FirstInstance points at
        // our newly-appended slot.
        DeviceMeshInterleaved deviceMesh = template.Mesh!.DeviceMeshInterleaved;
        Span<VkDrawIndexedIndirectCommand> cmd = stackalloc VkDrawIndexedIndirectCommand[1]
        {
            new VkDrawIndexedIndirectCommand
            {
                IndexCount    = deviceMesh.IndexCount,
                InstanceCount = 1,
                FirstIndex    = (int)(deviceMesh.IndicesOffset / deviceMesh.IndicesStride),
                VertexOffset  = (int)(deviceMesh.VerticesOffset / deviceMesh.VertexStride),
                FirstInstance = newInstanceSlot,
            }
        };
        ReadOnlySpan<byte> cmdBytes = MemoryMarshal.AsBytes(cmd);
        drawCmdVec.Add(cmdBytes);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _bindlessLib.FreeTexture(BindlessHandle); } catch { /* best-effort */ }
    }
}
