using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>How the game's window sits on the screen (U.SPC-T276).</summary>
    public enum DisplayMode
    {
        /// <summary>An ordinary window with a frame, of the chosen size.</summary>
        Windowed,

        /// <summary>A borderless window covering the whole screen, drawn at the chosen size and scaled to fit.</summary>
        Borderless,

        /// <summary>The screen handed over to the game outright, switched to the chosen size and refresh rate.</summary>
        Exclusive,
    }

    /// <summary>One size the screen can be set to, at the highest refresh rate it offers that size.</summary>
    public readonly struct DisplayResolution : IEquatable<DisplayResolution>
    {
        public readonly int Width, Height;
        public readonly double RefreshHz;

        public DisplayResolution(int width, int height, double refreshHz)
        {
            Width = width;
            Height = height;
            RefreshHz = refreshHz;
        }

        public bool SameSize(int width, int height) => Width == width && Height == height;

        public bool Equals(DisplayResolution other) => Width == other.Width && Height == other.Height && RefreshHz.Equals(other.RefreshHz);
        public override bool Equals(object obj) => obj is DisplayResolution other && Equals(other);
        public override int GetHashCode() => (Width * 397) ^ Height ^ RefreshHz.GetHashCode();

        public override string ToString() =>
            Width.ToString(CultureInfo.InvariantCulture) + " x " + Height.ToString(CultureInfo.InvariantCulture) +
            (RefreshHz > 0 ? "  @ " + Math.Round(RefreshHz).ToString(CultureInfo.InvariantCulture) + " Hz" : "");
    }

    /// <summary>
    /// The player's display settings (U.SPC-T276): the size the game is drawn at, how its window sits on the
    /// screen, and whether frames wait for the screen's refresh. Kept free of the engine, so the pause menu's
    /// choices and what is remembered between runs can be checked without it.
    /// </summary>
    public readonly struct DisplaySettings : IEquatable<DisplaySettings>
    {
        public readonly int Width, Height;
        public readonly DisplayMode Mode;
        public readonly bool VSync;

        public DisplaySettings(int width, int height, DisplayMode mode, bool vSync)
        {
            Width = width;
            Height = height;
            Mode = mode;
            VSync = vSync;
        }

        public DisplaySettings WithSize(int width, int height) => new DisplaySettings(width, height, Mode, VSync);
        public DisplaySettings WithMode(DisplayMode mode) => new DisplaySettings(Width, Height, mode, VSync);
        public DisplaySettings WithVSync(bool vSync) => new DisplaySettings(Width, Height, Mode, vSync);

        /// <summary>What the menu calls each mode.</summary>
        public static string ModeName(DisplayMode mode) =>
            mode == DisplayMode.Borderless ? "Borderless windowed" : mode == DisplayMode.Exclusive ? "Exclusive fullscreen" : "Windowed";

        /// <summary>How the settings are remembered between runs: "1920x1080;Borderless;vsync" (or "novsync").</summary>
        public string Format() =>
            Width.ToString(CultureInfo.InvariantCulture) + "x" + Height.ToString(CultureInfo.InvariantCulture) + ";" + Mode + ";" + (VSync ? "vsync" : "novsync");

        /// <summary>Reads back what <see cref="Format"/> wrote; false (and nothing applied) for anything else, such as an
        /// empty or damaged preference, or a size too small to play in.</summary>
        public static bool TryParse(string text, out DisplaySettings settings)
        {
            settings = default;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split(';');
            if (parts.Length != 3) return false;
            var size = parts[0].Split('x');
            if (size.Length != 2
                || !int.TryParse(size[0], NumberStyles.None, CultureInfo.InvariantCulture, out int w)
                || !int.TryParse(size[1], NumberStyles.None, CultureInfo.InvariantCulture, out int h)
                || w < MinWidth || h < MinHeight)
                return false;
            if (!Enum.TryParse(parts[1], false, out DisplayMode mode) || !Enum.IsDefined(typeof(DisplayMode), mode)) return false;
            bool vSync;
            if (parts[2] == "vsync") vSync = true;
            else if (parts[2] == "novsync") vSync = false;
            else return false;
            settings = new DisplaySettings(w, h, mode, vSync);
            return true;
        }

        /// <summary>The smallest size worth restoring: anything below it is a damaged preference, not a choice.</summary>
        public const int MinWidth = 640, MinHeight = 360;

        /// <summary>
        /// The sizes the menu offers: each width x height once, at the highest refresh rate the screen gives it,
        /// smallest first (by width, then height). Sizes too small to play in are left out.
        /// </summary>
        public static List<DisplayResolution> Distinct(IEnumerable<DisplayResolution> modes)
        {
            var best = new Dictionary<(int, int), DisplayResolution>();
            foreach (var m in modes)
            {
                if (m.Width < MinWidth || m.Height < MinHeight) continue;
                var key = (m.Width, m.Height);
                if (!best.TryGetValue(key, out var have) || m.RefreshHz > have.RefreshHz) best[key] = m;
            }
            var list = new List<DisplayResolution>(best.Values);
            list.Sort((a, b) => a.Width != b.Width ? a.Width.CompareTo(b.Width) : a.Height.CompareTo(b.Height));
            return list;
        }

        /// <summary>The aspect ratios the Display page offers, narrowest first (U.SPC-T284): ultrawide 21:9 and super-ultrawide
        /// 32:9 among them.</summary>
        public static readonly string[] Aspects = { "4:3", "5:4", "16:10", "16:9", "21:9", "32:9" };

        static readonly double[] AspectValues = { 4.0 / 3, 5.0 / 4, 16.0 / 10, 16.0 / 9, 21.0 / 9, 32.0 / 9 };

        /// <summary>
        /// The aspect ratio a size is sold as: the nearest of <see cref="Aspects"/> within 6%, so 2560 x 1080 and
        /// 3440 x 1440 (really 64:27 and 43:18) are 21:9 and 5120 x 1440 is 32:9; "Other" for anything further off.
        /// </summary>
        public static string AspectOf(int width, int height)
        {
            if (width <= 0 || height <= 0) return "Other";
            double r = (double)width / height;
            int best = -1;
            double bestGap = 0.06;
            for (int i = 0; i < AspectValues.Length; i++)
            {
                double gap = Math.Abs(r / AspectValues[i] - 1);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = i;
                }
            }
            return best >= 0 ? Aspects[best] : "Other";
        }

        /// <summary>The usual sizes of each aspect, so a widescreen or ultrawide one can be picked even where the screen
        /// does not list it (a window or a borderless window can be any size that fits the screen).</summary>
        static readonly (int w, int h)[] StandardSizes =
        {
            (1024, 768), (1280, 960), (1600, 1200),
            (1280, 1024),
            (1280, 800), (1440, 900), (1680, 1050), (1920, 1200), (2560, 1600),
            (1280, 720), (1600, 900), (1920, 1080), (2560, 1440), (3200, 1800), (3840, 2160), (5120, 2880),
            (2560, 1080), (3440, 1440), (3840, 1600), (5120, 2160),
            (3840, 1080), (5120, 1440), (7680, 2160),
        };

        /// <summary>
        /// <paramref name="listed"/> (what the screen offers) with the usual sizes of every aspect added that fit within
        /// <paramref name="maxWidth"/> x <paramref name="maxHeight"/> (the screen's own size); each size once, smallest
        /// first. An added size has no refresh rate of its own.
        /// </summary>
        public static List<DisplayResolution> WithStandardSizes(IReadOnlyList<DisplayResolution> listed, int maxWidth, int maxHeight)
        {
            var all = new List<DisplayResolution>(listed);
            foreach (var (w, h) in StandardSizes)
                if (w <= maxWidth && h <= maxHeight) all.Add(new DisplayResolution(w, h, 0));
            return Distinct(all);
        }

        /// <summary>The sizes of <paramref name="list"/> of aspect <paramref name="aspect"/> (null or "All": every one).</summary>
        public static List<DisplayResolution> OfAspect(IReadOnlyList<DisplayResolution> list, string aspect)
        {
            var result = new List<DisplayResolution>();
            foreach (var r in list)
                if (string.IsNullOrEmpty(aspect) || aspect == "All" || AspectOf(r.Width, r.Height) == aspect) result.Add(r);
            return result;
        }

        /// <summary>Which of <paramref name="list"/> is <paramref name="width"/> x <paramref name="height"/>, or failing
        /// that the one closest to it in pixel count (-1 for an empty list): what the menu starts with picked.</summary>
        public static int IndexOfNearest(IReadOnlyList<DisplayResolution> list, int width, int height)
        {
            int best = -1;
            long bestGap = long.MaxValue;
            long want = (long)width * height;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].SameSize(width, height)) return i;
                long gap = Math.Abs((long)list[i].Width * list[i].Height - want);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = i;
                }
            }
            return best;
        }

        public bool Equals(DisplaySettings other) => Width == other.Width && Height == other.Height && Mode == other.Mode && VSync == other.VSync;
        public override bool Equals(object obj) => obj is DisplaySettings other && Equals(other);
        public override int GetHashCode() => (((Width * 397) ^ Height) * 397 ^ (int)Mode) * 2 + (VSync ? 1 : 0);
        public static bool operator ==(DisplaySettings a, DisplaySettings b) => a.Equals(b);
        public static bool operator !=(DisplaySettings a, DisplaySettings b) => !a.Equals(b);

        public override string ToString() => Width + " x " + Height + ", " + ModeName(Mode) + ", VSync " + (VSync ? "on" : "off");
    }

    /// <summary>
    /// The "Keep these display settings?" countdown after Apply (U.SPC-T276): the new settings stand only if the
    /// player says so within <see cref="Seconds"/> of real time; otherwise (or on Revert) the previous ones come back.
    /// </summary>
    public sealed class DisplayConfirmation
    {
        /// <summary>How long the player has to keep new settings before they are taken back.</summary>
        public const double Seconds = 10;

        /// <summary>What was on the screen before Apply: what a revert goes back to.</summary>
        public DisplaySettings Previous { get; }

        /// <summary>What Apply put on the screen: what Keep remembers.</summary>
        public DisplaySettings Applied { get; }

        /// <summary>Real seconds left before the settings revert by themselves.</summary>
        public double Remaining { get; private set; }

        public DisplayConfirmation(DisplaySettings previous, DisplaySettings applied, double seconds = Seconds)
        {
            Previous = previous;
            Applied = applied;
            Remaining = seconds;
        }

        /// <summary>The whole seconds shown in "Reverting in N": 10 the moment it starts, 1 in its last second.</summary>
        public int SecondsShown => Math.Max(0, (int)Math.Ceiling(Remaining));

        /// <summary>Counts down by <paramref name="realSeconds"/> (unscaled: a paused or slowed game still counts);
        /// true once the time is up and the previous settings must come back.</summary>
        public bool Tick(double realSeconds)
        {
            if (realSeconds > 0) Remaining = Math.Max(0, Remaining - realSeconds);
            return Remaining <= 0;
        }
    }
}
