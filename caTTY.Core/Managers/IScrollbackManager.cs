namespace caTTY.Core.Managers;

using caTTY.Core.Types;

/// <summary>
///     Interface for managing scrollback buffer operations and viewport management.
///     Handles historical lines that have scrolled off the top of the screen.
/// </summary>
public interface IScrollbackManager
{
    /// <summary>
    ///     Maximum number of lines that can be stored in the scrollback buffer.
    /// </summary>
    int MaxLines { get; }

    /// <summary>
    ///     Current number of lines stored in the scrollback buffer.
    /// </summary>
    int CurrentLines { get; }

    /// <summary>
    ///     Current viewport offset from the bottom of the scrollback buffer.
    ///     0 means viewing the most recent content (bottom), positive values scroll up into history.
    /// </summary>
    int ViewportOffset { get; set; }

    /// <summary>
    ///     Whether the viewport is currently at the bottom (showing most recent content).
    /// </summary>
    bool IsAtBottom { get; }

    /// <summary>
    ///     Adds a line to the scrollback buffer. If the buffer is full, the oldest line is removed.
    /// </summary>
    /// <param name="line">The line to add to scrollback</param>
    void AddLine(ReadOnlySpan<Cell> line);

    /// <summary>
    ///     Gets a line from the scrollback buffer by index.
    /// </summary>
    /// <param name="index">Index of the line (0 = oldest, CurrentLines-1 = newest)</param>
    /// <returns>The requested line as a read-only span</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range</exception>
    ReadOnlySpan<Cell> GetLine(int index);

    /// <summary>
    ///     Clears all lines from the scrollback buffer and resets viewport to bottom.
    /// </summary>
    void Clear();

    /// <summary>
    ///     Sets the viewport offset, clamping to valid range.
    /// </summary>
    /// <param name="offset">Offset from bottom (0 = bottom, positive = scroll up)</param>
    void SetViewportOffset(int offset);

    /// <summary>
    ///     Whether auto-scroll is enabled (follows new content automatically).
    ///     Disabled when user scrolls up, re-enabled when they return to bottom.
    /// </summary>
    bool AutoScrollEnabled { get; }

    /// <summary>
    ///     Scrolls the viewport up by the specified number of lines.
    ///     Disables auto-scroll if not already at the top.
    /// </summary>
    /// <param name="lines">Number of lines to scroll up</param>
    void ScrollUp(int lines);

    /// <summary>
    ///     Scrolls the viewport down by the specified number of lines.
    ///     Re-enables auto-scroll if scrolled to the bottom.
    /// </summary>
    /// <param name="lines">Number of lines to scroll down</param>
    void ScrollDown(int lines);

    /// <summary>
    ///     Scrolls to the top of the scrollback buffer.
    ///     Disables auto-scroll.
    /// </summary>
    void ScrollToTop();

    /// <summary>
    ///     Scrolls to the bottom of the scrollback buffer.
    ///     Re-enables auto-scroll.
    /// </summary>
    void ScrollToBottom();

    /// <summary>
    ///     Called when the user provides input (keypress/typing) while the terminal has focus.
    ///     This should snap the viewport to the most recent content and re-enable auto-scroll.
    /// </summary>
    void OnUserInput();

    /// <summary>
    ///     Called when new content is added to notify viewport management.
    ///     If auto-scroll is enabled, automatically scrolls to show new content.
    /// </summary>
    /// <summary>
    ///     Called when new content is added to notify viewport management.
    ///     If auto-scroll is enabled, automatically scrolls to show new content.
    ///     If auto-scroll is disabled (user scrolled up), adjusts the viewport offset to keep
    ///     the visible content stable as new rows are appended.
    /// </summary>
    /// <param name="linesAdded">Number of rows appended since the last notification.</param>
    void OnNewContentAdded(int linesAdded = 1);

    /// <summary>
    ///     Gets viewport rows combining scrollback and screen buffer content.
    /// </summary>
    /// <param name="screenBuffer">Current screen buffer</param>
    /// <param name="isAlternateScreenActive">Whether alternate screen is active</param>
    /// <param name="requestedRows">Number of rows to return</param>
    /// <returns>List of rows for display, with scrollback content first</returns>
    List<ReadOnlyMemory<Cell>> GetViewportRows(ReadOnlyMemory<Cell>[] screenBuffer, bool isAlternateScreenActive, int requestedRows);

    /// <summary>
    ///     Gets viewport rows combining scrollback and screen buffer content, using a pre-allocated result list.
    ///     This overload avoids allocations by reusing the provided list and not copying row data.
    ///     The returned ReadOnlyMemory references point directly to internal storage (scrollback) or the provided
    ///     screen buffer, so they must not be stored beyond the current frame.
    /// </summary>
    /// <param name="screenBuffer">Current screen buffer (must remain valid while result is in use)</param>
    /// <param name="isAlternateScreenActive">Whether alternate screen is active</param>
    /// <param name="requestedRows">Number of rows to return</param>
    /// <param name="result">Pre-allocated list to fill with row references (will be cleared first)</param>
    void GetViewportRowsNonAlloc(ReadOnlyMemory<Cell>[] screenBuffer, bool isAlternateScreenActive, int requestedRows, List<ReadOnlyMemory<Cell>> result);
}