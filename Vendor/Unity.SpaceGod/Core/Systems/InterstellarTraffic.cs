using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>Why a ship is crossing the gates.</summary>
    public enum VoyagePurpose
    {
        /// <summary>A freighter carrying goods to another system's markets (and a backhaul home).</summary>
        Trade,

        /// <summary>A courier carrying mail and data cores between the settled systems, for a fee.</summary>
        Courier,

        /// <summary>A yacht or survey ship off to look at an unsettled system.</summary>
        Survey,

        /// <summary>A raider group off to prey on another system's traffic.</summary>
        Raid,

        /// <summary>A navy group sent after raiders to the system they came from.</summary>
        Expedition,

        /// <summary>A war fleet on campaign against another power's systems (U.SPC-T92, see <see cref="InterstellarWars"/>).</summary>
        Campaign,

        /// <summary>A pirate clan's smuggler running loot to a black market in another system (U.SPC-T94).</summary>
        Smuggle,

        /// <summary>A raider band moving to its clan's haven in another system, or off to found one (U.SPC-T94).</summary>
        Relocate,

        /// <summary>A bounty hunter group gone after a pirate clan with a price on it in another system (U.SPC-T94).</summary>
        Hunt,

        /// <summary>A raider band or a navy picket gone out under its own drive to lie in wait on an FTL route with a gravity
        /// projector (U.SPC-T421, see <see cref="DeepSpace.LieInWait"/>).</summary>
        Ambush,

        /// <summary>A navy group sent by its polity's command to carry out an objective in another system (U.SPC-T423, see
        /// <see cref="StrategicCommand"/>): there until the objective is done or called off.</summary>
        Directed,

        /// <summary>A hull its captain sent along a plotted course (U.SPC-T516, see <see cref="TravelCourse"/>): the crew flies it
        /// gate by gate (by the hyperlanes across each system where they run, see <see cref="Fleet"/>), through every system on the
        /// way, and its voyage ends on arrival.</summary>
        Plotted,

        /// <summary>A navy's supply convoy (U.SPC-T525, see <see cref="MilitaryLogistics"/>): tankers and ammunition colliers
        /// sailing from a naval base after a war fleet on campaign, through the gates to wherever it is now; they hand over what
        /// they carry when they come up with it, and come home.</summary>
        Supply,

        /// <summary>A colony ship's hull taking its colonists to a site in another system (U.SPC-T524, see <see cref="Colonisation"/>):
        /// there until it has landed them (<see cref="Voyage.TargetBody"/>, the site), then home; home with them still aboard
        /// if the site was taken.</summary>
        Colonise,
    }

    /// <summary>
    /// One ship's (or group's) trip across the gate network: where it belongs, where it is bound,
    /// the gates to take, and what it does when it gets there. Kept on the flagship
    /// (<see cref="Ship.Voyage"/>), the same object all the way (U.SPC-T453).
    /// </summary>
    public sealed class Voyage
    {
        public VoyagePurpose Purpose;

        /// <summary>The system the ship belongs to, and comes back to.</summary>
        public StarSystem Home;

        /// <summary>Where this leg ends: the far system outbound, <see cref="Home"/> on the way back.</summary>
        public StarSystem Destination;

        /// <summary>The gates still to take, in order (see <see cref="GateNetwork.Route"/>), and the next one's index.</summary>
        public List<JumpGate> Route = new List<JumpGate>();
        public int Leg;

        /// <summary>Held at a gate: waiting to go through (it is about to, or the way is shut).</summary>
        public bool AtGate;
        public double AtGateJd;

        /// <summary>When a ship held at a shut gate started waiting; NaN when not waiting.</summary>
        public double WaitingSinceJd = double.NaN;

        /// <summary>Abroad, when it turns for home.</summary>
        public double StayUntilJd;

        /// <summary>When it left home.</summary>
        public double SetOutJd;

        /// <summary>The colours it set out under: a ship taken by another faction on the way is off the voyage.</summary>
        public Faction Faction;

        /// <summary>An expedition's quarry: the raiders it was sent after.</summary>
        public Faction Quarry;

        /// <summary>A war fleet's campaign (U.SPC-T92), or null.</summary>
        public Campaign Campaign;

        /// <summary>The supply convoy a navy's supply ship sails in (U.SPC-T525), while it is still bound for its war fleet;
        /// null once it has handed over its load, been turned back, or for any other voyage.</summary>
        public SupplyConvoy Convoy;

        /// <summary>The enemy gate a war fleet holds beside, having come out of it, until it has taken it (U.SPC-T92); null when none.</summary>
        public JumpGate HoldGate;

        /// <summary>A smuggler's market at the far end (a black market's body there), or a band's haven or haven site; -1 if none.</summary>
        public int TargetBody = -1;

        /// <summary>The name a band off to found a haven will give it (U.SPC-T94).</summary>
        public string SiteName;

        /// <summary>The hull that set out (a bounty hunter's), whose place at home the ship takes up again when it is back.</summary>
        public Ship Origin;

        /// <summary>A hunt voyage that ends with no hull left to come home (U.SPC-T588): the hunter's row stops being kept
        /// for an heir that will never arrive, so it can be pruned. Does nothing for any other voyage.</summary>
        public void LostWithAllHands()
        {
            if (Purpose == VoyagePurpose.Hunt && Origin != null) Home?.World.Outlaws?.SetAway(Origin, false);
        }

        /// <summary>Light-years along the outbound route (what tolls and fees scale with).</summary>
        public double Ly;

        /// <summary>Gate tolls paid so far, credits.</summary>
        public double Tolls;

        /// <summary>What it was doing at home, to take up again when it gets back (body indices of the home catalogue).</summary>
        public ShipRole HomeRole;
        public int[] HomeRoute;
        public int[] HomePorts;

        /// <summary>A trade run's plan, on the news it was planned on (U.SPC-T100): the market it was bound for, what that
        /// market's board said the cargo would fetch there, and how old that board was when the ship set out.</summary>
        public string PlannedMarket;
        public double ExpectedSale, PlannedOnNewsJd = double.NaN;

        /// <summary>
        /// This leg is flown under the ship's own cruise drive through open space (U.SPC-T412, see <see cref="DeepSpace"/>),
        /// not by the gates: its <see cref="Ship.GateKm"/> is the point at the edge of its star's well where it engages.
        /// </summary>
        public bool ByCruise;

        /// <summary>A plain star (one nobody simulates) this leg is bound for under the ship's own drive, by name; null for a system.</summary>
        public string StarName;

        /// <summary>A point in open space this leg is bound for under the ship's own drive (an ambush's station, U.SPC-T421),
        /// and what it is called; null for a star or a system.</summary>
        public GalacticPosition? Station;
        public string StationName;

        /// <summary>The way out from its destination's star (its system's km frame) a jump strike group drops out at, on the
        /// flank away from the gates held against it (U.SPC-T474, <see cref="FleetDoctrine.DropDirection"/>); null to drop
        /// out on the side facing the way it came.</summary>
        public DVec3? DropDir;

        public JumpGate NextGate => Route != null && Leg < Route.Count ? Route[Leg] : null;
        public bool Homeward => Destination == Home;
    }

    /// <summary>
    /// One group's trip through a gate, kept for the galaxy map's trade flows (U.SPC-T103): which link, which way, when,
    /// what for, under whose colours, how many hulls and how many tonnes of cargo.
    /// </summary>
    public sealed class GateCrossing
    {
        public GateLink Link;

        /// <summary>The system it left by this link.</summary>
        public StarSystem From;

        public double Jd;
        public VoyagePurpose Purpose;
        public Faction Faction;
        public int Ships;
        public double Tonnes;
    }

    /// <summary>A ship (and its escorts) inside a gate, between leaving one system and coming out in the next.</summary>
    public sealed class Passage
    {
        /// <summary>Flagship first, then its escorts: the same hulls that will come out at the far end (U.SPC-T453).</summary>
        public readonly List<Ship> Ships = new List<Ship>();
        public JumpGate From, To;
        public double EnterJd, ExitJd;

        /// <summary>Which of <see cref="Ships"/> were hired fighters in the system this passage left, so the
        /// destination's own <see cref="MerchantEscorts"/> can pick their tracking back up on arrival (U.SPC-T301).</summary>
        public readonly HashSet<Ship> Hired = new HashSet<Ship>();

        /// <summary>The news it carries: what the system it left knew when it went in (U.SPC-T100, <see cref="Newswire.Bundle"/>).</summary>
        public List<SystemReport> News;

        public Ship Leader => Ships[0];

        /// <summary>How far through (0 at the near gate, 1 at the far one).</summary>
        public double Progress(double jd) => ExitJd > EnterJd ? Math.Max(0, Math.Min(1, (jd - EnterJd) / (ExitJd - EnterJd))) : 1;
    }

    /// <summary>
    /// The traffic between the star systems. Every system runs its own world and nothing else is
    /// shared, so a ship crosses by flying to one of its system's gates (a burn to the gate's fixed
    /// point, see <see cref="Ship.GateKm"/>), going in (the hull itself leaves its fleet's list, a gone
    /// marker left in its place, as the piloted crossing does; see <see cref="GateTransit"/>, <see cref="Fleet.LetGo"/>),
    /// spending <see cref="GateNetwork.TransitDays"/> in the gate (a <see cref="Passage"/>), and coming out the
    /// same hull in the next system's fleet, at the far gate. A route of several gates crosses each system between them gate to gate.
    ///
    /// Each day it sends out what the settled systems would: freighters carrying a good to another
    /// system's market when the price gap there beats the cost of the trip, gate tolls included
    /// (<see cref="MarkupPerLy"/> of the cargo's cost a light-year, paid to the gates' owners);
    /// couriers with the mail; now and then a raider group bound for another system's lanes and a
    /// yacht off to look at an unsettled star; and, after raiders come through, a navy group sent to
    /// hunt them in the system they came from. Abroad, each does its work for a few weeks (the
    /// freighter trades in the local markets, the raiders raid) and then comes home the quickest open
    /// way. A gate's owner closes it to all but its own ships for <see cref="BlockadeDays"/> after
    /// raiders come through it; traffic then goes round by other gates where it can, or waits.
    /// </summary>
    [SettingsSource]
    public sealed class InterstellarTraffic
    {
        readonly Galaxy galaxy;
        readonly Random rng;
        readonly List<Passage> inGates = new List<Passage>();
        readonly List<(StarSystem system, Ship ship)> atGates = new List<(StarSystem, Ship)>();
        readonly List<(StarSystem from, StarSystem to, Faction quarry, double jd)> expeditionsWanted = new List<(StarSystem, StarSystem, Faction, double)>();
        double nextDailyJd;

        /// <summary>Ships inside a gate right now.</summary>
        public IReadOnlyList<Passage> InGates => inGates;

        /// <summary>Every ship inside a gate or out between the stars under its own drive right now (in no system's fleet
        /// until it comes out at the far end).</summary>
        public IEnumerable<Ship> InTransit()
        {
            foreach (var p in inGates)
                foreach (var s in p.Ships)
                    yield return s;
            foreach (var s in galaxy.DeepSpace.InFlight())
                yield return s;
        }

        /// <summary>The voyages of the groups inside the gates and out between the stars (their flagships').</summary>
        IEnumerable<Voyage> VoyagesInTransit()
        {
            foreach (var p in inGates)
                if (p.Leader.Voyage != null) yield return p.Leader.Voyage;
            foreach (var c in galaxy.DeepSpace.Cruises)
                if (c.Ships.Count > 0 && c.Leader.Voyage != null) yield return c.Leader.Voyage;
        }

        /// <summary>Decisions about the open space between the stars (U.SPC-T412), on their own numbers so the gate traffic's
        /// stay as they were whenever no ship goes that way.</summary>
        readonly Random cruiseRng;

        /// <summary>The bands sent to lie in wait on the routes (U.SPC-T421), on their own numbers too.</summary>
        readonly Random ambushRng;

        /// <summary>Share of survey trips a survey hull with a drive of its own makes to a star off the gate network.</summary>
        public const double StarSurveyShare = 0.3;

        /// <summary>The farthest (ly) a survey service sends a hull under its own drive to look at a plain star.</summary>
        public const double MaxStarSurveyLy = 16;

        /// <summary>What each system knows of the others, and how old it is (U.SPC-T100).</summary>
        public Newswire News { get; }

        /// <summary>Voyages that have reached their far end so far, by purpose (homecomings not counted).</summary>
        public readonly Dictionary<VoyagePurpose, int> Arrivals = new Dictionary<VoyagePurpose, int>();

        /// <summary>The crossings of the last <see cref="CrossingLogDays"/> days, oldest first (U.SPC-T103): what the gates' owners
        /// log of the traffic through them, and the galaxy map draws as trade flows.</summary>
        public IReadOnlyList<GateCrossing> CrossingLog => crossingLog;
        readonly List<GateCrossing> crossingLog = new List<GateCrossing>();

        /// <summary>How long the crossing log is kept, days.</summary>
        public const double CrossingLogDays = 180;

        /// <summary>Logs a crossing (kept in date order): every group that goes into a gate is logged as it goes in.</summary>
        public void LogCrossing(GateCrossing c)
        {
            int i = crossingLog.Count;
            while (i > 0 && crossingLog[i - 1].Jd > c.Jd) i--;
            crossingLog.Insert(i, c);
        }

        /// <summary>Gate crossings so far (a group counts once), and the tolls paid on cargo, credits.</summary>
        public int Crossings { get; private set; }
        public double TollsPaid { get; private set; }

        /// <summary>Gate tolls and freight, as a share of a cargo's cost per light-year crossed.</summary>
        public const double MarkupPerLy = 0.03;

        /// <summary>A freighter goes only for a margin of at least this share of what the cargo cost landed.</summary>
        public const double MinMargin = 0.12;

        /// <summary>The most freighters out on one system's runs to another at once (4 unless the player tunes it, U.SPC-T431).</summary>
        public static int MaxTradersPerRoute => TradersPerRoute.IntValue;

        /// <summary>The settings menu's Economy tuning of <see cref="MaxTradersPerRoute"/>.</summary>
        public static readonly RangeSetting TradersPerRoute = new RangeSetting("economy.tradersPerRoute", SettingGroups.Economy,
            "Freighters per interstellar route", 4, 1, 12, 1,
            "The most of a system's freighters out on its runs to one other system at once: more makes busier lanes and tighter margins.",
            v => v.ToString("0", CultureInfo.InvariantCulture), "trade traffic interstellar freight");

        /// <summary>
        /// The most of a system's own freighters that may be out on interstellar runs at once, as a share of them: the
        /// rest stay to carry its own trade, so a thin frontier system (U.SPC-T90) is not emptied by a dozen routes.
        /// </summary>
        public const double MaxAbroadShare = 0.25;

        /// <summary>What a mail run pays a courier a light-year, credits (the receiving system's government pays).</summary>
        public const double MailFeePerLy = 60000;

        /// <summary>How long a gate stays closed after raiders come through it (days).</summary>
        public const double BlockadeDays = 30;

        /// <summary>A ship held at a shut gate gives up after this long (days) and goes about its business where it is.</summary>
        public const double MaxWaitDays = 8;

        /// <summary>Daily chance a settled system's raiders set out for another system, when none are out.</summary>
        public const double RaidChancePerDay = 0.02;

        /// <summary>Daily chance a yacht or survey ship sets out for an unsettled system.</summary>
        public const double SurveyChancePerDay = 0.03;

        /// <summary>A gate's traffic (<see cref="JumpGate.Traffic"/>) counts the ships through it over about this many days.</summary>
        public const double GateTrafficDays = 30;

        /// <summary>The pirate clans' business across the gates: havens, smuggling, splits and the hunt for them (U.SPC-T94).</summary>
        public PirateClans Clans { get; }

        public InterstellarTraffic(Galaxy galaxy, double jd, int seed = 1601)
        {
            this.galaxy = galaxy;
            rng = new Random(seed);
            cruiseRng = new Random(seed + 13);
            ambushRng = new Random(seed + 29);
            nextDailyJd = jd + 0.5;
            News = new Newswire(galaxy, jd);
            Clans = new PirateClans(galaxy, this, seed + 7);
            foreach (var system in galaxy.Systems) Register(system);
            galaxy.DeepSpace.Arrived += OnCruiseArrived;
            galaxy.DeepSpace.ReachedStar += OnReachedStar;
        }

        /// <summary>Takes <paramref name="s"/>'s gate traffic on: ships there reaching a gate, and the orders of those abroad (a system opened by a new gate, U.SPC-T97, as well as the first ones).</summary>
        internal void Register(StarSystem s)
        {
            s.World.ReachedGate += (ship, when) => OnReachedGate(s, ship, when);
            s.World.VoyageOrders = (ship, here, when) => Orders(s, ship, here, when);
            Clans.Watch(s);
        }

        internal GateNetwork Network => galaxy.Network;

        /// <summary>Gate arrivals, emergences and blocked ships to <paramref name="jd"/>; each day, new voyages.</summary>
        public void Update(double jd)
        {
            // Ships that have reached a gate go in (or wait, or turn for another), in date order.
            atGates.Sort((a, b) => (a.ship.Voyage?.AtGateJd ?? 0).CompareTo(b.ship.Voyage?.AtGateJd ?? 0));
            foreach (var (system, ship) in new List<(StarSystem, Ship)>(atGates))
            {
                var v = ship.Voyage;
                // The first try is when it got there; a ship already waiting tries again now.
                double when = v != null && double.IsNaN(v.WaitingSinceJd) ? Math.Min(v.AtGateJd, jd) : jd;
                TryEnter(system, ship, when, jd);
            }

            // In the order they come out, not the order they went in: each draws on shared random numbers, so two
            // due in one step must take them as they would on separate frames (U.SPC-T384).
            List<Passage> due = null;
            for (int i = inGates.Count - 1; i >= 0; i--)
                if (inGates[i].ExitJd <= jd)
                {
                    (due ??= new List<Passage>()).Add(inGates[i]);
                    inGates.RemoveAt(i);
                }
            if (due != null)
            {
                due.Sort((a, b) => a.ExitJd.CompareTo(b.ExitJd));
                foreach (var p in due) Emerge(p);
            }

            // The news: relays sent and reports arrived, before anyone plans on it.
            News.Update(jd);

            while (jd >= nextDailyJd)
            {
                Daily(nextDailyJd);
                nextDailyJd += 1;
            }
        }

        // ---------------------------------------------------------------- at the gate

        void OnReachedGate(StarSystem system, Ship ship, double jd)
        {
            var v = ship.Voyage;
            if (v == null)
            {
                // Bound for a gate with nowhere to go: back to work.
                ship.GateKm = null;
                return;
            }
            v.AtGate = true;
            v.AtGateJd = jd;
            atGates.Add((system, ship));
        }

        /// <summary>Takes a held ship through its next gate if it opens for it; else round by another, or waits.</summary>
        void TryEnter(StarSystem system, Ship ship, double when, double now)
        {
            var v = ship.Voyage;
            // Fighting at the gate: it goes through (or not) once the fight is over.
            if (!ship.Destroyed && ship.Battle != null) return;
            // A hulk cast adrift there is going nowhere.
            if (ship.Destroyed || v == null || ship == system.World.PilotedShip || ship.Faction != v.Faction || ship.Derelict)
            {
                atGates.RemoveAll(e => e.ship == ship);
                if (!ship.Destroyed) CancelVoyage(system, ship, now, v != null && ship.Faction == v.Faction);
                return;
            }
            // At the edge of the well, bound across open space under its own drive (U.SPC-T412): no gate to open.
            if (v.ByCruise)
            {
                atGates.RemoveAll(e => e.ship == ship);
                EngageCruise(system, ship, when);
                return;
            }
            var gate = v.NextGate;
            bool here = gate != null && gate.Home == system && gate.InRange(ship.Orbit.OffsetAt(when));
            bool campaign = v.Purpose == VoyagePurpose.Campaign && v.Campaign != null;
            if (campaign && gate != null && gate.Home == system && here)
            {
                // A war fleet at an enemy's gate takes it before it goes through (U.SPC-T92): it holds here while the
                // campaign fights its forts and holds the gate (see InterstellarWars), however long that takes.
                if (galaxy.Wars != null && galaxy.Wars.MustTake(gate, v))
                {
                    if (double.IsNaN(v.WaitingSinceJd)) v.WaitingSinceJd = now;
                    return;
                }
                // The far end held against it is forced: it goes through, to fight there.
                // (Through a ring that works: a sabotaged or fought-down one is shut to a war fleet too, U.SPC-T383.)
                if (gate.Link.Working && GateNetwork.Passable(gate, ship.Faction, when))
                {
                    atGates.RemoveAll(e => e.ship == ship);
                    Enter(system, ship, gate, when);
                    return;
                }
            }
            if (gate == null || gate.Home != system || !gate.Link.Open(system, ship.Faction, when) || !here)
            {
                var route = Network.Route(system, v.Destination, ship.Faction, now, campaign);
                if (route != null && route.Count > 0 && route[0] != gate)
                {
                    // Round by another gate.
                    atGates.RemoveAll(e => e.ship == ship);
                    v.Route = route;
                    v.Leg = 0;
                    v.AtGate = false;
                    v.WaitingSinceJd = double.NaN;
                    ship.GateKm = route[0].PositionKm;
                    system.World.Fleet.ScheduleNext(ship, now);
                    if (gate != null)
                        system.World.Log(now, $"{ship.Name} turns for the {route[0].Name}: the {gate.Name} is shut to it", WorldEventKind.Info, system.Catalog.Root.Index);
                    return;
                }
                if (route != null && route.Count > 0 && !here)
                {
                    // The right gate, but not at it (it was knocked off course): fly the last of the way.
                    atGates.RemoveAll(e => e.ship == ship);
                    v.AtGate = false;
                    ship.GateKm = route[0].PositionKm;
                    system.World.Fleet.ScheduleNext(ship, now);
                    return;
                }
                if (double.IsNaN(v.WaitingSinceJd))
                {
                    v.WaitingSinceJd = now;
                    if (gate != null) system.World.Log(now, $"{ship.Name} waits at the {gate.Name}: it is shut to {ship.Faction.Name}", WorldEventKind.Info, system.Catalog.Root.Index);
                }
                if (now - v.WaitingSinceJd > MaxWaitDays)
                {
                    atGates.RemoveAll(e => e.ship == ship);
                    GiveUp(system, ship, now);
                }
                return;
            }

            atGates.RemoveAll(e => e.ship == ship);
            Enter(system, ship, gate, when);
        }

        /// <summary>
        /// Takes the group itself (never copies, U.SPC-T453) out of the fleet here and into the gate, bound for the
        /// far end: each hull the same object all the way, <see cref="Fleet.LetGo"/> leaving a gone marker in its old
        /// slot so no other id here moves.
        /// </summary>
        void Enter(StarSystem system, Ship leader, JumpGate gate, double jd)
        {
            var v = leader.Voyage;
            var world = system.World;
            var link = gate.Link;
            v.AtGate = false;
            v.WaitingSinceJd = double.NaN;
            v.Leg++;

            // The gate's toll on a cargo, split between its two owners: the distance-scaled markup on interstellar freight.
            if (leader.HasCargo && leader.CargoCost > 0)
            {
                double toll = leader.CargoCost * MarkupPerLy * link.Ly;
                leader.CargoCost += toll;
                leader.Faction.Treasury -= toll;
                v.Tolls += toll;
                TollsPaid += toll;
                if (gate.Owner != null) gate.Owner.Treasury += toll * 0.5;
                if (gate.Partner.Owner != null) gate.Partner.Owner.Treasury += toll * 0.5;
            }
            world.Economy.Release(leader);

            var p = new Passage { From = gate, To = gate.Partner, EnterJd = jd, ExitJd = jd + link.TransitDays, News = News.Bundle(system, jd) };
            p.Ships.Add(leader);
            var going = new List<Ship>();
            foreach (var e in world.Fleet.Ships)
            {
                // An escort still fighting, or left a hulk, stays behind; so does one that never leaves its home system
                // (a corvette, U.SPC-T472). Every other hull crosses by the gate, drive or none.
                if (e.Leader != leader || e.Destroyed || e.Battle != null || e.Derelict || !FtlRoster.LeavesHome(e)) continue;
                going.Add(e);
            }
            foreach (var e in going)
            {
                p.Ships.Add(e);
                if (world.Escorts != null && world.Escorts.IsHired(e))
                {
                    p.Hired.Add(e);
                    world.Escorts.UnmarkHired(e);
                }
                world.War?.Forget(e);
                world.Fleet.LetGo(e);
                // The same leader it was flying with, still the one going along with it, orbit and all until it
                // too is reset at the far gate (Ship.ResetForArrival, in Emerge).
                e.Leader = leader;
            }
            DepartLeader(world, leader, v);
            inGates.Add(p);
            Crossings++;
            LogCrossing(new GateCrossing
            {
                Link = link, From = system, Jd = jd, Purpose = v.Purpose, Faction = leader.Faction, Ships = p.Ships.Count,
                Tonnes = leader.HasCargo ? leader.CargoTonnes : 0,
            });
            gate.Traffic += 1;
            string group = p.Ships.Count > 1 ? $" and {p.Ships.Count - 1} more" : "";
            world.Log(jd, $"{leader.Name}{group} enter{(p.Ships.Count > 1 ? "" : "s")} the {gate.Name} for {gate.Destination.Name}", WorldEventKind.Info, system.Catalog.Root.Index);
        }

        /// <summary>
        /// Takes <paramref name="leader"/> itself out of <paramref name="world"/>'s fleet (U.SPC-T453), whether bound
        /// for a gate or across open space under its own drive: its orders here are for a hull no longer here
        /// (U.SPC-T311), and those left behind (U.SPC-T310) are no longer its escorts, so they carry on under the
        /// strongest of them rather than holding station for ever on a hull that has gone -- a hunter's place on the
        /// boards goes with it. The hand-over reads the leader's own state (route, orbit, journey) to pass to any
        /// such heir, so <see cref="Fleet.LetGo"/> (which clears some of it) comes after, and the voyage is masked
        /// off first so a stay-behind heir starts fresh rather than inheriting a journey it never asked for; the
        /// caller gets the same object back, ready to cross, with its own voyage restored, everything else about it
        /// (orbit included) still exactly as it was here until <see cref="Ship.ResetForArrival"/> at the far end.
        /// </summary>
        static void DepartLeader(World world, Ship leader, Voyage v)
        {
            world.War?.Forget(leader);
            leader.GateKm = null;
            leader.Voyage = null;
            // The old flagship's hold goes with the hull that is actually leaving, not a stay-behind heir.
            leader.Destroyed = true;
            world.War?.HandOverCommand(leader, keepHunter: v.Purpose == VoyagePurpose.Hunt);
            leader.Destroyed = false;
            if (v.Purpose == VoyagePurpose.Hunt) world.Outlaws?.SetAway(leader, true);
            world.Fleet.LetGo(leader);
            leader.Voyage = v;
        }

        /// <summary>Out of the far gate: into that system's fleet, then on to the next gate or to the voyage's business there.</summary>
        void Emerge(Passage p)
        {
            var system = p.To.Home;
            var world = system.World;
            var fleet = world.Fleet;
            var leader = p.Leader;
            var v = leader.Voyage;
            double jd = p.ExitJd;
            var forward = p.From.ArrivalForward;
            var up = Math.Abs(forward.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
            var right = DVec3.Cross(forward, up).Normalized;
            ParkingOrbit AtGate() => new ParkingOrbit
            {
                Body = system.Catalog.Root.Index, RadiusKm = 0, Center = p.From.ArrivalOffsetKm, U = right, V = forward,
                EpochJd = jd, PhaseAtEpoch = 0, RateRadPerDay = 0, Fixed = true,
            };
            foreach (var s in p.Ships)
            {
                // Still exactly as it was leaving the last system (U.SPC-T453: the same object, never reset in
                // transit) until now, arriving at this one.
                s.ResetForArrival();
                s.Orbit = AtGate();
                s.Phase = ShipPhase.Parked;
                s.PhaseStartJd = jd;
                s.PhaseEndJd = jd;
                s.DestinationBody = s.Orbit.Body;
                s.Ports = world.FreeShipPorts();
                fleet.Receive(s);
                // A fresh id in this system's fleet, so a leftover id from the system it came from never
                // happens to land on some other local group here: the arriving band keeps its own identity
                // (see Ship.BandId), rebuilt from ids this fleet just handed out, the same way Fleet.AddEscort
                // would (U.SPC-T363).
                s.BandId = s.Leader == null ? s.Id : s.Leader.BandId;
                // Picks a hired fighter's tracking back up here, so it is still paid off when its merchant is gone,
                // instead of flying on untracked once the gate it crossed left the system that hired it behind (U.SPC-T301).
                if (p.Hired.Contains(s)) world.Escorts?.MarkHired(s);
            }

            if (v.Purpose == VoyagePurpose.Campaign && galaxy.Wars != null && galaxy.Wars.OnEmerged(system, leader, p.To, jd))
            {
                // A war fleet come out beside an enemy's gate holds there to take it first (U.SPC-T92).
            }
            else if (v.NextGate != null)
            {
                // Passing through: across to the next gate.
                leader.GateKm = v.NextGate.PositionKm;
                fleet.ScheduleNext(leader, jd);
            }
            else if (v.Homeward) Home(system, leader, jd);
            else Arrive(system, leader, p.To, jd);
            foreach (var s in p.Ships) world.NotifyAdded(s);
            // The gate's pickets see it come out and warn their navy if it is hostile (U.SPC-T480).
            GatePickets.OnArrival(p.To, p.Ships, jd);
            News.Carried(system, p.News, leader.Name, jd);
            p.To.Traffic += 1;
            // Raiders lying off the gate see it come out (U.SPC-T94), however quickly it moves on.
            world.War.GateAmbushes(double.IsNaN(world.Jd) ? jd : Math.Max(jd, world.Jd));
        }

        // ---------------------------------------------------------------- under its own drive (U.SPC-T412)

        /// <summary>Whether <paramref name="ship"/> carries a cruise drive, and so can cross between the stars without a gate: by the
        /// owner's roster (U.SPC-T472, <see cref="FtlRoster"/>) a carrier, a battleship fitted with one or an explorer, never a
        /// cruiser or destroyer, whose voyages here go by the gates.</summary>
        public static bool CanCruise(Ship ship) => CruiseDrives.Of(ship) != null;

        /// <summary>
        /// Sends <paramref name="ship"/> (idle in <paramref name="from"/>, with a cruise drive) to <paramref name="to"/> through
        /// open space under its own drive, gates or none: out to the edge of the well, across, and in from the far edge.
        /// Null if it cannot go.
        /// </summary>
        public Voyage SendByCruise(Ship ship, StarSystem from, StarSystem to, VoyagePurpose purpose, double jd)
        {
            if (to == null || from == to || !Idle(from.World, ship) || !CanCruise(ship)) return null;
            return SetOutByCruise(from, to, null, ship, purpose, jd);
        }

        /// <summary>
        /// Sends <paramref name="ship"/> (idle in <paramref name="from"/>, with a cruise drive) to <paramref name="star"/>, a star
        /// nobody simulates, under its own drive: it drops out at the edge of the star's well, stays a while and comes home.
        /// Null if it cannot go.
        /// </summary>
        public Voyage SendToStar(Ship ship, StarSystem from, NearbyStar star, VoyagePurpose purpose, double jd)
        {
            if (star.Name == null || !Idle(from.World, ship) || !CanCruise(ship)) return null;
            return SetOutByCruise(from, null, star.Name, ship, purpose, jd);
        }

        /// <summary>
        /// Sends <paramref name="ship"/> (idle in <paramref name="from"/>, with a cruise drive, and its escorts) under its own
        /// drive to <paramref name="station"/> in open space, to lie in wait there with a gravity projector (U.SPC-T421) and then
        /// come home. Null if it cannot go.
        /// </summary>
        public Voyage SendToAmbush(Ship ship, StarSystem from, GalacticPosition station, string where, double jd)
        {
            if (!Idle(from.World, ship) || !CanCruise(ship)) return null;
            return SetOutByCruise(from, null, null, ship, VoyagePurpose.Ambush, jd, station, where);
        }

        Voyage SetOutByCruise(StarSystem from, StarSystem to, string star, Ship ship, VoyagePurpose purpose, double jd,
            GalacticPosition? station = null, string stationName = null)
        {
            var target = station ?? (to != null ? to.Origin : FindStar(star)?.Origin);
            if (target == null) return null;
            ship.StakeoutKm = null;
            var v = new Voyage
            {
                Purpose = purpose, Home = from, Destination = to, Route = new List<JumpGate>(), SetOutJd = jd, Faction = ship.Faction,
                Ly = target.Value.DistanceLy(from.Origin), ByCruise = true, StarName = star, Station = station, StationName = stationName,
                HomeRole = ship.Role, HomeRoute = ship.Route, HomePorts = ship.Ports,
            };
            ship.Voyage = v;
            outCache?.Add(v);
            ship.GateKm = DeepSpace.DeparturePointKm(from, target.Value);
            from.World.Fleet.ScheduleNext(ship, jd);
            return v;
        }

        /// <summary>Turns a voyage under way in <paramref name="system"/> onto its own drive for <paramref name="to"/>: out to the well's edge from here.</summary>
        void CruiseFrom(StarSystem system, Ship ship, StarSystem to, double jd)
        {
            var v = ship.Voyage;
            v.Destination = to;
            v.ByCruise = true;
            v.StarName = null;
            v.Station = null;
            v.Route = new List<JumpGate>();
            v.Leg = 0;
            v.AtGate = false;
            v.WaitingSinceJd = double.NaN;
            ship.GateKm = DeepSpace.DeparturePointKm(system, to.Origin);
            system.World.Fleet.ScheduleNext(ship, jd);
        }

        NearbyStar? FindStar(string name)
        {
            if (name == null) return null;
            foreach (var n in galaxy.NearbyStars)
                if (string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }

        /// <summary>
        /// At the edge of the well: the group's hulls with a drive go out into open space together, the same objects
        /// (U.SPC-T453), never copies, with the fighters and small craft that fit docked aboard them (U.SPC-T419, see
        /// <see cref="FtlCarriage"/>); escorts that cannot cross on their own and find no berth stay behind under the
        /// strongest of them.
        /// </summary>
        void EngageCruise(StarSystem system, Ship leader, double jd)
        {
            var v = leader.Voyage;
            var world = system.World;
            var drive = CruiseDrives.Of(leader);
            var star = v.StarName != null ? FindStar(v.StarName) : null;
            if (drive == null || (v.Destination == null && star == null && v.Station == null) || v.Destination == system)
            {
                CancelVoyage(system, leader, jd, system == v.Home);
                return;
            }
            v.AtGate = false;
            v.WaitingSinceJd = double.NaN;
            world.Economy.Release(leader);
            var at = leader.Orbit != null ? leader.Orbit.OffsetAt(jd) : leader.GateKm ?? DVec3.Zero;

            var ships = new List<Ship> { leader };
            var hired = new HashSet<Ship>();
            var going = new List<Ship>();
            var craft = new List<Ship>();
            foreach (var e in world.Fleet.Ships)
            {
                // An escort still fighting or left a hulk stays behind; one with no drive of its own goes only docked aboard,
                // or clamped to a carrier's hull (U.SPC-T473).
                if (e.Leader != leader || e.Destroyed || e.Battle != null || e.Derelict) continue;
                if (CanCruise(e)) going.Add(e);
                else craft.Add(e);
            }
            var carriers = new List<Ship> { leader };
            carriers.AddRange(going);
            var aboard = FtlCarriage.Stow(carriers, craft);
            foreach (var e in craft)
                if (aboard.ContainsKey(e)) going.Add(e);
            foreach (var e in going)
            {
                ships.Add(e);
                if (world.Escorts != null && world.Escorts.IsHired(e))
                {
                    hired.Add(e);
                    world.Escorts.UnmarkHired(e);
                }
                world.War?.Forget(e);
                world.Fleet.LetGo(e);
                // The same leader it was flying with, still the one going along with it, orbit and all until it
                // too is reset dropping out of cruise (Ship.ResetForArrival, in DeepSpace.Drop).
                e.Leader = leader;
            }
            DepartLeader(world, leader, v);

            var news = News.Bundle(system, jd);
            var c = v.Station != null && v.Destination == null
                ? galaxy.DeepSpace.LaunchToPoint(ships, system, at, v.Station.Value, v.StationName ?? "open space", drive, jd, news)
                : star != null
                ? galaxy.DeepSpace.LaunchToStar(ships, system, at, star.Value, drive, jd, news)
                : galaxy.DeepSpace.Launch(ships, system, at, v.Destination, drive, jd, news, dropDir: v.DropDir);
            foreach (var h in hired) c.Hired.Add(h);
            foreach (var kv in aboard) c.Aboard[kv.Key] = kv.Value;
            string group = ships.Count > 1 ? $" and {ships.Count - 1} more" : "";
            // A jump strike group: its escorts clamped to the carrier's hull go with it (U.SPC-T473).
            int clamped = FtlCarriage.CountClamped(c.Aboard.Keys);
            string onHull = clamped > 0 ? $", {clamped} escort{(clamped == 1 ? "" : "s")} clamped to the hull" : "";
            world.Log(jd, $"{leader.Name}{group} engage{(ships.Count > 1 ? "" : "s")} {drive.Name.ToLowerInvariant()} for {c.ToName}, " +
                          $"{c.Profile.Ly.ToString("0.00", CultureInfo.InvariantCulture)} ly through open space{onHull}", WorldEventKind.Info, system.Catalog.Root.Index);
        }

        /// <summary>Dropped out at the edge of a system's well and in its fleet: home, or to its business there.</summary>
        void OnCruiseArrived(Cruise c, double jd)
        {
            var system = c.To;
            var leader = c.Leader;
            var v = leader.Voyage;
            if (v != null)
            {
                v.ByCruise = false;
                v.DropDir = null;
                if (v.Homeward) Home(system, leader, jd);
                else if (v.Destination == system) Arrive(system, leader, null, jd);
                else
                {
                    // Sent somewhere else on the way: on from here, by the gates if they go there, else by its drive.
                    var route = v.Destination == null ? null : Network.Route(system, v.Destination, leader.Faction, jd);
                    if (route != null && route.Count > 0)
                    {
                        v.Route = route;
                        v.Leg = 0;
                        leader.GateKm = route[0].PositionKm;
                        system.World.Fleet.ScheduleNext(leader, jd);
                    }
                    else if (v.Destination != null && CanCruise(leader)) CruiseFrom(system, leader, v.Destination, jd);
                    else Arrive(system, leader, null, jd);
                }
            }
            News.Carried(system, c.News, leader.Name, jd);
        }

        /// <summary>Dropped out at a plain star it was sent to look at: it holds there a while, then sets out for home.</summary>
        void OnReachedStar(Cruise c, double jd)
        {
            var leader = c.Leader;
            var v = leader.Voyage;
            if (v == null || v.Home == null) return;
            Arrivals[v.Purpose] = (Arrivals.TryGetValue(v.Purpose, out int n) ? n : 0) + 1;
            if (v.Purpose == VoyagePurpose.Ambush && v.Station != null)
            {
                // At its station on the route: it lies in wait there a while, then goes home (U.SPC-T421).
                double wait = AmbushDays + ambushRng.NextDouble() * AmbushDays;
                string where = v.StationName ?? "in open space";
                v.Station = null;
                v.Destination = v.Home;
                v.StayUntilJd = jd + wait;
                galaxy.DeepSpace.Retarget(c, v.Home, jd + wait);
                galaxy.DeepSpace.LieInWait(c, jd, wait, where);
                return;
            }
            double stay = 8 + cruiseRng.NextDouble() * 12;
            v.Home.World.Log(jd, $"{leader.Name} of {leader.Faction.Name} reports from {c.ToName}, {v.Ly.ToString("0.0", CultureInfo.InvariantCulture)} ly out under its own drive",
                WorldEventKind.Info, v.Home.Catalog.Root.Index);
            // The first survey of a star off the gate network pays for its charts (U.SPC-T421).
            if (v.Purpose == VoyagePurpose.Survey && galaxy.DeepSpace.SurveyStar(c.ToName))
            {
                double pay = StarSurveyCredits * (1 + v.Ly / 10);
                leader.Credits += pay;
                leader.Faction.Treasury += pay;
                galaxy.DeepSpace.Log(jd, $"Survey find: {leader.Name} of {leader.Faction.Name} is first to chart {c.ToName}, " +
                                         $"for {pay.ToString("N0", CultureInfo.InvariantCulture)} cr", c.Target);
            }
            v.StarName = null;
            v.Destination = v.Home;
            v.StayUntilJd = jd + stay;
            galaxy.DeepSpace.Retarget(c, v.Home, jd + stay);
        }

        /// <summary>What the first survey of a plain star pays, credits, plus as much again for every ten light-years out.</summary>
        public const double StarSurveyCredits = 300000;

        /// <summary>How long a band lies in wait on a route at least, days (up to twice as long).</summary>
        public const double AmbushDays = 15;

        /// <summary>Daily chance a settled system's pirates, or its navy at war, send a band out to lie in wait on an FTL route.</summary>
        public const double AmbushChancePerDay = 0.01;

        /// <summary>
        /// Now and then a band goes out under its own drive to lie in wait on an FTL route with a gravity projector
        /// (U.SPC-T421): a system's pirates on the line between it and another settled system, its navy, when at war, on the
        /// approach from the enemy's. Only hulls with a drive of their own go (U.SPC-T472: a carrier or a battleship with one
        /// fitted, so a raider band of cruisers and destroyers keeps to the gates and lies in wait off them and on their lanes
        /// instead); none goes while one from here is still out.
        /// </summary>
        void DispatchAmbush(StarSystem from, List<StarSystem> settled, double jd)
        {
            var world = from.World;
            if (Out(from, VoyagePurpose.Ambush) > 0 || ambushRng.NextDouble() > AmbushChancePerDay) return;
            var pirates = world.Pirates;
            var navy = world.Sol != null && world.Sol.IsMilitary && galaxy.Polities.AtWar(world.Sol) ? world.Sol : null;
            foreach (var side in new[] { pirates, navy })
            {
                if (side == null) continue;
                StarSystem other = null;
                foreach (var s in settled)
                {
                    if (s == from) continue;
                    // A navy pickets the approach from an enemy's system; a clan works any run between settled systems.
                    if (side == navy && (s.World.Sol == null || galaxy.Polities.Diplomacy.Get(side, s.World.Sol) != Relation.War)) continue;
                    if (s.Origin.DistanceLy(from.Origin) > DeepSpaceFeatures.MaxRouteLy) continue;
                    if (other == null || ambushRng.NextDouble() < 0.5) other = s;
                }
                if (other == null) continue;
                foreach (var ship in world.Fleet.Ships)
                {
                    if (!Idle(world, ship) || ship.Faction != side || !CanCruise(ship) || ship.Phase != ShipPhase.Parked) continue;
                    if (ship.Combat == null || ship.Combat.HullDamage > 0.2 || !ship.Combat.Armed || (side == navy && (ship.PatrolWing || world.War.IsHomeGuard(ship)))) continue;
                    double t = side == navy ? 0.15 + 0.15 * ambushRng.NextDouble() : 0.3 + 0.4 * ambushRng.NextDouble();
                    var station = GalacticPosition.Lerp(from.Origin, other.Origin, t);
                    string where = side == navy ? $"on the approach from {other.Name}" : $"on the {from.Name}–{other.Name} run";
                    var v = SendToAmbush(ship, from, station, where, jd);
                    if (v == null) continue;
                    world.Log(jd, side == navy
                            ? $"{ship.Name} goes out under its own drive to picket the FTL approach from {other.Name}"
                            : $"The {side.Name} raiders under {ship.Name} go out under their own drive to lie in wait {where}",
                        WorldEventKind.Info, PortOf(ship));
                    return;
                }
            }
        }

        /// <summary>Back where it belongs: takes up its old role, route and ports.</summary>
        void Home(StarSystem system, Ship ship, double jd)
        {
            var v = ship.Voyage;
            ship.Voyage = null;
            ship.GateKm = null;
            v.ByCruise = false;
            ship.Role = v.HomeRole;
            ship.Route = v.HomeRoute;
            ship.RouteIndex = 0;
            ship.Ports = v.HomePorts ?? ship.Ports;
            // A bounty hunter takes up its old place on the home boards (U.SPC-T94). The hull that set out is the one
            // coming home now (U.SPC-T453: the same object the whole trip) unless it was lost abroad and an escort
            // took the hunt on as heir, so this only rebinds the hunter's row when that actually happened.
            if (v.Purpose == VoyagePurpose.Hunt && v.Origin != null)
            {
                if (v.Origin != ship) system.World.Outlaws?.OnSuccession(v.Origin, ship);
                else system.World.Outlaws?.SetAway(ship, false);
            }
            // A smuggler back with goods it could not sell puts them back in the haven's stores.
            if (ship.Runner && ship.Plunder != null && system.World.War.LiveHavenOf(ship.Faction) is Settlement haven)
            {
                for (int i = 0; i < GoodInfo.Count; i++) haven.Stock[i] += ship.Plunder[i];
                ship.Plunder = null;
            }
            system.World.Fleet.ScheduleNext(ship, jd);
            system.World.Log(jd, $"{ship.Name} is home from {Describe(v)}", WorldEventKind.Info, system.Catalog.Root.Index);
        }

        static string Describe(Voyage v) => v.Purpose switch
        {
            VoyagePurpose.Raid => "a raid beyond the gates",
            VoyagePurpose.Expedition => "an expedition beyond the gates",
            VoyagePurpose.Campaign => "a campaign beyond the gates",
            VoyagePurpose.Survey => "a survey beyond the gates",
            VoyagePurpose.Courier => "a mail run beyond the gates",
            VoyagePurpose.Smuggle => "a smuggling run beyond the gates",
            VoyagePurpose.Hunt => "a hunt beyond the gates",
            VoyagePurpose.Relocate => "a move beyond the gates",
            VoyagePurpose.Ambush => "lying in wait between the stars",
            VoyagePurpose.Directed => "its orders beyond the gates",
            VoyagePurpose.Plotted => "a plotted course beyond the gates",
            VoyagePurpose.Colonise => "a colony voyage beyond the gates",
            VoyagePurpose.Supply => "a supply run to the war fleet",
            _ => "a run beyond the gates",
        };

        /// <summary>At the voyage's far end: to work, by what it came for.</summary>
        void Arrive(StarSystem system, Ship ship, JumpGate cameIn, double jd)
        {
            var v = ship.Voyage;
            var world = system.World;
            ship.GateKm = null;
            Arrivals[v.Purpose] = (Arrivals.TryGetValue(v.Purpose, out int n) ? n : 0) + 1;
            switch (v.Purpose)
            {
                case VoyagePurpose.Trade:
                    ship.Ports = world.PortBodies();
                    v.StayUntilJd = jd + 25 + rng.NextDouble() * 20;
                    break;
                case VoyagePurpose.Courier:
                {
                    double fee = MailFeePerLy * v.Ly;
                    ship.Faction.Treasury += fee;
                    ship.Credits += fee;
                    if (world.Sol != null) world.Sol.Treasury -= fee;
                    ship.Cargo = null;
                    ship.Ports = world.PortBodies();
                    v.StayUntilJd = jd + 5 + rng.NextDouble() * 7;
                    break;
                }
                case VoyagePurpose.Survey:
                    ship.Ports = world.FreeShipPorts();
                    v.StayUntilJd = jd + 15 + rng.NextDouble() * 15;
                    break;
                case VoyagePurpose.Raid:
                    ship.Route = null;
                    ship.Ports = null;
                    v.StayUntilJd = jd + 20 + rng.NextDouble() * 15;
                    // The gate's owner shuts it behind them, and sends a navy after them to where they came from. (Raiders
                    // who came in through open space under their own drive came by no one's gate.)
                    if (cameIn != null && cameIn.Owner != null && cameIn.Owner.IsMilitary)
                    {
                        Blockade(cameIn, jd, BlockadeDays, $"{ship.Faction.Name} raiders come through");
                        expeditionsWanted.Add((system, v.Home, ship.Faction, jd));
                    }
                    break;
                case VoyagePurpose.Expedition:
                case VoyagePurpose.Hunt:
                    ship.Route = null;
                    ship.Ports = null;
                    v.StayUntilJd = jd + 20 + rng.NextDouble() * 10;
                    break;
                case VoyagePurpose.Directed:
                    // Here until its objective is done or called off (see StrategicCommand); the war here gives its orders.
                    ship.Route = null;
                    ship.Ports = null;
                    v.StayUntilJd = double.PositiveInfinity;
                    break;
                case VoyagePurpose.Plotted:
                    // The course is run: the voyage ends and the hull is its own again (see the end of this method).
                    ship.Route = null;
                    ship.Ports = null;
                    break;
                case VoyagePurpose.Campaign:
                    // Here until the campaign is won, lost or called off (see InterstellarWars), not for a set stay.
                    ship.Route = null;
                    ship.Ports = null;
                    v.StayUntilJd = double.PositiveInfinity;
                    break;
                case VoyagePurpose.Smuggle:
                    // Until it has sold (see Orders), and no longer than this.
                    ship.Ports = world.FreeShipPorts();
                    v.StayUntilJd = jd + 30;
                    break;
                case VoyagePurpose.Colonise:
                    // In to the site to land its colonists (U.SPC-T524, see World.ColonyHullArrived); here until it has.
                    ship.Route = null;
                    ship.Ports = v.TargetBody >= 0 ? new[] { v.TargetBody } : world.FreeShipPorts();
                    v.StayUntilJd = double.PositiveInfinity;
                    break;
                case VoyagePurpose.Relocate:
                    Clans.Settle(system, ship, jd);
                    return;
                case VoyagePurpose.Supply:
                    // Here to come up with its war fleet (U.SPC-T525): its navy's logistics says where it goes next and when
                    // it turns for home. Turned back on the way (its fleet's campaign over), it goes home at once.
                    ship.Ports = world.FreeShipPorts();
                    v.StayUntilJd = v.Convoy != null ? double.PositiveInfinity : jd;
                    break;
            }
            ship.Role = v.Purpose == VoyagePurpose.Survey ? ShipRole.Free : v.HomeRole;
            if (ship.Role == ShipRole.Free || v.Purpose == VoyagePurpose.Raid || v.Purpose == VoyagePurpose.Expedition || v.Purpose == VoyagePurpose.Campaign || v.Purpose == VoyagePurpose.Hunt ||
                v.Purpose == VoyagePurpose.Directed || v.Purpose == VoyagePurpose.Plotted)
                ship.Ports ??= world.FreeShipPorts();
            world.Log(jd, cameIn != null
                    ? $"{ship.Name} of {ship.Faction.Name} comes through the {cameIn.Partner.Name} from {v.Home.Name}"
                    : $"{ship.Name} of {ship.Faction.Name} comes in from {v.Home.Name} under its own drive", WorldEventKind.Info, system.Catalog.Root.Index);
            if (v.Purpose == VoyagePurpose.Plotted) ship.Voyage = null;
            world.Fleet.ScheduleNext(ship, jd);
        }

        /// <summary>
        /// Shuts <paramref name="gate"/> to everyone but its owner for <paramref name="days"/> (or
        /// longer, if already shut longer).
        /// </summary>
        public void Blockade(JumpGate gate, double jd, double days, string why = null)
        {
            bool already = gate.Blockaded(jd);
            gate.BlockadeUntilJd = Math.Max(gate.BlockadeUntilJd, jd + days);
            if (!already && gate.Owner != null)
                gate.Home.World.Log(jd, $"{gate.Owner.Name} closes the {gate.Name}" + (why != null ? " after " + why : ""), WorldEventKind.Diplomacy, gate.Home.Catalog.Root.Index);
        }

        /// <summary>Opens a blockaded gate again.</summary>
        public void Lift(JumpGate gate, double jd)
        {
            if (!gate.Blockaded(jd)) return;
            gate.BlockadeUntilJd = jd;
            if (gate.Owner != null) gate.Home.World.Log(jd, $"{gate.Owner.Name} opens the {gate.Name} again", WorldEventKind.Diplomacy, gate.Home.Catalog.Root.Index);
        }

        /// <summary>The way is shut and stays shut: it gives up and goes about its business where it is.</summary>
        void GiveUp(StarSystem system, Ship ship, double jd)
        {
            var v = ship.Voyage;
            v.AtGate = false;
            v.WaitingSinceJd = double.NaN;
            ship.GateKm = null;
            // The gates are shut to it, but it has a drive of its own (U.SPC-T412): it goes the long way, through open space.
            if (v.Campaign == null && v.Destination != null && v.Destination != system && CanCruise(ship))
            {
                CruiseFrom(system, ship, v.Destination, jd);
                system.World.Log(jd, $"{ship.Name} gives up on the gates and sets out for {v.Destination.Name} under its own drive", WorldEventKind.Info, system.Catalog.Root.Index);
                return;
            }
            // A war fleet's voyage is its campaign's: it is not called off here but ended by the war (U.SPC-T296).
            if (system == v.Home && v.Campaign == null)
            {
                CancelVoyage(system, ship, jd);
                return;
            }
            // Stranded abroad (or between): it stays a while and tries again for home.
            v.Destination = system;
            v.Route = new List<JumpGate>();
            v.Leg = 0;
            v.StayUntilJd = jd + 3;
            ship.Ports = ship.Role == ShipRole.Free || ship.Spec.IsMilitary || ship.Faction.IsPirate ? system.World.FreeShipPorts() : system.World.PortBodies();
            system.World.Fleet.ScheduleNext(ship, jd);
        }

        /// <summary>
        /// Calls a voyage off where the ship is (it was taken over by the pilot or by captors, or never
        /// got away); <paramref name="restore"/> gives it back its home role if it is at home.
        /// </summary>
        void CancelVoyage(StarSystem system, Ship ship, double jd, bool restore = true)
        {
            var v = ship.Voyage;
            ship.GateKm = null;
            if (v == null) return;
            v.AtGate = false;
            ship.Voyage = null;
            if (restore && system == v.Home)
            {
                ship.Role = v.HomeRole;
                ship.Route = v.HomeRoute;
                ship.Ports = v.HomePorts ?? ship.Ports;
            }
            else if (restore || ship.Ports == null) ship.Ports = system.World.FreeShipPorts();
            if (ship != system.World.PilotedShip && !ship.Destroyed && !ship.Derelict) system.World.Fleet.ScheduleNext(ship, jd);
        }

        // ---------------------------------------------------------------- orders abroad

        /// <summary>World hook: where a raider group or an expedition abroad goes next (-1 for anything else).</summary>
        int Orders(StarSystem system, Ship ship, int here, double jd)
        {
            var v = ship.Voyage;
            if (v == null || ship.GateKm != null) return -1;
            if (v.Purpose == VoyagePurpose.Campaign) return galaxy.Wars?.Orders(system, ship, here, jd) ?? -1;
            // A navy's supply ships make for their war fleet wherever it is in this system, home or abroad (U.SPC-T525).
            if (v.Purpose == VoyagePurpose.Supply && v.Convoy != null) return galaxy.Wars?.Logistics.ConvoyOrders(system, ship, here, jd) ?? -1;
            if (system == v.Home) return -1;
            var economy = system.World.Economy;
            var war = system.World.War;
            if (v.Purpose == VoyagePurpose.Raid)
            {
                // Lying in wait off a gate till the watch is up; or, now and then, off the busiest one (U.SPC-T94).
                int root = system.Catalog.Root.Index;
                var watch = ship.StakeoutKm;
                ship.StakeoutKm = null;
                if (watch is DVec3 spot && jd < ship.StakeoutUntilJd)
                {
                    ship.StakeoutKm = spot;
                    return root;
                }
                if (here != root && rng.NextDouble() < War.GateWatchChance && war.BusiestGate(ship.Faction, jd) is JumpGate gate)
                {
                    war.StakeOut(ship, gate, jd + system.World.Fleet.EstimateTravelDays(ship, here, root, jd) + 2 + rng.NextDouble() * 2);
                    return root;
                }
                // The busiest port with guns it can face; it hangs about a port that is paying off.
                int best = -1;
                double bestScore = 0;
                foreach (var s in economy.Settlements)
                {
                    if (s.Kind == SettlementKind.Haven || !s.Discovered) continue;
                    double traffic = 1;
                    foreach (var t in system.World.Fleet.Ships)
                        if (!t.Destroyed && system.World.IsTrader(t) && (t.DestinationBody == s.Body || (t.Orbit != null && t.Orbit.Body == s.Body))) traffic++;
                    double defence = war.DefenceAt(s.Body, ship.Faction);
                    double score = traffic / (defence + 100) * (0.7 + rng.NextDouble() * 0.6);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = s.Body;
                    }
                }
                return best >= 0 ? best : here;
            }
            if (v.Purpose == VoyagePurpose.Smuggle) return Clans.SmugglerOrders(system, ship, here, jd);
            if (v.Purpose == VoyagePurpose.Expedition || v.Purpose == VoyagePurpose.Hunt)
            {
                // The quarry's haven, if it is known (to it); else wherever its raiders are; else the busiest port.
                // Sent after one clan, it goes after that clan only, not any other pirate's hold or band (U.SPC-T309).
                foreach (var s in economy.Settlements)
                    if (s.Kind == SettlementKind.Haven && !s.Fallen && s.KnownBy(ship.Faction) && (v.Quarry == null || s.Owner == v.Quarry))
                    {
                        // A hidden hold found, its guns silenced: a navy storms it, boarding it if it has the marines (U.SPC-T94).
                        // (An open one, like Blackwater, the navies leave be.)
                        if (s.Body == here && s.Hidden && v.Purpose == VoyagePurpose.Expedition && war.CanAssault(s, ship))
                        {
                            war.AssaultHaven(s, ship, war.HasMarines(ship), jd);
                            continue;
                        }
                        return s.Body;
                    }
                foreach (var t in system.World.Fleet.Ships)
                    if (!t.Destroyed && !t.IsEscort && t.Faction.IsPirate && !t.Runner && (v.Quarry == null || t.Faction == v.Quarry) && t.Orbit != null && t.Phase == ShipPhase.Parked)
                        return t.Orbit.Body;
                var ports = system.World.PortBodies();
                return ports.Length > 0 ? ports[rng.Next(ports.Length)] : here;
            }
            return -1;
        }

        // ---------------------------------------------------------------- sending ships out

        void Daily(double jd)
        {
            int stale = 0;
            while (stale < crossingLog.Count && crossingLog[stale].Jd < jd - CrossingLogDays) stale++;
            if (stale > 0) crossingLog.RemoveRange(0, stale);

            var settled = new List<StarSystem>();
            foreach (var s in galaxy.Systems)
                if (s.Settled) settled.Add(s);

            foreach (var s in galaxy.Systems) SendHomeWhenDone(s, jd);
            // Every voyage under way, gathered once: the dispatchers below count them for each of many routes.
            outCache = new List<Voyage>(VoyagesInTransit());
            foreach (var s in galaxy.Systems)
                foreach (var ship in s.World.Fleet.Ships)
                    if (!ship.Destroyed && ship.Voyage != null) outCache.Add(ship.Voyage);
            foreach (var from in settled)
                foreach (var to in settled)
                {
                    if (from == to) continue;
                    DispatchTrade(from, to, jd);
                    DispatchCourier(from, to, jd);
                }
            foreach (var from in settled)
            {
                DispatchRaid(from, settled, jd);
                DispatchSurvey(from, jd);
                DispatchAmbush(from, settled, jd);
            }
            for (int i = expeditionsWanted.Count - 1; i >= 0; i--)
            {
                var (from, to, quarry, asked) = expeditionsWanted[i];
                if (DispatchExpedition(from, to, quarry, jd) || jd - asked > 20) expeditionsWanted.RemoveAt(i);
            }
            // The clans: their smugglers, their moves, their splits; and the navies and hunters sent after them.
            Clans.Daily(jd);
            outCache = null;
            double fade = Math.Exp(-1 / GateTrafficDays);
            foreach (var link in Network.Links)
            {
                link.AtA.Traffic *= fade;
                link.AtB.Traffic *= fade;
            }
        }

        /// <summary>A ship that can be sent: its own master (not an escort, not the player's), idle in port, not fighting.</summary>
        public static bool Idle(World world, Ship s) =>
            !s.Destroyed && !s.IsEscort && !s.Derelict && s.Battle == null && s != world.PilotedShip && s.Voyage == null && s.GateKm == null &&
            (s.Phase == ShipPhase.Parked || s.Phase == ShipPhase.Landed) && s.Orbit != null
            // Nor one of the player's own fleet: it sails where its captain's standing orders take it.
            && (world.Holdings == null || !world.Holdings.Owns(s))
            // Nor one dry in port waiting on a tanker: it would go back for its old trip first.
            && !s.OutOfPropellant
            // Nor a raider band lying in wait off a gate: it is on its own job until the watch is up.
            && s.StakeoutKm == null
            // Nor a hull that never leaves its home system (a corvette, U.SPC-T472).
            && FtlRoster.LeavesHome(s);

        /// <summary>The body a ship in port is at (its ring's world when landed).</summary>
        internal static int PortOf(Ship s) => s.Phase == ShipPhase.Landed && s.Ring != null ? s.Ring.Body : s.Orbit.Body;

        /// <summary>The voyages under way, while <see cref="Daily"/> sends ships out (null otherwise).</summary>
        List<Voyage> outCache;

        /// <summary>How many of <paramref name="home"/>'s ships with <paramref name="purpose"/> are out (on the way, abroad or coming back).</summary>
        internal int Out(StarSystem home, VoyagePurpose purpose, StarSystem bound = null)
        {
            int n = 0;
            bool Counts(Voyage v) => v != null && v.Home == home && v.Purpose == purpose && (bound == null || v.Destination == bound || (v.Homeward && v.Route.Count > 0 && v.Route[0].Home == bound) || v.Destination == bound);
            if (outCache != null)
            {
                foreach (var v in outCache)
                    if (Counts(v)) n++;
                return n;
            }
            foreach (var v in VoyagesInTransit())
                if (Counts(v)) n++;
            foreach (var s in galaxy.Systems)
                foreach (var ship in s.World.Fleet.Ships)
                    if (!ship.Destroyed && Counts(ship.Voyage)) n++;
            return n;
        }

        /// <summary>
        /// Sends <paramref name="ship"/> (a flagship or a ship on its own, in <paramref name="from"/>)
        /// to <paramref name="to"/> on <paramref name="purpose"/>, by the quickest way open to it.
        /// Returns the voyage, or null if there is no way through or the ship is not free to go.
        /// </summary>
        public Voyage Send(Ship ship, StarSystem from, StarSystem to, VoyagePurpose purpose, double jd)
        {
            if (from == to || !Idle(from.World, ship)) return null;
            var route = Network.Route(from, to, ship.Faction, jd);
            // No way through the gates: a hull with a drive of its own goes through open space instead (U.SPC-T412).
            if (route == null || route.Count == 0) return CanCruise(ship) ? SetOutByCruise(from, to, null, ship, purpose, jd) : null;
            return SetOut(from, to, ship, purpose, route, jd);
        }

        /// <summary>
        /// Sends a war fleet (<paramref name="ship"/>, a flagship idle in <paramref name="from"/>) on <paramref name="campaign"/>
        /// to <paramref name="to"/> (U.SPC-T92), by the quickest way it can fight through: gates held by the enemy count,
        /// to be forced. Null if there is no way at all or the ship is not free to go.
        /// </summary>
        public Voyage SendCampaign(Ship ship, StarSystem from, StarSystem to, Campaign campaign, double jd)
        {
            if (from == to || !Idle(from.World, ship)) return null;
            var route = Network.Route(from, to, ship.Faction, jd, force: true);
            if (route == null || route.Count == 0) return null;
            var v = SetOut(from, to, ship, VoyagePurpose.Campaign, route, jd);
            v.Campaign = campaign;
            v.StayUntilJd = double.PositiveInfinity;
            return v;
        }

        /// <summary>
        /// Sends <paramref name="ship"/> (the player's hull, handed to its crew, or one the god view follows, standing in
        /// <paramref name="from"/>) to <paramref name="to"/> along the quickest open course (U.SPC-T516): gate by gate, each system
        /// on the way crossed to its next gate, never a jump to the far system. Null if there is no way or it is not free to go.
        /// </summary>
        public Voyage SendPlotted(Ship ship, StarSystem from, StarSystem to, double jd)
        {
            if (ship == null || from == null || to == null || from == to) return null;
            var world = from.World;
            // Its own master, not fighting, with nothing else on and standing still (a hull just let go of at the helm is):
            // the player's own hull is not "idle in port" the way a fleet's trader is, so the rest of the usual test is waived.
            if (ship.Destroyed || ship.IsEscort || ship.Derelict || ship.Battle != null || ship == world.PilotedShip || ship.Voyage != null ||
                ship.GateKm != null || ship.Orbit == null || (ship.Phase != ShipPhase.Parked && ship.Phase != ShipPhase.Landed) ||
                ship.Id < 0 || ship.Id >= world.Fleet.Ships.Count || world.Fleet.Ships[ship.Id] != ship) return null;
            var route = Network.Route(from, to, ship.Faction, jd);
            if (route == null || route.Count == 0) return null;
            return SetOut(from, to, ship, VoyagePurpose.Plotted, route, jd);
        }

        /// <summary>
        /// Sends a navy's supply convoy (U.SPC-T525, <paramref name="ship"/> the hull leading it, in port at its naval base in
        /// <paramref name="from"/>, the rest of its hulls in formation on it) after a war fleet in <paramref name="to"/> by
        /// <paramref name="route"/> (gates that will open for it; empty when the fleet is in the base's own system, when it
        /// just flies to it). The hulls carry <paramref name="convoy"/> on the voyage until they hand over its load.
        /// </summary>
        public Voyage SendSupply(Ship ship, StarSystem from, StarSystem to, List<JumpGate> route, SupplyConvoy convoy, double jd)
        {
            if (ship == null || from == null || to == null || ship.Voyage != null) return null;
            Voyage v;
            if (from == to || route == null || route.Count == 0)
            {
                if (from != to) return null;
                // No gate to take: a voyage that never leaves home, so the hull is flown to the fleet by its orders here.
                ship.StakeoutKm = null;
                v = new Voyage
                {
                    Purpose = VoyagePurpose.Supply, Home = from, Destination = from, SetOutJd = jd, Faction = ship.Faction,
                    HomeRole = ship.Role, HomeRoute = ship.Route, HomePorts = ship.Ports, StayUntilJd = double.PositiveInfinity,
                };
                ship.Voyage = v;
                outCache?.Add(v);
                ship.GateKm = null;
                from.World.Fleet.ScheduleNext(ship, jd);
            }
            else v = SetOut(from, to, ship, VoyagePurpose.Supply, route, jd);
            v.Convoy = convoy;
            return v;
        }

        /// <summary>
        /// Stops a hull on a voyage where it is (U.SPC-T525: supply ships that have found their war fleet in the system they
        /// are in): whatever gate it was bound for, it now stays in <paramref name="system"/>, which its voyage now ends in.
        /// </summary>
        public void Hold(StarSystem system, Ship ship, double jd)
        {
            var v = ship.Voyage;
            if (v == null) return;
            atGates.RemoveAll(e => e.ship == ship);
            v.AtGate = false;
            v.HoldGate = null;
            v.WaitingSinceJd = double.NaN;
            v.Destination = system;
            v.Route = new List<JumpGate>();
            v.Leg = 0;
            v.ByCruise = false;
            ship.GateKm = null;
            if (ship.Battle == null) system.World.Fleet.ScheduleNext(ship, jd);
        }

        /// <summary>
        /// Sends a jump strike group (<paramref name="ship"/>, its carrier or FTL capital flagship idle in <paramref name="from"/>)
        /// on <paramref name="campaign"/> to <paramref name="to"/> under its own drives (U.SPC-T474, see <see cref="FleetDoctrine"/>):
        /// out to the edge of the well, across open space with its escorts clamped to the carrier, and in on the flank of
        /// <paramref name="to"/> away from the gates held against it. Null if it cannot go.
        /// </summary>
        public Voyage SendStrike(Ship ship, StarSystem from, StarSystem to, Campaign campaign, double jd)
        {
            if (to == null || from == to || !Idle(from.World, ship) || !CanCruise(ship)) return null;
            var v = SetOutByCruise(from, to, null, ship, VoyagePurpose.Campaign, jd);
            if (v == null) return null;
            v.Campaign = campaign;
            v.StayUntilJd = double.PositiveInfinity;
            v.DropDir = FleetDoctrine.DropDirection(to, from.Origin, ship.Faction);
            return v;
        }

        /// <summary>
        /// Sends a navy group (<paramref name="ship"/>, a flagship idle in <paramref name="from"/>) to <paramref name="to"/> on its
        /// polity's orders (U.SPC-T423, see <see cref="StrategicCommand"/>), by the quickest way open to it, or under its own
        /// drive where no gate goes. It stays there while its objective there stands. Null if it cannot go.
        /// </summary>
        public Voyage SendDirected(Ship ship, StarSystem from, StarSystem to, double jd)
        {
            if (from == to || !Idle(from.World, ship)) return null;
            Voyage v;
            if (CanCruise(ship) && FleetDoctrine.IsStrikeGroup(from.World, ship))
            {
                // A jump strike group jumps in on the flank, gates or none (U.SPC-T474).
                v = SetOutByCruise(from, to, null, ship, VoyagePurpose.Directed, jd);
                if (v != null) v.DropDir = FleetDoctrine.DropDirection(to, from.Origin, ship.Faction);
            }
            else
            {
                var route = Network.Route(from, to, ship.Faction, jd);
                if (route == null || route.Count == 0) v = CanCruise(ship) ? SetOutByCruise(from, to, null, ship, VoyagePurpose.Directed, jd) : null;
                else v = SetOut(from, to, ship, VoyagePurpose.Directed, route, jd);
            }
            if (v != null) v.StayUntilJd = double.PositiveInfinity;
            return v;
        }

        /// <summary>
        /// Points a war fleet on campaign (<paramref name="ship"/>, its flagship in <paramref name="system"/>) at another
        /// system (U.SPC-T92): on to its next goal, or (<paramref name="to"/> its home) back where it set out from, the way it
        /// can fight through. At home already, it takes up its old duties at once. False if there is no way (it stays).
        /// <paramref name="force"/> false keeps it to gates that will open for it (a navy's supply ships, U.SPC-T525, which
        /// cannot fight their way through).
        /// </summary>
        public bool Redirect(StarSystem system, Ship ship, StarSystem to, double jd, bool force = true)
        {
            var v = ship.Voyage;
            if (v == null) return false;
            // A jump strike group goes on, or home, under its own drives (U.SPC-T474): no gates to fight through.
            if (system != to && v.Campaign != null && v.Campaign.Force == ForceKind.JumpStrikeGroup && CanCruise(ship))
            {
                atGates.RemoveAll(e => e.ship == ship);
                v.AtGate = false;
                v.HoldGate = null;
                v.WaitingSinceJd = double.NaN;
                v.Destination = to;
                v.ByCruise = true;
                v.StarName = null;
                v.Station = null;
                v.Route = new List<JumpGate>();
                v.Leg = 0;
                v.DropDir = to == v.Home ? (DVec3?)null : FleetDoctrine.DropDirection(to, system.Origin, ship.Faction);
                ship.GateKm = DeepSpace.DeparturePointKm(system, to.Origin);
                if (ship.Battle == null) system.World.Fleet.ScheduleNext(ship, jd);
                return true;
            }
            List<JumpGate> route = null;
            if (system != to)
            {
                route = Network.Route(system, to, ship.Faction, jd, force: force);
                if (route == null || (!force && route.Count == 0)) return false;
            }
            atGates.RemoveAll(e => e.ship == ship);
            v.AtGate = false;
            v.HoldGate = null;
            v.WaitingSinceJd = double.NaN;
            if (system == to && to == v.Home)
            {
                Home(system, ship, jd);
                return true;
            }
            v.Destination = to;
            v.Route = route ?? new List<JumpGate>();
            v.Leg = 0;
            v.ByCruise = false;
            v.StarName = null;
            v.Station = null;
            ship.GateKm = v.Route.Count > 0 ? v.Route[0].PositionKm : (DVec3?)null;
            if (ship.Battle == null) system.World.Fleet.ScheduleNext(ship, jd);
            return true;
        }

        /// <summary>
        /// A war fleet holding beside a gate it has taken goes on (U.SPC-T92): to its next gate, or, at the end of its way, to
        /// its work in this system.
        /// </summary>
        public void Release(StarSystem system, Ship ship, double jd)
        {
            var v = ship.Voyage;
            if (v == null) return;
            var held = v.HoldGate;
            v.HoldGate = null;
            ship.GateKm = v.NextGate?.PositionKm;
            if (ship.GateKm == null && system == v.Destination && held != null && ship.Battle == null)
            {
                // The way's end: it has arrived (or is home), as a fleet that met no gate in its way would have.
                if (v.Homeward) Home(system, ship, jd);
                else Arrive(system, ship, held, jd);
                return;
            }
            if (ship.Battle == null) system.World.Fleet.ScheduleNext(ship, jd);
        }

        /// <summary>Starts <paramref name="ship"/> on a voyage from <paramref name="from"/> to <paramref name="to"/> by <paramref name="route"/>.</summary>
        internal Voyage SetOut(StarSystem from, StarSystem to, Ship ship, VoyagePurpose purpose, List<JumpGate> route, double jd)
        {
            ship.StakeoutKm = null;
            var v = new Voyage
            {
                Purpose = purpose, Home = from, Destination = to, Route = route, SetOutJd = jd, Ly = GateNetwork.RouteLy(route), Faction = ship.Faction,
                HomeRole = ship.Role, HomeRoute = ship.Route, HomePorts = ship.Ports,
            };
            ship.Voyage = v;
            outCache?.Add(v);
            ship.GateKm = route[0].PositionKm;
            from.World.Fleet.ScheduleNext(ship, jd);
            return v;
        }

        /// <summary>Sends an idle freighter with the most profitable cargo for <paramref name="to"/>'s markets, if one pays.</summary>
        void DispatchTrade(StarSystem from, StarSystem to, double jd)
        {
            if (Out(from, VoyagePurpose.Trade, to) >= MaxTradersPerRoute) return;
            if (Out(from, VoyagePurpose.Trade) >= Math.Max(1, (int)(Freighters(from) * MaxAbroadShare))) return;
            // Traders here know the far markets only by the last news from them (U.SPC-T100): none, no run.
            var news = News.Known(from, to);
            if (news == null) return;
            var world = from.World;
            Ship bestShip = null;
            Settlement bestFrom = null;
            Good bestGood = default;
            double bestTonnes = 0, bestProfit = 0;
            List<JumpGate> bestRoute = null;
            Settlement bestTo = null;
            double bestSale = 0;
            foreach (var ship in world.Fleet.Ships)
            {
                if (!Idle(world, ship) || ship.Role != ShipRole.Trader || ship.Spec.Class != ShipClass.Freighter || ship.HasCargo) continue;
                var route = Network.Route(from, to, ship.Faction, jd);
                if (route == null || route.Count == 0) continue;
                if (BestCargo(from, to, ship, PortOf(ship), route, jd, news.Markets, out var src, out var good, out double tonnes, out double profit, out var dst, out double sale)
                    && profit > bestProfit)
                {
                    bestTo = dst;
                    bestSale = sale;
                    bestProfit = profit;
                    bestShip = ship;
                    bestFrom = src;
                    bestGood = good;
                    bestTonnes = tonnes;
                    bestRoute = route;
                }
            }
            if (bestShip == null) return;
            world.Economy.LoadUnbooked(bestShip, bestFrom, bestGood, bestTonnes, to.Name);
            var voyage = SetOut(from, to, bestShip, VoyagePurpose.Trade, bestRoute, jd);
            if (voyage != null)
            {
                voyage.PlannedMarket = bestTo.Name;
                voyage.ExpectedSale = bestSale;
                voyage.PlannedOnNewsJd = news.AsOfJd;
            }
            world.Log(jd, $"{bestShip.Name} loads {Tonnes(bestTonnes)} {GoodInfo.Of(bestGood).Name} at {bestFrom.Name} for {bestTo.Name} at {to.Name}, by the {bestRoute[0].Name}, on news {LightSpeed.Describe(news.AgeDays(jd))} old", WorldEventKind.Info, bestFrom.Body);
        }

        static string Tonnes(double t) => t.ToString("N0", CultureInfo.InvariantCulture) + " t";

        /// <summary>
        /// The best good to carry from the ship's port to <paramref name="to"/>'s markets: bought here,
        /// marked up by the tolls for the route's light-years (<see cref="MarkupPerLy"/>) and the days
        /// on the way, against what the dearest market there would pay. True if one clears <see cref="MinMargin"/>.
        /// </summary>
        public static bool BestCargo(StarSystem from, StarSystem to, Ship ship, int port, List<JumpGate> route, double jd,
            out Settlement source, out Good good, out double tonnes, out double profit) =>
            BestCargo(from, to, ship, port, route, jd, to.World.Economy.Settlements, out source, out good, out tonnes, out profit, out _, out _);

        /// <summary>
        /// As <see cref="BestCargo(StarSystem, StarSystem, Ship, int, List{JumpGate}, double, out Settlement, out Good, out double, out double)"/>,
        /// but against <paramref name="markets"/>: the far system's boards as the news here has them (U.SPC-T100), not as
        /// they are. <paramref name="market"/> is the board the run is planned on and <paramref name="sale"/> what it says
        /// the cargo will fetch there, after port tax.
        /// </summary>
        public static bool BestCargo(StarSystem from, StarSystem to, Ship ship, int port, List<JumpGate> route, double jd, IEnumerable<Settlement> markets,
            out Settlement source, out Good good, out double tonnes, out double profit, out Settlement market, out double sale)
        {
            market = null;
            sale = 0;
            source = null;
            good = default;
            tonnes = 0;
            profit = 0;
            double ly = GateNetwork.RouteLy(route);
            double days = VoyageDays(from, to, ship, port, route);
            double markup = 1 + MarkupPerLy * ly;
            foreach (var src in from.World.Economy.At(port))
            {
                if (!from.World.Economy.CanTrade(ship, src)) continue;
                for (int g = 0; g < GoodInfo.Count; g++)
                {
                    var gd = (Good)g;
                    if (!Economy.Carries(ship.Spec.Class, gd)) continue;
                    double available = src.Available(gd);
                    double minimum = Math.Min(ship.CapacityTonnes * 0.1, 200);
                    if (available < minimum) continue;
                    foreach (var dst in markets)
                    {
                        if (dst.Kind == SettlementKind.Haven || !to.World.Diplomacy.CanDockAt(ship.Faction, dst)) continue;
                        double t = Math.Min(Math.Min(ship.CapacityTonnes, available), dst.Absorb(gd));
                        if (t < minimum) continue;
                        double landed = src.PurchaseCost(gd, t) * (1 + Economy.PortTax) * markup;
                        double value = dst.SaleValue(gd, t) * (1 - Economy.PortTax);
                        double p = value - landed - days * RunningCostPerDay;
                        if (p > landed * MinMargin && p > profit)
                        {
                            profit = p;
                            market = dst;
                            sale = value;
                            source = src;
                            good = gd;
                            tonnes = t;
                        }
                    }
                }
            }
            return source != null;
        }

        /// <summary>A freighter's running costs a day (as at home).</summary>
        const double RunningCostPerDay = 1500;

        /// <summary>Rough door-to-door days: out to the gate, through the gates, and in from the far gate to a port.</summary>
        public static double VoyageDays(StarSystem from, StarSystem to, Ship ship, int port, List<JumpGate> route)
        {
            double accel = ship.Spec.AccelG * Astro.GravityKmPerDay2;
            double Burn(double km) => 2 * Math.Sqrt(Math.Max(km, 1) / accel);
            double outbound = Burn(from.Ephemeris.Helio(port).Length);
            // In from the far gate: to a typical port, about as far out as the one it left.
            double inbound = Burn(Math.Max(from.Ephemeris.Helio(port).Length * 0.5, 0.05 * Astro.AuKm));
            return outbound + GateNetwork.RouteDays(route) + inbound + 1;
        }

        /// <summary>A system's own working freighters, at home or away.</summary>
        int Freighters(StarSystem home)
        {
            int n = 0;
            foreach (var s in home.World.Fleet.Ships)
                if (!s.Destroyed && !s.IsEscort && s.Role == ShipRole.Trader && s.Spec.Class == ShipClass.Freighter && s.Voyage == null) n++;
            return n + Out(home, VoyagePurpose.Trade);
        }

        /// <summary>A courier with the mail, every so often, when none is out on the run.</summary>
        void DispatchCourier(StarSystem from, StarSystem to, double jd)
        {
            if (Out(from, VoyagePurpose.Courier, to) > 0 || rng.NextDouble() > 0.2) return;
            var world = from.World;
            foreach (var ship in world.Fleet.Ships)
            {
                if (!Idle(world, ship) || ship.Role != ShipRole.Courier || ship.HasCargo) continue;
                var route = Network.Route(from, to, ship.Faction, jd);
                if (route == null || route.Count == 0) continue;
                SetOut(from, to, ship, VoyagePurpose.Courier, route, jd);
                ship.Cargo = "Interstellar mail and data cores for " + to.Name;
                world.Log(jd, $"{ship.Name} takes the mail for {to.Name} by the {route[0].Name}", WorldEventKind.Info, PortOf(ship));
                return;
            }
        }

        /// <summary>Now and then a raider group slips out for another settled system's lanes.</summary>
        void DispatchRaid(StarSystem from, List<StarSystem> settled, double jd)
        {
            var world = from.World;
            if (world.Pirates == null || Out(from, VoyagePurpose.Raid) > 0 || rng.NextDouble() > RaidChancePerDay) return;
            var targets = new List<StarSystem>();
            foreach (var s in settled)
                if (s != from) targets.Add(s);
            if (targets.Count == 0) return;
            var to = targets[rng.Next(targets.Count)];
            foreach (var ship in world.Fleet.Ships)
            {
                if (!Idle(world, ship) || ship.Faction != world.Pirates || ship.Phase != ShipPhase.Parked || ship.Combat.HullDamage > 0.2 || !ship.Combat.Armed) continue;
                var route = Network.Route(from, to, ship.Faction, jd);
                if (route == null || route.Count == 0) continue;
                SetOut(from, to, ship, VoyagePurpose.Raid, route, jd);
                world.Log(jd, $"The {world.Pirates.Name} raiders under {ship.Name} slip away for {to.Name}", WorldEventKind.Info, -1);
                return;
            }
        }

        /// <summary>Now and then a yacht or survey ship sets off to look at an unsettled star, often several gates out.</summary>
        void DispatchSurvey(StarSystem from, double jd)
        {
            if (Out(from, VoyagePurpose.Survey) >= 2 || rng.NextDouble() > SurveyChancePerDay) return;
            // Now and then a survey hull with a drive of its own goes to look at a star off the gate network (U.SPC-T412).
            if (galaxy.NearbyStars.Count > 0 && cruiseRng.NextDouble() < StarSurveyShare && DispatchStarSurvey(from, jd)) return;
            var world = from.World;
            var wild = new List<StarSystem>();
            foreach (var s in galaxy.Systems)
                if (s != from && !s.Settled) wild.Add(s);
            if (wild.Count == 0) return;
            var to = wild[rng.Next(wild.Count)];
            foreach (var ship in world.Fleet.Ships)
            {
                if (!Idle(world, ship) || !IsSurveyHull(ship)) continue;
                var route = Network.Route(from, to, ship.Faction, jd);
                if (route == null || route.Count == 0) continue;
                SetOut(from, to, ship, VoyagePurpose.Survey, route, jd);
                ship.Cargo = "Survey instruments, bound for " + to.Name;
                world.Log(jd, $"{ship.Name} sets out to survey {to.Name}, {route.Count} gate{(route.Count == 1 ? "" : "s")} out", WorldEventKind.Info, PortOf(ship));
                return;
            }
        }

        /// <summary>A free pilot's hull that goes surveying: a fast courier through the gates, or a deep-space explorer
        /// (U.SPC-T419), which can also go off the gate network under its own FTL drive.</summary>
        public static bool IsSurveyHull(Ship ship) =>
            ship.Role == ShipRole.Free && (ship.Spec.Class == ShipClass.Courier || ship.Spec.Class == ShipClass.Explorer);

        /// <summary>A survey hull with a cruise drive sent to a plain star within <see cref="MaxStarSurveyLy"/>; false if none could go.</summary>
        bool DispatchStarSurvey(StarSystem from, double jd)
        {
            var near = new List<NearbyStar>();
            foreach (var n in galaxy.NearbyStars)
                if (n.Origin.DistanceLy(from.Origin) <= MaxStarSurveyLy) near.Add(n);
            if (near.Count == 0) return false;
            var star = near[cruiseRng.Next(near.Count)];
            var world = from.World;
            foreach (var ship in world.Fleet.Ships)
            {
                if (!Idle(world, ship) || !IsSurveyHull(ship) || !CanCruise(ship)) continue;
                if (SendToStar(ship, from, star, VoyagePurpose.Survey, jd) == null) continue;
                ship.Cargo = "Survey instruments, bound for " + star.Name;
                world.Log(jd, $"{ship.Name} sets out under its own drive to survey {star.Name}, " +
                              $"{star.Origin.DistanceLy(from.Origin).ToString("0.0", CultureInfo.InvariantCulture)} ly through open space", WorldEventKind.Info, PortOf(ship));
                return true;
            }
            return false;
        }

        /// <summary>A navy group sent after raiders to the system they came from; false if none could go yet.</summary>
        internal bool DispatchExpedition(StarSystem from, StarSystem to, Faction quarry, double jd)
        {
            var world = from.World;
            var navy = world.Sol;
            if (navy == null || !navy.IsMilitary || Out(from, VoyagePurpose.Expedition) > 0) return Out(from, VoyagePurpose.Expedition) > 0;
            foreach (var ship in world.Fleet.Ships)
            {
                if (!Idle(world, ship) || ship.Faction != navy || !ship.Spec.IsMilitary || ship.PatrolWing || ship.Phase != ShipPhase.Parked) continue;
                if (world.War.IsHomeGuard(ship) || ship.Combat.HullDamage > 0.2 || !ship.Combat.Armed) continue;
                var route = Network.Route(from, to, ship.Faction, jd);
                if (route == null || route.Count == 0) continue;
                var v = SetOut(from, to, ship, VoyagePurpose.Expedition, route, jd);
                v.Quarry = quarry;
                world.Log(jd, $"{ship.Name} leads an expedition through the {route[0].Name} after the {quarry.Name} at {to.Name}", WorldEventKind.Info, PortOf(ship));
                return true;
            }
            return false;
        }

        /// <summary>Ships abroad whose stay is over turn for home, a freighter with a backhaul if one pays.</summary>
        void SendHomeWhenDone(StarSystem system, double jd)
        {
            var world = system.World;
            foreach (var ship in world.Fleet.Ships)
            {
                var v = ship.Voyage;
                if (v == null || ship.Destroyed || ship.IsEscort || v.Homeward || v.AtGate || ship.GateKm != null || system != v.Destination) continue;
                if (ship == world.PilotedShip || ship.Faction != v.Faction)
                {
                    CancelVoyage(system, ship, jd, ship.Faction == v.Faction);
                    continue;
                }
                // A war fleet comes home when its campaign says so (see InterstellarWars).
                if (v.Purpose == VoyagePurpose.Campaign) continue;
                // So do supply ships still bound for their war fleet, when their navy's logistics says so (U.SPC-T525).
                if (v.Purpose == VoyagePurpose.Supply && v.Convoy != null) continue;
                bool hurt = (ship.Spec.IsMilitary || ship.Faction.IsPirate) && ship.Combat.HullDamage > 0.45;
                // A group its polity sent here stays while its objective here stands (U.SPC-T423).
                if (v.Purpose == VoyagePurpose.Directed && !hurt && galaxy.Polities.Command.Holds(ship, world)) continue;
                if (jd < v.StayUntilJd && !hurt && v.Purpose != VoyagePurpose.Directed) continue;
                if (ship.Battle != null || (ship.Phase != ShipPhase.Parked && ship.Phase != ShipPhase.Landed)) continue;
                // A freighter sells what it carries before it goes (unless it has been at it far too long).
                if (ship.HasCargo && v.Purpose == VoyagePurpose.Trade && jd < v.StayUntilJd + 30) continue;
                var route = Network.Route(system, v.Home, ship.Faction, jd);
                ship.StakeoutKm = null;
                if (route == null || route.Count == 0)
                {
                    // No way home through the gates, but a drive of its own (U.SPC-T412): home through open space.
                    if (CanCruise(ship) && system != v.Home) CruiseFrom(system, ship, v.Home, jd);
                    continue;
                }
                // It knows home's markets only by the news that has reached it here (U.SPC-T100).
                var homeNews = News.Known(system, v.Home);
                if (v.Purpose == VoyagePurpose.Trade && !ship.HasCargo && homeNews != null &&
                    BestCargo(system, v.Home, ship, PortOf(ship), route, jd, homeNews.Markets, out var src, out var good, out double tonnes, out _, out _, out _))
                {
                    world.Economy.LoadUnbooked(ship, src, good, tonnes, v.Home.Name);
                    world.Log(jd, $"{ship.Name} loads {Tonnes(tonnes)} {GoodInfo.Of(good).Name} at {src.Name} to take home to {v.Home.Name}", WorldEventKind.Info, src.Body);
                }
                v.Destination = v.Home;
                v.Route = route;
                v.Leg = 0;
                v.ByCruise = false;
                ship.GateKm = route[0].PositionKm;
                world.Fleet.ScheduleNext(ship, jd);
            }
        }
    }
}
