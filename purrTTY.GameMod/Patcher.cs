using HarmonyLib;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using KSA;
using purrTTY.Display.Ghostty;
using purrTTY.GameMod;
using purrTTY.GameMod.InWorld;
using purrTTY.GameMod.Patches;
using purrTTY.GameMod.InWorld.Patches;
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
    // In-world quad draw injection (no-op unless the in-world feature is active).
    typeof(RenderMainPassPatch),
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
    // be re-evaluated by the next patch() — the mod set may have changed — and
    // the held-key model must not carry stale entries across a reload.
    Patch02.Reset();
    Patch01.Reset();
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
    if (!consoleOpen && (ImGui.GetIO().WantTextInput || InWorldTerminalManager.IsInputFocused))
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
  // Keys whose PRESS was forwarded to KSA.Program.OnKey (i.e. pressed while no
  // terminal was capturing input). This models the down-state the game believes
  // each key holds, so a RELEASE is forwarded only for a key the game actually
  // saw go down. Same idea as the per-button _appMousePressSent gating in
  // TerminalWindow.Input. Accessed only from Prefix1, which runs on the GLFW
  // poll (tick) thread — no locking needed.
  private static readonly HashSet<GlfwKey> s_gameHeldKeys = new();

  /// <summary>Drops the held-key model (called on unload; statics survive a StarMap reload).</summary>
  internal static void Reset() => s_gameHeldKeys.Clear();

  // The terminal owns the keyboard while a 2D window is focused OR the in-world
  // quad is focused — game keys are gated and routed to a shell in either case.
  private static bool TerminalOwnsKeyboard =>
    GhosttyTerminalController.IsAnyTerminalActive || InWorldTerminalManager.IsInputFocused;

  [HarmonyPrefix]
  [HarmonyPatch(nameof(KSA.Program.OnKey))]
  static bool Prefix1(GlfwWindow window, GlfwKey key, int scanCode, GlfwKeyAction action, GlfwModifier mods)
  {
    // Gate game key handling while a terminal is capturing input. The naive
    // "swallow press/repeat, always forward release" rule strands held movement
    // keys (camera/vehicle controls stick down) if it swallows a release too —
    // BUT unconditionally forwarding releases leaks KSA's release-triggered
    // toggle hotkeys: ToggleFps/ToggleUi/ToggleThreadProfiler/etc. fire in
    // Program.OnKey's `case GlfwKeyAction.Release` arm, so releasing an F-key
    // typed into a focused terminal toggled game UI even though the press was
    // correctly swallowed.
    //
    // Fix: the game may only see a key event for a key whose PRESS it received.
    // Forward a release/repeat only for a key in s_gameHeldKeys. That still
    // clears a genuinely-held key (its press WAS forwarded before focus moved to
    // the terminal) while never leaking a toggle whose press the terminal ate.
    switch (action)
    {
      case GlfwKeyAction.Press:
        if (TerminalOwnsKeyboard)
        {
          // Terminal owns the keyboard: swallow the press. The game never sees
          // the key go down, so its release stays swallowed too (not tracked).
          return false;
        }
        s_gameHeldKeys.Add(key);
        return true;

      case GlfwKeyAction.Repeat:
        // Only sustain a repeat the game already owns and only while no terminal
        // owns the keyboard; otherwise the terminal owns it.
        return !TerminalOwnsKeyboard && s_gameHeldKeys.Contains(key);

      case GlfwKeyAction.Release:
        // Forward iff the game believes this key is held; remove either way so
        // the held-key model drains as keys come back up.
        return s_gameHeldKeys.Remove(key);

      default:
        return true;
    }
  }
}
