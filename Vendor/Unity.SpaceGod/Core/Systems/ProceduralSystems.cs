using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The Milky Way's shape (U.SPC-T413), for placing the generated stars: where its centre and plane lie from Sol, and how
    /// thickly stars fill it. A thin exponential disc (scale length 2.6 kpc, scale height 300 pc, after Bland-Hawthorn and
    /// Gerhard 2016) fading out past 50,000 light-years from the centre, plus a central bulge, normalised so the density at
    /// Sol is the local one: about 0.0032 star systems a cubic light-year, as the catalogued systems within 12 light-years
    /// have it. The centre is Sagittarius A* (RA 17h45m40.04s, Dec -29°00'28.2"), 8.178 kpc off (GRAVITY 2019); the north
    /// galactic pole is at RA 12h51m26.28s, Dec +27°07'42.0" (J2000); the Sun sits 20.8 pc above the plane (Bennett and
    /// Bovy 2019). Spiral arms are left out.
    /// </summary>
    public static class GalacticFrame
    {
        /// <summary>Sol's distance from the galactic centre, light-years.</summary>
        public const double SunToCentreLy = 26673;

        /// <summary>Sol's height above the galactic plane, light-years.</summary>
        public const double SunHeightLy = 68;

        /// <summary>Star systems a cubic light-year at Sol.</summary>
        public const double LocalDensity = 0.0032;

        /// <summary>The disc's scale length and height, light-years.</summary>
        public const double ScaleLengthLy = 8480, ScaleHeightLy = 980;

        /// <summary>How far from the centre the disc runs at full strength before it fades, light-years.</summary>
        public const double DiscRadiusLy = 50000;

        /// <summary>The fade past <see cref="DiscRadiusLy"/>, light-years.</summary>
        public const double EdgeFadeLy = 2000;

        /// <summary>The bulge's density at the centre (a cubic light-year) and its scale, light-years.</summary>
        public const double BulgeDensity = 0.1, BulgeScaleLy = 1500;

        /// <summary>Unit vector toward the north galactic pole (J2000 ecliptic axes).</summary>
        public static readonly DVec3 North = Astro.DirectionFromRaDec(Astro.Hms(12, 51, 26.28), Astro.Dms(27, 7, 42.0));

        /// <summary>Unit vector from Sol toward the galactic centre, in the plane.</summary>
        public static readonly DVec3 ToCentre = InPlane(Astro.DirectionFromRaDec(Astro.Hms(17, 45, 40.04), Astro.Dms(29, 0, 28.2, true)));

        static DVec3 InPlane(DVec3 v) => (v - North * DVec3.Dot(v, North)).Normalized;

        /// <summary>Where the galactic centre is, light-years from Sol.</summary>
        public static DVec3 CentreLy => ToCentre * SunToCentreLy - North * SunHeightLy;

        /// <summary>A place's distance from the galaxy's axis (<paramref name="radiusLy"/>) and height off its plane (<paramref name="heightLy"/>).</summary>
        public static void Cylindrical(DVec3 ly, out double radiusLy, out double heightLy)
        {
            var q = ly - CentreLy;
            heightLy = DVec3.Dot(q, North);
            radiusLy = (q - North * heightLy).Length;
        }

        /// <summary>Star systems a cubic light-year at <paramref name="ly"/> (from Sol).</summary>
        public static double Density(DVec3 ly)
        {
            Cylindrical(ly, out double r, out double z);
            double disc = LocalDensity * Math.Exp(-(r - SunToCentreLy) / ScaleLengthLy) * Math.Exp(-(Math.Abs(z) - SunHeightLy) / ScaleHeightLy);
            if (r > DiscRadiusLy) disc *= Math.Exp(-(r - DiscRadiusLy) / EdgeFadeLy);
            double sphere = Math.Sqrt(r * r + z * z);
            return disc + BulgeDensity * Math.Exp(-sphere / BulgeScaleLy);
        }
    }

    /// <summary>A small, fast random stream that comes out the same on every runtime (SplitMix64), for the galaxy's seeds.</summary>
    public sealed class SeededRandom
    {
        ulong state;

        public SeededRandom(ulong seed) => state = seed;

        public ulong NextULong()
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        public double Range(double lo, double hi) => lo + (hi - lo) * NextDouble();

        /// <summary>Log-uniform between <paramref name="lo"/> and <paramref name="hi"/> (both above zero).</summary>
        public double LogRange(double lo, double hi) => Math.Exp(Range(Math.Log(lo), Math.Log(hi)));

        /// <summary>A Poisson count with mean <paramref name="mean"/> (normal approximation above 30).</summary>
        public int Poisson(double mean)
        {
            if (!(mean > 0)) return 0;
            if (mean > 30)
            {
                double u1 = Math.Max(1e-300, NextDouble()), u2 = NextDouble();
                double n = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
                return Math.Max(0, (int)Math.Round(mean + n * Math.Sqrt(mean)));
            }
            double l = Math.Exp(-mean), p = 1;
            int k = 0;
            do
            {
                k++;
                p *= NextDouble();
            } while (p > l);
            return k - 1;
        }

        /// <summary>A seed made from <paramref name="parts"/>, each mixed in turn.</summary>
        public static ulong Mix(params long[] parts)
        {
            ulong h = 0x243F6A8885A308D3UL;
            foreach (long p in parts)
            {
                h ^= unchecked((ulong)p);
                h = new SeededRandom(h).NextULong();
            }
            return h;
        }
    }

    /// <summary>
    /// The rest of the galaxy, made up from its seed (U.SPC-T413): every star system beyond the real ones near Sol, out to the
    /// far edge of the disc, and none of them built until somebody comes near. Space is cut into cells
    /// <see cref="CellLy"/> on a side; each cell's stars come from the seed and the cell alone (how many from the galaxy's
    /// density there, <see cref="GalacticFrame.Density"/>; where in the cell, and what each one is, from its own stream), so
    /// any part of the galaxy can be charted in any order and comes out the same every time, and nothing about one cell
    /// depends on another having been looked at. A star's planets come from its own seed when it is built.
    ///
    /// The mix of stars is the solar neighbourhood's (RECONS): three in four red dwarfs, then K, G, F, A and the rare B
    /// stars, one in twenty a white dwarf and a few brown dwarfs; masses within a class are uniform and radius, luminosity
    /// and temperature follow from them on the main sequence. Planets are drawn so most stars have some (a few per star on
    /// average, rocky ones most often), spaced as real systems are (each 1.4 to 3 times the last one's distance), and a
    /// system now and then has a debris belt past its last planet. All of it is fiction, labelled so.
    /// </summary>
    public sealed class ProceduralSystems : SystemSource
    {
        public const string SourceName = "Procedural";

        /// <summary>The edge of a cell, light-years.</summary>
        public const double CellLy = 10;

        /// <summary>The most stars one cell holds (the galactic core is far denser than anything a chart can show).</summary>
        public const int MaxPerCell = 400;

        /// <summary>The prefix of every generated star's designation.</summary>
        public const string Prefix = "SGC";

        public override string Name => SourceName;
        public override int Order => 1000;

        public override IEnumerable<StarEntry> StarsNear(DVec3 centreLy, double radiusLy, int seed)
        {
            long x0 = Cell(centreLy.X - radiusLy), x1 = Cell(centreLy.X + radiusLy);
            long y0 = Cell(centreLy.Y - radiusLy), y1 = Cell(centreLy.Y + radiusLy);
            long z0 = Cell(centreLy.Z - radiusLy), z1 = Cell(centreLy.Z + radiusLy);
            for (long x = x0; x <= x1; x++)
            for (long y = y0; y <= y1; y++)
            for (long z = z0; z <= z1; z++)
            {
                // A cell wholly out of reach is skipped without being made.
                var near = new DVec3(
                    Math.Max(x * CellLy, Math.Min(centreLy.X, (x + 1) * CellLy)),
                    Math.Max(y * CellLy, Math.Min(centreLy.Y, (y + 1) * CellLy)),
                    Math.Max(z * CellLy, Math.Min(centreLy.Z, (z + 1) * CellLy)));
                if ((near - centreLy).Length > radiusLy) continue;
                foreach (var e in StarsInCell(seed, x, y, z))
                    if ((e.PositionLy - centreLy).Length <= radiusLy) yield return e;
            }
        }

        static long Cell(double ly) => (long)Math.Floor(ly / CellLy);

        /// <summary>Every star of cell (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) in the galaxy of <paramref name="seed"/>.</summary>
        public static List<StarEntry> StarsInCell(int seed, long x, long y, long z)
        {
            var rng = new SeededRandom(SeededRandom.Mix(seed, x, y, z));
            var centre = new DVec3((x + 0.5) * CellLy, (y + 0.5) * CellLy, (z + 0.5) * CellLy);
            int n = Math.Min(MaxPerCell, rng.Poisson(GalacticFrame.Density(centre) * CellLy * CellLy * CellLy));
            var list = new List<StarEntry>(n);
            for (int i = 0; i < n; i++)
            {
                var at = new DVec3((x + rng.NextDouble()) * CellLy, (y + rng.NextDouble()) * CellLy, (z + rng.NextDouble()) * CellLy);
                ulong starSeed = rng.NextULong();
                var e = Star(starSeed);
                e.PositionLy = at;
                e.Name = Designation(starSeed);
                e.Source = SourceName;
                e.Seed = starSeed;
                list.Add(e);
            }
            return list;
        }

        /// <summary>A generated star's name: the catalogue prefix and its seed's digits, so it is unique and the same whenever it is made.</summary>
        public static string Designation(ulong starSeed)
        {
            const string digits = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
            var c = new char[9];
            ulong v = starSeed;
            for (int i = 0; i < 8; i++)
            {
                c[i < 4 ? i : i + 1] = digits[(int)(v % (ulong)digits.Length)];
                v /= (ulong)digits.Length;
            }
            c[4] = '-';
            return Prefix + " " + new string(c);
        }

        // ---------------------------------------------------------------- stars

        /// <summary>The kinds of star, with how common each is and its mass and temperature ranges (low mass, high mass).</summary>
        static readonly (string cls, double share, double m0, double m1, double t0, double t1)[] Classes =
        {
            ("M", 0.73, 0.08, 0.45, 2400, 3850),
            ("K", 0.12, 0.45, 0.8, 3900, 5250),
            ("G", 0.07, 0.8, 1.04, 5300, 5950),
            ("F", 0.03, 1.04, 1.4, 6000, 7300),
            ("A", 0.006, 1.4, 2.1, 7500, 10000),
            ("B", 0.0013, 2.1, 10, 10500, 25000),
            ("D", 0.05, 0.5, 0.8, 5000, 20000),
            ("L", 0.0127, 0.03, 0.075, 600, 2200),
        };

        /// <summary>The star made from <paramref name="starSeed"/>: its class, mass, radius, temperature and luminosity.</summary>
        public static StarEntry Star(ulong starSeed)
        {
            var rng = new SeededRandom(starSeed);
            double pick = rng.NextDouble(), sum = 0;
            var k = Classes[0];
            foreach (var c in Classes)
            {
                sum += c.share;
                k = c;
                if (pick < sum) break;
            }
            double f = rng.NextDouble();
            double mass = k.m0 + (k.m1 - k.m0) * f;
            double teff = k.t0 + (k.t1 - k.t0) * f;
            double radius;
            string spectral;
            if (k.cls == "D")
            {
                // A white dwarf: Earth-sized, its temperature how long it has been cooling, not its mass.
                teff = rng.Range(k.t0, k.t1);
                radius = 0.012;
                spectral = "DA" + Math.Max(1, Math.Min(9, (int)Math.Round(50400 / teff)));
            }
            else if (k.cls == "L")
            {
                radius = 0.1;
                spectral = (teff < 1300 ? "T" : "L") + (int)Math.Floor((1 - f) * 9.99);
            }
            else
            {
                radius = SystemSources.MainSequenceRadius(mass);
                spectral = k.cls + (int)Math.Floor((1 - f) * 9.99) + "V";
            }
            return new StarEntry
            {
                Spectral = spectral, MassSolar = mass, RadiusSolar = radius, TeffK = teff,
                LuminositySolar = FrontierSystems.LuminosityOf(radius, teff),
            };
        }

        // ---------------------------------------------------------------- systems

        public override SystemSpec Define(StarEntry star, int seed)
        {
            if (star == null) return null;
            var made = Star(star.Seed);
            string name = star.Name ?? Designation(star.Seed);
            var rng = new SeededRandom(SeededRandom.Mix(seed, unchecked((long)star.Seed), 7));
            var spec = new StarSpec
            {
                Name = name, Spectral = made.Spectral, MassSolar = made.MassSolar, RadiusSolar = made.RadiusSolar,
                LuminositySolar = made.LuminositySolar, TeffK = made.TeffK,
                RotationDays = made.Spectral.StartsWith("D", StringComparison.Ordinal) ? rng.Range(0.1, 2) : rng.Range(1, 45),
                Summary = $"A {Kind(made.Spectral)}, {Format(made.MassSolar, "0.00")} solar masses, made up from the galaxy's seed. Fiction.",
            };
            var entry = new StarEntry { Name = name, PositionLy = star.PositionLy };
            var system = SystemSources.LoneStar(entry, spec, null);
            system.ReferenceIDeg = Math.Acos(rng.Range(-1, 1)) * Astro.Rad2Deg;
            system.ReferenceNodeDeg = rng.Range(0, 360);

            double starAu = made.RadiusSolar * StarSystemBuilder.SunRadiusKm / Astro.AuKm;
            int planets = rng.Poisson(made.Spectral.StartsWith("D", StringComparison.Ordinal) ? 0.6
                : made.MassSolar < 0.08 ? 0.5 : made.MassSolar > 2 ? 1.5 : 2.6);
            double a = Math.Max(starAu * 8, rng.LogRange(0.02, 0.25) * Math.Max(0.15, Math.Sqrt(made.LuminositySolar)));
            for (int i = 0; i < planets && a < 200; i++)
            {
                double roll = rng.NextDouble();
                double massEarth = roll < 0.6 ? rng.LogRange(0.1, 5) : roll < 0.85 ? rng.LogRange(5, 50) : rng.LogRange(50, 3000);
                double e = Math.Min(0.6, rng.NextDouble() * rng.NextDouble() * 0.5);
                system.Planets.Add(new PlanetSpec
                {
                    Name = name + " " + (char)('b' + i), Host = name, AAu = a, E = e, MassEarth = massEarth,
                    PeriodDays = OrbitalElements.PeriodFromGm(a * Astro.AuKm, StarSystemBuilder.SunGm * made.MassSolar),
                    RadiusEarth = NearbySystems.RadiusFromMass(massEarth), MassIsMinimum = false, Status = BodyStatus.Fictional,
                    MeanAnomalyDeg = rng.Range(0, 360), ArgPeriDeg = rng.Range(0, 360),
                    Source = "Generated from the galaxy's seed",
                    Summary = $"{Format(massEarth, "0.0")} Earth masses, {Format(a, "0.###")} AU out. Made up from the galaxy's seed. Fiction.",
                });
                a *= rng.Range(1.4, 3.0);
            }
            if (rng.NextDouble() < 0.15)
            {
                double inner = Math.Max(a, rng.LogRange(2, 40) * Math.Max(0.3, Math.Sqrt(made.LuminositySolar)));
                system.Discs.Add(new DiscSpec
                {
                    Name = name + " Belt", Host = name, InnerAu = inner, OuterAu = inner * rng.Range(1.2, 2.0),
                    Source = "Generated from the galaxy's seed", Summary = "A ring of rock and ice past the planets. Fiction.",
                });
            }
            system.Summary = planets == 0 ? $"A lone {Kind(made.Spectral)} with no planets. Fiction, from the galaxy's seed."
                : $"A {Kind(made.Spectral)} with {planets} planet{(planets == 1 ? "" : "s")}. Fiction, from the galaxy's seed.";
            return system;
        }

        static string Kind(string spectral)
        {
            switch (spectral[0])
            {
                case 'M': return "red dwarf";
                case 'K': return "orange dwarf";
                case 'G': return "yellow dwarf";
                case 'F': return "yellow-white star";
                case 'A': return "white star";
                case 'B': return "blue-white star";
                case 'D': return "white dwarf";
                default: return "brown dwarf";
            }
        }

        static string Format(double v, string f) => v.ToString(f, CultureInfo.InvariantCulture);
    }
}
