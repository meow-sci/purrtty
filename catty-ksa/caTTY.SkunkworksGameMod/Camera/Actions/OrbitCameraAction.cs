using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using caTTY.SkunkworksGameMod.Camera.Animation;

namespace caTTY.SkunkworksGameMod.Camera.Actions;

/// <summary>
/// Generates keyframes for a circular orbit animation around a target.
/// The camera orbits horizontally while continuously looking at the target.
/// </summary>
public class OrbitCameraAction : ICameraAction
{
    public string ActionName => "orbit";

    public ValidationResult Validate(CameraActionContext context)
    {
        if (context.Camera.FollowTarget == null)
        {
            return ValidationResult.Fail("No follow target - camera must be following an object");
        }

        if (context.Duration <= 0)
        {
            return ValidationResult.Fail("Duration must be greater than 0");
        }

        if (context.Distance < 0)
        {
            return ValidationResult.Fail("Distance cannot be negative");
        }

        if (context.UseStartLerp && context.StartLerpTime <= 0)
        {
            return ValidationResult.Fail("StartLerpTime must be greater than 0 when UseStartLerp is true");
        }

        if (context.UseEndLerp && context.EndLerpTime <= 0)
        {
            return ValidationResult.Fail("EndLerpTime must be greater than 0 when UseEndLerp is true");
        }

        if (context.UseEndLerp && context.OriginalState == null)
        {
            return ValidationResult.Fail("OriginalState is required when UseEndLerp is true");
        }

        return ValidationResult.Success();
    }

    public IEnumerable<CameraKeyframe> GenerateKeyframes(CameraActionContext context)
    {
        var keyframes = new List<CameraKeyframe>();

        // Calculate orbit parameters
        double3 currentOffset = context.CurrentOffset;

        // Calculate horizontal distance (in XZ plane)
        double horizontalDistance = Math.Sqrt(
            currentOffset.X * currentOffset.X +
            currentOffset.Z * currentOffset.Z
        );

        // Use specified distance or current distance as radius
        double radius = context.Distance > 0 ? context.Distance : Math.Max(horizontalDistance, 100.0);

        // Calculate starting angle
        double startAngle = Math.Atan2(currentOffset.Z, currentOffset.X);

        // Preserve altitude (Y offset)
        double altitudeOffset = currentOffset.Y;

        // Direction multiplier
        double directionMultiplier = context.CounterClockwise ? -1.0 : 1.0;

        // ECL up vector
        double3 upEcl = new double3(0, 0, 1);

        // Generate 25 orbit keyframes for smooth full circle
        int keyframeCount = 25;

        for (int i = 0; i < keyframeCount; i++)
        {
            float linearProgress = (float)i / (keyframeCount - 1);
            float easedProgress = EasingFunctions.ApplyEasing(linearProgress, context.Easing);

            // Calculate angle with easing applied
            double angle = startAngle + easedProgress * 2.0 * Math.PI * directionMultiplier;

            // Calculate position on circle (in XZ plane at current altitude)
            double offsetX = radius * Math.Cos(angle);
            double offsetY = altitudeOffset;
            double offsetZ = radius * Math.Sin(angle);

            double3 offset = new double3(offsetX, offsetY, offsetZ);

            // Calculate look-at rotation toward target
            double3 cameraPos = context.TargetPosition + offset;
            double3 lookDirection = (context.TargetPosition - cameraPos);
            double lookMagnitude = lookDirection.Length();
            if (lookMagnitude > 0.001)
            {
                lookDirection = lookDirection / lookMagnitude;
            }

            // Create rotation quaternion
            doubleQuat lookAtQuaternion = KSA.Camera.LookAtRotation(lookDirection, upEcl);
            var (orbitYaw, orbitPitch, orbitRoll) = QuaternionToYPR(lookAtQuaternion);

            // Timestamp is linear progress * duration (starts at 0)
            float timestamp = linearProgress * context.Duration;

            string? debugLabel = i == 0 ? "Orbit Start" :
                                 i == keyframeCount - 1 ? "Orbit End" :
                                 null;

            keyframes.Add(new CameraKeyframe(
                timestamp,
                offset,
                orbitYaw,
                orbitPitch,
                orbitRoll,
                context.CurrentFov,
                debugLabel
            ));
        }

        return keyframes;
    }

    /// <summary>
    /// Converts a quaternion to Yaw/Pitch/Roll angles in ECL coordinate system.
    /// Matches the forward conversion: yawQuat(Z) * pitchQuat(X) * rollQuat(Y)
    /// </summary>
    private static (float yaw, float pitch, float roll) QuaternionToYPR(doubleQuat q)
    {
        var qw = q.W;
        var qx = q.X;
        var qy = q.Y;
        var qz = q.Z;

        // Rotation matrix elements
        double r00 = 1.0 - 2.0 * (qy * qy + qz * qz);
        double r01 = 2.0 * (qx * qy - qw * qz);
        double r11 = 1.0 - 2.0 * (qx * qx + qz * qz);
        double r20 = 2.0 * (qx * qz - qw * qy);
        double r21 = 2.0 * (qy * qz + qw * qx);
        double r22 = 1.0 - 2.0 * (qx * qx + qy * qy);

        // Extract angles (extrinsic ZXY)
        var pitch = Math.Asin(Math.Clamp(r21, -1.0, 1.0));
        var yaw = Math.Atan2(-r01, r11);
        var roll = Math.Atan2(-r20, r22);

        // Convert to degrees
        return (
            (float)(yaw * 180.0 / Math.PI),
            (float)(pitch * 180.0 / Math.PI),
            (float)(roll * 180.0 / Math.PI)
        );
    }

    /// <summary>
    /// Extracts yaw from quaternion (quick version for current rotation).
    /// </summary>
    private static float ExtractYaw(doubleQuat q)
    {
        var (yaw, _, _) = QuaternionToYPR(q);
        return yaw;
    }

    /// <summary>
    /// Extracts pitch from quaternion (quick version for current rotation).
    /// </summary>
    private static float ExtractPitch(doubleQuat q)
    {
        var (_, pitch, _) = QuaternionToYPR(q);
        return pitch;
    }

    /// <summary>
    /// Extracts roll from quaternion (quick version for current rotation).
    /// </summary>
    private static float ExtractRoll(doubleQuat q)
    {
        var (_, _, roll) = QuaternionToYPR(q);
        return roll;
    }
}
