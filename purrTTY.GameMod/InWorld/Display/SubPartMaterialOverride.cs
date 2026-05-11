using System;
using Core;
using HarmonyLib;
using KSA;
using purrTTY.Logging;
using RenderCore.Systems;

namespace purrTTY.GameMod.InWorld.Display;

/// <summary>
///     Phase 6B — owns the bindless texture handle that lets a chosen <see cref="Part"/>'s
///     mesh sample our off-screen color image instead of its own diffuse map.
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
///         our texture in <see cref="BindlessTextureLibrary"/> and let the patch in
///         <see cref="purrTTY.GameMod.InWorld.Patches.FramePatches"/> overwrite the
///         just-emitted PerDrawData entry's diffuse + emissive indices in the
///         host-coherent device-vector buffer (which is CPU-mapped, so the rewrite
///         is a normal memory write).
///     </para>
///     <para>
///         Acceptable side effects: the override applies to every instance of the
///         target part's <see cref="PartModel"/> template — if multiple sibling parts
///         share the model, they all sample our texture. The user picks part names
///         and is expected to choose one whose template is unique on the vessel.
///     </para>
/// </summary>
public sealed class SubPartMaterialOverride : IDisposable
{
    private readonly OffscreenRenderTarget _target;
    private readonly BindlessTextureLibrary _bindlessLib;
    private bool _disposed;

    public Part Target { get; }
    public PartModel TargetPartModel { get; }
    public int BindlessHandle { get; }

    public SubPartMaterialOverride(Renderer renderer, OffscreenRenderTarget target, Part targetPart)
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

        // TODO Phase 6B follow-up: provide a "neutral" normal/PBR/emissive override
        // so the lit shader doesn't dim/tint the terminal pixels — currently we only
        // swap the diffuse + emissive bindless indices in PerDrawData, which leaves
        // the part's original normal + PBR maps in effect (acceptable per the plan).
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _bindlessLib.FreeTexture(BindlessHandle); } catch { /* best-effort */ }
    }
}
