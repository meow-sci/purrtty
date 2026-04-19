using NUnit.Framework;
using caTTY.Core.Terminal;
using caTTY.Core.Types;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Tests for ECH (Erase Character) sequence handling.
/// </summary>
[TestFixture]
[Category("Unit")]
public class EraseCharacterTest
{
    private TerminalEmulator _terminal = null!;

    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, 1000);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal.Dispose();
    }

    [Test]
    public void EraseCharacter_BasicErase_ShouldReplaceWithSpaces()
    {
        // Arrange: Write some text
        _terminal.Write("Hello World");
        
        // Move cursor to position 6 (after "Hello ")
        _terminal.Write("\x1b[1;7H");
        
        // Act: Erase 5 characters (should erase "World")
        _terminal.Write("\x1b[5X");
        
        // Assert: Check that "World" was replaced with spaces
        var row = _terminal.ScreenBuffer.GetRow(0);
        
        // "Hello " should remain
        Assert.That(row[0].Character, Is.EqualTo('H'));
        Assert.That(row[1].Character, Is.EqualTo('e'));
        Assert.That(row[2].Character, Is.EqualTo('l'));
        Assert.That(row[3].Character, Is.EqualTo('l'));
        Assert.That(row[4].Character, Is.EqualTo('o'));
        Assert.That(row[5].Character, Is.EqualTo(' '));
        
        // "World" should be replaced with spaces
        Assert.That(row[6].Character, Is.EqualTo(' '));
        Assert.That(row[7].Character, Is.EqualTo(' '));
        Assert.That(row[8].Character, Is.EqualTo(' '));
        Assert.That(row[9].Character, Is.EqualTo(' '));
        Assert.That(row[10].Character, Is.EqualTo(' '));
    }

    [Test]
    public void EraseCharacter_NvimScenario_ShouldClearCommandLine()
    {
        // Arrange: Simulate nvim command line with leftover text
        _terminal.Write("search text: ker");
        
        // Move cursor to beginning of line (like nvim does)
        _terminal.Write("\x1b[1;1H");
        
        // Write colon for command mode
        _terminal.Write(":");
        
        // Act: Erase 68 characters (like nvim does with CSI 68 X)
        _terminal.Write("\x1b[68X");
        
        // Assert: The line should have colon followed by spaces
        var row = _terminal.ScreenBuffer.GetRow(0);
        
        Assert.That(row[0].Character, Is.EqualTo(':'));
        
        // Next characters should be spaces (erased)
        for (int i = 1; i < 69 && i < _terminal.Width; i++)
        {
            Assert.That(row[i].Character, Is.EqualTo(' '), $"Character at position {i} should be space");
        }
    }

    [Test]
    public void EraseCharacter_WithSgrAttributes_ShouldUseCurrentAttributes()
    {
        // Arrange: Set red background
        _terminal.Write("\x1b[41m");
        _terminal.Write("test");
        
        // Move cursor back to beginning
        _terminal.Write("\x1b[1;1H");
        
        // Act: Erase 2 characters
        _terminal.Write("\x1b[2X");
        
        // Assert: Erased characters should have red background
        var row = _terminal.ScreenBuffer.GetRow(0);
        
        Assert.That(row[0].Character, Is.EqualTo(' '));
        Assert.That(row[1].Character, Is.EqualTo(' '));
        
        // Check that the erased characters have the red background
        // Note: SGR 41 (red background) creates a Named color, not Indexed
        Assert.That(row[0].Attributes.BackgroundColor?.Type, Is.EqualTo(ColorType.Named));
        Assert.That(row[0].Attributes.BackgroundColor?.NamedColor, Is.EqualTo(NamedColor.Red));
        Assert.That(row[1].Attributes.BackgroundColor?.Type, Is.EqualTo(ColorType.Named));
        Assert.That(row[1].Attributes.BackgroundColor?.NamedColor, Is.EqualTo(NamedColor.Red));
    }

    [Test]
    public void EraseCharacter_BeyondLineEnd_ShouldNotCrash()
    {
        // Arrange: Position cursor near end of line
        _terminal.Write("\x1b[1;75H");
        
        // Act: Try to erase more characters than available
        _terminal.Write("\x1b[100X");
        
        // Assert: Should not crash and should erase to end of line
        var row = _terminal.ScreenBuffer.GetRow(0);
        
        // Characters from position 74 to end should be spaces
        for (int i = 74; i < _terminal.Width; i++)
        {
            Assert.That(row[i].Character, Is.EqualTo(' '));
        }
    }

    [Test]
    public void EraseCharacter_DefaultParameter_ShouldEraseOneCharacter()
    {
        // Arrange: Write text
        _terminal.Write("ABC");
        _terminal.Write("\x1b[1;2H"); // Move to 'B'
        
        // Act: Erase with no parameter (should default to 1)
        _terminal.Write("\x1b[X");
        
        // Assert: Only 'B' should be erased
        var row = _terminal.ScreenBuffer.GetRow(0);
        
        Assert.That(row[0].Character, Is.EqualTo('A'));
        Assert.That(row[1].Character, Is.EqualTo(' ')); // 'B' erased
        Assert.That(row[2].Character, Is.EqualTo('C'));
    }
}