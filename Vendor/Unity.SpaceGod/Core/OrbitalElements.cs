using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Keplerian elements with linear secular rates, in the layout of JPL's "Keplerian Elements
    /// for Approximate Positions of the Major Planets" (Standish): a, e, i, mean longitude L,
    /// longitude of perihelion ϖ, longitude of ascending node Ω, each with a per-century rate.
    /// Every body uses this one shape; moons and minor planets are converted into it.
    /// </summary>
    public sealed class OrbitalElements
    {
        public double EpochJd { get; private set; }
        public double AKm { get; private set; }
        public double ADotKmPerCentury { get; private set; }
        public double E { get; private set; }
        public double EDot { get; private set; }
        public double IDeg { get; private set; }
        public double IDot { get; private set; }
        public double LDeg { get; private set; }
        public double LDotDegPerCentury { get; private set; }
        public double VarpiDeg { get; private set; }
        public double VarpiDot { get; private set; }
        public double NodeDeg { get; private set; }
        public double NodeDot { get; private set; }

        /// <summary>
        /// Secular acceleration of the mean longitude: the coefficient of t² (degrees per century²).
        /// Zero except for Phobos, whose orbit is decaying under Mars's tides.
        /// </summary>
        public double LDotDotDegPerCentury2 { get; private set; }

        OrbitalElements() { }

        /// <summary>JPL planet table entry (J2000 epoch, a in AU, rates per Julian century).</summary>
        public static OrbitalElements FromJpl(
            double aAu, double e, double iDeg, double lDeg, double varpiDeg, double nodeDeg,
            double aDotAu, double eDot, double iDot, double lDot, double varpiDot, double nodeDot)
        {
            return new OrbitalElements
            {
                EpochJd = Astro.J2000,
                AKm = aAu * Astro.AuKm,
                ADotKmPerCentury = aDotAu * Astro.AuKm,
                E = e,
                EDot = eDot,
                IDeg = iDeg,
                IDot = iDot,
                LDeg = lDeg,
                LDotDegPerCentury = lDot,
                VarpiDeg = varpiDeg,
                VarpiDot = varpiDot,
                NodeDeg = nodeDeg,
                NodeDot = nodeDot,
            };
        }

        /// <summary>
        /// Classical elements at an epoch (SBDB / satellite tables), with optional nodal and
        /// apsidal precession in degrees per day.
        /// </summary>
        public static OrbitalElements FromClassical(
            double epochJd, double aKm, double e, double iDeg, double nodeDeg, double argPeriDeg,
            double meanAnomalyDeg, double periodDays,
            double nodeRateDegPerDay = 0, double varpiRateDegPerDay = 0, double meanLongitudeAccelDegPerYear2 = 0)
        {
            double varpi = nodeDeg + argPeriDeg;
            return new OrbitalElements
            {
                LDotDotDegPerCentury2 = meanLongitudeAccelDegPerYear2 * 1e4,
                EpochJd = epochJd,
                AKm = aKm,
                E = e,
                IDeg = iDeg,
                LDeg = meanAnomalyDeg + varpi,
                LDotDegPerCentury = 360.0 / periodDays * Astro.DaysPerCentury,
                VarpiDeg = varpi,
                VarpiDot = varpiRateDegPerDay * Astro.DaysPerCentury,
                NodeDeg = nodeDeg,
                NodeDot = nodeRateDegPerDay * Astro.DaysPerCentury,
            };
        }

        /// <summary>
        /// An orbit given by its geometry in the ecliptic frame: the direction of periapsis and the
        /// orbit's normal (the right-hand pole of its motion). Used where the real orbit is measured
        /// in another frame (a visual binary on the sky) or only its shape is known (exoplanets).
        /// </summary>
        public static OrbitalElements FromVectors(double epochJd, double aKm, double e, DVec3 periapsisDirection, DVec3 normal,
            double meanAnomalyDeg, double periodDays)
        {
            var h = normal.Normalized;
            var p = (periapsisDirection - h * DVec3.Dot(periapsisDirection, h)).Normalized;
            double i = Math.Acos(Math.Max(-1, Math.Min(1, h.Z))) * Astro.Rad2Deg;
            var n = DVec3.Cross(DVec3.UnitZ, h);
            n = n.LengthSquared < 1e-12 ? DVec3.UnitX : n.Normalized;
            double node = Math.Atan2(n.Y, n.X) * Astro.Rad2Deg;
            double argPeri = Math.Atan2(DVec3.Dot(DVec3.Cross(n, p), h), DVec3.Dot(n, p)) * Astro.Rad2Deg;
            return FromClassical(epochJd, aKm, e, i, node, argPeri, meanAnomalyDeg, periodDays);
        }

        /// <summary>
        /// The periapsis direction and orbit normal (ecliptic J2000) of a visual binary's relative orbit
        /// (Campbell elements: inclination, position angle of the node, argument of periastron), for a
        /// pair at the given right ascension and declination. The sky is spanned by north, east and the
        /// line of sight away from us; which of the two nodes is ascending cannot be told from images
        /// alone, so the convention that the companion recedes past the given node is used.
        /// </summary>
        public static void VisualBinaryGeometry(double raDeg, double decDeg, double iDeg, double nodeDeg, double argPeriDeg,
            out DVec3 periapsisDirection, out DVec3 normal)
        {
            double ra = raDeg * Astro.Deg2Rad, dec = decDeg * Astro.Deg2Rad;
            var north = new DVec3(-Math.Sin(dec) * Math.Cos(ra), -Math.Sin(dec) * Math.Sin(ra), Math.Cos(dec));
            var east = new DVec3(-Math.Sin(ra), Math.Cos(ra), 0);
            var away = new DVec3(Math.Cos(dec) * Math.Cos(ra), Math.Cos(dec) * Math.Sin(ra), Math.Sin(dec));
            double i = iDeg * Astro.Deg2Rad, node = nodeDeg * Astro.Deg2Rad, w = argPeriDeg * Astro.Deg2Rad;
            DVec3 Sky(double u) =>
                north * (Math.Cos(u) * Math.Cos(node) - Math.Sin(u) * Math.Sin(node) * Math.Cos(i))
                + east * (Math.Cos(u) * Math.Sin(node) + Math.Sin(u) * Math.Cos(node) * Math.Cos(i))
                + away * (Math.Sin(u) * Math.Sin(i));
            var p = Astro.EquatorialToEcliptic(Sky(w));
            var q = Astro.EquatorialToEcliptic(Sky(w + Math.PI / 2));
            periapsisDirection = p.Normalized;
            normal = DVec3.Cross(p, q).Normalized;
        }

        /// <summary>Kepler's third law: period in days for semi-major axis a (km) around gm (km³/s²).</summary>
        public static double PeriodFromGm(double aKm, double gm) =>
            Astro.TwoPi * Math.Sqrt(aKm * aKm * aKm / gm) / Astro.SecondsPerDay;

        public KeplerState At(double jd)
        {
            double t = (jd - EpochJd) / Astro.DaysPerCentury;
            double a = AKm + ADotKmPerCentury * t;
            double e = Math.Max(0, Math.Min(0.99, E + EDot * t));
            double i = (IDeg + IDot * t) * Astro.Deg2Rad;
            double l = LDeg + LDotDegPerCentury * t + LDotDotDegPerCentury2 * t * t;
            double varpi = VarpiDeg + VarpiDot * t;
            double node = NodeDeg + NodeDot * t;
            double m = Astro.WrapRadians((l - varpi) * Astro.Deg2Rad);
            // Mean motion is dM/dt = dL/dt - d(varpi)/dt, not the mean-longitude rate alone.
            double n = (LDotDegPerCentury + 2 * LDotDotDegPerCentury2 * t - VarpiDot) / Astro.DaysPerCentury * Astro.Deg2Rad;
            return new KeplerState(a, e, i, node * Astro.Deg2Rad, (varpi - node) * Astro.Deg2Rad, m, n);
        }
    }
}
