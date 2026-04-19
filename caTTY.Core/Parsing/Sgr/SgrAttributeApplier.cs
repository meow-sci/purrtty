using caTTY.Core.Types;

namespace caTTY.Core.Parsing.Sgr;

/// <summary>
///     Applies parsed SGR messages to SGR attributes.
///     Handles the transformation of SGR messages into terminal attribute state changes.
/// </summary>
public class SgrAttributeApplier
{
    /// <summary>
    ///     Applies SGR attributes to the current state.
    /// </summary>
    /// <param name="current">The current SGR attributes</param>
    /// <param name="messages">The SGR messages to apply</param>
    /// <returns>The updated SGR attributes</returns>
    public SgrAttributes ApplyAttributes(SgrAttributes current, ReadOnlySpan<SgrMessage> messages)
    {
        var result = current;

        foreach (var message in messages)
        {
            result = ApplySingleMessage(result, message);
        }

        return result;
    }

    /// <summary>
    ///     Applies a single SGR message to the current attributes.
    /// </summary>
    private SgrAttributes ApplySingleMessage(SgrAttributes current, SgrMessage message)
    {
        return message.Type switch
        {
            "sgr.reset" => SgrAttributes.Default,
            "sgr.bold" => UpdateAttribute(current, bold: true),
            "sgr.faint" => UpdateAttribute(current, faint: true),
            "sgr.italic" => UpdateAttribute(current, italic: true),
            "sgr.underline" => message.Data is UnderlineStyle style
                ? UpdateAttribute(current, underline: true, underlineStyle: style)
                : UpdateAttribute(current, underline: true, underlineStyle: UnderlineStyle.Single),
            "sgr.doubleUnderline" => UpdateAttribute(current, underline: true, underlineStyle: UnderlineStyle.Double),
            "sgr.slowBlink" or "sgr.rapidBlink" => UpdateAttribute(current, blink: true),
            "sgr.inverse" => UpdateAttribute(current, inverse: true),
            "sgr.hidden" => UpdateAttribute(current, hidden: true),
            "sgr.strikethrough" => UpdateAttribute(current, strikethrough: true),
            "sgr.normalIntensity" => UpdateAttribute(current, bold: false, faint: false),
            "sgr.notItalic" => UpdateAttribute(current, italic: false),
            "sgr.notUnderlined" => UpdateAttribute(current, underline: false, underlineStyle: UnderlineStyle.None),
            "sgr.notBlinking" => UpdateAttribute(current, blink: false),
            "sgr.notInverse" => UpdateAttribute(current, inverse: false),
            "sgr.notHidden" => UpdateAttribute(current, hidden: false),
            "sgr.notStrikethrough" => UpdateAttribute(current, strikethrough: false),
            "sgr.foregroundColor" when message.Data is Color color => UpdateAttributeWithColor(current, foregroundColor: color),
            "sgr.backgroundColor" when message.Data is Color bgColor => UpdateAttributeWithColor(current, backgroundColor: bgColor),
            "sgr.underlineColor" when message.Data is Color ulColor => UpdateAttributeWithColor(current, underlineColor: ulColor),
            "sgr.defaultForeground" => UpdateAttributeWithColor(current, foregroundColor: null),
            "sgr.defaultBackground" => UpdateAttributeWithColor(current, backgroundColor: null),
            "sgr.defaultUnderlineColor" => UpdateAttributeWithColor(current, underlineColor: null),
            "sgr.font" when message.Data is int font => UpdateAttribute(current, font: font),
            "sgr.disableProportionalSpacing" => current, // Not implemented, no change
            "sgr.framed" => current, // Not implemented, no change
            "sgr.encircled" => current, // Not implemented, no change
            "sgr.overlined" => current, // Not implemented, no change
            "sgr.notFramed" => current, // Not implemented, no change
            "sgr.notOverlined" => current, // Not implemented, no change
            "sgr.ideogram" => current, // Not implemented, no change
            "sgr.superscript" => current, // Not implemented, no change
            "sgr.subscript" => current, // Not implemented, no change
            "sgr.notSuperscriptSubscript" => current, // Not implemented, no change
            _ => current // Unknown or unimplemented messages don't change attributes
        };
    }

    /// <summary>
    ///     Helper method to create a new SgrAttributes with updated values.
    /// </summary>
    private static SgrAttributes UpdateAttribute(
        SgrAttributes current,
        bool? bold = null,
        bool? faint = null,
        bool? italic = null,
        bool? underline = null,
        UnderlineStyle? underlineStyle = null,
        bool? blink = null,
        bool? inverse = null,
        bool? hidden = null,
        bool? strikethrough = null,
        int? font = null)
    {
        return new SgrAttributes(
            bold: bold ?? current.Bold,
            faint: faint ?? current.Faint,
            italic: italic ?? current.Italic,
            underline: underline ?? current.Underline,
            underlineStyle: underlineStyle ?? current.UnderlineStyle,
            blink: blink ?? current.Blink,
            inverse: inverse ?? current.Inverse,
            hidden: hidden ?? current.Hidden,
            strikethrough: strikethrough ?? current.Strikethrough,
            foregroundColor: current.ForegroundColor,
            backgroundColor: current.BackgroundColor,
            underlineColor: current.UnderlineColor,
            font: font ?? current.Font);
    }

    /// <summary>
    ///     Helper method to create a new SgrAttributes with updated color values.
    /// </summary>
    private static SgrAttributes UpdateAttributeWithColor(
        SgrAttributes current,
        Color? foregroundColor = default,
        Color? backgroundColor = default,
        Color? underlineColor = default)
    {
        return new SgrAttributes(
            bold: current.Bold,
            faint: current.Faint,
            italic: current.Italic,
            underline: current.Underline,
            underlineStyle: current.UnderlineStyle,
            blink: current.Blink,
            inverse: current.Inverse,
            hidden: current.Hidden,
            strikethrough: current.Strikethrough,
            foregroundColor: foregroundColor == default(Color?) ? current.ForegroundColor : foregroundColor,
            backgroundColor: backgroundColor == default(Color?) ? current.BackgroundColor : backgroundColor,
            underlineColor: underlineColor == default(Color?) ? current.UnderlineColor : underlineColor,
            font: current.Font);
    }
}
