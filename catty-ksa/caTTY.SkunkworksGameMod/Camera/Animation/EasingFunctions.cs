using System;

namespace caTTY.SkunkworksGameMod.Camera.Animation;

/// <summary>
/// Types of easing functions for animation timing.
/// </summary>
public enum EasingType
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut
}

/// <summary>
/// Provides easing functions for smooth animation timing curves.
/// </summary>
public static class EasingFunctions
{
    /// <summary>
    /// Applies an easing function to a linear progress value (0 to 1).
    /// </summary>
    /// <param name="t">Linear progress from 0 to 1.</param>
    /// <param name="easing">Type of easing to apply.</param>
    /// <returns>Eased progress value.</returns>
    public static float ApplyEasing(float t, EasingType easing)
    {
        return easing switch
        {
            EasingType.Linear => t,
            EasingType.EaseIn => t * t,
            EasingType.EaseOut => t * (2 - t),
            EasingType.EaseInOut => t < 0.5f ? 2 * t * t : -1 + (4 - 2 * t) * t,
            _ => t
        };
    }
}
