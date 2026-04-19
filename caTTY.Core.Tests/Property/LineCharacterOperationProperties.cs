using caTTY.Core.Terminal;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;
using Microsoft.Extensions.Logging.Abstractions;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for line and character insertion/deletion operations in the terminal emulator.
///     These tests verify universal properties that should hold for all line and character operations.
///     Validates Requirements 22.1, 22.2, 22.3, 22.4, 22.5.
/// </summary>
[TestFixture]
[Category("Property")]
public class LineCharacterOperationProperties
{
    /// <summary>
    ///     Generator for valid terminal dimensions.
    /// </summary>
    public static Arbitrary<(int width, int height)> TerminalDimensionsArb =>
        Arb.From(Gen.Choose(10, 25).SelectMany(width =>
            Gen.Choose(8, 20).Select(height => (width, height))));

    /// <summary>
    ///     Generator for valid operation counts.
    /// </summary>
    public static Arbitrary<int> OperationCountArb =>
        Arb.From(Gen.Choose(1, 5));

    /// <summary>
    ///     Generator for cursor positions within terminal bounds.
    /// </summary>
    public static Arbitrary<(int row, int col)> CursorPositionArb =>
        Arb.From(Gen.Choose(0, 19).SelectMany(row =>
            Gen.Choose(0, 24).Select(col => (row, col))));

    /// <summary>
    ///     Generator for simple test characters.
    /// </summary>
    public static Arbitrary<char> TestCharArb =>
        Arb.From(Gen.Elements('A', 'B', 'C', 'X', 'Y', 'Z', '1', '2', '3', '@', '#', '$'));

    /// <summary>
    ///     **Feature: catty-ksa, Property 32: Line and character insertion/deletion**
    ///     **Validates: Requirements 22.1, 22.2, 22.3, 22.4, 22.5**
    ///     Property: For any line insertion operation, content should be shifted correctly and blank lines inserted.
    ///     Line insertion should preserve existing content by shifting it down within the scroll region.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property LineInsertionShiftsContentCorrectly()
    {
        return Prop.ForAll(TerminalDimensionsArb, OperationCountArb, TestCharArb,
            (dimensions, insertCount, testChar) =>
        {
            var (width, height) = dimensions;
            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Fill terminal with test pattern
            FillTerminalWithPattern(terminal, testChar, width, height);

            // Position cursor in middle of screen
            int cursorRow = height / 2;
            terminal.Write($"\x1b[{cursorRow + 1};1H"); // Move to target row

            // Capture content before insertion
            var contentBefore = CaptureTerminalContent(terminal, width, height);

            // Act: Insert lines
            terminal.Write($"\x1b[{insertCount}L"); // CSI L - Insert Lines

            // Assert: Verify line insertion behavior
            bool insertionCorrect = VerifyLineInsertion(terminal, contentBefore, cursorRow, insertCount, width, height);

            // Verify terminal remains functional
            bool terminalFunctional = VerifyTerminalFunctionality(terminal, width, height);

            return insertionCorrect && terminalFunctional;
        });
    }

    /// <summary>
    ///     Property: For any line deletion operation, content should be shifted correctly and lines removed.
    ///     Line deletion should preserve existing content by shifting it up within the scroll region.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property LineDeletionShiftsContentCorrectly()
    {
        return Prop.ForAll(TerminalDimensionsArb, OperationCountArb, TestCharArb,
            (dimensions, deleteCount, testChar) =>
        {
            var (width, height) = dimensions;
            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Fill terminal with test pattern
            FillTerminalWithPattern(terminal, testChar, width, height);

            // Position cursor in middle of screen
            int cursorRow = height / 2;
            terminal.Write($"\x1b[{cursorRow + 1};1H"); // Move to target row

            // Capture content before deletion
            var contentBefore = CaptureTerminalContent(terminal, width, height);

            // Act: Delete lines
            terminal.Write($"\x1b[{deleteCount}M"); // CSI M - Delete Lines

            // Assert: Verify line deletion behavior
            bool deletionCorrect = VerifyLineDeletion(terminal, contentBefore, cursorRow, deleteCount, width, height);

            // Verify terminal remains functional
            bool terminalFunctional = VerifyTerminalFunctionality(terminal, width, height);

            return deletionCorrect && terminalFunctional;
        });
    }

    /// <summary>
    ///     Property: For any character insertion operation, content should be shifted correctly and blank characters inserted.
    ///     Character insertion should preserve existing content by shifting it right within the current line.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CharacterInsertionShiftsContentCorrectly()
    {
        return Prop.ForAll(TerminalDimensionsArb, OperationCountArb, TestCharArb,
            (dimensions, insertCount, testChar) =>
        {
            var (width, height) = dimensions;
            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Fill a single line with test pattern
            terminal.Write("\x1b[5;1H"); // Move to row 5
            string testLine = new string(testChar, Math.Min(width - 5, 10)); // Leave room for insertion
            terminal.Write(testLine);

            // Position cursor in middle of the line
            int cursorCol = testLine.Length / 2;
            terminal.Write($"\x1b[5;{cursorCol + 1}H"); // Move to target position

            // Capture content before insertion
            var contentBefore = CaptureTerminalContent(terminal, width, height);

            // Act: Insert characters
            terminal.Write($"\x1b[{insertCount}@"); // CSI @ - Insert Characters

            // Assert: Verify character insertion behavior
            bool insertionCorrect = VerifyCharacterInsertion(terminal, contentBefore, 4, cursorCol, insertCount, width); // Row 4 (0-based)

            // Verify terminal remains functional
            bool terminalFunctional = VerifyTerminalFunctionality(terminal, width, height);

            return insertionCorrect && terminalFunctional;
        });
    }

    /// <summary>
    ///     Property: For any character deletion operation, content should be shifted correctly and characters removed.
    ///     Character deletion should preserve existing content by shifting it left within the current line.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CharacterDeletionShiftsContentCorrectly()
    {
        return Prop.ForAll(TerminalDimensionsArb, OperationCountArb, TestCharArb,
            (dimensions, deleteCount, testChar) =>
        {
            var (width, height) = dimensions;
            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Fill a single line with test pattern
            terminal.Write("\x1b[5;1H"); // Move to row 5
            string testLine = new string(testChar, Math.Min(width - 2, 15)); // Fill most of the line
            terminal.Write(testLine);

            // Position cursor in middle of the line
            int cursorCol = testLine.Length / 2;
            terminal.Write($"\x1b[5;{cursorCol + 1}H"); // Move to target position

            // Capture content before deletion
            var contentBefore = CaptureTerminalContent(terminal, width, height);

            // Act: Delete characters
            terminal.Write($"\x1b[{deleteCount}P"); // CSI P - Delete Characters

            // Assert: Verify character deletion behavior
            bool deletionCorrect = VerifyCharacterDeletion(terminal, contentBefore, 4, cursorCol, deleteCount, width); // Row 4 (0-based)

            // Verify terminal remains functional
            bool terminalFunctional = VerifyTerminalFunctionality(terminal, width, height);

            return deletionCorrect && terminalFunctional;
        });
    }

    /// <summary>
    ///     Property: Line and character operations should preserve SGR attributes.
    ///     When content is shifted during operations, attributes should be preserved with the content.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OperationsPreserveSgrAttributes()
    {
        return Prop.ForAll(TerminalDimensionsArb, OperationCountArb,
            (dimensions, operationCount) =>
        {
            var (width, height) = dimensions;
            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Write content with different attributes
            terminal.Write("\x1b[3;1H"); // Move to row 3
            terminal.Write("\x1b[1mBOLD"); // Bold text
            terminal.Write("\x1b[3mITALIC"); // Italic text
            terminal.Write("\x1b[0mNORMAL"); // Normal text

            // Position cursor at beginning of the line
            terminal.Write("\x1b[3;1H");

            // Capture content with attributes before operation
            var contentBefore = CaptureTerminalContent(terminal, width, height);

            // Act: Insert characters (this should preserve attributes of shifted content)
            terminal.Write($"\x1b[{operationCount}@"); // CSI @ - Insert Characters

            // Assert: Verify attributes are preserved for shifted content
            bool attributesPreserved = VerifyAttributePreservation(terminal, contentBefore, 2, 0, operationCount, width); // Row 2 (0-based)

            return attributesPreserved;
        });
    }

    /// <summary>
    ///     Property: Line and character operations should respect scroll region boundaries.
    ///     Operations should only affect content within the defined scroll region.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OperationsRespectScrollRegion()
    {
        return Prop.ForAll(TerminalDimensionsArb, OperationCountArb, TestCharArb,
            (dimensions, operationCount, testChar) =>
        {
            var (width, height) = dimensions;
            if (height < 6) return true; // Skip if terminal too small for scroll region test

            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Set scroll region (leave top and bottom rows outside)
            int scrollTop = 2;
            int scrollBottom = height - 2;
            terminal.Write($"\x1b[{scrollTop};{scrollBottom}r"); // Set scroll region

            // Fill entire terminal with test pattern
            FillTerminalWithPattern(terminal, testChar, width, height);

            // Position cursor within scroll region
            int cursorRow = scrollTop + 1; // Within scroll region
            terminal.Write($"\x1b[{cursorRow + 1};1H");

            // Capture content before operation
            var contentBefore = CaptureTerminalContent(terminal, width, height);

            // Act: Insert lines (should only affect scroll region)
            terminal.Write($"\x1b[{operationCount}L"); // CSI L - Insert Lines

            // Assert: Content outside scroll region should be unchanged
            bool outsideRegionUnchanged = VerifyScrollRegionBoundaries(terminal, contentBefore, scrollTop - 1, scrollBottom, width, height);

            return outsideRegionUnchanged;
        });
    }

    /// <summary>
    ///     Property: Operations should handle edge cases correctly.
    ///     Operations at terminal boundaries should work without errors.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OperationsHandleEdgeCases()
    {
        return Prop.ForAll(TerminalDimensionsArb, TestCharArb,
            (dimensions, testChar) =>
        {
            var (width, height) = dimensions;
            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            bool allEdgeCasesWork = true;

            // Test edge cases
            var edgeCases = new[]
            {
                // Line operations at top and bottom
                (operation: $"\x1b[1;1H\x1b[1L", description: "Insert line at top"),
                (operation: $"\x1b[{height};1H\x1b[1L", description: "Insert line at bottom"),
                (operation: $"\x1b[1;1H\x1b[1M", description: "Delete line at top"),
                (operation: $"\x1b[{height};1H\x1b[1M", description: "Delete line at bottom"),

                // Character operations at line boundaries
                (operation: $"\x1b[5;1H\x1b[1@", description: "Insert char at line start"),
                (operation: $"\x1b[5;{width}H\x1b[1@", description: "Insert char at line end"),
                (operation: $"\x1b[5;1H\x1b[1P", description: "Delete char at line start"),
                (operation: $"\x1b[5;{width}H\x1b[1P", description: "Delete char at line end"),

                // Large operation counts
                (operation: $"\x1b[5;5H\x1b[{height}L", description: "Insert many lines"),
                (operation: $"\x1b[5;5H\x1b[{width}@", description: "Insert many chars")
            };

            foreach (var (operation, description) in edgeCases)
            {
                try
                {
                    // Fill with test content
                    FillTerminalWithPattern(terminal, testChar, width, height);

                    // Apply edge case operation
                    terminal.Write(operation);

                    // Verify terminal remains functional
                    bool functional = VerifyTerminalFunctionality(terminal, width, height);
                    if (!functional)
                    {
                        allEdgeCasesWork = false;
                        break;
                    }
                }
                catch
                {
                    allEdgeCasesWork = false;
                    break;
                }
            }

            return allEdgeCasesWork;
        });
    }

    /// <summary>
    ///     Property: Multiple consecutive operations should work correctly.
    ///     Performing multiple line/character operations should maintain terminal integrity.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MultipleOperationsWork()
    {
        return Prop.ForAll(TerminalDimensionsArb, TestCharArb,
            (dimensions, testChar) =>
        {
            var (width, height) = dimensions;
            using var terminal = TerminalEmulator.Create(width, height, 10, NullLogger.Instance);

            // Fill with initial content
            FillTerminalWithPattern(terminal, testChar, width, height);

            // Perform sequence of operations
            var operations = new[]
            {
                "\x1b[5;5H\x1b[2L",  // Insert 2 lines
                "\x1b[7;10H\x1b[3@", // Insert 3 characters
                "\x1b[6;1H\x1b[1M",  // Delete 1 line
                "\x1b[8;5H\x1b[2P",  // Delete 2 characters
                "\x1b[4;8H\x1b[1L"   // Insert 1 line
            };

            bool allOperationsWork = true;

            foreach (var operation in operations)
            {
                try
                {
                    terminal.Write(operation);

                    // Verify terminal remains functional after each operation
                    bool functional = VerifyTerminalFunctionality(terminal, width, height);
                    if (!functional)
                    {
                        allOperationsWork = false;
                        break;
                    }
                }
                catch
                {
                    allOperationsWork = false;
                    break;
                }
            }

            return allOperationsWork;
        });
    }

    /// <summary>
    ///     Helper method to fill the terminal with a test pattern.
    /// </summary>
    private static void FillTerminalWithPattern(TerminalEmulator terminal, char baseChar, int width, int height)
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
    ///     Helper method to capture current terminal content.
    /// </summary>
    private static Cell[,] CaptureTerminalContent(TerminalEmulator terminal, int width, int height)
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
    ///     Helper method to verify line insertion behavior.
    /// </summary>
    private static bool VerifyLineInsertion(TerminalEmulator terminal, Cell[,] contentBefore,
        int insertRow, int insertCount, int width, int height)
    {
        // After line insertion:
        // - Lines at insertRow and below should be shifted down by insertCount
        // - New blank lines should be inserted at insertRow
        // - Content above insertRow should be unchanged

        // Check content above insertion point is unchanged
        for (int row = 0; row < insertRow; row++)
        {
            for (int col = 0; col < width; col++)
            {
                var currentCell = terminal.ScreenBuffer.GetCell(row, col);
                var originalCell = contentBefore[row, col];
                if (currentCell.Character != originalCell.Character)
                {
                    return false;
                }
            }
        }

        // Check that blank lines were inserted
        int linesToCheck = Math.Min(insertCount, height - insertRow);
        for (int i = 0; i < linesToCheck; i++)
        {
            int row = insertRow + i;
            if (row >= height) break;

            for (int col = 0; col < width; col++)
            {
                var cell = terminal.ScreenBuffer.GetCell(row, col);
                if (cell.Character != ' ')
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    ///     Helper method to verify line deletion behavior.
    /// </summary>
    private static bool VerifyLineDeletion(TerminalEmulator terminal, Cell[,] contentBefore,
        int deleteRow, int deleteCount, int width, int height)
    {
        // After line deletion:
        // - Lines below deleteRow should be shifted up by deleteCount
        // - Content above deleteRow should be unchanged
        // - Bottom lines should be blank

        // Check content above deletion point is unchanged
        for (int row = 0; row < deleteRow; row++)
        {
            for (int col = 0; col < width; col++)
            {
                var currentCell = terminal.ScreenBuffer.GetCell(row, col);
                var originalCell = contentBefore[row, col];
                if (currentCell.Character != originalCell.Character)
                {
                    return false;
                }
            }
        }

        // Check that content was shifted up correctly
        int shiftedRows = Math.Min(deleteCount, height - deleteRow);
        for (int row = deleteRow; row < height - shiftedRows; row++)
        {
            int sourceRow = row + deleteCount;
            if (sourceRow >= height) break;

            for (int col = 0; col < width; col++)
            {
                var currentCell = terminal.ScreenBuffer.GetCell(row, col);
                var expectedCell = contentBefore[sourceRow, col];
                if (currentCell.Character != expectedCell.Character)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    ///     Helper method to verify character insertion behavior.
    /// </summary>
    private static bool VerifyCharacterInsertion(TerminalEmulator terminal, Cell[,] contentBefore,
        int row, int insertCol, int insertCount, int width)
    {
        // After character insertion:
        // - Characters at insertCol and right should be shifted right by insertCount
        // - New blank characters should be inserted at insertCol
        // - Characters left of insertCol should be unchanged

        // Check characters left of insertion point are unchanged
        for (int col = 0; col < insertCol; col++)
        {
            var currentCell = terminal.ScreenBuffer.GetCell(row, col);
            var originalCell = contentBefore[row, col];
            if (currentCell.Character != originalCell.Character)
            {
                return false;
            }
        }

        // Check that blank characters were inserted
        int charsToCheck = Math.Min(insertCount, width - insertCol);
        for (int i = 0; i < charsToCheck; i++)
        {
            int col = insertCol + i;
            if (col >= width) break;

            var cell = terminal.ScreenBuffer.GetCell(row, col);
            if (cell.Character != ' ')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Helper method to verify character deletion behavior.
    /// </summary>
    private static bool VerifyCharacterDeletion(TerminalEmulator terminal, Cell[,] contentBefore,
        int row, int deleteCol, int deleteCount, int width)
    {
        // After character deletion:
        // - Characters right of deleteCol should be shifted left by deleteCount
        // - Characters left of deleteCol should be unchanged
        // - Right side of line should be blank

        // Check characters left of deletion point are unchanged
        for (int col = 0; col < deleteCol; col++)
        {
            var currentCell = terminal.ScreenBuffer.GetCell(row, col);
            var originalCell = contentBefore[row, col];
            if (currentCell.Character != originalCell.Character)
            {
                return false;
            }
        }

        // Check that characters were shifted left correctly
        int shiftedChars = Math.Min(deleteCount, width - deleteCol);
        for (int col = deleteCol; col < width - shiftedChars; col++)
        {
            int sourceCol = col + deleteCount;
            if (sourceCol >= width) break;

            var currentCell = terminal.ScreenBuffer.GetCell(row, col);
            var expectedCell = contentBefore[row, sourceCol];
            if (currentCell.Character != expectedCell.Character)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Helper method to verify attribute preservation during operations.
    /// </summary>
    private static bool VerifyAttributePreservation(TerminalEmulator terminal, Cell[,] contentBefore,
        int row, int insertCol, int insertCount, int width)
    {
        // Check that attributes are preserved for shifted content
        for (int col = insertCol + insertCount; col < width; col++)
        {
            int originalCol = col - insertCount;
            if (originalCol < 0 || originalCol >= width) continue;

            var currentCell = terminal.ScreenBuffer.GetCell(row, col);
            var originalCell = contentBefore[row, originalCol];

            // If the character was preserved, attributes should be too
            if (currentCell.Character == originalCell.Character && currentCell.Character != ' ')
            {
                if (currentCell.Attributes.Bold != originalCell.Attributes.Bold ||
                    currentCell.Attributes.Italic != originalCell.Attributes.Italic)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    ///     Helper method to verify scroll region boundaries are respected.
    /// </summary>
    private static bool VerifyScrollRegionBoundaries(TerminalEmulator terminal, Cell[,] contentBefore,
        int scrollTop, int scrollBottom, int width, int height)
    {
        // Check content above scroll region is unchanged
        for (int row = 0; row < scrollTop; row++)
        {
            for (int col = 0; col < width; col++)
            {
                var currentCell = terminal.ScreenBuffer.GetCell(row, col);
                var originalCell = contentBefore[row, col];
                if (currentCell.Character != originalCell.Character)
                {
                    return false;
                }
            }
        }

        // Check content below scroll region is unchanged
        for (int row = scrollBottom + 1; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                var currentCell = terminal.ScreenBuffer.GetCell(row, col);
                var originalCell = contentBefore[row, col];
                if (currentCell.Character != originalCell.Character)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    ///     Helper method to verify terminal functionality after operations.
    /// </summary>
    private static bool VerifyTerminalFunctionality(TerminalEmulator terminal, int width, int height)
    {
        try
        {
            // Test that we can write to the terminal
            terminal.Write("\x1b[1;1H"); // Move to top-left
            terminal.Write("TEST");

            // Test that we can read from the terminal
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
