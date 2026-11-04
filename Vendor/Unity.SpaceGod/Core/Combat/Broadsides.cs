using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    // The capital-ship broadside game (U.SPC-T425): commanding a capital ship as a naval battle, Black Flag / Rebel Galaxy
    // style. Every gun aims within its own fire cone (U.SPC-T533, see FireCones), so the captain's orders go to the guns that
    // bear where the captain looks, a volley is called and telegraphed, the gun crews go for a weakened patch of the enemy's
    // shields or spread their fire (shields are emitters on the hull, not arcs: U.SPC-T501, see ShieldEmitters), the crew
    // braces for a broadside and damage control teams patch mounts under fire and bring back shield emitters and gun mounts
    // shot away, and the flagship orders its line into formation. AI captains use the same commands by doctrine. There are no
    // bow, starboard, stern and port arcs: everything fights in full 3D, and armour angling is measured against the hull's
    // own surface where each shot strikes it (U.SPC-T534, see ArmourAngling). The handling itself
    // (engine telegraph, steerage, keel, full 3D) is the capital flight model's (U.SPC-T499, docs/FLIGHT_MODELS.md); this
    // file is everything a captain does on top of it.

    /// <summary>What a gun's crew does with a target in its fire cone (U.SPC-T425, U.SPC-T533).</summary>
    public enum BatteryOrder
    {
        /// <summary>Every gun fires as it bears and is loaded (what a crew does on its own).</summary>
        FireAtWill,

        /// <summary>The gun does not fire.</summary>
        HoldFire,

        /// <summary>The gun holds until every gun of the volley bearing on the same hull is loaded, then they fire together: a
        /// broadside, telegraphed to the target while it builds, surer and harder than fire at will.</summary>
        Volley,
    }

    /// <summary>Where a capital ship's damage control teams work first (U.SPC-T425).</summary>
    public enum DamageControlFocus
    {
        /// <summary>The worst-hurt mount first, whatever it is.</summary>
        Balanced,

        Weapons,

        /// <summary>Shield emitters: bringing back those shot away first, and keeping the fields of those under fire charging.</summary>
        Shields,

        Engines,
    }

    /// <summary>The formations a flagship orders its line into (U.SPC-T425).</summary>
    public enum FleetFormation
    {
        /// <summary>No order given: escorts keep loose station as they always have.</summary>
        None,

        /// <summary>A column astern of the flagship, every broadside clear to bear on the beam.</summary>
        LineAhead,

        /// <summary>Abreast of the flagship, alternately to starboard and to port, every bow bearing ahead.</summary>
        LineAbreast,

        /// <summary>Stepped back to starboard: bows and starboard batteries both clear.</summary>
        Echelon,

        /// <summary>A ring around the flagship, to take fire and fighters off it.</summary>
        Screen,

        /// <summary>The line breaks up and each ship fights its own fight.</summary>
        EngageAtWill,
    }

    /// <summary>
    /// A capital ship's standing orders and state in the broadside game (U.SPC-T425), on its <see cref="BattleUnit"/>: the
    /// order each of its guns fires under (given to the guns that bear where the captain looks, U.SPC-T533), whether its gun
    /// crews go for a weakened patch of the enemy's shields, its crew's brace, where damage control works, the formation it
    /// has ordered its line into, and the broadside it is building (its telegraph).
    /// </summary>
    public sealed class BroadsideCommand
    {
        BatteryOrder[] orders = new BatteryOrder[0];

        /// <summary>The order the mount at <paramref name="index"/> in the hull's layout fires under (fire at will unless given another).</summary>
        public BatteryOrder this[int index]
        {
            get => index >= 0 && index < orders.Length ? orders[index] : BatteryOrder.FireAtWill;
            set
            {
                if (index < 0) return;
                if (index >= orders.Length) Array.Resize(ref orders, index + 1);
                orders[index] = value;
            }
        }

        /// <summary>The order <paramref name="s"/> fires under.</summary>
        public BatteryOrder OrderOf(Subsystem s) => this[s.Index];

        /// <summary>The gun crews spread their fire over the enemy's hull instead of going for the patch of it whose shield field
        /// is weakest (U.SPC-T501, see <see cref="ShieldEmitters.FocusWeight"/>): false (focus) unless the captain orders it.</summary>
        public bool SpreadFire;

        /// <summary>Seconds the crew stays braced, and until it can brace again.</summary>
        public double BraceLeft, BraceCooldown;

        public bool Braced => BraceLeft > 0;

        public DamageControlFocus Focus;

        /// <summary>The formation this hull has ordered its line into (it leads it).</summary>
        public FleetFormation Formation;

        /// <summary>The telegraph: the hull a volley is building on and the share of the guns of the volley bearing on it that
        /// are loaded (1 fires it). <see cref="ReadyingOn"/> is null while no volley is building.</summary>
        public BattleUnit ReadyingOn;

        public double ReadyingLoaded;

        /// <summary>Whether a volley is building (the telegraph is up).</summary>
        public bool Readying => ReadyingOn != null;

        /// <summary>Keep the guns and drive at full however hot the hull gets (U.SPC-T491, see <see cref="Radiators"/>): past
        /// the heat sinks' limit the crew and systems take the heat instead of the crew throttling down.</summary>
        public bool RunHot;

        /// <summary>Hit points damage control has put back this battle (for the HUD and the tests).</summary>
        public double Repaired;

        /// <summary>Mounts shot away that the emergency repair crews have brought back this battle (U.SPC-T501).</summary>
        public int Restored;

        /// <summary>The player's order at the helm (U.SPC-T502, see <see cref="WeakPatches"/>): the helm assist rolls the hull
        /// to keep its weak patches out of the heaviest fire (an AI captain does so by doctrine, <see cref="WeakPatches.AiRolls"/>).</summary>
        public bool RollWeakPatches;

        /// <summary>The attitude the captain last settled on to shield its weak patches (U.SPC-T502): the bearing off its bow
        /// it turns the enemy onto, so the guns whose fire cones hold it there bear (U.SPC-T533,
        /// <see cref="WeakPatches.NoBearing"/> for a roll about the bow alone), and how far it is rolled from level about the
        /// line it turns on, deg; and seconds until it weighs the fire again.</summary>
        public int RollBearing = WeakPatches.NoBearing;

        public double RollDeg, RollReconsider;

        /// <summary>Whether <see cref="RollBearing"/> and <see cref="RollDeg"/> hold a choice yet.</summary>
        public bool RollChosen;

        /// <summary>This step: whether the volley on each hull goes now, worked out the first time one of its guns asks.</summary>
        internal readonly Dictionary<BattleUnit, bool> Releasing = new Dictionary<BattleUnit, bool>();
    }

    /// <summary>The broadside game's rules (U.SPC-T425): batteries (the guns whose fire cones bear together, U.SPC-T533), focus
    /// fire on a weakened shield patch, the brace, damage control, formations and turning circles. Tunables are declared settings.</summary>
    [SettingsSource]
    public static class Broadsides
    {
        // ------------------------------------------------------------ settings

        public static readonly RangeSetting BraceReduction = new RangeSetting("broadside.brace", SettingGroups.Broadsides,
            "Brace", 0.5, 0, 0.9, 0.05,
            "The share of every hit a braced crew shrugs off while the brace lasts (its guns hold their fire while it is braced).",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "brace broadside capital volley");

        public static readonly RangeSetting DamageControlRate = new RangeSetting("broadside.damageControl", SettingGroups.Broadsides,
            "Damage control rate", 1, 0, 3, 0.25,
            "How fast a capital ship's damage control teams patch its mounts, shields and engines in battle: 0 turns damage control off, 1 is as designed.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "repair crew damage control capital");

        public static readonly ToggleSetting AiVolleys = new ToggleSetting("broadside.aiVolleys", SettingGroups.Broadsides,
            "AI captains fire broadsides", true,
            "AI capital ships hold the guns whose fire cones bear together for a volley and fire them together, telegraphing it, as the player can; a gun that bears alone fires at will.",
            "volley broadside ai doctrine");

        public static readonly ToggleSetting AiFocusFire = new ToggleSetting("broadside.aiFocusFire", SettingGroups.Broadsides,
            "AI gunners focus a weakened patch", true,
            "AI gun crews go for the hardpoints in a patch of the enemy's hull whose shield field is weakened or holed (how strongly is the shield focus setting).",
            "shield emitter patch focus fire ai doctrine");

        public static readonly ToggleSetting AiBraces = new ToggleSetting("broadside.aiBrace", SettingGroups.Broadsides,
            "AI captains brace", true,
            "AI capital ships brace when a broadside they can see building is about to come in on them.",
            "brace ai doctrine");

        // ------------------------------------------------------------ constants

        /// <summary>The smallest hull commanded as a capital ship, km (a destroyer and up).</summary>
        public static double MinCapitalKm => WeakPoints.MinCapitalKm;

        /// <summary>How long a brace lasts, and how long before the crew can brace again, s.</summary>
        public const double BraceSeconds = 3, BraceCooldownSeconds = 12;

        /// <summary>How much harder, and surer, a crew's volley is than the same guns at will.</summary>
        public const double CrewVolleyDamageBonus = 0.2, CrewVolleyAccuracyBonus = 0.2;

        /// <summary>How loaded the guns of a volley building on a hull must be before that hull's AI captain braces.</summary>
        public const double BraceAtLoaded = 0.75;

        /// <summary>People per damage control team, the most teams any hull musters, the hit points a team puts back a
        /// second, and how whole it can make a mount in the field (a refit at a yard does the rest).</summary>
        public const double CrewPerTeam = 250, RepairHpPerTeamS = 1.5, FieldRepairCap = 0.85;

        public const int MaxTeams = 8;

        // ------------------------------------------------------------ who commands

        /// <summary>A hull fought as a capital ship: a ship of a destroyer's size and up.</summary>
        public static bool Commands(BattleUnit u) => u != null && u.Kind == UnitKind.Ship && u.LengthKm >= MinCapitalKm;

        /// <summary><paramref name="u"/>'s orders (made on first asking), or null for anything smaller than a capital ship.</summary>
        public static BroadsideCommand Of(BattleUnit u) => !Commands(u) ? null : u.Broadside ??= new BroadsideCommand();

        // ------------------------------------------------------------ batteries

        /// <summary>A gun that fires in broadsides: a live direct-fire mount (not a missile, point defence or a support mount).</summary>
        public static bool IsBatteryGun(Subsystem s)
        {
            var w = s.Def.Weapon;
            // A spinal mount is no battery: the hull lays it, not its gun crews (U.SPC-T490).
            return w != null && !s.Destroyed && !w.Missile && !w.AntiSmall && !w.Supporting && s.Kind != SubsystemKind.BoardingPods && !s.Def.Spinal;
        }

        /// <summary>A mount that takes the captain's orders: a live weapon that is not point defence, a support mount or a pod
        /// launcher (missile launchers and spinal mounts hold or fire under them like any gun).</summary>
        public static bool IsOrdered(Subsystem s)
        {
            var w = s.Def.Weapon;
            return w != null && !s.Destroyed && !w.AntiSmall && !w.Supporting && s.Kind != SubsystemKind.BoardingPods;
        }

        /// <summary>The battery bearing along a battle-local direction off <paramref name="u"/> (where the captain looks): its
        /// guns whose fire cones take that line in (U.SPC-T533), and how many are loaded.</summary>
        public static (int loaded, int guns) Battery(BattleUnit u, DVec3 dir)
        {
            int loaded = 0, guns = 0;
            foreach (var s in u.Combat.Subsystems)
            {
                if (!IsBatteryGun(s) || !u.BearsAlong(s.Def, dir)) continue;
                guns++;
                if (s.Cooldown <= 0) loaded++;
            }
            return (loaded, guns);
        }

        /// <summary>The volley bearing on <paramref name="target"/>: the guns under a volley order whose cones hold it, and how
        /// many of them are loaded.</summary>
        public static (int loaded, int guns) VolleyOn(BattleUnit u, BattleUnit target)
        {
            int loaded = 0, guns = 0;
            var cmd = u.Broadside;
            if (cmd == null) return (0, 0);
            foreach (var s in u.Combat.Subsystems)
            {
                if (!IsBatteryGun(s) || cmd.OrderOf(s) != BatteryOrder.Volley || !u.Bears(s.Def, target.Pos)) continue;
                guns++;
                if (s.Cooldown <= 0) loaded++;
            }
            return (loaded, guns);
        }

        /// <summary>
        /// The order the mounts bearing along a battle-local direction off <paramref name="u"/> fire under: theirs if they share
        /// one, null if they differ; and how many there are (0 if none bears there).
        /// </summary>
        public static (BatteryOrder? order, int mounts) OrderAlong(BattleUnit u, DVec3 dir)
        {
            BatteryOrder? order = null;
            int n = 0;
            var cmd = u.Broadside;
            foreach (var s in u.Combat.Subsystems)
            {
                if (!IsOrdered(s) || !u.BearsAlong(s.Def, dir)) continue;
                var o = cmd?.OrderOf(s) ?? BatteryOrder.FireAtWill;
                if (n == 0) order = o;
                else if (order != o) order = null;
                n++;
            }
            return (n == 0 ? BatteryOrder.FireAtWill : order, n);
        }

        /// <summary>Puts every mount bearing along a battle-local direction off <paramref name="u"/> under
        /// <paramref name="order"/>; returns how many heard it.</summary>
        public static int OrderAlong(BattleUnit u, DVec3 dir, BatteryOrder order)
        {
            var cmd = Of(u);
            if (cmd == null) return 0;
            int n = 0;
            foreach (var s in u.Combat.Subsystems)
            {
                if (!IsOrdered(s) || !u.BearsAlong(s.Def, dir)) continue;
                cmd[s.Index] = order;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Steps the battery bearing along a battle-local direction off <paramref name="u"/> (where the captain looks) to its
        /// next order (at will, hold, volley), every mount whose cone takes that line in together; returns the new order and
        /// how many mounts took it (none: no gun bears there, and nothing changes). Mounts under different orders all go to
        /// fire at will first.
        /// </summary>
        public static (BatteryOrder order, int mounts) CycleOrder(BattleUnit u, DVec3 dir)
        {
            if (Of(u) == null) return (BatteryOrder.FireAtWill, 0);
            var (now, n) = OrderAlong(u, dir);
            if (n == 0) return (BatteryOrder.FireAtWill, 0);
            var next = (BatteryOrder)(((int)(now ?? BatteryOrder.Volley) + 1) % 3);
            OrderAlong(u, dir, next);
            return (next, n);
        }

        /// <summary>Puts every mount under <paramref name="order"/>.</summary>
        public static void SetAll(BattleUnit u, BatteryOrder order)
        {
            var cmd = Of(u);
            if (cmd == null) return;
            foreach (var s in u.Combat.Subsystems) cmd[s.Index] = order;
        }

        /// <summary>Before a hull's guns fire this step: the telegraph and last step's volleys are cleared, to be worked out
        /// again by the guns that hold or release this step (<see cref="Releases"/>, <see cref="Holding"/>).</summary>
        public static void BeginVolleys(BattleUnit u)
        {
            var cmd = u.Broadside;
            if (cmd == null) return;
            cmd.ReadyingOn = null;
            cmd.ReadyingLoaded = 0;
            cmd.Releasing.Clear();
        }

        /// <summary>
        /// Whether the volley on <paramref name="target"/> goes this step: once every gun under a volley order whose cone holds
        /// the target is loaded (with none, at once), and every loaded one goes with it. Worked out once a step, before any of
        /// them fires.
        /// </summary>
        public static bool Releases(BattleUnit u, BattleUnit target)
        {
            var cmd = u.Broadside;
            if (cmd == null || target == null) return true;
            if (cmd.Releasing.TryGetValue(target, out bool go)) return go;
            var (loaded, guns) = VolleyOn(u, target);
            go = guns == 0 || loaded == guns;
            cmd.Releasing[target] = go;
            return go;
        }

        /// <summary>A gun holds its fire for the volley building on <paramref name="target"/>: the telegraph shows it, with how
        /// loaded the guns of the volley bearing on it are.</summary>
        public static void Holding(BattleUnit u, BattleUnit target)
        {
            var cmd = u.Broadside;
            if (cmd == null || cmd.Readying) return;
            var (loaded, guns) = VolleyOn(u, target);
            cmd.ReadyingOn = target;
            cmd.ReadyingLoaded = guns == 0 ? 1 : (double)loaded / guns;
        }

        // ------------------------------------------------------------ focus fire

        /// <summary>Toggles whether <paramref name="u"/>'s gun crews spread their fire (true) or go for a weakened shield patch
        /// (false, the default); returns whether they now spread it.</summary>
        public static bool ToggleSpreadFire(BattleUnit u)
        {
            var cmd = Of(u);
            if (cmd == null) return false;
            cmd.SpreadFire = !cmd.SpreadFire;
            return cmd.SpreadFire;
        }

        /// <summary>
        /// Whether <paramref name="shooter"/>'s gunners favour the hardpoints in a weakened patch of their target's shields
        /// (U.SPC-T501, see <see cref="ShieldEmitters.FocusWeight"/>): an AI's crews by doctrine (<see cref="AiFocusFire"/>),
        /// a player's unless the captain has ordered the fire spread. With no shooter named (a stray hit), they do not.
        /// </summary>
        public static bool FocusesFire(BattleUnit shooter)
        {
            if (shooter == null) return false;
            if (shooter.Broadside != null && shooter.Broadside.SpreadFire) return false;
            return shooter.PlayerControlled || AiFocusFire.Value;
        }

        // ------------------------------------------------------------ the brace

        /// <summary>The crew braces for a broadside: <see cref="BraceSeconds"/> of every hit shrugged off by
        /// <see cref="BraceReduction"/>, its guns holding their fire meanwhile. False while the last brace is still wearing off.</summary>
        public static bool Brace(BattleUnit u)
        {
            var cmd = Of(u);
            if (cmd == null || cmd.Braced || cmd.BraceCooldown > 0) return false;
            cmd.BraceLeft = BraceSeconds;
            cmd.BraceCooldown = BraceCooldownSeconds;
            return true;
        }

        /// <summary>The share of a hit that gets past <paramref name="u"/>'s crew: less while braced.</summary>
        public static double BraceFactor(BattleUnit u) => u.Broadside != null && u.Broadside.Braced ? 1 - BraceReduction.Value : 1;

        /// <summary>Whether <paramref name="u"/>'s crews work its guns this step: not while braced (they load, and fire when
        /// the brace is over; point defence fires on its own throughout).</summary>
        public static bool Fires(BattleUnit u) => u.Broadside == null || !u.Broadside.Braced;

        /// <summary>
        /// The broadside about to come in on <paramref name="u"/>: a hostile's volley building on it with most of its guns
        /// loaded, or a hostile helmsman building one with the trigger held while looking its way. Null if none is.
        /// </summary>
        public static BattleUnit IncomingBroadside(Battle b, BattleUnit u)
        {
            double cone = Math.Cos(Battle.VolleyConeDeg * Astro.Deg2Rad);
            foreach (var o in b.Units)
            {
                if (!b.Hostile(u, o) || o.Stunned > 0 || o.Surrendering || o.Captured) continue;
                var cmd = o.Broadside;
                if (cmd != null && cmd.Readying && cmd.ReadyingOn == u && cmd.ReadyingLoaded >= BraceAtLoaded) return o;
                if (o.PlayerControlled && o.PlayerHelm && o.PlayerFireHeld && o.VolleyCharge >= Battle.VolleyBuildSeconds * 0.5 && o.PlayerVolleyAim.LengthSquared > 1e-12)
                {
                    var to = u.Pos - o.Pos;
                    if (to.LengthSquared > 1e-12 && DVec3.Dot(o.PlayerVolleyAim.Normalized, to.Normalized) > cone) return o;
                }
            }
            return null;
        }

        // ------------------------------------------------------------ damage control

        /// <summary>The damage control teams <paramref name="u"/> musters: one per <see cref="CrewPerTeam"/> of its crew
        /// (half while boarders are aboard), at most <see cref="MaxTeams"/>.</summary>
        public static int Teams(BattleUnit u)
        {
            var interior = u.Combat.Interior;
            if (interior == null) return 0;
            double crew = interior.Crew * (interior.UnderBoarding ? 0.5 : 1);
            return (int)Math.Min(MaxTeams, Math.Floor(crew / CrewPerTeam));
        }

        /// <summary>Steps where damage control works first; returns the new focus.</summary>
        public static DamageControlFocus CycleFocus(BattleUnit u)
        {
            var cmd = Of(u);
            if (cmd == null) return DamageControlFocus.Balanced;
            cmd.Focus = (DamageControlFocus)(((int)cmd.Focus + 1) % 4);
            return cmd.Focus;
        }

        static bool InFocus(Subsystem s, DamageControlFocus focus)
        {
            switch (focus)
            {
                case DamageControlFocus.Weapons: return s.Def.IsWeapon;
                case DamageControlFocus.Shields: return s.Kind == SubsystemKind.ShieldGenerator;
                case DamageControlFocus.Engines: return s.Kind == SubsystemKind.Engine;
                default: return false;
            }
        }

        /// <summary>
        /// The teams work: each puts back <see cref="RepairHpPerTeamS"/> a second (times <see cref="DamageControlRate"/> and
        /// the fleet's supply readiness) into damaged mounts, those in the focus first and the worst-hurt first among them, up
        /// to <see cref="FieldRepairCap"/> of whole. Its emergency repair crews bring back a shield emitter or gun mount shot
        /// away (U.SPC-T501): <see cref="ShieldEmitters.EmergencyRepairWork"/> of its hit points' worth of work, at
        /// <see cref="ShieldEmitters.EmergencyRepairRate"/>, restores it to <see cref="ShieldEmitters.RestoredHealth"/> (an
        /// emitter's field then regrows from nothing); anything else shot away, and the hull itself, wait for a yard.
        /// With the focus on shields, emitters under fire keep their fields charging at half their rate.
        /// </summary>
        public static void StepDamageControl(BattleUnit u, double dt)
        {
            var cmd = u.Broadside;
            // Nor with the hull's systems knocked out: the teams are working in the dark.
            if (cmd == null || u.Captured || u.Surrendering || u.Dead || u.Stunned > 0) return;
            double rate = DamageControlRate.Value;
            int teams = Teams(u);
            if (rate <= 0 || teams <= 0) return;
            var c = u.Combat;
            double budget = teams * RepairHpPerTeamS * rate * c.Readiness * dt;
            for (int pass = 0; pass < 8 && budget > 1e-9; pass++)
            {
                Subsystem worst = null;
                double worstScore = double.MaxValue;
                foreach (var s in c.Subsystems)
                {
                    if (s.Destroyed || s.Hp >= s.Def.MaxHp * FieldRepairCap) continue;
                    double score = s.Health - (InFocus(s, cmd.Focus) ? 2 : 0);
                    if (score < worstScore)
                    {
                        worstScore = score;
                        worst = s;
                    }
                }
                if (worst == null) break;
                double fix = Math.Min(budget, worst.Def.MaxHp * FieldRepairCap - worst.Hp);
                worst.Hp += fix;
                cmd.Repaired += fix;
                budget -= fix;
            }
            StepEmergencyRepair(c, cmd, teams, dt);
            if (cmd.Focus == DamageControlFocus.Shields)
            {
                double ratings = 0, room = 0;
                foreach (var s in c.Subsystems)
                    if (s.Kind == SubsystemKind.ShieldGenerator && !s.Destroyed)
                    {
                        ratings += s.Def.Rating;
                        room += s.MaxShield - s.Shield;
                    }
                if (ratings <= 0 || u.Stunned > 0) return;
                foreach (var s in c.Subsystems)
                {
                    if (room <= 0) return;
                    if (s.Kind != SubsystemKind.ShieldGenerator || s.Destroyed || s.Shield >= s.MaxShield) continue;
                    // RegenShields already regrows a patch that has not been hit lately: this is for the ones under fire.
                    double add = Math.Min(room, Math.Min(s.MaxShield - s.Shield, c.Spec.ShieldRegen * s.Def.Rating / ratings * 0.5 * rate * dt));
                    s.Shield += add;
                    room -= add;
                }
            }
        }

        /// <summary>
        /// The emergency repair crews (U.SPC-T501): <paramref name="teams"/> teams' work, at
        /// <see cref="ShieldEmitters.EmergencyRepairRate"/>, goes into the shield emitters and gun mounts shot away, those in
        /// the damage control focus first and among them the one nearest done, each coming back at
        /// <see cref="ShieldEmitters.RestoredHealth"/> once its <see cref="ShieldEmitters.EmergencyRepairWork"/> is in.
        /// </summary>
        static void StepEmergencyRepair(ShipCombat c, BroadsideCommand cmd, int teams, double dt)
        {
            double rate = ShieldEmitters.EmergencyRepairRate.Value;
            if (rate <= 0) return;
            double work = teams * RepairHpPerTeamS * rate * c.Readiness * dt;
            double share = ShieldEmitters.EmergencyRepairWork.Value;
            for (int pass = 0; pass < 8 && work > 1e-9; pass++)
            {
                Subsystem next = null;
                double nextScore = double.MaxValue;
                foreach (var s in c.Subsystems)
                {
                    if (!ShieldEmitters.Restorable(s)) continue;
                    double score = -s.Restoring / s.Def.MaxHp - (InFocus(s, cmd.Focus) ? 10 : 0);
                    if (score < nextScore)
                    {
                        nextScore = score;
                        next = s;
                    }
                }
                if (next == null) return;
                double need = share * next.Def.MaxHp - next.Restoring;
                double put = Math.Min(work, Math.Max(0, need));
                next.Restoring += put;
                work -= put;
                if (next.Restoring < share * next.Def.MaxHp - 1e-9) continue;
                next.Hp = next.Def.MaxHp * ShieldEmitters.RestoredHealth.Value;
                next.Restoring = 0;
                next.Shield = 0;
                cmd.Restored++;
            }
        }

        // ------------------------------------------------------------ the step

        /// <summary>
        /// A capital ship's broadside step, run by the battle each step before its shields regenerate: the brace wears off,
        /// an AI captain follows doctrine (beam batteries in volleys, a brace when a broadside is about to come in) and damage
        /// control works, its emergency repair crews bringing back what has been shot away.
        /// </summary>
        public static void Step(Battle b, BattleUnit u, double dt)
        {
            var cmd = Of(u);
            if (cmd == null) return;
            cmd.BraceLeft = Math.Max(0, cmd.BraceLeft - dt);
            cmd.BraceCooldown = Math.Max(0, cmd.BraceCooldown - dt);
            if (!u.PlayerControlled && !u.Captured && !u.Surrendering && !u.Combat.Disabled) Doctrine(b, u, cmd);
            StepDamageControl(u, dt);
        }

        /// <summary>
        /// What an AI captain orders on its own (each part a setting): the guns whose fire cones overlap another gun's, those
        /// that can bear on a hull together, fire in volleys (<see cref="AiVolleys"/>), and a gun that bears alone fires at
        /// will (U.SPC-T533); and it braces for a broadside about to come in.
        /// </summary>
        static void Doctrine(Battle b, BattleUnit u, BroadsideCommand cmd)
        {
            var subs = u.Combat.Subsystems;
            var spec = u.Combat.Spec;
            foreach (var s in subs)
            {
                if (!IsOrdered(s)) continue;
                bool together = false;
                if (AiVolleys.Value && IsBatteryGun(s))
                    foreach (var o in subs)
                        if (o != s && IsBatteryGun(o) && FireCones.Overlap(s.Def, o.Def, spec))
                        {
                            together = true;
                            break;
                        }
                cmd[s.Index] = together ? BatteryOrder.Volley : BatteryOrder.FireAtWill;
            }
            if (AiBraces.Value && !cmd.Braced && cmd.BraceCooldown <= 0 && IncomingBroadside(b, u) != null) Brace(u);
        }

        // ------------------------------------------------------------ formations

        /// <summary>A formation that holds its ships on station (every order but none and engage at will).</summary>
        public static bool IsHeld(FleetFormation f) => f != FleetFormation.None && f != FleetFormation.EngageAtWill;

        /// <summary>Whether <paramref name="u"/> keeps a held formation on its leader.</summary>
        public static bool HoldsFormation(BattleUnit u) =>
            u.Leader != null && u.Leader.Present && !u.Leader.Fleeing && u.Leader.Broadside != null && IsHeld(u.Leader.Broadside.Formation);

        /// <summary>The ships <paramref name="flag"/> can order into formation: its own faction's warships of a destroyer's
        /// size and up on its side, flown by their crews and still fighting, biggest first.</summary>
        public static List<BattleUnit> FormationMembers(Battle b, BattleUnit flag)
        {
            var list = new List<BattleUnit>();
            foreach (var o in b.Units)
            {
                if (o == flag || !o.Present || o.Side != flag.Side || o.Faction != flag.Faction || !Commands(o)) continue;
                if (o.PlayerControlled || o.Captured || o.Surrendering || o.Fleeing || o.Combat.Disabled || o.Latched != null || o.BoardTarget != null) continue;
                if (o.Ship == null || !o.Ship.Spec.IsMilitary) continue;
                list.Add(o);
            }
            list.Sort((a, c) =>
            {
                int h = c.Combat.Spec.Hull.CompareTo(a.Combat.Spec.Hull);
                return h != 0 ? h : a.Id.CompareTo(c.Id);
            });
            return list;
        }

        /// <summary>
        /// The <paramref name="i"/>th of <paramref name="n"/> ships' station in <paramref name="f"/>, km in the flagship's
        /// frame (right, up, forward), <paramref name="spacing"/> km apart.
        /// </summary>
        public static DVec3 SlotFor(FleetFormation f, int i, int n, double spacing)
        {
            int k = i + 1;
            switch (f)
            {
                case FleetFormation.LineAhead: return new DVec3(0, 0, -k * spacing);
                case FleetFormation.LineAbreast:
                    int rank = (i / 2) + 1;
                    return new DVec3((i % 2 == 0 ? 1 : -1) * rank * spacing, 0, 0);
                case FleetFormation.Echelon: return new DVec3(k * spacing * 0.7, 0, -k * spacing * 0.7);
                case FleetFormation.Screen:
                    double a = 2 * Math.PI * i / Math.Max(1, n);
                    double r = spacing * 1.5;
                    return new DVec3(Math.Sin(a) * r, 0, Math.Cos(a) * r);
                default: return DVec3.Zero;
            }
        }

        /// <summary>The distance between ships in a line led by <paramref name="flag"/>, km: half again the longest hull, plus a km.</summary>
        public static double Spacing(BattleUnit flag, List<BattleUnit> members)
        {
            double longest = flag.LengthKm;
            foreach (var m in members) longest = Math.Max(longest, m.LengthKm);
            return longest * 1.5 + 1;
        }

        /// <summary>
        /// <paramref name="flag"/> orders its line into <paramref name="f"/>: every ship it can order (see
        /// <see cref="FormationMembers"/>) takes station on it, or, to engage at will, is cut loose to fight its own fight.
        /// Returns how many ships heard it.
        /// </summary>
        public static int OrderFormation(Battle b, BattleUnit flag, FleetFormation f)
        {
            var cmd = Of(flag);
            if (cmd == null) return 0;
            var members = FormationMembers(b, flag);
            cmd.Formation = f;
            double spacing = Spacing(flag, members);
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (IsHeld(f))
                {
                    m.Leader = flag;
                    m.Slot = SlotFor(f, i, members.Count, spacing);
                }
                else if (m.Leader == flag)
                {
                    m.Leader = null;
                    m.Slot = DVec3.Zero;
                }
            }
            return members.Count;
        }

        public static string Label(FleetFormation f)
        {
            switch (f)
            {
                case FleetFormation.LineAhead: return "line ahead";
                case FleetFormation.LineAbreast: return "line abreast";
                case FleetFormation.Echelon: return "echelon";
                case FleetFormation.Screen: return "screen";
                case FleetFormation.EngageAtWill: return "engage at will";
                default: return "no formation";
            }
        }

        public static string Label(BatteryOrder o) => o == BatteryOrder.HoldFire ? "hold fire" : o == BatteryOrder.Volley ? "volley" : "fire at will";

        public static string Label(DamageControlFocus f) => f == DamageControlFocus.Weapons ? "weapons" : f == DamageControlFocus.Shields ? "shields" : f == DamageControlFocus.Engines ? "engines" : "balanced";

        // ------------------------------------------------------------ turning circles

        /// <summary>The radius of the circle a hull turning at <paramref name="turnDegS"/> traces at <paramref name="speedKmS"/>, km.</summary>
        public static double TurningRadiusKm(double speedKmS, double turnDegS) => turnDegS <= 0 ? double.PositiveInfinity : Math.Abs(speedKmS) / (turnDegS * Astro.Deg2Rad);

        /// <summary>How long a hull turning at <paramref name="turnDegS"/> takes to come about (turn through 180 deg), s.</summary>
        public static double ComeAboutSeconds(double turnDegS) => turnDegS <= 0 ? double.PositiveInfinity : 180 / turnDegS;

        /// <summary>How long <paramref name="u"/>, turning at <paramref name="turnDegS"/>, takes to swing
        /// <paramref name="target"/> into <paramref name="gun"/>'s fire cone, s (0 if it already bears).</summary>
        public static double SecondsToBear(BattleUnit u, BattleUnit target, Subsystem gun, double turnDegS)
        {
            if (u.Bears(gun.Def, target.Pos)) return 0;
            if (turnDegS <= 0) return double.PositiveInfinity;
            return FireCones.OutsideDeg(gun.Def, u.Combat.Spec, u.DirToLocal(target.Pos - u.Pos)) / turnDegS;
        }
    }
}
