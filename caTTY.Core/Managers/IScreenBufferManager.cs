using caTTY.Core.Types;
using System;

namespace caTTY.Core.Managers;

/// <summary>
///     Interface for managing screen buffer operations including cell access, clearing, and resizing.
/// </summary>
public interface IScreenBufferManager
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
    ///     Gets a cell at the specified position.
    /// </summary>
    /// <param name="row">Row index (0-based)</param>
    /// <param name="col">Column index (0-based)</param>
    /// <returns>The cell at the specified position</returns>
    Cell GetCell(int row, int col);

    /// <summary>
    ///     Sets a cell at the specified position.
    /// </summary>
    /// <param name="row">Row index (0-based)</param>
    /// <param name="col">Column index (0-based)</param>
    /// <param name="cell">The cell to set</param>
    void SetCell(int row, int col, Cell cell);

    /// <summary>
    ///     Clears the entire screen buffer.
    /// </summary>
    void Clear();

    /// <summary>
    ///     Clears a specific region of the screen buffer.
    /// </summary>
    /// <param name="startRow">Starting row (0-based, inclusive)</param>
    /// <param name="startCol">Starting column (0-based, inclusive)</param>
    /// <param name="endRow">Ending row (0-based, inclusive)</param>
    /// <param name="endCol">Ending column (0-based, inclusive)</param>
    void ClearRegion(int startRow, int startCol, int endRow, int endCol);

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
    ///     Scrolls up within a specific scroll region.
    ///     Used for scroll regions defined by CSI r (DECSTBM).
    /// </summary>
    /// <param name="lines">Number of lines to scroll up</param>
    /// <param name="scrollTop">Top boundary of scroll region (0-based, inclusive)</param>
    /// <param name="scrollBottom">Bottom boundary of scroll region (0-based, inclusive)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for new blank lines</param>
    void ScrollUpInRegion(int lines, int scrollTop, int scrollBottom, SgrAttributes currentSgrAttributes);

    /// <summary>
    ///     Scrolls down within a specific scroll region.
    ///     Used for scroll regions defined by CSI r (DECSTBM).
    /// </summary>
    /// <param name="lines">Number of lines to scroll down</param>
    /// <param name="scrollTop">Top boundary of scroll region (0-based, inclusive)</param>
    /// <param name="scrollBottom">Bottom boundary of scroll region (0-based, inclusive)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for new blank lines</param>
    void ScrollDownInRegion(int lines, int scrollTop, int scrollBottom, SgrAttributes currentSgrAttributes);

    /// <summary>
    ///     Inserts blank lines at the cursor position within the scroll region.
    ///     Implements CSI L (Insert Lines) sequence.
    /// </summary>
    /// <param name="count">Number of lines to insert</param>
    /// <param name="cursorRow">Current cursor row (0-based)</param>
    /// <param name="scrollTop">Top boundary of scroll region (0-based, inclusive)</param>
    /// <param name="scrollBottom">Bottom boundary of scroll region (0-based, inclusive)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for new blank lines</param>
    /// <param name="currentCharacterProtection">Current character protection status for new blank lines</param>
    void InsertLinesInRegion(int count, int cursorRow, int scrollTop, int scrollBottom, SgrAttributes currentSgrAttributes, bool currentCharacterProtection);

    /// <summary>
    ///     Deletes lines at the cursor position within the scroll region.
    ///     Implements CSI M (Delete Lines) sequence.
    /// </summary>
    /// <param name="count">Number of lines to delete</param>
    /// <param name="cursorRow">Current cursor row (0-based)</param>
    /// <param name="scrollTop">Top boundary of scroll region (0-based, inclusive)</param>
    /// <param name="scrollBottom">Bottom boundary of scroll region (0-based, inclusive)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for new blank lines</param>
    /// <param name="currentCharacterProtection">Current character protection status for new blank lines</param>
    void DeleteLinesInRegion(int count, int cursorRow, int scrollTop, int scrollBottom, SgrAttributes currentSgrAttributes, bool currentCharacterProtection);

    /// <summary>
    ///     Inserts blank characters at the cursor position within the current line.
    ///     Implements CSI @ (Insert Characters) sequence.
    /// </summary>
    /// <param name="count">Number of characters to insert</param>
    /// <param name="cursorRow">Current cursor row (0-based)</param>
    /// <param name="cursorCol">Current cursor column (0-based)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for new blank characters</param>
    /// <param name="currentCharacterProtection">Current character protection status for new blank characters</param>
    void InsertCharactersInLine(int count, int cursorRow, int cursorCol, SgrAttributes currentSgrAttributes, bool currentCharacterProtection);

    /// <summary>
    ///     Deletes characters at the cursor position within the current line.
    ///     Implements CSI P (Delete Characters) sequence.
    /// </summary>
    /// <param name="count">Number of characters to delete</param>
    /// <param name="cursorRow">Current cursor row (0-based)</param>
    /// <param name="cursorCol">Current cursor column (0-based)</param>
    /// <param name="currentSgrAttributes">Current SGR attributes for new blank characters</param>
    /// <param name="currentCharacterProtection">Current character protection status for new blank characters</param>
    void DeleteCharactersInLine(int count, int cursorRow, int cursorCol, SgrAttributes currentSgrAttributes, bool currentCharacterProtection);

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
    void EraseCharactersInLine(int count, int cursorRow, int cursorCol, SgrAttributes currentSgrAttributes, bool currentCharacterProtection);

    /// <summary>
    ///     Sets the scrollback integration callbacks for proper scrollback behavior.
    /// </summary>
    /// <param name="pushScrollbackRow">Callback to push a row to scrollback buffer</param>
    /// <param name="isAlternateScreenActive">Function to check if alternate screen is active</param>
    void SetScrollbackIntegration(Action<ReadOnlySpan<Cell>>? pushScrollbackRow, Func<bool>? isAlternateScreenActive);

    /// <summary>
    ///     Resizes the screen buffer to the specified dimensions.
    /// </summary>
    /// <param name="width">New width in columns</param>
    /// <param name="height">New height in rows</param>
    void Resize(int width, int height);

    /// <summary>
    ///     Gets a read-only span of cells for the specified row.
    /// </summary>
    /// <param name="row">Row index (0-based)</param>
    /// <returns>Read-only span of cells for the row</returns>
    ReadOnlySpan<Cell> GetRow(int row);

    /// <summary>
    ///     Copies a range of rows to the specified destination span.
    /// </summary>
    /// <param name="destination">Destination span to copy to</param>
    /// <param name="startRow">Starting row (0-based, inclusive)</param>
    /// <param name="endRow">Ending row (0-based, inclusive)</param>
    void CopyTo(Span<Cell> destination, int startRow, int endRow);
}