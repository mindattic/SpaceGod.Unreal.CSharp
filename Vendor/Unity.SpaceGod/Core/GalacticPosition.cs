using System;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A place anywhere in the galaxy, held to the same precision everywhere (U.SPC-T412): a whole-number sector
    /// (a cube <see cref="SectorKm"/> on a side, J2000 ecliptic axes, Sol's root at the centre of sector 0,0,0) and an
    /// offset in km from that sector's centre, never more than half a sector on any axis.
    ///
    /// A single double loses its grip with distance: a light-year is 9.5e12 km, so a point 50,000 ly out held as one
    /// double of km is only good to about a hundred km, and a hull parked there would jitter and collide wrongly. Split
    /// this way the offset never exceeds half a sector, so it holds to a tenth of a millimetre whether the sector is
    /// next to Sol or across the galaxy, and the sector index (a long) is exact. Differences are taken sector by sector
    /// first (<see cref="KmFrom"/>), so two places near each other far from Sol still come out exact relative to each
    /// other: that is what a floating origin needs.
    ///
    /// Inside a star system everything is still kept in that system's own doubles (km from its root, as its
    /// ephemeris and fleet have always had them); <see cref="StarSystem.Origin"/> is where that frame sits in the
    /// galaxy, and <see cref="StarSystem.At"/> / <see cref="StarSystem.LocalKm"/> convert between the two levels.
    /// </summary>
    public readonly struct GalacticPosition : IEquatable<GalacticPosition>
    {
        /// <summary>The edge of one sector, km (a billion km, about 6.7 AU): small enough that the offset inside it holds to
        /// a tenth of a millimetre, large enough that a star system's inner worlds share a handful of sectors.</summary>
        public const double SectorKm = 1e9;

        /// <summary>Sectors in a light-year (not a whole number: a light-year is just a length here).</summary>
        public const double SectorsPerLy = Astro.LightYearKm / SectorKm;

        public readonly long X, Y, Z;

        /// <summary>Offset from the centre of sector (<see cref="X"/>, <see cref="Y"/>, <see cref="Z"/>), km, each axis within half a sector.</summary>
        public readonly DVec3 LocalKm;

        /// <summary>The sector's centre plus <paramref name="localKm"/>, carried into neighbouring sectors as far as it reaches.</summary>
        public GalacticPosition(long x, long y, long z, DVec3 localKm)
        {
            Carry(localKm.X, out long cx, out double lx);
            Carry(localKm.Y, out long cy, out double ly);
            Carry(localKm.Z, out long cz, out double lz);
            X = x + cx;
            Y = y + cy;
            Z = z + cz;
            LocalKm = new DVec3(lx, ly, lz);
        }

        public static readonly GalacticPosition Origin = new GalacticPosition(0, 0, 0, DVec3.Zero);

        /// <summary>Splits <paramref name="km"/> into whole sectors and what is left over, the remainder within half a sector.</summary>
        static void Carry(double km, out long sectors, out double rest)
        {
            if (double.IsNaN(km) || double.IsInfinity(km)) throw new ArgumentOutOfRangeException(nameof(km), "Not a place: " + km);
            double s = Math.Round(km / SectorKm, MidpointRounding.AwayFromZero);
            sectors = (long)s;
            rest = km - s * SectorKm;
        }

        /// <summary>The place <paramref name="ly"/> light-years from Sol's root (a catalogue star's position).</summary>
        public static GalacticPosition FromLy(DVec3 ly)
        {
            // Whole sectors first, from the light-years, so the offset is taken off a small number, not a huge one.
            static void Split(double v, out long sector, out double km)
            {
                double sectors = v * SectorsPerLy;
                double whole = Math.Round(sectors, MidpointRounding.AwayFromZero);
                sector = (long)whole;
                km = (sectors - whole) * SectorKm;
            }

            Split(ly.X, out long x, out double kx);
            Split(ly.Y, out long y, out double ky);
            Split(ly.Z, out long z, out double kz);
            return new GalacticPosition(x, y, z, new DVec3(kx, ky, kz));
        }

        /// <summary>This place moved by <paramref name="km"/>.</summary>
        public GalacticPosition Offset(DVec3 km) => new GalacticPosition(X, Y, Z, LocalKm + km);

        /// <summary>
        /// Where this place is from <paramref name="origin"/>, km: the sector difference (exact, in whole sectors) and the
        /// offsets' difference, added only at the end. Exact to well under a metre for any two places within a few
        /// light-years of each other, wherever in the galaxy they are.
        /// </summary>
        public DVec3 KmFrom(GalacticPosition origin) =>
            new DVec3((X - origin.X) * SectorKm, (Y - origin.Y) * SectorKm, (Z - origin.Z) * SectorKm) + (LocalKm - origin.LocalKm);

        /// <summary>Distance to <paramref name="other"/>, km.</summary>
        public double DistanceKm(GalacticPosition other) => KmFrom(other).Length;

        /// <summary>Distance to <paramref name="other"/>, light-years.</summary>
        public double DistanceLy(GalacticPosition other) => DistanceKm(other) / Astro.LightYearKm;

        /// <summary>Light-years from Sol's root: for maps and the interstellar view, which draw at light-year scale.</summary>
        public DVec3 Ly => new DVec3(
            (X + LocalKm.X / SectorKm) / SectorsPerLy,
            (Y + LocalKm.Y / SectorKm) / SectorsPerLy,
            (Z + LocalKm.Z / SectorKm) / SectorsPerLy);

        /// <summary>The place a share <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public static GalacticPosition Lerp(GalacticPosition a, GalacticPosition b, double t) => a.Offset(b.KmFrom(a) * t);

        public bool Equals(GalacticPosition o) => X == o.X && Y == o.Y && Z == o.Z && LocalKm.Equals(o.LocalKm);
        public override bool Equals(object obj) => obj is GalacticPosition o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = X.GetHashCode();
                h = h * 397 ^ Y.GetHashCode();
                h = h * 397 ^ Z.GetHashCode();
                return h * 397 ^ LocalKm.GetHashCode();
            }
        }

        public static bool operator ==(GalacticPosition a, GalacticPosition b) => a.Equals(b);
        public static bool operator !=(GalacticPosition a, GalacticPosition b) => !a.Equals(b);

        public override string ToString()
        {
            var ly = Ly;
            return string.Format(CultureInfo.InvariantCulture, "[{0},{1},{2}]+({3:0.###},{4:0.###},{5:0.###}) km ({6:0.###}, {7:0.###}, {8:0.###} ly)",
                X, Y, Z, LocalKm.X, LocalKm.Y, LocalKm.Z, ly.X, ly.Y, ly.Z);
        }
    }
}
