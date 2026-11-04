using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// One satellite of a constellation as an <see cref="IEmbodiable"/> (U.SPC-T448). Taken, it leaves its Walker slot to
    /// fly on its own state (<see cref="Satellite"/>): the player turns and points it (its sensors and relay antenna look
    /// down its boresight) and nudges it about on its station-keeping thrusters. Handed back, its AI holds station again:
    /// it flies home to its slot from wherever it was left, settles back to its working attitude, and rejoins the cloud.
    /// The satellite is stepped by the world (<see cref="Infrastructure.UpdateSatellites"/>), so this only flips who holds it.
    /// </summary>
    public sealed class SatelliteSeat : IEmbodiable
    {
        readonly Func<double> now;

        public Constellation Constellation { get; }
        public BodyDefinition Body { get; }
        public int Index { get; }

        /// <param name="now">The world's date, for where it leaves its slot when taken.</param>
        public SatelliteSeat(Constellation constellation, BodyDefinition body, int index, Func<double> now)
        {
            Constellation = constellation;
            Body = body;
            Index = index;
            this.now = now;
        }

        /// <summary>Satellite <paramref name="index"/> of a constellation in <paramref name="world"/>'s system, on its clock.</summary>
        public static SatelliteSeat Of(World world, Constellation constellation, int index) =>
            new SatelliteSeat(constellation, constellation == null ? null : world.Catalog[constellation.Body], index, () => world.Jd);

        /// <summary>
        /// The constellation satellite nearest a point (km from <paramref name="body"/>'s centre) at a date, where it
        /// really is (in its slot, or flying on its own), or null if <paramref name="body"/> has none: what a picker
        /// offers for "the satellite there".
        /// </summary>
        public static SatelliteSeat Nearest(World world, int body, DVec3 offsetKm, double jd)
        {
            var infra = world?.Infrastructure;
            if (infra == null) return null;
            Constellation best = null;
            int bestIndex = -1;
            double bestKm2 = double.MaxValue;
            foreach (var c in infra.Constellations)
            {
                if (c.Body != body) continue;
                var points = new DVec3[c.Count];
                c.Offsets(world.Catalog[body], jd, points);
                for (int i = 0; i < points.Length; i++)
                {
                    double d = (points[i] - offsetKm).LengthSquared;
                    if (d >= bestKm2) continue;
                    bestKm2 = d;
                    best = c;
                    bestIndex = i;
                }
            }
            return best == null ? null : Of(world, best, bestIndex);
        }

        /// <summary>Its own state, while it flies on one (taken, or its AI still bringing it home); null while in its slot.</summary>
        public Satellite Satellite => Constellation != null && Constellation.Flying.TryGetValue(Index, out var s) ? s : null;

        public string Name => Constellation?.NameOf(Index) ?? "satellite";
        public EmbodimentKind Kind => EmbodimentKind.Satellite;

        /// <summary>The constellations are the real, unowned spacecraft: no faction.</summary>
        public Faction Faction => null;

        public IEmbodiable Host => null;
        public bool CanEmbody => Blocker == null;
        public bool Embodied => Satellite?.Held ?? false;

        public string Blocker
        {
            get
            {
                if (Constellation == null || Body == null || Index < 0 || Index >= Constellation.Count) return "nothing there";
                return null;
            }
        }

        public EmbodimentPose PoseAt(double jd)
        {
            var s = Satellite;
            if (s != null)
            {
                s.AdvanceTo(jd);
                return new EmbodimentPose(Body.Index, s.OffsetKm, s.Forward, s.Up);
            }
            Constellation.SlotAt(Body, Index, jd, out var offset, out var velocity);
            Satellite.WorkingAttitude(offset, velocity, out var f, out var u);
            return new EmbodimentPose(Body.Index, offset, f, u);
        }

        /// <summary>Its AI lets go: it leaves its slot where it is, still pointing where it was, and answers only the player.</summary>
        public void TakeCharge()
        {
            double jd = now?.Invoke() ?? double.NaN;
            if (double.IsNaN(jd)) jd = Astro.J2000;
            var s = Constellation.Detach(Body, Index, jd);
            s.AdvanceTo(jd);
            s.Held = true;
            s.Command = default;
        }

        /// <summary>Where to point it and how to push it, for the world's next step of it.</summary>
        public void Control(in EmbodimentInput input, double dt)
        {
            var s = Satellite;
            if (s == null || !s.Held) return;
            s.Command = new SatelliteCommand
            {
                Aim = input.Aim,
                Throttle = input.Throttle,
                StrafeRight = input.StrafeRight,
                StrafeUp = input.StrafeUp,
                Roll = input.Roll,
            };
        }

        /// <summary>Its station-keeping AI takes it back, from wherever and however the player left it.</summary>
        public void HandBack()
        {
            var s = Satellite;
            if (s == null) return;
            s.Held = false;
            s.Command = default;
        }

        public override bool Equals(object obj) => obj is SatelliteSeat t && t.Constellation == Constellation && t.Index == Index;
        public override int GetHashCode() => (Constellation?.GetHashCode() ?? 0) * 397 ^ Index;
    }
}
