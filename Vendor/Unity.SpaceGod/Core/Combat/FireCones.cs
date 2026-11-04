using System;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Every weapon hardpoint aims within a cone, not a facing arc (U.SPC-T533, docs/SHIP_ROSTER.md "Fire cones"). Everything
    /// fights in full 3D, so a gun bears on whatever lies inside its cone: a direction on the hull (its axis, by default the
    /// mount's own normal, the way the hull's surface faces where it sits) and a half-angle it can traverse within, above or
    /// below the hull as well as to the side. A turret has a wide cone round its normal; a spinal mount or a fixed tube a
    /// narrow one along the keel. The half-angle is a datum on the mount's <see cref="ModuleSpec"/>
    /// (<see cref="ModuleSpec.ConeHalfDeg"/>) unless the hull frame gives the hardpoint its own
    /// (<see cref="SubsystemDef.ConeHalfDeg"/>, <see cref="SubsystemDef.ConeAxis"/>), and the settings here scale every cone.
    /// Battery orders, the AI captain's choice of which guns to bring to bear, the player's fire control and the turrets a
    /// player mans all work from the cones.
    /// </summary>
    [SettingsSource]
    public static class FireCones
    {
        // ------------------------------------------------------------ settings

        public static readonly RangeSetting TurretTraverse = new RangeSetting("cone.turretTraverse", SettingGroups.Weapons,
            "Turret traverse", 1, 0.25, 1.75, 0.05,
            "How far every turret can traverse off its mount's normal, against its design: below 1 narrows every turret's cone, above 1 widens it (no cone ever wider than the whole sky).",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "fire cone turret traverse arc mount bear 3d");

        public static readonly RangeSetting FixedCone = new RangeSetting("cone.fixed", SettingGroups.Weapons,
            "Fixed mount cone", 1, 0.25, 3, 0.05,
            "How far a spinal mount or fixed tube can be laid off the keel, against its design: the hull itself has to lay the rest.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "fire cone spinal fixed tube keel gimbal bear");

        public static readonly ToggleSetting AiBringsGunsToBear = new ToggleSetting("cone.aiBearing", SettingGroups.Broadsides,
            "AI captains bring their guns to bear", true,
            "An AI hull turns so that the guns whose cones can take its target carry the most firepower onto it; off, it simply points its bow at the target.",
            "fire cone bear turn broadside ai doctrine");

        // ------------------------------------------------------------ hull-frame directions (right, up, forward)

        public static readonly DVec3 Bow = new DVec3(0, 0, 1);
        public static readonly DVec3 Stern = new DVec3(0, 0, -1);
        public static readonly DVec3 Starboard = new DVec3(1, 0, 0);
        public static readonly DVec3 Port = new DVec3(-1, 0, 0);
        public static readonly DVec3 Dorsal = new DVec3(0, 1, 0);
        public static readonly DVec3 Ventral = new DVec3(0, -1, 0);

        /// <summary>The beam a hull is reckoned with when its design is not to hand, as a share of its length.</summary>
        public const double DefaultBeamFraction = 0.2;

        // ------------------------------------------------------------ what a mount is

        /// <summary>
        /// A mount laid along the keel rather than on its own turret: a spinal mount, a fixed tube or launcher (the parts a
        /// <see cref="ModuleSpec.Spinal"/> module is), a fighter's fixed guns, a bomber's slung torpedo or a drill head.
        /// </summary>
        public static bool IsFixed(SubsystemDef d)
        {
            if (d.Spinal) return true;
            switch (d.Kind)
            {
                case SubsystemKind.FighterGuns:
                case SubsystemKind.Torpedo:
                case SubsystemKind.Drill:
                    return true;
            }
            var m = ModuleOf(d.Kind);
            return m.HasValue && ModuleSpec.Of(m.Value).Spinal;
        }

        /// <summary>The module a hardpoint kind is, where a yard fits one, or null for the parts of a hull no yard refits.</summary>
        public static ModuleKind? ModuleOf(SubsystemKind k)
        {
            switch (k)
            {
                case SubsystemKind.LaserTurret: return ModuleKind.LaserTurret;
                case SubsystemKind.HeavyLaser: return ModuleKind.HeavyLaser;
                case SubsystemKind.Railgun: return ModuleKind.Railgun;
                case SubsystemKind.MissileLauncher: return ModuleKind.MissileLauncher;
                case SubsystemKind.PointDefense: return ModuleKind.PointDefense;
                case SubsystemKind.Autocannon: return ModuleKind.Autocannon;
                case SubsystemKind.LightRailgun: return ModuleKind.LightRailgun;
                case SubsystemKind.EmpLauncher: return ModuleKind.EmpLauncher;
                case SubsystemKind.MineLayer: return ModuleKind.MineLayer;
                case SubsystemKind.AutoTurret: return ModuleKind.AutoTurret;
                case SubsystemKind.TorpedoTube: return ModuleKind.TorpedoTube;
                case SubsystemKind.TachyonLance: return ModuleKind.TachyonLance;
                case SubsystemKind.PhaserArray: return ModuleKind.PhaserArray;
                case SubsystemKind.PlasmaCannon: return ModuleKind.PlasmaCannon;
                case SubsystemKind.AntimatterTorpedo: return ModuleKind.AntimatterTorpedo;
                case SubsystemKind.FlakCannon: return ModuleKind.FlakCannon;
                case SubsystemKind.InterceptorMissile: return ModuleKind.InterceptorMissile;
                case SubsystemKind.Coilgun: return ModuleKind.Coilgun;
                case SubsystemKind.ShieldGenerator: return ModuleKind.ShieldGenerator;
                case SubsystemKind.Engine: return ModuleKind.Engine;
                default: return null;
            }
        }

        /// <summary>The half-angle a mount of <paramref name="k"/> is built with, deg: its module's
        /// (<see cref="ModuleSpec.ConeHalfDeg"/>), or for the parts no yard fits, their own (a fighter's fixed guns and a
        /// bomber's torpedo along the keel, pods and decoys thrown anywhere).</summary>
        public static double DesignHalfDeg(SubsystemKind k)
        {
            switch (k)
            {
                case SubsystemKind.FighterGuns: return 12;
                case SubsystemKind.Torpedo: return 20;
                case SubsystemKind.Drill: return 10;
                case SubsystemKind.BoardingPods:
                case SubsystemKind.Countermeasures: return 180;
            }
            var m = ModuleOf(k);
            return m.HasValue ? ModuleSpec.Of(m.Value).ConeHalfDeg : 180;
        }

        /// <summary>
        /// How far off its axis <paramref name="d"/> can bear, deg (0-180): the hull frame's own figure for the hardpoint, else
        /// its module's, scaled by <see cref="FixedCone"/> for a fixed mount and <see cref="TurretTraverse"/> for a turret.
        /// </summary>
        public static double HalfDeg(SubsystemDef d)
        {
            double design = d.ConeHalfDeg ?? DesignHalfDeg(d.Kind);
            if (design >= 180) return 180;
            double scale = IsFixed(d) ? FixedCone.Value : TurretTraverse.Value;
            return Math.Max(0, Math.Min(180, design * scale));
        }

        /// <summary>
        /// The direction <paramref name="d"/> aims about, unit, in the hull frame (right, up, forward): the hull frame's own
        /// axis for the hardpoint if it gives one; along the keel for a fixed mount (astern if it faces aft); else the
        /// mount's normal, the way the hull's surface faces where it sits (<see cref="MountNormal"/>).
        /// </summary>
        public static DVec3 Axis(SubsystemDef d, double beamFraction = DefaultBeamFraction)
        {
            if (d.ConeAxis is DVec3 a && a.LengthSquared > 1e-18) return a.Normalized;
            if (IsFixed(d)) return d.FacesAft ? Stern : Bow;
            return MountNormal(d.X, d.Y, d.Z, beamFraction, d.FacesAft);
        }

        public static DVec3 Axis(SubsystemDef d, CombatSpec spec) => Axis(d, spec?.BeamFraction ?? DefaultBeamFraction);

        /// <summary>
        /// The outward normal of a hull at a point on it (ship lengths, hull frame): the hull is a capsule its beam wide
        /// (<see cref="Collisions.SurfacePoint"/>), so the normal points straight out from the keel along its length and
        /// rounds over to the bow or stern at its ends. A point on the keel line itself, where no side is nearer than any
        /// other, faces up (or along the keel past the capsule's straight length).
        /// </summary>
        public static DVec3 MountNormal(double x, double y, double z, double beamFraction = DefaultBeamFraction, bool facesAft = false)
        {
            double r = Math.Max(0.005, Math.Min(0.5, beamFraction * 0.5));
            double core = Math.Max(0, 0.5 - r);
            double along = Math.Max(-core, Math.Min(core, z));
            var n = new DVec3(x, y, z - along);
            if (n.LengthSquared > 1e-10) return n.Normalized;
            return Math.Abs(z) >= core && core > 0 ? (z > 0 ? Bow : Stern) : facesAft ? Stern : Dorsal;
        }

        // ------------------------------------------------------------ bearing

        /// <summary>Whether a direction in the hull frame (right, up, forward; any length) lies inside <paramref name="d"/>'s cone.</summary>
        public static bool Covers(SubsystemDef d, CombatSpec spec, DVec3 dirLocal)
        {
            double half = HalfDeg(d);
            if (half >= 180) return true;
            if (dirLocal.LengthSquared < 1e-24) return false;
            return OffAxisDeg(d, spec, dirLocal) <= half + 1e-9;
        }

        /// <summary>How far a direction in the hull frame lies off <paramref name="d"/>'s axis, deg (0-180).</summary>
        public static double OffAxisDeg(SubsystemDef d, CombatSpec spec, DVec3 dirLocal)
        {
            if (dirLocal.LengthSquared < 1e-24) return 0;
            double cos = DVec3.Dot(Axis(d, spec), dirLocal.Normalized);
            return Math.Acos(Math.Max(-1, Math.Min(1, cos))) * Astro.Rad2Deg;
        }

        /// <summary>How far past the edge of <paramref name="d"/>'s cone a direction in the hull frame lies, deg: 0 inside it.</summary>
        public static double OutsideDeg(SubsystemDef d, CombatSpec spec, DVec3 dirLocal) => Math.Max(0, OffAxisDeg(d, spec, dirLocal) - HalfDeg(d));

        /// <summary>Whether two mounts' cones overlap: some direction lies inside both (their axes closer than their half-angles together).</summary>
        public static bool Overlap(SubsystemDef a, SubsystemDef b, CombatSpec spec)
        {
            double cos = DVec3.Dot(Axis(a, spec), Axis(b, spec));
            double between = Math.Acos(Math.Max(-1, Math.Min(1, cos))) * Astro.Rad2Deg;
            return between <= HalfDeg(a) + HalfDeg(b) + 1e-9;
        }

        /// <summary>
        /// A direction (unit) brought within <paramref name="d"/>'s cone, both in the same frame as <paramref name="axis"/>:
        /// unchanged inside it, else swung back onto the cone's edge on the way to it.
        /// </summary>
        public static DVec3 Clamp(DVec3 dir, DVec3 axis, double halfDeg)
        {
            if (halfDeg >= 180 || dir.LengthSquared < 1e-24) return dir;
            var a = axis.Normalized;
            var v = dir.Normalized;
            double off = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(a, v))));
            double half = halfDeg * Astro.Deg2Rad;
            if (off <= half + 1e-12) return v;
            return DVec3.TurnStep(a, v, half / off);
        }
    }
}
