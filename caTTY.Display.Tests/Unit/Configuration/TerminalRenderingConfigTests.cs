using caTTY.Display.Configuration;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit.Configuration;

/// <summary>
///     Unit tests for TerminalRenderingConfig class.
///     Tests factory methods, validation logic, and bounds checking.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TerminalRenderingConfigTests
{
    [Test]
    public void CreateForTestApp_ShouldReturnStandardMetrics()
    {
        // Act
        var config = TerminalRenderingConfig.CreateForTestApp();

        // Assert
        Assert.That(config.FontSize, Is.EqualTo(32.0f));
        Assert.That(config.CharacterWidth, Is.EqualTo(19.2f));
        Assert.That(config.LineHeight, Is.EqualTo(36.0f));
    }

    [Test]
    public void CreateDefault_ShouldReturnAutoDetectConfiguration()
    {
        // Act
        var config = TerminalRenderingConfig.CreateDefault();

        // Assert
        Assert.That(config.FontSize, Is.EqualTo(32.0f));
        Assert.That(config.CharacterWidth, Is.EqualTo(19.2f));
        Assert.That(config.LineHeight, Is.EqualTo(36.0f));
    }

    [Test]
    public void Validate_WithValidMetrics_ShouldNotThrow()
    {
        // Arrange
        var config = new TerminalRenderingConfig
        {
            FontSize = 12.0f, CharacterWidth = 8.0f, LineHeight = 14.0f
        };

        // Act & Assert
        Assert.DoesNotThrow(() => config.Validate());
    }

    [Test]
    public void Validate_WithZeroFontSize_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalRenderingConfig { FontSize = 0.0f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("FontSize"));
        Assert.That(ex.Message, Does.Contain("must be between 0 and 128"));
    }

    [Test]
    public void Validate_WithNegativeFontSize_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalRenderingConfig { FontSize = -5.0f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("FontSize"));
        Assert.That(ex.Message, Does.Contain("must be between 0 and 128"));
    }

    [Test]
    public void Validate_WithExcessiveFontSize_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalRenderingConfig { FontSize = 256.0f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("FontSize"));
        Assert.That(ex.Message, Does.Contain("must be between 0 and 128"));
    }

    [Test]
    public void Validate_WithZeroCharacterWidth_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalRenderingConfig { CharacterWidth = 0.0f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("CharacterWidth"));
        Assert.That(ex.Message, Does.Contain("must be between 0 and 50"));
    }

    [Test]
    public void Validate_WithNegativeCharacterWidth_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalRenderingConfig { CharacterWidth = -2.0f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("CharacterWidth"));
        Assert.That(ex.Message, Does.Contain("must be between 0 and 50"));
    }

    [Test]
    public void Validate_WithExcessiveCharacterWidth_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalRenderingConfig { CharacterWidth = 128.0f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("CharacterWidth"));
        Assert.That(ex.Message, Does.Contain("must be between 0 and 50"));
    }

    [Test]
    public void Validate_WithZeroLineHeight_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalRenderingConfig { LineHeight = 0.0f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("LineHeight"));
        Assert.That(ex.Message, Does.Contain("must be between 0 and 100"));
    }

    [Test]
    public void Validate_WithNegativeLineHeight_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalRenderingConfig { LineHeight = -3.0f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("LineHeight"));
        Assert.That(ex.Message, Does.Contain("must be between 0 and 100"));
    }

    [Test]
    public void Validate_WithExcessiveLineHeight_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalRenderingConfig { LineHeight = 150.0f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("LineHeight"));
        Assert.That(ex.Message, Does.Contain("must be between 0 and 100"));
    }

    [Test]
    public void WithModifications_WithAllParameters_ShouldReturnModifiedConfig()
    {
        // Arrange
        var original = TerminalRenderingConfig.CreateForTestApp();

        // Act
        TerminalRenderingConfig modified = original.WithModifications(
            20.0f,
            12.0f,
            24.0f);

        // Assert
        Assert.That(modified.FontSize, Is.EqualTo(20.0f));
        Assert.That(modified.CharacterWidth, Is.EqualTo(12.0f));
        Assert.That(modified.LineHeight, Is.EqualTo(24.0f));
    }

    [Test]
    public void WithModifications_WithPartialParameters_ShouldRetainOriginalValues()
    {
        // Arrange
        var original = TerminalRenderingConfig.CreateForGameMod();

        // Act
        TerminalRenderingConfig modified = original.WithModifications(10.0f);

        // Assert
        Assert.That(modified.FontSize, Is.EqualTo(10.0f));
        Assert.That(modified.CharacterWidth, Is.EqualTo(original.CharacterWidth));
        Assert.That(modified.LineHeight, Is.EqualTo(original.LineHeight));
    }

    [Test]
    public void WithModifications_WithNoParameters_ShouldReturnIdenticalConfig()
    {
        // Arrange
        var original = TerminalRenderingConfig.CreateDefault();

        // Act
        TerminalRenderingConfig modified = original.WithModifications();

        // Assert
        Assert.That(modified.FontSize, Is.EqualTo(original.FontSize));
        Assert.That(modified.CharacterWidth, Is.EqualTo(original.CharacterWidth));
        Assert.That(modified.LineHeight, Is.EqualTo(original.LineHeight));
    }

    [Test]
    public void ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var config = new TerminalRenderingConfig
        {
            FontSize = 14.5f,
            CharacterWidth = 8.7f,
            LineHeight = 16.2f
        };

        // Act
        string result = config.ToString();

        // Assert
        Assert.That(result, Does.Contain("FontSize=14.5"));
        Assert.That(result, Does.Contain("CharacterWidth=8.7"));
        Assert.That(result, Does.Contain("LineHeight=16.2"));
    }

    [Test]
    public void BoundaryValues_ShouldPassValidation()
    {
        // Test minimum valid values
        var minConfig = new TerminalRenderingConfig
        {
            FontSize = 0.1f, CharacterWidth = 0.1f, LineHeight = 0.1f
        };
        Assert.DoesNotThrow(() => minConfig.Validate());

        // Test maximum valid values
        var maxConfig = new TerminalRenderingConfig
        {
            FontSize = 72.0f, CharacterWidth = 50.0f, LineHeight = 100.0f
        };
        Assert.DoesNotThrow(() => maxConfig.Validate());
    }
}
