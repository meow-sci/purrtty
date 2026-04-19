namespace caTTY.Core.Types;

/// <summary>
///     Represents a parsed ESC sequence message.
///     Based on the TypeScript EscMessage type.
/// </summary>
public class EscMessage
{
    /// <summary>
    ///     The type of ESC sequence.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    ///     The raw sequence string.
    /// </summary>
    public string Raw { get; set; } = string.Empty;

    /// <summary>
    ///     Whether this sequence type is implemented.
    /// </summary>
    public bool Implemented { get; set; }

    /// <summary>
    ///     For character set designation sequences, the G slot being designated.
    /// </summary>
    public string? Slot { get; set; }

    /// <summary>
    ///     For character set designation sequences, the character set identifier.
    /// </summary>
    public string? Charset { get; set; }
}
