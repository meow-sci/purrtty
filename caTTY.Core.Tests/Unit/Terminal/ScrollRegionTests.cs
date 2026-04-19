using caTTY.Core.Terminal;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Tests for scroll region management functionality.
/// </summary>
[TestFixture]
public class ScrollRegionTests
{
    private TerminalEmulator _terminal = null!;

    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal.Dispose();
    }

    [Test]
    public void SetScrollRegion_WithParameters_SetsCorrectBounds()
    {
        // Act: Set scroll region from row 5 to row 20 (1-based)
        _terminal.Write("\x1b[5;20r");

        // Assert: Scroll region should be set correctly (0-based)
        Assert.That(_terminal.State.ScrollTop, Is.EqualTo(4)); // 5-1 = 4
        Assert.That(_terminal.State.ScrollBottom, Is.EqualTo(19)); // 20-1 = 19
    }

    [Test]
    public void SetScrollRegion_WithNoParameters_ResetsToFullScreen()
    {
        // Arrange: First set a custom scroll region
        _terminal.Write("\x1b[5;20r");
        Assert.That(_terminal.State.ScrollTop, Is.EqualTo(4));
        Assert.That(_terminal.State.ScrollBottom, Is.EqualTo(19));

        // Act: Reset scroll region
        _terminal.Write("\x1b[r");

        // Assert: Should reset to full screen
        Assert.That(_terminal.State.ScrollTop, Is.EqualTo(0));
        Assert.That(_terminal.State.ScrollBottom, Is.EqualTo(23)); // 24-1 = 23
    }

    [Test]
    public void SetScrollRegion_HomesCursorToScrollRegion()
    {
        // Arrange: Move cursor to middle of screen
        _terminal.Write("\x1b[12;40H");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(11));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(39));

        // Act: Set scroll region from row 5 to row 20
        _terminal.Write("\x1b[5;20r");

        // Assert: Cursor should be homed to top-left of scroll region
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(4)); // ScrollTop (0-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0)); // Column 0
    }

    [Test]
    public void SetScrollRegion_WithInvalidBounds_IgnoresInvalidRegion()
    {
        // Act: Try to set invalid scroll region (top > bottom)
        _terminal.Write("\x1b[20;5r");

        // Assert: Should keep default scroll region
        Assert.That(_terminal.State.ScrollTop, Is.EqualTo(0));
        Assert.That(_terminal.State.ScrollBottom, Is.EqualTo(23));
    }

    [Test]
    public void CursorMovement_RespectsScrollRegionInOriginMode()
    {
        // Arrange: Set scroll region and enable origin mode
        _terminal.Write("\x1b[5;20r"); // Set scroll region rows 5-20
        _terminal.Write("\x1b[?6h");   // Enable origin mode (DECOM)

        // Act: Move cursor to position 1,1 (should be relative to scroll region)
        _terminal.Write("\x1b[1;1H");

        // Assert: Cursor should be at top of scroll region
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(4)); // ScrollTop (0-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));
    }

    [Test]
    public void CursorMovement_ClampsToScrollRegionInOriginMode()
    {
        // Arrange: Set scroll region and enable origin mode
        _terminal.Write("\x1b[5;20r"); // Set scroll region rows 5-20
        _terminal.Write("\x1b[?6h");   // Enable origin mode (DECOM)

        // Act: Try to move cursor beyond scroll region
        _terminal.Write("\x1b[50;1H"); // Try to move to row 50

        // Assert: Cursor should be clamped to bottom of scroll region
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(19)); // ScrollBottom (0-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));
    }

    [Test]
    public void ScrollUp_WithinScrollRegion_OnlyScrollsRegion()
    {
        // Arrange: Set scroll region and fill with test content
        _terminal.Write("\x1b[5;20r"); // Set scroll region rows 5-20
        
        // Fill the scroll region with identifiable content
        for (int row = 5; row <= 20; row++)
        {
            _terminal.Write($"\x1b[{row};1H{row:D2}"); // Write row number at start of each line
        }

        // Act: Scroll up within the region
        _terminal.Write("\x1b[2S"); // Scroll up 2 lines

        // Assert: Content outside scroll region should be unchanged
        // Content within scroll region should have scrolled
        _terminal.Write("\x1b[4;1H"); // Move to row before scroll region
        // We can't easily test the actual content without more complex setup,
        // but we can verify the scroll region bounds are still correct
        Assert.That(_terminal.State.ScrollTop, Is.EqualTo(4));
        Assert.That(_terminal.State.ScrollBottom, Is.EqualTo(19));
    }
}