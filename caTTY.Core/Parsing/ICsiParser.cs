using caTTY.Core.Types;

namespace caTTY.Core.Parsing;

/// <summary>
///     Interface for CSI (Control Sequence Introducer) sequence parsing.
///     Handles CSI parameter extraction and command identification.
/// </summary>
public interface ICsiParser
{
    /// <summary>
    ///     Parses a complete CSI sequence from the provided bytes.
    /// </summary>
    /// <param name="sequence">The complete CSI sequence bytes (including ESC [)</param>
    /// <param name="raw">The raw string representation of the sequence</param>
    /// <returns>A parsed CSI message with parameters and command information</returns>
    CsiMessage ParseCsiSequence(ReadOnlySpan<byte> sequence, string raw);

    /// <summary>
    ///     Attempts to parse CSI parameters from a parameter string.
    /// </summary>
    /// <param name="parameterString">The parameter portion of the CSI sequence</param>
    /// <param name="parameters">The parsed numeric parameters</param>
    /// <param name="isPrivate">True if the sequence has a '?' prefix</param>
    /// <param name="prefix">The prefix character ('>' or null)</param>
    /// <returns>True if parsing was successful</returns>
    bool TryParseParameters(ReadOnlySpan<char> parameterString, out int[] parameters, out bool isPrivate,
        out string? prefix);

    /// <summary>
    ///     Gets a parameter value with a fallback default.
    /// </summary>
    /// <param name="parameters">The parameter array</param>
    /// <param name="index">The parameter index</param>
    /// <param name="fallback">The default value if parameter is missing</param>
    /// <returns>The parameter value or fallback</returns>
    int GetParameter(int[] parameters, int index, int fallback);
}
