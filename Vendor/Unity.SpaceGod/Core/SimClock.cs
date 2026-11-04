using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>Simulation time as a Julian date, advanced by real time multiplied by a warp level.</summary>
    [SettingsSource]
    public sealed class SimClock
    {
        public readonly struct WarpLevel
        {
            public readonly double SimSecondsPerSecond;
            public readonly string Label;

            public WarpLevel(double rate, string label)
            {
                SimSecondsPerSecond = rate;
                Label = label;
            }
        }

        public static readonly WarpLevel[] Levels =
        {
            new WarpLevel(1, "real time"),
            new WarpLevel(10, "10 s/s"),
            new WarpLevel(60, "1 min/s"),
            new WarpLevel(600, "10 min/s"),
            new WarpLevel(3600, "1 hour/s"),
            new WarpLevel(6 * 3600, "6 hours/s"),
            new WarpLevel(86400, "1 day/s"),
            new WarpLevel(7 * 86400, "1 week/s"),
            new WarpLevel(30 * 86400, "1 month/s"),
            new WarpLevel(365.25 * 86400, "1 year/s"),
        };

        public const int DefaultWarpIndex = 4;

        /// <summary>The warp a new game starts at (a save keeps its own), from the settings menu's Simulation group (U.SPC-T431).</summary>
        public static readonly ChoiceSetting StartingWarp = new ChoiceSetting("simulation.startingWarp", SettingGroups.Simulation,
            "Starting time warp", Array.ConvertAll(Levels, l => l.Label), DefaultWarpIndex,
            "How fast the clock runs when a new game starts; the time controls still change it in play.", "speed clock rate time");

        public double Jd { get; private set; }
        public int WarpIndex { get; private set; } = DefaultWarpIndex;

        /// <summary>Stopped outright (scripts set this directly; the player's pause eases into it, see <see cref="ToggleEasedPause"/>).</summary>
        public bool Paused
        {
            get => paused;
            set
            {
                paused = value;
                pausing = resuming = false;
                Speed = value ? 0 : 1;
            }
        }

        /// <summary>How much of the warp rate is running (1 normally, easing to 0 when pausing and back when resuming).</summary>
        public double Speed { get; private set; } = 1;

        public bool Pausing => pausing;
        public bool Resuming => resuming;

        /// <summary>Each easing step multiplies (pausing) or divides (resuming) the speed by this.</summary>
        public const double EaseFactor = 0.97;

        /// <summary>Easing steps per real second, so the ease takes the same time at any frame rate (about 1.3 s each way).</summary>
        public const double EaseStepsPerSecond = 120;

        /// <summary>Below this share of full speed a pause snaps to a dead stop; a resume starts from it.</summary>
        public const double EaseFloor = 0.01;

        bool paused, pausing, resuming;

        /// <summary>
        /// The player's pause: time slows by 0.97 a step until it is under 1% of full speed, then stops;
        /// pressed again (or while still slowing), it picks up from 1% (or wherever it had got to) and
        /// speeds back up the same way.
        /// </summary>
        public void ToggleEasedPause()
        {
            if (paused || pausing)
            {
                paused = false;
                pausing = false;
                resuming = true;
                Speed = Math.Max(Speed, EaseFloor);
            }
            else
            {
                resuming = false;
                pausing = true;
            }
        }

        /// <summary>Paused, or on the way to it: what a pause button should show.</summary>
        public bool PauseHeld => paused || pausing;

        public WarpLevel Warp => Levels[WarpIndex];
        public DateTime Utc => Astro.ToDateTime(Jd);

        public SimClock(double startJd) => Jd = startJd;

        public static SimClock StartingNow() => new SimClock(Astro.JulianDate(DateTime.UtcNow));

        /// <summary>
        /// The slowest warp level that gets <paramref name="simSeconds"/> of simulated time over within
        /// <paramref name="realSeconds"/> of real time (the fastest level if none does), never slower than
        /// <paramref name="atLeast"/>: how a piloted hyperlane ride of hours plays out in under a minute (U.SPC-T41).
        /// </summary>
        public static int WarpToCover(double simSeconds, double realSeconds, int atLeast = 0)
        {
            int i = Math.Max(0, Math.Min(Levels.Length - 1, atLeast));
            while (i < Levels.Length - 1 && simSeconds / Levels[i].SimSecondsPerSecond > realSeconds) i++;
            return i;
        }

        public void SetWarp(int index) => WarpIndex = Math.Max(0, Math.Min(Levels.Length - 1, index));
        public void Faster() => SetWarp(WarpIndex + 1);
        public void Slower() => SetWarp(WarpIndex - 1);
        public void SetJd(double jd) => Jd = jd;

        /// <summary>Advances by real seconds; returns the simulated days elapsed.</summary>
        public double Advance(double realSeconds)
        {
            if (paused || realSeconds <= 0) return 0;
            if (pausing || resuming)
            {
                double factor = Math.Pow(EaseFactor, realSeconds * EaseStepsPerSecond);
                if (pausing)
                {
                    Speed *= factor;
                    if (Speed < EaseFloor)
                    {
                        Speed = 0;
                        pausing = false;
                        paused = true;
                        return 0;
                    }
                }
                else
                {
                    Speed /= factor;
                    if (Speed >= 1)
                    {
                        Speed = 1;
                        resuming = false;
                    }
                }
            }
            double days = realSeconds * Warp.SimSecondsPerSecond * Speed / Astro.SecondsPerDay;
            Jd += days;
            return days;
        }
    }
}
