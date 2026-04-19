using caTTY.Core.Terminal;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Tests for control string handling (SOS/PM/APC sequences).
///     Ensures these sequences are safely skipped until ST terminator.
/// </summary>
[TestFixture]
public class ControlStringTests
{
    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal.Dispose();
    }

    private TerminalEmulator _terminal = null!;

    [Test]
    public void HandleControlString_SosSequence_SafelySkippedUntilST()
    {
        // Arrange - Get initial cursor position
        (int Col, int Row) initialCursor = (_terminal.Cursor.Col, _terminal.Cursor.Row);

        // Act - Send SOS sequence with some payload, then normal text
        _terminal.Write("\x1bXSome SOS payload\x1b\\Hello");

        // Assert - SOS should be skipped, only "Hello" should be processed
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(initialCursor.Col + 5)); // "Hello" = 5 chars
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(initialCursor.Row));

        // Verify the screen contains "Hello"
        Cell cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('H'));
    }

    [Test]
    public void HandleControlString_PmSequence_SafelySkippedUntilST()
    {
        // Arrange - Get initial cursor position
        (int Col, int Row) initialCursor = (_terminal.Cursor.Col, _terminal.Cursor.Row);

        // Act - Send PM sequence with some payload, then normal text
        _terminal.Write("\x1b^Some PM payload\x1b\\World");

        // Assert - PM should be skipped, only "World" should be processed
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(initialCursor.Col + 5)); // "World" = 5 chars
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(initialCursor.Row));

        // Verify the screen contains "World"
        Cell cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('W'));
    }

    [Test]
    public void HandleControlString_ApcSequence_SafelySkippedUntilST()
    {
        // Arrange - Get initial cursor position
        (int Col, int Row) initialCursor = (_terminal.Cursor.Col, _terminal.Cursor.Row);

        // Act - Send APC sequence with some payload, then normal text
        _terminal.Write("\x1b_Some APC payload\x1b\\Test");

        // Assert - APC should be skipped, only "Test" should be processed
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(initialCursor.Col + 4)); // "Test" = 4 chars
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(initialCursor.Row));

        // Verify the screen contains "Test"
        Cell cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('T'));
    }

    [Test]
    public void HandleControlString_CanAbortControlString_ResetsState()
    {
        // Arrange - Get initial cursor position
        (int Col, int Row) initialCursor = (_terminal.Cursor.Col, _terminal.Cursor.Row);

        // Act - Start SOS sequence, send CAN to abort, then normal text
        _terminal.Write("\x1bXSome SOS\x18Normal");

        // Assert - CAN should abort the control string, "Normal" should be processed
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(initialCursor.Col + 6)); // "Normal" = 6 chars
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(initialCursor.Row));

        // Verify the screen contains "Normal"
        Cell cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('N'));
    }

    [Test]
    public void HandleControlString_SubAbortControlString_ResetsState()
    {
        // Arrange - Get initial cursor position
        (int Col, int Row) initialCursor = (_terminal.Cursor.Col, _terminal.Cursor.Row);

        // Act - Start PM sequence, send SUB to abort, then normal text
        _terminal.Write("\x1b^Some PM\x1aText");

        // Assert - SUB should abort the control string, "Text" should be processed
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(initialCursor.Col + 4)); // "Text" = 4 chars
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(initialCursor.Row));

        // Verify the screen contains "Text"
        Cell cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('T'));
    }
}
