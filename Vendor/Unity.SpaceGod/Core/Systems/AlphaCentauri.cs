using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The Alpha Centauri system, the Sun's nearest neighbour: the binary Alpha Centauri A and B
    /// circling their barycentre every 80 years, the red dwarf Proxima Centauri 13,000 AU away on a
    /// half-million-year orbit around the pair, and the planets found so far. All positions are in
    /// the same J2000 ecliptic axes as the Sol system, with the A-B barycentre at the origin. Built
    /// from its catalogue entry (<see cref="Spec"/>) by <see cref="StarSystemBuilder"/>, like every
    /// other nearby system.
    ///
    /// Sources (see Core/Systems/SOURCES.md):
    ///  - A-B orbit, masses, distance: Akeson et al. 2021, AJ 162, 14 (arXiv 2104.10086).
    ///  - Radii, luminosities, temperatures, rotation: Wikipedia "Alpha Centauri", "Proxima Centauri"
    ///    and the papers they cite.
    ///  - Proxima's orbit around A-B: Kervella, Thévenin and Lovis 2017, A&amp;A 598, L7 (arXiv 1611.03495).
    ///  - Proxima b and d: NASA Exoplanet Archive; c (Damasso et al. 2020) is disputed and no longer listed there.
    ///  - Alpha Cen A "S1": Beichman et al. 2025, ApJL (JWST/MIRI, August 2024), a candidate not yet re-detected.
    ///
    /// What is not known is assumed and said so in each summary: the planets' orbital planes and
    /// phases (only periods, separations and minimum masses are measured), the stars' spin poles,
    /// and the orientation of Proxima's orbital plane. The stations are game-layer fiction.
    /// </summary>
    public static class AlphaCentauri
    {
        public const double SunGm = StarSystemBuilder.SunGm;
        const double EarthRadiusKm = StarSystemBuilder.EarthRadiusKm;
        const double JupiterRadiusKm = 71492;

        public const double MassA = 1.0788, MassB = 0.9092, MassProxima = 0.1221;

        /// <summary>Distance of the A-B pair (light-years, Akeson et al. 2021) and of Proxima (light-years).</summary>
        public const double DistanceLy = 4.344, ProximaDistanceLy = 4.2465;

        /// <summary>Alpha Centauri A's position on the sky (ICRS J2000).</summary>
        public static readonly double RaDeg = Astro.Hms(14, 39, 36.494), DecDeg = Astro.Dms(60, 50, 2.37, south: true);

        /// <summary>Proxima Centauri's position on the sky (ICRS J2000).</summary>
        public static readonly double ProximaRaDeg = Astro.Hms(14, 29, 42.946), ProximaDecDeg = Astro.Dms(62, 40, 46.16, south: true);

        /// <summary>A-B relative orbit (Akeson et al. 2021): Campbell elements of B about A (a = 17.493" at 1.3319 pc).</summary>
        public static readonly BinaryOrbitSpec Orbit = new BinaryOrbitSpec
        {
            PeriodYears = 79.762, AAu = 23.299, E = 0.51947, IDeg = 79.243, NodeDeg = 205.073, ArgPeriDeg = 231.519, PeriastronYear = 1875.66,
        };

        /// <summary>Unit vector (ecliptic J2000) from the Sun toward the A-B pair.</summary>
        public static DVec3 Direction => Astro.DirectionFromRaDec(RaDeg, DecDeg);

        /// <summary>Position of the A-B pair relative to the Sun, light-years (ecliptic J2000).</summary>
        public static DVec3 PositionLy => Direction * DistanceLy;

        /// <summary>Proxima's position relative to the A-B pair today, km (ecliptic J2000), from the two stars' sky positions and distances.</summary>
        public static DVec3 ProximaOffsetKm =>
            (Astro.DirectionFromRaDec(ProximaRaDeg, ProximaDecDeg) * ProximaDistanceLy - PositionLy) * Astro.LightYearKm;

        /// <summary>The normal of the A-B orbit (ecliptic J2000), also used as the assumed pole of the stars and of Proxima's planets.</summary>
        public static DVec3 BinaryNormal
        {
            get
            {
                OrbitalElements.VisualBinaryGeometry(RaDeg, DecDeg, Orbit.IDeg, Orbit.NodeDeg, Orbit.ArgPeriDeg, out _, out var normal);
                return normal;
            }
        }

        /// <summary>Alpha Centauri's catalogue entry.</summary>
        public static SystemSpec Spec()
        {
            var a = new StarSpec
            {
                Name = "Alpha Centauri A", Spectral = "G2V", MassSolar = MassA, RadiusSolar = 1.223, LuminositySolar = 1.5059, TeffK = 5748, RotationDays = 28.3,
                Summary = "Rigil Kentaurus. G2V, 1.08 solar masses, half again as luminous as the Sun. Pole assumed along the A-B orbit's.",
            };
            var b = new StarSpec
            {
                Name = "Alpha Centauri B", Spectral = "K1V", MassSolar = MassB, RadiusSolar = 0.864, LuminositySolar = 0.4981, TeffK = 5154, RotationDays = 36.7,
                Summary = "Toliman. K1V, an orange dwarf. Swings between 11.2 and 35.4 AU from A on an 80-year orbit. "
                          + "No planet confirmed (the 2012 \"Bb\" was refuted).",
            };
            var proxima = new StarSpec
            {
                Name = "Proxima Centauri", Spectral = "M5.5Ve", MassSolar = MassProxima, RadiusSolar = 0.1542, LuminositySolar = 0.001567, TeffK = 2992, RotationDays = 84.9,
                Summary = "The nearest star to the Sun: an M5.5 red dwarf flare star, 4.25 light-years away. "
                          + "Orbits A and B every ~500,000 years; its orbital plane is assumed.",
            };

            var spec = new SystemSpec
            {
                Name = "Alpha Centauri", RaDeg = RaDeg, DecDeg = DecDeg, DistanceLy = DistanceLy,
                Primary = StellarNode.Pair("Alpha Centauri", a, b, Orbit, "The centre of mass that Alpha Centauri A and B circle every 79.8 years."),
                OverviewSpanAu = 22,
                Summary = "The Sun's nearest neighbour: a Sun-like binary and a red dwarf a fifth of a light-year out.",
            };

            // Proxima: on a 0.5-eccentricity orbit about the pair, now near apastron (Kervella et al. 2017:
            // P ≈ 550 kyr, periastron in ~280 kyr), placed where its sky position and distance put it, at
            // mean anomaly 174°. The orbital plane's orientation is assumed (the A-B plane, tipped to contain the offset).
            spec.Companions.Add(new CompanionSpec
            {
                Node = StellarNode.Single(proxima), RaDeg = ProximaRaDeg, DecDeg = ProximaDecDeg, DistanceLy = ProximaDistanceLy,
                E = 0.50, MeanAnomalyDeg = 173.7,
            });

            // Proxima's planets: periods, separations, eccentricities and minimum masses from the NASA
            // Exoplanet Archive (radii are its mass-radius estimates); the plane (the A-B plane) and phases are assumed.
            spec.Planets.Add(new PlanetSpec
            {
                Name = "Proxima d", Host = "Proxima Centauri", PeriodDays = 5.12338, AAu = 0.02881, E = 0, MassEarth = 0.26, RadiusEarth = 0.692,
                MeanAnomalyDeg = 40, DiscoveryYear = 2025, Source = "NASA Exoplanet Archive (Suárez Mascareño et al. 2025)",
                Color = new Rgb(0.55f, 0.42f, 0.36f),
                Summary = "Smallest known planet of Proxima, a quarter of Earth's mass at least, scorched on a 5.1-day orbit. "
                          + "Confirmed in 2025. Radius estimated; orbital plane and phase assumed.",
            });
            spec.Planets.Add(new PlanetSpec
            {
                Name = "Proxima b", Host = "Proxima Centauri", PeriodDays = 11.18465, AAu = 0.04848, E = 0, MassEarth = 1.055, RadiusEarth = 1.02,
                MeanAnomalyDeg = 210, DiscoveryYear = 2016, Source = "NASA Exoplanet Archive (Suárez Mascareño et al. 2025)",
                Color = new Rgb(0.5f, 0.45f, 0.42f), AtmosphereColor = new Rgb(0.75f, 0.55f, 0.5f), AtmosphereStrength = 0.3f,
                Summary = "At least 1.06 Earth masses, probably tidally locked: one face in endless day. "
                          + "Discovered 2016. Radius estimated, atmosphere unknown (drawn thin); orbital plane and phase assumed.",
            });
            spec.Planets.Add(new PlanetSpec
            {
                Name = "Proxima c", Host = "Proxima Centauri", PeriodDays = 1928, AAu = 1.489, E = 0.04, MassEarth = 7, RadiusEarth = 1.6,
                MeanAnomalyDeg = 95, Status = BodyStatus.Disputed, DiscoveryYear = 2020, Source = "Damasso et al. 2020",
                Color = new Rgb(0.72f, 0.76f, 0.8f), RotationHours = 30,
                Summary = "DISPUTED. A cold super-Earth claimed in 2020 on a 5.3-year orbit; later data have not confirmed it. "
                          + "Radius guessed from its 7 Earth masses.",
            });

            // The JWST candidate around A, inclined to the A-B plane (Beichman et al. 2025: a ≈ 1-2 AU,
            // e ≈ 0.4, inclined orbit; one of the allowed solutions is used).
            spec.Planets.Add(new PlanetSpec
            {
                Name = "Alpha Centauri Ab", Host = "Alpha Centauri A", AAu = 1.9, E = 0.4,
                PeriodDays = OrbitalElements.PeriodFromGm(1.9 * Astro.AuKm, SunGm * MassA),
                MassEarth = 120, MassIsMinimum = false, RadiusEarth = 1.05 * JupiterRadiusKm / EarthRadiusKm,
                TiltFromReferenceDeg = 50, MeanAnomalyDeg = 300, Status = BodyStatus.Candidate, DiscoveryYear = 2025, Source = "Beichman et al. 2025",
                Color = new Rgb(0.78f, 0.72f, 0.62f), GasGiant = true, RotationHours = 10.5,
                AtmosphereColor = new Rgb(0.85f, 0.78f, 0.65f), AtmosphereStrength = 0.3f,
                Summary = "CANDIDATE \"S1\": a Saturn-mass gas giant glimpsed by JWST in August 2024, "
                          + "not yet seen again. Orbit is one of the solutions allowed (a 1.9 AU, e 0.4, tilted 50°).",
            });

            // Game layer: free-flying stations on their own orbits about each star.
            spec.Stations.Add(new StationSpec { Name = "Rigil Gate", Host = "Alpha Centauri A", AAu = 1.2, MeanAnomalyDeg = 20,
                Summary = "Fiction. The Toliman Combine's trade hub in A's habitable zone, where the long lanes meet." });
            spec.Stations.Add(new StationSpec { Name = "Toliman Forge", Host = "Alpha Centauri B", AAu = 0.7, MeanAnomalyDeg = 140,
                Summary = "Fiction. Orbital foundry and shipyard of the Toliman Combine, in B's habitable zone." });
            spec.Stations.Add(new StationSpec { Name = "Blackhollow", Host = "Proxima Centauri", AAu = 0.5, MeanAnomalyDeg = 260,
                Summary = "Fiction. A derelict survey station the Long Dark took over, dark against a dim star." });
            return spec;
        }

        public static IEnumerable<BodyDefinition> Definitions() => StarSystemBuilder.Build(Spec()).Bodies;

        /// <summary>
        /// Approximate visible colour of a blackbody photosphere (normalised so the brightest channel
        /// is 1), from a fit to the CIE colour of Planck spectra (Tanner Helland's approximation).
        /// </summary>
        public static Rgb StarColor(double teffK)
        {
            double t = teffK / 100;
            double r, g, b;
            if (t <= 66)
            {
                r = 255;
                g = 99.4708025861 * Math.Log(t) - 161.1195681661;
                b = t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            }
            else
            {
                r = 329.698727446 * Math.Pow(t - 60, -0.1332047592);
                g = 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
                b = 255;
            }
            float C(double v) => (float)(Math.Max(0, Math.Min(255, v)) / 255);
            return new Rgb(C(r), C(g), C(b));
        }
    }
}
