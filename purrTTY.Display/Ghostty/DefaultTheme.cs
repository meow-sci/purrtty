using PurrTTY.Terminal;
using PurrTTY.Terminal.Rendering;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// Builds the standard xterm 256-color palette as a <see cref="TerminalTheme"/>
/// to push into the engine, so cell colors come back fully resolved. This is a
/// sensible default; theme customization is follow-up work.
/// </summary>
internal static class DefaultTheme
{
    public static TerminalTheme Create()
    {
        var palette = new RgbaColor[256];

        // 0-15: standard + bright ANSI colors.
        (byte r, byte g, byte b)[] base16 =
        {
            (0, 0, 0), (205, 0, 0), (0, 205, 0), (205, 205, 0),
            (0, 0, 238), (205, 0, 205), (0, 205, 205), (229, 229, 229),
            (127, 127, 127), (255, 0, 0), (0, 255, 0), (255, 255, 0),
            (92, 92, 255), (255, 0, 255), (0, 255, 255), (255, 255, 255),
        };
        for (int i = 0; i < 16; i++)
        {
            palette[i] = new RgbaColor(base16[i].r, base16[i].g, base16[i].b);
        }

        // 16-231: 6×6×6 color cube.
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

        // 232-255: grayscale ramp.
        for (int i = 0; i < 24; i++)
        {
            byte v = (byte)(8 + i * 10);
            palette[232 + i] = new RgbaColor(v, v, v);
        }

        return new TerminalTheme(palette)
        {
            DefaultForeground = new RgbaColor(0xCC, 0xCC, 0xCC),
            DefaultBackground = new RgbaColor(0x0A, 0x0A, 0x0A),
            Cursor = new RgbaColor(0xCC, 0xCC, 0xCC),
        };
    }
}
