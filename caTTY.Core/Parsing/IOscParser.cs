using caTTY.Core.Types;

namespace caTTY.Core.Parsing;

/// <summary>
///     Interface for parsing OSC (Operating System Command) sequences.
/// </summary>
public interface IOscParser
{
    /// <summary>
    ///     Processes a byte in the OSC sequence parsing state.
    /// </summary>
    /// <param name="b">The byte to process</param>
    /// <param name="escapeSequence">The current escape sequence buffer</param>
    /// <param name="message">The parsed OSC message if sequence is complete</param>
    /// <returns>True if the sequence is complete, false if more bytes are needed</returns>
    bool ProcessOscByte(byte b, List<byte> escapeSequence, out OscMessage? message);

    /// <summary>
    ///     Processes a byte in the OSC escape state (checking for ST terminator).
    /// </summary>
    /// <param name="b">The byte to process</param>
    /// <param name="escapeSequence">The current escape sequence buffer</param>
    /// <param name="message">The parsed OSC message if sequence is complete</param>
    /// <returns>True if the sequence is complete, false if more bytes are needed</returns>
    bool ProcessOscEscapeByte(byte b, List<byte> escapeSequence, out OscMessage? message);
}