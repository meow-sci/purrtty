using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using caTTY.Core.Types;
using caTTY.Display.Configuration;

namespace caTTY.Display.Rendering;

/// <summary>
/// Terminal color palette containing all standard ANSI colors and UI colors.
/// Based on TypeScript TerminalTheme implementation.
/// </summary>
public readonly struct TerminalColorPalette
{
    // Standard 16 ANSI colors
    public float4 Black { get; }
    public float4 Red { get; }
    public float4 Green { get; }
    public float4 Yellow { get; }
    public float4 Blue { get; }
    public float4 Magenta { get; }
    public float4 Cyan { get; }
    public float4 White { get; }
    public float4 BrightBlack { get; }
    public float4 BrightRed { get; }
    public float4 BrightGreen { get; }
    public float4 BrightYellow { get; }
    public float4 BrightBlue { get; }
    public float4 BrightMagenta { get; }
    public float4 BrightCyan { get; }
    public float4 BrightWhite { get; }

    // Terminal UI colors
    public float4 Foreground { get; }
    public float4 Background { get; }
    public float4 Cursor { get; }
    public float4 Selection { get; }

    public TerminalColorPalette(
        float4 black, float4 red, float4 green, float4 yellow,
        float4 blue, float4 magenta, float4 cyan, float4 white,
        float4 brightBlack, float4 brightRed, float4 brightGreen, float4 brightYellow,
        float4 brightBlue, float4 brightMagenta, float4 brightCyan, float4 brightWhite,
        float4 foreground, float4 background, float4 cursor, float4 selection)
    {
        Black = black;
        Red = red;
        Green = green;
        Yellow = yellow;
        Blue = blue;
        Magenta = magenta;
        Cyan = cyan;
        White = white;
        BrightBlack = brightBlack;
        BrightRed = brightRed;
        BrightGreen = brightGreen;
        BrightYellow = brightYellow;
        BrightBlue = brightBlue;
        BrightMagenta = brightMagenta;
        BrightCyan = brightCyan;
        BrightWhite = brightWhite;
        Foreground = foreground;
        Background = background;
        Cursor = cursor;
        Selection = selection;
    }
}

/// <summary>
/// Cursor configuration for terminal themes.
/// </summary>
public readonly struct CursorConfig
{
    /// <summary>
    /// Default cursor style for the theme.
    /// </summary>
    public CursorStyle DefaultStyle { get; }

    /// <summary>
    /// Whether cursor should blink by default.
    /// </summary>
    public bool DefaultBlink { get; }

    /// <summary>
    /// Cursor blink interval in milliseconds.
    /// </summary>
    public int BlinkIntervalMs { get; }

    public CursorConfig(CursorStyle defaultStyle, bool defaultBlink, int blinkIntervalMs = 500)
    {
        DefaultStyle = defaultStyle;
        DefaultBlink = defaultBlink;
        BlinkIntervalMs = blinkIntervalMs;
    }
}

/// <summary>
/// Complete terminal theme definition.
/// </summary>
public readonly struct TerminalTheme
{
    public string Name { get; }
    public ThemeType Type { get; }
    public TerminalColorPalette Colors { get; }
    public CursorConfig Cursor { get; }
    public ThemeSource Source { get; }
    public string? FilePath { get; }

    public TerminalTheme(string name, ThemeType type, TerminalColorPalette colors, CursorConfig cursor, ThemeSource source = ThemeSource.BuiltIn, string? filePath = null)
    {
        Name = name;
        Type = type;
        Colors = colors;
        Cursor = cursor;
        Source = source;
        FilePath = filePath;
    }
}

/// <summary>
/// Theme type enumeration.
/// </summary>
public enum ThemeType
{
    Dark,
    Light
}

/// <summary>
/// Theme source enumeration indicating where the theme was loaded from.
/// </summary>
public enum ThemeSource
{
    BuiltIn,
    TomlFile
}

/// <summary>
/// Event arguments for theme change notifications.
/// </summary>
public class ThemeChangedEventArgs : EventArgs
{
    public TerminalTheme PreviousTheme { get; }
    public TerminalTheme NewTheme { get; }

    public ThemeChangedEventArgs(TerminalTheme previousTheme, TerminalTheme newTheme)
    {
        PreviousTheme = previousTheme;
        NewTheme = newTheme;
    }
}

/// <summary>
/// Theme manager for handling terminal color themes.
/// Provides default themes, TOML theme loading, and color resolution.
/// </summary>
public static class ThemeManager
{
    private static readonly object _lock = new object();
    private static List<TerminalTheme> _availableThemes = new List<TerminalTheme>();
    private static TerminalTheme _currentTheme;
    private static bool _initialized = false;

    /// <summary>
    ///     Global version counter for theme changes.
    ///     Used for render cache invalidation.
    /// </summary>
    public static int Version { get; private set; } = 1;

    /// <summary>
    /// Default theme using Adventure.toml color values as the baseline.
    /// This serves as the fallback when no TOML themes are available.
    /// </summary>
    public static readonly TerminalTheme DefaultTheme = new(
        "Default",
        ThemeType.Dark,
        new TerminalColorPalette(
            // Standard ANSI colors from Adventure.toml
            black: TomlThemeLoader.ParseHexColor("#040404"),
            red: TomlThemeLoader.ParseHexColor("#d84a33"),
            green: TomlThemeLoader.ParseHexColor("#5da602"),
            yellow: TomlThemeLoader.ParseHexColor("#eebb6e"),
            blue: TomlThemeLoader.ParseHexColor("#417ab3"),
            magenta: TomlThemeLoader.ParseHexColor("#e5c499"),
            cyan: TomlThemeLoader.ParseHexColor("#bdcfe5"),
            white: TomlThemeLoader.ParseHexColor("#dbded8"),

            // Bright ANSI colors from Adventure.toml
            brightBlack: TomlThemeLoader.ParseHexColor("#685656"),
            brightRed: TomlThemeLoader.ParseHexColor("#d76b42"),
            brightGreen: TomlThemeLoader.ParseHexColor("#99b52c"),
            brightYellow: TomlThemeLoader.ParseHexColor("#ffb670"),
            brightBlue: TomlThemeLoader.ParseHexColor("#97d7ef"),
            brightMagenta: TomlThemeLoader.ParseHexColor("#aa7900"),
            brightCyan: TomlThemeLoader.ParseHexColor("#bdcfe5"),
            brightWhite: TomlThemeLoader.ParseHexColor("#e4d5c7"),

            // Terminal UI colors from Adventure.toml
            foreground: TomlThemeLoader.ParseHexColor("#feffff"),
            background: TomlThemeLoader.ParseHexColor("#040404"),
            cursor: TomlThemeLoader.ParseHexColor("#feffff"),
            selection: TomlThemeLoader.ParseHexColor("#606060")
        ),
        new CursorConfig(CursorStyle.BlinkingBlock, true, 500),
        ThemeSource.BuiltIn
    );

    /// <summary>
    /// Default light theme for terminals.
    /// </summary>
    public static readonly TerminalTheme DefaultLightTheme = new(
        "Default Light",
        ThemeType.Light,
        new TerminalColorPalette(
            // Standard ANSI colors (darker for light background)
            black: new float4(0.0f, 0.0f, 0.0f, 1.0f),
            red: new float4(0.8f, 0.0f, 0.0f, 1.0f),
            green: new float4(0.0f, 0.6f, 0.0f, 1.0f),
            yellow: new float4(0.8f, 0.6f, 0.0f, 1.0f),
            blue: new float4(0.0f, 0.0f, 0.8f, 1.0f),
            magenta: new float4(0.8f, 0.0f, 0.8f, 1.0f),
            cyan: new float4(0.0f, 0.6f, 0.6f, 1.0f),
            white: new float4(0.8f, 0.8f, 0.8f, 1.0f),

            // Bright ANSI colors
            brightBlack: new float4(0.4f, 0.4f, 0.4f, 1.0f),
            brightRed: new float4(1.0f, 0.2f, 0.2f, 1.0f),
            brightGreen: new float4(0.2f, 0.8f, 0.2f, 1.0f),
            brightYellow: new float4(1.0f, 0.8f, 0.2f, 1.0f),
            brightBlue: new float4(0.2f, 0.2f, 1.0f, 1.0f),
            brightMagenta: new float4(1.0f, 0.2f, 1.0f, 1.0f),
            brightCyan: new float4(0.2f, 0.8f, 0.8f, 1.0f),
            brightWhite: new float4(1.0f, 1.0f, 1.0f, 1.0f),

            // Terminal UI colors (inverted for light theme)
            foreground: new float4(0.0f, 0.0f, 0.0f, 1.0f),
            background: new float4(1.0f, 1.0f, 1.0f, 1.0f),
            cursor: new float4(0.0f, 0.0f, 0.0f, 1.0f),
            selection: new float4(0.8f, 0.8f, 0.8f, 1.0f)
        ),
        new CursorConfig(CursorStyle.BlinkingBlock, true, 500),
        ThemeSource.BuiltIn
    );

    /// <summary>
    /// Current active theme. Defaults to the default theme.
    /// </summary>
    public static TerminalTheme CurrentTheme
    {
        get
        {
            lock (_lock)
            {
                if (!_initialized)
                {
                    InitializeThemes();
                }
                return _currentTheme;
            }
        }
        private set => _currentTheme = value;
    }

    /// <summary>
    /// List of all available themes (built-in and TOML-loaded).
    /// </summary>
    public static IReadOnlyList<TerminalTheme> AvailableThemes
    {
        get
        {
            lock (_lock)
            {
                if (!_initialized)
                {
                    InitializeThemes();
                }
                return _availableThemes.AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Event fired when the current theme changes.
    /// </summary>
    public static event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    /// <summary>
    /// Initialize the theme system by loading TOML themes and setting up defaults.
    /// </summary>
    public static void InitializeThemes()
    {
        lock (_lock)
        {
            if (_initialized)
                return;

            _availableThemes.Clear();

            // Always add built-in themes first
            _availableThemes.Add(DefaultTheme);
            _availableThemes.Add(DefaultLightTheme);

            // Load TOML themes from the TerminalThemes directory
            try
            {
                var tomlThemes = TomlThemeLoader.LoadThemesFromDirectory();
                Console.WriteLine($"THEMES LOADED: {tomlThemes.ToArray()}");
                _availableThemes.AddRange(tomlThemes);
            }
            catch (Exception ex)
            {
                // Log error but continue with built-in themes
                Console.WriteLine($"Error loading TOML themes: {ex.Message}");
            }

            // Set initial theme - try to load saved preference, otherwise use default
            var savedThemeName = LoadThemePreference();
            if (!string.IsNullOrEmpty(savedThemeName))
            {
                var savedTheme = _availableThemes.FirstOrDefault(t => t.Name == savedThemeName);
                _currentTheme = savedTheme.Name != null ? savedTheme : DefaultTheme;
            }
            else
            {
                _currentTheme = DefaultTheme;
            }

            _initialized = true;
        }
    }

    /// <summary>
    /// Refresh the list of available themes by reloading from the filesystem.
    /// </summary>
    public static void RefreshAvailableThemes()
    {
        lock (_lock)
        {
            var currentThemeName = _currentTheme.Name;

            _availableThemes.Clear();

            // Always add built-in themes first
            _availableThemes.Add(DefaultTheme);
            _availableThemes.Add(DefaultLightTheme);

            // Reload TOML themes
            try
            {
                var tomlThemes = TomlThemeLoader.LoadThemesFromDirectory();
                _availableThemes.AddRange(tomlThemes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing TOML themes: {ex.Message}");
            }

            // Try to maintain current theme if it still exists
            var existingTheme = _availableThemes.FirstOrDefault(t => t.Name == currentThemeName);
            if (existingTheme.Name != null)
            {
                _currentTheme = existingTheme;
            }
            else
            {
                // Fall back to default theme if current theme no longer exists
                var previousTheme = _currentTheme;
                _currentTheme = DefaultTheme;
                ThemeChanged?.Invoke(null, new ThemeChangedEventArgs(previousTheme, _currentTheme));
            }

            Version++;
        }
    }

    /// <summary>
    /// Apply a theme by name.
    /// </summary>
    /// <param name="themeName">Name of the theme to apply</param>
    /// <returns>True if theme was found and applied, false otherwise</returns>
    public static bool ApplyTheme(string themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName))
            return false;

        lock (_lock)
        {
            if (!_initialized)
            {
                InitializeThemes();
            }

            var theme = _availableThemes.FirstOrDefault(t => t.Name == themeName);
            if (theme.Name == null)
                return false;

            var previousTheme = _currentTheme;
            _currentTheme = theme;

            // Save theme preference
            SaveThemePreference(themeName);

            // Notify listeners of theme change
            ThemeChanged?.Invoke(null, new ThemeChangedEventArgs(previousTheme, _currentTheme));

            Version++;

            return true;
        }
    }

    /// <summary>
    /// Apply a theme directly.
    /// </summary>
    /// <param name="theme">The theme to apply</param>
    public static void ApplyTheme(TerminalTheme theme)
    {
        lock (_lock)
        {
            var previousTheme = _currentTheme;
            _currentTheme = theme;

            // Save theme preference
            SaveThemePreference(theme.Name);

            // Notify listeners of theme change
            ThemeChanged?.Invoke(null, new ThemeChangedEventArgs(previousTheme, _currentTheme));

            Version++;
        }
    }

    /// <summary>
    /// Save the current theme preference to persistent storage.
    /// </summary>
    /// <param name="themeName">Name of the theme to save as preference</param>
    public static void SaveThemePreference(string themeName)
    {
        try
        {
            var config = ThemeConfiguration.Load();
            config.SelectedThemeName = themeName;
            config.Save();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving theme preference: {ex.Message}");
        }
    }

    /// <summary>
    /// Load the saved theme preference from persistent storage.
    /// </summary>
    /// <returns>Saved theme name or null if no preference is saved</returns>
    public static string? LoadThemePreference()
    {
        try
        {
            var config = ThemeConfiguration.Load();
            return config.SelectedThemeName;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading theme preference: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolve a color by ANSI color code using the current theme.
    /// </summary>
    /// <param name="colorCode">ANSI color code (0-15 for standard colors)</param>
    /// <returns>float4 color value</returns>
    public static float4 ResolveThemeColor(int colorCode)
    {
        var palette = CurrentTheme.Colors;

        return colorCode switch
        {
            0 => palette.Black,
            1 => palette.Red,
            2 => palette.Green,
            3 => palette.Yellow,
            4 => palette.Blue,
            5 => palette.Magenta,
            6 => palette.Cyan,
            7 => palette.White,
            8 => palette.BrightBlack,
            9 => palette.BrightRed,
            10 => palette.BrightGreen,
            11 => palette.BrightYellow,
            12 => palette.BrightBlue,
            13 => palette.BrightMagenta,
            14 => palette.BrightCyan,
            15 => palette.BrightWhite,
            _ => palette.Foreground
        };
    }

    /// <summary>
    /// Get the default foreground color from the current theme.
    /// </summary>
    public static float4 GetDefaultForeground() => CurrentTheme.Colors.Foreground;

    /// <summary>
    /// Get the default background color from the current theme.
    /// </summary>
    public static float4 GetDefaultBackground() => CurrentTheme.Colors.Background;

    /// <summary>
    /// Get the cursor color from the current theme.
    /// </summary>
    public static float4 GetCursorColor() => CurrentTheme.Colors.Cursor;

    /// <summary>
    /// Get the selection color from the current theme.
    /// </summary>
    public static float4 GetSelectionColor() => CurrentTheme.Colors.Selection;

    /// <summary>
    /// Get the default cursor style from the current theme.
    /// </summary>
    public static CursorStyle GetDefaultCursorStyle() => CurrentTheme.Cursor.DefaultStyle;

    /// <summary>
    /// Get the default cursor blink setting from the current theme.
    /// </summary>
    public static bool GetDefaultCursorBlink() => CurrentTheme.Cursor.DefaultBlink;

    /// <summary>
    /// Get the cursor blink interval from the current theme.
    /// </summary>
    public static int GetCursorBlinkInterval() => CurrentTheme.Cursor.BlinkIntervalMs;
}
