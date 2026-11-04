using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Radiators and heat on big ships (U.SPC-T491). A capital ship's reactor, engines and guns build heat, and it sheds it
    /// through radiator panels mounted out on its hull (<see cref="SubsystemKind.Radiator"/>), with a little more through
    /// the hull's own skin. With its radiators whole a ship sheds everything it can make, fighting flat out under full
    /// burn, so heat is never a worry for it. The panels are thin and spread out to the sky, though: a weak point, and
    /// gunners go for them, the more so on a hull already running hot. A ship that has lost enough of them builds heat
    /// (<see cref="BattleUnit.HullHeat"/>, 1 at its sinks' limit); past <see cref="ThrottleAt"/> its crew throttles its guns'
    /// reloading and its drive (<see cref="Throttle"/>) to hold it, and a ship that runs hot instead (one running for its
    /// life, or a player who orders it, <see cref="BroadsideCommand.RunHot"/>) goes past the limit, where its crew and
    /// systems cook: hands lost and its guns, drives and shield generators burned out.
    ///
    /// Which hulls carry panels, and where, is data (<see cref="Register"/>): a class with none (destroyers and
    /// everything smaller) sheds what it makes through its skin and has no heat to manage. Every figure is a setting in
    /// Settings > Broadsides.
    /// </summary>
    [SettingsSource]
    public static class Radiators
    {
        // ------------------------------------------------------------ settings

        public static readonly RangeSetting RadiatorShed = new RangeSetting("heat.radiators", SettingGroups.Broadsides,
            "Radiator capacity", 1.0, 0.25, 3, 0.05,
            "How much heat a big ship's radiators shed with every panel whole, as a share of what its reactor, guns and drive make together at full stretch (they make 1.05, the hull's skin sheds the rest).",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "heat radiator capital cooling thermal");

        public static readonly RangeSetting SkinShed = new RangeSetting("heat.skin", SettingGroups.Broadsides,
            "Hull skin cooling", 0.3, 0, 1, 0.05,
            "How much heat a big ship sheds through its own hull with no radiators at all, on the same scale.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "heat radiator capital cooling hull skin thermal");

        public static readonly RangeSetting WeaponHeat = new RangeSetting("heat.weapons", SettingGroups.Broadsides,
            "Heat from the guns", 0.6, 0, 2, 0.05,
            "How much heat a big ship's guns and launchers make with every one of them reloading at once.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "heat weapons guns capital thermal");

        public static readonly RangeSetting EngineHeat = new RangeSetting("heat.engines", SettingGroups.Broadsides,
            "Heat from the drive", 0.35, 0, 2, 0.05,
            "How much heat a big ship's drive makes at its full battle speed.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "heat engines drive capital thermal");

        public static readonly RangeSetting SinkSeconds = new RangeSetting("heat.sinks", SettingGroups.Broadsides,
            "Heat sinks", 90, 15, 300, 5,
            "How long a big ship's heat sinks hold heat it cannot shed: seconds of making a full share more than it sheds before it reaches their limit.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " s", "heat sinks capital thermal");

        public static readonly RangeSetting ThrottleAt = new RangeSetting("heat.throttleAt", SettingGroups.Broadsides,
            "Throttle from", 0.6, 0.2, 1, 0.05,
            "How full a big ship's heat sinks get before its crew starts throttling its guns and drive to hold the heat.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "heat throttle capital thermal");

        public static readonly RangeSetting MinThrottle = new RangeSetting("heat.minThrottle", SettingGroups.Broadsides,
            "Throttled to", 0.15, 0, 1, 0.05,
            "How far the crew throttles the guns' reloading and the drive with the heat sinks at their limit (as designed, far enough to hold a hull with no radiators left short of it).",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "heat throttle capital thermal");

        public static readonly RangeSetting CrewLoss = new RangeSetting("heat.crewLoss", SettingGroups.Broadsides,
            "Heat casualties", 0.004, 0, 0.03, 0.001,
            "The share of its crew a big ship loses each second past its heat sinks' limit (more the further past it goes).",
            v => (v * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%/s", "heat crew casualties capital thermal");

        public static readonly RangeSetting SystemBurn = new RangeSetting("heat.systemBurn", SettingGroups.Broadsides,
            "Heat damage to systems", 0.01, 0, 0.05, 0.001,
            "The share of its toughness each gun, drive and shield generator loses each second a big ship runs past its heat sinks' limit (more the further past it goes).",
            v => (v * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%/s", "heat damage systems capital thermal");

        public static readonly ToggleSetting AiFleesHot = new ToggleSetting("heat.aiFleesHot", SettingGroups.Broadsides,
            "AI runs hot to escape", true,
            "An AI captain running from a fight keeps its drive at full however hot the hull gets, and takes what the heat does to it.",
            "heat flee run hot capital thermal");

        // ------------------------------------------------------------ rules

        /// <summary>A panel's toughness: a thin sheet out in the open, a fraction of a gun mount's (a heavy laser's hit
        /// nearly takes one away).</summary>
        public const double PanelHp = 140;

        /// <summary>What the reactor makes at idle, on the same scale as the settings' figures.</summary>
        public const double ReactorHeat = 0.1;

        /// <summary>The furthest past the limit the heat goes: past this it is all burning out what makes it.</summary>
        public const double MaxHeat = 1.5;

        /// <summary>How much more gunners favour a panel on a hull running at its limit than on a cold one.</summary>
        public const double HotTargetBonus = 2;

        static readonly Dictionary<ShipClass, SubsystemDef[]> Panels = new Dictionary<ShipClass, SubsystemDef[]>
        {
            // Two pairs of wings off the flanks abaft the island, clear of the flight deck.
            [ShipClass.Carrier] = Pairs(0.15, 0.0, -0.12, -0.24),
            // Two pairs off the flanks between the beam batteries and the drive.
            [ShipClass.Battleship] = Pairs(0.12, 0.0, -0.14, -0.33),
            [ShipClass.CommandCruiser] = Pairs(0.12, 0.0, -0.1, -0.26),
            // Two pairs off the flanks abaft the beam batteries, the plasma bottles' and the drive's heat (U.SPC-T478).
            [ShipClass.GateBreaker] = Pairs(0.12, -0.02, -0.28, -0.38),
            // One pair amidships forward of the engine nacelles, one pair abaft them.
            [ShipClass.Cruiser] = Pairs(0.14, 0.0, 0.12, -0.36),
        };

        /// <summary>Port and starboard panels at each of <paramref name="zs"/>, <paramref name="x"/> out from the keel.</summary>
        static SubsystemDef[] Pairs(double x, double y, params double[] zs)
        {
            var list = new List<SubsystemDef>();
            foreach (var z in zs)
            {
                list.Add(new SubsystemDef(SubsystemKind.Radiator, x, y, z));
                list.Add(new SubsystemDef(SubsystemKind.Radiator, -x, y, z));
            }
            return list.ToArray();
        }

        /// <summary>The radiator panels a class is designed with (none for a class that sheds through its skin).</summary>
        public static IReadOnlyList<SubsystemDef> PanelsFor(ShipClass c) => Panels.TryGetValue(c, out var p) ? p : Array.Empty<SubsystemDef>();

        /// <summary>Sets (or, with null, removes) a class's radiator panels. A design built after this carries them;
        /// <see cref="CombatSpec"/> fits the stock designs' when it first builds them.</summary>
        public static void Register(ShipClass c, SubsystemDef[] panels)
        {
            if (panels == null) Panels.Remove(c);
            else Panels[c] = panels;
        }

        /// <summary><paramref name="layout"/> with <paramref name="c"/>'s panels added after its own hardpoints (unchanged if it
        /// has them already, or the class has none).</summary>
        public static SubsystemDef[] Fit(ShipClass c, SubsystemDef[] layout)
        {
            var panels = PanelsFor(c);
            if (panels.Count == 0 || layout == null) return layout;
            foreach (var d in layout) if (d.Kind == SubsystemKind.Radiator) return layout;
            var all = new SubsystemDef[layout.Length + panels.Count];
            layout.CopyTo(all, 0);
            for (int i = 0; i < panels.Count; i++) all[layout.Length + i] = panels[i];
            return all;
        }

        /// <summary>Whether the hull manages heat at all: one built with radiator panels.</summary>
        public static bool Carries(ShipCombat c)
        {
            if (c == null) return false;
            foreach (var s in c.Subsystems) if (s.Kind == SubsystemKind.Radiator) return true;
            return false;
        }

        /// <summary>The share of its panels' cooling the hull still has (by their grades), 1 for a hull with none.</summary>
        public static double Intact(ShipCombat c)
        {
            double total = 0, alive = 0;
            if (c != null)
                foreach (var s in c.Subsystems)
                {
                    if (s.Kind != SubsystemKind.Radiator) continue;
                    total += s.Def.Rating;
                    if (!s.Destroyed) alive += s.Def.Rating;
                }
            return total <= 0 ? 1 : alive / total;
        }

        /// <summary>Heat the hull sheds a second, on the settings' scale: its skin's and its working panels'.</summary>
        public static double Shed(ShipCombat c) => SkinShed.Value + RadiatorShed.Value * Intact(c);

        /// <summary>
        /// Heat the hull makes a second, on the settings' scale: the reactor's, its guns' and launchers' (each one
        /// reloading makes its share by firepower, scaled by how fast it is reloading, <paramref name="reload"/>), and its
        /// drive's (by how near its full battle speed it is going).
        /// </summary>
        public static double Made(BattleUnit u, double reload)
        {
            var c = u.Combat;
            double all = 0, busy = 0;
            foreach (var s in c.Subsystems)
            {
                var w = s.Def.Weapon;
                if (w == null || w.Supporting || w.Cooldown <= 0) continue;
                double dps = w.Damage * Math.Max(1, w.Salvo) / w.Cooldown * (w.AntiSmall ? 0.3 : 1);
                all += dps;
                if (!s.Destroyed && s.Cooldown > 0) busy += dps;
            }
            double guns = all > 0 ? WeaponHeat.Value * busy / all * Math.Max(0, reload) : 0;
            double top = c.Spec.MaxSpeed;
            double drive = top > 0 ? EngineHeat.Value * Math.Min(1, u.Vel.Length / top) : 0;
            return ReactorHeat + guns + drive;
        }

        /// <summary>Whether the hull keeps its guns and drive at full however hot it gets: ordered to
        /// (<see cref="BroadsideCommand.RunHot"/>), or an AI captain running for its life (<see cref="AiFleesHot"/>).</summary>
        public static bool RunsHot(BattleUnit u) =>
            u != null && (u.Broadside?.RunHot == true || (!u.PlayerControlled && u.Fleeing && AiFleesHot.Value));

        /// <summary>How hard the crew lets the guns reload and the drive push at the hull's heat: 1 below
        /// <see cref="ThrottleAt"/>, down to <see cref="MinThrottle"/> at the sinks' limit; 1 always for a hull running hot
        /// or one with no radiators to manage.</summary>
        public static double Throttle(BattleUnit u)
        {
            if (u == null || u.HullHeat <= 0 || RunsHot(u)) return 1;
            double from = ThrottleAt.Value;
            if (u.HullHeat <= from) return 1;
            double f = Math.Min(1, (u.HullHeat - from) / Math.Max(1e-6, 1 - from));
            return 1 - f * (1 - MinThrottle.Value);
        }

        /// <summary>
        /// One battle step of the hull's heat: it fills by what is made less what is shed, over the sinks' size, and empties
        /// the same way. Past the limit the crew loses hands and every working gun, drive and shield generator burns
        /// (those burned out are added to <paramref name="burnedOut"/>, if given). <paramref name="reload"/> is how fast the
        /// guns are reloading this step before the heat's own throttle. Nothing for a hull without radiators.
        /// </summary>
        public static void Step(BattleUnit u, double dt, double reload = 1, List<Subsystem> burnedOut = null)
        {
            var c = u.Combat;
            if (dt <= 0 || !Carries(c)) return;
            double net = Made(u, reload * Throttle(u)) - Shed(c);
            u.HullHeat = Math.Max(0, Math.Min(MaxHeat, u.HullHeat + net * dt / Math.Max(1, SinkSeconds.Value)));
            double over = u.HullHeat - 1;
            if (over <= 0) return;
            // The further past the limit, the worse: twice as bad a quarter past it.
            double severity = 1 + over * 4;
            double lost = c.Interior.TakeCrew(c.Interior.Crew * Math.Min(1, CrewLoss.Value * severity * dt));
            c.Interior.CrewLost += lost;
            double burn = SystemBurn.Value * severity * dt;
            if (burn <= 0) return;
            foreach (var s in c.Subsystems)
            {
                if (s.Destroyed || !Cooks(s)) continue;
                s.Hp -= s.Def.MaxHp * burn;
                if (s.Hp > 0) continue;
                s.Hp = 0;
                if (s.Kind == SubsystemKind.ShieldGenerator) s.Shield = 0;
                burnedOut?.Add(s);
            }
        }

        /// <summary>What heat burns out: the guns, drives and shield generators it pours through. Never the reactor (its
        /// own safeties scram it short of that), the bridge or the panels themselves.</summary>
        static bool Cooks(Subsystem s) =>
            s.Kind == SubsystemKind.Engine || s.Kind == SubsystemKind.ShieldGenerator || (s.Def.Weapon != null && !s.Def.Weapon.Supporting);

        /// <summary>How much gunners want a panel on <paramref name="target"/>: some, and more as the hull runs hotter (it
        /// shows, and they know what it means).</summary>
        public static double TargetWeight(BattleUnit target) => 1 + HotTargetBonus * Math.Min(1, Math.Max(0, target?.HullHeat ?? 0));
    }
}
