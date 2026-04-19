using caTTY.Core.Types;

namespace caTTY.Core.Parsing;

/// <summary>
///     Interface for SGR (Select Graphic Rendition) sequence parsing.
///     Handles parsing of CSI ... m sequences and parameter processing.
/// </summary>
public interface ISgrParser
{
    /// <summary>
    ///     Parses an SGR sequence from CSI parameters.
    /// </summary>
    /// <param name="escapeSequence">The raw escape sequence bytes</param>
    /// <param name="raw">The raw sequence string</param>
    /// <returns>The parsed SGR sequence with individual messages</returns>
    SgrSequence ParseSgrSequence(byte[] escapeSequence, string raw);

    /// <summary>
    ///     Parses SGR parameters with support for both semicolon and colon separators.
    /// </summary>
    /// <param name="parameterString">The parameter string to parse</param>
    /// <param name="parameters">The parsed parameters</param>
    /// <returns>True if parsing was successful</returns>
    bool TryParseParameters(ReadOnlySpan<char> parameterString, out int[] parameters);

    /// <summary>
    ///     Applies SGR attributes to the current state.
    /// </summary>
    /// <param name="current">The current SGR attributes</param>
    /// <param name="messages">The SGR messages to apply</param>
    /// <returns>The updated SGR attributes</returns>
    SgrAttributes ApplyAttributes(SgrAttributes current, ReadOnlySpan<SgrMessage> messages);
}