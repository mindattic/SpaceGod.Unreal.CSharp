using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Photographic exposure as HDRP meters it (U.SPC-T430): an exposure value at ISO 100 (EV100) shows the luminance
    /// 1.2 × 2^EV as white (the saturation-based relation, K = 12.5 and q = 0.65), so each step up halves what reaches
    /// the image. Sunlit scenes sit near EV 15 ("sunny 16").
    /// </summary>
    public static class ExposureMath
    {
        /// <summary>The luminance (cd/m²) that exposure <paramref name="ev100"/> shows as white.</summary>
        public static double LuminanceAtEv100(double ev100) => 1.2 * Math.Pow(2, ev100);

        /// <summary>The exposure (EV100) that shows <paramref name="luminance"/> (cd/m², above 0) as white.</summary>
        public static double Ev100ForLuminance(double luminance) => Math.Log(luminance / 1.2, 2);

        /// <summary>What HDRP multiplies scene light by at exposure <paramref name="ev100"/> (1 / white luminance).</summary>
        public static double Multiplier(double ev100) => 1.0 / LuminanceAtEv100(ev100);
    }

    /// <summary>How the eye of the camera is metered on a pipeline with physical exposure (U.SPC-T430).</summary>
    public enum SunExposure
    {
        /// <summary>Metered once for sunlight (the pipeline's reference exposure): the scene looks as it is drawn.</summary>
        Fixed,

        /// <summary>Adapts to what is in view, like an eye: the star's glare in frame stops the image down, and it opens again in shadow.</summary>
        Adaptive,
    }

    /// <summary>
    /// The photoreal choices the settings menu makes (U.SPC-T430), in pipeline-neutral terms: each backend puts into
    /// effect what its pipeline offers (<see cref="RenderPipelineProfile.Features"/>) and ignores the rest, so URP,
    /// which offers none of them, draws the game exactly as before.
    /// </summary>
    public struct PhotorealOptions : IEquatable<PhotorealOptions>
    {
        /// <summary>A physically based sky and atmosphere for the planet a docking-ring descent the camera rides is coming down to (U.SPC-T452).</summary>
        public bool PhysicalSky;

        /// <summary>Volumetric light (sunbeams, haze lit by the star) inside a planet's atmosphere.</summary>
        public bool Volumetrics;

        public SunExposure Exposure;

        /// <summary>Exposure compensation in stops on top of the metering (positive brightens).</summary>
        public double ExposureCompensation;

        /// <summary>How much brighter than a lit surface's white the star's photosphere is drawn, so it glares (1 = no brighter).</summary>
        public double SunBrightness;

        /// <summary>Hardware ray tracing, where the GPU and graphics API support it.</summary>
        public bool RayTracing;

        /// <summary>What a fresh profile gets: the sky, volumetrics and fixed sunlight exposure on, ray tracing off.</summary>
        public static PhotorealOptions Default => new PhotorealOptions
        {
            PhysicalSky = true, Volumetrics = true, Exposure = SunExposure.Fixed, ExposureCompensation = 0, SunBrightness = 16, RayTracing = false,
        };

        /// <summary>The exposure (EV100) to meter at, on a pipeline whose reference exposure is <paramref name="referenceEv100"/>.</summary>
        public double MeteredEv100(double referenceEv100) => referenceEv100 - ExposureCompensation;

        public bool Equals(PhotorealOptions o) => PhysicalSky == o.PhysicalSky && Volumetrics == o.Volumetrics && Exposure == o.Exposure
            && ExposureCompensation.Equals(o.ExposureCompensation) && SunBrightness.Equals(o.SunBrightness) && RayTracing == o.RayTracing;

        public override bool Equals(object obj) => obj is PhotorealOptions o && Equals(o);

        public override int GetHashCode()
        {
            int flags = (PhysicalSky ? 1 : 0) | (Volumetrics ? 2 : 0) | ((int)Exposure << 2) | (RayTracing ? 16 : 0);
            return (flags * 397 ^ ExposureCompensation.GetHashCode()) * 397 ^ SunBrightness.GetHashCode();
        }
    }

    /// <summary>
    /// A planet's atmosphere as a physically based sky draws it (U.SPC-T430), in the scene's own units: the planet's
    /// radius, the shell of air above it, and how opaque that air is looking straight up in red, green and blue (the
    /// zenith opacity HDRP's sky takes), with the haze lower down and the fog the camera sits in.
    /// </summary>
    public struct AtmosphereShell
    {
        /// <summary>The body whose atmosphere this is (its catalogue index), or -1 for none.</summary>
        public int Body;

        public double Radius;

        /// <summary>How high the air reaches above the surface.</summary>
        public double Thickness;

        /// <summary>The camera's height above the surface (negative inside the planet, which never draws).</summary>
        public double Altitude;

        /// <summary>The air's zenith opacity per channel, 0 to 1.</summary>
        public double AirR, AirG, AirB;

        /// <summary>The haze's (aerosols') zenith opacity, and how high it reaches.</summary>
        public double Haze, HazeThickness;

        /// <summary>The distance light travels through the air at the camera before half of it is scattered; infinite above the air.</summary>
        public double FogMeanFreePath;

        public bool IsNone => Body < 0;

        /// <summary>Whether the camera is inside the air.</summary>
        public bool CameraInside => Body >= 0 && Altitude < Thickness;

        public static AtmosphereShell None => new AtmosphereShell { Body = -1, FogMeanFreePath = double.PositiveInfinity };
    }

    /// <summary>
    /// Which planet's atmosphere the physically based sky draws, and how (U.SPC-T430). A sky models one planet, so it
    /// follows the atmosphere the camera is in or closest to (by height over the planet's size), within
    /// <see cref="ReachRadii"/> of the surface; farther out the hand-drawn atmosphere rims carry it alone. It is drawn
    /// only during docking-ring atmospheric entry (<see cref="EntryBody"/>, U.SPC-T452): a narrow experiment behind
    /// the pipeline abstraction, not a renderer migration.
    /// </summary>
    public static class PlanetAtmospheres
    {
        /// <summary>How far above a surface (in the planet's radii) its sky still draws.</summary>
        public const double ReachRadii = 4.0;

        /// <summary>
        /// Earth's air, as HDRP's physically based sky defaults have it: zenith opacity 0.233 in blue from Rayleigh
        /// scattering of 33.1e-6 per metre over an 8 km scale height. With Earth's atmosphere strength (0.65) and blue
        /// tint this constant gives that back, and red and green follow the tint squared (Rayleigh falls off steeply).
        /// </summary>
        public const double AirOpacityPerStrength = 0.408;

        /// <summary>
        /// How eligible a body is for the sky: its altitude over its radius, or +∞ when it has no atmosphere, is a
        /// star, or is farther than <see cref="ReachRadii"/>. The smallest wins.
        /// </summary>
        public static double Closeness(double distanceToCentre, double radius, float atmosphereStrength, bool isStar)
        {
            if (isStar || !(atmosphereStrength > 0) || !(radius > 0) || double.IsNaN(distanceToCentre)) return double.PositiveInfinity;
            double altitude = (distanceToCentre - radius) / radius;
            if (altitude > ReachRadii) return double.PositiveInfinity;
            return Math.Max(altitude, 0);
        }

        /// <summary>How high the air reaches, as a share of the planet's radius: 2% for a thin one up to 6% for a thick one.</summary>
        public static double ThicknessFraction(float strength) => 0.02 + 0.04 * Clamp01(strength);

        /// <summary>
        /// The sky for body <paramref name="body"/>, of radius <paramref name="radius"/>, with the camera
        /// <paramref name="distanceToCentre"/> from its centre, an atmosphere of colour (<paramref name="r"/>,
        /// <paramref name="g"/>, <paramref name="b"/>) and strength <paramref name="strength"/> (as the planet shader takes them).
        /// </summary>
        public static AtmosphereShell Describe(int body, double radius, double distanceToCentre, float r, float g, float b, float strength)
        {
            if (body < 0 || !(radius > 0) || !(strength > 0)) return AtmosphereShell.None;
            double s = Clamp01(strength);
            double max = Math.Max(1e-3, Math.Max(r, Math.Max(g, b)));
            double Opacity(float c)
            {
                double t = Clamp01((float)(c / max));
                return 1 - Math.Exp(-AirOpacityPerStrength * s * t * t);
            }

            double thickness = radius * ThicknessFraction(strength);
            double altitude = distanceToCentre - radius;
            var shell = new AtmosphereShell
            {
                Body = body, Radius = radius, Thickness = thickness, Altitude = altitude,
                AirR = Opacity(r), AirG = Opacity(g), AirB = Opacity(b),
                // Haze: Earth's aerosols reach about a seventh of the way up its air; thicker air holds more.
                Haze = 1 - Math.Exp(-0.02 * s * 4), HazeThickness = thickness * 0.15,
            };
            shell.FogMeanFreePath = FogMeanFreePath(shell, s);
            return shell;
        }

        /// <summary>
        /// The fog at the camera: none above the air; inside it, the air thins exponentially with height (a scale height of
        /// a sixth of the shell), and near the surface of a strong atmosphere light goes about the shell's thickness.
        /// </summary>
        public static double FogMeanFreePath(AtmosphereShell shell, double strength)
        {
            if (shell.IsNone || !(strength > 0) || shell.Altitude >= shell.Thickness) return double.PositiveInfinity;
            double scaleHeight = shell.Thickness / 6.0;
            double density = Math.Exp(-Math.Max(shell.Altitude, 0) / scaleHeight);
            return shell.Thickness / (Clamp01((float)strength) * density);
        }

        static double Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>
        /// The one planet whose physically based sky may draw (U.SPC-T452), or -1 for none: the ringed world of the ship
        /// the camera rides (<paramref name="ridden"/>, followed or flown) while that ship, or the ship it escorts, is
        /// coming down through a docking ring, landed at its city, or climbing back to the ring. That descent is the one
        /// sequence in which the player sits inside a single atmosphere, which is what HDRP's sky models; everywhere
        /// else (orbit, open space, other planets seen from afar) the shaders' own per-planet atmosphere rims carry the
        /// air, so the experiment never touches the rest of the renderer (docs/RENDER_PIPELINES_SPIKE.md).
        /// </summary>
        public static int EntryBody(Ship ridden)
        {
            if (ridden == null || ridden.Destroyed) return -1;
            var c = ridden.Controller;
            return c != null && !c.Destroyed && c.Ring != null && c.Planetside ? c.Ring.Body : -1;
        }
    }
}
