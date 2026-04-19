using caTTY.Core.Types;

namespace caTTY.Core.Managers;

/// <summary>
///     Manages SGR attribute state and application to characters.
///     Handles foreground/background colors, text styles, and other character attributes.
/// </summary>
public class AttributeManager : IAttributeManager
{
    private SgrAttributes _currentAttributes;

    /// <summary>
    ///     Creates a new attribute manager with default attributes.
    /// </summary>
    public AttributeManager()
    {
        _currentAttributes = SgrAttributes.Default;
        CurrentCharacterProtection = false;
        CurrentHyperlinkUrl = null;
    }

    /// <summary>
    ///     Gets or sets the current SGR attributes for new characters.
    /// </summary>
    public SgrAttributes CurrentAttributes
    {
        get => _currentAttributes;
        set => _currentAttributes = value;
    }

    /// <summary>
    ///     Gets or sets the current character protection attribute.
    /// </summary>
    public bool CurrentCharacterProtection { get; set; }

    /// <summary>
    ///     Gets or sets the current hyperlink URL for new characters.
    ///     Null means no hyperlink is active.
    /// </summary>
    public string? CurrentHyperlinkUrl { get; set; }

    /// <summary>
    ///     Applies an SGR message to update the current attributes.
    /// </summary>
    /// <param name="message">The SGR message to apply</param>
    public void ApplySgrMessage(SgrMessage message)
    {
        if (message == null)
        {
            return;
        }

        // Handle SGR message based on type
        switch (message.Type.ToLowerInvariant())
        {
            case "sgr.reset":
                ResetAttributes();
                break;
                
            case "sgr.bold":
                _currentAttributes = new SgrAttributes(
                    bold: true,
                    faint: _currentAttributes.Faint,
                    italic: _currentAttributes.Italic,
                    underline: _currentAttributes.Underline,
                    underlineStyle: _currentAttributes.UnderlineStyle,
                    blink: _currentAttributes.Blink,
                    inverse: _currentAttributes.Inverse,
                    hidden: _currentAttributes.Hidden,
                    strikethrough: _currentAttributes.Strikethrough,
                    foregroundColor: _currentAttributes.ForegroundColor,
                    backgroundColor: _currentAttributes.BackgroundColor,
                    underlineColor: _currentAttributes.UnderlineColor,
                    font: _currentAttributes.Font);
                break;
                
            case "sgr.normalintensity":
                _currentAttributes = new SgrAttributes(
                    bold: false,
                    faint: false,
                    italic: _currentAttributes.Italic,
                    underline: _currentAttributes.Underline,
                    underlineStyle: _currentAttributes.UnderlineStyle,
                    blink: _currentAttributes.Blink,
                    inverse: _currentAttributes.Inverse,
                    hidden: _currentAttributes.Hidden,
                    strikethrough: _currentAttributes.Strikethrough,
                    foregroundColor: _currentAttributes.ForegroundColor,
                    backgroundColor: _currentAttributes.BackgroundColor,
                    underlineColor: _currentAttributes.UnderlineColor,
                    font: _currentAttributes.Font);
                break;
                
            case "sgr.italic":
                _currentAttributes = new SgrAttributes(
                    bold: _currentAttributes.Bold,
                    faint: _currentAttributes.Faint,
                    italic: true,
                    underline: _currentAttributes.Underline,
                    underlineStyle: _currentAttributes.UnderlineStyle,
                    blink: _currentAttributes.Blink,
                    inverse: _currentAttributes.Inverse,
                    hidden: _currentAttributes.Hidden,
                    strikethrough: _currentAttributes.Strikethrough,
                    foregroundColor: _currentAttributes.ForegroundColor,
                    backgroundColor: _currentAttributes.BackgroundColor,
                    underlineColor: _currentAttributes.UnderlineColor,
                    font: _currentAttributes.Font);
                break;
                
            case "sgr.notitalic":
                _currentAttributes = new SgrAttributes(
                    bold: _currentAttributes.Bold,
                    faint: _currentAttributes.Faint,
                    italic: false,
                    underline: _currentAttributes.Underline,
                    underlineStyle: _currentAttributes.UnderlineStyle,
                    blink: _currentAttributes.Blink,
                    inverse: _currentAttributes.Inverse,
                    hidden: _currentAttributes.Hidden,
                    strikethrough: _currentAttributes.Strikethrough,
                    foregroundColor: _currentAttributes.ForegroundColor,
                    backgroundColor: _currentAttributes.BackgroundColor,
                    underlineColor: _currentAttributes.UnderlineColor,
                    font: _currentAttributes.Font);
                break;
                
            case "sgr.underline":
                _currentAttributes = new SgrAttributes(
                    bold: _currentAttributes.Bold,
                    faint: _currentAttributes.Faint,
                    italic: _currentAttributes.Italic,
                    underline: true,
                    underlineStyle: _currentAttributes.UnderlineStyle,
                    blink: _currentAttributes.Blink,
                    inverse: _currentAttributes.Inverse,
                    hidden: _currentAttributes.Hidden,
                    strikethrough: _currentAttributes.Strikethrough,
                    foregroundColor: _currentAttributes.ForegroundColor,
                    backgroundColor: _currentAttributes.BackgroundColor,
                    underlineColor: _currentAttributes.UnderlineColor,
                    font: _currentAttributes.Font);
                break;
                
            case "sgr.notunderlined":
                _currentAttributes = new SgrAttributes(
                    bold: _currentAttributes.Bold,
                    faint: _currentAttributes.Faint,
                    italic: _currentAttributes.Italic,
                    underline: false,
                    underlineStyle: _currentAttributes.UnderlineStyle,
                    blink: _currentAttributes.Blink,
                    inverse: _currentAttributes.Inverse,
                    hidden: _currentAttributes.Hidden,
                    strikethrough: _currentAttributes.Strikethrough,
                    foregroundColor: _currentAttributes.ForegroundColor,
                    backgroundColor: _currentAttributes.BackgroundColor,
                    underlineColor: _currentAttributes.UnderlineColor,
                    font: _currentAttributes.Font);
                break;
                
            case "sgr.foregroundcolor":
            case "sgr.backgroundcolor":
                // Color handling
                if (message.Data is Color color)
                {
                    if (message.Type.ToLowerInvariant() == "sgr.foregroundcolor")
                    {
                        _currentAttributes = new SgrAttributes(
                            bold: _currentAttributes.Bold,
                            faint: _currentAttributes.Faint,
                            italic: _currentAttributes.Italic,
                            underline: _currentAttributes.Underline,
                            underlineStyle: _currentAttributes.UnderlineStyle,
                            blink: _currentAttributes.Blink,
                            inverse: _currentAttributes.Inverse,
                            hidden: _currentAttributes.Hidden,
                            strikethrough: _currentAttributes.Strikethrough,
                            foregroundColor: color,
                            backgroundColor: _currentAttributes.BackgroundColor,
                            underlineColor: _currentAttributes.UnderlineColor,
                            font: _currentAttributes.Font);
                    }
                    else
                    {
                        _currentAttributes = new SgrAttributes(
                            bold: _currentAttributes.Bold,
                            faint: _currentAttributes.Faint,
                            italic: _currentAttributes.Italic,
                            underline: _currentAttributes.Underline,
                            underlineStyle: _currentAttributes.UnderlineStyle,
                            blink: _currentAttributes.Blink,
                            inverse: _currentAttributes.Inverse,
                            hidden: _currentAttributes.Hidden,
                            strikethrough: _currentAttributes.Strikethrough,
                            foregroundColor: _currentAttributes.ForegroundColor,
                            backgroundColor: color,
                            underlineColor: _currentAttributes.UnderlineColor,
                            font: _currentAttributes.Font);
                    }
                }
                break;
        }
    }

    /// <summary>
    ///     Resets all attributes to their default values.
    /// </summary>
    public void ResetAttributes()
    {
        _currentAttributes = SgrAttributes.Default;
        CurrentCharacterProtection = false;
        CurrentHyperlinkUrl = null;
    }

    /// <summary>
    ///     Gets the default SGR attributes.
    /// </summary>
    /// <returns>Default SGR attributes</returns>
    public SgrAttributes GetDefaultAttributes()
    {
        return SgrAttributes.Default;
    }

    /// <summary>
    ///     Sets the foreground color.
    /// </summary>
    /// <param name="color">The foreground color to set</param>
    public void SetForegroundColor(Color color)
    {
        _currentAttributes = new SgrAttributes(
            bold: _currentAttributes.Bold,
            faint: _currentAttributes.Faint,
            italic: _currentAttributes.Italic,
            underline: _currentAttributes.Underline,
            underlineStyle: _currentAttributes.UnderlineStyle,
            blink: _currentAttributes.Blink,
            inverse: _currentAttributes.Inverse,
            hidden: _currentAttributes.Hidden,
            strikethrough: _currentAttributes.Strikethrough,
            foregroundColor: color,
            backgroundColor: _currentAttributes.BackgroundColor,
            underlineColor: _currentAttributes.UnderlineColor,
            font: _currentAttributes.Font);
    }

    /// <summary>
    ///     Sets the background color.
    /// </summary>
    /// <param name="color">The background color to set</param>
    public void SetBackgroundColor(Color color)
    {
        _currentAttributes = new SgrAttributes(
            bold: _currentAttributes.Bold,
            faint: _currentAttributes.Faint,
            italic: _currentAttributes.Italic,
            underline: _currentAttributes.Underline,
            underlineStyle: _currentAttributes.UnderlineStyle,
            blink: _currentAttributes.Blink,
            inverse: _currentAttributes.Inverse,
            hidden: _currentAttributes.Hidden,
            strikethrough: _currentAttributes.Strikethrough,
            foregroundColor: _currentAttributes.ForegroundColor,
            backgroundColor: color,
            underlineColor: _currentAttributes.UnderlineColor,
            font: _currentAttributes.Font);
    }

    /// <summary>
    ///     Sets text style attributes.
    /// </summary>
    /// <param name="bold">Whether text should be bold</param>
    /// <param name="italic">Whether text should be italic</param>
    /// <param name="underline">Whether text should be underlined</param>
    public void SetTextStyle(bool bold, bool italic, bool underline)
    {
        _currentAttributes = new SgrAttributes(
            bold: bold,
            faint: _currentAttributes.Faint,
            italic: italic,
            underline: underline,
            underlineStyle: _currentAttributes.UnderlineStyle,
            blink: _currentAttributes.Blink,
            inverse: _currentAttributes.Inverse,
            hidden: _currentAttributes.Hidden,
            strikethrough: _currentAttributes.Strikethrough,
            foregroundColor: _currentAttributes.ForegroundColor,
            backgroundColor: _currentAttributes.BackgroundColor,
            underlineColor: _currentAttributes.UnderlineColor,
            font: _currentAttributes.Font);
    }

    /// <summary>
    ///     Sets the inverse video attribute.
    /// </summary>
    /// <param name="inverse">Whether inverse video should be enabled</param>
    public void SetInverse(bool inverse)
    {
        _currentAttributes = new SgrAttributes(
            bold: _currentAttributes.Bold,
            faint: _currentAttributes.Faint,
            italic: _currentAttributes.Italic,
            underline: _currentAttributes.Underline,
            underlineStyle: _currentAttributes.UnderlineStyle,
            blink: _currentAttributes.Blink,
            inverse: inverse,
            hidden: _currentAttributes.Hidden,
            strikethrough: _currentAttributes.Strikethrough,
            foregroundColor: _currentAttributes.ForegroundColor,
            backgroundColor: _currentAttributes.BackgroundColor,
            underlineColor: _currentAttributes.UnderlineColor,
            font: _currentAttributes.Font);
    }

    /// <summary>
    ///     Sets the dim attribute.
    /// </summary>
    /// <param name="dim">Whether dim should be enabled</param>
    public void SetDim(bool dim)
    {
        _currentAttributes = new SgrAttributes(
            bold: _currentAttributes.Bold,
            faint: dim,
            italic: _currentAttributes.Italic,
            underline: _currentAttributes.Underline,
            underlineStyle: _currentAttributes.UnderlineStyle,
            blink: _currentAttributes.Blink,
            inverse: _currentAttributes.Inverse,
            hidden: _currentAttributes.Hidden,
            strikethrough: _currentAttributes.Strikethrough,
            foregroundColor: _currentAttributes.ForegroundColor,
            backgroundColor: _currentAttributes.BackgroundColor,
            underlineColor: _currentAttributes.UnderlineColor,
            font: _currentAttributes.Font);
    }

    /// <summary>
    ///     Sets the strikethrough attribute.
    /// </summary>
    /// <param name="strikethrough">Whether strikethrough should be enabled</param>
    public void SetStrikethrough(bool strikethrough)
    {
        _currentAttributes = new SgrAttributes(
            bold: _currentAttributes.Bold,
            faint: _currentAttributes.Faint,
            italic: _currentAttributes.Italic,
            underline: _currentAttributes.Underline,
            underlineStyle: _currentAttributes.UnderlineStyle,
            blink: _currentAttributes.Blink,
            inverse: _currentAttributes.Inverse,
            hidden: _currentAttributes.Hidden,
            strikethrough: strikethrough,
            foregroundColor: _currentAttributes.ForegroundColor,
            backgroundColor: _currentAttributes.BackgroundColor,
            underlineColor: _currentAttributes.UnderlineColor,
            font: _currentAttributes.Font);
    }

    /// <summary>
    ///     Sets the blink attribute.
    /// </summary>
    /// <param name="blink">Whether blink should be enabled</param>
    public void SetBlink(bool blink)
    {
        _currentAttributes = new SgrAttributes(
            bold: _currentAttributes.Bold,
            faint: _currentAttributes.Faint,
            italic: _currentAttributes.Italic,
            underline: _currentAttributes.Underline,
            underlineStyle: _currentAttributes.UnderlineStyle,
            blink: blink,
            inverse: _currentAttributes.Inverse,
            hidden: _currentAttributes.Hidden,
            strikethrough: _currentAttributes.Strikethrough,
            foregroundColor: _currentAttributes.ForegroundColor,
            backgroundColor: _currentAttributes.BackgroundColor,
            underlineColor: _currentAttributes.UnderlineColor,
            font: _currentAttributes.Font);
    }

    /// <summary>
    ///     Sets the current hyperlink URL for new characters.
    /// </summary>
    /// <param name="url">The hyperlink URL, or null to clear hyperlink state</param>
    public void SetHyperlinkUrl(string? url)
    {
        CurrentHyperlinkUrl = url;
    }

    /// <summary>
    ///     Parses SGR sequence from CSI parameters and creates an SgrSequence.
    /// </summary>
    /// <param name="parameters">The CSI parameters</param>
    /// <param name="raw">The raw sequence string</param>
    /// <returns>The parsed SGR sequence</returns>
    public SgrSequence ParseSgrFromCsi(int[] parameters, string raw)
    {
        // Create a fake SGR sequence by converting CSI parameters to SGR format
        // CSI parameters are already parsed, so we just need to create the SGR sequence
        var sgrParser = new Parsing.SgrParser();
        
        // Convert parameters to byte array for SGR parser
        // The SGR parser expects the full escape sequence, so we reconstruct it
        string sgrRaw = $"\x1b[{string.Join(";", parameters)}m";
        byte[] sgrBytes = System.Text.Encoding.UTF8.GetBytes(sgrRaw);
        
        return sgrParser.ParseSgrSequence(sgrBytes, sgrRaw);
    }

    /// <summary>
    ///     Parses enhanced SGR sequence from CSI parameters and creates an SgrSequence.
    ///     Used for enhanced SGR sequences with > prefix (e.g., CSI > 4 ; 2 m).
    /// </summary>
    /// <param name="parameters">The CSI parameters</param>
    /// <param name="raw">The raw sequence string</param>
    /// <returns>The parsed enhanced SGR sequence</returns>
    public SgrSequence ParseEnhancedSgrFromCsi(int[] parameters, string raw)
    {
        // Create an enhanced SGR sequence by converting CSI parameters to enhanced SGR format
        // Enhanced SGR sequences have the > prefix
        var sgrParser = new Parsing.SgrParser();
        
        // Convert parameters to byte array for SGR parser
        // The SGR parser expects the full escape sequence with > prefix
        string sgrRaw = $"\x1b[>{string.Join(";", parameters)}m";
        byte[] sgrBytes = System.Text.Encoding.UTF8.GetBytes(sgrRaw);
        
        return sgrParser.ParseSgrSequence(sgrBytes, sgrRaw);
    }

    /// <summary>
    ///     Parses private SGR sequence from CSI parameters and creates an SgrSequence.
    ///     Used for private SGR sequences with ? prefix (e.g., CSI ? 4 m).
    /// </summary>
    /// <param name="parameters">The CSI parameters</param>
    /// <param name="raw">The raw sequence string</param>
    /// <returns>The parsed private SGR sequence</returns>
    public SgrSequence ParsePrivateSgrFromCsi(int[] parameters, string raw)
    {
        // Create a private SGR sequence by converting CSI parameters to private SGR format
        // Private SGR sequences have the ? prefix
        var sgrParser = new Parsing.SgrParser();
        
        // Convert parameters to byte array for SGR parser
        // The SGR parser expects the full escape sequence with ? prefix
        string sgrRaw = $"\x1b[?{string.Join(";", parameters)}m";
        byte[] sgrBytes = System.Text.Encoding.UTF8.GetBytes(sgrRaw);
        
        return sgrParser.ParseSgrSequence(sgrBytes, sgrRaw);
    }

    /// <summary>
    ///     Parses SGR sequence with intermediate characters from CSI parameters and creates an SgrSequence.
    ///     Used for SGR sequences with intermediate characters (e.g., CSI 0 % m).
    /// </summary>
    /// <param name="parameters">The CSI parameters</param>
    /// <param name="intermediate">The intermediate character string</param>
    /// <param name="raw">The raw sequence string</param>
    /// <returns>The parsed SGR sequence with intermediate characters</returns>
    public SgrSequence ParseSgrWithIntermediateFromCsi(int[] parameters, string intermediate, string raw)
    {
        // Create an SGR sequence with intermediate characters by converting CSI parameters to SGR format
        // SGR sequences with intermediate characters have the format: CSI Ps <intermediate> m
        var sgrParser = new Parsing.SgrParser();
        
        // Convert parameters to byte array for SGR parser
        // The SGR parser expects the full escape sequence with intermediate characters
        string sgrRaw = $"\x1b[{string.Join(";", parameters)}{intermediate}m";
        byte[] sgrBytes = System.Text.Encoding.UTF8.GetBytes(sgrRaw);
        
        return sgrParser.ParseSgrSequence(sgrBytes, sgrRaw);
    }

    /// <summary>
    ///     Applies multiple SGR attributes from an array of messages.
    /// </summary>
    /// <param name="current">The current SGR attributes</param>
    /// <param name="messages">The SGR messages to apply</param>
    /// <returns>The updated SGR attributes</returns>
    public SgrAttributes ApplyAttributes(SgrAttributes current, ReadOnlySpan<SgrMessage> messages)
    {
        var sgrParser = new Parsing.SgrParser();
        return sgrParser.ApplyAttributes(current, messages);
    }
}