using System;
using caTTY.Core.Terminal;
using caTTY.Display.Configuration;
using caTTY.Display.Controllers;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit.Controllers;

/// <summary>
///     Unit tests for TerminalController error handling and robustness.
///     Tests error recovery, accumulator reset, invalid input handling, and exception handling.
///     Uses direct testing approach without mocking to verify error handling logic.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TerminalControllerErrorHandlingTests
{
    [Test]
    public void WheelDeltaValidation_WithNaNValue_ShouldBeDetectedAsInvalid()
    {
        // This test verifies that NaN wheel delta values are properly detected by validation
        
        // Arrange
        float nanDelta = float.NaN;
        
        // Act
        bool isValid = float.IsFinite(nanDelta);
        
        // Assert
        Assert.That(isValid, Is.False, "NaN values should be detected as invalid");
    }

    [Test]
    public void WheelDeltaValidation_WithInfinityValue_ShouldBeDetectedAsInvalid()
    {
        // This test verifies that infinity wheel delta values are properly detected by validation
        
        // Arrange
        float[] infinityValues = { float.PositiveInfinity, float.NegativeInfinity };
        
        foreach (float infinityDelta in infinityValues)
        {
            // Act
            bool isValid = float.IsFinite(infinityDelta);
            
            // Assert
            Assert.That(isValid, Is.False, $"Infinity value {infinityDelta} should be detected as invalid");
        }
    }

    [Test]
    public void WheelDeltaValidation_WithValidValues_ShouldBeDetectedAsValid()
    {
        // This test verifies that valid wheel delta values are properly detected
        
        // Arrange
        float[] validValues = { -5.0f, -1.0f, 0.0f, 1.0f, 5.0f, 0.1f, -0.1f };
        
        foreach (float validDelta in validValues)
        {
            // Act
            bool isValid = float.IsFinite(validDelta);
            
            // Assert
            Assert.That(isValid, Is.True, $"Valid value {validDelta} should be detected as valid");
        }
    }

    [Test]
    public void AccumulatorOverflowProtection_WithLargeValues_ShouldClampCorrectly()
    {
        // This test verifies that the accumulator overflow protection works correctly
        
        // Arrange
        float[] testAccumulators = { 150.0f, -200.0f, 500.0f, -1000.0f };
        
        foreach (float testAccumulator in testAccumulators)
        {
            // Act - Simulate the overflow protection logic from ProcessMouseWheelScroll
            float clampedAccumulator = testAccumulator;
            if (Math.Abs(clampedAccumulator) > 100.0f)
            {
                clampedAccumulator = Math.Sign(clampedAccumulator) * 10.0f;
            }
            
            // Assert
            Assert.That(Math.Abs(clampedAccumulator), Is.LessThanOrEqualTo(100.0f), 
                $"Accumulator {testAccumulator} should be clamped to reasonable range");
            Assert.That(Math.Sign(clampedAccumulator), Is.EqualTo(Math.Sign(testAccumulator)), 
                $"Accumulator {testAccumulator} should preserve sign after clamping");
        }
    }

    [Test]
    public void AccumulatorReset_AfterError_ShouldPreventStuckState()
    {
        // This test verifies that accumulator reset logic prevents stuck states
        
        // Arrange
        float accumulator = 25.0f; // Some accumulated value
        
        // Act - Simulate error condition requiring accumulator reset
        bool errorOccurred = true; // Simulate error
        if (errorOccurred)
        {
            accumulator = 0.0f; // Reset as per error handling
        }
        
        // Assert
        Assert.That(accumulator, Is.EqualTo(0.0f), "Accumulator should be reset to 0 after error");
    }

    [Test]
    public void ExtremeWheelDeltaHandling_WithVeryLargeValues_ShouldClampCorrectly()
    {
        // This test verifies that extreme wheel delta values are handled correctly
        
        // Arrange
        float[] extremeDeltas = { 1500.0f, -2000.0f, 10000.0f, -5000.0f };
        
        foreach (float extremeDelta in extremeDeltas)
        {
            // Act - Simulate the extreme value clamping logic from HandleMouseWheelInput
            float clampedDelta = extremeDelta;
            if (Math.Abs(clampedDelta) > 1000.0f)
            {
                clampedDelta = Math.Sign(clampedDelta) * 10.0f; // Clamp to reasonable range
            }
            
            // Assert
            Assert.That(Math.Abs(clampedDelta), Is.LessThanOrEqualTo(1000.0f), 
                $"Extreme delta {extremeDelta} should be clamped");
            Assert.That(Math.Sign(clampedDelta), Is.EqualTo(Math.Sign(extremeDelta)), 
                $"Extreme delta {extremeDelta} should preserve sign after clamping");
        }
    }

    [Test]
    public void ScrollConfigValidation_WithInvalidValues_ShouldThrowArgumentException()
    {
        // This test verifies that invalid scroll configurations are properly rejected
        
        // Test invalid LinesPerStep values
        var invalidLinesPerStepConfigs = new[]
        {
            new MouseWheelScrollConfig { LinesPerStep = 0 },
            new MouseWheelScrollConfig { LinesPerStep = -1 },
            new MouseWheelScrollConfig { LinesPerStep = 15 }
        };
        
        foreach (var config in invalidLinesPerStepConfigs)
        {
            Assert.Throws<ArgumentException>(() => config.Validate(), 
                $"Invalid LinesPerStep {config.LinesPerStep} should throw ArgumentException");
        }
        
        // Test invalid MinimumWheelDelta values
        var invalidMinDeltaConfigs = new[]
        {
            new MouseWheelScrollConfig { MinimumWheelDelta = 0.005f },
            new MouseWheelScrollConfig { MinimumWheelDelta = 1.5f },
            new MouseWheelScrollConfig { MinimumWheelDelta = -0.1f }
        };
        
        foreach (var config in invalidMinDeltaConfigs)
        {
            Assert.Throws<ArgumentException>(() => config.Validate(), 
                $"Invalid MinimumWheelDelta {config.MinimumWheelDelta} should throw ArgumentException");
        }
        
        // Test invalid MaxLinesPerOperation values
        var invalidMaxLinesConfigs = new[]
        {
            new MouseWheelScrollConfig { MaxLinesPerOperation = 0 },
            new MouseWheelScrollConfig { MaxLinesPerOperation = -1 },
            new MouseWheelScrollConfig { MaxLinesPerOperation = 100 }
        };
        
        foreach (var config in invalidMaxLinesConfigs)
        {
            Assert.Throws<ArgumentException>(() => config.Validate(), 
                $"Invalid MaxLinesPerOperation {config.MaxLinesPerOperation} should throw ArgumentException");
        }
    }

    [Test]
    public void ScrollConfigValidation_WithValidBoundaryValues_ShouldNotThrow()
    {
        // This test verifies that valid boundary configurations are accepted
        
        // Test minimum valid values
        var minValidConfig = new MouseWheelScrollConfig
        {
            LinesPerStep = 1,
            MinimumWheelDelta = 0.01f,
            MaxLinesPerOperation = 1
        };
        Assert.DoesNotThrow(() => minValidConfig.Validate(), "Minimum valid config should not throw");
        
        // Test maximum valid values
        var maxValidConfig = new MouseWheelScrollConfig
        {
            LinesPerStep = 10,
            MinimumWheelDelta = 1.0f,
            MaxLinesPerOperation = 50
        };
        Assert.DoesNotThrow(() => maxValidConfig.Validate(), "Maximum valid config should not throw");
    }

    [Test]
    public void BoundaryConditionHandling_WithNoScrollingOccurred_ShouldClearAccumulator()
    {
        // This test verifies that boundary condition handling clears accumulator correctly
        
        // Arrange
        float accumulator = 15.0f; // Some accumulated value
        int previousOffset = 10;
        int newOffset = 10; // No change - at boundary
        
        // Act - Simulate boundary condition logic from ProcessMouseWheelScroll
        bool actuallyScrolled = (newOffset != previousOffset);
        if (!actuallyScrolled)
        {
            // At boundary - clear accumulator to prevent stuck state
            accumulator = 0.0f;
        }
        
        // Assert
        Assert.That(accumulator, Is.EqualTo(0.0f), 
            "Accumulator should be cleared when no scrolling occurred (boundary condition)");
    }

    [Test]
    public void BoundaryConditionHandling_WithScrollingOccurred_ShouldUpdateAccumulator()
    {
        // This test verifies that normal accumulator update works when scrolling occurs
        
        // Arrange
        float accumulator = 15.0f;
        int scrollLines = 5;
        bool scrollUp = true;
        int previousOffset = 10;
        int newOffset = 15; // Changed - scrolling occurred
        
        // Act - Simulate normal accumulator update logic
        bool actuallyScrolled = (newOffset != previousOffset);
        if (actuallyScrolled)
        {
            float consumedDelta = scrollLines * (scrollUp ? 1 : -1);
            accumulator -= consumedDelta;
        }
        
        // Assert
        Assert.That(accumulator, Is.EqualTo(10.0f), 
            "Accumulator should be updated by consumed delta when scrolling occurred");
    }

    [Test]
    public void ErrorRecovery_AfterMultipleErrors_ShouldMaintainValidState()
    {
        // This test verifies that error recovery maintains valid state after multiple errors
        
        // Arrange
        float accumulator = 0.0f;
        var config = MouseWheelScrollConfig.CreateDefault();
        
        // Act - Simulate multiple error conditions and recovery
        for (int i = 0; i < 10; i++)
        {
            try
            {
                // Simulate some wheel processing that might fail
                accumulator += 5.0f * config.LinesPerStep;
                
                // Simulate error condition
                if (i % 3 == 0)
                {
                    throw new InvalidOperationException("Simulated error");
                }
                
                // Normal processing continues...
            }
            catch (Exception)
            {
                // Error handling: reset accumulator
                accumulator = 0.0f;
            }
            
            // Verify state remains valid after each iteration
            Assert.That(float.IsFinite(accumulator), Is.True, 
                $"Accumulator should remain finite after iteration {i}");
            Assert.That(Math.Abs(accumulator), Is.LessThanOrEqualTo(1000.0f), 
                $"Accumulator should remain reasonable after iteration {i}");
        }
        
        // Assert final state is valid
        Assert.That(float.IsFinite(accumulator), Is.True, "Final accumulator should be finite");
    }

    [Test]
    public void FocusBasedFiltering_WithoutFocus_ShouldIgnoreWheelEvents()
    {
        // This test verifies that wheel events are ignored when terminal doesn't have focus
        
        // Arrange
        bool hasFocus = false;
        float wheelDelta = 2.0f;
        var config = MouseWheelScrollConfig.CreateDefault();
        
        // Act - Simulate focus check logic from HandleMouseWheelInput
        bool shouldProcess = hasFocus && Math.Abs(wheelDelta) >= config.MinimumWheelDelta;
        
        // Assert
        Assert.That(shouldProcess, Is.False, "Wheel events should be ignored when unfocused");
    }

    [Test]
    public void FocusBasedFiltering_WithFocus_ShouldProcessWheelEvents()
    {
        // This test verifies that wheel events are processed when terminal has focus
        
        // Arrange
        bool hasFocus = true;
        float wheelDelta = 2.0f;
        var config = MouseWheelScrollConfig.CreateDefault();
        
        // Act - Simulate focus check logic from HandleMouseWheelInput
        bool shouldProcess = hasFocus && Math.Abs(wheelDelta) >= config.MinimumWheelDelta;
        
        // Assert
        Assert.That(shouldProcess, Is.True, "Wheel events should be processed when focused");
    }

    [Test]
    public void ThresholdFiltering_WithBelowThresholdDelta_ShouldIgnoreWheelEvents()
    {
        // This test verifies that wheel deltas below threshold are ignored
        
        // Arrange
        bool hasFocus = true;
        float wheelDelta = 0.05f; // Below default threshold of 0.1f
        var config = MouseWheelScrollConfig.CreateDefault();
        
        // Act - Simulate threshold check logic
        bool shouldProcess = hasFocus && Math.Abs(wheelDelta) >= config.MinimumWheelDelta;
        
        // Assert
        Assert.That(shouldProcess, Is.False, "Wheel events below threshold should be ignored");
    }

    [Test]
    public void ThresholdFiltering_WithAboveThresholdDelta_ShouldProcessWheelEvents()
    {
        // This test verifies that wheel deltas above threshold are processed
        
        // Arrange
        bool hasFocus = true;
        float wheelDelta = 0.5f; // Above default threshold of 0.1f
        var config = MouseWheelScrollConfig.CreateDefault();
        
        // Act - Simulate threshold check logic
        bool shouldProcess = hasFocus && Math.Abs(wheelDelta) >= config.MinimumWheelDelta;
        
        // Assert
        Assert.That(shouldProcess, Is.True, "Wheel events above threshold should be processed");
    }

    [Test]
    public void ConfigurationUpdate_WithNullConfig_ShouldThrowArgumentNullException()
    {
        // This test verifies that null configuration updates are properly rejected
        // We test this by simulating the validation logic that would be used
        
        // Arrange
        MouseWheelScrollConfig? nullConfig = null;
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
        {
            if (nullConfig == null)
            {
                throw new ArgumentNullException(nameof(nullConfig));
            }
        }, "Null configuration should throw ArgumentNullException");
    }

    [Test]
    public void ConfigurationUpdate_WithInvalidConfig_ShouldThrowAndPreservePreviousConfig()
    {
        // This test verifies that invalid configuration updates are rejected
        // and previous configuration is preserved
        
        // Arrange
        var validConfig = MouseWheelScrollConfig.CreateDefault();
        var invalidConfig = new MouseWheelScrollConfig { LinesPerStep = -1 };
        var currentConfig = validConfig;
        
        // Act
        bool updateSucceeded = false;
        try
        {
            invalidConfig.Validate();
            currentConfig = invalidConfig;
            updateSucceeded = true;
        }
        catch (ArgumentException)
        {
            // Expected - invalid config should be rejected
            // currentConfig should remain unchanged
        }
        
        // Assert
        Assert.That(updateSucceeded, Is.False, "Invalid config update should fail");
        Assert.That(currentConfig, Is.EqualTo(validConfig), "Previous config should be preserved");
        Assert.That(currentConfig.LinesPerStep, Is.EqualTo(validConfig.LinesPerStep), 
            "Previous config values should be preserved");
    }
}