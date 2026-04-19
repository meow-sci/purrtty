using caTTY.Core.Managers;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles selective erase in line operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalSelectiveEraseInLineOps
{
    private readonly ICursorManager _cursorManager;
    private readonly IAttributeManager _attributeManager;
    private readonly IScreenBufferManager _screenBufferManager;
    private readonly Func<TerminalState> _getState;
    private readonly Func<int> _getWidth;
    private readonly Func<int> _getHeight;
    private readonly ILogger _logger;

    /// <summary>
    ///     Creates a new selective erase in line operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="attributeManager">The attribute manager for SGR attributes</param>
    /// <param name="screenBufferManager">The screen buffer manager for cell operations</param>
    /// <param name="getState">Function to get the current terminal state</param>
    /// <param name="getWidth">Function to get the current terminal width</param>
    /// <param name="getHeight">Function to get the current terminal height</param>
    /// <param name="logger">Logger for debugging</param>
    public TerminalSelectiveEraseInLineOps(
        ICursorManager cursorManager,
        IAttributeManager attributeManager,
        IScreenBufferManager screenBufferManager,
        Func<TerminalState> getState,
        Func<int> getWidth,
        Func<int> getHeight,
        ILogger logger)
    {
        _cursorManager = cursorManager;
        _attributeManager = attributeManager;
        _screenBufferManager = screenBufferManager;
        _getState = getState;
        _getWidth = getWidth;
        _getHeight = getHeight;
        _logger = logger;
    }

    /// <summary>
    ///     Clears the current line selectively according to the specified erase mode.
    ///     Implements CSI ? K (Selective Erase in Line) sequence.
    ///     Only erases unprotected cells, preserving protected cells.
    /// </summary>
    /// <param name="mode">Erase mode: 0=cursor to end of line, 1=start of line to cursor, 2=entire line</param>
    public void ClearLineSelective(int mode)
    {
        // Clear wrap pending state
        _cursorManager.SetWrapPending(false);

        // Bounds check
        if (_cursorManager.Row < 0 || _cursorManager.Row >= _getHeight())
        {
            return;
        }

        // Create empty cell with current SGR attributes (unprotected)
        var emptyCell = new Cell(' ', _attributeManager.CurrentAttributes, false);

        switch (mode)
        {
            case 0: // From cursor to end of line
                for (int col = _cursorManager.Column; col < _getWidth(); col++)
                {
                    Cell currentCell = _screenBufferManager.GetCell(_cursorManager.Row, col);
                    if (!currentCell.IsProtected)
                    {
                        _screenBufferManager.SetCell(_cursorManager.Row, col, emptyCell);
                    }
                }
                break;

            case 1: // From start of line to cursor
                for (int col = 0; col <= _cursorManager.Column && col < _getWidth(); col++)
                {
                    Cell currentCell = _screenBufferManager.GetCell(_cursorManager.Row, col);
                    if (!currentCell.IsProtected)
                    {
                        _screenBufferManager.SetCell(_cursorManager.Row, col, emptyCell);
                    }
                }
                break;

            case 2: // Entire line
                for (int col = 0; col < _getWidth(); col++)
                {
                    Cell currentCell = _screenBufferManager.GetCell(_cursorManager.Row, col);
                    if (!currentCell.IsProtected)
                    {
                        _screenBufferManager.SetCell(_cursorManager.Row, col, emptyCell);
                    }
                }
                break;
        }

        // Sync state with managers
        _getState().CursorX = _cursorManager.Column;
        _getState().CursorY = _cursorManager.Row;
        _getState().WrapPending = _cursorManager.WrapPending;
    }
}
