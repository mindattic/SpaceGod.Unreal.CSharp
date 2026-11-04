using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The groups the settings menu shows, in the order it shows them (U.SPC-T431). A setting may name a group
    /// that is not here; it is listed after these, by name. A group with no settings declared in it is not shown.
    /// </summary>
    public static class SettingGroups
    {
        public const string Graphics = "Graphics";
        public const string RenderPipeline = "Render pipeline";
        public const string RenderBudget = "LOD & render budget";
        public const string Streaming = "Streaming";
        public const string Simulation = "Simulation";
        public const string Ftl = "FTL";
        public const string Civilizations = "Civilizations";
        public const string Ai = "AI";
        public const string Dogfighting = "Dogfighting";
        public const string Broadsides = "Broadsides";
        public const string Weapons = "Weapons";
        public const string Economy = "Economy";
        public const string Controls = "Controls";
        public const string Audio = "Audio";

        public static readonly string[] Order =
        {
            Graphics, RenderPipeline, RenderBudget, Streaming, Simulation, Ftl, Civilizations, Ai, Dogfighting, Broadsides, Weapons, Economy, Controls, Audio,
        };
    }

    /// <summary>What sort of control the menu draws for a setting.</summary>
    public enum SettingKind
    {
        Toggle,
        Range,
        Choice,
    }

    /// <summary>
    /// One tunable a system declares (U.SPC-T431): its key (the field of the profile's settings it is kept in), the group
    /// and label the settings menu shows it under, its default, and what it may be set to. A system declares its
    /// settings as static fields of a class marked <see cref="SettingsSourceAttribute"/>; they appear in the menu
    /// by themselves, so a new mechanic adds a setting by declaring it, never by editing the menu.
    /// </summary>
    public abstract class GameSetting
    {
        public readonly string Key, Group, Label, Description;

        /// <summary>Extra words the menu's search finds this setting by.</summary>
        public readonly string Keywords;

        /// <summary>The registry this setting is in: the one that remembers its value in a profile.</summary>
        public SettingsRegistry Registry { get; private set; }

        protected GameSetting(string key, string group, string label, string description, string keywords)
        {
            if (!IsValidKey(key)) throw new ArgumentException("Not a setting key (letters, digits, '.', '-' and '_'): " + key, nameof(key));
            Key = key;
            Group = string.IsNullOrWhiteSpace(group) ? "General" : group;
            Label = string.IsNullOrWhiteSpace(label) ? key : label;
            Description = description ?? "";
            Keywords = keywords ?? "";
        }

        public abstract SettingKind Kind { get; }

        /// <summary>Whether the setting stands at its default (the menu's Reset does nothing then).</summary>
        public abstract bool IsDefault { get; }

        /// <summary>The value as the menu shows it ("144 fps", "On", "75%").</summary>
        public abstract string ValueText { get; }

        /// <summary>How the value is written in the profile.</summary>
        public abstract string Format();

        /// <summary>Takes back what <see cref="Format"/> wrote; false (and the value left alone) for anything else.</summary>
        internal abstract bool TryLoad(string text);

        internal abstract void SetToDefault();

        /// <summary>Puts the value back to its default, and forgets it in the profile.</summary>
        public void Reset()
        {
            if (Registry != null) Registry.Reset(this);
            else if (!IsDefault)
            {
                SetToDefault();
                RaiseChanged();
            }
        }

        /// <summary>Raised after the value changes (set, reset or loaded from a profile): where a system puts it into effect.</summary>
        public event Action<GameSetting> Changed;

        internal void RaiseChanged() => Changed?.Invoke(this);

        /// <summary>Called by the subclass when the player (or code) changed the value.</summary>
        protected void OnSet()
        {
            RaiseChanged();
            Registry?.Remember(this);
        }

        internal void Attach(SettingsRegistry registry) => Registry = registry;

        /// <summary>Whether every word of <paramref name="query"/> is in the label, group, description or keywords.</summary>
        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string hay = (Label + " " + Group + " " + Description + " " + Keywords + " " + Key).ToLowerInvariant();
            foreach (var word in query.ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if (hay.IndexOf(word, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        /// <summary>A plain key: letters, digits, '.', '-' and '_', not starting or ending with '.'.</summary>
        public static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 64 || key[0] == '.' || key[key.Length - 1] == '.') return false;
            foreach (char c in key)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '-' || c == '_')) return false;
            return true;
        }

        public override string ToString() => Group + " / " + Label + ": " + ValueText;
    }

    /// <summary>An on/off setting.</summary>
    public sealed class ToggleSetting : GameSetting
    {
        public readonly bool Default;
        public bool Value { get; private set; }

        public ToggleSetting(string key, string group, string label, bool defaultValue, string description = null, string keywords = null,
            SettingsRegistry registry = null)
            : base(key, group, label, description, keywords)
        {
            Default = Value = defaultValue;
            (registry ?? SettingsRegistry.Default).Register(this);
        }

        public override SettingKind Kind => SettingKind.Toggle;
        public override bool IsDefault => Value == Default;
        public override string ValueText => Value ? "On" : "Off";

        public void Set(bool value)
        {
            if (value == Value) return;
            Value = value;
            OnSet();
        }

        public override string Format() => Value ? "true" : "false";

        internal override bool TryLoad(string text)
        {
            if (!bool.TryParse(text, out bool v)) return false;
            Value = v;
            return true;
        }

        internal override void SetToDefault() => Value = Default;

        /// <summary>Puts the value into effect now and after every change (set, reset or a profile loaded); returns this.</summary>
        public ToggleSetting Applying(Action<bool> apply)
        {
            Changed += _ => apply(Value);
            apply(Value);
            return this;
        }

        public static implicit operator bool(ToggleSetting s) => s.Value;
    }

    /// <summary>A number between <see cref="Min"/> and <see cref="Max"/>, in steps of <see cref="Step"/> (0: any value).</summary>
    public sealed class RangeSetting : GameSetting
    {
        public readonly double Default, Min, Max, Step;

        /// <summary>How the menu writes a value ("1.25x", "4000 effects"); plain digits if none is given.</summary>
        readonly Func<double, string> display;

        public double Value { get; private set; }

        /// <summary>The value rounded to a whole number, for a count.</summary>
        public int IntValue => (int)Math.Round(Value);

        public RangeSetting(string key, string group, string label, double defaultValue, double min, double max, double step = 0,
            string description = null, Func<double, string> display = null, string keywords = null, SettingsRegistry registry = null)
            : base(key, group, label, description, keywords)
        {
            if (!(min <= max)) throw new ArgumentException("A range's minimum must not be above its maximum: " + key);
            if (step < 0) throw new ArgumentException("A range's step must not be negative: " + key);
            Min = min;
            Max = max;
            Step = step;
            this.display = display;
            Default = Value = Snap(defaultValue);
            (registry ?? SettingsRegistry.Default).Register(this);
        }

        public override SettingKind Kind => SettingKind.Range;
        public override bool IsDefault => Value == Default;
        public override string ValueText => display != null ? display(Value) : Value.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>Where <paramref name="v"/> lands: within the range, on a step counted from <see cref="Min"/>.</summary>
        public double Snap(double v)
        {
            if (double.IsNaN(v)) return Default;
            v = Math.Max(Min, Math.Min(Max, v));
            if (Step > 0)
            {
                v = Min + Math.Round((v - Min) / Step) * Step;
                v = Math.Max(Min, Math.Min(Max, Math.Round(v, 9)));
            }
            return v;
        }

        public void Set(double value)
        {
            value = Snap(value);
            if (value == Value) return;
            Value = value;
            OnSet();
        }

        /// <summary>Where the value sits in the range, 0 to 1 (a slider's position).</summary>
        public double Fraction => Max > Min ? (Value - Min) / (Max - Min) : 0;

        /// <summary>Puts the value into effect now and after every change (set, reset or a profile loaded); returns this.</summary>
        public RangeSetting Applying(Action<double> apply)
        {
            Changed += _ => apply(Value);
            apply(Value);
            return this;
        }

        public void SetFraction(double f) => Set(Min + Math.Max(0, Math.Min(1, f)) * (Max - Min));

        public override string Format() => Value.ToString("R", CultureInfo.InvariantCulture);

        internal override bool TryLoad(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || double.IsNaN(v) || double.IsInfinity(v))
                return false;
            Value = Snap(v);
            return true;
        }

        internal override void SetToDefault() => Value = Default;
    }

    /// <summary>One of a list of named options, remembered by name so options added or reordered later keep the choice.</summary>
    public sealed class ChoiceSetting : GameSetting
    {
        public readonly IReadOnlyList<string> Options;
        public readonly int Default;
        public int Index { get; private set; }

        public string Selected => Options[Index];

        public ChoiceSetting(string key, string group, string label, IReadOnlyList<string> options, int defaultIndex,
            string description = null, string keywords = null, SettingsRegistry registry = null)
            : base(key, group, label, description, keywords)
        {
            if (options == null || options.Count == 0) throw new ArgumentException("A choice needs at least one option: " + key);
            if (defaultIndex < 0 || defaultIndex >= options.Count) throw new ArgumentOutOfRangeException(nameof(defaultIndex));
            Options = options;
            Default = Index = defaultIndex;
            (registry ?? SettingsRegistry.Default).Register(this);
        }

        public override SettingKind Kind => SettingKind.Choice;
        public override bool IsDefault => Index == Default;
        public override string ValueText => Selected;

        public void Set(int index)
        {
            index = Math.Max(0, Math.Min(Options.Count - 1, index));
            if (index == Index) return;
            Index = index;
            OnSet();
        }

        /// <summary>The next option, or the previous for a negative <paramref name="step"/>, wrapping round.</summary>
        public void Cycle(int step) => Set(((Index + step) % Options.Count + Options.Count) % Options.Count);

        /// <summary>Puts the choice into effect now and after every change (set, reset or a profile loaded); returns this.</summary>
        public ChoiceSetting Applying(Action<int> apply)
        {
            Changed += _ => apply(Index);
            apply(Index);
            return this;
        }

        public override string Format() => Selected;

        internal override bool TryLoad(string text)
        {
            for (int i = 0; i < Options.Count; i++)
                if (string.Equals(Options[i], text, StringComparison.Ordinal))
                {
                    Index = i;
                    return true;
                }
            return false;
        }

        internal override void SetToDefault() => Index = Default;
    }
}
