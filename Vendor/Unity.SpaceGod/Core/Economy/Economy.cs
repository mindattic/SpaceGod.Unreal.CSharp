using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The system's markets. Every settlement produces through its facilities (each limited by its
    /// scarcest input and by warehouse space), feeds its people, and prices every good from its stock
    /// against what it uses. Merchant ships do the rest: at each port a trader looks for the cargo and
    /// destination that earn the most per day of travel, counting deliveries already on the way, so
    /// goods flow from surplus to shortage on their own. Ships' profits go to their faction, ports tax
    /// the trade they host, and shipyards turn alloys, electronics and machinery into new hulls.
    /// </summary>
    public sealed class Economy
    {
        public readonly List<Settlement> Settlements = new List<Settlement>();
        public readonly Diplomacy Diplomacy;

        /// <summary>Economic time; advanced in ticks by <see cref="AdvanceTo"/>.</summary>
        public double Jd { get; private set; }

        /// <summary>Estimated days for a ship to fly between two bodies (ship, from, to, departure date).</summary>
        public Func<Ship, int, int, double, double> EstimateTravelDays;

        /// <summary>
        /// What a trip takes (days, the delta-v of its own burns, the lanes it rides) for a ship between two
        /// bodies at a date. When set, traders price every run by its real propellant, lane tolls and days
        /// (and drop runs their tanks cannot cover); when null, by days alone.
        /// </summary>
        public Func<Ship, int, int, double, TripEstimate> EstimateTrip;

        /// <summary>Extra cost a trader puts on a body (pirates, battles), as a fraction of a trip's value.</summary>
        public Func<int, double> Danger;

        /// <summary>Called when a yard has banked enough to lay down a hull. Return the class to build, or null to wait.</summary>
        public Func<Settlement, ShipClass?> ChooseBuild;

        /// <summary>
        /// Whether a settlement's owner favours an industry (its production focus, U.SPC-T423: it grows on thinner margins and
        /// is kept up through a glut) and the extra growth a review its research gives it. Unset, every industry follows its market.
        /// </summary>
        public Func<Settlement, IndustryKind, (bool Favoured, double Bonus)> Priority;

        /// <summary>Factions founding new settlements as their people run out of room (U.SPC-T98), run at each review; null for none.</summary>
        public Colonisation Colonisation;

        /// <summary>Raised when a yard completes a hull; the world puts it into service.</summary>
        public Action<Settlement, ShipClass, double> ShipBuilt;

        /// <summary>Raised when a trader sells a load at a port (ship, port, good, tonnes): who supplies whom, for the news and for embargoes.</summary>
        public Action<Ship, Settlement, Good, double> Sold;

        /// <summary>Whether a yard's owner has the hands to crew a hull of this class (see <see cref="Crews"/>); null for always.</summary>
        public Func<Settlement, ShipClass, bool> CanCrew;

        /// <summary>Whether a buyer (a trading company, see <see cref="Companies"/>) has already paid for the hull a yard is
        /// about to lay down, so its owner's treasury is not charged; null for nobody.</summary>
        public Func<Settlement, ShipClass, double, bool> PaidFor;

        /// <summary>Raised when a ship loads a cargo for sale (the ship and the settlement it bought at).</summary>
        public Action<Ship, Settlement> Loaded;

        /// <summary>
        /// How a ship's owners weigh a run from one settlement (null: the cargo already aboard) to another: multiplies a
        /// paying run's score in <see cref="PlanTrade"/>, and 0 rules the run out (see <see cref="Companies.RouteFactor"/>).
        /// Null for 1.
        /// </summary>
        public Func<Ship, Settlement, Settlement, double> RouteFactor;

        double RouteFactorOf(Ship ship, Settlement from, Settlement to) => RouteFactor == null ? 1 : RouteFactor(ship, from, to);

        /// <summary>Port tax on every sale and purchase, paid to the port's owner.</summary>
        public const double PortTax = 0.04;

        /// <summary>
        /// The smallest load worth booking, tonnes, for a market that wants less than a usual load (an outpost's
        /// medicine, a fab's platinum); bigger markets are sent at least 2% of the hold, up to 20 t.
        /// </summary>
        public const double MinLoadTonnes = 2;

        public const double TickDays = 0.25;

        /// <summary>Ships' propellant purchases count toward a market's demand averaged over about this many days.</summary>
        public const double PropellantDemandDays = 10;

        /// <summary>A hyperlane's toll for one ride: a flat fee plus so much per tonne of ship (hull, tanks and hold).</summary>
        public const double LaneTollBase = 5000, LaneTollPerTonne = 0.5;

        readonly Random rng;
        double nextReviewJd, lastReviewJd;

        public Economy(Diplomacy diplomacy, double startJd, int seed = 2077)
        {
            Diplomacy = diplomacy;
            Jd = startJd;
            nextReviewJd = startJd + 10;
            lastReviewJd = startJd;
            rng = new Random(seed);
        }

        public Settlement Add(string name, int body, SettlementKind kind, Faction owner, double population)
        {
            var s = new Settlement { Id = Settlements.Count, Name = name, Body = body, Kind = kind, Owner = owner, Population = population, InitialPopulation = population };
            Settlements.Add(s);
            return s;
        }

        /// <summary>Recomputes every settlement's full-capacity demand and supply and stocks it at its target cover.</summary>
        public void Initialise()
        {
            foreach (var s in Settlements)
            {
                RecomputeFlows(s);
                for (int i = 0; i < GoodInfo.Count; i++)
                {
                    s.Stock[i] = s.Target((Good)i) * (0.8 + rng.NextDouble() * 0.5);
                    s.Satisfaction[i] = 1;
                }
                s.RefreshPrices();
                People.AssignLabour(s);
            }
            People.Migrate(Settlements, Diplomacy, 0);
        }

        /// <summary>
        /// Opens a settlement added in play (a colony founded, U.SPC-T98) for business: its flows, prices and crews
        /// as <see cref="Initialise"/> sets them, but with only the stock it was given.
        /// </summary>
        public void Open(Settlement s)
        {
            RecomputeFlows(s);
            for (int i = 0; i < GoodInfo.Count; i++) s.Satisfaction[i] = 1;
            s.RefreshPrices();
            People.AssignLabour(s);
        }

        public IEnumerable<Settlement> At(int body)
        {
            foreach (var s in Settlements)
                if (s.Body == body) yield return s;
        }

        // ---------------------------------------------------------------- production and consumption

        /// <summary>Runs the markets forward to <paramref name="jd"/> in ticks of about <see cref="TickDays"/>.</summary>
        public void AdvanceTo(double jd)
        {
            double gap = jd - Jd;
            if (gap <= 0) return;
            // Very large jumps (a year per second) take coarser ticks; every update is stable at any step.
            double tick = Math.Max(TickDays, gap / 600);
            while (Jd < jd)
            {
                double dt = Math.Min(tick, jd - Jd);
                Tick(dt);
                Jd += dt;
                if (Jd >= nextReviewJd)
                {
                    Review();
                    nextReviewJd = Jd + 10;
                }
            }
        }

        void Tick(double dt)
        {
            foreach (var s in Settlements)
            {
                // A fallen haven is an empty shell until it is rebuilt: nobody there to work, eat or fence
                // (and the population floor below would otherwise bring its people straight back).
                if (s.Fallen)
                {
                    // ... and nobody there going without.
                    Array.Fill(s.Satisfaction, 1.0);
                    s.DaysSinceCapture += dt;
                    continue;
                }
                FoldPropellantSales(s, dt);
                RecomputeFlows(s);
                People.AssignLabour(s);
                Produce(s, dt);
                Consume(s, dt);
                if (s.Kind == SettlementKind.Haven) Fence(s, dt);
                // A lawless station's middlemen pass what they bought on the black market into its own stores (U.SPC-T94).
                if (s.Lawless) BlackMarket.Launder(s, dt);
                s.DaysSinceCapture += dt;
                s.RefreshPrices();
            }
        }

        static void RecomputeFlows(Settlement s)
        {
            Array.Clear(s.Demand, 0, s.Demand.Length);
            Array.Clear(s.Supply, 0, s.Supply.Length);
            foreach (var (g, perPerson) in s.PersonNeeds) s.Demand[(int)g] += perPerson * s.Population * s.RationOf(g);
            for (int i = 0; i < GoodInfo.Count; i++) s.Demand[i] += s.ExtraDemand[i] + s.PropellantDemand[i];
            foreach (var f in s.Facilities)
            {
                foreach (var (g, rate) in f.Recipe.Inputs) s.Demand[(int)g] += rate * f.Level;
                foreach (var (g, rate) in f.Recipe.Outputs) s.Supply[(int)g] += rate * f.Level;
            }
        }

        /// <summary>Folds what ships bought as propellant since the last tick into the market's smoothed propellant demand.</summary>
        static void FoldPropellantSales(Settlement s, double dt)
        {
            double k = 1 - Math.Exp(-dt / PropellantDemandDays);
            for (int i = 0; i < GoodInfo.Count; i++)
            {
                double rate = s.PropellantSold[i] / dt;
                s.PropellantDemand[i] += (rate - s.PropellantDemand[i]) * k;
                s.PropellantSold[i] = 0;
            }
        }

        void Produce(Settlement s, double dt)
        {
            // A recently captured settlement is in disarray: output recovers over 60 days.
            double disruption = Math.Min(1, 0.25 + s.DaysSinceCapture / 60);
            // ... and restive under an occupier: unrest idles up to half of it (U.SPC-T92).
            disruption *= 1 - 0.5 * Math.Max(0, Math.Min(1, s.Unrest));
            double wages = 0;
            foreach (var f in s.Facilities)
            {
                // Life support keeps running through a change of owner; industry does not. Neither runs
                // past its crew: an understaffed facility makes less, and below a minimum crew nothing.
                bool lifeSupport = f.LifeSupport;
                // A shock to the facility itself (an accident, a blight) and a strike (which life support works through).
                double run = (lifeSupport ? 1 : disruption * s.IndustryFactor) * f.Impairment * People.CrewFactor(f.Staffing), efficiency = 1;
                var inputs = f.Recipe.Inputs;
                for (int k = 0; k < inputs.Length; k++)
                {
                    double need = inputs[k].perDay * f.Level * dt;
                    if (need <= 0) continue;
                    double have = Math.Min(1, s.Stock[(int)inputs[k].good] / need);
                    if (f.Recipe.Limiting[k]) run = Math.Min(run, have);
                    else if (!lifeSupport) efficiency = Math.Min(efficiency, Recipe.MinorFloor + (1 - Recipe.MinorFloor) * have);
                }
                foreach (var (g, rate) in f.Recipe.Outputs)
                {
                    // Full warehouses throttle production rather than overflow.
                    double room = s.Capacity(g) - s.Stock[(int)g];
                    double make = rate * f.Level * dt * efficiency;
                    if (make > 0) run = Math.Min(run, Math.Max(0, room) / make);
                }
                run = Math.Max(0, Math.Min(1, run));
                f.Utilisation = run * efficiency;
                foreach (var (g, rate) in f.Recipe.Inputs) s.Stock[(int)g] = Math.Max(0, s.Stock[(int)g] - rate * f.Level * dt * run * efficiency);
                foreach (var (g, rate) in f.Recipe.Outputs) s.Stock[(int)g] += rate * f.Level * dt * run * efficiency;
                if (f.Recipe.Kind == IndustryKind.Mine) f.Extracted += f.Recipe.Outputs[0].perDay * f.Level * dt * run * efficiency;
                if (f.Recipe.Kind == IndustryKind.Shipyard)
                {
                    double points = f.Recipe.Inputs[0].perDay * f.Level * dt * run;
                    s.BuildPoints = Math.Min(s.BuildPoints + points, 400000);
                }
                // Workers take their share of what the facility actually added.
                wages += People.ValueAdded(s, f) * f.Utilisation * People.LabourShare;
            }
            // Havens pay no taxes: whatever their crews earn stays off the books.
            s.WageBill = s.Kind == SettlementKind.Haven ? 0 : wages;
            s.WageTax = s.WageBill * People.WageTaxRate;
            if (s.Owner != null) s.Owner.Treasury += s.WageTax * dt;
        }

        static void Consume(Settlement s, double dt)
        {
            for (int i = 0; i < s.Satisfaction.Length; i++) s.Satisfaction[i] = 1;
            foreach (var (g, perPerson) in s.PersonNeeds)
            {
                int i = (int)g;
                double need = perPerson * s.Population * s.RationOf(g) * dt;
                if (need <= 0) continue;
                double eaten = Math.Min(need, s.Stock[i]);
                s.Stock[i] -= eaten;
                s.Satisfaction[i] = eaten / need;
            }

            // Fed and watered settlements grow up to 4% a year, less without medicine (down to a slow
            // decline of about 5% a year with none); hunger or thirst shrinks them fast, to a hard core.
            double sustenance = Math.Min(s.Satisfaction[(int)Good.Food], s.Satisfaction[(int)Good.Water]);
            double health = s.Satisfaction[(int)Good.MedicalSupplies];
            double rate = People.GrowthRate(sustenance, health);
            double grown = s.Population * (1 + rate * dt), cap = s.InitialPopulation * People.MaxGrowth;
            // Growth with no room to grow into is people with nowhere to go: the pressure that founds colonies (U.SPC-T98).
            if (grown > cap) People.AddOverflow(s, grown - Math.Max(s.Population, cap));
            // ... and in a crowded place, a share of those born would rather go than squeeze in.
            else if (grown > s.Population) People.AddOverflow(s, (grown - s.Population) * People.Crowding(s));
            s.Population = Math.Max(s.InitialPopulation * People.MinShare, Math.Min(cap, grown));

            // How people have been living, remembered for about a month: what draws or drives migrants.
            double k = 1 - Math.Exp(-dt / People.WellbeingDays);
            s.Wellbeing += (sustenance - s.Wellbeing) * k;
            s.HealthWellbeing += (health - s.HealthWellbeing) * k;
        }

        /// <summary>A haven's fences smuggle loot onward to the black market: stock drains, the owner is paid.</summary>
        static void Fence(Settlement s, double dt)
        {
            double share = 1 - Math.Exp(-0.03 * dt);
            for (int i = 0; i < GoodInfo.Count; i++)
            {
                double moved = Math.Max(0, s.Stock[i] - s.Target((Good)i)) * share;
                if (moved <= 0) continue;
                s.Stock[i] -= moved;
                s.Owner.Treasury += moved * GoodInfo.Of((Good)i).BasePrice * 0.5;
            }
        }

        /// <summary>Every 10 days: facilities that sell dear expand, those glutted shrink; yards lay down hulls.</summary>
        void Review()
        {
            // People move toward food, water and work, then the facilities follow the market.
            People.Migrate(Settlements, Diplomacy, Jd - lastReviewJd);
            lastReviewJd = Jd;
            foreach (var s in Settlements)
            {
                // A fallen haven makes and trades nothing until it is rebuilt: its last prices are no market to follow.
                if (s.Fallen) continue;
                foreach (var f in s.Facilities)
                {
                    if (f.Recipe.Kind == IndustryKind.Shipyard) continue;
                    double margin = Margin(s, f.Recipe);
                    double step = 0;
                    // An industry its owner favours grows on a thinner margin and is kept up through a glut (U.SPC-T423).
                    var (favoured, bonus) = Priority != null ? Priority(s, f.Recipe.Kind) : (false, 0.0);
                    double grow = favoured ? 1.0 : 1.4, shrink = favoured ? 0.4 : 0.7;
                    // Expanding takes hands: a facility short of crew does not grow.
                    if (margin > grow && f.Utilisation > 0.8 && f.Staffing >= 0.95) step = 0.03 + bonus;
                    else if (margin < shrink || f.Utilisation < 0.2) step = -0.02;
                    f.Level = Math.Max(f.BaseLevel * 0.4, Math.Min(f.BaseLevel * 3, f.Level * (1 + step)));
                }

                var yard = s.Shipyard;
                if (yard == null || ChooseBuild == null) continue;
                var cls = ChooseBuild(s);
                s.OnSlip = cls;
                if (cls == null) continue;
                double cost = HullCost(cls.Value);
                if (s.BuildPoints < cost) continue;
                double price = HullPrice(cls.Value);
                // A hull a company ordered is already paid for; the owner pays for its own.
                bool prepaid = PaidFor != null && PaidFor(s, cls.Value, price);
                if (!prepaid && s.Owner.Treasury < price) continue;
                // Nor is a hull laid down that nobody could crew.
                if (CanCrew != null && !CanCrew(s, cls.Value)) continue;
                s.BuildPoints -= cost;
                if (!prepaid) s.Owner.Treasury -= price;
                ShipBuilt?.Invoke(s, cls.Value, Jd);
            }
            Colonisation?.Review(this, Jd);
        }

        /// <summary>Value of a recipe's outputs over its inputs at local prices (mines count only their running costs).</summary>
        static double Margin(Settlement s, Recipe r)
        {
            double input = 0, output = 0;
            foreach (var (g, rate) in r.Inputs) input += rate * s.Price[(int)g];
            foreach (var (g, rate) in r.Outputs) output += rate * s.Price[(int)g];
            return input <= 0 ? 2 : output / input;
        }

        /// <summary>Build points (tonnes of alloys through the yard) to lay down a hull.</summary>
        public static double HullCost(ShipClass c)
        {
            switch (c)
            {
                case ShipClass.Carrier: return 90000;
                case ShipClass.Battleship: return 70000;
                case ShipClass.Cruiser: return 30000;
                case ShipClass.Destroyer: return 12000;
                case ShipClass.Freighter: return 9000;
                case ShipClass.Miner: return 7000;
                case ShipClass.Courier: return 1500;
                case ShipClass.Explorer: return 5000;
                case ShipClass.OreTug: return 6000;
                // Half an ore tug's frame and one drive fewer (U.SPC-T522).
                case ShipClass.CaptureTug: return 2800;
                case ShipClass.Interceptor: return 2200;
                case ShipClass.Bomber: return 4500;
                // An interceptor's airframe round a sensor and jamming suite: the electronics are the price (U.SPC-T485).
                case ShipClass.Recon: return 3800;
                case ShipClass.LaneTender: return 6000;
                case ShipClass.GatePicket: return 2500;
                // A battlecruiser's hull, its sensor array, communications suite and targeting computers dearer than guns (U.SPC-T477).
                case ShipClass.CommandCruiser: return 52000;
                // Smaller than a destroyer, but its engines and antimatter magazines are dear (U.SPC-T475).
                case ShipClass.Frigate: return 10000;
                // A small hull with no drive room: cheap enough to keep one at every port (U.SPC-T476).
                case ShipClass.Corvette: return 6000;
                // A bomber's armour round a troop bay and a docking collar (U.SPC-T486).
                case ShipClass.AssaultShuttle: return 4000;
                // More plate than a battleship, its own FTL drive and a siege engineering bay (U.SPC-T478).
                case ShipClass.GateBreaker: return 80000;
                default: return 3000;
            }
        }

        /// <summary>
        /// Share of a warship's (or platform's) price its navy pays a year to keep it in service: crews,
        /// fuel, spares. Operating and support is about 70% of a US weapon system's life-cycle cost (GAO),
        /// so over 30 years each year costs 0.7 / 0.3 / 30 of the purchase.
        /// </summary>
        public const double UpkeepPerYear = 0.078;

        /// <summary>Credits the owner pays the yard (crew, weapons, fit-out) on top of the materials.</summary>
        public static double HullPrice(ShipClass c) => HullCost(c) * 1200;

        // ---------------------------------------------------------------- trade

        public static double CapacityOf(ShipClass c)
        {
            switch (c)
            {
                // A truss freighter's hold is its stock design's pods (U.SPC-T482): 8 x 2,500 t.
                case ShipClass.Freighter: return BulkFreighters.StockPods * BulkFreighters.PodTonnes;
                case ShipClass.Miner: return 30000;
                case ShipClass.Courier: return 400;
                default: return 0;
            }
        }

        /// <summary>The goods a hull is fitted to carry: bulk holds for haulers, secure holds for couriers.</summary>
        public static bool Carries(ShipClass c, Good g)
        {
            var cat = GoodInfo.Of(g).Category;
            switch (c)
            {
                case ShipClass.Freighter: return true;
                case ShipClass.Miner: return cat == GoodCategory.Mineral || g == Good.Water || g == Good.Nitrogen || g == Good.Hydrocarbons;
                case ShipClass.Courier:
                    return g == Good.MedicalSupplies || g == Good.Electronics || g == Good.Platinum || g == Good.RareEarths || g == Good.HeliumThree;
                default: return false;
            }
        }

        /// <summary>Crew, upkeep and insurance a day under way (propellant and tolls are counted separately).</summary>
        public static double RunningCostPerDay(ShipClass c) => c == ShipClass.Courier ? 400 : c == ShipClass.Miner ? 1200 : 1500;

        /// <summary>The toll for one hyperlane ride by a ship of <paramref name="massTonnes"/>, credits.</summary>
        public static double LaneToll(double massTonnes) => LaneTollBase + LaneTollPerTonne * Math.Max(0, massTonnes);

        /// <summary>The toll a ship of <paramref name="cls"/> pays for one ride: a bulk freighter rides at its bulk share
        /// (<see cref="BulkFreighters.LaneTollShare"/>, U.SPC-T482), any other ship the full toll for its mass.</summary>
        public static double LaneToll(ShipClass cls, double massTonnes) => LaneToll(massTonnes) * BulkFreighters.LaneTollShareOf(cls);

        /// <summary>What a run costs and whether the tanks cover it (see <see cref="PriceRun"/>).</summary>
        struct Run
        {
            public double Days, Propellant, Cost;
            public bool Reachable;
        }

        /// <summary>
        /// Prices a run from <paramref name="from"/> to <paramref name="to"/> by what it really takes: the
        /// days under way at the ship's running cost, the propellant its burns throw out with
        /// <paramref name="cargoTonnes"/> aboard and <paramref name="tanks"/> in the tanks (rocket equation)
        /// at <paramref name="fuelPrice"/> a tonne, and a toll for every lane it rides. Unreachable if the
        /// tanks do not hold that propellant and a reserve. Without <see cref="EstimateTrip"/>, days alone.
        /// </summary>
        Run PriceRun(Ship ship, int from, int to, double jd, double cargoTonnes, double tanks, double fuelPrice)
        {
            var cls = ship.Spec.Class;
            if (EstimateTrip == null)
            {
                double d = TravelDays(ship, from, to, jd);
                return new Run { Days = d, Cost = d * RunningCostPerDay(cls), Reachable = true };
            }
            var trip = EstimateTrip(ship, from, to, jd);
            double days = Math.Max(0.05, trip.Days) + 1.0;
            double burn = Propulsion.PropellantFor(ship, trip.DeltaVKmS, cargoTonnes, tanks);
            double mass = ship.DryMassTonnes + cargoTonnes + tanks;
            return new Run
            {
                Days = days,
                Propellant = burn,
                Cost = days * RunningCostPerDay(cls) + burn * fuelPrice + trip.LaneHops * LaneToll(cls, mass),
                Reachable = burn * (1 + Propulsion.PlanningReserve) <= tanks,
            };
        }

        /// <summary>Whether <paramref name="ship"/>'s tanks, as they stand, cover a run from <paramref name="from"/> to <paramref name="to"/> (always true when trips are not priced).</summary>
        public bool CanReach(Ship ship, int from, int to, double jd) =>
            PriceRun(ship, from, to, jd, ship.HasCargo ? ship.CargoTonnes : 0, ship.Propellant, 0).Reachable;

        // A fallen haven makes and trades nothing until it is rebuilt.
        public bool CanTrade(Ship ship, Settlement s) => !s.Fallen && Diplomacy.CanDockAt(ship.Faction, s) && (DockAllowed == null || DockAllowed(ship, s));

        /// <summary>Whether the law bars <paramref name="ship"/> from a port its faction could otherwise use (see <see cref="Law.CanDock"/>).</summary>
        public bool LawRefuses(Ship ship, Settlement s) => DockAllowed != null && Diplomacy.CanDockAt(ship.Faction, s) && !DockAllowed(ship, s);

        /// <summary>The law's say on docking: false bars a ship from a port (its commander's standing there, see <see cref="Law.CanDock"/>); null for no law.</summary>
        public Func<Ship, Settlement, bool> DockAllowed;

        /// <summary>What standing does to a ship's prices at a port: the player's hold purchases cost this much more
        /// and its sales fetch this much less (see <see cref="Law.PriceFactor"/>); null for 1.</summary>
        public Func<Ship, Settlement, double> PriceFactor;

        double PriceFactorOf(Ship ship, Settlement s) => PriceFactor == null ? 1 : Math.Max(0.1, PriceFactor(ship, s));

        /// <summary>Sells whatever is in the hold at the best market at the body the ship has reached.</summary>
        public void OnArrive(Ship ship, int body, double jd)
        {
            if (!ship.HasCargo) return;
            Settlement best = null;
            double bestScore = 0, bestValue = 0;
            foreach (var s in At(body))
            {
                if (!CanTrade(ship, s) || s.Demand[(int)ship.CargoGood] <= 0) continue;
                double v = s.SaleValue(ship.CargoGood, ship.CargoTonnes);
                // The booked market wins a near tie; it still pays only what the cargo is worth there.
                double score = s == ship.CargoDestination ? v * 1.05 : v;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestValue = v;
                    best = s;
                }
            }
            Release(ship);
            if (best == null)
            {
                ship.CargoDestination = null;
                ship.Cargo = CargoText(ship); // the label must not keep naming the port that refused it
                return; // Nobody here will buy; the trader carries it on.
            }
            Sell(ship, best, bestValue);
        }

        void Sell(Ship ship, Settlement s, double value)
        {
            int g = (int)ship.CargoGood;
            s.Stock[g] += ship.CargoTonnes;
            double tax = value * PortTax;
            s.Owner.Treasury += tax;
            // Paid by the owner who offered it: a port taken since the load was booked does not pay its old owner's bonus.
            if (s == ship.CargoDestination && ship.CargoBonus > 0 && (ship.CargoBonusPayer == null || s.Owner == ship.CargoBonusPayer))
            {
                s.Owner.Treasury -= ship.CargoBonus;
                value += ship.CargoBonus;
            }
            ship.CargoBonus = 0;
            double profit = value - tax - ship.CargoCost;
            ship.Credits += profit;
            ship.Faction.Treasury += value - tax;
            s.TradeVolumeCredits += value;
            Sold?.Invoke(ship, s, ship.CargoGood, ship.CargoTonnes);
            ship.HasCargo = false;
            ship.CargoTonnes = 0;
            ship.CargoCost = 0;
            ship.CargoDestination = null;
            ship.Cargo = null;
            s.RefreshPrices();
        }

        /// <summary>Cancels a ship's booking of its cargo at its destination (it arrived, died or changed plans).</summary>
        public void Release(Ship ship)
        {
            if (ship.CargoDestination != null && ship.HasCargo)
                ship.CargoDestination.Inbound[(int)ship.CargoGood] = Math.Max(0, ship.CargoDestination.Inbound[(int)ship.CargoGood] - ship.CargoTonnes);
        }

        /// <summary>Rebuilds every market's book of deliveries on the way from what ships are actually carrying.</summary>
        public void RecountInbound(IEnumerable<Ship> ships)
        {
            foreach (var s in Settlements) Array.Clear(s.Inbound, 0, s.Inbound.Length);
            foreach (var ship in ships)
                if (!ship.Destroyed && ship.HasCargo && ship.CargoDestination != null)
                    ship.CargoDestination.Inbound[(int)ship.CargoGood] += ship.CargoTonnes;
        }

        struct Option
        {
            public Settlement From, To;
            public Good Good;
            public double Tonnes, Score, Cost, Bonus;
            public bool Empty;
        }

        /// <summary>
        /// Picks a trader's next port from <paramref name="here"/> and loads its cargo: the trade with the
        /// best profit per day, among the top few (so the fleet does not stampede), or an empty run to
        /// where the best trades start. Returns -1 if nothing is worth doing.
        /// </summary>
        public int PlanTrade(Ship ship, int here, double jd)
        {
            var cls = ship.Spec.Class;
            var options = new List<Option>();

            double fuelHere = PropellantPriceAt(ship, here);
            if (ship.HasCargo)
            {
                // Already loaded (nobody here wanted it): find where it sells best.
                foreach (var to in Settlements)
                {
                    if (to.Body == here || !CanTrade(ship, to) || to.Demand[(int)ship.CargoGood] <= 0) continue;
                    var run = PriceRun(ship, here, to.Body, jd, ship.CargoTonnes, ship.Propellant, fuelHere);
                    if (!run.Reachable) continue;
                    double factor = RouteFactorOf(ship, null, to);
                    if (factor <= 0) continue;
                    double value = to.SaleValue(ship.CargoGood, ship.CargoTonnes) * (1 - DangerAt(to.Body));
                    double score = (value - run.Cost) / run.Days;
                    options.Add(new Option { To = to, Good = ship.CargoGood, Tonnes = ship.CargoTonnes, Score = score > 0 ? score * factor : score });
                }
                if (options.Count == 0) return LowOnPropellant(ship, here, jd);
                var pick = Choose(options);
                Release(ship);
                ship.CargoBonus = 0;
                ship.CargoDestination = pick.To;
                pick.To.Inbound[(int)pick.Good] += pick.Tonnes;
                ship.Cargo = CargoText(ship);
                return pick.To.Body;
            }

            foreach (var from in At(here))
            {
                if (!CanTrade(ship, from)) continue;
                AddLoadedOptions(ship, from, here, jd, 0, 0, ship.Propellant, fuelHere, options);
            }

            if (options.Count == 0 || Best(options) <= 0)
            {
                // Nothing worth carrying from here: reposition, empty, to where the trades are.
                foreach (var from in Settlements)
                {
                    if (from.Body == here || !CanTrade(ship, from)) continue;
                    var leg = PriceRun(ship, here, from.Body, jd, 0, ship.Propellant, fuelHere);
                    if (!leg.Reachable) continue;
                    // It fills up again there if the port sells propellant; else it goes on with what is left.
                    double fuelThere = PropellantPriceAt(ship, from.Body);
                    double tanks = HasPropellant(ship, from) ? ship.PropellantCapacity : ship.Propellant - leg.Propellant;
                    var onward = new List<Option>();
                    AddLoadedOptions(ship, from, from.Body, jd + leg.Days, leg.Days, leg.Cost - leg.Days * RunningCostPerDay(cls), tanks, fuelThere, onward);
                    double best = Best(onward);
                    if (best > 0) options.Add(new Option { To = from, Score = best * 0.85, Empty = true });
                }
            }

            options.RemoveAll(o => o.Score <= 0);
            if (options.Count == 0) return LowOnPropellant(ship, here, jd);
            var choice = Choose(options);
            if (!choice.Empty) Load(ship, choice);
            else ship.Cargo = "Empty, repositioning to " + choice.To.Name;
            return choice.To.Body;
        }

        /// <summary>
        /// A captain on a fixed trade route (U.SPC-T102): loads, at <paramref name="here"/>, the cargo that earns the
        /// most per day carried to the route's next stop, <paramref name="toBody"/>, if any run there pays at all.
        /// Paid for and booked as <see cref="PlanTrade"/> does. Returns whether it loaded anything.
        /// </summary>
        public bool LoadFor(Ship ship, int here, int toBody, double jd)
        {
            if (ship.HasCargo || ship.CapacityTonnes <= 0) return false;
            double fuelHere = PropellantPriceAt(ship, here);
            var options = new List<Option>();
            foreach (var from in At(here))
            {
                if (!CanTrade(ship, from)) continue;
                AddLoadedOptions(ship, from, here, jd, 0, 0, ship.Propellant, fuelHere, options);
            }
            Option best = default;
            bool found = false;
            foreach (var o in options)
                if (o.To.Body == toBody && o.Score > 0 && (!found || o.Score > best.Score))
                {
                    best = o;
                    found = true;
                }
            if (!found) return false;
            Load(ship, best);
            return true;
        }

        /// <summary>
        /// Puts <paramref name="tonnes"/> of <paramref name="good"/> the ship came by without buying (ore cut from a
        /// mining claim) in its hold, at no cost, and books it for the best market it can reach; returns that body, or -1.
        /// </summary>
        public int LoadOwn(Ship ship, Good good, double tonnes, int here, double jd)
        {
            if (ship.HasCargo || tonnes <= 0) return -1;
            ship.HasCargo = true;
            ship.CargoGood = good;
            ship.CargoTonnes = tonnes;
            ship.CargoCost = 0;
            ship.CargoBonus = 0;
            ship.CargoDestination = null;
            ship.Cargo = CargoText(ship);
            return PlanTrade(ship, here, jd);
        }

        /// <summary>Sells <paramref name="tonnes"/> of an own-mined <paramref name="good"/> straight to a market on <paramref name="here"/>
        /// that wants it (the claim body's own port); false, hold untouched, when none does.</summary>
        public bool SellOwn(Ship ship, Good good, double tonnes, int here, double jd)
        {
            if (ship.HasCargo || tonnes <= 0) return false;
            bool wanted = false;
            foreach (var s in At(here))
                if (CanTrade(ship, s) && s.Demand[(int)good] > 0) { wanted = true; break; }
            if (!wanted) return false;
            ship.HasCargo = true;
            ship.CargoGood = good;
            ship.CargoTonnes = tonnes;
            ship.CargoCost = 0;
            ship.CargoBonus = 0;
            ship.CargoDestination = null;
            OnArrive(ship, here, jd);
            if (!ship.HasCargo) return true;
            ship.HasCargo = false;
            ship.CargoTonnes = 0;
            ship.Cargo = null;
            return false;
        }

        /// <summary>
        /// The loads a ship could take from <paramref name="from"/> and where to: each priced by its run
        /// (<see cref="PriceRun"/>) plus <paramref name="extraDays"/> and <paramref name="extraCost"/> already
        /// spent getting there, leaving out any its <paramref name="tanks"/> cannot carry that far.
        /// </summary>
        void AddLoadedOptions(Ship ship, Settlement from, int fromBody, double jd, double extraDays, double extraCost, double tanks, double fuelPrice, List<Option> into)
        {
            var cls = ship.Spec.Class;
            double capacity = ship.CapacityTonnes;
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                var good = (Good)g;
                if (!Carries(cls, good)) continue;
                double available = from.Available(good);
                double minimum = Math.Min(capacity * 0.02, 20);
                if (available < Math.Min(minimum, MinLoadTonnes)) continue;
                foreach (var to in Settlements)
                {
                    if (to.Body == fromBody || !CanTrade(ship, to)) continue;
                    double factor = RouteFactorOf(ship, from, to);
                    if (factor <= 0) continue;
                    double tonnes = Math.Min(Math.Min(capacity, available), to.Absorb(good));
                    // A small market's whole want of a good can be less than a usual load: it takes what it wants
                    // (down to MinLoadTonnes), if a run that small pays.
                    if (tonnes < Math.Min(minimum, Math.Max(MinLoadTonnes, to.Target(good) * 0.5))) continue;
                    var run = PriceRun(ship, fromBody, to.Body, jd, tonnes, tanks, fuelPrice);
                    if (!run.Reachable) continue;
                    double cost = from.PurchaseCost(good, tonnes);
                    if (ship.Faction.IsHouse && cost * (1 + PortTax) > ship.Faction.Treasury)
                    {
                        // The house cannot overdraw its account: take only the lot it can pay for (U.SPC-T539).
                        double afford = Math.Max(0, ship.Faction.Treasury) / (cost * (1 + PortTax)) * 0.95;
                        tonnes *= afford;
                        if (tonnes < Math.Min(minimum, MinLoadTonnes)) continue;
                        cost = from.PurchaseCost(good, tonnes);
                        if (cost * (1 + PortTax) > ship.Faction.Treasury) continue;
                        run = PriceRun(ship, fromBody, to.Body, jd, tonnes, tanks, fuelPrice);
                        if (!run.Reachable) continue;
                    }
                    double days = run.Days + extraDays;
                    double bonus = ContractBonus(to, good, tonnes);
                    double value = (SaleEstimate(to, good, tonnes) + bonus) * (1 - DangerAt(to.Body));
                    double profit = value - cost - run.Cost - extraCost - extraDays * RunningCostPerDay(cls);
                    double score = profit / days;
                    into.Add(new Option { From = from, To = to, Good = good, Tonnes = tonnes, Cost = cost, Bonus = bonus, Score = score > 0 ? score * factor : score });
                }
            }
        }

        /// <summary>
        /// Owners post supply contracts for essentials their settlements are short of: a flat fee per
        /// run plus a premium per tonne, scaled by how short they are, paid on delivery.
        /// </summary>
        static double ContractBonus(Settlement to, Good g, double tonnes)
        {
            if (GoodInfo.Of(g).Category != GoodCategory.Essential || to.Owner.IsPirate || to.Owner.Treasury <= 0) return 0;
            double expected = to.Stock[(int)g] + to.Inbound[(int)g];
            double target = to.Target(g);
            if (target <= 0 || expected >= target * 0.7) return 0;
            double shortfall = 1 - expected / (target * 0.7);
            return shortfall * (600000 + tonnes * GoodInfo.Of(g).BasePrice * 1.5);
        }

        /// <summary>What a load will fetch once the deliveries already heading there have landed.</summary>
        static double SaleEstimate(Settlement to, Good g, double tonnes)
        {
            const int steps = 4;
            double value = 0, stock = to.Stock[(int)g] + to.Inbound[(int)g];
            for (int k = 0; k < steps; k++)
            {
                double chunk = tonnes / steps;
                value += to.PriceAt(g, stock + chunk * 0.5) * chunk;
                stock += chunk;
            }
            return value * 0.97 * (1 - PortTax);
        }

        void Load(Ship ship, Option o)
        {
            int g = (int)o.Good;
            o.From.Stock[g] = Math.Max(0, o.From.Stock[g] - o.Tonnes);
            o.From.Owner.Treasury += o.Cost * PortTax;
            o.From.TradeVolumeCredits += o.Cost;
            o.To.Inbound[g] += o.Tonnes;
            ship.HasCargo = true;
            ship.CargoGood = o.Good;
            ship.CargoTonnes = o.Tonnes;
            ship.CargoCost = o.Cost * (1 + PortTax);
            ship.Faction.Treasury -= ship.CargoCost;
            ship.CargoBonus = o.Bonus;
            ship.CargoBonusPayer = o.To.Owner;
            ship.CargoDestination = o.To;
            ship.Cargo = CargoText(ship);
            o.From.RefreshPrices();
            Loaded?.Invoke(ship, o.From);
        }

        /// <summary>
        /// Loads a contract's lot (see <see cref="Contracts"/>): <paramref name="tonnes"/> of <paramref name="good"/>
        /// bought at <paramref name="from"/> as <see cref="Load"/> buys, booked for delivery at <paramref name="to"/>
        /// (on its inbound book) with no supply bonus: the contract pays for the run.
        /// </summary>
        public void LoadFor(Ship ship, Settlement from, Good good, double tonnes, Settlement to) =>
            Load(ship, new Option { From = from, To = to, Good = good, Tonnes = tonnes, Cost = from.PurchaseCost(good, tonnes) });

        /// <summary>
        /// Loads <paramref name="tonnes"/> of <paramref name="good"/> bought at <paramref name="from"/>
        /// for a market outside this economy (another system's, across the gates): paid for as
        /// <see cref="Load"/> pays, but booked at no settlement here. <paramref name="bound"/> names
        /// where it is going, for the HUD. Returns what it cost, tax included.
        /// </summary>
        public double LoadUnbooked(Ship ship, Settlement from, Good good, double tonnes, string bound)
        {
            int g = (int)good;
            double cost = from.PurchaseCost(good, tonnes);
            from.Stock[g] = Math.Max(0, from.Stock[g] - tonnes);
            from.Owner.Treasury += cost * PortTax;
            from.TradeVolumeCredits += cost;
            ship.HasCargo = true;
            ship.CargoGood = good;
            ship.CargoTonnes = tonnes;
            ship.CargoCost = cost * (1 + PortTax);
            ship.Faction.Treasury -= ship.CargoCost;
            ship.CargoBonus = 0;
            ship.CargoDestination = null;
            ship.Cargo = tonnes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " t " + GoodInfo.Of(good).Name + " for " + bound;
            from.RefreshPrices();
            Loaded?.Invoke(ship, from);
            return ship.CargoCost;
        }

        public static string CargoText(Ship ship)
        {
            if (!ship.HasCargo) return null;
            return ship.CargoTonnes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " t " + GoodInfo.Of(ship.CargoGood).Name +
                   (ship.CargoDestination != null ? " for " + ship.CargoDestination.Name : "");
        }

        /// <summary>
        /// Nothing worth doing from here: a ship running low makes for the nearest port it can reach that sells
        /// propellant, so it is not stranded somewhere with none; otherwise -1 (it waits for work).
        /// </summary>
        int LowOnPropellant(Ship ship, int here, double jd)
        {
            if (ship.PropellantFraction >= 0.5) return -1;
            var port = NearestPropellantPort(ship, here, jd);
            if (port == null) return -1;
            if (!ship.HasCargo) ship.Cargo = "Low on propellant, making for " + port.Name;
            return port.Body;
        }

        /// <summary>
        /// The quickest port from <paramref name="here"/> that sells <paramref name="ship"/> propellant and that
        /// its tanks can still reach, or -1 (also -1 if it can fill up where it is, or trips are not priced).
        /// </summary>
        public int NearestPropellant(Ship ship, int here, double jd) => NearestPropellantPort(ship, here, jd)?.Body ?? -1;

        Settlement NearestPropellantPort(Ship ship, int here, double jd)
        {
            if (EstimateTrip == null || HasPropellantAt(ship, here)) return null;
            Settlement best = null;
            double bestDays = double.MaxValue;
            foreach (var s in Settlements)
            {
                if (s.Body == here || !HasPropellant(ship, s)) continue;
                var run = PriceRun(ship, here, s.Body, jd, ship.HasCargo ? ship.CargoTonnes : 0, ship.Propellant, 0);
                if (!run.Reachable || run.Days >= bestDays) continue;
                bestDays = run.Days;
                best = s;
            }
            return best;
        }

        double TravelDays(Ship ship, int from, int to, double jd) =>
            Math.Max(0.05, EstimateTravelDays != null ? EstimateTravelDays(ship, from, to, jd) : 10) + 1.0;

        double DangerAt(int body) => Danger != null ? Math.Max(0, Math.Min(0.9, Danger(body))) : 0;

        static double Best(List<Option> options)
        {
            double best = double.NegativeInfinity;
            foreach (var o in options) best = Math.Max(best, o.Score);
            return best;
        }

        /// <summary>A weighted pick among the best few options, so traders spread across good routes.</summary>
        Option Choose(List<Option> options)
        {
            options.Sort((a, b) => b.Score.CompareTo(a.Score));
            int n = Math.Min(4, options.Count);
            double top = Math.Max(options[0].Score, 1e-9);
            double total = 0;
            var weights = new double[n];
            for (int i = 0; i < n; i++)
            {
                double w = Math.Max(0, options[i].Score / top);
                weights[i] = w * w * w;
                total += weights[i];
            }
            double pick = rng.NextDouble() * total;
            for (int i = 0; i < n; i++)
            {
                pick -= weights[i];
                if (pick <= 0) return options[i];
            }
            return options[0];
        }

        // ---------------------------------------------------------------- propellant

        /// <summary>Whether <paramref name="ship"/> can buy any propellant at <paramref name="s"/> (it may dock, and there is reaction mass to spare).</summary>
        public bool HasPropellant(Ship ship, Settlement s)
        {
            if (s.Fallen || !CanTrade(ship, s)) return false;
            foreach (var g in Propulsion.ReactionMass)
                if (s.Available(g) >= 1) return true;
            return false;
        }

        /// <summary>Whether any market at <paramref name="body"/> sells <paramref name="ship"/> propellant.</summary>
        public bool HasPropellantAt(Ship ship, int body)
        {
            foreach (var s in At(body))
                if (HasPropellant(ship, s)) return true;
            return false;
        }

        /// <summary>
        /// A tonne of propellant at <paramref name="s"/>, credits: the cheaper reaction mass it has to spare
        /// (water or hydrocarbons) and the helium-3 that burns it, with the port tax.
        /// </summary>
        public static double PropellantPrice(Settlement s)
        {
            double mass = double.MaxValue;
            foreach (var g in Propulsion.ReactionMass)
                if (s.Available(g) >= 1) mass = Math.Min(mass, s.Price[(int)g]);
            if (mass == double.MaxValue)
                foreach (var g in Propulsion.ReactionMass) mass = Math.Min(mass, s.Price[(int)g]);
            return (mass + Propulsion.HeliumThreePerTonne * s.Price[(int)Good.HeliumThree]) * (1 + PortTax);
        }

        /// <summary>The best price <paramref name="ship"/> can fill its tanks at <paramref name="body"/>, or the systemwide base price if nowhere there sells it.</summary>
        public double PropellantPriceAt(Ship ship, int body)
        {
            double best = double.MaxValue;
            foreach (var s in At(body))
                if (HasPropellant(ship, s)) best = Math.Min(best, PropellantPrice(s));
            return best == double.MaxValue ? Propulsion.PropellantBasePrice * (1 + PortTax) : best;
        }

        /// <summary>
        /// Tops up a ship's tanks at <paramref name="body"/> (an AI ship in port): from the cheapest market
        /// there it may trade at, reaction mass from what it can spare beyond its own reserve, paid from the
        /// ship's faction. Returns the tonnes taken on. A tank at or above the top-up threshold is left alone unless
        /// <paramref name="force"/> (a planned trip it cannot cover as it stands, U.SPC-T545).
        /// </summary>
        public double Refuel(Ship ship, int body, double jd, bool force = false)
        {
            if (ship.PropellantCapacity <= 0 || (!force && ship.PropellantFraction >= Propulsion.TopUpBelow)) return 0;
            var markets = new List<Settlement>();
            foreach (var s in At(body))
                if (HasPropellant(ship, s)) markets.Add(s);
            markets.Sort((a, b) => PropellantPrice(a).CompareTo(PropellantPrice(b)));
            double taken = 0;
            foreach (var s in markets)
            {
                double room = ship.PropellantCapacity - ship.Propellant;
                if (room < 1) break;
                taken += BuyPropellant(ship, s, room, mustAfford: ship.Faction.IsHouse, out _);
            }
            return taken;
        }

        /// <summary>
        /// Fills the player's piloted <paramref name="ship"/> at <paramref name="s"/>, as far as the market can
        /// spare and the faction's credits stretch. Reports the tonnes and what they cost.
        /// </summary>
        public TradeOutcome RefuelAt(Ship ship, Settlement s, out double tonnes, out double cost)
        {
            tonnes = cost = 0;
            if (!CanTrade(ship, s)) return LawRefuses(ship, s) ? TradeOutcome.Refused : TradeOutcome.CannotDock;
            double room = ship.PropellantCapacity - ship.Propellant;
            if (room < 0.01) return TradeOutcome.NoRoom;
            if (!HasPropellant(ship, s)) return TradeOutcome.NoStock;
            tonnes = BuyPropellant(ship, s, room, mustAfford: true, out cost);
            return tonnes > 0 ? TradeOutcome.Ok : TradeOutcome.CantAfford;
        }

        /// <summary>
        /// Buys up to <paramref name="tonnes"/> of propellant into a ship's tanks at <paramref name="s"/>:
        /// reaction mass cheapest first from what the market can spare, and the helium-3 that burns it from
        /// what it has (a port short of helium-3 still fills the tanks). Stock, prices, the port's tax and its
        /// propellant demand all follow; the ship's faction pays (only what it can, if <paramref name="mustAfford"/>).
        /// </summary>
        double BuyPropellant(Ship ship, Settlement s, double tonnes, bool mustAfford, out double cost)
        {
            cost = 0;
            double got = 0;
            var order = new List<Good>(Propulsion.ReactionMass);
            order.Sort((a, b) => s.Price[(int)a].CompareTo(s.Price[(int)b]));
            var payer = ship.Faction;
            foreach (var g in order)
            {
                double t = Math.Min(tonnes - got, s.Available(g));
                if (t < 0.01) continue;
                // Each tonne also takes its helium-3, where there is any, so price the pair together.
                double heRatio = s.Available(Good.HeliumThree) > 0 ? Propulsion.HeliumThreePerTonne : 0;
                double total = (s.PurchaseCost(g, t) + s.PurchaseCost(Good.HeliumThree, t * heRatio)) * (1 + PortTax);
                if (mustAfford && total > payer.Treasury - cost)
                {
                    double afford = Math.Max(0, payer.Treasury - cost);
                    if (afford <= 0 || total <= 0) break;
                    t *= afford / total * 0.99;
                    if (t < 0.01) break;
                }
                got += t;
                cost += Purchase(s, g, t);
                double he = Math.Min(t * heRatio, s.Available(Good.HeliumThree));
                if (he > 0) cost += Purchase(s, Good.HeliumThree, he);
            }
            if (got <= 0) return 0;
            ship.Propellant += got;
            payer.Treasury -= cost;
            ship.Credits -= cost;
            s.RefreshPrices();
            return got;
        }

        /// <summary>Takes <paramref name="tonnes"/> of a good out of a market as propellant; returns what it cost with tax.</summary>
        static double Purchase(Settlement s, Good g, double tonnes)
        {
            double price = s.PurchaseCost(g, tonnes);
            s.Stock[(int)g] = Math.Max(0, s.Stock[(int)g] - tonnes);
            s.Owner.Treasury += price * PortTax;
            s.TradeVolumeCredits += price;
            s.PropellantSold[(int)g] += tonnes;
            return price * (1 + PortTax);
        }

        // ---------------------------------------------------------------- player trade

        /// <summary>Result of one manual buy/sell click, for the HUD to pick a toast from. <see cref="Ok"/>
        /// also covers a partial fill (ran out of stock, credits or hold room partway through a multi-crate
        /// request), since part of the trade still went through.</summary>
        public enum TradeOutcome
        {
            Ok,
            CannotDock,
            NoStock,
            NoRoom,
            CantAfford,
            NothingToSell,

            /// <summary>The port's faction could deal with the ship's, but the law bars the ship from docking (see <see cref="Law.CanDock"/>).</summary>
            Refused,

            /// <summary>All there is of that good aboard is stolen, and a lawful market will not take it.</summary>
            Contraband,
        }

        /// <summary>
        /// Buys up to <paramref name="crates"/> standard crates of <paramref name="good"/> (see
        /// <see cref="CargoFootprint"/>) at <paramref name="s"/> for the player's piloted <paramref name="ship"/>:
        /// walks the price up and drains stock exactly as <see cref="Load"/> does for an AI trader's purchase,
        /// paying from the ship's own faction treasury, and places each crate into the ship's personal
        /// <see cref="CargoHold"/> -- never the single-slot cargo fields above, which belong to the AI trade
        /// loop and a player-flown ship does not use. Stops at the first crate that cannot be bought; reports
        /// how many actually went through in <paramref name="bought"/>.
        /// </summary>
        public TradeOutcome BuyIntoHold(Ship ship, Settlement s, Good good, int crates, out int bought)
            => BuyIntoHold(ship, s, good, crates, out bought, out _);

        /// <summary>
        /// As <see cref="BuyIntoHold(Ship, Settlement, Good, int, out int)"/>, and also reports why the run of crates
        /// ended in <paramref name="stoppedBy"/>: <see cref="TradeOutcome.Ok"/> when all asked for were bought, else the
        /// limit hit (<see cref="TradeOutcome.NoStock"/>, <see cref="TradeOutcome.CantAfford"/>, <see cref="TradeOutcome.NoRoom"/>),
        /// even when some crates went through first.
        /// </summary>
        public TradeOutcome BuyIntoHold(Ship ship, Settlement s, Good good, int crates, out int bought, out TradeOutcome stoppedBy)
        {
            bought = 0;
            stoppedBy = TradeOutcome.Ok;
            if (!CanTrade(ship, s)) return stoppedBy = LawRefuses(ship, s) ? TradeOutcome.Refused : TradeOutcome.CannotDock;
            double unitTonnes = CargoFootprint.Of(good).Tonnes;
            double factor = PriceFactorOf(ship, s);
            var stopped = TradeOutcome.Ok;
            for (int i = 0; i < crates; i++)
            {
                if (s.Available(good) < unitTonnes) { stopped = TradeOutcome.NoStock; break; }
                double cost = s.PurchaseCost(good, unitTonnes) * factor;
                double total = cost * (1 + PortTax);
                if (ship.Faction.Treasury < total) { stopped = TradeOutcome.CantAfford; break; }
                var crate = ship.PersonalHold.AddFirstFit(good);
                if (crate == null) { stopped = TradeOutcome.NoRoom; break; }
                // What a haven sells is plunder: stolen goods, contraband to any lawful patrol.
                crate.Stolen = s.Owner != null && s.Owner.IsPirate;

                s.Stock[(int)good] = Math.Max(0, s.Stock[(int)good] - unitTonnes);
                s.Owner.Treasury += cost * PortTax;
                s.TradeVolumeCredits += cost;
                ship.Faction.Treasury -= total;
                bought++;
            }
            // Prices follow the stock that changed, however the run of crates ended.
            if (bought > 0) s.RefreshPrices();
            stoppedBy = stopped;
            return bought > 0 ? TradeOutcome.Ok : stopped;
        }

        /// <summary>
        /// Sells up to <paramref name="crates"/> crates of <paramref name="good"/> out of the player's
        /// piloted <paramref name="ship"/>'s own <see cref="CargoHold"/> at <paramref name="s"/>: walks the
        /// price down and credits treasuries exactly as <see cref="Sell"/> does for an AI trader's cargo.
        /// Stops at the first crate of that good no longer in the hold; reports how many actually sold in
        /// <paramref name="sold"/>.
        /// </summary>
        public TradeOutcome SellFromHold(Ship ship, Settlement s, Good good, int crates, out int sold)
        {
            sold = 0;
            if (!CanTrade(ship, s)) return LawRefuses(ship, s) ? TradeOutcome.Refused : TradeOutcome.CannotDock;
            var hold = ship.PersonalHold;
            // A lawful market will not touch stolen goods; a haven takes anything, and a lawless station's black market
            // takes stolen goods at its own prices (U.SPC-T94).
            bool haven = s.Owner != null && s.Owner.IsPirate;
            bool fence = haven || BlackMarket.Takes(s);
            double factor = PriceFactorOf(ship, s);
            var stopped = TradeOutcome.Ok;
            for (int i = 0; i < crates; i++)
            {
                CargoItem item = null;
                bool stolenOnly = false;
                foreach (var it in hold.Items)
                {
                    if (it.Good != good) continue;
                    if (it.Stolen && !fence) { stolenOnly = true; continue; }
                    item = it;
                    break;
                }
                if (item == null) { stopped = stolenOnly ? TradeOutcome.Contraband : TradeOutcome.NothingToSell; break; }

                double tonnes = item.Tonnes;
                // Stolen goods sold at a lawless station go to its black market, not its open board.
                bool black = item.Stolen && !haven;
                double value = (black ? BlackMarket.SaleValue(s, good, tonnes) : s.SaleValue(good, tonnes)) / factor;
                // A black sale is paid out of the station owner's treasury (as BlackMarket.Sell does) and is untaxed.
                double tax = black ? 0 : value * PortTax;
                hold.Remove(item);
                if (black) s.BlackStock[(int)good] += tonnes;
                else s.Stock[(int)good] += tonnes;
                if (black) s.Owner.Treasury -= value;
                else s.Owner.Treasury += tax;
                s.TradeVolumeCredits += value;
                ship.Faction.Treasury += value - tax;
                sold++;
            }
            // Prices follow the stock that changed, however the run of crates ended.
            if (sold > 0) s.RefreshPrices();
            return sold > 0 ? TradeOutcome.Ok : stopped;
        }

        // ---------------------------------------------------------------- queries

        /// <summary>Systemwide average price relative to base for a good (1 = normal).</summary>
        public double PriceIndex(Good g)
        {
            double sum = 0;
            int n = 0;
            foreach (var s in Settlements)
            {
                if (s.Demand[(int)g] <= 0) continue;
                sum += s.Price[(int)g] / GoodInfo.Of(g).BasePrice;
                n++;
            }
            return n > 0 ? sum / n : 1;
        }

        /// <summary>Captures a settlement: the owner changes and production is disrupted for a while.</summary>
        public void Capture(Settlement s, Faction newOwner)
        {
            s.Owner = newOwner;
            s.DaysSinceCapture = 0;
            s.BuildPoints *= 0.3;
        }
    }
}
