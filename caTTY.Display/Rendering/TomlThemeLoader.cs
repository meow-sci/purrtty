using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Brutal.Numerics;
using caTTY.Core.Types;
using Tomlyn;
using Tomlyn.Model;

namespace caTTY.Display.Rendering;

/// <summary>
/// Loads terminal themes from TOML configuration files.
/// Handles theme discovery, parsing, and validation.
/// </summary>
public static class TomlThemeLoader
{
    /// <summary>
    /// Load all themes from the TerminalThemes directory relative to the assembly location.
    /// </summary>
    /// <returns>List of successfully loaded themes</returns>
    public static List<TerminalTheme> LoadThemesFromDirectory()
    {
        var themesDirectory = GetThemesDirectory();
        return LoadThemesFromDirectory(themesDirectory);
    }

    /// <summary>
    /// Load all themes from the specified directory.
    /// </summary>
    /// <param name="themesDirectory">Directory containing TOML theme files</param>
    /// <returns>List of successfully loaded themes</returns>
    public static List<TerminalTheme> LoadThemesFromDirectory(string themesDirectory)
    {
        var themes = new List<TerminalTheme>();

        if (!Directory.Exists(themesDirectory))
        {
            return themes;
        }

        try
        {
            var tomlFiles = Directory.GetFiles(themesDirectory, "*.toml", SearchOption.TopDirectoryOnly);
            
            
            foreach (var filePath in tomlFiles)
            {
                var theme = LoadThemeFromFile(filePath);
                if (theme.HasValue)
                {
                    themes.Add(theme.Value);
                }
            }
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException || ex is UnauthorizedAccessException || ex is IOException)
        {
            // Log error and continue with empty theme list
            // In a real implementation, this would use a proper logging framework
            Console.WriteLine($"Error accessing themes directory '{themesDirectory}': {ex.Message}");
        }

        return themes;
    }

    /// <summary>
    /// Load a single theme from a TOML file.
    /// </summary>
    /// <param name="filePath">Path to the TOML theme file</param>
    /// <returns>Loaded theme or null if loading failed</returns>
    public static TerminalTheme? LoadThemeFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

  
            var tomlContent = File.ReadAllText(filePath);
            
            // Use TryToModel for graceful error handling with Tomlyn
            if (!Toml.TryToModel<TomlTable>(tomlContent, out var tomlTable, out var diagnostics))
            {
                foreach (var diagnostic in diagnostics)
                {
                    // Console.WriteLine($"TOML parsing error in {filePath}: {diagnostic}");
                }
                return null;
            }
            
            // Validate required structure
            if (!ValidateThemeStructure(tomlTable))
            {
                // Console.WriteLine($"Invalid theme structure in {filePath}");
                return null;
            }
            
            var colorPalette = ParseColorPaletteFromTomlTable(tomlTable);
            var themeName = GetThemeDisplayName(filePath);
            
            // Create cursor configuration with defaults
            var cursorConfig = new CursorConfig(CursorStyle.BlinkingBlock, true, 500);
            
            // Determine theme type based on background brightness
            var themeType = GetThemeType(colorPalette.Background);
            
            return new TerminalTheme(themeName, themeType, colorPalette, cursorConfig);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Console.WriteLine($"Error reading theme file '{filePath}': {ex.Message}");
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is FormatException)
        {
            Console.WriteLine($"Error parsing theme file '{filePath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Validate that TOML table contains all required sections.
    /// </summary>
    /// <param name="tomlTable">Parsed TOML table</param>
    /// <returns>True if all required sections are present</returns>
    private static bool ValidateThemeStructure(TomlTable tomlTable)
    {
        // Check for colors section
        if (!tomlTable.ContainsKey("colors") || !(tomlTable["colors"] is TomlTable colorsTable))
        {
            return false;
        }
        
        // Check for required color subsections
        var requiredColorSections = new[] { "normal", "bright", "primary", "cursor", "selection" };
        foreach (var section in requiredColorSections)
        {
            if (!colorsTable.ContainsKey(section) || !(colorsTable[section] is TomlTable))
            {
                // Console.WriteLine($"Missing required color section: colors.{section}");
                return false;
            }
        }
        
        return true;
    }

    /// <summary>
    /// Parse the color palette from TOML table.
    /// </summary>
    /// <param name="tomlTable">Parsed TOML table</param>
    /// <returns>Terminal color palette</returns>
    private static TerminalColorPalette ParseColorPaletteFromTomlTable(TomlTable tomlTable)
    {
        var colorsTable = (TomlTable)tomlTable["colors"];
        
        // Parse normal colors
        var normalTable = (TomlTable)colorsTable["normal"];
        var colorNames = new[] { "black", "red", "green", "yellow", "blue", "magenta", "cyan", "white" };
        
        var black = ParseHexColor((string)normalTable["black"]);
        var red = ParseHexColor((string)normalTable["red"]);
        var green = ParseHexColor((string)normalTable["green"]);
        var yellow = ParseHexColor((string)normalTable["yellow"]);
        var blue = ParseHexColor((string)normalTable["blue"]);
        var magenta = ParseHexColor((string)normalTable["magenta"]);
        var cyan = ParseHexColor((string)normalTable["cyan"]);
        var white = ParseHexColor((string)normalTable["white"]);

        // Parse bright colors
        var brightTable = (TomlTable)colorsTable["bright"];
        var brightBlack = ParseHexColor((string)brightTable["black"]);
        var brightRed = ParseHexColor((string)brightTable["red"]);
        var brightGreen = ParseHexColor((string)brightTable["green"]);
        var brightYellow = ParseHexColor((string)brightTable["yellow"]);
        var brightBlue = ParseHexColor((string)brightTable["blue"]);
        var brightMagenta = ParseHexColor((string)brightTable["magenta"]);
        var brightCyan = ParseHexColor((string)brightTable["cyan"]);
        var brightWhite = ParseHexColor((string)brightTable["white"]);

        // Parse primary colors
        var primaryTable = (TomlTable)colorsTable["primary"];
        var foreground = ParseHexColor((string)primaryTable["foreground"]);
        var background = ParseHexColor((string)primaryTable["background"]);

        // Parse cursor colors
        var cursorTable = (TomlTable)colorsTable["cursor"];
        var cursor = ParseHexColor((string)cursorTable["cursor"]);

        // Parse selection colors
        var selectionTable = (TomlTable)colorsTable["selection"];
        var selection = ParseHexColor((string)selectionTable["background"]);

        return new TerminalColorPalette(
            black, red, green, yellow, blue, magenta, cyan, white,
            brightBlack, brightRed, brightGreen, brightYellow, brightBlue, brightMagenta, brightCyan, brightWhite,
            foreground, background, cursor, selection
        );
    }

    /// <summary>
    /// Parse a hex color string to float4.
    /// </summary>
    /// <param name="hexColor">Hex color string (e.g., '#ff6188')</param>
    /// <returns>float4 color value</returns>
    /// <exception cref="ArgumentException">Thrown when hex color format is invalid</exception>
    public static float4 ParseHexColor(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor))
        {
            throw new ArgumentException("Hex color cannot be null or empty");
        }

        // Require # prefix
        if (!hexColor.StartsWith('#'))
        {
            throw new ArgumentException($"Invalid hex color format: '{hexColor}'. Expected format: #RRGGBB");
        }

        // Remove # prefix
        var cleanHex = hexColor.Substring(1);

        // Validate hex format
        if (cleanHex.Length != 6)
        {
            throw new ArgumentException($"Invalid hex color format: '{hexColor}'. Expected format: #RRGGBB");
        }

        // Validate hex characters
        if (!cleanHex.All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
        {
            throw new ArgumentException($"Invalid hex color format: '{hexColor}'. Contains invalid characters");
        }

        try
        {
            // Parse RGB components
            var r = int.Parse(cleanHex.Substring(0, 2), NumberStyles.HexNumber) / 255.0f;
            var g = int.Parse(cleanHex.Substring(2, 2), NumberStyles.HexNumber) / 255.0f;
            var b = int.Parse(cleanHex.Substring(4, 2), NumberStyles.HexNumber) / 255.0f;

            return new float4(r, g, b, 1.0f);
        }
        catch (Exception ex) when (ex is FormatException || ex is OverflowException)
        {
            throw new ArgumentException($"Failed to parse hex color '{hexColor}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Convert float4 color to hex string.
    /// </summary>
    /// <param name="color">float4 color value</param>
    /// <returns>Hex color string (e.g., '#ff6188')</returns>
    public static string ToHexColor(float4 color)
    {
        var r = (int)Math.Round(Math.Clamp(color.X, 0.0f, 1.0f) * 255.0f);
        var g = (int)Math.Round(Math.Clamp(color.Y, 0.0f, 1.0f) * 255.0f);
        var b = (int)Math.Round(Math.Clamp(color.Z, 0.0f, 1.0f) * 255.0f);

        return $"#{r:x2}{g:x2}{b:x2}";
    }

    /// <summary>
    /// Extract theme display name from file path.
    /// </summary>
    /// <param name="filePath">Path to theme file</param>
    /// <returns>Theme display name</returns>
    public static string GetThemeDisplayName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return string.IsNullOrWhiteSpace(fileName) ? "Unknown Theme" : fileName;
    }

    /// <summary>
    /// Get the TerminalThemes directory path relative to the assembly location.
    /// </summary>
    /// <returns>Path to TerminalThemes directory</returns>
    public static string GetThemesDirectory()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation) ?? Environment.CurrentDirectory;
        return Path.Combine(assemblyDirectory, "TerminalThemes");
    }

    /// <summary>
    /// Determine theme type based on background color brightness.
    /// </summary>
    /// <param name="backgroundColor">Background color</param>
    /// <returns>Theme type (Dark or Light)</returns>
    private static ThemeType GetThemeType(float4 backgroundColor)
    {
        // Calculate perceived brightness using standard luminance formula
        var brightness = 0.299f * backgroundColor.X + 0.587f * backgroundColor.Y + 0.114f * backgroundColor.Z;
        return brightness < 0.5f ? ThemeType.Dark : ThemeType.Light;
    }
}