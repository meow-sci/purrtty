using System;
using System.Text.Json.Serialization;
using Brutal.Numerics;

namespace StarMap.SimpleExampleMod
{
    public class QuickView
    {
        public string Name { get; set; } = "New View";
        public SerializableVector3 Offset { get; set; } = new SerializableVector3();
        public SerializableVector3 Rotation { get; set; } = new SerializableVector3(); // Yaw, Pitch, Roll
        public float Fov { get; set; } = 45.0f; // Default FOV in degrees

        public QuickView() { }

        public QuickView( string name, double3 offset, float yaw, float pitch, float roll, float fov )
        {
            Name = name;
            Offset = new SerializableVector3( offset.X, offset.Y, offset.Z );
            Rotation = new SerializableVector3( yaw, pitch, roll );
            Fov = fov;
        }

        public double3 GetDouble3Offset()
        {
            return new double3( Offset.X, Offset.Y, Offset.Z );
        }
        
        public (float Yaw, float Pitch, float Roll) GetRotation()
        {
            return ((float)Rotation.X, (float)Rotation.Y, (float)Rotation.Z);
        }
    }

    public class SerializableVector3
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public SerializableVector3() { }

        public SerializableVector3( double x, double y, double z )
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
