using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>Where a planet sits against its star's habitable zone (Kopparapu et al. 2013), see <see cref="HabitableZones"/>.</summary>
    public enum HabitableZone
    {
        /// <summary>Outside it, or not a planet.</summary>
        None,

        /// <summary>Inside the optimistic zone (recent Venus to early Mars) but not the conservative one.</summary>
        Optimistic,

        /// <summary>Inside the conservative zone (runaway to maximum greenhouse).</summary>
        Conservative,
    }

    /// <summary>One star (or brown dwarf) of a catalogued system: measured values only, each sourced in Systems/SOURCES.md.</summary>
    public sealed class StarSpec
    {
        public string Name;
        public string Spectral;
        public double MassSolar;
        public double RadiusSolar;
        public double LuminositySolar;
        public double TeffK;

        /// <summary>Rotation period, days (assumed where the summary says so).</summary>
        public double RotationDays;

        public string Summary;
    }

    /// <summary>
    /// A visual binary's relative orbit: the secondary about the primary, in Campbell elements on the
    /// sky (inclination, position angle of the node, argument of periastron) as orbit papers give them.
    /// </summary>
    public sealed class BinaryOrbitSpec
    {
        public double PeriodYears;
        public double AAu;
        public double E;
        public double IDeg;
        public double NodeDeg;
        public double ArgPeriDeg;

        /// <summary>Year of periastron (Julian years from J2000, as the papers' decimal years are read).</summary>
        public double PeriastronYear;
    }

    /// <summary>
    /// Either one star or a bound pair circling its own barycentre (A and B on a shared relative
    /// orbit). A pair's barycentre is an invisible body named <see cref="Name"/>.
    /// </summary>
    public sealed class StellarNode
    {
        public StarSpec Star;

        public string Name;
        public StarSpec A, B;
        public BinaryOrbitSpec Orbit;

        /// <summary>The barycentre's description; null for a plain one.</summary>
        public string Summary;

        public bool IsPair => Star == null;
        public double MassSolar => IsPair ? A.MassSolar + B.MassSolar : Star.MassSolar;

        public static StellarNode Single(StarSpec star) => new StellarNode { Star = star };

        public static StellarNode Pair(string name, StarSpec a, StarSpec b, BinaryOrbitSpec orbit, string summary = null) =>
            new StellarNode { Name = name, A = a, B = b, Orbit = orbit, Summary = summary };
    }

    /// <summary>
    /// A wide companion (Proxima about Alpha Centauri A-B, the Epsilon Indi B pair about A), on an
    /// orbit through where it is seen today. Its offset from the primary comes either from its own
    /// sky position and distance, or from a projected separation; the orbit's plane is assumed.
    /// </summary>
    public sealed class CompanionSpec
    {
        public StellarNode Node;

        /// <summary>The companion's own sky position (ICRS) and distance; NaN to use a projected separation instead.</summary>
        public double RaDeg = double.NaN, DecDeg = double.NaN, DistanceLy = double.NaN;

        /// <summary>Projected separation (AU), used when the sky position is not given.</summary>
        public double SeparationAu = double.NaN;

        /// <summary>Position angle on the sky (degrees east of north) of the projected separation; assumed unless stated.</summary>
        public double PositionAngleDeg;

        public double E;

        /// <summary>Mean anomaly today (degrees): 180 puts the companion at apastron.</summary>
        public double MeanAnomalyDeg = 180;
    }

    /// <summary>A planet as the catalogue lists it. NaN means not measured (the builder assumes, and the summary says so).</summary>
    public sealed class PlanetSpec
    {
        public string Name;

        /// <summary>The star it orbits.</summary>
        public string Host;

        public double PeriodDays;
        public double AAu;
        public double E;
        public double MassEarth;

        /// <summary>The mass is m sin i (radial velocity only), a lower limit.</summary>
        public bool MassIsMinimum = true;

        /// <summary>Radius in Earth radii (the NASA Exoplanet Archive's mass-radius estimate for planets never seen to transit).</summary>
        public double RadiusEarth;

        /// <summary>Inclination to the sky plane; NaN for the system's reference plane.</summary>
        public double IDeg = double.NaN;

        /// <summary>Position angle of the node on the sky; NaN for the system's (assumed) one.</summary>
        public double NodeDeg = double.NaN;

        /// <summary>Argument of periastron; NaN when unmeasured (0 is used).</summary>
        public double ArgPeriDeg = double.NaN;

        /// <summary>Time of periastron (JD); sets the phase when known.</summary>
        public double PeriastronJd = double.NaN;

        /// <summary>Mean anomaly at J2000 (degrees) when the phase is unknown; NaN picks a fixed one from the name.</summary>
        public double MeanAnomalyDeg = double.NaN;

        /// <summary>Tilt of the orbit (degrees) out of the system's reference plane, about the line across the binary's periastron (Alpha Centauri Ab).</summary>
        public double TiltFromReferenceDeg;

        public BodyStatus Status = BodyStatus.Confirmed;
        public int DiscoveryYear;

        /// <summary>Short reference for the values ("NASA Exoplanet Archive; Feng et al. 2017").</summary>
        public string Source;

        public string Summary;

        // Looks (assumed): left at their defaults the builder picks them from the planet's mass and warmth.
        public Rgb? Color;
        public bool? GasGiant;
        public bool? TidallyLocked;
        public double RotationHours = double.NaN;
        public Rgb AtmosphereColor;
        public float AtmosphereStrength;
    }

    /// <summary>A debris belt or disc, a ring of dust and planetesimals about one star.</summary>
    public sealed class DiscSpec
    {
        public string Name;
        public string Host;
        public double InnerAu;
        public double OuterAu;

        /// <summary>Inclination to the sky plane (measured for resolved discs); NaN for the reference plane.</summary>
        public double IDeg = double.NaN;

        public string Source;
        public string Summary;
    }

    /// <summary>An invented station on its own circular orbit about a star (game layer).</summary>
    public sealed class StationSpec
    {
        public string Name;
        public string Host;
        public double AAu;
        public double MeanAnomalyDeg;
        public string Summary;

        /// <summary>
        /// Not a station yet but the orbit marked out for one (U.SPC-T293): a frontier star's habitat site, where the
        /// first colony ship through its gate builds its settlers' home (see <see cref="ColonySite.Survey"/>).
        /// </summary>
        public bool HabitatSite;
    }

    /// <summary>
    /// A real star system as data: where it is, its stars, its catalogued planets and discs, and any
    /// invented stations. <see cref="StarSystemBuilder"/> turns it into bodies. Values are sourced in
    /// Systems/SOURCES.md; only what the planet and station summaries call assumed or fiction is not.
    /// </summary>
    public sealed class SystemSpec
    {
        public string Name;

        /// <summary>The primary's sky position (ICRS J2000) and distance (light-years, from the parallax).</summary>
        public double RaDeg, DecDeg, DistanceLy;

        /// <summary>The name <see cref="Galaxy.Neighbours"/> lists it under, so the simulated system replaces the plain star there.</summary>
        public string NeighbourName;

        public StellarNode Primary;
        public readonly List<CompanionSpec> Companions = new List<CompanionSpec>();
        public readonly List<PlanetSpec> Planets = new List<PlanetSpec>();
        public readonly List<DiscSpec> Discs = new List<DiscSpec>();
        public readonly List<StationSpec> Stations = new List<StationSpec>();

        /// <summary>
        /// The plane assumed for everything whose orientation is not measured, as an inclination to the
        /// sky and a node position angle. A pair at the root overrides it with its own orbit plane.
        /// 60 degrees is the median inclination of randomly oriented orbits.
        /// </summary>
        public double ReferenceIDeg = 60, ReferenceNodeDeg;

        /// <summary>How much of the system the overview frames (AU); NaN to work it out from the planets.</summary>
        public double OverviewSpanAu = double.NaN;

        public string Summary;

        public DVec3 PositionLy => Astro.DirectionFromRaDec(RaDeg, DecDeg) * DistanceLy;

        /// <summary>Distance in light-years from a parallax in milliarcseconds.</summary>
        public static double LyFromParallax(double mas) => 1000.0 / mas * 3.2615637769;

        /// <summary>A visual binary's angular semi-major axis (arcsec) in AU at a parallax (mas).</summary>
        public static double AuFromArcsec(double arcsec, double parallaxMas) => arcsec / (parallaxMas / 1000.0);

        public IEnumerable<StarSpec> Stars()
        {
            foreach (var s in NodeStars(Primary)) yield return s;
            foreach (var c in Companions)
                foreach (var s in NodeStars(c.Node)) yield return s;
        }

        static IEnumerable<StarSpec> NodeStars(StellarNode n)
        {
            if (n.IsPair)
            {
                yield return n.A;
                yield return n.B;
            }
            else yield return n.Star;
        }
    }

    /// <summary>
    /// The habitable zone of Kopparapu et al. 2013 (ApJ 765, 131; corrected coefficients of the 2013
    /// erratum): the stellar flux, relative to Earth's, at which an Earth-like planet's water runs away
    /// (inner edge) or its CO2 greenhouse can no longer keep it above freezing (outer edge), as a
    /// polynomial in the star's temperature (fit for 2600-7200 K; cooler or hotter stars are clamped).
    /// The optimistic edges (recent Venus 1.7763, early Mars 0.3207 at the Sun) are scaled from the
    /// conservative ones by their solar ratios, an approximation.
    /// </summary>
    public static class HabitableZones
    {
        /// <summary>Effective flux (Earth = 1) at the runaway-greenhouse inner edge.</summary>
        public static double RunawayFlux(double teffK) => Seff(teffK, 1.0385, 1.2456e-4, 1.4612e-8, -7.6345e-12, -1.7511e-15);

        /// <summary>Effective flux (Earth = 1) at the maximum-greenhouse outer edge.</summary>
        public static double MaxGreenhouseFlux(double teffK) => Seff(teffK, 0.3507, 5.9578e-5, 1.6707e-9, -3.0058e-12, -5.1925e-16);

        const double RecentVenusRatio = 1.7763 / 1.0385, EarlyMarsRatio = 0.3207 / 0.3507;

        static double Seff(double teffK, double s0, double a, double b, double c, double d)
        {
            double t = Math.Max(2600, Math.Min(7200, teffK)) - 5780;
            return s0 + a * t + b * t * t + c * t * t * t + d * t * t * t * t;
        }

        /// <summary>The orbit-averaged flux a planet receives, Earth = 1: L / (a^2 sqrt(1 - e^2)).</summary>
        public static double MeanFlux(double luminositySolar, double aAu, double e) => luminositySolar / (aAu * aAu * Math.Sqrt(1 - e * e));

        public static HabitableZone Classify(double luminositySolar, double teffK, double aAu, double e)
        {
            double s = MeanFlux(luminositySolar, aAu, e);
            double inner = RunawayFlux(teffK), outer = MaxGreenhouseFlux(teffK);
            if (s <= inner && s >= outer) return HabitableZone.Conservative;
            if (s <= inner * RecentVenusRatio && s >= outer * EarlyMarsRatio) return HabitableZone.Optimistic;
            return HabitableZone.None;
        }

        /// <summary>The conservative zone's inner and outer edges (AU) for a star.</summary>
        public static void ConservativeEdgesAu(double luminositySolar, double teffK, out double innerAu, out double outerAu)
        {
            innerAu = Math.Sqrt(luminositySolar / RunawayFlux(teffK));
            outerAu = Math.Sqrt(luminositySolar / MaxGreenhouseFlux(teffK));
        }
    }
}
