using System.Text.Json.Serialization;
using Brutal.Numerics;

namespace StarMap.SimpleExampleMod
{
    public class AnimationKeyframe
    {
        public float Timestamp { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double OffsetZ { get; set; }
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public float Roll { get; set; }
        public float Fov { get; set; }

        [JsonIgnore]
        public double3 Offset
        {
            get => new double3( OffsetX, OffsetY, OffsetZ );
            set { OffsetX = value.X; OffsetY = value.Y; OffsetZ = value.Z; }
        }

        public AnimationKeyframe() { }

        public AnimationKeyframe( float timestamp, double3 offset, float yaw, float pitch, float roll, float fov )
        {
            Timestamp = timestamp;
            Offset = offset;
            Yaw = yaw;
            Pitch = pitch;
            Roll = roll;
            Fov = fov;
        }
    }
}
