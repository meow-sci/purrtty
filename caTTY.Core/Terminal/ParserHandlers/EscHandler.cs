using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     Handles ESC (escape) sequences.
/// </summary>
internal class EscHandler
{
    private readonly TerminalEmulator _terminal;
    private readonly ILogger _logger;

    public EscHandler(TerminalEmulator terminal, ILogger logger)
    {
        _terminal = terminal;
        _logger = logger;
    }

    public void HandleEsc(EscMessage message)
    {
        switch (message.Type)
        {
            case "esc.saveCursor":
                _terminal.SaveCursorPosition();
                break;

            case "esc.restoreCursor":
                _terminal.RestoreCursorPosition();
                break;

            case "esc.index":
                _terminal.HandleIndex();
                break;

            case "esc.reverseIndex":
                _terminal.HandleReverseIndex();
                break;

            case "esc.nextLine":
                _terminal.HandleCarriageReturn();
                _terminal.HandleLineFeed();
                break;

            case "esc.horizontalTabSet":
                _terminal.SetTabStopAtCursor();
                break;

            case "esc.resetToInitialState":
                _terminal.ResetToInitialState();
                break;

            case "esc.designateCharacterSet":
                if (message.Slot != null && message.Charset != null)
                {
                    _terminal.DesignateCharacterSet(message.Slot, message.Charset);
                }
                else
                {
                    _logger.LogWarning("Character set designation missing slot or charset: {Raw}", message.Raw);
                }

                break;

            default:
                _logger.LogDebug("ESC sequence: {Type} - {Raw}", message.Type, message.Raw);
                break;
        }
    }
}
