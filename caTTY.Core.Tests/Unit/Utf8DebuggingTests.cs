using System.Text;
using caTTY.Core.Terminal;
using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit;

/// <summary>
///     Debugging tests to understand UTF-8 handling issues.
/// </summary>
[TestFixture]
[Category("Unit")]
public class Utf8DebuggingTests
{
    private static void WriteMinimalSummary(string testName, params (string Key, object Value)[] items)
    {
        // Compact single-line summary to minimize stdout while keeping useful debug data.
        var parts = items.Select(i => $"{i.Key}={i.Value}");
        TestContext.WriteLine($"{testName}: {string.Join(", ", parts)}");
    }

    [Test]
    public void Debug_InvalidUtf8Byte153()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        // (int Row, int Col) initialCursor = (terminal.Cursor.Row, terminal.Cursor.Col);

        // Act - Process invalid UTF-8 byte 153 (0x99)
        byte[] invalidSequence = { 153 };
        terminal.Write(invalidSequence);

        // Debug output (compact)
        ICursor cursor = terminal.Cursor;
        // bool cursorValid = cursor.Row >= 0 && cursor.Row < terminal.Height && cursor.Col >= 0 && cursor.Col < terminal.Width;

        // Try recovery
        terminal.Write("RECOVERY_TEST");
        // ICursor recoveryCursor = terminal.Cursor;
        // bool recoverySuccessful = recoveryCursor.Col > cursor.Col || recoveryCursor.Row > cursor.Row;

        // WriteMinimalSummary(nameof(Debug_InvalidUtf8Byte153),
        //     ("InitialCursor", $"({initialCursor.Row},{initialCursor.Col})"),
        //     ("FinalCursor", $"({cursor.Row},{cursor.Col})"),
        //     ("CursorValid", cursorValid),
        //     ("RecoveryCursor", $"({recoveryCursor.Row},{recoveryCursor.Col})"),
        //     ("RecoverySuccessful", recoverySuccessful));

        // Assert - This should pass if the terminal handles invalid UTF-8 gracefully
        Assert.That(cursor.Row >= 0 && cursor.Row < terminal.Height, Is.True, "Cursor row should be valid");
        Assert.That(cursor.Col >= 0 && cursor.Col < terminal.Width, Is.True, "Cursor col should be valid");
    }

    [Test]
    public void Debug_MixedUtf8AndControl()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        // (int Row, int Col) initialCursor = (terminal.Cursor.Row, terminal.Cursor.Col);

        // Act - Process "Hello Worldpiñata"
        string mixedContent = "Hello Worldpiñata";
        terminal.Write(mixedContent);

        // Debug output (compact)
        ICursor cursor = terminal.Cursor;
        // var utf8Bytes = Encoding.UTF8.GetBytes(mixedContent);
        // (int Row, int Col) testPos = (cursor.Row, cursor.Col);
        terminal.Write("✓");
        // ICursor newCursor = terminal.Cursor;
        // bool utfWorks = newCursor.Col > testPos.Col || newCursor.Row > testPos.Row;

        // WriteMinimalSummary(nameof(Debug_MixedUtf8AndControl),
        //     ("InitialCursor", $"({initialCursor.Row},{initialCursor.Col})"),
        //     ("FinalCursor", $"({cursor.Row},{cursor.Col})"),
        //     ("ContentLen", mixedContent.Length),
        //     ("UTF8Bytes", string.Join(",", utf8Bytes)),
        //     ("CheckmarkCursor", $"({newCursor.Row},{newCursor.Col})"),
        //     ("UTF8Works", utfWorks));

        // Assert
        Assert.That(cursor.Row >= 0 && cursor.Row < terminal.Height, Is.True, "Cursor row should be valid");
        Assert.That(cursor.Col >= 0 && cursor.Col < terminal.Width, Is.True, "Cursor col should be valid");
    }

    [Test]
    public void Debug_WideCharacter()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        (int Row, int Col) initialCursor = (terminal.Cursor.Row, terminal.Cursor.Col);

        // Act - Process Chinese character "好"
        string wideChar = "好";
        terminal.Write(wideChar);

        // Debug output (compact)
        ICursor cursor = terminal.Cursor;
        var utf8Bytes = Encoding.UTF8.GetBytes(wideChar);
        bool cursorAdvanced = cursor.Col > initialCursor.Col || cursor.Row > initialCursor.Row;

        // Test functionality
        terminal.Write("X");
        ICursor testCursor = terminal.Cursor;
        bool terminalFunctional = testCursor.Row >= 0 && testCursor.Row < terminal.Height && testCursor.Col >= 0 && testCursor.Col < terminal.Width;

        // WriteMinimalSummary(nameof(Debug_WideCharacter),
        //     ("InitialCursor", $"({initialCursor.Row},{initialCursor.Col})"),
        //     ("FinalCursor", $"({cursor.Row},{cursor.Col})"),
        //     ("Char", wideChar),
        //     ("UTF8Bytes", string.Join(",", utf8Bytes)),
        //     ("CursorAdvanced", cursorAdvanced),
        //     ("TestCursor", $"({testCursor.Row},{testCursor.Col})"),
        //     ("TerminalFunctional", terminalFunctional));

        // Assert
        Assert.That(cursor.Col > initialCursor.Col || cursor.Row > initialCursor.Row, Is.True,
            "Cursor should advance for wide character");
    }
}
