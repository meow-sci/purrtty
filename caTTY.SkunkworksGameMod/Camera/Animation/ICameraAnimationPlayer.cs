using System.Collections.Generic;

namespace caTTY.SkunkworksGameMod.Camera.Animation;

/// <summary>
/// Interface for camera animation playback.
/// Manages keyframes, playback state, and frame interpolation.
/// </summary>
public interface ICameraAnimationPlayer
{
    /// <summary>
    /// Whether the animation is currently playing.
    /// </summary>
    bool IsPlaying { get; }

    /// <summary>
    /// Current playback time in seconds.
    /// </summary>
    float CurrentTime { get; }

    /// <summary>
    /// Total animation duration in seconds.
    /// </summary>
    float Duration { get; }

    /// <summary>
    /// Read-only list of all keyframes.
    /// </summary>
    IReadOnlyList<CameraKeyframe> Keyframes { get; }

    /// <summary>
    /// Sets the keyframes for this animation.
    /// Automatically sorts by timestamp.
    /// </summary>
    /// <param name="keyframes">Keyframes to use.</param>
    void SetKeyframes(IEnumerable<CameraKeyframe> keyframes);

    /// <summary>
    /// Clears all keyframes and stops playback.
    /// </summary>
    void ClearKeyframes();

    /// <summary>
    /// Starts or resumes playback.
    /// Requires at least 2 keyframes.
    /// </summary>
    void Play();

    /// <summary>
    /// Stops playback and resets time to 0.
    /// </summary>
    void Stop();

    /// <summary>
    /// Updates the animation and returns the current interpolated frame.
    /// Should be called each frame with delta time.
    /// </summary>
    /// <param name="deltaTime">Time since last frame in seconds.</param>
    /// <returns>Interpolated camera frame, or null if not playing.</returns>
    CameraAnimationFrame? Update(double deltaTime);
}
