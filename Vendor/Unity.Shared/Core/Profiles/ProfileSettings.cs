using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Shared.Core.Json;

namespace Unity.Shared.Core
{
    /// <summary>How a settings file load went.</summary>
    public enum SettingsLoadStatus
    {
        /// <summary>The file was read.</summary>
        Loaded,

        /// <summary>There was no file; defaults are in use.</summary>
        Missing,

        /// <summary>The file could not be read as settings; it was moved aside (see <see cref="ProfileSettings.MovedAsidePath"/>) and defaults are in use.</summary>
        Damaged,
    }

    /// <summary>
    /// One profile's settings.json (format <c>mindattic.settings</c>, version 1):
    /// <c>{"format":"mindattic.settings","version":1,"common":{...},"game":{...}}</c>.
    /// <see cref="Common"/> is the block every MindAttic game shares; <see cref="Game"/> belongs to the game.
    /// Unknown fields anywhere in the file are kept and written back.
    /// </summary>
    public sealed class ProfileSettings
    {
        /// <summary>The value of the file's <c>"format"</c> field.</summary>
        public const string Format = "mindattic.settings";

        /// <summary>The format version this code writes.</summary>
        public const int CurrentVersion = 1;

        /// <summary>The settings file's name inside a profile folder.</summary>
        public const string FileName = "settings.json";

        /// <summary>A damaged file is renamed to its name plus this plus a UTC timestamp.</summary>
        public const string DamagedSuffix = ".bad-";

        readonly Func<string> profileName;

        ProfileSettings(JsonObject root, Func<string> profileName)
        {
            this.profileName = profileName;
            Root = root;
            if (!(root["format"] is string)) root.Set("format", Format);
            if (!(root["version"] is JsonNumber)) root.Set("version", CurrentVersion);
            if (!(root["common"] is JsonObject)) root.Set("common", new JsonObject());
            if (!(root["game"] is JsonObject)) root.Set("game", new JsonObject());
            Common = new CommonSettings(root.GetObject("common"), profileName);
            Common.FillDefaults();
        }

        /// <summary>The whole file as JSON, unknown top-level fields included.</summary>
        public JsonObject Root { get; }

        /// <summary>The shared block: volumes, mutes, reduce motion, language, display name, colour.</summary>
        public CommonSettings Common { get; }

        /// <summary>
        /// The game's own block, free-form. Read and write it with <see cref="JsonObject"/>'s getters and
        /// <see cref="JsonObject.Set"/>, e.g. <c>Game.GetFloat("panSpeed", 0.05f)</c>, <c>Game.Set("panSpeed", 0.1f)</c>.
        /// </summary>
        public JsonObject Game => Root.GetOrAddObject("game");

        /// <summary>The file's format version (a newer file keeps its number when saved by this code).</summary>
        public int Version => Root.GetInt("version", CurrentVersion);

        /// <summary>How the last load went.</summary>
        public SettingsLoadStatus Status { get; private set; } = SettingsLoadStatus.Missing;

        /// <summary>Where a damaged file was moved to, when <see cref="Status"/> is <see cref="SettingsLoadStatus.Damaged"/>.</summary>
        public string MovedAsidePath { get; private set; }

        /// <summary>Fresh default settings for a profile called <paramref name="profileName"/>.</summary>
        public static ProfileSettings CreateDefault(string profileName = null) => new ProfileSettings(new JsonObject(), () => profileName);

        internal static ProfileSettings CreateDefault(Func<string> profileName) => new ProfileSettings(new JsonObject(), profileName);

        /// <summary>Settings read from JSON text; throws when the text is not a settings file.</summary>
        public static ProfileSettings FromJson(string json, IJsonCodec codec = null, string profileName = null) =>
            FromJson(json, codec, () => profileName);

        static ProfileSettings FromJson(string json, IJsonCodec codec, Func<string> profileName)
        {
            var root = (codec ?? MiniJsonCodec.Instance).Parse(json) ?? throw new JsonFormatException("The codec returned no object.", 0);
            var format = root["format"];
            if (format != null && !(format is string s && s == Format))
                throw new JsonFormatException("Not a " + Format + " file (format is " + format + ").", 0);
            return new ProfileSettings(root, profileName);
        }

        /// <summary>The settings as JSON text.</summary>
        public string ToJson(IJsonCodec codec = null) => (codec ?? MiniJsonCodec.Instance).Write(Root);

        /// <summary>
        /// Reads the settings at <paramref name="path"/>. Never throws for a bad file: a missing one gives
        /// defaults; one that cannot be read or parsed is moved aside to
        /// <c>settings.json.bad-&lt;yyyyMMddTHHmmssfffZ&gt;</c> and defaults are used.
        /// </summary>
        public static ProfileSettings Load(string path, IJsonCodec codec = null, string profileName = null) =>
            Load(path, codec, () => profileName);

        internal static ProfileSettings Load(string path, IJsonCodec codec, Func<string> profileName)
        {
            if (!File.Exists(path)) return CreateDefault(profileName);
            try
            {
                var settings = FromJson(File.ReadAllText(path, Encoding.UTF8), codec, profileName);
                settings.Status = SettingsLoadStatus.Loaded;
                return settings;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                var fresh = CreateDefault(profileName);
                fresh.Status = SettingsLoadStatus.Damaged;
                fresh.MovedAsidePath = MoveAside(path);
                return fresh;
            }
        }

        /// <summary>Writes the settings to <paramref name="path"/> atomically (temp file, then replace).</summary>
        public void Save(string path, IJsonCodec codec = null) => AtomicFile.WriteAllText(path, ToJson(codec));

        static string MoveAside(string path)
        {
            try
            {
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
                string target = path + DamagedSuffix + stamp;
                for (int i = 2; File.Exists(target); i++) target = path + DamagedSuffix + stamp + "-" + i;
                File.Move(path, target);
                return target;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null; // could not move it; defaults are still used and the next save replaces it
            }
        }
    }
}
