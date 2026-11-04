using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// What throws a ship out of a hyperlane (U.SPC-T479): a field projector that disrupts the lane's magnetic field. The
    /// projector is heavy equipment, not a ship class (U.SPC-T507): a facility (<see cref="FacilityKinds.FieldProjector"/>)
    /// fitted into the internal space of any hull big enough to carry it and its reactor load (<see cref="CanCarry(ShipClass)"/>),
    /// so a destroyer, a cruiser or a raider's converted freighter can each be fitted out as a lane interdictor, and a yard
    /// fits and strips it like any other fitting. A group lying in wait beside a lane (see the lane ambushes in
    /// <see cref="War"/>) can pulse only while one of its hulls carries a working projector; the rest of the group fights
    /// what the pulse pulls out, in the battle beside the lane. A lane's route is predictable, so the projector is the
    /// pirate's and raider's favourite: every raider band fits one to a hull for its lane ambushes. Navies fit one to a
    /// destroyer of a lane picket, to pull raiders and enemies off the lanes out of their systems. The tunables are in the
    /// settings menu's AI group.
    /// </summary>
    [SettingsSource]
    public static class LaneInterdictors
    {
        /// <summary>The projector's installed mass with its reactor, tonnes (one fixed fitting whatever the hull).</summary>
        public const double ProjectorMassTonnes = 900;

        /// <summary>
        /// The least dry mass, tonnes, of a hull with the reactor load to spare for a projector: a destroyer's or a truss
        /// freighter's (9,000 t) and up, an ore hauler's at the margin; never a frigate, corvette or anything smaller.
        /// </summary>
        public const double MinHullTonnes = 8000;

        /// <summary>The hull categories that may carry a projector: warships, capitals and freighters.</summary>
        public static readonly HullCategory[] Hulls =
        {
            HullCategory.Warship, HullCategory.Battleship, HullCategory.Carrier, HullCategory.Freighter,
        };

        /// <summary>The facility kind.</summary>
        public static FacilityKind Kind => FacilityKinds.Of(FacilityKinds.FieldProjector);

        /// <summary>Whether hulls of <paramref name="cls"/> are big enough to carry a projector and its reactor load (their
        /// category allowing, see <see cref="Hulls"/>).</summary>
        public static bool CanCarry(ShipClass cls)
        {
            var designs = CombatSpec.DesignsOf(cls);
            return designs.Count > 0 && designs[0].DryMassTonnes >= MinHullTonnes - 1e-9;
        }

        /// <summary>Whether <paramref name="ship"/> could take a projector into its free space now.</summary>
        public static bool CanFit(Ship ship)
        {
            if (ship?.Spec == null || Kind == null) return false;
            var l = Outfitting.LoadoutOf(ship);
            return l != null && l.CanAdd(Kind) == OutfitOutcome.Ok;
        }

        /// <summary>Fits a projector into <paramref name="ship"/> as part of its build or raising (no yard bill). False if
        /// the hull may not carry one or has no room for it.</summary>
        public static bool Fit(Ship ship) => ship != null && Kind != null && Outfitting.Install(ship, Kind);

        /// <summary>Projectors in <paramref name="ship"/>: its refitted loadout's, or its design's as laid down.</summary>
        public static int Count(Ship ship) => FleetCoordination.Count(ship, FacilityKinds.FieldProjector);

        /// <summary>Whether <paramref name="ship"/> carries a field projector.</summary>
        public static bool Carries(Ship ship) => Count(ship) > 0;

        public static readonly RangeSetting RechargeDays = new RangeSetting("war.interdictorRechargeDays", SettingGroups.Ai,
            "Interdictor recharge", 1, 0.1, 5, 0.1,
            "How long (days) a lane interdictor's field projector takes to charge again after it has pulsed and thrown ships out of a hyperlane.",
            v => v.ToString("0.0", CultureInfo.InvariantCulture) + " days", "interdictor interdiction pulse lane hyperlane recharge pirate ambush projector");

        public static readonly RangeSetting PicketChance = new RangeSetting("war.lanePicketChance", SettingGroups.Ai,
            "Navy lane pickets", 0.35, 0, 1, 0.05,
            "The chance a navy group with a field projector fitted to one of its hulls, with nothing more pressing to do, lies in " +
            "wait on the most troubled hyperlane out of its system, to pull the raiders and enemies riding it out into a fight.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "interdictor lane picket navy patrol ambush hyperlane projector");

        /// <summary>
        /// The hull in <paramref name="leader"/>'s group carrying a working field projector: the leader itself, else an escort
        /// flying with it (one of <paramref name="ships"/>), or null when none is left (lost, stripped, or never fitted).
        /// </summary>
        public static Ship EmitterOf(Ship leader, IEnumerable<Ship> ships)
        {
            if (leader == null || leader.Destroyed) return null;
            if (Working(leader)) return leader;
            if (ships != null)
                foreach (var s in ships)
                    if (s != null && s.Leader == leader && Working(s)) return s;
            return null;
        }

        /// <summary>Whether <paramref name="leader"/>'s group carries a working field projector.</summary>
        public static bool Fielded(Ship leader, IEnumerable<Ship> ships) => EmitterOf(leader, ships) != null;

        static bool Working(Ship s) => !s.Destroyed && !s.Derelict && s.Spec != null && Carries(s);
    }
}
