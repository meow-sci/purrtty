using System.Collections;

namespace caTTY.Core.Types;

/// <summary>
///     Implementation of IScreenBuffer using a jagged array of cells.
///     This allows GetRow() to return spans without allocation.
///     All cells are initialized to the default empty cell (space character).
///     Includes dirty row tracking for incremental rendering optimization.
/// </summary>
public class ScreenBuffer : IScreenBuffer
{
    private Cell[][] _rows;
    private int _width;
    private int _height;
    
    /// <summary>
    ///     Tracks which rows have been modified since last ClearDirtyFlags().
    ///     Used for incremental rendering optimization.
    /// </summary>
    private BitArray _dirtyRows;
    
    /// <summary>
    ///     Cached flag indicating if any row is dirty.
    ///     Avoids scanning the entire BitArray when no changes have occurred.
    /// </summary>
    private bool _anyRowDirty;

    /// <summary>
    ///     Current content revision number.
    /// </summary>
    public long Revision { get; private set; }

    /// <summary>
    ///     Creates a new screen buffer with the specified dimensions.
    ///     All cells are initialized to empty (space character).
    /// </summary>
    /// <param name="width">Width in columns</param>
    /// <param name="height">Height in rows</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when width or height is less than 1</exception>
    public ScreenBuffer(int width, int height)
    {
        if (width < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be at least 1");
        }

        if (height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be at least 1");
        }

        _width = width;
        _height = height;
        
        // Use jagged array for efficient row access without allocation
        _rows = new Cell[height][];
        for (int row = 0; row < height; row++)
        {
            _rows[row] = new Cell[width];
        }

        // Initialize dirty tracking - all rows start dirty to force initial render
        _dirtyRows = new BitArray(height, true);
        _anyRowDirty = true;

        // Initialize all cells to empty (space character)
        Clear();
    }

    /// <summary>
    ///     Gets the width of the screen buffer in columns.
    /// </summary>
    public int Width => _width;

    /// <summary>
    ///     Gets the height of the screen buffer in rows.
    /// </summary>
    public int Height => _height;

    /// <summary>
    ///     Gets the cell at the specified position.
    ///     Returns Empty cell if coordinates are out of bounds.
    /// </summary>
    /// <param name="row">The row index (0-based)</param>
    /// <param name="col">The column index (0-based)</param>
    /// <returns>The cell at the specified position</returns>
    public Cell GetCell(int row, int col)
    {
        if (!IsInBounds(row, col))
        {
            return Cell.Empty;
        }

        return _rows[row][col];
    }

    /// <summary>
    ///     Sets the cell at the specified position.
    ///     Ignores the operation if coordinates are out of bounds.
    /// </summary>
    /// <param name="row">The row index (0-based)</param>
    /// <param name="col">The column index (0-based)</param>
    /// <param name="cell">The cell to set</param>
    public void SetCell(int row, int col, Cell cell)
    {
        if (!IsInBounds(row, col))
        {
            return;
        }

        _rows[row][col] = cell;
        MarkRowDirty(row);
    }

    /// <summary>
    ///     Clears the entire screen buffer, setting all cells to empty.
    /// </summary>
    public void Clear()
    {
        Cell emptyCell = Cell.Empty;
        for (int row = 0; row < _height; row++)
        {
            var rowData = _rows[row];
            for (int col = 0; col < _width; col++)
            {
                rowData[col] = emptyCell;
            }
        }
        MarkAllRowsDirty();
    }

    /// <summary>
    ///     Clears a specific row, setting all cells in that row to empty.
    /// </summary>
    /// <param name="row">The row index to clear</param>
    public void ClearRow(int row)
    {
        if (row < 0 || row >= _height)
        {
            return;
        }

        Cell emptyCell = Cell.Empty;
        var rowData = _rows[row];
        for (int col = 0; col < _width; col++)
        {
            rowData[col] = emptyCell;
        }
        MarkRowDirty(row);
    }

    /// <summary>
    ///     Clears a region of the screen buffer.
    /// </summary>
    /// <param name="startRow">Starting row (inclusive)</param>
    /// <param name="startCol">Starting column (inclusive)</param>
    /// <param name="endRow">Ending row (inclusive)</param>
    /// <param name="endCol">Ending column (inclusive)</param>
    public void ClearRegion(int startRow, int startCol, int endRow, int endCol)
    {
        // Clamp coordinates to valid bounds
        startRow = Math.Max(0, Math.Min(startRow, _height - 1));
        startCol = Math.Max(0, Math.Min(startCol, _width - 1));
        endRow = Math.Max(0, Math.Min(endRow, _height - 1));
        endCol = Math.Max(0, Math.Min(endCol, _width - 1));

        // Ensure start <= end
        if (startRow > endRow || startCol > endCol)
        {
            return;
        }

        Cell emptyCell = Cell.Empty;
        for (int row = startRow; row <= endRow; row++)
        {
            var rowData = _rows[row];
            for (int col = startCol; col <= endCol; col++)
            {
                rowData[col] = emptyCell;
            }
            MarkRowDirty(row);
        }
    }

    /// <summary>
    ///     Gets a read-only span of cells for the specified row.
    ///     Returns empty span if row is out of bounds.
    ///     This method does NOT allocate - it returns a span directly over the internal row array.
    /// </summary>
    /// <param name="row">The row index</param>
    /// <returns>Read-only span of cells in the row</returns>
    public ReadOnlySpan<Cell> GetRow(int row)
    {
        if (row < 0 || row >= _height)
        {
            return ReadOnlySpan<Cell>.Empty;
        }

        // Return span directly from the jagged array row - no allocation!
        return _rows[row].AsSpan();
    }

    /// <summary>
    ///     Gets a read-only memory of cells for the specified row.
    ///     Returns empty memory if row is out of bounds.
    ///     This method does NOT allocate - it returns memory directly over the internal row array.
    /// </summary>
    /// <param name="row">The row index</param>
    /// <returns>Read-only memory of cells in the row</returns>
    public ReadOnlyMemory<Cell> GetRowMemory(int row)
    {
        if (row < 0 || row >= _height)
        {
            return ReadOnlyMemory<Cell>.Empty;
        }

        // Return memory directly from the jagged array row - no allocation!
        return _rows[row].AsMemory();
    }

    /// <summary>
    ///     Scrolls the buffer up by the specified number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to scroll up</param>
    public void ScrollUp(int lines)
    {
        if (lines <= 0 || lines >= _height)
        {
            // If scrolling entire buffer or more, just clear it
            if (lines >= _height)
            {
                Clear();
            }
            return;
        }

        // Move row references up (efficient for jagged arrays - just swap references)
        for (int row = 0; row < _height - lines; row++)
        {
            // Swap the row arrays instead of copying cell by cell
            (_rows[row], _rows[row + lines]) = (_rows[row + lines], _rows[row]);
        }

        // Clear the bottom rows (the old top rows that were swapped down)
        Cell emptyCell = Cell.Empty;
        for (int row = _height - lines; row < _height; row++)
        {
            var rowData = _rows[row];
            for (int col = 0; col < _width; col++)
            {
                rowData[col] = emptyCell;
            }
        }
        
        // All rows have effectively changed after a scroll
        MarkAllRowsDirty();
    }

    /// <summary>
    ///     Scrolls the buffer down by the specified number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to scroll down</param>
    public void ScrollDown(int lines)
    {
        if (lines <= 0 || lines >= _height)
        {
            // If scrolling entire buffer or more, just clear it
            if (lines >= _height)
            {
                Clear();
            }
            return;
        }

        // Move row references down (efficient for jagged arrays - just swap references)
        for (int row = _height - 1; row >= lines; row--)
        {
            // Swap the row arrays instead of copying cell by cell
            (_rows[row], _rows[row - lines]) = (_rows[row - lines], _rows[row]);
        }

        // Clear the top rows (the old bottom rows that were swapped up)
        Cell emptyCell = Cell.Empty;
        for (int row = 0; row < lines; row++)
        {
            var rowData = _rows[row];
            for (int col = 0; col < _width; col++)
            {
                rowData[col] = emptyCell;
            }
        }
        
        // All rows have effectively changed after a scroll
        MarkAllRowsDirty();
    }

    /// <summary>
    ///     Copies a range of rows to the specified destination span.
    /// </summary>
    /// <param name="destination">Destination span to copy to</param>
    /// <param name="startRow">Starting row (0-based, inclusive)</param>
    /// <param name="endRow">Ending row (0-based, inclusive)</param>
    public void CopyTo(Span<Cell> destination, int startRow, int endRow)
    {
        if (startRow < 0 || startRow >= _height || endRow < 0 || endRow >= _height)
        {
            return;
        }

        if (startRow > endRow)
        {
            (startRow, endRow) = (endRow, startRow);
        }

        int rowCount = endRow - startRow + 1;
        int totalCells = rowCount * _width;

        if (destination.Length < totalCells)
        {
            return; // Not enough space in destination
        }

        int destIndex = 0;
        for (int row = startRow; row <= endRow; row++)
        {
            var rowData = _rows[row];
            for (int col = 0; col < _width; col++)
            {
                destination[destIndex++] = rowData[col];
            }
        }
    }

    /// <summary>
    ///     Resizes the screen buffer to the specified dimensions with content preservation.
    ///     Height change: preserve top-to-bottom rows where possible.
    ///     Width change: truncate/pad each row; do not attempt complex reflow.
    /// </summary>
    /// <param name="newWidth">New width in columns</param>
    /// <param name="newHeight">New height in rows</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when dimensions are invalid</exception>
    public void Resize(int newWidth, int newHeight)
    {
        if (newWidth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(newWidth), "Width must be at least 1");
        }

        if (newHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(newHeight), "Height must be at least 1");
        }

        // If dimensions are the same, no work needed
        if (newWidth == _width && newHeight == _height)
        {
            return;
        }

        // Create new jagged array with new dimensions
        var newRows = new Cell[newHeight][];
        Cell emptyCell = Cell.Empty;

        // Initialize all new rows
        for (int row = 0; row < newHeight; row++)
        {
            newRows[row] = new Cell[newWidth];
            // Initialize to empty cells
            for (int col = 0; col < newWidth; col++)
            {
                newRows[row][col] = emptyCell;
            }
        }

        // Copy existing content with preservation policy
        int rowsToCopy = Math.Min(_height, newHeight);
        int colsToCopy = Math.Min(_width, newWidth);

        for (int row = 0; row < rowsToCopy; row++)
        {
            var srcRow = _rows[row];
            var dstRow = newRows[row];
            for (int col = 0; col < colsToCopy; col++)
            {
                dstRow[col] = srcRow[col];
            }
        }

        // Update internal state
        _rows = newRows;
        _width = newWidth;
        _height = newHeight;
        
        // Resize dirty tracking array and mark all rows as dirty
        _dirtyRows = new BitArray(newHeight, true);
        _anyRowDirty = true;
    }

    /// <summary>
    ///     Checks if the specified coordinates are within bounds.
    /// </summary>
    /// <param name="row">The row index</param>
    /// <param name="col">The column index</param>
    /// <returns>True if coordinates are valid, false otherwise</returns>
    public bool IsInBounds(int row, int col)
    {
        return row >= 0 && row < _height && col >= 0 && col < _width;
    }

    /// <summary>
    ///     Checks if the specified row has been modified since the last call to <see cref="ClearDirtyFlags"/>.
    /// </summary>
    /// <param name="row">The row index</param>
    /// <returns>True if the row has been modified, false otherwise</returns>
    public bool IsRowDirty(int row)
    {
        if (row < 0 || row >= _height)
        {
            return false;
        }
        return _dirtyRows[row];
    }

    /// <summary>
    ///     Checks if any row has been modified since the last call to <see cref="ClearDirtyFlags"/>.
    /// </summary>
    /// <returns>True if any row is dirty, false otherwise</returns>
    public bool HasAnyDirtyRows()
    {
        return _anyRowDirty;
    }

    /// <summary>
    ///     Clears all dirty flags, marking all rows as clean.
    /// </summary>
    public void ClearDirtyFlags()
    {
        _dirtyRows.SetAll(false);
        _anyRowDirty = false;
    }

    /// <summary>
    ///     Marks all rows as dirty, forcing a full re-render.
    /// </summary>
    public void MarkAllRowsDirty()
    {
        _dirtyRows.SetAll(true);
        _anyRowDirty = true;
        Revision++;
    }

    /// <summary>
    ///     Marks a specific row as dirty.
    /// </summary>
    /// <param name="row">The row index to mark as dirty</param>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void MarkRowDirty(int row)
    {
        if (row >= 0 && row < _height)
        {
            _dirtyRows[row] = true;
            _anyRowDirty = true;
            Revision++;
        }
    }
}
