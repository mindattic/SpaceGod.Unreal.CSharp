using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    // Phaser-style beam arrays and plasma cannons (U.SPC-T488, docs/SHIP_ROSTER.md "Weapons by tier"). A phaser array is
    // the versatile medium-range energy weapon of destroyers and cruisers, and its captain tunes it in battle; a plasma
    // cannon is the brawler's and gate breaker's short-range gun, devastating up close, its bolt bleeding off over distance.

    /// <summary>
    /// How a hull's phaser arrays are tuned (U.SPC-T488): every array on the hull at once, by its captain, at any moment of a
    /// fight. The tuning model is beam shape. A <see cref="Lance"/> is one narrow, high-power beam at medium range for burning
    /// hulls; a <see cref="Sweep"/> spreads the same power into a wide, rapid, low-power fan at short range that works like
    /// point defence, picking off missiles and fighters first.
    /// </summary>
    public enum PhaserTuning
    {
        Lance,
        Sweep,
    }

    /// <summary>
    /// The phaser arrays' and plasma cannons' figures, every tunable a declared setting (Settings &gt; Weapons), the beam
    /// tuning AI captains choose, and the hulls each weapon is fitted to by role.
    /// </summary>
    [SettingsSource]
    public static class EnergyWeapons
    {
        // ------------------------------------------------------------ settings

        static string Km(double v) => v.ToString("0.#", CultureInfo.InvariantCulture) + " km";
        static string Hp(double v) => v.ToString("0", CultureInfo.InvariantCulture) + " hp";

        public static readonly RangeSetting LanceDamage = new RangeSetting("phaser.lanceDamage", SettingGroups.Weapons,
            "Phaser lance damage", 100, 20, 300, 5,
            "What one shot of a phaser array tuned to a narrow lance does to a hull.", Hp, "phaser beam lance energy weapon damage");

        public static readonly RangeSetting LanceRange = new RangeSetting("phaser.lanceRange", SettingGroups.Weapons,
            "Phaser lance range", 20, 8, 40, 1,
            "How far a phaser array tuned to a narrow lance reaches.", Km, "phaser beam lance energy weapon range");

        public static readonly RangeSetting SweepDamage = new RangeSetting("phaser.sweepDamage", SettingGroups.Weapons,
            "Phaser sweep damage", 16, 2, 60, 1,
            "What each pulse of a phaser array tuned to a wide sweep does: low, but fast enough to cut down fighters and missiles.", Hp,
            "phaser beam sweep point defence fighter missile damage");

        public static readonly RangeSetting SweepRange = new RangeSetting("phaser.sweepRange", SettingGroups.Weapons,
            "Phaser sweep range", 6, 2, 15, 0.5,
            "How far a phaser array tuned to a wide sweep reaches.", Km, "phaser beam sweep point defence range");

        public static readonly ToggleSetting AiTunesPhasers = new ToggleSetting("phaser.aiTunes", SettingGroups.Weapons,
            "AI captains tune phasers", true,
            "AI captains sweep their phaser arrays wide when fighters or missiles close on them and lance hulls otherwise; off, they keep the lance.",
            "phaser beam tune sweep lance ai doctrine");

        public static readonly RangeSetting PlasmaDamage = new RangeSetting("plasma.damage", SettingGroups.Weapons,
            "Plasma cannon damage", 480, 100, 1500, 10,
            "What a plasma bolt does at point-blank range, before it bleeds off over distance.", Hp, "plasma cannon bolt damage brawler");

        public static readonly RangeSetting PlasmaRange = new RangeSetting("plasma.range", SettingGroups.Weapons,
            "Plasma cannon range", 9, 3, 20, 0.5,
            "How far a plasma bolt holds together.", Km, "plasma cannon bolt range brawler");

        public static readonly RangeSetting PlasmaEdgeShare = new RangeSetting("plasma.edgeShare", SettingGroups.Weapons,
            "Plasma damage at full range", 0.2, 0, 1, 0.05,
            "The share of a plasma bolt's damage left when it lands at the very edge of its range; it falls off evenly in between (100% turns the bleed off).",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "plasma cannon falloff bleed distance");

        // ------------------------------------------------------------ the weapons

        /// <summary>A phaser array tuned to a narrow lance: a hull-burning beam at medium range, the default tuning and what a
        /// <see cref="SubsystemKind.PhaserArray"/>'s hardpoint carries as its own weapon.</summary>
        public static readonly WeaponSpec PhaserLance = new WeaponSpec { Cooldown = 5, Accuracy = 0.78 };

        /// <summary>A phaser array tuned to a wide sweep: a fast, low-power fan that engages missiles and small craft first.</summary>
        public static readonly WeaponSpec PhaserSweep = new WeaponSpec { Cooldown = 0.5, Accuracy = 0.6, AntiSmall = true };

        /// <summary>A plasma cannon: a short-ranged bolt that hits harder than any gun but a torpedo up close and bleeds off with distance.</summary>
        public static readonly WeaponSpec PlasmaCannon = new WeaponSpec { Cooldown = 10, Accuracy = 0.7 };

        static EnergyWeapons()
        {
            Apply();
            foreach (var s in new GameSetting[] { LanceDamage, LanceRange, SweepDamage, SweepRange, PlasmaDamage, PlasmaRange, PlasmaEdgeShare })
                s.Changed += _ => Apply();
        }

        /// <summary>Writes the settings into the shared weapon figures, so every fitted array and cannon follows the menu at once.</summary>
        static void Apply()
        {
            PhaserLance.Damage = LanceDamage.Value;
            PhaserLance.RangeKm = LanceRange.Value;
            PhaserSweep.Damage = SweepDamage.Value;
            PhaserSweep.RangeKm = SweepRange.Value;
            PlasmaCannon.Damage = PlasmaDamage.Value;
            PlasmaCannon.RangeKm = PlasmaRange.Value;
            PlasmaCannon.FalloffEdgeShare = PlasmaEdgeShare.Value;
        }

        // ------------------------------------------------------------ tuning

        /// <summary>The weapon a hardpoint fires as it stands: a phaser array's tuned beam, otherwise its own.</summary>
        public static WeaponSpec Effective(Subsystem s, ShipCombat c) =>
            s.Kind == SubsystemKind.PhaserArray && c != null && c.PhaserTuning == PhaserTuning.Sweep ? PhaserSweep : s.Def.Weapon;

        /// <summary>Whether the hull carries a working phaser array.</summary>
        public static bool HasPhasers(ShipCombat c)
        {
            if (c == null) return false;
            foreach (var s in c.Subsystems)
                if (s.Kind == SubsystemKind.PhaserArray && !s.Destroyed) return true;
            return false;
        }

        /// <summary>The captain's other tuning: the player's order at the helm or in the seat.</summary>
        public static PhaserTuning Retune(ShipCombat c)
        {
            if (c == null) return PhaserTuning.Lance;
            c.PhaserTuning = c.PhaserTuning == PhaserTuning.Lance ? PhaserTuning.Sweep : PhaserTuning.Lance;
            return c.PhaserTuning;
        }

        public static string Label(PhaserTuning t) => t == PhaserTuning.Sweep ? "wide sweep" : "narrow lance";

        /// <summary>Small craft or missiles hostile to the hull, within the sweep's reach of it, that make an AI captain sweep:
        /// two at once, or one with no hull in the lance's reach to burn instead.</summary>
        public const int SweepThreats = 2;

        /// <summary>
        /// An AI captain's tuning for this moment (<see cref="AiTunesPhasers"/>): a wide sweep while fighters or missiles are
        /// closing within its reach (a little beyond, so it is ready when they arrive), a lance otherwise. The player's own
        /// hull is left as the player tuned it.
        /// </summary>
        public static PhaserTuning Choose(Battle b, BattleUnit u)
        {
            double reach = PhaserSweep.RangeKm * 1.5, reach2 = reach * reach;
            double lance = PhaserLance.RangeKm;
            int small = 0;
            bool hullInReach = false;
            foreach (var o in b.Units)
            {
                if (o == u || !b.Hostile(u, o) || o.Surrendering || o.Captured) continue;
                double d = (o.Pos - u.Pos).Length;
                if (o.Kind == UnitKind.Fighter) { if (d * d <= reach2) small++; }
                else if (d - o.Radius <= lance) hullInReach = true;
            }
            foreach (var m in b.Missiles)
                if (!m.Gone && m.Side != u.Side && (m.Pos - u.Pos).LengthSquared <= reach2) small++;
            return small >= SweepThreats || (small > 0 && !hullInReach) ? PhaserTuning.Sweep : PhaserTuning.Lance;
        }

        /// <summary>Retunes an AI-flown hull's arrays for the fight as it stands, each step before its guns fire.</summary>
        public static void Step(Battle b, BattleUnit u)
        {
            if (u.PlayerControlled || !AiTunesPhasers.Value || !HasPhasers(u.Combat)) return;
            u.Combat.PhaserTuning = Choose(b, u);
        }

        /// <summary>The effect a shot from this hardpoint draws, or null for the battle's usual beam, gun or slug.</summary>
        public static FxKind? FxOf(SubsystemKind k, bool hit)
        {
            switch (k)
            {
                case SubsystemKind.PhaserArray: return hit ? FxKind.Phaser : FxKind.PhaserMiss;
                case SubsystemKind.PlasmaCannon: return hit ? FxKind.Plasma : FxKind.PlasmaMiss;
                default: return null;
            }
        }

        // ------------------------------------------------------------ fitting by role

        /// <summary>
        /// The hull classes each role-bound weapon is fitted to (docs/SHIP_ROSTER.md): phaser arrays to destroyers and cruisers,
        /// plasma cannons to the brawlers (the battleship's line and the breacher's assault) and the gate breaker's siege
        /// batteries (U.SPC-T478). A module not listed fits any hull with a slot that takes it. A new class joins a role with
        /// <see cref="Allow"/>.
        /// </summary>
        static readonly Dictionary<ModuleKind, HashSet<ShipClass>> Roles = new Dictionary<ModuleKind, HashSet<ShipClass>>
        {
            [ModuleKind.PhaserArray] = new HashSet<ShipClass> { ShipClass.Destroyer, ShipClass.Cruiser },
            [ModuleKind.PlasmaCannon] = new HashSet<ShipClass> { ShipClass.Battleship, ShipClass.GateBreaker },
        };

        /// <summary>Lets hulls of <paramref name="cls"/> take <paramref name="module"/> (a new class taking up a weapon's role).</summary>
        public static void Allow(ModuleKind module, ShipClass cls)
        {
            if (!Roles.TryGetValue(module, out var set)) return;
            set.Add(cls);
        }

        /// <summary>Whether a hull of <paramref name="cls"/> (null: a station or satellite) may carry <paramref name="module"/>.</summary>
        public static bool RoleAllows(ModuleKind module, ShipClass? cls) =>
            !Roles.TryGetValue(module, out var set) || (cls is ShipClass c && set.Contains(c));
    }
}
