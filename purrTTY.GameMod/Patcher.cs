using HarmonyLib;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using KSA;
using purrTTY.Display.Controllers;
using purrTTY.GameMod;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;


[HarmonyPatch]
internal static class Patcher
{
  private static Harmony? m_harmony = new Harmony("purrTTY");

  public static void patch()
  {
    m_harmony?.PatchAll(typeof(Patcher).Assembly);
  }

  public static void unload()
  {
    m_harmony?.UnpatchAll("purrTTY");
    m_harmony = null;
  }
}

[HarmonyPatch(typeof(KSA.Program), "DrawMenuBar")]
static class Patch02
{
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);

        MethodInfo endMenuMethod = AccessTools.Method(typeof(ImGui), nameof(ImGui.EndMenu));
        MethodInfo injectMethod  = AccessTools.Method(typeof(Patch02), nameof(DrawPurrTtyMenu));

        int endMenuCount = 0;
        for (int i = codes.Count - 1; i >= 0; i--)
        {
            if ((codes[i].opcode == OpCodes.Call || codes[i].opcode == OpCodes.Callvirt)
                && codes[i].operand is MethodInfo m && m == endMenuMethod)
            {
                endMenuCount++;
                if (endMenuCount == 1) // last EndMenu in source = View menu's EndMenu
                {
                    codes.Insert(i + 4, new CodeInstruction(OpCodes.Ldarg_1));
                    codes.Insert(i + 5, new CodeInstruction(OpCodes.Call, injectMethod));
                    Console.WriteLine("purrTTY: game menu bar injection succeeded");
                    break;
                }
            }
        }

        return codes;
    }

    private static void DrawPurrTtyMenu(Viewport viewport)
    {
        if (ImGui.BeginMenu("purrTTY"))
        {
            viewport.MenuBarInUse = true;

            bool isVisible = TerminalMod.GetIsVisible?.Invoke() ?? false;
            if (ImGui.MenuItem("Toggle Terminal", "F12", isVisible))
                TerminalMod.Toggle?.Invoke();

            ImGui.EndMenu();
        }
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
