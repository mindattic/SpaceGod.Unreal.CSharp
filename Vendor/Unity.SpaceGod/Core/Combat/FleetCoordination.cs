using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A fleet fought from a command ship (U.SPC-T477): the hull that carries the fleet's senior officers, its
    /// communications suite to give their orders, its targeting computers to lay a whole line's guns and its sensor array
    /// to share one picture of the fight. Any hull with a working communications suite inside it
    /// (<see cref="FacilityKinds.CommsSuite"/>) commands; the <see cref="ShipClass.CommandCruiser"/> is built for it, and a
    /// yard can fit the facilities into another capital's free space. Every ship on its side within its reach
    /// (<see cref="ReachOf"/>) fights coordinated: its gunners find their marks sooner (<see cref="RetargetFactor"/>), shoot
    /// truer (<see cref="AccuracyFactor"/>, one share per targeting computer), read a target's course from the shared
    /// picture rather than their own (<see cref="SharedPicture"/>, one share per sensor array) and concentrate on the hull
    /// the command ship is fighting (<see cref="FocusTarget"/>). Lose the command ship (sunk, taken, struck, its bridge shot
    /// away or its systems knocked out) and the ships it was coordinating are thrown into confusion for a while
    /// (<see cref="Disrupted"/>), so every hostile gunner favours it (<see cref="Priority"/>). Each figure is a setting in
    /// the menu's Broadsides group.
    /// </summary>
    [SettingsSource]
    public static class FleetCoordination
    {
        static string Km(double v) => v.ToString("0", CultureInfo.InvariantCulture) + " km";

        static string Percent(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        public static readonly RangeSetting ReachKm = new RangeSetting("command.reachKm", SettingGroups.Broadsides,
            "Command reach", 20, 0, 60, 1,
            "How far (km) a command ship's communications suite reaches in a battle: every ship on its side within it fights " +
            "coordinated. Each further suite fitted adds half again.", Km, "command cruiser battlecruiser coordination reach range comms flagship");

        public static readonly RangeSetting AccuracyPerComputer = new RangeSetting("command.accuracy", SettingGroups.Broadsides,
            "Coordinated accuracy", 0.08, 0, 0.3, 0.01,
            "How much surer every coordinated ship's guns are for each targeting computer its command ship carries (a command " +
            "cruiser carries two).", Percent, "command cruiser targeting computers accuracy coordination fire control");

        public static readonly RangeSetting RetargetCut = new RangeSetting("command.retarget", SettingGroups.Broadsides,
            "Coordinated targeting", 0.5, 0, 0.9, 0.05,
            "How much sooner a coordinated ship's gunners take up a new target, the command ship's staff having already picked it out.",
            Percent, "command cruiser targeting retarget coordination fleet");

        public static readonly RangeSetting Focus = new RangeSetting("command.focus", SettingGroups.Broadsides,
            "Coordinated focus fire", 10, 0, 40, 1,
            "How strongly coordinated ships concentrate on the hull their command ship is fighting (as if it were that many km " +
            "nearer than it is).", Km, "command cruiser focus fire concentrate coordination target");

        public static readonly RangeSetting PicturePerArray = new RangeSetting("command.picture", SettingGroups.Broadsides,
            "Shared sensor picture", 0.4, 0, 1, 0.05,
            "How much of a target's tracking error and range falloff a coordinated ship's gunners lose for each sensor array its " +
            "command ship carries: they lay their guns on the shared picture, not their own.", Percent,
            "command cruiser sensor array picture tracking coordination sensors");

        public static readonly RangeSetting DisruptionPenalty = new RangeSetting("command.disruption", SettingGroups.Broadsides,
            "Loss of command", 0.2, 0, 0.6, 0.05,
            "How much worse the ships a command ship was coordinating shoot, and how much slower they find their targets, for a " +
            "while after it is lost.", Percent, "command cruiser loss disruption confusion flagship lost coordination");

        public static readonly RangeSetting DisruptionSeconds = new RangeSetting("command.disruptionSeconds", SettingGroups.Broadsides,
            "Loss of command lasts", 90, 0, 600, 10,
            "How long (seconds) the ships a lost command ship was coordinating stay in confusion, unless another command ship takes them over.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " s", "command cruiser loss disruption confusion time");

        public static readonly RangeSetting Priority = new RangeSetting("command.priority", SettingGroups.Broadsides,
            "Command ship targeting", 15, 0, 60, 1,
            "How strongly hostile gunners favour a command ship over other targets (as if it were that many km nearer than it is): " +
            "sink it and its fleet fights blind.", Km, "command cruiser priority target targeting focus battlecruiser");

        /// <summary>The most of a tracking error the shared picture can take away.</summary>
        public const double MaxPicture = 0.8;

        // ---------------------------------------------------------------- what a hull carries

        /// <summary>Facilities of kind <paramref name="id"/> inside <paramref name="s"/>: its loadout's if it has one, else
        /// its class's design.</summary>
        public static int Count(Ship s, string id)
        {
            if (s?.Spec == null) return 0;
            if (s.Loadout != null) return s.Loadout.CountFacility(id);
            int n = 0;
            foreach (var f in FacilityLayouts.For(s.Spec.Class))
                if (string.Equals(f.Kind, id, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        /// <summary>Whether <paramref name="s"/> is a command ship: a communications suite inside it.</summary>
        public static bool IsCommandShip(Ship s) => Count(s, FacilityKinds.CommsSuite) > 0;

        /// <summary>Whether hulls of <paramref name="cls"/> are designed as command ships.</summary>
        public static bool IsCommandClass(ShipClass cls)
        {
            foreach (var f in FacilityLayouts.For(cls))
                if (string.Equals(f.Kind, FacilityKinds.CommsSuite, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ---------------------------------------------------------------- in battle

        /// <summary>Whether <paramref name="u"/> is a command ship coordinating its side right now: present, still its own
        /// side's (not taken), not struck or disabled, its systems up and its flag bridge standing.</summary>
        public static bool Coordinates(BattleUnit u) =>
            u != null && u.Present && u.Kind == UnitKind.Ship && u.Ship != null && !u.Captured && !u.Surrendering && u.Stunned <= 0
            && u.Combat != null && !u.Combat.Disabled && u.Combat.BridgeIntact && IsCommandShip(u.Ship);

        /// <summary>How far <paramref name="commander"/>'s orders reach, km: <see cref="ReachKm"/>, half again for each
        /// further communications suite.</summary>
        public static double ReachOf(BattleUnit commander)
        {
            int suites = commander?.Ship == null ? 0 : Count(commander.Ship, FacilityKinds.CommsSuite);
            return suites <= 0 ? 0 : ReachKm.Value * (1 + 0.5 * (suites - 1));
        }

        /// <summary>
        /// A step of the battle's coordination, before anyone thinks: each unit is taken under the nearest working command
        /// ship of its side whose reach it is in (<see cref="BattleUnit.CommandedBy"/>). One whose command ship has just been
        /// lost, with no other to take it over, falls into confusion for <see cref="DisruptionSeconds"/>
        /// (<see cref="BattleUnit.Disrupted"/>); one merely out of reach just fights on its own.
        /// </summary>
        public static void Step(Battle b, double dt)
        {
            List<BattleUnit> commanders = null;
            foreach (var u in b.Units)
                if (Coordinates(u)) (commanders ??= new List<BattleUnit>()).Add(u);
            foreach (var u in b.Units)
            {
                if (!u.Present) continue;
                BattleUnit best = null;
                double bestKm = double.MaxValue;
                if (commanders != null)
                    foreach (var c in commanders)
                    {
                        if (c.Side != u.Side) continue;
                        double d = (c.Pos - u.Pos).Length;
                        if (d <= ReachOf(c) && d < bestKm)
                        {
                            bestKm = d;
                            best = c;
                        }
                    }
                if (best != null) u.Disrupted = 0;
                else if (u.CommandedBy != null && !Coordinates(u.CommandedBy)) u.Disrupted = DisruptionSeconds.Value;
                else if (u.Disrupted > 0) u.Disrupted = Math.Max(0, u.Disrupted - dt);
                u.CommandedBy = best;
            }
        }

        /// <summary>What <paramref name="u"/>'s accuracy is multiplied by: more under a command ship with targeting
        /// computers, less in the confusion after losing one.</summary>
        public static double AccuracyFactor(BattleUnit u)
        {
            if (u?.CommandedBy?.Ship != null) return 1 + AccuracyPerComputer.Value * Count(u.CommandedBy.Ship, FacilityKinds.TargetingComputers);
            return u != null && u.Disrupted > 0 ? Math.Max(0, 1 - DisruptionPenalty.Value) : 1;
        }

        /// <summary>What the time <paramref name="u"/>'s gunners take to choose a target again is multiplied by.</summary>
        public static double RetargetFactor(BattleUnit u)
        {
            if (u?.CommandedBy != null) return Math.Max(0.05, 1 - RetargetCut.Value);
            return u != null && u.Disrupted > 0 ? 1 + DisruptionPenalty.Value : 1;
        }

        /// <summary>The share of a target's tracking error and of its range falloff <paramref name="u"/>'s gunners are spared
        /// by the shared picture, 0 with none.</summary>
        public static double SharedPicture(BattleUnit u)
        {
            if (u?.CommandedBy?.Ship == null) return 0;
            return Math.Min(MaxPicture, PicturePerArray.Value * Count(u.CommandedBy.Ship, FacilityKinds.SensorArray));
        }

        /// <summary>The hull <paramref name="u"/>'s command ship is fighting, for it to concentrate on, or null.</summary>
        public static BattleUnit FocusTarget(BattleUnit u)
        {
            var c = u?.CommandedBy;
            return c == null || c == u ? null : c.Target;
        }
    }
}
