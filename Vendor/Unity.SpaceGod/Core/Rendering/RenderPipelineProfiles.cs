using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>The photoreal pieces a render pipeline can offer beyond drawing the game's own shaders (U.SPC-T430).</summary>
    [Flags]
    public enum PipelineFeatures
    {
        None = 0,

        /// <summary>A physically based sky: a planet's atmosphere scattering the star's light, seen from inside or from orbit.</summary>
        PhysicalSky = 1,

        /// <summary>Volumetric light (shafts of sunlight) in a planet's atmosphere.</summary>
        Volumetrics = 2,

        /// <summary>Physical exposure: the scene is metered for the star's light, fixed or adapting like an eye.</summary>
        Exposure = 4,

        /// <summary>Hardware ray tracing (shadows, reflections), where the GPU and graphics API support it.</summary>
        RayTracing = 8,

        All = PhysicalSky | Volumetrics | Exposure | RayTracing,
    }

    /// <summary>
    /// What the game knows about one render pipeline, as data (U.SPC-T429): which pipeline asset selects it, the
    /// <c>RenderPipeline</c> tag its hand-written shaders' SubShaders carry, the Resources folder they load from, and
    /// how the camera flags behave under it. Game code never names a pipeline: it asks the active backend
    /// (<c>Unity.SpaceGod.Rendering.RenderBackend</c>) for a shader, a camera, a light or a post-process look, and the
    /// backend registered for the active profile supplies it. A second pipeline is one more profile here and one more
    /// backend registered for it, never an edit to the code that draws the game.
    /// </summary>
    public sealed class RenderPipelineProfile
    {
        /// <summary>The key a backend registers under and a setting would store ("urp").</summary>
        public readonly string Id;

        public readonly string DisplayName;

        /// <summary>The type name of the pipeline asset that selects this pipeline (matched against the active one).</summary>
        public readonly string AssetTypeName;

        /// <summary>The <c>"RenderPipeline"</c> SubShader tag every hand-written shader for this pipeline carries.</summary>
        public readonly string ShaderTag;

        /// <summary>The Resources folder this pipeline's versions of the game's shaders load from.</summary>
        public readonly string ShaderFolder;

        /// <summary>Whether the camera's own HDR flag decides HDR (false where the pipeline always renders HDR).</summary>
        public readonly bool HonoursCameraHdr;

        /// <summary>Whether the camera's own MSAA flag decides anti-aliasing (false where the pipeline sets it elsewhere).</summary>
        public readonly bool HonoursCameraMsaa;

        /// <summary>The renderer features the game expects this pipeline's renderer to carry (none, today).</summary>
        public readonly IReadOnlyList<string> RenderFeatures;

        /// <summary>How the settings menu names this pipeline (and how the choice is remembered in a profile).</summary>
        public readonly string MenuName;

        /// <summary>
        /// The exposure (EV100) the game's display-referred shaders are metered for under this pipeline (U.SPC-T430):
        /// at it a shader's 1.0 is the display's white, as on a pipeline with no exposure. NaN where the pipeline has no
        /// physical exposure and draws the shaders' colours as they are (URP).
        /// </summary>
        public readonly double ReferenceEv100;

        /// <summary>The photoreal pieces this pipeline offers.</summary>
        public readonly PipelineFeatures Features;

        /// <summary>Whether the pipeline runs only on a GPU with compute shaders (the game stays on the default without them).</summary>
        public readonly bool RequiresComputeShaders;

        /// <summary>
        /// Where this pipeline's player sits beside the default one (U.SPC-T430): Unity builds a player for one pipeline
        /// only, so each pipeline but the default is built into a folder of this name inside the default player's
        /// folder ("" for the default itself), and the game restarts into the other player to switch.
        /// </summary>
        public readonly string PlayerFolder;

        public RenderPipelineProfile(string id, string displayName, string assetTypeName, string shaderTag, string shaderFolder,
            bool honoursCameraHdr = true, bool honoursCameraMsaa = true, IReadOnlyList<string> renderFeatures = null,
            string menuName = null, double referenceEv100 = double.NaN, PipelineFeatures features = PipelineFeatures.None,
            bool requiresComputeShaders = false, string playerFolder = "")
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A render pipeline needs an id.", nameof(id));
            if (string.IsNullOrEmpty(shaderFolder)) throw new ArgumentException("A render pipeline needs a shader folder.", nameof(shaderFolder));
            Id = id;
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName;
            AssetTypeName = assetTypeName ?? "";
            ShaderTag = shaderTag ?? "";
            ShaderFolder = shaderFolder.TrimEnd('/');
            HonoursCameraHdr = honoursCameraHdr;
            HonoursCameraMsaa = honoursCameraMsaa;
            RenderFeatures = renderFeatures ?? Array.Empty<string>();
            MenuName = string.IsNullOrEmpty(menuName) ? DisplayName : menuName;
            ReferenceEv100 = referenceEv100;
            Features = features;
            RequiresComputeShaders = requiresComputeShaders;
            PlayerFolder = (playerFolder ?? "").Trim('/', '\\');
        }

        public bool Offers(PipelineFeatures feature) => feature != PipelineFeatures.None && (Features & feature) == feature;

        /// <summary>Whether the pipeline meters the scene with a physical exposure (<see cref="ReferenceEv100"/>).</summary>
        public bool HasExposure => !double.IsNaN(ReferenceEv100) && !double.IsInfinity(ReferenceEv100);

        /// <summary>
        /// The luminance (cd/m²) a shader's 1.0 stands for: what exposure <see cref="ReferenceEv100"/> shows as white
        /// (<see cref="ExposureMath.LuminanceAtEv100"/>), and 1 on a pipeline with no exposure.
        /// </summary>
        public double ReferenceLuminance => HasExposure ? ExposureMath.LuminanceAtEv100(ReferenceEv100) : 1.0;

        /// <summary>
        /// The illuminance (lux) of a light of intensity 1 as the game's shaders mean it: a white Lambert surface it
        /// lights head on shows as white at the reference exposure (π × <see cref="ReferenceLuminance"/>; 1 without exposure).
        /// </summary>
        public double LuxPerLightUnit => HasExposure ? Math.PI * ReferenceLuminance : 1.0;

        /// <summary>The Resources path of <paramref name="shader"/> (one of <see cref="RenderShaders"/>) under this pipeline.</summary>
        public string ShaderPath(string shader) => ShaderFolder + "/" + shader;

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// The game's hand-written shaders by name, independent of any pipeline (U.SPC-T429). Each pipeline supplies its
    /// own version of every one (<see cref="RenderPipelineProfile.ShaderPath"/>); the shading math they share lives in
    /// the pipeline-neutral <c>SolarCommon.hlsl</c>, and the one pipeline-specific include in <c>PipelineCore.hlsl</c>.
    /// </summary>
    public static class RenderShaders
    {
        public const string Planet = "Planet", Sun = "Sun", Glow = "Glow", Ring = "Ring", Ship = "Ship", Line = "Line",
            Stars = "Stars", GasGiant = "GasGiant", Asteroids = "Asteroids", Fx = "Fx", Points = "Points", Sky = "Sky";

        public static readonly IReadOnlyList<string> All = new[] { Planet, Sun, Glow, Ring, Ship, Line, Stars, GasGiant, Asteroids, Fx, Points, Sky };
    }

    /// <summary>
    /// The shader globals the game lights everything by (U.SPC-T429): the nearest star and a companion star, in
    /// camera-relative world space. Hand-written shaders read these instead of Unity lights, on every pipeline.
    /// </summary>
    public static class RenderGlobals
    {
        public const string SunPosition = "_SunPositionWS", SunColor = "_SunColor", Sun2Position = "_Sun2PositionWS", Sun2Color = "_Sun2Color";

        /// <summary>
        /// On a pipeline with physical exposure (U.SPC-T430): the luminance a shader's 1.0 stands for
        /// (<see cref="RenderPipelineProfile.ReferenceLuminance"/>), and how much brighter than that the star's face is
        /// drawn. <c>PipelineCore.hlsl</c> reads them; a pipeline without exposure never sets them.
        /// </summary>
        public const string SceneLuminance = "_UnitySpaceSceneLuminance", SunBrightness = "_UnitySpaceSunBrightness";
    }

    /// <summary>
    /// The render pipelines the game knows (U.SPC-T429). URP is the first and the default; another registers its
    /// profile here and its backend with <c>RenderBackend.Register</c>.
    /// </summary>
    public static class RenderPipelineProfiles
    {
        /// <summary>
        /// URP 17: the shaders carry <c>"UniversalPipeline"</c> and load from <c>Resources/Shaders</c>, the camera's HDR
        /// and MSAA flags are honoured, and the renderer carries no custom features.
        /// </summary>
        public static readonly RenderPipelineProfile Universal = new RenderPipelineProfile("urp", "Universal Render Pipeline",
            "UniversalRenderPipelineAsset", "UniversalPipeline", "Shaders", menuName: "URP (default, lighter)");

        /// <summary>
        /// HDRP 17, the photoreal path (U.SPC-T430): the same shader files (each carries a SubShader tagged
        /// <c>"HDRenderPipeline"</c> beside its URP one), HDR always on and anti-aliasing set per camera rather than by
        /// the camera's MSAA flag, the scene metered for sunlight (EV100 15), and a physically based sky, volumetric
        /// light and ray tracing on offer. It runs from a quality level of its own, so it is chosen at start-up.
        /// </summary>
        public static readonly RenderPipelineProfile HighDefinition = new RenderPipelineProfile("hdrp", "High Definition Render Pipeline",
            "HDRenderPipelineAsset", "HDRenderPipeline", "Shaders", honoursCameraHdr: false, honoursCameraMsaa: false,
            menuName: "HDRP (photoreal)", referenceEv100: 15, features: PipelineFeatures.All, requiresComputeShaders: true,
            playerFolder: "HDRP");

        static readonly List<RenderPipelineProfile> registered = new List<RenderPipelineProfile> { Universal, HighDefinition };

        public static IReadOnlyList<RenderPipelineProfile> All => registered;

        /// <summary>The pipeline used when the active one is none the game knows.</summary>
        public static RenderPipelineProfile Default => Universal;

        /// <summary>Adds <paramref name="profile"/>, replacing one already registered under its id.</summary>
        public static void Register(RenderPipelineProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            int i = registered.FindIndex(p => p.Id == profile.Id);
            if (i >= 0) registered[i] = profile;
            else registered.Add(profile);
        }

        /// <summary>The profile registered under <paramref name="id"/>, or null.</summary>
        public static RenderPipelineProfile Find(string id) => registered.Find(p => p.Id == id);

        /// <summary>The profile the settings menu names <paramref name="menuName"/> (or whose id it is), or null.</summary>
        public static RenderPipelineProfile ForMenuName(string menuName)
        {
            if (string.IsNullOrEmpty(menuName)) return null;
            return registered.Find(p => string.Equals(p.MenuName, menuName, StringComparison.Ordinal))
                ?? registered.Find(p => string.Equals(p.Id, menuName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The player to restart into for <paramref name="wanted"/> (U.SPC-T430), from the player running now
        /// (<paramref name="runningExe"/>, on pipeline <paramref name="running"/>): the default pipeline's player sits at
        /// the top, each other pipeline's in its <see cref="RenderPipelineProfile.PlayerFolder"/> beside it, under the same
        /// file name. Null when there is nothing to switch (the same pipeline, or no path).
        /// </summary>
        public static string PlayerExecutable(string runningExe, RenderPipelineProfile running, RenderPipelineProfile wanted)
        {
            if (string.IsNullOrEmpty(runningExe) || running == null || wanted == null || running.Id == wanted.Id) return null;
            string dir = System.IO.Path.GetDirectoryName(runningExe) ?? "";
            string file = System.IO.Path.GetFileName(runningExe);
            // Up out of the running pipeline's own folder (one level per segment) to the default player's.
            if (running.PlayerFolder.Length > 0)
                foreach (var _ in running.PlayerFolder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
                    dir = System.IO.Path.GetDirectoryName(dir) ?? "";
            return wanted.PlayerFolder.Length > 0 ? System.IO.Path.Combine(dir, wanted.PlayerFolder, file) : System.IO.Path.Combine(dir, file);
        }

        /// <summary>
        /// The quality level to run <paramref name="wanted"/> from (U.SPC-T430). Each level names the pipeline asset it
        /// runs in <paramref name="levelAssetTypes"/> (the asset's type name, or null for one that falls back to the
        /// project's default asset, of type <paramref name="defaultAssetType"/>). Returns the <paramref name="current"/>
        /// level when it already runs that pipeline, otherwise the highest level that does, and -1 when none does (the
        /// pipeline is not set up in this build, so the game stays on the one it has).
        /// </summary>
        public static int QualityLevelFor(RenderPipelineProfile wanted, IReadOnlyList<string> levelAssetTypes, string defaultAssetType, int current)
        {
            if (wanted == null || levelAssetTypes == null) return -1;
            bool Runs(int i)
            {
                string type = levelAssetTypes[i];
                if (string.IsNullOrEmpty(type)) type = defaultAssetType;
                return string.Equals(type, wanted.AssetTypeName, StringComparison.Ordinal);
            }
            if (current >= 0 && current < levelAssetTypes.Count && Runs(current)) return current;
            for (int i = levelAssetTypes.Count - 1; i >= 0; i--)
                if (Runs(i)) return i;
            return -1;
        }

        /// <summary>
        /// The profile whose pipeline asset is of type <paramref name="assetTypeName"/>, or null when none is (no
        /// pipeline asset at all, or one the game has no backend for).
        /// </summary>
        public static RenderPipelineProfile ForAsset(string assetTypeName)
        {
            if (string.IsNullOrEmpty(assetTypeName)) return null;
            return registered.Find(p => string.Equals(p.AssetTypeName, assetTypeName, StringComparison.Ordinal));
        }
    }
}
