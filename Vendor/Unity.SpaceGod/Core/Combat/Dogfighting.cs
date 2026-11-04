using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    // The dogfighting game (U.SPC-T424): a carrier's squadrons as craft of their own classes, each flown by its own
    // flight model, with weapon groups, lock-on missiles, countermeasures, power to share between weapons, shields and
    // engines, wingmen who take orders, and capital-ship weak points a lone small craft can bring a battle down on.
    // Every part is data a new craft, weak point or order joins by a table entry, not by an edit to the battle loop.

    /// <summary>What a launched small craft is for (U.SPC-T424): each is a <see cref="ShipClass"/> of its own (U.SPC-T484)
    /// with its own handling.</summary>
    public enum FighterRole
    {
        /// <summary>An interceptor (<see cref="ShipClass.Interceptor"/>): the fastest, the tightest turn, light guns and a
        /// missile, for hunting other craft and screening its own bombers.</summary>
        Interceptor,

        /// <summary>A heavy fighter: tougher and slower to turn, guns and a full missile load, for fighting anything. Flown on
        /// the gunship's hull (U.SPC-T467).</summary>
        HeavyFighter,

        /// <summary>A bomber (<see cref="ShipClass.Bomber"/>): slow and armoured, slung torpedoes for capital ships.</summary>
        Bomber,

        /// <summary>A space superiority fighter (<see cref="ShipClass.Fighter"/>, U.SPC-T484): the balanced workhorse, guns
        /// and a pair of missile racks, as ready to fight craft as to strafe a hull.</summary>
        SpaceSuperiority,

        /// <summary>A recon / electronic warfare fighter (<see cref="ShipClass.Recon"/>, U.SPC-T485): flown ahead of the
        /// squadrons in a carrier's recon flight, it jams the enemy's targeting and paints a capital for the bombers (see
        /// <see cref="ElectronicWarfare"/>). Never in <see cref="FighterCraft.SquadronMix"/>.</summary>
        Recon,

        /// <summary>A boarding shuttle (<see cref="ShipClass.AssaultShuttle"/>, U.SPC-T486): unarmed and armoured, it carries a
        /// marine team to a disabled hull or a silenced station, docks and latches on (<see cref="BoardingShuttles"/>). Not
        /// one of a carrier's squadrons: a carrier sends one out when there is a prize to take.</summary>
        AssaultShuttle,
    }

    /// <summary>
    /// One class of launched small craft (U.SPC-T424): the <see cref="ShipClass"/> it is a hull of (U.SPC-T484), its
    /// fighting qualities and hull (<see cref="Spec"/>, with a real mass, thrust and torque so it flies through
    /// <see cref="HullDynamics"/>), the <see cref="IFlightModel"/> its class flies on in a player's hands (U.SPC-T17), the
    /// handling numbers flight assist works to, and how its pilots pick their fights. A new class is a new entry here
    /// (<see cref="Register"/>) and a place in <see cref="SquadronMix"/>.
    /// </summary>
    public sealed class FighterCraft
    {
        public readonly FighterRole Role;
        public readonly string Name;

        /// <summary>The hull class it is (U.SPC-T484): what the carrier launches, what it is drawn as, and whose flight
        /// model flies it (<see cref="FlightModelRegistry.For"/>).</summary>
        public readonly ShipClass Class;

        public readonly CombatSpec Spec;
        public readonly double LengthKm;

        /// <summary>The afterburner: top speed times this while the charge lasts, over this many seconds.</summary>
        public readonly double BoostMultiplier, BoostSeconds;

        /// <summary>How much further its pilots go after an enemy fighter than after a hull, km (an interceptor hunts craft;
        /// a bomber never picks a fighter at all, see <see cref="FighterState.Bomber"/>).</summary>
        public double HuntsFightersKm = 6;

        /// <summary>Whether its pilots go first for an enemy fighter closing on one of their own side's bombers (U.SPC-T484):
        /// the interceptor's screen.</summary>
        public bool ScreensBombers;

        public FighterCraft(FighterRole role, string name, ShipClass cls, CombatSpec spec, double lengthKm, double boostMultiplier, double boostSeconds)
        {
            Role = role;
            Name = name;
            Class = cls;
            Spec = spec;
            LengthKm = lengthKm;
            BoostMultiplier = boostMultiplier;
            BoostSeconds = boostSeconds;
        }

        /// <summary>The flight family its class flies in.</summary>
        public FlightFamily Family => FlightModelRegistry.Default.FamilyOf(Class);

        /// <summary>How flight assist flies it at <paramref name="engines"/> times its own power (see <see cref="PowerDistribution"/>).</summary>
        public PilotTuning Tuning(double engines = 1) =>
            new PilotTuning(Spec.MaxSpeed * engines, Spec.TurnRateDegS, BoostMultiplier, BoostSeconds, 1, 0);

        /// <summary>The flight model a player flies it on: its class's.</summary>
        public IFlightModel Model => FlightModelRegistry.Default.For(Class);

        static readonly Dictionary<FighterRole, FighterCraft> Crafts = new Dictionary<FighterRole, FighterCraft>();

        /// <summary>A carrier's squadrons in launch order, one role each, repeating: interceptors, space superiority fighters,
        /// bombers, then (on a carrier with a fourth squadron) heavy fighters.</summary>
        public static FighterRole[] SquadronMix = { FighterRole.Interceptor, FighterRole.SpaceSuperiority, FighterRole.Bomber, FighterRole.HeavyFighter };

        public static FighterRole RoleOfSquadron(int squadron) => SquadronMix[((squadron % SquadronMix.Length) + SquadronMix.Length) % SquadronMix.Length];

        public static void Register(FighterCraft craft) => Crafts[craft.Role] = craft ?? throw new ArgumentNullException(nameof(craft));

        public static FighterCraft Of(FighterRole role) => Crafts[role];

        public static readonly FighterCraft Interceptor = Add(FighterRole.Interceptor, "interceptor", ShipClass.Interceptor, 0.016, 0.012, 400, 1.6, 6, new CombatSpec
        {
            Hull = 70, Shield = 30, ShieldRegen = 2, Armor = 0, MaxSpeed = 0.35, Accel = 0.25, TurnRateDegS = 120,
            Layout = new[]
            {
                new SubsystemDef(SubsystemKind.FighterGuns, 0.2, 0, 0.4, coneAxis: FireCones.Bow, coneHalfDeg: 14),
                new SubsystemDef(SubsystemKind.FighterGuns, -0.2, 0, 0.4, coneAxis: FireCones.Bow, coneHalfDeg: 14),
                new SubsystemDef(SubsystemKind.MissileLauncher, 0, -0.1, 0.1, coneAxis: FireCones.Bow, coneHalfDeg: 20) { Weapon = WeaponSpec.DogfightMissile, MaxHp = 30 },
                new SubsystemDef(SubsystemKind.Countermeasures, 0, 0, -0.4, facesAft: true),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0, -0.3),
            },
        }, huntsFightersKm: 14, screensBombers: true);

        /// <summary>The balanced workhorse (U.SPC-T484): a little slower and wider in the turn than the interceptor, tougher,
        /// and with twice its missiles, as ready to strafe a hull as to fight a craft.</summary>
        public static readonly FighterCraft SpaceSuperiority = Add(FighterRole.SpaceSuperiority, "fighter", ShipClass.Fighter, 0.018, 0.018, 260, 1.55, 5.5, new CombatSpec
        {
            Hull = 100, Shield = 45, ShieldRegen = 2.5, Armor = 0.5, MaxSpeed = 0.32, Accel = 0.22, TurnRateDegS = 100,
            Layout = new[]
            {
                new SubsystemDef(SubsystemKind.FighterGuns, 0.22, 0, 0.4, coneAxis: FireCones.Bow, coneHalfDeg: 14),
                new SubsystemDef(SubsystemKind.FighterGuns, -0.22, 0, 0.4, coneAxis: FireCones.Bow, coneHalfDeg: 14),
                new SubsystemDef(SubsystemKind.MissileLauncher, 0.12, -0.1, 0.1, coneAxis: FireCones.Bow, coneHalfDeg: 20) { Weapon = WeaponSpec.DogfightMissile, MaxHp = 30 },
                new SubsystemDef(SubsystemKind.MissileLauncher, -0.12, -0.1, 0.1, coneAxis: FireCones.Bow, coneHalfDeg: 20) { Weapon = WeaponSpec.DogfightMissile, MaxHp = 30 },
                new SubsystemDef(SubsystemKind.Countermeasures, 0, 0, -0.4, facesAft: true),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0, -0.3),
            },
        });

        public static readonly FighterCraft HeavyFighter = Add(FighterRole.HeavyFighter, "heavy fighter", ShipClass.Gunship, 0.02, 0.025, 160, 1.5, 5, new CombatSpec
        {
            Hull = 140, Shield = 60, ShieldRegen = 3, Armor = 1, MaxSpeed = 0.3, Accel = 0.2, TurnRateDegS = 90,
            Layout = new[]
            {
                new SubsystemDef(SubsystemKind.FighterGuns, 0.25, 0, 0.4, coneAxis: FireCones.Bow, coneHalfDeg: 14),
                new SubsystemDef(SubsystemKind.FighterGuns, -0.25, 0, 0.4, coneAxis: FireCones.Bow, coneHalfDeg: 14),
                new SubsystemDef(SubsystemKind.MissileLauncher, 0.15, -0.1, 0.1, coneAxis: FireCones.Bow, coneHalfDeg: 20) { Weapon = WeaponSpec.DogfightMissile, MaxHp = 30 },
                new SubsystemDef(SubsystemKind.MissileLauncher, -0.15, -0.1, 0.1, coneAxis: FireCones.Bow, coneHalfDeg: 20) { Weapon = WeaponSpec.DogfightMissile, MaxHp = 30 },
                new SubsystemDef(SubsystemKind.Countermeasures, 0, 0, -0.4, facesAft: true),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0, -0.3),
            },
        });

        public static readonly FighterCraft Bomber = Add(FighterRole.Bomber, "bomber", ShipClass.Bomber, 0.024, 0.03, 120, 1.4, 5, new CombatSpec
        {
            Hull = 130, Shield = 50, ShieldRegen = 2, Armor = 1, MaxSpeed = 0.26, Accel = 0.15, TurnRateDegS = 80,
            Layout = new[]
            {
                new SubsystemDef(SubsystemKind.FighterGuns, 0, 0.05, 0.4, coneAxis: FireCones.Bow, coneHalfDeg: 14),
                new SubsystemDef(SubsystemKind.Torpedo, 0, -0.1, 0),
                new SubsystemDef(SubsystemKind.Countermeasures, 0, 0, -0.4, facesAft: true),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0, -0.3),
            },
        });

        /// <summary>The recon / EW fighter (U.SPC-T485): an interceptor's pace, a light gun for its own defence, flares, and
        /// the rest of it sensors and jammers (<see cref="ElectronicWarfare"/>). It never hunts fighters: it holds off its
        /// mark, jamming and painting.</summary>
        public static readonly FighterCraft Recon = Add(FighterRole.Recon, "recon fighter", ShipClass.Recon, 0.017, 0.014, 360, 1.6, 7, new CombatSpec
        {
            Hull = 65, Shield = 40, ShieldRegen = 2.5, Armor = 0, MaxSpeed = 0.34, Accel = 0.24, TurnRateDegS = 110,
            Layout = new[]
            {
                new SubsystemDef(SubsystemKind.FighterGuns, 0, -0.05, 0.4, coneAxis: FireCones.Bow, coneHalfDeg: 14),
                new SubsystemDef(SubsystemKind.Countermeasures, 0, 0, -0.4, facesAft: true),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0, -0.3),
            },
        }, huntsFightersKm: 0);

        /// <summary>The boarding shuttle (U.SPC-T486): slow, plated and shielded to get its team across, no guns, flares to
        /// shake a missile off. Its pilots fly it to a prize, never into a fight (<see cref="BoardingShuttles"/>).</summary>
        public static readonly FighterCraft AssaultShuttle = Add(FighterRole.AssaultShuttle, "assault shuttle", ShipClass.AssaultShuttle, 0.03, 0.04, 90, 1.3, 4, new CombatSpec
        {
            Hull = 200, Shield = 70, ShieldRegen = 2, Armor = 2, MaxSpeed = 0.2, Accel = 0.1, TurnRateDegS = 60,
            Layout = new[]
            {
                new SubsystemDef(SubsystemKind.Countermeasures, 0, 0, -0.4, facesAft: true),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0, -0.3),
            },
        }, huntsFightersKm: 0);

        /// <summary>
        /// Gives <paramref name="spec"/> a real hull for the flight models: <paramref name="massKt"/>, a main drive that
        /// pushes it at its <see cref="CombatSpec.Accel"/>, retro thrusters three quarters as strong and manoeuvre
        /// thrusters half, and attitude torque that spins it up at <paramref name="angularAccel"/> rad/s² (rolling twice as
        /// readily), and registers it.
        /// </summary>
        static FighterCraft Add(FighterRole role, string name, ShipClass cls, double lengthKm, double massKt, double angularAccel, double boost, double boostSeconds, CombatSpec spec,
            double huntsFightersKm = 6, bool screensBombers = false)
        {
            spec.DesignName = name;
            spec.LengthKm = lengthKm;
            spec.MassKt = massKt;
            spec.MainThrustMN = spec.Accel * massKt * 1000;
            spec.RetroThrustMN = spec.MainThrustMN * 0.75;
            spec.LateralThrustMN = spec.VerticalThrustMN = spec.MainThrustMN * 0.5;
            spec.BoostSpeedMultiplier = boost;
            spec.BoostDurationS = boostSeconds;
            spec.PitchYawTorqueMNKm = angularAccel * spec.PitchYawInertia * 1000;
            spec.RollTorqueMNKm = angularAccel * 2 * spec.RollInertia * 1000;
            var craft = new FighterCraft(role, name, cls, spec, lengthKm, boost, boostSeconds) { HuntsFightersKm = huntsFightersKm, ScreensBombers = screensBombers };
            Register(craft);
            return craft;
        }

        /// <summary>The craft a carrier launches as hulls of <paramref name="cls"/>, or null if none is.</summary>
        public static FighterCraft OfClass(ShipClass cls)
        {
            foreach (var c in Crafts.Values)
                if (c.Class == cls) return c;
            return null;
        }
    }

    // ---------------------------------------------------------------- weapon groups

    /// <summary>The groups a small craft's weapons are fired in (U.SPC-T424): the guns on the main trigger, and one
    /// selectable group of guided weapons on the second.</summary>
    public enum WeaponGroup
    {
        Guns,
        Missiles,
        Torpedoes,
    }

    /// <summary>Which group each kind of mount fires in: a table, so a new mount joins a group by an entry here.</summary>
    public static class WeaponGroups
    {
        static readonly Dictionary<SubsystemKind, WeaponGroup> ByKind = new Dictionary<SubsystemKind, WeaponGroup>
        {
            [SubsystemKind.FighterGuns] = WeaponGroup.Guns,
            [SubsystemKind.Autocannon] = WeaponGroup.Guns,
            [SubsystemKind.LightRailgun] = WeaponGroup.Guns,
            [SubsystemKind.MissileLauncher] = WeaponGroup.Missiles,
            [SubsystemKind.Torpedo] = WeaponGroup.Torpedoes,
        };

        public static void Assign(SubsystemKind kind, WeaponGroup group) => ByKind[kind] = group;

        /// <summary>The group a mount fires in, or null for one that is not fired from the cockpit (a shield, a dispenser).</summary>
        public static WeaponGroup? Of(SubsystemKind kind) => ByKind.TryGetValue(kind, out var g) ? g : (WeaponGroup?)null;

        /// <summary>The guided groups (everything but the guns) <paramref name="u"/> carries a live mount of, in order.</summary>
        public static List<WeaponGroup> Secondaries(BattleUnit u)
        {
            var list = new List<WeaponGroup>();
            foreach (WeaponGroup g in Enum.GetValues(typeof(WeaponGroup)))
            {
                if (g == WeaponGroup.Guns) continue;
                foreach (var s in u.Combat.Subsystems)
                    if (!s.Destroyed && Of(s.Kind) == g)
                    {
                        list.Add(g);
                        break;
                    }
            }
            return list;
        }

        /// <summary>Rounds left across <paramref name="u"/>'s live mounts of <paramref name="g"/> (-1 for guns, which never run dry).</summary>
        public static int Ammo(BattleUnit u, WeaponGroup g)
        {
            if (g == WeaponGroup.Guns) return -1;
            int n = 0;
            foreach (var s in u.Combat.Subsystems)
                if (!s.Destroyed && Of(s.Kind) == g) n += s.Ammo;
            return n;
        }

        public static string Label(WeaponGroup g) => g == WeaponGroup.Guns ? "guns" : g == WeaponGroup.Missiles ? "missiles" : "torpedoes";
    }

    // ---------------------------------------------------------------- energy

    /// <summary>The three systems a small craft's reactor feeds (U.SPC-T424).</summary>
    public enum PowerSystem
    {
        Weapons,
        Shields,
        Engines,
    }

    /// <summary>
    /// How a small craft shares its reactor (U.SPC-T424): <see cref="TotalPips"/> pips across weapons, shields and
    /// engines, at most <see cref="MaxPips"/> to any one. At an even share each runs as designed; every pip more is a
    /// quarter faster (guns reloading and cooling, shields recharging, top speed and thrust), every pip less a quarter
    /// slower. A unit with no distribution runs everything as designed.
    /// </summary>
    public sealed class PowerDistribution
    {
        public const int TotalPips = 6, MaxPips = 4, EvenPips = 2;
        public const double PerPip = 0.25;

        readonly int[] pips = { EvenPips, EvenPips, EvenPips };

        public int this[PowerSystem s] => pips[(int)s];

        /// <summary>How hard <paramref name="s"/> runs: 1 at an even share, 0.5 with none, 1.5 at the most.</summary>
        public double Factor(PowerSystem s) => 1 + (pips[(int)s] - EvenPips) * PerPip;

        /// <summary>Moves a pip to <paramref name="s"/> from whichever other system has the most; false at the most already.</summary>
        public bool Shift(PowerSystem s)
        {
            int i = (int)s;
            if (pips[i] >= MaxPips) return false;
            int from = -1;
            for (int k = 0; k < pips.Length; k++)
                if (k != i && pips[k] > 0 && (from < 0 || pips[k] > pips[from])) from = k;
            if (from < 0) return false;
            pips[from]--;
            pips[i]++;
            return true;
        }

        /// <summary>Back to an even share.</summary>
        public void Balance()
        {
            for (int k = 0; k < pips.Length; k++) pips[k] = EvenPips;
        }

        /// <summary>Sets the share outright (each 0..<see cref="MaxPips"/>, summing to <see cref="TotalPips"/>); false if it cannot be.</summary>
        public bool Set(int weapons, int shields, int engines)
        {
            if (weapons + shields + engines != TotalPips || Math.Max(weapons, Math.Max(shields, engines)) > MaxPips || Math.Min(weapons, Math.Min(shields, engines)) < 0)
                return false;
            pips[0] = weapons;
            pips[1] = shields;
            pips[2] = engines;
            return true;
        }

        public bool IsEven => pips[0] == EvenPips && pips[1] == EvenPips && pips[2] == EvenPips;

        /// <summary><paramref name="u"/>'s factor for <paramref name="s"/>: 1 when it shares no power.</summary>
        public static double FactorOf(BattleUnit u, PowerSystem s) => u.Power?.Factor(s) ?? 1;

        public override string ToString() => "W" + pips[0] + " S" + pips[1] + " E" + pips[2];
    }

    // ---------------------------------------------------------------- countermeasures

    /// <summary>
    /// A flare-and-chaff cloud a small craft throws out astern (U.SPC-T424): each guided round homing on the craft
    /// within <see cref="Countermeasures.ReachKm"/> may be seduced onto it (<see cref="Countermeasures.Effectiveness"/>,
    /// less for a torpedo), and goes off harmlessly when it reaches it or the cloud burns out.
    /// </summary>
    public sealed class Decoy
    {
        public DVec3 Pos, Vel;
        public int Side;
        public double Life;
        public bool Gone;
    }

    [SettingsSource]
    public static class Countermeasures
    {
        /// <summary>How long a cloud burns, and how far off a homing round must be to be drawn onto it, km.</summary>
        public const double LifeSeconds = 4, ReachKm = 3;

        /// <summary>The share of an incoming torpedo's chance of being seduced to a missile's: a torpedo's seeker is harder to fool.</summary>
        public const double TorpedoShare = 0.4;

        /// <summary>How near an inbound round an AI pilot waits to throw a cloud, km.</summary>
        public const double AiReactKm = 2.2;

        public static readonly RangeSetting Effectiveness = new RangeSetting("dogfight.countermeasures", SettingGroups.Dogfighting,
            "Countermeasure effectiveness", 0.65, 0, 1, 0.05,
            "The chance a flare-and-chaff cloud draws off each missile homing on the craft that threw it (a torpedo is fooled less than half as often).",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "flares chaff decoy missile dogfight");

        /// <summary>The chance a cloud seduces a round of <paramref name="kind"/>; never an EMP round or a boarding pod.</summary>
        public static double SeduceChance(MissileKind kind) =>
            kind == MissileKind.Missile || kind == MissileKind.Interceptor ? Effectiveness.Value
            : kind == MissileKind.Torpedo || kind == MissileKind.Antimatter ? Effectiveness.Value * TorpedoShare : 0;
    }

    // ---------------------------------------------------------------- capital-ship weak points

    /// <summary>
    /// A part of a capital ship that is exposed from one side (U.SPC-T424): its reactor's vent, its engine bells, its
    /// shield emitters. A small craft's shot that strikes it coming in from that side (within <see cref="ConeDeg"/> of
    /// <see cref="LocalAxis"/>, in ship-length axes: right, up, forward) does <see cref="Multiplier"/> times the damage
    /// that gets through the shields.
    /// </summary>
    public sealed class WeakPointSpec
    {
        public readonly SubsystemKind Kind;
        public readonly string Label;
        public readonly DVec3 LocalAxis;
        public readonly double ConeDeg, Multiplier;

        /// <summary>Which a pilot goes for first when several are open: higher first.</summary>
        public readonly int Priority;

        public WeakPointSpec(SubsystemKind kind, string label, DVec3 localAxis, double coneDeg, double multiplier, int priority)
        {
            Kind = kind;
            Label = label;
            LocalAxis = localAxis.Normalized;
            ConeDeg = coneDeg;
            Multiplier = multiplier;
            Priority = priority;
        }
    }

    [SettingsSource]
    public static class WeakPoints
    {
        static readonly Dictionary<SubsystemKind, WeakPointSpec> Specs = new Dictionary<SubsystemKind, WeakPointSpec>();

        /// <summary>The smallest hull with weak points to go for, km (a destroyer and up), and the largest that counts as a small craft.</summary>
        public const double MinCapitalKm = 0.4, MaxSmallCraftKm = 0.2;

        public static readonly RangeSetting Damage = new RangeSetting("dogfight.weakPointDamage", SettingGroups.Dogfighting,
            "Weak point damage", 1, 0, 3, 0.25,
            "How much harder a small craft hits a capital ship's exposed reactor, engines and shield emitters: 0 takes the bonus away, 1 is as designed.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "reactor engines shield attack run bomber dogfight");

        public static readonly ToggleSetting AiGoesForThem = new ToggleSetting("dogfight.aiWeakPoints", SettingGroups.Dogfighting,
            "AI bombers go for weak points", true,
            "Carrier bombers fly their runs onto a capital ship's exposed reactor, engines and shield emitters, as the player can.",
            "bomber torpedo reactor attack run dogfight");

        static WeakPoints()
        {
            Register(new WeakPointSpec(SubsystemKind.Reactor, "reactor vent", new DVec3(0, -0.6, -0.8), 55, 4, 3));
            Register(new WeakPointSpec(SubsystemKind.ShieldGenerator, "shield emitter", new DVec3(0, 1, 0), 60, 3, 2));
            Register(new WeakPointSpec(SubsystemKind.Engine, "engine bells", new DVec3(0, 0, -1), 50, 2.5, 1));
        }

        public static void Register(WeakPointSpec spec) => Specs[spec.Kind] = spec;

        public static WeakPointSpec SpecOf(SubsystemKind kind) => Specs.TryGetValue(kind, out var s) ? s : null;

        /// <summary>A hull big enough to have weak points worth an attack run.</summary>
        public static bool IsCapital(BattleUnit u) => u != null && u.Kind != UnitKind.Fighter && u.LengthKm >= MinCapitalKm;

        /// <summary>A craft small enough to get in close to a weak point: any launched craft, or a fighter-sized hull.</summary>
        public static bool IsSmallCraft(BattleUnit u) => u != null && (u.Kind == UnitKind.Fighter || u.LengthKm <= MaxSmallCraftKm);

        /// <summary>The world direction <paramref name="spec"/> is open toward on <paramref name="target"/>.</summary>
        public static DVec3 Axis(BattleUnit target, WeakPointSpec spec)
        {
            target.Basis(out var right, out var up);
            var a = spec.LocalAxis;
            return (right * a.X + up * a.Y + target.Forward * a.Z).Normalized;
        }

        /// <summary>Whether a shot travelling along <paramref name="incoming"/> strikes <paramref name="sub"/> from its open side.</summary>
        public static bool Exposed(BattleUnit target, Subsystem sub, DVec3 incoming)
        {
            if (sub == null || sub.Destroyed || !IsCapital(target) || incoming.LengthSquared < 1e-18) return false;
            var spec = SpecOf(sub.Kind);
            if (spec == null) return false;
            return DVec3.Dot(-incoming.Normalized, Axis(target, spec)) >= Math.Cos(spec.ConeDeg * Astro.Deg2Rad);
        }

        /// <summary>Whether <paramref name="sub"/> lies open to a craft at <paramref name="from"/>.</summary>
        public static bool ExposedFrom(BattleUnit target, Subsystem sub, DVec3 from) =>
            sub != null && Exposed(target, sub, target.PointOf(sub.Def) - from);

        /// <summary>
        /// What a small craft's hit on <paramref name="sub"/> travelling along <paramref name="incoming"/> does to what
        /// gets through the shields: the weak point's multiplier (scaled by <see cref="Damage"/>) from its open side, else 1.
        /// </summary>
        public static double Factor(BattleUnit target, Subsystem sub, DVec3 incoming)
        {
            if (!Exposed(target, sub, incoming)) return 1;
            return Math.Max(1, 1 + (SpecOf(sub.Kind).Multiplier - 1) * Damage.Value);
        }

        /// <summary><paramref name="target"/>'s live weak points, the most telling first.</summary>
        public static List<Subsystem> On(BattleUnit target)
        {
            var list = new List<Subsystem>();
            if (!IsCapital(target)) return list;
            foreach (var s in target.Combat.Subsystems)
                if (!s.Destroyed && SpecOf(s.Kind) != null) list.Add(s);
            list.Sort((a, b) =>
            {
                int p = SpecOf(b.Kind).Priority.CompareTo(SpecOf(a.Kind).Priority);
                return p != 0 ? p : a.Index.CompareTo(b.Index);
            });
            return list;
        }

        /// <summary>The weak point a run should go for: the most telling one still standing (a shield emitter whose field is
        /// already down is passed over while anything else is left), or null.</summary>
        public static Subsystem Preferred(BattleUnit target)
        {
            Subsystem fallback = null;
            foreach (var s in On(target))
            {
                if (s.Kind == SubsystemKind.ShieldGenerator && s.Shield <= 0)
                {
                    fallback = fallback ?? s;
                    continue;
                }
                return s;
            }
            return fallback;
        }

        /// <summary>The most telling weak point open to a craft at <paramref name="from"/>, or null if none is.</summary>
        public static Subsystem BestFrom(BattleUnit target, DVec3 from)
        {
            foreach (var s in On(target))
                if (ExposedFrom(target, s, from)) return s;
            return null;
        }

        /// <summary>The next (or previous) weak point on <paramref name="target"/> after <paramref name="current"/>, wrapping.</summary>
        public static Subsystem Cycle(BattleUnit target, Subsystem current, int step)
        {
            var list = On(target);
            if (list.Count == 0) return null;
            int i = current == null ? -1 : list.IndexOf(current);
            if (i < 0) return step >= 0 ? list[0] : list[list.Count - 1];
            return list[((i + step) % list.Count + list.Count) % list.Count];
        }

        public static string Label(Subsystem s) => s == null ? null : SpecOf(s.Kind)?.Label ?? SubsystemDef.Name(s.Kind).ToLowerInvariant();
    }

    // ---------------------------------------------------------------- strike torpedoes

    /// <summary>
    /// A bomber's torpedoes against a capital ship (U.SPC-T484, docs/SHIP_ROSTER.md: "bombers knock the shields down first
    /// and then torpedo"). A shield soaks a torpedo like any other hit, but one that arrives where the capital's shield
    /// on that side is already down meets bare hull with a shaped warhead and does <see cref="BareHullDamage"/> times its
    /// damage. Small craft only (a capital's own torpedo tubes are not strike torpedoes), and only on a capital.
    /// </summary>
    [SettingsSource]
    public static class StrikeTorpedoes
    {
        public static readonly RangeSetting BareHullDamage = new RangeSetting("dogfight.torpedoBareHull", SettingGroups.Dogfighting,
            "Torpedoes on a bare hull", 3, 1, 6, 0.25,
            "How much harder a bomber's torpedo hits a capital ship whose shield on that side is already down: bombers knock the shields down, then torpedo. 1 takes the bonus away.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "bomber torpedo shields capital dogfight");

        /// <summary>Whether <paramref name="m"/> is a strike craft's torpedo.</summary>
        public static bool IsStrike(Missile m) => m != null && m.Kind == MissileKind.Torpedo && m.SmallCraft;

        /// <summary>The warhead's factor on <paramref name="target"/> when the shield it came in through is
        /// <paramref name="shieldDown"/>: <see cref="BareHullDamage"/> on a capital's bare hull, else 1.</summary>
        public static double Factor(BattleUnit target, bool shieldDown) =>
            shieldDown && WeakPoints.IsCapital(target) ? Math.Max(1, BareHullDamage.Value) : 1;

        /// <summary>Whether the field over the patch of <paramref name="target"/>'s hull facing a craft at <paramref name="from"/>
        /// is down (or its emitter is shot away, or it has none, U.SPC-T501): where a bomber's torpedo does its work.</summary>
        public static bool ShieldDownToward(BattleUnit target, DVec3 from)
        {
            var g = target.Combat.EmitterFor(target.ShieldStrike(from));
            return g == null || g.Shield <= 0;
        }
    }

    // ---------------------------------------------------------------- wingmen

    /// <summary>What a squadron's lead has told its wingmen to do (U.SPC-T424).</summary>
    public enum WingOrder
    {
        /// <summary>Hold the vee on the lead and fire on its target (what a squadron does on its own).</summary>
        FormUp,

        /// <summary>Break off and fly runs of their own on the lead's target.</summary>
        AttackMyTarget,

        /// <summary>Go after whatever hostile is on the lead or closing on it, then form up again.</summary>
        CoverMe,

        /// <summary>Each picks and fights its own target.</summary>
        EngageAtWill,
    }

    /// <summary>A squadron's wingmen and the orders its lead gives them (U.SPC-T424).</summary>
    public static class Wing
    {
        /// <summary>How near the lead a hostile must be for a wingman covering it to go after it, km.</summary>
        public const double CoverKm = 4;

        /// <summary>The craft of <paramref name="lead"/>'s squadron still flying, but for the lead itself.</summary>
        public static List<BattleUnit> Members(Battle b, BattleUnit lead)
        {
            var list = new List<BattleUnit>();
            var f = lead?.Fighter;
            if (f == null) return list;
            foreach (var o in b.Units)
                if (o != lead && o.Present && o.Kind == UnitKind.Fighter && o.Fighter != null && o.Fighter.Carrier == f.Carrier && o.Fighter.Squadron == f.Squadron)
                    list.Add(o);
            return list;
        }

        /// <summary>
        /// <paramref name="lead"/> takes its squadron and gives it <paramref name="order"/>: every wingman flies on it from
        /// now on, under that order. Returns how many heard it.
        /// </summary>
        public static int Command(Battle b, BattleUnit lead, WingOrder order)
        {
            if (lead?.Fighter == null) return 0;
            var members = Members(b, lead);
            lead.Fighter.Lead = null;
            foreach (var m in members)
            {
                var f = m.Fighter;
                f.Lead = lead;
                if (f.Order != order && f.Phase != FighterPhase.Return)
                {
                    // A fresh start on the new order, not the tail of an old run.
                    f.Phase = FighterPhase.Approach;
                    f.PhaseTime = 0;
                    f.RunEntry = DVec3.Zero;
                }
                f.Order = order;
            }
            lead.Fighter.Order = order;
            return members.Count;
        }

        /// <summary>The hostile a wingman covering <paramref name="lead"/> goes after: the nearest armed one within
        /// <see cref="CoverKm"/> of it, one behind it first; null if the lead is clear.</summary>
        public static BattleUnit Threat(Battle b, BattleUnit lead)
        {
            BattleUnit best = null;
            double bestScore = double.MaxValue;
            foreach (var o in b.Units)
            {
                if (!b.Hostile(lead, o) || o.Surrendering || o.Captured || !o.Combat.Armed) continue;
                var to = o.Pos - lead.Pos;
                double d = to.Length;
                if (d > CoverKm || d < 1e-9) continue;
                // On its tail counts double.
                double score = DVec3.Dot(lead.Forward, to / d) < -0.3 ? d * 0.5 : d;
                if (o.Kind != UnitKind.Fighter) score += CoverKm;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }
            return best;
        }

        public static string Label(WingOrder o)
        {
            switch (o)
            {
                case WingOrder.AttackMyTarget: return "attack my target";
                case WingOrder.CoverMe: return "cover me";
                case WingOrder.EngageAtWill: return "engage at will";
                default: return "form up";
            }
        }
    }
}
