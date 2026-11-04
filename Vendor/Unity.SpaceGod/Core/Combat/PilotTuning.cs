using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// How a hull handles under a human's hands, independent of <see cref="CombatSpec"/>'s AI-battle
    /// balance numbers -- a Freighter tuned to feel fine as a slow AI hauler (three degrees a second)
    /// is not a Freighter that's fun to actually fly. Keyed by <see cref="ShipClass"/> rather than by
    /// design, since the arcade feel is a class-wide call, not a per-hull balance knob.
    ///
    /// Since U.SPC-T59 the hull's push and spin come from its own design (mass, thrust, torque and
    /// inertia on <see cref="CombatSpec"/>, see <see cref="HullDynamics"/>); what is left here is what
    /// the flight computer does with them: the speeds and turn rates flight assist holds the hull to,
    /// and the boost and cruise drive tiers.
    /// </summary>
    public readonly struct PilotTuning
    {
        /// <summary>The speed flight assist flies the hull at with the throttle floored, km/s: its
        /// set point, not a hard limit -- with assist off nothing caps the speed a hull can burn up to.</summary>
        public readonly double MaxSpeed;

        /// <summary>The fastest the attitude computer swings the nose (pitch/yaw), deg/s; how quickly
        /// it gets there is the hull's own torque over its inertia.</summary>
        public readonly double TurnRateDegS;

        /// <summary>Boost lights an afterburner: main-drive thrust times this while the charge lasts
        /// (draining over <see cref="BoostDurationS"/>, recharging otherwise), and flight assist's set
        /// point rises by the same factor while it burns.</summary>
        public readonly double BoostSpeedMultiplier, BoostDurationS;

        /// <summary>The cruise drive (see <see cref="PilotCruiseState"/>) spools main-drive thrust up
        /// to this many times normal, and flight assist's set point with it -- deliberately far above
        /// anything boost reaches, since cruise exists to cover open-space distances that would
        /// otherwise take real-world days at tactical speeds, not to win a fight.</summary>
        public readonly double CruiseSpeedMultiplier;

        /// <summary>Seconds of spooling up from rest to full cruise thrust once engaged.</summary>
        public readonly double CruiseRampS;

        /// <summary>The fastest flight assist lets the hull roll, deg/s (a multiple of the turn rate:
        /// a long hull rolls far more readily than it turns, see <see cref="CombatSpec.RollInertia"/>).</summary>
        public double RollRateDegS => TurnRateDegS * RollRateFactor;

        public const double RollRateFactor = 1.5;

        public PilotTuning(double maxSpeed, double turnRateDegS, double boostSpeedMultiplier, double boostDurationS, double cruiseSpeedMultiplier, double cruiseRampS)
        {
            MaxSpeed = maxSpeed;
            TurnRateDegS = turnRateDegS;
            BoostSpeedMultiplier = boostSpeedMultiplier;
            BoostDurationS = boostDurationS;
            CruiseSpeedMultiplier = cruiseSpeedMultiplier;
            CruiseRampS = cruiseRampS;
        }

        static readonly Dictionary<ShipClass, PilotTuning> ByClass = new Dictionary<ShipClass, PilotTuning>
        {
            // The Courier (U.SPC-T444): a blockade runner, not a dogfighter. The highest top speed and the
            // longest boost in the game and the fastest cruise spool, on a turn wider than any strike craft's.
            [ShipClass.Courier] = new PilotTuning(0.52, 120, 2.4, 45, 10, 1.2),
            // Star fighters: Everspace 2-paced, a full about-face in a second or so. Cruise spools up
            // fast too -- small, nimble hulls should feel quick to get going in open space.
            // The heavy fighter (U.SPC-T467): a light fighter's pace less a little, stiffer in the turn, a longer boost.
            [ShipClass.Gunship] = new PilotTuning(0.4, 115, 2.0, 20, 9, 2.2),
            // The space superiority fighter, the dedicated dogfighter: trades a hair of the Courier's top
            // speed for a turn only the interceptor beats.
            [ShipClass.Fighter] = new PilotTuning(0.42, 165, 2.3, 15, 9, 1.8),
            // The interceptor (U.SPC-T484): the fastest strike craft and the tightest turn in the game, on a
            // short boost -- it runs fighters down. The bomber: slow, steady, a short boost to get clear.
            [ShipClass.Interceptor] = new PilotTuning(0.48, 180, 2.4, 12, 9, 1.6),
            [ShipClass.Bomber] = new PilotTuning(0.3, 85, 1.6, 12, 8, 2.4),
            // The recon fighter (U.SPC-T485): an interceptor's pace on a longer boost, a little wider in the turn for its pods.
            [ShipClass.Recon] = new PilotTuning(0.46, 160, 2.4, 16, 9, 1.6),
            // The boarding shuttle (U.SPC-T486): slower and heavier on the stick than a bomber, built to come alongside gently.
            [ShipClass.AssaultShuttle] = new PilotTuning(0.24, 60, 1.4, 10, 8, 2.6),
            // A truss freighter (U.SPC-T482): a weak drive for its mass, so its cruise drive takes twice a hauler's time to spool.
            [ShipClass.Freighter] = new PilotTuning(0.18, 75, 1.6, 36, 8, 6.0),
            [ShipClass.Miner] = new PilotTuning(0.16, 70, 1.6, 38, 8, 3.0),
            // The deep-space explorer (U.SPC-T419): handier than a hauler, nothing like a fighter.
            [ShipClass.Explorer] = new PilotTuning(0.22, 60, 1.6, 26, 8, 3.0),
            // The ore tug (U.SPC-T483): flown like a hauler, a touch heavier on the stick.
            [ShipClass.OreTug] = new PilotTuning(0.14, 60, 1.5, 40, 8, 3.2),
            // The capture tug (U.SPC-T522): an ore tug's controls on a lighter frame, handier for matching a tumbling rock.
            [ShipClass.CaptureTug] = new PilotTuning(0.18, 65, 1.5, 34, 8, 3.0),
            // The lane tender (U.SPC-T481): a work boat, steadier than an explorer and slower to spool.
            [ShipClass.LaneTender] = new PilotTuning(0.18, 55, 1.5, 30, 7, 3.5),
            // The gate picket (U.SPC-T480): a small hull, flown nose-first like a heavy fighter but no dogfighter.
            [ShipClass.GatePicket] = new PilotTuning(0.3, 80, 1.8, 22, 8, 2.6),

            // Capital ships: still weighty -- you're captaining a warship, not dogfighting in it --
            // but sharper than their AI-battle turn rates so they're not sluggish to the point of
            // being unplayable. Their huge pitch/yaw inertia is what sells the weight: the nose takes
            // real time to start swinging. Cruise ramps take longest here.
            [ShipClass.Carrier] = new PilotTuning(0.07, 8, 1.3, 30, 5, 5.0),
            [ShipClass.Battleship] = new PilotTuning(0.09, 10, 1.3, 32, 5, 4.5),
            [ShipClass.Cruiser] = new PilotTuning(0.14, 16, 1.4, 28, 6, 4.0),
            // Destroyer stays the nose-forward skirmisher (see Battle.BroadsideFacing).
            [ShipClass.Destroyer] = new PilotTuning(0.24, 28, 1.5, 25, 7, 3.5),
            // The command cruiser (U.SPC-T477): a battlecruiser, between the cruiser and the battleship at the helm.
            [ShipClass.CommandCruiser] = new PilotTuning(0.12, 14, 1.35, 30, 5.5, 4.5),
            // The frigate (U.SPC-T475): the fastest warship hull, quick to flip end for end, with a long, hard boost for its runs.
            [ShipClass.Frigate] = new PilotTuning(0.38, 40, 1.6, 30, 8, 3.0),
            // The corvette (U.SPC-T476): a patrol hull, handier than a destroyer, for running down a smuggler in its system.
            [ShipClass.Corvette] = new PilotTuning(0.3, 34, 1.5, 26, 7.5, 3.2),
            // The gate breaker (U.SPC-T478): a battleship's helm under more plate, slower and stiffer still.
            [ShipClass.GateBreaker] = new PilotTuning(0.08, 9, 1.25, 30, 4.5, 4.5),
        };

        public static PilotTuning Of(ShipClass c) =>
            ByClass.TryGetValue(c, out var t) ? t : new PilotTuning(0.1, 20, 1.3, 20, 6, 3.0);

        /// <summary>How a hull of class <paramref name="c"/> is flown at the helm (U.SPC-T269).</summary>
        public static PilotControl ControlOf(ShipClass c)
        {
            switch (c)
            {
                case ShipClass.Fighter:
                case ShipClass.Interceptor:
                case ShipClass.Recon:
                case ShipClass.Courier:
                    return PilotControl.FreeAim;
                case ShipClass.Gunship:
                case ShipClass.Bomber:
                case ShipClass.GatePicket:
                    return PilotControl.DirectNose;
                case ShipClass.Freighter:
                case ShipClass.Miner:
                case ShipClass.OreTug:
                case ShipClass.CaptureTug:
                case ShipClass.LaneTender:
                case ShipClass.Explorer:
                    return PilotControl.Hauler;
                default:
                    return PilotControl.Helm;
            }
        }
    }

    /// <summary>
    /// The way a hull is flown at the helm, one per kind of craft (U.SPC-T269): see <see cref="PilotAim"/>.
    /// </summary>
    public enum PilotControl
    {
        /// <summary>Light fighters, Star Conflict style: the mouse swings the camera and crosshair freely
        /// and the hull turns after them at its own rate.</summary>
        FreeAim,

        /// <summary>Heavy fighters, Everspace 2 style: the mouse turns the hull itself and the nose is the
        /// crosshair; the aim never leads it by more than the hull can brake its swing in.</summary>
        DirectNose,

        /// <summary>Freighters and miners: flown like <see cref="DirectNose"/>, with the hull's weight and
        /// a slow mouse making it a heavy, deliberate thing to manoeuvre.</summary>
        Hauler,

        /// <summary>Capital ships, naval style: the keys turn the hull, the mouse looks around it freely,
        /// and the turrets fight on their own.</summary>
        Helm,
    }
}
