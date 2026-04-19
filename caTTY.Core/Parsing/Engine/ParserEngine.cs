using Microsoft.Extensions.Logging;

namespace caTTY.Core.Parsing.Engine;

/// <summary>
///     Core state machine engine for processing individual bytes through parser states.
///     Handles the main dispatch logic for routing bytes to appropriate state handlers.
/// </summary>
public class ParserEngine
{
    private readonly ParserEngineContext _context;
    private readonly ILogger _logger;
    private readonly Action<byte> _handleNormalState;
    private readonly Action<byte> _handleEscapeState;
    private readonly Action<byte> _handleCsiState;
    private readonly Action<byte> _handleOscState;
    private readonly Action<byte> _handleOscEscapeState;
    private readonly Action<byte> _handleDcsState;
    private readonly Action<byte> _handleDcsEscapeState;
    private readonly Action<byte> _handleControlStringState;
    private readonly Action<byte> _handleControlStringEscapeState;

    /// <summary>
    ///     Creates a new parser engine with the specified context and state handlers.
    /// </summary>
    /// <param name="context">Parser engine context for state tracking</param>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <param name="handleNormalState">Handler for normal state</param>
    /// <param name="handleEscapeState">Handler for escape state</param>
    /// <param name="handleCsiState">Handler for CSI state</param>
    /// <param name="handleOscState">Handler for OSC state</param>
    /// <param name="handleOscEscapeState">Handler for OSC escape state</param>
    /// <param name="handleDcsState">Handler for DCS state</param>
    /// <param name="handleDcsEscapeState">Handler for DCS escape state</param>
    /// <param name="handleControlStringState">Handler for control string state</param>
    /// <param name="handleControlStringEscapeState">Handler for control string escape state</param>
    public ParserEngine(
        ParserEngineContext context,
        ILogger logger,
        Action<byte> handleNormalState,
        Action<byte> handleEscapeState,
        Action<byte> handleCsiState,
        Action<byte> handleOscState,
        Action<byte> handleOscEscapeState,
        Action<byte> handleDcsState,
        Action<byte> handleDcsEscapeState,
        Action<byte> handleControlStringState,
        Action<byte> handleControlStringEscapeState)
    {
        _context = context;
        _logger = logger;
        _handleNormalState = handleNormalState;
        _handleEscapeState = handleEscapeState;
        _handleCsiState = handleCsiState;
        _handleOscState = handleOscState;
        _handleOscEscapeState = handleOscEscapeState;
        _handleDcsState = handleDcsState;
        _handleDcsEscapeState = handleDcsEscapeState;
        _handleControlStringState = handleControlStringState;
        _handleControlStringEscapeState = handleControlStringEscapeState;
    }

    /// <summary>
    ///     Main state machine processor for handling bytes based on current parser state.
    /// </summary>
    public void ProcessByte(byte b)
    {
        if (b > 255)
        {
            _logger.LogWarning("Ignoring out-of-range byte: {Byte}", b);
            return;
        }

        switch (_context.State)
        {
            case ParserState.Normal:
                _handleNormalState(b);
                break;
            case ParserState.Escape:
                _handleEscapeState(b);
                break;
            case ParserState.CsiEntry:
                _handleCsiState(b);
                break;
            case ParserState.Osc:
                _handleOscState(b);
                break;
            case ParserState.OscEscape:
                _handleOscEscapeState(b);
                break;
            case ParserState.Dcs:
                _handleDcsState(b);
                break;
            case ParserState.DcsEscape:
                _handleDcsEscapeState(b);
                break;
            case ParserState.ControlString:
                _handleControlStringState(b);
                break;
            case ParserState.ControlStringEscape:
                _handleControlStringEscapeState(b);
                break;
        }
    }
}
