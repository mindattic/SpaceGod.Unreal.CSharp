using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A lost carrier strands its wings (U.SPC-T473; docs/SHIP_ROSTER.md, "Carrier"). Fighters and bombers out of their
    /// hangars when their carrier is destroyed, taken, or leaves the field (runs, or jumps away) have nowhere to land: each
    /// looks for another friendly carrier on the field with room in its hangars, or a friendly station (a defence platform
    /// or gate fort fighting on its side), and lands there when the shooting stops. One that finds nowhere on the field
    /// can still put in at a friendly station at the world the fight was over when it ends; one that cannot is lost, its
    /// pilot ejecting into the pods. A carrier's hangars hold no more than its own squadrons
    /// (<see cref="CombatSpec.Squadrons"/>, six craft each), so a carrier can take in only as many strays as its own losses
    /// have left room for.
    /// </summary>
    public static class StrandedWings
    {
        /// <summary>How often a stranded craft looks again for somewhere to land, seconds.</summary>
        public const double HavenCheckSeconds = 5;

        /// <summary>Whether a craft can still land on <paramref name="home"/>: it is on the field and has not been taken.</summary>
        public static bool CanLandOn(BattleUnit home) => home != null && home.Present && !home.Captured;

        /// <summary>Whether <paramref name="haven"/> is a station rather than a carrier: a defence platform or a gate fort.</summary>
        public static bool IsStation(BattleUnit haven) => haven != null && haven.Kind == UnitKind.Platform;

        /// <summary>Whether <paramref name="haven"/> could take in a stray: a station, or a carrier with a working hangar,
        /// still fighting on its side (not running, struck or taken).</summary>
        public static bool TakesStrays(BattleUnit haven)
        {
            if (!CanLandOn(haven) || haven.Kind == UnitKind.Fighter || haven.Surrendering || haven.Fleeing) return false;
            if (IsStation(haven)) return true;
            if (haven.Combat == null || haven.Combat.Spec.Squadrons <= 0) return false;
            foreach (var s in haven.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Hangar && !s.Destroyed) return true;
            return false;
        }

        /// <summary>How many more craft <paramref name="haven"/> can take in: a station any number; a carrier what its hangars
        /// hold less the craft aboard and those already flying back to land on it.</summary>
        public static int Room(Battle b, BattleUnit haven)
        {
            if (IsStation(haven)) return int.MaxValue;
            int room = haven.Combat.Spec.HangarCraft - haven.Combat.Fighters;
            foreach (var o in b.Units)
                // A boarding shuttle lands in its own berths, not a squadron's (U.SPC-T486).
                if (o.Kind == UnitKind.Fighter && o.Present && o.Fighter != null && o.Fighter.LandsOn == haven && !BoardingShuttles.Is(o)) room--;
            return room;
        }

        /// <summary>The nearest friendly carrier with room, or station, on the field that <paramref name="craft"/> could land on
        /// instead, or null.</summary>
        public static BattleUnit Haven(Battle b, BattleUnit craft)
        {
            BattleUnit best = null;
            double bestD = double.MaxValue;
            foreach (var o in b.Units)
            {
                if (o == craft || o.Side != craft.Side || o == craft.Fighter?.LandsOn || !TakesStrays(o)) continue;
                double d = (o.Pos - craft.Pos).LengthSquared;
                if (d >= bestD || Room(b, o) <= 0) continue;
                best = o;
                bestD = d;
            }
            return best;
        }

        /// <summary>
        /// A craft's step (from <see cref="FighterAI.Step"/>): while it can still land where it was going, nothing; once that
        /// carrier is lost or gone, it looks (every <see cref="HavenCheckSeconds"/>) for a friendly carrier or station on the
        /// field to land on instead, and is <see cref="FighterState.Stranded"/> until it finds one.
        /// </summary>
        public static void Step(Battle b, BattleUnit u, double dt)
        {
            var f = u.Fighter;
            if (f == null || CanLandOn(f.LandsOn)) return;
            f.HavenCheckIn -= dt;
            if (f.HavenCheckIn > 0) return;
            f.HavenCheckIn = HavenCheckSeconds;
            var haven = Haven(b, u);
            if (haven == null)
            {
                f.Stranded = true;
                return;
            }
            f.Home = haven;
            f.Stranded = false;
        }

        /// <summary>A friendly station at the world <paramref name="b"/> was fought over that a stray from it can put in at once
        /// the fight is over: its own side's first, else one of a friendly power's it may dock at; or null, and none for a
        /// fight out on a lane, far from any port.</summary>
        public static Settlement Port(Battle b, Faction owners, Economy economy, Diplomacy diplomacy)
        {
            if (b == null || b.Site != null || economy == null || owners == null) return null;
            Settlement friendly = null;
            foreach (var s in economy.At(b.Body))
            {
                if (s.Owner == null || s.Kind == SettlementKind.MiningOutpost) continue;
                if (s.Owner == owners) return s;
                if (friendly == null && diplomacy != null && !diplomacy.Hostile(owners, s.Owner) && diplomacy.CanDockAt(owners, s)) friendly = s;
            }
            return friendly;
        }
    }
}
