using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A piloted ship's crossing between two of the independent per-system simulations, at once (AI
    /// traffic crosses the same way but spends time in the gate; see <see cref="InterstellarTraffic"/>).
    /// Each system runs its own <see cref="World"/> (own bodies, factions, economy, war) with nothing
    /// shared between them, and every ship's id is a fixed index into its own fleet's list (ids are
    /// never reused or shuffled -- a destroyed hull stays in place, see <see cref="Ship.Destroyed"/>).
    /// The hull itself goes, the same object (U.SPC-T453), never a copy: <see cref="Fleet.LetGo"/> takes
    /// it out of the origin fleet's list, leaving a gone marker (<see cref="Ship.Tombstone"/>) in its old
    /// slot so no other id there moves, and <see cref="Fleet.Receive"/> takes it into the destination
    /// fleet's list, parked at the far gate -- the same ship carrying on, not a lookalike. Anything it was
    /// mid-way through at home (a trade route, a patrol, a hyperlane ride) meant nothing there either,
    /// built from body indices that belong to a different catalogue entirely, so <see cref="Ship.ResetForArrival"/>
    /// clears it: the hull arrives a free agent, able to wander and dock same as any independent ship
    /// (see <see cref="World.AddFreeShips"/>).
    /// </summary>
    public static class GateTransit
    {
        /// <summary>
        /// Sends <paramref name="ship"/> through <paramref name="gate"/> if it is close enough and the
        /// gate is paired (whether it is open to the ship is the caller's check, see <see cref="GateLink.Open"/>):
        /// takes it out of <paramref name="origin"/> and hands back the same hull, now living in the destination
        /// system's fleet. Returns false (no-op, <paramref name="arrived"/> null) otherwise.
        /// </summary>
        public static bool TryPerform(JumpGate gate, DVec3 shipHelioKm, Ship ship, World origin, double jd, out Ship arrived)
        {
            arrived = null;
            if (gate == null || ship == null || origin == null || !gate.CanActivate(shipHelioKm) || !origin.Fleet.IsPresent(ship)) return false;

            // Its orders, cooldowns and bookings here are for a hull no longer here (U.SPC-T311): the ship goes on
            // under its own steam, not a husk that stayed flagged for these books to read. Any band it led carries
            // on under the strongest of its escorts left behind (U.SPC-T310), same as a jump under its own drive;
            // the old flagship's hold goes with the hull that is actually leaving, not a stay-behind heir.
            origin.War?.Forget(ship);
            origin.Economy?.Release(ship);
            if (!ship.IsEscort)
            {
                bool wasDestroyed = ship.Destroyed;
                ship.Destroyed = true;
                origin.War?.HandOverCommand(ship);
                ship.Destroyed = wasDestroyed;
            }
            if (origin.Fleet.LetGo(ship) == null) return false;
            ship.Leader = null;
            ship.FormationSlot = default;
            ship.Voyage = null;

            var destination = gate.Destination;
            ship.ResetForArrival();
            ship.Role = ShipRole.Free;
            ship.Ports = destination.World.FreeShipPorts();

            var forward = gate.ArrivalForward;
            var up = Math.Abs(forward.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
            var right = DVec3.Cross(forward, up).Normalized;
            ship.Orbit = new ParkingOrbit
            {
                Body = destination.Catalog.Root.Index,
                RadiusKm = 0,
                Center = gate.ArrivalOffsetKm,
                U = right,
                V = forward,
                EpochJd = jd,
                PhaseAtEpoch = 0,
                RateRadPerDay = 0,
                Fixed = true,
            };
            ship.Phase = ShipPhase.Parked;
            ship.PhaseStartJd = jd;

            destination.World.Fleet.Receive(ship);
            // Announced like any other hull coming into the system (as gate traffic is, see InterstellarTraffic), so a
            // company hull is taken back into its company's fleet rather than dropped from it.
            destination.World.NotifyAdded(ship);
            // The far gate's pickets see it come out (U.SPC-T480).
            GatePickets.OnArrival(gate.Partner, new[] { ship }, jd);
            arrived = ship;
            return true;
        }
    }
}
