using caTTY.Core.Terminal;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;
using Microsoft.Extensions.Logging.Abstractions;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for screen buffer operations in the terminal emulator.
///     These tests verify universal properties that should hold for all screen buffer operations.
///     Validates Requirements 7.2.
/// </summary>
[TestFixture]
[Category("Property")]
public class ScreenBufferProperties
{
    /// <summary>
    ///     Generator for valid terminal dimensions.
    /// </summary>
    public static Arbitrary<(int width, int height)> TerminalDimensionsArb =>
        Arb.From(Gen.Choose(5, 20).SelectMany(width =>
            Gen.Choose(3, 15).Select(height => (width, height))));

    /// <summary>
    ///     Generator for new dimensions for resize operations.
    /// </summary>
    public static Arbitrary<(int newWidth, int newHeight)> NewDimensionsArb =>
        Arb.From(Gen.Choose(3, 25).SelectMany(newWidth =>
            Gen.Choose(2, 18).Select(newHeight => (newWidth, newHeight))));

    /// <summary>
    ///     Generator for simple test characters.
    /// </summary>
    public static Arbitrary<char> TestCharArb =>
        Arb.From(Gen.Elements('A', 'B', 'C', 'X', 'Y', 'Z', '1', '2', '3', '@', '#', '$'));

    /// <summary>
    ///     **Feature: catty-ksa, Property 8: Screen buffer resize preservation**
    ///     **Validates: Requirements 7.2**
    ///     Property: For any screen buffer resize operation, content should be preserved where possible.
    ///     When dimensions change, the buffer should resize and preserve content according to the simple policy:
    ///     - Height change: preserve top-to-bottom rows where possible
    ///     - Width change: truncate/pad each row; do not attempt complex reflow
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScreenBufferResizePreservesContent()
    {
        return Prop.ForAll(TerminalDimensionsArb, NewDimensionsArb, TestCharArb,
            (originalDimensions, newDimensions, testChar) =>
        {
            var (originalWidth, originalHeight) = originalDimensions;
            var (newWidth, newHeight) = newDimensions;

            using var terminal = TerminalEmulator.Create(originalWidth, originalHeight, 10, NullLogger.Instance);

            // Fill the original buffer with a test pattern
            FillBufferWithPattern(terminal, testChar, originalWidth, originalHeight);

            // Capture the original content for verification
            var originalContent = CaptureBufferContent(terminal, originalWidth, originalHeight);

            // Act: Resize the terminal
            terminal.Resize(newWidth, newHeight);

            // Assert: Verify dimensions changed correctly
            bool dimensionsCorrect = terminal.Width == newWidth && terminal.Height == newHeight;

            // Verify content preservation according to the simple policy
            bool contentPreserved = VerifyContentPreservation(
                terminal, originalContent, originalWidth, originalHeight, newWidth, newHeight);

            // Verify buffer remains functional after resize
            bool bufferFunctional = VerifyBufferFunctionality(terminal, newWidth, newHeight);

            return dimensionsCorrect && contentPreserved && bufferFunctional;
        });
    }

    /// <summary>
    ///     Property: Screen buffer resize should handle edge cases correctly.
    ///     Resizing to same dimensions should have no effect on content.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ResizeToSameDimensionsHasNoEffect()
    {
        return Prop.ForAll(TerminalDimensionsArb, TestCharArb,
            (dimensions, testChar) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Fill buffer with test pattern
            FillBufferWithPattern(terminal, testChar, width, height);

            // Capture original content
            var originalContent = CaptureBufferContent(terminal, width, height);

            // Act: Resize to same dimensions
            terminal.Resize(width, height);

            // Assert: Content should be identical
            bool contentUnchanged = true;
            for (int row = 0; row < height && contentUnchanged; row++)
            {
                for (int col = 0; col < width && contentUnchanged; col++)
                {
                    var currentCell = terminal.ScreenBuffer.GetCell(row, col);
                    var originalCell = originalContent[row, col];
                    if (currentCell.Character != originalCell.Character)
                    {
                        contentUnchanged = false;
                    }
                }
            }

            // Verify dimensions remain the same
            bool dimensionsUnchanged = terminal.Width == width && terminal.Height == height;

            return contentUnchanged && dimensionsUnchanged;
        });
    }

    /// <summary>
    ///     Property: Screen buffer resize should preserve cursor position appropriately.
    ///     Cursor should remain valid after resize, clamped to new dimensions if necessary.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ResizePreservesCursorPosition()
    {
        return Prop.ForAll(TerminalDimensionsArb, NewDimensionsArb,
            (originalDimensions, newDimensions) =>
        {
            var (originalWidth, originalHeight) = originalDimensions;
            var (newWidth, newHeight) = newDimensions;

            using var terminal = TerminalEmulator.Create(originalWidth, originalHeight, 10, NullLogger.Instance);

            // Position cursor at a specific location
            var targetRow = Math.Min(originalHeight / 2, originalHeight - 1);
            var targetCol = Math.Min(originalWidth / 2, originalWidth - 1);
            terminal.Write($"\x1b[{targetRow + 1};{targetCol + 1}H"); // CSI positioning is 1-based

            var originalCursorRow = terminal.Cursor.Row;
            var originalCursorCol = terminal.Cursor.Col;

            // Act: Resize the terminal
            terminal.Resize(newWidth, newHeight);

            // Assert: Cursor should be valid within new dimensions
            bool cursorValid = terminal.Cursor.Row >= 0 && terminal.Cursor.Row < newHeight &&
                               terminal.Cursor.Col >= 0 && terminal.Cursor.Col < newWidth;

            // Verify cursor position follows the actual resize logic from TerminalEmulator.Resize()
            bool cursorPositionCorrect = true;

            // Expected column position: clamped to new width
            int expectedCol = Math.Min(originalCursorCol, newWidth - 1);

            // Expected row position: follows the corrected height adjustment logic
            // Only adjust cursor if rows were actually pushed to scrollback (cursor >= new height)
            int expectedRow;
            if (newHeight < originalHeight && originalCursorRow >= newHeight)
            {
                // Height decreased AND cursor was in the area that got pushed to scrollback - adjust position
                int rowsLost = originalHeight - newHeight;
                expectedRow = Math.Max(0, originalCursorRow - rowsLost);
            }
            else
            {
                // No rows pushed to scrollback OR height increased - just clamp to new bounds
                expectedRow = Math.Min(originalCursorRow, newHeight - 1);
            }

            cursorPositionCorrect = terminal.Cursor.Row == expectedRow &&
                                  terminal.Cursor.Col == expectedCol;

            return cursorValid && cursorPositionCorrect;
        });
    }

    /// <summary>
    ///     Property: Screen buffer resize should handle extreme dimension changes correctly.
    ///     Resizing to very small or very large dimensions should work without errors.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ResizeHandlesExtremeDimensions()
    {
        return Prop.ForAll(TestCharArb, testChar =>
        {
            // Test various extreme resize scenarios
            var scenarios = new[]
            {
                (original: (10, 10), newDims: (1, 1)),     // Shrink to minimum
                (original: (5, 5), newDims: (50, 50)),     // Expand significantly
                (original: (20, 5), newDims: (5, 20)),     // Swap width/height
                (original: (10, 10), newDims: (10, 1)),    // Collapse height
                (original: (10, 10), newDims: (1, 10))     // Collapse width
            };

            bool allScenariosWork = true;

            foreach (var scenario in scenarios)
            {
                var (originalWidth, originalHeight) = scenario.original;
                var (newWidth, newHeight) = scenario.newDims;

                try
                {
                    using var terminal = TerminalEmulator.Create(originalWidth, originalHeight, 10, NullLogger.Instance);

                    // Fill with some content
                    FillBufferWithPattern(terminal, testChar, originalWidth, originalHeight);

                    // Resize
                    terminal.Resize(newWidth, newHeight);

                    // Verify basic functionality
                    bool dimensionsCorrect = terminal.Width == newWidth && terminal.Height == newHeight;
                    bool canWriteAfterResize = VerifyBufferFunctionality(terminal, newWidth, newHeight);

                    if (!dimensionsCorrect || !canWriteAfterResize)
                    {
                        allScenariosWork = false;
                        break;
                    }
                }
                catch
                {
                    allScenariosWork = false;
                    break;
                }
            }

            return allScenariosWork;
        });
    }

    /// <summary>
    ///     Property: Screen buffer resize should be deterministic.
    ///     Resizing with the same parameters should always produce the same result.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ResizeIsDeterministic()
    {
        return Prop.ForAll(TerminalDimensionsArb, NewDimensionsArb, TestCharArb,
            (originalDimensions, newDimensions, testChar) =>
        {
            var (originalWidth, originalHeight) = originalDimensions;
            var (newWidth, newHeight) = newDimensions;

            // Create two identical terminals
            using var terminal1 = TerminalEmulator.Create(originalWidth, originalHeight, 10, NullLogger.Instance);
            using var terminal2 = TerminalEmulator.Create(originalWidth, originalHeight, 10, NullLogger.Instance);

            // Fill both with identical content
            FillBufferWithPattern(terminal1, testChar, originalWidth, originalHeight);
            FillBufferWithPattern(terminal2, testChar, originalWidth, originalHeight);

            // Resize both identically
            terminal1.Resize(newWidth, newHeight);
            terminal2.Resize(newWidth, newHeight);

            // Assert: Both should have identical content
            bool contentIdentical = true;
            for (int row = 0; row < newHeight && contentIdentical; row++)
            {
                for (int col = 0; col < newWidth && contentIdentical; col++)
                {
                    var cell1 = terminal1.ScreenBuffer.GetCell(row, col);
                    var cell2 = terminal2.ScreenBuffer.GetCell(row, col);
                    if (cell1.Character != cell2.Character)
                    {
                        contentIdentical = false;
                    }
                }
            }

            // Verify dimensions are identical
            bool dimensionsIdentical = terminal1.Width == terminal2.Width &&
                                     terminal1.Height == terminal2.Height;

            return contentIdentical && dimensionsIdentical;
        });
    }

    /// <summary>
    ///     Property: Screen buffer resize should preserve SGR attributes along with characters.
    ///     When content is preserved during resize, attributes should also be preserved.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ResizePreservesAttributes()
    {
        return Prop.ForAll(TerminalDimensionsArb, NewDimensionsArb,
            (originalDimensions, newDimensions) =>
        {
            var (originalWidth, originalHeight) = originalDimensions;
            var (newWidth, newHeight) = newDimensions;

            using var terminal = TerminalEmulator.Create(originalWidth, originalHeight, 10, NullLogger.Instance);

            // Set bold attribute and write some content
            terminal.Write("\x1b[1mBOLD");

            // Set italic attribute and write more content
            terminal.Write("\x1b[3mITALIC");

            // Reset and write normal content
            terminal.Write("\x1b[0mNORMAL");

            // Capture original content with attributes
            var originalContent = CaptureBufferContent(terminal, originalWidth, originalHeight);

            // Act: Resize
            terminal.Resize(newWidth, newHeight);

            // Assert: Verify preserved content maintains attributes
            bool attributesPreserved = true;
            int rowsToCheck = Math.Min(originalHeight, newHeight);
            int colsToCheck = Math.Min(originalWidth, newWidth);

            for (int row = 0; row < rowsToCheck && attributesPreserved; row++)
            {
                for (int col = 0; col < colsToCheck && attributesPreserved; col++)
                {
                    var currentCell = terminal.ScreenBuffer.GetCell(row, col);
                    var originalCell = originalContent[row, col];

                    // If the character is preserved, attributes should be too
                    if (currentCell.Character == originalCell.Character &&
                        currentCell.Character != ' ')
                    {
                        if (currentCell.Attributes.Bold != originalCell.Attributes.Bold ||
                            currentCell.Attributes.Italic != originalCell.Attributes.Italic)
                        {
                            attributesPreserved = false;
                        }
                    }
                }
            }

            return attributesPreserved;
        });
    }

    /// <summary>
    ///     Property: Multiple consecutive resizes should work correctly.
    ///     Performing multiple resize operations should maintain buffer integrity.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MultipleResizesWork()
    {
        return Prop.ForAll(TestCharArb, testChar =>
        {
            using var terminal = TerminalEmulator.Create(10, 10, 10, NullLogger.Instance);

            // Fill with initial content
            FillBufferWithPattern(terminal, testChar, 10, 10);

            // Perform multiple resizes
            var resizeSequence = new[]
            {
                (15, 15), // Expand
                (8, 8),   // Shrink
                (12, 6),  // Change aspect ratio
                (6, 12),  // Swap dimensions
                (10, 10)  // Return to original
            };

            bool allResizesWork = true;

            foreach (var (width, height) in resizeSequence)
            {
                try
                {
                    terminal.Resize(width, height);

                    // Verify basic functionality after each resize
                    bool dimensionsCorrect = terminal.Width == width && terminal.Height == height;
                    bool bufferFunctional = VerifyBufferFunctionality(terminal, width, height);

                    if (!dimensionsCorrect || !bufferFunctional)
                    {
                        allResizesWork = false;
                        break;
                    }
                }
                catch
                {
                    allResizesWork = false;
                    break;
                }
            }

            return allResizesWork;
        });
    }

    /// <summary>
    ///     Helper method to fill the buffer with a test pattern.
    /// </summary>
    private static void FillBufferWithPattern(TerminalEmulator terminal, char baseChar, int width, int height)
    {
        terminal.Write("\x1b[1;1H"); // Move to top-left

        for (int row = 0; row < height; row++)
        {
            // Create a pattern with the base character and row number
            var rowChar = (char)(baseChar + (row % 10));
            for (int col = 0; col < width; col++)
            {
                var colChar = (char)(rowChar + (col % 10));
                terminal.Write(colChar.ToString());
            }

            if (row < height - 1) // Don't move down on last row to avoid scrolling
            {
                terminal.Write("\r\n");
            }
        }
    }

    /// <summary>
    ///     Helper method to capture current buffer content.
    /// </summary>
    private static Cell[,] CaptureBufferContent(TerminalEmulator terminal, int width, int height)
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

    /// <summary>
    ///     Helper method to verify content preservation according to the simple policy.
    /// </summary>
    private static bool VerifyContentPreservation(TerminalEmulator terminal, Cell[,] originalContent,
        int originalWidth, int originalHeight, int newWidth, int newHeight)
    {
        // According to the simple policy:
        // - Height change: preserve top-to-bottom rows where possible
        // - Width change: truncate/pad each row; do not attempt complex reflow

        int rowsToCheck = Math.Min(originalHeight, newHeight);
        int colsToCheck = Math.Min(originalWidth, newWidth);

        for (int row = 0; row < rowsToCheck; row++)
        {
            for (int col = 0; col < colsToCheck; col++)
            {
                var currentCell = terminal.ScreenBuffer.GetCell(row, col);
                var originalCell = originalContent[row, col];

                // Content should be preserved in the overlapping region
                if (currentCell.Character != originalCell.Character)
                {
                    return false;
                }
            }

            // If new width is larger, additional columns should be empty
            if (newWidth > originalWidth)
            {
                for (int col = originalWidth; col < newWidth; col++)
                {
                    var cell = terminal.ScreenBuffer.GetCell(row, col);
                    if (cell.Character != ' ')
                    {
                        return false;
                    }
                }
            }
        }

        // If new height is larger, additional rows should be empty
        if (newHeight > originalHeight)
        {
            for (int row = originalHeight; row < newHeight; row++)
            {
                for (int col = 0; col < newWidth; col++)
                {
                    var cell = terminal.ScreenBuffer.GetCell(row, col);
                    if (cell.Character != ' ')
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    ///     Helper method to verify buffer functionality after resize.
    /// </summary>
    private static bool VerifyBufferFunctionality(TerminalEmulator terminal, int width, int height)
    {
        try
        {
            // Test that we can write to the buffer
            terminal.Write("\x1b[1;1H"); // Move to top-left
            terminal.Write("TEST");

            // Test that we can read from the buffer
            var cell = terminal.ScreenBuffer.GetCell(0, 0);

            // Test cursor is within bounds
            bool cursorValid = terminal.Cursor.Row >= 0 && terminal.Cursor.Row < height &&
                               terminal.Cursor.Col >= 0 && terminal.Cursor.Col < width;

            return cursorValid;
        }
        catch
        {
            return false;
        }
    }
}
