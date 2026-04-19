using caTTY.Core.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Tests for cursor movement CSI sequences.
///     Validates Requirements 11.1, 11.2, 11.3, 11.4, 11.5.
/// </summary>
[TestFixture]
public class CursorMovementTests
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
    public void CursorUp_MovesUpCorrectly()
    {
        // Arrange: Start at position (5, 10)
        _terminal.Write("\x1b[6;11H"); // Move to row 6, col 11 (1-based)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(5)); // 0-based
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(10)); // 0-based

        // Act: Move up 2 lines
        _terminal.Write("\x1b[2A");

        // Assert: Should be at row 3
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(3));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(10)); // Column unchanged
    }

    [Test]
    public void CursorUp_ClampsAtTopBoundary()
    {
        // Arrange: Start at row 1
        _terminal.Write("\x1b[2;5H"); // Move to row 2, col 5 (1-based)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(1)); // 0-based

        // Act: Try to move up 5 lines (should clamp to row 0)
        _terminal.Write("\x1b[5A");

        // Assert: Should be at row 0 (top boundary)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(4)); // Column unchanged
    }

    [Test]
    public void CursorDown_MovesDownCorrectly()
    {
        // Arrange: Start at position (5, 10)
        _terminal.Write("\x1b[6;11H"); // Move to row 6, col 11 (1-based)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(5)); // 0-based

        // Act: Move down 3 lines
        _terminal.Write("\x1b[3B");

        // Assert: Should be at row 8
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(8));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(10)); // Column unchanged
    }

    [Test]
    public void CursorDown_ClampsAtBottomBoundary()
    {
        // Arrange: Start at row 22 (near bottom of 24-row terminal)
        _terminal.Write("\x1b[23;5H"); // Move to row 23, col 5 (1-based)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(22)); // 0-based

        // Act: Try to move down 5 lines (should clamp to row 23)
        _terminal.Write("\x1b[5B");

        // Assert: Should be at row 23 (bottom boundary)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(23));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(4)); // Column unchanged
    }

    [Test]
    public void CursorForward_MovesRightCorrectly()
    {
        // Arrange: Start at position (5, 10)
        _terminal.Write("\x1b[6;11H"); // Move to row 6, col 11 (1-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(10)); // 0-based

        // Act: Move forward 5 columns
        _terminal.Write("\x1b[5C");

        // Assert: Should be at column 15
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(5)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(15));
    }

    [Test]
    public void CursorForward_ClampsAtRightBoundary()
    {
        // Arrange: Start at column 75 (near right edge of 80-column terminal)
        _terminal.Write("\x1b[6;76H"); // Move to row 6, col 76 (1-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(75)); // 0-based

        // Act: Try to move forward 10 columns (should clamp to col 79)
        _terminal.Write("\x1b[10C");

        // Assert: Should be at column 79 (right boundary)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(5)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(79));
    }

    [Test]
    public void CursorBackward_MovesLeftCorrectly()
    {
        // Arrange: Start at position (5, 20)
        _terminal.Write("\x1b[6;21H"); // Move to row 6, col 21 (1-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(20)); // 0-based

        // Act: Move backward 7 columns
        _terminal.Write("\x1b[7D");

        // Assert: Should be at column 13
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(5)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(13));
    }

    [Test]
    public void CursorBackward_ClampsAtLeftBoundary()
    {
        // Arrange: Start at column 3
        _terminal.Write("\x1b[6;4H"); // Move to row 6, col 4 (1-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(3)); // 0-based

        // Act: Try to move backward 10 columns (should clamp to col 0)
        _terminal.Write("\x1b[10D");

        // Assert: Should be at column 0 (left boundary)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(5)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));
    }

    [Test]
    public void CursorPosition_SetsAbsolutePosition()
    {
        // Arrange: Start at some position
        _terminal.Write("\x1b[10;20H"); // Move to row 10, col 20 (1-based)

        // Act: Set absolute position to row 15, col 35
        _terminal.Write("\x1b[15;35H");

        // Assert: Should be at the specified position
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(14)); // 0-based
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(34)); // 0-based
    }

    [Test]
    public void CursorPosition_ClampsToTerminalBounds()
    {
        // Act: Try to set position beyond terminal bounds
        _terminal.Write("\x1b[50;100H"); // Row 50, col 100 (both out of bounds)

        // Assert: Should be clamped to terminal bounds
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(23)); // Bottom row (0-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(79)); // Right column (0-based)
    }

    [Test]
    public void CursorPosition_DefaultsToOrigin()
    {
        // Arrange: Start at some position
        _terminal.Write("\x1b[10;20H");

        // Act: Set position with no parameters (should default to 1,1)
        _terminal.Write("\x1b[H");

        // Assert: Should be at origin
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0)); // 0-based
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0)); // 0-based
    }

    [Test]
    public void CursorMovement_ClearsWrapPendingState()
    {
        // Arrange: Set up wrap pending state by writing to right edge
        _terminal.Write("\x1b[1;80H"); // Move to row 1, col 80 (right edge)
        _terminal.Write("X"); // This should set wrap pending

        // Act: Move cursor (any movement should clear wrap pending)
        _terminal.Write("\x1b[A"); // Move up

        // Assert: Write another character - it should not wrap
        int initialRow = _terminal.Cursor.Row;
        _terminal.Write("Y");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(initialRow)); // Should not have wrapped
    }

    [Test]
    public void CursorMovement_WithZeroParameter_UsesDefaultCount()
    {
        // Arrange: Start at position (5, 10)
        _terminal.Write("\x1b[6;11H");

        // Act: Move with zero parameter (should default to 1)
        _terminal.Write("\x1b[0A"); // Move up 0 (should be treated as 1)

        // Assert: Should have moved up by 1
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(4)); // Moved up by 1
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(10)); // Column unchanged
    }

    [Test]
    public void CursorMovement_WithNoParameter_UsesDefaultCount()
    {
        // Arrange: Start at position (5, 10)
        _terminal.Write("\x1b[6;11H");

        // Act: Move with no parameter (should default to 1)
        _terminal.Write("\x1b[A"); // Move up (no parameter)

        // Assert: Should have moved up by 1
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(4)); // Moved up by 1
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(10)); // Column unchanged
    }
}
