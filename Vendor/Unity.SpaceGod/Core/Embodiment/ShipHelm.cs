using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A ship's helm as an <see cref="IEmbodiable"/> (U.SPC-T18): every ship role -- freighters, miners, couriers,
    /// escorts, fighters and capital ships -- is taken and flown through it (U.SPC-T449). Who may take it, where it
    /// starts and the take-charge/hand-back bookkeeping (<see cref="World.PilotedShip"/>, an escort cut loose, the war's
    /// command handed over, a fighting hull's <see cref="BattleUnit"/> passed between player and crew) live here, and
    /// so does the hull's flight: <see cref="Control"/> flies its <see cref="Flight"/> by its class's own flight model
    /// (U.SPC-T17) on the control set fitted to its role (<see cref="Controls"/>), paying for every burn from its tanks.
    /// What the world does around it (gravity, surfaces, collisions, the fight, the law) is the host's.
    /// </summary>
    public sealed class ShipHelm : IEmbodiable
    {
        readonly World world;

        public Ship Ship { get; }

        public ShipHelm(World world, Ship ship)
        {
            this.world = world;
            Ship = ship;
        }

        public string Name => Ship.Name;
        public EmbodimentKind Kind => EmbodimentKind.Ship;
        public Faction Faction => Ship.Faction;
        public IEmbodiable Host => null;
        public string Blocker => BlockerOf(Ship);
        public bool CanEmbody => Blocker == null;
        public bool Embodied => world.PilotedShip == Ship && Ship != null;

        /// <summary>The hull's place in the fight it is in, if any.</summary>
        BattleUnit Unit => Ship.Battle?.UnitOf(Ship);

        /// <summary>
        /// Why <paramref name="ship"/>'s helm cannot be taken right now, or null if it can: only a wreck, a
        /// derelict, or a prize already taken in a fight still on (its captors' to dispose of when it ends).
        /// </summary>
        public static string BlockerOf(Ship ship)
        {
            if (ship == null) return "nothing there";
            if (ship.Destroyed) return "destroyed";
            if (ship.Derelict) return "derelict";
            if (ship.Battle?.UnitOf(ship)?.Captured ?? false) return "taken as a prize";
            return null;
        }

        /// <summary>
        /// The body whose frame best describes a ship's current position, and its offset there in true km --
        /// from wherever it actually is (parked, mid-lane, mid-burn, queued, landed), not only a parked orbit.
        /// </summary>
        public static (int body, DVec3 offsetKm) FrameOf(World world, Ship ship, double jd)
        {
            // An escort has no schedule of its own (no orbit, no phase): it is wherever its leader is.
            var c = ship.Controller;
            var st = world.Fleet.StateOf(c, jd);
            if (c.Phase == ShipPhase.Parked)
                return (c.Orbit.Body, c.Orbit.OffsetAt(jd));
            if ((c.Phase == ShipPhase.Queued || c.Planetside) && c.Ring != null)
                return (c.Ring.Body, st.HelioKm - world.Ephemeris.Helio(c.Ring.Body));
            return (c.FrameBody, st.HelioKm - world.Ephemeris.Helio(c.FrameBody));
        }

        public EmbodimentPose PoseAt(double jd)
        {
            var unit = Unit;
            if (unit != null)
            {
                unit.Basis(out _, out var up);
                return new EmbodimentPose(Ship.Battle.Body, unit.Pos + Ship.Battle.Anchor, unit.Forward, up);
            }
            var (body, offset) = FrameOf(world, Ship, jd);
            var f = world.Fleet.StateOf(Ship, jd).Facing;
            f = f.LengthSquared > 1e-18 ? f.Normalized : DVec3.UnitX;
            return new EmbodimentPose(body, offset, f, PilotState.LevelUp(f));
        }

        /// <summary>
        /// The player takes the helm: the fleet stops scheduling the hull, an escort is cut loose from its
        /// formation (the war's command handed over otherwise), and a hull already fighting has its own
        /// <see cref="BattleUnit"/> handed to the player rather than a new fight started.
        /// </summary>
        public void TakeCharge()
        {
            world.PilotedShip = Ship;
            if (Ship.IsEscort) Ship.Leader = null;
            else world.War?.HandOverCommand(Ship);
            var unit = Unit;
            if (unit != null)
            {
                // Keep the hull's roll: the helm's roof starts where the captain's (or level) roof was.
                unit.Basis(out _, out var rolled);
                unit.Up = rolled;
                unit.PlayerControlled = true;
                unit.PlayerFireHeld = false;
                unit.PlayerLockTarget = null;
            }
        }

        /// <summary>The motion this helm flies (U.SPC-T449): given by whoever hosts the helm, which keeps it across frames.
        /// With none, <see cref="Control"/> hands over only the trigger and the crosshair.</summary>
        public HelmFlight Flight { get; set; }

        /// <summary>The control set the hull is flown with, fitted to its role (U.SPC-T449).</summary>
        public HelmControls Controls => HelmControls.Of(Ship);

        /// <summary>
        /// One frame at the helm: the trigger and the crosshair go to the hull's battle unit, and the hull flies its
        /// <see cref="Flight"/> on the stick (see <see cref="Fly"/>).
        /// </summary>
        public void Control(in EmbodimentInput input, double dt)
        {
            var unit = Unit;
            if (unit != null && unit.PlayerControlled)
            {
                unit.PlayerFireHeld = input.Primary;
                unit.PlayerSecondaryHeld = input.Secondary;
                unit.PlayerAim = input.Aim;
            }
            if (Flight != null && !Ship.Destroyed) Fly(Flight, input, dt);
        }

        /// <summary>
        /// Flies <paramref name="f"/> one frame (U.SPC-T449): the stick, in world terms (<paramref name="e"/>'s aim is
        /// where the nose is wanted), through the hull's class's flight model (U.SPC-T17), on its own hull and tuning,
        /// with what the role's controls add (a hauler's load and docking mode, a capital's telegraph and the helm assist
        /// rolling its weak patches away), nothing at all while its drives are down; then every change of velocity its
        /// thrusters made (not gravity's, which is free) is paid for from its tanks, and a dry hull only coasts.
        /// </summary>
        public void Fly(HelmFlight f, in EmbodimentInput e, double dt)
        {
            var ship = Ship;
            var controls = Controls;
            var tuning = PilotTuning.Of(ship.Spec.Class);
            var hull = HullDynamics.Of(ship.Combat.Spec);
            var model = FlightModelRegistry.Default.For(ship.Spec.Class);
            var capital = model as CapitalFlightModel;
            var input = new PilotInput
            {
                DesiredForward = e.Aim,
                Throttle = Clamp1(e.Throttle),
                StrafeRight = Clamp1(e.StrafeRight),
                StrafeUp = Clamp1(e.StrafeUp),
                Roll = Clamp1(e.Roll),
                Boost = e.Boost,
                Cruise = e.Cruise,
                InCombat = ship.Battle != null,
                FlightAssistOff = f.AssistOff,
                TelegraphStep = controls.Telegraph ? e.TelegraphStep : 0,
                // A hauler flies heavy on what it carries, and Ctrl inside a port's approach is docking mode (U.SPC-T443).
                MassRatio = HaulerFlightModel.LadenRatio(ship),
                Fine = e.Fine,
                DockingApproach = f.DockingApproach,
            };
            if (f.DrivesDown)
            {
                input.DesiredForward = f.State.Forward;
                input.Throttle = input.StrafeRight = input.StrafeUp = input.Roll = 0;
                input.Boost = input.Cruise = false;
                input.FlightAssistOff = true;
                input.TelegraphStep = 0;
            }
            // Ordered to roll the weak patches away (U.SPC-T502): the helm assist rolls the hull while the helmsman's hands
            // are off the roll keys; the nose stays the helmsman's.
            if (capital != null && !input.FlightAssistOff && Math.Abs(input.Roll) < 0.01 && ship.Battle != null
                && ship.Battle.UnitOf(ship) is BattleUnit helmUnit && helmUnit.Broadside != null && helmUnit.Broadside.RollWeakPatches)
                input.Roll = WeakPatches.HelmRoll(ship.Battle, helmUnit, f.State.Forward, f.State.Up,
                    capital.PeakTurnRateDegS(hull) * capital.Handling.RollFactor, hull.RollAngularAccel, dt);
            // Read before the model drains the charge further this frame (a capital rung to Flank boosts too).
            bool flank = capital != null && !input.FlightAssistOff && f.State.Telegraph == EngineTelegraph.Flank;
            f.Boosting = (input.Boost || flank) && tuning.BoostDurationS > 0 && ship.Combat.BoostFraction > 0;

            var gravity = f.Gravity;
            var posBefore = f.State.Pos;
            var velBefore = f.State.Vel;
            model.Step(ref f.State, ship.Combat, hull, tuning, input, gravity, dt);
            var pos = f.State.Pos;
            var vel = f.State.Vel;
            Propulsion.ApplyManoeuvre(ship, ref pos, ref vel, posBefore, velBefore + gravity * dt, dt);
            f.State.Pos = pos;
            f.State.Vel = vel;

            f.Model = model;
            f.Hull = hull;
            f.Input = input;
            f.PosBefore = posBefore;
            f.VelBefore = velBefore;
            f.Burning = dt > 0 && (vel - (velBefore + gravity * dt)).Length / dt > HelmFlight.BurnKmS2;
        }

        static double Clamp1(double v) => Math.Max(-1, Math.Min(1, v));

        /// <summary>
        /// The crew takes the helm back: a fighting hull's own AI (broadsides, point defence, missiles) picks
        /// up where manual control left off, same facing and velocity. Pinning it where it was left and
        /// rescheduling it are the Runtime's, which knows the pilot's last spot.
        /// </summary>
        public void HandBack()
        {
            if (world.PilotedShip == Ship) world.PilotedShip = null;
            if (Ship.Destroyed) return;
            var unit = Unit;
            if (unit != null)
            {
                // Keep the hull's roll: the captain's roof picks up from the helm's.
                if (unit.PlayerControlled) unit.Roof = unit.Up;
                unit.PlayerControlled = false;
                unit.PlayerFireHeld = false;
                unit.PlayerLockTarget = null;
            }
        }

        public override bool Equals(object obj) => obj is ShipHelm h && h.Ship == Ship;
        public override int GetHashCode() => Ship?.GetHashCode() ?? 0;
    }
}
