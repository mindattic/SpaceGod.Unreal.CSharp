using System;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Recon and electronic warfare (U.SPC-T485): what a <see cref="ShipClass.Recon"/> fighter does for its side. A carrier
    /// flies its recon flight (<see cref="CombatSpec.ReconCraft"/>) out ahead of its squadrons to a mark, the enemy capital its
    /// bombers are going for or else the biggest one in the fight, and holds off it at <see cref="StandoffKm"/>, outside the
    /// reach of its point defence. From there it
    /// <list type="bullet">
    /// <item><b>jams</b>: every enemy hull, platform and craft within <see cref="JamRangeKm"/> of it has its gunners' and point
    /// defence's accuracy cut by <see cref="JamAccuracy"/> (<see cref="JamFactor"/>; several jammers do not stack);</item>
    /// <item><b>paints</b> its mark for its own side's bombers while it is within <see cref="PaintRangeKm"/> of it
    /// (<see cref="Painted"/>): bombers go for a painted capital first, and their torpedoes home on it
    /// <see cref="PaintGuidance"/> times as hard and are drawn off by its decoys only a fraction as often
    /// (<see cref="PaintDecoyResist"/>), so a painted target is far easier for the torpedoes to hit;</item>
    /// <item><b>scouts</b>: berthed aboard, the recon flight stretches its carrier's own sensor reach
    /// (<see cref="SensorScanners"/>).</item>
    /// </list>
    /// A recon craft knocked out by a pulse or an EMP, struck or taken does none of it. Every figure is in the settings menu's
    /// Dogfighting group. The recon craft a battle has aloft are found once a step (<see cref="Sweep"/>), so a battle without
    /// one pays nothing for any of this.
    /// </summary>
    [SettingsSource]
    public static class ElectronicWarfare
    {
        static string Km(double v) => v.ToString("0.#", CultureInfo.InvariantCulture) + " km";

        static string Percent(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        public static readonly RangeSetting JamRangeKm = new RangeSetting("dogfight.jamRange", SettingGroups.Dogfighting,
            "Recon jamming range", 8, 0, 20, 0.5,
            "How far a recon fighter's jammers reach: every enemy ship, platform and craft this near it fires less accurately. 0 switches jamming off.",
            Km, "recon ew electronic warfare jamming jammer accuracy dogfight");

        public static readonly RangeSetting JamAccuracy = new RangeSetting("dogfight.jamAccuracy", SettingGroups.Dogfighting,
            "Accuracy lost to jamming", 0.3, 0, 0.8, 0.05,
            "How much of its gunners' and point defence's accuracy a jammed ship loses (several jammers on it do not add up).",
            Percent, "recon ew electronic warfare jamming jammer accuracy gunnery dogfight");

        public static readonly RangeSetting PaintRangeKm = new RangeSetting("dogfight.paintRange", SettingGroups.Dogfighting,
            "Recon painting range", 12, 0, 25, 0.5,
            "How near its mark a recon fighter must be to paint it for its bombers. 0 switches painting off.",
            Km, "recon ew electronic warfare paint designate target bomber torpedo dogfight");

        public static readonly RangeSetting PaintGuidance = new RangeSetting("dogfight.paintGuidance", SettingGroups.Dogfighting,
            "Torpedo guidance on a painted target", 2.5, 1, 5, 0.25,
            "How much harder a bomber's torpedo steers onto a target a recon fighter is painting. 1 takes the bonus away.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "recon paint bomber torpedo guidance homing dogfight");

        public static readonly RangeSetting PaintDecoyResist = new RangeSetting("dogfight.paintDecoyResist", SettingGroups.Dogfighting,
            "Painted torpedoes ignore decoys", 0.8, 0, 1, 0.05,
            "How much less often a flare-and-chaff cloud draws off a bomber's torpedo homing on a painted target: it rides the paint, not the heat.",
            Percent, "recon paint bomber torpedo decoy flare chaff countermeasures dogfight");

        /// <summary>How far off its mark a recon fighter holds, km: outside point defence's reach.</summary>
        public const double StandoffKm = 6;

        /// <summary>How much nearer than it is a painted capital counts for a bomber choosing its target, km.</summary>
        public const double PaintBonusKm = 10;

        /// <summary>What each recon craft berthed aboard a carrier is worth to its sensor reach, in scanner suites (see
        /// <see cref="SensorSuite.Of(Ship)"/>): the pair a carrier carries add one suite's half again of range.</summary>
        public const double SensorScanners = 0.5;

        /// <summary>Whether <paramref name="u"/> is a recon fighter: a launched recon craft, or a recon hull flown as a ship.</summary>
        public static bool IsRecon(BattleUnit u) =>
            u != null && (u.Fighter != null ? u.Fighter.Role == FighterRole.Recon : u.Ship != null && u.Ship.Spec.Class == ShipClass.Recon);

        /// <summary>Whether recon craft <paramref name="r"/> is at work: there, its systems up, not struck and not taken.</summary>
        public static bool Working(BattleUnit r) => r.Present && r.Stunned <= 0 && !r.Surrendering && !r.Captured;

        /// <summary>Finds the recon craft <paramref name="b"/> has in the fight, once a step.</summary>
        public static void Sweep(Battle b)
        {
            b.ReconAloft.Clear();
            foreach (var u in b.Units)
                if (u.Present && IsRecon(u)) b.ReconAloft.Add(u);
        }

        /// <summary>What <paramref name="u"/>'s accuracy is multiplied by: 1 less <see cref="JamAccuracy"/> while a working
        /// enemy recon craft is within <see cref="JamRangeKm"/> of it, else 1.</summary>
        public static double JamFactor(Battle b, BattleUnit u)
        {
            if (b == null || u == null || b.ReconAloft.Count == 0) return 1;
            double range = JamRangeKm.Value;
            if (range <= 0) return 1;
            foreach (var r in b.ReconAloft)
                if (r.Side != u.Side && Working(r) && (r.Pos - u.Pos).Length - u.Radius <= range)
                    return 1 - Math.Max(0, Math.Min(0.95, JamAccuracy.Value));
            return 1;
        }

        /// <summary>Whether <paramref name="u"/> is being jammed.</summary>
        public static bool Jammed(Battle b, BattleUnit u) => JamFactor(b, u) < 1;

        /// <summary>Whether <paramref name="target"/> is painted for side <paramref name="side"/>: a working recon craft of that
        /// side has it as its mark and is within <see cref="PaintRangeKm"/> of it.</summary>
        public static bool Painted(Battle b, BattleUnit target, int side)
        {
            if (b == null || target == null || b.ReconAloft.Count == 0 || target.Side == side) return false;
            double range = PaintRangeKm.Value;
            if (range <= 0) return false;
            foreach (var r in b.ReconAloft)
                if (r.Side == side && r.Target == target && Working(r) && (r.Pos - target.Pos).Length - target.Radius <= range) return true;
            return false;
        }

        /// <summary>How much harder guided round <paramref name="m"/> steers: <see cref="PaintGuidance"/> for a strike craft's
        /// torpedo on a painted target, else 1.</summary>
        public static double Guidance(Battle b, Missile m) =>
            StrikeTorpedoes.IsStrike(m) && Painted(b, m.Target, m.Side) ? Math.Max(1, PaintGuidance.Value) : 1;

        /// <summary>The chance a flare-and-chaff cloud draws off <paramref name="m"/>: the countermeasures' own
        /// (<see cref="Countermeasures.SeduceChance"/>), cut by <see cref="PaintDecoyResist"/> for a strike torpedo on a
        /// painted target.</summary>
        public static double SeduceChance(Battle b, Missile m)
        {
            double chance = Countermeasures.SeduceChance(m.Kind);
            if (StrikeTorpedoes.IsStrike(m) && Painted(b, m.Target, m.Side)) chance *= 1 - Math.Max(0, Math.Min(1, PaintDecoyResist.Value));
            return chance;
        }

        /// <summary>
        /// The mark a recon craft goes for: the enemy capital the nearest of its own side's bombers is attacking, else the
        /// hostile it would most help to paint (a capital before anything smaller, the biggest and nearest first), never a
        /// fighter, nor a hull that has struck or been taken; null with nothing to mark.
        /// </summary>
        public static BattleUnit ChooseMark(Battle b, BattleUnit u)
        {
            BattleUnit bomberMark = null;
            double nearestBomber = double.MaxValue;
            foreach (var f in b.Units)
            {
                if (!f.Present || f.Kind != UnitKind.Fighter || f.Side != u.Side || f.Fighter == null || !f.Fighter.Bomber) continue;
                var t = f.Target;
                if (t == null || t.Kind == UnitKind.Fighter || !b.Hostile(u, t) || t.Surrendering || t.Captured || !WeakPoints.IsCapital(t)) continue;
                double d = (f.Pos - u.Pos).Length;
                if (d < nearestBomber)
                {
                    nearestBomber = d;
                    bomberMark = t;
                }
            }
            if (bomberMark != null) return bomberMark;
            BattleUnit best = null;
            double bestScore = double.MaxValue;
            foreach (var o in b.Units)
            {
                if (o.Kind == UnitKind.Fighter || !b.Hostile(u, o) || o.Surrendering || o.Captured) continue;
                double score = (o.Pos - u.Pos).Length - o.Combat.Spec.Hull / 1000.0 - (WeakPoints.IsCapital(o) ? 30 : 0);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>Recon craft docked aboard <paramref name="s"/> (U.SPC-T526): its recon flight, those of it still berthed
        /// (the recon flight launches with the squadrons and lands with the rest).</summary>
        public static int ReconAboard(Ship s) => s?.Combat == null ? 0 : CarrierWings.Aboard(s.Combat, FighterRole.Recon);
    }
}
