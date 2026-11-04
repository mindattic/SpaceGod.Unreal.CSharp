using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>A hyperlane between two planetary systems (each named by its planet, the body orbiting the Sun).</summary>
    public sealed class Lane
    {
        public int Id;
        public int A, B;
        public string Name;

        /// <summary>Sideways bow of the lane as a fraction of its length; lanes always bow away from the Sun.</summary>
        public double Bend;

        /// <summary>
        /// The nearest star (or barycentre) both ends orbit within: the Sun for every lane in Sol, a
        /// star for a lane inside its own system, the A-B barycentre for Alpha Centauri's long lanes.
        /// Lanes bow away from it.
        /// </summary>
        public int Hub;

        /// <summary>
        /// The jump gate a gate spur leads to (U.SPC-T379), or null for an ordinary lane. A spur runs from
        /// <see cref="A"/>, a planetary system on the network, to <see cref="B"/>, the system's root, where its
        /// far ramp sits right at the gate (see <see cref="Hyperlanes.GateSpurs"/>).
        /// </summary>
        public JumpGate Gate;

        /// <summary>Whether this is a spur to a jump gate rather than a lane between two planetary systems.</summary>
        public bool IsGateSpur => Gate != null;

        /// <summary>
        /// A heavy lane (U.SPC-T483): built wide and slow to carry what no ordinary lane will take, an ore tug pushing a
        /// captured asteroid or a block of comet ice of up to <see cref="Hyperlanes.HeavyLaneMaxTonnes"/>, at
        /// <see cref="Hyperlanes.HeavyLaneKmS"/>. Ordinary traffic rides it like any other lane.
        /// </summary>
        public bool Heavy;

        public int Other(int system) => system == A ? B : A;
    }

    /// <summary>One lane ridden in one direction.</summary>
    public readonly struct LaneHop
    {
        public readonly Lane Lane;
        public readonly int From, To;

        public LaneHop(Lane lane, int from, int to)
        {
            Lane = lane;
            From = from;
            To = to;
        }
    }

    /// <summary>
    /// The X3-style highway network. Each planetary system has an on-ramp for each of its lanes, a
    /// fixed distance out past its moons, facing the system at the lane's other end. A ship crosses
    /// to the ramp under its own drive, rides the lane at up to <see cref="CruiseKmS"/> (it eases in
    /// and out, arriving at rest relative to the far ramp), then crosses from the far ramp to its
    /// destination, again under its own drive. Lanes bow gently to one side and swing wide of the
    /// Sun. Within one planet's system (Earth to the Moon, Io to Callisto) ships just fly.
    ///
    /// Flown at the helm (U.SPC-T41, U.SPC-T379) a lane is an X3 Rebirth highway: a chain of rings
    /// (<see cref="RingCount"/>, about every <see cref="RingSpacingKm"/>) strung from ramp to ramp, which
    /// speed a rider up through the first half of the ride and brake it through the second
    /// (<see cref="Progress"/>). A pilot reaches a ramp under its own drive (thrusters, boost, then the
    /// cruise drive), flies in through its ring gate and rides the same <see cref="Fleet.JoinLane"/> path
    /// AI traffic does; there is no separate pilot-only lane. Each system's network also leads on to its
    /// jump gates by a <see cref="GateSpurs">gate spur</see> per gate, joined by flying through either end's
    /// ring, so a pilot can ride from a planet to a gate, jump, and pick up the far system's own network
    /// at the gate it comes out of.
    /// </summary>
    public sealed class Hyperlanes
    {
        public readonly List<Lane> Lanes = new List<Lane>();

        /// <summary>
        /// How dangerous each lane has been lately (ambushes), in days of extra cost when planning a
        /// route, so traffic shifts to safer lanes until it fades.
        /// </summary>
        public readonly Dictionary<Lane, double> Danger = new Dictionary<Lane, double>();

        /// <summary>Sets a lane's danger; planners see the change at once (cached routes are dropped).</summary>
        public void SetDanger(Lane lane, double value)
        {
            Danger[lane] = value;
            routeCache.Clear();
            routeTrees.Clear();
        }

        // ---------------------------------------------------------------- upkeep (U.SPC-T481)

        /// <summary>
        /// Each lane's condition (U.SPC-T481), 1 fully tended to 0 dark: how well its accelerator rings are powered and
        /// aligned. A lane not listed is in full repair. It runs down without tenders working it and is restored by them
        /// (see <see cref="LaneUpkeep"/>); as it falls the lane rides slower (<see cref="SpeedFactor"/>) and less reliably
        /// (<see cref="Reliability"/>), and at <see cref="LaneCondition.FailAt"/> it fails and carries nothing
        /// (<see cref="Carries"/>), so routes go round it. Gate spurs are not tended and never wear.
        /// </summary>
        public readonly Dictionary<Lane, double> Condition = new Dictionary<Lane, double>();

        /// <summary>The condition of <paramref name="lane"/> (see <see cref="Condition"/>), 1 when it has never worn.</summary>
        public double ConditionOf(Lane lane) => lane != null && Condition.TryGetValue(lane, out double c) ? c : 1;

        /// <summary>Sets a lane's condition (clamped to 0-1). Planners see a lane fail or come back at once (cached routes
        /// are dropped); a lane merely slowing is seen at the next half day, as its changing length is.</summary>
        public void SetCondition(Lane lane, double value)
        {
            if (lane == null) return;
            bool carried = Carries(lane);
            Condition[lane] = Math.Max(0, Math.Min(1, value));
            if (Carries(lane) != carried)
            {
                routeCache.Clear();
                routeTrees.Clear();
            }
        }

        /// <summary>Whether <paramref name="lane"/> still carries traffic: not failed (<see cref="LaneCondition.Carries"/>).</summary>
        public bool Carries(Lane lane) => lane != null && LaneCondition.Carries(ConditionOf(lane));

        /// <summary>The share of its full pace <paramref name="lane"/> rides at, for its condition (<see cref="LaneCondition.SpeedFactor"/>).</summary>
        public double SpeedFactor(Lane lane) => LaneCondition.SpeedFactor(ConditionOf(lane));

        /// <summary>The chance a ride of <paramref name="lane"/> goes through without a stall (<see cref="LaneCondition.Reliability"/>).</summary>
        public double Reliability(Lane lane) => LaneCondition.Reliability(ConditionOf(lane));

        /// <summary>Days to ride a whole <paramref name="lane"/> of this length at its condition's pace (<see cref="RideDays(double)"/>
        /// over <see cref="SpeedFactor"/>).</summary>
        public double RideDays(Lane lane, double lengthKm) => RideDays(lengthKm) / Math.Max(1e-3, SpeedFactor(lane));
        readonly BodyCatalog catalog;
        readonly Ephemeris ephemeris;

        /// <summary>Peak speed in a lane: 4% of light, so Earth to Mars takes hours, not weeks.</summary>
        public const double CruiseKmS = 12000;

        /// <summary>Lanes stay this far from the Sun; other stars scale it with the square root of their luminosity (equal heating).</summary>
        public const double SunKeepOutKm = 0.3 * Astro.AuKm;

        /// <summary>
        /// The longest a ride takes, days. A lane's peak speed is raised past <see cref="CruiseKmS"/>
        /// when it is that long: the 13,000 AU between Alpha Centauri A-B and Proxima is crossed in days
        /// (deep fiction), while in Sol only the longest outer lanes are sped up a little.
        /// </summary>
        public const double MaxRideDays = 5;

        public Hyperlanes(Ephemeris ephemeris)
        {
            this.ephemeris = ephemeris;
            catalog = ephemeris.Catalog;
        }

        public static Hyperlanes CreateDefault(Ephemeris ephemeris)
        {
            var h = new Hyperlanes(ephemeris);
            (string, string, double)[] links =
            {
                ("Mercury", "Venus", 0.06), ("Venus", "Earth", -0.05), ("Earth", "Mars", 0.07), ("Mars", "Ceres", -0.06),
                ("Ceres", "Jupiter", 0.05), ("Earth", "Ceres", -0.04), ("Mars", "Jupiter", 0.04), ("Jupiter", "Saturn", -0.05),
                ("Saturn", "Uranus", 0.05), ("Uranus", "Neptune", -0.06), ("Neptune", "Pluto", 0.07), ("Saturn", "Neptune", 0.03),
                // Out to the Greek camp's mines at Jupiter's L4, 60 degrees ahead along its orbit (the Trojan camp at
                // L5 has no lane: only the raiders hidden there go).
                ("Jupiter", "Hektor", 0.04), ("Hektor", "Eurybates", -0.05),
            };
            foreach (var (a, b, bend) in links) h.Add(a, b, bend);
            // The belt's freight road (U.SPC-T483): the Ceres-Jupiter lane is built heavy, so the Belt Consortium's tugs can
            // push ice and rock from the belt out to its depot at Callisto.
            h.MarkHeavy("Ceres", "Jupiter");
            return h;
        }

        // ---------------------------------------------------------------- heavy lanes (U.SPC-T483)

        /// <summary>How fast a heavy lane carries a tug and its load at its peak, km/s: a twentieth of an ordinary lane's cruise.</summary>
        public const double HeavyLaneKmS = 600;

        /// <summary>The biggest load a heavy lane will carry, tonnes: a rock a few hundred metres across.</summary>
        public const double HeavyLaneMaxTonnes = 150e6;

        /// <summary>Makes the lane between <paramref name="a"/> and <paramref name="b"/> a heavy lane; returns it, or null if there is none.</summary>
        public Lane MarkHeavy(string a, string b)
        {
            int ia = catalog[a].Index, ib = catalog[b].Index;
            foreach (var lane in Lanes)
                if ((lane.A == ia && lane.B == ib) || (lane.A == ib && lane.B == ia))
                {
                    lane.Heavy = true;
                    return lane;
                }
            return null;
        }

        /// <summary>Days to ride a whole heavy lane of this length with a load, eased in and out like any ride but at <see cref="HeavyLaneKmS"/>.</summary>
        public static double HeavyRideDays(double lengthKm) => 2 * Math.Max(0, lengthKm) / HeavyLaneKmS / Astro.SecondsPerDay;

        /// <summary>
        /// Days <paramref name="ship"/> takes to ride <paramref name="lengthKm"/> of <paramref name="lane"/>: an ore tug with a
        /// load latched on (U.SPC-T498, <see cref="Ship.Tow"/>) rides a heavy lane at its heavy pace (<see cref="HeavyRideDays(double)"/>);
        /// everything else at the lane's own. A worn lane is slower for both.
        /// </summary>
        public double RideDays(Ship ship, Lane lane, double lengthKm) =>
            ship != null && ship.Tow != null && lane.Heavy ? HeavyRideDays(lengthKm) / Math.Max(1e-3, SpeedFactor(lane)) : RideDays(lane, lengthKm);

        /// <summary>
        /// The lanes <paramref name="ship"/> rides from one planetary system to another: an ore tug with a load latched on
        /// (U.SPC-T498) only heavy lanes (<see cref="HeavyRoute"/>, null where none join them: it pushes through open space),
        /// everyone else the quickest route over any (<see cref="Route"/>).
        /// </summary>
        public List<LaneHop> RouteFor(Ship ship, int fromSystem, int toSystem, double jd) =>
            ship != null && ship.Tow != null ? HeavyRoute(fromSystem, toSystem, jd) : Route(fromSystem, toSystem, jd);

        /// <summary>
        /// The quickest route between two systems over heavy lanes alone (what a tug with a big load can take), or null
        /// when they are not joined by heavy lanes (or are the same system).
        /// </summary>
        public List<LaneHop> HeavyRoute(int fromSystem, int toSystem, double jd) =>
            fromSystem == toSystem ? null : FindRoute(fromSystem, toSystem, jd, heavyOnly: true);

        /// <summary>
        /// Alpha Centauri's network: short lanes inside each star's system, and two long trunk lanes
        /// across the 13,000 AU between the A-B pair and Proxima (the only practical way across).
        /// </summary>
        public static Hyperlanes CreateAlphaCentauri(Ephemeris ephemeris)
        {
            var h = new Hyperlanes(ephemeris);
            (string, string, double)[] links =
            {
                ("Rigil Gate", "Alpha Centauri Ab", 0.06), ("Rigil Gate", "Toliman Forge", -0.05),
                ("Toliman Forge", "Proxima b", 0.02), ("Rigil Gate", "Proxima b", -0.02),
                ("Proxima b", "Proxima c", 0.05), ("Proxima b", "Proxima d", -0.06), ("Proxima b", "Blackhollow", 0.07),
            };
            foreach (var (a, b, bend) in links) h.Add(a, b, bend);
            return h;
        }

        // ---------------------------------------------------------------- the ring chain (U.SPC-T379)

        /// <summary>How far apart the rings strung along a lane are, km: a few dozen between Earth and Mars.</summary>
        public const double RingSpacingKm = 2e6;

        /// <summary>The fewest and most rings strung along any one lane.</summary>
        public const int MinRings = 6, MaxRings = 240;

        /// <summary>How many rings are strung along a lane of this length (between its two ramps' ring gates).</summary>
        public static int RingCount(double lengthKm) =>
            Math.Max(MinRings, Math.Min(MaxRings, (int)Math.Round(Math.Max(0, lengthKm) / RingSpacingKm)));

        /// <summary>
        /// Where ring <paramref name="k"/> (0 nearest whichever ramp the curve is measured from) of
        /// <paramref name="count"/> sits, as a fraction along the lane: evenly spaced strictly between the ramps,
        /// so the chain reads the same ridden either way.
        /// </summary>
        public static double RingFraction(int k, int count) => (k + 1.0) / (count + 1.0);

        /// <summary>How many of a lane's <paramref name="count"/> rings a rider <paramref name="progress"/> of the way along has flown through.</summary>
        public static int RingsPassed(double progress, int count) =>
            Math.Max(0, Math.Min(count, (int)Math.Floor(Math.Max(0, progress) * (count + 1) + 1e-9)));

        /// <summary>Whether the rings are speeding a rider up (the first half of its ride, by time) rather than braking it for the far ramp.</summary>
        public static bool RingsAccelerate(double timeFraction) => timeFraction < 0.5;

        // ---------------------------------------------------------------- gate spurs (U.SPC-T379)

        /// <summary>
        /// The spurs that lead the network on to the system's jump gates, one per gate (<see cref="AddGateSpur"/>).
        /// Kept apart from <see cref="Lanes"/>: AI traffic's routes, tolls, law and raiders work the lanes between
        /// planetary systems and reach the gates by their own in-system legs (see <see cref="InterstellarTraffic"/>),
        /// so a spur is for the helm, ridden by the same <see cref="Fleet.JoinLane"/> as any lane.
        /// </summary>
        public readonly List<Lane> GateSpurs = new List<Lane>();

        /// <summary>Spur ids start here, clear of the lanes' own (the length cache is keyed by id).</summary>
        public const int GateSpurIdBase = 100000;

        /// <summary>
        /// How far in front of its gate (toward the network) a spur's far ramp sits, km: inside the gate's
        /// <see cref="JumpGate.ActivationRangeKm"/>, so a ride to the gate ends where the gate can be opened.
        /// </summary>
        public const double GateRampInsetKm = 200;

        /// <summary>Where a gate stands, in this system's coordinates (relative to its root, as <see cref="StarSystem.GateInRange"/> takes them).</summary>
        public static DVec3 GateHelio(JumpGate gate) => gate.PositionKm;

        /// <summary>
        /// Strings a spur from the network to <paramref name="gate"/>: from whichever planetary system on a lane
        /// lies nearest the gate at <paramref name="jd"/>, to a ramp just in front of the gate. Null when the system
        /// has no lanes to lead on from (a frontier system, a Void Base). Adding the same gate again returns its spur.
        /// </summary>
        public Lane AddGateSpur(JumpGate gate, double jd)
        {
            if (gate == null || Lanes.Count == 0) return null;
            foreach (var spur in GateSpurs)
                if (spur.Gate == gate) return spur;
            var at = GateHelio(gate);
            int best = -1;
            double bestKm = double.MaxValue;
            foreach (var lane in Lanes)
                for (int end = 0; end < 2; end++)
                {
                    int system = end == 0 ? lane.A : lane.B;
                    double km = (ephemeris.HelioAt(system, jd) - at).Length;
                    if (km >= bestKm) continue;
                    bestKm = km;
                    best = system;
                }
            if (best < 0) return null;
            int root = catalog.Root.Index;
            var made = new Lane
            {
                Id = GateSpurIdBase + GateSpurs.Count,
                A = best,
                B = root,
                Bend = GateSpurs.Count % 2 == 0 ? 0.03 : -0.03,
                Name = catalog[best].Name + "–" + gate.Name,
                Hub = root,
                Gate = gate,
            };
            GateSpurs.Add(made);
            return made;
        }

        /// <summary>What a lane's end at <paramref name="at"/> is called: its planetary system, or a spur's gate.</summary>
        public string EndName(Lane lane, int at) => lane.Gate != null && at == lane.B ? lane.Gate.Name : catalog[at].Name;

        /// <summary>Where a lane's end at <paramref name="at"/> is anchored: its planet, or for a spur's far end, its gate.</summary>
        DVec3 AnchorHelio(Lane lane, int at, double jd) =>
            lane.Gate != null && at == lane.B ? GateHelio(lane.Gate) : ephemeris.HelioAt(at, jd);

        /// <summary>
        /// The gate spur whose ring (at either end) the segment from <paramref name="fromHelio"/> to
        /// <paramref name="toHelio"/> (true km) flies through, heading along the spur, as the hop that rides it away
        /// from that end, or null. A spur is joined by flying through its ring the way it runs, not by being
        /// near it, so a hull just come out of the gate, or brought off at the ramp, is not swept back in.
        /// </summary>
        public LaneHop? GateSpurRingCrossed(DVec3 fromHelio, DVec3 toHelio, double jd)
        {
            foreach (var spur in GateSpurs)
                for (int end = 0; end < 2; end++)
                {
                    int at = end == 0 ? spur.A : spur.B;
                    var centre = RampHelio(spur, at, jd);
                    var n = RampDirection(spur, at, jd);
                    double d0 = DVec3.Dot(fromHelio - centre, n), d1 = DVec3.Dot(toHelio - centre, n);
                    // d0 must be clearly behind the ring: a path lying in its plane is not a crossing, and float noise at
                    // heliocentric magnitudes (~1e-7 km) must not read as one.
                    if (!(d0 < -1e-4 && d1 >= 0)) continue;
                    var hit = fromHelio + (toHelio - fromHelio) * (d0 / (d0 - d1));
                    if ((hit - centre).Length <= GateRadiusKm) return new LaneHop(spur, at, spur.Other(at));
                }
            return null;
        }

        public Lane Add(string a, string b, double bend)
        {
            var lane = new Lane { Id = Lanes.Count, A = catalog[a].Index, B = catalog[b].Index, Bend = bend, Name = a + "–" + b };
            lane.Hub = CommonStar(lane.A, lane.B);
            Lanes.Add(lane);
            return lane;
        }

        /// <summary>The nearest star or barycentre that both bodies orbit within.</summary>
        int CommonStar(int a, int b)
        {
            for (int x = catalog[a].ParentIndex; x >= 0; x = catalog[x].ParentIndex)
            {
                if (!catalog[x].UsesStellarMapping) continue;
                for (int y = catalog[b].ParentIndex; y >= 0; y = catalog[y].ParentIndex)
                    if (x == y) return x;
            }
            return 0;
        }

        /// <summary>
        /// The planetary system a body belongs to: the body itself, or the planet it orbits. Anything
        /// orbiting a star directly (a planet, a station) is a system of its own.
        /// </summary>
        public int SystemOf(int body)
        {
            while (body > 0 && !catalog[catalog[body].ParentIndex].UsesStellarMapping) body = catalog[body].ParentIndex;
            return body;
        }

        /// <summary>How close lanes may pass a star (km): <see cref="SunKeepOutKm"/> scaled for its luminosity.</summary>
        public double KeepOutKm(int star) => SunKeepOutKm * Math.Sqrt(Math.Max(catalog[star].LuminositySolar, 1e-6));

        /// <summary>
        /// The star a lane between two points (true km) must swing round: the one the straight line
        /// passes closest to, measured in its own keep-out radii. In Sol it is always the Sun.
        /// </summary>
        public int KeepOutStar(DVec3 aHelio, DVec3 bHelio, double jd)
        {
            int best = 0;
            double bestRatio = double.MaxValue;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (!catalog[i].IsStar) continue;
                var c = ephemeris.HelioAt(i, jd);
                var chord = bHelio - aHelio;
                double t = Math.Max(0, Math.Min(1, DVec3.Dot(c - aHelio, chord) / Math.Max(chord.LengthSquared, 1e-12)));
                double ratio = (aHelio + chord * t - c).Length / KeepOutKm(i);
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// A point on a lane in true km, between ramp points <paramref name="a"/> and <paramref name="b"/>
        /// (heliocentric). A ship riding it passes the star and shape fixed when it joined
        /// (<paramref name="star"/>, <paramref name="shape"/>), so its curve cannot flip mid-ride
        /// as the ramps move; -1 decides them now.
        /// </summary>
        public DVec3 CurveKm(Lane lane, DVec3 a, DVec3 b, double s, double jd, int star = -1, int shape = -1)
        {
            if (star < 0) star = KeepOutStar(a, b, jd);
            return Curve(a, b, lane.Bend, ephemeris.HelioAt(lane.Hub, jd), ephemeris.HelioAt(star, jd), KeepOutKm(star), s, DVec3.UnitZ, shape);
        }

        public double CurveLengthKm(Lane lane, DVec3 a, DVec3 b, double jd, int star = -1, int shape = -1)
        {
            if (star < 0) star = KeepOutStar(a, b, jd);
            return CurveLength(a, b, lane.Bend, ephemeris.HelioAt(lane.Hub, jd), ephemeris.HelioAt(star, jd), KeepOutKm(star), DVec3.UnitZ, shape);
        }

        /// <summary>Which shape the curve takes between these points now: 0 bowed, 1 swung round the star.</summary>
        public int ShapeOf(Lane lane, DVec3 a, DVec3 b, int star, double jd) =>
            Detours(a, b, ephemeris.HelioAt(star, jd), KeepOutKm(star)) ? 1 : 0;

        static bool Detours(DVec3 a, DVec3 b, DVec3 keepOutCentre, double keepOut)
        {
            var chord = b - a;
            var ac = a - keepOutCentre;
            double t0 = Math.Max(0, Math.Min(1, -DVec3.Dot(ac, chord) / Math.Max(chord.LengthSquared, 1e-12)));
            return !((ac + chord * t0).Length > keepOut * 1.5 || t0 <= 0 || t0 >= 1);
        }

        /// <summary>How far out a system's ramps sit: past its moons, inside the planet's drawn neighbourhood.</summary>
        public double RampRadiusKm(int system)
        {
            var p = catalog[system];
            double wanted = Math.Max(p.MoonReachKm * 1.1, p.RadiusKm * 6);
            return Math.Min(wanted, ScaleModel.NeighbourhoodKm(p) * 0.9);
        }

        /// <summary>The ramp's direction from its planet: toward the system at the lane's other end.</summary>
        public DVec3 RampDirection(Lane lane, int at, double jd) =>
            (AnchorHelio(lane, lane.Other(at), jd) - AnchorHelio(lane, at, jd)).Normalized;

        /// <summary>
        /// The ramp's offset from body <paramref name="at"/>: out past its moons toward the lane's other end, or for a
        /// gate spur's far end, just in front of its gate (<see cref="GateRampInsetKm"/>).
        /// </summary>
        public DVec3 RampOffset(Lane lane, int at, double jd) =>
            lane.Gate != null && at == lane.B
                ? GateHelio(lane.Gate) + RampDirection(lane, at, jd) * GateRampInsetKm - ephemeris.HelioAt(at, jd)
                : RampDirection(lane, at, jd) * RampRadiusKm(at);

        public DVec3 RampHelio(Lane lane, int at, double jd) => ephemeris.HelioAt(at, jd) + RampOffset(lane, at, jd);

        /// <summary>
        /// Radius of the ring gate at each ramp, km (U.SPC-T41): a hull flown in through it joins the lane.
        /// AI traffic burns to the ramp's centre; a pilot only has to thread the ring.
        /// </summary>
        public const double GateRadiusKm = 1500;

        /// <summary>
        /// The on-ramp whose ring gate a point (true heliocentric km) is inside, as the hop that rides
        /// its lane away from that ramp's system, or null. The nearest gate wins where two overlap.
        /// </summary>
        public LaneHop? OnRampAt(DVec3 helioKm, double jd)
        {
            LaneHop? best = null;
            double bestKm = GateRadiusKm;
            foreach (var lane in Lanes)
                for (int end = 0; end < 2; end++)
                {
                    // A failed lane's ring gates are dark (U.SPC-T481): flying through one joins nothing.
                    if (!Carries(lane)) break;
                    int at = end == 0 ? lane.A : lane.B;
                    double km = (helioKm - RampHelio(lane, at, jd)).Length;
                    if (km > bestKm) continue;
                    bestKm = km;
                    best = new LaneHop(lane, at, lane.Other(at));
                }
            return best;
        }

        /// <summary>
        /// The lanes a rider coming off at <paramref name="system"/>'s ramp could carry on along (the
        /// off-ramp's exit choice), never straight back down <paramref name="cameIn"/>: the lanes on to other
        /// planetary systems, then the spurs on to the system's jump gates. A spur's far end, at its gate,
        /// leads on to nothing but the gate. A failed lane (U.SPC-T481) is not offered.
        /// </summary>
        public List<LaneHop> OnwardHops(int system, Lane cameIn)
        {
            var hops = new List<LaneHop>();
            if (cameIn != null && cameIn.Gate != null && system == cameIn.B) return hops;
            foreach (var lane in Lanes)
                if (lane != cameIn && (lane.A == system || lane.B == system) && Carries(lane)) hops.Add(new LaneHop(lane, system, lane.Other(system)));
            foreach (var spur in GateSpurs)
                if (spur != cameIn && spur.A == system) hops.Add(new LaneHop(spur, system, spur.B));
            return hops;
        }

        /// <summary>
        /// A point along a lane from <paramref name="a"/> to <paramref name="b"/> (s from 0 to 1): a
        /// quadratic curve bowed outward by <paramref name="bend"/> and pushed out to stay
        /// <paramref name="keepOut"/> from the origin (the Sun). Works in true km or scene units alike.
        /// </summary>
        public static DVec3 Curve(DVec3 a, DVec3 b, double bend, double keepOut, double s, DVec3 up) =>
            Curve(a, b, bend, DVec3.Zero, DVec3.Zero, keepOut, s, up);

        /// <summary>
        /// The general lane curve: bowed away from <paramref name="hub"/> (the star or barycentre the
        /// lane belongs to), and swung round <paramref name="keepOutCentre"/> (the star the line passes
        /// nearest) at <paramref name="keepOut"/>. In Sol both are the Sun at the origin.
        /// </summary>
        public static DVec3 Curve(DVec3 a, DVec3 b, double bend, DVec3 hub, DVec3 keepOutCentre, double keepOut, double s, DVec3 up, int shape = -1)
        {
            var chord = b - a;
            double length = chord.Length;
            var dir = length > 0 ? chord / length : DVec3.UnitX;
            var mid = (a + b) * 0.5;
            var side = DVec3.Cross(chord, up);
            side = side.LengthSquared > 0 ? side.Normalized : DVec3.UnitX;
            // Bow away from the hub, so the curve never sags toward the star.
            if (DVec3.Dot(side, mid - hub) < 0) side = -side;

            // Closest approach of the straight line to the star.
            var ac = a - keepOutCentre;
            double t0 = Math.Max(0, Math.Min(1, -DVec3.Dot(ac, chord) / Math.Max(length * length, 1e-12)));
            var nearest = ac + chord * t0;
            bool detour = shape >= 0 ? shape == 1 : !(nearest.Length > keepOut * 1.5 || t0 <= 0 || t0 >= 1);
            if (!detour)
            {
                var apex = mid + side * (Math.Abs(bend) * length);
                // The control point that puts the curve's middle at the apex.
                var control = apex * 2 - mid;
                return Quadratic(a, control, b, s);
            }

            // The line passes near the star: go round through a waypoint out on the keep-out circle,
            // as two quadratics that join smoothly there (tangent along the chord).
            var outward = nearest - dir * DVec3.Dot(nearest, dir);
            outward = outward.LengthSquared > 1e-6 * keepOut * keepOut ? outward.Normalized : side;
            var w = keepOutCentre + nearest - outward * DVec3.Dot(nearest, outward) + outward * (keepOut * 1.4);
            if (s < 0.5)
            {
                var c1 = w - dir * ((w - a).Length * 0.5);
                return Quadratic(a, c1, w, s * 2);
            }
            var c2 = w + dir * ((b - w).Length * 0.5);
            return Quadratic(w, c2, b, s * 2 - 1);
        }

        static DVec3 Quadratic(DVec3 a, DVec3 c, DVec3 b, double t)
        {
            double u = 1 - t;
            return a * (u * u) + c * (2 * u * t) + b * (t * t);
        }

        public static DVec3 CurveTangent(DVec3 a, DVec3 b, double bend, double keepOut, double s, DVec3 up) =>
            CurveTangent(a, b, bend, DVec3.Zero, DVec3.Zero, keepOut, s, up);

        public static DVec3 CurveTangent(DVec3 a, DVec3 b, double bend, DVec3 hub, DVec3 keepOutCentre, double keepOut, double s, DVec3 up, int shape = -1)
        {
            const double h = 1e-4;
            double s0 = Math.Max(0, s - h), s1 = Math.Min(1, s + h);
            var d = Curve(a, b, bend, hub, keepOutCentre, keepOut, s1, up, shape) - Curve(a, b, bend, hub, keepOutCentre, keepOut, s0, up, shape);
            return d.LengthSquared > 0 ? d.Normalized : (b - a).Normalized;
        }

        public static double CurveLength(DVec3 a, DVec3 b, double bend, double keepOut, DVec3 up) =>
            CurveLength(a, b, bend, DVec3.Zero, DVec3.Zero, keepOut, up);

        public static double CurveLength(DVec3 a, DVec3 b, double bend, DVec3 hub, DVec3 keepOutCentre, double keepOut, DVec3 up, int shape = -1)
        {
            double len = 0;
            var prev = a;
            for (int i = 1; i <= 32; i++)
            {
                var p = Curve(a, b, bend, hub, keepOutCentre, keepOut, i / 32.0, up, shape);
                len += (p - prev).Length;
                prev = p;
            }
            return len;
        }

        /// <summary>Ease in, cruise, ease out: distance fraction at time fraction u, at rest at both ends.</summary>
        public static double Progress(double u)
        {
            u = Math.Max(0, Math.Min(1, u));
            return u - Math.Sin(Astro.TwoPi * u) / Astro.TwoPi;
        }

        /// <summary>
        /// Days to ride a whole lane of this length (the eased profile peaks at twice the mean speed),
        /// never more than <see cref="MaxRideDays"/>. Part of a lane takes that fraction of the time.
        /// </summary>
        public static double RideDays(double lengthKm) => Math.Min(2 * lengthKm / CruiseKmS / Astro.SecondsPerDay, MaxRideDays);

        // Lane geometry changes over days, and planning asks for it constantly: cache per half day.
        readonly Dictionary<(int, long), double> lengthCache = new Dictionary<(int, long), double>();
        readonly Dictionary<(int, int, long), List<LaneHop>> routeCache = new Dictionary<(int, int, long), List<LaneHop>>();
        // The quickest hop into every system from one, per origin and half day: planning asks for many routes from the
        // same place, and one sweep of Dijkstra's answers them all.
        readonly Dictionary<(int, long), Dictionary<int, LaneHop>> routeTrees = new Dictionary<(int, long), Dictionary<int, LaneHop>>();
        long cacheBucket = long.MinValue;

        long Bucket(double jd)
        {
            long bucket = (long)Math.Floor(jd * 2);
            if (bucket != cacheBucket)
            {
                cacheBucket = bucket;
                lengthCache.Clear();
                routeCache.Clear();
                routeTrees.Clear();
            }
            return bucket;
        }

        public double LaneLengthKm(Lane lane, double jd)
        {
            var key = (lane.Id, Bucket(jd));
            if (lengthCache.TryGetValue(key, out double km)) return km;
            km = CurveLengthKm(lane, RampHelio(lane, lane.A, jd), RampHelio(lane, lane.B, jd), jd);
            lengthCache[key] = km;
            return km;
        }

        /// <summary>
        /// Fewest-hours route between two systems (Dijkstra on ride times at the given date), or null. Each lane is priced at
        /// its condition's pace plus the stalls its reliability promises (U.SPC-T481), and a failed lane is left out, so
        /// traffic goes round it, or, with no way round, flies there under its own drive.
        /// </summary>
        public List<LaneHop> Route(int fromSystem, int toSystem, double jd)
        {
            if (fromSystem == toSystem) return null;
            var key = (fromSystem, toSystem, Bucket(jd));
            if (routeCache.TryGetValue(key, out var cached)) return cached;
            var route = FindRoute(fromSystem, toSystem, jd);
            routeCache[key] = route;
            return route;
        }

        List<LaneHop> FindRoute(int fromSystem, int toSystem, double jd, bool heavyOnly = false)
        {
            Dictionary<int, LaneHop> via;
            if (heavyOnly) via = RouteTree(fromSystem, jd, true);
            else
            {
                var treeKey = (fromSystem, Bucket(jd));
                if (!routeTrees.TryGetValue(treeKey, out via)) routeTrees[treeKey] = via = RouteTree(fromSystem, jd, false);
            }
            if (!via.ContainsKey(toSystem)) return null;
            var route = new List<LaneHop>();
            for (int at = toSystem; at != fromSystem; at = via[at].From) route.Insert(0, via[at]);
            return route;
        }

        /// <summary>The last hop of the quickest route from <paramref name="fromSystem"/> into every system it reaches.</summary>
        Dictionary<int, LaneHop> RouteTree(int fromSystem, double jd, bool heavyOnly)
        {
            var dist = new Dictionary<int, double> { [fromSystem] = 0 };
            var via = new Dictionary<int, LaneHop>();
            var open = new List<int> { fromSystem };
            var done = new HashSet<int>();
            while (open.Count > 0)
            {
                open.Sort((x, y) => dist[x].CompareTo(dist[y]));
                int here = open[0];
                open.RemoveAt(0);
                if (!done.Add(here)) continue;
                foreach (var lane in Lanes)
                {
                    if (lane.A != here && lane.B != here) continue;
                    if (heavyOnly && !lane.Heavy) continue;
                    if (!Carries(lane)) continue;
                    int next = lane.Other(here);
                    // Each hop also costs the crossing between ramps at a junction: about half a day.
                    Danger.TryGetValue(lane, out double danger);
                    double stalls = (1 - Reliability(lane)) * LaneCondition.StallDays.Value;
                    double cost = dist[here] + RideDays(lane, LaneLengthKm(lane, jd)) + 0.5 + danger + stalls;
                    if (dist.TryGetValue(next, out double old) && old <= cost) continue;
                    dist[next] = cost;
                    via[next] = new LaneHop(lane, here, next);
                    open.Add(next);
                }
            }
            return via;
        }
    }
}
