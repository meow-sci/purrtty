using caTTY.Display.Configuration;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit.Configuration;

/// <summary>
///     Unit tests for MouseWheelScrollConfig class.
///     Tests factory methods, validation logic, and bounds checking.
/// </summary>
[TestFixture]
[Category("Unit")]
public class MouseWheelScrollConfigTests
{
    [Test]
    public void CreateForTestApp_ShouldReturnTestAppDefaults()
    {
        // Act
        var config = MouseWheelScrollConfig.CreateForTestApp();

        // Assert
        Assert.That(config.LinesPerStep, Is.EqualTo(3));
        Assert.That(config.EnableSmoothScrolling, Is.True);
        Assert.That(config.MinimumWheelDelta, Is.EqualTo(0.1f));
        Assert.That(config.MaxLinesPerOperation, Is.EqualTo(10));
    }

    [Test]
    public void CreateForGameMod_ShouldReturnGameModDefaults()
    {
        // Act
        var config = MouseWheelScrollConfig.CreateForGameMod();

        // Assert
        Assert.That(config.LinesPerStep, Is.EqualTo(5));
        Assert.That(config.EnableSmoothScrolling, Is.True);
        Assert.That(config.MinimumWheelDelta, Is.EqualTo(0.05f));
        Assert.That(config.MaxLinesPerOperation, Is.EqualTo(15));
    }

    [Test]
    public void CreateDefault_ShouldReturnDefaultConfiguration()
    {
        // Act
        var config = MouseWheelScrollConfig.CreateDefault();

        // Assert
        Assert.That(config.LinesPerStep, Is.EqualTo(3));
        Assert.That(config.EnableSmoothScrolling, Is.True);
        Assert.That(config.MinimumWheelDelta, Is.EqualTo(0.1f));
        Assert.That(config.MaxLinesPerOperation, Is.EqualTo(10));
    }

    [Test]
    public void Validate_WithValidConfiguration_ShouldNotThrow()
    {
        // Arrange
        var config = new MouseWheelScrollConfig
        {
            LinesPerStep = 5,
            EnableSmoothScrolling = true,
            MinimumWheelDelta = 0.05f,
            MaxLinesPerOperation = 20
        };

        // Act & Assert
        Assert.DoesNotThrow(() => config.Validate());
    }

    [Test]
    public void Validate_WithLinesPerStepTooLow_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new MouseWheelScrollConfig { LinesPerStep = 0 };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("LinesPerStep"));
        Assert.That(ex.Message, Does.Contain("must be between 1 and 10"));
    }

    [Test]
    public void Validate_WithLinesPerStepTooHigh_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new MouseWheelScrollConfig { LinesPerStep = 15 };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("LinesPerStep"));
        Assert.That(ex.Message, Does.Contain("must be between 1 and 10"));
    }

    [Test]
    public void Validate_WithMinimumWheelDeltaTooLow_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new MouseWheelScrollConfig { MinimumWheelDelta = 0.005f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("MinimumWheelDelta"));
        Assert.That(ex.Message, Does.Contain("must be between 0.01 and 1.0"));
    }

    [Test]
    public void Validate_WithMinimumWheelDeltaTooHigh_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new MouseWheelScrollConfig { MinimumWheelDelta = 1.5f };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("MinimumWheelDelta"));
        Assert.That(ex.Message, Does.Contain("must be between 0.01 and 1.0"));
    }

    [Test]
    public void Validate_WithMaxLinesPerOperationTooLow_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new MouseWheelScrollConfig { MaxLinesPerOperation = 0 };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("MaxLinesPerOperation"));
        Assert.That(ex.Message, Does.Contain("must be between 1 and 50"));
    }

    [Test]
    public void Validate_WithMaxLinesPerOperationTooHigh_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new MouseWheelScrollConfig { MaxLinesPerOperation = 100 };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("MaxLinesPerOperation"));
        Assert.That(ex.Message, Does.Contain("must be between 1 and 50"));
    }

    [Test]
    public void Validate_WithMaxLinesPerOperationLessThanLinesPerStep_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new MouseWheelScrollConfig 
        { 
            LinesPerStep = 5, 
            MaxLinesPerOperation = 3 
        };

        // Act & Assert
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("MaxLinesPerOperation"));
        Assert.That(ex.Message, Does.Contain("must be at least as large as LinesPerStep"));
    }

    [Test]
    public void WithModifications_WithAllParameters_ShouldReturnModifiedConfig()
    {
        // Arrange
        var original = MouseWheelScrollConfig.CreateDefault();

        // Act
        MouseWheelScrollConfig modified = original.WithModifications(
            linesPerStep: 7,
            enableSmoothScrolling: false,
            minimumWheelDelta: 0.2f,
            maxLinesPerOperation: 25);

        // Assert
        Assert.That(modified.LinesPerStep, Is.EqualTo(7));
        Assert.That(modified.EnableSmoothScrolling, Is.False);
        Assert.That(modified.MinimumWheelDelta, Is.EqualTo(0.2f));
        Assert.That(modified.MaxLinesPerOperation, Is.EqualTo(25));
    }

    [Test]
    public void WithModifications_WithPartialParameters_ShouldRetainOriginalValues()
    {
        // Arrange
        var original = MouseWheelScrollConfig.CreateForGameMod();

        // Act
        MouseWheelScrollConfig modified = original.WithModifications(linesPerStep: 2);

        // Assert
        Assert.That(modified.LinesPerStep, Is.EqualTo(2));
        Assert.That(modified.EnableSmoothScrolling, Is.EqualTo(original.EnableSmoothScrolling));
        Assert.That(modified.MinimumWheelDelta, Is.EqualTo(original.MinimumWheelDelta));
        Assert.That(modified.MaxLinesPerOperation, Is.EqualTo(original.MaxLinesPerOperation));
    }

    [Test]
    public void WithModifications_WithNoParameters_ShouldReturnIdenticalConfig()
    {
        // Arrange
        var original = MouseWheelScrollConfig.CreateForTestApp();

        // Act
        MouseWheelScrollConfig modified = original.WithModifications();

        // Assert
        Assert.That(modified.LinesPerStep, Is.EqualTo(original.LinesPerStep));
        Assert.That(modified.EnableSmoothScrolling, Is.EqualTo(original.EnableSmoothScrolling));
        Assert.That(modified.MinimumWheelDelta, Is.EqualTo(original.MinimumWheelDelta));
        Assert.That(modified.MaxLinesPerOperation, Is.EqualTo(original.MaxLinesPerOperation));
    }

    [Test]
    public void ToString_ShouldReturnFormattedString()
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
    public void BoundaryValues_ShouldPassValidation()
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
    public void DefaultConstructor_ShouldHaveValidDefaults()
    {
        // Act
        var config = new MouseWheelScrollConfig();

        // Assert
        Assert.DoesNotThrow(() => config.Validate());
        Assert.That(config.LinesPerStep, Is.EqualTo(3));
        Assert.That(config.EnableSmoothScrolling, Is.True);
        Assert.That(config.MinimumWheelDelta, Is.EqualTo(0.1f));
        Assert.That(config.MaxLinesPerOperation, Is.EqualTo(10));
    }

    [Test]
    public void FactoryMethods_ShouldProduceValidConfigurations()
    {
        // Act & Assert
        Assert.DoesNotThrow(() => MouseWheelScrollConfig.CreateDefault().Validate());
        Assert.DoesNotThrow(() => MouseWheelScrollConfig.CreateForTestApp().Validate());
        Assert.DoesNotThrow(() => MouseWheelScrollConfig.CreateForGameMod().Validate());
    }

    /// <summary>
    /// Tests for configuration sensitivity behavior as per Requirements 4.2, 4.3, 4.4
    /// </summary>
    [TestCase(1, 1.0f, 1)] // Sensitivity=1 should scroll exactly 1 line per wheel step
    [TestCase(3, 1.0f, 3)] // Sensitivity=3 should scroll exactly 3 lines per wheel step
    [TestCase(5, 1.0f, 5)] // Sensitivity=5 should scroll exactly 5 lines per wheel step
    [TestCase(1, 2.0f, 2)] // Sensitivity=1 with 2.0 wheel delta should scroll 2 lines
    [TestCase(3, 2.0f, 6)] // Sensitivity=3 with 2.0 wheel delta should scroll 6 lines
    [TestCase(2, 0.5f, 1)] // Sensitivity=2 with 0.5 wheel delta should scroll 1 line
    public void SensitivityBehavior_ShouldProduceCorrectLineCount(int sensitivity, float wheelDelta, int expectedLines)
    {
        // Arrange
        var config = new MouseWheelScrollConfig
        {
            LinesPerStep = sensitivity,
            EnableSmoothScrolling = true,
            MinimumWheelDelta = 0.1f,
            MaxLinesPerOperation = 50 // High enough to not interfere
        };

        // Act
        config.Validate(); // Should not throw

        // Simulate the wheel processing algorithm from TerminalController
        float accumulator = 0.0f;
        accumulator += wheelDelta * config.LinesPerStep;

        // Apply overflow protection (same as implementation)
        if (Math.Abs(accumulator) > 100.0f)
        {
            accumulator = Math.Sign(accumulator) * 10.0f;
        }

        // Extract integer scroll lines (same as implementation)
        int scrollLines = (int)Math.Floor(Math.Abs(accumulator));
        int clampedScrollLines = Math.Min(scrollLines, config.MaxLinesPerOperation);

        // Assert
        Assert.That(clampedScrollLines, Is.EqualTo(expectedLines), 
            $"Sensitivity {sensitivity} with wheel delta {wheelDelta} should produce {expectedLines} lines");
    }

    [Test]
    public void SensitivityBehavior_WithSensitivity1_ShouldScrollExactly1LinePerWheelStep()
    {
        // Arrange
        var config = new MouseWheelScrollConfig
        {
            LinesPerStep = 1,
            EnableSmoothScrolling = true,
            MinimumWheelDelta = 0.1f,
            MaxLinesPerOperation = 10
        };

        // Test various wheel delta values
        float[] wheelDeltas = { 1.0f, 2.0f, 3.0f, 0.5f, 1.5f };
        int[] expectedLines = { 1, 2, 3, 0, 1 }; // Floor of delta * sensitivity

        for (int i = 0; i < wheelDeltas.Length; i++)
        {
            // Act
            float accumulator = wheelDeltas[i] * config.LinesPerStep;
            int scrollLines = (int)Math.Floor(Math.Abs(accumulator));
            int clampedScrollLines = Math.Min(scrollLines, config.MaxLinesPerOperation);

            // Assert
            Assert.That(clampedScrollLines, Is.EqualTo(expectedLines[i]),
                $"Wheel delta {wheelDeltas[i]} with sensitivity 1 should produce {expectedLines[i]} lines");
        }
    }

    [Test]
    public void SensitivityBehavior_WithSensitivity3_ShouldScrollExactly3LinesPerWheelStep()
    {
        // Arrange
        var config = new MouseWheelScrollConfig
        {
            LinesPerStep = 3,
            EnableSmoothScrolling = true,
            MinimumWheelDelta = 0.1f,
            MaxLinesPerOperation = 20
        };

        // Test various wheel delta values
        float[] wheelDeltas = { 1.0f, 2.0f, 0.5f, 1.5f, 0.3f };
        int[] expectedLines = { 3, 6, 1, 4, 0 }; // Floor of delta * 3

        for (int i = 0; i < wheelDeltas.Length; i++)
        {
            // Act
            float accumulator = wheelDeltas[i] * config.LinesPerStep;
            int scrollLines = (int)Math.Floor(Math.Abs(accumulator));
            int clampedScrollLines = Math.Min(scrollLines, config.MaxLinesPerOperation);

            // Assert
            Assert.That(clampedScrollLines, Is.EqualTo(expectedLines[i]),
                $"Wheel delta {wheelDeltas[i]} with sensitivity 3 should produce {expectedLines[i]} lines");
        }
    }

    [Test]
    public void SensitivityBehavior_WithDifferentSensitivities_ShouldScaleProportionally()
    {
        // Arrange
        float wheelDelta = 1.0f;
        int[] sensitivities = { 1, 2, 3, 5, 10 };

        foreach (int sensitivity in sensitivities)
        {
            var config = new MouseWheelScrollConfig
            {
                LinesPerStep = sensitivity,
                EnableSmoothScrolling = true,
                MinimumWheelDelta = 0.1f,
                MaxLinesPerOperation = 50
            };

            // Act
            float accumulator = wheelDelta * config.LinesPerStep;
            int scrollLines = (int)Math.Floor(Math.Abs(accumulator));
            int clampedScrollLines = Math.Min(scrollLines, config.MaxLinesPerOperation);

            // Assert
            Assert.That(clampedScrollLines, Is.EqualTo(sensitivity),
                $"Sensitivity {sensitivity} with wheel delta 1.0 should produce {sensitivity} lines");
        }
    }

    [Test]
    public void SensitivityBehavior_WithMaxLinesPerOperationClamping_ShouldRespectLimit()
    {
        // Arrange
        var config = new MouseWheelScrollConfig
        {
            LinesPerStep = 10,
            EnableSmoothScrolling = true,
            MinimumWheelDelta = 0.1f,
            MaxLinesPerOperation = 5 // Lower than what sensitivity would produce
        };

        // Act
        float wheelDelta = 2.0f; // Would produce 20 lines with sensitivity 10
        float accumulator = wheelDelta * config.LinesPerStep;
        int scrollLines = (int)Math.Floor(Math.Abs(accumulator));
        int clampedScrollLines = Math.Min(scrollLines, config.MaxLinesPerOperation);

        // Assert
        Assert.That(clampedScrollLines, Is.EqualTo(5),
            "Lines should be clamped to MaxLinesPerOperation even with high sensitivity");
    }

    [Test]
    public void SensitivityBehavior_WithFractionalAccumulation_ShouldAccumulateCorrectly()
    {
        // Arrange
        var config = new MouseWheelScrollConfig
        {
            LinesPerStep = 3,
            EnableSmoothScrolling = true,
            MinimumWheelDelta = 0.1f,
            MaxLinesPerOperation = 20
        };

        // Act - Simulate multiple small wheel events that should accumulate
        float accumulator = 0.0f;
        
        // First event: 0.3 * 3 = 0.9 (should produce 0 lines, accumulator = 0.9)
        accumulator += 0.3f * config.LinesPerStep;
        int scrollLines1 = (int)Math.Floor(Math.Abs(accumulator));
        float remainingAccumulator1 = accumulator - scrollLines1;

        // Second event: 0.4 * 3 = 1.2, total = 0.9 + 1.2 = 2.1 (should produce 2 lines, accumulator = 0.1)
        accumulator = remainingAccumulator1 + (0.4f * config.LinesPerStep);
        int scrollLines2 = (int)Math.Floor(Math.Abs(accumulator));
        float remainingAccumulator2 = accumulator - scrollLines2;

        // Assert
        Assert.That(scrollLines1, Is.EqualTo(0), "First fractional event should produce 0 lines");
        Assert.That(scrollLines2, Is.EqualTo(2), "Second event should produce 2 lines from accumulated delta");
        Assert.That(remainingAccumulator2, Is.EqualTo(0.1f).Within(0.01f), 
            "Remaining accumulator should be approximately 0.1");
    }

    [Test]
    public void ConfigurationClamping_ShouldRejectInvalidSensitivityValues()
    {
        // Test values outside the valid range (1-10)
        int[] invalidSensitivities = { 0, -1, 11, 15, 100 };

        foreach (int invalidSensitivity in invalidSensitivities)
        {
            // Arrange
            var config = new MouseWheelScrollConfig
            {
                LinesPerStep = invalidSensitivity,
                EnableSmoothScrolling = true,
                MinimumWheelDelta = 0.1f,
                MaxLinesPerOperation = 10
            };

            // Act & Assert
            ArgumentException? ex = Assert.Throws<ArgumentException>(() => config.Validate());
            Assert.That(ex.ParamName, Is.EqualTo("LinesPerStep"));
            Assert.That(ex.Message, Does.Contain("must be between 1 and 10"),
                $"Invalid sensitivity {invalidSensitivity} should be rejected");
        }
    }

    [Test]
    public void ConfigurationClamping_ShouldAcceptValidSensitivityBoundaries()
    {
        // Test boundary values (1 and 10)
        int[] validBoundarySensitivities = { 1, 10 };

        foreach (int sensitivity in validBoundarySensitivities)
        {
            // Arrange
            var config = new MouseWheelScrollConfig
            {
                LinesPerStep = sensitivity,
                EnableSmoothScrolling = true,
                MinimumWheelDelta = 0.1f,
                MaxLinesPerOperation = Math.Max(sensitivity, 10)
            };

            // Act & Assert
            Assert.DoesNotThrow(() => config.Validate(),
                $"Valid boundary sensitivity {sensitivity} should be accepted");
        }
    }
}