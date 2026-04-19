using caTTY.Core.Terminal;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Tests for scrolling operations with scrollback integration.
///     Validates that scrolling properly moves content to scrollback buffer.
/// </summary>
[TestFixture]
public class ScrollingIntegrationTests
{
    private TerminalEmulator _terminal = null!;

    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(10, 5, 10, NullLogger.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal?.Dispose();
    }

    [Test]
    public void CsiScrollUp_WithContent_MovesContentToScrollback()
    {
        // Arrange: Fill screen with content
        _terminal.Write("Line 1\r\n");
        _terminal.Write("Line 2\r\n");
        _terminal.Write("Line 3\r\n");
        _terminal.Write("Line 4\r\n");
        _terminal.Write("Line 5");

        // Verify initial state
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(0, 0)), Is.EqualTo('L'));
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(1, 0)), Is.EqualTo('L'));

        // Act: Scroll up by 2 lines using CSI S
        _terminal.Write("\x1b[2S");

        // Assert: Top lines should be moved to scrollback, bottom should be empty
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(0, 0)), Is.EqualTo('L')); // Line 3 moved up
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(1, 0)), Is.EqualTo('L')); // Line 4 moved up
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(2, 0)), Is.EqualTo('L')); // Line 5 moved up
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(3, 0)), Is.EqualTo(' ')); // Empty
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(4, 0)), Is.EqualTo(' ')); // Empty

        // Verify scrollback has the scrolled content
        Assert.That(_terminal.ScrollbackManager.CurrentLines, Is.EqualTo(2));
    }

    [Test]
    public void CsiScrollDown_WithContent_ClearsTopLines()
    {
        // Arrange: Fill screen with content
        _terminal.Write("Line 1\r\n");
        _terminal.Write("Line 2\r\n");
        _terminal.Write("Line 3\r\n");
        _terminal.Write("Line 4\r\n");
        _terminal.Write("Line 5");

        // Act: Scroll down by 2 lines using CSI T
        _terminal.Write("\x1b[2T");

        // Assert: Top lines should be empty, content moved down
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(0, 0)), Is.EqualTo(' ')); // Empty
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(1, 0)), Is.EqualTo(' ')); // Empty
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(2, 0)), Is.EqualTo('L')); // Line 1 moved down
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(3, 0)), Is.EqualTo('L')); // Line 2 moved down
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(4, 0)), Is.EqualTo('L')); // Line 3 moved down
    }

    [Test]
    public void LineFeed_AtBottomOfScreen_ScrollsUpWithScrollback()
    {
        // Arrange: Fill screen and position cursor at bottom
        for (int i = 0; i < 5; i++)
        {
            _terminal.Write($"Line {i + 1}\r\n");
        }
        
        // Move cursor to last row
        _terminal.Write("\x1b[5;1H"); // Move to row 5, col 1

        // Act: Write line feed to trigger scroll
        _terminal.Write("\n");

        // Assert: Content should have scrolled up and moved to scrollback
        Assert.That(_terminal.ScrollbackManager.CurrentLines, Is.GreaterThan(0));
    }

    [Test]
    public void ScrollUp_InAlternateScreen_DoesNotAddToScrollback()
    {
        // Arrange: Simulate alternate screen mode
        _terminal.State.IsAlternateScreenActive = true;
        
        // Fill screen with content
        _terminal.Write("Line 1\r\n");
        _terminal.Write("Line 2\r\n");
        _terminal.Write("Line 3");

        // Act: Scroll up
        _terminal.Write("\x1b[1S");

        // Assert: No content should be added to scrollback in alternate screen
        Assert.That(_terminal.ScrollbackManager.CurrentLines, Is.EqualTo(0));
    }

    [Test]
    public void ScrollUp_WithZeroLines_DoesNothing()
    {
        // Arrange: Fill screen with content
        _terminal.Write("Line 1\r\n");
        _terminal.Write("Line 2");

        var originalFirstChar = GetCellText(_terminal.ScreenBuffer.GetCell(0, 0));

        // Act: Scroll up by 0 lines
        _terminal.Write("\x1b[0S");

        // Assert: Nothing should change
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(0, 0)), Is.EqualTo(originalFirstChar));
        Assert.That(_terminal.ScrollbackManager.CurrentLines, Is.EqualTo(0));
    }

    [Test]
    public void ScrollDown_WithZeroLines_DoesNothing()
    {
        // Arrange: Fill screen with content
        _terminal.Write("Line 1\r\n");
        _terminal.Write("Line 2");

        var originalFirstChar = GetCellText(_terminal.ScreenBuffer.GetCell(0, 0));

        // Act: Scroll down by 0 lines
        _terminal.Write("\x1b[0T");

        // Assert: Nothing should change
        Assert.That(GetCellText(_terminal.ScreenBuffer.GetCell(0, 0)), Is.EqualTo(originalFirstChar));
    }

    /// <summary>
    ///     Helper method to get the character from a cell.
    /// </summary>
    /// <param name="cell">The cell to extract character from</param>
    /// <returns>The character in the cell</returns>
    private static char GetCellText(Cell cell)
    {
        return cell.Character;
    }
}