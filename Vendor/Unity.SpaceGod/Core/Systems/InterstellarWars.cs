using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>Where a war fleet's campaign stands (see <see cref="Campaign"/>).</summary>
    public enum CampaignStage
    {
        /// <summary>On its way over the gates to the system it means to take, forcing and taking enemy gates as it meets them.</summary>
        Marching,

        /// <summary>In the goal system: breaking the defences over each enemy settlement and landing marines to take it.</summary>
        Invading,

        /// <summary>Every enemy settlement in its goal taken: it stays to hold the system until the peace.</summary>
        Holding,

        /// <summary>Called home (beaten, spent, or the war is over), the way it can get through.</summary>
        Returning,

        /// <summary>Home again, or lost.</summary>
        Over,
    }

    /// <summary>
    /// One war fleet's campaign (U.SPC-T92): a navy group sent across the gates to take an enemy system. It plans its way
    /// over the gate graph (<see cref="GateNetwork.Route"/>, forcing gates the enemy holds), fights each such gate's forts
    /// and holds the gate until it is taken, then, in the goal system, breaks the platforms and warships over each enemy
    /// settlement and lands its marines to take it. The flagship carries the campaign on its <see cref="Voyage"/>, so it
    /// survives the gates' carrying a hull from one system to the next and a flagship lost to its heir.
    /// </summary>
    public sealed class Campaign
    {
        public Conflict War;
        public Faction Faction;

        /// <summary>Where it set out from (and comes back to), and the system it is taking.</summary>
        public StarSystem Home, Goal;

        public CampaignStage Stage;
        public double SetOutJd, EndedJd = double.NaN;

        /// <summary>Its flagship's name, as it last stood (the flagship can change if it is lost).</summary>
        public string Flagship;

        /// <summary>The settlement it is reducing now, or null.</summary>
        public Settlement Objective;

        /// <summary>The broken enemy gate it holds beside, and for how many days, until it is taken (see <see cref="InterstellarWars.GateHoldDays"/>).</summary>
        public JumpGate Besieging;
        public double BesiegedDays;

        /// <summary>Gate battles it has fought, gates and settlements it has taken.</summary>
        public int GateBattles, GatesTaken, SettlementsTaken;

        /// <summary>Destroyed or taken to the last hull.</summary>
        public bool Lost;

        /// <summary>What it has left of its propellant, munitions and spares, and its supply line (U.SPC-T93, see <see cref="MilitaryLogistics"/>).</summary>
        public readonly FleetSupply Supply = new FleetSupply();

        /// <summary>The kind of force sent (U.SPC-T474, see <see cref="FleetDoctrine"/>): a gate fleet marching through the gates,
        /// or a jump strike group crossing under its own drives.</summary>
        public ForceKind Force;

        /// <summary>What it was sent to do (U.SPC-T474): invade, open a second front, relieve or raid.</summary>
        public CampaignMission Mission;

        /// <summary>When a strike group reached the system it was sent to (NaN until then).</summary>
        public double ArrivedJd = double.NaN;

        /// <summary>Ports a raid has broken the defences of, and garrisons a relief has stiffened.</summary>
        public int PortsRaided, Relieved;

        /// <summary>The ports a raid has fallen on, or a relief has seen to, so far.</summary>
        public readonly List<Settlement> Raided = new List<Settlement>();

        public bool Active => Stage != CampaignStage.Over;

        public override string ToString() => $"{Faction?.Name} {(Force == ForceKind.JumpStrikeGroup ? "strike group" : "campaign")} ({Mission}) for {Goal?.Name} ({Stage})";
    }

    /// <summary>Marines put down on an enemy settlement, fighting its garrison (U.SPC-T92).</summary>
    public sealed class Landing
    {
        public Settlement Target;
        public Faction By;
        public Campaign Campaign;
        public double Troops, SinceJd;
    }

    /// <summary>
    /// A war between two powers over systems (U.SPC-T92): the aggressor's goals (the enemy systems it means to take), the
    /// campaigns either side has sent, and, once it is over, the treaty that ended it.
    /// </summary>
    public sealed class Conflict
    {
        public Faction Aggressor, Defender;
        public double StartJd, EndedJd = double.NaN;

        /// <summary>The systems the aggressor means to take from the defender.</summary>
        public readonly List<StarSystem> Goals = new List<StarSystem>();

        public readonly List<Campaign> Campaigns = new List<Campaign>();

        /// <summary>When either side last had a campaign in the field (for a stalemate's end).</summary>
        public double LastActiveJd;

        /// <summary>The peace that ended it; null while it is fought.</summary>
        public Treaty Treaty;

        public bool Over => Treaty != null;

        public bool Involves(Faction f) => f != null && (f == Aggressor || f == Defender);

        public Faction Enemy(Faction f) => f == Aggressor ? Defender : f == Defender ? Aggressor : null;

        public override string ToString() => $"{Aggressor?.Name} against {Defender?.Name}";
    }

    /// <summary>
    /// The wars between the systems' powers (U.SPC-T92), run by the galaxy whether or not the player is in any of the
    /// systems involved (a system nobody is watching fights at coarse detail, see <see cref="Galaxy.Focus"/>).
    ///
    /// A state goes to war with a neighbour (<see cref="Declare"/>, or of its own accord when its navy far outweighs the
    /// other's, see <see cref="AutoDeclare"/>) with war goals: the neighbour's systems it means to take. Each day each side
    /// that has a goal and a navy group fit to go sends it on a <see cref="Campaign"/> over the gate graph; gates the enemy
    /// holds are forced: the fleet fights the gate's forts (<see cref="War.StartGateBattle"/>), holds the broken gate
    /// <see cref="GateHoldDays"/> and takes it. In the goal system it breaks each enemy settlement's platforms and the
    /// warships defending it (the system's own <see cref="War"/> fights those battles), then lands its marines against
    /// the settlement's <see cref="Settlement.Garrison"/>; when the garrison is beaten the settlement is occupied: it
    /// changes hands, the marines are its garrison, and its people are restive (<see cref="Settlement.Unrest"/>) and may
    /// rise against a thin garrison while the war lasts. A defender sends campaigns to take back what it has lost.
    ///
    /// A war ends in a <see cref="Treaty"/> signed through <see cref="Diplomacy.Sign"/>: when the aggressor has taken its
    /// goals, the defender's navy is gone, a year has passed, or neither side has had a fleet in the field for a season.
    /// Every system where one side now holds the most populous settlement, taken from the other, is ceded to it: the
    /// loser's other settlements and its gates there go with it. Whatever else was occupied goes back, gates taken
    /// elsewhere go back to their owners, the fleets go home, and the two keep the peace for <see cref="TruceDays"/>.
    /// </summary>
    public sealed class InterstellarWars
    {
        readonly Galaxy galaxy;
        readonly Random rng;
        readonly List<Conflict> wars = new List<Conflict>();
        readonly List<Conflict> ended = new List<Conflict>();
        readonly List<Campaign> campaigns = new List<Campaign>();
        readonly List<Landing> landings = new List<Landing>();

        /// <summary>
        /// Gates taken in a war still being fought, with who held them before and which war took them (a treaty that
        /// does not cede their system gives them back). Kept per war, not just per gate (U.SPC-T323): two overlapping
        /// wars can each force the same gate in turn (the second declared by hand, past the one-war-per-faction rule
        /// a plain <see cref="Declare"/> enforces), and the first of them to make peace must leave the gate alone once
        /// it is the other war's to answer for, rather than handing it back to a now-stale "before".
        /// </summary>
        readonly Dictionary<JumpGate, (Conflict War, Faction Before)> takenGates = new Dictionary<JumpGate, (Conflict War, Faction Before)>();

        double nextDailyJd, nextDeclareJd;

        /// <summary>Whether states go to war of their own accord (see <see cref="Declare"/>). A scripted war turns it off.</summary>
        public bool AutoDeclare = true;

        /// <summary>Wars being fought, and those ended (with their treaties).</summary>
        public IReadOnlyList<Conflict> Active => wars;
        public IReadOnlyList<Conflict> Ended => ended;

        /// <summary>Every campaign sent so far, in the field or over.</summary>
        public IReadOnlyList<Campaign> Campaigns => campaigns;

        /// <summary>Marines on the ground, fighting garrisons.</summary>
        public IReadOnlyList<Landing> Landings => landings;

        /// <summary>The war fleets' supply: their stores, naval bases and supply convoys (U.SPC-T93).</summary>
        public readonly MilitaryLogistics Logistics;

        /// <summary>Troops a settlement's own garrison musters, per person living there.</summary>
        public const double GarrisonPerPerson = 0.001;

        /// <summary>The smallest garrison anywhere settled.</summary>
        public const double MinGarrison = 15;

        /// <summary>Days a war fleet holds a gate whose forts it has broken before it is theirs.</summary>
        public const double GateHoldDays = 2;

        /// <summary>A day's ground fighting: the share of its own strength each side's troops kill of the other's.</summary>
        public const double GroundRate = 0.3;

        /// <summary>How much a garrison fighting from its own works is worth, man for man, over marines coming down on it.</summary>
        public const double DugIn = 1.5;

        /// <summary>Unrest in a settlement just occupied.</summary>
        public const double OccupationUnrest = 0.8;

        /// <summary>Marines go down past defending warships only while the fleet overhead has this many times their strength (inverted: their share of it).</summary>
        public const double LandingCover = 0.5;

        /// <summary>A war fleet sets out only with this much more strength than it expects to meet (gate forts, platforms, warships).</summary>
        public const double LaunchRatio = 1.2;

        /// <summary>A war is ended by a treaty after this long whatever stands (days).</summary>
        public const double MaxWarDays = 365;

        /// <summary>Neither side with a fleet in the field for this long: a stalemate, and a peace (days).</summary>
        public const double StalemateDays = 90;

        /// <summary>How long a treaty keeps the peace between its two sides (days).</summary>
        public const double TruceDays = 730;

        /// <summary>A state goes to war of its own accord only with a navy this many times the neighbour's.</summary>
        public const double DeclareRatio = 3;

        /// <summary>Monthly chance that a state able to (see <see cref="DeclareRatio"/>) goes to war, when no war is being fought.</summary>
        public double DeclareChancePerMonth = 0.12;

        public InterstellarWars(Galaxy galaxy, double jd, int seed = 9203)
        {
            this.galaxy = galaxy;
            rng = new Random(seed);
            Logistics = new MilitaryLogistics(galaxy);
            nextDailyJd = jd + 0.75;
            nextDeclareJd = jd + 60;
        }

        Polities Polities => galaxy.Polities;
        Diplomacy Diplomacy => galaxy.Polities.Diplomacy;
        InterstellarTraffic Traffic => galaxy.Traffic;

        /// <summary>The war being fought between <paramref name="a"/> and <paramref name="b"/>, or null.</summary>
        public Conflict Between(Faction a, Faction b)
        {
            foreach (var w in wars)
                if (w.Involves(a) && w.Involves(b)) return w;
            return null;
        }

        /// <summary>The war <paramref name="f"/> is fighting, or null.</summary>
        public Conflict Of(Faction f)
        {
            foreach (var w in wars)
                if (w.Involves(f)) return w;
            return null;
        }

        // ---------------------------------------------------------------- going to war

        /// <summary>
        /// <paramref name="aggressor"/> goes to war with <paramref name="defender"/> (they are at war in the one diplomacy table
        /// every system shares) to take <paramref name="goals"/>: systems where the defender holds settlements. None given,
        /// it goes for every system the defender holds. Null if either is no state, they are bound by a treaty's truce, or
        /// either is already fighting such a war.
        /// </summary>
        public Conflict Declare(Faction aggressor, Faction defender, double jd, params StarSystem[] goals)
        {
            if (aggressor == null || defender == null || aggressor == defender || aggressor.IsPirate || defender.IsPirate) return null;
            if (Of(aggressor) != null || Of(defender) != null || Diplomacy.InTruce(aggressor, defender, jd)) return null;
            // A pact that keeps the peace must be broken first (Statecraft.DeclareWar), and a vassal makes no war of its own (U.SPC-T427).
            var statecraft = Polities.Statecraft;
            if (statecraft.BoundToPeace(aggressor, defender)) return null;
            var liege = statecraft.OverlordOf(aggressor);
            if (liege != null && liege != defender) return null;
            var w = new Conflict { Aggressor = aggressor, Defender = defender, StartJd = jd, LastActiveJd = jd };
            if (goals != null && goals.Length > 0) w.Goals.AddRange(goals);
            else
                foreach (var s in galaxy.Systems)
                    if (Polities.HolderOf(s.World) == defender) w.Goals.Add(s);
            w.Goals.RemoveAll(s => s == null || !EnemyHolds(defender, s));
            if (w.Goals.Count == 0) return null;
            Diplomacy.Set(aggressor, defender, Relation.War);
            wars.Add(w);
            string goalNames = Names(w.Goals);
            foreach (var world in Involved(w))
            {
                world.War.RevokeBookings();
                world.Log(jd, $"The {aggressor.Name} declares war on the {defender.Name}, its goal {goalNames}", WorldEventKind.Diplomacy);
            }
            return w;
        }

        static string Names(List<StarSystem> systems)
        {
            var names = new List<string>();
            foreach (var s in systems) names.Add(s.Name);
            return names.Count <= 1 ? string.Concat(names) : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1];
        }

        /// <summary>The worlds where either side holds anything, or a campaign of the war is.</summary>
        List<World> Involved(Conflict w)
        {
            var list = new List<World>();
            foreach (var world in Polities.Worlds)
                foreach (var s in world.Economy.Settlements)
                    if (!s.Fallen && w.Involves(s.Owner))
                    {
                        list.Add(world);
                        break;
                    }
            return list;
        }

        /// <summary>Whether <paramref name="f"/> still holds a standing settlement (not a haven) in <paramref name="s"/>.</summary>
        static bool EnemyHolds(Faction f, StarSystem s)
        {
            foreach (var st in s.World.Economy.Settlements)
                if (st.Owner == f && !st.Fallen && st.Kind != SettlementKind.Haven) return true;
            return false;
        }

        // ---------------------------------------------------------------- the day

        public void Update(double jd)
        {
            while (jd >= nextDailyJd)
            {
                Daily(nextDailyJd);
                nextDailyJd += 1;
            }
        }

        void Daily(double jd)
        {
            foreach (var w in wars)
            {
                Plan(w, w.Aggressor, jd);
                Plan(w, w.Defender, jd);
            }
            foreach (var c in new List<Campaign>(campaigns))
                if (c.Active) Step(c, jd);
            GroundWar(jd);
            Garrisons(jd);
            foreach (var w in new List<Conflict>(wars)) Judge(w, jd);
            RebuildForts(jd);
            // The gates' pickets: handed on with their gates, and replaced when lost (U.SPC-T480).
            if (galaxy.Network != null)
                foreach (var link in galaxy.Network.Links)
                {
                    GatePickets.Upkeep(link.AtA, jd);
                    GatePickets.Upkeep(link.AtB, jd);
                }
            if (AutoDeclare && jd >= nextDeclareJd)
            {
                nextDeclareJd = jd + 30;
                Ambitions(jd);
            }
        }

        // ---------------------------------------------------------------- campaigns

        /// <summary>The systems <paramref name="f"/> is fighting for in <paramref name="w"/>: the aggressor its goals still
        /// held against it, the defender those where it has lost settlements to the aggressor.</summary>
        List<StarSystem> GoalsOf(Conflict w, Faction f)
        {
            var list = new List<StarSystem>();
            var enemy = w.Enemy(f);
            if (f == w.Aggressor)
            {
                foreach (var s in w.Goals)
                    if (EnemyHolds(enemy, s)) list.Add(s);
                return list;
            }
            foreach (var s in galaxy.Systems)
                foreach (var st in s.World.Economy.Settlements)
                    if (st.Owner == enemy && st.OccupiedFrom == f && !st.Fallen)
                    {
                        list.Add(s);
                        break;
                    }
            return list;
        }

        /// <summary>
        /// A day of <paramref name="f"/>'s strategic command in <paramref name="w"/> (U.SPC-T474, see <see cref="FleetDoctrine"/>):
        /// with no gate fleet in the field, its best one marches on its next goal; with no strike group out, its best jump
        /// strike group is given the mission its objectives call for. Neither goes if it would leave a system of its own
        /// short of the reserve it keeps against the enemy's strike groups.
        /// </summary>
        void Plan(Conflict w, Faction f, double jd)
        {
            Campaign main = null, strike = null;
            foreach (var c in w.Campaigns)
                if (c.Active && c.Faction == f)
                {
                    w.LastActiveJd = jd;
                    if (c.Force == ForceKind.JumpStrikeGroup) strike ??= c;
                    else main ??= c;
                }
            bool doctrine = FleetDoctrine.StrikeGroups.Value;
            // Before the doctrine, a navy kept one fleet in the field whatever it was.
            if (!doctrine && (main != null || strike != null)) return;
            double threat = FleetDoctrine.StrikeThreat(galaxy, f);
            if (main == null) PlanGateFleet(w, f, jd, doctrine, threat);
            if (doctrine && strike == null) PlanStrike(w, f, jd, threat);
        }

        /// <summary>Sends <paramref name="f"/>'s best gate fleet fit to go on a campaign for its next goal, when the group is strong
        /// enough for what it expects to meet (with the <paramref name="doctrine"/> off, its best group of any kind).</summary>
        void PlanGateFleet(Conflict w, Faction f, double jd, bool doctrine, double threat)
        {
            var goals = GoalsOf(w, f);
            if (goals.Count == 0) return;

            Ship best = null;
            StarSystem from = null;
            double bestStrength = 0;
            foreach (var s in galaxy.Systems)
            {
                var world = s.World;
                foreach (var ship in world.Fleet.Ships)
                {
                    if (!Fit(world, ship, f)) continue;
                    // A jump strike group is kept for the missions only it can fly (U.SPC-T474).
                    if (doctrine && FleetDoctrine.IsStrikeGroup(world, ship)) continue;
                    if (!FleetDoctrine.LeavesReserve(world, ship, f, threat)) continue;
                    double strength = world.War.GroupStrength(ship);
                    if (strength > bestStrength)
                    {
                        bestStrength = strength;
                        best = ship;
                        from = s;
                    }
                }
            }
            if (best == null) return;

            // The nearest goal (by the way it would go) that this group is strong enough for.
            StarSystem goal = null;
            List<JumpGate> route = null;
            double bestDays = double.MaxValue;
            foreach (var g in goals)
            {
                // Taking back what was lost at home is its own navy's work there, not a campaign's.
                if (g == from) continue;
                var r = galaxy.Network.Route(from, g, f, jd, force: true);
                if (r == null) continue;
                if (bestStrength < LaunchRatio * Expected(f, w.Enemy(f), g, r)) continue;
                double days = GateNetwork.RouteDays(r);
                if (days < bestDays)
                {
                    bestDays = days;
                    goal = g;
                    route = r;
                }
            }
            if (goal == null) return;

            var c2 = new Campaign { War = w, Faction = f, Home = from, Goal = goal, Stage = CampaignStage.Marching, SetOutJd = jd, Flagship = best.Name };
            if (Traffic.SendCampaign(best, from, goal, c2, jd) == null) return;
            w.Campaigns.Add(c2);
            campaigns.Add(c2);
            w.LastActiveJd = jd;
            from.World.Log(jd, $"The {best.Name} leads the {f.Name} war fleet out for {goal.Name}, {route.Count} gate{(route.Count == 1 ? "" : "s")} away by the {route[0].Name}",
                WorldEventKind.Diplomacy, from.Catalog.Root.Index);
        }

        /// <summary>
        /// Gives <paramref name="f"/>'s best idle jump strike group the mission its objectives call for (U.SPC-T474), in this
        /// order: relieve one of its own systems the enemy is attacking; open a second front against a goal the gate fleet is not
        /// marching on (or, with no gate fleet out, jump on the nearest goal itself); else raid the nearest enemy system with a
        /// port it outguns. Each only within <see cref="FleetDoctrine.StrikeRangeLy"/> under its own drive and against what it
        /// outweighs there, the gates' forts left behind.
        /// </summary>
        void PlanStrike(Conflict w, Faction f, double jd, double threat)
        {
            Ship best = null;
            StarSystem from = null;
            double strength = 0;
            foreach (var s in galaxy.Systems)
            {
                var world = s.World;
                foreach (var ship in world.Fleet.Ships)
                {
                    if (!FitStrike(world, ship, f) || !FleetDoctrine.IsStrikeGroup(world, ship) || !FleetDoctrine.LeavesReserve(world, ship, f, threat)) continue;
                    double g = world.War.GroupStrength(ship);
                    if (g > strength)
                    {
                        strength = g;
                        best = ship;
                        from = s;
                    }
                }
            }
            if (best == null) return;
            var enemy = w.Enemy(f);
            double range = FleetDoctrine.StrikeRangeLy.Value;
            bool InReach(StarSystem t) => t != from && t.Origin.DistanceLy(from.Origin) <= range;

            // Relieve: one of its own systems the enemy is attacking.
            StarSystem goal = null;
            var mission = CampaignMission.Relieve;
            double nearest = double.MaxValue;
            foreach (var t in galaxy.Systems)
            {
                if (!InReach(t)) continue;
                double attackers = Attacking(t.World, f, enemy);
                if (attackers <= 0 || strength < LaunchRatio * attackers) continue;
                double ly = t.Origin.DistanceLy(from.Origin);
                if (ly < nearest)
                {
                    nearest = ly;
                    goal = t;
                }
            }

            // A second front: a goal the gate fleet is not marching on (with none out, the nearest goal itself), if it has
            // the marines to take it.
            if (goal == null && GroupMarines(from.World, best) > 0)
            {
                Campaign main = null;
                foreach (var c in w.Campaigns)
                    if (c.Active && c.Faction == f && c.Force == ForceKind.GateFleet) main = c;
                double bestScore = double.MaxValue;
                foreach (var g in GoalsOf(w, f))
                {
                    if (!InReach(g) || strength < LaunchRatio * DefenceOf(enemy, g)) continue;
                    // The gate fleet's own goal only when there is no other.
                    double score = g.Origin.DistanceLy(from.Origin) + (main != null && main.Goal == g ? 1e6 : 0);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        goal = g;
                    }
                }
                if (goal != null) mission = main != null ? CampaignMission.SecondFront : CampaignMission.Invade;
            }

            // A raid: the nearest enemy system with a port it outguns.
            if (goal == null)
            {
                nearest = double.MaxValue;
                foreach (var t in galaxy.Systems)
                {
                    if (!InReach(t) || !EnemyHolds(enemy, t)) continue;
                    if (RaidTarget(t.World, f, enemy, strength, null) == null) continue;
                    double ly = t.Origin.DistanceLy(from.Origin);
                    if (ly < nearest)
                    {
                        nearest = ly;
                        goal = t;
                    }
                }
                mission = CampaignMission.Raid;
            }
            if (goal == null) return;
            LaunchStrike(w, best, from, goal, mission, jd);
        }

        /// <summary>Sends <paramref name="leader"/>'s jump strike group from <paramref name="from"/> on <paramref name="mission"/>
        /// against <paramref name="goal"/> in <paramref name="w"/>, under its own drives. Null if it cannot go.</summary>
        Campaign LaunchStrike(Conflict w, Ship leader, StarSystem from, StarSystem goal, CampaignMission mission, double jd)
        {
            var f = leader.Faction;
            var c = new Campaign
            {
                War = w, Faction = f, Home = from, Goal = goal, Stage = CampaignStage.Marching, SetOutJd = jd, Flagship = leader.Name,
                Force = ForceKind.JumpStrikeGroup, Mission = mission,
            };
            if (Traffic.SendStrike(leader, from, goal, c, jd) == null) return null;
            w.Campaigns.Add(c);
            campaigns.Add(c);
            w.LastActiveJd = jd;
            string why = mission switch
            {
                CampaignMission.Relieve => "to relieve",
                CampaignMission.Raid => "to raid",
                CampaignMission.SecondFront => "to open a second front at",
                _ => "against",
            };
            from.World.Log(jd, $"The {leader.Name} leads a {f.Name} jump strike group out under its own drives {why} {goal.Name}, " +
                               $"{goal.Origin.DistanceLy(from.Origin).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} ly away",
                WorldEventKind.Diplomacy, from.Catalog.Root.Index);
            return c;
        }

        /// <summary>The strength of <paramref name="enemy"/>'s warships loose in <paramref name="world"/>, where <paramref name="f"/>
        /// holds a port (at least a token when only its marines are down on one). 0 when nobody is attacking it there.</summary>
        double Attacking(World world, Faction f, Faction enemy)
        {
            bool holds = false;
            foreach (var s in world.Economy.Settlements)
                if (s.Owner == f && !s.Fallen && s.Kind != SettlementKind.Haven) holds = true;
            if (!holds) return 0;
            double total = 0;
            foreach (var s in world.Fleet.Ships)
                if (EnemyWarship(s, enemy)) total += s.Combat.Strength;
            if (total > 0) return total;
            foreach (var l in landings)
                if (l.By == enemy && l.Troops > 0 && l.Target.Owner == f && Polities.WorldOf(l.Target) == world) return 1;
            return 0;
        }

        /// <summary>Whether <paramref name="s"/> is a warship of <paramref name="enemy"/>'s that fights: not a picket, not a hulk.</summary>
        static bool EnemyWarship(Ship s, Faction enemy) =>
            !s.Destroyed && !s.Derelict && s.Faction == enemy && s.Spec.IsMilitary && s.PicketGate == null && s.Combat != null;

        /// <summary>What <paramref name="enemy"/> holds <paramref name="goal"/> with: its platforms and warships there (a strike
        /// group jumping in meets no gate forts).</summary>
        static double DefenceOf(Faction enemy, StarSystem goal)
        {
            double there = 0;
            foreach (var st in goal.World.Economy.Settlements)
                if (st.Owner == enemy)
                    foreach (var p in st.Platforms) there += p.Strength;
            foreach (var s in goal.World.Fleet.Ships)
                if (!s.Destroyed && s.Faction == enemy && s.Spec.IsMilitary) there += s.Combat.Strength;
            return there;
        }

        /// <summary>The port of <paramref name="enemy"/>'s in <paramref name="world"/> a raid of <paramref name="strength"/> falls on
        /// next: the most worth breaking (people, a yard) for what defends it, of those it outguns whose defences still stand
        /// (or that still have people to frighten off), none of <paramref name="done"/>. Null when there is none.</summary>
        static Settlement RaidTarget(World world, Faction f, Faction enemy, double strength, List<Settlement> done)
        {
            Settlement best = null;
            double bestValue = 0;
            foreach (var s in world.Economy.Settlements)
            {
                if (s.Owner != enemy || s.Fallen || s.Kind == SettlementKind.Haven || (done != null && done.Contains(s))) continue;
                double defence = world.War.DefenceAt(s.Body, f);
                if (strength < defence * 1.25) continue;
                double value = (s.Population / 1e5 + 2) * (s.HasShipyard ? 2 : 1) / (defence + 200);
                if (value > bestValue)
                {
                    bestValue = value;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>
        /// Sends <paramref name="leader"/>'s group (idle in <paramref name="from"/>) on a campaign to take <paramref name="goal"/>,
        /// on its polity's orders (U.SPC-T423, see <see cref="StrategicCommand"/>): the war it is fighting against whoever holds
        /// ports there. Null if it is at war with nobody there or there is no way to fight through.
        /// </summary>
        public Campaign Launch(Ship leader, StarSystem from, StarSystem goal, double jd)
        {
            var f = leader?.Faction;
            if (f == null || from == null || goal == null || from == goal) return null;
            Conflict w = null;
            foreach (var c in wars)
                if (c.Involves(f) && EnemyHolds(c.Enemy(f), goal))
                {
                    w = c;
                    break;
                }
            // A defender's campaigns take back only what was taken from it; anything else is the group's own work there.
            if (w == null || (f == w.Defender && !GoalsOf(w, f).Contains(goal))) return null;
            // A jump strike group jumps in on the flank under its own drives (U.SPC-T474), a second front if a gate fleet of its
            // side is already out.
            if (FleetDoctrine.StrikeGroups.Value && InterstellarTraffic.CanCruise(leader) && FleetDoctrine.IsStrikeGroup(from.World, leader))
            {
                bool marching = false;
                foreach (var c in w.Campaigns)
                    if (c.Active && c.Faction == f && c.Force == ForceKind.GateFleet) marching = true;
                var strike = LaunchStrike(w, leader, from, goal, marching ? CampaignMission.SecondFront : CampaignMission.Invade, jd);
                if (strike != null && f == w.Aggressor && !w.Goals.Contains(goal)) w.Goals.Add(goal);
                return strike;
            }
            var route = galaxy.Network.Route(from, goal, f, jd, force: true);
            if (route == null || route.Count == 0) return null;
            var campaign = new Campaign { War = w, Faction = f, Home = from, Goal = goal, Stage = CampaignStage.Marching, SetOutJd = jd, Flagship = leader.Name };
            if (Traffic.SendCampaign(leader, from, goal, campaign, jd) == null) return null;
            if (f == w.Aggressor && !w.Goals.Contains(goal)) w.Goals.Add(goal);
            w.Campaigns.Add(campaign);
            campaigns.Add(campaign);
            w.LastActiveJd = jd;
            from.World.Log(jd, $"The {leader.Name} leads a {f.Name} war fleet out for {goal.Name} on its command's orders, {route.Count} gate{(route.Count == 1 ? "" : "s")} away",
                WorldEventKind.Diplomacy, from.Catalog.Root.Index);
            return campaign;
        }

        /// <summary>A group <paramref name="f"/> can send: a sound, armed flagship of its navy idle in port, not its home guard,
        /// with marines to land.</summary>
        static bool Fit(World world, Ship ship, Faction f) => FitStrike(world, ship, f) && GroupMarines(world, ship) > 0;

        /// <summary>A group <paramref name="f"/> can send on a raid or a relief (U.SPC-T474): as <see cref="Fit"/>, marines or none.</summary>
        static bool FitStrike(World world, Ship ship, Faction f)
        {
            // Nor a patrol wing: it keeps to its beat at home (U.SPC-T371).
            if (ship.Destroyed || ship.IsEscort || ship.Faction != f || !ship.Spec.IsMilitary || ship.Derelict || ship.Battle != null || ship.PatrolWing) return false;
            // Nor one dry in port waiting on a tanker (InterstellarTraffic.Idle refuses to send it): it would be picked
            // every day and every day fail to sail, holding back every other group.
            if (ship == world.PilotedShip || ship.Voyage != null || ship.GateKm != null || ship.Phase != ShipPhase.Parked || ship.OutOfPropellant) return false;
            if (world.Holdings != null && world.Holdings.Owns(ship)) return false;
            // Nor one its polity's command has given an objective of its own (U.SPC-T423).
            if (world.Polities.Command.Holds(ship)) return false;
            return ship.Combat.HullDamage <= 0.2 && ship.Combat.Armed && !world.War.IsHomeGuard(ship);
        }

        static double GroupMarines(World world, Ship leader)
        {
            double m = leader.Combat.MarinesAvailable;
            foreach (var s in world.Fleet.Ships)
                if (s.Leader == leader && !s.Destroyed) m += s.Combat.MarinesAvailable;
            return m;
        }

        /// <summary>
        /// The hardest fight a campaign of <paramref name="f"/> expects on the way to and in <paramref name="goal"/>, by
        /// strength: the forts of any one enemy gate on <paramref name="route"/>, or the platforms and warships the enemy
        /// has in the goal system. They are met one after another, so it is the biggest of them that the fleet must outweigh.
        /// </summary>
        double Expected(Faction f, Faction enemy, StarSystem goal, List<JumpGate> route)
        {
            double worst = 0;
            foreach (var g in route)
                foreach (var end in new[] { g, g.Partner })
                    if (end.HostileTo(f))
                    {
                        double forts = 0;
                        foreach (var fort in end.Forts) forts += fort.Strength;
                        worst = Math.Max(worst, forts);
                    }
            double there = 0;
            foreach (var st in goal.World.Economy.Settlements)
                if (st.Owner == enemy)
                    foreach (var p in st.Platforms) there += p.Strength;
            foreach (var s in goal.World.Fleet.Ships)
                if (!s.Destroyed && s.Faction == enemy && s.Spec.IsMilitary) there += s.Combat.Strength;
            return Math.Max(worst, there);
        }

        /// <summary>Where a campaign's flagship is: its system and hull, or (null, null) inside a gate or gone.</summary>
        (StarSystem system, Ship ship) Locate(Campaign c)
        {
            foreach (var s in galaxy.Systems)
                foreach (var ship in s.World.Fleet.Ships)
                    // Still its own side's and under way: a flagship taken (no heir to pass the voyage to) or left a hulk
                    // carries the voyage on, but it is no longer the campaign.
                    if (!ship.Destroyed && !ship.IsEscort && !ship.Derelict && ship.Faction == c.Faction && ship.Voyage != null && ship.Voyage.Campaign == c)
                        return (s, ship);
            return (null, null);
        }

        bool InGate(Campaign c)
        {
            foreach (var p in Traffic.InGates)
                if (p.Leader.Voyage?.Campaign == c) return true;
            return false;
        }

        /// <summary>Whether a campaign's group is out between the stars under its own drives (a jump strike group, U.SPC-T474).</summary>
        bool InCruise(Campaign c)
        {
            foreach (var cr in galaxy.DeepSpace.Cruises)
                if (cr.Ships.Count > 0 && !cr.Leader.Destroyed && cr.Leader.Voyage?.Campaign == c) return true;
            return false;
        }

        /// <summary>A day of a campaign: at an enemy gate, fight it or hold it; in the goal system, reduce and take each enemy settlement.</summary>
        void Step(Campaign c, double jd)
        {
            var (system, ship) = Locate(c);
            if (ship == null)
            {
                if (InGate(c) || InCruise(c))
                {
                    Logistics.InGate(c, jd);
                    return;
                }
                // Gone from every system and no gate holds it: home (its voyage over), or lost with every hull.
                if (c.Stage != CampaignStage.Returning)
                {
                    c.Lost = true;
                    var where = c.Goal.World;
                    where.Log(jd, $"The {c.Faction.Name} war fleet sent against {c.Goal.Name} is lost", WorldEventKind.Loss, where.Catalog.Root.Index);
                }
                Finish(c, jd);
                return;
            }
            c.Flagship = ship.Name;
            var v = ship.Voyage;
            var world = system.World;
            Logistics.Day(c, system, ship, jd);
            if (ship.Battle != null) return;
            if (c.Stage == CampaignStage.Returning)
            {
                // Called home but not yet on the way (no way through when it was called): try again.
                if (v.Destination != v.Home && v.HoldGate == null && !v.AtGate) Traffic.Redirect(system, ship, v.Home, jd);
            }
            else if (GroupDamage(world, ship) > 0.4 || ship.Combat.HullDamage > 0.45 || !ship.Combat.Armed)
            {
                Recall(c, system, ship, jd, "battered");
                return;
            }
            else if (c.Supply.MustWithdraw)
            {
                // Its stores run down and nothing on its way to it: it cannot carry the campaign on (U.SPC-T93).
                Recall(c, system, ship, jd, c.Supply.OutOfSupply ? "out of supply" : "its stores spent");
                return;
            }
            else if (v.Destination != c.Goal && v.HoldGate == null && !v.AtGate)
            {
                // Its way on was shut for good and it gave up where it was (U.SPC-T296): it goes home.
                Recall(c, system, ship, jd, "its way shut");
                return;
            }

            // At an enemy gate: one it came out of, or one it has to go through.
            var gate = v.HoldGate ?? (v.AtGate && v.NextGate != null && v.NextGate.Home == system ? v.NextGate : null);
            if (gate != null)
            {
                Siege(c, system, ship, gate, jd);
                return;
            }
            if (c.Stage == CampaignStage.Returning || system != c.Goal || ship.GateKm != null) return;

            // A jump strike group on a relief or a raid (U.SPC-T474).
            if (c.Mission == CampaignMission.Relieve || c.Mission == CampaignMission.Raid)
            {
                StrikeStep(c, system, ship, jd);
                return;
            }

            // In the goal system.
            var target = Objective(c, system);
            if (target == null)
            {
                Won(c, system, ship, jd);
                return;
            }
            c.Stage = CampaignStage.Invading;
            if (c.Objective != target)
            {
                c.Objective = target;
                world.Log(jd, $"The {c.Faction.Name} war fleet under the {ship.Name} moves on {target.Name} ({world.Catalog[target.Body].Name})", WorldEventKind.Battle, target.Body);
            }
            if (!Fleet.IsAt(ship, target.Body))
            {
                // Idling somewhere else: on to the objective now rather than at the end of its stay.
                if (ship.Phase == ShipPhase.Parked && ship.DestinationBody != target.Body) world.Fleet.ScheduleNext(ship, jd);
                return;
            }
            if (world.War.BattleAt(target.Body) != null) return;
            // The platforms must be down first; a defending squadron the fleet far outweighs cannot stop the landing, though
            // the fleet still fights it off.
            double defence = world.War.DefenceAt(target.Body, c.Faction);
            if (target.Platforms.Count > 0 || defence > LandingCover * world.War.GroupStrength(ship))
            {
                world.War.TryStartBattle(target.Body, c.Faction, jd);
                return;
            }
            Land(c, world, ship, target, jd);
            if (defence > 0) world.War.TryStartBattle(target.Body, c.Faction, jd);
        }

        /// <summary>
        /// A day of a jump strike group's relief or raid in the system it was sent to (U.SPC-T474). A relief makes for the
        /// port of its own the enemy is at (else wherever the enemy's warships loose in the system are) and fights them off,
        /// and at a port of its own with the orbit clear puts its marines down to stiffen the garrison; with nobody left
        /// attacking, it goes home. A raid falls on the enemy's ports it outguns one after another, breaking their defences,
        /// and jumps out for home when it has none left. Either jumps home when its time is up
        /// (<see cref="FleetDoctrine.StrikeDays"/>), before the enemy can gather.
        /// </summary>
        void StrikeStep(Campaign c, StarSystem system, Ship ship, double jd)
        {
            var world = system.World;
            c.Stage = CampaignStage.Invading;
            bool relief = c.Mission == CampaignMission.Relieve;
            string done = relief ? "the system relieved" : $"its raid done ({c.PortsRaided} port{(c.PortsRaided == 1 ? "" : "s")} struck)";
            // Its time runs from when it first reaches its work, not from the edge of the well.
            if (!double.IsNaN(c.ArrivedJd) && jd - c.ArrivedJd > FleetDoctrine.StrikeDays.Value)
            {
                Recall(c, system, ship, jd, done);
                return;
            }
            var (body, port) = StrikeObjective(c, system, ship, jd);
            if (body < 0)
            {
                Recall(c, system, ship, jd, done);
                return;
            }
            if (port != null && c.Objective != port)
            {
                c.Objective = port;
                world.Log(jd, relief
                        ? $"The {c.Faction.Name} strike group under the {ship.Name} jumps in to relieve {port.Name} ({world.Catalog[body].Name})"
                        : $"The {c.Faction.Name} strike group under the {ship.Name} falls on {port.Name} ({world.Catalog[body].Name})",
                    WorldEventKind.Battle, body);
            }
            if (!Fleet.IsAt(ship, body))
            {
                if (ship.Phase == ShipPhase.Parked && ship.DestinationBody != body) world.Fleet.ScheduleNext(ship, jd);
                return;
            }
            if (double.IsNaN(c.ArrivedJd)) c.ArrivedJd = jd;
            if (world.War.BattleAt(body) != null) return;
            if (relief)
            {
                if (world.War.EnemiesAt(body, c.Faction))
                {
                    world.War.TryStartBattle(body, c.Faction, jd);
                    return;
                }
                if (port == null || c.Raided.Contains(port)) return;
                // The orbit clear over its own port: its marines go down to the garrison, to fight beside it.
                double marines = 0;
                foreach (var s in new List<Ship>(world.Fleet.Ships))
                    if (!s.Destroyed && (s == ship || s.Leader == ship) && s.Battle == null)
                        marines += s.Combat.Interior.TakeMarines(s.Combat.MarinesAvailable);
                if (marines >= 1)
                {
                    port.Garrison = GarrisonOf(port) + marines;
                    c.Relieved++;
                    world.Log(jd, $"{marines:0} {c.Faction.Name} marines from the {ship.Name} go down to stiffen {port.Name}'s garrison", WorldEventKind.Battle, body);
                }
                c.Raided.Add(port);
                return;
            }
            if (world.War.DefenceAt(body, c.Faction) > 0)
            {
                world.War.TryStartBattle(body, c.Faction, jd);
                return;
            }
            // Its defences broken: on to the next.
            c.Raided.Add(port);
            c.PortsRaided++;
            world.Log(jd, $"The {c.Faction.Name} strike group under the {ship.Name} leaves {port.Name}'s defences broken", WorldEventKind.Battle, body);
        }

        /// <summary>
        /// Where a strike group on a relief or a raid makes for next in <paramref name="system"/>: the body, and the port there
        /// (null when it is going after the enemy's warships away from any port of its own); -1 when it has nothing left to do.
        /// </summary>
        (int Body, Settlement Port) StrikeObjective(Campaign c, StarSystem system, Ship ship, double jd)
        {
            var world = system.World;
            var enemy = c.War.Enemy(c.Faction);
            if (c.Mission == CampaignMission.Raid)
            {
                var mark = RaidTarget(world, c.Faction, enemy, world.War.GroupStrength(ship), c.Raided);
                return mark != null ? (mark.Body, mark) : (-1, null);
            }
            // A relief: its own port with the enemy at it; else one with the enemy's marines down on it; else, while the
            // enemy is loose in the system, the nearest of its ports it has not yet stiffened; else the enemy's warships
            // wherever they are.
            bool loose = false;
            foreach (var s in world.Fleet.Ships)
                if (EnemyWarship(s, enemy)) loose = true;
            Settlement landed = null, unseen = null;
            double unseenDays = double.MaxValue;
            int here = ship.Orbit?.Body ?? -1;
            foreach (var s in world.Economy.Settlements)
            {
                if (s.Owner != c.Faction || s.Fallen || s.Kind == SettlementKind.Haven) continue;
                if (world.War.EnemiesAt(s.Body, c.Faction)) return (s.Body, s);
                if (c.Raided.Contains(s)) continue;
                foreach (var l in landings)
                    if (l.Target == s && l.By == enemy && l.Troops > 0) landed ??= s;
                if (!loose || here < 0) continue;
                double days = s.Body == here ? 0 : world.Fleet.EstimateTravelDays(ship, here, s.Body, jd);
                if (days < unseenDays)
                {
                    unseenDays = days;
                    unseen = s;
                }
            }
            if (landed != null) return (landed.Body, landed);
            if (unseen != null) return (unseen.Body, unseen);
            foreach (var s in world.Fleet.Ships)
            {
                if (!EnemyWarship(s, enemy)) continue;
                var e = s.Controller;
                int at = e.Phase == ShipPhase.Parked && e.Orbit != null ? e.Orbit.Body : e.DestinationBody;
                if (at < 0) continue;
                Settlement own = null;
                foreach (var st in world.Economy.At(at))
                    if (st.Owner == c.Faction && !st.Fallen && st.Kind != SettlementKind.Haven) own = st;
                return (at, own);
            }
            return (-1, null);
        }

        static double GroupDamage(World world, Ship leader)
        {
            double sum = leader.Combat.HullDamage;
            int n = 1;
            foreach (var s in world.Fleet.Ships)
                if (s.Leader == leader && !s.Destroyed)
                {
                    sum += s.Combat.HullDamage;
                    n++;
                }
            return sum / n;
        }

        /// <summary>A war fleet beside an enemy gate: fights its forts (and whatever warships hold it), then holds it until it is taken.</summary>
        void Siege(Campaign c, StarSystem system, Ship ship, JumpGate gate, double jd)
        {
            var v = ship.Voyage;
            if (!gate.HostileTo(c.Faction))
            {
                // Taken (by this fleet or another), or the war is over: on its way.
                if (v.HoldGate != null) Traffic.Release(system, ship, jd);
                return;
            }
            if (gate.Forts.Count > 0 || DefendersAt(system.World, gate, c.Faction))
            {
                if (system.World.War.StartGateBattle(gate, c.Faction, jd) != null) c.GateBattles++;
                c.Besieging = gate;
                c.BesiegedDays = 0;
                return;
            }
            if (c.Stage == CampaignStage.Returning)
            {
                // Its guns silenced: a fleet going home slips through without stopping to take it.
                if (v.HoldGate != null) Traffic.Release(system, ship, jd);
                return;
            }
            if (c.Besieging != gate)
            {
                c.Besieging = gate;
                c.BesiegedDays = 0;
            }
            c.BesiegedDays += 1;
            // Boarding shuttles in the group put marines aboard the gate and take it sooner (U.SPC-T486); a gate breaker's
            // boarding and engineering teams storm it the first day and take it intact (U.SPC-T478).
            var group = new List<Ship>(Group(system.World, ship));
            bool shuttles = BoardingShuttles.CanSeizeGate(group);
            var breaker = GateBreakers.Stormer(group);
            double hold = BoardingShuttles.GateHoldDays(GateHoldDays, shuttles);
            if (breaker != null) hold = GateBreakers.GateHoldDays(hold);
            if (c.BesiegedDays < hold) return;
            SeizeGate(c, gate, jd, shuttles, breaker);
            if (v.HoldGate != null) Traffic.Release(system, ship, jd);
        }

        /// <summary>Enemy warships holding beside <paramref name="gate"/>.</summary>
        static bool DefendersAt(World world, JumpGate gate, Faction f)
        {
            int root = world.Catalog.Root.Index;
            foreach (var s in world.Fleet.Ships)
            {
                if (s.Destroyed || !s.Spec.IsMilitary || s.Faction.IsPirate || !world.Diplomacy.Hostile(f, s.Faction)) continue;
                var o = s.Controller.Orbit;
                if (Fleet.IsAt(s.Controller, root) && o != null && (o.OffsetAt(world.Jd) - gate.PositionKm).Length <= War.GateReachKm) return true;
            }
            return false;
        }

        /// <summary>A war fleet's group: its leader and the ships keeping station on it.</summary>
        static IEnumerable<Ship> Group(World world, Ship leader)
        {
            yield return leader;
            foreach (var s in world.Fleet.Ships)
                if (s.Leader == leader && !s.Destroyed) yield return s;
        }

        void TakeGate(Campaign c, JumpGate gate, double jd) => SeizeGate(c, gate, jd, false);

        /// <summary>Takes <paramref name="gate"/> for the campaign; <paramref name="boarded"/> when its boarding shuttles put the
        /// marines aboard (U.SPC-T486); <paramref name="breaker"/>, the gate breaker whose boarding and engineering teams stormed
        /// it, its engineers making good the damage the ring has taken (U.SPC-T478).</summary>
        void SeizeGate(Campaign c, JumpGate gate, double jd, bool boarded, Ship breaker = null)
        {
            double patched = breaker != null ? GateBreakers.MakeGood(gate) : 0;
            var old = gate.Owner;
            if (takenGates.TryGetValue(gate, out var taken) && taken.War == c.War)
            {
                // Back to how this war found it: nothing left for its own peace to give back.
                if (taken.Before == c.Faction) takenGates.Remove(gate);
            }
            else
                // Not recorded for this war: either its first time taking the gate, or a different, still-unsettled
                // war's claim on it that this capture supersedes (U.SPC-T323) -- either way, this war now owns its fate.
                takenGates[gate] = (c.War, old);
            gate.Owner = c.Faction;
            gate.BlockadeUntilJd = double.NegativeInfinity;
            c.GatesTaken++;
            c.Besieging = null;
            c.BesiegedDays = 0;
            var world = gate.Home.World;
            string how = breaker != null
                ? $"its forts silenced, the {breaker.Name}'s boarding and engineering teams storm it and take it intact" +
                  (patched > 0 ? $", patching its ring to {gate.Integrity * 100:0}% whole" : "")
                : boarded ? "its forts silenced, their boarding shuttles put marines aboard and take it intact" : "its forts silenced, the gate is theirs";
            world.Log(jd, $"The {c.Faction.Name} take the {gate.Name} at {gate.Home.Name} from the {old?.Name ?? "nobody"}: {how}", WorldEventKind.Capture,
                world.Catalog.Root.Index);
        }

        /// <summary>
        /// A war fleet has come out beside <paramref name="gate"/> in <paramref name="system"/> (U.SPC-T92): if the gate is held
        /// against it, it holds there to fight the forts and take the gate before going on (true); else it goes on (false).
        /// </summary>
        public bool OnEmerged(StarSystem system, Ship leader, JumpGate gate, double jd)
        {
            var v = leader.Voyage;
            var c = v?.Campaign;
            if (c == null || !c.Active || !gate.HostileTo(c.Faction)) return false;
            if (c.Stage == CampaignStage.Returning && gate.Forts.Count == 0) return false;
            v.HoldGate = gate;
            leader.GateKm = null;
            system.World.Fleet.ScheduleNext(leader, jd);
            if (system.World.War.StartGateBattle(gate, c.Faction, jd) != null) c.GateBattles++;
            c.Besieging = gate;
            c.BesiegedDays = 0;
            return true;
        }

        /// <summary>Whether a war fleet on <paramref name="v"/> must take <paramref name="gate"/> (in the system it is in) before
        /// going through: it is the enemy's, and armed, or the fleet is advancing and secures its way as it goes.</summary>
        public bool MustTake(JumpGate gate, Voyage v)
        {
            var c = v?.Campaign;
            return c != null && c.Active && gate.HostileTo(c.Faction) && (gate.Forts.Count > 0 || c.Stage != CampaignStage.Returning);
        }

        /// <summary>
        /// Where a war fleet on campaign goes next in <paramref name="system"/> (the voyage hook, see
        /// <see cref="World.VoyageOrders"/>): it stays beside a gate it is taking, and in its goal system makes for the
        /// settlement it is reducing. -1 leaves it to its own navy's orders.
        /// </summary>
        public int Orders(StarSystem system, Ship ship, int here, double jd)
        {
            var v = ship.Voyage;
            var c = v?.Campaign;
            if (c == null || !c.Active) return -1;
            if (v.HoldGate != null) return here;
            if (c.Stage == CampaignStage.Returning || system != c.Goal) return -1;
            if (c.Mission == CampaignMission.Relieve || c.Mission == CampaignMission.Raid)
            {
                var (mark, _) = StrikeObjective(c, system, ship, jd);
                return mark >= 0 ? mark : here;
            }
            var target = Objective(c, system);
            return target != null ? target.Body : here;
        }

        /// <summary>The enemy settlement a campaign reduces next in its goal system: the most populous it has not yet taken.</summary>
        static Settlement Objective(Campaign c, StarSystem system)
        {
            var enemy = c.War.Enemy(c.Faction);
            Settlement best = null;
            foreach (var s in system.World.Economy.Settlements)
            {
                if (s.Owner != enemy || s.Fallen || s.Kind == SettlementKind.Haven) continue;
                // A defender takes back only what was taken from it.
                if (c.Faction == c.War.Defender && s.OccupiedFrom != c.Faction) continue;
                if (best == null || s.Population > best.Population) best = s;
            }
            return best;
        }

        /// <summary>Its goal taken: on to the next, or it stays to hold the system until the peace.</summary>
        void Won(Campaign c, StarSystem system, Ship ship, double jd)
        {
            if (c.Stage != CampaignStage.Holding)
                system.World.Log(jd, $"The {c.Faction.Name} hold {system.Name}: every {c.War.Enemy(c.Faction).Name} settlement there taken", WorldEventKind.Capture,
                    system.Catalog.Root.Index);
            c.Objective = null;
            foreach (var g in GoalsOf(c.War, c.Faction))
            {
                if (g == system) continue;
                var r = galaxy.Network.Route(system, g, c.Faction, jd, force: true);
                if (r == null) continue;
                if (Traffic.Redirect(system, ship, g, jd))
                {
                    c.Goal = g;
                    c.Stage = CampaignStage.Marching;
                    return;
                }
            }
            c.Stage = CampaignStage.Holding;
        }

        /// <summary>Calls a campaign home.</summary>
        void Recall(Campaign c, StarSystem system, Ship ship, double jd, string why)
        {
            if (c.Stage == CampaignStage.Returning) return;
            c.Stage = CampaignStage.Returning;
            c.Objective = null;
            c.Besieging = null;
            system.World.Log(jd, $"The {c.Faction.Name} war fleet under the {ship.Name} turns for home, {why}", WorldEventKind.Info, system.Catalog.Root.Index);
            Traffic.Redirect(system, ship, ship.Voyage.Home, jd);
        }

        void Finish(Campaign c, double jd)
        {
            c.Stage = CampaignStage.Over;
            c.EndedJd = jd;
            Logistics.Ended(c, jd);
            landings.RemoveAll(l => l.Campaign == c && l.Troops <= 0);
        }

        // ---------------------------------------------------------------- marines and garrisons

        /// <summary>A settlement's garrison, mustered from its people the first time a war asks.</summary>
        public static double GarrisonOf(Settlement s)
        {
            if (double.IsNaN(s.Garrison)) s.Garrison = FullGarrison(s);
            return s.Garrison;
        }

        /// <summary>What its own people keep under arms: <see cref="GarrisonPerPerson"/> of them, at least <see cref="MinGarrison"/>.</summary>
        public static double FullGarrison(Settlement s) => Math.Max(MinGarrison, s.Population * GarrisonPerPerson);

        /// <summary>The orbit over <paramref name="target"/> is clear: every marine the fleet has goes down.</summary>
        void Land(Campaign c, World world, Ship leader, Settlement target, double jd)
        {
            double marines = 0;
            foreach (var s in new List<Ship>(world.Fleet.Ships))
                if (!s.Destroyed && (s == leader || s.Leader == leader) && s.Battle == null)
                    marines += s.Combat.Interior.TakeMarines(s.Combat.MarinesAvailable);
            Landing landing = null;
            foreach (var l in landings)
                if (l.Target == target && l.By == c.Faction) landing = l;
            if (marines < 1)
            {
                if (landing == null) Recall(c, c.Goal, leader, jd, "its marines spent");
                return;
            }
            double garrison = GarrisonOf(target);
            if (landing == null)
            {
                landing = new Landing { Target = target, By = c.Faction, Campaign = c, SinceJd = jd };
                landings.Add(landing);
                world.Log(jd, $"{marines:0} {c.Faction.Name} marines land on {target.Name} ({world.Catalog[target.Body].Name}) against {garrison:0} of the {target.Owner.Name}'s garrison",
                    WorldEventKind.Battle, target.Body);
            }
            else world.Log(jd, $"{marines:0} more {c.Faction.Name} marines go down to {target.Name}", WorldEventKind.Battle, target.Body);
            landing.Troops += marines;
        }

        /// <summary>A day's fighting on the ground at every settlement marines have landed on.</summary>
        void GroundWar(double jd)
        {
            foreach (var l in new List<Landing>(landings))
            {
                var s = l.Target;
                var world = Polities.WorldOf(s);
                if (world == null || s.Fallen || s.Owner == l.By || !Diplomacy.Hostile(l.By, s.Owner) || l.Troops <= 0)
                {
                    landings.Remove(l);
                    continue;
                }
                double d = GarrisonOf(s), a = l.Troops;
                double killed = Math.Min(d, a * l.By.MarineQuality * GroundRate);
                double lost = Math.Min(a, d * s.Owner.MarineQuality * DugIn * GroundRate);
                s.Garrison = d - killed;
                l.Troops = a - lost;
                if (s.Garrison < 0.5)
                {
                    landings.Remove(l);
                    Occupy(l, world, jd);
                }
                else if (l.Troops < 0.5)
                {
                    landings.Remove(l);
                    world.Log(jd, $"The {l.By.Name} marines on {s.Name} are wiped out: its garrison holds ({s.Garrison:0} left)", WorldEventKind.Battle, s.Body);
                }
            }
        }

        /// <summary>The garrison beaten: the settlement changes hands, the marines its garrison, its people restive.</summary>
        void Occupy(Landing l, World world, double jd)
        {
            var s = l.Target;
            var old = s.Owner;
            world.Economy.Capture(s, l.By);
            s.Garrison = l.Troops;
            if (s.OccupiedFrom == l.By)
            {
                // Taken back: liberated rather than occupied.
                s.OccupiedFrom = null;
                s.Unrest = Math.Max(0.1, s.Unrest * 0.3);
            }
            else
            {
                s.OccupiedFrom ??= old;
                s.Unrest = Math.Max(s.Unrest, OccupationUnrest);
            }
            if (l.Campaign != null) l.Campaign.SettlementsTaken++;
            world.War.RevokeBookings();
            world.Log(jd, $"{s.Name} ({world.Catalog[s.Body].Name}) falls to the {l.By.Name}: the {old.Name} garrison overrun, {s.Garrison:0} marines hold it", WorldEventKind.Capture, s.Body);
        }

        /// <summary>
        /// Garrisons and unrest, everywhere a war has asked about them: a garrison of its own people rebuilds to strength;
        /// unrest wears down, faster under a strong garrison; and while the war is fought, a restive settlement whose
        /// occupier's garrison has grown thin may rise and let its old owner back in.
        /// </summary>
        void Garrisons(double jd)
        {
            foreach (var world in Polities.Worlds)
                foreach (var s in world.Economy.Settlements)
                {
                    if (s.Fallen) continue;
                    double full = FullGarrison(s);
                    if (!double.IsNaN(s.Garrison) && s.OccupiedFrom == null && s.Garrison < full && !UnderAssault(s))
                        s.Garrison = Math.Min(full, s.Garrison + full * 0.02);
                    if (s.Unrest <= 0) continue;
                    double hold = double.IsNaN(s.Garrison) ? 1 : Math.Min(1, s.Garrison / full);
                    s.Unrest = Math.Max(0, s.Unrest - 0.006 * (0.5 + hold));
                    if (s.OccupiedFrom != null && s.Unrest > 0.5 && hold < 0.25 && Between(s.Owner, s.OccupiedFrom) != null && rng.NextDouble() < 0.03)
                    {
                        var occupier = s.Owner;
                        var old = s.OccupiedFrom;
                        world.Economy.Capture(s, old);
                        s.OccupiedFrom = null;
                        s.Garrison = full * 0.3;
                        s.Unrest = 0.3;
                        world.War.RevokeBookings();
                        world.Log(jd, $"{s.Name} rises against the {occupier.Name}: its thin garrison overrun, it declares for the {old.Name} again", WorldEventKind.Capture, s.Body);
                    }
                }
        }

        bool UnderAssault(Settlement s)
        {
            foreach (var l in landings)
                if (l.Target == s) return true;
            return false;
        }

        /// <summary>Gate forts rebuilt by their owners, a fort every few weeks while the treasury can pay, when nobody is fighting there.</summary>
        void RebuildForts(double jd)
        {
            if (galaxy.Network == null) return;
            foreach (var link in galaxy.Network.Links)
                foreach (var g in new[] { link.AtA, link.AtB })
                {
                    if (g.Forts.Count >= g.MaxForts || g.Owner == null || g.Owner.Treasury < War.PlatformPrice) continue;
                    var war = g.Home.World.War;
                    if (war.BattleAt(g.Home.Catalog.Root.Index) != null || rng.NextDouble() > 0.04) continue;
                    g.Owner.Treasury -= War.PlatformPrice;
                    g.Forts.Add(new ShipCombat(CombatSpec.Platform));
                }
        }

        // ---------------------------------------------------------------- peace

        /// <summary>Whether a war is over: its goals taken, the defender's navy gone, too long fought, or stalled.</summary>
        void Judge(Conflict w, double jd)
        {
            var a = w.Aggressor;
            var d = w.Defender;
            if (Diplomacy.Get(a, d) != Relation.War)
            {
                Settle(w, jd, "the two make peace");
                return;
            }
            bool goalsTaken = true;
            foreach (var g in w.Goals)
                if (EnemyHolds(d, g)) goalsTaken = false;
            if (goalsTaken)
            {
                Settle(w, jd, $"the {d.Name}, its systems taken, sues for peace");
                return;
            }
            if (Polities.Navy(d).Count == 0 && Occupied(a, d) > 0)
            {
                Settle(w, jd, $"the {d.Name}, its navy gone, sues for peace");
                return;
            }
            if (Polities.Navy(a).Count == 0)
            {
                Settle(w, jd, $"the {a.Name}, its navy gone, sues for peace");
                return;
            }
            if (jd - w.StartJd > MaxWarDays)
            {
                Settle(w, jd, "both sides war-weary after a year");
                return;
            }
            if (jd - w.LastActiveJd > StalemateDays)
                Settle(w, jd, "neither side able to carry the war to the other");
        }

        /// <summary>Settlements <paramref name="by"/> holds that it took from <paramref name="from"/>.</summary>
        int Occupied(Faction by, Faction from)
        {
            int n = 0;
            foreach (var s in Polities.Settlements)
                if (s.Owner == by && s.OccupiedFrom == from && !s.Fallen) n++;
            return n;
        }

        /// <summary>
        /// Ends a war in a treaty: each system where one side now holds the most populous settlement, having taken it (or
        /// another there) from the other, is ceded to it, with every settlement and gate the other still had there; all else
        /// occupied, and every gate taken elsewhere, goes back; the fleets go home.
        /// </summary>
        void Settle(Conflict w, double jd, string why)
        {
            var a = w.Aggressor;
            var d = w.Defender;
            var t = new Treaty { A = a, B = d, Jd = jd, TruceUntilJd = jd + TruceDays };
            var ceded = new Dictionary<StarSystem, Faction>();
            foreach (var s in galaxy.Systems)
            {
                var holder = Polities.HolderOf(s.World);
                if (!w.Involves(holder)) continue;
                var other = w.Enemy(holder);
                foreach (var st in s.World.Economy.Settlements)
                    if (st.Owner == holder && st.OccupiedFrom == other && !st.Fallen)
                    {
                        ceded[s] = holder;
                        break;
                    }
            }
            int toA = 0, toD = 0;
            foreach (var kv in ceded)
                if (kv.Value == a) toA++;
                else toD++;
            t.Winner = toA > toD ? a : toD > toA ? d : null;

            foreach (var s in galaxy.Systems)
            {
                var world = s.World;
                bool isCeded = ceded.TryGetValue(s, out var to);
                foreach (var st in world.Economy.Settlements)
                {
                    if (st.Fallen) continue;
                    if (isCeded && st.Owner == w.Enemy(to) && st.Kind != SettlementKind.Haven)
                    {
                        // Ceded with its system.
                        world.Economy.Capture(st, to);
                        st.OccupiedFrom = null;
                        st.Garrison = FullGarrison(st) * 0.5;
                        st.Unrest = Math.Max(st.Unrest, 0.3);
                    }
                    else if (isCeded && st.Owner == to && w.Involves(st.OccupiedFrom)) st.OccupiedFrom = null;
                    else if (w.Involves(st.Owner) && st.OccupiedFrom == w.Enemy(st.Owner))
                    {
                        // Given back.
                        world.Economy.Capture(st, st.OccupiedFrom);
                        st.OccupiedFrom = null;
                        st.Garrison = double.NaN;
                        st.Unrest = Math.Min(st.Unrest, 0.1);
                    }
                }
                foreach (var g in s.Gates)
                {
                    if (isCeded && g.Owner == w.Enemy(to))
                    {
                        g.Owner = to;
                        if (takenGates.TryGetValue(g, out var taken) && taken.War == w) takenGates.Remove(g);
                    }
                    else if (takenGates.TryGetValue(g, out var taken) && taken.War == w)
                    {
                        // Only this war's own capture: a different, still-unsettled war's claim on this same gate
                        // (U.SPC-T323) is that war's to answer for when it makes its own peace, not this one's.
                        if (!isCeded) g.Owner = taken.Before;
                        takenGates.Remove(g);
                    }
                }
                if (isCeded) t.Ceded.Add(s.Name);
            }
            landings.RemoveAll(l => w.Involves(l.By));

            var names = new List<StarSystem>();
            foreach (var kv in ceded) names.Add(kv.Key);
            // Named for the loser's seat of government (the defender's in a white peace), as founded: lost in the war, still its name.
            var seat = (t.Loser ?? d).Capital ?? Polities.CapitalOf(t.Loser ?? d);
            string place = seat?.Name ?? (t.Loser ?? d).Name;
            t.Terms = t.Winner == null
                ? $"The Treaty of {place}: {why}; the {a.Name} and the {d.Name} make peace, all occupied ground given back"
                : $"The Treaty of {place}: {why}; the {t.Loser.Name} cede{(names.Count == 1 ? "s" : "")} {Names(names)} to the {t.Winner.Name}";
            Diplomacy.Sign(t);
            w.Treaty = t;
            w.EndedJd = jd;
            wars.Remove(w);
            ended.Add(w);

            foreach (var c in w.Campaigns)
            {
                if (!c.Active || c.Stage == CampaignStage.Returning) continue;
                var (system, ship) = Locate(c);
                if (ship == null)
                {
                    c.Stage = CampaignStage.Returning;
                    continue;
                }
                Recall(c, system, ship, jd, "the war over");
            }
            var told = new HashSet<World>();
            foreach (var world in Involved(w))
                if (told.Add(world))
                {
                    world.War.RevokeBookings();
                    world.Log(jd, t.Terms, WorldEventKind.Diplomacy);
                }
            foreach (var kv in ceded)
                if (told.Add(kv.Key.World)) kv.Key.World.Log(jd, t.Terms, WorldEventKind.Diplomacy);
        }

        // ---------------------------------------------------------------- ambition

        /// <summary>
        /// Now and then a state whose navy far outweighs a neighbour's (one a gate away from a system where it holds
        /// settlements) goes to war with it for that system, when no other war is being fought.
        /// </summary>
        void Ambitions(double jd)
        {
            if (wars.Count > 0 || galaxy.Network == null || rng.NextDouble() > DeclareChancePerMonth) return;
            var options = new List<(Faction f, Faction g, StarSystem goal)>();
            foreach (var f in Polities.Factions)
            {
                if (!Warlike(f) || Polities.AtWar(f) || Polities.Statecraft.OverlordOf(f) != null) continue;
                double mine = NavyStrength(f);
                if (mine <= 0) continue;
                foreach (var home in Polities.SystemsWithHoldings(f))
                {
                    var homeSystem = SystemOf(home);
                    if (homeSystem == null) continue;
                    foreach (var link in galaxy.Network.LinksOf(homeSystem))
                    {
                        var next = link.Other(homeSystem);
                        var g = Polities.HolderOf(next.World);
                        if (g == null || g == f || !Warlike(g) || Polities.AtWar(g) || Diplomacy.InTruce(f, g, jd)) continue;
                        // Bound by a pact, or too friendly to want it (U.SPC-T427).
                        if (Polities.Statecraft.BoundToPeace(f, g) || Polities.Statecraft.Opinion(f, g) > 40) continue;
                        if (mine < DeclareRatio * Math.Max(NavyStrength(g), 1)) continue;
                        options.Add((f, g, next));
                    }
                }
            }
            if (options.Count == 0) return;
            var (by, against, goal) = options[rng.Next(options.Count)];
            Declare(by, against, jd, goal);
        }

        static bool Warlike(Faction f) => f != null && !f.IsPirate && f.IsMilitary && f.Kind == PolityKind.State;

        double NavyStrength(Faction f)
        {
            double total = 0;
            foreach (var s in Polities.Navy(f)) total += s.Combat.Strength;
            return total;
        }

        StarSystem SystemOf(World w)
        {
            foreach (var s in galaxy.Systems)
                if (s.World == w) return s;
            return null;
        }
    }
}
