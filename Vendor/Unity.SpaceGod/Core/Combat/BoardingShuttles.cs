using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Boarding shuttles (U.SPC-T486): a piloted, armoured, unarmed craft (<see cref="ShipClass.AssaultShuttle"/>) a carrier
    /// keeps in its hangars beside its squadrons (<see cref="CombatSpec.Shuttles"/>) to take prizes and seize stations
    /// intact. When there is a hull to take (one lying disabled, stunned, or struck on terms that open it to boarding) or a
    /// station or gate fort whose guns have been silenced, and no armed enemy close by it, the carrier sends a shuttle with a
    /// marine team (<see cref="TeamSize"/>) drawn from its barracks. The shuttle flies across, comes alongside, matches the
    /// prize's drift, docks and latches on (<see cref="LatchSeconds"/>) and lets its team in through the airlock into the
    /// compartment it docked against, where the compartment-by-compartment boarding fight (<see cref="Interior"/>) takes
    /// over. It is slower and gentler than a Breacher round fired from a Breacher bay (U.SPC-T466): no drill, no breach,
    /// no damage to the hull, and it can only take what has stopped fighting. It casts off once the fight aboard is
    /// decided and flies home to land, its team's survivors aboard if the fight was lost. A carrier with shuttles sends
    /// them rather than closing to board with its own hull. On campaign, a war fleet whose group carries shuttles and the
    /// marines for a team boards a jump gate whose forts it has silenced and takes it sooner (<see cref="GateSeizeDays"/>).
    /// Every figure is in the settings menu's Broadsides group.
    /// </summary>
    [SettingsSource]
    public static class BoardingShuttles
    {
        static string Seconds(double v) => v.ToString("0", CultureInfo.InvariantCulture) + " s";

        public static readonly RangeSetting TeamSize = new RangeSetting("war.shuttleTeam", SettingGroups.Broadsides,
            "Boarding shuttle team", 24, 6, 60, 1,
            "How many marines a carrier's boarding shuttle carries across to a prize. The carrier keeps enough aboard to defend " +
            "itself and sends a shuttle only with a whole team to spare.", v => v.ToString("0", CultureInfo.InvariantCulture) + " marines",
            "boarding shuttle assault shuttle marines team carrier");

        public static readonly RangeSetting LatchSeconds = new RangeSetting("war.shuttleLatch", SettingGroups.Broadsides,
            "Boarding shuttle docking", 12, 2, 60, 1,
            "How long (seconds) a boarding shuttle lies matched alongside its prize, docking and latching on, before its team goes in " +
            "through the airlock.", Seconds, "boarding shuttle dock latch airlock");

        public static readonly RangeSetting RangeKm = new RangeSetting("war.shuttleRange", SettingGroups.Broadsides,
            "Boarding shuttle range", 40, 5, 120, 5,
            "How far (km) from its carrier a prize may lie for the carrier to send a boarding shuttle to it.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " km", "boarding shuttle range carrier prize");

        public static readonly RangeSetting GateSeizeDays = new RangeSetting("war.shuttleGateDays", SettingGroups.Broadsides,
            "Gate seizure with shuttles", 1, 1, 2, 1,
            "How many days a war fleet whose group carries boarding shuttles holds a jump gate whose forts it has silenced before its " +
            "boarding parties have taken it (a fleet without them takes two).", v => v.ToString("0", CultureInfo.InvariantCulture) + " d",
            "boarding shuttle gate seize capture days");

        /// <summary>Marines a carrier keeps aboard to repel boarders, beyond a shuttle's team.</summary>
        public const double Reserve = 15;

        /// <summary>Seconds between one shuttle leaving a carrier's hangar and the next.</summary>
        public const double LaunchSeconds = 4;

        /// <summary>How near its docking point (km) a shuttle must be, and how closely matched to the prize's drift (km/s), to latch on.</summary>
        public const double DockKm = 0.06, MatchKmS = 0.01;

        /// <summary>How far off the prize's side (km) a shuttle docks.</summary>
        public const double StandOffKm = 0.02;

        /// <summary>How near a prize (km) an armed enemy keeps a carrier from sending a shuttle to it.</summary>
        public const double ThreatKm = 6;

        /// <summary>Whether <paramref name="u"/> is a boarding shuttle.</summary>
        public static bool Is(BattleUnit u) => u?.Fighter != null && u.Fighter.Role == FighterRole.AssaultShuttle;

        /// <summary>Whether any of <paramref name="group"/> carries a shuttle and the marines for a team to go in it: enough
        /// to board a jump gate whose forts are silenced and take it intact (<see cref="GateSeizeDays"/>).</summary>
        public static bool CanSeizeGate(IEnumerable<Ship> group)
        {
            if (group == null) return false;
            foreach (var s in group)
                if (s != null && !s.Destroyed && s.Combat != null && CarrierWings.ReadyShuttle(s.Combat) != null && s.Combat.MarinesAvailable >= TeamSize.Value + Reserve)
                    return true;
            return false;
        }

        /// <summary>Days a war fleet holds a silenced enemy gate before it is theirs: <paramref name="holdDays"/>, or fewer
        /// (<see cref="GateSeizeDays"/>) with boarding shuttles to send.</summary>
        public static double GateHoldDays(double holdDays, bool shuttles) => shuttles ? Math.Min(holdDays, GateSeizeDays.Value) : holdDays;
    }

    // The battle's side of the boarding shuttles (U.SPC-T486): who sends them, at what, and what happens when one docks.
    public sealed partial class Battle
    {
        /// <summary>
        /// A carrier sends a boarding shuttle (U.SPC-T486): with one docked in its shuttle berths fit to fly (a crippled one
        /// stays aboard, U.SPC-T590) and a working hangar, marines enough for a team and its own reserve, and a prize in reach
        /// (<see cref="ShuttleTarget"/>), one shuttle every <see cref="BoardingShuttles.LaunchSeconds"/>, each to a prize no
        /// other shuttle is already flying to.
        /// </summary>
        void SendShuttles(BattleUnit u, double dt)
        {
            var c = u.Combat;
            if (u.Kind != UnitKind.Ship || c.Shuttles <= 0) return;
            if (u.ShuttleLaunchIn > 0)
            {
                u.ShuttleLaunchIn -= dt;
                return;
            }
            if (u.Fleeing || c.Disabled || u.Captured || u.Surrendering || u.PlayerControlled) return;
            if (!(u.Faction.IsPirate || u.Faction.IsMilitary || (Boards != null && Boards(u)))) return;
            if (!HangarWorks(u)) return;
            double team = BoardingShuttles.TeamSize.Value;
            if (c.MarinesAvailable < team + BoardingShuttles.Reserve) return;
            var hull = CarrierWings.ReadyShuttle(c);
            if (hull == null) return;
            var target = ShuttleTarget(u);
            if (target == null) return;
            FighterAI.LaunchShuttle(this, u, target, c.Interior.TakeMarines(team), hull);
            u.ShuttleLaunchIn = BoardingShuttles.LaunchSeconds;
        }

        static bool HangarWorks(BattleUnit u)
        {
            foreach (var s in u.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Hangar && !s.Destroyed) return true;
            return false;
        }

        /// <summary>Whether <paramref name="carrier"/> boards by shuttle: it has one in its hangars or out, so it sends them
        /// rather than closing to board with its own hull.</summary>
        bool BoardsByShuttle(BattleUnit carrier)
        {
            if (CarrierWings.ReadyShuttle(carrier.Combat) != null) return true;
            foreach (var o in Units)
                if (o.Present && BoardingShuttles.Is(o) && o.Fighter.Carrier == carrier) return true;
            return false;
        }

        /// <summary>
        /// Whether a shuttle (or the carrier sending it), <paramref name="seeker"/>, may board <paramref name="o"/> now: a
        /// hostile hull or station on the field, not taken and not being boarded by anyone else, that its side may board
        /// (a hull waiting on terms only once they open it; a merchant's hold is asked for first), and that has stopped
        /// fighting: disabled, struck on terms that open it, or stunned.
        /// </summary>
        public bool ShuttleMayBoard(BattleUnit seeker, BattleUnit o)
        {
            if (seeker == null || o == null || !o.Present || !Hostile(seeker, o) || o.Captured || o.Kind == UnitKind.Fighter) return false;
            var boarder = seeker.Fighter?.Carrier ?? seeker;
            var inside = o.Combat.Interior;
            // A rival's boarding is theirs; an ally's on the same side is joined (U.SPC-T392).
            if (!inside.Admits(seeker.Faction, seeker.Side)) return false;
            if (!MayBoard(boarder, o) || (o.Surrendering && !o.SurrenderOpenToBoarding)) return false;
            if (o.Ship != null && Spares(boarder, o) && o.Ship.HasCargo && !o.SurrenderOpenToBoarding) return false;
            return o.Combat.Disabled || o.SurrenderOpenToBoarding || o.Stunned > 10;
        }

        /// <summary>The prize a shuttle (or the carrier sending it) goes for: the nearest it may board within
        /// <see cref="BoardingShuttles.RangeKm"/>, with no armed enemy near it and no other shuttle of its side already
        /// flying to it (one its side is already boarding only while its defenders outnumber the boarders); or null.</summary>
        public BattleUnit ShuttleTarget(BattleUnit seeker)
        {
            BattleUnit best = null;
            double bestD = BoardingShuttles.RangeKm.Value;
            foreach (var o in Units)
            {
                if (!ShuttleMayBoard(seeker, o)) continue;
                double d = (o.Pos - seeker.Pos).Length;
                if (d > bestD || ThreatNear(seeker, o.Pos, BoardingShuttles.ThreatKm) || ShuttleBound(seeker, o)) continue;
                // A hull its side is already boarding gets another team only while the defenders still outnumber them.
                var inside = o.Combat.Interior;
                if (inside.Boarder != null && inside.Boarders >= inside.Marines + inside.Crew * 0.15) continue;
                bestD = d;
                best = o;
            }
            return best;
        }

        /// <summary>Whether another shuttle of <paramref name="seeker"/>'s side is already flying to <paramref name="o"/>.</summary>
        bool ShuttleBound(BattleUnit seeker, BattleUnit o)
        {
            foreach (var x in Units)
                if (x != seeker && x.Present && x.Side == seeker.Side && BoardingShuttles.Is(x) && x.BoardTarget == o) return true;
            return false;
        }

        /// <summary>
        /// A shuttle docked alongside <paramref name="t"/> latches on and opens its airlock (U.SPC-T486): its team goes in
        /// at the compartment it docked against, fighting as well as it drilled in its carrier's habitats, with no breach
        /// and no damage to the hull. If the hull is no longer theirs to take (boarded by another faction, or already
        /// held), the team stays aboard and the shuttle casts off.
        /// </summary>
        public void ShuttleLatch(BattleUnit shuttle, BattleUnit t)
        {
            shuttle.Latched = t;
            shuttle.LatchLocal = t.ToLocal(shuttle.Pos);
            shuttle.BoardTarget = null;
            shuttle.LatchTimer = 0;
            var inside = t.Combat.Interior;
            var at = inside.Nearest(shuttle.LatchLocal.Z, shuttle.LatchLocal.Y);
            double marines = shuttle.Combat.Interior.TakeMarines(shuttle.Combat.Interior.Marines);
            double drill = shuttle.Fighter?.Drill ?? 1;
            if (marines < 0.5 || !inside.Board(shuttle.Faction, shuttle.Faction.MarineQuality * drill, marines, at, false, Seconds, rng, shuttle.Side))
            {
                shuttle.Combat.Interior.Embark(0, marines);
                shuttle.Latched = null;
                return;
            }
            Emit(FxKind.Latch, shuttle.Pos, t.Pos, shuttle.LengthKm, shuttle.Side, t, at.Index);
        }
    }
}
