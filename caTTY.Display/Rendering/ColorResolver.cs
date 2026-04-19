using System;
using caTTY.Core.Types;
using Brutal.Numerics;

namespace caTTY.Display.Rendering;

/// <summary>
/// Color resolver for different SGR color types.
/// Handles ANSI colors, 256-color palette, and RGB colors.
/// Based on TypeScript ColorResolver implementation.
/// </summary>
public class ColorResolver
{
    private readonly Performance.PerformanceStopwatch _perfWatch;

    /// <summary>
    /// Initializes a new instance of the ColorResolver class.
    /// </summary>
    /// <param name="perfWatch">Performance stopwatch for timing measurements</param>
    public ColorResolver(Performance.PerformanceStopwatch perfWatch)
    {
        _perfWatch = perfWatch ?? throw new ArgumentNullException(nameof(perfWatch));
    }

    // Note: Hardcoded colors removed - now using ThemeManager for dynamic theme support

    /// <summary>
    /// Resolve SGR color type to ImGui float4 color value.
    /// </summary>
    /// <param name="color">SGR color specification</param>
    /// <param name="isBackground">Whether this is a background color (affects defaults)</param>
    /// <returns>ImGui float4 color value</returns>
    public float4 Resolve(caTTY.Core.Types.Color? color, bool isBackground = false)
    {
//        _perfWatch.Start("ColorResolver.Resolve");
        try
        {
            if (!color.HasValue)
            {
//                _perfWatch.Start("ColorResolver.Resolve.DefaultColor");
                var result = isBackground ? ThemeManager.GetDefaultBackground() : ThemeManager.GetDefaultForeground();
//                _perfWatch.Stop("ColorResolver.Resolve.DefaultColor");
                return result;
            }

            float4 resolved;
            switch (color.Value.Type)
            {
                case ColorType.Named:
//                    _perfWatch.Start("ColorResolver.Resolve.Named");
                    resolved = ResolveNamedColor(color.Value.NamedColor);
//                    _perfWatch.Stop("ColorResolver.Resolve.Named");
                    return resolved;
                    
                case ColorType.Indexed:
//                    _perfWatch.Start("ColorResolver.Resolve.Indexed");
                    resolved = ResolveIndexedColor(color.Value.Index);
//                    _perfWatch.Stop("ColorResolver.Resolve.Indexed");
                    return resolved;
                    
                case ColorType.Rgb:
//                    _perfWatch.Start("ColorResolver.Resolve.Rgb");
                    resolved = ResolveRgbColor(color.Value.Red, color.Value.Green, color.Value.Blue);
//                    _perfWatch.Stop("ColorResolver.Resolve.Rgb");
                    return resolved;
                    
                default:
//                    _perfWatch.Start("ColorResolver.Resolve.DefaultColor");
                    resolved = isBackground ? ThemeManager.GetDefaultBackground() : ThemeManager.GetDefaultForeground();
//                    _perfWatch.Stop("ColorResolver.Resolve.DefaultColor");
                    return resolved;
            }
        }
        finally
        {
//            _perfWatch.Stop("ColorResolver.Resolve");
        }
    }

    /// <summary>
    /// Resolve named ANSI color to float4 color using current theme.
    /// </summary>
    private float4 ResolveNamedColor(NamedColor namedColor)
    {
//        _perfWatch.Start("ColorResolver.ResolveNamedColor.ThemeLookup");
        var result = namedColor switch
        {
            NamedColor.Black => ThemeManager.ResolveThemeColor(0),
            NamedColor.Red => ThemeManager.ResolveThemeColor(1),
            NamedColor.Green => ThemeManager.ResolveThemeColor(2),
            NamedColor.Yellow => ThemeManager.ResolveThemeColor(3),
            NamedColor.Blue => ThemeManager.ResolveThemeColor(4),
            NamedColor.Magenta => ThemeManager.ResolveThemeColor(5),
            NamedColor.Cyan => ThemeManager.ResolveThemeColor(6),
            NamedColor.White => ThemeManager.ResolveThemeColor(7),
            NamedColor.BrightBlack => ThemeManager.ResolveThemeColor(8),
            NamedColor.BrightRed => ThemeManager.ResolveThemeColor(9),
            NamedColor.BrightGreen => ThemeManager.ResolveThemeColor(10),
            NamedColor.BrightYellow => ThemeManager.ResolveThemeColor(11),
            NamedColor.BrightBlue => ThemeManager.ResolveThemeColor(12),
            NamedColor.BrightMagenta => ThemeManager.ResolveThemeColor(13),
            NamedColor.BrightCyan => ThemeManager.ResolveThemeColor(14),
            NamedColor.BrightWhite => ThemeManager.ResolveThemeColor(15),
            _ => ThemeManager.GetDefaultForeground()
        };
//        _perfWatch.Stop("ColorResolver.ResolveNamedColor.ThemeLookup");
        return result;
    }

    /// <summary>
    /// Resolve indexed color (256-color palette) to float4 color using current theme for ANSI colors.
    /// </summary>
    private float4 ResolveIndexedColor(byte index)
    {
        // Standard 16 colors (0-15) - use theme colors
        if (index <= 15)
        {
//            _perfWatch.Start("ColorResolver.ResolveIndexedColor.Theme");
            var result = ThemeManager.ResolveThemeColor(index);
//            _perfWatch.Stop("ColorResolver.ResolveIndexedColor.Theme");
            return result;
        }

        // 216 color cube (16-231)
        if (index >= 16 && index <= 231)
        {
//            _perfWatch.Start("ColorResolver.ResolveIndexedColor.Cube");
            var result = GetCubeColor(index - 16);
//            _perfWatch.Stop("ColorResolver.ResolveIndexedColor.Cube");
            return result;
        }

        // Grayscale ramp (232-255)
        if (index >= 232 && index <= 255)
        {
//            _perfWatch.Start("ColorResolver.ResolveIndexedColor.Grayscale");
            var result = GetGrayscaleColor(index - 232);
//            _perfWatch.Stop("ColorResolver.ResolveIndexedColor.Grayscale");
            return result;
        }

        // Invalid index, return default
        return ThemeManager.GetDefaultForeground();
    }

    /// <summary>
    /// Generate color from 6x6x6 color cube.
    /// </summary>
    /// <param name="cubeIndex">Index in the color cube (0-215)</param>
    /// <returns>float4 color</returns>
    private float4 GetCubeColor(int cubeIndex)
    {
        int r = cubeIndex / 36;
        int g = (cubeIndex % 36) / 6;
        int b = cubeIndex % 6;

        static float ToColorValue(int n) => n == 0 ? 0.0f : (55 + n * 40) / 255.0f;

        return new float4(ToColorValue(r), ToColorValue(g), ToColorValue(b), 1.0f);
    }

    /// <summary>
    /// Generate grayscale color.
    /// </summary>
    /// <param name="grayIndex">Index in grayscale ramp (0-23)</param>
    /// <returns>float4 color</returns>
    private float4 GetGrayscaleColor(int grayIndex)
    {
        float gray = (8 + grayIndex * 10) / 255.0f;
        return new float4(gray, gray, gray, 1.0f);
    }

    /// <summary>
    /// Resolve RGB color to float4 color.
    /// </summary>
    private float4 ResolveRgbColor(byte red, byte green, byte blue)
    {
        return new float4(red / 255.0f, green / 255.0f, blue / 255.0f, 1.0f);
    }

    /// <summary>
    /// Convert ANSI color code to Color type.
    /// </summary>
    /// <param name="colorCode">ANSI color code (30-37, 40-47, 90-97, 100-107)</param>
    /// <returns>Color or null if invalid</returns>
    public static caTTY.Core.Types.Color? AnsiCodeToColor(int colorCode)
    {
        // Standard foreground colors (30-37)
        if (colorCode >= 30 && colorCode <= 37)
        {
            var namedColors = new[]
            {
                NamedColor.Black, NamedColor.Red, NamedColor.Green, NamedColor.Yellow,
                NamedColor.Blue, NamedColor.Magenta, NamedColor.Cyan, NamedColor.White
            };
            return new caTTY.Core.Types.Color(namedColors[colorCode - 30]);
        }

        // Bright foreground colors (90-97)
        if (colorCode >= 90 && colorCode <= 97)
        {
            var namedColors = new[]
            {
                NamedColor.BrightBlack, NamedColor.BrightRed, NamedColor.BrightGreen, NamedColor.BrightYellow,
                NamedColor.BrightBlue, NamedColor.BrightMagenta, NamedColor.BrightCyan, NamedColor.BrightWhite
            };
            return new caTTY.Core.Types.Color(namedColors[colorCode - 90]);
        }

        // Standard background colors (40-47)
        if (colorCode >= 40 && colorCode <= 47)
        {
            var namedColors = new[]
            {
                NamedColor.Black, NamedColor.Red, NamedColor.Green, NamedColor.Yellow,
                NamedColor.Blue, NamedColor.Magenta, NamedColor.Cyan, NamedColor.White
            };
            return new caTTY.Core.Types.Color(namedColors[colorCode - 40]);
        }

        // Bright background colors (100-107)
        if (colorCode >= 100 && colorCode <= 107)
        {
            var namedColors = new[]
            {
                NamedColor.BrightBlack, NamedColor.BrightRed, NamedColor.BrightGreen, NamedColor.BrightYellow,
                NamedColor.BrightBlue, NamedColor.BrightMagenta, NamedColor.BrightCyan, NamedColor.BrightWhite
            };
            return new caTTY.Core.Types.Color(namedColors[colorCode - 100]);
        }

        return null;
    }

    /// <summary>
    /// Create indexed color type.
    /// </summary>
    /// <param name="index">Color index (0-255)</param>
    /// <returns>Color for indexed color</returns>
    public static caTTY.Core.Types.Color CreateIndexedColor(byte index)
    {
        return new caTTY.Core.Types.Color(index);
    }

    /// <summary>
    /// Create RGB color type.
    /// </summary>
    /// <param name="r">Red component (0-255)</param>
    /// <param name="g">Green component (0-255)</param>
    /// <param name="b">Blue component (0-255)</param>
    /// <returns>Color for RGB color</returns>
    public static caTTY.Core.Types.Color CreateRgbColor(byte r, byte g, byte b)
    {
        return new caTTY.Core.Types.Color(r, g, b);
    }

    /// <summary>
    /// Create named color type.
    /// </summary>
    /// <param name="namedColor">Named color</param>
    /// <returns>Color for named color</returns>
    public static caTTY.Core.Types.Color CreateNamedColor(NamedColor namedColor)
    {
        return new caTTY.Core.Types.Color(namedColor);
    }
}