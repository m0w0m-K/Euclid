using System;
using UnityEngine;

namespace Euclid
{
    internal readonly struct Vector2d
    {
        internal static readonly Vector2d Zero = new Vector2d(0d, 0d);
        internal static readonly Vector2d Right = new Vector2d(1d, 0d);

        internal Vector2d(double x, double y)
        {
            X = x;
            Y = y;
        }

        internal Vector2d(Vector2 value)
            : this(value.x, value.y)
        {
        }

        internal double X { get; }

        internal double Y { get; }

        internal double SqrMagnitude => X * X + Y * Y;

        internal double Magnitude => Math.Sqrt(SqrMagnitude);

        internal Vector2 ToVector2()
        {
            return new Vector2((float)X, (float)Y);
        }

        internal static double Dot(Vector2d a, Vector2d b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        public static Vector2d operator +(Vector2d a, Vector2d b)
        {
            return new Vector2d(a.X + b.X, a.Y + b.Y);
        }

        public static Vector2d operator -(Vector2d a, Vector2d b)
        {
            return new Vector2d(a.X - b.X, a.Y - b.Y);
        }

        public static Vector2d operator *(Vector2d value, double scale)
        {
            return new Vector2d(value.X * scale, value.Y * scale);
        }

        public static Vector2d operator /(Vector2d value, double scale)
        {
            return new Vector2d(value.X / scale, value.Y / scale);
        }
    }
}
