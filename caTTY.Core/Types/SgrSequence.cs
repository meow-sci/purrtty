namespace caTTY.Core.Types;

/// <summary>
///     Represents a parsed SGR sequence with individual messages.
///     Based on the TypeScript SgrSequence type.
/// </summary>
public class SgrSequence
{
    /// <summary>
    ///     The type identifier for SGR sequences.
    /// </summary>
    public string Type { get; set; } = "sgr";

    /// <summary>
    ///     Whether all SGR messages in this sequence are implemented.
    /// </summary>
    public bool Implemented { get; set; }

    /// <summary>
    ///     The raw sequence string.
    /// </summary>
    public string Raw { get; set; } = string.Empty;

    /// <summary>
    ///     Individual SGR messages parsed from the sequence.
    /// </summary>
    public SgrMessage[] Messages { get; set; } = Array.Empty<SgrMessage>();
}

/// <summary>
///     Represents an individual SGR message within a sequence.
/// </summary>
public class SgrMessage
{
    /// <summary>
    ///     The type of SGR message.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    ///     Whether this SGR message type is implemented.
    /// </summary>
    public bool Implemented { get; set; }

    /// <summary>
    ///     Additional data specific to the SGR message type.
    /// </summary>
    public object? Data { get; set; }
}
