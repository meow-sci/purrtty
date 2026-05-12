using System;
using System.Runtime.InteropServices;
using Brutal;
using Core;
using HarmonyLib;
using KSA;
using KSA.Rendering;
using purrTTY.Logging;
using RenderCore.Systems;

namespace purrTTY.GameMod.InWorld.Display;

/// <summary>
///     Per-template SubPart override (the original Phase 6B behavior). Owns the
///     bindless texture handle and rewrites the just-emitted
///     <see cref="PartModel.PerDrawData"/> entry's diffuse + emissive bindless
///     indices to point at our off-screen color image.
///     <para>
///         Architectural note: KSA's part-mesh path does NOT route through
///         <see cref="GpuMaterialSystem"/>. <see cref="PartModel.WriteInstancesToGpu"/>
///         emits a flat <see cref="PartModel.PerDrawData"/> struct each frame whose
///         <c>DiffuseTextureIndex</c>/<c>EmissiveTextureIndex</c> are read directly
///         from <c>Template.Material.*.BindlessHandle</c> and indexed straight into
///         the shared bindless texture array (<see cref="GpuTextureSystem.DescriptorSet"/>).
///         There is no <c>MaterialData</c> indirection in this draw path, so the
///         "register a new MaterialData entry and patch the part's material index"
///         design from the original plan does not apply here. We instead register
///         our texture in <see cref="BindlessTextureLibrary"/> and overwrite the
///         just-emitted PerDrawData entry in the host-coherent device-vector buffer
///         (which is CPU-mapped, so the rewrite is a normal memory write).
///     </para>
///     <para>
///         Acceptable side effects: the override applies to every instance of the
///         target part's <see cref="PartModel"/> template — if multiple sibling parts
///         share the model, they all sample our texture. Pick the
///         <see cref="OverrideMode.PerInstanceOverlay"/> mode if you want only one
///         specific instance to be replaced.
///     </para>
/// </summary>
public sealed class SubPartTemplateOverride : SubPartOverrideBase
{
    private readonly OffscreenRenderTarget _target;
    private readonly BindlessTextureLibrary _bindlessLib;
    private bool _disposed;

    public override OverrideMode Mode => OverrideMode.PerTemplate;

    public SubPartTemplateOverride(Renderer renderer, OffscreenRenderTarget target, Part targetPart)
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

        // GpuTextureSystem.BindTexture(SimpleVkTexture) wraps a SimpleVkTexture, but
        // we want to bind the EXISTING ImageView from OffscreenRenderTarget without
        // allocating a second image. The underlying BindlessTextureLibrary.AddTexture
        // takes a raw VkImageView and is public surface — but the field on
        // GpuTextureSystem holding the library is private, so we go through Harmony's
        // Traverse to fetch it once. (FreeTexture is also public on the library.)
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

        // TODO: provide a "neutral" normal/PBR/emissive override so the lit shader
        // doesn't dim/tint the terminal pixels — currently we only swap the diffuse
        // + emissive bindless indices in PerDrawData, which leaves the part's
        // original normal + PBR maps in effect (acceptable per the plan).
    }

    public override void OnWriteInstancesToGpu(
        PartModel partModel, Viewport viewport, int frameIndex, int deviceInstanceOffsetBefore)
    {
        // Per-template mode ignores deviceInstanceOffsetBefore — we only rewrite
        // the just-appended PerDrawData entry.
        _ = deviceInstanceOffsetBefore;

        // The ViewportData.PerDrawDataVectors[frameIndex] DeviceVector now contains
        // the freshly-appended PerDrawData entry as its LAST element
        // (WriteInstancesToGpu only appends if InstanceList.Count > 0; if the count
        // was zero this hook is a no-op since we matched after Add).
        PartModel.Shared.ViewportData? sharedVp = PartModel.Shared.ViewportData.TryGet(viewport);
        if (sharedVp == null) return;

        DeviceVector vec = sharedVp.PerDrawDataVectors[frameIndex];
        int count = vec.ElementCount;
        if (count <= 0) return;

        // Rebuild the override PerDrawData using the same field layout as the
        // original WriteInstancesToGpu emission, with diffuse + emissive swapped to
        // our bindless handle. Normal + PBR + TFI are preserved so the lit shader
        // still has plausible inputs (the TODO above tracks providing neutral
        // defaults).
        int handle = BindlessHandle;
        var template = partModel.Template;
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
        // ElementSizeInBytes stride starting at MemoryPair.Offset; the new element
        // was added at offset (count-1) * stride.
        ByteSize entryOffset = vec.ElementSizeInBytes * (count - 1);
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref replacement, 1));
        Program.DeviceHostSharedMemory.Write(vec.MemoryPair, entryOffset, bytes);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _bindlessLib.FreeTexture(BindlessHandle); } catch { /* best-effort */ }
    }
}
