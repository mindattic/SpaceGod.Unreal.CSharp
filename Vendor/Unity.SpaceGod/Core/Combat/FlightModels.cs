using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The families of craft that fly by rules of their own at the helm (U.SPC-T17, see
    /// docs/FLIGHT_MODELS.md). A family is a way of flying, not a hull: every <see cref="ShipClass"/>
    /// belongs to exactly one (<see cref="FlightModelRegistry.FamilyOf"/>), and each family has one
    /// <see cref="IFlightModel"/>. This is where the fighter lineup splits into light and heavy.
    /// </summary>
    public enum FlightFamily
    {
        /// <summary>Star Conflict-style dogfighters (the Fighter, the space superiority workhorse): the track carves
        /// after the nose, strafing is strong, and a turn is tightest at the corner speed.</summary>
        LightFighter,

        /// <summary>Everspace 2-style heavy fighters (U.SPC-T441, <see cref="HeavyFighterFlightModel"/>): the Gunship (U.SPC-T467).</summary>
        HeavyFighter,

        /// <summary>Freighters and miners: heavy, deliberate haulers (U.SPC-T443, <see cref="HaulerFlightModel"/>).</summary>
        Hauler,

        /// <summary>Capital ships (Carrier, Battleship, Cruiser, Destroyer): naval broadside handling.</summary>
        Capital,

        /// <summary>Interceptors (U.SPC-T484): the light fighter's rules pushed further, with more grip, harder strafing
        /// and a turn that holds up at higher speed, so it can run a fighter down and still turn with it.</summary>
        Interceptor,

        /// <summary>Bombers (U.SPC-T484): a steady, heavy torpedo platform, with a wide carve, a weak strafe and a
        /// throttle with heft.</summary>
        Bomber,

        /// <summary>Runners (U.SPC-T444): the Courier, a blockade runner rather than a dogfighter. The light fighter's
        /// rules with little grip and an early corner speed, so it turns on a wide circle, on the highest top speed,
        /// the longest boost and the fastest cruise spool in the game (<see cref="LightFighterHandling.Runner"/>).
        /// Last in the list so the other families keep their numbers.</summary>
        Runner,
    }

    /// <summary>
    /// How one family of craft turns the pilot's input into motion each frame (U.SPC-T17). Every model
    /// flies the same <see cref="PilotState"/> on the same hull (<see cref="HullDynamics"/>) and class
    /// tuning (<see cref="PilotTuning"/>), so a hull handed from one model to another carries on where it
    /// was; what differs is the rules in between. A model keeps no state of its own between frames
    /// (everything that carries is on <see cref="PilotState"/> or <see cref="ShipCombat"/>), so one
    /// instance serves every hull of its family. A new way of flying is a new implementation registered
    /// for its family, never a branch in <see cref="PilotFlight"/>.
    /// </summary>
    public interface IFlightModel
    {
        /// <summary>A short name for the HUD and logs.</summary>
        string Name { get; }

        /// <summary>Advances the hull by <paramref name="dt"/> seconds; see <see cref="PilotFlight.Step"/>
        /// for what each argument means.</summary>
        void Step(ref PilotState s, ShipCombat combat, in HullDynamics hull, in PilotTuning tuning, in PilotInput input, DVec3 gravityKmS2, double dt);
    }

    /// <summary>
    /// The shared Newtonian model every hull flew before U.SPC-T17 (U.SPC-T59, <see cref="PilotFlight.Step"/>),
    /// unchanged: the families with no model of their own yet fly it.
    /// </summary>
    public sealed class NewtonianFlightModel : IFlightModel
    {
        public static readonly NewtonianFlightModel Instance = new NewtonianFlightModel();

        public string Name => "Newtonian";

        public void Step(ref PilotState s, ShipCombat combat, in HullDynamics hull, in PilotTuning tuning, in PilotInput input, DVec3 gravityKmS2, double dt) =>
            PilotFlight.Step(ref s, combat, hull, tuning, input, gravityKmS2, dt);
    }

    /// <summary>What stiffens a light fighter's turn (U.SPC-T440, <see cref="LightFighterHandling.TurnPenalty"/>).</summary>
    public enum TurnPenaltyRule
    {
        /// <summary>Speed alone: the turn tapers from the corner speed down to <see cref="LightFighterHandling.OverspeedTurnFactor"/>
        /// at full boost speed, boost held or not, so a hull still carrying a boost's speed turns wide until it sheds it.</summary>
        Speed,

        /// <summary>Boost held, as a Star Conflict afterburner: the nose swings at <see cref="LightFighterHandling.OverspeedTurnFactor"/>
        /// of its rate while the boost fires and at the full rate at any speed once it is let go.</summary>
        BoostHeld,
    }

    /// <summary>
    /// The light fighter's handling numbers (U.SPC-T17): data, not code, so a tuning pass or the settings
    /// registry (U.SPC-T431) changes them without touching <see cref="LightFighterFlightModel"/>. The game's
    /// light fighters, interceptors and runners read theirs from Settings > Dogfighting (<see cref="LightFighterSettings"/>, U.SPC-T440).
    /// </summary>
    public readonly struct LightFighterHandling
    {
        /// <summary>With flight assist on, how hard the flight computer may push across the nose to pull
        /// the track round after it (the "grip" that makes a turn a carve, not a slide), as a fraction of
        /// the push that holds the track to the nose through the tightest turn at the corner speed
        /// (corner speed times full turn rate): at 1 the track sticks to the nose at or below the corner
        /// speed and slides wide above it. The Newtonian model is held to the manoeuvre thrusters, a few
        /// percent of that.</summary>
        public readonly double Grip;

        /// <summary>Flight assist's strafe set point as a fraction of the forward one (the Newtonian
        /// model's is <see cref="PilotFlight.StrafeSpeedFraction"/>): a light fighter dodges sideways hard.</summary>
        public readonly double StrafeSpeedFraction;

        /// <summary>The corner speed, as a fraction of <see cref="PilotTuning.MaxSpeed"/>: at or below it
        /// the nose swings at the full <see cref="PilotTuning.TurnRateDegS"/>.</summary>
        public readonly double CornerSpeedFraction;

        /// <summary>The turn rate left at full boost speed (<see cref="PilotTuning.MaxSpeed"/> times
        /// <see cref="PilotTuning.BoostSpeedMultiplier"/>) and beyond, as a fraction of the full rate;
        /// between the corner speed and there it tapers linearly. Boost to get somewhere, ease off to turn.</summary>
        public readonly double OverspeedTurnFactor;

        /// <summary>Whether the turn penalty follows speed or the boost held (U.SPC-T440, see <see cref="TurnPenaltyRule"/>).</summary>
        public readonly TurnPenaltyRule TurnPenalty;

        public LightFighterHandling(double grip, double strafeSpeedFraction, double cornerSpeedFraction, double overspeedTurnFactor,
            TurnPenaltyRule turnPenalty = TurnPenaltyRule.Speed)
        {
            Grip = grip;
            StrafeSpeedFraction = strafeSpeedFraction;
            CornerSpeedFraction = cornerSpeedFraction;
            OverspeedTurnFactor = overspeedTurnFactor;
            TurnPenalty = turnPenalty;
        }

        /// <summary>These numbers with the turn penalty following <paramref name="rule"/>.</summary>
        public LightFighterHandling WithTurnPenalty(TurnPenaltyRule rule) =>
            new LightFighterHandling(Grip, StrafeSpeedFraction, CornerSpeedFraction, OverspeedTurnFactor, rule);

        /// <summary>The first-pass numbers, for the owner's review (docs/FLIGHT_MODELS.md).</summary>
        public static readonly LightFighterHandling Default = new LightFighterHandling(
            grip: 1.0, strafeSpeedFraction: 0.5, cornerSpeedFraction: 0.6, overspeedTurnFactor: 0.6);

        /// <summary>The interceptor's numbers (U.SPC-T484, docs/FLIGHT_MODELS.md): more grip, the hardest strafe in the
        /// game, a higher corner speed and less of the turn lost past it, so it turns with what it runs down.</summary>
        public static readonly LightFighterHandling Interceptor = new LightFighterHandling(
            grip: 1.25, strafeSpeedFraction: 0.6, cornerSpeedFraction: 0.75, overspeedTurnFactor: 0.75);

        /// <summary>The runner's numbers (U.SPC-T444, docs/FLIGHT_MODELS.md, Couriers): little grip, a modest strafe, an
        /// early corner speed and most of the turn lost past it, so at speed it swings wide on a big circle. It outruns a
        /// fight; it does not turn in one.</summary>
        public static readonly LightFighterHandling Runner = new LightFighterHandling(
            grip: 0.6, strafeSpeedFraction: 0.35, cornerSpeedFraction: 0.4, overspeedTurnFactor: 0.45);
    }

    /// <summary>
    /// Light fighters, Star Conflict style (U.SPC-T17): arcade-coupled dogfighting. With flight assist on,
    /// (1) the track carves after the nose -- the computer may push across the nose as hard as
    /// <see cref="LightFighterHandling.Grip"/> allows, so a hard turn at the corner speed leaves no
    /// slide and a faster one only a little; (2) strafing is strong (<see cref="LightFighterHandling.StrafeSpeedFraction"/>); and (3) the
    /// nose swings fastest at or below the corner speed and stiffens above it, down to
    /// <see cref="LightFighterHandling.OverspeedTurnFactor"/> of the full rate at full boost, so speed is
    /// something to manage in a fight (or, on <see cref="TurnPenaltyRule.BoostHeld"/>, only while the boost fires).
    /// With flight assist off the hull is the Newtonian one, unchanged: the drift and the turn-and-burn stay a
    /// pilot's tool. The player and the crews in a battle's dogfight (<see cref="Battle"/>, U.SPC-T274, U.SPC-T440)
    /// fly the same model.
    /// </summary>
    public sealed class LightFighterFlightModel : IFlightModel
    {
        readonly Func<LightFighterHandling> handling;

        /// <summary>The numbers it flies on now: fixed, or read from the settings each frame (<see cref="LightFighterSettings"/>).</summary>
        public LightFighterHandling Handling => handling();

        public LightFighterFlightModel() : this(LightFighterHandling.Default) { }

        public LightFighterFlightModel(LightFighterHandling handling, string name = "Light fighter")
            : this(() => handling, name) { }

        /// <summary>A model that reads its numbers from <paramref name="handling"/> each frame, so a setting changed in the
        /// menu takes hold at once (U.SPC-T440).</summary>
        public LightFighterFlightModel(Func<LightFighterHandling> handling, string name = "Light fighter")
        {
            this.handling = handling ?? throw new ArgumentNullException(nameof(handling));
            Name = name;
        }

        public string Name { get; }

        /// <summary>The fastest the nose may swing at <paramref name="speedKmS"/> with the boost not firing, deg/s (see the class remarks).</summary>
        public double TurnRateAt(double speedKmS, in PilotTuning tuning) => TurnRateAt(speedKmS, tuning, false);

        /// <summary>The fastest the nose may swing at <paramref name="speedKmS"/>, deg/s, the boost firing or not
        /// (<paramref name="boosting"/>; it counts only on <see cref="TurnPenaltyRule.BoostHeld"/>).</summary>
        public double TurnRateAt(double speedKmS, in PilotTuning tuning, bool boosting) => TurnRateAt(Handling, speedKmS, tuning, boosting);

        static double TurnRateAt(in LightFighterHandling h, double speedKmS, in PilotTuning tuning, bool boosting)
        {
            if (h.TurnPenalty == TurnPenaltyRule.BoostHeld)
                return boosting ? tuning.TurnRateDegS * h.OverspeedTurnFactor : tuning.TurnRateDegS;
            double corner = tuning.MaxSpeed * h.CornerSpeedFraction;
            double top = tuning.MaxSpeed * Math.Max(1, tuning.BoostSpeedMultiplier);
            if (speedKmS <= corner || top <= corner) return tuning.TurnRateDegS;
            double t = Math.Min(1, (speedKmS - corner) / (top - corner));
            return tuning.TurnRateDegS * (1 - t * (1 - h.OverspeedTurnFactor));
        }

        /// <summary>The most the flight computer pushes across the nose with assist on, km/s² (see
        /// <see cref="LightFighterHandling.Grip"/>).</summary>
        public double GripAccel(in PilotTuning tuning) => GripAccel(Handling, tuning);

        static double GripAccel(in LightFighterHandling h, in PilotTuning tuning) =>
            h.Grip * tuning.MaxSpeed * h.CornerSpeedFraction * tuning.TurnRateDegS * Astro.Deg2Rad;

        public void Step(ref PilotState s, ShipCombat combat, in HullDynamics hull, in PilotTuning tuning, in PilotInput input, DVec3 gravityKmS2, double dt)
        {
            if (input.FlightAssistOff)
            {
                PilotFlight.Step(ref s, combat, hull, tuning, input, gravityKmS2, dt);
                return;
            }
            if (dt <= 0) return;

            var h = Handling;
            // Firing this frame: held, with charge left (as PilotFlight.DriveMultiplier has it).
            bool boosting = input.Boost && tuning.BoostDurationS > 0 && combat != null && combat.BoostFraction > 0;
            var turning = new PilotTuning(tuning.MaxSpeed, TurnRateAt(h, s.Vel.Length, tuning, boosting), tuning.BoostSpeedMultiplier,
                tuning.BoostDurationS, tuning.CruiseSpeedMultiplier, tuning.CruiseRampS);
            var forwardBeforeTurn = s.Forward;
            PilotFlight.Rotate(ref s, hull, turning, input, dt);
            double driveMult = PilotFlight.DriveMultiplier(ref s, combat, tuning, input, forwardBeforeTurn, dt);

            double grip = GripAccel(h, tuning);
            var thrust = PilotFlight.AssistThrust(s, hull, tuning, input, gravityKmS2, driveMult, dt, h.StrafeSpeedFraction,
                Math.Max(hull.LateralAccel, grip), Math.Max(hull.VerticalAccel, grip));
            PilotFlight.Integrate(ref s, thrust, gravityKmS2, dt);
        }
    }

    /// <summary>
    /// The light fighter's handling in Settings > Dogfighting (U.SPC-T440, the settings registry of U.SPC-T431): grip, strafe,
    /// corner speed and the turn left at full boost for each family flying <see cref="LightFighterFlightModel"/> (the light
    /// fighter, the interceptor and the runner), defaulting to their own <see cref="LightFighterHandling"/>, and one choice of
    /// what stiffens the turn, shared by all three. The game's registry reads them every frame, so a change takes hold in
    /// the player's hands and the crews' at once.
    /// </summary>
    [SettingsSource]
    public static class LightFighterSettings
    {
        /// <summary>The <see cref="TurnPenaltyRule"/> options, in its order.</summary>
        static readonly string[] TurnPenaltyOptions = { "By speed", "While boosting" };

        public static readonly ChoiceSetting TurnPenalty = new ChoiceSetting("flight.lightFighter.turnPenalty", SettingGroups.Dogfighting,
            "Light fighters: what stiffens the turn", TurnPenaltyOptions, (int)TurnPenaltyRule.Speed,
            "By speed: past the corner speed the turn tapers off toward full boost speed, boost held or not. While boosting: the turn stiffens only while the boost fires (a Star Conflict afterburner) and is full at any speed once it is let go. Fighters, interceptors and couriers alike.",
            "flight model handling light fighter interceptor courier runner boost afterburner turn corner");

        /// <summary>The rule <see cref="TurnPenalty"/> names.</summary>
        public static TurnPenaltyRule Rule => (TurnPenaltyRule)TurnPenalty.Index;

        /// <summary>One family's four numbers.</summary>
        public sealed class Family
        {
            public readonly RangeSetting Grip, Strafe, CornerSpeed, OverspeedTurn;

            internal Family(string key, string label, string keywords, LightFighterHandling defaults)
            {
                string k = "flight." + key + ".";
                string words = "flight model handling " + keywords;
                Grip = new RangeSetting(k + "grip", SettingGroups.Dogfighting, label + ": grip", defaults.Grip, 0.25, 2, 0.05,
                    "How hard flight assist pulls the track round after the nose. At 1 the track holds to the nose through the tightest turn at the corner speed; less slides wide, more carves tighter.",
                    Times, words + " grip carve slide");
                Strafe = new RangeSetting(k + "strafe", SettingGroups.Dogfighting, label + ": strafe", defaults.StrafeSpeedFraction, 0.1, 1, 0.05,
                    "How fast it strafes, as a share of the forward speed it is set to.",
                    Percent, words + " strafe dodge sideways");
                CornerSpeed = new RangeSetting(k + "cornerSpeed", SettingGroups.Dogfighting, label + ": corner speed", defaults.CornerSpeedFraction, 0.2, 1, 0.05,
                    "The speed, as a share of top speed, at or below which the nose swings at its full rate; above it the turn stiffens (when the turn stiffens by speed).",
                    Percent, words + " corner speed turn");
                OverspeedTurn = new RangeSetting(k + "overspeedTurn", SettingGroups.Dogfighting, label + ": turn at full boost", defaults.OverspeedTurnFactor, 0.2, 1, 0.05,
                    "The share of the turn rate left at full boost speed (or while boosting, when the turn stiffens only then). 1 takes the penalty away.",
                    Percent, words + " boost afterburner turn penalty");
            }

            /// <summary>The numbers as set now, with the shared turn penalty rule.</summary>
            public LightFighterHandling Handling =>
                new LightFighterHandling(Grip.Value, Strafe.Value, CornerSpeed.Value, OverspeedTurn.Value, Rule);
        }

        /// <summary>The Fighter's (<see cref="FlightFamily.LightFighter"/>).</summary>
        public static readonly Family LightFighter = new Family("lightFighter", "Light fighters", "light fighter space superiority", LightFighterHandling.Default);

        /// <summary>The interceptor's and the recon fighter's (<see cref="FlightFamily.Interceptor"/>).</summary>
        public static readonly Family Interceptor = new Family("interceptor", "Interceptors", "interceptor recon", LightFighterHandling.Interceptor);

        /// <summary>The Courier's (<see cref="FlightFamily.Runner"/>).</summary>
        public static readonly Family Runner = new Family("runner", "Couriers", "courier runner blockade", LightFighterHandling.Runner);

        static string Times(double v) => v.ToString("0.##", CultureInfo.InvariantCulture) + "x";

        static string Percent(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>
    /// The bomber's handling numbers (U.SPC-T484): data, not code, like <see cref="LightFighterHandling"/>.
    /// </summary>
    public readonly struct BomberHandling
    {
        /// <summary>With flight assist on, how hard the flight computer may push across the nose, as a fraction of the
        /// push that holds the track to the nose through its tightest turn at top speed (top speed times turn rate):
        /// below 1 the track swings wide of the nose in a hard turn, a carve with a slide in it.</summary>
        public readonly double Grip;

        /// <summary>Flight assist's strafe set point as a fraction of the forward one: a bomber barely sidesteps.</summary>
        public readonly double StrafeSpeedFraction;

        /// <summary>The share of its drive (and retros) flight assist may use to change speed along the nose: the heft
        /// on the throttle, so a bomber gathers and sheds way slowly.</summary>
        public readonly double ThrottleAuthority;

        public BomberHandling(double grip, double strafeSpeedFraction, double throttleAuthority)
        {
            Grip = grip;
            StrafeSpeedFraction = strafeSpeedFraction;
            ThrottleAuthority = throttleAuthority;
        }

        /// <summary>The first-pass numbers, for the owner's review (docs/FLIGHT_MODELS.md).</summary>
        public static readonly BomberHandling Default = new BomberHandling(grip: 0.6, strafeSpeedFraction: 0.25, throttleAuthority: 0.6);
    }

    /// <summary>
    /// Bombers (U.SPC-T484): a steady torpedo platform, not a dogfighter. With flight assist on, (1) the nose turns at
    /// the class's own rate at any speed (no corner speed, so a run in is flown at one steady rate), (2) the track
    /// carves after it only as hard as <see cref="BomberHandling.Grip"/> allows, so a hard turn swings wide, (3) the
    /// strafe is weak (<see cref="BomberHandling.StrafeSpeedFraction"/>) and (4) assist changes speed with only
    /// <see cref="BomberHandling.ThrottleAuthority"/> of the drive, so a bomber is slow to gather way and slow to shed
    /// it. With flight assist off the hull is the Newtonian one, unchanged.
    /// </summary>
    public sealed class BomberFlightModel : IFlightModel
    {
        public BomberHandling Handling { get; }

        public BomberFlightModel() : this(BomberHandling.Default) { }

        public BomberFlightModel(BomberHandling handling) => Handling = handling;

        public string Name => "Bomber";

        /// <summary>The most the flight computer pushes across the nose with assist on, km/s² (see <see cref="BomberHandling.Grip"/>).</summary>
        public double GripAccel(in PilotTuning tuning) => Handling.Grip * tuning.MaxSpeed * tuning.TurnRateDegS * Astro.Deg2Rad;

        public void Step(ref PilotState s, ShipCombat combat, in HullDynamics hull, in PilotTuning tuning, in PilotInput input, DVec3 gravityKmS2, double dt)
        {
            if (input.FlightAssistOff)
            {
                PilotFlight.Step(ref s, combat, hull, tuning, input, gravityKmS2, dt);
                return;
            }
            if (dt <= 0) return;

            var forwardBeforeTurn = s.Forward;
            PilotFlight.Rotate(ref s, hull, tuning, input, dt);
            double driveMult = PilotFlight.DriveMultiplier(ref s, combat, tuning, input, forwardBeforeTurn, dt);

            double grip = GripAccel(tuning);
            double authority = Math.Max(0, Math.Min(1, Handling.ThrottleAuthority));
            var heavy = new HullDynamics(hull.MassKt, hull.MainAccel * authority, hull.RetroAccel * authority, hull.LateralAccel, hull.VerticalAccel,
                hull.PitchYawAngularAccel, hull.RollAngularAccel);
            var thrust = PilotFlight.AssistThrust(s, heavy, tuning, input, gravityKmS2, driveMult, dt, Handling.StrafeSpeedFraction,
                Math.Max(hull.LateralAccel, grip), Math.Max(hull.VerticalAccel, grip));
            PilotFlight.Integrate(ref s, thrust, gravityKmS2, dt);
        }
    }

    /// <summary>
    /// The heavy fighter's handling numbers (U.SPC-T441, docs/FLIGHT_MODELS.md, Heavy fighters): data, not code, like
    /// <see cref="LightFighterHandling"/>, each one a setting for the settings registry (U.SPC-T431) to declare.
    /// </summary>
    public readonly struct HeavyFighterHandling
    {
        /// <summary>The cap on the nose's swing, as a fraction of the light fighter's turn rate (the space superiority
        /// fighter's, <see cref="LightFighterTurnRateDegS"/>): stiff, below any light fighter. A class tuned slower still keeps
        /// its own rate.</summary>
        public readonly double TurnRateCapFraction;

        /// <summary>Seconds for the nose to spin up from still to its top swing (and to stop from it): short, so the nose
        /// starts and stops crisply. A hull whose own attitude thrusters spin it faster keeps its own.</summary>
        public readonly double SpinUpS;

        /// <summary>With flight assist on, seconds for the hull to settle (to within 5%) on a new set point, forward,
        /// sideways or vertically: a short ease, whatever its thrusters, so a change of speed reads as weight without being slow.</summary>
        public readonly double SettleS;

        /// <summary>Flight assist's strafe and vertical set points as a fraction of the forward one.</summary>
        public readonly double StrafeSpeedFraction;

        /// <summary>Seconds over which, once boost is let go, assist bleeds the boost's extra speed off (the whole of
        /// it, from full boost speed down to top speed), instead of braking hard: a boosted run carries through the target.</summary>
        public readonly double BoostBleedS;

        public HeavyFighterHandling(double turnRateCapFraction, double spinUpS, double settleS, double strafeSpeedFraction, double boostBleedS)
        {
            TurnRateCapFraction = turnRateCapFraction;
            SpinUpS = spinUpS;
            SettleS = settleS;
            StrafeSpeedFraction = strafeSpeedFraction;
            BoostBleedS = boostBleedS;
        }

        /// <summary>The light fighter's turn rate the cap is a share of, deg/s: the space superiority fighter's.</summary>
        public static double LightFighterTurnRateDegS => PilotTuning.Of(ShipClass.Fighter).TurnRateDegS;

        /// <summary>The first-pass numbers, for the owner's review (docs/FLIGHT_MODELS.md).</summary>
        public static readonly HeavyFighterHandling Default = new HeavyFighterHandling(
            turnRateCapFraction: 0.7, spinUpS: 0.1, settleS: 0.5, strafeSpeedFraction: 0.45, boostBleedS: 2);
    }

    /// <summary>
    /// Heavy fighters, Everspace 2 style (U.SPC-T441): always coupled and very responsive, heavy without being sluggish.
    /// With flight assist on, (1) the nose turns at one rate at any speed (no corner speed), capped below a light
    /// fighter's (<see cref="HeavyFighterHandling.TurnRateCapFraction"/>), and spins up and stops hard
    /// (<see cref="HeavyFighterHandling.SpinUpS"/>), so the aim is "on rails"; (2) assist eases onto its set point over
    /// <see cref="HeavyFighterHandling.SettleS"/>, forward, sideways and vertically alike, so the hull goes where it
    /// points and a change of speed has heft; (3) the strafe and vertical set points are
    /// <see cref="HeavyFighterHandling.StrafeSpeedFraction"/> of the forward one, reached as fast as forward speed, for
    /// circle-strafing; and (4) speed above the set point (a boost let go) bleeds off over
    /// <see cref="HeavyFighterHandling.BoostBleedS"/> rather than being braked away, so a boosted run carries through.
    /// With flight assist off the hull is the Newtonian one, unchanged.
    /// </summary>
    public sealed class HeavyFighterFlightModel : IFlightModel
    {
        public HeavyFighterHandling Handling { get; }

        public HeavyFighterFlightModel() : this(HeavyFighterHandling.Default) { }

        public HeavyFighterFlightModel(HeavyFighterHandling handling) => Handling = handling;

        public string Name => "Heavy fighter";

        /// <summary>The nose's top swing, deg/s, at any speed: the class's own, capped below a light fighter's.</summary>
        public double TurnRateDegS(in PilotTuning tuning) =>
            Math.Min(tuning.TurnRateDegS, Handling.TurnRateCapFraction * HeavyFighterHandling.LightFighterTurnRateDegS);

        /// <summary>The angular acceleration the nose swings with, rad/s²: enough to reach the top swing in
        /// <see cref="HeavyFighterHandling.SpinUpS"/>, or the hull's own if that is more.</summary>
        public double TurnAlpha(in HullDynamics hull, in PilotTuning tuning) =>
            Math.Max(hull.PitchYawAngularAccel, TurnRateDegS(tuning) * Astro.Deg2Rad / Math.Max(1e-3, Handling.SpinUpS));

        /// <summary>The time constant of assist's ease onto its set point, s: a first-order ease is within 5% after three.</summary>
        public double EaseTimeConstantS => Math.Max(1e-3, Handling.SettleS) / 3;

        /// <summary>The most assist slows a hull running above its set point, km/s² (the boost bleed): the boost's extra
        /// speed over <see cref="HeavyFighterHandling.BoostBleedS"/>, and never less than the retros give.</summary>
        public double BleedAccel(in HullDynamics hull, in PilotTuning tuning)
        {
            double extra = tuning.MaxSpeed * (Math.Max(1, tuning.BoostSpeedMultiplier) - 1);
            double bleed = Handling.BoostBleedS > 0 ? extra / Handling.BoostBleedS : double.PositiveInfinity;
            return Math.Max(hull.RetroAccel, bleed);
        }

        /// <summary>How far the stick's aim may run ahead of the nose, radians (as <see cref="PilotFlight.AimLeadRad"/>, for
        /// this model's own swing and spin-up).</summary>
        public double AimLeadRad(in HullDynamics hull, in PilotTuning tuning)
        {
            double rate = TurnRateDegS(tuning) * Astro.Deg2Rad;
            double brake = rate * rate / (2 * TurnAlpha(hull, tuning));
            return Math.Min(Math.PI, Math.Max(PilotFlight.MinAimLeadDeg * Astro.Deg2Rad, brake));
        }

        public void Step(ref PilotState s, ShipCombat combat, in HullDynamics hull, in PilotTuning tuning, in PilotInput input, DVec3 gravityKmS2, double dt)
        {
            if (input.FlightAssistOff)
            {
                PilotFlight.Step(ref s, combat, hull, tuning, input, gravityKmS2, dt);
                return;
            }
            if (dt <= 0) return;

            var stiff = new HullDynamics(hull.MassKt, hull.MainAccel, hull.RetroAccel, hull.LateralAccel, hull.VerticalAccel,
                TurnAlpha(hull, tuning), hull.RollAngularAccel);
            var forwardBeforeTurn = s.Forward;
            PilotFlight.Rotate(ref s, stiff, TurnRateDegS(tuning), tuning.RollRateDegS, input, dt);
            double driveMult = PilotFlight.DriveMultiplier(ref s, combat, tuning, input, forwardBeforeTurn, dt);

            var f = s.Forward;
            var r = s.Right;
            var u = s.Up;
            double setPoint = tuning.MaxSpeed * driveMult;
            double strafe = setPoint * Handling.StrafeSpeedFraction;
            var commanded = f * (Clamp(input.Throttle) * setPoint) + r * (Clamp(input.StrafeRight) * strafe) + u * (Clamp(input.StrafeUp) * strafe);

            // The ease: every axis closes on its set point with one time constant (gravity cancelled as it goes).
            var ease = (commanded - s.Vel) / Math.Max(dt, EaseTimeConstantS) - gravityKmS2;
            double along = DVec3.Dot(s.Vel, f);
            double fwd = DVec3.Dot(ease, f);
            // The boost bleed: speed above the set point is shed no faster than the bleed, down to the set point, and the
            // ease takes over from there.
            if (along > setPoint)
            {
                double shed = Math.Max(-(along - setPoint) / dt, -BleedAccel(hull, tuning)) - DVec3.Dot(gravityKmS2, f);
                fwd = Math.Max(fwd, shed);
            }
            var thrust = f * fwd + r * DVec3.Dot(ease, r) + u * DVec3.Dot(ease, u);
            PilotFlight.Integrate(ref s, thrust, gravityKmS2, dt);
        }

        static double Clamp(double x) => Math.Max(-1, Math.Min(1, x));
    }

    /// <summary>
    /// A capital ship's engine telegraph (U.SPC-T499): the stepped speeds its engines are rung to, which hold
    /// until rung again. Flank is the boost, and drains the boost charge.
    /// </summary>
    public enum EngineTelegraph
    {
        FullAstern,
        Stop,
        Slow,
        Half,
        Full,
        Flank,
    }

    /// <summary>
    /// A capital ship's handling numbers (U.SPC-T499, docs/FLIGHT_MODELS.md, Capital ships): data, not code, with a
    /// <see cref="Default"/> and per-class overrides (<see cref="Of"/>). The turn itself is not a number here: it is fitted
    /// to <see cref="ComeAboutS"/> (<see cref="CapitalFlightModel.PeakTurnRateDegS"/>), so the come-about is what the
    /// numbers promise.
    /// </summary>
    public readonly struct CapitalHandling
    {
        /// <summary>The telegraph's speeds as fractions of <see cref="PilotTuning.MaxSpeed"/> (Full is 1, Flank the boost
        /// on top of it, Stop 0): Full Astern (astern, so the hull backs at this share), Slow and Half.</summary>
        public readonly double FullAsternFraction, SlowFraction, HalfFraction;

        /// <summary>How the engines answer a new step: the time constant, s, with which the hull eases toward the step's
        /// speed, as fast as its drive and retros allow.</summary>
        public readonly double SpoolS;

        /// <summary>Steerage, as fractions of the peak turn rate (which is at Half): a slow thruster pivot at Stop, what is
        /// left at Full, and at Flank speed and beyond. Between them it runs linearly with the hull's speed.</summary>
        public readonly double PivotFactor, FullFactor, FlankFactor;

        /// <summary>The keel: with flight assist on, the most the hull pushes across its nose to kill slip, as a fraction
        /// of top speed times the peak turn rate. Enough to trace a turning circle at Half and Full; a little slip shows at
        /// Flank.</summary>
        public readonly double Keel;

        /// <summary>The pitch rate as a fraction of the yaw rate (a turn between them blends the two); 1 swings the nose
        /// equally readily every way, as nothing holds a capital to a plane.</summary>
        public readonly double PitchFactor;

        /// <summary>Flight assist's roll rate as a fraction of the peak turn rate.</summary>
        public readonly double RollFactor;

        /// <summary>The come-about: seconds for a 180 deg turn at Half, from a steady heading to a steady heading.</summary>
        public readonly double ComeAboutS;

        public CapitalHandling(double fullAsternFraction, double slowFraction, double halfFraction, double spoolS, double pivotFactor,
            double fullFactor, double flankFactor, double keel, double pitchFactor, double rollFactor, double comeAboutS)
        {
            FullAsternFraction = fullAsternFraction;
            SlowFraction = slowFraction;
            HalfFraction = halfFraction;
            SpoolS = spoolS;
            PivotFactor = pivotFactor;
            FullFactor = fullFactor;
            FlankFactor = flankFactor;
            Keel = keel;
            PitchFactor = pitchFactor;
            RollFactor = rollFactor;
            ComeAboutS = comeAboutS;
        }

        /// <summary>These numbers with the ones given changed.</summary>
        public CapitalHandling With(double? halfFraction = null, double? spoolS = null, double? pivotFactor = null, double? fullFactor = null,
            double? flankFactor = null, double? comeAboutS = null) =>
            new CapitalHandling(FullAsternFraction, SlowFraction, halfFraction ?? HalfFraction, spoolS ?? SpoolS, pivotFactor ?? PivotFactor,
                fullFactor ?? FullFactor, flankFactor ?? FlankFactor, Keel, PitchFactor, RollFactor, comeAboutS ?? ComeAboutS);

        /// <summary>The first-pass numbers, for the owner's review (docs/FLIGHT_MODELS.md), with a cruiser's come-about.</summary>
        public static readonly CapitalHandling Default = new CapitalHandling(
            fullAsternFraction: 0.4, slowFraction: 0.25, halfFraction: 0.5, spoolS: 4, pivotFactor: 0.25,
            fullFactor: 0.7, flankFactor: 0.6, keel: 0.75, pitchFactor: 1, rollFactor: 1, comeAboutS: 10);

        static readonly Dictionary<ShipClass, CapitalHandling> ByClass = new Dictionary<ShipClass, CapitalHandling>
        {
            [ShipClass.Carrier] = Default.With(comeAboutS: 18, spoolS: 6),
            [ShipClass.Battleship] = Default.With(comeAboutS: 14, spoolS: 5),
            [ShipClass.Cruiser] = Default.With(comeAboutS: 10, spoolS: 4),
            // The skirmisher (as in Battle.BroadsideFacing): fights slower for its pace, so its circle is the tightest of the
            // line, and keeps more of its turn at Full and Flank to cut across a battleship's bow.
            [ShipClass.Destroyer] = Default.With(comeAboutS: 6, spoolS: 2.5, halfFraction: 0.35, pivotFactor: 0.35, fullFactor: 0.85, flankFactor: 0.7),
            // The battlecruiser, between the cruiser and the battleship.
            [ShipClass.CommandCruiser] = Default.With(comeAboutS: 12, spoolS: 4.5),
            // The frigate: the fastest warship hull, quick to flip end for end.
            [ShipClass.Frigate] = Default.With(comeAboutS: 5, spoolS: 2.5, fullFactor: 0.8),
            // The gate breaker (U.SPC-T478): heavier than a battleship for its length, the slowest to bring about and spool.
            [ShipClass.GateBreaker] = Default.With(comeAboutS: 16, spoolS: 5.5),
        };

        /// <summary>The handling of a capital of class <paramref name="c"/>; <see cref="Default"/> for one with none of its own.</summary>
        public static CapitalHandling Of(ShipClass c) => ByClass.TryGetValue(c, out var h) ? h : Default;

        /// <summary>The classes with handling of their own.</summary>
        public static IEnumerable<ShipClass> Classes => ByClass.Keys;
    }

    /// <summary>
    /// Capital ships (U.SPC-T499, docs/FLIGHT_MODELS.md): Black Flag's handling, in full 3D. With flight assist on,
    /// (1) the engine telegraph sets the speed: it is rung a step at a time (<see cref="PilotInput.TelegraphStep"/>) and
    /// holds, and the hull eases toward each step's speed (<see cref="CapitalHandling.SpoolS"/>); Flank is the boost, and
    /// rings itself down to Full once the charge is spent; (2) steerage: the nose swings fastest at Half, less at Full and
    /// Flank, and only pivots slowly at Stop; (3) the keel kills slip across the hull, so a turn traces a turning circle;
    /// and (4) nothing holds the hull to a plane or levels it: pitch, yaw and roll are all free, slow and heavy, so a
    /// capital climbs, dives and rolls to bring a broadside to bear above or below. The turn is fitted to the class's
    /// come-about (<see cref="CapitalHandling.ComeAboutS"/>). With flight assist off the hull is the Newtonian one, unchanged.
    /// </summary>
    public sealed class CapitalFlightModel : IFlightModel
    {
        public CapitalHandling Handling { get; }

        public CapitalFlightModel() : this(CapitalHandling.Default) { }

        public CapitalFlightModel(CapitalHandling handling) => Handling = handling;

        public string Name => "Capital";

        /// <summary>The telegraph rung <paramref name="steps"/> steps ahead (negative: astern), held between Full Astern and Flank.</summary>
        public static EngineTelegraph Ring(EngineTelegraph t, int steps) =>
            (EngineTelegraph)Math.Max((int)EngineTelegraph.FullAstern, Math.Min((int)EngineTelegraph.Flank, (int)t + steps));

        /// <summary>The speed a telegraph step asks for along the nose, km/s (negative astern); Flank's is Full's times the
        /// boost, while the charge lasts.</summary>
        public double SpeedOf(EngineTelegraph t, in PilotTuning tuning)
        {
            switch (t)
            {
                case EngineTelegraph.FullAstern: return -Handling.FullAsternFraction * tuning.MaxSpeed;
                case EngineTelegraph.Stop: return 0;
                case EngineTelegraph.Slow: return Handling.SlowFraction * tuning.MaxSpeed;
                case EngineTelegraph.Half: return Handling.HalfFraction * tuning.MaxSpeed;
                case EngineTelegraph.Full: return tuning.MaxSpeed;
                default: return tuning.MaxSpeed * Math.Max(1, tuning.BoostSpeedMultiplier);
            }
        }

        /// <summary>The step (Full Astern to Full) nearest a hull making <paramref name="forwardSpeedKmS"/> along its nose:
        /// what the telegraph stands at when a hull already under way is first taken in hand.</summary>
        public EngineTelegraph NearestStep(double forwardSpeedKmS, in PilotTuning tuning)
        {
            var best = EngineTelegraph.Stop;
            double bestOff = double.MaxValue;
            for (var t = EngineTelegraph.FullAstern; t <= EngineTelegraph.Full; t++)
            {
                double off = Math.Abs(SpeedOf(t, tuning) - forwardSpeedKmS);
                if (off < bestOff)
                {
                    bestOff = off;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>The angular acceleration the nose swings with, rad/s²: the hull's own, or where its attitude thrusters
        /// could not make the come-about at all, the least the helm needs to (with a quarter to spare).</summary>
        public double TurnAlpha(in HullDynamics hull)
        {
            double t = Math.Max(1e-3, Handling.ComeAboutS);
            return Math.Max(hull.PitchYawAngularAccel, 1.25 * 4 * Math.PI / (t * t));
        }

        /// <summary>The nose's top swing at Half, deg/s: fitted so a 180 deg turn from a steady heading to a steady heading
        /// (spin up at <see cref="TurnAlpha"/>, swing, brake) takes the come-about. With swing rate r and spin-up α the turn
        /// takes π/r + r/α, so r is the smaller root of r² - αTr + απ = 0.</summary>
        public double PeakTurnRateDegS(in HullDynamics hull)
        {
            double a = TurnAlpha(hull), t = Math.Max(1e-3, Handling.ComeAboutS);
            double disc = a * a * t * t - 4 * a * Math.PI;
            double r = (a * t - Math.Sqrt(Math.Max(0, disc))) / 2;
            return r * Astro.Rad2Deg;
        }

        /// <summary>Steerage at <paramref name="speedKmS"/> (either way), as a fraction of the peak turn rate: from the
        /// pivot at Stop up to 1 at Half, down to <see cref="CapitalHandling.FullFactor"/> at Full and
        /// <see cref="CapitalHandling.FlankFactor"/> at Flank speed and beyond.</summary>
        public double SteerageFactor(double speedKmS, in PilotTuning tuning)
        {
            var h = Handling;
            double v = Math.Abs(speedKmS);
            double half = SpeedOf(EngineTelegraph.Half, tuning), full = SpeedOf(EngineTelegraph.Full, tuning), flank = SpeedOf(EngineTelegraph.Flank, tuning);
            if (v <= half) return half > 0 ? h.PivotFactor + (1 - h.PivotFactor) * v / half : 1;
            if (v <= full) return full > half ? 1 + (h.FullFactor - 1) * (v - half) / (full - half) : h.FullFactor;
            if (v < flank) return h.FullFactor + (h.FlankFactor - h.FullFactor) * (v - full) / (flank - full);
            return h.FlankFactor;
        }

        /// <summary>The fastest the nose may swing (yawing) at <paramref name="speedKmS"/>, deg/s.</summary>
        public double TurnRateAt(double speedKmS, in HullDynamics hull, in PilotTuning tuning) =>
            PeakTurnRateDegS(hull) * SteerageFactor(speedKmS, tuning);

        /// <summary>How long a 180 deg turn takes holding <paramref name="speedKmS"/>, s: spin up, swing at the steerage's
        /// rate, brake. At Half it is the come-about (<see cref="CapitalHandling.ComeAboutS"/>).</summary>
        public double ComeAboutSecondsAt(double speedKmS, in HullDynamics hull, in PilotTuning tuning)
        {
            double r = TurnRateAt(speedKmS, hull, tuning) * Astro.Deg2Rad;
            return r <= 0 ? double.PositiveInfinity : Math.PI / r + r / TurnAlpha(hull);
        }

        /// <summary>The most the keel pushes across the nose with assist on, km/s² (see <see cref="CapitalHandling.Keel"/>).</summary>
        public double KeelAccel(in HullDynamics hull, in PilotTuning tuning) =>
            Handling.Keel * tuning.MaxSpeed * PeakTurnRateDegS(hull) * Astro.Deg2Rad;

        /// <summary>How far the helm's heading may run ahead of the nose, radians (as <see cref="PilotFlight.AimLeadRad"/>,
        /// for this model's own swing): enough for the nose to reach its peak rate while a turn key is held.</summary>
        public double AimLeadRad(in HullDynamics hull)
        {
            double rate = PeakTurnRateDegS(hull) * Astro.Deg2Rad;
            double brake = rate * rate / (2 * TurnAlpha(hull));
            return Math.Min(Math.PI, Math.Max(PilotFlight.MinAimLeadDeg * Astro.Deg2Rad, brake));
        }

        public void Step(ref PilotState s, ShipCombat combat, in HullDynamics hull, in PilotTuning tuning, in PilotInput input, DVec3 gravityKmS2, double dt)
        {
            var telegraph = Ring(s.Telegraph ?? NearestStep(DVec3.Dot(s.Vel, s.Forward), tuning), input.TelegraphStep);
            s.Telegraph = telegraph;
            if (input.FlightAssistOff)
            {
                PilotFlight.Step(ref s, combat, hull, tuning, input, gravityKmS2, dt);
                return;
            }
            if (dt <= 0) return;

            // Steerage, and a pitch that may be slower than the yaw: a turn between them blends the two.
            double rate = TurnRateAt(s.Vel.Length, hull, tuning);
            if (input.DesiredForward.LengthSquared > 1e-12 && Handling.PitchFactor > 0 && Handling.PitchFactor != 1)
            {
                var axis = DVec3.Cross(s.Forward, input.DesiredForward.Normalized);
                if (axis.LengthSquared > 1e-18)
                {
                    axis = axis.Normalized;
                    double yaw = DVec3.Dot(axis, s.Up), pitch = DVec3.Dot(axis, s.Right) / Handling.PitchFactor;
                    rate /= Math.Max(1, Math.Sqrt(yaw * yaw + pitch * pitch));
                }
            }
            var turning = new HullDynamics(hull.MassKt, hull.MainAccel, hull.RetroAccel, hull.LateralAccel, hull.VerticalAccel,
                TurnAlpha(hull), hull.RollAngularAccel);
            var forwardBeforeTurn = s.Forward;
            PilotFlight.Rotate(ref s, turning, rate, PeakTurnRateDegS(hull) * Handling.RollFactor, input, dt);

            // The engines: Flank (or the boost held) is the boost, and Full lets the cruise drive spool.
            bool flank = telegraph == EngineTelegraph.Flank || input.Boost;
            var drive = input;
            drive.Boost = flank;
            drive.Throttle = flank || telegraph == EngineTelegraph.Full ? 1 : 0;
            double driveMult = PilotFlight.DriveMultiplier(ref s, combat, tuning, drive, forwardBeforeTurn, dt);
            if (telegraph == EngineTelegraph.Flank && combat.BoostFraction <= 0) s.Telegraph = EngineTelegraph.Full;
            double target = (flank ? tuning.MaxSpeed : SpeedOf(telegraph, tuning)) * driveMult;

            var f = s.Forward;
            var r = s.Right;
            var u = s.Up;
            double along = DVec3.Dot(s.Vel, f);
            double ease = (target - along) / Math.Max(dt, Handling.SpoolS) - DVec3.Dot(gravityKmS2, f);
            double fwd = Math.Max(-hull.RetroAccel, Math.Min(hull.MainAccel * driveMult, ease));

            // The keel: slip across the hull (and gravity across it) is killed as hard as it can push.
            var across = (-(s.Vel - f * along)) / dt - (gravityKmS2 - f * DVec3.Dot(gravityKmS2, f));
            double keel = KeelAccel(hull, tuning);
            var thrust = f * fwd
                + r * Limit(DVec3.Dot(across, r), Math.Max(hull.LateralAccel, keel))
                + u * Limit(DVec3.Dot(across, u), Math.Max(hull.VerticalAccel, keel));
            PilotFlight.Integrate(ref s, thrust, gravityKmS2, dt);
        }

        static double Limit(double x, double max) => Math.Max(-max, Math.Min(max, x));
    }

    /// <summary>
    /// Which family each <see cref="ShipClass"/> flies as, and which model flies each family (U.SPC-T17):
    /// the one extension point for per-class handling. Both tables are data: a new class is assigned a
    /// family, and a new way of flying is a model registered for its family, without touching the code
    /// that flies the others. A class may also fly a model of its own within its family
    /// (<see cref="Register(ShipClass, IFlightModel)"/>): the same rules on its own numbers, as each capital class
    /// flies the capital model on its own <see cref="CapitalHandling"/> (U.SPC-T499). <see cref="Default"/> is the
    /// game's; tests build their own.
    /// </summary>
    public sealed class FlightModelRegistry
    {
        readonly Dictionary<ShipClass, FlightFamily> families = new Dictionary<ShipClass, FlightFamily>();
        readonly Dictionary<FlightFamily, IFlightModel> models = new Dictionary<FlightFamily, IFlightModel>();
        readonly Dictionary<ShipClass, IFlightModel> classModels = new Dictionary<ShipClass, IFlightModel>();

        /// <summary>The model a family with none registered flies: the shared Newtonian one.</summary>
        public IFlightModel Fallback { get; set; } = NewtonianFlightModel.Instance;

        /// <summary>The game's registry: every class in its family, and the light fighters, interceptors,
        /// heavy fighters, bombers, runners and capital ships on models of their own; the other families fly the Newtonian model until theirs land (docs/FLIGHT_MODELS.md).</summary>
        public static FlightModelRegistry Default { get; } = CreateDefault();

        public static FlightModelRegistry CreateDefault()
        {
            var r = new FlightModelRegistry();
            r.Assign(ShipClass.Fighter, FlightFamily.LightFighter);
            r.Assign(ShipClass.Interceptor, FlightFamily.Interceptor);
            r.Assign(ShipClass.Bomber, FlightFamily.Bomber);
            // The recon fighter (U.SPC-T485) is an interceptor's airframe and flies as one.
            r.Assign(ShipClass.Recon, FlightFamily.Interceptor);
            // A boarding shuttle (U.SPC-T486) flies like a bomber: a heavy throttle and a weak strafe.
            r.Assign(ShipClass.AssaultShuttle, FlightFamily.Bomber);
            // The Courier (U.SPC-T444) is a blockade runner, flying a family of its own.
            r.Assign(ShipClass.Courier, FlightFamily.Runner);
            r.Assign(ShipClass.Gunship, FlightFamily.HeavyFighter);
            r.Assign(ShipClass.Freighter, FlightFamily.Hauler);
            r.Assign(ShipClass.Miner, FlightFamily.Hauler);
            r.Assign(ShipClass.Explorer, FlightFamily.Hauler);
            r.Assign(ShipClass.OreTug, FlightFamily.Hauler);
            r.Assign(ShipClass.CaptureTug, FlightFamily.Hauler);
            r.Assign(ShipClass.LaneTender, FlightFamily.Hauler);
            r.Assign(ShipClass.GatePicket, FlightFamily.HeavyFighter);
            r.Assign(ShipClass.Carrier, FlightFamily.Capital);
            r.Assign(ShipClass.Battleship, FlightFamily.Capital);
            r.Assign(ShipClass.Cruiser, FlightFamily.Capital);
            r.Assign(ShipClass.Destroyer, FlightFamily.Capital);
            r.Assign(ShipClass.CommandCruiser, FlightFamily.Capital);
            r.Assign(ShipClass.Frigate, FlightFamily.Capital);
            r.Assign(ShipClass.Corvette, FlightFamily.Capital);
            r.Assign(ShipClass.GateBreaker, FlightFamily.Capital);
            // The light fighter's rules on the numbers in Settings > Dogfighting (U.SPC-T440).
            r.Register(FlightFamily.LightFighter, new LightFighterFlightModel(() => LightFighterSettings.LightFighter.Handling));
            r.Register(FlightFamily.Interceptor, new LightFighterFlightModel(() => LightFighterSettings.Interceptor.Handling, "Interceptor"));
            r.Register(FlightFamily.Bomber, new BomberFlightModel());
            // Heavy fighters (U.SPC-T441): the Gunship, and the gate picket flown nose-first like one.
            r.Register(FlightFamily.HeavyFighter, new HeavyFighterFlightModel());
            r.Register(FlightFamily.Runner, new LightFighterFlightModel(() => LightFighterSettings.Runner.Handling, "Runner"));
            // Haulers (U.SPC-T443): freighters, miners and the other work hulls fly heavy on what they carry.
            r.Register(FlightFamily.Hauler, new HaulerFlightModel());
            // Capital ships (U.SPC-T499): one model, each class on its own handling.
            r.Register(FlightFamily.Capital, new CapitalFlightModel());
            foreach (var c in CapitalHandling.Classes) r.Register(c, new CapitalFlightModel(CapitalHandling.Of(c)));
            return r;
        }

        /// <summary>Puts <paramref name="c"/> in <paramref name="family"/>, flying that family's model (any model of its own
        /// it flew in another family is dropped).</summary>
        public void Assign(ShipClass c, FlightFamily family)
        {
            if (families.TryGetValue(c, out var was) && was != family) classModels.Remove(c);
            families[c] = family;
        }

        /// <summary>Flies every hull of <paramref name="family"/> with <paramref name="model"/>, but for a class flying a
        /// model of its own.</summary>
        public void Register(FlightFamily family, IFlightModel model) =>
            models[family] = model ?? throw new ArgumentNullException(nameof(model));

        /// <summary>Flies every hull of class <paramref name="c"/> with <paramref name="model"/>, over its family's.</summary>
        public void Register(ShipClass c, IFlightModel model) =>
            classModels[c] = model ?? throw new ArgumentNullException(nameof(model));

        /// <summary>The family <paramref name="c"/> flies as; a class never assigned flies as a capital ship.</summary>
        public FlightFamily FamilyOf(ShipClass c) => families.TryGetValue(c, out var f) ? f : FlightFamily.Capital;

        /// <summary>The model flying <paramref name="family"/>.</summary>
        public IFlightModel ModelOf(FlightFamily family) => models.TryGetValue(family, out var m) ? m : Fallback;

        /// <summary>The model flying a hull of class <paramref name="c"/>.</summary>
        public IFlightModel For(ShipClass c) => classModels.TryGetValue(c, out var m) ? m : ModelOf(FamilyOf(c));
    }
}
