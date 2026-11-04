using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>A trading company's offer to charter a hull: the class it wants, for how long, at what hire a day.</summary>
    public sealed class CharterOffer
    {
        public int Id;
        public Company Charterer;
        public ShipClass Class;

        /// <summary>The hire a day, credits, and the term, days.</summary>
        public double RatePerDay, Days;

        public double PostedJd, ExpiresJd;

        /// <summary>The charterer's home port, where the offer is posted.</summary>
        public Settlement Port;

        /// <summary>What the whole term pays.</summary>
        public double Total => RatePerDay * Days;

        public string Title =>
            $"{Charterer.Name}: a {ShipClassSpec.Of(Class).DisplayName.ToLowerInvariant()} for {Days.ToString("0", CultureInfo.InvariantCulture)} d at {RatePerDay.ToString("N0", CultureInfo.InvariantCulture)} cr a day";

        public override string ToString() => Title;
    }

    /// <summary>One of the house's hulls out on a time charter: whose, for how long, and what it has paid and made.</summary>
    public sealed class Charter
    {
        public OwnedShip Ship;
        public Company Charterer;
        public double RatePerDay, StartJd, EndJd;

        /// <summary>Hire the house has been paid, and the trade result the charterer has had of the hull (its gain or loss).</summary>
        public double HirePaid, TradeResult;

        /// <summary>Hire is paid for the time served only: this is the moment it has been paid up to.</summary>
        public double PaidThroughJd;

        public bool Active = true;

        /// <summary>How it ended ("term served", "recalled", "the hull was lost", "the charterer defaulted"), when, and
        /// whether it ran its term.</summary>
        public string EndedHow;
        public double EndedJd = double.NaN;
        public bool Served;

        /// <summary>The orders the hull had before it went out on charter, given back when it returns.</summary>
        public StandingOrder PreviousOrders;

        public double DaysLeft(double jd) => Math.Max(0, EndJd - jd);

        public override string ToString() => Ship?.Name + " to " + Charterer?.Name;
    }

    /// <summary>
    /// Time charters (U.SPC-T426): the trading companies hire hulls they cannot build fast enough. A company whose fleet
    /// earns well posts an offer for a class at a day rate (a share of what such a hull earns it trading,
    /// <see cref="TradeRules.CharterHire"/>, a little more for a trader of rank) for a term of weeks to months. The
    /// player charters a crewed hull of that class out to it: for the term the hull trades for the charterer, which keeps
    /// whatever it makes or loses (propellant and tolls included) and pays the house its hire every day; the house still
    /// pays the crew and captain. At the end of the term the hull goes back to its old orders. The house may recall it
    /// early for <see cref="RecallPenaltyDays"/> days' hire; a charterer that cannot pay (or goes under) ends the charter
    /// itself, and a hull lost on charter is the house's loss (see <see cref="TradeInsurance"/>).
    /// </summary>
    public sealed class Charters
    {
        readonly TradeDesk desk;
        readonly Random rng;

        public readonly List<CharterOffer> Offers = new List<CharterOffer>();

        /// <summary>Every charter taken, running or ended (the latest <see cref="HistoryCap"/> ended ones kept).</summary>
        public readonly List<Charter> All = new List<Charter>();

        int nextId = 1;
        double nextPostJd = double.NaN;

        /// <summary>How often companies post, days; how long an offer stays up; the most offers up at once.</summary>
        public const double PostEveryDays = 5, OfferDays = 15;
        public const int MaxOffers = 8, HistoryCap = 50;

        /// <summary>The shortest and longest term asked, days.</summary>
        public const double MinDays = 30, MaxDays = 120;

        /// <summary>Days' hire the house pays to take a hull back early.</summary>
        public const double RecallPenaltyDays = 10;

        /// <summary>The hulls a company charters: the merchant classes.</summary>
        public static readonly ShipClass[] Classes = { ShipClass.Freighter, ShipClass.Miner, ShipClass.Courier };

        public Charters(TradeDesk desk, int seed)
        {
            this.desk = desk;
            rng = new Random(seed);
        }

        World World => desk.World;
        Holdings Holdings => desk.Holdings;

        /// <summary>The charter <paramref name="o"/> is out on, or null.</summary>
        public Charter Of(OwnedShip o) => o != null && o.OnCharter ? o.Charter : null;

        public IEnumerable<Charter> Active
        {
            get
            {
                foreach (var c in All)
                    if (c.Active) yield return c;
            }
        }

        /// <summary>
        /// What <paramref name="c"/> would pay a day for a hull of <paramref name="cls"/>: the charter share of what such a
        /// hull earns it (its own hulls of the class, else the trade's, else twice the hull's running cost), never less
        /// than its running cost and a fifth again, and a twentieth more a rank of the trader's.
        /// </summary>
        public double RateFor(Company c, ShipClass cls)
        {
            double perDay = c != null ? c.ClassPerDay[(int)cls] : double.NaN;
            if (double.IsNaN(perDay)) perDay = World.Companies?.MarketPerDay(cls) ?? double.NaN;
            if (double.IsNaN(perDay) || perDay <= 0) perDay = 2.5 * Economy.RunningCostPerDay(cls);
            double rate = Math.Max(1.2 * Economy.RunningCostPerDay(cls), perDay * TradeRules.CharterHire.Value);
            return Math.Round(rate * (1 + 0.05 * (int)desk.Rank) / 10) * 10;
        }

        /// <summary>Takes down lapsed offers and has the companies that can afford a hull's hire post new ones.</summary>
        public void Post(double jd)
        {
            Offers.RemoveAll(o => jd >= o.ExpiresJd || o.Charterer == null || !o.Charterer.Trading);
            nextPostJd = jd + PostEveryDays;
            var companies = World.Companies;
            if (companies == null) return;
            var live = new List<Company>(companies.Live);
            // A shuffle, so the first companies listed do not always post first.
            for (int i = live.Count - 1; i > 0; i--)
            {
                int k = rng.Next(i + 1);
                (live[i], live[k]) = (live[k], live[i]);
            }
            foreach (var c in live)
            {
                if (Offers.Count >= MaxOffers) break;
                if (c.HomePort == null || c.HomePort.Fallen) continue;
                bool posted = false;
                foreach (var o in Offers)
                    if (o.Charterer == c) posted = true;
                if (posted || rng.NextDouble() > 0.5) continue;
                var cls = Classes[rng.Next(Classes.Length)];
                double days = Math.Round(MinDays + rng.NextDouble() * (MaxDays - MinDays));
                double rate = RateFor(c, cls);
                // It posts only what it could pay for a month at least.
                if (c.Cash < rate * 30) continue;
                Offers.Add(new CharterOffer
                {
                    Id = nextId++, Charterer = c, Class = cls, RatePerDay = rate, Days = days, PostedJd = jd, ExpiresJd = jd + OfferDays, Port = c.HomePort,
                });
            }
        }

        public enum TakeOutcome
        {
            Ok,

            /// <summary>The offer is gone (taken, lapsed, or its company gone under).</summary>
            Gone,

            /// <summary>Not a house hull, lost, or flown by the player.</summary>
            NotYours,

            /// <summary>Not the class the company asked for.</summary>
            WrongClass,

            /// <summary>No captain: a hull is chartered with its crew.</summary>
            NoCaptain,

            /// <summary>Already out on a charter.</summary>
            Chartered,
        }

        /// <summary>Whether <paramref name="ship"/> can go out on <paramref name="offer"/>.</summary>
        public TakeOutcome CanTake(CharterOffer offer, OwnedShip ship, double jd)
        {
            if (offer == null || !Offers.Contains(offer) || jd >= offer.ExpiresJd || !offer.Charterer.Trading) return TakeOutcome.Gone;
            if (ship == null || ship.Lost || !Holdings.Owns(ship.Ship) || ship.Ship == World.PilotedShip) return TakeOutcome.NotYours;
            if (ship.OnCharter) return TakeOutcome.Chartered;
            if (ship.Ship.Spec.Class != offer.Class) return TakeOutcome.WrongClass;
            if (ship.Captain == null) return TakeOutcome.NoCaptain;
            return TakeOutcome.Ok;
        }

        /// <summary>The first of the house's hulls that could go out on <paramref name="offer"/>, or null.</summary>
        public OwnedShip Candidate(CharterOffer offer, double jd)
        {
            foreach (var o in Holdings.Ships)
                if (CanTake(offer, o, jd) == TakeOutcome.Ok) return o;
            return null;
        }

        /// <summary>
        /// Charters <paramref name="ship"/> out on <paramref name="offer"/>: from now it free trades for the charterer, its
        /// old orders kept for its return. Returns the charter, or null (see <see cref="CanTake"/> for why).
        /// </summary>
        public Charter Take(CharterOffer offer, OwnedShip ship, double jd)
        {
            if (CanTake(offer, ship, jd) != TakeOutcome.Ok) return null;
            var previous = ship.Orders;
            if (!Holdings.SetOrders(ship, StandingOrder.FreeTrade())) return null;
            var c = new Charter
            {
                Ship = ship, Charterer = offer.Charterer, RatePerDay = offer.RatePerDay, StartJd = jd, PaidThroughJd = jd, EndJd = jd + offer.Days, PreviousOrders = previous,
            };
            ship.Charter = c;
            All.Add(c);
            Offers.Remove(offer);
            Holdings.Transact(0, $"The {ship.Name} goes out on charter to {c.Charterer.Name} for {offer.Days:0} d at {TradeDesk.Cr(c.RatePerDay)} a day", jd);
            World.Log(jd, $"{c.Charterer.Name} charters your {ship.Ship.Spec.DisplayName.ToLowerInvariant()} {ship.Name}", WorldEventKind.Economy, ship.Ship.Orbit?.Body ?? -1);
            return c;
        }

        /// <summary>Takes a hull back before its term is out, paying the charterer <see cref="RecallPenaltyDays"/> days' hire.
        /// False if it is not on charter or the account cannot pay.</summary>
        public bool Recall(Charter c, double jd)
        {
            if (c == null || !c.Active) return false;
            double penalty = c.RatePerDay * RecallPenaltyDays;
            if (!Holdings.Transact(-penalty, $"Recalled the {c.Ship.Name} from charter to {c.Charterer.Name}", jd)) return false;
            c.Charterer.Cash += penalty;
            End(c, jd, "recalled", false);
            return true;
        }

        /// <summary>Once a day: every charter pays its hire, or ends (its term out, its hull lost, its charterer unable to pay).</summary>
        public void Daily(double jd)
        {
            foreach (var c in new List<Charter>(Active))
            {
                if (c.Ship.Lost || !Holdings.Owns(c.Ship.Ship))
                {
                    End(c, jd, "the hull was lost", false);
                    continue;
                }
                if (c.Ship.Captain == null)
                {
                    End(c, jd, "its captain walked off", false);
                    continue;
                }
                if (!c.Charterer.Trading)
                {
                    End(c, jd, c.Charterer.Name + " has gone under", false);
                    continue;
                }
                // Hire is for the days served, so the last tick pays only the part of a day left in the term.
                double owed = c.RatePerDay * Math.Max(0, Math.Min(jd, c.EndJd) - c.PaidThroughJd);
                if (c.Charterer.Cash < owed)
                {
                    End(c, jd, c.Charterer.Name + " defaulted on the hire", false);
                    continue;
                }
                c.PaidThroughJd = Math.Min(jd, c.EndJd);
                c.Charterer.Cash -= owed;
                c.HirePaid += owed;
                desk.HireEarned += owed;
                if (owed > 0) Holdings.Transact(owed, $"Charter hire for the {c.Ship.Name} from {c.Charterer.Name}", jd);
                if (jd >= c.EndJd) End(c, jd, "term served", true);
            }
            if (double.IsNaN(nextPostJd) || jd >= nextPostJd) Post(jd);
        }

        void End(Charter c, double jd, string how, bool served)
        {
            c.Active = false;
            c.EndedHow = how;
            c.EndedJd = jd;
            c.Served = served;
            var ship = c.Ship;
            if (served) desk.ChartersServed++;
            if (!ship.Lost)
            {
                ship.Charter = null;
                if (!Holdings.SetOrders(ship, c.PreviousOrders ?? StandingOrder.Hold())) Holdings.SetOrders(ship, StandingOrder.Hold());
            }
            string text = $"The {ship.Name}'s charter to {c.Charterer.Name} ends: {how} ({TradeDesk.Cr(c.HirePaid)} hire paid)";
            desk.Notice(text);
            World.Log(jd, text, WorldEventKind.Economy, ship.Ship.Orbit?.Body ?? -1);
            int ended = 0;
            foreach (var x in All)
                if (!x.Active) ended++;
            for (int i = 0; i < All.Count && ended > HistoryCap; i++)
                if (!All[i].Active)
                {
                    All.RemoveAt(i--);
                    ended--;
                }
        }
    }
}
