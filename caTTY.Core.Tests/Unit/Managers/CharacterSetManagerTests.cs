using caTTY.Core.Managers;
using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Managers;

/// <summary>
///     Unit tests for CharacterSetManager.
///     Tests character set designation, switching, and DEC Special Graphics mapping.
/// </summary>
[TestFixture]
public class CharacterSetManagerTests
{
    private CharacterSetManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _manager = new CharacterSetManager();
    }

    [Test]
    public void Constructor_SetsDefaultState()
    {
        // Arrange & Act
        var manager = new CharacterSetManager();

        // Assert
        Assert.That(manager.CharacterSets.G0, Is.EqualTo("B"));
        Assert.That(manager.CharacterSets.G1, Is.EqualTo("B"));
        Assert.That(manager.CharacterSets.G2, Is.EqualTo("B"));
        Assert.That(manager.CharacterSets.G3, Is.EqualTo("B"));
        Assert.That(manager.CharacterSets.Current, Is.EqualTo(CharacterSetKey.G0));
        Assert.That(manager.Utf8Mode, Is.True);
    }

    [Test]
    public void Constructor_WithParameters_SetsProvidedState()
    {
        // Arrange
        var characterSets = new CharacterSetState
        {
            G0 = "0",
            G1 = "A",
            Current = CharacterSetKey.G1
        };

        // Act
        var manager = new CharacterSetManager(characterSets, false);

        // Assert
        Assert.That(manager.CharacterSets.G0, Is.EqualTo("0"));
        Assert.That(manager.CharacterSets.G1, Is.EqualTo("A"));
        Assert.That(manager.CharacterSets.Current, Is.EqualTo(CharacterSetKey.G1));
        Assert.That(manager.Utf8Mode, Is.False);
    }

    [Test]
    public void SetUtf8Mode_UpdatesMode()
    {
        // Act
        _manager.SetUtf8Mode(false);

        // Assert
        Assert.That(_manager.Utf8Mode, Is.False);

        // Act
        _manager.SetUtf8Mode(true);

        // Assert
        Assert.That(_manager.Utf8Mode, Is.True);
    }

    [Test]
    public void DesignateCharacterSet_UpdatesCorrectSlot()
    {
        // Act & Assert
        _manager.DesignateCharacterSet(CharacterSetKey.G0, "0");
        Assert.That(_manager.CharacterSets.G0, Is.EqualTo("0"));

        _manager.DesignateCharacterSet(CharacterSetKey.G1, "A");
        Assert.That(_manager.CharacterSets.G1, Is.EqualTo("A"));

        _manager.DesignateCharacterSet(CharacterSetKey.G2, "B");
        Assert.That(_manager.CharacterSets.G2, Is.EqualTo("B"));

        _manager.DesignateCharacterSet(CharacterSetKey.G3, "0");
        Assert.That(_manager.CharacterSets.G3, Is.EqualTo("0"));
    }

    [Test]
    public void GetCharacterSet_ReturnsCorrectCharset()
    {
        // Arrange
        _manager.DesignateCharacterSet(CharacterSetKey.G0, "0");
        _manager.DesignateCharacterSet(CharacterSetKey.G1, "A");

        // Act & Assert
        Assert.That(_manager.GetCharacterSet(CharacterSetKey.G0), Is.EqualTo("0"));
        Assert.That(_manager.GetCharacterSet(CharacterSetKey.G1), Is.EqualTo("A"));
        Assert.That(_manager.GetCharacterSet(CharacterSetKey.G2), Is.EqualTo("B"));
        Assert.That(_manager.GetCharacterSet(CharacterSetKey.G3), Is.EqualTo("B"));
    }

    [Test]
    public void GetCurrentCharacterSet_ReturnsActiveCharset()
    {
        // Arrange
        _manager.DesignateCharacterSet(CharacterSetKey.G0, "0");
        _manager.DesignateCharacterSet(CharacterSetKey.G1, "A");

        // Act & Assert - Default is G0
        Assert.That(_manager.GetCurrentCharacterSet(), Is.EqualTo("0"));

        // Switch to G1
        _manager.SwitchCharacterSet(CharacterSetKey.G1);
        Assert.That(_manager.GetCurrentCharacterSet(), Is.EqualTo("A"));
    }

    [Test]
    public void SwitchCharacterSet_UpdatesCurrentSlot()
    {
        // Act & Assert
        _manager.SwitchCharacterSet(CharacterSetKey.G1);
        Assert.That(_manager.CharacterSets.Current, Is.EqualTo(CharacterSetKey.G1));

        _manager.SwitchCharacterSet(CharacterSetKey.G2);
        Assert.That(_manager.CharacterSets.Current, Is.EqualTo(CharacterSetKey.G2));

        _manager.SwitchCharacterSet(CharacterSetKey.G0);
        Assert.That(_manager.CharacterSets.Current, Is.EqualTo(CharacterSetKey.G0));
    }

    [Test]
    public void TranslateCharacter_InUtf8Mode_ReturnsOriginalCharacter()
    {
        // Arrange
        _manager.SetUtf8Mode(true);
        _manager.DesignateCharacterSet(CharacterSetKey.G0, "0"); // DEC Special Graphics

        // Act & Assert
        Assert.That(_manager.TranslateCharacter('q'), Is.EqualTo("q"));
        Assert.That(_manager.TranslateCharacter('x'), Is.EqualTo("x"));
        Assert.That(_manager.TranslateCharacter('A'), Is.EqualTo("A"));
    }

    [Test]
    public void TranslateCharacter_WithDecSpecialGraphics_MapsCorrectly()
    {
        // Arrange
        _manager.SetUtf8Mode(false);
        _manager.DesignateCharacterSet(CharacterSetKey.G0, "0"); // DEC Special Graphics
        _manager.SwitchCharacterSet(CharacterSetKey.G0);

        // Act & Assert - Test a representative subset of mappings
        Assert.That(_manager.TranslateCharacter('q'), Is.EqualTo("─")); // horizontal line
        Assert.That(_manager.TranslateCharacter('x'), Is.EqualTo("│")); // vertical line
        Assert.That(_manager.TranslateCharacter('l'), Is.EqualTo("┌")); // upper left corner
        Assert.That(_manager.TranslateCharacter('k'), Is.EqualTo("┐")); // upper right corner
        Assert.That(_manager.TranslateCharacter('m'), Is.EqualTo("└")); // lower left corner
        Assert.That(_manager.TranslateCharacter('j'), Is.EqualTo("┘")); // lower right corner
        Assert.That(_manager.TranslateCharacter('n'), Is.EqualTo("┼")); // crossing lines
        Assert.That(_manager.TranslateCharacter('t'), Is.EqualTo("├")); // left tee
        Assert.That(_manager.TranslateCharacter('u'), Is.EqualTo("┤")); // right tee
        Assert.That(_manager.TranslateCharacter('v'), Is.EqualTo("┴")); // bottom tee
        Assert.That(_manager.TranslateCharacter('w'), Is.EqualTo("┬")); // top tee
    }

    [Test]
    public void TranslateCharacter_WithDecSpecialGraphics_UnmappedCharactersPassThrough()
    {
        // Arrange
        _manager.SetUtf8Mode(false);
        _manager.DesignateCharacterSet(CharacterSetKey.G0, "0"); // DEC Special Graphics
        _manager.SwitchCharacterSet(CharacterSetKey.G0);

        // Act & Assert - Characters not in the mapping should pass through unchanged
        Assert.That(_manager.TranslateCharacter('A'), Is.EqualTo("A"));
        Assert.That(_manager.TranslateCharacter('1'), Is.EqualTo("1"));
        Assert.That(_manager.TranslateCharacter(' '), Is.EqualTo(" "));
    }

    [Test]
    public void TranslateCharacter_WithAsciiCharset_ReturnsOriginalCharacter()
    {
        // Arrange
        _manager.SetUtf8Mode(false);
        _manager.DesignateCharacterSet(CharacterSetKey.G0, "B"); // ASCII
        _manager.SwitchCharacterSet(CharacterSetKey.G0);

        // Act & Assert
        Assert.That(_manager.TranslateCharacter('q'), Is.EqualTo("q"));
        Assert.That(_manager.TranslateCharacter('x'), Is.EqualTo("x"));
        Assert.That(_manager.TranslateCharacter('A'), Is.EqualTo("A"));
    }

    [Test]
    public void GenerateCharacterSetQueryResponse_InUtf8Mode_ReturnsUtf8()
    {
        // Arrange
        _manager.SetUtf8Mode(true);

        // Act
        string response = _manager.GenerateCharacterSetQueryResponse();

        // Assert
        Assert.That(response, Is.EqualTo("\x1b[?26;utf-8\x1b\\"));
    }

    [Test]
    public void GenerateCharacterSetQueryResponse_WithCurrentCharset_ReturnsCharset()
    {
        // Arrange
        _manager.SetUtf8Mode(false);
        _manager.DesignateCharacterSet(CharacterSetKey.G0, "0");
        _manager.SwitchCharacterSet(CharacterSetKey.G0);

        // Act
        string response = _manager.GenerateCharacterSetQueryResponse();

        // Assert
        Assert.That(response, Is.EqualTo("\x1b[?26;0\x1b\\"));
    }

    [Test]
    public void CharacterSetSwitching_WithShiftInOut_WorksCorrectly()
    {
        // Arrange
        _manager.DesignateCharacterSet(CharacterSetKey.G0, "B"); // ASCII
        _manager.DesignateCharacterSet(CharacterSetKey.G1, "0"); // DEC Special Graphics
        _manager.SetUtf8Mode(false);

        // Act & Assert - Start with G0 (ASCII)
        Assert.That(_manager.CharacterSets.Current, Is.EqualTo(CharacterSetKey.G0));
        Assert.That(_manager.TranslateCharacter('q'), Is.EqualTo("q"));

        // Shift Out to G1 (DEC Special Graphics)
        _manager.SwitchCharacterSet(CharacterSetKey.G1);
        Assert.That(_manager.CharacterSets.Current, Is.EqualTo(CharacterSetKey.G1));
        Assert.That(_manager.TranslateCharacter('q'), Is.EqualTo("─"));

        // Shift In back to G0 (ASCII)
        _manager.SwitchCharacterSet(CharacterSetKey.G0);
        Assert.That(_manager.CharacterSets.Current, Is.EqualTo(CharacterSetKey.G0));
        Assert.That(_manager.TranslateCharacter('q'), Is.EqualTo("q"));
    }
}