namespace caTTY.Core.Parsing;

/// <summary>
///     Represents the current state of the escape sequence parser.
///     Based on the TypeScript implementation state machine.
/// </summary>
public enum ParserState
{
    /// <summary>
    ///     Normal text processing state - no escape sequence in progress.
    /// </summary>
    Normal,

    /// <summary>
    ///     ESC character received, waiting for next byte to determine sequence type.
    /// </summary>
    Escape,

    /// <summary>
    ///     CSI sequence in progress (ESC [ received).
    /// </summary>
    CsiEntry,

    /// <summary>
    ///     OSC sequence in progress (ESC ] received).
    /// </summary>
    Osc,

    /// <summary>
    ///     OSC sequence received ESC, checking for ST terminator (ESC \).
    /// </summary>
    OscEscape,

    /// <summary>
    ///     DCS sequence in progress (ESC P received).
    /// </summary>
    Dcs,

    /// <summary>
    ///     DCS sequence received ESC, checking for ST terminator (ESC \).
    /// </summary>
    DcsEscape,

    /// <summary>
    ///     Control string sequence in progress (SOS/PM/APC: ESC X/^/_).
    /// </summary>
    ControlString,

    /// <summary>
    ///     Control string sequence received ESC, checking for ST terminator (ESC \).
    /// </summary>
    ControlStringEscape
}
