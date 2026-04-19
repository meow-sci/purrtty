using caTTY.Core.Parsing;
using caTTY.Core.Rpc;
using caTTY.Core.Terminal.ParserHandlers;
using caTTY.Core.Types;
using caTTY.Core.Tracing;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal;

/// <summary>
///     Parser handlers implementation for the terminal emulator.
///     Bridges parsed sequences to terminal operations and optionally delegates RPC sequences.
/// </summary>
internal class TerminalParserHandlers : IParserHandlers
{
    private readonly ILogger _logger;
    private readonly TerminalEmulator _terminal;
    private readonly IRpcHandler? _rpcHandler;
    private readonly SgrHandler _sgrHandler;
    private readonly DcsHandler _dcsHandler;
    private readonly OscHandler _oscHandler;
    private readonly CsiCursorHandler _csiCursorHandler;
    private readonly CsiEraseHandler _csiEraseHandler;
    private readonly CsiScrollHandler _csiScrollHandler;
    private readonly CsiInsertDeleteHandler _csiInsertDeleteHandler;
    private readonly CsiDecModeHandler _csiDecModeHandler;
    private readonly CsiDeviceQueryHandler _csiDeviceQueryHandler;
    private readonly CsiWindowManipulationHandler _csiWindowManipulationHandler;
    private readonly CsiDispatcher _csiDispatcher;
    private readonly C0Handler _c0Handler;
    private readonly EscHandler _escHandler;
    private readonly IOscRpcHandler _oscRpcHandler;

    public TerminalParserHandlers(TerminalEmulator terminal, ILogger logger, IRpcHandler? rpcHandler = null, IOscRpcHandler? oscRpcHandler = null)
    {
        _terminal = terminal;
        _logger = logger;
        _rpcHandler = rpcHandler;
        _oscRpcHandler = oscRpcHandler ?? new NullOscRpcHandler();
        _sgrHandler = new SgrHandler(terminal, logger);
        _dcsHandler = new DcsHandler(terminal, logger);
        _oscHandler = new OscHandler(terminal, logger, _oscRpcHandler);
        _csiCursorHandler = new CsiCursorHandler(terminal, logger);
        _csiEraseHandler = new CsiEraseHandler(terminal);
        _csiScrollHandler = new CsiScrollHandler(terminal);
        _csiInsertDeleteHandler = new CsiInsertDeleteHandler(terminal, logger);
        _csiDecModeHandler = new CsiDecModeHandler(terminal);
        _csiDeviceQueryHandler = new CsiDeviceQueryHandler(terminal, logger);
        _csiWindowManipulationHandler = new CsiWindowManipulationHandler(terminal, logger);
        _csiDispatcher = new CsiDispatcher(terminal, logger, _sgrHandler, _csiCursorHandler, _csiEraseHandler, _csiScrollHandler, _csiInsertDeleteHandler, _csiDecModeHandler, _csiDeviceQueryHandler, _csiWindowManipulationHandler);
        _c0Handler = new C0Handler(terminal);
        _escHandler = new EscHandler(terminal, logger);
    }

    /// <summary>
    /// Gets whether RPC handling is currently enabled.
    /// </summary>
    public bool IsRpcEnabled => _rpcHandler?.IsEnabled ?? false;

    public void HandleBell()
    {
        _c0Handler.HandleBell();
    }

    public void HandleBackspace()
    {
        _c0Handler.HandleBackspace();
    }

    public void HandleTab()
    {
        _c0Handler.HandleTab();
    }

    public void HandleLineFeed()
    {
        _c0Handler.HandleLineFeed();
    }

    public void HandleFormFeed()
    {
        _c0Handler.HandleFormFeed();
    }

    public void HandleCarriageReturn()
    {
        _c0Handler.HandleCarriageReturn();
    }

    public void HandleShiftIn()
    {
        _c0Handler.HandleShiftIn();
    }

    public void HandleShiftOut()
    {
        _c0Handler.HandleShiftOut();
    }

    public void HandleNormalByte(int codePoint)
    {
        _c0Handler.HandleNormalByte(codePoint);
    }

    public void HandleEsc(EscMessage message)
    {
        _escHandler.HandleEsc(message);
    }

    public void HandleCsi(CsiMessage message)
    {
        _csiDispatcher.HandleCsi(message);
    }

    public void HandleOsc(OscMessage message)
    {
        _oscHandler.HandleOsc(message);
    }

    public void HandleDcs(DcsMessage message)
    {
        // DECRQSS: DCS $ q <request> ST
        // The parser puts intermediates (like $) in the parameters list (or attached to the last parameter).
        // We check if the last parameter ends with '$' and the command is 'q'.
        string lastParam = message.Parameters.Length > 0 ? message.Parameters[^1] : "";

        if (message.Command == "q" && lastParam.EndsWith("$"))
        {
            _dcsHandler.HandleDecrqss(message);
            return;
        }

        // Log unhandled DCS sequences for debugging
        _logger.LogDebug("DCS sequence: {Type} - {Raw}", message.Type, message.Raw);
    }

    public void HandleSgr(SgrSequence sequence)
    {
        _sgrHandler.HandleSgrSequence(sequence);
    }

    public void HandleXtermOsc(XtermOscMessage message)
    {
        _oscHandler.HandleXtermOsc(message);
    }

}

/// <summary>
/// No-op implementation of IOscRpcHandler for when RPC is disabled.
/// Used as default fallback when no OSC RPC handler is provided.
/// </summary>
internal class NullOscRpcHandler : IOscRpcHandler
{
    public bool IsPrivateCommand(int command) => false;
    public void HandleCommand(int command, string? payload) { }
}
