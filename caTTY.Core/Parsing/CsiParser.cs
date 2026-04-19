using caTTY.Core.Types;
using caTTY.Core.Tracing;
using caTTY.Core.Parsing.Csi;

namespace caTTY.Core.Parsing;

/// <summary>
///     CSI (Control Sequence Introducer) sequence parser.
///     Handles parameter parsing, prefix detection, and command identification.
///     Based on the TypeScript ParseCsi.ts implementation.
/// </summary>
public class CsiParser : ICsiParser
{
    private readonly ICursorPositionProvider? _cursorPositionProvider;
    private readonly CsiTokenizer _tokenizer;
    private readonly CsiMessageFactory _messageFactory;

    /// <summary>
    /// Creates a new CSI parser.
    /// </summary>
    /// <param name="cursorPositionProvider">Optional cursor position provider for tracing</param>
    public CsiParser(ICursorPositionProvider? cursorPositionProvider = null)
    {
        _cursorPositionProvider = cursorPositionProvider;
        _tokenizer = new CsiTokenizer();
        _messageFactory = new CsiMessageFactory(_tokenizer);
    }
    /// <summary>
    ///     Parses a complete CSI sequence from the provided bytes.
    /// </summary>
    /// <param name="sequence">The complete CSI sequence bytes (including ESC [)</param>
    /// <param name="raw">The raw string representation of the sequence</param>
    /// <returns>A parsed CSI message with parameters and command information</returns>
    public CsiMessage ParseCsiSequence(ReadOnlySpan<byte> sequence, string raw)
    {
        // Tokenize the CSI sequence
        var tokenResult = _tokenizer.Tokenize(sequence);

        if (sequence.Length < 3) // Minimum: ESC [ final
        {
            return _messageFactory.BuildMessage(0, "", Array.Empty<int>(), false, null, "", raw);
        }

        // Build message using factory
        var result = _messageFactory.BuildMessage(tokenResult.FinalByte, tokenResult.Final, tokenResult.Parameters,
            tokenResult.IsPrivate, tokenResult.Prefix, tokenResult.Intermediate, raw);

        // Trace the parsed CSI sequence
        TraceHelper.TraceCsiSequence((char)tokenResult.FinalByte, tokenResult.ParameterText,
            tokenResult.Prefix?.FirstOrDefault(), TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);

        return result;
    }

    /// <summary>
    ///     Attempts to parse CSI parameters from a parameter string.
    /// </summary>
    /// <param name="parameterString">The parameter portion of the CSI sequence</param>
    /// <param name="parameters">The parsed numeric parameters</param>
    /// <param name="isPrivate">True if the sequence has a '?' prefix</param>
    /// <param name="prefix">The prefix character ('>' or null)</param>
    /// <returns>True if parsing was successful</returns>
    public bool TryParseParameters(ReadOnlySpan<char> parameterString, out int[] parameters, out bool isPrivate,
        out string? prefix)
    {
        return _tokenizer.TryParseParameters(parameterString, out parameters, out isPrivate, out prefix);
    }

    /// <summary>
    ///     Gets a parameter value with a fallback default.
    /// </summary>
    /// <param name="parameters">The parameter array</param>
    /// <param name="index">The parameter index</param>
    /// <param name="fallback">The default value if parameter is missing</param>
    /// <returns>The parameter value or fallback</returns>
    public int GetParameter(int[] parameters, int index, int fallback)
    {
        return CsiParamParsers.GetParameter(parameters, index, fallback);
    }

}
