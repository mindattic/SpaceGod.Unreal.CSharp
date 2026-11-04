using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>What a gate project is for.</summary>
    public enum GateJob
    {
        /// <summary>A new pair of gates: a shorter way between two systems already on the network, or the first way to a frontier star.</summary>
        NewLink,

        /// <summary>Rebuilding a ring damaged or wrecked in a war.</summary>
        Repair,
    }

    public enum GateStage
    {
        /// <summary>Charting the gate site and the far star before any steel is cut.</summary>
        Survey,

        /// <summary>Constructor ships at the site, building as fast as the materials come.</summary>
        Building,

        Done,

        /// <summary>Given up: its builder fell, or the way it was for was made some other way.</summary>
        Abandoned,
    }

    /// <summary>One gate being surveyed, built or rebuilt (see <see cref="GateConstruction"/>).</summary>
    public sealed class GateProject
    {
        public GateJob Work;
        public GateStage Stage;

        /// <summary>The polity paying for it and doing the work.</summary>
        public Faction Builder;

        /// <summary>The system the work is done from: its constructors sit at the gate site at its edge.</summary>
        public StarSystem From;

        /// <summary>The system at the far end; null while that is a frontier star nobody simulates yet (see <see cref="Frontier"/>).</summary>
        public StarSystem To;

        /// <summary>The far star's catalogue entry, for the first gate to a frontier star; null otherwise.</summary>
        public SystemSpec Frontier;

        /// <summary>A repair's ring (in <see cref="From"/>); for a new link, the near gate once it is finished.</summary>
        public JumpGate Gate;

        /// <summary>A repair's starting <see cref="JumpGate.Integrity"/>.</summary>
        public double FromIntegrity;

        public string TargetName;
        public double Ly;

        public double StartJd;
        public double BuildStartJd = double.NaN, DoneJd = double.NaN;

        /// <summary>How long the survey takes, days (none for a repair).</summary>
        public double SurveyDays;

        /// <summary>Days of work the build takes with its full crew of constructors and every tonne to hand (see <see cref="GateConstruction.BuildDays"/>).</summary>
        public double WorkDays;

        /// <summary>Days of full-rate work done so far.</summary>
        public double WorkDone;

        /// <summary>Tonnes of each of <see cref="GateConstruction.Materials"/> the whole job takes, and has taken so far.</summary>
        public readonly double[] Need = new double[3], Used = new double[3];

        /// <summary>Credits paid for the materials so far.</summary>
        public double Spent;

        /// <summary>The constructor ships at the site (hulls in <see cref="From"/>'s fleet).</summary>
        public readonly List<Ship> Constructors = new List<Ship>();

        /// <summary>When the site was last left with no constructor working (lost to raiders or a war), for a replacement.</summary>
        public double CrewLostJd = double.NaN;

        /// <summary>Share of the work done, 0..1.</summary>
        public double Progress => WorkDays <= 0 ? 1 : Math.Min(1, WorkDone / WorkDays);

        public bool Active => Stage == GateStage.Survey || Stage == GateStage.Building;

        /// <summary>Where the far end is, light-years from the Sun.</summary>
        public DVec3 TargetLy => To != null ? To.PositionLy : Frontier.PositionLy;

        /// <summary>The gate site in <see cref="From"/>, km from its root: a repair's ring, or the spot facing the far star.</summary>
        public DVec3 SiteKm => Gate != null ? Gate.PositionKm : (TargetLy - From.PositionLy).Normalized * Galaxy.GateDistanceKm(From);

        /// <summary>Whether this project joins <paramref name="a"/> and the system or star called <paramref name="b"/>, either way round.</summary>
        public bool Joins(StarSystem a, string b) => (From == a && TargetName == b) || (From?.Name == b && TargetName == a.Name);

        public override string ToString() => (Work == GateJob.Repair ? "Rebuilding the " : "") + (Gate?.Name ?? TargetName + " Gate") + " at " + From.Name;
    }

    /// <summary>
    /// How the jump-gate network grows and heals over sim time (U.SPC-T97). The <see cref="GateNetwork"/> a game starts
    /// with is the one grown out from the Sol-Centauri pair before it; from then on the states that hold systems keep
    /// building, a gate at a time:
    ///  - Planning, monthly: each state that holds a system and can stand the bill picks its best project within
    ///    <see cref="MaxLinkLy"/> of a system it holds, or of an empty one it opened itself: the first gate to a frontier star nobody has reached (see
    ///    <see cref="FrontierSystems"/>), or a shortcut between two systems whose way round by other gates is days longer.
    ///    A rich state (the Sol Federation) runs two at once; a poor frontier one waits until it can afford any.
    ///  - Survey (<see cref="SurveyDays"/>): the site and the far star are charted.
    ///  - Building (<see cref="BuildDays"/>, years): constructor ships go out to the site and work through a large bill
    ///    of Alloys, Electronics and Machinery (<see cref="Cost"/>), bought day by day out of real market stock in the
    ///    system (and the builder's capital system), at the market's price, from its treasury. A day short of material,
    ///    money or constructors is a day's work not done, so a war, a blockade or a lean market stretches the years.
    ///  - Done: the pair of gates opens. A gate to a frontier star brings that star into the simulation as an empty
    ///    system, open to settlement (<see cref="Galaxy.SystemOpened"/>).
    /// Gates are damaged in war: a ring whose far end is held by its owner's enemy is now and then sabotaged
    /// (<see cref="JumpGate.Damage"/>; any other harm, a fleet action at the gate, goes through the same call), and a
    /// ring below <see cref="JumpGate.OperationalIntegrity"/> is out of action until its owner rebuilds it, a repair
    /// costing and taking the share of a new gate that the damage did. Repairs are planned before any new work.
    /// </summary>
    public sealed class GateConstruction
    {
        readonly Galaxy galaxy;
        readonly Random rng;
        readonly List<GateProject> projects = new List<GateProject>();
        double nextDailyJd, nextPlanJd;

        /// <summary>Every project, under way or over.</summary>
        public IReadOnlyList<GateProject> Projects => projects;

        /// <summary>The goods a gate is built of.</summary>
        public static readonly Good[] Materials = { Good.Alloys, Good.Electronics, Good.Machinery };

        /// <summary>The farthest a new gate reaches, light-years; <see cref="GateNetwork.MaxLinkLy"/> is the same number, for the lattice it starts from.</summary>
        public const double MaxLinkLy = 7;

        /// <summary>Constructor ships sent to each site; fewer working means slower work.</summary>
        public const int ConstructorsPerProject = 2;

        /// <summary>How often (days) the states look for new work.</summary>
        public const double PlanIntervalDays = 30;

        /// <summary>A state starts a gate only with this share of its bill (at base prices) in its treasury.</summary>
        public const double ReserveShare = 0.5;

        /// <summary>A treasury this deep runs two projects at once.</summary>
        public const double RichTreasury = 2e9;

        /// <summary>A shortcut is only worth building if it saves this many days on the way round.</summary>
        public const double MinSavedDays = 1.5;

        /// <summary>What reaching a new star is worth against days saved on an old way (the frontier comes first).</summary>
        public const double FrontierWorthDays = 6;

        /// <summary>Days before a site that has lost all its constructors gets new ones.</summary>
        public const double ReplaceCrewDays = 60;

        /// <summary>Daily chance a ring whose far end its owner's enemy holds is sabotaged, while the war lasts.</summary>
        public const double SabotageChancePerDay = 1.0 / 400;

        /// <summary>What a gate pair takes, tonnes: a ring tens of kilometres across, with its field coils and the machinery to spin it up.</summary>
        public static double[] Cost(double ly) => new[] { 40000 + 6000 * ly, 5000 + 800 * ly, 8000 + 1200 * ly };

        /// <summary>The bill at base prices, credits.</summary>
        public static double CostCredits(double ly)
        {
            var c = Cost(ly);
            double total = 0;
            for (int i = 0; i < Materials.Length; i++) total += c[i] * GoodInfo.Of(Materials[i]).BasePrice;
            return total;
        }

        /// <summary>Days of work a new pair takes, fully supplied: two years and a little more for each light-year it spans.</summary>
        public static double BuildDays(double ly) => 365.25 * (2 + 0.2 * ly);

        /// <summary>Days to chart the site and the far star before building starts.</summary>
        public static double SurveyDays(double ly) => 60 + 15 * ly;

        /// <summary>A repair's share of a new gate's bill and time: the damage done (a wreck still has its frame, so at most this much).</summary>
        public const double RepairShare = 0.6;

        public GateConstruction(Galaxy galaxy, double jd, int seed = 9701)
        {
            this.galaxy = galaxy;
            rng = new Random(seed);
            nextDailyJd = jd + 1;
            nextPlanJd = jd + 1;
        }

        public IEnumerable<GateProject> Active
        {
            get
            {
                foreach (var p in projects)
                    if (p.Active) yield return p;
            }
        }

        /// <summary>
        /// The new gates under way for the galaxy map (see <see cref="GalaxyMap.Build"/>): each one being built between two
        /// systems already simulated. A first gate to a frontier star is not among them: its far end is no system yet.
        /// </summary>
        public List<GateWork> Works()
        {
            var works = new List<GateWork>();
            foreach (var p in projects)
                if (p.Active && p.Work == GateJob.NewLink && p.Stage == GateStage.Building && p.To != null)
                    works.Add(new GateWork { A = p.From, B = p.To, Progress = p.Progress, Builder = p.Builder });
            return works;
        }

        /// <summary>Gate pairs finished, rings rebuilt, and frontier systems opened so far.</summary>
        public int Built { get; private set; }
        public int Repaired { get; private set; }
        public int Opened { get; private set; }

        /// <summary>Raised when a project is finished.</summary>
        public event Action<GateProject> Completed;

        /// <summary>Each day to <paramref name="jd"/>: the work, then (monthly) the planning, then the war's harm.</summary>
        public void Update(double jd)
        {
            while (jd >= nextDailyJd)
            {
                Daily(nextDailyJd);
                nextDailyJd += 1;
            }
        }

        void Daily(double jd)
        {
            foreach (var p in projects)
                if (p.Active) Step(p, jd, 1);
            Sabotage(jd);
            if (jd >= nextPlanJd)
            {
                Plan(jd);
                nextPlanJd = jd + PlanIntervalDays;
            }
        }

        // ---------------------------------------------------------------- the work

        /// <summary>A day (or <paramref name="days"/>) of work on <paramref name="p"/>.</summary>
        public void Step(GateProject p, double jd, double days)
        {
            if (!p.Active) return;
            if (Fallen(p.Builder))
            {
                Abandon(p, jd, p.Builder.Name + " is no more");
                return;
            }
            if (p.Stage == GateStage.Survey)
            {
                if (jd - p.StartJd < p.SurveyDays) return;
                p.Stage = GateStage.Building;
                p.BuildStartJd = jd;
                SendConstructors(p, jd);
                Log(p, jd, $"The survey of the {SiteName(p)} site is done: {p.Builder.Name} constructors begin the gate to {p.TargetName}, " +
                           $"{p.Ly:0.0} ly, {p.WorkDays / 365.25:0.0} years of work");
                return;
            }
            if (p.Work == GateJob.NewLink && LinkedAlready(p))
            {
                Abandon(p, jd, "the way is already built");
                return;
            }

            double crew = Crew(p, jd);
            if (crew <= 0) return;
            // The day's share of the bill, at the crew's pace; short of any one material, the work goes at that one's pace.
            double rate = crew * days;
            var want = new double[Materials.Length];
            double have = 1;
            for (int i = 0; i < Materials.Length; i++)
            {
                want[i] = Math.Min(p.Need[i] - p.Used[i], p.Need[i] / p.WorkDays * rate);
                if (want[i] <= 1e-9) continue;
                double stock = 0;
                foreach (var s in Sources(p))
                {
                    stock += s.Stock[(int)Materials[i]];
                    s.ExtraDemand[(int)Materials[i]] += want[i] / days;
                }
                have = Math.Min(have, stock / want[i]);
            }
            // ... and as far as the builder can pay for it.
            double bill = 0;
            for (int i = 0; i < Materials.Length; i++) bill += want[i] * PriceOf(p, Materials[i]);
            if (bill > 0) have = Math.Min(have, Math.Max(0, p.Builder.Treasury) / bill);
            if (have <= 0) return;
            for (int i = 0; i < Materials.Length; i++) Buy(p, i, want[i] * have);
            double before = p.WorkDone;
            p.WorkDone = Math.Min(p.WorkDays, p.WorkDone + rate * have);
            // The day's work mends its share of the damage it was sized for, on top of whatever is left of the ring: damage done
            // to it meanwhile stands (U.SPC-T299).
            if (p.Work == GateJob.Repair) p.Gate.Integrity = Math.Min(1, p.Gate.Integrity + (1 - p.FromIntegrity) * (p.WorkDone - before) / p.WorkDays);
            if (p.WorkDone >= p.WorkDays - 1e-9) Complete(p, jd);
        }

        /// <summary>Working constructors as a share of a full crew; a site with none left gets new hulls after <see cref="ReplaceCrewDays"/>.</summary>
        double Crew(GateProject p, double jd)
        {
            int working = 0;
            foreach (var c in p.Constructors)
                if (!c.Destroyed && !c.Derelict && c.Faction == p.Builder) working++;
            if (working > 0)
            {
                p.CrewLostJd = double.NaN;
                return Math.Min(1, working / (double)ConstructorsPerProject);
            }
            if (double.IsNaN(p.CrewLostJd))
            {
                p.CrewLostJd = jd;
                Log(p, jd, $"Work stops on the {SiteName(p)}: no {p.Builder.Name} constructor is left at the site");
            }
            if (jd - p.CrewLostJd >= ReplaceCrewDays)
            {
                p.Constructors.Clear();
                SendConstructors(p, jd);
                p.CrewLostJd = double.NaN;
            }
            return 0;
        }

        /// <summary>Puts a full crew of constructor ships at the site, holding there until the work is done.</summary>
        void SendConstructors(GateProject p, double jd)
        {
            var fleet = p.From.World.Fleet;
            fleet.AddFaction(p.Builder);
            var site = p.SiteKm;
            var outward = site.Normalized;
            var side = DVec3.Cross(Math.Abs(outward.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX, outward).Normalized;
            int root = p.From.Catalog.Root.Index;
            for (int i = p.Constructors.Count; i < ConstructorsPerProject; i++)
            {
                // Either side of the ring, clear of it and of each other.
                var at = site + side * (i % 2 == 0 ? 60 : -60) - outward * 20;
                var ship = fleet.SpawnAt($"Constructor {p.TargetName} {i + 1}", ShipClass.Freighter, p.Builder, root, at, -outward, jd);
                ship.Cargo = "Gate segments for the " + SiteName(p);
                // Held at the site: nothing is scheduled for it until the gate is done.
                ship.PhaseEndJd = jd + 1e6;
                p.Constructors.Add(ship);
            }
        }

        /// <summary>The markets the materials come from: the site's own system first, then the builder's capital system.</summary>
        List<Settlement> Sources(GateProject p)
        {
            var list = new List<Settlement>();
            void Add(World w)
            {
                if (w == null) return;
                foreach (var s in w.Economy.Settlements)
                    if (!s.Fallen && !s.Hidden && s.Kind != SettlementKind.Haven && s.Owner != null && !s.Owner.IsPirate && !list.Contains(s)) list.Add(s);
            }
            Add(p.From.World);
            Add(galaxy.Polities.CapitalSystemOf(p.Builder));
            return list;
        }

        /// <summary>The price the builder pays for a tonne: the dearest market it buys from (a fair estimate of the day's bill).</summary>
        double PriceOf(GateProject p, Good g)
        {
            double price = 0;
            foreach (var s in Sources(p))
                if (s.Stock[(int)g] > 0) price = Math.Max(price, s.Price[(int)g]);
            return price > 0 ? price : GoodInfo.Of(g).BasePrice;
        }

        /// <summary>Takes <paramref name="tonnes"/> of material <paramref name="i"/> from the sources, biggest stocks first, and pays for it.</summary>
        void Buy(GateProject p, int i, double tonnes)
        {
            if (tonnes <= 0) return;
            var g = Materials[i];
            var sources = Sources(p);
            sources.Sort((a, b) => b.Stock[(int)g].CompareTo(a.Stock[(int)g]));
            double left = tonnes;
            foreach (var s in sources)
            {
                if (left <= 1e-9) break;
                double take = Math.Min(left, s.Stock[(int)g]);
                if (take <= 0) continue;
                s.Stock[(int)g] -= take;
                double cost = take * (s.Price[(int)g] > 0 ? s.Price[(int)g] : GoodInfo.Of(g).BasePrice);
                p.Builder.Treasury -= cost;
                p.Spent += cost;
                if (s.Owner != null && s.Owner != p.Builder) s.Owner.Treasury += cost * Economy.PortTax;
                left -= take;
            }
            p.Used[i] += tonnes - left;
        }

        void Complete(GateProject p, double jd)
        {
            p.Stage = GateStage.Done;
            p.DoneJd = jd;
            foreach (var c in p.Constructors)
            {
                // Back to the yards as ordinary hulls: their next move is their own.
                if (c.Destroyed || c.Phase != ShipPhase.Parked) continue;
                c.Cargo = null;
                c.PhaseEndJd = jd + 0.5 + rng.NextDouble();
            }
            if (p.Work == GateJob.Repair)
            {
                // Whole, unless it was damaged again while the work went on: that is left for the next repair (U.SPC-T299).
                p.Gate.Integrity = Math.Min(1, p.Gate.Integrity + (1 - p.FromIntegrity) * Math.Max(0, 1 - p.Progress) + 1e-9);
                if (p.Gate.Integrity > 1 - 1e-6) p.Gate.Integrity = 1;
                // The ring is rebuilt with its forts (U.SPC-T294), not left open to the next war fleet.
                GateNetwork.Fortify(p.Gate);
                Repaired++;
                Log(p, jd, $"{p.Builder.Name} has rebuilt the {p.Gate.Name}: the way to {p.Gate.Destination?.Name} is open again");
                Completed?.Invoke(p);
                return;
            }

            bool opened = false;
            if (p.To == null)
            {
                p.To = galaxy.Find(p.Frontier.Name);
                if (p.To == null)
                {
                    p.To = StarSystem.Create(p.Frontier, jd, polities: galaxy.Polities);
                    galaxy.AddSystem(p.To, jd);
                }
            }
            if (!p.To.Settled && !p.To.OpenToSettlement) opened = true;
            var link = galaxy.Network.Link(p.From, p.To);
            link.AtA.Owner = p.Builder;
            // The far end is the far system's own, if anyone holds it; an empty system's is the builder's.
            link.AtB.Owner = p.To.Holder ?? (p.To.Settled ? p.To.World.Sol : p.Builder);
            // Both ends armed like every other held gate (U.SPC-T92, U.SPC-T294).
            GateNetwork.Fortify(link.AtA);
            GateNetwork.Fortify(link.AtB);
            p.Gate = link.AtA;
            p.To.World.Fleet.AddFaction(p.Builder);
            // And watched from the day it opens (U.SPC-T480).
            GatePickets.PostFull(link.AtA, jd);
            GatePickets.PostFull(link.AtB, jd);
            Built++;
            Log(p, jd, $"The {link.AtA.Name} is finished: {p.Builder.Name} opens the way from {p.From.Name} to {p.To.Name}, {p.Ly:0.0} ly, " +
                       $"after {(jd - p.StartJd) / 365.25:0.0} years and {p.Spent / 1e6:0} million credits");
            if (opened)
            {
                Opened++;
                p.To.OpenedJd = jd;
                p.To.World.Log(jd, $"The first gate into {p.To.Name}, from {p.From.Name}, is open: the system is open to settlement", WorldEventKind.Diplomacy, p.To.Catalog.Root.Index);
                galaxy.RaiseOpened(p.To, p.Builder, jd);
            }
            Completed?.Invoke(p);
        }

        void Abandon(GateProject p, double jd, string why)
        {
            p.Stage = GateStage.Abandoned;
            p.DoneJd = jd;
            foreach (var c in p.Constructors)
                if (!c.Destroyed && c.Phase == ShipPhase.Parked) c.PhaseEndJd = jd + 0.5;
            Log(p, jd, $"Work on the {SiteName(p)} is given up: {why}");
        }

        bool LinkedAlready(GateProject p)
        {
            var to = p.To ?? galaxy.Find(p.TargetName);
            return to != null && galaxy.Network.Between(p.From, to) != null;
        }

        /// <summary>A polity with nothing left: no standing settlement anywhere.</summary>
        bool Fallen(Faction f)
        {
            foreach (var _ in galaxy.Polities.SettlementsOf(f)) return false;
            return true;
        }

        static string SiteName(GateProject p) => p.Gate?.Name ?? GateNetworkName(p.TargetName);

        static string GateNetworkName(string to) => (to == "Sol System" ? "Sol" : to == "Alpha Centauri" ? "Centauri" : to) + " Gate";

        static void Log(GateProject p, double jd, string text) =>
            p.From.World.Log(jd, text, WorldEventKind.Info, p.From.Catalog.Root.Index);

        // ---------------------------------------------------------------- starting work

        /// <summary>Starts a project directly (the planner's way in, and a test's): a new pair from <paramref name="from"/> to a system or frontier star.</summary>
        public GateProject Start(Faction builder, StarSystem from, StarSystem to, SystemSpec frontier, double jd)
        {
            double ly = ((to != null ? to.PositionLy : frontier.PositionLy) - from.PositionLy).Length;
            var p = new GateProject
            {
                Work = GateJob.NewLink, Stage = GateStage.Survey, Builder = builder, From = from, To = to, Frontier = to == null ? frontier : null,
                TargetName = to != null ? to.Name : frontier.Name, Ly = ly, StartJd = jd, SurveyDays = SurveyDays(ly), WorkDays = BuildDays(ly),
            };
            Array.Copy(Cost(ly), p.Need, p.Need.Length);
            projects.Add(p);
            Log(p, jd, $"{builder.Name} begins surveying a gate site at {from.Name} for a way to {p.TargetName}, {ly:0.0} ly");
            return p;
        }

        /// <summary>Starts rebuilding a damaged ring for its owner (or <paramref name="builder"/>).</summary>
        public GateProject StartRepair(JumpGate gate, Faction builder, double jd)
        {
            double damage = 1 - gate.Integrity;
            double share = RepairShare * damage;
            var p = new GateProject
            {
                Work = GateJob.Repair, Stage = GateStage.Building, Builder = builder, From = gate.Home, To = gate.Destination, Gate = gate,
                FromIntegrity = gate.Integrity, TargetName = gate.Destination?.Name ?? gate.Name, Ly = gate.Link?.Ly ?? 0,
                StartJd = jd, BuildStartJd = jd, WorkDays = Math.Max(30, BuildDays(gate.Link?.Ly ?? 0) * share),
            };
            var c = Cost(p.Ly);
            for (int i = 0; i < c.Length; i++) p.Need[i] = c[i] * share;
            projects.Add(p);
            SendConstructors(p, jd);
            Log(p, jd, $"{builder.Name} sends constructors to rebuild the {gate.Name}, {damage * 100:0}% wrecked");
            return p;
        }

        /// <summary>Damage done to a ring (sabotage, a fleet action): logged, and a ring knocked out of action stops traffic until it is rebuilt.</summary>
        public void Damage(JumpGate gate, double amount, double jd, string by = null)
        {
            if (gate == null || amount <= 0) return;
            bool down = gate.Damage(amount);
            string what = gate.Wrecked ? "wrecked" : down ? "knocked out of action" : "damaged";
            gate.Home.World.Log(jd, $"The {gate.Name} is {what}" + (by != null ? " by " + by : "") + $" ({gate.Integrity * 100:0}% whole)",
                WorldEventKind.Battle, gate.Home.Catalog.Root.Index);
        }

        /// <summary>The project under way at <paramref name="gate"/>, or null.</summary>
        public GateProject RepairOf(JumpGate gate)
        {
            foreach (var p in projects)
                if (p.Active && p.Work == GateJob.Repair && p.Gate == gate) return p;
            return null;
        }

        /// <summary>Repairs first, then each state's next new gate.</summary>
        void Plan(double jd)
        {
            foreach (var link in galaxy.Network.Links)
                foreach (var gate in new[] { link.AtA, link.AtB })
                {
                    if (gate.Integrity >= 1 || RepairOf(gate) != null) continue;
                    var builder = gate.Owner != null && !gate.Owner.IsPirate && !Fallen(gate.Owner) ? gate.Owner : gate.Home.Holder;
                    if (builder == null || builder.IsPirate) continue;
                    // A ring on the front line is not rebuilt while the enemy holds the far end.
                    if (Hostile(builder, gate.Partner?.Home?.Holder)) continue;
                    StartRepair(gate, builder, jd);
                }

            foreach (var f in galaxy.Polities.Factions)
            {
                if (f.Kind != PolityKind.State || f.IsPirate) continue;
                int running = 0;
                foreach (var p in projects)
                    if (p.Active && p.Work == GateJob.NewLink && p.Builder == f) running++;
                if (running >= (f.Treasury >= RichTreasury ? 2 : 1)) continue;
                if (BestProject(f, out var from, out var to, out var frontier, out double ly) && f.Treasury >= ReserveShare * CostCredits(ly))
                    Start(f, from, to, frontier, jd);
            }
        }

        bool Hostile(Faction a, Faction b) => a != null && b != null && a != b && galaxy.Polities.Diplomacy.Get(a, b) == Relation.War;

        /// <summary>
        /// <paramref name="f"/>'s best new gate: from a system it holds (or an empty one it opened, see <see cref="BasesOf"/>), within <see cref="MaxLinkLy"/>, to a frontier star nobody has
        /// reached (worth <see cref="FrontierWorthDays"/>) or a system whose way round is at least <see cref="MinSavedDays"/> longer
        /// than a direct gate would be; whichever is worth most for its length. False if nothing is.
        /// </summary>
        public bool BestProject(Faction f, out StarSystem from, out StarSystem to, out SystemSpec frontier, out double ly)
        {
            from = to = null;
            frontier = null;
            ly = 0;
            double best = 0;
            foreach (var s in BasesOf(f))
            {
                foreach (var t in galaxy.Systems)
                {
                    // A Void Base is a relay for the network's gaps (U.SPC-T372), no place for a state to reach out to: nothing there to
                    // open or hold, and a gate it built in would hand the lawless relay to the builder.
                    if (t == s || t.IsVoidBase || galaxy.Network.Between(s, t) != null || Targeted(s, t.Name)) continue;
                    double d = Galaxy.DistanceLy(s, t);
                    if (d > MaxLinkLy || !SiteClear(s, t.PositionLy) || !SiteClear(t, s.PositionLy)) continue;
                    var round = galaxy.Network.Route(s, t);
                    double saved = (round == null ? 30 : GateNetwork.RouteDays(round)) - GateNetwork.TransitDays(d);
                    if (saved < MinSavedDays) continue;
                    double score = saved / d;
                    if (score > best)
                    {
                        best = score;
                        from = s;
                        to = t;
                        frontier = null;
                        ly = d;
                    }
                }
                foreach (var spec in FrontierSystems.All())
                {
                    // Nobody surveys a gate to a star nobody has discovered (U.SPC-T456).
                    if (galaxy.Find(spec.Name) != null || galaxy.IsUndiscovered(spec.NeighbourName ?? spec.Name) || Targeted(null, spec.Name)) continue;
                    double d = (spec.PositionLy - s.PositionLy).Length;
                    if (d > MaxLinkLy || !SiteClear(s, spec.PositionLy)) continue;
                    double score = FrontierWorthDays / d;
                    if (score > best)
                    {
                        best = score;
                        from = s;
                        to = null;
                        frontier = spec;
                        ly = d;
                    }
                }
            }
            return from != null;
        }

        /// <summary>Whether a new link from <paramref name="a"/> (to <paramref name="b"/>, or to anywhere when null; a frontier star
        /// is only ever reached once) is already being worked on.</summary>
        bool Targeted(StarSystem a, string b)
        {
            foreach (var p in projects)
            {
                if (!p.Active || p.Work != GateJob.NewLink) continue;
                if (a == null ? p.TargetName == b : p.Joins(a, b)) return true;
            }
            return false;
        }

        /// <summary>A gate in <paramref name="s"/> facing <paramref name="towardLy"/> would stand well clear of every gate it has (and every site being built).</summary>
        bool SiteClear(StarSystem s, DVec3 towardLy)
        {
            var site = (towardLy - s.PositionLy).Normalized * Galaxy.GateDistanceKm(s);
            foreach (var g in s.Gates)
                if ((g.PositionKm - site).Length < JumpGate.ActivationRangeKm * 3) return false;
            foreach (var p in projects)
                if (p.Active && p.Work == GateJob.NewLink && p.From == s && (p.SiteKm - site).Length < JumpGate.ActivationRangeKm * 3) return false;
            return true;
        }

        /// <summary>
        /// Where <paramref name="f"/> can build from: the systems it holds, and the empty ones it opened itself (it holds the
        /// gate in), so its gates go on outward star by star, their materials coming from its capital's markets.
        /// </summary>
        public List<StarSystem> BasesOf(Faction f)
        {
            var bases = new List<StarSystem>();
            foreach (var held in galaxy.Polities.SystemsHeldBy(f))
            {
                var s = SystemOf(held);
                if (s != null) bases.Add(s);
            }
            foreach (var s in galaxy.Systems)
            {
                if (s.Settled || s.IsVoidBase || bases.Contains(s)) continue;
                foreach (var gate in s.Gates)
                    if (gate.Owner == f && gate.Working)
                    {
                        bases.Add(s);
                        break;
                    }
            }
            return bases;
        }

        StarSystem SystemOf(World w)
        {
            foreach (var s in galaxy.Systems)
                if (s.World == w) return s;
            return null;
        }

        // ---------------------------------------------------------------- war

        /// <summary>Now and then, a ring whose far end its owner's enemy holds is sabotaged from the far side.</summary>
        void Sabotage(double jd)
        {
            foreach (var link in galaxy.Network.Links)
                foreach (var gate in new[] { link.AtA, link.AtB })
                {
                    if (gate.Wrecked || gate.Owner == null) continue;
                    var enemy = gate.Partner?.Home?.Holder;
                    if (!Hostile(gate.Owner, enemy) || rng.NextDouble() > SabotageChancePerDay) continue;
                    Damage(gate, 0.35 + 0.65 * rng.NextDouble(), jd, enemy.Name + " saboteurs");
                }
        }
    }
}
