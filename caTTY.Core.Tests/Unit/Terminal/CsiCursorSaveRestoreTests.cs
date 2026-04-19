using System.Text;
using caTTY.Core.Terminal;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

[TestFixture]
[Category("Unit")]
public class CsiCursorSaveRestoreTests
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
        _terminal?.Dispose();
    }

    [Test]
    public void AnsiSaveAndRestoreCursor_ShouldPreserveCursorPosition()
    {
        // Arrange: Move cursor to a specific position
        byte[] csiH = Encoding.ASCII.GetBytes("\x1b[5;10H"); // ESC[5;10H
        _terminal.Write(csiH.AsSpan());
        int initialRow = _terminal.Cursor.Row;
        int initialCol = _terminal.Cursor.Col;

        // Act: Save cursor (ANSI style), move to different position, then restore
        byte[] csiS = Encoding.ASCII.GetBytes("\x1b[s"); // CSI s
        _terminal.Write(csiS.AsSpan());
        byte[] csiH2 = Encoding.ASCII.GetBytes("\x1b[1;1H"); // ESC[1;1H
        _terminal.Write(csiH2.AsSpan());
        byte[] csiU = Encoding.ASCII.GetBytes("\x1b[u"); // CSI u
        _terminal.Write(csiU.AsSpan());

        // Assert: Cursor should be back to original position
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(initialRow), "Cursor row should be restored");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(initialCol), "Cursor column should be restored");
    }

    [Test]
    public void AnsiRestoreCursor_WithoutSave_ShouldNotCrash()
    {
        // Arrange: Start with cursor at origin
        int initialRow = _terminal.Cursor.Row;
        int initialCol = _terminal.Cursor.Col;

        // Act: Try to restore cursor without saving first (ANSI style)
        byte[] csiU = Encoding.ASCII.GetBytes("\x1b[u"); // CSI u
        _terminal.Write(csiU.AsSpan());

        // Assert: Should not crash and cursor should remain unchanged
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(initialRow), "Cursor row should be unchanged");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(initialCol), "Cursor column should be unchanged");
    }

    [Test]
    public void AnsiCursorSaveRestore_ShouldBeIndependentFromDecSaveRestore()
    {
        // Arrange: Move cursor to position (3, 7)
        byte[] csiH1 = Encoding.ASCII.GetBytes("\x1b[3;7H"); // ESC[3;7H
        _terminal.Write(csiH1.AsSpan());
        int position1Row = _terminal.Cursor.Row;
        int position1Col = _terminal.Cursor.Col;

        // Save using DEC style (ESC 7)
        byte[] esc7 = Encoding.ASCII.GetBytes("\x1b\x37"); // ESC 7
        _terminal.Write(esc7.AsSpan());

        // Move to position (8, 15)
        byte[] csiH2 = Encoding.ASCII.GetBytes("\x1b[8;15H"); // ESC[8;15H
        _terminal.Write(csiH2.AsSpan());
        int position2Row = _terminal.Cursor.Row;
        int position2Col = _terminal.Cursor.Col;

        // Save using ANSI style (CSI s)
        byte[] csiS = Encoding.ASCII.GetBytes("\x1b[s"); // CSI s
        _terminal.Write(csiS.AsSpan());

        // Move to position (12, 25)
        byte[] csiH3 = Encoding.ASCII.GetBytes("\x1b[12;25H"); // ESC[12;25H
        _terminal.Write(csiH3.AsSpan());

        // Act: Restore using ANSI style (CSI u) - should restore to position (8, 15)
        byte[] csiU = Encoding.ASCII.GetBytes("\x1b[u"); // CSI u
        _terminal.Write(csiU.AsSpan());

        // Assert: Should restore to ANSI saved position (8, 15)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(position2Row), "ANSI restore should restore to ANSI saved position");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(position2Col), "ANSI restore should restore to ANSI saved position");

        // Act: Restore using DEC style (ESC 8) - should restore to position (3, 7)
        byte[] esc8 = Encoding.ASCII.GetBytes("\x1b\x38"); // ESC 8
        _terminal.Write(esc8.AsSpan());

        // Assert: Should restore to DEC saved position (3, 7)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(position1Row), "DEC restore should restore to DEC saved position");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(position1Col), "DEC restore should restore to DEC saved position");
    }

    [Test]
    public void AnsiCursorSaveRestore_ShouldClampToBufferBounds()
    {
        // Arrange: Save cursor at a valid position
        byte[] csiH1 = Encoding.ASCII.GetBytes("\x1b[10;20H"); // ESC[10;20H
        _terminal.Write(csiH1.AsSpan());
        byte[] csiS = Encoding.ASCII.GetBytes("\x1b[s"); // CSI s
        _terminal.Write(csiS.AsSpan());

        // Resize terminal to smaller dimensions
        _terminal.Resize(15, 8); // Smaller than saved position

        // Act: Restore cursor
        byte[] csiU = Encoding.ASCII.GetBytes("\x1b[u"); // CSI u
        _terminal.Write(csiU.AsSpan());

        // Assert: Cursor should be clamped to buffer bounds
        Assert.That(_terminal.Cursor.Row, Is.LessThan(_terminal.Height), "Cursor row should be within buffer bounds");
        Assert.That(_terminal.Cursor.Col, Is.LessThan(_terminal.Width), "Cursor column should be within buffer bounds");
        Assert.That(_terminal.Cursor.Row, Is.GreaterThanOrEqualTo(0), "Cursor row should be non-negative");
        Assert.That(_terminal.Cursor.Col, Is.GreaterThanOrEqualTo(0), "Cursor column should be non-negative");
    }

    [Test]
    public void AnsiCursorSaveRestore_ShouldClearWrapPending()
    {
        // Arrange: Move cursor to right edge and trigger wrap pending
        byte[] csiH = Encoding.ASCII.GetBytes($"\x1b[1;{_terminal.Width}H"); // Move to right edge
        _terminal.Write(csiH.AsSpan());
        
        // Write a character to potentially set wrap pending
        _terminal.Write("X");
        
        // Save cursor position
        byte[] csiS = Encoding.ASCII.GetBytes("\x1b[s"); // CSI s
        _terminal.Write(csiS.AsSpan());

        // Move cursor elsewhere
        byte[] csiH2 = Encoding.ASCII.GetBytes("\x1b[5;5H"); // ESC[5;5H
        _terminal.Write(csiH2.AsSpan());

        // Act: Restore cursor
        byte[] csiU = Encoding.ASCII.GetBytes("\x1b[u"); // CSI u
        _terminal.Write(csiU.AsSpan());

        // Assert: Wrap pending should be cleared
        Assert.That(_terminal.State.WrapPending, Is.False, "Wrap pending should be cleared after cursor restore");
    }

    [Test]
    public void AnsiCursorSave_MultipleSaves_ShouldOverwritePrevious()
    {
        // Arrange: Move to first position and save
        byte[] csiH1 = Encoding.ASCII.GetBytes("\x1b[3;5H"); // ESC[3;5H
        _terminal.Write(csiH1.AsSpan());
        byte[] csiS1 = Encoding.ASCII.GetBytes("\x1b[s"); // CSI s
        _terminal.Write(csiS1.AsSpan());

        // Move to second position and save again
        byte[] csiH2 = Encoding.ASCII.GetBytes("\x1b[7;12H"); // ESC[7;12H
        _terminal.Write(csiH2.AsSpan());
        int expectedRow = _terminal.Cursor.Row;
        int expectedCol = _terminal.Cursor.Col;
        byte[] csiS2 = Encoding.ASCII.GetBytes("\x1b[s"); // CSI s
        _terminal.Write(csiS2.AsSpan());

        // Move to third position
        byte[] csiH3 = Encoding.ASCII.GetBytes("\x1b[15;25H"); // ESC[15;25H
        _terminal.Write(csiH3.AsSpan());

        // Act: Restore cursor
        byte[] csiU = Encoding.ASCII.GetBytes("\x1b[u"); // CSI u
        _terminal.Write(csiU.AsSpan());

        // Assert: Should restore to second saved position, not first
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(expectedRow), "Should restore to most recent saved position");
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(expectedCol), "Should restore to most recent saved position");
    }
}