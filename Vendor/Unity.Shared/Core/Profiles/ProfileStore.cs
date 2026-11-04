using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Shared.Core.Json;

namespace Unity.Shared.Core
{
    /// <summary>
    /// The player profiles under one Profiles folder (normally <c>&lt;persistentDataPath&gt;/Profiles</c>):
    /// a folder per profile holding its <c>settings.json</c> and <c>Saves/</c>, a <c>current.txt</c> naming the one
    /// in use, and <c>.trash/</c> for deleted ones. Plain files, the same on every platform. One instance
    /// hands out one <see cref="Profile"/> object per profile, so settings changed through it are seen everywhere.
    /// Use it from one thread (Unity's main thread).
    /// </summary>
    public sealed class ProfileStore
    {
        /// <summary>The profile made when there is none.</summary>
        public const string DefaultName = "Default";

        /// <summary>The folder under a data path that holds the profiles.</summary>
        public const string FolderName = "Profiles";

        /// <summary>The file naming the profile in use.</summary>
        public const string CurrentFileName = "current.txt";

        /// <summary>Where deleted profiles go (HOUSE-LAW-2: nothing is destroyed, only moved aside).</summary>
        public const string TrashFolderName = ".trash";

        /// <summary>The longest profile name.</summary>
        public const int MaxNameLength = 64;

        static readonly char[] ForbiddenChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

        static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        readonly Dictionary<string, Profile> open = new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A store over the Profiles folder <paramref name="root"/> (the override tests and automation use).</summary>
        public ProfileStore(string root, IJsonCodec codec = null)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A profiles folder is needed.", nameof(root));
            Root = Path.GetFullPath(root);
            Codec = codec ?? MiniJsonCodec.Instance;
        }

        /// <summary>The store under <c>&lt;dataPath&gt;/Profiles</c>, e.g. Unity's <c>Application.persistentDataPath</c>.</summary>
        public static ProfileStore ForDataPath(string dataPath, IJsonCodec codec = null) =>
            new ProfileStore(Path.Combine(dataPath, FolderName), codec);

        /// <summary>The Profiles folder.</summary>
        public string Root { get; }

        /// <summary>How settings files are read and written.</summary>
        public IJsonCodec Codec { get; }

        /// <summary>The path of <c>current.txt</c>.</summary>
        public string CurrentFile => Path.Combine(Root, CurrentFileName);

        /// <summary>The folder deleted profiles are moved into.</summary>
        public string TrashFolder => Path.Combine(Root, TrashFolderName);

        /// <summary>
        /// A name that makes one folder on every platform: not blank, at most 64 characters, no leading or
        /// trailing spaces, not starting with '.' or ending with '.', no path or wildcard characters
        /// (<c>/ \ : * ? " &lt; &gt; |</c>) and no control characters, and not a Windows device name (CON, NUL, COM1...).
        /// </summary>
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength || name != name.Trim()) return false;
            if (name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith(".", StringComparison.Ordinal)) return false;
            if (name.IndexOfAny(ForbiddenChars) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            foreach (char c in name) if (char.IsControl(c)) return false;
            int dot = name.IndexOf('.');
            string stem = dot < 0 ? name : name.Substring(0, dot);
            return !ReservedNames.Contains(stem.TrimEnd());
        }

        /// <summary>Every profile's name, sorted ignoring case.</summary>
        public IReadOnlyList<string> List() =>
            Directory.Exists(Root)
                ? Directory.GetDirectories(Root).Select(Path.GetFileName).Where(IsValidName)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();

        /// <summary>Whether a profile called <paramref name="name"/> exists (ignoring case: two names differing only in case would share a folder on Windows).</summary>
        public bool Exists(string name) => FolderNameOf(name) != null;

        /// <summary>The profile called <paramref name="name"/> (ignoring case), or null when there is none.</summary>
        public Profile Get(string name)
        {
            string actual = FolderNameOf(name);
            if (actual == null) return null;
            if (open.TryGetValue(actual, out var profile) && profile.Name == actual) return profile;
            profile = new Profile(this, actual);
            open[actual] = profile;
            return profile;
        }

        /// <summary>
        /// The profile in use: the one <c>current.txt</c> names; if it names none that exists, the first profile
        /// by name; if there are none, a new <see cref="DefaultName"/> profile. Whichever it is, it is then recorded
        /// in <c>current.txt</c>.
        /// </summary>
        public Profile Current
        {
            get
            {
                string name = ReadCurrentName();
                var profile = IsValidName(name) ? Get(name) : null;
                if (profile != null) return profile;
                var names = List();
                return Select(names.Count > 0 ? names[0] : Create(DefaultName).Name);
            }
        }

        /// <summary>Makes <paramref name="name"/> the profile in use. Throws when there is no such profile.</summary>
        public Profile Select(string name)
        {
            var profile = Get(name) ?? throw new ArgumentException("There is no profile called " + name + ".", nameof(name));
            AtomicFile.WriteAllText(CurrentFile, profile.Name + "\n");
            return profile;
        }

        /// <summary>
        /// Makes a new profile: its folder, its <c>Saves/</c> folder and a default <c>settings.json</c>. Throws
        /// <see cref="ArgumentException"/> for a bad name and <see cref="InvalidOperationException"/> when it exists.
        /// It does not become current; call <see cref="Select"/> for that.
        /// </summary>
        public Profile Create(string name)
        {
            RequireValid(name, nameof(name));
            if (Exists(name)) throw new InvalidOperationException("A profile called " + name + " already exists.");
            var profile = new Profile(this, name);
            Directory.CreateDirectory(profile.SavesFolder);
            profile.SaveSettings();
            open[name] = profile;
            return profile;
        }

        /// <summary>
        /// Renames a profile's folder (a change of case alone is allowed). The <see cref="Profile"/> object keeps
        /// working under its new name, and <c>current.txt</c> follows it if it was the profile in use.
        /// </summary>
        public Profile Rename(string name, string newName)
        {
            RequireValid(newName, nameof(newName));
            var profile = Get(name) ?? throw new ArgumentException("There is no profile called " + name + ".", nameof(name));
            if (profile.Name == newName) return profile;
            bool caseOnly = string.Equals(profile.Name, newName, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && Exists(newName)) throw new InvalidOperationException("A profile called " + newName + " already exists.");

            bool wasCurrent = string.Equals(ReadCurrentName(), profile.Name, StringComparison.OrdinalIgnoreCase);
            string from = profile.Folder;
            string to = Path.Combine(Root, newName);
            if (caseOnly)
            {
                // A case-only move is a no-op or an error on case-insensitive file systems: go through a spare name.
                string spare = Path.Combine(Root, ".rename-" + Guid.NewGuid().ToString("N"));
                Directory.Move(from, spare);
                try
                {
                    Directory.Move(spare, to);
                }
                catch
                {
                    // Left under the hidden spare name the profile would vanish from List(): put it back.
                    Directory.Move(spare, from);
                    throw;
                }
            }
            else
            {
                Directory.Move(from, to);
            }

            open.Remove(profile.Name);
            profile.Renamed(newName);
            open[newName] = profile;
            if (wasCurrent) AtomicFile.WriteAllText(CurrentFile, newName + "\n");
            return profile;
        }

        /// <summary>
        /// Deletes a profile by moving its folder to <c>.trash/&lt;name&gt;~&lt;UTC timestamp&gt;</c>, never by erasing
        /// it. If it was in use, <c>current.txt</c> is cleared and <see cref="Current"/> picks another next time.
        /// Returns where the folder went.
        /// </summary>
        public string Delete(string name)
        {
            var profile = Get(name) ?? throw new ArgumentException("There is no profile called " + name + ".", nameof(name));
            bool wasCurrent = string.Equals(ReadCurrentName(), profile.Name, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(TrashFolder);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            string target = Path.Combine(TrashFolder, profile.Name + "~" + stamp);
            for (int i = 2; Directory.Exists(target) || File.Exists(target); i++)
                target = Path.Combine(TrashFolder, profile.Name + "~" + stamp + "-" + i);
            Directory.Move(profile.Folder, target);
            open.Remove(profile.Name);
            if (wasCurrent && File.Exists(CurrentFile)) File.Delete(CurrentFile);
            return target;
        }

        string ReadCurrentName()
        {
            try
            {
                return File.Exists(CurrentFile) ? File.ReadAllText(CurrentFile).Trim() : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        string FolderNameOf(string name)
        {
            if (!IsValidName(name) || !Directory.Exists(Root)) return null;
            foreach (var existing in List())
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)) return existing;
            return null;
        }

        static void RequireValid(string name, string param)
        {
            if (!IsValidName(name)) throw new ArgumentException("Not a valid profile name: \"" + name + "\".", param);
        }
    }

    /// <summary>
    /// One player profile: a folder with <c>settings.json</c> (see <see cref="Settings"/>) and <c>Saves/</c>
    /// for the game's own save files. Get it from <see cref="ProfileStore"/> or <see cref="Profiles.Current"/>.
    /// </summary>
    public sealed class Profile
    {
        readonly ProfileStore store;
        ProfileSettings settings;

        internal Profile(ProfileStore store, string name)
        {
            this.store = store;
            Name = name;
        }

        /// <summary>The profile's name, which is also its folder's name.</summary>
        public string Name { get; private set; }

        /// <summary>The profile's folder.</summary>
        public string Folder => Path.Combine(store.Root, Name);

        /// <summary>The path of its settings.json.</summary>
        public string SettingsPath => Path.Combine(Folder, ProfileSettings.FileName);

        /// <summary>The folder for the game's own save files (made by <see cref="ProfileStore.Create"/>; call <see cref="EnsureSavesFolder"/> to be sure).</summary>
        public string SavesFolder => Path.Combine(Folder, "Saves");

        /// <summary>Makes <see cref="SavesFolder"/> if it is missing and returns it.</summary>
        public string EnsureSavesFolder()
        {
            Directory.CreateDirectory(SavesFolder);
            return SavesFolder;
        }

        /// <summary>
        /// The profile's settings, read from disk the first time they are asked for (never throws: a missing or
        /// damaged file gives defaults, see <see cref="ProfileSettings.Status"/>). Changes stay in memory until
        /// <see cref="SaveSettings"/>.
        /// </summary>
        public ProfileSettings Settings => settings ?? (settings = ProfileSettings.Load(SettingsPath, store.Codec, () => Name));

        /// <summary>Writes <see cref="Settings"/> to settings.json atomically. Throws on an I/O failure; the old file is then untouched.</summary>
        public void SaveSettings() => Settings.Save(SettingsPath, store.Codec);

        /// <summary>As <see cref="SaveSettings"/>, but returns false instead of throwing when the disk refuses.</summary>
        public bool TrySaveSettings()
        {
            try
            {
                SaveSettings();
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Drops unsaved changes and reads settings.json again.</summary>
        public ProfileSettings ReloadSettings()
        {
            settings = null;
            return Settings;
        }

        internal void Renamed(string newName) => Name = newName;

        /// <inheritdoc />
        public override string ToString() => Name;
    }
}
