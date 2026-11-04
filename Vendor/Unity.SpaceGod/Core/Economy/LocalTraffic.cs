using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>The kinds of ordinary local craft (U.SPC-T375): buses and taxis, and people's own small ships.</summary>
    public enum CivilCraft
    {
        None,

        /// <summary>An orbital bus: forty seats on a fixed line round a planet's ports.</summary>
        Bus,

        /// <summary>A four-seat taxi taking fares between a planet's ports.</summary>
        Taxi,

        /// <summary>A single-seat personal skiff.</summary>
        Skiff,

        /// <summary>A four-seat personal runabout.</summary>
        Runabout,

        /// <summary>A six-seat personal tourer.</summary>
        Tourer,
    }

    /// <summary>
    /// Ordinary civilian traffic (U.SPC-T375), so a system reads as lived-in rather than as a combat arena: every
    /// inhabited port runs orbital buses on a line round its planet's other ports and taxis taking fares among them, and
    /// its people fly their own 1-, 4- and 6-seat craft about their neighbourhood (out to sightsee, over to a moon).
    /// All of it is local: a craft keeps to the worlds and stations of its own planet (one planetary system, so it never
    /// rides a hyperlane), as <see cref="ShipRole.Commuter"/> traffic. It is unarmed and no combatant (a fight never sweeps
    /// it in, and it steers clear of one), carries no trade, and runs on its operators' own fuel rather than the markets'.
    /// </summary>
    public static class LocalTraffic
    {
        /// <summary>The most local craft one system raises, the busiest ports first, so a crowded system stays cheap to fly.</summary>
        public const int MaxCraftPerSystem = 60;

        /// <summary>The most stops on one bus line.</summary>
        public const int LineLength = 4;

        /// <summary>The seats aboard a craft of this kind (its crew's included).</summary>
        public static int Seats(CivilCraft c)
        {
            switch (c)
            {
                case CivilCraft.Bus: return 40;
                case CivilCraft.Taxi: return 4;
                case CivilCraft.Skiff: return 1;
                case CivilCraft.Runabout: return 4;
                case CivilCraft.Tourer: return 6;
                default: return 0;
            }
        }

        /// <summary>Someone's own craft rather than a bus or a taxi.</summary>
        public static bool IsPersonal(CivilCraft c) => c == CivilCraft.Skiff || c == CivilCraft.Runabout || c == CivilCraft.Tourer;

        /// <summary>Buses a port of <paramref name="population"/> people runs: none for a small outpost, one, or two for a big city.</summary>
        public static int BusesFor(double population) => population < 1e5 ? 0 : population < 1e8 ? 1 : 2;

        /// <summary>Taxis working a port of <paramref name="population"/> people.</summary>
        public static int TaxisFor(double population) => population < 1e5 ? 0 : population < 1e7 ? 1 : population < 1e9 ? 2 : 3;

        /// <summary>Personal craft its people fly about: one at ten thousand people, one more for every hundredfold, up to four.</summary>
        public static int PersonalFor(double population) =>
            population < 1e4 ? 0 : Math.Min(4, 1 + (int)Math.Floor(Math.Log10(population / 1e4) / 2));

        /// <summary>The design each kind of craft is built to: small, quick and unarmed, a bridge, shields, an engine and a reactor.</summary>
        public static CombatSpec DesignOf(CivilCraft c)
        {
            switch (c)
            {
                case CivilCraft.Bus: return BusDesign;
                case CivilCraft.Taxi: return TaxiDesign;
                case CivilCraft.Skiff: return SkiffDesign;
                case CivilCraft.Runabout: return RunaboutDesign;
                case CivilCraft.Tourer: return TourerDesign;
                default: return null;
            }
        }

        static readonly CombatSpec BusDesign = Design("Orbital bus", 0.06, 1.5, 45, 600, 200, 0.22, 12, 3, 1.0);
        static readonly CombatSpec TaxiDesign = Design("Taxi", 0.02, 0.2, 8, 250, 80, 0.3, 24, 1, 2.0);
        static readonly CombatSpec SkiffDesign = Design("Single-seat skiff", 0.008, 0.02, 1, 120, 40, 0.34, 32, 1, 3.0);
        static readonly CombatSpec RunaboutDesign = Design("Four-seat runabout", 0.015, 0.08, 3, 200, 60, 0.3, 26, 1, 2.0);
        static readonly CombatSpec TourerDesign = Design("Six-seat tourer", 0.025, 0.15, 5, 300, 90, 0.27, 20, 2, 1.5);

        /// <summary>An unarmed small-craft design: <paramref name="massKt"/> of hull pushed by <paramref name="thrustMN"/> of
        /// drive, turning as readily for its size as <paramref name="agility"/> says (1 a bus, 3 a nimble skiff).</summary>
        static CombatSpec Design(string name, double lengthKm, double massKt, double thrustMN, double hull, double shield, double maxSpeed,
            double turnDegS, double crew, double agility)
        {
            double accel = thrustMN / (massKt * 1000);
            double pitchYaw = massKt * lengthKm * lengthKm / 12 * 10000 * agility;
            return new CombatSpec
            {
                DesignName = name, LengthKm = lengthKm,
                MassKt = massKt, MainThrustMN = thrustMN, RetroThrustMN = thrustMN * 0.75, LateralThrustMN = thrustMN * 0.4, VerticalThrustMN = thrustMN * 0.4,
                PitchYawTorqueMNKm = pitchYaw, RollTorqueMNKm = pitchYaw / 4,
                Hull = hull, Shield = shield, ShieldRegen = shield / 40, Armor = 0, MaxSpeed = maxSpeed, Accel = accel * 0.4, Decel = accel * 0.45,
                BoostSpeedMultiplier = 1.3, BoostDurationS = 12, TurnRateDegS = turnDegS,
                Crew = crew, Marines = 0,
                DryMassTonnes = massKt * 1000 * 0.7, PropellantTonnes = massKt * 1000 * 0.3, ExhaustVelocityKmS = 4000,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.04, 0.25),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, 0.1),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, -0.2),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.45),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.25),
                },
            };
        }

        static readonly string[] PersonalNames =
        {
            "Sunday Driver", "Pocket Rocket", "Weekender", "Little Wonder", "Moonshot", "Joyride", "Afterglow", "Day Tripper",
            "Lazy Susan", "Second Thoughts", "Gadabout", "Starling", "Puddle Jumper", "Retirement Plan", "Midlife", "Picnic",
            "Easy Does It", "Bright Idea", "Homebody", "Spare Change", "Tin Lizzie", "Odd Socks", "Free Spirit", "Night Owl",
            "Wishful Thinking", "Sightseer", "Hopscotch", "Daydream", "Wayfarer", "Small Hours",
        };

        static readonly CivilCraft[] PersonalKinds = { CivilCraft.Runabout, CivilCraft.Skiff, CivilCraft.Tourer };

        /// <summary>The planet whose neighbourhood <paramref name="body"/> is in (itself, for a planet or a body orbiting the
        /// star directly): the moons and stations of one planet share it, and a craft flying within it needs no hyperlane.</summary>
        public static int NeighbourhoodOf(BodyCatalog catalog, int body)
        {
            while (body > 0 && catalog[body].ParentIndex >= 0 && !catalog[catalog[body].ParentIndex].UsesStellarMapping) body = catalog[body].ParentIndex;
            return body;
        }

        /// <summary>
        /// Raises the system's local traffic: for each inhabited port (no haven, no hidden rock), the busiest first, its
        /// buses and taxis when its planet has another port to run to, and its people's own craft. Returns the craft raised.
        /// </summary>
        public static List<Ship> Raise(World w, double jd)
        {
            var raised = new List<Ship>();
            var catalog = w.Catalog;
            var ports = new List<Settlement>();
            foreach (var s in w.Economy.Settlements)
                if (s.Kind != SettlementKind.Haven && !s.Hidden && !s.Fallen && s.Owner != null && !s.Owner.IsPirate && s.Population > 0) ports.Add(s);
            ports.Sort((a, b) => b.Population.CompareTo(a.Population));
            var places = w.FreeShipPorts();
            int personal = 0;
            foreach (var home in ports)
            {
                if (raised.Count >= MaxCraftPerSystem) break;
                int hood = NeighbourhoodOf(catalog, home.Body);
                // The planet's port bodies, this one first, then the most populous.
                var stops = new List<int> { home.Body };
                foreach (var s in ports)
                    if (!stops.Contains(s.Body) && NeighbourhoodOf(catalog, s.Body) == hood && w.Diplomacy.CanDock(home.Owner, s.Owner)) stops.Add(s.Body);
                var sights = new List<int>();
                foreach (int b in places)
                    if (NeighbourhoodOf(catalog, b) == hood) sights.Add(b);
                if (!sights.Contains(home.Body)) sights.Add(home.Body);

                if (stops.Count >= 2)
                {
                    var line = stops.GetRange(0, Math.Min(LineLength, stops.Count)).ToArray();
                    for (int i = 0; i < BusesFor(home.Population) && raised.Count < MaxCraftPerSystem; i++)
                    {
                        var bus = Add(w, home.Name + " Bus " + (i + 1), CivilCraft.Bus, home.Owner, line, line[i % line.Length], jd);
                        bus.Route = line;
                        bus.RouteIndex = i % line.Length;
                        bus.Cargo = "Passengers on the " + home.Name + " line";
                        raised.Add(bus);
                    }
                    for (int i = 0; i < TaxisFor(home.Population) && raised.Count < MaxCraftPerSystem; i++)
                    {
                        var taxi = Add(w, home.Name + " Cab " + (i + 1), CivilCraft.Taxi, home.Owner, stops.ToArray(), home.Body, jd);
                        taxi.Cargo = "A fare";
                        raised.Add(taxi);
                    }
                }
                for (int i = 0; i < PersonalFor(home.Population) && raised.Count < MaxCraftPerSystem; i++)
                {
                    var kind = PersonalKinds[personal % PersonalKinds.Length];
                    string name = PersonalNames[personal % PersonalNames.Length] + (personal >= PersonalNames.Length ? " " + (personal / PersonalNames.Length + 1) : "");
                    personal++;
                    var craft = Add(w, name, kind, w.Independents ?? home.Owner, sights.ToArray(), home.Body, jd);
                    craft.Cargo = "Private flight";
                    raised.Add(craft);
                }
            }
            return raised;
        }

        static Ship Add(World w, string name, CivilCraft kind, Faction owner, int[] ports, int at, double jd)
        {
            var ship = w.Fleet.AddShip(name, ShipClass.Courier, owner, ShipRole.Commuter);
            // Its own registration, not a merchant's or a navy's prefix.
            ship.Name = name;
            ship.Civil = kind;
            ship.Combat = new ShipCombat(DesignOf(kind), ShipClass.Courier);
            ship.Ports = ports;
            w.Fleet.Station(ship, at, jd);
            return ship;
        }
    }
}
