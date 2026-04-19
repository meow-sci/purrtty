using System.Text;

namespace caTTY.Core.Parsing.Engine;

/// <summary>
///     Holds the state for the parser engine state machine.
///     Manages buffers, state tracking, and DCS-related data.
/// </summary>
public class ParserEngineContext
{
    /// <summary>
    ///     Current parser state.
    /// </summary>
    public ParserState State = ParserState.Normal;

    /// <summary>
    ///     Buffer for accumulating CSI sequence bytes.
    /// </summary>
    public readonly StringBuilder CsiSequence = new();

    /// <summary>
    ///     Buffer for accumulating escape sequence bytes.
    /// </summary>
    public readonly List<byte> EscapeSequence = new();

    /// <summary>
    ///     Buffer for accumulating DCS parameter bytes.
    /// </summary>
    public readonly StringBuilder DcsParamBuffer = new();

    /// <summary>
    ///     Current DCS command string (field for ref access).
    /// </summary>
    public string? DcsCommand;

    /// <summary>
    ///     Parsed DCS parameters (field for ref access).
    /// </summary>
    public string[] DcsParameters = Array.Empty<string>();

    /// <summary>
    ///     Type of control string currently being parsed (SOS/PM/APC).
    /// </summary>
    public ControlStringKind? ControlStringKind;

    /// <summary>
    ///     Resets all state and buffers to initial values.
    /// </summary>
    public void Reset()
    {
        State = ParserState.Normal;
        EscapeSequence.Clear();
        CsiSequence.Clear();
        ControlStringKind = null;
        DcsCommand = null;
        DcsParamBuffer.Clear();
        DcsParameters = Array.Empty<string>();
    }
}
