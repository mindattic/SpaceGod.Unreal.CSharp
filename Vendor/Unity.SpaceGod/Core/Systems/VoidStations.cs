using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The void stations of the real neighbourhood (U.SPC-T454), as a <see cref="SystemSource"/>: wherever a stretch of
    /// <see cref="MaxLegLy"/> between the real systems out to <see cref="CatalogueStars.ChartedToLy"/> (Sol, the hand-built
    /// systems and the <see cref="CatalogueStars"/>) would otherwise hold no destination, a relay stands in open space
    /// (<see cref="StarSystem.CreateVoidBase"/>), so no leg between charted destinations is longer than a gate reaches.
    ///
    /// The real systems are joined by their shortest links (a minimum spanning tree over the distances, from Sol): a link
    /// within <see cref="MaxLegLy"/> needs nothing, and one longer is cut into even legs no longer than that by stations
    /// laid along it (<see cref="GateNetwork.RelayPositions"/>). Every real system can so be reached from every other in
    /// legs of <see cref="MaxLegLy"/> or less, by way of other systems and stations. A tree's long link never passes near a
    /// third system (that one would be the shorter way round), so no station sits on top of a star. They are numbered
    /// outward from Sol, nearest first, and are the same in every galaxy: they come from the real stars, not the seed.
    /// </summary>
    public sealed class VoidStations : SystemSource
    {
        public const string SourceName = "Void stations";

        /// <summary>What every void station's name starts with; its number follows, nearest Sol first.</summary>
        public const string Prefix = "Void Station";

        /// <summary>The longest leg left between charted destinations: the reach of one gate (<see cref="GateConstruction.MaxLinkLy"/>).</summary>
        public const double MaxLegLy = GateConstruction.MaxLinkLy;

        /// <summary>The clear space kept about each station, so the procedural galaxy puts no star on one.</summary>
        public const double ClearLy = 1;

        public override string Name => SourceName;
        /// <summary>
        /// Ahead of the catalogue (U.SPC-T456), whose claim on the 20 ly about Sol and the 3 ly about each of its stars is to keep
        /// invented stars out, not the relays between real ones: after it, a station that near Sol or a star was never charted.
        /// </summary>
        public override int Order => 75;

        /// <summary>In the galaxy from the start like the stars they join (U.SPC-T456): known within <see cref="StarDiscovery.KnownToLy"/>
        /// of Sol, undiscovered past it, as the stars about them are.</summary>
        public override bool ChartedFromStart => true;

        static List<StarEntry> stations;
        static Dictionary<string, (string a, string b)> bridges;
        static readonly object gate = new object();

        /// <summary>The real systems the stations join, Sol first, then the hand-built systems and the catalogue stars (by name, once each).</summary>
        public static List<StarEntry> Destinations()
        {
            var list = new List<StarEntry> { new StarEntry { Name = "Sol", PositionLy = DVec3.Zero, Tag = "sol" } };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sol", "Sol System" };
            foreach (var e in HandBuiltSystems.Charted())
                if (names.Add(e.Name)) list.Add(e);
            foreach (var e in CatalogueStars.Stars())
                if (names.Add(e.Name)) list.Add(e);
            return list;
        }

        /// <summary>Every void station, nearest Sol first.</summary>
        public static IReadOnlyList<StarEntry> Stations()
        {
            lock (gate)
            {
                if (stations == null) Lay();
                return stations;
            }
        }

        /// <summary>The two real systems whose link the station called <paramref name="name"/> stands on; nulls if there is none.</summary>
        public static (string a, string b) Bridges(string name)
        {
            Stations();
            return bridges.TryGetValue(name, out var pair) ? pair : (null, null);
        }

        static void Lay()
        {
            var at = Destinations();
            int n = at.Count;
            // Prim's tree from Sol; ties go to the earlier system, so it is the same tree every run.
            var inTree = new bool[n];
            var best = new double[n];
            var from = new int[n];
            for (int i = 0; i < n; i++)
            {
                best[i] = double.PositiveInfinity;
                from[i] = -1;
            }
            best[0] = 0;
            var laid = new List<(DVec3 ly, string a, string b)>();
            for (int step = 0; step < n; step++)
            {
                int u = -1;
                for (int i = 0; i < n; i++)
                    if (!inTree[i] && (u < 0 || best[i] < best[u])) u = i;
                inTree[u] = true;
                if (from[u] >= 0 && best[u] > MaxLegLy)
                    foreach (var p in GateNetwork.RelayPositions(at[from[u]].PositionLy, at[u].PositionLy, MaxLegLy))
                        laid.Add((p, at[from[u]].Name, at[u].Name));
                for (int v = 0; v < n; v++)
                {
                    if (inTree[v]) continue;
                    double d = (at[v].PositionLy - at[u].PositionLy).Length;
                    if (d < best[v])
                    {
                        best[v] = d;
                        from[v] = u;
                    }
                }
            }
            laid.Sort((x, y) =>
            {
                int c = x.ly.Length.CompareTo(y.ly.Length);
                return c != 0 ? c : x.ly.X.CompareTo(y.ly.X);
            });
            stations = new List<StarEntry>();
            bridges = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            for (int k = 0; k < laid.Count; k++)
            {
                string name = Prefix + " " + (k + 1).ToString(CultureInfo.InvariantCulture);
                bridges[name] = (laid[k].a, laid[k].b);
                stations.Add(new StarEntry
                {
                    Name = name, PositionLy = laid[k].ly, Spectral = "Void station", TeffK = VoidBaseBuilder.TinySunTeffK,
                    MassSolar = 0, RadiusSolar = 0, LuminositySolar = VoidBaseBuilder.TinySuns * 1e-6, Source = SourceName, Tag = "void station",
                });
            }
        }

        public override bool Claims(DVec3 ly)
        {
            if (ly.Length > CatalogueStars.ChartedToLy + ClearLy) return false;
            foreach (var s in Stations())
                if ((s.PositionLy - ly).Length < ClearLy) return true;
            return false;
        }

        public override IEnumerable<StarEntry> StarsNear(DVec3 centreLy, double radiusLy, int seed)
        {
            foreach (var s in Stations())
                if ((s.PositionLy - centreLy).Length <= radiusLy)
                    yield return new StarEntry
                    {
                        Name = s.Name, PositionLy = s.PositionLy, Spectral = s.Spectral, TeffK = s.TeffK, MassSolar = s.MassSolar,
                        RadiusSolar = s.RadiusSolar, LuminositySolar = s.LuminositySolar, Source = s.Source, Tag = s.Tag,
                    };
        }

        public override SystemSpec Define(StarEntry star, int seed)
        {
            StarEntry row = null;
            foreach (var s in Stations())
                if (string.Equals(s.Name, star.Name, StringComparison.OrdinalIgnoreCase)) row = s;
            if (row == null) return null;
            var (a, b) = Bridges(row.Name);
            string summary = $"A void station in open space between {a} and {b}, {row.PositionLy.Length.ToString("0.0", CultureInfo.InvariantCulture)} "
                             + "light-years from the Sun: no star, no planet, a relay so the way between them has a stop. Fiction.";
            double ra = 0, dec = 0;
            Astro.RaDecOf(row.PositionLy, out ra, out dec);
            return new SystemSpec
            {
                Name = row.Name, NeighbourName = row.Name, RaDeg = ra, DecDeg = dec, DistanceLy = row.PositionLy.Length,
                Primary = StellarNode.Single(new StarSpec
                {
                    Name = row.Name, Spectral = row.Spectral, MassSolar = row.MassSolar, RadiusSolar = row.RadiusSolar,
                    LuminositySolar = row.LuminositySolar, TeffK = row.TeffK, Summary = summary,
                }),
                Summary = summary,
            };
        }

        /// <summary>Built as the relay it is: the generator and its tiny suns, unsettled and nobody's (see <see cref="StarSystem.IsVoidBase"/>).</summary>
        public override StarSystem Build(StarEntry star, SystemSpec spec, double jd, Polities polities) =>
            StarSystem.CreateVoidBase(star.Name, star.PositionLy, jd, polities);
    }
}
