using HarmonyLib;
using KSA;
using purrTTY.Display.Rendering.TerminalTexture;

namespace purrTTY.GameMod.Patches;

[HarmonyPatch(typeof(SuperMeshRenderSystem), nameof(SuperMeshRenderSystem.RenderMainPass))]
internal static class TerminalWorldQuadRenderPassPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        TerminalTextureWorldQuadPresenter.DrawCurrent();
    }
}
