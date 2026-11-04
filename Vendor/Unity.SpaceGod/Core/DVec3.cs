using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Double-precision 3-vector. All simulation state lives in doubles: a float cannot hold a
    /// heliocentric position to better than ~10 km at Neptune's distance, which is visible jitter
    /// when the camera sits a few kilometres from a ship. Conversion to float happens only after
    /// subtracting the camera position (camera-relative rendering).
    /// </summary>
    public readonly struct DVec3 : IEquatable<DVec3>
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public DVec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly DVec3 Zero = new DVec3(0, 0, 0);
        public static readonly DVec3 UnitX = new DVec3(1, 0, 0);
        public static readonly DVec3 UnitY = new DVec3(0, 1, 0);
        public static readonly DVec3 UnitZ = new DVec3(0, 0, 1);

        public double LengthSquared => X * X + Y * Y + Z * Z;
        public double Length => Math.Sqrt(LengthSquared);

        public DVec3 Normalized
        {
            get
            {
                double len = Length;
                return len > 0 ? this / len : Zero;
            }
        }

        public static DVec3 operator +(DVec3 a, DVec3 b) => new DVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static DVec3 operator -(DVec3 a, DVec3 b) => new DVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static DVec3 operator -(DVec3 a) => new DVec3(-a.X, -a.Y, -a.Z);
        public static DVec3 operator *(DVec3 a, double s) => new DVec3(a.X * s, a.Y * s, a.Z * s);
        public static DVec3 operator *(double s, DVec3 a) => new DVec3(a.X * s, a.Y * s, a.Z * s);
        public static DVec3 operator /(DVec3 a, double s) => new DVec3(a.X / s, a.Y / s, a.Z / s);

        public static double Dot(DVec3 a, DVec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static DVec3 Cross(DVec3 a, DVec3 b) =>
            new DVec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public static double Distance(DVec3 a, DVec3 b) => (a - b).Length;

        public static DVec3 Lerp(DVec3 a, DVec3 b, double t) => a + (b - a) * t;

        /// <summary>
        /// Rotates unit direction <paramref name="a"/> a fraction <paramref name="t"/> of the angle
        /// round to unit direction <paramref name="b"/> (a slerp): a straight lerp turns well short of
        /// that at wide angles, next to nothing when nearly opposite, and never leaves the line when
        /// exactly opposite, where any perpendicular is taken as the way round.
        /// </summary>
        public static DVec3 TurnStep(DVec3 a, DVec3 b, double t)
        {
            double cos = Math.Max(-1, Math.Min(1, Dot(a, b)));
            double angle = Math.Acos(cos);
            if (angle < 1e-12) return b;
            var side = b - a * cos;
            if (side.LengthSquared < 1e-18)
            {
                side = Cross(a, UnitZ);
                if (side.LengthSquared < 1e-12) side = Cross(a, UnitY);
            }
            double step = t * angle;
            return (a * Math.Cos(step) + side.Normalized * Math.Sin(step)).Normalized;
        }

        /// <summary>
        /// Ecliptic (right-handed, z = ecliptic north) to scene (Unity, left-handed, y = up).
        /// Swapping y and z is a reflection, which is exactly the handedness change; it is its own inverse.
        /// </summary>
        public DVec3 SwapYZ => new DVec3(X, Z, Y);

        public bool Equals(DVec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is DVec3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:G6}, {Y:G6}, {Z:G6})";
    }
}
