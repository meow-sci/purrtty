using HarmonyLib;
using Brutal.GlfwApi;
using caTTY.Display.Controllers;


[HarmonyPatch]
internal static class Patcher
{
  private static Harmony? m_harmony = new Harmony("caTTY");

  public static void patch()
  {
    m_harmony?.PatchAll(typeof(Patcher).Assembly);
  }

  public static void unload()
  {
    m_harmony?.UnpatchAll("caTTY");
    m_harmony = null;
  }
}

[HarmonyPatch(typeof(KSA.Program))]
class Patch01
{

  [HarmonyPrefix]
  [HarmonyPatch(nameof(KSA.Program.OnKey))]
  static bool Prefix1(GlfwWindow window, GlfwKey key, int scanCode, GlfwKeyAction action, GlfwModifier mods)
  {
    if (TerminalController.IsAnyTerminalActive)
    {
      // skipping Program.OnKey
      return false;
    }
    return true;
  }
}
