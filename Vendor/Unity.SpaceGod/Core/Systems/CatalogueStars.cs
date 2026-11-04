using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The real stars near Sol (U.SPC-T413), as a <see cref="SystemSource"/>: the nearest stars the interstellar view has
    /// always shown (<see cref="Galaxy.Neighbours"/>), and every other system of the owner's chart of the nearest stars out to
    /// <see cref="ChartedToLy"/> (U.SPC-T454, docs/NEAREST_STARS.md), by its 7 ly divisions, the bright ones past
    /// <see cref="RadiusLy"/> landmarks of Earth's sky. A hand-built entry of the same name (<see cref="HandBuiltSystems"/>)
    /// wins its star. It claims the sphere of <see cref="RadiusLy"/> about Sol and <see cref="ClearLy"/> about each of its
    /// stars, so the procedural galaxy (<see cref="ProceduralSystems"/>) never puts an invented star where a real one is, or
    /// in the real neighbourhood; the gaps between them wider than a gate's reach are bridged by <see cref="VoidStations"/>.
    ///
    /// The list is far from every star out there (some 14,000 systems lie within 100 ly, most of them faint red dwarfs):
    /// it is the notable ones, and more can be added here as rows. Positions are the chart's, for 1 January 2027 (Gaia DR3
    /// and Hipparcos), listed with the rest of each star's values in Systems/SOURCES.md. A star without a mass in its row
    /// has one estimated from its temperature as if it were on the main sequence, and says so. Known planets of these stars
    /// are not modelled yet: each is built as its star, with the habitat orbit settlers would use.
    /// </summary>
    public sealed class CatalogueStars : SystemSource
    {
        public const string SourceName = "Catalogue";

        /// <summary>The sphere about Sol the catalogue holds: no star is generated within it.</summary>
        public const double RadiusLy = 20;

        /// <summary>The clear space kept about each catalogue star, inside or outside <see cref="RadiusLy"/>.</summary>
        public const double ClearLy = 3;

        public override string Name => SourceName;
        public override int Order => 100;
        public override bool ChartedFromStart => true;

        static List<StarEntry> stars;

        /// <summary>Every star the catalogue lists, nearest first.</summary>
        public static IReadOnlyList<StarEntry> Stars()
        {
            if (stars != null) return stars;
            var list = new List<StarEntry>();
            foreach (var n in Galaxy.Neighbours())
                list.Add(new StarEntry
                {
                    Name = n.Name, PositionLy = n.PositionLy, Spectral = n.SpectralType, ApparentMagnitude = n.ApparentMagnitude,
                    TeffK = n.TeffK, Source = SourceName, Tag = "neighbour", MassSolar = double.NaN, RadiusSolar = double.NaN,
                    LuminositySolar = double.NaN,
                });
            aliases["Gliese 65"] = "Luyten 726-8, UV and BL Ceti";
            aliases["Gliese 887"] = "Lacaille 9352";
            list.AddRange(Beyond());
            list.Sort((a, b) => a.PositionLy.Length.CompareTo(b.PositionLy.Length));
            return stars = list;
        }

        static StarEntry R(string name, double x, double y, double z, string type, double mag, double teff, double mass, double radius,
            double luminosity, string aka = null)
        {
            var at = new DVec3(x, y, z);
            if (aka != null) aliases[name] = aka;
            return new StarEntry
            {
                Name = name, PositionLy = at, Spectral = type, ApparentMagnitude = mag, TeffK = teff, MassSolar = mass, RadiusSolar = radius,
                LuminositySolar = luminosity, Source = SourceName,
                Tag = at.Length > RadiusLy && mag < LandmarkMagnitude ? "landmark" : "catalogue",
            };
        }

        /// <summary>A catalogue star past <see cref="RadiusLy"/> this bright to the eye (or brighter) is a landmark of Earth's sky.</summary>
        public const double LandmarkMagnitude = 2.5;

        /// <summary>A value a row does not give (see <see cref="Beyond"/>).</summary>
        const double NaN = double.NaN;

        static readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The other name a catalogue star goes by (Wolf 1061's Gliese 628), or null.</summary>
        public static string AlsoKnownAs(string name)
        {
            Stars();
            return aliases.TryGetValue(name, out var aka) ? aka : null;
        }

        /// <summary>
        /// Every system of the owner's chart of the nearest stars out to <see cref="ChartedToLy"/> (docs/NEAREST_STARS.md, U.SPC-T454)
        /// past the neighbours: its position for 1 January 2027 straight from docs/nearest_stars_2027.csv (Sol-centric J2000 ecliptic
        /// light-years, from Gaia DR3 and Hipparcos parallaxes and motions, so the pasted list's slips are not in it), its type,
        /// visual magnitude and temperature, and, where the star is not an ordinary dwarf (or is one already known well), its mass,
        /// radius and luminosity; NaN there estimates them from the temperature as a main-sequence star. Sources in Systems/SOURCES.md.
        /// </summary>
        static IEnumerable<StarEntry> Beyond() => new[]
        {
            R("YZ Ceti", 10.9990, 1.8986, -4.6785, "M4.5V", 12.1, 3150, NaN, NaN, NaN),
            R("Luyten's Star", -4.5794, 10.9197, -3.5146, "M3.5V", 9.9, 3150, NaN, NaN, NaN),
            R("Teegarden's Star", 8.7136, 8.9707, 0.0625, "M7.0V", 15.1, 2900, NaN, NaN, NaN),
            R("Kapteyn's Star", 1.8903, 4.5276, -11.8787, "sdM1", 8.85, 3570, 0.274, 0.291, 0.012),
            R("Lacaille 8760", 7.6430, -9.2685, -4.8422, "M0Ve", 6.67, 3800, 0.6, 0.51, 0.072, "AX Microscopii"),
            R("Kruger 60", 6.4428, 1.8925, 11.2445, "M3V", 9.6, 3180, 0.271, 0.35, 0.01),
            R("Ross 614", -1.7130, 11.9176, -5.8852, "M4.5V", 11.2, 3200, NaN, NaN, NaN),
            R("Wolf 1061", -5.2288, -12.8505, 2.2124, "M3V", 10.1, 3340, NaN, NaN, NaN, "Gliese 628"),
            R("Van Maanen's Star", 13.6914, 3.2628, 0.0206, "DZ7", 12.4, 6220, 0.68, 0.011, 0.00017, "Gliese 35"),
            R("Gliese 1", 11.2827, -3.1760, -8.0194, "M1.5V", 8.6, 3600, NaN, NaN, NaN),
            R("Gliese 687", -0.5614, 0.4833, 14.7794, "M3V", 9.2, 3400, NaN, NaN, NaN),
            R("Gliese 674", -1.3772, -13.4891, -5.9297, "M2.5V", 9.4, 3400, NaN, NaN, NaN),
            R("Gliese 876", 14.1116, -5.3679, -1.7560, "M4V", 10.2, 3473, 0.37, 0.376, 0.0122),
            R("Gliese 412", -11.1343, 6.8157, 8.9113, "M1V", 8.8, 3700, NaN, NaN, NaN, "WX Ursae Majoris"),
            R("Gliese 832", 8.5291, -10.6780, -8.6998, "M1.5V", 8.7, 3600, NaN, NaN, NaN),
            R("AD Leonis", -13.7976, 8.1207, 2.4810, "M3.5V", 9.4, 3400, NaN, NaN, NaN),
            R("40 Eridani", 7.1301, 12.4290, -7.7619, "K0.5V", 4.43, 5072, 0.78, 0.81, 0.46, "Omicron² Eridani"),
            R("EV Lacertae", 11.2059, 1.1863, 12.0528, "M3.5V", 10.2, 3300, NaN, NaN, NaN),
            R("70 Ophiuchi", 0.3949, -14.9237, 7.2572, "K0V", 4.0, 5300, 0.89, 0.91, 0.6),
            R("Altair", 7.6689, -12.3775, 8.1729, "A7V", 0.76, 7550, 1.86, 1.79, 10.6),
            R("Gliese 229", -0.8046, 13.2050, -13.3581, "M1V", 8.1, 3700, NaN, NaN, NaN),
            R("Sigma Draconis", 2.5656, 1.4937, 18.5667, "G9V", 4.67, 5297, 0.87, 0.78, 0.43),
            R("Gliese 570", -12.7769, -14.2601, -1.4639, "K4V", 5.7, 4500, NaN, NaN, NaN),
            R("Eta Cassiopeiae", 10.0982, 8.5493, 14.1889, "F9V", 3.44, 5973, 0.97, 1.04, 1.23),
            R("36 Ophiuchi", -3.3768, -19.1673, -1.2105, "K2V", 5.1, 5100, NaN, NaN, NaN),
            R("82 Eridani", 9.2530, 4.7683, -16.7345, "G8V", 4.25, 5401, 0.85, 0.92, 0.69),
            R("Gliese 783", 8.6169, -16.8815, -5.3285, "K2.5V", 5.3, 4900, NaN, NaN, NaN, "HR 7703"),
            R("Delta Pavonis", 4.2805, -13.4778, -13.9986, "G8IV", 3.56, 5604, 0.99, 1.22, 1.22),
            R("Gliese 581", -13.0971, -15.3417, 3.6489, "M3V", 10.6, 3480, NaN, NaN, NaN),
            R("Xi Boötis", -15.2416, -10.1077, 12.2285, "G7V", 4.55, 5550, NaN, NaN, NaN),
            R("Gliese 667", -3.4410, -22.8389, -4.8485, "K3V", 5.9, 4600, NaN, NaN, NaN),
            R("Beta Hydri", 5.3282, -8.8720, -21.9883, "G2IV", 2.8, 5870, 1.08, 1.81, 3.5),
            R("107 Piscium", 20.8057, 12.5433, 3.8468, "K1V", 5.24, 5240, NaN, NaN, NaN),
            R("Vega", 3.1278, -11.4176, 22.0182, "A0V", 0.03, 9602, 2.14, 2.5, 40.1),
            R("Fomalhaut", 21.0175, -10.3137, -9.0515, "A3V", 1.16, 8590, 1.92, 1.84, 16.6),
            R("Pi³ Orionis", 7.8668, 24.1087, -6.9779, "F6V", 3.19, 6500, NaN, NaN, NaN),
            R("Mu Herculis", -1.4176, -16.9584, 21.0887, "G5IV", 3.42, 5560, 1.1, 1.73, 2.54),
            R("Chara", -20.4183, 4.4523, 17.8760, "G0V", 4.26, 5930, NaN, NaN, NaN, "Beta Canum Venaticorum"),
            R("61 Virginis", -24.9518, -11.6367, -4.5126, "G7V", 4.74, 5530, NaN, NaN, NaN),
            R("Zeta Tucanae", 11.8469, -9.1245, -23.6736, "F9.5V", 4.23, 5960, NaN, NaN, NaN),
            R("Gliese 250", -6.4642, 24.5148, -13.4492, "K3V", 6.6, 4600, NaN, NaN, NaN),
            R("Delta Eridani", 16.3365, 20.0755, -14.1532, "K0IV", 3.52, 5020, 1.19, 2.35, 3.2),
            R("Beta Comae Berenices", -25.1406, -1.9150, 16.0723, "F9.5V", 4.26, 5960, NaN, NaN, NaN),
            R("Kappa¹ Ceti", 19.3144, 21.7027, -7.4865, "G5V", 4.84, 5700, NaN, NaN, NaN),
            R("Gamma Pavonis", 9.8681, -18.0909, -22.0730, "F9V", 4.22, 6100, NaN, NaN, NaN),
            R("GJ 367", -17.7781, 2.1990, -24.9385, "M1V", 10.2, 3520, NaN, NaN, NaN),
            R("Gliese 357", -23.1540, 10.9071, -17.1211, "M2.5V", 10.9, 3500, NaN, NaN, NaN),
            R("Gliese 176", 9.6421, 29.3063, -1.7681, "M2V", 10.0, 3680, NaN, NaN, NaN),
            R("Wolf 1069", 9.6811, -1.4028, 29.6268, "M5V", 14.0, 3160, NaN, NaN, NaN),
            R("AU Microscopii", 17.8656, -25.2221, -7.0369, "M1V", 8.6, 3700, NaN, NaN, NaN),
            R("Gliese 436", -28.4134, 7.7289, 12.2722, "M2.5V", 10.7, 3480, NaN, NaN, NaN),
            R("Pollux", -13.2309, 30.8534, 3.9336, "K0III", 1.14, 4586, 1.91, 9.06, 32.7),
            R("Zeta Herculis", -10.0379, -18.4516, 27.9881, "F9IV", 2.81, 5800, 1.45, 2.56, 6.6),
            R("Gliese 86", 18.7311, 0.1483, -29.8077, "K1V", 6.1, 5180, NaN, NaN, NaN),
            R("Denebola", -34.7056, 5.1160, 7.6261, "A3V", 2.13, 8500, NaN, NaN, NaN),
            R("54 Piscium", 33.1486, 10.4772, 9.7157, "K0V", 5.88, 5250, NaN, NaN, NaN),
            R("HD 85512", -22.3357, 2.9453, -28.5891, "K6V", 7.67, 4400, NaN, NaN, NaN),
            R("Arcturus", -28.7701, -12.9489, 18.7473, "K1.5III", -0.05, 4286, 1.08, 25.4, 170),
            R("Porrima", -37.4591, -6.6974, 1.8537, "F0V", 2.74, 7100, NaN, NaN, NaN, "Gamma Virginis"),
            R("Deneb Algedi", 31.0942, -22.9718, -1.7585, "A7III", 2.85, 7300, 2, 1.91, 8.5, "Delta Capricorni"),
            R("Zeta Reticuli", 11.7679, -1.1935, -37.4788, "G2V", 5.2, 5720, NaN, NaN, NaN),
            R("TRAPPIST-1", 39.4384, -10.0244, 0.4457, "M8V", 18.8, 2566, 0.0898, 0.1192, 0.000553),
            R("55 Cancri", -24.7416, 31.9778, 7.3947, "K0IV-V", 5.95, 5200, NaN, NaN, NaN),
            R("HD 40307", 0.5483, 4.8204, -42.1215, "K2.5V", 7.1, 4980, NaN, NaN, NaN),
            R("Capella", 5.5987, 39.1336, 16.6673, "K0III + G1III", 0.08, 4970, 2.57, 11.98, 78.7),
            R("Upsilon Andromedae", 30.1020, 23.9851, 21.3157, "F8V", 4.09, 6200, NaN, NaN, NaN),
            R("Errai", 9.5976, 16.6873, 40.6703, "K1IV", 3.22, 4800, 1.4, 4.74, 11.7, "Gamma Cephei"),
            R("47 Ursae Majoris", -33.2865, 19.9473, 23.3731, "G1V", 5.03, 5890, NaN, NaN, NaN),
            R("Gliese 1214", -9.2236, -41.2216, 22.3729, "M4.5V", 14.7, 3100, NaN, NaN, NaN),
            R("Rasalhague", -5.1773, -39.0603, 28.4527, "A5III", 2.08, 8000, 2.4, 2.6, 25, "Alpha Ophiuchi"),
            R("Alderamin", 17.1221, 3.8839, 45.5312, "A8Vn", 2.45, 7700, 2, 2.3, 18, "Alpha Cephei"),
            R("51 Pegasi", 45.5592, -4.5588, 21.5321, "G2IV", 5.49, 5790, NaN, NaN, NaN),
            R("Mu Arae", -2.1614, -44.4425, -24.0930, "G3IV-V", 5.15, 5800, NaN, NaN, NaN),
            R("Castor", -17.3698, 47.1110, 8.9389, "A1V + A2Vm", 1.58, 10286, 2.37, 2.4, 30),
            R("Tau Boötis", -43.4194, -14.0475, 22.7657, "F7V", 4.5, 6400, NaN, NaN, NaN),
            R("Gliese 777", 23.1573, -25.1761, 39.1596, "G7IV-V", 5.7, 5550, NaN, NaN, NaN, "HD 190360"),
            R("Caph", 28.0289, 19.7132, 42.6377, "F2III", 2.27, 6700, 1.91, 3.5, 27, "Beta Cassiopeiae"),
            R("HD 10647", 30.3432, -4.8984, -47.8857, "F9V", 5.5, 6150, NaN, NaN, NaN, "q¹ Eridani"),
            R("Zosma", -53.5992, 18.1236, 14.4570, "A4V", 2.56, 8300, 2.2, 2.1, 23, "Delta Leonis"),
            R("Menkent", -40.2953, -36.6741, -22.1076, "K0III", 2.06, 4980, 1.27, 10.6, 60, "Theta Centauri"),
            R("Mothallah", 48.4839, 36.3503, 18.2941, "F6IV", 3.41, 6290, 1.7, 3, 14, "Alpha Trianguli"),
            R("Beta Pictoris", 2.2094, 16.8816, -61.0731, "A6V", 3.86, 8050, 1.75, 1.5, 8.7),
            R("HD 189733", 29.9135, -37.2846, 43.3046, "K1V", 7.67, 5050, NaN, NaN, NaN),
            R("Aldebaran", 22.4583, 61.0051, -6.2238, "K5III", 0.86, 3900, 1.16, 45, 440),
            R("Hamal", 51.3016, 39.5981, 11.3847, "K2III", 2.0, 4480, 1.5, 15, 91, "Alpha Arietis"),
            R("Unukalhai", -41.0481, -52.6833, 31.8679, "K2III", 2.63, 4500, 1.66, 12, 71, "Alpha Serpentis"),
            R("Alphecca", -39.6873, -36.1092, 52.4030, "A1IV", 2.22, 9700, 2.58, 3, 74, "Alpha Coronae Borealis"),
            R("Zubenelgenubi", -53.5180, -53.6715, 0.4397, "A3IV", 2.75, 8000, 2, 2.2, 31, "Alpha Librae"),
            R("Regulus", -68.5543, 39.8557, 0.6425, "B8IVn", 1.4, 12460, 3.8, 4.35, 341),
            R("Merak", -42.7118, 36.5631, 56.4868, "A1IV", 2.37, 9377, 2.7, 3, 63, "Beta Ursae Majoris"),
            R("Megrez", -43.7050, 24.1607, 63.1362, "A3V", 3.31, 9480, 1.63, 1.4, 14, "Delta Ursae Majoris"),
            R("Delta Velorum", -30.6570, 5.9871, -74.3014, "A1V", 1.96, 9450, 2.4, 2.97, 67),
            R("Alioth", -44.9578, 17.3167, 67.0936, "A1III-IVp", 1.77, 9020, 2.91, 4.14, 102, "Epsilon Ursae Majoris"),
            R("Mizar", -44.4789, 11.3364, 69.0322, "A1V", 2.23, 9000, 2.2, 2.4, 33, "Mizar and Alcor"),
            R("Phecda", -49.2430, 27.8856, 60.9882, "A0Ve", 2.44, 9355, 2.94, 3.04, 71, "Gamma Ursae Majoris"),
            R("Ankaa", 62.4518, -16.1584, -55.3609, "K0.5III", 2.4, 4436, 2.5, 15, 86, "Alpha Phoenicis"),
            R("Gacrux", -47.6624, -35.5803, -65.6706, "M3.5III", 1.64, 3626, 1.5, 84, 820, "Gamma Crucis"),
            R("Algol", 46.3187, 69.1054, 34.3379, "B8V", 2.12, 13000, 3.17, 2.73, 182, "Beta Persei"),
            R("Eta Draconis", -17.8482, -4.6109, 90.1330, "G8III", 2.73, 5055, 2.55, 11, 60),
            R("Diphda", 89.6625, 4.0485, -34.0657, "K0III", 2.04, 4800, 2.8, 18, 145, "Beta Ceti"),
            R("Ruchbah", 45.9266, 50.8850, 71.9857, "A5IV", 2.68, 8400, 2.5, 3.2, 63, "Delta Cassiopeiae"),
        };

        /// <summary>How far out the catalogue charts real stars: the owner's 100 ly chart (U.SPC-T454).</summary>
        public const double ChartedToLy = 100;

        public override bool Claims(DVec3 ly)
        {
            double r = ly.Length;
            if (r < RadiusLy) return true;
            if (r > ChartedToLy + ClearLy) return false;
            foreach (var s in Stars())
                if ((s.PositionLy - ly).Length < ClearLy) return true;
            return false;
        }

        public override IEnumerable<StarEntry> StarsNear(DVec3 centreLy, double radiusLy, int seed)
        {
            foreach (var s in Stars())
                if ((s.PositionLy - centreLy).Length <= radiusLy) yield return Copy(s);
        }

        static StarEntry Copy(StarEntry e) => new StarEntry
        {
            Name = e.Name, PositionLy = e.PositionLy, Spectral = e.Spectral, MassSolar = e.MassSolar, RadiusSolar = e.RadiusSolar,
            LuminositySolar = e.LuminositySolar, TeffK = e.TeffK, ApparentMagnitude = e.ApparentMagnitude, Source = e.Source, Tag = e.Tag,
        };

        public override SystemSpec Define(StarEntry star, int seed)
        {
            StarEntry row = null;
            foreach (var s in Stars())
                if (string.Equals(s.Name, star.Name, StringComparison.OrdinalIgnoreCase)) row = s;
            if (row == null) return null;
            bool estimated = double.IsNaN(row.MassSolar);
            double mass = estimated ? SystemSources.MainSequenceMass(row.TeffK) : row.MassSolar;
            double radius = estimated ? SystemSources.MainSequenceRadius(mass) : row.RadiusSolar;
            double luminosity = estimated ? FrontierSystems.LuminosityOf(radius, row.TeffK) : row.LuminositySolar;
            string aka = AlsoKnownAs(row.Name);
            var spec = new StarSpec
            {
                Name = row.Name, Spectral = row.Spectral, MassSolar = mass, RadiusSolar = radius, LuminositySolar = luminosity, TeffK = row.TeffK,
                RotationDays = row.TeffK > 7000 ? 1 : 30,
                Summary = $"{row.Name}{(aka != null ? " (" + aka + ")" : "")}, a real star {Ly(row)} light-years from the Sun ({row.Spectral})." +
                          (estimated ? " Mass, radius and luminosity estimated from its temperature as a main-sequence star." : "") +
                          " Rotation assumed; any known planets are not modelled yet.",
            };
            return SystemSources.LoneStar(row, spec, row.Tag == "landmark"
                ? $"{row.Name}, one of the bright stars of Earth's sky, {Ly(row)} light-years out."
                : $"A real star {Ly(row)} light-years from the Sun, empty until a gate or a ship reaches it.");
        }

        static string Ly(StarEntry e) => e.PositionLy.Length.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
