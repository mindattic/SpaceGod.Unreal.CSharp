using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The camera's way from one star system to another through the jump gates (the STAR SYSTEMS trip,
    /// see <c>StarTravel</c>), as far as it can be worked out without the engine: which gate it leaves by
    /// and which it comes out of, and how the trip is paced. In four phases:
    ///  1. Approach: from wherever the view is, down to a point lined up on the ring of the gate it leaves by,
    ///     looking through it at the star behind.
    ///  2. Plunge: faster and faster, straight through the ring.
    ///  3. Tunnel: a short spell inside the gate, nothing of either system on screen (the destination is built
    ///     then, hidden).
    ///  4. Emerge: out of the partner gate in the destination, the view looking back at it and its star, then
    ///     pulling back to the usual overview of the system.
    /// A system with no gates, or none leading anywhere near the destination, is left to the old way across.
    /// </summary>
    public static class GateJump
    {
        public enum Phase
        {
            Approach,
            Plunge,
            Tunnel,
            Emerge,
        }

        public const double ApproachSeconds = 2.8, PlungeSeconds = 0.9, TunnelSeconds = 1.8, EmergeSeconds = 3.4;

        /// <summary>The whole trip, seconds (the way across between the stars took 9.4).</summary>
        public const double TotalSeconds = ApproachSeconds + PlungeSeconds + TunnelSeconds + EmergeSeconds;

        /// <summary>How far in front of the ring the view lines up before the plunge, in ring diameters.</summary>
        public const double LineUpDiameters = 3.5;

        /// <summary>Share of the way through the tunnel at which the destination is built (hidden): early, so its models
        /// and textures have the rest of the tunnel to stream in (U.SPC-T414).</summary>
        public const double BuildAt = 0.2;

        /// <summary>Share of the way through the tunnel at which it is held open (warp at its height) while the far side
        /// is still loading (U.SPC-T414, see <see cref="GateCover"/>).</summary>
        public const double HoldAt = 0.5;

        /// <summary>How much wider the view opens (degrees) going through a gate.</summary>
        public const double FovKickDeg = 24;

        /// <summary>
        /// The gate to leave <paramref name="from"/> by on the way to <paramref name="to"/> (the one straight there,
        /// else the first on the quickest route over the network, open or not: the view is no ship and nobody's
        /// blockade stops it, though a ring out of action does) and the one in <paramref name="to"/> to come out of (the far
        /// end of the last gate on that route). False, with both null, when <paramref name="from"/> has no gates or there is
        /// no way through.
        /// </summary>
        public static bool TryChoose(GateNetwork network, StarSystem from, StarSystem to, out JumpGate departure, out JumpGate arrival)
        {
            departure = arrival = null;
            if (from == null || to == null || from == to || from.Gates.Count == 0) return false;
            var direct = from.GateTo(to);
            if (direct != null && direct.Partner != null && direct.Link.Working)
            {
                departure = direct;
                arrival = direct.Partner;
                return true;
            }
            var route = network?.Route(from, to);
            if (route == null || route.Count == 0) return false;
            var last = route[route.Count - 1].Partner;
            if (route[0].Home != from || last == null || last.Home != to) return false;
            departure = route[0];
            arrival = last;
            return true;
        }

        /// <summary>
        /// The gates the view takes from <paramref name="from"/> to <paramref name="to"/>, one per leg and in order (U.SPC-T516):
        /// the direct gate if one leads there, else the quickest route over the network (open or not, the view is no ship, though
        /// a ring out of action stops it). Empty, false, when <paramref name="from"/> has no gates or there is no way through.
        /// </summary>
        public static bool TryPlan(GateNetwork network, StarSystem from, StarSystem to, out System.Collections.Generic.List<JumpGate> route)
        {
            route = new System.Collections.Generic.List<JumpGate>();
            if (from == null || to == null || from == to || from.Gates.Count == 0) return false;
            var direct = from.GateTo(to);
            if (direct != null && direct.Partner != null && direct.Link.Working)
            {
                route.Add(direct);
                return true;
            }
            var planned = network?.Route(from, to);
            if (planned == null || planned.Count == 0 || planned[0].Home != from || planned[planned.Count - 1].Partner?.Home != to)
                return false;
            route = planned;
            return true;
        }

        /// <summary>How long <paramref name="phase"/> lasts, seconds.</summary>
        public static double Seconds(Phase phase) =>
            phase == Phase.Approach ? ApproachSeconds : phase == Phase.Plunge ? PlungeSeconds : phase == Phase.Tunnel ? TunnelSeconds : EmergeSeconds;

        /// <summary>0..1 through the whole trip, at <paramref name="t"/> (0..1) through <paramref name="phase"/>.</summary>
        public static double Overall(Phase phase, double t)
        {
            double before = 0;
            for (var p = Phase.Approach; p < phase; p++) before += Seconds(p);
            return Clamp01((before + Clamp01(t) * Seconds(phase)) / TotalSeconds);
        }

        /// <summary>Share of the way down to the line-up point (log distance from the system's root) during the approach.</summary>
        public static double ApproachDescent(double t) => Smoother(t);

        /// <summary>Share of the swing round the system's root to the line of the gate's ring during the approach: done
        /// before the descent is, so the last of the way in is straight down the line.</summary>
        public static double ApproachSwing(double t) => Smoother(t / 0.75);

        /// <summary>Share of the turn from the starting view to looking at the gate during the approach.</summary>
        public static double ApproachLook(double t) => Smoother(t * 1.8);

        /// <summary>How much of the line-up distance is still ahead during the plunge: 1 at the line-up point,
        /// gathering speed all the way to 0 at the ring.</summary>
        public static double PlungeRemaining(double t)
        {
            t = Clamp01(t);
            return 1 - t * t * t;
        }

        /// <summary>How hard the view warps, 0..1 (the warp lines and dust): none on the approach, building as the ring
        /// comes up, at its height in the tunnel, gone soon after coming out.</summary>
        public static double Warp(Phase phase, double t)
        {
            t = Clamp01(t);
            switch (phase)
            {
                case Phase.Approach: return 0;
                case Phase.Plunge: return 0.6 * Smoother((t - 0.35) / 0.65);
                case Phase.Tunnel: return 0.6 + 0.4 * Math.Sin(Math.PI * t);
                default: return 0.6 * (1 - Smoother(t / 0.15));
            }
        }

        /// <summary>The flash of going through a ring, 0..1: rising to full at the ring on the way in, fading inside,
        /// full again on the way out and fading as the destination opens out.</summary>
        public static double Flash(Phase phase, double t)
        {
            t = Clamp01(t);
            switch (phase)
            {
                case Phase.Approach: return 0;
                case Phase.Plunge: return Smoother((t - 0.7) / 0.3);
                case Phase.Tunnel: return Math.Max(1 - Smoother(t / 0.2), Smoother((t - 0.85) / 0.15));
                default: return 1 - Smoother(t / 0.18);
            }
        }

        /// <summary>The glow of the inside of the gate, 0..1: all through the tunnel and nowhere else.</summary>
        public static double Glow(Phase phase, double t) => phase == Phase.Tunnel ? 1 : 0;

        /// <summary>How much wider the view is than usual (degrees): opening up through the plunge, held through the
        /// tunnel and closing again as the view comes out.</summary>
        public static double FovKick(Phase phase, double t)
        {
            t = Clamp01(t);
            switch (phase)
            {
                case Phase.Approach: return 0;
                case Phase.Plunge: return FovKickDeg * t * t;
                case Phase.Tunnel: return FovKickDeg * (1 - 1.6 * Fold(phase, t));
                default: return FovKickDeg * (1 - Smoother(t / 0.25));
            }
        }

        /// <summary>The fold at the end of the tunnel, 0..1 (U.SPC-T414): space pinches shut ahead (the view narrowing past
        /// its usual width) just before the far gate's flare throws it open again. None anywhere else.</summary>
        public static double Fold(Phase phase, double t)
        {
            if (phase != Phase.Tunnel) return 0;
            t = Clamp01(t);
            return Smoother((t - 0.7) / 0.22) * (1 - Smoother((t - 0.95) / 0.05));
        }

        static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

        /// <summary>Smootherstep on a value clamped to 0..1.</summary>
        public static double Smoother(double x)
        {
            x = Clamp01(x);
            return x * x * x * (x * (x * 6 - 15) + 10);
        }
    }
}
