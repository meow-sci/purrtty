using Brutal.Numerics;

namespace caTTY.SkunkworksGameMod.Camera.Animation;

/// <summary>
/// Represents a single keyframe in a camera animation.
/// Contains position offset (relative to target), rotation (YPR), and field of view.
/// </summary>
public sealed class CameraKeyframe
{
    /// <summary>
    /// Timestamp of this keyframe in seconds from animation start.
    /// </summary>
    public float Timestamp { get; init; }

    /// <summary>
    /// Position offset relative to the followed target in ECL coordinates.
    /// </summary>
    public double3 Offset { get; init; }

    /// <summary>
    /// Yaw rotation in degrees (around Z axis).
    /// </summary>
    public float Yaw { get; init; }

    /// <summary>
    /// Pitch rotation in degrees (around X axis).
    /// </summary>
    public float Pitch { get; init; }

    /// <summary>
    /// Roll rotation in degrees (around Y axis).
    /// </summary>
    public float Roll { get; init; }

    /// <summary>
    /// Field of view in degrees.
    /// </summary>
    public float Fov { get; init; }

    /// <summary>
    /// Optional debug label for this keyframe.
    /// </summary>
    public string? DebugLabel { get; init; }

    public CameraKeyframe(float timestamp, double3 offset, float yaw, float pitch, float roll, float fov, string? debugLabel = null)
    {
        Timestamp = timestamp;
        Offset = offset;
        Yaw = yaw;
        Pitch = pitch;
        Roll = roll;
        Fov = fov;
        DebugLabel = debugLabel;
    }
}
