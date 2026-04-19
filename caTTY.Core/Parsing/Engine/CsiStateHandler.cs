using System.Text;
using Microsoft.Extensions.Logging;
using caTTY.Core.Types;

namespace caTTY.Core.Parsing.Engine;

/// <summary>
///     Handles bytes in CSI (Control Sequence Introducer) state.
///     Processes CSI sequence bytes and dispatches complete sequences to handlers.
/// </summary>
public class CsiStateHandler
{
    private readonly ILogger _logger;
    private readonly bool _processC0ControlsDuringEscapeSequence;
    private readonly ICsiParser _csiParser;
    private readonly ISgrParser _sgrParser;
    private readonly IParserHandlers _handlers;
    private readonly Func<byte, bool> _handleC0ExceptEscape;
    private readonly Action<byte> _maybeEmitNormalByteDuringEscapeSequence;
    private readonly Func<bool> _isRpcHandlingEnabled;
    private readonly Func<bool> _tryHandleRpcSequence;
    private readonly Action _resetEscapeState;

    /// <summary>
    ///     Creates a new CSI state handler.
    /// </summary>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <param name="processC0ControlsDuringEscapeSequence">Whether to process C0 controls during escape sequences</param>
    /// <param name="csiParser">CSI sequence parser</param>
    /// <param name="sgrParser">SGR sequence parser</param>
    /// <param name="handlers">Parser handlers for dispatching parsed sequences</param>
    /// <param name="handleC0ExceptEscape">Delegate to handle C0 control characters (except ESC), returns true if handled</param>
    /// <param name="maybeEmitNormalByteDuringEscapeSequence">Delegate to optionally emit normal bytes during escape sequences</param>
    /// <param name="isRpcHandlingEnabled">Delegate to check if RPC handling is enabled</param>
    /// <param name="tryHandleRpcSequence">Delegate to try handling RPC sequences</param>
    /// <param name="resetEscapeState">Delegate to reset the escape state</param>
    public CsiStateHandler(
        ILogger logger,
        bool processC0ControlsDuringEscapeSequence,
        ICsiParser csiParser,
        ISgrParser sgrParser,
        IParserHandlers handlers,
        Func<byte, bool> handleC0ExceptEscape,
        Action<byte> maybeEmitNormalByteDuringEscapeSequence,
        Func<bool> isRpcHandlingEnabled,
        Func<bool> tryHandleRpcSequence,
        Action resetEscapeState)
    {
        _logger = logger;
        _processC0ControlsDuringEscapeSequence = processC0ControlsDuringEscapeSequence;
        _csiParser = csiParser;
        _sgrParser = sgrParser;
        _handlers = handlers;
        _handleC0ExceptEscape = handleC0ExceptEscape;
        _maybeEmitNormalByteDuringEscapeSequence = maybeEmitNormalByteDuringEscapeSequence;
        _isRpcHandlingEnabled = isRpcHandlingEnabled;
        _tryHandleRpcSequence = tryHandleRpcSequence;
        _resetEscapeState = resetEscapeState;
    }

    /// <summary>
    ///     Handles bytes in CSI sequence state.
    /// </summary>
    public void HandleCsiState(byte b, ParserEngineContext context)
    {
        // Optional: still execute C0 controls while parsing CSI (common terminal behavior)
        if (b < 0x20 && b != 0x1b && _processC0ControlsDuringEscapeSequence && _handleC0ExceptEscape(b))
        {
            return;
        }

        HandleCsiByte(b, context);
    }

    /// <summary>
    ///     Handles bytes in CSI sequence, building the sequence until final byte.
    /// </summary>
    private void HandleCsiByte(byte b, ParserEngineContext context)
    {
        // Guard against bytes outside the allowed CSI byte range (0x20 - 0x7E)
        if (b < 0x20 || b > 0x7e)
        {
            _logger.LogWarning("CSI: byte out of range 0x{Byte:X2}", b);
            _maybeEmitNormalByteDuringEscapeSequence(b);
            return;
        }

        // Always add byte to the escape sequence and csi sequence
        context.CsiSequence.Append((char)b);
        context.EscapeSequence.Add(b);

        // CSI final bytes are 0x40-0x7E
        if (b >= 0x40 && b <= 0x7e)
        {
            FinishCsiSequence(context);
        }
    }

    /// <summary>
    ///     Finishes a CSI sequence and sends it to the handler.
    /// </summary>
    private void FinishCsiSequence(ParserEngineContext context)
    {
        string raw = BytesToString(context.EscapeSequence);
        byte finalByte = context.EscapeSequence[^1];

        // Check for RPC sequences first (ESC [ > format) if RPC handling is enabled
        if (_isRpcHandlingEnabled() && _tryHandleRpcSequence())
        {
            _resetEscapeState();
            return;
        }

        // CSI SGR: parse using the dedicated SGR parser
        if (finalByte == 0x6d) // 'm'
        {
            SgrSequence sgrSequence = _sgrParser.ParseSgrSequence(context.EscapeSequence.ToArray(), raw);
            _handlers.HandleSgr(sgrSequence);
        }
        else
        {
            // Parse CSI sequence using the dedicated CSI parser
            CsiMessage message = _csiParser.ParseCsiSequence(context.EscapeSequence.ToArray(), raw);
            _handlers.HandleCsi(message);
        }

        _resetEscapeState();
    }

    /// <summary>
    ///     Converts a list of bytes to a string representation.
    /// </summary>
    private static string BytesToString(IEnumerable<byte> bytes)
    {
        return string.Concat(bytes.Select(b => (char)b));
    }
}
