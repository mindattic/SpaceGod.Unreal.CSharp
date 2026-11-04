using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// One warning a gate picket has given its navy (U.SPC-T480): who came in, how, and when. Kept by the system's
    /// <see cref="War"/> (<see cref="War.PicketWarnings"/>), whose command acts on a fresh one by sending a group to hold
    /// the gate while there is still time (see <see cref="GatePickets.WarningDays"/>).
    /// </summary>
    public sealed class PicketWarning
    {
        /// <summary>The gate the warning came from (the picket's), and the navy that holds it.</summary>
        public JumpGate Gate;
        public Faction Owner;

        /// <summary>The picket that saw it.</summary>
        public Ship Picket;

        /// <summary>The flagship of the group that came in, its faction, and how many hulls it had.</summary>
        public Ship Intruder;
        public Faction By;
        public int Hulls;

        /// <summary>Dropped out of FTL nearby (true), or came through the gate (false).</summary>
        public bool SelfJump;

        public double Jd;

        public override string ToString() => $"{Picket?.Name}: {Hulls} of the {By?.Name} {(SelfJump ? "out of FTL" : "through the gate")}";
    }

    /// <summary>
    /// Gate pickets (U.SPC-T480): small, sensor-heavy hulls (<see cref="ShipClass.GatePicket"/>) a navy stations at the exit
    /// of every jump gate it holds, for early warning. A picket carries a long-range search suite (<see cref="SensorSuite.Picket"/>)
    /// that joins its navy's picture like any other hull's, so raiders lying dark off the gate are made out and command
    /// sends a group to clear them (see <see cref="War"/>'s gate orders). Whatever hostile and armed comes through the gate,
    /// or drops out of FTL within <see cref="JumpWatchAu"/> of a picket, is reported at once: the intruders go straight into
    /// the navy's picture, the news carries the warning, and command sends a group strong enough to hold the gate against
    /// them while they are still there (<see cref="War.PicketWarnings"/>). A gate held with pickets gives its owner warning
    /// and time to react; one without them is blind until the intruders reach a port.
    /// <para>Raiders know it: a band lying off a gate goes for its pickets first, taking on more guns to silence one than
    /// it would for warships alone (<see cref="RaidDoctrine.PicketNerve"/>), and its gunners shoot a picket before anything
    /// else in the fight (<see cref="RaidDoctrine.TargetBias"/>).</para>
    /// <para>A picket flies as a patrol wing (<see cref="Ship.PatrolWing"/>: never merged, sent away or counted as a warship),
    /// holds its post off the gate (<see cref="Ship.PicketPostKm"/>) and never opens a fight; badly hurt it puts in at a yard
    /// and comes back. One lost is replaced, at the navy's expense, after <see cref="RebuildDays"/>; a gate that changes hands
    /// sends the old holder's pickets off to patrol its ports, and the new holder posts its own. Distinct from a navy's
    /// destroyer patrol wing (<see cref="PatrolWings"/>), which flies a beat of its ports.</para>
    /// </summary>
    [SettingsSource]
    public static class GatePickets
    {
        /// <summary>The settings menu's pickets per gate (AI): how many a navy keeps at each gate it holds.</summary>
        public static readonly RangeSetting PerGate = new RangeSetting("pickets.perGate", SettingGroups.Ai,
            "Gate pickets per gate", 1, 0, 4, 1,
            "How many gate pickets (small, sensor-heavy early-warning hulls) a navy keeps at the exit of each jump gate it holds. 0 leaves the gates blind.",
            v => v.ToString("0", CultureInfo.InvariantCulture), "picket gate sensor early warning navy");

        /// <summary>The settings menu's picket FTL watch (AI): how far from a picket a ship dropping out of FTL is reported.</summary>
        public static readonly RangeSetting JumpWatchAu = new RangeSetting("pickets.jumpWatch", SettingGroups.Ai,
            "Picket FTL watch", 4, 0, 20, 0.5,
            "How far (AU) from a gate picket a hostile group dropping out of FTL under its own drive is seen and reported to the picket's navy.",
            v => v.ToString("0.#", CultureInfo.InvariantCulture) + " AU", "picket gate ftl jump drop out warning");

        /// <summary>How far inside its gate (toward the system root) a picket holds, km: clear of the ring, where everything
        /// coming out passes it, and nearer the gate than raiders lying off its approach (<see cref="JumpGate.ApproachInsetKm"/>).</summary>
        public const double PostInsetKm = 250;

        /// <summary>How far apart a gate's pickets hold, side by side across the way in, km.</summary>
        public const double PostSpacingKm = 120;

        /// <summary>How long command acts on a picket's warning (days): it sends a group to hold the gate while the warned-of
        /// intruders are still there.</summary>
        public const double WarningDays = 2;

        /// <summary>The most warnings a system's war keeps (the oldest go).</summary>
        public const int WarningsKept = 64;

        /// <summary>How long a gate goes short of its pickets before its navy posts a new one (days).</summary>
        public const double RebuildDays = 10;

        /// <summary>The damage (share of its hull and systems) past which a picket leaves its post for a yard.</summary>
        public const double RepairDamage = 0.5;

        /// <summary>Whether <paramref name="gate"/>'s holder keeps pickets there: a state with a navy, as for its forts.</summary>
        public static bool Keeps(JumpGate gate) =>
            gate?.Owner != null && gate.Owner.IsMilitary && !gate.Owner.IsPirate && gate.Owner.Kind == PolityKind.State;

        /// <summary>How many pickets <paramref name="gate"/>'s holder keeps there.</summary>
        public static int Wanted(JumpGate gate) => Keeps(gate) ? Math.Max(0, (int)Math.Round(PerGate.Value)) : 0;

        /// <summary>The post of the <paramref name="index"/>th of <paramref name="count"/> pickets at <paramref name="gate"/>, km off
        /// its system's root: <see cref="PostInsetKm"/> inside the gate, spread side by side across the way in.</summary>
        public static DVec3 Post(JumpGate gate, int index, int count)
        {
            var axis = gate.PositionKm.LengthSquared > 1e-9 ? gate.PositionKm.Normalized : DVec3.UnitX;
            var side = DVec3.Cross(axis, Math.Abs(axis.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX).Normalized;
            double across = (index - 0.5 * (Math.Max(1, count) - 1)) * PostSpacingKm;
            return gate.PositionKm - axis * PostInsetKm + side * across;
        }

        /// <summary>Whether <paramref name="s"/> is a picket of <paramref name="gate"/>, afloat in its world.</summary>
        static bool Serves(World w, Ship s, JumpGate gate) => s.PicketGate == gate && !s.Destroyed && !s.Derelict && w.Fleet.IsPresent(s);

        /// <summary>The pickets of <paramref name="gate"/> (its holder's or not), afloat in its system.</summary>
        public static List<Ship> Of(JumpGate gate)
        {
            var list = new List<Ship>();
            var w = gate?.Home?.World;
            if (w == null) return list;
            foreach (var s in w.Fleet.Ships)
                if (Serves(w, s, gate)) list.Add(s);
            return list;
        }

        /// <summary>Whether <paramref name="picket"/> is at its post off its gate (not away at a yard, not in flight).</summary>
        public static bool OnPost(World w, Ship picket) =>
            picket?.PicketGate != null && !picket.Destroyed && Fleet.IsAt(picket, w.Catalog.Root.Index) && picket.Orbit != null &&
            (picket.Orbit.OffsetAt(w.Jd) - picket.PicketPostKm).Length <= War.GateReachKm;

        /// <summary>Posts every gate's pickets in <paramref name="network"/> (the galaxy's start: free, as its forts are).</summary>
        public static void PostAll(GateNetwork network, double jd)
        {
            if (network == null) return;
            foreach (var l in network.Links)
            {
                PostFull(l.AtA, jd);
                PostFull(l.AtB, jd);
            }
        }

        /// <summary>Posts as many pickets at <paramref name="gate"/> as its holder keeps there (a gate just built or rebuilt
        /// opens watched, as it opens guarded).</summary>
        public static void PostFull(JumpGate gate, double jd)
        {
            if (gate?.Home?.World == null) return;
            int wanted = Wanted(gate);
            int have = 0;
            foreach (var s in Of(gate))
                if (s.Faction == gate.Owner) have++;
            for (int i = have; i < wanted; i++) Station(gate, i, wanted, jd);
        }

        /// <summary>
        /// Builds a picket for <paramref name="gate"/>'s holder and posts it off the gate (the <paramref name="index"/>th of
        /// <paramref name="count"/>). Added to the system's fleet directly, drawing nothing from its random stream.
        /// </summary>
        public static Ship Station(JumpGate gate, int index, int count, double jd)
        {
            var w = gate.Home.World;
            var f = gate.Owner;
            // Gates are named for where they lead, so two systems can each hold a "Centauri Gate"; the home system keeps hull names unique.
            string name = gate.Name + " Picket" + (index > 0 ? " " + (index + 1) : "") + (gate.Home?.Name != null ? " (" + gate.Home.Name + ")" : "");
            var ship = new Ship
            {
                Name = f.Prefix.Length > 0 ? f.Prefix + " " + name : name,
                Spec = ShipClassSpec.Of(ShipClass.GatePicket),
                Faction = f,
                Role = ShipRole.Patrol,
                Combat = new ShipCombat(CombatSpec.Of(ShipClass.GatePicket), ShipClass.GatePicket),
                PatrolWing = true,
                PicketGate = gate,
                PicketPostKm = Post(gate, index, count),
            };
            w.Fleet.Receive(ship);
            ship.BandId = ship.Id;
            Fleet.PinAt(ship, w.Catalog.Root.Index, ship.PicketPostKm, -gate.PositionKm.Normalized, jd);
            w.NotifyAdded(ship);
            return ship;
        }

        /// <summary>
        /// A day's upkeep of <paramref name="gate"/>'s pickets (run by <see cref="InterstellarWars"/>): the old holder's sent
        /// off to patrol its ports once the gate has changed hands, and one lost replaced at its navy's expense once the gate
        /// has been short of them for <see cref="RebuildDays"/>, when it can pay and crew it and nobody is fighting there.
        /// </summary>
        public static void Upkeep(JumpGate gate, double jd)
        {
            var w = gate?.Home?.World;
            if (w == null) return;
            int have = 0;
            foreach (var s in Of(gate))
            {
                if (s.Faction == gate.Owner && Keeps(gate)) have++;
                else StandDown(w, s);
            }
            int wanted = Wanted(gate);
            if (have >= wanted)
            {
                gate.PicketsShortSinceJd = double.NaN;
                return;
            }
            if (double.IsNaN(gate.PicketsShortSinceJd)) gate.PicketsShortSinceJd = jd;
            if (jd - gate.PicketsShortSinceJd < RebuildDays) return;
            var f = gate.Owner;
            double price = Economy.HullPrice(ShipClass.GatePicket);
            if (f.Treasury < price || !Crews.CanCrew(f, ShipClass.GatePicket) || w.War.BattleAt(w.Catalog.Root.Index) != null) return;
            f.Treasury -= price;
            Crews.Draft(f, Crews.Complement(ShipClass.GatePicket));
            Station(gate, have, wanted, jd);
            gate.PicketsShortSinceJd = have + 1 >= wanted ? double.NaN : jd;
        }

        /// <summary>A picket whose gate is no longer its navy's: off its post, to patrol its navy's ports here as a wing of one.</summary>
        static void StandDown(World w, Ship picket)
        {
            picket.PicketGate = null;
            var ports = PatrolWings.Ports(w, picket.Faction);
            if (ports.Count == 0) return;
            picket.Route = PatrolWings.Beat(ports, 0, 1);
            picket.RouteIndex = 0;
        }

        // ---------------------------------------------------------------- warnings

        /// <summary>
        /// <paramref name="ships"/> (a group, flagship first) have come out of the gate <paramref name="exit"/> (U.SPC-T480):
        /// its holder's pickets on post there report them if they are hostile and armed. Returns the warning, or null.
        /// </summary>
        public static PicketWarning OnArrival(JumpGate exit, IReadOnlyList<Ship> ships, double jd)
        {
            var w = exit?.Home?.World;
            if (w == null || ships == null || ships.Count == 0) return null;
            foreach (var p in Of(exit))
                if (p.Faction == exit.Owner && OnPost(w, p)) return Warn(w, exit, p, ships, false, jd);
            return null;
        }

        /// <summary>
        /// <paramref name="ships"/> have dropped out of FTL at <paramref name="atKm"/> (off the system root) in
        /// <paramref name="system"/>: any picket on post within <see cref="JumpWatchAu"/> reports them to its navy if they
        /// are hostile and armed. Returns the first warning given, or null.
        /// </summary>
        public static PicketWarning OnDropOut(StarSystem system, IReadOnlyList<Ship> ships, DVec3 atKm, double jd)
        {
            var w = system?.World;
            if (w == null || ships == null || ships.Count == 0 || JumpWatchAu.Value <= 0) return null;
            double reach = JumpWatchAu.Value * Astro.AuKm;
            PicketWarning first = null;
            var warned = new HashSet<Faction>();
            foreach (var p in w.Fleet.Ships)
            {
                if (p.PicketGate == null || warned.Contains(p.Faction) || !Serves(w, p, p.PicketGate) || !OnPost(w, p)) continue;
                if ((p.PicketPostKm - atKm).Length > reach) continue;
                var warning = Warn(w, p.PicketGate, p, ships, true, jd);
                if (warning == null) continue;
                warned.Add(p.Faction);
                first ??= warning;
            }
            return first;
        }

        /// <summary>Whether a picket of <paramref name="owner"/> warns of <paramref name="s"/>: an armed hull (a warship or a
        /// raider) of a power it is hostile to.</summary>
        public static bool Threat(World w, Faction owner, Ship s) =>
            s != null && !s.Destroyed && s.Faction != null && s.Faction != owner && (s.Spec.IsMilitary || s.Faction.IsPirate) &&
            w.Diplomacy.Hostile(owner, s.Faction);

        static PicketWarning Warn(World w, JumpGate gate, Ship picket, IReadOnlyList<Ship> ships, bool selfJump, double jd)
        {
            var owner = picket.Faction;
            var leader = ships[0].Controller ?? ships[0];
            int hulls = 0;
            foreach (var s in ships)
                if (Threat(w, owner, s)) hulls++;
            if (hulls == 0) return null;
            // Straight into its navy's picture: the picket has them in its sights (raiders squawk nothing, but a picket
            // makes out a hull's class as it comes through).
            if (w.Sensors != null)
                foreach (var s in ships)
                    if (Threat(w, owner, s)) w.Sensors.Knows(owner, s, jd);
            var warning = new PicketWarning
            {
                Gate = gate, Owner = owner, Picket = picket, Intruder = leader, By = leader.Faction, Hulls = hulls, SelfJump = selfJump, Jd = jd,
            };
            var list = w.War.PicketWarnings;
            list.Add(warning);
            if (list.Count > WarningsKept) list.RemoveRange(0, list.Count - WarningsKept);
            string what = hulls == 1 ? (leader.Faction.IsPirate ? "a raider" : "a warship") : hulls + (leader.Faction.IsPirate ? " raiders" : " warships");
            string how = selfJump ? "dropping out of FTL nearby" : "coming through the gate";
            w.Log(jd, $"{picket.Name} warns the {owner.Name}: {what} of the {leader.Faction.Name} ({leader.Name}) {how}", WorldEventKind.Battle,
                w.Catalog.Root.Index);
            return warning;
        }
    }
}
