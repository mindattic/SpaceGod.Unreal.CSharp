using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Everything that happens in the system, advanced together in date order: the fleet's
    /// schedules, the markets, and the wars. The orbital mechanics are real; the settlements,
    /// factions and their economy are the game layer, placed on worlds that plausibly hold what they
    /// produce (ice moons sell water, Titan hydrocarbons, Io copper and zinc from its sulphide flows).
    /// </summary>
    public sealed partial class World
    {
        public Ephemeris Ephemeris { get; }
        public BodyCatalog Catalog => Ephemeris.Catalog;
        public Fleet Fleet { get; }
        public Economy Economy { get; }
        /// <summary>The interstellar polities this world's factions belong to (U.SPC-T91): shared by every system of a galaxy.</summary>
        public Polities Polities { get; }

        /// <summary>Who is at war with whom: the one table every system shares (see <see cref="Polities.Diplomacy"/>).</summary>
        public Diplomacy Diplomacy => Polities.Diplomacy;
        public War War { get; private set; }

        /// <summary>Stations, yards, factories and spacecraft in orbit, and the docking rings over populous worlds.</summary>
        public Infrastructure Infrastructure { get; private set; }

        public Faction Sol, Belt, Reach, Independents, Pirates;

        /// <summary>The two powers whose wars and ceasefires come and go (Sol: the Federation and the Reach).</summary>
        public Faction RivalA, RivalB;

        /// <summary>Names the pirates give their raiders; null for the default list.</summary>
        public string[] RaiderNames;

        /// <summary>The merchant fleet yards keep up before navies get their slips, and the most they build to.</summary>
        public int MerchantTarget = 100, MerchantCap = 170;

        /// <summary>How many warships a navy keeps in peace and in war.</summary>
        public int NavyCap = 20, NavyCapAtWar = 34;

        /// <summary>The most raider groups the pirates field, and what smuggling and rackets bring them a day.</summary>
        public int PirateGroupCap = 4;
        public double PirateIncomePerDay = 1.5e6;

        /// <summary>Things worth telling an observer about (a battle, a capture, a new hull), newest last.</summary>
        public readonly List<WorldEvent> Events = new List<WorldEvent>();

        /// <summary>The ship under direct player control, if any; its schedule is suspended while flown
        /// (see <see cref="Fleet.IsHeld"/>) and a pilot-flight controller owns its position instead.</summary>
        public Ship PilotedShip;

        /// <summary>The dynamic event system: adventures near the player drawn from what the sim is doing
        /// (see <see cref="EventDirector"/>), advanced by <see cref="UpdateAdventures"/>.</summary>
        public EventDirector Adventures { get; private set; }

        /// <summary>Economic events driven by the sim (accidents, worked-out seams, blights, strikes, war rationing,
        /// blockades and embargoes) and the price spikes they and the war cause, reported in <see cref="Events"/>.</summary>
        public MarketShocks Shocks { get; }
        /// <summary>Law, crime and reputation: jurisdictions, the player's and every captain's record, bounties and
        /// the patrols that act on them (see <see cref="Core.Law"/>).</summary>
        public Law Law { get; private set; }

        /// <summary>Sensors, signatures and stealth: what each faction's ships and stations, and the player's own ship,
        /// can see of every other ship (see <see cref="Core.Sensors"/>).</summary>
        public Sensors Sensors { get; private set; }

        /// <summary>Named outlaw captains, the ports' bounty boards and the bounty hunters who work them (see <see cref="Core.Outlaws"/>).</summary>
        public Outlaws Outlaws { get; private set; }

        /// <summary>Hostile groups running the piloted ship down (U.SPC-T273).</summary>
        public Pursuit Pursuit { get; private set; }

        /// <summary>The trading companies that own and run the merchant fleet (U.SPC-T99; see <see cref="Core.Companies"/>).</summary>
        public Companies Companies { get; private set; }

        /// <summary>The stations' contracts boards: freight, passengers and colonists, escorts, patrols, surveys and
        /// bounties posted from what the system needs, and who has taken them (see <see cref="Core.Contracts"/>).</summary>
        public Contracts Contracts { get; private set; }

        /// <summary>The merchant fleet's fighter escorts (U.SPC-T275).</summary>
        public MerchantEscorts Escorts { get; private set; }

        /// <summary>The player's own fleet and property: owned ships under hired captains' standing orders, warehouses,
        /// station shares and mining claims, and the house account their earnings go to (see <see cref="Core.Holdings"/>).</summary>
        public Holdings Holdings { get; private set; }

        /// <summary>The player's trading career (U.SPC-T426): price intelligence, trade news, charters, insurance, the
        /// back door and the trader's rank (see <see cref="Core.TradeDesk"/>).</summary>
        public TradeDesk Trade { get; private set; }

        /// <summary>Factions founding new settlements as their people run out of room (U.SPC-T98).</summary>
        public Colonisation Colonisation { get; private set; }

        /// <summary>The hyperlanes' upkeep and the lane tenders that keep them running (U.SPC-T481, see <see cref="Core.LaneUpkeep"/>).</summary>
        public LaneUpkeep LaneUpkeep { get; private set; }

        /// <summary>The most bounty hunter groups the free traders keep in service.</summary>
        public int HunterCap = 3;

        /// <summary>Raised when a ship joins the fleet after the start (a yard finished it, or it came through a gate).</summary>
        public event Action<Ship> ShipAdded;

        /// <summary>Raised when a ship bound for a jump gate reaches it (see <see cref="Ship.GateKm"/> and <see cref="InterstellarTraffic"/>).</summary>
        public event Action<Ship, double> ReachedGate;

        /// <summary>
        /// Orders for a ship here on an interstellar voyage (raiders from another system, a navy's
        /// expedition): the body to go to next, or -1 to fall back on the usual choice.
        /// </summary>
        public Func<Ship, int, double, int> VoyageOrders;

        /// <summary>This system's jump gates (see <see cref="StarSystem.Gates"/>); empty for a world on its own.</summary>
        public readonly List<JumpGate> Gates = new List<JumpGate>();

        /// <summary>
        /// Hides in this system's belts and discs where no haven stands yet (body indices): where a pirate clan from
        /// elsewhere can found one (U.SPC-T94, see <see cref="FoundHaven"/>).
        /// </summary>
        public readonly List<int> HavenSites = new List<int>();

        int builtCount;

        World(Ephemeris ephemeris, double startJd, int seed, Polities polities)
        {
            Ephemeris = ephemeris;
            Polities = polities ?? new Polities();
            Polities.Register(this);
            Fleet = new Fleet(ephemeris, seed);
            Economy = new Economy(Diplomacy, startJd, seed + 1);
            Adventures = new EventDirector(seed + 5);
            Shocks = new MarketShocks(this, seed + 7);
            Adventures.Spawned += op => Log(op.SpawnJd, op.MarkerLabel + ": " + op.Hook, WorldEventKind.Opportunity, op.Ship?.Orbit?.Body ?? op.Body);
            Adventures.Resolved += op => Log(op.ResolvedJd, op.MarkerLabel + (op.State == OpportunityState.Completed ? " done: " : " lost: ") + op.Outcome,
                WorldEventKind.Opportunity, op.Ship?.Orbit?.Body ?? op.Body);
        }

        double nextRecountJd;

        /// <summary>The date the world has been run to (NaN before its first <see cref="Update"/>).</summary>
        public double Jd { get; private set; } = double.NaN;

        /// <summary>
        /// Run at a coarse level of detail (U.SPC-T104): a system the player is not in, stepped every few hours
        /// rather than every frame (see <see cref="Galaxy.Focus"/>). Trade, industry and the markets are
        /// integrated over the step as they always are; ships keep their real schedules but fly without
        /// sidestepping one another and plan their windows on a coarser grid (<see cref="Fleet.Coarse"/>); and
        /// battles are fought out in steps of <see cref="Battle.CoarseStep"/> seconds rather than one. Nothing is
        /// summarised away, so a world promoted back to full detail carries on from exactly where it stands.
        /// </summary>
        public bool Coarse
        {
            get => Fleet.Coarse;
            set => Fleet.Coarse = value;
        }

        public void Update(double jd)
        {
            Jd = jd;
            Fleet.Update(jd);
            Economy.AdvanceTo(jd);
            Companies.Update(jd);
            War.Update(jd);
            LaneUpkeep?.Update(jd);
            Sensors?.Update(jd);
            Infrastructure?.UpdateSatellites(jd);
            Shocks.Update(jd);
            Law.Update(jd);
            Outlaws.Update(jd);
            Contracts.Update(jd);
            Escorts?.Update(jd);
            Holdings.Update(jd);
            Trade?.Update(jd);
            Mining?.Update(jd);
            ReplaceWorksEscorts(jd);
            if (jd >= nextRecountJd)
            {
                Economy.RecountInbound(Fleet.Ships);
                nextRecountJd = jd + 1;
            }
        }

        /// <summary>
        /// Advances the dynamic event system to <paramref name="jd"/> around the player, who is at
        /// <paramref name="playerHelioKm"/> (the piloted ship, or the camera when only watching); only a
        /// piloted ship (<see cref="PilotedShip"/>) can arrive on the scene and collect.
        /// </summary>
        public void UpdateAdventures(double jd, DVec3 playerHelioKm)
        {
            if (War == null) return;
            Adventures.Update(jd, EventSources.Of(this, jd), playerHelioKm, PilotedShip);
        }

        /// <summary>Where a wreck is now, heliocentric km: fixed in its body's frame, or beside the lane it died by.</summary>
        public DVec3 WreckHelio(Wreck w, double jd)
        {
            var lanes = Fleet.Lanes;
            if (w.Site == null || lanes == null) return Ephemeris.HelioAt(w.Body, jd) + w.Pos;
            var hop = w.Site.Hop;
            var a = Ephemeris.HelioAt(hop.From, jd) + w.Site.FromOffset;
            var b = Ephemeris.HelioAt(hop.To, jd) + w.Site.ToOffset;
            return lanes.CurveKm(hop.Lane, a, b, w.Site.Progress, jd) + w.Site.Side + w.Local;
        }

        /// <summary>Where an escape pod cluster is now, heliocentric km: drifting in its body's frame, or beside the lane its hull died by.</summary>
        public DVec3 PodHelio(EscapePod pod, double jd)
        {
            var lanes = Fleet.Lanes;
            if (pod.Site == null || lanes == null) return Ephemeris.HelioAt(pod.Body, jd) + pod.PosAt(jd);
            var hop = pod.Site.Hop;
            var a = Ephemeris.HelioAt(hop.From, jd) + pod.Site.FromOffset;
            var b = Ephemeris.HelioAt(hop.To, jd) + pod.Site.ToOffset;
            return lanes.CurveKm(hop.Lane, a, b, pod.Site.Progress, jd) + pod.Site.Side + pod.LocalAt(jd);
        }

        /// <summary>
        /// The player's ship (<paramref name="ship"/>) comes alongside drifting escape pods and takes them aboard:
        /// a friend brings them home, a pirate holds them for ransom, an enemy takes them prisoner (see
        /// <see cref="Crews.Collect"/>). The player is paid through the adventure, not the owners' rescue fee.
        /// </summary>
        public PodState PickUpPods(EscapePod pod, Ship ship, double jd)
        {
            if (pod == null || ship == null || War == null) return pod?.State ?? PodState.Lost;
            return War.Crews.Collect(pod, ship.Faction, ship, Diplomacy, jd, payRescuer: false);
        }

        /// <summary>
        /// The player's ship lets go of a hulk (or has just claimed it): the clamps come off, what came of it
        /// goes in the event log, and the derelict or wreck adventure on it, if one is open, is seen through
        /// (<see cref="EventDirector.HulkWorked"/>). Returns that adventure, or null.
        /// </summary>
        public Opportunity FinishSalvage(SalvageSession session, double jd)
        {
            if (session == null) return null;
            session.CastOff();
            if (!session.Worked) return null;
            int body = session.Derelict?.Orbit?.Body ?? session.Wreck?.Body ?? -1;
            Log(jd, $"The {session.Player.Name} {session.Summary()} aboard {session.Name}", session.Claimed ? WorldEventKind.Capture : WorldEventKind.Info, body);
            return Adventures?.HulkWorked(session, jd);
        }

        public void NotifyAdded(Ship ship) => ShipAdded?.Invoke(ship);

        /// <summary>Raised for every entry in <see cref="Events"/> as it is logged (the log itself keeps only the latest 400).</summary>
        public event Action<WorldEvent> Logged;

        public void Log(double jd, string text, WorldEventKind kind = WorldEventKind.Info, int body = -1)
        {
            var e = new WorldEvent(jd, text, kind, body);
            Events.Add(e);
            if (Events.Count > 400) Events.RemoveRange(0, Events.Count - 400);
            Logged?.Invoke(e);
        }

        public bool IsTrader(Ship s) => !s.IsEscort && (s.Role == ShipRole.Trader || s.Role == ShipRole.Miner || s.Role == ShipRole.Courier);

        /// <summary>The chance any one civilian transit springs a leak and leaves the ship to run dry (see <see cref="Fleet.LeakChance"/>).</summary>
        public const double PropellantLeakChance = 0.003;

        /// <summary>
        /// Ships whose transits burn real propellant from their tanks (see <see cref="Propulsion"/>): every civilian
        /// hull in its own system. One on an interstellar voyage is not charged on the way: its run in to a jump gate
        /// hard by the star is a direct crossing of the whole system, far past any tank, flown on the gate network's
        /// own approach (it pays the gates' tolls instead) until fuel for it is modelled too.
        /// </summary>
        public bool BurnsPropellant(Ship s) => !s.IsEscort && !s.Spec.IsMilitary && s.Voyage == null && !s.PatrolWing && s.Role != ShipRole.Commuter &&
                                               s.Role != ShipRole.Tender && s.Role != ShipRole.Tug && s.Role != ShipRole.Hauler;

        /// <summary>
        /// A ship joins a hyperlane: civilian traffic pays the ride's toll (<see cref="Economy.LaneToll"/>, by its
        /// all-up mass) to whoever keeps the system's ramps, the owner of its biggest settlement. Navies and
        /// raiders ride free; so does the player's ship (the helm is local flight, never a lane), and a lane tender at its work.
        /// </summary>
        void PayToll(Ship ship, LaneHop hop, double jd)
        {
            // An ore tug on its works' heavy lanes rides at its works' charge, like a tender at its upkeep (U.SPC-T498).
            if (ship.Spec.IsMilitary || ship.PatrolWing || ship.Faction.IsPirate || ship == PilotedShip || ship.Role == ShipRole.Tender || ship.Role == ShipRole.Tug ||
                ship.Role == ShipRole.Hauler) return;
            double toll = Economy.LaneToll(ship.Spec.Class, ship.MassTonnes);
            ship.Faction.Treasury -= toll;
            ship.Credits -= toll;
            var keeper = LaneKeeper(hop.From);
            if (keeper != null) keeper.Treasury += toll;
        }

        /// <summary>Who keeps a planetary system's hyperlane ramps (and takes their tolls): the owner of its most populous settlement, or nobody.</summary>
        public Faction LaneKeeper(int system)
        {
            var lanes = Fleet.Lanes;
            Settlement best = null;
            foreach (var s in Economy.Settlements)
            {
                if (s.Fallen || s.Owner == null || s.Owner.IsPirate || lanes == null || lanes.SystemOf(s.Body) != system) continue;
                if (best == null || s.Population > best.Population) best = s;
            }
            return best?.Owner;
        }

        // ---------------------------------------------------------------- polities

        /// <summary>
        /// Founds the polity called <paramref name="name"/> here, or, if another system already has, gives it a presence
        /// here (U.SPC-T91): it is the same faction, so this system's holdings add their revenues to its one treasury
        /// (<paramref name="treasury"/> is added either way) and its ships join its one navy. Its marines, colours and
        /// kind are those it was founded with.
        /// </summary>
        public Faction Polity(string name, string prefix, Rgb color, PolityKind kind, double treasury, double marines = 1)
        {
            var f = Polities.Find(name);
            if (f == null)
            {
                f = new Faction(name, prefix, color) { Kind = kind, MarineQuality = marines };
                f.IsPirate = kind == PolityKind.PirateClan;
                Polities.Found(f, this);
            }
            f.Treasury += treasury;
            return Fleet.AddFaction(f);
        }

        /// <summary>People a clan's newly founded haven starts with.</summary>
        public const double FoundedHavenPopulation = 5e3;

        /// <summary>
        /// A pirate clan founds a haven here (U.SPC-T94): a hidden hold at <paramref name="body"/> (one of the
        /// <see cref="HavenSites"/>), feeding itself, with one gun platform, and the clan gains a presence in this system.
        /// Where no clan of its own holds a haven here, this one's raiders are now the system's: new bands sail from the
        /// haven (one at a time) and a bounty hunter can be fitted out to go after them.
        /// </summary>
        public Settlement FoundHaven(string name, int body, Faction clan, double jd)
        {
            if (clan == null || !clan.IsPirate) return null;
            Fleet.AddFaction(clan);
            var s = Economy.Add(name, body, SettlementKind.Haven, clan, FoundedHavenPopulation);
            s.Hidden = true;
            Subsist(s, (st, g, perDay) => st.Facilities.Add(new Facility(Recipe.Mine(g, perDay), 1)),
                (st, k, level) => st.Facilities.Add(new Facility(Recipe.Of(k), level)));
            Economy.Open(s);
            // Stocked for a start, as havens are at the beginning.
            for (int i = 0; i < GoodInfo.Count; i++) s.Stock[i] = s.Target((Good)i);
            s.RefreshPrices();
            s.MaxPlatforms = 1;
            s.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            HavenSites.Remove(body);
            bool ownClan = false;
            foreach (var h in Economy.Settlements)
                if (h != s && h.Kind == SettlementKind.Haven && !h.Fallen && h.Owner == Pirates) ownClan = true;
            if (!ownClan) Pirates = clan;
            PirateGroupCap = Math.Max(PirateGroupCap, 1);
            PirateIncomePerDay = Math.Max(PirateIncomePerDay, 2e5);
            HunterCap = Math.Max(HunterCap, 1);
            Log(jd, $"A new raider hold is whispered of somewhere in the dark: {clan.Name} have come to {Catalog.Root.Name}", WorldEventKind.Info, -1);
            return s;
        }

        /// <summary>The raiders of a system with no clan of its own: one unknown, the same in every such system.</summary>
        Faction UnknownRaiders() => Polity("Unknown", "", new Rgb(0.8f, 0.3f, 0.3f), PolityKind.PirateClan, 0);

        /// <summary>Gives each polity present here and still without a seat its capital: its most populous settlement.</summary>
        void SeatCapitals()
        {
            foreach (var f in Fleet.Factions)
                if (f.Capital == null) Polities.CapitalOf(f);
        }

        /// <summary>
        /// Every hull is crewed; from here on new ones draw on a real reserve of trained spacers, mustered from every
        /// settlement a polity holds in any system (a polity joined here is mustered again, with this system's people).
        /// </summary>
        void MusterReserves() => Crews.MusterReserves(Fleet.Factions, Polities.Settlements);

        /// <summary>Who holds this system (see <see cref="Polities.HolderOf"/>).</summary>
        public Faction Holder => Polities.HolderOf(this);

        // ---------------------------------------------------------------- the default system

        public static World CreateDefault(Ephemeris ephemeris, double jd, int seed = 1977, Polities polities = null)
        {
            double startJd = jd - 45;
            var w = new World(ephemeris, startJd, seed, polities);
            var fleet = w.Fleet;
            var economy = w.Economy;

            // The core: the Federation governs Earth, the Moon and Mars, and from Earth the frontier it holds beyond the gates.
            w.Sol = w.Polity(SolFederation, "SFS", SolFederationColor, PolityKind.State, 4e9, 1.25);
            w.Belt = w.Polity("Belt Consortium", "BCS", new Rgb(1f, 0.6f, 0.25f), PolityKind.State, 2.5e9, 1.0);
            w.Reach = w.Polity("Outer Reach Compact", "ORS", new Rgb(0.72f, 0.5f, 1f), PolityKind.State, 3.5e9, 1.15);
            w.Independents = w.Polity("Independent", "MV", new Rgb(0.85f, 0.85f, 0.78f), PolityKind.FreeTraders, 6e8, 0.6);
            w.Pirates = w.Polity("Black Banner", "", new Rgb(0.95f, 0.22f, 0.2f), PolityKind.PirateClan, 2e8, 1.1);
            w.Sol.IsMilitary = w.Belt.IsMilitary = w.Reach.IsMilitary = true;
            w.Diplomacy.Set(w.Sol, w.Reach, Relation.War);
            w.RivalA = w.Sol;
            w.RivalB = w.Reach;

            w.BuildSettlements();
            w.Wire(startJd, seed, Hyperlanes.CreateDefault(ephemeris));
            var war = w.War;
            w.Infrastructure.AddSolSpacecraft();
            w.Infrastructure.AddSolLagrangeStations(w.Sol, w.Belt);
            w.Infrastructure.AddSolRings(w.Economy);
            w.Fleet.Rings.AddRange(w.Infrastructure.Rings);

            w.BuildMilitary(startJd);
            w.BuildMerchants(startJd, FreighterNames, MinerNames, CourierNames, new[] { w.Independents, w.Independents, w.Sol, w.Belt, w.Reach, w.Independents },
                i => i % 3 == 2 ? w.Reach : w.Belt);
            w.FoundCompanies(startJd,
                ("Kepler Freight Lines", "KFL", "Earth Orbital", w.Sol, false),
                ("Ares Haulage", "AH", "Ares Prime", w.Sol, true),
                ("Jovian Bulk Carriers", "JBC", "Ganymede Union", w.Belt, false),
                ("Vesta Ore and Haulage", "VOH", "Ceres Hub", w.Belt, true),
                ("Kraken Hydrocarbon Company", "KHC", "Kraken Port", w.Reach, false),
                ("Meridian Free Traders", "MFT", "Tycho City", w.Independents, false),
                ("Long Haul Carrying Company", "LHC", "Ares Prime", w.Independents, false));
            w.AddFreeShips(startJd, new[] { "Kestrel's Folly", "Wanderlust", "Solitaire", "Driftwood", "Errant", "Idle Hours" },
                new[] { "Prospector Nine", "Lucky Strike" }, new[] { "Far Horizon", "Stargazer" });
            int haven = w.Catalog["Deimos"].Index;
            war.RaisePirateGroup(startJd, haven);
            // The Banner's second band sails from its biggest belt hideout; recruiting fills the other
            // havens first (it raises each new band at the standing haven with the fewest).
            war.RaisePirateGroup(startJd, w.Catalog[BeltHavens[0].body].Index);
            // Each navy's standing patrol wings fly a beat of its ports (U.SPC-T371).
            PatrolWings.Raise(w, startJd);
            // And its ports' ordinary traffic: buses, taxis and people's own craft (U.SPC-T375).
            LocalTraffic.Raise(w, startJd);
            // Its belt works and the builds they feed (U.SPC-T330).
            w.SeedSolMining(jd);
            w.SeatCapitals();
            w.MusterReserves();

            w.Update(jd);
            return w;
        }

        /// <summary>The polity that holds the core: Sol, and the frontier at Barnard's Star.</summary>
        public const string SolFederation = "Sol Federation";

        public static readonly Rgb SolFederationColor = new Rgb(0.35f, 0.65f, 1f);

        /// <summary>Connects the markets, the wars and the fleet's schedule (the same in every system).</summary>
        void Wire(double startJd, int seed, Hyperlanes lanes)
        {
            var economy = Economy;
            var fleet = Fleet;
            economy.Initialise();
            War = new War(this, startJd, seed + 2);
            var war = War;
            Sensors = new Sensors(this);
            // The player's standing is one table: adventures seen through raise it, crimes lower it.
            Law = new Law(this, Adventures.Reputation);
            // The law judges a battle first; then the outlaws' records take in what their bands did in it.
            Outlaws = new Outlaws(this, seed + 11);
            Pursuit = new Pursuit(this, seed + 13);
            war.BattleStarted += Outlaws.OnBattleStarted;
            war.BattleEnded += Law.OnBattleEnded;
            war.BattleEnded += Outlaws.OnBattleEnded;
            Law.Committed += Outlaws.OnCrime;
            war.Crews.Resolved += Outlaws.OnPodResolved;
            // The boards post from the markets, migration, the war's danger and the law's books (U.SPC-T101).
            Contracts = new Contracts(this, seed + 17);
            Escorts = new MerchantEscorts(this, seed + 19);
            war.BattleEnded += Contracts.OnBattleEnded;
            // Each subsystem its own offset: two on one seed would draw the very same "random" numbers in step.
            Holdings = new Holdings(this, seed + 23);
            Trade = new TradeDesk(this, seed + 41);
            WireMining(seed + 31);
            economy.DockAllowed = Law.AllowsDocking;
            economy.PriceFactor = Law.PriceFactorFor;
            Infrastructure = new Infrastructure(Catalog, Ephemeris);
            Infrastructure.AddSettlementStructures(economy, seed + 3);
            WireColonisation();
            // Merchants and couriers go down to the surface through the rings; warships and pirates hold orbit, and so does a
            // bulk freighter, built in space and never landing: it trades from orbit (U.SPC-T482).
            fleet.Lands = s => IsTrader(s) && !BulkFreighters.NeverLands(s);

            fleet.Lanes = lanes;
            // The lanes wear, and the tenders their keepers raise keep them up (U.SPC-T481).
            LaneUpkeep = new LaneUpkeep(this, seed + 43);
            // Drive-appropriate trajectories: torches flip and burn, slow boats fall along transfer orbits.
            fleet.OrbitalTransfers = true;
            economy.EstimateTravelDays = fleet.EstimateTravelDays;
            // Civilian hulls fly on real propellant and buy it in port; traders price every run by it.
            // (Navies' own supply -- tankers and depots -- is a war-logistics matter, left to their stores for now.)
            fleet.BurnsPropellant = BurnsPropellant;
            fleet.CanRefuelAt = economy.HasPropellantAt;
            fleet.ForceTopUp = (ship, body, jd) => economy.Refuel(ship, body, jd, force: true);
            economy.EstimateTrip = fleet.EstimateTrip;
            fleet.RanDry = (ship, when) =>
                Log(when, $"{ship.Name} has run its tanks dry off {Catalog[ship.Orbit.Body].Name} and calls for a tanker", WorldEventKind.Info, ship.Orbit.Body);
            fleet.TankerArrived = (ship, tonnes, when) =>
            {
                if (tonnes <= 0) return;
                double fee = Propulsion.TankerFee(tonnes);
                ship.Faction.Treasury -= fee;
                ship.Credits -= fee;
                Log(when, $"A tanker reaches {ship.Name} with {tonnes:N0} t of reaction mass", WorldEventKind.Info, ship.Orbit.Body);
            };
            fleet.LaneJoined = (ship, hop, when) =>
            {
                PayToll(ship, hop, when);
                // A worn lane's rings may slip under the rider (U.SPC-T481).
                LaneUpkeep.OnLaneJoined(ship, hop, when);
            };
            // Now and then a tank is holed on the way: the ship runs dry and calls for help.
            fleet.LeakChance = PropellantLeakChance;
            fleet.Leaked = (ship, tonnes, when) =>
                Log(when, $"{ship.Name} vents {tonnes:N0} t of reaction mass from a holed tank", WorldEventKind.Info, ship.Orbit?.Body ?? -1);
            economy.Danger = body => war.Danger(body) * 0.08;
            // The merchant fleet belongs to trading companies, which buy hulls from their profits: a yard lays down a
            // company's order first, then the navies take their slips. A system without companies keeps the old way:
            // the yards' owners keep the merchant fleet up to strength before the navies get theirs.
            Companies = new Companies(this, seed + 29);
            economy.Loaded += Companies.OnLoaded;
            economy.Sold += Companies.OnSold;
            economy.RouteFactor = Companies.RouteFactor;
            economy.PaidFor = Companies.PaidFor;
            ShipAdded += Companies.OnShipAdded;
            // Each polity's production focus steers which of its industries grow (U.SPC-T423).
            economy.Priority = (s, kind) => Polities.Command.Priority(s.Owner, kind);
            // A lane short of its tenders comes first: everything else rides the lanes (U.SPC-T481).
            economy.ChooseBuild = yard => LaneUpkeep.OrderAt(yard) ?? (Companies.Enabled
                ? Requisition(yard) ?? Companies.OrderAt(yard) ?? war.ChooseWarship(yard)
                : TraderCount() < MerchantTarget ? ChooseBuild(yard) : war.ChooseWarship(yard) ?? ChooseBuild(yard));
            economy.ShipBuilt = OnShipBuilt;
            economy.CanCrew = (yard, cls) => Crews.CanCrew(yard.Owner, cls);
            fleet.BeforeEvent = economy.AdvanceTo;
            // A group running the player down is flown by the chase, not its schedule (U.SPC-T273).
            fleet.IsHeld = ship => ship.Battle != null || ship.Derelict || ship == PilotedShip || (ship.Voyage != null && ship.Voyage.AtGate) ||
                                   (Pursuit != null && Pursuit.Active && Pursuit.Of(ship) != null);
            // The player's captains keep their own time in port (mining a claim, waiting on a charge).
            fleet.Dwell = (ship, normal) => Holdings.Owns(ship) ? Holdings.Dwell(ship, normal)
                : ship.Role == ShipRole.Tender ? LaneUpkeep.Dwell(ship)
                : ship.Role == ShipRole.Tug || ship.Role == ShipRole.Hauler ? normal : war.Dwell(ship, normal);
            fleet.Arrived = (ship, when) =>
            {
                // At a jump gate, not a port: the interstellar traffic takes it from here.
                if (ship.GateKm != null && ship.Orbit.Body == Catalog.Root.Index)
                {
                    ReachedGate?.Invoke(ship, when);
                    return;
                }
                // A freighter bound for a jump gate carries goods for another system's markets: it keeps them through
                // any port it passes on the way to the gate (selling there skipped the gate's toll and the far market).
                bool bound = ship.Voyage != null && ship.GateKm != null;
                // A colony ship's hull lands its colonists where they are bound, and is broken up there for a new settlement (U.SPC-T524).
                if (ship.Role == ShipRole.Colonist && ColonyHullArrived(ship, when)) return;
                // Contract cargo and people go to the contract, not the market (U.SPC-T101).
                // An ore tug's works latches its load on out in the field, or takes it in where it is for (U.SPC-T498).
                if (ship.Role == ShipRole.Tug) TugArrived(ship, when);
                // A works' freighter empties its hold into the warehouse its convoy is for (U.SPC-T523).
                if (ship.Role == ShipRole.Hauler) FreighterArrived(ship, when);
                bool contracted = Contracts.OnArrived(ship, ship.Orbit.Body, when);
                if (IsTrader(ship) && !bound && !contracted) economy.OnArrive(ship, ship.Orbit.Body, when);
                war.OnArrived(ship, when);
            };
            fleet.ReachedRing = war.OnArrived;
            // Merchants heading into a fight that has just broken out turn aside for somewhere safe.
            war.BattleStarted += b => DivertAround(b, b.StartJd);
            // A free ship does not wander to a known haven, or an armed port, of someone hostile to it: their platforms
            // would fire on it every time it came back (U.SPC-T291).
            fleet.Shuns = (ship, body) =>
            {
                // Local traffic keeps clear of a fight (U.SPC-T375).
                if (ship.Role == ShipRole.Commuter && War.BattleAt(body) != null) return true;
                foreach (var s in economy.At(body))
                    if ((s.Kind == SettlementKind.Haven || s.Platforms.Count > 0) && Diplomacy.Hostile(ship.Faction, s.Owner)) return true;
                return false;
            };
            fleet.ChooseNext = (ship, here, when) =>
            {
                // In port, a ship on propellant tops up its tanks before it plans the next run.
                if (BurnsPropellant(ship)) economy.Refuel(ship, here, when);
                if (ship.Voyage != null && VoyageOrders != null)
                {
                    int ordered = VoyageOrders(ship, here, when);
                    if (ordered >= 0) return ordered;
                }
                // A colony ship's hull flies its voyage: to the site, or home with colonists it could not land (U.SPC-T524).
                if (ship.Role == ShipRole.Colonist)
                {
                    int colonyNext = ColonyHullNext(ship, here, when);
                    if (colonyNext >= 0) return colonyNext;
                }
                // A lane tender works its lane (U.SPC-T481).
                if (ship.Role == ShipRole.Tender && !Holdings.Owns(ship)) return LaneUpkeep.ChooseNext(ship, here, when);
                // An ore tug goes where its belt works sends it (U.SPC-T498).
                if (ship.Role == ShipRole.Tug && !Holdings.Owns(ship))
                {
                    int tugNext = Mining?.TugNext(ship, here) ?? -1;
                    return tugNext >= 0 ? tugNext : here;
                }
                // A works' freighter goes where its belt works sends it (U.SPC-T523).
                if (ship.Role == ShipRole.Hauler && !Holdings.Owns(ship))
                {
                    int haulerNext = Mining?.FreighterNext(ship, here) ?? -1;
                    return haulerNext >= 0 ? haulerNext : here;
                }
                // A clan's smuggler lies at its haven between runs (U.SPC-T94).
                if (ship.Runner) return ship.Route != null && ship.Route.Length > 0 ? ship.Route[0] : here;
                // The player's own ships go where their captains' standing orders take them.
                if (Holdings.Owns(ship)) return Holdings.ChooseNext(ship, here, when);
                // Local traffic (U.SPC-T375) keeps to its line or its whims, the fleet's own choice.
                if (ship.Role == ShipRole.Commuter) return -1;
                // A merchant, courier or free ship on a contract goes where it takes it; an idle one may take one here.
                if (IsTrader(ship) || ship.Role == ShipRole.Free)
                {
                    int job = Contracts.ChooseNext(ship, here, when);
                    if (job >= 0) return job;
                }
                // Free ships follow their own whims (the fleet's built-in choice), not orders or markets -- unless
                // their tanks are getting low, when even an idle wanderer heads for somewhere to fill them.
                if (ship.Role == ShipRole.Free)
                    return ship.PropellantFraction < 0.4 && !economy.HasPropellantAt(ship, here) ? economy.NearestPropellant(ship, here, when) : -1;
                if (!IsTrader(ship)) return war.ChooseNext(ship, here, when);
                int next = economy.PlanTrade(ship, here, when);
                if (next < 0 && !ship.HasCargo) ship.Cargo = "Empty, looking for work";
                return next;
            };
        }

        /// <summary>
        /// Colonisation (U.SPC-T98): every world of the system surveyed as a site, colony ships timed by the
        /// distance at the date they sail (a freighter's flip-and-burn, as the contract boards reckon it), and
        /// the news of each voyage, founding, build-out, promotion and failure in the log.
        /// </summary>
        void WireColonisation()
        {
            var c = Colonisation = new Colonisation();
            c.SurveyAll(Catalog);
            c.TravelDays = (from, body, when) =>
                Contracts.TravelDays(from.Body == body ? 0 : (Ephemeris.HelioAt(from.Body, when) - Ephemeris.HelioAt(body, when)).Length);
            c.Launched = v => Log(v.DepartJd, v.Reinforcing != null
                ? $"The colony ship {v.Name} sails from {v.From.Name} with {v.Colonists:N0} settlers for {v.Reinforcing.Name}"
                : $"The colony ship {v.Name} sails from {v.From.Name} with {v.Colonists:N0} colonists to settle {v.Site.Name}" +
                  (v.Abroad ? $" in {v.Where}, through the gates" : ""), WorldEventKind.Launch, v.From.Body);
            c.Landed = (v, s) => Log(Economy.Jd, s != null
                ? $"The colony ship {v.Name} lands {v.Colonists:N0} settlers at {s.Name}"
                : $"The colony ship {v.Name} finds {v.Site.Name}{(v.Abroad ? " in " + v.Where : "")} taken and carries its colonists home",
                WorldEventKind.Info, v.Abroad ? v.From.Body : v.Body);
            c.Founded = s =>
            {
                // Settlers from another system bring their polity here with them (U.SPC-T293).
                Fleet.AddFaction(s.Owner);
                Log(Economy.Jd, $"{s.Owner.Name} colonists found {s.Name}, an outpost of {s.Population:N0} " +
                                (Catalog[s.Body].HabitatSite ? "in a new habitat" : "on " + Catalog[s.Body].Name), WorldEventKind.Economy, s.Body);
            };
            c.Expanded = s => Log(Economy.Jd, $"{s.Name} builds out its habitat for {s.InitialPopulation * People.MaxGrowth:N0} people", WorldEventKind.Economy, s.Body);
            c.Promoted = s => Log(Economy.Jd, $"{s.Name} has grown from an outpost into a colony of {s.Population:N0}", WorldEventKind.Economy, s.Body);
            c.Ruined = s => Log(Economy.Jd, $"{s.Name} has failed: its last settlers leave and it stands empty, a ruin", WorldEventKind.Loss, s.Body);
            WireColonyHulls(c);
            Economy.Colonisation = c;
        }

        // ---------------------------------------------------------------- Alpha Centauri

        /// <summary>
        /// Alpha Centauri, a century or so after settlement: the same machinery as Sol, but young and
        /// thin. The Centauri Accord governs the Proxima colonies with a small navy; the Toliman
        /// Combine, a corporate concern, runs the stations around A and B with no warships of its own;
        /// Kentaurus Free Traders carry most of the freight; and the Long Dark raid the long lanes
        /// from a derelict station out by Proxima. All fiction, on real (and candidate) worlds.
        /// </summary>
        public static World CreateAlphaCentauri(Ephemeris ephemeris, double jd, int seed = 4243, Polities polities = null)
        {
            double startJd = jd - 45;
            var w = new World(ephemeris, startJd, seed, polities);
            var fleet = w.Fleet;

            // The Centauri government, its own state beyond the Federation.
            var accord = w.Sol = w.Polity("Centauri Accord", "CAS", new Rgb(0.3f, 0.88f, 0.75f), PolityKind.State, 9e8, 1.15);
            var combine = w.Belt = w.Polity("Toliman Combine", "TCV", new Rgb(1f, 0.78f, 0.3f), PolityKind.Company, 7e8, 0.8);
            w.Independents = w.Polity("Kentaurus Free Traders", "KFT", new Rgb(0.78f, 0.84f, 0.92f), PolityKind.FreeTraders, 1.2e8, 0.6);
            w.Pirates = w.Polity("Long Dark", "", new Rgb(0.85f, 0.25f, 0.6f), PolityKind.PirateClan, 8e7, 1.2);
            accord.IsMilitary = true;
            // Rivals, not enemies: the hook for a war later.
            w.RivalA = accord;
            w.RivalB = combine;
            w.MerchantTarget = 50;
            w.MerchantCap = 80;
            w.NavyCap = 10;
            w.NavyCapAtWar = 16;
            // A thin system feeds few raiders.
            w.PirateGroupCap = 2;
            w.PirateIncomePerDay = 4e5;
            w.HunterCap = 1;
            w.RaiderNames = new[]
            {
                "Lightless", "Event Horizon", "Cold Sleep", "Red Shift", "Penumbra", "Dead Air", "Null Return", "Last Light",
                "Occultation", "Silent Running", "Umbral", "Long Fall",
            };

            w.BuildCentauriSettlements();
            w.Wire(startJd, seed, Hyperlanes.CreateAlphaCentauri(ephemeris));
            var war = w.War;
            w.Infrastructure.AddCentauriRings(w.Economy);
            w.Fleet.Rings.AddRange(w.Infrastructure.Rings);

            fleet.Group(accord, startJd, ShipClass.Battleship, "Terminator", new[] { "Proxima b", "Proxima d", "Proxima c" },
                (ShipClass.Cruiser, "Long Noon"), (ShipClass.Destroyer, "Flare"), (ShipClass.Destroyer, "Ember"));
            fleet.Group(accord, startJd, ShipClass.Cruiser, "Centaur", new[] { "Proxima b", "Rigil Gate", "Toliman Forge" },
                (ShipClass.Destroyer, "Hadar"), (ShipClass.Destroyer, "Agena"));

            string[] freighters =
            {
                "Second Sun", "Tidal Lock", "Far Side", "Kentaur Pride", "Twilight Band", "Nine Lights", "Slow Fuse",
                "Toliman's Due", "Red Noon", "Barycentre",
            };
            string[] haulers = { "Dross", "Cinder", "Ore Moth", "Spoil" };
            string[] couriers = { "Long Shot", "Lightspeed Promise" };
            w.BuildMerchants(startJd, freighters, haulers, couriers,
                new[] { w.Independents, w.Independents, combine, w.Independents, accord }, i => combine);

            w.Companies.MaxCompanies = 5;
            w.FoundCompanies(startJd,
                ("Kentaurus Carrying Company", "KCC", "New Kentaurus", w.Independents, false),
                ("Toliman Bulk", "TB", "Toliman Forge", combine, true),
                ("Proxima Line", "PL", "New Kentaurus", accord, false));
            w.AddFreeShips(startJd, new[] { "First Light", "Terminator Dawn" }, new[] { "Red Dust" }, new[] { "Long Look" });
            int haven = w.Catalog["Blackhollow"].Index;
            war.RaisePirateGroup(startJd, haven);
            PatrolWings.Raise(w, startJd);
            // And its ports' ordinary traffic: buses, taxis and people's own craft (U.SPC-T375).
            LocalTraffic.Raise(w, startJd);
            w.SeatCapitals();
            w.MusterReserves();

            w.Update(jd);
            return w;
        }

        /// <summary>
        /// A system nobody has settled yet: real stars and worlds, the same machinery, but no
        /// settlements, lanes or ships. Two factions exist only so the game layer has someone to
        /// hand a ship to (a survey service and the raiders it will one day meet); both are fiction.
        /// </summary>
        public static World CreateUnsettled(Ephemeris ephemeris, double jd, int seed = 1, Polities polities = null)
        {
            double startJd = jd - 45;
            var w = new World(ephemeris, startJd, seed, polities);
            // One survey service and one unknown for every empty system: they are the same polities everywhere.
            w.Sol = w.Independents = w.Polity("Survey Service", "SSV", new Rgb(0.7f, 0.85f, 0.95f), PolityKind.FreeTraders, 0);
            w.Pirates = w.UnknownRaiders();
            w.MerchantTarget = w.MerchantCap = 0;
            w.NavyCap = w.NavyCapAtWar = 0;
            w.PirateGroupCap = 0;
            w.PirateIncomePerDay = 0;
            w.HunterCap = 0;
            w.Wire(startJd, seed, new Hyperlanes(ephemeris));
            w.Update(jd);
            return w;
        }

        void BuildCentauriSettlements()
        {
            var e = Economy;
            int B(string name) => Catalog[name].Index;
            void Mine(Settlement s, Good g, double perDay) => s.Facilities.Add(new Facility(Recipe.Mine(g, perDay), 1));
            void Make(Settlement s, IndustryKind k, double level) => s.Facilities.Add(new Facility(Recipe.Of(k), level));

            // The Accord: Proxima's worlds.
            var kentaurus = e.Add("New Kentaurus", B("Proxima b"), SettlementKind.Colony, Sol, 400e3);
            Mine(kentaurus, Good.Water, 2500);
            Mine(kentaurus, Good.Nitrogen, 700);
            Mine(kentaurus, Good.Hydrocarbons, 300);
            Make(kentaurus, IndustryKind.PharmaLab, 2);
            Make(kentaurus, IndustryKind.MunitionsPlant, 1);
            Make(kentaurus, IndustryKind.Shipyard, 0.8);

            var ember = e.Add("Ember Station", B("Proxima d"), SettlementKind.MiningOutpost, Sol, 30e3);
            Mine(ember, Good.NickelIron, 4500);
            Mine(ember, Good.Titanium, 220);
            Mine(ember, Good.Copper, 450);
            Mine(ember, Good.Zinc, 120);
            Make(ember, IndustryKind.Smelter, 1);

            var farReach = e.Add("Far Reach", B("Proxima c"), SettlementKind.Station, Sol, 20e3);
            Mine(farReach, Good.Water, 4000);
            Mine(farReach, Good.Nitrogen, 800);
            Mine(farReach, Good.HeliumThree, 8);

            // The Combine: stations around A and B.
            var rigil = e.Add("Rigil Gate", B("Rigil Gate"), SettlementKind.Station, Belt, 120e3);
            Make(rigil, IndustryKind.ElectronicsFab, 1.5);
            Make(rigil, IndustryKind.MachineWorks, 1);
            Make(rigil, IndustryKind.PharmaLab, 1);

            var aurora = e.Add("Aurora Skimmer", B("Alpha Centauri Ab"), SettlementKind.Station, Belt, 15e3);
            Mine(aurora, Good.HeliumThree, 30);
            Mine(aurora, Good.Hydrocarbons, 800);

            var forge = e.Add("Toliman Forge", B("Toliman Forge"), SettlementKind.Station, Belt, 80e3);
            // Ore from B's debris (fiction: no asteroid belt is known there).
            Mine(forge, Good.NickelIron, 1500);
            Mine(forge, Good.Titanium, 120);
            Mine(forge, Good.RareEarths, 40);
            Mine(forge, Good.Platinum, 4);
            Make(forge, IndustryKind.Smelter, 1.5);
            Make(forge, IndustryKind.MachineWorks, 1);
            Make(forge, IndustryKind.Shipyard, 1);

            e.Add("Blackhollow", B("Blackhollow"), SettlementKind.Haven, Pirates, 8e3);

            foreach (var s in e.Settlements) Subsist(s, Mine, Make);
            // The Combine has no navy, so its stations are well gunned.
            foreach (var s in e.Settlements)
            {
                s.MaxPlatforms = s.Population >= 400e3 ? 3 : s.Owner == Belt && s.Population >= 50e3 ? 3 : s.Kind == SettlementKind.Haven ? 1 : 2;
                for (int i = 0; i < s.MaxPlatforms; i++) s.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            }
        }

        void BuildSettlements()
        {
            var e = Economy;
            int B(string name) => Catalog[name].Index;
            void Mine(Settlement s, Good g, double perDay) => s.Facilities.Add(new Facility(Recipe.Mine(g, perDay), 1));
            void Make(Settlement s, IndustryKind k, double level) => s.Facilities.Add(new Facility(Recipe.Of(k), level));

            // Sol Federation: Earth's orbital industry and the inner worlds.
            var earth = e.Add("Earth Orbital", B("Earth"), SettlementKind.Colony, Sol, 12e6);
            Mine(earth, Good.Water, 82000);
            Mine(earth, Good.Nitrogen, 3500);
            Mine(earth, Good.Hydrocarbons, 1500);
            Make(earth, IndustryKind.Farm, 48);
            Make(earth, IndustryKind.PharmaLab, 12);
            Make(earth, IndustryKind.ElectronicsFab, 7);
            Make(earth, IndustryKind.MachineWorks, 3);
            Make(earth, IndustryKind.MunitionsPlant, 3);
            Make(earth, IndustryKind.Shipyard, 2);

            var luna = e.Add("Tycho City", B("Moon"), SettlementKind.Colony, Sol, 500e3);
            Mine(luna, Good.HeliumThree, 45);
            Mine(luna, Good.Titanium, 400);
            Mine(luna, Good.RareEarths, 70);
            Mine(luna, Good.Platinum, 6);
            Mine(luna, Good.Water, 1500);
            Make(luna, IndustryKind.Farm, 1.5);

            var mercury = e.Add("Caloris Works", B("Mercury"), SettlementKind.MiningOutpost, Sol, 100e3);
            Mine(mercury, Good.NickelIron, 6500);
            Mine(mercury, Good.Copper, 700);
            Mine(mercury, Good.Titanium, 300);
            Make(mercury, IndustryKind.Smelter, 2);

            var venus = e.Add("Aphrodite Aerostat", B("Venus"), SettlementKind.Station, Sol, 150e3);
            Mine(venus, Good.Nitrogen, 1500);
            Mine(venus, Good.Hydrocarbons, 1200);
            Make(venus, IndustryKind.PharmaLab, 4);

            var mars = e.Add("Ares Prime", B("Mars"), SettlementKind.Colony, Sol, 1.5e6);
            Mine(mars, Good.Water, 10500);
            Mine(mars, Good.NickelIron, 4200);
            Mine(mars, Good.Copper, 500);
            Mine(mars, Good.Zinc, 200);
            Make(mars, IndustryKind.Farm, 6);
            Make(mars, IndustryKind.Smelter, 1.5);
            Make(mars, IndustryKind.MunitionsPlant, 2);
            Make(mars, IndustryKind.Shipyard, 1.5);

            var phobos = e.Add("Stickney Depot", B("Phobos"), SettlementKind.Station, Sol, 25e3);
            Mine(phobos, Good.Water, 900);
            Mine(phobos, Good.Hydrocarbons, 400);

            e.Add("Blackwater", B("Deimos"), SettlementKind.Haven, Pirates, 15e3);

            // The Banner's hidden belt bases, off the lanes on real asteroids, unknown until someone finds them.
            foreach (var (name, body, people) in BeltHavens)
                e.Add(name, B(body), SettlementKind.Haven, Pirates, people).Hidden = true;
            // And one out in Jupiter's trailing Trojan camp, on the twin Patroclus-Menoetius.
            foreach (var (name, body, people) in TrojanHavens)
                e.Add(name, B(body), SettlementKind.Haven, Pirates, people).Hidden = true;

            // Belt Consortium: Ceres and the Jovian moons.
            var ceres = e.Add("Ceres Hub", B("Ceres"), SettlementKind.Colony, Belt, 700e3);
            Mine(ceres, Good.Water, 9000);
            Mine(ceres, Good.NickelIron, 9000);
            Mine(ceres, Good.Platinum, 15);
            Mine(ceres, Good.RareEarths, 50);
            Make(ceres, IndustryKind.Smelter, 3);
            Make(ceres, IndustryKind.Farm, 2.5);
            Make(ceres, IndustryKind.MunitionsPlant, 1.5);
            Make(ceres, IndustryKind.Shipyard, 1.5);

            var europa = e.Add("Conamara Station", B("Europa"), SettlementKind.Station, Belt, 120e3);
            Mine(europa, Good.Water, 14000);

            var ganymede = e.Add("Ganymede Union", B("Ganymede"), SettlementKind.Colony, Belt, 800e3);
            Mine(ganymede, Good.Water, 6500);
            Mine(ganymede, Good.Titanium, 250);
            Make(ganymede, IndustryKind.Farm, 5);
            Make(ganymede, IndustryKind.ElectronicsFab, 4);
            Make(ganymede, IndustryKind.MachineWorks, 3);
            Make(ganymede, IndustryKind.PharmaLab, 3);
            Make(ganymede, IndustryKind.Shipyard, 2);

            var callisto = e.Add("Valhalla Mines", B("Callisto"), SettlementKind.MiningOutpost, Belt, 150e3);
            Mine(callisto, Good.NickelIron, 7000);
            Mine(callisto, Good.Water, 2000);
            Make(callisto, IndustryKind.Smelter, 2);

            var io = e.Add("Loki Sulphide Works", B("Io"), SettlementKind.MiningOutpost, Belt, 40e3);
            Mine(io, Good.Copper, 2000);
            Mine(io, Good.Zinc, 600);

            // The Greek camp at Jupiter's L4: dark, primitive Trojans rich in ices and organics, mined
            // for volatiles and a little metal (game layer, on real rocks; see Core/Systems/SOURCES.md).
            var hektor = e.Add("Skamandrios Diggings", B("Hektor"), SettlementKind.MiningOutpost, Belt, 30e3);
            Mine(hektor, Good.Hydrocarbons, 900);
            Mine(hektor, Good.Water, 1800);
            Mine(hektor, Good.Nitrogen, 300);
            Mine(hektor, Good.NickelIron, 1200);

            var eurybates = e.Add("Queta Camp", B("Eurybates"), SettlementKind.MiningOutpost, Belt, 12e3);
            Mine(eurybates, Good.NickelIron, 900);
            Mine(eurybates, Good.RareEarths, 25);
            Mine(eurybates, Good.Water, 600);

            // Outer Reach Compact: Saturn and beyond.
            var titan = e.Add("Kraken Port", B("Titan"), SettlementKind.Colony, Reach, 500e3);
            Mine(titan, Good.Hydrocarbons, 2500);
            Mine(titan, Good.Nitrogen, 2000);
            Mine(titan, Good.Water, 3500);
            Make(titan, IndustryKind.Farm, 3);
            Make(titan, IndustryKind.PharmaLab, 4);
            Make(titan, IndustryKind.MunitionsPlant, 3);
            Make(titan, IndustryKind.Shipyard, 2);

            var enceladus = e.Add("Tiger Stripe", B("Enceladus"), SettlementKind.Station, Reach, 40e3);
            Mine(enceladus, Good.Water, 6000);
            Mine(enceladus, Good.Hydrocarbons, 500);

            var titania = e.Add("Gertrude Base", B("Titania"), SettlementKind.Station, Reach, 60e3);
            Mine(titania, Good.HeliumThree, 50);
            Mine(titania, Good.Water, 1500);

            var oberon = e.Add("Oberon Deep", B("Oberon"), SettlementKind.MiningOutpost, Reach, 40e3);
            Mine(oberon, Good.RareEarths, 150);
            Mine(oberon, Good.Titanium, 250);
            Mine(oberon, Good.Platinum, 6);

            var triton = e.Add("Triton Station", B("Triton"), SettlementKind.Station, Reach, 70e3);
            Mine(triton, Good.Nitrogen, 1200);
            Mine(triton, Good.HeliumThree, 35);
            Mine(triton, Good.Water, 1000);

            var charon = e.Add("Charon Outpost", B("Charon"), SettlementKind.MiningOutpost, Reach, 30e3);
            Mine(charon, Good.Water, 1500);
            Mine(charon, Good.Nitrogen, 600);

            foreach (var s in e.Settlements) Subsist(s, Mine, Make);

            // Orbital defence platforms: the big colonies are fortresses, outposts have one or none.
            foreach (var s in e.Settlements)
            {
                // A haven's garrison: two gun platforms over the rock.
                s.MaxPlatforms = s.Kind == SettlementKind.Haven ? 2 : s.Population >= 5e6 ? 4 : s.Population >= 1e6 ? 3 : s.Population >= 400e3 ? 2 : 1;
                for (int i = 0; i < s.MaxPlatforms; i++) s.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            }
        }

        /// <summary>The hidden pirate havens of Sol's main belt: name, asteroid, people.</summary>
        public static readonly (string name, string body, double people)[] BeltHavens =
        {
            ("Hollow Iron", "Psyche", 6e3),
            ("Lantern Rock", "Lutetia", 4e3),
            ("Dactyl's Rest", "Ida", 3e3),
        };

        /// <summary>The hidden pirate havens of Jupiter's Trojan swarms: name, Trojan, people.</summary>
        public static readonly (string name, string body, double people)[] TrojanHavens =
        {
            ("Menoetius Hold", "Patroclus", 5e3),
        };

        /// <summary>
        /// Every settlement grows most of its own food and melts most of its own water (a haven all of
        /// it, since nobody else will deliver there); imports and supply contracts cover the rest.
        /// </summary>
        static void Subsist(Settlement s, Action<Settlement, Good, double> mine, Action<Settlement, IndustryKind, double> make)
        {
            double share = s.Kind == SettlementKind.Haven ? 1.1 : 0.92;
            var farm = Recipe.Of(IndustryKind.Farm);
            double food = 0, water = 0, nitrogen = 0;
            foreach (var f in s.Facilities)
            {
                foreach (var (g, rate) in f.Recipe.Outputs)
                {
                    if (g == Good.Food) food += rate * f.Level;
                    if (g == Good.Water) water += rate * f.Level;
                    if (g == Good.Nitrogen) nitrogen += rate * f.Level;
                }
                foreach (var (g, rate) in f.Recipe.Inputs)
                {
                    if (g == Good.Water) water -= rate * f.Level;
                    if (g == Good.Nitrogen) nitrogen -= rate * f.Level;
                }
            }
            double foodNeed = s.Population * People.NeedOf(s.Kind, Good.Food) * share;
            if (food < foodNeed)
            {
                double level = (foodNeed - food) / farm.Outputs[0].perDay;
                make(s, IndustryKind.Farm, level);
                water -= farm.Inputs[0].perDay * level;
                nitrogen -= farm.Inputs[1].perDay * level;
            }
            double waterNeed = s.Population * People.NeedOf(s.Kind, Good.Water) * share;
            if (water < waterNeed) mine(s, Good.Water, (waterNeed - water) * 1.05);
            // Air leaks away: the nitrogen to top it up is a need like the farms' own.
            nitrogen -= s.Population * People.NeedOf(s.Kind, Good.Nitrogen) * share;
            // The people's share is already in the need above, and the farms want all the nitrogen they draw.
            if (nitrogen < 0) mine(s, Good.Nitrogen, -nitrogen);
        }

        void BuildMilitary(double jd)
        {
            var fleet = Fleet;
            fleet.Group(Sol, jd, ShipClass.Carrier, "Resolute", new[] { "Earth", "Moon", "Mars" },
                (ShipClass.Battleship, "Dauntless"), (ShipClass.Cruiser, "Aegis"), (ShipClass.Cruiser, "Halberd"),
                (ShipClass.Destroyer, "Kestrel"), (ShipClass.Destroyer, "Merlin"),
                (ShipClass.Fighter, "Mayfly"), (ShipClass.Fighter, "Wasp"));
            fleet.Group(Sol, jd, ShipClass.Battleship, "Vanguard", new[] { "Mars", "Ceres", "Earth" },
                (ShipClass.Cruiser, "Lance"), (ShipClass.Destroyer, "Swift"), (ShipClass.Destroyer, "Talon"),
                (ShipClass.Fighter, "Gadfly"), (ShipClass.Fighter, "Hornet"));
            fleet.Group(Belt, jd, ShipClass.Carrier, "Tycho Ascendant", new[] { "Ceres", "Ganymede", "Callisto", "Europa" },
                (ShipClass.Battleship, "Iron Ledger"), (ShipClass.Cruiser, "Ore Hawk"), (ShipClass.Cruiser, "Pallas"),
                (ShipClass.Destroyer, "Flint"), (ShipClass.Fighter, "Firefly"), (ShipClass.Fighter, "Cinder"));
            fleet.Group(Belt, jd, ShipClass.Cruiser, "Hygiea", new[] { "Ceres", "Mars" },
                (ShipClass.Destroyer, "Shard"), (ShipClass.Destroyer, "Spall"),
                (ShipClass.Fighter, "Grit"), (ShipClass.Fighter, "Pebble"));
            fleet.Group(Reach, jd, ShipClass.Battleship, "Long Night", new[] { "Titan", "Titania", "Triton" },
                (ShipClass.Cruiser, "Umbra"), (ShipClass.Cruiser, "Perihelion"), (ShipClass.Destroyer, "Wisp"),
                (ShipClass.Fighter, "Nightjar"), (ShipClass.Fighter, "Moth"));
            fleet.Group(Reach, jd, ShipClass.Carrier, "Endurance", new[] { "Triton", "Charon", "Titan", "Ganymede" },
                (ShipClass.Cruiser, "Aphelion"), (ShipClass.Destroyer, "Rime"), (ShipClass.Destroyer, "Hoarfrost"),
                (ShipClass.Fighter, "Sleet"), (ShipClass.Fighter, "Driftglass"));
        }

        static readonly string[] FreighterNames =
        {
            "Bountiful Harvest", "Kepler's Due", "Slow Boat", "Red Ledger", "Ice Mule", "Long Haul", "Patience",
            "Margin of Error", "Cold Comfort", "Ballast", "Heavy Lifting", "Second Wind", "Tin Can", "Far Shore",
            "Due Diligence", "Fair Winds", "Hard Bargain", "Payload", "Quiet Profit", "Steady State", "Tramp Royal",
            "Volume Discount", "Wanderer", "Yield Curve", "Small Mercies", "Cargo Cult", "Dead Reckoning", "Night Market",
        };

        static readonly string[] MinerNames = { "Pickaxe", "Rockhound", "Prospect", "Tailings", "Motherlode", "Seam", "Slag Heap", "Assay", "Lode Star", "Gangue" };
        static readonly string[] CourierNames = { "Quicksilver", "Hermes", "Dispatch", "Lightfoot", "Express", "Swiftsure" };

        void BuildMerchants(double jd, string[] freighters, string[] miners, string[] couriers, Faction[] owners, Func<int, Faction> minerOwner)
        {
            var ports = PortBodies();
            merchantNames = freighters;
            minerNames = miners;
            for (int i = 0; i < freighters.Length; i++)
                AddMerchant(freighters[i], ShipClass.Freighter, owners[i % owners.Length], ShipRole.Trader, ports, jd);
            for (int i = 0; i < miners.Length; i++)
                AddMerchant(miners[i], ShipClass.Miner, minerOwner(i), ShipRole.Miner, ports, jd);
            foreach (var name in couriers)
                AddMerchant(name, ShipClass.Courier, Independents, ShipRole.Courier, ports, jd);
        }

        string[] merchantNames = FreighterNames, minerNames = MinerNames;

        /// <summary>The worlds with a port a merchant may trade at (every settlement but a haven).</summary>
        public int[] PortBodies()
        {
            var list = new List<int>();
            foreach (var s in Economy.Settlements)
                if (s.Kind != SettlementKind.Haven && !list.Contains(s.Body)) list.Add(s.Body);
            return list.ToArray();
        }

        /// <summary>Puts a new hull in space: out of its yard's orbit if the yard has one, else in a parking orbit.</summary>
        public void Launch(Ship ship, Settlement yard, double jd)
        {
            var slip = Infrastructure?.YardOf(yard);
            Fleet.Station(ship, yard.Body, jd, slip?.Orbit);
        }

        /// <summary>
        /// A battle has broken out at a body: merchants still on their way there change course for the
        /// nearest other port they can reach without a hyperlane (often turning back where they came from).
        /// A fight off the system's root (at one jump gate) turns aside only those bound for that spot, not
        /// everyone bound for any of the system's gates (U.SPC-T313).
        /// </summary>
        public void DivertAround(Battle b, double jd)
        {
            if (b.Site != null) return;
            var lanes = Fleet.Lanes;
            bool offRoot = b.Body == Catalog.Root.Index;
            foreach (var s in Fleet.Ships)
            {
                if ((!IsTrader(s) && s.Role != ShipRole.Commuter) || s.Destroyed || s.Phase != ShipPhase.Transit || s.Orbit == null || s.Orbit.Body != b.Body) continue;
                if (s.Journey != null && s.JourneyIndex < s.Journey.Count) continue;
                if (s.PhaseEndJd - jd < 0.05 || s.Ports == null) continue;
                if (offRoot)
                {
                    var bound = s.GateKm ?? s.Orbit.OffsetAt(s.PhaseEndJd);
                    if ((bound - b.Anchor).Length > War.GateFightRangeKm) continue;
                }
                var here = Fleet.StateOf(s, jd).HelioKm;
                int best = -1;
                double bestKm = double.MaxValue;
                foreach (int p in s.Ports)
                {
                    if (p == b.Body || War.BattleAt(p) != null) continue;
                    bool reachable = lanes == null || lanes.SystemOf(p) == lanes.SystemOf(b.Body) || lanes.SystemOf(p) == lanes.SystemOf(s.OriginBody);
                    if (!reachable) continue;
                    double km = (Ephemeris.HelioAt(p, jd) - here).Length;
                    if (km < bestKm)
                    {
                        bestKm = km;
                        best = p;
                    }
                }
                if (best < 0 || !Fleet.Divert(s, best, jd)) continue;
                s.Cargo = s.HasCargo ? s.Cargo : "Empty, diverted";
                Log(jd, $"{s.Name} turns aside for {Catalog[best].Name}: fighting at {Catalog[b.Body].Name}", WorldEventKind.Info, best);
            }
        }

        /// <summary>Where a free ship may go: any visible world but a star -- and but a hidden haven's rock,
        /// which nobody knows to go to. Shared by the free roamers, sandbox spawns and gate arrivals.</summary>
        public int[] FreeShipPorts()
        {
            var hidden = new HashSet<int>();
            foreach (var s in Economy.Settlements)
                if (s.Hidden) hidden.Add(s.Body);
            var anywhere = new List<int>();
            foreach (var b in Catalog.Bodies)
                if (b.IsVisible && !b.IsStar && !hidden.Contains(b.Index)) anywhere.Add(b.Index);
            return anywhere.ToArray();
        }

        /// <summary>
        /// Independent pilots who go where they please: yachts and survey ships (fast courier hulls), prospectors (ore
        /// haulers) and deep-space explorers (U.SPC-T419, survey hulls with an FTL drive of their own, which now and then
        /// go off the gate network to look at a star), roaming every world in the system, not just its ports.
        /// </summary>
        void AddFreeShips(double jd, string[] yachts, string[] prospectors, string[] explorers = null)
        {
            var places = FreeShipPorts();
            void Add(string name, ShipClass cls)
            {
                var ship = Fleet.AddShip(name, cls, Independents, ShipRole.Free);
                ship.Ports = places;
                Fleet.Station(ship, places[Fleet.Rng.Next(places.Length)], jd);
            }
            foreach (var n in yachts) Add(n, ShipClass.Courier);
            foreach (var n in explorers ?? new string[0]) Add(n, ShipClass.Explorer);
            foreach (var n in prospectors) Add(n, ShipClass.Miner);
        }

        Ship AddMerchant(string name, ShipClass cls, Faction owner, ShipRole role, int[] ports, double jd, int at = -1, Settlement yard = null)
        {
            var ship = Fleet.AddShip(name, cls, owner, role);
            ship.CapacityTonnes = Economy.CapacityOf(cls);
            ship.Ports = ports;
            if (at < 0)
            {
                // Start at a port the owner may trade at.
                for (int tries = 0; tries < 20; tries++)
                {
                    at = ports[Fleet.Rng.Next(ports.Length)];
                    bool ok = false;
                    foreach (var s in Economy.At(at)) ok |= Diplomacy.CanDock(owner, s.Owner);
                    if (ok) break;
                }
            }
            if (yard != null) Launch(ship, yard, jd);
            else Fleet.Station(ship, at, jd);
            return ship;
        }

        /// <summary>
        /// Founds the system's first trading companies (name, house mark, headquarters, flag, whether it is in the ore
        /// trade) and hands each the merchant hulls already sailing under its flag: ore haulers to the miners, the rest
        /// to the carriers, each to whichever has fewest. A hull whose flag has no company stays its faction's own.
        /// </summary>
        void FoundCompanies(double jd, params (string name, string tag, string home, Faction flag, bool mining)[] table)
        {
            var made = new List<(Company c, bool mining)>();
            foreach (var (name, tag, home, flag, mining) in table)
            {
                Settlement port = null;
                foreach (var s in Economy.Settlements)
                    if (s.Name == home) port = s;
                made.Add((Companies.Found(name, tag, Companies.LiveryFor(made.Count), port, flag, Companies.FoundingCapital, jd), mining));
            }
            foreach (var ship in Fleet.Ships)
            {
                if (ship.Destroyed || !IsTrader(ship)) continue;
                bool ore = ship.Spec.Class == ShipClass.Miner;
                Company best = null;
                int bestRank = int.MaxValue;
                foreach (var (c, mining) in made)
                {
                    if (c.Flag != ship.Faction) continue;
                    int rank = (mining == ore ? 0 : 1000) + c.Ships.Count;
                    if (rank < bestRank)
                    {
                        bestRank = rank;
                        best = c;
                    }
                }
                if (best != null) Companies.Enlist(best, ship);
            }
        }

        // ---------------------------------------------------------------- shipbuilding

        public int TraderCount()
        {
            int n = 0;
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && IsTrader(s)) n++;
            return n;
        }

        /// <summary>
        /// A navy at war that has been worn below its peacetime strength takes its own yards' slips ahead of the
        /// companies' orders, to rebuild; otherwise null (the companies' hulls first, then the navy's).
        /// </summary>
        ShipClass? Requisition(Settlement yard)
        {
            var f = yard.Owner;
            if (f == null || !f.IsMilitary || !War.AtWar(f)) return null;
            int navy = 0;
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && s.Faction == f && s.Spec.IsMilitary && !s.PatrolWing) navy++;
            return navy < NavyCap ? War.ChooseWarship(yard) : null;
        }

        /// <summary>Yards build merchant hulls while the merchant fleet is under strength.</summary>
        ShipClass? ChooseBuild(Settlement yard)
        {
            if (TraderCount() >= MerchantCap) return null;
            return builtCount % 3 == 2 ? ShipClass.Miner : ShipClass.Freighter;
        }

        void OnShipBuilt(Settlement yard, ShipClass cls, double jd)
        {
            // Its crew and marines come out of the owner's trained reserve.
            Crews.Draft(yard.Owner, Crews.Complement(cls));
            Ship ship;
            if (ShipClassSpec.Of(cls).IsMilitary)
            {
                ship = War.LaunchWarship(yard, cls, jd);
                ShipAdded?.Invoke(ship);
                return;
            }
            if (cls == ShipClass.LaneTender)
            {
                // A tender for one of its owner's lanes (U.SPC-T481).
                ship = LaneUpkeep.Launch(yard, jd);
                if (ship != null) ShipAdded?.Invoke(ship);
                return;
            }
            builtCount++;
            Company company = null;
            if (cls == ShipClass.Freighter || cls == ShipClass.Miner || cls == ShipClass.Courier)
            {
                var role = cls == ShipClass.Miner ? ShipRole.Miner : cls == ShipClass.Courier ? ShipRole.Courier : ShipRole.Trader;
                var names = cls == ShipClass.Miner ? minerNames : merchantNames;
                // The company that ordered it pays the yard and takes it; else it is the yard owner's own.
                company = Companies.TakeDelivery(yard, cls, jd);
                string name = company != null ? Companies.HullName(company, names) : names[builtCount % names.Length] + " " + Roman(2 + builtCount / names.Length);
                ship = AddMerchant(name, cls, company?.Flag ?? yard.Owner, role, PortBodies(), jd, yard.Body, yard);
                if (company != null) Companies.Enlist(company, ship);
            }
            else return;
            Log(jd, $"{yard.Name} launches the {ship.Spec.DisplayName.ToLowerInvariant()} {ship.Name}" + (company != null ? $" for {company.Name}" : ""), WorldEventKind.Launch, yard.Body);
            ShipAdded?.Invoke(ship);
        }

        static string Roman(int n)
        {
            string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return n < r.Length ? r[n] : n.ToString();
        }
    }

    public enum WorldEventKind
    {
        Info,
        Launch,
        Battle,
        Capture,
        Diplomacy,
        Loss,

        /// <summary>An adventure offered to the player, or how one ended (see <see cref="EventDirector"/>).</summary>
        Opportunity,

        /// <summary>An economic event: an accident, a blight, a strike, rationing, a blockade (see <see cref="MarketShocks"/>).</summary>
        Economy,

        /// <summary>A price spike at a port, or across the system.</summary>
        Market,

        /// <summary>A crime recorded, a bounty posted, paid or collected (see <see cref="Law"/>).</summary>
        Law,
    }

    public readonly struct WorldEvent
    {
        public readonly double Jd;
        public readonly string Text;
        public readonly WorldEventKind Kind;
        public readonly int Body;

        public WorldEvent(double jd, string text, WorldEventKind kind, int body)
        {
            Jd = jd;
            Text = text;
            Kind = kind;
            Body = body;
        }
    }
}
