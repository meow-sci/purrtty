namespace caTTY.Core.Types;

/// <summary>
///     Represents a parsed CSI sequence message.
///     Based on the TypeScript CsiMessage type.
/// </summary>
public class CsiMessage
{
    /// <summary>
    ///     The type of CSI sequence.
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
    ///     The final byte of the CSI sequence.
    /// </summary>
    public byte FinalByte { get; set; }

    /// <summary>
    ///     Parsed parameters from the CSI sequence.
    /// </summary>
    public int[] Parameters { get; set; } = Array.Empty<int>();

    /// <summary>
    ///     Private mode prefix (e.g., '?' for DEC private modes).
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    ///     Intermediate characters in the sequence.
    /// </summary>
    public string? Intermediate { get; set; }

    /// <summary>
    ///     Whether this is a private mode sequence.
    /// </summary>
    public bool IsPrivate { get; set; }

    // Cursor-related properties
    /// <summary>
    ///     Count parameter for cursor movement, scrolling, etc.
    /// </summary>
    public int? Count { get; set; }

    /// <summary>
    ///     Row parameter for cursor positioning.
    /// </summary>
    public int? Row { get; set; }

    /// <summary>
    ///     Column parameter for cursor positioning.
    /// </summary>
    public int? Column { get; set; }

    /// <summary>
    ///     Cursor style for DECSCUSR.
    /// </summary>
    public int? CursorStyle { get; set; }

    // Mode-related properties
    /// <summary>
    ///     DEC private mode numbers.
    /// </summary>
    public int[]? DecModes { get; set; }

    /// <summary>
    ///     Enable/disable flag for mode commands.
    /// </summary>
    public bool? Enable { get; set; }

    /// <summary>
    ///     Mode value for various commands.
    /// </summary>
    public int? Mode { get; set; }

    /// <summary>
    ///     Protected flag for character protection.
    /// </summary>
    public bool? Protected { get; set; }

    // Scrolling properties
    /// <summary>
    ///     Number of lines for scrolling commands.
    /// </summary>
    public int? Lines { get; set; }

    /// <summary>
    ///     Top boundary for scroll region.
    /// </summary>
    public int? Top { get; set; }

    /// <summary>
    ///     Bottom boundary for scroll region.
    /// </summary>
    public int? Bottom { get; set; }

    // Window manipulation properties
    /// <summary>
    ///     Window manipulation operation code.
    /// </summary>
    public int? Operation { get; set; }

    /// <summary>
    ///     Additional parameters for window manipulation.
    /// </summary>
    public int[]? WindowParams { get; set; }
}
