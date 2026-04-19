using System.Text;
using caTTY.Core.Types;
using caTTY.Core.Tracing;
using caTTY.Core.Rpc;
using caTTY.Core.Parsing.Engine;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Parsing;

/// <summary>
///     Escape sequence parser state machine for terminal emulation.
///     Coordinates between specialized parsers for different sequence types.
///     Based on the TypeScript Parser implementation with identical state transitions and sequence detection.
/// </summary>
public class Parser
{
    private readonly ICsiParser _csiParser;
    private readonly IEscParser _escParser;
    private readonly IDcsParser _dcsParser;
    private readonly IOscParser _oscParser;
    private readonly ISgrParser _sgrParser;
    private readonly bool _emitNormalBytesDuringEscapeSequence;
    private readonly IParserHandlers _handlers;
    private readonly ILogger _logger;
    private readonly bool _processC0ControlsDuringEscapeSequence;
    private readonly ICursorPositionProvider? _cursorPositionProvider;

    // UTF-8 decoding
    private readonly IUtf8Decoder _utf8Decoder;

    // Parser engine context
    private readonly ParserEngineContext _context = new();

    // State handlers
    private readonly NormalStateHandler _normalStateHandler;
    private readonly EscapeStateHandler _escapeStateHandler;
    private readonly CsiStateHandler _csiStateHandler;
    private readonly OscStateHandler _oscStateHandler;
    private readonly DcsStateHandler _dcsStateHandler;
    private readonly ControlStringStateHandler _controlStringStateHandler;
    private readonly RpcSequenceHandler _rpcSequenceHandler;

    // Parser engine
    private readonly ParserEngine _engine;

    /// <summary>
    ///     Creates a new parser with the specified options.
    /// </summary>
    /// <param name="options">Parser configuration options</param>
    public Parser(ParserOptions options)
    {
        _handlers = options.Handlers ?? throw new ArgumentNullException(nameof(options.Handlers));
        _logger = options.Logger ?? throw new ArgumentNullException(nameof(options.Logger));
        _emitNormalBytesDuringEscapeSequence = options.EmitNormalBytesDuringEscapeSequence;
        _processC0ControlsDuringEscapeSequence = options.ProcessC0ControlsDuringEscapeSequence;
        _cursorPositionProvider = options.CursorPositionProvider;
        _utf8Decoder = options.Utf8Decoder ?? new Utf8Decoder();
        _csiParser = options.CsiParser ?? new CsiParser(options.CursorPositionProvider);
        _escParser = options.EscParser ?? new EscParser(_logger, options.CursorPositionProvider);
        _dcsParser = options.DcsParser ?? new DcsParser(_logger, options.CursorPositionProvider);
        _oscParser = options.OscParser ?? new OscParser(_logger, options.CursorPositionProvider);
        _sgrParser = options.SgrParser ?? new SgrParser(_logger, options.CursorPositionProvider);

        // Initialize RPC sequence handler with optional RPC components
        _rpcSequenceHandler = new RpcSequenceHandler(
            options.RpcSequenceDetector,
            options.RpcSequenceParser,
            options.RpcHandler);

        // Initialize state handlers
        _normalStateHandler = new NormalStateHandler(
            _cursorPositionProvider,
            HandleC0ExceptEscape,
            StartEscapeSequence,
            HandleNormalByte);

        _escapeStateHandler = new EscapeStateHandler(
            _processC0ControlsDuringEscapeSequence,
            HandleC0ExceptEscape,
            HandleEscapeByte);

        _csiStateHandler = new CsiStateHandler(
            _logger,
            _processC0ControlsDuringEscapeSequence,
            _csiParser,
            _sgrParser,
            _handlers,
            HandleC0ExceptEscape,
            MaybeEmitNormalByteDuringEscapeSequence,
            IsRpcHandlingEnabled,
            TryHandleRpcSequence,
            ResetEscapeState);

        _oscStateHandler = new OscStateHandler(
            _logger,
            _oscParser,
            _handlers,
            MaybeEmitNormalByteDuringEscapeSequence,
            ResetEscapeState);

        _dcsStateHandler = new DcsStateHandler(
            _dcsParser,
            _handlers,
            _cursorPositionProvider,
            ResetEscapeState);

        _controlStringStateHandler = new ControlStringStateHandler(
            _logger,
            ResetEscapeState);

        // Initialize parser engine with state handlers
        _engine = new ParserEngine(
            _context,
            _logger,
            HandleNormalState,
            HandleEscapeState,
            HandleCsiState,
            HandleOscState,
            HandleOscEscapeState,
            HandleDcsState,
            HandleDcsEscapeState,
            HandleControlStringState,
            HandleControlStringEscapeState);
    }

    /// <summary>
    ///     Processes a span of bytes through the parser state machine.
    /// </summary>
    /// <param name="data">The byte data to process</param>
    public void PushBytes(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            PushByte(b);
        }
    }

    /// <summary>
    ///     Flushes any incomplete UTF-8 sequences at the end of input.
    ///     This should be called when no more input is expected to ensure
    ///     incomplete sequences are handled gracefully.
    /// </summary>
    public void FlushIncompleteSequences()
    {
        if (_utf8Decoder.FlushIncompleteSequence(out ReadOnlySpan<byte> invalidBytes))
        {
            // Send each invalid byte as a separate character
            foreach (byte b in invalidBytes)
            {
                _handlers.HandleNormalByte(b);
            }
        }
    }

    /// <summary>
    ///     Processes a single byte through the parser state machine.
    /// </summary>
    /// <param name="data">The byte to process</param>
    public void PushByte(byte data)
    {
        ProcessByte(data);
    }

    /// <summary>
    ///     Main state machine processor for handling bytes based on current parser state.
    ///     Delegates to the parser engine for byte processing.
    /// </summary>
    private void ProcessByte(byte b)
    {
        _engine.ProcessByte(b);
    }

    /// <summary>
    ///     Handles bytes in normal text processing state.
    ///     Delegates to NormalStateHandler.
    /// </summary>
    private void HandleNormalState(byte b)
    {
        _normalStateHandler.HandleNormalState(b);
    }

    /// <summary>
    ///     Handles bytes in escape sequence state.
    ///     Delegates to EscapeStateHandler.
    /// </summary>
    private void HandleEscapeState(byte b)
    {
        _escapeStateHandler.HandleEscapeState(b);
    }

    /// <summary>
    ///     Handles bytes in CSI sequence state.
    ///     Delegates to CsiStateHandler.
    /// </summary>
    private void HandleCsiState(byte b)
    {
        _csiStateHandler.HandleCsiState(b, _context);
    }

    /// <summary>
    ///     Handles bytes in OSC sequence state.
    ///     Delegates to OscStateHandler.
    /// </summary>
    private void HandleOscState(byte b)
    {
        _oscStateHandler.HandleOscState(b, _context);
    }

    /// <summary>
    ///     Handles bytes in OSC escape state (checking for ST terminator).
    ///     Delegates to OscStateHandler.
    /// </summary>
    private void HandleOscEscapeState(byte b)
    {
        _oscStateHandler.HandleOscEscapeState(b, _context);
    }

    /// <summary>
    ///     Handles bytes in DCS sequence state.
    ///     Delegates to DcsStateHandler.
    /// </summary>
    private void HandleDcsState(byte b)
    {
        _dcsStateHandler.HandleDcsState(b, _context);
    }

    /// <summary>
    ///     Handles bytes in DCS escape state (checking for ST terminator).
    ///     Delegates to DcsStateHandler.
    /// </summary>
    private void HandleDcsEscapeState(byte b)
    {
        _dcsStateHandler.HandleDcsEscapeState(b, _context);
    }

    /// <summary>
    ///     Handles bytes in control string state (SOS/PM/APC).
    ///     Delegates to ControlStringStateHandler.
    /// </summary>
    private void HandleControlStringState(byte b)
    {
        _controlStringStateHandler.HandleControlStringState(b, _context);
    }

    /// <summary>
    ///     Handles bytes in control string escape state (checking for ST terminator).
    ///     Delegates to ControlStringStateHandler.
    /// </summary>
    private void HandleControlStringEscapeState(byte b)
    {
        _controlStringStateHandler.HandleControlStringEscapeState(b, _context);
    }


    /// <summary>
    ///     Handles ESC byte, accumulating into escape sequence and determining next state.
    /// </summary>
    private void HandleEscapeByte(byte b)
    {
        // Guard against bytes outside the allowed ESC byte range (0x20 - 0x7E)
        // Keep 1:1 behavior with the pre-refactor Parser: warn + optionally emit as normal byte, without resetting ESC state.
        if (b < 0x20 || b > 0x7e)
        {
            _logger.LogWarning("ESC: byte out of range 0x{Byte:X2}", b);
            MaybeEmitNormalByteDuringEscapeSequence(b);
            return;
        }

        // First byte after ESC decides the submode
        if (_context.EscapeSequence.Count == 1)
        {
            if (b == 0x5b) // [
            {
                _context.EscapeSequence.Add(b);
                _context.CsiSequence.Clear();
                _context.State = ParserState.CsiEntry;
                return;
            }

            if (b == 0x5d) // ]
            {
                _context.EscapeSequence.Add(b);
                _context.State = ParserState.Osc;
                return;
            }

            // DCS: ESC P ... ST
            if (b == 0x50) // P
            {
                _context.EscapeSequence.Add(b);
                _context.DcsCommand = null;
                _context.DcsParamBuffer.Clear();
                _context.DcsParameters = Array.Empty<string>();
                _dcsParser.Reset();
                _context.State = ParserState.Dcs;
                return;
            }

            // SOS / PM / APC: ESC X / ESC ^ / ESC _ ... ST
            if (b == 0x58 || b == 0x5e || b == 0x5f) // X, ^, _
            {
                _context.EscapeSequence.Add(b);
                _context.ControlStringKind = b switch
                {
                    0x58 => Parsing.ControlStringKind.Sos,
                    0x5e => Parsing.ControlStringKind.Pm,
                    0x5f => Parsing.ControlStringKind.Apc,
                    _ => null
                };
                _context.State = ParserState.ControlString;
                return;
            }
        }

        // Use the specialized ESC parser for all other ESC sequences
        if (_escParser.ProcessEscByte(b, _context.EscapeSequence, out EscMessage? message))
        {
            if (message != null)
            {
                _handlers.HandleEsc(message);
            }
            ResetEscapeState();
        }
    }

    /// <summary>
    ///     Handles normal printable bytes and UTF-8 sequences.
    /// </summary>
    private void HandleNormalByte(byte b)
    {
        // Use the dedicated UTF-8 decoder
        if (_utf8Decoder.ProcessByte(b, out int codePoint))
        {
            // Trace the decoded UTF-8 character
            if (TerminalTracer.Enabled) {
              string decodedText = char.ConvertFromUtf32(codePoint);
              int? row = _cursorPositionProvider?.Row;
              int? col = _cursorPositionProvider?.Column;
              TraceHelper.TraceUtf8Text(decodedText, TraceDirection.Output, row, col);
            }
            _handlers.HandleNormalByte(codePoint);
        }
    }

    /// <summary>
    ///     Resets the parser to normal state and clears all buffers.
    /// </summary>
    private void ResetEscapeState()
    {
        _context.Reset();
        _dcsParser.Reset();
    }

    /// <summary>
    ///     Handles C0 control characters except ESC.
    /// </summary>
    private bool HandleC0ExceptEscape(byte b)
    {
        switch (b)
        {
            case 0x07: // Bell
                TraceHelper.TraceControlChar(b, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
                _handlers.HandleBell();
                return true;
            case 0x08: // Backspace
                TraceHelper.TraceControlChar(b, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
                _handlers.HandleBackspace();
                return true;
            case 0x09: // Tab
                TraceHelper.TraceControlChar(b, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
                _handlers.HandleTab();
                return true;
            case 0x0e: // Shift Out (SO)
                TraceHelper.TraceControlChar(b, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
                _handlers.HandleShiftOut();
                return true;
            case 0x0f: // Shift In (SI)
                TraceHelper.TraceControlChar(b, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
                _handlers.HandleShiftIn();
                return true;
            case 0x0a: // Line Feed
                TraceHelper.TraceControlChar(b, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
                _handlers.HandleLineFeed();
                return true;
            case 0x0c: // Form Feed
                TraceHelper.TraceControlChar(b, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
                _handlers.HandleFormFeed();
                return true;
            case 0x0d: // Carriage Return
                TraceHelper.TraceControlChar(b, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
                _handlers.HandleCarriageReturn();
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Starts a new escape sequence and resets state.
    /// </summary>
    private void StartEscapeSequence(byte b)
    {
        _context.State = ParserState.Escape;
        _context.EscapeSequence.Clear();
        _context.EscapeSequence.Add(b);
    }

    /// <summary>
    ///     Optionally emits a normal byte during escape sequence processing.
    /// </summary>
    private void MaybeEmitNormalByteDuringEscapeSequence(byte b)
    {
        if (_emitNormalBytesDuringEscapeSequence)
        {
            HandleNormalByte(b);
        }
    }

    /// <summary>
    ///     Checks if RPC handling is enabled and all required components are available.
    ///     Delegates to RpcSequenceHandler.
    /// </summary>
    /// <returns>True if RPC handling is enabled</returns>
    private bool IsRpcHandlingEnabled()
    {
        return _rpcSequenceHandler.IsRpcHandlingEnabled();
    }

    /// <summary>
    ///     Attempts to handle the current escape sequence as an RPC sequence.
    ///     Delegates to RpcSequenceHandler.
    /// </summary>
    /// <returns>True if the sequence was handled as an RPC sequence</returns>
    private bool TryHandleRpcSequence()
    {
        return _rpcSequenceHandler.TryHandleRpcSequence(_context.EscapeSequence);
    }
}
