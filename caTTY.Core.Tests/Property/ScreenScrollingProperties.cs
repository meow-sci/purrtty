using caTTY.Core.Terminal;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;
using Microsoft.Extensions.Logging.Abstractions;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for screen scrolling operations in the terminal emulator.
///     These tests verify universal properties that should hold for all screen scrolling operations.
///     Validates Requirements 11.8, 11.9.
/// </summary>
[TestFixture]
[Category("Property")]
public class ScreenScrollingProperties
{
    /// <summary>
    ///     Generator for valid terminal dimensions.
    /// </summary>
    public static Arbitrary<(int width, int height)> TerminalDimensionsArb =>
        Arb.From(Gen.Choose(5, 20).SelectMany(width =>
            Gen.Choose(3, 10).Select(height => (width, height))));

    /// <summary>
    ///     Generator for valid scroll line counts.
    /// </summary>
    public static Arbitrary<int> ScrollLinesArb =>
        Arb.From(Gen.Choose(1, 5));

    /// <summary>
    ///     Generator for simple test characters.
    /// </summary>
    public static Arbitrary<char> TestCharArb =>
        Arb.From(Gen.Elements('A', 'B', 'C', 'X', 'Y', 'Z', '1', '2', '3'));

    /// <summary>
    ///     **Feature: catty-ksa, Property 20: Screen scrolling operations**
    ///     **Validates: Requirements 11.8, 11.9**
    ///     Property: For any scroll up operation (CSI S), content should move up correctly and scrolled lines should be added to scrollback.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScrollUpMovesContentCorrectly()
    {
        return Prop.ForAll(TerminalDimensionsArb, ScrollLinesArb, TestCharArb,
            (dimensions, scrollLines, testChar) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Fill screen with test character pattern
            FillScreenWithPattern(terminal, testChar, width, height);

            // Get initial content for verification
            var initialContent = CaptureScreenContent(terminal, width, height);

            // Act: Scroll up by specified lines
            terminal.Write($"\x1b[{scrollLines}S");

            // Handle case where scrollLines >= height (entire screen should be cleared)
            if (scrollLines >= height)
            {
                // Entire screen should be cleared
                bool screenCleared = true;
                for (int row = 0; row < height && screenCleared; row++)
                {
                    for (int col = 0; col < width && screenCleared; col++)
                    {
                        var cell = terminal.ScreenBuffer.GetCell(row, col);
                        if (cell.Character != ' ')
                        {
                            screenCleared = false;
                        }
                    }
                }

                // Scrollback should receive all original lines
                bool scrollbackReceivedLines = terminal.ScrollbackManager.CurrentLines == height;

                return screenCleared && scrollbackReceivedLines;
            }

            // Normal case: scrollLines < height
            bool contentMovedCorrectly = true;

            // Check that content moved up by scrollLines positions
            for (int row = 0; row < height - scrollLines; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    var currentCell = terminal.ScreenBuffer.GetCell(row, col);
                    var expectedCell = initialContent[row + scrollLines, col];

                    if (currentCell.Character != expectedCell.Character)
                    {
                        contentMovedCorrectly = false;
                        break;
                    }
                }
                if (!contentMovedCorrectly) break;
            }

            // Check that bottom lines are cleared (empty)
            bool bottomLinesCleared = true;
            for (int row = height - scrollLines; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    var cell = terminal.ScreenBuffer.GetCell(row, col);
                    if (cell.Character != ' ')
                    {
                        bottomLinesCleared = false;
                        break;
                    }
                }
                if (!bottomLinesCleared) break;
            }

            // Check that scrollback received the scrolled lines
            bool scrollbackUpdated = terminal.ScrollbackManager.CurrentLines == scrollLines;

            return contentMovedCorrectly && bottomLinesCleared && scrollbackUpdated;
        });
    }

    /// <summary>
    ///     Property: For any scroll down operation (CSI T), content should move down correctly and top lines should be cleared.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScrollDownMovesContentCorrectly()
    {
        return Prop.ForAll(TerminalDimensionsArb, ScrollLinesArb, TestCharArb,
            (dimensions, scrollLines, testChar) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Fill screen with test character pattern
            FillScreenWithPattern(terminal, testChar, width, height);

            // Get initial content for verification
            var initialContent = CaptureScreenContent(terminal, width, height);

            // Act: Scroll down by specified lines
            terminal.Write($"\x1b[{scrollLines}T");

            // Handle case where scrollLines >= height (entire screen should be cleared)
            if (scrollLines >= height)
            {
                // Entire screen should be cleared
                bool screenCleared = true;
                for (int row = 0; row < height && screenCleared; row++)
                {
                    for (int col = 0; col < width && screenCleared; col++)
                    {
                        var cell = terminal.ScreenBuffer.GetCell(row, col);
                        if (cell.Character != ' ')
                        {
                            screenCleared = false;
                        }
                    }
                }

                return screenCleared;
            }

            // Normal case: scrollLines < height
            bool contentMovedCorrectly = true;

            // Check that content moved down by scrollLines positions
            for (int row = scrollLines; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    var currentCell = terminal.ScreenBuffer.GetCell(row, col);
                    var expectedCell = initialContent[row - scrollLines, col];

                    if (currentCell.Character != expectedCell.Character)
                    {
                        contentMovedCorrectly = false;
                        break;
                    }
                }
                if (!contentMovedCorrectly) break;
            }

            // Check that top lines are cleared (empty)
            bool topLinesCleared = true;
            for (int row = 0; row < scrollLines; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    var cell = terminal.ScreenBuffer.GetCell(row, col);
                    if (cell.Character != ' ')
                    {
                        topLinesCleared = false;
                        break;
                    }
                }
                if (!topLinesCleared) break;
            }

            return contentMovedCorrectly && topLinesCleared;
        });
    }

    /// <summary>
    ///     Property: Scrolling operations should preserve terminal state integrity.
    ///     After any scrolling operation, the terminal should remain in a valid state.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScrollingPreservesStateIntegrity()
    {
        return Prop.ForAll(TerminalDimensionsArb, ScrollLinesArb,
            (dimensions, scrollLines) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Position cursor at a known location
            terminal.Write($"\x1b[{height/2};{width/2}H");
            var initialCursorRow = terminal.Cursor.Row;
            var initialCursorCol = terminal.Cursor.Col;

            // Act: Perform scroll operations
            terminal.Write($"\x1b[{scrollLines}S"); // Scroll up
            terminal.Write($"\x1b[{scrollLines}T"); // Scroll down

            // Assert: Terminal should remain functional
            bool cursorValid = terminal.Cursor.Row >= 0 && terminal.Cursor.Row < height &&
                               terminal.Cursor.Col >= 0 && terminal.Cursor.Col < width;

            // Verify we can still write content
            terminal.Write("TEST");
            bool canWrite = terminal.Cursor.Row >= 0 && terminal.Cursor.Row < height &&
                            terminal.Cursor.Col >= 0 && terminal.Cursor.Col < width;

            // Verify dimensions are preserved
            bool dimensionsPreserved = terminal.Width == width && terminal.Height == height;

            return cursorValid && canWrite && dimensionsPreserved;
        });
    }

    /// <summary>
    ///     Property: Scrolling operations should be deterministic.
    ///     Applying the same scrolling operation should always produce the same result.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScrollingIsDeterministic()
    {
        return Prop.ForAll(TerminalDimensionsArb, ScrollLinesArb, TestCharArb,
            (dimensions, scrollLines, testChar) =>
        {
            var (width, height) = dimensions;

            // Create two identical terminals
            using var terminal1 = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);
            using var terminal2 = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Fill both with same pattern
            FillScreenWithPattern(terminal1, testChar, width, height);
            FillScreenWithPattern(terminal2, testChar, width, height);

            // Apply same scroll operation to both
            terminal1.Write($"\x1b[{scrollLines}S");
            terminal2.Write($"\x1b[{scrollLines}S");

            // Assert: Both terminals should have identical screen content
            bool identical = true;
            for (int row = 0; row < height && identical; row++)
            {
                for (int col = 0; col < width && identical; col++)
                {
                    var cell1 = terminal1.ScreenBuffer.GetCell(row, col);
                    var cell2 = terminal2.ScreenBuffer.GetCell(row, col);
                    if (cell1.Character != cell2.Character)
                    {
                        identical = false;
                    }
                }
            }

            // Check scrollback is also identical
            bool scrollbackIdentical = terminal1.ScrollbackManager.CurrentLines == terminal2.ScrollbackManager.CurrentLines;

            return identical && scrollbackIdentical;
        });
    }

    /// <summary>
    ///     Property: Scroll operations with zero lines should have no effect.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ZeroLineScrollingHasNoEffect()
    {
        return Prop.ForAll(TerminalDimensionsArb, TestCharArb,
            (dimensions, testChar) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Fill screen with test pattern
            FillScreenWithPattern(terminal, testChar, width, height);

            // Capture initial state
            var initialContent = CaptureScreenContent(terminal, width, height);
            var initialScrollbackLines = terminal.ScrollbackManager.CurrentLines;

            // Act: Scroll by zero lines
            terminal.Write("\x1b[0S"); // Scroll up 0 lines
            terminal.Write("\x1b[0T"); // Scroll down 0 lines

            // Assert: Nothing should have changed
            bool contentUnchanged = true;
            for (int row = 0; row < height && contentUnchanged; row++)
            {
                for (int col = 0; col < width && contentUnchanged; col++)
                {
                    var currentCell = terminal.ScreenBuffer.GetCell(row, col);
                    var expectedCell = initialContent[row, col];
                    if (currentCell.Character != expectedCell.Character)
                    {
                        contentUnchanged = false;
                    }
                }
            }

            bool scrollbackUnchanged = terminal.ScrollbackManager.CurrentLines == initialScrollbackLines;

            return contentUnchanged && scrollbackUnchanged;
        });
    }

    /// <summary>
    ///     Property: Scrolling in alternate screen mode should not affect scrollback.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property AlternateScreenScrollingDoesNotAffectScrollback()
    {
        return Prop.ForAll(TerminalDimensionsArb, ScrollLinesArb, TestCharArb,
            (dimensions, scrollLines, testChar) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Activate alternate screen mode
            terminal.State.IsAlternateScreenActive = true;

            // Fill screen with test pattern
            FillScreenWithPattern(terminal, testChar, width, height);

            // Act: Scroll up in alternate screen
            terminal.Write($"\x1b[{scrollLines}S");

            // Assert: Scrollback should remain empty
            bool scrollbackEmpty = terminal.ScrollbackManager.CurrentLines == 0;

            return scrollbackEmpty;
        });
    }

    /// <summary>
    ///     Property: Excessive scrolling should clear the screen appropriately.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ExcessiveScrollingClearsScreen()
    {
        return Prop.ForAll(TerminalDimensionsArb, TestCharArb,
            (dimensions, testChar) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Fill screen with test pattern
            FillScreenWithPattern(terminal, testChar, width, height);

            // Act: Scroll by more lines than screen height
            var excessiveLines = height + 2;
            terminal.Write($"\x1b[{excessiveLines}S");

            // Assert: Screen should be completely cleared
            bool screenCleared = true;
            for (int row = 0; row < height && screenCleared; row++)
            {
                for (int col = 0; col < width && screenCleared; col++)
                {
                    var cell = terminal.ScreenBuffer.GetCell(row, col);
                    if (cell.Character != ' ')
                    {
                        screenCleared = false;
                    }
                }
            }

            return screenCleared;
        });
    }

    /// <summary>
    ///     Helper method to fill the screen with a test pattern.
    /// </summary>
    private static void FillScreenWithPattern(TerminalEmulator terminal, char baseChar, int width, int height)
    {
        terminal.Write("\x1b[1;1H"); // Move to top-left

        for (int row = 0; row < height; row++)
        {
            // Create a pattern with the base character and row number
            var rowChar = (char)(baseChar + (row % 26));
            terminal.Write(new string(rowChar, width));

            if (row < height - 1) // Don't move down on last row
            {
                terminal.Write("\r\n");
            }
        }
    }

    /// <summary>
    ///     Helper method to capture current screen content.
    /// </summary>
    private static Cell[,] CaptureScreenContent(TerminalEmulator terminal, int width, int height)
    {
        var content = new Cell[height, width];

        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                content[row, col] = terminal.ScreenBuffer.GetCell(row, col);
            }
        }

        return content;
    }
}
