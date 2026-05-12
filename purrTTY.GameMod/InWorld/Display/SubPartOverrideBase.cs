using System;
using KSA;

namespace purrTTY.GameMod.InWorld.Display;

/// <summary>
///     Two ways to redirect a chosen <see cref="Part"/>'s mesh to sample our
///     off-screen color image. The mode is user-selectable from the in-world
///     settings window — see <see cref="InWorld.Settings.InWorldSettings.TargetOverrideMode"/>.
/// </summary>
public enum OverrideMode
{
    /// <summary>
    ///     Rewrite the per-template <see cref="PartModel.PerDrawData"/> emitted by
    ///     <see cref="PartModel.WriteInstancesToGpu"/> so its diffuse + emissive
    ///     bindless indices point at our texture. Affects every instance of the
    ///     template on the vessel — there is exactly one PerDrawData entry per
    ///     PartModel and PartModel is shared across instances of the same template.
    /// </summary>
    PerTemplate,

    /// <summary>
    ///     Leave the original per-template draw alone and append a SECOND draw
    ///     covering only the chosen Part instance with our texture. Reverse-Z +
    ///     <c>GreaterOrEqual</c> in <see cref="RenderingPresets.ReverseZDepthStencil.DepthTestWrite"/>
    ///     means our overlay (drawn last at the same depth) wins, so other
    ///     instances of the same template keep their original material.
    /// </summary>
    PerInstanceOverlay,
}

/// <summary>
///     Base class for the two SubPart override strategies. Owns the bindless
///     texture handle that wraps the off-screen color image view, plus the
///     virtual hooks the <see cref="purrTTY.GameMod.InWorld.Patches.FramePatches"/>
///     dispatcher invokes inside KSA's render loop.
///     <para>
///         The patch dispatcher only forwards to <see cref="OnWriteInstancesToGpu"/>
///         when the patched <see cref="PartModel"/> matches <see cref="TargetPartModel"/>;
///         <see cref="OnUpdateRenderData"/> sees every module each frame and is
///         responsible for filtering itself.
///     </para>
/// </summary>
public abstract class SubPartOverrideBase : IDisposable
{
    public Part Target { get; protected set; } = null!;
    public PartModel TargetPartModel { get; protected set; } = null!;
    public int BindlessHandle { get; protected set; }

    public abstract OverrideMode Mode { get; }

    /// <summary>
    ///     Postfix hook on <see cref="PartModelModule.UpdateRenderData"/>. Used by
    ///     <see cref="SubPartOverlayOverride"/> to stash the just-appended instance
    ///     index for the current frame; the per-template path has nothing to do
    ///     here. <paramref name="instanceListCountBefore"/> is the per-PartModel
    ///     <see cref="PartModel.ViewportData.InstanceList"/> count captured by the
    ///     Prefix immediately before the original method ran — comparing it to the
    ///     current count tells us whether our part actually got added (Internal /
    ///     ShadowProxy / non-IVA paths can skip the Add inside
    ///     <see cref="PartModel.AddInstance"/>).
    /// </summary>
    public virtual void OnUpdateRenderData(
        PartModelModule module, Viewport viewport, int frameIndex, int instanceListCountBefore) { }

    /// <summary>
    ///     Postfix hook on <see cref="PartModel.WriteInstancesToGpu"/>. Only
    ///     invoked when <c>partModel == this.TargetPartModel</c> (the dispatcher
    ///     filters). The original method has already appended its draw command,
    ///     PerDrawData, and PerInstanceData; implementations either rewrite or
    ///     append more. <paramref name="deviceInstanceOffsetBefore"/> is the
    ///     per-viewport device <c>PerInstanceDataVectors[frameIndex].ElementCount</c>
    ///     captured by the Prefix immediately before the original method ran —
    ///     overlay mode needs it to locate the just-appended slice.
    /// </summary>
    public abstract void OnWriteInstancesToGpu(
        PartModel partModel, Viewport viewport, int frameIndex, int deviceInstanceOffsetBefore);

    public abstract void Dispose();
}
