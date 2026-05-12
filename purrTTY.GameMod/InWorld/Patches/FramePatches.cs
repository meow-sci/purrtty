using System;
using System.Reflection;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;
using purrTTY.GameMod.InWorld.Display;
using purrTTY.GameMod.InWorld.Input;
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
    public static SubPartOverrideBase? Override;

    /// <summary>Master enable. Patches are no-ops when false.</summary>
    public static bool IsActive;

    /// <summary>Phase 7A focus state. Null = in-world feature is not alive.</summary>
    public static InWorldFocus? Focus;

    /// <summary>
    ///     Phase 7A — the secondary ImGui context the GLFW Harmony patches forward
    ///     input events into. Null when the feature is not alive.
    /// </summary>
    public static OffscreenContext? SecondaryContext;

    // Per-call state stashed by the Prefix patches and consumed by the matching
    // Postfix patches on the same call. Both PartModelModule.UpdateRenderData and
    // PartModel.WriteInstancesToGpu run on the renderer thread; ThreadStatic
    // keeps each thread's stash isolated from any unexpected re-entry.
    [ThreadStatic] internal static int s_instanceListCountBefore;
    [ThreadStatic] internal static int s_deviceInstanceOffsetBefore;
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
    // Thin dispatcher. Per-template mode rewrites the just-appended PerDrawData
    // entry; per-instance overlay mode appends an extra draw + instance + draw
    // command for the chosen part instance. Both implementations live on the
    // SubPartOverrideBase subclass so this patch stays mode-agnostic.
    //
    // The Prefix captures the device PerInstanceDataVectors[frameIndex] count
    // BEFORE the original method appends — overlay mode needs that to locate the
    // just-appended slice (the per-PartModel InstanceList is .Clear()-ed at the
    // end of WriteInstancesToGpu, so reading it in Postfix gives 0).
    static void Prefix(PartModel __instance, Viewport viewport, int frameIndex)
    {
        if (!FramePatches.IsActive) return;
        var ov = FramePatches.Override;
        if (ov == null) return;
        if (!ReferenceEquals(__instance, ov.TargetPartModel)) return;

        PartModel.Shared.ViewportData? sharedVp = PartModel.Shared.ViewportData.TryGet(viewport);
        FramePatches.s_deviceInstanceOffsetBefore = sharedVp != null
            ? (int)sharedVp.PerInstanceDataVectors[frameIndex].ElementCount
            : 0;
    }

    static void Postfix(PartModel __instance, Viewport viewport, int frameIndex)
    {
        if (!FramePatches.IsActive) return;
        var ov = FramePatches.Override;
        if (ov == null) return;
        if (!ReferenceEquals(__instance, ov.TargetPartModel)) return;

        try
        {
            ov.OnWriteInstancesToGpu(__instance, viewport, frameIndex,
                FramePatches.s_deviceInstanceOffsetBefore);
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world subpart override failed; disabling: {ex}");
            FramePatches.IsActive = false;
        }
    }
}

[HarmonyPatch(typeof(PartModelModule), nameof(PartModelModule.UpdateRenderData))]
internal static class PartModelModule_UpdateRenderData_Patch
{
    // Per-instance overlay mode needs to know which slot of the per-PartModel
    // InstanceList holds OUR Part's instance for the current frame. The Prefix
    // captures the count just before the original method calls AddInstance; the
    // Postfix forwards that to the override which compares to the post-call
    // count to detect the Internal/ShadowProxy/non-IVA skip case in
    // PartModel.AddInstance.
    static void Prefix(PartModelModule __instance, Viewport viewport)
    {
        if (!FramePatches.IsActive) return;
        var ov = FramePatches.Override;
        if (ov == null) return;
        if (!ReferenceEquals(__instance.Parent, ov.Target)) return;
        if (!ReferenceEquals(__instance.PartModel, ov.TargetPartModel)) return;

        var perPartModelVp = PartModel.ViewportData.Get(ov.TargetPartModel, viewport);
        FramePatches.s_instanceListCountBefore = perPartModelVp.InstanceList.Count;
    }

    static void Postfix(PartModelModule __instance, Viewport viewport, int frameIndex)
    {
        if (!FramePatches.IsActive) return;
        var ov = FramePatches.Override;
        if (ov == null) return;

        try
        {
            ov.OnUpdateRenderData(__instance, viewport, frameIndex,
                FramePatches.s_instanceListCountBefore);
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world UpdateRenderData hook failed; disabling: {ex}");
            FramePatches.IsActive = false;
        }
    }
}

// Phase 7A — Technique B keyboard routing. The three patches below post-fix
// ImGuiBackendGlfwImpl's GLFW callbacks so that, while the in-world terminal
// owns focus, every key / char / modifier event the main context receives is
// ALSO delivered to the secondary context's IO. The original calls still run
// unchanged, so the screen-space terminal and the rest of the game's ImGui UI
// continue to see the same events; suppression of game-side handling is the
// separate responsibility of Patch01.Prefix1 in Patcher.cs.
//
// TranslateUntranslatedKey (decomp/ksa/KSA/ImGuiBackendGlfwImpl.cs:846) is a
// no-op in this build (returns its input unchanged), so we forward the raw
// GlfwKey parameter without re-translating. If KSA ever stops returning the
// input unchanged, the postfix would need to mirror the translation.

[HarmonyPatch(typeof(ImGuiBackendGlfwImpl), "OnKey")]
internal static class ImGuiBackendGlfwImpl_OnKey_Patch
{
    private static readonly MethodInfo? s_convert = AccessTools.Method(
        typeof(ImGuiBackendGlfwImpl), "Convert", new[] { typeof(GlfwKey) });

    static void Postfix(GlfwKey key, GlfwKeyAction action)
    {
        var focus = FramePatches.Focus;
        if (focus is null || !focus.IsFocused) return;
        var ctx = FramePatches.SecondaryContext;
        if (ctx is null) return;
        if (action != GlfwKeyAction.Press && action != GlfwKeyAction.Release) return;
        if (s_convert is null) return;

        try
        {
            // Reflection cost (~1 boxed allocation + invoke) is fine: key events
            // are infrequent (single-digit per second under typing) and Convert
            // is a private static so there is no public alternative.
            ImGuiKey ik = (ImGuiKey)s_convert.Invoke(null, new object[] { key })!;
            bool down = action == GlfwKeyAction.Press;
            ctx.With(() => ImGui.GetIO().AddKeyEvent(ik, down));
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world key forward failed: {ex}");
        }
    }
}

[HarmonyPatch(typeof(ImGuiBackendGlfwImpl), "OnChar")]
internal static class ImGuiBackendGlfwImpl_OnChar_Patch
{
    static void Postfix(uint codepoint)
    {
        var focus = FramePatches.Focus;
        if (focus is null || !focus.IsFocused) return;
        var ctx = FramePatches.SecondaryContext;
        if (ctx is null) return;

        try
        {
            ctx.With(() => ImGui.GetIO().AddInputCharacter(codepoint));
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world char forward failed: {ex}");
        }
    }
}

[HarmonyPatch(typeof(ImGuiBackendGlfwImpl), "UpdateKeyModifiers")]
internal static class ImGuiBackendGlfwImpl_UpdateKeyModifiers_Patch
{
    // UpdateKeyModifiers is called from OnMouseButton and OnKey. It queries the
    // window's current modifier state and pushes the four Mod_* keys at lines
    // 570-573 of the decomp. Re-issue the same four events into the secondary
    // context whenever focus is on so secondary handlers see modifier chords.
    static void Postfix(GlfwWindow window)
    {
        var focus = FramePatches.Focus;
        if (focus is null || !focus.IsFocused) return;
        var ctx = FramePatches.SecondaryContext;
        if (ctx is null) return;

        try
        {
            bool ctrl  = window.GetKey(GlfwKey.LeftControl) == GlfwKeyAction.Press || window.GetKey(GlfwKey.RightControl) == GlfwKeyAction.Press;
            bool shift = window.GetKey(GlfwKey.LeftShift)   == GlfwKeyAction.Press || window.GetKey(GlfwKey.RightShift)   == GlfwKeyAction.Press;
            bool alt   = window.GetKey(GlfwKey.LeftAlt)     == GlfwKeyAction.Press || window.GetKey(GlfwKey.RightAlt)     == GlfwKeyAction.Press;
            bool sup   = window.GetKey(GlfwKey.LeftSuper)   == GlfwKeyAction.Press || window.GetKey(GlfwKey.RightSuper)   == GlfwKeyAction.Press;
            ctx.With(() =>
            {
                ImGuiIOPtr io = ImGui.GetIO();
                io.AddKeyEvent(ImGuiKey.Mod_Ctrl,  ctrl);
                io.AddKeyEvent(ImGuiKey.Mod_Shift, shift);
                io.AddKeyEvent(ImGuiKey.Mod_Alt,   alt);
                io.AddKeyEvent(ImGuiKey.Mod_Super, sup);
            });
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world modifier forward failed: {ex}");
        }
    }
}
