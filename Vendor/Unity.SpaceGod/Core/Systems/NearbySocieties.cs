using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The societies of the twelve nearby systems (U.SPC-T90), all game-layer fiction placed on the real
    /// bodies of <see cref="NearbySystems"/>: colonies on the habitable-zone worlds, mines on the hot
    /// rocky ones and in the known belts and discs, skimmers on the giants, and invented stations in orbit
    /// where no world will do (Sirius and the brown dwarfs have none). Each is sized by its distance from
    /// Sol (<see cref="SocietySpec.Frontier"/>), so Barnard's Star is a busy half-Centauri and the twelve
    /// light-year frontier a scatter of outposts. Physical values stay those of the catalogue; the only
    /// numbers invented here are the stations' orbits and the people.
    /// </summary>
    public static class NearbySocieties
    {
        /// <summary>The society planned for a system, or null if it has none.</summary>
        public static SocietySpec For(SystemSpec system)
        {
            SocietySpec s;
            switch (system.Name)
            {
                case "Barnard's Star": s = BarnardsStar(); break;
                case "Luhman 16": s = Luhman16(); break;
                case "Wolf 359": s = Wolf359(); break;
                case "Lalande 21185": s = Lalande21185(); break;
                case "Sirius": s = Sirius(); break;
                case "Epsilon Eridani": s = EpsilonEridani(); break;
                case "Ross 128": s = Ross128(); break;
                case "Epsilon Indi": s = EpsilonIndi(); break;
                case "Tau Ceti": s = TauCeti(); break;
                case "GJ 1061": s = Gj1061(); break;
                case "Luyten's Star": s = LuytensStar(); break;
                case "Teegarden's Star": s = TeegardensStar(); break;
                default: return null;
            }
            s.System = system.Name;
            s.DistanceLy = system.DistanceLy;
            return s;
        }

        static Rgb C(float r, float g, float b) => new Rgb(r, g, b);

        // The goods each kind of place digs.
        static readonly Good[] Metals = { Good.NickelIron, Good.Titanium, Good.Copper, Good.Zinc };
        static readonly Good[] Precious = { Good.RareEarths, Good.Platinum };
        static readonly Good[] Volatiles = { Good.Water, Good.Nitrogen, Good.Hydrocarbons };
        static readonly Good[] Skimmed = { Good.HeliumThree, Good.Hydrocarbons, Good.Nitrogen };

        static Good[] G(params Good[] goods) => goods;

        static Good[] Join(params Good[][] sets)
        {
            var list = new List<Good>();
            foreach (var set in sets)
                foreach (var g in set)
                    if (!list.Contains(g)) list.Add(g);
            return list.ToArray();
        }

        static Good[] With(Good[] set, params Good[] more) => Join(set, more);

        static IndustryKind[] I(params IndustryKind[] k) => k;

        const IndustryKind Smelter = IndustryKind.Smelter, Fab = IndustryKind.ElectronicsFab, Works = IndustryKind.MachineWorks, Pharma = IndustryKind.PharmaLab;
        const SettlementKind Colony = SettlementKind.Colony, Station = SettlementKind.Station, Outpost = SettlementKind.MiningOutpost, Haven = SettlementKind.Haven;
        const SocietyRole Gov = SocietyRole.Authority, Co = SocietyRole.Company, Raid = SocietyRole.Raiders;

        // ---------------------------------------------------------------- 5.96 ly

        static SocietySpec BarnardsStar()
        {
            var s = new SocietySpec
            {
                // The Federation's own frontier (U.SPC-T91): the one star past Centauri it governs from Earth, with the same
                // treasury, navy and capital as at home; the farther systems are states of their own.
                Authority = new FactionSpec(World.SolFederation, "SFS", World.SolFederationColor, 6e8, 1.25),
                Company = new FactionSpec("Arrowhead Metals", "AHM", C(0.85f, 0.8f, 0.45f), 5e8, 0.8),
                Traders = new FactionSpec("Runaway Star Traders", "RST", C(0.8f, 0.82f, 0.9f), 1e8, 0.6),
                Raiders = new FactionSpec("The Drift", "", C(0.75f, 0.3f, 0.55f), 6e7, 1.15),
                Summary = "The first star past Centauri the gates reached, and the Sol Federation's frontier province: a busy frontier of hot mining worlds and a hub station in the habitable zone.",
            };
            s.Station("Arrow Station", "Barnard's Star", 0.075, 30, "The Federation's hub in the habitable zone, outside all four planets' orbits.");
            s.Station("Longwater", "Barnard's Star", 0.6, 200, "An ice harvester working a comet swarm (no belt is known here).");
            s.Station("Slipstream", "Barnard's Star", 0.3, 120, "A raider hold in a dead freighter's hull, parked off the lanes.");
            s.Add("Arrow Station", "Arrow Station", Station, Gov, 260e3, "The only temperate orbit in the system; a station, since every planet is too hot.",
                G(Good.HeliumThree), I(Fab, Works, Pharma), shipyard: 0.8, munitions: 0.6);
            s.Add("Ironside", "Barnard e", Outpost, Co, 160e3, "The coolest of the four rocky worlds, still twice Earth's sunlight: metal mines dug in on the night side.",
                Metals, I(Smelter), shipyard: 0.5);
            s.Add("Crucible", "Barnard c", Outpost, Co, 50e3, "The heaviest planet: rare metals, and helium-3 from the stellar wind in its regolith.",
                With(Precious, Good.Copper, Good.HeliumThree));
            s.Add("Longwater", "Longwater", Station, Gov, 40e3, "Water, nitrogen and hydrocarbons from captured comets.", Volatiles);
            s.Add("Slipstream", "Slipstream", Haven, Raid, 8e3, "The Drift's hold.");
            s.Lane("Arrow Station", "Barnard e", 0.06);
            s.Lane("Arrow Station", "Barnard c", -0.05);
            s.Lane("Barnard c", "Barnard e", 0.04);
            s.Lane("Arrow Station", "Longwater", -0.04);
            s.Lane("Longwater", "Slipstream", 0.05);
            s.Freighters = new[] { "Proper Motion", "Runaway", "Tenth Arcsecond", "Ophiuchus", "Old Red", "Fast Mover", "Nearest Single", "Long Drift", "Dimlight", "Second Neighbour", "Steady Glow", "Cold Iron" };
            s.Haulers = new[] { "Night Side", "Slag Moth", "Ironmonger", "Furnace Rat" };
            s.Couriers = new[] { "Quickstep", "Barnard's Arrow", "Errand" };
            s.Yachts = new[] { "Wanderjahr" };
            s.Explorers = new[] { "Over the Horizon" };
            s.Prospectors = new[] { "Iron Hope" };
            s.Navy = new[] { "Steadfast", "Bulwark", "Sentry" };
            s.RaiderNames = new[] { "Slipknot", "Undertow", "Riptide", "Drift Ice", "Cutpurse", "Fade" };
            s.Companies.Add(("Arrowhead Ore", "AHO", "Ironside", Co, true));
            s.Companies.Add(("Runaway Star Carrying Company", "RSC", "Arrow Station", Gov, false));
            return s;
        }

        // ---------------------------------------------------------------- 6.51 ly

        static SocietySpec Luhman16()
        {
            var s = new SocietySpec
            {
                Authority = new FactionSpec("Twin Dark Authority", "TDA", C(0.6f, 0.45f, 0.35f), 4e8, 1.0),
                Company = new FactionSpec("Cloudline Extraction", "CLX", C(0.9f, 0.6f, 0.3f), 4e8, 0.8),
                Traders = new FactionSpec("Ember Lantern Traders", "ELT", C(0.85f, 0.75f, 0.65f), 8e7, 0.6),
                Summary = "Two brown dwarfs and no worlds: skimmer stations dredging the dwarfs' clouds, and little else.",
            };
            s.Station("Cloudbreak", "Luhman 16A", 0.006, 40, "The Authority's skimmer hub, low over the larger brown dwarf.");
            s.Station("Ironrain", "Luhman 16B", 0.005, 220, "A dredge station in the lighter dwarf's patchy iron and silicate clouds.");
            s.Station("Scree", "Luhman 16A", 0.05, 160, "A rubble-processing station on a captured swarm of rock (no disc is known here).");
            s.Add("Cloudbreak", "Cloudbreak", Station, Gov, 80e3, "Water, methane and ammonia (for nitrogen) skimmed from a T-dwarf's air.",
                With(Volatiles, Good.HeliumThree), I(Pharma, Fab), shipyard: 0.4);
            s.Add("Ironrain", "Ironrain", Station, Co, 55e3, "Iron and silicates rained out of the cloud decks.",
                G(Good.NickelIron, Good.Titanium), I(Smelter, Works));
            // Too far out and too small for the Authority's writ: its middlemen keep a black market (U.SPC-T94).
            s.Add("Scree", "Scree", Outpost, Co, 35e3, "Metals and rare metals from captured rubble.", With(Precious, Good.Copper, Good.Zinc), lawless: true);
            s.Lane("Cloudbreak", "Scree", 0.05);
            s.Lane("Cloudbreak", "Ironrain", -0.03);
            s.Lane("Scree", "Ironrain", 0.03);
            s.Freighters = new[] { "Dim Ember", "Cloud Diver", "Iron Rain", "Twin Shadow" };
            s.Haulers = new[] { "Dredge" };
            s.Couriers = new[] { "Faint Signal" };
            s.Yachts = new[] { "Infrared" };
            s.Navy = new[] { "Lantern", "Spark" };
            s.Companies.Add(("Cloudline Haulage", "CLH", "Ironrain", Co, true));
            s.Companies.Add(("Ember Lantern Carriers", "ELC", "Cloudbreak", Gov, false));
            return s;
        }

        // ---------------------------------------------------------------- 7.86 ly

        static SocietySpec Wolf359()
        {
            var s = new SocietySpec
            {
                Authority = new FactionSpec("Wolf Flare Accord", "WFA", C(0.95f, 0.35f, 0.3f), 4e8, 1.1),
                Company = new FactionSpec("Leo Mining Combine", "LMC", C(0.95f, 0.75f, 0.35f), 4e8, 0.8),
                Traders = new FactionSpec("Flarewatch Free Traders", "FFT", C(0.8f, 0.8f, 0.75f), 8e7, 0.6),
                Summary = "A flare star's hardened stations and a skimmer on its one (unconfirmed) giant.",
            };
            s.Station("Flarewall", "Wolf 359", 0.045, 80, "The Accord's hub in the habitable zone, shielded deep against the star's flares.");
            s.Station("Cinderfall", "Wolf 359", 0.4, 260, "A smelting station on a captured swarm of rock (no belt is known here).");
            s.Add("Flarewall", "Flarewall", Station, Gov, 100e3, "The temperate orbit; nothing else at this star is.", null, I(Fab, Works, Pharma), shipyard: 0.6, munitions: 0.4);
            s.Add("Howling Deep", "Wolf 359 b", Station, Gov, 80e3, "An aerostat skimming the cold giant (a candidate planet).", With(Skimmed, Good.Water));
            s.Add("Cinderfall", "Cinderfall", Outpost, Co, 70e3, "Ore and rare metals from the rubble.", Join(Metals, Precious), I(Smelter), lawless: true);
            s.Lane("Flarewall", "Cinderfall", 0.05);
            s.Lane("Cinderfall", "Wolf 359 b", -0.04);
            s.Lane("Flarewall", "Wolf 359 b", 0.03);
            s.Freighters = new[] { "Flare Warning", "Leo's Mane", "Faint Sun", "Shield Wall", "Burst" };
            s.Haulers = new[] { "Cinder Moth", "Grit" };
            s.Couriers = new[] { "Fast Flare" };
            s.Yachts = new[] { "Long Howl" };
            s.Prospectors = new[] { "Pick of the Litter" };
            s.Navy = new[] { "Grey Wolf", "Snarl", "Fang" };
            s.Companies.Add(("Leo Ore Haulers", "LOH", "Cinderfall", Co, true));
            s.Companies.Add(("Flarewatch Carriers", "FWC", "Flarewall", Gov, false));
            return s;
        }

        // ---------------------------------------------------------------- 8.30 ly

        static SocietySpec Lalande21185()
        {
            var s = new SocietySpec
            {
                Authority = new FactionSpec("Ursa Commonwealth", "UCW", C(0.4f, 0.7f, 0.95f), 5e8, 1.1),
                Company = new FactionSpec("Hearthstone Combine", "HSC", C(0.9f, 0.65f, 0.35f), 4e8, 0.8),
                Traders = new FactionSpec("Great Bear Carriers", "GBC", C(0.8f, 0.85f, 0.9f), 8e7, 0.6),
                Summary = "The brightest red dwarf in the north: a habitable-zone hub, a hot mining world and a skimmer on the cold Neptune.",
            };
            s.Station("Ursa Station", "Lalande 21185", 0.18, 300, "The Commonwealth's hub in the habitable zone, between the hot super-Earth and the candidate d.");
            s.Add("Ursa Station", "Ursa Station", Station, Gov, 180e3, "The temperate orbit, where the lanes meet: the yards and the Commonwealth.", null, null, shipyard: 0.7, munitions: 0.6);
            s.Add("Hearthstone", "Lalande 21185 b", Outpost, Co, 120e3, "A hot super-Earth's metal mines, under three times Earth's sunlight.", Metals, I(Smelter, Fab, Works), shipyard: 0.3);
            s.Add("Bear Skimmer", "Lalande 21185 c", Station, Gov, 60e3, "An aerostat skimming the cold Neptune, with the labs that turn its hydrocarbons into medicine.", With(Skimmed, Good.Water), I(Pharma));
            s.Add("Polaris Diggings", "Lalande 21185 d", Outpost, Co, 40e3, "Rare metals on a cold world (a candidate planet).", With(Precious, Good.Water));
            s.Lane("Ursa Station", "Lalande 21185 b", 0.05);
            s.Lane("Ursa Station", "Lalande 21185 d", -0.04);
            s.Lane("Lalande 21185 d", "Lalande 21185 c", 0.03);
            s.Freighters = new[] { "Great Bear", "Big Dipper", "Northern Light", "Ursa Minor", "Seven Stars", "Hearth Fire", "Pole Star" };
            s.Haulers = new[] { "Bear Claw", "Hearthstone Mule" };
            s.Couriers = new[] { "Alkaid", "Merak" };
            s.Yachts = new[] { "Northbound" };
            s.Prospectors = new[] { "Honeypot" };
            s.Navy = new[] { "Arcturus", "Guardian", "Bearward" };
            s.Companies.Add(("Hearthstone Ore", "HSO", "Hearthstone", Co, true));
            s.Companies.Add(("Great Bear Freight", "GBF", "Ursa Station", Gov, false));
            return s;
        }

        // ---------------------------------------------------------------- 8.61 ly

        static SocietySpec Sirius()
        {
            var s = new SocietySpec
            {
                Authority = new FactionSpec("Sirian Directorate", "SDR", C(0.7f, 0.85f, 1f), 5e8, 1.0),
                Company = new FactionSpec("Canis Major Foundries", "CMF", C(0.95f, 0.85f, 0.5f), 5e8, 0.8),
                Traders = new FactionSpec("Dogstar Free Traders", "DFT", C(0.85f, 0.88f, 0.92f), 8e7, 0.6),
                Summary = "A white star and its white dwarf with no worlds at all: solar-powered stations on stable orbits close to each.",
            };
            s.Station("Canicula", "Sirius A", 2.2, 50, "The Directorate's solar-powered hub, well inside the orbit B allows (its habitable zone, past 5 AU, is not stable).");
            s.Station("Frostline", "Sirius A", 1.4, 230, "An ice station working a comet swarm close to A (no belt is known here).");
            s.Station("Pup Forge", "Sirius B", 0.4, 140, "A foundry by the white dwarf, on captured rock (no disc is known here).");
            s.Add("Canicula", "Canicula", Station, Gov, 150e3, "Sunlight for nothing, and the helium-3 of A's hard wind.", G(Good.HeliumThree), I(Fab, Works, Pharma), shipyard: 0.6, munitions: 0.5);
            s.Add("Frostline", "Frostline", Station, Gov, 40e3, "Water, nitrogen and hydrocarbons from captured comets.", Volatiles);
            s.Add("Pup Forge", "Pup Forge", Outpost, Co, 90e3, "Ore and rare metals from the rubble, smelted by the white dwarf's light.", Join(Metals, Precious), I(Smelter), shipyard: 0.3);
            s.Lane("Canicula", "Frostline", 0.04);
            s.Lane("Canicula", "Pup Forge", -0.03);
            s.Lane("Frostline", "Pup Forge", 0.03);
            s.Freighters = new[] { "Dog Star", "White Fire", "Canicula", "Heliacal Rising", "The Pup" };
            s.Haulers = new[] { "Forge Hound" };
            s.Couriers = new[] { "Sothis" };
            s.Yachts = new[] { "Brightest" };
            s.Navy = new[] { "Orion's Hound", "Laelaps" };
            s.Companies.Add(("Canis Major Ore", "CMO", "Pup Forge", Co, true));
            s.Companies.Add(("Dogstar Carriers", "DSC", "Canicula", Gov, false));
            return s;
        }

        // ---------------------------------------------------------------- 10.47 ly

        static SocietySpec EpsilonEridani()
        {
            var s = new SocietySpec
            {
                Authority = new FactionSpec("Eridani Union", "ERU", C(0.45f, 0.8f, 0.85f), 6e8, 1.1),
                Company = new FactionSpec("Aegir Deepworks", "ADW", C(0.4f, 0.55f, 0.9f), 5e8, 0.8),
                Traders = new FactionSpec("River Traders", "RVT", C(0.82f, 0.86f, 0.9f), 1e8, 0.6),
                Raiders = new FactionSpec("Wave Daughters", "", C(0.55f, 0.3f, 0.75f), 6e7, 1.15),
                Summary = "The nearest system like our own: a habitable-zone hub, miners in both inner belts and a skimmer on AEgir.",
            };
            s.Station("Eridanus", "Epsilon Eridani", 0.75, 10, "The Union's hub in the habitable zone of a young orange dwarf.");
            s.Station("Ran", "Epsilon Eridani", 1.75, 130, "A mining station in the warm inner belt.");
            s.Station("Hlesey", "Epsilon Eridani", 12, 250, "An ice station in the outer belt.");
            s.Station("Wavecrest", "Epsilon Eridani", 16, 60, "A raider hold hidden in the outer belt's dust.");
            s.Add("Eridanus", "Eridanus", Station, Gov, 280e3, "The temperate orbit, where the lanes meet: the yards and the Union.", null, null, shipyard: 0.8, munitions: 0.6);
            s.Add("Ran", "Ran", Outpost, Co, 200e3, "Ore and rare metals from the inner belt's rock, and the fabs and works they feed.", Join(Metals, Precious), I(Smelter, Fab, Works), shipyard: 0.4);
            s.Add("AEgir Skimmer", "Epsilon Eridani b", Station, Gov, 90e3, "An aerostat skimming the Jupiter-mass giant, with labs on its hydrocarbons.", Skimmed, I(Pharma));
            s.Add("Hlesey", "Hlesey", Station, Co, 70e3, "Water and nitrogen from the outer belt's ice.", Volatiles);
            s.Add("Wavecrest", "Wavecrest", Haven, Raid, 8e3, "The Wave Daughters' hold.", hidden: true);
            s.HavenSite("Farstrand", "Epsilon Eridani", 70, 200, "An empty hide among the icy debris of the 70 AU ring: room for another clan's hold.");
            s.Lane("Eridanus", "Ran", 0.05);
            s.Lane("Ran", "Epsilon Eridani b", -0.04);
            s.Lane("Epsilon Eridani b", "Hlesey", 0.04);
            s.Lane("Eridanus", "Epsilon Eridani b", 0.03);
            s.Freighters = new[] { "River of Stars", "AEgir's Cup", "Young Sun", "Eridanus", "Delta", "Long Water", "Ninth Wave" };
            s.Haulers = new[] { "Reef", "Shoal" };
            s.Couriers = new[] { "Ran's Net", "Swift Current" };
            s.Yachts = new[] { "Ozma", "Dustlight" };
            s.Prospectors = new[] { "Beachcomber" };
            s.Navy = new[] { "Hymir", "Breaker", "Skerry" };
            s.RaiderNames = new[] { "Billow", "Blood-Hair", "Comber", "Surge", "Undertow", "Wavelet" };
            s.Companies.Add(("Aegir Ore", "AGO", "Ran", Co, true));
            s.Companies.Add(("River Freight", "RFT", "Eridanus", Gov, false));
            return s;
        }

        // ---------------------------------------------------------------- 11.01 ly

        static SocietySpec Ross128()
        {
            var s = new SocietySpec
            {
                Authority = new FactionSpec("Ross Landing Council", "RLC", C(0.5f, 0.85f, 0.55f), 5e8, 1.0),
                Company = new FactionSpec("Virgo Quarrying", "VQC", C(0.85f, 0.7f, 0.5f), 4e8, 0.8),
                Traders = new FactionSpec("Quiet Star Freight", "QSF", C(0.82f, 0.86f, 0.82f), 8e7, 0.6),
                Summary = "A calm red dwarf and its temperate world: the most Earth-like colony of the frontier, and a quarry station.",
            };
            s.Station("Ross Quarry", "Ross 128", 0.15, 200, "A quarry station on a captured swarm of rock (no belt is known here).");
            s.Add("Calm Harbour", "Ross 128 b", Colony, Gov, 450e3, "A temperate world round a star that rarely flares: farms under domes.",
                Join(Volatiles), I(Pharma, Fab, Works), shipyard: 0.6, munitions: 0.4);
            s.Add("Ross Quarry", "Ross Quarry", Outpost, Co, 150e3, "Ore, rare metals and wind-blown helium-3 from the rubble.", Join(Metals, Precious, new[] { Good.HeliumThree }), I(Smelter));
            s.Lane("Ross 128 b", "Ross Quarry", 0.05);
            s.Freighters = new[] { "Stillwater", "Calm Seas", "Virgo", "Quiet Hours", "Harbour Light" };
            s.Haulers = new[] { "Quarryman", "Gravel" };
            s.Couriers = new[] { "Zavijava" };
            s.Yachts = new[] { "Hushed" };
            s.Navy = new[] { "Vigil", "Watchman" };
            s.Companies.Add(("Virgo Ore", "VGO", "Ross Quarry", Co, true));
            s.Companies.Add(("Quiet Star Lines", "QSL", "Calm Harbour", Gov, false));
            return s;
        }

        // ---------------------------------------------------------------- 11.87 ly

        static SocietySpec EpsilonIndi()
        {
            var s = new SocietySpec
            {
                Authority = new FactionSpec("Indus Protectorate", "INP", C(0.95f, 0.65f, 0.45f), 4e8, 1.0),
                Company = new FactionSpec("Indus Gas and Ore", "IGO", C(0.7f, 0.8f, 0.45f), 4e8, 0.8),
                Traders = new FactionSpec("Peacock Free Traders", "PFT", C(0.4f, 0.75f, 0.75f), 8e7, 0.6),
                Summary = "An orange dwarf with a habitable-zone hub, a quarry and a skimmer on its super-Jupiter; the brown dwarfs 1,500 AU out are left alone.",
            };
            s.Station("Indus Station", "Epsilon Indi A", 0.6, 100, "The Protectorate's hub in the habitable zone.");
            s.Station("Tucana Quarry", "Epsilon Indi A", 2.5, 300, "A quarry station on a captured swarm of rock (no belt is known here).");
            s.Add("Indus Station", "Indus Station", Station, Gov, 220e3, "The temperate orbit, where the lanes meet: the yards and the Protectorate.", null, null, shipyard: 0.6, munitions: 0.5);
            s.Add("Ab Skimmer", "Epsilon Indi Ab", Station, Co, 80e3, "An aerostat skimming the cold super-Jupiter, with labs on its hydrocarbons.", With(Skimmed, Good.Water), I(Pharma));
            s.Add("Tucana Quarry", "Tucana Quarry", Outpost, Co, 130e3, "Ore and rare metals from the rubble, and the fabs and works they feed.", Join(Metals, Precious), I(Smelter, Fab, Works));
            s.Lane("Indus Station", "Tucana Quarry", 0.05);
            s.Lane("Tucana Quarry", "Epsilon Indi Ab", -0.04);
            s.Lane("Indus Station", "Epsilon Indi Ab", 0.03);
            s.Freighters = new[] { "Peacock", "Toucan", "Southern Sky", "Indus River", "Tail Feather" };
            s.Haulers = new[] { "Rock Wren" };
            s.Couriers = new[] { "Far Pair" };
            s.Yachts = new[] { "Brown Study" };
            s.Prospectors = new[] { "Cold Comfort" };
            s.Navy = new[] { "Pavo", "Grus", "Tucana" };
            s.Companies.Add(("Indus Ore", "INO", "Tucana Quarry", Co, true));
            s.Companies.Add(("Peacock Carriers", "PKC", "Indus Station", Gov, false));
            return s;
        }

        // ---------------------------------------------------------------- 11.91 ly

        static SocietySpec TauCeti()
        {
            var s = new SocietySpec
            {
                Authority = new FactionSpec("Ceti Federation", "CTF", C(0.55f, 0.75f, 1f), 5e8, 1.1),
                Company = new FactionSpec("Whale Road Mining", "WRM", C(0.4f, 0.6f, 0.75f), 4e8, 0.8),
                Traders = new FactionSpec("Cetus Free Traders", "CFT", C(0.82f, 0.85f, 0.9f), 8e7, 0.6),
                Summary = "The nearest single Sun-like star: a habitable-zone hub, miners on its worlds and ice from its heavy debris disc.",
            };
            s.Station("Cetus Station", "Tau Ceti", 0.9, 170, "The Federation's hub in the habitable zone, between e's old place and f.");
            s.Station("Cometreach", "Tau Ceti", 10, 40, "An ice station in the inner edge of the debris disc.");
            s.Add("Cetus Station", "Cetus Station", Station, Gov, 300e3, "The temperate orbit, where the lanes meet: the yards and the Federation.", null, null, shipyard: 0.7, munitions: 0.6);
            s.Add("Frostfall", "Tau Ceti f", Outpost, Gov, 140e3, "A cold super-Earth just past the habitable zone: ice and titanium under domes.",
                G(Good.Water, Good.Nitrogen, Good.Titanium, Good.RareEarths));
            s.Add("Kiln", "Tau Ceti h", Outpost, Co, 110e3, "A hot world's metal mines, and the fabs and works they feed.", Join(Metals, new[] { Good.Platinum }), I(Smelter, Fab, Works), shipyard: 0.3);
            s.Add("Cometreach", "Cometreach", Station, Co, 60e3, "Water, hydrocarbons and helium-3 from the disc's comets, and labs on the hydrocarbons.", With(Volatiles, Good.HeliumThree), I(Pharma),
                lawless: true);
            // Deep in the disc, a hollow where a clan from elsewhere could hide a haven (U.SPC-T94).
            s.HavenSite("Driftglass", "Tau Ceti", 30, 300, "A hollow comet deep in the debris disc, dark and empty: room for a raider hold.");
            s.Lane("Cetus Station", "Tau Ceti f", 0.04);
            s.Lane("Cetus Station", "Tau Ceti h", -0.05);
            s.Lane("Tau Ceti f", "Cometreach", 0.04);
            s.Freighters = new[] { "Whale Road", "Leviathan", "Sun Twin", "Ceti Alpha", "Deep Disc", "Old Sun" };
            s.Haulers = new[] { "Kiln Mule", "Snowball" };
            s.Couriers = new[] { "Menkar" };
            s.Yachts = new[] { "Ozma's Call", "Long Silence" };
            s.Prospectors = new[] { "Comet Chaser" };
            s.Navy = new[] { "Cetus", "Harpoon", "Fluke" };
            s.Companies.Add(("Whale Road Ore", "WRO", "Kiln", Co, true));
            s.Companies.Add(("Cetus Lines", "CTL", "Cetus Station", Gov, false));
            return s;
        }

        // ---------------------------------------------------------------- 11.98 ly

        static SocietySpec Gj1061()
        {
            var s = new SocietySpec
            {
                Authority = new FactionSpec("Horologe Council", "HGC", C(0.75f, 0.7f, 0.95f), 4e8, 1.0),
                Company = new FactionSpec("Clockwork Metals", "CWM", C(0.85f, 0.7f, 0.4f), 4e8, 0.8),
                Traders = new FactionSpec("Escapement Carriers", "ESC", C(0.85f, 0.85f, 0.85f), 6e7, 0.6),
                Summary = "A quiet red dwarf's three Earth-mass worlds: a colony on the temperate outer one, mines on the others.",
            };
            s.Add("Tidewater", "GJ 1061 d", Colony, Gov, 230e3, "The temperate outer world, getting half Earth's light.", Join(Volatiles), I(Pharma, Fab, Works), shipyard: 0.5, munitions: 0.3);
            s.Add("Balance Wheel", "GJ 1061 c", Outpost, Co, 120e3, "Metal mines on the warmer middle world.", Join(Metals, new[] { Good.HeliumThree }), I(Smelter));
            s.Add("Hairspring", "GJ 1061 b", Outpost, Co, 50e3, "Rare metals on the hot inner world.", Precious);
            s.Lane("GJ 1061 d", "GJ 1061 c", 0.05);
            s.Lane("GJ 1061 c", "GJ 1061 b", -0.05);
            s.Freighters = new[] { "Tick", "Tock", "Pendulum", "Mainspring" };
            s.Haulers = new[] { "Ratchet" };
            s.Couriers = new[] { "Second Hand" };
            s.Yachts = new[] { "Horologium" };
            s.Navy = new[] { "Regulator", "Chime" };
            s.Companies.Add(("Clockwork Ore", "CWO", "Balance Wheel", Co, true));
            s.Companies.Add(("Escapement Freight", "ESF", "Tidewater", Gov, false));
            return s;
        }

        // ---------------------------------------------------------------- 12.35 ly

        static SocietySpec LuytensStar()
        {
            var s = new SocietySpec
            {
                Authority = new FactionSpec("Sonar Assembly", "SNA", C(0.45f, 0.85f, 0.8f), 4e8, 1.0),
                Company = new FactionSpec("Canis Minor Mining", "CMM", C(0.9f, 0.7f, 0.45f), 4e8, 0.8),
                Traders = new FactionSpec("Signal Free Traders", "SGT", C(0.85f, 0.88f, 0.85f), 6e7, 0.6),
                Summary = "The red dwarf humanity once sent a message to: a colony on its temperate super-Earth, mines and a skimmer.",
            };
            s.Add("Sonar Landing", "Luyten b", Colony, Gov, 230e3, "The temperate super-Earth GJ 273 b, the 2017 message's target.", Join(Volatiles), I(Pharma, Fab, Works), shipyard: 0.5, munitions: 0.3);
            s.Add("Echo Mines", "Luyten c", Outpost, Co, 110e3, "A hot rocky world's metal mines.", Join(Metals, Precious), I(Smelter));
            s.Add("Reply Skimmer", "Luyten d", Station, Co, 50e3, "An aerostat on a cold mini-Neptune (a candidate planet).", Skimmed);
            s.Lane("Luyten b", "Luyten c", 0.05);
            s.Lane("Luyten b", "Luyten d", -0.04);
            s.Freighters = new[] { "Carrier Wave", "Sonar", "Calling", "Long Reply" };
            s.Haulers = new[] { "Echo" };
            s.Couriers = new[] { "Ping" };
            s.Yachts = new[] { "Listener" };
            s.Navy = new[] { "Beacon", "Pulse" };
            s.Companies.Add(("Canis Minor Ore", "CMI", "Echo Mines", Co, true));
            s.Companies.Add(("Signal Lines", "SGL", "Sonar Landing", Gov, false));
            return s;
        }

        // ---------------------------------------------------------------- 12.50 ly

        static SocietySpec TeegardensStar()
        {
            var s = new SocietySpec
            {
                Authority = new FactionSpec("Aries Settlement Board", "ASB", C(0.95f, 0.8f, 0.55f), 4e8, 1.0),
                Company = new FactionSpec("Ramshorn Mining", "RHM", C(0.75f, 0.6f, 0.4f), 4e8, 0.8),
                Traders = new FactionSpec("Fleece Carriers", "FLC", C(0.9f, 0.88f, 0.75f), 6e7, 0.6),
                Summary = "The far edge: a colony on one of the most Earth-like worlds known, with an ice outpost and a mine beside it.",
            };
            s.Add("Golden Fleece", "Teegarden's Star b", Colony, Gov, 230e3, "An Earth-mass world with about Earth's sunlight.", G(Good.Water, Good.Nitrogen), null, shipyard: 0.5, munitions: 0.3);
            s.Add("Ramshorn", "Teegarden's Star c", Outpost, Co, 80e3, "Metal mines on the cooler middle world, and the fabs and works they feed.", Join(Metals, Precious), I(Smelter, Fab, Works));
            s.Add("Hamal Ice", "Teegarden's Star d", Outpost, Gov, 60e3, "Ice, hydrocarbons and helium-3 on the cold outer world, and labs on the hydrocarbons.", With(Volatiles, Good.HeliumThree), I(Pharma));
            s.Lane("Teegarden's Star b", "Teegarden's Star c", 0.05);
            s.Lane("Teegarden's Star c", "Teegarden's Star d", -0.05);
            s.Freighters = new[] { "Golden Ram", "Fleece", "Argo", "First Point" };
            s.Haulers = new[] { "Tup" };
            s.Couriers = new[] { "Hermes' Ram" };
            s.Yachts = new[] { "Far Edge" };
            s.Navy = new[] { "Ram", "Bellwether" };
            s.Companies.Add(("Ramshorn Ore", "RHO", "Ramshorn", Co, true));
            s.Companies.Add(("Fleece Lines", "FLL", "Golden Fleece", Gov, false));
            return s;
        }
    }
}
