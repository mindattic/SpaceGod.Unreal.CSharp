using System;
using Unity.Shared.Core.Json;

namespace Unity.Shared.Core
{
    /// <summary>
    /// The <c>"common"</c> block of settings.json: what every MindAttic game reads the same way. Each property
    /// reads straight from, and writes straight into, the underlying <see cref="Data"/> object, so fields this
    /// version does not know survive a load and save. A missing or wrong-typed field reads as its default.
    /// Change values, then call <see cref="Profile.SaveSettings"/> to write them.
    /// </summary>
    public sealed class CommonSettings
    {
        /// <summary>JSON field names, as written in settings.json.</summary>
        public static class Keys
        {
#pragma warning disable CS1591
            public const string MusicVolume = "musicVolume";
            public const string SfxVolume = "sfxVolume";
            public const string MuteMusic = "muteMusic";
            public const string MuteSfx = "muteSfx";
            public const string ReduceMotion = "reduceMotion";
            public const string Language = "language";
            public const string DisplayName = "displayName";
            public const string Color = "color";
#pragma warning restore CS1591
        }

        /// <summary>Music volume before the player changes it (GridGame2026's default).</summary>
        public const float DefaultMusicVolume = 0.6f;

        /// <summary>Sound-effect volume before the player changes it (GridGame2026's default).</summary>
        public const float DefaultSfxVolume = 0.85f;

        /// <summary>Language before the player changes it: an IETF tag.</summary>
        public const string DefaultLanguage = "en";

        /// <summary>The longest display name kept; a longer one is cut to this.</summary>
        public const int MaxDisplayNameLength = 32;

        readonly Func<string> profileName;

        internal CommonSettings(JsonObject data, Func<string> profileName)
        {
            Data = data;
            this.profileName = profileName;
        }

        /// <summary>The raw <c>"common"</c> object, unknown fields and all.</summary>
        public JsonObject Data { get; }

        /// <summary>
        /// Music volume, 0-1 (stored values outside that are clamped on read and write). Reads 0 while
        /// <see cref="Profiles.SessionMuted"/> is on; the stored value is kept and still saved.
        /// </summary>
        public float MusicVolume
        {
            get => Profiles.SessionMuted ? 0f : Clamp01(Data.GetFloat(Keys.MusicVolume, DefaultMusicVolume));
            set => Data.Set(Keys.MusicVolume, Clamp01(value));
        }

        /// <summary>Sound-effect volume, 0-1. Reads 0 while <see cref="Profiles.SessionMuted"/> is on.</summary>
        public float SfxVolume
        {
            get => Profiles.SessionMuted ? 0f : Clamp01(Data.GetFloat(Keys.SfxVolume, DefaultSfxVolume));
            set => Data.Set(Keys.SfxVolume, Clamp01(value));
        }

        /// <summary>Music muted. Reads true while <see cref="Profiles.SessionMuted"/> is on.</summary>
        public bool MuteMusic
        {
            get => Profiles.SessionMuted || Data.GetBool(Keys.MuteMusic, false);
            set => Data.Set(Keys.MuteMusic, value);
        }

        /// <summary>Sound effects muted. Reads true while <see cref="Profiles.SessionMuted"/> is on.</summary>
        public bool MuteSfx
        {
            get => Profiles.SessionMuted || Data.GetBool(Keys.MuteSfx, false);
            set => Data.Set(Keys.MuteSfx, value);
        }

        /// <summary>The player asked for less motion (no screen shake, softer transitions).</summary>
        public bool ReduceMotion
        {
            get => Data.GetBool(Keys.ReduceMotion, false);
            set => Data.Set(Keys.ReduceMotion, value);
        }

        /// <summary>UI language as an IETF tag (<c>"en"</c>, <c>"fr-CA"</c>); null or blank resets it to <see cref="DefaultLanguage"/>.</summary>
        public string Language
        {
            get
            {
                var s = Data.GetString(Keys.Language);
                return string.IsNullOrWhiteSpace(s) ? DefaultLanguage : s.Trim();
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) Data.Remove(Keys.Language);
                else Data.Set(Keys.Language, value.Trim());
            }
        }

        /// <summary>
        /// The name other players see (multiplayer lobbies, scoreboards). Until the player sets one it is the
        /// profile's name, and it follows a profile rename. Setting null or blank clears it back to that;
        /// anything else is trimmed and cut to <see cref="MaxDisplayNameLength"/> characters.
        /// </summary>
        public string DisplayName
        {
            get
            {
                var s = Data.GetString(Keys.DisplayName);
                return string.IsNullOrWhiteSpace(s) ? FallbackName : s.Trim();
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) { Data.Remove(Keys.DisplayName); return; }
                value = value.Trim();
                if (value.Length > MaxDisplayNameLength)
                {
                    // Don't cut between the halves of a surrogate pair (an emoji): a lone half is written as U+FFFD.
                    int cut = char.IsHighSurrogate(value[MaxDisplayNameLength - 1]) ? MaxDisplayNameLength - 1 : MaxDisplayNameLength;
                    value = value.Substring(0, cut).TrimEnd();
                }
                Data.Set(Keys.DisplayName, value);
            }
        }

        /// <summary>Whether the player chose a <see cref="DisplayName"/> (false when it is the profile name by default).</summary>
        public bool HasDisplayName => !string.IsNullOrWhiteSpace(Data.GetString(Keys.DisplayName));

        /// <summary>
        /// The player's colour (cursor, avatar tint, lobby badge). Until the player picks one it is a
        /// <see cref="RgbColor.Palette"/> colour chosen from the profile name, the same on every run.
        /// Stored as <c>"#RRGGBB"</c>. In Unity: <c>Common.GetUnityColor()</c> / <c>Common.SetUnityColor(c)</c>.
        /// </summary>
        public RgbColor Color
        {
            get => RgbColor.TryParse(Data.GetString(Keys.Color), out var c) ? c : RgbColor.ForName(FallbackName);
            set => Data.Set(Keys.Color, value.ToHex());
        }

        /// <summary>Whether the player chose a <see cref="Color"/>.</summary>
        public bool HasColor => RgbColor.TryParse(Data.GetString(Keys.Color), out _);

        /// <summary>Forgets the chosen colour, back to the one picked from the profile name.</summary>
        public void ClearColor() => Data.Remove(Keys.Color);

        string FallbackName
        {
            get
            {
                var n = profileName?.Invoke();
                return string.IsNullOrWhiteSpace(n) ? "Player" : n;
            }
        }

        /// <summary>Writes every standard field that is missing, with its default (displayName and color stay optional).</summary>
        internal void FillDefaults()
        {
            if (!(Data[Keys.MusicVolume] is JsonNumber)) MusicVolume = DefaultMusicVolume;
            if (!(Data[Keys.SfxVolume] is JsonNumber)) SfxVolume = DefaultSfxVolume;
            if (!(Data[Keys.MuteMusic] is bool)) MuteMusic = false;
            if (!(Data[Keys.MuteSfx] is bool)) MuteSfx = false;
            if (!(Data[Keys.ReduceMotion] is bool)) ReduceMotion = false;
            if (!(Data[Keys.Language] is string)) Language = DefaultLanguage;
        }

        static float Clamp01(float v) => float.IsNaN(v) ? 0f : Math.Max(0f, Math.Min(1f, v));
    }
}
