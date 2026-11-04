using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>A part a yard builds into a hull: what it is called, what it is built from and what it weighs. Both a
    /// hardpoint's module (<see cref="ModuleSpec"/>) and a facility (<see cref="FacilityKind"/>) are one.</summary>
    public interface IPartSpec
    {
        string Name { get; }

        /// <summary>The bill of materials, tonnes, for a hull of scale 1 (a destroyer) if the part grows with the hull.</summary>
        (Good good, double tonnes)[] Bill { get; }

        /// <summary>Materials for a hull of <paramref name="scale"/>, tonnes.</summary>
        double Tonnes(int i, double scale);

        /// <summary>Installed mass on a hull of <paramref name="scale"/>, tonnes.</summary>
        double Mass(double scale);
    }

    /// <summary>
    /// One kind of facility (U.SPC-T470): a thing inside the body of a ship, not on its skin. A facility has no
    /// position on the hull and no firing arc, only a size: the share of the hull's internal space it takes
    /// (<see cref="Loadout.InternalSpace"/>), in sections (one section is what an internal bay was before facilities).
    /// Every kind is data in <see cref="FacilityKinds"/>: its size, mass and bill of materials, which hull categories
    /// may fit it, whether a yard fits and strips it, the boarding compartment it gives the hull's interior, and what it
    /// does to the hull's fighting spec (<see cref="Effect"/>). A new facility (a marine habitat, a Breacher bay, a
    /// radiator, a drive room) is a new registered kind, never an edit to the code that fits the first.
    /// </summary>
    public sealed class FacilityKind : IPartSpec
    {
        /// <summary>A stable name for saves and bindings ("armour-plating", "hangar").</summary>
        public readonly string Id;

        public readonly string Name;

        /// <summary>Sections of internal space it takes. A hull whose free space is smaller cannot fit it at all.</summary>
        public readonly double Size;

        /// <summary>Installed mass, tonnes (per unit of hull scale if <see cref="Scaled"/>).</summary>
        public readonly double MassTonnes;

        /// <summary>Whether the bill and mass grow with the hull (<see cref="Loadout.Scale"/>) or are one fixed fitting.</summary>
        public readonly bool Scaled;

        public readonly (Good good, double tonnes)[] Bill;

        /// <summary>The hull categories that may fit it (null: any).</summary>
        public readonly HullCategory[] Categories;

        /// <summary>Whether a yard fits it into free space and strips it out again. False for the hull's structure as
        /// designed (its bridge, engineering, quarters, hangar and holds), which comes with the frame.</summary>
        public readonly bool Fittable;

        /// <summary>The boarding compartment it is in the hull's interior (<see cref="Interior.For(IReadOnlyList{FacilitySite}, double, double)"/>),
        /// or null for one that is not a space of its own (plate, racks, a scanner).</summary>
        public readonly CompartmentKind? Compartment;

        /// <summary>The module it is, for the facilities that were internal-bay modules before (U.SPC-T65): their effects
        /// are still read by <see cref="Loadout.Count(ModuleKind)"/> (plate in <see cref="Loadout.Build"/>, racks in the
        /// holds, scanners in <see cref="SensorSuite"/>, a brig's berths).</summary>
        public readonly ModuleKind? Module;

        /// <summary>
        /// What one of it does to the hull's fighting spec as <see cref="Loadout.Build"/> makes it, beyond its mass:
        /// called with +1 for each fitted over the design and -1 for each of the design's own taken out. Null for one
        /// whose effect is read elsewhere (by <see cref="Module"/>, or by the system that uses it).
        /// </summary>
        public readonly Action<CombatSpec, Loadout, int> Effect;

        /// <summary>Marines it quarters (U.SPC-T465), per unit of hull scale if <see cref="Scaled"/>: what one adds to the
        /// hull's marine complement (<see cref="CombatSpec.Marines"/>). A site may set its own (<see cref="FacilitySite.Berths"/>).</summary>
        public readonly double Berths;

        /// <summary>Tonnes of trade cargo one of it carries (U.SPC-T482): a truss freighter's cargo pod is its hold, so its
        /// capacity is the pods it carries (<see cref="Loadout.CapacityTonnes"/>). Zero for most.</summary>
        public readonly double CargoTonnes;

        /// <summary>Rows of the grid hold (<see cref="CargoHold"/>) one of it gives (a pod's share of the hold).</summary>
        public readonly int HoldRows;

        /// <summary>The ship classes that may fit it (null: any class its <see cref="Categories"/> allow), so a pod clamps
        /// only to a hull that has a truss (U.SPC-T482).</summary>
        public readonly ICollection<ShipClass> Classes;

        public FacilityKind(string id, string name, double size, double massTonnes, bool scaled, (Good good, double tonnes)[] bill,
                            HullCategory[] categories = null, bool fittable = true, CompartmentKind? compartment = null,
                            ModuleKind? module = null, Action<CombatSpec, Loadout, int> effect = null, double berths = 0,
                            double cargoTonnes = 0, int holdRows = 0, ICollection<ShipClass> classes = null)
        {
            Berths = Math.Max(0, berths);
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            Size = Math.Max(0, size);
            MassTonnes = massTonnes;
            Scaled = scaled;
            Bill = bill ?? Array.Empty<(Good, double)>();
            Categories = categories;
            Fittable = fittable;
            Compartment = compartment;
            Module = module;
            Effect = effect;
            CargoTonnes = Math.Max(0, cargoTonnes);
            HoldRows = Math.Max(0, holdRows);
            Classes = classes;
        }

        /// <summary>A facility that is one of the former internal-bay modules, with that module's name, bill and mass.</summary>
        public static FacilityKind OfModule(string id, ModuleKind module, double size, HullCategory[] categories = null)
        {
            var m = ModuleSpec.Of(module);
            return new FacilityKind(id, m.Name, size, m.MassTonnes, m.Scaled, m.Bill, categories, true, null, module);
        }

        string IPartSpec.Name => Name;
        (Good good, double tonnes)[] IPartSpec.Bill => Bill;

        public double Tonnes(int i, double scale) => Bill[i].tonnes * (Scaled ? scale : 1);

        public double Mass(double scale) => MassTonnes * (Scaled ? scale : 1);

        /// <summary>Whether a hull of this category may carry it at all.</summary>
        public bool AllowedOn(HullCategory category)
        {
            if (Categories == null) return true;
            foreach (var c in Categories) if (c == category) return true;
            return false;
        }

        /// <summary>Which ship classes within its categories may fit it (null: any of them), for a facility that only some
        /// hulls of a category take: the FTL drive goes in a command cruiser but not a plain cruiser, both warships
        /// (U.SPC-T472). Asked with the hull's class, or null for a station or satellite.</summary>
        public Func<ShipClass?, bool> ClassFilter { get; set; }

        /// <summary>The id of a facility the hull must already carry to fit this one, or null (U.SPC-T466): a Breacher bay
        /// is fitted only where a marine habitat supplies its teams, and that habitat cannot be stripped from under it.</summary>
        public string Requires { get; set; }

        /// <summary>The reactor power one draws, MW (U.SPC-T521): a hull fits it only with that much of its reactor's reserve
        /// for fittings still free (<see cref="Loadout.FreePowerMw"/>). Zero for most (a projector brings its own reactor).</summary>
        public double PowerMw { get; set; }

        /// <summary>The crew one takes to man, beyond the hull's running crew (U.SPC-T521): a hull fits it only with that many
        /// hands still to spare (<see cref="Loadout.FreeCrew"/>). Zero for most.</summary>
        public double Crew { get; set; }

        /// <summary>Whether a hull of <paramref name="category"/> and <paramref name="cls"/> may carry it at all: not one the
        /// class bars (<see cref="FacilityLayouts.Bars"/>), and, for a kind with <see cref="Classes"/>, only those.</summary>
        public bool AllowedOn(HullCategory category, ShipClass? cls)
        {
            if (!AllowedOn(category) || (ClassFilter != null && !ClassFilter(cls))) return false;
            if (cls is ShipClass c && FacilityLayouts.Bars(c, Id)) return false;
            return Classes == null || (cls is ShipClass k && Classes.Contains(k));
        }
    }

    /// <summary>
    /// The registry of facility kinds (U.SPC-T470). The hull's structure (bridge, engineering, quarters, hangar deck,
    /// holds, batteries' magazines, the bow and midships sections, a Breacher round's troop compartment) and the four fittable
    /// facilities that were internal-bay modules (armour plate, cargo racks, a scanner suite, a brig) are registered
    /// here, with the marine habitat (U.SPC-T465, <see cref="MarineHabitats"/>) and the Breacher bay that fires Breacher
    /// rounds (U.SPC-T466, U.SPC-T521, <see cref="BreacherBays"/>); new kinds plug in with <see cref="Register"/>.
    /// </summary>
    public static class FacilityKinds
    {
        static readonly List<FacilityKind> All = new List<FacilityKind>();
        static readonly Dictionary<string, FacilityKind> ById = new Dictionary<string, FacilityKind>(StringComparer.OrdinalIgnoreCase);

        // The structure every hull is built around (not fittable: it comes with the frame).
        public const string Bridge = "bridge", Engineering = "engineering", Quarters = "quarters", Hangar = "hangar", Hold = "hold",
                            Battery = "battery", Bow = "bow", Midships = "midships", TroopCompartment = "troop-compartment";

        // What a yard fits into free space.
        public const string ArmourPlating = "armour-plating", CargoRacks = "cargo-racks", Scanner = "scanner-suite", Brig = "brig",
                            MarineHabitat = "marine-habitat";

        /// <summary>The firing room that launches Breacher rounds (U.SPC-T466, U.SPC-T521, see <see cref="BreacherBays"/>): any
        /// ship's to fit that has the space, the reactor power and the crew for it, and only beside a marine habitat to
        /// supply its teams.</summary>
        public const string BreacherBay = "breacher-bay";

        /// <summary>The Breacher bay's id before U.SPC-T521 renamed it; <see cref="Of"/> still finds the bay by it.</summary>
        public const string OldBreacherBayId = "assault-bay";

        /// <summary>The hulls that may fit a Breacher bay (U.SPC-T521): any ship that can quarter marines, navy, pirate or a
        /// merchant's escort, so long as it has the space, power and crew for it; never a station.</summary>
        public static readonly HullCategory[] BreacherHulls =
        {
            HullCategory.Freighter, HullCategory.Warship, HullCategory.Battleship, HullCategory.Carrier, HullCategory.Support,
        };

        /// <summary>The hulls big enough for a marine habitat (U.SPC-T465): every one but fighters, small craft and
        /// satellites, whose crews repel boarders alone.</summary>
        public static readonly HullCategory[] MarineHulls =
        {
            HullCategory.Freighter, HullCategory.Warship, HullCategory.Battleship, HullCategory.Carrier, HullCategory.Station, HullCategory.Support,
        };

        // What makes a hull a fleet's command ship (U.SPC-T477, see FleetCoordination): a capital's to fit.
        public const string SensorArray = "sensor-array", CommsSuite = "comms-suite", TargetingComputers = "targeting-computers";

        static readonly HullCategory[] Capitals = { HullCategory.Warship, HullCategory.Battleship, HullCategory.Carrier };

        /// <summary>
        /// A self-contained FTL drive (U.SPC-T472): what lets a hull whose class carries one only when its design fits it
        /// (<see cref="FtlAccess.Fitted"/>: battleships, and command cruisers when they are a class) cross between the stars
        /// under its own power (<see cref="CruiseDrives.Of(Ship)"/>). The stock battleship is designed with one; a yard can
        /// strip it out for two more sections of plate or racks, and the hull then travels by the gates like a cruiser.
        /// </summary>
        public const string FtlDrive = "ftl-drive";

        /// <summary>
        /// A spinal power plant (U.SPC-T490, see <see cref="SpinalMounts"/>): the large reactor and capacitor banks a
        /// ship-length spinal mount draws on, three sections deep in the hull. Only a hull built around spinal mounts may fit
        /// one; the stock battleship is designed with one for each of its two mounts, and with fewer plants than standing
        /// mounts they recharge slowly (<see cref="SpinalMounts.ChargeRate"/>).
        /// </summary>
        public const string SpinalPlant = "spinal-plant";

        // What clamps along a truss freighter's spine (U.SPC-T482, see BulkFreighters).
        public const string CargoPod = "cargo-pod", FuelSphere = "fuel-sphere";

        /// <summary>
        /// An interdiction field projector (U.SPC-T507, see <see cref="LaneInterdictors"/>): the heavy equipment that disrupts
        /// a hyperlane's magnetic field and throws the ships riding past out of it, with the reactor that feeds it, two
        /// sections deep. Any warship, capital or freighter hull big enough to carry it and its reactor load
        /// (<see cref="LaneInterdictors.CanCarry(ShipClass)"/>) may fit it; a yard fits and strips it like any other fitting,
        /// and its ring shows on whatever hull carries it.
        /// </summary>
        public const string FieldProjector = "field-projector";

        /// <summary>
        /// A siege engineering bay (U.SPC-T478, see <see cref="GateBreakers"/>): the gate breaker's engineering teams, their
        /// workshops, cutting gear, gate-control rigs and demolition charges, two sections deep. With its boarding teams they
        /// seize a jump gate intact and patch its ring. Part of the hull as designed, not a fitting, and only a hull of a
        /// gate-breaking class has one (<see cref="GateBreakers.Classes"/>).
        /// </summary>
        public const string SiegeEngineering = "siege-engineering";

        static FacilityKinds()
        {
            Register(Structure(Bridge, "Bridge", 1, CompartmentKind.Bridge));
            Register(Structure(Engineering, "Engineering", 2, CompartmentKind.Engineering));
            Register(Structure(Quarters, "Crew quarters", 2, CompartmentKind.Quarters));
            Register(Structure(Hangar, "Hangar deck", 4, CompartmentKind.Hangar));
            Register(Structure(Hold, "Hold", 3, CompartmentKind.Hold));
            Register(Structure(Battery, "Battery magazine", 2, CompartmentKind.Battery));
            Register(Structure(Bow, "Bow section", 1, CompartmentKind.Bow));
            Register(Structure(Midships, "Midships", 1, CompartmentKind.Midships));
            // The team's seats inside a Breacher round (U.SPC-T466): what boarders storm out of once it has drilled in.
            Register(Structure(TroopCompartment, "Troop compartment", 2, CompartmentKind.BreacherBay));

            Register(FacilityKind.OfModule(ArmourPlating, ModuleKind.ArmourPlating, 1));
            Register(FacilityKind.OfModule(CargoRacks, ModuleKind.CargoRack, 1));
            Register(FacilityKind.OfModule(Scanner, ModuleKind.Scanner, 1));
            Register(FacilityKind.OfModule(Brig, ModuleKind.Brig, 1));

            // The marines' barracks (U.SPC-T465): berths, a drill deck and an armoury, one section, growing with the hull
            // (fifty marines on a destroyer, some two hundred on a carrier). See MarineHabitats.
            Register(new FacilityKind(MarineHabitat, "Marine habitat", 1, 220, true,
                new[] { (Good.Alloys, 70.0), (Good.Machinery, 15.0), (Good.Electronics, 8.0) },
                MarineHulls, true, CompartmentKind.Barracks, null, null, MarineHabitats.BerthsPerScale));
            // The command ship's facilities (U.SPC-T477). Their effects are read by the systems that use them: the array by
            // SensorSuite.Of and the battle's shared picture, the suite and the computers by FleetCoordination.
            Register(new FacilityKind(SensorArray, "Sensor array", 2, 180, false,
                new[] { (Good.Electronics, 90.0), (Good.Alloys, 40.0), (Good.Machinery, 15.0) }, Capitals));
            Register(new FacilityKind(CommsSuite, "Communications suite", 1, 60, false,
                new[] { (Good.Electronics, 50.0), (Good.Alloys, 10.0) }, Capitals));
            Register(new FacilityKind(TargetingComputers, "Targeting computers", 1, 40, false,
                new[] { (Good.Electronics, 40.0), (Good.Machinery, 5.0) }, Capitals));
            // The Breacher bay (U.SPC-T466, U.SPC-T521): the breech, the loading cradle and the round magazine, two sections,
            // drawing on the reactor and manned by its own crew, so any ship with the space, power and hands may fit one. A
            // boarding fight inside the hull can be fought through it like any other space.
            Register(new FacilityKind(BreacherBay, "Breacher bay", 2, 400, false,
                new[] { (Good.Alloys, 160.0), (Good.Machinery, 60.0), (Good.Electronics, 20.0) },
                BreacherHulls, true, CompartmentKind.BreacherBay)
            {
                Requires = MarineHabitat,
                PowerMw = BreacherBays.PowerMw,
                Crew = BreacherBays.Crew,
            });
            // Only the classes the owner's roster lets carry a drive when fitted (battleships, command cruisers) may fit it
            // (U.SPC-T472); a carrier has its own already, a cruiser or destroyer none.
            Register(new FacilityKind(FtlDrive, "FTL drive", 2, 900, true,
                new[] { (Good.Alloys, 260.0), (Good.Machinery, 90.0), (Good.Electronics, 60.0), (Good.HeliumThree, 20.0) },
                new[] { HullCategory.Battleship, HullCategory.Warship })
            {
                ClassFilter = c => c is ShipClass k && FtlRoster.Of(k) == FtlAccess.Fitted,
            });

            // The spinal mounts' power plants (U.SPC-T490): a fusion plant and its capacitor banks, growing with the hull.
            Register(new FacilityKind(SpinalPlant, "Spinal power plant", 3, 700, true,
                new[] { (Good.Alloys, 220.0), (Good.Machinery, 120.0), (Good.Electronics, 70.0), (Good.HeliumThree, 15.0) },
                new[] { HullCategory.Battleship }, effect: (spec, l, n) => spec.SpinalPlants = Math.Max(0, spec.SpinalPlants + n))
            {
                ClassFilter = c => SpinalMounts.CanCarry(c),
            });

            // The interdiction field projector (U.SPC-T507): the emitter ring, its capacitors and the reactor that feeds them,
            // one fixed fitting whatever the hull. Only a hull with the reactor load to spare may carry it (LaneInterdictors).
            Register(new FacilityKind(FieldProjector, "Interdiction field projector", 2, LaneInterdictors.ProjectorMassTonnes, false,
                new[] { (Good.Alloys, 180.0), (Good.Machinery, 90.0), (Good.Electronics, 110.0), (Good.HeliumThree, 10.0) },
                LaneInterdictors.Hulls)
            {
                ClassFilter = c => c is ShipClass k && LaneInterdictors.CanCarry(k),
            });

            // The gate breaker's engineering teams (U.SPC-T478): structure, built into the frame, a working space boarders can
            // fight through like engineering.
            Register(new FacilityKind(SiegeEngineering, "Siege engineering bay", 2, 650, false,
                new[] { (Good.Alloys, 180.0), (Good.Machinery, 140.0), (Good.Electronics, 60.0), (Good.Munitions, 30.0) },
                new[] { HullCategory.Battleship }, false, CompartmentKind.Engineering)
            {
                ClassFilter = c => c is ShipClass k && GateBreakers.IsClass(k),
            });

            // A truss freighter's standard modules (U.SPC-T482): a pod is a share of its hold, a sphere a share of its tanks.
            Register(new FacilityKind(CargoPod, "Cargo pod", 1, BulkFreighters.PodMassTonnes, false, new[] { (Good.Alloys, 90.0), (Good.Machinery, 6.0) },
                fittable: true, cargoTonnes: BulkFreighters.PodTonnes, holdRows: BulkFreighters.PodRows, classes: BulkFreighters.TrussClasses));
            Register(new FacilityKind(FuelSphere, "Fuel sphere", 1, BulkFreighters.SphereMassTonnes, false, new[] { (Good.Alloys, 60.0), (Good.Machinery, 4.0) },
                fittable: true, effect: (spec, l, n) => spec.PropellantTonnes = Math.Max(0, spec.PropellantTonnes + n * BulkFreighters.SphereTonnes),
                classes: BulkFreighters.TrussClasses));
        }

        static FacilityKind Structure(string id, string name, double size, CompartmentKind compartment) =>
            new FacilityKind(id, name, size, 0, false, null, null, false, compartment);

        /// <summary>Adds a kind (or replaces the one with its id).</summary>
        public static void Register(FacilityKind kind)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            if (ById.TryGetValue(kind.Id, out var old)) All.Remove(old);
            ById[kind.Id] = kind;
            All.Add(kind);
        }

        public static IReadOnlyList<FacilityKind> Catalogue => All;

        public static FacilityKind Of(string id)
        {
            if (id == null) return null;
            if (ById.TryGetValue(id, out var k)) return k;
            // A hull saved before the Assault Bay became the Breacher bay (U.SPC-T521) still names it by its old id.
            return string.Equals(id, OldBreacherBayId, StringComparison.OrdinalIgnoreCase) && ById.TryGetValue(BreacherBay, out k) ? k : null;
        }

        /// <summary>The facility that is <paramref name="module"/>, or null if the module goes on a hardpoint.</summary>
        public static FacilityKind ForModule(ModuleKind module)
        {
            foreach (var k in All) if (k.Module == module) return k;
            return null;
        }
    }

    /// <summary>
    /// One facility in a hull (U.SPC-T470): its kind, the name its space goes by aboard ("Forward hold", "Cockpit"), where
    /// that space lies for the boarding interior (ship lengths along the keel, nose +0.5, and height) and how it joins the
    /// rest: on to the previous space along the keel (<see cref="Keel"/>), off another of the layout's facilities, or, for
    /// one a yard fitted later, off midships (<see cref="Amidships"/>). It has no firing arc: only a hardpoint
    /// (<see cref="Hardpoint"/>) has one.
    /// </summary>
    public sealed class FacilitySite
    {
        public readonly string Kind;
        public readonly string Name;
        public readonly double Z, Y;

        /// <summary><see cref="Keel"/>: the next space along the keel from the one before it; <see cref="Amidships"/>: a
        /// space opening off midships; otherwise the index of the facility in the same layout it branches off.</summary>
        public readonly int Link;

        public const int Keel = -1, Amidships = -2;

        /// <summary>The marines this space quarters as the hull was designed (U.SPC-T465), or null for its kind's
        /// (<see cref="FacilityKind.Berths"/>, grown with the hull): a stock design's habitat holds the complement it was drawn for.</summary>
        public readonly double? Berths;

        /// <summary>A space buried at the centre of the hull (U.SPC-T492, a warship's bridge): no pod, Breacher round or
        /// airlock opens into it, so boarders reach it only by fighting through the spaces around it.</summary>
        public bool Buried { get; set; }

        public FacilitySite(string kind, string name = null, double z = 0, double y = 0, int link = Keel, double? berths = null)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Name = name ?? FacilityKinds.Of(kind)?.Name ?? kind;
            Z = z;
            Y = y;
            Link = link;
            Berths = berths;
        }

        /// <summary>Marines it quarters on a hull of <paramref name="scale"/> (<see cref="Loadout.Scale"/>).</summary>
        public double BerthsOn(double scale)
        {
            if (Berths is double b) return Math.Max(0, b);
            var d = Def;
            return d == null ? 0 : d.Berths * (d.Scaled ? scale : 1);
        }

        /// <summary>A facility a yard fits into a hull's free space (it opens off midships, if it is a space at all).</summary>
        public static FacilitySite Fitted(FacilityKind kind) => new FacilitySite(kind.Id, kind.Name, 0, 0, Amidships);

        public FacilityKind Def => FacilityKinds.Of(Kind);

        /// <summary>The space it takes, sections.</summary>
        public double Size => Def?.Size ?? 0;
    }

    /// <summary>
    /// The facilities each hull is designed with (U.SPC-T470): its bridge, engineering, quarters, hangar, holds and the
    /// rest, laid out along the keel as the boarding interior has them, and the free space each class has for fitted
    /// facilities beyond them. A new frame can bring its own layout (<see cref="HullFrame"/>); a new class registers one
    /// with <see cref="Register"/>.
    /// </summary>
    public static class FacilityLayouts
    {
        /// <summary>A warship's bridge (U.SPC-T492): buried at the centre of the hull behind its full thickness, with no
        /// viewports (the crew sees out through its sensors and holo displays), opening only off the space it links to.</summary>
        static FacilitySite Buried(int link, string name = "Bridge") =>
            new FacilitySite(FacilityKinds.Bridge, name, 0, 0, link) { Buried = true };

        // The warships' bridges are buried at the centre of the hull, off midships (U.SPC-T492).
        // The warships' marine habitats (U.SPC-T465) are barracks branching off below their quarters (or midships), each
        // quartering the complement the class was drawn with; a freighter keeps a security post for its few guards.
        static readonly Dictionary<ShipClass, FacilitySite[]> ByClass = new Dictionary<ShipClass, FacilitySite[]>
        {
            [ShipClass.Carrier] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Bow", 0.4), new FacilitySite(FacilityKinds.Hangar, "Hangar deck", 0.15),
                new FacilitySite(FacilityKinds.Quarters, "Crew quarters", -0.02), new FacilitySite(FacilityKinds.Midships, "Midships", -0.18),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.4), Buried(3),
                new FacilitySite(FacilityKinds.MarineHabitat, "Marine barracks", -0.02, -0.06, 2, 320),
            },
            [ShipClass.Battleship] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Bow", 0.38), new FacilitySite(FacilityKinds.Battery, "Forward battery", 0.16),
                new FacilitySite(FacilityKinds.Quarters, "Crew quarters", 0.0), new FacilitySite(FacilityKinds.Midships, "Midships", -0.16),
                new FacilitySite(FacilityKinds.Battery, "Aft battery", -0.28), new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.42),
                Buried(3),
                new FacilitySite(FacilityKinds.MarineHabitat, "Marine barracks", 0.0, -0.06, 2, 260),
                // Designed with its own FTL drive (U.SPC-T472): a fitting, so a yard may strip it for the space.
                new FacilitySite(FacilityKinds.FtlDrive, "FTL drive", -0.36, 0, FacilitySite.Amidships),
                // A power plant for each of its two spinal mounts (U.SPC-T490), beside engineering.
                new FacilitySite(FacilityKinds.SpinalPlant, "Spinal power plant", -0.3, 0.02, FacilitySite.Amidships),
                new FacilitySite(FacilityKinds.SpinalPlant, "Spinal power plant", -0.3, -0.02, FacilitySite.Amidships),
            },
            [ShipClass.Cruiser] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Bow", 0.3), new FacilitySite(FacilityKinds.Quarters, "Crew quarters", 0.05),
                new FacilitySite(FacilityKinds.Midships, "Midships", -0.12), new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.38),
                Buried(2),
                new FacilitySite(FacilityKinds.MarineHabitat, "Marine barracks", 0.05, -0.05, 1, 140),
            },
            [ShipClass.Destroyer] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Bow", 0.25), new FacilitySite(FacilityKinds.Midships, "Midships", -0.05),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.36), Buried(1),
                new FacilitySite(FacilityKinds.MarineHabitat, "Marine barracks", -0.05, -0.04, 1, 50),
            },
            // The command cruiser (U.SPC-T477): the sensor array forward under its mast, the flag bridge buried at the centre with
            // the communications suite and the fire-control centre's targeting computers either side of it, and the staff's
            // quarters aft of them.
            [ShipClass.CommandCruiser] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Bow", 0.36), new FacilitySite(FacilityKinds.SensorArray, "Sensor array", 0.24),
                new FacilitySite(FacilityKinds.Quarters, "Staff quarters", 0.12), new FacilitySite(FacilityKinds.Midships, "Midships", -0.05),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.36), Buried(3, "Flag bridge"),
                new FacilitySite(FacilityKinds.CommsSuite, "Communications suite", 0.08),
                new FacilitySite(FacilityKinds.TargetingComputers, "Fire-control centre", 0.0),
                new FacilitySite(FacilityKinds.TargetingComputers, "Fire-control centre", -0.02),
                new FacilitySite(FacilityKinds.MarineHabitat, "Marine barracks", 0.12, -0.05, 2, 120),
                // Designed with its own FTL drive, as a battleship is (U.SPC-T472): a fitting a yard may strip.
                new FacilitySite(FacilityKinds.FtlDrive, "FTL drive", -0.3, 0, FacilitySite.Amidships),
            },
            // The frigate (U.SPC-T475): its torpedo magazine behind the bow launchers and the missile magazine under the racks
            // forward, the bridge buried at the centre off midships, and long engine rooms for its four big engines. No quarters for marines.
            [ShipClass.Frigate] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Bow", 0.42), new FacilitySite(FacilityKinds.Battery, "Torpedo magazine", 0.3),
                new FacilitySite(FacilityKinds.Battery, "Missile magazine", 0.12), new FacilitySite(FacilityKinds.Midships, "Midships", -0.05),
                new FacilitySite(FacilityKinds.Engineering, "Engine rooms", -0.34), Buried(3),
            },
            // The corvette (U.SPC-T476): no FTL drive room; its space goes to an impound hold for what it seizes, crew
            // quarters for weeks on patrol and the big tanks round its engineering spaces.
            [ShipClass.Corvette] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Bow", 0.38), new FacilitySite(FacilityKinds.Hold, "Impound hold", 0.18),
                new FacilitySite(FacilityKinds.Quarters, "Crew quarters", 0.0), new FacilitySite(FacilityKinds.Midships, "Midships", -0.12),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.34), new FacilitySite(FacilityKinds.Bridge, "Bridge", 0.05, 0.05, 2),
            },
            // The gate breaker (U.SPC-T478): an armoured ram bow, the plasma batteries' magnetic bottles forward, the boarding
            // teams' barracks (the biggest marine complement afloat) off the crew quarters, the shuttle deck their two boarding
            // shuttles fly from, the engineering teams' siege bay, and the bridge buried at the centre. Its FTL drive is its own
            // (FtlAccess.Own), built into engineering like a carrier's, not a fitting a yard could strip.
            [ShipClass.GateBreaker] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Ram bow", 0.4), new FacilitySite(FacilityKinds.Battery, "Plasma batteries", 0.2),
                new FacilitySite(FacilityKinds.Quarters, "Crew quarters", 0.04), new FacilitySite(FacilityKinds.Midships, "Midships", -0.12),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.4), Buried(3),
                new FacilitySite(FacilityKinds.MarineHabitat, "Boarding teams' barracks", 0.04, -0.06, 2, 340),
                new FacilitySite(FacilityKinds.Hangar, "Shuttle deck", -0.05, -0.05, 3),
                new FacilitySite(FacilityKinds.SiegeEngineering, "Siege engineering bay", -0.26, 0, 3),
                // A Breacher bay beside the barracks (U.SPC-T521): the boarding ship fires its teams into a fort or a
                // crippled hull as well as ferrying them over in its shuttles.
                new FacilitySite(FacilityKinds.BreacherBay, "Breacher bay", 0.2, -0.05, 6),
            },
            // A truss freighter (U.SPC-T482): the cockpit at the bow, the pod gantry down the spine, the pods and spheres clamped
            // along it (no space of their own: nobody walks into a pod) and the engine room astern.
            [ShipClass.Freighter] = BulkFreighters.StockLayout(),
            [ShipClass.Miner] = new[]
            {
                new FacilitySite(FacilityKinds.Bridge, "Cockpit", 0.3, 0.05), new FacilitySite(FacilityKinds.Hold, "Ore hold", 0),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.35),
            },
            // No hold: the claw gantry forward, the cockpit over it, then a long engine room for its big drive.
            [ShipClass.OreTug] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Claw gantry", 0.35), new FacilitySite(FacilityKinds.Bridge, "Cockpit", 0.15, 0.05),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.3),
            },
            // A capture tug (U.SPC-T522): an ore tug's plan on a shorter frame.
            [ShipClass.CaptureTug] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Claw gantry", 0.35), new FacilitySite(FacilityKinds.Bridge, "Cockpit", 0.15, 0.05),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.3),
            },
            // A work boat (U.SPC-T481): the bridge forward, the workshop that services a lane's rings, a big engine room.
            [ShipClass.LaneTender] = new[]
            {
                new FacilitySite(FacilityKinds.Bridge, "Bridge", 0.32, 0.05), new FacilitySite(FacilityKinds.Midships, "Ring workshop", 0.05),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.3),
            },
            // Multi-crew (U.SPC-T467): the flight deck, then the gun deck its turret crews work from.
            [ShipClass.Gunship] = new[]
            {
                new FacilitySite(FacilityKinds.Bridge, "Flight deck", 0.2, 0.03), new FacilitySite(FacilityKinds.Battery, "Gun deck", 0),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.3),
            },
            // A sensor mast with a drive (U.SPC-T480): the bridge, the sensor room under the mast, engineering.
            [ShipClass.GatePicket] = new[]
            {
                new FacilitySite(FacilityKinds.Bridge, "Bridge", 0.15, 0.04), new FacilitySite(FacilityKinds.Midships, "Sensor room", -0.08),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.3),
            },
            // A boarding shuttle (U.SPC-T486): the cockpit forward, then the troop bay its one team rides in (the docking collar
            // on its flank opens off it) and does not train in, and engineering astern.
            [ShipClass.AssaultShuttle] = new[]
            {
                new FacilitySite(FacilityKinds.Bridge, "Cockpit", 0.32, 0.04),
                new FacilitySite(FacilityKinds.TroopCompartment, "Troop bay", 0.05, 0, FacilitySite.Keel, 24),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.32),
            },
        };

        /// <summary>A small hull's: a cockpit and its engineering.</summary>
        public static readonly FacilitySite[] Small =
        {
            new FacilitySite(FacilityKinds.Bridge, "Cockpit", 0.1), new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.3),
        };

        /// <summary>A defence platform's (and any station's or satellite's without its own): a gun deck around a control
        /// room and its reactor, and the garrison's barracks off the control room.</summary>
        public static readonly FacilitySite[] Platform =
        {
            new FacilitySite(FacilityKinds.Battery, "Gun deck", 0.15, 0.15), new FacilitySite(FacilityKinds.Bridge, "Control", 0, 0),
            new FacilitySite(FacilityKinds.Engineering, "Reactor", -0.15, -0.15),
            new FacilitySite(FacilityKinds.MarineHabitat, "Garrison barracks", 0, -0.08, 1, 60),
        };

        /// <summary>A satellite's: too small for a garrison, its spaces only.</summary>
        public static readonly FacilitySite[] Satellite =
        {
            new FacilitySite(FacilityKinds.Battery, "Gun deck", 0.15, 0.15), new FacilitySite(FacilityKinds.Bridge, "Control", 0, 0),
            new FacilitySite(FacilityKinds.Engineering, "Reactor", -0.15, -0.15),
        };

        /// <summary>An orbital station's: a platform's spaces with barracks for its larger garrison.</summary>
        public static readonly FacilitySite[] Station =
        {
            new FacilitySite(FacilityKinds.Battery, "Gun deck", 0.15, 0.15), new FacilitySite(FacilityKinds.Bridge, "Control", 0, 0),
            new FacilitySite(FacilityKinds.Engineering, "Reactor", -0.15, -0.15),
            new FacilitySite(FacilityKinds.MarineHabitat, "Garrison barracks", 0, -0.08, 1, 180),
        };

        /// <summary>The facilities a ship of the class is designed with.</summary>
        public static IReadOnlyList<FacilitySite> For(ShipClass c) => ByClass.TryGetValue(c, out var l) ? l : Small;

        /// <summary>The marines a hull with these facilities quarters (U.SPC-T465): every space's berths on a hull of
        /// <paramref name="scale"/>. A hull with no habitat (or troop compartment) carries none.</summary>
        public static double Complement(IEnumerable<FacilitySite> facilities, double scale)
        {
            double n = 0;
            if (facilities != null) foreach (var s in facilities) n += s.BerthsOn(scale);
            return n;
        }

        /// <summary>Facilities of kind <paramref name="kind"/> in a layout.</summary>
        public static int CountOf(IEnumerable<FacilitySite> layout, string kind)
        {
            int n = 0;
            if (layout != null)
                foreach (var s in layout)
                    if (string.Equals(s.Kind, kind, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        /// <summary>The marine complement a class's design is drawn with: its layout's berths.</summary>
        public static double Complement(ShipClass c, CombatSpec design) => Complement(For(c), Loadout.HullScale(design));

        /// <summary>Sets (or replaces) the facilities a class is designed with.</summary>
        public static void Register(ShipClass c, FacilitySite[] layout) => ByClass[c] = layout ?? throw new ArgumentNullException(nameof(layout));

        static readonly Dictionary<ShipClass, HashSet<string>> Barred = new Dictionary<ShipClass, HashSet<string>>
        {
            // Every kilogram of a truss freighter goes to cargo: no plate, and no racks, since its pods are its hold (U.SPC-T482).
            [ShipClass.Freighter] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FacilityKinds.ArmourPlating, FacilityKinds.CargoRacks },
        };

        /// <summary>Bars (or, with <paramref name="barred"/> false, allows again) a facility kind on every hull of a class.</summary>
        public static void Bar(ShipClass c, string kind, bool barred = true)
        {
            if (!Barred.TryGetValue(c, out var set)) Barred[c] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (barred) set.Add(kind);
            else set.Remove(kind);
        }

        /// <summary>Whether hulls of <paramref name="c"/> may never fit a facility of kind <paramref name="kind"/>.</summary>
        public static bool Bars(ShipClass c, string kind) => kind != null && Barred.TryGetValue(c, out var set) && set.Contains(kind);

        /// <summary>Free internal space a hull of each class has for fitted facilities beyond its structure, sections
        /// (as many as it had internal bays before U.SPC-T470).</summary>
        public static double SpareSpace(ShipClass c)
        {
            switch (c)
            {
                case ShipClass.Carrier:
                case ShipClass.Battleship: return 6;
                case ShipClass.Cruiser:
                case ShipClass.CommandCruiser:
                // Its plate and siege bay fill most of a gate breaker (U.SPC-T478).
                case ShipClass.GateBreaker:
                case ShipClass.Freighter: return 4;
                case ShipClass.Destroyer:
                case ShipClass.Miner:
                case ShipClass.Explorer:
                case ShipClass.LaneTender: return 3;
                case ShipClass.AssaultShuttle:
                case ShipClass.Gunship:
                case ShipClass.Courier:
                case ShipClass.OreTug:
                case ShipClass.CaptureTug:
                case ShipClass.GatePicket:
                // Its magazines and engine rooms fill a frigate (U.SPC-T475).
                case ShipClass.Frigate:
                // The space a drive room would take, kept free for a corvette's patrol fit (U.SPC-T476).
                case ShipClass.Corvette: return 2;
                default: return 1;
            }
        }

        /// <summary>The space a layout's facilities take, sections.</summary>
        public static double SpaceOf(IEnumerable<FacilitySite> layout)
        {
            double n = 0;
            if (layout != null) foreach (var s in layout) n += s.Size;
            return n;
        }
    }
}
