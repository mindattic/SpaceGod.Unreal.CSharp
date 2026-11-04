using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>The player's hand on one gun mount while it is embodied (see <see cref="MountedTurret"/>).</summary>
    public sealed class MountControl
    {
        /// <summary>Where the player wants the barrel (unit, ecliptic axes).</summary>
        public DVec3 Aim;

        /// <summary>The trigger, held this frame.</summary>
        public bool Trigger;

        /// <summary>Laid as one of a whole station's guns (<see cref="StationSeat"/>, U.SPC-T447), not as a turret on its own.</summary>
        public bool ByStation;
    }

    /// <summary>
    /// One gun mount on a hull or station in a battle, as an <see cref="IEmbodiable"/> (U.SPC-T18): a ship's
    /// battery, a defence platform's heavy laser, a fort's point defence. Taken, it traverses toward the
    /// player's aim at its own rate within its own fire cone and fires down its barrel on the trigger, every shot a
    /// real aimed one (see <see cref="Battle.FireManned"/>); every other mount on the hull goes on under its
    /// crew, and the hull itself flies as before. Handed back, its crew works it again.
    /// </summary>
    public sealed class MountedTurret : IEmbodiable
    {
        readonly IEmbodiable host;

        public Battle Battle { get; }
        public BattleUnit Unit { get; }
        public Subsystem Mount { get; }

        public MountedTurret(Battle battle, BattleUnit unit, Subsystem mount, IEmbodiable host = null)
        {
            Battle = battle;
            Unit = unit;
            Mount = mount;
            this.host = host;
        }

        public string Name => Unit.Name + " " + SubsystemDef.Name(Mount.Kind).ToLowerInvariant() + " " + (Mount.Index + 1);
        public EmbodimentKind Kind => EmbodimentKind.Turret;
        public Faction Faction => Unit.Faction;
        public IEmbodiable Host => host;
        public bool CanEmbody => Blocker == null;
        /// <summary>The player holds this mount on its own: one laid as part of a whole station (<see cref="StationSeat"/>) is the station's.</summary>
        public bool Embodied => Battle.Manned.TryGetValue(Mount, out var m) && !m.ByStation;

        public string Blocker
        {
            get
            {
                if (!IsMannable(Mount.Def)) return "not a gun mount";
                if (Battle.Over) return "the fight is over";
                if (!Unit.Present) return "gone";
                if (Unit.Captured) return "taken as a prize";
                if (Mount.Destroyed) return "shot away";
                return null;
            }
        }

        /// <summary>A direct-fire gun the player can lay: not a launcher, a mine layer, an EMP or a boarding bay.</summary>
        public static bool IsMannable(SubsystemDef d) =>
            d.Weapon != null && !d.Weapon.Missile && d.Weapon.Deploy == DeployKind.None && d.Weapon.EmpSeconds <= 0 && d.Kind != SubsystemKind.BoardingPods;

        /// <summary>How fast a mount traverses, deg/s: point defence and light guns whip round, heavy lasers swing
        /// slowly, and a spinal railgun barely moves within its gimbal.</summary>
        public static double TraverseDegS(SubsystemKind k)
        {
            switch (k)
            {
                case SubsystemKind.PointDefense:
                case SubsystemKind.AutoTurret: return 120;
                case SubsystemKind.FlakCannon: return 100;
                case SubsystemKind.Autocannon:
                case SubsystemKind.FighterGuns: return 90;
                case SubsystemKind.LaserTurret:
                case SubsystemKind.PhaserArray: return 45;
                case SubsystemKind.PlasmaCannon: return 20;
                case SubsystemKind.LightRailgun: return 30;
                case SubsystemKind.HeavyLaser: return 25;
                case SubsystemKind.Railgun:
                case SubsystemKind.Coilgun: return 10;
                case SubsystemKind.TachyonLance: return 5;
                default: return 40;
            }
        }

        /// <summary>
        /// Where a mount points at rest (U.SPC-T533): along its cone's axis, in the battle's frame; a turret whose cone is
        /// centred straight up or down off the hull (a dorsal or ventral turret) rests trained fore (aft for one facing
        /// aft) inside that cone instead of pointing at the sky.
        /// </summary>
        public static DVec3 RestAim(BattleUnit u, SubsystemDef d)
        {
            var axis = d.AxisOn(u.Combat?.Spec);
            if (!FireCones.IsFixed(d) && Math.Abs(axis.Y) > 0.7)
            {
                var along = d.FacesAft ? FireCones.Stern : FireCones.Bow;
                axis = FireCones.Clamp(along, axis, d.HalfDeg);
            }
            return u.LocalDirToWorld(axis).Normalized;
        }

        /// <summary>
        /// <paramref name="aim"/> (unit) brought within where the mount can point: a spinal railgun only within its gimbal of
        /// the keel, any other mount only within its fire cone (U.SPC-T533), swung back onto the cone's edge on the way to it.
        /// </summary>
        public static DVec3 Bound(BattleUnit u, SubsystemDef d, DVec3 aim)
        {
            // A spinal mount, whatever gun is in it, swings only within the same gimbal (U.SPC-T490).
            if (SpinalMounts.HullLaid(d)) return Gunnery.Gimbal(aim, RestAim(u, d), Gunnery.RailgunGimbalDeg);
            if (d.BearsEverywhere) return aim;
            var axis = u.LocalDirToWorld(d.AxisOn(u.Combat?.Spec));
            return FireCones.Clamp(aim, axis, d.HalfDeg);
        }

        public EmbodimentPose PoseAt(double jd)
        {
            var aim = Mount.Aim.LengthSquared > 1e-12 ? Mount.Aim.Normalized : RestAim(Unit, Mount.Def);
            Unit.Basis(out _, out var up);
            var roof = up - aim * DVec3.Dot(up, aim);
            return new EmbodimentPose(Battle.Body, Unit.PointOf(Mount.Def) + Battle.Anchor, aim, roof.LengthSquared > 1e-12 ? roof.Normalized : PilotState.LevelUp(aim));
        }

        /// <summary>The crew stands aside: the mount is the player's, still pointing where they left it.</summary>
        public void TakeCharge()
        {
            var aim = Mount.Aim.LengthSquared > 1e-12 ? Mount.Aim.Normalized : RestAim(Unit, Mount.Def);
            Battle.Manned[Mount] = new MountControl { Aim = aim };
        }

        public void Control(in EmbodimentInput input, double dt)
        {
            if (!Battle.Manned.TryGetValue(Mount, out var m)) return;
            if (input.Aim.LengthSquared > 1e-12) m.Aim = input.Aim.Normalized;
            m.Trigger = input.Primary;
        }

        /// <summary>The crew takes the mount back and picks its own targets again.</summary>
        public void HandBack() => Battle.Manned.Remove(Mount);

        public override bool Equals(object obj) => obj is MountedTurret t && t.Mount == Mount && t.Battle == Battle;
        public override int GetHashCode() => Mount.GetHashCode();
    }
}
