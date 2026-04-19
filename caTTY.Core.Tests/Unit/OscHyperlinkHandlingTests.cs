using caTTY.Core.Terminal;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit;

/// <summary>
///     Tests for OSC 8 hyperlink sequence handling in the terminal emulator.
///     Validates hyperlink URL association with character ranges and state management.
/// </summary>
[TestFixture]
[Category("Unit")]
public class OscHyperlinkHandlingTests
{
    private TerminalEmulator _terminal = null!;

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

    [Test]
    public void HandleHyperlink_WithUrl_SetsHyperlinkState()
    {
        // Act
        _terminal.HandleHyperlink("https://example.com");

        // Assert
        Assert.That(_terminal.AttributeManager.CurrentHyperlinkUrl, Is.EqualTo("https://example.com"));
        Assert.That(_terminal.State.CurrentHyperlinkUrl, Is.EqualTo("https://example.com"));
    }

    [Test]
    public void HandleHyperlink_WithEmptyUrl_ClearsHyperlinkState()
    {
        // Arrange - Set initial hyperlink
        _terminal.HandleHyperlink("https://example.com");
        Assert.That(_terminal.AttributeManager.CurrentHyperlinkUrl, Is.EqualTo("https://example.com"));

        // Act - Clear hyperlink
        _terminal.HandleHyperlink("");

        // Assert
        Assert.That(_terminal.AttributeManager.CurrentHyperlinkUrl, Is.Null);
        Assert.That(_terminal.State.CurrentHyperlinkUrl, Is.Null);
    }

    [Test]
    public void HandleHyperlink_WithNullUrl_ClearsHyperlinkState()
    {
        // Arrange - Set initial hyperlink
        _terminal.HandleHyperlink("https://example.com");
        Assert.That(_terminal.AttributeManager.CurrentHyperlinkUrl, Is.EqualTo("https://example.com"));

        // Act - Clear hyperlink with null
        _terminal.HandleHyperlink(null!);

        // Assert
        Assert.That(_terminal.AttributeManager.CurrentHyperlinkUrl, Is.Null);
        Assert.That(_terminal.State.CurrentHyperlinkUrl, Is.Null);
    }

    [Test]
    public void WriteCharacterAtCursor_WithHyperlinkActive_AssociatesUrlWithCell()
    {
        // Arrange - Set hyperlink URL
        _terminal.HandleHyperlink("https://example.com");

        // Act - Write a character
        _terminal.WriteCharacterAtCursor('A');

        // Assert - Check that the cell has the hyperlink URL
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('A'));
        Assert.That(cell.HyperlinkUrl, Is.EqualTo("https://example.com"));
    }

    [Test]
    public void WriteCharacterAtCursor_WithoutHyperlink_CellHasNoUrl()
    {
        // Act - Write a character without setting hyperlink
        _terminal.WriteCharacterAtCursor('A');

        // Assert - Check that the cell has no hyperlink URL
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('A'));
        Assert.That(cell.HyperlinkUrl, Is.Null);
    }

    [Test]
    public void WriteCharacterAtCursor_HyperlinkThenClear_OnlyFirstCharacterHasUrl()
    {
        // Arrange - Set hyperlink URL
        _terminal.HandleHyperlink("https://example.com");

        // Act - Write first character with hyperlink
        _terminal.WriteCharacterAtCursor('A');

        // Clear hyperlink
        _terminal.HandleHyperlink("");

        // Write second character without hyperlink
        _terminal.WriteCharacterAtCursor('B');

        // Assert - Check first cell has URL, second doesn't
        var cell1 = _terminal.ScreenBuffer.GetCell(0, 0);
        var cell2 = _terminal.ScreenBuffer.GetCell(0, 1);
        
        Assert.That(cell1.Character, Is.EqualTo('A'));
        Assert.That(cell1.HyperlinkUrl, Is.EqualTo("https://example.com"));
        
        Assert.That(cell2.Character, Is.EqualTo('B'));
        Assert.That(cell2.HyperlinkUrl, Is.Null);
    }

    [Test]
    public void OscSequence_Hyperlink_ProcessedCorrectly()
    {
        // Act - Send OSC 8 hyperlink sequence
        _terminal.Write("\x1b]8;;https://example.com\x07Hello\x1b]8;;\x07World");

        // Assert - Check hyperlink state and characters
        var cell1 = _terminal.ScreenBuffer.GetCell(0, 0); // 'H'
        var cell2 = _terminal.ScreenBuffer.GetCell(0, 4); // 'o'
        var cell3 = _terminal.ScreenBuffer.GetCell(0, 5); // 'W'
        var cell4 = _terminal.ScreenBuffer.GetCell(0, 9); // 'd'

        // First part should have hyperlink
        Assert.That(cell1.Character, Is.EqualTo('H'));
        Assert.That(cell1.HyperlinkUrl, Is.EqualTo("https://example.com"));
        
        Assert.That(cell2.Character, Is.EqualTo('o'));
        Assert.That(cell2.HyperlinkUrl, Is.EqualTo("https://example.com"));

        // Second part should not have hyperlink
        Assert.That(cell3.Character, Is.EqualTo('W'));
        Assert.That(cell3.HyperlinkUrl, Is.Null);
        
        Assert.That(cell4.Character, Is.EqualTo('d'));
        Assert.That(cell4.HyperlinkUrl, Is.Null);
    }

    [Test]
    public void OscSequence_HyperlinkWithParameters_ExtractsUrlCorrectly()
    {
        // Act - Send OSC 8 hyperlink sequence with parameters
        _terminal.Write("\x1b]8;id=link1;https://example.com/page\x07Test\x1b]8;;\x07");

        // Assert - Check hyperlink URL extraction
        var cell = _terminal.ScreenBuffer.GetCell(0, 0); // 'T'
        Assert.That(cell.Character, Is.EqualTo('T'));
        Assert.That(cell.HyperlinkUrl, Is.EqualTo("https://example.com/page"));
    }

    [Test]
    public void AttributeManager_ResetAttributes_ClearsHyperlinkUrl()
    {
        // Arrange - Set hyperlink URL
        _terminal.HandleHyperlink("https://example.com");
        Assert.That(_terminal.AttributeManager.CurrentHyperlinkUrl, Is.EqualTo("https://example.com"));

        // Act - Reset attributes
        _terminal.AttributeManager.ResetAttributes();

        // Assert - Hyperlink URL should be cleared
        Assert.That(_terminal.AttributeManager.CurrentHyperlinkUrl, Is.Null);
    }
}