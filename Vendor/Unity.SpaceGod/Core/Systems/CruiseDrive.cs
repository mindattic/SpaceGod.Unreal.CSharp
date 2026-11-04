using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A long-range cruise drive (U.SPC-T412): what lets a hull leave its system by its own power and cross the open
    /// space between the stars, rather than only by a jump gate. It charges (spools) at the edge of its system's
    /// gravity well, comes up to its cruise speed, holds it across the light-years, and comes down again to drop out
    /// at the edge of the far star's well, where the hull finishes the way in under its ordinary drive. Its speed is
    /// many times light's, but slow next to a gate: a gate is a shortcut, the drive is the way anywhere.
    ///
    /// A drive is data, not code: a new one is a new spec registered for the hull classes that carry it
    /// (<see cref="CruiseDrives.Register"/>).
    /// </summary>
    public sealed class CruiseDriveSpec
    {
        public string Name { get; }

        /// <summary>Top cruise speed, as a multiple of the speed of light (before the menu's <see cref="CruiseDrives.SpeedScale"/>).</summary>
        public double CruiseC { get; }

        /// <summary>How long it is rated to charge at the well's edge before it engages, days.</summary>
        public double RatedSpoolDays { get; }

        /// <summary>How long it charges before it engages, days: its rating times the menu's <see cref="FtlRules.SpoolScale"/>.</summary>
        public double SpoolDays => RatedSpoolDays * FtlRules.SpoolScale.Value;

        /// <summary>How long it takes to come up to cruise speed (and to come down from it), days.</summary>
        public double RampDays { get; }

        /// <summary>How long it is rated to cool after it drops out before it can charge again, days (U.SPC-T418).</summary>
        public double RatedCooldownDays { get; }

        /// <summary>How long it cools after it drops out, days: its rating times the menu's <see cref="FtlRules.CooldownScale"/>.</summary>
        public double CooldownDays => RatedCooldownDays * FtlRules.CooldownScale.Value;

        /// <summary>The share of a hull's tanks a charge burns, and the share each light-year flown burns (U.SPC-T418,
        /// before the menu's <see cref="FtlRules.FuelScale"/>; see <see cref="FtlRules.PropellantFor"/>).</summary>
        public double ChargePropellantShare { get; }
        public double PropellantSharePerLy { get; }

        public CruiseDriveSpec(string name, double cruiseC, double spoolDays, double rampDays, double cooldownDays = 0,
            double chargePropellantShare = 0, double propellantSharePerLy = 0)
        {
            if (!(cruiseC > 0)) throw new ArgumentOutOfRangeException(nameof(cruiseC), "A cruise drive must move: " + name);
            Name = name;
            CruiseC = cruiseC;
            RatedSpoolDays = Math.Max(0, spoolDays);
            RampDays = Math.Max(1e-6, rampDays);
            RatedCooldownDays = Math.Max(0, cooldownDays);
            ChargePropellantShare = Math.Max(0, chargePropellantShare);
            PropellantSharePerLy = Math.Max(0, propellantSharePerLy);
        }

        /// <summary>Light-years a day at cruise speed, at the menu's speed scale now.</summary>
        public double LyPerDay => CruiseC * CruiseDrives.SpeedScale.Value / (Astro.DaysPerCentury / 100);

        public override string ToString() => Name;
    }

    /// <summary>
    /// A cruise's speed through one leg (U.SPC-T412): charging, coming up to speed, holding it, coming down. Worked out
    /// once when the leg is set and closed-form after that, so where a group is between the stars never depends on how
    /// often it is looked at.
    /// </summary>
    public readonly struct CruiseProfile
    {
        /// <summary>The leg's length, light-years; its speed at cruise, light-years a day; the ramp up and the charge, days.</summary>
        public readonly double Ly, LyPerDay, RampDays, SpoolDays;

        public CruiseProfile(double ly, double lyPerDay, double rampDays, double spoolDays)
        {
            Ly = Math.Max(0, ly);
            LyPerDay = Math.Max(1e-12, lyPerDay);
            RampDays = Math.Max(1e-9, rampDays);
            SpoolDays = Math.Max(0, spoolDays);
        }

        public static CruiseProfile For(CruiseDriveSpec drive, double ly) => new CruiseProfile(ly, drive.LyPerDay, drive.RampDays, drive.SpoolDays);

        double Accel => LyPerDay / RampDays;

        /// <summary>Whether the leg is too short to reach cruise speed (it comes straight down again from its peak).</summary>
        bool Short => Ly < LyPerDay * RampDays;

        /// <summary>Days under way once engaged (not counting the charge).</summary>
        public double FlightDays => Short ? 2 * Math.Sqrt(Ly / Accel) : RampDays + Ly / LyPerDay;

        /// <summary>Days from the start of the charge to dropping out.</summary>
        public double TotalDays => SpoolDays + FlightDays;

        /// <summary>Light-years covered <paramref name="days"/> after the charge began.</summary>
        public double LyAt(double days)
        {
            double t = days - SpoolDays;
            if (t <= 0) return 0;
            double total = FlightDays;
            if (t >= total) return Ly;
            double a = Accel;
            if (Short)
            {
                double half = total / 2;
                if (t <= half) return 0.5 * a * t * t;
                double r = total - t;
                return Ly - 0.5 * a * r * r;
            }
            if (t <= RampDays) return 0.5 * a * t * t;
            double coastEnd = total - RampDays;
            if (t <= coastEnd) return 0.5 * LyPerDay * RampDays + LyPerDay * (t - RampDays);
            double left = total - t;
            return Ly - 0.5 * a * left * left;
        }

        /// <summary>Speed <paramref name="days"/> after the charge began, light-years a day.</summary>
        public double SpeedAt(double days)
        {
            double t = days - SpoolDays;
            double total = FlightDays;
            if (t <= 0 || t >= total) return 0;
            double a = Accel;
            double peak = Short ? a * total / 2 : LyPerDay;
            return Math.Min(peak, Math.Min(a * t, a * (total - t)));
        }
    }

    /// <summary>How a class of hull crosses between the stars (U.SPC-T472; docs/SHIP_ROSTER.md, "Who has their own FTL").</summary>
    public enum FtlAccess
    {
        /// <summary>Always carries a self-contained FTL drive: carriers, gate breakers, deep-space explorers.</summary>
        Own,

        /// <summary>Carries one only when its design fits the FTL drive facility (<see cref="FacilityKinds.FtlDrive"/>), and
        /// otherwise goes by the gates: battleships, command cruisers.</summary>
        Fitted,

        /// <summary>No FTL of its own: it crosses by the jump gates, or docked on a carrier's hull: cruisers, destroyers,
        /// frigates, couriers, freighters and the other small and civil hulls.</summary>
        Gates,

        /// <summary>Crosses between the stars inside a carrier's hangar (or through a gate with its group): fighters, bombers.</summary>
        Hangar,

        /// <summary>Never leaves its home system, by gate, drive or carriage: corvettes.</summary>
        HomeSystem,
    }

    /// <summary>
    /// The owner's roster of who has their own FTL (U.SPC-T472), as data: carriers and gate breakers always have a
    /// self-contained drive; battleships and command cruisers only when the design fits one (a drive facility taking
    /// hull space, <see cref="FacilityKinds.FtlDrive"/>); cruisers, destroyers and frigates have none and travel by gate,
    /// or docked on a carrier; corvettes never leave their home system; fighters and bombers travel inside a carrier's
    /// hangar. Explorers (U.SPC-T419) are long-legged survey ships and keep their own. A new class slots in with one
    /// <see cref="Register"/> call (and, if it has a drive, <see cref="CruiseDrives.Register"/>), never an edit to the
    /// systems that move fleets between the stars, which all ask <see cref="CruiseDrives.Of(Ship)"/> and
    /// <see cref="LeavesHome"/>.
    /// </summary>
    public static class FtlRoster
    {
        static readonly Dictionary<ShipClass, FtlAccess> byClass = new Dictionary<ShipClass, FtlAccess>
        {
            [ShipClass.Carrier] = FtlAccess.Own,
            [ShipClass.Explorer] = FtlAccess.Own,
            // The gate breaker (U.SPC-T478) always has its own: it attacks the gates the rest of the fleet would use.
            [ShipClass.GateBreaker] = FtlAccess.Own,
            [ShipClass.Battleship] = FtlAccess.Fitted,
            [ShipClass.CommandCruiser] = FtlAccess.Fitted,
            [ShipClass.Cruiser] = FtlAccess.Gates,
            [ShipClass.Destroyer] = FtlAccess.Gates,
            // No drive of its own (U.SPC-T475): the gates, or clamped to a carrier's hull (FtlCarriage).
            [ShipClass.Frigate] = FtlAccess.Gates,
            // System defence (U.SPC-T476): no drive, no gate, no carriage; it never leaves its home system.
            [ShipClass.Corvette] = FtlAccess.HomeSystem,
            [ShipClass.Fighter] = FtlAccess.Hangar,
            [ShipClass.Interceptor] = FtlAccess.Hangar,
            [ShipClass.Bomber] = FtlAccess.Hangar,
            [ShipClass.Recon] = FtlAccess.Hangar,
            // A boarding shuttle (U.SPC-T486) rides in a carrier's hangar.
            [ShipClass.AssaultShuttle] = FtlAccess.Hangar,
        };

        /// <summary>Sets how hulls of <paramref name="cls"/> cross between the stars: how a new class slots in.</summary>
        public static void Register(ShipClass cls, FtlAccess access) => byClass[cls] = access;

        /// <summary>How hulls of <paramref name="cls"/> cross between the stars. A class not on the roster has its own drive
        /// if one is registered for it (<see cref="CruiseDrives.Register"/>), and otherwise goes by the gates.</summary>
        public static FtlAccess Of(ShipClass cls) =>
            byClass.TryGetValue(cls, out var a) ? a : CruiseDrives.Of(cls) != null ? FtlAccess.Own : FtlAccess.Gates;

        /// <summary>Whether a hull of <paramref name="cls"/> ever leaves its home system (a corvette never does).</summary>
        public static bool LeavesHome(ShipClass cls) => Of(cls) != FtlAccess.HomeSystem;

        /// <summary>Whether <paramref name="ship"/> ever leaves its home system.</summary>
        public static bool LeavesHome(Ship ship) => ship?.Spec == null || LeavesHome(ship.Spec.Class);

        /// <summary>Whether <paramref name="ship"/>'s hull has the FTL drive facility in it now: its refitted loadout's, or
        /// its design's as laid down (<see cref="FacilityLayouts.For"/>) if it was never refitted.</summary>
        public static bool DriveFitted(Ship ship)
        {
            if (ship?.Spec == null) return false;
            if (ship.Loadout != null) return ship.Loadout.CountFacility(FacilityKinds.FtlDrive) > 0;
            foreach (var f in FacilityLayouts.For(ship.Spec.Class))
                if (string.Equals(f.Kind, FacilityKinds.FtlDrive, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Why <paramref name="ship"/> cannot cross between the stars under its own power, by its roster entry, or
        /// null if it has a drive.</summary>
        public static string NoDriveReason(Ship ship)
        {
            if (ship?.Spec == null) return "No ship";
            if (CruiseDrives.Of(ship) != null) return null;
            string cls = ShipClassSpec.Of(ship.Spec.Class)?.DisplayName?.ToLowerInvariant() ?? ship.Spec.Class.ToString().ToLowerInvariant();
            switch (Of(ship.Spec.Class))
            {
                case FtlAccess.Fitted:
                    return "The " + ship.Name + " has no FTL drive fitted: a yard can fit one into its free space; until then the jump gates are its way";
                case FtlAccess.Hangar:
                    return "A " + cls + " carries no FTL drive: it crosses between the stars in a carrier's hangar, or through the jump gates";
                case FtlAccess.HomeSystem:
                    return "A " + cls + " carries no FTL drive and never leaves its home system";
                default:
                    return FtlCarriage.RidesClamped(ship.Spec.Class)
                        ? "A " + cls + " carries no FTL drive of its own: the jump gates and lanes are its way, or clamped to a carrier's hull"
                        : "A " + cls + " carries no FTL drive of its own: the jump gates and lanes are its way, or docked aboard a carrier";
            }
        }
    }

    /// <summary>
    /// Which hulls carry a cruise (FTL) drive, and the menu's FTL tuning (U.SPC-T412, U.SPC-T419, U.SPC-T472). Who has a
    /// drive is the owner's roster (<see cref="FtlRoster"/>): carriers always carry the heavy capital drive, battleships
    /// and command cruisers only when their design fits the FTL drive facility, and the deep-space explorer, the hull the survey services send
    /// out past the gates, the lighter, faster survey drive. Everything else (cruisers, destroyers, fighters, couriers,
    /// breachers, freighters, haulers) cannot leave a system except by a gate, or docked aboard a hull that can
    /// (see <see cref="FtlCarriage"/>).
    /// </summary>
    [SettingsSource]
    public static class CruiseDrives
    {
        /// <summary>A capital ship's drive: a light-year a day, charging six hours and cooling twelve after, a charge burning
        /// 2% of the tanks and each light-year 0.2% (some 490 ly on full tanks).</summary>
        public static readonly CruiseDriveSpec Capital = new CruiseDriveSpec("Capital cruise drive", (Astro.DaysPerCentury / 100), 0.25, 0.1,
            cooldownDays: 0.5, chargePropellantShare: 0.02, propellantSharePerLy: 0.002);

        /// <summary>A survey hull's drive: two light-years a day, charging under three hours and cooling six after, a charge
        /// burning 1% of the tanks and each light-year 0.1% (some 990 ly on full tanks).</summary>
        public static readonly CruiseDriveSpec Survey = new CruiseDriveSpec("Survey cruise drive", 2 * (Astro.DaysPerCentury / 100), 0.1, 0.05,
            cooldownDays: 0.25, chargePropellantShare: 0.01, propellantSharePerLy: 0.001);

        static readonly Dictionary<ShipClass, CruiseDriveSpec> byClass = new Dictionary<ShipClass, CruiseDriveSpec>
        {
            [ShipClass.Carrier] = Capital,
            // Only a battleship (or command cruiser) whose design fits the drive facility carries it (FtlAccess.Fitted, U.SPC-T472).
            [ShipClass.Battleship] = Capital,
            // A command cruiser, like a battleship, only when its design fits the drive facility (U.SPC-T477, U.SPC-T472).
            [ShipClass.CommandCruiser] = Capital,
            // The gate breaker's own capital drive, built into its engineering (U.SPC-T478).
            [ShipClass.GateBreaker] = Capital,
            [ShipClass.Explorer] = Survey,
        };

        /// <summary>Gives hulls of <paramref name="cls"/> <paramref name="drive"/> (null takes it away): how a new drive or a
        /// new hull plugs in. Whether a given hull carries it is its class's <see cref="FtlRoster"/> entry.</summary>
        public static void Register(ShipClass cls, CruiseDriveSpec drive)
        {
            if (drive == null) byClass.Remove(cls);
            else byClass[cls] = drive;
        }

        /// <summary>The drive hulls of <paramref name="cls"/> carry (for a class that carries one only when fitted, the one
        /// it carries then), or null.</summary>
        public static CruiseDriveSpec Of(ShipClass cls) => byClass.TryGetValue(cls, out var d) ? d : null;

        /// <summary>The drive <paramref name="ship"/> carries, or null if it cannot cross between the stars on its own: by its
        /// class's roster entry (<see cref="FtlRoster"/>), and for a battleship whether its design has the drive fitted.</summary>
        public static CruiseDriveSpec Of(Ship ship)
        {
            if (ship?.Spec == null) return null;
            var drive = Of(ship.Spec.Class);
            if (drive == null) return null;
            switch (FtlRoster.Of(ship.Spec.Class))
            {
                case FtlAccess.Own: return drive;
                case FtlAccess.Fitted: return FtlRoster.DriveFitted(ship) ? drive : null;
                default: return null;
            }
        }

        /// <summary>The settings menu's FTL tuning: every cruise drive's speed, as a multiple of its rated speed.</summary>
        public static readonly RangeSetting SpeedScale = new RangeSetting("ftl.cruiseSpeedScale", SettingGroups.Ftl,
            "Cruise drive top speed", 1, 0.25, 50, 0.25,
            "How fast every cruise drive crosses between the stars, as a multiple of its rated speed (a capital drive is rated at a light-year a day: " +
            "the galaxy's width in some 270 years; at the top of the range, in about five). Applies to trips set out on from now.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "ftl interstellar cruise speed drive");

        /// <summary>The settings menu's FTL tuning: how far out from a star a cruise drive can engage, and where it drops out.</summary>
        public static readonly RangeSetting WellLimit = new RangeSetting("ftl.wellLimitAu", SettingGroups.Ftl,
            "Gravity well limit", 2, 0.25, 20, 0.25,
            "How far from a star (AU) a cruise drive must be to engage, and where it drops out on arrival, the rest of the way flown under " +
            "the ordinary drive. Applies to trips set out on from now.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + " AU", "ftl well drop out gravity");

        /// <summary>How far from <paramref name="system"/>'s root a cruise engages and drops out, km.</summary>
        public static double WellLimitKm(StarSystem system) =>
            Math.Max(WellLimit.Value * Astro.AuKm, system == null ? 0 : (system.Catalog.Root.RadiusKm + JumpGate.RadiusKm) * 4);
    }

    /// <summary>
    /// What a hull with an FTL drive can take with it docked aboard (U.SPC-T419): carriers, and battleships and command cruisers
    /// with a drive fitted, have hangar berths, and fighters and small craft that cannot cross between the stars on their own ride in
    /// them, so a fleet can strike or explore off the gate network with its wings. Cruisers and destroyers have no drive
    /// and no berths (U.SPC-T472): they cross by the gates, or clamped to a carrier's hull. A carrier, besides its hangars,
    /// has docking clamps on the outside of its hull (U.SPC-T473) that take a few escorts too big for a berth (destroyers
    /// and frigates; never a corvette, which does not leave its home system, U.SPC-T476) through its own jump, so a jump strike group is a carrier, the
    /// capitals with drives of their own, and the escorts clamped to it. Data, not code: a hull's berths and clamps and a
    /// craft's size are registered (<see cref="RegisterBerths"/>, <see cref="RegisterCraft"/>, <see cref="RegisterClamps"/>,
    /// <see cref="RegisterClampable"/>), never threaded through the cruise.
    /// </summary>
    public static class FtlCarriage
    {
        /// <summary>Hangar berths each class of drive-carrying hull has for small craft.</summary>
        static readonly Dictionary<ShipClass, int> berths = new Dictionary<ShipClass, int>
        {
            [ShipClass.Carrier] = 12,
            [ShipClass.Battleship] = 4,
            [ShipClass.CommandCruiser] = 3,
            // Its shuttle deck (U.SPC-T478): room for its own two boarding shuttles' worth of small craft.
            [ShipClass.GateBreaker] = 4,
        };

        /// <summary>How many berths a craft of each class takes up; a class not listed cannot be carried.</summary>
        static readonly Dictionary<ShipClass, int> sizes = new Dictionary<ShipClass, int>
        {
            [ShipClass.Fighter] = 1,
            [ShipClass.Interceptor] = 1,
            [ShipClass.Bomber] = 2,
            [ShipClass.Recon] = 1,
            [ShipClass.AssaultShuttle] = 2,
            [ShipClass.Courier] = 2,
            [ShipClass.Gunship] = 2,
        };

        /// <summary>Docking clamps on the outside of each class of drive-carrying hull (U.SPC-T473): a carrier's four take
        /// two destroyers or four frigates.</summary>
        static readonly Dictionary<ShipClass, int> clamps = new Dictionary<ShipClass, int>
        {
            [ShipClass.Carrier] = 4,
        };

        /// <summary>How many clamps an escort of each class takes up; a class not listed is never clamped to a hull.</summary>
        static readonly Dictionary<ShipClass, int> clampSizes = new Dictionary<ShipClass, int>
        {
            [ShipClass.Destroyer] = 2,
            // A frigate rides clamped to a carrier's hull (U.SPC-T475), never in a hangar.
            [ShipClass.Frigate] = 1,
        };

        /// <summary>Gives hulls of <paramref name="cls"/> <paramref name="count"/> berths (0 takes them away).</summary>
        public static void RegisterBerths(ShipClass cls, int count)
        {
            if (count <= 0) berths.Remove(cls);
            else berths[cls] = count;
        }

        /// <summary>Lets craft of <paramref name="cls"/> be carried, taking <paramref name="size"/> berths (0 says it cannot be).</summary>
        public static void RegisterCraft(ShipClass cls, int size)
        {
            if (size <= 0) sizes.Remove(cls);
            else sizes[cls] = size;
        }

        /// <summary>Gives hulls of <paramref name="cls"/> <paramref name="count"/> docking clamps on the outside of the hull
        /// (0 takes them away, U.SPC-T473).</summary>
        public static void RegisterClamps(ShipClass cls, int count)
        {
            if (count <= 0) clamps.Remove(cls);
            else clamps[cls] = count;
        }

        /// <summary>Lets escorts of <paramref name="cls"/> ride clamped to a hull, taking <paramref name="size"/> clamps (0 says
        /// they cannot be): how a frigate slotted in (U.SPC-T473, U.SPC-T475).</summary>
        public static void RegisterClampable(ShipClass cls, int size)
        {
            if (size <= 0) clampSizes.Remove(cls);
            else clampSizes[cls] = size;
        }

        /// <summary>Berths hulls of <paramref name="cls"/> have.</summary>
        public static int BerthsOf(ShipClass cls) => berths.TryGetValue(cls, out int n) ? n : 0;

        /// <summary>Berths <paramref name="ship"/> has to carry craft through FTL: none without a drive of its own to carry them on.</summary>
        public static int BerthsOf(Ship ship) =>
            ship?.Spec == null || CruiseDrives.Of(ship) == null ? 0 : BerthsOf(ship.Spec.Class);

        /// <summary>Docking clamps hulls of <paramref name="cls"/> have on the outside of the hull.</summary>
        public static int ClampsOf(ShipClass cls) => clamps.TryGetValue(cls, out int n) ? n : 0;

        /// <summary>Clamps <paramref name="ship"/> has to take escorts through FTL: none without a drive of its own.</summary>
        public static int ClampsOf(Ship ship) =>
            ship?.Spec == null || CruiseDrives.Of(ship) == null ? 0 : ClampsOf(ship.Spec.Class);

        /// <summary>Berths a craft of <paramref name="cls"/> takes up, or 0 if it cannot be carried in a hangar.</summary>
        public static int SizeOf(ShipClass cls) => sizes.TryGetValue(cls, out int n) ? n : 0;

        /// <summary>Clamps an escort of <paramref name="cls"/> takes up, or 0 if it is never clamped to a hull.</summary>
        public static int ClampSizeOf(ShipClass cls) => clampSizes.TryGetValue(cls, out int n) ? n : 0;

        /// <summary>Whether a hull of <paramref name="cls"/> rides clamped to the outside of its carrier rather than in a hangar.</summary>
        public static bool RidesClamped(ShipClass cls) => SizeOf(cls) == 0 && ClampSizeOf(cls) > 0;

        /// <summary>Whether <paramref name="craft"/> rides clamped to the outside of its carrier rather than in a hangar.</summary>
        public static bool RidesClamped(Ship craft) => craft?.Spec != null && RidesClamped(craft.Spec.Class);

        /// <summary>Whether <paramref name="craft"/> needs carrying (no drive of its own) and can be (it fits a berth, or a clamp).</summary>
        public static bool Carriable(Ship craft) =>
            craft?.Spec != null && !craft.Destroyed && !craft.Derelict && CruiseDrives.Of(craft) == null
            && (SizeOf(craft.Spec.Class) > 0 || ClampSizeOf(craft.Spec.Class) > 0);

        /// <summary>
        /// Docks as many of <paramref name="craft"/> as fit aboard <paramref name="carriers"/> (the hulls going under
        /// their own drives), each in the first with berths (or, for an escort too big for a hangar, clamps) to spare, in
        /// the order given: what goes aboard which. Craft that need no carrying, cannot be carried or find no room are
        /// left out (they stay behind).
        /// </summary>
        public static Dictionary<Ship, Ship> Stow(IEnumerable<Ship> carriers, IEnumerable<Ship> craft)
        {
            var free = new List<(Ship hull, int berths, int clamps)>();
            foreach (var c in carriers)
            {
                int n = BerthsOf(c), k = ClampsOf(c);
                if (n > 0 || k > 0) free.Add((c, n, k));
            }
            var aboard = new Dictionary<Ship, Ship>();
            foreach (var s in craft)
            {
                if (!Carriable(s) || aboard.ContainsKey(s)) continue;
                bool clamped = RidesClamped(s);
                int size = clamped ? ClampSizeOf(s.Spec.Class) : SizeOf(s.Spec.Class);
                for (int i = 0; i < free.Count; i++)
                {
                    var f = free[i];
                    if (f.hull == s || (clamped ? f.clamps : f.berths) < size) continue;
                    aboard[s] = f.hull;
                    free[i] = clamped ? (f.hull, f.berths, f.clamps - size) : (f.hull, f.berths - size, f.clamps);
                    break;
                }
            }
            return aboard;
        }

        /// <summary>How many of <paramref name="aboard"/> (craft to the hull carrying them) ride clamped to the hull rather than
        /// docked in a hangar.</summary>
        public static int CountClamped(IEnumerable<Ship> aboard)
        {
            int n = 0;
            foreach (var s in aboard)
                if (RidesClamped(s)) n++;
            return n;
        }

        /// <summary>How a group going through FTL is made up, for the log: ", 2 escorts alongside, 6 craft docked aboard and 1
        /// escort clamped to the hull", or empty for a hull going alone.</summary>
        public static string Describe(int alongside, int docked, int clamped)
        {
            var parts = new List<string>();
            if (alongside > 0) parts.Add(alongside + " escort" + (alongside == 1 ? "" : "s") + " alongside");
            if (docked > 0) parts.Add(docked + " craft docked aboard");
            if (clamped > 0) parts.Add(clamped + " escort" + (clamped == 1 ? "" : "s") + " clamped to the hull");
            if (parts.Count == 0) return "";
            if (parts.Count == 1) return ", " + parts[0];
            return ", " + string.Join(", ", parts.GetRange(0, parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }
    }
}
