using HarmonyLib;
using KSA.Kfs;
using purrTTY.Kfs.Lsp.InGame;

namespace purrTTY.GameMod.Kfs;

/// <summary>
/// Risk R17 (plan §9): <c>KfsIntegration.Error(line)</c> logs the line — which the game's console
/// already shows — and writes it to <c>Output</c> too when <c>Output</c> is not the game's own
/// console writer (KfsIntegration.cs:128-135). With the language server's output tee installed that
/// second write would print every <c>kfs</c> error twice. This prefix/finalizer pair marks the call
/// so the tee passes the line to the editor (the command's output, <c>purrtty/kfsOutput</c>) but not
/// to the console writer: the console shows each error once, as without the tee.
/// </summary>
/// <remarks>
/// Optional (Patcher's optional list): if it does not apply, or the JIT inlined <c>Error</c> into a
/// caller compiled earlier, errors simply appear twice in the console. The pair only moves a
/// per-thread counter (<see cref="OutputTee.EnterClientsOnly"/>); the finalizer runs even when
/// <c>Error</c> throws, so the counter never stays raised. Main thread (the console's commands).
/// </remarks>
[HarmonyPatch(typeof(KfsIntegration), nameof(KfsIntegration.Error))]
internal static class KfsErrorEchoPatch
{
    [HarmonyPrefix]
    private static void Prefix() => OutputTee.EnterClientsOnly();

    [HarmonyFinalizer]
    private static void Finalizer() => OutputTee.ExitClientsOnly();
}
