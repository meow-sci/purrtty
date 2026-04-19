using System.Text;
using caTTY.Core.Terminal;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Tests that simulate real shell command output with escape sequences.
///     These tests verify that the terminal can handle realistic shell output patterns.
///     Validates Requirements 11.1, 12.1, 13.1.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ShellCommandSimulationTests
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
    ///     Simulates a simple shell prompt with cursor positioning.
    /// </summary>
    [Test]
    public void SimulateShellPrompt_WithCursorPositioning_WorksCorrectly()
    {
        // Simulate: user@host:~$ 
        string promptData = "user@host:~$ ";

        // Act
        _terminal.Write(promptData);

        // Assert
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(promptData.Length));

        // Verify prompt content
        for (int i = 0; i < promptData.Length; i++)
        {
            Assert.That(_terminal.ScreenBuffer.GetCell(0, i).Character, Is.EqualTo(promptData[i]));
        }
    }

    /// <summary>
    ///     Simulates ls command output with cursor movements.
    /// </summary>
    [Test]
    public void SimulateLsCommand_WithCursorMovements_WorksCorrectly()
    {
        // Simulate ls output with cursor positioning
        var lsOutput = new StringBuilder();
        lsOutput.Append("file1.txt\x1b[20G"); // Tab to column 20
        lsOutput.Append("file2.txt\x1b[40G"); // Tab to column 40
        lsOutput.Append("file3.txt\r\n"); // New line
        lsOutput.Append("dir1/\x1b[20G"); // Tab to column 20
        lsOutput.Append("dir2/\x1b[40G"); // Tab to column 40
        lsOutput.Append("dir3/");

        // Act
        _terminal.Write(lsOutput.ToString());

        // Assert
        // First line
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('f'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 8).Character, Is.EqualTo('t'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 19).Character,
            Is.EqualTo('f')); // file2.txt at column 20 (0-based 19)
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 39).Character,
            Is.EqualTo('f')); // file3.txt at column 40 (0-based 39)

        // Second line
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 0).Character, Is.EqualTo('d'));
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 4).Character, Is.EqualTo('/'));
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 19).Character, Is.EqualTo('d')); // dir2/ at column 20
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 39).Character, Is.EqualTo('d')); // dir3/ at column 40
    }

    /// <summary>
    ///     Simulates a command that clears the screen and redraws content.
    /// </summary>
    [Test]
    public void SimulateClearCommand_ClearsAndRedraws_WorksCorrectly()
    {
        // Arrange - Fill screen with initial content
        _terminal.Write("Initial content\nLine 2\nLine 3");

        // Act - Simulate clear command (clear screen + home cursor)
        _terminal.Write("\x1b[2J\x1b[H");
        _terminal.Write("After clear\r\nNew content"); // Use CR+LF for proper line ending

        // Assert
        // Old content should be gone
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('A')); // "After clear"
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 0).Character, Is.EqualTo('N')); // "New content"

        // Verify the clear worked by checking a position that had old content
        Assert.That(_terminal.ScreenBuffer.GetCell(2, 0).Character, Is.EqualTo(' ')); // Should be empty
    }

    /// <summary>
    ///     Simulates vim-like application that uses alternate screen buffer.
    ///     Note: This test focuses on cursor movements since alternate screen is not yet implemented.
    /// </summary>
    [Test]
    public void SimulateVimLikeApp_WithCursorMovements_WorksCorrectly()
    {
        // Simulate vim-like cursor movements
        _terminal.Write("Hello World");
        _terminal.Write("\x1b[1;1H"); // Home cursor
        _terminal.Write("\x1b[5C"); // Move right 5 columns
        _terminal.Write("X"); // Insert character
        _terminal.Write("\x1b[2;1H"); // Move to line 2
        _terminal.Write("Line 2");

        // Assert
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('H'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 5).Character, Is.EqualTo('X')); // Inserted at column 5
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 6).Character, Is.EqualTo('W')); // Original 'W' remains unchanged
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 0).Character, Is.EqualTo('L')); // "Line 2"
    }

    /// <summary>
    ///     Simulates a progress bar with carriage returns and overwrites.
    /// </summary>
    [Test]
    public void SimulateProgressBar_WithCarriageReturns_WorksCorrectly()
    {
        // Simulate progress bar updates
        _terminal.Write("Progress: [          ] 0%");
        _terminal.Write("\rProgress: [#         ] 10%");
        _terminal.Write("\rProgress: [##        ] 20%");
        _terminal.Write("\rProgress: [###       ] 30%");

        // Assert - Only the last progress state should be visible
        string line = "";
        for (int i = 0; i < 30; i++)
        {
            line += _terminal.ScreenBuffer.GetCell(0, i).Character;
        }

        Assert.That(line.TrimEnd(), Is.EqualTo("Progress: [###       ] 30%"));
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(26)); // End of progress string (26 characters)
    }

    /// <summary>
    ///     Simulates command output with line clearing (like when typing and using backspace).
    /// </summary>
    [Test]
    public void SimulateCommandLineEditing_WithLineClear_WorksCorrectly()
    {
        // Simulate typing a command, then moving cursor back and clearing part of it
        _terminal.Write("user@host:~$ ls -la /very/long/path");
        _terminal.Write("\x1b[14G"); // Move cursor to after prompt (column 14)
        _terminal.Write("\x1b[K"); // Clear from cursor to end of line
        _terminal.Write("pwd"); // Type new command

        // Assert
        string line = "";
        for (int i = 0; i < 20; i++)
        {
            line += _terminal.ScreenBuffer.GetCell(0, i).Character;
        }

        Assert.That(line.TrimEnd(), Is.EqualTo("user@host:~$ pwd")); // Should show new command
    }

    /// <summary>
    ///     Simulates output with mixed control characters and escape sequences.
    /// </summary>
    [Test]
    public void SimulateMixedOutput_WithControlAndEscape_WorksCorrectly()
    {
        // Simulate output with tabs, newlines, and cursor movements
        var output = new StringBuilder();
        output.Append("Column1\tColumn2\tColumn3\r\n"); // Tab-separated columns with proper line ending
        output.Append("Data1\tData2\tData3\r\n");
        output.Append("\x1b[1A"); // Move up one line
        output.Append("\x1b[20G"); // Move to column 20
        output.Append("*MODIFIED*"); // Mark as modified

        // Act
        _terminal.Write(output.ToString());

        // Assert
        // First line should have tab-separated content
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('C')); // "Column1"
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 8).Character, Is.EqualTo('C')); // "Column2" after tab
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 16).Character, Is.EqualTo('C')); // "Column3" after tab

        // Second line should have the modification
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 0).Character, Is.EqualTo('D')); // "Data1"
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 19).Character, Is.EqualTo('*')); // "*MODIFIED*" at column 20
    }

    /// <summary>
    ///     Simulates a command that produces output with cursor save/restore.
    ///     Note: This test focuses on basic cursor movements since save/restore is not yet implemented.
    /// </summary>
    [Test]
    public void SimulateCursorSaveRestore_WithBasicMovements_WorksCorrectly()
    {
        // Simulate saving cursor position, moving around, then restoring
        _terminal.Write("Original position");
        int originalRow = _terminal.Cursor.Row;
        int originalCol = _terminal.Cursor.Col;

        // Move cursor and write something
        _terminal.Write("\x1b[5;10H"); // Move to row 5, col 10
        _terminal.Write("Temporary");

        // Move back to approximate original position
        _terminal.Write($"\x1b[{originalRow + 1};{originalCol + 1}H");
        _terminal.Write(" RESTORED");

        // Assert
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('O')); // "Original position"
        Assert.That(_terminal.ScreenBuffer.GetCell(0, originalCol).Character, Is.EqualTo(' ')); // " RESTORED"
        Assert.That(_terminal.ScreenBuffer.GetCell(4, 9).Character, Is.EqualTo('T')); // "Temporary" at row 5, col 10
    }

    /// <summary>
    ///     Simulates rapid shell output with many escape sequences.
    ///     This tests the parser's ability to handle high-frequency escape sequence processing.
    /// </summary>
    [Test]
    public void SimulateRapidShellOutput_WithManyEscapeSequences_WorksCorrectly()
    {
        var output = new StringBuilder();

        // Simulate rapid cursor movements and text output
        for (int i = 0; i < 10; i++)
        {
            output.Append($"\x1b[{i + 1};1H"); // Move to line i+1, column 1
            output.Append($"Line {i}: ");
            output.Append($"\x1b[{i + 1};20H"); // Move to column 20
            output.Append($"Data {i}");
        }

        // Act
        _terminal.Write(output.ToString());

        // Assert
        for (int i = 0; i < 10; i++)
        {
            // Check line labels
            Assert.That(_terminal.ScreenBuffer.GetCell(i, 0).Character, Is.EqualTo('L')); // "Line"
            Assert.That(_terminal.ScreenBuffer.GetCell(i, 5).Character, Is.EqualTo((char)('0' + i))); // Line number

            // Check data at column 20
            Assert.That(_terminal.ScreenBuffer.GetCell(i, 19).Character, Is.EqualTo('D')); // "Data" at column 20
            Assert.That(_terminal.ScreenBuffer.GetCell(i, 24).Character, Is.EqualTo((char)('0' + i))); // Data number
        }
    }
}
