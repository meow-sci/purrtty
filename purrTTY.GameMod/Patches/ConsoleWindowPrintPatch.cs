using HarmonyLib;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Abstractions;
using purrTTY.CustomShells;

namespace purrTTY.GameMod.Patches;

/// <summary>
///     Harmony patch for ConsoleWindow.Print to capture console output.
///     This patch is automatically installed by Patcher.patch() at mod startup.
/// </summary>
/// <remarks>
///     The installed Brutal API funnels every string/char print (and the
///     ILogger <c>OnLog</c> path) into the single
///     <c>Print(ReadOnlySpan&lt;char&gt;, ImColor8, int)</c> sink, so patching it
///     captures all colored console output in one place. (The older API exposed a
///     <c>Print(string, uint, int, ConsoleLineType)</c> sink; both
///     <c>ConsoleLineType</c> and the <c>uint</c> color overloads were removed.)
///     Caveat for the next Brutal bump: the byte-span/<c>ImString</c> <c>Print</c>
///     overloads call <c>AddPendingMessage</c> directly and bypass this sink —
///     capture is complete only because every current KSA call site prints
///     string/char. A future build printing <c>u8</c> literals directly would
///     silently escape capture; re-verify the funnel when the pin moves.
/// </remarks>
[HarmonyPatch(typeof(ConsoleWindow))]
[HarmonyPatch(nameof(ConsoleWindow.Print))]
[HarmonyPatch(new[] { typeof(ReadOnlySpan<char>), typeof(ImColor8), typeof(int) })]
public static class ConsoleWindowPrintPatch
{
    /// <summary>
    ///     Postfix patch that captures console output. Parameter names must match
    ///     the original (<c>text</c>, <c>color</c>) so Harmony injects them.
    /// </summary>
    [HarmonyPostfix]
    public static void Postfix(ReadOnlySpan<char> text, ImColor8 color)
    {
        // Cheap lock-free gate so we only allocate a string while our shell is
        // actively capturing a command's output.
        if (!GameConsoleShell.IsCapturing)
        {
            return;
        }

        try
        {
            GameConsoleShell.OnConsolePrint(new string(text), color);
        }
        catch (Exception)
        {
            // Silently handle errors to avoid disrupting the game console
        }
    }
}
