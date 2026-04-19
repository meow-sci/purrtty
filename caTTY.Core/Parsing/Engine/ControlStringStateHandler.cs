using Microsoft.Extensions.Logging;

namespace caTTY.Core.Parsing.Engine;

/// <summary>
///     Handles bytes in control string state (SOS/PM/APC).
///     Processes control string sequence bytes and handles termination.
/// </summary>
public class ControlStringStateHandler
{
    private readonly ILogger _logger;
    private readonly Action _resetEscapeState;

    /// <summary>
    ///     Creates a new control string state handler.
    /// </summary>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <param name="resetEscapeState">Delegate to reset the escape state</param>
    public ControlStringStateHandler(
        ILogger logger,
        Action resetEscapeState)
    {
        _logger = logger;
        _resetEscapeState = resetEscapeState;
    }

    /// <summary>
    ///     Handles bytes in control string state (SOS/PM/APC).
    /// </summary>
    public void HandleControlStringState(byte b, ParserEngineContext context)
    {
        // CAN (0x18) / SUB (0x1a) abort a control string per ECMA-48
        if (b == 0x18 || b == 0x1a)
        {
            _resetEscapeState();
            return;
        }

        context.EscapeSequence.Add(b);

        if (b == 0x1b) // ESC
        {
            context.State = ParserState.ControlStringEscape;
        }
    }

    /// <summary>
    ///     Handles bytes in control string escape state (checking for ST terminator).
    /// </summary>
    public void HandleControlStringEscapeState(byte b, ParserEngineContext context)
    {
        context.EscapeSequence.Add(b);

        if (b == 0x5c) // \
        {
            // ST terminator
            string raw = BytesToString(context.EscapeSequence);
            string kind = context.ControlStringKind?.ToString().ToUpperInvariant() ?? "STR";
            _logger.LogDebug("{Kind} (ST): {Raw}", kind, raw);
            _resetEscapeState();
            return;
        }

        if (b == 0x18 || b == 0x1a) // CAN/SUB
        {
            _resetEscapeState();
            return;
        }

        context.State = ParserState.ControlString;
    }

    /// <summary>
    ///     Converts a list of bytes to a string representation.
    /// </summary>
    private static string BytesToString(IEnumerable<byte> bytes)
    {
        return string.Concat(bytes.Select(b => (char)b));
    }
}
