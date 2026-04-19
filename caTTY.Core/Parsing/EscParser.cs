using caTTY.Core.Types;
using caTTY.Core.Tracing;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Parsing;

/// <summary>
///     Parser for ESC sequences (non-CSI escape sequences).
///     Handles save/restore cursor, character set designation, and other ESC sequences.
/// </summary>
public class EscParser : IEscParser
{
    private readonly ILogger _logger;
    private readonly ICursorPositionProvider? _cursorPositionProvider;

    /// <summary>
    ///     Creates a new ESC parser.
    /// </summary>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <param name="cursorPositionProvider">Optional cursor position provider for tracing</param>
    public EscParser(ILogger logger, ICursorPositionProvider? cursorPositionProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cursorPositionProvider = cursorPositionProvider;
    }

    /// <summary>
    ///     Processes a byte in the ESC sequence parsing state.
    /// </summary>
    /// <param name="b">The byte to process</param>
    /// <param name="escapeSequence">The current escape sequence buffer</param>
    /// <param name="message">The parsed ESC message if sequence is complete</param>
    /// <returns>True if the sequence is complete, false if more bytes are needed</returns>
    public bool ProcessEscByte(byte b, List<byte> escapeSequence, out EscMessage? message)
    {
        message = null;

        // Guard against bytes outside the allowed ESC byte range (0x20 - 0x7E)
        if (b < 0x20 || b > 0x7e)
        {
            _logger.LogWarning("ESC: byte out of range 0x{Byte:X2}", b);
            return true; // Complete the sequence with error
        }

        // First byte after ESC decides the submode
        if (escapeSequence.Count == 1)
        {
            // Character set designation sequences need one more byte
            if (b == 0x28 || b == 0x29 || b == 0x2a || b == 0x2b)
            {
                escapeSequence.Add(b);
                return false; // Need one more byte
            }

            // Handle single-byte ESC sequences
            message = HandleSingleByteEscSequence(b, escapeSequence);
            if (message != null)
            {
                // Trace the complete ESC sequence
                string escSequence = BytesToString(escapeSequence.Skip(1)); // Skip the ESC byte
                TraceHelper.TraceEscSequence(escSequence, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
                return true; // Sequence complete
            }

            // Unknown single-byte ESC sequence (byte already added by HandleSingleByteEscSequence)
            string raw = BytesToString(escapeSequence);
            string unknownSequence = BytesToString(escapeSequence.Skip(1)); // Skip the ESC byte
            TraceHelper.TraceEscSequence(unknownSequence, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
            _logger.LogDebug("ESC (opaque): {Raw}", raw);
            return true; // Complete unknown sequence
        }

        // Handle multi-byte ESC sequences (like character set designation)
        return HandleMultiByteEscSequence(b, escapeSequence, out message);
    }

    /// <summary>
    ///     Handles single-byte ESC sequences that complete immediately.
    /// </summary>
    private static EscMessage? HandleSingleByteEscSequence(byte b, List<byte> escapeSequence)
    {
        escapeSequence.Add(b);
        string raw = BytesToString(escapeSequence);

        return b switch
        {
            0x37 => new EscMessage { Type = "esc.saveCursor", Raw = raw, Implemented = true }, // ESC 7
            0x38 => new EscMessage { Type = "esc.restoreCursor", Raw = raw, Implemented = true }, // ESC 8
            0x4d => new EscMessage { Type = "esc.reverseIndex", Raw = raw, Implemented = true }, // ESC M
            0x44 => new EscMessage { Type = "esc.index", Raw = raw, Implemented = true }, // ESC D
            0x45 => new EscMessage { Type = "esc.nextLine", Raw = raw, Implemented = true }, // ESC E
            0x48 => new EscMessage { Type = "esc.horizontalTabSet", Raw = raw, Implemented = true }, // ESC H
            0x63 => new EscMessage { Type = "esc.resetToInitialState", Raw = raw, Implemented = true }, // ESC c
            _ => null
        };
    }

    /// <summary>
    ///     Handles multi-byte ESC sequences like character set designation.
    /// </summary>
    private bool HandleMultiByteEscSequence(byte b, List<byte> escapeSequence, out EscMessage? message)
    {
        message = null;

        // Second byte after ESC for character set designation
        if (escapeSequence.Count == 2)
        {
            byte firstByte = escapeSequence[1];
            if (firstByte == 0x28 || firstByte == 0x29 || firstByte == 0x2a || firstByte == 0x2b)
            {
                escapeSequence.Add(b);
                string raw = BytesToString(escapeSequence);

                // Determine which G slot is being designated
                string slot = firstByte switch
                {
                    0x28 => "G0",
                    0x29 => "G1",
                    0x2a => "G2",
                    0x2b => "G3",
                    _ => "G0"
                };

                string charset = ((char)b).ToString();
                message = new EscMessage
                {
                    Type = "esc.designateCharacterSet",
                    Raw = raw,
                    Slot = slot,
                    Charset = charset,
                    Implemented = true
                };

                // Trace the complete ESC sequence
                string escSequence = BytesToString(escapeSequence.Skip(1)); // Skip the ESC byte
                TraceHelper.TraceEscSequence(escSequence, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);

                return true; // Sequence complete
            }
        }

        escapeSequence.Add(b);

        // Basic ESC sequence: intermediates 0x20-0x2F, final 0x30-0x7E
        if (b >= 0x30 && b <= 0x7e)
        {
            string raw = BytesToString(escapeSequence);
            string escSequence = BytesToString(escapeSequence.Skip(1)); // Skip the ESC byte
            TraceHelper.TraceEscSequence(escSequence, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
            _logger.LogDebug("ESC (opaque): {Raw}", raw);
            return true; // Sequence complete
        }

        return false; // Need more bytes
    }

    /// <summary>
    ///     Converts a list of bytes to a string representation.
    /// </summary>
    private static string BytesToString(IEnumerable<byte> bytes)
    {
        return string.Concat(bytes.Select(b => (char)b));
    }
}