using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// One star on the galaxy's charts (U.SPC-T413): enough to show it, aim a cruise at it and tell it from its neighbours,
    /// without building it. Which <see cref="SystemSource"/> charted it (<see cref="Source"/>) turns it into a full catalogue
    /// entry (<see cref="SystemSpec"/>) when a pilot comes near enough for it to be simulated; the entry is made afresh each
    /// time from the galaxy's seed, so a star is the same star whenever and however often it is reached.
    /// </summary>
    public sealed class StarEntry
    {
        public string Name;

        /// <summary>Where it is, light-years from Sol's root (J2000 ecliptic axes, as every system's <see cref="StarSystem.PositionLy"/>).</summary>
        public DVec3 PositionLy;

        public string Spectral;
        public double MassSolar, RadiusSolar, LuminositySolar, TeffK;

        /// <summary>Visual magnitude as seen from Sol; NaN to work it out from the luminosity (see <see cref="ToNearbyStar"/>).</summary>
        public double ApparentMagnitude = double.NaN;

        /// <summary>The <see cref="SystemSource.Name"/> of the source that charted it, and so defines it.</summary>
        public string Source;

        /// <summary>The star's own seed, for a source that makes its system from one (the procedural galaxy).</summary>
        public ulong Seed;

        /// <summary>A source's own note of where the entry came from (a hand-built list's name, a catalogue's).</summary>
        public string Tag;

        public GalacticPosition Origin => GalacticPosition.FromLy(PositionLy);

        /// <summary>
        /// The plain star the charts show until it is simulated. A magnitude not given is worked out from the
        /// luminosity, as if it were all visual light (M = 4.83 - 2.5 log L, no bolometric correction): close enough
        /// for a dot's brightness, not a catalogue value.
        /// </summary>
        public NearbyStar ToNearbyStar()
        {
            double mag = ApparentMagnitude;
            double d = PositionLy.Length;
            if (double.IsNaN(mag) && LuminositySolar > 0 && d > 0)
                mag = 4.83 - 2.5 * Math.Log10(LuminositySolar) + 5 * (Math.Log10(d / 3.261563777) - 1);
            return new NearbyStar(Name, PositionLy, Spectral, mag, TeffK);
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Somewhere star systems come from (U.SPC-T413): the extension point every system outside Sol goes through, hand-built
    /// or generated. A source charts the stars it has near a place (<see cref="StarsNear"/>, cheap: nothing is built), turns
    /// one of them into a catalogue entry when it is wanted (<see cref="Define"/>), and builds that entry into a running
    /// system (<see cref="Build"/>, unsettled unless the source says otherwise). A source may claim a region
    /// (<see cref="Claims"/>), so that sources after it leave that space to it: the real stars near Sol keep the
    /// procedural galaxy out of their neighbourhood. New stars, regions and their content plug in by registering a
    /// source (<see cref="SystemSources.Register"/>) or a <see cref="SystemContent"/>, never by editing the galaxy.
    /// </summary>
    public abstract class SystemSource
    {
        /// <summary>Its name, unique among the sources: a charted star names its source by it.</summary>
        public abstract string Name { get; }

        /// <summary>Where it stands among the sources: lower goes first, wins a name both chart, and may claim space from the later ones.</summary>
        public virtual int Order => 500;

        /// <summary>Whether this source holds the space at <paramref name="ly"/>, so that later sources chart nothing there.</summary>
        public virtual bool Claims(DVec3 ly) => false;

        /// <summary>
        /// Whether its stars are a fixed map put in the galaxy whole at the start (U.SPC-T456, see <see cref="Galaxy.SetUpNeighbourhood"/>),
        /// known or undiscovered by their distance from Sol, rather than charted only as a pilot comes near: the real neighbourhood's
        /// sources, not the procedural galaxy.
        /// </summary>
        public virtual bool ChartedFromStart => false;

        /// <summary>The stars it has within <paramref name="radiusLy"/> of <paramref name="centreLy"/>, in the galaxy of <paramref name="seed"/>.</summary>
        public abstract IEnumerable<StarEntry> StarsNear(DVec3 centreLy, double radiusLy, int seed);

        /// <summary>A fresh catalogue entry for <paramref name="star"/> (one of its own), or null if it cannot define it.</summary>
        public abstract SystemSpec Define(StarEntry star, int seed);

        /// <summary>The running system made from <paramref name="spec"/>: empty and unsettled unless a source overrides it.</summary>
        public virtual StarSystem Build(StarEntry star, SystemSpec spec, double jd, Polities polities) =>
            StarSystem.Create(spec, jd, polities: polities);

        public override string ToString() => Name;
    }

    /// <summary>
    /// Content laid into every system a <see cref="SystemSource"/> defines (U.SPC-T413), whichever source it comes from:
    /// a derelict, a ruin, a station, a belt. It works on the fresh catalogue entry before it is built, so what it adds is
    /// built with the system; it should take any chance it needs from the star's seed, so the same star always gets it.
    /// </summary>
    public abstract class SystemContent
    {
        public abstract void Furnish(StarEntry star, SystemSpec spec, int seed);
    }

    /// <summary>
    /// The registered <see cref="SystemSource"/>s, in order (U.SPC-T413): the hand-built systems first
    /// (<see cref="HandBuiltSystems"/>), then the void stations bridging the gaps between the real stars (<see cref="VoidStations"/>)
    /// and the real catalogue stars near Sol (<see cref="CatalogueStars"/>), then the rest of the galaxy, generated from its seed (<see cref="ProceduralSystems"/>). Ask it for the stars near a place and
    /// it merges what every source charts there; ask it for one of them and the source that charted it builds it.
    /// </summary>
    public static class SystemSources
    {
        static readonly List<SystemSource> sources = new List<SystemSource>
        {
            new HandBuiltSystems(),
            new VoidStations(),
            new CatalogueStars(),
            new ProceduralSystems(),
        };

        static readonly object gate = new object();

        /// <summary>Content laid into every system the sources define (see <see cref="SystemContent"/>), in order.</summary>
        public static readonly List<SystemContent> Content = new List<SystemContent>();

        /// <summary>The sources, first to last.</summary>
        public static IReadOnlyList<SystemSource> All
        {
            get
            {
                lock (gate) return sources.ToArray();
            }
        }

        /// <summary>Adds <paramref name="source"/> in its <see cref="SystemSource.Order"/>, replacing any of the same name: how a new region plugs in.</summary>
        public static void Register(SystemSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            lock (gate)
            {
                sources.RemoveAll(s => string.Equals(s.Name, source.Name, StringComparison.OrdinalIgnoreCase));
                int at = 0;
                while (at < sources.Count && sources[at].Order <= source.Order) at++;
                sources.Insert(at, source);
            }
        }

        /// <summary>Takes the source called <paramref name="name"/> away; false if there was none.</summary>
        public static bool Unregister(string name)
        {
            lock (gate) return sources.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        /// <summary>The source called <paramref name="name"/>, or null.</summary>
        public static SystemSource Find(string name)
        {
            lock (gate)
            {
                foreach (var s in sources)
                    if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            }
            return null;
        }

        /// <summary>
        /// Every star within <paramref name="radiusLy"/> of <paramref name="centreLy"/> in the galaxy of <paramref name="seed"/>,
        /// nearest first: each source's in turn, a name already charted by an earlier source left to it, and nothing from a
        /// source in space an earlier one claims. The same place and seed always give the same stars.
        /// </summary>
        public static List<StarEntry> StarsNear(DVec3 centreLy, double radiusLy, int seed) => StarsNear(centreLy, radiusLy, seed, null);

        /// <summary>
        /// As <see cref="StarsNear(DVec3, double, int)"/>, from only the sources <paramref name="which"/> takes (all when null); a
        /// source left out still claims its space from the later ones.
        /// </summary>
        public static List<StarEntry> StarsNear(DVec3 centreLy, double radiusLy, int seed, Func<SystemSource, bool> which)
        {
            var list = new List<StarEntry>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var order = All;
            for (int i = 0; i < order.Count; i++)
            {
                if (which != null && !which(order[i])) continue;
                foreach (var e in order[i].StarsNear(centreLy, radiusLy, seed))
                {
                    if (e == null || (e.PositionLy - centreLy).Length > radiusLy || names.Contains(e.Name)) continue;
                    bool claimed = false;
                    for (int j = 0; j < i && !claimed; j++) claimed = order[j].Claims(e.PositionLy);
                    if (claimed) continue;
                    e.Source = order[i].Name;
                    names.Add(e.Name);
                    list.Add(e);
                }
            }
            list.Sort((a, b) =>
            {
                int c = (a.PositionLy - centreLy).Length.CompareTo((b.PositionLy - centreLy).Length);
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });
            return list;
        }

        /// <summary>The charted star called <paramref name="name"/> near <paramref name="nearLy"/> (within a light-year), or null.</summary>
        public static StarEntry Entry(string name, DVec3 nearLy, int seed)
        {
            foreach (var e in StarsNear(nearLy, 1, seed))
                if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        /// <summary>A fresh catalogue entry for <paramref name="star"/> from the source that charted it, furnished with the registered <see cref="Content"/>; null if none can define it.</summary>
        public static SystemSpec Define(StarEntry star, int seed)
        {
            var source = star == null ? null : Find(star.Source);
            var spec = source?.Define(star, seed);
            if (spec == null) return null;
            foreach (var c in Content.ToArray()) c.Furnish(star, spec, seed);
            return spec;
        }

        /// <summary><paramref name="star"/> built into a running system from <paramref name="jd"/>; null if it cannot be.</summary>
        public static StarSystem Build(StarEntry star, double jd, int seed, Polities polities = null)
        {
            var spec = Define(star, seed);
            return spec == null ? null : Find(star.Source).Build(star, spec, jd, polities);
        }

        // Pecaut and Mamajek 2013 (ApJS 208, 9; the 2022 online table): main-sequence temperature (K) against mass (solar), M8V to B0V.
        static readonly (double teff, double mass)[] MainSequence =
        {
            (2570, 0.079), (2680, 0.090), (2810, 0.102), (3060, 0.162), (3210, 0.23), (3430, 0.37), (3560, 0.44), (3660, 0.50),
            (3850, 0.57), (4440, 0.69), (5270, 0.88), (5770, 1.0), (6510, 1.21), (7220, 1.61), (8550, 2.0), (9700, 2.34),
            (15700, 4.4), (31400, 17),
        };

        /// <summary>
        /// The main-sequence mass (solar) of a star of <paramref name="teffK"/>, for a catalogue star listed by temperature
        /// only: read off <see cref="MainSequence"/> between its rows, held at its ends.
        /// </summary>
        public static double MainSequenceMass(double teffK)
        {
            if (teffK <= MainSequence[0].teff) return MainSequence[0].mass;
            for (int i = 1; i < MainSequence.Length; i++)
                if (teffK <= MainSequence[i].teff)
                {
                    var (t0, m0) = MainSequence[i - 1];
                    var (t1, m1) = MainSequence[i];
                    return m0 + (teffK - t0) / (t1 - t0) * (m1 - m0);
                }
            return MainSequence[MainSequence.Length - 1].mass;
        }

        /// <summary>The main-sequence radius (solar) of a star of <paramref name="massSolar"/>: M^0.8 below a solar mass, M^0.57 above.</summary>
        public static double MainSequenceRadius(double massSolar) =>
            massSolar < 1 ? Math.Pow(massSolar, 0.8) : Math.Pow(massSolar, 0.57);

        /// <summary>
        /// The catalogue entry for a lone star at <paramref name="star"/>'s place, with the orbit marked out for a
        /// settlers' habitat as the frontier stars have (<see cref="FrontierSystems.Habitat"/>), so a gate in opens it.
        /// </summary>
        public static SystemSpec LoneStar(StarEntry star, StarSpec spec, string summary)
        {
            double d = star.PositionLy.Length;
            double ra = 0, dec = 0;
            if (d > 0) Astro.RaDecOf(star.PositionLy, out ra, out dec);
            var s = new SystemSpec
            {
                Name = star.Name, NeighbourName = star.Name, RaDeg = ra, DecDeg = dec, DistanceLy = d,
                Primary = StellarNode.Single(spec), Summary = summary,
            };
            s.Stations.Add(FrontierSystems.Habitat(star.Name, spec));
            return s;
        }
    }

    /// <summary>
    /// The hand-built systems as a source (U.SPC-T413): Alpha Centauri, the nearest real systems
    /// (<see cref="NearbySystems"/>) and the frontier stars (<see cref="FrontierSystems"/>), each defined from its own
    /// hand-written entry and built the way it always was (Alpha Centauri and the nearby systems with their societies).
    /// </summary>
    public sealed class HandBuiltSystems : SystemSource
    {
        public const string SourceName = "Hand-built";
        public override string Name => SourceName;
        public override int Order => 0;
        public override bool ChartedFromStart => true;

        static List<StarEntry> stars;

        static IEnumerable<(string tag, SystemSpec spec)> Specs()
        {
            yield return ("alpha", AlphaCentauri.Spec());
            foreach (var s in NearbySystems.All()) yield return ("nearby", s);
            foreach (var s in FrontierSystems.All()) yield return ("frontier", s);
        }

        /// <summary>Every hand-built system as it is charted.</summary>
        public static IReadOnlyList<StarEntry> Charted() => Stars();

        static List<StarEntry> Stars()
        {
            if (stars != null) return stars;
            var list = new List<StarEntry>();
            foreach (var (tag, spec) in Specs())
            {
                StarSpec first = null;
                foreach (var st in spec.Stars())
                {
                    first = st;
                    break;
                }
                list.Add(new StarEntry
                {
                    Name = spec.NeighbourName ?? spec.Name, PositionLy = spec.PositionLy, Spectral = first?.Spectral,
                    MassSolar = spec.Primary.MassSolar, RadiusSolar = first?.RadiusSolar ?? 0, LuminositySolar = first?.LuminositySolar ?? 0,
                    TeffK = first?.TeffK ?? 0, Source = SourceName, Tag = tag,
                });
            }
            return stars = list;
        }

        public override IEnumerable<StarEntry> StarsNear(DVec3 centreLy, double radiusLy, int seed)
        {
            foreach (var e in Stars())
                if ((e.PositionLy - centreLy).Length <= radiusLy) yield return Copy(e);
        }

        static StarEntry Copy(StarEntry e) => new StarEntry
        {
            Name = e.Name, PositionLy = e.PositionLy, Spectral = e.Spectral, MassSolar = e.MassSolar, RadiusSolar = e.RadiusSolar,
            LuminositySolar = e.LuminositySolar, TeffK = e.TeffK, ApparentMagnitude = e.ApparentMagnitude, Source = e.Source, Tag = e.Tag,
        };

        public override SystemSpec Define(StarEntry star, int seed)
        {
            foreach (var (tag, spec) in Specs())
                if (string.Equals(spec.NeighbourName ?? spec.Name, star.Name, StringComparison.OrdinalIgnoreCase)) return spec;
            return null;
        }

        public override StarSystem Build(StarEntry star, SystemSpec spec, double jd, Polities polities)
        {
            if (spec.Name == "Alpha Centauri") return StarSystem.CreateAlphaCentauri(jd, polities);
            if (star.Tag == "nearby") return StarSystem.CreateSettled(spec, jd, polities);
            return StarSystem.Create(spec, jd, polities: polities);
        }
    }
}
