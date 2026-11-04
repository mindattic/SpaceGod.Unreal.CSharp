using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Body-fixed frames, so colour maps face the right way: the map's north pole and the direction
    /// of its 0° meridian on the equator, both as ecliptic J2000 unit vectors.
    ///
    /// Bodies with an IAU prime meridian (planets, Ceres, Pluto) use W = W0 + dW/dt·d (Archinal et
    /// al. 2018, IAU WGCCRE; periodic terms dropped), measured from the node Q of the body's equator
    /// on the ICRF equator, at right ascension α0 + 90°. Synchronous moons point their 0° meridian
    /// at the parent, which is how the IAU defines it for them (mean sub-planet longitude 0°).
    ///
    /// Maps are east-positive: with the map's north up, longitude increases to the right, which is
    /// counter-clockwise seen from above the north pole.
    /// </summary>
    public static class BodyOrientation
    {
        /// <summary>The map's north pole (ecliptic unit vector).</summary>
        public static DVec3 MapNorth(BodyDefinition body) => body.MapNorthFlipped ? -body.Pole : body.Pole;

        /// <summary>Prime meridian rate about the map's north pole, deg/day (prograde rotators are positive about their north).</summary>
        public static double RateDegPerDay(BodyDefinition body)
        {
            if (!double.IsNaN(body.PrimeMeridianRateDegPerDay)) return body.PrimeMeridianRateDegPerDay;
            if (body.RotationHours == 0) return 0;
            double aboutPole = 360.0 * 24.0 / body.RotationHours;
            return body.MapNorthFlipped ? -aboutPole : aboutPole;
        }

        /// <summary>
        /// The map's north pole and its 0°-longitude direction at <paramref name="jd"/>.
        /// <paramref name="towardParent"/> is the unit vector from the body to its parent, used by synchronous moons.
        /// </summary>
        public static void Frame(BodyDefinition body, double jd, DVec3 towardParent, out DVec3 north, out DVec3 primeMeridian) =>
            Frame(body, jd, towardParent, DVec3.Zero, out north, out primeMeridian);

        /// <summary>
        /// As above. A synchronous moon with no IAU pole of its own spins about its orbit normal
        /// (<paramref name="orbitNormal"/>, r × v relative to the parent): its spin axis follows the orbit as
        /// it precesses, which matters for Triton, inclined 157° to Neptune's equator.
        /// </summary>
        public static void Frame(BodyDefinition body, double jd, DVec3 towardParent, DVec3 orbitNormal, out DVec3 north, out DVec3 primeMeridian)
        {
            north = MapNorth(body);
            if (body.FacesParent && double.IsNaN(body.PoleRaDeg) && orbitNormal.LengthSquared > 0)
            {
                var n = orbitNormal.Normalized;
                north = DVec3.Dot(n, north) >= 0 ? n : -n;
            }
            if (body.FacesParent && towardParent.LengthSquared > 0)
            {
                // Sub-parent point, projected onto the equator.
                var p = towardParent - north * DVec3.Dot(towardParent, north);
                if (p.LengthSquared > 1e-18)
                {
                    primeMeridian = p.Normalized;
                    return;
                }
            }

            var node = EquatorNode(north);
            var east = DVec3.Cross(north, node);
            double w0 = double.IsNaN(body.PrimeMeridianDeg) ? 0 : body.PrimeMeridianDeg;
            double w = (w0 + RateDegPerDay(body) * (jd - Astro.J2000)) % 360.0 * Astro.Deg2Rad;
            primeMeridian = node * Math.Cos(w) + east * Math.Sin(w);
        }

        /// <summary>Ascending node Q of the equator of a pole on the ICRF equator (RA = α0 + 90°), as an ecliptic vector.</summary>
        public static DVec3 EquatorNode(DVec3 northEcliptic)
        {
            var eq = EclipticToEquatorial(northEcliptic);
            var q = DVec3.Cross(DVec3.UnitZ, eq);
            // A pole exactly at the celestial pole (Earth, α0 = 0) has its node at RA 90°.
            if (q.LengthSquared < 1e-18) q = DVec3.UnitY;
            return EquatorialToEcliptic(q.Normalized);
        }

        /// <summary>Planetocentric east longitude (deg, -180..180) and latitude of a body-fixed direction.</summary>
        public static void LonLat(DVec3 north, DVec3 primeMeridian, DVec3 direction, out double lonDeg, out double latDeg)
        {
            var east = DVec3.Cross(north, primeMeridian);
            var d = direction.Normalized;
            latDeg = Math.Asin(Math.Max(-1, Math.Min(1, DVec3.Dot(d, north)))) * Astro.Rad2Deg;
            lonDeg = Math.Atan2(DVec3.Dot(d, east), DVec3.Dot(d, primeMeridian)) * Astro.Rad2Deg;
        }

        static DVec3 EclipticToEquatorial(DVec3 v)
        {
            double eps = Astro.ObliquityDeg * Astro.Deg2Rad;
            return new DVec3(v.X, v.Y * Math.Cos(eps) - v.Z * Math.Sin(eps), v.Y * Math.Sin(eps) + v.Z * Math.Cos(eps));
        }

        static DVec3 EquatorialToEcliptic(DVec3 v)
        {
            double eps = Astro.ObliquityDeg * Astro.Deg2Rad;
            return new DVec3(v.X, v.Y * Math.Cos(eps) + v.Z * Math.Sin(eps), -v.Y * Math.Sin(eps) + v.Z * Math.Cos(eps));
        }
    }
}
