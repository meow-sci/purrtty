using System;

namespace caTTY.Display.Configuration;

/// <summary>
/// Configuration class that encapsulates all mouse wheel scrolling settings for the terminal controller.
/// Provides configurable scroll sensitivity, smooth scrolling behavior, and input filtering options.
/// </summary>
public class MouseWheelScrollConfig
{
    /// <summary>
    /// Gets or sets the number of lines to scroll per mouse wheel step.
    /// </summary>
    /// <value>The number of lines to scroll per wheel step. Must be between 1 and 10. Defaults to 3.</value>
    public int LinesPerStep { get; set; } = 3;

    /// <summary>
    /// Gets or sets whether to enable smooth scrolling with fractional accumulation.
    /// When enabled, fractional wheel deltas are accumulated until they reach a full line scroll.
    /// </summary>
    /// <value>True to enable smooth scrolling, false for discrete scrolling. Defaults to true.</value>
    public bool EnableSmoothScrolling { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum wheel delta required to trigger scrolling.
    /// This prevents micro-movements from causing unwanted scrolling.
    /// </summary>
    /// <value>The minimum wheel delta threshold. Must be between 0.01 and 1.0. Defaults to 0.1.</value>
    public float MinimumWheelDelta { get; set; } = 0.1f;

    /// <summary>
    /// Gets or sets the maximum lines to scroll in a single operation.
    /// This prevents excessive jumps when processing rapid wheel events.
    /// </summary>
    /// <value>The maximum lines per operation. Must be between 1 and 50. Defaults to 10.</value>
    public int MaxLinesPerOperation { get; set; } = 10;

    /// <summary>
    /// Creates a mouse wheel scroll configuration optimized for TestApp development context.
    /// Uses development-friendly defaults with moderate sensitivity for testing.
    /// </summary>
    /// <returns>A MouseWheelScrollConfig instance configured for TestApp usage.</returns>
    public static MouseWheelScrollConfig CreateForTestApp()
    {
        return new MouseWheelScrollConfig
        {
            LinesPerStep = 3,
            EnableSmoothScrolling = true,
            MinimumWheelDelta = 0.1f,
            MaxLinesPerOperation = 10
        };
    }

    /// <summary>
    /// Creates a mouse wheel scroll configuration optimized for GameMod context.
    /// Uses game-appropriate defaults with slightly higher sensitivity for game integration.
    /// </summary>
    /// <returns>A MouseWheelScrollConfig instance configured for GameMod usage.</returns>
    public static MouseWheelScrollConfig CreateForGameMod()
    {
        return new MouseWheelScrollConfig
        {
            LinesPerStep = 5,
            EnableSmoothScrolling = true,
            MinimumWheelDelta = 0.05f,
            MaxLinesPerOperation = 15
        };
    }

    /// <summary>
    /// Creates a default mouse wheel scroll configuration with balanced settings.
    /// Suitable for most use cases with moderate sensitivity and smooth scrolling enabled.
    /// </summary>
    /// <returns>A MouseWheelScrollConfig instance with default settings.</returns>
    public static MouseWheelScrollConfig CreateDefault()
    {
        return new MouseWheelScrollConfig
        {
            LinesPerStep = 3,
            EnableSmoothScrolling = true,
            MinimumWheelDelta = 0.1f,
            MaxLinesPerOperation = 10
        };
    }

    /// <summary>
    /// Validates the mouse wheel scroll configuration and ensures all properties are within acceptable bounds.
    /// Performs range checking for all numeric properties to prevent invalid configurations.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when any property is outside valid bounds.</exception>
    public void Validate()
    {
        if (LinesPerStep < 1 || LinesPerStep > 10)
        {
            throw new ArgumentException($"LinesPerStep must be between 1 and 10, but was {LinesPerStep}", nameof(LinesPerStep));
        }

        if (MinimumWheelDelta < 0.01f || MinimumWheelDelta > 1.0f)
        {
            throw new ArgumentException($"MinimumWheelDelta must be between 0.01 and 1.0, but was {MinimumWheelDelta}", nameof(MinimumWheelDelta));
        }

        if (MaxLinesPerOperation < 1 || MaxLinesPerOperation > 50)
        {
            throw new ArgumentException($"MaxLinesPerOperation must be between 1 and 50, but was {MaxLinesPerOperation}", nameof(MaxLinesPerOperation));
        }

        // Ensure MaxLinesPerOperation is at least as large as LinesPerStep
        if (MaxLinesPerOperation < LinesPerStep)
        {
            throw new ArgumentException($"MaxLinesPerOperation ({MaxLinesPerOperation}) must be at least as large as LinesPerStep ({LinesPerStep})", nameof(MaxLinesPerOperation));
        }
    }

    /// <summary>
    /// Creates a copy of this configuration with the specified modifications.
    /// This method provides a fluent interface for creating configuration variants.
    /// </summary>
    /// <param name="linesPerStep">Optional new lines per step. If null, uses current value.</param>
    /// <param name="enableSmoothScrolling">Optional new smooth scrolling setting. If null, uses current value.</param>
    /// <param name="minimumWheelDelta">Optional new minimum wheel delta. If null, uses current value.</param>
    /// <param name="maxLinesPerOperation">Optional new maximum lines per operation. If null, uses current value.</param>
    /// <returns>A new MouseWheelScrollConfig with the specified modifications.</returns>
    public MouseWheelScrollConfig WithModifications(
        int? linesPerStep = null,
        bool? enableSmoothScrolling = null,
        float? minimumWheelDelta = null,
        int? maxLinesPerOperation = null)
    {
        return new MouseWheelScrollConfig
        {
            LinesPerStep = linesPerStep ?? LinesPerStep,
            EnableSmoothScrolling = enableSmoothScrolling ?? EnableSmoothScrolling,
            MinimumWheelDelta = minimumWheelDelta ?? MinimumWheelDelta,
            MaxLinesPerOperation = maxLinesPerOperation ?? MaxLinesPerOperation
        };
    }

    /// <summary>
    /// Returns a string representation of this configuration for debugging purposes.
    /// </summary>
    /// <returns>A formatted string containing all configuration values.</returns>
    public override string ToString()
    {
        return $"MouseWheelScrollConfig {{ LinesPerStep={LinesPerStep}, " +
               $"EnableSmoothScrolling={EnableSmoothScrolling}, " +
               $"MinimumWheelDelta={MinimumWheelDelta:F2}, " +
               $"MaxLinesPerOperation={MaxLinesPerOperation} }}";
    }
}