using NUnit.Framework;
using caTTY.Core.Parsing;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging.Abstractions;

namespace caTTY.Core.Tests.Unit.Parsing;

/// <summary>
///     Unit tests for extended color parsing functionality in SGR sequences.
///     Tests the requirements for task 3.3: 256-color, RGB, colon-separated formats, and bright colors.
/// </summary>
[TestFixture]
public class SgrParserExtendedColorTests
{
    private SgrParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _parser = new SgrParser(NullLogger.Instance);
    }

    #region 256-Color Tests

    [Test]
    public void ParseSgrSequence_256ColorForeground_CreatesIndexedColorMessage()
    {
        // Arrange - ESC[38;5;196m (bright red in 256-color palette)
        var raw = "\x1b[38;5;196m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Type, Is.EqualTo("sgr"));
        Assert.That(result.Messages, Has.Length.EqualTo(1));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.foregroundColor"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        var color = (Color)result.Messages[0].Data!;
        Assert.That(color.Type, Is.EqualTo(ColorType.Indexed));
        Assert.That(color.Index, Is.EqualTo(196));
    }

    [Test]
    public void ParseSgrSequence_256ColorBackground_CreatesIndexedColorMessage()
    {
        // Arrange - ESC[48;5;21m (blue in 256-color palette)
        var raw = "\x1b[48;5;21m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Type, Is.EqualTo("sgr"));
        Assert.That(result.Messages, Has.Length.EqualTo(1));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.backgroundColor"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        var color = (Color)result.Messages[0].Data!;
        Assert.That(color.Type, Is.EqualTo(ColorType.Indexed));
        Assert.That(color.Index, Is.EqualTo(21));
    }

    [Test]
    public void ParseSgrSequence_256ColorUnderline_CreatesIndexedColorMessage()
    {
        // Arrange - ESC[58;5;128m (underline color in 256-color palette)
        var raw = "\x1b[58;5;128m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Type, Is.EqualTo("sgr"));
        Assert.That(result.Messages, Has.Length.EqualTo(1));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.underlineColor"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        var color = (Color)result.Messages[0].Data!;
        Assert.That(color.Type, Is.EqualTo(ColorType.Indexed));
        Assert.That(color.Index, Is.EqualTo(128));
    }

    [TestCase(0)]
    [TestCase(15)]
    [TestCase(255)]
    public void ParseSgrSequence_256ColorBoundaryValues_ParsesCorrectly(int colorIndex)
    {
        // Arrange
        var raw = $"\x1b[38;5;{colorIndex}m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Messages, Has.Length.EqualTo(1));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.foregroundColor"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        var color = (Color)result.Messages[0].Data!;
        Assert.That(color.Type, Is.EqualTo(ColorType.Indexed));
        Assert.That(color.Index, Is.EqualTo((byte)colorIndex));
    }

    [TestCase(256)]
    [TestCase(300)]
    [TestCase(999)]
    public void ParseSgrSequence_256ColorInvalidValues_CreatesUnknownMessage(int colorIndex)
    {
        // Arrange
        var raw = $"\x1b[38;5;{colorIndex}m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        // When extended color parsing fails, we get:
        // 1. Unknown message for 38
        // 2. Slow blink message for 5 
        // 3. Unknown message for the invalid color index
        Assert.That(result.Messages, Has.Length.EqualTo(3));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.unknown"));
        Assert.That(result.Messages[0].Implemented, Is.False);
        Assert.That(result.Messages[1].Type, Is.EqualTo("sgr.slowBlink"));
        Assert.That(result.Messages[1].Implemented, Is.True);
        Assert.That(result.Messages[2].Type, Is.EqualTo("sgr.unknown"));
        Assert.That(result.Messages[2].Implemented, Is.False);
    }

    #endregion

    #region RGB Color Tests

    [Test]
    public void ParseSgrSequence_RgbForeground_CreatesRgbColorMessage()
    {
        // Arrange - ESC[38;2;255;128;64m (orange RGB color)
        var raw = "\x1b[38;2;255;128;64m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Type, Is.EqualTo("sgr"));
        Assert.That(result.Messages, Has.Length.EqualTo(1));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.foregroundColor"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        var color = (Color)result.Messages[0].Data!;
        Assert.That(color.Type, Is.EqualTo(ColorType.Rgb));
        Assert.That(color.Red, Is.EqualTo(255));
        Assert.That(color.Green, Is.EqualTo(128));
        Assert.That(color.Blue, Is.EqualTo(64));
    }

    [Test]
    public void ParseSgrSequence_RgbBackground_CreatesRgbColorMessage()
    {
        // Arrange - ESC[48;2;0;255;0m (pure green RGB background)
        var raw = "\x1b[48;2;0;255;0m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Type, Is.EqualTo("sgr"));
        Assert.That(result.Messages, Has.Length.EqualTo(1));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.backgroundColor"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        var color = (Color)result.Messages[0].Data!;
        Assert.That(color.Type, Is.EqualTo(ColorType.Rgb));
        Assert.That(color.Red, Is.EqualTo(0));
        Assert.That(color.Green, Is.EqualTo(255));
        Assert.That(color.Blue, Is.EqualTo(0));
    }

    [Test]
    public void ParseSgrSequence_RgbUnderline_CreatesRgbColorMessage()
    {
        // Arrange - ESC[58;2;128;64;192m (purple RGB underline)
        var raw = "\x1b[58;2;128;64;192m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Type, Is.EqualTo("sgr"));
        Assert.That(result.Messages, Has.Length.EqualTo(1));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.underlineColor"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        var color = (Color)result.Messages[0].Data!;
        Assert.That(color.Type, Is.EqualTo(ColorType.Rgb));
        Assert.That(color.Red, Is.EqualTo(128));
        Assert.That(color.Green, Is.EqualTo(64));
        Assert.That(color.Blue, Is.EqualTo(192));
    }

    [TestCase(0, 0, 0)]
    [TestCase(255, 255, 255)]
    [TestCase(128, 128, 128)]
    public void ParseSgrSequence_RgbBoundaryValues_ParsesCorrectly(int r, int g, int b)
    {
        // Arrange
        var raw = $"\x1b[38;2;{r};{g};{b}m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Messages, Has.Length.EqualTo(1));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.foregroundColor"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        var color = (Color)result.Messages[0].Data!;
        Assert.That(color.Type, Is.EqualTo(ColorType.Rgb));
        Assert.That(color.Red, Is.EqualTo((byte)r));
        Assert.That(color.Green, Is.EqualTo((byte)g));
        Assert.That(color.Blue, Is.EqualTo((byte)b));
    }

    [TestCase(256, 128, 128)]
    [TestCase(128, 256, 128)]
    [TestCase(128, 128, 300)]
    public void ParseSgrSequence_RgbInvalidValues_CreatesUnknownMessage(int r, int g, int b)
    {
        // Arrange
        var raw = $"\x1b[38;2;{r};{g};{b}m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        // When extended color parsing fails, we get:
        // 1. Unknown message for 38
        // 2. Faint message for 2
        // 3. Unknown messages for the remaining parameters
        Assert.That(result.Messages, Has.Length.EqualTo(5));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.unknown"));
        Assert.That(result.Messages[0].Implemented, Is.False);
        Assert.That(result.Messages[1].Type, Is.EqualTo("sgr.faint"));
        Assert.That(result.Messages[1].Implemented, Is.True);
        // The remaining parameters will be processed as individual SGR commands
    }

    #endregion

    #region Colon-Separated Format Tests

    [Test]
    public void ParseSgrSequence_ColonSeparated256Color_ParsesCorrectly()
    {
        // Arrange - ESC[38:5:196m (colon-separated 256-color)
        var raw = "\x1b[38:5:196m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Messages, Has.Length.EqualTo(1));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.foregroundColor"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        var color = (Color)result.Messages[0].Data!;
        Assert.That(color.Type, Is.EqualTo(ColorType.Indexed));
        Assert.That(color.Index, Is.EqualTo(196));
    }

    [Test]
    public void ParseSgrSequence_ColonSeparatedRgb_ParsesCorrectly()
    {
        // Arrange - ESC[38:2:255:128:64m (colon-separated RGB)
        var raw = "\x1b[38:2:255:128:64m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Messages, Has.Length.EqualTo(1));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.foregroundColor"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        var color = (Color)result.Messages[0].Data!;
        Assert.That(color.Type, Is.EqualTo(ColorType.Rgb));
        Assert.That(color.Red, Is.EqualTo(255));
        Assert.That(color.Green, Is.EqualTo(128));
        Assert.That(color.Blue, Is.EqualTo(64));
    }

    [Test]
    public void ParseSgrSequence_ColonSeparatedRgbWithColorspace_ParsesCorrectly()
    {
        // Arrange - ESC[38:2::255:128:64m (colon-separated RGB with empty colorspace)
        var raw = "\x1b[38:2::255:128:64m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Messages, Has.Length.EqualTo(1));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.foregroundColor"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        var color = (Color)result.Messages[0].Data!;
        Assert.That(color.Type, Is.EqualTo(ColorType.Rgb));
        Assert.That(color.Red, Is.EqualTo(255));
        Assert.That(color.Green, Is.EqualTo(128));
        Assert.That(color.Blue, Is.EqualTo(64));
    }

    [Test]
    public void ParseSgrSequence_ColonSeparatedRgbWithColorspaceId_ParsesCorrectly()
    {
        // Arrange - ESC[38:2:0:255:128:64m (colon-separated RGB with colorspace ID 0)
        var raw = "\x1b[38:2:0:255:128:64m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Messages, Has.Length.EqualTo(1));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.foregroundColor"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        var color = (Color)result.Messages[0].Data!;
        Assert.That(color.Type, Is.EqualTo(ColorType.Rgb));
        Assert.That(color.Red, Is.EqualTo(255));
        Assert.That(color.Green, Is.EqualTo(128));
        Assert.That(color.Blue, Is.EqualTo(64));
    }

    #endregion

    #region Bright Color Tests

    [Test]
    public void ParseSgrSequence_BrightForegroundColors_ParsesCorrectly()
    {
        // Test all bright foreground colors (90-97)
        var brightColors = new[]
        {
            (90, NamedColor.BrightBlack),
            (91, NamedColor.BrightRed),
            (92, NamedColor.BrightGreen),
            (93, NamedColor.BrightYellow),
            (94, NamedColor.BrightBlue),
            (95, NamedColor.BrightMagenta),
            (96, NamedColor.BrightCyan),
            (97, NamedColor.BrightWhite)
        };

        foreach (var (code, expectedColor) in brightColors)
        {
            // Arrange
            var raw = $"\x1b[{code}m";
            var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

            // Act
            var result = _parser.ParseSgrSequence(escapeSequence, raw);

            // Assert
            Assert.That(result.Messages, Has.Length.EqualTo(1), $"Failed for bright color code {code}");
            Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.foregroundColor"), $"Failed for bright color code {code}");
            Assert.That(result.Messages[0].Implemented, Is.True, $"Bright color {code} should be implemented (matching TypeScript)");
            
            var color = (Color)result.Messages[0].Data!;
            Assert.That(color.Type, Is.EqualTo(ColorType.Named), $"Failed for bright color code {code}");
            Assert.That(color.NamedColor, Is.EqualTo(expectedColor), $"Failed for bright color code {code}");
        }
    }

    [Test]
    public void ParseSgrSequence_BrightBackgroundColors_ParsesCorrectly()
    {
        // Test all bright background colors (100-107)
        var brightColors = new[]
        {
            (100, NamedColor.BrightBlack),
            (101, NamedColor.BrightRed),
            (102, NamedColor.BrightGreen),
            (103, NamedColor.BrightYellow),
            (104, NamedColor.BrightBlue),
            (105, NamedColor.BrightMagenta),
            (106, NamedColor.BrightCyan),
            (107, NamedColor.BrightWhite)
        };

        foreach (var (code, expectedColor) in brightColors)
        {
            // Arrange
            var raw = $"\x1b[{code}m";
            var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

            // Act
            var result = _parser.ParseSgrSequence(escapeSequence, raw);

            // Assert
            Assert.That(result.Messages, Has.Length.EqualTo(1), $"Failed for bright background color code {code}");
            Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.backgroundColor"), $"Failed for bright background color code {code}");
            Assert.That(result.Messages[0].Implemented, Is.True, $"Bright background color {code} should be implemented (matching TypeScript)");
            
            var color = (Color)result.Messages[0].Data!;
            Assert.That(color.Type, Is.EqualTo(ColorType.Named), $"Failed for bright background color code {code}");
            Assert.That(color.NamedColor, Is.EqualTo(expectedColor), $"Failed for bright background color code {code}");
        }
    }

    #endregion

    #region Complex Sequence Tests

    [Test]
    public void ParseSgrSequence_MixedExtendedColors_ParsesCorrectly()
    {
        // Arrange - Bold + 256-color foreground + RGB background
        var raw = "\x1b[1;38;5;196;48;2;0;128;255m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Messages, Has.Length.EqualTo(3));
        
        // Bold
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.bold"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        
        // 256-color foreground
        Assert.That(result.Messages[1].Type, Is.EqualTo("sgr.foregroundColor"));
        Assert.That(result.Messages[1].Implemented, Is.True);
        var fgColor = (Color)result.Messages[1].Data!;
        Assert.That(fgColor.Type, Is.EqualTo(ColorType.Indexed));
        Assert.That(fgColor.Index, Is.EqualTo(196));
        
        // RGB background
        Assert.That(result.Messages[2].Type, Is.EqualTo("sgr.backgroundColor"));
        Assert.That(result.Messages[2].Implemented, Is.True);
        var bgColor = (Color)result.Messages[2].Data!;
        Assert.That(bgColor.Type, Is.EqualTo(ColorType.Rgb));
        Assert.That(bgColor.Red, Is.EqualTo(0));
        Assert.That(bgColor.Green, Is.EqualTo(128));
        Assert.That(bgColor.Blue, Is.EqualTo(255));
    }

    [Test]
    public void ParseSgrSequence_ExtendedColorWithUnderlineStyle_ParsesCorrectly()
    {
        // Arrange - Curly underline + RGB underline color
        var raw = "\x1b[4:3;58;2;255;0;255m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        Assert.That(result.Messages, Has.Length.EqualTo(2));
        
        // Curly underline
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.underline"));
        Assert.That(result.Messages[0].Implemented, Is.True);
        var style = (UnderlineStyle)result.Messages[0].Data!;
        Assert.That(style, Is.EqualTo(UnderlineStyle.Curly));
        
        // RGB underline color
        Assert.That(result.Messages[1].Type, Is.EqualTo("sgr.underlineColor"));
        Assert.That(result.Messages[1].Implemented, Is.True);
        var ulColor = (Color)result.Messages[1].Data!;
        Assert.That(ulColor.Type, Is.EqualTo(ColorType.Rgb));
        Assert.That(ulColor.Red, Is.EqualTo(255));
        Assert.That(ulColor.Green, Is.EqualTo(0));
        Assert.That(ulColor.Blue, Is.EqualTo(255));
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public void ParseSgrSequence_IncompleteExtendedColor_CreatesUnknownMessage()
    {
        // Arrange - ESC[38;5m (missing color index)
        var raw = "\x1b[38;5m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        // When extended color parsing fails, we get:
        // 1. Unknown message for 38
        // 2. Slow blink message for 5
        Assert.That(result.Messages, Has.Length.EqualTo(2));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.unknown"));
        Assert.That(result.Messages[0].Implemented, Is.False);
        Assert.That(result.Messages[1].Type, Is.EqualTo("sgr.slowBlink"));
        Assert.That(result.Messages[1].Implemented, Is.True);
    }

    [Test]
    public void ParseSgrSequence_IncompleteRgbColor_CreatesUnknownMessage()
    {
        // Arrange - ESC[38;2;255m (missing green and blue components)
        var raw = "\x1b[38;2;255m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        // When extended color parsing fails, we get:
        // 1. Unknown message for 38
        // 2. Faint message for 2
        // 3. Unknown message for 255
        Assert.That(result.Messages, Has.Length.EqualTo(3));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.unknown"));
        Assert.That(result.Messages[0].Implemented, Is.False);
        Assert.That(result.Messages[1].Type, Is.EqualTo("sgr.faint"));
        Assert.That(result.Messages[1].Implemented, Is.True);
        Assert.That(result.Messages[2].Type, Is.EqualTo("sgr.unknown"));
        Assert.That(result.Messages[2].Implemented, Is.False);
    }

    [Test]
    public void ParseSgrSequence_UnknownExtendedColorType_CreatesUnknownMessage()
    {
        // Arrange - ESC[38;3;128m (unknown color type 3)
        var raw = "\x1b[38;3;128m";
        var escapeSequence = System.Text.Encoding.UTF8.GetBytes(raw);

        // Act
        var result = _parser.ParseSgrSequence(escapeSequence, raw);

        // Assert
        // When extended color parsing fails, we get:
        // 1. Unknown message for 38
        // 2. Italic message for 3
        // 3. Unknown message for 128
        Assert.That(result.Messages, Has.Length.EqualTo(3));
        Assert.That(result.Messages[0].Type, Is.EqualTo("sgr.unknown"));
        Assert.That(result.Messages[0].Implemented, Is.False);
        Assert.That(result.Messages[1].Type, Is.EqualTo("sgr.italic"));
        Assert.That(result.Messages[1].Implemented, Is.True);
        Assert.That(result.Messages[2].Type, Is.EqualTo("sgr.unknown"));
        Assert.That(result.Messages[2].Implemented, Is.False);
    }

    #endregion
}