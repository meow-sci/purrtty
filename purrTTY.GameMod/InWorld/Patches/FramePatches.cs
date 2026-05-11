using System;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;
using KSA.Rendering;
using purrTTY.GameMod.InWorld.Display;
using purrTTY.Logging;

namespace purrTTY.GameMod.InWorld.Patches;

/// <summary>
///     Static glue between <see cref="InWorld.InWorldTerminalManager"/> and the
///     Harmony postfixes on <c>SuperMeshRenderSystem.RenderMainPass</c> and
///     <c>PartModel.WriteInstancesToGpu</c>.
///     <para>
///         The patches are attribute-discovered by <c>Patcher.patch()</c>'s
///         <c>PatchAll(typeof(Patcher).Assembly)</c>, so no manual registration
///         is required.
///     </para>
///     <para>
///         Both fields are touched on the main thread only (the manager updates
///         them in <see cref="InWorld.InWorldTerminalManager.Toggle"/>, the patch
///         reads them on the same thread inside KSA's render loop).
///     </para>
/// </summary>
internal static class FramePatches
{
    /// <summary>The quad to draw. Set by the manager when the feature is alive.</summary>
    public static QuadDisplay? Display;

    /// <summary>Optional Phase 6B SubPart texture override. Null = quad-only.</summary>
    public static SubPartMaterialOverride? Override;

    /// <summary>Master enable. Patches are no-ops when false.</summary>
    public static bool IsActive;
}

[HarmonyPatch(typeof(SuperMeshRenderSystem), nameof(SuperMeshRenderSystem.RenderMainPass))]
internal static class SuperMeshRenderSystem_RenderMainPass_Patch
{
    // We piggy-back on the main pass: SuperMeshRenderSystem.RenderMainPass runs
    // inside an already-begun render pass on the supplied command buffer. A
    // postfix appends our draw to the same command buffer after KSA's opaque
    // mesh draws, before the caller (Program.RenderGame) calls EndRenderPass.
    static void Postfix(CommandBuffer commandBuffer)
    {
        if (!FramePatches.IsActive) return;
        var display = FramePatches.Display;
        if (display == null) return;

        try
        {
            display.RecordDraw(commandBuffer);
        }
        catch (Exception ex)
        {
            // Don't crash the game's render loop on a transient draw failure.
            // Disable ourselves so the user can re-toggle after fixing whatever
            // is wrong; the manager observes IsActive when toggling back on.
            ModLog.Log.Error($"purrTTY in-world quad draw failed: {ex}");
            FramePatches.IsActive = false;
        }
    }
}

[HarmonyPatch(typeof(PartModel), nameof(PartModel.WriteInstancesToGpu))]
internal static class PartModel_WriteInstancesToGpu_Patch
{
    // PartModel.WriteInstancesToGpu (decomp/ksa/KSA/PartModel.cs:393-425) appends
    // exactly ONE PerDrawData entry per call (the per-template draw constants —
    // diffuse/normal/PBR/emissive bindless indices) and N PerInstanceData entries
    // for the part instances. The PerDrawData is appended to a host-coherent
    // CPU-mapped buffer (DeviceVector → DeviceHostSharedMemory.Write), so a postfix
    // can rewrite the just-written entry in-place before the GPU consumes it.
    //
    // Why not patch PartModelModule.UpdateRenderData instead: that method's data
    // never reaches PerDrawData — it only feeds PerInstanceData. The PerDrawData
    // values come from Template.Material.*.BindlessHandle at WriteInstancesToGpu
    // time, so the rewrite must happen at this site.
    static void Postfix(PartModel __instance, Viewport viewport, int frameIndex)
    {
        if (!FramePatches.IsActive) return;
        var ov = FramePatches.Override;
        if (ov == null) return;
        if (!ReferenceEquals(__instance, ov.TargetPartModel)) return;

        try
        {
            // The ViewportData.PerDrawDataVectors[frameIndex] DeviceVector now
            // contains the freshly-appended PerDrawData entry as its LAST element
            // (WriteInstancesToGpu only appends if InstanceList.Count > 0; if the
            // count was zero this postfix is a no-op since we matched after Add).
            PartModel.Shared.ViewportData? sharedVp = PartModel.Shared.ViewportData.TryGet(viewport);
            if (sharedVp == null) return;

            DeviceVector vec = sharedVp.PerDrawDataVectors[frameIndex];
            int count = vec.ElementCount;
            if (count <= 0) return;

            // Construct an override PerDrawData using the same field layout as the
            // original WriteInstancesToGpu emission, with diffuse + emissive swapped
            // to our bindless handle. Normal + PBR + TFI are preserved so the lit
            // shader still has plausible inputs (a TODO in SubPartMaterialOverride
            // tracks providing neutral defaults).
            int handle = ov.BindlessHandle;
            var template = __instance.Template;
            int normalIdx = template.Material?.NormalReference?.BindlessHandle   ?? -1;
            int pbrIdx    = template.Material?.PBRMap?.BindlessHandle            ?? -1;
            int tfiIdx    = template.Material?.ThinFilmMap?.BindlessHandle       ?? -1;

            PartModel.PerDrawData replacement = new PartModel.PerDrawData
            {
                DiffuseTextureIndex  = handle,
                NormalTextureIndex   = normalIdx,
                PbrTextureIndex      = pbrIdx,
                EmissiveTextureIndex = handle,
                TfiTextureIndex      = tfiIdx,
            };

            // Overwrite the last element. DeviceVector lays elements end-to-end at
            // ElementSizeInBytes stride starting at MemoryPair.Offset; the new
            // element was added at offset (count-1) * stride.
            ByteSize entryOffset = vec.ElementSizeInBytes * (count - 1);
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(
                MemoryMarshal.CreateReadOnlySpan(ref replacement, 1));
            Program.DeviceHostSharedMemory.Write(vec.MemoryPair, entryOffset, bytes);
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world subpart override failed; disabling: {ex}");
            FramePatches.IsActive = false;
        }
    }
}
