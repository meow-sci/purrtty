using caTTY.Core.Terminal;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for screen clearing operations in the terminal emulator.
///     These tests verify universal properties that should hold for all screen clearing operations.
///     Validates Requirements 11.6, 11.7.
/// </summary>
[TestFixture]
[Category("Property")]
public class ScreenClearingProperties
{
    /// <summary>
    ///     Generator for valid erase display modes (0, 1, 2, 3).
    /// </summary>
    public static Arbitrary<int> EraseDisplayModeArb =>
        Arb.From(Gen.Elements(0, 1, 2, 3));

    /// <summary>
    ///     Generator for valid erase line modes (0, 1, 2).
    /// </summary>
    public static Arbitrary<int> EraseLineModeArb =>
        Arb.From(Gen.Elements(0, 1, 2));

    /// <summary>
    ///     **Feature: catty-ksa, Property 19: Screen clearing operations**
    ///     **Validates: Requirements 11.6, 11.7**
    ///     Property: For any erase display sequence, the correct portions of the screen should be cleared according to
    ///     parameters.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property EraseDisplayClearsCorrectPortions()
    {
        return Prop.ForAll(EraseDisplayModeArb, mode =>
        {
            // Use fixed dimensions for simplicity
            const int width = 20;
            const int height = 10;
            const char fillChar = 'X';

            // Test with cursor at middle position
            const int cursorRow = 4;
            const int cursorCol = 8;

            // Arrange: Create terminal and fill with test character
            using var terminal = TerminalEmulator.Create(width, height);
            FillScreenWithCharacter(terminal, fillChar, width, height);

            // Position cursor at test position
            terminal.Write($"\x1b[{cursorRow + 1};{cursorCol + 1}H");

            // Act: Apply erase display sequence
            terminal.Write($"\x1b[{mode}J");

            // Assert: Verify correct portions are cleared
            bool correctlyCleared = true;

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    Cell cell = terminal.ScreenBuffer.GetCell(row, col);
                    bool shouldBeCleared = ShouldCellBeClearedByDisplayErase(
                        row, col, cursorRow, cursorCol, mode, width, height);

                    if (shouldBeCleared && cell.Character != ' ')
                    {
                        correctlyCleared = false;
                        break;
                    }

                    if (!shouldBeCleared && cell.Character != fillChar)
                    {
                        correctlyCleared = false;
                        break;
                    }
                }

                if (!correctlyCleared)
                {
                    break;
                }
            }

            return correctlyCleared;
        });
    }

    /// <summary>
    ///     Property: For any erase line sequence, the correct portions of the current line should be cleared according to
    ///     parameters.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property EraseLineClearsCorrectPortions()
    {
        return Prop.ForAll(EraseLineModeArb, mode =>
        {
            // Use fixed dimensions for simplicity
            const int width = 20;
            const int height = 10;
            const char fillChar = 'Y';

            // Test with cursor at middle position
            const int cursorRow = 4;
            const int cursorCol = 8;

            // Arrange: Create terminal and fill with test character
            using var terminal = TerminalEmulator.Create(width, height);
            FillScreenWithCharacter(terminal, fillChar, width, height);

            // Position cursor at test position
            terminal.Write($"\x1b[{cursorRow + 1};{cursorCol + 1}H");

            // Act: Apply erase line sequence
            terminal.Write($"\x1b[{mode}K");

            // Assert: Verify correct portions are cleared
            bool correctlyCleared = true;

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    Cell cell = terminal.ScreenBuffer.GetCell(row, col);
                    bool shouldBeCleared = ShouldCellBeClearedByLineErase(
                        row, col, cursorRow, cursorCol, mode);

                    if (shouldBeCleared && cell.Character != ' ')
                    {
                        correctlyCleared = false;
                        break;
                    }

                    if (!shouldBeCleared && cell.Character != fillChar)
                    {
                        correctlyCleared = false;
                        break;
                    }
                }

                if (!correctlyCleared)
                {
                    break;
                }
            }

            return correctlyCleared;
        });
    }

    /// <summary>
    ///     Property: Screen clearing operations should preserve terminal state integrity.
    ///     After any clearing operation, the terminal should remain in a valid state.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScreenClearingPreservesStateIntegrity()
    {
        return Prop.ForAll(EraseDisplayModeArb, mode =>
        {
            // Use fixed dimensions for simplicity
            const int width = 20;
            const int height = 10;
            const int cursorRow = 4;
            const int cursorCol = 8;

            // Arrange: Create terminal
            using var terminal = TerminalEmulator.Create(width, height);

            // Position cursor at test position
            terminal.Write($"\x1b[{cursorRow + 1};{cursorCol + 1}H");

            // Act: Apply erase display sequence
            terminal.Write($"\x1b[{mode}J");

            // Assert: Terminal should remain functional
            ICursor cursor = terminal.Cursor;

            // Verify cursor is still valid
            bool cursorValid = cursor.Row >= 0 && cursor.Row < height &&
                               cursor.Col >= 0 && cursor.Col < width;

            // Verify we can still write content
            terminal.Write("TEST");
            ICursor finalCursor = terminal.Cursor;
            bool canWrite = finalCursor.Row >= 0 && finalCursor.Row < height &&
                            finalCursor.Col >= 0 && finalCursor.Col < width;

            // Verify wrap pending state is cleared
            bool wrapPendingCleared = !terminal.State.WrapPending;

            return cursorValid && canWrite && wrapPendingCleared;
        });
    }

    /// <summary>
    ///     Property: Screen clearing operations should be deterministic.
    ///     Applying the same clearing operation should always produce the same result.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScreenClearingIsDeterministic()
    {
        return Prop.ForAll(EraseDisplayModeArb, mode =>
        {
            // Use fixed dimensions for simplicity
            const int width = 20;
            const int height = 10;
            const char fillChar = 'Z';
            const int cursorRow = 4;
            const int cursorCol = 8;

            // Arrange: Create two identical terminals
            using var terminal1 = TerminalEmulator.Create(width, height);
            using var terminal2 = TerminalEmulator.Create(width, height);

            // Fill both with same character
            FillScreenWithCharacter(terminal1, fillChar, width, height);
            FillScreenWithCharacter(terminal2, fillChar, width, height);

            // Position cursors at same position
            terminal1.Write($"\x1b[{cursorRow + 1};{cursorCol + 1}H");
            terminal2.Write($"\x1b[{cursorRow + 1};{cursorCol + 1}H");

            // Act: Apply same erase sequence to both
            terminal1.Write($"\x1b[{mode}J");
            terminal2.Write($"\x1b[{mode}J");

            // Assert: Both terminals should have identical screen content
            bool identical = true;
            for (int row = 0; row < height && identical; row++)
            {
                for (int col = 0; col < width && identical; col++)
                {
                    Cell cell1 = terminal1.ScreenBuffer.GetCell(row, col);
                    Cell cell2 = terminal2.ScreenBuffer.GetCell(row, col);
                    if (cell1.Character != cell2.Character)
                    {
                        identical = false;
                    }
                }
            }

            return identical;
        });
    }

    /// <summary>
    ///     Property: Line clearing operations should only affect the current line.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property LineClearingOnlyAffectsCurrentLine()
    {
        return Prop.ForAll(EraseLineModeArb, mode =>
        {
            // Use fixed dimensions for simplicity
            const int width = 20;
            const int height = 10;
            const char fillChar = 'W';
            const int cursorRow = 4;
            const int cursorCol = 8;

            // Arrange: Create terminal and fill with test character
            using var terminal = TerminalEmulator.Create(width, height);
            FillScreenWithCharacter(terminal, fillChar, width, height);

            // Position cursor at test position
            terminal.Write($"\x1b[{cursorRow + 1};{cursorCol + 1}H");

            // Act: Apply erase line sequence
            terminal.Write($"\x1b[{mode}K");

            // Assert: Only the current line should be affected
            bool onlyCurrentLineAffected = true;

            for (int row = 0; row < height; row++)
            {
                if (row != cursorRow)
                {
                    // Other lines should remain unchanged
                    for (int col = 0; col < width; col++)
                    {
                        Cell cell = terminal.ScreenBuffer.GetCell(row, col);
                        if (cell.Character != fillChar)
                        {
                            onlyCurrentLineAffected = false;
                            break;
                        }
                    }
                }

                if (!onlyCurrentLineAffected)
                {
                    break;
                }
            }

            return onlyCurrentLineAffected;
        });
    }

    /// <summary>
    ///     Helper method to determine if a cell should be cleared by display erase operation.
    /// </summary>
    private static bool ShouldCellBeClearedByDisplayErase(int row, int col, int cursorRow, int cursorCol,
        int mode, int width, int height)
    {
        return mode switch
        {
            0 => // From cursor to end of display
                row > cursorRow || (row == cursorRow && col >= cursorCol),
            1 => // From start of display to cursor
                row < cursorRow || (row == cursorRow && col <= cursorCol),
            2 or 3 => // Entire display (mode 3 also clears scrollback, but we only test screen)
                true,
            _ => false
        };
    }

    /// <summary>
    ///     Helper method to determine if a cell should be cleared by line erase operation.
    /// </summary>
    private static bool ShouldCellBeClearedByLineErase(int row, int col, int cursorRow, int cursorCol, int mode)
    {
        if (row != cursorRow)
        {
            return false; // Line erase only affects current line
        }

        return mode switch
        {
            0 => col >= cursorCol, // From cursor to end of line
            1 => col <= cursorCol, // From start of line to cursor
            2 => true, // Entire line
            _ => false
        };
    }

    /// <summary>
    ///     Helper method to fill the entire screen with a specific character.
    /// </summary>
    private static void FillScreenWithCharacter(TerminalEmulator terminal, char character, int width, int height)
    {
        terminal.Write("\x1b[1;1H"); // Move to top-left
        for (int row = 0; row < height; row++)
        {
            terminal.Write(new string(character, width)); // Fill row
            if (row < height - 1) // Don't move down on last row
            {
                terminal.Write("\r\n"); // Move to next line
            }
        }
    }
}
