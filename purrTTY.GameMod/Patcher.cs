using HarmonyLib;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using KSA;
using purrTTY.Display.Ghostty;
using purrTTY.GameMod;
using purrTTY.GameMod.Patches;
using purrTTY.Logging;

internal static class Patcher
{
  private static Harmony? m_harmony;

  // Each Harmony patch is applied independently with its own try/catch so one
  // drifted target (a KSA/Brutal API rename, an IL reshuffle) can never abort
  // the whole mod — the old PatchAll path threw on the first failure and took
  // InitializeTerminal() down with it. Patches are classified by impact:
  //   required — input gating / typing guard; a failure degrades core function.
  //   optional — menu fallback + console capture; a failure leaves the mod usable.
  private static readonly Type[] s_requiredPatches =
  {
    typeof(Patch01),
    typeof(Patch03_HotkeyGuard),
  };

  private static readonly Type[] s_optionalPatches =
  {
    typeof(Patch02),
    typeof(ConsoleWindowPrintPatch),
  };

  public static void patch()
  {
    // Recreate the instance if a previous unload() nulled it: a StarMap reload
    // without an ALC unload reuses these static types, and `null?.Patch…` would
    // make patch() a silent no-op (no input gating, no menu, no capture).
    var harmony = m_harmony ??= new Harmony("purrTTY");

    foreach (var type in s_requiredPatches)
    {
      ApplyPatch(harmony, type, required: true);
    }

    foreach (var type in s_optionalPatches)
    {
      ApplyPatch(harmony, type, required: false);
    }
  }

  private static void ApplyPatch(Harmony harmony, Type type, bool required)
  {
    try
    {
      harmony.CreateClassProcessor(type).Patch();
    }
    catch (Exception ex)
    {
      string severity = required ? "REQUIRED" : "optional";
      string consequence = required
        ? "terminal input gating may be degraded; the terminal will still initialize"
        : "the mod remains usable without it";
      ModLog.Log.Error(
        $"purrTTY: {severity} Harmony patch '{type.Name}' failed to apply ({consequence}): {ex.Message}");
    }
  }

  public static void unload()
  {
    m_harmony?.UnpatchAll("purrTTY");
    m_harmony = null;

    // Statics survive a StarMap reload without an ALC unload (same reason
    // m_harmony is recreated lazily above), so cached environment probes must
    // be re-evaluated by the next patch() — the mod set may have changed.
    Patch02.Reset();
  }
}

/// <summary>
/// Draws the fallback "purrTTY" game menu via the <c>DrawProgramMenusHook()</c>
/// extension point KSA calls at the end of its menu bar (right after the View
/// menu), used only when the ModMenu companion mod is absent. A postfix on that
/// empty public hook replaces the previous fragile IL transpiler — no opcode
/// pattern matching to drift.
/// </summary>
[HarmonyPatch(typeof(KSA.Program), nameof(KSA.Program.DrawProgramMenusHook))]
static class Patch02
{
  private static bool? s_isModMenuEnabled;

  /// <summary>Drops the cached ModMenu-presence probe (called on unload; see Patcher.unload).</summary>
  internal static void Reset() => s_isModMenuEnabled = null;

  [HarmonyPostfix]
  public static void Postfix()
  {
    if (IsModMenuEnabled())
    {
      return;
    }

    if (ImGui.BeginMenu("purrTTY"))
    {
      // Keep the (possibly auto-hidden) menu bar shown while our menu is open.
      Program.MainViewport.MenuBarInUse = true;

      TerminalMenus.DrawMenuContent();

      ImGui.EndMenu();
    }
  }

  private static bool IsModMenuEnabled()
  {
    return s_isModMenuEnabled ??= ModLibrary.Find("ModMenu") is not null;
  }
}

/// <summary>
/// Blocks game hotkeys (GameSettings.OnKeyAll) whenever an ImGui text input has
/// keyboard focus (e.g. the save-theme name field), so typing does not trigger
/// game key bindings. The in-game console is exempt so it keeps working.
/// </summary>
[HarmonyPatch(typeof(GameSettings), nameof(GameSettings.OnKeyAll))]
static class Patch03_HotkeyGuard
{
  [HarmonyPrefix]
  static bool Prefix(ref bool __result)
  {
    // Program.ConsoleWindow can be unassigned very early in startup; treat a
    // null/closed console as "not open" so a focused text field still suppresses
    // game hotkeys without NREing on the static field.
    bool consoleOpen = Program.ConsoleWindow is { IsOpen: true };
    if (!consoleOpen && ImGui.GetIO().WantTextInput)
    {
      __result = true;
      return false;
    }
    return true;
  }
}

[HarmonyPatch(typeof(KSA.Program))]
class Patch01
{

  [HarmonyPrefix]
  [HarmonyPatch(nameof(KSA.Program.OnKey))]
  static bool Prefix1(GlfwWindow window, GlfwKey key, int scanCode, GlfwKeyAction action, GlfwModifier mods)
  {
    // Gate game key handling while a terminal is capturing input — but always
    // forward key *releases*. Swallowing a Release at the moment focus moves
    // into the terminal strands the game's held-key state (camera/vehicle
    // controls stick down); only presses/repeats need suppressing.
    if (GhosttyTerminalController.IsAnyTerminalActive && action != GlfwKeyAction.Release)
    {
      // skipping Program.OnKey for the press/repeat
      return false;
    }
    return true;
  }
}
