using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// What a war fleet on campaign has left of the three things it burns through (U.SPC-T93), each a share of a full
    /// load (1 as it sets out, 0 empty): propellant for its drives, munitions for its magazines, and the spares it mends
    /// itself with. They drain by the day (marching burns propellant far faster than lying in a system, fighting empties
    /// the magazines, and field repairs eat the spares), and its <see cref="MilitaryLogistics"/> fills them again from a
    /// naval base by supply convoys. Low on any of them the fleet loses <see cref="Readiness"/>, and too low it withdraws.
    /// </summary>
    public sealed class FleetSupply
    {
        public double Propellant = 1, Munitions = 1, Spares = 1;

        /// <summary>No way open over the gates to any naval base of its own (see <see cref="MilitaryLogistics.LineTo"/>), and since when.</summary>
        public bool OutOfSupply;
        public double OutOfSupplySinceJd = double.NaN;

        /// <summary>The naval base it draws on now, or null.</summary>
        public Settlement Base;

        /// <summary>The supply ships on their way to it, or null.</summary>
        public SupplyConvoy Convoy;

        /// <summary>Convoys that reached it, and those that could not get through.</summary>
        public int ConvoysArrived, ConvoysLost;

        /// <summary>Below this share of a full load, a store starts to tell on the fleet's readiness.</summary>
        public const double LowSupply = 0.3;

        /// <summary>A fleet's readiness with a store empty.</summary>
        public const double ReadinessFloor = 0.25;

        /// <summary>A fleet below this readiness, with no convoy on its way, gives up its campaign and goes home.</summary>
        public const double WithdrawReadiness = 0.6;

        /// <summary>A fleet sends for supply ships when its lowest store is below this.</summary>
        public const double ResupplyBelow = 0.75;

        /// <summary>A full load of propellant lasts this many days of marching (under way in a system, or in the gates)...</summary>
        public const double MarchDays = 30;

        /// <summary>...and this many days lying in a system, station-keeping.</summary>
        public const double IdleDays = 150;

        /// <summary>Full magazines last this many days of fighting.</summary>
        public const double BattleDays = 5;

        /// <summary>Its spares wear away over this many days whatever it does (breakdowns, wear).</summary>
        public const double WearDays = 120;

        /// <summary>A day's field repair mends this share of a hull's damage, at full readiness...</summary>
        public const double FieldRepairPerDay = 0.01;

        /// <summary>...and takes this share of the fleet's spares per share mended, per hull.</summary>
        public const double SparesPerRepair = 1.5;

        /// <summary>Readiness for what is left of one store.</summary>
        public static double ReadinessOf(double stock) =>
            stock >= LowSupply ? 1 : ReadinessFloor + (1 - ReadinessFloor) * Math.Max(0, stock) / LowSupply;

        /// <summary>The fleet's readiness: as low as its lowest store makes it (see <see cref="ShipCombat.Readiness"/>).</summary>
        public double Readiness => ReadinessOf(Lowest);

        /// <summary>Its lowest store.</summary>
        public double Lowest => Math.Min(Propellant, Math.Min(Munitions, Spares));

        /// <summary>Too run down to go on, and nothing on its way to it: it must withdraw.</summary>
        public bool MustWithdraw => Readiness < WithdrawReadiness && Convoy == null;

        /// <summary><paramref name="days"/> of campaigning: under way or not, fighting or not.</summary>
        public void Drain(double days, bool moving, bool fighting)
        {
            Propellant = Math.Max(0, Propellant - days / (moving ? MarchDays : IdleDays));
            if (fighting) Munitions = Math.Max(0, Munitions - days / BattleDays);
            Spares = Math.Max(0, Spares - days / WearDays);
        }

        /// <summary>
        /// A day's field repair of one of its <paramref name="hulls"/> carrying <paramref name="damage"/>: the share of it
        /// mended (slower as readiness falls), paid for out of the spares, as far as they go.
        /// </summary>
        public double FieldRepair(double damage, int hulls, double days = 1)
        {
            if (damage <= 0.001 || Spares <= 0) return 0;
            double perShare = SparesPerRepair / Math.Max(1, hulls);
            double share = Math.Min(damage, FieldRepairPerDay * days * Readiness);
            share = Math.Min(share, Spares / perShare);
            Spares = Math.Max(0, Spares - share * perShare);
            return share;
        }

        /// <summary>Takes in what a convoy brings.</summary>
        public void Deliver(SupplyConvoy c)
        {
            Propellant = Math.Min(1, Propellant + c.Propellant);
            Munitions = Math.Min(1, Munitions + c.Munitions);
            Spares = Math.Min(1, Spares + c.Spares);
        }
    }

    /// <summary>
    /// Supply ships a naval base sends after a war fleet (U.SPC-T93): tankers with its propellant, ammunition colliers
    /// with its munitions and spares. Every one is a real hull (U.SPC-T525, <see cref="Hulls"/>): a freighter of its navy's
    /// that sails from the base in formation with the rest, through the gates that will open for it to wherever the fleet
    /// is now, and hands over what it carries only when it comes up with it. A gate cut or shut against them behind the
    /// fleet and they cannot get through; whatever meets them on the way can destroy or take them, and what they carried
    /// goes with them.
    /// </summary>
    public sealed class SupplyConvoy
    {
        public Campaign Campaign;

        /// <summary>The navy whose ships they are.</summary>
        public Faction Faction;

        /// <summary>The naval base it set out from, and its system.</summary>
        public Settlement Base;
        public StarSystem From;

        /// <summary>When it set out, and when it was reckoned to come up with the fleet as it then lay (an estimate: it
        /// delivers when its hulls actually get there).</summary>
        public double SetOutJd, DueJd;

        /// <summary>How many of <see cref="Hulls"/> are tankers (the first ones) and ammunition colliers (the rest).</summary>
        public int Tankers, Colliers;

        /// <summary>
        /// The hulls themselves (U.SPC-T525): ships in their system's fleet, the first leading the rest, the first
        /// <see cref="Tankers"/> of them tankers and the others colliers. Each delivers its share only if it gets there.
        /// </summary>
        public readonly List<Ship> Hulls = new List<Ship>();

        /// <summary>What it carries, as shares of the fleet's full load.</summary>
        public double Propellant, Munitions, Spares;

        /// <summary>What it carries, in tonnes, and what the navy paid for it.</summary>
        public double Tonnes, Cost;

        public override string ToString() => $"{Tankers} tanker{(Tankers == 1 ? "" : "s")} and {Colliers} collier{(Colliers == 1 ? "" : "s")} from {Base?.Name}";
    }

    /// <summary>
    /// Military logistics (U.SPC-T93): the supply of the war fleets out on campaign (see <see cref="InterstellarWars"/>).
    ///
    /// Every fleet burns through its <see cref="FleetSupply"/>: propellant on the march, munitions in battle, spares
    /// mending itself in the field. A navy's naval bases are its shipyard ports; each day a fleet looks for the nearest one
    /// it can reach over gates that will open for it (its own, a neutral's, or enemy gates it has taken), not blockaded or
    /// fought over, and when its stores run down the base sends supply ships after it, bought from the base's own market
    /// (water and hydrocarbons for reaction mass, munitions, alloys and electronics for spares) and paid for from the
    /// navy's treasury: an empty market or an empty treasury sends less, or nothing. So the war draws on the economy, and
    /// the economy's shortages tell on the war.
    ///
    /// A fleet that can reach no base is out of supply. As its stores run low its readiness falls (its guns reload slower,
    /// its crews strike sooner, it mends slower: see <see cref="ShipCombat.Readiness"/>), and below
    /// <see cref="FleetSupply.WithdrawReadiness"/> it gives up the campaign and withdraws. Cutting a gate behind it, or
    /// blockading the base it draws on, starves the fleet.
    /// </summary>
    public sealed class MilitaryLogistics
    {
        readonly Galaxy galaxy;
        readonly List<SupplyConvoy> convoys = new List<SupplyConvoy>();

        /// <summary>Supply convoys on their way.</summary>
        public IReadOnlyList<SupplyConvoy> Convoys => convoys;

        /// <summary>Munitions (t) a hull's magazines hold, full.</summary>
        public const double MunitionsPerHull = 150;

        /// <summary>Alloys and electronics (t) a hull's stores of spares hold, full.</summary>
        public const double AlloysPerHull = 120, ElectronicsPerHull = 20;

        /// <summary>What one tanker and one ammunition collier carry (t).</summary>
        public const double TankerTonnes = 4000, CollierTonnes = 1500;

        /// <summary>Days a base takes to load its supply ships, and to cross a system to a fleet there.</summary>
        public const double LoadingDays = 0.5, CrossingDays = 0.5;

        public MilitaryLogistics(Galaxy galaxy)
        {
            this.galaxy = galaxy;
        }

        Diplomacy Diplomacy => galaxy.Polities.Diplomacy;

        /// <summary>
        /// Whether <paramref name="s"/> serves its owner as a naval base now: a standing shipyard port, not under blockade
        /// and nobody besieging or fighting over it.
        /// </summary>
        public bool BaseOpen(Settlement s, World world)
        {
            if (s == null || s.Fallen || !s.HasShipyard || s.Kind == SettlementKind.Haven) return false;
            var by = Diplomacy.BlockadedBy(s);
            if (by != null && by != s.Owner) return false;
            var besieger = world.War.Besieger(s);
            if (besieger != null && besieger != s.Owner) return false;
            return world.War.BattleAt(s.Body) == null;
        }

        /// <summary>
        /// The naval base of <paramref name="f"/> that can supply a fleet in <paramref name="here"/> soonest, and the way
        /// its supply ships take there (empty if it is in the same system); (null, null) when none can: the fleet is out of supply.
        /// </summary>
        public (Settlement Base, List<JumpGate> Route) LineTo(Faction f, StarSystem here, double jd)
        {
            Settlement best = null;
            List<JumpGate> bestRoute = null;
            double bestDays = double.MaxValue;
            if (f == null || here == null) return (null, null);
            foreach (var s in galaxy.Systems)
            {
                List<JumpGate> route = null;
                bool tried = false;
                foreach (var st in s.World.Economy.Settlements)
                {
                    if (st.Owner != f || !BaseOpen(st, s.World)) continue;
                    if (!tried)
                    {
                        tried = true;
                        route = s == here ? new List<JumpGate>() : galaxy.Network?.Route(s, here, f, jd);
                    }
                    if (route == null) break;
                    double days = GateNetwork.RouteDays(route);
                    if (days < bestDays)
                    {
                        bestDays = days;
                        best = st;
                        bestRoute = route;
                    }
                }
            }
            return (best, bestRoute);
        }

        /// <summary>A campaign's day in the gates: under way, burning propellant.</summary>
        public void InGate(Campaign c, double jd) => c.Supply.Drain(1, moving: true, fighting: false);

        /// <summary>
        /// A day of a campaign's supply, its flagship <paramref name="leader"/> in <paramref name="system"/>: its stores drain
        /// and pay for field repairs, a convoy due comes up with it (or cannot get through), it sends for another when it
        /// runs low and a base can reach it, and its hulls take on its readiness.
        /// </summary>
        public void Day(Campaign c, StarSystem system, Ship leader, double jd)
        {
            var supply = c.Supply;
            var world = system.World;
            var group = Group(world, leader);
            bool fighting = false;
            foreach (var s in group) fighting |= s.Battle != null;
            bool moving = leader.Phase != ShipPhase.Parked || leader.GateKm != null;
            supply.Drain(1, moving, fighting);
            foreach (var s in group)
                if (s.Battle == null)
                {
                    double share = supply.FieldRepair(s.Combat.Damage, group.Count);
                    if (share > 0) s.Combat.Repair(share);
                }

            var (station, route) = LineTo(c.Faction, system, jd);
            supply.Base = station;
            if (station == null && !supply.OutOfSupply)
            {
                supply.OutOfSupply = true;
                supply.OutOfSupplySinceJd = jd;
                world.Log(jd, $"The {c.Faction.Name} war fleet under the {leader.Name} at {system.Name} is cut off from its bases: out of supply", WorldEventKind.Info,
                    system.Catalog.Root.Index);
            }
            else if (station != null && supply.OutOfSupply)
            {
                supply.OutOfSupply = false;
                supply.OutOfSupplySinceJd = double.NaN;
                world.Log(jd, $"The {c.Faction.Name} war fleet under the {leader.Name} is in supply again from {station.Name}", WorldEventKind.Info, system.Catalog.Root.Index);
            }

            var convoy = supply.Convoy;
            if (convoy != null) Track(c, convoy, system, leader, group, jd);
            if (supply.Convoy == null && station != null && supply.Lowest < FleetSupply.ResupplyBelow) Dispatch(c, station, route, system, leader, group, jd);

            double r = supply.Readiness;
            foreach (var s in group) s.Combat.Readiness = r;
        }

        /// <summary>How near (km) a convoy's leading hull must be to the war fleet's flagship to come up with it, when the two
        /// are not lying at the same world.</summary>
        public const double RendezvousKm = 0.01 * Astro.AuKm;

        /// <summary>
        /// A day of a convoy's way to its war fleet (U.SPC-T525), the fleet's flagship <paramref name="leader"/> in
        /// <paramref name="system"/>: lost if every hull of it is gone (destroyed, a hulk, or taken), or if it cannot get
        /// through to where the fleet is now; sent on after the fleet if it has moved to another system; and, its hulls come
        /// up with the fleet, it hands over what it carries.
        /// </summary>
        void Track(Campaign c, SupplyConvoy convoy, StarSystem system, Ship leader, List<Ship> group, double jd)
        {
            if (Alive(convoy, 0, convoy.Hulls.Count) == 0)
            {
                Lost(c, convoy, system, jd, $"The {c.Faction.Name} supply ships ({convoy}) are lost on their way to the war fleet at {system.Name}");
                return;
            }
            var (at, flag) = LeaderOf(convoy);
            if (flag == null)
            {
                // Inside a gate: it is on its way.
                if (InGate(convoy)) return;
                // Its voyage given up (a gate shut against it for good) or its leading hull taken with nobody to carry on.
                Lost(c, convoy, system, jd, $"The {c.Faction.Name} supply ships ({convoy}) cannot get through to the war fleet at {system.Name}: the way is shut to them");
                return;
            }
            var v = flag.Voyage;
            var traffic = galaxy.Traffic;
            if (at == system)
            {
                if (Met(system.World, flag, leader, jd))
                {
                    Arrive(c, convoy, system, leader, group, jd);
                    return;
                }
                // Here, but bound on through a gate for where the fleet was: it stops here, and makes for the fleet.
                if (v.Destination != system) traffic?.Hold(system, flag, jd);
                return;
            }
            // In another system, or bound for one the fleet has left: the way from where it is to where the fleet is now
            // must be open to it, or it cannot get through.
            var way = galaxy.Network?.Route(at, system, c.Faction, jd);
            if (way == null || way.Count == 0)
            {
                Lost(c, convoy, system, jd, $"The {c.Faction.Name} supply ships ({convoy}) cannot get through to the war fleet at {system.Name}: the way behind it is cut");
                return;
            }
            if (v.Destination != system && !v.AtGate && flag.Battle == null) traffic?.Redirect(at, flag, system, jd, force: false);
        }

        /// <summary>
        /// Where a convoy's supply ships make for in <paramref name="system"/> (U.SPC-T525, the traffic's orders for a hull
        /// on a <see cref="VoyagePurpose.Supply"/> voyage): the world its war fleet's flagship lies at or is bound for, or,
        /// with the fleet not here, nowhere (it holds where it is until its navy sends it on).
        /// </summary>
        public int ConvoyOrders(StarSystem system, Ship ship, int here, double jd)
        {
            var convoy = ship.Voyage?.Convoy;
            if (convoy?.Campaign == null) return -1;
            ship.StakeoutKm = null;
            var leader = FlagshipIn(system.World, convoy.Campaign);
            if (leader == null) return here;
            bool parked = leader.Phase == ShipPhase.Parked || leader.Phase == ShipPhase.Landed;
            int port = parked ? InterstellarTraffic.PortOf(leader) : leader.DestinationBody;
            if (port < 0) return here;
            int root = system.Catalog.Root.Index;
            if (port != root) return port;
            // The fleet is out off a gate (holding it, or bound for it): the supply ships fly out to the same spot off the
            // star, as raiders do to lie in wait off one, never to an orbit round the star itself.
            DVec3? spot = leader.GateKm ?? leader.StakeoutKm ??
                          (parked && leader.Orbit != null && leader.Orbit.Body == root ? leader.Orbit.OffsetAt(jd) : (DVec3?)null);
            if (spot == null) return here;
            ship.StakeoutKm = spot;
            // Already off the star, but away from the spot: a real hop out to it, though it starts and ends at one body.
            if (here == root && ship.Orbit != null && (ship.Orbit.OffsetAt(jd) - spot.Value).Length > RendezvousKm) ship.Homing = true;
            return root;
        }

        /// <summary>Whether a convoy's leading hull, in port, has come up with the war fleet's flagship: lying at the same
        /// world, or within <see cref="RendezvousKm"/> of it.</summary>
        static bool Met(World world, Ship flag, Ship leader, double jd)
        {
            if (flag.Battle != null || (flag.Phase != ShipPhase.Parked && flag.Phase != ShipPhase.Landed)) return false;
            if ((leader.Phase == ShipPhase.Parked || leader.Phase == ShipPhase.Landed) &&
                InterstellarTraffic.PortOf(flag) == InterstellarTraffic.PortOf(leader)) return true;
            return (world.Fleet.StateOf(flag, jd).HelioKm - world.Fleet.StateOf(leader, jd).HelioKm).Length <= RendezvousKm;
        }

        /// <summary>A campaign's flagship in <paramref name="world"/>, or null when it is not there.</summary>
        static Ship FlagshipIn(World world, Campaign c)
        {
            foreach (var s in world.Fleet.Ships)
                if (!s.Destroyed && !s.IsEscort && !s.Derelict && s.Faction == c.Faction && s.Voyage != null && s.Voyage.Campaign == c)
                    return s;
            return null;
        }

        /// <summary>The hull leading a convoy now (its first, or an heir if that was lost) and its system; (null, null) when
        /// none is in any system (inside a gate, or none left on the voyage).</summary>
        (StarSystem system, Ship ship) LeaderOf(SupplyConvoy convoy)
        {
            foreach (var s in galaxy.Systems)
                foreach (var ship in s.World.Fleet.Ships)
                    if (!ship.Destroyed && !ship.IsEscort && !ship.Derelict && ship.Faction == convoy.Faction && ship.Voyage?.Convoy == convoy)
                        return (s, ship);
            return (null, null);
        }

        bool InGate(SupplyConvoy convoy)
        {
            if (galaxy.Traffic == null) return false;
            foreach (var p in galaxy.Traffic.InGates)
                if (p.Leader.Voyage?.Convoy == convoy) return true;
            return false;
        }

        /// <summary>How many of a convoy's hulls from <paramref name="from"/> (count <paramref name="count"/>) are still its navy's.</summary>
        static int Alive(SupplyConvoy convoy, int from, int count)
        {
            int n = 0;
            for (int i = from; i < from + count && i < convoy.Hulls.Count; i++)
            {
                var s = convoy.Hulls[i];
                if (s != null && !s.Destroyed && !s.Derelict && s.Faction == convoy.Faction) n++;
            }
            return n;
        }

        /// <summary>A convoy that will never come up with its fleet: written off, and whatever is left of it turns for home.</summary>
        void Lost(Campaign c, SupplyConvoy convoy, StarSystem system, double jd, string why)
        {
            c.Supply.Convoy = null;
            convoys.Remove(convoy);
            c.Supply.ConvoysLost++;
            system.World.Log(jd, why, WorldEventKind.Loss, system.Catalog.Root.Index);
            Release(convoy, jd);
        }

        /// <summary>
        /// A convoy's work is over (delivered, lost or turned back): its hulls are no longer bound for the fleet, and those
        /// still in a system turn for their base's system (home at once if they are there); those inside a gate turn back
        /// when they come out.
        /// </summary>
        void Release(SupplyConvoy convoy, double jd)
        {
            var traffic = galaxy.Traffic;
            if (traffic != null)
                foreach (var p in traffic.InGates)
                    if (p.Leader.Voyage?.Convoy == convoy) p.Leader.Voyage.Convoy = null;
            var bound = new List<(StarSystem system, Ship ship)>();
            foreach (var s in galaxy.Systems)
                foreach (var ship in s.World.Fleet.Ships)
                    if (ship.Voyage?.Convoy == convoy) bound.Add((s, ship));
            foreach (var (s, ship) in bound)
            {
                var v = ship.Voyage;
                v.Convoy = null;
                ship.StakeoutKm = null;
                ship.Homing = false;
                if (ship.Destroyed || ship.Derelict || ship.IsEscort || ship.Faction != v.Faction || traffic == null) continue;
                // Home now, or the way home; with no way open, it lies where it is and the traffic tries again later.
                if (v.AtGate) traffic.Hold(s, ship, jd);
                if (traffic.Redirect(s, ship, v.Home, jd, force: false)) continue;
                v.Destination = s;
                v.Route = new List<JumpGate>();
                v.Leg = 0;
                v.StayUntilJd = jd;
            }
        }

        /// <summary>The convoy comes up with the fleet: its hulls that got there hand over what they carry.</summary>
        void Arrive(Campaign c, SupplyConvoy convoy, StarSystem system, Ship leader, List<Ship> group, double jd)
        {
            var supply = c.Supply;
            supply.Convoy = null;
            convoys.Remove(convoy);
            var world = system.World;
            // Each tanker and collier brings its own share: those lost on the way bring nothing.
            int tankers = Alive(convoy, 0, convoy.Tankers), colliers = Alive(convoy, convoy.Tankers, convoy.Colliers);
            double tankShare = convoy.Tankers > 0 ? (double)tankers / convoy.Tankers : 0;
            double collierShare = convoy.Colliers > 0 ? (double)colliers / convoy.Colliers : 0;
            convoy.Propellant *= tankShare;
            convoy.Munitions *= collierShare;
            convoy.Spares *= collierShare;
            supply.Deliver(convoy);
            supply.ConvoysArrived++;
            world.Log(jd, $"The {c.Faction.Name} supply ships from {convoy.Base?.Name} come up with the war fleet under the {leader.Name} at {system.Name}: " +
                          $"{tankers} tanker{(tankers == 1 ? "" : "s")} and {colliers} collier{(colliers == 1 ? "" : "s")} hand over their loads",
                WorldEventKind.Info, InterstellarTraffic.PortOf(leader));
            foreach (var s in convoy.Hulls)
                if (s != null && !s.Destroyed && s.Faction == convoy.Faction) s.Cargo = "Empty, back to " + (convoy.Base?.Name ?? "base");
            Release(convoy, jd);
            // Tanks topped up and magazines filled from what the fleet now has aboard.
            foreach (var s in group)
            {
                s.Propellant = Math.Max(s.Propellant, s.PropellantCapacity * supply.Propellant);
                foreach (var sub in s.Combat.Subsystems)
                    if (sub.Def.Weapon != null && sub.Def.Weapon.Missile && !sub.Destroyed)
                        sub.Ammo = Math.Max(sub.Ammo, (int)Math.Round(sub.Def.Weapon.Ammo * supply.Munitions));
            }
        }

        /// <summary>
        /// A naval base sends supply ships after a fleet: what it needs to fill its stores, as far as the base's market has
        /// it and the navy can pay, on the way <paramref name="route"/>.
        /// </summary>
        void Dispatch(Campaign c, Settlement station, List<JumpGate> route, StarSystem system, Ship leader, List<Ship> group, double jd)
        {
            var f = c.Faction;
            if (f.Treasury <= 0) return;
            var supply = c.Supply;
            int hulls = group.Count;
            double capacity = 0;
            foreach (var s in group) capacity += s.PropellantCapacity;

            double propNeed = (1 - supply.Propellant) * capacity;
            double munNeed = (1 - supply.Munitions) * MunitionsPerHull * hulls;
            double alloysNeed = (1 - supply.Spares) * AlloysPerHull * hulls;
            double elecNeed = (1 - supply.Spares) * ElectronicsPerHull * hulls;
            var stock = station.Stock;
            var price = station.Price;
            // The economy feels what the war asks of it, whether or not it can give it.
            station.ExtraDemand[(int)Good.Water] += propNeed;
            station.ExtraDemand[(int)Good.Munitions] += munNeed;
            station.ExtraDemand[(int)Good.Alloys] += alloysNeed;
            station.ExtraDemand[(int)Good.Electronics] += elecNeed;

            double massHave = stock[(int)Good.Water] + stock[(int)Good.Hydrocarbons];
            double propShare = propNeed > 0 ? Math.Min(1, massHave / propNeed) : 0;
            double munShare = munNeed > 0 ? Math.Min(1, stock[(int)Good.Munitions] / munNeed) : 0;
            double sparesShare = alloysNeed > 0 ? Math.Min(1, Math.Min(stock[(int)Good.Alloys] / alloysNeed, stock[(int)Good.Electronics] / Math.Max(elecNeed, 1e-9))) : 0;

            // Worked out, and the trip judged worth sending, before anything is actually taken off the market: Take()
            // mutates the station's stock as it goes, so finding out only afterward that there is nothing worth a
            // convoy over would already have thrown away stock the fleet never got and nobody ever paid for.
            double prop = propNeed * propShare;
            double mun = munNeed * munShare;
            double spares = (alloysNeed + elecNeed) * sparesShare;
            if (prop + mun + spares < 1) return;
            // The supply ships sail from the base's own system (U.SPC-T525): no traffic to fly them, no convoy.
            var from = SystemOf(station) ?? system;
            if (galaxy.Traffic == null || (from != system && (route == null || route.Count == 0))) return;
            int tankers = prop > 0 ? (int)Math.Ceiling(prop / TankerTonnes) : 0;
            int colliers = mun + spares > 0 ? (int)Math.Ceiling((mun + spares) / CollierTonnes) : 0;
            if (tankers + colliers == 0) return;

            double cost = 0;
            double fromWater = Math.Min(prop, stock[(int)Good.Water]);
            cost += Take(station, Good.Water, fromWater) + Take(station, Good.Hydrocarbons, prop - fromWater);
            cost += Take(station, Good.HeliumThree, prop * Propulsion.HeliumThreePerTonne);
            cost += Take(station, Good.Munitions, mun);
            cost += Take(station, Good.Alloys, alloysNeed * sparesShare) + Take(station, Good.Electronics, elecNeed * sparesShare);
            f.Treasury -= cost;

            var convoy = new SupplyConvoy
            {
                Campaign = c, Faction = f, Base = station, From = from, SetOutJd = jd,
                DueJd = jd + LoadingDays + GateNetwork.RouteDays(route) + CrossingDays,
                Tankers = tankers,
                Colliers = colliers,
                Propellant = (1 - supply.Propellant) * propShare,
                Munitions = (1 - supply.Munitions) * munShare,
                Spares = (1 - supply.Spares) * sparesShare,
                Tonnes = prop + mun + spares, Cost = cost,
            };
            Muster(convoy, station, from, jd);
            // Fresh from port, with its way checked above, the leading hull always sails.
            galaxy.Traffic.SendSupply(convoy.Hulls[0], from, system, route, convoy, jd);
            supply.Convoy = convoy;
            convoys.Add(convoy);
            var baseWorld = from.World;
            baseWorld.Log(jd, $"{station.Name} sends {convoy.Tankers} tanker{(convoy.Tankers == 1 ? "" : "s")} and {convoy.Colliers} ammunition collier{(convoy.Colliers == 1 ? "" : "s")} after the {f.Name} war fleet at {system.Name}",
                WorldEventKind.Launch, station.Body);
        }

        // ---------------------------------------------------------------- the supply ships' hulls (U.SPC-T525)

        /// <summary>Names a navy gives its tankers and its ammunition colliers, in turn.</summary>
        static readonly string[] TankerNames = { "Wellspring", "Deep Well", "Cistern", "Freshet", "Reservoir", "Brimming", "Spring Tide", "Aquifer" };
        static readonly string[] CollierNames = { "Powder Hoy", "Magazine", "Quartermaster", "Sutler", "Armourer", "Stores", "Bandolier", "Caisson" };

        int tankersNamed, colliersNamed;

        /// <summary>
        /// Gives <paramref name="convoy"/> its hulls (U.SPC-T525): the navy's supply ships lying idle in the base's system
        /// first, then new ones fitted out at the base for the rest. The first <see cref="SupplyConvoy.Tankers"/> are its
        /// tankers; the first leads, and the rest take station on it in formation.
        /// </summary>
        void Muster(SupplyConvoy convoy, Settlement station, StarSystem from, double jd)
        {
            var world = from.World;
            var fleet = world.Fleet;
            var f = convoy.Faction;
            int need = convoy.Tankers + convoy.Colliers;
            // The navy's supply ships in port here with nothing to do: each its own master again (a convoy home from a run
            // still lies in formation on the hull that led it).
            var idle = new List<Ship>();
            foreach (var s in fleet.Ships)
            {
                if (s.Destroyed || s.Derelict || s.Faction != f || s.Battle != null || s.Voyage != null || s.GateKm != null || s == world.PilotedShip) continue;
                if (world.Holdings != null && world.Holdings.Owns(s)) continue;
                var head = s.Controller;
                if (head.Role != ShipRole.Supply || head.Voyage != null || head.Battle != null || head.GateKm != null) continue;
                if ((head.Phase != ShipPhase.Parked && head.Phase != ShipPhase.Landed) || head.Orbit == null) continue;
                // Lying at this base: one at another world of the system is not here to load.
                if (InterstellarTraffic.PortOf(head) != station.Body) continue;
                idle.Add(s);
            }
            // Every one its own master first, so none left out of this convoy goes along on a leader that is in it.
            foreach (var s in idle)
                if (s.IsEscort) Stand(s, station, world, jd);
            foreach (var s in idle)
                if (convoy.Hulls.Count < need) convoy.Hulls.Add(s);
            while (convoy.Hulls.Count < need)
            {
                bool tanker = convoy.Hulls.Count < convoy.Tankers;
                string name = tanker ? Named(TankerNames, ref tankersNamed) : Named(CollierNames, ref colliersNamed);
                var s = fleet.AddShip(name, ShipClass.Freighter, f, ShipRole.Supply);
                s.CapacityTonnes = Economy.CapacityOf(ShipClass.Freighter);
                Stand(s, station, world, jd);
                world.NotifyAdded(s);
                convoy.Hulls.Add(s);
            }
            var flag = convoy.Hulls[0];
            for (int i = 0; i < convoy.Hulls.Count; i++)
            {
                var s = convoy.Hulls[i];
                s.Cargo = i < convoy.Tankers
                    ? $"Reaction mass for the {f.Name} war fleet"
                    : $"Munitions and spares for the {f.Name} war fleet";
                if (i > 0) fleet.AddEscort(flag, s, Fleet.SlotFor(i - 1));
            }
        }

        static string Named(string[] names, ref int named)
        {
            string name = names[named % names.Length] + (named >= names.Length ? " " + (named / names.Length + 1) : "");
            named++;
            return name;
        }

        /// <summary>A supply ship its own master, lying at its naval base: there between runs, and making for it if elsewhere.</summary>
        static void Stand(Ship s, Settlement station, World world, double jd)
        {
            s.Leader = null;
            s.Role = ShipRole.Supply;
            s.Route = null;
            s.Ports = new[] { station.Body };
            world.Fleet.Station(s, station.Body, jd);
        }

        /// <summary>Buys up to <paramref name="tonnes"/> of <paramref name="g"/> off <paramref name="s"/>'s market: what it cost.</summary>
        static double Take(Settlement s, Good g, double tonnes)
        {
            tonnes = Math.Min(Math.Max(0, tonnes), s.Stock[(int)g]);
            s.Stock[(int)g] -= tonnes;
            return tonnes * s.Price[(int)g];
        }

        /// <summary>A campaign is over: whatever is still on its way to it turns back.</summary>
        public void Ended(Campaign c, double jd = double.NaN)
        {
            var convoy = c.Supply.Convoy;
            c.Supply.Convoy = null;
            if (convoy == null) return;
            convoys.Remove(convoy);
            // Its supply ships, wherever they are, turn for home with what they carry (U.SPC-T525).
            if (double.IsNaN(jd)) jd = convoy.From != null && !double.IsNaN(convoy.From.World.Jd) ? convoy.From.World.Jd : convoy.SetOutJd;
            Release(convoy, jd);
        }

        StarSystem SystemOf(Settlement st)
        {
            var world = galaxy.Polities.WorldOf(st);
            foreach (var s in galaxy.Systems)
                if (s.World == world) return s;
            return null;
        }

        /// <summary>A fleet's flagship and the hulls in its group.</summary>
        static List<Ship> Group(World world, Ship leader)
        {
            var list = new List<Ship> { leader };
            foreach (var s in world.Fleet.Ships)
                if (s != leader && s.Leader == leader && !s.Destroyed) list.Add(s);
            return list;
        }
    }
}
