using PurrTTY.Terminal;
using PurrTTY.Terminal.Rendering;

namespace purrTTY.Display.Theming;

/// <summary>Where a theme definition came from.</summary>
public enum ThemeSource
{
    /// <summary>Shipped with the mod (TerminalThemes/ beside the assemblies) or built in code.</summary>
    BuiltIn,

    /// <summary>Saved by the user into the config directory's themes folder.</summary>
    UserFile,
}

/// <summary>
/// The 16 ANSI colors plus the primary/cursor/selection colors a theme defines.
/// Renderer-neutral (<see cref="RgbaColor"/>), so it can be converted into the
/// engine-facing <see cref="TerminalTheme"/> 256-entry palette.
/// </summary>
public sealed class ThemeColors
{
    /// <summary>ANSI palette entries 0-15 (0-7 normal, 8-15 bright).</summary>
    public RgbaColor[] Ansi { get; } = new RgbaColor[16];

    public RgbaColor Foreground { get; set; } = new(0xCC, 0xCC, 0xCC);
    public RgbaColor Background { get; set; } = new(0x0A, 0x0A, 0x0A);
    public RgbaColor Cursor { get; set; } = new(0xCC, 0xCC, 0xCC);
    public RgbaColor SelectionBackground { get; set; } = new(0x33, 0x55, 0x88);

    public ThemeColors Clone()
    {
        var copy = new ThemeColors
        {
            Foreground = Foreground,
            Background = Background,
            Cursor = Cursor,
            SelectionBackground = SelectionBackground,
        };
        Ansi.CopyTo(copy.Ansi, 0);
        return copy;
    }

    /// <summary>
    /// Builds the engine-facing theme: ANSI 16 from this definition, the standard
    /// 6×6×6 color cube and grayscale ramp for 16-255, and the default fg/bg/cursor.
    /// </summary>
    public TerminalTheme ToEngineTheme()
    {
        var palette = new RgbaColor[256];
        Ansi.CopyTo(palette, 0);

        ReadOnlySpan<byte> steps = stackalloc byte[] { 0, 95, 135, 175, 215, 255 };
        int idx = 16;
        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    palette[idx++] = new RgbaColor(steps[r], steps[g], steps[b]);
                }
            }
        }

        for (int i = 0; i < 24; i++)
        {
            byte v = (byte)(8 + i * 10);
            palette[232 + i] = new RgbaColor(v, v, v);
        }

        return new TerminalTheme(palette)
        {
            DefaultForeground = Foreground,
            DefaultBackground = Background,
            Cursor = Cursor,
        };
    }
}

/// <summary>
/// A named, persistable terminal theme. Colors are always present; the display
/// settings (font family/size and the three opacities) are optional — built-in
/// themes only define colors, while user-saved themes capture the full window
/// appearance. A null display setting means "keep the window's current value"
/// when the theme is applied.
/// </summary>
public sealed class ThemeDefinition
{
    public required string Name { get; init; }
    public ThemeSource Source { get; init; }

    /// <summary>Backing file, or null for the code-built default theme.</summary>
    public string? FilePath { get; init; }

    public required ThemeColors Colors { get; init; }

    public string? FontFamily { get; init; }
    public float? FontSize { get; init; }
    public float? BackgroundOpacity { get; init; }
    public float? ForegroundOpacity { get; init; }
    public float? CellBackgroundOpacity { get; init; }
}
