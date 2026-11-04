// Port (US-T26) of Unity.SpaceGod's Runtime/UnitySpaceApp.cs, lines 976-2085 (piloting: TogglePiloting .. the overview):
// the original source, unchanged but for the lines marked US-T26, split out of the main port only to keep files reviewable.
using System;
using System.Collections.Generic;
using Unity.SpaceGod.Cameras;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Input;
using Unity.SpaceGod.Rendering;
using Unity.SpaceGod.UI;
using Unity.SpaceGod.Views;
using UnityEngine;

namespace Unity.SpaceGod
{
    public sealed partial class UnitySpaceApp
    {
        // ---------------------------------------------------------------- piloting

        public void TogglePiloting()
        {
            // At any seat, P hands it back to its AI and returns to command, the view and time warp it was taken from (U.SPC-T422).
            if (AtControls)
            {
                ReturnToCommand();
                return;
            }
            var t = Selected.IsShip ? Selected : Hovered.IsShip ? Hovered : Target.None;
            if (!t.IsShip)
            {
                ShowToast("Select a ship to pilot it");
                return;
            }
            // Only a wreck, a derelict or a prize already taken in a fight still on is out of reach (U.SPC-T18).
            if (!Embodiables.HelmOf(World, t.Ship).CanEmbody)
            {
                ShowToast("Can't take the helm of that ship right now");
                return;
            }
            RememberCommand();
            EnterPiloting(t);
        }

        /// <summary>
        /// Makes <paramref name="ship"/> a freshly Parked hull fixed at the pilot's spot and facing.
        /// Taken control may start mid-lane, mid-burn, queued, landed or in formation (see
        /// <see cref="EnterPiloting"/>), so the old phase's bookkeeping (path, journey, ring queue) is
        /// stale and dropped: done on taking the helm, so nothing treats a flown hull as landed (hidden,
        /// unpickable) or reads an escort's missing orbit, and again on release.
        /// </summary>
        void PinWherePiloted(Ship ship) => Fleet.PinAt(ship, pilotFrameBody, pilotOffsetKm, pilotForward, Clock.Jd);

        /// <summary>The body whose frame best describes a ship's current position, and its offset
        /// there in true km -- the same per-phase placement <see cref="UpdateShips"/> computes for
        /// drawing, used instead to seed piloting from wherever a ship actually is (mid-lane,
        /// mid-burn, queued, landed...), not only a parked orbit.</summary>
        (int body, DVec3 offsetKm) CurrentFrame(Ship ship, double jd) => ShipHelm.FrameOf(World, ship, jd);

        /// <summary>
        /// Takes direct control of any flyable hull, wherever it is -- parked, mid-transit, riding or
        /// lurking off a hyperlane, queued or landed at a ring, even mid-battle (an escort is detached
        /// from its formation; a ship already fighting has its existing <see cref="BattleUnit"/> handed
        /// to the player instead of starting a new fight) -- via <see cref="TogglePiloting"/>, or a
        /// freshly spawned one via <see cref="SpawnShip"/>. Seeds every per-frame pilot field and snaps
        /// the camera into chase.
        /// </summary>
        void EnterPiloting(Target t)
        {
            if (CoopRoutesHelmTake(t)) return; // US-T39/US-T43: in co-op a take goes into the frames, and every peer takes it there
            var ship = t.Ship;
            Piloted = t;
            // A fresh helm: no drives-down timer left over from the hull flown before (spawned or loaded in the same frame).
            pilotEmpSeconds = 0;
            // Where it is (and which way it faces) before an escort is cut loose from the leader it shares them with.
            var (frame, offsetKm) = CurrentFrame(ship, Clock.Jd);
            var facing = Fleet.StateOf(ship, Clock.Jd).Facing;
            var velocity = DVec3.Zero;
            // Riding a lane through to its far ramp: the ride goes on, now at the helm (U.SPC-T41). One
            // that means to stop part-way (a raider's lurk) is taken where it is, as any other flight.
            bool riding = !ship.IsEscort && ship.Battle == null && ship.Phase == ShipPhase.Lane && ship.LaneEndProgress >= 1;
            if (riding)
            {
                frame = ship.LaneLeg.From;
                offsetKm = Fleet.StateOf(ship, Clock.Jd).HelioKm - Ephemeris.Helio(frame);
            }
            // The helm is the player's (U.SPC-T18): the fleet lets go, an escort is cut loose from its formation (the war's
            // command handed over otherwise), and a hull already fighting has its own battle unit handed over.
            Embodiables.HelmOf(World, ship).TakeCharge();
            if (ship.Battle != null)
            {
                var unit = ship.Battle.UnitOf(ship);
                if (unit != null)
                {
                    // Mid-fight, the hull is where the battle has it, not where its schedule would put it:
                    // the pilot loop writes this pose back every frame (pilotOffsetKm - Anchor), so seed it from there.
                    frame = ship.Battle.Body;
                    offsetKm = unit.Pos + ship.Battle.Anchor;
                    facing = unit.Forward;
                    velocity = unit.Vel;
                }
            }
            pilotFrameBody = frame;
            SeedPilot(offsetKm, velocity, facing);
            // Mid-fight the hull keeps its roll: the pilot's roof is the unit's (TakeCharge stored it in Up), not the camera's.
            if (ship.Battle?.UnitOf(ship) is { } rolledUnit && rolledUnit.Up.LengthSquared > 1e-12) pilot.Up = rolledUnit.Up.Normalized;
            // A tanker called for some earlier hull (one taken from the player, say) is not this one's.
            TankerDueS = -1;
            propellantWarning = 0;
            pilotFlightAssistOff = false;
            pilotBattleCheckTimer = 0;
            demandPrompted = false;
            termsPrompted = null;
            havenPrompted = null;
            // Out of a battle, the hull is the pilot's from here: no longer landed, descending or in formation.
            // In one, the battle owns its pose, but an escort cut loose still needs an orbit of its own.
            rideWarpBefore = -1;
            insideRampGate = true;
            laneRejoinable = false;
            if (!riding && (ship.Battle == null || ship.Orbit == null)) PinWherePiloted(ship);

            if (CoopHelmMine) Select(t); // US-T43: the holder's view (every peer flies the helm, one watches from it)
            if (CoopHelmMine) SpaceCamera.EnterPilotChase(t, 6); // US-T43
            Clock.SetWarp(0);
            ShowToast("Piloting " + ship.Name + ", " + Clock.Warp.Label + "  (" + HelmControls.Of(ship).Hint + "; P to release, . to speed up, Left-click to fire, T to lock a target, Z/X to roll, F flight assist, hold J to cruise)");
            if (riding)
            {
                BeginRide(ship, false);
                ShowToast("Riding the " + ship.LaneLeg.Lane.Name + " lane at the helm of " + ship.Name + ", bound for " + Fleet.Lanes.EndName(ship.LaneLeg.Lane, ship.LaneLeg.To) +
                          "  (number keys pick the off-ramp's exit, P to release)");
            }
        }

        /// <summary>
        /// Dev/sandbox tool (U.SPC-T9): builds a ship of <paramref name="cls"/> (a random design if the
        /// class has more than one, see <see cref="CombatSpec.RandomOf"/>) via <see cref="Fleet.SpawnAt"/>,
        /// parked exactly where the camera is looking from, and immediately pilots it -- for testing
        /// flight feel and combat without hunting down an existing hull in the live simulation. Releases
        /// whatever was already being piloted first, same as pressing Enter would.
        /// </summary>
        public void SpawnShip(ShipClass cls, bool hostile)
        {
            RememberCommand();
            ReleaseSeat();
            if (IsPiloting) ExitPiloting();

            // Where the camera is now: its free-flight frame goes stale while it is focused on something far off.
            int frame = SpaceCamera.FrameAt(SpaceCamera.ScenePosition);
            var offsetKm = Scale.InverseLocalOffset(Catalog[frame], SpaceCamera.ScenePosition - BodyScene(frame));
            var forwardUnity = SpaceCamera.Rotation * Vector3.forward;
            var forward = new DVec3(forwardUnity.x, forwardUnity.y, forwardUnity.z).SwapYZ;
            if (CoopSpawns("spawn", (int)cls + " " + (hostile ? 1 : 0), frame, offsetKm, forward)) return; // US-T43: the rest on every peer (CoopSpawnShip)

            var faction = hostile ? World.Pirates : World.Sol;
            string name = "Sandbox " + cls + " " + (++spawnedCount);
            var ship = Fleet.SpawnAt(name, cls, faction, frame, offsetKm, forward, Clock.Jd);
            ship.Ports = World.FreeShipPorts(); // not a hidden haven's rock, as for every free ship

            EnterPiloting(Target.OfShip(ship));
        }

        int spawnedCount;

        /// <summary>Where the player is, true heliocentric km: the piloted ship, else the camera (an observer
        /// still has adventures offered around them, but only a piloted ship can answer one).</summary>
        public DVec3 PlayerHelioKm()
        {
            if (IsPiloting) return Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm;
            // The camera's free-flight frame goes stale while it is focused on something far off.
            int frame = SpaceCamera.FrameAt(SpaceCamera.ScenePosition);
            return Ephemeris.Helio(frame) + Scale.InverseLocalOffset(Catalog[frame], SpaceCamera.ScenePosition - BodyScene(frame));
        }

        /// <summary>Scene position of an opportunity's HUD marker: on its ship or wreck, else its point in its body's frame.</summary>
        public bool OpportunityScene(Opportunity op, out DVec3 scene)
        {
            scene = default;
            if (op.Ship != null && !op.Ship.Destroyed && op.Ship.Id < shipScene.Length) scene = shipScene[op.Ship.Id];
            else if (op.Wreck != null) scene = WreckScene(op.Wreck);
            else if (op.Pod != null) scene = PodScene(op.Pod);
            else if (op.Body >= 0 && op.Body < bodyScene.Length) scene = bodyScene[op.Body] + Scale.LocalOffset(Catalog[op.Body], op.OffsetKm);
            else return false;
            return true;
        }

        /// <summary>Starts the hull's motion at rest in attitude, nose on a facing (ecliptic, unit) and
        /// level with the camera's up frame unless that facing is itself near-vertical, with the stick
        /// pointing straight ahead -- shared by <see cref="TogglePiloting"/> and a jump gate handing the
        /// same pilot session to a new hull in the destination system.</summary>
        void SeedPilot(DVec3 offsetKm, DVec3 velocityKmS, DVec3 facing)
        {
            pilot = PilotState.At(offsetKm, velocityKmS, facing);
            var upUnity = (CoopUpFrame ?? SpaceCamera.UpFrame) * Vector3.up; // US-T43: the helm holder's frame in co-op
            var upReference = new DVec3(upUnity.x, upUnity.y, upUnity.z).SwapYZ;
            var level = upReference - pilot.Forward * DVec3.Dot(upReference, pilot.Forward);
            if (level.LengthSquared > 1e-6) pilot.Up = level.Normalized;
            ResetPilotAim();
        }

        /// <summary>The piloted ship was taken from the player (boarded and captured): the session ends with
        /// nothing handed back, since the hull is its captor's now and the war disposes of it.</summary>
        void LoseHelm()
        {
            StopFtl();
            EndSalvage(null);
            SalvageApproach = null;
            var ship = Piloted.Ship;
            // Where it was taken: the captor's schedule (or the drifting hulk) picks up from here. If the
            // capture ended the fight in the same tick, the war has already planned the prize's next trip from
            // its old spot: plan it again from here, or the pin would hold it until that trip's departure time.
            EndRide();
            PinWherePiloted(ship);
            if (ship.Battle == null && !ship.Destroyed && !ship.Derelict && !ship.IsEscort) Fleet.ScheduleNext(ship, Clock.Jd);
            Piloted = Target.None;
            pilotLockTarget = Target.None;
            pilotLockUnit = null;
            pilotBoosting = false;
            pilotCruise = default;
            // A trigger held as the helm was taken is not still held by the next hull flown.
            helmTriggerWas = false;
            if (CoopHelmMine) SpaceCamera.PilotLockTarget = Target.None; // US-T43: the holder's view only
            if (CoopHelmMine) SpaceCamera.ExitFocus(); // US-T43
            ShowToast("The " + ship.Name + " has been boarded and taken");
        }

        void ExitPiloting()
        {
            if (CoopRoutesHelmRelease()) return; // US-T43: a release the player asks for goes into the frames in co-op
            // A charge stops where it is (U.SPC-T417): the crew does not jump on its own.
            StopFtl();
            EndSalvage(null);
            SalvageApproach = null;
            TankerDueS = -1;
            propellantWarning = 0;
            var ship = Piloted.Ship;
            Piloted = Target.None;
            // The crew takes the helm back (U.SPC-T18): a hull still fighting has its battle unit handed back to its own AI,
            // which picks up right where manual control left off, same facing and velocity.
            if (ship != null) Embodiables.HelmOf(World, ship).HandBack();
            World.PilotedShip = null;
            // Nobody runs down a ship no one flies: the chase ends with the helm, even if another is taken this frame.
            if (World.Pursuit != null && World.Pursuit.Active) World.Pursuit.ReleaseAll(Clock.Jd);
            pilotEmpSeconds = 0;
            pilotLockTarget = Target.None;
            pilotLockUnit = null;
            pilotBoosting = false;
            pilotCruise = default;
            if (CoopHelmMine) SpaceCamera.PilotLockTarget = Target.None; // US-T43: the holder's view only
            bool rode = ship != null && !ship.Destroyed && ship.Battle == null && ship.Phase == ShipPhase.Lane;
            EndRide();
            // Mid-ride the crew rides it on: the fleet brings it off at the far ramp and on into that system.
            if (ship != null && !ship.Destroyed && !rode)
            {
                if (ship.Battle != null)
                {
                    // Its crew has the fight back (above); its schedule picks up from here when the fight is over, not from wherever
                    // the helm was taken (the battle's own pose doesn't move the ship's orbit).
                    PinWherePiloted(ship);
                }
                else
                {
                    // Pin the hull exactly where the player left it (facing included) before the AI
                    // schedule resumes -- otherwise it picks up from the stale pre-piloting orbit and
                    // the ship snaps back to wherever that orbit would have drifted to on its own.
                    PinWherePiloted(ship);
                    Fleet.ScheduleNext(ship, Clock.Jd);
                }
            }
            if (CoopHelmMine) SpaceCamera.ExitFocus(); // US-T43: the holder's view only
            // A trigger held as the helm was let go is not still held by the next hull taken.
            helmTriggerWas = false;
            // A hull shot to pieces under the helm ends the session as a loss, not as a release the player chose.
            ShowToast(ship != null && ship.Destroyed ? "The " + ship.Name + " has been destroyed" : "Released manual control");
        }

        /// <summary>Whether the piloted ship can trade at <paramref name="s"/> right now: piloting is a local
        /// flight within one body's frame (<see cref="pilotFrameBody"/> never changes mid-flight, see
        /// <see cref="TogglePiloting"/>), so reach is that frame matching the market's body and the ship still
        /// being within its neighbourhood -- the same "near a world" radius <see cref="Fleet.WanderOrbit"/>
        /// and the hyperlane ramps use -- on top of the usual diplomatic can-dock check.</summary>
        public bool CanTradeNow(Settlement s) => NearPort(s) && Economy.CanTrade(World.PilotedShip, s);

        /// <summary>Whether the piloted ship is close enough to <paramref name="s"/> to deal with its port (trade, pay fines), welcome or not.</summary>
        public bool NearPort(Settlement s) =>
            IsPiloting && World.PilotedShip != null && pilotFrameBody == s.Body && pilotOffsetKm.Length <= ScaleModel.NeighbourhoodKm(Catalog[s.Body]);

        /// <summary>Shows what the law has to tell the player (their crimes seen, hails, scans, bounties collected) as toasts.</summary>
        void ShowLawNotices()
        {
            // One toast slot: join the law's and the trade desk's lines so neither overwrites the other.
            var all = new System.Collections.Generic.List<string>();
            var notices = World.Law?.TakeNotices();
            if (notices != null) all.AddRange(notices);
            // The trade desk's news for the player: charters ending, claims paid, customs, a rank risen (U.SPC-T426).
            var trade = World.Trade?.TakeNotices();
            if (trade != null) all.AddRange(trade);
            if (all.Count > 0) ShowToast(string.Join("   ·   ", all));
        }

        /// <summary>
        /// A patrol's order to heave to (see <see cref="Law.UpdatePilot"/>): 1 pays the fine it asks from the
        /// player's purse. Returns true while that hail has the number keys.
        /// </summary>
        bool UpdateLawHail(Ship ship)
        {
            var hail = World.Law?.Hail;
            if (hail == null || !hail.Interdiction) return false;
            if (Input.NumberKey == 1)
            {
                if (World.Law.PayFine(ship, hail.By, Clock.Jd, out double paid))
                    ShowToast("Paid " + Num(paid, "N0") + " cr in fines to " + hail.By.Name + ": you are free to go");
                else ShowToast("Not enough credits to pay the fine of " + Num(hail.Fine, "N0") + " cr");
            }
            return true;
        }

        /// <summary>Turns mouse delta + throttle/strafe keys into a flight command and steps the hull's
        /// position every frame piloting is active, before <see cref="UpdateShips"/> reads it.</summary>
        void UpdatePiloting(float dt)
        {
            // Set again below once the hull has flown this frame; a lane ride or a lost helm leaves the chase on the hull.
            SpaceCamera.PilotView = null;
            // The hull out between the stars under its own drive (U.SPC-T417): the helm is taken again where it drops out.
            if (InFtlCruise)
            {
                UpdateFtlCruise();
                if (!IsPiloting) return;
            }
            if (!IsPiloting)
            {
                pilotBoosting = false;
                pilotCruise = default;
                nearGate = null;
                // Nobody runs down a ship no one is flying: the chasers go back to their business.
                if (World.Pursuit != null && World.Pursuit.Active) World.Pursuit.ReleaseAll(Clock.Jd);
                pilotEmpSeconds = 0;
                World.Sensors?.UpdatePilot(null, default, false, Clock.Jd);
                // A patrol's hail ends with the helm, so it is not answered later by whatever hull is flown next.
                World.Law?.UpdatePilot(null, 0, default, default, Clock.Jd);
                return;
            }
            var ship = Piloted.Ship;
            // Taken this very frame (the war let go of it on capture) -- even if its captors then scuttled it
            // at once: the session ends, as a loss, before anything here hands the hull back to the player.
            if (ship != null && World.PilotedShip != ship)
            {
                LoseHelm();
                return;
            }
            if (ship == null || ship.Destroyed)
            {
                ExitPiloting();
                return;
            }
            // Riding a lane, or thrown out of one this frame into an ambush (U.SPC-T42).
            if (ship.Phase == ShipPhase.Lane || (rideWarpBefore >= 0 && ship.Battle != null))
            {
                // Into a lane, the chase is shaken off.
                if (World.Pursuit != null && World.Pursuit.Active)
                {
                    World.Pursuit.ReleaseAll(Clock.Jd);
                    ShowToast("Into the lane: the hunters are left behind");
                }
                UpdateLaneRide(ship);
                ShowWarNotices();
                return;
            }

            // Interstellar navigation (U.SPC-T420): a course to a star or beacon picked and plotted, and the FTL autopilot
            // flying it; while it has the helm the stick is not read.
            if (Input.ClearFtlCourse && PlottedCourse != null) ClearFtlCourse();
            if (Input.CycleFtlTarget != 0) CycleFtlCourse(Input.CycleFtlTarget);
            if (FtlAutopilot.Active)
            {
                if (UpdateFtlAutopilot(ship, dt)) return;
            }
            else if (Input.ToggleFtlAutopilot && EngageFtlAutopilot()) return;
            RefreshFtlCourse(ship);

            // The stick, one way per kind of hull (U.SPC-T269): a light fighter's mouse swings the view and the
            // hull turns after it, a heavy fighter's or freighter's turns the hull itself, and a capital ship
            // is turned on the keys while the mouse looks around it. Every role is flown through its ShipHelm, on the
            // control set fitted to it (U.SPC-T449); this is only the input layer turning the player's hands into it.
            if (pilotHelm == null || pilotHelm.Ship != ship) pilotHelm = new ShipHelm(World, ship) { Flight = helmFlight };
            var controls = pilotHelm.Controls;
            var tuning = PilotTuning.Of(ship.Spec.Class);
            var hull = HullDynamics.Of(ship.Combat.Spec);
            var control = PilotControlMode = controls.Stick;
            var model = FlightModelRegistry.Default.For(ship.Spec.Class);
            var capital = model as CapitalFlightModel;
            // A capital's helm leads the nose by what its own swing needs (U.SPC-T499).
            double leadRad = capital != null ? capital.AimLeadRad(hull) : PilotFlight.AimLeadRad(hull, tuning);
            var look = ClickPanelOpen ? Vector2.zero : Input.MouseDelta;
            look.y = SpaceCamera.LookY(look.y);
            double lookRight = look.x * PilotLookSensitivity * Mathf.Deg2Rad, lookUp = look.y * PilotLookSensitivity * Mathf.Deg2Rad;
            double strafeRight = Input.Move.x, strafeUp = Input.Move.y;
            switch (control)
            {
                case PilotControl.FreeAim:
                    PilotAim.Look(ref pilotAim, ref pilotViewUp, lookRight, lookUp, pilot.Forward, pilot.Up, dt);
                    break;
                case PilotControl.Helm:
                    // A/D and Q/E (C/]) are the helm's yaw and pitch: a capital ship turns rather than strafes.
                    helmLookYaw = Mathf.Repeat(helmLookYaw + look.x * PilotLookSensitivity + 180f, 360f) - 180f;
                    helmLookPitch = Mathf.Clamp(helmLookPitch - look.y * PilotLookSensitivity, -80f, 80f);
                    pilotAim = PilotAim.Helm(pilot.Forward, pilot.Up, strafeRight, strafeUp, leadRad);
                    strafeRight = strafeUp = 0;
                    break;
                default:
                    float factor = (float)controls.LookFactor;
                    pilotAim = PilotAim.Nose(pilotAim, pilot.Forward, pilot.Up, lookRight * factor, lookUp * factor, leadRad);
                    break;
            }
            var desiredForward = pilotAim;

            if (Input.ToggleFlightAssist)
            {
                pilotFlightAssistOff = !pilotFlightAssistOff;
                ShowToast(pilotFlightAssistOff ? "FLIGHT ASSIST OFF: the hull drifts on its momentum  (F to turn it back on)" : "FLIGHT ASSIST ON");
            }

            if (Input.ToggleSilent)
            {
                ship.SilentRunning = !ship.SilentRunning;
                ShowToast(ship.SilentRunning
                    ? "SILENT RUNNING: transponder off, systems down, heat damped -- a burn still shows  (F3 to light up again)"
                    : "SILENT RUNNING OFF: transponder on");
            }

            if (Input.LockTarget) ToggleLockTarget();
            if (!pilotLockTarget.IsNone && (!pilotLockTarget.IsShip || pilotLockTarget.Ship.Destroyed)) pilotLockTarget = Target.None;
            if (ship.Battle == null) pilotLockUnit = null;
            SpaceCamera.PilotLockTarget = pilotLockTarget;

            // What the world does to the hull this frame, for the helm to fly in: the pull of the bodies near it, flight assist,
            // drives knocked out by an interdiction pulse (U.SPC-T42), and a port's approach for a hauler's docking mode (U.SPC-T443).
            helmFlight.Gravity = PilotGravity();
            helmFlight.AssistOff = pilotFlightAssistOff;
            helmFlight.DrivesDown = PilotStunSeconds > 0;
            helmFlight.DockingApproach = PortApproach();
            // The hull flies behind its helm (U.SPC-T449): its class's own flight model (U.SPC-T17, docs/FLIGHT_MODELS.md), on
            // its own tanks, with what its role's controls add (a hauler's load, a capital's telegraph and weak-patch roll).
            pilotHelm.Control(new EmbodimentInput
            {
                Aim = desiredForward,
                Throttle = Input.Move.z,
                StrafeRight = strafeRight,
                StrafeUp = strafeUp,
                Roll = Input.Roll,
                Boost = Input.Boost,
                Cruise = Input.Cruise,
                Fine = Input.Slow,
                // W and S ring a capital's engine telegraph a step at a time (U.SPC-T499).
                TelegraphStep = Input.ThrottleStep,
                // The trigger and the crosshair; UpdatePilotBattle below gates them on what the hull can fire right now.
                Primary = Input.Fire && !ClickPanelOpen,
                Secondary = Input.LookHeld && !ClickPanelOpen,
            }, dt);
            // The camera's FOV kick below, and the HUD's readouts.
            pilotBoosting = helmFlight.Boosting;
            HaulerReadout(helmFlight.Model, helmFlight.Hull, helmFlight.Input);
            PilotTelegraph = capital != null ? pilot.Telegraph : null;
            SpaceCamera.PilotView = PilotViewRotation(control);
            var posBefore = helmFlight.PosBefore;
            // Hulls, stations, rocks and wreckage have volume: what the hull flies into, it hits (U.SPC-T60).
            if (!KeepPilotAboveSurfaces(ship)) return;
            if (!UpdatePilotCollisions(ship, posBefore, dt)) return;
            UpdatePropellant(ship, dt);

            UpdatePilotBattle(ship, dt);
            // What the ship's own scanners make of everything else, and what everyone else can see of it: its drive is
            // lit whenever its thrusters changed its velocity beyond what gravity did this frame (U.SPC-T66).
            bool burning = helmFlight.Burning;
            World.Sensors?.UpdatePilot(ship, Ephemeris.HelioAt(pilotFrameBody, Clock.Jd) + pilotOffsetKm, burning, Clock.Jd);
            // The law around the ship: trespass, and patrols that scan it or, if it is wanted, hail it to pay up.
            World.Holdings.PilotFrameBody = pilotFrameBody;
            World.Law.UpdatePilot(ship, pilotFrameBody, pilotOffsetKm, pilotForward, Clock.Jd);
            // Bounty hunters with a contract on the ship close in once they find it (U.SPC-T62).
            World.Outlaws.UpdatePilot(ship, pilotFrameBody, pilotOffsetKm, pilotForward, Clock.Jd);
            // Contracts worked at the helm: crates handed over, passengers put ashore, patrols flown, bodies surveyed (U.SPC-T101).
            World.Contracts?.UpdatePilot(ship, pilotFrameBody, pilotOffsetKm, Clock.Jd);
            // Hostiles that see the ship turn to run it down (U.SPC-T273).
            UpdatePursuit(ship, dt);
            // Through a gate, the hull flown on is another one, in another system: nothing more for this one this frame.
            if (UpdateGateActivation(ship)) return;
            // Its own FTL drive (U.SPC-T417): beyond the rim, pointed at a star, spooled and engaged, the hull leaves the system.
            if (UpdateHelmFtl(ship)) return;
            UpdateLaneEntry(ship);
            UpdateLaneRejoin(ship);
            ShowWarNotices();
        }

        /// <summary>Where the chase camera looks, for the hulls whose view is not simply the hull's own facing:
        /// along the crosshair in free aim, and swung about the hull by the mouse at a capital ship's helm.</summary>
        Quaternion? PilotViewRotation(PilotControl control)
        {
            switch (control)
            {
                case PilotControl.FreeAim: return ToRotation(pilotAim, pilotViewUp);
                case PilotControl.Helm: return ToRotation(pilot.Forward, pilot.Up) * Quaternion.Euler(helmLookPitch, helmLookYaw, 0);
                default: return null;
            }
        }

        /// <summary>A Unity rotation facing <paramref name="forward"/> with its roof on <paramref name="up"/> (ecliptic axes).</summary>
        static Quaternion ToRotation(DVec3 forward, DVec3 up)
        {
            var f = forward.SwapYZ;
            var u = up.SwapYZ;
            var fv = new Vector3((float)f.X, (float)f.Y, (float)f.Z);
            var uv = new Vector3((float)u.X, (float)u.Y, (float)u.Z);
            if (fv.sqrMagnitude < 1e-12f) fv = Vector3.forward;
            if (uv.sqrMagnitude < 1e-12f || Mathf.Abs(Vector3.Dot(fv.normalized, uv.normalized)) > 0.98f) uv = Mathf.Abs(fv.normalized.y) > 0.98f ? Vector3.right : Vector3.up;
            return Quaternion.LookRotation(fv, uv);
        }

        /// <summary>Real seconds between looks for hostiles that might turn to run the ship down.</summary>
        const float PursuitCheckIntervalS = 4f;
        float pursuitCheckTimer;

        /// <summary>Seconds left with the drives knocked out by a hunter's EMP out of battle (see <see cref="PilotStunSeconds"/>).</summary>
        double pilotEmpSeconds;

        /// <summary>
        /// The chase (U.SPC-T273): every few seconds, hostiles whose scanners make the ship out may turn to run it down,
        /// showing up on its scanners some hundreds of km off; every frame they close, charge and set off EMPs, and start
        /// the fight where the ship is once they are close, or give up. Time is held at real speed when they make contact.
        /// </summary>
        void UpdatePursuit(Ship ship, float dt)
        {
            var pursuit = World.Pursuit;
            if (pursuit == null) return;
            pilotEmpSeconds = Math.Max(0, pilotEmpSeconds - dt);
            double jd = Clock.Jd;
            pursuitCheckTimer -= dt;
            if (pursuitCheckTimer <= 0 && ship.Battle == null)
            {
                pursuitCheckTimer = PursuitCheckIntervalS;
                foreach (var g in pursuit.Consider(ship, pilotFrameBody, pilotOffsetKm, pilotVelKmS, jd))
                {
                    if (Clock.WarpIndex > 0) Clock.SetWarp(0);
                    ShowToast("CONTACT: " + HunterName(g) + " has turned to run you down, " + Mathf.RoundToInt((float)(g.Pos - pilotOffsetKm).Length) + " km out");
                }
            }
            if (!pursuit.Active) return;
            foreach (var (g, what) in pursuit.Step(ship, pilotFrameBody, pilotOffsetKm, pilotVelKmS, dt, jd))
            {
                switch (what)
                {
                    case PursuitEvent.EmpCharging:
                        ShowToast("EMP CHARGING on the " + g.Leader.Name + ": get more than " + Mathf.RoundToInt((float)(Pursuit.EmpRangeKm * 1.25)) + " km clear in " +
                                  Mathf.RoundToInt((float)Pursuit.EmpChargeSeconds) + " s");
                        break;
                    case PursuitEvent.EmpHit:
                        pilotEmpSeconds = Pursuit.EmpStunSeconds;
                        pilotCruise = default;
                        ShowToast("EMP from the " + g.Leader.Name + ": drives down for " + Mathf.RoundToInt((float)Pursuit.EmpStunSeconds) + " s");
                        break;
                    case PursuitEvent.EmpMissed:
                        ShowToast("The " + g.Leader.Name + "'s EMP goes off short of you");
                        break;
                    case PursuitEvent.Engage:
                        var battle = World.War.TryStartPursuit(ship, g, pilotFrameBody, pilotOffsetKm, pilotForward, jd);
                        if (battle == null) break;
                        pursuit.Engaged(g);
                        EnterBattleTime(battle);
                        ShowToast(HunterName(g) + " engages: fight or run");
                        break;
                    case PursuitEvent.GaveUp:
                        ShowToast("The " + g.Leader.Name + " has given up the chase");
                        break;
                }
            }
        }

        static string HunterName(Pursuer g) =>
            "the " + g.Faction.Name + " " + g.Leader.Spec.Class.ToString().ToLowerInvariant() + " " + g.Leader.Name + (g.Ships.Count > 1 ? " and " + (g.Ships.Count - 1) + " more" : "");

        /// <summary>The pull on the piloted hull of every body it is close to (see <see cref="PilotFlight.Gravity"/>),
        /// km/s² -- its own frame's body above all, but a moon or a neighbour too when it flies near one.</summary>
        DVec3 PilotGravity()
        {
            var helio = Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm;
            var g = DVec3.Zero;
            for (int b = 0; b < Catalog.Count; b++)
            {
                var body = Catalog[b];
                if (body.Gm > 0) g += PilotFlight.Gravity(helio - Ephemeris.Helio(b), body.Gm, body.RadiusKm);
            }
            return g;
        }

        /// <summary>A hull its thrusters cannot hold up sinks onto the surface and rests there rather than
        /// falling through it -- any body it is close enough to feel, not only its own frame's. Coming down
        /// hard does damage (see <see cref="SurfaceImpact"/>); false if the hull did not survive it.</summary>
        bool KeepPilotAboveSurfaces(Ship ship)
        {
            var frameHelio = Ephemeris.Helio(pilotFrameBody);
            for (int b = 0; b < Catalog.Count; b++)
            {
                var body = Catalog[b];
                if (body.Gm <= 0) continue;
                var centre = Ephemeris.Helio(b) - frameHelio;
                if ((pilotOffsetKm - centre).Length > body.RadiusKm * PilotFlight.GravityReachRadii) continue;
                if (!PilotFlight.KeepAboveSurface(ref pilot, centre, body.RadiusKm + Math.Max(0.5, ship.LengthKm), out double sink)) continue;
                if (!SurfaceImpact(ship, b, (pilotOffsetKm - centre).Normalized, sink)) return false;
            }
            return true;
        }

        /// <summary>Warns as the piloted ship's tanks run low and dry, and brings a called tanker alongside when it is due.</summary>
        void UpdatePropellant(Ship ship, float dt)
        {
            if (TankerDueS >= 0)
            {
                TankerDueS -= dt;
                if (TankerDueS < 0)
                {
                    double tonnes = Propulsion.TankerLoad(ship);
                    double fee = Propulsion.TankerFee(tonnes);
                    ship.Propellant += tonnes;
                    ship.Faction.Treasury -= fee;
                    ShowToast("A tanker comes alongside: " + tonnes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " t of reaction mass for " +
                        fee.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " cr");
                }
            }
            int level = ship.Propellant <= 1e-6 ? 2 : ship.PropellantFraction < 0.15 ? 1 : 0;
            if (level > propellantWarning)
                ShowToast(level == 2 ? "PROPELLANT DRY: drifting. Refuel at a port's market, or call a tanker" : "Propellant low: " + Mathf.RoundToInt((float)(ship.PropellantFraction * 100)) + "% left");
            propellantWarning = level;
        }

        /// <summary>Calls an emergency tanker out to the piloted ship (dearer than a port: a call-out fee and three times the going rate).</summary>
        public void CallTanker()
        {
            if (CoopSends("tanker")) return; // US-T43: into the frames (the tanker's timer is the shared helm's)
            if (!IsPiloting || TankerDueS >= 0) return;
            var ship = Piloted.Ship;
            if (ship == null || Propulsion.TankerLoad(ship) < 1) { ShowToast("Tanks are full"); return; }
            TankerDueS = TankerCallS;
            ShowToast("Tanker called: about " + Mathf.RoundToInt(TankerCallS) + " s out, " +
                Propulsion.TankerFee(Propulsion.TankerLoad(ship)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " cr to fill the tanks");
        }

        /// <summary>
        /// While piloting, checks (and on the player's command, performs) a transit through whichever
        /// of this system's jump gates the ship is in range of (one that is shut will not open): a flyable, in-world alternative to the menu-driven STAR SYSTEMS jump (see
        /// <see cref="StarTravel"/>, untouched -- the camera still uses it on its own). The eligibility
        /// and position-handoff math is Core's (<see cref="GateTransit"/>); this just wires the
        /// activation key and re-seeds the pilot session onto the arriving hull in its new system,
        /// exactly as <see cref="TogglePiloting"/> seeds one at the start.
        /// </summary>
        bool UpdateGateActivation(Ship ship)
        {
            if (ActiveSystem.Gates.Count == 0 || ship.Battle != null) { nearGate = null; return false; }
            var shipHelioKm = Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm;
            var wasNear = nearGate;
            var gate = nearGate = ActiveSystem.GateInRange(shipHelioKm);
            if (gate != null && gate != wasNear)
                ShowToast("In range of " + gate.Name.ToUpperInvariant() + " to " + gate.Destination.Name.ToUpperInvariant() +
                          (gate.Admits(ship.Faction, Clock.Jd) && gate.Partner.Admits(ship.Faction, Clock.Jd) ? " (Y to activate)" : " (shut: under blockade)"));
            if (gate == null || !Input.ActivateGate) return false;
            // A blockaded gate (or one whose far end is) will not open for the pilot either.
            if (!gate.Link.Open(ActiveSystem, ship.Faction, Clock.Jd)) { ShowToast(gate.Name.ToUpperInvariant() + " is shut to " + ship.Faction.Name); return false; }

            var destination = gate.Destination;
            if (!GateTransit.TryPerform(gate, shipHelioKm, ship, World, Clock.Jd, out var arrived)) return false;
            CaptureEvents.GateDeparture(ship.Name, gate.Name, ActiveSystem.Name, destination.Name); // US-T72
            AdvanceCourse(gate, arrived);
            // A charge is lost going through a gate (U.SPC-T417).
            StopFtl();
            EndSalvage(null);
            SalvageApproach = null;

            World.PilotedShip = null;
            // The system left behind no longer sees the pilot, nor holds a patrol's hail on it.
            World.Sensors?.UpdatePilot(null, default, false, Clock.Jd);
            World.Law?.UpdatePilot(null, 0, default, default, Clock.Jd);
            // Through the gate, the chase is left behind in the system the ship has left.
            World.Pursuit?.ReleaseAll(Clock.Jd);
            // US-T58: the far system's world holds the hull before Activate brings that system up to date (Galaxy.Promote
            // steps its fleet): left unheld, the arrival GateTransit parked with no destination (DestinationBody -1, its phase
            // already over) departs for body -1 there, and the catalogue lookup throws every frame from then on.
            destination.World.PilotedShip = arrived;
            Activate(destination);
            // The far side streams in behind the gate's own flare and fold (U.SPC-T414).
            BeginHelmGateCover();
            Piloted = Target.OfShip(arrived);
            World.PilotedShip = arrived;
            pilotFrameBody = arrived.Orbit.Body;
            SeedPilot(arrived.Orbit.Center, DVec3.Zero, arrived.Orbit.V);
            pilotBattleCheckTimer = 0;
            pilotLockTarget = Target.None;
            pilotLockUnit = null;
            SpaceCamera.PilotLockTarget = Target.None;
            nearGate = null;
            // Arriving beside an on-ramp is not flying in through it.
            insideRampGate = true;
            // A tanker called in the old system does not follow the hull through the gate.
            TankerDueS = -1;

            Select(Piloted);
            if (CoopHelmMine) SpaceCamera.EnterPilotChase(Piloted, 6); // US-T43: the holder's view only
            ShowToast("Jumped to " + destination.Name.ToUpperInvariant() + " through the gate");
            return true;
        }

        /// <summary>Starts (or joins) a fight the instant piloting brings the ship within range of a
        /// hostile -- mirroring what <see cref="War.OnArrived"/> does for every AI ship, since a player
        /// flying there is otherwise invisible to that check (see <see cref="War.PresentGroups"/>) -- and,
        /// every frame afterward, hands the battle this frame's actual flown pose and fire/lock input
        /// instead of letting its own AI steer and pick targets for the hull.</summary>
        void UpdatePilotBattle(Ship ship, float dt)
        {
            if (ship.Battle != null && Salvage != null) EndSalvage("Clamps released: battle stations");
            if (ship.Battle != null) SalvageApproach = null;
            if (ship.Battle == null)
            {
                // Out of any fight: a hail left open when the last one ended (or before release) is gone,
                // so the next battle's demand or surrender gets its own prompt.
                demandPrompted = false;
                termsPrompted = null;
                // Nor is a trigger held as the last fight ended still held: the next one would open with a volley unasked.
                helmTriggerWas = false;
                // A patrol's order to heave to has the number keys (1 pays the fine) even alongside or latched to a hulk.
                bool salvaging = UpdateSalvage(ship, dt);
                bool hailed = UpdateLawHail(ship);
                if (!salvaging && !hailed) UpdateHavenAssault(ship);
                // Holding fire on a locked ship the player's flag has no quarrel with opens fire on it anyway: an
                // attack on a lawful ship, a crime if anyone sees it. An enemy is fought as usual (below), and one
                // alone with nobody armed to start that fight, a lone merchant, is attacked the same way.
                var locked = pilotLockTarget.IsShip ? pilotLockTarget.Ship : null;
                if (locked != null && Input.Fire && !pilotCruise.Active && !ClickPanelOpen)
                {
                    bool enemy = World.Diplomacy.Hostile(ship.Faction, locked.Faction);
                    if (World.War.TryStartPlayerAttack(ship, locked, pilotFrameBody, pilotOffsetKm, pilotForward, Clock.Jd) != null)
                    {
                        ShowToast(enemy ? "Weapons free on the " + locked.Name
                            : "Weapons free on the " + locked.Name + ": an attack on a lawful ship is a crime if anyone sees it");
                        return;
                    }
                }
                pilotBattleCheckTimer -= dt;
                if (pilotBattleCheckTimer > 0) return;
                pilotBattleCheckTimer = PilotBattleCheckIntervalS;
                // The piloted ship's scanners: a hidden haven within range goes on the map.
                foreach (var found in World.War.ScanFrom(ship.Faction, Ephemeris.HelioAt(pilotFrameBody, Clock.Jd) + pilotOffsetKm, Clock.Jd, ship.Name, Outfitting.ScanRangeKm(ship)))
                    ShowToast("Scanners pick out a hidden pirate haven: " + found.Name + " on " + Catalog[found.Body].Name);
                World.War.TryStartPlayerBattle(ship, pilotFrameBody, pilotOffsetKm, pilotForward, Clock.Jd);
                return;
            }

            var unit = ship.Battle.UnitOf(ship);
            if (unit == null) return;
            unit.PlayerControlled = true;
            unit.Pos = pilotOffsetKm - ship.Battle.Anchor;
            unit.Vel = pilotVelKmS;
            unit.Forward = pilotForward;
            // Rolled at the helm, its fire cones roll with it (U.SPC-T499, U.SPC-T533).
            unit.Up = pilot.Up;
            // No firing while cruising -- the cruise drive is a travel tier, not a combat one.
            // Nor while a pulse has the hull's systems down.
            bool armed = !pilotCruise.Active && !ClickPanelOpen && unit.Stunned <= 0;
            unit.PlayerFireHeld = Input.Fire && armed;
            // The right button launches missiles once the lock is made (U.SPC-T270); the guns fire down the crosshair.
            unit.PlayerSecondaryHeld = Input.LookHeld && armed;
            unit.PlayerAim = pilotAim;
            // At a capital ship's helm the trigger calls broadsides (U.SPC-T272): held, the battery bearing where the
            // player looks builds a volley; let go, it fires.
            bool helm = PilotControlMode == PilotControl.Helm;
            unit.PlayerHelm = helm;
            unit.PlayerVolleyAim = helm ? PilotLookDirection : DVec3.Zero;
            if (helm && helmTriggerWas && !unit.PlayerFireHeld) unit.PlayerVolleyRelease = true;
            helmTriggerWas = helm && unit.PlayerFireHeld;
            // The rest of the arsenal (U.SPC-T271): the EMP on the middle button, a mine on Ctrl; an auto turret needs no hand.
            unit.PlayerEmpHeld = Input.FireEmp && armed;
            if (Input.DropMine && armed) unit.PlayerMineRequested = true;
            // A fighter locked in this fight is held as its unit; a ship as itself, so the lock outlives the fight.
            if (pilotLockUnit != null && (!pilotLockUnit.Present || !ship.Battle.Units.Contains(pilotLockUnit))) pilotLockUnit = null;
            var lockUnit = pilotLockUnit ?? (pilotLockTarget.IsShip ? ship.Battle.UnitOf(pilotLockTarget.Ship) : null);
            if (lockUnit != unit.PlayerLockTarget) unit.LockProgress = 0;
            unit.PlayerLockTarget = lockUnit;
            UpdateSurrenderPrompts(ship.Battle, unit);
            // A fight aboard, this hull's own or one its marines are in (U.SPC-T468).
            UpdateBoardingCommand(ship.Battle, unit);
            // A capital ship's captain's orders: batteries, shields, brace, damage control, the line (U.SPC-T425).
            UpdateHelmCommand(ship.Battle, unit);
            // The phaser arrays' tuning, on any hull that carries them (U.SPC-T488).
            if (Input.TunePhasers && EnergyWeapons.HasPhasers(unit.Combat))
                ShowToast("Phaser arrays tuned to a " + EnergyWeapons.Label(EnergyWeapons.Retune(unit.Combat)));
            // Run hot or throttle down, on a big ship with radiators to manage (U.SPC-T491).
            if (Input.ToggleRunHot && Radiators.Carries(unit.Combat) && Broadsides.Of(unit) is BroadsideCommand cmd)
            {
                cmd.RunHot = !cmd.RunHot;
                ShowToast(cmd.RunHot ? "Run hot: guns and drive at full, whatever the heat does" : "Throttle down for the heat as needed");
            }
            // Roll the weak shield patches away from the heaviest fire, on a capital with patches to choose between (U.SPC-T502).
            if (Input.ToggleRollPatches && PilotControlMode == PilotControl.Helm && Broadsides.Commands(unit) && WeakPatches.HasPatches(unit.Combat))
                ShowToast(WeakPatches.ToggleOrder(unit)
                    ? "Helm: roll our weak patches away from their fire (steer as you like; Z/X overrides it while held)"
                    : "Helm: the roll is yours");
        }

        /// <summary>
        /// Flying over a pirate haven whose garrison platforms are gone (and with no raiders holding it):
        /// the number keys finish the assault, 1 to board and take it (the group needs marines), 2 to
        /// destroy it (see <see cref="War.AssaultHaven"/>).
        /// </summary>
        void UpdateHavenAssault(Ship ship)
        {
            Settlement haven = null;
            if (pilotOffsetKm.Length <= ScaleModel.NeighbourhoodKm(Catalog[pilotFrameBody]))
                foreach (var s in Economy.At(pilotFrameBody))
                    if (World.War.CanAssault(s, ship)) haven = s;
            if (haven == null)
            {
                havenPrompted = null;
                return;
            }
            bool marines = World.War.HasMarines(ship);
            // Destroying it takes guns (or marines to set the charges), as AssaultHaven requires.
            bool canDestroy = marines || ship.Controller.Combat.Armed;
            if (havenPrompted != haven)
            {
                havenPrompted = haven;
                ShowToast(!canDestroy ? haven.Name + "'s defences are down, but this hull has neither guns nor marines to finish it"
                    : haven.Name + "'s defences are down:  " + (marines ? "1 board and take it, " : "") + "2 destroy it");
            }
            int key = Input.NumberKey;
            if ((key == 1 && marines) || (key == 2 && canDestroy))
            {
                bool capture = key == 1;
                if (World.War.AssaultHaven(haven, ship, capture, Clock.Jd))
                    ShowToast(capture ? "Boarders take " + haven.Name + "; its raiders scatter" : haven.Name + " is destroyed; its raiders scatter");
                havenPrompted = null;
            }
        }

        Settlement havenPrompted;

        // ---------------------------------------------------------------- salvage and boarding of hulks

        /// <summary>A hulk's position and velocity in the pilot's frame (true km, km/s), from where it is at two nearby moments.</summary>
        (DVec3 offsetKm, DVec3 velKmS) HulkInPilotFrame(Func<double, DVec3> helioAt)
        {
            const double h = 1e-5;
            double jd = Clock.Jd;
            var now = helioAt(jd) - Ephemeris.HelioAt(pilotFrameBody, jd);
            var next = helioAt(jd + h) - Ephemeris.HelioAt(pilotFrameBody, jd + h);
            return (now, (next - now) / (h * Astro.SecondsPerDay));
        }

        static DVec3 DerelictHelio(Ephemeris eph, Ship s, double jd) => eph.HelioAt(s.Orbit.Body, jd) + s.Orbit.OffsetAt(jd);

        static string Num(double v, string format) => v.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Salvage while piloting, out of any battle: the nearest derelict (<see cref="Ship.Derelict"/>) or wreck
        /// gets an approach readout; matched alongside it (<see cref="SalvageSession.Check"/>), 1 latches on.
        /// Latched, the ship rides with the hulk and the number keys choose: with raiders aboard, 1 sends the
        /// marines through the airlock; once it is clear, 1 salvages cargo into the grid hold, 2 strips the
        /// most useful component, 3 claims the hull if it can fly; 4 (or throttling away) casts off. Returns
        /// true while it has the number keys (latched, or alongside and offering to latch).
        /// </summary>
        bool UpdateSalvage(Ship ship, float dt)
        {
            if (Salvage != null) return UpdateLatched(ship, dt);

            // The nearest hulk in reach of the readout.
            Ship derelict = null;
            Wreck wreck = null;
            DVec3 rel = default, relVel = default;
            double best = SalvageApproachKm;
            foreach (var s in Fleet.Ships)
            {
                if (!s.Derelict || s.Destroyed || s == ship || s.Orbit == null) continue;
                var (off, vel) = HulkInPilotFrame(jd => DerelictHelio(Ephemeris, s, jd));
                double d = (off - pilotOffsetKm).Length;
                if (d >= best) continue;
                best = d;
                derelict = s;
                rel = pilotOffsetKm - off;
                relVel = pilotVelKmS - vel;
            }
            foreach (var w in World.War.Wrecks.Items)
            {
                var (off, vel) = HulkInPilotFrame(jd => World.WreckHelio(w, jd));
                double d = (off - pilotOffsetKm).Length;
                if (d >= best) continue;
                best = d;
                wreck = w;
                derelict = null;
                rel = pilotOffsetKm - off;
                relVel = pilotVelKmS - vel;
            }
            if (derelict == null && wreck == null)
            {
                SalvageApproach = null;
                salvagePrompted = null;
                return false;
            }

            string name = derelict != null ? "DERELICT " + derelict.Name : "WRECK " + wreck.Name;
            double hulkLength = derelict != null ? derelict.LengthKm : wreck.LengthKm;
            double range = SalvageSession.LatchRangeKm(hulkLength, ship.LengthKm);
            var state = SalvageSession.Check(rel, relVel, range);
            double closing = rel.LengthSquared > 0 ? -DVec3.Dot(relVel, rel.Normalized) : 0;
            string how = state == LatchState.Ready ? "[1] LATCH ON"
                : state == LatchState.TooFast ? "match velocity: " + Num(relVel.Length * 1000, "0") + " m/s, under " + Num(SalvageSession.MatchSpeedKmS * 1000, "0") + " to latch"
                : "come within " + Num(range, "0.0") + " km and match velocity to latch";
            SalvageApproach = name + "   " + Num(best, "0.0") + " km   " + (closing >= 0 ? "closing " : "opening ") + Num(Math.Abs(closing) * 1000, "0") + " m/s   " + how;
            if (state != LatchState.Ready)
            {
                salvagePrompted = null;
                return false;
            }
            object hulk = (object)derelict ?? wreck;
            if (salvagePrompted != hulk)
            {
                salvagePrompted = hulk;
                ShowToast("Alongside " + (derelict != null ? "the derelict " + derelict.Name : "the wreck of " + wreck.Name) + ", velocity matched:  1 latch on");
            }
            if (SalvageKey != 1) return true;
            Salvage = derelict != null ? SalvageSession.Latch(ship, derelict) : SalvageSession.Latch(ship, wreck);
            if (Salvage == null) return true;
            salvageLatchOffsetKm = rel;
            salvageLogShown = 0;
            SalvageApproach = null;
            ShowSalvageLog();
            return true;
        }

        /// <summary>The number key for the salvage choices: none while a patrol's interdiction has the keys.</summary>
        int SalvageKey => World.Law?.Hail?.Interdiction == true ? -1 : Input.NumberKey;

        bool UpdateLatched(Ship ship, float dt)
        {
            var s = Salvage;
            bool hulkGone = s.Derelict != null ? s.Derelict.Destroyed || !s.Derelict.Derelict : !World.War.Wrecks.Items.Contains(s.Wreck);
            if (hulkGone) { EndSalvage("The hulk broke up under you: clamps released"); return false; }
            // Throttling or strafing away pulls the clamps off.
            if (Input.Move.sqrMagnitude > 0.25f) { EndSalvage("Clamps released"); return false; }

            // Riding with the hulk: held at the offset the clamps took hold at, moving as it moves.
            var (off, vel) = s.Derelict != null
                ? HulkInPilotFrame(jd => DerelictHelio(Ephemeris, s.Derelict, jd))
                : HulkInPilotFrame(jd => World.WreckHelio(s.Wreck, jd));
            pilotOffsetKm = off + salvageLatchOffsetKm;
            pilotVelKmS = vel;
            pilotCruise = default;

            // The fight aboard runs on the sim clock (real time while piloting, faster under warp).
            s.Step(Math.Min(10, dt * Clock.Warp.SimSecondsPerSecond * Clock.Speed));
            ShowSalvageLog();

            int key = SalvageKey;
            if (key == 4) { EndSalvage("Clamps released"); return true; }
            if (s.Hostile)
            {
                if (key == 1 && !s.Fighting && !s.SendBoarders()) ShowToast("No marines aboard to send across");
                return true;
            }
            if (key == 1)
            {
                if (s.SalvageCargo() > 0) ShowCargo = true;
            }
            else if (key == 2)
            {
                var part = s.BestPart();
                if (part == null) ShowToast("Nothing left aboard worth stripping");
                else s.Strip(part);
            }
            else if (key == 3)
            {
                if (!s.CanClaim(out var why)) ShowToast("Can't claim " + s.Name + ": " + why);
                else if (s.Claim(Fleet, Clock.Jd))
                {
                    var claimed = s.Derelict;
                    EndSalvage(null);
                    ShowToast("Claimed: the " + claimed.Name + " flies your colours (select it and press P to take its helm)");
                }
            }
            return true;
        }

        /// <summary>Toasts each new line of the latched session's log.</summary>
        void ShowSalvageLog()
        {
            var log = Salvage?.Log;
            if (log == null) return;
            if (salvageLogShown > log.Count) salvageLogShown = log.Count;
            if (salvageLogShown == log.Count) return;
            ShowToast(log[log.Count - 1]);
            salvageLogShown = log.Count;
        }

        /// <summary>The number-key choices open on the latched hulk right now, for the HUD.</summary>
        public string SalvageChoices()
        {
            var s = Salvage;
            if (s == null) return "";
            if (s.Hostile)
                return s.Fighting ? "Marines fighting aboard   4 cast off"
                    : (s.MarinesReady >= 0.5 ? "1 send " + Num(s.MarinesReady, "0") + " marines across   " : "No marines to send   ") + "4 cast off";
            return "1 salvage cargo   2 strip a component   3 claim the hull   4 cast off";
        }

        /// <summary>Lets go of the latched hulk: the session ends, is logged, and pays out any adventure on it.</summary>
        void EndSalvage(string toast)
        {
            var s = Salvage;
            if (s == null) return;
            Salvage = null;
            salvagePrompted = null;
            var op = World.FinishSalvage(s, Clock.Jd);
            if (op != null) ShowToast(op.Outcome);
            else if (toast != null) ShowToast(toast);
        }

        /// <summary>
        /// While piloting in a battle, the number keys answer hails: a pirate demand for this ship's cargo
        /// (1 hand it over, 2 refuse), or an enemy hull that has struck its colours to this side (1 demand
        /// its cargo, 2 take the ship, 3 let it go). Unanswered, the battle settles it after
        /// <see cref="Battle.SurrenderDecisionSeconds"/>.
        /// </summary>
        void UpdateSurrenderPrompts(Battle battle, BattleUnit unit)
        {
            int key = Input.NumberKey;
            if (unit.CargoDemandedBy != null)
            {
                if (!demandPrompted)
                {
                    demandPrompted = true;
                    ShowToast(unit.CargoDemandedBy.Name + " demand your cargo:  1 hand it over and go, 2 refuse and fight");
                }
                if (key == 1 && battle.AnswerCargoDemand(unit, true)) ShowToast("Cargo handed over; the pirates let you go");
                else if (key == 2 && battle.AnswerCargoDemand(unit, false)) ShowToast("Demand refused: weapons free");
                return;
            }
            demandPrompted = false;

            var hailing = battle.AwaitingTermsFrom(unit.Side);
            if (hailing == null)
            {
                termsPrompted = null;
                return;
            }
            if (termsPrompted != hailing)
            {
                termsPrompted = hailing;
                ShowToast("The " + hailing.Name + " strikes its colours:  1 demand its cargo, 2 take the ship, 3 let it go");
            }
            SurrenderTerms? terms = key == 1 ? SurrenderTerms.RansomCargo : key == 2 ? SurrenderTerms.TakeShip : key == 3 ? SurrenderTerms.LetGo : (SurrenderTerms?)null;
            if (terms == null) return;
            if (battle.OfferSurrenderTerms(hailing, terms.Value))
                ShowToast(terms == SurrenderTerms.RansomCargo ? "The " + hailing.Name + " hands over its cargo and is let go"
                    : terms == SurrenderTerms.TakeShip ? "Boarders away to take the " + hailing.Name
                    : "The " + hailing.Name + " is let go");
            else ShowToast("Nobody on this side can board the " + hailing.Name);
        }

        /// <summary>Locks the camera's tracking onto whatever's hovered/selected, or clears an existing
        /// lock -- the ship's own facing stays fully under the player's mouse control either way (the
        /// camera itself leans toward the lock in <see cref="Cameras.SpaceCamera"/>'s focus update).</summary>
        void ToggleLockTarget()
        {
            if (!pilotLockTarget.IsNone || pilotLockUnit != null)
            {
                pilotLockTarget = Target.None;
                pilotLockUnit = null;
                return;
            }
            // The piloted ship is itself the selection while piloting, so it can't stand in the way of the hovered one.
            bool Other(Target c) => c.IsShip && c.Ship != Piloted.Ship;
            var t = Other(Selected) ? Selected : Other(Hovered) ? Hovered : Target.None;
            if (t.IsShip)
            {
                pilotLockTarget = t;
                return;
            }
            // In a fight, the hostile nearest the crosshair: a fighter as readily as a ship (U.SPC-T271).
            var u = UnitNearestCrosshair();
            if (u == null) return;
            if (u.Ship != null) pilotLockTarget = Target.OfShip(u.Ship);
            else pilotLockUnit = u;
        }

        /// <summary>Whether the helm's trigger was held last frame, so letting go of it fires the volley.</summary>
        bool helmTriggerWas;

        /// <summary>Where the player is looking (ecliptic, unit): around a capital ship at its helm, else down the crosshair.</summary>
        public DVec3 PilotLookDirection
        {
            get
            {
                if (PilotControlMode != PilotControl.Helm) return pilotAim;
                var v = (ToRotation(pilot.Forward, pilot.Up) * Quaternion.Euler(helmLookPitch, helmLookYaw, 0)) * Vector3.forward;
                return new DVec3(v.x, v.y, v.z).SwapYZ.Normalized;
            }
        }

        /// <summary>A fighter locked in the flown hull's fight (a ship is held in <see cref="pilotLockTarget"/>).</summary>
        BattleUnit pilotLockUnit;

        /// <summary>The hostile in the flown hull's fight that sits nearest the crosshair, within <see cref="LockPickConeDeg"/> of it.</summary>
        BattleUnit UnitNearestCrosshair()
        {
            var battle = Piloted.Ship?.Battle;
            var me = battle?.UnitOf(Piloted.Ship);
            if (me == null) return null;
            BattleUnit best = null;
            double bestCos = Math.Cos(LockPickConeDeg * Mathf.Deg2Rad);
            foreach (var o in battle.Units)
            {
                if (o == me || !o.Present || !battle.Hostile(me, o)) continue;
                var to = o.Pos - me.Pos;
                if (to.LengthSquared < 1e-12) continue;
                double cos = DVec3.Dot(pilotAim, to.Normalized);
                if (cos > bestCos)
                {
                    bestCos = cos;
                    best = o;
                }
            }
            return best;
        }

        const double LockPickConeDeg = 15;

        /// <summary>The flown hull's own battle unit, if it is in a fight.</summary>
        public BattleUnit PilotUnit => IsPiloting && Piloted.Ship?.Battle != null ? Piloted.Ship.Battle.UnitOf(Piloted.Ship) : null;

        /// <summary>The locked hull's battle unit, if it is in the flown hull's fight.</summary>
        public BattleUnit PilotLockUnit => PilotUnit?.PlayerLockTarget;

        /// <summary>Camera elevation of the system overview, degrees.</summary>
        public const float OverviewPitch = 52f;
    }
}
