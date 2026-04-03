using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Brutal.Numerics;

namespace StarMap.SimpleExampleMod
{
    public class AnimationManager
    {
        private List<AnimationKeyframe> _keyframes = new List<AnimationKeyframe>();
        private const string KeyframesFile = "keyframes.json";
        
        public bool IsPlaying { get; private set; } = false;
        public float CurrentTime { get; private set; } = 0.0f;

        public AnimationManager()
        {
            Load();
        }

        public void AddKeyframe( float timestamp, double3 offset, float yaw, float pitch, float roll, float fov )
        {
            // If a keyframe exists at this exact time, replace it
            var existing = _keyframes.FirstOrDefault( k => Math.Abs( k.Timestamp - timestamp ) < 0.001f );
            if ( existing != null )
            {
                existing.Offset = offset;
                existing.Yaw = yaw;
                existing.Pitch = pitch;
                existing.Roll = roll;
                existing.Fov = fov;
            }
            else
            {
                _keyframes.Add( new AnimationKeyframe( timestamp, offset, yaw, pitch, roll, fov ) );
            }
            SortKeyframes();
            Save();
        }

        public void RemoveKeyframe( int index )
        {
            if ( index >= 0 && index < _keyframes.Count )
            {
                _keyframes.RemoveAt( index );
                Save();
            }
        }

        /// <summary>
        /// Updates a keyframe at the given index with new values.
        /// </summary>
        public void UpdateKeyframe( int index, double3 offset, float yaw, float pitch, float roll, float fov )
        {
            if ( index >= 0 && index < _keyframes.Count )
            {
                var kf = _keyframes[index];
                kf.Offset = offset;
                kf.Yaw = yaw;
                kf.Pitch = pitch;
                kf.Roll = roll;
                kf.Fov = fov;
                Save();
            }
        }

        public List<AnimationKeyframe> GetKeyframes()
        {
            return _keyframes;
        }

        /// <summary>
        /// Clears all keyframes from the animation.
        /// </summary>
        public void ClearKeyframes()
        {
            _keyframes.Clear();
            Save();
        }

        /// <summary>
        /// Adds multiple keyframes at once. Automatically sorts after adding.
        /// </summary>
        public void AddKeyframes(List<AnimationKeyframe> keyframes)
        {
            _keyframes.AddRange(keyframes);
            SortKeyframes();
            Save();
        }

        public void SortKeyframes()
        {
            _keyframes.Sort( ( a, b ) => a.Timestamp.CompareTo( b.Timestamp ) );
        }

        public void Play()
        {
            if ( _keyframes.Count < 2 ) return;
            IsPlaying = true;
            CurrentTime = _keyframes[0].Timestamp;
        }

        public void Stop()
        {
            IsPlaying = false;
            CurrentTime = 0.0f;
        }

        /// <summary>
        /// Saves keyframes to a JSON file.
        /// </summary>
        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize( _keyframes, options );
                File.WriteAllText( KeyframesFile, json );
            }
            catch ( Exception ex )
            {
                Console.WriteLine( $"AnimationManager - Failed to save keyframes: {ex.Message}" );
            }
        }

        /// <summary>
        /// Loads keyframes from a JSON file.
        /// </summary>
        public void Load()
        {
            try
            {
                if ( File.Exists( KeyframesFile ) )
                {
                    var json = File.ReadAllText( KeyframesFile );
                    var loaded = JsonSerializer.Deserialize<List<AnimationKeyframe>>( json );
                    if ( loaded != null )
                    {
                        _keyframes = loaded;
                        SortKeyframes();
                        Console.WriteLine( $"AnimationManager - Loaded {_keyframes.Count} keyframes." );
                    }
                }
            }
            catch ( Exception ex )
            {
                Console.WriteLine( $"AnimationManager - Failed to load keyframes: {ex.Message}" );
            }
        }

        public (double3 Offset, float Yaw, float Pitch, float Roll, float Fov)? Update( double dt )
        {
            if ( !IsPlaying ) return null;
            if ( _keyframes.Count < 2 )
            {
                Stop();
                return null;
            }

            CurrentTime += (float)dt;

            // Check if we passed the last keyframe
            if ( CurrentTime >= _keyframes.Last().Timestamp )
            {
                CurrentTime = _keyframes.Last().Timestamp;
                IsPlaying = false;
                var last = _keyframes.Last();
                return (last.Offset, last.Yaw, last.Pitch, last.Roll, last.Fov);
            }

            // Find the segment index we are in
            int segmentIndex = -1;
            for ( int i = 0; i < _keyframes.Count - 1; i++ )
            {
                if ( CurrentTime >= _keyframes[i].Timestamp && CurrentTime < _keyframes[i+1].Timestamp )
                {
                    segmentIndex = i;
                    break;
                }
            }

            if ( segmentIndex >= 0 )
            {
                // Get the four keyframes for Catmull-Rom spline (p0, p1, p2, p3)
                // p1 and p2 are the segment endpoints, p0 and p3 are neighbors for tangent calculation
                var p0 = _keyframes[Math.Max( 0, segmentIndex - 1 )];
                var p1 = _keyframes[segmentIndex];
                var p2 = _keyframes[segmentIndex + 1];
                var p3 = _keyframes[Math.Min( _keyframes.Count - 1, segmentIndex + 2 )];

                float duration = p2.Timestamp - p1.Timestamp;
                float elapsed = CurrentTime - p1.Timestamp;
                float t = elapsed / duration;

                // Catmull-Rom spline interpolation for smooth in/out on all nodes
                double3 newPos = CatmullRom( p0.Offset, p1.Offset, p2.Offset, p3.Offset, t );
                float newYaw = CatmullRomFloat( p0.Yaw, p1.Yaw, p2.Yaw, p3.Yaw, t );
                float newPitch = CatmullRomFloat( p0.Pitch, p1.Pitch, p2.Pitch, p3.Pitch, t );
                float newRoll = CatmullRomFloat( p0.Roll, p1.Roll, p2.Roll, p3.Roll, t );
                float newFov = CatmullRomFloat( p0.Fov, p1.Fov, p2.Fov, p3.Fov, t );

                return (newPos, newYaw, newPitch, newRoll, newFov);
            }

            return null;
        }

        /// <summary>
        /// Catmull-Rom spline interpolation for double3.
        /// Provides smooth in/out transitions across all keyframes.
        /// </summary>
        private double3 CatmullRom( double3 p0, double3 p1, double3 p2, double3 p3, float t )
        {
            double t2 = t * t;
            double t3 = t2 * t;

            return 0.5 * (
                ( 2.0 * p1 ) +
                ( -p0 + p2 ) * t +
                ( 2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3 ) * t2 +
                ( -p0 + 3.0 * p1 - 3.0 * p2 + p3 ) * t3
            );
        }

        /// <summary>
        /// Catmull-Rom spline interpolation for float.
        /// Provides smooth in/out transitions across all keyframes.
        /// </summary>
        private float CatmullRomFloat( float p0, float p1, float p2, float p3, float t )
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                ( 2.0f * p1 ) +
                ( -p0 + p2 ) * t +
                ( 2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3 ) * t2 +
                ( -p0 + 3.0f * p1 - 3.0f * p2 + p3 ) * t3
            );
        }
    }
}
