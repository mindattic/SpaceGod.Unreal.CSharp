namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The flight a <see cref="ShipHelm"/> flies (U.SPC-T449): the hull's motion that carries from frame to frame
    /// (<see cref="State"/>), what the world around it does to it this frame (set by whoever hosts the helm before each
    /// <see cref="ShipHelm.Control"/>), and what the frame just flown did, for the HUD, the camera and the sensors.
    /// </summary>
    public sealed class HelmFlight
    {
        /// <summary>Where the hull is, how it is moving and turning, its cruise ramp and engine telegraph: in its frame
        /// body's frame, km and km/s, ecliptic axes.</summary>
        public PilotState State;

        /// <summary>The pull of every body near the hull this frame, km/s^2 (see <see cref="PilotFlight.Gravity"/>).</summary>
        public DVec3 Gravity;

        /// <summary>Flight assist switched off at the helm: the hull drifts on its momentum.</summary>
        public bool AssistOff;

        /// <summary>The drives are knocked out (an interdiction pulse, a hunter's EMP): no thrust, no turning, no boost or
        /// cruise; the hull drifts on what way it had.</summary>
        public bool DrivesDown;

        /// <summary>The hull is inside a port's approach, where a hauler's Ctrl is docking mode (U.SPC-T443).</summary>
        public bool DockingApproach;

        /// <summary>The model the last frame was flown by, the hull it was flown on, and the input it was given.</summary>
        public IFlightModel Model { get; internal set; }
        public HullDynamics Hull { get; internal set; }
        public PilotInput Input { get; internal set; }

        /// <summary>Where the hull was and how it was moving before the last frame was flown.</summary>
        public DVec3 PosBefore { get; internal set; }
        public DVec3 VelBefore { get; internal set; }

        /// <summary>The afterburner (or a capital rung to Flank) lit the last frame.</summary>
        public bool Boosting { get; internal set; }

        /// <summary>The thrusters changed the hull's velocity beyond what gravity did the last frame, so its drive shows
        /// on others' scanners (U.SPC-T66).</summary>
        public bool Burning { get; internal set; }

        /// <summary>Thrust, km/s^2, above which the drive shows as a burn (about 0.01 g).</summary>
        public const double BurnKmS2 = 1e-4;
    }
}
