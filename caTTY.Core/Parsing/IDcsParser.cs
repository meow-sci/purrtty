using System.Text;
using caTTY.Core.Types;

namespace caTTY.Core.Parsing;

/// <summary>
///     Interface for parsing DCS (Device Control String) sequences.
/// </summary>
public interface IDcsParser
{
    /// <summary>
    ///     Processes a byte in the DCS sequence parsing state.
    /// </summary>
    /// <param name="b">The byte to process</param>
    /// <param name="escapeSequence">The current escape sequence buffer</param>
    /// <param name="dcsCommand">The current DCS command being parsed</param>
    /// <param name="dcsParamBuffer">The parameter buffer for DCS parsing</param>
    /// <param name="dcsParameters">The parsed DCS parameters</param>
    /// <param name="message">The parsed DCS message if sequence is complete</param>
    /// <returns>True if the sequence is complete, false if more bytes are needed</returns>
    bool ProcessDcsByte(byte b, List<byte> escapeSequence, ref string? dcsCommand, 
        StringBuilder dcsParamBuffer, ref string[] dcsParameters, out DcsMessage? message);

    /// <summary>
    ///     Resets the DCS parser state.
    /// </summary>
    void Reset();

    /// <summary>
    ///     Creates a DCS message from the parsed components.
    /// </summary>
    /// <param name="escapeSequence">The complete escape sequence</param>
    /// <param name="terminator">The sequence terminator</param>
    /// <param name="dcsCommand">The DCS command</param>
    /// <param name="dcsParameters">The DCS parameters</param>
    /// <returns>The parsed DCS message</returns>
    DcsMessage CreateDcsMessage(List<byte> escapeSequence, string terminator, 
        string? dcsCommand, string[] dcsParameters);
}