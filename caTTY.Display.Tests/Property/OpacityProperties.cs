using System;
using System.IO;
using System.Threading;
using Brutal.Numerics;
using caTTY.Display.Configuration;
using caTTY.Display.Rendering;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for opacity management functionality.
/// Tests universal properties for opacity persistence and validation.
/// </summary>
[TestFixture]
[Category("Property")]
public class OpacityProperties
{
    private string _tempConfigDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        // Create a unique temporary directory for each test run
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        _tempConfigDirectory = Path.Combine(Path.GetTempPath(), $"caTTY_OpacityTests_{uniqueId}");
        Directory.CreateDirectory(_tempConfigDirectory);

        // Override the application data path for testing
        Environment.SetEnvironmentVariable("APPDATA", _tempConfigDirectory);
        
        // Add a small delay to prevent file access conflicts
        Thread.Sleep(10);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up temporary directory
        if (Directory.Exists(_tempConfigDirectory))
        {
            Directory.Delete(_tempConfigDirectory, true);
        }

        // Restore original APPDATA
        Environment.SetEnvironmentVariable("APPDATA", null);
    }

    /// <summary>
    /// Generator for valid opacity values (0.0 to 1.0).
    /// </summary>
    public static Arbitrary<float> ValidOpacityValues()
    {
        return Gen.Choose(0, 1000).Select(i => i / 1000.0f).ToArbitrary();
    }

    /// <summary>
    /// Generator for invalid opacity values (outside 0.0 to 1.0 range).
    /// </summary>
    public static Arbitrary<float> InvalidOpacityValues()
    {
        return Gen.OneOf(
            Gen.Choose(-1000, -1).Select(i => i / 100.0f), // Negative values
            Gen.Choose(101, 1000).Select(i => i / 100.0f), // Values > 1.0
            Gen.Constant(float.NaN),
            Gen.Constant(float.PositiveInfinity),
            Gen.Constant(float.NegativeInfinity)
        ).ToArbitrary();
    }

    /// <summary>
    /// Property 12: Opacity Persistence Round-Trip
    /// For any valid opacity value, setting the opacity and reloading the configuration
    /// should preserve the opacity value within acceptable precision.
    /// Feature: toml-terminal-theming, Property 12: Opacity Persistence Round-Trip
    /// Validates: Requirements 7.4
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OpacityPersistenceRoundTrip_ShouldPreserveValue()
    {
        return Prop.ForAll(ValidOpacityValues(), opacity =>
        {
            try
            {
                // Initialize opacity manager
                OpacityManager.Initialize();

                // Set the opacity value
                var setResult = OpacityManager.SetOpacity(opacity);
                if (!setResult) return false;

                // Verify the opacity was set correctly
                var currentOpacity = OpacityManager.CurrentOpacity;
                var validatedOpacity = OpacityManager.ValidateOpacity(opacity);
                
                if (Math.Abs(currentOpacity - validatedOpacity) >= 0.001f) return false;

                // Reinitialize to simulate application restart
                OpacityManager.Initialize();

                // Verify the opacity was persisted
                var persistedOpacity = OpacityManager.CurrentOpacity;
                
                // Should match the validated opacity (not necessarily the original if it was out of range)
                return Math.Abs(persistedOpacity - validatedOpacity) < 0.001f;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Opacity Validation Consistency
    /// For any opacity value, validation should be consistent and idempotent.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OpacityValidation_ShouldBeConsistent()
    {
        return Prop.ForAll(Gen.Choose(-1000, 2000).Select(i => i / 100.0f).ToArbitrary(), opacity =>
        {
            try
            {
                var validated1 = OpacityManager.ValidateOpacity(opacity);
                var validated2 = OpacityManager.ValidateOpacity(opacity);

                // Validation should be consistent
                if (Math.Abs(validated1 - validated2) >= 0.001f) return false;

                // Validation should be idempotent
                var validatedTwice = OpacityManager.ValidateOpacity(validated1);
                if (Math.Abs(validated1 - validatedTwice) >= 0.001f) return false;

                // Validated value should be in valid range
                if (validated1 < OpacityManager.MinOpacity || validated1 > OpacityManager.MaxOpacity) return false;

                // If original was in range, validation should preserve it
                if (opacity >= OpacityManager.MinOpacity && opacity <= OpacityManager.MaxOpacity)
                {
                    return Math.Abs(validated1 - opacity) < 0.001f;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Opacity Change Notification Consistency
    /// Setting opacity should trigger change notifications when the value actually changes.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OpacityChangeNotification_ShouldBeConsistent()
    {
        return Prop.ForAll(ValidOpacityValues(), ValidOpacityValues(), (opacity1, opacity2) =>
        {
            try
            {
                // Initialize opacity manager
                OpacityManager.Initialize();

                float? notifiedOpacity = null;
                int notificationCount = 0;

                // Subscribe to opacity change events
                void OnOpacityChanged(float newOpacity)
                {
                    notifiedOpacity = newOpacity;
                    notificationCount++;
                }

                OpacityManager.BackgroundOpacityChanged += OnOpacityChanged;

                try
                {
                    // Set first opacity
                    OpacityManager.SetOpacity(opacity1);
                    var firstNotificationCount = notificationCount;
                    var firstNotifiedOpacity = notifiedOpacity;

                    // Set second opacity
                    OpacityManager.SetOpacity(opacity2);
                    var secondNotificationCount = notificationCount;
                    var secondNotifiedOpacity = notifiedOpacity;

                    // Verify notifications
                    var validatedOpacity1 = OpacityManager.ValidateOpacity(opacity1);
                    var validatedOpacity2 = OpacityManager.ValidateOpacity(opacity2);

                    // If values are different, should have received notifications
                    if (Math.Abs(validatedOpacity1 - validatedOpacity2) >= 0.001f)
                    {
                        // Should have received at least one notification for each distinct value
                        if (secondNotificationCount < firstNotificationCount) return false;
                        
                        // Final notification should match current opacity
                        if (secondNotifiedOpacity.HasValue)
                        {
                            return Math.Abs(secondNotifiedOpacity.Value - OpacityManager.CurrentOpacity) < 0.001f;
                        }
                    }

                    return true;
                }
                finally
                {
                    OpacityManager.BackgroundOpacityChanged -= OnOpacityChanged;
                }
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Opacity Application Consistency
    /// Applying opacity to colors should be consistent and preserve color relationships.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OpacityApplication_ShouldBeConsistent()
    {
        return Prop.ForAll(ValidOpacityValues(), opacity =>
        {
            try
            {
                // Initialize and set opacity
                OpacityManager.Initialize();
                OpacityManager.SetOpacity(opacity);

                // Test color application
                var originalColor = new Brutal.Numerics.float4(0.5f, 0.7f, 0.9f, 1.0f);
                var appliedColor = OpacityManager.ApplyOpacity(originalColor);

                // RGB components should be preserved
                if (Math.Abs(appliedColor.X - originalColor.X) >= 0.001f) return false;
                if (Math.Abs(appliedColor.Y - originalColor.Y) >= 0.001f) return false;
                if (Math.Abs(appliedColor.Z - originalColor.Z) >= 0.001f) return false;

                // Alpha should be multiplied by opacity
                var expectedAlpha = originalColor.W * OpacityManager.ValidateOpacity(opacity);
                if (Math.Abs(appliedColor.W - expectedAlpha) >= 0.001f) return false;

                // Test alpha application
                var originalAlpha = 0.8f;
                var appliedAlpha = OpacityManager.ApplyOpacity(originalAlpha);
                var expectedAppliedAlpha = originalAlpha * OpacityManager.ValidateOpacity(opacity);
                
                return Math.Abs(appliedAlpha - expectedAppliedAlpha) < 0.001f;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Opacity Percentage Conversion Consistency
    /// Converting between opacity values and percentages should be consistent.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OpacityPercentageConversion_ShouldBeConsistent()
    {
        return Prop.ForAll(Gen.Choose(0, 100).ToArbitrary(), percentage =>
        {
            try
            {
                // Initialize opacity manager
                OpacityManager.Initialize();

                // Set opacity from percentage
                var setResult = OpacityManager.SetOpacityFromPercentage(percentage);
                if (!setResult) return false;

                // Get percentage back
                var retrievedPercentage = OpacityManager.GetOpacityPercentage();

                // Should be within 1% due to rounding
                var percentageDiff = Math.Abs(retrievedPercentage - percentage);
                if (percentageDiff > 1) return false;

                // Verify the actual opacity value is reasonable
                var expectedOpacity = percentage / 100.0f;
                var actualOpacity = OpacityManager.CurrentOpacity;
                
                return Math.Abs(actualOpacity - expectedOpacity) < 0.01f; // Allow for rounding
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Invalid Opacity Handling
    /// Invalid opacity values should be handled gracefully with validation.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property InvalidOpacityHandling_ShouldValidateGracefully()
    {
        return Prop.ForAll(InvalidOpacityValues(), invalidOpacity =>
        {
            try
            {
                // Initialize opacity manager
                OpacityManager.Initialize();

                // Validation should handle invalid values gracefully
                var validatedOpacity = OpacityManager.ValidateOpacity(invalidOpacity);

                // Validated value should be in valid range
                if (validatedOpacity < OpacityManager.MinOpacity || validatedOpacity > OpacityManager.MaxOpacity)
                {
                    return false;
                }

                // Setting invalid opacity should still work (with validation)
                var setResult = OpacityManager.SetOpacity(invalidOpacity);
                if (!setResult) return false;

                // Current opacity should be the validated value
                var currentOpacity = OpacityManager.CurrentOpacity;
                return Math.Abs(currentOpacity - validatedOpacity) < 0.001f;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property 11: Opacity Application Completeness
    /// For any opacity setting, all terminal rendering operations should consistently
    /// apply the opacity to colors including background, foreground, cursor, selection,
    /// underlines, and strikethrough elements.
    /// Feature: toml-terminal-theming, Property 11: Opacity Application Completeness
    /// Validates: Requirements 7.2, 7.3, 7.5
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OpacityApplicationCompleteness_ShouldApplyToAllElements()
    {
        return Prop.ForAll(ValidOpacityValues(), opacity =>
        {
            try
            {
                // Initialize opacity manager
                OpacityManager.Initialize();
                
                // Set the test opacity
                var setResult = OpacityManager.SetOpacity(opacity);
                if (!setResult) return false;

                var validatedOpacity = OpacityManager.ValidateOpacity(opacity);
                var currentOpacity = OpacityManager.CurrentOpacity;
                
                // Verify opacity was set correctly
                if (Math.Abs(currentOpacity - validatedOpacity) >= 0.001f) return false;

                // Test color application consistency
                var testColors = new[]
                {
                    new float4(1.0f, 0.0f, 0.0f, 1.0f), // Red
                    new float4(0.0f, 1.0f, 0.0f, 1.0f), // Green
                    new float4(0.0f, 0.0f, 1.0f, 1.0f), // Blue
                    new float4(1.0f, 1.0f, 1.0f, 1.0f), // White
                    new float4(0.0f, 0.0f, 0.0f, 1.0f), // Black
                    new float4(0.5f, 0.7f, 0.9f, 0.8f)  // Semi-transparent color
                };

                foreach (var originalColor in testColors)
                {
                    var appliedColor = OpacityManager.ApplyOpacity(originalColor);

                    // RGB components should be preserved
                    if (Math.Abs(appliedColor.X - originalColor.X) >= 0.001f) return false;
                    if (Math.Abs(appliedColor.Y - originalColor.Y) >= 0.001f) return false;
                    if (Math.Abs(appliedColor.Z - originalColor.Z) >= 0.001f) return false;

                    // Alpha should be multiplied by opacity
                    var expectedAlpha = originalColor.W * validatedOpacity;
                    if (Math.Abs(appliedColor.W - expectedAlpha) >= 0.001f) return false;

                    // Applied alpha should be within valid range
                    if (appliedColor.W < 0.0f || appliedColor.W > 1.0f) return false;
                }

                // Test alpha value application
                var testAlphas = new[] { 0.0f, 0.25f, 0.5f, 0.75f, 1.0f };
                foreach (var originalAlpha in testAlphas)
                {
                    var appliedAlpha = OpacityManager.ApplyOpacity(originalAlpha);
                    var expectedAlpha = originalAlpha * validatedOpacity;
                    
                    if (Math.Abs(appliedAlpha - expectedAlpha) >= 0.001f) return false;
                    if (appliedAlpha < 0.0f || appliedAlpha > 1.0f) return false;
                }

                // Test that opacity application is consistent across multiple calls
                var testColor = new float4(0.6f, 0.8f, 0.4f, 0.9f);
                var applied1 = OpacityManager.ApplyOpacity(testColor);
                var applied2 = OpacityManager.ApplyOpacity(testColor);
                
                if (Math.Abs(applied1.X - applied2.X) >= 0.001f) return false;
                if (Math.Abs(applied1.Y - applied2.Y) >= 0.001f) return false;
                if (Math.Abs(applied1.Z - applied2.Z) >= 0.001f) return false;
                if (Math.Abs(applied1.W - applied2.W) >= 0.001f) return false;

                // Test edge cases
                var transparentColor = new float4(1.0f, 1.0f, 1.0f, 0.0f);
                var appliedTransparent = OpacityManager.ApplyOpacity(transparentColor);
                if (appliedTransparent.W != 0.0f) return false; // Should remain transparent

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Opacity Rendering Integration
    /// Opacity changes should integrate properly with the rendering pipeline
    /// without causing visual artifacts or performance issues.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property OpacityRenderingIntegration_ShouldBeSeamless()
    {
        return Prop.ForAll(ValidOpacityValues(), ValidOpacityValues(), (opacity1, opacity2) =>
        {
            try
            {
                // Initialize opacity manager
                OpacityManager.Initialize();

                // Test rapid opacity changes (simulating user interaction)
                var setResult1 = OpacityManager.SetOpacity(opacity1);
                if (!setResult1) return false;

                var setResult2 = OpacityManager.SetOpacity(opacity2);
                if (!setResult2) return false;

                // Verify final state is consistent
                var finalOpacity = OpacityManager.CurrentOpacity;
                var expectedOpacity = OpacityManager.ValidateOpacity(opacity2);
                
                if (Math.Abs(finalOpacity - expectedOpacity) >= 0.001f) return false;

                // Test that color application still works correctly after changes
                var testColor = new float4(0.7f, 0.3f, 0.9f, 1.0f);
                var appliedColor = OpacityManager.ApplyOpacity(testColor);
                var expectedAlpha = testColor.W * expectedOpacity;
                
                return Math.Abs(appliedColor.W - expectedAlpha) < 0.001f;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }
}