using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    public enum ShipClass
    {
        Carrier,
        Battleship,
        Cruiser,
        Destroyer,
        Freighter,
        Miner,
        Courier,

        // Number 7 was the Breacher hull. A Breacher is only a round fired from a Breacher bay now, never a ship (U.SPC-T517,
        // see BreacherBays and MissileKind.Breacher), so the number is left unused and the classes after it keep theirs.

        /// <summary>The space superiority fighter (docs/SHIP_ROSTER.md, U.SPC-T484): the balanced all-round workhorse
        /// of the strike craft, twin forward guns and a missile rack, built to escort capital ships and freighters
        /// and to dogfight -- the light fighter, beside the Courier, a trade hull that doubles as one. Carriers launch
        /// it as their fighter squadrons (<see cref="FighterRole.SpaceSuperiority"/>).</summary>
        Fighter = 8,

        /// <summary>A deep-space explorer (U.SPC-T419): a long-legged survey hull built to leave the gate network under its
        /// own FTL drive and chart the stars nobody has been to. Lightly armed, big enough for its drive, small next to a
        /// capital ship.</summary>
        Explorer,

        /// <summary>The heavy fighter (U.SPC-T467): an armoured, multi-crew gunship with crewed turrets and a spinal gun,
        /// built to brawl and to take on corvettes. It flies the patrol and escort wings' heavy-fighter slots, in the
        /// <see cref="FlightFamily.HeavyFighter"/> family. Last in the list so saved class numbers keep their meaning.</summary>
        Gunship,
        /// <summary>An ore tug (U.SPC-T483): no hold, just a big engine with claws that latches onto a captured asteroid or a
        /// block of comet ice and pushes it, slowly, to a refinery or a depot (see <see cref="BeltMining"/>). The heavier the
        /// load, the gentler the push: its acceleration is its drive's thrust over the tug and its load together.</summary>
        OreTug,

        /// <summary>The interceptor (U.SPC-T484): the fastest, most agile strike craft, light guns and a missile, built to
        /// hunt enemy fighters and screen its own bombers. Flies on its own flight model (<see cref="FlightFamily.Interceptor"/>);
        /// carriers launch it as their interceptor squadrons (<see cref="FighterRole.Interceptor"/>).</summary>
        Interceptor,

        /// <summary>The bomber (U.SPC-T484): slow and tough, with torpedoes for anti-capital strikes that hit hardest once a
        /// capital's shields are down; it needs an escort. Flies on its own flight model (<see cref="FlightFamily.Bomber"/>);
        /// carriers launch it as their bomber squadrons (<see cref="FighterRole.Bomber"/>).</summary>
        Bomber,

        /// <summary>A lane tender (U.SPC-T481): an unarmed maintenance hull that keeps a hyperlane's accelerator rings powered
        /// and aligned (see <see cref="LaneUpkeep"/>). A lane nobody tends slows, grows unreliable and in the end fails, so
        /// tenders matter in peace and are a prime mark in war.</summary>
        LaneTender,

        /// <summary>A gate picket (U.SPC-T480): a small, sensor-heavy hull stationed at a held jump gate's exit to give its
        /// owner early warning of whatever comes through the gate or drops out of FTL nearby (see <see cref="GatePickets"/>).
        /// Lightly armed and no match for a raider band, which goes for it first.</summary>
        GatePicket,

        /// <summary>A command cruiser, or battlecruiser (U.SPC-T477): a capital that gives up some guns for a large sensor
        /// array, a communications suite and targeting computers, and carries the fleet's senior officers close to the
        /// action. Every ship on its side within its reach fights coordinated (see <see cref="FleetCoordination"/>); losing
        /// it throws them into confusion, so it is a priority target. Navies build it to lead their groups.</summary>
        CommandCruiser,

        /// <summary>A frigate (U.SPC-T475): a fast, lightly armoured, engine-heavy missile and torpedo boat with deep magazines
        /// of long-range anti-ship missiles and antimatter (photon) torpedoes. Its doctrine is the alpha strike (see
        /// <see cref="AlphaStrike"/>): burn in, loose one devastating volley, flip and burn back out, trusting to speed and
        /// distance rather than armour. No FTL drive: it crosses by the gates, or clamped to a carrier's hull.</summary>
        Frigate,

        /// <summary>A corvette (U.SPC-T476): system defence with no FTL drive, the weight saved going to patrol (deep tanks, a
        /// patrol search suite, a brawler's guns). Stationed for good at one of its navy's ports (a planet, moon or station),
        /// it rides the hyper lanes of its home system as local law enforcement, hails and scans traffic and runs down the
        /// smugglers and pirates working its beat, and it never leaves that system (see <see cref="Corvettes"/>).</summary>
        Corvette,

        /// <summary>The recon / electronic warfare fighter (U.SPC-T485): a fast, lightly armed strike craft packed with
        /// sensors and jammers. Carriers fly a pair of them ahead of their squadrons (see <see cref="ElectronicWarfare"/>):
        /// they scout ahead, jam the targeting of every enemy ship near them so it fires less accurately, and paint a capital
        /// ship for the bombers, whose torpedoes then home on it harder and are not drawn off by its decoys. Carried aboard, they
        /// stretch the carrier's own sensor reach.</summary>
        Recon,

        /// <summary>A boarding shuttle, or assault shuttle (U.SPC-T486): a piloted, armoured, unarmed craft carried in a
        /// carrier's hangars that ferries a marine team across to a disabled ship or a silenced station (a gate's fort among
        /// them), matches its drift, docks alongside and latches on, and lets the team in through the airlock to take it
        /// intact: slower and gentler than a fired Breacher round (U.SPC-T466), no drill and no breach. See
        /// <see cref="BoardingShuttles"/>; carriers launch it as <see cref="FighterRole.AssaultShuttle"/>.</summary>
        AssaultShuttle,

        /// <summary>A gate breaker (U.SPC-T478): the siege capital built to take a jump gate. The most heavily armoured hull
        /// in the game, it closes with a gate's forts behind brawling batteries of plasma cannons, and carries boarding teams
        /// (a big marine habitat and two boarding shuttles) and engineering teams (a siege engineering bay) to seize the gate
        /// intact and repair its ring. It always carries its own FTL drive, since it attacks the gates the rest of the fleet
        /// would use (see <see cref="GateBreakers"/>).</summary>
        GateBreaker,

        /// <summary>A capture tug (U.SPC-T522): a belt works' small, nimble claw boat. It flies out into the field round its
        /// refinery, matches a rock of a few tens to a hundred-odd metres (<see cref="MiningOperation.MinRockM"/> to
        /// <see cref="MiningOperation.MaxRockM"/>), latches on and pushes it in for the grinders (see <see cref="BeltMining"/>):
        /// an <see cref="OreTug"/>'s machinery on a lighter frame, for far smaller loads.</summary>
        CaptureTug,
    }

    public enum ShipRole
    {
        Trader,
        Miner,
        Courier,
        Patrol,
        Escort,

        /// <summary>
        /// An independent pilot (a yacht, a survey ship, a prospector) going where it pleases: out to
        /// open space near a world to drift a while, over to a neighbouring world, now and then further.
        /// </summary>
        Free,

        /// <summary>
        /// Ordinary local traffic (U.SPC-T375, see <see cref="LocalTraffic"/>): an orbital bus on its line, a taxi on
        /// its fares, or someone's own small craft, flying short hops between the worlds and stations of one planet's
        /// neighbourhood. Not a trader, not a combatant, and never sent through a gate.
        /// </summary>
        Commuter,

        /// <summary>
        /// A lane tender working its hyperlane (U.SPC-T481, see <see cref="LaneUpkeep"/>): it shuttles between the lane's two
        /// ramps and lies over the rings at each end, its polity's upkeep, not a market's, deciding where it goes.
        /// </summary>
        Tender,

        /// <summary>
        /// An ore tug (or a capture tug, U.SPC-T522) at work for its belt works (U.SPC-T498, see <see cref="BeltMining"/>): it
        /// lies at the field its loads come from, latches onto one when its operation sends it out (<see cref="Ship.Tow"/>),
        /// pushes it in to the refinery or the depot it is for, and comes home empty. Its operation, not a market, decides where it goes.
        /// </summary>
        Tug,

        /// <summary>
        /// A belt works' raw-material freighter (U.SPC-T523, see <see cref="MiningOperation.Freighters"/>): it lies at its
        /// refinery, sails with a hold of separated elements when its works loads one (a <see cref="MaterialConvoy"/>), flies
        /// it to the works' warehouse with its escorts in formation, and comes home empty. Its works, not a market, decides
        /// where it goes, and its load is never sold.
        /// </summary>
        Hauler,

        /// <summary>
        /// A navy's supply ship (U.SPC-T525, see <see cref="MilitaryLogistics"/>): a tanker or an ammunition collier that lies
        /// at its naval base between runs and sails, with the rest of a <see cref="SupplyConvoy"/> in formation, through the
        /// gates to a war fleet on campaign, hands over what it carries when it comes up with it, and comes home. Its navy's
        /// logistics, not a market, decides where it goes, and what it carries is never sold.
        /// </summary>
        Supply,

        /// <summary>
        /// A freighter fitted out as a colony ship (U.SPC-T524, see <see cref="Colonisation"/>): it carries a party of colonists
        /// and their starter goods (<see cref="Ship.Colony"/>) from their port to the site they are to settle, or to the young
        /// settlement they are to swell, through the gates for a site in another system, and they land only when it gets there.
        /// Its voyage, not a market, decides where it goes; once it has landed them it goes back to trading.
        /// </summary>
        Colonist,
    }

    public enum ShipPhase
    {
        Parked,

        /// <summary>A flip-and-burn under the ship's own drive (a local hop, or to and from a hyperlane ramp).</summary>
        Transit,

        /// <summary>Riding a hyperlane between two planetary systems.</summary>
        Lane,

        /// <summary>Holding station just off a hyperlane, waiting in ambush (pirates).</summary>
        Lurk,

        /// <summary>Thrown out of a hyperlane by an interdiction pulse, drifting beside it.</summary>
        Adrift,

        /// <summary>Waiting off a docking ring to be cleared down, edging in along the holding line.</summary>
        Queued,

        /// <summary>Dropping from the ring straight down to its city, through the atmosphere.</summary>
        Descent,

        /// <summary>Down at the city's port (not drawn; it trades here).</summary>
        Landed,

        /// <summary>Climbing back up to the ring to leave.</summary>
        Ascent,
    }

    public sealed class ShipClassSpec
    {
        public ShipClass Class { get; }
        public string DisplayName { get; }

        /// <summary>Sustained drive acceleration in g. Torch-drive fiction; the kinematics are real.</summary>
        public double AccelG { get; }

        public bool IsMilitary { get; }

        ShipClassSpec(ShipClass c, string name, double accelG, bool military)
        {
            Class = c;
            DisplayName = name;
            AccelG = accelG;
            IsMilitary = military;
        }

        static readonly ShipClassSpec[] Specs =
        {
            new ShipClassSpec(ShipClass.Carrier, "Carrier", 0.3, true),
            new ShipClassSpec(ShipClass.Battleship, "Battleship", 0.3, true),
            new ShipClassSpec(ShipClass.Cruiser, "Cruiser", 0.3, true),
            new ShipClassSpec(ShipClass.Destroyer, "Destroyer", 0.3, true),
            // A truss freighter's drive is weak (U.SPC-T482): it rides the lanes, and off them is slow to speed up and slow down.
            new ShipClassSpec(ShipClass.Freighter, "Freighter", 0.06, false),
            new ShipClassSpec(ShipClass.Miner, "Ore hauler", 0.06, false),
            new ShipClassSpec(ShipClass.Courier, "Courier", 1.2, false),
            // The retired Breacher hull's number (U.SPC-T517): no class, so no spec.
            null,
            new ShipClassSpec(ShipClass.Fighter, "Fighter", 1.0, true),
            new ShipClassSpec(ShipClass.Explorer, "Explorer", 0.6, false),
            new ShipClassSpec(ShipClass.Gunship, "Gunship", 0.7, true),
            new ShipClassSpec(ShipClass.OreTug, "Ore tug", 0.08, false),
            new ShipClassSpec(ShipClass.Interceptor, "Interceptor", 1.3, true),
            new ShipClassSpec(ShipClass.Bomber, "Bomber", 0.6, true),
            new ShipClassSpec(ShipClass.LaneTender, "Lane tender", 0.2, false),
            new ShipClassSpec(ShipClass.GatePicket, "Gate picket", 0.8, true),
            new ShipClassSpec(ShipClass.CommandCruiser, "Command cruiser", 0.3, true),
            // An engine-heavy frigate (U.SPC-T475): the fastest warship hull short of a strike craft.
            new ShipClassSpec(ShipClass.Frigate, "Frigate", 0.6, true),
            // A patrol hull (U.SPC-T476): quick enough to run a smuggler down inside its system, no frigate's sprinter.
            new ShipClassSpec(ShipClass.Corvette, "Corvette", 0.7, true),
            // The recon fighter (U.SPC-T485): as quick as an interceptor, it has to get ahead of the strike.
            new ShipClassSpec(ShipClass.Recon, "Recon fighter", 1.3, true),
            // A boarding shuttle (U.SPC-T486): heavy for its size, no faster than a bomber.
            new ShipClassSpec(ShipClass.AssaultShuttle, "Assault shuttle", 0.5, true),
            // The siege capital (U.SPC-T478): its plate makes it the slowest warship hull.
            new ShipClassSpec(ShipClass.GateBreaker, "Gate breaker", 0.25, true),
            // A capture tug (U.SPC-T522): a lighter frame than an ore tug's, quicker to chase down and match a tumbling rock.
            new ShipClassSpec(ShipClass.CaptureTug, "Capture tug", 0.15, false),
        };

        public static ShipClassSpec Of(ShipClass c) => Specs[(int)c];
    }

    public sealed class Faction
    {
        public string Name { get; }
        public string Prefix { get; }
        public Rgb Color { get; }

        /// <summary>Pirates trade only at their havens and are hostile to everyone.</summary>
        public bool IsPirate { get; set; }

        /// <summary>A navy that fights wars and builds warships (not the independents).</summary>
        public bool IsMilitary { get; set; }

        /// <summary>What sort of power it is: a state, a company, free traders or a pirate clan (see <see cref="Polities"/>).</summary>
        public PolityKind Kind { get; set; } = PolityKind.State;

        /// <summary>The seat of its government, as founded (see <see cref="Polities.CapitalOf"/>, which follows it when it is lost); null for none.</summary>
        public Settlement Capital { get; set; }

        /// <summary>How good its marines are, man for man (1 = average). Upgrades raise it.</summary>
        public double MarineQuality { get; set; } = 1;

        /// <summary>Credits from port taxes and its own ships' trade, spent on hulls, repairs and munitions.</summary>
        public double Treasury;

        /// <summary>The player's house account: its ships never spend more than it holds (U.SPC-T539).</summary>
        public bool IsHouse { get; set; }

        /// <summary>Goods its law bans: a patrol scan that finds them in a hold confiscates them as contraband
        /// (stolen goods always are; see <see cref="Law.ContrabandIn"/>).</summary>
        public readonly HashSet<Good> Contraband = new HashSet<Good>();

        /// <summary>
        /// Trained spacers it can put aboard: new hulls and refits draw their crews and marines from it,
        /// recruiting tops it up, and crew brought home from escape pods or ransomed back return to it
        /// (see <see cref="Crews"/>). Infinite (never short) until a world musters real reserves.
        /// </summary>
        public double Manpower = double.PositiveInfinity;

        /// <summary>
        /// How its crews feel about the war, 1 the baseline: heavy losses wear it down (crews then strike
        /// their colours sooner, see <see cref="Morale.Resolve"/>), rescues lift it, and it recovers with time.
        /// </summary>
        public double CrewMorale = 1;

        /// <summary>Spacers it has lost for good (died aboard, in a pod or as hostages), and brought home from pods.</summary>
        public double CrewLost, CrewRescued;

        public Faction(string name, string prefix, Rgb color)
        {
            Name = name;
            Prefix = prefix;
            Color = color;
        }

        public override string ToString() => Name;
    }

    /// <summary>A circular parking orbit around a body, in a plane given by an orthonormal pair (U, V).</summary>
    public sealed class ParkingOrbit
    {
        public int Body;
        public double RadiusKm;
        public DVec3 U;
        public DVec3 V;
        public double EpochJd;
        public double PhaseAtEpoch;
        public double RateRadPerDay;

        /// <summary>
        /// A fixed point rather than an orbit (a hyperlane ramp): it holds its offset from the body and
        /// planning does not re-phase it to face the incoming ship.
        /// </summary>
        public bool Fixed;

        /// <summary>
        /// Centre of the circle, offset from the body along its spin axis: zero for an orbit; for a
        /// point fixed over a place on a turning world (a docking ring) the circle is its parallel of latitude.
        /// </summary>
        public DVec3 Center;

        public double PhaseAt(double jd) => PhaseAtEpoch + RateRadPerDay * (jd - EpochJd);

        public DVec3 OffsetAt(double jd)
        {
            double p = PhaseAt(jd);
            return Center + (U * Math.Cos(p) + V * Math.Sin(p)) * RadiusKm;
        }

        public DVec3 TangentAt(double jd)
        {
            double p = PhaseAt(jd);
            return -U * Math.Sin(p) + V * Math.Cos(p);
        }

        /// <summary>Orbital velocity relative to the body, km/day.</summary>
        public DVec3 VelocityAt(double jd) => TangentAt(jd) * (RateRadPerDay * RadiusKm);

        /// <summary>The phase at which the orbit position points most nearly along <paramref name="direction"/>.</summary>
        public double PhaseFacing(DVec3 direction) => Math.Atan2(DVec3.Dot(direction, V), DVec3.Dot(direction, U));

        public double SpeedKmS => RateRadPerDay * RadiusKm / Astro.SecondsPerDay;
    }

    public sealed class Ship
    {
        public int Id;
        public string Name;
        public ShipClassSpec Spec;
        public Faction Faction;
        public ShipRole Role;

        /// <summary>This hull's own length — per-design (the mesh/stat variant this ship was built as), not per-class.</summary>
        public double LengthKm => Combat.Spec.LengthKm;

        /// <summary>Escorts fly in formation on a leader; slot is in leader lengths (x right, y up, z forward).</summary>
        public Ship Leader;
        public DVec3 FormationSlot;

        /// <summary>
        /// The group (flagship plus escorts, see <see cref="Fleet.AddEscort"/>) this hull was raised or mustered
        /// into -- the flagship's own id, shared by every escort added to it since, carried along through a
        /// merge (the absorbed band's flagship becomes an escort under the surviving one's id) and a
        /// succession (an escort already shares the dead flagship's id, so its heir keeps it). -1 until the
        /// ship has ever been part of a formed group.
        ///
        /// Unlike <see cref="Leader"/>, which the pilot's entry clears the instant an escort is taken at the
        /// helm (so the AI formation code lets go of it) and a flagship's succession clears on the ship that
        /// steps up, this id is never reset: it is what tells a bounty or an ambush apart from a raider of the
        /// same pirate clan who simply isn't on it (U.SPC-T363).
        /// </summary>
        public int BandId = -1;

        /// <summary>This ship's own group identity for that purpose: <see cref="BandId"/> once it has one,
        /// else the ship itself (so a hull that was never mustered into a group is its own, matching nothing
        /// else).</summary>
        public int Band => BandId >= 0 ? BandId : Id;

        /// <summary>Patrol waypoints (body indices), visited in order.</summary>
        public int[] Route;
        public int RouteIndex;

        /// <summary>Ports a trader or courier may visit; for miners, element 0 is the home rock.</summary>
        public int[] Ports;

        public ShipPhase Phase;
        public ParkingOrbit Orbit;
        public ParkingOrbit DepartureOrbit;
        public int OriginBody = -1;
        public int DestinationBody = -1;
        public double PhaseStartJd;
        public double PhaseEndJd;

        /// <summary>
        /// The transit is flown in this body's frame: the Sun between planets, the common parent for
        /// a hop within one planet's system (Earth to the Moon, Io to Europa).
        /// </summary>
        public int FrameBody;

        /// <summary>
        /// Velocity (km/day, relative to the frame) the ship leaves with and arrives at: those of its
        /// departure and arrival parking orbits. The drift they imply is added to the path.
        /// </summary>
        public DVec3 DepartureVelocity;
        public DVec3 ArrivalVelocity;

        /// <summary>The flip-and-burn path, relative to the frame body before drift is added.</summary>
        public DVec3[] Path;
        public double[] PathCumulative;
        public double PathLength;
        public double AccelKmPerDay2;

        /// <summary>
        /// A coasting transfer orbit this transit flies instead of the flip-and-burn (U.SPC-T95): a
        /// low-thrust hauler's escape burn, Lambert conic and capture burn. Null for a flip-and-burn,
        /// whose <see cref="Path"/> is then the line it holds.
        /// </summary>
        public ConicTransfer Conic;

        /// <summary>The delta-v (km/s) a flip-and-burn spends holding its straight line against gravity (U.SPC-T95).</summary>
        public double GravityLossKmS;

        /// <summary>
        /// The transfer the planner chose when it set the departure date (U.SPC-T95): when it leaves
        /// (a launch window it waits in port for), its flight time (negative when it judged a flip-and-burn
        /// the better buy), and whether it flies straight across on a transfer orbit rather than by
        /// hyperlane. NaN when nothing is planned.
        /// </summary>
        public double PlannedConicDepartJd = double.NaN;
        public double PlannedConicDays;
        public bool PlannedDirect;

        /// <summary>A one-line description of what is in the hold, for the HUD.</summary>
        public string Cargo;

        /// <summary>Sitting in port for propellant to be had before it can set out, whatever it carries (U.SPC-T538).</summary>
        public bool WaitingForPropellant;
        public int TripsCompleted;

        /// <summary>
        /// An interplanetary trip by hyperlane: the lanes still to ride (lane, from system, to system)
        /// and the body the trip ends at. Null for a local trip.
        /// </summary>
        public List<LaneHop> Journey;
        public int JourneyIndex;
        public int FinalBody = -1;

        /// <summary>While on a lane: which, and the fixed ramp offsets (km, ecliptic) from each end's planet.</summary>
        public LaneHop LaneLeg;
        public DVec3 LaneFromOffset, LaneToOffset;
        public double LaneLengthKm;

        /// <summary>The star this ride swings clear of and its curve's shape, fixed when it joined the lane.</summary>
        public int LaneStar = -1, LaneShape = -1;

        /// <summary>Where along the lane this leg starts and stops (part-way, for a lurk or a resumed ride).</summary>
        public double LaneStartProgress, LaneEndProgress = 1;

        /// <summary>
        /// Off-lane station: the distance fraction along <see cref="LaneLeg"/> and the sideways offset
        /// (km) from the lane line, for a ship lurking in ambush or drifting after being pulled out.
        /// </summary>
        public double LaneHold;
        public DVec3 LaneHoldSide;

        /// <summary>The rest of this ride surged through at <see cref="Fleet.SurgeFactor"/> times the lane's pace (a pilot boosting
        /// through an interdiction field, U.SPC-T42): once a ride, and a surging hull may outrun a pulse.</summary>
        public bool LaneSurged;

        /// <summary>A lurk planned for this trip: stop part-way along this lane and wait.</summary>
        public Lane LurkLane;
        public double LurkAt, LurkDays;

        /// <summary>The ride was stalled by a misaligned lane (U.SPC-T572): it waits off the lane in <see cref="ShipPhase.Lurk"/>
        /// but is no raider lying in wait. Cleared when the ride resumes or a new one starts.</summary>
        public bool Stalled;

        /// <summary>Lying in wait beside a lane on purpose (not stalled by a worn lane).</summary>
        public bool Ambushing => Phase == ShipPhase.Lurk && !Stalled;

        /// <summary>Boarded and left adrift (cargo taken, crew gone): no longer flies, burns out of the list later.</summary>
        public bool Derelict;
        public double DerelictSinceJd;

        /// <summary>Who stripped it and left it adrift (raiders some of whom may still be aboard).</summary>
        public Faction DerelictBy;

        /// <summary>What a salvager still finds aboard a derelict: what the raiders left of the cargo, and
        /// the ship's own stores. The player takes it crate by crate (see <see cref="SalvageSession"/>).</summary>
        public readonly List<SalvageStack> Salvage = new List<SalvageStack>();

        /// <summary>
        /// What an ore tug has latched onto in its claws and is pushing (U.SPC-T498): a captured asteroid or a block of comet
        /// ice, drawn ahead of its push plate and flown at the gentler acceleration its mass leaves (<see cref="AccelG"/>).
        /// Null when it is running empty. Not in the hold: <see cref="MassTonnes"/> and the propellant reckoning leave it out.
        /// </summary>
        public CapturedRock Tow;

        /// <summary>
        /// The colony voyage this hull is fitted out for (U.SPC-T524, <see cref="ShipRole.Colonist"/>): the colonists and starter
        /// goods aboard and where they are bound. The same record all the way, through the gates too; null for any other hull.
        /// </summary>
        public ColonyShip Colony;

        /// <summary>Tonnes latched on (<see cref="Tow"/>), or 0.</summary>
        public double TowTonnes => Tow == null ? 0 : Math.Max(0, Tow.Tonnes);

        /// <summary>
        /// An ore tug pushing its load in to the body it is at from a spot out in that body's neighbourhood, where it latched
        /// on (a load for a refinery at the field's own body): the trip is a real one, though it starts and ends at one body.
        /// Cleared when it arrives.
        /// </summary>
        public bool Homing;

        /// <summary>
        /// The drive's sustained acceleration now, g: its class's (<see cref="ShipClassSpec.AccelG"/>), cut by whatever it has
        /// latched on to the share its thrust can still move -- the tug's own mass over the tug and its load together.
        /// </summary>
        public double AccelG
        {
            get
            {
                double load = TowTonnes;
                if (load <= 0) return Spec.AccelG;
                double own = Math.Max(1, DryMassTonnes + PropellantCapacity);
                return Spec.AccelG * own / (own + load);
            }
        }

        /// <summary>Hold size in tonnes (0 for warships).</summary>
        public double CapacityTonnes;
        public bool HasCargo;
        public Good CargoGood;
        public double CargoTonnes;

        /// <summary>What the cargo cost, and the settlement it was bought for.</summary>
        public double CargoCost;
        public Settlement CargoDestination;

        /// <summary>A supply-contract bonus the destination's owner pays on delivery, credits.</summary>
        public double CargoBonus;

        /// <summary>Who offered <see cref="CargoBonus"/>: the destination's owner when the load was booked, the only one who pays it.</summary>
        public Faction CargoBonusPayer;

        /// <summary>The player's personal grid-based cargo hold (<see cref="CargoHold"/>) -- separate
        /// from the single-slot cargo fields above that the AI trade loop owns, and built only once
        /// asked for, since only the ship currently being piloted ever needs one.</summary>
        public CargoHold PersonalHold => personalHold ??= EmptyHold(Loadout == null ? 0 : Loadout.ExtraHoldRows);
        CargoHold personalHold;

        CargoHold EmptyHold(int extraRows)
        {
            var spec = CargoHoldSpec.Of(Spec.Class);
            return new CargoHold(spec.Width, Math.Max(1, spec.Height + extraRows));
        }

        /// <summary>The grid hold rebuilt with <paramref name="extraRows"/> rows of racks (U.SPC-T65), every crate
        /// where it was (or, if its cells are gone, wherever it first fits); null if they will not all fit.</summary>
        internal CargoHold ResizedHold(int extraRows)
        {
            var hold = EmptyHold(extraRows);
            if (personalHold == null) return hold;
            var moved = new List<CargoItem>();
            foreach (var item in personalHold.Items)
                if (hold.Add(item.Good, item.X, item.Y, item.Rotated) == null) moved.Add(item);
            foreach (var item in moved)
                if (hold.AddFirstFit(item.Good) == null) return null;
            return hold;
        }

        internal void SetHold(CargoHold hold) => personalHold = hold;

        /// <summary>What a yard has fitted into this hull's slots (U.SPC-T65), or null while it is as built.</summary>
        public Loadout Loadout;

        /// <summary>Prisoners locked in the brig, and the bounty and ransom they will fetch handed over in port.</summary>
        public double Prisoners, PrisonerBounty;

        /// <summary>Named outlaw captains locked in the brig (U.SPC-T63), each taking a berth, handed over to a lawful
        /// port of a faction that posted a bounty on them for the full price alive (see <see cref="Outlaws.Deliver"/>).</summary>
        public int CaptainsHeld;

        /// <summary>Running trade profit, credits.</summary>
        public double Credits;

        /// <summary>The trading company that owns this hull (U.SPC-T99), or null for one its faction runs itself.</summary>
        public Company Company;

        /// <summary>How much of <see cref="Credits"/> its company has already booked.</summary>
        public double CompanyBookedCredits;

        /// <summary>Where the cargo in the hold was loaded, when, and the running profit then: a company reckons each
        /// run from these (see <see cref="Companies.OnSold"/>).</summary>
        public Settlement LoadedFrom;
        public double LoadedJd, LoadedCredits;

        /// <summary>Set alongside <see cref="Destroyed"/> (and <see cref="GateDeparted"/>, so nothing draws a wreck) when
        /// its company sold it to the breakers.</summary>
        public bool Scrapped;

        /// <summary>Running dark (U.SPC-T66): transponder off, active systems down, hull heat damped, so its signature
        /// is a fraction of what it was (see <see cref="Signatures"/>). A burn still shows.</summary>
        public bool SilentRunning;

        // ---------------------------------------------------------------- propulsion (see Propulsion)

        /// <summary>The design whose drive and tanks this hull has (its combat design, or its class's first).</summary>
        CombatSpec Design => Combat?.Spec ?? CombatSpec.Of(Spec.Class);

        /// <summary>Mass with empty tanks and an empty hold, tonnes.</summary>
        public double DryMassTonnes => Design.DryMassTonnes;

        /// <summary>What the tanks hold when full, tonnes of reaction mass.</summary>
        public double PropellantCapacity => Design.PropellantTonnes;

        /// <summary>The speed the drive throws reaction mass out at, km/s.</summary>
        public double ExhaustVelocityKmS => Math.Max(1, Design.ExhaustVelocityKmS);

        /// <summary>Reaction mass in the tanks, tonnes (a new hull leaves its yard full).</summary>
        public double Propellant
        {
            get => propellant ?? PropellantCapacity;
            set => propellant = Math.Max(0, Math.Min(PropellantCapacity, value));
        }
        double? propellant;

        /// <summary>How full the tanks are, 0..1.</summary>
        public double PropellantFraction => PropellantCapacity > 0 ? Propellant / PropellantCapacity : 0;

        /// <summary>Tonnes in the holds: the trade cargo and whatever is in the personal grid hold.</summary>
        public double CarriedTonnes
        {
            get
            {
                double t = HasCargo ? CargoTonnes : 0;
                if (personalHold != null) foreach (var item in personalHold.Items) t += item.Tonnes;
                return t;
            }
        }

        /// <summary>All-up mass now: hull, tanks and holds, tonnes.</summary>
        public double MassTonnes => DryMassTonnes + Propellant + CarriedTonnes;

        /// <summary>The delta-v (km/s) left in the tanks at the ship's present mass (Tsiolkovsky).</summary>
        public double DeltaVBudgetKmS => Propulsion.DeltaV(ExhaustVelocityKmS, MassTonnes, MassTonnes - Propellant);

        /// <summary>When its FTL drive is cool enough to charge again after its last drop-out (U.SPC-T418, see <see cref="FtlRules"/>).</summary>
        public double FtlCoolUntilJd;

        /// <summary>Ran dry in flight: drifting dead where the tanks gave out, calling for a tanker.</summary>
        public bool OutOfPropellant;
        public double OutOfPropellantSinceJd;

        /// <summary>The drive cut out part way through a transit: it drifts in open space, not at the world its
        /// fixed point is kept by, so when the tanker comes it still has the way to that world to fly.</summary>
        public bool StrandedInFlight;

        /// <summary>The propellant the current transit was charged when it was planned (refunded in part on a diversion).</summary>
        public double LegPropellant;

        /// <summary>When the current transit runs dry part way (its tanks could not cover it all), or NaN.</summary>
        public double StrandJd = double.NaN;

        /// <summary>Lost in battle: kept in the list (ids are indices) but no longer simulated or drawn.</summary>
        public bool Destroyed;

        /// <summary>Set alongside <see cref="Destroyed"/> when a jump gate retires this hull here
        /// because it carried on into another system's fleet (see <see cref="GateTransit"/>) rather
        /// than because it was actually lost -- so the Runtime draws nothing for it instead of a wreck.</summary>
        public bool GateDeparted;

        /// <summary>The interstellar voyage this ship (a flagship, for a group) is on, or null; see <see cref="InterstellarTraffic"/>.</summary>
        public Voyage Voyage;

        /// <summary>
        /// Bound for one of this system's jump gates: the gate's fixed position from the system root
        /// (km). Its next trip is a burn to that point rather than to a world; null otherwise.
        /// </summary>
        public DVec3? GateKm;

        /// <summary>
        /// A raider group lying in wait off a jump gate (U.SPC-T94): the point on the gate's approach (km from the
        /// system root) it holds, where traffic bunches up going in and coming out, until <see cref="StakeoutUntilJd"/>.
        /// Null otherwise. See <see cref="War.GateAmbushes"/>.
        /// </summary>
        public DVec3? StakeoutKm;
        public double StakeoutUntilJd;

        /// <summary>Plunder aboard, tonnes of each good (indexed by <see cref="Good"/>): what a raider band took and is
        /// carrying home, or what a clan's smuggler is running to a black market (U.SPC-T94). Null when there is none.</summary>
        public double[] Plunder;

        /// <summary>Tonnes of plunder aboard.</summary>
        public double PlunderTonnes
        {
            get
            {
                double t = 0;
                if (Plunder != null)
                    foreach (var x in Plunder) t += x;
                return t;
            }
        }

        /// <summary>A pirate clan's smuggling hull (U.SPC-T94): it carries loot to black markets and lies at its haven between runs,
        /// never raiding (see <see cref="PirateClans"/>).</summary>
        public bool Runner;

        /// <summary>
        /// One of a navy's standing patrol wings (U.SPC-T371, see <see cref="PatrolWings"/>): light and heavy fighters, or a
        /// destroyer with its fighters, flying a beat of their own ports. A wing keeps to its beat (it is never merged into a
        /// battle group, sent through a gate or counted against the yards' warship cap) and fights only where its orders put it.
        /// </summary>
        public bool PatrolWing;

        /// <summary>
        /// A navy supply convoy (U.SPC-T375, see <see cref="PatrolWings"/>): one of its own freighters carrying naval
        /// stores round a run of its ports under a fighter escort. Flies as a <see cref="PatrolWing"/> (never merged,
        /// sent away or counted as a warship) but never turns aside to defend or hunt: it goes on round its run, and to a
        /// yard when hurt.
        /// </summary>
        public bool Convoy;

        /// <summary>
        /// The jump gate this hull keeps watch at as a gate picket (U.SPC-T480, see <see cref="GatePickets"/>), or null. A
        /// picket flies as a <see cref="PatrolWing"/> (never merged, sent away or counted as a warship) and holds its post off
        /// the gate's exit (<see cref="PicketPostKm"/>), warning its navy of what comes through.
        /// </summary>
        public JumpGate PicketGate;

        /// <summary>A gate picket's post, km off the system root: just inside its gate's exit.</summary>
        public DVec3 PicketPostKm;

        /// <summary>
        /// The body a corvette is stationed at for good (U.SPC-T476, see <see cref="Corvettes"/>): one of its navy's ports, a
        /// planet, moon or station, or -1 for any other hull. A corvette flies as a <see cref="PatrolWing"/> (never merged,
        /// sent away or counted as a warship) on a beat of that port and the ports down the hyper lanes from it
        /// (<see cref="Route"/>), and never leaves its home system.
        /// </summary>
        public int CorvetteStation = -1;

        /// <summary>What sort of local craft this is when it is <see cref="ShipRole.Commuter"/> traffic (U.SPC-T375), or
        /// <see cref="CivilCraft.None"/>.</summary>
        public CivilCraft Civil;

        /// <summary>Hull, shields and hardpoints, carried between battles.</summary>
        public ShipCombat Combat;

        /// <summary>One of a carrier's craft (U.SPC-T526, see <see cref="CarrierWings"/>): its class, its berth and its pilot.
        /// Such a hull is in no fleet's list: it lives docked in its carrier's <see cref="ShipCombat.Hangar"/> and goes where
        /// the carrier goes, and flies only when it launches into a fight. Null for every other hull.</summary>
        public WingCraft Craft;

        /// <summary>A free ship's next trip is a hop out to open space near the world it is at, not to another world.</summary>
        public bool Wander;

        /// <summary>The docking ring a merchant is landing through (or has landed through), and where it waits to be cleared.</summary>
        public DockingRing Ring;
        public ParkingOrbit HoldPoint;

        /// <summary>On the ground or on its way down or up (not in orbit, cannot be attacked).</summary>
        public bool Planetside => Phase == ShipPhase.Descent || Phase == ShipPhase.Landed || Phase == ShipPhase.Ascent;

        /// <summary>The battle this ship is fighting in, or null.</summary>
        public Battle Battle;

        public bool IsEscort => Leader != null;

        /// <summary>The ship whose state this ship shares (itself unless it is an escort).</summary>
        public Ship Controller => Leader ?? this;

        /// <summary>
        /// Clears every scheduling and per-system field a hull carries only while it is in one system's own
        /// simulation -- orbit, route, journey, lane state, booked cargo, the market it was loaded for -- so the
        /// same object (never a copy, U.SPC-T453: this replaces the old CloneForGateArrival) arrives at a jump
        /// gate's far end, or at the edge of a well under its own cruise drive, as free an agent as the old copy
        /// always did. Called right at arrival, immediately before the caller sets Orbit, Phase, the PhaseJds,
        /// DestinationBody, Role and Ports fresh for wherever it actually lands -- never at departure, since a hull
        /// sitting in a gate's passage or out on a cruise is not yet anywhere new and must keep a valid Orbit in the
        /// meantime (whoever still has eyes on it reads where it last was, same as a retired husk always could).
        /// Leaves <see cref="Leader"/>, <see cref="FormationSlot"/> and <see cref="Voyage"/> alone: an escort's tie to
        /// its leader (the same object, still itself) survives the crossing, and a voyage still under way outlives
        /// this one call, read fresh by whatever the caller does with it afterward. Untouched too: name, hull class,
        /// combat state (hull, shields, hardpoints), cargo and the personal hold, credits, propellant, its company --
        /// everything that makes it feel like the same ship to the player.
        /// </summary>
        internal void ResetForArrival()
        {
            BandId = -1;
            Route = null;
            RouteIndex = 0;
            Phase = ShipPhase.Parked;
            Orbit = null;
            DepartureOrbit = null;
            OriginBody = -1;
            DestinationBody = -1;
            PhaseStartJd = 0;
            PhaseEndJd = 0;
            FrameBody = 0;
            DepartureVelocity = DVec3.Zero;
            ArrivalVelocity = DVec3.Zero;
            Path = null;
            PathCumulative = null;
            PathLength = 0;
            AccelKmPerDay2 = 0;
            Conic = null;
            GravityLossKmS = 0;
            PlannedConicDepartJd = double.NaN;
            PlannedConicDays = 0;
            PlannedDirect = false;
            Journey = null;
            JourneyIndex = 0;
            FinalBody = -1;
            LaneLeg = default;
            LaneFromOffset = DVec3.Zero;
            LaneToOffset = DVec3.Zero;
            LaneLengthKm = 0;
            LaneStar = -1;
            LaneShape = -1;
            LaneStartProgress = 0;
            LaneEndProgress = 1;
            LaneHold = 0;
            LaneHoldSide = DVec3.Zero;
            LaneSurged = false;
            LurkLane = null;
            LurkAt = 0;
            LurkDays = 0;
            Stalled = false;
            // Not Destroyed or Derelict: a hull crossing never carries those into this call (a cruise drops a lost
            // or boarded hull from its group on the spot, see DeepSpace.Destroy, and Derelict is a system-combat
            // outcome a gate's passage or a cruise never produces), so clearing them here would only ever be able to
            // revive one, never settle it.
            Salvage.Clear();
            // The market it was loaded for (and the contract bonus it posted) belongs to the origin system's
            // economy, so the goods arrive unbooked. Captains in the brig (CaptainsHeld) are not: they are aboard,
            // same as the hull's own crew, and cross with it.
            CargoDestination = null;
            CargoBonus = 0;
            CargoBonusPayer = null;
            LoadedFrom = null;
            LoadedJd = 0;
            LoadedCredits = 0;
            Scrapped = false;
            OutOfPropellant = false;
            OutOfPropellantSinceJd = 0;
            StrandedInFlight = false;
            LegPropellant = 0;
            StrandJd = double.NaN;
            GateDeparted = false;
            GateKm = null;
            StakeoutKm = null;
            StakeoutUntilJd = 0;
            PatrolWing = false;
            Convoy = false;
            CorvetteStation = -1;
            Civil = CivilCraft.None;
            Wander = false;
            Homing = false;
            Ring = null;
            HoldPoint = null;
            Battle = null;
        }

        /// <summary>
        /// What a ship leaving its system leaves in its place in that fleet's list (U.SPC-T417, see <see cref="Fleet.LetGo"/>):
        /// a marker with its id, name and class, gone (<see cref="Destroyed"/> and <see cref="GateDeparted"/>, so nothing
        /// draws a wreck for it), so every other hull's id there stays put while the ship itself, the same object, goes on.
        /// </summary>
        internal Ship Tombstone()
        {
            var t = (Ship)MemberwiseClone();
            // An escort flies its leader's motion (Controller) and has none of its own (it may never have had an Orbit):
            // its marker, leaderless, takes the group's motion as War.Detach gives an escort leaving its group, so it reads
            // where the hull last was rather than parked with no orbit (Fleet.StateOf threw on it every frame, US-T59).
            if (Leader != null) t.TakeMotionOf(Leader);
            t.Destroyed = true;
            t.GateDeparted = true;
            t.Leader = null;
            t.Battle = null;
            t.Voyage = null;
            t.GateKm = null;
            return t;
        }

        /// <summary>
        /// The motion War.Detach hands an escort leaving its group: its leader's, field for field. For a hull gone from the
        /// fleet as an escort (its marker, or a hired fighter paid off), so it reads where it last was (US-T59).
        /// </summary>
        internal void TakeMotionOf(Ship lead)
        {
            Phase = lead.Phase;
            Orbit = lead.Orbit;
            DepartureOrbit = lead.DepartureOrbit;
            DestinationBody = lead.DestinationBody;
            OriginBody = lead.OriginBody;
            PhaseStartJd = lead.PhaseStartJd;
            PhaseEndJd = lead.PhaseEndJd;
            FrameBody = lead.FrameBody;
            Path = lead.Path;
            PathCumulative = lead.PathCumulative;
            PathLength = lead.PathLength;
            AccelKmPerDay2 = lead.AccelKmPerDay2;
            Conic = lead.Conic;
            GravityLossKmS = lead.GravityLossKmS;
            DepartureVelocity = lead.DepartureVelocity;
            ArrivalVelocity = lead.ArrivalVelocity;
            Journey = lead.Journey;
            JourneyIndex = lead.JourneyIndex;
            FinalBody = lead.FinalBody;
            LaneLeg = lead.LaneLeg;
            LaneFromOffset = lead.LaneFromOffset;
            LaneToOffset = lead.LaneToOffset;
            LaneLengthKm = lead.LaneLengthKm;
            LaneStartProgress = lead.LaneStartProgress;
            LaneEndProgress = lead.LaneEndProgress;
            LaneHold = lead.LaneHold;
            LaneHoldSide = lead.LaneHoldSide;
            LaneStar = lead.LaneStar;
            LaneShape = lead.LaneShape;
            Ring = lead.Ring;
            HoldPoint = lead.HoldPoint;
        }

        public override string ToString() => Name;
    }

    /// <summary>Kinematic snapshot of a ship at an instant.</summary>
    public readonly struct ShipState
    {
        /// <summary>Heliocentric ecliptic position, km.</summary>
        public readonly DVec3 HelioKm;

        /// <summary>Where the nose points (ecliptic, unit) -- prograde throughout a transit, including
        /// its braking half (no visual 180 to burn retrograde; see <see cref="Fleet.BaseStateOf"/>).</summary>
        public readonly DVec3 Facing;

        public readonly double SpeedKmS;
        public readonly bool Burning;

        /// <summary>Fraction of the transit distance covered (0..1); 0 while parked.</summary>
        public readonly double Progress;

        /// <summary>Entry heating, 0..1: the plasma sheath of a ship descending through an atmosphere.</summary>
        public readonly float Heat;

        public ShipState(DVec3 helio, DVec3 facing, double speed, bool burning, double progress, float heat = 0)
        {
            HelioKm = helio;
            Facing = facing;
            SpeedKmS = speed;
            Burning = burning;
            Progress = progress;
            Heat = heat;
        }
    }
}
