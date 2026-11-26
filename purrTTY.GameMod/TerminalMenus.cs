using Brutal.ImGuiApi;
using purrTTY.Core.Terminal;
using purrTTY.CustomShells;
using purrTTY.Display.Ghostty;
using PurrTTY.Terminal.Ghostty;

namespace purrTTY.GameMod;

/// <summary>
///     The purrTTY game-menu content, drawn two ways with identical results: via
///     the <c>[ModMenuEntry]</c> attribute when the ModMenu companion mod is
///     present (<see cref="TerminalMod.DrawMenu"/>), and via <c>Patch02</c>'s
///     postfix on KSA's <c>Program.DrawProgramMenusHook()</c> otherwise. The
///     static hook fields are the seam to the live <see cref="TerminalMod"/>
///     instance: set in <c>InitializeTerminal</c>, cleared in
///     <c>DisposeResources</c>, and read here every frame a menu is open.
/// </summary>
internal static class TerminalMenus
{
    /// <summary>
    ///     Shared toggle action used by both hotkey and game menu bar entry.
    /// </summary>
    internal static Action? Toggle;

    /// <summary>
    ///     Returns the current terminal visibility state. Used by the game menu bar to show a checkmark.
    /// </summary>
    internal static Func<bool>? GetIsVisible;

    /// <summary>
    ///     Returns the active toggle hotkey text for menu display.
    /// </summary>
    internal static Func<string>? GetToggleHotkeyShortcut;

    /// <summary>
    ///     Opens the toggle hotkey settings modal.
    /// </summary>
    internal static Action? OpenToggleHotkeySettings;

    /// <summary>
    ///     Opens the per-terminal theme manager dialog (target picker, palette, font,
    ///     opacity, advanced cursor/border/lock settings, rename, and save/load).
    /// </summary>
    internal static Action? OpenThemeDialog;

    /// <summary>Opens the in-world terminal manager dialog. Null when the in-world feature is unavailable.</summary>
    internal static Action? OpenInWorldManager;

    /// <summary>Opens the layouts manager dialog (save / load / edit / tear-down sets of terminals). Null when unavailable.</summary>
    internal static Action? OpenLayoutManager;

    /// <summary>
    ///     Draws the entries of the <c>kFS</c> submenu (<c>Kfs/KfsMenu.Draw</c>: the kFS extension's
    ///     status and its <c>purr</c> verbs). Null until the mod has tried to install the extension.
    /// </summary>
    internal static Action? DrawKfsMenu;

    /// <summary>
    ///     The live controller the game menus act on (null until initialized).
    /// </summary>
    internal static GhosttyTerminalController? MenuController;

    // The menu draw path runs every frame while a menu is open; everything it
    // needs per frame is pre-built (cached shortcut string, static launch
    // delegates reading MenuController) so drawing allocates nothing.
    private static readonly string DefaultToggleShortcut = ToggleHotkeyBinding.Default.ToShortcutString();

    private static readonly Action<ProcessLaunchOptions> LaunchAsTab = static options =>
    {
        if (MenuController is { } c)
        {
            c.OpenTab(options);
            c.IsVisible = true;
        }
    };

    private static readonly Action<ProcessLaunchOptions> LaunchAsWindow = static options =>
    {
        if (MenuController is { } c)
        {
            c.OpenWindow(options);
            c.IsVisible = true;
        }
    };

    private static bool s_menuFailureLogged;

    /// <summary>
    ///     Draws the shared menu content used by both ModMenu and the fallback injected menu.
    ///     Never throws: it runs inside a Harmony postfix of the game's menu hook (or ModMenu's
    ///     draw), so an exception is logged once and swallowed; every submenu it begins is ended
    ///     in a <c>finally</c>, so the ImGui stack stays balanced either way.
    /// </summary>
    internal static void DrawMenuContent()
    {
        try
        {
            DrawMenuContentCore();
        }
        catch (Exception ex)
        {
            if (!s_menuFailureLogged)
            {
                s_menuFailureLogged = true;
                purrTTY.Logging.ModLog.Log.Warning($"purrTTY: drawing the menu failed ({ex.GetType().Name}: {ex.Message}); further failures are not logged");
            }
        }
    }

    // Ends a submenu begun by the caller, also when its body threw.
    private static void Submenu(Action body)
    {
        try
        {
            body();
        }
        finally
        {
            ImGui.EndMenu();
        }
    }

    private static void DrawMenuContentCore()
    {
        var isVisible = GetIsVisible?.Invoke() ?? false;
        var hotkeyShortcut = GetToggleHotkeyShortcut?.Invoke() ?? DefaultToggleShortcut;

        if (ImGui.MenuItem("Toggle Terminal", hotkeyShortcut, isVisible))
        {
            Toggle?.Invoke();
        }

        if (ImGui.MenuItem("Toggle Hotkey"))
        {
            OpenToggleHotkeySettings?.Invoke();
        }

        if (OpenInWorldManager != null || OpenLayoutManager != null || DrawKfsMenu != null)
        {
            ImGui.Separator();
        }

        if (OpenInWorldManager != null && ImGui.MenuItem("In-World Terminals..."))
        {
            OpenInWorldManager.Invoke();
        }

        if (OpenLayoutManager != null && ImGui.MenuItem("Layouts..."))
        {
            OpenLayoutManager.Invoke();
        }

        var drawKfsMenu = DrawKfsMenu;
        if (drawKfsMenu != null && KSA.ImGuiHelper.BeginMenu("kFS"))
        {
            Submenu(drawKfsMenu);
        }

        var controller = MenuController;
        if (controller == null)
        {
            return;
        }

        ImGui.Separator();

        if (KSA.ImGuiHelper.BeginMenu("New Tab"))
        {
            try
            {
                DrawShellItems(controller, LaunchAsTab);
            }
            finally
            {
                ImGui.EndMenu();
            }
        }

        if (KSA.ImGuiHelper.BeginMenu("New Window"))
        {
            try
            {
                DrawShellItems(controller, LaunchAsWindow);
            }
            finally
            {
                ImGui.EndMenu();
            }
        }

        ImGui.Separator();

        // Per-terminal theming (palette, font, opacity, advanced) lives in one
        // dialog now — the old Theme/Font/Focus submenus collapsed into this.
        if (ImGui.MenuItem("Theme..."))
        {
            OpenThemeDialog?.Invoke();
        }

        if (KSA.ImGuiHelper.BeginMenu("Window"))
        {
            try
            {
                DrawWindowMenu(controller);
            }
            finally
            {
                ImGui.EndMenu();
            }
        }
    }

    private static void DrawShellItems(GhosttyTerminalController controller, Action<ProcessLaunchOptions> launch)
    {
        // The draw path must never trigger shell detection (a slow probe would
        // hang the render thread) — it only reads the snapshot built once on a
        // background thread at init.
        var snapshot = ShellMenuCache.Current;
        if (snapshot == null)
        {
            ImGui.TextDisabled("Detecting shells...");
            if (ImGui.MenuItem("Game Console"))
            {
                launch(CreateLaunchOptionsFor(controller, ShellType.CustomGame));
            }

            DrawRegisteredCustomShellItems(launch);
            return;
        }

        var entries = snapshot.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var (label, shellType) = entries[i];
            if (shellType == ShellType.Wsl)
            {
                DrawWslItems(snapshot, launch);
                continue;
            }

            if (shellType == ShellType.Auto && !OperatingSystem.IsWindows())
            {
                DrawUnixShellItems(snapshot, launch);
                continue;
            }

            if (ImGui.MenuItem(label))
            {
                launch(CreateLaunchOptionsFor(controller, shellType));
            }
        }

        DrawRegisteredCustomShellItems(launch);
    }

    /// <summary>
    ///     Custom shells registered by <b>other mods</b> (over the exported
    ///     <c>purrTTY.CustomShellContract</c>), enumerated live from the registry on
    ///     every draw. Reading live — instead of snapshotting into
    ///     <see cref="ShellMenuCache"/> — solves cross-mod registration timing without
    ///     a refresh hook: mod load order is undefined, so another mod may register
    ///     its shell after this mod's init built the snapshot. The read stays within
    ///     the never-detect-on-the-draw-path rule: it is a plain ConcurrentDictionary
    ///     enumeration, and registry discovery already ran synchronously in
    ///     <c>InitializeTerminal</c> before <see cref="MenuController"/> — the gate on
    ///     reaching this code — was published.
    /// </summary>
    private static void DrawRegisteredCustomShellItems(Action<ProcessLaunchOptions> launch)
    {
        foreach (var (id, metadata) in CustomShellRegistry.Instance.GetAvailableShells())
        {
            if (id == nameof(GameConsoleShell))
            {
                continue; // built-in: already drawn via its ShellType.CustomGame entry
            }

            if (ImGui.MenuItem(metadata.Name))
            {
                launch(ProcessLaunchOptions.CreateCustomGame(id));
            }
        }
    }

    /// <summary>
    ///     On Linux/macOS the generic "Default Shell" entry is replaced by one item
    ///     per detected shell (/etc/shells + $SHELL), with the user's default marked
    ///     — the Unix analogue of the per-distribution WSL items.
    /// </summary>
    private static void DrawUnixShellItems(ShellMenuCache.Snapshot snapshot, Action<ProcessLaunchOptions> launch)
    {
        if (snapshot.UnixShells.Count == 0)
        {
            if (ImGui.MenuItem("Default Shell"))
            {
                launch(ProcessLaunchOptions.CreateDefault());
            }

            return;
        }

        var shells = snapshot.UnixShells;
        for (int i = 0; i < shells.Count; i++)
        {
            if (ImGui.MenuItem(shells[i].DisplayName))
            {
                launch(ProcessLaunchOptions.CreateCustom(shells[i].Path));
            }
        }
    }

    private static void DrawWslItems(ShellMenuCache.Snapshot snapshot, Action<ProcessLaunchOptions> launch)
    {
        // Only reached when the snapshot contains a Wsl entry, which requires at
        // least one detected distribution — no generic bare-`wsl` fallback item
        // (launching wsl.exe without a distribution yields a dead session).
        if (KSA.ImGuiHelper.BeginMenu("WSL2"))
        {
            try
            {
                var distributions = snapshot.WslDistributions;
                for (int i = 0; i < distributions.Count; i++)
                {
                    if (ImGui.MenuItem(distributions[i].DisplayName))
                    {
                        launch(ProcessLaunchOptions.CreateWsl(distributions[i].Name));
                    }
                }
            }
            finally
            {
                ImGui.EndMenu();
            }
        }
    }

    private static ProcessLaunchOptions CreateLaunchOptionsFor(GhosttyTerminalController controller, ShellType shellType)
        => shellType switch
        {
            ShellType.PowerShell => ProcessLaunchOptions.CreatePowerShell(),
            ShellType.PowerShellCore => ProcessLaunchOptions.CreatePowerShellCore(),
            ShellType.Cmd => ProcessLaunchOptions.CreateCmd(),
            // Stamps the configured prompt into the shell environment.
            ShellType.CustomGame => controller.Configuration.CreateGameShellLaunchOptions(),
            _ => ProcessLaunchOptions.CreateDefault(),
        };

    /// <summary>
    ///     Global, non-theme window toggles: chrome auto-hide, the perf HUD, kitty
    ///     diagnostics, and the kitty texture-cache cap. Per-terminal appearance
    ///     (palette, font, opacity, cursor/border/lock) lives in the Theme dialog.
    /// </summary>
    private static void DrawWindowMenu(GhosttyTerminalController controller)
    {
        bool hideChrome = controller.Configuration.HideUiWhenNotHovered;
        if (ImGui.Checkbox("Hide chrome when not hovered", ref hideChrome))
        {
            controller.Configuration.HideUiWhenNotHovered = hideChrome;
            controller.Configuration.Save();
        }

        bool perfHud = TerminalWindow.ShowPerfHud;
        if (ImGui.Checkbox("Show performance HUD", ref perfHud))
        {
            TerminalWindow.ShowPerfHud = perfHud;
        }

        bool kittyDiag = GhosttyTerminalSurface.DiagnoseKittyProtocol;
        if (ImGui.Checkbox("Diagnose kitty APC bytes", ref kittyDiag))
        {
            GhosttyTerminalSurface.DiagnoseKittyProtocol = kittyDiag;
        }

        int maxTex = TerminalWindow.KittyCacheLimit;
        ImGui.Text("Kitty cache limit");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        if (ImGui.DragInt("##purrtty_kitty_cache", ref maxTex, 0.5f, 4, 256, "%d tex"))
        {
            TerminalWindow.KittyCacheLimit = Math.Clamp(maxTex, 4, 256);
        }
    }
}
