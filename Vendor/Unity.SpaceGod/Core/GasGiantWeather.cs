using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Zonal wind profiles of the giant planets, and the Great Red Spot's position, so the gas giant
    /// shader can move cloud bands at their real speeds relative to each planet's rotation frame.
    ///
    /// Winds are eastward (prograde) speeds in m/s at planetographic latitudes, relative to the
    /// rotation period in <see cref="BodyCatalog"/> (Jupiter: System III; Saturn, Uranus, Neptune:
    /// the Voyager radio periods). The tables are approximate digitisations, read to roughly
    /// ±15 m/s, of the published cloud-tracked profiles:
    ///  - Jupiter: Cassini ISS, Porco et al. 2003 (Science 299, 1541), agreeing with Voyager
    ///    (Limaye 1986). Strongest jet +165 m/s at 23.7°N; retrograde -60 m/s at 19.5°S.
    ///  - Saturn: Voyager, Sánchez-Lavega et al. 2000 (Icarus 147, 405); equatorial jet ~400 m/s.
    ///  - Uranus: Sromovsky &amp; Fry 2005 / Hammel et al. 2001; retrograde equator, +250 m/s near 60°.
    ///  - Neptune: Voyager, Sromovsky et al. 1993; -400 m/s at the equator, +250 m/s near 70°.
    /// </summary>
    public static class GasGiantWeather
    {
        // Planetographic latitude (deg), eastward wind (m/s).
        static readonly double[,] Jupiter =
        {
            { -90, 0 }, { -78, 0 }, { -72, 25 }, { -68, -10 }, { -64, 30 }, { -60, -15 }, { -56, 35 }, { -52, -15 },
            { -48, 35 }, { -44, -20 }, { -40, 40 }, { -36, 30 }, { -32.5, -25 }, { -29, 10 }, { -26.5, 45 },
            { -23, 0 }, { -19.5, -60 }, { -15, 0 }, { -10, 60 }, { -7, 140 }, { -3, 105 }, { 0, 95 }, { 3, 105 },
            { 7, 130 }, { 12, 25 }, { 17, -25 }, { 20.5, 20 }, { 23.7, 165 }, { 27, -10 }, { 31, 30 }, { 35.5, -20 },
            { 39, 35 }, { 43, -15 }, { 48, 25 }, { 53, -10 }, { 56, 30 }, { 60, -10 }, { 66, 20 }, { 78, 0 }, { 90, 0 },
        };

        static readonly double[,] Saturn =
        {
            { -90, 0 }, { -80, 20 }, { -74, 100 }, { -67, -10 }, { -61, 110 }, { -53, -10 }, { -48, 70 }, { -46, 0 },
            { -41, 90 }, { -36, -20 }, { -30, 60 }, { -20, 220 }, { -10, 360 }, { 0, 400 }, { 10, 360 }, { 20, 220 },
            { 30, 60 }, { 36, -20 }, { 41, 90 }, { 46, 0 }, { 48, 70 }, { 53, -10 }, { 61, 110 }, { 67, -10 },
            { 74, 100 }, { 80, 20 }, { 90, 0 },
        };

        static readonly double[,] Uranus =
        {
            { -90, 0 }, { -75, 150 }, { -60, 250 }, { -40, 150 }, { -20, 0 }, { 0, -60 }, { 20, 0 }, { 40, 150 },
            { 60, 250 }, { 75, 150 }, { 90, 0 },
        };

        static readonly double[,] Neptune =
        {
            { -90, 0 }, { -80, 150 }, { -70, 250 }, { -50, 0 }, { -35, -150 }, { -20, -300 }, { 0, -400 },
            { 20, -300 }, { 35, -150 }, { 50, 0 }, { 70, 250 }, { 80, 150 }, { 90, 0 },
        };

        /// <summary>The wind table for a body, or null when it has none.</summary>
        public static double[,] ProfileFor(string bodyName)
        {
            switch (bodyName)
            {
                case "Jupiter": return Jupiter;
                case "Saturn": return Saturn;
                case "Uranus": return Uranus;
                case "Neptune": return Neptune;
                default: return null;
            }
        }

        /// <summary>Eastward wind (m/s) at a latitude, linearly interpolated in the table.</summary>
        public static double WindMs(double[,] profile, double latDeg)
        {
            int n = profile.GetLength(0);
            if (latDeg <= profile[0, 0]) return profile[0, 1];
            for (int i = 1; i < n; i++)
            {
                if (latDeg <= profile[i, 0])
                {
                    double t = (latDeg - profile[i - 1, 0]) / (profile[i, 0] - profile[i - 1, 0]);
                    return profile[i - 1, 1] + (profile[i, 1] - profile[i - 1, 1]) * t;
                }
            }
            return profile[n - 1, 1];
        }

        /// <summary>
        /// Drift of the clouds at a latitude in map longitude (deg/day, east-positive about the map's
        /// north) relative to the rotating frame. Prograde winds follow the rotation, so for a body
        /// spinning backwards about its map north they decrease map longitude.
        /// </summary>
        public static double DriftDegPerDay(BodyDefinition body, double[,] profile, double latDeg)
        {
            double u = WindMs(profile, latDeg);
            double circumferenceKm = Astro.TwoPi * body.RadiusKm * Math.Max(Math.Cos(latDeg * Astro.Deg2Rad), 0.05);
            double degPerDay = u / 1000.0 * Astro.SecondsPerDay / circumferenceKm * 360.0;
            return Math.Sign(BodyOrientation.RateDegPerDay(body)) >= 0 ? degPerDay : -degPerDay;
        }

        /// <summary>Samples the drift (deg/day) at evenly spaced latitudes from -90° to +90° into <paramref name="output"/>.</summary>
        public static void SampleDrift(BodyDefinition body, double[,] profile, float[] output)
        {
            int n = output.Length;
            for (int i = 0; i < n; i++)
            {
                double lat = -90.0 + 180.0 * i / Math.Max(1, n - 1);
                output[i] = (float)DriftDegPerDay(body, profile, lat);
            }
        }

        /// <summary>
        /// Jupiter's Great Red Spot. Position: BAA Jupiter Section report (2025 Sep 1, measured by
        /// S. Mizumoto): System II 77°, System III 292°. Drift: JUPOS (via projectpluto.com/grs_lon.txt)
        /// shows System II longitude rising from 410° (2024 Jan 1) to 439° (2025 Dec 1), +0.0415°/day
        /// westward; System III runs 8.0°/30 days faster than System II (BAA), so the spot drifts
        /// +0.308°/day in System III west longitude. Its drift oscillates (~90-day libration) and
        /// changes over years; this linear fit is good to a few degrees for 2024-2027.
        /// Internal circulation: ~4.5 to 6 days around the high-speed collar (Juno and Voyager/Galileo
        /// cloud tracking, e.g. Simon et al. 2018); the core is comparatively quiet.
        /// </summary>
        public static class GreatRedSpot
        {
            public const double AnchorJd = 2460919.5; // 2025-09-01 00:00 UT
            public const double AnchorSystemIIIWestDeg = 292.0;
            public const double SystemIIDriftDegPerDay = 29.0 / 700.0; // 410 -> 439 deg over 2024-01-01 .. 2025-12-01
            public const double SystemIIIMinusIIDegPerDay = 8.0 / 30.0;
            public const double DriftDegPerDay = SystemIIDriftDegPerDay + SystemIIIMinusIIDegPerDay;
            public const double LatitudeDeg = -22.6;
            public const double RotationPeriodDays = 6.0;

            /// <summary>Half-extents of the oval (deg of longitude, latitude), measured on the 2015 Hubble map.</summary>
            public const double HalfWidthDeg = 8.5;
            public const double HalfHeightDeg = 5.5;

            /// <summary>Where the spot sits on the colour map (OPAL Hubble map of 2015 Jan 19), System III west longitude.</summary>
            public const double MapSystemIIIWestDeg = 125.9;

            public static double SystemIIIWestDeg(double jd) => Astro.WrapDegrees360(AnchorSystemIIIWestDeg + DriftDegPerDay * (jd - AnchorJd));

            /// <summary>East longitude (map coordinates, -180..180) of the spot's centre.</summary>
            public static double EastLongitudeDeg(double jd)
            {
                double e = -SystemIIIWestDeg(jd);
                return Astro.WrapDegrees360(e + 180.0) - 180.0;
            }

            /// <summary>
            /// How far (deg) the colour map must be turned in longitude to put the mapped spot where it is
            /// today: sample longitude = display longitude + this. The rest of the 2015 map turns with it.
            /// </summary>
            public static double MapShiftDeg(double jd) => Astro.WrapDegrees360(SystemIIIWestDeg(jd) - MapSystemIIIWestDeg + 180.0) - 180.0;
        }
    }
}
