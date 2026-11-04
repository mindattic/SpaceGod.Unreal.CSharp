using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// How a renderable thing is drawn by its size on screen (U.SPC-T415), from nearest to farthest: <see cref="Full"/>
    /// model, <see cref="Simplified"/> model (the hull alone, without turrets, scars, glows, shields or exhaust),
    /// <see cref="Icon"/> (a flat faction icon, or a glow impostor for small craft), <see cref="Aggregate"/> (folded with
    /// its neighbours into one mark that stands for the lot: a fleet, a crowd of traffic, or a whole other star system),
    /// and <see cref="Culled"/> (not drawn at all).
    /// </summary>
    public enum LodTier
    {
        Full,
        Simplified,
        Icon,
        Aggregate,
        Culled,
    }

    /// <summary>What a class of thing becomes once it is too small to draw as a model.</summary>
    public enum LodFar
    {
        /// <summary>Nothing: below its model it is culled (a structure, a docking ring, a wreck).</summary>
        None,

        /// <summary>A glow or sprite a few pixels wide while the thing itself is still at least the cull size (a fighter, a pod's beacon).</summary>
        Impostor,

        /// <summary>An icon however small it gets, folded into a mark with its neighbours when they crowd (ships, worlds).</summary>
        Icon,
    }

    /// <summary>
    /// One class of renderable thing and how it degrades: whether it has a simpler model, the apparent size below which
    /// its model gives way to its far form, and what that far form is. New renderable things register one with
    /// <see cref="RenderLod.Register"/> instead of drawing in full at every size.
    /// </summary>
    public sealed class LodClass
    {
        public readonly string Key, Name;
        public readonly LodFar Far;

        /// <summary>Whether it has a simplified model between its full model and its far form.</summary>
        public readonly bool Simplifies;

        readonly Func<double> modelBelowPx;

        public LodClass(string key, string name, Func<double> modelBelowPx, LodFar far, bool simplifies)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A render class needs a key.", nameof(key));
            Key = key;
            Name = string.IsNullOrWhiteSpace(name) ? key : name;
            this.modelBelowPx = modelBelowPx ?? (() => 0);
            Far = far;
            Simplifies = simplifies;
        }

        /// <summary>Below this many pixels on screen the model gives way to its far form (or to nothing).</summary>
        public double ModelBelowPx => modelBelowPx();

        public override string ToString() => Name;
    }

    /// <summary>
    /// The render budget at universe scale (U.SPC-T415): every renderable class (ships, worlds, gates, structures,
    /// docking rings, defence platforms, fighters, escape pods, wrecks, and any a new system registers) asks here which
    /// <see cref="LodTier"/> it is drawn in for its apparent size, so the 3D view, the HUD's icons and picking always
    /// agree. Icons that crowd on screen are folded into one mark by <see cref="Aggregate"/>, and another star system is
    /// one mark in the sky. The swap points are settings (LOD &amp; render budget), so the trade between detail and frame
    /// rate is the player's to make; a ship's own model-to-icon point stays <see cref="ShipLod.IconBelowPx"/>.
    /// </summary>
    [SettingsSource]
    public static class RenderLod
    {
        /// <summary>Below this many pixels long a model drops its detail for its simplified form.</summary>
        public static readonly RangeSetting SimplifyBelowPx = new RangeSetting("lod.simplifyPx", SettingGroups.RenderBudget,
            "Full model to simplified", 48, 8, 400, 1,
            "A ship or structure shorter than this on screen drops its turrets, damage, glows, shields and exhaust and draws its hull alone. Higher is faster with many ships in view.",
            v => v.ToString("0") + " px", "lod simplified detail model hardpoints turret performance");

        /// <summary>Below this many pixels a thing with no icon is not drawn at all.</summary>
        public static readonly RangeSetting CullBelowPx = new RangeSetting("lod.cullPx", SettingGroups.RenderBudget,
            "Cull below", 1, 0, 4, 0.25,
            "Anything smaller than this on screen that has no icon (fighters, pods, wrecks, structures, rings, gates and platforms) is not drawn at all. Ships and worlds keep their icons and fold into marks instead.",
            v => v.ToString("0.##") + " px", "lod cull pixel hide small far performance");

        /// <summary>Icons of one side closer than this on screen fold into one mark with a count.</summary>
        public static readonly RangeSetting AggregatePx = new RangeSetting("lod.aggregatePx", SettingGroups.RenderBudget,
            "Merge icons closer than", 14, 0, 64, 1,
            "Far-off ships of one side whose icons come closer than this on screen are drawn as one mark with how many it stands for: a fleet, or a crowd of traffic round a port. 0 draws every icon.",
            v => v <= 0 ? "Off" : v.ToString("0") + " px", "lod aggregate merge cluster fleet icon mark count performance");

        static readonly List<LodClass> classes = new List<LodClass>();
        static readonly Dictionary<string, LodClass> byKey = new Dictionary<string, LodClass>();

        public static readonly LodClass Ship = Register(new LodClass("ship", "Ship", () => ShipLod.IconBelowPx.Value, LodFar.Icon, true));
        public static readonly LodClass Body = Register(new LodClass("body", "World", () => 0, LodFar.Icon, false));
        public static readonly LodClass Gate = Register(new LodClass("gate", "Jump gate", () => 0, LodFar.None, false));
        public static readonly LodClass Structure = Register(new LodClass("structure", "Structure", () => 4, LodFar.None, false));
        public static readonly LodClass DockingRing = Register(new LodClass("ring", "Docking ring", () => 3, LodFar.None, false));
        public static readonly LodClass Platform = Register(new LodClass("platform", "Defence platform", () => 0, LodFar.None, false));
        public static readonly LodClass Fighter = Register(new LodClass("fighter", "Fighter", () => 3, LodFar.Impostor, false));
        public static readonly LodClass Pod = Register(new LodClass("pod", "Escape pod", () => 3, LodFar.Impostor, false));
        public static readonly LodClass Wreck = Register(new LodClass("wreck", "Wreck", () => 0, LodFar.None, false));

        /// <summary>Every registered render class, in the order registered.</summary>
        public static IReadOnlyList<LodClass> Classes => classes;

        /// <summary>Adds a render class (a new kind of renderable thing joins the scheme here); a key already taken is refused.</summary>
        public static LodClass Register(LodClass c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            lock (classes)
            {
                if (byKey.ContainsKey(c.Key)) throw new ArgumentException("A render class is already registered as " + c.Key, nameof(c));
                byKey[c.Key] = c;
                classes.Add(c);
            }
            return c;
        }

        public static LodClass Find(string key)
        {
            lock (classes) return key != null && byKey.TryGetValue(key, out var c) ? c : null;
        }

        /// <summary>How big <paramref name="length"/> scene units look at <paramref name="depth"/>, given the camera's pixels per unit at unit distance.</summary>
        public static double ApparentPx(double length, double depth, double pixelsPerUnit) =>
            length / Math.Max(depth, 1e-9) * pixelsPerUnit;

        /// <summary>The tier a thing of class <paramref name="c"/> is drawn in at <paramref name="px"/> pixels, by the current settings.</summary>
        public static LodTier Tier(LodClass c, double px) =>
            Tier(px, c.ModelBelowPx, c.Far, c.Simplifies, SimplifyBelowPx.Value, CullBelowPx.Value);

        /// <summary>
        /// The tier for <paramref name="px"/> pixels: its model at or above the model swap point (and never below the cull
        /// size), simplified under <paramref name="simplifyBelowPx"/> if it has a simpler form; below that its icon,
        /// its impostor while it is still at least <paramref name="cullBelowPx"/>, or nothing. <see cref="LodTier.Aggregate"/>
        /// is never returned here: icons are folded into marks by <see cref="Aggregate"/>, where they crowd.
        /// </summary>
        public static LodTier Tier(double px, double modelBelowPx, LodFar far, bool simplifies, double simplifyBelowPx, double cullBelowPx)
        {
            if (px >= Math.Max(modelBelowPx, cullBelowPx) && px > 0)
                return simplifies && px < simplifyBelowPx ? LodTier.Simplified : LodTier.Full;
            if (far == LodFar.Icon) return LodTier.Icon;
            if (far == LodFar.Impostor && px >= cullBelowPx) return LodTier.Icon;
            return LodTier.Culled;
        }

        /// <summary>Whether the tier draws a 3D model.</summary>
        public static bool DrawsModel(LodTier t) => t == LodTier.Full || t == LodTier.Simplified;

        /// <summary>
        /// Folds icons that crowd on screen into marks (U.SPC-T415). Icons of one <see cref="LodMark.Group"/> (a side)
        /// within <paramref name="radiusPx"/> of a mark's place fold into it; the heaviest icon (the biggest hull)
        /// stands for each mark and the mark sits where it is. A pinned icon (selected, hovered, followed, flown) is
        /// always its own mark and folds nothing. Runs in about linear time on a grid, so thousands of icons cost little.
        /// Each mark's <see cref="LodCluster.Members"/> lists its icons by index into <paramref name="marks"/>, its
        /// representative first.
        /// </summary>
        public static List<LodCluster> Aggregate(IReadOnlyList<LodMark> marks, double radiusPx)
        {
            var result = new List<LodCluster>();
            if (marks == null || marks.Count == 0) return result;
            var order = new int[marks.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            // Pinned first, then heaviest first; ties by index so the result is stable frame to frame.
            Array.Sort(order, (a, b) =>
            {
                var ma = marks[a];
                var mb = marks[b];
                if (ma.Pinned != mb.Pinned) return ma.Pinned ? -1 : 1;
                int w = mb.Weight.CompareTo(ma.Weight);
                return w != 0 ? w : a.CompareTo(b);
            });

            bool merge = radiusPx > 0;
            double cell = merge ? radiusPx : 1;
            double r2 = radiusPx * radiusPx;
            var grid = new Dictionary<(long, long, int), List<LodCluster>>();
            foreach (int i in order)
            {
                var m = marks[i];
                if (m.Pinned || !merge)
                {
                    var own = new LodCluster(i, m);
                    result.Add(own);
                    continue;
                }
                long cx = (long)Math.Floor(m.X / cell), cy = (long)Math.Floor(m.Y / cell);
                LodCluster into = null;
                double best = double.MaxValue;
                for (long dx = -1; dx <= 1; dx++)
                    for (long dy = -1; dy <= 1; dy++)
                    {
                        if (!grid.TryGetValue((cx + dx, cy + dy, m.Group), out var near)) continue;
                        foreach (var c in near)
                        {
                            double ex = c.X - m.X, ey = c.Y - m.Y, d2 = ex * ex + ey * ey;
                            if (d2 <= r2 && d2 < best)
                            {
                                best = d2;
                                into = c;
                            }
                        }
                    }
                if (into != null)
                {
                    into.Members.Add(i);
                    continue;
                }
                var made = new LodCluster(i, m);
                result.Add(made);
                var key = (cx, cy, m.Group);
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<LodCluster>();
                list.Add(made);
            }
            return result;
        }

        /// <summary>How a folded mark is labelled with the count it stands for: "" for one, "×12" for twelve.</summary>
        public static string CountLabel(int count) => count > 1 ? "×" + count : "";

        /// <summary>
        /// How another star system's one mark in the sky is labelled (U.SPC-T415): every ship in it is folded into that
        /// mark, so it says how many there are; "" when there are none.
        /// </summary>
        public static string SystemShipsLabel(int ships) => ships <= 0 ? "" : ships == 1 ? "1 ship" : ships + " ships";

        /// <summary>The ships still flying in <paramref name="fleet"/> (not destroyed, not gone through a gate), for a system's mark.</summary>
        public static int LiveShips(Fleet fleet)
        {
            if (fleet == null) return 0;
            int n = 0;
            foreach (var s in fleet.Ships)
                if (!s.Destroyed) n++;
            return n;
        }
    }

    /// <summary>One icon on screen, for <see cref="RenderLod.Aggregate"/>.</summary>
    public struct LodMark
    {
        /// <summary>Its place on screen, pixels.</summary>
        public double X, Y;

        /// <summary>Its side: only icons of one group fold together.</summary>
        public int Group;

        /// <summary>How much it should stand for the others (the biggest hull leads).</summary>
        public double Weight;

        /// <summary>Kept apart: never folded into another mark, never folds one in.</summary>
        public bool Pinned;

        public LodMark(double x, double y, int group, double weight, bool pinned = false)
        {
            X = x;
            Y = y;
            Group = group;
            Weight = weight;
            Pinned = pinned;
        }
    }

    /// <summary>One mark drawn for one or more icons (see <see cref="RenderLod.Aggregate"/>).</summary>
    public sealed class LodCluster
    {
        /// <summary>Index of the icon that stands for the mark.</summary>
        public readonly int Representative;

        /// <summary>Where the mark sits on screen: its representative's place.</summary>
        public readonly double X, Y;

        /// <summary>Every icon it stands for, by index, its representative first.</summary>
        public readonly List<int> Members = new List<int>(1);

        public int Count => Members.Count;

        /// <summary>The tier its icons are drawn in: <see cref="LodTier.Icon"/> for one alone, <see cref="LodTier.Aggregate"/> for a fold.</summary>
        public LodTier Tier => Members.Count > 1 ? LodTier.Aggregate : LodTier.Icon;

        internal LodCluster(int index, LodMark m)
        {
            Representative = index;
            X = m.X;
            Y = m.Y;
            Members.Add(index);
        }
    }
}
