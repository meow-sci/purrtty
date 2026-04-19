using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Resize operations for terminal emulator.
///     Handles terminal dimension changes and buffer resizing.
/// </summary>
internal class TerminalResizeOps
{
    private readonly TerminalState _state;
    private readonly IScreenBufferManager _screenBufferManager;
    private readonly ICursorManager _cursorManager;
    private readonly IScrollbackManager _scrollbackManager;
    private readonly Func<int> _getWidth;
    private readonly Func<int> _getHeight;
    private readonly Action _onScreenUpdated;

    /// <summary>
    ///     Creates a new resize operations handler.
    /// </summary>
    /// <param name="state">The terminal state</param>
    /// <param name="screenBufferManager">The screen buffer manager</param>
    /// <param name="cursorManager">The cursor manager</param>
    /// <param name="scrollbackManager">The scrollback manager</param>
    /// <param name="getWidth">Function to get current terminal width</param>
    /// <param name="getHeight">Function to get current terminal height</param>
    /// <param name="onScreenUpdated">Callback to notify when screen needs refresh</param>
    public TerminalResizeOps(
        TerminalState state,
        IScreenBufferManager screenBufferManager,
        ICursorManager cursorManager,
        IScrollbackManager scrollbackManager,
        Func<int> getWidth,
        Func<int> getHeight,
        Action onScreenUpdated)
    {
        _state = state;
        _screenBufferManager = screenBufferManager;
        _cursorManager = cursorManager;
        _scrollbackManager = scrollbackManager;
        _getWidth = getWidth;
        _getHeight = getHeight;
        _onScreenUpdated = onScreenUpdated;
    }

    /// <summary>
    ///     Resizes the terminal to the specified dimensions.
    ///     Preserves cursor position and updates scrollback during resize operations.
    ///     Uses simple resize policy: height change preserves top-to-bottom rows,
    ///     width change truncates/pads each row without complex reflow.
    /// </summary>
    /// <param name="width">New width in columns</param>
    /// <param name="height">New height in rows</param>
    public void Resize(int width, int height)
    {
        if (width < 1 || width > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be between 1 and 1000");
        }

        if (height < 1 || height > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be between 1 and 1000");
        }

        int currentWidth = _getWidth();
        int currentHeight = _getHeight();

        // If dimensions are the same, no work needed
        if (width == currentWidth && height == currentHeight)
        {
            return;
        }

        // Save current cursor position for preservation
        int oldCursorX = _cursorManager.Column;
        int oldCursorY = _cursorManager.Row;
        int oldWidth = currentWidth;
        int oldHeight = currentHeight;

        // Handle scrollback updates during resize
        // If height is decreasing and cursor is below the new height,
        // we need to push the excess rows to scrollback
        bool rowsPushedToScrollback = false;
        int rowsMovedToScrollback = 0;
        if (height < oldHeight && oldCursorY >= height)
        {
            // Calculate how many rows need to be moved to scrollback
            int excessRows = oldHeight - height;

            // Push the top rows to scrollback to preserve content
            for (int row = 0; row < excessRows; row++)
            {
                var rowSpan = _screenBufferManager.GetRow(row);
                if (!rowSpan.IsEmpty)
                {
                    _scrollbackManager.AddLine(rowSpan);
                }
            }

            rowsPushedToScrollback = true;
            rowsMovedToScrollback = excessRows;
        }

        // Resize the screen buffer (this preserves content according to the simple policy)
        _screenBufferManager.Resize(width, height);

        // Update terminal state dimensions
        _state.Resize(width, height);

        // Preserve cursor position with intelligent clamping
        int newCursorX = Math.Min(oldCursorX, width - 1);
        int newCursorY;

        if (rowsPushedToScrollback)
        {
            // Height decreased and rows were pushed to scrollback - adjust cursor position
            newCursorY = Math.Max(0, oldCursorY - rowsMovedToScrollback);
        }
        else
        {
            // No rows pushed to scrollback - just clamp cursor to new bounds
            newCursorY = Math.Min(oldCursorY, height - 1);
        }

        // Update cursor position
        _cursorManager.MoveTo(newCursorY, newCursorX);

        // Update scroll region to match new dimensions if it was full-screen
        if (_state.ScrollTop == 0 && _state.ScrollBottom == oldHeight - 1)
        {
            _state.ScrollTop = 0;
            _state.ScrollBottom = height - 1;
        }
        else
        {
            // Clamp existing scroll region to new dimensions
            _state.ScrollTop = Math.Min(_state.ScrollTop, height - 1);
            _state.ScrollBottom = Math.Min(_state.ScrollBottom, height - 1);

            // Ensure scroll region is still valid
            if (_state.ScrollTop >= _state.ScrollBottom)
            {
                _state.ScrollTop = 0;
                _state.ScrollBottom = height - 1;
            }
        }

        // Update tab stops array to match new width
        _state.ResizeTabStops(width);

        // Sync state with managers
        _state.CursorX = _cursorManager.Column;
        _state.CursorY = _cursorManager.Row;
        _state.WrapPending = _cursorManager.WrapPending;

        // Notify that the screen has been updated
        _onScreenUpdated();
    }
}
