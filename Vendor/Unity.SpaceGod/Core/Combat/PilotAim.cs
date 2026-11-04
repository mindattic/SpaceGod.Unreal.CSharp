using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Turns the stick into where the hull should point, one way per <see cref="PilotControl"/>
    /// (U.SPC-T269). The result is <see cref="PilotInput.DesiredForward"/>; the hull then swings toward
    /// it at its own rate (<see cref="PilotFlight.Step"/>).
    /// </summary>
    public static class PilotAim
    {
        /// <summary>How far a free-aim crosshair may run ahead of the nose: anywhere in front of the hull,
        /// never behind it, so the camera always looks over the hull's shoulder rather than back at it.</summary>
        public const double FreeAimMaxLeadDeg = 90;

        /// <summary>How fast the free-aim view's roof follows the hull's own as it rolls, 1/s.</summary>
        const double ViewRollFollowHz = 6;

        /// <summary>
        /// Free aim (<see cref="PilotControl.FreeAim"/>): swings the crosshair <paramref name="aim"/>
        /// right by <paramref name="rightRad"/> and up by <paramref name="upRad"/> in the view's own frame
        /// (its roof <paramref name="viewUp"/>, which turns with it), so the mouse is screen-relative
        /// whatever the hull is doing. The roof then eases toward the hull's own
        /// (<paramref name="hullUp"/>) about the line of sight, so rolling the hull rolls the view, and
        /// the crosshair is held within <see cref="FreeAimMaxLeadDeg"/> of the nose.
        /// </summary>
        public static void Look(ref DVec3 aim, ref DVec3 viewUp, double rightRad, double upRad, DVec3 hullForward, DVec3 hullUp, double dt)
        {
            viewUp = Square(viewUp, aim, hullUp);
            if (rightRad != 0)
            {
                aim = PilotFlight.RotateAbout(aim, viewUp, -rightRad).Normalized;
            }
            if (upRad != 0)
            {
                var right = DVec3.Cross(aim, viewUp).Normalized;
                aim = PilotFlight.RotateAbout(aim, right, upRad).Normalized;
                viewUp = PilotFlight.RotateAbout(viewUp, right, upRad);
            }
            aim = PilotFlight.ClampAim(aim, hullForward, FreeAimMaxLeadDeg * Astro.Deg2Rad);
            viewUp = Square(viewUp, aim, hullUp);

            // The hull's roof seen square to the line of sight: well defined unless the hull is pitched
            // nearly across it, when the view keeps its own roof.
            var roof = hullUp - aim * DVec3.Dot(hullUp, aim);
            if (roof.LengthSquared > 0.25 && dt > 0)
            {
                double k = 1 - Math.Exp(-dt * ViewRollFollowHz);
                viewUp = Square(viewUp + (roof.Normalized - viewUp) * k, aim, hullUp);
            }
        }

        /// <summary>
        /// Nose steering (<see cref="PilotControl.DirectNose"/>, <see cref="PilotControl.Hauler"/>): the
        /// mouse moves the aim about the hull's own roof and starboard axes, and the aim never leads the
        /// nose by more than the hull can brake its swing in (<see cref="PilotFlight.AimLeadRad"/>), so the
        /// nose stops soon after the mouse does.
        /// </summary>
        public static DVec3 Nose(DVec3 aim, DVec3 hullForward, DVec3 hullUp, double rightRad, double upRad, double leadRad)
        {
            var right = DVec3.Cross(hullForward, hullUp).Normalized;
            aim = PilotFlight.RotateAbout(aim, hullUp, -rightRad);
            aim = PilotFlight.RotateAbout(aim, right, upRad).Normalized;
            return PilotFlight.ClampAim(aim, hullForward, leadRad);
        }

        /// <summary>
        /// The capital-ship helm (<see cref="PilotControl.Helm"/>): <paramref name="yaw"/> and
        /// <paramref name="pitch"/> (each -1..1, positive to starboard and up) ask the hull to turn at full
        /// rate while held, by setting the heading the brake angle (<paramref name="leadRad"/>) ahead of the
        /// nose; let go and the heading is the nose itself, so the swing stops as fast as the hull allows.
        /// </summary>
        public static DVec3 Helm(DVec3 hullForward, DVec3 hullUp, double yaw, double pitch, double leadRad)
        {
            yaw = Math.Max(-1, Math.Min(1, yaw));
            pitch = Math.Max(-1, Math.Min(1, pitch));
            double mag = Math.Sqrt(yaw * yaw + pitch * pitch);
            if (mag < 1e-9) return hullForward;
            var right = DVec3.Cross(hullForward, hullUp).Normalized;
            // Turn about the axis square to the asked direction (right·yaw + up·pitch), by the lead scaled to the stick.
            var toward = (right * yaw + hullUp * pitch) / mag;
            var axis = DVec3.Cross(hullForward, toward).Normalized;
            return PilotFlight.RotateAbout(hullForward, axis, leadRad * Math.Min(1, mag)).Normalized;
        }

        /// <summary><paramref name="up"/> made square to <paramref name="forward"/> (unit), falling back to
        /// <paramref name="fallback"/>'s and then the level roof when it lies along it.</summary>
        static DVec3 Square(DVec3 up, DVec3 forward, DVec3 fallback)
        {
            var u = up - forward * DVec3.Dot(up, forward);
            if (u.LengthSquared > 1e-6) return u.Normalized;
            u = fallback - forward * DVec3.Dot(fallback, forward);
            return u.LengthSquared > 1e-6 ? u.Normalized : PilotState.LevelUp(forward);
        }
    }
}
