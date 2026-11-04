using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>How a report from another system got here.</summary>
    public enum NewsCarrier
    {
        /// <summary>Beamed to the gate, passed through it by the gates' own relay, and beamed in from the far gate.</summary>
        Relay,

        /// <summary>Carried by a ship through the gates (a courier with the mail, or any ship with its data cores).</summary>
        Ship,

        /// <summary>What a system knew of the others when the simulation began.</summary>
        Charts,

        /// <summary>Beamed relay to relay through open space by a power's deep-space relays (U.SPC-T421), where the gates
        /// cannot pass it.</summary>
        DeepSpaceRelay,
    }

    /// <summary>
    /// What one system's markets and news looked like when a report left it (U.SPC-T100): every market's board,
    /// copied as it stood (<see cref="Settlement.Snapshot"/>), and the notable things that had happened there since
    /// the last report. Nothing in it changes after it is taken, so by the time it reaches anyone it is as old as the
    /// trip that brought it, and the real markets may have moved a long way.
    /// </summary>
    public sealed class SystemReport
    {
        public StarSystem Source;

        /// <summary>When the report left the source's markets (the moment its boards were copied).</summary>
        public double AsOfJd;

        /// <summary>When it reached the system it is held in.</summary>
        public double ReceivedJd;

        public NewsCarrier Carrier;

        /// <summary>The ship that brought it, for a <see cref="NewsCarrier.Ship"/> report.</summary>
        public string CarriedBy;

        /// <summary>Every market's board in the source system as it stood at <see cref="AsOfJd"/> (no hidden havens).</summary>
        public readonly List<Settlement> Markets = new List<Settlement>();

        /// <summary>The source's own notable news up to <see cref="AsOfJd"/>, oldest first.</summary>
        public readonly List<WorldEvent> Headlines = new List<WorldEvent>();

        /// <summary>Who held the source at <see cref="AsOfJd"/> (<see cref="Polities.HolderOf"/>); null if nobody did.</summary>
        public Faction Holder;

        /// <summary>The source's own gates as they stood: who held each and how long it was shut for (U.SPC-T103).</summary>
        public readonly List<GateState> Gates = new List<GateState>();

        /// <summary>Its ports a besieger had declared blockaded, and those under siege, as they stood (U.SPC-T103).</summary>
        public readonly List<PortThreat> PortThreats = new List<PortThreat>();

        /// <summary>Every pair of polities at war at <see cref="AsOfJd"/>, by the one table every system shares (pirates aside).</summary>
        public readonly List<FactionPair> Wars = new List<FactionPair>();

        /// <summary>Armed raider hulls about the source, and the pirate havens known there (U.SPC-T103).</summary>
        public int PirateShips, Havens;

        /// <summary>Whether the report has <paramref name="a"/> and <paramref name="b"/> at war.</summary>
        public bool AtWar(Faction a, Faction b)
        {
            if (a == null || b == null || a == b) return false;
            foreach (var w in Wars)
                if (w.Involves(a) && w.Involves(b)) return true;
            return false;
        }

        /// <summary>How old the news is at <paramref name="jd"/>, days.</summary>
        public double AgeDays(double jd) => jd - AsOfJd;

        /// <summary>The board of the market named <paramref name="name"/>, as the report has it; null if it has none.</summary>
        public Settlement Market(string name)
        {
            foreach (var s in Markets)
                if (s.Name == name) return s;
            return null;
        }

        /// <summary>The same report, as delivered somewhere else at another time by another carrier.</summary>
        internal SystemReport Delivered(double receivedJd, NewsCarrier carrier, string carriedBy)
        {
            var r = new SystemReport { Source = Source, AsOfJd = AsOfJd, ReceivedJd = receivedJd, Carrier = carrier, CarriedBy = carriedBy };
            r.Markets.AddRange(Markets);
            r.Headlines.AddRange(Headlines);
            r.Holder = Holder;
            r.Gates.AddRange(Gates);
            r.PortThreats.AddRange(PortThreats);
            r.Wars.AddRange(Wars);
            r.PirateShips = PirateShips;
            r.Havens = Havens;
            return r;
        }

        /// <summary>The news kinds worth sending beyond the gates: captures, diplomacy and economic shocks.</summary>
        public static bool Notable(WorldEventKind k) => k == WorldEventKind.Capture || k == WorldEventKind.Diplomacy || k == WorldEventKind.Economy;

        /// <summary>The prefix news from abroad is logged under, so it is never sent on again as the receiver's own.</summary>
        public const string ForeignPrefix = "News from ";

        /// <summary>Copies <paramref name="s"/>'s boards and its notable news now.</summary>
        public static SystemReport Take(StarSystem s, double jd)
        {
            var r = new SystemReport { Source = s, AsOfJd = jd, ReceivedJd = jd };
            foreach (var st in s.World.Economy.Settlements)
                if (!st.Hidden && st.Kind != SettlementKind.Haven) r.Markets.Add(st.Snapshot());
            var events = s.World.Events;
            for (int i = Math.Max(0, events.Count - 40); i < events.Count; i++)
            {
                var e = events[i];
                if (e.Jd <= jd && Notable(e.Kind) && !e.Text.StartsWith(ForeignPrefix, StringComparison.Ordinal)) r.Headlines.Add(e);
            }
            TakeSituation(r, s, jd);
            return r;
        }

        /// <summary>
        /// The rest of what a report carries for the galaxy map (U.SPC-T103): who holds the system and its gates, which
        /// gates are shut, its ports under blockade or siege, the wars of the day, and how thick the raiders are there.
        /// </summary>
        static void TakeSituation(SystemReport r, StarSystem s, double jd)
        {
            var world = s.World;
            r.Holder = world.Holder;
            foreach (var g in s.Gates) r.Gates.Add(new GateState { Gate = g, Owner = g.Owner, BlockadeUntilJd = g.BlockadeUntilJd, Damage = 1 - g.Integrity });
            foreach (var st in world.Economy.Settlements)
            {
                if (st.Kind == SettlementKind.Haven)
                {
                    if (!st.Hidden && !st.Fallen) r.Havens++;
                    continue;
                }
                if (st.Hidden || st.Fallen) continue;
                var blockader = world.Diplomacy.BlockadedBy(st);
                var besieger = world.War?.Besieger(st);
                if (blockader != null || besieger != null)
                    r.PortThreats.Add(new PortThreat { Port = st.Name, Owner = st.Owner, BlockadedBy = blockader, BesiegedBy = besieger });
            }
            foreach (var ship in world.Fleet.Ships)
                if (!ship.Destroyed && !ship.IsEscort && !ship.Derelict && ship.Faction != null && ship.Faction.IsPirate && ship.Combat != null && ship.Combat.Armed)
                    r.PirateShips++;
            var factions = world.Polities.Factions;
            for (int i = 0; i < factions.Count; i++)
                for (int j = i + 1; j < factions.Count; j++)
                {
                    var a = factions[i];
                    var b = factions[j];
                    if (!a.IsPirate && !b.IsPirate && world.Diplomacy.Get(a, b) == Relation.War) r.Wars.Add(new FactionPair(a, b));
                }
        }
    }

    /// <summary>One gate as a report saw it: who held it and until when it was shut (U.SPC-T103).</summary>
    public struct GateState
    {
        public JumpGate Gate;
        public Faction Owner;
        public double BlockadeUntilJd;

        /// <summary>Shut at <paramref name="jd"/>, as far as the report knew (a blockade lasts until its date unless lifted).</summary>
        public bool Shut(double jd) => jd < BlockadeUntilJd;

        /// <summary>How much of the ring had been knocked out of it (U.SPC-T97): 0 whole, 1 a wreck (see <see cref="JumpGate.Integrity"/>).</summary>
        public double Damage;

        /// <summary>The ring was out of action: too damaged to hold a crossing open until it is rebuilt.</summary>
        public bool Down => 1 - Damage < JumpGate.OperationalIntegrity;
    }

    /// <summary>A port a report saw under a declared blockade or a siege (U.SPC-T103).</summary>
    public struct PortThreat
    {
        public string Port;
        public Faction Owner, BlockadedBy, BesiegedBy;
    }

    /// <summary>Two polities, as a pair (at war, say), either way round.</summary>
    public readonly struct FactionPair
    {
        public readonly Faction A, B;

        public FactionPair(Faction a, Faction b)
        {
            A = a;
            B = b;
        }

        public bool Involves(Faction f) => f != null && (A == f || B == f);

        public bool Same(FactionPair o) => (A == o.A && B == o.B) || (A == o.B && B == o.A);
    }

    /// <summary>
    /// The news between the star systems (U.SPC-T100). Nothing is known of another system but what has been brought
    /// from it, and nothing crosses faster than the gates: every <see cref="RelayIntervalDays"/> each settled system's
    /// boards and news are beamed out to its gates, passed through by the gates' relays (as long as a crossing takes a
    /// ship, see <see cref="GateNetwork.TransitDays"/>) and beamed in from the far gate (<see cref="RelayDays"/>); a
    /// gate under blockade passes nothing, so the news goes round by other gates or not at all. Every ship that crosses
    /// also carries what its system knew when it went in (<see cref="Bundle"/>), which can be fresher than the relay's.
    /// A system keeps only its newest report from each other (<see cref="Known"/>), and that is what traders there plan
    /// their runs on: stale by the trip, and wrong if the market moved.
    /// </summary>
    public sealed class Newswire
    {
        readonly Galaxy galaxy;
        readonly Dictionary<StarSystem, Dictionary<StarSystem, SystemReport>> known = new Dictionary<StarSystem, Dictionary<StarSystem, SystemReport>>();
        readonly List<(SystemReport report, StarSystem to, double arriveJd)> inFlight = new List<(SystemReport, StarSystem, double)>();
        double nextRelayJd;

        /// <summary>How often each settled system's relay sends its boards and news out, days.</summary>
        public double RelayIntervalDays = 1;

        /// <summary>The most headlines from one report logged at the receiving end.</summary>
        public const int HeadlinesPerReport = 4;

        /// <summary>Reports delivered so far.</summary>
        public int Delivered { get; private set; }

        /// <summary>Raised when a report reaches a system: (where it arrived, the report).</summary>
        public event Action<StarSystem, SystemReport> Received;

        /// <summary>The relay's own traffic through the gates: nobody's enemy, but shut out by a blockade like any outsider.</summary>
        static readonly Faction RelayTraffic = new Faction("Gate relay", "RLY", default);

        /// <summary>When the relays next send.</summary>
        public double NextRelayJd
        {
            get => nextRelayJd;
            set => nextRelayJd = value;
        }

        /// <summary>Reports on their way right now.</summary>
        public int InFlight => inFlight.Count;

        public Newswire(Galaxy galaxy, double jd)
        {
            this.galaxy = galaxy;
            nextRelayJd = jd + RelayIntervalDays;
            // Each system starts knowing the others as the last relay left them.
            foreach (var from in galaxy.Systems)
            {
                if (!from.Settled) continue;
                var report = SystemReport.Take(from, jd);
                foreach (var to in galaxy.Systems)
                {
                    if (to == from) continue;
                    double lag = RelayDays(from, to, jd);
                    if (double.IsInfinity(lag)) continue;
                    var r = report.Delivered(jd, NewsCarrier.Charts, null);
                    r.AsOfJd = jd - lag;
                    Book(to)[from] = r;
                }
            }
        }

        Dictionary<StarSystem, SystemReport> Book(StarSystem at)
        {
            if (!known.TryGetValue(at, out var book)) known[at] = book = new Dictionary<StarSystem, SystemReport>();
            return book;
        }

        /// <summary>The newest report <paramref name="at"/> holds of <paramref name="about"/>; null if none has ever come.
        /// A system's own markets are not news: null for the same system.</summary>
        public SystemReport Known(StarSystem at, StarSystem about)
        {
            if (at == null || about == null || at == about) return null;
            return known.TryGetValue(at, out var book) && book.TryGetValue(about, out var r) ? r : null;
        }

        /// <summary>Every report <paramref name="at"/> holds.</summary>
        public IEnumerable<SystemReport> KnownAt(StarSystem at) =>
            known.TryGetValue(at, out var book) ? book.Values : (IEnumerable<SystemReport>)Array.Empty<SystemReport>();

        /// <summary>
        /// How long light takes between a system's gates and its nearest market, days: the relay beams the news in to that
        /// port (and out from it), and the system's own traffic carries it on from there. Minutes in Sol (Mercury's Caloris
        /// Works), over an hour at Alpha Centauri (Rigil Gate, 9.5 AU out). Zero for a system with no markets.
        /// </summary>
        public static double InnerLightDays(StarSystem s)
        {
            double nearest = double.PositiveInfinity;
            foreach (var st in s.World.Economy.Settlements)
            {
                if (st.Hidden || st.Kind == SettlementKind.Haven) continue;
                nearest = Math.Min(nearest, s.Ephemeris.Helio(st.Body).Length);
            }
            return double.IsInfinity(nearest) ? 0 : LightSpeed.DelayDays(nearest);
        }

        /// <summary>
        /// How long the relay takes from <paramref name="from"/>'s markets to <paramref name="to"/>'s, days: light out to
        /// the gate, the quickest open way through the gates (<see cref="GateNetwork.RouteDays"/>, blockaded gates left
        /// out), and light in from the far gate; or, where that is slower or shut, by a deep-space relay on the route between
        /// the two (U.SPC-T421, <see cref="DeepSpace.RelayLagDays"/>). Infinity when no way is open.
        /// </summary>
        public double RelayDays(StarSystem from, StarSystem to, double jd) => RelayDays(from, to, jd, out _);

        double RelayDays(StarSystem from, StarSystem to, double jd, out bool deepSpace)
        {
            var route = galaxy.Network?.Route(from, to, RelayTraffic, jd);
            double gates = route == null ? double.PositiveInfinity : GateNetwork.RouteDays(route);
            double relay = galaxy.DeepSpace?.RelayLagDays(from, to) ?? double.PositiveInfinity;
            deepSpace = relay < gates;
            double way = Math.Min(gates, relay);
            if (double.IsInfinity(way)) return double.PositiveInfinity;
            return InnerLightDays(from) + way + InnerLightDays(to);
        }

        /// <summary>Sends each settled system's relay report when one is due, and delivers the reports that have arrived.</summary>
        public void Update(double jd)
        {
            if (jd >= nextRelayJd)
            {
                // A long jump of the clock sends once, from now: the boards are as they are now, not as they were.
                Relay(jd);
                nextRelayJd = Math.Max(nextRelayJd + RelayIntervalDays, jd + RelayIntervalDays * 0.5);
            }
            if (inFlight.Count == 0) return;
            inFlight.Sort((a, b) => a.arriveJd.CompareTo(b.arriveJd));
            int done = 0;
            while (done < inFlight.Count && inFlight[done].arriveJd <= jd)
            {
                var (r, to, at) = inFlight[done];
                Deliver(to, r, at);
                done++;
            }
            if (done > 0) inFlight.RemoveRange(0, done);
        }

        /// <summary>Every settled system's boards and news, beamed out now to every system the gates can reach.</summary>
        public void Relay(double jd)
        {
            foreach (var from in galaxy.Systems)
            {
                if (!from.Settled) continue;
                var report = SystemReport.Take(from, jd);
                foreach (var to in galaxy.Systems)
                {
                    if (to == from) continue;
                    double lag = RelayDays(from, to, jd, out bool deepSpace);
                    if (double.IsInfinity(lag)) continue;
                    Send(report, to, jd + lag, deepSpace ? NewsCarrier.DeepSpaceRelay : NewsCarrier.Relay, null);
                }
            }
        }

        void Send(SystemReport report, StarSystem to, double arriveJd, NewsCarrier carrier, string by) =>
            inFlight.Add((report.Delivered(arriveJd, carrier, by), to, arriveJd));

        /// <summary>
        /// What a ship going into a gate from <paramref name="from"/> carries: that system's own boards and news now, and
        /// every report it held of the others.
        /// </summary>
        public List<SystemReport> Bundle(StarSystem from, double jd)
        {
            var bundle = new List<SystemReport>();
            if (from.Settled) bundle.Add(SystemReport.Take(from, jd));
            bundle.AddRange(KnownAt(from));
            return bundle;
        }

        /// <summary>A ship has come out of a gate into <paramref name="to"/> with <paramref name="bundle"/>: it is beamed
        /// in from the gate, arriving at the markets after the light time.</summary>
        public void Carried(StarSystem to, List<SystemReport> bundle, string ship, double jd)
        {
            if (bundle == null) return;
            double arrive = jd + InnerLightDays(to);
            foreach (var r in bundle)
                if (r.Source != to) Send(r, to, arrive, NewsCarrier.Ship, ship);
        }

        /// <summary>
        /// A report reaches <paramref name="to"/>: kept if it is newer than what was known, and its headlines newer than the
        /// last report's are logged there as news from abroad, dated as they arrive and saying how old they are.
        /// </summary>
        public void Deliver(StarSystem to, SystemReport r, double jd)
        {
            var book = Book(to);
            book.TryGetValue(r.Source, out var had);
            if (had != null && had.AsOfJd >= r.AsOfJd) return;
            book[r.Source] = r;
            Delivered++;
            // The newest few headlines since the last report, logged oldest first so the feed reads in order.
            double since = had?.AsOfJd ?? double.NegativeInfinity;
            int first = r.Headlines.Count;
            while (first > 0 && r.Headlines.Count - first < HeadlinesPerReport && r.Headlines[first - 1].Jd > since) first--;
            for (int i = first; i < r.Headlines.Count; i++)
            {
                var e = r.Headlines[i];
                to.World.Log(jd, $"{SystemReport.ForeignPrefix}{r.Source.Name} ({LightSpeed.Describe(jd - e.Jd)} old, by {CarrierText(r)}): {e.Text}", e.Kind, -1);
            }
            Received?.Invoke(to, r);
        }

        static string CarrierText(SystemReport r) => r.Carrier switch
        {
            NewsCarrier.Ship => r.CarriedBy ?? "ship",
            NewsCarrier.Charts => "the charts",
            NewsCarrier.DeepSpaceRelay => "deep-space relay",
            _ => "gate relay",
        };

        /// <summary>A line for the HUD: how old the news from <paramref name="about"/> is here, and how it came.</summary>
        public string Describe(StarSystem at, StarSystem about, double jd)
        {
            var r = Known(at, about);
            if (r == null) return "no news";
            return "news " + LightSpeed.Describe(r.AgeDays(jd)) + " old, by " + CarrierText(r)
                   + (r.Markets.Count > 0 ? ", " + r.Markets.Count.ToString(CultureInfo.InvariantCulture) + " markets" : "");
        }
    }
}
