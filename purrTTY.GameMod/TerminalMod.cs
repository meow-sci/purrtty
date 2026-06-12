using Brutal.ImGuiApi;
using purrTTY.Core.Terminal;
using purrTTY.Display.Configuration;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Rendering;
using purrTTY.Display.Theming;
using PurrTTY.Terminal.Rendering;
using StarMap.API;
using purrTTY.Logging;
using ModMenu;
using float2 = Brutal.Numerics.float2;
using float3 = Brutal.Numerics.float3;
using float4 = Brutal.Numerics.float4;

namespace purrTTY.GameMod;

/// <summary>
///     KSA game mod for purrTTY terminal emulator.
///     Provides multi-window, multi-tab terminal sessions toggled with a
///     configurable hotkey and driven from the top-level game menus.
/// </summary>
[StarMapMod]
public class TerminalMod
{
    private const string ToggleHotkeyPopupId = "purrTTY Hot Key Settings##purrtty_toggle_hotkey_modal";
    private const string SaveThemePopupId = "Save purrTTY Theme##purrtty_save_theme_modal";
    private const string DeleteThemePopupId = "Delete purrTTY Theme##purrtty_delete_theme_modal";

    private GhosttyTerminalController? _controller;
    private bool _isDisposed;
    private bool _isInitialized;
    private bool _terminalVisible;
    private ToggleHotkeyBinding _toggleHotkey = ToggleHotkeyBinding.Default;
    private ToggleHotkeyBinding _draftToggleHotkey = ToggleHotkeyBinding.Default;
    private ToggleHotkeyBinding? _lastCapturedToggleHotkey;
    private bool _isCapturingToggleHotkey;
    private bool _toggleHotkeyModalOpenRequested;
    private bool _suppressNextTerminalKeyboardInputFrame;

    private readonly ImInputString _themeNameInput = new(96);
    private bool _saveThemeModalOpenRequested;
    private string? _saveThemeError;

    private bool _deleteThemeModalOpenRequested;
    private string? _deleteThemeName;
    private string? _deleteThemeError;

    private bool IsTerminalVisible => _controller?.IsVisible ?? _terminalVisible;

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

    /// <summary>
    ///     Gets a value indicating whether the mod should be unloaded immediately.
    /// </summary>
    public bool ImmediateUnload => false;

    [ModMenuEntry("purrTTY")]
    public static void DrawMenu()
    {
        DrawMenuContent();
    }

    /// <summary>
    ///     Draws the shared menu content used by both ModMenu and the fallback injected menu.
    /// </summary>
    internal static void DrawMenuContent()
    {
        var isVisible = GetIsVisible?.Invoke() ?? false;
        var hotkeyShortcut = GetToggleHotkeyShortcut?.Invoke() ?? ToggleHotkeyBinding.Default.ToShortcutString();

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
            DrawShellItems(controller, options =>
            {
                controller.OpenTab(options);
                controller.IsVisible = true;
            });
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("New Window"))
        {
            DrawShellItems(controller, options =>
            {
                controller.OpenWindow(options);
                controller.IsVisible = true;
            });
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

            return;
        }

        foreach (var (label, shellType) in snapshot.Entries)
        {
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

        foreach (var shell in snapshot.UnixShells)
        {
            if (ImGui.MenuItem(shell.DisplayName))
            {
                launch(ProcessLaunchOptions.CreateCustom(shell.Path));
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
            foreach (var distribution in snapshot.WslDistributions)
            {
                if (ImGui.MenuItem(distribution.DisplayName))
                {
                    launch(ProcessLaunchOptions.CreateWsl(distribution.Name));
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
            ShellType.CustomGame => ProcessLaunchOptions.CreateCustomGame(
                controller.Configuration.DefaultCustomGameShellId ?? "GameConsoleShell"),
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
        foreach (var theme in themes)
        {
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
        if (ImGui.SliderInt("##purrtty_font_size", ref fontSize, 4, 72))
        {
            target.Settings.FontSize = fontSize;
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
            () => settings.BorderOpacity, v => settings.BorderOpacity = v);

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
            () => settings.HotZoneWidth, v => settings.HotZoneWidth = v);
        DrawHotZoneSizeSlider(controller, target, "Hot Zone Height", "##purrtty_hotzone_h",
            () => settings.HotZoneHeight, v => settings.HotZoneHeight = v);

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
            () => settings.HotZoneOpacity, v => settings.HotZoneOpacity = v);
        DrawOpacitySlider(controller, target, "Hot Zone Hover Opacity", "##purrtty_hotzone_hover_opacity",
            () => settings.HotZoneHoverOpacity, v => settings.HotZoneHoverOpacity = v);
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

    private static void DrawHotZoneSizeSlider(
        GhosttyTerminalController controller,
        TerminalWindow target,
        string label,
        string id,
        Func<float> get,
        Action<float> set)
    {
        int pixels = (int)MathF.Round(get());
        ImGui.Text(label);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderInt(id, ref pixels, (int)TerminalWindow.MinHotZoneSize, 200, "%d px"))
        {
            set(Math.Clamp(pixels, (int)TerminalWindow.MinHotZoneSize, (int)TerminalWindow.MaxHotZoneSize));
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
            () => target.Settings.BackgroundOpacity, v => target.Settings.BackgroundOpacity = v);
        DrawOpacitySlider(controller, target, "Foreground Opacity", "##purrtty_fg_opacity",
            () => target.Settings.ForegroundOpacity, v => target.Settings.ForegroundOpacity = v);
        DrawOpacitySlider(controller, target, "Cell Background Opacity", "##purrtty_cellbg_opacity",
            () => target.Settings.CellBackgroundOpacity, v => target.Settings.CellBackgroundOpacity = v);
    }

    private static void DrawOpacitySlider(
        GhosttyTerminalController controller,
        TerminalWindow target,
        string label,
        string id,
        Func<float> get,
        Action<float> set)
    {
        int percent = (int)MathF.Round(get() * 100f);
        ImGui.Text(label);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderInt(id, ref percent, 0, 100, "%d%%"))
        {
            set(Math.Clamp(percent, 0, 100) / 100f);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            controller.PersistDisplayDefaults(target);
        }
    }

    /// <summary>
    ///     Called after the GUI is rendered.
    /// </summary>
    /// <param name="dt">Delta time.</param>
    [StarMapAfterGui]
    public void OnAfterUi(double dt)
    {
        if (!_isInitialized || _isDisposed)
        {
            return;
        }

        try
        {
            bool modalVisible = RenderToggleHotkeyModal();
            modalVisible |= RenderSaveThemeModal();
            modalVisible |= RenderDeleteThemeModal();

            // Handle terminal toggle keybind (dynamic, defaults to F12)
            if (!modalVisible && IsToggleHotkeyPressed())
            {
                bool wasVisible = IsTerminalVisible;
                ToggleTerminal();

                if (!wasVisible && IsTerminalVisible)
                {
                    // Swallow the hotkey press so printable toggle keys do not appear as terminal input when opening.
                    _suppressNextTerminalKeyboardInputFrame = true;
                }
            }

            // Update runs even while hidden: it drains hidden sessions' PTY
            // backlogs on a low cadence (their inboxes otherwise grow unbounded
            // under chatty output). Render() is also called unconditionally and
            // early-outs when hidden — that early-out clears the game-key gate
            // (_anyTerminalActive). Calling it only while visible would strand
            // the flag `true` after hiding a focused terminal, killing the
            // game's entire keyboard pipeline until the terminal is shown again.
            if (_controller != null)
            {
                _controller.Update((float)dt);
                _controller.Render();
            }
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod OnAfterUi error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            // Don't let exceptions crash the game
        }
    }

    /// <summary>
    ///     Called before the GUI is rendered.
    /// </summary>
    /// <param name="dt">Delta time.</param>
    [StarMapBeforeGui]
    public void OnBeforeUi(double dt)
    {
        // No pre-UI logic needed currently
    }

    /// <summary>
    ///     Called when all mods are loaded.
    /// </summary>
    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        ModLog.Log.Debug("purrTTY OnFullyLoaded");
        try
        {
            // patch() applies each Harmony patch independently and never throws —
            // an optional patch (menu fallback, console capture) failing must not
            // block terminal init, and a required one failing is logged loudly but
            // still lets the terminal come up.
            Patcher.patch();

            InitializeTerminal();
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY GameMod initialization failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Called for immediate loading.
    /// </summary>
    [StarMapImmediateLoad]
    public void OnImmediatLoad()
    {
        ModLog.Log.Debug("purrTTY OnImmediatLoad");
        // No immediate load logic needed
    }

    /// <summary>
    ///     Called when the mod is unloaded.
    /// </summary>
    [StarMapUnload]
    public void Unload()
    {
        ModLog.Log.Debug("purrTTY Unload");
        try
        {
            Patcher.unload();

            DisposeResources();
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod unload error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the production configuration directory path for persistent storage.
    /// This ensures theme configuration is saved to the user's Documents folder.
    /// Must be called BEFORE any ThemeConfiguration access.
    /// </summary>
    private void SetProductionConfigPath()
    {
        try
        {
            var myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var productionConfigRoot = Path.Combine(myDocuments, "My Games", "Kitten Space Agency");

            ThemeConfiguration.OverrideConfigDirectory = productionConfigRoot;

            ModLog.Log.Debug($"purrTTY GameMod: Production config path set to: {productionConfigRoot}");
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod: WARNING - Failed to set production config path: {ex.Message}");
            ModLog.Log.Debug("Configuration will use temporary directory and settings will not persist.");
        }
    }

    /// <summary>
    ///     Initializes the terminal controller and related components.
    ///     Guards against double initialization.
    /// </summary>
    private void InitializeTerminal()
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        ModLog.Log.Debug("purrTTY GameMod: Initializing terminal...");

        try
        {
            SetProductionConfigPath();
            LoadToggleHotkeyFromConfiguration();

            // Load fonts first
            PurrTTYFontManager.LoadFonts();

            // Game-console shells are launchable from the menus at any time.
            GhosttySessionManagerFactory.EnsureGameShellsDiscovered();

            // All shell detection (PATH probes, `wsl --list`, /etc/shells) runs
            // once here on a background thread; the menus only read the snapshot.
            ShellMenuCache.BeginDetection();

            var themeConfig = ThemeConfiguration.Load();
            var catalog = new ThemeCatalog();

            var controller = new GhosttyTerminalController(themeConfig, catalog);
            controller.KeyboardSuppression = ConsumeKeyboardInputSuppression;
            _controller = controller;

            // Pre-open the first window so its default shell is ready before the
            // terminal is first toggled visible.
            controller.OpenWindow();
            controller.IsVisible = _terminalVisible;

            ModLog.Log.Debug("purrTTY online");

            Toggle = ToggleTerminal;
            GetIsVisible = () => IsTerminalVisible;
            GetToggleHotkeyShortcut = () => _toggleHotkey.ToShortcutString();
            OpenToggleHotkeySettings = RequestOpenToggleHotkeyModal;
            OpenSaveThemeDialog = RequestOpenSaveThemeModal;
            OpenDeleteThemeDialog = RequestOpenDeleteThemeModal;
            MenuController = controller;

            _isInitialized = true;
            ModLog.Log.Debug($"purrTTY GameMod: Terminal initialized successfully. Press {_toggleHotkey.ToDisplayString()} to toggle.");
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod: Terminal initialization failed: {ex.Message}");
            DisposeResources();
            throw;
        }
    }

    /// <summary>
    ///     Toggles the terminal window visibility.
    /// </summary>
    private void ToggleTerminal()
    {
        SetTerminalVisibility(!IsTerminalVisible);
    }

    /// <summary>
    ///     Synchronizes terminal visibility across the game mod and the controller.
    /// </summary>
    /// <param name="isVisible">The desired visibility state.</param>
    private void SetTerminalVisibility(bool isVisible)
    {
        if (!_isInitialized || _isDisposed)
        {
            return;
        }

        _terminalVisible = isVisible;
        if (_controller != null)
        {
            _controller.IsVisible = isVisible;
        }

        ModLog.Log.Debug($"purrTTY GameMod: Terminal {(IsTerminalVisible ? "shown" : "hidden")}");
    }

    private void LoadToggleHotkeyFromConfiguration()
    {
        try
        {
            var config = ThemeConfiguration.Load();
            _toggleHotkey = ToggleHotkeyBinding.FromConfiguration(config);
            _draftToggleHotkey = _toggleHotkey;
            _lastCapturedToggleHotkey = _toggleHotkey;
        }
        catch (Exception ex)
        {
            _toggleHotkey = ToggleHotkeyBinding.Default;
            _draftToggleHotkey = _toggleHotkey;
            _lastCapturedToggleHotkey = _toggleHotkey;
            ModLog.Log.Debug($"purrTTY GameMod: Failed to load toggle hotkey from config, defaulting to F12. Error: {ex.Message}");
        }
    }

    private bool IsToggleHotkeyPressed()
    {
        return _toggleHotkey.MatchesPress(ImGui.GetIO());
    }

    private void RequestOpenSaveThemeModal()
    {
        if (!_isInitialized || _isDisposed || _controller == null)
        {
            return;
        }

        _themeNameInput.Clear();
        _saveThemeError = null;
        _saveThemeModalOpenRequested = true;
    }

    private bool RenderSaveThemeModal()
    {
        if (_saveThemeModalOpenRequested)
        {
            _saveThemeModalOpenRequested = false;
            ImGui.OpenPopup(SaveThemePopupId);
        }

        bool open = true;
        ImGui.SetNextWindowSize(new float2(560f, 0f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(SaveThemePopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return false;
        }

        ImGui.Text("Save the focused window's colors, font, and opacity settings as a theme.");
        ImGui.Spacing();

        ImGui.Text("Name");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }

        bool submitted = ImGui.InputText("##purrtty_theme_name", _themeNameInput, ImGuiInputTextFlags.EnterReturnsTrue);

        // BRUTAL's InputText only recomputes ImInputString.Length when it returns true. With
        // EnterReturnsTrue that's *only* on Enter, never on plain typing, so Length (and thus
        // ToString()/Value) would stay empty while the user types — leaving the Save button
        // permanently disabled. Re-evaluate the length from the buffer ourselves every frame.
        _themeNameInput.EvaluateLength();

        string name = _themeNameInput.ToString().Trim();
        if (name.Length > 0 && _controller?.Catalog.UserThemeExists(name) == true)
        {
            ImGui.TextColored(new float4(1f, 0.8f, 0.3f, 1f), $"A saved theme named '{name}' already exists and will be overwritten.");
        }

        if (_saveThemeError != null)
        {
            ImGui.TextColored(new float4(1f, 0.4f, 0.4f, 1f), _saveThemeError);
        }

        ImGui.Spacing();

        float availW = ImGui.GetContentRegionAvail().X;
        const float gap = 8f;
        float buttonWidth = (availW - gap) / 2f;
        bool canSave = name.Length > 0;

        if (!canSave)
        {
            ImGui.BeginDisabled();
        }

        bool saveClicked = ImGui.Button(" Save ##purrtty_save_theme", new float2(buttonWidth, 0f));

        if (!canSave)
        {
            ImGui.EndDisabled();
        }

        if ((saveClicked || submitted) && canSave)
        {
            try
            {
                if (_controller?.SaveFocusedWindowAsTheme(name) != null)
                {
                    ImGui.CloseCurrentPopup();
                }
                else
                {
                    _saveThemeError = "No terminal window to save settings from.";
                }
            }
            catch (Exception ex)
            {
                _saveThemeError = $"Save failed: {ex.Message}";
            }
        }

        ImGui.SameLine(0, gap);
        if (ImGui.Button(" Cancel ##purrtty_cancel_save_theme", new float2(buttonWidth, 0f)) || !open)
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
        return true;
    }

    private void RequestOpenDeleteThemeModal(string themeName)
    {
        if (!_isInitialized || _isDisposed || _controller == null || string.IsNullOrWhiteSpace(themeName))
        {
            return;
        }

        _deleteThemeName = themeName;
        _deleteThemeError = null;
        _deleteThemeModalOpenRequested = true;
    }

    private bool RenderDeleteThemeModal()
    {
        if (_deleteThemeModalOpenRequested)
        {
            _deleteThemeModalOpenRequested = false;
            ImGui.OpenPopup(DeleteThemePopupId);
        }

        bool open = true;
        ImGui.SetNextWindowSize(new float2(480f, 0f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(DeleteThemePopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return false;
        }

        ImGui.TextWrapped($"Delete the saved theme '{_deleteThemeName}'? This permanently removes its file and cannot be undone.");

        if (_deleteThemeError != null)
        {
            ImGui.TextColored(new float4(1f, 0.4f, 0.4f, 1f), _deleteThemeError);
        }

        ImGui.Spacing();

        float availW = ImGui.GetContentRegionAvail().X;
        const float gap = 8f;
        float buttonWidth = (availW - gap) / 2f;

        if (ImGui.Button(" Delete ##purrtty_confirm_delete_theme", new float2(buttonWidth, 0f)))
        {
            try
            {
                _controller?.Catalog.DeleteUserTheme(_deleteThemeName ?? string.Empty);
                ImGui.CloseCurrentPopup();
            }
            catch (Exception ex)
            {
                _deleteThemeError = $"Delete failed: {ex.Message}";
            }
        }

        ImGui.SameLine(0, gap);
        if (ImGui.Button(" Cancel ##purrtty_cancel_delete_theme", new float2(buttonWidth, 0f)) || !open)
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
        return true;
    }

    private void RequestOpenToggleHotkeyModal()
    {
        if (!_isInitialized || _isDisposed)
        {
            return;
        }

        _draftToggleHotkey = _toggleHotkey;
        _lastCapturedToggleHotkey = _toggleHotkey;
        _isCapturingToggleHotkey = false;
        _toggleHotkeyModalOpenRequested = true;
    }

    private bool RenderToggleHotkeyModal()
    {
        if (_toggleHotkeyModalOpenRequested)
        {
            _toggleHotkeyModalOpenRequested = false;
            ImGui.OpenPopup(ToggleHotkeyPopupId);
        }

        bool open = true;
        ImGui.SetNextWindowSize(new float2(760f, 0f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(ToggleHotkeyPopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return false;
        }

        if (_isCapturingToggleHotkey)
        {
            UpdateToggleHotkeyCapture();
            RenderCaptureHotkeyModalContent();
        }
        else
        {
            RenderStandardHotkeyModalContent();
        }

        RenderHotkeyModalSaveCancelButtons();

        if (!open)
        {
            _isCapturingToggleHotkey = false;
            _draftToggleHotkey = _toggleHotkey;
            _lastCapturedToggleHotkey = _toggleHotkey;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
        return true;
    }

    private void RenderStandardHotkeyModalContent()
    {
        RenderHotkeySummaryTable("Current Hot Key", GetCurrentHotkeyDisplay());

        ImGui.Spacing();

        float availW = ImGui.GetContentRegionAvail().X;
        const float gap = 8f;
        float buttonWidth = (availW - gap) / 2f;

        if (ImGui.Button(" Capture Now ##purrtty_capture_hotkey", new float2(buttonWidth, 0f)))
        {
            _isCapturingToggleHotkey = true;
            _lastCapturedToggleHotkey = null;
        }

        ImGui.SameLine(0, gap);
        if (ImGui.Button(" Reset ##purrtty_reset_hotkey", new float2(buttonWidth, 0f)))
        {
            _draftToggleHotkey = ToggleHotkeyBinding.Default;
            _lastCapturedToggleHotkey = ToggleHotkeyBinding.Default;
        }
    }

    private void RenderCaptureHotkeyModalContent()
    {
        ImGui.Text("Press desired hotkey now");
        RenderHotkeySummaryTable("Hot Key", GetCaptureHotkeyDisplay());

        ImGui.Spacing();

        float availW = ImGui.GetContentRegionAvail().X;
        const float gap = 8f;
        float buttonWidth = (availW - gap) / 2f;

        bool hasCapturedHotkey = _lastCapturedToggleHotkey.HasValue;

        if (!hasCapturedHotkey)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button(" Use This ##purrtty_use_captured_hotkey", new float2(buttonWidth, 0f)) &&
            _lastCapturedToggleHotkey is ToggleHotkeyBinding capturedHotkey)
        {
            _draftToggleHotkey = capturedHotkey;
            ApplyToggleHotkeySetting(_draftToggleHotkey);
            _isCapturingToggleHotkey = false;
        }

        if (!hasCapturedHotkey)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine(0, gap);
        if (ImGui.Button(" Cancel ##purrtty_cancel_capture_hotkey", new float2(buttonWidth, 0f)))
        {
            _isCapturingToggleHotkey = false;
        }
    }

    private void RenderHotkeyModalSaveCancelButtons()
    {
        ImGui.Spacing();

        float availW = ImGui.GetContentRegionAvail().X;
        const float gap = 8f;
        float buttonWidth = (availW - gap) / 2f;

        if (_isCapturingToggleHotkey)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button(" Save ##purrtty_save_hotkey", new float2(buttonWidth, 0f)))
        {
            ApplyToggleHotkeySetting(_draftToggleHotkey);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0, gap);
        if (ImGui.Button(" Cancel ##purrtty_cancel_hotkey", new float2(buttonWidth, 0f)))
        {
            _draftToggleHotkey = _toggleHotkey;
            _lastCapturedToggleHotkey = _toggleHotkey;
            _isCapturingToggleHotkey = false;
            ImGui.CloseCurrentPopup();
        }

        if (_isCapturingToggleHotkey)
        {
            ImGui.EndDisabled();
        }
    }

    private void RenderHotkeySummaryTable(string label, string value)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##purrtty_toggle_hotkey_summary", 2,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("##hotkey_label", ImGuiTableColumnFlags.WidthFixed, 260f);
            ImGui.TableSetupColumn("##hotkey_value", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(label);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(value);

            ImGui.EndTable();
        }

        ImGui.PopStyleVar();
    }

    private string GetCurrentHotkeyDisplay()
    {
        return _draftToggleHotkey.IsDefault
            ? $"{_draftToggleHotkey.ToDisplayString()} (default)"
            : _draftToggleHotkey.ToDisplayString();
    }

    private string GetCaptureHotkeyDisplay()
    {
        if (_lastCapturedToggleHotkey is ToggleHotkeyBinding capturedHotkey)
        {
            return capturedHotkey.ToDisplayString();
        }

        var io = ImGui.GetIO();
        var modifierParts = new List<string>(4);

        if (io.KeyCtrl)
        {
            modifierParts.Add("Ctrl");
        }

        if (io.KeyShift)
        {
            modifierParts.Add("Shift");
        }

        if (io.KeyAlt)
        {
            modifierParts.Add("Alt");
        }

        if (ImGuiHotkeyHelpers.GetSuperModifier(io))
        {
            modifierParts.Add("Super");
        }

        return modifierParts.Count == 0
            ? "Waiting for key press..."
            : $"{string.Join(" + ", modifierParts)} + ...";
    }

    private void UpdateToggleHotkeyCapture()
    {
        var io = ImGui.GetIO();
        bool super = ImGuiHotkeyHelpers.GetSuperModifier(io);

        foreach (ImGuiKey key in ToggleHotkeyBinding.CapturableKeys)
        {
            if (!ImGui.IsKeyPressed(key))
            {
                continue;
            }

            _lastCapturedToggleHotkey = new ToggleHotkeyBinding(
                key,
                io.KeyShift,
                io.KeyCtrl,
                io.KeyAlt,
                super);
            break;
        }
    }

    private void ApplyToggleHotkeySetting(ToggleHotkeyBinding binding)
    {
        _toggleHotkey = binding;
        _draftToggleHotkey = binding;

        try
        {
            // Use the controller's live configuration instance so a later display
            // settings save does not clobber the new hotkey with stale values.
            var config = _controller?.Configuration ?? ThemeConfiguration.Load();
            _toggleHotkey.WriteToConfiguration(config);
            config.Save();
            ModLog.Log.Debug($"purrTTY GameMod: Toggle hotkey updated to {_toggleHotkey.ToDisplayString()}");
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod: Failed to persist toggle hotkey: {ex.Message}");
        }
    }

    private bool ConsumeKeyboardInputSuppression()
    {
        if (!_suppressNextTerminalKeyboardInputFrame)
        {
            return false;
        }

        _suppressNextTerminalKeyboardInputFrame = false;
        return true;
    }

    /// <summary>
    ///     Gets a loaded font by name, or null if not found.
    /// </summary>
    public static ImFontPtr? GetFont(string fontName)
    {
        return PurrTTYFontManager.LoadedFonts.TryGetValue(fontName, out ImFontPtr font) ? font : null;
    }

    /// <summary>
    ///     Disposes all resources and cleans up.
    ///     Guards against double disposal.
    /// </summary>
    private void DisposeResources()
    {
        if (_isDisposed)
        {
            return;
        }

        ModLog.Log.Debug("purrTTY GameMod: Disposing resources...");

        try
        {
            // Dispose components (the controller disposes its windows, whose session
            // managers handle process cleanup)
            _controller?.Dispose();
            _controller = null;

            Toggle = null;
            GetIsVisible = null;
            GetToggleHotkeyShortcut = null;
            OpenToggleHotkeySettings = null;
            OpenSaveThemeDialog = null;
            OpenDeleteThemeDialog = null;
            MenuController = null;

            _isInitialized = false;
            _isDisposed = true;

            ModLog.Log.Debug("purrTTY GameMod: Resources disposed successfully");
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod: Error during resource disposal: {ex.Message}");
        }
    }
}
