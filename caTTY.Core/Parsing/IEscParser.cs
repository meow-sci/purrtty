using caTTY.Core.Types;

namespace caTTY.Core.Parsing;

/// <summary>
///     Interface for parsing ESC sequences (non-CSI escape sequences).
/// </summary>
public interface IEscParser
{
    /// <summary>
    ///     Processes a byte in the ESC sequence parsing state.
    /// </summary>
    /// <param name="b">The byte to process</param>
    /// <param name="escapeSequence">The current escape sequence buffer</param>
    /// <param name="message">The parsed ESC message if sequence is complete</param>
    /// <returns>True if the sequence is complete, false if more bytes are needed</returns>
    bool ProcessEscByte(byte b, List<byte> escapeSequence, out EscMessage? message);
}