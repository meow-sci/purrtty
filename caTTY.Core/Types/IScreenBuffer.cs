namespace caTTY.Core.Types;

/// <summary>
///     Interface for a terminal screen buffer that manages a 2D grid of cells.
///     Provides operations for cell access and clearing operations needed by CSI erase modes.
/// </summary>
public interface IScreenBuffer
{
    /// <summary>
    ///     Gets the width of the screen buffer in columns.
    /// </summary>
    int Width { get; }

    /// <summary>
    ///     Gets the height of the screen buffer in rows.
    /// </summary>
    int Height { get; }

    /// <summary>
    ///     Gets the current content revision number.
    ///     Increments whenever the buffer content is modified (cells set, cleared, scrolled, or resized).
    /// </summary>
    long Revision { get; }

    /// <summary>
    ///     Gets the cell at the specified position.
    ///     Returns Empty cell if coordinates are out of bounds.
    /// </summary>
    /// <param name="row">The row index (0-based)</param>
    /// <param name="col">The column index (0-based)</param>
    /// <returns>The cell at the specified position</returns>
    Cell GetCell(int row, int col);

    /// <summary>
    ///     Sets the cell at the specified position.
    ///     Ignores the operation if coordinates are out of bounds.
    /// </summary>
    /// <param name="row">The row index (0-based)</param>
    /// <param name="col">The column index (0-based)</param>
    /// <param name="cell">The cell to set</param>
    void SetCell(int row, int col, Cell cell);

    /// <summary>
    ///     Clears the entire screen buffer, setting all cells to empty.
    /// </summary>
    void Clear();

    /// <summary>
    ///     Clears a specific row, setting all cells in that row to empty.
    /// </summary>
    /// <param name="row">The row index to clear</param>
    void ClearRow(int row);

    /// <summary>
    ///     Clears a region of the screen buffer.
    /// </summary>
    /// <param name="startRow">Starting row (inclusive)</param>
    /// <param name="startCol">Starting column (inclusive)</param>
    /// <param name="endRow">Ending row (inclusive)</param>
    /// <param name="endCol">Ending column (inclusive)</param>
    void ClearRegion(int startRow, int startCol, int endRow, int endCol);

    /// <summary>
    ///     Gets a read-only span of cells for the specified row.
    ///     Returns empty span if row is out of bounds.
    /// </summary>
    /// <param name="row">The row index</param>
    /// <returns>Read-only span of cells in the row</returns>
    ReadOnlySpan<Cell> GetRow(int row);

    /// <summary>
    ///     Gets a read-only memory of cells for the specified row.
    ///     Returns empty memory if row is out of bounds.
    ///     This method is useful for deferred access where a span cannot be used.
    /// </summary>
    /// <param name="row">The row index</param>
    /// <returns>Read-only memory of cells in the row</returns>
    ReadOnlyMemory<Cell> GetRowMemory(int row);

    /// <summary>
    ///     Scrolls the buffer up by the specified number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to scroll up</param>
    void ScrollUp(int lines);

    /// <summary>
    ///     Scrolls the buffer down by the specified number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to scroll down</param>
    void ScrollDown(int lines);

    /// <summary>
    ///     Copies a range of rows to the specified destination span.
    /// </summary>
    /// <param name="destination">Destination span to copy to</param>
    /// <param name="startRow">Starting row (0-based, inclusive)</param>
    /// <param name="endRow">Ending row (0-based, inclusive)</param>
    void CopyTo(Span<Cell> destination, int startRow, int endRow);

    /// <summary>
    ///     Resizes the screen buffer to the specified dimensions with content preservation.
    ///     Height change: preserve top-to-bottom rows where possible.
    ///     Width change: truncate/pad each row; do not attempt complex reflow.
    /// </summary>
    /// <param name="newWidth">New width in columns</param>
    /// <param name="newHeight">New height in rows</param>
    void Resize(int newWidth, int newHeight);

    /// <summary>
    ///     Checks if the specified coordinates are within bounds.
    /// </summary>
    /// <param name="row">The row index</param>
    /// <param name="col">The column index</param>
    /// <returns>True if coordinates are valid, false otherwise</returns>
    bool IsInBounds(int row, int col);

    /// <summary>
    ///     Checks if the specified row has been modified since the last call to <see cref="ClearDirtyFlags"/>.
    ///     Used for incremental rendering optimization.
    /// </summary>
    /// <param name="row">The row index</param>
    /// <returns>True if the row has been modified, false otherwise</returns>
    bool IsRowDirty(int row);

    /// <summary>
    ///     Checks if any row has been modified since the last call to <see cref="ClearDirtyFlags"/>.
    ///     Used for quick early-out in rendering.
    /// </summary>
    /// <returns>True if any row is dirty, false otherwise</returns>
    bool HasAnyDirtyRows();

    /// <summary>
    ///     Clears all dirty flags, marking all rows as clean.
    ///     Should be called after rendering is complete.
    /// </summary>
    void ClearDirtyFlags();

    /// <summary>
    ///     Marks all rows as dirty, forcing a full re-render.
    ///     Used after resize or screen switch operations.
    /// </summary>
    void MarkAllRowsDirty();
}
