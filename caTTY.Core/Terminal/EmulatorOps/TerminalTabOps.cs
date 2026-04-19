using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles tab operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalTabOps
{
    private readonly ICursorManager _cursorManager;
    private readonly Func<TerminalState> _getState;
    private readonly Func<ICursor> _getCursor;
    private readonly Func<int> _getWidth;

    /// <summary>
    ///     Creates a new tab operations handler.
    /// </summary>
    /// <param name="cursorManager">Cursor manager for cursor operations</param>
    /// <param name="getState">Function to get current terminal state</param>
    /// <param name="getCursor">Function to get current cursor</param>
    /// <param name="getWidth">Function to get terminal width</param>
    public TerminalTabOps(ICursorManager cursorManager, Func<TerminalState> getState, Func<ICursor> getCursor, Func<int> getWidth)
    {
        _cursorManager = cursorManager;
        _getState = getState;
        _getCursor = getCursor;
        _getWidth = getWidth;
    }

    /// <summary>
    ///     Handles a tab character - move to next tab stop using terminal state.
    /// </summary>
    public void HandleTab()
    {
        var state = _getState();
        var cursor = _getCursor();
        var width = _getWidth();

        // Sync state with cursor
        state.CursorX = cursor.Col;
        state.CursorY = cursor.Row;

        // Clear wrap pending state since we're moving the cursor
        state.WrapPending = false;

        // Find next tab stop
        int nextTabStop = -1;
        for (int col = state.CursorX + 1; col < width; col++)
        {
            if (col < state.TabStops.Length && state.TabStops[col])
            {
                nextTabStop = col;
                break;
            }
        }

        // If no tab stop found, go to right edge
        if (nextTabStop == -1)
        {
            nextTabStop = width - 1;
        }

        // Move cursor to the tab stop
        state.CursorX = nextTabStop;

        // Handle wrap pending if we're at the right edge and auto-wrap is enabled
        if (state.CursorX >= width - 1 && state.AutoWrapMode)
        {
            state.WrapPending = true;
        }

        // Update cursor to match state
        cursor.SetPosition(state.CursorY, state.CursorX);
    }

    /// <summary>
    ///     Sets a tab stop at the current cursor position.
    ///     Implements ESC H (Horizontal Tab Set) sequence.
    /// </summary>
    public void SetTabStopAtCursor()
    {
        var state = _getState();
        var cursor = _getCursor();

        // Sync cursor with state
        state.CursorX = cursor.Col;
        state.CursorY = cursor.Row;

        // Set tab stop at current cursor position
        if (state.CursorX >= 0 && state.CursorX < state.TabStops.Length)
        {
            state.TabStops[state.CursorX] = true;
        }
    }

    /// <summary>
    ///     Moves cursor forward to the next tab stop.
    ///     Implements CSI I (Cursor Forward Tab) sequence.
    /// </summary>
    /// <param name="count">Number of tab stops to move forward</param>
    public void CursorForwardTab(int count)
    {
        var state = _getState();
        var width = _getWidth();

        // Clear wrap pending state first
        _cursorManager.SetWrapPending(false);

        int n = Math.Max(1, count);
        int currentCol = _cursorManager.Column;

        if (currentCol < 0)
        {
            currentCol = 0;
        }

        for (int i = 0; i < n; i++)
        {
            int nextStop = -1;
            for (int x = currentCol + 1; x < width; x++)
            {
                if (x < state.TabStops.Length && state.TabStops[x])
                {
                    nextStop = x;
                    break;
                }
            }

            currentCol = nextStop == -1 ? width - 1 : nextStop;
        }

        // Update cursor position through cursor manager
        _cursorManager.MoveTo(_cursorManager.Row, currentCol);

        // Sync state with cursor manager
        state.CursorX = _cursorManager.Column;
        state.CursorY = _cursorManager.Row;
        state.WrapPending = _cursorManager.WrapPending;
    }

    /// <summary>
    ///     Moves cursor backward to the previous tab stop.
    ///     Implements CSI Z (Cursor Backward Tab) sequence.
    /// </summary>
    /// <param name="count">Number of tab stops to move backward</param>
    public void CursorBackwardTab(int count)
    {
        var state = _getState();

        // Clear wrap pending state first
        _cursorManager.SetWrapPending(false);

        int n = Math.Max(1, count);
        int currentCol = _cursorManager.Column;

        if (currentCol < 0)
        {
            currentCol = 0;
        }

        for (int i = 0; i < n; i++)
        {
            int prevStop = -1;
            for (int x = currentCol - 1; x >= 0; x--)
            {
                if (x < state.TabStops.Length && state.TabStops[x])
                {
                    prevStop = x;
                    break;
                }
            }

            currentCol = prevStop == -1 ? 0 : prevStop;
        }

        // Update cursor position through cursor manager
        _cursorManager.MoveTo(_cursorManager.Row, currentCol);

        // Sync state with cursor manager
        state.CursorX = _cursorManager.Column;
        state.CursorY = _cursorManager.Row;
        state.WrapPending = _cursorManager.WrapPending;
    }

    /// <summary>
    ///     Clears the tab stop at the current cursor position.
    ///     Implements CSI g (Tab Clear) sequence with mode 0.
    /// </summary>
    public void ClearTabStopAtCursor()
    {
        var state = _getState();
        var cursor = _getCursor();

        // Sync cursor with state
        state.CursorX = cursor.Col;
        state.CursorY = cursor.Row;

        // Clear tab stop at current cursor position
        if (state.CursorX >= 0 && state.CursorX < state.TabStops.Length)
        {
            state.TabStops[state.CursorX] = false;
        }
    }

    /// <summary>
    ///     Clears all tab stops.
    ///     Implements CSI 3 g (Tab Clear) sequence with mode 3.
    /// </summary>
    public void ClearAllTabStops()
    {
        var state = _getState();

        // Clear all tab stops
        for (int i = 0; i < state.TabStops.Length; i++)
        {
            state.TabStops[i] = false;
        }
    }
}
