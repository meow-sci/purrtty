using Microsoft.Extensions.Logging;
using caTTY.Core.Types;

namespace caTTY.Core.Parsing.Engine;

/// <summary>
///     Handles bytes in OSC (Operating System Command) state.
///     Processes OSC sequence bytes and dispatches complete sequences to handlers.
/// </summary>
public class OscStateHandler
{
    private readonly ILogger _logger;
    private readonly IOscParser _oscParser;
    private readonly IParserHandlers _handlers;
    private readonly Action<byte> _maybeEmitNormalByteDuringEscapeSequence;
    private readonly Action _resetEscapeState;

    /// <summary>
    ///     Creates a new OSC state handler.
    /// </summary>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <param name="oscParser">OSC sequence parser</param>
    /// <param name="handlers">Parser handlers for dispatching parsed sequences</param>
    /// <param name="maybeEmitNormalByteDuringEscapeSequence">Delegate to optionally emit normal bytes during escape sequences</param>
    /// <param name="resetEscapeState">Delegate to reset the escape state</param>
    public OscStateHandler(
        ILogger logger,
        IOscParser oscParser,
        IParserHandlers handlers,
        Action<byte> maybeEmitNormalByteDuringEscapeSequence,
        Action resetEscapeState)
    {
        _logger = logger;
        _oscParser = oscParser;
        _handlers = handlers;
        _maybeEmitNormalByteDuringEscapeSequence = maybeEmitNormalByteDuringEscapeSequence;
        _resetEscapeState = resetEscapeState;
    }

    /// <summary>
    ///     Handles bytes in OSC sequence state.
    /// </summary>
    public void HandleOscState(byte b, ParserEngineContext context)
    {
        // Allow UTF-8 bytes in OSC sequences
        // Only reject control characters (0x00-0x1F) except for BEL (0x07) and ESC (0x1b) which are terminators
        // Keep 1:1 behavior with the pre-refactor Parser: warn + optionally emit as normal byte, without altering OSC state.
        if (b < 0x20 && b != 0x07 && b != 0x1b)
        {
            _logger.LogWarning("OSC: control character byte 0x{Byte:X2}", b);
            _maybeEmitNormalByteDuringEscapeSequence(b);
            return;
        }

        if (_oscParser.ProcessOscByte(b, context.EscapeSequence, out OscMessage? message))
        {
            if (message != null)
            {
                // If we have a parsed xterm message, handle it specifically
                if (message.XtermMessage != null)
                {
                    _handlers.HandleXtermOsc(message.XtermMessage);
                }
                else
                {
                    _handlers.HandleOsc(message);
                }
            }
            _resetEscapeState();
            return;
        }

        if (b == 0x1b) // ESC
        {
            context.State = ParserState.OscEscape;
        }
    }

    /// <summary>
    ///     Handles bytes in OSC escape state (checking for ST terminator).
    /// </summary>
    public void HandleOscEscapeState(byte b, ParserEngineContext context)
    {
        if (_oscParser.ProcessOscEscapeByte(b, context.EscapeSequence, out OscMessage? message))
        {
            if (message != null)
            {
                // If we have a parsed xterm message, handle it specifically
                if (message.XtermMessage != null)
                {
                    _handlers.HandleXtermOsc(message.XtermMessage);
                }
                else
                {
                    _handlers.HandleOsc(message);
                }
            }
            _resetEscapeState();
            return;
        }

        // Continue OSC payload
        context.State = ParserState.Osc;
    }
}
