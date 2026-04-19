using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles index operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalIndexOps
{
    private readonly IScreenBufferManager _screenBufferManager;
    private readonly IAttributeManager _attributeManager;
    private readonly Func<TerminalState> _getState;
    private readonly Func<ICursor> _getCursor;
    private readonly Func<int> _getHeight;

    /// <summary>
    ///     Creates a new index operations handler.
    /// </summary>
    /// <param name="screenBufferManager">The screen buffer manager for scrolling operations</param>
    /// <param name="attributeManager">The attribute manager for current attributes</param>
    /// <param name="getState">Function to get the current terminal state</param>
    /// <param name="getCursor">Function to get the current cursor</param>
    /// <param name="getHeight">Function to get the current terminal height</param>
    public TerminalIndexOps(
        IScreenBufferManager screenBufferManager,
        IAttributeManager attributeManager,
        Func<TerminalState> getState,
        Func<ICursor> getCursor,
        Func<int> getHeight)
    {
        _screenBufferManager = screenBufferManager;
        _attributeManager = attributeManager;
        _getState = getState;
        _getCursor = getCursor;
        _getHeight = getHeight;
    }

    /// <summary>
    ///     Handles index operation (ESC D) - move cursor down one line without changing column.
    ///     Used by ESC D sequence.
    /// </summary>
    public void HandleIndex()
    {
        var cursor = _getCursor();
        var state = _getState();
        var height = _getHeight();

        if (cursor.Row < height - 1)
        {
            // Move cursor down one row, keep same column
            cursor.SetPosition(cursor.Row + 1, cursor.Col);
        }
        else
        {
            // At bottom row - need to scroll up by one line
            _screenBufferManager.ScrollUpInRegion(1, state.ScrollTop, state.ScrollBottom, _attributeManager.CurrentAttributes);
            // Cursor stays at the bottom row
        }

        // Sync state with cursor
        state.CursorX = cursor.Col;
        state.CursorY = cursor.Row;
        state.WrapPending = false;
    }
}
