using caTTY.Core.Rpc;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     Handles OSC (Operating System Command) sequence processing.
///     Implements standard ECMA-48/xterm OSC sequences and delegates
///     private-use commands (1000+) to the RPC layer.
/// </summary>
internal class OscHandler
{
    private readonly ILogger _logger;
    private readonly TerminalEmulator _terminal;
    private readonly IOscRpcHandler? _oscRpcHandler;

    public OscHandler(TerminalEmulator terminal, ILogger logger, IOscRpcHandler? oscRpcHandler = null)
    {
        _terminal = terminal;
        _logger = logger;
        _oscRpcHandler = oscRpcHandler;
    }

    public void HandleOsc(OscMessage message)
    {
        // Check if this is an implemented xterm OSC message
        if (message.XtermMessage != null && message.XtermMessage.Implemented)
        {
            HandleXtermOsc(message.XtermMessage);
            return;
        }

        // Handle generic OSC sequences
        _logger.LogDebug("OSC sequence: {Type} - {Raw}", message.Type, message.Raw);
    }

    public void HandleXtermOsc(XtermOscMessage message)
    {
        switch (message.Type)
        {
            case "osc.setTitleAndIcon":
                // OSC 0: Set both window title and icon name
                _terminal.SetTitleAndIcon(message.Title ?? string.Empty);
                _logger.LogDebug("Set title and icon: {Title}", message.Title);
                break;

            case "osc.setIconName":
                // OSC 1: Set icon name only
                _terminal.SetIconName(message.IconName ?? string.Empty);
                _logger.LogDebug("Set icon name: {IconName}", message.IconName);
                break;

            case "osc.setWindowTitle":
                // OSC 2: Set window title only
                _terminal.SetWindowTitle(message.Title ?? string.Empty);
                _logger.LogDebug("Set window title: {Title}", message.Title);
                break;

            case "osc.queryWindowTitle":
                // OSC 21: Query window title - respond with OSC ] L <title> ST (ESC \\)
                string currentTitle = _terminal.GetWindowTitle();
                string titleResponse = $"\x1b]L{currentTitle}\x1b\\";
                _terminal.EmitResponse(titleResponse);
                _logger.LogDebug("Query window title response: {Response}", titleResponse);
                break;

            case "osc.clipboard":
                // OSC 52: Clipboard operations - handle clipboard data and queries
                if (message.ClipboardData != null)
                {
                    _terminal.HandleClipboard(message.ClipboardData);
                    _logger.LogDebug("Clipboard operation: {Data}", message.ClipboardData);
                }
                break;

            case "osc.hyperlink":
                // OSC 8: Hyperlink operations - associate URLs with character ranges
                if (message.HyperlinkUrl != null)
                {
                    _terminal.HandleHyperlink(message.HyperlinkUrl);
                    _logger.LogDebug("Hyperlink operation: {Url}", message.HyperlinkUrl);
                }
                break;

            case "osc.queryForegroundColor":
                // OSC 10;? : Query foreground color - respond with current foreground color
                var currentForeground = _terminal.GetCurrentForegroundColor();
                string foregroundResponse = DeviceResponses.GenerateForegroundColorResponse(
                    currentForeground.Red, currentForeground.Green, currentForeground.Blue);
                _terminal.EmitResponse(foregroundResponse);
                _logger.LogDebug("Query foreground color response: {Response}", foregroundResponse);
                break;

            case "osc.queryBackgroundColor":
                // OSC 11;? : Query background color - respond with current background color
                var currentBackground = _terminal.GetCurrentBackgroundColor();
                string backgroundResponse = DeviceResponses.GenerateBackgroundColorResponse(
                    currentBackground.Red, currentBackground.Green, currentBackground.Blue);
                _terminal.EmitResponse(backgroundResponse);
                _logger.LogDebug("Query background color response: {Response}", backgroundResponse);
                break;

            default:
                // Check if this is a private-use command (1000+) for RPC handling
                if (_oscRpcHandler != null && message.Command >= 1000)
                {
                    _oscRpcHandler.HandleCommand(message.Command, message.Payload);
                }
                else
                {
                    // Log unhandled xterm OSC sequences for debugging
                    _logger.LogDebug("Xterm OSC: {Type} - {Raw}", message.Type, message.Raw);
                }
                break;
        }
    }
}
