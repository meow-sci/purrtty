using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles carriage return operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalCarriageReturnOps
{
    private readonly ICursorManager _cursorManager;
    private readonly Func<TerminalState> _getState;

    /// <summary>
    ///     Creates a new carriage return operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="getState">Function to get the current terminal state</param>
    public TerminalCarriageReturnOps(
        ICursorManager cursorManager,
        Func<TerminalState> getState)
    {
        _cursorManager = cursorManager;
        _getState = getState;
    }

    /// <summary>
    ///     Handles a carriage return (CR) character - move to column 0.
    ///     Uses cursor manager for proper cursor management.
    /// </summary>
    public void HandleCarriageReturn()
    {
        _cursorManager.MoveTo(_cursorManager.Row, 0);
        _cursorManager.SetWrapPending(false);

        // Sync state with cursor manager
        _getState().CursorX = _cursorManager.Column;
        _getState().CursorY = _cursorManager.Row;
        _getState().WrapPending = _cursorManager.WrapPending;
    }
}
