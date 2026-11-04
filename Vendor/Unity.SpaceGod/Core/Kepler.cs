using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>Instantaneous classical elements: angles in radians, a in km, mean motion in rad/day.</summary>
    public readonly struct KeplerState
    {
        public readonly double A;
        public readonly double E;
        public readonly double I;
        public readonly double Node;
        public readonly double ArgPeri;
        public readonly double MeanAnomaly;
        public readonly double MeanMotion;

        public KeplerState(double a, double e, double i, double node, double argPeri, double meanAnomaly, double meanMotion)
        {
            A = a;
            E = e;
            I = i;
            Node = node;
            ArgPeri = argPeri;
            MeanAnomaly = meanAnomaly;
            MeanMotion = meanMotion;
        }

        public KeplerState WithMeanAnomaly(double m) => new KeplerState(A, E, I, Node, ArgPeri, m, MeanMotion);

        public double PeriodDays => Astro.TwoPi / Math.Abs(MeanMotion);
    }

    /// <summary>Two-body (Keplerian) propagation: Kepler's equation and the perifocal-to-reference rotation.</summary>
    public static class Kepler
    {
        /// <summary>Solves M = E - e·sin(E) for E by Newton-Raphson; robust up to e≈0.99.</summary>
        public static double SolveEccentricAnomaly(double meanAnomaly, double e)
        {
            double m = Astro.WrapRadians(meanAnomaly);
            double ecc = e < 0.8 ? m + e * Math.Sin(m) : (m >= 0 ? Math.PI : -Math.PI);
            for (int iter = 0; iter < 60; iter++)
            {
                double f = ecc - e * Math.Sin(ecc) - m;
                double fp = 1 - e * Math.Cos(ecc);
                double delta = f / fp;
                ecc -= delta;
                if (Math.Abs(delta) < 1e-14) break;
            }
            return ecc;
        }

        /// <summary>Position (km) in the elements' reference plane (x toward the reference direction, z toward its pole).</summary>
        public static DVec3 Position(in KeplerState k)
        {
            double ecc = SolveEccentricAnomaly(k.MeanAnomaly, k.E);
            double xp = k.A * (Math.Cos(ecc) - k.E);
            double yp = k.A * Math.Sqrt(1 - k.E * k.E) * Math.Sin(ecc);
            return Rotate(k, xp, yp);
        }

        /// <summary>Velocity (km/day) in the same frame as <see cref="Position"/>.</summary>
        public static DVec3 Velocity(in KeplerState k)
        {
            double ecc = SolveEccentricAnomaly(k.MeanAnomaly, k.E);
            double eDot = k.MeanMotion / (1 - k.E * Math.Cos(ecc));
            double vxp = -k.A * Math.Sin(ecc) * eDot;
            double vyp = k.A * Math.Sqrt(1 - k.E * k.E) * Math.Cos(ecc) * eDot;
            return Rotate(k, vxp, vyp);
        }

        static DVec3 Rotate(in KeplerState k, double xp, double yp)
        {
            double cw = Math.Cos(k.ArgPeri), sw = Math.Sin(k.ArgPeri);
            double cn = Math.Cos(k.Node), sn = Math.Sin(k.Node);
            double ci = Math.Cos(k.I), si = Math.Sin(k.I);
            double x = (cw * cn - sw * sn * ci) * xp + (-sw * cn - cw * sn * ci) * yp;
            double y = (cw * sn + sw * cn * ci) * xp + (-sw * sn + cw * cn * ci) * yp;
            double z = sw * si * xp + cw * si * yp;
            return new DVec3(x, y, z);
        }

        /// <summary>Circular-orbit angular rate (rad/day) at radius r around a body with gravitational parameter gm (km³/s²).</summary>
        public static double CircularRate(double gm, double radiusKm) =>
            Math.Sqrt(gm / (radiusKm * radiusKm * radiusKm)) * Astro.SecondsPerDay;

        /// <summary>Vis-viva speed in km/s.</summary>
        public static double VisVivaSpeed(double gm, double rKm, double aKm) =>
            Math.Sqrt(Math.Max(0, gm * (2.0 / rKm - 1.0 / aKm)));
    }
}
