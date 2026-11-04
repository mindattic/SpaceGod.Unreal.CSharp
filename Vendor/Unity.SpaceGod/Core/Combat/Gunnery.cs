using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Aimed fire (U.SPC-T270): the geometry that decides whether a shot actually lands, for the guns the
    /// player points and for every shot fired at the player's hull. A beam is a ray, a round a swept
    /// segment, and either hits only what it really passes through, so a crosshair laid on target hits
    /// and a hull jinking across a gunner's sights makes him miss.
    /// </summary>
    public static class Gunnery
    {
        /// <summary>How far off the nose a player's gun swings to meet the crosshair, deg; the railgun's
        /// long spinal mount barely moves.</summary>
        public const double GimbalDeg = 12, RailgunGimbalDeg = 5;

        /// <summary>The smallest a hull is to a shot, km: a fighter's own length is a few metres across.</summary>
        public const double MinHitRadiusKm = 0.012;

        /// <summary>Heat a player's guns build per point of damage fired, the share of full heat they shed
        /// each second, and how far they must cool once overheated before they fire again.</summary>
        public const double HeatPerDamage = 0.004, HeatCoolPerS = 0.2, HeatRecoverBelow = 0.35;

        /// <summary>A missile lock builds while the crosshair sits within <see cref="LockConeDeg"/> of the
        /// locked target, over <see cref="LockSeconds"/>; once made it holds while the target stays within
        /// <see cref="LockHoldDeg"/> of the nose.</summary>
        public const double LockConeDeg = 4, LockSeconds = 1.5, LockHoldDeg = 35;

        /// <summary>How long a gunner takes to read a change in a target's course, seconds (see
        /// <see cref="BattleUnit.SeenVel"/>): a steady course is tracked, a jink is aimed behind.</summary>
        public const double TrackingLagS = 0.35;

        /// <summary>The scatter of a gun of no accuracy at all, deg (a perfect one has none), and the
        /// scatter of the player's own guns by kind.</summary>
        public const double MaxScatterDeg = 1.5, AutocannonScatterDeg = 0.35, BeamScatterDeg = 0.05;

        /// <summary>How big a unit is to a shot.</summary>
        public static double HitRadius(BattleUnit u) => Math.Max(MinHitRadiusKm, u.Radius);

        /// <summary>
        /// How far along the ray from <paramref name="origin"/> down unit <paramref name="dir"/> it first
        /// meets the sphere of radius <paramref name="radius"/> about <paramref name="centre"/>, within
        /// <paramref name="maxT"/>; -1 if it never does. A ray starting inside the sphere meets it at once.
        /// </summary>
        public static double RaySphere(DVec3 origin, DVec3 dir, double maxT, DVec3 centre, double radius)
        {
            var oc = origin - centre;
            double b = DVec3.Dot(oc, dir);
            double c = oc.LengthSquared - radius * radius;
            if (c <= 0) return 0;
            if (b > 0) return -1;
            double disc = b * b - c;
            if (disc < 0) return -1;
            double t = -b - Math.Sqrt(disc);
            return t <= maxT ? t : -1;
        }

        /// <summary>
        /// When a shot at <paramref name="speed"/> (km/s, relative to the shooter) meets a target now at
        /// <paramref name="relPos"/> moving at <paramref name="relVel"/> (both relative to the shooter),
        /// seconds; NaN if it can never catch it.
        /// </summary>
        public static double InterceptTime(DVec3 relPos, DVec3 relVel, double speed)
        {
            // |relPos + relVel t| = speed t  =>  (v·v - s²) t² + 2 (p·v) t + p·p = 0
            double a = relVel.LengthSquared - speed * speed;
            double b = 2 * DVec3.Dot(relPos, relVel);
            double c = relPos.LengthSquared;
            if (Math.Abs(a) < 1e-12) return b < 0 ? -c / b : double.NaN;
            double disc = b * b - 4 * a * c;
            if (disc < 0) return double.NaN;
            double sq = Math.Sqrt(disc);
            double t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a);
            double t = Math.Min(t1, t2);
            if (t <= 0) t = Math.Max(t1, t2);
            return t > 0 ? t : double.NaN;
        }

        /// <summary>
        /// Where to aim so a shot at <paramref name="speed"/> from a shooter at <paramref name="shooterPos"/>
        /// moving at <paramref name="shooterVel"/> meets a target at <paramref name="targetPos"/> moving at
        /// <paramref name="targetVel"/>: the lead pip. The target itself when no shot can catch it.
        /// </summary>
        public static DVec3 LeadPoint(DVec3 shooterPos, DVec3 shooterVel, DVec3 targetPos, DVec3 targetVel, double speed)
        {
            var relVel = targetVel - shooterVel;
            double t = InterceptTime(targetPos - shooterPos, relVel, speed);
            return double.IsNaN(t) ? targetPos : targetPos + relVel * t;
        }

        /// <summary>The crosshair <paramref name="aim"/>, or as near it as a gun on the nose
        /// <paramref name="forward"/> can swing within <paramref name="gimbalDeg"/>.</summary>
        public static DVec3 Gimbal(DVec3 aim, DVec3 forward, double gimbalDeg) =>
            PilotFlight.ClampAim(aim, forward, gimbalDeg * Astro.Deg2Rad);

        /// <summary><paramref name="dir"/> (unit) knocked off line by a random angle of about
        /// <paramref name="sigmaRad"/>, in a random direction across it.</summary>
        public static DVec3 Scatter(DVec3 dir, double sigmaRad, Random rng)
        {
            if (sigmaRad <= 0) return dir;
            var across = PilotState.LevelUp(dir);
            across = PilotFlight.RotateAbout(across, dir, rng.NextDouble() * 2 * Math.PI);
            // A normal-ish spread (sum of uniforms), so most shots land near the line and a few stray far.
            double angle = (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) * 2 * sigmaRad;
            return PilotFlight.RotateAbout(dir, across, angle).Normalized;
        }
    }
}
