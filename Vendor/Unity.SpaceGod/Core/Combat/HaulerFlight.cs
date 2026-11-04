using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A hauler's handling numbers (U.SPC-T443, docs/FLIGHT_MODELS.md, Haulers): data, not code, like
    /// <see cref="LightFighterHandling"/>, so a tuning pass or the settings registry (U.SPC-T431) changes them without
    /// touching <see cref="HaulerFlightModel"/>.
    /// </summary>
    public readonly struct HaulerHandling
    {
        /// <summary>The mass-per-tonne weighting: how much of each tonne aboard beyond the hull's ready mass (dry, tanks
        /// full, hold empty: what its thrust and turn figures were set for) counts against its drive, thrusters and
        /// attitude thrusters. At 1 the hull is honest Newtonian mass (double the mass, half the push); below 1 a full hold
        /// weighs on the hull less than physics would, for feel. Tanks burned down make it lighter than ready, and a little
        /// handier, by the same rule.</summary>
        public readonly double MassWeight;

        /// <summary>Docking mode's set point (Ctrl held inside a port's approach), as a fraction of
        /// <see cref="PilotTuning.MaxSpeed"/>: the throttle floored asks only for docking speed.</summary>
        public readonly double DockingSpeedFraction;

        /// <summary>Docking mode's top turn rate, as a fraction of the hull's own (laden) one, for precise placement.</summary>
        public readonly double DockingTurnFraction;

        /// <summary>Flight assist's strafe set point as a fraction of the forward one: a hauler barely sidesteps.</summary>
        public readonly double StrafeSpeedFraction;

        /// <summary>The boost's charge as a fraction of the class's <see cref="PilotTuning.BoostDurationS"/>: a short
        /// emergency burn to get clear, not a dodge.</summary>
        public readonly double BoostDurationFraction;

        public HaulerHandling(double massWeight, double dockingSpeedFraction, double dockingTurnFraction, double strafeSpeedFraction,
            double boostDurationFraction)
        {
            MassWeight = massWeight;
            DockingSpeedFraction = dockingSpeedFraction;
            DockingTurnFraction = dockingTurnFraction;
            StrafeSpeedFraction = strafeSpeedFraction;
            BoostDurationFraction = boostDurationFraction;
        }

        /// <summary>The first-pass numbers, for the owner's review (docs/FLIGHT_MODELS.md): a full truss freighter (20,000 t in
        /// its pods on a 15,000 t ready hull) pushes and turns at a little over half what it does empty.</summary>
        public static readonly HaulerHandling Default = new HaulerHandling(
            massWeight: 0.6, dockingSpeedFraction: 0.1, dockingTurnFraction: 0.35, strafeSpeedFraction: 0.25, boostDurationFraction: 0.25);
    }

    /// <summary>
    /// Where a hull comes to rest under full retro (U.SPC-T443): every thruster set braking its own share of the way at
    /// its full push, the retros against the way along the nose (the main drive for a hull backing), the manoeuvre
    /// thrusters across it. What the HUD shows a hauler's pilot so the braking can be planned.
    /// </summary>
    public readonly struct HaulerStop
    {
        /// <summary>From the hull to where it stops, km (its own frame's axes).</summary>
        public readonly DVec3 OffsetKm;

        /// <summary>How long it takes to stop, s (the slowest axis); infinite when a set of thrusters that is needed has no push.</summary>
        public readonly double Seconds;

        public HaulerStop(DVec3 offsetKm, double seconds)
        {
            OffsetKm = offsetKm;
            Seconds = seconds;
        }

        /// <summary>How far off the stop point is, km.</summary>
        public double DistanceKm => OffsetKm.Length;

        /// <summary>Whether the hull can stop at all.</summary>
        public bool Reachable => !double.IsInfinity(Seconds) && !double.IsNaN(Seconds);
    }

    /// <summary>
    /// Haulers (U.SPC-T443, docs/FLIGHT_MODELS.md): freighters, miners and the other work hulls, satisfying to fly because
    /// they are heavy. (1) Laden mass: what is aboard (the hold, the personal grid hold and the tanks, given as
    /// <see cref="PilotInput.MassRatio"/>) weighs on the drive, retros, manoeuvre and attitude thrusters and the top turn,
    /// by <see cref="HaulerHandling.MassWeight"/>, so a full hull gathers way, brakes and turns slower than an empty one.
    /// (2) The retros stay weak, and <see cref="StopPoint(in PilotState, in HullDynamics, in PilotInput)"/> is where the
    /// hull comes to rest at full retro, for the HUD. (3) Docking mode: holding Ctrl (<see cref="PilotInput.Fine"/>) inside
    /// a port's approach (<see cref="PilotInput.DockingApproach"/>) drops the set point to docking speed and gentles the
    /// turn, with no boost or cruise. (4) No combat agility: the strafe is weak and the boost a short emergency burn.
    /// With flight assist off the hull is the Newtonian one on its laden mass: mass is physics, not the flight computer.
    /// </summary>
    public sealed class HaulerFlightModel : IFlightModel
    {
        /// <summary>The least a light hull's mass factor goes to, however far its tanks are burned down.</summary>
        public const double MinMassFactor = 0.5;

        public HaulerHandling Handling { get; }

        public HaulerFlightModel() : this(HaulerHandling.Default) { }

        public HaulerFlightModel(HaulerHandling handling) => Handling = handling;

        public string Name => "Hauler";

        /// <summary>
        /// A ship's all-up mass (hull, tanks and holds, <see cref="Ship.MassTonnes"/>) over its ready mass (dry with full
        /// tanks and an empty hold): what the helm passes as <see cref="PilotInput.MassRatio"/>. 1 for a hull fresh from its
        /// yard, more for a laden one.
        /// </summary>
        public static double LadenRatio(Ship ship)
        {
            if (ship == null) return 1;
            double ready = ship.DryMassTonnes + ship.PropellantCapacity;
            return ready > 0 ? ship.MassTonnes / ready : 1;
        }

        /// <summary>What a mass ratio does to the hull: the divisor on every push and on the turn (1 for a ready hull, or
        /// for no ratio given), by <see cref="HaulerHandling.MassWeight"/>.</summary>
        public double MassFactor(double massRatio)
        {
            if (massRatio <= 0 || double.IsNaN(massRatio) || double.IsInfinity(massRatio)) return 1;
            return Math.Max(MinMassFactor, 1 + Math.Max(0, Handling.MassWeight) * (massRatio - 1));
        }

        /// <summary>The hull with its load aboard: every thruster set's push and the attitude thrusters' spin cut by the mass factor.</summary>
        public HullDynamics Laden(in HullDynamics hull, double massRatio)
        {
            double k = MassFactor(massRatio);
            if (k == 1) return hull;
            return new HullDynamics(hull.MassKt * k, hull.MainAccel / k, hull.RetroAccel / k, hull.LateralAccel / k, hull.VerticalAccel / k,
                hull.PitchYawAngularAccel / k, hull.RollAngularAccel / k);
        }

        /// <summary>Whether this frame flies in docking mode: Ctrl held inside a port's approach, flight assist on.</summary>
        public static bool Docking(in PilotInput input) => input.Fine && input.DockingApproach && !input.FlightAssistOff;

        /// <summary>The flight computer's figures for a hauler this frame: the top turn cut by the load (and by docking mode),
        /// the boost a short burn, and in docking mode the set point at docking speed.</summary>
        public PilotTuning HelmTuning(in PilotTuning tuning, in PilotInput input)
        {
            double k = MassFactor(input.MassRatio);
            bool docking = Docking(input);
            double turn = tuning.TurnRateDegS / k * (docking ? Clamp01(Handling.DockingTurnFraction) : 1);
            double speed = docking ? tuning.MaxSpeed * Clamp01(Handling.DockingSpeedFraction) : tuning.MaxSpeed;
            double boost = tuning.BoostDurationS * Math.Max(0, Handling.BoostDurationFraction);
            return new PilotTuning(speed, turn, tuning.BoostSpeedMultiplier, boost, tuning.CruiseSpeedMultiplier, tuning.CruiseRampS);
        }

        /// <summary>Where the hull comes to rest under full retro with this frame's load aboard (see <see cref="HaulerStop"/>).</summary>
        public HaulerStop StopPoint(in PilotState s, in HullDynamics hull, in PilotInput input) => StopPoint(s, Laden(hull, input.MassRatio));

        /// <summary>Where a hull of <paramref name="hull"/>'s push comes to rest under full retro (see <see cref="HaulerStop"/>):
        /// each axis of its way braked by its own thrusters at full push, v²/2a along each.</summary>
        public static HaulerStop StopPoint(in PilotState s, in HullDynamics hull)
        {
            var f = s.Forward;
            var r = s.Right;
            var u = s.Up;
            double vf = DVec3.Dot(s.Vel, f), vr = DVec3.Dot(s.Vel, r), vu = DVec3.Dot(s.Vel, u);
            double seconds = 0;
            var offset = f * Brake(vf, vf > 0 ? hull.RetroAccel : hull.MainAccel, ref seconds)
                + r * Brake(vr, hull.LateralAccel, ref seconds)
                + u * Brake(vu, hull.VerticalAccel, ref seconds);
            return new HaulerStop(offset, seconds);
        }

        static double Brake(double v, double a, ref double seconds)
        {
            if (Math.Abs(v) < 1e-12) return 0;
            if (a <= 0)
            {
                seconds = double.PositiveInfinity;
                return 0;
            }
            seconds = Math.Max(seconds, Math.Abs(v) / a);
            return v * Math.Abs(v) / (2 * a);
        }

        public void Step(ref PilotState s, ShipCombat combat, in HullDynamics hull, in PilotTuning tuning, in PilotInput input, DVec3 gravityKmS2, double dt)
        {
            var laden = Laden(hull, input.MassRatio);
            var helm = HelmTuning(tuning, input);
            if (input.FlightAssistOff)
            {
                PilotFlight.Step(ref s, combat, laden, helm, input, gravityKmS2, dt);
                return;
            }
            if (dt <= 0) return;

            // Docking mode is a slow, careful approach: no boost and no cruise.
            var drive = input;
            if (Docking(input))
            {
                drive.Boost = false;
                drive.Cruise = false;
            }
            var forwardBeforeTurn = s.Forward;
            PilotFlight.Rotate(ref s, laden, helm, drive, dt);
            double driveMult = PilotFlight.DriveMultiplier(ref s, combat, helm, drive, forwardBeforeTurn, dt);
            var thrust = PilotFlight.AssistThrust(s, laden, helm, drive, gravityKmS2, driveMult, dt, Handling.StrafeSpeedFraction,
                laden.LateralAccel, laden.VerticalAccel);
            PilotFlight.Integrate(ref s, thrust, gravityKmS2, dt);
        }

        static double Clamp01(double x) => Math.Max(0, Math.Min(1, x));
    }
}
