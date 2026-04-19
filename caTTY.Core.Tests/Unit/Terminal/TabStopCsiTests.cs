using caTTY.Core.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Tests for tab stop CSI sequences.
///     Validates Requirements 10.4, 11.1, 19.1, 19.2.
/// </summary>
[TestFixture]
public class TabStopCsiTests
{
    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal?.Dispose();
    }

    private TerminalEmulator _terminal = null!;

    [Test]
    public void CursorForwardTab_MovesToNextTabStop()
    {
        // Arrange: Start at origin (0, 0)
        _terminal.Write("\x1b[H"); // Move to home position
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));

        // Act: Move forward 1 tab stop (CSI I)
        _terminal.Write("\x1b[I");

        // Assert: Should be at column 8 (first tab stop)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(8));
    }

    [Test]
    public void CursorForwardTab_WithCount_MovesMultipleTabStops()
    {
        // Arrange: Start at origin (0, 0)
        _terminal.Write("\x1b[H");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));

        // Act: Move forward 3 tab stops (CSI 3 I)
        _terminal.Write("\x1b[3I");

        // Assert: Should be at column 24 (3rd tab stop: 8, 16, 24)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(24));
    }

    [Test]
    public void CursorForwardTab_FromMiddlePosition_MovesToNextTabStop()
    {
        // Arrange: Start at column 5
        _terminal.Write("\x1b[1;6H"); // Move to row 1, col 6 (1-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(5));

        // Act: Move forward 1 tab stop
        _terminal.Write("\x1b[I");

        // Assert: Should be at column 8 (next tab stop after 5)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(8));
    }

    [Test]
    public void CursorForwardTab_AtRightEdge_StaysAtRightEdge()
    {
        // Arrange: Start at right edge (column 79)
        _terminal.Write("\x1b[1;80H"); // Move to row 1, col 80 (1-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(79));

        // Act: Try to move forward 1 tab stop
        _terminal.Write("\x1b[I");

        // Assert: Should stay at right edge (no tab stops beyond)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(79));
    }

    [Test]
    public void CursorForwardTab_WithZeroParameter_UsesDefaultCount()
    {
        // Arrange: Start at origin
        _terminal.Write("\x1b[H");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));

        // Act: Move forward with zero parameter (should default to 1)
        _terminal.Write("\x1b[0I");

        // Assert: Should move to first tab stop
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(8));
    }

    [Test]
    public void CursorForwardTab_ClearsWrapPending()
    {
        // Arrange: Set up wrap pending state
        _terminal.Write("\x1b[1;80H"); // Move to right edge
        _terminal.Write("X"); // This should set wrap pending

        // Act: Move forward tab (should clear wrap pending)
        _terminal.Write("\x1b[I");

        // Assert: Write another character - it should not wrap
        int initialRow = _terminal.Cursor.Row;
        _terminal.Write("Y");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(initialRow)); // Should not have wrapped
    }

    [Test]
    public void CursorBackwardTab_MovesToPreviousTabStop()
    {
        // Arrange: Start at column 25
        _terminal.Write("\x1b[1;26H"); // Move to row 1, col 26 (1-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(25));

        // Act: Move backward 1 tab stop (CSI Z)
        _terminal.Write("\x1b[Z");

        // Assert: Should be at column 24 (previous tab stop)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(24));
    }

    [Test]
    public void CursorBackwardTab_WithCount_MovesMultipleTabStops()
    {
        // Arrange: Start at column 25
        _terminal.Write("\x1b[1;26H"); // Move to row 1, col 26 (1-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(25));

        // Act: Move backward 2 tab stops (CSI 2 Z)
        _terminal.Write("\x1b[2Z");

        // Assert: Should be at column 16 (2 tab stops back: 25 -> 24 -> 16)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(16));
    }

    [Test]
    public void CursorBackwardTab_FromLeftEdge_StaysAtLeftEdge()
    {
        // Arrange: Start at left edge (column 0)
        _terminal.Write("\x1b[H"); // Move to home position
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));

        // Act: Try to move backward 1 tab stop
        _terminal.Write("\x1b[Z");

        // Assert: Should stay at left edge (no tab stops before)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));
    }

    [Test]
    public void CursorBackwardTab_WithZeroParameter_UsesDefaultCount()
    {
        // Arrange: Start at column 16
        _terminal.Write("\x1b[1;17H"); // Move to row 1, col 17 (1-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(16));

        // Act: Move backward with zero parameter (should default to 1)
        _terminal.Write("\x1b[0Z");

        // Assert: Should move to previous tab stop
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(8));
    }

    [Test]
    public void CursorBackwardTab_ClearsWrapPending()
    {
        // Arrange: Set up wrap pending state
        _terminal.Write("\x1b[1;80H"); // Move to right edge
        _terminal.Write("X"); // This should set wrap pending

        // Act: Move backward tab (should clear wrap pending)
        _terminal.Write("\x1b[Z");

        // Assert: Write another character - it should not wrap
        int initialRow = _terminal.Cursor.Row;
        _terminal.Write("Y");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(initialRow)); // Should not have wrapped
    }

    [Test]
    public void TabClear_AtCursor_ClearsTabStopAtCurrentPosition()
    {
        // Arrange: Move to column 8 (default tab stop) and verify it exists
        _terminal.Write("\x1b[1;9H"); // Move to row 1, col 9 (1-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(8));

        // Act: Clear tab stop at current position (CSI g)
        _terminal.Write("\x1b[g");

        // Assert: Tab from origin should skip the cleared stop and go to column 16
        _terminal.Write("\x1b[H"); // Move to home
        _terminal.Write("\t"); // Regular tab character
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(16)); // Should skip cleared stop at 8
    }

    [Test]
    public void TabClear_WithZeroParameter_ClearsTabStopAtCurrentPosition()
    {
        // Arrange: Move to column 16 (default tab stop)
        _terminal.Write("\x1b[1;17H"); // Move to row 1, col 17 (1-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(16));

        // Act: Clear tab stop with explicit mode 0 (CSI 0 g)
        _terminal.Write("\x1b[0g");

        // Assert: Tab from column 8 should skip the cleared stop and go to column 24
        _terminal.Write("\x1b[1;9H"); // Move to column 8
        _terminal.Write("\t"); // Regular tab character
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(24)); // Should skip cleared stop at 16
    }

    [Test]
    public void TabClear_AllTabStops_ClearsAllTabStops()
    {
        // Arrange: Start at origin
        _terminal.Write("\x1b[H");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));

        // Act: Clear all tab stops (CSI 3 g)
        _terminal.Write("\x1b[3g");

        // Assert: Tab should go to right edge since no tab stops exist
        _terminal.Write("\t"); // Regular tab character
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(79)); // Should go to right edge
    }

    [Test]
    public void TabClear_AllTabStops_AffectsForwardTabMovement()
    {
        // Arrange: Clear all tab stops
        _terminal.Write("\x1b[3g");
        _terminal.Write("\x1b[H"); // Move to home

        // Act: Try to move forward tab (no tab stops exist)
        _terminal.Write("\x1b[I");

        // Assert: Should go to right edge
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(79));
    }

    [Test]
    public void TabClear_AllTabStops_AffectsBackwardTabMovement()
    {
        // Arrange: Clear all tab stops and move to middle
        _terminal.Write("\x1b[3g");
        _terminal.Write("\x1b[1;40H"); // Move to column 40

        // Act: Try to move backward tab (no tab stops exist)
        _terminal.Write("\x1b[Z");

        // Assert: Should go to left edge
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));
    }

    [Test]
    public void TabStops_WorkWithCustomTabStops()
    {
        // Arrange: Clear all tab stops and set custom ones
        _terminal.Write("\x1b[3g"); // Clear all
        _terminal.Write("\x1b[1;11H"); // Move to column 10
        _terminal.Write("\x1bH"); // Set tab stop (ESC H)
        _terminal.Write("\x1b[1;21H"); // Move to column 20
        _terminal.Write("\x1bH"); // Set tab stop (ESC H)

        // Act: Test forward tab from origin
        _terminal.Write("\x1b[H"); // Move to home
        _terminal.Write("\x1b[I"); // Forward tab

        // Assert: Should go to first custom tab stop (column 10)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(10));

        // Act: Move forward tab again
        _terminal.Write("\x1b[I");

        // Assert: Should go to second custom tab stop (column 20)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(20));
    }

    [Test]
    public void TabStops_BackwardTabWithCustomTabStops()
    {
        // Arrange: Clear all tab stops and set custom ones
        _terminal.Write("\x1b[3g"); // Clear all
        _terminal.Write("\x1b[1;11H"); // Move to column 10
        _terminal.Write("\x1bH"); // Set tab stop (ESC H)
        _terminal.Write("\x1b[1;21H"); // Move to column 20
        _terminal.Write("\x1bH"); // Set tab stop (ESC H)

        // Act: Test backward tab from column 25
        _terminal.Write("\x1b[1;26H"); // Move to column 25
        _terminal.Write("\x1b[Z"); // Backward tab

        // Assert: Should go to previous custom tab stop (column 20)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(20));

        // Act: Move backward tab again
        _terminal.Write("\x1b[Z");

        // Assert: Should go to first custom tab stop (column 10)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(10));
    }

    [Test]
    public void TabStops_IntegrationWithRegularTabCharacter()
    {
        // Arrange: Set up custom tab stops
        _terminal.Write("\x1b[3g"); // Clear all
        _terminal.Write("\x1b[1;6H"); // Move to column 5
        _terminal.Write("\x1bH"); // Set tab stop (ESC H)
        _terminal.Write("\x1b[1;16H"); // Move to column 15
        _terminal.Write("\x1bH"); // Set tab stop (ESC H)

        // Act: Test regular tab character
        _terminal.Write("\x1b[H"); // Move to home
        _terminal.Write("\t"); // Regular tab

        // Assert: Should go to first custom tab stop
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(5));

        // Act: Use CSI forward tab
        _terminal.Write("\x1b[I");

        // Assert: Should go to second custom tab stop
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(15));
    }
}
