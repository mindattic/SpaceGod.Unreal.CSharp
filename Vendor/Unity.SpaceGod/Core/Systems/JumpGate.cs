using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A fixed structure at a system's outer edge, paired one-for-one with a gate in a neighbouring
    /// system (see <see cref="GateNetwork"/>): a ship that flies to one and activates it emerges at the
    /// other. AI traffic takes <see cref="GateNetwork.TransitDays"/> to cross (see
    /// <see cref="InterstellarTraffic"/>); a piloted ship crosses at once (see <see cref="GateTransit"/>),
    /// since piloting runs the clock at real time. The menu-driven STAR SYSTEMS jump takes the camera
    /// through the gates too, on its own (see <see cref="GateJump"/>), without moving any ship.
    ///
    /// Placed at <see cref="RadiusKm"/> from the system's own root rather than anywhere near the real
    /// scale of a light-year crossing: a piloted hull's top speed (<see cref="PilotTuning"/>, even
    /// cruising) is tuned for dogfighting range, not interplanetary distances, and piloting runs the
    /// simulation clock at real time with no warp (see <c>UnitySpaceApp.TogglePiloting</c>) -- so the
    /// gate has to sit somewhere an actual flight stick can reach in minutes, not light-years.
    /// </summary>
    public sealed class JumpGate
    {
        public string Name { get; }

        /// <summary>The system this gate stands in.</summary>
        public StarSystem Home { get; }

        /// <summary>Fixed position relative to the system's own root (its star or barycentre), km, in
        /// the same ecliptic axes as its ephemeris. The root never moves in its own frame, so this is
        /// constant for the life of the system.</summary>
        public DVec3 PositionKm { get; }

        /// <summary>The gate at the far end; set once both ends exist (see <see cref="GateNetwork.Link"/>).
        /// Null means this gate has no working pair yet.</summary>
        public JumpGate Partner { get; internal set; }

        /// <summary>The link this gate is one end of; null until paired.</summary>
        public GateLink Link { get; internal set; }

        /// <summary>The system this gate leads to (the partner's home), or null.</summary>
        public StarSystem Destination => Partner?.Home;

        /// <summary>The faction that holds this end: its system's leading power (see
        /// <see cref="GateNetwork.AssignOwners"/>), or null for nobody; a war fleet that takes it, or a treaty that
        /// cedes its system, hands it to another (U.SPC-T92).</summary>
        public Faction Owner { get; internal set; }

        /// <summary>Closed to everyone but its owner until this date (a blockade); see <see cref="Admits"/>.</summary>
        public double BlockadeUntilJd = double.NegativeInfinity;

        /// <summary>
        /// The forts guarding this end (U.SPC-T92): defence platforms its owner keeps at the gate, which a war fleet forcing
        /// it fights in a gate battle (see <see cref="War.StartGateBattle"/>) before it can take the gate. Destroyed ones
        /// are struck off; the owner rebuilds them in peace (see <see cref="InterstellarWars"/>).
        /// </summary>
        public readonly List<ShipCombat> Forts = new List<ShipCombat>();

        /// <summary>How many forts the owner keeps here when it can (0: an unguarded gate).</summary>
        public int MaxForts;

        /// <summary>Since when this end has been short of the gate pickets its owner keeps here (U.SPC-T480, see
        /// <see cref="GatePickets.Upkeep"/>); NaN while it has them all.</summary>
        public double PicketsShortSinceJd = double.NaN;

        /// <summary>
        /// Whether <paramref name="faction"/> is at war with this end's owner (U.SPC-T92): the gate will not open for it, but
        /// a war fleet may force it, fighting its forts and taking it (see <see cref="InterstellarWars"/>). A gate shut by a
        /// blockade against a power at peace is shut, not forcible.
        /// </summary>
        public bool HostileTo(Faction faction) =>
            Owner != null && faction != null && faction != Owner && !faction.IsPirate && Home?.World.Diplomacy.Get(Owner, faction) == Relation.War;

        /// <summary>How far out a gate sits from its system's root: above the surface of a star (inside
        /// the Sun would be nowhere a ship can fly), from the point itself for a barycentre; see
        /// <see cref="Galaxy.GateDistanceKm"/>.</summary>
        public const double RadiusKm = 5000;

        /// <summary>A ship must be within this of the gate's position to activate it.</summary>
        public const double ActivationRangeKm = 300;

        /// <summary>Clear of the far gate on arrival, toward that system's centre, so an arriving ship
        /// doesn't appear exactly coincident with the structure it just came out of.</summary>
        public const double ArrivalInsetKm = 150;

        public JumpGate(string name, StarSystem home, DVec3 positionKm)
        {
            Name = name;
            Home = home;
            PositionKm = positionKm;
        }

        /// <summary>
        /// How whole the ring is, 0..1 (U.SPC-T97): 1 as built, worn down by sabotage and fighting in a war
        /// (see <see cref="Damage"/>), 0 a wreck. Below <see cref="OperationalIntegrity"/> it will not hold a
        /// crossing open, and nobody can go through either way until it is rebuilt (see <see cref="GateConstruction"/>).
        /// </summary>
        public double Integrity = 1;

        /// <summary>The least of <see cref="Integrity"/> a ring still works at: damaged, but holding.</summary>
        public const double OperationalIntegrity = 0.5;

        /// <summary>Whole enough to hold a crossing open (see <see cref="Integrity"/>).</summary>
        public bool Working => Integrity >= OperationalIntegrity;

        /// <summary>Wrecked outright: nothing left of the ring but its frame.</summary>
        public bool Wrecked => Integrity <= 0;

        /// <summary>Knocks <paramref name="amount"/> (0..1) off the ring's <see cref="Integrity"/>; true if that put it out of action.</summary>
        public bool Damage(double amount)
        {
            bool was = Working;
            Integrity = Math.Max(0, Integrity - Math.Max(0, amount));
            return was && !Working;
        }

        /// <summary>
        /// How busy the gate is (U.SPC-T94): ships through it, in or out, over about the last
        /// <see cref="InterstellarTraffic.GateTrafficDays"/> (decaying). Where raiders lie in wait for traffic.
        /// </summary>
        public double Traffic;

        /// <summary>Raids sprung on its approach so far (see <see cref="War.GateAmbushes"/>).</summary>
        public int Ambushes;

        /// <summary>How far inside the gate (toward the root) raiders lying in wait hold: clear of the
        /// structure, where ships coming out and ships going in both pass.</summary>
        public const double ApproachInsetKm = 600;

        /// <summary>The gate's approach, relative to its system's root: <see cref="ApproachInsetKm"/> inside it.</summary>
        public DVec3 ApproachKm => PositionKm - PositionKm.Normalized * ApproachInsetKm;

        public bool InRange(DVec3 shipHelioKm) => (shipHelioKm - PositionKm).Length <= ActivationRangeKm;

        /// <summary>Whether a ship at <paramref name="shipHelioKm"/> (relative to this gate's own
        /// system root) can activate this gate right now: paired, both rings working, and close enough.</summary>
        public bool CanActivate(DVec3 shipHelioKm) => Partner != null && Working && Partner.Working && InRange(shipHelioKm);

        public bool Blockaded(double jd) => jd < BlockadeUntilJd;

        /// <summary>
        /// Whether this end lets a ship of <paramref name="faction"/> through at <paramref name="jd"/>:
        /// an unowned gate lets anyone through; an owned one never opens for its owner's declared
        /// enemies (at war in the one table every system shares), and under a blockade opens for its owner alone.
        /// Raiders, who are nobody's declared enemy, slip through an open gate like anyone else.
        /// </summary>
        public bool Admits(Faction faction, double jd)
        {
            if (Owner == null || faction == null || faction == Owner) return true;
            if (Blockaded(jd)) return false;
            return Home?.World.Diplomacy.Get(Owner, faction) != Relation.War;
        }

        /// <summary>The direction an arriving ship faces: from the far gate back toward that system's
        /// own root, so it comes through facing into the system rather than out into the void.</summary>
        public DVec3 ArrivalForward => Partner == null ? DVec3.UnitX : -Partner.PositionKm.Normalized;

        /// <summary>Where a ship taking this gate appears, relative to the destination system's root:
        /// just inside the far gate, facing <see cref="ArrivalForward"/>.</summary>
        public DVec3 ArrivalOffsetKm => Partner == null ? DVec3.Zero : Partner.PositionKm + ArrivalForward * ArrivalInsetKm;

        public override string ToString() => Name;
    }
}
