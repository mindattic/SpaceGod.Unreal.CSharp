using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Spinal mounts (U.SPC-T490, docs/SHIP_ROSTER.md "Dreadnought / Battleship"): a battleship is built around one or two
    /// guns as long as the hull itself, laid along the keel with their muzzles at the bow, fed by a large power plant inside
    /// the hull. A spinal mount cannot traverse (<see cref="ConeHalfDeg"/>): the hull lays it on the target. Four weapons go
    /// in one: a spinal railgun (the longest kinetic reach and the hardest slug in the game, on a slow recharge), a coilgun
    /// (a shorter reach, a lighter, slower slug, but cycling twice as fast), a tachyon lance (U.SPC-T487) and a plasma
    /// spinal cannon (an enormous bolt that bleeds off over distance, U.SPC-T488). Each spinal mount draws on a spinal power
    /// plant (<see cref="FacilityKinds.SpinalPlant"/>, a facility inside the hull): with fewer plants than standing mounts
    /// they recharge only at <see cref="Unpowered"/> of the rate. Which weapons go in a spinal mount, and what each becomes
    /// there, is data (<see cref="Register"/>); the yard, the battle and the doctrine (<see cref="RearLine"/>) read it from
    /// here. Every figure is a setting in Settings &gt; Broadsides.
    /// </summary>
    [SettingsSource]
    public static class SpinalMounts
    {
        // ------------------------------------------------------------ the weapons (before the settings that tune them)

        /// <summary>A spinal railgun: a slug driven down rails as long as the hull, the longest reach of any kinetic gun and a
        /// hit second only to a torpedo's, at twenty-two km/s, on a long recharge.</summary>
        public static readonly WeaponSpec Railgun = new WeaponSpec { RangeKm = 50, Damage = 900, Cooldown = 30, Accuracy = 0.9, MuzzleSpeedKmS = 22 };

        /// <summary>A spinal coilgun: magnetic coils down the keel throw a lighter, slower slug a little less far, but recharge in
        /// half the time.</summary>
        public static readonly WeaponSpec Coilgun = new WeaponSpec { RangeKm = 44, Damage = 520, Cooldown = 15, Accuracy = 0.85, MuzzleSpeedKmS = 14 };

        /// <summary>A plasma spinal cannon: a bolt four times a turret plasma cannon's, holding together more than twice as far,
        /// but still a brawler's weapon whose damage bleeds off with distance.</summary>
        public static readonly WeaponSpec Plasma = new WeaponSpec { RangeKm = 22, Damage = 1800, Cooldown = 40, Accuracy = 0.8, FalloffEdgeShare = 0.35 };

        // ------------------------------------------------------------ settings

        static string Km(double v) => v.ToString("0", CultureInfo.InvariantCulture) + " km";
        static string Hp(double v) => v.ToString("0", CultureInfo.InvariantCulture) + " hp";
        static string Secs(double v) => v.ToString("0", CultureInfo.InvariantCulture) + " s";
        static string Percent(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        public static readonly RangeSetting RailgunRange = new RangeSetting("spinal.railgunRange", SettingGroups.Broadsides,
            "Spinal railgun range", 50, 25, 54, 1,
            "How far a battleship's spinal railgun reaches (a destroyer's turret railgun reaches 30 km).", Km,
            "spinal railgun dreadnought battleship range sniper").Applying(v => Railgun.RangeKm = v);

        public static readonly RangeSetting RailgunDamage = new RangeSetting("spinal.railgunDamage", SettingGroups.Broadsides,
            "Spinal railgun damage", 900, 200, 2000, 25,
            "What one spinal railgun slug does to what it strikes.", Hp,
            "spinal railgun dreadnought battleship damage slug").Applying(v => Railgun.Damage = v);

        public static readonly RangeSetting RailgunRecharge = new RangeSetting("spinal.railgunRecharge", SettingGroups.Broadsides,
            "Spinal railgun recharge", 30, 8, 120, 1,
            "Seconds a spinal railgun takes to charge its rails again after a shot.", Secs,
            "spinal railgun dreadnought battleship recharge cooldown").Applying(v => Railgun.Cooldown = v);

        public static readonly RangeSetting CoilgunRange = new RangeSetting("spinal.coilgunRange", SettingGroups.Broadsides,
            "Spinal coilgun range", 44, 20, 54, 1,
            "How far a battleship's spinal coilgun reaches.", Km,
            "spinal coilgun dreadnought battleship range sniper").Applying(v => Coilgun.RangeKm = v);

        public static readonly RangeSetting CoilgunDamage = new RangeSetting("spinal.coilgunDamage", SettingGroups.Broadsides,
            "Spinal coilgun damage", 520, 100, 1500, 20,
            "What one spinal coilgun slug does to what it strikes.", Hp,
            "spinal coilgun dreadnought battleship damage slug").Applying(v => Coilgun.Damage = v);

        public static readonly RangeSetting CoilgunRecharge = new RangeSetting("spinal.coilgunRecharge", SettingGroups.Broadsides,
            "Spinal coilgun recharge", 15, 4, 60, 1,
            "Seconds a spinal coilgun takes to charge its coils again after a shot.", Secs,
            "spinal coilgun dreadnought battleship recharge cooldown").Applying(v => Coilgun.Cooldown = v);

        public static readonly RangeSetting PlasmaRange = new RangeSetting("spinal.plasmaRange", SettingGroups.Broadsides,
            "Plasma spinal cannon range", 22, 8, 40, 1,
            "How far a plasma spinal cannon's bolt holds together.", Km,
            "plasma spinal cannon dreadnought battleship range").Applying(v => Plasma.RangeKm = v);

        public static readonly RangeSetting PlasmaDamage = new RangeSetting("spinal.plasmaDamage", SettingGroups.Broadsides,
            "Plasma spinal cannon damage", 1800, 400, 4000, 50,
            "What a plasma spinal cannon's bolt does at point-blank range, before it bleeds off over distance.", Hp,
            "plasma spinal cannon dreadnought battleship damage bolt").Applying(v => Plasma.Damage = v);

        public static readonly RangeSetting PlasmaRecharge = new RangeSetting("spinal.plasmaRecharge", SettingGroups.Broadsides,
            "Plasma spinal cannon recharge", 40, 10, 120, 1,
            "Seconds a plasma spinal cannon takes to fill its magnetic bottle again after a shot.", Secs,
            "plasma spinal cannon dreadnought battleship recharge cooldown").Applying(v => Plasma.Cooldown = v);

        public static readonly RangeSetting Unpowered = new RangeSetting("spinal.unpowered", SettingGroups.Broadsides,
            "Spinal mount without its plant", 0.25, 0, 1, 0.05,
            "How fast a spinal mount recharges, against its full rate, when the hull has fewer spinal power plants than spinal " +
            "mounts (100% lets it charge as fast without one).", Percent,
            "spinal mount power plant reactor recharge dreadnought battleship");

        // ------------------------------------------------------------ the mount

        /// <summary>How far off the keel a spinal mount's narrow cone reaches, deg (U.SPC-T533): it cannot traverse, so the hull
        /// lays it.</summary>
        public const double ConeHalfDeg = 10;

        /// <summary>A spinal mount is a gun the length of the hull, built heavier than any turret: its hardpoint takes this many
        /// times a turret's damage before it is shot away.</summary>
        public const double HpFactor = 1.5;

        /// <summary>One weapon that goes in a spinal mount: the hardpoint it becomes, what it fires there and what the yard builds.</summary>
        public sealed class Kind
        {
            public readonly ModuleKind Module;
            public readonly SubsystemKind Subsystem;

            /// <summary>What it fires as a spinal mount (read when the mount is built, so it follows the settings).</summary>
            public readonly Func<WeaponSpec> Weapon;

            /// <summary>What a yard builds: the ship-length version of the module, its bill and mass.</summary>
            public readonly IPartSpec Part;

            public Kind(ModuleKind module, SubsystemKind subsystem, Func<WeaponSpec> weapon, IPartSpec part)
            {
                Module = module;
                Subsystem = subsystem;
                Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
                Part = part ?? ModuleSpec.Of(module);
            }
        }

        /// <summary>A module built to a spinal mount's length: its name, and its bill and mass that many times over.</summary>
        sealed class SpinalPart : IPartSpec
        {
            readonly ModuleSpec module;
            readonly double times;

            public SpinalPart(ModuleKind m, string name, double times)
            {
                module = ModuleSpec.Of(m);
                Name = name;
                this.times = times;
            }

            public string Name { get; }
            public (Good good, double tonnes)[] Bill => module.Bill;
            public double Tonnes(int i, double scale) => module.Tonnes(i, scale) * times;
            public double Mass(double scale) => module.Mass(scale) * times;
        }

        static readonly Dictionary<ModuleKind, Kind> kinds = new Dictionary<ModuleKind, Kind>();

        /// <summary>The hulls built around spinal mounts, the only ones that may fit one or a spinal power plant. A new
        /// dreadnought class joins by being added here.</summary>
        public static readonly HashSet<ShipClass> Classes = new HashSet<ShipClass> { ShipClass.Battleship };

        static SpinalMounts()
        {
            // A railgun's rails and a plasma cannon's bottle are four times the turret's; the coilgun is only ever spinal, and a
            // tachyon lance is a spinal weapon already, so their own modules are what is built.
            Register(new Kind(ModuleKind.Railgun, SubsystemKind.Railgun, () => Railgun, new SpinalPart(ModuleKind.Railgun, "Spinal railgun", 4)));
            Register(new Kind(ModuleKind.Coilgun, SubsystemKind.Coilgun, () => Coilgun, null));
            Register(new Kind(ModuleKind.TachyonLance, SubsystemKind.TachyonLance, () => TachyonLances.Spec, null));
            Register(new Kind(ModuleKind.PlasmaCannon, SubsystemKind.PlasmaCannon, () => Plasma, new SpinalPart(ModuleKind.PlasmaCannon, "Plasma spinal cannon", 4)));
        }

        /// <summary>Adds (or replaces) a weapon that a spinal mount takes.</summary>
        public static void Register(Kind kind)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            kinds[kind.Module] = kind;
        }

        /// <summary>The spinal version of <paramref name="m"/>, or null if a spinal mount does not take it.</summary>
        public static Kind Of(ModuleKind m) => kinds.TryGetValue(m, out var k) ? k : null;

        /// <summary>Whether a spinal mount takes <paramref name="m"/>.</summary>
        public static bool Takes(ModuleKind m) => kinds.ContainsKey(m);

        /// <summary>Whether a hull of <paramref name="cls"/> is built around spinal mounts.</summary>
        public static bool CanCarry(ShipClass? cls) => cls is ShipClass c && Classes.Contains(c);

        /// <summary>What the yard builds for <paramref name="m"/> in a spinal mount.</summary>
        public static IPartSpec Part(ModuleKind m) => Of(m)?.Part ?? ModuleSpec.Of(m);

        /// <summary>A spinal mount of <paramref name="m"/> with its muzzle at (x, y, z) ship lengths: down the bow, as long as
        /// the hull, laid by the hull.</summary>
        public static SubsystemDef Mount(ModuleKind m, double x, double y, double z)
        {
            var k = Of(m) ?? throw new ArgumentException(m + " does not go in a spinal mount", nameof(m));
            var def = new SubsystemDef(k.Subsystem, x, y, z, false, FireCones.Bow, ConeHalfDeg) { Spinal = true, Weapon = k.Weapon() };
            def.MaxHp *= HpFactor;
            return def;
        }

        /// <summary>The spinal mount of <paramref name="m"/> in the place of <paramref name="was"/> (a refit).</summary>
        public static SubsystemDef Mount(ModuleKind m, SubsystemDef was) => Mount(m, was.X, was.Y, was.Z);

        // ------------------------------------------------------------ in battle

        /// <summary>A gun that fires a slug down rails or coils (the turret and fighter railguns and the spinal guns): its round
        /// draws as a railgun's, and the hull lays it within the railgun's gimbal.</summary>
        public static bool IsSlug(SubsystemKind k) => k == SubsystemKind.Railgun || k == SubsystemKind.LightRailgun || k == SubsystemKind.Coilgun;

        /// <summary>A gun the hull itself lays (it swings only within the railgun's narrow gimbal): a railgun, a coilgun, a
        /// tachyon lance or any spinal mount.</summary>
        public static bool HullLaid(SubsystemDef d) =>
            d != null && (d.Spinal || d.Kind == SubsystemKind.Railgun || d.Kind == SubsystemKind.Coilgun || d.Kind == SubsystemKind.TachyonLance || (d.Weapon?.Lance ?? false));

        /// <summary>The spinal mounts on the hull still standing.</summary>
        public static int Standing(ShipCombat c)
        {
            if (c == null) return 0;
            int n = 0;
            foreach (var s in c.Subsystems)
                if (s.Def.Spinal && !s.Destroyed) n++;
            return n;
        }

        /// <summary>
        /// How fast <paramref name="s"/> recharges against its full rate: 1 for anything but a spinal mount, and for a spinal
        /// mount while the hull has a spinal power plant for every standing mount; with fewer, the plants' share of the mounts,
        /// never below <see cref="Unpowered"/>.
        /// </summary>
        public static double ChargeRate(ShipCombat c, Subsystem s)
        {
            if (c == null || s == null || !s.Def.Spinal) return 1;
            int mounts = Standing(c);
            double plants = Math.Max(0, c.Spec.SpinalPlants);
            if (mounts <= 0 || plants >= mounts) return 1;
            return Math.Max(Unpowered.Value, plants / mounts);
        }

        /// <summary>The reach of the shortest-ranged spinal mount on the hull still standing, km (0 if none): how far off the
        /// hull can stand and still have every spinal mount bear.</summary>
        public static double ReachKm(ShipCombat c)
        {
            if (c == null) return 0;
            double reach = double.MaxValue;
            foreach (var s in c.Subsystems)
                if (s.Def.Spinal && !s.Destroyed && s.Def.Weapon != null) reach = Math.Min(reach, s.Def.Weapon.RangeKm);
            return reach == double.MaxValue ? 0 : reach;
        }
    }
}
