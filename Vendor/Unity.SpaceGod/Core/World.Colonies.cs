using System;

namespace Unity.SpaceGod.Core
{
    public sealed partial class World
    {
        // ---------------------------------------------------------------- the colony ships' hulls (U.SPC-T524)

        /// <summary>
        /// Gives this system's colonisation real hulls (U.SPC-T524): each colony ship that sails is a freighter fitted out at its
        /// port (<see cref="ShipRole.Colonist"/>, <see cref="Ship.Colony"/>), in this fleet, flying the voyage itself; it can be
        /// fought, boarded or lost on the way, and its colonists land only when it gets there (see <see cref="ColonyHullArrived"/>).
        /// </summary>
        void WireColonyHulls(Colonisation c)
        {
            c.Embark = EmbarkColonists;
            c.Refit = RefitColonyHull;
            c.Lost = (v, by) =>
            {
                var hull = v.Hull;
                string text = hull != null && hull.Destroyed && !hull.Scrapped
                    ? $"The colony ship {v.Name} is lost with all {v.Colonists:N0} of its colonists"
                    : by != null
                        ? $"The colony ship {v.Name} is taken by the {by.Name}, with its {v.Colonists:N0} colonists"
                        : $"The colony ship {v.Name}, bound for {v.Site.Name}{(v.Abroad ? " in " + v.Where : "")}, is never heard from again: its {v.Colonists:N0} colonists are given up for lost";
                Log(Economy.Jd, text, WorldEventKind.Loss, v.From?.Body ?? -1);
            };
            c.Returned = v => Log(Economy.Jd, $"The colony ship {v.Name} brings its {v.Colonists:N0} colonists home to {v.From.Name}",
                WorldEventKind.Info, v.From.Body);
        }

        /// <summary>
        /// Fits out a freighter at a colony ship's port for its voyage (U.SPC-T524): a new hull in this fleet under the
        /// colonists' flag and the voyage's name, lying at the port with the colonists and their starter goods aboard.
        /// </summary>
        Ship EmbarkColonists(ColonyShip v, double jd)
        {
            if (v?.From == null || v.Owner == null) return null;
            var hull = Fleet.AddShip("", ShipClass.Freighter, v.Owner, ShipRole.Colonist);
            hull.Name = v.Name;
            hull.CapacityTonnes = Economy.CapacityOf(ShipClass.Freighter);
            hull.Colony = v;
            hull.Ports = new[] { v.From.Body };
            double tonnes = 0;
            foreach (var t in v.Cargo) tonnes += t;
            hull.Cargo = $"{v.Colonists:N0} colonists and {tonnes:N0} t of starter goods for " +
                         (v.Reinforcing != null ? v.Reinforcing.Name : v.Site.Name + (v.Abroad ? " in " + v.Where : ""));
            Fleet.Station(hull, v.From.Body, double.IsNaN(Jd) ? jd : Math.Max(jd, Jd));
            NotifyAdded(hull);
            return hull;
        }

        /// <summary>
        /// A colony ship's hull has reached a body here (U.SPC-T524): its colonisation lands them, if this is where they are
        /// bound. A founding voyage's hull is broken up for the new settlement's habitat; one that has landed settlers at a young
        /// settlement, or brought home colonists it could not land, goes back to trading; one whose site was taken turns for
        /// home with them still aboard (through the gates, from abroad). True if the hull is gone.
        /// </summary>
        bool ColonyHullArrived(Ship hull, double jd)
        {
            var v = hull.Colony;
            if (v?.Of == null) return false;
            switch (v.Of.HullArrived(hull, Colonisation, Economy, jd))
            {
                case ColonyLanding.Founded:
                    BreakUpColonyHull(hull, jd);
                    return true;
                case ColonyLanding.Reinforced:
                case ColonyLanding.Returned:
                    RefitColonyHull(hull);
                    return false;
                case ColonyLanding.TurnedBack:
                    hull.Cargo = $"{v.Colonists:N0} colonists, turned back for {v.From.Name}";
                    // Abroad, it goes home through the gates now; at home, it flies back to the port (see ChooseNext).
                    if (hull.Voyage != null) hull.Voyage.StayUntilJd = jd;
                    return false;
            }
            return false;
        }

        /// <summary>
        /// Where a colony ship's hull at <paramref name="here"/> goes next (U.SPC-T524): on to the site (or the settlement) it is
        /// bound for, or home to its port with colonists it could not land; it holds where it is in a system it is only passing
        /// through. One bound abroad that is back at home with no voyage (the gates shut to it and it gave up waiting) carries its
        /// colonists back to port. A hull with no voyage left goes back to trading: -1, for the ordinary trader's choice.
        /// </summary>
        int ColonyHullNext(Ship hull, int here, double jd)
        {
            var v = hull.Colony;
            int? bearing = Colonisation.HullDestination(hull, Colonisation);
            if (bearing == null)
            {
                RefitColonyHull(hull);
                return -1;
            }
            if (bearing.Value >= 0) return bearing.Value;
            // (While it is still being fitted out, before it has been sent through the gates, it just waits.)
            if (v.Hull == hull && hull.Voyage == null && v.Of == Colonisation && v.Abroad && !v.Returning)
            {
                v.Of.TurnBack(v);
                hull.Cargo = $"{v.Colonists:N0} colonists, turned back for {v.From.Name}";
                Log(jd, $"The colony ship {hull.Name} cannot get through the gates to {v.Where} and turns back for {v.From.Name}", WorldEventKind.Info, here);
                return v.Body;
            }
            return here;
        }

        /// <summary>A founding voyage's hull is broken up where it landed: its frame and fittings are the new settlement's first habitat.</summary>
        void BreakUpColonyHull(Ship hull, double jd)
        {
            hull.Voyage = null;
            hull.GateKm = null;
            hull.Colony = null;
            Economy.Release(hull);
            War?.HandOverCommand(hull);
            hull.Scrapped = true;
            // Gone from the fleet without a wreck.
            hull.Destroyed = true;
            hull.GateDeparted = true;
            int body = hull.Orbit?.Body ?? -1;
            Log(jd, $"The colony ship {hull.Name} is broken up where it landed, its hull the settlers' first habitat", WorldEventKind.Economy, body);
        }

        /// <summary>
        /// A colony ship's hull done with its voyage, or never sent on one, goes back to work as an ordinary freighter of its
        /// flag (U.SPC-T524). Out on a voyage through the gates it first comes home, and takes up trading there.
        /// </summary>
        public void RefitColonyHull(Ship hull)
        {
            if (hull == null || hull.Destroyed) return;
            hull.Colony = null;
            if (hull.CapacityTonnes <= 0) hull.CapacityTonnes = Economy.CapacityOf(hull.Spec.Class);
            var v = hull.Voyage;
            if (v != null && v.Purpose == VoyagePurpose.Colonise)
            {
                // Where it is, it goes about as it pleases until it sets out for home, where it trades.
                hull.Role = ShipRole.Free;
                hull.Cargo = "Empty, homeward";
                v.HomeRole = ShipRole.Trader;
                if (v.Home?.World == this) v.HomePorts = PortBodies();
                v.StayUntilJd = 0;
                return;
            }
            hull.Role = ShipRole.Trader;
            hull.Cargo = "Empty, looking for work";
            hull.Ports = PortBodies();
        }
    }
}
