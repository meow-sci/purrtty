using caTTY.Core.Types;

namespace caTTY.Core.Managers;

/// <summary>
///     Manages screen buffer operations including cell access, clearing, and resizing.
///     Provides efficient access to the 2D character grid with bounds checking.
/// </summary>
public class ScreenBufferManager : IScreenBufferManager
{
    private readonly IScreenBuffer _screenBuffer;
    private Action<ReadOnlySpan<Cell>>? _pushScrollbackRow;
    private Func<bool>? _isAlternateScreenActive;

    /// <summary>
    ///     Creates a new screen buffer manager with the specified screen buffer.
    /// </summary>
    /// <param name="screenBuffer">The screen buffer to manage</param>
    /// <exception cref="ArgumentNullException">Thrown when screenBuffer is null</exception>
    public ScreenBufferManager(IScreenBuffer screenBuffer)
    {
        _screenBuffer = screenBuffer ?? throw new ArgumentNullException(nameof(screenBuffer));
    }

    /// <summary>
    ///     Sets the scrollback integration callbacks for proper scrollback behavior.
    /// </summary>
    /// <param name="pushScrollbackRow">Callback to push a row to scrollback buffer</param>
    /// <param name="isAlternateScreenActive">Function to check if alternate screen is active</param>
    public void SetScrollbackIntegration(Action<ReadOnlySpan<Cell>>? pushScrollbackRow, Func<bool>? isAlternateScreenActive)
    {
        _pushScrollbackRow = pushScrollbackRow;
        _isAlternateScreenActive = isAlternateScreenActive;
    }

    /// <summary>
    ///     Gets the width of the screen buffer in columns.
    /// </summary>
    public int Width => _screenBuffer.Width;

    /// <summary>
    ///     Gets the height of the screen buffer in rows.
    /// </summary>
    public int Height => _screenBuffer.Height;

    /// <summary>
    ///     Gets a cell at the specified position with bounds checking.
    /// </summary>
    /// <param name="row">Row index (0-based)</param>
    /// <param name="col">Column index (0-based)</param>
    /// <returns>The cell at the specified position, or default cell if out of bounds</returns>
    public Cell GetCell(int row, int col)
    {
        if (row < 0 || row >= Height || col < 0 || col >= Width)
        {
            return Cell.Empty;
        }

        return _screenBuffer.GetCell(row, col);
    }

    /// <summary>
    ///     Sets a cell at the specified position with bounds checking.
    /// </summary>
    /// <param name="row">Row index (0-based)</param>
    /// <param name="col">Column index (0-based)</param>
    /// <param name="cell">The cell to set</param>
    public void SetCell(int row, int col, Cell cell)
    {
        if (row < 0 || row >= Height || col < 0 || col >= Width)
        {
            return;
        }

        _screenBuffer.SetCell(row, col, cell);
    }

    /// <summary>
    ///     Clears the entire screen buffer.
    /// </summary>
    public void Clear()
    {
        _screenBuffer.Clear();
    }

    /// <summary>
    ///     Clears a specific region of the screen buffer.
    /// </summary>
    /// <param name="startRow">Starting row (0-based, inclusive)</param>
    /// <param name="startCol">Starting column (0-based, inclusive)</param>
    /// <param name="endRow">Ending row (0-based, inclusive)</param>
    /// <param name="endCol">Ending column (0-based, inclusive)</param>
    public void ClearRegion(int startRow, int startCol, int endRow, int endCol)
    {
        // Clamp bounds
        startRow = Math.Max(0, Math.Min(Height - 1, startRow));
        startCol = Math.Max(0, Math.Min(Width - 1, startCol));
        endRow = Math.Max(0, Math.Min(Height - 1, endRow));
        endCol = Math.Max(0, Math.Min(Width - 1, endCol));

        // Ensure start <= end
        if (startRow > endRow)
        {
            (startRow, endRow) = (endRow, startRow);
        }
        if (startCol > endCol)
        {
            (startCol, endCol) = (endCol, startCol);
        }

        var emptyCell = Cell.Empty;
        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                _screenBuffer.SetCell(row, col, emptyCell);
            }
        }
    }

    /// <summary>
    ///     Scrolls the buffer up by the specified number of lines.
    ///     Moves scrolled content to scrollback buffer when in primary screen mode.
    /// </summary>
    /// <param name="lines">Number of lines to scroll up</param>
    public void ScrollUp(int lines)
    {
        if (lines <= 0)
        {
            return;
        }

        // Bounds checking - clamp lines to valid range
        lines = Math.Min(lines, Height);

        // Check if we should push to scrollback (primary screen only, not alternate)
        bool shouldPushToScrollback = _pushScrollbackRow != null && 
                                     (_isAlternateScreenActive?.Invoke() != true);

        // Push scrolled lines to scrollback buffer before scrolling
        if (shouldPushToScrollback)
        {
            for (int i = 0; i < lines; i++)
            {
                var row = _screenBuffer.GetRow(i);
                if (!row.IsEmpty)
                {
                    _pushScrollbackRow!(row);
                }
            }
        }

        // Perform the actual scroll operation
        _screenBuffer.ScrollUp(lines);
    }

    /// <summary>
    ///     Scrolls the buffer down by the specified number of lines.
    ///     Handles content preservation during scrolling with bounds checking.
    /// </summary>
    /// <param name="lines">Number of lines to scroll down</param>
    public void ScrollDown(int lines)
    {
        if (lines <= 0)
        {
            return;
        }

        // Bounds checking - clamp lines to valid range
        lines = Math.Min(lines, Height);

        // Perform the actual scroll operation
        _screenBuffer.ScrollDown(lines);
    }

    /// <summary>
    ///     Scrolls up within a specific scroll region.
    ///     Used for scroll regions defined by CSI r (DECSTBM).
    /// </summary>
    /// <param name="lines">Number of lines to scroll up</param>
    /// <param name="scrollTop">Top boundary of scroll region (0-based, inclusive)</param>
    /// <param name="scrollBottom">Bottom boundary of scroll region (0-based, inclusive)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for new blank lines</param>
    public void ScrollUpInRegion(int lines, int scrollTop, int scrollBottom, SgrAttributes currentSgrAttributes)
    {
        if (lines <= 0)
        {
            return;
        }

        // Validate scroll region bounds
        scrollTop = Math.Max(0, Math.Min(Height - 1, scrollTop));
        scrollBottom = Math.Max(0, Math.Min(Height - 1, scrollBottom));
        
        if (scrollTop >= scrollBottom)
        {
            return;
        }

        // If scroll region covers the full screen, use normal scroll with scrollback
        if (scrollTop == 0 && scrollBottom == Height - 1)
        {
            ScrollUp(lines);
            return;
        }

        // Bounds checking for lines within the region
        int regionHeight = scrollBottom - scrollTop + 1;
        lines = Math.Min(lines, regionHeight);

        // Scroll within the region only
        for (int i = 0; i < lines; i++)
        {
            // Move all lines up within the scroll region
            for (int row = scrollTop; row < scrollBottom; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    var cell = _screenBuffer.GetCell(row + 1, col);
                    _screenBuffer.SetCell(row, col, cell);
                }
            }

            // Clear the bottom line of the scroll region with current SGR attributes
            var blankCell = new Cell(' ', currentSgrAttributes, false);
            for (int col = 0; col < Width; col++)
            {
                _screenBuffer.SetCell(scrollBottom, col, blankCell);
            }
        }
    }

    /// <summary>
    ///     Scrolls down within a specific scroll region.
    ///     Used for scroll regions defined by CSI r (DECSTBM).
    /// </summary>
    /// <param name="lines">Number of lines to scroll down</param>
    /// <param name="scrollTop">Top boundary of scroll region (0-based, inclusive)</param>
    /// <param name="scrollBottom">Bottom boundary of scroll region (0-based, inclusive)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for new blank lines</param>
    public void ScrollDownInRegion(int lines, int scrollTop, int scrollBottom, SgrAttributes currentSgrAttributes)
    {
        if (lines <= 0)
        {
            return;
        }

        // Validate scroll region bounds
        scrollTop = Math.Max(0, Math.Min(Height - 1, scrollTop));
        scrollBottom = Math.Max(0, Math.Min(Height - 1, scrollBottom));
        
        if (scrollTop >= scrollBottom)
        {
            return;
        }

        // Bounds checking for lines within the region
        int regionHeight = scrollBottom - scrollTop + 1;
        lines = Math.Min(lines, regionHeight);

        // Scroll within the region only
        for (int i = 0; i < lines; i++)
        {
            // Move all lines down within the scroll region
            for (int row = scrollBottom; row > scrollTop; row--)
            {
                for (int col = 0; col < Width; col++)
                {
                    var cell = _screenBuffer.GetCell(row - 1, col);
                    _screenBuffer.SetCell(row, col, cell);
                }
            }

            // Clear the top line of the scroll region with current SGR attributes
            var blankCell = new Cell(' ', currentSgrAttributes, false);
            for (int col = 0; col < Width; col++)
            {
                _screenBuffer.SetCell(scrollTop, col, blankCell);
            }
        }
    }

    /// <summary>
    ///     Resizes the screen buffer to the specified dimensions.
    ///     Preserves content according to the simple resize policy:
    ///     - Height change: preserve top-to-bottom rows where possible
    ///     - Width change: truncate/pad each row; do not attempt complex reflow
    /// </summary>
    /// <param name="width">New width in columns</param>
    /// <param name="height">New height in rows</param>
    public void Resize(int width, int height)
    {
        if (width < 1 || width > 1000 || height < 1 || height > 1000)
        {
            throw new ArgumentOutOfRangeException("Width and height must be between 1 and 1000");
        }

        // If dimensions are the same, no work needed
        if (width == Width && height == Height)
        {
            return;
        }

        // Delegate to the underlying screen buffer
        _screenBuffer.Resize(width, height);
    }

    /// <summary>
    ///     Inserts blank lines at the cursor position within the scroll region.
    ///     Implements CSI L (Insert Lines) sequence.
    ///     Lines below the cursor are shifted down, and lines that would go beyond
    ///     the scroll region bottom are lost.
    /// </summary>
    /// <param name="count">Number of lines to insert</param>
    /// <param name="cursorRow">Current cursor row (0-based)</param>
    /// <param name="scrollTop">Top boundary of scroll region (0-based, inclusive)</param>
    /// <param name="scrollBottom">Bottom boundary of scroll region (0-based, inclusive)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for new blank lines</param>
    /// <param name="currentCharacterProtection">Current character protection status for new blank lines</param>
    public void InsertLinesInRegion(int count, int cursorRow, int scrollTop, int scrollBottom, SgrAttributes currentSgrAttributes, bool currentCharacterProtection)
    {
        if (count <= 0)
        {
            return;
        }

        // Validate scroll region bounds
        scrollTop = Math.Max(0, Math.Min(Height - 1, scrollTop));
        scrollBottom = Math.Max(0, Math.Min(Height - 1, scrollBottom));
        
        if (scrollTop >= scrollBottom)
        {
            return;
        }

        // IL affects only when the cursor is within the scroll region
        if (cursorRow < scrollTop || cursorRow > scrollBottom)
        {
            return;
        }

        // Calculate maximum insertable lines
        int maxInsertable = scrollBottom - cursorRow + 1;
        int n = Math.Max(0, Math.Min(count, maxInsertable));
        if (n == 0)
        {
            return;
        }

        // Shift lines down within the region starting at cursorRow
        for (int y = scrollBottom; y >= cursorRow + n; y--)
        {
            for (int x = 0; x < Width; x++)
            {
                var cell = _screenBuffer.GetCell(y - n, x);
                _screenBuffer.SetCell(y, x, cell);
            }
        }

        // Clear the inserted blank lines with current SGR attributes and protection status
        var blankCell = new Cell(' ', currentSgrAttributes, currentCharacterProtection);
        for (int y = cursorRow; y < cursorRow + n; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                _screenBuffer.SetCell(y, x, blankCell);
            }
        }
    }

    /// <summary>
    ///     Deletes lines at the cursor position within the scroll region.
    ///     Implements CSI M (Delete Lines) sequence.
    ///     Lines below the cursor are shifted up, and blank lines are added
    ///     at the bottom of the scroll region.
    /// </summary>
    /// <param name="count">Number of lines to delete</param>
    /// <param name="cursorRow">Current cursor row (0-based)</param>
    /// <param name="scrollTop">Top boundary of scroll region (0-based, inclusive)</param>
    /// <param name="scrollBottom">Bottom boundary of scroll region (0-based, inclusive)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for new blank lines</param>
    /// <param name="currentCharacterProtection">Current character protection status for new blank lines</param>
    public void DeleteLinesInRegion(int count, int cursorRow, int scrollTop, int scrollBottom, SgrAttributes currentSgrAttributes, bool currentCharacterProtection)
    {
        if (count <= 0)
        {
            return;
        }

        // Validate scroll region bounds
        scrollTop = Math.Max(0, Math.Min(Height - 1, scrollTop));
        scrollBottom = Math.Max(0, Math.Min(Height - 1, scrollBottom));
        
        if (scrollTop >= scrollBottom)
        {
            return;
        }

        // DL affects only when the cursor is within the scroll region
        if (cursorRow < scrollTop || cursorRow > scrollBottom)
        {
            return;
        }

        // Calculate maximum deletable lines
        int maxDeletable = scrollBottom - cursorRow + 1;
        int n = Math.Max(0, Math.Min(count, maxDeletable));
        if (n == 0)
        {
            return;
        }

        // Shift lines up within the region starting at cursorRow
        for (int y = cursorRow; y <= scrollBottom - n; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var cell = _screenBuffer.GetCell(y + n, x);
                _screenBuffer.SetCell(y, x, cell);
            }
        }

        // Clear the newly exposed bottom lines with current SGR attributes and protection status
        var blankCell = new Cell(' ', currentSgrAttributes, currentCharacterProtection);
        for (int y = scrollBottom - n + 1; y <= scrollBottom; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                _screenBuffer.SetCell(y, x, blankCell);
            }
        }
    }

    /// <summary>
    ///     Gets a read-only span of cells for the specified row.
    /// </summary>
    /// <param name="row">Row index (0-based)</param>
    /// <returns>Read-only span of cells for the row</returns>
    public ReadOnlySpan<Cell> GetRow(int row)
    {
        if (row < 0 || row >= Height)
        {
            return ReadOnlySpan<Cell>.Empty;
        }

        return _screenBuffer.GetRow(row);
    }

    /// <summary>
    ///     Copies a range of rows to the specified destination span.
    /// </summary>
    /// <param name="destination">Destination span to copy to</param>
    /// <param name="startRow">Starting row (0-based, inclusive)</param>
    /// <param name="endRow">Ending row (0-based, inclusive)</param>
    public void CopyTo(Span<Cell> destination, int startRow, int endRow)
    {
        if (startRow < 0 || startRow >= Height || endRow < 0 || endRow >= Height)
        {
            return;
        }

        if (startRow > endRow)
        {
            (startRow, endRow) = (endRow, startRow);
        }

        _screenBuffer.CopyTo(destination, startRow, endRow);
    }

    /// <summary>
    ///     Inserts blank characters at the cursor position within the current line.
    ///     Implements CSI @ (Insert Characters) sequence.
    /// </summary>
    /// <param name="count">Number of characters to insert</param>
    /// <param name="cursorRow">Current cursor row (0-based)</param>
    /// <param name="cursorCol">Current cursor column (0-based)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for new blank characters</param>
    /// <param name="currentCharacterProtection">Current character protection status for new blank characters</param>
    public void InsertCharactersInLine(int count, int cursorRow, int cursorCol, SgrAttributes currentSgrAttributes, bool currentCharacterProtection)
    {
        if (count <= 0)
        {
            return;
        }
        if (cursorRow < 0 || cursorRow >= Height)
        {
            return;
        }
        if (cursorCol < 0 || cursorCol >= Width)
        {
            return;
        }

        // Calculate the actual number of characters to insert (limited by remaining space in line)
        int n = Math.Min(count, Width - cursorCol);
        if (n <= 0)
        {
            return;
        }

        // Shift existing characters to the right
        for (int x = Width - 1; x >= cursorCol + n; x--)
        {
            var cell = _screenBuffer.GetCell(cursorRow, x - n);
            _screenBuffer.SetCell(cursorRow, x, cell);
        }

        // Insert blank characters with current SGR attributes
        var blankCell = new Cell(' ', currentSgrAttributes, currentCharacterProtection);
        for (int x = 0; x < n; x++)
        {
            _screenBuffer.SetCell(cursorRow, cursorCol + x, blankCell);
        }
    }

    /// <summary>
    ///     Deletes characters at the cursor position within the current line.
    ///     Implements CSI P (Delete Characters) sequence.
    /// </summary>
    /// <param name="count">Number of characters to delete</param>
    /// <param name="cursorRow">Current cursor row (0-based)</param>
    /// <param name="cursorCol">Current cursor column (0-based)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for new blank characters</param>
    /// <param name="currentCharacterProtection">Current character protection status for new blank characters</param>
    public void DeleteCharactersInLine(int count, int cursorRow, int cursorCol, SgrAttributes currentSgrAttributes, bool currentCharacterProtection)
    {
        if (count <= 0)
        {
            return;
        }
        if (cursorRow < 0 || cursorRow >= Height)
        {
            return;
        }
        if (cursorCol < 0 || cursorCol >= Width)
        {
            return;
        }

        // Calculate the actual number of characters to delete (limited by remaining characters in line)
        int n = Math.Min(count, Width - cursorCol);
        if (n <= 0)
        {
            return;
        }

        // Shift remaining characters to the left
        for (int x = cursorCol; x < Width - n; x++)
        {
            var cell = _screenBuffer.GetCell(cursorRow, x + n);
            _screenBuffer.SetCell(cursorRow, x, cell);
        }

        // Fill the end of the line with blank characters
        var blankCell = new Cell(' ', currentSgrAttributes, false); // Use false for character protection as per TypeScript
        for (int x = Width - n; x < Width; x++)
        {
            _screenBuffer.SetCell(cursorRow, x, blankCell);
        }
    }

    /// <summary>
    ///     Erases characters at the cursor position within the current line.
    ///     Implements CSI X (Erase Character) sequence.
    ///     Characters are replaced with blank characters using current SGR attributes.
    ///     Does not shift other characters - erases in place.
    /// </summary>
    /// <param name="count">Number of characters to erase</param>
    /// <param name="cursorRow">Current cursor row (0-based)</param>
    /// <param name="cursorCol">Current cursor column (0-based)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for blank characters</param>
    /// <param name="currentCharacterProtection">Current character protection status for blank characters</param>
    public void EraseCharactersInLine(int count, int cursorRow, int cursorCol, SgrAttributes currentSgrAttributes, bool currentCharacterProtection)
    {
        if (count <= 0)
        {
            return;
        }
        if (cursorRow < 0 || cursorRow >= Height)
        {
            return;
        }
        if (cursorCol < 0 || cursorCol >= Width)
        {
            return;
        }

        // Calculate the actual number of characters to erase (limited by remaining characters in line)
        int n = Math.Min(count, Width - cursorCol);
        if (n <= 0)
        {
            return;
        }

        // Replace characters with blank characters (no shifting)
        var blankCell = new Cell(' ', currentSgrAttributes, currentCharacterProtection);
        for (int x = cursorCol; x < cursorCol + n; x++)
        {
            _screenBuffer.SetCell(cursorRow, x, blankCell);
        }
    }
}