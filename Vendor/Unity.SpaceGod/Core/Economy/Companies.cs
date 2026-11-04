using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>What became of a trading company.</summary>
    public enum CompanyFate
    {
        /// <summary>Trading.</summary>
        Trading,

        /// <summary>Bought out by a rival, which took its hulls, its routes and its debts.</summary>
        Merged,

        /// <summary>Ran out of money: its hulls were sold off or broken up and it was wound up.</summary>
        Bankrupt,
    }

    /// <summary>
    /// A run a company's ships have worked: loaded at one port, sold at another, and what it earned. The
    /// company learns which runs pay from these and points its fleet at the best of them.
    /// </summary>
    public sealed class TradeRoute
    {
        public Settlement From, To;

        /// <summary>Runs completed, and their profit and days in all (smoothed toward the latest, see <see cref="Companies.RouteMemory"/>).</summary>
        public int Runs;
        public double Profit, Days;
        public double LastJd;

        /// <summary>Profit per day under way on this run, credits.</summary>
        public double PerDay => Days > 0 ? Profit / Days : 0;

        /// <summary>Whether the run joins two planetary systems by hyperlane (Earth to Ceres) rather than staying in one.</summary>
        public bool CrossSystem;

        public override string ToString() => From.Name + " - " + To.Name;
    }

    /// <summary>
    /// An NPC trading corporation (U.SPC-T99): it owns a fleet of freighters, ore haulers and couriers, takes their
    /// trade profit, pays their crews and upkeep and a tax to the flag it sails under, buys new hulls at yards when
    /// the fleet earns enough to pay them back, learns which runs pay and steers its ships to them, and goes under
    /// (or is bought out) when it cannot pay its way. Its ships sail under its flag faction's colours for docking
    /// and the law; the company is who profits.
    /// </summary>
    public sealed class Company
    {
        public string Name;

        /// <summary>A short house mark ("KFL") for lists.</summary>
        public string Tag;

        /// <summary>The colours its hulls are painted in.</summary>
        public Rgb Livery;

        /// <summary>Where it is headquartered: it prefers runs from and to here and buys its hulls here when it can.</summary>
        public Settlement HomePort;

        /// <summary>The faction it is registered with and sails under (its hulls' <see cref="Ship.Faction"/>).</summary>
        public Faction Flag;

        /// <summary>Cash in hand, credits (negative: in debt to its bankers).</summary>
        public double Cash;

        /// <summary>The hulls it owns now (anywhere, abroad too).</summary>
        public readonly List<Ship> Ships = new List<Ship>();

        /// <summary>How each faction regards it, -1 (barred) to 1 (favoured). It trades at a faction's ports only above
        /// <see cref="Companies.ShunnedStanding"/> and buys hulls at its yards only from <see cref="Companies.YardStanding"/>.</summary>
        public readonly Dictionary<Faction, double> Standing = new Dictionary<Faction, double>();

        /// <summary>The runs it knows, and the ones it is steering its fleet to.</summary>
        public readonly List<TradeRoute> Routes = new List<TradeRoute>();
        public readonly List<TradeRoute> Chosen = new List<TradeRoute>();

        public double FoundedJd;
        public CompanyFate Fate = CompanyFate.Trading;
        public double FateJd;

        /// <summary>The company that bought it out, if it merged.</summary>
        public Company MergedInto;

        /// <summary>Net income a day (trade profit less running costs and tax), smoothed over a couple of months.</summary>
        public double IncomePerDay;

        /// <summary>Trade profit a hull earns a day, by hull class, smoothed (NaN until a hull of that class has worked).</summary>
        public readonly double[] ClassPerDay = { double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN };

        /// <summary>Lifetime trade profit, running costs, tax and hull purchases, credits.</summary>
        public double Earned, RunningCosts, TaxPaid, HullsSpent;
        public int HullsBought, HullsLost, HullsSold, HullsScrapped;

        /// <summary>A hull on order: its class, the yard building it and when it was ordered. The price is held from
        /// <see cref="Cash"/> until the yard lays it down.</summary>
        public ShipClass? OnOrder;
        public Settlement OrderYard;
        public double OrderJd, OrderPrice;

        /// <summary>When it last went into debt, or NaN while solvent.</summary>
        public double InDebtSinceJd = double.NaN;

        public bool Trading => Fate == CompanyFate.Trading;

        public int Hulls => Ships.Count;

        /// <summary>Its standing with <paramref name="f"/> (0 for a faction it has no dealings with).</summary>
        public double StandingWith(Faction f) => f != null && Standing.TryGetValue(f, out double v) ? v : 0;

        internal void Adjust(Faction f, double by)
        {
            if (f == null) return;
            Standing[f] = Math.Max(-1, Math.Min(1, StandingWith(f) + by));
        }

        /// <summary>What it is worth: cash and its hulls at second-hand prices.</summary>
        public double Value
        {
            get
            {
                double v = Cash + (OnOrder != null ? OrderPrice : 0);
                foreach (var s in Ships) v += Companies.ResaleShare * Economy.HullPrice(s.Spec.Class);
                return v;
            }
        }

        /// <summary>The known run from <paramref name="from"/> to <paramref name="to"/>, or null.</summary>
        public TradeRoute RouteOf(Settlement from, Settlement to)
        {
            foreach (var r in Routes)
                if (r.From == from && r.To == to) return r;
            return null;
        }

        /// <summary>
        /// Trade profit a year, after running costs and tax, one more hull of <paramref name="cls"/> could be expected to
        /// earn: from what its own hulls of that class make, else what the trade makes (<paramref name="marketPerDay"/>,
        /// what every company's do), else a guess from its freighters scaled by hold size. 0 with nothing to go on.
        /// </summary>
        public double ExpectedYearly(ShipClass cls, double marketPerDay = double.NaN)
        {
            double rate = ClassPerDay[(int)cls];
            if (double.IsNaN(rate)) rate = marketPerDay;
            if (double.IsNaN(rate))
            {
                double f = ClassPerDay[(int)ShipClass.Freighter];
                if (double.IsNaN(f)) return 0;
                rate = f * Economy.CapacityOf(cls) / Economy.CapacityOf(ShipClass.Freighter) * 0.5;
            }
            return (rate - Economy.RunningCostPerDay(cls)) * 365.25 * (1 - Companies.CorporateTax);
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// The trading companies of one system (U.SPC-T99) and the market in hulls between them: every ten days each
    /// company books its ships' trade, pays its running costs and tax, adjusts its standing with the factions it deals
    /// with, re-picks the runs it steers to, orders a hull if its fleet is earning enough to pay one back, and if it is
    /// in trouble sells hulls, is bought out by a richer rival flying friendly colours, or goes bankrupt. New companies
    /// are founded at busy ports when the trade pays. So the merchant fleet's size and where it trades come out of
    /// what trade earns, rather than a fixed number of spawns.
    /// </summary>
    public sealed class Companies
    {
        readonly World world;
        readonly Random rng;

        public readonly List<Company> All = new List<Company>();

        /// <summary>The most companies trading at once, and how often a new one may be founded (days).</summary>
        public int MaxCompanies = 8;
        public double FoundingDays = 60;

        public const double ReviewDays = 10;

        /// <summary>How fast a company's sense of its income follows the latest (days).</summary>
        public const double SmoothingDays = 60;

        /// <summary>How much the latest run counts in a route's record against the ones before.</summary>
        public const double RouteMemory = 0.35;

        /// <summary>Share of positive profit paid to the flag as tax.</summary>
        public const double CorporateTax = 0.1;

        /// <summary>A hull is bought only if one more is expected to pay its price back within this many years ...</summary>
        public const double PaybackYears = 5;

        /// <summary>... and the company keeps this share of the price in hand after paying it.</summary>
        public const double ReserveShare = 0.25;

        /// <summary>What a second-hand hull fetches (to a rival), and what a breaker pays for one, as shares of its price.</summary>
        public const double ResaleShare = 0.55, ScrapShare = 0.15;

        /// <summary>Debt beyond which a company sells hulls, and how long it may stay in debt before it is wound up (days).</summary>
        public const double DebtLimit = 4e6;
        public const double BankruptDays = 180;

        /// <summary>Below this standing a company will not trade at a faction's ports; from this one it may buy at its yards.</summary>
        public const double ShunnedStanding = -0.5, YardStanding = -0.1;

        /// <summary>How much more a run the company has chosen scores in a captain's planning, and its home port.</summary>
        public const double ChosenBonus = 0.3, HomeBonus = 0.1;

        /// <summary>How many runs a company steers its fleet to.</summary>
        public const int ChosenRoutes = 6;

        /// <summary>An order a yard has not laid down in this many days is cancelled and placed again.</summary>
        public const double OrderPatienceDays = 120;

        /// <summary>Capital the investors put into a new company, credits.</summary>
        public double FoundingCapital = 2.2 * Economy.HullPrice(ShipClass.Freighter);

        double nextReviewJd = double.NaN, lastReviewJd = double.NaN, nextFoundingJd = double.NaN;
        int foundedCount, hullsNamed;

        public Companies(World world, int seed)
        {
            this.world = world;
            rng = new Random(seed);
        }

        public IEnumerable<Company> Live
        {
            get
            {
                foreach (var c in All)
                    if (c.Trading) yield return c;
            }
        }

        public int LiveCount
        {
            get
            {
                int n = 0;
                foreach (var c in All)
                    if (c.Trading) n++;
                return n;
            }
        }

        /// <summary>Whether this system's merchant shipping is company-run (else the yards' owners build it, as before).</summary>
        public bool Enabled => All.Count > 0;

        // ---------------------------------------------------------------- founding

        /// <summary>Founds a company headquartered at <paramref name="home"/>, registered with <paramref name="flag"/>, with <paramref name="cash"/> in hand.</summary>
        public Company Found(string name, string tag, Rgb livery, Settlement home, Faction flag, double cash, double jd)
        {
            var c = new Company { Name = name, Tag = tag, Livery = livery, HomePort = home, Flag = flag, Cash = cash, FoundedJd = jd };
            foreach (var f in world.Fleet.Factions)
            {
                double s = f == flag ? 0.5 : f.IsPirate ? -1 : world.Diplomacy.Get(flag, f) == Relation.War ? -0.6 : 0.1;
                c.Standing[f] = s;
            }
            All.Add(c);
            foundedCount++;
            if (double.IsNaN(nextReviewJd)) nextReviewJd = jd + ReviewDays;
            if (double.IsNaN(lastReviewJd)) lastReviewJd = jd;
            if (double.IsNaN(nextFoundingJd)) nextFoundingJd = jd + FoundingDays;
            return c;
        }

        /// <summary>Puts <paramref name="ship"/> in <paramref name="c"/>'s fleet, under its flag; its trade from here on is the company's.</summary>
        public void Enlist(Company c, Ship ship)
        {
            if (ship.Company != null && ship.Company != c)
            {
                SettleOut(ship.Company, ship);
                ship.Company.Ships.Remove(ship);
            }
            ship.Company = c;
            // A load aboard was bought by the flag it flew: the new flag, which will take the whole of its sale, pays for it.
            if (ship.HasCargo && ship.Faction != null && ship.Faction != c.Flag && ship.CargoCost > 0)
            {
                ship.Faction.Treasury += ship.CargoCost;
                c.Flag.Treasury -= ship.CargoCost;
            }
            ship.Faction = c.Flag;
            // A hull taken over abroad (its company merged into another) carries on with its run under the new flag, rather
            // than flying on with a voyage still in the old one's name.
            if (ship.Voyage != null && ship.Voyage.Faction != null) ship.Voyage.Faction = c.Flag;
            ship.CompanyBookedCredits = ship.Credits;
            ship.LoadedFrom = null;
            if (!c.Ships.Contains(ship)) c.Ships.Add(ship);
        }

        /// <summary>
        /// A hull leaving <paramref name="c"/>'s fleet between reviews (sold, merged, taken into the player's fleet): what it
        /// has taken since the last review is booked to the company now, as <see cref="Book"/> would, before it goes.
        /// </summary>
        public void SettleOut(Company c, Ship ship)
        {
            if (c == null || ship == null) return;
            double delta = ship.Credits - ship.CompanyBookedCredits;
            ship.CompanyBookedCredits = ship.Credits;
            if (delta == 0) return;
            double tax = Math.Max(0, delta) * CorporateTax;
            c.Flag.Treasury += tax - delta;
            c.Cash += delta - tax;
            c.Earned += delta;
            c.TaxPaid += tax;
        }

        /// <summary>A company hull came into this system (through a gate, as a copy): it is still its company's.</summary>
        public void OnShipAdded(Ship ship)
        {
            var c = ship.Company;
            if (c == null || c.Ships.Contains(ship)) return;
            if (!c.Trading)
            {
                // Bought out while this hull was inside a gate (in no system's fleet, so Merge never saw it): it carries
                // on under the buyer, as every sister hull did, and its takings follow the old books over as Merge's do.
                var buyer = c.Fate == CompanyFate.Merged ? c.MergedInto : null;
                while (buyer != null && buyer.Fate == CompanyFate.Merged && buyer.MergedInto != null) buyer = buyer.MergedInto;
                if (buyer != null && buyer.Trading)
                {
                    Enlist(buyer, ship);
                    buyer.Cash += c.Cash;
                    c.Cash = 0;
                    return;
                }
                ship.Company = null;
                return;
            }
            c.Ships.Add(ship);
        }

        // ---------------------------------------------------------------- what captains are told

        /// <summary>
        /// How a company's captain weighs a run from <paramref name="from"/> (null: whatever is in the hold already) to
        /// <paramref name="to"/>: 0 for a port of a faction that shuns the company, more for one of the company's chosen
        /// runs or its home port, less for a run it has lost money on. 1 for a ship no company owns.
        /// </summary>
        public static double RouteFactor(Ship ship, Settlement from, Settlement to)
        {
            var c = ship.Company;
            if (c == null || to == null) return 1;
            if (c.StandingWith(to.Owner) < ShunnedStanding) return 0;
            if (from != null && c.StandingWith(from.Owner) < ShunnedStanding) return 0;
            double f = 1;
            if (to == c.HomePort || from == c.HomePort) f += HomeBonus;
            if (from != null)
            {
                foreach (var r in c.Chosen)
                    if (r.From == from && r.To == to) { f += ChosenBonus; break; }
                var known = c.RouteOf(from, to);
                if (known != null && known.Runs >= 2 && known.Profit < 0) f *= 0.5;
            }
            return f;
        }

        /// <summary>A company ship loaded a cargo at <paramref name="from"/>: what the run earns is reckoned from here.</summary>
        public void OnLoaded(Ship ship, Settlement from)
        {
            if (ship.Company == null) return;
            ship.LoadedFrom = from;
            ship.LoadedJd = world.Economy.Jd;
            ship.LoadedCredits = ship.Credits;
        }

        /// <summary>A company ship sold its cargo at <paramref name="to"/>: the run goes in the company's book of routes,
        /// and the port's owner thinks a little better of the company.</summary>
        public void OnSold(Ship ship, Settlement to, Good good, double tonnes)
        {
            var c = ship.Company;
            if (c == null) return;
            c.Adjust(to.Owner, GoodInfo.Of(good).Category == GoodCategory.Essential ? 0.012 : 0.006);
            var from = ship.LoadedFrom;
            ship.LoadedFrom = null;
            if (from == null || from == to) return;
            double jd = world.Economy.Jd;
            Record(c, from, to, ship.Credits - ship.LoadedCredits, Math.Max(0.25, jd - ship.LoadedJd), jd);
        }

        /// <summary>Puts a completed run in <paramref name="c"/>'s book of routes.</summary>
        public void Record(Company c, Settlement from, Settlement to, double profit, double days, double jd)
        {
            var r = c.RouteOf(from, to);
            if (r == null)
            {
                var lanes = world.Fleet.Lanes;
                var here = world.Economy.Settlements;
                // Bought in another star system (an interstellar run), or in another planetary system of this one.
                bool cross = !here.Contains(from) || !here.Contains(to) || (lanes != null && lanes.SystemOf(from.Body) != lanes.SystemOf(to.Body));
                r = new TradeRoute { From = from, To = to, CrossSystem = cross };
                c.Routes.Add(r);
                r.Profit = profit;
                r.Days = days;
            }
            else
            {
                r.Profit += (profit - r.Profit) * RouteMemory;
                r.Days += (days - r.Days) * RouteMemory;
            }
            r.Runs++;
            r.LastJd = jd;
        }

        // ---------------------------------------------------------------- the review

        public void Update(double jd)
        {
            if (!Enabled || double.IsNaN(nextReviewJd) || jd < nextReviewJd) return;
            nextReviewJd = jd + ReviewDays;
            Review(jd);
            if (jd >= nextFoundingJd)
            {
                nextFoundingJd = jd + FoundingDays;
                TryFound(jd);
            }
        }

        /// <summary>One review of every trading company, now (<see cref="Update"/> holds one every <see cref="ReviewDays"/>).</summary>
        public void Review(double jd)
        {
            double days = double.IsNaN(lastReviewJd) ? 0 : Math.Max(0, jd - lastReviewJd);
            lastReviewJd = jd;
            // A list copy: mergers and bankruptcies change who trades.
            var trading = new List<Company>(Live);
            foreach (var c in trading) Book(c, days, jd);
            foreach (var c in trading)
            {
                if (!c.Trading) continue;
                Standings(c);
                ChooseRoutes(c, jd);
                if (Trouble(c, jd)) continue;
                Expand(c, jd);
            }
        }

        /// <summary>
        /// Takes in what <paramref name="c"/>'s ships earned since the last review (the change in their running
        /// profit, which their flag's treasury booked for them and now hands over), pays their crews and upkeep and
        /// the flag's tax, and drops hulls it no longer has.
        /// </summary>
        void Book(Company c, double days, double jd)
        {
            double trade = 0, running = 0;
            var classSum = new double[c.ClassPerDay.Length];
            var classCount = new int[c.ClassPerDay.Length];
            for (int i = c.Ships.Count - 1; i >= 0; i--)
            {
                var s = c.Ships[i];
                if (s.GateDeparted)
                {
                    // Gone on through a gate: its copy over there is still ours, carries the same unbooked takings
                    // and books them (and its own trade) itself.
                    c.Ships.RemoveAt(i);
                    continue;
                }
                double delta = s.Credits - s.CompanyBookedCredits;
                s.CompanyBookedCredits = s.Credits;
                trade += delta;
                // Its takings went to the flag it flew when it earned them: the company's, even if it has since been taken.
                c.Flag.Treasury -= delta;
                if (s.Destroyed || s.Derelict || s.Faction != c.Flag)
                {
                    c.Ships.RemoveAt(i);
                    c.HullsLost++;
                    if (s.Company == c) s.Company = null;
                    world.Log(jd, $"{c.Name} writes off the {s.Spec.DisplayName.ToLowerInvariant()} {s.Name}", WorldEventKind.Economy, s.Orbit?.Body ?? -1);
                    continue;
                }
                running += Economy.RunningCostPerDay(s.Spec.Class) * days;
                int k = (int)s.Spec.Class;
                classSum[k] += delta;
                classCount[k]++;
            }
            double tax = Math.Max(0, trade) * CorporateTax;
            c.Flag.Treasury += tax;
            c.Cash += trade - running - tax;
            c.Earned += trade;
            c.RunningCosts += running;
            c.TaxPaid += tax;
            if (days <= 0) return;
            double k2 = 1 - Math.Exp(-days / SmoothingDays);
            c.IncomePerDay += ((trade - running - tax) / days - c.IncomePerDay) * k2;
            for (int i = 0; i < classSum.Length; i++)
            {
                if (classCount[i] == 0) continue;
                double rate = classSum[i] / classCount[i] / days;
                c.ClassPerDay[i] = double.IsNaN(c.ClassPerDay[i]) ? rate : c.ClassPerDay[i] + (rate - c.ClassPerDay[i]) * k2;
            }
        }

        /// <summary>Standing drifts: down toward hostility with whoever is at war with the company's flag, back toward
        /// neutral with everyone else, and the flag always thinks well of its own.</summary>
        void Standings(Company c)
        {
            foreach (var f in world.Fleet.Factions)
            {
                if (f == c.Flag) { c.Standing[f] = Math.Max(c.StandingWith(f), 0.5); continue; }
                if (f.IsPirate) { c.Standing[f] = -1; continue; }
                double s = c.StandingWith(f);
                double toward = world.Diplomacy.Get(c.Flag, f) == Relation.War ? -0.7 : 0;
                if (s < toward || toward < 0) c.Standing[f] = s + (toward - s) * 0.05;
                else c.Standing[f] = s - Math.Min(0.002, s - toward);
            }
        }

        /// <summary>Picks the runs <paramref name="c"/> steers its fleet to: the best paying a day it knows, worked
        /// recently, at ports that still deal with it.</summary>
        void ChooseRoutes(Company c, double jd)
        {
            c.Routes.RemoveAll(r => jd - r.LastJd > 365);
            c.Chosen.Clear();
            var candidates = new List<TradeRoute>();
            foreach (var r in c.Routes)
                if (r.Profit > 0 && jd - r.LastJd < 120 && c.StandingWith(r.From.Owner) >= ShunnedStanding && c.StandingWith(r.To.Owner) >= ShunnedStanding)
                    candidates.Add(r);
            candidates.Sort((a, b) => b.PerDay.CompareTo(a.PerDay));
            for (int i = 0; i < candidates.Count && i < ChosenRoutes; i++) c.Chosen.Add(candidates[i]);
        }

        /// <summary>A company in debt sells a hull to pay it down, is bought out, or after long enough is wound up. True if it is in trouble.</summary>
        bool Trouble(Company c, double jd)
        {
            if (c.Cash >= 0)
            {
                c.InDebtSinceJd = double.NaN;
                return false;
            }
            if (double.IsNaN(c.InDebtSinceJd)) c.InDebtSinceJd = jd;
            CancelOrder(c);
            if (c.Cash >= 0) { c.InDebtSinceJd = double.NaN; return false; }
            if (TryMerge(c, jd)) return true;
            bool longInDebt = jd - c.InDebtSinceJd >= BankruptDays;
            if (c.Ships.Count == 0 || longInDebt)
            {
                Bankrupt(c, jd);
                return true;
            }
            if (c.Cash < -DebtLimit) SellHull(c, jd);
            return true;
        }

        // ---------------------------------------------------------------- growth

        /// <summary>
        /// If <paramref name="c"/>'s fleet earns enough that one more hull pays its price back in
        /// <see cref="PaybackYears"/> and it has the price and a reserve in hand, it orders one at the nearest yard
        /// that will build for it (its home port's first). An order the yard has sat on too long is placed again.
        /// </summary>
        void Expand(Company c, double jd)
        {
            if (c.OnOrder != null)
            {
                if (jd - c.OrderJd < OrderPatienceDays && YardWillBuild(c, c.OrderYard)) return;
                CancelOrder(c);
            }
            if (world.TraderCount() + Ordered() >= world.MerchantCap) return;
            ShipClass? best = null;
            double bestMargin = 0;
            foreach (var cls in new[] { ShipClass.Freighter, ShipClass.Miner })
            {
                double price = Economy.HullPrice(cls);
                double yearly = c.ExpectedYearly(cls, MarketPerDay(cls));
                double margin = yearly * PaybackYears / price;
                if (margin >= 1 && margin > bestMargin && c.Cash >= price * (1 + ReserveShare))
                {
                    bestMargin = margin;
                    best = cls;
                }
            }
            if (best == null) return;
            var yard = YardFor(c);
            if (yard == null) return;
            c.OnOrder = best;
            c.OrderYard = yard;
            c.OrderJd = jd;
            c.OrderPrice = Economy.HullPrice(best.Value) * (1 - 0.1 * Math.Max(0, c.StandingWith(yard.Owner)));
            c.Cash -= c.OrderPrice;
        }

        /// <summary>What a hull of <paramref name="cls"/> earns a day across the companies that run them (hull-weighted), or NaN if none do.</summary>
        public double MarketPerDay(ShipClass cls)
        {
            double sum = 0;
            int n = 0;
            foreach (var c in All)
            {
                double r = c.ClassPerDay[(int)cls];
                if (!c.Trading || double.IsNaN(r)) continue;
                int hulls = 0;
                foreach (var s in c.Ships)
                    if (s.Spec.Class == cls) hulls++;
                sum += r * hulls;
                n += hulls;
            }
            return n > 0 ? sum / n : double.NaN;
        }

        int Ordered()
        {
            int n = 0;
            foreach (var c in All)
                if (c.Trading && c.OnOrder != null) n++;
            return n;
        }

        bool YardWillBuild(Company c, Settlement yard) =>
            yard != null && !yard.Fallen && yard.HasShipyard && yard.Owner != null && !yard.Owner.IsPirate &&
            world.Diplomacy.CanDockAt(c.Flag, yard) && c.StandingWith(yard.Owner) >= YardStanding;

        /// <summary>Where <paramref name="c"/> orders its hulls: its home port if it has a yard that will build for it, else the
        /// yard in the same planetary system, else the one whose owner thinks best of it (fewest build points owed first).</summary>
        public Settlement YardFor(Company c)
        {
            if (YardWillBuild(c, c.HomePort)) return c.HomePort;
            var lanes = world.Fleet.Lanes;
            Settlement best = null;
            double bestScore = double.MinValue;
            foreach (var s in world.Economy.Settlements)
            {
                if (!YardWillBuild(c, s)) continue;
                double score = c.StandingWith(s.Owner) + s.BuildPoints / 1e6;
                if (lanes != null && c.HomePort != null && lanes.SystemOf(s.Body) == lanes.SystemOf(c.HomePort.Body)) score += 1;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            return best;
        }

        void CancelOrder(Company c)
        {
            if (c.OnOrder == null) return;
            c.Cash += c.OrderPrice;
            c.OnOrder = null;
            c.OrderYard = null;
            c.OrderPrice = 0;
        }

        /// <summary>The hull a yard should lay down for a company next (the oldest order placed with it), or null.</summary>
        public ShipClass? OrderAt(Settlement yard)
        {
            Company first = FirstOrderAt(yard, null);
            return first?.OnOrder;
        }

        Company FirstOrderAt(Settlement yard, ShipClass? cls)
        {
            Company first = null;
            foreach (var c in All)
                if (c.Trading && c.OnOrder != null && c.OrderYard == yard && (cls == null || c.OnOrder == cls) && (first == null || c.OrderJd < first.OrderJd))
                    first = c;
            return first;
        }

        /// <summary>Whether a company has already paid for the <paramref name="cls"/> hull <paramref name="yard"/> is about to lay down.</summary>
        public bool PaidFor(Settlement yard, ShipClass cls, double price) => FirstOrderAt(yard, cls) != null;

        /// <summary>The yard has finished a hull of <paramref name="cls"/>: the company whose order it was pays the yard's
        /// owner and takes delivery. Returns that company, or null if nobody ordered it.</summary>
        public Company TakeDelivery(Settlement yard, ShipClass cls, double jd)
        {
            var c = FirstOrderAt(yard, cls);
            if (c == null) return null;
            yard.Owner.Treasury += c.OrderPrice;
            c.HullsSpent += c.OrderPrice;
            c.HullsBought++;
            c.OnOrder = null;
            c.OrderYard = null;
            c.OrderPrice = 0;
            return c;
        }

        /// <summary>A name for a new company hull ("Kepler Endeavour"), from the company's own name and a list of ship names.</summary>
        public string HullName(Company c, string[] names)
        {
            string word = c.Name.Split(' ')[0];
            int round = hullsNamed / names.Length;
            string name = word + " " + names[hullsNamed % names.Length] + (round > 0 ? " " + (round + 1).ToString(CultureInfo.InvariantCulture) : "");
            hullsNamed++;
            return name;
        }

        // ---------------------------------------------------------------- trouble

        /// <summary>The hull a company in trouble lets go of first: the one earning least, of those in port and free to go.</summary>
        Ship Weakest(Company c)
        {
            Ship worst = null;
            double worstRate = double.MaxValue;
            foreach (var s in c.Ships)
            {
                if (!Saleable(s)) continue;
                double rate = c.ClassPerDay[(int)s.Spec.Class];
                if (double.IsNaN(rate)) rate = 0;
                rate -= Economy.RunningCostPerDay(s.Spec.Class);
                if (rate < worstRate)
                {
                    worstRate = rate;
                    worst = s;
                }
            }
            return worst;
        }

        bool Saleable(Ship s) =>
            !s.Destroyed && s.Battle == null && s != world.PilotedShip && s.Voyage == null && !s.IsEscort &&
            (s.Phase == ShipPhase.Parked || s.Phase == ShipPhase.Landed);

        /// <summary>Sells <paramref name="c"/>'s weakest hull: to the richest rival under friendly colours that can use it, else to the breakers.</summary>
        void SellHull(Company c, double jd)
        {
            var ship = Weakest(c);
            if (ship == null) return;
            double price = ResaleShare * Economy.HullPrice(ship.Spec.Class);
            Company buyer = null;
            foreach (var o in Live)
            {
                if (o == c || !Friendly(o, c) || o.Cash < price * (1 + ReserveShare) || o.ExpectedYearly(ship.Spec.Class, MarketPerDay(ship.Spec.Class)) * PaybackYears < price) continue;
                if (buyer == null || o.Cash > buyer.Cash) buyer = o;
            }
            if (buyer != null)
            {
                buyer.Cash -= price;
                c.Cash += price;
                c.Ships.Remove(ship);
                Enlist(buyer, ship);
                c.HullsSold++;
                world.Log(jd, $"{c.Name} sells the {ship.Spec.DisplayName.ToLowerInvariant()} {ship.Name} to {buyer.Name} to pay its debts", WorldEventKind.Economy, ship.Orbit?.Body ?? -1);
                return;
            }
            Scrap(c, ship, jd);
        }

        void Scrap(Company c, Ship ship, double jd)
        {
            // Its takings since the last review are its own too, same as a hull sold or merged away.
            SettleOut(c, ship);
            c.Cash += ScrapShare * Economy.HullPrice(ship.Spec.Class);
            c.Ships.Remove(ship);
            c.HullsScrapped++;
            world.Economy.Release(ship);
            ship.Company = null;
            // Its escorts (a hired fighter wing, U.SPC-T275) do not follow it to the breakers: they carry on under the strongest.
            world.War?.HandOverCommand(ship);
            ship.Scrapped = true;
            // Gone from the fleet without a wreck: the hull goes to the breakers' yard.
            ship.Destroyed = true;
            ship.GateDeparted = true;
            world.Log(jd, $"{c.Name} sends the {ship.Spec.DisplayName.ToLowerInvariant()} {ship.Name} to the breakers", WorldEventKind.Economy, ship.Orbit?.Body ?? -1);
        }

        bool Friendly(Company a, Company b) => a.Flag == b.Flag || (!a.Flag.IsPirate && !b.Flag.IsPirate && world.Diplomacy.Get(a.Flag, b.Flag) == Relation.Peace);

        /// <summary>
        /// A company in debt is bought out by the richest healthy rival under friendly colours that can pay what it is
        /// worth (its hulls at second-hand prices, less its debt) and keep a reserve: the rival takes its hulls, its
        /// routes and its debt, and the shareholders are paid off. True if it merged.
        /// </summary>
        public bool TryMerge(Company target, double jd)
        {
            if (!target.Trading) return false;
            double price = Math.Max(0, target.Value);
            Company buyer = null;
            foreach (var o in Live)
            {
                if (o == target || !Friendly(o, target) || o.IncomePerDay <= 0) continue;
                if (o.Cash + Math.Min(0, target.Cash) < price + ReserveShare * Economy.HullPrice(ShipClass.Freighter)) continue;
                if (buyer == null || o.Cash > buyer.Cash) buyer = o;
            }
            if (buyer == null) return false;
            Merge(buyer, target, price, jd);
            return true;
        }

        /// <summary><paramref name="buyer"/> takes over <paramref name="target"/>: its hulls (repainted, under the buyer's flag), its
        /// routes, its cash or debt, for <paramref name="price"/> paid to its shareholders.</summary>
        public void Merge(Company buyer, Company target, double price, double jd)
        {
            CancelOrder(target);
            // Its hulls' takings since the last review are its own, and go over with its cash.
            foreach (var s in new List<Ship>(target.Ships)) Enlist(buyer, s);
            target.Ships.Clear();
            buyer.Cash += target.Cash - price;
            target.Flag.Treasury += price;
            target.Cash = 0;
            foreach (var r in target.Routes)
                if (buyer.RouteOf(r.From, r.To) == null) buyer.Routes.Add(r);
            target.Fate = CompanyFate.Merged;
            target.FateJd = jd;
            target.MergedInto = buyer;
            world.Log(jd, $"{buyer.Name} buys out {target.Name}" + (price > 0 ? $" for {Credits(price)}" : " for its debts"), WorldEventKind.Economy, target.HomePort?.Body ?? -1);
        }

        /// <summary>Winds <paramref name="c"/> up: rivals buy what hulls they can use, the breakers take the rest.</summary>
        public void Bankrupt(Company c, double jd)
        {
            CancelOrder(c);
            // Rivals buy what hulls they can use, the breakers take the rest of those in port ...
            for (var s = Weakest(c); s != null; s = Weakest(c)) SellHull(c, jd);
            // ... and one out on a run goes to the flag, whose merchant service carries on with it.
            foreach (var s in new List<Ship>(c.Ships))
            {
                c.Ships.Remove(s);
                s.Company = null;
            }
            c.Fate = CompanyFate.Bankrupt;
            c.FateJd = jd;
            world.Log(jd, $"{c.Name} of {c.HomePort?.Name ?? "nowhere"} goes bankrupt" + (c.Cash < 0 ? $" owing {Credits(-c.Cash)}" : ""), WorldEventKind.Economy, c.HomePort?.Body ?? -1);
            // What the sales left over goes back where its money came from, rather than out of the world with the company.
            if (c.Cash > 0)
            {
                c.Flag.Treasury += c.Cash;
                c.Cash = 0;
            }
        }

        // ---------------------------------------------------------------- new companies

        /// <summary>
        /// Founds a company when the trade pays and there is room: at the busiest port that has no company of its own,
        /// with capital from investors under the port owner's flag (its treasury), if the companies already trading
        /// earn well enough per hull that a new fleet would pay.
        /// </summary>
        public Company TryFound(double jd)
        {
            if (LiveCount >= MaxCompanies || world.TraderCount() >= world.MerchantCap * 0.9) return null;
            double perHull = 0;
            int hulls = 0;
            foreach (var c in Live)
            {
                if (c.Ships.Count == 0) continue;
                perHull += c.ExpectedYearly(ShipClass.Freighter, MarketPerDay(ShipClass.Freighter)) * c.Ships.Count;
                hulls += c.Ships.Count;
            }
            if (hulls == 0 || perHull / hulls * PaybackYears < Economy.HullPrice(ShipClass.Freighter) * 1.2) return null;
            Settlement best = null;
            foreach (var s in world.Economy.Settlements)
            {
                if (s.Fallen || s.Owner == null || s.Owner.IsPirate || s.Kind == SettlementKind.Haven || s.Owner.Treasury < FoundingCapital * 5) continue;
                bool taken = false;
                foreach (var c in Live) taken |= c.HomePort == s;
                if (taken) continue;
                if (best == null || s.TradeVolumeCredits > best.TradeVolumeCredits) best = s;
            }
            if (best == null) return null;
            best.Owner.Treasury -= FoundingCapital;
            var (name, tag) = NewName(best);
            var co = Found(name, tag, LiveryFor(foundedCount), best, best.Owner, FoundingCapital, jd);
            world.Log(jd, $"{co.Name} is founded at {best.Name}, under the {best.Owner.Name} flag", WorldEventKind.Economy, best.Body);
            return co;
        }

        static readonly string[] Houses = { "Lines", "Carrying Company", "Freight", "Haulage", "Shipping", "Transport", "Bulk Carriers", "Trading Company" };

        (string, string) NewName(Settlement port)
        {
            string place = port.Name.Split(' ')[0];
            for (int tries = 0; tries < Houses.Length; tries++)
            {
                string house = Houses[(foundedCount + tries) % Houses.Length];
                string name = place + " " + house;
                bool used = false;
                foreach (var c in All) used |= c.Name == name;
                if (!used) return (name, TagOf(name));
            }
            string n = place + " Venture " + (foundedCount + 1).ToString(CultureInfo.InvariantCulture);
            return (n, TagOf(n));
        }

        static string TagOf(string name)
        {
            var tag = "";
            foreach (var w in name.Split(' '))
                if (w.Length > 0 && char.IsLetter(w[0])) tag += char.ToUpperInvariant(w[0]);
            return tag.Length > 4 ? tag.Substring(0, 4) : tag;
        }

        /// <summary>A livery for the <paramref name="n"/>th company: hues spaced by the golden angle so neighbours differ.</summary>
        public static Rgb LiveryFor(int n)
        {
            double h = (n * 0.618033988749895 + 0.11) % 1.0;
            return Hsv(h, 0.55, 0.95);
        }

        static Rgb Hsv(double h, double s, double v)
        {
            double r = Channel(5, h, s, v), g = Channel(3, h, s, v), b = Channel(1, h, s, v);
            return new Rgb((float)r, (float)g, (float)b);
        }

        static double Channel(double n, double h, double s, double v)
        {
            double k = (n + h * 6) % 6;
            return v - v * s * Math.Max(0, Math.Min(Math.Min(k, 4 - k), 1));
        }

        public static string Credits(double c) =>
            Math.Abs(c) >= 1e9 ? (c / 1e9).ToString("0.0", CultureInfo.InvariantCulture) + " B cr"
            : Math.Abs(c) >= 1e6 ? (c / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " M cr"
            : c.ToString("N0", CultureInfo.InvariantCulture) + " cr";
    }
}
