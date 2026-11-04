using System;
using System.Globalization;

namespace Unity.Shared.Core
{
    /// <summary>
    /// An opaque 24-bit colour, engine-free so Core code and dotnet tests can use it. Stored in settings as
    /// <c>"#RRGGBB"</c>. The Unity assembly converts it to and from <c>UnityEngine.Color</c>
    /// (<c>ToUnityColor()</c> / <c>ToRgbColor()</c>).
    /// </summary>
    public readonly struct RgbColor : IEquatable<RgbColor>
    {
        /// <summary>Red, 0-255.</summary>
        public readonly byte R;

        /// <summary>Green, 0-255.</summary>
        public readonly byte G;

        /// <summary>Blue, 0-255.</summary>
        public readonly byte B;

        /// <summary>A colour from its three channels.</summary>
        public RgbColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        /// <summary>A colour from 0-1 channels (as Unity's Color uses), clamped and rounded.</summary>
        public static RgbColor FromFloats(float r, float g, float b) => new RgbColor(ToByte(r), ToByte(g), ToByte(b));

        static byte ToByte(float v) => (byte)Math.Round(Math.Max(0f, Math.Min(1f, float.IsNaN(v) ? 0f : v)) * 255f);

        /// <summary>Red as 0-1.</summary>
        public float RedFloat => R / 255f;

        /// <summary>Green as 0-1.</summary>
        public float GreenFloat => G / 255f;

        /// <summary>Blue as 0-1.</summary>
        public float BlueFloat => B / 255f;

        /// <summary>The colour as <c>#RRGGBB</c> (upper-case hex).</summary>
        public string ToHex() => "#" + R.ToString("X2", CultureInfo.InvariantCulture) + G.ToString("X2", CultureInfo.InvariantCulture) + B.ToString("X2", CultureInfo.InvariantCulture);

        /// <summary>Reads <c>#RRGGBB</c> or <c>RRGGBB</c> (any case); false for anything else.</summary>
        public static bool TryParse(string text, out RgbColor color)
        {
            color = default;
            if (text == null) return false;
            text = text.Trim();
            if (text.StartsWith("#", StringComparison.Ordinal)) text = text.Substring(1);
            if (text.Length != 6) return false;
            if (!int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int v)) return false;
            color = new RgbColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }

        /// <summary>Reads <c>#RRGGBB</c>; throws <see cref="FormatException"/> for anything else.</summary>
        public static RgbColor Parse(string text) =>
            TryParse(text, out var c) ? c : throw new FormatException("Not a #RRGGBB colour: " + text);

        /// <summary>
        /// Eight player colours that stay distinct from one another and readable on dark and light backgrounds;
        /// a profile without a chosen colour gets one of these, picked by <see cref="ForName"/>.
        /// </summary>
        public static readonly RgbColor[] Palette =
        {
            new RgbColor(0xE6, 0x4B, 0x3C), // red
            new RgbColor(0x34, 0x98, 0xDB), // blue
            new RgbColor(0x2E, 0xCC, 0x71), // green
            new RgbColor(0xF1, 0xC4, 0x0F), // yellow
            new RgbColor(0x9B, 0x59, 0xB6), // purple
            new RgbColor(0xE6, 0x7E, 0x22), // orange
            new RgbColor(0x1A, 0xBC, 0x9C), // teal
            new RgbColor(0xE8, 0x43, 0x93), // pink
        };

        /// <summary>
        /// The same <see cref="Palette"/> colour for the same name on every run and platform (FNV-1a of the
        /// lower-cased name), so a player who never picks a colour still keeps one.
        /// </summary>
        public static RgbColor ForName(string name)
        {
            uint hash = 2166136261;
            foreach (char c in (name ?? "").ToLowerInvariant())
            {
                hash ^= c;
                hash *= 16777619;
            }
            return Palette[hash % (uint)Palette.Length];
        }

        /// <inheritdoc />
        public bool Equals(RgbColor other) => R == other.R && G == other.G && B == other.B;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is RgbColor other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => (R << 16) | (G << 8) | B;

        /// <summary>Same colour.</summary>
        public static bool operator ==(RgbColor a, RgbColor b) => a.Equals(b);

        /// <summary>Different colour.</summary>
        public static bool operator !=(RgbColor a, RgbColor b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() => ToHex();
    }
}
