using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles alternate screen buffer operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalAlternateScreenOps
{
    private readonly ICursorManager _cursorManager;
    private readonly IAlternateScreenManager _alternateScreenManager;
    private readonly IScrollbackManager _scrollbackManager;
    private readonly Func<TerminalState> _getState;

    /// <summary>
    ///     Creates a new alternate screen operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor synchronization</param>
    /// <param name="alternateScreenManager">The alternate screen manager for buffer switching</param>
    /// <param name="scrollbackManager">The scrollback manager for viewport operations</param>
    /// <param name="getState">Function to get the current terminal state</param>
    public TerminalAlternateScreenOps(
        ICursorManager cursorManager,
        IAlternateScreenManager alternateScreenManager,
        IScrollbackManager scrollbackManager,
        Func<TerminalState> getState)
    {
        _cursorManager = cursorManager;
        _alternateScreenManager = alternateScreenManager;
        _scrollbackManager = scrollbackManager;
        _getState = getState;
    }

    /// <summary>
    ///     Handles alternate screen mode activation/deactivation.
    ///     Supports modes 47 (basic), 1047 (with cursor save), and 1049 (with cursor save and clear).
    /// </summary>
    /// <param name="mode">The DEC mode number (47, 1047, or 1049)</param>
    /// <param name="enabled">True to activate alternate screen, false to deactivate</param>
    public void HandleAlternateScreenMode(int mode, bool enabled)
    {
        var State = _getState();

        if (enabled)
        {
            switch (mode)
            {
                case 47: // Basic alternate screen
                    _alternateScreenManager.ActivateAlternate();
                    break;
                case 1047: // Alternate screen with cursor save
                    _alternateScreenManager.ActivateAlternateWithCursorSave();
                    break;
                case 1049: // Alternate screen with cursor save and clear
                    _alternateScreenManager.ActivateAlternateWithClearAndCursorSave();
                    break;
            }
        }
        else
        {
            // Store whether we were in alternate screen before deactivation
            bool wasAlternate = State.IsAlternateScreenActive;

            switch (mode)
            {
                case 47: // Basic alternate screen
                    _alternateScreenManager.DeactivateAlternate();
                    break;
                case 1047: // Alternate screen with cursor restore
                case 1049: // Alternate screen with cursor restore
                    _alternateScreenManager.DeactivateAlternateWithCursorRestore();
                    break;
            }

            // Leaving a full-screen TUI should restore the prompt/cursor at the bottom
            // (matches catty-web controller behavior).
            if (wasAlternate)
            {
                _scrollbackManager.ScrollToBottom();
            }
        }

        // Sync cursor manager with terminal state after buffer switching
        State.CursorX = _cursorManager.Column;
        State.CursorY = _cursorManager.Row;
        State.WrapPending = _cursorManager.WrapPending;
    }
}
