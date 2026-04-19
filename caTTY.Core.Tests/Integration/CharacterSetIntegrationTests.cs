using System.Text;
using caTTY.Core.Terminal;
using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Integration;

/// <summary>
///     Integration tests for character set functionality.
///     Tests end-to-end character set designation, switching, and translation.
/// </summary>
[TestFixture]
public class CharacterSetIntegrationTests
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
    public void CharacterSetDesignation_ViaEscSequences_WorksCorrectly()
    {
        // Test ESC ( 0 - designate G0 as DEC Special Graphics
        _terminal.Write("\x1b(0");
        
        // Test ESC ) B - designate G1 as ASCII
        _terminal.Write("\x1b)B");
        
        // Test ESC * A - designate G2 as UK character set
        _terminal.Write("\x1b*A");
        
        // Test ESC + 0 - designate G3 as DEC Special Graphics
        _terminal.Write("\x1b+0");

        // Verify the character sets were designated correctly
        // Note: We can't directly access the character set manager from the test,
        // but we can verify the behavior by testing character translation
        
        // The terminal should start with G0 active, which is now DEC Special Graphics
        // Disable UTF-8 mode to enable character set translation
        _terminal.State.Utf8Mode = false;
        
        // Write a character that should be translated by DEC Special Graphics
        _terminal.Write("q"); // Should become horizontal line ─
        
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('─'), "DEC Special Graphics 'q' should be translated to horizontal line");
    }

    [Test]
    public void ShiftInOut_SwitchesCharacterSets_Correctly()
    {
        // Arrange - Set up G0 as ASCII and G1 as DEC Special Graphics
        _terminal.Write("\x1b(B"); // G0 = ASCII
        _terminal.Write("\x1b)0"); // G1 = DEC Special Graphics
        _terminal.State.Utf8Mode = false; // Enable character set translation

        // Test initial state (G0 active - ASCII)
        _terminal.Write("q");
        var cell1 = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell1.Character, Is.EqualTo('q'), "ASCII 'q' should remain unchanged");

        // Test Shift Out (SO) - switch to G1 (DEC Special Graphics)
        _terminal.Write("\x0e"); // SO
        _terminal.Write("q");
        var cell2 = _terminal.ScreenBuffer.GetCell(0, 1);
        Assert.That(cell2.Character, Is.EqualTo('─'), "DEC Special Graphics 'q' should be translated to horizontal line");

        // Test Shift In (SI) - switch back to G0 (ASCII)
        _terminal.Write("\x0f"); // SI
        _terminal.Write("q");
        var cell3 = _terminal.ScreenBuffer.GetCell(0, 2);
        Assert.That(cell3.Character, Is.EqualTo('q'), "ASCII 'q' should remain unchanged after SI");
    }

    [Test]
    public void DecSpecialGraphics_MapsLineDrawingCharacters_Correctly()
    {
        // Arrange - Set up DEC Special Graphics
        _terminal.Write("\x1b(0"); // G0 = DEC Special Graphics
        _terminal.State.Utf8Mode = false; // Enable character set translation

        // Test a representative set of line-drawing characters
        var testCases = new[]
        {
            ('q', '─'), // horizontal line
            ('x', '│'), // vertical line
            ('l', '┌'), // upper left corner
            ('k', '┐'), // upper right corner
            ('m', '└'), // lower left corner
            ('j', '┘'), // lower right corner
            ('n', '┼'), // crossing lines
            ('t', '├'), // left tee
            ('u', '┤'), // right tee
            ('v', '┴'), // bottom tee
            ('w', '┬'), // top tee
        };

        // Act & Assert
        for (int i = 0; i < testCases.Length; i++)
        {
            var (input, expected) = testCases[i];
            _terminal.Write(input.ToString());
            var cell = _terminal.ScreenBuffer.GetCell(0, i);
            Assert.That(cell.Character, Is.EqualTo(expected), 
                $"DEC Special Graphics '{input}' should be translated to '{expected}'");
        }
    }

    [Test]
    public void Utf8Mode_BypassesCharacterSetTranslation()
    {
        // Arrange - Set up DEC Special Graphics but keep UTF-8 mode enabled
        _terminal.Write("\x1b(0"); // G0 = DEC Special Graphics
        _terminal.State.Utf8Mode = true; // Keep UTF-8 mode enabled

        // Act - Write characters that would be translated in non-UTF-8 mode
        _terminal.Write("qx");

        // Assert - Characters should not be translated
        var cell1 = _terminal.ScreenBuffer.GetCell(0, 0);
        var cell2 = _terminal.ScreenBuffer.GetCell(0, 1);
        Assert.That(cell1.Character, Is.EqualTo('q'), "UTF-8 mode should bypass character set translation");
        Assert.That(cell2.Character, Is.EqualTo('x'), "UTF-8 mode should bypass character set translation");
    }

    [Test]
    public void CharacterSetQuery_ReturnsCorrectResponse()
    {
        // Arrange - Set up a specific character set
        _terminal.Write("\x1b(0"); // G0 = DEC Special Graphics
        _terminal.State.Utf8Mode = false;

        string? response = null;
        _terminal.ResponseEmitted += (sender, args) => response = Encoding.UTF8.GetString(args.ResponseData.Span);

        // Act - Send character set query
        _terminal.Write("\x1b[?26n");

        // Assert - Should respond with current character set
        Assert.That(response, Is.Not.Null, "Character set query should generate a response");
        Assert.That(response, Is.EqualTo("\x1b[?26;0\x1b\\"), "Should respond with DEC Special Graphics charset");
    }

    [Test]
    public void CharacterSetQuery_InUtf8Mode_ReturnsUtf8()
    {
        // Arrange - Keep UTF-8 mode enabled
        _terminal.State.Utf8Mode = true;

        string? response = null;
        _terminal.ResponseEmitted += (sender, args) => response = Encoding.UTF8.GetString(args.ResponseData.Span);

        // Act - Send character set query
        _terminal.Write("\x1b[?26n");

        // Assert - Should respond with UTF-8
        Assert.That(response, Is.Not.Null, "Character set query should generate a response");
        Assert.That(response, Is.EqualTo("\x1b[?26;utf-8\x1b\\"), "Should respond with UTF-8 in UTF-8 mode");
    }

    [Test]
    public void ComplexCharacterSetSequence_WorksCorrectly()
    {
        // Arrange - Complex sequence similar to what vim might use
        _terminal.State.Utf8Mode = false;

        // Act - Simulate a complex character set switching sequence
        _terminal.Write("\x1b(B");    // G0 = ASCII
        _terminal.Write("\x1b)0");    // G1 = DEC Special Graphics
        _terminal.Write("A");         // Write 'A' in ASCII
        _terminal.Write("\x0e");      // SO - switch to G1 (DEC Special Graphics)
        _terminal.Write("lqk");       // Write box drawing: ┌─┐
        _terminal.Write("\x0f");      // SI - switch back to G0 (ASCII)
        _terminal.Write("B");         // Write 'B' in ASCII

        // Assert - Verify the sequence was processed correctly
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('A'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 1).Character, Is.EqualTo('┌'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 2).Character, Is.EqualTo('─'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 3).Character, Is.EqualTo('┐'));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 4).Character, Is.EqualTo('B'));
    }
}