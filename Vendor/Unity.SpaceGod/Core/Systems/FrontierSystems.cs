using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The stars past the gate network's edge (U.SPC-T97): real neighbours that <see cref="Galaxy.Neighbours"/> shows but
    /// nobody simulates until a faction builds a gate to one (see <see cref="GateConstruction"/>). Each is a plain catalogue
    /// entry like <see cref="NearbySystems"/>' (the primary only, with the planets that are well established); the first gate
    /// in makes it a <see cref="StarSystem"/> of its own, empty and open to settlement. Values from the published
    /// measurements named in Systems/SOURCES.md; where a star's luminosity is marked derived it is worked out from its
    /// radius and temperature (Stefan-Boltzmann), and an unmeasured rotation is assumed.
    /// </summary>
    public static class FrontierSystems
    {
        public static IEnumerable<SystemSpec> All()
        {
            yield return Wise0855();
            yield return Gliese65();
            yield return Ross154();
            yield return Ross248();
            yield return Gliese887();
            yield return Cygni61();
            yield return Procyon();
            yield return Struve2398();
            yield return Groombridge34();
            yield return DxCancri();
        }

        /// <summary>The frontier star called <paramref name="name"/>, or null.</summary>
        public static SystemSpec Find(string name)
        {
            foreach (var s in All())
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// <summary>Luminosity (solar) from radius (solar) and effective temperature: L = R^2 (T / 5772 K)^4.</summary>
        public static double LuminosityOf(double radiusSolar, double teffK) => radiusSolar * radiusSolar * Math.Pow(teffK / 5772.0, 4);

        static SystemSpec System(string name, double rh, double rm, double rs, double dd, double dm, double ds, bool south, double ly,
            StarSpec star, string summary)
        {
            var s = new SystemSpec
            {
                Name = name, NeighbourName = name,
                RaDeg = Astro.Hms(rh, rm, rs), DecDeg = Astro.Dms(dd, dm, ds, south), DistanceLy = ly,
                Primary = StellarNode.Single(star), Summary = summary,
            };
            s.Stations.Add(Habitat(name, star));
            return s;
        }

        /// <summary>Stellar radii a habitat site keeps clear of its star, at the least.</summary>
        public const double HabitatMinRadii = 5;

        /// <summary>
        /// The orbit marked out for the first settlers' habitat (U.SPC-T293), as the settled frontier's hubs stand
        /// (<see cref="NearbySocieties"/>): the middle of the star's conservative habitable zone, or, round a star too
        /// cold to have one clear of its surface (a brown dwarf), <see cref="HabitatMinRadii"/> stellar radii out. Its
        /// water and air are assumed to come from captured comets, as at the frontier's ice stations. Fiction.
        /// </summary>
        public static StationSpec Habitat(string system, StarSpec star)
        {
            HabitableZones.ConservativeEdgesAu(star.LuminositySolar, star.TeffK, out double inner, out double outer);
            double aAu = Math.Max(Math.Sqrt(inner * outer), HabitatMinRadii * star.RadiusSolar * StarSystemBuilder.SunRadiusKm / Astro.AuKm);
            return new StationSpec
            {
                Name = system + " Habitat", Host = star.Name, AAu = aAu, MeanAnomalyDeg = 0, HabitatSite = true,
                Summary = "An orbit marked out for a settlers' habitat, in the habitable zone where the star has one clear of it; " +
                          "empty until a colony ship comes through the gate. Fiction.",
            };
        }

        static StarSpec Star(string name, string spectral, double mass, double radius, double luminosity, double teff, double rotationDays, string summary) =>
            new StarSpec
            {
                Name = name, Spectral = spectral, MassSolar = mass, RadiusSolar = radius, LuminositySolar = luminosity, TeffK = teff,
                RotationDays = rotationDays, Summary = summary,
            };

        static PlanetSpec Planet(string name, string host, double periodDays, double aAu, double e, double minMassEarth, int year, string reference, string summary) =>
            new PlanetSpec
            {
                Name = name, Host = host, PeriodDays = periodDays, AAu = aAu, E = e, MassEarth = minMassEarth,
                RadiusEarth = NearbySystems.RadiusFromMass(minMassEarth), MassIsMinimum = true, DiscoveryYear = year,
                Source = reference, Summary = summary,
            };

        const double JupiterMassSolar = 9.546e-4;

        // ---------------------------------------------------------------- 7.43 ly

        public static SystemSpec Wise0855() =>
            System("WISE 0855−0714", 8, 55, 10.8, 7, 14, 43, true, 7.430,
                Star("WISE 0855−0714", "Y4", 5 * JupiterMassSolar, 0.10, LuminosityOf(0.10, 285), 285, 0.4,
                    "The coldest brown dwarf known, about freezing point at its cloud tops, a few Jupiter masses. Found by WISE in 2014. "
                    + "Mass uncertain (3 to 10 Jupiter masses); rotation assumed."),
                "A lone, sunless brown dwarf colder than the North Pole: a gate site with nothing to warm it.");

        // ---------------------------------------------------------------- 8.77 ly

        public static SystemSpec Gliese65() =>
            System("Gliese 65", 1, 39, 1.3, 17, 57, 1, true, 8.770,
                Star("Gliese 65", "M5.5Ve", 0.1225, 0.165, LuminosityOf(0.165, 2670), 2670, 0.243,
                    "BL Ceti, the brighter of the Luyten 726-8 pair of flare stars; its twin UV Ceti, 5 AU off, gave its name to the class. "
                    + "Only A is simulated."),
                "Two tiny flare stars in a 26-year orbit, the nearest pair of their kind.");

        // ---------------------------------------------------------------- 9.71 ly

        public static SystemSpec Ross154() =>
            System("Ross 154", 18, 49, 49.4, 23, 50, 10, true, 9.7063,
                Star("Ross 154", "M3.5Ve", 0.177, 0.240, 0.0038, 3248, 2.85,
                    "A young, fast-spinning flare star in Sagittarius, a sixth of the Sun's mass."),
                "A lone red flare star with no planet yet found.");

        // ---------------------------------------------------------------- 10.31 ly

        public static SystemSpec Ross248() =>
            System("Ross 248", 23, 41, 54.7, 44, 10, 30, false, 10.3057,
                Star("Ross 248", "M5.5Ve", 0.136, 0.160, 0.0018, 2799, 4.2,
                    "A small flare star in Andromeda, closing on the Sun: in some 36,000 years it will be the nearest star of all."),
                "A dim red dwarf heading our way.");

        // ---------------------------------------------------------------- 10.72 ly

        public static SystemSpec Gliese887()
        {
            const string host = "Gliese 887";
            var s = System(host, 23, 5, 52.0, 35, 51, 11, true, 10.7241,
                Star(host, "M0.5V", 0.489, 0.471, 0.0368, 3688, 200,
                    "Lacaille 9352, the brightest red dwarf in the sky and one of the quietest: barely a flare in years of watching."),
                "A bright, calm red dwarf with two hot super-Earths.");
            s.Planets.Add(Planet("Gliese 887 b", host, 9.262, 0.068, 0.09, 4.2, 2020, "Jeffers et al. 2020",
                "A hot super-Earth, 4.2 Earth masses at least, on a 9-day orbit. Radius estimated; plane and phase assumed."));
            s.Planets.Add(Planet("Gliese 887 c", host, 21.789, 0.120, 0.22, 7.6, 2020, "Jeffers et al. 2020",
                "A warm super-Earth, 7.6 Earth masses at least, a little closer in than the habitable zone. Radius estimated; plane and phase assumed."));
            return s;
        }

        // ---------------------------------------------------------------- 11.40 ly

        public static SystemSpec Cygni61() =>
            System("61 Cygni", 21, 6, 53.9, 38, 44, 58, false, 11.4039,
                Star("61 Cygni A", "K5.0V", 0.70, 0.665, 0.153, 4526, 35.4,
                    "The first star to have its distance measured (Bessel, 1838), an orange dwarf; its companion 61 Cygni B, "
                    + "84 AU off on a 680-year orbit, is not simulated."),
                "Bessel's star: a pair of orange dwarfs, the nearest naked-eye stars of their kind in the north.");

        // ---------------------------------------------------------------- 11.46 ly

        public static SystemSpec Procyon() =>
            System("Procyon", 7, 39, 18.1, 5, 13, 30, false, 11.463,
                Star("Procyon", "F5IV–V", 1.478, 2.048, 6.93, 6530, 23,
                    "The Little Dog Star, the eighth brightest in the sky: a yellow-white star leaving the main sequence. "
                    + "Its white dwarf companion, Procyon B, on a 41-year orbit, is not simulated."),
                "A bright yellow-white star with a white dwarf companion and no known planets.");

        // ---------------------------------------------------------------- 11.49 ly

        public static SystemSpec Struve2398() =>
            System("Struve 2398", 18, 42, 46.7, 59, 37, 49, false, 11.4908,
                Star("Struve 2398", "M3.0V", 0.334, 0.351, LuminosityOf(0.351, 3400), 3400, 40,
                    "The brighter of a pair of red dwarfs in Draco, 50 AU apart; only A is simulated. Rotation assumed."),
                "A wide pair of red dwarfs in Draco.");

        // ---------------------------------------------------------------- 11.62 ly

        public static SystemSpec Groombridge34()
        {
            const string host = "Groombridge 34";
            var s = System(host, 0, 18, 22.9, 44, 1, 23, false, 11.6191,
                Star(host, "M1.5V", 0.38, 0.38, LuminosityOf(0.38, 3700), 3700, 44,
                    "Groombridge 34 A, the brighter of a wide pair of red dwarfs in Andromeda; B, 147 AU off, is not simulated."),
                "A wide pair of red dwarfs, the brighter with a hot super-Earth.");
            s.Planets.Add(Planet("Groombridge 34 Ab", host, 11.4433, 0.0717, 0.09, 3.03, 2014, "Howard et al. 2014; Pinamonti et al. 2018",
                "A hot super-Earth of 3 Earth masses at least on an 11-day orbit. Radius estimated; plane and phase assumed."));
            return s;
        }

        // ---------------------------------------------------------------- 11.68 ly

        public static SystemSpec DxCancri() =>
            System("DX Cancri", 8, 29, 49.5, 26, 46, 37, false, 11.6797,
                Star("DX Cancri", "M6.5Ve", 0.087, 0.11, LuminosityOf(0.11, 2700), 2700, 1.4,
                    "A tiny flare star in Cancer, under a tenth of the Sun's mass. Rotation assumed."),
                "One of the faintest red dwarfs near the Sun.");
    }
}
