using System;
using System.Collections.Generic;
using System.Linq;

namespace caTTY.SkunkworksGameMod.Camera.Animation;

/// <summary>
/// Camera animation player with Catmull-Rom spline interpolation.
/// </summary>
public class CameraAnimationPlayer : ICameraAnimationPlayer
{
    private readonly List<CameraKeyframe> _keyframes = new();

    public bool IsPlaying { get; private set; }
    public float CurrentTime { get; private set; }

    public float Duration => _keyframes.Count > 0 ? _keyframes.Last().Timestamp : 0f;

    public IReadOnlyList<CameraKeyframe> Keyframes => _keyframes.AsReadOnly();

    public void SetKeyframes(IEnumerable<CameraKeyframe> keyframes)
    {
        _keyframes.Clear();
        _keyframes.AddRange(keyframes);
        SortKeyframes();
    }

    public void ClearKeyframes()
    {
        _keyframes.Clear();
        Stop();
    }

    public void Play()
    {
        if (_keyframes.Count < 2)
        {
            Console.WriteLine("CameraAnimationPlayer: Need at least 2 keyframes to play");
            return;
        }

        IsPlaying = true;
        CurrentTime = _keyframes[0].Timestamp;
    }

    public void Stop()
    {
        IsPlaying = false;
        CurrentTime = 0.0f;
    }

    public CameraAnimationFrame? Update(double deltaTime)
    {
        if (!IsPlaying || _keyframes.Count < 2)
        {
            return null;
        }

        CurrentTime += (float)deltaTime;

        // Check if animation ended
        if (CurrentTime >= _keyframes.Last().Timestamp)
        {
            CurrentTime = _keyframes.Last().Timestamp;
            IsPlaying = false;
            var last = _keyframes.Last();
            Console.WriteLine($"[AnimationEnd] Animation completed at timestamp {last.Timestamp:F2}s");
            Console.WriteLine($"[AnimationEnd] Final offset: {last.Offset}");
            Console.WriteLine($"[AnimationEnd] Final YPR: ({last.Yaw:F1}, {last.Pitch:F1}, {last.Roll:F1})");
            if (last.DebugLabel != null)
            {
                Console.WriteLine($"[AnimationEnd] Final keyframe label: {last.DebugLabel}");
            }
            return new CameraAnimationFrame
            {
                Offset = last.Offset,
                Yaw = last.Yaw,
                Pitch = last.Pitch,
                Roll = last.Roll,
                Fov = last.Fov
            };
        }

        // Find current segment
        int segmentIndex = -1;
        for (int i = 0; i < _keyframes.Count - 1; i++)
        {
            if (CurrentTime >= _keyframes[i].Timestamp && CurrentTime < _keyframes[i + 1].Timestamp)
            {
                segmentIndex = i;
                break;
            }
        }

        if (segmentIndex < 0)
        {
            return null;
        }

        // Get 4 keyframes for Catmull-Rom (p0, p1, p2, p3)
        var p0 = _keyframes[Math.Max(0, segmentIndex - 1)];
        var p1 = _keyframes[segmentIndex];
        var p2 = _keyframes[segmentIndex + 1];
        var p3 = _keyframes[Math.Min(_keyframes.Count - 1, segmentIndex + 2)];

        // Calculate interpolation factor
        float duration = p2.Timestamp - p1.Timestamp;
        float elapsed = CurrentTime - p1.Timestamp;
        float t = duration > 0 ? elapsed / duration : 0;

        // Interpolate all values using Catmull-Rom
        var offset = KeyframeInterpolator.CatmullRom(p0.Offset, p1.Offset, p2.Offset, p3.Offset, t);
        var yaw = KeyframeInterpolator.CatmullRomFloat(p0.Yaw, p1.Yaw, p2.Yaw, p3.Yaw, t);
        var pitch = KeyframeInterpolator.CatmullRomFloat(p0.Pitch, p1.Pitch, p2.Pitch, p3.Pitch, t);
        var roll = KeyframeInterpolator.CatmullRomFloat(p0.Roll, p1.Roll, p2.Roll, p3.Roll, t);
        var fov = KeyframeInterpolator.CatmullRomFloat(p0.Fov, p1.Fov, p2.Fov, p3.Fov, t);

        return new CameraAnimationFrame
        {
            Offset = offset,
            Yaw = yaw,
            Pitch = pitch,
            Roll = roll,
            Fov = fov
        };
    }

    private void SortKeyframes()
    {
        _keyframes.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
    }
}
