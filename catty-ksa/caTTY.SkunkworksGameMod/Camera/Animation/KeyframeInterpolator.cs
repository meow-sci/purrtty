using Brutal.Numerics;

namespace caTTY.SkunkworksGameMod.Camera.Animation;

/// <summary>
/// Provides Catmull-Rom spline interpolation for smooth keyframe animation.
/// </summary>
public static class KeyframeInterpolator
{
    /// <summary>
    /// Catmull-Rom spline interpolation for double3 vectors.
    /// Provides smooth in/out transitions across all keyframes.
    /// </summary>
    /// <param name="p0">Control point before segment start.</param>
    /// <param name="p1">Segment start point.</param>
    /// <param name="p2">Segment end point.</param>
    /// <param name="p3">Control point after segment end.</param>
    /// <param name="t">Interpolation factor (0 to 1) within segment p1-p2.</param>
    /// <returns>Interpolated position.</returns>
    public static double3 CatmullRom(double3 p0, double3 p1, double3 p2, double3 p3, float t)
    {
        double t2 = t * t;
        double t3 = t2 * t;

        return 0.5 * (
            (2.0 * p1) +
            (-p0 + p2) * t +
            (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2 +
            (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3
        );
    }

    /// <summary>
    /// Catmull-Rom spline interpolation for float values.
    /// Provides smooth in/out transitions across all keyframes.
    /// </summary>
    /// <param name="p0">Control point before segment start.</param>
    /// <param name="p1">Segment start point.</param>
    /// <param name="p2">Segment end point.</param>
    /// <param name="p3">Control point after segment end.</param>
    /// <param name="t">Interpolation factor (0 to 1) within segment p1-p2.</param>
    /// <returns>Interpolated value.</returns>
    public static float CatmullRomFloat(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2.0f * p1) +
            (-p0 + p2) * t +
            (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
            (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
        );
    }
}
