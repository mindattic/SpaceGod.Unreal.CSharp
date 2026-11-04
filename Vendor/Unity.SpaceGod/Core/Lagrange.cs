using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>The five equilibrium points of a two-body pair.</summary>
    public enum LagrangePoint
    {
        None = 0,

        /// <summary>Between the two, on the line joining them (Sun-Earth L1: SOHO, 1.5 million km sunward).</summary>
        L1 = 1,

        /// <summary>Beyond the smaller body, on the far side from the larger (Sun-Earth L2: JWST).</summary>
        L2 = 2,

        /// <summary>On the far side of the larger body, a little inside the smaller one's orbit.</summary>
        L3 = 3,

        /// <summary>60° ahead of the smaller body along its orbit (stable: Jupiter's Greek camp).</summary>
        L4 = 4,

        /// <summary>60° behind it (stable: Jupiter's Trojan camp).</summary>
        L5 = 5,
    }

    /// <summary>
    /// The Lagrange points of every body pair, from the circular restricted three-body problem
    /// applied to the pair's instantaneous geometry: L1, L2 and L3 on the line through the two at
    /// the distances the collinear quintics give for the pair's mass ratio, L4 and L5 at the
    /// third corners of the equilateral triangles in the smaller body's orbital plane. This is
    /// how JPL Horizons defines its Lagrange-point ephemerides (SEMB-L1 to L5 for the Sun and the
    /// Earth-Moon barycentre, EM-L1 to L5 for Earth and the Moon). Each point moves with its pair,
    /// so anything put there stays co-orbital.
    /// </summary>
    public static class Lagrange
    {
        static readonly double Sin60 = Math.Sqrt(3) / 2;

        /// <summary>The smaller body's share of the pair's mass, μ = m₂ / (m₁ + m₂).</summary>
        public static double MassRatio(double gmPrimary, double gmSecondary) => gmSecondary / (gmPrimary + gmSecondary);

        /// <summary>
        /// For L1 and L2, the point's distance from the smaller body; for L3, its distance from the
        /// larger; as a fraction of their separation. Newton's method on the collinear quintics
        /// (Szebehely 1967, Theory of Orbits, §4.5). Zero for L4 and L5.
        /// </summary>
        public static double CollinearDistance(double mu, LagrangePoint point)
        {
            mu = Math.Max(mu, 1e-15);
            double g;
            Func<double, double> f, df;
            switch (point)
            {
                case LagrangePoint.L1:
                    g = Math.Pow(mu / 3, 1.0 / 3);
                    f = x => ((((x - (3 - mu)) * x + (3 - 2 * mu)) * x - mu) * x + 2 * mu) * x - mu;
                    df = x => (((5 * x - 4 * (3 - mu)) * x + 3 * (3 - 2 * mu)) * x - 2 * mu) * x + 2 * mu;
                    break;
                case LagrangePoint.L2:
                    g = Math.Pow(mu / 3, 1.0 / 3);
                    f = x => ((((x + (3 - mu)) * x + (3 - 2 * mu)) * x - mu) * x - 2 * mu) * x - mu;
                    df = x => (((5 * x + 4 * (3 - mu)) * x + 3 * (3 - 2 * mu)) * x - 2 * mu) * x - 2 * mu;
                    break;
                case LagrangePoint.L3:
                    g = 1 - 7 * mu / 12;
                    f = x => ((((x + (2 + mu)) * x + (1 + 2 * mu)) * x - (1 - mu)) * x - 2 * (1 - mu)) * x - (1 - mu);
                    df = x => (((5 * x + 4 * (2 + mu)) * x + 3 * (1 + 2 * mu)) * x - 2 * (1 - mu)) * x - 2 * (1 - mu);
                    break;
                default:
                    return 0;
            }
            for (int i = 0; i < 50; i++)
            {
                double step = f(g) / df(g);
                g -= step;
                if (Math.Abs(step) < 1e-15) break;
            }
            return g;
        }

        /// <summary>
        /// A point's offset from the larger body, given the smaller one's offset from it
        /// (<paramref name="relative"/>) and the direction of its orbital angular momentum
        /// (<paramref name="normal"/>, r × v).
        /// </summary>
        public static DVec3 Offset(DVec3 relative, DVec3 normal, double mu, LagrangePoint point)
        {
            switch (point)
            {
                case LagrangePoint.L1: return relative * (1 - CollinearDistance(mu, point));
                case LagrangePoint.L2: return relative * (1 + CollinearDistance(mu, point));
                case LagrangePoint.L3: return relative * -CollinearDistance(mu, point);
                case LagrangePoint.L4:
                case LagrangePoint.L5:
                {
                    // Turned 60° about the orbit's pole: ahead for L4, behind for L5.
                    var n = normal.Normalized;
                    var along = DVec3.Cross(n, relative);
                    double sign = point == LagrangePoint.L4 ? 1 : -1;
                    return relative * 0.5 + along * (sign * Sin60);
                }
                default: return DVec3.Zero;
            }
        }

        /// <summary>
        /// The pair a body's Lagrange points belong to: it and what it orbits. None for a body
        /// orbiting a barycentre (a binary star's members) or with nothing to orbit.
        /// </summary>
        public static bool HasPoints(BodyCatalog catalog, int secondary)
        {
            if (secondary < 0 || secondary >= catalog.Count) return false;
            var b = catalog[secondary];
            if (b.ParentIndex < 0 || b.Elements == null) return false;
            var p = catalog[b.ParentIndex];
            return p.Kind != BodyKind.Barycentre && p.Gm > 0;
        }

        /// <summary>Every pair with Lagrange points: (larger, smaller) by catalogue index.</summary>
        public static IEnumerable<(int primary, int secondary)> Pairs(BodyCatalog catalog)
        {
            for (int i = 0; i < catalog.Count; i++)
                if (HasPoints(catalog, i)) yield return (catalog[i].ParentIndex, i);
        }

        /// <summary>
        /// Where the smaller body's mass sits seen from a star: a planet with its moons (Earth and the
        /// Moon as their barycentre, as JPL's Sun-EMB points use) and their combined GM. A moon is
        /// just itself.
        /// </summary>
        static DVec3 MassCentre(Ephemeris eph, int body, double jd, out double gm)
        {
            var catalog = eph.Catalog;
            var b = catalog[body];
            gm = Math.Max(b.Gm, 0);
            var sum = eph.HelioAt(body, jd) * gm;
            if (b.ParentIndex >= 0 && catalog[b.ParentIndex].UsesStellarMapping)
                foreach (int c in catalog.ChildrenOf(body))
                {
                    double m = Math.Max(catalog[c].Gm, 0);
                    if (m <= 0) continue;
                    sum += eph.HelioAt(c, jd) * m;
                    gm += m;
                }
            return gm > 0 ? sum / gm : eph.HelioAt(body, jd);
        }

        /// <summary>
        /// A Lagrange point of a body (<paramref name="secondary"/>) and what it orbits, heliocentric km
        /// at a date. A planet's points use it and its moons together, a moon's use the moon alone.
        /// </summary>
        public static DVec3 HelioAt(Ephemeris eph, int secondary, LagrangePoint point, double jd)
        {
            var catalog = eph.Catalog;
            int primary = catalog[secondary].ParentIndex;
            var p = eph.HelioAt(primary, jd);
            var s = MassCentre(eph, secondary, jd, out double gm2);
            const double h = 1e-3;
            var motion = MassCentre(eph, secondary, jd + h, out _) - eph.HelioAt(primary, jd + h)
                         - (MassCentre(eph, secondary, jd - h, out _) - eph.HelioAt(primary, jd - h));
            var rel = s - p;
            double mu = MassRatio(catalog[primary].Gm, gm2);
            return p + Offset(rel, DVec3.Cross(rel, motion), mu, point);
        }

        /// <summary>A Lagrange point's offset from its pair's larger body, km.</summary>
        public static DVec3 OffsetAt(Ephemeris eph, int secondary, LagrangePoint point, double jd) =>
            HelioAt(eph, secondary, point, jd) - eph.HelioAt(eph.Catalog[secondary].ParentIndex, jd);

        /// <summary>
        /// The body a Lagrange point is drawn near at gameplay scale: L1 and L2 sit close to the smaller
        /// body, L3, L4 and L5 out on the larger one's side of things, at the smaller one's distance.
        /// </summary>
        public static int MappedNear(BodyCatalog catalog, int secondary, LagrangePoint point) =>
            point == LagrangePoint.L1 || point == LagrangePoint.L2 ? secondary : catalog[secondary].ParentIndex;

        /// <summary>"Sun-Earth L2", "Earth-Moon L1".</summary>
        public static string Name(BodyCatalog catalog, int secondary, LagrangePoint point) =>
            catalog[catalog[secondary].ParentIndex].Name + "-" + catalog[secondary].Name + " " + point;

        /// <summary>
        /// How far ahead of <paramref name="host"/> a body is along the orbit (degrees, -180 to 180),
        /// measured in the host's orbital plane: about +60 for a body in the host's L4 swarm, -60 in L5.
        /// </summary>
        public static double LeadDeg(Ephemeris eph, int host, int body, double jd)
        {
            var catalog = eph.Catalog;
            int star = catalog[host].ParentIndex;
            var c = eph.HelioAt(star, jd);
            var r = eph.HelioAt(host, jd) - c;
            const double h = 1e-2;
            var v = eph.HelioAt(host, jd + h) - eph.HelioAt(host, jd - h);
            var n = DVec3.Cross(r, v).Normalized;
            var x = r.Normalized;
            var y = DVec3.Cross(n, x);
            var q = eph.HelioAt(body, jd) - c;
            return Math.Atan2(DVec3.Dot(q, y), DVec3.Dot(q, x)) * Astro.Rad2Deg;
        }
    }
}
