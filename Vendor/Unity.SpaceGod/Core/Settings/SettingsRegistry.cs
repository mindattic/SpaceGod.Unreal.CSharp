using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Marks a class whose static fields declare <see cref="GameSetting"/>s (U.SPC-T431).
    /// <see cref="SettingsRegistry.DiscoverDeclarations"/> runs each such class's static initialiser, so its settings
    /// are in the menu before anything else has touched the class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SettingsSourceAttribute : Attribute
    {
    }

    /// <summary>Where a registry remembers values: a player profile's settings files, or memory in a test.</summary>
    public interface ISettingStore
    {
        /// <summary>The text last written for <paramref name="key"/>, or null when none was (or it cannot be read).</summary>
        string ReadSetting(string key);

        void WriteSetting(string key, string value);

        /// <summary>Forgets <paramref name="key"/>, so its default stands (and a later change to the default reaches it).</summary>
        void ForgetSetting(string key);
    }

    /// <summary>
    /// Every setting the game's systems declare (U.SPC-T431), grouped and searchable for the settings menu, each
    /// remembered in the bound <see cref="ISettingStore"/> (the active profile) the moment it changes. A setting
    /// at its default is not written, so a value never chosen follows the default if a later version moves it.
    /// </summary>
    public sealed class SettingsRegistry
    {
        /// <summary>The game's own registry, which declarations join unless they name another.</summary>
        public static SettingsRegistry Default { get; } = new SettingsRegistry();

        readonly List<GameSetting> settings = new List<GameSetting>();
        readonly Dictionary<string, GameSetting> byKey = new Dictionary<string, GameSetting>(StringComparer.Ordinal);
        readonly object gate = new object();
        ISettingStore store;

        /// <summary>Raised when the store refuses a write (a full or read-only disk): the value still stands for this run.</summary>
        public event Action<GameSetting, Exception> StoreFailed;

        public IReadOnlyList<GameSetting> All
        {
            get
            {
                lock (gate) return settings.ToArray();
            }
        }

        public ISettingStore Store => store;

        /// <summary>Adds <paramref name="setting"/>; a second setting with the same key is a mistake and refused.</summary>
        public void Register(GameSetting setting)
        {
            if (setting == null) throw new ArgumentNullException(nameof(setting));
            lock (gate)
            {
                if (byKey.ContainsKey(setting.Key)) throw new InvalidOperationException("Two settings declare the key '" + setting.Key + "'.");
                byKey.Add(setting.Key, setting);
                settings.Add(setting);
            }
            setting.Attach(this);
            // Declared after a profile was bound (a class first touched later): take the profile's value now.
            var s = store;
            if (s != null) Load(setting, s);
        }

        int discovered;

        /// <summary>The setting declared under <paramref name="key"/>, or null. The first miss in the default registry runs
        /// every marked class's declarations (<see cref="DiscoverLoadedDeclarations"/>), so a setting whose class nothing has
        /// touched yet is still found, whatever order the lookups come in.</summary>
        public GameSetting Find(string key)
        {
            if (key == null) return null;
            lock (gate) { if (byKey.TryGetValue(key, out var s)) return s; }
            if (this == Default && System.Threading.Interlocked.Exchange(ref discovered, 1) == 0)
            {
                DiscoverLoadedDeclarations();
                lock (gate) { if (byKey.TryGetValue(key, out var s)) return s; }
            }
            return null;
        }

        /// <summary>The groups that hold settings: those of <see cref="SettingGroups.Order"/> first, in that order, then the rest by name.</summary>
        public List<string> Groups()
        {
            var present = new HashSet<string>(All.Select(s => s.Group));
            var list = SettingGroups.Order.Where(present.Contains).ToList();
            list.AddRange(present.Where(g => Array.IndexOf(SettingGroups.Order, g) < 0).OrderBy(g => g, StringComparer.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>The settings in <paramref name="group"/>, in the order they were declared.</summary>
        public List<GameSetting> InGroup(string group) => All.Where(s => s.Group == group).ToList();

        /// <summary>The settings every word of <paramref name="query"/> finds (in label, group, description or keywords),
        /// by group as the menu orders them; every setting for an empty query.</summary>
        public List<GameSetting> Search(string query)
        {
            var groups = Groups();
            return All.Where(s => s.Matches(query)).OrderBy(s => groups.IndexOf(s.Group)).ToList();
        }

        /// <summary>
        /// Remembers values in <paramref name="newStore"/> from now on, and takes each setting's value from it (its
        /// default when it has none, or a damaged one). Every setting's <see cref="GameSetting.Changed"/> is raised,
        /// so what each system put into effect follows the profile. Null unbinds: values stay but are not written.
        /// </summary>
        public void Bind(ISettingStore newStore)
        {
            store = newStore;
            if (newStore == null) return;
            foreach (var s in All) Load(s, newStore);
        }

        static void Load(GameSetting s, ISettingStore from)
        {
            s.SetToDefault();
            string text = null;
            try { text = from.ReadSetting(s.Key); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            if (text != null) s.TryLoad(text.Trim());
            s.RaiseChanged();
        }

        /// <summary>Writes <paramref name="s"/>'s new value (or forgets it, back at its default).</summary>
        internal void Remember(GameSetting s)
        {
            var to = store;
            if (to == null) return;
            try
            {
                if (s.IsDefault) to.ForgetSetting(s.Key);
                else to.WriteSetting(s.Key, s.Format());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                StoreFailed?.Invoke(s, e);
            }
        }

        /// <summary>Puts <paramref name="s"/> back to its default and forgets it in the profile.</summary>
        public void Reset(GameSetting s)
        {
            if (s == null) return;
            bool changed = !s.IsDefault;
            s.SetToDefault();
            var to = store;
            if (to != null)
            {
                try { to.ForgetSetting(s.Key); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { StoreFailed?.Invoke(s, e); }
            }
            if (changed) s.RaiseChanged();
        }

        /// <summary>Puts every setting in <paramref name="group"/> back to its default.</summary>
        public void ResetGroup(string group)
        {
            foreach (var s in InGroup(group)) Reset(s);
        }

        public void ResetAll()
        {
            foreach (var s in All) Reset(s);
        }

        /// <summary>
        /// Runs the static initialiser of every class marked <see cref="SettingsSourceAttribute"/> in
        /// <paramref name="assemblies"/> (only those that are, or use, this one are looked through), so the settings
        /// they declare are registered. Returns how many such classes there are.
        /// </summary>
        public static int DiscoverDeclarations(IEnumerable<Assembly> assemblies)
        {
            var core = typeof(SettingsRegistry).Assembly;
            string coreName = core.GetName().Name;
            int found = 0;
            foreach (var asm in assemblies)
            {
                if (asm == null || asm.IsDynamic) continue;
                if (asm != core && !asm.GetReferencedAssemblies().Any(r => r.Name == coreName)) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var t in types)
                {
                    if (!t.IsDefined(typeof(SettingsSourceAttribute), false)) continue;
                    RuntimeHelpers.RunClassConstructor(t.TypeHandle);
                    found++;
                }
            }
            return found;
        }

        /// <summary>Every loaded assembly's declarations (<see cref="DiscoverDeclarations"/>).</summary>
        public static int DiscoverLoadedDeclarations() => DiscoverDeclarations(AppDomain.CurrentDomain.GetAssemblies());
    }

    /// <summary>A store kept in memory: settings nobody needs to remember between runs, and tests.</summary>
    public sealed class MemorySettingStore : ISettingStore
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);

        public string ReadSetting(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void WriteSetting(string key, string value) => Values[key] = value;
        public void ForgetSetting(string key) => Values.Remove(key);
    }
}
