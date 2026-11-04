using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>Where an alpha-striking hull is in its run (U.SPC-T475).</summary>
    public enum StrikePhase
    {
        /// <summary>Burning in at its target, every launcher held for the volley.</summary>
        RunIn,

        /// <summary>The volley loosed: flipped and burning back out of the enemy's reach while its launchers reload.</summary>
        Egress,
    }

    /// <summary>
    /// The frigate's doctrine, the alpha strike (U.SPC-T475, docs/SHIP_ROSTER.md): a fast, lightly armoured missile and
    /// torpedo boat does not trade fire. It burns in at its mark with every launcher held, and only once the mark is inside
    /// the reach of all of them (<see cref="VolleyShare"/>) and every one is loaded does it loose them together, one
    /// devastating volley of anti-ship missiles and antimatter torpedoes; then it flips and burns back out
    /// (<see cref="EgressKm"/>) while they reload, and comes round for another run. Speed and distance are its armour: it
    /// spends most of a fight beyond the guns' reach, and only runs the gauntlet to strike. With its magazines spent it has
    /// nothing left to fight with and withdraws. Which classes fly the doctrine is data (<see cref="Register"/>); the
    /// battle asks <see cref="Flies"/>, <see cref="Holds"/> and <see cref="Steer"/>. Every figure is a setting in the menu's
    /// Broadsides group.
    /// </summary>
    [SettingsSource]
    public static class AlphaStrike
    {
        static string Km(double v) => v.ToString("0", CultureInfo.InvariantCulture) + " km";

        static string Percent(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        static string Times(double v) => v.ToString("0.##", CultureInfo.InvariantCulture) + "x";

        public static readonly RangeSetting VolleyShare = new RangeSetting("frigate.volleyRange", SettingGroups.Broadsides,
            "Alpha strike range", 0.8, 0.3, 1, 0.05,
            "How far into the reach of its shortest-ranged launcher a frigate burns before loosing its volley: every launcher " +
            "then reaches its mark, and lower holds it until it is surer of it.", Percent,
            "frigate alpha strike volley range torpedo missile run");

        public static readonly RangeSetting EgressKm = new RangeSetting("frigate.egressKm", SettingGroups.Broadsides,
            "Alpha strike egress", 32, 10, 44, 1,
            "How far (km) from its mark a frigate burns back out after its volley before it comes round for another run, once " +
            "its launchers have reloaded.", Km, "frigate alpha strike egress withdraw distance burn out flip");

        public static readonly RangeSetting Burn = new RangeSetting("frigate.burn", SettingGroups.Broadsides,
            "Alpha strike burn", 1, 0, 1, 0.05,
            "How much of its boost a frigate burns on its runs in and out (0 flies them at its cruising speed).", Percent,
            "frigate alpha strike speed boost burn run");

        public static readonly RangeSetting CapitalBias = new RangeSetting("frigate.capitalBias", SettingGroups.Broadsides,
            "Frigate capital preference", 12, 0, 40, 1,
            "How strongly a frigate picks a capital ship over a smaller hull to strike (as if it were that many km nearer): its " +
            "torpedoes are built to sink the big ones.", Km, "frigate alpha strike target capital battleship carrier preference");

        // ---------------------------------------------------------------- the magazines

        /// <summary>A frigate's long-range anti-ship missile: further-reaching, quicker and harder-hitting than a warship's
        /// stock missile, from a magazine half again as deep.</summary>
        public static readonly WeaponSpec AntiShipMissile = new WeaponSpec
        {
            RangeKm = 50, Damage = 300, Cooldown = 30, Accuracy = 1, Missile = true, MissileSpeedKmS = 1.6, Salvo = 2, Ammo = 24,
        };

        /// <summary>A frigate's antimatter (photon) torpedo: the capitals' warhead (<see cref="WeaponSpec.AntimatterTorpedo"/>),
        /// with twice the rounds in the magazine behind its launcher.</summary>
        public static readonly WeaponSpec MagazineTorpedo = new WeaponSpec
        {
            RangeKm = WeaponSpec.AntimatterTorpedo.RangeKm, Damage = WeaponSpec.AntimatterTorpedo.Damage, Cooldown = WeaponSpec.AntimatterTorpedo.Cooldown,
            Accuracy = 1, Missile = true, MissileSpeedKmS = WeaponSpec.AntimatterTorpedo.MissileSpeedKmS, Salvo = 1, Ammo = 2 * WeaponSpec.AntimatterTorpedo.Ammo,
        };

        // ---------------------------------------------------------------- who flies it

        static readonly HashSet<ShipClass> classes = new HashSet<ShipClass> { ShipClass.Frigate };

        /// <summary>Has hulls of <paramref name="cls"/> fly the alpha strike (or not): how a new strike boat plugs in.</summary>
        public static void Register(ShipClass cls, bool flies = true)
        {
            if (flies) classes.Add(cls);
            else classes.Remove(cls);
        }

        /// <summary>Whether hulls of <paramref name="cls"/> fly the alpha strike.</summary>
        public static bool Doctrine(ShipClass cls) => classes.Contains(cls);

        /// <summary>Whether <paramref name="u"/> fights by the alpha strike now: a hull of a class that flies it, under its
        /// crew's command (the player flies and fires his own), with a working launcher left.</summary>
        public static bool Flies(BattleUnit u) =>
            u != null && u.Kind == UnitKind.Ship && u.Ship?.Spec != null && !u.PlayerControlled && Doctrine(u.Ship.Spec.Class)
            && u.Combat != null && StrikeMounts(u.Combat) > 0;

        /// <summary>A launcher that goes in the volley: a missile or torpedo launcher aimed at hulls (not a screen's
        /// interceptors, an EMP or boarding pods).</summary>
        public static bool IsStrikeMount(Subsystem s)
        {
            var w = s?.Def.Weapon;
            return w != null && w.Missile && !w.AntiSmall && !w.Supporting && s.Kind != SubsystemKind.BoardingPods;
        }

        /// <summary>The launchers that still have something to fire: standing, with rounds in the magazine.</summary>
        public static int StrikeMounts(ShipCombat c)
        {
            int n = 0;
            foreach (var s in c.Subsystems)
                if (IsStrikeMount(s) && !s.Destroyed && s.Ammo > 0) n++;
            return n;
        }

        /// <summary>How near its mark <paramref name="u"/> must be to loose its volley, km: <see cref="VolleyShare"/> of its
        /// shortest-ranged working launcher's reach, so every one of them reaches.</summary>
        public static double VolleyRangeKm(BattleUnit u)
        {
            double reach = double.MaxValue;
            foreach (var s in u.Combat.Subsystems)
                if (IsStrikeMount(s) && !s.Destroyed && s.Ammo > 0) reach = Math.Min(reach, s.Def.Weapon.RangeKm);
            return reach == double.MaxValue ? 0 : reach * VolleyShare.Value;
        }

        /// <summary>Whether every working launcher on <paramref name="u"/> is loaded.</summary>
        public static bool Loaded(BattleUnit u)
        {
            foreach (var s in u.Combat.Subsystems)
                if (IsStrikeMount(s) && !s.Destroyed && s.Ammo > 0 && s.Cooldown > 0) return false;
            return true;
        }

        /// <summary>How far <paramref name="u"/> is from the surface of its mark, km.</summary>
        static double Gap(BattleUnit u) => Math.Max(0, (u.Target.Pos - u.Pos).Length - u.Target.Radius);

        /// <summary>Whether <paramref name="u"/>, burning in, is close enough with every launcher loaded to loose the volley.</summary>
        public static bool Releases(BattleUnit u) =>
            u.Strike == StrikePhase.RunIn && u.Target != null && u.Target.Present && Loaded(u) && Gap(u) <= VolleyRangeKm(u);

        /// <summary>Whether <paramref name="s"/>, one of an alpha-striking hull's launchers, holds its fire this step: always on
        /// the way out, and on the way in until the whole volley can go (<paramref name="release"/>, from <see cref="Releases"/>).</summary>
        public static bool Holds(Subsystem s, bool release) => IsStrikeMount(s) && !release;

        /// <summary>The volley is away: <paramref name="u"/> flips and burns back out.</summary>
        public static void Loosed(BattleUnit u) => u.Strike = StrikePhase.Egress;

        /// <summary>
        /// Where <paramref name="u"/> wants to go and which way it points this step, flying the alpha strike at its
        /// <see cref="BattleUnit.Target"/> with <paramref name="speed"/> as its cruising speed. Burning in, it drives straight
        /// at the mark nose-on, its launchers bearing; burning out, it turns its tail to the mark and drives away from it,
        /// until it is <see cref="EgressKm"/> off (or near the edge of the fight) with every launcher loaded, and then comes
        /// round for the next run. Returns how hard it burns on this leg: the multiple of its cruising speed and its drive's
        /// push it flies at, its boost as far as <see cref="Burn"/> has it, so the flip sheds its run-in speed quickly.
        /// </summary>
        public static double Steer(BattleUnit u, double speed, out DVec3 desired, out DVec3 face)
        {
            var t = u.Target;
            var to = t.Pos - u.Pos;
            double dist = Math.Max(to.Length, 1e-6);
            var dir = to / dist;
            double boost = 1 + (Math.Max(1, u.Combat.Spec.BoostSpeedMultiplier) - 1) * Burn.Value;
            double top = speed * boost;
            // Out far enough (or at the edge of the fight): clear of the enemy's reach.
            bool clear = Gap(u) >= EgressKm.Value || u.Pos.Length > Battle.EscapeKm * 0.7;
            // Reloaded out there: come round for the next run.
            if (u.Strike == StrikePhase.Egress && clear && Loaded(u)) u.Strike = StrikePhase.RunIn;
            if (u.Strike == StrikePhase.RunIn)
            {
                desired = dir * top;
                face = dir;
                return boost;
            }
            // Burning out: away from the mark, bending back in rather than leaving the fight altogether.
            var away = -dir;
            if (u.Pos.Length > Battle.EscapeKm * 0.6) away = (away + (-u.Pos).Normalized * 1.2).Normalized;
            if (clear)
            {
                // Waiting on its reload out of reach: hold off, circling.
                var around = DVec3.Cross(DVec3.UnitZ, dir);
                if (around.LengthSquared < 1e-6) around = DVec3.UnitY;
                desired = around.Normalized * speed * 0.5;
                face = desired;
                return boost;
            }
            desired = away * top;
            face = away;
            return boost;
        }

        /// <summary>How much nearer than it is a frigate counts <paramref name="o"/> when it picks its mark, km: a capital
        /// ship is what its torpedoes are for.</summary>
        public static double TargetBias(BattleUnit u, BattleUnit o)
        {
            if (u?.Ship?.Spec == null || !Doctrine(u.Ship.Spec.Class) || o == null || o.Kind == UnitKind.Fighter) return 0;
            return o.LengthKm >= 1 ? CapitalBias.Value : 0;
        }
    }
}
