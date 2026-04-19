using caTTY.Core.Managers;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles erase in display operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalEraseInDisplayOps
{
    private readonly ICursorManager _cursorManager;
    private readonly IAttributeManager _attributeManager;
    private readonly IScreenBufferManager _screenBufferManager;
    private readonly IScrollbackManager _scrollbackManager;
    private readonly Func<TerminalState> _getState;
    private readonly Func<int> _getWidth;
    private readonly Func<int> _getHeight;
    private readonly Action<int> _clearLine;
    private readonly ILogger _logger;

    /// <summary>
    ///     Creates a new erase in display operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="attributeManager">The attribute manager for SGR attributes</param>
    /// <param name="screenBufferManager">The screen buffer manager for cell operations</param>
    /// <param name="scrollbackManager">The scrollback manager for clearing scrollback buffer</param>
    /// <param name="getState">Function to get the current terminal state</param>
    /// <param name="getWidth">Function to get the current terminal width</param>
    /// <param name="getHeight">Function to get the current terminal height</param>
    /// <param name="clearLine">Action to clear a line with the specified mode</param>
    /// <param name="logger">Logger for debugging</param>
    public TerminalEraseInDisplayOps(
        ICursorManager cursorManager,
        IAttributeManager attributeManager,
        IScreenBufferManager screenBufferManager,
        IScrollbackManager scrollbackManager,
        Func<TerminalState> getState,
        Func<int> getWidth,
        Func<int> getHeight,
        Action<int> clearLine,
        ILogger logger)
    {
        _cursorManager = cursorManager;
        _attributeManager = attributeManager;
        _screenBufferManager = screenBufferManager;
        _scrollbackManager = scrollbackManager;
        _getState = getState;
        _getWidth = getWidth;
        _getHeight = getHeight;
        _clearLine = clearLine;
        _logger = logger;
    }

    /// <summary>
    ///     Clears the display according to the specified erase mode.
    ///     Implements CSI J (Erase in Display) sequence.
    /// </summary>
    /// <param name="mode">Erase mode: 0=cursor to end, 1=start to cursor, 2=entire screen, 3=entire screen and scrollback</param>
    public void ClearDisplay(int mode)
    {
        // Clear wrap pending state
        _cursorManager.SetWrapPending(false);

        // Create empty cell with current SGR attributes (unprotected)
        var emptyCell = new Cell(' ', _attributeManager.CurrentAttributes, false);

        switch (mode)
        {
            case 0: // From cursor to end of display
                _clearLine(0); // Clear from cursor to end of current line
                // Clear all lines below cursor
                for (int row = _cursorManager.Row + 1; row < _getHeight(); row++)
                {
                    for (int col = 0; col < _getWidth(); col++)
                    {
                        _screenBufferManager.SetCell(row, col, emptyCell);
                    }
                }
                break;

            case 1: // From start of display to cursor
                // Clear all lines above cursor
                for (int row = 0; row < _cursorManager.Row; row++)
                {
                    for (int col = 0; col < _getWidth(); col++)
                    {
                        _screenBufferManager.SetCell(row, col, emptyCell);
                    }
                }
                _clearLine(1); // Clear from start of current line to cursor
                break;

            case 2: // Entire display
                _screenBufferManager.Clear();
                break;

            case 3: // Entire display and scrollback (xterm extension)
                _screenBufferManager.Clear();
                _scrollbackManager.Clear();
                _cursorManager.MoveTo(0, 0); // Move cursor to home
                break;
        }

        // Sync state with managers
        _getState().CursorX = _cursorManager.Column;
        _getState().CursorY = _cursorManager.Row;
        _getState().WrapPending = _cursorManager.WrapPending;
    }
}
