using System.Text;
using caTTY.Core.Terminal;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Integration;

/// <summary>
///     Integration tests for selective erase and character protection functionality.
///     Tests the complete flow from escape sequence parsing to terminal operations.
/// </summary>
[TestFixture]
public class SelectiveEraseIntegrationTests
{
    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(10, 3, NullLogger.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal.Dispose();
    }

    private TerminalEmulator _terminal = null!;

    private void SetupTerminal(int width, int height = 3)
    {
        _terminal?.Dispose();
        _terminal = TerminalEmulator.Create(width, height, NullLogger.Instance);
    }

    private string GetRowText(int row)
    {
        var sb = new StringBuilder();
        for (int col = 0; col < _terminal.Width; col++)
        {
            sb.Append(_terminal.ScreenBuffer.GetCell(row, col).Character);
        }

        return sb.ToString();
    }

    [Test]
    public void SelectiveEraseInLine_PreservesProtectedCells()
    {
        // Arrange - write text with protection in a 6-column terminal
        _terminal.Write(Encoding.UTF8.GetBytes("AA"));
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[46m")); // Cyan background
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[2\"q")); // Enable protection
        _terminal.Write(Encoding.UTF8.GetBytes("BB"));
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[0m")); // Reset SGR
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[0\"q")); // Disable protection
        _terminal.Write(Encoding.UTF8.GetBytes("CC"));

        // Move cursor to start and apply red background for erase
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[41m\r\x1b[?2K")); // Red background + selective erase entire line

        // Assert - only protected cells remain
        Assert.That(GetRowText(0), Is.EqualTo("  BB      "));

        // Verify protected cells retain their attributes
        Cell protectedCell = _terminal.ScreenBuffer.GetCell(0, 2);
        Assert.That(protectedCell.Character, Is.EqualTo('B'));
        Assert.That(protectedCell.IsProtected, Is.True);
        // Note: SGR parsing not implemented yet, so background color will be null

        // Verify erased cells have current SGR and are unprotected
        Cell erasedCell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(erasedCell.Character, Is.EqualTo(' '));
        Assert.That(erasedCell.IsProtected, Is.False);
        // Note: SGR parsing not implemented yet, so background color will be null
    }

    [Test]
    public void SelectiveEraseInLine_Mode0_FromCursorToEnd()
    {
        // Arrange
        _terminal.Write(Encoding.UTF8.GetBytes("ABCDE"));
        _terminal.Write(
            Encoding.UTF8.GetBytes("F\x1b[2\"qGH\x1b[0\"qIJ")); // F unprotected, GH protected, IJ unprotected

        // Move cursor to column 4 (after D)
        _terminal.Write(Encoding.UTF8.GetBytes("\r\x1b[4C\x1b[?K")); // Selective erase from cursor to end

        // Assert
        Assert.That(GetRowText(0), Is.EqualTo("ABCD  GH  "));

        // Verify protected cells are preserved
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 6).Character, Is.EqualTo('G'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 6).IsProtected, Is.True);
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 7).Character, Is.EqualTo('H'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 7).IsProtected, Is.True);
    }

    [Test]
    public void SelectiveEraseInLine_Mode1_FromStartToCursor()
    {
        // Arrange
        _terminal.Write(Encoding.UTF8.GetBytes("AB"));
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[2\"qCDE\x1b[0\"qFGHIJ")); // CDE protected, others unprotected

        // Move cursor to column 6 (after E)
        _terminal.Write(Encoding.UTF8.GetBytes("\r\x1b[6C\x1b[?1K")); // Selective erase from start to cursor

        // Assert
        Assert.That(GetRowText(0), Is.EqualTo("  CDE  HIJ"));

        // Verify protected cells are preserved
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 2).Character, Is.EqualTo('C'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 2).IsProtected, Is.True);
    }

    [Test]
    public void SelectiveEraseInDisplay_Mode2_EntireScreen()
    {
        // Use 6 columns like TypeScript display tests
        SetupTerminal(6);

        // Arrange - Row 0: 2 unprotected, 2 protected, 2 unprotected
        _terminal.Write(Encoding.UTF8.GetBytes("AA"));
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[2\"qBB\x1b[0\"qCC"));

        // Row 1: 1 protected, 5 unprotected
        _terminal.Write(Encoding.UTF8.GetBytes("\r\n\x1b[2\"qD\x1b[0\"qEEEEE"));

        // Apply red background and selective erase entire display
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[41m\x1b[?2J"));

        // Assert
        Assert.That(GetRowText(0), Is.EqualTo("  BB  "));
        Assert.That(GetRowText(1), Is.EqualTo("D     "));

        // Verify protected cells retain their protection
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 2).IsProtected, Is.True);
        Assert.That(_terminal.ScreenBuffer.GetCell(1, 0).IsProtected, Is.True);

        // Verify erased cells have current SGR and are unprotected
        Cell erasedCell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(erasedCell.IsProtected, Is.False);
        // Note: SGR parsing not implemented yet, so background color will be null
        // Assert.That(erasedCell.Attributes.BackgroundColor?.NamedColor, Is.EqualTo(Types.NamedColor.Red));
    }

    [Test]
    public void SelectiveEraseInDisplay_Mode0_FromCursorToEnd()
    {
        // Use 6 columns like TypeScript display tests
        SetupTerminal(6);

        // Arrange
        _terminal.Write(Encoding.UTF8.GetBytes("AAAAAA"));
        _terminal.Write(Encoding.UTF8.GetBytes("\r\n\x1b[2\"qBB\x1b[0\"qCCCC"));

        // Move cursor to row 0, col 3 (1-indexed: 1;4H)
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[1;4H\x1b[?J"));

        // Assert
        Assert.That(GetRowText(0), Is.EqualTo("AAA   "));
        Assert.That(GetRowText(1), Is.EqualTo("BB    ")); // Protected BB preserved
    }

    [Test]
    public void SelectiveEraseInDisplay_Mode1_FromStartToCursor()
    {
        // Use 6 columns like TypeScript display tests
        SetupTerminal(6);

        // Arrange
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[2\"qAA\x1b[0\"qBBBB"));
        _terminal.Write(Encoding.UTF8.GetBytes("\r\nCCCCCC"));

        // Move cursor to row 1, col 2 (1-indexed: 2;3H)
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[2;3H\x1b[?1J"));

        // Assert - Row 0 entirely affected, protected AA preserved
        Assert.That(GetRowText(0), Is.EqualTo("AA    "));
        // Row 1 from start to cursor (0..2) erased
        Assert.That(GetRowText(1), Is.EqualTo("   CCC"));
    }

    [Test]
    public void CharacterProtection_DECSCA_SetsProtectionState()
    {
        // Test CSI 0 " q (unprotected)
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[0\"q"));
        _terminal.Write(Encoding.UTF8.GetBytes("A"));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).IsProtected, Is.False);

        // Test CSI 2 " q (protected)
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[2\"q"));
        _terminal.Write(Encoding.UTF8.GetBytes("B"));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 1).IsProtected, Is.True);

        // Test CSI 1 " q (should default to unprotected)
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[1\"q"));
        _terminal.Write(Encoding.UTF8.GetBytes("C"));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 2).IsProtected, Is.False);
    }

    [Test]
    public void RegularErase_ErasesProtectedCells()
    {
        // Arrange - write protected characters
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[2\"qABC\x1b[0\"q"));

        // Act - regular erase (not selective)
        _terminal.Write(Encoding.UTF8.GetBytes("\x1b[2K"));

        // Assert - all cells erased, including protected ones
        Assert.That(GetRowText(0), Is.EqualTo("          ")); // 10 spaces
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo(' '));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 1).Character, Is.EqualTo(' '));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 2).Character, Is.EqualTo(' '));
    }
}
