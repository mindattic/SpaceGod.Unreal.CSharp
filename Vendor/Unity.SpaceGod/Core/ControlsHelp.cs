using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>One line of the Help screen: the keys (empty for a line that carries on the one above) and what they do.</summary>
    public readonly struct HelpLine
    {
        public readonly string Keys;
        public readonly string Text;

        public HelpLine(string keys, string text)
        {
            Keys = keys ?? "";
            Text = text ?? "";
        }

        public override string ToString() => Keys.Length == 0 ? "   " + Text : Keys + "   " + Text;
    }

    /// <summary>A group of the Help screen's lines under one heading.</summary>
    public sealed class HelpSection
    {
        public readonly string Title;
        public readonly IReadOnlyList<HelpLine> Lines;

        public HelpSection(string title, params HelpLine[] lines)
        {
            Title = title;
            Lines = lines;
        }
    }

    /// <summary>
    /// Every key, control and help line of the game, in groups (U.SPC-T494): what the Help screen prints, from the
    /// main menu, the Esc menu or H / F1 in game. A new key gets its line here, in the group it belongs to.
    /// </summary>
    public static class ControlsHelp
    {
        static HelpLine L(string keys, string text) => new HelpLine(keys, text);
        static HelpLine More(string text) => new HelpLine("", text);

        public static readonly IReadOnlyList<HelpSection> Sections = new[]
        {
            new HelpSection("CAMERA AND GOD VIEW",
                L("W A S D", "swim"),
                L("Q / E  or  C / ]", "down / up"),
                L("Shift", "boost"),
                L("Ctrl", "fine"),
                L("Wheel", "swim speed / zoom"),
                L("Right-drag", "look / orbit"),
                L("Left-click", "select (double-click: focus)"),
                L("F", "focus the selection"),
                L("Home", "system view"),
                L("1-9, 0", "planets (twice to focus)"),
                L("Tab", "cycle ships"),
                L("B", "watch a battle"),
                L("Space", "pause (eases to a stop and back)"),
                L(",  .", "time warp slower / faster"),
                L("T", "back to now"),
                L("G  or  `", "toggle GAMEPLAY / REALISTIC scale"),
                L("R", "auto-reorient on / off (keep this system's plane level)"),
                L("O", "orbits"),
                L("L", "labels"),
                L("N", "hyperlanes"),
                L("K", "satellites"),
                L("Shift+K  or  numpad *", "skybox: the next sky for the system in view, which keeps it (Shift+Alt+K or numpad / goes back)"),
                L("Navigator", "STAR SYSTEMS: fly to another star (Esc to skip the trip)"),
                L("Esc", "close panels / leave focus / deselect, then the pause menu")),

            new HelpSection("HELM AND FLIGHT",
                L("P", "pilot the selected ship (P again hands it back)"),
                L("V", "spawn & pilot: pick a hull from the grid to spawn and pilot it at the camera"),
                L("Shift+V", "practice fighter: one of your fighters at the camera, clear of what is there, you at its helm, to fly beside other hulls and get the feel of them (again brings the same one over)"),
                L("Mouse", "steer"),
                L("W / S", "thrust"),
                L("A / D  Q / E", "strafe"),
                L("Z / X", "roll"),
                L("F", "flight assist (off: drift, turn and burn)"),
                More("fighters: the mouse aims and the hull turns after it"),
                More("heavy fighters, freighters: the mouse turns the nose"),
                More("capital ships: A/D Q/E turn the hull, the mouse looks around"),
                L("Ctrl", "freighters, miners, in a port's approach: docking mode (docking speed, a gentle turn)"),
                L("Ring gate", "hyperlanes: fly through a ramp's ring gate to ride the lane"),
                L("1  /  2-9", "on the lane: 1 come off, 2-9 carry on at the off-ramp"),
                L("Shift", "interdiction (riding): a raider's field ahead, surge through"),
                L("Ctrl", "interdiction (riding): drop out short of the field"),
                L("Y", "back into the lane where you left it"),
                L("Enter", "FTL (capital ships, survey hulls): beyond the system's outer rim, put the nose on another star, Enter to spool (hold it there); it engages for that star"),
                L("PgDn / PgUp", "plot a course to the next star or beacon (nearest first), or pick one on the galaxy map ([): ETA, fuel, wells on the way"),
                L("Del", "clear the plotted course"),
                L("End", "FTL autopilot: flies out beyond the rim, turns onto the star, spools and engages, time compressed (Settings > FTL); End again takes the helm back"),
                L("F3", "silent running: transponder off, heat damped -- only what your scanners hold is shown: blips, then class, then name"),
                L("1, then 1-4", "derelicts and wrecks: match velocity alongside, 1 latch on, then 1-4 as the panel shows")),

            new HelpSection("COMBAT",
                L("Left mouse", "guns, down the crosshair (watch the heat)"),
                L("T", "lock the hull under the crosshair; hold the crosshair on it to lock"),
                L("Right mouse", "missiles"),
                L("Middle mouse", "EMP (knocks out a small craft's drives and shields)"),
                L("Ctrl", "drop a mine astern; an auto turret fires on its own"),
                L("Insert", "retune the phaser arrays: a narrow lance for hulls or a wide sweep for fighters and missiles"),
                More("a plasma cannon hits hardest close in"),
                L("Hold left mouse", "capital ships: look at a side and hold to build a broadside, let go to fire it"),
                More("the turrets fight on their own, at the locked hull first"),
                More("captain's orders, to the side you look at:"),
                L("1", "that battery at will / hold fire / volley"),
                L("2", "gunners: focus the weakest shield patch / spread fire"),
                L("3", "brace"),
                L("4", "damage control focus"),
                L("5  6  7", "line ahead, line abreast, echelon"),
                L("8  9", "screen, engage at will"),
                L("0", "all batteries at will"),
                More("angle the hull to the enemy's line to make shots glance"),
                L("Shift+Insert", "big ships: run hot (guns and drive at full past the heat limit, the crew and systems cook) or throttle for the heat"),
                More("shoot a hull's radiators away to make it overheat"),
                L("Shift+Del", "capital ships: the helm rolls your weak shield patches away from the heaviest fire (the readout above the orders names them)"),
                L("B", "boarding (a fight aboard your hull, or one your marines are in): the panel of compartments and orders"),
                L("1  2  3  0", "with it open: pick a compartment, push for it / muster on it, hold, auto-resolve")),

            new HelpSection("TRADE",
                L("M", "market: Buy / Sell and Refuel appear while piloting near the shown settlement's body"),
                L("I", "cargo hold (while piloting): click a crate, then a cell, to move it"),
                L("\\", "trade desk: runs from your price intelligence, brokers, trade news, charters, insurance, warehouses, the back door"),
                L("F7", "contracts board of the port in view: freight, passengers, colonists, escorts, patrols, surveys, bounties"),
                L("F2", "outfitting at a shipyard (or Outfit on the market): guns, shields and drives on the hardpoints; plate, racks, scanners, a brig in the internal space"),
                L("X", "news feed: shocks, price spikes, embargoes, battles and captures as they happen")),

            new HelpSection("DIPLOMACY AND THE LAW",
                L("F12", "hail the polity of the ship locked or selected (or the patrol hailing you) and treat with it"),
                L("[", "the galaxy map's DIPLOMACY panel: pacts, tribute, fealty, war and peace"),
                L("T, then fire", "lock a ship, hold fire on it to attack (a crime if seen)"),
                More("patrols scan: hold still"),
                L("1", "pay a fine when hailed"),
                L("F4", "bounty board of the port in view: outlaws wanted there, their records, where last seen, who hunts them")),

            new HelpSection("FLEET AND COMMAND",
                L("F10", "your fleet: buy hulls, warehouses, station shares and claims; hire captains and give them standing orders"),
                L("'  ;", "take charge, any time: jump into the next seat in the fight in view (a ship's helm, a fighter or bomber)"),
                L("/", "man the next gun on the hull (or on a station's defence platform, selected): the view sits on the mount, down the barrel"),
                L("Backspace", "at a gun: up to the helm of the hull it is on (or the whole station)"),
                L("F9", "take-charge picker: every seat on the hull in hand and around the camera (helms, fighters, gun mounts), click one to jump in"),
                More("in a fighter: mouse aims, W/S throttle, left mouse guns, right mouse torpedoes"),
                L("P", "hands the seat back to its AI and returns you to command")),

            new HelpSection("SCREENS AND PANELS",
                L("H  or  F1", "this help screen"),
                L("Esc", "pause menu (straight away at the helm): Resume, Help, Save / Load, Settings > Display (resolution, window mode, VSync), Main Menu, Quit"),
                L("[", "galaxy map: every system, who holds it, gate traffic, war fronts, blockades and raiders, as far as the news goes; click a star to inspect, double-click to travel"),
                L("U", "HUD panels and readouts on / off (the names and markers on the view stay)"),
                L("F11", "clear the screen: every bit of UI gone but the reticle in use; F11 again (or opening a panel) brings it all back"),
                L("F5", "quicksave"),
                L("F8", "quickload"),
                L("F6", "save slots"),
                More("autosaves every 5 minutes and on quit; -continue resumes the latest")),
        };

        /// <summary>Every line of every section, in order.</summary>
        public static IEnumerable<HelpLine> AllLines()
        {
            foreach (var s in Sections)
                foreach (var l in s.Lines)
                    yield return l;
        }
    }
}
