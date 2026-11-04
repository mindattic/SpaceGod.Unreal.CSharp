using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    public enum ShockKind
    {
        /// <summary>A mine shut down by an accident; likelier the harder it is worked and the shorter of machinery.</summary>
        MineAccident,

        /// <summary>A seam worked out: the mine's capacity falls for good (or prospectors find a new one).</summary>
        Depletion,

        /// <summary>A blight in the hydroponic farms; likelier when they are short of nitrogen.</summary>
        Blight,

        /// <summary>Workers down tools because wages have fallen behind the cost of living.</summary>
        Strike,

        /// <summary>A navy at war short of essentials or munitions puts its people on rations.</summary>
        Rationing,

        /// <summary>A besieger holds the space over a port and declares it blockaded through <see cref="Diplomacy"/>.</summary>
        Blockade,

        /// <summary>A navy closes its ports to a neutral's ships for supplying its enemy, through <see cref="Diplomacy"/>.</summary>
        Embargo,
    }

    /// <summary>One economic event in force (or just over), with what it did and where.</summary>
    public sealed class EconomicShock
    {
        public ShockKind Kind;

        /// <summary>The port it hit, or null for one that covers a faction (rationing, an embargo).</summary>
        public Settlement Settlement;

        /// <summary>The faction that declared it (rationing, a blockade, an embargo), or whose port was hit.</summary>
        public Faction Faction;

        /// <summary>The facility it shut, for an accident or a blight.</summary>
        public Facility Facility;

        /// <summary>The good it bears on most directly, if one.</summary>
        public Good? Good;

        public double StartJd, EndJd;
        public string Text;

        /// <summary>For a strike: how far the cost of living had run ahead of wages when it began (1.4 = 40%).</summary>
        public double Severity;

        /// <summary>A short name for the market board ("Strike", "Blockade by the Outer Reach Compact").</summary>
        public string Label;

        public Embargo Embargo;
    }

    /// <summary>
    /// Economic events driven by what the simulation is actually doing, never by a script: mines that are
    /// worked hard have accidents and work their seams out, hydroponic farms short of nitrogen catch a
    /// blight, workers strike when wages fall behind prices, a navy at war that runs short puts its
    /// people on rations, a besieger holding a port declares it blockaded and a navy closes its ports to
    /// a neutral caught running munitions to its enemy (both through <see cref="Diplomacy"/>). Each moves
    /// stock and prices through the ordinary markets, so traders (and the player) react. The news (the
    /// world's event log) reports each one, and every price spike, as it happens.
    /// </summary>
    public sealed class MarketShocks
    {
        readonly World world;
        readonly Random rng;
        double nextDailyJd = double.NaN, firstJd;

        /// <summary>Shocks in force now, oldest first.</summary>
        public readonly List<EconomicShock> Active = new List<EconomicShock>();

        /// <summary>Raised when a shock starts (an instantaneous one, like a worked-out seam, too).</summary>
        public event Action<EconomicShock> Started;

        // ---------------------------------------------------------------- tuning

        /// <summary>Days for wages to close most of a gap to the cost of living (they lag prices).</summary>
        public const double WageLagDays = 120;

        /// <summary>Cost of living over what wages buy at which strikes become possible.</summary>
        public const double StrikeThreshold = 1.3;

        /// <summary>A local price this many times base is a spike worth reporting, and one back under the lower figure is over.</summary>
        public const double SpikeRatio = 2.5, SpikeClear = 1.6;

        /// <summary>The same for a good's average across the system.</summary>
        public const double SystemSpikeRatio = 1.8, SystemSpikeClear = 1.3;

        /// <summary>Daily chance of an accident in a mine at its base level, fully worked; and of a blight in a farm.</summary>
        public double AccidentPerDay = 0.0006, BlightPerDay = 0.00025;

        /// <summary>Tonne-equivalents of war goods (munitions; electronics and alloys count part) a neutral can sell a
        /// navy's enemy, decaying over about <see cref="ContrabandMemoryDays"/>, before that navy embargoes it.</summary>
        public double EmbargoThreshold = 4000;

        public const double ContrabandMemoryDays = 45;

        /// <summary>Who holds the space over a port with a besieging group (by default the war's own siege bookkeeping,
        /// <see cref="War.Besieger"/>); such a besieger declares the port blockaded.</summary>
        public Func<Settlement, Faction> BesiegerOf;

        // ---------------------------------------------------------------- state

        readonly Dictionary<Facility, double> surveyedLevel = new Dictionary<Facility, double>();
        readonly Dictionary<Settlement, double> lastStrikeEndJd = new Dictionary<Settlement, double>();
        readonly HashSet<(Settlement, int)> spiking = new HashSet<(Settlement, int)>();
        readonly Dictionary<(Settlement, int), double> spikeClearedJd = new Dictionary<(Settlement, int), double>();
        readonly bool[] systemSpiking = new bool[GoodInfo.Count];
        readonly double[] systemIndex = new double[GoodInfo.Count];
        readonly Dictionary<(Settlement, int), double> smoothed = new Dictionary<(Settlement, int), double>();
        readonly Dictionary<Settlement, int> besiegerAbsentDays = new Dictionary<Settlement, int>();
        readonly Dictionary<(Faction by, Faction against), double> contraband = new Dictionary<(Faction, Faction), double>();
        readonly Dictionary<(Faction by, Faction against), Faction> contrabandFor = new Dictionary<(Faction, Faction), Faction>();

        public MarketShocks(World world, int seed)
        {
            this.world = world;
            rng = new Random(seed);
            world.Economy.Sold += OnSold;
        }

        Economy Economy => world.Economy;
        Diplomacy Diplomacy => world.Diplomacy;

        /// <summary>The shocks bearing on <paramref name="s"/> now: its own, and its owner's rationing and embargoes.</summary>
        public IEnumerable<EconomicShock> At(Settlement s)
        {
            foreach (var e in Active)
                if (e.Settlement == s || ((e.Kind == ShockKind.Rationing || e.Kind == ShockKind.Embargo) && e.Faction == s.Owner)) yield return e;
        }

        /// <summary>Runs a day at a time up to <paramref name="jd"/>.</summary>
        public void Update(double jd)
        {
            if (world.War == null) return;
            if (double.IsNaN(nextDailyJd)) nextDailyJd = firstJd = jd;
            while (jd >= nextDailyJd)
            {
                Daily(nextDailyJd);
                nextDailyJd += 1;
            }
        }

        void Daily(double jd)
        {
            EndExpired(jd);
            foreach (var s in Economy.Settlements)
            {
                if (s.Fallen || s.Kind == SettlementKind.Haven || s.Owner.IsPirate) continue;
                foreach (var f in s.Facilities)
                {
                    if (f.Recipe.Kind == IndustryKind.Mine) Mine(s, f, jd);
                    else if (f.Recipe.Kind == IndustryKind.Farm) Farm(s, f, jd);
                }
                Wages(s, jd);
            }
            Rationing(jd);
            Blockades(jd);
            Embargoes(jd);
            PriceSpikes(jd);
        }

        // ---------------------------------------------------------------- mines

        static bool IsOre(Facility f) => GoodInfo.Of(f.Recipe.Outputs[0].good).Category == GoodCategory.Mineral;

        void Mine(Settlement s, Facility f, double jd)
        {
            var ore = f.Recipe.Outputs[0].good;
            double perDay = f.Recipe.Outputs[0].perDay;
            if (IsOre(f))
            {
                // A seam holds a few years of output at the mine's base rate (the ice of a moon, its gas, never runs out).
                if (f.Reserve <= 0)
                {
                    surveyedLevel[f] = f.BaseLevel;
                    f.Reserve = perDay * f.BaseLevel * (900 + rng.NextDouble() * 2700);
                    f.Extracted = 0;
                }
                if (f.Extracted >= f.Reserve) Deplete(s, f, ore, jd);
            }

            if (f.Impairment < 1) return;
            // Pushed past its base level and short of machinery for upkeep, a mine gets dangerous.
            double push = Math.Max(0.5, f.Level / Math.Max(f.BaseLevel, 1e-9));
            double machinery = s.Target(Good.Machinery) > 0 ? Math.Min(1, s.Stock[(int)Good.Machinery] / s.Target(Good.Machinery)) : 1;
            double risk = AccidentPerDay * Math.Min(push, 2.5) * Math.Max(0.1, f.Utilisation) * (1 + 3 * (1 - machinery));
            if (rng.NextDouble() >= risk) return;
            double days = 7 + rng.NextDouble() * 21;
            f.Impairment = 0.2;
            string why = machinery < 0.5 ? ", short of machinery for upkeep" : push > 2 ? ", worked far past its design" : "";
            Start(new EconomicShock
            {
                Kind = ShockKind.MineAccident, Settlement = s, Faction = s.Owner, Facility = f, Good = ore, StartJd = jd, EndJd = jd + days,
                Label = "Mine accident (" + Name(ore) + ")",
                Text = $"Accident in the {Name(ore).ToLowerInvariant()} workings at {Where(s)}{why}: extraction cut to a fifth for about {Days(days)}",
            });
        }

        void Deplete(Settlement s, Facility f, Good ore, double jd)
        {
            double perDay = f.Recipe.Outputs[0].perDay;
            double original = surveyedLevel.TryGetValue(f, out var o) ? o : f.BaseLevel;
            string text;
            if (f.BaseLevel * 0.6 < original * 0.4)
            {
                // Worked down to little: the prospectors go out and find another seam.
                double level = original * (0.8 + rng.NextDouble() * 0.4);
                f.Level *= level / f.BaseLevel;
                f.BaseLevel = level;
                text = $"Prospectors at {Where(s)} strike a new {Name(ore).ToLowerInvariant()} seam: extraction is back to {Percent(level / original)} of what it was";
            }
            else
            {
                f.BaseLevel *= 0.6;
                f.Level *= 0.6;
                text = $"The {Name(ore).ToLowerInvariant()} seams at {Where(s)} are playing out: extraction falls to {Percent(f.BaseLevel / original)} of what it was";
            }
            f.Extracted = 0;
            f.Reserve = perDay * f.BaseLevel * (700 + rng.NextDouble() * 2000);
            Announce(new EconomicShock
            {
                Kind = ShockKind.Depletion, Settlement = s, Faction = s.Owner, Facility = f, Good = ore, StartJd = jd, EndJd = jd,
                Label = "Seam worked out (" + Name(ore) + ")", Text = text,
            });
        }

        // ---------------------------------------------------------------- farms

        void Farm(Settlement s, Facility f, double jd)
        {
            if (f.Impairment < 1) return;
            // Crops short of nitrogen are weak; crowded, intensively run farms spread it.
            double target = s.Target(Good.Nitrogen);
            double nitrogen = target > 0 ? Math.Min(1, s.Stock[(int)Good.Nitrogen] / target) : 1;
            double push = Math.Max(0.5, f.Level / Math.Max(f.BaseLevel, 1e-9));
            double risk = BlightPerDay * push * (1 + 4 * (1 - nitrogen));
            if (rng.NextDouble() >= risk) return;
            double days = 15 + rng.NextDouble() * 30;
            f.Impairment = 0.35;
            int food = (int)Good.Food;
            double lost = s.Stock[food] * 0.2;
            s.Stock[food] -= lost;
            s.RefreshPrices();
            Start(new EconomicShock
            {
                Kind = ShockKind.Blight, Settlement = s, Faction = s.Owner, Facility = f, Good = Good.Food, StartJd = jd, EndJd = jd + days,
                Label = "Hydroponics blight",
                Text = $"Blight in the hydroponic farms at {Where(s)}{(nitrogen < 0.5 ? ", short of nitrogen" : "")}: {Tonnes(lost)} of food spoiled, harvests down to a third for about {Days(days)}",
            });
        }

        // ---------------------------------------------------------------- wages and strikes

        void Wages(Settlement s, double jd)
        {
            double living = s.CostOfLiving();
            if (s.WageIndex <= 0) s.WageIndex = living;
            // Wages follow the cost of living, slowly; they hold up better than they rise.
            double lag = living > s.WageIndex ? WageLagDays : WageLagDays * 2;
            s.WageIndex += (living - s.WageIndex) * (1 - Math.Exp(-1 / lag));

            // The markets settle for a month after the start before anyone is counting what wages buy.
            if (s.IndustryFactor < 1 || s.Population < 20000 || jd - firstJd < 30) return;
            if (lastStrikeEndJd.TryGetValue(s, out var last) && jd - last < 45) return;
            double gap = living / s.WageIndex;
            if (gap < StrikeThreshold) return;
            if (rng.NextDouble() >= Math.Min(0.1, (gap - StrikeThreshold) * 0.25 + 0.01)) return;
            double days = 5 + rng.NextDouble() * 15;
            s.IndustryFactor = 0.15;
            var dearest = DearestNeed(s);
            Start(new EconomicShock
            {
                Kind = ShockKind.Strike, Settlement = s, Faction = s.Owner, Good = dearest, StartJd = jd, EndJd = jd + days, Severity = gap,
                Label = "Strike",
                Text = $"Workers at {Where(s)} strike: wages buy {Percent(1 - 1 / gap)} less than they did, {Name(dearest).ToLowerInvariant()} at {Ratio(s, dearest)} normal; industry stops, life support runs",
            });
        }

        /// <summary>The everyday need dearest against its base price at <paramref name="s"/>.</summary>
        static Good DearestNeed(Settlement s)
        {
            var best = Good.Food;
            double top = 0;
            foreach (var (g, _) in s.PersonNeeds)
            {
                double r = s.Price[(int)g] / GoodInfo.Of(g).BasePrice;
                if (r > top)
                {
                    top = r;
                    best = g;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- war rationing

        void Rationing(double jd)
        {
            foreach (var f in world.Fleet.Factions)
            {
                if (!f.IsMilitary || f.IsPirate) continue;
                var current = Find(ShockKind.Rationing, f);
                bool atWar = AtWar(f);
                double essentials = 0, munitions = 1;
                int n = 0, m = 0;
                double munitionSum = 0;
                foreach (var s in Economy.Settlements)
                {
                    if (s.Owner != f || s.Fallen || s.Kind == SettlementKind.Haven) continue;
                    double cover = 1.5;
                    foreach (var g in new[] { Good.Food, Good.Water, Good.MedicalSupplies })
                    {
                        double t = s.Target(g);
                        if (s.Demand[(int)g] > 0 && t > 0) cover = Math.Min(cover, s.Stock[(int)g] / t);
                    }
                    essentials += Math.Min(1.5, cover);
                    n++;
                    double mt = s.Target(Good.Munitions);
                    if (s.HasShipyard && mt > 0)
                    {
                        munitionSum += Math.Min(1.5, s.Stock[(int)Good.Munitions] / mt);
                        m++;
                    }
                }
                if (n == 0)
                {
                    // No ports left to ration: what it had in force goes with them, rather than standing on
                    // forever and landing on the first port it takes back.
                    if (current != null)
                    {
                        current.EndJd = jd;
                        End(current, jd, $"The {f.Name} ends war rationing: it has no ports left to ration");
                    }
                    continue;
                }
                essentials /= n;
                if (m > 0) munitions = munitionSum / m;

                if (current == null)
                {
                    if (!atWar || (essentials >= 0.5 && munitions >= 0.25)) continue;
                    bool food = essentials < 0.5;
                    var shock = new EconomicShock
                    {
                        Kind = ShockKind.Rationing, Faction = f, Good = food ? Good.Food : Good.Munitions, StartJd = jd, EndJd = double.PositiveInfinity,
                        Label = "War rationing",
                        Text = $"The {f.Name} brings in war rationing: {(food ? "stores of essentials are down to " + Percent(essentials) + " of what its ports want" : "munitions are down to " + Percent(munitions) + " of what its yards want")}, so its people get four-fifths of their food, water and medicine",
                    };
                    Start(shock);
                }
                else if (jd - current.StartJd >= 30 && (!atWar || (essentials >= 0.8 && munitions >= 0.6)))
                {
                    current.EndJd = jd;
                    End(current, jd, !atWar ? $"The {f.Name} ends war rationing now the fighting has stopped" : $"The {f.Name} ends war rationing: its stores have recovered");
                }
            }
            // Every port follows its owner's rationing (one taken in a siege goes on its captor's ration).
            foreach (var s in Economy.Settlements) s.Ration = Find(ShockKind.Rationing, s.Owner) != null ? 0.8 : 1;
        }

        bool AtWar(Faction f)
        {
            foreach (var o in world.Fleet.Factions)
                if (o != f && !o.IsPirate && Diplomacy.Get(f, o) == Relation.War) return true;
            return false;
        }

        // ---------------------------------------------------------------- blockades

        void Blockades(double jd)
        {
            bool changed = false;
            foreach (var s in Economy.Settlements)
            {
                if (s.Kind == SettlementKind.Haven) continue;
                var by = BesiegerOf != null ? BesiegerOf(s) : world.War.Besieger(s);
                var held = Diplomacy.BlockadedBy(s);
                // A blockade holds through a day or two of its ships coming and going; it lifts when they have gone.
                besiegerAbsentDays.TryGetValue(s, out int absent);
                besiegerAbsentDays[s] = absent = held != null && by != held ? absent + 1 : 0;
                if (held != null && by != held && (absent >= 3 || s.Owner == held || by != null))
                {
                    Diplomacy.LiftBlockade(s);
                    var shock = Find(ShockKind.Blockade, s);
                    string why = s.Owner == held ? "the port is theirs now" : by != null ? $"the {by.Name} holds the space over it now" : "its ships have gone";
                    if (shock != null)
                    {
                        shock.EndJd = jd;
                        End(shock, jd, $"The {held.Name} lifts its blockade of {Where(s)}: {why}");
                    }
                    held = null;
                    changed = true;
                }
                if (by != null && held == null && by != s.Owner && Diplomacy.DeclareBlockade(s, by))
                {
                    Start(new EconomicShock
                    {
                        Kind = ShockKind.Blockade, Settlement = s, Faction = by, StartJd = jd, EndJd = double.PositiveInfinity,
                        Label = "Blockade by the " + by.Name,
                        Text = $"The {by.Name} declares a blockade of {Where(s)}: no ship may dock there until it is lifted, and the {s.Owner.Name} port lives on what it has",
                    });
                    changed = true;
                }
            }
            if (changed) world.War.RevokeBookings();
        }

        // ---------------------------------------------------------------- embargoes

        static double WarWeight(Good g) => g == Good.Munitions ? 1 : g == Good.Electronics ? 0.5 : g == Good.Alloys ? 0.1 : 0;

        void OnSold(Ship ship, Settlement port, Good good, double tonnes)
        {
            double weight = WarWeight(good);
            if (weight <= 0 || ship.Faction == port.Owner || ship.Faction.IsPirate || port.Owner.IsPirate) return;
            foreach (var by in world.Fleet.Factions)
            {
                if (!by.IsMilitary || by.IsPirate || by == ship.Faction || by == port.Owner) continue;
                // A navy minds who arms its enemy, if the seller is not its enemy already.
                if (Diplomacy.Get(by, port.Owner) != Relation.War || Diplomacy.Hostile(by, ship.Faction)) continue;
                var key = (by, ship.Faction);
                contraband.TryGetValue(key, out double sum);
                contraband[key] = sum + tonnes * weight;
                contrabandFor[key] = port.Owner;
            }
        }

        /// <summary>Tonne-equivalents of war goods <paramref name="against"/>'s traders have lately sold <paramref name="by"/>'s enemies.</summary>
        public double Contraband(Faction by, Faction against) => contraband.TryGetValue((by, against), out var t) ? t : 0;

        void Embargoes(double jd)
        {
            // The navies' memory of who ran what fades.
            foreach (var key in new List<(Faction, Faction)>(contraband.Keys)) contraband[key] *= Math.Exp(-1 / ContrabandMemoryDays);

            bool changed = false;
            foreach (var kv in contraband)
            {
                if (kv.Value < EmbargoThreshold) continue;
                var (by, against) = kv.Key;
                var enemy = contrabandFor[kv.Key];
                if (Diplomacy.Get(by, enemy) != Relation.War) continue;
                string reason = $"running war goods to the {enemy.Name}";
                var e = Diplomacy.DeclareEmbargo(by, against, jd, jd + 90 + rng.NextDouble() * 60, reason);
                if (e == null) continue;
                Start(new EconomicShock
                {
                    Kind = ShockKind.Embargo, Faction = by, Embargo = e, Good = Good.Munitions, StartJd = jd, EndJd = e.UntilJd,
                    Label = $"Embargo on {MerchantsOf(against)}",
                    Text = $"The {by.Name} embargoes {MerchantsOf(against)} for {reason}: its ports are closed to their ships",
                });
                changed = true;
            }
            foreach (var e in new List<Embargo>(Diplomacy.Embargoes))
            {
                // Every system shares the one diplomacy table, but each keeps its own book of who ran war goods: only
                // the system that declared an embargo can judge when it is over, or the others would lift it the next day.
                var shock = Active.Find(x => x.Embargo == e);
                if (shock == null) continue;
                var key = (e.By, e.Against);
                bool warOver = !contrabandFor.TryGetValue(key, out var enemy) || Diplomacy.Get(e.By, enemy) != Relation.War;
                if (jd < e.UntilJd && !warOver) continue;
                Diplomacy.LiftEmbargo(e);
                contraband.Remove(key);
                shock.EndJd = jd;
                End(shock, jd, $"The {e.By.Name} lifts its embargo on {MerchantsOf(e.Against)}" + (warOver ? ": the war it was for has stopped" : ""));
                changed = true;
            }
            if (changed) world.War.RevokeBookings();
        }

        /// <summary>A faction's merchant fleet as the news names it: "the Belt Consortium", "independent merchants".</summary>
        static string MerchantsOf(Faction f) => f.IsMilitary || f.IsPirate ? "the " + f.Name : f.Name.ToLowerInvariant() + " merchants";

        // ---------------------------------------------------------------- price spikes

        /// <summary>Days over which a price is smoothed before it counts as a spike (a navy refitting for an afternoon is not news).</summary>
        public const double SpikeSmoothingDays = 3;

        void PriceSpikes(double jd)
        {
            double k = 1 - Math.Exp(-1 / SpikeSmoothingDays);
            foreach (var s in Economy.Settlements)
            {
                if (s.Fallen || !s.Discovered || s.Kind == SettlementKind.Haven) continue;
                for (int g = 0; g < GoodInfo.Count; g++)
                {
                    var key = (s, g);
                    double now = s.Price[g] / GoodInfo.Of((Good)g).BasePrice;
                    double ratio = smoothed.TryGetValue(key, out var was) ? was + (now - was) * k : now;
                    smoothed[key] = ratio;
                    if (spiking.Contains(key))
                    {
                        if (ratio < SpikeClear)
                        {
                            spiking.Remove(key);
                            spikeClearedJd[key] = jd;
                        }
                        continue;
                    }
                    if (ratio < SpikeRatio || s.Demand[g] <= 0) continue;
                    spiking.Add(key);
                    if (spikeClearedJd.TryGetValue(key, out var cleared) && jd - cleared < 30) continue; // the same flare-up, back again
                    string cause = CauseAt(s, (Good)g);
                    world.Log(jd, $"{Name((Good)g)} at {Where(s)} jumps to {now.ToString("0.0", CultureInfo.InvariantCulture)}x normal, " +
                                  $"{s.Price[g].ToString("N0", CultureInfo.InvariantCulture)} cr/t" + (cause != null ? " (" + cause + ")" : ""), WorldEventKind.Market, s.Body);
                }
            }
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                double now = SystemIndex((Good)g);
                double index = systemIndex[g] = systemIndex[g] > 0 ? systemIndex[g] + (now - systemIndex[g]) * (1 - Math.Exp(-1 / 5.0)) : now;
                string name = Name((Good)g).ToLowerInvariant();
                if (systemSpiking[g])
                {
                    if (index < SystemSpikeClear)
                    {
                        systemSpiking[g] = false;
                        world.Log(jd, $"Across the system, {name} eases back to {index.ToString("0.0", CultureInfo.InvariantCulture)}x normal on average", WorldEventKind.Market);
                    }
                }
                else if (index >= SystemSpikeRatio)
                {
                    systemSpiking[g] = true;
                    world.Log(jd, $"Across the system, {name} now costs {index.ToString("0.0", CultureInfo.InvariantCulture)}x normal on average", WorldEventKind.Market);
                }
            }
        }

        /// <summary>
        /// A good's price against base across the system, weighted by how much each port uses (people and
        /// industry; a navy passing through does not move it), so a handful of thin markets do not swing it.
        /// </summary>
        public double SystemIndex(Good g)
        {
            int i = (int)g;
            double sum = 0, weight = 0;
            foreach (var s in Economy.Settlements)
            {
                if (s.Fallen || s.Kind == SettlementKind.Haven) continue;
                double use = s.Demand[i] - s.ExtraDemand[i];
                if (use <= 0) continue;
                sum += use * s.Price[i] / GoodInfo.Of(g).BasePrice;
                weight += use;
            }
            return weight > 0 ? sum / weight : 1;
        }

        /// <summary>What lies behind a price at <paramref name="s"/>, if a shock does: a blockade, a strike, a blight.</summary>
        string CauseAt(Settlement s, Good g)
        {
            foreach (var e in At(s))
            {
                switch (e.Kind)
                {
                    case ShockKind.Blockade: return "blockaded by the " + e.Faction.Name;
                    case ShockKind.Strike: return "strike";
                    case ShockKind.Blight when g == Good.Food: return "blight";
                    case ShockKind.MineAccident when e.Good == g: return "mine accident";
                }
            }
            return null;
        }

        // ---------------------------------------------------------------- bookkeeping

        EconomicShock Find(ShockKind kind, Faction f)
        {
            foreach (var e in Active)
                if (e.Kind == kind && e.Faction == f) return e;
            return null;
        }

        EconomicShock Find(ShockKind kind, Settlement s)
        {
            foreach (var e in Active)
                if (e.Kind == kind && e.Settlement == s) return e;
            return null;
        }

        void Start(EconomicShock e)
        {
            Active.Add(e);
            Announce(e);
        }

        void Announce(EconomicShock e)
        {
            world.Log(e.StartJd, e.Text, e.Kind == ShockKind.Embargo ? WorldEventKind.Diplomacy : WorldEventKind.Economy, e.Settlement?.Body ?? -1);
            Started?.Invoke(e);
        }

        /// <summary>Shocks that run their course (an accident cleared, a blight over, a strike settled) end on their own.</summary>
        void EndExpired(double jd)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var e = Active[i];
                if (jd < e.EndJd) continue;
                switch (e.Kind)
                {
                    case ShockKind.MineAccident:
                        e.Facility.Impairment = 1;
                        End(e, jd, $"The {Name(e.Good.Value).ToLowerInvariant()} workings at {Where(e.Settlement)} are back at work");
                        break;
                    case ShockKind.Blight:
                        e.Facility.Impairment = 1;
                        End(e, jd, $"The hydroponic farms at {Where(e.Settlement)} are clear of the blight");
                        break;
                    case ShockKind.Strike:
                        var s = e.Settlement;
                        s.IndustryFactor = 1;
                        double living = s.CostOfLiving();
                        double before = s.WageIndex;
                        // The settlement closes most of the gap the strike was called over (or what is left of it now).
                        s.WageIndex = Math.Max(s.WageIndex * (1 + (e.Severity - 1) * 0.6), s.WageIndex + Math.Max(0, living - s.WageIndex) * 0.8);
                        lastStrikeEndJd[s] = jd;
                        End(e, jd, $"The strike at {Where(s)} ends: the workers win a {Percent(s.WageIndex / before - 1)} raise");
                        break;
                    default:
                        // Rationing, blockades and embargoes end when their cause does, in their own passes.
                        break;
                }
            }
        }

        void End(EconomicShock e, double jd, string text)
        {
            Active.Remove(e);
            world.Log(jd, text, e.Kind == ShockKind.Embargo ? WorldEventKind.Diplomacy : WorldEventKind.Economy, e.Settlement?.Body ?? -1);
        }

        // ---------------------------------------------------------------- text

        string Where(Settlement s) => s.Name + " (" + world.Catalog[s.Body].Name + ")";
        static string Name(Good g) => GoodInfo.Of(g).Name;
        static string Ratio(Settlement s, Good g) => (s.Price[(int)g] / GoodInfo.Of(g).BasePrice).ToString("0.0", CultureInfo.InvariantCulture) + "x";
        static string Percent(double f) => (Math.Max(0, f) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        static string Tonnes(double t) => t.ToString("N0", CultureInfo.InvariantCulture) + " t";
        static string Days(double d) => d >= 14 ? Math.Round(d / 7).ToString(CultureInfo.InvariantCulture) + " weeks" : Math.Round(d).ToString(CultureInfo.InvariantCulture) + " days";
    }
}
