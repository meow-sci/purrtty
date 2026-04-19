using caTTY.Core.Managers;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles DEC mode operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalDecModeOps
{
    private readonly ICursorManager _cursorManager;
    private readonly IModeManager _modeManager;
    private readonly IAlternateScreenManager _alternateScreenManager;
    private readonly ICharacterSetManager _characterSetManager;
    private readonly IScrollbackManager _scrollbackManager;
    private readonly Func<TerminalState> _getState;
    private readonly Action<int, bool> _handleAlternateScreenMode;
    private readonly ILogger _logger;

    /// <summary>
    ///     Creates a new DEC mode operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="modeManager">The mode manager for mode state</param>
    /// <param name="alternateScreenManager">The alternate screen manager for buffer switching</param>
    /// <param name="characterSetManager">The character set manager for UTF-8 mode</param>
    /// <param name="scrollbackManager">The scrollback manager for viewport operations</param>
    /// <param name="getState">Function to get the current terminal state</param>
    /// <param name="handleAlternateScreenMode">Action to handle alternate screen mode changes</param>
    /// <param name="logger">Logger for diagnostic messages</param>
    public TerminalDecModeOps(
        ICursorManager cursorManager,
        IModeManager modeManager,
        IAlternateScreenManager alternateScreenManager,
        ICharacterSetManager characterSetManager,
        IScrollbackManager scrollbackManager,
        Func<TerminalState> getState,
        Action<int, bool> handleAlternateScreenMode,
        ILogger logger)
    {
        _cursorManager = cursorManager;
        _modeManager = modeManager;
        _alternateScreenManager = alternateScreenManager;
        _characterSetManager = characterSetManager;
        _scrollbackManager = scrollbackManager;
        _getState = getState;
        _handleAlternateScreenMode = handleAlternateScreenMode;
        _logger = logger;
    }

    /// <summary>
    ///     Sets a DEC private mode.
    /// </summary>
    /// <param name="mode">The DEC mode number</param>
    /// <param name="enabled">True to enable, false to disable</param>
    public void SetDecMode(int mode, bool enabled)
    {
        var State = _getState();

        // Update the mode manager first
        _modeManager.SetPrivateMode(mode, enabled);

        switch (mode)
        {
            case 6: // DECOM - Origin Mode
                State.SetOriginMode(enabled);
                // Sync with cursor manager after origin mode change
                _cursorManager.MoveTo(State.CursorY, State.CursorX);
                _cursorManager.SetWrapPending(State.WrapPending);
                break;

            case 7: // DECAWM - Auto Wrap Mode
                State.SetAutoWrapMode(enabled);
                // Clear wrap pending when auto-wrap mode is disabled (matches TypeScript)
                if (!enabled)
                {
                    _cursorManager.SetWrapPending(false);
                }
                // Sync with cursor manager after auto wrap mode change
                _cursorManager.SetWrapPending(State.WrapPending);
                break;

            case 25: // DECTCEM - Text Cursor Enable Mode
                State.CursorVisible = enabled;
                _cursorManager.Visible = enabled;
                break;

            case 1: // DECCKM - Application Cursor Keys
                State.ApplicationCursorKeys = enabled;
                break;

            case 47: // Alternate Screen Buffer
            case 1047: // Alternate Screen Buffer with cursor save
            case 1049: // Alternate Screen Buffer with cursor save and clear
                _handleAlternateScreenMode(mode, enabled);
                break;

            case 1000: // VT200 mouse tracking (click)
            case 1002: // button-event tracking (drag)
            case 1003: // any-event tracking (motion)
                State.SetMouseTrackingMode(mode, enabled);
                break;

            case 1006: // SGR mouse encoding
                State.MouseSgrEncodingEnabled = enabled;
                break;

            case 2004: // Bracketed paste mode
                State.BracketedPasteMode = enabled;
                break;

            case 2027: // UTF-8 Mode
                State.Utf8Mode = enabled;
                _characterSetManager.SetUtf8Mode(enabled);
                break;

            default:
                _logger.LogDebug("Unknown DEC mode {Mode} {Action}", mode, enabled ? "set" : "reset");
                break;
        }
    }
}
