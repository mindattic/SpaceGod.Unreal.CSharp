using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Seamless streaming (U.SPC-T414): nothing waits behind a loading screen. What is drawn in full (a ship's model, a
    /// planet's texture, the systems next door) is loaded in the background, a little each frame within a time budget,
    /// in the order it will be needed: what is big on screen now first, then what the view is flying toward, judged
    /// by where the view will be a few seconds ahead along its own motion (the lead), with what lies ahead of the way it
    /// looks counted bigger than what lies behind. Something drops out again once it is well below the size it is
    /// wanted at (a gap between the two, so nothing flickers in and out at the edge).
    ///
    /// The lead is the promise that plain flight never waits: anything the view could reach at its present speed
    /// within the lead is asked for before it is reached, and a frame's budget clears far more than a lead's worth of
    /// newcomers. The one place loading may still be behind is the far side of a jump gate, where everything is new at
    /// once; the gate transit covers it (<see cref="GateCover"/>), holding its tunnel open until the far side is in.
    /// </summary>
    [SettingsSource]
    public static class Streaming
    {
        /// <summary>The share of a frame's time loading may spend.</summary>
        public static readonly RangeSetting FrameBudgetMs = new RangeSetting("stream.frameBudgetMs", SettingGroups.Streaming,
            "Loading budget per frame", 3, 0.5, 12, 0.5,
            "How long each frame may spend building models and textures that have come into view. Lower keeps the frame rate steadier; higher fills a crowded view in sooner.",
            v => v.ToString("0.#") + " ms", "stream load budget frame hitch stutter async");

        /// <summary>How far ahead (seconds of the view's own motion) things are asked for before they are needed.</summary>
        public static readonly RangeSetting LeadSeconds = new RangeSetting("stream.leadSeconds", SettingGroups.Streaming,
            "Look-ahead", 5, 1, 20, 0.5,
            "Models and textures are loaded this many seconds before the view, at its present speed, would reach the size they are drawn at. Longer is safer at speed and uses more memory.",
            v => v.ToString("0.#") + " s", "stream prefetch lead ahead preload");

        /// <summary>The most a jump gate's tunnel is held open past its own length while the far side finishes loading.</summary>
        public static readonly RangeSetting GateCoverMaxSeconds = new RangeSetting("stream.gateCoverMaxS", SettingGroups.Streaming,
            "Gate tunnel hold", 8, 0, 20, 0.5,
            "The longest a jump gate's tunnel is held open, past its usual length, while what lies on the far side finishes loading.",
            v => v.ToString("0.#") + " s", "gate jump tunnel transit loading cover");

        /// <summary>Asked for once it would be this share of the size it is drawn at (well before it shows).</summary>
        public const double LoadShare = 0.35;

        /// <summary>Let go of once it has fallen below this share (well under <see cref="LoadShare"/>, so no flicker).</summary>
        public const double UnloadShare = 0.12;

        /// <summary>Something dead ahead of the view counts this many times its size; straight behind, once.</summary>
        public const double AheadBoost = 2.5;

        /// <summary>Needed at least this soon (the view would reach it within <see cref="WarmSeconds"/>), a gate's far
        /// system is warmed: its textures and models start loading in the background.</summary>
        public const double WarmSeconds = 30;

        /// <summary>
        /// How much more an item counts for lying in the way the view looks: 1 beside or behind it, rising smoothly to
        /// <see cref="AheadBoost"/> dead ahead.
        /// </summary>
        public static double HeadingWeight(DVec3 heading, DVec3 toItem)
        {
            double h = heading.Length, d = toItem.Length;
            if (h < 1e-12 || d < 1e-12) return AheadBoost;
            double cos = DVec3.Dot(heading, toItem) / (h * d);
            if (cos <= 0) return 1;
            return 1 + (AheadBoost - 1) * cos * cos;
        }

        /// <summary>
        /// The nearest the view comes to <paramref name="item"/> within the lead: the closest point on its path from
        /// where it is to where it will be <paramref name="leadSeconds"/> on at <paramref name="velocity"/>.
        /// </summary>
        public static double NearestWithinLead(DVec3 viewer, DVec3 velocity, double leadSeconds, DVec3 item)
        {
            var path = velocity * Math.Max(leadSeconds, 0);
            double len2 = path.LengthSquared;
            if (len2 < 1e-24) return (item - viewer).Length;
            double s = DVec3.Dot(item - viewer, path) / len2;
            s = s < 0 ? 0 : s > 1 ? 1 : s;
            return (item - (viewer + path * s)).Length;
        }

        /// <summary>
        /// How badly <paramref name="size"/> scene units of something at <paramref name="item"/> is wanted, as a share of the
        /// size on screen it is drawn at: its largest size on screen within the lead, weighted by the way the view
        /// looks. 1 or more: it is (or is about to be) drawn in full.
        /// </summary>
        public static double Urgency(in StreamView view, DVec3 item, double size)
        {
            if (view.ModelPx <= 0) return double.PositiveInfinity;
            double nearest = NearestWithinLead(view.Position, view.Velocity, view.LeadSeconds, item);
            double px = size / Math.Max(nearest, 1e-9) * view.PixelsPerUnit;
            return px / view.ModelPx * HeadingWeight(view.Heading, item - view.Position);
        }

        /// <summary>Whether something this <paramref name="urgency"/> should be held in memory: asked for at
        /// <see cref="LoadShare"/>, let go of only below <see cref="UnloadShare"/>.</summary>
        public static bool WantsLoaded(double urgency, bool loaded) => urgency >= (loaded ? UnloadShare : LoadShare);

        /// <summary>
        /// What to load and what to let go of this frame. <paramref name="load"/> gets the items wanted and not loaded, the
        /// most urgent first (everything always wanted, such as the ship at the helm, ahead of all); <paramref name="unload"/>
        /// gets the loaded items no longer wanted. Both are cleared first.
        /// </summary>
        public static void Plan(IReadOnlyList<StreamItem> items, in StreamView view, List<int> load, List<int> unload)
        {
            load.Clear();
            unload.Clear();
            scored.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                double u = it.Always ? double.PositiveInfinity : Urgency(view, it.Position, it.Size);
                if (it.Loaded)
                {
                    if (!WantsLoaded(u, true)) unload.Add(it.Id);
                }
                else if (WantsLoaded(u, false)) scored.Add((u, it.Id));
            }
            scored.Sort((a, b) => b.urgency.CompareTo(a.urgency));
            foreach (var s in scored) load.Add(s.id);
            scored.Clear();
        }

        [ThreadStatic] static List<(double urgency, int id)> scoredList;
        static List<(double urgency, int id)> scored => scoredList ??= new List<(double, int)>();

        /// <summary>
        /// Works down <paramref name="ordered"/> calling <paramref name="work"/> on each until <paramref name="budgetMs"/>
        /// (by <paramref name="nowMs"/>) is spent: at least one each frame, so a heavy item still gets through. Work that
        /// reports false (not ready yet, e.g. its asset still on its way) costs a look but is skipped. Returns how many
        /// were done.
        /// </summary>
        public static int Spend(IReadOnlyList<int> ordered, Func<int, bool> work, double budgetMs, Func<double> nowMs)
        {
            if (ordered == null || work == null || ordered.Count == 0) return 0;
            double start = nowMs();
            int done = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (done > 0 && nowMs() - start >= budgetMs) break;
                if (work(ordered[i])) done++;
            }
            return done;
        }

        /// <summary>
        /// Whether the system beyond a gate at <paramref name="gate"/> should be warmed (its assets started loading): the
        /// view is within <paramref name="nearDistance"/> of it, or closing on it fast enough to be there within
        /// <see cref="WarmSeconds"/>.
        /// </summary>
        public static bool ShouldWarm(DVec3 viewer, DVec3 velocity, DVec3 gate, double nearDistance)
        {
            var to = gate - viewer;
            double d = to.Length;
            if (d <= nearDistance) return true;
            double closing = DVec3.Dot(velocity, to) / Math.Max(d, 1e-12);
            return closing > 0 && (d - nearDistance) / closing <= WarmSeconds;
        }
    }

    /// <summary>Where the view is and how it moves, for <see cref="Streaming.Plan"/>: scene units and real seconds.</summary>
    public readonly struct StreamView
    {
        public readonly DVec3 Position, Velocity, Heading;

        /// <summary>The camera's pixels per scene unit at unit distance (a size over a distance, times this, is pixels).</summary>
        public readonly double PixelsPerUnit;

        /// <summary>The size on screen, pixels, something is drawn in full at (below it, an icon).</summary>
        public readonly double ModelPx;

        public readonly double LeadSeconds;

        public StreamView(DVec3 position, DVec3 velocity, DVec3 heading, double pixelsPerUnit, double modelPx, double leadSeconds)
        {
            Position = position;
            Velocity = velocity;
            Heading = heading;
            PixelsPerUnit = pixelsPerUnit;
            ModelPx = modelPx;
            LeadSeconds = leadSeconds;
        }
    }

    /// <summary>One thing that may be streamed: where it is, how big (scene units), whether it is in memory, and whether
    /// it must always be (the ship at the helm, the one selected).</summary>
    public readonly struct StreamItem
    {
        public readonly int Id;
        public readonly DVec3 Position;
        public readonly double Size;
        public readonly bool Loaded, Always;

        public StreamItem(int id, DVec3 position, double size, bool loaded, bool always = false)
        {
            Id = id;
            Position = position;
            Size = size;
            Loaded = loaded;
            Always = always;
        }
    }

    /// <summary>
    /// The cover a jump gate transit gives the loading of what lies on the far side (U.SPC-T414). The effect (the tunnel
    /// between the stars, or the helm's flare-and-fold through a ring) runs its own length, but at its height
    /// (<see cref="HoldAt"/> of the way through) it is held for as long as the far side is still loading, up to a cap,
    /// so the destination is never seen half built; the rest of it plays out once loading is done.
    /// </summary>
    public sealed class GateCover
    {
        /// <summary>The effect's own length, seconds, with no hold.</summary>
        public double BaseSeconds { get; private set; }

        /// <summary>Share of the way through at which the effect may hold.</summary>
        public double HoldAt { get; private set; }

        /// <summary>The longest it may hold, seconds.</summary>
        public double MaxHoldSeconds { get; private set; }

        /// <summary>Seconds since it began, and how many of them it was held.</summary>
        public double Elapsed { get; private set; }

        public double Held { get; private set; }

        public bool Active { get; private set; }

        public void Begin(double baseSeconds, double holdAt, double maxHoldSeconds)
        {
            BaseSeconds = Math.Max(baseSeconds, 1e-6);
            HoldAt = holdAt < 0 ? 0 : holdAt > 1 ? 1 : holdAt;
            MaxHoldSeconds = Math.Max(maxHoldSeconds, 0);
            Elapsed = Held = 0;
            Active = true;
        }

        /// <summary>Moves the effect on by <paramref name="dt"/> seconds; at its hold point it stays put while
        /// <paramref name="farSideLoading"/>, until the cap is reached.</summary>
        public void Advance(double dt, bool farSideLoading)
        {
            if (!Active || dt <= 0) return;
            double holdPoint = HoldAt * BaseSeconds;
            double played = Elapsed - Held;
            // Held only at the hold point itself: loading that starts later (past the height of the effect) does not
            // freeze it part way through its ending.
            if (farSideLoading && Held < MaxHoldSeconds && played <= holdPoint + 1e-9 && played + dt >= holdPoint)
            {
                // Play up to the hold point, then hold the rest of this step (as far as the cap allows).
                double toHold = Math.Max(holdPoint - played, 0);
                double hold = Math.Min(dt - toHold, MaxHoldSeconds - Held);
                Held += hold;
                Elapsed += toHold + hold;
                dt -= toHold + hold;
                if (dt <= 1e-12) return;
            }
            Elapsed += dt;
            if (Elapsed - Held >= BaseSeconds) Active = false;
        }

        /// <summary>0..1 through the effect, standing still while it is held.</summary>
        public double T => Clamp01((Elapsed - Held) / BaseSeconds);

        /// <summary>Whether it is holding for the far side right now (at its hold point with time held).</summary>
        public bool Holding => Active && Held > 0 && Math.Abs(Elapsed - Held - HoldAt * BaseSeconds) < 1e-9;

        /// <summary>Played through to its end.</summary>
        public bool Done => !Active && Elapsed > 0;

        /// <summary>The helm's own flight through a ring (no camera trip): seconds, and where it holds.</summary>
        public const double HelmSeconds = 1.6, HelmHoldAt = 0.4;

        /// <summary>The glow of the far side's arrival through a ring at the helm, 0..1: up at once, held, then gone.</summary>
        public static double HelmGlow(double t)
        {
            t = Clamp01(t);
            return GateJump.Smoother(t / 0.12) * (1 - GateJump.Smoother((t - 0.6) / 0.4));
        }

        /// <summary>The fold of space shut and open again at the helm, 0..1: the view pinches in toward the hold and opens
        /// back out as the far side appears.</summary>
        public static double HelmFold(double t)
        {
            t = Clamp01(t);
            return Math.Sin(Math.PI * t);
        }

        static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
    }
}
