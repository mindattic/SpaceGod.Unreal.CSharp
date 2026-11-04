using System;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Shields come from emitters on the hull, not facing arcs (U.SPC-T501, docs/SHIP_ROSTER.md "Shields"). Everything
    /// fights in full 3D, so a hull's shield is the fields of the emitters mounted on it, each a hardpoint like a turret
    /// (<see cref="SubsystemKind.ShieldGenerator"/>) projecting its own field over the patch of hull around it: the part of
    /// the hull nearer to it than to any other emitter. A shot drains the field of the patch it actually strikes in 3D;
    /// a field shot away, or its emitter destroyed, leaves that patch bare while the rest of the hull stays covered, so a
    /// ship can be holed in one place and protected everywhere else. Gunners (AI crews, and a player's crews at the helm
    /// unless told otherwise) favour hardpoints in a weakened patch (<see cref="FocusWeight"/>), and the player's own aim
    /// hits whichever patch it lands on. A capital ship's emergency repair crews, its damage control teams, bring a
    /// shot-away emitter or gun mount back given time (<see cref="Broadsides.StepDamageControl"/>). Every rate is a setting.
    /// </summary>
    [SettingsSource]
    public static class ShieldEmitters
    {
        // ------------------------------------------------------------ settings

        public static readonly RangeSetting RegenDelay = new RangeSetting("shield.regenDelay", SettingGroups.Broadsides,
            "Shield field regrowth delay", 8, 0, 30, 1,
            "Seconds a shield emitter's field must go unhit before it starts to regrow: each patch of hull keeps its own clock, so the patch under fire stays down while the rest recover.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " s", "shield emitter field regen regrow delay patch");

        public static readonly RangeSetting RegenRate = new RangeSetting("shield.regenRate", SettingGroups.Broadsides,
            "Shield field regrowth rate", 1, 0, 3, 0.1,
            "How fast shield emitters regrow their fields, against each hull's own rating: 0 means a field shot away stays down for the battle, 1 is as designed.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "shield emitter field regen rate patch");

        public static readonly RangeSetting FocusFire = new RangeSetting("shield.focusFire", SettingGroups.Broadsides,
            "Gunners focus a weakened patch", 2, 0, 5, 0.25,
            "How strongly gunners favour a hardpoint in a patch of hull whose shield field is weakened: a mount in a patch holed outright is this many times again as likely to be picked (0 ignores the fields).",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "shield emitter patch focus fire gunners ai holed");

        public static readonly RangeSetting EmergencyRepairRate = new RangeSetting("broadside.emergencyRepair", SettingGroups.Broadsides,
            "Emergency repair rate", 1, 0, 3, 0.25,
            "How fast a capital ship's emergency repair crews bring back a shield emitter or gun mount shot away in battle: 0 leaves it lost until a yard refits it, 1 is as designed.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "emergency repair crew damage control shield emitter turret restore capital");

        public static readonly RangeSetting EmergencyRepairWork = new RangeSetting("broadside.emergencyRepairWork", SettingGroups.Broadsides,
            "Emergency repair work", 0.6, 0.1, 2, 0.1,
            "The work, as a share of a mount's hit points, the crews put in to bring a shot-away shield emitter or gun mount back.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "emergency repair crew damage control shield emitter turret restore capital");

        public static readonly RangeSetting RestoredHealth = new RangeSetting("broadside.restoredHealth", SettingGroups.Broadsides,
            "Emergency repair restores to", 0.3, 0.05, 0.85, 0.05,
            "How whole a shot-away mount is when the crews bring it back (an emitter's field then regrows from nothing).",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "emergency repair crew damage control shield emitter turret restore capital");

        // ------------------------------------------------------------ patches

        /// <summary>
        /// Where on the hull a strike at <paramref name="local"/> (ship lengths, hull frame) meets the shield: the point
        /// itself if it is on or in the hull; a point out beyond the hull is brought in to the hull's surface in its
        /// direction (a blast, a missile's entry, a craft off the beam); and one at the hull's very centre (a shot at the
        /// hull as a whole) is put on the surface facing back along <paramref name="incomingLocal"/>, the way it came.
        /// </summary>
        public static DVec3 StrikePoint(CombatSpec spec, DVec3 local, DVec3? incomingLocal = null)
        {
            if (local.LengthSquared < 0.02 * 0.02)
            {
                if (incomingLocal.HasValue && incomingLocal.Value.LengthSquared > 1e-18) return Collisions.SurfacePoint(spec, incomingLocal.Value * -1);
                return local;
            }
            var surface = Collisions.SurfacePoint(spec, local);
            return local.LengthSquared > surface.LengthSquared ? surface : local;
        }

        /// <summary>The live field strength of the patch a hull point lies in, 0 (holed: its emitter shot away or its field
        /// down) to 1 (full). A hull with no emitters at all reads 1: it is bare everywhere, with no weaker spot to find.</summary>
        public static double PatchStrength(ShipCombat c, DVec3 local)
        {
            var patch = c.PatchAt(local);
            if (patch == null) return 1;
            return patch.ShieldFraction;
        }

        /// <summary>
        /// How much more a gunner wants <paramref name="s"/> for the field over it: 1 under a full field, up to
        /// 1 + <see cref="FocusFire"/> in a patch holed outright, so fire goes where the hull is already bare.
        /// </summary>
        public static double FocusWeight(ShipCombat c, Subsystem s)
        {
            double k = FocusFire.Value;
            if (k <= 0 || s.Def.Buried) return 1;
            return 1 + k * (1 - PatchStrength(c, new DVec3(s.Def.X, s.Def.Y, s.Def.Z)));
        }

        /// <summary>Whether a shot-away mount is one emergency repair crews can bring back: a shield emitter or a gun mount.</summary>
        public static bool Restorable(Subsystem s) => s.Destroyed && (s.Kind == SubsystemKind.ShieldGenerator || s.Def.IsWeapon);
    }
}
