using Brutal.ImGuiApi;
using purrTTY.Core.Terminal;
using purrTTY.CustomShells;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Rendering;
using purrTTY.Display.Theming;
using PurrTTY.Terminal.Rendering;
using float3 = Brutal.Numerics.float3;

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
    ///     Opens the save-theme-as dialog.
    /// </summary>
    internal static Action? OpenSaveThemeDialog;

    /// <summary>
    ///     Opens the delete-theme confirmation dialog for the named user theme.
    /// </summary>
    internal static Action<string>? OpenDeleteThemeDialog;

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

    /// <summary>
    ///     Draws the shared menu content used by both ModMenu and the fallback injected menu.
    /// </summary>
    internal static void DrawMenuContent()
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

        var controller = MenuController;
        if (controller == null)
        {
            return;
        }

        ImGui.Separator();

        if (ImGui.BeginMenu("New Tab"))
        {
            DrawShellItems(controller, LaunchAsTab);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("New Window"))
        {
            DrawShellItems(controller, LaunchAsWindow);
            ImGui.EndMenu();
        }

        ImGui.Separator();

        if (ImGui.BeginMenu("Theme"))
        {
            DrawThemeMenu(controller);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Font"))
        {
            DrawFontMenu(controller);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Focus"))
        {
            DrawFocusMenu(controller);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Window"))
        {
            DrawWindowMenu(controller);
            ImGui.EndMenu();
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
        if (ImGui.BeginMenu("WSL2"))
        {
            var distributions = snapshot.WslDistributions;
            for (int i = 0; i < distributions.Count; i++)
            {
                if (ImGui.MenuItem(distributions[i].DisplayName))
                {
                    launch(ProcessLaunchOptions.CreateWsl(distributions[i].Name));
                }
            }

            ImGui.EndMenu();
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

    private static void DrawThemeMenu(GhosttyTerminalController controller)
    {
        var target = controller.FocusTarget;
        ImGui.TextDisabled(target != null ? $"Window: {target.Title}" : "No terminal window open");
        ImGui.Separator();

        string currentTheme = target?.Settings.ThemeName
                              ?? controller.Configuration.SelectedThemeName
                              ?? "Default";

        DrawThemeItems(controller, controller.Catalog.BuiltInThemes, currentTheme);

        if (controller.Catalog.UserThemes.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled("Saved Themes");
            DrawThemeItems(controller, controller.Catalog.UserThemes, currentTheme);
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Save Current As..."))
        {
            OpenSaveThemeDialog?.Invoke();
        }

        if (controller.Catalog.UserThemes.Count > 0 && ImGui.BeginMenu("Delete Theme"))
        {
            foreach (var theme in controller.Catalog.UserThemes)
            {
                if (ImGui.MenuItem(theme.Name))
                {
                    OpenDeleteThemeDialog?.Invoke(theme.Name);
                }
            }

            ImGui.EndMenu();
        }

        if (ImGui.MenuItem("Refresh Themes"))
        {
            controller.Catalog.Refresh();
        }
    }

    private static void DrawThemeItems(
        GhosttyTerminalController controller,
        IReadOnlyList<ThemeDefinition> themes,
        string currentTheme)
    {
        for (int i = 0; i < themes.Count; i++)
        {
            var theme = themes[i];
            bool selected = theme.Name.Equals(currentTheme, StringComparison.OrdinalIgnoreCase);
            if (ImGui.MenuItem(theme.Name, "", selected))
            {
                controller.ApplyTheme(theme);
            }
        }
    }

    private static void DrawFontMenu(GhosttyTerminalController controller)
    {
        var target = controller.FocusTarget;
        if (target == null)
        {
            ImGui.TextDisabled("No terminal window open");
            return;
        }

        int fontSize = (int)target.Settings.FontSize;
        ImGui.Text("Size");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180f);
        // DragInt (not SliderInt) so the value supports both click-drag and
        // double-click-to-type an exact size.
        if (ImGui.DragInt("##purrtty_font_size", ref fontSize, 0.25f, 4, 72, "%d px"))
        {
            target.Settings.FontSize = Math.Clamp(fontSize, 4, 72);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            controller.PersistDisplayDefaults(target);
        }

        ImGui.Separator();

        foreach (var family in PurrTTYFontManager.GetAvailableFontFamilies())
        {
            bool selected = family.Equals(target.Settings.FontFamily, StringComparison.OrdinalIgnoreCase);
            if (ImGui.MenuItem(family, "", selected) && !selected)
            {
                target.Settings.FontFamily = family;
                controller.PersistDisplayDefaults(target);
            }
        }
    }

    private static bool _hotZoneColorDirty;

    private static readonly (string Label, HotZonePlacement Placement)[] HotZonePlacements =
    {
        ("Top Left", HotZonePlacement.TopLeft),
        ("Top Center", HotZonePlacement.TopCenter),
        ("Top Right", HotZonePlacement.TopRight),
        ("Middle Left", HotZonePlacement.MiddleLeft),
        ("Middle Right", HotZonePlacement.MiddleRight),
        ("Bottom Left", HotZonePlacement.BottomLeft),
        ("Bottom Center", HotZonePlacement.BottomCenter),
        ("Bottom Right", HotZonePlacement.BottomRight),
    };

    /// <summary>
    ///     Focus settings for the focused window: cursor style/blink, the
    ///     focus/hover border, and lock mode with its focus hot zone. All of it
    ///     persists as new-window defaults and is captured by "Save Current As...".
    /// </summary>
    private static void DrawFocusMenu(GhosttyTerminalController controller)
    {
        var target = controller.FocusTarget;
        if (target == null)
        {
            ImGui.TextDisabled("No terminal window open");
            return;
        }

        var settings = target.Settings;

        ImGui.TextDisabled($"Window: {target.Title}");
        ImGui.Separator();

        ImGui.TextDisabled("Cursor");
        DrawCursorStyleItem(controller, target, "Block", CursorShape.Block);
        DrawCursorStyleItem(controller, target, "Bar", CursorShape.Bar);
        DrawCursorStyleItem(controller, target, "Underline", CursorShape.Underline);

        bool blink = settings.CursorBlink;
        if (ImGui.Checkbox("Blink", ref blink))
        {
            target.SetCursorStyle(settings.CursorStyle, blink);
            controller.PersistDisplayDefaults(target);
        }

        ImGui.Separator();
        ImGui.TextDisabled("Window Border");

        bool borderOnFocus = settings.BorderOnFocus;
        if (ImGui.Checkbox("Show when focused", ref borderOnFocus))
        {
            settings.BorderOnFocus = borderOnFocus;
            controller.PersistDisplayDefaults(target);
        }

        bool borderOnHover = settings.BorderOnHover;
        if (ImGui.Checkbox("Show when hovered", ref borderOnHover))
        {
            settings.BorderOnHover = borderOnHover;
            controller.PersistDisplayDefaults(target);
        }

        DrawOpacitySlider(controller, target, "Border Opacity", "##purrtty_border_opacity",
            static s => s.BorderOpacity, static (s, v) => s.BorderOpacity = v);

        ImGui.Separator();
        ImGui.TextDisabled("Lock Mode");

        bool lockMode = settings.LockMode;
        if (ImGui.Checkbox("Click-through when not focused", ref lockMode))
        {
            settings.LockMode = lockMode;
            controller.PersistDisplayDefaults(target);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("While locked and unfocused, mouse input passes through the terminal to the game.\nRefocus via the hot zone, this menu, or the toggle hotkey."u8);
        }

        bool hotZone = settings.HotZoneEnabled;
        if (ImGui.Checkbox("Focus hot zone", ref hotZone))
        {
            settings.HotZoneEnabled = hotZone;
            controller.PersistDisplayDefaults(target);
        }

        if (ImGui.BeginMenu("Hot Zone Position"))
        {
            foreach (var (label, placement) in HotZonePlacements)
            {
                if (ImGui.MenuItem(label, "", settings.HotZonePlacement == placement))
                {
                    settings.HotZonePlacement = placement;
                    controller.PersistDisplayDefaults(target);
                }
            }

            ImGui.EndMenu();
        }

        DrawHotZoneSizeSlider(controller, target, "Hot Zone Width", "##purrtty_hotzone_w",
            static s => s.HotZoneWidth, static (s, v) => s.HotZoneWidth = v);
        DrawHotZoneSizeSlider(controller, target, "Hot Zone Height", "##purrtty_hotzone_h",
            static s => s.HotZoneHeight, static (s, v) => s.HotZoneHeight = v);

        var zoneColor = settings.HotZoneColor;
        var rgb = new float3(zoneColor.R / 255f, zoneColor.G / 255f, zoneColor.B / 255f);
        ImGui.Text("Hot Zone Color");
        ImGui.SameLine();
        if (ImGui.ColorEdit3("##purrtty_hotzone_color", ref rgb, ImGuiColorEditFlags.NoInputs))
        {
            settings.HotZoneColor = new RgbaColor(
                (byte)Math.Clamp(rgb.X * 255f, 0f, 255f),
                (byte)Math.Clamp(rgb.Y * 255f, 0f, 255f),
                (byte)Math.Clamp(rgb.Z * 255f, 0f, 255f));
            _hotZoneColorDirty = true;
        }

        // Edits come from the swatch's picker *popup*, so the swatch item never
        // reports deactivated-after-edit; persist once the drag/click is released
        // (the open popup keeps this menu rendering, so this code still runs).
        if (_hotZoneColorDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _hotZoneColorDirty = false;
            controller.PersistDisplayDefaults(target);
        }

        DrawOpacitySlider(controller, target, "Hot Zone Opacity", "##purrtty_hotzone_opacity",
            static s => s.HotZoneOpacity, static (s, v) => s.HotZoneOpacity = v);
        DrawOpacitySlider(controller, target, "Hot Zone Hover Opacity", "##purrtty_hotzone_hover_opacity",
            static s => s.HotZoneHoverOpacity, static (s, v) => s.HotZoneHoverOpacity = v);
    }

    private static void DrawCursorStyleItem(
        GhosttyTerminalController controller,
        TerminalWindow target,
        string label,
        CursorShape shape)
    {
        if (ImGui.MenuItem(label, "", target.Settings.CursorStyle == shape))
        {
            target.SetCursorStyle(shape, target.Settings.CursorBlink);
            controller.PersistDisplayDefaults(target);
        }
    }

    // Slider helpers take static accessor lambdas over the settings object
    // (cached by the compiler) instead of closures over locals — the menus draw
    // every frame while open and must not allocate per slider per frame.
    private static void DrawHotZoneSizeSlider(
        GhosttyTerminalController controller,
        TerminalWindow target,
        string label,
        string id,
        Func<TerminalWindowSettings, float> get,
        Action<TerminalWindowSettings, float> set)
    {
        int pixels = (int)MathF.Round(get(target.Settings));
        ImGui.Text(label);
        ImGui.SetNextItemWidth(220f);
        // DragInt (not SliderInt) so the value supports both click-drag and
        // double-click-to-type an exact size, across the full hot-zone range.
        if (ImGui.DragInt(id, ref pixels, 1f, (int)TerminalWindow.MinHotZoneSize, (int)TerminalWindow.MaxHotZoneSize, "%d px"))
        {
            set(target.Settings, Math.Clamp(pixels, (int)TerminalWindow.MinHotZoneSize, (int)TerminalWindow.MaxHotZoneSize));
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            controller.PersistDisplayDefaults(target);
        }
    }

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

        var target = controller.FocusTarget;
        if (target == null)
        {
            return;
        }

        ImGui.Separator();

        DrawOpacitySlider(controller, target, "Background Opacity", "##purrtty_bg_opacity",
            static s => s.BackgroundOpacity, static (s, v) => s.BackgroundOpacity = v);
        DrawOpacitySlider(controller, target, "Foreground Opacity", "##purrtty_fg_opacity",
            static s => s.ForegroundOpacity, static (s, v) => s.ForegroundOpacity = v);
        DrawOpacitySlider(controller, target, "Cell Background Opacity", "##purrtty_cellbg_opacity",
            static s => s.CellBackgroundOpacity, static (s, v) => s.CellBackgroundOpacity = v);
    }

    private static void DrawOpacitySlider(
        GhosttyTerminalController controller,
        TerminalWindow target,
        string label,
        string id,
        Func<TerminalWindowSettings, float> get,
        Action<TerminalWindowSettings, float> set)
    {
        int percent = (int)MathF.Round(get(target.Settings) * 100f);
        ImGui.Text(label);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderInt(id, ref percent, 0, 100, "%d%%"))
        {
            set(target.Settings, Math.Clamp(percent, 0, 100) / 100f);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            controller.PersistDisplayDefaults(target);
        }
    }
}
