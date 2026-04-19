using NUnit.Framework;
using caTTY.Core.Terminal;
using caTTY.Core.Types;

namespace caTTY.Core.Tests.Integration;

/// <summary>
///     Integration tests for SGR sequence handling in the terminal emulator.
///     Tests the complete flow from SGR sequences to character attribute application.
/// </summary>
[TestFixture]
public class SgrIntegrationTests
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
    public void SgrReset_ShouldResetAttributesToDefault()
    {
        // Arrange: Set some attributes first
        _terminal.Write("\x1b[1;31m"); // Bold red
        
        // Act: Reset with SGR 0
        _terminal.Write("\x1b[0m");
        
        // Assert: Attributes should be reset to default
        var attributes = _terminal.AttributeManager.CurrentAttributes;
        Assert.That(attributes.Bold, Is.False, "Bold should be reset");
        Assert.That(attributes.ForegroundColor, Is.Null, "Foreground color should be reset");
        Assert.That(attributes, Is.EqualTo(SgrAttributes.Default), "All attributes should be default");
    }

    [Test]
    public void SgrBold_ShouldSetBoldAttribute()
    {
        // Act: Set bold
        _terminal.Write("\x1b[1m");
        
        // Assert: Bold should be set
        var attributes = _terminal.AttributeManager.CurrentAttributes;
        Assert.That(attributes.Bold, Is.True, "Bold should be set");
    }

    [Test]
    public void SgrForegroundColor_ShouldSetForegroundColor()
    {
        // Act: Set red foreground
        _terminal.Write("\x1b[31m");
        
        // Assert: Foreground color should be red
        var attributes = _terminal.AttributeManager.CurrentAttributes;
        Assert.That(attributes.ForegroundColor, Is.Not.Null, "Foreground color should be set");
        Assert.That(attributes.ForegroundColor!.Value.Type, Is.EqualTo(ColorType.Named), "Should be named color");
        Assert.That(attributes.ForegroundColor!.Value.NamedColor, Is.EqualTo(NamedColor.Red), "Should be red");
    }

    [Test]
    public void SgrBackgroundColor_ShouldSetBackgroundColor()
    {
        // Act: Set blue background
        _terminal.Write("\x1b[44m");
        
        // Assert: Background color should be blue
        var attributes = _terminal.AttributeManager.CurrentAttributes;
        Assert.That(attributes.BackgroundColor, Is.Not.Null, "Background color should be set");
        Assert.That(attributes.BackgroundColor!.Value.Type, Is.EqualTo(ColorType.Named), "Should be named color");
        Assert.That(attributes.BackgroundColor!.Value.NamedColor, Is.EqualTo(NamedColor.Blue), "Should be blue");
    }

    [Test]
    public void SgrMultipleAttributes_ShouldSetAllAttributes()
    {
        // Act: Set bold, italic, and red foreground
        _terminal.Write("\x1b[1;3;31m");
        
        // Assert: All attributes should be set
        var attributes = _terminal.AttributeManager.CurrentAttributes;
        Assert.That(attributes.Bold, Is.True, "Bold should be set");
        Assert.That(attributes.Italic, Is.True, "Italic should be set");
        Assert.That(attributes.ForegroundColor, Is.Not.Null, "Foreground color should be set");
        Assert.That(attributes.ForegroundColor!.Value.NamedColor, Is.EqualTo(NamedColor.Red), "Should be red");
    }

    [Test]
    public void SgrAttributesAppliedToCharacters_ShouldUseCurrentAttributes()
    {
        // Arrange: Set bold and red foreground
        _terminal.Write("\x1b[1;31m");
        
        // Act: Write a character
        _terminal.Write("A");
        
        // Assert: Character should have the SGR attributes
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('A'), "Character should be 'A'");
        Assert.That(cell.Attributes.Bold, Is.True, "Character should be bold");
        Assert.That(cell.Attributes.ForegroundColor, Is.Not.Null, "Character should have foreground color");
        Assert.That(cell.Attributes.ForegroundColor!.Value.NamedColor, Is.EqualTo(NamedColor.Red), "Character should be red");
    }

    [Test]
    public void SgrStateSync_ShouldSyncWithTerminalState()
    {
        // Act: Set some attributes
        _terminal.Write("\x1b[1;4;32m"); // Bold, underline, green
        
        // Assert: Terminal state should be synced
        var stateAttributes = _terminal.State.CurrentSgrState;
        var managerAttributes = _terminal.AttributeManager.CurrentAttributes;
        
        Assert.That(stateAttributes, Is.EqualTo(managerAttributes), "State and manager attributes should be synced");
        Assert.That(stateAttributes.Bold, Is.True, "State should show bold");
        Assert.That(stateAttributes.Underline, Is.True, "State should show underline");
        Assert.That(stateAttributes.ForegroundColor!.Value.NamedColor, Is.EqualTo(NamedColor.Green), "State should show green");
    }

    [Test]
    public void Sgr256Color_ShouldSet256Color()
    {
        // Act: Set 256-color foreground (color index 196 = bright red)
        _terminal.Write("\x1b[38;5;196m");
        
        // Assert: Should set indexed color
        var attributes = _terminal.AttributeManager.CurrentAttributes;
        Assert.That(attributes.ForegroundColor, Is.Not.Null, "Foreground color should be set");
        Assert.That(attributes.ForegroundColor!.Value.Type, Is.EqualTo(ColorType.Indexed), "Should be indexed color");
        Assert.That(attributes.ForegroundColor!.Value.Index, Is.EqualTo(196), "Should be color index 196");
    }

    [Test]
    public void SgrRgbColor_ShouldSetRgbColor()
    {
        // Act: Set RGB color (255, 128, 64)
        _terminal.Write("\x1b[38;2;255;128;64m");
        
        // Assert: Should set RGB color
        var attributes = _terminal.AttributeManager.CurrentAttributes;
        Assert.That(attributes.ForegroundColor, Is.Not.Null, "Foreground color should be set");
        Assert.That(attributes.ForegroundColor!.Value.Type, Is.EqualTo(ColorType.Rgb), "Should be RGB color");
        Assert.That(attributes.ForegroundColor!.Value.Red, Is.EqualTo(255), "Red should be 255");
        Assert.That(attributes.ForegroundColor!.Value.Green, Is.EqualTo(128), "Green should be 128");
        Assert.That(attributes.ForegroundColor!.Value.Blue, Is.EqualTo(64), "Blue should be 64");
    }

    [Test]
    public void SgrCurlyUnderline_ShouldSetCurlyUnderlineStyle()
    {
        // Act: Set curly underline (CSI 4 : 3 m)
        _terminal.Write("\x1b[4:3m");
        
        // Assert: Should set curly underline
        var attributes = _terminal.AttributeManager.CurrentAttributes;
        Assert.That(attributes.Underline, Is.True, "Underline should be enabled");
        Assert.That(attributes.UnderlineStyle, Is.EqualTo(UnderlineStyle.Curly), "Should be curly underline");
    }

    [Test]
    public void SgrDottedUnderline_ShouldSetDottedUnderlineStyle()
    {
        // Act: Set dotted underline (CSI 4 : 4 m)
        _terminal.Write("\x1b[4:4m");
        
        // Assert: Should set dotted underline
        var attributes = _terminal.AttributeManager.CurrentAttributes;
        Assert.That(attributes.Underline, Is.True, "Underline should be enabled");
        Assert.That(attributes.UnderlineStyle, Is.EqualTo(UnderlineStyle.Dotted), "Should be dotted underline");
    }

    [Test]
    public void SgrDashedUnderline_ShouldSetDashedUnderlineStyle()
    {
        // Act: Set dashed underline (CSI 4 : 5 m)
        _terminal.Write("\x1b[4:5m");
        
        // Assert: Should set dashed underline
        var attributes = _terminal.AttributeManager.CurrentAttributes;
        Assert.That(attributes.Underline, Is.True, "Underline should be enabled");
        Assert.That(attributes.UnderlineStyle, Is.EqualTo(UnderlineStyle.Dashed), "Should be dashed underline");
    }

    [Test]
    public void SgrDoubleUnderline_ShouldSetDoubleUnderlineStyle()
    {
        // Act: Set double underline (CSI 4 : 2 m)
        _terminal.Write("\x1b[4:2m");
        
        // Assert: Should set double underline
        var attributes = _terminal.AttributeManager.CurrentAttributes;
        Assert.That(attributes.Underline, Is.True, "Underline should be enabled");
        Assert.That(attributes.UnderlineStyle, Is.EqualTo(UnderlineStyle.Double), "Should be double underline");
    }

    [Test]
    public void SgrDisableUnderline_ShouldDisableUnderline()
    {
        // Arrange: First enable underline
        _terminal.Write("\x1b[4m");
        Assert.That(_terminal.AttributeManager.CurrentAttributes.Underline, Is.True, "Underline should be enabled initially");
        
        // Act: Disable underline (CSI 4 : 0 m)
        _terminal.Write("\x1b[4:0m");
        
        // Assert: Should disable underline
        var attributes = _terminal.AttributeManager.CurrentAttributes;
        Assert.That(attributes.Underline, Is.False, "Underline should be disabled");
        Assert.That(attributes.UnderlineStyle, Is.EqualTo(UnderlineStyle.None), "Underline style should be None");
    }

    [Test]
    public void SgrWithCharacters_ShouldApplyToWrittenCharacters()
    {
        // Act: Set curly underline and write character
        _terminal.Write("\x1b[4:3m");
        _terminal.Write("A");
        
        // Assert: Character should have underline style
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('A'), "Character should be 'A'");
        Assert.That(cell.Attributes.Underline, Is.True, "Character should have underline");
        Assert.That(cell.Attributes.UnderlineStyle, Is.EqualTo(UnderlineStyle.Curly), "Character should have curly underline");
    }
}