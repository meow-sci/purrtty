using System;
using NUnit.Framework;
using caTTY.Display.Configuration;

namespace caTTY.Display.Tests.Unit.Configuration;

[TestFixture]
public class CharacterMetricsTests
{
    [Test]
    public void Constructor_WithDefaultValues_SetsPropertiesCorrectly()
    {
        // Act
        var metrics = new CharacterMetrics();

        // Assert
        Assert.That(metrics.Width, Is.EqualTo(0));
        Assert.That(metrics.Height, Is.EqualTo(0));
        Assert.That(metrics.BaselineOffset, Is.EqualTo(0));
        Assert.That(metrics.FontSize, Is.EqualTo(0));
        Assert.That(metrics.FontName, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Constructor_WithParameters_SetsPropertiesCorrectly()
    {
        // Arrange
        const float width = 12.0f;
        const float height = 20.0f;
        const float baselineOffset = 16.0f;
        const float fontSize = 14.0f;
        const string fontName = "Consolas";

        // Act
        var metrics = new CharacterMetrics(width, height, baselineOffset, fontSize, fontName);

        // Assert
        Assert.That(metrics.Width, Is.EqualTo(width));
        Assert.That(metrics.Height, Is.EqualTo(height));
        Assert.That(metrics.BaselineOffset, Is.EqualTo(baselineOffset));
        Assert.That(metrics.FontSize, Is.EqualTo(fontSize));
        Assert.That(metrics.FontName, Is.EqualTo(fontName));
    }

    [Test]
    public void Constructor_WithNullFontName_SetsEmptyString()
    {
        // Act
        var metrics = new CharacterMetrics(10, 15, 12, 12, null!);

        // Assert
        Assert.That(metrics.FontName, Is.EqualTo(string.Empty));
    }

    [Test]
    public void AspectRatio_WithValidDimensions_ReturnsCorrectRatio()
    {
        // Arrange
        var metrics = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");

        // Act
        var aspectRatio = metrics.AspectRatio;

        // Assert
        Assert.That(aspectRatio, Is.EqualTo(0.6f).Within(0.001f));
    }

    [Test]
    public void AspectRatio_WithZeroHeight_ReturnsZero()
    {
        // Arrange
        var metrics = new CharacterMetrics(12.0f, 0.0f, 0.0f, 14.0f, "Consolas");

        // Act
        var aspectRatio = metrics.AspectRatio;

        // Assert
        Assert.That(aspectRatio, Is.EqualTo(0));
    }

    [Test]
    public void Descent_ReturnsCorrectValue()
    {
        // Arrange
        var metrics = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");

        // Act
        var descent = metrics.Descent;

        // Assert
        Assert.That(descent, Is.EqualTo(4.0f)); // 20 - 16 = 4
    }

    [Test]
    public void Ascent_ReturnsBaselineOffset()
    {
        // Arrange
        var metrics = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");

        // Act
        var ascent = metrics.Ascent;

        // Assert
        Assert.That(ascent, Is.EqualTo(16.0f));
    }

    [Test]
    public void Scale_WithScaleFactor_ReturnsScaledMetrics()
    {
        // Arrange
        var originalMetrics = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");
        const float scaleFactor = 1.5f;

        // Act
        var scaledMetrics = originalMetrics.Scale(scaleFactor);

        // Assert
        Assert.That(scaledMetrics.Width, Is.EqualTo(18.0f).Within(0.001f));
        Assert.That(scaledMetrics.Height, Is.EqualTo(30.0f).Within(0.001f));
        Assert.That(scaledMetrics.BaselineOffset, Is.EqualTo(24.0f).Within(0.001f));
        Assert.That(scaledMetrics.FontSize, Is.EqualTo(21.0f).Within(0.001f));
        Assert.That(scaledMetrics.FontName, Is.EqualTo("Consolas"));
    }

    [Test]
    public void Scale_DoesNotModifyOriginal()
    {
        // Arrange
        var originalMetrics = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");
        const float scaleFactor = 2.0f;

        // Act
        var scaledMetrics = originalMetrics.Scale(scaleFactor);

        // Assert - original should be unchanged
        Assert.That(originalMetrics.Width, Is.EqualTo(12.0f));
        Assert.That(originalMetrics.Height, Is.EqualTo(20.0f));
        Assert.That(originalMetrics.BaselineOffset, Is.EqualTo(16.0f));
        Assert.That(originalMetrics.FontSize, Is.EqualTo(14.0f));
        Assert.That(originalMetrics.FontName, Is.EqualTo("Consolas"));

        // Scaled should be different
        Assert.That(scaledMetrics.Width, Is.EqualTo(24.0f));
        Assert.That(scaledMetrics.Height, Is.EqualTo(40.0f));
    }

    [Test]
    public void ToString_ReturnsFormattedString()
    {
        // Arrange
        var metrics = new CharacterMetrics(12.5f, 20.0f, 16.0f, 14.0f, "Consolas");

        // Act
        var result = metrics.ToString();

        // Assert
        Assert.That(result, Is.EqualTo("CharacterMetrics(Width=12.5, Height=20.0, Baseline=16.0, Font=Consolas 14.0pt)"));
    }

    [Test]
    public void Equals_WithSameValues_ReturnsTrue()
    {
        // Arrange
        var metrics1 = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");
        var metrics2 = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");

        // Act & Assert
        Assert.That(metrics1.Equals(metrics2), Is.True);
        Assert.That(metrics2.Equals(metrics1), Is.True);
    }

    [Test]
    public void Equals_WithDifferentValues_ReturnsFalse()
    {
        // Arrange
        var metrics1 = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");
        var metrics2 = new CharacterMetrics(13.0f, 20.0f, 16.0f, 14.0f, "Consolas");

        // Act & Assert
        Assert.That(metrics1.Equals(metrics2), Is.False);
    }

    [Test]
    public void Equals_WithNullObject_ReturnsFalse()
    {
        // Arrange
        var metrics = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");

        // Act & Assert
        Assert.That(metrics.Equals(null), Is.False);
    }

    [Test]
    public void Equals_WithDifferentType_ReturnsFalse()
    {
        // Arrange
        var metrics = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");
        var otherObject = "not a CharacterMetrics";

        // Act & Assert
        Assert.That(metrics.Equals(otherObject), Is.False);
    }

    [Test]
    public void GetHashCode_WithSameValues_ReturnsSameHashCode()
    {
        // Arrange
        var metrics1 = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");
        var metrics2 = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");

        // Act & Assert
        Assert.That(metrics1.GetHashCode(), Is.EqualTo(metrics2.GetHashCode()));
    }

    [Test]
    public void GetHashCode_WithDifferentValues_ReturnsDifferentHashCode()
    {
        // Arrange
        var metrics1 = new CharacterMetrics(12.0f, 20.0f, 16.0f, 14.0f, "Consolas");
        var metrics2 = new CharacterMetrics(13.0f, 20.0f, 16.0f, 14.0f, "Consolas");

        // Act & Assert
        Assert.That(metrics1.GetHashCode(), Is.Not.EqualTo(metrics2.GetHashCode()));
    }
}