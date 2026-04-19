using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles insert mode operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalInsertModeOps
{
    private readonly ICursorManager _cursorManager;
    private readonly IScreenBufferManager _screenBufferManager;
    private readonly IAttributeManager _attributeManager;
    private readonly IModeManager _modeManager;
    private readonly Func<TerminalState> _getState;
    private readonly Func<int> _getWidth;
    private readonly Func<int> _getHeight;

    /// <summary>
    ///     Creates a new insert mode operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="screenBufferManager">The screen buffer manager for character operations</param>
    /// <param name="attributeManager">The attribute manager for current attributes</param>
    /// <param name="modeManager">The mode manager for insert mode state</param>
    /// <param name="getState">Function to get the current terminal state</param>
    /// <param name="getWidth">Function to get the current terminal width</param>
    /// <param name="getHeight">Function to get the current terminal height</param>
    public TerminalInsertModeOps(
        ICursorManager cursorManager,
        IScreenBufferManager screenBufferManager,
        IAttributeManager attributeManager,
        IModeManager modeManager,
        Func<TerminalState> getState,
        Func<int> getWidth,
        Func<int> getHeight)
    {
        _cursorManager = cursorManager;
        _screenBufferManager = screenBufferManager;
        _attributeManager = attributeManager;
        _modeManager = modeManager;
        _getState = getState;
        _getWidth = getWidth;
        _getHeight = getHeight;
    }

    /// <summary>
    ///     Sets insert mode state. When enabled, new characters are inserted, shifting existing characters right.
    ///     When disabled, new characters overwrite existing characters (default behavior).
    /// </summary>
    /// <param name="enabled">True to enable insert mode, false to disable</param>
    public void SetInsertMode(bool enabled)
    {
        // Update mode manager
        _modeManager.InsertMode = enabled;

        // Update terminal state for compatibility
        _modeManager.SetMode(4, enabled);
    }

    /// <summary>
    ///     Shifts characters to the right in the current row to make room for new characters.
    ///     Used when insert mode is enabled.
    /// </summary>
    /// <param name="row">The row to shift characters in</param>
    /// <param name="startColumn">The column to start shifting from</param>
    /// <param name="shiftAmount">Number of positions to shift (1 for normal chars, 2 for wide chars)</param>
    public void ShiftCharactersRight(int row, int startColumn, int shiftAmount)
    {
        int Width = _getWidth();
        int Height = _getHeight();

        // Bounds checking
        if (row < 0 || row >= Height || startColumn < 0 || startColumn >= Width)
        {
            return;
        }

        // Calculate how many characters we can actually shift
        int availableSpace = Width - startColumn;
        if (availableSpace <= shiftAmount)
        {
            // Not enough space to shift - clear from cursor to end of line
            for (int col = startColumn; col < Width; col++)
            {
                var emptyCell = new Cell(' ', _attributeManager.CurrentAttributes, false, null, false);
                _screenBufferManager.SetCell(row, col, emptyCell);
            }
            return;
        }

        // Shift characters to the right, starting from the rightmost character
        // Work backwards to avoid overwriting characters we haven't moved yet
        for (int col = Width - 1 - shiftAmount; col >= startColumn; col--)
        {
            int targetCol = col + shiftAmount;
            if (targetCol < Width)
            {
                // Get the cell to move
                var sourceCell = _screenBufferManager.GetCell(row, col);
                _screenBufferManager.SetCell(row, targetCol, sourceCell);
            }
        }

        // Clear the positions where we're about to insert
        for (int col = startColumn; col < startColumn + shiftAmount && col < Width; col++)
        {
            var emptyCell = new Cell(' ', _attributeManager.CurrentAttributes, false, null, false);
            _screenBufferManager.SetCell(row, col, emptyCell);
        }
    }
}
