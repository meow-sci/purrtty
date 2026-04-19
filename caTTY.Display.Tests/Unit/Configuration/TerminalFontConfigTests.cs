using caTTY.Display.Configuration;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit.Configuration;

/// <summary>
/// Unit tests for TerminalFontConfig class.
/// Tests factory methods, validation logic, and bounds checking.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TerminalFontConfigTests
{
    [Test]
    public void CreateForTestApp_ShouldReturnCorrectFontNamesAndSize()
    {
        // Act
        var config = TerminalFontConfig.CreateForTestApp();

        // Assert
        Assert.That(config.RegularFontName, Is.EqualTo("HackNerdFontMono-Regular"));
        Assert.That(config.BoldFontName, Is.EqualTo("HackNerdFontMono-Bold"));
        Assert.That(config.ItalicFontName, Is.EqualTo("HackNerdFontMono-Italic"));
        Assert.That(config.BoldItalicFontName, Is.EqualTo("HackNerdFontMono-BoldItalic"));
        Assert.That(config.FontSize, Is.EqualTo(32.0f));
        Assert.That(config.AutoDetectContext, Is.False);
    }

    [Test]
    public void CreateForGameMod_ShouldReturnCorrectFontNamesAndSize()
    {
        // Act
        var config = TerminalFontConfig.CreateForGameMod();

        // Assert
        Assert.That(config.RegularFontName, Is.EqualTo("HackNerdFontMono-Regular"));
        Assert.That(config.BoldFontName, Is.EqualTo("HackNerdFontMono-Bold"));
        Assert.That(config.ItalicFontName, Is.EqualTo("HackNerdFontMono-Italic"));
        Assert.That(config.BoldItalicFontName, Is.EqualTo("HackNerdFontMono-BoldItalic"));
        Assert.That(config.FontSize, Is.EqualTo(32.0f)); // Smaller for game context
        Assert.That(config.AutoDetectContext, Is.False);
    }

    [Test]
    public void DefaultConstructor_ShouldReturnDefaultValues()
    {
        // Act
        var config = new TerminalFontConfig();

        // Assert
        Assert.That(config.RegularFontName, Is.EqualTo("HackNerdFontMono-Regular"));
        Assert.That(config.BoldFontName, Is.EqualTo("HackNerdFontMono-Bold"));
        Assert.That(config.ItalicFontName, Is.EqualTo("HackNerdFontMono-Italic"));
        Assert.That(config.BoldItalicFontName, Is.EqualTo("HackNerdFontMono-BoldItalic"));
        Assert.That(config.FontSize, Is.EqualTo(16.0f));
        Assert.That(config.AutoDetectContext, Is.True);
    }

    [Test]
    public void Validate_WithValidConfiguration_ShouldNotThrow()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = "TestFont-Regular",
            BoldFontName = "TestFont-Bold",
            ItalicFontName = "TestFont-Italic",
            BoldItalicFontName = "TestFont-BoldItalic",
            FontSize = 12.0f
        };

        // Act & Assert
        Assert.DoesNotThrow(() => config.Validate());
    }

    [Test]
    public void Validate_WithNullRegularFontName_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalFontConfig { RegularFontName = null! };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("RegularFontName"));
        Assert.That(ex.Message, Does.Contain("cannot be null or empty"));
    }

    [Test]
    public void Validate_WithEmptyRegularFontName_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalFontConfig { RegularFontName = "" };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("RegularFontName"));
        Assert.That(ex.Message, Does.Contain("cannot be null or empty"));
    }

    [Test]
    public void Validate_WithWhitespaceRegularFontName_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalFontConfig { RegularFontName = "   " };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("RegularFontName"));
        Assert.That(ex.Message, Does.Contain("cannot be null or empty"));
    }

    [Test]
    public void Validate_WithZeroFontSize_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalFontConfig { FontSize = 0.0f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("FontSize"));
        Assert.That(ex.Message, Does.Contain("must be between 0 and 72"));
    }

    [Test]
    public void Validate_WithNegativeFontSize_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalFontConfig { FontSize = -5.0f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("FontSize"));
        Assert.That(ex.Message, Does.Contain("must be between 0 and 72"));
    }

    [Test]
    public void Validate_WithExcessiveFontSize_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new TerminalFontConfig { FontSize = 100.0f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("FontSize"));
        Assert.That(ex.Message, Does.Contain("must be between 0 and 72"));
    }

    [Test]
    public void Validate_WithNullBoldFontName_ShouldFallbackToRegular()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = "TestFont-Regular",
            BoldFontName = null!
        };

        // Act
        config.Validate();

        // Assert
        Assert.That(config.BoldFontName, Is.EqualTo("TestFont-Regular"));
    }

    [Test]
    public void Validate_WithNullItalicFontName_ShouldFallbackToRegular()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = "TestFont-Regular",
            ItalicFontName = null!
        };

        // Act
        config.Validate();

        // Assert
        Assert.That(config.ItalicFontName, Is.EqualTo("TestFont-Regular"));
    }

    [Test]
    public void Validate_WithNullBoldItalicFontName_ShouldFallbackToRegular()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = "TestFont-Regular",
            BoldItalicFontName = null!
        };

        // Act
        config.Validate();

        // Assert
        Assert.That(config.BoldItalicFontName, Is.EqualTo("TestFont-Regular"));
    }

    [Test]
    public void Validate_WithAllNullStyleFonts_ShouldFallbackToRegular()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = "TestFont-Regular",
            BoldFontName = null!,
            ItalicFontName = null!,
            BoldItalicFontName = null!
        };

        // Act
        config.Validate();

        // Assert
        Assert.That(config.BoldFontName, Is.EqualTo("TestFont-Regular"));
        Assert.That(config.ItalicFontName, Is.EqualTo("TestFont-Regular"));
        Assert.That(config.BoldItalicFontName, Is.EqualTo("TestFont-Regular"));
    }

    [Test]
    public void BoundaryValues_ShouldPassValidation()
    {
        // Test minimum valid font size
        var minConfig = new TerminalFontConfig
        {
            RegularFontName = "TestFont",
            FontSize = 0.1f
        };
        Assert.DoesNotThrow(() => minConfig.Validate());

        // Test maximum valid font size
        var maxConfig = new TerminalFontConfig
        {
            RegularFontName = "TestFont",
            FontSize = 72.0f
        };
        Assert.DoesNotThrow(() => maxConfig.Validate());
    }

    [Test]
    public void Validate_WithValidBoundaryFontSizes_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var config8 = new TerminalFontConfig { FontSize = 8.0f };
        Assert.DoesNotThrow(() => config8.Validate());

        var config72 = new TerminalFontConfig { FontSize = 72.0f };
        Assert.DoesNotThrow(() => config72.Validate());
    }

    [Test]
    public void Validate_WithInvalidBoundaryFontSizes_ShouldThrowArgumentException()
    {
        // Test just below minimum
        var configTooSmall = new TerminalFontConfig { FontSize = 0.0f };
        ArgumentException? ex1 = Assert.Throws<ArgumentException>(() => configTooSmall.Validate());
        Assert.That(ex1.ParamName, Is.EqualTo("FontSize"));

        // Test just above maximum
        var configTooLarge = new TerminalFontConfig { FontSize = 72.1f };
        ArgumentException? ex2 = Assert.Throws<ArgumentException>(() => configTooLarge.Validate());
        Assert.That(ex2.ParamName, Is.EqualTo("FontSize"));
    }

    [Test]
    public void CalculateCharacterMetrics_WithValidConfiguration_ReturnsCorrectMetrics()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = "Consolas",
            FontSize = 14.0f
        };
        config.Validate();

        // Act
        var metrics = config.CalculateCharacterMetrics();

        // Assert
        Assert.That(metrics.Width, Is.EqualTo(8.4f).Within(0.001f)); // 14 * 0.6
        Assert.That(metrics.Height, Is.EqualTo(14.0f));
        Assert.That(metrics.BaselineOffset, Is.EqualTo(11.2f).Within(0.001f)); // 14 * 0.8
        Assert.That(metrics.FontSize, Is.EqualTo(14.0f));
        Assert.That(metrics.FontName, Is.EqualTo("Consolas"));
    }

    [Test]
    public void CalculateCharacterMetrics_WithDifferentFontSizes_ScalesCorrectly()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = "Courier New",
            FontSize = 20.0f
        };
        config.Validate();

        // Act
        var metrics = config.CalculateCharacterMetrics();

        // Assert
        Assert.That(metrics.Width, Is.EqualTo(12.0f).Within(0.001f)); // 20 * 0.6
        Assert.That(metrics.Height, Is.EqualTo(20.0f));
        Assert.That(metrics.BaselineOffset, Is.EqualTo(16.0f).Within(0.001f)); // 20 * 0.8
        Assert.That(metrics.FontSize, Is.EqualTo(20.0f));
        Assert.That(metrics.FontName, Is.EqualTo("Courier New"));
    }

    [Test]
    public void CalculateScaledCharacterMetrics_WithDefaultScale_ReturnsSameAsUnscaled()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = "Consolas",
            FontSize = 12.0f
        };
        config.Validate();

        // Act
        var unscaledMetrics = config.CalculateCharacterMetrics();
        var scaledMetrics = config.CalculateScaledCharacterMetrics();

        // Assert
        Assert.That(scaledMetrics.Width, Is.EqualTo(unscaledMetrics.Width).Within(0.001f));
        Assert.That(scaledMetrics.Height, Is.EqualTo(unscaledMetrics.Height).Within(0.001f));
        Assert.That(scaledMetrics.BaselineOffset, Is.EqualTo(unscaledMetrics.BaselineOffset).Within(0.001f));
        Assert.That(scaledMetrics.FontSize, Is.EqualTo(unscaledMetrics.FontSize).Within(0.001f));
        Assert.That(scaledMetrics.FontName, Is.EqualTo(unscaledMetrics.FontName));
    }
}
