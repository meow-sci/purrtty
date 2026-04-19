namespace caTTY.Core.Types;

/// <summary>
///     Represents a parsed DCS sequence message.
///     Based on the TypeScript DcsMessage type.
/// </summary>
public class DcsMessage
{
    /// <summary>
    ///     The type identifier for DCS messages.
    /// </summary>
    public string Type { get; set; } = "dcs";

    /// <summary>
    ///     The raw sequence string.
    /// </summary>
    public string Raw { get; set; } = string.Empty;

    /// <summary>
    ///     The terminator used to end the DCS sequence.
    /// </summary>
    public string Terminator { get; set; } = string.Empty;

    /// <summary>
    ///     Whether this sequence type is implemented.
    /// </summary>
    public bool Implemented { get; set; }

    /// <summary>
    ///     The DCS command character.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    ///     Parsed parameters from the DCS sequence.
    /// </summary>
    public string[] Parameters { get; set; } = Array.Empty<string>();
}
