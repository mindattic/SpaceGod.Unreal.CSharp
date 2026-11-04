using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Interstellar piracy (U.SPC-T94): the pirate clans as powers that reach across the gates, run once a day by the
    /// <see cref="InterstellarTraffic"/>. A clan is one polity (<see cref="Polities"/>) wherever it holds havens, and:
    ///
    ///  - <b>raids where traffic bunches up.</b> Its bands lie in wait off a system's busiest jump gate as well as its
    ///    hyperlanes and ports (see <see cref="War.GateAmbushes"/>), and what they take goes to the clan's own haven in
    ///    that system, or home in their holds through the gates when they have none there;
    ///  - <b>smuggles.</b> When a haven's stores fill with loot, a smuggler (one of the clan's own freighters, kept at
    ///    the haven between runs) carries it through the gates to the black market of a lawless station in another
    ///    system (<see cref="BlackMarket"/>) that pays more for it than the haven's own fences, and the clan is paid;
    ///  - <b>grows and moves.</b> A rich clan sends a band through the gates to found a haven in an empty hide in a
    ///    frontier belt or debris disc (<see cref="World.HavenSites"/>), and moves bands between its havens to keep each
    ///    manned (in two systems at most); and a haven abroad that has stood a while may break away as a clan of its own;
    ///  - <b>is hunted.</b> Every state it robs puts a price on it (<see cref="Bounty"/>), paid to whoever destroys its
    ///    hulls, anywhere; those states send navy expeditions through the gates against its havens once they are found,
    ///    and bounty hunters go after its bands in the systems where they work.
    ///
    /// Kept small enough that a thin frontier system's merchants live through it: one smuggler out from a system at a
    /// time, a clan holds two havens at most, and founding, moving and splitting are rare.
    /// </summary>
    public sealed class PirateClans
    {
        readonly Galaxy galaxy;
        readonly InterstellarTraffic traffic;
        readonly Random rng;

        /// <summary>The price on each clan, by the state that posted it (credits).</summary>
        readonly Dictionary<Faction, Dictionary<Faction, double>> pledges = new Dictionary<Faction, Dictionary<Faction, double>>();

        /// <summary>When each haven founded abroad was founded.</summary>
        readonly Dictionary<Settlement, double> founded = new Dictionary<Settlement, double>();

        /// <summary>Clans that have resolved to found a haven abroad, and when: they send the first band fit to sail.</summary>
        readonly Dictionary<Faction, double> resolved = new Dictionary<Faction, double>();

        /// <summary>Smuggling runs set out, sales made, and what the sales brought in (credits).</summary>
        public int Runs { get; private set; }
        public int Sales { get; private set; }
        public double SmuggledValue { get; private set; }

        /// <summary>Bands sent to found a haven abroad, havens founded, bands moved between havens, clans split off, bounties paid
        /// (credits), hunts and expeditions sent.</summary>
        public int Expansions { get; private set; }
        public int Foundings { get; private set; }
        public int Moves { get; private set; }
        public int Splits { get; private set; }
        public double BountiesPaid { get; private set; }
        public int Hunts { get; private set; }
        public int Expeditions { get; private set; }

        /// <summary>A haven's loot worth (at base prices) this much is worth running to a black market.</summary>
        public const double MinSmuggleValue = 3e6;

        /// <summary>A run must clear this over what the haven's own fences would pay, credits.</summary>
        public const double MinSmuggleGain = 1e6;

        /// <summary>What a clan pays for a smuggler's hull (a used freighter, bought quietly), credits.</summary>
        public static double RunnerPrice => Economy.HullPrice(ShipClass.Freighter) * 0.5;

        /// <summary>The most systems one clan holds havens in.</summary>
        public const int MaxSystems = 2;

        /// <summary>A clan founds a haven abroad only with this in its treasury, and pays <see cref="FoundingCost"/> for it.</summary>
        public const double ExpandTreasury = 1.2e8;
        public const double FoundingCost = 4e7;

        /// <summary>Daily chance a clan able to found a haven abroad resolves to (it sends the first band free to sail).</summary>
        public const double ExpandChancePerDay = 0.02;

        /// <summary>Daily chance a clan moves a band to a haven of its that has none.</summary>
        public const double MoveChancePerDay = 0.05;

        /// <summary>A haven abroad must have stood this long (days) before it may break away, with this daily chance.</summary>
        public const double SplitMinDays = 180;
        public const double SplitChancePerDay = 0.002;

        /// <summary>A robbed state puts this share of what was taken on the clan's head (all of it).</summary>
        public const double PledgeShare = 1;

        /// <summary>The most one state keeps on one clan's head (credits), and how fast an old price is forgotten (days).</summary>
        public const double MaxPledge = 3e8;
        public const double PledgeMemoryDays = 180;

        /// <summary>What destroying one of a clan's hulls collects of the price on it: this share of the hull's price.</summary>
        public const double HullBountyShare = 0.25;

        /// <summary>A state with this much on a clan sends an expedition after a found haven of its; hunters go after a clan
        /// with this much on it in all; each is considered with this daily chance.</summary>
        public const double MinExpeditionPledge = 1e7;
        public const double MinHuntBounty = 1e7;
        public const double HuntChancePerDay = 0.05;

        static readonly string[] RunnerNames =
        {
            "Quiet Money", "Back Door", "Blind Eye", "Hush", "Fence Post", "Bad Penny", "Dark Horse", "Night Freight", "Small Hours", "Loose Change",
        };

        static readonly string[] BreakawayNames = { "Brethren", "Free Company", "Reavers", "Corsairs", "Black Hand" };

        int runnersBuilt;

        public PirateClans(Galaxy galaxy, InterstellarTraffic traffic, int seed)
        {
            this.galaxy = galaxy;
            this.traffic = traffic;
            rng = new Random(seed);
        }

        /// <summary>Takes on <paramref name="system"/>'s raids and fights (the traffic registers every system, those opened later too).</summary>
        internal void Watch(StarSystem system)
        {
            var w = system.World;
            if (w.War == null) return;
            w.War.Plundered += (clan, value, jd) => OnPlundered(w, clan, value);
            // A hull destroyed rather than robbed brings the state down on the clan far harder (U.SPC-T374).
            w.War.Heat += (clan, heat, jd) => Pledge(Polities.HolderOf(w), clan, heat);
            w.War.BattleEnded += (b, jd) => OnBattleEnded(w, b, jd);
        }

        // ---------------------------------------------------------------- who and where

        /// <summary>Every pirate clan in the polities.</summary>
        public List<Faction> Clans()
        {
            var list = new List<Faction>();
            foreach (var f in galaxy.Polities.Factions)
                if (f.IsPirate) list.Add(f);
            return list;
        }

        /// <summary>The standing havens of <paramref name="clan"/>, in every system.</summary>
        public List<(StarSystem system, Settlement haven)> Havens(Faction clan)
        {
            var list = new List<(StarSystem, Settlement)>();
            foreach (var s in galaxy.Systems)
                foreach (var st in s.World.Economy.Settlements)
                    if (st.Kind == SettlementKind.Haven && !st.Fallen && st.Owner == clan) list.Add((s, st));
            return list;
        }

        /// <summary>The systems those havens are in.</summary>
        static HashSet<StarSystem> SystemsOf(List<(StarSystem system, Settlement haven)> havens)
        {
            var set = new HashSet<StarSystem>();
            foreach (var (system, _) in havens) set.Add(system);
            return set;
        }

        /// <summary><paramref name="clan"/>'s raider bands in <paramref name="system"/> (flagships, at home there: not on a voyage).</summary>
        public List<Ship> Bands(Faction clan, StarSystem system)
        {
            var list = new List<Ship>();
            foreach (var s in system.World.Fleet.Ships)
                if (!s.Destroyed && !s.IsEscort && !s.Runner && !s.Derelict && s.Faction == clan && s.Voyage == null) list.Add(s);
            return list;
        }

        /// <summary>The price on <paramref name="clan"/>'s head, credits: every robbed state's pledge.</summary>
        public double Bounty(Faction clan)
        {
            double sum = 0;
            if (clan != null && pledges.TryGetValue(clan, out var by))
                foreach (var kv in by) sum += kv.Value;
            return sum;
        }

        /// <summary>What <paramref name="state"/> has put on <paramref name="clan"/>'s head, credits.</summary>
        public double Pledged(Faction state, Faction clan) =>
            clan != null && pledges.TryGetValue(clan, out var by) && by.TryGetValue(state, out double v) ? v : 0;

        /// <summary><paramref name="state"/> puts <paramref name="credits"/> more on <paramref name="clan"/>'s head.</summary>
        public void Pledge(Faction state, Faction clan, double credits)
        {
            if (state == null || clan == null || state.IsPirate || !clan.IsPirate || credits <= 0) return;
            if (!pledges.TryGetValue(clan, out var by)) pledges[clan] = by = new Dictionary<Faction, double>();
            by.TryGetValue(state, out double v);
            by[state] = Math.Min(MaxPledge, v + credits);
        }

        // ---------------------------------------------------------------- the day

        /// <summary>A day of the clans' business (called by the traffic's daily round, with its voyages counted).</summary>
        internal void Daily(double jd)
        {
            double fade = Math.Exp(-1 / PledgeMemoryDays);
            foreach (var by in pledges.Values)
                foreach (var state in new List<Faction>(by.Keys)) by[state] *= fade;
            foreach (var clan in Clans())
            {
                var havens = Havens(clan);
                if (havens.Count == 0) continue;
                Smuggle(clan, havens, jd);
                int systems = SystemsOf(havens).Count;
                if (systems < MaxSystems && clan.Treasury >= ExpandTreasury && !resolved.ContainsKey(clan) && rng.NextDouble() < ExpandChancePerDay) resolved[clan] = jd;
                if (resolved.TryGetValue(clan, out double resolvedJd))
                {
                    // Until a band is free to go (a month at most).
                    if (Expand(clan, jd) != null || jd - resolvedJd > 30) resolved.Remove(clan);
                }
                else if (systems > 1 && rng.NextDouble() < MoveChancePerDay) MoveBand(clan, havens, jd);
                if (systems > 1 && rng.NextDouble() < SplitChancePerDay)
                    foreach (var (system, haven) in havens)
                        if (founded.TryGetValue(haven, out double since) && jd - since >= SplitMinDays && system.World != galaxy.Polities.CapitalSystemOf(clan))
                        {
                            Split(haven, jd);
                            break;
                        }
                if (rng.NextDouble() < HuntChancePerDay) Hunt(clan, havens, jd);
            }
        }

        // ---------------------------------------------------------------- smuggling

        /// <summary>Sends a smuggler from each of <paramref name="clan"/>'s havens whose loot would fetch more at a black market abroad.</summary>
        void Smuggle(Faction clan, List<(StarSystem system, Settlement haven)> havens, double jd)
        {
            foreach (var (from, haven) in havens)
            {
                if (traffic.Out(from, VoyagePurpose.Smuggle) > 0 || BlackMarket.LootValue(haven) < MinSmuggleValue) continue;
                Settlement best = null;
                StarSystem bestSystem = null;
                List<JumpGate> bestRoute = null;
                double bestScore = 0;
                foreach (var to in galaxy.Systems)
                {
                    if (to == from) continue;
                    foreach (var market in to.World.Economy.Settlements)
                    {
                        if (!market.Lawless || market.Fallen) continue;
                        var route = traffic.Network.Route(from, to, clan, jd);
                        if (route == null || route.Count == 0) continue;
                        double gain = Plan(haven, market, Economy.CapacityOf(ShipClass.Freighter), null);
                        double score = gain / (GateNetwork.RouteDays(route) + 10);
                        if (gain >= MinSmuggleGain && score > bestScore)
                        {
                            bestScore = score;
                            best = market;
                            bestSystem = to;
                            bestRoute = route;
                        }
                    }
                }
                if (best != null) SendRunner(from, haven, bestSystem, best, bestRoute, jd);
            }
        }

        /// <summary>
        /// What a run of up to <paramref name="capacity"/> tonnes of <paramref name="haven"/>'s loot to <paramref name="market"/>
        /// would clear over the haven's own fences' price (credits), the goods with the best margin first; fills
        /// <paramref name="load"/> (tonnes by good) when given.
        /// </summary>
        public static double Plan(Settlement haven, Settlement market, double capacity, double[] load)
        {
            var goods = new List<(Good g, double margin)>();
            for (int i = 0; i < GoodInfo.Count; i++)
            {
                var g = (Good)i;
                double margin = BlackMarket.Price(market, g) - GoodInfo.Of(g).BasePrice * BlackMarket.HavenShare;
                if (margin > 0 && BlackMarket.Held(haven, g) > 1) goods.Add((g, margin));
            }
            goods.Sort((a, b) => b.margin.CompareTo(a.margin));
            double room = capacity, gain = 0;
            foreach (var (g, _) in goods)
            {
                if (room <= 1) break;
                double t = Math.Min(Math.Min(BlackMarket.Held(haven, g), room), BlackMarket.Appetite(market, g));
                double clear = BlackMarket.SaleValue(market, g, t) - t * GoodInfo.Of(g).BasePrice * BlackMarket.HavenShare;
                if (clear <= 0) continue;
                gain += clear;
                room -= t;
                if (load != null) load[(int)g] += t;
            }
            return gain;
        }

        /// <summary>
        /// Loads a smuggler at <paramref name="haven"/> (the clan's idle one there, or a hull bought for it) with the loot that
        /// pays best at <paramref name="market"/> and sends it there through the gates. Returns the smuggler, or null if the
        /// clan has none and cannot buy or crew one.
        /// </summary>
        public Ship SendRunner(StarSystem from, Settlement haven, StarSystem to, Settlement market, List<JumpGate> route, double jd)
        {
            var clan = haven.Owner;
            route ??= traffic.Network.Route(from, to, clan, jd);
            if (route == null || route.Count == 0) return null;
            var runner = IdleRunner(from, haven);
            // One back through the gates but not yet in at the haven (still crossing from the gate it came out of) is
            // waited for, not replaced with a new hull (U.SPC-T304).
            if (runner == null && !Returning(from, haven)) runner = BuildRunner(from, haven, jd);
            if (runner == null) return null;
            var load = new double[GoodInfo.Count];
            Plan(haven, market, runner.CapacityTonnes, load);
            double tonnes = 0;
            for (int i = 0; i < GoodInfo.Count; i++)
            {
                if (load[i] <= 0) continue;
                haven.Stock[i] = Math.Max(0, haven.Stock[i] - load[i]);
                tonnes += load[i];
            }
            if (tonnes <= 0) return null;
            runner.Plunder = load;
            runner.Cargo = "Stolen goods, bound for the black market at " + market.Name;
            var v = traffic.SetOut(from, to, runner, VoyagePurpose.Smuggle, route, jd);
            v.TargetBody = market.Body;
            Runs++;
            from.World.Log(jd, $"{runner.Name}, a {clan.Name} smuggler, slips out with {tonnes.ToString("N0", CultureInfo.InvariantCulture)} t of stolen goods for {to.Name}",
                WorldEventKind.Info, -1);
            return runner;
        }

        Ship IdleRunner(StarSystem system, Settlement haven)
        {
            foreach (var s in system.World.Fleet.Ships)
                if (s.Runner && s.Faction == haven.Owner && InterstellarTraffic.Idle(system.World, s) && InterstellarTraffic.PortOf(s) == haven.Body) return s;
            return null;
        }

        /// <summary>Whether a smuggler of <paramref name="haven"/>'s is home in <paramref name="system"/> but not idle there: on
        /// its way in from the gate it came back through, or kept from port by a fight.</summary>
        static bool Returning(StarSystem system, Settlement haven)
        {
            foreach (var s in system.World.Fleet.Ships)
                if (s.Runner && !s.Destroyed && !s.Derelict && s.Faction == haven.Owner && s.Voyage == null && s.Ports != null && Array.IndexOf(s.Ports, haven.Body) >= 0)
                    return true;
            return false;
        }

        Ship BuildRunner(StarSystem system, Settlement haven, double jd)
        {
            var clan = haven.Owner;
            var world = system.World;
            double hands = Crews.Complement(ShipClass.Freighter);
            if (clan.Treasury < RunnerPrice + 2e7 || clan.Manpower < hands) return null;
            clan.Treasury -= RunnerPrice;
            Crews.Draft(clan, hands);
            int n = runnersBuilt++;
            string name = RunnerNames[n % RunnerNames.Length] + (n >= RunnerNames.Length ? " " + (n / RunnerNames.Length + 1) : "");
            var ship = world.Fleet.AddShip(name, ShipClass.Freighter, clan, ShipRole.Free);
            ship.Runner = true;
            ship.CapacityTonnes = Economy.CapacityOf(ShipClass.Freighter);
            ship.Route = new[] { haven.Body };
            ship.Ports = new[] { haven.Body };
            world.Fleet.Station(ship, haven.Body, jd);
            world.NotifyAdded(ship);
            return ship;
        }

        /// <summary>
        /// A smuggler abroad (the voyage's orders): to its black market; there, it sells everything aboard at the market's
        /// own prices, the clan is paid, and it turns for home.
        /// </summary>
        internal int SmugglerOrders(StarSystem system, Ship ship, int here, double jd)
        {
            var v = ship.Voyage;
            Settlement market = null;
            foreach (var st in system.World.Economy.At(v.TargetBody))
                if (BlackMarket.Takes(st) && !st.Owner.IsPirate) market = st;
            if (market == null)
            {
                // Its market is gone (taken, or never was): home with the goods.
                v.StayUntilJd = jd;
                return here;
            }
            if (here != market.Body) return market.Body;
            if (ship.Plunder != null)
            {
                double value = 0, tonnes = 0;
                for (int i = 0; i < GoodInfo.Count; i++)
                {
                    double t = ship.Plunder[i];
                    if (t <= 0) continue;
                    value += BlackMarket.Sell(market, (Good)i, t);
                    tonnes += t;
                }
                ship.Plunder = null;
                ship.Cargo = null;
                ship.Faction.Treasury += value;
                ship.Credits += value;
                Sales++;
                SmuggledValue += value;
                system.World.Log(jd, $"{ship.Name} ({ship.Faction.Name}) sells {tonnes.ToString("N0", CultureInfo.InvariantCulture)} t of stolen goods on the black market at {market.Name} for {(value / 1e6).ToString("0.0", CultureInfo.InvariantCulture)} M",
                    WorldEventKind.Info, market.Body);
            }
            v.StayUntilJd = Math.Min(v.StayUntilJd, jd + 1);
            return here;
        }

        // ---------------------------------------------------------------- growing and moving

        /// <summary>
        /// <paramref name="clan"/> sends a band through the gates to found a haven in the nearest empty hide in a belt or disc
        /// of a system where it has none. Returns the band, or null if there is no hide in reach, no band free to go, a band
        /// of its is already on its way to found one (even one riding inside a gate right now), or its treasury has fallen
        /// under <see cref="FoundingCost"/> since it resolved to go.
        /// </summary>
        public Ship Expand(Faction clan, double jd)
        {
            var havens = Havens(clan);
            if (havens.Count == 0 || SystemsOf(havens).Count >= MaxSystems || clan.Treasury < FoundingCost) return null;
            foreach (var s in galaxy.Systems)
                foreach (var ship in s.World.Fleet.Ships)
                    if (!ship.Destroyed && ship.Faction == clan && ship.Voyage?.Purpose == VoyagePurpose.Relocate) return null;
            // A band already under way is retired out of every system's fleet while it rides inside the gate itself
            // (see InterstellarTraffic.InTransit): the loop above alone would miss it there and send a second one.
            foreach (var ship in traffic.InTransit())
                if (ship.Faction == clan && ship.Voyage?.Purpose == VoyagePurpose.Relocate) return null;
            var held = SystemsOf(havens);
            Ship band = null;
            StarSystem from = null, to = null;
            List<JumpGate> bestRoute = null;
            int site = -1;
            double bestDays = double.MaxValue;
            foreach (var system in held)
            {
                var ready = ReadyBand(clan, system);
                if (ready == null) continue;
                foreach (var target in galaxy.Systems)
                {
                    if (held.Contains(target) || target.World.HavenSites.Count == 0) continue;
                    var route = traffic.Network.Route(system, target, clan, jd);
                    if (route == null || route.Count == 0) continue;
                    double days = GateNetwork.RouteDays(route);
                    if (days < bestDays)
                    {
                        bestDays = days;
                        band = ready;
                        from = system;
                        to = target;
                        bestRoute = route;
                        site = target.World.HavenSites[0];
                    }
                }
            }
            if (band == null) return null;
            var v = traffic.SetOut(from, to, band, VoyagePurpose.Relocate, bestRoute, jd);
            v.TargetBody = site;
            v.SiteName = to.Catalog[site].Name;
            clan.Treasury -= FoundingCost;
            Expansions++;
            from.World.Log(jd, $"{clan.Name} raiders under {band.Name} slip away through the {bestRoute[0].Name} to found a hold beyond the gates", WorldEventKind.Info, -1);
            return band;
        }

        /// <summary>Moves a band from the clan's best-manned haven to one with none. Returns the band, or null.</summary>
        Ship MoveBand(Faction clan, List<(StarSystem system, Settlement haven)> havens, double jd)
        {
            (StarSystem system, Settlement haven) empty = default, crowded = default;
            int most = 1;
            foreach (var h in havens)
            {
                int n = Bands(clan, h.system).Count;
                if (n > most)
                {
                    most = n;
                    crowded = h;
                }
            }
            // An empty haven already being refilled (a band of the clan's on its way to it from another haven) is left
            // to that band: it is the moves to it that count, not those from it (U.SPC-T302).
            foreach (var h in havens)
            {
                if (empty.haven != null || Bands(clan, h.system).Count != 0) continue;
                bool incoming = false;
                foreach (var o in havens)
                    if (o.system != h.system && traffic.Out(o.system, VoyagePurpose.Relocate, h.system) > 0) { incoming = true; break; }
                if (!incoming) empty = h;
            }
            if (empty.haven == null || crowded.haven == null || empty.system == crowded.system) return null;
            var band = ReadyBand(clan, crowded.system);
            if (band == null) return null;
            var route = traffic.Network.Route(crowded.system, empty.system, clan, jd);
            if (route == null || route.Count == 0) return null;
            var v = traffic.SetOut(crowded.system, empty.system, band, VoyagePurpose.Relocate, route, jd);
            v.TargetBody = empty.haven.Body;
            Moves++;
            crowded.system.World.Log(jd, $"{clan.Name} raiders under {band.Name} slip away through the {route[0].Name} for the clan's hold at {empty.system.Name}", WorldEventKind.Info, -1);
            return band;
        }

        /// <summary>A band of <paramref name="clan"/>'s in <paramref name="system"/> fit to sail: idle in port, whole and armed.</summary>
        Ship ReadyBand(Faction clan, StarSystem system)
        {
            foreach (var s in Bands(clan, system))
                if (InterstellarTraffic.Idle(system.World, s) && s.Phase == ShipPhase.Parked && s.Combat.Damage <= 0.2 && s.Combat.Armed) return s;
            return null;
        }

        /// <summary>
        /// A band on a move comes out at its far end (the voyage's arrival): it founds the clan's haven at the hide it was
        /// sent to, if no haven of the clan stands there yet, and joins that system's raiders for good, based at the haven.
        /// If the hide has been taken meanwhile, it raids a while and goes home.
        /// </summary>
        internal void Settle(StarSystem system, Ship band, double jd)
        {
            var v = band.Voyage;
            var world = system.World;
            var clan = band.Faction;
            var haven = world.War.LiveHavenOf(clan);
            if (haven == null && v.TargetBody >= 0 && world.HavenSites.Contains(v.TargetBody))
            {
                haven = world.FoundHaven(v.SiteName ?? system.Catalog[v.TargetBody].Name, v.TargetBody, clan, jd);
                founded[haven] = jd;
                Foundings++;
            }
            band.GateKm = null;
            band.Role = v.HomeRole;
            if (haven == null)
            {
                v.Purpose = VoyagePurpose.Raid;
                v.StayUntilJd = jd + 15;
                band.Route = null;
                band.Ports = world.FreeShipPorts();
                world.Fleet.ScheduleNext(band, jd);
                return;
            }
            band.Voyage = null;
            band.Route = new[] { haven.Body };
            band.Ports = world.FreeShipPorts();
            world.Fleet.ScheduleNext(band, jd);
            world.Log(jd, $"{clan.Name} raiders under {band.Name} come through from {v.Home.Name} and make their hold in the dark", WorldEventKind.Info, -1);
        }

        /// <summary>
        /// <paramref name="haven"/> breaks away from its clan: a new clan, its own polity, takes it over with the bands
        /// based there and a share of the old clan's treasury and hands. Returns the new clan.
        /// </summary>
        public Faction Split(Settlement haven, double jd)
        {
            var old = haven.Owner;
            var world = galaxy.Polities.WorldOf(haven);
            if (old == null || !old.IsPirate || world == null) return null;
            string name = haven.Name + " " + BreakawayNames[rng.Next(BreakawayNames.Length)];
            if (galaxy.Polities.Find(name) != null) name = haven.Name + " " + BreakawayNames[Splits % BreakawayNames.Length] + " " + (Splits + 2);
            var c = old.Color;
            var clan = new Faction(name, old.Prefix, new Rgb(Math.Min(1f, c.R * 0.8f + 0.15f), c.G * 0.7f, Math.Min(1f, c.B * 0.8f + 0.2f)))
            {
                Kind = PolityKind.PirateClan, IsPirate = true, MarineQuality = old.MarineQuality,
            };
            galaxy.Polities.Found(clan, world);
            world.Fleet.AddFaction(clan);
            double purse = Math.Max(0, old.Treasury) * 0.3;
            old.Treasury -= purse;
            clan.Treasury = purse;
            if (!double.IsPositiveInfinity(old.Manpower))
            {
                double hands = Math.Max(0, old.Manpower) * 0.3;
                old.Manpower -= hands;
                clan.Manpower = hands;
            }
            clan.CrewMorale = old.CrewMorale;
            haven.Owner = clan;
            clan.Capital = haven;
            foreach (var s in world.Fleet.Ships)
            {
                if (s.Destroyed || s.Faction != old) continue;
                var lead = s.Controller;
                var route = lead.Route;
                if (route != null && route.Length > 0 && route[0] == haven.Body) s.Faction = clan;
            }
            if (world.Pirates == old) world.Pirates = clan;
            Splits++;
            world.Log(jd, $"{haven.Name} breaks away from {old.Name}: its raiders sail as {clan.Name} now", WorldEventKind.Diplomacy, haven.Discovered ? haven.Body : -1);
            return clan;
        }

        // ---------------------------------------------------------------- the hunt

        void OnPlundered(World world, Faction clan, double value)
        {
            var holder = Polities.HolderOf(world);
            if (holder != null) Pledge(holder, clan, value * PledgeShare);
        }

        /// <summary>Whoever won a fight in which a clan lost hulls (destroyed or taken) collects the price on them, from the states that posted it.</summary>
        void OnBattleEnded(World world, Battle b, double jd)
        {
            if (b.Winner < 0) return;
            var payee = b.SideLeaders[b.Winner];
            if (payee == null || payee.IsPirate) return;
            double paid = 0;
            Faction clan = null;
            foreach (var u in b.Units)
            {
                // (A hull taken is counted on its captors' side by then: a clan's hull lost when someone else won is theirs.)
                if (!(u.Dead || u.Captured) || u.Ship == null || u.Kind == UnitKind.Fighter) continue;
                var f = u.Ship.Faction;
                if (f == null || !f.IsPirate) continue;
                paid += Collect(f, Economy.HullPrice(u.Ship.Spec.Class) * HullBountyShare);
                clan = f;
            }
            if (paid <= 0) return;
            payee.Treasury += paid;
            BountiesPaid += paid;
            world.Log(jd, $"{payee.Name} collects {(paid / 1e6).ToString("0.0", CultureInfo.InvariantCulture)} M of the bounty on {clan.Name}", WorldEventKind.Info, -1);
        }

        /// <summary>Draws up to <paramref name="amount"/> from the price on <paramref name="clan"/>, each pledging state paying its share.</summary>
        double Collect(Faction clan, double amount)
        {
            double pool = Bounty(clan);
            if (pool <= 0 || amount <= 0) return 0;
            double take = Math.Min(pool, amount);
            var by = pledges[clan];
            foreach (var state in new List<Faction>(by.Keys))
            {
                double share = take * by[state] / pool;
                by[state] -= share;
                state.Treasury -= share;
            }
            return take;
        }

        /// <summary>The hunt for <paramref name="clan"/> today (see <see cref="Hunt"/>), whatever the day's dice say.</summary>
        public void Pursue(Faction clan, double jd) => Hunt(clan, Havens(clan), jd);

        /// <summary>
        /// The states with a price on <paramref name="clan"/> send navy expeditions through the gates against its havens
        /// that have been found, and bounty hunters go after its bands where they work.
        /// </summary>
        void Hunt(Faction clan, List<(StarSystem system, Settlement haven)> havens, double jd)
        {
            if (!pledges.TryGetValue(clan, out var by)) return;
            // Navies: against a hidden haven somebody has found (an open one, like Blackwater, they leave be).
            foreach (var (system, haven) in havens)
            {
                if (!haven.Hidden || !haven.Discovered) continue;
                foreach (var kv in by)
                {
                    var state = kv.Key;
                    if (kv.Value < MinExpeditionPledge || !state.IsMilitary) continue;
                    foreach (var from in galaxy.Systems)
                    {
                        if (from == system || from.World.Sol != state || traffic.Out(from, VoyagePurpose.Expedition) > 0) continue;
                        if (traffic.Network.Route(from, system, state, jd) == null) continue;
                        // Word of where it is has reached them.
                        haven.KnownTo.Add(state);
                        if (traffic.DispatchExpedition(from, system, clan, jd))
                        {
                            Expeditions++;
                            return;
                        }
                    }
                }
            }
            // Hunters: where the clan's bands are, from a system with a hunter free of contracts.
            if (Bounty(clan) < MinHuntBounty) return;
            StarSystem where = null;
            int most = 0;
            foreach (var s in galaxy.Systems)
            {
                int n = Bands(clan, s).Count;
                if (n > most)
                {
                    most = n;
                    where = s;
                }
            }
            if (where == null) return;
            foreach (var from in galaxy.Systems)
            {
                var outlaws = from.World.Outlaws;
                if (from == where || outlaws == null || traffic.Out(from, VoyagePurpose.Hunt) > 0) continue;
                foreach (var h in outlaws.Hunters)
                {
                    var ship = h.Ship;
                    if (!h.Alive || h.Contract != null || !InterstellarTraffic.Idle(from.World, ship) || ship.Phase != ShipPhase.Parked || ship.Combat.Damage > 0.2) continue;
                    var route = traffic.Network.Route(from, where, ship.Faction, jd);
                    if (route == null || route.Count == 0) continue;
                    var v = traffic.SetOut(from, where, ship, VoyagePurpose.Hunt, route, jd);
                    v.Quarry = clan;
                    v.Origin = ship;
                    Hunts++;
                    from.World.Log(jd, $"The bounty hunter {ship.Name} goes through the {route[0].Name} after {clan.Name} at {where.Name}: there is {(Bounty(clan) / 1e6).ToString("0", CultureInfo.InvariantCulture)} M on their heads",
                        WorldEventKind.Info, InterstellarTraffic.PortOf(ship));
                    return;
                }
            }
        }
    }
}
