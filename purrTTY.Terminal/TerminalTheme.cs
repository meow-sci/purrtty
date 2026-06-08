using PurrTTY.Terminal.Rendering;

namespace PurrTTY.Terminal;

/// <summary>
/// A renderer-neutral color theme pushed into the engine so that cell colors
/// come back pre-resolved. Only the RGB channels are sent to the engine;
/// background opacity is a frontend concern applied at draw time.
/// </summary>
public sealed class TerminalTheme
{
    /// <summary>The 256-entry ANSI palette. Indices 0-15 are the standard colors.</summary>
    public RgbaColor[] Palette { get; }

    public RgbaColor DefaultForeground { get; set; }
    public RgbaColor DefaultBackground { get; set; }
    public RgbaColor Cursor { get; set; }

    public TerminalTheme()
    {
        Palette = new RgbaColor[256];
        DefaultForeground = new RgbaColor(0xCC, 0xCC, 0xCC);
        DefaultBackground = new RgbaColor(0x0A, 0x0A, 0x0A);
        Cursor = new RgbaColor(0xCC, 0xCC, 0xCC);
    }

    public TerminalTheme(RgbaColor[] palette)
    {
        if (palette.Length != 256)
        {
            throw new ArgumentException("Palette must have exactly 256 entries.", nameof(palette));
        }

        Palette = palette;
    }
}
