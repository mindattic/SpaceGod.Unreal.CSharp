using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Shared.Core;
using Unity.Shared.Core.Json;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Unity.SpaceGod's own settings, kept in the <c>game</c> block of the profile's one <c>settings.json</c>
    /// (the shared mindattic.settings v1 format from the Unity.Shared package, U.SPC-T325). The profiles
    /// themselves (current.txt, one folder each, Saves/) are the package's <see cref="ProfileStore"/>.
    /// </summary>
    public static class GameSettings
    {
        /// <summary>The window size, mode and VSync, as <c>DisplaySettings.Format()</c> writes them.</summary>
        public const string Display = "display";

        /// <summary>Whether the view swings level with each system's plane.</summary>
        public const string AutoReorient = "autoReorient";

        /// <summary>The text of <paramref name="key"/>, or null when it was never set or is not text.</summary>
        public static string Setting(this Profile profile, string key) => profile.Settings.Game.GetString(key);

        /// <summary>Sets <paramref name="key"/> and writes settings.json (throws when the disk refuses; the old file is then intact).</summary>
        public static void SetSetting(this Profile profile, string key, string value)
        {
            profile.Settings.Game.Set(key, value ?? "");
            profile.SaveSettings();
        }

        /// <summary>An on/off setting, or <paramref name="fallback"/> when it was never set or is not one.</summary>
        public static bool Flag(this Profile profile, string key, bool fallback) => profile.Settings.Game.GetBool(key, fallback);

        /// <summary>Sets an on/off setting and writes settings.json.</summary>
        public static void SetFlag(this Profile profile, string key, bool on)
        {
            profile.Settings.Game.Set(key, on);
            profile.SaveSettings();
        }
    }

    /// <summary>
    /// Moves profiles written before U.SPC-T325, which kept each setting in a file of its own
    /// (<c>Profiles/&lt;Name&gt;/Settings/display.txt</c>, <c>auto-reorient.txt</c>...), onto the single
    /// <c>Profiles/&lt;Name&gt;/settings.json</c>: every <c>&lt;setting&gt;.txt</c> goes into the <c>game</c> block under
    /// its camelCase name (<c>true</c>/<c>false</c> as a JSON bool, anything else as text), a value already in
    /// settings.json wins, and the old <c>Settings</c> folder is then kept aside as <c>Settings.old</c>, never erased.
    /// current.txt and Saves/ are not touched.
    /// </summary>
    public static class LegacyProfileSettings
    {
        /// <summary>The per-setting folder profiles had before settings.json.</summary>
        public const string FolderName = "Settings";

        /// <summary>What the old folder is renamed to once its settings are in settings.json.</summary>
        public const string KeptAsideName = "Settings.old";

        /// <summary>Migrates every profile in <paramref name="store"/> that still has a Settings folder; returns the names of those migrated.</summary>
        public static List<string> MigrateAll(ProfileStore store)
        {
            var migrated = new List<string>();
            foreach (var name in store.List())
            {
                var profile = store.Get(name);
                if (profile != null && Migrate(profile)) migrated.Add(profile.Name);
            }
            return migrated;
        }

        /// <summary>
        /// Migrates one profile. Returns true when it had a Settings folder and it was moved into settings.json and
        /// kept aside; false when there was nothing to do or the disk refused (the old files are then left as they
        /// were, to be tried again next run).
        /// </summary>
        public static bool Migrate(Profile profile)
        {
            string folder = Path.Combine(profile.Folder, FolderName);
            if (!Directory.Exists(folder)) return false;
            try
            {
                var game = profile.Settings.Game;
                foreach (var file in Directory.GetFiles(folder, "*.txt"))
                {
                    string key = KeyFor(Path.GetFileNameWithoutExtension(file));
                    if (key.Length == 0 || game.ContainsKey(key)) continue;
                    string text = File.ReadAllText(file, Encoding.UTF8).Trim();
                    if (bool.TryParse(text, out bool on)) game.Set(key, on);
                    else game.Set(key, text);
                }
                profile.SaveSettings();
                Directory.Move(folder, KeptAsidePath(profile.Folder));
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>A setting file's name as a settings.json key: "auto-reorient" is "autoReorient".</summary>
        public static string KeyFor(string fileName)
        {
            var key = new StringBuilder();
            bool upper = false;
            foreach (char c in fileName.Trim())
            {
                if (c == '-' || c == '_' || c == ' ')
                {
                    upper = key.Length > 0;
                    continue;
                }
                key.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            return key.ToString();
        }

        static string KeptAsidePath(string profileFolder)
        {
            string target = Path.Combine(profileFolder, KeptAsideName);
            if (!Directory.Exists(target) && !File.Exists(target)) return target;
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            target = Path.Combine(profileFolder, KeptAsideName + "-" + stamp);
            for (int i = 2; Directory.Exists(target) || File.Exists(target); i++)
                target = Path.Combine(profileFolder, KeptAsideName + "-" + stamp + "-" + i);
            return target;
        }
    }

    /// <summary>
    /// The settings registry's store (U.SPC-T431) over a profile's settings.json: each declared setting is a field
    /// of the <c>game</c> block under its key, an on/off one as a JSON bool and a number as a JSON number (so the
    /// auto-reorient flag reads and writes exactly as it always has), anything else as text. Each change is
    /// written to settings.json at once; one back at its default is removed.
    /// </summary>
    public sealed class ProfileSettingStore : ISettingStore
    {
        readonly Profile profile;

        public ProfileSettingStore(Profile profile) => this.profile = profile ?? throw new ArgumentNullException(nameof(profile));

        public string ReadSetting(string key)
        {
            object v = profile.Settings.Game[key];
            switch (v)
            {
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case JsonNumber n: return n.Raw;
                default: return null;
            }
        }

        public void WriteSetting(string key, string value)
        {
            var game = profile.Settings.Game;
            if (value == "true" || value == "false") game.Set(key, value == "true");
            else if (value != null && MiniJson.IsNumber(value)) game.Set(key, JsonNumber.FromRaw(value));
            else game.Set(key, value ?? "");
            profile.SaveSettings();
        }

        public void ForgetSetting(string key)
        {
            if (profile.Settings.Game.Remove(key)) profile.SaveSettings();
        }
    }
}
