using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// One stock design's place for a role weapon (U.SPC-T505, docs/SHIP_ROSTER.md "Weapons by tier"): the class it is for,
    /// the weapon, and the stock part it goes in place of on that design, up to <see cref="Count"/> of them. A hull built to
    /// the pattern carries the weapon there from the slips; a warship refitting at its own yard may take it there later.
    /// </summary>
    public sealed class WeaponPattern
    {
        public readonly string Name;
        public readonly ShipClass Class;
        public readonly ModuleKind Weapon;

        /// <summary>The stock part the weapon goes in place of.</summary>
        public readonly ModuleKind Replaces;

        /// <summary>How many of those parts it takes the place of (the first ones in the design's layout).</summary>
        public readonly int Count;

        public WeaponPattern(string name, ShipClass cls, ModuleKind weapon, ModuleKind replaces, int count)
        {
            Name = name;
            Class = cls;
            Weapon = weapon;
            Replaces = replaces;
            Count = Math.Max(1, count);
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// How the navies and raiders fit the role weapons (U.SPC-T505): phaser arrays on destroyers and cruisers, plasma cannons
    /// on the brawlers, the tachyon lance on a rare capital. (Antimatter torpedoes on frigates and capitals, and flak and
    /// interceptor missiles on destroyers, are already in those stock designs, U.SPC-T489.) Each weapon has a place on its
    /// classes' stock designs (<see cref="Patterns"/>), and every new hull a navy or a raider band lays down is built to
    /// those patterns, or not, by three things: the owner's doctrine (its build plan, and each navy's own leaning, so two
    /// navies under the same plan still differ, <see cref="Leaning"/>), its wealth (it must hold <see cref="Wealth"/> times
    /// what the weapons add to the hull's price) and what the yard can build (only the greatest lawful yards build a lance).
    /// A navy that favours a weapon also refits its older hulls with it at its own yards (<see cref="Wishes"/>). Every
    /// figure is a setting in Settings &gt; AI.
    /// </summary>
    [SettingsSource]
    public static class FleetPatterns
    {
        // ------------------------------------------------------------ settings

        static string Times(double v) => v.ToString("0", CultureInfo.InvariantCulture) + "x";
        static string Percent(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        public static readonly RangeSetting Wealth = new RangeSetting("patterns.wealth", SettingGroups.Ai,
            "Role weapons wealth", 25, 0, 200, 5,
            "How many times what its role weapons add to a new hull's price a navy or raider band must hold before it builds the hull to its pattern (phaser arrays, plasma cannons, a tachyon lance); a poorer one lays down the plain stock design.",
            Times, "phaser plasma tachyon lance pattern doctrine wealth navy raider fit new hull");

        public static readonly RangeSetting Spread = new RangeSetting("patterns.spread", SettingGroups.Ai,
            "Navy weapon leanings", 0.5, 0, 1, 0.05,
            "How far each navy's own taste pulls its liking for each role weapon from its doctrine's either way, so two navies under the same build plan still field different mixes (0 makes every navy under a plan alike).",
            Percent, "phaser plasma tachyon lance doctrine faction leaning mix navy");

        public static readonly RangeSetting LanceShare = new RangeSetting("patterns.lanceShare", SettingGroups.Ai,
            "Lance capitals", 0.25, 0, 1, 0.05,
            "The most of the capitals a navy lays down at a yard able to build one that carry a tachyon lance (a navy whose doctrine leans wholly to the lance); the lance stays rare.",
            Percent, "tachyon lance capital rare doctrine navy");

        // ------------------------------------------------------------ the patterns

        static readonly List<WeaponPattern> patterns = new List<WeaponPattern>
        {
            // A destroyer's and a cruiser's point-defence mounts take a phaser array: swept wide it is their point defence,
            // laid narrow it burns hulls at medium range (U.SPC-T488).
            new WeaponPattern("Phaser destroyer", ShipClass.Destroyer, ModuleKind.PhaserArray, ModuleKind.PointDefense, 2),
            new WeaponPattern("Phaser cruiser", ShipClass.Cruiser, ModuleKind.PhaserArray, ModuleKind.PointDefense, 2),
            // A brawling battleship's two bow heavy lasers become plasma cannons, for whatever closes on it.
            new WeaponPattern("Plasma battleship", ShipClass.Battleship, ModuleKind.PlasmaCannon, ModuleKind.HeavyLaser, 2),
            // The rare lance capital: its spinal coilgun becomes a tachyon lance (U.SPC-T487, U.SPC-T490).
            new WeaponPattern("Lance battleship", ShipClass.Battleship, ModuleKind.TachyonLance, ModuleKind.Coilgun, 1),
        };

        public static IReadOnlyList<WeaponPattern> Patterns => patterns;

        /// <summary>Adds a pattern (a new class taking up a weapon's role, the gate breaker's plasma cannons once it is a
        /// class), replacing any with the same name.</summary>
        public static void Register(WeaponPattern p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            patterns.RemoveAll(x => x.Name == p.Name);
            patterns.Add(p);
        }

        /// <summary>The patterns for hulls of <paramref name="cls"/>.</summary>
        public static IEnumerable<WeaponPattern> For(ShipClass cls)
        {
            foreach (var p in patterns)
                if (p.Class == cls) yield return p;
        }

        // ------------------------------------------------------------ doctrine

        /// <summary>
        /// How much each build plan's doctrine likes each role weapon, 0 to 1: a battle line its plasma cannons and lances, a
        /// carrier navy and its screens the phaser arrays that sweep fighters and missiles away. A plan not listed likes
        /// them as the balanced fleet does.
        /// </summary>
        static readonly Dictionary<string, Dictionary<ModuleKind, double>> doctrine = new Dictionary<string, Dictionary<ModuleKind, double>>();

        /// <summary>The raiders' own taste: phaser arrays when they can pay for them; no lance, which no haven builds.</summary>
        static readonly Dictionary<ModuleKind, double> raiders = new Dictionary<ModuleKind, double>
        {
            [ModuleKind.PhaserArray] = 0.45, [ModuleKind.PlasmaCannon] = 0.6, [ModuleKind.TachyonLance] = 0,
        };

        static FleetPatterns()
        {
            Lean(BuildPlan.Balanced, 0.5, 0.45, 0.45);
            Lean(BuildPlan.Mobilise, 0.5, 0.45, 0.45);
            Lean(BuildPlan.BattleLine, 0.35, 0.8, 0.8);
            Lean(BuildPlan.Carriers, 0.75, 0.25, 0.3);
            Lean(BuildPlan.Strike, 0.6, 0.3, 0.2);
            Lean(BuildPlan.Screens, 0.7, 0.3, 0.2);
            Lean(BuildPlan.LaneSecurity, 0.65, 0.3, 0.2);
        }

        /// <summary>Sets how much <paramref name="plan"/>'s doctrine likes phaser arrays, plasma cannons and the lance.</summary>
        public static void Lean(BuildPlan plan, double phasers, double plasma, double lance)
        {
            if (plan == null) return;
            doctrine[plan.Name] = new Dictionary<ModuleKind, double>
            {
                [ModuleKind.PhaserArray] = phasers, [ModuleKind.PlasmaCannon] = plasma, [ModuleKind.TachyonLance] = lance,
            };
        }

        /// <summary>
        /// How much <paramref name="f"/> likes <paramref name="weapon"/>, 0 to 1: its doctrine's liking (its build plan's,
        /// <paramref name="plan"/>, the balanced fleet's if none; the raiders' own if it is a raider), moved either way by up to
        /// half of <see cref="Spread"/> by the navy's own taste, which is fixed by its name, so the same navy always leans the
        /// same way.
        /// </summary>
        public static double Leaning(Faction f, ModuleKind weapon, BuildPlan plan)
        {
            if (f == null) return 0;
            Dictionary<ModuleKind, double> likes;
            if (f.IsPirate) likes = raiders;
            else if (!doctrine.TryGetValue((plan ?? BuildPlan.Balanced).Name, out likes)) likes = doctrine[BuildPlan.Balanced.Name];
            if (!likes.TryGetValue(weapon, out double like)) return 0;
            if (like <= 0) return 0;
            double taste = (Taste(f.Name, weapon) - 0.5) * Spread.Value;
            return Math.Max(0, Math.Min(1, like + taste));
        }

        /// <summary>A navy's own taste for a weapon, 0 to 1, the same every game (a hash of its name and the weapon).</summary>
        static double Taste(string name, ModuleKind weapon)
        {
            uint h = 2166136261;
            foreach (char c in (name ?? "") + "/" + weapon)
            {
                h ^= c;
                h *= 16777619;
            }
            h ^= h >> 15;
            h *= 2246822519;
            h ^= h >> 13;
            return (h % 10007) / 10006.0;
        }

        /// <summary>The liking at which a navy refits its older hulls with a weapon, and not just builds new ones with it.</summary>
        public const double RefitLeaning = 0.5;

        // ------------------------------------------------------------ fitting

        /// <summary>The hardpoints of <paramref name="l"/> that pattern <paramref name="p"/> puts its weapon on and that still
        /// carry the part it replaces: its places are the first <see cref="WeaponPattern.Count"/> of the design's own mounts
        /// built with that part that take the weapon. Empty if none are left.</summary>
        public static List<int> Slots(Loadout l, WeaponPattern p)
        {
            var list = new List<int>();
            if (l == null || p == null || l.Class != p.Class) return list;
            int places = 0;
            for (int i = 0; i < l.Slots.Count && places < p.Count; i++)
            {
                if (l.Slots[i].Stock != p.Replaces || !Outfitting.CanFit(l, i, p.Weapon)) continue;
                places++;
                if (l.Fitted[i] == p.Replaces) list.Add(i);
            }
            return list;
        }

        /// <summary>What the pattern's weapons add to the hull's price, credits: their materials at base prices less those of
        /// the parts they replace.</summary>
        public static double ExtraCost(Loadout l, IReadOnlyList<int> slots, ModuleKind weapon)
        {
            double cost = 0;
            foreach (int i in slots)
            {
                cost += Worth(l.PartAt(i, weapon), l.Scale);
                if (l.Fitted[i] is ModuleKind was) cost -= Worth(l.PartAt(i, was), l.Scale);
            }
            return Math.Max(0, cost);
        }

        static double Worth(IPartSpec part, double scale)
        {
            double v = 0;
            for (int i = 0; i < part.Bill.Length; i++) v += part.Tonnes(i, scale) * GoodInfo.Of(part.Bill[i].good).BasePrice;
            return v;
        }

        /// <summary>Whether <paramref name="yard"/> can build <paramref name="weapon"/> (a raider band raised with no haven
        /// counts as the haven's black-market slips: anything but a lance).</summary>
        static bool YardBuilds(ModuleKind weapon, Settlement yard) =>
            yard != null ? ModuleSpec.YardBuilds(weapon, yard) : weapon != ModuleKind.TachyonLance;

        /// <summary>
        /// Builds a navy's or raiders' new hull to its owner's patterns as it leaves the slips at <paramref name="yard"/>
        /// (null for a raider band raised with no haven): each pattern for its class is taken with a chance of the owner's
        /// <see cref="Leaning"/> for the weapon (times <see cref="LanceShare"/> for the lance), if the yard can build the
        /// weapon and the owner holds <see cref="Wealth"/> times what it adds to the hull's price, which it pays. Returns the
        /// patterns it was built to.
        /// </summary>
        public static List<WeaponPattern> Commission(Ship ship, Settlement yard, BuildPlan plan, Random rng = null)
        {
            var built = new List<WeaponPattern>();
            if (ship?.Combat == null || ship.Faction == null || ship.Destroyed) return built;
            var f = ship.Faction;
            if (!f.IsMilitary && !f.IsPirate) return built;
            var cls = ship.Spec.Class;
            rng ??= new Random(unchecked((int)((uint)(Taste(f.Name, ModuleKind.LaserTurret) * int.MaxValue) ^ (uint)(ship.Id * 7919 + (int)cls * 104729))));
            Loadout l = null;
            foreach (var p in For(cls))
            {
                double chance = Leaning(f, p.Weapon, plan) * (p.Weapon == ModuleKind.TachyonLance ? LanceShare.Value : 1);
                // The draw is taken whatever follows, so one pattern's outcome never shifts another's.
                if (rng.NextDouble() >= chance) continue;
                if (!YardBuilds(p.Weapon, yard)) continue;
                l ??= Outfitting.LoadoutOf(ship);
                var slots = Slots(l, p);
                if (slots.Count == 0) continue;
                double cost = ExtraCost(l, slots, p.Weapon);
                if (f.Treasury < cost * Wealth.Value) continue;
                foreach (int i in slots) l.Fitted[i] = p.Weapon;
                f.Treasury -= cost;
                built.Add(p);
            }
            // A new hull, whole and loaded: its combat spec is built from the loadout, as any refit's is.
            if (built.Count > 0) ship.Combat = new ShipCombat(l.Build(), cls);
            return built;
        }

        /// <summary>
        /// The role weapons a navy's or raiders' warship wants at its own yard (<see cref="Outfitting.AiRefit"/>): each
        /// pattern for its class whose weapon its owner favours (<see cref="RefitLeaning"/>), on the hardpoints the pattern
        /// puts it. The lance is left to <see cref="Outfitting.AiRefit"/>'s own ordering (<see cref="FavoursLance"/>).
        /// </summary>
        public static IEnumerable<(int slot, ModuleKind module)> Wishes(Ship ship, Loadout l, BuildPlan plan)
        {
            if (ship?.Faction == null || !(l?.Class is ShipClass cls)) yield break;
            foreach (var p in For(cls))
            {
                if (p.Weapon == ModuleKind.TachyonLance || Leaning(ship.Faction, p.Weapon, plan) < RefitLeaning) continue;
                foreach (int i in Slots(l, p)) yield return (i, p.Weapon);
            }
        }

        /// <summary>Whether <paramref name="f"/>'s doctrine favours the lance enough to fit one before its drives are
        /// upgraded, rather than last of all.</summary>
        public static bool FavoursLance(Faction f, BuildPlan plan) => Leaning(f, ModuleKind.TachyonLance, plan) >= RefitLeaning;
    }
}
