namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// What kind of man-made thing an <see cref="IEmbodiable"/> is (U.SPC-T18), for display and grouping only:
    /// how control works is each implementation's own, and nothing branches on this to decide it.
    /// </summary>
    public enum EmbodimentKind
    {
        Ship,

        /// <summary>A launched fighter or bomber, one of a carrier's squadrons (U.SPC-T422).</summary>
        Fighter,
        Turret,
        Station,
        Satellite,
    }

    /// <summary>
    /// Where an embodied thing is and which way it faces: km from the centre of <see cref="FrameBody"/>
    /// (ecliptic axes), the same frame-body-plus-offset convention the pilot loop and battles use.
    /// </summary>
    public readonly struct EmbodimentPose
    {
        public readonly int FrameBody;
        public readonly DVec3 OffsetKm;

        /// <summary>Unit facing (a hull's nose, a turret's barrel) and roof.</summary>
        public readonly DVec3 Forward, Up;

        public EmbodimentPose(int frameBody, DVec3 offsetKm, DVec3 forward, DVec3 up)
        {
            FrameBody = frameBody;
            OffsetKm = offsetKm;
            Forward = forward;
            Up = up;
        }
    }

    /// <summary>
    /// One frame of the player's control, already in world terms (the Runtime input layer's job), for
    /// whatever is embodied. Each kind reads what it has: a turret its aim and trigger, a hull all of it.
    /// </summary>
    public struct EmbodimentInput
    {
        /// <summary>Where the player wants it pointed (unit, ecliptic axes); zero holds the current aim.</summary>
        public DVec3 Aim;

        /// <summary>Forward/reverse thrust, strafe and roll, each -1..1, for things that move.</summary>
        public double Throttle, StrafeRight, StrafeUp, Roll;

        /// <summary>The main trigger (guns) and the secondary (missiles), held this frame.</summary>
        public bool Primary, Secondary;

        /// <summary>The afterburner, held this frame, for things that move (U.SPC-T424).</summary>
        public bool Boost;

        /// <summary>A ship's helm (U.SPC-T449, <see cref="ShipHelm"/>): the cruise key held, Ctrl held (fine control, a
        /// hauler's docking mode in a port's approach), and the engine telegraph rung this frame (+1 ahead, -1 astern).</summary>
        public bool Cruise, Fine;
        public int TelegraphStep;
    }

    /// <summary>
    /// Anything man-made the player can take charge of (U.SPC-T18, see docs/EMBODIMENT.md): a ship's helm, a
    /// turret on a hull or station, a station, a satellite. The thing's own AI holds it whenever the player
    /// does not; <see cref="TakeCharge"/> and <see cref="HandBack"/> only change who holds it, so the AI picks
    /// up exactly where the player left off.
    /// </summary>
    public interface IEmbodiable
    {
        string Name { get; }
        EmbodimentKind Kind { get; }
        Faction Faction { get; }

        /// <summary>What it is mounted on (a turret's hull or station), or null for a free hull.</summary>
        IEmbodiable Host { get; }

        /// <summary>Whether the player can take it right now; when not, <see cref="Blocker"/> says why.</summary>
        bool CanEmbody { get; }
        string Blocker { get; }

        /// <summary>Whether the player holds it now.</summary>
        bool Embodied { get; }

        EmbodimentPose PoseAt(double jd);

        /// <summary>The player takes it: its AI lets go.</summary>
        void TakeCharge();

        /// <summary>One frame of the player's control, while <see cref="Embodied"/>.</summary>
        void Control(in EmbodimentInput input, double dt);

        /// <summary>Its AI takes it back, from wherever the player left it.</summary>
        void HandBack();
    }
}
