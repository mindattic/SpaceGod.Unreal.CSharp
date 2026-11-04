using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Gate breakers (U.SPC-T478): the siege capital built to take a jump gate intact (<see cref="ShipClass.GateBreaker"/>).
    /// It is the most heavily armoured hull afloat, so it can take a gate's forts' fire as it closes, and it fights them at
    /// close range with brawling batteries of plasma cannons; in a gate battle it goes for the forts before anything else
    /// (<see cref="FortFocus"/>), and its two boarding shuttles put marines aboard any fort the fight silences. It carries the
    /// teams a gate is taken with: boarding teams (the biggest marine complement afloat) and engineering teams (its siege
    /// engineering bay, <see cref="FacilityKinds.SiegeEngineering"/>). It always has its own FTL drive, since it attacks the
    /// gates the rest of the fleet would use.
    /// <para>On campaign (see <see cref="InterstellarWars"/>), a war fleet whose group has a gate breaker with its engineering
    /// bay and a boarding team's worth of marines aboard (<see cref="TeamMarines"/>) storms an enemy gate on the first day its
    /// forts are silenced and takes it intact: its engineers make good the damage the ring has taken (<see cref="FieldRepair"/>),
    /// so a ring the enemy sabotaged is back in service at once rather than waiting years for its owner's constructors. It
    /// never wrecks a gate (owner 2026-10-24: no gate wrecking yet).</para>
    /// <para>A navy at war lays down a gate breaker at its next keel when it has fewer than <see cref="PerNavy"/> afloat. Which
    /// classes break gates is data (<see cref="Classes"/>, <see cref="Register"/>). Every figure is a
    /// setting in the menu's Broadsides and AI groups.</para>
    /// </summary>
    [SettingsSource]
    public static class GateBreakers
    {
        static string Count(double v) => v.ToString("0", CultureInfo.InvariantCulture);

        static string Percent(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        public static readonly RangeSetting TeamMarines = new RangeSetting("war.breakerTeam", SettingGroups.Broadsides,
            "Gate breaker boarding teams", 80, 10, 300, 10,
            "How many marines a gate breaker must have aboard for its boarding teams to storm a jump gate whose forts are silenced " +
            "and take it intact on the first day; with fewer the fleet holds the gate the usual days before it is theirs.",
            v => Count(v) + " marines", "gate breaker siege boarding team marines seize gate");

        public static readonly RangeSetting FieldRepair = new RangeSetting("war.breakerRepair", SettingGroups.Broadsides,
            "Gate breaker field repairs", 1, 0, 1, 0.05,
            "The share of the damage a jump gate's ring has taken (sabotage, a fight) that a gate breaker's engineering teams make " +
            "good on the spot when they seize it, so a sabotaged ring is back in service at once.", Percent,
            "gate breaker engineers repair ring seize intact sabotage");

        public static readonly RangeSetting FortFocus = new RangeSetting("war.breakerFortFocus", SettingGroups.Broadsides,
            "Gate breaker fort preference", 15, 0, 40, 1,
            "How strongly a gate breaker picks a gate's fort over anything else in a gate battle (as if it were that many km nearer).",
            v => Count(v) + " km", "gate breaker siege fort target preference");

        public static readonly RangeSetting PerNavy = new RangeSetting("war.breakersPerNavy", SettingGroups.Ai,
            "Gate breakers per navy at war", 1, 0, 4, 1,
            "How many gate breakers a navy at war keeps afloat: one short, the next yard that can afford one lays it down before " +
            "its build plan's next hull. 0 leaves them to a player's orders.", Count,
            "gate breaker siege capital build navy war yard");

        // ---------------------------------------------------------------- who breaks gates

        static readonly HashSet<ShipClass> classes = new HashSet<ShipClass> { ShipClass.GateBreaker };

        /// <summary>The classes that break gates: a hull of one with its siege engineering bay is a gate breaker.</summary>
        public static IReadOnlyCollection<ShipClass> Classes => classes;

        /// <summary>Makes hulls of <paramref name="cls"/> gate breakers (or not): how a new siege class plugs in.</summary>
        public static void Register(ShipClass cls, bool breaks = true)
        {
            if (breaks) classes.Add(cls);
            else classes.Remove(cls);
        }

        /// <summary>Whether hulls of <paramref name="cls"/> break gates.</summary>
        public static bool IsClass(ShipClass cls) => classes.Contains(cls);

        /// <summary>Whether <paramref name="s"/> is a gate breaker able to work on a gate: a hull of a gate-breaking class,
        /// afloat and in fighting order, with its siege engineering bay (its refit's, or its design's as laid down).</summary>
        public static bool Works(Ship s)
        {
            if (s?.Spec == null || !IsClass(s.Spec.Class) || s.Destroyed || s.Derelict || s.Combat == null || s.Combat.Disabled) return false;
            return s.Loadout != null
                ? s.Loadout.CountFacility(FacilityKinds.SiegeEngineering) > 0
                : FacilityLayouts.CountOf(FacilityLayouts.For(s.Spec.Class), FacilityKinds.SiegeEngineering) > 0;
        }

        /// <summary>The gate breaker in <paramref name="group"/> whose boarding and engineering teams can storm a silenced
        /// gate and take it intact (<see cref="TeamMarines"/> aboard), or null.</summary>
        public static Ship Stormer(IEnumerable<Ship> group)
        {
            if (group == null) return null;
            foreach (var s in group)
                if (Works(s) && s.Combat.MarinesAvailable >= TeamMarines.Value) return s;
            return null;
        }

        /// <summary>Days a war fleet with a gate breaker's teams holds a silenced gate before it is theirs: they storm it the
        /// first day, whatever <paramref name="holdDays"/> it would hold otherwise.</summary>
        public static double GateHoldDays(double holdDays) => Math.Min(holdDays, 1);

        /// <summary>
        /// A gate breaker's engineering teams make good <see cref="FieldRepair"/> of the damage <paramref name="gate"/>'s ring
        /// has taken as they seize it; returns how much of the ring they put back (0..1).
        /// </summary>
        public static double MakeGood(JumpGate gate)
        {
            if (gate == null || gate.Integrity >= 1) return 0;
            double repaired = (1 - gate.Integrity) * Math.Max(0, Math.Min(1, FieldRepair.Value));
            gate.Integrity = Math.Min(1, gate.Integrity + repaired);
            return repaired;
        }

        // ---------------------------------------------------------------- in battle

        /// <summary>How much nearer (km) <paramref name="u"/>, a gate breaker under its crew's command, counts a gate's fort
        /// <paramref name="o"/> when picking its mark: it is there to break the gate's guns first.</summary>
        public static double TargetBias(BattleUnit u, BattleUnit o)
        {
            if (u == null || o == null || o.Fort == null || u.PlayerControlled || u.Ship?.Spec == null || !IsClass(u.Ship.Spec.Class)) return 0;
            return FortFocus.Value;
        }

        // ---------------------------------------------------------------- building them

        /// <summary>Whether <paramref name="f"/>, at war, lays down a gate breaker at its next keel when it already has
        /// <paramref name="have"/> of them afloat (<see cref="PerNavy"/>).</summary>
        public static bool Wanted(Faction f, bool atWar, int have) =>
            f != null && atWar && f.IsMilitary && !f.IsPirate && have < (int)Math.Round(PerNavy.Value);
    }
}
