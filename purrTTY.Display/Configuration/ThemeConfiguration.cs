using System;
using System.IO;
using float2 = Brutal.Numerics.float2;
using purrTTY.Core.Terminal;
using purrTTY.Logging;
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
    /// String representation of DefaultShellType for TOML serialization.
    /// This ensures stability across enum changes.
    /// </summary>
    [TomlIgnore]
    public string DefaultShellTypeString
    {
        get => Settings.DefaultShellTypeString;
        set => Settings.DefaultShellTypeString = value;
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
    /// Attempts to retrieve the persisted terminal window position, size, and grid dimensions.
    /// </summary>
    /// <param name="position">The saved window position.</param>
    /// <param name="size">The saved window size.</param>
    /// <param name="columns">The saved terminal width in columns.</param>
    /// <param name="rows">The saved terminal height in rows.</param>
    /// <returns>True when a complete valid window and grid state is available.</returns>
    public bool TryGetTerminalWindowState(out float2 position, out float2 size, out int columns, out int rows)
    {
        position = default;
        size = default;
        columns = 0;
        rows = 0;

        if (!TryGetTerminalWindowState(out position, out size) || !TryGetTerminalGridDimensions(out columns, out rows))
        {
            position = default;
            size = default;
            columns = 0;
            rows = 0;
            return false;
        }

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
            ShellType.CustomGame =>
                ProcessLaunchOptions.CreateCustomGame(DefaultCustomGameShellId ?? "GameConsoleShell"),
            _ => ProcessLaunchOptions.CreateDefault()
        };
    }

    /// <summary>
    /// Gets a display name for the current shell configuration.
    /// </summary>
    /// <returns>Human-readable shell configuration name</returns>
    public string GetShellDisplayName()
    {
        return DefaultShellType switch
        {
            ShellType.Wsl => string.IsNullOrEmpty(WslDistribution)
                ? "WSL2 (Default)"
                : $"WSL2 ({WslDistribution})",
            ShellType.PowerShell => "Windows PowerShell",
            ShellType.PowerShellCore => "PowerShell Core",
            ShellType.Cmd => "Command Prompt",
            ShellType.Custom when !string.IsNullOrEmpty(CustomShellPath) =>
                $"Custom ({Path.GetFileName(CustomShellPath)})",
            ShellType.CustomGame when !string.IsNullOrEmpty(DefaultCustomGameShellId) =>
                $"Game Console",
            _ => "Auto-detect"
        };
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
            File.WriteAllText(configPath, tomlContent);
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

        [TomlIgnore]
        public ShellType DefaultShellType { get; set; } = ShellType.PowerShell;

        [TomlPropertyName("DefaultShellType")]
        public string DefaultShellTypeString
        {
            get => DefaultShellType.ToString();
            set
            {
                if (Enum.TryParse<ShellType>(value, true, out var shellType))
                {
                    DefaultShellType = shellType;
                }
                else
                {
                    DefaultShellType = ShellType.Wsl;
                }
            }
        }

        public string? CustomShellPath { get; set; }

        public List<string> DefaultShellArguments { get; set; } = new();

        public string? WslDistribution { get; set; }

        public string? DefaultCustomGameShellId { get; set; }

        public string GameShellPrompt { get; set; } = "ksa> ";
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

        [TomlIgnore]
        public ShellType DefaultShellType { get; set; } = ShellType.PowerShell;

        [TomlPropertyName("DefaultShellType")]
        public string DefaultShellTypeString
        {
            get => DefaultShellType.ToString();
            set
            {
                if (Enum.TryParse<ShellType>(value, true, out var shellType))
                {
                    DefaultShellType = shellType;
                }
                else
                {
                    DefaultShellType = ShellType.Wsl;
                }
            }
        }

        public string? CustomShellPath { get; set; }

        public List<string> DefaultShellArguments { get; set; } = new();

        public string? WslDistribution { get; set; }

        public string? DefaultCustomGameShellId { get; set; }

        public string GameShellPrompt { get; set; } = "ksa> ";
    }
}
