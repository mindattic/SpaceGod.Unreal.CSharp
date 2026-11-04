using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>An insurance policy on one of the house's hulls (and its cargo) or warehouses (and its stock).</summary>
    public sealed class InsurancePolicy
    {
        public int Id;

        /// <summary>What it covers: a hull, or a property (a warehouse).</summary>
        public OwnedShip Ship;
        public Property Property;

        /// <summary>What it would pay out now, credits, and its premium a day (both reckoned daily).</summary>
        public double Cover, PremiumPerDay;

        public double SinceJd;

        /// <summary>Premiums paid on it, and what it paid out.</summary>
        public double PremiumsPaid, Paid;

        public bool Active = true;
        public double EndedJd = double.NaN;

        /// <summary>"claim paid", "cancelled", "lapsed: premium unpaid".</summary>
        public string EndedHow;

        public string Name => Ship != null ? "the " + Ship.Name : Property != null ? "the " + Property.Name : "?";

        public override string ToString() => "Policy on " + Name;
    }

    /// <summary>
    /// Underwriters (U.SPC-T426). The house can insure a hull (and whatever it carries) or a warehouse (and what is stored
    /// in it). A policy costs a premium a day: a share a year of what it covers (<see cref="TradeRules.InsuranceRate"/>),
    /// climbing with the risk where the hull trades or the warehouse stands (<see cref="TradeRules.RiskLoading"/>: raiders,
    /// a blockade, an embargo, a port near collapse, see <see cref="TradeDesk.Assess"/>), less for a trader of rank. Past
    /// <see cref="Uninsurable"/> risk nobody will write a new policy. A hull destroyed, taken or cast adrift, or a warehouse
    /// seized or lost with its port, is paid out at <see cref="TradeRules.InsuranceCover"/> of what it was worth. A policy
    /// whose premium the account cannot pay lapses.
    /// </summary>
    public sealed class TradeInsurance
    {
        readonly TradeDesk desk;

        public readonly List<InsurancePolicy> Policies = new List<InsurancePolicy>();

        int nextId = 1;

        /// <summary>The risk (0..1) past which underwriters will not write a new policy.</summary>
        public const double Uninsurable = 0.85;

        /// <summary>The most ended policies kept for the record.</summary>
        public const int HistoryCap = 50;

        public TradeInsurance(TradeDesk desk) => this.desk = desk;

        Holdings Holdings => desk.Holdings;

        /// <summary>The policy in force on <paramref name="o"/>, or null.</summary>
        public InsurancePolicy Of(OwnedShip o)
        {
            foreach (var p in Policies)
                if (p.Active && p.Ship == o) return p;
            return null;
        }

        /// <summary>The policy in force on <paramref name="prop"/>, or null.</summary>
        public InsurancePolicy Of(Property prop)
        {
            foreach (var p in Policies)
                if (p.Active && p.Property == prop) return p;
            return null;
        }

        // ---------------------------------------------------------------- what is at risk

        /// <summary>What a hull is worth to an underwriter: its price, less damage, and its cargo at cost.</summary>
        public static double ValueOf(OwnedShip o)
        {
            var s = o.Ship;
            return Economy.HullPrice(s.Spec.Class) * Math.Max(0.1, 1 - (s.Combat?.Damage ?? 0)) + (s.HasCargo ? s.CargoCost : 0);
        }

        /// <summary>What a warehouse is worth to an underwriter: what it cost, and its stock at base prices.</summary>
        public static double ValueOf(Property p)
        {
            double v = p.PricePaid;
            for (int g = 0; g < GoodInfo.Count; g++) v += p.Stock[g] * GoodInfo.Of((Good)g).BasePrice;
            return v;
        }

        /// <summary>
        /// The risk a hull runs, 0..1: the worst of where it is, where it is bound, and every stop its orders take it to
        /// (a route's or patrol's stops, its claim, its charge's whereabouts); a free trader trades anywhere, so the
        /// system's average danger counts too.
        /// </summary>
        public double Exposure(OwnedShip o)
        {
            var ship = o.Ship;
            double risk = 0;
            void At(int body)
            {
                if (body >= 0) risk = Math.Max(risk, desk.RiskAt(body));
            }
            At(ship.Orbit?.Body ?? -1);
            At(ship.DestinationBody);
            if (ship.CargoDestination != null) risk = Math.Max(risk, desk.Assess(ship.CargoDestination).Total);
            var orders = o.Orders;
            if (orders.Stops != null)
                foreach (int b in orders.Stops) At(b);
            if (orders.Claim != null) At(orders.Claim.Body);
            if (orders.Charge != null) At(orders.Charge.Orbit?.Body ?? -1);
            if (orders.Kind == StandingOrderKind.FreeTrade)
            {
                double sum = 0;
                int n = 0;
                foreach (var s in desk.World.Economy.Settlements)
                {
                    if (!s.Discovered || s.Kind == SettlementKind.Haven) continue;
                    sum += desk.Assess(s).Total;
                    n++;
                }
                if (n > 0) risk = Math.Max(risk, sum / n);
            }
            return risk;
        }

        /// <summary>The risk a warehouse runs, 0..1: its port's.</summary>
        public double Exposure(Property p) => p.Settlement != null ? desk.Assess(p.Settlement).Total : desk.RiskAt(p.Body);

        /// <summary>The premium a day to cover <paramref name="cover"/> credits at <paramref name="risk"/>.</summary>
        public double Premium(double cover, double risk) =>
            cover * TradeRules.InsuranceRate.Value / 365.25 * (1 + TradeRules.RiskLoading.Value * Math.Max(0, Math.Min(1, risk))) * (1 - desk.InsuranceDiscount);

        /// <summary>What a policy on <paramref name="o"/> would cover and cost a day now.</summary>
        public (double cover, double premium, double risk) Quote(OwnedShip o)
        {
            double cover = ValueOf(o) * TradeRules.InsuranceCover.Value, risk = Exposure(o);
            return (cover, Premium(cover, risk), risk);
        }

        /// <summary>What a policy on <paramref name="p"/> would cover and cost a day now.</summary>
        public (double cover, double premium, double risk) Quote(Property p)
        {
            double cover = ValueOf(p) * TradeRules.InsuranceCover.Value, risk = Exposure(p);
            return (cover, Premium(cover, risk), risk);
        }

        // ---------------------------------------------------------------- policies

        /// <summary>Insures a hull (and its cargo); null if it is not the house's, already covered, or too great a risk to write.</summary>
        public InsurancePolicy Insure(OwnedShip o, double jd)
        {
            if (o == null || o.Lost || !Holdings.Owns(o.Ship) || Of(o) != null) return null;
            var (cover, premium, risk) = Quote(o);
            if (risk >= Uninsurable) return null;
            return Write(new InsurancePolicy { Ship = o, Cover = cover, PremiumPerDay = premium }, jd);
        }

        /// <summary>Insures a warehouse (and its stock); null if it is not a standing warehouse of the house's, already
        /// covered, or too great a risk to write.</summary>
        public InsurancePolicy Insure(Property p, double jd)
        {
            if (p == null || p.Lost || p.Kind != PropertyKind.Warehouse || !Holdings.Properties.Contains(p) || Of(p) != null) return null;
            var (cover, premium, risk) = Quote(p);
            if (risk >= Uninsurable) return null;
            return Write(new InsurancePolicy { Property = p, Cover = cover, PremiumPerDay = premium }, jd);
        }

        InsurancePolicy Write(InsurancePolicy p, double jd)
        {
            p.Id = nextId++;
            p.SinceJd = jd;
            Policies.Add(p);
            Holdings.Transact(0, "Insured " + p.Name, jd);
            return p;
        }

        /// <summary>Cancels a policy (no refund: premiums are paid day by day).</summary>
        public bool Cancel(InsurancePolicy p, double jd)
        {
            if (p == null || !p.Active) return false;
            Close(p, jd, "cancelled");
            return true;
        }

        /// <summary>Once a day: every policy's cover and premium follow what it covers and the risk it runs, and the premium is paid (or it lapses).</summary>
        public void Daily(double jd)
        {
            foreach (var p in new List<InsurancePolicy>(Policies))
            {
                if (!p.Active) continue;
                // What it covers is already gone (and paid out, or not covered): nothing more to insure.
                if ((p.Ship != null && p.Ship.Lost) || (p.Property != null && p.Property.Lost))
                {
                    Close(p, jd, "nothing left to cover");
                    continue;
                }
                var (cover, premium, _) = p.Ship != null ? Quote(p.Ship) : Quote(p.Property);
                p.Cover = cover;
                p.PremiumPerDay = premium;
                if (!Holdings.Transact(-premium, "Insurance on " + p.Name, jd))
                {
                    Close(p, jd, "lapsed: premium unpaid");
                    desk.Notice("Your policy on " + p.Name + " lapses: the account cannot pay its premium");
                    continue;
                }
                p.PremiumsPaid += premium;
                desk.PremiumsPaid += premium;
            }
        }

        /// <summary>A hull is lost: its policy pays out.</summary>
        internal void OnShipLost(OwnedShip o, double value, double jd)
        {
            var p = Of(o);
            if (p == null) return;
            PayOut(p, value, jd);
        }

        /// <summary>A warehouse is seized or lost with its port: its policy pays out.</summary>
        internal void OnPropertyLost(Property prop, double value, double jd)
        {
            var p = Of(prop);
            if (p == null) return;
            PayOut(p, value, jd);
        }

        void PayOut(InsurancePolicy p, double value, double jd)
        {
            double paid = Math.Max(0, value) * TradeRules.InsuranceCover.Value;
            p.Paid = paid;
            desk.ClaimsPaid += paid;
            Holdings.Transact(paid, "Insurance pays out on " + p.Name, jd);
            Close(p, jd, "claim paid");
            desk.Notice($"Underwriters pay {TradeDesk.Cr(paid)} for {p.Name}");
        }

        void Close(InsurancePolicy p, double jd, string how)
        {
            p.Active = false;
            p.EndedJd = jd;
            p.EndedHow = how;
            int ended = 0;
            foreach (var x in Policies)
                if (!x.Active) ended++;
            for (int i = 0; i < Policies.Count && ended > HistoryCap; i++)
                if (!Policies[i].Active)
                {
                    Policies.RemoveAt(i--);
                    ended--;
                }
        }
    }
}
