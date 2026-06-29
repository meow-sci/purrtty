using Brutal.VulkanApi;
using HarmonyLib;
using KSA;
using purrTTY.Logging;

namespace purrTTY.GameMod.InWorld.Patches;

/// <summary>
///     Harmony postfix that injects the in-world quad draw into the game's scene
///     pass. <c>SuperMeshRenderSystem.RenderMainPass</c> runs inside the
///     already-begun offscreen render pass on the supplied command buffer;
///     appending our draw here — after KSA's opaque mesh draws, before the caller
///     calls <c>EndRenderPass</c> — makes the quad depth-test against the full
///     opaque scene (so a part can occlude it and it can occlude parts behind it).
///     <para>
///         Registered as an <b>optional</b> patch in <c>Patcher</c>: a drifted
///         target must never block terminal init. <see cref="InWorldTerminalManager.Active"/>
///         and <see cref="InWorldTerminalManager.Instance"/> are plain statics
///         flipped on the main thread; the postfix runs on that same render thread,
///         so no synchronization is needed.
///     </para>
/// </summary>
[HarmonyPatch(typeof(SuperMeshRenderSystem), nameof(SuperMeshRenderSystem.RenderMainPass))]
internal static class RenderMainPassPatch
{
    [HarmonyPostfix]
    public static void Postfix(CommandBuffer commandBuffer)
    {
        if (!InWorldTerminalManager.Active)
        {
            return;
        }

        try
        {
            InWorldTerminalManager.Instance?.RecordDrawAll(commandBuffer);
        }
        catch (Exception ex)
        {
            // Never crash the render loop on a transient draw failure; disable so it
            // doesn't spam every frame (re-enabled on the next toggle/reload).
            ModLog.Log.Error($"purrTTY in-world quad draw failed: {ex}");
            InWorldTerminalManager.Active = false;
        }
    }
}
