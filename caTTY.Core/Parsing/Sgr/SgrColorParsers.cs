using caTTY.Core.Types;

namespace caTTY.Core.Parsing.Sgr;

/// <summary>
///     Parses color sequences for SGR (Select Graphic Rendition) parameters.
///     Handles 8-bit indexed colors (256-color palette) and 24-bit RGB colors.
/// </summary>
public class SgrColorParsers
{
    /// <summary>
    ///     Parses extended foreground color from SGR parameters.
    /// </summary>
    /// <param name="context">The parse context containing parameters and separators</param>
    /// <returns>An SGR message with the foreground color, or unknown message if parsing fails</returns>
    internal SgrMessage? ParseExtendedForegroundColor(SgrParseContext context)
    {
        context.Index++; // Skip 38
        var color = ParseExtendedColor(context, out int consumed);
        if (color != null)
        {
            context.Index += consumed;
            return SgrMessageFactory.CreateSgrMessage("sgr.foregroundColor", true, color);
        }
        // If parsing failed, create unknown message for just the 38 parameter
        return SgrMessageFactory.CreateSgrMessage("sgr.unknown", false, 38);
    }

    /// <summary>
    ///     Parses extended background color from SGR parameters.
    /// </summary>
    /// <param name="context">The parse context containing parameters and separators</param>
    /// <returns>An SGR message with the background color, or unknown message if parsing fails</returns>
    internal SgrMessage? ParseExtendedBackgroundColor(SgrParseContext context)
    {
        context.Index++; // Skip 48
        var color = ParseExtendedColor(context, out int consumed);
        if (color != null)
        {
            context.Index += consumed;
            return SgrMessageFactory.CreateSgrMessage("sgr.backgroundColor", true, color);
        }
        // If parsing failed, create unknown message for just the 48 parameter
        return SgrMessageFactory.CreateSgrMessage("sgr.unknown", false, 48);
    }

    /// <summary>
    ///     Parses extended underline color from SGR parameters.
    /// </summary>
    /// <param name="context">The parse context containing parameters and separators</param>
    /// <returns>An SGR message with the underline color, or unknown message if parsing fails</returns>
    internal SgrMessage? ParseExtendedUnderlineColor(SgrParseContext context)
    {
        context.Index++; // Skip 58
        var color = ParseExtendedColor(context, out int consumed);
        if (color != null)
        {
            context.Index += consumed;
            return SgrMessageFactory.CreateSgrMessage("sgr.underlineColor", true, color);
        }
        // If parsing failed, create unknown message for just the 58 parameter
        return SgrMessageFactory.CreateSgrMessage("sgr.unknown", false, 58);
    }

    /// <summary>
    ///     Parses extended color sequences (8-bit indexed or 24-bit RGB).
    ///     Supports both semicolon and colon separators.
    /// </summary>
    /// <param name="context">The parse context containing parameters and separators</param>
    /// <param name="consumed">Number of parameters consumed from the context</param>
    /// <returns>The parsed color, or null if parsing fails</returns>
    private Color? ParseExtendedColor(SgrParseContext context, out int consumed)
    {
        consumed = 0;

        if (context.Index >= context.Params.Length)
            return null;

        int colorType = context.Params[context.Index];

        if (colorType == 5)
        {
            // 256-color mode: 38;5;n or 38:5:n
            if (context.Index + 1 < context.Params.Length)
            {
                int colorIndex = context.Params[context.Index + 1];
                if (colorIndex >= 0 && colorIndex <= 255)
                {
                    consumed = 2;
                    return new Color((byte)colorIndex);
                }
            }
            return null;
        }

        if (colorType == 2)
        {
            // True color mode: 38;2;r;g;b or 38:2:r:g:b (or 38:2::r:g:b with colorspace)
            // The ITU T.416 format includes an optional colorspace ID after the 2

            // Check if we have colon separators that might indicate colorspace format
            bool hasColonSeparators = context.Index < context.Separators.Length &&
                                     context.Separators[context.Index] == ":";

            if (hasColonSeparators && context.Index + 4 < context.Params.Length)
            {
                // Try parsing with colorspace ID: 38:2:<colorspace>:r:g:b or 38:2::r:g:b
                int r = context.Params[context.Index + 2];
                int g = context.Params[context.Index + 3];
                int b = context.Params[context.Index + 4];
                if (r >= 0 && r <= 255 && g >= 0 && g <= 255 && b >= 0 && b <= 255)
                {
                    consumed = 5;
                    return new Color((byte)r, (byte)g, (byte)b);
                }
            }

            // Standard format: 38;2;r;g;b or 38:2:r:g:b
            if (context.Index + 3 < context.Params.Length)
            {
                int r = context.Params[context.Index + 1];
                int g = context.Params[context.Index + 2];
                int b = context.Params[context.Index + 3];
                if (r >= 0 && r <= 255 && g >= 0 && g <= 255 && b >= 0 && b <= 255)
                {
                    consumed = 4;
                    return new Color((byte)r, (byte)g, (byte)b);
                }
            }
            return null;
        }

        // Unknown color type
        return null;
    }
}
