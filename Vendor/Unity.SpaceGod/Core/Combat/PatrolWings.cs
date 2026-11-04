using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A navy's standing patrol wings (U.SPC-T371): besides its battle groups, every navy that holds ports in a system keeps
    /// a few small wings flying a beat of them, so the space between its worlds is never empty. A wing is a light wing
    /// (three fighters), a heavy-fighter wing (a gunship leading two light fighters) or a destroyer wing (a destroyer
    /// leading a heavy and two light fighters), raised in that order, so a navy with a port or two flies only light fighters
    /// and the destroyer wings are the big navies' (a navy's gate pickets are another thing, see <see cref="GatePickets"/>); each on a beat of up to <see cref="BeatLength"/> of its navy's ports, the wings spread so
    /// that between them they cover every port.
    /// <para>A wing is commanded, not loosed on whatever it meets (see <see cref="War.Commanded"/>): it defends the ports on
    /// its beat when an enemy turns up there, hunts raiders seen working them, and puts in at a yard to repair, but it never
    /// strikes out at an enemy port, is never merged into a battle group, sent through a gate on a campaign, or counted
    /// against the yards' warship cap (<see cref="Ship.PatrolWing"/>).</para>
    /// <para>A navy's corvettes fly as wings do, each stationed for good at one of its ports (U.SPC-T476, see <see cref="Corvettes"/>).</para>
    /// <para>Beside its wings a navy runs supply convoys (U.SPC-T375): one of its own freighters carrying naval stores
    /// round a run of its ports under two fighters, flown as a wing is but never turning aside to defend or hunt
    /// (<see cref="Ship.Convoy"/>).</para>
    /// </summary>
    public static class PatrolWings
    {
        /// <summary>The most wings one navy flies in one system.</summary>
        public const int MaxWingsPerFaction = 4;

        /// <summary>The most ports on one wing's beat.</summary>
        public const int BeatLength = 3;

        /// <summary>The kinds of wing, in the order a navy raises them: light wing, heavy-fighter wing, lane picket (two
        /// destroyers, the second fitted with a field projector as the wing's lane interdictor, that lie in wait on the lanes
        /// between the ports of its beat for raiders, U.SPC-T479, U.SPC-T507), destroyer wing.</summary>
        static readonly ShipClass[][] Compositions =
        {
            new[] { ShipClass.Fighter, ShipClass.Fighter, ShipClass.Fighter },
            new[] { ShipClass.Gunship, ShipClass.Fighter, ShipClass.Fighter },
            new[] { ShipClass.Destroyer, ShipClass.Destroyer, ShipClass.Fighter, ShipClass.Fighter },
            new[] { ShipClass.Destroyer, ShipClass.Gunship, ShipClass.Fighter, ShipClass.Fighter },
        };

        /// <summary>The heavy equipment each kind of wing fits to its hulls as it is raised (U.SPC-T507): which member (0 the
        /// leader) takes which facility. The lane picket's second destroyer carries the field projector.</summary>
        static readonly (int member, string facility)[][] Fittings =
        {
            new (int, string)[0],
            new (int, string)[0],
            new[] { (1, FacilityKinds.FieldProjector) },
            new (int, string)[0],
        };

        static readonly string[] CallSigns =
        {
            "Lamplighter", "Nighthawk", "Vigilant", "Redstart", "Sentinel", "Outrider", "Lookout", "Harfang", "Tripwire", "Nightwatch",
            "Gyrfalcon", "Warder", "Ranger", "Skirmisher", "Lanner", "Kingfisher", "Osprey", "Shrike", "Peregrine", "Hobby",
            "Goshawk", "Kite", "Sparrowhawk", "Falconet",
        };

        static readonly string[] Wingmen = { "Two", "Three", "Four", "Five" };

        /// <summary>The most supply convoys one navy runs in one system (U.SPC-T375).</summary>
        public const int MaxConvoysPerFaction = 3;

        /// <summary>The most ports on one convoy's run.</summary>
        public const int RunLength = 4;

        /// <summary>A convoy's escort: two light fighters, or for every other one a heavy fighter and a light one.</summary>
        static readonly ShipClass[][] ConvoyEscorts =
        {
            new[] { ShipClass.Fighter, ShipClass.Fighter },
            new[] { ShipClass.Gunship, ShipClass.Fighter },
        };

        static readonly string[] ConvoyNames =
        {
            "Provender", "Quartermaster", "Victualler", "Commissary", "Replenisher", "Sutler", "Purveyor", "Storekeeper",
            "Bounty", "Larder", "Forager", "Steward",
        };

        /// <summary>How many supply convoys a navy holding <paramref name="ports"/> ports here runs (U.SPC-T375): one for
        /// every two ports, up to <see cref="MaxConvoysPerFaction"/>; none with fewer than two, with nowhere to run to.</summary>
        public static int ConvoysFor(int ports) => ports < 2 ? 0 : Math.Min(MaxConvoysPerFaction, ports / 2);

        /// <summary>The escort classes of the <paramref name="index"/>th convoy a navy runs.</summary>
        public static ShipClass[] ConvoyEscort(int index) => ConvoyEscorts[index % ConvoyEscorts.Length];

        /// <summary>How many wings a navy holding <paramref name="ports"/> ports here flies: one, plus one for every two
        /// ports, up to <see cref="MaxWingsPerFaction"/>; none with fewer than two, since a beat runs between ports and a
        /// navy's one port is its battle group's to hold.</summary>
        public static int WingsFor(int ports) => ports < 2 ? 0 : Math.Min(MaxWingsPerFaction, 1 + ports / 2);

        /// <summary>The classes of the <paramref name="index"/>th wing a navy raises, its leader first.</summary>
        public static ShipClass[] Composition(int index) => Compositions[index % Compositions.Length];

        /// <summary>The facilities the <paramref name="index"/>th wing a navy raises fits to its hulls: which member (0 the
        /// leader, then its wingmen in <see cref="Composition"/>'s order) takes which facility kind.</summary>
        public static IReadOnlyList<(int member, string facility)> FittingsOf(int index) => Fittings[index % Fittings.Length];

        static void Fit(Ship ship, int index, int member)
        {
            foreach (var (m, facility) in FittingsOf(index))
                if (m == member && FacilityKinds.Of(facility) is FacilityKind kind) Outfitting.Install(ship, kind);
        }

        /// <summary>The bodies of <paramref name="f"/>'s standing ports in <paramref name="w"/>, the most populous first.</summary>
        public static List<int> Ports(World w, Faction f)
        {
            var list = new List<Settlement>();
            foreach (var s in w.Economy.Settlements)
                if (s.Owner == f && !s.Fallen && s.Kind != SettlementKind.Haven) list.Add(s);
            list.Sort((a, b) => b.Population.CompareTo(a.Population));
            var bodies = new List<int>();
            foreach (var s in list)
                if (!bodies.Contains(s.Body)) bodies.Add(s.Body);
            return bodies;
        }

        /// <summary>The beat of the <paramref name="index"/>th of <paramref name="wings"/> wings over <paramref name="ports"/>:
        /// up to <see cref="BeatLength"/> ports in a row, each wing's beat starting further round, so the wings between them
        /// cover them all.</summary>
        public static int[] Beat(List<int> ports, int index, int wings) => Run(ports, index, wings, BeatLength);

        /// <summary>The run of the <paramref name="index"/>th of <paramref name="convoys"/> supply convoys over
        /// <paramref name="ports"/>: up to <see cref="RunLength"/> ports in a row, each convoy's starting further round.</summary>
        public static int[] ConvoyRun(List<int> ports, int index, int convoys) => Run(ports, index, convoys, RunLength);

        static int[] Run(List<int> ports, int index, int wings, int maxLength)
        {
            int n = ports.Count;
            int length = Math.Min(maxLength, n);
            int start = (int)((long)index * n / Math.Max(1, wings)) % n;
            var beat = new int[length];
            for (int j = 0; j < length; j++) beat[j] = ports[(start + j) % n];
            return beat;
        }

        /// <summary>
        /// Raises the standing patrol wings of every navy (not a pirate clan, nor a power without a navy) that holds ports in
        /// <paramref name="w"/>, each stationed at the first port of its beat. Returns the wings' leaders.
        /// </summary>
        public static List<Ship> Raise(World w, double jd)
        {
            var leaders = new List<Ship>();
            foreach (var f in new List<Faction>(w.Fleet.Factions))
            {
                if (!f.IsMilitary || f.IsPirate) continue;
                var ports = Ports(w, f);
                int wings = WingsFor(ports.Count);
                // Call signs run on across every system the navy holds, so no two of its wings share one.
                int sign = 0;
                foreach (var s in w.Polities.Navy(f))
                    if (s.PatrolWing && !s.IsEscort && !s.Convoy && !Corvettes.Stationed(s)) sign++;
                for (int i = 0; i < wings; i++)
                {
                    var classes = Composition(i);
                    string call = CallSigns[sign % CallSigns.Length] + (sign >= CallSigns.Length ? " " + (sign / CallSigns.Length + 1) : "");
                    sign++;
                    var leader = w.Fleet.AddShip(call, classes[0], f, ShipRole.Patrol);
                    leader.PatrolWing = true;
                    leader.Route = Beat(ports, i, wings);
                    leader.RouteIndex = 0;
                    Fit(leader, i, 0);
                    for (int k = 1; k < classes.Length; k++)
                    {
                        var e = w.Fleet.AddShip(call + " " + Wingmen[(k - 1) % Wingmen.Length], classes[k], f, ShipRole.Escort);
                        e.PatrolWing = true;
                        Fit(e, i, k);
                        w.Fleet.AddEscort(leader, e, Fleet.SlotFor(k - 1));
                    }
                    w.Fleet.Station(leader, leader.Route[0], jd);
                    leaders.Add(leader);
                }
                // Its supply convoys: one of its freighters with naval stores round a run of its ports, under escort (U.SPC-T375).
                int convoys = ConvoysFor(ports.Count);
                int named = w.Polities.ConvoyCount(f);
                for (int i = 0; i < convoys; i++)
                {
                    string call = ConvoyNames[named % ConvoyNames.Length] + (named >= ConvoyNames.Length ? " " + (named / ConvoyNames.Length + 1) : "");
                    named++;
                    var leader = w.Fleet.AddShip(call, ShipClass.Freighter, f, ShipRole.Patrol);
                    leader.PatrolWing = true;
                    leader.Convoy = true;
                    leader.Route = ConvoyRun(ports, i, convoys);
                    leader.RouteIndex = 0;
                    leader.Cargo = "Naval stores";
                    var escort = ConvoyEscort(i);
                    for (int k = 0; k < escort.Length; k++)
                    {
                        var e = w.Fleet.AddShip(call + " " + Wingmen[k % Wingmen.Length], escort[k], f, ShipRole.Escort);
                        e.PatrolWing = true;
                        w.Fleet.AddEscort(leader, e, Fleet.SlotFor(k));
                    }
                    w.Fleet.Station(leader, leader.Route[0], jd);
                    leaders.Add(leader);
                }
            }
            // And its corvettes, stationed for good at its ports to police the lanes of the system (U.SPC-T476).
            Corvettes.Raise(w, jd);
            return leaders;
        }

        /// <summary>The next port on <paramref name="leader"/>'s beat after <paramref name="here"/> (its first if it is off
        /// its beat), or <paramref name="here"/> if it has none.</summary>
        public static int NextOnBeat(Ship leader, int here)
        {
            var beat = leader.Route;
            if (beat == null || beat.Length == 0) return here;
            int i = Array.IndexOf(beat, here);
            return i < 0 ? beat[0] : beat[(i + 1) % beat.Length];
        }

        /// <summary>Whether <paramref name="body"/> is on <paramref name="leader"/>'s beat (its patrol route).</summary>
        public static bool OnBeat(Ship leader, int body) => leader.Route != null && Array.IndexOf(leader.Route, body) >= 0;
    }
}
