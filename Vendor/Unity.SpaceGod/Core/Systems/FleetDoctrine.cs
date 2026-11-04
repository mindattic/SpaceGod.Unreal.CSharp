using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>The two kinds of force a navy raises (U.SPC-T474, see <see cref="FleetDoctrine"/>).</summary>
    public enum ForceKind
    {
        /// <summary>A large group that must come through the gates: predictable, and met at the gate exits by the forts and
        /// pickets there.</summary>
        GateFleet,

        /// <summary>A carrier, the capitals with FTL drives of their own and the escorts docked or clamped to the carrier: smaller,
        /// but it can jump in anywhere under its own drives, gates or none.</summary>
        JumpStrikeGroup,
    }

    /// <summary>What a campaign is sent to do (U.SPC-T474): its objective decides which kind of force goes.</summary>
    public enum CampaignMission
    {
        /// <summary>Take an enemy system: the gate fleet's work, forcing the gates on the way.</summary>
        Invade,

        /// <summary>A jump strike group dropping into another enemy system the war is fought over, while the gate fleet marches
        /// on its own goal, to take it from the flank.</summary>
        SecondFront,

        /// <summary>A jump strike group sent to one of its own systems under attack, to break the enemy over its ports and put
        /// its marines down to stiffen their garrisons.</summary>
        Relieve,

        /// <summary>A jump strike group falling on an enemy system's weakest ports and jumping out again before the enemy can
        /// gather.</summary>
        Raid,
    }

    /// <summary>
    /// AI fleet doctrine (U.SPC-T474): navies raise two kinds of force and use them differently. A gate fleet is large but
    /// has to come through the gates, so it is predictable and met at the gate exits; a jump strike group (a carrier, the
    /// capitals with FTL drives of their own and the escorts docked aboard or clamped to the carrier, <see cref="FtlCarriage"/>)
    /// is smaller but can jump in anywhere, dropping out on the flank of a system away from its gates and their pickets.
    /// Strategic command (<see cref="InterstellarWars"/>) picks between them by objective: a gate fleet to invade, a strike
    /// group to relieve one of its own systems under attack, open a second front or raid. Defenders keep pickets at their
    /// gates (<see cref="GatePickets"/>) and, against an enemy that has strike groups, a reserve at home in every system they
    /// hold that no campaign may draw below (<see cref="ReserveShare"/>), which meets a strike group at the ports it falls on.
    /// Which hulls make a strike group is the carriage data (drives, berths and clamps), never a list of classes here.
    /// </summary>
    [SettingsSource]
    public static class FleetDoctrine
    {
        /// <summary>The settings menu's strike groups (AI): whether navies send jump strike groups on missions of their own.</summary>
        public static readonly ToggleSetting StrikeGroups = new ToggleSetting("doctrine.strikeGroups", SettingGroups.Ai,
            "Jump strike groups", true,
            "Navies at war send their jump strike groups (a carrier, FTL capitals and escorts clamped to the carrier) under their own drives " +
            "to relieve their own systems, open a second front or raid, besides marching their gate fleets through the gates.",
            "doctrine strike group carrier ftl jump raid relieve second front");

        /// <summary>The settings menu's home reserve (AI): how much of the strongest enemy strike group a navy keeps at home.</summary>
        public static readonly RangeSetting ReserveShare = new RangeSetting("doctrine.reserveShare", SettingGroups.Ai,
            "Reserve against strike groups", 1, 0, 3, 0.25,
            "How much warship strength a navy keeps at home in every system it holds, against an enemy with jump strike groups, as a multiple " +
            "of the strongest such group: no campaign is sent that would leave less. 0 keeps no reserve.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "doctrine reserve defence strike group home");

        /// <summary>The settings menu's strike range (AI): how far a strike group is sent under its own drive.</summary>
        public static readonly RangeSetting StrikeRangeLy = new RangeSetting("doctrine.strikeRange", SettingGroups.Ai,
            "Strike group range", 20, 1, 200, 1,
            "How far (light-years) a navy sends a jump strike group under its own drive on a raid, a relief or a second front.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " ly", "doctrine strike group range ftl");

        /// <summary>The settings menu's strike length (AI): how long a strike group raids or relieves before it jumps home.</summary>
        public static readonly RangeSetting StrikeDays = new RangeSetting("doctrine.strikeDays", SettingGroups.Ai,
            "Strike group mission length", 10, 1, 60, 1,
            "How many days a jump strike group falls on an enemy system's ports, or fights to relieve one of its own, before it jumps out for home.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " days", "doctrine strike group raid relieve");

        /// <summary>How far round from the way it comes in a strike group may drop out, degrees: never past the side of the
        /// system, so it does not fly through the star to get there.</summary>
        public const double MaxFlankDegrees = 90;

        /// <summary>The flanks tried, degrees either way from the way it comes in.</summary>
        static readonly double[] flanks = { 0, 30, -30, 60, -60, 90, -90 };

        // ---------------------------------------------------------------- what a group is

        /// <summary>Whether <paramref name="ship"/> is a carrier in the doctrine's sense: a hull with an FTL drive of its own and
        /// docking clamps to take escorts through its jump.</summary>
        public static bool IsCarrier(Ship ship) => FtlCarriage.ClampsOf(ship) > 0;

        /// <summary>
        /// Which kind of force <paramref name="leader"/>'s group in <paramref name="world"/> is: a jump strike group when its
        /// flagship has an FTL drive, a carrier is among it and every hull that leaves home either has a drive of its own or is
        /// docked or clamped aboard one (nothing would be left behind at the well's edge); otherwise a gate fleet.
        /// </summary>
        public static ForceKind KindOf(World world, Ship leader)
        {
            if (leader == null || CruiseDrives.Of(leader) == null) return ForceKind.GateFleet;
            var drives = new List<Ship> { leader };
            var craft = new List<Ship>();
            bool carrier = IsCarrier(leader);
            if (world != null)
                foreach (var e in world.Fleet.Ships)
                {
                    if (e.Leader != leader || e == leader || e.Destroyed || e.Derelict || !FtlRoster.LeavesHome(e)) continue;
                    if (CruiseDrives.Of(e) != null)
                    {
                        drives.Add(e);
                        carrier |= IsCarrier(e);
                    }
                    else craft.Add(e);
                }
            if (!carrier) return ForceKind.GateFleet;
            var aboard = FtlCarriage.Stow(drives, craft);
            foreach (var c in craft)
                if (!aboard.ContainsKey(c)) return ForceKind.GateFleet;
            return ForceKind.JumpStrikeGroup;
        }

        /// <summary>Whether <paramref name="leader"/>'s group is a jump strike group.</summary>
        public static bool IsStrikeGroup(World world, Ship leader) => KindOf(world, leader) == ForceKind.JumpStrikeGroup;

        /// <summary>Whether <paramref name="s"/> leads a navy group of its own here: a warship flagship, not a patrol wing or a
        /// picket, not inside a voyage, not the helm's.</summary>
        static bool Leads(World w, Ship s) =>
            !s.Destroyed && !s.IsEscort && !s.Derelict && s.Spec.IsMilitary && !s.PatrolWing && s.PicketGate == null && !s.Convoy &&
            s != w.PilotedShip && w.Fleet.IsPresent(s);

        // ---------------------------------------------------------------- reserves

        /// <summary>
        /// The strength of the strongest jump strike group fielded by anyone at war with <paramref name="f"/> (in any system,
        /// or between the stars): what a system of <paramref name="f"/>'s must be ready to meet. 0 when none.
        /// </summary>
        public static double StrikeThreat(Galaxy galaxy, Faction f)
        {
            if (galaxy == null || f == null) return 0;
            var dip = galaxy.Polities.Diplomacy;
            double worst = 0;
            foreach (var s in galaxy.Systems)
            {
                var w = s.World;
                foreach (var ship in w.Fleet.Ships)
                {
                    if (ship.Faction == f || ship.Faction == null || ship.Faction.IsPirate || dip.Get(f, ship.Faction) != Relation.War) continue;
                    if (!Leads(w, ship) || CruiseDrives.Of(ship) == null || !IsStrikeGroup(w, ship)) continue;
                    worst = Math.Max(worst, w.War.GroupStrength(ship));
                }
            }
            foreach (var c in galaxy.DeepSpace.Cruises)
            {
                var lead = c.Leader;
                if (lead == null || lead.Faction == f || lead.Faction == null || lead.Faction.IsPirate || !lead.Spec.IsMilitary) continue;
                if (dip.Get(f, lead.Faction) != Relation.War) continue;
                bool carrier = false;
                double strength = 0;
                foreach (var s in c.Ships)
                    if (!s.Destroyed && s.Combat != null)
                    {
                        strength += s.Combat.Strength;
                        carrier |= IsCarrier(s);
                    }
                if (carrier) worst = Math.Max(worst, strength);
            }
            return worst;
        }

        /// <summary>The warship strength <paramref name="f"/> keeps at home in <paramref name="world"/> against <paramref name="threat"/>
        /// (<see cref="StrikeThreat"/>): its share of it, where <paramref name="f"/> holds a port; none elsewhere.</summary>
        public static double ReserveWanted(World world, Faction f, double threat)
        {
            if (threat <= 0 || ReserveShare.Value <= 0 || world == null) return 0;
            foreach (var s in world.Economy.Settlements)
                if (s.Owner == f && !s.Fallen && s.Kind != SettlementKind.Haven) return threat * ReserveShare.Value;
            return 0;
        }

        /// <summary>
        /// Whether sending <paramref name="leader"/>'s group away from <paramref name="world"/> leaves <paramref name="f"/> the
        /// reserve it keeps there (<see cref="ReserveWanted"/>): the other groups it would leave at home are strong enough.
        /// </summary>
        public static bool LeavesReserve(World world, Ship leader, Faction f, double threat)
        {
            double wanted = ReserveWanted(world, f, threat);
            if (wanted <= 0) return true;
            double left = 0;
            foreach (var s in world.Fleet.Ships)
                if (s != leader && s.Faction == f && s.Voyage == null && Leads(world, s)) left += world.War.GroupStrength(s);
            return left >= wanted;
        }

        // ---------------------------------------------------------------- jumping in

        /// <summary>
        /// The way out from <paramref name="to"/>'s star (a unit vector, its system's km frame) at which a strike group of
        /// <paramref name="f"/> coming from <paramref name="from"/> drops out: of the flanks up to <see cref="MaxFlankDegrees"/>
        /// round from the way it comes in, the one farthest from every gate and gate picket held against it there, so it is not
        /// met at the exit as a gate fleet is. Straight in when nothing there watches for it.
        /// </summary>
        public static DVec3 DropDirection(StarSystem to, GalacticPosition from, Faction f)
        {
            var back = from.KmFrom(to.Origin);
            var a = back.LengthSquared > 0 ? back.Normalized : DVec3.UnitX;
            var watch = Watchers(to, f);
            if (watch.Count == 0) return a;
            var axis = Math.Abs(a.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
            var p = DVec3.Cross(axis, a).Normalized;
            double wellKm = CruiseDrives.WellLimitKm(to);
            var best = a;
            double bestClear = -1;
            foreach (double deg in flanks)
            {
                if (Math.Abs(deg) > MaxFlankDegrees) continue;
                double r = deg * Math.PI / 180;
                var dir = (a * Math.Cos(r) + p * Math.Sin(r)).Normalized;
                var at = dir * wellKm;
                double clear = double.MaxValue;
                foreach (var w in watch) clear = Math.Min(clear, (w - at).Length);
                // Strictly better only: the straight way in wins a tie.
                if (clear > bestClear + 1)
                {
                    bestClear = clear;
                    best = dir;
                }
            }
            return best;
        }

        /// <summary>Where <paramref name="f"/> would be seen dropping out in <paramref name="to"/>: the gates of anyone hostile to it
        /// and the posts of their pickets, km off its root.</summary>
        static List<DVec3> Watchers(StarSystem to, Faction f)
        {
            var list = new List<DVec3>();
            var w = to.World;
            foreach (var g in to.Gates)
            {
                if (g.Owner != null && g.Owner != f && w.Diplomacy.Hostile(f, g.Owner)) list.Add(g.PositionKm);
                foreach (var p in GatePickets.Of(g))
                    if (p.Faction != f && w.Diplomacy.Hostile(f, p.Faction)) list.Add(p.PicketPostKm);
            }
            return list;
        }

        /// <summary>The words for a campaign's force and mission, for the logs.</summary>
        public static string Describe(Campaign c) => c == null ? "" : c.Force == ForceKind.JumpStrikeGroup
            ? c.Mission switch
            {
                CampaignMission.Relieve => "jump strike group sent to relieve",
                CampaignMission.Raid => "jump strike group raiding",
                CampaignMission.SecondFront => "jump strike group opening a second front against",
                _ => "jump strike group sent against",
            }
            : "war fleet sent against";
    }
}
