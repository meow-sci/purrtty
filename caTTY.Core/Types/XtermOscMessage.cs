namespace caTTY.Core.Types;

/// <summary>
///     Represents a parsed xterm OSC extension message.
///     Based on the TypeScript XtermOscMessage type.
/// </summary>
public class XtermOscMessage
{
    /// <summary>
    ///     The type of xterm OSC message.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    ///     The raw sequence string.
    /// </summary>
    public string Raw { get; set; } = string.Empty;

    /// <summary>
    ///     The terminator used to end the OSC sequence.
    /// </summary>
    public string Terminator { get; set; } = string.Empty;

    /// <summary>
    ///     Whether this message type is implemented.
    /// </summary>
    public bool Implemented { get; set; }

    /// <summary>
    ///     The OSC command number.
    /// </summary>
    public int Command { get; set; }

    /// <summary>
    ///     The payload data for the OSC command.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    ///     The window title for title-setting commands (OSC 0, 2).
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    ///     The icon name for icon-setting commands (OSC 1).
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    ///     The clipboard data for clipboard commands (OSC 52).
    /// </summary>
    public string? ClipboardData { get; set; }

    /// <summary>
    ///     The hyperlink URL for hyperlink commands (OSC 8).
    /// </summary>
    public string? HyperlinkUrl { get; set; }
}
