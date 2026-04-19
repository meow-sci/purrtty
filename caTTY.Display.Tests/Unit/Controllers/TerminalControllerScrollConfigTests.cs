using caTTY.Display.Configuration;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit.Controllers;

/// <summary>
///     Unit tests for TerminalController mouse wheel scroll configuration integration.
///     Tests constructor overloads, runtime configuration updates, and validation.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TerminalControllerScrollConfigTests
{
    [Test]
    public void MouseWheelScrollConfig_CreateDefault_ShouldReturnValidConfiguration()
    {
        // Act
        var config = MouseWheelScrollConfig.CreateDefault();

        // Assert
        Assert.DoesNotThrow(() => config.Validate());
        Assert.That(config.LinesPerStep, Is.EqualTo(3));
        Assert.That(config.EnableSmoothScrolling, Is.True);
        Assert.That(config.MinimumWheelDelta, Is.EqualTo(0.1f));
        Assert.That(config.MaxLinesPerOperation, Is.EqualTo(10));
    }

    [Test]
    public void MouseWheelScrollConfig_CreateForTestApp_ShouldReturnValidConfiguration()
    {
        // Act
        var config = MouseWheelScrollConfig.CreateForTestApp();

        // Assert
        Assert.DoesNotThrow(() => config.Validate());
        Assert.That(config.LinesPerStep, Is.EqualTo(3));
        Assert.That(config.EnableSmoothScrolling, Is.True);
        Assert.That(config.MinimumWheelDelta, Is.EqualTo(0.1f));
        Assert.That(config.MaxLinesPerOperation, Is.EqualTo(10));
    }

    [Test]
    public void MouseWheelScrollConfig_CreateForGameMod_ShouldReturnValidConfiguration()
    {
        // Act
        var config = MouseWheelScrollConfig.CreateForGameMod();

        // Assert
        Assert.DoesNotThrow(() => config.Validate());
        Assert.That(config.LinesPerStep, Is.EqualTo(5));
        Assert.That(config.EnableSmoothScrolling, Is.True);
        Assert.That(config.MinimumWheelDelta, Is.EqualTo(0.05f));
        Assert.That(config.MaxLinesPerOperation, Is.EqualTo(15));
    }

    [Test]
    public void MouseWheelScrollConfig_WithModifications_ShouldCreateValidVariants()
    {
        // Arrange
        var baseConfig = MouseWheelScrollConfig.CreateDefault();

        // Act
        var modifiedConfig = baseConfig.WithModifications(
            linesPerStep: 7,
            enableSmoothScrolling: false,
            minimumWheelDelta: 0.2f,
            maxLinesPerOperation: 25);

        // Assert
        Assert.DoesNotThrow(() => modifiedConfig.Validate());
        Assert.That(modifiedConfig.LinesPerStep, Is.EqualTo(7));
        Assert.That(modifiedConfig.EnableSmoothScrolling, Is.False);
        Assert.That(modifiedConfig.MinimumWheelDelta, Is.EqualTo(0.2f));
        Assert.That(modifiedConfig.MaxLinesPerOperation, Is.EqualTo(25));
    }

    [Test]
    public void MouseWheelScrollConfig_ValidationConstraints_ShouldEnforceBusinessRules()
    {
        // Test that MaxLinesPerOperation must be >= LinesPerStep
        var invalidConfig = new MouseWheelScrollConfig 
        { 
            LinesPerStep = 8, 
            MaxLinesPerOperation = 5 
        };

        ArgumentException? ex = Assert.Throws<ArgumentException>(() => invalidConfig.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("MaxLinesPerOperation"));
        Assert.That(ex.Message, Does.Contain("must be at least as large as LinesPerStep"));
    }

    [Test]
    public void MouseWheelScrollConfig_ToString_ShouldProvideReadableOutput()
    {
        // Arrange
        var config = new MouseWheelScrollConfig
        {
            LinesPerStep = 4,
            EnableSmoothScrolling = false,
            MinimumWheelDelta = 0.15f,
            MaxLinesPerOperation = 12
        };

        // Act
        string result = config.ToString();

        // Assert
        Assert.That(result, Does.Contain("LinesPerStep=4"));
        Assert.That(result, Does.Contain("EnableSmoothScrolling=False"));
        Assert.That(result, Does.Contain("MinimumWheelDelta=0.15"));
        Assert.That(result, Does.Contain("MaxLinesPerOperation=12"));
    }

    [Test]
    public void MouseWheelScrollConfig_BoundaryValues_ShouldPassValidation()
    {
        // Test minimum valid values
        var minConfig = new MouseWheelScrollConfig
        {
            LinesPerStep = 1,
            MinimumWheelDelta = 0.01f,
            MaxLinesPerOperation = 1
        };
        Assert.DoesNotThrow(() => minConfig.Validate());

        // Test maximum valid values
        var maxConfig = new MouseWheelScrollConfig
        {
            LinesPerStep = 10,
            MinimumWheelDelta = 1.0f,
            MaxLinesPerOperation = 50
        };
        Assert.DoesNotThrow(() => maxConfig.Validate());
    }

    [Test]
    public void MouseWheelScrollConfig_InvalidBoundaryValues_ShouldFailValidation()
    {
        // Test LinesPerStep too low
        var configTooLow = new MouseWheelScrollConfig { LinesPerStep = 0 };
        Assert.Throws<ArgumentException>(() => configTooLow.Validate());

        // Test LinesPerStep too high
        var configTooHigh = new MouseWheelScrollConfig { LinesPerStep = 15 };
        Assert.Throws<ArgumentException>(() => configTooHigh.Validate());

        // Test MinimumWheelDelta too low
        var deltaTooLow = new MouseWheelScrollConfig { MinimumWheelDelta = 0.005f };
        Assert.Throws<ArgumentException>(() => deltaTooLow.Validate());

        // Test MinimumWheelDelta too high
        var deltaTooHigh = new MouseWheelScrollConfig { MinimumWheelDelta = 1.5f };
        Assert.Throws<ArgumentException>(() => deltaTooHigh.Validate());

        // Test MaxLinesPerOperation too low
        var maxTooLow = new MouseWheelScrollConfig { MaxLinesPerOperation = 0 };
        Assert.Throws<ArgumentException>(() => maxTooLow.Validate());

        // Test MaxLinesPerOperation too high
        var maxTooHigh = new MouseWheelScrollConfig { MaxLinesPerOperation = 100 };
        Assert.Throws<ArgumentException>(() => maxTooHigh.Validate());
    }
}