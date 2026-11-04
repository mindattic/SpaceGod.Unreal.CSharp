using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The tachyon lance (U.SPC-T487, docs/SHIP_ROSTER.md "Weapons by tier"): a capital ship's spinal weapon whose
    /// shot crosses its whole reach at once, so there is no travel time and no lead to lay. It ignores light-speed
    /// delay, and it outranges every other gun. That would change the whole doctrine, so it is held in check: only a
    /// capital hull can carry one, in a bow mount the hull itself must lay on the target (a narrow spinal cone,
    /// <see cref="ConeHalfDeg"/>); it is dear to build and only the greatest yards can build it
    /// (<see cref="YardBuilds"/>); it recharges slowly; and while it charges it draws on the reactor so hard that the
    /// hull's shields recover more slowly (<see cref="ShieldRegenFactor"/>). Kinetics and missiles stay the weapons
    /// of the line. Its tunables are declared settings.
    /// </summary>
    [SettingsSource]
    public static class TachyonLances
    {
        // ------------------------------------------------------------ settings

        public static readonly RangeSetting Range = new RangeSetting("lance.range", SettingGroups.Broadsides,
            "Tachyon lance range", 60, 30, 120, 5,
            "How far a tachyon lance reaches, km. Its shot lands the instant it is fired, anywhere in that reach.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " km", "tachyon lance spinal capital range weapon")
            .Applying(v => WeaponSpec.TachyonLance.RangeKm = v);

        public static readonly RangeSetting Damage = new RangeSetting("lance.damage", SettingGroups.Broadsides,
            "Tachyon lance damage", 650, 200, 1500, 50,
            "What one tachyon lance shot does to what it strikes, hit points (a railgun slug does 260, a capital torpedo 900).",
            v => v.ToString("0", CultureInfo.InvariantCulture), "tachyon lance spinal capital damage weapon")
            .Applying(v => WeaponSpec.TachyonLance.Damage = v);

        public static readonly RangeSetting Recharge = new RangeSetting("lance.recharge", SettingGroups.Broadsides,
            "Tachyon lance recharge", 60, 15, 180, 5,
            "Seconds a tachyon lance takes to charge again after a shot.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " s", "tachyon lance spinal capital recharge cooldown power")
            .Applying(v => WeaponSpec.TachyonLance.Cooldown = v);

        public static readonly RangeSetting PowerDraw = new RangeSetting("lance.powerDraw", SettingGroups.Broadsides,
            "Tachyon lance power draw", 0.5, 0, 1, 0.05,
            "How much of the hull's shield recovery a charging tachyon lance takes from the reactor (0 lets it charge for nothing).",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "tachyon lance power reactor shield regen capital");

        public static readonly RangeSetting YardLevel = new RangeSetting("lance.yardLevel", SettingGroups.Broadsides,
            "Tachyon lance works", 2, 0, 4, 0.25,
            "How great a shipyard must be to build a tachyon lance: only yards at least this size (Earth's, Ganymede's and Titan's are 2) can.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture), "tachyon lance yard shipyard rare build");

        // ------------------------------------------------------------ rules

        /// <summary>The lance's weapon, at the settings' figures.</summary>
        public static WeaponSpec Spec => WeaponSpec.TachyonLance;

        /// <summary>How far off the keel a lance's narrow cone reaches, deg (U.SPC-T533): spinal, so the hull lays it.</summary>
        public const double ConeHalfDeg = 10;

        /// <summary>The hulls that can carry a lance: the capital ships, the only ones with the power plant to feed it. A new
        /// capital class joins by being added here.</summary>
        public static readonly HashSet<ShipClass> CapitalClasses = new HashSet<ShipClass> { ShipClass.Battleship, ShipClass.Carrier };

        /// <summary>Whether a hull of <paramref name="cls"/> can carry a lance (a station or satellite, with no class, cannot).</summary>
        public static bool CanCarry(ShipClass? cls) => cls is ShipClass c && CapitalClasses.Contains(c);

        /// <summary>Whether <paramref name="yard"/> can build a lance: a lawful shipyard (not a raider haven's black-market
        /// slips) of at least <see cref="YardLevel"/>.</summary>
        public static bool YardBuilds(Settlement yard)
        {
            if (yard == null || yard.Fallen || yard.Kind == SettlementKind.Haven) return false;
            if (yard.Owner == null || yard.Owner.IsPirate) return false;
            var works = yard.Shipyard;
            return works != null && works.Level + 1e-9 >= YardLevel.Value;
        }

        /// <summary>The share of its shield recovery a hull keeps: less <see cref="PowerDraw"/> while any lance aboard is
        /// charging (a lance shot away draws nothing).</summary>
        public static double ShieldRegenFactor(ShipCombat c)
        {
            if (c == null) return 1;
            foreach (var s in c.Subsystems)
                if (s.Kind == SubsystemKind.TachyonLance && !s.Destroyed && s.Cooldown > 0) return 1 - PowerDraw.Value;
            return 1;
        }
    }
}
