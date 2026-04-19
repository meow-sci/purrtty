using HarmonyLib;
using Brutal.ImGuiApi.Abstractions;
using caTTY.CustomShells;

namespace caTTY.GameMod.Patches;

/// <summary>
///     Harmony patch for ConsoleWindow.Print to capture console output.
///     This patch is automatically installed by Patcher.patch() at mod startup.
/// </summary>
[HarmonyPatch(typeof(ConsoleWindow))]
[HarmonyPatch(nameof(ConsoleWindow.Print))]
[HarmonyPatch(new[] { typeof(string), typeof(uint), typeof(int), typeof(ConsoleLineType) })]
public static class ConsoleWindowPrintPatch
{
    /// <summary>
    ///     Postfix patch that captures console output.
    /// </summary>
    [HarmonyPostfix]
    public static void Postfix(string inOutput, uint inColor, ConsoleLineType inType)
    {
        try
        {
            GameConsoleShell.OnConsolePrint(inOutput, inColor, inType);
        }
        catch (Exception)
        {
            // Silently handle errors to avoid disrupting the game console
        }
    }
}
