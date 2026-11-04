using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>The trading game's tunables (U.SPC-T426), in Settings > Economy.</summary>
    [SettingsSource]
    public static class TradeRules
    {
        static string Percent(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        public static readonly RangeSetting CharterHire = new RangeSetting("trade.charterHire", SettingGroups.Economy,
            "Charter hire", 0.6, 0.2, 1.2, 0.05,
            "What a trading company offers a day to charter one of your hulls, as a share of what a hull of that class earns it trading.",
            Percent, "charter hire freighter company trading");

        public static readonly RangeSetting InsuranceRate = new RangeSetting("trade.insuranceRate", SettingGroups.Economy,
            "Insurance premium", 0.05, 0, 0.25, 0.01,
            "What underwriters charge a year to cover a hull or a warehouse somewhere safe, as a share of what they cover (more where it is dangerous).",
            Percent, "insurance premium underwriter hull cargo warehouse");

        public static readonly RangeSetting InsuranceCover = new RangeSetting("trade.insuranceCover", SettingGroups.Economy,
            "Insurance cover", 0.8, 0.2, 1, 0.05,
            "The share of a lost hull, cargo or warehouse an insurance policy pays out.",
            Percent, "insurance cover claim payout");

        public static readonly RangeSetting RiskLoading = new RangeSetting("trade.riskLoading", SettingGroups.Economy,
            "Risk loading", 4, 0, 10, 0.5,
            "How steeply premiums climb with the danger where a hull trades or a warehouse stands: at full risk, this many times the base premium on top.",
            v => v.ToString("0.#", CultureInfo.InvariantCulture) + "x", "insurance risk piracy blockade collapse premium");

        public static readonly RangeSetting IntelHalfLife = new RangeSetting("trade.intelHalfLife", SettingGroups.Economy,
            "Price intelligence half-life", 8, 1, 60, 1,
            "Days after which a price you saw or bought is trusted only half as much when the trade desk ranks runs.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " d", "intel prices stale age broker news");

        public static readonly RangeSetting BrokerFee = new RangeSetting("trade.brokerFee", SettingGroups.Economy,
            "Broker's fee", 40000, 0, 500000, 5000,
            "What a port's broker charges for a price sheet of every known market in the system (less for a trader of higher rank).",
            v => v.ToString("N0", CultureInfo.InvariantCulture) + " cr", "broker intel price report fee");

        public static readonly RangeSetting CustomsChance = new RangeSetting("trade.customs", SettingGroups.Economy,
            "Customs vigilance", 0.2, 0, 1, 0.05,
            "The chance a back-door sale of contraband at a lawful port is caught (a blockade's pickets catch more, a fence of stolen goods fewer).",
            Percent, "smuggling customs contraband black market blockade catch");

        public static readonly RangeSetting BackDoorPremium = new RangeSetting("trade.backDoorPremium", SettingGroups.Economy,
            "Smuggling premium", 1.5, 1, 4, 0.1,
            "How much over the port's own price its back door pays for contraband, a blockade run or an embargo broken.",
            v => v.ToString("0.0", CultureInfo.InvariantCulture) + "x", "smuggling black market premium blockade embargo");
    }

    /// <summary>What a trader faces at a port: raiders, a blockade or an embargo closing it, and the place falling apart.</summary>
    public readonly struct PortRisk
    {
        /// <summary>The war's tally of fights and raids at its body, 0..1 (<see cref="Contracts.DangerAt"/>).</summary>
        public readonly double Piracy;

        /// <summary>How near the settlement is to collapse, 0..1: unrest, hunger and want, people leaving; 1 for a fallen one.</summary>
        public readonly double Collapse;

        /// <summary>Who holds it blockaded, or null.</summary>
        public readonly Faction BlockadedBy;

        /// <summary>Whether its owner has closed its ports to the trader's flag.</summary>
        public readonly bool Embargoed;

        /// <summary>All of it in one figure, 0..1: the chance-weighted share of a run's value a trader should expect to lose.</summary>
        public readonly double Total;

        public PortRisk(double piracy, double collapse, Faction blockadedBy, bool embargoed)
        {
            Piracy = Clamp01(piracy);
            Collapse = Clamp01(collapse);
            BlockadedBy = blockadedBy;
            Embargoed = embargoed;
            double t = 1 - (1 - Piracy) * (1 - 0.6 * Collapse);
            if (blockadedBy != null || embargoed) t = Math.Max(t, 0.5);
            Total = Clamp01(t);
        }

        /// <summary>Shut to the trader's open business: only the back door (see <see cref="Smuggling"/>) is left.</summary>
        public bool Closed => BlockadedBy != null || Embargoed;

        /// <summary>A few words for the desk: "raiders, near collapse", "blockaded by the Outer Reach Compact".</summary>
        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (BlockadedBy != null) parts.Add("blockaded by the " + BlockadedBy.Name);
                if (Embargoed) parts.Add("embargoed");
                if (Piracy >= 0.5) parts.Add("raiders active");
                else if (Piracy >= 0.15) parts.Add("raids lately");
                if (Collapse >= 0.99) parts.Add("fallen");
                else if (Collapse >= 0.5) parts.Add("near collapse");
                else if (Collapse >= 0.25) parts.Add("unsettled");
                return parts.Count == 0 ? "quiet" : string.Join(", ", parts);
            }
        }

        static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Max(0, Math.Min(1, v));
    }

    /// <summary>A trader's standing in the trade, by what their house is worth (<see cref="TradeDesk.NetWorth"/>).</summary>
    public enum TraderRank
    {
        Peddler,
        Hauler,
        Merchant,
        Shipowner,
        Magnate,
        MerchantPrince,
    }

    /// <summary>One piece of trade news for the desk: where, what, and why it matters to a trader.</summary>
    public sealed class TradeTip
    {
        public double Jd;
        public string Text;
        public Settlement Market;
        public Good? Good;

        /// <summary>How much it matters, for ordering the desk's list (higher first).</summary>
        public double Weight;

        public override string ToString() => Text;
    }

    /// <summary>
    /// The trading game (U.SPC-T426): a full trader's career on the live economy, beside the market boards (buying and
    /// selling at the helm), the contracts boards and the player's own fleet and property (<see cref="Holdings"/>). It
    /// keeps the player's price intelligence (<see cref="MarketIntel"/>: prices seen in port, reported by the house's
    /// captains, bought from brokers, and the runs they add up to), the trade news that moves markets (shocks,
    /// shortages, ports at risk), time charters of the house's hulls to the trading companies (<see cref="Charters"/>),
    /// insurance on hulls, cargoes and warehouses (<see cref="TradeInsurance"/>), the back door of every lawful port for
    /// contraband, blockade runs and embargoes broken (<see cref="Smuggling"/>), and the trader's rank by what the house
    /// is worth. Everything prices risk the same way (<see cref="Assess"/>): raiders, blockades, embargoes and collapse.
    /// New kinds of trade news plug in by registering a source in <see cref="TipSources"/>.
    /// </summary>
    public sealed class TradeDesk
    {
        readonly World world;

        public readonly MarketIntel Intel;
        public readonly Charters Charters;
        public readonly TradeInsurance Insurance;
        public readonly Smuggling Smuggling;

        /// <summary>The trader's rank as last reckoned (it is reckoned daily, and announced when it rises).</summary>
        public TraderRank Rank { get; private set; }

        /// <summary>The best rank reached.</summary>
        public TraderRank BestRank { get; private set; }

        double nextDailyJd = double.NaN;
        readonly List<string> notices = new List<string>();

        /// <summary>Net worth, credits, at which each rank is reached (index by <see cref="TraderRank"/>).</summary>
        public static readonly double[] RankWorth = { 0, 2e7, 1e8, 4e8, 1.5e9, 5e9 };

        /// <summary>
        /// Where the desk's trade news comes from: each source is asked for its tips in turn (the market shocks, shortages
        /// the player's intelligence shows, risks to what the house owns). A new kind of trade news registers a source.
        /// </summary>
        public static readonly List<Func<TradeDesk, double, IEnumerable<TradeTip>>> TipSources = new List<Func<TradeDesk, double, IEnumerable<TradeTip>>>
        {
            ShockTips,
            ShortageTips,
            RiskTips,
        };

        public TradeDesk(World world, int seed)
        {
            this.world = world;
            Intel = new MarketIntel(this);
            Charters = new Charters(this, seed + 1);
            Insurance = new TradeInsurance(this);
            Smuggling = new Smuggling(this, seed + 2);
            world.Holdings.ShipLost += Insurance.OnShipLost;
            world.Holdings.PropertyLost += Insurance.OnPropertyLost;
        }

        public World World => world;
        public Holdings Holdings => world.Holdings;

        /// <summary>The flag the trader's business sails under (the house's).</summary>
        public Faction Flag => world.Holdings.House;

        // ---------------------------------------------------------------- the clock

        /// <summary>Once a day: captains report prices, charters pay their hire and end, premiums are paid, the rank is reckoned.</summary>
        public void Update(double jd)
        {
            if (double.IsNaN(nextDailyJd))
            {
                nextDailyJd = jd + 1;
                Charters.Post(jd);
                return;
            }
            for (int guard = 0; guard < 4000 && jd >= nextDailyJd; guard++)
            {
                double day = nextDailyJd;
                Intel.Daily(day);
                Charters.Daily(day);
                Insurance.Daily(day);
                Reckon(day);
                nextDailyJd += 1;
            }
        }

        // ---------------------------------------------------------------- risk

        /// <summary>What a trader under <paramref name="flag"/> faces at <paramref name="s"/> now (see <see cref="PortRisk"/>).</summary>
        public PortRisk Assess(Settlement s, Faction flag = null)
        {
            if (s == null) return default;
            flag ??= Flag;
            double piracy = world.Contracts != null ? world.Contracts.DangerAt(s.Body) : 0;
            bool embargoed = s.Owner != null && flag != null && world.Diplomacy.IsEmbargoed(s.Owner, flag);
            return new PortRisk(piracy, CollapseOf(s), world.Diplomacy.BlockadedBy(s), embargoed);
        }

        /// <summary>The worst a trader faces at any port on <paramref name="body"/> (just the raiding where there is none).</summary>
        public double RiskAt(int body)
        {
            double worst = -1;
            foreach (var s in world.Economy.At(body))
                if (s.Discovered) worst = Math.Max(worst, Assess(s).Total);
            if (worst >= 0) return worst;
            return world.Contracts != null ? Math.Max(0, Math.Min(1, world.Contracts.DangerAt(body))) : 0;
        }

        /// <summary>
        /// How near <paramref name="s"/> is to collapse, 0..1: unrest under an occupier, its people going hungry, thirsty or
        /// untreated (as remembered over a month), and its people leaving it below what it was founded with. 1 if fallen.
        /// </summary>
        public static double CollapseOf(Settlement s)
        {
            if (s == null) return 0;
            if (s.Fallen) return 1;
            double unrest = Math.Max(0, Math.Min(1, s.Unrest));
            double living = Math.Min(s.Wellbeing, 0.5 + 0.5 * s.HealthWellbeing);
            double want = Math.Max(0, Math.Min(1, (0.85 - living) / 0.85));
            double shrink = s.InitialPopulation > 0 ? Math.Max(0, Math.Min(1, (0.9 - s.Population / s.InitialPopulation) / 0.6)) : 0;
            return Math.Max(0, Math.Min(1, 0.45 * unrest + 0.6 * want + 0.3 * shrink));
        }

        // ---------------------------------------------------------------- the career

        /// <summary>
        /// What the house is worth, credits: the account, its hulls at second-hand prices (less damage) and the cargo they
        /// carry at cost, its warehouses and what is in them at base prices, its shares at their price today, half what
        /// its claims cost; and <paramref name="purse"/>'s treasury if the player flies another flag's ship.
        /// </summary>
        public double NetWorth(Faction purse = null)
        {
            var h = Holdings;
            double v = h.Balance + h.CargoAfloat;
            foreach (var o in h.Ships)
            {
                if (o.Lost) continue;
                v += Companies.ResaleShare * Economy.HullPrice(o.Ship.Spec.Class) * Math.Max(0.1, 1 - (o.Ship.Combat?.Damage ?? 0));
            }
            foreach (var p in h.Properties)
            {
                if (p.Lost) continue;
                switch (p.Kind)
                {
                    case PropertyKind.Warehouse:
                        v += p.PricePaid;
                        for (int g = 0; g < GoodInfo.Count; g++) v += p.Stock[g] * GoodInfo.Of((Good)g).BasePrice;
                        break;
                    case PropertyKind.StationShare:
                        v += Holdings.SharePrice(p.Settlement, p.Share);
                        break;
                    default:
                        v += 0.5 * p.PricePaid;
                        break;
                }
            }
            if (purse != null && purse != h.House) v += purse.Treasury;
            return v;
        }

        /// <summary>The rank a house worth <paramref name="worth"/> holds.</summary>
        public static TraderRank RankFor(double worth)
        {
            var r = TraderRank.Peddler;
            for (int i = 0; i < RankWorth.Length; i++)
                if (worth >= RankWorth[i]) r = (TraderRank)i;
            return r;
        }

        public static string RankName(TraderRank r)
        {
            switch (r)
            {
                case TraderRank.Hauler: return "Hauler";
                case TraderRank.Merchant: return "Merchant";
                case TraderRank.Shipowner: return "Shipowner";
                case TraderRank.Magnate: return "Shipping magnate";
                case TraderRank.MerchantPrince: return "Merchant prince";
                default: return "Peddler";
            }
        }

        /// <summary>The worth the next rank needs, or NaN at the top.</summary>
        public double NextRankWorth => (int)Rank + 1 < RankWorth.Length ? RankWorth[(int)Rank + 1] : double.NaN;

        /// <summary>A broker's discount for the trader's rank (a tenth a rank).</summary>
        public double BrokerDiscount => Math.Min(0.5, 0.1 * (int)Rank);

        /// <summary>Underwriters' discount for the trader's rank (a twentieth a rank: a known name is a better risk).</summary>
        public double InsuranceDiscount => Math.Min(0.25, 0.05 * (int)Rank);

        /// <summary>Over the career: back-door sales, their takings and the times customs caught them; charters served and
        /// their hire; premiums paid and claims paid out; price sheets bought.</summary>
        public int Smuggled, TimesCaught, ChartersServed, ReportsBought;
        public double SmugglingTakings, HireEarned, PremiumsPaid, ClaimsPaid;

        void Reckon(double jd)
        {
            var r = RankFor(NetWorth(world.PilotedShip != null && world.PilotedShip.Faction != Flag ? world.PilotedShip.Faction : null));
            Rank = r;
            if (r <= BestRank) return;
            BestRank = r;
            string text = "Your house is now counted a " + RankName(r).ToLowerInvariant() + "'s";
            Notice(text);
            world.Log(jd, text, WorldEventKind.Economy);
        }

        // ---------------------------------------------------------------- trade news

        /// <summary>The trade news the desk knows of now, most important first (at most <paramref name="max"/>).</summary>
        public List<TradeTip> Tips(double jd, int max = 12)
        {
            var list = new List<TradeTip>();
            foreach (var source in TipSources)
            {
                var tips = source(this, jd);
                if (tips != null) list.AddRange(tips);
            }
            list.Sort((a, b) => b.Weight.CompareTo(a.Weight));
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        /// <summary>Each economic event in force, and what it means for a trader.</summary>
        static IEnumerable<TradeTip> ShockTips(TradeDesk desk, double jd)
        {
            foreach (var e in desk.world.Shocks.Active)
            {
                if (e.Settlement != null && !e.Settlement.Discovered) continue;
                string where = e.Settlement != null ? e.Settlement.Name : e.Faction != null ? "the " + e.Faction.Name : "the system";
                string good = e.Good != null ? GoodInfo.Of(e.Good.Value).Name.ToLowerInvariant() : null;
                string text;
                double weight = 2;
                switch (e.Kind)
                {
                    case ShockKind.MineAccident:
                    case ShockKind.Depletion:
                        text = $"{e.Label} at {where}: {good ?? "ore"} will run short there";
                        break;
                    case ShockKind.Blight:
                        text = $"Blight at {where}: food will be dear there";
                        weight = 3;
                        break;
                    case ShockKind.Strike:
                        text = $"Strike at {where}: its industry is idle, its goods will run short";
                        break;
                    case ShockKind.Rationing:
                        text = $"{where} rations its people: essentials fetch a premium in its ports";
                        weight = 3;
                        break;
                    case ShockKind.Blockade:
                        text = $"{where} is blockaded{(e.Faction != null ? " by the " + e.Faction.Name : "")}: closed to trade, but its back door pays well for a run";
                        weight = 4;
                        break;
                    case ShockKind.Embargo:
                        text = $"{e.Label} declared by {where}";
                        weight = 3;
                        break;
                    default:
                        text = e.Label + " at " + where;
                        break;
                }
                yield return new TradeTip { Jd = e.StartJd, Text = text, Market = e.Settlement, Good = e.Good, Weight = weight };
            }
        }

        /// <summary>Markets the player's intelligence shows paying well over the odds for something (at least twice its base price).</summary>
        static IEnumerable<TradeTip> ShortageTips(TradeDesk desk, double jd)
        {
            foreach (var sheet in desk.Intel.Sheets)
            {
                if (sheet.Market.Fallen) continue;
                for (int g = 0; g < GoodInfo.Count; g++)
                {
                    if (!sheet.Buys[g]) continue;
                    double ratio = sheet.Price[g] / GoodInfo.Of((Good)g).BasePrice;
                    if (ratio < 2) continue;
                    double age = sheet.AgeDays(jd);
                    yield return new TradeTip
                    {
                        Jd = sheet.Jd, Market = sheet.Market, Good = (Good)g,
                        Text = $"{sheet.Market.Name} pays {ratio.ToString("0.0", CultureInfo.InvariantCulture)}x for {GoodInfo.Of((Good)g).Name.ToLowerInvariant()} ({MarketIntel.AgeText(age)})",
                        Weight = 1 + ratio * desk.Intel.Confidence(sheet, jd),
                    };
                }
            }
        }

        /// <summary>What the house owns at a port that is in danger: a warehouse or share where it is blockaded, raided or collapsing.</summary>
        static IEnumerable<TradeTip> RiskTips(TradeDesk desk, double jd)
        {
            foreach (var p in desk.Holdings.Properties)
            {
                if (p.Lost || p.Settlement == null) continue;
                var risk = desk.Assess(p.Settlement);
                if (risk.Total < 0.35) continue;
                bool insured = desk.Insurance.Of(p) != null;
                yield return new TradeTip
                {
                    Jd = jd, Market = p.Settlement,
                    Text = $"Your {p.Name}: {risk.Summary}{(insured ? "" : ", uninsured")}",
                    Weight = 3 + 3 * risk.Total,
                };
            }
        }

        // ---------------------------------------------------------------- messages

        /// <summary>Records a message for the player (shown as a toast by the HUD).</summary>
        public void Notice(string text)
        {
            notices.Add(text);
            if (notices.Count > 20) notices.RemoveAt(0);
        }

        /// <summary>The player's messages since the last call, oldest first.</summary>
        public List<string> TakeNotices()
        {
            var list = new List<string>(notices);
            notices.Clear();
            return list;
        }

        public static string Cr(double v) => v.ToString("N0", CultureInfo.InvariantCulture) + " cr";
    }
}
