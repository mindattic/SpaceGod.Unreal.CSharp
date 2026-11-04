using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>One market's prices as the player last knew them: when, from whom, and what it sold and wanted then.</summary>
    public sealed class PriceSheet
    {
        public Settlement Market;

        /// <summary>When the prices were true, and who reported them ("seen in port", "Capt. Ada Okonkwo", "broker at Ceres").</summary>
        public double Jd;
        public string Source;

        /// <summary>By good: the price a tonne, the tonnes a trader could buy, and the tonnes it would still take.</summary>
        public readonly double[] Price = new double[GoodInfo.Count];
        public readonly double[] Available = new double[GoodInfo.Count];
        public readonly double[] Wants = new double[GoodInfo.Count];

        /// <summary>By good: whether anybody there uses it (a market that only makes a good does not buy it).</summary>
        public readonly bool[] Buys = new bool[GoodInfo.Count];

        public double AgeDays(double jd) => Math.Max(0, jd - Jd);

        public override string ToString() => Market?.Name + " (" + Source + ")";
    }

    /// <summary>A run the player's intelligence says pays: buy here, sell there, what it should make and how far to trust it.</summary>
    public sealed class TradeLead
    {
        public Settlement From, To;
        public Good Good;

        /// <summary>The prices a tonne as last known at each end, the tonnes the run could carry, and the margin a tonne
        /// (sale less purchase, port tax and the price walking against the trade both ends).</summary>
        public double BuyPrice, SellPrice, Tonnes, Margin;

        /// <summary>Margin times tonnes, credits.</summary>
        public double Profit;

        /// <summary>The older of the two price sheets, days; and the risk at the far end, 0..1.</summary>
        public double AgeDays, Risk;

        /// <summary>The far end is closed to the house (blockaded or embargoed): only its back door buys.</summary>
        public bool BackDoor;

        /// <summary>Profit weighed by how fresh the prices are and how safe the run is: what the desk ranks runs by.</summary>
        public double Score;

        public override string ToString() =>
            $"{GoodInfo.Of(Good).Name}: {From.Name} -> {To.Name}, {Profit.ToString("N0", CultureInfo.InvariantCulture)} cr";
    }

    /// <summary>
    /// The player's price intelligence (U.SPC-T426). Prices are not known everywhere at once: the player knows a market's
    /// prices from the last time they were there to see them (the market board at the helm), from the house's own
    /// captains (every owned hull in port reports the markets at its world each day), or from a broker, who sells a
    /// price sheet of every known market in the system at a lawful port. Each sheet ages, and the desk trusts it less as
    /// it does (<see cref="TradeRules.IntelHalfLife"/>). From the sheets it holds, the desk ranks the runs they add up to
    /// (<see cref="Leads"/>): buy where a good is cheap and to spare, sell where it is dear and wanted, weighed by how
    /// fresh the prices are and the risk at the far end.
    /// </summary>
    public sealed class MarketIntel
    {
        readonly TradeDesk desk;
        readonly Dictionary<Settlement, PriceSheet> sheets = new Dictionary<Settlement, PriceSheet>();

        public MarketIntel(TradeDesk desk) => this.desk = desk;

        World World => desk.World;

        /// <summary>Every market the player holds prices for.</summary>
        public IEnumerable<PriceSheet> Sheets => sheets.Values;

        public int Count => sheets.Count;

        /// <summary>The prices last known for <paramref name="s"/>, or null.</summary>
        public PriceSheet Of(Settlement s) => s != null && sheets.TryGetValue(s, out var sheet) ? sheet : null;

        /// <summary>Takes down <paramref name="s"/>'s prices as they stand now (an older report never overwrites a newer one).
        /// Nothing for a market nobody has found.</summary>
        public PriceSheet Observe(Settlement s, double jd, string source)
        {
            if (s == null || !s.Discovered) return null;
            if (sheets.TryGetValue(s, out var sheet))
            {
                if (sheet.Jd > jd) return sheet;
            }
            else sheets[s] = sheet = new PriceSheet { Market = s };
            sheet.Jd = jd;
            sheet.Source = source;
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                var good = (Good)g;
                sheet.Price[g] = s.Price[g];
                sheet.Available[g] = s.Fallen ? 0 : s.Available(good);
                sheet.Wants[g] = s.Fallen ? 0 : s.Absorb(good);
                sheet.Buys[g] = !s.Fallen && s.Demand[g] > 0;
            }
            return sheet;
        }

        /// <summary>Takes down every market's prices at <paramref name="body"/>; returns how many.</summary>
        public int ObserveAt(int body, double jd, string source)
        {
            int n = 0;
            foreach (var s in World.Economy.At(body))
                if (Observe(s, jd, source) != null) n++;
            return n;
        }

        /// <summary>How far the desk trusts a sheet now: 1 when fresh, half after <see cref="TradeRules.IntelHalfLife"/> days.</summary>
        public double Confidence(PriceSheet sheet, double jd) =>
            sheet == null ? 0 : Math.Pow(0.5, sheet.AgeDays(jd) / Math.Max(0.1, TradeRules.IntelHalfLife.Value));

        /// <summary>"today", "3 d old".</summary>
        public static string AgeText(double days) =>
            days < 0.5 ? "today" : days.ToString("0", CultureInfo.InvariantCulture) + " d old";

        /// <summary>Every day the house's captains in port report the markets at their world.</summary>
        public void Daily(double jd)
        {
            foreach (var o in desk.Holdings.Ships)
            {
                if (o.Lost || o.Captain == null) continue;
                var ship = o.Ship;
                if (ship.Orbit == null || (ship.Phase != ShipPhase.Parked && ship.Phase != ShipPhase.Landed)) continue;
                ObserveAt(ship.Orbit.Body, jd, "Capt. " + o.Captain.Name);
            }
        }

        // ---------------------------------------------------------------- brokers

        /// <summary>Whether <paramref name="port"/> has a broker who will sell the house a price sheet: a lawful, standing port that deals with it.</summary>
        public bool HasBroker(Settlement port) =>
            port != null && !port.Fallen && port.Owner != null && !port.Owner.IsPirate && port.Kind != SettlementKind.Haven &&
            World.Diplomacy.CanDockAt(desk.Flag, port);

        /// <summary>What a price sheet costs at a broker now (less for a trader of rank).</summary>
        public double ReportFee => TradeRules.BrokerFee.Value * (1 - desk.BrokerDiscount);

        /// <summary>
        /// Buys a broker's price sheet at <paramref name="port"/>, paid by <paramref name="payer"/> (the house account, or the
        /// purse of the ship the player flies) to the port's owner: the prices of every market in the system anyone has
        /// found, as they stand now. Returns the markets learned, or -1 if there is no broker or the payer cannot pay.
        /// </summary>
        public int BuyReport(Settlement port, Faction payer, double jd)
        {
            if (!HasBroker(port) || payer == null) return -1;
            double fee = ReportFee;
            if (payer == desk.Flag)
            {
                if (!desk.Holdings.Transact(-fee, "A broker's price sheet at " + port.Name, jd)) return -1;
            }
            else
            {
                if (payer.Treasury < fee) return -1;
                payer.Treasury -= fee;
            }
            port.Owner.Treasury += fee;
            int n = 0;
            foreach (var s in World.Economy.Settlements)
                if (Observe(s, jd, "broker at " + port.Name) != null) n++;
            desk.ReportsBought++;
            return n;
        }

        // ---------------------------------------------------------------- the runs

        /// <summary>
        /// The runs the player's prices add up to for a hold of <paramref name="holdTonnes"/>, best first (at most
        /// <paramref name="max"/>): every good to spare at one known market that another known market buys for more, net of
        /// port tax at both ends and the price walking against the trade, sized to what the one can spare, the other
        /// would take and the hold carries. Each is scored by its profit, trusted less the older its prices and the riskier
        /// the far end. Runs from a pirate haven (its goods are stolen) are the back door's business, not the desk's.
        /// </summary>
        public List<TradeLead> Leads(double holdTonnes, double jd, int max = 12, Faction flag = null, CargoHold crateHold = null)
        {
            var leads = new List<TradeLead>();
            // A player flying the crate hold carries only what its free cells take, good by good.
            double[] room = null;
            if (crateHold != null)
            {
                room = new double[GoodInfo.Count];
                holdTonnes = 0;
                for (int g = 0; g < room.Length; g++) { room[g] = crateHold.RoomTonnes((Good)g); holdTonnes = Math.Max(holdTonnes, room[g]); }
            }
            if (holdTonnes <= 0) return leads;
            var known = new List<PriceSheet>(sheets.Values);
            foreach (var a in known)
            {
                var from = a.Market;
                if (from.Fallen || from.Kind == SettlementKind.Haven || (from.Owner != null && from.Owner.IsPirate)) continue;
                var fromRisk = desk.Assess(from, flag);
                // Nobody sells the house anything at a port closed to it.
                if (fromRisk.Closed) continue;
                double ca = Confidence(a, jd);
                foreach (var b in known)
                {
                    if (b == a || b.Market.Fallen) continue;
                    var toRisk = desk.Assess(b.Market, flag);
                    double cb = Confidence(b, jd);
                    for (int g = 0; g < GoodInfo.Count; g++)
                    {
                        if (!b.Buys[g] || a.Available[g] < 1) continue;
                        double buy = a.Price[g], sell = b.Price[g];
                        double margin = sell * 0.97 * (1 - Economy.PortTax) - buy * 1.03 * (1 + Economy.PortTax);
                        if (margin <= 0) continue;
                        double tonnes = Math.Min(room != null ? room[g] : holdTonnes, Math.Min(a.Available[g], b.Wants[g]));
                        if (tonnes < 1) continue;
                        double profit = margin * tonnes;
                        double risk = Math.Max(toRisk.Total, fromRisk.Total);
                        leads.Add(new TradeLead
                        {
                            From = from, To = b.Market, Good = (Good)g, BuyPrice = buy, SellPrice = sell, Tonnes = tonnes, Margin = margin,
                            Profit = profit, AgeDays = Math.Max(a.AgeDays(jd), b.AgeDays(jd)), Risk = risk, BackDoor = toRisk.Closed,
                            Score = profit * ca * cb * (1 - risk),
                        });
                    }
                }
            }
            leads.Sort((x, y) => y.Score.CompareTo(x.Score));
            if (leads.Count > max) leads.RemoveRange(max, leads.Count - max);
            return leads;
        }
    }
}
