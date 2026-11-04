using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    public enum UnitKind
    {
        Ship,
        Platform,
        Fighter,
    }

    /// <summary>One combatant in a battle. Positions are km from the battle's anchor, ecliptic axes.</summary>
    public sealed class BattleUnit
    {
        public int Id;
        public UnitKind Kind;
        public int Side;
        public Faction Faction;
        public Ship Ship;
        public Settlement Platform;

        /// <summary>The gate a fort unit guards (U.SPC-T92); its <see cref="Kind"/> is <see cref="UnitKind.Platform"/>, with no settlement.</summary>
        public JumpGate Fort;
        public ShipCombat Combat;
        public double LengthKm;

        public DVec3 Pos, Vel;
        public DVec3 Forward = DVec3.UnitX;

        /// <summary>How fast the AI is swinging the nose, rad/s about a world axis across it (U.SPC-T557, <see cref="Battle.TurnToward"/>);
        /// for a crew in a dogfight, its flight model's spin (U.SPC-T440).</summary>
        public DVec3 TurnSpin;

        /// <summary>True while a human is flying this unit's <see cref="Ship"/> directly: <see cref="Battle.Move"/>
        /// skips its own AI steering/position-integration for it (the Runtime pilot loop drives <see cref="Pos"/>,
        /// <see cref="Vel"/> and <see cref="Forward"/> instead every frame) and <see cref="Battle.Fire"/> routes its
        /// forward-facing weapons through <see cref="PlayerFireHeld"/>/<see cref="PlayerLockTarget"/> rather than
        /// AI target selection. Side/aft turrets and point defence are unaffected and keep firing autonomously.</summary>
        public bool PlayerControlled
        {
            get => playerControlled;
            set
            {
                playerControlled = value;
                if (value) PlayerFlown = true;
            }
        }

        bool playerControlled;

        /// <summary>A human flew this unit at some point in the battle (kept after <see cref="PlayerControlled"/> is
        /// cleared, as when the hull is captured), so the law knows whose deeds its were (see <see cref="Law.OnBattleEnded"/>).</summary>
        public bool PlayerFlown { get; private set; }

        /// <summary>While <see cref="PlayerControlled"/>: whether the player is holding the fire trigger this frame.</summary>
        public bool PlayerFireHeld;

        /// <summary>While <see cref="PlayerControlled"/>: the target a forward missile launcher fires at, once
        /// <see cref="LockProgress"/> has made the lock. Direct-fire weapons ignore this and fire down the
        /// crosshair (<see cref="PlayerAim"/>) instead.</summary>
        public BattleUnit PlayerLockTarget;

        /// <summary>While <see cref="PlayerControlled"/>: where the crosshair points (unit, ecliptic axes), the
        /// line the player's guns swing to meet (<see cref="Gunnery"/>, U.SPC-T270). Zero means down the nose.</summary>
        public DVec3 PlayerAim;

        /// <summary>While <see cref="PlayerControlled"/>: the missile trigger held this frame.</summary>
        public bool PlayerSecondaryHeld;

        /// <summary>The player's guns' heat, 0-1: firing builds it, and once it reaches 1 they are
        /// <see cref="Overheated"/> until it has fallen below <see cref="Gunnery.HeatRecoverBelow"/>.</summary>
        public double Heat;
        public bool Overheated;

        /// <summary>A big ship's heat (U.SPC-T491, see <see cref="Radiators"/>): 0 cold, 1 with its heat sinks at their
        /// limit, past which its crew and systems cook. Zero always for a hull with no radiators to manage.</summary>
        public double HullHeat;

        /// <summary>The missile lock on <see cref="PlayerLockTarget"/>, 0-1: made at 1 (see <see cref="Gunnery.LockSeconds"/>).</summary>
        public double LockProgress;

        /// <summary>A dogfighting small craft's manoeuvre (see <see cref="Battle.Dogfight"/>, U.SPC-T274), the seconds
        /// it holds it for, and the way it breaks.</summary>
        public DogfightManeuver Maneuver;
        public double ManeuverLeft;
        public int BreakSide = 1;

        /// <summary>The velocity gunners read this hull as having: its real one, eased in over
        /// <see cref="Gunnery.TrackingLagS"/>, so a steady course is tracked and a sudden jink is not (U.SPC-T270).</summary>
        public DVec3 SeenVel;

        public BattleUnit Target;
        public double RetargetIn;
        public bool Fled, Dead;
        public bool Fleeing;

        /// <summary>True once this hull has struck its colours: holds fire and waits on its captor's
        /// terms (<see cref="SurrenderOutcome"/>) rather than fighting to the last. See <see cref="Morale"/>.</summary>
        public bool Surrendering;

        /// <summary>Seconds spent hailing since <see cref="Surrendering"/> began; past
        /// <see cref="Battle.SurrenderDecisionSeconds"/> the battle decides terms itself if nobody chooses first.</summary>
        public double SurrenderTimer;

        /// <summary>What its captor decided once it struck its colours, or null while still awaiting terms.</summary>
        public SurrenderTerms? SurrenderOutcome;

        /// <summary>Set once its captor chooses to take the ship: open to boarding exactly like a disabled hull.</summary>
        public bool SurrenderOpenToBoarding;

        /// <summary>This unit's own breaking point, 0-1, rolled once at creation so otherwise-identical
        /// hulls do not all strike their colours on the same tick under the same pressure. See <see cref="Morale.Pressure"/>.</summary>
        public double MoraleThreshold;

        /// <summary>Pirates hailing this hull with "your cargo or your life": who is asking, or null. Answered through
        /// <see cref="Battle.AnswerCargoDemand"/> (at once by an AI hull); unanswered, it lapses into a fight.</summary>
        public Faction CargoDemandedBy;

        /// <summary>Seconds the current <see cref="CargoDemandedBy"/> has gone unanswered.</summary>
        public double CargoDemandTimer;

        /// <summary>Set once a cargo demand has been made of this hull, so pirates ask only once a battle.</summary>
        public bool CargoDemandMade;

        /// <summary>Set when this hull turned down (or let lapse) a pirate cargo demand: the raiders no longer spare it once
        /// it lies dead in space (U.SPC-T374, see <see cref="RaidDoctrine"/>).</summary>
        public bool CargoRefused;

        /// <summary>Formation: the unit this one keeps station on, and where (km, in the leader's frame).</summary>
        public BattleUnit Leader;
        public DVec3 Slot;

        /// <summary>Per-unit randomness: circling direction and a phase for station-keeping wander.</summary>
        public double Wander, Circle;

        /// <summary>Fighter-only state (see the fighter AI).</summary>
        public FighterState Fighter;

        /// <summary>The carrier's craft this launched unit is (U.SPC-T526, see <see cref="CarrierWings"/>): the hull it flies
        /// on (its <see cref="Combat"/> is that hull's own) and docks again as when it lands. Null for any other unit (it is set for a
        /// carrier's boarding shuttles, U.SPC-T590). Not <see cref="Ship"/>, which is only ever a hull in a fleet's list.</summary>
        public Ship CraftHull;

        /// <summary>Boarding: the disabled enemy this ship is closing on to board, and the one it is latched to.</summary>
        public BattleUnit BoardTarget, Latched;

        /// <summary>Where it is fixed to the latched hull (ship lengths in that hull's frame), and the countdown to opening the airlock.</summary>
        public DVec3 LatchLocal;
        public double LatchTimer;
        /// <summary>Which flank of its boarding target this unit docks on (+1 starboard, -1 port), fixed when the approach starts (U.SPC-T567).</summary>
        public double DockSide;
        BattleUnit dockSideFor;

        /// <summary>The dock point off <paramref name="t"/>'s flank facing this unit when it began closing, kept thereafter so it never flies through the hull.</summary>
        public DVec3 DockPoint(BattleUnit t, double standOffKm)
        {
            t.Basis(out var right, out _);
            if (dockSideFor != t || DockSide == 0)
            {
                dockSideFor = t;
                DockSide = DVec3.Dot(Pos - t.Pos, right) >= 0 ? 1 : -1;
            }
            return t.Pos + right * (DockSide * (t.Radius * 0.35 + Radius + standOffKm));
        }

        /// <summary>Seconds until its Breacher bays can fire the next Breacher round (U.SPC-T466, <see cref="BreacherBays.IntervalSeconds"/>).</summary>
        public double BreacherBayReload;

        /// <summary>Seconds until a carrier can send its next boarding shuttle out of its hangar (U.SPC-T486, <see cref="BoardingShuttles"/>).</summary>
        public double ShuttleLaunchIn;

        /// <summary>Taken by boarders: it has changed sides and carries a prize crew, so it no longer fights.</summary>
        public bool Captured;

        /// <summary>The side a captured hull fought on before it was taken (its <see cref="Side"/> is now its captor's); -1 until then.</summary>
        public int TakenFromSide = -1;

        /// <summary>The side it fought on: the one it was taken from, if it was captured.</summary>
        public int FoughtOn => Captured && TakenFromSide >= 0 ? TakenFromSide : Side;

        /// <summary>A fighter that has landed back aboard its carrier.</summary>
        public bool Docked;

        /// <summary>Seconds left with its systems knocked out by an interdiction pulse or an EMP: drives, guns and
        /// shield regeneration all down until it comes back.</summary>
        public double Stunned;

        /// <summary>The command ship on its side it fights coordinated under this step, or null (U.SPC-T477, see
        /// <see cref="FleetCoordination"/>).</summary>
        public BattleUnit CommandedBy;

        /// <summary>Seconds left in the confusion after the command ship coordinating it was lost (U.SPC-T477).</summary>
        public double Disrupted;

        /// <summary>Where an alpha-striking hull (a frigate, U.SPC-T475) is in its run: burning in, or burning back out after its
        /// volley (see <see cref="AlphaStrike"/>).</summary>
        public StrikePhase Strike;

        public bool Present => !Dead && !Fled && !Docked;
        public double Radius => LengthKm * 0.5;
        public string Name => Ship != null ? Ship.Name : Platform != null ? Platform.Name + " defence platform" : Fort != null ? Fort.Name + " fort" : CraftHull != null ? CraftHull.Name : "Fighter";

        /// <summary>While <see cref="PlayerControlled"/>: the EMP trigger held, and a mine asked for this frame (the
        /// battle clears it once it has tried, U.SPC-T271).</summary>
        public bool PlayerEmpHeld, PlayerMineRequested;

        /// <summary>While <see cref="PlayerControlled"/>: a capital ship at the helm (U.SPC-T272). Its crews work every
        /// mount, favouring the locked hull, and the player calls broadsides: holding the trigger has the battery
        /// bearing on <see cref="PlayerVolleyAim"/> hold its fire and build a volley, and letting go fires it together.</summary>
        public bool PlayerHelm;

        /// <summary>At the helm: where the player is looking (unit, ecliptic axes), the side a volley is called on.</summary>
        public DVec3 PlayerVolleyAim;

        /// <summary>At the helm: the trigger let go this frame, firing the volley (cleared once the battle has fired it).</summary>
        public bool PlayerVolleyRelease;

        /// <summary>At the helm: seconds the volley has been building, up to <see cref="Battle.VolleyBuildSeconds"/>.</summary>
        public double VolleyCharge;

        /// <summary>How a small craft shares its reactor between weapons, shields and engines (U.SPC-T424); null runs
        /// everything as designed.</summary>
        public PowerDistribution Power;

        /// <summary>While <see cref="PlayerControlled"/>: the weak point on <see cref="PlayerLockTarget"/> its missiles and
        /// torpedoes are aimed at (U.SPC-T424), or null to leave it to the seeker.</summary>
        public Subsystem PlayerSubTarget;

        /// <summary>While <see cref="PlayerControlled"/>: a flare-and-chaff cloud asked for this frame (the battle clears it
        /// once it has tried, U.SPC-T424).</summary>
        public bool PlayerDecoyRequested;

        /// <summary>A capital ship's orders in the broadside game (U.SPC-T425): orders to its guns, focus fire on a weakened shield patch, the
        /// brace, damage control and the formation it leads, given by the player at the helm or by its captain's doctrine.
        /// Null for anything smaller (see <see cref="Broadsides.Of"/>).</summary>
        public BroadsideCommand Broadside;

        /// <summary>The roof of a hull flown at the helm, rolled however its captain has rolled it (U.SPC-T499: capitals fight
        /// in full 3D, rolling a broadside onto a ship above or below); read only while <see cref="PlayerControlled"/>.
        /// Zero is held level, roof toward ecliptic north; a hull flown by the battle takes its captain's <see cref="Roof"/> instead.</summary>
        public DVec3 Up;

        /// <summary>The roof of a hull the battle flies, rolled however its captain has rolled it to keep its weak patches
        /// out of the heaviest fire (U.SPC-T502, see <see cref="WeakPatches"/>); read only while not
        /// <see cref="PlayerControlled"/>. Zero holds it level, roof toward ecliptic north.</summary>
        public DVec3 Roof;

        /// <summary>Right and up axes of the hull (nose along <see cref="Forward"/>, up toward ecliptic north, or the helm's
        /// own roof, <see cref="Up"/>, or its captain's, <see cref="Roof"/>): the frame its fire cones, bearings and hardpoints are reckoned in.</summary>
        public void Basis(out DVec3 right, out DVec3 up)
        {
            var roof = PlayerControlled ? Up : Roof;
            if (roof.LengthSquared > 1e-12)
            {
                var r = DVec3.Cross(Forward, roof);
                if (r.LengthSquared > 1e-12)
                {
                    right = r.Normalized;
                    up = DVec3.Cross(right, Forward);
                    return;
                }
            }
            // Level: roof is north projected off the nose. Continuous right up to the pole (U.SPC-T562); only a nose
            // exactly along the pole has no north to project, and falls back to X.
            var upRef = DVec3.Cross(Forward, DVec3.UnitZ).LengthSquared > 1e-12 ? DVec3.UnitZ : DVec3.UnitX;
            right = DVec3.Cross(Forward, upRef).Normalized;
            up = DVec3.Cross(right, Forward);
        }

        /// <summary>Battle-local position of a hardpoint.</summary>
        public DVec3 PointOf(SubsystemDef d) => PointLocal(new DVec3(d.X, d.Y, d.Z));

        /// <summary>Battle-local position of a point given in ship lengths (right, up, forward).</summary>
        public DVec3 PointLocal(DVec3 local)
        {
            Basis(out var right, out var up);
            return Pos + (right * local.X + up * local.Y + Forward * local.Z) * LengthKm;
        }

        /// <summary>A battle-local point expressed in ship lengths in this hull's frame.</summary>
        public DVec3 ToLocal(DVec3 point)
        {
            Basis(out var right, out var up);
            var d = (point - Pos) / Math.Max(LengthKm, 1e-9);
            return new DVec3(DVec3.Dot(d, right), DVec3.Dot(d, up), DVec3.Dot(d, Forward));
        }

        /// <summary>A battle-local direction expressed in this hull's frame (right, up, forward), unscaled.</summary>
        public DVec3 DirToLocal(DVec3 dir)
        {
            Basis(out var right, out var up);
            return new DVec3(DVec3.Dot(dir, right), DVec3.Dot(dir, up), DVec3.Dot(dir, Forward));
        }

        /// <summary>
        /// Where on this hull, in ship lengths in its frame, a strike at battle-local <paramref name="point"/> travelling
        /// along <paramref name="incoming"/> meets the shield (U.SPC-T501, see <see cref="ShieldEmitters.StrikePoint"/>):
        /// the patch whose emitter's field takes it.
        /// </summary>
        public DVec3 ShieldStrike(DVec3 point, DVec3? incoming = null)
        {
            var local = ToLocal(point);
            var spec = Combat?.Spec;
            if (spec == null) return local;
            return ShieldEmitters.StrikePoint(spec, local, incoming.HasValue ? DirToLocal(incoming.Value) : (DVec3?)null);
        }

        /// <summary>A direction in this hull's frame (right, up, forward) as a battle-local direction, unscaled.</summary>
        public DVec3 LocalDirToWorld(DVec3 local)
        {
            Basis(out var right, out var up);
            return right * local.X + up * local.Y + Forward * local.Z;
        }

        /// <summary>Whether the mount <paramref name="d"/> on this hull bears on a battle-local point: the point lies inside its
        /// fire cone seen from where the mount sits, above or below the hull as well as to the side (U.SPC-T533).</summary>
        public bool Bears(SubsystemDef d, DVec3 point)
        {
            if (d.BearsEverywhere) return true;
            var to = point - PointOf(d);
            if (to.LengthSquared < 1e-18) to = point - Pos;
            return d.Covers(Combat?.Spec, DirToLocal(to));
        }

        /// <summary>Whether the mount <paramref name="d"/> on this hull bears along a battle-local direction (a line of sight
        /// off the hull, with no point at its end).</summary>
        public bool BearsAlong(SubsystemDef d, DVec3 dir) => d.BearsEverywhere || d.Covers(Combat?.Spec, DirToLocal(dir));

        /// <summary>Yaw angle (degrees) of a battle-local point relative to this hull's own facing:
        /// 0 = dead ahead, 90 = starboard, 180 = astern, 270 = port.</summary>
        public double YawDegOf(DVec3 point)
        {
            var local = ToLocal(point);
            return Astro.WrapDegrees360(Math.Atan2(local.X, local.Z) * Astro.Rad2Deg);
        }
    }

    /// <summary>What a dogfighting small craft is doing (U.SPC-T274).</summary>
    public enum DogfightManeuver
    {
        /// <summary>Nose on the lead point, guns working, jinking on the way in.</summary>
        Attack,

        /// <summary>Through the merge and out the other side under boost, to come round for another pass.</summary>
        Extend,

        /// <summary>A hard turn away from a hostile on its tail.</summary>
        Break,
    }

    public enum MissileKind
    {
        Missile,
        Torpedo,
        Pod,

        /// <summary>An EMP round: it bursts beside its target and stuns rather than damages (U.SPC-T271).</summary>
        Emp,

        /// <summary>An antimatter (photon) torpedo (U.SPC-T489): a heavy guided warhead, tougher than a torpedo to stop.</summary>
        Antimatter,

        /// <summary>A screen's interceptor (U.SPC-T489): it hunts a hostile round (<see cref="Missile.Prey"/>) or a fighter.</summary>
        Interceptor,

        /// <summary>A Breacher round (U.SPC-T466): a marine team fired from a Breacher bay, flying like a bullet with only a
        /// slight course correction until it strikes a hull and drills in (<see cref="BreacherBays"/>).</summary>
        Breacher,
    }

    /// <summary>
    /// A proximity mine (U.SPC-T271): it sits where it was laid, arms after a moment, and blows when a hull hostile to
    /// its layer comes within its trigger, hurting everything hostile within its blast.
    /// </summary>
    public sealed class Mine
    {
        public DVec3 Pos, Vel;
        public int Side;
        public double Damage, BlastKm;

        /// <summary>Seconds until it is armed, and until it fizzles out unexploded.</summary>
        public double ArmIn, Life;
        public bool Gone;

        public bool Armed => ArmIn <= 0;
    }

    /// <summary>A boarding pod or Breacher round stuck in a hull: where (ship lengths in that hull's frame), whose, and pointing which way.</summary>
    public sealed class Embedded
    {
        public BattleUnit Unit;
        public DVec3 Local;
        public DVec3 Inward;
        public int Side;
        public bool Breacher;
    }

    /// <summary>A battle fought in open space beside a hyperlane, after an interdiction pulse.</summary>
    public sealed class LaneSite
    {
        public LaneHop Hop;
        public DVec3 FromOffset, ToOffset;
        public double Progress;

        /// <summary>Offset of the fight from the lane line, km (the ambushers wait just outside it).</summary>
        public DVec3 Side;
    }

    public sealed class Missile
    {
        public DVec3 Pos, Vel;
        public BattleUnit Target;
        public Subsystem TargetSub;
        public double Damage, Speed, Hp, Life;
        public int Side;
        public bool Torpedo;
        public bool Gone;
        public MissileKind Kind;

        /// <summary>A pod's squad: who they fight for and how good they are.</summary>
        public Faction Owner;
        public double Marines;

        /// <summary>The ship that fired a pod, so a squad that cannot land can go home.</summary>
        public BattleUnit Source;

        /// <summary>An EMP round's stun and reach (see <see cref="WeaponSpec.EmpSeconds"/>).</summary>
        public double EmpSeconds, EmpRadiusKm;

        /// <summary>Fired by a small craft, so it hits a capital ship's weak points as hard as one (U.SPC-T424, see <see cref="WeakPoints"/>).</summary>
        public bool SmallCraft;

        /// <summary>The flare-and-chaff cloud it has been drawn off onto, or null (U.SPC-T424).</summary>
        public Decoy Decoy;

        /// <summary>The hostile round an interceptor is hunting (U.SPC-T489), or null for one sent after a fighter.</summary>
        public Missile Prey;

        /// <summary>A Breacher round that missed or glanced off raised shields (U.SPC-T519): tumbling on through space, a
        /// derelict with its team still alive inside, drilling into nothing now. Point defence can still shoot it out of the
        /// sky; once the fight is over it is left drifting for a tow to recover or pirates to salvage (<see cref="DriftingRounds"/>).</summary>
        public bool Adrift;
    }

    /// <summary>
    /// A railgun slug or autocannon burst in flight: a dumb, non-homing round that was aimed, with a
    /// lead, at where its target was expected to be, and takes real time crossing the range instead of
    /// landing the instant the trigger was pulled. <see cref="Hit"/> is decided at the muzzle, same as a
    /// beam's accuracy roll; a round that "hit" only actually calls <see cref="Battle.ApplyDamage"/> once
    /// it arrives, so a target that dies or peels off first takes nothing. A round that missed keeps
    /// flying past its aim point and fades, rather than never having existed.
    /// </summary>
    public sealed class KineticRound
    {
        public DVec3 Pos, Vel;
        public BattleUnit Target;
        public Subsystem TargetSub;
        public double Damage;

        /// <summary>Seconds left before the round reaches its aim point (where it resolves, if it hit).</summary>
        public double ArriveIn;

        /// <summary>Seconds left before the round is removed -- longer than <see cref="ArriveIn"/> for a
        /// miss, so it keeps travelling past the target before it fades.</summary>
        public double Life;

        public int Side;
        public bool Hit;

        /// <summary>True for a railgun slug, false for fighter autocannon fire -- cosmetic only (Runtime
        /// draws the two differently).</summary>
        public bool Railgun;
        public bool Gone;

        /// <summary>An aimed round (U.SPC-T270): no roll at the muzzle and no <see cref="Target"/>; it hits
        /// whatever hostile hull it actually flies through, or nothing (see <see cref="Gunnery"/>).</summary>
        public bool Aimed;

        /// <summary>Fired by a small craft (U.SPC-T424, see <see cref="WeakPoints"/>).</summary>
        public bool SmallCraft;
    }

    public enum FxKind
    {
        Beam,
        BeamMiss,

        /// <summary>A railgun's hypervelocity kinetic round -- a harder, thinner, whiter streak than a
        /// laser <see cref="Beam"/>.</summary>
        Railgun,
        RailgunMiss,
        PointDefense,
        Guns,
        ShieldHit,
        Impact,
        SubsystemDestroyed,
        MissileKill,
        UnitDestroyed,
        MissileLaunch,

        /// <summary>A pod or Breacher round holes a hull: a burst of venting atmosphere.</summary>
        Breach,
        PodBounce,
        Captured,
        Latch,

        /// <summary>An interdiction pulse: an expanding electromagnetic shell.</summary>
        Pulse,

        /// <summary>A hull breaking off, weapons powered down, hailing to ask terms.</summary>
        Hail,

        /// <summary>A mine going off (Size: its blast, km).</summary>
        MineBlast,

        /// <summary>An EMP round bursting (Size: its reach, km).</summary>
        EmpBurst,

        /// <summary>A mine laid.</summary>
        Deployed,

        /// <summary>A tachyon lance's shot (U.SPC-T487): there and gone in the same instant along its whole length, a
        /// wide, violet-white line from the bow to what it struck.</summary>
        TachyonLance,
        TachyonLanceMiss,

        /// <summary>A phaser array's beam (U.SPC-T488): Size is its damage, so a lance draws as one hard line and a
        /// sweep as a thin flicker.</summary>
        Phaser,
        PhaserMiss,

        /// <summary>A plasma cannon's bolt (U.SPC-T488): Size is the damage it landed with, after its bleed over distance.</summary>
        Plasma,
        PlasmaMiss,

        /// <summary>A flak shell (U.SPC-T489): its tracer from the cannon (A) to its burst (B), Size the burst, km.</summary>
        FlakBurst,
    }

    /// <summary>Something to draw: a beam, a hit, an explosion. A and B are battle-local km.</summary>
    public struct CombatFx
    {
        public int Seq;
        public FxKind Kind;
        public DVec3 A, B;
        public double Size;
        public int Side;
        public BattleUnit Unit;

        /// <summary>The subsystem index concerned, or -1: the hardpoint hit or lost, or for a
        /// <see cref="FxKind.ShieldHit"/> the shield emitter whose patch took the hit.</summary>
        public int Sub;
    }

    /// <summary>
    /// A fight at one body. It runs a tactical simulation in a small local space (tens of km) anchored
    /// in orbit: each hull manoeuvres to its weapons' range, every hardpoint picks targets and fires,
    /// lasers and missiles hit particular subsystems on the far hull, point defence shoots missiles and
    /// fighters down, and damaged ships break off and run. Outcomes are real: what dies is gone, what
    /// runs carries its damage home. Seconds are simulated seconds; at high time warp the battle takes
    /// coarser steps rather than holding the clock back.
    /// </summary>
    public sealed partial class Battle
    {
        public readonly int Id;
        public readonly int Body;
        public readonly string Name;

        /// <summary>Where the fight is: an offset (km, ecliptic) from the body's centre, fixed in its frame.</summary>
        public readonly DVec3 Anchor;

        /// <summary>This battle's line-of-approach axis; fixed at creation so late joiners always use the right axis.</summary>
        public DVec3 ApproachAxis = DVec3.UnitX;

        public readonly double StartJd;
        public double Seconds;
        public readonly List<BattleUnit> Units = new List<BattleUnit>();
        public readonly List<Missile> Missiles = new List<Missile>();
        public readonly List<KineticRound> Rounds = new List<KineticRound>();
        public readonly List<Mine> Mines = new List<Mine>();

        /// <summary>Flare-and-chaff clouds burning in the battle (U.SPC-T424).</summary>
        public readonly List<Decoy> Decoys = new List<Decoy>();

        /// <summary>The recon fighters in the fight this step (U.SPC-T485, see <see cref="ElectronicWarfare.Sweep"/>): who jams and paints.</summary>
        public readonly List<BattleUnit> ReconAloft = new List<BattleUnit>();
        public readonly List<Embedded> Embedded = new List<Embedded>();

        /// <summary>Gun mounts the player has taken (U.SPC-T18, see <see cref="MountedTurret"/>): each is laid and fired by
        /// <see cref="FireManned"/> instead of its crew. Kept on the battle, so nothing stays manned past the fight.</summary>
        public readonly Dictionary<Subsystem, MountControl> Manned = new Dictionary<Subsystem, MountControl>();

        /// <summary>Launched small craft the player is flying (U.SPC-T422, see <see cref="FighterSeat"/>): each is flown by
        /// <see cref="FighterAI.FlyManned"/> on its stick instead of by its pilot. Kept on the battle, like <see cref="Manned"/>.</summary>
        public readonly Dictionary<BattleUnit, FighterControl> Flown = new Dictionary<BattleUnit, FighterControl>();

        /// <summary>Set for an ambush beside a hyperlane; null for a battle at a body.</summary>
        public LaneSite Site;

        /// <summary>The side that started the fight (every battle the war starts puts its aggressor on side 0); a
        /// lawful ship on the other side was defending itself (see <see cref="Law.OnBattleEnded"/>).</summary>
        public int AggressorSide;

        /// <summary>The side enforcing the law (a patrol interdicting a wanted ship, see <see cref="War.TryStartInterdiction"/>), or -1.</summary>
        public int EnforcingSide = -1;

        /// <summary>Raised when boarders take a unit (the world decides what they do with it).</summary>
        public Action<Battle, BattleUnit, Faction> UnitCaptured;
        public readonly List<CombatFx> Fx = new List<CombatFx>();
        public readonly Faction[] SideLeaders = new Faction[2];

        /// <summary>How much of their usual resolve each side's crews keep (1 normally): facing a feared outlaw
        /// they strike their colours sooner (see <see cref="Outlaws.FearedResolve"/>).</summary>
        public readonly double[] Dread = { 1, 1 };

        public bool Over { get; private set; }

        /// <summary>True until the battle's first advance: it does not run in the frame it starts, so an observer can slow the clock first.</summary>
        public bool Fresh = true;

        /// <summary>The side that held the field (0 or 1), or -1 if both broke off.</summary>
        public int Winner { get; private set; } = -1;

        public int FxSeq { get; private set; }

        /// <summary>Raised when a unit is destroyed (the world books the loss).</summary>
        public Action<Battle, BattleUnit> UnitDestroyed;

        /// <summary>Raised the moment a hull strikes its colours: breaks off, powers down weapons and hails.</summary>
        public Action<Battle, BattleUnit> UnitSurrendered;

        /// <summary>Raised once a captor's terms for a surrendered hull are settled -- auto-decided or
        /// chosen externally via <see cref="OfferSurrenderTerms"/> -- so the world can apply the
        /// consequences (cargo ransomed, a clean getaway; a taken prize follows the normal capture hook).</summary>
        public Action<Battle, BattleUnit, Faction, SurrenderTerms> SurrenderDecided;

        /// <summary>Raised when pirates demand a player-flown hull's cargo (the hull, the pirates).</summary>
        public Action<Battle, BattleUnit, Faction> CargoDemanded;

        /// <summary>Fighter AI hook, run each step for every fighter (see FighterAI).</summary>
        public Action<Battle, BattleUnit, double> FighterStep;

        /// <summary>Carriers launch their squadrons through this (see FighterAI).</summary>
        public Action<Battle, BattleUnit> Launch;

        public const double EscapeKm = 55;
        public const double MaxStep = 1.0;

        /// <summary>How long a hailing hull waits before the battle decides terms itself if nobody has.</summary>
        public const double SurrenderDecisionSeconds = 15;

        /// <summary>How long an AI captor takes to answer a hail when no player-flown hull is there to choose.</summary>
        public const double SurrenderAiDecisionSeconds = 4;

        readonly double[] sideStrength = new double[2];
        const int MaxStepsPerAdvance = 500;
        const double MaxSeconds = 4 * 3600;

        readonly Random rng;
        int nextUnitId;
        double accumulator;

        public Battle(int id, int body, string name, DVec3 anchor, double startJd, int seed)
        {
            Id = id;
            Body = body;
            Name = name;
            Anchor = anchor;
            StartJd = startJd;
            rng = new Random(seed);
        }

        public Random Rng => rng;

        // ---------------------------------------------------------------- membership

        public BattleUnit AddShip(Ship ship, int side, DVec3 pos, DVec3 forward)
        {
            var u = NewUnit(UnitKind.Ship, side, ship.Faction, ship.Combat, ship.LengthKm, pos, forward);
            u.Ship = ship;
            ship.Battle = this;
            if (ship.Combat.Spec.Squadrons > 0) Launch?.Invoke(this, u);
            return u;
        }

        public BattleUnit AddPlatform(Settlement s, ShipCombat platform, int side, DVec3 pos)
        {
            var u = NewUnit(UnitKind.Platform, side, s.Owner, platform, CombatSpec.PlatformLengthKm, pos, DVec3.UnitX);
            u.Platform = s;
            return u;
        }

        /// <summary>A gate fort joins the fight for its gate's owner (U.SPC-T92).</summary>
        public BattleUnit AddFort(JumpGate gate, ShipCombat fort, int side, DVec3 pos)
        {
            var u = NewUnit(UnitKind.Platform, side, gate.Owner, fort, CombatSpec.PlatformLengthKm, pos, DVec3.UnitX);
            u.Fort = gate;
            return u;
        }

        public BattleUnit NewUnit(UnitKind kind, int side, Faction faction, ShipCombat combat, double lengthKm, DVec3 pos, DVec3 forward)
        {
            combat.ResetShieldTimers();
            var u = new BattleUnit
            {
                Id = nextUnitId++, Kind = kind, Side = side, Faction = faction, Combat = combat, LengthKm = lengthKm,
                Pos = pos, Forward = forward.LengthSquared > 0 ? forward.Normalized : DVec3.UnitX,
                RetargetIn = rng.NextDouble() * 2, Wander = rng.NextDouble() * 100, Circle = rng.Next(2) == 0 ? -1 : 1,
                MoraleThreshold = 0.55 + rng.NextDouble() * 0.5,
            };
            if (side >= 0 && side < 2 && SideLeaders[side] == null) SideLeaders[side] = faction;
            Units.Add(u);
            return u;
        }

        public BattleUnit UnitOf(Ship ship)
        {
            foreach (var u in Units)
                if (u.Ship == ship) return u;
            return null;
        }

        public bool Hostile(BattleUnit a, BattleUnit b) => a.Side != b.Side && a.Present && b.Present;

        // ---------------------------------------------------------------- stepping

        /// <summary>A step (seconds) of a battle fought at coarse detail, in a system nobody is looking at (U.SPC-T104).</summary>
        public const double CoarseStep = 15;

        /// <summary>The most coarse steps one advance takes: enough for six hours, the coarse worlds' own step.</summary>
        const int MaxCoarseStepsPerAdvance = 1600;

        /// <summary>
        /// Fights <paramref name="seconds"/> more of the battle: in steps of a second (<see cref="MaxStep"/>), or
        /// at <paramref name="coarse"/> detail (a system nobody is looking at, see <see cref="World.Coarse"/>) in
        /// steps of <see cref="CoarseStep"/>, the same fight resolved fifteen times more cheaply. A battle can go
        /// from one to the other at any step and carries on as it stands.
        /// </summary>
        public void Advance(double seconds, bool coarse = false)
        {
            if (Over || seconds <= 0) return;
            accumulator += seconds;
            double unit = coarse ? CoarseStep : MaxStep;
            int cap = coarse ? MaxCoarseStepsPerAdvance : MaxStepsPerAdvance;
            int steps = (int)Math.Ceiling(accumulator / unit - 1e-9);
            double dt = unit;
            if (steps > cap)
            {
                // Far behind (a year per second): take coarser steps, up to 20 s; drop the rest.
                dt = Math.Min(Math.Max(20, unit), accumulator / cap);
                steps = cap;
            }
            for (int i = 0; i < steps && !Over; i++)
            {
                double step = Math.Min(dt, accumulator);
                if (step <= 1e-9) break;
                Step(step);
                accumulator -= step;
            }
            if (accumulator > dt * 2) accumulator = 0;
        }

        void Step(double dt)
        {
            Seconds += dt;
            TallySideStrength();
            // Who fights under which command ship this step, and who has just lost theirs (U.SPC-T477).
            FleetCoordination.Step(this, dt);
            // Which recon fighters are up to jam and paint this step (U.SPC-T485).
            ElectronicWarfare.Sweep(this);
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (!u.Present) continue;
                if (u.Kind == UnitKind.Fighter)
                {
                    // One the player has taken flies on their stick; the rest of its squadron flies on as before (U.SPC-T422).
                    if (Flown.Count > 0 && Flown.TryGetValue(u, out var stick)) FighterAI.FlyManned(this, u, stick, dt);
                    else FighterStep?.Invoke(this, u, dt);
                    continue;
                }
                Think(u, dt);
                Move(u, dt);
            }
            // Craft down on a flight deck ride with their hull, are turned round and launched again (U.SPC-T518).
            FlightDecks.Step(this, dt);
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (!u.Present) continue;
                if (u.PlayerControlled) StepPlayerWeapons(u, dt);
                Fire(u, dt);
                // A mine asked for is laid now or not at all, not saved up for when the mount is ready; a volley
                // called is fired now, and the next one builds from nothing.
                if (u.PlayerControlled)
                {
                    u.PlayerMineRequested = false;
                    u.PlayerDecoyRequested = false;
                    if (u.PlayerVolleyRelease) u.VolleyCharge = 0;
                    u.PlayerVolleyRelease = false;
                }
            }
            StepMissiles(dt);
            StepRounds(dt);
            StepMines(dt);
            StepDecoys(dt);
            StepBoarding(dt);
            StepSurrenders(dt);
            double track = 1 - Math.Exp(-dt / Gunnery.TrackingLagS);
            foreach (var u in Units)
            {
                if (!u.Present) continue;
                u.SeenVel += (u.Vel - u.SeenVel) * track;
                var c = u.Combat;
                // A capital ship's captain orders its batteries, braces and works damage control (U.SPC-T425).
                if (Broadsides.Commands(u)) Broadsides.Step(this, u, dt);
                // A big ship's heat fills or drains, and past its sinks' limit its crew and systems cook (U.SPC-T491).
                StepHeat(u, dt);
                if (u.Stunned > 0) u.Stunned -= dt;
                // A small craft with power to spare for its shields recharges them that much faster (U.SPC-T424).
                // A charging tachyon lance draws on the reactor, and the shields recover that much slower (U.SPC-T487).
                else c.RegenShields(Seconds, dt * PowerDistribution.FactorOf(u, PowerSystem.Shields) * TachyonLances.ShieldRegenFactor(c));
                // No escape with boarders fighting aboard (or latched to a hull it is boarding), nor for a hull that has struck
                // its colours or been taken: drifting off with its drives cold is not running.
                if (u.Kind == UnitKind.Ship && u.Latched == null && !c.Disabled && !c.Interior.UnderBoarding && !u.Surrendering && !u.Captured && (u.Pos.Length > EscapeKm || (u.Fleeing && NearestHostileDistance(u) > 30)))
                    u.Fled = true;
            }
            if (Fx.Count > 600) Fx.RemoveRange(0, Fx.Count - 600);
            CheckOver();
        }

        /// <summary>
        /// Over when one side has nobody left on the field (dead or got away), when neither side has a
        /// gun it is willing to use, or after four hours. Those running are still fair game until they
        /// get clear, so a rout is a pursuit, not an armistice.
        /// </summary>
        void CheckOver()
        {
            bool any0 = false, any1 = false, fight0 = false, fight1 = false;
            foreach (var u in Units)
            {
                if (!u.Present || u.Kind == UnitKind.Fighter || u.Captured) continue;
                // Marines alone make a combatant only of a hull that will fly a boarding approach: the AI's,
                // never one the player is flying (see Think), which with no guns has nothing left to fight with.
                bool willing = !u.Fleeing && !u.Surrendering && !u.Combat.Disabled && (u.Combat.Armed || (CanBoard(u) && !u.PlayerControlled));
                if (u.Side == 0) { any0 = true; fight0 |= willing; }
                else { any1 = true; fight1 |= willing; }
            }
            bool fighting = any0 && any1 && (fight0 || fight1);
            Mopping = !fighting;
            if (Seconds < MaxSeconds && (fighting || Boarding || FightersAloft || AwaitingSurrenderTerms)) return;
            Over = true;
            Winner = fight0 && !fight1 ? 0 : fight1 && !fight0 ? 1 : any0 && !any1 ? 0 : any1 && !any0 ? 1 : -1;
        }

        /// <summary>A ship our side is boarding, or about to board: worth more taken than destroyed.</summary>
        bool IsPrize(BattleUnit u, BattleUnit target)
        {
            // Its own boarding, or an ally's on its side it may join (U.SPC-T392).
            var inside = target.Combat.Interior;
            if (inside.Boarder != null && inside.Admits(u.Faction, u.Side)) return true;
            foreach (var o in Units)
                if (o.Side == u.Side && o.Present && (o.BoardTarget == target || o.Latched == target)) return true;
            return false;
        }

        /// <summary>A raider or warship with marines to spare is a combatant even with no guns.</summary>
        bool CanBoard(BattleUnit u) =>
            u.Kind == UnitKind.Ship && (u.Faction.IsPirate || u.Faction.IsMilitary || (Boards != null && Boards(u))) && u.Combat.MarinesAvailable >= 15;

        /// <summary>Whether a unit holds its fire on a disabled foe it wants taken alive (a bounty hunter after its
        /// quarry's captain, U.SPC-T63) while its side can still board it, or null.</summary>
        public Func<BattleUnit, BattleUnit, bool> WantsAlive;

        /// <summary>Whether a unit sends boarders across though its faction is neither a navy nor raiders (bounty hunters), or null.</summary>
        public Func<BattleUnit, bool> Boards;

        /// <summary>Whether <paramref name="u"/> would board <paramref name="target"/>: a navy or raiders board anything,
        /// a hunter only the quarry it wants alive (never a lawful hull: that would be piracy).</summary>
        bool MayBoard(BattleUnit u, BattleUnit target) =>
            u.Faction.IsPirate || u.Faction.IsMilitary || (WantsAlive != null && WantsAlive(u, target));

        /// <summary>Whether anyone on <paramref name="side"/> can still send a boarding party.</summary>
        bool SideCanBoard(int side)
        {
            foreach (var o in Units)
                if (o.Present && o.Side == side && !o.Captured && !o.Combat.Disabled && CanBoard(o)) return true;
            return false;
        }

        /// <summary>The shooting is over; boarding parties and landing fighters are finishing up.</summary>
        public bool Mopping { get; private set; }

        /// <summary>A boarding fight or a boarding approach is under way somewhere in the battle.</summary>
        bool Boarding
        {
            get
            {
                foreach (var u in Units)
                {
                    if (!u.Present) continue;
                    if (u.Combat.Interior.UnderBoarding || u.BoardTarget != null || u.Latched != null) return true;
                }
                return false;
            }
        }

        /// <summary>Fighters still flying that have a carrier to land on. One the player is flying never lands on its own, so
        /// it does not hold the fight open: it is taken back aboard with the rest when the fight ends (see War).</summary>
        bool FightersAloft
        {
            get
            {
                foreach (var u in Units)
                    if (u.Kind == UnitKind.Fighter && u.Present && !Flown.ContainsKey(u) && StrandedWings.CanLandOn(u.Fighter.LandsOn)) return true;
                return false;
            }
        }

        double NearestHostileDistance(BattleUnit u)
        {
            double best = double.MaxValue;
            foreach (var o in Units)
                if (Hostile(u, o) && o.Kind != UnitKind.Fighter && !o.Captured && (o.Combat.Armed || CanBoard(o)))
                    best = Math.Min(best, (o.Pos - u.Pos).Length);
            return best;
        }

        // ---------------------------------------------------------------- decisions

        void Think(BattleUnit u, double dt)
        {
            var c = u.Combat;
            if (u.Captured || u.Latched != null || u.Surrendering) return;
            if (u.Kind == UnitKind.Ship && !u.PlayerControlled && !c.Disabled && StrikesColours(u))
            {
                Surrender(u);
                return;
            }
            if (u.Kind == UnitKind.Ship && u.Faction.IsPirate && !u.PlayerControlled)
            {
                MaybeDemandCargo(u);
                // The cargo in hand and nothing left worth taking: raiders withdraw rather than trade fire with escorts for
                // nothing (U.SPC-T374).
                if (!u.Fleeing && !c.Disabled && RaidDone(u)) u.Fleeing = true;
            }
            if (u.Kind == UnitKind.Ship)
            {
                bool merchant = u.Ship != null && !u.Ship.Spec.IsMilitary && !u.Faction.IsPirate;
                // Whether to run is the player's call when the player is flying it.
                if (c.Disabled || u.PlayerControlled) u.Fleeing = false;
                else if (!u.Fleeing && (merchant || c.HullFraction < 0.25 || (!c.Armed && !CanBoard(u)) || Routed(u.Side)))
                    u.Fleeing = true;
                // Only the AI flies a boarding approach (Move leaves a player-flown hull alone), so a
                // player-flown hull never holds a boarding target it would never close on.
                // Nor does a ship its flagship has ordered into line break off to board (U.SPC-T425).
                if (u.PlayerControlled || Broadsides.HoldsFormation(u)) u.BoardTarget = null;
                else ChooseBoarding(u);
                // A carrier sends its boarding shuttles to the prizes instead (U.SPC-T486).
                SendShuttles(u, dt);
            }
            u.RetargetIn -= dt;
            if (u.Target == null || !u.Target.Present || u.RetargetIn <= 0)
            {
                u.Target = PickTarget(u);
                // Under a command ship its staff has the next mark picked out already; with it lost, nobody has (U.SPC-T477).
                u.RetargetIn = ((c.BridgeIntact ? 3 : 7) + rng.NextDouble() * 3) * FleetCoordination.RetargetFactor(u);
            }
        }

        bool Routed(int side)
        {
            if (Seconds < 120) return false;
            double mine = 0, theirs = 0;
            foreach (var u in Units)
            {
                if (!u.Present) continue;
                double s = u.Combat.Strength;
                if (u.Side == side) mine += s;
                else theirs += s;
            }
            return theirs > 0 && mine < theirs * 0.22;
        }

        /// <summary>Score multiplier for the hull already being fought; beats the +/-15% jitter so a tie doesn't flip-flop.</summary>
        public const double TargetStickiness = 0.7;

        public BattleUnit PickTarget(BattleUnit u)
        {
            BattleUnit best = null;
            double bestScore = double.MaxValue;
            BattleUnit flagship = SideFlagship(u.Side);
            // The hull its command ship is fighting, to concentrate on (U.SPC-T477).
            var focus = FleetCoordination.FocusTarget(u);
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Captured || o.Surrendering) continue;
                // A hunter wants its quarry's captain alive: a disabled flagship is boarded, not finished off.
                if (o.Combat.Disabled && WantsAlive != null && WantsAlive(u, o) && SideCanBoard(u.Side)) continue;
                // Raiders rob a merchant or civilian hull, they don't destroy it (U.SPC-T374): once it lies dead in space
                // they hold their fire, and demand its cargo instead (see MaybeDemandCargo).
                if (o.Combat.Disabled && Spares(u, o)) continue;
                double score = (o.Pos - u.Pos).Length;
                // A disabled hull is a prize, not a threat: shoot it only if nothing else is left.
                if (o.Combat.Disabled) score += 40;
                if (o.Kind == UnitKind.Fighter) score += u.Kind == UnitKind.Fighter ? 0 : 25;
                else if (u.Kind == UnitKind.Fighter) score -= 4;
                if (o.Ship != null && !o.Ship.Spec.IsMilitary) score += u.Faction.IsPirate ? -15 : 10;
                // A lane tender is the prize of the fight (U.SPC-T481): sink it and its lane goes dark.
                if (LaneUpkeep.IsTender(o.Ship)) score -= LaneUpkeep.TenderFocus.Value;
                // Raiders kill a gate picket first, before it can warn its navy (U.SPC-T480).
                if (o.Ship != null && !o.Combat.Disabled) score -= RaidDoctrine.TargetBias(u.Faction, o.Ship);
                // A command ship is the priority target (U.SPC-T477): sink it and its whole fleet fights uncoordinated.
                if (FleetCoordination.Coordinates(o)) score -= FleetCoordination.Priority.Value;
                // A frigate's torpedoes are for the capitals (U.SPC-T475), and so are a dreadnought's spinal guns (U.SPC-T490).
                score -= AlphaStrike.TargetBias(u, o);
                score -= RearLine.TargetBias(u, o);
                // A gate breaker is there to break the gate's forts (U.SPC-T478).
                score -= GateBreakers.TargetBias(u, o);
                if (o.Kind == UnitKind.Platform) score += 6;
                if (flagship != null && flagship != u && flagship.Target == o) score -= 6;
                if (focus != null && focus == o) score -= FleetCoordination.Focus.Value;
                score *= 0.85 + rng.NextDouble() * 0.3;
                // Stickiness (U.SPC-T560): the current mark keeps the gun unless a rival is clearly better, so heading settles.
                if (o == u.Target) score *= TargetStickiness;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>The side's flagship: its working command ship if it has one, where the fleet's senior officers are
        /// (U.SPC-T477), else its biggest hull.</summary>
        BattleUnit SideFlagship(int side)
        {
            BattleUnit best = null;
            bool command = false;
            foreach (var u in Units)
            {
                if (!u.Present || u.Side != side || u.Kind == UnitKind.Fighter) continue;
                bool c = FleetCoordination.Coordinates(u);
                if (best == null || (c && !command) || (c == command && u.Combat.Spec.Hull > best.Combat.Spec.Hull))
                {
                    best = u;
                    command = c;
                }
            }
            return best;
        }

        void Move(BattleUnit u, double dt)
        {
            // The Runtime pilot loop is the single source of truth for a player-flown hull's pose;
            // fighting it here would mean the battle sees a ghost position the player never flew to.
            if (u.PlayerControlled) return;
            var c = u.Combat;
            if (u.Latched != null)
            {
                // Fixed to the hull it is boarding.
                u.Pos = u.Latched.PointLocal(u.LatchLocal);
                u.Vel = u.Latched.Vel;
                return;
            }
            if (c.Disabled || u.Captured || u.Surrendering || u.Stunned > 0)
            {
                // Adrift: no drive, slowly losing what way it had.
                u.Vel *= Math.Max(0, 1 - 0.002 * dt);
                u.Pos += u.Vel * dt;
                return;
            }
            if (u.BoardTarget != null && !u.Fleeing)
            {
                Approach(u, dt);
                return;
            }
            if (!u.Fleeing && Dogfights(u))
            {
                Dogfight(u, dt);
                return;
            }
            // ...and its drive (U.SPC-T491).
            double maxSpeed = c.Spec.MaxSpeed * (0.25 + 0.75 * c.EngineFactor) * Radiators.Throttle(u);
            if (maxSpeed <= 0)
            {
                u.Vel = DVec3.Zero;
                if (u.Target != null) TurnToward(u, u.Target.Pos - u.Pos, 3, dt);
                return;
            }

            DVec3 desired;
            DVec3? strikeFace = null;
            double burn = 1;
            if (u.Fleeing)
            {
                var away = u.Pos - HostileCentroid(u);
                if (away.LengthSquared < 1e-6) away = u.Pos.LengthSquared > 0 ? u.Pos : DVec3.UnitX;
                desired = away.Normalized * maxSpeed;
            }
            else if (u.Target != null && AlphaStrike.Flies(u) && !Broadsides.HoldsFormation(u))
            {
                // A frigate's alpha strike (U.SPC-T475): burn in nose-on, loose the volley, flip and burn back out.
                burn = AlphaStrike.Steer(u, maxSpeed, out desired, out var strikeHeading);
                maxSpeed *= burn;
                strikeFace = strikeHeading;
            }
            else if (u.Target != null && RearLine.Flies(u) && !Broadsides.HoldsFormation(u))
            {
                // A dreadnought holds the rear and snipes from its spinal guns' extreme reach, its bow on the mark (U.SPC-T490).
                RearLine.Steer(this, u, maxSpeed, out desired, out var laid);
                strikeFace = laid;
            }
            else if (u.Target != null)
            {
                var t = u.Target;
                var d = u.Pos - t.Pos;
                double dist = Math.Max(d.Length, 1e-6);
                double range = PreferredRange(u);
                var dir = d / dist;
                var spot = t.Pos + dir * range;
                var around = DVec3.Cross(DVec3.UnitZ, dir);
                if (around.LengthSquared < 1e-6) around = DVec3.UnitY;
                desired = (spot - u.Pos) * 0.02 + around.Normalized * (maxSpeed * 0.35 * u.Circle);
                if (u.Leader != null && u.Leader.Present && !u.Leader.Fleeing)
                {
                    // Escorts hold loose station on their flagship while it fights.
                    var station = StationOf(u);
                    desired = desired * 0.45 + (station - u.Pos) * 0.03 * 0.55 + u.Leader.Vel * 0.55;
                }
                // A hull carrying the screen keeps between the capital it guards and the enemy, so what comes at the capital
                // passes through its umbrella first (U.SPC-T489).
                double screen = ScreenWeapons.ScreenStation.Value;
                if (screen > 0 && ScreenWeapons.Screens(c) && ScreenWeapons.WardOf(this, u) is BattleUnit ward)
                {
                    var post = ScreenWeapons.StationOn(ward, ThreatTo(ward, u));
                    desired = desired * (1 - screen) + ((post - u.Pos) * 0.05 + ward.Vel) * screen;
                }
            }
            else
            {
                desired = -u.Pos * 0.01;
            }
            // In a formation its flagship ordered (U.SPC-T425) it keeps its station tightly, fighting from there.
            bool held = !u.Fleeing && Broadsides.HoldsFormation(u);
            if (held) desired = (StationOf(u) - u.Pos) * 0.05 + u.Leader.Vel;
            if (desired.Length > maxSpeed) desired = desired.Normalized * maxSpeed;

            double accel = c.Spec.Accel * (0.3 + 0.7 * c.EngineFactor) * burn;
            var dv = desired - u.Vel;
            double step = accel * dt;
            u.Vel += dv.Length > step ? dv.Normalized * step : dv;
            u.Pos += u.Vel * dt;

            bool broadside = !u.Fleeing && strikeFace == null && u.Target != null;
            var face = u.Fleeing ? u.Vel
                : strikeFace is DVec3 strike ? strike
                : u.Target == null ? (held ? u.Leader.Forward : u.Vel)
                : BroadsideFacing(u, (u.Target.Pos - u.Pos).Normalized);
            if (WeakPatches.Steers(u))
            {
                // A capital's captain rolls and turns its hull to keep its weak patches out of the heaviest fire (U.SPC-T502).
                var bearings = broadside ? ConeFirepower(u, (u.Target.Pos - u.Pos).Normalized, ArmourAngling.AiSlants(u) && FireCones.AiBringsGunsToBear.Value) : null;
                WeakPatches.Orient(this, u, face, bearings, bearings != null ? (u.Target.Pos - u.Pos).Normalized : (DVec3?)null, null, dt,
                    out face, out var roof, u.Forward);
                TurnToward(u, face, c.Spec.TurnRateDegS, dt);
                WeakPatches.RollToward(u, roof, c.Spec.TurnRateDegS, dt);
                return;
            }
            TurnToward(u, face, c.Spec.TurnRateDegS, dt);
            if (u.Roof.LengthSquared > 1e-12) WeakPatches.RollToward(u, WeakPatches.LevelRoof(u.Forward), c.Spec.TurnRateDegS, dt);
        }

        // ---------------------------------------------------------------- dogfighting (U.SPC-T274)

        /// <summary>The share of a hull's own turn rate at the helm (<see cref="PilotTuning.TurnRateDegS"/>) its crew
        /// flies it at in a dogfight: a human's hands are quicker than a crew's reactions.</summary>
        public const double DogfightTurnShare = 0.6;

        /// <summary>How close the merge is, km (inside it the attacker extends), how far it extends before coming
        /// round, and how near a hostile behind must be to count as on its tail.</summary>
        public const double MergeKm = 0.3, ExtendKm = 1.6, TailKm = 2.5;

        /// <summary>How far off a dogfighter goes for the player in preference to whatever else it was after, km.</summary>
        public const double HuntPlayerKm = 8;

        /// <summary>The small craft that dogfight: the light fighters and interceptors. A heavy fighter (the gunship) brawls
        /// on the ordinary circling approach, its crewed turrets firing all round, and a bomber flies at the hulls.</summary>
        static bool Dogfights(BattleUnit u) =>
            u.Kind == UnitKind.Ship && u.Ship != null
            && (u.Ship.Spec.Class == ShipClass.Fighter || u.Ship.Spec.Class == ShipClass.Interceptor || u.Ship.Spec.Class == ShipClass.Courier);

        /// <summary>
        /// A light fighter flown by its crew in a dogfight: on the same airframe the player flies (its helm turn rate,
        /// cut to <see cref="DogfightTurnShare"/>, and its own thrust) and through the same flight model, its class's
        /// (<see cref="FlightModelRegistry.For"/>, U.SPC-T440), so it carves, corners and boosts as the player's would. It lays
        /// its nose on the lead point for its guns and jinks on the way in; through the merge it extends under boost and comes
        /// round again; with a hostile on its tail and pointed at it, it breaks hard to one side under boost. It goes for the
        /// player first.
        /// </summary>
        void Dogfight(BattleUnit u, double dt)
        {
            var c = u.Combat;
            var cls = u.Ship.Spec.Class;
            var tune = PilotTuning.Of(cls);
            var hull = HullDynamics.Of(c.Spec);
            double engines = 0.4 + 0.6 * c.EngineFactor;
            double top = tune.MaxSpeed * engines;
            double speed = top;
            bool boost = false;

            // The player first, if near enough to be worth the chase.
            foreach (var o in Units)
                if (o.PlayerControlled && Hostile(u, o) && (o.Pos - u.Pos).Length < HuntPlayerKm)
                {
                    u.Target = o;
                    break;
                }
            var t = u.Target != null && u.Target.Present && Hostile(u, u.Target) ? u.Target : null;

            var threat = OnItsTail(u);
            u.ManeuverLeft -= dt;
            if (threat != null && u.Maneuver != DogfightManeuver.Break)
            {
                u.Maneuver = DogfightManeuver.Break;
                u.ManeuverLeft = 1.6 + rng.NextDouble();
                u.BreakSide = rng.Next(2) == 0 ? -1 : 1;
            }
            else if (u.ManeuverLeft <= 0 && u.Maneuver != DogfightManeuver.Attack)
                u.Maneuver = DogfightManeuver.Attack;

            u.Basis(out var right, out var up);
            DVec3 goal;
            switch (u.Maneuver)
            {
                case DogfightManeuver.Break:
                    // Hard over to one side and a little up, under boost.
                    goal = right * u.BreakSide + up * 0.35 + u.Forward * 0.2;
                    boost = true;
                    break;
                case DogfightManeuver.Extend:
                    goal = u.Forward + (t != null ? (u.Pos - t.Pos).Normalized * 0.4 : DVec3.Zero);
                    boost = true;
                    if (t != null && (t.Pos - u.Pos).Length > ExtendKm) u.ManeuverLeft = 0;
                    break;
                default:
                    if (t == null)
                    {
                        goal = -u.Pos;
                        speed *= 0.5;
                        break;
                    }
                    var rel = t.Pos - u.Pos;
                    double d = rel.Length;
                    double rounds = RoundSpeedOf(c);
                    var aimAt = rounds > 0 ? Gunnery.LeadPoint(u.Pos, u.Vel, t.Pos, t.Vel, rounds) : t.Pos;
                    goal = aimAt - u.Pos;
                    // A weave on the way in, so the target's gunners have a hard time of it too.
                    if (d > 0.8)
                    {
                        double w = Math.Sin(Seconds * 2.3 + u.Wander) * 0.12;
                        goal = goal.Normalized + (right * w + up * w * 0.5);
                    }
                    // On its tail, hold station rather than overrun it.
                    if (d < 1.2) speed = Math.Max(speed * 0.6, Math.Min(speed, t.Vel.Length + 0.05));
                    if (d < MergeKm)
                    {
                        u.Maneuver = DogfightManeuver.Extend;
                        u.ManeuverLeft = 2 + rng.NextDouble();
                    }
                    break;
            }
            // Never out of the fight altogether: past most of the escape radius, it turns back in.
            if (u.Pos.Length > EscapeKm * 0.8) goal = goal.Normalized + (-u.Pos).Normalized * 1.5;
            FlyCrewed(u, cls, tune, hull, engines, goal, top > 0 ? speed / top : 0, boost, dt);
        }

        /// <summary>
        /// One frame of a crew at the stick of <paramref name="u"/> (U.SPC-T440): its class's flight model (the one the player
        /// flies it on) with flight assist on, the nose steered toward <paramref name="goal"/> at the crew's share of the helm
        /// turn rate, the throttle at <paramref name="throttle"/> of top speed and the boost held on <paramref name="boost"/>
        /// (drawing on the hull's own charge), all scaled by its engines.
        /// </summary>
        static void FlyCrewed(BattleUnit u, ShipClass cls, in PilotTuning tune, in HullDynamics hull, double engines, DVec3 goal, double throttle, bool boost, double dt)
        {
            var crew = new PilotTuning(tune.MaxSpeed * engines, tune.TurnRateDegS * DogfightTurnShare * engines, tune.BoostSpeedMultiplier,
                tune.BoostDurationS, 1, 0);
            var drives = new HullDynamics(hull.MassKt, hull.MainAccel * engines, hull.RetroAccel * engines, hull.LateralAccel * engines,
                hull.VerticalAccel * engines, hull.PitchYawAngularAccel, hull.RollAngularAccel);
            var f = u.Forward;
            var up = u.Up - f * DVec3.Dot(u.Up, f);
            var state = new PilotState
            {
                Pos = u.Pos, Vel = u.Vel, Forward = f,
                Up = up.LengthSquared > 1e-12 ? up.Normalized : PilotState.LevelUp(f),
                AngularVelocity = u.TurnSpin,
            };
            var input = new PilotInput
            {
                DesiredForward = goal.LengthSquared > 1e-12 ? goal.Normalized : DVec3.Zero,
                Throttle = Math.Max(0, Math.Min(1, throttle)),
                Boost = boost, InCombat = true,
            };
            FlightModelRegistry.Default.For(cls).Step(ref state, u.Combat, drives, crew, input, DVec3.Zero, dt);
            u.Pos = state.Pos;
            u.Vel = state.Vel;
            u.Forward = state.Forward;
            u.Up = state.Up;
            u.TurnSpin = state.AngularVelocity;
        }

        /// <summary>A hostile close behind <paramref name="u"/> with its nose on it, or null.</summary>
        BattleUnit OnItsTail(BattleUnit u)
        {
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Combat.Disabled || o.Stunned > 0 || !o.Combat.Armed || o.Surrendering || o.Captured) continue;
                var to = o.Pos - u.Pos;
                double d = to.Length;
                if (d < 1e-6 || d > TailKm) continue;
                var dir = to / d;
                if (DVec3.Dot(u.Forward, dir) < -0.3 && DVec3.Dot(o.Forward, -dir) > 0.94) return o;
            }
            return null;
        }

        /// <summary>The fastest round a hull's forward guns fire, km/s; 0 if they are beams.</summary>
        static double RoundSpeedOf(ShipCombat c)
        {
            double speed = 0;
            foreach (var s in c.Subsystems)
            {
                var w = s.Def.Weapon;
                if (s.Destroyed || w == null || w.Missile || w.AntiSmall || w.Supporting) continue;
                speed = Math.Max(speed, w.MuzzleSpeedKmS);
            }
            return speed;
        }

        /// <summary>
        /// Which way to point the bow so the guns whose fire cones can take the target carry the most firepower onto it
        /// (U.SPC-T533, see <see cref="ConeFirepower"/>). So a broadside warship turns a flank in, not its nose, while a hull
        /// whose guns bear best ahead, or bear as well any way (or any hull with <see cref="FireCones.AiBringsGunsToBear"/>
        /// off), simply points its nose at the target. An AI captain also weighs laying its hull slanted to the target's fire
        /// so the hits glance off its armour (<see cref="ArmourAngling.AiWeight"/>, U.SPC-T534): a broadside hull keeps its
        /// flank in but angled off the line of fire, as long as its guns still bear.
        /// </summary>
        DVec3 BroadsideFacing(BattleUnit u, DVec3 toTarget)
        {
            // Only fire coming back from the target is worth slanting the hull to.
            var foe = u.Target;
            bool slant = ArmourAngling.AiSlants(u) && FireCones.AiBringsGunsToBear.Value
                && foe != null && foe.Combat.Armed && !foe.Combat.Disabled && foe.Stunned <= 0;
            var byBearing = ConeFirepower(u, toTarget, slant);
            if (byBearing == null) return toTarget;
            int bestBearing = 0;
            double best = -1;
            if (!slant)
            {
                foreach (var kv in byBearing)
                    if (kv.Value > best + 1e-9) { best = kv.Value; bestBearing = kv.Key; }
                return BowFor(toTarget, bestBearing);
            }
            // Slanting the hull to its target's fire turns part of every hit aside on its armour (U.SPC-T534): the captain
            // gives up a share of its guns' bearing for it, by doctrine, and keeps to the heading nearest its own.
            double most = 0;
            foreach (var kv in byBearing) most = Math.Max(most, kv.Value);
            if (most <= 0) return toTarget;
            double weight = ArmourAngling.AiWeight.Value;
            var t = toTarget.Normalized;
            best = double.NegativeInfinity;
            foreach (var kv in byBearing)
            {
                var f = BowFor(t, kv.Key).Normalized;
                var up = WeakPatches.LevelRoof(f);
                var right = DVec3.Cross(f, up).Normalized;
                var local = new DVec3(DVec3.Dot(t, right), DVec3.Dot(t, up), DVec3.Dot(t, f));
                double score = kv.Value / most - weight * ArmourAngling.Harm(u.Combat.Spec, local) + 1e-3 * DVec3.Dot(f, u.Forward);
                if (score > best + 1e-9) { best = score; bestBearing = kv.Key; }
            }
            return BowFor(toTarget, bestBearing);
        }

        /// <summary>
        /// The headings a captain could turn its hull to so its guns bear on a target along <paramref name="toTarget"/>
        /// (U.SPC-T533): by the bearing off the bow the target would then lie on, whole degrees (0 = the bow on it, 90 =
        /// starboard; see <see cref="BowFor"/>), the firepower (damage a second) of every ready gun (not on cooldown, with
        /// ammo, its mount not destroyed) whose fire cone would hold the target from that heading, in 3D. The candidates are
        /// the bow and each ready gun's cone axis swung round onto the target; a cone centred straight up or down off the hull
        /// is brought to bear by no turn of the bow. With <paramref name="slant"/> the strongest candidates are also swung
        /// <see cref="ArmourAngling.SlantDeg"/> either way, headings that lay the hull slanted to the target's fire
        /// (U.SPC-T534). Null for a hull with no ready guns; just the bow with <see cref="FireCones.AiBringsGunsToBear"/> off.
        /// </summary>
        public static Dictionary<int, double> ConeFirepower(BattleUnit u, DVec3 toTarget, bool slant = false)
        {
            if (toTarget.LengthSquared < 1e-18) return null;
            var spec = u.Combat.Spec;
            List<(SubsystemDef def, double fire)> guns = null;
            foreach (var s in u.Combat.Subsystems)
            {
                if (!s.Def.IsWeapon || s.Destroyed || s.Def.Weapon.AntiSmall || s.Kind == SubsystemKind.BoardingPods || s.Def.Weapon.Supporting) continue;
                var w = s.Def.Weapon;
                if (w.Missile && s.Ammo <= 0) continue;
                guns ??= new List<(SubsystemDef, double)>();
                guns.Add((s.Def, w.Damage * w.Salvo / w.Cooldown));
            }
            if (guns == null) return null;
            var t = toTarget.Normalized;
            double Score(int bearing)
            {
                var f = BowFor(t, bearing).Normalized;
                var up = WeakPatches.LevelRoof(f);
                var right = DVec3.Cross(f, up).Normalized;
                var local = new DVec3(DVec3.Dot(t, right), DVec3.Dot(t, up), DVec3.Dot(t, f));
                double sum = 0;
                foreach (var (def, fire) in guns)
                    if (def.Covers(spec, local)) sum += fire;
                return sum;
            }
            var byBearing = new Dictionary<int, double> { [0] = Score(0) };
            if (!FireCones.AiBringsGunsToBear.Value) return byBearing;
            foreach (var (def, _) in guns)
            {
                var axis = def.AxisOn(spec);
                if (axis.X * axis.X + axis.Z * axis.Z < 0.01) continue;
                int bearing = (int)Math.Round(Astro.WrapDegrees360(Math.Atan2(axis.X, axis.Z) * Astro.Rad2Deg)) % 360;
                if (!byBearing.ContainsKey(bearing)) byBearing[bearing] = Score(bearing);
            }
            // With slant, the strongest headings slanted either way too, so a captain can weigh laying its strongest battery
            // at an angle to the fire while its guns still bear (U.SPC-T534, see ArmourAngling).
            if (slant)
            {
                int off = (int)Math.Round(ArmourAngling.SlantDeg);
                double most = 0;
                foreach (var kv in byBearing) most = Math.Max(most, kv.Value);
                var strongest = new List<int>();
                foreach (var kv in byBearing)
                    if (most > 0 && kv.Value >= most - 1e-9) strongest.Add(kv.Key);
                foreach (int bearing in strongest)
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        int b = ((bearing + sign * off) % 360 + 360) % 360;
                        if (!byBearing.ContainsKey(b)) byBearing[b] = Score(b);
                    }
            }
            return byBearing;
        }

        /// <summary>The way to point the bow, held level, so a target along <paramref name="toTarget"/> lies
        /// <paramref name="bearingDeg"/> off it (0 = dead ahead, 90 = starboard): the heading that turns the guns whose cones
        /// are centred there onto the target.</summary>
        public static DVec3 BowFor(DVec3 toTarget, int bearingDeg) => RotateYaw(toTarget, bearingDeg);

        /// <summary>Rotates a direction by an angle (degrees) around the battle's vertical (ecliptic) axis.</summary>
        static DVec3 RotateYaw(DVec3 v, double angleDeg)
        {
            double rad = angleDeg * Astro.Deg2Rad;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            // Turn about the vertical axis made square to v (in v's vertical plane), so the result lies exactly
            // angleDeg off v even when v is above or below the horizon; level v gives the plain vertical axis.
            var axis = DVec3.UnitZ;
            var vn = v.LengthSquared > 1e-18 ? v.Normalized : v;
            var square = axis - vn * DVec3.Dot(axis, vn);
            if (square.LengthSquared > 1e-12) axis = square.Normalized;
            return v * cos + DVec3.Cross(axis, v) * sin + axis * DVec3.Dot(axis, v) * (1 - cos);
        }

        /// <summary>A unit's station on its leader: slot in the leader's frame plus a slow human wander.</summary>
        public DVec3 StationOf(BattleUnit u)
        {
            var l = u.Leader;
            // Level frame (U.SPC-T565): a weak-patch roll of the leader must not swing line-abreast slots round it.
            var up = WeakPatches.LevelRoof(l.Forward);
            var right = DVec3.Cross(l.Forward, up).Normalized;
            double t = Seconds * 0.05 + u.Wander;
            var wander = new DVec3(Math.Sin(t * 1.3), Math.Sin(t * 0.9 + 1.7), Math.Sin(t * 0.7 + 4.1)) * 0.35;
            var s = u.Slot + wander;
            return l.Pos + right * s.X + up * s.Y + l.Forward * s.Z;
        }

        /// <summary>Swings an AI hull's nose toward <paramref name="dir"/> the way the helm does (U.SPC-T557,
        /// <see cref="PilotFlight.Rotate(ref PilotState, in HullDynamics, double, double, in PilotInput, double)"/>): the nose spins up
        /// and brakes at the hull's angular acceleration, to a top swing of <paramref name="degPerSec"/>, so a wanted facing that flips
        /// cannot reverse the swing in one frame. A hull with no attitude thrusters of its own spins up in about a second and a half.</summary>
        public static void TurnToward(BattleUnit u, DVec3 dir, double degPerSec, double dt)
        {
            if (dir.LengthSquared < 1e-12) return;
            var want = dir.Normalized;
            var f = u.Forward;
            double rate = degPerSec * Astro.Deg2Rad;
            if (rate <= 0 || dt <= 0) return;
            double alpha = u.Combat != null ? HullDynamics.Of(u.Combat.Spec).PitchYawAngularAccel : 0;
            alpha = Math.Max(alpha, rate / 1.5);
            double maxDw = alpha * dt;

            var axis = DVec3.Cross(f, want);
            double sin = axis.Length, cos = DVec3.Dot(f, want);
            double angle = Math.Atan2(sin, cos);
            if (sin < 1e-9) axis = cos > 0 ? DVec3.Zero : DVec3.Cross(f, Math.Abs(f.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX);
            var target = axis.LengthSquared > 0 ? axis.Normalized * Math.Min(rate, Math.Sqrt(2 * alpha * angle)) : DVec3.Zero;

            var w = u.TurnSpin;
            w -= f * DVec3.Dot(w, f);
            var dw = target - w;
            if (dw.Length > maxDw) dw = dw.Normalized * maxDw;
            w += dw;

            double spin = w.Length;
            // Arriving this frame with little swing left to kill: settle on the heading rather than hunt about it.
            if (angle <= spin * dt + 1e-12 && spin <= 3 * maxDw + 1e-12)
            {
                u.Forward = want;
                u.TurnSpin = DVec3.Zero;
                return;
            }
            if (spin > 0)
            {
                var nf = PilotFlight.RotateAbout(f, w / spin, spin * dt).Normalized;
                u.Forward = nf;
                w -= nf * DVec3.Dot(w, nf);
            }
            u.TurnSpin = w;
        }

        static double PreferredRange(BattleUnit u)
        {
            double best = 0;
            foreach (var s in u.Combat.Subsystems)
            {
                var w = s.Def.Weapon;
                // A tachyon lance reaches far past the line's guns (U.SPC-T487): a hull fights at its battery's range and
                // the lance reaches out from there, so it never hangs back where only the lance bears.
                if (w == null || s.Destroyed || w.AntiSmall || w.Lance) continue;
                if (w.Missile && s.Ammo <= 0) continue;
                best = Math.Max(best, w.Missile ? w.RangeKm * 0.5 : w.RangeKm * 0.7);
            }
            return best > 0 ? best : 12;
        }

        DVec3 HostileCentroid(BattleUnit u)
        {
            var sum = DVec3.Zero;
            int n = 0;
            foreach (var o in Units)
            {
                if (!Hostile(u, o)) continue;
                sum += o.Pos;
                n++;
            }
            return n > 0 ? sum / n : DVec3.Zero;
        }

        // ---------------------------------------------------------------- weapons

        void Fire(BattleUnit u, double dt)
        {
            var c = u.Combat;
            // Knocked out by a pulse or an EMP: no gun on the hull fires until its systems come back.
            if (u.Captured || u.Surrendering || u.Stunned > 0) return;
            // Surer under a command ship's targeting computers, worse in the confusion after losing it (U.SPC-T477).
            // Worse again with an enemy recon fighter jamming it (U.SPC-T485).
            double accuracyScale = (c.BridgeIntact ? 1 : 0.6) * FleetCoordination.AccuracyFactor(u) * ElectronicWarfare.JamFactor(this, u);
            // The shared sensor picture: gunners lay on the command ship's track, not their own (U.SPC-T477).
            double picture = FleetCoordination.SharedPicture(u);
            // A small craft's weapons reload as fast as the power it gives them (U.SPC-T424).
            // A big ship that cannot shed its heat has its crew throttle the guns' reloading (U.SPC-T491).
            double weaponPower = PowerDistribution.FactorOf(u, PowerSystem.Weapons) * Radiators.Throttle(u);
            foreach (var s in c.Subsystems)
            {
                // A fleet out of supply reloads slower: short of rounds, of spares for its mounts, of power to spare (U.SPC-T93).
                // A spinal mount recharges only as fast as the hull's spinal power plants feed it (U.SPC-T490).
                if (s.Def.Weapon != null && !s.Destroyed) s.Cooldown -= dt * c.Readiness * weaponPower * SpinalMounts.ChargeRate(c, s);
            }
            // A braced crew is holding on, not working its guns: they load, but only point defence fires (U.SPC-T425).
            bool braced = !Broadsides.Fires(u);
            // The volleys are worked out afresh this step, now every gun is as loaded as it will be (U.SPC-T425).
            Broadsides.BeginVolleys(u);
            // The captain tunes the phaser arrays for the fight as it stands: a lance or a sweep (U.SPC-T488).
            EnergyWeapons.Step(this, u);
            // The Breacher bay fires a Breacher round once the habitats have the next team ready (U.SPC-T466).
            FireBreacherBay(u, dt);
            // A frigate holds every launcher for one volley, loosed together once its mark is in reach of them all (U.SPC-T475).
            bool alpha = AlphaStrike.Flies(u);
            bool release = alpha && AlphaStrike.Releases(u);
            bool loosed = false;
            foreach (var s in c.Subsystems)
            {
                var w = EnergyWeapons.Effective(s, c);
                if (w == null || s.Destroyed) continue;
                // A mount the player has taken is theirs to lay and fire, reloading or not (U.SPC-T18).
                if (Manned.Count > 0 && Manned.TryGetValue(s, out var manned))
                {
                    FireManned(u, s, manned, w, dt, accuracyScale);
                    continue;
                }
                if (braced && !w.AntiSmall) continue;
                if (s.Cooldown > 0 || s.Kind == SubsystemKind.Torpedo) continue;
                if (s.Kind == SubsystemKind.BoardingPods)
                {
                    FirePods(u, s);
                    continue;
                }
                if (w.Deploy != DeployKind.None)
                {
                    TryDeploy(u, s, w);
                    continue;
                }
                var origin = u.PointOf(s.Def);

                if (w.AntiSmall && ScreenWeapons.IsScreenMount(s.Kind))
                {
                    // The screen (U.SPC-T489): flak bursts on, or an interceptor goes after, the worst round in reach.
                    var threat = ScreenTarget(u, s, origin, w);
                    if (threat != null)
                    {
                        FireScreen(u, s, origin, w, threat, null, accuracyScale);
                        continue;
                    }
                }
                else if (w.AntiSmall)
                {
                    var missile = NearestMissile(u.Side, origin, w.RangeKm);
                    if (missile != null)
                    {
                        s.Aim = (missile.Pos - origin).Normalized;
                        s.Cooldown = w.Cooldown * (0.85 + rng.NextDouble() * 0.3);
                        if (rng.NextDouble() < w.Accuracy * accuracyScale)
                        {
                            missile.Hp -= w.Damage;
                            if (missile.Hp <= 0)
                            {
                                missile.Gone = true;
                                RoundLost(missile);
                                Emit(FxKind.MissileKill, missile.Pos, missile.Pos, 0.08, missile.Side, null, -1);
                            }
                        }
                        Emit(EnergyWeapons.FxOf(s.Kind, true) ?? FxKind.PointDefense, origin, missile.Pos, 0, u.Side, u, s.Index);
                        continue;
                    }
                }

                BattleUnit target;
                bool helm = u.PlayerControlled && u.PlayerHelm;
                if (helm && HelmVolley(u, s, origin, w, accuracyScale)) continue;
                if (u.PlayerControlled && !helm && IsPlayerAimable(u, s.Def))
                {
                    // The player's own nose guns: no AI target selection, no autofire. Side/aft
                    // batteries (a broadside hull's real firepower) fall through to the branch below
                    // completely unchanged, so piloting never silences a ship's crewed turrets.
                    if (w.EmpSeconds > 0) target = u.PlayerEmpHeld ? PlayerEmpTarget(u, origin, w) : null;
                    else if (!w.Missile)
                    {
                        // Down the crosshair, hitting only what the shot really meets (U.SPC-T270).
                        if (u.PlayerFireHeld && !u.Overheated) FirePlayerGun(u, s, origin, w);
                        else s.Aim = DVec3.Zero;
                        continue;
                    }
                    else target = u.PlayerSecondaryHeld && u.LockProgress >= 1 ? PlayerLockedTarget(u, w, origin) : null;
                }
                else
                {
                    target = ChooseWeaponTarget(u, origin, w);
                    // At a capital ship's helm the crews go for the hull the player has locked, wherever it is in their fire cones and reach.
                    if (helm && LockedFor(u, s, origin, w) is BattleUnit locked) target = locked;
                    // Hold fire on a prize our own people are boarding or closing on.
                    if (target != null && IsPrize(u, target)) target = null;
                }
                // Every mount aims within its fire cone (U.SPC-T533): a turret's wide one round its mount's normal, a spinal
                // mount's or fixed tube's narrow one along the keel. It engages only a target that lies inside it, in 3D.
                if (target != null && !u.Bears(s.Def, target.Pos)) target = null;
                // A capital ship's mounts fire under their own orders (U.SPC-T425, given to the guns that bear where the captain
                // looks): held, not at all; in a volley, a gun holds until every gun of the volley bearing on the same hull is
                // loaded, and they go together, surer and harder.
                double volley = 1;
                if (target != null && u.Broadside != null && Broadsides.IsOrdered(s))
                {
                    var order = u.Broadside.OrderOf(s);
                    if (order == BatteryOrder.HoldFire) target = null;
                    else if (order == BatteryOrder.Volley && Broadsides.IsBatteryGun(s))
                    {
                        if (!Broadsides.Releases(u, target))
                        {
                            Broadsides.Holding(u, target);
                            s.Aim = (target.Pos - origin).Normalized;
                            continue;
                        }
                        volley = 1 + Broadsides.CrewVolleyDamageBonus;
                    }
                }
                if (target == null)
                {
                    s.Aim = DVec3.Zero;
                    continue;
                }
                if (alpha && AlphaStrike.Holds(s, release))
                {
                    s.Aim = (target.Pos - origin).Normalized;
                    continue;
                }
                // No round in reach: the screen turns on fighters (U.SPC-T489); a flak cannon with only a hull near fires on it as
                // point defence does.
                if (ScreenWeapons.IsScreenMount(s.Kind) && WeakPoints.IsSmallCraft(target))
                {
                    FireScreen(u, s, origin, w, null, target, accuracyScale);
                    continue;
                }
                // A player's guided rounds go for the weak point picked on the locked hull (U.SPC-T424).
                var sub = u.PlayerControlled && w.Missile && target == u.PlayerLockTarget && u.PlayerSubTarget != null && !u.PlayerSubTarget.Destroyed
                          && target.Combat.Subsystems.Contains(u.PlayerSubTarget)
                    ? u.PlayerSubTarget
                    : PickSubsystem(target, u);
                var aimPoint = sub != null ? target.PointOf(sub.Def) : target.Pos;
                s.Aim = (aimPoint - origin).Normalized;
                s.Cooldown = w.Cooldown * (0.85 + rng.NextDouble() * 0.3);
                bool small = WeakPoints.IsSmallCraft(u);

                if (w.Missile)
                {
                    if (s.Ammo <= 0) continue;
                    // A capital torpedo tube's round flies as a torpedo: tougher for point defence to stop (U.SPC-T377); an
                    // antimatter torpedo is tougher still and hits hardest of all (U.SPC-T489).
                    var kindOf = ScreenWeapons.KindOf(s.Kind, w);
                    bool torpedo = kindOf == MissileKind.Torpedo || kindOf == MissileKind.Antimatter;
                    if (alpha && AlphaStrike.IsStrikeMount(s)) loosed = true;
                    for (int k = 0; k < w.Salvo && s.Ammo > 0; k++)
                    {
                        s.Ammo--;
                        var spread = new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * 0.4;
                        Missiles.Add(new Missile
                        {
                            Pos = origin, Vel = (u.Forward + spread + s.Aim).Normalized * (w.MissileSpeedKmS * 0.3) + u.Vel,
                            Target = target, TargetSub = sub, Damage = ScreenWeapons.Warhead(s.Kind, w), Speed = w.MissileSpeedKmS,
                            Hp = ScreenWeapons.RoundHp(s.Kind), Life = 90, Side = u.Side, Torpedo = torpedo,
                            Kind = kindOf,
                            EmpSeconds = w.EmpSeconds, EmpRadiusKm = w.EmpRadiusKm, SmallCraft = small,
                        });
                    }
                    Emit(FxKind.MissileLaunch, origin, origin + s.Aim, 0, u.Side, u, s.Index);
                    continue;
                }

                if (target.PlayerControlled)
                {
                    // At the player's own hull every shot is aimed and real (U.SPC-T270): led, late on a target
                    // crossing the gunner's sights and scattered by his skill, it hits only if it meets the hull.
                    var dir = AimAt(u, origin, target, aimPoint, w, accuracyScale * (volley > 1 ? 1 + Broadsides.CrewVolleyAccuracyBonus : 1));
                    s.Aim = dir;
                    ResolveShot(u, s, origin, dir, w, volley);
                    continue;
                }

                double dist = (aimPoint - origin).Length;
                double size = Math.Max(0.15, Math.Min(1, target.LengthKm / 0.4));
                if (target.Kind == UnitKind.Fighter) size *= w.AntiSmall ? 1.4 : 0.35;
                // A tachyon lance's shot lands the instant it is fired, so it is as sure at the end of its reach as close in (U.SPC-T487).
                double falloff = w.Lance ? 1 : 1 - 0.4 * dist / w.RangeKm * (1 - picture);
                double chance = w.Accuracy * accuracyScale * size * falloff * (volley > 1 ? 1 + Broadsides.CrewVolleyAccuracyBonus : 1);
                bool hit = rng.NextDouble() < chance;
                var kind = EnergyWeapons.FxOf(s.Kind, hit) is FxKind energy ? energy
                    : s.Kind == SubsystemKind.FighterGuns || s.Kind == SubsystemKind.Autocannon || s.Kind == SubsystemKind.AutoTurret ? FxKind.Guns
                    : w.AntiSmall ? FxKind.PointDefense
                    : w.Lance ? (hit ? FxKind.TachyonLance : FxKind.TachyonLanceMiss)
                    : SpinalMounts.IsSlug(s.Kind) ? (hit ? FxKind.Railgun : FxKind.RailgunMiss)
                    : hit ? FxKind.Beam : FxKind.BeamMiss;

                if (w.MuzzleSpeedKmS > 0)
                {
                    // A dumb kinetic round, not a hitscan: it takes real time to cross the range, so
                    // fire it off and let FireKineticRound/StepRounds decide what it actually hits.
                    FireKineticRound(u, s, origin, aimPoint, target, sub, w, hit, dist, kind, volley);
                    continue;
                }

                if (hit)
                {
                    // A plasma bolt bleeds off over the distance it crossed (U.SPC-T488); every other beam hits as hard at any range.
                    double landed = w.DamageAt(dist) * volley;
                    Emit(kind, origin, aimPoint, landed, u.Side, target, sub?.Index ?? -1);
                    ApplyDamage(target, sub, landed, aimPoint, null, small ? WeakPoints.Factor(target, sub, aimPoint - origin) : 1, aimPoint - origin);
                }
                else
                {
                    var miss = aimPoint + new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * (target.LengthKm * 2 + 0.3);
                    Emit(kind, origin, origin + (miss - origin).Normalized * Math.Min(w.RangeKm, dist * 1.3), 0, u.Side, null, -1);
                }
            }
            // The volley is away: flip and burn back out (U.SPC-T475).
            if (loosed) AlphaStrike.Loosed(u);
        }

        /// <summary>
        /// Launches a railgun slug or autocannon round: leads the target's current velocity across the
        /// round's own time of flight (same lead construction a homing missile uses in <see cref="StepMissiles"/>,
        /// just without the homing -- a kinetic round is dumb and flies the straight line it was aimed
        /// down). The hit/miss roll already happened in <see cref="Fire"/>; a hit flies true to the lead
        /// point, a miss gets the same random offset a beam's miss does and keeps going past it. Emits the
        /// muzzle-flash Fx immediately (so it still reads as instant feedback at the gun) but defers the
        /// actual <see cref="ApplyDamage"/> to <see cref="StepRounds"/>, once the round has actually crossed
        /// the intervening space.
        /// </summary>
        void FireKineticRound(BattleUnit u, Subsystem mount, DVec3 origin, DVec3 aimPoint, BattleUnit target, Subsystem targetSub, WeaponSpec w, bool hit, double dist, FxKind kind, double damageScale = 1)
        {
            double speed = w.MuzzleSpeedKmS;
            double leadTime = Math.Min(dist / speed, 20);
            var lead = aimPoint + target.Vel * leadTime;
            var impact = hit ? lead : lead + new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * (target.LengthKm * 2 + 0.3);
            var dir = impact - origin;
            double travelKm = dir.Length;
            var vel = travelKm > 1e-9 ? dir / travelKm * speed : u.Forward * speed;
            double arriveIn = travelKm / speed;

            Emit(kind, origin, hit ? impact : origin + (impact - origin).Normalized * Math.Min(w.RangeKm, dist * 1.3), 0, u.Side, null, -1);
            Rounds.Add(new KineticRound
            {
                Pos = origin, Vel = vel, Target = target, TargetSub = targetSub, Damage = w.Damage * damageScale,
                ArriveIn = arriveIn, Life = arriveIn + (hit ? 0 : 0.5), Side = u.Side, Hit = hit,
                Railgun = SpinalMounts.IsSlug(mount.Kind),
                SmallCraft = WeakPoints.IsSmallCraft(u),
            });
        }

        /// <summary>Advances every kinetic round in flight; a round that hit calls <see cref="ApplyDamage"/>
        /// the instant it arrives (skipped if the target died or got clear first), a round that missed just
        /// keeps flying until its own fuse runs out.</summary>
        void StepRounds(double dt)
        {
            foreach (var r in Rounds)
            {
                if (r.Gone) continue;
                if (r.Aimed)
                {
                    StepAimedRound(r, dt);
                    continue;
                }
                r.Pos += r.Vel * dt;
                r.ArriveIn -= dt;
                r.Life -= dt;
                if (r.Hit && r.ArriveIn <= 0)
                {
                    r.Gone = true;
                    // Not into a hull that has since come over to the firer's own side (taken as a prize),
                    // nor one that struck its colours while the round was in flight (the same race StepMissiles
                    // spares a target for with its own "spared" check).
                    if (r.Target.Present && r.Target.Side != r.Side && !r.Target.Surrendering)
                    {
                        var sub = r.TargetSub != null && !r.TargetSub.Destroyed ? r.TargetSub : null;
                        ApplyDamage(r.Target, sub, r.Damage, r.Pos, null, r.SmallCraft ? WeakPoints.Factor(r.Target, sub, r.Vel - r.Target.Vel) : 1, r.Vel - r.Target.Vel);
                    }
                }
                else if (r.Life <= 0) r.Gone = true;
            }
            Rounds.RemoveAll(r => r.Gone);
        }

        /// <summary>A mount the player can personally aim by pointing the hull's own nose at something --
        /// a forward-centred hardpoint, as opposed to a side or aft battery a crew works autonomously.</summary>
        // Point defence is never the player's to aim: it keeps engaging missiles and small craft on its own.
        // Nor is an auto turret (U.SPC-T278): its own fire control works it, whoever flies the hull.
        // A forward mount is one whose fire cone takes in dead ahead (U.SPC-T533).
        static bool IsPlayerAimable(BattleUnit u, SubsystemDef d) =>
            !(d.Weapon != null && d.Weapon.AntiSmall) && d.Kind != SubsystemKind.AutoTurret && d.Covers(u.Combat?.Spec, FireCones.Bow);

        /// <summary>
        /// One of the player's guns fires down the crosshair (U.SPC-T270): swung off the nose as far as its
        /// gimbal lets it (<see cref="Gunnery.Gimbal"/>), converged on whatever hostile the line of sight meets
        /// (else at the gun's range) so guns spread along the hull all land where the crosshair is, and
        /// building heat. What it hits is <see cref="ResolveShot"/>'s to decide.
        /// </summary>
        void FirePlayerGun(BattleUnit u, Subsystem s, DVec3 origin, WeaponSpec w)
        {
            var aim = u.PlayerAim.LengthSquared > 1e-12 ? u.PlayerAim.Normalized : u.Forward;
            var sight = Gunnery.Gimbal(aim, u.Forward, SpinalMounts.IsSlug(s.Kind) || w.Lance || s.Def.Spinal ? Gunnery.RailgunGimbalDeg : Gunnery.GimbalDeg);
            double converge = FirstHostileAlong(u.Side, u.Pos, sight, w.RangeKm, out _);
            if (converge < 0) converge = w.RangeKm;
            var toConverge = u.Pos + sight * converge - origin;
            var dir = toConverge.LengthSquared > 1e-12 ? toConverge.Normalized : sight;
            double scatter = w.MuzzleSpeedKmS > 0 && !SpinalMounts.IsSlug(s.Kind) ? Gunnery.AutocannonScatterDeg : Gunnery.BeamScatterDeg;
            dir = Gunnery.Scatter(dir, scatter * Astro.Deg2Rad, rng);
            s.Aim = dir;
            // The player's trigger keeps the gun's own rhythm, not a crew's jittered one.
            s.Cooldown = w.Cooldown;
            // A tachyon lance is fed by the reactor and held back by its recharge, not by the guns' heat (U.SPC-T487).
            // Nor is a spinal mount, which its own power plant feeds (U.SPC-T490).
            if (!w.Lance && !s.Def.Spinal) u.Heat += w.Damage * Gunnery.HeatPerDamage;
            if (u.Heat >= 1)
            {
                u.Heat = 1;
                u.Overheated = true;
            }
            ResolveShot(u, s, origin, dir, w);
        }

        /// <summary>
        /// A gun mount the player has taken (U.SPC-T18, <see cref="MountedTurret"/>): it traverses toward their aim
        /// at its own rate, never past its fire cone (a spinal railgun never past its gimbal), and on the trigger fires
        /// down its barrel at its own rhythm, an aimed shot that hits only what it meets (<see cref="ResolveShot"/>).
        /// </summary>
        void FireManned(BattleUnit u, Subsystem s, MountControl m, WeaponSpec w, double dt, double accuracyScale)
        {
            var current = s.Aim.LengthSquared > 1e-12 ? s.Aim.Normalized : MountedTurret.RestAim(u, s.Def);
            var want = m.Aim.LengthSquared > 1e-12 ? MountedTurret.Bound(u, s.Def, m.Aim.Normalized) : current;
            s.Aim = PilotFlight.ClampAim(want, current, MountedTurret.TraverseDegS(s.Kind) * Astro.Deg2Rad * dt);
            if (!m.Trigger || s.Cooldown > 0) return;
            s.Cooldown = w.Cooldown;
            bool slug = SpinalMounts.IsSlug(s.Kind);
            double scatter = (w.MuzzleSpeedKmS > 0 && !slug ? Gunnery.AutocannonScatterDeg : Gunnery.BeamScatterDeg) / Math.Max(accuracyScale, 0.1);
            ResolveShot(u, s, u.PointOf(s.Def), Gunnery.Scatter(s.Aim, scatter * Astro.Deg2Rad, rng), w);
        }

        /// <summary>
        /// Where a gunner lays his gun on the player's hull: led for the round's flight (none for a beam), but
        /// trailing a target that crosses his sights by a reaction lag, and scattered, both less the better the
        /// gun (<see cref="WeaponSpec.Accuracy"/>, cut by a lost bridge). A hull flying straight is hit; one
        /// turning, jinking or sliding across the line is missed.
        /// </summary>
        DVec3 AimAt(BattleUnit u, DVec3 origin, BattleUnit target, DVec3 aimPoint, WeaponSpec w, double accuracyScale)
        {
            double skill = Math.Max(0, Math.Min(1, w.Accuracy * accuracyScale));
            // Where he sees it: carried on from a moment ago at the velocity he has read it as having, so a hull that
            // has just changed course is not where he thinks.
            // A coordinated gunner reads the course off the shared picture, and lags it less (U.SPC-T477).
            var seen = aimPoint - (target.Vel - target.SeenVel) * (Gunnery.TrackingLagS * (1 - 0.5 * skill) * (1 - FleetCoordination.SharedPicture(u)));
            var point = w.MuzzleSpeedKmS > 0 ? Gunnery.LeadPoint(origin, u.Vel, seen, target.SeenVel, w.MuzzleSpeedKmS) : seen;
            var to = point - origin;
            var dir = to.LengthSquared > 1e-12 ? to.Normalized : u.Forward;
            return Gunnery.Scatter(dir, Gunnery.MaxScatterDeg * (1 - skill) * Astro.Deg2Rad, rng);
        }

        /// <summary>
        /// An aimed shot from <paramref name="origin"/> down <paramref name="dir"/>: a beam lands at once on the
        /// first hostile hull it meets within range, a round flies off to hit whatever it meets on the way
        /// (<see cref="StepAimedRound"/>). Either hits the hardpoint nearest where it struck.
        /// </summary>
        void ResolveShot(BattleUnit u, Subsystem s, DVec3 origin, DVec3 dir, WeaponSpec w, double damageScale = 1)
        {
            bool beam = w.MuzzleSpeedKmS <= 0;
            double damage = w.Damage * damageScale;
            if (!beam)
            {
                bool slug = SpinalMounts.IsSlug(s.Kind);
                Emit(slug ? FxKind.Railgun : FxKind.Guns, origin, origin + dir * w.RangeKm, 0, u.Side, null, -1);
                Rounds.Add(new KineticRound
                {
                    Pos = origin, Vel = u.Vel + dir * w.MuzzleSpeedKmS, Damage = damage, Life = w.RangeKm / w.MuzzleSpeedKmS,
                    Side = u.Side, Railgun = slug, Aimed = true, SmallCraft = WeakPoints.IsSmallCraft(u),
                });
                return;
            }
            double t = FirstHostileAlong(u.Side, origin, dir, w.RangeKm, out var hit);
            if (hit == null)
            {
                Emit(EnergyWeapons.FxOf(s.Kind, false) ?? (w.AntiSmall ? FxKind.PointDefense : w.Lance ? FxKind.TachyonLanceMiss : FxKind.BeamMiss), origin, origin + dir * w.RangeKm, 0, u.Side, null, -1);
                return;
            }
            var at = origin + dir * t;
            var sub = SubsystemNear(hit, at);
            // A plasma bolt bleeds off over the distance it crossed (U.SPC-T488).
            if (w.FalloffEdgeShare < 1 && w.Damage > 0) damage *= w.DamageAt(t) / w.Damage;
            Emit(EnergyWeapons.FxOf(s.Kind, true) ?? (w.AntiSmall ? FxKind.PointDefense : w.Lance ? FxKind.TachyonLance : FxKind.Beam), origin, at, damage, u.Side, hit, sub?.Index ?? -1);
            ApplyDamage(hit, sub, damage, at, null, WeakPoints.IsSmallCraft(u) ? WeakPoints.Factor(hit, sub, dir) : 1, dir);
        }

        /// <summary>An aimed round flies on, and hits the first hostile hull its path crosses this step.</summary>
        void StepAimedRound(KineticRound r, double dt)
        {
            var step = r.Vel * dt;
            double len = step.Length;
            r.Life -= dt;
            if (len > 1e-12)
            {
                double t = FirstHostileAlong(r.Side, r.Pos, step / len, len, out var hit);
                if (hit != null)
                {
                    var at = r.Pos + step / len * t;
                    r.Gone = true;
                    var sub = SubsystemNear(hit, at);
                    ApplyDamage(hit, sub, r.Damage, at, null, r.SmallCraft ? WeakPoints.Factor(hit, sub, r.Vel - hit.Vel) : 1, r.Vel - hit.Vel);
                    return;
                }
            }
            r.Pos += step;
            if (r.Life <= 0) r.Gone = true;
        }

        /// <summary>How far down a line of fire the first hull hostile to <paramref name="side"/> lies, within
        /// <paramref name="maxKm"/>; -1 (and no <paramref name="hit"/>) if the line is clear. A hull that has
        /// struck its colours is never what a shot resolves onto: it is no longer shot at while it waits on
        /// its captor's terms (see <see cref="Surrender"/>), the same as target selection already honours.</summary>
        double FirstHostileAlong(int side, DVec3 origin, DVec3 dir, double maxKm, out BattleUnit hit)
        {
            hit = null;
            double best = -1;
            foreach (var o in Units)
            {
                if (o.Side == side || !o.Present || o.Surrendering) continue;
                double t = Gunnery.RaySphere(origin, dir, maxKm, o.Pos, Gunnery.HitRadius(o));
                if (t >= 0 && (best < 0 || t < best))
                {
                    best = t;
                    hit = o;
                }
            }
            return best;
        }

        /// <summary>The hardpoint nearest where a shot struck, if one is close enough to have taken it (within a
        /// sixth of the hull's length); otherwise the shot is to the hull. A bridge buried at the centre of the hull
        /// (U.SPC-T492) is never near where a shot strikes the outside.</summary>
        static Subsystem SubsystemNear(BattleUnit target, DVec3 at)
        {
            if (target.Kind == UnitKind.Fighter) return null;
            Subsystem best = null;
            double bestD = target.LengthKm / 6;
            foreach (var s in target.Combat.Subsystems)
            {
                if (s.Destroyed || s.Def.Buried) continue;
                double d = (target.PointOf(s.Def) - at).Length;
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- broadsides at the helm (U.SPC-T272)

        /// <summary>Seconds a volley takes to build to full, the extra damage a full one does (as a share of each
        /// gun's own), and how much surer its gunners' aim is than their aim at will.</summary>
        public const double VolleyBuildSeconds = 1.5, VolleyDamageBonus = 0.5, VolleyAccuracyBonus = 0.25;

        /// <summary>How far off the line the player is looking a volley will take a target, deg.</summary>
        public const double VolleyConeDeg = 25;

        /// <summary>
        /// A mount at a capital ship's helm that bears on the side the player is looking: held, its gun waits on the
        /// volley; let go, every such gun that is loaded fires together at the hostile nearest that line (the locked one
        /// if it is near it), harder and surer the longer the volley was built. True if the mount was the volley's.
        /// </summary>
        bool HelmVolley(BattleUnit u, Subsystem s, DVec3 origin, WeaponSpec w, double accuracyScale)
        {
            if (w.Missile || w.AntiSmall || w.Supporting || u.PlayerVolleyAim.LengthSquared < 1e-12) return false;
            var look = u.PlayerVolleyAim.Normalized;
            if (!u.BearsAlong(s.Def, look)) return false;
            if (u.PlayerVolleyRelease)
            {
                var target = VolleyTarget(u, look, w.RangeKm);
                if (target == null) return false;
                double built = Math.Min(1, u.VolleyCharge / VolleyBuildSeconds);
                var sub = PickSubsystem(target);
                var aimPoint = sub != null ? target.PointOf(sub.Def) : target.Pos;
                var dir = AimAt(u, origin, target, aimPoint, w, accuracyScale * (1 + VolleyAccuracyBonus * built));
                s.Aim = dir;
                s.Cooldown = w.Cooldown;
                ResolveShot(u, s, origin, dir, w, 1 + VolleyDamageBonus * built);
                return true;
            }
            if (!u.PlayerFireHeld) return false;
            s.Aim = look;
            return true;
        }

        /// <summary>What a volley called along <paramref name="look"/> is fired at: the locked hull if it lies near that
        /// line, else the hostile nearest it within <see cref="VolleyConeDeg"/> and <paramref name="rangeKm"/>.</summary>
        BattleUnit VolleyTarget(BattleUnit u, DVec3 look, double rangeKm)
        {
            double cone = Math.Cos(VolleyConeDeg * Astro.Deg2Rad);
            BattleUnit best = null;
            double bestCos = cone;
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Surrendering || o.Captured) continue;
                var to = o.Pos - u.Pos;
                double d = to.Length;
                if (d < 1e-6 || d - o.Radius > rangeKm) continue;
                double cos = DVec3.Dot(look, to / d);
                if (o == u.PlayerLockTarget && cos > cone) return o;
                if (cos > bestCos)
                {
                    bestCos = cos;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>The player's locked hull, if this mount can reach it and it lies in the mount's fire cone.</summary>
        BattleUnit LockedFor(BattleUnit u, Subsystem s, DVec3 origin, WeaponSpec w)
        {
            var t = u.PlayerLockTarget;
            if (t == null || !Hostile(u, t) || t.Surrendering || t.Captured) return null;
            if ((t.Pos - origin).Length - t.Radius > w.RangeKm) return null;
            if (w.AntiSmall && t.Kind != UnitKind.Fighter) return null;
            return u.Bears(s.Def, t.Pos) ? t : null;
        }

        // ---------------------------------------------------------------- the dogfight arsenal (U.SPC-T271)

        /// <summary>How long a mine takes to arm once laid, and how long it lies waiting, seconds.</summary>
        public const double MineArmSeconds = 1.5, MineLifeSeconds = 240;

        /// <summary>How near a mine a hostile hull must come to set it off, km beyond the hull's own size.</summary>
        public const double MineTriggerKm = 0.12;

        /// <summary>Lays a mine astern when the mount's owner wants one: the player when asked, a crew when something
        /// is on its tail.</summary>
        void TryDeploy(BattleUnit u, Subsystem s, WeaponSpec w)
        {
            if (w.Deploy == DeployKind.Decoy)
            {
                ThrowDecoy(u, s, w);
                return;
            }
            bool want = u.PlayerControlled ? u.PlayerMineRequested : SomethingOnItsTail(u);
            if (!want || s.Ammo <= 0 || u.Stunned > 0) return;
            s.Ammo--;
            s.Cooldown = w.Cooldown;
            var at = u.Pos - u.Forward * (u.Radius + 0.04);
            Mines.Add(new Mine { Pos = at, Side = u.Side, Damage = w.Damage, BlastKm = w.RangeKm, ArmIn = MineArmSeconds, Life = MineLifeSeconds });
            Emit(FxKind.Deployed, at, at, 0.02, u.Side, u, s.Index);
        }

        /// <summary>An armed hostile close astern: what a crew lays a mine for.</summary>
        bool SomethingOnItsTail(BattleUnit u)
        {
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Captured || o.Surrendering || !o.Combat.Armed) continue;
                var to = o.Pos - u.Pos;
                double d = to.Length;
                if (d < 2.5 && d > 1e-6 && DVec3.Dot(u.Forward, to / d) < -0.3) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- countermeasures (U.SPC-T424)

        /// <summary>
        /// Throws a flare-and-chaff cloud astern when the dispenser's owner wants one (the player when asked, a pilot when
        /// a guided round is closing on it), and gives every round homing on the craft within
        /// <see cref="Countermeasures.ReachKm"/> its chance of being drawn off onto it.
        /// </summary>
        void ThrowDecoy(BattleUnit u, Subsystem s, WeaponSpec w)
        {
            bool want = u.PlayerControlled ? u.PlayerDecoyRequested : MissileInbound(u, Countermeasures.AiReactKm) != null;
            if (!want || s.Ammo <= 0 || u.Stunned > 0) return;
            s.Ammo--;
            s.Cooldown = w.Cooldown;
            var at = u.Pos - u.Forward * (u.Radius + 0.02);
            var decoy = new Decoy { Pos = at, Vel = u.Vel * 0.6 - u.Forward * 0.05, Side = u.Side, Life = Countermeasures.LifeSeconds };
            Decoys.Add(decoy);
            Emit(FxKind.Deployed, at, at, 0.03, u.Side, u, s.Index);
            foreach (var m in Missiles)
            {
                if (m.Gone || m.Target != u || m.Decoy != null || (m.Pos - u.Pos).Length > Countermeasures.ReachKm) continue;
                // A bomber's torpedo riding a recon fighter's paint is seldom fooled (U.SPC-T485).
                if (rng.NextDouble() < ElectronicWarfare.SeduceChance(this, m)) m.Decoy = decoy;
            }
        }

        /// <summary>The nearest guided round homing on <paramref name="u"/> within <paramref name="km"/> and not already drawn off, or null.</summary>
        public Missile MissileInbound(BattleUnit u, double km)
        {
            Missile best = null;
            double bestD = km;
            foreach (var m in Missiles)
            {
                if (m.Gone || m.Target != u || m.Decoy != null || m.Kind == MissileKind.Pod || m.Kind == MissileKind.Breacher) continue;
                double d = (m.Pos - u.Pos).Length;
                if (d <= bestD)
                {
                    bestD = d;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>Clouds drift and burn out.</summary>
        void StepDecoys(double dt)
        {
            foreach (var d in Decoys)
            {
                d.Pos += d.Vel * dt;
                d.Life -= dt;
                if (d.Life <= 0) d.Gone = true;
            }
            Decoys.RemoveAll(d => d.Gone);
        }

        /// <summary>What the player's EMP launcher fires at: the locked hull if it is in reach, else the hostile nearest
        /// the crosshair (within 25 degrees of it) that is not already drifting dead.</summary>
        BattleUnit PlayerEmpTarget(BattleUnit u, DVec3 origin, WeaponSpec w)
        {
            var locked = PlayerLockedTarget(u, w, origin);
            if (locked != null) return locked;
            var aim = u.PlayerAim.LengthSquared > 1e-12 ? u.PlayerAim.Normalized : u.Forward;
            BattleUnit best = null;
            double bestCos = Math.Cos(25 * Astro.Deg2Rad);
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Stunned > 0 || o.Surrendering || o.Captured) continue;
                var to = o.Pos - origin;
                double d = to.Length;
                if (d < 1e-6 || d - o.Radius > w.RangeKm) continue;
                double cos = DVec3.Dot(aim, to / d);
                if (cos > bestCos)
                {
                    bestCos = cos;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>The stun an EMP burst leaves a hull of <paramref name="lengthKm"/>: the full
        /// <paramref name="seconds"/> for a small craft, less for a bigger hull with more to knock out.</summary>
        public static double EmpStunSeconds(double seconds, double lengthKm) => seconds * Math.Min(1, 0.2 / Math.Max(lengthKm, 1e-6));

        /// <summary>An EMP round bursts: every hull hostile to <paramref name="side"/> within its reach has its drives and
        /// guns knocked out (<see cref="BattleUnit.Stunned"/>) and its shields halved.</summary>
        public void EmpBurst(DVec3 at, int side, double seconds, double radiusKm)
        {
            Emit(FxKind.EmpBurst, at, at, radiusKm, side, null, -1);
            foreach (var o in Units)
            {
                if (o.Side == side || !o.Present || o.Surrendering) continue;
                if ((o.Pos - at).Length > radiusKm + Gunnery.HitRadius(o)) continue;
                o.Stunned = Math.Max(o.Stunned, EmpStunSeconds(seconds, o.LengthKm));
                foreach (var g in o.Combat.Subsystems)
                    if (g.Kind == SubsystemKind.ShieldGenerator) g.Shield *= 0.5;
            }
        }

        /// <summary>Mines arm, wait and run out; an armed one with a hostile hull inside its trigger blows, hurting
        /// everything hostile in its blast, harder the nearer the centre.</summary>
        void StepMines(double dt)
        {
            foreach (var m in Mines)
            {
                if (m.Gone) continue;
                m.Pos += m.Vel * dt;
                m.ArmIn -= dt;
                m.Life -= dt;
                if (m.Life <= 0)
                {
                    m.Gone = true;
                    continue;
                }
                if (!m.Armed) continue;
                bool tripped = false;
                foreach (var o in Units)
                    if (o.Side != m.Side && o.Present && (o.Pos - m.Pos).Length <= MineTriggerKm + Gunnery.HitRadius(o))
                    {
                        tripped = true;
                        break;
                    }
                if (!tripped) continue;
                m.Gone = true;
                Emit(FxKind.MineBlast, m.Pos, m.Pos, m.BlastKm, m.Side, null, -1);
                for (int i = 0; i < Units.Count; i++)
                {
                    var o = Units[i];
                    if (o.Side == m.Side || !o.Present || o.Surrendering) continue;
                    double d = Math.Max(0, (o.Pos - m.Pos).Length - Gunnery.HitRadius(o));
                    if (d > m.BlastKm) continue;
                    ApplyDamage(o, null, m.Damage * (1 - d / m.BlastKm), m.Pos);
                }
            }
            Mines.RemoveAll(m => m.Gone);
        }

        readonly List<Subsystem> burnedOut = new List<Subsystem>();

        /// <summary>A big ship's heat this step (U.SPC-T491, <see cref="Radiators.Step"/>), with the guns reloading as fast as
        /// <see cref="Fire"/> lets them, and every system the heat burns out shown going.</summary>
        void StepHeat(BattleUnit u, double dt)
        {
            var c = u.Combat;
            bool working = !u.Captured && !u.Surrendering && u.Stunned <= 0;
            double reload = working ? c.Readiness * PowerDistribution.FactorOf(u, PowerSystem.Weapons) : 0;
            burnedOut.Clear();
            Radiators.Step(u, dt, reload, burnedOut);
            foreach (var s in burnedOut)
            {
                var at = u.PointOf(s.Def);
                Emit(FxKind.SubsystemDestroyed, at, at, u.LengthKm * 0.12, u.Side, u, s.Index);
            }
        }

        /// <summary>
        /// The player's guns cool and the missile lock builds or slips (U.SPC-T270): the lock grows while the
        /// crosshair sits on the locked target and, once made, holds while the target stays ahead of the nose.
        /// </summary>
        void StepPlayerWeapons(BattleUnit u, double dt)
        {
            // Power to the weapons cools the guns faster (U.SPC-T424).
            u.Heat = Math.Max(0, u.Heat - Gunnery.HeatCoolPerS * PowerDistribution.FactorOf(u, PowerSystem.Weapons) * dt);
            if (u.PlayerHelm && u.PlayerFireHeld) u.VolleyCharge = Math.Min(VolleyBuildSeconds, u.VolleyCharge + dt);
            if (u.Overheated && u.Heat < Gunnery.HeatRecoverBelow) u.Overheated = false;

            var t = u.PlayerLockTarget;
            if (t == null || !t.Present || !Hostile(u, t))
            {
                u.LockProgress = 0;
                return;
            }
            var to = t.Pos - u.Pos;
            if (to.LengthSquared < 1e-12) return;
            var dir = to.Normalized;
            var aim = u.PlayerAim.LengthSquared > 1e-12 ? u.PlayerAim.Normalized : u.Forward;
            if (u.LockProgress >= 1)
            {
                if (DVec3.Dot(u.Forward, dir) < Math.Cos(Gunnery.LockHoldDeg * Astro.Deg2Rad)) u.LockProgress = 0;
                return;
            }
            bool onIt = DVec3.Dot(aim, dir) >= Math.Cos(Gunnery.LockConeDeg * Astro.Deg2Rad);
            u.LockProgress = onIt ? Math.Min(1, u.LockProgress + dt / Gunnery.LockSeconds) : Math.Max(0, u.LockProgress - 2 * dt / Gunnery.LockSeconds);
        }

        /// <summary>A player's locked-on missile target, valid only while it is still hostile, present
        /// and in range -- cleared (by the Runtime pilot loop) the moment it stops being any of those.</summary>
        BattleUnit PlayerLockedTarget(BattleUnit u, WeaponSpec w, DVec3 origin)
        {
            var t = u.PlayerLockTarget;
            if (t == null || !t.Present || !Hostile(u, t) || t.Surrendering || t.Captured) return null;
            return (t.Pos - origin).Length - t.Radius <= w.RangeKm ? t : null;
        }

        BattleUnit ChooseWeaponTarget(BattleUnit u, DVec3 origin, WeaponSpec w)
        {
            if (w.AntiSmall)
            {
                // No missile in reach: point defence turns on fighters, then anything close.
                BattleUnit near = null;
                double best = w.RangeKm;
                foreach (var o in Units)
                {
                    if (!Hostile(u, o) || o.Captured || o.Surrendering || HoldsFireOn(u, o)) continue;
                    // An interceptor is wasted on a hull: it goes only after small craft (U.SPC-T489).
                    if (w.Missile && !WeakPoints.IsSmallCraft(o)) continue;
                    double d = (o.Pos - origin).Length - o.Radius;
                    if ((d < best && (near == null || near.Kind != UnitKind.Fighter || o.Kind == UnitKind.Fighter)) || (o.Kind == UnitKind.Fighter && near != null && near.Kind != UnitKind.Fighter && d < w.RangeKm))
                    {
                        best = d;
                        near = o;
                    }
                }
                return near;
            }
            var t = u.Target;
            // Its own target only while that is still a fair one: not struck its colours, not since taken
            // by this side (a capture doesn't clear everyone's target the way a surrender does).
            bool emp = w.EmpSeconds > 0;
            // A dogfight missile's seeker takes a fighter as readily as a hull (U.SPC-T424).
            bool seeksSmall = emp || w.SeeksSmallCraft;
            if (t != null && Hostile(u, t) && !t.Surrendering && !t.Captured && !HoldsFireOn(u, t) && (t.Pos - origin).Length - t.Radius <= w.RangeKm && !(t.Kind == UnitKind.Fighter && w.Missile && !seeksSmall) && !(emp && t.Stunned > 0))
                return t;
            BattleUnit any = null;
            double nearest = w.RangeKm;
            foreach (var o in Units)
            {
                // An EMP round is for a small craft too, but wasted on one already drifting dead (U.SPC-T271).
                // An auto turret's fire control takes fighters as readily as hulls (U.SPC-T278).
                bool takesFighters = seeksSmall || u.Kind == UnitKind.Fighter || ReferenceEquals(w, WeaponSpec.AutoTurret);
                if (!Hostile(u, o) || o.Captured || o.Surrendering || HoldsFireOn(u, o) || (emp && o.Stunned > 0) || (o.Kind == UnitKind.Fighter && (w.Missile && !seeksSmall || !takesFighters))) continue;
                double d = (o.Pos - origin).Length - o.Radius;
                if (d < nearest)
                {
                    nearest = d;
                    any = o;
                }
            }
            return any;
        }

        // ---------------------------------------------------------------- the screen (U.SPC-T489)

        /// <summary>
        /// The hostile round a screen mount on <paramref name="u"/> shoots at: the worst threat (<see cref="ScreenWeapons.Threat"/>)
        /// within its reach, not merely the nearest. An interceptor launcher skips a round one of its side's interceptors is
        /// already hunting, so a salvo spreads over a swarm instead of piling onto its leader.
        /// </summary>
        Missile ScreenTarget(BattleUnit u, Subsystem mount, DVec3 origin, WeaponSpec w)
        {
            bool interceptor = mount.Kind == SubsystemKind.InterceptorMissile;
            if (interceptor && mount.Ammo <= 0) return null;
            Missile best = null;
            double bestThreat = 0;
            foreach (var m in Missiles)
            {
                if (m.Side == u.Side || !ScreenWeapons.IsThreat(m)) continue;
                if ((m.Pos - origin).Length > w.RangeKm) continue;
                if (interceptor && Hunted(m, u.Side)) continue;
                double threat = ScreenWeapons.Threat(m);
                if (threat > bestThreat)
                {
                    bestThreat = threat;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>Where the danger to <paramref name="ward"/> comes from, for its screen <paramref name="u"/> to stand in the way
        /// of: the centre of the hostile rounds homing on it, weighted by their warheads, or the enemy's centre when none are.</summary>
        DVec3 ThreatTo(BattleUnit ward, BattleUnit u)
        {
            var sum = DVec3.Zero;
            double weight = 0;
            foreach (var m in Missiles)
            {
                if (m.Gone || m.Side == u.Side || m.Target != ward || !ScreenWeapons.IsThreat(m)) continue;
                double w = Math.Max(m.Damage, 1);
                sum += m.Pos * w;
                weight += w;
            }
            return weight > 0 ? sum / weight : HostileCentroid(u);
        }

        /// <summary>Whether an interceptor of <paramref name="side"/> is already after <paramref name="m"/>.</summary>
        bool Hunted(Missile m, int side)
        {
            foreach (var i in Missiles)
                if (!i.Gone && i.Kind == MissileKind.Interceptor && i.Side == side && i.Prey == m) return true;
            return false;
        }

        /// <summary>
        /// A screen mount fires on a hostile round (<paramref name="round"/>) or, with none in reach, a fighter
        /// (<paramref name="craft"/>): a flak cannon lays a shell that bursts on it (scattered by a miss, but a burst among a
        /// swarm still finds something), an interceptor launcher sends a round after it.
        /// </summary>
        void FireScreen(BattleUnit u, Subsystem s, DVec3 origin, WeaponSpec w, Missile round, BattleUnit craft, double accuracyScale)
        {
            var at = round != null ? round.Pos : craft.Pos;
            s.Aim = (at - origin).Normalized;
            s.Cooldown = w.Cooldown * (0.85 + rng.NextDouble() * 0.3);
            if (s.Kind == SubsystemKind.InterceptorMissile)
            {
                if (s.Ammo <= 0) return;
                s.Ammo--;
                Missiles.Add(new Missile
                {
                    Pos = origin, Vel = s.Aim * (w.MissileSpeedKmS * 0.4) + u.Vel,
                    Target = craft, Prey = round, Damage = w.Damage, Speed = w.MissileSpeedKmS,
                    Hp = ScreenWeapons.InterceptorHp, Life = ScreenWeapons.InterceptorLifeSeconds, Side = u.Side,
                    Kind = MissileKind.Interceptor,
                });
                Emit(FxKind.MissileLaunch, origin, origin + s.Aim, 0, u.Side, u, s.Index);
                return;
            }
            double burst = Math.Max(w.BurstKm, 0.05) * ScreenWeapons.FlakBurst.Value;
            if (rng.NextDouble() >= w.Accuracy * accuracyScale)
            {
                // Wide: the shell bursts a burst and a half or two off its mark.
                var off = new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
                at += (off.LengthSquared > 1e-9 ? off.Normalized : DVec3.UnitX) * burst * (1.5 + rng.NextDouble() * 0.5);
            }
            FlakBurstAt(at, u.Side, w.Damage * ScreenWeapons.FlakDamage.Value, burst);
            Emit(FxKind.FlakBurst, origin, at, burst, u.Side, u, s.Index);
        }

        /// <summary>A flak burst at <paramref name="at"/>: every hostile round (an interceptor too) and every hostile small craft
        /// within <paramref name="radiusKm"/> takes its <paramref name="damage"/>, less toward the burst's edge.</summary>
        public void FlakBurstAt(DVec3 at, int side, double damage, double radiusKm)
        {
            if (damage <= 0 || radiusKm <= 0) return;
            foreach (var m in Missiles)
            {
                if (m.Gone || m.Side == side) continue;
                double d = (m.Pos - at).Length;
                if (d > radiusKm) continue;
                m.Hp -= damage * (1 - 0.5 * d / radiusKm);
                if (m.Hp <= 0)
                {
                    m.Gone = true;
                    RoundLost(m);
                    Emit(FxKind.MissileKill, m.Pos, m.Pos, 0.08, m.Side, null, -1);
                }
            }
            for (int i = 0; i < Units.Count; i++)
            {
                var o = Units[i];
                if (o.Side == side || !o.Present || !WeakPoints.IsSmallCraft(o) || o.Surrendering) continue;
                double d = Math.Max(0, (o.Pos - at).Length - o.Radius);
                if (d > radiusKm) continue;
                ApplyDamage(o, null, damage * (1 - 0.5 * d / radiusKm), at);
            }
        }

        /// <summary>
        /// An interceptor hunting a round: it leads its prey hard (it is far quicker and more agile than what it hunts), and
        /// its proximity fuze goes off when it passes near enough, killing the round at <see cref="ScreenWeapons.InterceptorReliability"/>.
        /// A prey gone already (shot down, gone off, drawn away) and it looks for another within <see cref="ScreenWeapons.ReacquireKm"/>
        /// nobody else is hunting, or gives up.
        /// </summary>
        void StepInterceptor(Missile m, double dt)
        {
            if (m.Life <= 0)
            {
                m.Gone = true;
                Emit(FxKind.MissileKill, m.Pos, m.Pos, 0.03, m.Side, null, -1);
                return;
            }
            if (m.Prey == null || m.Prey.Gone)
            {
                m.Prey = null;
                double bestD = ScreenWeapons.ReacquireKm;
                foreach (var o in Missiles)
                {
                    if (o.Side == m.Side || !ScreenWeapons.IsThreat(o) || Hunted(o, m.Side)) continue;
                    double d = (o.Pos - m.Pos).Length;
                    if (d < bestD)
                    {
                        bestD = d;
                        m.Prey = o;
                    }
                }
                if (m.Prey == null)
                {
                    m.Gone = true;
                    Emit(FxKind.MissileKill, m.Pos, m.Pos, 0.03, m.Side, null, -1);
                    return;
                }
            }
            var prey = m.Prey;
            var to = prey.Pos - m.Pos;
            double dist = to.Length;
            var lead = prey.Pos + prey.Vel * Math.Min(dist / Math.Max(m.Speed, 1e-6), 10);
            var want = (lead - m.Pos).Normalized * m.Speed;
            var dv = want - m.Vel;
            double turn = m.Speed * 1.2 * dt;
            m.Vel += dv.Length > turn ? dv.Normalized * turn : dv;
            var start = m.Pos;
            m.Pos += m.Vel * dt;
            // Swept, and allowing for the prey's own motion this step, so neither a coarse step nor a crossing shot slips past.
            if (SegmentPointDistance(start, m.Pos, prey.Pos) > ScreenWeapons.FuzeKm + prey.Vel.Length * dt) return;
            m.Gone = true;
            Emit(FxKind.MissileKill, m.Pos, m.Pos, 0.04, m.Side, null, -1);
            if (rng.NextDouble() >= ScreenWeapons.InterceptorReliability.Value) return;
            prey.Hp -= m.Damage;
            if (prey.Hp > 0) return;
            prey.Gone = true;
            RoundLost(prey);
            Emit(FxKind.MissileKill, prey.Pos, prey.Pos, 0.08, prey.Side, null, -1);
        }

        Missile NearestMissile(int side, DVec3 origin, double range)
        {
            Missile best = null;
            double bestD = range * 2;
            foreach (var m in Missiles)
            {
                // Another side's interceptors threaten nothing a laser need waste a shot on (U.SPC-T489).
                if (m.Gone || m.Side == side || m.Kind == MissileKind.Interceptor) continue;
                double d = (m.Pos - origin).Length;
                if (d > range) continue;
                // A Breacher round tumbling adrift threatens nobody (U.SPC-T519): shot at only with nothing live in reach.
                if (m.Adrift) d += range;
                if (d < bestD)
                {
                    bestD = d;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>Gunners go for weapons first, then shields, engines and the bridge; the reactor is a lucky shot; and any of
        /// them the more in a patch of hull whose shield field is weakened (U.SPC-T501, <see cref="ShieldEmitters.FocusWeight"/>). Raiders
        /// working a merchant or civilian hull (<paramref name="shooter"/>, see <see cref="RaidDoctrine"/>, U.SPC-T374) go for
        /// its shields and engines instead, to leave it dead in space, and never its reactor.</summary>
        public Subsystem PickSubsystem(BattleUnit target, BattleUnit shooter = null)
        {
            bool robbing = shooter != null && !shooter.PlayerControlled && RaidDoctrine.Robs(shooter.Faction, target.Ship);
            if (target.Kind == UnitKind.Fighter) return null;
            bool focus = Broadsides.FocusesFire(shooter);
            double total = 0;
            var subs = target.Combat.Subsystems;
            var weights = new double[subs.Count];
            for (int i = 0; i < subs.Count; i++)
            {
                var s = subs[i];
                if (s.Destroyed) continue;
                double w;
                if (robbing) w = RaidDoctrine.SubsystemWeight(s.Kind, s.Shield > 0);
                else switch (s.Kind)
                {
                    case SubsystemKind.HeavyLaser:
                    case SubsystemKind.LaserTurret:
                    case SubsystemKind.Railgun:
                    case SubsystemKind.Autocannon:
                    case SubsystemKind.LightRailgun:
                    case SubsystemKind.AutoTurret:
                    case SubsystemKind.TorpedoTube:
                    case SubsystemKind.PhaserArray:
                    case SubsystemKind.PlasmaCannon:
                    case SubsystemKind.AntimatterTorpedo:
                    case SubsystemKind.MissileLauncher: w = 3; break;
                    // The gun that outranges them all is the one gunners most want silenced (U.SPC-T487).
                    case SubsystemKind.TachyonLance:
                    // As are a dreadnought's spinal guns (U.SPC-T490).
                    case SubsystemKind.Coilgun: w = 4; break;
                    case SubsystemKind.ShieldGenerator: w = s.Shield > 0 ? 2.5 : 0.5; break;
                    case SubsystemKind.Engine: w = 1.4; break;
                    case SubsystemKind.Hangar: w = target.Combat.Fighters > 0 ? 1.8 : 0.4; break;
                    case SubsystemKind.Bridge: w = 1.1; break;
                    case SubsystemKind.PointDefense: w = 0.8; break;
                    // The screen is worth knocking out before the strike goes in (U.SPC-T489).
                    case SubsystemKind.FlakCannon:
                    case SubsystemKind.InterceptorMissile: w = 1.2; break;
                    case SubsystemKind.Reactor: w = 0.25; break;
                    // A radiator panel is a weak point, the more worth it on a hull already running hot (U.SPC-T491).
                    case SubsystemKind.Radiator: w = Radiators.TargetWeight(target); break;
                    default: w = 0.5; break;
                }
                // A bridge buried at the centre of the hull (U.SPC-T492) is worth aiming at only once the hull is holed deep
                // enough for fire to get through to it.
                if (s.Def.Buried) w *= BuriedBridges.Reach(target.Combat, s);
                // Fire goes where the shield is already weak (U.SPC-T501): a mount in a patch whose field is down or whose
                // emitter is shot away, unless a captain at the helm has told the crews to spread it.
                if (focus) w *= ShieldEmitters.FocusWeight(target.Combat, s);
                weights[i] = w;
                total += w;
            }
            if (total <= 0) return null;
            double pick = rng.NextDouble() * total;
            for (int i = 0; i < subs.Count; i++)
            {
                pick -= weights[i];
                if (weights[i] > 0 && pick <= 0) return subs[i];
            }
            return null;
        }

        /// <summary>
        /// Shields soak damage first; armour blunts each hit (small guns most); what gets through is
        /// split between the struck hardpoint and the hull. A lost reactor takes the ship with it. What gets through the
        /// shields is multiplied by <paramref name="exposed"/>: a small craft's hit on an open weak point (U.SPC-T424, see
        /// <see cref="WeakPoints.Factor"/>). A <paramref name="strikeTorpedo"/> (a bomber's) that arrives where the shield
        /// is already down hits a capital's bare hull harder (U.SPC-T484, see <see cref="StrikeTorpedoes"/>).
        /// </summary>
        public void ApplyDamage(BattleUnit target, Subsystem sub, double damage, DVec3 at, DVec3? shieldAt = null, double exposed = 1, DVec3? incoming = null,
            bool strikeTorpedo = false)
        {
            if (!target.Present) return;
            var c = target.Combat;
            // The shield that takes it is the field over the patch of hull it strikes, in 3D (U.SPC-T501); a guided munition
            // strikes where it crossed into the hull on the side it came in from, not at the hardpoint it homed on (U.SPC-T380).
            var strike = target.ShieldStrike(shieldAt ?? at, incoming);
            var shieldPoint = target.PointLocal(strike);
            var generator = c.EmitterFor(strike);
            if (strikeTorpedo) damage *= StrikeTorpedoes.Factor(target, generator == null || generator.Shield <= 0);
            if (generator != null)
            {
                generator.LastHitSeconds = Seconds;
                if (generator.Shield > 0)
                {
                    double soaked = Math.Min(generator.Shield, damage);
                    generator.Shield -= soaked;
                    damage -= soaked;
                    // Sub names the emitter whose field took it, so the bubble it raises is that patch's (U.SPC-T40).
                    Emit(FxKind.ShieldHit, shieldPoint, target.Pos, target.LengthKm, target.Side, target, generator.Index);
                    if (damage <= 0) return;
                }
            }
            if (sub != null && exposed > 1) damage *= exposed;
            // A capital ship's crew braced for the broadside shrugs off part of it (U.SPC-T425), and a shot meeting its hull
            // obliquely, measured against the hull's own surface where the shot's line strikes it in 3D, crosses thicker plate
            // and is partly turned aside (U.SPC-T534).
            double armour = c.Spec.Armor;
            if (Broadsides.Commands(target))
            {
                damage *= Broadsides.BraceFactor(target);
                // A weak point struck from its open side has no armour to angle (U.SPC-T424).
                if (incoming.HasValue && !(sub != null && exposed > 1))
                {
                    var (thicker, through) = ArmourAngling.Angling(target, shieldAt ?? at, incoming.Value);
                    armour *= thicker;
                    damage *= through;
                }
            }
            damage = Math.Max(damage * 0.15, damage - armour);
            Emit(FxKind.Impact, at, at, damage, target.Side, target, sub?.Index ?? -1);
            if (sub != null && !sub.Destroyed)
            {
                // A buried bridge (U.SPC-T492) takes only what gets through the hull around it; the rest stays in the hull.
                double toSub = 0.65 * BuriedBridges.Reach(c, sub);
                sub.Hp -= damage * toSub;
                c.Hull -= damage * (1 - toSub);
                if (sub.Hp <= 0)
                {
                    c.Hull -= -sub.Hp;
                    sub.Hp = 0;
                    Emit(FxKind.SubsystemDestroyed, at, at, target.LengthKm * 0.12, target.Side, target, sub.Index);
                    if (sub.Kind == SubsystemKind.Reactor) c.Hull = 0;
                    if (sub.Kind == SubsystemKind.ShieldGenerator) sub.Shield = 0;
                    if (sub.Kind == SubsystemKind.Hangar) c.Fighters = Math.Max(0, c.Fighters - 6);
                }
            }
            else c.Hull -= damage;

            if (c.Hull <= 0)
            {
                c.Hull = 0;
                target.Dead = true;
                Emit(FxKind.UnitDestroyed, target.Pos, target.Pos, target.LengthKm, target.Side, target, -1);
                UnitDestroyed?.Invoke(this, target);
            }
        }

        /// <summary>
        /// A collision's damage to a unit (U.SPC-T60): <paramref name="hp"/> (see <see cref="Collisions.DamageHp"/>)
        /// done at battle-local point <paramref name="at"/> to the hardpoints on that side, through its shields there
        /// (<see cref="Collisions.ApplyDamage"/>), with the same effects and the same death as a shot.
        /// </summary>
        public void ApplyImpact(BattleUnit target, DVec3 at, double hp)
        {
            if (!target.Present || hp <= 0) return;
            var c = target.Combat;
            double shieldBefore = c.Shield;
            var generator = c.EmitterFor(target.ShieldStrike(at));
            var lost = new List<Subsystem>();
            double through = Collisions.ApplyDamage(c, target.ToLocal(at), hp, Seconds, lost);
            if (c.Shield < shieldBefore) Emit(FxKind.ShieldHit, at, target.Pos, target.LengthKm, target.Side, target, generator?.Index ?? -1);
            if (through > 0) Emit(FxKind.Impact, at, at, through, target.Side, target, -1);
            foreach (var s in lost) Emit(FxKind.SubsystemDestroyed, at, at, target.LengthKm * 0.12, target.Side, target, s.Index);
            if (c.Hull <= 0)
            {
                c.Hull = 0;
                target.Dead = true;
                Emit(FxKind.UnitDestroyed, target.Pos, target.Pos, target.LengthKm, target.Side, target, -1);
                UnitDestroyed?.Invoke(this, target);
            }
        }

        void StepMissiles(double dt)
        {
            foreach (var m in Missiles)
            {
                if (m.Gone) continue;
                m.Life -= dt;
                // An interceptor after a round flies its own hunt (U.SPC-T489).
                if (m.Kind == MissileKind.Interceptor && m.Target == null)
                {
                    StepInterceptor(m, dt);
                    continue;
                }
                // A Breacher round flies its own ballistic course (U.SPC-T466).
                if (m.Kind == MissileKind.Breacher)
                {
                    StepBreacher(m, dt);
                    continue;
                }
                // A hull that has struck its colours is no longer shot at, nor boarded until its terms open it.
                bool spared = m.Target != null && m.Target.Surrendering && !(m.Kind == MissileKind.Pod && m.Target.SurrenderOpenToBoarding);
                if (m.Life <= 0 || m.Target == null || !m.Target.Present || m.Target.Side == m.Side || spared)
                {
                    m.Gone = true;
                    Emit(FxKind.MissileKill, m.Pos, m.Pos, 0.05, m.Side, null, -1);
                    continue;
                }
                // Drawn off by a flare-and-chaff cloud (U.SPC-T424): it chases the cloud and goes off in it, harmlessly.
                if (m.Decoy != null)
                {
                    var toDecoy = m.Decoy.Pos - m.Pos;
                    if (m.Decoy.Gone || toDecoy.Length <= Math.Max(0.03, m.Speed * dt))
                    {
                        m.Gone = true;
                        Emit(FxKind.MissileKill, m.Pos, m.Pos, 0.05, m.Side, null, -1);
                        continue;
                    }
                    var chase = toDecoy.Normalized * m.Speed - m.Vel;
                    double swing = m.Speed * 0.6 * dt;
                    m.Vel += chase.Length > swing ? chase.Normalized * swing : chase;
                    m.Pos += m.Vel * dt;
                    continue;
                }
                var t = m.Target;
                var aim = m.TargetSub != null && !m.TargetSub.Destroyed ? t.PointOf(m.TargetSub.Def) : t.Pos;
                var to = aim - m.Pos;
                double dist = to.Length;
                // Lead the target a little and close at full speed.
                var lead = aim + t.Vel * Math.Min(dist / Math.Max(m.Speed, 1e-6), 20);
                var want = (lead - m.Pos).Normalized * m.Speed;
                var dv = want - m.Vel;
                // A bomber's torpedo on a target a recon fighter is painting steers onto it that much harder (U.SPC-T485).
                double turn = m.Speed * 0.6 * dt * ElectronicWarfare.Guidance(this, m);
                m.Vel += dv.Length > turn ? dv.Normalized * turn : dv;
                var start = m.Pos;
                m.Pos += m.Vel * dt;
                // Swept test, so a coarse step cannot pass through the target.
                if (SegmentPointDistance(start, m.Pos, aim) < t.Radius * 0.6 + 0.05)
                {
                    m.Gone = true;
                    // Where the warhead crossed the hull toward where it came from: the arc it has to get through.
                    var from = start - t.Pos;
                    if (from.LengthSquared < 1e-12) from = m.Vel * -1;
                    var entry = t.Pos + from.Normalized * t.Radius;
                    if (m.Kind == MissileKind.Pod) PodHit(m, t, aim, entry);
                    else if (m.Kind == MissileKind.Emp) EmpBurst(m.Pos, m.Side, m.EmpSeconds, m.EmpRadiusKm);
                    else
                    {
                        var sub = m.TargetSub != null && !m.TargetSub.Destroyed ? m.TargetSub : PickSubsystem(t);
                        ApplyDamage(t, sub, m.Damage, aim, entry, m.SmallCraft ? WeakPoints.Factor(t, sub, m.Vel - t.Vel) : 1, m.Vel - t.Vel,
                            StrikeTorpedoes.IsStrike(m));
                    }
                }
            }
            Missiles.RemoveAll(m => m.Gone);
        }

        // ---------------------------------------------------------------- boarding

        /// <summary>
        /// Pods go at hulls whose shields are down (they cannot get through a shield); each carries a
        /// squad. A ship keeps a reserve of marines aboard to defend itself.
        /// </summary>
        void FirePods(BattleUnit u, Subsystem s)
        {
            if (s.Ammo <= 0 || u.Combat.MarinesAvailable < WeaponSpec.PodMarines * 2) return;
            BattleUnit target = null;
            double best = s.Def.Weapon.RangeKm;
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Captured || o.Kind == UnitKind.Fighter) continue;
                // A hull waiting on terms isn't fired on, pods included -- unless the terms are to take it.
                if (o.Surrendering && !o.SurrenderOpenToBoarding) continue;
                if (o.Combat.Shield > o.Combat.Spec.Shield * 0.05 && o.Combat.HasShieldGenerator) continue;
                // A rival's boarding is theirs; an ally's on this side is joined (U.SPC-T392).
                if (!o.Combat.Interior.Admits(u.Faction, u.Side)) continue;
                double d = (o.Pos - u.Pos).Length;
                if (o.Combat.Disabled) d *= 0.5;
                if (d < best)
                {
                    best = d;
                    target = o;
                }
            }
            if (target == null) return;
            var origin = u.PointOf(s.Def);
            s.Cooldown = s.Def.Weapon.Cooldown * (0.85 + rng.NextDouble() * 0.3);
            for (int k = 0; k < s.Def.Weapon.Salvo && s.Ammo > 0; k++)
            {
                if (u.Combat.MarinesAvailable < WeaponSpec.PodMarines * 2) break;
                s.Ammo--;
                RemoveMarines(u.Combat.Interior, WeaponSpec.PodMarines);
                var spread = new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * 0.3;
                Missiles.Add(new Missile
                {
                    Pos = origin, Vel = ((target.Pos - origin).Normalized + spread).Normalized * 0.3 + u.Vel, Target = target, TargetSub = PickSubsystem(target, u),
                    Damage = s.Def.Weapon.Damage, Speed = s.Def.Weapon.MissileSpeedKmS, Hp = 40, Life = 60, Side = u.Side, Kind = MissileKind.Pod,
                    Owner = u.Faction, Marines = WeaponSpec.PodMarines, Source = u,
                });
            }
            Emit(FxKind.MissileLaunch, origin, origin + u.Forward, 0, u.Side, u, s.Index);
        }

        void PodHit(Missile m, BattleUnit t, DVec3 at, DVec3? shieldAt = null)
        {
            var c = t.Combat;
            var generator = c.EmitterFor(t.ShieldStrike(shieldAt ?? at, m.Vel - t.Vel));
            if (generator != null && generator.Shield > 0)
            {
                Emit(FxKind.PodBounce, at, at, t.LengthKm, t.Side, t, -1);
                return;
            }
            // The drill tip punches in; the squad goes through the hole it made.
            ApplyDamage(t, m.TargetSub != null && !m.TargetSub.Destroyed ? m.TargetSub : null, m.Damage, at, shieldAt);
            if (!t.Present) return;
            Embed(t, at, m.Vel, m.Side, false);
            var local = t.ToLocal(at);
            var space = c.Interior.Nearest(local.Z, local.Y);
            // An ally on the same side boarding it already is joined, the two fighting it as one party (U.SPC-T392); a hull
            // taken while the pod was in flight (or one a faction of another side boards) cannot be landed on: the squad
            // returns to the ship that fired it, if that is still there.
            // The squad fights as well as it drilled in the habitat it came from (U.SPC-T465).
            double drill = m.Source?.Combat?.Interior.DrillFactor ?? 1;
            if (!c.Interior.Board(m.Owner, m.Owner.MarineQuality * drill, m.Marines, space, true, Seconds, rng, m.Side))
            {
                if (m.Source != null && m.Source.Present && !m.Source.Captured) m.Source.Combat.Interior.Embark(0, m.Marines);
            }
            Emit(FxKind.Breach, at, at - m.Vel.Normalized * 0.05, t.LengthKm * 0.1, m.Side, t, space.Index);
        }

        void Embed(BattleUnit t, DVec3 at, DVec3 travel, int side, bool breacher)
        {
            var inward = travel.LengthSquared > 0 ? travel.Normalized : (t.Pos - at).Normalized;
            t.Basis(out var right, out var up);
            Embedded.Add(new Embedded
            {
                Unit = t, Local = t.ToLocal(at), Side = side, Breacher = breacher,
                Inward = new DVec3(DVec3.Dot(inward, right), DVec3.Dot(inward, up), DVec3.Dot(inward, t.Forward)),
            });
        }

        static void RemoveMarines(Interior interior, double count)
        {
            foreach (var c in interior.Compartments)
            {
                double take = Math.Min(c.Marines, count);
                c.Marines -= take;
                count -= take;
                if (count <= 0) return;
            }
        }

        /// <summary>
        /// A ship's Breacher bays (U.SPC-T466, U.SPC-T521): once the bays have cycled and the marine habitats have a whole
        /// team ready, the captain, of whatever side (a navy, raiders, a bounty hunter or a merchant's escort), fires a
        /// Breacher round at the weakened hostile hull in range that the team could take as a prize and the round could get
        /// into (its shields down to a quarter, or none). The team is drawn from the habitats' barracks, and the bays wait
        /// for the next one to be readied (<see cref="BreacherBays.IntervalSeconds"/>).
        /// </summary>
        void FireBreacherBay(BattleUnit u, double dt)
        {
            if (u.Kind != UnitKind.Ship || u.Ship == null) return;
            if (u.BreacherBayReload > 0)
            {
                u.BreacherBayReload -= dt * u.Combat.Readiness;
                return;
            }
            // Any side's captain fires the bay its hull carries (U.SPC-T521); whom it may fire at is BreacherTarget's to say.
            if (u.Fleeing || u.Combat.Disabled || BreacherBays.Count(u.Ship) <= 0) return;
            var interior = u.Combat.Interior;
            if (!BreacherBays.TeamReady(interior)) return;
            var target = BreacherTarget(u);
            if (target == null) return;

            double speed = BreacherBays.RoundSpeedKmS.Value;
            var origin = u.Pos + u.Forward * (u.Radius * 0.3);
            var aim = BreacherBays.Lead(origin, u.Vel, target.Pos, target.Vel, speed);
            var dir = aim - origin;
            if (dir.LengthSquared < 1e-18) dir = u.Forward;
            double team = MarineHabitats.Draw(interior, BreacherBays.TeamSize.Value);
            Missiles.Add(new Missile
            {
                Pos = origin, Vel = u.Vel + dir.Normalized * speed, Target = target,
                Damage = BreacherBays.ImpactDamage.Value, Speed = speed, Hp = BreacherBays.RoundHp,
                // It flies on past a miss for as long again as it takes to cross the bay's range, then goes adrift (U.SPC-T519).
                Life = BreacherBays.RangeKm.Value / speed * 2 + 5,
                Side = u.Side, Kind = MissileKind.Breacher, Owner = u.Faction, Marines = team, Source = u,
            });
            u.BreacherBayReload = BreacherBays.IntervalSeconds(u.Ship);
            Emit(FxKind.MissileLaunch, origin, origin + dir.Normalized, 0, u.Side, u, -1);
        }

        /// <summary>The hull a ship's Breacher bay would fire a round at: the nearest hostile in range that its team may take
        /// as a prize (<see cref="MayBreach"/>) and the round would get into, the most weakened favoured (a disabled one, then
        /// the one with the least of its hull left, U.SPC-T521); or null.</summary>
        BattleUnit BreacherTarget(BattleUnit u)
        {
            BattleUnit best = null;
            double bestD = BreacherBays.RangeKm.Value;
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Captured || o.Kind == UnitKind.Fighter) continue;
                var inside = o.Combat.Interior;
                if (!inside.Admits(u.Faction, u.Side)) continue;
                // A hull waiting on terms is not boarded until they open it to boarding.
                if (!MayBreach(u, o) || (o.Surrendering && !o.SurrenderOpenToBoarding)) continue;
                // A merchant's hold is asked for first, not fought through its corridors for (U.SPC-T374).
                if (o.Ship != null && Spares(u, o) && o.Ship.HasCargo && !o.SurrenderOpenToBoarding) continue;
                if (!BreacherBays.Opens(o.Combat)) continue;
                double d = (o.Pos - u.Pos).Length;
                if (o.Combat.Disabled) d *= 0.5;
                // A hull battered nearer to wreck is the readier prize (U.SPC-T521).
                d *= 0.5 + 0.5 * Math.Min(1, o.Combat.HullFraction);
                if (d < bestD)
                {
                    bestD = d;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>Whether <paramref name="u"/>'s Breacher bay may fire its team into <paramref name="target"/> to take it as a
        /// prize (U.SPC-T521): whatever its side would board (<see cref="MayBoard"/>), and, for a lawful hull of any side (a
        /// merchant's escort), a raider attacking it, which is no piracy to take.</summary>
        bool MayBreach(BattleUnit u, BattleUnit target) => MayBoard(u, target) || (target.Faction != null && target.Faction.IsPirate);

        /// <summary>
        /// A Breacher round in flight (U.SPC-T466): it coasts like a bullet, its thrusters bending its track only slightly
        /// toward where its target will be, and strikes the first hostile hull it flies through. One that strikes nothing
        /// is not lost with its team (U.SPC-T519): once it has flown its course it goes adrift, tumbling on with the
        /// marines alive inside, and coasts until point defence shoots it down or the fight ends and it is left drifting.
        /// </summary>
        void StepBreacher(Missile m, double dt)
        {
            if (m.Adrift)
            {
                m.Pos += m.Vel * dt;
                return;
            }
            if (m.Life <= 0)
            {
                GoAdrift(m);
                m.Pos += m.Vel * dt;
                return;
            }
            var t = m.Target;
            if (t != null && t.Present)
            {
                var aim = BreacherBays.Lead(m.Pos, DVec3.Zero, t.Pos, t.Vel, m.Vel.Length);
                m.Vel = BreacherBays.Correct(m.Vel, aim - m.Pos, BreacherBays.SteerKmS2.Value, dt);
            }
            var start = m.Pos;
            m.Pos += m.Vel * dt;
            BattleUnit hit = null;
            double nearest = double.MaxValue;
            foreach (var o in Units)
            {
                if (!o.Present || o.Side == m.Side || o.Kind == UnitKind.Fighter) continue;
                if (SegmentPointDistance(start, m.Pos, o.Pos) >= o.Radius * 0.6 + 0.05) continue;
                double d = (o.Pos - start).LengthSquared;
                if (d < nearest)
                {
                    nearest = d;
                    hit = o;
                }
            }
            if (hit == null) return;
            var from = start - hit.Pos;
            if (from.LengthSquared < 1e-12) from = m.Vel * -1;
            m.Gone = BreacherHit(m, hit, from.Normalized);
        }

        /// <summary>
        /// A Breacher round goes adrift (U.SPC-T519): its thrusters give up the chase and it tumbles on along its line, a
        /// derelict with its team alive inside. It no longer homes on anything or drills into anything.
        /// </summary>
        static void GoAdrift(Missile m)
        {
            m.Adrift = true;
            m.Target = null;
            m.TargetSub = null;
        }

        /// <summary>
        /// A Breacher round is shot out of space (U.SPC-T519): the team inside dies with it.
        /// </summary>
        static void RoundLost(Missile m)
        {
            if (m.Kind != MissileKind.Breacher || m.Marines < 0.5) return;
            Crews.RecordDeaths(m.Owner, m.Marines);
            m.Marines = 0;
        }

        /// <summary>
        /// A Breacher round strikes <paramref name="t"/> from direction <paramref name="from"/> (U.SPC-T466); true if it is
        /// spent (drilled in, or the hull it struck died). A shield holding more than a quarter of its strength turns it aside:
        /// it glances off and tumbles away adrift, its team still alive inside (U.SPC-T519, <see cref="GoAdrift"/>). Otherwise its drill
        /// bores in, it stays stuck in the plating, and its team storms out into the compartment it struck, where the
        /// boarding fight takes over and the hull's own marines try to stop them. If the hull is no longer theirs to take
        /// (struck its colours on terms that do not open it, already held, or boarded by another faction), the team stays
        /// in the round and is taken off by the ship that fired it.
        /// </summary>
        bool BreacherHit(Missile m, BattleUnit t, DVec3 from)
        {
            var c = t.Combat;
            var entry = t.Pos + from * t.Radius;
            var contact = t.Pos + from * (t.Radius * 0.3);
            if (!BreacherBays.Opens(c))
            {
                Emit(FxKind.PodBounce, entry, entry, t.LengthKm, t.Side, t, -1);
                // It glances off the shield and careens away, tumbling (U.SPC-T519).
                m.Vel = t.Vel + DriftingRounds.Glance(m.Vel - t.Vel, from);
                m.Pos = entry + from * 0.05;
                GoAdrift(m);
                return false;
            }
            ApplyDamage(t, PickSubsystem(t), m.Damage, contact, entry);
            if (!t.Present) return true;
            Embed(t, contact, m.Vel - t.Vel, m.Side, true);
            var local = t.ToLocal(contact);
            var space = c.Interior.Nearest(local.Z, local.Y);
            bool spared = t.Surrendering && !t.SurrenderOpenToBoarding;
            // The team fights as well as it drilled in the habitat it came from (U.SPC-T465), with the shock of the breach.
            double drill = m.Source?.Combat?.Interior.DrillFactor ?? 1;
            if (spared || !c.Interior.Board(m.Owner, m.Owner.MarineQuality * BreacherBays.ShockEdge * drill, m.Marines, space, true, Seconds, rng, m.Side))
            {
                if (m.Source != null && m.Source.Present && !m.Source.Captured) m.Source.Combat.Interior.Embark(0, m.Marines);
            }
            Emit(FxKind.Breach, contact, contact + from * 0.1, t.LengthKm * 0.15, m.Side, t, space.Index);
            return true;
        }

        /// <summary>
        /// A ship with marines to spare picks a disabled enemy to board, once no healthy enemy warship
        /// is close enough to make it suicide.
        /// </summary>
        void ChooseBoarding(BattleUnit u)
        {
            if (u.BoardTarget != null)
            {
                var bt = u.BoardTarget;
                // A hull waiting on terms is not boarded until they open it to boarding (as it is not fired on, pods included).
                bool stillOpen = bt.Present && !bt.Captured && (!bt.Surrendering || bt.SurrenderOpenToBoarding) &&
                    (bt.Combat.Disabled || bt.SurrenderOpenToBoarding || bt.Stunned > 10);
                if (!stillOpen) u.BoardTarget = null;
            }
            // A boarder that can no longer fly the approach (crippled, or breaking off) gives up the
            // prize, or the battle would wait on a boarding that never comes.
            if (u.Fleeing || u.Combat.Disabled) { u.BoardTarget = null; return; }
            // A carrier with boarding shuttles sends them, not its own hull (U.SPC-T486).
            if (BoardsByShuttle(u)) { u.BoardTarget = null; return; }
            if (u.BoardTarget != null) return;
            bool boarder = u.Faction.IsPirate || u.Faction.IsMilitary || (Boards != null && Boards(u));
            if (!boarder || u.Combat.MarinesAvailable < 15) return;
            BattleUnit best = null;
            double bestD = double.MaxValue;
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Captured || o.Kind != UnitKind.Ship) continue;
                if (!o.Combat.Interior.Admits(u.Faction, u.Side)) continue;
                if (!MayBoard(u, o) || (o.Surrendering && !o.SurrenderOpenToBoarding)) continue;
                // A merchant's hold is asked for, not fought through its corridors for (U.SPC-T374): raiders board a hull
                // with cargo aboard only once it has turned their demand down.
                if (Spares(u, o) && o.Ship.HasCargo && !o.SurrenderOpenToBoarding) continue;
                bool open = o.Combat.Disabled || o.SurrenderOpenToBoarding || o.Stunned > 10;
                if (!open) continue;
                if (ThreatNear(u, o.Pos, 6)) continue;
                double d = (o.Pos - u.Pos).Length;
                if (d < bestD)
                {
                    bestD = d;
                    best = o;
                }
            }
            u.BoardTarget = best;
        }

        bool ThreatNear(BattleUnit u, DVec3 at, double km)
        {
            foreach (var o in Units)
                if (Hostile(u, o) && !o.Captured && !o.Surrendering && o.Kind != UnitKind.Fighter && o.Combat.Armed && !o.Combat.Disabled && (o.Pos - at).Length < km)
                    return true;
            return false;
        }

        /// <summary>
        /// Closing to board. A boarding ship lines up alongside, matches the target's drift, latches
        /// and after a while opens its airlock. (A Breacher round is fired, not flown: <see cref="FireBreacherBay"/>.)
        /// </summary>
        void Approach(BattleUnit u, double dt)
        {
            var t = u.BoardTarget;
            var c = u.Combat;
            double maxSpeed = c.Spec.MaxSpeed * (0.25 + 0.75 * c.EngineFactor);
            var dock = u.DockPoint(t, 0.06);
            var to = dock - u.Pos;
            double dist = to.Length;
            // Arrive matched: slow down as it closes, holding the target's own drift.
            double want = Math.Min(maxSpeed, dist * 0.05 + 0.002);
            var desired = t.Vel + (dist > 1e-6 ? to / dist : DVec3.Zero) * want;
            var dv = desired - u.Vel;
            double step = c.Spec.Accel * (0.3 + 0.7 * c.EngineFactor) * dt * 1.5;
            u.Vel += dv.Length > step ? dv.Normalized * step : dv;
            u.Pos += u.Vel * dt;
            TurnToward(u, t.Forward, c.Spec.TurnRateDegS, dt);

            if (dist < 0.08 && (u.Vel - t.Vel).Length < 0.01)
            {
                u.LatchTimer += dt;
                if (u.LatchTimer > 20)
                {
                    u.Latched = t;
                    u.LatchLocal = t.ToLocal(u.Pos);
                    u.BoardTarget = null;
                    u.LatchTimer = 0;
                    if (t.Combat.Interior.Admits(u.Faction, u.Side))
                    {
                        double marines = c.MarinesAvailable * 0.85;
                        RemoveMarines(c.Interior, marines);
                        var airlock = t.Combat.Interior.Of(CompartmentKind.Midships) ?? t.Combat.Interior.Nearest(0, 0);
                        t.Combat.Interior.Board(u.Faction, u.Faction.MarineQuality * c.Interior.DrillFactor, marines, airlock, false, Seconds, rng, u.Side);
                        Emit(FxKind.Latch, u.Pos, t.Pos, u.LengthKm, u.Side, t, airlock.Index);
                    }
                }
            }
            else u.LatchTimer = 0;
        }

        /// <summary>Runs every fight aboard; releases boarding ships when it is decided.</summary>
        void StepBoarding(double dt)
        {
            foreach (var u in Units)
            {
                if (!u.Present || u.Captured) continue;
                var interior = u.Combat.Interior;
                if (interior.Boarder == null) continue;
                var boarder = interior.Boarder;
                if (interior.Commanded) KeepOrdersCommanded(u, interior);
                if (u.SurrenderOpenToBoarding) Capture(u, boarder);
                else if (interior.Step(dt, Seconds, rng)) Capture(u, boarder);
            }
            foreach (var u in Units)
            {
                if (u.Latched == null) continue;
                var t = u.Latched;
                if (!t.Present || t.Captured || t.Combat.Interior.Boarder == null)
                {
                    // Done (taken, repelled or gone): cast off.
                    u.Latched = null;
                    u.Vel += (u.Pos - t.Pos).Normalized * 0.01;
                }
            }
        }

        /// <summary>
        /// A player's boarding orders (U.SPC-T468) stand only while a player is there to command that side: the hull's
        /// own helm for its defenders, a hull of the boarding faction for the boarders. Once the player has gone (handed
        /// the helm back, left the fight) that side is the resolver's again: the auto-resolve.
        /// </summary>
        void KeepOrdersCommanded(BattleUnit u, Interior interior)
        {
            if (interior.DefenderOrder != BoardingOrder.Auto && !(u.PlayerControlled && u.Present))
                interior.AutoResolve(BoardingRole.Defenders);
            if (interior.BoarderOrder == BoardingOrder.Auto) return;
            foreach (var o in Units)
                if (o.PlayerControlled && o.Present && !o.Captured && interior.IsParty(o.Faction)) return;
            interior.AutoResolve(BoardingRole.Boarders);
        }

        void Capture(BattleUnit u, Faction captor)
        {
            int side = -1;
            foreach (var o in Units)
                if (o.Faction == captor && o.Side != u.Side) side = o.Side;
            if (side < 0) side = 1 - u.Side;
            if (!u.Captured) u.TakenFromSide = u.Side;
            u.Captured = true;
            u.Side = side;
            u.Fleeing = false;
            u.Target = null;
            u.BoardTarget = null;
            // Taken while still hailing: there are no terms left to settle.
            u.Surrendering = false;
            // Its escorts no longer keep station on it (it fights for the other side now), nor it on its old flagship.
            u.Leader = null;
            foreach (var o in Units)
            {
                if (o.Leader == u) o.Leader = null;
                // Nobody keeps steering on it as it was: its captors' side re-picks now rather than circling its own prize.
                if (o.Target == u) o.Target = null;
            }
            // A surrendered hull is taken without a fight aboard: mark it held so the fight stops there too.
            u.Combat.Interior.Captured = true;
            u.Combat.Interior.Settle();
            Emit(FxKind.Captured, u.Pos, u.Pos, u.LengthKm, side, u, -1);
            UnitCaptured?.Invoke(this, u, captor);
        }

        // ---------------------------------------------------------------- surrender

        /// <summary>A hull has struck its colours and nobody has settled its terms yet.</summary>
        bool AwaitingSurrenderTerms
        {
            get
            {
                foreach (var u in Units)
                    if (u.Present && u.Surrendering && u.SurrenderOutcome == null) return true;
                return false;
            }
        }

        void TallySideStrength()
        {
            sideStrength[0] = sideStrength[1] = 0;
            foreach (var u in Units)
                if (u.Present && !u.Captured && !u.Surrendering && u.Side >= 0 && u.Side < 2)
                    sideStrength[u.Side] += u.Combat.Strength;
        }

        double OutgunnedOf(BattleUnit u) =>
            u.Side < 0 || u.Side > 1 ? 0 : Morale.Outgunned(sideStrength[u.Side], sideStrength[1 - u.Side]);

        static double DamageTollOf(BattleUnit u)
        {
            var c = u.Combat;
            double shield = c.Spec.Shield > 0 ? c.Shield / c.Spec.Shield : 1;
            return Morale.DamageToll(c.HullFraction, shield);
        }

        bool StrikesColours(BattleUnit u) =>
            Morale.StrikesColours(DamageTollOf(u), OutgunnedOf(u),
                Morale.Resolve(u.Faction, u.Ship != null && u.Ship.Spec.IsMilitary) * (u.Side >= 0 && u.Side < 2 ? Dread[u.Side] : 1) * Morale.SupplyResolve(u.Combat.Readiness),
                u.MoraleThreshold);

        /// <summary>
        /// Strikes <paramref name="u"/>'s colours: it breaks off, powers down its weapons and hails,
        /// and is no longer shot at while it waits on its captor's terms.
        /// </summary>
        public void Surrender(BattleUnit u)
        {
            if (u.Surrendering || !u.Present || u.Captured) return;
            u.Surrendering = true;
            u.SurrenderTimer = 0;
            u.Fleeing = false;
            u.Target = null;
            u.BoardTarget = null;
            foreach (var o in Units)
            {
                if (o.Target == u) o.Target = null;
                if (o.BoardTarget == u && !u.SurrenderOpenToBoarding) o.BoardTarget = null;
            }
            Emit(FxKind.Hail, u.Pos, u.Pos, u.LengthKm, u.Side, u, -1);
            UnitSurrendered?.Invoke(this, u);
        }

        /// <summary>The faction a hull on <paramref name="side"/> surrenders to: the other side's leader.</summary>
        public Faction CaptorOf(int side) => side < 0 || side > 1 ? null : SideLeaders[1 - side];

        /// <summary>True when someone on the side facing <paramref name="u"/> could board it.</summary>
        bool CaptorCanBoard(BattleUnit u)
        {
            foreach (var o in Units)
                // A player-flown hull never flies a boarding approach (see Think), so it can't be the one to take it.
                if (Hostile(u, o) && !o.Captured && !o.Surrendering && !o.Combat.Disabled && !o.PlayerControlled && CanBoard(o) && MayBoard(o, u)) return true;
            return false;
        }

        bool CaptorPlayerPresent(BattleUnit u)
        {
            foreach (var o in Units)
                if (Hostile(u, o) && o.PlayerControlled) return true;
            return false;
        }

        /// <summary>The first hull on the side facing <paramref name="captorSide"/> still waiting for terms, or null.</summary>
        public BattleUnit AwaitingTermsFrom(int captorSide)
        {
            foreach (var u in Units)
                if (u.Present && u.Surrendering && u.SurrenderOutcome == null && u.Side != captorSide) return u;
            return null;
        }

        /// <summary>
        /// The captor's answer to a hull that has struck its colours: hand over the cargo and go, be
        /// boarded and taken, or simply go. Taking it needs someone able to board; with nobody, it is
        /// refused. False when <paramref name="u"/> isn't waiting on terms.
        /// </summary>
        public bool OfferSurrenderTerms(BattleUnit u, SurrenderTerms terms)
        {
            if (u == null || !u.Present || !u.Surrendering || u.SurrenderOutcome != null) return false;
            if (terms == SurrenderTerms.TakeShip && !CaptorCanBoard(u)) return false;
            Settle(u, CaptorOf(u.Side), terms);
            return true;
        }

        void Settle(BattleUnit u, Faction captor, SurrenderTerms terms)
        {
            u.SurrenderOutcome = terms;
            if (terms == SurrenderTerms.TakeShip) u.SurrenderOpenToBoarding = true;
            if (terms == SurrenderTerms.RansomCargo && captor != null && captor.IsPirate && (u.Side == 0 || u.Side == 1)) Robbed[1 - u.Side] = true;
            SurrenderDecided?.Invoke(this, u, captor, terms);
            // Released: the hull powers up and leaves the field.
            if (terms != SurrenderTerms.TakeShip) u.Fled = true;
        }

        void StepSurrenders(double dt)
        {
            foreach (var u in Units)
            {
                if (!u.Present) continue;
                if (u.CargoDemandedBy != null)
                {
                    u.CargoDemandTimer += dt;
                    if (u.CargoDemandTimer > SurrenderDecisionSeconds) AnswerCargoDemand(u, false);
                }
                if (!u.Surrendering || u.SurrenderOutcome != null) continue;
                u.SurrenderTimer += dt;
                double wait = CaptorPlayerPresent(u) ? SurrenderDecisionSeconds : SurrenderAiDecisionSeconds;
                if (u.SurrenderTimer < wait) continue;
                var captor = CaptorOf(u.Side);
                if (captor == null) { Settle(u, null, SurrenderTerms.LetGo); continue; }
                bool warship = u.Ship != null && u.Ship.Spec.IsMilitary;
                bool cargo = u.Ship != null && u.Ship.HasCargo;
                Settle(u, captor, Morale.ChooseTerms(captor.IsPirate, warship, cargo, CaptorCanBoard(u), rng.NextDouble()));
            }
        }

        /// <summary>
        /// Pirates with the upper hand hail a hull that carries cargo: jettison it and go, or fight. Asked once a battle.
        /// A player-flown hull answers through <see cref="AnswerCargoDemand"/>; an AI merchant or civilian hull is asked
        /// once the raiders have done what their doctrine says to first (U.SPC-T374, <see cref="RaidDoctrine"/>): left it
        /// dead in space or knocked out by a pulse, or stripped its shields while they have it outgunned. Its crew are paid
        /// to deliver, not to die, so it complies, and the raiders' drones collect what it jettisons.
        /// </summary>
        void MaybeDemandCargo(BattleUnit pirate)
        {
            foreach (var v in Units)
            {
                if (!Hostile(pirate, v) || v.CargoDemandMade || v.Surrendering || !v.Present || v.Captured) continue;
                if (v.Ship == null || !v.Ship.HasCargo) continue;
                if (v.PlayerControlled)
                {
                    if (!v.Combat.Disabled && !Morale.ShouldDemandCargo(OutgunnedOf(v), DamageTollOf(v))) continue;
                }
                else if (!RaidDoctrine.Robs(pirate.Faction, v.Ship) || !Stripped(v)) continue;
                v.CargoDemandMade = true;
                v.CargoDemandedBy = pirate.Faction;
                v.CargoDemandTimer = 0;
                Emit(FxKind.Hail, pirate.Pos, v.Pos, pirate.LengthKm, pirate.Side, pirate, -1);
                CargoDemanded?.Invoke(this, v, pirate.Faction);
                if (!v.PlayerControlled) AnswerCargoDemand(v, true);
            }
        }

        /// <summary>Whether raiders have done to <paramref name="v"/> what their doctrine says to before they demand its
        /// cargo: left it dead in space or stunned by a pulse, or stripped its shields while they have it outgunned.</summary>
        bool Stripped(BattleUnit v)
        {
            var c = v.Combat;
            if (c.Disabled || v.Stunned > 0) return true;
            bool shieldsDown = c.Spec.Shield <= 0 || c.Shield <= c.Spec.Shield * 0.05;
            return shieldsDown && OutgunnedOf(v) >= 0.3;
        }

        /// <summary>
        /// The answer to a pirate cargo demand: comply and the cargo is jettisoned for the raiders' drones to collect and the
        /// hull goes free (it leaves the field); refuse and the fight goes on, and the raiders no longer spare it. False when
        /// nothing was asked.
        /// </summary>
        public bool AnswerCargoDemand(BattleUnit victim, bool comply)
        {
            if (victim == null || victim.CargoDemandedBy == null) return false;
            var pirates = victim.CargoDemandedBy;
            victim.CargoDemandedBy = null;
            if (!comply) victim.CargoRefused = true;
            if (!comply || !victim.Present) return true;
            victim.SurrenderOutcome = SurrenderTerms.RansomCargo;
            if (victim.Side == 0 || victim.Side == 1) Robbed[1 - victim.Side] = true;
            SurrenderDecided?.Invoke(this, victim, pirates, SurrenderTerms.RansomCargo);
            victim.Fled = true;
            return true;
        }

        /// <summary>Whether each side has robbed a hull of its cargo in this fight (U.SPC-T374): raiders who have, with nothing
        /// left worth taking, withdraw (see <see cref="RaidDone"/>).</summary>
        public readonly bool[] Robbed = new bool[2];

        /// <summary>Whether <paramref name="u"/>, an AI raider, means to rob <paramref name="o"/> rather than destroy it: a
        /// merchant or civilian hull that hasn't turned its demand down (see <see cref="RaidDoctrine"/>).</summary>
        bool Spares(BattleUnit u, BattleUnit o) =>
            !u.PlayerControlled && !o.CargoRefused && RaidDoctrine.Robs(u.Faction, o.Ship);

        /// <summary>A raider holds its fire on a hull it is robbing once that lies dead in space.</summary>
        bool HoldsFireOn(BattleUnit u, BattleUnit o) => o.Combat.Disabled && Spares(u, o);

        /// <summary>
        /// Whether raider <paramref name="u"/>'s work here is done (U.SPC-T374): no merchant or civilian hull is left that it
        /// could still rob (one with cargo it hasn't handed over, one that turned the demand down, one waiting on terms, one
        /// its side is boarding or has marines to board), and either its side has robbed someone or there is nobody armed left to fight. Then it
        /// withdraws, rather than trade fire with escorts for nothing.
        /// </summary>
        bool RaidDone(BattleUnit u)
        {
            if (u.Side != 0 && u.Side != 1) return false;
            bool armedFoe = false, prey = false;
            foreach (var o in Units)
            {
                if (!o.Present || o.Captured || !Hostile(u, o)) continue;
                if (o.Ship == null || !RaidDoctrine.Robs(u.Faction, o.Ship))
                {
                    if (!o.Surrendering && !o.Combat.Disabled && (o.Combat.Armed || CanBoard(o))) armedFoe = true;
                    continue;
                }
                prey = true;
                bool stillToTake = (o.Ship.HasCargo && o.SurrenderOutcome == null) || o.CargoRefused || IsPrize(u, o) ||
                    (o.Surrendering && o.SurrenderOutcome == null) || o.SurrenderOpenToBoarding || SideCanBoard(u.Side);
                if (stillToTake) return false;
            }
            if (!prey && !Robbed[u.Side]) return false;
            return Robbed[u.Side] || !armedFoe;
        }

        static double SegmentPointDistance(DVec3 a, DVec3 b, DVec3 p)
        {
            var d = b - a;
            double len2 = d.LengthSquared;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, DVec3.Dot(p - a, d) / len2)) : 0;
            return (a + d * t - p).Length;
        }

        public void Emit(FxKind kind, DVec3 a, DVec3 b, double size, int side, BattleUnit unit, int sub)
        {
            Fx.Add(new CombatFx { Seq = ++FxSeq, Kind = kind, A = a, B = b, Size = size, Side = side, Unit = unit, Sub = sub });
        }

        // ---------------------------------------------------------------- placement

        /// <summary>
        /// How far out from <paramref name="body"/> a battle is fought: high enough that its tens of km,
        /// drawn at ship scale in the gameplay view, stay well clear of the inflated world.
        /// </summary>
        public static double AnchorDistanceKm(BodyDefinition body)
        {
            double displayRadius = ScaleModel.GameplayRadius(body.RadiusKm);
            double wanted = displayRadius + 900;
            double lo = body.RadiusKm, hi = body.RadiusKm * 400;
            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (ScaleModel.GameplayLocalDistance(body, mid) < wanted) lo = mid;
                else hi = mid;
            }
            return Math.Max(hi, body.RadiusKm * 6);
        }
    }
}
