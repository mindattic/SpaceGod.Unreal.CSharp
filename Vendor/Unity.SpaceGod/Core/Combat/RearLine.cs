using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The dreadnought's doctrine, the rear line (U.SPC-T490, docs/SHIP_ROSTER.md): a battleship built around its spinal
    /// mounts (<see cref="SpinalMounts"/>) does not close with the enemy. It holds the rear of its own side, behind the line
    /// its escorts fight on, and snipes from the extreme reach of its spinal guns: it keeps <see cref="Standoff"/> of the
    /// reach of its shortest-ranged spinal mount between itself and its mark, so every one of them bears, holds its bow on
    /// the mark (the hull lays the guns), and backs off from any hostile hull that closes inside <see cref="GuardKm"/>. It
    /// picks the capital ships first (<see cref="CapitalBias"/>): their size is what a slug crossing fifty km is sure to
    /// find. Once its spinal mounts are shot away it fights as any warship does. Which classes fly it is data
    /// (<see cref="Register"/>); the battle asks <see cref="Flies"/>, <see cref="Steer"/> and <see cref="TargetBias"/>.
    /// Every figure is a setting in the menu's Broadsides group.
    /// </summary>
    [SettingsSource]
    public static class RearLine
    {
        static string Km(double v) => v.ToString("0", CultureInfo.InvariantCulture) + " km";

        static string Percent(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        public static readonly RangeSetting Standoff = new RangeSetting("dreadnought.standoff", SettingGroups.Broadsides,
            "Dreadnought standoff", 0.85, 0.4, 1, 0.05,
            "How far into the reach of its shortest-ranged spinal mount a battleship holds off its mark: every spinal gun then " +
            "bears, and lower brings it in closer to be surer of its shots.", Percent,
            "dreadnought battleship spinal standoff range snipe rear line");

        public static readonly RangeSetting GuardKm = new RangeSetting("dreadnought.guardKm", SettingGroups.Broadsides,
            "Dreadnought guard distance", 12, 0, 30, 1,
            "How near (km) a hostile hull may close on a battleship holding the rear before it backs away from it.", Km,
            "dreadnought battleship rear line guard distance back off withdraw");

        public static readonly RangeSetting RearBias = new RangeSetting("dreadnought.rearBias", SettingGroups.Broadsides,
            "Dreadnought holds the rear", 0.6, 0, 2, 0.05,
            "How strongly a battleship keeps its own side's line between itself and the enemy as it takes its firing station " +
            "(0 lets it stand off on whatever side it happens to be).", Percent,
            "dreadnought battleship rear line station behind escorts");

        public static readonly RangeSetting CapitalBias = new RangeSetting("dreadnought.capitalBias", SettingGroups.Broadsides,
            "Dreadnought capital preference", 10, 0, 40, 1,
            "How strongly a battleship picks a capital ship over a smaller hull to snipe (as if it were that many km nearer).", Km,
            "dreadnought battleship spinal target capital preference");

        // ---------------------------------------------------------------- who flies it

        static readonly HashSet<ShipClass> classes = new HashSet<ShipClass> { ShipClass.Battleship };

        /// <summary>Has hulls of <paramref name="cls"/> hold the rear line (or not): how a new dreadnought plugs in.</summary>
        public static void Register(ShipClass cls, bool flies = true)
        {
            if (flies) classes.Add(cls);
            else classes.Remove(cls);
        }

        /// <summary>Whether hulls of <paramref name="cls"/> hold the rear line.</summary>
        public static bool Doctrine(ShipClass cls) => classes.Contains(cls);

        /// <summary>Whether <paramref name="u"/> fights by the rear line now: a hull of a class that flies it, under its crew's
        /// command (the player lays his own guns), with a spinal mount still standing.</summary>
        public static bool Flies(BattleUnit u) =>
            u != null && u.Kind == UnitKind.Ship && u.Ship?.Spec != null && !u.PlayerControlled && Doctrine(u.Ship.Spec.Class)
            && u.Combat != null && SpinalMounts.Standing(u.Combat) > 0;

        /// <summary>How far off its mark's surface <paramref name="u"/> holds, km: <see cref="Standoff"/> of its shortest
        /// standing spinal mount's reach.</summary>
        public static double StandoffKm(BattleUnit u) => SpinalMounts.ReachKm(u.Combat) * Standoff.Value;

        /// <summary>
        /// Where <paramref name="u"/> wants to go and which way it points this step, holding the rear line against its
        /// <see cref="BattleUnit.Target"/> with <paramref name="speed"/> as its top speed. It takes a firing station
        /// <see cref="StandoffKm"/> off its mark, swung round toward its own side's rear (<see cref="RearBias"/>), backs away
        /// from any hostile hull inside <see cref="GuardKm"/>, keeps inside the fight, and holds its bow on the mark.
        /// </summary>
        public static void Steer(Battle b, BattleUnit u, double speed, out DVec3 desired, out DVec3 face)
        {
            var t = u.Target;
            var from = u.Pos - t.Pos;
            double dist = Math.Max(from.Length, 1e-6);
            var outward = from / dist;

            // The rear of its own side: from the enemy's centre toward its own line's (or toward itself, alone).
            DVec3 friends = DVec3.Zero, foes = DVec3.Zero;
            int nf = 0, nh = 0;
            foreach (var o in b.Units)
            {
                if (o == u || !o.Present || o.Kind == UnitKind.Fighter) continue;
                if (o.Side == u.Side) { friends += o.Pos; nf++; }
                else if (b.Hostile(u, o)) { foes += o.Pos; nh++; }
            }
            var dir = outward;
            if (nh > 0 && RearBias.Value > 0)
            {
                var rear = (nf > 0 ? friends / nf : u.Pos) - foes / nh;
                if (rear.LengthSquared > 1e-9)
                {
                    var bent = outward + rear.Normalized * RearBias.Value;
                    if (bent.LengthSquared > 1e-9) dir = bent.Normalized;
                }
            }
            var spot = t.Pos + dir * (StandoffKm(u) + t.Radius);
            desired = (spot - u.Pos) * 0.02;

            // Anything closing on it is backed away from, harder the nearer it is.
            double guard = GuardKm.Value;
            if (guard > 0)
                foreach (var o in b.Units)
                {
                    if (!o.Present || o.Kind == UnitKind.Fighter || !b.Hostile(u, o)) continue;
                    var away = u.Pos - o.Pos;
                    double gap = away.Length - o.Radius - u.Radius;
                    if (gap >= guard || away.LengthSquared < 1e-12) continue;
                    desired += away.Normalized * speed * (1 - Math.Max(0, gap) / guard);
                }

            // Never so far back that it leaves the fight.
            if (u.Pos.Length > Battle.EscapeKm * 0.7) desired += -u.Pos.Normalized * speed;

            face = t.Pos - u.Pos;
        }

        /// <summary>How much nearer than it is a dreadnought counts <paramref name="o"/> when it picks its mark, km: a capital
        /// ship is what its spinal guns are for.</summary>
        public static double TargetBias(BattleUnit u, BattleUnit o)
        {
            if (!Flies(u) || o == null || o.Kind == UnitKind.Fighter) return 0;
            return o.LengthKm >= 1 ? CapitalBias.Value : 0;
        }
    }
}
