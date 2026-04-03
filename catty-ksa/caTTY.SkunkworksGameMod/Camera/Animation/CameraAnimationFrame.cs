using Brutal.Numerics;

namespace caTTY.SkunkworksGameMod.Camera.Animation;

/// <summary>
/// Represents an interpolated animation frame returned by the animation player.
/// </summary>
public readonly struct CameraAnimationFrame
{
    /// <summary>
    /// Position offset relative to the followed target.
    /// </summary>
    public double3 Offset { get; init; }

    /// <summary>
    /// Yaw rotation in degrees.
    /// </summary>
    public float Yaw { get; init; }

    /// <summary>
    /// Pitch rotation in degrees.
    /// </summary>
    public float Pitch { get; init; }

    /// <summary>
    /// Roll rotation in degrees.
    /// </summary>
    public float Roll { get; init; }

    /// <summary>
    /// Field of view in degrees.
    /// </summary>
    public float Fov { get; init; }
}
