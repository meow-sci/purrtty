using caTTY.Core.Types;

namespace caTTY.Core.Parsing.Sgr;

/// <summary>
///     Factory for creating and building SGR messages.
///     Handles message construction, special mode processing, and message validation.
/// </summary>
public class SgrMessageFactory
{
    private readonly SgrColorParsers _colorParsers;

    /// <summary>
    ///     Named colors for standard 8 colors (SGR 30-37, 40-47).
    /// </summary>
    private static readonly NamedColor[] StandardColors =
    {
        NamedColor.Black,
        NamedColor.Red,
        NamedColor.Green,
        NamedColor.Yellow,
        NamedColor.Blue,
        NamedColor.Magenta,
        NamedColor.Cyan,
        NamedColor.White
    };

    /// <summary>
    ///     Named colors for bright colors (SGR 90-97, 100-107).
    /// </summary>
    private static readonly NamedColor[] BrightColors =
    {
        NamedColor.BrightBlack,
        NamedColor.BrightRed,
        NamedColor.BrightGreen,
        NamedColor.BrightYellow,
        NamedColor.BrightBlue,
        NamedColor.BrightMagenta,
        NamedColor.BrightCyan,
        NamedColor.BrightWhite
    };

    /// <summary>
    ///     Creates a new SGR message factory.
    /// </summary>
    /// <param name="colorParsers">Color parser for extended color sequences</param>
    public SgrMessageFactory(SgrColorParsers colorParsers)
    {
        _colorParsers = colorParsers ?? throw new ArgumentNullException(nameof(colorParsers));
    }

    /// <summary>
    ///     Creates an SGR message with the specified properties.
    /// </summary>
    public static SgrMessage CreateSgrMessage(string type, bool implemented, object? data = null)
    {
        return new SgrMessage
        {
            Type = type,
            Implemented = implemented,
            Data = data
        };
    }

    /// <summary>
    ///     Builds SGR messages from parsed parameters.
    /// </summary>
    /// <param name="parameters">The parsed parameters</param>
    /// <param name="separators">The separator strings between parameters</param>
    /// <param name="prefix">Optional prefix character (e.g., '>' or '?')</param>
    /// <param name="intermediate">Optional intermediate character(s)</param>
    /// <returns>List of SGR messages</returns>
    public List<SgrMessage> BuildMessages(int[] parameters, string[] separators, string? prefix, string? intermediate)
    {
        var messages = new List<SgrMessage>();

        // Handle special sequences with prefixes or intermediates
        if (prefix == ">")
        {
            return HandleEnhancedSgrMode(parameters);
        }

        if (prefix == "?")
        {
            return HandlePrivateSgrMode(parameters);
        }

        if (intermediate != null)
        {
            return HandleSgrWithIntermediate(parameters, intermediate);
        }

        // Empty or single zero param means reset
        if (parameters.Length == 0 || (parameters.Length == 1 && parameters[0] == 0))
        {
            messages.Add(CreateSgrMessage("sgr.reset", true));
            return messages;
        }

        var context = new SgrParseContext
        {
            Params = parameters,
            Separators = separators,
            Index = 0
        };

        while (context.Index < parameters.Length)
        {
            int param = parameters[context.Index];
            string nextSep = context.Index < separators.Length ? separators[context.Index] : ";";

            var message = BuildSingleParameterMessage(context, param, nextSep);
            if (message != null)
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    /// <summary>
    ///     Builds a single SGR message from a parameter.
    /// </summary>
    internal SgrMessage? BuildSingleParameterMessage(SgrParseContext context, int param, string nextSep)
    {
        switch (param)
        {
            case 0:
                context.Index++;
                return CreateSgrMessage("sgr.reset", true);

            case 1:
                context.Index++;
                return CreateSgrMessage("sgr.bold", true);

            case 2:
                context.Index++;
                return CreateSgrMessage("sgr.faint", true);

            case 3:
                context.Index++;
                return CreateSgrMessage("sgr.italic", true);

            case 4:
                return ParseUnderlineParameter(context, nextSep);

            case 5:
                context.Index++;
                return CreateSgrMessage("sgr.slowBlink", true);

            case 6:
                context.Index++;
                return CreateSgrMessage("sgr.rapidBlink", true);

            case 7:
                context.Index++;
                return CreateSgrMessage("sgr.inverse", true);

            case 8:
                context.Index++;
                return CreateSgrMessage("sgr.hidden", true);

            case 9:
                context.Index++;
                return CreateSgrMessage("sgr.strikethrough", true);

            case >= 10 and <= 19:
                context.Index++;
                return CreateSgrMessage("sgr.font", false, param - 10);

            case 20:
                context.Index++;
                return CreateSgrMessage("sgr.fraktur", false);

            case 21:
                context.Index++;
                // SGR 21 is formally "Double Underline" in ECMA-48, but historically
                // implemented as "Normal Intensity" (disable bold) by Linux console and others.
                // Many apps (e.g. starship, nvim) rely on 21 to disable bold.
                // We map it to normal intensity to prevent unwanted double underlines.
                // True double underline is still supported via SGR 4:2.
                return CreateSgrMessage("sgr.normalIntensity", true);

            case 22:
                context.Index++;
                return CreateSgrMessage("sgr.normalIntensity", true);

            case 23:
                context.Index++;
                return CreateSgrMessage("sgr.notItalic", true);

            case 24:
                context.Index++;
                return CreateSgrMessage("sgr.notUnderlined", true);

            case 25:
                context.Index++;
                return CreateSgrMessage("sgr.notBlinking", true);

            case 26:
                context.Index++;
                return CreateSgrMessage("sgr.proportionalSpacing", false);

            case 27:
                context.Index++;
                return CreateSgrMessage("sgr.notInverse", true);

            case 28:
                context.Index++;
                return CreateSgrMessage("sgr.notHidden", true);

            case 29:
                context.Index++;
                return CreateSgrMessage("sgr.notStrikethrough", true);

            case >= 30 and <= 37:
                context.Index++;
                return CreateSgrMessage("sgr.foregroundColor", true, new Color(StandardColors[param - 30]));

            case 38:
                return _colorParsers.ParseExtendedForegroundColor(context);

            case 39:
                context.Index++;
                return CreateSgrMessage("sgr.defaultForeground", true);

            case >= 40 and <= 47:
                context.Index++;
                return CreateSgrMessage("sgr.backgroundColor", true, new Color(StandardColors[param - 40]));

            case 48:
                return _colorParsers.ParseExtendedBackgroundColor(context);

            case 49:
                context.Index++;
                return CreateSgrMessage("sgr.defaultBackground", true);

            case >= 90 and <= 97:
                context.Index++;
                return CreateSgrMessage("sgr.foregroundColor", true, new Color(BrightColors[param - 90]));

            case >= 100 and <= 107:
                context.Index++;
                return CreateSgrMessage("sgr.backgroundColor", true, new Color(BrightColors[param - 100]));

            case 50:
                context.Index++;
                return CreateSgrMessage("sgr.disableProportionalSpacing", false);

            case 51:
                context.Index++;
                return CreateSgrMessage("sgr.framed", false);

            case 52:
                context.Index++;
                return CreateSgrMessage("sgr.encircled", false);

            case 53:
                context.Index++;
                return CreateSgrMessage("sgr.overlined", false);

            case 54:
                context.Index++;
                return CreateSgrMessage("sgr.notFramed", false);

            case 55:
                context.Index++;
                return CreateSgrMessage("sgr.notOverlined", false);

            case >= 60 and <= 65:
                context.Index++;
                return CreateSgrMessage("sgr.ideogram", false, GetIdeogramStyle(param));

            case 73:
                context.Index++;
                return CreateSgrMessage("sgr.superscript", false);

            case 74:
                context.Index++;
                return CreateSgrMessage("sgr.subscript", false);

            case 75:
                context.Index++;
                return CreateSgrMessage("sgr.notSuperscriptSubscript", false);

            case 58:
                return _colorParsers.ParseExtendedUnderlineColor(context);

            case 59:
                context.Index++;
                return CreateSgrMessage("sgr.defaultUnderlineColor", true);

            default:
                context.Index++;
                return CreateSgrMessage("sgr.unknown", false, param);
        }
    }

    /// <summary>
    ///     Parses underline parameter with optional style.
    /// </summary>
    internal SgrMessage? ParseUnderlineParameter(SgrParseContext context, string nextSep)
    {
        if (nextSep == ":" && context.Index + 1 < context.Params.Length)
        {
            int styleParam = context.Params[context.Index + 1];
            if (styleParam == 0)
            {
                context.Index += 2;
                return CreateSgrMessage("sgr.notUnderlined", true);
            }

            var style = ParseUnderlineStyle(styleParam);
            context.Index += 2;
            return CreateSgrMessage("sgr.underline", true, style);
        }

        context.Index++;
        return CreateSgrMessage("sgr.underline", true, UnderlineStyle.Single);
    }

    /// <summary>
    ///     Parses underline style from parameter value.
    /// </summary>
    private static UnderlineStyle ParseUnderlineStyle(int style)
    {
        return style switch
        {
            0 or 1 => UnderlineStyle.Single,
            2 => UnderlineStyle.Double,
            3 => UnderlineStyle.Curly,
            4 => UnderlineStyle.Dotted,
            5 => UnderlineStyle.Dashed,
            _ => UnderlineStyle.Single
        };
    }

    /// <summary>
    ///     Gets ideogram style string from parameter value.
    /// </summary>
    private static string GetIdeogramStyle(int param)
    {
        return param switch
        {
            60 => "underline",
            61 => "doubleUnderline",
            62 => "overline",
            63 => "doubleOverline",
            64 => "stress",
            65 => "reset",
            _ => "unknown"
        };
    }

    /// <summary>
    ///     Handles enhanced SGR sequences with > prefix (e.g., CSI > 4 ; 2 m).
    ///     These are typically used for advanced terminal features like enhanced underline styles.
    /// </summary>
    /// <param name="parameters">The SGR parameters</param>
    /// <returns>List of SGR messages for the enhanced mode</returns>
    private List<SgrMessage> HandleEnhancedSgrMode(int[] parameters)
    {
        var messages = new List<SgrMessage>();

        if (parameters.Length >= 2 && parameters[0] == 4)
        {
            // CONFLICT: CSI > 4 ; n m is XTerm's 'modifyOtherKeys' sequence.
            // It is NOT a standard 'Enhanced SGR' sequence for underlines.
            // Previous implementation incorrectly interpreted this as 'Enhanced Underline Mode'.
            //   > 4; 2 m -> Enable modifyOtherKeys mode 2.
            //   Old logic:  -> Double Underline (style 2).
            // This collision causes apps like nvim/starship (which enable modifyOtherKeys)
            // to inadvertently trigger Double Underline for all text.
            
            // We return an unimplemented/ignored message to prevent attribute contamination.
            // True enhanced underlines use SGR 4:x (Colon) or 4:x (Semicolon) without '>' prefix.
            messages.Add(CreateSgrMessage("sgr.modifyOtherKeys", false, parameters));
            return messages;
        }

        // Other enhanced modes not yet supported - create unimplemented message
        messages.Add(CreateSgrMessage("sgr.enhancedMode", false, parameters));
        return messages;
    }

    /// <summary>
    ///     Handles private SGR sequences with ? prefix (e.g., CSI ? 4 m).
    ///     These are typically used for private/experimental features.
    /// </summary>
    /// <param name="parameters">The SGR parameters</param>
    /// <returns>List of SGR messages for the private mode</returns>
    private List<SgrMessage> HandlePrivateSgrMode(int[] parameters)
    {
        var messages = new List<SgrMessage>();

        // Handle specific private SGR modes
        if (parameters.Length == 1 && parameters[0] == 4)
        {
            // Private underline mode (?4m) - enable underline
            messages.Add(CreateSgrMessage("sgr.underline", true, UnderlineStyle.Single));
            return messages;
        }

        // For other private modes, gracefully ignore with unimplemented message
        messages.Add(CreateSgrMessage("sgr.privateMode", false, parameters));
        return messages;
    }

    /// <summary>
    ///     Handles SGR sequences with intermediate characters (e.g., CSI 0 % m).
    ///     These are used for special SGR attribute resets or modifications.
    /// </summary>
    /// <param name="parameters">The SGR parameters</param>
    /// <param name="intermediate">The intermediate character string</param>
    /// <returns>List of SGR messages for the intermediate sequence</returns>
    private List<SgrMessage> HandleSgrWithIntermediate(int[] parameters, string intermediate)
    {
        var messages = new List<SgrMessage>();

        // Handle specific intermediate character sequences
        if (intermediate == "%")
        {
            // CSI 0 % m - Reset specific attributes
            if (parameters.Length == 1 && parameters[0] == 0)
            {
                // Reset all SGR attributes (similar to SGR 0)
                messages.Add(CreateSgrMessage("sgr.reset", true));
                return messages;
            }
        }

        // For other intermediate sequences, gracefully ignore with unimplemented message
        bool implemented = intermediate == "%" && parameters.Length == 1 && parameters[0] == 0;
        messages.Add(CreateSgrMessage("sgr.withIntermediate", implemented, new { parameters, intermediate }));
        return messages;
    }
}
