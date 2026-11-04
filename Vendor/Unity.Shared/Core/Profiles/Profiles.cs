using System;
using System.IO;
using Unity.Shared.Core.Json;

namespace Unity.Shared.Core
{
    /// <summary>
    /// The one-line way in: <c>Profiles.Current.Settings.Common.DisplayName</c>. In Unity the package's runtime
    /// assembly points this at <c>Application.persistentDataPath</c> before the first scene loads; anywhere else
    /// (dotnet tests, tools) call <see cref="UseRoot"/> or <see cref="UseDataPath"/> first, or set the
    /// <see cref="RootEnvironmentVariable"/> environment variable.
    /// </summary>
    public static class Profiles
    {
        /// <summary>An environment variable naming a Profiles folder to use instead of the data path's (tests, automation, CI).</summary>
        public const string RootEnvironmentVariable = "MINDATTIC_PROFILES_ROOT";

        /// <summary>A command-line switch naming a Profiles folder, as <c>-profiles-root &lt;path&gt;</c> or <c>-profiles-root=&lt;path&gt;</c>.</summary>
        public const string RootCommandLineSwitch = "-profiles-root";

        /// <summary>An environment variable that, set to <c>1</c> (or <c>true</c>/<c>yes</c>/<c>on</c>), mutes all audio for this session (see <see cref="SessionMuted"/>).</summary>
        public const string MutedEnvironmentVariable = "MINDATTIC_CLI_MUTED";

        /// <summary>A command-line switch that mutes all audio for this session, as <c>-muted</c> (see <see cref="SessionMuted"/>).</summary>
        public const string MutedCommandLineSwitch = "-muted";

        static ProfileStore store;

        /// <summary>
        /// A session-only audio mute: while true, every profile's <see cref="CommonSettings"/> reports music and
        /// sound effects muted at volume 0, but the stored values are untouched, so nothing reaches settings.json
        /// (saving writes the player's real values). The Unity bootstrap turns it on from
        /// <see cref="MutedEnvironmentVariable"/> or <see cref="MutedCommandLineSwitch"/> before the first scene
        /// loads, so a CLI or agent launching a game does not play sound.
        /// </summary>
        public static bool SessionMuted { get; set; }

        /// <summary>Whether the environment variable or command line asks for a muted session.</summary>
        public static bool MutedOverride()
        {
            string env = Environment.GetEnvironmentVariable(MutedEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(env))
            {
                switch (env.Trim().ToLowerInvariant())
                {
                    case "1": case "true": case "yes": case "on": return true;
                }
            }
            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch (NotSupportedException)
            {
                return false;
            }
            foreach (string a in args)
                if (string.Equals(a, MutedCommandLineSwitch, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Turns <see cref="SessionMuted"/> on if <see cref="MutedOverride"/> asks for it (never turns it off). Returns <see cref="SessionMuted"/>.</summary>
        public static bool ApplyMutedOverride()
        {
            if (MutedOverride()) SessionMuted = true;
            return SessionMuted;
        }

        /// <summary>Whether a store has been set (by the Unity bootstrap, <see cref="UseRoot"/>, <see cref="UseDataPath"/> or the override).</summary>
        public static bool IsConfigured => store != null;

        /// <summary>
        /// The store in use. If none was set but the environment variable or command-line override names a
        /// folder, that is used; otherwise this throws <see cref="InvalidOperationException"/>.
        /// </summary>
        public static ProfileStore Store
        {
            get
            {
                if (store != null) return store;
                string overrideRoot = OverrideRoot();
                if (overrideRoot != null) return store = new ProfileStore(overrideRoot);
                throw new InvalidOperationException(
                    "Profiles has no folder yet. In Unity the Unity.Shared assembly sets it; elsewhere call Profiles.UseRoot(path) or Profiles.UseDataPath(path), or set " + RootEnvironmentVariable + ".");
            }
        }

        /// <summary>The profile in use (see <see cref="ProfileStore.Current"/>).</summary>
        public static Profile Current => Store.Current;

        /// <summary>
        /// Uses <c>&lt;dataPath&gt;/Profiles</c>, unless the environment variable or command-line override names
        /// another folder, which wins. Returns the store.
        /// </summary>
        public static ProfileStore UseDataPath(string dataPath, IJsonCodec codec = null)
        {
            string overrideRoot = OverrideRoot();
            store = overrideRoot != null ? new ProfileStore(overrideRoot, codec) : ProfileStore.ForDataPath(dataPath, codec);
            return store;
        }

        /// <summary>Uses exactly <paramref name="profilesRoot"/> as the Profiles folder (no override applies). Returns the store.</summary>
        public static ProfileStore UseRoot(string profilesRoot, IJsonCodec codec = null) => store = new ProfileStore(profilesRoot, codec);

        /// <summary>Uses a store you built. Returns it.</summary>
        public static ProfileStore Use(ProfileStore profileStore) => store = profileStore ?? throw new ArgumentNullException(nameof(profileStore));

        /// <summary>Forgets the store, so the next use needs one set again (tests).</summary>
        public static void Reset() => store = null;

        /// <summary>The Profiles folder the environment variable or command line names, or null.</summary>
        public static string OverrideRoot()
        {
            string env = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch (NotSupportedException)
            {
                return null;
            }
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string value = null;
                if (string.Equals(a, RootCommandLineSwitch, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    value = args[i + 1];
                else if (a.StartsWith(RootCommandLineSwitch + "=", StringComparison.OrdinalIgnoreCase))
                    value = a.Substring(RootCommandLineSwitch.Length + 1);
                // A blank value ("-profiles-root=") is no override, as for the environment variable; passing it
                // on would make ProfileStore throw from the Unity bootstrap.
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return null;
        }
    }
}
