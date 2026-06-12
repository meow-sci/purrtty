using System;
using System.IO;
using float2 = Brutal.Numerics.float2;
using purrTTY.Core.Terminal;
using purrTTY.Display.Theming;
using purrTTY.Logging;
using PurrTTY.Terminal.Rendering;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace purrTTY.Display.Configuration;

/// <summary>
/// Configuration class for persisting theme and display settings.
/// Handles serialization to/from TOML configuration files.
/// </summary>
public class ThemeConfiguration
{
    private static readonly TomlSerializerOptions TomlOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        DefaultIgnoreCondition = TomlIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// User-editable purrTTY settings persisted to the [settings] TOML table.
    /// </summary>
    [TomlPropertyName("settings")]
    public ThemeSettingsSection Settings { get; set; } = new();

    /// <summary>
    /// Internal UI state persisted to the [do-not-touch] TOML table.
    /// </summary>
    [TomlPropertyName("do-not-touch")]
    public ThemeUiStateSection DoNotTouch { get; set; } = new();

    /// <summary>
    /// Name of the currently selected theme.
    /// </summary>
    [TomlIgnore]
    public string? SelectedThemeName
    {
        get => Settings.SelectedThemeName;
        set => Settings.SelectedThemeName = value;
    }

    /// <summary>
    /// Background opacity setting for terminal background colors (0.0 to 1.0).
    /// </summary>
    [TomlIgnore]
    public float BackgroundOpacity
    {
        get => Settings.BackgroundOpacity;
        set => Settings.BackgroundOpacity = value;
    }

    /// <summary>
    /// Whether to hide the window border and menu bar when the mouse is not hovered over the window.
    /// Defaults to true for a cleaner look.
    /// </summary>
    [TomlIgnore]
    public bool HideUiWhenNotHovered
    {
        get => Settings.HideUiWhenNotHovered;
        set => Settings.HideUiWhenNotHovered = value;
    }

    /// <summary>
    /// Foreground opacity setting for terminal text colors (0.0 to 1.0).
    /// </summary>
    [TomlIgnore]
    public float ForegroundOpacity
    {
        get => Settings.ForegroundOpacity;
        set => Settings.ForegroundOpacity = value;
    }

    /// <summary>
    /// Cell background opacity setting for terminal cell background colors (0.0 to 1.0).
    /// </summary>
    [TomlIgnore]
    public float CellBackgroundOpacity
    {
        get => Settings.CellBackgroundOpacity;
        set => Settings.CellBackgroundOpacity = value;
    }

    /// <summary>
    /// Font family name for terminal text rendering.
    /// </summary>
    [TomlIgnore]
    public string? FontFamily
    {
        get => Settings.FontFamily;
        set => Settings.FontFamily = value;
    }

    /// <summary>
    /// Font size for terminal text rendering (4.0 to 72.0).
    /// </summary>
    [TomlIgnore]
    public float? FontSize
    {
        get => Settings.FontSize;
        set => Settings.FontSize = value;
    }

    /// <summary>Default cursor shape for new windows (Block/Bar/Underline).</summary>
    [TomlIgnore]
    public CursorShape CursorStyle
    {
        get => Settings.CursorStyle;
        set => Settings.CursorStyle = value;
    }

    /// <summary>Whether the default cursor blinks.</summary>
    [TomlIgnore]
    public bool CursorBlink
    {
        get => Settings.CursorBlink;
        set => Settings.CursorBlink = value;
    }

    /// <summary>Draw a window border while the terminal holds focus.</summary>
    [TomlIgnore]
    public bool BorderOnFocus
    {
        get => Settings.BorderOnFocus;
        set => Settings.BorderOnFocus = value;
    }

    /// <summary>Draw a window border while the mouse hovers the terminal.</summary>
    [TomlIgnore]
    public bool BorderOnHover
    {
        get => Settings.BorderOnHover;
        set => Settings.BorderOnHover = value;
    }

    /// <summary>Opacity of the focus/hover border (0.0 to 1.0).</summary>
    [TomlIgnore]
    public float BorderOpacity
    {
        get => Settings.BorderOpacity;
        set => Settings.BorderOpacity = value;
    }

    /// <summary>Lock mode: unfocused terminal windows are mouse click-through.</summary>
    [TomlIgnore]
    public bool LockMode
    {
        get => Settings.LockMode;
        set => Settings.LockMode = value;
    }

    /// <summary>Whether the lock-mode focus hot zone is shown.</summary>
    [TomlIgnore]
    public bool HotZoneEnabled
    {
        get => Settings.HotZoneEnabled;
        set => Settings.HotZoneEnabled = value;
    }

    /// <summary>Anchor corner/side for the focus hot zone.</summary>
    [TomlIgnore]
    public HotZonePlacement HotZonePlacement
    {
        get => Settings.HotZonePlacement;
        set => Settings.HotZonePlacement = value;
    }

    /// <summary>Focus hot zone width in pixels.</summary>
    [TomlIgnore]
    public float HotZoneWidth
    {
        get => Settings.HotZoneWidth;
        set => Settings.HotZoneWidth = value;
    }

    /// <summary>Focus hot zone height in pixels.</summary>
    [TomlIgnore]
    public float HotZoneHeight
    {
        get => Settings.HotZoneHeight;
        set => Settings.HotZoneHeight = value;
    }

    /// <summary>Focus hot zone fill color.</summary>
    [TomlIgnore]
    public RgbaColor HotZoneColor
    {
        get => Settings.HotZoneColor;
        set => Settings.HotZoneColor = value;
    }

    /// <summary>Focus hot zone idle opacity (0.0 to 1.0).</summary>
    [TomlIgnore]
    public float HotZoneOpacity
    {
        get => Settings.HotZoneOpacity;
        set => Settings.HotZoneOpacity = value;
    }

    /// <summary>Focus hot zone hovered opacity (0.0 to 1.0).</summary>
    [TomlIgnore]
    public float HotZoneHoverOpacity
    {
        get => Settings.HotZoneHoverOpacity;
        set => Settings.HotZoneHoverOpacity = value;
    }

    /// <summary>
    /// Last saved terminal window X position.
    /// </summary>
    [TomlIgnore]
    public float? TerminalWindowPosX
    {
        get => DoNotTouch.TerminalWindowPosX;
        set => DoNotTouch.TerminalWindowPosX = value;
    }

    /// <summary>
    /// Last saved terminal window Y position.
    /// </summary>
    [TomlIgnore]
    public float? TerminalWindowPosY
    {
        get => DoNotTouch.TerminalWindowPosY;
        set => DoNotTouch.TerminalWindowPosY = value;
    }

    /// <summary>
    /// Last saved terminal window width.
    /// </summary>
    [TomlIgnore]
    public float? TerminalWindowWidth
    {
        get => DoNotTouch.TerminalWindowWidth;
        set => DoNotTouch.TerminalWindowWidth = value;
    }

    /// <summary>
    /// Last saved terminal window height.
    /// </summary>
    [TomlIgnore]
    public float? TerminalWindowHeight
    {
        get => DoNotTouch.TerminalWindowHeight;
        set => DoNotTouch.TerminalWindowHeight = value;
    }

    /// <summary>
    /// Last saved terminal width in columns.
    /// </summary>
    [TomlIgnore]
    public int? TerminalColumns
    {
        get => DoNotTouch.TerminalColumns;
        set => DoNotTouch.TerminalColumns = value;
    }

    /// <summary>
    /// Last saved terminal height in rows.
    /// </summary>
    [TomlIgnore]
    public int? TerminalRows
    {
        get => DoNotTouch.TerminalRows;
        set => DoNotTouch.TerminalRows = value;
    }

    /// <summary>
    /// Default shell type for new terminal sessions.
    /// </summary>
    [TomlIgnore]
    public ShellType DefaultShellType
    {
        get => Settings.DefaultShellType;
        set => Settings.DefaultShellType = value;
    }

    /// <summary>
    /// Custom shell path when DefaultShellType is Custom.
    /// </summary>
    [TomlIgnore]
    public string? CustomShellPath
    {
        get => Settings.CustomShellPath;
        set => Settings.CustomShellPath = value;
    }

    /// <summary>
    /// Additional arguments for the default shell.
    /// </summary>
    [TomlIgnore]
    public List<string> DefaultShellArguments
    {
        get => Settings.DefaultShellArguments;
        set => Settings.DefaultShellArguments = value ?? new List<string>();
    }

    /// <summary>
    /// WSL distribution name when DefaultShellType is Wsl.
    /// </summary>
    [TomlIgnore]
    public string? WslDistribution
    {
        get => Settings.WslDistribution;
        set => Settings.WslDistribution = value;
    }

    /// <summary>
    /// Custom shell ID when DefaultShellType is CustomGame.
    /// </summary>
    [TomlIgnore]
    public string? DefaultCustomGameShellId
    {
        get => Settings.DefaultCustomGameShellId;
        set => Settings.DefaultCustomGameShellId = value;
    }

    /// <summary>
    /// Custom prompt string for the Game Console Shell.
    /// </summary>
    [TomlIgnore]
    public string GameShellPrompt
    {
        get => Settings.GameShellPrompt;
        set => Settings.GameShellPrompt = value;
    }

    /// <summary>
    /// Optional key name for the terminal visibility toggle hotkey.
    /// When unset, the default hotkey (F12) is used.
    /// </summary>
    [TomlIgnore]
    public string? ToggleHotkeyKey
    {
        get => Settings.ToggleHotkeyKey;
        set => Settings.ToggleHotkeyKey = value;
    }

    /// <summary>
    /// Shift modifier flag for the toggle hotkey.
    /// </summary>
    [TomlIgnore]
    public bool ToggleHotkeyShift
    {
        get => Settings.ToggleHotkeyShift;
        set => Settings.ToggleHotkeyShift = value;
    }

    /// <summary>
    /// Ctrl modifier flag for the toggle hotkey.
    /// </summary>
    [TomlIgnore]
    public bool ToggleHotkeyCtrl
    {
        get => Settings.ToggleHotkeyCtrl;
        set => Settings.ToggleHotkeyCtrl = value;
    }

    /// <summary>
    /// Alt modifier flag for the toggle hotkey.
    /// </summary>
    [TomlIgnore]
    public bool ToggleHotkeyAlt
    {
        get => Settings.ToggleHotkeyAlt;
        set => Settings.ToggleHotkeyAlt = value;
    }

    /// <summary>
    /// Super modifier flag for the toggle hotkey.
    /// </summary>
    [TomlIgnore]
    public bool ToggleHotkeySuper
    {
        get => Settings.ToggleHotkeySuper;
        set => Settings.ToggleHotkeySuper = value;
    }

    /// <summary>
    /// Attempts to retrieve the persisted terminal window position and size.
    /// </summary>
    /// <param name="position">The saved window position.</param>
    /// <param name="size">The saved window size.</param>
    /// <returns>True when a complete valid window state is available.</returns>
    public bool TryGetTerminalWindowState(out float2 position, out float2 size)
    {
        position = default;
        size = default;

        if (TerminalWindowPosX is null || TerminalWindowPosY is null || TerminalWindowWidth is null || TerminalWindowHeight is null)
        {
            return false;
        }

        if (TerminalWindowWidth <= 0.0f || TerminalWindowHeight <= 0.0f)
        {
            return false;
        }

        position = new float2(TerminalWindowPosX.Value, TerminalWindowPosY.Value);
        size = new float2(TerminalWindowWidth.Value, TerminalWindowHeight.Value);
        return true;
    }

    /// <summary>
    /// Attempts to retrieve the persisted terminal grid dimensions.
    /// </summary>
    /// <param name="columns">The saved terminal width in columns.</param>
    /// <param name="rows">The saved terminal height in rows.</param>
    /// <returns>True when valid grid dimensions are available.</returns>
    public bool TryGetTerminalGridDimensions(out int columns, out int rows)
    {
        columns = 0;
        rows = 0;

        if (TerminalColumns is null || TerminalRows is null)
        {
            return false;
        }

        if (TerminalColumns < 1 || TerminalColumns > 1000 || TerminalRows < 1 || TerminalRows > 1000)
        {
            return false;
        }

        columns = TerminalColumns.Value;
        rows = TerminalRows.Value;
        return true;
    }

    /// <summary>
    /// Updates the persisted terminal window position and size.
    /// </summary>
    /// <param name="position">The window position to store.</param>
    /// <param name="size">The window size to store.</param>
    public void SetTerminalWindowState(float2 position, float2 size)
    {
        TerminalWindowPosX = position.X;
        TerminalWindowPosY = position.Y;
        TerminalWindowWidth = size.X;
        TerminalWindowHeight = size.Y;
    }

    /// <summary>
    /// Updates the persisted terminal window position, size, and grid dimensions.
    /// </summary>
    /// <param name="position">The window position to store.</param>
    /// <param name="size">The window size to store.</param>
    /// <param name="columns">The terminal width in columns.</param>
    /// <param name="rows">The terminal height in rows.</param>
    public void SetTerminalWindowState(float2 position, float2 size, int columns, int rows)
    {
        SetTerminalWindowState(position, size);
        TerminalColumns = columns;
        TerminalRows = rows;
    }

    /// <summary>
    /// Synchronizes a window's live display settings into the in-memory
    /// configuration snapshot (the defaults applied to new windows). This keeps
    /// later saves for unrelated settings from discarding the current theme,
    /// font, opacity, cursor, border, or lock values. Taking the whole settings
    /// object (instead of one positional parameter per field) means a new
    /// display setting cannot silently miss — or transpose — its persistence.
    /// </summary>
    public void SyncRuntimeDisplaySettings(Ghostty.TerminalWindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!string.IsNullOrWhiteSpace(settings.ThemeName))
        {
            SelectedThemeName = settings.ThemeName;
        }

        if (!string.IsNullOrWhiteSpace(settings.FontFamily))
        {
            FontFamily = settings.FontFamily;
        }

        if (settings.FontSize > 0.0f)
        {
            FontSize = settings.FontSize;
        }

        BackgroundOpacity = settings.BackgroundOpacity;
        ForegroundOpacity = settings.ForegroundOpacity;
        CellBackgroundOpacity = settings.CellBackgroundOpacity;

        CursorStyle = settings.CursorStyle;
        CursorBlink = settings.CursorBlink;
        BorderOnFocus = settings.BorderOnFocus;
        BorderOnHover = settings.BorderOnHover;
        BorderOpacity = settings.BorderOpacity;
        LockMode = settings.LockMode;
        HotZoneEnabled = settings.HotZoneEnabled;
        HotZonePlacement = settings.HotZonePlacement;
        HotZoneWidth = settings.HotZoneWidth;
        HotZoneHeight = settings.HotZoneHeight;
        HotZoneColor = settings.HotZoneColor;
        HotZoneOpacity = settings.HotZoneOpacity;
        HotZoneHoverOpacity = settings.HotZoneHoverOpacity;
    }

    /// <summary>
    /// Creates ProcessLaunchOptions based on the current configuration.
    /// </summary>
    /// <returns>ProcessLaunchOptions configured according to current settings</returns>
    public ProcessLaunchOptions CreateLaunchOptions()
    {
        return DefaultShellType switch
        {
            ShellType.Wsl => string.IsNullOrEmpty(WslDistribution)
                ? ProcessLaunchOptions.CreateWsl()
                : ProcessLaunchOptions.CreateWsl(WslDistribution),
            ShellType.PowerShell => ProcessLaunchOptions.CreatePowerShell(),
            ShellType.PowerShellCore => ProcessLaunchOptions.CreatePowerShellCore(),
            ShellType.Cmd => ProcessLaunchOptions.CreateCmd(),
            ShellType.Custom when !string.IsNullOrEmpty(CustomShellPath) =>
                ProcessLaunchOptions.CreateCustom(CustomShellPath, DefaultShellArguments.ToArray()),
            ShellType.CustomGame => CreateGameShellLaunchOptions(),
            _ => ProcessLaunchOptions.CreateDefault()
        };
    }

    /// <summary>
    /// Game Console launch options with the configured prompt stamped into the
    /// shell environment — the shell layer reads it from there instead of
    /// depending on this configuration type (see WellKnownShellEnvironment).
    /// </summary>
    public ProcessLaunchOptions CreateGameShellLaunchOptions()
    {
        var options = ProcessLaunchOptions.CreateCustomGame(DefaultCustomGameShellId ?? "GameConsoleShell");
        options.EnvironmentVariables[WellKnownShellEnvironment.GameShellPrompt] = GameShellPrompt;
        return options;
    }


    /// <summary>
    /// Override for the configuration directory path.
    /// When null (default), uses a temporary directory for safety.
    /// Production code should explicitly set this to the desired persistent location.
    /// Tests can set this to custom temporary directories for isolation.
    /// </summary>
    public static string? OverrideConfigDirectory { get; set; }

    /// <summary>
    /// Load configuration from the default configuration file.
    /// </summary>
    /// <returns>Loaded configuration or default configuration if file doesn't exist</returns>
    public static ThemeConfiguration Load()
    {
        try
        {
            var configPath = GetConfigFilePath();

            if (!File.Exists(configPath))
            {
                return new ThemeConfiguration();
            }

            var tomlContent = File.ReadAllText(configPath);
            if (string.IsNullOrWhiteSpace(tomlContent))
            {
                return new ThemeConfiguration();
            }

            var tomlTable = TomlSerializer.Deserialize<TomlTable>(tomlContent, TomlOptions);
            if (tomlTable is null)
            {
                return new ThemeConfiguration();
            }

            ThemeConfiguration? config;
            if (IsStructuredFormat(tomlTable))
            {
                config = TomlSerializer.Deserialize<ThemeConfiguration>(tomlContent, TomlOptions);
                config?.Normalize();
            }
            else
            {
                var legacyConfig = TomlSerializer.Deserialize<LegacyThemeConfiguration>(tomlContent, TomlOptions);
                config = FromLegacyConfiguration(legacyConfig);
            }

            return config ?? new ThemeConfiguration();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is TomlException)
        {
            // Log error and return default configuration
            ModLog.Log.Debug($"Error loading theme configuration: {ex.Message}");
            return new ThemeConfiguration();
        }
    }

    /// <summary>
    /// Save configuration to the default configuration file.
    /// </summary>
    public void Save()
    {
        try
        {
            var configPath = GetConfigFilePath();
            var configDirectory = Path.GetDirectoryName(configPath);

            // Ensure directory exists
            if (!string.IsNullOrEmpty(configDirectory) && !Directory.Exists(configDirectory))
            {
                Directory.CreateDirectory(configDirectory);
            }

            Normalize();
            var tomlContent = TomlSerializer.Serialize(this, TomlOptions);
            AtomicFile.WriteAllText(configPath, tomlContent);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is TomlException)
        {
            ModLog.Log.Debug($"Error saving theme configuration: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the path to the configuration file.
    /// </summary>
    /// <returns>Full path to the configuration file</returns>
    public static string GetConfigFilePath()
    {
        string baseDirectory;

        if (!string.IsNullOrEmpty(OverrideConfigDirectory))
        {
            // Explicit override (production or test-specific temp)
            baseDirectory = OverrideConfigDirectory;
        }
        else
        {
            // DEFAULT: Temp directory (safe by default)
            baseDirectory = Path.Combine(Path.GetTempPath(), "purrTTY_config_default");
        }

        var configDirectory = Path.Combine(baseDirectory, ".purrTTY");
        return Path.Combine(configDirectory, "purrtty.toml");
    }

    private void Normalize()
    {
        Settings ??= new ThemeSettingsSection();
        DoNotTouch ??= new ThemeUiStateSection();
        Settings.DefaultShellArguments ??= new List<string>();
    }

    private static bool IsStructuredFormat(TomlTable tomlTable)
    {
        return tomlTable.TryGetValue("settings", out var settingsValue) && settingsValue is TomlTable ||
               tomlTable.TryGetValue("do-not-touch", out var doNotTouchValue) && doNotTouchValue is TomlTable;
    }

    private static ThemeConfiguration FromLegacyConfiguration(LegacyThemeConfiguration? legacy)
    {
        if (legacy is null)
        {
            return new ThemeConfiguration();
        }

        var config = new ThemeConfiguration
        {
            SelectedThemeName = legacy.SelectedThemeName,
            BackgroundOpacity = legacy.BackgroundOpacity,
            HideUiWhenNotHovered = legacy.HideUiWhenNotHovered,
            ForegroundOpacity = legacy.ForegroundOpacity,
            CellBackgroundOpacity = legacy.CellBackgroundOpacity,
            FontFamily = legacy.FontFamily,
            FontSize = legacy.FontSize,
            DefaultShellType = legacy.DefaultShellType,
            CustomShellPath = legacy.CustomShellPath,
            DefaultShellArguments = legacy.DefaultShellArguments ?? new List<string>(),
            WslDistribution = legacy.WslDistribution,
            DefaultCustomGameShellId = legacy.DefaultCustomGameShellId,
            GameShellPrompt = legacy.GameShellPrompt,
            ToggleHotkeyKey = legacy.ToggleHotkeyKey,
            ToggleHotkeyShift = legacy.ToggleHotkeyShift,
            ToggleHotkeyCtrl = legacy.ToggleHotkeyCtrl,
            ToggleHotkeyAlt = legacy.ToggleHotkeyAlt,
            ToggleHotkeySuper = legacy.ToggleHotkeySuper,
            TerminalWindowPosX = legacy.TerminalWindowPosX,
            TerminalWindowPosY = legacy.TerminalWindowPosY,
            TerminalWindowWidth = legacy.TerminalWindowWidth,
            TerminalWindowHeight = legacy.TerminalWindowHeight,
            TerminalColumns = legacy.TerminalColumns,
            TerminalRows = legacy.TerminalRows
        };

        config.Normalize();
        return config;
    }

    /// <summary>
    /// User-editable purrTTY settings persisted under [settings].
    /// </summary>
    public sealed class ThemeSettingsSection
    {
        public string? SelectedThemeName { get; set; }

        public float BackgroundOpacity { get; set; } = 1.0f;

        public bool HideUiWhenNotHovered { get; set; } = true;

        public float ForegroundOpacity { get; set; } = 1.0f;

        public float CellBackgroundOpacity { get; set; } = 1.0f;

        public string? FontFamily { get; set; }

        public float? FontSize { get; set; }

        // Cursor + focus-border + lock-mode display defaults for new windows.
        // Enum-typed values serialize as strings (same stability pattern as
        // DefaultShellType); unparsable values fall back to the default.
        [TomlIgnore]
        public CursorShape CursorStyle { get; set; } = CursorShape.Block;

        [TomlPropertyName("CursorStyle")]
        public string CursorStyleString
        {
            get => CursorStyle.ToString();
            set
            {
                // BlockHollow is the unfocused-window cursor, not a selectable style.
                CursorStyle = Enum.TryParse<CursorShape>(value, true, out var shape) && shape != CursorShape.BlockHollow
                    ? shape
                    : CursorShape.Block;
            }
        }

        public bool CursorBlink { get; set; } = true;

        public bool BorderOnFocus { get; set; }

        public bool BorderOnHover { get; set; }

        public float BorderOpacity { get; set; } = 0.5f;

        public bool LockMode { get; set; }

        public bool HotZoneEnabled { get; set; } = true;

        [TomlIgnore]
        public HotZonePlacement HotZonePlacement { get; set; } = HotZonePlacement.TopRight;

        [TomlPropertyName("HotZonePlacement")]
        public string HotZonePlacementString
        {
            get => HotZonePlacement.ToString();
            set
            {
                HotZonePlacement = Enum.TryParse<HotZonePlacement>(value, true, out var placement)
                    ? placement
                    : HotZonePlacement.TopRight;
            }
        }

        public float HotZoneWidth { get; set; } = 28f;

        public float HotZoneHeight { get; set; } = 28f;

        [TomlIgnore]
        public RgbaColor HotZoneColor { get; set; } = Ghostty.TerminalWindowSettings.DefaultHotZoneColor;

        [TomlPropertyName("HotZoneColor")]
        public string HotZoneColorHex
        {
            get => ThemeTomlFormat.ToHex(HotZoneColor);
            set
            {
                HotZoneColor = ThemeTomlFormat.TryParseHexColor(value, out var color)
                    ? color
                    : Ghostty.TerminalWindowSettings.DefaultHotZoneColor;
            }
        }

        public float HotZoneOpacity { get; set; } = 0.25f;

        public float HotZoneHoverOpacity { get; set; } = 0.6f;

        // Auto is the only default (and unparsable-value fallback) that works on
        // every platform: it resolves $SHELL/zsh/bash/sh on Unix and
        // WSL/PowerShell/cmd on Windows. A platform-specific default produces a
        // dead empty window elsewhere.
        [TomlIgnore]
        public ShellType DefaultShellType { get; set; } = ShellType.Auto;

        [TomlPropertyName("DefaultShellType")]
        public string DefaultShellTypeString
        {
            get => DefaultShellType.ToString();
            set
            {
                DefaultShellType = Enum.TryParse<ShellType>(value, true, out var shellType)
                    ? shellType
                    : ShellType.Auto;
            }
        }

        public string? CustomShellPath { get; set; }

        public List<string> DefaultShellArguments { get; set; } = new();

        public string? WslDistribution { get; set; }

        public string? DefaultCustomGameShellId { get; set; }

        public string GameShellPrompt { get; set; } = "ksa> ";

        public string? ToggleHotkeyKey { get; set; }

        public bool ToggleHotkeyShift { get; set; }

        public bool ToggleHotkeyCtrl { get; set; }

        public bool ToggleHotkeyAlt { get; set; }

        public bool ToggleHotkeySuper { get; set; }
    }

    /// <summary>
    /// Internal window state persisted under [do-not-touch].
    /// </summary>
    public sealed class ThemeUiStateSection
    {
        public float? TerminalWindowPosX { get; set; }

        public float? TerminalWindowPosY { get; set; }

        public float? TerminalWindowWidth { get; set; }

        public float? TerminalWindowHeight { get; set; }

        public int? TerminalColumns { get; set; }

        public int? TerminalRows { get; set; }
    }

    private sealed class LegacyThemeConfiguration
    {
        public string? SelectedThemeName { get; set; }

        public float BackgroundOpacity { get; set; } = 1.0f;

        public bool HideUiWhenNotHovered { get; set; } = true;

        public float ForegroundOpacity { get; set; } = 1.0f;

        public float CellBackgroundOpacity { get; set; } = 1.0f;

        public string? FontFamily { get; set; }

        public float? FontSize { get; set; }

        public float? TerminalWindowPosX { get; set; }

        public float? TerminalWindowPosY { get; set; }

        public float? TerminalWindowWidth { get; set; }

        public float? TerminalWindowHeight { get; set; }

        public int? TerminalColumns { get; set; }

        public int? TerminalRows { get; set; }

        // Auto is the only default (and unparsable-value fallback) that works on
        // every platform: it resolves $SHELL/zsh/bash/sh on Unix and
        // WSL/PowerShell/cmd on Windows. A platform-specific default produces a
        // dead empty window elsewhere.
        [TomlIgnore]
        public ShellType DefaultShellType { get; set; } = ShellType.Auto;

        [TomlPropertyName("DefaultShellType")]
        public string DefaultShellTypeString
        {
            get => DefaultShellType.ToString();
            set
            {
                DefaultShellType = Enum.TryParse<ShellType>(value, true, out var shellType)
                    ? shellType
                    : ShellType.Auto;
            }
        }

        public string? CustomShellPath { get; set; }

        public List<string> DefaultShellArguments { get; set; } = new();

        public string? WslDistribution { get; set; }

        public string? DefaultCustomGameShellId { get; set; }

        public string GameShellPrompt { get; set; } = "ksa> ";

        public string? ToggleHotkeyKey { get; set; }

        public bool ToggleHotkeyShift { get; set; }

        public bool ToggleHotkeyCtrl { get; set; }

        public bool ToggleHotkeyAlt { get; set; }

        public bool ToggleHotkeySuper { get; set; }
    }
}
