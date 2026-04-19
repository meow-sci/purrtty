using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles cursor movement operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalCursorMovementOps
{
    private readonly ICursorManager _cursorManager;
    private readonly Func<TerminalState> _getState;
    private readonly Func<int> _getWidth;

    /// <summary>
    ///     Creates a new cursor movement operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="getState">Function to get the current terminal state</param>
    /// <param name="getWidth">Function to get the current terminal width</param>
    public TerminalCursorMovementOps(ICursorManager cursorManager, Func<TerminalState> getState, Func<int> getWidth)
    {
        _cursorManager = cursorManager;
        _getState = getState;
        _getWidth = getWidth;
    }

    /// <summary>
    ///     Moves the cursor up by the specified number of lines.
    /// </summary>
    /// <param name="count">Number of lines to move up (minimum 1)</param>
    public void MoveCursorUp(int count)
    {
        var state = _getState();
        count = Math.Max(1, count);
        _cursorManager.MoveUp(count);

        // Sync cursor manager position to terminal state
        state.CursorX = _cursorManager.Column;
        state.CursorY = _cursorManager.Row;
        state.WrapPending = _cursorManager.WrapPending;

        // Use terminal state clamping to respect scroll regions and origin mode
        state.ClampCursor();

        // Sync back to cursor manager after clamping
        _cursorManager.MoveTo(state.CursorY, state.CursorX);
        _cursorManager.SetWrapPending(state.WrapPending);
    }

    /// <summary>
    ///     Moves the cursor down by the specified number of lines.
    /// </summary>
    /// <param name="count">Number of lines to move down (minimum 1)</param>
    public void MoveCursorDown(int count)
    {
        var state = _getState();
        count = Math.Max(1, count);
        _cursorManager.MoveDown(count);

        // Sync cursor manager position to terminal state
        state.CursorX = _cursorManager.Column;
        state.CursorY = _cursorManager.Row;
        state.WrapPending = _cursorManager.WrapPending;

        // Use terminal state clamping to respect scroll regions and origin mode
        state.ClampCursor();

        // Sync back to cursor manager after clamping
        _cursorManager.MoveTo(state.CursorY, state.CursorX);
        _cursorManager.SetWrapPending(state.WrapPending);
    }

    /// <summary>
    ///     Moves the cursor forward (right) by the specified number of columns.
    /// </summary>
    /// <param name="count">Number of columns to move forward (minimum 1)</param>
    public void MoveCursorForward(int count)
    {
        var state = _getState();
        count = Math.Max(1, count);
        _cursorManager.MoveRight(count);

        // Sync cursor manager position to terminal state
        state.CursorX = _cursorManager.Column;
        state.CursorY = _cursorManager.Row;
        state.WrapPending = _cursorManager.WrapPending;

        // Use terminal state clamping to respect scroll regions and origin mode
        state.ClampCursor();

        // Sync back to cursor manager after clamping
        _cursorManager.MoveTo(state.CursorY, state.CursorX);
        _cursorManager.SetWrapPending(state.WrapPending);
    }

    /// <summary>
    ///     Moves the cursor backward (left) by the specified number of columns.
    /// </summary>
    /// <param name="count">Number of columns to move backward (minimum 1)</param>
    public void MoveCursorBackward(int count)
    {
        var state = _getState();
        count = Math.Max(1, count);
        _cursorManager.MoveLeft(count);

        // Sync cursor manager position to terminal state
        state.CursorX = _cursorManager.Column;
        state.CursorY = _cursorManager.Row;
        state.WrapPending = _cursorManager.WrapPending;

        // Use terminal state clamping to respect scroll regions and origin mode
        state.ClampCursor();

        // Sync back to cursor manager after clamping
        _cursorManager.MoveTo(state.CursorY, state.CursorX);
        _cursorManager.SetWrapPending(state.WrapPending);
    }

    /// <summary>
    ///     Sets the cursor to an absolute position.
    /// </summary>
    /// <param name="row">Target row (1-based, will be converted to 0-based)</param>
    /// <param name="column">Target column (1-based, will be converted to 0-based)</param>
    public void SetCursorPosition(int row, int column)
    {
        var state = _getState();
        var width = _getWidth();

        // Map row parameter based on origin mode (following TypeScript mapRowParamToCursorY)
        int baseRow = state.OriginMode ? state.ScrollTop : 0;
        int targetRow = baseRow + (row - 1);

        // Convert column from 1-based to 0-based
        int targetCol = Math.Max(0, Math.Min(width - 1, column - 1));

        _cursorManager.MoveTo(targetRow, targetCol);

        // Sync cursor manager position to terminal state
        state.CursorX = _cursorManager.Column;
        state.CursorY = _cursorManager.Row;
        state.WrapPending = _cursorManager.WrapPending;

        // Clamp cursor to respect scroll region and origin mode
        state.ClampCursor();

        // Sync back to cursor manager after clamping
        _cursorManager.MoveTo(state.CursorY, state.CursorX);
        _cursorManager.SetWrapPending(state.WrapPending);
    }

    /// <summary>
    ///     Sets the cursor to an absolute column position on the current row.
    ///     Implements CSI G (Cursor Horizontal Absolute) sequence.
    /// </summary>
    /// <param name="column">Target column (1-based, will be converted to 0-based)</param>
    public void SetCursorColumn(int column)
    {
        var state = _getState();
        var width = _getWidth();

        // Convert from 1-based to 0-based coordinates and clamp to bounds
        int targetCol = Math.Max(0, Math.Min(width - 1, column - 1));

        _cursorManager.MoveTo(_cursorManager.Row, targetCol);

        // Sync state with cursor manager
        state.CursorX = _cursorManager.Column;
        state.CursorY = _cursorManager.Row;
        state.WrapPending = _cursorManager.WrapPending;
    }
}
