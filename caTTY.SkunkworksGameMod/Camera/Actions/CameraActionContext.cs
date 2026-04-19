using Brutal.Numerics;
using caTTY.SkunkworksGameMod.Camera.Animation;

namespace caTTY.SkunkworksGameMod.Camera.Actions;

/// <summary>
/// Shared context for camera action execution.
/// Contains camera state and action parameters.
/// </summary>
public sealed class CameraActionContext
{
    /// <summary>
    /// Camera service for accessing and manipulating the camera.
    /// </summary>
    public required ICameraService Camera { get; init; }

    /// <summary>
    /// Position of the target being followed (in ECL coordinates).
    /// </summary>
    public required double3 TargetPosition { get; init; }

    /// <summary>
    /// Current camera offset from the target.
    /// </summary>
    public required double3 CurrentOffset { get; init; }

    /// <summary>
    /// Current field of view in degrees.
    /// </summary>
    public required float CurrentFov { get; init; }

    /// <summary>
    /// Current camera rotation quaternion.
    /// </summary>
    public required doubleQuat CurrentRotation { get; init; }

    /// <summary>
    /// Animation duration in seconds.
    /// </summary>
    public float Duration { get; init; }

    /// <summary>
    /// Distance parameter (usage varies by action).
    /// For orbit: orbit radius in meters.
    /// </summary>
    public float Distance { get; init; }

    /// <summary>
    /// Original camera state before animation started (for end lerp restoration).
    /// </summary>
    public OriginalCameraState? OriginalState { get; init; }

    /// <summary>
    /// Whether to smoothly lerp to the start position before beginning the main animation.
    /// </summary>
    public bool UseStartLerp { get; init; }

    /// <summary>
    /// Duration of the start lerp phase in seconds (if UseStartLerp is true).
    /// </summary>
    public float StartLerpTime { get; init; }

    /// <summary>
    /// Easing function for the start lerp.
    /// </summary>
    public EasingType StartLerpEasing { get; init; } = EasingType.EaseInOut;

    /// <summary>
    /// Whether to smoothly lerp back to original position after the main animation.
    /// </summary>
    public bool UseEndLerp { get; init; }

    /// <summary>
    /// Duration of the end lerp phase in seconds (if UseEndLerp is true).
    /// </summary>
    public float EndLerpTime { get; init; }

    /// <summary>
    /// Easing function for the end lerp.
    /// </summary>
    public EasingType EndLerpEasing { get; init; } = EasingType.EaseInOut;

    /// <summary>
    /// Easing function to apply to the main animation.
    /// </summary>
    public EasingType Easing { get; init; } = EasingType.EaseInOut;

    /// <summary>
    /// Whether to orbit counter-clockwise (for orbit action).
    /// </summary>
    public bool CounterClockwise { get; init; }
}
