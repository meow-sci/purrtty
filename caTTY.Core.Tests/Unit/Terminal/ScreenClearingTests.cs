using caTTY.Core.Terminal;
using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Tests for screen clearing operations (CSI J and CSI K sequences).
///     Validates Requirements 11.6, 11.7.
/// </summary>
[TestFixture]
public class ScreenClearingTests
{
    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(10, 5); // 10 cols, 5 rows for testing
    }

    [TearDown]
    public void TearDown()
    {
        _terminal.Dispose();
    }

    private TerminalEmulator _terminal = null!;

    [Test]
    public void ClearDisplay_Mode0_ClearsFromCursorToEnd()
    {
        // Arrange: Fill screen with 'X' characters
        FillScreenWithCharacter('X');

        // Position cursor at (2, 3) - middle of screen
        _terminal.Write("\x1b[3;4H"); // Move to row 3, col 4 (1-based)

        // Act: Clear from cursor to end (CSI 0 J)
        _terminal.Write("\x1b[0J");

        // Assert: Characters before cursor should remain, after should be cleared
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                Cell cell = _terminal.ScreenBuffer.GetCell(row, col);

                if (row < 2) // Rows above cursor
                {
                    Assert.That(cell.Character, Is.EqualTo('X'), $"Cell at ({row}, {col}) should remain 'X'");
                }
                else if (row == 2 && col < 3) // Current row, before cursor
                {
                    Assert.That(cell.Character, Is.EqualTo('X'), $"Cell at ({row}, {col}) should remain 'X'");
                }
                else // From cursor to end
                {
                    Assert.That(cell.Character, Is.EqualTo(' '), $"Cell at ({row}, {col}) should be cleared");
                }
            }
        }
    }

    [Test]
    public void ClearDisplay_Mode1_ClearsFromStartToCursor()
    {
        // Arrange: Fill screen with 'X' characters
        FillScreenWithCharacter('X');

        // Position cursor at (2, 3) - middle of screen
        _terminal.Write("\x1b[3;4H"); // Move to row 3, col 4 (1-based)

        // Act: Clear from start to cursor (CSI 1 J)
        _terminal.Write("\x1b[1J");

        // Assert: Characters from start to cursor should be cleared, after should remain
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                Cell cell = _terminal.ScreenBuffer.GetCell(row, col);

                if (row < 2) // Rows above cursor
                {
                    Assert.That(cell.Character, Is.EqualTo(' '), $"Cell at ({row}, {col}) should be cleared");
                }
                else if (row == 2 && col <= 3) // Current row, up to cursor
                {
                    Assert.That(cell.Character, Is.EqualTo(' '), $"Cell at ({row}, {col}) should be cleared");
                }
                else // After cursor
                {
                    Assert.That(cell.Character, Is.EqualTo('X'), $"Cell at ({row}, {col}) should remain 'X'");
                }
            }
        }
    }

    [Test]
    public void ClearDisplay_Mode2_ClearsEntireScreen()
    {
        // Arrange: Fill screen with 'X' characters
        FillScreenWithCharacter('X');

        // Position cursor at (2, 3) - middle of screen
        _terminal.Write("\x1b[3;4H"); // Move to row 3, col 4 (1-based)

        // Act: Clear entire screen (CSI 2 J)
        _terminal.Write("\x1b[2J");

        // Assert: All characters should be cleared
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                Cell cell = _terminal.ScreenBuffer.GetCell(row, col);
                Assert.That(cell.Character, Is.EqualTo(' '), $"Cell at ({row}, {col}) should be cleared");
            }
        }
    }

    [Test]
    public void ClearDisplay_Mode3_ClearsEntireScreenAndScrollback()
    {
        // Arrange: Fill screen with 'X' characters
        FillScreenWithCharacter('X');

        // Act: Clear entire screen and scrollback (CSI 3 J)
        _terminal.Write("\x1b[3J");

        // Assert: All characters should be cleared (scrollback clearing will be tested in future task)
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                Cell cell = _terminal.ScreenBuffer.GetCell(row, col);
                Assert.That(cell.Character, Is.EqualTo(' '), $"Cell at ({row}, {col}) should be cleared");
            }
        }
    }

    [Test]
    public void ClearLine_Mode0_ClearsFromCursorToEndOfLine()
    {
        // Arrange: Fill screen with 'X' characters
        FillScreenWithCharacter('X');

        // Position cursor at (2, 3) - middle of line
        _terminal.Write("\x1b[3;4H"); // Move to row 3, col 4 (1-based)

        // Act: Clear from cursor to end of line (CSI 0 K)
        _terminal.Write("\x1b[0K");

        // Assert: Only current line from cursor to end should be cleared
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                Cell cell = _terminal.ScreenBuffer.GetCell(row, col);

                if (row == 2 && col >= 3) // Current row, from cursor to end
                {
                    Assert.That(cell.Character, Is.EqualTo(' '), $"Cell at ({row}, {col}) should be cleared");
                }
                else // All other cells
                {
                    Assert.That(cell.Character, Is.EqualTo('X'), $"Cell at ({row}, {col}) should remain 'X'");
                }
            }
        }
    }

    [Test]
    public void ClearLine_Mode1_ClearsFromStartOfLineToCursor()
    {
        // Arrange: Fill screen with 'X' characters
        FillScreenWithCharacter('X');

        // Position cursor at (2, 3) - middle of line
        _terminal.Write("\x1b[3;4H"); // Move to row 3, col 4 (1-based)

        // Act: Clear from start of line to cursor (CSI 1 K)
        _terminal.Write("\x1b[1K");

        // Assert: Only current line from start to cursor should be cleared
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                Cell cell = _terminal.ScreenBuffer.GetCell(row, col);

                if (row == 2 && col <= 3) // Current row, from start to cursor
                {
                    Assert.That(cell.Character, Is.EqualTo(' '), $"Cell at ({row}, {col}) should be cleared");
                }
                else // All other cells
                {
                    Assert.That(cell.Character, Is.EqualTo('X'), $"Cell at ({row}, {col}) should remain 'X'");
                }
            }
        }
    }

    [Test]
    public void ClearLine_Mode2_ClearsEntireLine()
    {
        // Arrange: Fill screen with 'X' characters
        FillScreenWithCharacter('X');

        // Position cursor at (2, 3) - middle of line
        _terminal.Write("\x1b[3;4H"); // Move to row 3, col 4 (1-based)

        // Act: Clear entire line (CSI 2 K)
        _terminal.Write("\x1b[2K");

        // Assert: Only current line should be cleared
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                Cell cell = _terminal.ScreenBuffer.GetCell(row, col);

                if (row == 2) // Current row
                {
                    Assert.That(cell.Character, Is.EqualTo(' '), $"Cell at ({row}, {col}) should be cleared");
                }
                else // All other rows
                {
                    Assert.That(cell.Character, Is.EqualTo('X'), $"Cell at ({row}, {col}) should remain 'X'");
                }
            }
        }
    }

    [Test]
    public void ClearOperations_PreserveSgrAttributes()
    {
        // Arrange: Set SGR attributes (will be implemented in future task)
        // For now, just verify that clearing uses current SGR state

        // Position cursor at (1, 1)
        _terminal.Write("\x1b[2;2H");

        // Act: Clear line
        _terminal.Write("\x1b[2K");

        // Assert: Cleared cells should have current SGR attributes
        Cell cell = _terminal.ScreenBuffer.GetCell(1, 1);
        Assert.That(cell.Character, Is.EqualTo(' '));
        // SGR attributes will be tested when SGR is implemented
    }

    [Test]
    public void ClearOperations_ClearWrapPendingState()
    {
        // Arrange: Set wrap pending state
        _terminal.Write(new string('X', 10)); // Fill first line to trigger wrap pending

        // Act: Clear display
        _terminal.Write("\x1b[2J");

        // Assert: Wrap pending should be cleared (verified by cursor position)
        Assert.That(_terminal.State.WrapPending, Is.False);
    }

    /// <summary>
    ///     Helper method to fill the entire screen with a specific character.
    /// </summary>
    private void FillScreenWithCharacter(char character)
    {
        _terminal.Write("\x1b[1;1H"); // Move to top-left
        for (int row = 0; row < 5; row++)
        {
            _terminal.Write(new string(character, 10)); // Fill row
            if (row < 4) // Don't move down on last row
            {
                _terminal.Write("\r\n"); // Move to next line
            }
        }
    }
}
