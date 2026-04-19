using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles scroll operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalScrollOps
{
    private readonly ICursorManager _cursorManager;
    private readonly IScreenBufferManager _screenBufferManager;
    private readonly IAttributeManager _attributeManager;
    private readonly Func<TerminalState> _getState;
    private readonly Func<ICursor> _getCursor;

    /// <summary>
    ///     Creates a new scroll operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="screenBufferManager">The screen buffer manager for scrolling operations</param>
    /// <param name="attributeManager">The attribute manager for SGR attributes</param>
    /// <param name="getState">Function to get the current terminal state</param>
    /// <param name="getCursor">Function to get the current cursor</param>
    public TerminalScrollOps(
        ICursorManager cursorManager,
        IScreenBufferManager screenBufferManager,
        IAttributeManager attributeManager,
        Func<TerminalState> getState,
        Func<ICursor> getCursor)
    {
        _cursorManager = cursorManager;
        _screenBufferManager = screenBufferManager;
        _attributeManager = attributeManager;
        _getState = getState;
        _getCursor = getCursor;
    }

    /// <summary>
    ///     Handles scroll up sequence (CSI S) - scroll screen up by specified lines.
    ///     Implements CSI Ps S (Scroll Up) sequence.
    /// </summary>
    /// <param name="lines">Number of lines to scroll up (default: 1)</param>
    public void ScrollScreenUp(int lines = 1)
    {
        if (lines <= 0)
        {
            return; // Do nothing for zero or negative lines
        }

        // Clear wrap pending state
        _cursorManager.SetWrapPending(false);

        // Use screen buffer manager for proper scrollback integration
        _screenBufferManager.ScrollUpInRegion(lines, _getState().ScrollTop, _getState().ScrollBottom, _attributeManager.CurrentAttributes);

        // Sync state with managers
        _getState().CursorX = _cursorManager.Column;
        _getState().CursorY = _cursorManager.Row;
        _getState().WrapPending = _cursorManager.WrapPending;
    }

    /// <summary>
    ///     Handles scroll down sequence (CSI T) - scroll screen down by specified lines.
    ///     Implements CSI Ps T (Scroll Down) sequence.
    /// </summary>
    /// <param name="lines">Number of lines to scroll down (default: 1)</param>
    public void ScrollScreenDown(int lines = 1)
    {
        if (lines <= 0)
        {
            return; // Do nothing for zero or negative lines
        }

        // Clear wrap pending state
        _cursorManager.SetWrapPending(false);

        // Use screen buffer manager for proper scrollback integration
        _screenBufferManager.ScrollDownInRegion(lines, _getState().ScrollTop, _getState().ScrollBottom, _attributeManager.CurrentAttributes);

        // Sync state with managers
        _getState().CursorX = _cursorManager.Column;
        _getState().CursorY = _cursorManager.Row;
        _getState().WrapPending = _cursorManager.WrapPending;
    }

    /// <summary>
    ///     Handles reverse index (RI) sequence - moves cursor up one line with scrolling.
    ///     If cursor is at the top of the scroll region, scrolls the region down one line.
    ///     Used by full-screen applications like less to scroll the display down within the scroll region.
    /// </summary>
    public void HandleReverseIndex()
    {
        // Sync cursor with state
        _getState().CursorX = _getCursor().Col;
        _getState().CursorY = _getCursor().Row;

        // Clear wrap pending state
        _getState().WrapPending = false;

        if (_getState().CursorY <= _getState().ScrollTop)
        {
            // At top of scroll region - scroll the region down
            _getState().CursorY = _getState().ScrollTop;
            _screenBufferManager.ScrollDownInRegion(1, _getState().ScrollTop, _getState().ScrollBottom, _attributeManager.CurrentAttributes);
        }
        else
        {
            // Move cursor up one line
            _getState().CursorY = Math.Max(_getState().ScrollTop, _getState().CursorY - 1);
        }

        // Update cursor to match state
        _getCursor().SetPosition(_getState().CursorY, _getState().CursorX);
    }
}
