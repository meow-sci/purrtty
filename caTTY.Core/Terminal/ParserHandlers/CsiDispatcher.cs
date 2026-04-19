using caTTY.Core.Parsing;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     CSI (Control Sequence Introducer) message dispatcher.
///     Routes CSI messages to appropriate handlers.
/// </summary>
internal class CsiDispatcher
{
    private readonly TerminalEmulator _terminal;
    private readonly ILogger _logger;
    private readonly SgrHandler _sgrHandler;
    private readonly CsiCursorHandler _cursorHandler;
    private readonly CsiEraseHandler _eraseHandler;
    private readonly CsiScrollHandler _scrollHandler;
    private readonly CsiInsertDeleteHandler _insertDeleteHandler;
    private readonly CsiDecModeHandler _decModeHandler;
    private readonly CsiDeviceQueryHandler _deviceQueryHandler;
    private readonly CsiWindowManipulationHandler _windowManipulationHandler;

    public CsiDispatcher(TerminalEmulator terminal, ILogger logger, SgrHandler sgrHandler, CsiCursorHandler cursorHandler, CsiEraseHandler eraseHandler, CsiScrollHandler scrollHandler, CsiInsertDeleteHandler insertDeleteHandler, CsiDecModeHandler decModeHandler, CsiDeviceQueryHandler deviceQueryHandler, CsiWindowManipulationHandler windowManipulationHandler)
    {
        _terminal = terminal;
        _logger = logger;
        _sgrHandler = sgrHandler;
        _cursorHandler = cursorHandler;
        _eraseHandler = eraseHandler;
        _scrollHandler = scrollHandler;
        _insertDeleteHandler = insertDeleteHandler;
        _decModeHandler = decModeHandler;
        _deviceQueryHandler = deviceQueryHandler;
        _windowManipulationHandler = windowManipulationHandler;
    }

    public void HandleCsi(CsiMessage message)
    {
        switch (message.Type)
        {
            case "csi.cursorUp":
                _cursorHandler.HandleCursorUp(message);
                break;

            case "csi.cursorDown":
                _cursorHandler.HandleCursorDown(message);
                break;

            case "csi.cursorForward":
                _cursorHandler.HandleCursorForward(message);
                break;

            case "csi.cursorBackward":
                _cursorHandler.HandleCursorBackward(message);
                break;

            case "csi.cursorPosition":
                _cursorHandler.HandleCursorPosition(message);
                break;

            case "csi.cursorHorizontalAbsolute":
                _cursorHandler.HandleCursorHorizontalAbsolute(message);
                break;

            case "csi.cursorNextLine":
                _cursorHandler.HandleCursorNextLine(message);
                break;

            case "csi.cursorPrevLine":
                _cursorHandler.HandleCursorPrevLine(message);
                break;

            case "csi.verticalPositionAbsolute":
                _cursorHandler.HandleVerticalPositionAbsolute(message);
                break;

            case "csi.saveCursorPosition":
                _cursorHandler.HandleSaveCursorPosition();
                break;

            case "csi.restoreCursorPosition":
                _cursorHandler.HandleRestoreCursorPosition();
                break;

            case "csi.eraseInDisplay":
                _eraseHandler.HandleEraseInDisplay(message);
                break;

            case "csi.eraseInLine":
                _eraseHandler.HandleEraseInLine(message);
                break;

            case "csi.cursorForwardTab":
                _cursorHandler.HandleCursorForwardTab(message);
                break;

            case "csi.cursorBackwardTab":
                _cursorHandler.HandleCursorBackwardTab(message);
                break;

            case "csi.tabClear":
                if (message.Mode == 3)
                {
                    _terminal.ClearAllTabStops();
                }
                else
                {
                    _terminal.ClearTabStopAtCursor();
                }

                break;

            case "csi.scrollUp":
                _scrollHandler.HandleScrollUp(message);
                break;

            case "csi.scrollDown":
                _scrollHandler.HandleScrollDown(message);
                break;

            case "csi.setScrollRegion":
                _scrollHandler.HandleSetScrollRegion(message);
                break;

            case "csi.selectiveEraseInDisplay":
                _eraseHandler.HandleSelectiveEraseInDisplay(message);
                break;

            case "csi.selectiveEraseInLine":
                _eraseHandler.HandleSelectiveEraseInLine(message);
                break;

            case "csi.selectCharacterProtection":
                // DECSCA - Select Character Protection Attribute
                if (message.Protected.HasValue)
                {
                    _terminal.SetCharacterProtection(message.Protected.Value);
                    _logger.LogDebug("Set character protection: {Protected}", message.Protected.Value);
                }

                break;

            case "csi.sgr":
                // Standard SGR sequence (CSI ... m) - delegate to SGR parser
                var sgrSequence = _terminal.AttributeManager.ParseSgrFromCsi(message.Parameters, message.Raw);
                _sgrHandler.HandleSgrSequence(sgrSequence);
                break;

            case "csi.enhancedSgrMode":
                // Enhanced SGR sequences with > prefix (e.g., CSI > 4 ; 2 m)
                var enhancedSgrSequence = _terminal.AttributeManager.ParseEnhancedSgrFromCsi(message.Parameters, message.Raw);
                _sgrHandler.HandleSgrSequence(enhancedSgrSequence);
                break;

            case "csi.privateSgrMode":
                // Private SGR sequences with ? prefix (e.g., CSI ? 4 m)
                var privateSgrSequence = _terminal.AttributeManager.ParsePrivateSgrFromCsi(message.Parameters, message.Raw);
                _sgrHandler.HandleSgrSequence(privateSgrSequence);
                break;

            case "csi.sgrWithIntermediate":
                // SGR sequences with intermediate characters (e.g., CSI 0 % m)
                var sgrWithIntermediateSequence = _terminal.AttributeManager.ParseSgrWithIntermediateFromCsi(
                    message.Parameters, message.Intermediate ?? "", message.Raw);
                _sgrHandler.HandleSgrSequence(sgrWithIntermediateSequence);
                break;

            // Device query sequences
            case "csi.deviceAttributesPrimary":
                _deviceQueryHandler.HandleDeviceAttributesPrimary();
                break;

            case "csi.deviceAttributesSecondary":
                _deviceQueryHandler.HandleDeviceAttributesSecondary();
                break;

            case "csi.cursorPositionReport":
                _deviceQueryHandler.HandleCursorPositionReport();
                break;

            case "csi.deviceStatusReport":
                _deviceQueryHandler.HandleDeviceStatusReport();
                break;

            case "csi.terminalSizeQuery":
                _deviceQueryHandler.HandleTerminalSizeQuery();
                break;

            case "csi.characterSetQuery":
                _deviceQueryHandler.HandleCharacterSetQuery();
                break;

            case "csi.decModeSet":
                _decModeHandler.HandleDecModeSet(message);
                break;

            case "csi.decModeReset":
                _decModeHandler.HandleDecModeReset(message);
                break;

            case "csi.insertLines":
                _insertDeleteHandler.HandleInsertLines(message);
                break;

            case "csi.deleteLines":
                _insertDeleteHandler.HandleDeleteLines(message);
                break;

            case "csi.insertChars":
                _insertDeleteHandler.HandleInsertChars(message);
                break;

            case "csi.deleteChars":
                _insertDeleteHandler.HandleDeleteChars(message);
                break;

            case "csi.eraseCharacter":
                // Erase Character (CSI X) - erase characters at cursor position
                _eraseHandler.HandleEraseCharacter(message);
                break;

            case "csi.savePrivateMode":
                _decModeHandler.HandleSavePrivateMode(message);
                break;

            case "csi.restorePrivateMode":
                _decModeHandler.HandleRestorePrivateMode(message);
                break;

            case "csi.setCursorStyle":
                _cursorHandler.HandleSetCursorStyle(message);
                break;

            case "csi.decSoftReset":
                // DEC soft reset (CSI ! p) - DECSTR
                _terminal.SoftReset();
                _logger.LogDebug("DEC soft reset executed");
                break;

            case "csi.insertMode":
                // Insert/Replace Mode (CSI 4 h/l) - IRM
                if (message.Enable.HasValue)
                {
                    _terminal.SetInsertMode(message.Enable.Value);
                    _logger.LogDebug("Insert mode {Action}: {Enabled}",
                        message.Enable.Value ? "set" : "reset", message.Enable.Value);
                }
                break;

            case "csi.windowManipulation":
                // Window manipulation commands (CSI Ps t) - handle title stack operations and size queries
                if (message.Operation.HasValue)
                {
                    int[] windowParams = message.WindowParams ?? Array.Empty<int>();
                    _windowManipulationHandler.HandleWindowManipulation(message.Operation.Value, windowParams);
                }
                break;

            default:
                // TODO: Implement other CSI sequence handling (task 2.8, etc.)
                _logger.LogDebug("CSI sequence: {Type} - {Raw}", message.Type, message.Raw);
                break;
        }
    }
}
