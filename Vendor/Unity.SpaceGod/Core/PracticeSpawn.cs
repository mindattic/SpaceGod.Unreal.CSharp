using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>Something a practice fighter must not be spawned inside: a sphere, heliocentric km.</summary>
    public readonly struct SpawnObstacle
    {
        public readonly DVec3 CentreKm;
        public readonly double RadiusKm;

        public SpawnObstacle(DVec3 centreKm, double radiusKm)
        {
            CentreKm = centreKm;
            RadiusKm = Math.Max(0, radiusKm);
        }
    }

    /// <summary>
    /// The practice fighter (U.SPC-T601): Shift+V (or the button on the Spawn &amp; Pilot screen) puts a home-side fighter
    /// at the camera's spot, wherever it is, and the player at its helm, to fly alongside the hulls there and get the feel
    /// of their scale and of the flight. One per system: pressing again moves the same hull (repaired, refuelled) to the new
    /// spot rather than adding another, so practice never fills the fleet, or a save, with spare fighters; only one lost,
    /// taken, or still in a fight is replaced by a new one. This is the placement: the spot nudged clear of every hull,
    /// station, wreck and world near it, so the fighter never starts inside anything.
    /// </summary>
    public static class PracticeSpawn
    {
        /// <summary>What a practice fighter is called (after its faction's prefix): how one is found again, a save later.</summary>
        public const string Name = "Practice Fighter";

        /// <summary>Whether <paramref name="ship"/> is a practice fighter.</summary>
        public static bool Is(Ship ship) => ship?.Name != null && ship.Name.EndsWith(Name, StringComparison.Ordinal);

        /// <summary>The clear water kept between the fighter's hull and anything beside it, km: a few hull lengths, so
        /// it starts right alongside without touching.</summary>
        public static double GapKm(double selfLengthKm) => Math.Max(0.05, selfLengthKm * 3);

        /// <summary>Most nudges tried before settling: enough for a spot crowded by a fleet in formation.</summary>
        const int MaxNudges = 64;

        /// <summary>
        /// Where a fighter <paramref name="selfLengthKm"/> long goes, as near <paramref name="atKm"/> as it can: left
        /// where it is if nothing is within reach, else pushed straight out from whatever it would touch (sideways off
        /// <paramref name="forward"/> if it sits dead on something's centre) to just clear it, again until nothing is
        /// touched. Every position is heliocentric km.
        /// </summary>
        public static DVec3 Clear(DVec3 atKm, DVec3 forward, double selfLengthKm, IReadOnlyList<SpawnObstacle> obstacles)
        {
            if (obstacles == null || obstacles.Count == 0) return atKm;
            double self = Math.Max(0, selfLengthKm) * 0.5;
            double gap = GapKm(selfLengthKm);
            var side = Side(forward);
            var p = atKm;
            for (int n = 0; n < MaxNudges; n++)
            {
                int worst = -1;
                double worstDepth = 0;
                for (int i = 0; i < obstacles.Count; i++)
                {
                    var o = obstacles[i];
                    double need = o.RadiusKm + self + gap;
                    double depth = need - (p - o.CentreKm).Length;
                    if (depth > worstDepth)
                    {
                        worstDepth = depth;
                        worst = i;
                    }
                }
                if (worst < 0) return p;
                var w = obstacles[worst];
                var away = p - w.CentreKm;
                var dir = away.LengthSquared > 1e-18 ? away.Normalized : side;
                // A hair past the edge, so a spot squeezed between two things is not nudged back and forth forever.
                p = w.CentreKm + dir * ((w.RadiusKm + self + gap) * (1 + 1e-6) + 1e-6);
                // Squeezed between things: each further nudge also steps sideways, out of the crowd.
                if (n >= 8) p += side * (gap * (n - 7));
            }
            return p;
        }

        /// <summary>A direction square to <paramref name="forward"/>: the way out from dead centre.</summary>
        static DVec3 Side(DVec3 forward)
        {
            var f = forward.LengthSquared > 1e-12 ? forward.Normalized : DVec3.UnitX;
            var s = DVec3.Cross(Math.Abs(f.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX, f);
            return s.LengthSquared > 1e-12 ? s.Normalized : DVec3.UnitY;
        }
    }
}
