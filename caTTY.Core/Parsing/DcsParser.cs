using System.Text;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Parsing;

/// <summary>
///     Parser for DCS (Device Control String) sequences.
///     Handles device control commands and parameter parsing.
/// </summary>
public class DcsParser : IDcsParser
{
    private readonly ILogger _logger;
    private readonly ICursorPositionProvider? _cursorPositionProvider;

    /// <summary>
    ///     Creates a new DCS parser.
    /// </summary>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <param name="cursorPositionProvider">Optional cursor position provider for tracing</param>
    public DcsParser(ILogger logger, ICursorPositionProvider? cursorPositionProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cursorPositionProvider = cursorPositionProvider;
    }

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
    public bool ProcessDcsByte(byte b, List<byte> escapeSequence, ref string? dcsCommand, 
        StringBuilder dcsParamBuffer, ref string[] dcsParameters, out DcsMessage? message)
    {
        message = null;

        // CAN (0x18) / SUB (0x1a) abort a control string per ECMA-48
        if (b == 0x18 || b == 0x1a)
        {
            return true; // Abort sequence
        }

        escapeSequence.Add(b);

        // Parse the DCS "function identifier" (params/intermediates/final) once
        if (dcsCommand == null)
        {
            // Final byte (0x40-0x7E) ends the identifier
            if (b >= 0x40 && b <= 0x7e)
            {
                dcsCommand = ((char)b).ToString();
                dcsParameters = dcsParamBuffer.Length == 0
                    ? Array.Empty<string>()
                    : dcsParamBuffer.ToString().Split(';');
                return false; // Continue parsing data
            }

            // Parameter bytes are typically 0x30-0x3F (digits and delimiters)
            // We keep it simple and buffer printable parameter bytes
            if (b >= 0x20 && b <= 0x3f)
            {
                dcsParamBuffer.Append((char)b);
            }

            return false; // Continue parsing
        }

        return false; // Continue parsing data
    }

    /// <summary>
    ///     Creates a DCS message from the parsed components.
    /// </summary>
    /// <param name="escapeSequence">The complete escape sequence</param>
    /// <param name="terminator">The sequence terminator</param>
    /// <param name="dcsCommand">The DCS command</param>
    /// <param name="dcsParameters">The DCS parameters</param>
    /// <returns>The parsed DCS message</returns>
    public DcsMessage CreateDcsMessage(List<byte> escapeSequence, string terminator, 
        string? dcsCommand, string[] dcsParameters)
    {
        string raw = BytesToString(escapeSequence);
        var message = new DcsMessage
        {
            Type = "dcs",
            Raw = raw,
            Terminator = terminator,
            Implemented = false,
            Command = dcsCommand ?? string.Empty,
            Parameters = dcsParameters
        };

        _logger.LogDebug("DCS ({Terminator}): {Raw}", terminator, raw);
        return message;
    }

    /// <summary>
    ///     Resets the DCS parser state.
    /// </summary>
    public void Reset()
    {
        // No internal state to reset in this implementation
    }

    /// <summary>
    ///     Converts a list of bytes to a string representation.
    /// </summary>
    private static string BytesToString(IEnumerable<byte> bytes)
    {
        return string.Concat(bytes.Select(b => (char)b));
    }
}