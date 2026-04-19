using caTTY.Core.Parsing;
using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Parsing;

/// <summary>
///     Unit tests for SGR state processor functionality.
/// </summary>
[TestFixture]
public class SgrStateProcessorTests
{
    [Test]
    public void ProcessSgrMessage_WithResetMessage_ResetsAllAttributes()
    {
        // Arrange
        var state = new SgrState
        {
            Bold = true,
            Italic = true,
            ForegroundColor = new Color(NamedColor.Red),
            BackgroundColor = new Color(NamedColor.Blue)
        };
        var message = new SgrMessage { Type = "sgr.reset", Implemented = true };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Bold, Is.False);
        Assert.That(state.Italic, Is.False);
        Assert.That(state.ForegroundColor, Is.Null);
        Assert.That(state.BackgroundColor, Is.Null);
        Assert.That(SgrStateProcessor.IsDefaultState(state), Is.True);
    }

    [Test]
    public void ProcessSgrMessage_WithBoldMessage_SetsBold()
    {
        // Arrange
        var state = new SgrState();
        var message = new SgrMessage { Type = "sgr.bold", Implemented = true };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Bold, Is.True);
        Assert.That(state.Italic, Is.False); // Other attributes unchanged
    }

    [Test]
    public void ProcessSgrMessage_WithNormalIntensityMessage_ClearsBoldAndFaint()
    {
        // Arrange
        var state = new SgrState { Bold = true, Faint = true };
        var message = new SgrMessage { Type = "sgr.normalIntensity", Implemented = true };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Bold, Is.False);
        Assert.That(state.Faint, Is.False);
    }

    [Test]
    public void ProcessSgrMessage_WithItalicMessage_SetsItalic()
    {
        // Arrange
        var state = new SgrState();
        var message = new SgrMessage { Type = "sgr.italic", Implemented = true };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Italic, Is.True);
    }

    [Test]
    public void ProcessSgrMessage_WithNotItalicMessage_ClearsItalic()
    {
        // Arrange
        var state = new SgrState { Italic = true };
        var message = new SgrMessage { Type = "sgr.notItalic", Implemented = true };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Italic, Is.False);
    }

    [Test]
    public void ProcessSgrMessage_WithUnderlineMessage_SetsUnderline()
    {
        // Arrange
        var state = new SgrState();
        var message = new SgrMessage { Type = "sgr.underline", Implemented = true };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Underline, Is.True);
        Assert.That(state.UnderlineStyle, Is.EqualTo(UnderlineStyle.Single));
    }

    [Test]
    public void ProcessSgrMessage_WithUnderlineMessageAndStyle_SetsUnderlineWithStyle()
    {
        // Arrange
        var state = new SgrState();
        var message = new SgrMessage 
        { 
            Type = "sgr.underline", 
            Implemented = true, 
            Data = UnderlineStyle.Double 
        };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Underline, Is.True);
        Assert.That(state.UnderlineStyle, Is.EqualTo(UnderlineStyle.Double));
    }

    [Test]
    public void ProcessSgrMessage_WithNotUnderlinedMessage_ClearsUnderline()
    {
        // Arrange
        var state = new SgrState { Underline = true, UnderlineStyle = UnderlineStyle.Single };
        var message = new SgrMessage { Type = "sgr.notUnderlined", Implemented = true };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Underline, Is.False);
        Assert.That(state.UnderlineStyle, Is.Null);
    }

    [Test]
    public void ProcessSgrMessage_WithInverseMessage_SetsInverse()
    {
        // Arrange
        var state = new SgrState();
        var message = new SgrMessage { Type = "sgr.inverse", Implemented = true };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Inverse, Is.True);
    }

    [Test]
    public void ProcessSgrMessage_WithNotInverseMessage_ClearsInverse()
    {
        // Arrange
        var state = new SgrState { Inverse = true };
        var message = new SgrMessage { Type = "sgr.notInverse", Implemented = true };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Inverse, Is.False);
    }

    [Test]
    public void ProcessSgrMessage_WithForegroundColorMessage_SetsForegroundColor()
    {
        // Arrange
        var state = new SgrState();
        var redColor = new Color(NamedColor.Red);
        var message = new SgrMessage { Type = "sgr.foregroundColor", Implemented = true, Data = redColor };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.ForegroundColor, Is.EqualTo(redColor));
    }

    [Test]
    public void ProcessSgrMessage_WithDefaultForegroundMessage_ClearsForegroundColor()
    {
        // Arrange
        var state = new SgrState { ForegroundColor = new Color(NamedColor.Red) };
        var message = new SgrMessage { Type = "sgr.defaultForeground", Implemented = true };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.ForegroundColor, Is.Null);
    }

    [Test]
    public void ProcessSgrMessage_WithBackgroundColorMessage_SetsBackgroundColor()
    {
        // Arrange
        var state = new SgrState();
        var blueColor = new Color(NamedColor.Blue);
        var message = new SgrMessage { Type = "sgr.backgroundColor", Implemented = true, Data = blueColor };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.BackgroundColor, Is.EqualTo(blueColor));
    }

    [Test]
    public void ProcessSgrMessage_WithDefaultBackgroundMessage_ClearsBackgroundColor()
    {
        // Arrange
        var state = new SgrState { BackgroundColor = new Color(NamedColor.Blue) };
        var message = new SgrMessage { Type = "sgr.defaultBackground", Implemented = true };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.BackgroundColor, Is.Null);
    }

    [Test]
    public void ProcessSgrMessage_WithEnhancedModeUnderline_SetsCorrectUnderlineStyle()
    {
        // Arrange
        var state = new SgrState();
        var enhancedParams = new[] { 4, 3 }; // Curly underline
        var message = new SgrMessage { Type = "sgr.enhancedMode", Implemented = true, Data = enhancedParams };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Underline, Is.True);
        Assert.That(state.UnderlineStyle, Is.EqualTo(UnderlineStyle.Curly));
    }

    [Test]
    public void ProcessSgrMessage_WithEnhancedModeDisableUnderline_ClearsUnderline()
    {
        // Arrange
        var state = new SgrState { Underline = true, UnderlineStyle = UnderlineStyle.Single };
        var enhancedParams = new[] { 4, 0 }; // No underline
        var message = new SgrMessage { Type = "sgr.enhancedMode", Implemented = true, Data = enhancedParams };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Underline, Is.False);
        Assert.That(state.UnderlineStyle, Is.Null);
    }

    [Test]
    public void ProcessSgrMessage_WithUnknownMessage_DoesNothing()
    {
        // Arrange
        var state = new SgrState { Bold = true, Italic = true };
        var originalState = new SgrState(state);
        var message = new SgrMessage { Type = "sgr.unknown", Implemented = false };

        // Act
        SgrStateProcessor.ProcessSgrMessage(state, message);

        // Assert
        Assert.That(state.Equals(originalState), Is.True);
    }

    [Test]
    public void ProcessSgrMessages_WithMultipleMessages_AppliesAllMessages()
    {
        // Arrange
        var state = new SgrState();
        var messages = new[]
        {
            new SgrMessage { Type = "sgr.bold", Implemented = true },
            new SgrMessage { Type = "sgr.italic", Implemented = true },
            new SgrMessage { Type = "sgr.foregroundColor", Implemented = true, Data = new Color(NamedColor.Red) }
        };

        // Act
        var result = SgrStateProcessor.ProcessSgrMessages(state, messages);

        // Assert
        Assert.That(result.Bold, Is.True);
        Assert.That(result.Italic, Is.True);
        Assert.That(result.ForegroundColor, Is.EqualTo(new Color(NamedColor.Red)));
        
        // Original state should be unchanged
        Assert.That(state.Bold, Is.False);
        Assert.That(state.Italic, Is.False);
        Assert.That(state.ForegroundColor, Is.Null);
    }

    [Test]
    public void ProcessSgrMessagesInPlace_WithMultipleMessages_ModifiesOriginalState()
    {
        // Arrange
        var state = new SgrState();
        var messages = new[]
        {
            new SgrMessage { Type = "sgr.bold", Implemented = true },
            new SgrMessage { Type = "sgr.italic", Implemented = true }
        };

        // Act
        SgrStateProcessor.ProcessSgrMessagesInPlace(state, messages);

        // Assert
        Assert.That(state.Bold, Is.True);
        Assert.That(state.Italic, Is.True);
    }

    [Test]
    public void ApplyInverseVideo_WithInverseTrue_SwapsColors()
    {
        // Arrange
        var state = new SgrState
        {
            Inverse = true,
            ForegroundColor = new Color(NamedColor.Red),
            BackgroundColor = new Color(NamedColor.Blue)
        };

        // Act
        var result = SgrStateProcessor.ApplyInverseVideo(state);

        // Assert
        Assert.That(result.ForegroundColor, Is.EqualTo(new Color(NamedColor.Blue)));
        Assert.That(result.BackgroundColor, Is.EqualTo(new Color(NamedColor.Red)));
    }

    [Test]
    public void ApplyInverseVideo_WithInverseFalse_DoesNotSwapColors()
    {
        // Arrange
        var state = new SgrState
        {
            Inverse = false,
            ForegroundColor = new Color(NamedColor.Red),
            BackgroundColor = new Color(NamedColor.Blue)
        };

        // Act
        var result = SgrStateProcessor.ApplyInverseVideo(state);

        // Assert
        Assert.That(result.ForegroundColor, Is.EqualTo(new Color(NamedColor.Red)));
        Assert.That(result.BackgroundColor, Is.EqualTo(new Color(NamedColor.Blue)));
    }

    [Test]
    public void ApplyInverseVideo_WithNoColorsSet_UsesDefaultColors()
    {
        // Arrange
        var state = new SgrState { Inverse = true };

        // Act
        var result = SgrStateProcessor.ApplyInverseVideo(state);

        // Assert
        Assert.That(result.ForegroundColor, Is.EqualTo(new Color(NamedColor.Black)));
        Assert.That(result.BackgroundColor, Is.EqualTo(new Color(NamedColor.White)));
    }

    [Test]
    public void AnsiCodeToNamedColor_WithValidStandardForegroundCode_ReturnsCorrectColor()
    {
        // Act & Assert
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(30), Is.EqualTo(NamedColor.Black));
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(31), Is.EqualTo(NamedColor.Red));
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(37), Is.EqualTo(NamedColor.White));
    }

    [Test]
    public void AnsiCodeToNamedColor_WithValidBrightForegroundCode_ReturnsCorrectColor()
    {
        // Act & Assert
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(90), Is.EqualTo(NamedColor.BrightBlack));
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(91), Is.EqualTo(NamedColor.BrightRed));
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(97), Is.EqualTo(NamedColor.BrightWhite));
    }

    [Test]
    public void AnsiCodeToNamedColor_WithValidBackgroundCode_ReturnsCorrectColor()
    {
        // Act & Assert
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(40), Is.EqualTo(NamedColor.Black));
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(47), Is.EqualTo(NamedColor.White));
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(100), Is.EqualTo(NamedColor.BrightBlack));
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(107), Is.EqualTo(NamedColor.BrightWhite));
    }

    [Test]
    public void AnsiCodeToNamedColor_WithInvalidCode_ReturnsNull()
    {
        // Act & Assert
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(29), Is.Null);
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(38), Is.Null);
        Assert.That(SgrStateProcessor.AnsiCodeToNamedColor(200), Is.Null);
    }

    [Test]
    public void IsDefaultState_WithDefaultState_ReturnsTrue()
    {
        // Arrange
        var state = new SgrState();

        // Act & Assert
        Assert.That(SgrStateProcessor.IsDefaultState(state), Is.True);
    }

    [Test]
    public void IsDefaultState_WithModifiedState_ReturnsFalse()
    {
        // Arrange
        var state = new SgrState { Bold = true };

        // Act & Assert
        Assert.That(SgrStateProcessor.IsDefaultState(state), Is.False);
    }
}