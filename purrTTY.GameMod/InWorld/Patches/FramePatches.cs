using System;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;
using purrTTY.GameMod.InWorld.Display;
using purrTTY.Logging;

namespace purrTTY.GameMod.InWorld.Patches;

/// <summary>
///     Static glue between <see cref="InWorld.InWorldTerminalManager"/> and the
///     Harmony postfix on <c>SuperMeshRenderSystem.RenderMainPass</c>.
///     <para>
///         The patch is attribute-discovered by <c>Patcher.patch()</c>'s
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

    /// <summary>Master enable. Patch is a no-op when false.</summary>
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
