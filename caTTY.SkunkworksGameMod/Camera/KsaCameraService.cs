using System;
using System.Linq;
using System.Reflection;
using Brutal.Numerics;
using KSA;

namespace caTTY.SkunkworksGameMod.Camera;

/// <summary>
/// Options for calling KSA.Camera.SetFollow with boolean flags.
/// Enables systematic experimentation of camera control behavior.
/// </summary>
public readonly record struct SetFollowOptions(
    bool Unknown0,
    bool ChangeControl,
    bool Alert)
{
    /// <summary>
    /// Default options: Unknown0=true, ChangeControl=true, Alert=false
    /// (Based on observed KSA behavior)
    /// </summary>
    public static readonly SetFollowOptions Default = new(true, true, false);
}

/// <summary>
/// KSA-specific camera service implementation using reflection.
/// Handles camera access, manual follow mode, and rotation/position updates.
/// </summary>
public class KsaCameraService : ICameraService
{
    private KSA.Camera? _camera;
    private dynamic? _followedObject;
    private double3 _followOffset;
    private bool _isManualFollowing;

    public bool IsAvailable => GetCamera() != null;

    public double3 Position
    {
        get => GetCamera()?.PositionEcl ?? double3.Zero;
        set
        {
            var camera = GetCamera();
            if (camera != null)
            {
                camera.PositionEcl = value;
            }
        }
    }

    public doubleQuat Rotation
    {
        get => GetCamera()?.WorldRotation ?? new doubleQuat(0, 0, 0, 1);
        set
        {
            var camera = GetCamera();
            if (camera != null)
            {
                camera.WorldRotation = value;
            }
        }
    }

    public float FieldOfView
    {
        get
        {
            var camera = GetCamera();
            if (camera == null) return 60.0f;
            // GetFieldOfView returns radians, convert to degrees
            return camera.GetFieldOfView() * 57.2958f;
        }
        set
        {
            var camera = GetCamera();
            if (camera != null)
            {
                // SetFieldOfView accepts degrees
                camera.SetFieldOfView(value);
            }
        }
    }

    public double3 Forward => GetCamera()?.GetForward() ?? new double3(0, 1, 0);
    public double3 Right => GetCamera()?.GetRight() ?? new double3(1, 0, 0);
    public double3 Up => GetCamera()?.GetUp() ?? new double3(0, 0, 1);

    public object? FollowTarget => _isManualFollowing ? _followedObject : GetCamera()?.Following;

    public bool IsManualFollowing => _isManualFollowing;

    public bool IsFollowing
    {
        get
        {
            if (_isManualFollowing) return false; // Manual follow is distinct from native KSA follow
            var camera = GetCamera();
            return camera?.Following != null;
        }
    }

    public double3 GetTargetPosition()
    {
        var target = FollowTarget;
        if (target == null)
        {
            return Position;
        }

        try
        {
            // Call GetPositionEcl() on the dynamic target
            dynamic dynTarget = target;
            return dynTarget.GetPositionEcl();
        }
        catch
        {
            return Position;
        }
    }

    public bool StartFollowing()
    {
        var camera = GetCamera();
        if (camera == null) return false;

        // If we have a stored follow object from manual follow, use it
        var targetObject = _followedObject ?? camera.Following;
        if (targetObject == null) return false;

        // Clear manual follow state
        _isManualFollowing = false;
        _followedObject = null;

        // Call SetFollow with default parameters
        // Parameters: (object, bool, bool changeControl, bool alert)
        var options = new SetFollowOptions(false, false, false);
        var success = TrySetFollow(targetObject, options, out string? error);

        if (success)
        {
            Console.WriteLine($"[KsaCameraService] Started following in native KSA mode");
        }
        else
        {
            Console.WriteLine($"[KsaCameraService] Failed to start following: {error}");
        }

        return success;
    }

    public bool TryStartFollowingWithOptions(bool unknown0, bool changeControl, bool alert)
    {
        var camera = GetCamera();
        if (camera == null)
        {
            Console.WriteLine("[TryStartFollowingWithOptions] Camera not available");
            return false;
        }

        var target = FollowTarget;
        if (target == null)
        {
            Console.WriteLine("[TryStartFollowingWithOptions] No follow target available");
            return false;
        }

        // If in manual follow, exit it first
        if (_isManualFollowing)
        {
            _isManualFollowing = false;
            _followedObject = null;
            _followOffset = double3.Zero;
        }

        var options = new SetFollowOptions(unknown0, changeControl, alert);
        var success = TrySetFollow(target, options, out var error);

        if (!success)
        {
            Console.WriteLine($"[TryStartFollowingWithOptions] Failed: {error}");
        }

        return success;
    }

    public void EnterFreeCameraMode()
    {
        var camera = GetCamera();
        if (camera == null)
        {
            Console.WriteLine("[EnterFreeCameraMode] Camera not available");
            return;
        }

        // Exit manual follow if active
        if (_isManualFollowing)
        {
            _isManualFollowing = false;
            _followedObject = null;
            _followOffset = double3.Zero;
            Console.WriteLine("[EnterFreeCameraMode] Exited manual follow mode");
        }

        // Unfollow to enter free camera
        camera.Unfollow();
        Console.WriteLine("[EnterFreeCameraMode] Entered free camera mode");
    }

    public string GetCurrentMode()
    {
        if (_isManualFollowing)
        {
            return "Manual Follow";
        }

        var camera = GetCamera();
        if (camera == null)
        {
            return "Unavailable";
        }

        if (camera.Following != null)
        {
            return "Following";
        }

        return "Free Camera";
    }

    public void StartManualFollow(double3 offset)
    {
        var camera = GetCamera();
        if (camera == null) return;

        var currentFollowing = camera.Following;
        if (currentFollowing != null)
        {
            _followedObject = currentFollowing;
            _followOffset = offset;
            _isManualFollowing = true;
            camera.Unfollow();
        }
    }

    public void UpdateFollowOffset(double3 offset)
    {
        if (_isManualFollowing)
        {
            _followOffset = offset;
            // Removed per-frame logging to reduce verbosity
        }
    }

    public void StopManualFollow()
    {
        // DON'T call SetFollow() - it re-applies KSA's default offset, causing a snap
        // Instead, just clear the manual follow state and leave camera following with current offset
        // The Update() method will continue to maintain the follow relationship

        Console.WriteLine($"[CameraRestore] Stopping manual follow (keeping current offset {_followOffset})");
        Console.WriteLine($"[CameraRestore] Camera will continue following target at current position");

        // Keep _followedObject and _followOffset active!
        // Keep _isManualFollowing = true!
        // This allows the camera to continue smoothly following the target

        // User can manually re-follow with default offset later if desired
        // (e.g., via a button or keybind that calls the old SetFollow logic)
    }

    public bool ExitManualFollow(
        ManualFollowExitMode mode,
        bool? unknown0 = null,
        bool? changeControl = null,
        bool? alert = null)
    {
        if (!_isManualFollowing)
        {
            Console.WriteLine("[CameraExit] Not in manual follow mode, nothing to exit");
            return true; // Already not in manual follow
        }

        switch (mode)
        {
            case ManualFollowExitMode.KeepCurrentOffset:
                return ExitKeepOffset();

            case ManualFollowExitMode.RestoreNativeFollow:
                return ExitRestoreNative(unknown0, changeControl, alert);

            default:
                Console.WriteLine($"[CameraExit] Unknown exit mode: {mode}");
                return false;
        }
    }

    private bool ExitKeepOffset()
    {
        Console.WriteLine($"[CameraExit] Exiting manual follow (keeping current offset {_followOffset})");
        Console.WriteLine($"[CameraExit] Camera will continue following target at current position");

        // Keep _followedObject and _followOffset active!
        // Keep _isManualFollowing = true!
        // This allows the camera to continue smoothly following the target
        // without snapping to KSA's default offset

        return true;
    }

    private bool ExitRestoreNative(bool? unknown0, bool? changeControl, bool? alert)
    {
        var camera = GetCamera();
        if (camera == null)
        {
            Console.WriteLine("[CameraExit] Camera not available for native restore");
            return false;
        }

        if (_followedObject == null)
        {
            Console.WriteLine("[CameraExit] No follow target to restore");
            _isManualFollowing = false;
            return true;
        }

        try
        {
            // Try to call SetFollow via reflection with optional flags
            var cameraType = camera.GetType();
            var setFollowMethod = cameraType.GetMethod("SetFollow",
                BindingFlags.Public | BindingFlags.Instance);

            if (setFollowMethod == null)
            {
                Console.WriteLine("[CameraExit] SetFollow method not found on camera");
                return false;
            }

            var parameters = setFollowMethod.GetParameters();
            Console.WriteLine($"[CameraExit] Restoring native follow via SetFollow with {parameters.Length} parameters");

            object?[] args;
            if (parameters.Length == 1)
            {
                // Simple case: only target object
                args = new object?[] { _followedObject };
            }
            else if (parameters.Length == 4)
            {
                // Full signature: SetFollow(object target, bool unknown0, bool changeControl, bool alert)
                args = new object?[]
                {
                    _followedObject,
                    unknown0 ?? true,
                    changeControl ?? true,
                    alert ?? false
                };
                Console.WriteLine($"[CameraExit] Using flags: unknown0={args[1]}, changeControl={args[2]}, alert={args[3]}");
            }
            else
            {
                Console.WriteLine($"[CameraExit] Unexpected SetFollow signature with {parameters.Length} parameters");
                return false;
            }

            // Call SetFollow to restore native behavior
            setFollowMethod.Invoke(camera, args);

            // Clear manual follow state
            _isManualFollowing = false;
            _followedObject = null;
            _followOffset = double3.Zero;

            Console.WriteLine("[CameraExit] Successfully restored native follow behavior");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CameraExit] Failed to restore native follow: {ex.Message}");
            Console.WriteLine($"[CameraExit] Target type: {_followedObject?.GetType().Name ?? "null"}");
            return false;
        }
    }

    public void LookAt(double3 target)
    {
        var camera = GetCamera();
        if (camera == null) return;

        // ECL up vector (Z-axis)
        var upEcl = new double3(0, 0, 1);
        camera.LookAt(target, upEcl);
    }

    public void ApplyRotation(float yaw, float pitch, float roll)
    {
        var camera = GetCamera();
        if (camera == null) return;

        // Convert degrees to radians
        var yawRad = yaw * (Math.PI / 180.0);
        var pitchRad = pitch * (Math.PI / 180.0);
        var rollRad = roll * (Math.PI / 180.0);

        // Create rotation quaternions in ECL space
        // Yaw around Z (Up), Pitch around X (Right), Roll around Y (Forward)
        var yawQuat = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), yawRad);
        var pitchQuat = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), pitchRad);
        var rollQuat = doubleQuat.CreateFromAxisAngle(new double3(0, 1, 0), rollRad);

        // Combine rotations (extrinsic ZXY order)
        var newRot = yawQuat * pitchQuat * rollQuat;

        // Convert to float quaternion for matrix creation
        var fQuat = new floatQuat(
            (float)newRot.X,
            (float)newRot.Y,
            (float)newRot.Z,
            (float)newRot.W
        );

        // Create rotation matrix
        var rotMatrix = float4x4.CreateFromQuaternion(fQuat);

        // CRITICAL: Preserve position when using SetMatrix
        var savedPos = camera.PositionEcl;
        var savedLocalPos = camera.LocalPosition;

        camera.SetMatrix(rotMatrix);

        // Restore position to prevent drift
        camera.LocalPosition = savedLocalPos;
        camera.PositionEcl = savedPos;
        camera.WorldRotation = newRot;
    }

    public void Update(double deltaTime)
    {
        // Update manual follow position if active
        if (_isManualFollowing && _followedObject != null)
        {
            try
            {
                dynamic? dynTarget = _followedObject;
                double3 targetPos = dynTarget?.GetPositionEcl() ?? double3.Zero;
                var camera = GetCamera();
                if (camera != null)
                {
                    camera.PositionEcl = targetPos + _followOffset;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"KsaCameraService: Error updating manual follow: {ex.Message}");
                _isManualFollowing = false;
                _followedObject = null;
            }
        }
    }

    /// <summary>
    /// Attempts to call KSA.Camera.SetFollow using reflection, allowing dynamic/object targets.
    /// This method provides safe invocation even when the follow target is not statically typed
    /// as Astronomical, and enables systematic experimentation of camera control flags.
    /// </summary>
    /// <param name="target">The object to follow (typically an Astronomical but stored as object/dynamic)</param>
    /// <param name="options">The boolean flags to pass to SetFollow</param>
    /// <param name="error">Receives a detailed error message on failure</param>
    /// <returns>True if SetFollow was successfully invoked, false otherwise</returns>
    public bool TrySetFollow(object target, SetFollowOptions options, out string? error)
    {
        error = null;
        var camera = GetCamera();

        if (camera == null)
        {
            error = "Camera is not available";
            return false;
        }

        if (target == null)
        {
            error = "Target object is null";
            return false;
        }

        try
        {
            var cameraType = camera.GetType();
            
            // Find SetFollow method with 4 parameters (object, bool, bool, bool)
            var setFollowMethod = cameraType.GetMethod("SetFollow",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(object), typeof(bool), typeof(bool), typeof(bool) },
                null);

            if (setFollowMethod == null)
            {
                // Fallback: search by name and parameter count
                var methods = cameraType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "SetFollow" && m.GetParameters().Length == 4)
                    .ToArray();

                if (methods.Length == 0)
                {
                    error = "SetFollow method with 4 parameters not found on Camera type";
                    return false;
                }

                setFollowMethod = methods[0];
            }

            // Validate parameter[0] type compatibility
            var parameters = setFollowMethod.GetParameters();
            var targetParamType = parameters[0].ParameterType;
            var actualType = target.GetType();

            if (!targetParamType.IsAssignableFrom(actualType))
            {
                error = $"Type mismatch: SetFollow expects {targetParamType.Name} but target is {actualType.Name}";
                return false;
            }

            // Invoke with the three bool flags
            object?[] args = new object?[]
            {
                target,
                options.Unknown0,
                options.ChangeControl,
                options.Alert
            };

            setFollowMethod.Invoke(camera, args);

            Console.WriteLine($"[SetFollow] Success: target={actualType.Name}, " +
                            $"unknown0={options.Unknown0}, changeControl={options.ChangeControl}, alert={options.Alert}");
            return true;
        }
        catch (TargetInvocationException ex)
        {
            var innerEx = ex.InnerException ?? ex;
            error = $"SetFollow threw exception: {innerEx.GetType().Name}: {innerEx.Message}";
            Console.WriteLine($"[SetFollow] Failed: {error}");
            return false;
        }
        catch (Exception ex)
        {
            error = $"Reflection error: {ex.GetType().Name}: {ex.Message}";
            Console.WriteLine($"[SetFollow] Failed: {error}");
            return false;
        }
    }

    /// <summary>
    /// Attempts to discover and expose KSA's internal camera control/controller mode
    /// using reflection. Looks for common property/field names that might indicate
    /// the camera's internal state beyond Follow/Unfollow/ManualFollow.
    /// </summary>
    /// <returns>
    /// A formatted string with type and value information if discoverable,
    /// or null if no relevant mode information can be found.
    /// </returns>
    public string? GetNativeControlModeDebug()
    {
        var camera = GetCamera();
        if (camera == null) return null;

        var cameraType = camera.GetType();
        var candidateNames = new[]
        {
            "ControlMode",
            "Controller",
            "CameraController",
            "Mode",
            "State",
            "ControllerMode",
            "CameraMode",
            "CameraState",
            "Control"
        };

        // Search for properties first
        foreach (var name in candidateNames)
        {
            try
            {
                var property = cameraType.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (property != null && property.CanRead)
                {
                    var value = property.GetValue(camera);
                    var typeName = property.PropertyType.Name;
                    var valueStr = value?.ToString() ?? "null";
                    return $"{name} ({typeName}): {valueStr}";
                }
            }
            catch
            {
                // Silently continue on read errors
            }
        }

        // Search for fields if properties not found
        foreach (var name in candidateNames)
        {
            try
            {
                var field = cameraType.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (field != null)
                {
                    var value = field.GetValue(camera);
                    var typeName = field.FieldType.Name;
                    var valueStr = value?.ToString() ?? "null";
                    return $"{name} ({typeName}): {valueStr}";
                }
            }
            catch
            {
                // Silently continue on read errors
            }
        }

        // Nothing found
        return null;
    }

    /// <summary>
    /// Gets the KSA camera instance via reflection.
    /// Caches the result for performance.
    /// </summary>
    private KSA.Camera? GetCamera()
    {
        // Return cached camera if available
        if (_camera != null) return _camera;

        try
        {
            var ksaAssembly = typeof(KSA.Camera).Assembly;
            var programType = ksaAssembly.GetType("KSA.Program");

            if (programType != null)
            {
                // Try GetMainCamera first, fall back to GetCamera
                var getMainCameraMethod = programType.GetMethod("GetMainCamera",
                    BindingFlags.Public | BindingFlags.Static);
                var getCameraMethod = programType.GetMethod("GetCamera",
                    BindingFlags.Public | BindingFlags.Static);

                MethodInfo? methodToUse = getMainCameraMethod ?? getCameraMethod;

                if (methodToUse != null)
                {
                    _camera = methodToUse.Invoke(null, null) as KSA.Camera;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"KsaCameraService: Error getting camera: {ex.Message}");
        }

        return _camera;
    }
}
