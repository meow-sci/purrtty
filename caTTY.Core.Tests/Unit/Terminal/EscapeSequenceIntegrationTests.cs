using System.Text;
using caTTY.Core.Terminal;
using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Integration tests for escape sequence parsing in the terminal emulator.
///     Tests the integration between the parser and terminal for handling escape sequences,
///     including partial sequences across multiple Write calls.
///     Validates Requirements 11.1, 12.1, 13.1.
/// </summary>
[TestFixture]
[Category("Unit")]
public class EscapeSequenceIntegrationTests
{
    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal?.Dispose();
    }

    private TerminalEmulator _terminal = null!;

    /// <summary>
    ///     Tests that a complete CSI cursor movement sequence works correctly.
    /// </summary>
    [Test]
    public void Write_CompleteCsiCursorUp_MovesCursorCorrectly()
    {
        // Arrange
        _terminal.Write("Hello"); // Move cursor to column 5
        int initialRow = _terminal.Cursor.Row;
        int initialCol = _terminal.Cursor.Col;

        // Act - Send CSI A (cursor up)
        _terminal.Write("\x1b[A");

        // Assert
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(Math.Max(0, initialRow - 1)));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(initialCol)); // Column should not change
    }

    /// <summary>
    ///     Tests that a CSI cursor position sequence works correctly.
    /// </summary>
    [Test]
    public void Write_CsiCursorPosition_SetsCursorCorrectly()
    {
        // Arrange - Move cursor away from origin
        _terminal.Write("Hello World");

        // Act - Send CSI 5;10H (move to row 5, column 10)
        _terminal.Write("\x1b[5;10H");

        // Assert
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(4)); // 1-based to 0-based
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(9)); // 1-based to 0-based
    }

    /// <summary>
    ///     Tests that partial escape sequences across multiple Write calls work correctly.
    ///     This is critical for real shell integration where data may arrive in chunks.
    /// </summary>
    [Test]
    public void Write_PartialEscapeSequence_HandlesCorrectly()
    {
        // Arrange
        _terminal.Write("Test"); // Move cursor to column 4

        // Act - Send CSI sequence in parts: ESC, [, 5, A
        _terminal.Write("\x1b"); // ESC
        _terminal.Write("["); // [
        _terminal.Write("5"); // 5
        _terminal.Write("A"); // A (final byte)

        // Assert - Should move cursor up 5 rows
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0)); // Can't go above row 0
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(4)); // Column should not change
    }

    /// <summary>
    ///     Tests that partial escape sequences with parameters work correctly.
    /// </summary>
    [Test]
    public void Write_PartialEscapeSequenceWithParameters_HandlesCorrectly()
    {
        // Arrange
        _terminal.Write("Hello");

        // Act - Send CSI 10;20H in parts
        _terminal.Write("\x1b"); // ESC
        _terminal.Write("["); // [
        _terminal.Write("10"); // 10
        _terminal.Write(";"); // ;
        _terminal.Write("20"); // 20
        _terminal.Write("H"); // H (final byte)

        // Assert
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(9)); // 1-based to 0-based
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(19)); // 1-based to 0-based
    }

    /// <summary>
    ///     Tests that normal text mixed with escape sequences works correctly.
    /// </summary>
    [Test]
    public void Write_MixedTextAndEscapeSequences_HandlesCorrectly()
    {
        // Act - Write text, move cursor, write more text
        _terminal.Write("Hello");
        _terminal.Write("\x1b[2;1H"); // Move to row 2, column 1
        _terminal.Write("World");

        // Assert
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('H'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 1).Character, Is.EqualTo('e'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 2).Character, Is.EqualTo('l'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 3).Character, Is.EqualTo('l'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 4).Character, Is.EqualTo('o'));

        Assert.That(_terminal.ScreenBuffer.GetCell(1, 0).Character, Is.EqualTo('W'));
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 1).Character, Is.EqualTo('o'));
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 2).Character, Is.EqualTo('r'));
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 3).Character, Is.EqualTo('l'));
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 4).Character, Is.EqualTo('d'));
    }

    /// <summary>
    ///     Tests that screen clearing sequences work correctly.
    /// </summary>
    [Test]
    public void Write_ScreenClearingSequences_ClearsCorrectly()
    {
        // Arrange - Fill screen with content
        for (int i = 0; i < 5; i++)
        {
            _terminal.Write($"Line {i}\n");
        }

        // Act - Clear entire screen
        _terminal.Write("\x1b[2J");

        // Assert - Screen should be cleared
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                Cell cell = _terminal.ScreenBuffer.GetCell(row, col);
                Assert.That(cell.Character, Is.EqualTo(' '), $"Cell at ({row}, {col}) should be empty");
            }
        }
    }

    /// <summary>
    ///     Tests that line clearing sequences work correctly.
    /// </summary>
    [Test]
    public void Write_LineClearingSequences_ClearsCorrectly()
    {
        // Arrange
        _terminal.Write("Hello World");
        _terminal.Write("\x1b[1;6H"); // Move to column 6

        // Act - Clear from cursor to end of line
        _terminal.Write("\x1b[0K");

        // Assert
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('H'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 1).Character, Is.EqualTo('e'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 2).Character, Is.EqualTo('l'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 3).Character, Is.EqualTo('l'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 4).Character, Is.EqualTo('o'));

        // Characters from cursor position onward should be cleared
        for (int col = 5; col < 11; col++)
        {
            Assert.That(_terminal.ScreenBuffer.GetCell(0, col).Character, Is.EqualTo(' '),
                $"Character at column {col} should be cleared");
        }
    }

    /// <summary>
    ///     Tests that control characters during escape sequences are handled correctly.
    /// </summary>
    [Test]
    public void Write_ControlCharactersDuringEscapeSequence_HandlesCorrectly()
    {
        // Arrange
        bool bellRaised = false;
        _terminal.Bell += (sender, args) => bellRaised = true;

        // Act - Send BEL during CSI sequence (should be processed)
        _terminal.Write("\x1b"); // ESC
        _terminal.Write("["); // [
        _terminal.Write("\x07"); // BEL (should be processed)
        _terminal.Write("5"); // 5
        _terminal.Write("A"); // A (complete the sequence)

        // Assert
        Assert.That(bellRaised, Is.True, "BEL should be processed during escape sequence");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0)); // Cursor should still move up
    }

    /// <summary>
    ///     Tests that incomplete escape sequences at end of input are handled gracefully.
    /// </summary>
    [Test]
    public void Write_IncompleteEscapeSequenceAtEnd_HandlesGracefully()
    {
        // Act - Send incomplete escape sequence
        _terminal.Write("Hello");
        _terminal.Write("\x1b[5"); // Incomplete CSI sequence

        // Flush incomplete sequences
        _terminal.FlushIncompleteSequences();

        // Assert - Should not crash and cursor should remain at original position
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(5));

        // Screen content should be intact
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('H'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 1).Character, Is.EqualTo('e'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 2).Character, Is.EqualTo('l'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 3).Character, Is.EqualTo('l'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 4).Character, Is.EqualTo('o'));
    }

    /// <summary>
    ///     Tests that rapid sequence of escape sequences works correctly.
    ///     This simulates real shell output with many escape sequences.
    /// </summary>
    [Test]
    public void Write_RapidEscapeSequences_HandlesCorrectly()
    {
        // Act - Send multiple cursor movements rapidly
        _terminal.Write("\x1b[5;5H"); // Move to (5,5)
        _terminal.Write("\x1b[3A"); // Move up 3
        _terminal.Write("\x1b[2C"); // Move right 2
        _terminal.Write("\x1b[1B"); // Move down 1
        _terminal.Write("\x1b[1D"); // Move left 1

        // Assert - Final position should be (3,6) (0-based)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(2)); // 5-1-3+1 = 2
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(5)); // 5-1+2-1 = 5
    }

    /// <summary>
    ///     Tests that byte-by-byte processing of escape sequences works correctly.
    ///     This is the most stringent test for partial sequence handling.
    /// </summary>
    [Test]
    public void Write_ByteByByteEscapeSequence_HandlesCorrectly()
    {
        // Arrange
        string sequence = "\x1b[10;20H"; // CSI 10;20H
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act - Send each byte individually
        foreach (byte b in bytes)
        {
            _terminal.Write(new ReadOnlySpan<byte>(new[] { b }));
        }

        // Assert
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(9)); // 1-based to 0-based
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(19)); // 1-based to 0-based
    }
}
