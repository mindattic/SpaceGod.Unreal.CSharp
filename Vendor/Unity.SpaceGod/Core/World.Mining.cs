using System;

namespace Unity.SpaceGod.Core
{
    public sealed partial class World
    {
        /// <summary>
        /// The asteroid-belt mining pipeline that feeds this system's megastructure builds (U.SPC-T330, see
        /// <see cref="BeltMining"/>): capture, grinding, separation, convoys to warehouses, and the builds they supply.
        /// </summary>
        public BeltMining Mining { get; private set; }

        /// <summary>Raiders about at which the pipeline's convoys and warehouses run their full risk.</summary>
        public const int MiningPressureRaiders = 6;

        /// <summary>Connects the pipeline to this world: real distances, the raiders actually about, its guns, its news.</summary>
        void WireMining(int seed)
        {
            var m = Mining = new BeltMining(seed);
            m.Position = (body, jd) => Ephemeris.HelioAt(body, jd);
            m.PiratePressure = MiningPiratePressure;
            m.Raiders = () => Pirates;
            m.Guarded = w =>
            {
                foreach (var s in Economy.Settlements)
                    if (s.Body == w.Body && s.Owner == w.Owner && !s.Fallen && s.Platforms.Count > 0) return true;
                return false;
            };
            m.Log = (jd, text, body) => Log(jd, text, WorldEventKind.Economy, body);
            m.HeavyLane = MiningHeavyLane;
            // The ore tugs are real hulls in the fleet (U.SPC-T498): the works sends them, the fleet flies them.
            m.TugFree = TugFree;
            m.HaulSent = TugSent;
            m.TugThreat = TugThreat;
            m.TugSeized = HandOverTug;
            // So are the freighters its raw-material convoys sail in (U.SPC-T523): the works loads them, the fleet flies them.
            m.FreighterFree = FreighterFree;
            m.ConvoySailed = FreighterSailed;
            m.ConvoyDelivered = FreighterDelivered;
            m.YardBuilt = (p, jd) =>
            {
                foreach (var s in Economy.Settlements)
                {
                    if (s.Body != p.SiteBody || s.Owner != p.Builder || s.Fallen) continue;
                    foreach (var f in s.Facilities)
                        if (f.Recipe.Kind == IndustryKind.Shipyard)
                        {
                            f.Level += 1;
                            f.BaseLevel += 1;
                            return;
                        }
                    s.Facilities.Add(new Facility(Recipe.Of(IndustryKind.Shipyard), 1));
                    return;
                }
            };
        }

        /// <summary>
        /// How many raiders are about, 0..1: the raider hulls at sea (escorts aside) and the havens they work from,
        /// against <see cref="MiningPressureRaiders"/>.
        /// </summary>
        double MiningPiratePressure()
        {
            double raiders = 0;
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && !s.IsEscort && s.Faction != null && s.Faction.IsPirate) raiders++;
            foreach (var h in Economy.Settlements)
                if (h.Kind == SettlementKind.Haven && !h.Fallen) raiders += 0.5;
            return Math.Min(1, raiders / MiningPressureRaiders);
        }

        /// <summary>
        /// The heavy lanes an ore tug can push a load along from <paramref name="from"/> to <paramref name="to"/> (U.SPC-T483):
        /// the quickest route between their planetary systems over heavy lanes alone, its length, and the pushes from the
        /// source to the first ramp and from the last ramp to the destination. Null where no heavy lanes join them.
        /// </summary>
        HeavyLanePath? MiningHeavyLane(int from, int to, double jd)
        {
            var lanes = Fleet?.Lanes;
            if (lanes == null) return null;
            var route = lanes.HeavyRoute(lanes.SystemOf(from), lanes.SystemOf(to), jd);
            if (route == null || route.Count == 0) return null;
            double laneKm = 0;
            var names = new System.Text.StringBuilder();
            foreach (var hop in route)
            {
                laneKm += lanes.LaneLengthKm(hop.Lane, jd);
                if (names.Length > 0) names.Append(", ");
                names.Append(hop.Lane.Name);
            }
            var first = route[0];
            var last = route[route.Count - 1];
            double legKm = (Ephemeris.HelioAt(from, jd) - lanes.RampHelio(first.Lane, first.From, jd)).Length
                + (Ephemeris.HelioAt(to, jd) - lanes.RampHelio(last.Lane, last.To, jd)).Length;
            return new HeavyLanePath(laneKm, legKm, names.ToString());
        }

        /// <summary>
        /// Sol's belt works: the Belt Consortium's refinery at Ceres ships what it separates to a depot on Callisto,
        /// out at Jupiter, which feeds the tether going up at Ganymede and the first ring of a swarm around the Sun.
        /// Its two ore tugs (U.SPC-T483), real hulls in the fleet (U.SPC-T498), push blocks of ice cut in the belt out to
        /// the depot along the heavy Ceres-Jupiter lane, water and volatiles for the builds (the tether's carbon ribbon among them).
        /// </summary>
        void SeedSolMining(double jd)
        {
            var depot = Mining.AddWarehouse("Valhalla Depot", Belt, Catalog["Callisto"].Index);
            var works = Mining.AddOperation("Ceres Belt Works", Belt, Catalog["Ceres"].Index, depot);
            works.OreTugs = 2;
            works.TugIceShare = 1;
            works.IceDepot = Mining.AddWarehouse("Valhalla Ice Depot", Belt, Catalog["Callisto"].Index);
            RaiseTugs(works, jd);
            // Its convoys sail in two freighters of its own, each with a gunship riding escort (U.SPC-T523).
            works.ConvoyFreighters = 2;
            works.EscortShare = 0.5;
            RaiseFreighters(works, jd);
            // Its capture tugs (U.SPC-T522) are real hulls too, bringing belt rock in to the grinders at Ceres.
            RaiseCaptureTugs(works, jd);
            Mining.Begin(MegastructureSpec.SpaceElevator, Belt, Catalog["Ganymede"].Index, "Ganymede Elevator", jd);
            Mining.Begin(MegastructureSpec.DysonRing, Belt, 0, "Helios Swarm", jd);
        }

        // ---------------------------------------------------------------- the ore tugs' hulls (U.SPC-T498)

        /// <summary>Names the belt works give their ore tugs, in turn.</summary>
        static readonly string[] TugNames = { "Bulldog", "Sisyphus", "Atlas", "Yoke", "Plough Horse", "Long Shove", "Hauler's Oath", "Mule" };

        int tugsNamed;

        /// <summary>How near (km) raiders must be to an ore tug, where it actually is, to threaten it.</summary>
        public const double TugRaidReachKm = 0.2 * Astro.AuKm;

        /// <summary>Raider hulls about a tug at which it runs its full risk of being seized.</summary>
        public const int TugPressureRaiders = 3;

        /// <summary>
        /// Gives <paramref name="op"/> a real hull for each of its ore tugs (U.SPC-T498): every tug is a ship in this world's
        /// fleet, lying at the field it works (<see cref="MiningOperation.TugHome"/>) until the works sends it out. Returns them.
        /// </summary>
        public System.Collections.Generic.List<Ship> RaiseTugs(MiningOperation op, double jd)
        {
            op.Tugs ??= new System.Collections.Generic.List<Ship>();
            var made = new System.Collections.Generic.List<Ship>();
            int home = op.TugHome, to = op.IceDepot?.Body ?? op.RefineryBody;
            while (op.Tugs.Count < op.OreTugs)
            {
                string name = TugNames[tugsNamed % TugNames.Length] + (tugsNamed >= TugNames.Length ? " " + (tugsNamed / TugNames.Length + 1) : "");
                tugsNamed++;
                var ship = Fleet.AddShip(name, ShipClass.OreTug, op.Owner, ShipRole.Tug);
                ship.Ports = home == to ? new[] { home } : new[] { home, to };
                ship.Cargo = "Claws open, waiting for a load";
                Fleet.Station(ship, home, jd);
                op.Tugs.Add(ship);
                made.Add(ship);
            }
            op.OreTugs = op.Tugs.Count;
            return made;
        }

        // ---------------------------------------------------------------- the capture tugs' hulls (U.SPC-T522)

        /// <summary>Names the belt works give their capture tugs, in turn.</summary>
        static readonly string[] CaptureTugNames = { "Lasso", "Grapple", "Gaff", "Tongs", "Snare", "Bola", "Butterfly Net", "Catch-As-Catch-Can" };

        int captureTugsNamed;

        /// <summary>
        /// Gives <paramref name="op"/> a real hull for each of its capture tugs (U.SPC-T522): every one a ship
        /// (<see cref="ShipClass.CaptureTug"/>) in this world's fleet, lying at its refinery until the works sends it out into
        /// the field round it for a rock. Flown, threatened, seized and helmed as an ore tug's hull is. Returns them.
        /// </summary>
        public System.Collections.Generic.List<Ship> RaiseCaptureTugs(MiningOperation op, double jd)
        {
            op.CaptureHulls ??= new System.Collections.Generic.List<Ship>();
            var made = new System.Collections.Generic.List<Ship>();
            int home = op.RefineryBody;
            while (op.CaptureHulls.Count < op.CaptureTugs)
            {
                string name = CaptureTugNames[captureTugsNamed % CaptureTugNames.Length]
                    + (captureTugsNamed >= CaptureTugNames.Length ? " " + (captureTugsNamed / CaptureTugNames.Length + 1) : "");
                captureTugsNamed++;
                var ship = Fleet.AddShip(name, ShipClass.CaptureTug, op.Owner, ShipRole.Tug);
                ship.Ports = new[] { home };
                ship.Cargo = "Claws open, waiting to go out for a rock";
                Fleet.Station(ship, home, jd);
                op.CaptureHulls.Add(ship);
                made.Add(ship);
            }
            op.CaptureTugs = op.CaptureHulls.Count;
            return made;
        }

        /// <summary>Whether a tug's hull lying at its field may be sent out: not at the player's helm, not fighting, not held.</summary>
        bool TugFree(Ship tug) => tug != PilotedShip && tug.Battle == null && (Fleet.IsHeld == null || !Fleet.IsHeld(tug));

        /// <summary>
        /// The works has sent a tug's hull out on a haul: with its load latched on it sets out for where the load is for now,
        /// by the heavy lanes where they run (see <see cref="Hyperlanes.RouteFor"/>); with its load out in the field of the
        /// body it is for, it flies out empty to it first.
        /// </summary>
        void TugSent(TugHaul haul, double jd)
        {
            var tug = haul.Tug;
            double now = double.IsNaN(Jd) ? jd : Math.Max(jd, Jd);
            if (haul.Latched)
            {
                tug.Cargo = $"Pushing {haul.Load.Designation}, {haul.Load.Tonnes:N0} t, to {Catalog[haul.ToBody].Name}";
                Fleet.ScheduleNext(tug, now);
                return;
            }
            tug.Cargo = $"Out to the field for {haul.Load.Designation}";
            tug.Wander = true;
            tug.DestinationBody = tug.Orbit.Body;
            tug.PhaseStartJd = now;
            tug.PhaseEndJd = now + 0.05;
        }

        /// <summary>The tug's hull has arrived somewhere: its works latches its load on, or takes the load in.</summary>
        void TugArrived(Ship tug, double jd)
        {
            var h = Mining?.HaulOf(tug);
            if (h == null || !Mining.TugArrived(tug, jd)) return;
            if (tug.Tow != null) tug.Cargo = $"Pushing {h.Load.Designation}, {h.Load.Tonnes:N0} t, in to {Catalog[h.ToBody].Name}";
            else
            {
                tug.Cargo = h.Capture ? "Claws open, back at the refinery" : "Running empty, back to the field";
                Log(jd, $"The {h.TugKind} {tug.Name} brings {h.Load.Designation} in to {(h.ToDepot != null ? "the " + h.ToDepot.Name : Catalog[h.ToBody].Name)}: {h.Load.Tonnes:N0} t of {h.Load.Class.Name.ToLowerInvariant()}",
                    WorldEventKind.Economy, h.ToBody);
            }
        }

        /// <summary>
        /// What threatens a tug's hull where it actually is (U.SPC-T498): raider hulls within <see cref="TugRaidReachKm"/> of it,
        /// against <see cref="TugPressureRaiders"/>, the nearest of them the ones who would take it, and the world it is nearest.
        /// </summary>
        TugDanger TugThreat(TugHaul haul, double jd)
        {
            var tug = haul.Tug;
            double at = double.IsNaN(Jd) ? jd : Jd;
            var here = Fleet.StateOf(tug, at).HelioKm;
            int near = 0;
            Faction nearest = null;
            double best = double.MaxValue;
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || s.Derelict || s.Faction == null || !s.Faction.IsPirate || s.Controller.Planetside) continue;
                double d = (Fleet.StateOf(s, at).HelioKm - here).Length;
                if (d > TugRaidReachKm) continue;
                near++;
                if (d < best)
                {
                    best = d;
                    nearest = s.Faction;
                }
            }
            if (near == 0) return new TugDanger(null, 0, -1);
            return new TugDanger(nearest, Math.Min(1, (double)near / TugPressureRaiders), NearestBody(here, at));
        }

        /// <summary>The world (planet, moon or minor body, not a star) nearest a point on a date.</summary>
        int NearestBody(DVec3 helioKm, double jd)
        {
            int best = -1;
            double bestKm = double.MaxValue;
            foreach (var b in Catalog.Bodies)
            {
                if (!b.IsVisible || b.IsStar) continue;
                double d = (Ephemeris.HelioAt(b.Index, jd) - helioKm).Length;
                if (d < bestKm)
                {
                    bestKm = d;
                    best = b.Index;
                }
            }
            return best;
        }

        /// <summary>
        /// Raiders have seized a tug's hull where it was (U.SPC-T498): it flies their colours from there, and makes for one of
        /// their havens (or, with none, goes where it pleases); its load is already theirs.
        /// </summary>
        void HandOverTug(TugHaul haul, double jd)
        {
            var tug = haul.Tug;
            var by = haul.TakenBy;
            if (tug == null || by == null || tug.Destroyed) return;
            var old = tug.Faction;
            tug.Faction = by;
            if (by.Prefix.Length > 0 && old != null && tug.Name.StartsWith(old.Prefix + " ")) tug.Name = by.Prefix + tug.Name.Substring(old.Prefix.Length);
            Fleet.AddFaction(by);
            tug.Role = ShipRole.Free;
            tug.Wander = false;
            tug.Homing = false;
            tug.Cargo = $"Taken by the {by.Name}";
            var havens = new System.Collections.Generic.List<int>();
            foreach (var s in Economy.Settlements)
                if (s.Owner == by && s.Kind == SettlementKind.Haven && !s.Fallen && !havens.Contains(s.Body)) havens.Add(s.Body);
            bool hold = havens.Count > 0;
            if (!hold)
                foreach (var b in Catalog.Bodies)
                    if (b.IsVisible && !b.IsStar) havens.Add(b.Index);
            tug.Ports = havens.ToArray();
            // Turned for the raiders' hold at once where it is on a burn; on a lane, or in orbit, when it next plans.
            double now = double.IsNaN(Jd) ? jd : Math.Max(jd, Jd);
            if (hold && Fleet.Divert(tug, havens[0], now)) return;
            if (tug.Phase == ShipPhase.Parked && (Fleet.IsHeld == null || !Fleet.IsHeld(tug))) Fleet.ScheduleNext(tug, now);
        }
    }
}
