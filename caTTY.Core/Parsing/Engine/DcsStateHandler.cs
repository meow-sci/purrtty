using caTTY.Core.Tracing;
using caTTY.Core.Types;

namespace caTTY.Core.Parsing.Engine;

/// <summary>
///     Handles bytes in DCS (Device Control String) state.
///     Processes DCS sequence bytes and dispatches complete sequences to handlers.
/// </summary>
public class DcsStateHandler
{
    private readonly IDcsParser _dcsParser;
    private readonly IParserHandlers _handlers;
    private readonly ICursorPositionProvider? _cursorPositionProvider;
    private readonly Action _resetEscapeState;

    /// <summary>
    ///     Creates a new DCS state handler.
    /// </summary>
    /// <param name="dcsParser">DCS sequence parser</param>
    /// <param name="handlers">Parser handlers for dispatching parsed sequences</param>
    /// <param name="cursorPositionProvider">Optional cursor position provider for tracing</param>
    /// <param name="resetEscapeState">Delegate to reset the escape state</param>
    public DcsStateHandler(
        IDcsParser dcsParser,
        IParserHandlers handlers,
        ICursorPositionProvider? cursorPositionProvider,
        Action resetEscapeState)
    {
        _dcsParser = dcsParser;
        _handlers = handlers;
        _cursorPositionProvider = cursorPositionProvider;
        _resetEscapeState = resetEscapeState;
    }

    /// <summary>
    ///     Handles bytes in DCS sequence state.
    /// </summary>
    public void HandleDcsState(byte b, ParserEngineContext context)
    {
        if (_dcsParser.ProcessDcsByte(b, context.EscapeSequence, ref context.DcsCommand, context.DcsParamBuffer, ref context.DcsParameters, out DcsMessage? message))
        {
            // Sequence aborted (CAN/SUB)
            if (message == null)
            {
                _resetEscapeState();
                return;
            }
        }

        if (b == 0x1b) // ESC
        {
            context.State = ParserState.DcsEscape;
        }
    }

    /// <summary>
    ///     Handles bytes in DCS escape state (checking for ST terminator).
    /// </summary>
    public void HandleDcsEscapeState(byte b, ParserEngineContext context)
    {
        // We just saw an ESC while inside DCS. If next byte is "\" then it's ST terminator.
        // Otherwise, it was a literal ESC in the payload and we continue in DCS.
        context.EscapeSequence.Add(b);

        if (b == 0x5c) // \
        {
            FinishDcsSequence("ST", context);
            return;
        }

        // CAN/SUB should still abort even if we were in the ESC lookahead
        if (b == 0x18 || b == 0x1a)
        {
            _resetEscapeState();
            return;
        }

        context.State = ParserState.Dcs;
    }

    /// <summary>
    ///     Finishes a DCS sequence and sends it to the handler.
    /// </summary>
    public void FinishDcsSequence(string terminator, ParserEngineContext context)
    {
        DcsMessage message = _dcsParser.CreateDcsMessage(context.EscapeSequence, terminator, context.DcsCommand, context.DcsParameters);

        // Trace the DCS sequence with command, parameters, and data payload
        string? parametersString = context.DcsParameters.Length > 0 ? string.Join(";", context.DcsParameters) : null;
        TraceHelper.TraceDcsSequence(context.DcsCommand ?? string.Empty, parametersString, null, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);

        _handlers.HandleDcs(message);
        _resetEscapeState();
    }
}
