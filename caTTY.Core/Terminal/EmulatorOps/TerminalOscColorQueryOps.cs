using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles OSC color query operations for the terminal emulator.
///     Provides methods for querying current, default, named, and indexed colors.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalOscColorQueryOps
{
    private readonly IAttributeManager _attributeManager;

    /// <summary>
    ///     Creates a new OSC color query operations handler.
    /// </summary>
    /// <param name="attributeManager">The attribute manager for accessing SGR attributes</param>
    public TerminalOscColorQueryOps(IAttributeManager attributeManager)
    {
        _attributeManager = attributeManager;
    }

    /// <summary>
    ///     Gets the current foreground color for color queries.
    ///     Returns the current SGR foreground color or default terminal theme color.
    /// </summary>
    /// <returns>RGB color values for the current foreground color</returns>
    public (byte Red, byte Green, byte Blue) GetCurrentForegroundColor()
    {
        var currentAttributes = _attributeManager.CurrentAttributes;

        // If a specific foreground color is set in SGR attributes, use it
        if (currentAttributes.ForegroundColor.HasValue)
        {
            var color = currentAttributes.ForegroundColor.Value;
            return color.Type switch
            {
                ColorType.Rgb => (color.Red, color.Green, color.Blue),
                ColorType.Named => GetNamedColorRgb(color.NamedColor, isBackground: false),
                ColorType.Indexed => GetIndexedColorRgb(color.Index, isBackground: false),
                _ => GetDefaultForegroundColor()
            };
        }

        // Return default terminal foreground color
        return GetDefaultForegroundColor();
    }

    /// <summary>
    ///     Gets the current background color for color queries.
    ///     Returns the current SGR background color or default terminal theme color.
    /// </summary>
    /// <returns>RGB color values for the current background color</returns>
    public (byte Red, byte Green, byte Blue) GetCurrentBackgroundColor()
    {
        var currentAttributes = _attributeManager.CurrentAttributes;

        // If a specific background color is set in SGR attributes, use it
        if (currentAttributes.BackgroundColor.HasValue)
        {
            var color = currentAttributes.BackgroundColor.Value;
            return color.Type switch
            {
                ColorType.Rgb => (color.Red, color.Green, color.Blue),
                ColorType.Named => GetNamedColorRgb(color.NamedColor, isBackground: true),
                ColorType.Indexed => GetIndexedColorRgb(color.Index, isBackground: true),
                _ => GetDefaultBackgroundColor()
            };
        }

        // Return default terminal background color
        return GetDefaultBackgroundColor();
    }

    /// <summary>
    ///     Gets the default foreground color (typically white or light gray).
    /// </summary>
    public static (byte Red, byte Green, byte Blue) GetDefaultForegroundColor()
    {
        // Standard terminal default foreground (light gray)
        return (192, 192, 192);
    }

    /// <summary>
    ///     Gets the default background color (typically black or dark).
    /// </summary>
    public static (byte Red, byte Green, byte Blue) GetDefaultBackgroundColor()
    {
        // Standard terminal default background (black)
        return (0, 0, 0);
    }

    /// <summary>
    ///     Converts a named color to RGB values.
    /// </summary>
    public static (byte Red, byte Green, byte Blue) GetNamedColorRgb(NamedColor namedColor, bool isBackground)
    {
        return namedColor switch
        {
            NamedColor.Black => (0, 0, 0),
            NamedColor.Red => (128, 0, 0),
            NamedColor.Green => (0, 128, 0),
            NamedColor.Yellow => (128, 128, 0),
            NamedColor.Blue => (0, 0, 128),
            NamedColor.Magenta => (128, 0, 128),
            NamedColor.Cyan => (0, 128, 128),
            NamedColor.White => (192, 192, 192),
            NamedColor.BrightBlack => (128, 128, 128),
            NamedColor.BrightRed => (255, 0, 0),
            NamedColor.BrightGreen => (0, 255, 0),
            NamedColor.BrightYellow => (255, 255, 0),
            NamedColor.BrightBlue => (0, 0, 255),
            NamedColor.BrightMagenta => (255, 0, 255),
            NamedColor.BrightCyan => (0, 255, 255),
            NamedColor.BrightWhite => (255, 255, 255),
            _ => isBackground ? GetDefaultBackgroundColor() : GetDefaultForegroundColor()
        };
    }

    /// <summary>
    ///     Converts an indexed color (0-255) to RGB values using standard terminal palette.
    /// </summary>
    public static (byte Red, byte Green, byte Blue) GetIndexedColorRgb(int index, bool isBackground)
    {
        // Standard 16 colors (0-15)
        if (index < 16)
        {
            var namedColor = (NamedColor)index;
            return GetNamedColorRgb(namedColor, isBackground);
        }

        // 216 color cube (16-231): 6x6x6 RGB cube
        if (index >= 16 && index <= 231)
        {
            int cubeIndex = index - 16;
            int r = (cubeIndex / 36) % 6;
            int g = (cubeIndex / 6) % 6;
            int b = cubeIndex % 6;

            // Convert 0-5 range to 0-255 range
            byte red = (byte)(r == 0 ? 0 : 55 + r * 40);
            byte green = (byte)(g == 0 ? 0 : 55 + g * 40);
            byte blue = (byte)(b == 0 ? 0 : 55 + b * 40);

            return (red, green, blue);
        }

        // Grayscale ramp (232-255): 24 shades of gray
        if (index >= 232 && index <= 255)
        {
            int grayLevel = 8 + (index - 232) * 10;
            byte gray = (byte)Math.Min(255, grayLevel);
            return (gray, gray, gray);
        }

        // Invalid index - return default
        return isBackground ? GetDefaultBackgroundColor() : GetDefaultForegroundColor();
    }
}
