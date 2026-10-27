using System;

namespace Wrestling.Core.Numerics
{
    /// <summary>
    /// 2D fixed-point vector in metres on the mat plane (X right, Y up as seen from the officials'
    /// table). Directions are kept as unit vectors, so the simulation needs no trigonometry.
    /// </summary>
    public readonly struct FixVector2 : IEquatable<FixVector2>
    {
        public static readonly FixVector2 Zero = new FixVector2(Fix64.Zero, Fix64.Zero);
        public static readonly FixVector2 UnitX = new FixVector2(Fix64.One, Fix64.Zero);
        public static readonly FixVector2 UnitY = new FixVector2(Fix64.Zero, Fix64.One);

        public FixVector2(Fix64 x, Fix64 y)
        {
            X = x;
            Y = y;
        }

        public Fix64 X { get; }

        public Fix64 Y { get; }

        public Fix64 LengthSquared => (X * X) + (Y * Y);

        public Fix64 Length => Fix64.Sqrt(LengthSquared);

        public static FixVector2 operator +(FixVector2 a, FixVector2 b) => new FixVector2(a.X + b.X, a.Y + b.Y);

        public static FixVector2 operator -(FixVector2 a, FixVector2 b) => new FixVector2(a.X - b.X, a.Y - b.Y);

        public static FixVector2 operator -(FixVector2 v) => new FixVector2(-v.X, -v.Y);

        public static FixVector2 operator *(FixVector2 v, Fix64 scale) => new FixVector2(v.X * scale, v.Y * scale);

        public static FixVector2 operator /(FixVector2 v, Fix64 divisor) => new FixVector2(v.X / divisor, v.Y / divisor);

        public static bool operator ==(FixVector2 a, FixVector2 b) => a.Equals(b);

        public static bool operator !=(FixVector2 a, FixVector2 b) => !a.Equals(b);

        public static Fix64 Dot(FixVector2 a, FixVector2 b) => (a.X * b.X) + (a.Y * b.Y);

        /// <summary>Z component of the 3D cross product; positive when <paramref name="b"/> is counter-clockwise from <paramref name="a"/>.</summary>
        public static Fix64 Cross(FixVector2 a, FixVector2 b) => (a.X * b.Y) - (a.Y * b.X);

        public static Fix64 DistanceSquared(FixVector2 a, FixVector2 b) => (a - b).LengthSquared;

        /// <summary>Unit vector in the same direction; the zero vector stays zero.</summary>
        public FixVector2 Normalized()
        {
            Fix64 length = Length;
            if (length == Fix64.Zero)
            {
                return Zero;
            }

            Fix64 inverse = Fix64.One / length;
            return new FixVector2(X * inverse, Y * inverse);
        }

        /// <summary>Rotated 90° counter-clockwise.</summary>
        public FixVector2 Perpendicular() => new FixVector2(-Y, X);

        /// <summary>
        /// Rotates by the angle of the unit vector <paramref name="rotation"/> (complex multiplication).
        /// Combine with <see cref="Normalized"/> after many rotations to remove rounding drift.
        /// </summary>
        public FixVector2 Rotate(FixVector2 rotation) => new FixVector2((X * rotation.X) - (Y * rotation.Y), (X * rotation.Y) + (Y * rotation.X));

        /// <summary>Same direction, length limited to <paramref name="maxLength"/>.</summary>
        public FixVector2 ClampLength(Fix64 maxLength)
        {
            Fix64 squared = LengthSquared;
            return squared <= maxLength * maxLength ? this : Normalized() * maxLength;
        }

        public bool Equals(FixVector2 other) => X == other.X && Y == other.Y;

        public override bool Equals(object? obj) => obj is FixVector2 other && Equals(other);

        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();

        public override string ToString() => $"({X}, {Y})";
    }
}
