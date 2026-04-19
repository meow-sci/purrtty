using System;
using System.Linq;
using System.Text.Json;
using caTTY.SkunkworksGameMod.Camera;
using caTTY.SkunkworksGameMod.Camera.Actions;
using caTTY.SkunkworksGameMod.Camera.Animation;

namespace caTTY.SkunkworksGameMod.Rpc.Actions;

/// <summary>
/// RPC action handler for "camera-orbit" command.
/// Generates and executes an orbital camera animation around the followed target.
/// </summary>
public class CameraOrbitRpcAction : ISocketRpcAction
{
    public string ActionName => "camera-orbit";

    private readonly ICameraService _cameraService;
    private readonly ICameraAnimationPlayer _animationPlayer;
    private readonly OrbitCameraAction _orbitAction;

    public CameraOrbitRpcAction(
        ICameraService cameraService,
        ICameraAnimationPlayer animationPlayer)
    {
        _cameraService = cameraService;
        _animationPlayer = animationPlayer;
        _orbitAction = new OrbitCameraAction();
    }

    public SocketRpcResponse Execute(JsonElement? @params)
    {
        try
        {
            // Parse parameters
            var orbitParams = ParseParams(@params);

            // Validate lerp parameters
            if (orbitParams.UseStartLerp && !orbitParams.StartLerpTime.HasValue)
            {
                return SocketRpcResponse.Fail("startLerpTime required when useStartLerp=true");
            }

            if (orbitParams.UseEndLerp && !orbitParams.EndLerpTime.HasValue)
            {
                return SocketRpcResponse.Fail("endLerpTime required when useEndLerp=true");
            }

            // Check camera availability
            if (!_cameraService.IsAvailable)
            {
                return SocketRpcResponse.Fail("Camera not available");
            }

            if (_cameraService.FollowTarget == null)
            {
                return SocketRpcResponse.Fail("No follow target - camera must be following an object");
            }

            // Capture original camera state (before any changes)
            var targetPosition = _cameraService.GetTargetPosition();
            var currentOffset = _cameraService.Position - targetPosition;
            var currentRotation = _cameraService.Rotation;
            var currentFov = _cameraService.FieldOfView;

            var (currentYaw, currentPitch, currentRoll) = QuaternionToYPR(currentRotation);

            var originalState = new OriginalCameraState(
                currentOffset,
                currentYaw,
                currentPitch,
                currentRoll,
                currentFov
            );

            Console.WriteLine($"[CameraOrbit] Captured original state:");
            Console.WriteLine($"[CameraOrbit]   Camera position: {_cameraService.Position}");
            Console.WriteLine($"[CameraOrbit]   Target position: {targetPosition}");
            Console.WriteLine($"[CameraOrbit]   Original offset: {currentOffset}");
            Console.WriteLine($"[CameraOrbit]   Original YPR: ({currentYaw:F1}, {currentPitch:F1}, {currentRoll:F1})");

            // Build context
            var context = new CameraActionContext
            {
                Camera = _cameraService,
                TargetPosition = targetPosition,
                CurrentOffset = currentOffset,
                CurrentFov = currentFov,
                CurrentRotation = currentRotation,
                OriginalState = originalState,
                Duration = orbitParams.Time,
                Distance = orbitParams.Distance,
                UseStartLerp = orbitParams.UseStartLerp,
                StartLerpTime = orbitParams.StartLerpTime ?? 0f,
                StartLerpEasing = orbitParams.StartLerpEasing,
                UseEndLerp = orbitParams.UseEndLerp,
                EndLerpTime = orbitParams.EndLerpTime ?? 0f,
                EndLerpEasing = orbitParams.EndLerpEasing,
                Easing = orbitParams.Easing,
                CounterClockwise = orbitParams.CounterClockwise
            };

            // Validate
            var validation = _orbitAction.Validate(context);
            if (!validation.IsValid)
            {
                return SocketRpcResponse.Fail(validation.ErrorMessage ?? "Validation failed");
            }

            // Generate action keyframes
            var actionKeyframes = _orbitAction.GenerateKeyframes(context);

            // Build complete animation with start/end lerps
            var completeKeyframes = CameraAnimationBuilder.BuildAnimation(actionKeyframes, context);

            // Load keyframes and start animation
            _animationPlayer.ClearKeyframes();
            _animationPlayer.SetKeyframes(completeKeyframes);

            // Set up manual follow with zero offset (offset handled by animation)
            _cameraService.StartManualFollow(Brutal.Numerics.double3.Zero);

            // Play animation
            _animationPlayer.Play();

            return SocketRpcResponse.Ok(new
            {
                status = "playing",
                duration = orbitParams.Time,
                distance = orbitParams.Distance,
                useStartLerp = orbitParams.UseStartLerp,
                useEndLerp = orbitParams.UseEndLerp,
                counterClockwise = orbitParams.CounterClockwise,
                totalKeyframes = completeKeyframes.Count()
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CameraOrbitRpcAction: Error executing orbit: {ex.Message}");
            return SocketRpcResponse.Fail($"Failed to execute orbit: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts a quaternion to Yaw/Pitch/Roll angles.
    /// </summary>
    private static (float yaw, float pitch, float roll) QuaternionToYPR(Brutal.Numerics.doubleQuat q)
    {
        var qw = q.W;
        var qx = q.X;
        var qy = q.Y;
        var qz = q.Z;

        double r00 = 1.0 - 2.0 * (qy * qy + qz * qz);
        double r01 = 2.0 * (qx * qy - qw * qz);
        double r11 = 1.0 - 2.0 * (qx * qx + qz * qz);
        double r20 = 2.0 * (qx * qz - qw * qy);
        double r21 = 2.0 * (qy * qz + qw * qx);
        double r22 = 1.0 - 2.0 * (qx * qx + qy * qy);

        var pitch = Math.Asin(Math.Clamp(r21, -1.0, 1.0));
        var yaw = Math.Atan2(-r01, r11);
        var roll = Math.Atan2(-r20, r22);

        return (
            (float)(yaw * 180.0 / Math.PI),
            (float)(pitch * 180.0 / Math.PI),
            (float)(roll * 180.0 / Math.PI)
        );
    }

    private OrbitParams ParseParams(JsonElement? @params)
    {
        var result = new OrbitParams();

        if (@params == null || @params.Value.ValueKind == JsonValueKind.Null)
        {
            return result;
        }

        var elem = @params.Value;

        if (elem.ValueKind == JsonValueKind.Object)
        {
            if (elem.TryGetProperty("time", out var timeProp))
            {
                result.Time = timeProp.GetSingle();
            }

            if (elem.TryGetProperty("distance", out var distanceProp))
            {
                result.Distance = distanceProp.GetSingle();
            }

            // Start lerp parameters
            if (elem.TryGetProperty("useStartLerp", out var useStartLerpProp))
            {
                result.UseStartLerp = useStartLerpProp.GetBoolean();
            }

            if (elem.TryGetProperty("startLerpTime", out var startLerpTimeProp))
            {
                result.StartLerpTime = startLerpTimeProp.GetSingle();
            }

            if (elem.TryGetProperty("startLerpEasing", out var startLerpEasingProp))
            {
                result.StartLerpEasing = ParseEasing(startLerpEasingProp.GetString());
            }

            // End lerp parameters
            if (elem.TryGetProperty("useEndLerp", out var useEndLerpProp))
            {
                result.UseEndLerp = useEndLerpProp.GetBoolean();
            }

            if (elem.TryGetProperty("endLerpTime", out var endLerpTimeProp))
            {
                result.EndLerpTime = endLerpTimeProp.GetSingle();
            }

            if (elem.TryGetProperty("endLerpEasing", out var endLerpEasingProp))
            {
                result.EndLerpEasing = ParseEasing(endLerpEasingProp.GetString());
            }

            // Main animation parameters
            if (elem.TryGetProperty("counterClockwise", out var ccwProp))
            {
                result.CounterClockwise = ccwProp.GetBoolean();
            }

            if (elem.TryGetProperty("easing", out var easingProp))
            {
                result.Easing = ParseEasing(easingProp.GetString());
            }
        }

        return result;
    }

    private EasingType ParseEasing(string? easingStr)
    {
        return easingStr?.ToLowerInvariant() switch
        {
            "linear" => EasingType.Linear,
            "easein" => EasingType.EaseIn,
            "easeout" => EasingType.EaseOut,
            "easeinout" => EasingType.EaseInOut,
            _ => EasingType.EaseInOut
        };
    }

    private class OrbitParams
    {
        public float Time { get; set; } = 5.0f;
        public float Distance { get; set; } = 100.0f;

        // Start lerp
        public bool UseStartLerp { get; set; } = false;
        public float? StartLerpTime { get; set; }
        public EasingType StartLerpEasing { get; set; } = EasingType.EaseInOut;

        // End lerp
        public bool UseEndLerp { get; set; } = false;
        public float? EndLerpTime { get; set; }
        public EasingType EndLerpEasing { get; set; } = EasingType.EaseInOut;

        // Main animation
        public bool CounterClockwise { get; set; } = false;
        public EasingType Easing { get; set; } = EasingType.EaseInOut;
    }
}
