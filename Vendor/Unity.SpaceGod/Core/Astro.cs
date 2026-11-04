using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>Physical constants and time conversions shared by the whole simulation.</summary>
    public static class Astro
    {
        public const double AuKm = 149597870.7;
        public const double SecondsPerDay = 86400.0;
        public const double DaysPerCentury = 36525.0;
        public const double J2000 = 2451545.0;
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;
        public const double TwoPi = Math.PI * 2.0;

        /// <summary>Standard gravity in km/day², the unit ship drives are specified in (g).</summary>
        public const double GravityKmPerDay2 = 9.80665e-3 * SecondsPerDay * SecondsPerDay;

        /// <summary>Mean obliquity of the ecliptic at J2000, used to turn IAU pole RA/Dec into ecliptic vectors.</summary>
        public const double ObliquityDeg = 23.4392911;

        public const double LightSpeedKmPerSec = 299792.458;

        /// <summary>Julian year light-year (IAU), km.</summary>
        public const double LightYearKm = 9460730472580.8;

        static readonly DateTime J2000Utc = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>Julian date for a UTC instant. UTC vs TT (~69 s) is ignored: it moves Earth ~2000 km.</summary>
        public static double JulianDate(DateTime utc) =>
            J2000 + (utc.ToUniversalTime() - J2000Utc).TotalDays;

        public static DateTime ToDateTime(double jd)
        {
            double days = jd - J2000;
            // DateTime spans years 1-9999; clamp so absurd time warps cannot throw.
            double maxDays = (DateTime.MaxValue - J2000Utc).TotalDays - 1;
            double minDays = (DateTime.MinValue - J2000Utc).TotalDays + 1;
            days = Math.Max(minDays, Math.Min(maxDays, days));
            return J2000Utc.AddDays(days);
        }

        public static double WrapRadians(double a)
        {
            a %= TwoPi;
            if (a > Math.PI) a -= TwoPi;
            else if (a < -Math.PI) a += TwoPi;
            return a;
        }

        public static double WrapDegrees360(double a)
        {
            a %= 360.0;
            return a < 0 ? a + 360.0 : a;
        }

        /// <summary>Unit vector (ecliptic J2000 frame) for an IAU pole given in equatorial RA/Dec.</summary>
        public static DVec3 PoleFromRaDec(double raDeg, double decDeg)
        {
            double ra = raDeg * Deg2Rad, dec = decDeg * Deg2Rad, eps = ObliquityDeg * Deg2Rad;
            double x = Math.Cos(dec) * Math.Cos(ra);
            double y = Math.Cos(dec) * Math.Sin(ra);
            double z = Math.Sin(dec);
            return new DVec3(x, y * Math.Cos(eps) + z * Math.Sin(eps), -y * Math.Sin(eps) + z * Math.Cos(eps)).Normalized;
        }

        /// <summary>An equatorial J2000 vector turned into the ecliptic J2000 frame.</summary>
        public static DVec3 EquatorialToEcliptic(DVec3 v)
        {
            double eps = ObliquityDeg * Deg2Rad;
            return new DVec3(v.X, v.Y * Math.Cos(eps) + v.Z * Math.Sin(eps), -v.Y * Math.Sin(eps) + v.Z * Math.Cos(eps));
        }

        /// <summary>An ecliptic J2000 vector turned into the equatorial J2000 frame.</summary>
        public static DVec3 EclipticToEquatorial(DVec3 v)
        {
            double eps = ObliquityDeg * Deg2Rad;
            return new DVec3(v.X, v.Y * Math.Cos(eps) - v.Z * Math.Sin(eps), v.Y * Math.Sin(eps) + v.Z * Math.Cos(eps));
        }

        /// <summary>Unit vector (ecliptic J2000) toward an equatorial right ascension and declination.</summary>
        public static DVec3 DirectionFromRaDec(double raDeg, double decDeg) => PoleFromRaDec(raDeg, decDeg);

        /// <summary>Right ascension and declination (degrees) of an ecliptic J2000 direction.</summary>
        public static void RaDecOf(DVec3 ecliptic, out double raDeg, out double decDeg)
        {
            var q = EclipticToEquatorial(ecliptic).Normalized;
            raDeg = WrapDegrees360(Math.Atan2(q.Y, q.X) * Rad2Deg);
            decDeg = Math.Asin(Math.Max(-1, Math.Min(1, q.Z))) * Rad2Deg;
        }

        /// <summary>Right ascension in hours, minutes and seconds, as degrees.</summary>
        public static double Hms(double h, double m, double s) => (h + m / 60 + s / 3600) * 15;

        /// <summary>Declination from degrees, arcminutes and arcseconds; <paramref name="south"/> makes it negative.</summary>
        public static double Dms(double d, double m, double s, bool south = false) =>
            (south ? -1 : 1) * (Math.Abs(d) + m / 60 + s / 3600);

        public static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Math.Max(0, Math.Min(1, (x - edge0) / (edge1 - edge0)));
            return t * t * (3 - 2 * t);
        }
    }
}
