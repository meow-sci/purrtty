using caTTY.Core.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Tests for cursor movement CSI sequences compatibility with TypeScript implementation.
///     Validates Requirements 3.3 (TypeScript compatibility for cursor operations).
/// </summary>
[TestFixture]
public class CursorMovementCompatibilityTests
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
    public void CursorMovement_MatchesTypeScriptBoundsChecking()
    {
        // Test that cursor movement bounds checking matches TypeScript implementation
        // TypeScript uses Math.max(0, cursor - count) for backward movement
        // and Math.min(maxBound, cursor + count) for forward movement

        // Test cursor up bounds checking
        _terminal.Write("\x1b[3;5H"); // Row 3, Col 5
        _terminal.Write("\x1b[10A"); // Move up 10 (should clamp to row 0)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(4)); // Column unchanged

        // Test cursor down bounds checking
        _terminal.Write("\x1b[20;5H"); // Row 20, Col 5
        _terminal.Write("\x1b[10B"); // Move down 10 (should clamp to row 23)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(23));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(4)); // Column unchanged

        // Test cursor left bounds checking
        _terminal.Write("\x1b[10;5H"); // Row 10, Col 5
        _terminal.Write("\x1b[10D"); // Move left 10 (should clamp to col 0)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(9)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));

        // Test cursor right bounds checking
        _terminal.Write("\x1b[10;75H"); // Row 10, Col 75
        _terminal.Write("\x1b[10C"); // Move right 10 (should clamp to col 79)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(9)); // Row unchanged
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(79));
    }

    [Test]
    public void CursorPosition_MatchesTypeScriptParameterHandling()
    {
        // TypeScript implementation uses Math.max(0, Math.min(maxBound, param - 1))
        // for converting 1-based parameters to 0-based coordinates

        // Test normal position setting
        _terminal.Write("\x1b[10;20H");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(9)); // 10-1 = 9
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(19)); // 20-1 = 19

        // Test position with out-of-bounds parameters
        _terminal.Write("\x1b[100;200H");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(23)); // Clamped to max row
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(79)); // Clamped to max col

        // Test position with zero parameters (should default to 1,1)
        _terminal.Write("\x1b[0;0H");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0)); // 1-1 = 0
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0)); // 1-1 = 0
    }

    [Test]
    public void CursorMovement_MatchesTypeScriptCountHandling()
    {
        // TypeScript uses Math.max(1, count) for movement counts
        // This ensures that zero or negative counts are treated as 1

        _terminal.Write("\x1b[10;10H"); // Start at row 10, col 10
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(9)); // Should be 9 (0-based)
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(9)); // Should be 9 (0-based)

        // Test zero count (should move by 1)
        _terminal.Write("\x1b[0A");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(8)); // Moved up by 1

        // Test missing count (should move by 1)
        _terminal.Write("\x1b[B");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(9)); // Moved down by 1

        // Test large count with bounds checking
        // Starting from col 9, moving right 50: 9 + 50 = 59
        // Since 59 < 79 (max col), it should stay at 59, not clamp to 79
        _terminal.Write("\x1b[50C");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(59)); // Should be 59, not clamped

        // Now test actual clamping by moving to exceed bounds
        _terminal.Write("\x1b[50C"); // Move right 50 more: 59 + 50 = 109, should clamp to 79
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(79)); // Now should be clamped

        _terminal.Write("\x1b[100D");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0)); // Clamped to left edge
    }

    [Test]
    public void CursorMovement_ClearsWrapPendingLikeTypeScript()
    {
        // TypeScript implementation clears wrap pending state on cursor movement
        // This test verifies that behavior matches

        // Set up wrap pending by writing to right edge
        _terminal.Write("\x1b[1;80H"); // Move to right edge
        _terminal.Write("X"); // This should set wrap pending

        // Any cursor movement should clear wrap pending
        _terminal.Write("\x1b[A"); // Move up

        // Write another character - it should not wrap to next line
        int currentRow = _terminal.Cursor.Row;
        _terminal.Write("Y");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(currentRow)); // Should not wrap

        // Test with other movement commands
        _terminal.Write("\x1b[1;80H"); // Reset to right edge
        _terminal.Write("X"); // Set wrap pending again
        _terminal.Write("\x1b[C"); // Move right (should clear wrap pending)

        currentRow = _terminal.Cursor.Row;
        _terminal.Write("Z");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(currentRow)); // Should not wrap
    }

    [Test]
    public void CursorPosition_HandlesPartialParametersLikeTypeScript()
    {
        // TypeScript handles missing parameters by defaulting to 1

        // Test with only row parameter
        _terminal.Write("\x1b[15H"); // Row 15, column defaults to 1
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(14)); // 15-1 = 14
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0)); // 1-1 = 0

        // Test with row and empty column
        _terminal.Write("\x1b[10;H"); // Row 10, column defaults to 1
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(9)); // 10-1 = 9
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0)); // 1-1 = 0

        // Test with empty row and column
        _terminal.Write("\x1b[;20H"); // Row defaults to 1, column 20
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0)); // 1-1 = 0
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(19)); // 20-1 = 19
    }

    [Test]
    public void CursorMovement_PreservesNonMovingCoordinate()
    {
        // Verify that cursor movement only affects the intended coordinate
        // and preserves the other coordinate exactly like TypeScript

        _terminal.Write("\x1b[15;25H"); // Start at row 15, col 25

        // Vertical movement should preserve column
        _terminal.Write("\x1b[5A");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(9)); // 15-1-5 = 9
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(24)); // Preserved

        _terminal.Write("\x1b[3B");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(12)); // 9+3 = 12
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(24)); // Preserved

        // Horizontal movement should preserve row
        _terminal.Write("\x1b[10C");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(12)); // Preserved
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(34)); // 24+10 = 34

        _terminal.Write("\x1b[5D");
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(12)); // Preserved
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(29)); // 34-5 = 29
    }
}
