using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>One topic of a manual chapter: a short heading and the paragraph that explains it.</summary>
    public readonly struct ManualTopic
    {
        public readonly string Heading;
        public readonly string Text;

        public ManualTopic(string heading, string text)
        {
            Heading = heading ?? "";
            Text = text ?? "";
        }
    }

    /// <summary>
    /// One chapter of the in-game manual: its title in the contents list, a line saying what it covers, its topics,
    /// and the <see cref="ControlsHelp"/> groups whose keys it prints after them (the keys live in one place only).
    /// </summary>
    public sealed class ManualChapter
    {
        public readonly string Title;
        public readonly string Summary;
        public readonly IReadOnlyList<ManualTopic> Topics;
        public readonly IReadOnlyList<string> ControlGroups;

        public ManualChapter(string title, string summary, string[] controlGroups, params ManualTopic[] topics)
        {
            Title = title;
            Summary = summary ?? "";
            ControlGroups = controlGroups ?? new string[0];
            Topics = topics;
        }

        /// <summary>The Help screen's key groups this chapter prints, in the order it names them.</summary>
        public IEnumerable<HelpSection> Controls()
        {
            foreach (var name in ControlGroups)
                foreach (var s in ControlsHelp.Sections)
                    if (s.Title == name)
                        yield return s;
        }
    }

    /// <summary>
    /// The in-game manual (U.SPC-T497): everything in the game, chapter by chapter, opened from the Help screen's
    /// contents list (main menu Help, H / F1 in game, the Esc menu's Help). It is kept current as features land:
    /// a new feature is not done until its topic is written here, in the chapter it belongs to, and any new key has
    /// its line in <see cref="ControlsHelp"/> (which the chapters print, so a key is never written twice). A new
    /// chapter is one more entry in <see cref="Chapters"/>.
    /// </summary>
    public static class GameManual
    {
        /// <summary>The last chapter: every key group of <see cref="ControlsHelp"/>, the whole reference card.</summary>
        public const string EveryKeyTitle = "Every key";

        static ManualTopic T(string heading, string text) => new ManualTopic(heading, text);
        static string[] Keys(params string[] groups) => groups;

        public static readonly IReadOnlyList<ManualChapter> Chapters = new[]
        {
            new ManualChapter("Getting started", "What the game is, and the first minutes in it", Keys(),
                T("What this is",
                  "A living galaxy you can watch from above or step into anywhere. The solar system runs on its real orbits " +
                  "and clock; on top of it, factions trade, build, fight wars, raid, make treaties and colonise, all on their " +
                  "own. You can play it as a strategy game, a dogfighter, a capital-ship broadside game, a trader's career or a " +
                  "diplomat's, or take nothing at all and let it play out."),
                T("The main menu",
                  "Continue loads the latest save, New Game starts at the current date beside Earth, Load Game opens the " +
                  "saves, Profiles switches player profile (each keeps its own saves and settings), Settings opens every " +
                  "tunable, Help opens this manual and Quit leaves."),
                T("Your first minutes",
                  "You start in free swim beside Earth, with Earth selected, at gameplay scale and real time. Swim with " +
                  "W A S D, look with the right mouse button, and click anything to select it. Open the navigator to pick " +
                  "a body or a ship, press Tab to cycle through ships, and B to jump to the biggest battle going. Select a " +
                  "ship and press P to take its helm; P again hands it back to its crew."),
                T("Two ways to play",
                  "From the god view you watch, inspect and command (polities, fleets, your own house). At the helm you fly " +
                  "one hull, or step into any seat in a fight: a ship's helm, a fighter's cockpit, a gun. Nothing pauses and " +
                  "nothing loads when you move between them."),
                T("Finding your way in this manual",
                  "Pick a chapter in the contents on the left. Each chapter explains a part of the game and ends with the " +
                  "keys for it; the last chapter, Every key, is the whole reference card.")),

            new ManualChapter("The god view", "The free camera, time, scale and what the panels show", Keys("CAMERA AND GOD VIEW"),
                T("Swimming and focus",
                  "The camera swims freely: Shift to go faster, Ctrl for fine moves, the wheel for swim speed or zoom. Click " +
                  "to select, double-click or F to focus on the selection and orbit it with the right mouse. Home shows the " +
                  "whole system and the number keys jump to the planets."),
                T("The navigator",
                  "A tree of the system: bodies, fleets, docking rings and their queues, and everything in orbit. STAR SYSTEMS " +
                  "at its top switches to another star: the camera pulls out, glides between the stars and falls into the " +
                  "other system (Esc skips the trip). Every system runs all the time on the shared clock; only the one you " +
                  "are in is drawn."),
                T("The details panel",
                  "For a body: distance, light time, orbital speed, period, eccentricity, gravity, and each settlement's " +
                  "exports, shortages and defences. For a ship: status, speed, ETA, cargo and trade profit, or hull, shields, " +
                  "lost hardpoints, fighters aboard and orders. The selected ship's flight path is drawn, lanes included."),
                T("Time",
                  "The clock starts at the current date and runs from real time up to a year a second. Space pauses (easing " +
                  "to a stop), comma and period slow and speed the warp, and T brings it back to now. Watching a battle or " +
                  "taking a helm drops to real time."),
                T("Gameplay and realistic scale",
                  "G switches scale. At gameplay scale worlds are drawn large and close so a system can be read at a glance; " +
                  "at realistic scale everything drops back to its true size and distance, and most things become icons. " +
                  "The simulation itself is the same at both."),
                T("What is drawn",
                  "Orbits, labels, hyperlanes and satellites each toggle on their own. Far or small things fade from model to " +
                  "simpler model, to icon, to one marker for a fleet, so the view stays readable at any distance."),
                T("The sky",
                  "Every system has a skybox behind it. Shift+K (or numpad *) moves the system in view on to the next sky and " +
                  "Shift+Alt+K (or numpad /) back; each system keeps the sky you gave it, in your profile, Sol or any other. " +
                  "Until you pick one, Sol shows the real sky (NASA's Deep Star Map, every star where it really is) and every " +
                  "other system its own nebulae, made from its seed so the same star always looks the same. The skies: this " +
                  "system's own nebulae; Real sky: NASA Deep Star Map; Real sky: ESO Milky Way panorama; Blue nebulae; Hazy " +
                  "nebulae; Deep starfield; Ringed giant (8K panoramas from Space Spheremaps); and Bare starfield, the old black " +
                  "sky with point stars. Settings > Graphics sets the default sky, its brightness and how finely seeded nebulae " +
                  "are made. Your own 2:1 panoramas (.jpg or .png) dropped into the Skyboxes folder of the game's data folder " +
                  "(on Windows %USERPROFILE%\\AppData\\LocalLow\\MindAttic\\Unity.SpaceGod\\Skyboxes) join the cycle as " +
                  "'Your sky' the next time a game starts.")),

            new ManualChapter("Flying", "Taking a helm, the flight models, propellant, lanes and gates", Keys("HELM AND FLIGHT"),
                T("Any ship, anywhere",
                  "Select a ship and press P, whether it is parked, in transit, on a lane, queued at a ring, landed or in a " +
                  "battle. V spawns any hull you pick from a grid at the camera and puts you at its helm. P again hands the " +
                  "ship back to its crew, who carry on from where it is."),
                T("A practice fighter",
                  "Shift+V (or Practice Fighter Here on the V screen) puts a fighter of your own side at the camera, in any " +
                  "view and any system: deep space, beside a station, among a fleet or over a world. It starts just clear of " +
                  "whatever is there and you are straight at its helm, so you can fly alongside other hulls to see how big " +
                  "they are and practise the flight. Each system keeps one: pressing again brings the same fighter, mended " +
                  "and refuelled, to wherever the camera is now, so practice never fills your saves with spare hulls. A " +
                  "practice fighter lost, taken or still in a fight is left to it and a new one takes its place."),
                T("Newtonian flight",
                  "Every hull has mass, a main drive, weaker retro thrusters and manoeuvre thrusters, so it carries its " +
                  "momentum. The mouse steers, W and S thrust, A D Q E strafe, Z and X roll, Shift is the afterburner. Flight " +
                  "assist (on by default) counter-thrusts to hold the velocity you ask for; F turns it off for raw thrust, " +
                  "sliding past a target or flipping end for end to brake. Near a world its gravity pulls on the hull."),
                T("A flight model per family",
                  "Light fighters carve their track after the nose and turn tightest at their corner speed; interceptors push " +
                  "that further (their grip, strafe, corner speed and boost turn penalty, by speed or only while boosting, are " +
                  "in Settings > Dogfighting, and enemy crews fly the same model); bombers are a steady torpedo platform. Heavy fighters (the gunship) turn at one stiff rate at " +
                  "any speed that starts and stops crisply, settle on the throttle and strafe through a short ease, and carry " +
                  "a boost through the target, bleeding it off over a couple of seconds. Capital ships ring an engine telegraph a step at a " +
                  "time (Full Astern, Stop, Slow, Half, Full, Flank), turn best at Half and trace a turning circle; pitch, yaw " +
                  "and roll are all free, slow and heavy. Freighters, miners and the other work hulls fly heavy on what they " +
                  "carry. With assist off, every hull flies plain Newtonian."),
                T("Every role at the helm",
                  "Every ship can be flown, and each is flown with the controls its work wants. A fighter's mouse is its gunsight " +
                  "(the hull turns after the crosshair, or the mouse turns the nose itself on a heavy fighter or bomber); a courier " +
                  "flies a fighter's stick on the longest boost, to run rather than fight; an escort taken from a formation is cut " +
                  "loose from it and flies as its class does; freighters and miners have a slower mouse, fly heavy on their load " +
                  "and dock on Ctrl; capital ships ring the engine telegraph and turn on the keys while the mouse looks round. " +
                  "Taking a helm names the role and its keys."),
                T("Flying a hauler",
                  "A freighter or miner is heavy, and heavier laden: what is in its hold and its tanks slows its drive, its " +
                  "braking and its turn, so a full hull is noticeably slower than an empty one. Its retros are weak, and the " +
                  "HUD shows the stop point, where full retro would bring it to rest and how long that takes, so start " +
                  "braking early. Inside a port's approach, hold Ctrl for docking mode: the throttle asks only for docking " +
                  "speed and the nose turns gently, for a clean docking. Its strafe is weak and its boost a short emergency " +
                  "burn, not a dodge."),
                T("Cruise",
                  "Hold J flying straight at full throttle and the cruise drive spools up to many times normal speed. It " +
                  "drops out if you take damage, enter a battle, throttle down or turn hard, and the guns cannot fire in it."),
                T("Propellant",
                  "Every change of speed is paid for in reaction mass, by the rocket equation, and a dry hull only coasts. The " +
                  "status bar shows the tanks and the ship panel the delta-v left. Refuel on the market board at a port, or " +
                  "call a tanker when low, at a call-out fee and a steep price."),
                T("Collisions",
                  "Hulls, stations, asteroids and debris have real volumes. A hit exchanges momentum and does damage scaled " +
                  "to the hull's own mass, so a courier that rams a battleship is wrecked and the battleship scratched. " +
                  "Bumps under 5 m/s do nothing."),
                T("Sensors and silent running",
                  "At the helm you see only what your scanners hold: an unknown blip, then a class, then a named, coloured " +
                  "contact. Your own signature jumps when the drive lights. F3 runs silent: transponder off and heat damped, " +
                  "though a burn still shows."),
                T("Hyperlanes",
                  "Fly through a ramp's ring gate to ride the lane at up to 4% of light, the clock running fast. On the lane, " +
                  "1 comes off at the next ramp and 2-9 carry on along another lane. Raiders can lie beside a lane with an " +
                  "interdiction pulse: Shift surges through, Ctrl drops out short, or ride into it and fight. Y gets back " +
                  "into a lane where you left it."),
                T("Docking and landing",
                  "Merchants and couriers land on populous worlds through docking rings over their cities: approach, queue, " +
                  "descend tail-first and trade. Warships hold orbit."),
                T("Derelicts and wrecks",
                  "Come alongside a derelict or wreck, match velocity (within about a kilometre and 20 m/s) and press 1 to " +
                  "latch on. Fight off any raiders aboard, then salvage cargo, strip a component or claim the hull with a " +
                  "prize crew.")),

            new ManualChapter("Ships: strike craft", "Fighters, interceptors, bombers, heavy fighters, recon and boarding craft", Keys(),
                T("Space superiority fighter",
                  "The balanced workhorse: guns and four missiles, the carving Star Conflict style turn. Flown from a " +
                  "carrier's squadron or on a navy patrol wing."),
                T("Interceptor",
                  "Fast, the tightest turn and the hardest strafe, light guns and dogfight missiles. It hunts fighters and " +
                  "goes first for one closing on its own bombers."),
                T("Bomber",
                  "Slow and armoured, with two torpedoes for capital ships. Its torpedo does three times the damage on bare " +
                  "hull where the shield is down, and two through an open reactor vent bring down a battleship. It needs " +
                  "an escort."),
                T("Heavy fighter / gunship",
                  "Tougher and stiffer, guns and four missiles; a carrier with a fourth squadron flies them, and gunships " +
                  "lead navy wings and escort convoys."),
                T("Recon / EW fighter",
                  "A pair flies from every carrier: they hold off the enemy capital its bombers are going for, jam every ship " +
                  "near it (its gunners fire less accurately) and paint it, so the bombers' torpedoes steer harder onto it."),
                T("Boarding shuttle",
                  "Armoured and unarmed; a carrier keeps two. It carries a team of 24 marines to a disabled hull or a " +
                  "silenced station or gate fort, latches on and lets them in through the airlock, so the prize is taken intact. " +
                  "Each is a craft of its own, docked in its carrier's shuttle berths: it comes home as itself, keeps its " +
                  "pilot and its damage until a yard mends it, and only one lost is built anew."),
                T("Flight decks",
                  "Carriers launch their craft down fore-and-aft flight decks and out of the bow. Craft come home by flying " +
                  "the glide path in through the stern, are refuelled, rearmed and repaired, and launch again. At the stick " +
                  "you can land on a friendly deck the same way; the HUD gives the guidance."),
                T("Dogfighting keys",
                  "Left mouse guns, right mouse the second trigger's missiles or torpedoes (1 cycles them), T locks the hostile " +
                  "nearest the crosshair, Ctrl throws flares and chaff, 7 / 8 / 9 move reactor power to weapons, shields or " +
                  "engines and 0 evens it, 2-5 order your wingmen, and 6 picks a capital's next weak point.")),

            new ManualChapter("Ships: capital ships", "Carriers, battleships, command cruisers and gate breakers", Keys(),
                T("Carrier",
                  "Has its own FTL drive. Launches and recovers fighter, bomber, recon and boarding-shuttle squadrons, and can " +
                  "carry frigates clamped to its hull. Losing it strands its wings, which look for another deck or a friendly port."),
                T("Battleship / dreadnought",
                  "Built around two spinal mounts down the keel, a railgun and a coilgun, each fed by a spinal power plant; a " +
                  "yard may fit a tachyon lance or plasma spinal cannon instead. It holds the rear line, sniping from the " +
                  "extreme reach of its spinal guns with its bow on the mark and backing from anything that closes."),
                T("Command cruiser / battlecruiser",
                  "Gives up most of a cruiser's guns for a sensor array, a communications suite and targeting computers. " +
                  "Ships on its side within reach find targets faster, shoot truer and concentrate on its mark; lose it and " +
                  "they are thrown into confusion for a while, so enemy gunners go for it first."),
                T("Gate breaker",
                  "The siege capital: the heaviest armour afloat, the slowest drive, plasma siege guns, the biggest marine " +
                  "barracks, boarding shuttles and engineering teams. A war fleet with one takes an enemy jump gate intact " +
                  "the day its forts are silenced. It always has its own FTL drive."),
                T("Capital handling",
                  "All capitals fly the engine telegraph and turn slowly; a destroyer comes about in about 6 seconds at Half, " +
                  "a carrier in 18. Big ships shed heat through radiator panels that gunners can shoot away.")),

            new ManualChapter("Ships: escorts and sub-capitals", "Cruisers, destroyers, frigates, corvettes and gate pickets", Keys(),
                T("Cruiser",
                  "Long-endurance patrol on the gates and lanes, needing no support fleet. Can carry phaser arrays. No FTL drive."),
                T("Destroyer",
                  "The anti-fighter and point-defence screen, with a fixed spinal railgun and phaser arrays it can tune between " +
                  "a narrow lance and a wide sweep. Leads destroyer wings and lane picket wings."),
                T("Frigate",
                  "A missile and torpedo boat, the fastest warship hull and the thinnest. It fights by the alpha strike: " +
                  "burns in holding every launcher, looses them all at once, flips and burns back out while they reload. " +
                  "No FTL; it crosses by the gates or clamped to a carrier."),
                T("Corvette",
                  "System defence with no FTL: deep tanks, a long-reach patrol search suite and a brawler's guns. Every navy " +
                  "stations corvettes at its ports; they ride the home system's lanes as the local law, hailing and scanning " +
                  "traffic and running down smugglers and pirates."),
                T("Gate picket",
                  "A small, sensor-heavy hull posted just inside each gate a navy holds. It sees hostiles coming through the " +
                  "gate or dropping out of FTL nearby and calls a group to meet them, so raiders shoot it first."),
                T("Lane interdictor",
                  "Not a class but heavy equipment: an interdiction field projector fitted into any hull big enough (a " +
                  "destroyer, cruiser or converted freighter). It pulses ships out of a lane. Every raider band sails with " +
                  "one, and navies fly lane picket wings with one.")),

            new ManualChapter("Ships: logistics and civilian craft", "Freighters, haulers, couriers, tugs, tenders and private craft", Keys(),
                T("Bulk freighter",
                  "A truss with its engine astern and cargo pods and fuel spheres clamped along it; a yard adds or removes " +
                  "pods and spheres. It never lands, carries no armour or weapons (its escorts carry the guns), rides the " +
                  "lanes at a bulk toll and is slow to speed up and slow down off them."),
                T("Ore haulers and couriers",
                  "Ore haulers carry raw ore between works and smelters on a weak drive; couriers are small and fast and " +
                  "carry urgent cargo and surveys. Both land at docking rings."),
                T("Ore tugs and capture tugs",
                  "An engine with claws. Ore tugs push rocks and comet ice hundreds of metres across over weeks, through " +
                  "open space or on a heavy lane; capture tugs bring smaller rocks in to a refinery's grinders. Each is a " +
                  "real hull you can take the helm of, and raiders can seize it only where it is."),
                T("Lane tenders",
                  "Unarmed maintenance hulls that keep a lane's rings powered and aligned. A lane with nobody tending it " +
                  "wears, rides slower, stalls and finally goes dark, so tenders are a prime target in war."),
                T("Colony ships, convoys and supply hulls",
                  "Colony ships carry settlers on their voyage's timetable; navies run supply convoys of tankers and colliers " +
                  "to their fleets; belt works send material convoys to their depots. All are real hulls that fly the trip."),
                T("Private craft",
                  "Yachts, survey ships, deep-space explorers and prospectors go where they please. Every inhabited port " +
                  "also has its local traffic: buses, taxis, skiffs, runabouts and tourers flying round its own planet, " +
                  "never in a fight.")),

            new ManualChapter("Combat and boarding", "Battles, broadsides, weapons, shields, surrender and boarding", Keys("COMBAT"),
                T("How a battle starts",
                  "Wherever hostile forces meet under orders that put them there: groups, merchants, a port's defence " +
                  "platforms. Flying near hostiles starts or joins one. Battles are fought in a local space tens of " +
                  "kilometres across, every hardpoint picking its own target."),
                T("Weapons",
                  "Railgun and gun rounds fly a lead-compensated line and land only when they arrive; beams sweep onto their " +
                  "target; missiles and torpedoes home. Phaser arrays tune between a lance for hulls and a sweep for fighters " +
                  "and missiles; plasma cannons hit hardest close in; the tachyon lance lands the instant it fires. Point " +
                  "defence shoots down missiles and fighters. Every weapon aims within a fire cone in full 3D."),
                T("Shields and armour",
                  "Shields come from emitters on the hull, each covering the patch around it; a patch shot down is holed while " +
                  "the rest stays covered, so gunners go for the weak spot. Shots that meet the plate slanted glance off, so " +
                  "angling the hull to the enemy's line pays. A warship's bridge is buried at its centre."),
                T("Broadsides at a capital's helm",
                  "Look at a side and hold left mouse to build a volley with every gun that bears, release to fire. The number " +
                  "keys are the captain's orders: battery orders, focus or spread fire, brace, damage control focus, the " +
                  "formation of your own warships, and all batteries at will. Shift+Delete has the helm roll weak shield " +
                  "patches away from the heaviest fire."),
                T("Heat",
                  "Big ships shed heat through radiators. Lose enough and the crew throttles the guns and drive; Shift+Insert " +
                  "runs hot instead, at full past the limit, at a cost in crew and systems."),
                T("Hails, surrender and wrecks",
                  "In a battle the number keys answer hails: to a pirate demand, 1 hands over the cargo and 2 refuses; to an " +
                  "enemy that strikes its colours, 1 demands its cargo, 2 takes the ship and 3 lets it go. A destroyed ship " +
                  "leaves a wreck with its cargo as salvage, and its survivors in escape pods that whoever finds them decides the fate of."),
                T("Boarding",
                  "Every hull has an interior of compartments with crew and marines in them. A disabled ship can be boarded " +
                  "through the airlock, by boarding pods, by a carrier's shuttles or by a Breacher round fired from a Breacher " +
                  "bay. The fight goes compartment by compartment; holding the bridge and engineering takes the ship. While " +
                  "your hull is being boarded, or your marines are boarding, B opens the boarding panel: pick a compartment " +
                  "and push for it or muster on it, hold, or auto-resolve."),
                T("Boarding with allies",
                  "Marines of allied factions on the same side of a battle can board one hull together. They fight its " +
                  "defenders as one party, as well as the mix of them who landed, and your orders from the boarding panel " +
                  "command all of them. A faction on the other side still cannot land on a hull someone is boarding. Once the " +
                  "hull is taken, each ally's claim is the fighting strength it sent aboard. The cargo is split by claim. The " +
                  "hull goes to the largest claimant that can pay the others their share of its worth, and if none can, it " +
                  "is sold to the breakers and the money is shared."),
                T("Taking any seat",
                  "From the god view ' jumps into the nearest seat in the fight in view and ' and ; step through the rest; / " +
                  "mans the next gun. P hands the seat back to its AI.")),

            new ManualChapter("Trade", "Markets, cargo, contracts, the trade desk and outfitting", Keys("TRADE"),
                T("The economy",
                  "Sixteen goods are mined, made and eaten across the settlements, each priced from its own stock against what " +
                  "it uses. AI freighters carry whatever earns most per day, so goods flow from surplus to shortage, and " +
                  "shocks (mine accidents, blights, strikes, blockades, embargoes) move prices for real."),
                T("Buying and selling",
                  "Near a settlement's world the market board (M) has Buy and Sell, trading whole crates at the prices and tax " +
                  "AI traders pay, and Refuel. I opens the grid cargo hold: click a crate, then a cell, to move it."),
                T("Contracts",
                  "Every lawful port posts jobs from what the system needs: freight, passengers and colonists, escorts, " +
                  "patrols, surveys and bounties (F7). Completing one pays and raises standing with the patron; failing one costs it."),
                T("The trade desk",
                  "The backslash key opens it: the runs your known prices add up to (prices you saw, your captains report or a " +
                  "broker sells, each ageing), trade news, time charters of your hulls to the companies, insurance, warehouses " +
                  "and the back door, where smugglers buy what the open market will not, at the risk of customs."),
                T("Outfitting",
                  "At a shipyard F2 (or Outfit on the market board) opens the hull's hardpoints and internal space: guns, " +
                  "shields and drives on the hardpoints; plate, cargo racks, scanners, a brig, marine habitats and more inside. " +
                  "Every part is bought from that yard's market, and every tonne costs acceleration."),
                T("The news",
                  "X opens the news feed: shocks, price spikes, embargoes, battles and captures as they happen, with news from " +
                  "other systems as old as the gates made it.")),

            new ManualChapter("Diplomacy and the law", "States, treaties, crime, bounties and patrols", Keys("DIPLOMACY AND THE LAW"),
                T("States and opinion",
                  "Every state holds an opinion of every other, drifting with wars, pacts, common enemies and strength. They " +
                  "sign non-aggression pacts, trade pacts, alliances, tribute and vassalage, and the AI puts deals to you each month."),
                T("Treating with a state",
                  "F12 hails the polity of the ship you have locked or selected and opens the envoy; the galaxy map's " +
                  "DIPLOMACY panel does the same for any state. Each offer shows how it would be taken. Breaking a pact is " +
                  "noticed by everyone."),
                T("Crime",
                  "Attacking a ship you are not at war with, piracy, smuggling, trespass and running from a scan are crimes, " +
                  "but only if someone's scanners saw them. A crime posts a bounty and lowers your standing, which sets your " +
                  "prices and whether a port lets you dock."),
                T("Patrols and scans",
                  "Navy patrols and corvettes hail you in their space for a cargo scan: hold still. Wanted, you are ordered to " +
                  "heave to and pay (1) or be interdicted. Fines can be paid at any of a faction's ports."),
                T("Bounties and outlaws",
                  "Every raider captain has a record and a price. The bounty board (F4) lists who is wanted, where last seen " +
                  "and who hunts them. A bounty pays in full alive (board the flagship and hand the captain over) and half " +
                  "dead, on proof from close to the wreck. Bounty hunters come for you too once your price is high enough.")),

            new ManualChapter("Fleets and command", "Your house, your fleet, and commanding a polity", Keys("FLEET AND COMMAND"),
                T("Your house",
                  "F10 opens your fleet: buy hulls at a lawful yard or take the one you fly, hire captains and give them " +
                  "standing orders (trade routes, mining a claim, escorts, patrols, free trading). Buy warehouses, station " +
                  "shares and mining claims. Every credit is on the books, and your house is ranked by what it is worth."),
                T("Commanding a polity",
                  "On the galaxy map's COMMAND panel any polity with a navy can be steered: a production focus, a build plan " +
                  "and a research line, and objectives for one squadron, a system's groups or the whole navy (take, hold, " +
                  "blockade, escort, explore). Without a directive a polity runs itself."),
                T("How navies fight",
                  "Gate fleets march through the gates and are met at the exits; jump strike groups (a carrier and FTL capitals " +
                  "with escorts docked) drop out on a system's flank. Navies keep patrol wings, supply convoys, corvettes and " +
                  "gate pickets, and send groups to clear raiders off their gates."),
                T("Taking a seat in a fight",
                  "' jumps into the fight in view, ; and ' step through its seats, / mans a gun, P returns you to command with " +
                  "the view and warp you had."),
                T("Manning a gun",
                  "/ mans the next gun on the hull you fly or have selected, or on a station's defence platform when the station " +
                  "is selected while its defences fight. The view sits on the mount, looking down the barrel: the mouse lays it " +
                  "(a heavy gun swings slowly, and only within its arc) and the left button fires it. / again steps to the next " +
                  "gun, Backspace moves up to the hull's helm, and P hands it back to its crew.")),

            new ManualChapter("The galaxy and travel", "Star systems, gates, lanes, FTL and the galaxy map", Keys(),
                T("The systems",
                  "Sol on its real orbits, Alpha Centauri with its colonies and factions, and twelve more of the nearest real " +
                  "systems; past them the galaxy runs to the rim, real catalogue stars near Sol and stars generated from the " +
                  "galaxy's seed everywhere else, charted as you go."),
                T("Hyperlanes",
                  "Inside a system, lanes of accelerator rings link the planetary systems and the jump gates. They carry most " +
                  "traffic cheaply but predictably, so raiders ambush them, and they need tenders to keep running."),
                T("Jump gates",
                  "Gates stand at a system's edge, each facing the star it leads to. Fly within 300 km and press Y to come out " +
                  "at the far gate, unless it is shut to you. Whoever holds a gate controls its traffic, and gates shut after raids."),
                T("Your own FTL drive",
                  "Carriers, gate breakers, fitted battleships and command cruisers and deep-space explorers have their own " +
                  "drive. No jump is possible inside a system's gravity wells: fly out beyond the outer rim, put the nose on " +
                  "another star and press Enter to spool, holding the nose on it. PgDn / PgUp or the galaxy map plot a course " +
                  "with its ETA and fuel, and End engages the FTL autopilot, compressing the trip on the clock."),
                T("Deep space",
                  "Space between the stars is simulated: void stations, gravity projectors that pull ships out of FTL, and the " +
                  "traffic crossing it."),
                T("The galaxy map",
                  "[ opens it: every system at its real place, who holds it, gate traffic, war fronts, blockades and raiders as " +
                  "far as the news goes. Click a star to inspect it, double-click to travel; DIPLOMACY and COMMAND are its panels."),
                T("Information lag",
                  "News travels at light speed inside a system and through the gates between them, so what anyone knows of a " +
                  "distant place is as old as the news that brought it.")),

            new ManualChapter("Settings", "Everything you can change from the game", Keys(),
                T("The settings menu",
                  "Settings, from the main menu or the Esc menu, lists every tunable the game's systems declare by group: " +
                  "Graphics, Render pipeline, LOD & render budget, Simulation, Economy, Controls, Audio, Dogfighting, " +
                  "Broadsides, Weapons, FTL, AI, Civilizations and more. Search finds a setting across all of them."),
                T("Changing and resetting",
                  "Each change takes effect at once and is kept in your profile. Reset puts back one setting, a group or " +
                  "everything found."),
                T("Post-processing (URP)",
                  "On URP, Render pipeline's post-processing settings put URP's own Volume stack over the view: HDR, so the " +
                  "star's face glares and blooms above white; exposure fixed, automatic or on a histogram (both adapt like an " +
                  "eye, the histogram ignoring black space and the star's face), or a physical camera's ISO, shutter and " +
                  "aperture against sunny 16; a tone curve (none, Neutral or ACES) and contrast, saturation and white balance. " +
                  "Fresh profiles keep the image as drawn, only the star glaring. Post-processing off draws it with none."),
                T("Display",
                  "Display has its own page: resolution, window mode and VSync."),
                T("Profiles",
                  "Each profile keeps its own settings and saves; switch or make one from Profiles on the main menu.")),

            new ManualChapter("Screens and saving", "The HUD, the panels, the pause menu and saves", Keys("SCREENS AND PANELS"),
                T("The HUD",
                  "U hides the HUD panels and readouts (the names and markers in the view stay); F11 clears everything but " +
                  "the reticle in use. Esc closes panels, then opens the pause menu."),
                T("The pause menu",
                  "Resume, Help, Save / Load, Settings, Main Menu (with an autosave) and Quit."),
                T("Saving",
                  "The whole simulation saves and resumes exactly where it was. F5 quicksaves and F8 loads it; F6 opens the " +
                  "save slots, where you can make as many saves as you like. The game autosaves every five minutes and on " +
                  "quit, keeping three, and saving waits while an FTL jump is under way.")),

            new ManualChapter(EveryKeyTitle, "The whole reference card: every key and control, by what it is for", null),
        };

        /// <summary>The key groups a chapter prints: its own, or every group for <see cref="EveryKeyTitle"/>.</summary>
        public static IEnumerable<HelpSection> ControlsOf(ManualChapter chapter) =>
            chapter.Title == EveryKeyTitle ? ControlsHelp.Sections : chapter.Controls();

        /// <summary>The chapter of that title, or null.</summary>
        public static ManualChapter Find(string title)
        {
            foreach (var c in Chapters)
                if (c.Title == title)
                    return c;
            return null;
        }
    }
}
