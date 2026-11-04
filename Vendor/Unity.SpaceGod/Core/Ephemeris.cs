using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Computes where every body is at a Julian date by two-body Kepler propagation of its
    /// elements. Positions are km in the J2000 ecliptic frame; "relative" means relative to the
    /// parent body, "helio" means relative to the Sun's centre.
    ///
    /// Analytic propagation (not numerical integration) is deliberate: it is exact for any time
    /// warp, never drifts, and can be evaluated at arbitrary future dates, which ship flight
    /// planning needs for intercepts.
    /// </summary>
    public sealed class Ephemeris
    {
        public BodyCatalog Catalog { get; }
        public double Jd { get; private set; } = double.NaN;

        readonly DVec3[] relative;
        readonly DVec3[] helio;
        readonly DVec3[] velocity;
        readonly FrameBasis[] frames;
        readonly int moonIndex = -1;
        readonly int earthIndex = -1;
        readonly double moonMassFraction;

        // HelioAt's last answer for each body, and the lunar theory's (see HelioAt).
        readonly double[] memoJd;
        readonly DVec3[] memoHelio;
        double lunarJd = double.NaN;
        DVec3 lunarPos;

        public Ephemeris(BodyCatalog catalog)
        {
            Catalog = catalog;
            relative = new DVec3[catalog.Count];
            helio = new DVec3[catalog.Count];
            velocity = new DVec3[catalog.Count];
            frames = new FrameBasis[catalog.Count];
            memoJd = new double[catalog.Count];
            memoHelio = new DVec3[catalog.Count];
            for (int i = 0; i < memoJd.Length; i++) memoJd[i] = double.NaN;

            for (int i = 0; i < catalog.Count; i++)
            {
                var b = catalog[i];
                if (b.Plane != OrbitPlane.ParentEquator || b.ParentIndex < 0)
                    frames[i] = FrameBasis.Ecliptic;
                else if (!double.IsNaN(b.LaplacePoleRaDeg))
                    frames[i] = FrameBasis.FromPole(Astro.PoleFromRaDec(b.LaplacePoleRaDeg, b.LaplacePoleDecDeg));
                else
                    frames[i] = FrameBasis.FromPole(catalog[b.ParentIndex].SpinAxis);
            }

            if (catalog.TryGet("Earth", out var earth) && catalog.TryGet("Moon", out var moon) && moon.ParentIndex == earth.Index)
            {
                earthIndex = earth.Index;
                moonIndex = moon.Index;
                moonMassFraction = moon.Gm / (earth.Gm + moon.Gm);
            }
        }

        public DVec3 Relative(int body) => relative[body];
        public DVec3 Helio(int body) => helio[body];

        /// <summary>Velocity relative to the parent, km/s.</summary>
        public DVec3 RelativeVelocityKmS(int body) => velocity[body] / Astro.SecondsPerDay;

        public void Update(double jd)
        {
            Jd = jd;
            for (int i = 0; i < Catalog.Count; i++)
            {
                var b = Catalog[i];
                if (b.Elements == null)
                {
                    relative[i] = DVec3.Zero;
                    velocity[i] = DVec3.Zero;
                    continue;
                }
                var k = b.Elements.At(jd);
                relative[i] = frames[i].ToEcliptic(Kepler.Position(k)) * b.OrbitScale;
                velocity[i] = frames[i].ToEcliptic(Kepler.Velocity(k)) * b.OrbitScale;
            }

            // The Moon follows a lunar theory, not an ellipse (see LunarTheory).
            if (moonIndex >= 0)
            {
                relative[moonIndex] = Lunar(jd);
                const double h = 1e-3;
                velocity[moonIndex] = (LunarTheory.Position(jd + h) - LunarTheory.Position(jd - h)) / (2 * h);
            }

            // JPL's "Earth" elements are the Earth-Moon barycentre; Earth sits 4,670 km from it.
            if (earthIndex >= 0)
            {
                relative[earthIndex] -= relative[moonIndex] * moonMassFraction;
                velocity[earthIndex] -= velocity[moonIndex] * moonMassFraction;
            }

            for (int i = 0; i < Catalog.Count; i++)
            {
                int p = Catalog[i].ParentIndex;
                helio[i] = p < 0 ? relative[i] : helio[p] + relative[i];
            }
        }

        /// <summary>Heliocentric position of one body at an arbitrary date, without touching the cached frame.</summary>
        /// <remarks>
        /// Each body keeps the last date it was asked for and the answer (U.SPC-T104): a flight plan samples a
        /// world, its moons and the frame they share at the same date one after another, and the moons' parents
        /// and the Earth's lunar theory are then worked out once, not once a moon. The answer is the same either way.
        /// </remarks>
        public DVec3 HelioAt(int body, double jd)
        {
            var b = Catalog[body];
            if (b.Elements == null) return DVec3.Zero;
            if (memoJd[body] == jd) return memoHelio[body];
            var rel = body == moonIndex ? Lunar(jd) : frames[body].ToEcliptic(Kepler.Position(b.Elements.At(jd))) * b.OrbitScale;
            if (body == earthIndex)
                rel -= RelativeAt(moonIndex, jd) * moonMassFraction;
            var at = HelioAt(b.ParentIndex, jd) + rel;
            memoJd[body] = jd;
            memoHelio[body] = at;
            return at;
        }

        DVec3 RelativeAt(int body, double jd) =>
            body == moonIndex ? Lunar(jd) : frames[body].ToEcliptic(Kepler.Position(Catalog[body].Elements.At(jd))) * Catalog[body].OrbitScale;

        /// <summary>The Moon about the Earth by the lunar theory, worked out once for each date asked in turn.</summary>
        DVec3 Lunar(double jd)
        {
            if (lunarJd != jd)
            {
                lunarPos = LunarTheory.Position(jd);
                lunarJd = jd;
            }
            return lunarPos;
        }

        /// <summary>
        /// Samples one full orbit starting at the body's current position and running backwards
        /// along it, so a fading alpha ramp along the samples reads as a trail behind the body.
        /// </summary>
        public void SampleOrbitTrail(int body, double jd, DVec3[] output)
        {
            var b = Catalog[body];
            var k = b.Elements.At(jd);
            double now = Kepler.SolveEccentricAnomaly(k.MeanAnomaly, k.E);
            int n = output.Length;
            double sense = k.MeanMotion >= 0 ? 1 : -1;
            // The Moon (lunar theory) and Earth (Earth-Moon barycentre offset) are drawn off their
            // mean Kepler ellipse; shift the whole trail so it still meets the body where it is drawn.
            DVec3 offset = DVec3.Zero;
            if (body == moonIndex) offset = LunarTheory.Position(jd) - frames[body].ToEcliptic(Kepler.Position(k)) * b.OrbitScale;
            else if (body == earthIndex) offset = -LunarTheory.Position(jd) * moonMassFraction;
            for (int i = 0; i < n; i++)
            {
                double ecc = now - sense * Astro.TwoPi * i / Math.Max(1, n - 1);
                double m = ecc - k.E * Math.Sin(ecc);
                output[i] = frames[body].ToEcliptic(Kepler.Position(k.WithMeanAnomaly(m))) * b.OrbitScale + offset;
            }
        }

        /// <summary>Orbital speed relative to the parent (km/s) from vis-viva.</summary>
        public double OrbitalSpeedKmS(int body)
        {
            var b = Catalog[body];
            if (b.ParentIndex < 0 || b.Elements == null) return 0;
            if (b.OrbitScale != 1) return RelativeVelocityKmS(body).Length;
            double gm = Catalog[b.ParentIndex].Gm + b.Gm;
            return Kepler.VisVivaSpeed(gm, relative[body].Length, b.Elements.At(Jd).A);
        }

        readonly struct FrameBasis
        {
            readonly DVec3 x;
            readonly DVec3 y;
            readonly DVec3 z;

            FrameBasis(DVec3 x, DVec3 y, DVec3 z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public static readonly FrameBasis Ecliptic = new FrameBasis(DVec3.UnitX, DVec3.UnitY, DVec3.UnitZ);

            /// <summary>Equatorial frame of a pole: x toward the equator's ascending node on the ecliptic.</summary>
            public static FrameBasis FromPole(DVec3 pole)
            {
                var z = pole.Normalized;
                var node = DVec3.Cross(DVec3.UnitZ, z);
                var x = node.LengthSquared < 1e-12 ? DVec3.UnitX : node.Normalized;
                var y = DVec3.Cross(z, x);
                return new FrameBasis(x, y, z);
            }

            public DVec3 ToEcliptic(DVec3 v) => x * v.X + y * v.Y + z * v.Z;
        }
    }
}
