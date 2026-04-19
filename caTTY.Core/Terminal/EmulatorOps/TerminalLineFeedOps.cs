using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles line feed operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalLineFeedOps
{
    private readonly ICursorManager _cursorManager;
    private readonly IScreenBufferManager _screenBufferManager;
    private readonly IAttributeManager _attributeManager;
    private readonly Func<TerminalState> _getState;

    /// <summary>
    ///     Creates a new line feed operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="screenBufferManager">The screen buffer manager for scrolling operations</param>
    /// <param name="attributeManager">The attribute manager for current attributes</param>
    /// <param name="getState">Function to get the current terminal state</param>
    public TerminalLineFeedOps(
        ICursorManager cursorManager,
        IScreenBufferManager screenBufferManager,
        IAttributeManager attributeManager,
        Func<TerminalState> getState)
    {
        _cursorManager = cursorManager;
        _screenBufferManager = screenBufferManager;
        _attributeManager = attributeManager;
        _getState = getState;
    }

    /// <summary>
    ///     Handles a line feed (LF) character - move down one line, keeping same column.
    ///     In raw terminal mode, LF only moves down without changing column position.
    ///     Uses cursor manager for proper cursor management.
    /// </summary>
    public void HandleLineFeed()
    {
        // Clear wrap pending and move cursor down
        _cursorManager.SetWrapPending(false);

        // Move cursor down one line
        if (_cursorManager.Row + 1 > _getState().ScrollBottom)
        {
            // At bottom of scroll region - need to scroll up by one line within the region
            _screenBufferManager.ScrollUpInRegion(1, _getState().ScrollTop, _getState().ScrollBottom, _attributeManager.CurrentAttributes);
            _cursorManager.MoveTo(_getState().ScrollBottom, _cursorManager.Column);
        }
        else
        {
            _cursorManager.MoveTo(_cursorManager.Row + 1, _cursorManager.Column);
        }

        // Sync state with cursor manager
        _getState().CursorX = _cursorManager.Column;
        _getState().CursorY = _cursorManager.Row;
        _getState().WrapPending = _cursorManager.WrapPending;
    }
}
