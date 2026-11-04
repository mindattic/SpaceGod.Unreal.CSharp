using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>The jobs a station's contracts board posts (see <see cref="Contracts"/>).</summary>
    public enum ContractKind
    {
        /// <summary>Carry a lot of a good a settlement is short of from a port that has it.</summary>
        Freight,

        /// <summary>Carry a small party of people moving for a better life (migrants travelling on their own account).</summary>
        Passengers,

        /// <summary>Carry a colony party: a settlement people are leaving in numbers sends them where people are going.</summary>
        Colonists,

        /// <summary>See a valuable cargo safely in to a dangerous port: be there when it arrives.</summary>
        Escort,

        /// <summary>Hold station over a settlement that has seen fighting or raiders lately.</summary>
        Patrol,

        /// <summary>Go and look at a body no ship has been to.</summary>
        Survey,

        /// <summary>Kill or take a wanted captain (the board's copy of a bounty, see <see cref="Outlaws"/>).</summary>
        Bounty,
    }

    public enum ContractState
    {
        /// <summary>On the board, waiting for a taker.</summary>
        Open,

        /// <summary>Taken: the taker is working it.</summary>
        Taken,

        Completed,

        /// <summary>Taken and not done: the deadline passed, the cargo or the passengers were lost, the client died.</summary>
        Failed,

        /// <summary>Taken and given up by the taker.</summary>
        Abandoned,

        /// <summary>Never taken: the offer lapsed, or the need it was posted for went away.</summary>
        Expired,
    }

    /// <summary>One job on a contracts board: what, for whom, where, what it pays, and who has taken it.</summary>
    public sealed class Contract
    {
        public int Id;
        public ContractKind Kind;
        public ContractState State = ContractState.Open;

        /// <summary>The settlement whose board it is posted on (where a taker signs, and where freight is loaded and people board).</summary>
        public Settlement Board;

        /// <summary>Who pays (and whose standing it moves).</summary>
        public Faction Patron;

        /// <summary>Where freight, passengers and colonists go; null for the other kinds.</summary>
        public Settlement Destination;

        /// <summary>The body to patrol, survey or escort the client to; for freight and people, the destination's body.</summary>
        public int Body = -1;

        /// <summary>Freight: the good, the lot and what has been handed over so far, tonnes.</summary>
        public Good Good;
        public double Tonnes, Delivered;

        /// <summary>For freight, how short the destination was when it was posted: its stock (with what was on the way) over the
        /// stock its market wants (<see cref="Settlement.Target"/>).</summary>
        public double CoverAtPost;

        /// <summary>Passengers and colonists: how many; and whether they are aboard the taker's hull.</summary>
        public int People;
        public bool Aboard;

        /// <summary>Escort: the merchant to see in, and when it got there (NaN until it does).</summary>
        public Ship Client;
        public double ClientArrivedJd = double.NaN;

        /// <summary>Bounty: whose head.</summary>
        public LawRecord Target;

        /// <summary>Patrol: days on station needed, and done.</summary>
        public double PatrolDays, OnStation;

        /// <summary>What it pays on completion, credits; and what went into the price: distance (km from the board),
        /// danger (0..1 at the far end) and urgency (0..1).</summary>
        public double Pay, DistanceKm, Danger, Urgency;

        /// <summary>When it was posted, when the offer lapses untaken, how long a taker has once it signs, and when it is due.</summary>
        public double PostedJd, ExpiresJd, DeadlineDays, DueJd = double.NaN;

        public double TakenJd = double.NaN, ClosedJd = double.NaN;

        /// <summary>The hull that took it (the group leader for a hunter group), the record of whoever commands it, and
        /// whether that is the player.</summary>
        public Ship Taker;
        public LawRecord TakerRecord;

        /// <summary>The colours the taker flew when it took the job: a hull taken as a prize since is lost to it.</summary>
        public Faction TakerFaction;
        public bool ByPlayer;

        public bool IsOpen => State == ContractState.Open;
        public bool IsActive => State == ContractState.Taken;
        public bool IsClosed => State != ContractState.Open && State != ContractState.Taken;

        /// <summary>The job in a few words: "120 t Food to Ares Prime".</summary>
        public string Title
        {
            get
            {
                string dest = Destination != null ? Destination.Name : null;
                switch (Kind)
                {
                    case ContractKind.Freight: return Tonnes.ToString("N0", CultureInfo.InvariantCulture) + " t " + GoodInfo.Of(Good).Name + " to " + dest;
                    case ContractKind.Passengers: return People + (People == 1 ? " passenger" : " passengers") + " to " + dest;
                    case ContractKind.Colonists: return People.ToString("N0", CultureInfo.InvariantCulture) + " colonists to " + dest;
                    case ContractKind.Escort: return "Escort the " + (Client != null ? Client.Name : "merchant") + " in to " + BodyLabel;
                    case ContractKind.Patrol: return "Patrol " + BodyLabel + " for " + PatrolDays.ToString("0.#", CultureInfo.InvariantCulture) + " d";
                    case ContractKind.Survey: return "Survey " + BodyLabel;
                    default: return "Bounty on " + (Target != null ? (Target.IsPlayer ? "you" : Target.Name) : "a wanted captain");
                }
            }
        }

        /// <summary>The body's name, set when posted (the contract holds no catalog).</summary>
        public string BodyLabel;

        public override string ToString() => Title;
    }

    /// <summary>
    /// Contracts boards (U.SPC-T101). Every lawful station posts jobs from what the simulation actually needs:
    /// freight runs of a good a settlement is short of (from the nearest port that has it to spare), passengers
    /// and colony parties from places people are leaving to where they are going (<see cref="People.Migrate"/>),
    /// escorts for valuable cargoes bound into danger, patrols over settlements that have seen fighting or raiders,
    /// surveys of bodies no ship has been to, and the bounties on the law's books. Pay reflects distance, danger at
    /// the far end and urgency (how short the settlement is, how fast the people are leaving). The player takes
    /// jobs at the board of the port they are alongside (F7); NPC captains take them too -- merchants freight and
    /// people, free ships surveys, bounty hunters patrols, escorts and bounties -- and every contract either
    /// completes (the patron pays and the taker's standing rises), or fails or is abandoned (and standing with
    /// the patron falls; below <see cref="Law.ContractMinStanding"/> the patron offers no more). An offer nobody
    /// takes lapses, and one whose need has gone (the shortage filled, the client arrived) is taken down.
    /// </summary>
    public sealed class Contracts
    {
        readonly World world;
        readonly Random rng;

        /// <summary>Every contract posted and not yet forgotten: open, taken, and the latest closed ones (newest last).</summary>
        public readonly List<Contract> All = new List<Contract>();

        /// <summary>Bodies some ship has been to, and when it was last there (every settled body counts as visited).</summary>
        public readonly Dictionary<int, double> Visited = new Dictionary<int, double>();

        int nextId = 1;
        double nextDailyJd = double.NaN;

        /// <summary>The player's ship as last flown (for deliveries, patrols and surveys at the helm).</summary>
        int pilotBody = -1;
        DVec3 pilotOffset;
        double pilotJd = double.NaN;

        // ---------------------------------------------------------------- tuning

        /// <summary>A market with less than this share of the stock it wants (counting what is on the way) is short, and posts freight.</summary>
        public const double ShortCover = 0.5;

        /// <summary>The cover freight contracts restore a short market to (and past which an open one is taken down).</summary>
        public const double RestoreCover = 0.7;

        /// <summary>The biggest bulk lot one freight contract asks for, tonnes; and the crates a parcel lot (sized for a pilot's hold) asks for.</summary>
        public const double BulkLotTonnes = 8000;
        public const int ParcelCrates = 8;

        /// <summary>How long an untaken offer stays up, days, by kind.</summary>
        public const double OfferDays = 12, SurveyOfferDays = 30;

        /// <summary>The most open contracts one board holds.</summary>
        public const int BoardCap = 14;

        /// <summary>A settlement losing people faster than this (a day) posts a colony party; any loss at all posts passengers.</summary>
        public const double ColonistOutflow = 20;

        /// <summary>The share of its founding people a settlement keeps whatever passage is posted from it.</summary>
        public const double MinPeopleShare = 0.6;

        /// <summary>Danger (0..1) at a settlement past which its owner posts a patrol, and into which a valuable cargo wants an escort.</summary>
        public const double PatrolDanger = 0.3, EscortDanger = 0.3;

        /// <summary>A cargo worth this much at base prices is worth escorting, credits.</summary>
        public const double EscortCargoValue = 2e6;

        /// <summary>How long after the client arrives the escort may still be there to count, days.</summary>
        public const double EscortWindowDays = 1;

        /// <summary>Days on station a patrol asks for.</summary>
        public const double PatrolStationDays = 1;

        /// <summary>Standing a completed contract earns (more for an urgent one), and what failing or abandoning one costs.</summary>
        public const double CompleteStanding = 2, UrgentStanding = 3, FailStanding = 6, AbandonStanding = 4;

        /// <summary>How many closed contracts are kept for the record.</summary>
        public const int HistoryCap = 200;

        /// <summary>The chance an idle NPC captain looks at the board before it goes about its usual business.</summary>
        public const double NpcTakeChance = 0.6;

        public Contracts(World world, int seed)
        {
            this.world = world;
            rng = new Random(seed);
            foreach (var s in world.Economy.Settlements) Visited[s.Body] = double.NegativeInfinity;
        }

        Economy Economy => world.Economy;
        Law Law => world.Law;

        // ---------------------------------------------------------------- the clock

        /// <summary>Once a day: needs posted as contracts, lapsed offers taken down, deadlines and losses judged.</summary>
        public void Update(double jd)
        {
            if (double.IsNaN(nextDailyJd))
            {
                Post(jd);
                nextDailyJd = jd + 1;
                return;
            }
            while (jd >= nextDailyJd)
            {
                Review(nextDailyJd);
                Post(nextDailyJd);
                nextDailyJd += 1;
            }
        }

        // ---------------------------------------------------------------- the boards

        /// <summary>The open contracts on <paramref name="port"/>'s board, best paid first. A haven keeps no board.</summary>
        public List<Contract> BoardAt(Settlement port)
        {
            var list = new List<Contract>();
            if (!Lawful(port)) return list;
            foreach (var c in All)
                if (c.IsOpen && c.Board == port) list.Add(c);
            list.Sort((a, b) => b.Pay.CompareTo(a.Pay));
            return list;
        }

        /// <summary>The contracts the player has taken and not yet finished, oldest first.</summary>
        public List<Contract> PlayerActive()
        {
            var list = new List<Contract>();
            foreach (var c in All)
                if (c.IsActive && c.ByPlayer) list.Add(c);
            return list;
        }

        /// <summary>The contract <paramref name="ship"/> (or the group it flies in) is working, or null.</summary>
        public Contract ActiveFor(Ship ship)
        {
            if (ship == null) return null;
            var lead = ship.Controller;
            foreach (var c in All)
                if (c.IsActive && !c.ByPlayer && (c.Taker == lead || c.Taker == ship)) return c;
            return null;
        }

        static bool Lawful(Settlement s) => s != null && s.Owner != null && !s.Owner.IsPirate && !s.Fallen && s.Kind != SettlementKind.Haven;

        int OpenAt(Settlement board)
        {
            int n = 0;
            foreach (var c in All)
                if (c.IsOpen && c.Board == board) n++;
            return n;
        }

        Contract Add(Contract c, double jd)
        {
            c.Id = nextId++;
            c.PostedJd = jd;
            if (c.ExpiresJd <= jd) c.ExpiresJd = jd + OfferDays;
            if (c.Body >= 0 && c.Body < world.Catalog.Count) c.BodyLabel = world.Catalog[c.Body].Name;
            All.Add(c);
            return c;
        }

        // ---------------------------------------------------------------- pricing

        /// <summary>A flip-and-burn's days for <paramref name="km"/> at a working merchant's 0.3 g, and a day in port.</summary>
        public static double TravelDays(double km) => 2 * Math.Sqrt(Math.Max(0, km) / 0.003) / Astro.SecondsPerDay + 1;

        /// <summary>
        /// What a job pays, credits: its base (the goods' worth for freight, a fare a head for people, a fee for the
        /// rest), with a carriage premium for every AU from the board, doubled at full danger at the far end and
        /// raised by half again at full urgency.
        /// </summary>
        public static double Price(double basePay, double perAu, double km, double danger, double urgency) =>
            Math.Round((basePay + perAu * km / Astro.AuKm) * (1 + 2 * Clamp01(danger)) * (1 + 0.5 * Clamp01(urgency)) * 0.01) * 100;

        static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));

        /// <summary>How dangerous a body is now, 0..1: the war's tally of fights and raids there, fading over weeks.</summary>
        public double DangerAt(int body) => world.War == null || body < 0 ? 0 : 1 - Math.Exp(-world.War.Danger(body) / 3);

        double Km(int a, int b, double jd) => a == b || a < 0 || b < 0 ? 0 : (world.Ephemeris.HelioAt(a, jd) - world.Ephemeris.HelioAt(b, jd)).Length;

        // ---------------------------------------------------------------- posting

        /// <summary>Posts what the system needs today, kind by kind.</summary>
        public void Post(double jd)
        {
            PostFreight(jd);
            PostPeople(jd);
            PostEscorts(jd);
            PostPatrols(jd);
            PostSurveys(jd);
            PostBounties(jd);
        }

        /// <summary>Tonnes of a good already on contract to a settlement and not yet delivered by the player (an NPC's
        /// contract cargo is in the market's inbound book already).</summary>
        double OnContract(Settlement to, Good g)
        {
            double t = 0;
            foreach (var c in All)
                if (c.Kind == ContractKind.Freight && c.Destination == to && c.Good == g && (c.IsOpen || (c.IsActive && c.ByPlayer)))
                    t += Math.Max(0, c.Tonnes - c.Delivered);
            return t;
        }

        bool OpenFreight(Settlement to, Good g)
        {
            foreach (var c in All)
                if (c.Kind == ContractKind.Freight && c.IsOpen && c.Destination == to && c.Good == g) return true;
            return false;
        }

        /// <summary>
        /// Every lawful market short of a good it uses (its stock and what is on the way below <see cref="ShortCover"/>
        /// of what it wants) posts freight for it at the nearest port with it to spare: a bulk lot for a merchant's hold
        /// and a parcel for a pilot's, together no more than it takes to bring it back to <see cref="RestoreCover"/>.
        /// The shorter it is, the more urgent, the better it pays and the sooner it is due.
        /// </summary>
        void PostFreight(double jd)
        {
            foreach (var to in Economy.Settlements)
            {
                if (!Lawful(to)) continue;
                for (int i = 0; i < GoodInfo.Count; i++)
                {
                    var g = (Good)i;
                    double target = to.Target(g);
                    if (to.Demand[i] <= 0 || target <= 0) continue;
                    double expected = to.Stock[i] + to.Inbound[i];
                    double cover = expected / target;
                    if (cover >= ShortCover || OpenFreight(to, g)) continue;
                    double want = target * RestoreCover - expected - OnContract(to, g);
                    double crate = CargoFootprint.Of(g).Tonnes;
                    if (want < crate * ParcelCrates) continue;
                    var from = Supplier(to, g, Math.Min(want, crate * ParcelCrates), jd);
                    if (from == null || OpenAt(from) >= BoardCap) continue;
                    double urgency = 1 - cover / ShortCover;
                    double bulk = Math.Min(BulkLotTonnes, want - crate * ParcelCrates);
                    if (bulk >= crate * ParcelCrates * 2 && from.Available(g) >= bulk)
                        PostFreightLot(from, to, g, Math.Floor(bulk / crate) * crate, cover, urgency, jd);
                    PostFreightLot(from, to, g, crate * ParcelCrates, cover, urgency, jd);
                }
            }
        }

        void PostFreightLot(Settlement from, Settlement to, Good g, double tonnes, double cover, double urgency, double jd)
        {
            double km = Km(from.Body, to.Body, jd);
            double danger = DangerAt(to.Body);
            // The goods at a fair price, and the carriage: a fee a run and so much a tonne for every AU.
            double goods = tonnes * GoodInfo.Of(g).BasePrice;
            double pay = Price(goods + 20000 + goods * 0.15, 30000 + goods * 0.1, km, danger, urgency);
            if (to.Owner.Treasury < pay * 2) return;
            Add(new Contract
            {
                Kind = ContractKind.Freight, Board = from, Patron = to.Owner, Destination = to, Body = to.Body, Good = g, Tonnes = tonnes,
                CoverAtPost = cover, Pay = pay, DistanceKm = km, Danger = danger, Urgency = urgency,
                DeadlineDays = Math.Round(Math.Max(5, TravelDays(km) * 3 * (1 - 0.4 * urgency))),
            }, jd);
        }

        /// <summary>The nearest lawful port (on another body) that will sell <paramref name="tonnes"/> of a good, or null.</summary>
        Settlement Supplier(Settlement to, Good g, double tonnes, double jd)
        {
            Settlement best = null;
            double bestKm = double.MaxValue;
            foreach (var s in Economy.Settlements)
            {
                if (s == to || s.Body == to.Body || !Lawful(s) || s.Available(g) < tonnes) continue;
                if (world.Diplomacy.Hostile(s.Owner, to.Owner)) continue;
                double km = Km(s.Body, to.Body, jd);
                if (km < bestKm)
                {
                    bestKm = km;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>
        /// Where people are leaving (<see cref="Settlement.NetMigration"/> below zero) they want passage to where people
        /// are going: a small party at a time on their own account, and, from a place losing more than
        /// <see cref="ColonistOutflow"/> a day, a colony party the destination's owner pays to bring in.
        /// </summary>
        void PostPeople(double jd)
        {
            foreach (var from in Economy.Settlements)
            {
                if (!Lawful(from) || from.NetMigration >= -0.5 || OpenAt(from) >= BoardCap) continue;
                double outflow = -from.NetMigration;
                Settlement best = null;
                foreach (var to in Economy.Settlements)
                {
                    if (to == from || to.Body == from.Body || !Lawful(to) || to.NetMigration <= 0 || !People.CanMigrate(world.Diplomacy, from, to)) continue;
                    if (best == null || to.NetMigration > best.NetMigration) best = to;
                }
                if (best == null) continue;
                double urgency = Clamp01(outflow / (ColonistOutflow * 5));
                if (!HasOpen(ContractKind.Passengers, from, best))
                {
                    int people = (int)Math.Max(4, Math.Min(60, Math.Round(outflow * 3)));
                    PostPeopleLot(ContractKind.Passengers, from, best, people, from.Owner, urgency, jd);
                }
                if (outflow >= ColonistOutflow && !HasOpen(ContractKind.Colonists, from, best))
                {
                    int people = (int)Math.Max(100, Math.Min(4000, Math.Round(outflow * 20 / 10) * 10));
                    PostPeopleLot(ContractKind.Colonists, from, best, people, best.Owner, urgency, jd);
                }
            }
        }

        bool HasOpen(ContractKind kind, Settlement board, Settlement to)
        {
            foreach (var c in All)
                if (c.Kind == kind && c.Board == board && c.Destination == to && (c.IsOpen || c.IsActive)) return true;
            return false;
        }

        void PostPeopleLot(ContractKind kind, Settlement from, Settlement to, int people, Faction patron, double urgency, double jd)
        {
            // Passage is how migrants travel, not more of them: never more than a sliver, nor below six tenths of the founding people.
            if (people > from.Population * 0.05 || from.Population - people < from.InitialPopulation * MinPeopleShare) return;
            double km = Km(from.Body, to.Body, jd);
            double danger = DangerAt(to.Body);
            double fare = kind == ContractKind.Passengers ? 2500 : 150;
            double perAu = kind == ContractKind.Passengers ? 3000 : 200;
            double pay = Price(people * fare, people * perAu, km, danger, urgency);
            if (patron.Treasury < pay * 2) return;
            Add(new Contract
            {
                Kind = kind, Board = from, Patron = patron, Destination = to, Body = to.Body, People = people,
                Pay = pay, DistanceKm = km, Danger = danger, Urgency = urgency,
                DeadlineDays = Math.Round(Math.Max(6, TravelDays(km) * 3)),
            }, jd);
        }

        /// <summary>
        /// A merchant under way with a cargo worth <see cref="EscortCargoValue"/> into a port as dangerous as
        /// <see cref="EscortDanger"/> wants an escort there to see it in: posted on the board it sailed from, paid by
        /// its owners, open until it gets there.
        /// </summary>
        void PostEscorts(double jd)
        {
            foreach (var s in world.Fleet.Ships)
            {
                if (s.Destroyed || s.IsEscort || !world.IsTrader(s) || s == world.PilotedShip || !s.HasCargo || s.Phase != ShipPhase.Transit) continue;
                if (s.Faction == null || s.Faction.IsPirate || s.DestinationBody < 0 || s.PhaseEndJd - jd < 0.5) continue;
                double value = s.CargoTonnes * GoodInfo.Of(s.CargoGood).BasePrice;
                double danger = DangerAt(s.DestinationBody);
                if (value < EscortCargoValue || danger < EscortDanger || HasEscort(s)) continue;
                Settlement board = null;
                foreach (var st in Economy.At(s.OriginBody))
                    if (Lawful(st) && (board == null || st.Population > board.Population)) board = st;
                if (board == null || OpenAt(board) >= BoardCap) continue;
                double km = Km(s.OriginBody, s.DestinationBody, jd);
                double pay = Price(60000 + value * 0.02, 40000, km, danger, Clamp01(1 - (s.PhaseEndJd - jd) / 10));
                if (s.Faction.Treasury < pay * 2) continue;
                Add(new Contract
                {
                    Kind = ContractKind.Escort, Board = board, Patron = s.Faction, Client = s, Body = s.DestinationBody,
                    Pay = pay, DistanceKm = km, Danger = danger, Urgency = Clamp01(1 - (s.PhaseEndJd - jd) / 10),
                    ExpiresJd = s.PhaseEndJd, DeadlineDays = s.PhaseEndJd - jd + EscortWindowDays,
                }, jd);
            }
        }

        bool HasEscort(Ship client)
        {
            foreach (var c in All)
                if (c.Kind == ContractKind.Escort && c.Client == client && (c.IsOpen || c.IsActive)) return true;
            return false;
        }

        /// <summary>A lawful settlement that has seen fighting or raiders lately (danger past <see cref="PatrolDanger"/>) posts a patrol over itself.</summary>
        void PostPatrols(double jd)
        {
            foreach (var s in Economy.Settlements)
            {
                if (!Lawful(s) || OpenAt(s) >= BoardCap) continue;
                double danger = DangerAt(s.Body);
                if (danger < PatrolDanger) continue;
                bool posted = false;
                foreach (var c in All)
                    if (c.Kind == ContractKind.Patrol && c.Body == s.Body && (c.IsOpen || c.IsActive)) posted = true;
                if (posted) continue;
                double pay = Price(120000, 0, 0, danger, danger);
                if (s.Owner.Treasury < pay * 2) continue;
                Add(new Contract
                {
                    Kind = ContractKind.Patrol, Board = s, Patron = s.Owner, Body = s.Body, PatrolDays = PatrolStationDays,
                    Pay = pay, Danger = danger, Urgency = danger, DeadlineDays = 12,
                }, jd);
            }
        }

        /// <summary>Each lawful power's biggest port posts surveys of the two nearest bodies no ship has been to.</summary>
        void PostSurveys(double jd)
        {
            var capitals = new Dictionary<Faction, Settlement>();
            foreach (var s in Economy.Settlements)
                if (Lawful(s) && (!capitals.TryGetValue(s.Owner, out var c) || s.Population > c.Population)) capitals[s.Owner] = s;
            var hidden = new HashSet<int>();
            foreach (var s in Economy.Settlements)
                if (s.Hidden) hidden.Add(s.Body);
            foreach (var board in capitals.Values)
            {
                int open = 0;
                foreach (var c in All)
                    if (c.Kind == ContractKind.Survey && c.Board == board && c.IsOpen) open++;
                if (open >= 2 || OpenAt(board) >= BoardCap) continue;
                var candidates = new List<(int body, double km)>();
                foreach (var b in world.Catalog.Bodies)
                {
                    if (!b.IsVisible || b.IsStar || b.Index == world.Catalog.Root.Index || hidden.Contains(b.Index) || Visited.ContainsKey(b.Index)) continue;
                    if (SurveyPosted(b.Index)) continue;
                    if (world.Fleet.Lanes != null && world.Fleet.Lanes.SystemOf(b.Index) < 0) continue;
                    candidates.Add((b.Index, Km(board.Body, b.Index, jd)));
                }
                candidates.Sort((a, b) => a.km.CompareTo(b.km));
                for (int i = 0; i < candidates.Count && open < 2; i++, open++)
                {
                    var (body, km) = candidates[i];
                    double danger = DangerAt(body);
                    double pay = Price(50000, 40000, km, danger, 0);
                    if (board.Owner.Treasury < pay * 2) break;
                    Add(new Contract
                    {
                        Kind = ContractKind.Survey, Board = board, Patron = board.Owner, Body = body, Pay = pay, DistanceKm = km, Danger = danger,
                        ExpiresJd = jd + SurveyOfferDays, DeadlineDays = Math.Round(Math.Max(10, TravelDays(km) * 3)),
                    }, jd);
                }
            }
        }

        bool SurveyPosted(int body)
        {
            foreach (var c in All)
                if (c.Kind == ContractKind.Survey && c.Body == body && (c.IsOpen || c.IsActive)) return true;
            return false;
        }

        /// <summary>Every price on a head worth a hunter's while (<see cref="Outlaws.MinContract"/>) goes up as a bounty
        /// contract at the biggest port of whoever posted the most of it, paying the whole price.</summary>
        void PostBounties(double jd)
        {
            if (world.Outlaws == null) return;
            foreach (var p in world.Outlaws.Postings())
            {
                if (p.IsPlayer || p.TotalBounty < Outlaws.MinContract) continue;
                Contract existing = null;
                foreach (var c in All)
                    if (c.Kind == ContractKind.Bounty && c.Target == p.Target && (c.IsOpen || c.IsActive)) existing = c;
                if (existing != null)
                {
                    existing.Pay = p.TotalBounty;
                    continue;
                }
                Faction patron = null;
                foreach (var kv in p.Target.Bounty)
                    if (patron == null || kv.Value > p.Target.BountyBy(patron)) patron = kv.Key;
                Settlement board = null;
                foreach (var s in Economy.Settlements)
                    if (Lawful(s) && s.Owner == patron && (board == null || s.Population > board.Population)) board = s;
                if (board == null) continue;
                int body = p.LastSeen?.Body ?? -1;
                Add(new Contract
                {
                    Kind = ContractKind.Bounty, Board = board, Patron = patron, Target = p.Target, Body = body,
                    Pay = p.TotalBounty, Danger = 1, Urgency = p.Outlaw != null && p.Outlaw.Feared ? 1 : 0.5,
                    DistanceKm = Km(board.Body, body, jd), ExpiresJd = jd + Outlaws.ContractDays, DeadlineDays = Outlaws.ContractDays,
                }, jd);
            }
        }

        // ---------------------------------------------------------------- taking

        public enum TakeOutcome
        {
            Ok,
            NotOpen,

            /// <summary>The patron offers this taker nothing (wanted by it, or standing below <see cref="Law.ContractMinStanding"/>).</summary>
            NotOffered,

            /// <summary>Not alongside the board's port.</summary>
            NotHere,

            /// <summary>No room for the people (or no hold for the freight).</summary>
            NoRoom,

            /// <summary>An escort, patrol or bounty wants guns.</summary>
            Unarmed,

            /// <summary>Freight: the port has not the goods to sell, or the taker cannot pay for them.</summary>
            NoGoods,
        }

        /// <summary>People a hull can carry: berths on a courier, a colony deck in a freighter, ten to a cell of a pilot's hold.</summary>
        public static int Berths(Ship ship, bool player)
        {
            if (ship == null) return 0;
            if (player) return ship.PersonalHold.Width * ship.PersonalHold.Height * 10;
            switch (ship.Spec.Class)
            {
                case ShipClass.Courier: return 60;
                case ShipClass.Freighter: return 5000;
                default: return 0;
            }
        }

        LawRecord RecordFor(Ship ship, bool player) => player ? Law?.Player : Law?.RecordOf(ship.Controller);

        /// <summary>Whether <paramref name="ship"/> could take <paramref name="c"/> (alongside the board for people,
        /// with the hold, berths or guns it needs). The player takes jobs at the board of the port they are at.</summary>
        public TakeOutcome CanTake(Contract c, Ship ship, bool player, double jd)
        {
            if (c == null || !c.IsOpen) return TakeOutcome.NotOpen;
            if (ship == null || ship.Destroyed) return TakeOutcome.NotHere;
            var rec = player ? Law?.Player : Law?.FindRecord(ship.Controller);
            if (rec != null && !Law.OffersContracts(rec, c.Patron)) return TakeOutcome.NotOffered;
            if (player && !PlayerNear(ship, c.Board.Body, jd)) return TakeOutcome.NotHere;
            switch (c.Kind)
            {
                case ContractKind.Passengers:
                case ContractKind.Colonists:
                    if (!player && !At(ship, c.Board.Body)) return TakeOutcome.NotHere;
                    if (Berths(ship, player) < c.People) return TakeOutcome.NoRoom;
                    break;
                case ContractKind.Freight:
                    if (player) { if (ship.PersonalHold.Width * ship.PersonalHold.Height <= 0) return TakeOutcome.NoRoom; }
                    else
                    {
                        if (!At(ship, c.Board.Body)) return TakeOutcome.NotHere;
                        if (ship.HasCargo || !Economy.Carries(ship.Spec.Class, c.Good) || ship.CapacityTonnes < c.Tonnes) return TakeOutcome.NoRoom;
                        // A lot booked for a port its flag may not use (an embargo, a war) is none for this captain to take.
                        if (c.Destination != null && !Economy.CanTrade(ship, c.Destination)) return TakeOutcome.NotOffered;
                        if (c.Board.Available(c.Good) < c.Tonnes || ship.Faction.Treasury < c.Board.PurchaseCost(c.Good, c.Tonnes) * (1 + Economy.PortTax)) return TakeOutcome.NoGoods;
                    }
                    break;
                case ContractKind.Escort:
                case ContractKind.Patrol:
                case ContractKind.Bounty:
                    if (ship.Combat == null || !ship.Combat.Armed) return TakeOutcome.Unarmed;
                    break;
            }
            return TakeOutcome.Ok;
        }

        /// <summary>
        /// <paramref name="ship"/> takes <paramref name="c"/>: the clock starts, people board, and a merchant buys the
        /// lot at the board's port and books it for the destination. (The player buys a lot's crates at the market
        /// themselves, here or anywhere, and hands them over at the destination.)
        /// </summary>
        public TakeOutcome Take(Contract c, Ship ship, bool player, double jd)
        {
            var outcome = CanTake(c, ship, player, jd);
            if (outcome != TakeOutcome.Ok) return outcome;
            var taker = player ? ship : ship.Controller;
            c.State = ContractState.Taken;
            c.Taker = taker;
            c.ByPlayer = player;
            c.TakerRecord = RecordFor(ship, player);
            c.TakerFaction = taker.Faction;
            c.TakenJd = jd;
            c.DueJd = jd + c.DeadlineDays;
            switch (c.Kind)
            {
                case ContractKind.Passengers:
                case ContractKind.Colonists:
                    c.Board.Population -= c.People;
                    c.Board.Emigrated += c.People;
                    c.Aboard = true;
                    if (!player) taker.Cargo = c.Title;
                    break;
                case ContractKind.Freight:
                    if (!player)
                    {
                        Economy.LoadFor(taker, c.Board, c.Good, c.Tonnes, c.Destination);
                        taker.Cargo += " (contract)";
                    }
                    break;
            }
            string who = player ? "You take" : "The " + taker.Name + " takes";
            world.Log(jd, $"{who} a contract at {c.Board.Name}: {c.Title} for {Cr(c.Pay)} cr, due in {c.DeadlineDays:0} d", WorldEventKind.Info, c.Board.Body);
            return TakeOutcome.Ok;
        }

        /// <summary>The player gives up <paramref name="c"/>: no pay, standing with the patron falls, and anyone aboard is put ashore where they boarded.</summary>
        public bool Abandon(Contract c, double jd)
        {
            if (c == null || !c.IsActive) return false;
            Close(c, ContractState.Abandoned, jd, "given up");
            return true;
        }

        // ---------------------------------------------------------------- NPC captains

        /// <summary>
        /// Fleet hook for merchants, couriers and free ships (before their usual choice): a ship working a contract
        /// goes where it takes it; an idle one in port now and then looks at the boards here and takes the job that
        /// pays best for the days it costs -- freight it can carry and pay for, people it has berths for, a survey for
        /// a free ship or a courier. Returns the body to go to, or -1 to go about its usual business.
        /// </summary>
        public int ChooseNext(Ship ship, int here, double jd)
        {
            if (ship == null || ship == world.PilotedShip || ship.IsEscort) return -1;
            var active = ActiveFor(ship);
            if (active != null) return active.Body >= 0 && active.Body != here ? active.Body : -1;
            if (ship.HasCargo || ship.Faction == null || ship.Faction.IsPirate || rng.NextDouble() > NpcTakeChance) return -1;
            Contract best = null;
            double bestScore = 0;
            foreach (var st in Economy.At(here))
            {
                if (!Lawful(st)) continue;
                foreach (var c in All)
                {
                    if (!c.IsOpen || c.Board != st || !Suits(ship, c) || CanTake(c, ship, false, jd) != TakeOutcome.Ok) continue;
                    double days = c.Body == here ? 1 : world.Fleet.EstimateTravelDays(ship, here, c.Body, jd) + 1;
                    if (days > c.DeadlineDays) continue;
                    double cost = c.Kind == ContractKind.Freight ? c.Board.PurchaseCost(c.Good, c.Tonnes) : 0;
                    double score = (c.Pay - cost) / days;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
            }
            if (best == null || Take(best, ship, false, jd) != TakeOutcome.Ok) return -1;
            return best.Body;
        }

        /// <summary>Which NPC hulls work which jobs: merchants freight and people, free ships and couriers surveys.</summary>
        static bool Suits(Ship ship, Contract c)
        {
            switch (c.Kind)
            {
                case ContractKind.Freight: return ship.Role == ShipRole.Trader || ship.Role == ShipRole.Miner || ship.Role == ShipRole.Courier;
                case ContractKind.Passengers: return ship.Role == ShipRole.Courier || ship.Role == ShipRole.Trader;
                case ContractKind.Colonists: return ship.Role == ShipRole.Trader;
                case ContractKind.Survey: return ship.Role == ShipRole.Free || ship.Role == ShipRole.Courier;
                default: return false;
            }
        }

        /// <summary>
        /// Through <see cref="Outlaws.ChooseNext"/>: a bounty hunter group with no bounty to chase works the guns'
        /// jobs on the boards -- a patrol over a troubled port, an escort into one -- taking the best paid it can
        /// reach in time, and goes where it takes it. Returns the body, or -1.
        /// </summary>
        public int ChooseForHunter(Ship leader, int here, double jd)
        {
            var active = ActiveFor(leader);
            if (active != null) return active.Kind == ContractKind.Bounty ? -1 : active.Body;
            Contract best = null;
            double bestScore = 0;
            foreach (var c in All)
            {
                if (!c.IsOpen || (c.Kind != ContractKind.Patrol && c.Kind != ContractKind.Escort) || CanTake(c, leader, false, jd) != TakeOutcome.Ok) continue;
                double days = c.Body == here ? 0.5 : world.Fleet.EstimateTravelDays(leader, here, c.Body, jd);
                double window = c.Kind == ContractKind.Escort ? c.ExpiresJd - jd + EscortWindowDays : c.DeadlineDays - c.PatrolDays;
                if (days > window) continue;
                if (world.War != null && world.War.DefenceAt(c.Body, leader.Faction) > world.War.GroupStrength(leader) * 1.2) continue;
                double score = c.Pay / (days + 1);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            if (best == null || Take(best, leader, false, jd) != TakeOutcome.Ok) return -1;
            return best.Body;
        }

        /// <summary>
        /// Fleet hook (from <see cref="World"/>, before the market sees the ship): a ship has arrived at
        /// <paramref name="body"/>. The body is visited; contract freight and people for here are delivered, a survey
        /// of it is done, an escorted client is in. True when the ship carries contract cargo the market here must
        /// not buy (delivered, or bound elsewhere).
        /// </summary>
        public bool OnArrived(Ship ship, int body, double jd)
        {
            if (ship == null) return false;
            Visited[body] = jd;
            bool holding = false;
            foreach (var c in new List<Contract>(All))
            {
                if (c.Kind == ContractKind.Escort && c.Client == ship && double.IsNaN(c.ClientArrivedJd) && body == c.Body)
                {
                    c.ClientArrivedJd = jd;
                    if (c.IsActive && TakerAt(c, body, jd)) Close(c, ContractState.Completed, jd, "the " + ship.Name + " is in safe");
                    continue;
                }
                // Bound for a place since abandoned: nothing to deliver to (the day's review calls it off).
                if (!c.IsActive || c.ByPlayer || (c.Taker != ship && c.Taker != ship.Controller) || DestinationGone(c)) continue;
                switch (c.Kind)
                {
                    case ContractKind.Freight:
                        if (!CarriesLot(c)) break;
                        if (body == c.Body) DeliverNpcFreight(c, jd);
                        holding = true;
                        break;
                    case ContractKind.Passengers:
                    case ContractKind.Colonists:
                        if (body == c.Body) Close(c, ContractState.Completed, jd, "delivered");
                        break;
                    case ContractKind.Survey:
                        if (body == c.Body) Close(c, ContractState.Completed, jd, "surveyed");
                        break;
                    case ContractKind.Escort:
                        if (body == c.Body && !double.IsNaN(c.ClientArrivedJd) && jd <= c.ClientArrivedJd + EscortWindowDays)
                            Close(c, ContractState.Completed, jd, "the " + c.Client.Name + " seen in");
                        break;
                }
            }
            return holding;
        }

        /// <summary>Whether a merchant on a freight contract still has its lot aboard, booked for the destination.</summary>
        static bool CarriesLot(Contract c)
        {
            var s = c.Taker;
            return s != null && !s.Destroyed && s.HasCargo && s.CargoGood == c.Good && s.CargoDestination == c.Destination;
        }

        void DeliverNpcFreight(Contract c, double jd)
        {
            var ship = c.Taker;
            Economy.Release(ship);
            c.Destination.Stock[(int)c.Good] += ship.CargoTonnes;
            c.Delivered = ship.CargoTonnes;
            c.Destination.RefreshPrices();
            ship.HasCargo = false;
            ship.CargoTonnes = 0;
            // What the lot cost comes off its earnings (as a sale's would), the pay going on below: its profit, not its takings.
            ship.Credits -= ship.CargoCost;
            ship.CargoCost = 0;
            ship.CargoBonus = 0;
            ship.CargoDestination = null;
            ship.Cargo = null;
            Close(c, ContractState.Completed, jd, "delivered");
        }

        // ---------------------------------------------------------------- the player at the helm

        /// <summary>
        /// The player's ship (<paramref name="ship"/>) flown at <paramref name="offsetKm"/> from <paramref name="body"/>:
        /// close in, the body is visited, a survey of it is done, freight crates for a port here are handed over (and
        /// the job paid once the whole lot is in), passengers put ashore, and a patrol here counts its time on station.
        /// </summary>
        public void UpdatePilot(Ship ship, int body, DVec3 offsetKm, double jd)
        {
            if (ship == null || ship.Destroyed || body < 0) return;
            double last = pilotJd;
            pilotBody = body;
            pilotOffset = offsetKm;
            pilotJd = jd;
            if (!PlayerNear(ship, body, jd)) return;
            Visited[body] = jd;
            foreach (var c in new List<Contract>(All))
            {
                if (!c.IsActive || !c.ByPlayer || c.Body != body || DestinationGone(c)) continue;
                switch (c.Kind)
                {
                    case ContractKind.Survey:
                        Close(c, ContractState.Completed, jd, "surveyed");
                        break;
                    case ContractKind.Passengers:
                    case ContractKind.Colonists:
                        if (c.Taker == ship) Close(c, ContractState.Completed, jd, "put ashore");
                        break;
                    case ContractKind.Freight:
                        HandOver(c, ship, jd);
                        break;
                    case ContractKind.Patrol:
                        if (!double.IsNaN(last) && jd > last) c.OnStation += Math.Min(jd - last, 0.1);
                        if (c.OnStation >= c.PatrolDays) Close(c, ContractState.Completed, jd, "patrol flown");
                        break;
                    case ContractKind.Escort:
                        if (!double.IsNaN(c.ClientArrivedJd) && jd <= c.ClientArrivedJd + EscortWindowDays)
                            Close(c, ContractState.Completed, jd, "the " + c.Client.Name + " seen in");
                        break;
                }
            }
        }

        /// <summary>Hands over the crates of a freight contract's good (not stolen ones) from the player's hold at its destination.</summary>
        public double HandOver(Contract c, Ship ship, double jd)
        {
            if (c == null || !c.IsActive || c.Kind != ContractKind.Freight || ship == null || DestinationGone(c)) return 0;
            double handed = 0;
            var hold = ship.PersonalHold;
            foreach (var item in new List<CargoItem>(hold.Items))
            {
                if (c.Delivered >= c.Tonnes - 1e-6) break;
                if (item.Good != c.Good || item.Stolen) continue;
                hold.Remove(item);
                c.Delivered += item.Tonnes;
                handed += item.Tonnes;
                c.Destination.Stock[(int)c.Good] += item.Tonnes;
            }
            if (handed <= 0) return 0;
            c.Destination.RefreshPrices();
            if (c.Delivered >= c.Tonnes - 1e-6) Close(c, ContractState.Completed, jd, "delivered");
            else Law?.Notice($"Handed over {handed:N0} t {GoodInfo.Of(c.Good).Name} at {c.Destination.Name}: {c.Delivered:N0} of {c.Tonnes:N0} t");
            return handed;
        }

        /// <summary>Whether the player's ship is close in at <paramref name="body"/> (within its neighbourhood) as last flown.</summary>
        public bool PlayerNear(Ship ship, int body, double jd) =>
            ship != null && ship == world.PilotedShip && pilotBody == body && !double.IsNaN(pilotJd) && Math.Abs(jd - pilotJd) < 0.25 &&
            body >= 0 && body < world.Catalog.Count && pilotOffset.Length <= ScaleModel.NeighbourhoodKm(world.Catalog[body]);

        /// <summary>Whether an NPC hull (its group) is in port or parked at <paramref name="body"/>.</summary>
        static bool At(Ship ship, int body)
        {
            var c = ship.Controller;
            if (c.Destroyed || c.Orbit == null || c.Orbit.Body != body) return false;
            return c.Phase == ShipPhase.Parked || c.Phase == ShipPhase.Queued || c.Planetside;
        }

        bool TakerAt(Contract c, int body, double jd) => c.ByPlayer ? PlayerNear(c.Taker, body, jd) || (c.Taker != world.PilotedShip && At(c.Taker, body)) : At(c.Taker, body);

        // ---------------------------------------------------------------- review

        /// <summary>
        /// Once a day: offers past their time, or whose need has gone, come down; taken contracts whose deadline has
        /// passed, whose taker was lost or taken, whose cargo went elsewhere or whose client died, fail; a patrol on
        /// station counts its day; a bounty whose target is dead or taken is done (by the hunter that took it) or void.
        /// </summary>
        public void Review(double jd)
        {
            foreach (var c in new List<Contract>(All))
            {
                if (c.IsOpen)
                {
                    if (jd >= c.ExpiresJd || !StillNeeded(c)) Close(c, ContractState.Expired, jd, null);
                    else if (c.Kind == ContractKind.Bounty) MirrorHunter(c, jd);
                    continue;
                }
                if (!c.IsActive) continue;
                var t = c.Taker;
                // Taken as a prize (by pirates, or a navy at war with its flag), the hull is its captor's, not the taker's.
                bool lost = t == null || t.Destroyed || t.Derelict || t.GateDeparted || (c.TakerRecord != null && !c.ByPlayer && t.Faction != null && t.Faction.IsPirate)
                            || (!c.ByPlayer && c.TakerFaction != null && t.Faction != c.TakerFaction);
                if (c.ByPlayer && lost && t != null && !t.Destroyed && !t.Derelict) lost = false;
                if (lost)
                {
                    Close(c, ContractState.Failed, jd, t != null && t.Destroyed ? "the " + t.Name + " was lost" : "the taker is gone");
                    continue;
                }
                // Its destination was abandoned to ruin after it was taken: nobody is left there to take the goods or
                // the people. Called off, the taker not to blame; a merchant's lot goes back to the open market.
                if (DestinationGone(c))
                {
                    if (!c.ByPlayer && c.Kind == ContractKind.Freight && CarriesLot(c))
                    {
                        Economy.Release(t);
                        t.CargoDestination = null;
                    }
                    Close(c, ContractState.Failed, jd, c.Destination.Name + " is no more", blame: false);
                    continue;
                }
                switch (c.Kind)
                {
                    case ContractKind.Freight:
                        if (!c.ByPlayer && !CarriesLot(c)) { Close(c, ContractState.Failed, jd, "the cargo went elsewhere"); continue; }
                        break;
                    case ContractKind.Escort:
                        if (c.Client == null || c.Client.Destroyed || c.Client.Faction != c.Patron) { Close(c, ContractState.Failed, jd, "the " + c.Client?.Name + " was lost"); continue; }
                        if (!double.IsNaN(c.ClientArrivedJd))
                        {
                            if (TakerAt(c, c.Body, jd)) { Close(c, ContractState.Completed, jd, "the " + c.Client.Name + " seen in"); continue; }
                            if (jd > c.ClientArrivedJd + EscortWindowDays) { Close(c, ContractState.Failed, jd, "not there when the " + c.Client.Name + " came in"); continue; }
                        }
                        break;
                    case ContractKind.Patrol:
                        if (!c.ByPlayer && At(c.Taker, c.Body)) c.OnStation += 1;
                        if (c.OnStation >= c.PatrolDays) { Close(c, ContractState.Completed, jd, "patrol flown"); continue; }
                        break;
                    case ContractKind.Bounty:
                        if (ReviewBounty(c, jd)) continue;
                        break;
                }
                if (jd > c.DueJd) Close(c, ContractState.Failed, jd, "overdue");
            }
            if (All.Count > 0) Trim();
        }

        /// <summary>Whether what an open offer was posted for still stands.</summary>
        bool StillNeeded(Contract c)
        {
            if (!Lawful(c.Board)) return false;
            switch (c.Kind)
            {
                case ContractKind.Freight:
                {
                    var to = c.Destination;
                    if (!Lawful(to)) return false;
                    double target = to.Target(c.Good);
                    return target > 0 && (to.Stock[(int)c.Good] + to.Inbound[(int)c.Good]) / target < RestoreCover;
                }
                case ContractKind.Passengers:
                case ContractKind.Colonists:
                    return Lawful(c.Destination) && c.Board.Population - c.People >= c.Board.InitialPopulation * MinPeopleShare;
                case ContractKind.Escort:
                    return c.Client != null && !c.Client.Destroyed && double.IsNaN(c.ClientArrivedJd) && c.Client.Faction == c.Patron;
                case ContractKind.Patrol:
                    return DangerAt(c.Body) >= PatrolDanger * 0.5;
                case ContractKind.Survey:
                    return !Visited.ContainsKey(c.Body);
                case ContractKind.Bounty:
                {
                    var o = world.Outlaws?.Of(c.Target);
                    return c.Target.Wanted && (o == null || o.AtLarge);
                }
                default: return true;
            }
        }

        /// <summary>A hunter that has taken the contract on a bounty's target (in <see cref="Outlaws"/>) has taken this one too.</summary>
        void MirrorHunter(Contract c, double jd)
        {
            var h = world.Outlaws?.HunterOn(c.Target);
            if (h == null) return;
            c.State = ContractState.Taken;
            c.Taker = h.Ship;
            c.TakerRecord = Law?.RecordOf(h.Ship);
            c.TakenJd = jd;
            c.DueJd = h.Contract != null ? h.Contract.UntilJd : jd + Outlaws.ContractDays;
        }

        /// <summary>A taken bounty: done once the target is killed or taken (by its taker: a hunter's take is paid by the
        /// law's books, the player's by the contract), void if someone else got there first, given up if a hunter dropped it.</summary>
        bool ReviewBounty(Contract c, double jd)
        {
            var o = world.Outlaws?.Of(c.Target);
            bool ended = o != null ? !o.AtLarge : !c.Target.Wanted;
            if (!c.ByPlayer)
            {
                var h = world.Outlaws?.HunterOf(c.Taker);
                if (ended)
                {
                    bool theirs = o != null && h != null && o.FateBy != null && o.FateBy.Contains(h.Name);
                    Close(c, theirs ? ContractState.Completed : ContractState.Expired, jd, theirs ? "collected" : null, pay: false);
                    return true;
                }
                if (h == null || h.Contract == null || h.Contract.Target != c.Target)
                {
                    Close(c, ContractState.Abandoned, jd, "dropped");
                    return true;
                }
                return false;
            }
            if (ended)
            {
                // The player's own kill is closed by OnBattleEnded; here only someone else's.
                Close(c, ContractState.Expired, jd, null);
                return true;
            }
            return false;
        }

        /// <summary>
        /// A battle has ended: a bounty target the player took a contract on, killed or taken with the player's hull on
        /// the other side, completes it (the patron pays the contract's price).
        /// </summary>
        public void OnBattleEnded(Battle b, double jd)
        {
            if (b == null) return;
            foreach (var c in new List<Contract>(All))
            {
                if (!c.IsActive || !c.ByPlayer || c.Kind != ContractKind.Bounty) continue;
                foreach (var u in b.Units)
                {
                    if (u.Ship == null || !(u.Dead || u.Captured) || u.Ship.IsEscort) continue;
                    if (Law?.FindRecord(u.Ship) != c.Target) continue;
                    bool playerOpposite = false;
                    foreach (var v in b.Units)
                        if (v.Side == 1 - u.FoughtOn && v.PlayerFlown) playerOpposite = true;
                    // A captain who got away in the pods is still at large: the contract stands.
                    var outlaw = world.Outlaws?.Of(c.Target);
                    if (playerOpposite && (outlaw == null || !outlaw.AtLarge))
                        // The bounty itself is the law's to pay (a captain in the brig handed over, a kill on a scanned
                        // wreck): the patron does not pay it a second time.
                        Close(c, ContractState.Completed, jd, u.Dead ? "killed" : "taken", pay: false);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- closing

        /// <summary>
        /// Ends a contract. Completed: the patron pays (unless the law's books already did) and the taker's standing
        /// with it rises. Failed or abandoned: no pay, standing falls, people still aboard go ashore where they boarded
        /// (or die with a lost hull), and NPC contract freight is the taker's to sell.
        /// </summary>
        /// <summary>A freight or passage contract whose destination has fallen (abandoned, a ruin): it cannot be delivered.</summary>
        static bool DestinationGone(Contract c) =>
            (c.Kind == ContractKind.Freight || c.Kind == ContractKind.Passengers || c.Kind == ContractKind.Colonists) &&
            c.Destination != null && c.Destination.Fallen;

        void Close(Contract c, ContractState state, double jd, string why, bool pay = true, bool blame = true)
        {
            var was = c.State;
            c.State = state;
            c.ClosedJd = jd;
            if (was == ContractState.Open) return;
            var rec = c.TakerRecord;
            string who = c.ByPlayer ? "you" : "the " + (c.Taker?.Name ?? "taker");
            if (state == ContractState.Completed)
            {
                if (c.Aboard)
                {
                    c.Destination.Population += c.People;
                    c.Aboard = false;
                }
                if (pay && c.Pay > 0)
                {
                    c.Patron.Treasury -= c.Pay;
                    if (c.Taker != null)
                    {
                        c.Taker.Faction.Treasury += c.Pay;
                        if (!c.ByPlayer) c.Taker.Credits += c.Pay;
                    }
                }
                Standing(rec, c.Patron, CompleteStanding + UrgentStanding * c.Urgency);
                if (!c.ByPlayer && c.Taker != null && (c.Kind == ContractKind.Passengers || c.Kind == ContractKind.Colonists)) c.Taker.Cargo = null;
                world.Log(jd, $"Contract done: {c.Title} ({why}) by {who}" + (pay ? $", {Cr(c.Pay)} cr from {c.Patron.Name}" : ""), WorldEventKind.Info, c.Body);
                if (c.ByPlayer) Law?.Notice($"Contract complete: {c.Title}" + (pay ? $": {c.Patron.Name} pays {Cr(c.Pay)} cr" : ""));
                return;
            }
            if (state == ContractState.Expired) return;
            if (c.Aboard)
            {
                // A hull gone through a gate or sent to the breakers is marked destroyed too, but nobody died with it:
                // its people go back ashore where they boarded rather than vanishing.
                bool lostWithHull = c.Taker == null || (c.Taker.Destroyed && !c.Taker.GateDeparted && !c.Taker.Scrapped);
                if (!lostWithHull)
                {
                    c.Board.Population += c.People;
                    c.Board.Emigrated = Math.Max(0, c.Board.Emigrated - c.People);
                }
                c.Aboard = false;
                if (lostWithHull) world.Log(jd, $"{c.People:N0} {(c.Kind == ContractKind.Colonists ? "colonists" : "passengers")} bound for {c.Destination.Name} are lost with the {c.Taker?.Name}", WorldEventKind.Loss, -1);
            }
            if (!c.ByPlayer && c.Taker != null && (c.Kind == ContractKind.Passengers || c.Kind == ContractKind.Colonists)) c.Taker.Cargo = null;
            string what = state == ContractState.Abandoned ? "abandoned" : "failed";
            if (!blame)
            {
                world.Log(jd, $"Contract called off: {c.Title} by {who} ({why})", WorldEventKind.Info, c.Body);
                if (c.ByPlayer) Law?.Notice($"Contract called off: {c.Title} ({why})");
                return;
            }
            Standing(rec, c.Patron, -(state == ContractState.Abandoned ? AbandonStanding : FailStanding));
            world.Log(jd, $"Contract {what}: {c.Title} by {who} ({why}); {c.Patron.Name} will remember it", WorldEventKind.Info, c.Body);
            if (c.ByPlayer) Law?.Notice($"Contract {what}: {c.Title} ({why}). Standing with {c.Patron.Name} falls");
        }

        static void Standing(LawRecord r, Faction f, double delta)
        {
            if (r == null || f == null || f.IsPirate || delta == 0) return;
            r.Standing[f] = r.StandingWith(f) + delta;
        }

        /// <summary>Forgets the oldest closed contracts past <see cref="HistoryCap"/>.</summary>
        void Trim()
        {
            int closed = 0;
            foreach (var c in All) if (c.IsClosed) closed++;
            for (int i = 0; i < All.Count && closed > HistoryCap; )
            {
                if (All[i].IsClosed)
                {
                    All.RemoveAt(i);
                    closed--;
                }
                else i++;
            }
        }

        /// <summary>A board line in words: the job, the pay, the deadline, and where the money comes from.</summary>
        public static string Describe(Contract c, double jd)
        {
            string text = c.Title + "  ·  " + Cr(c.Pay) + " cr  ·  " + c.Patron.Name;
            if (c.IsOpen) text += "  ·  " + c.DeadlineDays.ToString("0", CultureInfo.InvariantCulture) + " d to do it, offer ends in " + Math.Max(0, c.ExpiresJd - jd).ToString("0", CultureInfo.InvariantCulture) + " d";
            else if (c.IsActive) text += "  ·  due in " + Math.Max(0, c.DueJd - jd).ToString("0.#", CultureInfo.InvariantCulture) + " d";
            if (c.Kind == ContractKind.Freight && c.IsActive) text += "  ·  " + c.Delivered.ToString("N0", CultureInfo.InvariantCulture) + " t handed over";
            if (c.Kind == ContractKind.Patrol && c.IsActive) text += "  ·  " + c.OnStation.ToString("0.0", CultureInfo.InvariantCulture) + " d on station";
            if (c.Danger >= 0.3) text += "  ·  dangerous";
            if (c.Urgency >= 0.6) text += "  ·  urgent";
            return text;
        }

        static string Cr(double v) => v.ToString("N0", CultureInfo.InvariantCulture);
    }
}
