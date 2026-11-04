using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The settings menu's control of how much of the galaxy is charted and simulated about the pilot (U.SPC-T413, see
    /// <see cref="Galaxy.Explore"/>).
    /// </summary>
    [SettingsSource]
    public static class GalaxyStreaming
    {
        /// <summary>How far about the pilot stars are charted: shown in the interstellar view and open to a cruise.</summary>
        public static readonly RangeSetting ChartRadius = new RangeSetting("streaming.chartRadiusLy", SettingGroups.Streaming,
            "Star chart radius", 20, 5, 60, 1,
            "How far about you (light-years) the galaxy's stars are charted: shown between the systems and open to a cruise drive. " +
            "Farther charts more stars.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " ly", "galaxy stars chart streaming radius procedural");

        /// <summary>How near the pilot a charted star is built into a running system.</summary>
        public static readonly RangeSetting SystemRadius = new RangeSetting("streaming.systemRadiusLy", SettingGroups.Streaming,
            "System streaming radius", 3, 0, 12, 0.5,
            "How near you (light-years) a charted star is built into a full star system that runs with the rest. A system built " +
            "this way that nobody has gone to or gated is let go again once you are far off, and comes back the same.",
            v => v.ToString("0.#", CultureInfo.InvariantCulture) + " ly", "galaxy systems streaming radius simulate load");

        /// <summary>The most charted stars kept at once, nearest first.</summary>
        public static readonly RangeSetting MaxCharted = new RangeSetting("streaming.maxChartedStars", SettingGroups.Streaming,
            "Charted stars", 400, 50, 3000, 50,
            "The most stars kept on the charts at once, the nearest to you first (the galaxy's core is far denser than anywhere near Sol).",
            v => v.ToString("0", CultureInfo.InvariantCulture), "galaxy stars chart count budget");
    }

    public sealed partial class Galaxy
    {
        /// <summary>The seed the galaxy's own stars are made from, past the real ones near Sol (U.SPC-T413).</summary>
        public const int DefaultSeed = 1977;

        /// <summary>This galaxy's seed: the same seed is the same galaxy, star for star, wherever it is explored from.</summary>
        public int Seed = DefaultSeed;

        /// <summary>
        /// Where the pilot is (U.SPC-T413): the galaxy is charted about it and built near it as it moves
        /// (<see cref="Explore"/>), every <see cref="ExploreStepLy"/> it goes. Null charts nothing beyond what the galaxy began with.
        /// </summary>
        public GalacticPosition? Viewpoint;

        /// <summary>How far the <see cref="Viewpoint"/> moves before the charts are brought up to date, light-years.</summary>
        public const double ExploreStepLy = 0.25;

        /// <summary>A star charted or a system built this far beyond the chart's radius is let go again (if nothing holds it).</summary>
        public const double ForgetMarginLy = 5;

        /// <summary>Where the charts were last brought up to date from (NaN: never).</summary>
        DVec3 exploredAtLy = new DVec3(double.NaN, double.NaN, double.NaN);

        /// <summary>The stars put on the charts by exploring (the rest were there from the start and stay).</summary>
        readonly HashSet<string> charted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The systems built by exploring, and so let go again when left untouched far behind.</summary>
        readonly HashSet<string> streamed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Counts every change to the charts or the systems explored in, so a view of them knows to rebuild.</summary>
        public int ChartVersion { get; private set; }

        /// <summary>Raised when a system built by exploring is let go (see <see cref="Explore"/>): (the system, when).</summary>
        public event Action<StarSystem, double> SystemRemoved;

        /// <summary>The systems built on demand by exploring that are running now.</summary>
        public IEnumerable<string> Streamed => streamed;

        /// <summary>
        /// Brings the charts up to date about <paramref name="atLy"/> (light-years from Sol): every star the
        /// <see cref="SystemSources"/> have within <see cref="GalaxyStreaming.ChartRadius"/> is charted (up to
        /// <see cref="GalaxyStreaming.MaxCharted"/>, nearest first) as a plain star among the <see cref="NearbyStars"/>; any
        /// within <see cref="GalaxyStreaming.SystemRadius"/> is built into a running system (<see cref="Realize(StarEntry, double)"/>);
        /// and what exploring put on the charts or built that is now well beyond them, and that nothing holds, is let go.
        /// Everything comes from the seed, so a star let go and reached again is the same star.
        /// </summary>
        public void Explore(DVec3 atLy, double jd)
        {
            exploredAtLy = atLy;
            double chartLy = GalaxyStreaming.ChartRadius.Value, systemLy = GalaxyStreaming.SystemRadius.Value;
            int cap = GalaxyStreaming.MaxCharted.IntValue;
            bool changed = false;

            // Let go first, so what is charted next has the room.
            double forgetLy = chartLy + ForgetMarginLy;
            if (NearbyStars.RemoveAll(n => charted.Contains(n.Name) && (n.PositionLy - atLy).Length > forgetLy && !Held(n.Name)) > 0)
            {
                charted.RemoveWhere(name => !NearbyStars.Exists(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)));
                changed = true;
            }
            foreach (var s in Systems.ToArray())
                if (streamed.Contains(s.Name) && (s.PositionLy - atLy).Length > Math.Max(systemLy, 1) + ForgetMarginLy && CanRelease(s))
                {
                    Release(s, jd);
                    changed = true;
                }

            // The pilot finds what is undiscovered about them first (U.SPC-T456); what is still undiscovered is charted and built by nobody.
            if (DiscoverAbout(GalacticPosition.FromLy(atLy), jd, "you") > 0) changed = true;

            int count = 0;
            foreach (var n in NearbyStars)
                if (charted.Contains(n.Name)) count++;
            foreach (var e in SystemSources.StarsNear(atLy, chartLy, Seed))
            {
                if (IsUndiscovered(e.Name) || SimulatedAs(e) != null) continue;
                if ((e.PositionLy - atLy).Length <= systemLy)
                {
                    if (Realize(e, jd) != null) changed = true;
                    continue;
                }
                if (ChartedStar(e.Name) || count >= cap) continue;
                NearbyStars.Add(e.ToNearbyStar());
                charted.Add(e.Name);
                count++;
                changed = true;
            }
            if (changed) ChartVersion++;
        }

        /// <summary>Brings the charts up to date about the <see cref="Viewpoint"/> if it has moved <see cref="ExploreStepLy"/> since they were.</summary>
        void ExploreIfMoved(double jd)
        {
            if (!(Viewpoint is GalacticPosition at)) return;
            var ly = at.Ly;
            if (double.IsNaN(exploredAtLy.X) || (ly - exploredAtLy).Length >= ExploreStepLy) Explore(ly, jd);
        }

        bool ChartedStar(string name)
        {
            foreach (var n in NearbyStars)
                if (string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The running system <paramref name="e"/> already is (by name, by the star it replaced, or by place), or null.</summary>
        public StarSystem SimulatedAs(StarEntry e)
        {
            foreach (var s in Systems)
            {
                if (string.Equals(s.Name, e.Name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.Spec?.NeighbourName, e.Name, StringComparison.OrdinalIgnoreCase)
                    || (s.PositionLy - e.PositionLy).Length < 0.01) return s;
            }
            return null;
        }

        /// <summary>
        /// The charted star called <paramref name="name"/> built into a running system from <paramref name="jd"/> (a pilot
        /// bound for it, or come near): the system it already is if it is one; null if no star of that name is charted.
        /// </summary>
        public StarSystem Realize(string name, double jd)
        {
            var existing = Find(name);
            if (existing != null) return existing;
            foreach (var n in NearbyStars)
                if (string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    var e = SystemSources.Entry(n.Name, n.PositionLy, Seed);
                    return e == null ? null : Realize(e, jd);
                }
            return null;
        }

        /// <summary>
        /// <paramref name="e"/> built into a running system from <paramref name="jd"/> by the source that charted it, joined to
        /// the galaxy (<see cref="AddSystem"/>) in place of the plain star it was shown as; the system it already is, if it is one.
        /// </summary>
        public StarSystem Realize(StarEntry e, double jd)
        {
            var existing = SimulatedAs(e);
            if (existing != null) return existing;
            // Nobody builds or goes to a star nobody knows is there (U.SPC-T456).
            if (IsUndiscovered(e.Name)) return null;
            var system = SystemSources.Build(e, jd, Seed, Polities);
            if (system == null) return null;
            streamed.Add(system.Name);
            charted.Remove(e.Name);
            AddSystem(system, jd);
            ChartVersion++;
            return system;
        }

        /// <summary>
        /// Whether a system built by exploring can be let go: it is not where the player is, no gate leads in or is being
        /// built to it, nobody has settled it, no ship is in it, and no ship between the stars is bound for it.
        /// </summary>
        bool CanRelease(StarSystem s)
        {
            if (s == Focus || s.Gates.Count > 0 || s.Settled || s.World.Fleet.Ships.Count > 0) return false;
            foreach (var c in DeepSpace.Cruises)
                if (c.To == s) return false;
            if (Construction != null)
                foreach (var p in Construction.Projects)
                    if (p.Active && (p.To == s || p.From == s)) return false;
            return true;
        }

        /// <summary>Takes a system built by exploring out of the galaxy again, back to the plain star it was built from.</summary>
        void Release(StarSystem s, double jd)
        {
            Systems.Remove(s);
            streamed.Remove(s.Name);
            SystemRemoved?.Invoke(s, jd);
        }

        /// <summary>Whether a ship is bound for the plain star called <paramref name="name"/>, between the stars or on its way out to it.</summary>
        bool Held(string name)
        {
            foreach (var c in DeepSpace.Cruises)
                if (string.Equals(c.ToName, name, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var s in Systems)
                foreach (var ship in s.World.Fleet.Ships)
                    if (ship.Voyage != null && string.Equals(ship.Voyage.StarName, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
