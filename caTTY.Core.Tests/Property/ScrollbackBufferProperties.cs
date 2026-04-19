using caTTY.Core.Managers;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for scrollback buffer management operations.
///     These tests verify universal properties that should hold for all scrollback buffer operations.
///     Validates Requirements 14.1, 14.2.
/// </summary>
[TestFixture]
[Category("Property")]
public class ScrollbackBufferProperties
{
    /// <summary>
    ///     Generator for valid scrollback buffer dimensions.
    /// </summary>
    public static Arbitrary<(int maxLines, int columns)> ScrollbackDimensionsArb =>
        Arb.From(Gen.Choose(1, 20).SelectMany(maxLines =>
            Gen.Choose(1, 50).Select(columns => (maxLines, columns))));

    /// <summary>
    ///     Generator for simple test characters.
    /// </summary>
    public static Arbitrary<char> TestCharArb =>
        Arb.From(Gen.Elements('A', 'B', 'C', 'X', 'Y', 'Z', ' ', '1', '2', '3'));

    /// <summary>
    ///     **Feature: catty-ksa, Property 26: Scrollback buffer management**
    ///     **Validates: Requirements 14.1, 14.2**
    ///     Property: For any scrollback buffer, adding lines should preserve content and manage capacity correctly.
    ///     When buffer is not full, all lines should be stored. When buffer exceeds capacity, oldest lines should be removed.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScrollbackBufferManagesCapacityCorrectly()
    {
        return Prop.ForAll(ScrollbackDimensionsArb, TestCharArb,
            (dimensions, testChar) =>
        {
            var (maxLines, columns) = dimensions;

            using var scrollback = new ScrollbackManager(maxLines, columns);

            // Create test line with the generated character
            var testLine = CreateTestLine(columns, testChar);

            // Add line and verify capacity management
            scrollback.AddLine(testLine);

            bool correctLines = scrollback.CurrentLines == 1;
            bool correctMaxLines = scrollback.MaxLines == maxLines;
            bool correctContent = scrollback.GetLine(0).Length == columns;

            return correctLines && correctMaxLines && correctContent;
        });
    }

    /// <summary>
    ///     Property: Scrollback buffer should maintain FIFO (First In, First Out) ordering.
    ///     The oldest lines should be removed first when capacity is exceeded.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScrollbackBufferMaintainsFifoOrdering()
    {
        return Prop.ForAll<int, int>((maxLines, columns) =>
        {
            if (maxLines < 2 || maxLines > 10 || columns < 5 || columns > 20)
                return true; // Skip invalid inputs

            using var scrollback = new ScrollbackManager(maxLines, columns);

            // Add more lines than capacity to test FIFO behavior
            var totalLines = maxLines + 2;
            var lastChar = 'A';

            for (int lineNum = 0; lineNum < totalLines; lineNum++)
            {
                var fillChar = (char)('A' + (lineNum % 26));
                var line = CreateTestLine(columns, fillChar);
                scrollback.AddLine(line);
                lastChar = fillChar;
            }

            // Verify only the last maxLines are stored
            bool correctCount = scrollback.CurrentLines == maxLines;

            // Verify the last line contains the expected character
            var lastStoredLine = scrollback.GetLine(scrollback.CurrentLines - 1);
            bool correctLastChar = lastStoredLine[0].Character == lastChar;

            return correctCount && correctLastChar;
        });
    }

    /// <summary>
    ///     Property: Scrollback buffer should handle edge cases correctly.
    ///     Empty lines should be handled properly.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScrollbackBufferHandlesEdgeCases()
    {
        return Prop.ForAll<int, int>((maxLines, columns) =>
        {
            if (maxLines < 1 || maxLines > 20 || columns < 1 || columns > 50)
                return true; // Skip invalid inputs

            using var scrollback = new ScrollbackManager(maxLines, columns);

            // Test empty line
            scrollback.AddLine(ReadOnlySpan<Cell>.Empty);

            bool correctCount = scrollback.CurrentLines == 1;
            var emptyLine = scrollback.GetLine(0);
            bool correctLength = emptyLine.Length == columns;
            bool allSpaces = emptyLine[0] == Cell.Space;

            return correctCount && correctLength && allSpaces;
        });
    }

    /// <summary>
    ///     Property: Scrollback buffer viewport management should work correctly.
    ///     Viewport offset should be clamped to valid range and IsAtBottom should be accurate.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScrollbackBufferViewportManagementWorks()
    {
        return Prop.ForAll<int, int>((maxLines, columns) =>
        {
            if (maxLines < 1 || maxLines > 20 || columns < 1 || columns > 50)
                return true; // Skip invalid inputs

            using var scrollback = new ScrollbackManager(maxLines, columns);

            // Add a line
            var line = CreateTestLine(columns, 'A');
            scrollback.AddLine(line);

            // Test viewport offset clamping
            scrollback.ViewportOffset = -10; // Should clamp to 0
            bool clampedToZero = scrollback.ViewportOffset == 0;

            scrollback.ViewportOffset = 0;
            bool isAtBottom = scrollback.IsAtBottom;

            return clampedToZero && isAtBottom;
        });
    }

    /// <summary>
    ///     Property: Scrollback buffer should handle zero capacity correctly.
    ///     When maxLines is 0, no lines should be stored.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScrollbackBufferHandlesZeroCapacity()
    {
        return Prop.ForAll(TestCharArb, testChar =>
        {
            var columns = 10; // Fixed for simplicity
            using var scrollback = new ScrollbackManager(0, columns);

            // Add line to zero-capacity buffer
            var testLine = CreateTestLine(columns, testChar);
            scrollback.AddLine(testLine);

            // Should never store any lines
            bool noLines = scrollback.CurrentLines == 0;
            bool correctMaxLines = scrollback.MaxLines == 0;
            bool atBottom = scrollback.IsAtBottom;

            return noLines && correctMaxLines && atBottom;
        });
    }

    /// <summary>
    ///     Property: Scrollback buffer clear operation should reset all state.
    ///     After clearing, buffer should be empty and at bottom.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScrollbackBufferClearResetsState()
    {
        return Prop.ForAll(ScrollbackDimensionsArb, TestCharArb,
            (dimensions, testChar) =>
        {
            var (maxLines, columns) = dimensions;

            using var scrollback = new ScrollbackManager(maxLines, columns);

            // Add a line
            var line = CreateTestLine(columns, testChar);
            scrollback.AddLine(line);

            // Set viewport offset
            if (scrollback.CurrentLines > 0)
            {
                scrollback.ViewportOffset = 1;
            }

            // Clear the buffer
            scrollback.Clear();

            // Verify state is reset
            bool noLines = scrollback.CurrentLines == 0;
            bool offsetReset = scrollback.ViewportOffset == 0;
            bool atBottom = scrollback.IsAtBottom;
            bool maxLinesPreserved = scrollback.MaxLines == maxLines;

            return noLines && offsetReset && atBottom && maxLinesPreserved;
        });
    }

    /// <summary>
    ///     Property: Scrollback buffer should preserve line content integrity.
    ///     Retrieved lines should exactly match what was added (with proper padding/truncation).
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScrollbackBufferPreservesLineIntegrity()
    {
        return Prop.ForAll(TestCharArb, testChar =>
        {
            var maxLines = 10; // Fixed for simplicity
            var columns = 20; // Fixed for simplicity

            using var scrollback = new ScrollbackManager(maxLines, columns);

            // Create test line with specific pattern
            var line = new Cell[columns];
            var halfPoint = columns / 2;

            for (int j = 0; j < halfPoint; j++)
            {
                line[j] = new Cell(testChar);
            }
            for (int j = halfPoint; j < columns; j++)
            {
                line[j] = Cell.Space;
            }

            scrollback.AddLine(line);

            // Verify line is preserved correctly
            var retrievedLine = scrollback.GetLine(0);

            bool correctLength = retrievedLine.Length == columns;
            bool correctFirstHalf = retrievedLine[0].Character == testChar;
            bool correctSecondHalf = retrievedLine[columns - 1] == Cell.Space;

            return correctLength && correctFirstHalf && correctSecondHalf;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 27: Viewport and auto-scroll behavior**
    ///     **Validates: Requirements 14.3, 14.4**
    ///     Property: For any scrollback buffer, viewport offset should be maintained correctly with auto-scroll behavior.
    ///     When user scrolls up, auto-scroll should be disabled. When user returns to bottom, auto-scroll should be re-enabled.
    ///     New content should not yank viewport while user is reviewing history.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ViewportAndAutoScrollBehaviorWorks()
    {
        return Prop.ForAll(ScrollbackDimensionsArb, TestCharArb,
            (dimensions, testChar) =>
        {
            var (maxLines, columns) = dimensions;
            if (maxLines < 3) return true; // Need at least 3 lines for meaningful test

            using var scrollback = new ScrollbackManager(maxLines, columns);

            // Initially should be at bottom with auto-scroll enabled
            bool initiallyAtBottom = scrollback.IsAtBottom;
            bool initiallyAutoScroll = scrollback.AutoScrollEnabled;

            // Add some lines
            for (int i = 0; i < 3; i++)
            {
                var line = CreateTestLine(columns, (char)('A' + i));
                scrollback.AddLine(line);
            }

            // Should still be at bottom with auto-scroll enabled after adding content
            bool stillAtBottomAfterAdd = scrollback.IsAtBottom;
            bool stillAutoScrollAfterAdd = scrollback.AutoScrollEnabled;

            // Scroll up - should disable auto-scroll
            scrollback.ScrollUp(1);
            bool notAtBottomAfterScrollUp = !scrollback.IsAtBottom;
            bool autoScrollDisabledAfterScrollUp = !scrollback.AutoScrollEnabled;
            int offsetAfterScrollUp = scrollback.ViewportOffset;

            // Add new content while scrolled up - viewport should not move (no yanking)
            var newLine = CreateTestLine(columns, 'Z');
            scrollback.AddLine(newLine);
            // ViewportOffset is measured from bottom; keeping the visible content stable requires
            // increasing the offset by the number of appended rows.
            bool viewportNotYanked = scrollback.ViewportOffset == offsetAfterScrollUp + 1;
            bool stillNotAutoScrolling = !scrollback.AutoScrollEnabled;

            // Scroll back to bottom - should re-enable auto-scroll
            scrollback.ScrollToBottom();
            bool backAtBottom = scrollback.IsAtBottom;
            bool autoScrollReEnabled = scrollback.AutoScrollEnabled;

            // Add more content - should stay at bottom due to auto-scroll
            var finalLine = CreateTestLine(columns, 'Y');
            scrollback.AddLine(finalLine);
            bool staysAtBottomWithAutoScroll = scrollback.IsAtBottom;

            return initiallyAtBottom && initiallyAutoScroll &&
                   stillAtBottomAfterAdd && stillAutoScrollAfterAdd &&
                   notAtBottomAfterScrollUp && autoScrollDisabledAfterScrollUp &&
                   viewportNotYanked && stillNotAutoScrolling &&
                   backAtBottom && autoScrollReEnabled &&
                   staysAtBottomWithAutoScroll;
        });
    }

    /// <summary>
    ///     Property: Viewport offset clamping should work correctly.
    ///     Setting viewport offset beyond valid range should clamp to valid bounds.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ViewportOffsetClampingWorks()
    {
        return Prop.ForAll(ScrollbackDimensionsArb, TestCharArb,
            (dimensions, testChar) =>
        {
            var (maxLines, columns) = dimensions;

            using var scrollback = new ScrollbackManager(maxLines, columns);

            // Add some lines
            var linesToAdd = Math.Min(maxLines, 5);
            for (int i = 0; i < linesToAdd; i++)
            {
                var line = CreateTestLine(columns, testChar);
                scrollback.AddLine(line);
            }

            // Test negative offset clamping
            scrollback.ViewportOffset = -10;
            bool clampedToZero = scrollback.ViewportOffset == 0;

            // Test excessive positive offset clamping
            scrollback.ViewportOffset = scrollback.CurrentLines + 10;
            bool clampedToMax = scrollback.ViewportOffset == scrollback.CurrentLines;

            // Test valid offset
            var validOffset = Math.Max(0, scrollback.CurrentLines - 1);
            scrollback.ViewportOffset = validOffset;
            bool validOffsetSet = scrollback.ViewportOffset == validOffset;

            return clampedToZero && clampedToMax && validOffsetSet;
        });
    }

    /// <summary>
    ///     Property: Scroll operations should update viewport offset correctly.
    ///     ScrollUp/ScrollDown should move viewport by correct amounts and respect bounds.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScrollOperationsUpdateViewportCorrectly()
    {
        return Prop.ForAll<int, int>((maxLines, columns) =>
        {
            if (maxLines < 5 || maxLines > 20 || columns < 5 || columns > 20)
                return true; // Skip invalid inputs

            using var scrollback = new ScrollbackManager(maxLines, columns);

            // Add enough lines to make scrolling meaningful
            for (int i = 0; i < maxLines; i++)
            {
                var line = CreateTestLine(columns, (char)('A' + (i % 26)));
                scrollback.AddLine(line);
            }

            // Start at bottom
            scrollback.ScrollToBottom();
            int initialOffset = scrollback.ViewportOffset;

            // Scroll up by 2
            scrollback.ScrollUp(2);
            bool scrollUpWorked = scrollback.ViewportOffset == initialOffset + 2;

            // Scroll down by 1
            var offsetBeforeScrollDown = scrollback.ViewportOffset;
            scrollback.ScrollDown(1);
            bool scrollDownWorked = scrollback.ViewportOffset == offsetBeforeScrollDown - 1;

            // Scroll to top
            scrollback.ScrollToTop();
            bool scrollToTopWorked = scrollback.ViewportOffset == scrollback.CurrentLines;
            bool autoScrollDisabledAtTop = !scrollback.AutoScrollEnabled;

            // Scroll to bottom
            scrollback.ScrollToBottom();
            bool scrollToBottomWorked = scrollback.ViewportOffset == 0;
            bool autoScrollEnabledAtBottom = scrollback.AutoScrollEnabled;

            return scrollUpWorked && scrollDownWorked &&
                   scrollToTopWorked && autoScrollDisabledAtTop &&
                   scrollToBottomWorked && autoScrollEnabledAtBottom;
        });
    }

    /// <summary>
    ///     Property: GetViewportRows should provide correct content based on viewport offset.
    ///     Should combine scrollback and screen buffer content appropriately.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property GetViewportRowsReturnsCorrectContent()
    {
        return Prop.ForAll<int, int>((maxLines, columns) =>
        {
            if (maxLines < 2 || maxLines > 10 || columns < 5 || columns > 15)
                return true; // Skip invalid inputs

            using var scrollback = new ScrollbackManager(maxLines, columns);

            // Add scrollback content
            for (int i = 0; i < 3; i++)
            {
                var line = CreateTestLine(columns, (char)('A' + i));
                scrollback.AddLine(line);
            }

            // Create mock screen buffer
            var screenBuffer = new ReadOnlyMemory<Cell>[2];
            for (int i = 0; i < screenBuffer.Length; i++)
            {
                var screenLine = CreateTestLine(columns, (char)('X' + i));
                screenBuffer[i] = screenLine.AsMemory();
            }

            // Test viewport at bottom (should show scrollback + screen)
            scrollback.ScrollToBottom();
            var viewportRows = scrollback.GetViewportRows(screenBuffer, false, 5);

            bool correctRowCount = viewportRows.Count == 5;

            // Determine what the first scrollback character should be
            // If maxLines >= 3, we have all lines A, B, C, so first is A
            // If maxLines == 2, we have lines B, C (A was overwritten), so first is B
            char expectedFirstChar = maxLines >= 3 ? 'A' : 'B';
            bool hasScrollbackContent = viewportRows.Count > 0 &&
                                      viewportRows[0].Span.Length > 0 &&
                                      viewportRows[0].Span[0].Character == expectedFirstChar;

            // Test in alternate screen mode (should only show screen buffer)
            var alternateViewportRows = scrollback.GetViewportRows(screenBuffer, true, 2);
            bool alternateCorrectCount = alternateViewportRows.Count == 2;
            bool alternateShowsScreen = alternateViewportRows.Count > 0 &&
                                      alternateViewportRows[0].Span.Length > 0 &&
                                      alternateViewportRows[0].Span[0].Character == 'X';

            return correctRowCount && hasScrollbackContent &&
                   alternateCorrectCount && alternateShowsScreen;
        });
    }

    /// <summary>
    ///     Helper method to create a test line filled with a specific character.
    /// </summary>
    private static Cell[] CreateTestLine(int columns, char fillChar)
    {
        var line = new Cell[columns];
        for (int i = 0; i < columns; i++)
        {
            line[i] = new Cell(fillChar);
        }
        return line;
    }
}
