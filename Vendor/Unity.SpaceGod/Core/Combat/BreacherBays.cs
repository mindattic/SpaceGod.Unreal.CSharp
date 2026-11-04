using System;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Breacher bays and the Breacher rounds they fire (U.SPC-T466, renamed from the Assault Bay and made a fitting any
    /// ship can carry by U.SPC-T521). A Breacher is a whole team of marines (<see cref="TeamSize"/>) sealed in an armoured
    /// round, fired at high velocity (<see cref="RoundSpeedKmS"/>) from a Breacher Bay: the firing room, a facility
    /// (<see cref="FacilityKinds.BreacherBay"/>) fitted into the internal space of any hull, navy, pirate or merchant escort,
    /// that has the free sections for it (<see cref="FacilityKind.Size"/>), the reactor power to spare for its breech
    /// (<see cref="PowerMw"/>, <see cref="Loadout.FreePowerMw"/>), the crew to man it (<see cref="Crew"/>,
    /// <see cref="Loadout.FreeCrew"/>) and a Marine Habitat to supply the teams (<see cref="FacilityKind.Requires"/>). The
    /// yard's fitting screen shows those costs; navies design it into the hulls built for boarding (the gate breaker) and
    /// fit it as a refit to their big hulls (<see cref="Outfitting.NavyFacilities"/>), raider leaders carry one. A Breacher is only
    /// ever such a round, never a ship (U.SPC-T517): it has no ship class, design, yard entry or crew of its own, and is
    /// drawn in the scene as its own bullet-shaped round model. The round flies like a bullet, with only a slight course
    /// correction (<see cref="SteerKmS2"/>, <see cref="Correct"/>), until it strikes a hull, drills in and lets its team out into the compartment it
    /// hit, where the compartment-by-compartment boarding fight (<see cref="Interior"/>) takes over and the target's own
    /// marines try to stop them. It cannot get through a shield still holding more than a quarter of its strength, and a
    /// round that misses or glances off goes adrift with its team alive inside (U.SPC-T519, <see cref="DriftingRounds"/>). The bay
    /// fires again only as fast as the habitats can ready the next team (<see cref="IntervalSeconds"/>). The lighter option
    /// is still the boarding pods' twelve-marine squads (<see cref="WeaponSpec.PodMarines"/>). Every figure is in the
    /// settings menu's Broadsides group.
    /// </summary>
    [SettingsSource]
    public static class BreacherBays
    {
        static string Seconds(double v) => v.ToString("0", CultureInfo.InvariantCulture) + " s";

        public static readonly RangeSetting TeamSize = new RangeSetting("war.breacherTeam", SettingGroups.Broadsides,
            "Breacher team", 70, 10, 200, 5,
            "How many marines ride in one Breacher round fired from a Breacher bay. The ship's marine habitats must have a whole " +
            "team ready before the bay can fire.", v => v.ToString("0", CultureInfo.InvariantCulture) + " marines",
            "breacher bay assault boarding round marines team");

        public static readonly RangeSetting ReadySeconds = new RangeSetting("war.breacherReady", SettingGroups.Broadsides,
            "Breacher team readying", 120, 10, 900, 10,
            "How long (seconds) one marine habitat takes to suit up the next team and load it into a Breacher round. A ship with " +
            "more habitats readies teams faster; the Breacher bay reloads no faster than they do.", Seconds,
            "breacher bay assault reload marines habitat ready team");

        public static readonly RangeSetting CycleSeconds = new RangeSetting("war.breacherCycle", SettingGroups.Broadsides,
            "Breacher bay cycle", 20, 2, 120, 1,
            "How long (seconds) a Breacher bay takes to fire again with a team ready and waiting. More bays share the work.", Seconds,
            "breacher bay assault reload cycle");

        public static readonly RangeSetting RoundSpeedKmS = new RangeSetting("war.breacherSpeed", SettingGroups.Broadsides,
            "Breacher round speed", 2.5, 0.5, 8, 0.1,
            "How fast (km/s, relative to the ship that fired it) a Breacher round leaves the Breacher bay.",
            v => v.ToString("0.0", CultureInfo.InvariantCulture) + " km/s", "breacher round speed velocity bay assault");

        public static readonly RangeSetting SteerKmS2 = new RangeSetting("war.breacherSteer", SettingGroups.Broadsides,
            "Breacher course correction", 0.02, 0, 0.2, 0.005,
            "How hard (km/s²) a Breacher round's thrusters can push it off its line toward its target: a slight correction, " +
            "not a missile's homing.", v => v.ToString("0.000", CultureInfo.InvariantCulture) + " km/s²",
            "breacher round steering correction ballistic");

        public static readonly RangeSetting RangeKm = new RangeSetting("war.breacherRange", SettingGroups.Broadsides,
            "Breacher range", 15, 2, 40, 1,
            "How far (km) a Breacher bay fires a Breacher round at a hull.", v => v.ToString("0", CultureInfo.InvariantCulture) + " km",
            "breacher bay assault range");

        public static readonly RangeSetting ImpactDamage = new RangeSetting("war.breacherImpact", SettingGroups.Broadsides,
            "Breacher impact", 300, 0, 2000, 25,
            "How much damage a Breacher round's drill does to the hull it strikes as it bores in.",
            v => v.ToString("0", CultureInfo.InvariantCulture), "breacher round drill impact damage");

        /// <summary>How long a round is, km: a team's casing behind its drill head, what its model is drawn at (U.SPC-T517).</summary>
        public const double RoundLengthKm = 0.06;

        /// <summary>A round's armour against point defence (a pod's is 40).</summary>
        public const double RoundHp = 400;

        /// <summary>How much harder a team storming out of a round fights than a squad off a pod: the shock of the breach.</summary>
        public const double ShockEdge = 1.15;

        /// <summary>A target's shields hold a round off above this share of their strength.</summary>
        public const double ShieldShare = 0.25;

        /// <summary>The reactor power the bay's breech, cradle and magazine hoists draw, MW: a destroyer's reactor or bigger
        /// has it to spare (<see cref="Loadout.ReactorReservePerScale"/>), a corvette's or anything smaller has not.</summary>
        public const double PowerMw = 30;

        /// <summary>The hands that man the bay (the breech crew, loaders and the fire-control watch): a hull with fewer to
        /// spare (<see cref="Loadout.FreeCrew"/>), a freighter's or a gunship's, cannot work one.</summary>
        public const double Crew = 20;

        /// <summary>The facility kind.</summary>
        public static FacilityKind Kind => FacilityKinds.Of(FacilityKinds.BreacherBay);

        /// <summary>Breacher bays in <paramref name="s"/>.</summary>
        public static int Count(Ship s) => FleetCoordination.Count(s, FacilityKinds.BreacherBay);

        /// <summary>Marine habitats in <paramref name="s"/>, which ready its teams.</summary>
        public static int Habitats(Ship s) => FleetCoordination.Count(s, FacilityKinds.MarineHabitat);

        /// <summary>
        /// Seconds between rounds from <paramref name="s"/>'s bays: the slower of its bays' cycle (shared between them) and
        /// its habitats readying the next team (shared between them). Infinite with no bay or no habitat.
        /// </summary>
        public static double IntervalSeconds(Ship s)
        {
            int bays = Count(s), habitats = Habitats(s);
            if (bays <= 0 || habitats <= 0) return double.PositiveInfinity;
            return Math.Max(CycleSeconds.Value / bays, ReadySeconds.Value / habitats);
        }

        /// <summary>Whether <paramref name="interior"/>'s habitats have a whole team ready to fire.</summary>
        public static bool TeamReady(Interior interior) => MarineHabitats.Ready(interior) >= TeamSize.Value - 1e-9;

        /// <summary>Whether a round would get into <paramref name="c"/>: no shield generator, or its shields down to a quarter.</summary>
        public static bool Opens(ShipCombat c) => c != null && (!c.HasShieldGenerator || c.Shield <= c.Spec.Shield * ShieldShare);

        /// <summary>
        /// Whether a hull with loadout <paramref name="l"/> could carry a bay at all, space and habitat aside: a hull its
        /// category and class allow, with the reactor power and the crew to spare for it.
        /// </summary>
        public static bool CanCarry(Loadout l)
        {
            var k = Kind;
            return l != null && k != null && k.AllowedOn(l.Category, l.Class)
                   && l.FreePowerMw >= k.PowerMw - 1e-9 && l.FreeCrew >= k.Crew - 1e-9;
        }

        /// <summary>Whether <paramref name="l"/>'s habitats quarter a whole team, so a bay in it could ever fire.</summary>
        public static bool CanMan(Loadout l) => l != null && MarineHabitats.Complement(l) >= TeamSize.Value - 1e-9;

        /// <summary>
        /// Fits a Breacher bay into <paramref name="s"/> as part of its build, first fitting habitats until they quarter a
        /// whole team (a raider destroyer's one habitat is too small), as room allows. False if it could not take the bay
        /// (no room, or not the reactor power or crew to spare for it).
        /// </summary>
        public static bool Equip(Ship s)
        {
            if (s == null || Kind == null) return false;
            var habitat = MarineHabitats.Kind;
            var l = Outfitting.LoadoutOf(s);
            if (l == null || !CanCarry(l)) return false;
            while (MarineHabitats.Complement(l) < TeamSize.Value && l.FreeSpace >= habitat.Size + Kind.Size - 1e-9
                   && Outfitting.Install(s, habitat)) { }
            return Outfitting.Install(s, Kind);
        }

        /// <summary>
        /// A round's slight course correction: its velocity <paramref name="vel"/> turned toward <paramref name="toward"/>,
        /// its speed kept, by no more than <paramref name="accelKmS2"/> of sideways push over <paramref name="dt"/>.
        /// </summary>
        public static DVec3 Correct(DVec3 vel, DVec3 toward, double accelKmS2, double dt)
        {
            double speed = vel.Length;
            if (speed < 1e-12 || toward.LengthSquared < 1e-18 || accelKmS2 <= 0 || dt <= 0) return vel;
            var dv = toward.Normalized * speed - vel;
            double most = accelKmS2 * dt;
            var turned = vel + (dv.Length > most ? dv.Normalized * most : dv);
            return turned.LengthSquared < 1e-18 ? vel : turned.Normalized * speed;
        }

        /// <summary>
        /// Where to aim a round fired at <paramref name="speedKmS"/> from <paramref name="from"/> (moving at
        /// <paramref name="fromVel"/>, which the round keeps) to meet a target at <paramref name="at"/> moving at
        /// <paramref name="atVel"/>: the target's place when the round gets there, if both hold their courses.
        /// </summary>
        public static DVec3 Lead(DVec3 from, DVec3 fromVel, DVec3 at, DVec3 atVel, double speedKmS)
        {
            var rel = atVel - fromVel;
            var aim = at;
            for (int i = 0; i < 4; i++) aim = at + rel * ((aim - from).Length / Math.Max(speedKmS, 1e-6));
            return aim;
        }
    }
}
