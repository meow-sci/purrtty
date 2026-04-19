using System;
using System.Text;

namespace caTTY.Core.Utils;

public static class MouseInputEncoder
{
    private const int ModShift = 4;
    private const int ModAlt = 8;
    private const int ModCtrl = 16;

    public static string EncodeMouseWheel(
        bool directionUp,
        int x1,
        int y1,
        bool shift,
        bool alt,
        bool ctrl,
        bool sgrEncoding)
    {
        x1 = Math.Max(1, x1);
        y1 = Math.Max(1, y1);

        int modBits = 0;
        if (shift) modBits |= ModShift;
        if (alt) modBits |= ModAlt;
        if (ctrl) modBits |= ModCtrl;

        // xterm wheel: buttons 64/65 (press only)
        int wheelButton = directionUp ? 64 : 65;
        int b = wheelButton + modBits;

        if (sgrEncoding)
        {
            // SGR mouse: ESC[<b;x;yM (press)
            return $"\x1b[<{b};{x1};{y1}M";
        }

        // X10 mouse: ESC[M bxy (single-byte values with 32 offset)
        // Coordinates are 1-based. X10 can only represent values up to 223; clamp for safety.
        int bx = 32 + b;
        int cx = 32 + Math.Min(223, x1);
        int cy = 32 + Math.Min(223, y1);

        var sb = new StringBuilder(6);
        sb.Append('\x1b');
        sb.Append('[');
        sb.Append('M');
        sb.Append((char)bx);
        sb.Append((char)cx);
        sb.Append((char)cy);
        return sb.ToString();
    }
}
