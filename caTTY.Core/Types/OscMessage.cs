namespace caTTY.Core.Types;

/// <summary>
///     Represents a parsed OSC sequence message.
///     Based on the TypeScript OscMessage type.
/// </summary>
public class OscMessage
{
    /// <summary>
    ///     The type identifier for OSC messages.
    /// </summary>
    public string Type { get; set; } = "osc";

    /// <summary>
    ///     The raw sequence string.
    /// </summary>
    public string Raw { get; set; } = string.Empty;

    /// <summary>
    ///     The terminator used to end the OSC sequence.
    /// </summary>
    public string Terminator { get; set; } = string.Empty;

    /// <summary>
    ///     Whether this sequence type is implemented.
    /// </summary>
    public bool Implemented { get; set; }

    /// <summary>
    ///     The parsed xterm OSC message if this is a recognized xterm extension.
    /// </summary>
    public XtermOscMessage? XtermMessage { get; set; }
}
