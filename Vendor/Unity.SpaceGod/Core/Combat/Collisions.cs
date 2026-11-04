using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A collision volume (U.SPC-T60): a capsule, a segment of half-length <see cref="HalfLengthKm"/> along
    /// <see cref="Axis"/> swept by a sphere of <see cref="RadiusKm"/>. A hull is a capsule as long as the
    /// ship and as thick as its beam; a station, an asteroid or a piece of debris is a sphere (no axis).
    /// Positions and velocities are km and km/s in whatever frame the caller shares between the two.
    /// </summary>
    public struct CollisionVolume
    {
        public DVec3 Pos, Vel;

        /// <summary>Unit long axis (a hull's nose), or zero for a sphere.</summary>
        public DVec3 Axis;

        public double HalfLengthKm, RadiusKm;

        /// <summary>Kilotonnes; zero or less for something immovable (a station, an asteroid): it takes
        /// no impulse and the other body bounces off it as off a wall.</summary>
        public double MassKt;

        /// <summary>Its motion is driven elsewhere (a ship on its schedule, a drawn debris piece): it counts
        /// with its mass in the impulse, but <see cref="Collisions.Resolve"/> moves neither its position nor
        /// its velocity, and the other body is pushed all the way out of it.</summary>
        public bool Pinned;

        public bool Immovable => MassKt <= 0;

        /// <summary>The ends of the core segment.</summary>
        public DVec3 Head => Pos + Axis * HalfLengthKm;
        public DVec3 Tail => Pos - Axis * HalfLengthKm;

        /// <summary>The radius of a sphere that holds the whole volume, for broad-phase tests.</summary>
        public double BoundingRadiusKm => RadiusKm + HalfLengthKm;

        public static CollisionVolume Sphere(DVec3 pos, DVec3 vel, double radiusKm, double massKt) =>
            new CollisionVolume { Pos = pos, Vel = vel, RadiusKm = Math.Max(0, radiusKm), MassKt = massKt };

        /// <summary>A hull of design <paramref name="spec"/>, nose on <paramref name="forward"/>: as long as the
        /// ship, its beam (<see cref="CombatSpec.BeamFraction"/> of its length) the capsule's diameter.</summary>
        public static CollisionVolume Hull(CombatSpec spec, DVec3 pos, DVec3 vel, DVec3 forward, double massKt = double.NaN)
        {
            double length = Math.Max(1e-6, spec.LengthKm);
            double r = length * Math.Max(0.01, Math.Min(1, spec.BeamFraction)) * 0.5;
            var axis = forward.LengthSquared > 1e-18 ? forward.Normalized : DVec3.UnitX;
            return new CollisionVolume
            {
                Pos = pos, Vel = vel, Axis = axis,
                HalfLengthKm = Math.Max(0, length * 0.5 - r), RadiusKm = r,
                MassKt = double.IsNaN(massKt) ? Collisions.MassOf(spec) : massKt,
            };
        }
    }

    /// <summary>What one contact did: where, along which normal, how hard, and how much energy the
    /// crumpling took out of it (each body's share of which is what damages it).</summary>
    public struct Impact
    {
        public bool Hit;

        /// <summary>The contact point (between the two surfaces) and the unit normal from A toward B.</summary>
        public DVec3 Point, Normal;

        /// <summary>How deep the volumes overlapped before they were pushed apart, km.</summary>
        public double DepthKm;

        /// <summary>The speed they were closing at along the normal, km/s (0 if already parting).</summary>
        public double ClosingKmS;

        /// <summary>The impulse B received (A received its opposite), kt·km/s.</summary>
        public DVec3 Impulse;

        /// <summary>Kinetic energy the impact dissipated, TJ (kt·km²/s²): the relative kinetic energy in
        /// the bodies' centre-of-mass frame before it, less what is left after. Split evenly between them
        /// (<see cref="EnergyShareTJ"/>): what crumples one hull crumples the other.</summary>
        public double EnergyTJ;

        public double EnergyShareTJ => EnergyTJ * 0.5;
    }

    /// <summary>
    /// Collisions and ramming (U.SPC-T60): hulls, stations, asteroids and wrecks have volumes
    /// (<see cref="CollisionVolume"/>), a contact between two of them exchanges momentum by an impulse along the
    /// contact normal (part elastic, the rest crumpling) with friction across it, so a glancing pass is a
    /// scrape, and the kinetic energy it dissipates is damage, done to the subsystems on the struck side.
    ///
    /// Damage scales with that energy over the hull's own mass (<see cref="DamageHp"/>): a hull is about as
    /// tough as it is heavy, so a courier that rams a battleship is wrecked while the battleship is
    /// scratched, though both absorbed the same energy. Contacts slower than <see cref="SafeImpactKmS"/>
    /// are fender bumps: no bounce and no damage, which is what lets a pilot nudge alongside a hulk.
    /// The impulse acts through the centres (no spin is imparted).
    /// </summary>
    public static class Collisions
    {
        /// <summary>The share of closing speed a hard impact gives back as bounce; the rest crumples metal.</summary>
        public const double Restitution = 0.2;

        /// <summary>Coulomb friction between scraping hulls: the most tangential impulse a contact can give,
        /// as a share of its normal impulse.</summary>
        public const double Friction = 0.3;

        /// <summary>Closing speeds under this (km/s, 5 m/s) are fender bumps: no bounce, and below the energy
        /// a hull shrugs off (<see cref="DamageHp"/>).</summary>
        public const double SafeImpactKmS = 0.005;

        /// <summary>Energy per kilotonne of hull, TJ/kt, that would take a hull's whole worth of hit points:
        /// a courier into a station at its full flight-assist speed (0.45 km/s) loses well over half its hull,
        /// a carrier at its own (0.07 km/s) a percent or two.</summary>
        public const double ToughnessTJPerKt = 0.084;

        /// <summary>Of the damage an impact does, the share taken by the hardpoints on the struck side (the
        /// rest goes straight to the hull, as does whatever is left over once a hardpoint is gone).</summary>
        public const double SubsystemShare = 0.6;

        /// <summary>How far from the point of impact (in ship lengths) a hardpoint still takes a share.</summary>
        public const double SubsystemReachLengths = 0.35;

        /// <summary>A design's mass, kt: its own, or for a design that has none (a defence platform, a fighter)
        /// as heavy as a cruiser-like hull of its length (mass scales with the cube of size).</summary>
        public static double MassOf(CombatSpec spec) =>
            spec.MassKt > 0 ? spec.MassKt : 3500 * Math.Pow(Math.Max(0.005, spec.LengthKm), 3);

        // ---------------------------------------------------------------- detection

        /// <summary>
        /// Whether two volumes overlap and, if so, the unit normal from <paramref name="a"/> toward
        /// <paramref name="b"/>, how deep they overlap and the contact point.
        /// </summary>
        public static bool Contact(in CollisionVolume a, in CollisionVolume b, out DVec3 normal, out double depthKm, out DVec3 point)
        {
            ClosestPoints(a.Tail, a.Head, b.Tail, b.Head, out var pa, out var pb);
            var d = pb - pa;
            double dist = d.Length;
            double reach = a.RadiusKm + b.RadiusKm;
            normal = default;
            point = default;
            depthKm = reach - dist;
            if (depthKm <= 0) return false;
            // Centres on top of each other: any direction will do, the line between the bodies if they have one.
            if (dist > 1e-12) normal = d / dist;
            else
            {
                var c = b.Pos - a.Pos;
                normal = c.LengthSquared > 1e-24 ? c.Normalized : DVec3.UnitX;
            }
            point = pa + normal * (a.RadiusKm - depthKm * 0.5);
            return true;
        }

        /// <summary>
        /// Continuous detection for a fast pass: the earliest fraction (0..1) of a step over which
        /// <paramref name="b"/> moves by <paramref name="moveB"/> relative to <paramref name="a"/> (b's offset
        /// from a going from <paramref name="relStart"/> to <paramref name="relStart"/> + moveB) at which their
        /// bounding spheres (radii summing to <paramref name="reachKm"/>) first touch, or -1 if they don't in it.
        /// A hull at cruise speed covers more than its own length a frame, so without this it could pass
        /// straight through a piece of debris between two frames.
        /// </summary>
        public static double TimeOfImpact(DVec3 relStart, DVec3 moveB, double reachKm)
        {
            double c = relStart.LengthSquared - reachKm * reachKm;
            if (c <= 0) return 0;
            double aa = moveB.LengthSquared;
            if (aa < 1e-24) return -1;
            double bb = DVec3.Dot(relStart, moveB);
            if (bb >= 0) return -1; // not closing
            double disc = bb * bb - aa * c;
            if (disc < 0) return -1;
            double t = (-bb - Math.Sqrt(disc)) / aa;
            return t >= 0 && t <= 1 ? t : -1;
        }

        /// <summary>
        /// Detects and resolves a contact: pushes the two apart out of any overlap (by inverse mass, or all
        /// of it onto the one free to move) and, if they were closing, exchanges the impulse that stops them
        /// closing and gives back <see cref="Restitution"/> of it as bounce (none for a fender bump), with
        /// friction against their sliding across each other. Momentum between two free bodies is conserved
        /// exactly; an immovable one takes none of it, and a <see cref="CollisionVolume.Pinned"/> one is left as it was.
        /// </summary>
        public static Impact Resolve(ref CollisionVolume a, ref CollisionVolume b)
        {
            var impact = new Impact();
            if (!Contact(a, b, out var n, out double depth, out var point)) return impact;
            double ia = a.Immovable ? 0 : 1 / a.MassKt;
            double ib = b.Immovable ? 0 : 1 / b.MassKt;
            impact.Hit = true;
            impact.Normal = n;
            impact.Point = point;
            impact.DepthKm = depth;
            if (ia + ib <= 0) return impact;

            // Push apart: the one that cannot move (or is pinned to a track) stays put.
            bool aMoves = !a.Immovable && !a.Pinned, bMoves = !b.Immovable && !b.Pinned;
            double shareA = aMoves && bMoves ? ia / (ia + ib) : aMoves ? 1 : 0;
            double shareB = aMoves && bMoves ? ib / (ia + ib) : bMoves ? 1 : 0;
            if (aMoves) a.Pos -= n * (depth * shareA);
            if (bMoves) b.Pos += n * (depth * shareB);

            var rel = b.Vel - a.Vel;
            double vn = DVec3.Dot(rel, n);
            if (vn >= 0) return impact; // already parting
            double closing = -vn;
            double e = closing > SafeImpactKmS ? Restitution : 0;
            double invSum = ia + ib;
            double jn = (1 + e) * closing / invSum;
            var tangential = rel - n * vn;
            double vt = tangential.Length;
            var j = n * jn;
            if (vt > 1e-15)
            {
                // Friction: enough to stop the slide, but never more than the contact can grip.
                double jt = Math.Min(Friction * jn, vt / invSum);
                j -= tangential / vt * jt;
            }

            var va = a.Vel - j * ia;
            var vb = b.Vel + j * ib;
            var relAfter = vb - va;
            double mu = 1 / invSum;
            impact.ClosingKmS = closing;
            impact.Impulse = j;
            impact.EnergyTJ = Math.Max(0, 0.5 * mu * (rel.LengthSquared - relAfter.LengthSquared));
            if (aMoves) a.Vel = va;
            if (bMoves) b.Vel = vb;
            return impact;
        }

        /// <summary>
        /// A hull settling onto (or slamming into) a world's surface: the sink speed it loses there is a
        /// fully inelastic impact with something immovable, and this is the hull's share of the energy.
        /// </summary>
        public static double SurfaceImpactShareTJ(double massKt, double sinkKmS) =>
            massKt <= 0 || sinkKmS <= 0 ? 0 : 0.5 * (0.5 * massKt * sinkKmS * sinkKmS);

        // ---------------------------------------------------------------- damage

        /// <summary>
        /// The hit points an impact's energy share does to a hull of design <paramref name="spec"/>: what is
        /// left after the energy it shrugs off (a <see cref="SafeImpactKmS"/> bump, more with armour), over the
        /// energy that takes its whole hull (<see cref="ToughnessTJPerKt"/> per kilotonne), times its hull.
        /// So it is linear in the energy and, for a given pair, goes as the square of the closing speed.
        /// </summary>
        public static double DamageHp(CombatSpec spec, double energyShareTJ)
        {
            double m = MassOf(spec);
            double shrugged = 0.5 * m * SafeImpactKmS * SafeImpactKmS * (1 + spec.Armor * 0.1);
            double e = energyShareTJ - shrugged;
            return e <= 0 ? 0 : spec.Hull * e / (m * ToughnessTJPerKt);
        }

        /// <summary>
        /// Ship lengths in a hull's own frame (right, up, forward: a <see cref="SubsystemDef"/>'s X, Y, Z) of a
        /// point given in the shared frame.
        /// </summary>
        public static DVec3 ToLocal(DVec3 point, DVec3 centre, DVec3 forward, DVec3 up, double lengthKm)
        {
            var f = forward.Normalized;
            var u = (up - f * DVec3.Dot(up, f));
            u = u.LengthSquared > 1e-18 ? u.Normalized : PilotState.LevelUp(f);
            var r = DVec3.Cross(f, u);
            var d = (point - centre) / Math.Max(lengthKm, 1e-9);
            return new DVec3(DVec3.Dot(d, r), DVec3.Dot(d, u), DVec3.Dot(d, f));
        }

        /// <summary>The inverse of <see cref="ToLocal"/> for an offset: a hull-frame vector (right, up, forward)
        /// in the shared frame, unscaled.</summary>
        public static DVec3 ToWorld(DVec3 local, DVec3 forward, DVec3 up)
        {
            var f = forward.Normalized;
            var u = (up - f * DVec3.Dot(up, f));
            u = u.LengthSquared > 1e-18 ? u.Normalized : PilotState.LevelUp(f);
            var r = DVec3.Cross(f, u);
            return r * local.X + u * local.Y + f * local.Z;
        }

        /// <summary>The point on a hull's surface (ship lengths, hull frame) farthest along <paramref name="dirLocal"/>:
        /// where it touches a flat surface lying that way, such as the ground it comes down on.</summary>
        public static DVec3 SurfacePoint(CombatSpec spec, DVec3 dirLocal)
        {
            var d = dirLocal.LengthSquared > 1e-18 ? dirLocal.Normalized : new DVec3(0, -1, 0);
            double r = spec.BeamFraction * 0.5;
            double core = Math.Max(0, 0.5 - r);
            return new DVec3(0, 0, core * Math.Sign(Math.Abs(d.Z) < 1e-9 ? 0 : d.Z)) + d * r;
        }

        /// <summary>The share of an impact at <paramref name="local"/> (ship lengths, hull frame) each live
        /// hardpoint takes: those on the struck side of the hull's axis, more the nearer they sit to the point
        /// of impact, none beyond <see cref="SubsystemReachLengths"/>. Zero everywhere if none is in reach.</summary>
        public static double[] StruckSideWeights(ShipCombat c, DVec3 local)
        {
            var subs = c.Subsystems;
            var w = new double[subs.Count];
            double core = Math.Max(0, 0.5 - c.Spec.BeamFraction * 0.5);
            var onAxis = new DVec3(0, 0, Math.Max(-core, Math.Min(core, local.Z)));
            var outward = local - onAxis;
            outward = outward.LengthSquared > 1e-18 ? outward.Normalized : new DVec3(0, 0, local.Z >= 0 ? 1 : -1);
            double total = 0;
            for (int i = 0; i < subs.Count; i++)
            {
                var s = subs[i];
                // A bridge buried at the centre of the hull (U.SPC-T492) is behind its full thickness: a collision never reaches it.
                if (s.Destroyed || s.Def.Buried) continue;
                var p = new DVec3(s.Def.X, s.Def.Y, s.Def.Z);
                if (DVec3.Dot(p - onAxis, outward) < 0) continue; // the far side of the hull
                double near = 1 - (p - local).Length / SubsystemReachLengths;
                if (near <= 0) continue;
                w[i] = near;
                total += near;
            }
            if (total > 0) for (int i = 0; i < w.Length; i++) w[i] /= total;
            return w;
        }

        /// <summary>
        /// Does <paramref name="hp"/> of impact damage to a hull struck at <paramref name="local"/> (ship lengths
        /// in its frame): the field of the shield emitter whose patch is struck soaks what it can, as it would a shot; then the
        /// hardpoints on the struck side take <see cref="SubsystemShare"/> between them by nearness (what a lost
        /// one cannot take goes through to the hull) and the hull the rest. A lost reactor takes the ship with it.
        /// Returns the damage that got past the shields; hardpoints knocked out go into <paramref name="destroyed"/>.
        /// </summary>
        public static double ApplyDamage(ShipCombat c, DVec3 local, double hp, double nowSeconds = -999, List<Subsystem> destroyed = null)
        {
            if (hp <= 0 || !c.Alive) return 0;
            // The field over the patch of hull struck takes it (U.SPC-T501).
            var generator = c.EmitterFor(ShieldEmitters.StrikePoint(c.Spec, local));
            if (generator != null)
            {
                generator.LastHitSeconds = nowSeconds;
                double soaked = Math.Min(generator.Shield, hp);
                generator.Shield -= soaked;
                hp -= soaked;
                if (hp <= 0) return 0;
            }

            var weights = StruckSideWeights(c, local);
            double toSubs = 0;
            foreach (var w in weights) toSubs += w;
            double subsDamage = toSubs > 0 ? hp * SubsystemShare : 0;
            c.Hull -= hp - subsDamage;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0) continue;
                var s = c.Subsystems[i];
                s.Hp -= subsDamage * weights[i];
                if (s.Hp > 0) continue;
                c.Hull -= -s.Hp;
                s.Hp = 0;
                destroyed?.Add(s);
                if (s.Kind == SubsystemKind.Reactor) c.Hull = 0;
                if (s.Kind == SubsystemKind.ShieldGenerator) s.Shield = 0;
                if (s.Kind == SubsystemKind.Hangar) c.Fighters = Math.Max(0, c.Fighters - 6);
            }
            if (c.Hull < 0) c.Hull = 0;
            return hp;
        }

        // ---------------------------------------------------------------- geometry

        /// <summary>Closest points between segments p1-q1 and p2-q2 (Ericson, Real-Time Collision Detection 5.1.9).</summary>
        public static void ClosestPoints(DVec3 p1, DVec3 q1, DVec3 p2, DVec3 q2, out DVec3 c1, out DVec3 c2)
        {
            var d1 = q1 - p1;
            var d2 = q2 - p2;
            var r = p1 - p2;
            double a = d1.LengthSquared, e = d2.LengthSquared, f = DVec3.Dot(d2, r);
            double s, t;
            const double eps = 1e-18;
            if (a <= eps && e <= eps)
            {
                c1 = p1;
                c2 = p2;
                return;
            }
            if (a <= eps)
            {
                s = 0;
                t = Clamp01(f / e);
            }
            else
            {
                double c = DVec3.Dot(d1, r);
                if (e <= eps)
                {
                    t = 0;
                    s = Clamp01(-c / a);
                }
                else
                {
                    double b = DVec3.Dot(d1, d2);
                    double denom = a * e - b * b;
                    s = denom > eps ? Clamp01((b * f - c * e) / denom) : 0;
                    t = (b * s + f) / e;
                    if (t < 0)
                    {
                        t = 0;
                        s = Clamp01(-c / a);
                    }
                    else if (t > 1)
                    {
                        t = 1;
                        s = Clamp01((b - c) / a);
                    }
                }
            }
            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }

        static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
    }
}
