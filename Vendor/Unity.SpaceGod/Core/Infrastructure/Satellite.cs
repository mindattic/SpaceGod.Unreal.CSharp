using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>The player's hand on a satellite while it is embodied (see <see cref="SatelliteSeat"/>), in world terms.</summary>
    public struct SatelliteCommand
    {
        /// <summary>Where the player wants the boresight (unit, ecliptic axes); zero holds the current pointing.</summary>
        public DVec3 Aim;

        /// <summary>Thrust along the boresight, to its right and along its roof, each -1..1, and roll about the boresight.</summary>
        public double Throttle, StrafeRight, StrafeUp, Roll;
    }

    /// <summary>
    /// One satellite of a <see cref="Constellation"/> flying on its own state (U.SPC-T448): a position and velocity about its
    /// body (km, km/s, ecliptic axes) under the body's gravity, and an attitude (the boresight its sensors and relay antenna
    /// look down, and its roof). A constellation's satellites sit exactly in their Walker slots until one is taken or
    /// knocked off; only then does it carry a state of its own, stepped here.
    /// <para>
    /// Held by the player (<see cref="Held"/>), it turns toward the player's aim at <see cref="SlewDegS"/> and its thrusters
    /// push it along its own axes at up to <see cref="ThrustKmS2"/>; nothing else acts on it but gravity. Handed back, its
    /// station-keeping AI flies it home to its slot from wherever it was left: a closing speed it can still brake from,
    /// trimmed to the slot's orbit, and a slew back to its working attitude (boresight on the ground below, roof along
    /// its track). Back on station, it is placed in its slot again and the constellation lets the state go.
    /// </para>
    /// </summary>
    public sealed class Satellite
    {
        /// <summary>Top thrust of its station-keeping engines, km/s² (2 m/s², a game-scale figure, not a real ion drive's).</summary>
        public const double ThrustKmS2 = 0.002;

        /// <summary>How fast it turns its boresight and rolls, deg/s.</summary>
        public const double SlewDegS = 20, RollDegS = 30;

        /// <summary>Half-width of its sensor field and relay beam about the boresight, degrees.</summary>
        public const double BeamHalfDeg = 15;

        /// <summary>Longest integration step, seconds.</summary>
        public const double MaxStepSeconds = 1;

        /// <summary>A gap longer than this (s) with nobody holding it: its AI has long since put it back on station.</summary>
        public const double MaxCatchUpSeconds = 20000;

        /// <summary>Station-keeping: the closing-speed ramp near the slot (1/s) and how hard it chases that speed (1/s).</summary>
        const double ClosingRate = 0.02, ChaseGain = 0.1;

        /// <summary>Within these of its slot (km, km/s, degrees) it is on station.</summary>
        const double StationKm = 0.01, StationKmS = 1e-3, StationDeg = 1;

        public Constellation Constellation { get; }
        public BodyDefinition Body { get; }

        /// <summary>Its place in the constellation (the order of <see cref="Constellation.Offsets"/>).</summary>
        public int Index { get; }

        /// <summary>The date its state is at.</summary>
        public double Jd { get; private set; }

        /// <summary>From the body's centre, km, and its velocity there, km/s (ecliptic axes, not turning with the body).</summary>
        public DVec3 OffsetKm, VelocityKmS;

        /// <summary>Unit boresight (where its sensors and relay antenna point) and roof.</summary>
        public DVec3 Forward, Up;

        bool held;

        /// <summary>Whether the player holds it (its AI stands aside). Taking or handing it back puts it off station until
        /// its AI has it settled in its slot again.</summary>
        public bool Held
        {
            get => held;
            set
            {
                held = value;
                OnStation = false;
            }
        }

        /// <summary>What the player last asked of it, while <see cref="Held"/>.</summary>
        public SatelliteCommand Command;

        /// <summary>The thrust it put out on its last step, km/s².</summary>
        public double LastThrustKmS2 { get; private set; }

        /// <summary>Back in its slot at its working attitude, with nobody holding it.</summary>
        public bool OnStation { get; private set; }

        public Satellite(Constellation constellation, BodyDefinition body, int index, double jd)
        {
            Constellation = constellation;
            Body = body;
            Index = index;
            ReturnToSlot(jd);
        }

        public string Name => Constellation.NameOf(Index);

        /// <summary>Unit vector to its right (boresight × roof).</summary>
        public DVec3 Right => DVec3.Cross(Forward, Up);

        /// <summary>How far it is from its slot at its state's date, km.</summary>
        public double SlotErrorKm
        {
            get
            {
                Constellation.SlotAt(Body, Index, Jd, out var slot, out _);
                return (OffsetKm - slot).Length;
            }
        }

        /// <summary>
        /// The working attitude at a place in orbit: boresight straight down at the body, roof along the track
        /// (the velocity, squared off to the boresight).
        /// </summary>
        public static void WorkingAttitude(DVec3 offsetKm, DVec3 velocityKmS, out DVec3 forward, out DVec3 up)
        {
            forward = offsetKm.LengthSquared > 1e-18 ? -offsetKm.Normalized : -DVec3.UnitX;
            var along = velocityKmS - forward * DVec3.Dot(velocityKmS, forward);
            up = along.LengthSquared > 1e-18 ? along.Normalized : PilotState.LevelUp(forward);
        }

        /// <summary>Puts it in its slot at <paramref name="jd"/>, at its working attitude.</summary>
        public void ReturnToSlot(double jd)
        {
            Jd = jd;
            Constellation.SlotAt(Body, Index, jd, out OffsetKm, out VelocityKmS);
            WorkingAttitude(OffsetKm, VelocityKmS, out Forward, out Up);
            LastThrustKmS2 = 0;
            OnStation = !Held;
        }

        /// <summary>
        /// Steps it on to <paramref name="jd"/>: under the player's command while <see cref="Held"/>, under its
        /// station-keeping AI otherwise. A date at or before its own leaves it as it is.
        /// </summary>
        public void AdvanceTo(double jd)
        {
            if (!(jd > Jd)) return;
            if (OnStation && !Held)
            {
                ReturnToSlot(jd);
                return;
            }
            double total = (jd - Jd) * Astro.SecondsPerDay;
            if (!Held && total > MaxCatchUpSeconds)
            {
                ReturnToSlot(jd);
                return;
            }
            int steps = (int)Math.Ceiling(total / MaxStepSeconds);
            // A held satellite is flown at the player's pace (real time): a long gap is only ever a hitch, taken in coarser steps.
            steps = Math.Max(1, Math.Min(steps, (int)(MaxCatchUpSeconds / MaxStepSeconds)));
            double h = total / steps;
            double start = Jd;
            for (int i = 0; i < steps; i++)
            {
                Step(start + i * h / Astro.SecondsPerDay, h);
                if (!Held && Settled(start + (i + 1) * h / Astro.SecondsPerDay))
                {
                    ReturnToSlot(jd);
                    return;
                }
            }
            Jd = jd;
        }

        DVec3 Gravity(DVec3 r)
        {
            double r2 = r.LengthSquared;
            if (r2 < 1e-12) return DVec3.Zero;
            return r * (-Body.Gm / (r2 * Math.Sqrt(r2)));
        }

        /// <summary>One step of <paramref name="h"/> seconds from <paramref name="jd"/> (semi-implicit Euler).</summary>
        void Step(double jd, double h)
        {
            var g = Gravity(OffsetKm);
            DVec3 thrust;
            if (Held)
            {
                var c = Command;
                if (c.Aim.LengthSquared > 1e-12) Slew(c.Aim.Normalized, h);
                Roll(Clamp(c.Roll) * RollDegS * Astro.Deg2Rad * h);
                thrust = (Forward * Clamp(c.Throttle) + Right * Clamp(c.StrafeRight) + Up * Clamp(c.StrafeUp)) * ThrustKmS2;
                if (thrust.Length > ThrustKmS2) thrust = thrust.Normalized * ThrustKmS2;
            }
            else
            {
                thrust = KeepStation(jd, g);
                WorkingAttitude(OffsetKm, VelocityKmS, out var f, out var u);
                Slew(f, h);
                RollToward(u, h);
            }
            LastThrustKmS2 = thrust.Length;
            VelocityKmS += (g + thrust) * h;
            OffsetKm += VelocityKmS * h;
        }

        /// <summary>
        /// The station-keeping thrust: a trim that gives it the slot's own acceleration (so it moves with the slot rather
        /// than on an orbit of its own), and up to <see cref="ThrustKmS2"/> more to close on the slot at a speed it can brake
        /// from, slowing as it comes in.
        /// </summary>
        DVec3 KeepStation(double jd, DVec3 gravity)
        {
            Constellation.SlotAt(Body, Index, jd, out var slot, out var slotVelocity);
            var trim = Gravity(slot) - gravity;
            var error = OffsetKm - slot;
            double distance = error.Length;
            var wanted = slotVelocity;
            if (distance > 1e-12)
                wanted -= error / distance * Math.Min(Math.Sqrt(ThrustKmS2 * distance), ClosingRate * distance);
            var chase = (wanted - VelocityKmS) * ChaseGain;
            if (chase.Length > ThrustKmS2) chase = chase.Normalized * ThrustKmS2;
            return trim + chase;
        }

        bool Settled(double jd)
        {
            Constellation.SlotAt(Body, Index, jd, out var slot, out var slotVelocity);
            if ((OffsetKm - slot).Length > StationKm || (VelocityKmS - slotVelocity).Length > StationKmS) return false;
            WorkingAttitude(slot, slotVelocity, out var f, out var u);
            return AngleDeg(Forward, f) < StationDeg && AngleDeg(Up, u) < StationDeg;
        }

        /// <summary>Turns the boresight toward <paramref name="aim"/> (unit) by at most its slew rate, carrying the roof with it.</summary>
        void Slew(DVec3 aim, double h)
        {
            double angle = AngleDeg(Forward, aim);
            if (angle < 1e-9) return;
            double t = Math.Min(1, SlewDegS * h / angle);
            Forward = DVec3.TurnStep(Forward, aim, t);
            var roof = Up - Forward * DVec3.Dot(Up, Forward);
            Up = roof.LengthSquared > 1e-12 ? roof.Normalized : PilotState.LevelUp(Forward);
        }

        void Roll(double radians)
        {
            if (Math.Abs(radians) < 1e-15) return;
            Up = PilotFlight.RotateAbout(Up, Forward, radians).Normalized;
        }

        /// <summary>Rolls the roof toward <paramref name="up"/> (squared off to the boresight) at no more than its roll rate.</summary>
        void RollToward(DVec3 up, double h)
        {
            var target = up - Forward * DVec3.Dot(up, Forward);
            if (target.LengthSquared < 1e-12) return;
            target = target.Normalized;
            double angle = Math.Atan2(DVec3.Dot(DVec3.Cross(Up, target), Forward), DVec3.Dot(Up, target));
            double max = RollDegS * Astro.Deg2Rad * h;
            Roll(Math.Max(-max, Math.Min(max, angle)));
        }

        /// <summary>
        /// Whether a point (km from the body's centre) lies in its sensor field and relay beam: within
        /// <see cref="BeamHalfDeg"/> of the boresight and not hidden behind the body.
        /// </summary>
        public bool Covers(DVec3 pointKm)
        {
            var to = pointKm - OffsetKm;
            double d = to.Length;
            if (d < 1e-9) return true;
            if (AngleDeg(Forward, to / d) > BeamHalfDeg) return false;
            // The closest the line of sight comes to the body's centre, short of the point itself.
            double along = Math.Max(0, Math.Min(d, -DVec3.Dot(OffsetKm, to / d)));
            var nearest = OffsetKm + to / d * along;
            return nearest.Length >= Body.RadiusKm * 0.999;
        }

        static double Clamp(double x) => Math.Max(-1, Math.Min(1, x));

        static double AngleDeg(DVec3 a, DVec3 b) => Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(a, b)))) * Astro.Rad2Deg;

        public override string ToString() => Name;
    }
}
