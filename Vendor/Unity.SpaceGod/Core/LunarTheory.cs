using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The Moon's geocentric position from the principal terms of the ELP-2000/82 lunar theory, as
    /// tabulated by Meeus (Astronomical Algorithms, 2nd ed., ch. 47, tables 47.A and 47.B), turned
    /// from the mean ecliptic and equinox of date to J2000. A Kepler ellipse cannot follow the Moon:
    /// the Sun's pull swings it up to 1.3° ahead or behind (evection) and 0.7° more (variation).
    /// With these terms it stays within a few hundredths of a degree of JPL Horizons.
    /// </summary>
    public static class LunarTheory
    {
        // D, M, M', F multipliers; longitude (1e-6 deg) and distance (1e-3 km) coefficients.
        static readonly int[,] LrArgs =
        {
            { 0, 0, 1, 0 }, { 2, 0, -1, 0 }, { 2, 0, 0, 0 }, { 0, 0, 2, 0 }, { 0, 1, 0, 0 }, { 0, 0, 0, 2 }, { 2, 0, -2, 0 },
            { 2, -1, -1, 0 }, { 2, 0, 1, 0 }, { 2, -1, 0, 0 }, { 0, 1, -1, 0 }, { 1, 0, 0, 0 }, { 0, 1, 1, 0 }, { 2, 0, 0, -2 },
            { 0, 0, 1, 2 }, { 0, 0, 1, -2 }, { 4, 0, -1, 0 }, { 0, 0, 3, 0 }, { 4, 0, -2, 0 }, { 2, 1, -1, 0 }, { 2, 1, 0, 0 },
            { 1, 0, -1, 0 }, { 1, 1, 0, 0 }, { 2, -1, 1, 0 }, { 2, 0, 2, 0 }, { 4, 0, 0, 0 }, { 2, 0, -3, 0 }, { 0, 1, -2, 0 },
            { 2, 0, -1, 2 }, { 2, -1, -2, 0 }, { 1, 0, 1, 0 }, { 2, -2, 0, 0 }, { 0, 1, 2, 0 }, { 0, 2, 0, 0 }, { 2, -2, -1, 0 },
            { 2, 0, 1, -2 }, { 2, 0, 0, 2 }, { 4, -1, -1, 0 }, { 0, 0, 2, 2 }, { 3, 0, -1, 0 }, { 2, 1, 1, 0 }, { 4, -1, -2, 0 },
            { 0, 2, -1, 0 }, { 2, 2, -1, 0 }, { 2, 1, -2, 0 }, { 2, -1, 0, -2 }, { 4, 0, 1, 0 }, { 0, 0, 4, 0 }, { 4, -1, 0, 0 },
            { 1, 0, -2, 0 }, { 2, 1, 0, -2 }, { 0, 0, 2, -2 }, { 1, 1, 1, 0 }, { 3, 0, -2, 0 }, { 4, 0, -3, 0 }, { 2, -1, 2, 0 },
            { 0, 2, 1, 0 }, { 1, 1, -1, 0 }, { 2, 0, 3, 0 }, { 2, 0, -1, -2 },
        };

        static readonly double[] Lcoef =
        {
            6288774, 1274027, 658314, 213618, -185116, -114332, 58793, 57066, 53322, 45758, -40923, -34720, -30383, 15327,
            -12528, 10980, 10675, 10034, 8548, -7888, -6766, -5163, 4987, 4036, 3994, 3861, 3665, -2689, -2602, 2390, -2348,
            2236, -2120, -2069, 2048, -1773, -1595, 1215, -1110, -892, -810, 759, -713, -700, 691, 596, 549, 537, 520, -487,
            -399, -381, 351, -340, 330, 327, -323, 299, 294, 0,
        };

        static readonly double[] Rcoef =
        {
            -20905355, -3699111, -2955968, -569925, 48888, -3149, 246158, -152138, -170733, -204586, -129620, 108743, 104755,
            10321, 0, 79661, -34782, -23210, -21636, 24208, 30824, -8379, -16675, -12831, -10445, -11650, 14403, -7003, 0,
            10056, 6322, -9884, 5751, 0, -4950, 4130, 0, -3958, 0, 3258, 2616, -1897, -2117, 2354, 0, 0, -1423, -1117, -1571,
            -1739, 0, -4421, 0, 0, 0, 0, 1165, 0, 0, 8752,
        };

        // Latitude (1e-6 deg).
        static readonly int[,] BArgs =
        {
            { 0, 0, 0, 1 }, { 0, 0, 1, 1 }, { 0, 0, 1, -1 }, { 2, 0, 0, -1 }, { 2, 0, -1, 1 }, { 2, 0, -1, -1 }, { 2, 0, 0, 1 },
            { 0, 0, 2, 1 }, { 2, 0, 1, -1 }, { 0, 0, 2, -1 }, { 2, -1, 0, -1 }, { 2, 0, -2, -1 }, { 2, 0, 1, 1 }, { 2, 1, 0, -1 },
            { 2, -1, -1, 1 }, { 2, -1, 0, 1 }, { 2, -1, -1, -1 }, { 0, 1, -1, -1 }, { 4, 0, -1, -1 }, { 0, 1, 0, 1 }, { 0, 0, 0, 3 },
            { 0, 1, -1, 1 }, { 1, 0, 0, 1 }, { 0, 1, 1, 1 }, { 0, 1, 1, -1 }, { 0, 1, 0, -1 }, { 1, 0, 0, -1 }, { 0, 0, 3, 1 },
            { 4, 0, 0, -1 }, { 4, 0, -1, 1 },
        };

        static readonly double[] Bcoef =
        {
            5128122, 280602, 277693, 173237, 55413, 46271, 32573, 17198, 9266, 8822, 8216, 4324, 4200, -3359, 2463, 2211, 2065,
            -1870, 1828, -1794, -1749, -1565, -1491, -1475, -1410, -1344, -1335, 1107, 1021, 833,
        };

        /// <summary>Geocentric position of the Moon (km, J2000 ecliptic) at a Julian date (TT ≈ TDB).</summary>
        public static DVec3 Position(double jd)
        {
            double t = (jd - Astro.J2000) / Astro.DaysPerCentury;
            double t2 = t * t, t3 = t2 * t, t4 = t3 * t;
            double lp = 218.3164477 + 481267.88123421 * t - 0.0015786 * t2 + t3 / 538841 - t4 / 65194000;
            double d = 297.8501921 + 445267.1114034 * t - 0.0018819 * t2 + t3 / 545868 - t4 / 113065000;
            double m = 357.5291092 + 35999.0502909 * t - 0.0001536 * t2 + t3 / 24490000;
            double mp = 134.9633964 + 477198.8675055 * t + 0.0087414 * t2 + t3 / 69699 - t4 / 14712000;
            double f = 93.2720950 + 483202.0175233 * t - 0.0036539 * t2 - t3 / 3526000 + t4 / 863310000;
            double a1 = 119.75 + 131.849 * t, a2 = 53.09 + 479264.290 * t, a3 = 313.45 + 481266.484 * t;
            double e = 1 - 0.002516 * t - 0.0000074 * t2;

            double R(double deg) => deg * Astro.Deg2Rad;
            double sl = 0, sr = 0, sb = 0;
            for (int i = 0; i < Lcoef.Length; i++)
            {
                int cd = LrArgs[i, 0], cm = LrArgs[i, 1], cmp = LrArgs[i, 2], cf = LrArgs[i, 3];
                double arg = R(cd * d + cm * m + cmp * mp + cf * f);
                double ef = Math.Abs(cm) == 1 ? e : Math.Abs(cm) == 2 ? e * e : 1;
                sl += Lcoef[i] * ef * Math.Sin(arg);
                sr += Rcoef[i] * ef * Math.Cos(arg);
            }
            for (int i = 0; i < Bcoef.Length; i++)
            {
                int cd = BArgs[i, 0], cm = BArgs[i, 1], cmp = BArgs[i, 2], cf = BArgs[i, 3];
                double arg = R(cd * d + cm * m + cmp * mp + cf * f);
                double ef = Math.Abs(cm) == 1 ? e : Math.Abs(cm) == 2 ? e * e : 1;
                sb += Bcoef[i] * ef * Math.Sin(arg);
            }
            sl += 3958 * Math.Sin(R(a1)) + 1962 * Math.Sin(R(lp - f)) + 318 * Math.Sin(R(a2));
            sb += -2235 * Math.Sin(R(lp)) + 382 * Math.Sin(R(a3)) + 175 * Math.Sin(R(a1 - f)) + 175 * Math.Sin(R(a1 + f))
                  + 127 * Math.Sin(R(lp - mp)) - 115 * Math.Sin(R(lp + mp));

            double lambda = lp + sl / 1e6; // mean ecliptic and equinox of date
            double beta = sb / 1e6;
            double distance = 385000.56 + sr / 1000;

            // Of date to J2000: undo general precession in longitude, then the slow tilt of the ecliptic
            // (Lieske 1977 angles: the ecliptic of date is inclined pi_A to J2000's about a node at Pi_A).
            double pA = (5029.0966 * t + 1.11113 * t2) / 3600;
            double piA = (47.0029 * t - 0.03302 * t2) / 3600 * Astro.Deg2Rad;
            double bigPi = (174.876384 * 3600 - 869.8089 * t + 0.03536 * t2) / 3600;
            // Position in the ecliptic of date, longitudes measured from the J2000-node frame.
            double lon = R(lambda - pA - bigPi), lat = R(beta);
            double x = Math.Cos(lat) * Math.Cos(lon), y = Math.Cos(lat) * Math.Sin(lon), z = Math.Sin(lat);
            // Rotate about the node line by -pi_A into the J2000 ecliptic, then back by Pi_A in longitude.
            double y2 = y * Math.Cos(piA) - z * Math.Sin(piA);
            double z2 = y * Math.Sin(piA) + z * Math.Cos(piA);
            double bp = R(bigPi);
            double x3 = x * Math.Cos(bp) - y2 * Math.Sin(bp);
            double y3 = x * Math.Sin(bp) + y2 * Math.Cos(bp);
            return new DVec3(x3, y3, z2) * distance;
        }
    }
}
