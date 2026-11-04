using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.SpaceGod.Core
{
    /// <summary>What a sky is drawn from (U.SPC-T595).</summary>
    public enum SkyKind
    {
        /// <summary>No backdrop: only the point-cloud starfield on black (the game's sky before skyboxes).</summary>
        Starfield,

        /// <summary>An equirectangular (2:1) panorama shipped in the game's Resources.</summary>
        Image,

        /// <summary>An equirectangular panorama the player dropped into the profile's Skyboxes folder.</summary>
        ImageFile,

        /// <summary>Nebulae made from the system's own seed (<see cref="ProceduralSky"/>), under the point-cloud stars.</summary>
        Procedural,
    }

    /// <summary>
    /// The frame a panorama is laid out in (U.SPC-T595). Its centre column is longitude 0, longitude rises to the left (as the
    /// sky is seen from inside), and the top row is latitude +90, so a real sky map lands where its stars really are.
    /// </summary>
    public enum SkyFrame
    {
        /// <summary>The game's own J2000 ecliptic axes: the horizon of the picture lies along the ecliptic.</summary>
        Ecliptic,

        /// <summary>J2000 right ascension and declination (NASA SVS Deep Star Maps).</summary>
        Equatorial,

        /// <summary>Galactic longitude and latitude, the galactic centre in the middle (ESO's Milky Way panorama).</summary>
        Galactic,
    }

    /// <summary>One sky the player can cycle to (U.SPC-T595): where it comes from, how it is laid out and under what licence.</summary>
    public sealed class SkyboxSpec
    {
        /// <summary>The stable id it is remembered by in a profile (never shown).</summary>
        public readonly string Id;

        /// <summary>The name the toast, the settings menu and the manual show.</summary>
        public readonly string Name;

        public readonly SkyKind Kind;

        /// <summary>The Resources path of a shipped panorama (no extension), or the full path of a dropped-in file.</summary>
        public readonly string Path;

        public readonly SkyFrame Frame;

        /// <summary>Whether the point-cloud starfield is drawn over it: a photographed sky has its own stars.</summary>
        public readonly bool PointStars;

        /// <summary>Who made it, its licence and the credit it asks for (recorded in Resources/Skyboxes/SOURCES.md too).</summary>
        public readonly string Credit, Licence;

        public SkyboxSpec(string id, string name, SkyKind kind, string path, SkyFrame frame, bool pointStars, string credit, string licence)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A sky needs an id.", nameof(id));
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? id : name;
            Kind = kind;
            Path = path ?? "";
            Frame = frame;
            PointStars = pointStars;
            Credit = credit ?? "";
            Licence = licence ?? "";
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Every sky the game can draw behind a star system (U.SPC-T595): the panoramas shipped in Resources/Skyboxes, the
    /// system's own seeded nebulae, the bare starfield, and any panorama the player drops into the profile's Skyboxes folder
    /// (<see cref="Register"/>). Shift+K (or numpad *) cycles through them in the system in view; the choice is kept per
    /// system (<see cref="SkyboxMemory"/>). A new sky is one more <see cref="Register"/> call, never an edit to the view.
    /// </summary>
    public static class SkyboxCatalog
    {
        public const string SeededNebulaId = "seeded-nebula", DeepStarMapId = "nasa-deep-star-map", StarfieldId = "starfield";

        /// <summary>The folder under Resources the shipped panoramas live in.</summary>
        public const string ResourceFolder = "Skyboxes/";

        static readonly List<SkyboxSpec> all = new List<SkyboxSpec>();
        static readonly object gate = new object();

        /// <summary>The skies shipped with the game, in the order the key cycles them.</summary>
        public static readonly IReadOnlyList<SkyboxSpec> BuiltIn = new[]
        {
            new SkyboxSpec(SeededNebulaId, "This system's own nebulae", SkyKind.Procedural, "", SkyFrame.Ecliptic, true,
                "Generated in game from the system's seed, after wwwtyro's Space 3D (github.com/wwwtyro/space-3d)", "Unlicense (public domain)"),
            new SkyboxSpec(DeepStarMapId, "Real sky: NASA Deep Star Map", SkyKind.Image, ResourceFolder + "nasa_deep_star_map_8k", SkyFrame.Equatorial, false,
                "NASA/Goddard Space Flight Center Scientific Visualization Studio; Gaia DR2: ESA/Gaia/DPAC", "Public domain (NASA)"),
            new SkyboxSpec("eso-milky-way", "Real sky: ESO Milky Way panorama", SkyKind.Image, ResourceFolder + "eso_milky_way_6k", SkyFrame.Galactic, false,
                "ESO/S. Brunier", "CC BY 4.0"),
            new SkyboxSpec("spheremaps-blue-nebulae", "Blue nebulae", SkyKind.Image, ResourceFolder + "spheremaps_blue_nebulae_8k", SkyFrame.Ecliptic, false,
                "Space Spheremaps (space-spheremaps.itch.io)", "Free for any use, redistribution allowed, attribution appreciated"),
            new SkyboxSpec("spheremaps-hazy-nebulae", "Hazy nebulae", SkyKind.Image, ResourceFolder + "spheremaps_hazy_nebulae_8k", SkyFrame.Ecliptic, false,
                "Space Spheremaps (space-spheremaps.itch.io)", "Free for any use, redistribution allowed, attribution appreciated"),
            new SkyboxSpec("spheremaps-plain-starfield", "Deep starfield", SkyKind.Image, ResourceFolder + "spheremaps_plain_starfield_8k", SkyFrame.Ecliptic, false,
                "Space Spheremaps (space-spheremaps.itch.io)", "Free for any use, redistribution allowed, attribution appreciated"),
            new SkyboxSpec("spheremaps-ringed-gas-giant", "Ringed giant", SkyKind.Image, ResourceFolder + "spheremaps_ringed_gas_giant_8k", SkyFrame.Ecliptic, false,
                "Space Spheremaps (space-spheremaps.itch.io)", "Free for any use, redistribution allowed, attribution appreciated"),
            new SkyboxSpec(StarfieldId, "Bare starfield", SkyKind.Starfield, "", SkyFrame.Ecliptic, true, "The game's own point-cloud stars", "Part of the game"),
        };

        static SkyboxCatalog()
        {
            all.AddRange(BuiltIn);
        }

        /// <summary>Every sky there is, built-in first, then the dropped-in ones in the order they were found.</summary>
        public static IReadOnlyList<SkyboxSpec> All
        {
            get
            {
                lock (gate) return all.ToArray();
            }
        }

        /// <summary>Adds a sky (a dropped-in panorama); one whose id is already listed is refused and false returned.</summary>
        public static bool Register(SkyboxSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            lock (gate)
            {
                foreach (var s in all)
                    if (string.Equals(s.Id, spec.Id, StringComparison.Ordinal)) return false;
                all.Add(spec);
                return true;
            }
        }

        /// <summary>The sky with this id, or null.</summary>
        public static SkyboxSpec Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (gate)
                foreach (var s in all)
                    if (string.Equals(s.Id, id, StringComparison.Ordinal)) return s;
            return null;
        }

        /// <summary>The sky <paramref name="step"/> places on from <paramref name="current"/> in <see cref="All"/>, wrapping round.</summary>
        public static SkyboxSpec Step(SkyboxSpec current, int step)
        {
            var list = All;
            int i = -1;
            for (int k = 0; k < list.Count; k++)
                if (list[k] == current) { i = k; break; }
            if (i < 0) return list[0];
            int n = list.Count;
            return list[((i + step) % n + n) % n];
        }

        /// <summary>
        /// The sky a system shows before the player picks one: <see cref="SkyboxSettings.DefaultSky"/>, or with that left at
        /// "Per system", the real sky in Sol and the system's own seeded nebulae everywhere else.
        /// </summary>
        public static SkyboxSpec DefaultFor(string systemName)
        {
            int pick = SkyboxSettings.DefaultSky.Index;
            if (pick > 0 && pick - 1 < BuiltIn.Count) return BuiltIn[pick - 1];
            return Find(IsSol(systemName) ? DeepStarMapId : SeededNebulaId);
        }

        /// <summary>Whether <paramref name="systemName"/> is Sol (<see cref="Galaxy.CreateSol"/> names it "Sol System").</summary>
        public static bool IsSol(string systemName) =>
            string.Equals(systemName, "Sol System", StringComparison.Ordinal) || string.Equals(systemName, "Sol", StringComparison.Ordinal);

        /// <summary>
        /// A panorama frame's three axes in the game's J2000 ecliptic coordinates: longitude 0 (x), longitude 90 (y) and the
        /// north pole (z). A sky direction <c>d</c> reads at longitude atan2(d·y, d·x) and latitude asin(d·z).
        /// </summary>
        public static void FrameAxes(SkyFrame frame, out DVec3 x, out DVec3 y, out DVec3 z)
        {
            switch (frame)
            {
                case SkyFrame.Equatorial:
                    x = Astro.PoleFromRaDec(0, 0);
                    y = Astro.PoleFromRaDec(90, 0);
                    z = Astro.PoleFromRaDec(0, 90);
                    return;
                case SkyFrame.Galactic:
                    // The galactic centre and north galactic pole (J2000, Reid & Brunthaler 2004 / IAU 1958 frame).
                    x = Astro.PoleFromRaDec(266.40499, -28.93617);
                    z = Astro.PoleFromRaDec(192.85948, 27.12825);
                    y = DVec3.Cross(z, x).Normalized;
                    return;
                default:
                    x = DVec3.UnitX;
                    y = DVec3.UnitY;
                    z = DVec3.UnitZ;
                    return;
            }
        }

        /// <summary>Where direction <paramref name="d"/> (ecliptic) falls on a panorama in <paramref name="frame"/>: u, v in 0..1.</summary>
        public static void PanoramaUv(SkyFrame frame, DVec3 d, out double u, out double v)
        {
            FrameAxes(frame, out var ax, out var ay, out var az);
            d = d.Normalized;
            double lon = Math.Atan2(DVec3.Dot(d, ay), DVec3.Dot(d, ax));
            double lat = Math.Asin(Math.Max(-1, Math.Min(1, DVec3.Dot(d, az))));
            u = 0.5 - lon / (2 * Math.PI);
            u -= Math.Floor(u);
            v = 0.5 + lat / Math.PI;
        }
    }

    /// <summary>The sky's settings (U.SPC-T595), under Graphics.</summary>
    [SettingsSource]
    public static class SkyboxSettings
    {
        public const string PerSystem = "Per system (real sky in Sol, its own nebulae elsewhere)";

        static string[] DefaultOptions()
        {
            var options = new string[SkyboxCatalog.BuiltIn.Count + 1];
            options[0] = PerSystem;
            for (int i = 0; i < SkyboxCatalog.BuiltIn.Count; i++) options[i + 1] = SkyboxCatalog.BuiltIn[i].Name;
            return options;
        }

        /// <summary>What a system shows until the player picks a sky there with Shift+K.</summary>
        public static readonly ChoiceSetting DefaultSky = new ChoiceSetting("sky.default", SettingGroups.Graphics, "Skybox", DefaultOptions(), 0,
            "The sky behind every star system you have not picked one for (Shift+K or numpad * cycles the sky in the system in view, and that " +
            "system keeps it). The real skies are NASA's Deep Star Map and ESO's Milky Way panorama, laid where the stars really are; the " +
            "nebula skies are 8K panoramas; 'its own nebulae' is made from the system's seed; 'Bare starfield' is the old black sky.",
            "sky skybox background nebula stars milky way space backdrop");

        /// <summary>How bright the sky is drawn (the point-cloud stars keep their own brightness).</summary>
        public static readonly RangeSetting Brightness = new RangeSetting("sky.brightness", SettingGroups.Graphics, "Skybox brightness", 1, 0.1, 3, 0.05,
            "How bright the skybox is drawn behind everything. Lower keeps the sky from competing with ships and worlds.",
            v => v.ToString("0.##") + "x", "sky skybox brightness background dim");

        /// <summary>The seeded-nebula sky's resolution: a quarter of its panorama's width (a cube face's edge at the same detail).</summary>
        public static readonly ChoiceSetting NebulaResolution = new ChoiceSetting("sky.nebulaResolution", SettingGroups.Graphics, "Seeded nebula detail",
            new[] { "512", "1024", "2048" }, 0,
            "How finely a system's own nebulae are made, on background threads (the old sky stays up and the new one fades in when done): " +
            "512 is a 2048 x 1024 panorama, made in a second or two; 2048 is 8192 x 4096 and can take half a minute.",
            "sky skybox nebula procedural resolution detail");

        public static int NebulaFaceSize => int.Parse(NebulaResolution.Selected, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Which sky each star system was last given (U.SPC-T595), kept in the active profile's settings under
    /// <see cref="Key"/> as "system=sky" pairs, so every system keeps its own across sessions; a system never set shows
    /// <see cref="SkyboxCatalog.DefaultFor"/>. With no profile bound the choices last for the run.
    /// </summary>
    public sealed class SkyboxMemory
    {
        public const string Key = "sky.bySystem";

        readonly Func<ISettingStore> store;
        readonly Dictionary<string, string> chosen = new Dictionary<string, string>(StringComparer.Ordinal);
        ISettingStore loadedFrom;
        bool loaded;

        /// <summary>A memory over <paramref name="store"/> (read each time, so a profile switched later is followed).</summary>
        public SkyboxMemory(Func<ISettingStore> store = null) => this.store = store ?? (() => SettingsRegistry.Default.Store);

        /// <summary>The sky <paramref name="systemName"/> shows: the one picked there (if it still exists), else its default.</summary>
        public SkyboxSpec For(string systemName)
        {
            Sync();
            if (systemName != null && chosen.TryGetValue(systemName, out var id))
            {
                var spec = SkyboxCatalog.Find(id);
                if (spec != null) return spec;
            }
            return SkyboxCatalog.DefaultFor(systemName);
        }

        /// <summary>Whether the player picked this system's sky (rather than it showing the default).</summary>
        public bool IsChosen(string systemName)
        {
            Sync();
            return systemName != null && chosen.ContainsKey(systemName);
        }

        /// <summary>Gives <paramref name="systemName"/> <paramref name="spec"/> and writes it to the profile.</summary>
        public void Set(string systemName, SkyboxSpec spec)
        {
            if (systemName == null || spec == null) return;
            Sync();
            chosen[systemName] = spec.Id;
            Write();
        }

        /// <summary>Moves <paramref name="systemName"/>'s sky <paramref name="step"/> places on through the catalogue; returns it.</summary>
        public SkyboxSpec Cycle(string systemName, int step = 1)
        {
            var next = SkyboxCatalog.Step(For(systemName), step == 0 ? 1 : step);
            Set(systemName, next);
            return next;
        }

        /// <summary>Forgets every system's pick (each shows its default again).</summary>
        public void Clear()
        {
            Sync();
            chosen.Clear();
            Write();
        }

        void Sync()
        {
            var s = store();
            if (loaded && ReferenceEquals(s, loadedFrom)) return;
            loaded = true;
            loadedFrom = s;
            if (s == null) return; // no profile: keep what this run has chosen
            chosen.Clear();
            string text = null;
            try { text = s.ReadSetting(Key); }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException) { }
            foreach (var pair in Parse(text)) chosen[pair.Key] = pair.Value;
        }

        void Write()
        {
            var s = store();
            if (s == null) return;
            try
            {
                if (chosen.Count == 0) s.ForgetSetting(Key);
                else s.WriteSetting(Key, Format(chosen));
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException) { }
        }

        /// <summary>"Sol=nasa-deep-star-map;Alpha%20Centauri=seeded-nebula": names and ids escaped, sorted by system.</summary>
        public static string Format(IDictionary<string, string> picks)
        {
            var keys = new List<string>(picks.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var k in keys)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(picks[k]));
            }
            return sb.ToString();
        }

        /// <summary>Reads what <see cref="Format"/> wrote; damaged pairs are skipped.</summary>
        public static IEnumerable<KeyValuePair<string, string>> Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            foreach (var part in text.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0 || eq == part.Length - 1) continue;
                string name, id;
                try
                {
                    name = Uri.UnescapeDataString(part.Substring(0, eq));
                    id = Uri.UnescapeDataString(part.Substring(eq + 1));
                }
                catch (UriFormatException) { continue; }
                yield return new KeyValuePair<string, string>(name, id);
            }
        }
    }
}
