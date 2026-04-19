using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles backspace operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalBackspaceOps
{
    private readonly ICursorManager _cursorManager;
    private readonly Func<TerminalState> _getState;

    /// <summary>
    ///     Creates a new backspace operations handler.
    /// </summary>
    /// <param name="cursorManager">Cursor manager for cursor operations</param>
    /// <param name="getState">Function to get current terminal state</param>
    public TerminalBackspaceOps(ICursorManager cursorManager, Func<TerminalState> getState)
    {
        _cursorManager = cursorManager;
        _getState = getState;
    }

    /// <summary>
    ///     Handles a backspace character (BS) - move cursor one position left if not at column 0.
    ///     Uses cursor manager for proper cursor management.
    /// </summary>
    public void HandleBackspace()
    {
        _cursorManager.SetWrapPending(false);

        if (_cursorManager.Column > 0)
        {
            _cursorManager.MoveLeft(1);
        }

        // Sync state with cursor manager
        var state = _getState();
        state.CursorX = _cursorManager.Column;
        state.CursorY = _cursorManager.Row;
        state.WrapPending = _cursorManager.WrapPending;
    }
}
