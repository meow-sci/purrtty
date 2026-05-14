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
///     Harmony postfixes on <c>SuperMeshRenderSystem.RenderMainPass</c> and the
///     GLFW input callbacks.
///     <para>
///         The patches are attribute-discovered by <c>Patcher.patch()</c>'s
///         <c>PatchAll(typeof(Patcher).Assembly)</c>, so no manual registration
///         is required.
///     </para>
///     <para>
///         All fields are touched on the main thread only (the manager updates
///         them in <see cref="InWorld.InWorldTerminalManager.Toggle"/>, the patch
///         reads them on the same thread inside KSA's render loop).
///     </para>
/// </summary>
internal static class FramePatches
{
    /// <summary>The quad to draw. Set by the manager when the feature is alive.</summary>
    public static QuadDisplay? Display;

    /// <summary>Master enable. Patches are no-ops when false.</summary>
    public static bool IsActive;

    /// <summary>Phase 7A focus state. Null = in-world feature is not alive.</summary>
    public static InWorldFocus? Focus;

    /// <summary>
    ///     Phase 7A — the secondary ImGui context the GLFW Harmony patches forward
    ///     input events into. Null when the feature is not alive.
    /// </summary>
    public static OffscreenContext? SecondaryContext;
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
