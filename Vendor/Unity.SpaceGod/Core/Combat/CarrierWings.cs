using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>The pilot of one of a carrier's craft (U.SPC-T526): how good a shot, how quick to react (seconds of lag), how
    /// tidy in formation (0..1), and the wander that makes their flying their own. Rolled the first time the craft launches and
    /// kept with it from then on, fight after fight, until the craft is lost.</summary>
    public sealed class CraftPilot
    {
        public double Skill, Reaction, Discipline, Seed;

        /// <summary>Times this pilot has launched in this craft.</summary>
        public int Sorties;
    }

    /// <summary>
    /// What makes a <see cref="Ship"/> one of a carrier's craft (U.SPC-T526, see <see cref="CarrierWings"/>): its class, the
    /// berth it holds in its carrier's hangar (and so the squadron it flies in), and its pilot. Null on any other hull.
    /// </summary>
    public sealed class WingCraft
    {
        public FighterRole Role;

        /// <summary>Its berth in the hangar it is docked in: an index into that carrier's layout (<see cref="CarrierWings.Berth"/>).</summary>
        public int Berth;

        /// <summary>Its pilot, or null until it first launches.</summary>
        public CraftPilot Pilot;

        /// <summary>The carrier this craft last launched from (U.SPC-T591): where a stray set down on a station or at a port
        /// goes back to when that carrier calls there.</summary>
        public Ship Carrier;
    }

    /// <summary>
    /// A carrier's wing as real craft (U.SPC-T526): every fighter, interceptor, bomber and recon craft a carrier carries is a
    /// <see cref="Ship"/> of its own class, docked in the carrier's hangar (<see cref="ShipCombat.Hangar"/>), which goes
    /// wherever the carrier goes, through a gate, a cruise or its own jump. When a fight starts each craft still fit to fly
    /// launches as itself (<see cref="FighterAI.Launch"/>), flying on its own hull (<see cref="Ship.Combat"/>: its own damage
    /// and rounds) with its own pilot; one that lands, or is taken back aboard when the fight ends, docks again as itself,
    /// rearmed and its shields recharged by the deck crews but its damage carried until a yard mends it; one shot down is
    /// gone. A yard builds new craft only for the berths left empty (<see cref="ShipCombat.RestoreFighters"/>).
    ///
    /// A hangar's berths are laid out as its squadrons were always launched: the recon flight first
    /// (<see cref="CombatSpec.ReconCraft"/>), then squadrons of <see cref="SquadronSize"/> in
    /// <see cref="FighterCraft.SquadronMix"/>'s order, so a part-filled hangar holds the recon flight and the first squadrons.
    ///
    /// A carrier's boarding shuttles (U.SPC-T590, <see cref="BoardingShuttles"/>) are real craft the same way, in berths of
    /// their own beside the squadrons (<see cref="ShipCombat.ShuttleBay"/>, <see cref="CombatSpec.Shuttles"/> of them): each
    /// an assault shuttle hull with its own damage and pilot, sent out as itself with a marine team, docked again as itself
    /// when it lands, carried hurt until a yard mends it, lost as itself when shot down; a yard builds new ones only for the
    /// shuttle berths left empty (<see cref="ShipCombat.RestoreShuttles"/>).
    /// </summary>
    public static class CarrierWings
    {
        public const int SquadronSize = 6;

        /// <summary>The role berth <paramref name="berth"/> of a hangar laid out for <paramref name="spec"/> is built for, the
        /// squadron it flies in (-1 for the recon flight) and its place in that squadron.</summary>
        public static FighterRole Berth(CombatSpec spec, int berth, out int squadron, out int slot)
        {
            int recon = Math.Max(0, spec.ReconCraft);
            if (berth < recon)
            {
                squadron = -1;
                slot = berth;
                return FighterRole.Recon;
            }
            int j = berth - recon;
            squadron = j / SquadronSize;
            slot = j % SquadronSize;
            return FighterCraft.RoleOfSquadron(squadron);
        }

        /// <summary>A new craft of <paramref name="role"/>'s class for berth <paramref name="berth"/>: whole, loaded, its pilot not yet rolled.</summary>
        public static Ship NewCraft(FighterRole role, int berth, Faction faction = null)
        {
            var craft = FighterCraft.Of(role);
            return new Ship
            {
                Id = -1, Name = char.ToUpperInvariant(craft.Name[0]) + craft.Name.Substring(1) + " " + (berth + 1), Spec = ShipClassSpec.Of(craft.Class), Faction = faction, Role = ShipRole.Escort,
                Phase = ShipPhase.Parked, Combat = new ShipCombat(craft.Spec, craft.Class),
                Craft = new WingCraft { Role = role, Berth = berth },
            };
        }

        /// <summary>Whether <paramref name="s"/> is one of a carrier's craft.</summary>
        public static bool IsCraft(Ship s) => s?.Craft != null;

        /// <summary>The craft docked in <paramref name="c"/>'s hangar, in berth order.</summary>
        public static IReadOnlyList<Ship> Aboard(ShipCombat c) => c?.Hangar ?? (IReadOnlyList<Ship>)Array.Empty<Ship>();

        /// <summary>How many craft of <paramref name="role"/> are docked aboard.</summary>
        public static int Aboard(ShipCombat c, FighterRole role)
        {
            int n = 0;
            if (c?.Hangar != null)
                foreach (var s in c.Hangar)
                    if (s.Craft.Role == role) n++;
            return n;
        }

        /// <summary>Builds new craft for the first <paramref name="count"/> empty berths of <paramref name="c"/>'s hangar (as many
        /// as it has empty), each of the class its berth is laid out for.</summary>
        public static void Fill(ShipCombat c, int count)
        {
            if (count <= 0) return;
            int berths = c.Spec.HangarCraft;
            var taken = new HashSet<int>();
            foreach (var s in c.Hangar) taken.Add(s.Craft.Berth);
            for (int b = 0; b < berths && count > 0; b++)
            {
                if (taken.Contains(b)) continue;
                Insert(c, NewCraft(Berth(c.Spec, b, out _, out _), b));
                count--;
            }
        }

        /// <summary>Builds new boarding shuttles for the first <paramref name="count"/> empty shuttle berths of
        /// <paramref name="c"/> (U.SPC-T590), as many as it has empty.</summary>
        public static void FillShuttles(ShipCombat c, int count)
        {
            if (count <= 0) return;
            int berths = Math.Max(0, c.Spec.Shuttles);
            var taken = new HashSet<int>();
            foreach (var s in c.ShuttleBay) taken.Add(s.Craft.Berth);
            for (int b = 0; b < berths && count > 0; b++)
            {
                if (taken.Contains(b)) continue;
                Insert(c.ShuttleBay, NewCraft(FighterRole.AssaultShuttle, b));
                count--;
            }
        }

        /// <summary>Takes shuttles out of the shuttle berths from the last until <paramref name="count"/> are left (U.SPC-T590).</summary>
        public static void TrimShuttles(ShipCombat c, int count)
        {
            count = Math.Max(0, count);
            while (c.ShuttleBay.Count > count) c.ShuttleBay.RemoveAt(c.ShuttleBay.Count - 1);
        }

        /// <summary>Whether <paramref name="craft"/> is a boarding shuttle's hull (U.SPC-T590).</summary>
        public static bool IsShuttle(Ship craft) => craft?.Craft != null && craft.Craft.Role == FighterRole.AssaultShuttle;

        /// <summary>The first shuttle docked in <paramref name="c"/>'s shuttle berths fit to fly (U.SPC-T590), or null.</summary>
        public static Ship ReadyShuttle(ShipCombat c)
        {
            if (c?.ShuttleBay == null) return null;
            foreach (var s in c.ShuttleBay)
                if (Flyable(s)) return s;
            return null;
        }

        /// <summary>Whether <paramref name="craft"/> is docked in <paramref name="c"/>, in its hangar or its shuttle berths.</summary>
        public static bool DockedIn(ShipCombat c, Ship craft) =>
            c != null && craft != null && (c.Hangar.Contains(craft) || c.ShuttleBay.Contains(craft));

        /// <summary>Takes craft out of the hangar from its last berths until <paramref name="count"/> are left: they are lost
        /// with the bay they were in.</summary>
        public static void Trim(ShipCombat c, int count)
        {
            count = Math.Max(0, count);
            while (c.Hangar.Count > count) c.Hangar.RemoveAt(c.Hangar.Count - 1);
        }

        /// <summary>
        /// Docks <paramref name="craft"/> in <paramref name="c"/>'s hangar, if it has room: in its own berth when that is free,
        /// else in the first free berth laid out for its class, else in any free one (a stray from a lost carrier, U.SPC-T473).
        /// The deck crews rearm it, top up its flares and recharge its shields; its damage stays until a yard mends it.
        /// Returns false, leaving it out, when the hangar is full. A boarding shuttle docks in the shuttle berths instead
        /// (U.SPC-T590): its own when free, else any free one.
        /// </summary>
        public static bool Dock(ShipCombat c, Ship craft)
        {
            if (c == null || craft?.Craft == null || DockedIn(c, craft)) return false;
            if (IsShuttle(craft)) return DockShuttle(c, craft);
            int berths = c.Spec.HangarCraft;
            if (c.Hangar.Count >= berths) return false;
            var taken = new HashSet<int>();
            foreach (var s in c.Hangar) taken.Add(s.Craft.Berth);
            int berth = craft.Craft.Berth;
            if (berth < 0 || berth >= berths || taken.Contains(berth))
            {
                berth = -1;
                for (int b = 0; b < berths; b++)
                {
                    if (taken.Contains(b)) continue;
                    if (berth < 0) berth = b;
                    if (Berth(c.Spec, b, out _, out _) == craft.Craft.Role)
                    {
                        berth = b;
                        break;
                    }
                }
            }
            craft.Craft.Berth = berth;
            Rearm(craft.Combat);
            Insert(c, craft);
            return true;
        }

        static bool DockShuttle(ShipCombat c, Ship craft)
        {
            int berths = Math.Max(0, c.Spec.Shuttles);
            if (c.ShuttleBay.Count >= berths) return false;
            var taken = new HashSet<int>();
            foreach (var s in c.ShuttleBay) taken.Add(s.Craft.Berth);
            int berth = craft.Craft.Berth;
            if (berth < 0 || berth >= berths || taken.Contains(berth))
            {
                berth = 0;
                while (taken.Contains(berth)) berth++;
            }
            craft.Craft.Berth = berth;
            Rearm(craft.Combat);
            Insert(c.ShuttleBay, craft);
            return true;
        }

        /// <summary>Leaves <paramref name="craft"/>, a stray with no berth to dock in, held as its own hull at <paramref name="at"/>
        /// until its carrier (or another of its faction's, when that is lost) calls there (U.SPC-T591).</summary>
        public static void Strand(Settlement at, Ship craft)
        {
            if (at == null || craft?.Craft == null || at.Strays.Contains(craft)) return;
            at.Strays.Add(craft);
        }

        /// <summary>Takes back aboard <paramref name="carrier"/> the strays held at <paramref name="at"/> that are its own, or
        /// (when their carrier is lost) of its faction's, as far as its hangar has room; returns how many rejoined.</summary>
        public static int Rejoin(Settlement at, Ship carrier)
        {
            if (at == null || carrier?.Combat == null || at.Strays.Count == 0) return 0;
            int n = 0;
            for (int i = at.Strays.Count - 1; i >= 0; i--)
            {
                var craft = at.Strays[i];
                var own = craft.Craft.Carrier;
                bool mine = own == carrier || ((own == null || own.Destroyed) && craft.Faction == carrier.Faction);
                if (!mine || !Dock(carrier.Combat, craft)) continue;
                craft.Craft.Carrier = carrier;
                at.Strays.RemoveAt(i);
                n++;
            }
            return n;
        }

        /// <summary>Takes <paramref name="craft"/> out of the hangar it is docked in, to fly.</summary>
        public static bool Undock(ShipCombat c, Ship craft) => c != null && (c.Hangar.Remove(craft) || c.ShuttleBay.Remove(craft));

        static void Insert(ShipCombat c, Ship craft) => Insert(c.Hangar, craft);

        static void Insert(List<Ship> berths, Ship craft)
        {
            int at = berths.Count;
            while (at > 0 && berths[at - 1].Craft.Berth > craft.Craft.Berth) at--;
            berths.Insert(at, craft);
        }

        /// <summary>Whether a docked craft is fit to launch: alive and not crippled (its drives or most of its hull gone).</summary>
        public static bool Flyable(Ship craft) => craft?.Combat != null && craft.Combat.Alive && !craft.Combat.Disabled;

        /// <summary>The deck crews' turnaround: magazines and flare dispensers full, live shields charged, the afterburner charged.</summary>
        public static void Rearm(ShipCombat c)
        {
            if (c == null) return;
            foreach (var s in c.Subsystems)
            {
                if (s.Def.Weapon != null) s.Ammo = s.Def.Weapon.Ammo;
                if (s.Kind == SubsystemKind.ShieldGenerator && !s.Destroyed) s.Shield = s.MaxShield;
                s.Cooldown = 0;
            }
            c.BoostFraction = 1;
        }

        /// <summary>Repairs every damaged craft docked in <paramref name="c"/>'s hangar and shuttle berths whole, as a yard
        /// does; returns how many it mended.</summary>
        public static int MendAboard(ShipCombat c)
        {
            int n = 0;
            if (c?.Hangar == null) return 0;
            foreach (var s in Docked(c))
            {
                if (s.Combat.Damage <= 0.001) continue;
                s.Combat.Repair(1);
                Rearm(s.Combat);
                n++;
            }
            return n;
        }

        /// <summary>Docked craft in <paramref name="c"/>'s hangar and shuttle berths carrying damage.</summary>
        public static int Damaged(ShipCombat c)
        {
            int n = 0;
            if (c?.Hangar != null)
                foreach (var s in Docked(c))
                    if (s.Combat.Damage > 0.001) n++;
            return n;
        }

        static int DamagedIn(List<Ship> berths)
        {
            int n = 0;
            foreach (var s in berths)
                if (s.Combat.Damage > 0.001) n++;
            return n;
        }

        /// <summary>Every craft docked in <paramref name="c"/>: the hangar's, then the shuttle berths'.</summary>
        static IEnumerable<Ship> Docked(ShipCombat c)
        {
            foreach (var s in c.Hangar) yield return s;
            if (c.ShuttleBay != null)
                foreach (var s in c.ShuttleBay) yield return s;
        }

        /// <summary>The hangar for the HUD: "18 of 26 aboard", and how many of them are hurt.</summary>
        public static string Describe(ShipCombat c)
        {
            if (c == null) return "";
            int hurt = DamagedIn(c.Hangar);
            return c.Fighters + " of " + c.Spec.HangarCraft + " aboard" + (hurt > 0 ? ", " + hurt + " damaged" : "");
        }

        /// <summary>The craft's pilot, rolled from <paramref name="rng"/> the first time it is asked for.</summary>
        public static CraftPilot PilotOf(Ship craft, Random rng)
        {
            var w = craft.Craft;
            if (w.Pilot != null) return w.Pilot;
            w.Pilot = new CraftPilot
            {
                Skill = 0.4 + rng.NextDouble() * 0.6, Reaction = 0.4 + rng.NextDouble() * 1.4, Discipline = 0.45 + rng.NextDouble() * 0.5,
                Seed = rng.NextDouble() * 1000,
            };
            return w.Pilot;
        }
    }
}
