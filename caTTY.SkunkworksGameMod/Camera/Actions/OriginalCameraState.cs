using Brutal.Numerics;

namespace caTTY.SkunkworksGameMod.Camera.Actions;

/// <summary>
/// Captures the original camera state before an animation starts.
/// Used for end lerp to restore camera to its original position.
/// </summary>
public sealed class OriginalCameraState
{
    /// <summary>
    /// Original position offset from the target.
    /// </summary>
    public double3 Offset { get; init; }

    /// <summary>
    /// Original yaw rotation in degrees.
    /// </summary>
    public float Yaw { get; init; }

    /// <summary>
    /// Original pitch rotation in degrees.
    /// </summary>
    public float Pitch { get; init; }

    /// <summary>
    /// Original roll rotation in degrees.
    /// </summary>
    public float Roll { get; init; }

    /// <summary>
    /// Original field of view in degrees.
    /// </summary>
    public float Fov { get; init; }

    public OriginalCameraState(double3 offset, float yaw, float pitch, float roll, float fov)
    {
        Offset = offset;
        Yaw = yaw;
        Pitch = pitch;
        Roll = roll;
        Fov = fov;
    }
}
