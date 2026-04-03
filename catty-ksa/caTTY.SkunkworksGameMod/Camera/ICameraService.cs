using Brutal.Numerics;

namespace caTTY.SkunkworksGameMod.Camera;

/// <summary>
/// Defines how to exit manual follow mode.
/// </summary>
public enum ManualFollowExitMode
{
    /// <summary>
    /// Keep the current camera offset and avoid snapping.
    /// Camera continues following with the current offset.
    /// </summary>
    KeepCurrentOffset,

    /// <summary>
    /// Restore native KSA follow behavior with default offset.
    /// Attempts to call SetFollow with native flags.
    /// </summary>
    RestoreNativeFollow,
}

/// <summary>
/// Abstraction over KSA camera access and manipulation.
/// Provides a clean interface for camera operations without exposing KSA internals.
/// </summary>
public interface ICameraService
{
    /// <summary>
    /// Whether the camera is available and can be controlled.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Camera position in ECL (Ecliptic) coordinates.
    /// </summary>
    double3 Position { get; set; }

    /// <summary>
    /// Camera rotation as a quaternion in world space.
    /// </summary>
    doubleQuat Rotation { get; set; }

    /// <summary>
    /// Field of view in degrees.
    /// </summary>
    float FieldOfView { get; set; }

    /// <summary>
    /// Camera forward direction vector (normalized).
    /// </summary>
    double3 Forward { get; }

    /// <summary>
    /// Camera right direction vector (normalized).
    /// </summary>
    double3 Right { get; }

    /// <summary>
    /// Camera up direction vector (normalized).
    /// </summary>
    double3 Up { get; }

    /// <summary>
    /// The object the camera is currently following (or null).
    /// </summary>
    object? FollowTarget { get; }

    /// <summary>
    /// Gets the position of the current follow target.
    /// </summary>
    /// <returns>Target position in ECL coordinates.</returns>
    double3 GetTargetPosition();

    /// <summary>
    /// Starts manual follow mode with a specific offset from the target.
    /// This unfollows the target in KSA but maintains tracking manually.
    /// </summary>
    /// <param name="offset">Offset from target in ECL coordinates.</param>
    void StartManualFollow(double3 offset);

    /// <summary>
    /// Updates the follow offset during manual follow mode.
    /// This should be called to sync the offset with animation frames before calling StopManualFollow.
    /// </summary>
    /// <param name="offset">New offset from target in ECL coordinates.</param>
    void UpdateFollowOffset(double3 offset);

    /// <summary>
    /// Stops manual follow mode.
    /// </summary>
    void StopManualFollow();

    /// <summary>
    /// Exits manual follow mode with explicit behavior control.
    /// </summary>
    /// <param name="mode">How to exit manual follow mode.</param>
    /// <param name="unknown0">Optional native SetFollow flag (behavior unknown).</param>
    /// <param name="changeControl">Optional flag to change camera control.</param>
    /// <param name="alert">Optional flag to show alert.</param>
    /// <returns>True if exit succeeded; false if operation failed (e.g., wrong target type for native restore).</returns>
    bool ExitManualFollow(
        ManualFollowExitMode mode,
        bool? unknown0 = null,
        bool? changeControl = null,
        bool? alert = null);

    /// <summary>
    /// Whether manual follow mode is active.
    /// </summary>
    bool IsManualFollowing { get; }

    /// <summary>
    /// Gets whether the camera is currently following an object in native KSA follow mode.
    /// </summary>
    bool IsFollowing { get; }

    /// <summary>
    /// Starts following the current follow target using native KSA follow mode.
    /// Restores the camera to standard following behavior with default offset.
    /// </summary>
    /// <returns>True if successfully started following, false if no target available.</returns>
    bool StartFollowing();

    /// <summary>
    /// Attempts native KSA SetFollow using the provided flags.
    /// This exists specifically to explore the meaning of SetFollow's boolean parameters.
    /// </summary>
    bool TryStartFollowingWithOptions(bool unknown0, bool changeControl, bool alert);

    /// <summary>
    /// Stops following and enters free camera mode.
    /// Camera position and rotation become fully manual.
    /// </summary>
    void EnterFreeCameraMode();

    /// <summary>
    /// Gets the current camera control mode as a human-readable string.
    /// E.g., "Following", "Free Camera", "Manual Follow"
    /// </summary>
    string GetCurrentMode();

    /// <summary>
    /// Best-effort debug string describing KSA's internal camera controller/control mode.
    /// Returns null if not discoverable via reflection.
    /// </summary>
    string? GetNativeControlModeDebug();

    /// <summary>
    /// Orients the camera to look at a target position.
    /// </summary>
    /// <param name="target">Target position in ECL coordinates.</param>
    void LookAt(double3 target);

    /// <summary>
    /// Applies yaw/pitch/roll rotation to the camera.
    /// </summary>
    /// <param name="yaw">Yaw in degrees (around Z axis).</param>
    /// <param name="pitch">Pitch in degrees (around X axis).</param>
    /// <param name="roll">Roll in degrees (around Y axis).</param>
    void ApplyRotation(float yaw, float pitch, float roll);

    /// <summary>
    /// Updates the camera service (should be called each frame).
    /// Handles manual follow position updates.
    /// </summary>
    /// <param name="deltaTime">Time since last frame in seconds.</param>
    void Update(double deltaTime);
}
