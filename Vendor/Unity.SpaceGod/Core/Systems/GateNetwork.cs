using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>One jump-gate link: a gate in each of two systems, paired, and the light-years between them.</summary>
    public sealed class GateLink
    {
        public StarSystem A { get; }
        public StarSystem B { get; }
        public JumpGate AtA { get; }
        public JumpGate AtB { get; }
        public double Ly { get; }

        /// <summary>How long a ship spends in the gate crossing this link (days); see <see cref="GateNetwork.TransitDays"/>.</summary>
        public double TransitDays => GateNetwork.TransitDays(Ly);

        internal GateLink(StarSystem a, StarSystem b, JumpGate atA, JumpGate atB)
        {
            A = a;
            B = b;
            AtA = atA;
            AtB = atB;
            Ly = Galaxy.DistanceLy(a, b);
        }

        public StarSystem Other(StarSystem s) => s == A ? B : s == B ? A : null;

        /// <summary>This link's gate in <paramref name="s"/>, or null if it does not touch it.</summary>
        public JumpGate GateIn(StarSystem s) => s == A ? AtA : s == B ? AtB : null;

        /// <summary>Both rings whole enough to hold a crossing open (see <see cref="JumpGate.Working"/>).</summary>
        public bool Working => AtA.Working && AtB.Working;

        /// <summary>Open for <paramref name="faction"/> leaving <paramref name="from"/>: both rings working, and both ends let it through.</summary>
        public bool Open(StarSystem from, Faction faction, double jd)
        {
            var near = GateIn(from);
            return near != null && Working && near.Admits(faction, jd) && near.Partner.Admits(faction, jd);
        }

        /// <summary>
        /// Open for <paramref name="faction"/>'s war fleet leaving <paramref name="from"/> (U.SPC-T92): each end either lets
        /// it through or is held by a power it is at war with, and so may be forced (see <see cref="JumpGate.HostileTo"/>).
        /// </summary>
        public bool Forcible(StarSystem from, Faction faction, double jd)
        {
            var near = GateIn(from);
            return near != null && Working && GateNetwork.Passable(near, faction, jd);
        }

        public override string ToString() => AtA.Name + " / " + AtB.Name;
    }

    /// <summary>
    /// The jump gates between the simulated systems: a sparse lattice, not all-pairs. Each system is
    /// linked to its <see cref="Neighbours"/> nearest systems within <see cref="MaxLinkLy"/> (and they to
    /// it), and any islands that leaves are joined by their shortest links (a minimum spanning tree over
    /// the distances), so every system can reach every other, often only by way of others. A bridge
    /// longer than <see cref="MaxLinkLy"/> is never built direct: one or more Void Bases
    /// (<see cref="StarSystem.CreateVoidBase"/>) are laid along the way instead, purely to relay it
    /// (U.SPC-T372). Each link is a pair of <see cref="JumpGate"/>s, one at each end, set at that
    /// system's edge facing the other star. Routes are planned over the graph by crossing time
    /// (<see cref="Route"/>), skipping gates that will not open for the traveller (an enemy's, or one
    /// under blockade).
    /// </summary>
    public sealed class GateNetwork
    {
        readonly List<GateLink> links = new List<GateLink>();
        readonly List<StarSystem> systems = new List<StarSystem>();

        public IReadOnlyList<GateLink> Links => links;
        public IReadOnlyList<StarSystem> Systems => systems;

        /// <summary>How many nearest neighbours each system is linked to.</summary>
        public const int Neighbours = 2;

        /// <summary>
        /// The farthest a single gate link reaches (U.SPC-T372): the same limit a state's own new gates are built
        /// within once the game is under way (<see cref="GateConstruction.MaxLinkLy"/>), kept in the one place so the
        /// initial lattice and everything grown onto it later share the same reach. Past it, two systems are only
        /// ever joined by a chain of hops through others' gates, or, where no real star sits near enough along the
        /// way, through Void Bases built in open space purely to relay it.
        /// </summary>
        public const double MaxLinkLy = GateConstruction.MaxLinkLy;

        /// <summary>Forts a state with a navy keeps at each gate it holds (U.SPC-T92).</summary>
        public const int GateForts = 2;

        /// <summary>What a route planner reckons a gate held against it costs to force (days), so a war fleet goes round by a
        /// friendly or neutral way where there is one (U.SPC-T92).</summary>
        public const double ForcingDays = 6;

        /// <summary>A crossing: six hours to spin the gate up and line up, plus two hours a light-year.</summary>
        public static double TransitDays(double ly) => 0.25 + ly / 12.0;

        /// <summary>Allowance for the flight from one gate to the next inside a system a route passes through (days).</summary>
        public const double CrossingDays = 0.5;

        /// <summary>
        /// Builds the lattice over <paramref name="systems"/> (<paramref name="jd"/> and <paramref name="polities"/>
        /// for any Void Base a gap wider than <paramref name="maxLinkLy"/> needs) and gives each system its gates
        /// (see <see cref="StarSystem.Gates"/>). Any Void Base built is appended to <paramref name="systems"/> itself,
        /// so the caller's own list picks it up.
        /// </summary>
        public static GateNetwork Build(List<StarSystem> systems, double jd, Polities polities, int neighbours = Neighbours, double maxLinkLy = MaxLinkLy)
        {
            var net = new GateNetwork();
            net.systems.AddRange(systems);
            int n = systems.Count;
            var linked = new HashSet<(int, int)>();
            void Join(int i, int j)
            {
                var key = i < j ? (i, j) : (j, i);
                if (i == j || !linked.Add(key)) return;
                net.Link(systems[key.Item1], systems[key.Item2]);
            }

            // Each system's nearest few within reach (by distance, ties by list order so the lattice is the same every run).
            for (int i = 0; i < n; i++)
            {
                var order = new List<int>();
                for (int j = 0; j < n; j++) if (j != i) order.Add(j);
                int me = i;
                order.Sort((a, b) =>
                {
                    int c = Galaxy.DistanceLy(systems[me], systems[a]).CompareTo(Galaxy.DistanceLy(systems[me], systems[b]));
                    return c != 0 ? c : a.CompareTo(b);
                });
                int joined = 0;
                for (int k = 0; k < order.Count && joined < neighbours; k++)
                {
                    // Sorted ascending: the first one out of reach means every one after it is too.
                    if (Galaxy.DistanceLy(systems[i], systems[order[k]]) > maxLinkLy) break;
                    Join(i, order[k]);
                    joined++;
                }
            }

            // Join any islands by their shortest links (Kruskal over all pairs, keeping only those that bridge two pieces);
            // one too wide for a direct gate is bridged by Void Bases instead (U.SPC-T372).
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }
            foreach (var (i, j) in linked) parent[Find(i)] = Find(j);
            var pairs = new List<(double ly, int i, int j)>();
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    pairs.Add((Galaxy.DistanceLy(systems[i], systems[j]), i, j));
            pairs.Sort((a, b) => a.ly != b.ly ? a.ly.CompareTo(b.ly) : a.i != b.i ? a.i.CompareTo(b.i) : a.j.CompareTo(b.j));
            int nextVoidBase = 1;
            foreach (var s in systems) if (s.IsVoidBase) nextVoidBase++;
            foreach (var (ly, i, j) in pairs)
            {
                if (Find(i) == Find(j)) continue;
                parent[Find(i)] = Find(j);
                if (ly <= maxLinkLy) Join(i, j);
                else BridgeWithRelays(net, systems, systems[i], systems[j], jd, polities, maxLinkLy, ref nextVoidBase);
            }
            net.AssignOwners();
            return net;
        }

        /// <summary>
        /// Joins <paramref name="a"/> and <paramref name="b"/>, too far apart for one gate, by a chain of Void Bases
        /// (U.SPC-T372) laid along the straight line between them, each hop within <paramref name="maxLinkLy"/>; the
        /// new systems are appended to <paramref name="systems"/> (the caller's own list) as they are built.
        /// </summary>
        static void BridgeWithRelays(GateNetwork net, List<StarSystem> systems, StarSystem a, StarSystem b, double jd, Polities polities,
            double maxLinkLy, ref int nextVoidBase)
        {
            var prev = a;
            foreach (var positionLy in RelayPositions(a.PositionLy, b.PositionLy, maxLinkLy))
            {
                var relay = StarSystem.CreateVoidBase("Void Base " + nextVoidBase++, positionLy, jd, polities);
                systems.Add(relay);
                net.Link(prev, relay);
                prev = relay;
            }
            net.Link(prev, b);
        }

        /// <summary>
        /// Light-year positions for a chain of Void Bases along the straight line from <paramref name="aLy"/> to
        /// <paramref name="bLy"/> (U.SPC-T372), as evenly spaced as the gap needs so no hop is longer than
        /// <paramref name="maxLy"/>; empty when the two already are.
        /// </summary>
        public static List<DVec3> RelayPositions(DVec3 aLy, DVec3 bLy, double maxLy)
        {
            var list = new List<DVec3>();
            double total = (bLy - aLy).Length;
            int hops = Math.Max(1, (int)Math.Ceiling(total / maxLy - 1e-9));
            for (int k = 1; k < hops; k++) list.Add(aLy + (bLy - aLy) * ((double)k / hops));
            return list;
        }

        /// <summary>Pairs a new gate in each of the two systems, each at its own edge facing the other.</summary>
        public GateLink Link(StarSystem a, StarSystem b)
        {
            var atA = new JumpGate(GateName(b), a, Galaxy.Direction(a, b) * Galaxy.GateDistanceKm(a));
            var atB = new JumpGate(GateName(a), b, Galaxy.Direction(b, a) * Galaxy.GateDistanceKm(b));
            atA.Partner = atB;
            atB.Partner = atA;
            var link = new GateLink(a, b, atA, atB);
            atA.Link = atB.Link = link;
            a.AddGate(atA);
            b.AddGate(atB);
            links.Add(link);
            if (!systems.Contains(a)) systems.Add(a);
            if (!systems.Contains(b)) systems.Add(b);
            return link;
        }

        /// <summary>A gate is named for where it leads: the Centauri Gate in Sol, the Sol Gate at Alpha Centauri.</summary>
        static string GateName(StarSystem to) =>
            (to.Name == "Sol System" ? "Sol" : to.Name == "Alpha Centauri" ? "Centauri" : to.Name) + " Gate";

        /// <summary>Each gate belongs to its system's leading power (Sol's Federation, the Centauri Accord, a survey service).</summary>
        public void AssignOwners()
        {
            foreach (var l in links)
            {
                l.AtA.Owner = l.A.World.Sol;
                l.AtB.Owner = l.B.World.Sol;
            }
        }

        /// <summary>
        /// Arms the gates (U.SPC-T92): each one a state with a navy holds gets <see cref="GateForts"/> forts, which a war
        /// fleet must fight to force it; a survey service's gates in empty systems stand unguarded.
        /// </summary>
        public void Fortify()
        {
            foreach (var l in links)
            {
                Fortify(l.AtA);
                Fortify(l.AtB);
            }
        }

        /// <summary>
        /// Arms one gate by the same rule as <see cref="Fortify()"/>: its owner's full complement of forts if a state with a
        /// navy holds it, none otherwise. Used for a gate the builders have just finished or rebuilt (U.SPC-T294), so it
        /// opens guarded like every other held gate.
        /// </summary>
        public static void Fortify(JumpGate g)
        {
            if (g == null) return;
            g.MaxForts = g.Owner != null && g.Owner.IsMilitary && g.Owner.Kind == PolityKind.State ? GateForts : 0;
            while (g.Forts.Count < g.MaxForts) g.Forts.Add(new ShipCombat(CombatSpec.Platform));
        }

        /// <summary>
        /// Whether a war fleet of <paramref name="faction"/> can take <paramref name="near"/> to its partner (U.SPC-T92): both
        /// ends let it through, or are held by an enemy it may force.
        /// </summary>
        public static bool Passable(JumpGate near, Faction faction, double jd) =>
            near?.Partner != null && (near.Admits(faction, jd) || near.HostileTo(faction)) && (near.Partner.Admits(faction, jd) || near.Partner.HostileTo(faction));

        public IEnumerable<GateLink> LinksOf(StarSystem s)
        {
            foreach (var l in links)
                if (l.A == s || l.B == s) yield return l;
        }

        /// <summary>The link joining two systems directly, or null.</summary>
        public GateLink Between(StarSystem a, StarSystem b)
        {
            foreach (var l in links)
                if ((l.A == a && l.B == b) || (l.A == b && l.B == a)) return l;
            return null;
        }

        /// <summary>
        /// The quickest way from <paramref name="from"/> to <paramref name="to"/> over the gates
        /// (Dijkstra on crossing time, plus <see cref="CrossingDays"/> for each system passed through),
        /// as the gates to take in order, each in the system the ship is in when it takes it. Gates
        /// that will not open for <paramref name="traveller"/> at <paramref name="jd"/> are left out
        /// (null traveller: any gate still working), and a link with a ring out of action (see
        /// <see cref="GateLink.Working"/>) for everyone. Empty for the same system; null when there is no way through.
        /// <paramref name="force"/> plans a war fleet's way (U.SPC-T92): a gate held by an enemy counts too, at
        /// <see cref="ForcingDays"/> more, as one it can fight its way through.
        /// </summary>
        public List<JumpGate> Route(StarSystem from, StarSystem to, Faction traveller = null, double jd = double.NegativeInfinity, bool force = false)
        {
            if (from == null || to == null) return null;
            if (from == to) return new List<JumpGate>();
            var best = new Dictionary<StarSystem, double> { [from] = 0 };
            var via = new Dictionary<StarSystem, JumpGate>();
            var done = new HashSet<StarSystem>();
            while (true)
            {
                StarSystem here = null;
                double hereDays = double.MaxValue;
                foreach (var kv in best)
                    if (!done.Contains(kv.Key) && kv.Value < hereDays)
                    {
                        hereDays = kv.Value;
                        here = kv.Key;
                    }
                if (here == null) return null;
                if (here == to) break;
                done.Add(here);
                foreach (var l in LinksOf(here))
                {
                    var next = l.Other(here);
                    if (done.Contains(next) || !l.Working) continue;
                    double forcing = 0;
                    if (traveller != null && !l.Open(here, traveller, jd))
                    {
                        if (!force || !l.Forcible(here, traveller, jd)) continue;
                        forcing = ForcingDays;
                    }
                    double days = hereDays + l.TransitDays + (here == from ? 0 : CrossingDays) + forcing;
                    if (!best.TryGetValue(next, out double known) || days < known)
                    {
                        best[next] = days;
                        via[next] = l.GateIn(here);
                    }
                }
            }
            var route = new List<JumpGate>();
            for (var s = to; s != from; s = via[s].Home) route.Add(via[s]);
            route.Reverse();
            return route;
        }

        /// <summary>Time in the gates along a route, plus the crossings between them (days).</summary>
        public static double RouteDays(List<JumpGate> route)
        {
            if (route == null || route.Count == 0) return 0;
            double days = (route.Count - 1) * CrossingDays;
            foreach (var g in route) days += g.Link.TransitDays;
            return days;
        }

        /// <summary>Light-years along a route.</summary>
        public static double RouteLy(List<JumpGate> route)
        {
            double ly = 0;
            if (route != null)
                foreach (var g in route) ly += g.Link.Ly;
            return ly;
        }
    }
}
