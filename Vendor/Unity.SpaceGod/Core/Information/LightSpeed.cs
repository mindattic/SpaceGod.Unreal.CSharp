using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Light speed, the fastest anything is known inside a system (U.SPC-T100). A scanner sees a ship where it was
    /// when the light left it, and a sighting radioed on to a fleet elsewhere in the system arrives as late again as
    /// the fleet is far from whoever saw it: about 8 minutes an AU, so a picture shared across Sol is minutes to
    /// hours old by the time anyone far away acts on it.
    /// </summary>
    public static class LightSpeed
    {
        /// <summary>The speed of light, km/s (exact, by the SI definition of the metre).</summary>
        public const double SpeedKmS = 299792.458;

        /// <summary>How long light takes to cross <paramref name="km"/>, seconds.</summary>
        public static double DelaySeconds(double km) => Math.Max(0, km) / SpeedKmS;

        /// <summary>How long light takes to cross <paramref name="km"/>, days.</summary>
        public static double DelayDays(double km) => DelaySeconds(km) / Astro.SecondsPerDay;

        /// <summary>How long light takes between two points (heliocentric km), days.</summary>
        public static double DelayDays(DVec3 a, DVec3 b) => DelayDays((a - b).Length);

        /// <summary>A delay for the HUD: "4.2 s", "8.3 min", "1.4 h", "3.2 days".</summary>
        public static string Describe(double days)
        {
            double s = days * Astro.SecondsPerDay;
            if (s < 90) return s.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";
            if (s < 5400) return (s / 60).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " min";
            if (days < 2) return (s / 3600).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " h";
            return days.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " days";
        }
    }
}
