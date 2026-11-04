using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The nearest real star systems beyond Alpha Centauri, as catalogue entries for
    /// <see cref="StarSystemBuilder"/>. Every number is sourced in Core/Systems/SOURCES.md:
    ///  - positions and parallaxes, stellar masses, radii, temperatures, luminosities and rotation:
    ///    the Wikipedia article of each star (from Gaia DR3 and the papers it cites), the NASA
    ///    Exoplanet Archive's stellar values where an article gives none;
    ///  - planets: the NASA Exoplanet Archive's composite table (pscomppars, queried 2026-10-16):
    ///    period, semi-major axis, eccentricity, mass (minimum mass for radial-velocity planets),
    ///    the archive's radius estimate, argument and time of periastron where listed;
    ///  - candidates the archive does not list (or lists from one unconfirmed detection) come from
    ///    their discovery papers and are flagged <see cref="BodyStatus.Candidate"/>;
    ///  - binary orbits and debris discs: the orbit and disc papers named beside them.
    /// Orbital planes and phases nobody has measured are assumed and say so. The societies settled on
    /// them (<see cref="NearbySocieties"/>) are fiction, kept apart from these entries.
    /// </summary>
    public static class NearbySystems
    {
        const double JupiterMassSolar = 9.547919e-4;

        /// <summary>Every catalogued system beyond Alpha Centauri, nearest first.</summary>
        public static IEnumerable<SystemSpec> All()
        {
            yield return BarnardsStar();
            yield return Luhman16();
            yield return Wolf359();
            yield return Lalande21185();
            yield return Sirius();
            yield return EpsilonEridani();
            yield return Ross128();
            yield return EpsilonIndi();
            yield return TauCeti();
            yield return Gj1061();
            yield return LuytensStar();
            yield return TeegardensStar();
        }

        static StarSpec Star(string name, string spectral, double mass, double radius, double luminosity, double teff, double rotationDays, string summary) =>
            new StarSpec
            {
                Name = name, Spectral = spectral, MassSolar = mass, RadiusSolar = radius, LuminositySolar = luminosity, TeffK = teff,
                RotationDays = rotationDays, Summary = summary,
            };

        /// <summary>A planet from the NASA Exoplanet Archive's composite table.</summary>
        static PlanetSpec Archive(string name, string host, double periodDays, double aAu, double e, double massEarth, double radiusEarth,
            double argPeriDeg, int year, string reference, string summary, bool minimumMass = true, double periastronJd = double.NaN) =>
            new PlanetSpec
            {
                Name = name, Host = host, PeriodDays = periodDays, AAu = aAu, E = e, MassEarth = massEarth, RadiusEarth = radiusEarth,
                MassIsMinimum = minimumMass, ArgPeriDeg = argPeriDeg, PeriastronJd = periastronJd, DiscoveryYear = year,
                Source = "NASA Exoplanet Archive (" + reference + ")", Summary = summary,
            };

        /// <summary>A candidate from its discovery paper (radius from the Chen and Kipping 2017 mass-radius relation).</summary>
        static PlanetSpec Candidate(string name, string host, double periodDays, double aAu, double e, double minMassEarth, int year,
            string reference, string summary) =>
            new PlanetSpec
            {
                Name = name, Host = host, PeriodDays = periodDays, AAu = aAu, E = e, MassEarth = minMassEarth,
                RadiusEarth = RadiusFromMass(minMassEarth), MassIsMinimum = true, Status = BodyStatus.Candidate, DiscoveryYear = year,
                Source = reference, Summary = summary,
            };

        /// <summary>
        /// Chen and Kipping 2017 (ApJ 834, 17) mean mass-radius relation, Earth units: R = 1.008 M^0.279
        /// for rocky worlds below 2.04 Earth masses, R ∝ M^0.589 for Neptune-like ones up to 0.414
        /// Jupiter masses, then nearly flat (M^-0.044) for gas giants.
        /// </summary>
        public static double RadiusFromMass(double massEarth)
        {
            const double terran = 2.04, neptunian = 0.414 * 317.828;
            double rTerran = 1.008 * Math.Pow(terran, 0.279);
            if (massEarth <= terran) return 1.008 * Math.Pow(massEarth, 0.279);
            double rNeptunian = rTerran * Math.Pow(neptunian / terran, 0.589);
            if (massEarth <= neptunian) return rTerran * Math.Pow(massEarth / terran, 0.589);
            return rNeptunian * Math.Pow(massEarth / neptunian, -0.044);
        }

        // ---------------------------------------------------------------- 5.96 ly

        public static SystemSpec BarnardsStar()
        {
            const string host = "Barnard's Star";
            var s = new SystemSpec
            {
                Name = "Barnard's Star", NeighbourName = "Barnard's Star",
                RaDeg = Astro.Hms(17, 57, 48.49847), DecDeg = Astro.Dms(4, 41, 36.1139), DistanceLy = SystemSpec.LyFromParallax(546.9759),
                Primary = StellarNode.Single(Star(host, "M4.0V", 0.162, 0.187, 0.003550, 3195, 114.06,
                    "An old M4 red dwarf, the nearest single star to the Sun. It crosses our sky faster than any other star, "
                    + "10.4 arcseconds a year.")),
                Summary = "The nearest single star, with four small rocky planets packed inside Mercury's distance.",
            };
            s.Planets.Add(Archive("Barnard d", host, 2.3402, 0.0188, 0.04, 0.263, 0.694, -51.8, 2025, "Basant et al. 2025",
                "A quarter of Earth's mass at least, the innermost of four, on a 2.3-day orbit. Orbital plane assumed."));
            s.Planets.Add(Archive("Barnard b", host, 3.1542, 0.0229, 0.03, 0.299, 0.72, 3.8, 2024, "Basant et al. 2025",
                "Found by ESPRESSO in 2024, a hot rocky world under a third of Earth's mass. Orbital plane assumed."));
            s.Planets.Add(Archive("Barnard c", host, 4.1244, 0.0274, 0.08, 0.335, 0.743, 90.8, 2025, "Basant et al. 2025",
                "The heaviest of the four, still only a third of Earth's mass at least. Orbital plane assumed."));
            s.Planets.Add(Archive("Barnard e", host, 6.7392, 0.0381, 0.04, 0.193, 0.637, -27.5, 2025, "Basant et al. 2025",
                "The outermost and lightest, a fifth of Earth's mass at least; all four are too hot for liquid water. Orbital plane assumed."));
            return s;
        }

        // ---------------------------------------------------------------- 6.51 ly

        public static SystemSpec Luhman16()
        {
            const double plx = 500.993;
            var a = Star("Luhman 16A", "L7.5", 35.4 * JupiterMassSolar, 0.102, 2.2e-5, 1305, 6.94 / 24,
                "A brown dwarf of 35 Jupiter masses at the L-T transition, too light to fuse hydrogen, glowing from its own heat.");
            var b = Star("Luhman 16B", "T0.5", 29.4 * JupiterMassSolar, 0.102, 2.1e-5, 1320, 5.28 / 24,
                "The lighter brown dwarf, 29 Jupiter masses, with patchy clouds that make it the among the most variable brown dwarfs known.");
            return new SystemSpec
            {
                Name = "Luhman 16", NeighbourName = "Luhman 16",
                RaDeg = Astro.Hms(10, 49, 18.771), DecDeg = Astro.Dms(53, 19, 9.88, south: true), DistanceLy = SystemSpec.LyFromParallax(plx),
                // Bedin et al. 2024: the astrometric orbit of B about A.
                Primary = StellarNode.Pair("Luhman 16", a, b, new BinaryOrbitSpec
                {
                    PeriodYears = 26.55, AAu = SystemSpec.AuFromArcsec(1.764, plx), E = 0.344, IDeg = 79.92, NodeDeg = 130.02, ArgPeriDeg = 136.67,
                    PeriastronYear = 2018.060,
                }),
                Summary = "The nearest brown dwarfs: a pair of failed stars circling each other every 26.6 years, found in 2013.",
            };
        }

        // ---------------------------------------------------------------- 7.86 ly

        public static SystemSpec Wolf359()
        {
            const string host = "Wolf 359";
            var s = new SystemSpec
            {
                Name = "Wolf 359", NeighbourName = "Wolf 359",
                RaDeg = Astro.Hms(10, 56, 28.92087), DecDeg = Astro.Dms(7, 0, 53.0033), DistanceLy = SystemSpec.LyFromParallax(415.1794),
                Primary = StellarNode.Single(Star(host, "M6V", 0.110, 0.144, 0.00106, 2749, 2.704,
                    "One of the faintest and lowest-mass stars near the Sun: an M6 flare star a tenth of the Sun's mass, spinning in 2.7 days.")),
                Summary = "A dim, flaring red dwarf with one unconfirmed giant planet far out.",
            };
            s.Planets.Add(Candidate("Wolf 359 b", host, 2938, 1.845, 0.04, 43.9, 2019, "Tuomi et al. 2019",
                "CANDIDATE: a cold Neptune-to-Saturn world (44 Earth masses at least) on an 8-year orbit, from one radial-velocity analysis. "
                + "Its inner partner \"c\" turned out to be the star's rotation. Radius estimated; plane and phase assumed."));
            return s;
        }

        // ---------------------------------------------------------------- 8.30 ly

        public static SystemSpec Lalande21185()
        {
            const string host = "Lalande 21185";
            var s = new SystemSpec
            {
                Name = "Lalande 21185", NeighbourName = "Lalande 21185",
                RaDeg = Astro.Hms(11, 3, 20.19482), DecDeg = Astro.Dms(35, 58, 11.5762), DistanceLy = SystemSpec.LyFromParallax(392.7529),
                Primary = StellarNode.Single(Star(host, "M2V", 0.389, 0.392, 0.01950, 3547, 53.33,
                    "The brightest red dwarf in the northern sky, just too faint for the naked eye, in Ursa Major.")),
                Summary = "A bright red dwarf with a hot super-Earth and a cold Neptune.",
            };
            s.Planets.Add(Archive("Lalande 21185 b", host, 12.9394, 0.07879, 0.063, 2.69, 1.45, -30, 2019, "Hurt et al. 2022",
                "A hot super-Earth, 2.7 Earth masses at least, on a 13-day orbit. Orbital plane assumed.", periastronJd: 2456307.8));
            s.Planets.Add(Candidate("Lalande 21185 d", host, 215.7, 0.5142, 0.18, 3.89, 2022, "Hurt et al. 2022",
                "CANDIDATE: a 216-day signal that may be a 3.9-Earth-mass planet. Radius estimated; plane and phase assumed."));
            s.Planets.Add(Archive("Lalande 21185 c", host, 2946, 2.94, 0.132, 13.6, 3.76, 63, 2021, "Hurt et al. 2022",
                "A cold Neptune-mass world (13.6 Earth masses at least) on an 8-year orbit. Orbital plane assumed.", periastronJd: 2457250));
            return s;
        }

        // ---------------------------------------------------------------- 8.61 ly

        public static SystemSpec Sirius()
        {
            const double plx = 378.9;
            var a = Star("Sirius A", "A0mA1 Va", 2.063, 1.7144, 24.74, 9845, 5.3,
                "The brightest star in Earth's night sky: a white main-sequence star of twice the Sun's mass and 25 times its light. "
                + "Rotation from its 16 km/s spin, assuming we see the equator.");
            var b = Star("Sirius B", "DA2", 1.018, 0.008098, 0.02448, 25000, 1,
                "A white dwarf: the Sun's mass crushed into a ball smaller than Earth, the burnt-out core of a star once heavier than A. "
                + "Rotation assumed.");
            return new SystemSpec
            {
                Name = "Sirius", NeighbourName = "Sirius",
                RaDeg = Astro.Hms(6, 45, 8.917), DecDeg = Astro.Dms(16, 42, 58.02, south: true), DistanceLy = SystemSpec.LyFromParallax(plx),
                // Bond et al. 2017 (ApJ 840, 70): the visual orbit of B about A.
                Primary = StellarNode.Pair("Sirius", a, b, new BinaryOrbitSpec
                {
                    PeriodYears = 50.1284, AAu = SystemSpec.AuFromArcsec(7.4957, plx), E = 0.59142, IDeg = 136.336, NodeDeg = 45.400, ArgPeriDeg = 149.161,
                    PeriastronYear = 1994.5715,
                }),
                Summary = "The Dog Star and its white-dwarf companion, 8 to 31 AU apart on a 50-year orbit. No planets are known.",
            };
        }

        // ---------------------------------------------------------------- 10.47 ly

        public static SystemSpec EpsilonEridani()
        {
            const string host = "Epsilon Eridani";
            var s = new SystemSpec
            {
                Name = "Epsilon Eridani", NeighbourName = "Epsilon Eridani",
                RaDeg = Astro.Hms(3, 32, 55.84496), DecDeg = Astro.Dms(9, 27, 29.7312, south: true), DistanceLy = SystemSpec.LyFromParallax(311.37),
                Primary = StellarNode.Single(Star(host, "K2V", 0.82, 0.742, 0.326, 5085, 11.60,
                    "A young orange dwarf, under a billion years old, magnetically active and ringed with dust like the young Sun.")),
                // The main belt's measured tilt to the sky (Booth et al. 2023); its node is assumed.
                ReferenceIDeg = 33.7,
                Summary = "A young Sun-like star with a Jupiter, two asteroid belts and a Kuiper belt: the nearest planetary system like ours.",
            };
            s.Planets.Add(Archive("Epsilon Eridani b", host, 2680, 3.53, 0.06, 317.83, 13.8, 268, 2000, "Thompson et al. 2025",
                "AEgir: a Jupiter-mass giant on a 7.3-year orbit, its mass and tilt (40° to the sky) measured from astrometry. Node assumed.",
                minimumMass: false, periastronJd: 2458017.5));
            s.Planets[0].IDeg = 40;
            s.Discs.Add(new DiscSpec
            {
                Name = "Epsilon Eridani inner belt", Host = host, InnerAu = 1.5, OuterAu = 2.0, Source = "Su et al. 2017; Backman et al. 2009",
                Summary = "Warm dust inside the giant planet's orbit, an asteroid belt like the Sun's (some models put it at 3-4 AU instead). Plane assumed to be the main belt's.",
            });
            s.Discs.Add(new DiscSpec
            {
                Name = "Epsilon Eridani outer belt", Host = host, InnerAu = 8, OuterAu = 20, Source = "Su et al. 2017; Backman et al. 2009",
                Summary = "A second, denser belt of dust out to 20 AU (Spitzer). Plane assumed to be the main belt's.",
            });
            s.Discs.Add(new DiscSpec
            {
                Name = "Epsilon Eridani main belt", Host = host, InnerAu = 65, OuterAu = 75, IDeg = 33.7, Source = "Booth et al. 2017, 2023; MacGregor et al. 2015",
                Summary = "A narrow ring of icy debris at 70 AU, the Kuiper belt's twin, imaged by ALMA.",
            });
            return s;
        }

        // ---------------------------------------------------------------- 11.01 ly

        public static SystemSpec Ross128()
        {
            const string host = "Ross 128";
            var s = new SystemSpec
            {
                Name = "Ross 128", NeighbourName = "Ross 128",
                RaDeg = Astro.Hms(11, 47, 44.39727), DecDeg = Astro.Dms(0, 48, 16.4003), DistanceLy = SystemSpec.LyFromParallax(296.3053),
                Primary = StellarNode.Single(Star(host, "M4V", 0.176, 0.198, 0.003620, 3189, 162,
                    "A quiet, old red dwarf in Virgo. Rotation the middle of the 101-223-day range measured.")),
                Summary = "A calm red dwarf with one temperate Earth-mass planet.",
            };
            s.Planets.Add(Archive("Ross 128 b", host, 9.8658, 0.0496, 0.116, 1.40, 1.11, double.NaN, 2017, "Bonfils et al. 2018",
                "A temperate world of 1.4 Earth masses at least on a 9.9-day orbit, round a star that rarely flares. Orbital plane and phase assumed."));
            return s;
        }

        // ---------------------------------------------------------------- 11.87 ly

        public static SystemSpec EpsilonIndi()
        {
            const string host = "Epsilon Indi A";
            var s = new SystemSpec
            {
                Name = "Epsilon Indi", NeighbourName = "Epsilon Indi",
                RaDeg = Astro.Hms(22, 3, 21.65363), DecDeg = Astro.Dms(56, 47, 9.5228, south: true), DistanceLy = SystemSpec.LyFromParallax(274.8431),
                Primary = StellarNode.Single(Star(host, "K5Ve", 0.782, 0.713, 0.21, 4649, 35.732,
                    "An orange dwarf in Indus, three-quarters of the Sun's mass, with a giant planet and two brown dwarfs.")),
                Summary = "An orange dwarf with a cold super-Jupiter, imaged by JWST, and a binary brown dwarf 1,500 AU out.",
            };
            var ba = Star("Epsilon Indi Ba", "T1", 66.92 * JupiterMassSolar, 0.0805, 2.04e-5, 1312, 2.0 / 24,
                "A T1 brown dwarf of 67 Jupiter masses, the heavier of the pair. Rotation assumed.");
            var bb = Star("Epsilon Indi Bb", "T6", 53.25 * JupiterMassSolar, 0.0825, 5.97e-6, 972, 2.0 / 24,
                "A T6 brown dwarf of 53 Jupiter masses, cool enough for methane in its air. Rotation assumed.");
            s.Companions.Add(new CompanionSpec
            {
                // Chen et al. 2022: the pair's orbit; its phase (periastron year) is assumed.
                Node = StellarNode.Pair("Epsilon Indi B", ba, bb, new BinaryOrbitSpec
                {
                    PeriodYears = 11.0197, AAu = 2.4058, E = 0.54042, IDeg = 77.082, NodeDeg = 147.959, ArgPeriDeg = 328.27, PeriastronYear = 2000,
                }, "The centre of mass of the brown dwarfs Ba and Bb, 2.4 AU apart on an 11-year orbit, 1,500 AU from A."),
                // Projected separation about 1,500 AU (Scholz et al. 2003); the direction in depth and the orbit are assumed.
                SeparationAu = 1500, PositionAngleDeg = 0, E = 0,
            });
            var ab = Archive("Epsilon Indi Ab", host, 25450, 15.76, 0.25, 2065.88, 12.7, -28.13, 2019, "Sanghi et al. 2026",
                "A cold super-Jupiter of 6.5 Jupiter masses on a 70-year orbit, the nearest giant planet ever imaged (JWST, 2024). Node assumed.",
                minimumMass: false, periastronJd: 2441420.76);
            ab.IDeg = 102.18;
            s.Planets.Add(ab);
            return s;
        }

        // ---------------------------------------------------------------- 11.91 ly

        public static SystemSpec TauCeti()
        {
            const string host = "Tau Ceti";
            var s = new SystemSpec
            {
                Name = "Tau Ceti", NeighbourName = "Tau Ceti",
                RaDeg = Astro.Hms(1, 44, 4.083), DecDeg = Astro.Dms(15, 56, 14.93, south: true), DistanceLy = SystemSpec.LyFromParallax(273.8097),
                Primary = StellarNode.Single(Star(host, "G8V", 0.800, 0.793, 0.488, 5320, 46,
                    "The nearest single Sun-like star, older and dimmer than the Sun, swaddled in ten times the Sun's cometary debris.")),
                // The debris disc's tilt to the sky (MacGregor et al. 2016); planets assumed to share it, node assumed.
                ReferenceIDeg = 35,
                Summary = "A nearby Sun-like star with a heavy debris disc and three super-Earths.",
            };
            PlanetSpec Feng(string name, double p, double a, double e, double m, double r, double w, string summary) =>
                Archive(name, host, p, a, e, m, r, w, 2017, "Feng et al. 2017", summary);
            s.Planets.Add(Feng("Tau Ceti g", 20.00, 0.133, 0.06, 1.75, 1.18, 35.341,
                "A hot super-Earth on a 20-day orbit, the innermost of Tau Ceti's three known planets."));
            s.Planets.Add(Feng("Tau Ceti h", 49.41, 0.243, 0.23, 1.83, 1.19, 7.45,
                "A 1.8-Earth-mass world on a 49-day orbit."));
            s.Planets.Add(Feng("Tau Ceti f", 636.13, 1.334, 0.16, 3.93, 1.81, 119.75,
                "A cold super-Earth just beyond the outer edge of the habitable zone."));
            s.Discs.Add(new DiscSpec
            {
                Name = "Tau Ceti debris disc", Host = host, InnerAu = 6.2, OuterAu = 52, IDeg = 35, Source = "MacGregor et al. 2016",
                Summary = "A broad disc of dust and comets from about 6 to 52 AU, far heavier than the Sun's Kuiper belt (ALMA).",
            });
            return s;
        }

        // ---------------------------------------------------------------- 11.98 ly

        public static SystemSpec Gj1061()
        {
            const string host = "GJ 1061";
            var s = new SystemSpec
            {
                Name = "GJ 1061", NeighbourName = "GJ 1061",
                RaDeg = Astro.Hms(3, 35, 59.69916), DecDeg = Astro.Dms(44, 30, 45.7308, south: true), DistanceLy = SystemSpec.LyFromParallax(272.1615),
                Primary = StellarNode.Single(Star(host, "M5.5V", 0.125, 0.152, 0.001700, 2977, 125,
                    "A small, quiet red dwarf in Horologium, an eighth of the Sun's mass.")),
                // The inclination Dreizler et al. adopt for all three planets; node assumed.
                ReferenceIDeg = 77,
                Summary = "A quiet red dwarf with three Earth-mass planets, the outer two temperate.",
            };
            s.Planets.Add(Archive("GJ 1061 b", host, 3.2073, 0.0210, 0.05, 1.37, 1.04, 145, 2020, "Dreizler et al. 2025",
                "A hot Earth-mass world, 1.4 Earth masses at least, on a 3.2-day orbit."));
            s.Planets.Add(Archive("GJ 1061 c", host, 6.6821, 0.0342, 0.02, 1.74, 1.19, 88, 2020, "Dreizler et al. 2025",
                "A 1.7-Earth-mass world on a 6.7-day orbit, getting about one and a half times the light Earth does."));
            s.Planets.Add(Archive("GJ 1061 d", host, 13.066, 0.054, 0.04, 1.64, 1.16, 157, 2020, "Dreizler et al. 2025",
                "A 1.6-Earth-mass world on a 13-day orbit, getting a little over half the light Earth does."));
            return s;
        }

        // ---------------------------------------------------------------- 12.35 ly

        public static SystemSpec LuytensStar()
        {
            const string host = "Luyten's Star";
            var s = new SystemSpec
            {
                Name = "Luyten's Star", NeighbourName = "Luyten's Star",
                RaDeg = Astro.Hms(7, 27, 24.49897), DecDeg = Astro.Dms(5, 13, 32.8415), DistanceLy = SystemSpec.LyFromParallax(264.1269),
                Primary = StellarNode.Single(Star(host, "M3.5V", 0.29, 0.293, 0.0088, 3382, 115.6,
                    "GJ 273, a red dwarf in Canis Minor; 1.2 light-years from Procyon, its nearest neighbour.")),
                Summary = "A red dwarf with a temperate super-Earth (the target of the 2017 \"Sónar Calling GJ 273b\" message) and two candidate outer worlds.",
            };
            s.Planets.Add(Archive("Luyten c", host, 4.7234, 0.036467, 0.17, 1.18, 1.06, double.NaN, 2017, "Astudillo-Defru et al. 2017",
                "GJ 273 c: a hot rocky world, 1.2 Earth masses at least, on a 4.7-day orbit. Orbital plane and phase assumed."));
            s.Planets.Add(Archive("Luyten b", host, 18.6498, 0.091101, 0.10, 2.89, 1.51, double.NaN, 2017, "Astudillo-Defru et al. 2017",
                "GJ 273 b: a super-Earth of 2.9 Earth masses at least on an 18.6-day orbit. Orbital plane and phase assumed."));
            s.Planets.Add(Candidate("Luyten d", host, 413.9, 0.712, 0.17, 10.8, 2019, "Tuomi et al. 2019",
                "CANDIDATE: a cold mini-Neptune on a 414-day orbit. Radius estimated; plane and phase assumed."));
            s.Planets.Add(Candidate("Luyten e", host, 542, 0.849, 0.03, 9.3, 2019, "Tuomi et al. 2019",
                "CANDIDATE: a second cold mini-Neptune on a 542-day orbit. Radius estimated; plane and phase assumed."));
            return s;
        }

        // ---------------------------------------------------------------- 12.50 ly

        public static SystemSpec TeegardensStar()
        {
            const string host = "Teegarden's Star";
            var s = new SystemSpec
            {
                Name = "Teegarden's Star", NeighbourName = "Teegarden's Star",
                RaDeg = Astro.Hms(2, 53, 0.89179), DecDeg = Astro.Dms(16, 52, 52.6322), DistanceLy = SystemSpec.LyFromParallax(260.9884),
                Primary = StellarNode.Single(Star(host, "M7.0V", 0.097, 0.120, 0.000722, 3034, 98.05,
                    "A tiny, cool red dwarf in Aries, under a tenth of the Sun's mass; found only in 2003.")),
                Summary = "A tiny red dwarf with three Earth-mass planets, the inner one among the most Earth-like known.",
            };
            s.Planets.Add(Archive("Teegarden's Star b", host, 4.90634, 0.0259, 0.03, 1.16, 1.05, 338, 2019, "Dreizler et al. 2024",
                "An Earth-mass world (1.16 at least) receiving about as much light as Earth. Orbital plane assumed."));
            s.Planets.Add(Archive("Teegarden's Star c", host, 11.416, 0.0455, 0.04, 1.05, 1.02, 301, 2019, "Dreizler et al. 2024",
                "An Earth-mass world on an 11.4-day orbit, getting about a third of Earth's light. Orbital plane assumed."));
            s.Planets.Add(Archive("Teegarden's Star d", host, 26.13, 0.0791, 0.07, 0.82, 0.954, 345, 2024, "Dreizler et al. 2024",
                "The outermost, 0.82 Earth masses at least, cold on a 26-day orbit. Orbital plane assumed."));
            return s;
        }
    }
}
