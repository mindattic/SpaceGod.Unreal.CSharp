using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The main asteroid belt and Jupiter's Trojans as real objects: osculating heliocentric
    /// elements from the JPL Small-Body Database (SBDB query API), propagated two-body with
    /// Kepler's equation.
    ///
    /// Data (Resources/Data/asteroids.bytes, built from the SBDB query of 2026-10-11 with
    /// full-prec=1): every numbered main-belt asteroid (sb-class MBA) brighter than absolute
    /// magnitude H 15.5, and every Jupiter Trojan (TJN) brighter than H 15; Ceres is left out
    /// because it is a full body. All elements share one osculating epoch.
    ///
    /// Binary layout (little-endian): "AST1", int32 count, float64 epoch JD (TDB), then per
    /// asteroid 36 bytes: float32 a (AU), e, i, Ω, ω, M (deg, J2000 ecliptic), H; int32 number;
    /// uint8 group (0 main belt, 1 Trojan); 3 bytes padding. Sorted brightest first.
    ///
    /// Two-body propagation ignores Jupiter's perturbations: away from the epoch a belt asteroid
    /// drifts from its true place by roughly 0.01 AU per year (see the tests against JPL Horizons).
    /// Kirkwood gaps and the Trojan clouds are in the data itself, not modelled.
    /// </summary>
    public sealed class AsteroidField
    {
        /// <summary>Gaussian gravitational constant: mean motion (rad/day) of a 1 AU orbit around the Sun.</summary>
        public const double GaussK = 0.01720209895;

        public const int RecordBytes = 36;

        public int Count { get; }
        public double EpochJd { get; }

        public readonly float[] A;
        public readonly float[] E;
        public readonly float[] InclinationDeg;
        public readonly float[] NodeDeg;
        public readonly float[] ArgPeriDeg;
        public readonly float[] MeanAnomalyDeg;
        public readonly float[] H;
        public readonly int[] Number;
        public readonly byte[] Group;

        /// <summary>Mean motion, rad/day.</summary>
        public readonly double[] MeanMotion;

        // Perifocal basis: P toward perihelion, Q 90° ahead in the orbit plane (ecliptic J2000 unit vectors).
        readonly double[] px, py, pz, qx, qy, qz;

        AsteroidField(int count, double epochJd)
        {
            Count = count;
            EpochJd = epochJd;
            A = new float[count];
            E = new float[count];
            InclinationDeg = new float[count];
            NodeDeg = new float[count];
            ArgPeriDeg = new float[count];
            MeanAnomalyDeg = new float[count];
            H = new float[count];
            Number = new int[count];
            Group = new byte[count];
            MeanMotion = new double[count];
            px = new double[count];
            py = new double[count];
            pz = new double[count];
            qx = new double[count];
            qy = new double[count];
            qz = new double[count];
        }

        public static AsteroidField Parse(byte[] data)
        {
            if (data == null || data.Length < 16 || data[0] != 'A' || data[1] != 'S' || data[2] != 'T' || data[3] != '1')
                throw new FormatException("Not an AST1 asteroid file");
            int count = BitConverter.ToInt32(data, 4);
            double epoch = BitConverter.ToDouble(data, 8);
            if (count < 0 || data.Length < 16 + (long)count * RecordBytes)
                throw new FormatException("Truncated asteroid file");

            var f = new AsteroidField(count, epoch);
            int o = 16;
            for (int i = 0; i < count; i++, o += RecordBytes)
            {
                f.A[i] = BitConverter.ToSingle(data, o);
                f.E[i] = BitConverter.ToSingle(data, o + 4);
                f.InclinationDeg[i] = BitConverter.ToSingle(data, o + 8);
                f.NodeDeg[i] = BitConverter.ToSingle(data, o + 12);
                f.ArgPeriDeg[i] = BitConverter.ToSingle(data, o + 16);
                f.MeanAnomalyDeg[i] = BitConverter.ToSingle(data, o + 20);
                f.H[i] = BitConverter.ToSingle(data, o + 24);
                f.Number[i] = BitConverter.ToInt32(data, o + 28);
                f.Group[i] = data[o + 32];
                f.Derive(i);
            }
            return f;
        }

        void Derive(int i)
        {
            double a = A[i];
            MeanMotion[i] = GaussK / (a * Math.Sqrt(a));
            double w = ArgPeriDeg[i] * Astro.Deg2Rad, n = NodeDeg[i] * Astro.Deg2Rad, inc = InclinationDeg[i] * Astro.Deg2Rad;
            double cw = Math.Cos(w), sw = Math.Sin(w), cn = Math.Cos(n), sn = Math.Sin(n), ci = Math.Cos(inc), si = Math.Sin(inc);
            px[i] = cw * cn - sw * sn * ci;
            py[i] = cw * sn + sw * cn * ci;
            pz[i] = sw * si;
            qx[i] = -sw * cn - cw * sn * ci;
            qy[i] = -sw * sn + cw * cn * ci;
            qz[i] = cw * si;
        }

        /// <summary>The geometric albedo assumed for an asteroid's size from its brightness (a typical belt value).</summary>
        public const double TypicalAlbedo = 0.14;

        /// <summary>
        /// One asteroid's diameter, km, from its absolute magnitude: D = 1329 km / √p · 10^(−H/5) with the
        /// <see cref="TypicalAlbedo"/> (so H 15 is about 3.6 km): the size of the rock a hull can fly into (U.SPC-T60).
        /// </summary>
        public double DiameterKm(int i) => 1329 / Math.Sqrt(TypicalAlbedo) * Math.Pow(10, -H[i] / 5.0);

        /// <summary>Heliocentric position (AU, ecliptic J2000) of one asteroid.</summary>
        public DVec3 PositionAu(int i, double jd)
        {
            double m = MeanAnomalyDeg[i] * Astro.Deg2Rad + MeanMotion[i] * (jd - EpochJd);
            double e = E[i];
            double ecc = Kepler.SolveEccentricAnomaly(m, e);
            double x = A[i] * (Math.Cos(ecc) - e);
            double y = A[i] * Math.Sqrt(1 - e * e) * Math.Sin(ecc);
            return new DVec3(px[i] * x + qx[i] * y, py[i] * x + qy[i] * y, pz[i] * x + qz[i] * y);
        }

        /// <summary>
        /// Positions (AU, ecliptic J2000) of a range of asteroids into <paramref name="xyz"/>
        /// (3 floats each, written at the asteroid's own index). Allocation-free, so it can run on a
        /// stride of the field every frame or on a worker thread.
        /// </summary>
        public void PositionsAu(double jd, int start, int count, float[] xyz)
        {
            int end = Math.Min(Count, start + count);
            double dt = jd - EpochJd;
            for (int i = start; i < end; i++)
            {
                double e = E[i];
                double m = Astro.WrapRadians(MeanAnomalyDeg[i] * Astro.Deg2Rad + MeanMotion[i] * dt);
                // Newton's method from a good start; e < 0.5 for everything in the file.
                double ecc = m + e * Math.Sin(m);
                for (int k = 0; k < 6; k++)
                    ecc -= (ecc - e * Math.Sin(ecc) - m) / (1 - e * Math.Cos(ecc));
                double x = A[i] * (Math.Cos(ecc) - e);
                double y = A[i] * Math.Sqrt(1 - e * e) * Math.Sin(ecc);
                xyz[i * 3] = (float)(px[i] * x + qx[i] * y);
                xyz[i * 3 + 1] = (float)(py[i] * x + qy[i] * y);
                xyz[i * 3 + 2] = (float)(pz[i] * x + qz[i] * y);
            }
        }

        public const int GpuFloatsPerAsteroid = 12;

        /// <summary>
        /// Packs the per-asteroid constants for the GPU (Asteroids.shader solves Kepler per vertex):
        /// (e, n rad/day, M at epoch rad, H), (a·P, 0), (b·Q, 0) with b = a·√(1-e²).
        /// </summary>
        public void PackGpu(float[] dest)
        {
            for (int i = 0; i < Count; i++)
            {
                int o = i * GpuFloatsPerAsteroid;
                double a = A[i], b = a * Math.Sqrt(1 - E[i] * (double)E[i]);
                dest[o] = E[i];
                dest[o + 1] = (float)MeanMotion[i];
                dest[o + 2] = (float)(Astro.WrapRadians(MeanAnomalyDeg[i] * Astro.Deg2Rad) );
                dest[o + 3] = H[i];
                dest[o + 4] = (float)(a * px[i]);
                dest[o + 5] = (float)(a * py[i]);
                dest[o + 6] = (float)(a * pz[i]);
                dest[o + 8] = (float)(b * qx[i]);
                dest[o + 9] = (float)(b * qy[i]);
                dest[o + 10] = (float)(b * qz[i]);
            }
        }

        /// <summary>
        /// Single-precision mirror of the vertex shader's propagation (same packing, same iteration
        /// count), so the tests can hold the GPU path to the double-precision one.
        /// </summary>
        public static void GpuPosition(float[] packed, int i, float dtDays, out float x, out float y, out float z)
        {
            int o = i * GpuFloatsPerAsteroid;
            float e = packed[o];
            float m = packed[o + 2] + packed[o + 1] * dtDays;
            const float twoPi = 6.2831853f;
            m -= twoPi * (float)Math.Floor(m / twoPi + 0.5f);
            float ecc = m + e * (float)Math.Sin(m);
            for (int k = 0; k < 5; k++)
                ecc -= (ecc - e * (float)Math.Sin(ecc) - m) / (1 - e * (float)Math.Cos(ecc));
            float c = (float)Math.Cos(ecc) - e, s = (float)Math.Sin(ecc);
            x = packed[o + 4] * c + packed[o + 8] * s;
            y = packed[o + 5] * c + packed[o + 9] * s;
            z = packed[o + 6] * c + packed[o + 10] * s;
        }

        /// <summary>Index of a numbered asteroid, or -1.</summary>
        public int IndexOfNumber(int number)
        {
            for (int i = 0; i < Count; i++)
                if (Number[i] == number) return i;
            return -1;
        }

        /// <summary>
        /// The Sun's gameplay/realistic distance mapping (<see cref="ScaleModel.LocalDistance"/> for the
        /// Sun) reduced to the constants the asteroid shader needs: (blend, units per AU at realistic
        /// scale, gameplay Sun radius, gameplay units per AU^p) and (p, (R_sun/AU)^p).
        /// </summary>
        public static void SunMapping(ScaleModel scale, BodyDefinition sun, out float[] c0, out float[] c1)
        {
            double sunAu = sun.RadiusKm / Astro.AuKm;
            c0 = new[]
            {
                (float)scale.Blend, (float)(Astro.AuKm / ScaleModel.KmPerUnit),
                (float)ScaleModel.GameplayRadius(sun.RadiusKm), (float)ScaleModel.GameAuScale,
            };
            c1 = new[] { (float)ScaleModel.GameAuExponent, (float)Math.Pow(sunAu, ScaleModel.GameAuExponent), 0f, 0f };
        }

        /// <summary>Single-precision mirror of the shader's Sun mapping: scene distance for a heliocentric distance in AU.</summary>
        public static float SceneDistanceFromSun(float[] c0, float[] c1, float dAu)
        {
            float real = dAu * c0[1];
            float game = c0[2] + c0[3] * ((float)Math.Pow(dAu, c1[0]) - c1[1]);
            return (float)Math.Exp(Math.Log(real) + (Math.Log(game) - Math.Log(real)) * c0[0]);
        }
    }
}
