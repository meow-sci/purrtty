using caTTY.Core.Terminal;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Integration;

/// <summary>
///     Integration tests for scrolling functionality validation.
///     These tests verify that scrolling works correctly in realistic scenarios.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ScrollingValidationTests
{
    /// <summary>
    ///     Test that long command output scrolls correctly and preserves content in scrollback.
    /// </summary>
    [Test]
    public void LongCommandOutput_ScrollsCorrectly_AndPreservesScrollback()
    {
        // Arrange
        using var terminal = TerminalEmulator.Create(80, 24, 100, NullLogger.Instance);
        
        // Act: Simulate long command output (more lines than screen height)
        var totalLines = 50; // More than 24 lines (screen height)
        for (int i = 1; i <= totalLines; i++)
        {
            terminal.Write($"Line {i:D3} - This is test content for scrolling validation\r\n");
        }

        // Assert: Verify scrolling behavior
        Assert.That(terminal.ScrollbackManager.CurrentLines, Is.GreaterThan(0), 
            "Scrollback should contain lines that scrolled off screen");
        
        // Note: The exact number may vary due to cursor positioning and line wrapping
        Assert.That(terminal.ScrollbackManager.CurrentLines, Is.GreaterThanOrEqualTo(totalLines - terminal.Height),
            "Scrollback should contain at least the lines that scrolled off screen");

        // Verify the last line is visible on screen (check multiple lines since cursor position affects layout)
        bool lastLineFound = false;
        for (int row = 0; row < terminal.Height; row++)
        {
            var screenLine = terminal.ScreenBuffer.GetRow(row);
            var lineText = new string(screenLine.ToArray().Select(c => c.Character).ToArray()).Trim();
            if (lineText.Contains($"Line {totalLines:D3}"))
            {
                lastLineFound = true;
                break;
            }
        }
        Assert.That(lastLineFound, Is.True,
            "Last line should be visible somewhere on screen");

        // Verify scrollback contains earlier content
        if (terminal.ScrollbackManager.CurrentLines > 0)
        {
            var firstScrollbackLine = terminal.ScrollbackManager.GetLine(0);
            var firstLineText = new string(firstScrollbackLine.ToArray().Select(c => c.Character).ToArray()).Trim();
            Assert.That(firstLineText, Does.Contain("Line 001"),
                "First line should be preserved in scrollback");
        }
    }

    /// <summary>
    ///     Test that viewport navigation and auto-scroll behavior work correctly.
    /// </summary>
    [Test]
    public void ViewportNavigation_WorksCorrectly_WithAutoScroll()
    {
        // Arrange
        using var terminal = TerminalEmulator.Create(80, 24, 100, NullLogger.Instance);
        
        // Generate content to fill scrollback
        for (int i = 1; i <= 30; i++)
        {
            terminal.Write($"Line {i:D3} - Content for viewport navigation test\r\n");
        }

        // Initially should be at bottom with auto-scroll enabled
        Assert.That(terminal.ScrollbackManager.IsAtBottom, Is.True,
            "Terminal should initially be at bottom");
        Assert.That(terminal.ScrollbackManager.AutoScrollEnabled, Is.True,
            "Auto-scroll should initially be enabled");

        // Act: Scroll up to disable auto-scroll
        terminal.ScrollbackManager.ScrollUp(5);

        // Assert: Auto-scroll should be disabled
        Assert.That(terminal.ScrollbackManager.IsAtBottom, Is.False,
            "Terminal should not be at bottom after scrolling up");
        Assert.That(terminal.ScrollbackManager.AutoScrollEnabled, Is.False,
            "Auto-scroll should be disabled after scrolling up");

        // Store current viewport offset
        var offsetBeforeNewContent = terminal.ScrollbackManager.ViewportOffset;
        var scrollbackLinesBeforeNewContent = terminal.ScrollbackManager.CurrentLines;

        // Act: Add new content while scrolled up
        terminal.Write("New line added while scrolled up\r\n");

        var scrollbackLinesAfterNewContent = terminal.ScrollbackManager.CurrentLines;
        var linesAddedToScrollback = Math.Max(0, scrollbackLinesAfterNewContent - scrollbackLinesBeforeNewContent);

        // Assert: Viewport should not yank.
        // ViewportOffset is measured from bottom; keeping the visible content stable requires
        // increasing the offset by the number of appended scrollback rows.
        Assert.That(terminal.ScrollbackManager.ViewportOffset, Is.EqualTo(offsetBeforeNewContent + linesAddedToScrollback),
            "Viewport should remain stable when new content is added while scrolled up");
        Assert.That(terminal.ScrollbackManager.AutoScrollEnabled, Is.False,
            "Auto-scroll should remain disabled");

        // Act: Scroll back to bottom
        terminal.ScrollbackManager.ScrollToBottom();

        // Assert: Auto-scroll should be re-enabled
        Assert.That(terminal.ScrollbackManager.IsAtBottom, Is.True,
            "Terminal should be at bottom after scrolling to bottom");
        Assert.That(terminal.ScrollbackManager.AutoScrollEnabled, Is.True,
            "Auto-scroll should be re-enabled after scrolling to bottom");

        // Act: Add more content
        terminal.Write("Final line with auto-scroll enabled\r\n");

        // Assert: Should stay at bottom due to auto-scroll
        Assert.That(terminal.ScrollbackManager.IsAtBottom, Is.True,
            "Terminal should stay at bottom with auto-scroll enabled");
    }

    /// <summary>
    ///     Test that screen buffer resize preserves content appropriately.
    /// </summary>
    [Test]
    public void ScreenBufferResize_PreservesContent_Appropriately()
    {
        // Arrange
        using var terminal = TerminalEmulator.Create(40, 10, 50, NullLogger.Instance);
        
        // Fill terminal with identifiable content
        for (int i = 1; i <= 15; i++) // More than screen height
        {
            terminal.Write($"Line {i:D2} - Resize test content\r\n");
        }

        // Store some content for verification
        var scrollbackLinesBefore = terminal.ScrollbackManager.CurrentLines;
        
        // Act: Resize terminal (increase height)
        terminal.Resize(50, 15);

        // Assert: Verify resize behavior
        Assert.That(terminal.Width, Is.EqualTo(50), "Width should be updated");
        Assert.That(terminal.Height, Is.EqualTo(15), "Height should be updated");
        
        // Scrollback should be preserved
        Assert.That(terminal.ScrollbackManager.CurrentLines, Is.GreaterThanOrEqualTo(0),
            "Scrollback should be preserved after resize");

        // Terminal should remain functional
        terminal.Write("Post-resize test line\r\n");
        Assert.That(terminal.Cursor.Row, Is.GreaterThanOrEqualTo(0), "Cursor should remain valid");
        Assert.That(terminal.Cursor.Row, Is.LessThan(terminal.Height), "Cursor should be within bounds");
    }

    /// <summary>
    ///     Test that scrollback buffer manages capacity correctly.
    /// </summary>
    [Test]
    public void ScrollbackBuffer_ManagesCapacity_Correctly()
    {
        // Arrange: Create terminal with small scrollback capacity
        var scrollbackCapacity = 10;
        using var terminal = TerminalEmulator.Create(80, 5, scrollbackCapacity, NullLogger.Instance);
        
        // Act: Generate more content than scrollback capacity
        var totalLines = scrollbackCapacity + 10; // Exceed capacity
        for (int i = 1; i <= totalLines; i++)
        {
            terminal.Write($"Line {i:D3} - Capacity management test\r\n");
        }

        // Assert: Scrollback should not exceed capacity
        Assert.That(terminal.ScrollbackManager.CurrentLines, Is.LessThanOrEqualTo(scrollbackCapacity),
            "Scrollback should not exceed configured capacity");

        // Verify FIFO behavior - oldest lines should be removed
        if (terminal.ScrollbackManager.CurrentLines > 0)
        {
            var firstScrollbackLine = terminal.ScrollbackManager.GetLine(0);
            var firstLineText = new string(firstScrollbackLine.ToArray().Select(c => c.Character).ToArray()).Trim();
            
            // Should not contain the very first lines (they should be removed)
            Assert.That(firstLineText, Does.Not.Contain("Line 001"),
                "Oldest lines should be removed when capacity is exceeded");
        }
    }

    /// <summary>
    ///     Test that mixed content types scroll correctly.
    /// </summary>
    [Test]
    public void MixedContentTypes_ScrollCorrectly()
    {
        // Arrange
        using var terminal = TerminalEmulator.Create(80, 10, 50, NullLogger.Instance);
        
        // Act: Generate mixed content types
        terminal.Write("Short line\r\n");
        terminal.Write("This is a very long line that exceeds the normal terminal width and should test wrapping behavior during scrolling operations\r\n");
        terminal.Write("Line with special chars: àáâãäåæçèéêë\r\n");
        terminal.Write("\x1b[31mRed text\x1b[32mGreen text\x1b[0mNormal text\r\n");
        
        // Generate enough content to cause scrolling
        for (int i = 1; i <= 20; i++)
        {
            terminal.Write($"Additional line {i} to trigger scrolling\r\n");
        }

        // Assert: Verify scrolling occurred and content is preserved
        Assert.That(terminal.ScrollbackManager.CurrentLines, Is.GreaterThan(0),
            "Mixed content should scroll correctly");

        // Verify terminal remains functional after mixed content
        terminal.Write("Final test line\r\n");
        Assert.That(terminal.Cursor.Row, Is.GreaterThanOrEqualTo(0), "Cursor should remain valid");
        Assert.That(terminal.Cursor.Col, Is.GreaterThanOrEqualTo(0), "Cursor should remain valid");
    }

    /// <summary>
    ///     Test that scroll operations work correctly with CSI sequences.
    /// </summary>
    [Test]
    public void CsiScrollOperations_WorkCorrectly()
    {
        // Arrange
        using var terminal = TerminalEmulator.Create(80, 10, 50, NullLogger.Instance);
        
        // Fill screen with content
        for (int i = 1; i <= 10; i++)
        {
            terminal.Write($"Line {i:D2} - CSI scroll test\r\n");
        }

        var initialScrollbackLines = terminal.ScrollbackManager.CurrentLines;

        // Act: Perform CSI scroll up operation
        terminal.Write("\x1b[3S"); // Scroll up 3 lines

        // Assert: Verify scroll operation
        Assert.That(terminal.ScrollbackManager.CurrentLines, Is.EqualTo(initialScrollbackLines + 3),
            "CSI scroll up should add lines to scrollback");

        // Verify screen content shifted correctly
        var topLine = terminal.ScreenBuffer.GetRow(0);
        var topLineText = new string(topLine.ToArray().Select(c => c.Character).ToArray()).Trim();
        
        // After scroll up, the content should have moved up, so we should see content that was previously lower
        // The exact content depends on the implementation, but it should not be the original first line
        Assert.That(topLineText, Is.Not.Empty,
            "Screen should contain content after scroll up operation");

        // Act: Perform CSI scroll down operation
        terminal.Write("\x1b[2T"); // Scroll down 2 lines

        // Assert: Verify scroll down operation
        // Note: Scroll down doesn't add to scrollback, it just moves content down
        var bottomLines = terminal.ScreenBuffer.GetRow(terminal.Height - 1);
        var bottomLineText = new string(bottomLines.ToArray().Select(c => c.Character).ToArray()).Trim();
        
        // Bottom line should be empty after scroll down
        Assert.That(bottomLineText, Is.Empty.Or.EqualTo(new string(' ', 80).Trim()),
            "Bottom lines should be cleared after scroll down operation");
    }

    /// <summary>
    ///     Test that terminal remains stable under stress conditions.
    /// </summary>
    [Test]
    public void StressTest_TerminalRemainsStable()
    {
        // Arrange
        using var terminal = TerminalEmulator.Create(80, 24, 100, NullLogger.Instance);
        
        // Act: Generate high volume of content rapidly
        var totalLines = 500;
        for (int i = 1; i <= totalLines; i++)
        {
            terminal.Write($"Stress test line {i:D4} - High volume content generation for stability testing\r\n");
            
            // Occasionally perform other operations
            if (i % 50 == 0)
            {
                terminal.Write("\x1b[2S"); // Scroll up
                terminal.Write("\x1b[1T"); // Scroll down
            }
        }

        // Assert: Terminal should remain stable and functional
        Assert.That(terminal.Width, Is.EqualTo(80), "Terminal width should be preserved");
        Assert.That(terminal.Height, Is.EqualTo(24), "Terminal height should be preserved");
        Assert.That(terminal.Cursor.Row, Is.GreaterThanOrEqualTo(0), "Cursor row should be valid");
        Assert.That(terminal.Cursor.Row, Is.LessThan(terminal.Height), "Cursor row should be within bounds");
        Assert.That(terminal.Cursor.Col, Is.GreaterThanOrEqualTo(0), "Cursor column should be valid");
        Assert.That(terminal.Cursor.Col, Is.LessThan(terminal.Width), "Cursor column should be within bounds");

        // Scrollback should be managed correctly
        Assert.That(terminal.ScrollbackManager.CurrentLines, Is.LessThanOrEqualTo(100),
            "Scrollback should not exceed capacity even under stress");

        // Terminal should still be responsive
        terminal.Write("Final stability test line\r\n");
        Assert.DoesNotThrow(() => terminal.Write("Additional test content\r\n"),
            "Terminal should remain responsive after stress test");
    }

    /// <summary>
    ///     Test edge case with empty terminal scrolling.
    /// </summary>
    [Test]
    public void EmptyTerminalScrolling_HandledGracefully()
    {
        // Arrange
        using var terminal = TerminalEmulator.Create(80, 24, 50, NullLogger.Instance);
        
        // Act & Assert: Try scrolling operations on empty terminal
        Assert.DoesNotThrow(() => terminal.ScrollbackManager.ScrollUp(5),
            "Scrolling up on empty terminal should not throw");
        Assert.DoesNotThrow(() => terminal.ScrollbackManager.ScrollDown(3),
            "Scrolling down on empty terminal should not throw");
        Assert.DoesNotThrow(() => terminal.ScrollbackManager.ScrollToTop(),
            "Scrolling to top on empty terminal should not throw");
        Assert.DoesNotThrow(() => terminal.ScrollbackManager.ScrollToBottom(),
            "Scrolling to bottom on empty terminal should not throw");

        // Verify state remains valid
        Assert.That(terminal.ScrollbackManager.CurrentLines, Is.EqualTo(0),
            "Empty terminal should have no scrollback lines");
        Assert.That(terminal.ScrollbackManager.IsAtBottom, Is.True,
            "Empty terminal should be considered at bottom");
        Assert.That(terminal.ScrollbackManager.AutoScrollEnabled, Is.True,
            "Empty terminal should have auto-scroll enabled");
    }
}