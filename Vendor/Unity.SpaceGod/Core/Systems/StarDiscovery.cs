using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>Whether a star of the real neighbourhood is known yet (U.SPC-T456, see <see cref="Galaxy.Undiscovered"/>).</summary>
    public enum DiscoveryState
    {
        /// <summary>Known: on the charts, the galaxy map and the FTL targets, as every star always was.</summary>
        Discovered,

        /// <summary>
        /// Not known to anyone yet: in the galaxy, its well pulling a cruise out like any star's, but on no chart, not on the
        /// galaxy map, not a destination to plot, survey or aim an FTL drive at, and never built into a running system,
        /// until something comes within <see cref="StarDiscovery.Range"/> of it.
        /// </summary>
        Undiscovered,
    }

    /// <summary>
    /// How the real neighbourhood starts and is found (U.SPC-T456): every system of docs/nearest_stars_2027.csv and every void
    /// station between them is in the galaxy from the start (<see cref="Galaxy.SetUpNeighbourhood"/>), those within
    /// <see cref="KnownToLy"/> of Sol charted and known, the rest <see cref="DiscoveryState.Undiscovered"/>. One is discovered
    /// when a lookout (<see cref="Lookouts"/>: the pilot, a group crossing open space) comes within <see cref="Range"/> of it.
    /// <see cref="VoidStations"/> keeps every leg at <see cref="VoidStations.MaxLegLy"/> or less, so with the range at least that,
    /// going to each star as it is found reaches every one in the end. New ways of finding a star plug in as a lookout.
    /// </summary>
    [SettingsSource]
    public static class StarDiscovery
    {
        /// <summary>How far from Sol the neighbourhood is known at the start: every real system and void station within it.</summary>
        public const double KnownToLy = 21;

        /// <summary>How near a lookout must come to a star to discover it, light-years.</summary>
        public static readonly RangeSetting Range = new RangeSetting("simulation.discoveryRangeLy", SettingGroups.Simulation,
            "Discovery range", VoidStations.MaxLegLy, 1, 20, 0.5,
            "How near (light-years) you, or a ship crossing open space, must come to an undiscovered star or void station past " +
            "the known neighbourhood to discover it and put it on the charts. Below 7 ly some stars can only be found by passing them.",
            v => v.ToString("0.#", CultureInfo.InvariantCulture) + " ly", "discovery discover undiscovered explore range stars charts");

        /// <summary>
        /// A source of the places stars are being looked for from: (the galaxy, when) to each place and who is looking, for the
        /// news. A new way of finding stars (a long-range telescope, a probe) plugs in by adding one.
        /// </summary>
        public delegate IEnumerable<(GalacticPosition at, string by)> Lookout(Galaxy galaxy, double jd);

        /// <summary>Where stars are looked for from: the pilot, and every group crossing open space.</summary>
        public static readonly List<Lookout> Lookouts = new List<Lookout> { Pilot, Cruises };

        /// <summary>The pilot (<see cref="Galaxy.Viewpoint"/>), wherever they are.</summary>
        public static IEnumerable<(GalacticPosition at, string by)> Pilot(Galaxy galaxy, double jd)
        {
            if (galaxy.Viewpoint is GalacticPosition at) yield return (at, "you");
        }

        /// <summary>Every group between the stars, where it is now.</summary>
        public static IEnumerable<(GalacticPosition at, string by)> Cruises(Galaxy galaxy, double jd)
        {
            foreach (var c in galaxy.DeepSpace.Cruises)
                if (c.Leader != null) yield return (c.PositionAt(jd), "the " + c.Leader.Name);
        }
    }

    /// <summary>A star found (U.SPC-T456): which, where it was found from, by whom, and when.</summary>
    public sealed class StarDiscoveredEvent
    {
        public string Name;
        public DVec3 PositionLy;
        public string By;
        public double Jd;
    }

    public sealed partial class Galaxy
    {
        /// <summary>
        /// The stars of the real neighbourhood nobody has discovered yet (U.SPC-T456, see <see cref="DiscoveryState.Undiscovered"/>):
        /// held apart from the <see cref="NearbyStars"/>, so nothing that reads the charts (the interstellar view, the galaxy
        /// map, the FTL targets and courses, the surveys and cruises) sees them, until <see cref="Discover"/> moves one across.
        /// </summary>
        public IReadOnlyList<NearbyStar> Undiscovered => undiscovered;

        readonly List<NearbyStar> undiscovered = new List<NearbyStar>();

        /// <summary>The <see cref="Undiscovered"/> by name, for a quick look (the charts ask for every star they see).</summary>
        readonly HashSet<string> undiscoveredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The stars found since the start, in order (see <see cref="Discover"/>).</summary>
        public readonly List<StarDiscoveredEvent> Discoveries = new List<StarDiscoveredEvent>();

        /// <summary>Raised when a star is discovered (see <see cref="Discover"/>).</summary>
        public event Action<StarDiscoveredEvent> StarDiscovered;

        /// <summary>When the lookouts last looked for stars (NaN: never); they look every <see cref="CoarseStepDays"/>.</summary>
        double lookedJd = double.NaN;

        /// <summary>Whether the star or void station called <paramref name="name"/> is still undiscovered.</summary>
        public bool IsUndiscovered(string name)
        {
            return name != null && undiscoveredNames.Contains(name);
        }

        /// <summary>Whether the star called <paramref name="name"/> is known (<see cref="DiscoveryState.Discovered"/>) or not yet.</summary>
        public DiscoveryState DiscoveryOf(string name) => IsUndiscovered(name) ? DiscoveryState.Undiscovered : DiscoveryState.Discovered;

        /// <summary>
        /// Puts every system the real sources hold (Sol's neighbourhood out to <see cref="CatalogueStars.ChartedToLy"/>: the
        /// hand-built systems, the catalogue stars and the void stations; see <see cref="SystemSource.ChartedFromStart"/>) in the
        /// galaxy at the start (U.SPC-T456): those within <see cref="StarDiscovery.KnownToLy"/> of Sol charted among the
        /// <see cref="NearbyStars"/> (unless already running), the rest <see cref="Undiscovered"/>.
        /// </summary>
        public void SetUpNeighbourhood()
        {
            foreach (var e in SystemSources.StarsNear(DVec3.Zero, CatalogueStars.ChartedToLy + CatalogueStars.ClearLy, Seed, s => s.ChartedFromStart))
            {
                if (SimulatedAs(e) != null || ChartedStar(e.Name) || IsUndiscovered(e.Name)) continue;
                if (e.PositionLy.Length <= StarDiscovery.KnownToLy) NearbyStars.Add(e.ToNearbyStar());
                else
                {
                    undiscovered.Add(e.ToNearbyStar());
                    undiscoveredNames.Add(e.Name);
                }
            }
            ChartVersion++;
        }

        /// <summary>
        /// Discovers the star called <paramref name="name"/> (found by <paramref name="by"/>): it leaves the
        /// <see cref="Undiscovered"/> for the charts, where every view, course and voyage can see it from now on. False if it
        /// was not undiscovered.
        /// </summary>
        public bool Discover(string name, double jd, string by = null)
        {
            if (!IsUndiscovered(name)) return false;
            int i = undiscovered.FindIndex(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
            var star = undiscovered[i];
            undiscovered.RemoveAt(i);
            undiscoveredNames.Remove(star.Name);
            if (Find(star.Name) == null && !ChartedStar(star.Name)) NearbyStars.Add(star);
            var e = new StarDiscoveredEvent { Name = star.Name, PositionLy = star.PositionLy, By = by, Jd = jd };
            Discoveries.Add(e);
            DeepSpace.Log(jd, $"{star.Name} is discovered{(by != null ? " by " + by : "")}, " +
                              $"{star.PositionLy.Length.ToString("0.0", CultureInfo.InvariantCulture)} ly from Sol", star.Origin);
            ChartVersion++;
            StarDiscovered?.Invoke(e);
            return true;
        }

        /// <summary>Discovers every undiscovered star within <see cref="StarDiscovery.Range"/> of <paramref name="at"/>; how many it found.</summary>
        public int DiscoverAbout(GalacticPosition at, double jd, string by = null)
        {
            if (undiscovered.Count == 0) return 0;
            double range = StarDiscovery.Range.Value;
            var near = new List<string>();
            foreach (var n in undiscovered)
                if (n.Origin.DistanceLy(at) <= range) near.Add(n.Name);
            int found = 0;
            foreach (var name in near)
                if (Discover(name, jd, by)) found++;
            return found;
        }

        /// <summary>Every <see cref="CoarseStepDays"/>, each lookout (<see cref="StarDiscovery.Lookouts"/>) discovers what is within range of it.</summary>
        void LookForStars(double jd)
        {
            if (undiscovered.Count == 0) return;
            if (!double.IsNaN(lookedJd) && jd - lookedJd < CoarseStepDays && jd >= lookedJd) return;
            lookedJd = jd;
            foreach (var lookout in StarDiscovery.Lookouts.ToArray())
            foreach (var (at, by) in lookout(this, jd))
            {
                DiscoverAbout(at, jd, by);
                if (undiscovered.Count == 0) return;
            }
        }

        /// <summary>Takes the star called <paramref name="name"/> off the <see cref="Undiscovered"/> without news (it is running now); whether it was there.</summary>
        bool Unhide(string name)
        {
            if (!undiscoveredNames.Remove(name)) return false;
            undiscovered.RemoveAll(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
            ChartVersion++;
            return true;
        }
    }
}
