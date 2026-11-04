using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// What a trip is expected to take, for a trader pricing it: days under way (queues and landing
    /// included), the delta-v the ship's own drive must supply (the burns to, between and from the
    /// hyperlane ramps, or the one burn of a local hop) and how many lanes it rides (each one tolled).
    /// </summary>
    public readonly struct TripEstimate
    {
        public readonly double Days;
        public readonly double DeltaVKmS;
        public readonly int LaneHops;

        public TripEstimate(double days, double deltaVKmS, int laneHops)
        {
            Days = days;
            DeltaVKmS = deltaVKmS;
            LaneHops = laneHops;
        }
    }

    /// <summary>
    /// Reaction mass. Every hull carries a tank of propellant (its design's <see cref="CombatSpec.PropellantTonnes"/>)
    /// and a fusion drive that throws it out at <see cref="CombatSpec.ExhaustVelocityKmS"/>: the rocket
    /// equation (Tsiolkovsky) then fixes what every manoeuvre costs, heavier for a full hold than an empty
    /// one. Reaction mass is water or hydrocarbons (whichever the port sells cheaper), heated by a D–He-3
    /// burn that takes <see cref="HeliumThreePerTonne"/> of helium-3 with every tonne. Hyperlane rides cost
    /// no propellant (the lane carries the ship) but are tolled.
    /// </summary>
    public static class Propulsion
    {
        /// <summary>Helium-3 the fusion burn takes per tonne of reaction mass thrown out (0.5 kg).</summary>
        public const double HeliumThreePerTonne = 0.0005;

        /// <summary>A planner keeps this much more than a trip needs in the tanks before it sets out.</summary>
        public const double PlanningReserve = 0.25;

        /// <summary>A ship tops up when its tanks are below this share of full (not for every last tonne).</summary>
        public const double TopUpBelow = 0.97;

        /// <summary>The goods a drive can use as reaction mass.</summary>
        public static readonly Good[] ReactionMass = { Good.Water, Good.Hydrocarbons };

        /// <summary>Days an emergency tanker takes to reach a ship that has run dry (plus up to as much again).</summary>
        public const double TankerDays = 1.5;

        /// <summary>m0 / mf for a burn of <paramref name="deltaVKmS"/> at exhaust velocity <paramref name="exhaustKmS"/>.</summary>
        public static double MassRatio(double deltaVKmS, double exhaustKmS) => Math.Exp(Math.Max(0, deltaVKmS) / exhaustKmS);

        /// <summary>The delta-v (km/s) a drive gets from burning a ship of <paramref name="wetTonnes"/> down to <paramref name="dryTonnes"/>.</summary>
        public static double DeltaV(double exhaustKmS, double wetTonnes, double dryTonnes) =>
            dryTonnes <= 0 || wetTonnes <= dryTonnes ? 0 : exhaustKmS * Math.Log(wetTonnes / dryTonnes);

        /// <summary>Propellant (t) a burn of <paramref name="deltaVKmS"/> throws out of a ship that starts it at <paramref name="startTonnes"/>.</summary>
        public static double PropellantBurned(double deltaVKmS, double exhaustKmS, double startTonnes) =>
            startTonnes * (1 - 1 / MassRatio(deltaVKmS, exhaustKmS));

        /// <summary>
        /// The delta-v (km/s) of a flip-and-burn over <paramref name="distanceKm"/> from rest to rest at a
        /// constant <paramref name="accelKmS2"/>: accelerate half way, brake the other half, 2·√(a·d).
        /// </summary>
        public static double BrachistochroneDeltaV(double accelKmS2, double distanceKm) => 2 * Math.Sqrt(Math.Max(0, accelKmS2 * distanceKm));

        /// <summary>
        /// The delta-v (km/s) of a planned transit: the flip-and-burn (its acceleration for the whole flight)
        /// plus the steady push that takes the ship from its departure velocity to its arrival velocity and
        /// what holding its line against gravity costs; or, on a transfer orbit, its escape and capture burns.
        /// </summary>
        public static double TransitDeltaV(Ship s)
        {
            if (s.Conic != null) return s.Conic.DeltaVKmS;
            double days = s.PhaseEndJd - s.PhaseStartJd;
            if (days <= 0) return 0;
            return (s.AccelKmPerDay2 * days + (s.ArrivalVelocity - s.DepartureVelocity).Length) / Astro.SecondsPerDay + s.GravityLossKmS;
        }

        /// <summary>Propellant (t) <paramref name="ship"/> as it is now (hold and tanks) burns for <paramref name="deltaVKmS"/>.</summary>
        public static double PropellantFor(Ship ship, double deltaVKmS) => PropellantBurned(deltaVKmS, ship.ExhaustVelocityKmS, ship.MassTonnes);

        /// <summary>
        /// Propellant (t) a trip of <paramref name="deltaVKmS"/> takes with <paramref name="cargoTonnes"/> aboard,
        /// setting out with <paramref name="propellantTonnes"/> in the tanks.
        /// </summary>
        public static double PropellantFor(Ship ship, double deltaVKmS, double cargoTonnes, double propellantTonnes) =>
            PropellantBurned(deltaVKmS, ship.ExhaustVelocityKmS, ship.DryMassTonnes + cargoTonnes + propellantTonnes);

        /// <summary>
        /// Flies a manoeuvre on the ship's own tanks: burns what the change of velocity
        /// <paramref name="deltaVKmS"/> costs, or as much of it as is left. Returns the share of the
        /// manoeuvre the propellant covered (1 for all of it, 0 for a dry ship).
        /// </summary>
        public static double Spend(Ship ship, double deltaVKmS)
        {
            if (deltaVKmS <= 0) return 1;
            double need = PropellantFor(ship, deltaVKmS);
            if (need <= ship.Propellant)
            {
                ship.Propellant -= need;
                return 1;
            }
            double have = ship.Propellant;
            double reached = DeltaV(ship.ExhaustVelocityKmS, ship.MassTonnes, ship.MassTonnes - have);
            ship.Propellant = 0;
            return Math.Max(0, Math.Min(1, reached / deltaVKmS));
        }

        /// <summary>
        /// One frame of hand-flown thrust on the ship's tanks: the pilot's controls moved the velocity from
        /// <paramref name="velBefore"/> to <paramref name="vel"/> (km/s) and the position on from
        /// <paramref name="posBefore"/> over <paramref name="dt"/> seconds. The change is paid for in
        /// propellant; a ship that cannot pay for it (dry, or running dry part way) gets only what its tanks
        /// covered and coasts on with the rest, so a dry hull drifts on the velocity it had.
        /// </summary>
        public static void ApplyManoeuvre(Ship ship, ref DVec3 pos, ref DVec3 vel, DVec3 posBefore, DVec3 velBefore, double dt)
        {
            var change = vel - velBefore;
            double share = Spend(ship, change.Length);
            if (share >= 1) return;
            vel = velBefore + change * share;
            pos = posBefore + (velBefore + vel) * (0.5 * dt);
        }

        /// <summary>What an emergency tanker brings a ship that has run dry: enough to fill its tanks.</summary>
        public static double TankerLoad(Ship ship) => Math.Max(0, ship.PropellantCapacity - ship.Propellant);

        /// <summary>What the tanker charges for coming out with <paramref name="tonnes"/>: a call-out fee, then three times the going rate.</summary>
        public static double TankerFee(double tonnes) => 40000 + tonnes * 3 * PropellantBasePrice;

        /// <summary>Base price of a tonne of propellant: a tonne of water and its helium-3.</summary>
        public static double PropellantBasePrice => GoodInfo.Of(Good.Water).BasePrice + HeliumThreePerTonne * GoodInfo.Of(Good.HeliumThree).BasePrice;

        /// <summary>
        /// Gives <paramref name="to"/> up to <paramref name="tonnes"/> from <paramref name="from"/>'s tanks
        /// (a pilot answering a dry ship's call), as much as both can spare and hold. Returns what moved.
        /// </summary>
        public static double Transfer(Ship from, Ship to, double tonnes)
        {
            double moved = Math.Max(0, Math.Min(tonnes, Math.Min(from.Propellant, to.PropellantCapacity - to.Propellant)));
            from.Propellant -= moved;
            to.Propellant += moved;
            return moved;
        }
    }
}
