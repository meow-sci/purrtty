using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;

namespace StarMap.SimpleExampleMod
{
    public enum EasingType
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut
    }

    public static class CameraActionGenerator
    {
        /// <summary>
        /// Applies easing function to a linear progress value (0 to 1).
        /// </summary>
        public static float ApplyEasing(float t, EasingType easing)
        {
            return easing switch
            {
                EasingType.Linear => t,
                EasingType.EaseIn => t * t,
                EasingType.EaseOut => t * (2 - t),
                EasingType.EaseInOut => t < 0.5f ? 2 * t * t : -1 + (4 - 2 * t) * t,
                _ => t
            };
        }

        /// <summary>
        /// Generates keyframes for a circular orbit animation around the target.
        /// The camera orbits horizontally around the target while always looking at it.
        /// </summary>
        public static List<AnimationKeyframe> GenerateOrbit(
            Camera camera,
            dynamic target,
            float duration,
            bool counterClockwise,
            EasingType easing)
        {
            var keyframes = new List<AnimationKeyframe>();

            // Clamp minimum duration
            duration = Math.Max(0.1f, duration);

            // Get current positions
            double3 currentPos = camera.PositionEcl;
            double3 targetPos = target.GetPositionEcl();
            double3 currentOffset = currentPos - targetPos;

            // Calculate horizontal distance (in XZ plane)
            double horizontalDistance = Math.Sqrt(currentOffset.X * currentOffset.X + currentOffset.Z * currentOffset.Z);

            // Use default radius if too close
            double radius = horizontalDistance < 10.0 ? 100.0 : horizontalDistance;

            // Calculate starting angle
            double startAngle = Math.Atan2(currentOffset.Z, currentOffset.X);

            // Preserve altitude (Y offset)
            double altitudeOffset = currentOffset.Y;

            // Direction multiplier
            double directionMultiplier = counterClockwise ? -1.0 : 1.0;

            // Generate 25 keyframes for smooth circle
            int keyframeCount = 25;
            double3 upEcl = new double3(0, 0, 1);

            // Get current FOV (no change during orbit)
            float currentFov = camera.GetFieldOfView() * 57.2958f;

            for (int i = 0; i < keyframeCount; i++)
            {
                float linearProgress = (float)i / (keyframeCount - 1);
                float easedProgress = ApplyEasing(linearProgress, easing);

                // Calculate angle with easing applied to rotation
                double angle = startAngle + easedProgress * 2.0 * Math.PI * directionMultiplier;

                // Calculate position on circle (in XZ plane at current altitude)
                double offsetX = radius * Math.Cos(angle);
                double offsetY = altitudeOffset;
                double offsetZ = radius * Math.Sin(angle);

                double3 offset = new double3(offsetX, offsetY, offsetZ);

                // Calculate look-at rotation toward target (from orbit position)
                // Note: These YPR values will be overridden by look-at during playback,
                // but we set them for consistency
                double3 cameraPos = targetPos + offset;
                double3 lookDirection = (targetPos - cameraPos);
                double lookMag = lookDirection.Length();
                if (lookMag > 0.001)
                {
                    lookDirection = lookDirection / lookMag;
                }

                // Create rotation quaternion
                doubleQuat lookAtQuat = Camera.LookAtRotation(lookDirection, upEcl);
                var (yaw, pitch, roll) = QuaternionToYPR(lookAtQuat);

                // Use linear progress for timestamp
                float timestamp = linearProgress * duration;

                keyframes.Add(new AnimationKeyframe(timestamp, offset, yaw, pitch, roll, currentFov));
            }

            return keyframes;
        }

        /// <summary>
        /// Generates keyframes to zoom into the character's face area.
        /// Face is positioned at 2/3 up from center (1.5m up).
        /// </summary>
        public static List<AnimationKeyframe> GenerateZoomFace(
            Camera camera,
            dynamic target,
            float duration,
            EasingType easing)
        {
            var keyframes = new List<AnimationKeyframe>();

            // Clamp minimum duration
            duration = Math.Max(0.1f, duration);

            // Get current positions
            double3 currentPos = camera.PositionEcl;
            double3 targetPos = target.GetPositionEcl();

            // Calculate face position (1.5m up from center of mass)
            double3 facePos = targetPos + new double3(0, 0, 1.5);

            // Get current forward direction
            double3 currentFwd = camera.GetForward();

            // Calculate final camera position (2m away from face)
            double3 finalCameraPos = facePos - currentFwd * 2.0;

            // Calculate offsets relative to target
            double3 currentOffset = currentPos - targetPos;
            double3 finalOffset = finalCameraPos - targetPos;

            // Get current and target FOV
            float currentFov = camera.GetFieldOfView() * 57.2958f;
            float targetFov = 30.0f; // Narrow FOV for close-up

            // Generate 12 keyframes
            int keyframeCount = 12;
            double3 upEcl = new double3(0, 0, 1);

            for (int i = 0; i < keyframeCount; i++)
            {
                float linearProgress = (float)i / (keyframeCount - 1);
                float easedProgress = ApplyEasing(linearProgress, easing);

                // Lerp offset from current to final
                double3 offset = Lerp(currentOffset, finalOffset, easedProgress);
                double3 cameraPos = targetPos + offset;

                // Calculate look-at rotation toward face
                double3 lookDirection = (facePos - cameraPos);
                double lookMag = lookDirection.Length();
                if (lookMag > 0.001)
                {
                    lookDirection = lookDirection / lookMag;
                }

                // Create rotation quaternion
                doubleQuat lookAtQuat = Camera.LookAtRotation(lookDirection, upEcl);
                var (yaw, pitch, roll) = QuaternionToYPR(lookAtQuat);

                // Lerp FOV
                float fov = currentFov + (targetFov - currentFov) * easedProgress;

                // Use linear progress for timestamp
                float timestamp = linearProgress * duration;

                keyframes.Add(new AnimationKeyframe(timestamp, offset, yaw, pitch, roll, fov));
            }

            return keyframes;
        }

        /// <summary>
        /// Generates keyframes to zoom out by moving camera backward.
        /// </summary>
        public static List<AnimationKeyframe> GenerateZoomOut(
            Camera camera,
            dynamic target,
            double distance,
            float duration,
            EasingType easing)
        {
            var keyframes = new List<AnimationKeyframe>();

            // Clamp minimum duration
            duration = Math.Max(0.1f, duration);

            // Skip if distance is too small
            if (distance < 0.1)
            {
                Console.WriteLine("CameraActionGenerator - Zoom out distance too small, skipping");
                return keyframes;
            }

            // Get current position and forward vector
            double3 currentPos = camera.PositionEcl;
            double3 forward = camera.GetForward();

            // Calculate final position (move backward)
            double3 finalPos = currentPos - forward * distance;

            // Get target position (or use current pos if no target)
            double3 targetPos = target != null ? target.GetPositionEcl() : currentPos;

            // Calculate offsets
            double3 currentOffset = currentPos - targetPos;
            double3 finalOffset = finalPos - targetPos;

            // Get current rotation (preserve it)
            doubleQuat currentRot = camera.WorldRotation;
            var (currentYaw, currentPitch, currentRoll) = QuaternionToYPR(currentRot);

            // Get current and target FOV (optionally widen slightly)
            float currentFov = camera.GetFieldOfView() * 57.2958f;
            float targetFov = Math.Min(currentFov + 10.0f, 120.0f);

            // Generate 12 keyframes
            int keyframeCount = 12;

            for (int i = 0; i < keyframeCount; i++)
            {
                float linearProgress = (float)i / (keyframeCount - 1);
                float easedProgress = ApplyEasing(linearProgress, easing);

                // Lerp offset from current to final
                double3 offset = Lerp(currentOffset, finalOffset, easedProgress);

                // Keep current rotation (no change)
                float yaw = currentYaw;
                float pitch = currentPitch;
                float roll = currentRoll;

                // Lerp FOV (slight widening)
                float fov = currentFov + (targetFov - currentFov) * easedProgress;

                // Use linear progress for timestamp
                float timestamp = linearProgress * duration;

                keyframes.Add(new AnimationKeyframe(timestamp, offset, yaw, pitch, roll, fov));
            }

            return keyframes;
        }

        /// <summary>
        /// Linear interpolation for double3.
        /// </summary>
        private static double3 Lerp(double3 start, double3 end, float t)
        {
            return start + (end - start) * t;
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

            double r00 = 1.0 - 2.0 * (qy * qy + qz * qz);
            double r01 = 2.0 * (qx * qy - qw * qz);
            double r02 = 2.0 * (qx * qz + qw * qy);
            double r10 = 2.0 * (qx * qy + qw * qz);
            double r11 = 1.0 - 2.0 * (qx * qx + qz * qz);
            double r12 = 2.0 * (qy * qz - qw * qx);
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
    }
}
