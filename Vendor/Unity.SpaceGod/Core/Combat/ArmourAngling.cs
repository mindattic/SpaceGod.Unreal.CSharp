using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Armour angling on the real 3D hull (U.SPC-T534, docs/SHIP_ROSTER.md "Armour angling"). There are no bow, beam and
    /// stern armour faces (they went with the facing arcs, U.SPC-T533): each hit on a capital ship is measured against the
    /// hull's own surface where it actually strikes, the hull's outward normal at the impact point on its real shape (the
    /// capsule its beam wide, <see cref="Collisions.SurfacePoint"/>), found by following the shot's line in to where it meets
    /// the hull. The more slanted the strike, the thicker the plate it has to cross (one over the cosine of the strike, at most
    /// <see cref="MaxObliquity"/>) and the more of it is turned aside, both scaled by <see cref="Strength"/>. So a shot
    /// raking a hull lengthways, or striking the curve of its side or its rounded ends, does less than one square on, and a
    /// captain who keeps its hull slanted to the fire (AI captains by doctrine, <see cref="AiWeight"/>; the weak-patch rolling
    /// weighs it too, <see cref="WeakPatches"/>) takes less while its guns still bear.
    /// </summary>
    [SettingsSource]
    public static class ArmourAngling
    {
        // ------------------------------------------------------------ settings

        public static readonly RangeSetting Strength = new RangeSetting("broadside.armourAngling", SettingGroups.Broadsides,
            "Armour angling", 0.4, 0, 1, 0.05,
            "How much a slanted strike on the hull is turned aside: the share of a hit lost to a fully glancing strike on the hull's surface where it lands, and how much thicker the plate reads the more slanted the strike (0 turns angling off).",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "armor armour angle angling glance oblique slant belt hull surface broadside capital");

        public static readonly RangeSetting MaxObliquity = new RangeSetting("broadside.maxObliquity", SettingGroups.Broadsides,
            "Armour obliquity cap", 3, 1, 6, 0.25,
            "The thickest a slanted strike can find the armour: plate crossed at a slant reads one over the cosine of the strike thicker, but never more than this many times.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "armor armour angle obliquity cap slant thickness glance");

        public static readonly RangeSetting AiWeight = new RangeSetting("broadside.aiAngleHull", SettingGroups.Broadsides,
            "AI captains slant the hull", 1, 0, 3, 0.1,
            "How much of its guns' bearing an AI captain (and the weak-patch rolling) gives up to present its hull slanted to the fire, so hits glance off its armour (0: it ignores the slant and lays its guns as before).",
            v => v.ToString("0.#", CultureInfo.InvariantCulture) + "x", "armor armour angle angling slant hull ai doctrine broadside capital");

        /// <summary>How far either side of a battery's heading an AI captain looks for a slanted one that still bears, deg.</summary>
        public const double SlantDeg = 30;

        // ------------------------------------------------------------ one hit

        /// <summary>
        /// The thicker armour a strike meets and the share of it not turned aside, for a strike whose cosine against the
        /// hull's surface normal is <paramref name="cos"/> (1 square on, 0 grazing): the armour reads one over the cosine,
        /// at most <see cref="MaxObliquity"/>, scaled by <see cref="Strength"/>, and a fully glancing strike loses
        /// <see cref="Strength"/> of the hit.
        /// </summary>
        public static (double armour, double through) Angling(double cos)
        {
            double k = Strength.Value;
            if (k <= 0) return (1, 1);
            cos = Math.Max(0, Math.Min(1, cos));
            double cap = Math.Max(1, MaxObliquity.Value);
            double obliquity = Math.Min(cap, 1 / Math.Max(cos, 1 / cap));
            double armour = 1 + (obliquity - 1) * k;
            double through = 1 - k * (1 - cos);
            return (Math.Max(0, armour), Math.Max(0, Math.Min(1, through)));
        }

        /// <summary>
        /// The angling of a hit on <paramref name="target"/> aimed at battle-local <paramref name="at"/> and travelling along
        /// <paramref name="incoming"/>: measured against the hull's surface where the shot's line meets it
        /// (<see cref="StrikeCos"/>).
        /// </summary>
        public static (double armour, double through) Angling(BattleUnit target, DVec3 at, DVec3 incoming)
        {
            var spec = target.Combat?.Spec;
            if (spec == null || Strength.Value <= 0 || incoming.LengthSquared < 1e-18) return (1, 1);
            return Angling(StrikeCos(spec, target.ToLocal(at), target.DirToLocal(incoming)));
        }

        // ------------------------------------------------------------ the hull's surface

        static void Shape(CombatSpec spec, out double r, out double core)
        {
            r = Math.Max(0.005, Math.Min(0.5, (spec?.BeamFraction ?? FireCones.DefaultBeamFraction) * 0.5));
            core = Math.Max(0, 0.5 - r);
        }

        /// <summary>The hull's outward surface normal at a point on it (ship lengths, hull frame): the capsule's, straight
        /// out from the keel along its length and rounding over to the bow or stern at its ends.</summary>
        public static DVec3 SurfaceNormal(CombatSpec spec, DVec3 local) =>
            FireCones.MountNormal(local.X, local.Y, local.Z, spec?.BeamFraction ?? FireCones.DefaultBeamFraction);

        /// <summary>
        /// Where a shot whose line passes through <paramref name="throughLocal"/> travelling along <paramref name="dirLocal"/>
        /// (both hull frame, ship lengths) first meets the hull's surface, if it does: the nearest entry into the capsule along
        /// that line, its straight length or either rounded end.
        /// </summary>
        public static bool Entry(CombatSpec spec, DVec3 throughLocal, DVec3 dirLocal, out DVec3 hit)
        {
            hit = default;
            if (dirLocal.LengthSquared < 1e-24) return false;
            Shape(spec, out double r, out double core);
            var d = dirLocal.Normalized;
            var p = throughLocal;
            double best = double.PositiveInfinity;
            // The straight length: x² + y² = r², between the ends.
            double a = d.X * d.X + d.Y * d.Y;
            if (a > 1e-18)
            {
                double bh = p.X * d.X + p.Y * d.Y;
                double c = p.X * p.X + p.Y * p.Y - r * r;
                double disc = bh * bh - a * c;
                if (disc >= 0)
                {
                    double t = (-bh - Math.Sqrt(disc)) / a;
                    double z = p.Z + d.Z * t;
                    if (z >= -core && z <= core) best = Math.Min(best, t);
                }
            }
            // The rounded ends: spheres of the beam's radius at either end of the straight length.
            for (int sign = -1; sign <= 1; sign += 2)
            {
                var o = p - new DVec3(0, 0, sign * core);
                double bh = DVec3.Dot(o, d);
                double c = o.LengthSquared - r * r;
                double disc = bh * bh - c;
                if (disc < 0) continue;
                double t = -bh - Math.Sqrt(disc);
                double z = p.Z + d.Z * t;
                if (sign * z >= core - 1e-12) best = Math.Min(best, t);
            }
            if (double.IsPositiveInfinity(best)) return false;
            hit = p + d * best;
            return true;
        }

        /// <summary>
        /// The cosine of a strike against the hull's surface, 1 square on to 0 grazing, for a shot whose line passes through
        /// <paramref name="throughLocal"/> travelling along <paramref name="dirLocal"/> (hull frame): measured at the point
        /// where the line meets the hull (<see cref="Entry"/>); a line that misses the hull's body (a mount standing proud of
        /// it struck at the edge) is measured against the surface where it was aimed.
        /// </summary>
        public static double StrikeCos(CombatSpec spec, DVec3 throughLocal, DVec3 dirLocal)
        {
            if (dirLocal.LengthSquared < 1e-24) return 1;
            var d = dirLocal.Normalized;
            var point = Entry(spec, throughLocal, d, out var hit) ? hit : throughLocal;
            return Math.Max(0, Math.Min(1, -DVec3.Dot(SurfaceNormal(spec, point), d)));
        }

        // ------------------------------------------------------------ the face a hull shows the fire

        /// <summary>The share of a hit's harm that gets home through the armour at cosine <paramref name="cos"/>: what is not
        /// turned aside, over how much thicker the plate reads (1 square on, or with angling off).</summary>
        public static double HarmAt(double cos)
        {
            var (armour, through) = Angling(cos);
            return armour > 0 ? through / armour : through;
        }

        /// <summary>How finely <see cref="Harm(CombatSpec, DVec3)"/> is tabled by the fire's angle to the keel.</summary>
        const int TableSteps = 64;

        sealed class Table
        {
            public double Beam = double.NaN, Strength = double.NaN, Cap = double.NaN;
            public double[] Harm;
        }

        static readonly ConditionalWeakTable<CombatSpec, Table> Tables = new ConditionalWeakTable<CombatSpec, Table>();

        /// <summary>
        /// The mean share of the harm getting home over the face a capsule hull shows fire coming in at an angle to its keel
        /// whose cosine is <paramref name="keelCos"/> (0 square to its beam, 1 straight along it), each part of its surface
        /// weighted by how much of the line of fire it takes up: its straight length and its two rounded ends integrated
        /// finely. The capsule is the same all round its keel, so this is all the slant depends on.
        /// </summary>
        static double FaceHarm(double r, double core, double keelCos)
        {
            const int around = 48, polar = 24;
            double c = Math.Max(0, Math.Min(1, keelCos)), sn = Math.Sqrt(1 - c * c);
            var d = new DVec3(sn, 0, c);
            double face = 0, harm = 0;
            double da = 2 * Math.PI / around;
            double side = 2 * Math.PI * r * 2 * core / around;
            for (int i = 0; i < around; i++)
            {
                double a = (i + 0.5) * da;
                double k = Math.Cos(a) * sn;
                if (k <= 0 || side <= 0) continue;
                face += k * side;
                harm += k * side * HarmAt(k);
            }
            double dp = Math.PI / 2 / polar;
            for (int sign = -1; sign <= 1; sign += 2)
                for (int j = 0; j < polar; j++)
                {
                    double p = (j + 0.5) * dp;
                    double area = r * r * Math.Sin(p) * dp * da;
                    for (int i = 0; i < around; i++)
                    {
                        double a = (i + 0.5) * da;
                        var n = new DVec3(Math.Sin(p) * Math.Cos(a), Math.Sin(p) * Math.Sin(a), sign * Math.Cos(p));
                        double k = DVec3.Dot(n, d);
                        if (k <= 0) continue;
                        face += k * area;
                        harm += k * area * HarmAt(k);
                    }
                }
            return face > 0 ? harm / face : 1;
        }

        static double[] TableOf(CombatSpec spec)
        {
            var t = Tables.GetValue(spec, _ => new Table());
            lock (t)
            {
                double strength = Strength.Value, cap = MaxObliquity.Value;
                if (t.Harm == null || t.Beam != spec.BeamFraction || t.Strength != strength || t.Cap != cap)
                {
                    Shape(spec, out double r, out double core);
                    var harm = new double[TableSteps + 1];
                    for (int i = 0; i <= TableSteps; i++) harm[i] = FaceHarm(r, core, (double)i / TableSteps);
                    t.Harm = harm;
                    t.Beam = spec.BeamFraction;
                    t.Strength = strength;
                    t.Cap = cap;
                }
                return t.Harm;
            }
        }

        /// <summary>The mean share of a hit's harm that gets home (<see cref="HarmAt"/>) over the face a hull of
        /// <paramref name="spec"/> shows fire from the way <paramref name="towardShooterLocal"/> (hull frame): 1 with angling
        /// off, lower the more slanted the hull lies to the fire.</summary>
        public static double Harm(CombatSpec spec, DVec3 towardShooterLocal)
        {
            if (spec == null || Strength.Value <= 0 || towardShooterLocal.LengthSquared < 1e-24) return 1;
            var table = TableOf(spec);
            double x = Math.Min(1, Math.Abs(towardShooterLocal.Z) / towardShooterLocal.Length) * TableSteps;
            int i = Math.Min(TableSteps - 1, (int)x);
            double f = x - i;
            return table[i] + (table[i + 1] - table[i]) * f;
        }

        /// <summary>The share of the harm getting home of fire from every hostile in <paramref name="fire"/>, each by its
        /// weight, on a hull of <paramref name="spec"/> lying in frame (<paramref name="right"/>, <paramref name="up"/>,
        /// <paramref name="forward"/>).</summary>
        public static double Harm(CombatSpec spec, IReadOnlyList<IncomingFire> fire, DVec3 right, DVec3 up, DVec3 forward)
        {
            if (Strength.Value <= 0) return 1;
            double total = 0, weights = 0;
            foreach (var f in fire)
            {
                var d = new DVec3(DVec3.Dot(f.Dir, right), DVec3.Dot(f.Dir, up), DVec3.Dot(f.Dir, forward));
                total += f.Weight * Harm(spec, d);
                weights += f.Weight;
            }
            return weights > 0 ? total / weights : 1;
        }

        /// <summary>Whether <paramref name="u"/>'s captain weighs slanting its hull to the fire: a capital ship's AI captain
        /// with angling and the doctrine's weight on.</summary>
        public static bool AiSlants(BattleUnit u) => Strength.Value > 0 && AiWeight.Value > 0 && !u.PlayerControlled && Broadsides.Commands(u);
    }
}
