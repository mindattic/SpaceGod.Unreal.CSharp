using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// How the game's camera is exposed on a pipeline whose own Volume stack does the post-processing (U.SPC-T451, URP).
    /// Every mode ends in a number of stops added to the drawn image before tonemapping (0 leaves it as the shaders draw).
    /// </summary>
    public enum CameraMetering
    {
        /// <summary>Exposure compensation only: the image as drawn, brightened or darkened by a fixed number of stops.</summary>
        Fixed,

        /// <summary>Metered like an eye on the log average of everything in view, adapting over time.</summary>
        Automatic,

        /// <summary>Metered on a histogram of what is in view with its darkest and brightest shares thrown out (black space and the star's face), adapting over time.</summary>
        Histogram,

        /// <summary>Set by the camera's own ISO, shutter and aperture, as on a real camera, against "sunny 16" (EV100 15).</summary>
        PhysicalCamera,
    }

    /// <summary>The tone curve that maps the scene's range (the star's glare above white) onto the display (U.SPC-T451).</summary>
    public enum ToneCurve
    {
        /// <summary>None: values above white clip, as the game drew before.</summary>
        None,

        /// <summary>A neutral curve that rolls highlights off with little change to colour.</summary>
        Neutral,

        /// <summary>The film-like ACES curve: more contrast, saturated highlights desaturating toward white.</summary>
        Aces,
    }

    /// <summary>
    /// The post-processing choices of the settings menu for a pipeline that runs its own Volume stack (U.SPC-T451, URP):
    /// exposure metering, the physical camera, bloom and colour grading, in pipeline-neutral terms. With
    /// <see cref="Enabled"/> off the game draws exactly as before (no HDR, no post-processing).
    /// </summary>
    public struct CameraLook : IEquatable<CameraLook>
    {
        public bool Enabled;

        public CameraMetering Metering;

        /// <summary>Stops added on top of the metering (positive brightens).</summary>
        public double Compensation;

        /// <summary>The physical camera's sensitivity (ISO), shutter time (seconds) and aperture (f-number).</summary>
        public double Iso, ShutterSeconds, Aperture;

        /// <summary>Bloom strength (0 for none) and the brightness it starts at (1 is the display's white).</summary>
        public double Bloom, BloomThreshold;

        public ToneCurve Tonemapping;

        /// <summary>Colour grading: contrast and saturation, -100 to 100, and white balance temperature, -100 (cool) to 100 (warm); 0 leaves the image as it is.</summary>
        public double Contrast, Saturation, Temperature;

        /// <summary>How much brighter than a lit surface's white the star's face is drawn, so it glares and blooms (1 = no brighter).</summary>
        public double SunBrightness;

        /// <summary>
        /// What a fresh profile gets: the stack on with the image exposed as drawn (fixed, no compensation, no tone
        /// curve, no grading), so the only change from the game without it is the star glaring and blooming; the
        /// adapting meters, the tone curves and grading are the player's to turn on. The physical camera starts at
        /// "sunny 16" (f/16, 1/125 s, ISO 100), which also shows the image as drawn.
        /// </summary>
        public static CameraLook Default => new CameraLook
        {
            Enabled = true, Metering = CameraMetering.Fixed, Compensation = 0,
            Iso = 100, ShutterSeconds = 1.0 / 125, Aperture = 16,
            Bloom = 0.35, BloomThreshold = 1.0, Tonemapping = ToneCurve.None,
            Contrast = 0, Saturation = 0, Temperature = 0, SunBrightness = 8,
        };

        /// <summary>Whether the exposure adapts to what is in view (and so needs the image metered each frame).</summary>
        public bool Adapts => Enabled && (Metering == CameraMetering.Automatic || Metering == CameraMetering.Histogram);

        public bool Equals(CameraLook o) => Enabled == o.Enabled && Metering == o.Metering && Compensation.Equals(o.Compensation)
            && Iso.Equals(o.Iso) && ShutterSeconds.Equals(o.ShutterSeconds) && Aperture.Equals(o.Aperture)
            && Bloom.Equals(o.Bloom) && BloomThreshold.Equals(o.BloomThreshold) && Tonemapping == o.Tonemapping
            && Contrast.Equals(o.Contrast) && Saturation.Equals(o.Saturation) && Temperature.Equals(o.Temperature)
            && SunBrightness.Equals(o.SunBrightness);

        public override bool Equals(object obj) => obj is CameraLook o && Equals(o);

        public override int GetHashCode()
        {
            int h = (Enabled ? 1 : 0) | ((int)Metering << 1) | ((int)Tonemapping << 4);
            h = h * 397 ^ Compensation.GetHashCode();
            h = h * 397 ^ Iso.GetHashCode();
            h = h * 397 ^ ShutterSeconds.GetHashCode();
            h = h * 397 ^ Aperture.GetHashCode();
            h = h * 397 ^ Bloom.GetHashCode();
            h = h * 397 ^ Contrast.GetHashCode();
            h = h * 397 ^ Saturation.GetHashCode();
            return h * 397 ^ SunBrightness.GetHashCode();
        }
    }

    /// <summary>
    /// The exposure maths of <see cref="CameraLook"/> (U.SPC-T451), all in stops added to the drawn image. The shaders
    /// draw display-referred colour (1.0 is white), metered as if for sunlight at EV100 <see cref="ReferenceEv100"/>, so
    /// a physical camera set to that exposure shows the image as drawn and each EV darker opens it a stop. The adapting
    /// modes meter the scene's luminance (before exposure) against middle grey, inside <see cref="MinStops"/> to
    /// <see cref="MaxStops"/> so deep space never turns grey and the star never blacks the image out, and move toward it
    /// over time as an eye does: quickly when the view brightens, slowly when it darkens.
    /// </summary>
    public static class CameraExposure
    {
        /// <summary>The exposure the drawn image stands for: sunlight, "sunny 16" (f/16 at 1/125 s and ISO 100 is EV100 14.97).</summary>
        public const double ReferenceEv100 = 15;

        /// <summary>The scene luminance an adapting exposure brings to the display's middle grey.</summary>
        public const double MiddleGrey = 0.18;

        /// <summary>How far an adapting exposure may stop down (the star in frame) and open up (a dim view), in stops.</summary>
        public const double MinStops = -4, MaxStops = 1.5;

        /// <summary>The darkest luminance metered (black space counts as this, not as zero), and the brightest.</summary>
        public const double MinLuminance = 1.0 / 1024, MaxLuminance = 1024;

        /// <summary>The shares of the histogram's darkest and brightest pixels thrown out before averaging.</summary>
        public const double HistogramLow = 0.45, HistogramHigh = 0.98;

        /// <summary>How fast an adapting exposure follows: per second, toward a brighter view and toward a darker one.</summary>
        public const double SpeedToBright = 3.0, SpeedToDark = 1.0;

        const int HistogramBins = 64;

        /// <summary>The exposure value at ISO 100 of a camera at f-number <paramref name="aperture"/>, shutter <paramref name="shutterSeconds"/> and sensitivity <paramref name="iso"/>.</summary>
        public static double Ev100(double aperture, double shutterSeconds, double iso)
        {
            if (!(aperture > 0) || !(shutterSeconds > 0) || !(iso > 0)) return ReferenceEv100;
            return Math.Log(aperture * aperture / shutterSeconds, 2) - Math.Log(iso / 100.0, 2);
        }

        /// <summary>Stops a physical camera adds to the drawn image: one per EV it sits below the reference.</summary>
        public static double PhysicalStops(double aperture, double shutterSeconds, double iso) => ReferenceEv100 - Ev100(aperture, shutterSeconds, iso);

        /// <summary>The Rec. 709 luminance of a linear colour.</summary>
        public static double Luminance(double r, double g, double b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;

        static double ClampLuminance(double l) => double.IsNaN(l) ? MinLuminance : Math.Max(MinLuminance, Math.Min(MaxLuminance, l));

        /// <summary>The log-average (geometric mean) of <paramref name="luminances"/>, each clamped to the metered range; middle grey for none.</summary>
        public static double LogAverage(IReadOnlyList<double> luminances)
        {
            if (luminances == null || luminances.Count == 0) return MiddleGrey;
            double sum = 0;
            for (int i = 0; i < luminances.Count; i++) sum += Math.Log(ClampLuminance(luminances[i]), 2);
            return Math.Pow(2, sum / luminances.Count);
        }

        /// <summary>
        /// The average luminance of <paramref name="luminances"/> from a histogram in log2 bins, with the darkest
        /// <paramref name="low"/> and brightest share above <paramref name="high"/> of the pixels thrown out; middle grey for none.
        /// </summary>
        public static double HistogramAverage(IReadOnlyList<double> luminances, double low = HistogramLow, double high = HistogramHigh)
        {
            if (luminances == null || luminances.Count == 0) return MiddleGrey;
            low = Math.Max(0, Math.Min(1, low));
            high = Math.Max(low, Math.Min(1, high));
            double minLog = Math.Log(MinLuminance, 2), maxLog = Math.Log(MaxLuminance, 2), width = (maxLog - minLog) / HistogramBins;
            var bins = new int[HistogramBins];
            for (int i = 0; i < luminances.Count; i++)
            {
                int b = (int)((Math.Log(ClampLuminance(luminances[i]), 2) - minLog) / width);
                bins[Math.Max(0, Math.Min(HistogramBins - 1, b))]++;
            }
            double total = luminances.Count, from = low * total, to = high * total, seen = 0, weight = 0, logSum = 0;
            for (int b = 0; b < HistogramBins; b++)
            {
                double start = seen, end = seen + bins[b];
                seen = end;
                double kept = Math.Min(end, to) - Math.Max(start, from);
                if (kept <= 0) continue;
                logSum += kept * (minLog + (b + 0.5) * width);
                weight += kept;
            }
            // Everything thrown out (a range too narrow to keep a pixel): meter the plain average instead.
            return weight > 0 ? Math.Pow(2, logSum / weight) : LogAverage(luminances);
        }

        /// <summary>The stops that bring <paramref name="averageLuminance"/> to middle grey, plus <paramref name="compensation"/>, inside the adaptation limits.</summary>
        public static double TargetStops(double averageLuminance, double compensation)
        {
            double stops = Math.Log(MiddleGrey / ClampLuminance(averageLuminance), 2);
            return Math.Max(MinStops, Math.Min(MaxStops, stops)) + compensation;
        }

        /// <summary>
        /// Moves exposure <paramref name="current"/> toward <paramref name="target"/> over <paramref name="dt"/> seconds:
        /// fast toward a brighter view (fewer stops) and slower toward a darker one, never past the target.
        /// </summary>
        public static double Adapt(double current, double target, double dt)
        {
            if (double.IsNaN(current) || double.IsInfinity(current)) return target;
            if (!(dt > 0)) return current;
            double speed = target < current ? SpeedToBright : SpeedToDark;
            return current + (target - current) * (1 - Math.Exp(-dt * speed));
        }

        /// <summary>
        /// The stops a non-adapting <paramref name="look"/> adds: its compensation, plus the physical camera's exposure
        /// in that mode. For an adapting look, its compensation alone (the metering adds the rest); 0 when off.
        /// </summary>
        public static double FixedStops(CameraLook look)
        {
            if (!look.Enabled) return 0;
            return look.Metering == CameraMetering.PhysicalCamera
                ? PhysicalStops(look.Aperture, look.ShutterSeconds, look.Iso) + look.Compensation
                : look.Compensation;
        }
    }
}
