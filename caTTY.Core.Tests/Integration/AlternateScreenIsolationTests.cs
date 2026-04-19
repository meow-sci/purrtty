using caTTY.Core.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Integration;

/// <summary>
///     Integration tests for alternate screen isolation behavior.
///     Validates Requirements 15.3 and 15.5 - scrollback isolation and state preservation.
/// </summary>
[TestFixture]
[Category("Integration")]
public class AlternateScreenIsolationTests
{
    private TerminalEmulator _terminal = null!;

    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, 1000, NullLogger.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal.Dispose();
    }

    [Test]
    public void AlternateScreen_DoesNotAddToScrollback_WhenScrollingContent()
    {
        // Arrange: Fill primary screen with content that will create scrollback
        for (int i = 0; i < 30; i++)
        {
            _terminal.Write($"Primary line {i}\r\n");
        }
        
        int initialScrollbackLines = _terminal.ScrollbackManager.CurrentLines;
        Assert.That(initialScrollbackLines, Is.GreaterThan(0), "Should have scrollback from primary screen");

        // Act: Switch to alternate screen and add content that would normally create scrollback
        _terminal.Write("\x1b[?47h"); // Switch to alternate screen
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.True);

        for (int i = 0; i < 30; i++)
        {
            _terminal.Write($"Alternate line {i}\r\n");
        }

        // Assert: Scrollback should not have increased while in alternate screen
        int scrollbackAfterAlternate = _terminal.ScrollbackManager.CurrentLines;
        Assert.That(scrollbackAfterAlternate, Is.EqualTo(initialScrollbackLines), 
            "Scrollback should not increase while in alternate screen mode");

        // Act: Switch back to primary screen
        _terminal.Write("\x1b[?47l"); // Switch back to primary screen
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.False);

        // Assert: Scrollback should still be unchanged
        int finalScrollbackLines = _terminal.ScrollbackManager.CurrentLines;
        Assert.That(finalScrollbackLines, Is.EqualTo(initialScrollbackLines), 
            "Scrollback should remain unchanged after returning to primary screen");
    }

    [Test]
    public void AlternateScreen_ClearsBufferOnActivation_WithMode1049()
    {
        // Arrange: Put content in primary screen
        _terminal.Write("Primary content");
        _terminal.Write("\x1b[5;10H"); // Move cursor to specific position
        
        // Act: Activate alternate screen with clear (mode 1049)
        _terminal.Write("\x1b[?1049h");
        
        // Assert: Alternate screen should be clear and cursor at origin
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.True);
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0), "Cursor should be at row 0 after clearing alternate screen");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0), "Cursor should be at column 0 after clearing alternate screen");
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo(' '), "Alternate screen should be cleared");
        
        // Act: Add content to alternate screen
        _terminal.Write("Alternate content");
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('A'));
        
        // Act: Switch back to primary screen
        _terminal.Write("\x1b[?1049l");
        
        // Assert: Primary screen content should be preserved
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.False);
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('P'), 
            "Primary screen content should be preserved");
    }

    [Test]
    public void AlternateScreen_MaintainsSeparateCursorPositions()
    {
        // Arrange: Set cursor position in primary screen
        _terminal.Write("\x1b[10;20H"); // Move to row 10, col 20
        _terminal.Write("Primary");
        int primaryRow = _terminal.Cursor.Row;
        int primaryCol = _terminal.Cursor.Col;
        
        // Act: Switch to alternate screen
        _terminal.Write("\x1b[?47h");
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.True);
        
        // Cursor should be at alternate screen's saved position (initially 0,0)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));
        
        // Move cursor in alternate screen
        _terminal.Write("\x1b[5;15H"); // Move to row 5, col 15
        _terminal.Write("Alternate");
        int alternateRow = _terminal.Cursor.Row;
        int alternateCol = _terminal.Cursor.Col;
        
        // Act: Switch back to primary screen
        _terminal.Write("\x1b[?47l");
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.False);
        
        // Assert: Primary screen cursor position should be restored
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(primaryRow), 
            "Primary screen cursor row should be restored");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(primaryCol), 
            "Primary screen cursor column should be restored");
        
        // Act: Switch back to alternate screen again
        _terminal.Write("\x1b[?47h");
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.True);
        
        // Assert: Alternate screen cursor position should be restored
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(alternateRow), 
            "Alternate screen cursor row should be restored");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(alternateCol), 
            "Alternate screen cursor column should be restored");
    }

    [Test]
    public void AlternateScreen_PreservesBufferContentIndependently()
    {
        // Arrange: Put content in primary screen
        _terminal.Write("Primary screen content");
        char primaryFirstChar = _terminal.ScreenBuffer.GetCell(0, 0).Character;
        
        // Act: Switch to alternate screen and add different content
        _terminal.Write("\x1b[?47h");
        _terminal.Write("Alternate screen content");
        char alternateFirstChar = _terminal.ScreenBuffer.GetCell(0, 0).Character;
        
        // Assert: Content should be different
        Assert.That(alternateFirstChar, Is.Not.EqualTo(primaryFirstChar));
        
        // Act: Switch back to primary
        _terminal.Write("\x1b[?47l");
        
        // Assert: Primary content should be restored
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo(primaryFirstChar));
        
        // Act: Switch back to alternate
        _terminal.Write("\x1b[?47h");
        
        // Assert: Alternate content should be preserved
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo(alternateFirstChar));
    }
}