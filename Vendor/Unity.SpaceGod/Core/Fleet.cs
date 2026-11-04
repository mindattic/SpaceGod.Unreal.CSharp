using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Every ship in the system and its schedule. Ships alternate between parking in a circular
    /// orbit (real Keplerian rate for the body's GM) and brachistochrone transits: constant thrust
    /// toward the destination, flip at the midpoint, constant braking. Each transit is planned
    /// once at departure as an intercept against where the destination will be on arrival, so the
    /// whole schedule is deterministic and can be evaluated at any time. A hop within one planet's
    /// system is flown in that planet's frame, which carries the ship along the planet's orbit.
    /// </summary>
    public sealed class Fleet
    {
        public IReadOnlyList<Ship> Ships => ships;
        public IReadOnlyList<Faction> Factions => factions;

        readonly Ephemeris ephemeris;
        readonly BodyCatalog catalog;
        readonly List<Ship> ships = new List<Ship>();
        readonly List<Faction> factions = new List<Faction>();
        readonly Random rng;

        /// <summary>Transits are routed around the Sun at this distance.</summary>
        public const double SunKeepOutKm = 0.15 * Astro.AuKm;

        /// <summary>Hops within a planet's system are routed around the planet at this many radii.</summary>
        public const double PlanetKeepOutRadii = 2.0;

        /// <summary>A ship waits for its parking orbit to face the way out only if that takes less than this (days).</summary>
        public const double MaxFacingWaitDays = 2.0;

        public Fleet(Ephemeris ephemeris, int seed)
        {
            this.ephemeris = ephemeris;
            catalog = ephemeris.Catalog;
            rng = new Random(seed);
            leaks = new Random(seed ^ 0x5eed);
        }

        // ---------------------------------------------------------------- simulation

        /// <summary>The hyperlane network; null for the classic all-brachistochrone fleet.</summary>
        public Hyperlanes Lanes;

        /// <summary>Called with each event's date before it is processed, so the world can catch up to it.</summary>
        public Action<double> BeforeEvent;

        /// <summary>Called when a ship reaches its destination's parking orbit, before it picks the next one.</summary>
        public Action<Ship, double> Arrived;

        /// <summary>Called when a lander reaches a ring's queue: it is now in orbit there and can be fought (it trades on landing).</summary>
        public Action<Ship, double> ReachedRing;

        /// <summary>Picks a ship's next destination body (never the one it is at), or returns -1 for the built-in choice.</summary>
        public Func<Ship, int, double, int> ChooseNext;

        /// <summary>
        /// Worlds a free ship will not pick on a whim: a known haven or an armed port held by someone hostile to it,
        /// whose platforms would open on it each time it came (U.SPC-T291). Null for none.
        /// </summary>
        public Func<Ship, int, bool> Shuns;

        /// <summary>Overrides how long a ship lingers in port, given the default; null keeps the default.</summary>
        public Func<Ship, double, double> Dwell;

        /// <summary>Ships that run their own schedule (not escorts, not destroyed, not held by something else).</summary>
        public Func<Ship, bool> IsHeld;

        /// <summary>Docking rings: ships that <see cref="Lands"/> reach a ringed world through one of its rings.</summary>
        public readonly List<DockingRing> Rings = new List<DockingRing>();

        /// <summary>Which ships go down to the surface at ringed worlds (merchants); null for none.</summary>
        public Func<Ship, bool> Lands;

        /// <summary>
        /// Which ships fly on real propellant (see <see cref="Propulsion"/>): every transit is charged its
        /// delta-v from the tanks by the rocket equation, a ship that cannot cover a trip waits in port for
        /// propellant, and one that runs dry in flight drifts until a tanker reaches it. Null for none (the
        /// classic fleet, whose drives never run out).
        /// </summary>
        public Func<Ship, bool> BurnsPropellant;

        /// <summary>Whether a ship short of propellant for its next trip can wait at a body for its tanks to be filled (a port that sells it).</summary>
        public Func<Ship, int, bool> CanRefuelAt;
        /// <summary>Tops a ship up in port whatever its tank level, for a trip it is short of propellant for (U.SPC-T545).</summary>
        public Action<Ship, int, double> ForceTopUp;

        /// <summary>Raised when a ship runs dry: it drifts where its tanks gave out and calls for a tanker.</summary>
        public Action<Ship, double> RanDry;

        /// <summary>Raised when a tanker reaches a dry ship, with the tonnes it pumped across (0 if someone else already had).</summary>
        public Action<Ship, double, double> TankerArrived;

        /// <summary>Raised as a ship joins a hyperlane (where its toll is paid).</summary>
        public Action<Ship, LaneHop, double> LaneJoined;

        /// <summary>
        /// The chance a transit on propellant springs a leak (a micrometeoroid through a tank, a stuck valve)
        /// and vents most of what it carries, so it runs dry part way and drifts until a tanker comes. 0 for never.
        /// </summary>
        public double LeakChance;

        /// <summary>Raised when a transit springs a leak (see <see cref="LeakChance"/>), with the tonnes vented.</summary>
        public Action<Ship, double, double> Leaked;

        /// <summary>Rolls for leaks apart from the schedule's own dice, so turning them on changes nothing else.</summary>
        readonly Random leaks;

        bool Burns(Ship ship) => BurnsPropellant != null && BurnsPropellant(ship);

        /// <summary>Ships see each other coming and sidestep (see <see cref="StepAvoidance"/>).</summary>
        public bool AvoidCollisions = true;

        /// <summary>
        /// Run at a coarse level of detail (U.SPC-T104): the fleet of a system nobody is looking at (see
        /// <see cref="Galaxy.Focus"/>). Its pilots do not sidestep one another (that is flying round each
        /// other for the eye, not the schedule), and a slow boat looks for its launch window on a coarser grid
        /// of dates and flight times. Every schedule, route, cargo and propellant charge is still the real
        /// one, so a fleet promoted back to full detail flies on from where it is without a jump.
        /// </summary>
        public bool Coarse
        {
            get => coarse;
            set
            {
                coarse = value;
                if (value) evasion.Clear();
            }
        }
        bool coarse;

        /// <summary>How far ahead (days) ships look for a close pass: about an hour and a quarter.</summary>
        public const double AvoidanceHorizonDays = 0.05;

        readonly Dictionary<Ship, (DVec3 offset, DVec3 velocity)> evasion = new Dictionary<Ship, (DVec3, DVec3)>();
        readonly Dictionary<Ship, Ship> evadingFrom = new Dictionary<Ship, Ship>();
        double lastAvoidanceJd = double.NaN;

        /// <summary>How far (km) a ship has stepped aside from its planned path to keep clear of another.</summary>
        public DVec3 EvasionOf(Ship ship) => evasion.TryGetValue(ship.Controller, out var e) ? e.offset : DVec3.Zero;

        /// <summary>The ship it is giving way to right now, or null.</summary>
        public Ship EvadingFrom(Ship ship) => evadingFrom.TryGetValue(ship.Controller, out var s) ? s : null;

        /// <summary>The closest two ships may pass (km): a margin plus a few hull lengths each.</summary>
        public static double SafeDistanceKm(Ship a, Ship b) => 3 + 4 * (a.LengthKm + b.LengthKm);

        /// <summary>A ship at a body and able to fight there: holding orbit, or waiting at its ring.</summary>
        public static bool IsAt(Ship ship, int body) =>
            (ship.Phase == ShipPhase.Parked || ship.Phase == ShipPhase.Queued) && ship.Orbit != null && ship.Orbit.Body == body;

        /// <summary>
        /// Advances every ship's schedule up to <paramref name="jd"/>, processing departures and
        /// arrivals in date order across the whole fleet so that what one ship does (buying out a
        /// market, say) is seen by the next.
        /// </summary>
        public void Update(double jd)
        {
            for (int guard = 0; guard < 100000; guard++)
            {
                Ship next = null;
                double when = jd;
                foreach (var ship in ships)
                {
                    if (!RunsSchedule(ship)) continue;
                    double at = NextEventJd(ship);
                    if (at <= when)
                    {
                        when = at;
                        next = ship;
                    }
                }
                if (next == null) break;
                BeforeEvent?.Invoke(when);
                AdvancePhase(next);
            }
            StepAvoidance(jd);
        }

        /// <summary>Flying under the ship's own drive (in orbit, burning between worlds, holding off a lane or a ring): it can sidestep.</summary>
        static bool Manoeuvrable(Ship s) =>
            !s.IsEscort && !s.Destroyed && !s.Derelict && s.Battle == null &&
            (s.Phase == ShipPhase.Parked || s.Phase == ShipPhase.Transit || s.Phase == ShipPhase.Queued || s.Phase == ShipPhase.Lurk || s.Phase == ShipPhase.Adrift);

        /// <summary>
        /// Collision avoidance. Each ship projects every other one's course an hour or so ahead (from
        /// where both are and how they are moving); if they would pass closer than
        /// <see cref="SafeDistanceKm"/>, both turn aside, away from each other along the line of the
        /// near miss, the smaller ship giving way more, and drift back onto their planned paths once
        /// clear. The schedule is untouched: this is the pilots flying round each other.
        /// </summary>
        void StepAvoidance(double jd)
        {
            double dt = double.IsNaN(lastAvoidanceJd) ? 0 : jd - lastAvoidanceJd;
            lastAvoidanceJd = jd;
            if (!AvoidCollisions || coarse || dt <= 0) return;
            var list = new List<Ship>();
            // A held ship (the one the player is flying) isn't where its schedule says: nobody steers round that ghost.
            foreach (var s in ships) if (Manoeuvrable(s) && (IsHeld == null || !IsHeld(s))) list.Add(s);
            int n = list.Count;
            var pos = new DVec3[n];
            var vel = new DVec3[n];
            var target = new DVec3[n];
            var threatened = new bool[n];
            var near = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var s = list[i];
                evasion.TryGetValue(s, out var e);
                pos[i] = BaseStateOf(s, jd).HelioKm + e.offset;
                vel[i] = HelioVelocityOf(s, jd) + e.velocity;
            }
            evadingFrom.Clear();
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var a = list[i];
                    var b = list[j];
                    // Aim for a little more than the minimum, so they hold clear rather than see-saw at the edge.
                    double safe = SafeDistanceKm(a, b) * 1.25;
                    var r = pos[j] - pos[i];
                    // Far apart and not closing fast enough to meet within the horizon: skip.
                    var dv = vel[j] - vel[i];
                    double reach = safe + dv.Length * AvoidanceHorizonDays;
                    if (r.LengthSquared > reach * reach) continue;
                    double v2 = dv.LengthSquared;
                    double tca = v2 > 1e-12 ? Math.Max(0, Math.Min(AvoidanceHorizonDays, -DVec3.Dot(r, dv) / v2)) : 0;
                    var miss = r + dv * tca;
                    double dmin = miss.Length;
                    if (dmin >= safe)
                    {
                        // Clear, but only just: hold the sidestep rather than drift straight back into it.
                        if (dmin < safe * 1.6) near[i] = near[j] = true;
                        continue;
                    }
                    DVec3 away;
                    if (dmin > 1e-6) away = miss / dmin;
                    else
                    {
                        // Dead on: break to one side, the same way for the same pair.
                        var side = DVec3.Cross(dv.LengthSquared > 0 ? dv : DVec3.UnitX, DVec3.UnitZ);
                        away = side.LengthSquared > 1e-12 ? side.Normalized : DVec3.UnitY;
                    }
                    // The nearer the pass in time, the harder they turn.
                    double push = (safe - dmin) * (1 - 0.5 * tca / AvoidanceHorizonDays);
                    double la = a.LengthKm, lb = b.LengthKm;
                    target[i] -= away * (push * lb / (la + lb));
                    target[j] += away * (push * la / (la + lb));
                    threatened[i] = threatened[j] = true;
                    evadingFrom[a] = b;
                    evadingFrom[b] = a;
                }
            }
            // Ease each ship toward its sidestep (or back to its path): a critically damped turn that
            // settles in about a quarter of an hour, never harder than its drive can push.
            const double omega = 4 / 0.0104;
            for (int i = 0; i < n; i++)
            {
                var s = list[i];
                evasion.TryGetValue(s, out var e);
                // Threatened: step further aside from where it already is. Clear: drift back onto the path.
                target[i] = threatened[i] ? e.offset + target[i] : near[i] ? e.offset : DVec3.Zero;
                if (!threatened[i] && !near[i] && e.offset.LengthSquared < 1e-8 && e.velocity.LengthSquared < 1e-8)
                {
                    evasion.Remove(s);
                    continue;
                }
                var off = e.offset;
                var v = e.velocity;
                if (omega * dt > 1)
                {
                    // A long frame (high time warp): settle part way in one go, without overshoot, rather than integrating thousands of tiny steps.
                    off = DVec3.Lerp(off, target[i], Math.Min(1, omega * dt * 0.5));
                    v = DVec3.Zero;
                }
                else
                {
                    double step = Math.Min(dt, 0.002);
                    for (double done = 0; done < dt - 1e-12; done += step)
                    {
                        double d = Math.Min(step, dt - done);
                        var acc = (target[i] - off) * (omega * omega) - v * (2 * omega);
                        double maxAcc = s.AccelG * Astro.GravityKmPerDay2;
                        if (acc.Length > maxAcc) acc = acc.Normalized * maxAcc;
                        v += acc * d;
                        off += v * d;
                    }
                }
                evasion[s] = (off, v);
            }
            // Ships no longer flying free (landed, fighting, lost, or flown by the player) let go of their sidestep.
            var stale = new List<Ship>();
            foreach (var kv in evasion) if (!Manoeuvrable(kv.Key) || (IsHeld != null && IsHeld(kv.Key))) stale.Add(kv.Key);
            foreach (var s in stale) evasion.Remove(s);
        }

        bool RunsSchedule(Ship ship) => !ship.IsEscort && !ship.Destroyed && (IsHeld == null || !IsHeld(ship));

        /// <summary>State at <paramref name="jd"/>, including any sidestep to keep clear of other ships; parked ships use the ephemeris' current frame, which must be at the same date.</summary>
        public ShipState StateOf(Ship ship, double jd)
        {
            var st = BaseStateOf(ship, jd);
            var off = EvasionOf(ship);
            if (off.LengthSquared <= 0) return st;
            return new ShipState(st.HelioKm + off, st.Facing, st.SpeedKmS, st.Burning, st.Progress, st.Heat);
        }

        /// <summary>
        /// The ship's heliocentric velocity (km/day) on its schedule, before any sidestep. A parked or
        /// ring-bound ship is placed in its body's cached frame, which holds still across the finite
        /// difference, so that body's own motion round the Sun is added back: without it every such
        /// ship looked tens of km/s off from a ship in flight beside it.
        /// </summary>
        public DVec3 HelioVelocityOf(Ship ship, double jd)
        {
            const double h = 1e-4;
            var v = (BaseStateOf(ship, jd + h).HelioKm - BaseStateOf(ship, jd - h).HelioKm) / (2 * h);
            var s = ship.Controller;
            int frame = s.Phase == ShipPhase.Parked ? s.Orbit.Body
                : s.Phase == ShipPhase.Queued || s.Planetside ? s.Ring.Body
                : -1;
            if (frame >= 0) v += (ephemeris.HelioAt(frame, jd + h) - ephemeris.HelioAt(frame, jd - h)) / (2 * h);
            return v;
        }

        /// <summary>Where the ship's schedule puts it, before any sidestep.</summary>
        ShipState BaseStateOf(Ship ship, double jd)
        {
            var s = ship.Controller;
            if (s.Phase == ShipPhase.Parked)
            {
                var helio = ephemeris.Helio(s.Orbit.Body) + s.Orbit.OffsetAt(jd);
                return new ShipState(helio, s.Orbit.TangentAt(jd), s.Orbit.SpeedKmS, false, 0);
            }
            if (s.Phase == ShipPhase.Queued || s.Planetside) return RingState(s, jd);
            if (s.Phase == ShipPhase.Lurk || s.Phase == ShipPhase.Adrift)
            {
                // Slide out of the lane over the first couple of hours rather than jump.
                double ease = Astro.SmoothStep(0, 0.08, jd - s.PhaseStartJd);
                var held = LanePoint(s, jd, s.LaneHold) + s.LaneHoldSide * ease;
                var along = LanePoint(s, jd, Math.Min(1, s.LaneHold + 1e-4)) - LanePoint(s, jd, Math.Max(0, s.LaneHold - 1e-4));
                return new ShipState(held, along.LengthSquared > 0 ? along.Normalized : DVec3.UnitX, 0, false, s.LaneHold);
            }
            if (s.Phase == ShipPhase.Lane)
            {
                double progress = LaneProgress(s, jd);
                var here = RidePoint(s, jd, progress);
                const double h = 1e-3;
                var ahead = RidePoint(s, jd + h, LaneProgress(s, jd + h));
                var behind = RidePoint(s, jd - h, LaneProgress(s, jd - h));
                var v = (ahead - behind) / (2 * h);
                var facing = v.LengthSquared > 0 ? v.Normalized : (ahead - here).Normalized;
                return new ShipState(here, facing, v.Length / Astro.SecondsPerDay, true, progress);
            }

            if (s.Conic != null) return ConicState(s, jd);

            double total = s.PhaseEndJd - s.PhaseStartJd;
            double tau = Math.Max(0, Math.Min(total, jd - s.PhaseStartJd));
            double half = total * 0.5;
            double dist = tau < half
                ? 0.5 * s.AccelKmPerDay2 * tau * tau
                : s.PathLength - 0.5 * s.AccelKmPerDay2 * (total - tau) * (total - tau);

            PointAtDistance(s, dist, out var pos, out var dir);
            var burn = dir * (s.AccelKmPerDay2 * Math.Min(tau, total - tau));
            var velocity = burn + DriftVelocity(s, tau);
            // Same accelerate-then-decelerate profile as ever (see dist above), but the nose stays
            // pointed prograde the whole way instead of flipping to burn retrograde at the midpoint --
            // a visible 180 every single trip read as dead time, not drama, so the hull now just slows
            // (owner: "same physics, just have the ship slowing down... it doesn't look fun").
            var thrust = dir * s.AccelKmPerDay2 + DriftAcceleration(s);
            var at = ephemeris.HelioAt(s.FrameBody, jd) + pos + Drift(s, tau);
            return new ShipState(at, thrust.Normalized, velocity.Length / Astro.SecondsPerDay, true,
                s.PathLength > 0 ? dist / s.PathLength : 1);
        }

        /// <summary>
        /// A ship on a transfer orbit (U.SPC-T95): falling along its conic, nose along its velocity, its
        /// drive lit only for the escape burn as it leaves and the capture burn as it arrives.
        /// </summary>
        ShipState ConicState(Ship s, double jd)
        {
            var c = s.Conic;
            double total = c.Days;
            double tau = Math.Max(0, Math.Min(total, jd - s.PhaseStartJd));
            var pos = c.OffsetAt(tau);
            const double h = 1e-4;
            double t0 = Math.Max(0, tau - h), t1 = Math.Min(total, tau + h);
            var v = t1 > t0 ? (c.OffsetAt(t1) - c.OffsetAt(t0)) / (t1 - t0) : c.V0;
            bool burning = tau < c.DepartureBurnDays || total - tau < c.ArrivalBurnDays;
            var at = ephemeris.HelioAt(s.FrameBody, jd) + pos;
            return new ShipState(at, v.LengthSquared > 0 ? v.Normalized : DVec3.UnitX, v.Length / Astro.SecondsPerDay, burning,
                total > 0 ? tau / total : 1);
        }

        /// <summary>
        /// A ship at a docking ring, in the body's turning frame: queued (easing in from its holding
        /// point to the ring over the wait), descending (straight down under the ring to the city,
        /// heating as it passes the entry interface), landed, or climbing back up.
        /// </summary>
        ShipState RingState(Ship s, double jd)
        {
            var ring = s.Ring;
            var centre = ephemeris.Helio(ring.Body);
            double total = Math.Max(s.PhaseEndJd - s.PhaseStartJd, 1e-9);
            double u = Math.Max(0, Math.Min(1, (jd - s.PhaseStartJd) / total));
            if (s.Phase == ShipPhase.Queued)
            {
                var p = QueuePosition(s, jd);
                const double h = 1e-4;
                var drift = (QueuePosition(s, jd + h) - QueuePosition(s, jd - h)).Length / (2 * h * Astro.SecondsPerDay);
                return new ShipState(centre + p, (centre - (centre + p)).Normalized, drift, false, 0);
            }
            var up = ring.Up(jd);
            double r = RingRadiusAt(s, u);
            double rate = (RingRadiusAt(s, Math.Min(1, u + 1e-4)) - RingRadiusAt(s, Math.Max(0, u - 1e-4))) / (2e-4 * total * Astro.SecondsPerDay);
            float heat = 0;
            if (s.Phase != ShipPhase.Landed)
            {
                double altitude = r - ring.SurfaceKm;
                // Brightest on the way down, a little after the interface; a gentler glow climbing out.
                double depth = 1 - altitude / Math.Max(ring.EntryInterfaceKm, 1);
                heat = (float)(Math.Max(0, Math.Min(1, depth * 1.6)) * Math.Max(0, Math.Min(1, altitude / (ring.EntryInterfaceKm * 0.15))) * (s.Phase == ShipPhase.Descent ? 1 : 0.35));
            }
            // Tail-first like a landing rocket: the drive points at the ground, braking on the way
            // down and lifting on the way up.
            var facing = up;
            return new ShipState(centre + up * r, facing, Math.Abs(rate), s.Phase != ShipPhase.Landed, u, heat);
        }

        /// <summary>Spacing of the queue up a ring's approach line, km per ship (one per clearance interval).</summary>
        public const double QueueSpacingKm = 25;

        /// <summary>
        /// Where a queued ship waits (relative to the ringed body): in line up the approach path above
        /// the ring, one <see cref="QueueSpacingKm"/> per ship ahead of it, creeping down as each is
        /// cleared and reaching the ring as its own turn comes. It joins the line from wherever it
        /// arrived over the first quarter-hour.
        /// </summary>
        DVec3 QueuePosition(Ship s, double jd)
        {
            var ring = s.Ring;
            double ahead = Math.Max(0, s.PhaseEndJd - jd) / Math.Max(ring.EntryIntervalDays, 1e-9);
            double along = Math.Min(ring.HoldKm, QueueSpacingKm * ahead);
            var slot = ring.Point(ring.RadiusKm + along).OffsetAt(jd);
            double total = Math.Max(s.PhaseEndJd - s.PhaseStartJd, 1e-9);
            double join = Astro.SmoothStep(0, Math.Min(total, 0.01), jd - s.PhaseStartJd);
            return DVec3.Lerp(s.HoldPoint.OffsetAt(jd), slot, join);
        }

        /// <summary>Distance from the ringed body's centre during a descent or climb at fraction u.</summary>
        static double RingRadiusAt(Ship s, double u)
        {
            var ring = s.Ring;
            if (s.Phase == ShipPhase.Landed) return ring.SurfaceKm;
            // Fast through the empty upper part, slow and careful through the air near the ground.
            double e = s.Phase == ShipPhase.Descent ? 1 - Math.Pow(1 - u, 2.2) : Math.Pow(u, 2.2);
            double top = ring.RadiusKm, bottom = ring.SurfaceKm;
            return s.Phase == ShipPhase.Descent ? top + (bottom - top) * e : bottom + (top - bottom) * e;
        }

        /// <summary>Distance fraction along the lane at a date (eased in and out).</summary>
        public static double LaneProgress(Ship s, double jd) =>
            s.LaneStartProgress + (s.LaneEndProgress - s.LaneStartProgress) * Hyperlanes.Progress((jd - s.PhaseStartJd) / Math.Max(s.PhaseEndJd - s.PhaseStartJd, 1e-9));

        /// <summary>
        /// Where a riding ship is: on its lane, plus whatever is left of a sideways offset it is easing
        /// back in from (after a lurk or an interdiction).
        /// </summary>
        DVec3 RidePoint(Ship s, double jd, double progress) =>
            LanePoint(s, jd, progress) + s.LaneHoldSide * LaneRejoin(s, jd);

        /// <summary>How much of its sideways hold a ship rejoining a lane still has (1 → 0 over the first couple of hours).</summary>
        public static double LaneRejoin(Ship s, double jd) =>
            s.LaneHoldSide.LengthSquared > 0 ? 1 - Astro.SmoothStep(0, 0.08, jd - s.PhaseStartJd) : 0;

        /// <summary>Heliocentric position on the ship's lane at a date and distance fraction.</summary>
        public DVec3 LanePoint(Ship s, double jd, double progress)
        {
            var a = ephemeris.HelioAt(s.LaneLeg.From, jd) + s.LaneFromOffset;
            var b = ephemeris.HelioAt(s.LaneLeg.To, jd) + s.LaneToOffset;
            return Lanes.CurveKm(s.LaneLeg.Lane, a, b, progress, jd, s.LaneStar, s.LaneShape);
        }

        /// <summary>
        /// Position relative to the ship's <see cref="Ship.FrameBody"/> at a fraction of its transit
        /// (by distance along the path, like <see cref="ShipState.Progress"/>).
        /// </summary>
        public static DVec3 TransitOffset(Ship s, double progress)
        {
            // On a transfer orbit progress is by time (see ConicState).
            if (s.Conic != null) return s.Conic.OffsetAt(Math.Max(0, Math.Min(1, progress)) * s.Conic.Days);
            double dist = Math.Max(0, Math.Min(1, progress)) * s.PathLength;
            PointAtDistance(s, dist, out var pos, out _);
            double total = s.PhaseEndJd - s.PhaseStartJd;
            double tau = s.AccelKmPerDay2 <= 0 ? progress * total
                : dist < s.PathLength * 0.5
                    ? Math.Sqrt(2 * dist / s.AccelKmPerDay2)
                    : total - Math.Sqrt(Math.Max(0, 2 * (s.PathLength - dist) / s.AccelKmPerDay2));
            return pos + Drift(s, tau);
        }

        /// <summary>Position relative to the ship's <see cref="Ship.FrameBody"/> a time into its transit (days).</summary>
        public static DVec3 TransitOffsetAt(Ship s, double tau)
        {
            if (s.Conic != null) return s.Conic.OffsetAt(tau);
            double total = s.PhaseEndJd - s.PhaseStartJd;
            tau = Math.Max(0, Math.Min(total, tau));
            double dist = tau < total * 0.5
                ? 0.5 * s.AccelKmPerDay2 * tau * tau
                : s.PathLength - 0.5 * s.AccelKmPerDay2 * (total - tau) * (total - tau);
            PointAtDistance(s, dist, out var pos, out _);
            return pos + Drift(s, tau);
        }

        // A steady acceleration (v1 - v0) / T on top of the flip-and-burn takes the ship from its
        // departure velocity to its arrival velocity; its displacement is v0·t + (v1 - v0)·t² / 2T.
        static DVec3 Drift(Ship s, double tau)
        {
            double total = s.PhaseEndJd - s.PhaseStartJd;
            return s.DepartureVelocity * tau + (s.ArrivalVelocity - s.DepartureVelocity) * (tau * tau / (2 * total));
        }

        static DVec3 DriftVelocity(Ship s, double tau) =>
            DVec3.Lerp(s.DepartureVelocity, s.ArrivalVelocity, tau / (s.PhaseEndJd - s.PhaseStartJd));

        static DVec3 DriftAcceleration(Ship s) =>
            (s.ArrivalVelocity - s.DepartureVelocity) / (s.PhaseEndJd - s.PhaseStartJd);

        /// <summary>
        /// Position (relative to the ship's <see cref="Ship.FrameBody"/>, before drift) and travel
        /// direction at a distance along a transiting ship's flip-and-burn path.
        /// </summary>
        public static void PointAtDistance(Ship s, double dist, out DVec3 pos, out DVec3 dir)
        {
            var path = s.Path;
            for (int i = 1; i < path.Length; i++)
            {
                if (dist <= s.PathCumulative[i] || i == path.Length - 1)
                {
                    double segStart = s.PathCumulative[i - 1];
                    double segLen = s.PathCumulative[i] - segStart;
                    double t = segLen > 0 ? Math.Max(0, Math.Min(1, (dist - segStart) / segLen)) : 1;
                    pos = DVec3.Lerp(path[i - 1], path[i], t);
                    dir = (path[i] - path[i - 1]).Normalized;
                    return;
                }
            }
            pos = path[path.Length - 1];
            dir = DVec3.UnitX;
        }

        /// <summary>Time remaining in the current phase, days.</summary>
        public static double RemainingDays(Ship ship, double jd) => Math.Max(0, ship.Controller.PhaseEndJd - jd);

        /// <summary>When the ship's schedule next needs attention: the end of its phase, or the moment its tanks run dry part way through a transit.</summary>
        static double NextEventJd(Ship s) => s.Phase == ShipPhase.Transit && s.StrandJd < s.PhaseEndJd ? s.StrandJd : s.PhaseEndJd;

        void AdvancePhase(Ship ship)
        {
            if (ship.Phase == ShipPhase.Transit && ship.StrandJd < ship.PhaseEndJd)
            {
                Strand(ship, ship.StrandJd);
                return;
            }
            if (ship.Phase == ShipPhase.Parked && ship.OutOfPropellant)
            {
                TankerArrives(ship);
                return;
            }
            if (ship.Phase == ShipPhase.Parked)
            {
                // Holding position (a siege, a refit, an ambush): decide again rather than fly to itself.
                // A free ship's hop out to open space near the same world is a real trip, though.
                // A ship bound for a gate is leaving even from the root it is parked at (one gate to the next).
                // So is an ore tug's push in from the field to the very world the field lies round (U.SPC-T498).
                if (ship.DestinationBody == ship.Orbit.Body && !ship.Wander && !ship.Homing && ship.GateKm == null) ScheduleNext(ship, ship.PhaseEndJd);
                else Depart(ship);
            }
            else if (ship.Phase == ShipPhase.Lane) EndLane(ship);
            else if (ship.Phase == ShipPhase.Lurk || ship.Phase == ShipPhase.Adrift) ResumeLane(ship, ship.PhaseEndJd);
            else if (ship.Phase == ShipPhase.Queued) BeginDescent(ship);
            else if (ship.Phase == ShipPhase.Descent) Arrive(ship, landed: true);
            else if (ship.Phase == ShipPhase.Landed)
            {
                if (ship.DestinationBody == ship.Ring.Body) ScheduleNext(ship, ship.PhaseEndJd);
                else BeginAscent(ship);
            }
            else if (ship.Phase == ShipPhase.Ascent) Depart(ship);
            else if (ship.Journey != null && ship.JourneyIndex < ship.Journey.Count && ship.Orbit.Fixed) ReachRamp(ship, ship.PhaseEndJd);
            else if (ship.Ring != null && ship.Orbit == ship.HoldPoint) JoinQueue(ship);
            else Arrive(ship);
        }

        // ---------------------------------------------------------------- docking rings

        /// <summary>The way a ship arrives at a body: in orbit, or to the holding point of the ring with the shortest queue.</summary>
        ParkingOrbit ArrivalFor(Ship ship, int body)
        {
            ship.Ring = null;
            ship.HoldPoint = null;
            if (Lands == null || !Lands(ship)) return NewParkingOrbit(body, ship);
            DockingRing best = null;
            foreach (var r in Rings)
                if (r.Body == body && (best == null || r.NextEntryJd < best.NextEntryJd)) best = r;
            if (best == null) return NewParkingOrbit(body, ship);
            ship.Ring = best;
            ship.HoldPoint = best.HoldPoint(ship);
            return ship.HoldPoint;
        }

        /// <summary>Minimum time from the holding point to the ring (an unhurried approach).</summary>
        public const double MinQueueDays = 12.0 / 1440;

        /// <summary>Arrived off the ring: take the next free slot to descend (rings clear one ship per interval).</summary>
        void JoinQueue(Ship ship)
        {
            double jd = ship.PhaseEndJd;
            var ring = ship.Ring;
            double entry = Math.Max(jd + MinQueueDays, ring.NextEntryJd);
            ring.NextEntryJd = entry + ring.EntryIntervalDays;
            ship.Phase = ShipPhase.Queued;
            ship.Path = null;
            ship.PhaseStartJd = jd;
            ship.PhaseEndJd = entry;
            ReachedRing?.Invoke(ship, jd);
        }

        void BeginDescent(Ship ship)
        {
            double jd = ship.PhaseEndJd;
            ship.Orbit = ship.Ring.RingPoint;
            ship.Phase = ShipPhase.Descent;
            ship.PhaseStartJd = jd;
            ship.PhaseEndJd = jd + ship.Ring.DescentDays;
        }

        void BeginAscent(Ship ship)
        {
            double jd = ship.PhaseEndJd;
            ship.Phase = ShipPhase.Ascent;
            ship.PhaseStartJd = jd;
            ship.PhaseEndJd = jd + ship.Ring.DescentDays;
        }

        /// <summary>Days a ship landing at a ringed world adds for the queue, the descent and the climb out.</summary>
        double RingOverheadDays(Ship ship, int body, double arriveJd)
        {
            if (Lands == null || !Lands(ship)) return 0;
            double best = double.MaxValue;
            foreach (var r in Rings)
                if (r.Body == body) best = Math.Min(best, Math.Max(0, r.NextEntryJd - arriveJd) + MinQueueDays + 2 * r.DescentDays);
            return best == double.MaxValue ? 0 : best;
        }

        /// <summary>
        /// Leaves port. A trip to another planetary system goes by hyperlane when there is one: first
        /// a burn to the first lane's ramp; otherwise (or locally) one burn straight to the destination.
        /// </summary>
        void Depart(Ship ship)
        {
            double departJd = ship.PhaseEndJd;
            int from = ship.Orbit.Body;
            int to = ship.DestinationBody;
            ship.FinalBody = to;
            ship.Journey = null;
            ship.JourneyIndex = 0;
            if (ship.GateKm is DVec3 gate && to == catalog.Root.Index)
            {
                // A plotted course (U.SPC-T516) crosses the system by the hyperlanes where they run to the gate.
                if (ship.Voyage != null && ship.Voyage.Purpose == VoyagePurpose.Plotted && TryGateJourney(ship, from, gate, departJd))
                {
                    var first = ship.Journey[0];
                    Burn(ship, departJd, first.From, () => RampOrbit(first, first.From, departJd), allowConic: true);
                    return;
                }
                // Out to a jump gate: a fixed point off the system's root.
                Burn(ship, departJd, to, () => FixedPoint(to, gate));
                return;
            }
            if (ship.StakeoutKm is DVec3 approach && to == catalog.Root.Index)
            {
                // Raiders out to lie in wait off a gate (U.SPC-T94): the same fixed point off the root.
                Burn(ship, departJd, to, () => FixedPoint(to, approach));
                return;
            }
            if (ship.PicketGate != null && to == catalog.Root.Index)
            {
                // A gate picket back out to its post off its gate's exit (U.SPC-T480).
                var post = ship.PicketPostKm;
                Burn(ship, departJd, to, () => FixedPoint(to, post));
                return;
            }
            // A slow boat that judged the lanes' tolls dearer than the time flies straight across on a transfer orbit.
            bool direct = ship.PlannedDirect && UsesConics(ship);
            ship.PlannedDirect = false;
            if (Lanes != null && !direct)
            {
                int fromSystem = Lanes.SystemOf(from), toSystem = Lanes.SystemOf(to);
                if (fromSystem != toSystem) ship.Journey = Lanes.RouteFor(ship, fromSystem, toSystem, departJd);
            }
            if (ship.Journey != null)
            {
                var hop = ship.Journey[0];
                Burn(ship, departJd, hop.From, () => RampOrbit(hop, hop.From, departJd), allowConic: true);
            }
            else if (ship.Wander) Burn(ship, departJd, to, () => WanderOrbit(to, ship));
            else
            {
                var home = ship.Orbit;
                var ring = ship.Ring;
                var hold = ship.HoldPoint;
                Burn(ship, departJd, to, () => ArrivalFor(ship, to), allowConic: true);
                // The planned transfer orbit failed at the real departure: the straight flip-and-burn was only ever the
                // comparison baseline, so take the lanes the planner priced as the alternative (U.SPC-T390).
                if (direct && ship.Conic == null && Lanes != null && !ship.Wander)
                {
                    int fromSystem = Lanes.SystemOf(from), toSystem = Lanes.SystemOf(to);
                    var route = fromSystem != toSystem ? Lanes.Route(fromSystem, toSystem, departJd) : null;
                    if (route != null)
                    {
                        RefundUnflown(ship, departJd);
                        ship.Orbit = home;
                        ship.Ring = ring;
                        ship.HoldPoint = hold;
                        ship.Journey = route;
                        ship.JourneyIndex = 0;
                        var hop = route[0];
                        Burn(ship, departJd, hop.From, () => RampOrbit(hop, hop.From, departJd), allowConic: true);
                    }
                }
            }
        }

        /// <summary>How near a gate a hull must be to count as at it, to set out along its spur, km.</summary>
        const double AtGateKm = 100000;

        /// <summary>
        /// The lanes a hull on a plotted course takes across a system to <paramref name="gate"/> (U.SPC-T516): out along the spur
        /// of the gate it has just come through (or from its planetary system), across the network between, and in along the
        /// spur of the gate it is bound for. False, leaving no journey, where no lane runs to that gate (a frontier system, a Void
        /// Base) or the network does not join, and the hull flies straight across as any ship bound for a gate does.
        /// </summary>
        bool TryGateJourney(Ship ship, int from, DVec3 gate, double jd)
        {
            if (Lanes == null || Lanes.GateSpurs.Count == 0) return false;
            Lane target = null;
            foreach (var spur in Lanes.GateSpurs)
                if ((Hyperlanes.GateHelio(spur.Gate) - gate).LengthSquared < 1) target = spur;
            if (target == null) return false;
            var hops = new List<LaneHop>();
            int at;
            if (from == catalog.Root.Index)
            {
                // At the gate it came through: out from there along its spur.
                var here = ship.Orbit.OffsetAt(jd);
                Lane start = null;
                double nearest = AtGateKm;
                foreach (var spur in Lanes.GateSpurs)
                {
                    double km = (Hyperlanes.GateHelio(spur.Gate) - here).Length;
                    if (km >= nearest) continue;
                    nearest = km;
                    start = spur;
                }
                if (start == null || start == target) return false;
                hops.Add(new LaneHop(start, start.B, start.A));
                at = start.A;
            }
            else at = Lanes.SystemOf(from);
            if (at != target.A)
            {
                var between = Lanes.Route(at, target.A, jd);
                if (between == null) return false;
                hops.AddRange(between);
            }
            hops.Add(new LaneHop(target, target.A, target.B));
            ship.Journey = hops;
            ship.JourneyIndex = 0;
            return true;
        }

        /// <summary>
        /// A spot out in open space near a world for a free ship to drift in: a slow orbit at any tilt,
        /// anywhere from three radii out to well past its moons (but inside the world's neighbourhood).
        /// </summary>
        ParkingOrbit WanderOrbit(int body, Ship ship)
        {
            var b = catalog[body];
            double inner = b.RadiusKm * 3;
            double outer = Math.Max(inner * 1.5, Math.Min(ScaleModel.NeighbourhoodKm(b) * 0.6, Math.Max(b.RadiusKm * 12, b.MoonReachKm * 0.9)));
            double r = inner + (outer - inner) * rng.NextDouble();
            var normal = new DVec3(Jitter(1), Jitter(1), Jitter(1));
            if (normal.LengthSquared < 1e-6) normal = DVec3.UnitZ;
            normal = normal.Normalized;
            var reference = Math.Abs(normal.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
            var u = DVec3.Cross(reference, normal).Normalized;
            return new ParkingOrbit
            {
                Body = body, RadiusKm = r, U = u, V = DVec3.Cross(normal, u),
                RateRadPerDay = Kepler.CircularRate(Math.Max(b.Gm, 1e-12), r),
            };
        }

        /// <summary>A hyperlane ramp as a fixed point attached to its planet.</summary>
        ParkingOrbit RampOrbit(LaneHop hop, int system, double jd) => FixedPoint(system, Lanes.RampOffset(hop.Lane, system, jd + 0.4));

        static ParkingOrbit FixedPoint(int body, DVec3 offset)
        {
            var u = offset.Normalized;
            var v = DVec3.Cross(Math.Abs(u.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX, u).Normalized;
            return new ParkingOrbit { Body = body, RadiusKm = offset.Length, U = u, V = v, RateRadPerDay = 0, Fixed = true };
        }

        /// <summary>
        /// At the ramp the burn has brought it to: joins the lane, or, when the lane has failed since the route was planned
        /// (U.SPC-T481), goes round it by what is left of the network, or with no way round, on under its own drive.
        /// </summary>
        void ReachRamp(Ship ship, double jd)
        {
            var hop = ship.Journey[ship.JourneyIndex];
            if (Lanes.Carries(hop.Lane))
            {
                StartLane(ship, jd);
                return;
            }
            int to = ship.FinalBody;
            int toSystem = Lanes.SystemOf(to);
            var route = toSystem != hop.From ? Lanes.RouteFor(ship, hop.From, toSystem, jd) : null;
            ship.Journey = route;
            ship.JourneyIndex = 0;
            if (route != null)
            {
                var next = route[0];
                Burn(ship, jd, next.From, () => RampOrbit(next, next.From, jd), allowConic: true);
            }
            else if (ship.GateKm is DVec3 gate && to == catalog.Root.Index) Burn(ship, jd, to, () => FixedPoint(to, gate));
            else Burn(ship, jd, to, () => ArrivalFor(ship, to), allowConic: true);
        }

        /// <summary>
        /// A lane's rings slip out of alignment under a rider (U.SPC-T481, see <see cref="Hyperlanes.Reliability"/>): the ride
        /// stalls <paramref name="at"/> of the way along, the hull easing to a stop just off the lane, and picks up again
        /// <paramref name="days"/> later, from there. Only a ride just joined, not one already part-way or held at the helm.
        /// </summary>
        public bool StallRide(Ship ship, double at, double days)
        {
            if (ship.Phase != ShipPhase.Lane || ship.LaneStartProgress > 0 || ship.LaneEndProgress < 1) return false;
            at = Math.Max(0.05, Math.Min(0.95, at));
            ship.LaneEndProgress = at;
            ship.PhaseEndJd = ship.PhaseStartJd + (ship.PhaseEndJd - ship.PhaseStartJd) * at;
            ship.LurkDays = Math.Max(0.01, days);
            ship.Stalled = true;
            return true;
        }

        /// <summary>Joins the lane at its ramp (where the burn has just brought the ship).</summary>
        void StartLane(Ship ship, double jd)
        {
            var hop = ship.Journey[ship.JourneyIndex];
            ship.LaneLeg = hop;
            ship.LaneFromOffset = ship.Orbit.OffsetAt(jd);
            var a = ephemeris.HelioAt(hop.From, jd) + ship.LaneFromOffset;
            var estimate = Lanes.RideDays(ship, hop.Lane, Lanes.LaneLengthKm(hop.Lane, jd));
            ship.LaneToOffset = Lanes.RampOffset(hop.Lane, hop.To, jd + estimate);
            var b = ephemeris.HelioAt(hop.To, jd) + ship.LaneToOffset;
            ship.LaneStar = Lanes.KeepOutStar(a, b, jd);
            ship.LaneShape = Lanes.ShapeOf(hop.Lane, a, b, ship.LaneStar, jd);
            ship.LaneHoldSide = DVec3.Zero;
            // A fresh ride: no point left part-way on an earlier lane to get back in at (CanRejoin).
            ship.LaneHold = 0;
            ship.LaneLengthKm = Lanes.CurveLengthKm(hop.Lane, a, b, jd, ship.LaneStar, ship.LaneShape);
            ship.DepartureOrbit = ship.Orbit;
            ship.OriginBody = hop.From;
            ship.FrameBody = 0;
            ship.Path = null;
            ship.Conic = null;
            ship.Phase = ShipPhase.Lane;
            ship.LaneSurged = false;
            ship.Stalled = false;
            ship.LaneStartProgress = 0;
            ship.LaneEndProgress = ship.LurkLane == hop.Lane ? ship.LurkAt : 1;
            ship.PhaseStartJd = jd;
            // A worn lane (U.SPC-T481) rides at its condition's pace.
            ship.PhaseEndJd = jd + Lanes.RideDays(ship, hop.Lane, ship.LaneLengthKm) * ship.LaneEndProgress;
            LaneJoined?.Invoke(ship, hop, jd);
        }

        /// <summary>Back into the lane from where the ship stopped (a lurk over, or an ambush survived), and on to its end.</summary>
        public void ResumeLane(Ship ship, double jd)
        {
            ship.Phase = ShipPhase.Lane;
            ship.LaneSurged = false;
            ship.Stalled = false;
            ship.LaneStartProgress = ship.LaneHold;
            ship.LaneEndProgress = 1;
            // LaneHoldSide is kept: the ship eases back in from beside the lane over the first hours.
            ship.PhaseStartJd = jd;
            ship.PhaseEndJd = jd + Lanes.RideDays(ship, ship.LaneLeg.Lane, ship.LaneLengthKm) * Math.Max(1e-3, 1 - ship.LaneHold);
        }

        /// <summary>Drops out of the lane where the ship is, a little to one side (an interdiction pulse).</summary>
        public void PullOut(Ship ship, double jd, double progress, DVec3 side)
        {
            ship.Phase = ShipPhase.Adrift;
            ship.LaneHold = Math.Max(0, Math.Min(1, progress));
            ship.LaneHoldSide = side;
            ship.PhaseStartJd = jd;
            ship.PhaseEndJd = jd + 0.5;
        }

        /// <summary>A sideways offset (km, ecliptic) from a lane at a point, to wait just outside it.</summary>
        public DVec3 OffLane(Ship s, double jd, double progress, double km, int sign)
        {
            var along = LanePoint(s, jd, Math.Min(1, progress + 1e-3)) - LanePoint(s, jd, Math.Max(0, progress - 1e-3));
            var side = DVec3.Cross(along, DVec3.UnitZ);
            return side.LengthSquared > 0 ? side.Normalized * (km * sign) : DVec3.UnitX * km;
        }

        /// <summary>Leaves the lane at the far ramp: on to the next lane's ramp, or the last burn home.</summary>
        void EndLane(Ship ship)
        {
            double jd = ship.PhaseEndJd;
            var hop = ship.LaneLeg;
            if (ship.LaneEndProgress < 1)
            {
                // Stop part-way and wait just off the lane.
                ship.Phase = ShipPhase.Lurk;
                ship.LaneHold = ship.LaneEndProgress;
                ship.LaneHoldSide = OffLane(ship, jd, ship.LaneHold, 30000, ship.Id % 2 == 0 ? 1 : -1);
                ship.PhaseStartJd = jd;
                ship.PhaseEndJd = jd + ship.LurkDays;
                ship.LurkLane = null;
                return;
            }
            // Ridden to its end: nothing left part-way to rejoin.
            ship.LaneHold = 0;
            ship.Orbit = FixedPoint(hop.To, ship.LaneToOffset);
            ship.JourneyIndex++;
            if (ship.JourneyIndex < ship.Journey.Count)
            {
                var next = ship.Journey[ship.JourneyIndex];
                Burn(ship, jd, next.From, () => RampOrbit(next, next.From, jd), allowConic: true);
            }
            else
            {
                int to = ship.FinalBody;
                // Off the spur at a jump gate (a plotted course): in to the gate itself, not into an orbit round the star.
                if (ship.GateKm is DVec3 gate && to == catalog.Root.Index) Burn(ship, jd, to, () => FixedPoint(to, gate));
                else Burn(ship, jd, to, () => ArrivalFor(ship, to), allowConic: true);
            }
        }

        // ---------------------------------------------------------------- the player's own rides (U.SPC-T41)

        /// <summary>
        /// A piloted hull flown in through an on-ramp's ring gate joins the lane where it is, by the same
        /// <see cref="StartLane"/> AI traffic rides: a one-hop journey ending at the far ramp. The hull is
        /// held (<see cref="IsHeld"/>), so its pilot brings it off there with <see cref="RampReached"/>; let
        /// go of mid-ride, the fleet carries it on to that system like any other ship.
        /// </summary>
        public void JoinLane(Ship ship, LaneHop hop, DVec3 helioKm, double jd)
        {
            ship.Journey = new List<LaneHop> { hop };
            ship.JourneyIndex = 0;
            ship.FinalBody = ship.DestinationBody = RideDestination(hop);
            ship.Wander = false;
            ship.Ring = null;
            ship.HoldPoint = null;
            ship.LurkLane = null;
            ship.StrandedInFlight = false;
            ship.Orbit = FixedPoint(hop.From, helioKm - ephemeris.HelioAt(hop.From, jd));
            StartLane(ship, jd);
        }

        /// <summary>
        /// Where a ride's crew make for if its pilot lets go of it: the far ramp's system, or, on a spur to a jump gate
        /// (U.SPC-T379), which AI crews do not take on their own, back to the planetary system the spur leaves from.
        /// </summary>
        static int RideDestination(LaneHop hop) => hop.Lane.Gate != null && hop.To == hop.Lane.B ? hop.From : hop.To;

        /// <summary>
        /// The ring chain as a rider sees it (U.SPC-T379): how many rings its lane strings (<see cref="Hyperlanes.RingCount"/>),
        /// how many it has flown through, and whether they are speeding it up or braking it for the far ramp.
        /// </summary>
        public static int RideRings(Ship ship, double jd, out int passed, out bool accelerating)
        {
            int count = Hyperlanes.RingCount(ship.LaneLengthKm);
            passed = Hyperlanes.RingsPassed(LaneProgress(ship, jd), count);
            double span = Math.Max(ship.PhaseEndJd - ship.PhaseStartJd, 1e-9);
            accelerating = Hyperlanes.RingsAccelerate((jd - ship.PhaseStartJd) / span);
            return count;
        }

        /// <summary>Whether a ride has reached the far ramp (a held rider waits there for its pilot's call).</summary>
        public static bool RideOver(Ship ship, double jd) => ship.Phase == ShipPhase.Lane && jd >= ship.PhaseEndJd;

        /// <summary>
        /// A piloted ride has reached its far ramp: carry on along <paramref name="next"/> (one of
        /// <see cref="Hyperlanes.OnwardHops"/>) from right where the hull is, or, with none, come off and be
        /// pinned there under the pilot's hand, facing <paramref name="facing"/>.
        /// </summary>
        public void RampReached(Ship ship, LaneHop? next, DVec3 facing, double jd)
        {
            var hop = ship.LaneLeg;
            if (next is LaneHop onward && onward.From == hop.To)
            {
                ship.Orbit = FixedPoint(hop.To, ship.LaneToOffset);
                // A fresh list: a journey may be a planner's cached route, never to be added to.
                ship.Journey = new List<LaneHop> { onward };
                ship.JourneyIndex = 0;
                ship.FinalBody = ship.DestinationBody = RideDestination(onward);
                StartLane(ship, jd);
                return;
            }
            // Brought off at the far ramp: the ride is done, nothing left part-way to rejoin.
            ship.LaneHold = 0;
            PinAt(ship, hop.To, ship.LaneToOffset, facing, jd);
        }

        // ---------------------------------------------------------------- the player's answers to an interdiction (U.SPC-T42)

        /// <summary>How many times the lane's pace a surging rider goes (<see cref="SurgeRide"/>).</summary>
        public const double SurgeFactor = 2.5;

        /// <summary>How far from the lane line, km, a hull out of it (dropped out, or pulled out and fought clear) can still get back in where it left.</summary>
        public const double RejoinKm = 100000;

        /// <summary>
        /// A pilot boosting through: the rest of the ride goes at <see cref="SurgeFactor"/> times its pace, from right where
        /// the hull is (its progress along the lane does not jump), so it reaches every point ahead, and its off-ramp, sooner.
        /// Once a ride; false if the hull is not riding or has already surged.
        /// </summary>
        public static bool SurgeRide(Ship ship, double jd)
        {
            if (ship.Phase != ShipPhase.Lane || ship.LaneSurged || jd >= ship.PhaseEndJd) return false;
            double span = Math.Max(ship.PhaseEndJd - ship.PhaseStartJd, 1e-9);
            double u = Math.Max(0, Math.Min(1, (jd - ship.PhaseStartJd) / span));
            double surged = span / SurgeFactor;
            // A hull still easing back in from beside the lane times that from PhaseStartJd too: what is left of its sideways
            // offset is rescaled so moving the start does not pop it outward (U.SPC-T388).
            double eased = LaneRejoin(ship, jd);
            ship.PhaseStartJd = jd - u * surged;
            ship.PhaseEndJd = ship.PhaseStartJd + surged;
            double easedNow = LaneRejoin(ship, jd);
            if (easedNow > 0) ship.LaneHoldSide *= eased / easedNow;
            ship.LaneSurged = true;
            return true;
        }

        /// <summary>
        /// A pilot drops out of the lane early, on their own terms: off it right where the hull is, pinned there in the
        /// system root's frame facing <paramref name="facing"/>, the point it left kept (<see cref="Ship.LaneHold"/>) so it
        /// can get back in there (<see cref="RejoinLane"/>).
        /// </summary>
        public void DropOut(Ship ship, double jd, DVec3 facing)
        {
            if (ship.Phase != ShipPhase.Lane) return;
            var helio = StateOf(ship, jd).HelioKm;
            ship.LaneHold = LaneProgress(ship, jd);
            int root = catalog.Root.Index;
            PinAt(ship, root, helio - ephemeris.HelioAt(root, jd), facing, jd);
        }

        /// <summary>Where a hull out of its lane would get back in: the lane line at the point it left, true heliocentric km.</summary>
        public DVec3 RejoinPoint(Ship ship, double jd) => LanePoint(ship, jd, ship.LaneHold);

        /// <summary>Whether a hull at <paramref name="helioKm"/> that left its lane part-way can get back in where it left.</summary>
        public bool CanRejoin(Ship ship, DVec3 helioKm, double jd) =>
            ship.LaneLeg.Lane != null && ship.Phase != ShipPhase.Lane && ship.Battle == null && !ship.Destroyed &&
            ship.LaneHold > 0 && ship.LaneHold < 1 && (helioKm - RejoinPoint(ship, jd)).Length <= RejoinKm;

        /// <summary>
        /// Back into the lane a hull left part-way (dropped out, or pulled out by a pulse and fought clear): it eases in from
        /// where it is (<paramref name="helioKm"/>) to the point it left and rides on from there to the far ramp, like a
        /// ship that survived an ambush (<see cref="ResumeLane"/>). False if it is not near enough (<see cref="CanRejoin"/>).
        /// </summary>
        public bool RejoinLane(Ship ship, DVec3 helioKm, double jd)
        {
            if (!CanRejoin(ship, helioKm, jd)) return false;
            var hop = ship.LaneLeg;
            ship.Journey = new List<LaneHop> { hop };
            ship.JourneyIndex = 0;
            ship.FinalBody = hop.To;
            ship.DestinationBody = hop.To;
            ship.Orbit = FixedPoint(hop.From, ship.LaneFromOffset);
            ship.LaneHoldSide = helioKm - RejoinPoint(ship, jd);
            ResumeLane(ship, jd);
            return true;
        }

        /// <summary>
        /// Changes course mid-flight: from wherever the ship is now, still moving as it is, a new
        /// flip-and-burn to <paramref name="to"/>. For a burn between worlds (not a hyperlane ride).
        /// Returns false if the ship is not on such a burn.
        /// </summary>
        public bool Divert(Ship ship, int to, double jd)
        {
            if (ship.IsEscort || ship.Destroyed || ship.Battle != null || ship.Phase != ShipPhase.Transit || ship.Path == null) return false;
            if (ship.Journey != null && ship.JourneyIndex < ship.Journey.Count) return false;
            if (jd >= ship.PhaseEndJd - 1e-4) return false;
            int frame = ship.FrameBody;
            const double h = 1e-4;
            var here = BaseStateOf(ship, jd).HelioKm;
            var velocity = (BaseStateOf(ship, jd + h).HelioKm - BaseStateOf(ship, jd - h).HelioKm) / (2 * h)
                           - (ephemeris.HelioAt(frame, jd + h) - ephemeris.HelioAt(frame, jd - h)) / (2 * h);
            RefundUnflown(ship, jd);
            ship.Orbit = FixedPoint(frame, here - ephemeris.HelioAt(frame, jd));
            ship.Journey = null;
            ship.JourneyIndex = 0;
            ship.FinalBody = to;
            ship.DestinationBody = to;
            ship.Wander = false;
            Burn(ship, jd, to, () => ArrivalFor(ship, to), velocity);
            return true;
        }

        /// <summary>A flip-and-burn from the ship's current orbit (or ramp) to <paramref name="to"/>, arriving in the orbit the factory gives.</summary>
        void Burn(Ship ship, double departJd, int to, Func<ParkingOrbit> arrivalOrbit, DVec3? initialVelocity = null, bool allowConic = false)
        {
            ship.Conic = null;
            ship.GravityLossKmS = 0;
            int from = ship.Orbit.Body;
            int frame = CommonFrame(from, to);
            double keepOut = FrameKeepOutKm(frame);
            var start = RelativeTo(frame, from, departJd) + ship.Orbit.OffsetAt(departJd);
            // Leaving an orbit it moves with the orbit; changing course mid-flight it keeps its own velocity.
            var startVelocity = VelocityRelativeTo(frame, from, departJd) + (initialVelocity ?? ship.Orbit.VelocityAt(departJd));
            // Match velocities at both ends unless that path runs into something the rest-to-rest
            // one in the frame avoids; if both are blocked, try another parking orbit plane.
            ParkingOrbit arrival = null;
            bool clear = false;
            // A ship already under way cannot stop dead to start from rest: it keeps its momentum.
            bool underWay = initialVelocity != null;
            for (int attempt = 0; attempt < 6 && !clear; attempt++)
            {
                arrival = arrivalOrbit();
                clear = Plan(ship, frame, from, to, departJd, start, startVelocity, arrival, keepOut, true)
                        || (!underWay && Plan(ship, frame, from, to, departJd, start, startVelocity, arrival, keepOut, false));
            }
            // Nothing cleared: fly rest to rest, with no drift, so the ship keeps exactly to its path
            // (which goes round the frame's centre) rather than drifting through it.
            if (!clear) Plan(ship, frame, from, to, departJd, start, startVelocity, arrival, keepOut, underWay);
            if (OrbitalTransfers)
            {
                // A torch holds its straight line against the wells it climbs out of and falls into.
                ship.GravityLossKmS = GravityLoss(ship, frame, from, to);
                // A slow boat weighs a transfer orbit against that flip-and-burn and flies whichever costs less.
                if (allowConic && !underWay && UsesConics(ship))
                {
                    // Weighing the conic asks for an arrival of its own, which books a ring's holding point afresh:
                    // keep the one this flip-and-burn arrives at unless the conic is flown.
                    var ring = ship.Ring;
                    var hold = ship.HoldPoint;
                    var conic = TryConic(ship, frame, from, to, departJd, arrivalOrbit, keepOut);
                    if (conic != null) arrival = conic;
                    else
                    {
                        ship.Ring = ring;
                        ship.HoldPoint = hold;
                    }
                }
            }
            ship.PlannedConicDepartJd = double.NaN;

            ship.DepartureOrbit = ship.Orbit;
            ship.Orbit = arrival;
            ship.OriginBody = from;
            ship.Phase = ShipPhase.Transit;
            ship.FrameBody = frame;
            ship.PhaseStartJd = departJd;
            ChargePropellant(ship);
        }

        // ---------------------------------------------------------------- propellant

        /// <summary>
        /// Takes the transit just planned out of the tanks: its delta-v (<see cref="Propulsion.TransitDeltaV"/>)
        /// at the ship's present mass, by the rocket equation. If the tanks cannot cover it all, the ship
        /// sets out anyway and its drive cuts out when they run dry (<see cref="Ship.StrandJd"/>): the burn
        /// is constant thrust, so that is the same share of the way through in time as of the delta-v.
        /// </summary>
        void ChargePropellant(Ship ship)
        {
            ship.StrandJd = double.NaN;
            ship.LegPropellant = 0;
            if (!Burns(ship)) return;
            double dv = Propulsion.TransitDeltaV(ship);
            if (!(dv > 0)) return;
            double need = Propulsion.PropellantFor(ship, dv);
            if (LeakChance > 0 && !tankerAlongside && leaks.NextDouble() < LeakChance)
            {
                // A holed tank: what is left sees the ship only part of the way (a fifth to four fifths of the leg's delta-v).
                double share = 0.2 + 0.6 * leaks.NextDouble();
                double kept = Math.Min(ship.Propellant, (ship.MassTonnes - ship.Propellant) * (Propulsion.MassRatio(dv * share, ship.ExhaustVelocityKmS) - 1));
                double vented = ship.Propellant - kept;
                ship.Propellant = kept;
                Leaked?.Invoke(ship, vented, ship.PhaseStartJd);
            }
            if (need <= ship.Propellant)
            {
                ship.Propellant -= need;
                ship.LegPropellant = need;
                return;
            }
            double have = ship.Propellant, mass = ship.MassTonnes;
            double reach = Propulsion.DeltaV(ship.ExhaustVelocityKmS, mass, mass - have);
            ship.Propellant = 0;
            ship.LegPropellant = have;
            // A tanker that came out to it stays alongside to the end of the leg, topping it up: it does not run dry twice.
            if (tankerAlongside) return;
            var conic = ship.Conic;
            if (conic != null)
            {
                // A transfer orbit spends its delta-v in a burn at each end, not evenly along the way: short of the
                // departure burn it dies leaving, else it coasts the whole arc and dies in the capture burn.
                double dep = conic.DepartureBurnKmS, rest = dv - dep;
                if (reach < dep)
                    ship.StrandJd = ship.PhaseStartJd + conic.DepartureBurnDays * Math.Max(0, reach / Math.Max(dep, 1e-9));
                else
                    ship.StrandJd = ship.PhaseEndJd - conic.ArrivalBurnDays * (1 - Math.Max(0, Math.Min(1, rest > 0 ? (reach - dep) / rest : 1)));
                return;
            }
            ship.StrandJd = ship.PhaseStartJd + (ship.PhaseEndJd - ship.PhaseStartJd) * Math.Max(0, Math.Min(1, dv > 0 ? reach / dv : 1));
        }

        /// <summary>Gives back the propellant of the part of a transit that will not now be flown (a change of course).</summary>
        void RefundUnflown(Ship ship, double jd)
        {
            if (!Burns(ship) || ship.LegPropellant <= 0) return;
            double end = ship.StrandJd < ship.PhaseEndJd ? ship.StrandJd : ship.PhaseEndJd;
            var conic = ship.Conic;
            if (conic != null && conic.DeltaVKmS > 0)
            {
                // A transfer orbit burns at each end and coasts between, so what is unflown is the delta-v not yet burned.
                double paid = ConicDeltaVSpent(ship, conic, end);
                if (paid > 0) ship.Propellant += ship.LegPropellant * Math.Max(0, Math.Min(1, (paid - ConicDeltaVSpent(ship, conic, jd)) / paid));
            }
            else
            {
                double span = end - ship.PhaseStartJd;
                if (span > 0) ship.Propellant += ship.LegPropellant * Math.Max(0, Math.Min(1, (end - jd) / span));
            }
            ship.LegPropellant = 0;
            ship.StrandJd = double.NaN;
        }

        /// <summary>Delta-v a ship on <paramref name="conic"/> has burned by <paramref name="jd"/>: the departure burn, the
        /// handoff trims spread over the coast, then the capture burn, km/s.</summary>
        static double ConicDeltaVSpent(Ship ship, ConicTransfer conic, double jd)
        {
            double depEnd = ship.PhaseStartJd + conic.DepartureBurnDays, capStart = ship.PhaseEndJd - conic.ArrivalBurnDays;
            return conic.DepartureBurnKmS * Share(jd, ship.PhaseStartJd, depEnd)
                 + conic.CorrectionBurnKmS * Share(jd, depEnd, capStart)
                 + conic.ArrivalBurnKmS * Share(jd, capStart, ship.PhaseEndJd);
        }

        static double Share(double t, double from, double to) =>
            to <= from ? (t >= to ? 1 : 0) : Math.Max(0, Math.Min(1, (t - from) / (to - from)));

        /// <summary>
        /// The tanks have run dry part way through a transit: the ship drifts dead where the drive cut out,
        /// fixed in the transit's frame, still bound where it was going, and calls for a tanker
        /// (<see cref="RanDry"/>), which reaches it in a couple of days.
        /// </summary>
        void Strand(Ship ship, double jd)
        {
            int frame = ship.FrameBody;
            var here = BaseStateOf(ship, jd).HelioKm;
            ship.StrandJd = double.NaN;
            ship.LegPropellant = 0;
            ship.Orbit = FixedPoint(frame, here - ephemeris.HelioAt(frame, jd));
            ship.Path = null;
            ship.Conic = null;
            ship.Phase = ShipPhase.Parked;
            ship.StrandedInFlight = true;
            CallTanker(ship, jd);
        }

        /// <summary>Calls a tanker out to a ship that cannot move for want of propellant: it waits where it is until one comes.</summary>
        public void CallTanker(Ship ship, double jd)
        {
            ship.OutOfPropellant = true;
            ship.OutOfPropellantSinceJd = jd;
            ship.PhaseStartJd = jd;
            ship.PhaseEndJd = jd + Propulsion.TankerDays * (1 + rng.NextDouble());
            RanDry?.Invoke(ship, jd);
        }

        /// <summary>
        /// The tanker is alongside (or a passing pilot answered the call first): it fills the ship's tanks
        /// and the ship carries on to where it was bound.
        /// </summary>
        void TankerArrives(Ship ship)
        {
            double jd = ship.PhaseEndJd;
            // A ship someone already refuelled (a pilot answering the call) needs no tanker, and pays for none.
            double load = ship.PropellantFraction >= 0.25 ? 0 : Propulsion.TankerLoad(ship);
            ship.Propellant += load;
            ship.OutOfPropellant = false;
            TankerArrived?.Invoke(ship, load, jd);
            Resume(ship, jd);
        }

        /// <summary>Set while a ship a tanker has reached plans the rest of its leg (see <see cref="ChargePropellant"/>).</summary>
        bool tankerAlongside;

        /// <summary>Back under way from wherever the ship is: the rest of the leg it was flying (to its next ramp, or its destination).</summary>
        void Resume(Ship ship, double jd)
        {
            tankerAlongside = true;
            bool inFlight = ship.StrandedInFlight;
            ship.StrandedInFlight = false;
            try { ResumeLeg(ship, jd, inFlight); }
            finally { tankerAlongside = false; }
        }

        void ResumeLeg(Ship ship, double jd, bool inFlight)
        {
            if (ship.Journey != null && ship.JourneyIndex < ship.Journey.Count)
            {
                var hop = ship.Journey[ship.JourneyIndex];
                Burn(ship, jd, hop.From, () => RampOrbit(hop, hop.From, jd));
            }
            // Dead in open space short of the world its frame is kept by (a moon's hop in to its planet, or the
            // last burn from a lane's ramp in to the world itself), it still has the way there to fly.
            else if (ship.FinalBody >= 0 && (ship.FinalBody != ship.Orbit.Body || inFlight))
            {
                int to = ship.FinalBody;
                if (!inFlight)
                {
                    // Filled in port, it sets out as from any port: by the lanes (or a transfer orbit) its trip was costed for.
                    ship.DestinationBody = to;
                    ship.PhaseEndJd = jd;
                    Depart(ship);
                }
                // Dry on the way out to a jump gate: on to the gate, not into an orbit round the star.
                else if (ship.GateKm is DVec3 gate && to == catalog.Root.Index) Burn(ship, jd, to, () => FixedPoint(to, gate));
                else if (ship.Wander) Burn(ship, jd, to, () => WanderOrbit(to, ship));
                else Burn(ship, jd, to, () => ArrivalFor(ship, to));
            }
            else
            {
                ship.DestinationBody = ship.Orbit.Body;
                ship.Journey = null;
                ScheduleNext(ship, jd);
            }
        }

        /// <summary>Whether a ship flying on propellant lacks what its drive needs to get from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public bool ShortOfPropellant(Ship ship, int from, int to, double jd) =>
            Burns(ship) && Propulsion.PropellantFor(ship, EstimateTrip(ship, from, to, jd).DeltaVKmS) > ship.Propellant;

        /// <summary>
        /// How wide a transit flown in a frame gives its centre: <see cref="SunKeepOutKm"/> for a star
        /// (scaled for its luminosity, so Proxima's close planets stay reachable), a couple of radii
        /// for a planet, nothing for an empty barycentre (its stars are steered round instead).
        /// </summary>
        double FrameKeepOutKm(int frame)
        {
            var f = catalog[frame];
            if (f.IsStar) return SunKeepOutKm * Math.Sqrt(Math.Max(f.LuminositySolar, 1e-6));
            if (f.Kind == BodyKind.Barycentre) return 0;
            return f.RadiusKm * PlanetKeepOutRadii;
        }

        /// <summary>
        /// Plans the transit onto <paramref name="ship"/>. Returns false if the path would pass inside
        /// the keep-out around the frame's centre, or through either end's body or the planets they orbit.
        /// </summary>
        bool Plan(Ship ship, int frame, int from, int to, double departJd, DVec3 start, DVec3 startVelocity,
            ParkingOrbit arrival, double keepOut, bool matchVelocity)
        {
            double accel = ship.AccelG * Astro.GravityKmPerDay2;
            var v0 = matchVelocity ? startVelocity : DVec3.Zero;
            var v1 = DVec3.Zero;
            DVec3[] path = null;

            // For a flight time T, the path to where the target will be then, and how much longer
            // (positive) or shorter the drive needs to fly it than T.
            double Shortfall(double flightDays)
            {
                double arriveJd = departJd + flightDays;
                var target = RelativeTo(frame, to, arriveJd);
                // A fixed point keeps its own epoch: re-dating it would turn a point that rotates with
                // its body (a docking ring) to the wrong place.
                if (!arrival.Fixed)
                {
                    arrival.EpochJd = arriveJd;
                    arrival.PhaseAtEpoch = arrival.PhaseFacing((start - target).Normalized);
                }
                v1 = matchVelocity ? VelocityRelativeTo(frame, to, arriveJd) + arrival.VelocityAt(arriveJd) : DVec3.Zero;
                // The drift covers (v0 + v1)·T / 2 of the way; the flip-and-burn covers the rest.
                var shift = (v0 + v1) * (flightDays * 0.5);
                var end = target + arrival.OffsetAt(arriveJd) - shift;
                path = BuildPath(start, end, keepOut, Avoidances(frame, from, to, departJd, arriveJd, shift));
                return 2 * Math.Sqrt(PolylineLength(path) / accel) - flightDays;
            }

            // Intercept: bracket the flight time, then bisect. The drive can always outrun the
            // target eventually, so the shortfall turns negative for a long enough flight.
            double lo = 1e-4;
            double hi = Math.Max(2 * Math.Sqrt((RelativeTo(frame, to, departJd) - start).Length / accel), 2 * lo);
            for (int i = 0; i < 60 && Shortfall(hi) > 0; i++) hi *= 2;
            for (int i = 0; i < 60 && hi - lo > 1e-9 * hi; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Shortfall(mid) > 0) lo = mid;
                else hi = mid;
            }
            double flight = hi;
            Shortfall(flight);

            ship.DepartureVelocity = v0;
            ship.ArrivalVelocity = v1;
            ship.Path = path;
            ship.PathCumulative = Cumulative(path);
            ship.PathLength = ship.PathCumulative[path.Length - 1];
            // The path was built for exactly this arrival time; trim the drive (by a hair, once
            // converged) so the brachistochrone lands on it then.
            ship.AccelKmPerDay2 = 4 * ship.PathLength / (flight * flight);
            ship.PhaseStartJd = departJd;
            ship.PhaseEndJd = departJd + flight;

            return Clear(ship, frame, from, to, keepOut);
        }

        /// <summary>Samples a planned transit against the frame's keep-out and the bodies near its ends.</summary>
        bool Clear(Ship ship, int frame, int from, int to, double keepOut)
        {
            var obstacles = new List<int>();
            foreach (int end in new[] { from, to })
                for (int b = end; b >= 0 && b != frame; b = catalog[b].ParentIndex)
                    if (!obstacles.Contains(b)) obstacles.Add(b);
            // Every other world orbiting the frame is something to miss as well.
            foreach (int other in catalog.ChildrenOf(frame))
                if (!obstacles.Contains(other) && catalog[other].IsVisible && !catalog[other].IsStar) obstacles.Add(other);

            // Bodies sit at the ends, where the ship is slow but a planet is passed in minutes: sample
            // the first and last few percent densely, and test each segment, not just its ends. At coarse
            // detail a third as many samples: the segments between them are still tested whole.
            int samples = coarse ? 128 : 384;
            double total = ship.PhaseEndJd - ship.PhaseStartJd;
            var previous = new DVec3[obstacles.Count];
            for (int k = 0; k <= samples; k++)
            {
                double u = (double)k / samples;
                double tau = total * (u < 0.25 ? 0.12 * u / 0.25 * (u / 0.25) : u > 0.75 ? 1 - 0.12 * ((1 - u) / 0.25) * ((1 - u) / 0.25) : 0.12 + (u - 0.25) / 0.5 * 0.76);
                var offset = TransitOffsetAt(ship, tau);
                if (k > 0 && k < samples && offset.Length < keepOut * 0.8) return false;
                double jd = ship.PhaseStartJd + tau;
                var helio = ephemeris.HelioAt(frame, jd) + offset;
                for (int i = 0; i < obstacles.Count; i++)
                {
                    int b = obstacles[i];
                    var rel = helio - ephemeris.HelioAt(b, jd);
                    double limit = catalog[b].RadiusKm * 1.5;
                    if (k > 0 && k < samples && rel.Length < limit) return false;
                    if (k > 0 && SegmentDistance(previous[i], rel) < limit) return false;
                    previous[i] = rel;
                }
            }
            return true;
        }

        /// <summary>Closest approach to the origin of the segment from <paramref name="a"/> to <paramref name="b"/>.</summary>
        static double SegmentDistance(DVec3 a, DVec3 b)
        {
            var d = b - a;
            double len2 = d.LengthSquared;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, -DVec3.Dot(a, d) / len2)) : 0;
            return (a + d * t).Length;
        }

        void Arrive(Ship ship, bool landed = false)
        {
            double arriveJd = ship.PhaseEndJd;
            ship.Phase = landed ? ShipPhase.Landed : ShipPhase.Parked;
            ship.Wander = false;
            ship.Homing = false;
            ship.TripsCompleted++;
            ship.Path = null;
            ship.Conic = null;
            ship.Journey = null;
            ship.JourneyIndex = 0;
            ship.PhaseStartJd = arriveJd;
            Arrived?.Invoke(ship, arriveJd);
            if (ship.Destroyed || (IsHeld != null && IsHeld(ship))) return;
            ScheduleNext(ship, arriveJd);
        }

        /// <summary>
        /// Holds a hull fixed at <paramref name="offsetKm"/> in <paramref name="body"/>'s frame, nose on
        /// <paramref name="forward"/>, as of <paramref name="jd"/>: where a pilot let go of it, the raiders cast it
        /// off, or its captors took it. Whatever trip it had planned before is forgotten -- its departure is due
        /// now and it is bound nowhere but here -- so the next <see cref="ScheduleNext"/> plans afresh from this
        /// spot (for its new owner, if it changed hands) rather than the hull sitting frozen until the old plan's
        /// departure and then setting off on it (U.SPC-T178).
        /// </summary>
        public static void PinAt(Ship ship, int body, DVec3 offsetKm, DVec3 forward, double jd)
        {
            var v = forward.LengthSquared > 1e-12 ? forward.Normalized : DVec3.UnitX;
            var u = DVec3.Cross(Math.Abs(v.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX, v).Normalized;
            ship.Orbit = new ParkingOrbit
            {
                Body = body, Center = offsetKm, RadiusKm = 0,
                U = u, V = v, EpochJd = jd, PhaseAtEpoch = 0, RateRadPerDay = 0, Fixed = true,
            };
            ship.Phase = ShipPhase.Parked;
            ship.Path = null;
            ship.Journey = null;
            ship.JourneyIndex = 0;
            ship.Ring = null;
            ship.HoldPoint = null;
            ship.LaneHoldSide = DVec3.Zero;
            ship.Wander = false;
            ship.DestinationBody = body;
            // Bound only where it is now: a tanker that later fills it does not send it on the old trip.
            ship.FinalBody = body;
            ship.StrandedInFlight = false;
            ship.PhaseStartJd = ship.PhaseEndJd = jd;
        }

        /// <summary>Picks the next destination and the departure time: dwell, then wait for the parking orbit to face the target.</summary>
        public void ScheduleNext(Ship ship, double nowJd)
        {
            // Mid-descent or mid-climb the schedule is fixed; a queued ship (after a fight, say) just
            // takes its turn again.
            if (ship.Phase == ShipPhase.Descent || ship.Phase == ShipPhase.Ascent) return;
            if (ship.OutOfPropellant)
            {
                // Dead in space (back from a fight, or let go by its pilot): it waits on for its tanker, unless
                // somebody has filled its tanks meanwhile.
                if (ship.PropellantFraction < 0.25 || ship.Phase != ShipPhase.Parked)
                {
                    ship.PhaseEndJd = Math.Max(ship.PhaseEndJd, nowJd + 0.05);
                    return;
                }
                // Filled: it carries on to where it was bound, from where it lay (not from the frame's centre,
                // which is all its fixed point's body says of where it is).
                ship.OutOfPropellant = false;
                Resume(ship, nowJd);
                return;
            }
            if (ship.Phase == ShipPhase.Queued && ship.Ring == null)
            {
                // Lost track of its ring (it changed hands mid-queue): hold where it is, as in orbit.
                ship.Orbit = ship.HoldPoint ?? ship.Orbit;
                ship.Phase = ShipPhase.Parked;
            }
            if (ship.Phase == ShipPhase.Queued)
            {
                double entry = ship.PhaseEndJd;
                if (entry < nowJd + MinQueueDays)
                {
                    // Its turn went by while it was fighting: back to its spot off the top of the line,
                    // for the next free turn (it is drawn in the battle until then, so nothing jumps).
                    entry = Math.Max(nowJd + MinQueueDays, ship.Ring.NextEntryJd);
                    ship.Ring.NextEntryJd = entry + ship.Ring.EntryIntervalDays;
                    ship.HoldPoint = ship.Ring.HoldPoint(ship);
                }
                else ship.HoldPoint = FixedHold(ship, nowJd);
                ship.Orbit = ship.HoldPoint;
                ship.PhaseStartJd = nowJd;
                ship.PhaseEndJd = entry;
                return;
            }
            if (ship.GateKm != null)
            {
                // Bound for a gate (see InterstellarTraffic): no markets or orders to consult, just go.
                ship.DestinationBody = catalog.Root.Index;
                ship.Wander = false;
                // A ship already under way only retargets: moving its phase end would cut the transit short and
                // teleport it (U.SPC-T570). It heads for the gate when this leg ends.
                if (ship.Phase != ShipPhase.Transit && ship.Phase != ShipPhase.Lane)
                    ship.PhaseEndJd = nowJd + 0.05 + rng.NextDouble() * 0.15;
                return;
            }
            int here = ship.Orbit.Body;
            ship.WaitingForPropellant = false;
            int chosen = ChooseNext != null ? ChooseNext(ship, here, nowJd) : -1;
            double dwell = Dwell != null ? Dwell(ship, DwellDays(ship)) : DwellDays(ship);
            // A point with no size (a binary's barycentre) has no space round it to wander out into.
            // Nor a star: three to five of its radii out is deep inside the keep-out every trajectory steers clear of.
            // Someone's own little craft (U.SPC-T375) goes out sightseeing the same way.
            if ((ship.Role == ShipRole.Free || (ship.Role == ShipRole.Commuter && LocalTraffic.IsPersonal(ship.Civil))) && chosen < 0 && rng.NextDouble() < 0.55 && catalog[here].RadiusKm > 0 && !catalog[here].UsesStellarMapping)
            {
                // A free ship's whim: out to somewhere new nearby rather than to another world.
                ship.Wander = true;
                ship.DestinationBody = here;
                ship.Cargo = null;
                ship.PhaseEndJd = nowJd + dwell;
                return;
            }
            if (chosen == here)
            {
                ship.DestinationBody = here;
                ship.PhaseEndJd = nowJd + Math.Max(dwell, 0.05);
                return;
            }
            // A system with nowhere to go (only stars round a barycentre): hold here rather than pick from no
            // ports. A patrol goes by its route, not ports.
            bool nowhere = ship.Role != ShipRole.Patrol && (ship.Ports == null || ship.Ports.Length == 0);
            int next = chosen >= 0 ? chosen : nowhere ? here : ChooseDestination(ship, here, nowJd);
            if (next == here)
            {
                ship.DestinationBody = here;
                ship.PhaseEndJd = nowJd + Math.Max(dwell, 0.05);
                return;
            }
            // The fleet's own pick (a free ship's whim, a trader with no work) it cannot reach on what is in its
            // tanks: a few more picks, for somewhere it can.
            for (int k = 0; k < 6 && chosen < 0 && !nowhere && next != here && ShortOfPropellant(ship, here, next, nowJd); k++)
                next = ChooseDestination(ship, here, nowJd);
            if (next == here)
            {
                ship.DestinationBody = here;
                ship.PhaseEndJd = nowJd + Math.Max(dwell, 0.05);
                return;
            }
            if (ShortOfPropellant(ship, here, next, nowJd) && ForceTopUp != null && CanRefuelAt != null && CanRefuelAt(ship, here))
                ForceTopUp(ship, here, nowJd);
            if (ShortOfPropellant(ship, here, next, nowJd))
            {
                // Not enough in the tanks for the trip. In port, it waits for propellant to be had; anywhere
                // else, with next to nothing left, it calls a tanker; with some, it sets out and gets as far as it can.
                if (CanRefuelAt != null && CanRefuelAt(ship, here))
                {
                    ship.DestinationBody = here;
                    ship.WaitingForPropellant = true;
                    ship.PhaseEndJd = nowJd + Math.Max(dwell, 0.5);
                    return;
                }
                if (ship.PropellantFraction < 0.01 && ship.Phase == ShipPhase.Parked)
                {
                    ship.DestinationBody = next;
                    ship.FinalBody = next;
                    ship.Journey = null;
                    CallTanker(ship, nowJd);
                    return;
                }
            }
            ship.DestinationBody = next;
            if (ChooseNext == null) ship.Cargo = CargoFor(ship, here);

            double windowJd = nowJd + dwell;
            ship.PlannedConicDepartJd = double.NaN;
            ship.PlannedDirect = false;
            // A slow boat waits in port for its launch window when a transfer orbit then beats flying now.
            if (UsesConics(ship))
            {
                if (PlanWindow(ship, here, next, windowJd, out double windowDepartJd))
                {
                    ship.PhaseEndJd = windowDepartJd;
                    return;
                }
                // No transfer orbit beats flying now: remember that, so the departure does not weigh it again.
                ship.PlannedConicDepartJd = windowJd;
                ship.PlannedConicDays = -1;
            }
            var toTarget = (FirstLegTarget(here, next, windowJd) - ephemeris.HelioAt(here, windowJd)).Normalized;
            double want = ship.Orbit.PhaseFacing(toTarget);
            double have = ship.Orbit.PhaseAt(windowJd);
            double delta = (want - have) % Astro.TwoPi;
            if (delta < 0) delta += Astro.TwoPi;
            // Around a station (no real gravity) or on a fixed point, the "orbit" barely turns: just go.
            double wait = ship.Orbit.RateRadPerDay > 0 && !ship.Orbit.Fixed ? delta / ship.Orbit.RateRadPerDay : 0;
            ship.PhaseEndJd = windowJd + (wait > MaxFacingWaitDays ? 0 : wait);
        }

        /// <summary>A queued ship's current spot, frozen into a new holding point to ease in from.</summary>
        ParkingOrbit FixedHold(Ship ship, double jd)
        {
            var at = RingState(ship, jd).HelioKm - ephemeris.Helio(ship.Ring.Body);
            var hold = ship.Ring.Point(at.Length);
            // Keep it on the ship's own parallel and meridian, not the ring's.
            var north = BodyOrientation.MapNorth(catalog[ship.Ring.Body]);
            double lat = Math.Asin(Math.Max(-1, Math.Min(1, DVec3.Dot(at.Normalized, north))));
            double phase = Math.Atan2(DVec3.Dot(at, hold.V), DVec3.Dot(at, hold.U));
            hold.Center = north * (at.Length * Math.Sin(lat));
            hold.RadiusKm = at.Length * Math.Cos(lat);
            hold.EpochJd = jd;
            hold.PhaseAtEpoch = phase;
            return hold;
        }

        int ChooseDestination(Ship ship, int here, double jd)
        {
            switch (ship.Role)
            {
                case ShipRole.Patrol:
                    ship.RouteIndex = (ship.RouteIndex + 1) % ship.Route.Length;
                    if (ship.Route[ship.RouteIndex] == here)
                        ship.RouteIndex = (ship.RouteIndex + 1) % ship.Route.Length;
                    return ship.Route[ship.RouteIndex];

                case ShipRole.Miner:
                // An ore tug with no belt works to send it (U.SPC-T498) shuttles the same way: out from its field and home.
                case ShipRole.Tug:
                // So does a works' freighter with no works to load it (U.SPC-T523): out to its depot and home.
                case ShipRole.Hauler:
                    if (here != ship.Ports[0]) return ship.Ports[0];
                    return ship.Ports.Length > 1 ? ship.Ports[1 + rng.Next(ship.Ports.Length - 1)] : here;

                case ShipRole.Free:
                    return Whim(ship, here, jd);

                case ShipRole.Commuter:
                {
                    // A bus keeps to its line, passing over a stop it should not call at (U.SPC-T375); a taxi or
                    // someone's own craft goes where it fancies about its neighbourhood, as a free ship does.
                    if (ship.Route == null || ship.Route.Length == 0) return Whim(ship, here, jd);
                    for (int k = 0; k < ship.Route.Length; k++)
                    {
                        ship.RouteIndex = (ship.RouteIndex + 1) % ship.Route.Length;
                        int stop = ship.Route[ship.RouteIndex];
                        if (stop != here && (Shuns == null || !Shuns(ship, stop))) return stop;
                    }
                    return here;
                }

                default:
                {
                    // Prefer nearby ports: weight 1 / (0.3 AU + distance).
                    var herePos = ephemeris.HelioAt(here, jd);
                    double total = 0;
                    var weights = new double[ship.Ports.Length];
                    for (int i = 0; i < ship.Ports.Length; i++)
                    {
                        if (ship.Ports[i] == here) continue;
                        double au = (ephemeris.HelioAt(ship.Ports[i], jd) - herePos).Length / Astro.AuKm;
                        weights[i] = 1.0 / (0.3 + au);
                        total += weights[i];
                    }
                    double pick = rng.NextDouble() * total;
                    for (int i = 0; i < weights.Length; i++)
                    {
                        pick -= weights[i];
                        if (weights[i] > 0 && pick <= 0) return ship.Ports[i];
                    }
                    return ship.Ports[0] == here ? ship.Ports[Math.Min(1, ship.Ports.Length - 1)] : ship.Ports[0];
                }
            }
        }

        /// <summary>Somewhere a free ship (or a taxi, or someone's own craft) fancies, mostly close: weight
        /// 1 / (0.05 AU + distance)², so neighbouring moons and planets come up far more often than the far side of the
        /// system. Never a place it shuns; with nowhere else, it stays put.</summary>
        int Whim(Ship ship, int here, double jd)
        {
            var herePos = ephemeris.HelioAt(here, jd);
            double total = 0;
            var weights = new double[ship.Ports.Length];
            for (int i = 0; i < ship.Ports.Length; i++)
            {
                if (ship.Ports[i] == here || (Shuns != null && Shuns(ship, ship.Ports[i]))) continue;
                double au = (ephemeris.HelioAt(ship.Ports[i], jd) - herePos).Length / Astro.AuKm;
                weights[i] = 1.0 / ((0.05 + au) * (0.05 + au));
                total += weights[i];
            }
            double pick = rng.NextDouble() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                pick -= weights[i];
                if (weights[i] > 0 && pick <= 0) return ship.Ports[i];
            }
            // Nowhere it would go but places it shuns: it stays put.
            return total > 0 ? ship.Ports[Array.FindLastIndex(weights, x => x > 0)] : here;
        }

        double DwellDays(Ship ship)
        {
            switch (ship.Role)
            {
                // A bus keeps a timetable of a few hours; a taxi waits for a fare; a private flight lingers longer.
                case ShipRole.Commuter:
                    return ship.Civil == CivilCraft.Bus ? 0.05 + rng.NextDouble() * 0.1
                        : ship.Civil == CivilCraft.Taxi ? 0.05 + rng.NextDouble() * 0.3
                        : 0.2 + rng.NextDouble() * 1.0;
                case ShipRole.Patrol: return 1.0 + rng.NextDouble() * 3.0;
                case ShipRole.Courier: return 0.1 + rng.NextDouble() * 0.4;
                case ShipRole.Free: return 0.1 + rng.NextDouble() * 0.6;
                case ShipRole.Miner: return 1.0 + rng.NextDouble() * 2.0;
                // An ore tug with a load latched on sets straight out; running empty it lies at the field a while.
                case ShipRole.Tug: return ship.Tow != null ? 0.05 + rng.NextDouble() * 0.1 : 0.5 + rng.NextDouble() * 1.0;
                // A works' freighter with a convoy's load aboard sails at once; empty it lies at its refinery a while.
                case ShipRole.Hauler: return ship.HasCargo ? 0.05 + rng.NextDouble() * 0.1 : 0.5 + rng.NextDouble() * 1.0;
                default: return 0.5 + rng.NextDouble() * 1.5;
            }
        }

        ParkingOrbit NewParkingOrbit(int body, Ship ship)
        {
            var b = catalog[body];
            // Military ships hold high orbits; civilian traffic sits lower and more varied.
            double radii = ship.Spec.IsMilitary ? 4.0 + rng.NextDouble() * 3.0 : 2.5 + rng.NextDouble() * 4.5;
            // Round a tiny moon or a station, a few radii is a crowded few tens of km: spread out to room for a fleet.
            double r = Math.Max(b.RadiusKm * radii, b.RadiusKm + 60 + rng.NextDouble() * 140);

            var normal = (b.SpinAxis + new DVec3(Jitter(0.12), Jitter(0.12), Jitter(0.12))).Normalized;
            var reference = Math.Abs(normal.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
            var u = DVec3.Cross(reference, normal).Normalized;
            var v = DVec3.Cross(normal, u);
            return new ParkingOrbit
            {
                Body = body,
                RadiusKm = r,
                U = u,
                V = v,
                RateRadPerDay = Kepler.CircularRate(b.Gm, r),
            };
        }

        double Jitter(double amount) => (rng.NextDouble() * 2 - 1) * amount;

        /// <summary>
        /// The frame a transit is flown in: the lowest body that both ends orbit within (or are), so
        /// Earth for Earth to the Moon, Jupiter for Io to Europa, the Sun between planetary systems.
        /// </summary>
        int CommonFrame(int a, int b)
        {
            for (int x = a; x >= 0; x = catalog[x].ParentIndex)
                for (int y = b; y >= 0; y = catalog[y].ParentIndex)
                    if (x == y) return x;
            return 0;
        }

        DVec3 RelativeTo(int frame, int body, double jd) =>
            frame == body ? DVec3.Zero : HelioAt(body, jd) - HelioAt(frame, jd);

        // Planning asks where the same bodies are at the same instants over and over (each Lambert try,
        // each intercept bisection): remember the last few thousand answers.
        readonly Dictionary<(int, double), DVec3> helioCache = new Dictionary<(int, double), DVec3>();

        DVec3 HelioAt(int body, double jd)
        {
            if (helioCache.TryGetValue((body, jd), out var p)) return p;
            if (helioCache.Count > 8192) helioCache.Clear();
            p = ephemeris.HelioAt(body, jd);
            helioCache[(body, jd)] = p;
            return p;
        }

        /// <summary>Velocity of <paramref name="body"/> relative to <paramref name="frame"/>, km/day (central difference).</summary>
        DVec3 VelocityRelativeTo(int frame, int body, double jd)
        {
            const double h = 1e-3;
            return (RelativeTo(frame, body, jd + h) - RelativeTo(frame, body, jd - h)) / (2 * h);
        }

        /// <summary>
        /// The planets that the ends' moons orbit, as spheres to steer around, in path coordinates
        /// (the arrival side is shifted like the path's end, since drift is added on top of the path).
        /// </summary>
        List<(DVec3 center, double radius)> Avoidances(int frame, int from, int to, double departJd, double arriveJd, DVec3 shift)
        {
            var list = new List<(DVec3, double)>();
            // Other worlds orbiting the frame (Io when flying Ganymede to Europa): give them a berth too.
            double mid = 0.5 * (departJd + arriveJd);
            foreach (int other in catalog.ChildrenOf(frame))
            {
                if (OnChain(other, from) || OnChain(other, to) || !catalog[other].IsVisible || catalog[other].IsStar) continue;
                list.Add((RelativeTo(frame, other, mid) - shift * 0.5, catalog[other].RadiusKm * 3));
            }
            if (catalog[frame].Kind == BodyKind.Barycentre)
                foreach (int star in catalog.ChildrenOf(frame))
                    if (catalog[star].IsStar)
                        list.Add((RelativeTo(frame, star, 0.5 * (departJd + arriveJd)), FrameKeepOutKm(star)));
            for (int b = catalog[from].ParentIndex, moon = from; b > 0 && b != frame; moon = b, b = catalog[b].ParentIndex)
                list.Add((RelativeTo(frame, b, departJd), Berth(b, moon, departJd)));
            for (int b = catalog[to].ParentIndex, moon = to; b > 0 && b != frame; moon = b, b = catalog[b].ParentIndex)
                list.Add((RelativeTo(frame, b, arriveJd) - shift, Berth(b, moon, arriveJd)));
            return list;
        }

        /// <summary>True if <paramref name="body"/> is <paramref name="end"/> or one of the bodies it orbits.</summary>
        bool OnChain(int body, int end)
        {
            for (int b = end; b >= 0; b = catalog[b].ParentIndex)
                if (b == body) return true;
            return false;
        }

        /// <summary>
        /// How wide a ship leaving or joining a moon gives its planet: a good part of the moon system,
        /// so it never skims the world, but inside the moon's own orbit so the moon stays reachable.
        /// </summary>
        double Berth(int planet, int moon, double jd)
        {
            var p = catalog[planet];
            double wide = Math.Max(p.RadiusKm * 2.5, p.MoonReachKm * 0.3);
            double moonDistance = (ephemeris.HelioAt(moon, jd) - ephemeris.HelioAt(planet, jd)).Length;
            return Math.Max(p.RadiusKm * 2.5, Math.Min(wide, moonDistance * 0.75));
        }

        /// <summary>
        /// Straight line, or a dog-leg around the frame's centre when the line would pass within
        /// <paramref name="keepOutKm"/>, then detours around any of <paramref name="avoid"/> a leg runs through.
        /// </summary>
        static DVec3[] BuildPath(DVec3 start, DVec3 end, double keepOutKm, List<(DVec3 center, double radius)> avoid = null)
        {
            var points = new List<DVec3> { start, end };
            // Swing round the frame's centre: keep adding waypoints out past the keep-out until no leg
            // cuts inside it (one is not always enough when the ends are nearly opposite each other).
            for (int pass = 0; pass < 8; pass++)
            {
                bool inserted = false;
                for (int i = 0; i + 1 < points.Count && !inserted; i++)
                {
                    var a = points[i];
                    var d = points[i + 1] - a;
                    double t = d.LengthSquared > 0 ? Math.Max(0, Math.Min(1, -DVec3.Dot(a, d) / d.LengthSquared)) : 0;
                    var closest = a + d * t;
                    if (closest.Length >= keepOutKm * 0.999 || t <= 0 || t >= 1) continue;
                    var away = closest.LengthSquared > 1 ? closest.Normalized : DVec3.Cross(d, DVec3.UnitZ).Normalized;
                    points.Insert(i + 1, away * (keepOutKm * 1.4));
                    inserted = true;
                }
                if (!inserted) break;
            }

            if (avoid != null)
            {
                foreach (var (center, radius) in avoid)
                {
                    for (int i = 0; i + 1 < points.Count; i++)
                    {
                        var a = points[i] - center;
                        var seg = points[i + 1] - points[i];
                        double len2 = seg.LengthSquared;
                        double u = len2 > 0 ? -DVec3.Dot(a, seg) / len2 : 0;
                        if (u <= 0 || u >= 1) continue;
                        var near = a + seg * u;
                        if (near.Length >= radius) continue;
                        var out1 = near.LengthSquared > 1 ? near.Normalized : DVec3.Cross(seg, DVec3.UnitZ).Normalized;
                        points.Insert(i + 1, center + out1 * (radius * 1.4));
                        break;
                    }
                }
            }
            return points.ToArray();
        }

        static double PolylineLength(DVec3[] p)
        {
            double len = 0;
            for (int i = 1; i < p.Length; i++) len += (p[i] - p[i - 1]).Length;
            return len;
        }

        static double[] Cumulative(DVec3[] p)
        {
            var c = new double[p.Length];
            for (int i = 1; i < p.Length; i++) c[i] = c[i - 1] + (p[i] - p[i - 1]).Length;
            return c;
        }

        string CargoFor(Ship ship, int origin)
        {
            if (ship.Spec.IsMilitary) return null;
            if (ship.Role == ShipRole.Courier) return "Priority mail and data cores";
            if (ship.Role == ShipRole.Miner)
                return origin == ship.Ports[0] ? (rng.Next(2) == 0 ? "Nickel-iron ore" : "Platinum-group metals") : "Empty, outbound to the belt";
            if (ship.Role == ShipRole.Tug)
                return ship.Tow != null ? $"Pushing {ship.Tow.Designation}, {ship.Tow.Tonnes:N0} t" : origin == ship.Ports[0] ? "Claws open, out for a load" : "Running empty, back to the field";

            switch (catalog[origin].Name)
            {
                case "Earth": return Pick("Machinery", "Medical supplies", "Electronics");
                case "Moon": return Pick("Helium-3", "Regolith glass");
                case "Mars": return Pick("Fabricated alloys", "Hydroponic food");
                case "Ceres": return Pick("Water ice", "Nickel-iron ore");
                case "Europa": return "Water";
                case "Ganymede": return Pick("Habitat modules", "Reactor fuel");
                case "Callisto": return "Refined metals";
                case "Titan": return Pick("Hydrocarbons", "Nitrogen");
                case "Enceladus": return "Volatiles";
                case "Titania": return "Water ice";
                case "Triton": return "Nitrogen ice";
                default: return "General freight";
            }
        }

        string Pick(params string[] options) => options[rng.Next(options.Length)];

        // ---------------------------------------------------------------- setup

        public Faction AddFaction(string name, string prefix, Rgb color)
        {
            var f = new Faction(name, prefix, color);
            factions.Add(f);
            return f;
        }

        /// <summary>Gives a faction that already exists (an interstellar polity, see <see cref="Polities"/>) a presence in this system.</summary>
        public Faction AddFaction(Faction f)
        {
            if (!factions.Contains(f)) factions.Add(f);
            return f;
        }

        public Ship AddShip(string name, ShipClass c, Faction faction, ShipRole role)
        {
            var ship = new Ship
            {
                Id = ships.Count,
                Name = faction.Prefix.Length > 0 ? faction.Prefix + " " + name : name,
                Spec = ShipClassSpec.Of(c),
                Faction = faction,
                Role = role,
                Combat = new ShipCombat(CombatSpec.RandomOf(c, rng), c),
            };
            ships.Add(ship);
            return ship;
        }

        /// <summary>
        /// Adds a ship built elsewhere than <see cref="AddShip"/> straight into this fleet's list: a jump gate's
        /// arrival or a cruise dropping out (see <see cref="GateTransit"/>, <see cref="LetGo"/>) -- the same hull
        /// that left another fleet's list, not a copy (U.SPC-T453), since every other ship's id there is its fixed
        /// index into that list (ships are never removed, only marked <see cref="Ship.Destroyed"/> in place) --
        /// shuffling an existing one out from the middle would desync every id after it.
        /// </summary>
        public void Receive(Ship ship)
        {
            ship.Id = ships.Count;
            ships.Add(ship);
        }

        /// <summary>
        /// Whether <paramref name="ship"/> itself still sits in this very fleet's list at its own id (not a gone
        /// marker that has taken its slot, see <see cref="LetGo"/>): a tracker outside the fleet (an outlaw's
        /// captain, a sensor book) that holds a ship reference across a long game should check this before reading
        /// anything this fleet's own catalogue and ephemeris give it a position with -- once it is carried on
        /// elsewhere (U.SPC-T453), that position belongs to a different system's fleet, never this one's.
        /// </summary>
        public bool IsPresent(Ship ship) => ship != null && ship.Id >= 0 && ship.Id < ships.Count && ships[ship.Id] == ship;

        /// <summary>
        /// Takes <paramref name="ship"/> itself out of this fleet to go on somewhere else, the same object (U.SPC-T417: a
        /// hull jumping out under its own FTL drive is carried, not copied). Its slot keeps a marker of it, gone
        /// (<see cref="Ship.Tombstone"/>), so no other id here moves; escorts it leaves behind follow the marker, so the
        /// fleet's own succession picks a new leader among them as if it had gone through a gate. Its bookings here end.
        /// Returns the marker, or null if it is not in this fleet.
        /// </summary>
        public Ship LetGo(Ship ship)
        {
            if (ship == null || ship.Id < 0 || ship.Id >= ships.Count || ships[ship.Id] != ship) return null;
            var marker = ship.Tombstone();
            ships[ship.Id] = marker;
            foreach (var s in ships)
                if (s.Leader == ship) s.Leader = marker;
            ship.Leader = null;
            ship.Battle = null;
            ship.GateKm = null;
            ship.StakeoutKm = null;
            ship.Route = null;
            ship.RouteIndex = 0;
            return marker;
        }

        /// <summary>
        /// Creates a ship of a class/design (see <see cref="AddShip"/>) and parks it at an exact
        /// body-relative offset facing <paramref name="forward"/>, for a dev/sandbox spawn rather than
        /// a real yard -- parked the same way a jump gate's arrival parks the hull at the far gate
        /// (see <see cref="GateTransit.TryPerform"/>). Free role with every visible body as a port, so
        /// it behaves like any independent ship if ever released from piloting.
        /// </summary>
        public Ship SpawnAt(string name, ShipClass c, Faction faction, int frameBody, DVec3 offsetKm, DVec3 forward, double jd)
        {
            var ship = AddShip(name, c, faction, ShipRole.Free);
            var anywhere = new List<int>();
            foreach (var b in catalog.Bodies)
                if (b.IsVisible && !b.IsStar) anywhere.Add(b.Index);
            ship.Ports = anywhere.ToArray();

            var v = forward.LengthSquared > 1e-12 ? forward.Normalized : DVec3.UnitX;
            var u = DVec3.Cross(Math.Abs(v.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX, v).Normalized;
            ship.Orbit = new ParkingOrbit
            {
                Body = frameBody, RadiusKm = 0, Center = offsetKm, U = u, V = v,
                EpochJd = jd, PhaseAtEpoch = 0, RateRadPerDay = 0, Fixed = true,
            };
            ship.Phase = ShipPhase.Parked;
            ship.PhaseStartJd = jd;
            // Holding here: if nothing holds it (no pilot, no battle), the next update picks a real trip
            // rather than departing for no destination at all.
            ship.DestinationBody = frameBody;
            ship.PhaseEndJd = jd;
            return ship;
        }

        public void AddEscort(Ship leader, Ship escort, DVec3 slot)
        {
            // The leader's own first muster (never reassigned since, even if it later ends up riding as
            // somebody else's escort after a merge) names the group for as long as any of today's members
            // are in it (see Ship.BandId, U.SPC-T363).
            if (leader.BandId < 0) leader.BandId = leader.Id;
            escort.Leader = leader;
            escort.BandId = leader.BandId;
            escort.FormationSlot = slot;
            escort.Role = ShipRole.Escort;
        }

        /// <summary>Parks a ship at a body with a randomised departure so a fresh fleet does not move in lockstep.</summary>
        public void Station(Ship ship, int body, double jd) => Station(ship, body, jd, null);

        /// <summary>As <see cref="Station(Ship, int, double)"/>, in a given orbit (a new hull leaving its yard).</summary>
        public void Station(Ship ship, int body, double jd, ParkingOrbit orbit)
        {
            ship.Ring = null;
            ship.HoldPoint = null;
            if (orbit != null)
            {
                ship.Orbit = new ParkingOrbit
                {
                    Body = body, RadiusKm = orbit.RadiusKm, U = orbit.U, V = orbit.V, Center = orbit.Center,
                    EpochJd = jd, PhaseAtEpoch = orbit.PhaseAt(jd) - 0.002, RateRadPerDay = orbit.RateRadPerDay,
                };
                ship.Phase = ShipPhase.Parked;
                ship.PhaseStartJd = jd;
                ScheduleNext(ship, jd + 0.2 + rng.NextDouble() * 0.5);
                return;
            }
            ship.Orbit = NewParkingOrbit(body, ship);
            ship.Orbit.EpochJd = jd;
            ship.Orbit.PhaseAtEpoch = rng.NextDouble() * Astro.TwoPi;
            ship.Phase = ShipPhase.Parked;
            ship.PhaseStartJd = jd;
            ScheduleNext(ship, jd + rng.NextDouble() * 2.0);
        }

        int Body(string name) => catalog[name].Index;

        int[] Bodies(params string[] names)
        {
            var result = new int[names.Length];
            for (int i = 0; i < names.Length; i++) result[i] = Body(names[i]);
            return result;
        }

        /// <summary>
        /// The default traffic: three navies patrolling with carrier and battleship groups, plus
        /// freighters, ore haulers and couriers working the ports. The schedule is pre-rolled for
        /// a few weeks so ships start spread along their routes instead of all parked.
        /// </summary>
        public static Fleet CreateDefault(Ephemeris ephemeris, double jd, int seed = 1977)
        {
            var fleet = new Fleet(ephemeris, seed);
            double startJd = jd - 45;

            var sol = fleet.AddFaction("Sol Federation", "SFS", new Rgb(0.35f, 0.65f, 1f));
            var belt = fleet.AddFaction("Belt Consortium", "BCS", new Rgb(1f, 0.6f, 0.25f));
            var reach = fleet.AddFaction("Outer Reach Compact", "ORS", new Rgb(0.72f, 0.5f, 1f));
            var civil = fleet.AddFaction("Independent", "MV", new Rgb(0.85f, 0.85f, 0.78f));

            fleet.Group(sol, startJd, ShipClass.Carrier, "Resolute", new[] { "Earth", "Moon", "Mars" },
                (ShipClass.Battleship, "Dauntless"), (ShipClass.Cruiser, "Aegis"), (ShipClass.Cruiser, "Halberd"),
                (ShipClass.Destroyer, "Kestrel"), (ShipClass.Destroyer, "Merlin"));
            fleet.Group(sol, startJd, ShipClass.Battleship, "Vanguard", new[] { "Mars", "Ceres", "Earth" },
                (ShipClass.Cruiser, "Lance"), (ShipClass.Destroyer, "Swift"), (ShipClass.Destroyer, "Talon"));
            fleet.Group(belt, startJd, ShipClass.Carrier, "Tycho Ascendant", new[] { "Ceres", "Ganymede", "Callisto", "Europa" },
                (ShipClass.Battleship, "Iron Ledger"), (ShipClass.Cruiser, "Ore Hawk"), (ShipClass.Cruiser, "Pallas"),
                (ShipClass.Destroyer, "Flint"));
            fleet.Group(belt, startJd, ShipClass.Cruiser, "Hygiea", new[] { "Ceres", "Mars" },
                (ShipClass.Destroyer, "Shard"), (ShipClass.Destroyer, "Spall"));
            fleet.Group(reach, startJd, ShipClass.Battleship, "Long Night", new[] { "Titan", "Titania", "Triton" },
                (ShipClass.Cruiser, "Umbra"), (ShipClass.Cruiser, "Perihelion"), (ShipClass.Destroyer, "Wisp"));
            fleet.Group(reach, startJd, ShipClass.Carrier, "Endurance", new[] { "Triton", "Charon", "Titan", "Ganymede" },
                (ShipClass.Cruiser, "Aphelion"), (ShipClass.Destroyer, "Rime"), (ShipClass.Destroyer, "Hoarfrost"));

            var ports = fleet.Bodies("Earth", "Moon", "Mars", "Ceres", "Europa", "Ganymede", "Callisto", "Titan", "Enceladus", "Titania", "Triton");
            var innerPorts = fleet.Bodies("Earth", "Moon", "Mars", "Ceres", "Ganymede");
            string[] freighterNames =
            {
                "Bountiful Harvest", "Kepler's Due", "Slow Boat", "Red Ledger", "Ice Mule", "Long Haul", "Patience",
                "Margin of Error", "Cold Comfort", "Ballast", "Heavy Lifting", "Second Wind", "Tin Can", "Far Shore",
            };
            Faction[] traderFactions = { civil, civil, sol, belt, reach };
            for (int i = 0; i < freighterNames.Length; i++)
            {
                var f = traderFactions[i % traderFactions.Length];
                var ship = fleet.AddShip(freighterNames[i], ShipClass.Freighter, f, ShipRole.Trader);
                ship.Ports = i % 3 == 0 ? ports : innerPorts;
                fleet.Station(ship, ship.Ports[fleet.rng.Next(ship.Ports.Length)], startJd);
            }

            string[] minerNames = { "Pickaxe", "Rockhound", "Prospect", "Tailings", "Motherlode", "Seam" };
            var minerPorts = fleet.Bodies("Ceres", "Mars", "Earth", "Moon");
            foreach (var name in minerNames)
            {
                var ship = fleet.AddShip(name, ShipClass.Miner, belt, ShipRole.Miner);
                ship.Ports = minerPorts;
                fleet.Station(ship, minerPorts[fleet.rng.Next(minerPorts.Length)], startJd);
            }

            string[] courierNames = { "Quicksilver", "Hermes", "Dispatch", "Lightfoot" };
            foreach (var name in courierNames)
            {
                var ship = fleet.AddShip(name, ShipClass.Courier, civil, ShipRole.Courier);
                ship.Ports = ports;
                fleet.Station(ship, ports[fleet.rng.Next(ports.Length)], startJd);
            }

            // The Consortium's ore tugs (U.SPC-T498): every class a real hull in the fleet. With no belt works here to send
            // them, they push metal from the field round Psyche in to the refinery at Ceres and go back for more.
            var tugPorts = fleet.Bodies("Psyche", "Ceres");
            foreach (var name in new[] { "Bulldog", "Sisyphus" })
            {
                var ship = fleet.AddShip(name, ShipClass.OreTug, belt, ShipRole.Tug);
                ship.Ports = tugPorts;
                fleet.Station(ship, tugPorts[0], startJd);
            }

            fleet.Update(jd);
            return fleet;
        }

        public static DVec3 SlotFor(int index) => EscortSlots[index % EscortSlots.Length] * (1 + index / EscortSlots.Length * 0.6);

        static readonly DVec3[] EscortSlots =
        {
            new DVec3(0, 0.45, -1.7),
            new DVec3(-1.5, 0.05, 0.2),
            new DVec3(1.5, 0.05, 0.2),
            new DVec3(-2.6, -0.25, 1.3),
            new DVec3(2.6, -0.25, 1.3),
        };

        /// <summary>
        /// Rough flight time between two bodies for planning: the brachistochrone over their current
        /// separation, plus a day of manoeuvring.
        /// </summary>
        public double EstimateTravelDays(Ship ship, int from, int to, double jd) => EstimateTrip(ship, from, to, jd).Days;

        /// <summary>A planned burn's delta-v runs this much over the straight flip-and-burn (dog-legs round worlds), plus <see cref="LegMatchKmS"/>.</summary>
        public const double LegMargin = 1.2;

        /// <summary>Delta-v a planner allows per burn for matching velocity with its parking orbits and ramps, km/s.</summary>
        public const double LegMatchKmS = 3;

        /// <summary>
        /// A trip as a planner sees it: the days of <see cref="EstimateTravelDays"/>, plus the delta-v its own
        /// drive supplies (each burn's 2·√(a·d) over the straight distance, with a margin for dog-legs and
        /// velocity matching) and the lanes it rides. Hyperlane rides cost no delta-v; they are tolled instead.
        /// </summary>
        public TripEstimate EstimateTrip(Ship ship, int from, int to, double jd) => EstimateTrip(ship, from, to, jd, out _);

        /// <summary>
        /// As <see cref="EstimateTrip(Ship, int, int, double)"/>, and whether a slow boat would rather fly
        /// straight across on a transfer orbit than pay the lanes' tolls (U.SPC-T95). With
        /// <see cref="OrbitalTransfers"/> on, each burn also carries its gravity correction, and a
        /// low-thrust hauler prices each leg as the cheaper of the flip-and-burn and a transfer orbit
        /// (its launch window and flight time against its propellant, at the hull's time value).
        /// </summary>
        public TripEstimate EstimateTrip(Ship ship, int from, int to, double jd, out bool direct)
        {
            direct = false;
            double accel = ship.AccelG * Astro.GravityKmPerDay2;
            bool conics = UsesConics(ship);
            double deltaV = 0, cost = 0;
            double Burn(double km, int frame, LegEnd start, LegEnd end)
            {
                double days = 2 * Math.Sqrt(Math.Max(km, 1) / accel);
                double dv = accel * days / Astro.SecondsPerDay * LegMargin + LegMatchKmS;
                if (OrbitalTransfers) dv += GravityLossEstimate(ship, frame, start, end, days);
                if (conics && BestConic(ship, frame, start, end, jd, out double conicDays, out double conicDv)
                    && LegCost(ship, conicDays, conicDv) < LegCost(ship, days, dv))
                {
                    days = conicDays;
                    dv = conicDv;
                }
                deltaV += dv;
                cost += LegCost(ship, days, dv);
                return days;
            }
            if (Lanes != null)
            {
                int a = Lanes.SystemOf(from), b = Lanes.SystemOf(to);
                var route = a != b ? Lanes.RouteFor(ship, a, b, jd) : null;
                if (route != null)
                {
                    int first = CommonFrame(from, a), last = CommonFrame(b, to);
                    double days = Burn((Lanes.RampHelio(route[0].Lane, a, jd) - ephemeris.HelioAt(from, jd)).Length,
                        first, BodyEnd(first, from, ship, jd), RampEnd(a));
                    double rideDays = 0;
                    for (int i = 0; i < route.Count; i++)
                    {
                        double ride = Lanes.RideDays(ship, route[i].Lane, Lanes.LaneLengthKm(route[i].Lane, jd));
                        days += ride;
                        rideDays += ride;
                        if (i + 1 < route.Count) days += Burn(Lanes.RampRadiusKm(route[i].To) * 1.5, route[i].To, RampEnd(route[i].To), RampEnd(route[i].To));
                    }
                    days += Burn((Lanes.RampHelio(route[route.Count - 1].Lane, b, jd) - ephemeris.HelioAt(to, jd)).Length,
                        last, RampEnd(b), BodyEnd(last, to, null, jd));
                    var viaLanes = new TripEstimate(days + 0.3 + RingOverheadDays(ship, to, jd + days), deltaV, route.Count);
                    if (!conics) return viaLanes;
                    // A slow boat may sooner wait for a window and fall across than pay the tolls.
                    // The burns are priced in cost; the time on the lanes themselves and in the rings is not, so add it here.
                    double laneCost = cost + route.Count * Economy.LaneToll(ship.Spec.Class, ship.MassTonnes) + (rideDays + viaLanes.Days - days) * TimeValueOf(ship);
                    int frame = CommonFrame(from, to);
                    if (BestConic(ship, frame, BodyEnd(frame, from, ship, jd), BodyEnd(frame, to, null, jd), jd, out double straightDays, out double straightDv)
                        && LegCost(ship, straightDays, straightDv) < laneCost)
                    {
                        direct = true;
                        double total = straightDays + 1.0;
                        return new TripEstimate(total + RingOverheadDays(ship, to, jd + total), straightDv, 0);
                    }
                    return viaLanes;
                }
            }
            int common = CommonFrame(from, to);
            double d = (ephemeris.HelioAt(to, jd) - ephemeris.HelioAt(from, jd)).Length;
            double flight = Burn(d, common, BodyEnd(common, from, ship, jd), BodyEnd(common, to, null, jd)) + 1.0;
            return new TripEstimate(flight + RingOverheadDays(ship, to, jd + flight), deltaV, 0);
        }

        // ---------------------------------------------------------------- transfer orbits (U.SPC-T95)

        /// <summary>
        /// Real orbital mechanics for traffic (U.SPC-T95). On, every ship flies the trajectory its drive
        /// suits: a torch keeps its flip-and-burn and pays a gravity correction for holding its line
        /// through the wells at either end and the frame's own; a low-thrust hauler weighs that against a
        /// transfer orbit under the frame's gravity (a Lambert conic, patched onto escape and capture
        /// hyperbolas at worlds with a sphere of influence) and flies whichever costs its owner less,
        /// waiting in port for a launch window when that pays. Off, the classic all-flip-and-burn fleet.
        /// </summary>
        public bool OrbitalTransfers;

        /// <summary>A drive of at least this (g) is a torch: it flies flip-and-burns everywhere.</summary>
        public const double TorchAccelG = 0.25;

        /// <summary>The longest a slow boat will wait in port for a launch window, days.</summary>
        public const double MaxWindowWaitDays = 800;

        /// <summary>How many times the ship's distance from a world its sphere of influence must reach for the trip to be flown as a patched conic.</summary>
        public const double PatchFactor = 2;

        /// <summary>Cargo under a supply contract is wanted urgently: its hull's time is worth this many times more.</summary>
        public const double UrgentCargoFactor = 4;

        /// <summary>Flight times a slow boat tries, as fractions of the leg's Hohmann time.</summary>
        static readonly double[] TransferTimeFactors = { 0.15, 0.22, 0.3, 0.45, 0.6, 0.8, 1.0, 1.2 };

        /// <summary>The fewer flight times a window search first sweeps its whole span with.</summary>
        static readonly double[] CoarseTimeFactors = { 0.15, 0.3, 0.6, 1.0 };

        public static DriveKind DriveOf(Ship ship) => ship.Spec.AccelG >= TorchAccelG ? DriveKind.Torch : DriveKind.LowThrust;

        // An ore tug works to its operation's timetable, laden or not (U.SPC-T498): it never waits on a launch window.
        bool UsesConics(Ship ship) => OrbitalTransfers && !ship.Spec.IsMilitary && DriveOf(ship) == DriveKind.LowThrust && ship.Role != ShipRole.Tug && ship.Role != ShipRole.Hauler &&
                                      ship.Role != ShipRole.Colonist && !NavySupply(ship);  // nor does a colony ship (U.SPC-T524): it flies to its voyage's timetable

        // Nor does a navy's supply ship (U.SPC-T525), at its base or on its way to a war fleet that is waiting on it.
        static bool NavySupply(Ship ship) => ship.Role == ShipRole.Supply || ship.Voyage?.Purpose == VoyagePurpose.Supply;

        /// <summary>
        /// What a day of the hull's time is worth to its owner (credits) when it trades time for propellant:
        /// its crew's days (<see cref="Economy.RunningCostPerDay"/>), what its cargo cost (money tied up on
        /// the way, 0.3% a day), and <see cref="UrgentCargoFactor"/> times as much for contracted supplies.
        /// An empty or cheaply laden hauler's time is worth little, so it takes the slow boat.
        /// </summary>
        public static double TimeValuePerDay(Ship ship)
        {
            double v = Economy.RunningCostPerDay(ship.Spec.Class);
            if (ship.HasCargo) v += 0.003 * Math.Max(0, ship.CargoCost);
            if (ship.HasCargo && ship.CargoBonus > 0) v *= UrgentCargoFactor;
            return v;
        }

        /// <summary>Overrides what a day of a hull's time is worth to its planner; null for <see cref="TimeValuePerDay"/>.</summary>
        public Func<Ship, double> TimeValue;

        double TimeValueOf(Ship ship) => TimeValue != null ? Math.Max(0, TimeValue(ship)) : TimeValuePerDay(ship);

        /// <summary>What a leg costs the ship's owner: its days at its time value and its propellant at the going rate.</summary>
        public double LegCost(Ship ship, double days, double deltaVKmS) =>
            Math.Max(0, days) * TimeValueOf(ship) + Propulsion.PropellantFor(ship, Math.Max(0, deltaVKmS)) * Propulsion.PropellantBasePrice;

        /// <summary>The frame's gravitational parameter in km³/day².</summary>
        double FrameMu(int frame) => Math.Max(0, catalog[frame].Gm) * Astro.SecondsPerDay * Astro.SecondsPerDay;

        /// <summary>The pole a frame's transfers go round prograde about: the ecliptic's for a star, the body's spin axis otherwise.</summary>
        DVec3 FrameNormal(int frame)
        {
            var f = catalog[frame];
            return f.IsStar || f.Kind == BodyKind.Barycentre ? DVec3.UnitZ : f.SpinAxis;
        }

        /// <summary>
        /// The world whose well a ship at <paramref name="body"/> climbs out of to fly free in
        /// <paramref name="frame"/>: the body on its chain that orbits the frame directly (Earth for the
        /// Moon in the Sun's frame), or -1 if it is the frame itself.
        /// </summary>
        int WellOf(int frame, int body)
        {
            if (body == frame) return -1;
            int b = body;
            while (b >= 0 && catalog[b].ParentIndex != frame) b = catalog[b].ParentIndex;
            return b;
        }

        /// <summary>A world's sphere of influence against the frame it orbits, km, where it is now.</summary>
        double SoiKm(int frame, int well, double jd) => Conics.SoiRadius(RelativeTo(frame, well, jd).Length, catalog[well].Gm, catalog[frame].Gm);

        /// <summary>The parking orbit radius a planner assumes at a world it has not yet reached, km.</summary>
        double NominalParkingKm(int body)
        {
            var b = catalog[body];
            return Math.Max(b.RadiusKm * 4.75, b.RadiusKm + 160);
        }

        /// <summary>A parking orbit the planner assumes at a world (equatorial, at <see cref="NominalParkingKm"/>), for weighing windows.</summary>
        ParkingOrbit NominalOrbit(int body)
        {
            var b = catalog[body];
            var normal = b.SpinAxis.LengthSquared > 0 ? b.SpinAxis.Normalized : DVec3.UnitZ;
            var u = DVec3.Cross(Math.Abs(normal.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX, normal).Normalized;
            double r = NominalParkingKm(body);
            return new ParkingOrbit { Body = body, RadiusKm = r, U = u, V = DVec3.Cross(normal, u), RateRadPerDay = Kepler.CircularRate(Math.Max(b.Gm, 0), r) };
        }

        /// <summary>
        /// Solves the transfer orbit from <paramref name="departure"/> (at <paramref name="from"/>) to
        /// <paramref name="arrival"/> (at <paramref name="to"/>) leaving at <paramref name="departJd"/> and
        /// taking <paramref name="days"/>, in <paramref name="frame"/>: a Lambert conic between the ends,
        /// patched onto an escape hyperbola where the ship leaves a world whose sphere of influence is well
        /// outside its orbit (and a capture hyperbola where it arrives at one), and the burns at each end.
        /// A free (non-fixed) arrival orbit is phased so the ship circularises where the capture leaves it.
        /// Returns null when there is no such orbit, or its legs inside the spheres would overlap.
        /// </summary>
        ConicTransfer SolveConic(Ship ship, int frame, int from, int to, double departJd, double days, ParkingOrbit departure, ParkingOrbit arrival)
        {
            double mu = FrameMu(frame);
            if (!(mu > 0) || !(days > 0)) return null;
            const double day = Astro.SecondsPerDay;
            double arriveJd = departJd + days;

            var startRel = RelativeTo(frame, from, departJd) + departure.OffsetAt(departJd);
            var startVel = (from == frame ? DVec3.Zero : VelocityRelativeTo(frame, from, departJd)) + departure.VelocityAt(departJd);
            int esc = WellOf(frame, from);
            DVec3 escR = DVec3.Zero, escV = DVec3.Zero;
            double escSoi = 0, rpDep = 0;
            bool patchedOut = false;
            if (esc >= 0 && catalog[esc].Gm > 0)
            {
                escR = RelativeTo(frame, esc, departJd);
                escV = VelocityRelativeTo(frame, esc, departJd);
                escSoi = SoiKm(frame, esc, departJd);
                rpDep = (startRel - escR).Length;
                patchedOut = rpDep > 0 && escSoi > rpDep * PatchFactor;
            }

            var toR = RelativeTo(frame, to, arriveJd);
            var toV = to == frame ? DVec3.Zero : VelocityRelativeTo(frame, to, arriveJd);
            int cap = WellOf(frame, to);
            DVec3 capR = DVec3.Zero, capV = DVec3.Zero;
            double capSoi = 0, rpArr = 0;
            bool patchedIn = false;
            if (cap >= 0 && catalog[cap].Gm > 0)
            {
                capR = RelativeTo(frame, cap, arriveJd);
                capV = VelocityRelativeTo(frame, cap, arriveJd);
                capSoi = SoiKm(frame, cap, arriveJd);
                rpArr = (toR + arrival.OffsetAt(arriveJd) - capR).Length;
                patchedIn = rpArr > 0 && capSoi > rpArr * PatchFactor;
            }

            var r1 = patchedOut ? escR : startRel;
            var h = DVec3.Cross(r1, patchedOut ? escV : startVel);
            var normal = h.LengthSquared > 1e-6 ? h.Normalized : FrameNormal(frame);
            if (!patchedIn && !arrival.Fixed)
            {
                // Arrive where the conic comes in: facing the incoming ship, or (in orbit round the
                // frame's own centre) a little short of opposite where it left, a near-Hohmann half turn.
                DVec3 dir;
                if (to == frame)
                {
                    var rh = r1.Normalized;
                    var n = arrival.U.LengthSquared > 0 ? DVec3.Cross(arrival.U, arrival.V).Normalized : normal;
                    dir = rh * -Math.Cos(10 * Astro.Deg2Rad) + DVec3.Cross(n, rh) * Math.Sin(10 * Astro.Deg2Rad);
                }
                else dir = (r1 - toR).Normalized;
                arrival.EpochJd = arriveJd;
                arrival.PhaseAtEpoch = arrival.PhaseFacing(dir);
            }
            var endRel = toR + arrival.OffsetAt(arriveJd);
            var endVel = toV + arrival.VelocityAt(arriveJd);
            var r2 = patchedIn ? capR : endRel;
            if (!Conics.Lambert(r1, r2, days, mu, normal, out var v1, out var v2)) return null;

            var c = new ConicTransfer { Mu = mu, Days = days };
            // The legs inside each sphere of influence, and the states where they hand over to the frame's conic.
            DVec3 exit = startRel, entry = endRel, exitVel = startVel, entryVel = endVel;
            if (patchedOut)
            {
                // The escape burn at the ship's own spot in its parking orbit, onto the hyperbola through it.
                var vInf = v1 - escV;
                var rel = startRel - escR;
                var relVel = startVel - escV;
                var nPark = DVec3.Cross(rel, relVel);
                c.Escape = Hyperbola.Through(FrameMu(esc), rel, vInf, nPark.LengthSquared > 0 ? nPark.Normalized : normal);
                if (c.Escape == null) return null;
                c.FromR0 = escR;
                c.FromV0 = escV;
                c.FromSoiKm = escSoi;
                c.ExitDays = c.Escape.TimeToRadius(escSoi);
                if (c.ExitDays > 0.45 * days) return null;
                Conics.Propagate(escR, escV, mu, c.ExitDays, out var bodyAt, out var bodyVel);
                exit = bodyAt + c.Escape.PositionAt(c.ExitDays);
                exitVel = bodyVel + c.Escape.VelocityAt(c.ExitDays);
                c.DepartureBurnKmS = (c.Escape.Q * c.Escape.PeriapsisSpeed - relVel).Length / day;
            }
            if (patchedIn)
            {
                var vInf = v2 - capV;
                if (!arrival.Fixed && to == cap && arrival.U.LengthSquared > 0)
                {
                    // Circularise where a capture from this approach bottoms out: θ∞ round from the incoming asymptote.
                    var ideal = Hyperbola.Arrival(FrameMu(cap), rpArr, vInf, DVec3.Cross(arrival.U, arrival.V).Normalized);
                    if (ideal == null) return null;
                    arrival.EpochJd = arriveJd;
                    arrival.PhaseAtEpoch = arrival.PhaseFacing(ideal.P);
                    endRel = toR + arrival.OffsetAt(arriveJd);
                    endVel = toV + arrival.VelocityAt(arriveJd);
                }
                c.Capture = Hyperbola.Through(FrameMu(cap), endRel - capR, vInf, normal);
                if (c.Capture == null) return null;
                c.ToR1 = capR;
                c.ToV1 = capV;
                c.ToSoiKm = capSoi;
                c.EntryDays = c.Capture.TimeToRadius(capSoi);
                if (c.EntryDays > 0.45 * days) return null;
                Conics.Propagate(capR, capV, mu, -c.EntryDays, out var bodyAt, out var bodyVel);
                entry = bodyAt + c.Capture.PositionAt(-c.EntryDays);
                entryVel = bodyVel + c.Capture.VelocityAt(-c.EntryDays);
                c.ArrivalBurnKmS = (c.Capture.Q * c.Capture.PeriapsisSpeed - (endVel - capV)).Length / day;
            }
            double cruise = days - c.ExitDays - c.EntryDays;
            if (cruise < 0.1 * days) return null;
            if (patchedOut || patchedIn)
            {
                // The frame's conic from handoff to handoff, and the trims that match it to each hyperbola there.
                if (!Conics.Lambert(exit, entry, cruise, mu, normal, out v1, out v2)) return null;
                c.CorrectionBurnKmS = ((patchedOut ? (v1 - exitVel).Length : 0) + (patchedIn ? (v2 - entryVel).Length : 0)) / day;
            }
            c.R0 = exit;
            c.V0 = v1;
            c.R1 = entry;
            c.V1 = v2;
            if (!patchedOut) c.DepartureBurnKmS = (v1 - startVel).Length / day;
            if (!patchedIn) c.ArrivalBurnKmS = (v2 - endVel).Length / day;

            double accel = Math.Max(1e-9, ship.AccelG * 9.80665e-3);
            c.DepartureBurnDays = c.DepartureBurnKmS / accel / day;
            c.ArrivalBurnDays = c.ArrivalBurnKmS / accel / day;
            if (c.DepartureBurnDays + c.ArrivalBurnDays > 0.5 * days) return null;
            return c;
        }

        /// <summary>The Hohmann time between the ends of a leg about its frame, days (the yardstick for the flight times tried).</summary>
        double HohmannDaysFor(int frame, int from, int to, double jd, ParkingOrbit departure, ParkingOrbit arrival)
        {
            double r1 = (RelativeTo(frame, from, jd) + departure.OffsetAt(jd)).Length;
            double r2 = (RelativeTo(frame, to, jd) + arrival.OffsetAt(jd)).Length;
            double mu = FrameMu(frame);
            return mu > 0 && r1 > 0 && r2 > 0 ? Conics.HohmannTime(mu, r1, r2) : 0;
        }

        /// <summary>
        /// Weighs a transfer orbit against the flip-and-burn just planned onto <paramref name="ship"/>, leaving
        /// now: the flight times it tries (and the one its window was planned for), each priced at
        /// <see cref="LegCost"/>; if the cheapest beats the flip-and-burn and keeps clear of everything, it
        /// becomes the transit and its arrival orbit is returned. Otherwise the flip-and-burn stands (null).
        /// </summary>
        ParkingOrbit TryConic(Ship ship, int frame, int from, int to, double departJd, Func<ParkingOrbit> arrivalOrbit, double keepOut)
        {
            double brachyCost = LegCost(ship, ship.PhaseEndJd - departJd, Propulsion.TransitDeltaV(ship));
            var arrival = arrivalOrbit();
            double hohmann = HohmannDaysFor(frame, from, to, departJd, ship.Orbit, arrival);
            if (!(hohmann > 0)) return null;
            double sincePlan = double.IsNaN(ship.PlannedConicDepartJd) ? double.PositiveInfinity : Math.Abs(ship.PlannedConicDepartJd - departJd);
            // Its window planner already judged the flip-and-burn the better buy for this leg.
            if (ship.PlannedConicDays < 0 && sincePlan <= MaxFacingWaitDays + 0.5) return null;
            bool planned = ship.PlannedConicDays > 0 && sincePlan < 1;
            var tries = new List<double>();
            if (planned) tries.AddRange(new[] { ship.PlannedConicDays, ship.PlannedConicDays * 0.8, ship.PlannedConicDays * 1.25 });
            else foreach (double f in TransferTimeFactors) tries.Add(hohmann * f);
            double bestCost = brachyCost, bestDays = 0;
            foreach (double days in tries)
            {
                var c = SolveConic(ship, frame, from, to, departJd, days, ship.Orbit, arrival);
                if (c == null) continue;
                double cost = LegCost(ship, days, c.DeltaVKmS);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestDays = days;
                }
            }
            if (bestDays <= 0) return null;
            var conic = SolveConic(ship, frame, from, to, departJd, bestDays, ship.Orbit, arrival);
            if (conic == null) return null;

            var saved = (ship.Path, ship.PathCumulative, ship.PathLength, ship.PhaseEndJd, ship.DepartureVelocity, ship.ArrivalVelocity, ship.GravityLossKmS);
            ship.Conic = conic;
            ship.GravityLossKmS = 0;
            ship.PhaseStartJd = departJd;
            ship.PhaseEndJd = departJd + conic.Days;
            ship.DepartureVelocity = conic.V0;
            ship.ArrivalVelocity = conic.V1;
            // The arc as a polyline, for whatever reads the path (its length on the helm, a diversion's check).
            const int n = 32;
            var path = new DVec3[n + 1];
            for (int i = 0; i <= n; i++) path[i] = conic.OffsetAt(conic.Days * i / n);
            ship.Path = path;
            ship.PathCumulative = Cumulative(path);
            ship.PathLength = ship.PathCumulative[n];
            if (Clear(ship, frame, from, to, keepOut)) return arrival;

            ship.Conic = null;
            (ship.Path, ship.PathCumulative, ship.PathLength, ship.PhaseEndJd, ship.DepartureVelocity, ship.ArrivalVelocity, ship.GravityLossKmS) = saved;
            return null;
        }

        /// <summary>
        /// Sets a slow boat's departure for its next trip (U.SPC-T95): over the coming synodic period
        /// (capped at <see cref="MaxWindowWaitDays"/>, and at however long waiting would cost more than
        /// flying now) it tries leaving on each of a grid of dates with each of a range of flight times,
        /// and keeps the cheapest wait plus transfer. If that beats a flip-and-burn now, it waits in port for
        /// that window and records the plan; otherwise it returns false and leaves as usual.
        /// </summary>
        bool PlanWindow(Ship ship, int here, int next, double earliestJd, out double departJd)
        {
            departJd = earliestJd;
            if (ship.Wander || ship.Orbit == null || ship.GateKm != null) return false;
            int legTo = next;
            Func<double, ParkingOrbit> arrivalAt = null;
            bool direct = false;
            DVec3 target = ephemeris.HelioAt(next, earliestJd);
            if (Lanes != null)
            {
                int a = Lanes.SystemOf(here), b = Lanes.SystemOf(next);
                var route = a != b ? Lanes.Route(a, b, earliestJd) : null;
                if (route != null)
                {
                    EstimateTrip(ship, here, next, earliestJd, out direct);
                    if (!direct)
                    {
                        var hop = route[0];
                        legTo = hop.From;
                        arrivalAt = jd => RampOrbit(hop, hop.From, jd);
                        target = Lanes.RampHelio(hop.Lane, a, earliestJd);
                    }
                }
            }
            if (arrivalAt == null) arrivalAt = jd => NominalOrbit(legTo);
            int frame = CommonFrame(here, legTo);
            double mu = FrameMu(frame);
            if (!(mu > 0)) return false;

            // What flying now by flip-and-burn would cost, for comparison.
            double accel = ship.AccelG * Astro.GravityKmPerDay2;
            double km = Math.Max(1, (target - ephemeris.HelioAt(here, earliestJd)).Length);
            double burnDays = 2 * Math.Sqrt(km / accel);
            double burnDv = accel * burnDays / Astro.SecondsPerDay * LegMargin + LegMatchKmS
                            + GravityLossEstimate(ship, frame, BodyEnd(frame, here, ship, earliestJd), BodyEnd(frame, legTo, null, earliestJd), burnDays);
            double brachyCost = LegCost(ship, burnDays, burnDv);
            double tv = Math.Max(1e-6, TimeValueOf(ship));

            // Windows come round once a synodic period of the two ends about the frame.
            double n1 = here == frame ? ship.Orbit.RateRadPerDay : MeanMotion(frame, WellOf(frame, here), earliestJd);
            var probe = arrivalAt(earliestJd);
            double n2 = legTo == frame ? (probe.Fixed ? 0 : probe.RateRadPerDay) : MeanMotion(frame, WellOf(frame, legTo), earliestJd);
            double synodic = Math.Abs(n1 - n2) > 1e-12 ? Astro.TwoPi / Math.Abs(n1 - n2) : MaxWindowWaitDays;
            double maxWait = Math.Max(0, Math.Min(Math.Min(synodic, MaxWindowWaitDays), brachyCost / tv));
            double hohmann = HohmannDaysFor(frame, here, legTo, earliestJd, ship.Orbit, probe);
            if (!(hohmann > 0)) return false;

            double bestCost = brachyCost, bestWait = -1, bestDays = 0;
            double period = !ship.Orbit.Fixed && ship.Orbit.RateRadPerDay > 0 ? Astro.TwoPi / ship.Orbit.RateRadPerDay : 0;
            void Try(double wait, double[] factors, double onlyDays = 0)
            {
                if (wait < 0 || wait > maxWait + period) return;
                double jd = earliestJd + wait;
                var arrival = arrivalAt(jd);
                foreach (double f in factors)
                {
                    double days = onlyDays > 0 ? onlyDays : hohmann * f;
                    if (onlyDays > 0 && f != factors[0]) break;
                    var c = SolveConic(ship, frame, here, legTo, jd, days, ship.Orbit, arrival);
                    if (c == null) continue;
                    double cost = wait * tv + LegCost(ship, days, c.DeltaVKmS);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestWait = wait;
                        bestDays = days;
                    }
                }
            }
            // At coarse detail (a system nobody is looking at) the grid is half as fine and the escape burn is not timed to the turn.
            int steps = coarse ? 8 : 16, refine = coarse ? 2 : 4;
            double step = maxWait / steps;
            // A coarse look over the whole span, then every flight time around the best date found.
            for (int i = 0; i <= steps; i++) Try(step * i, CoarseTimeFactors);
            if (bestWait >= 0)
            {
                double centre = bestWait;
                if (step > 0)
                    for (int i = -refine; i <= refine; i++) Try(centre + step * i / refine, TransferTimeFactors);
                else Try(centre, TransferTimeFactors);
            }
            if (bestWait < 0) return false;
            // Time the escape burn to the ship's place in its parking orbit: within one turn of it, the
            // moment it lies the right way round from where it is going.
            if (period > 0 && period < 5 && !coarse)
            {
                double centre = bestWait, days = bestDays;
                for (int i = -12; i <= 12; i++) if (i != 0) Try(centre + period * i / 24.0, TransferTimeFactors, days);
            }
            ship.PlannedConicDepartJd = earliestJd + bestWait;
            ship.PlannedConicDays = bestDays;
            ship.PlannedDirect = direct;
            departJd = ship.PlannedConicDepartJd;
            return true;
        }

        /// <summary>Mean motion (rad/day) of a world about the frame, from its distance now.</summary>
        double MeanMotion(int frame, int well, double jd)
        {
            if (well < 0) return 0;
            double r = RelativeTo(frame, well, jd).Length;
            double mu = FrameMu(frame);
            return r > 0 && mu > 0 ? Math.Sqrt(mu / (r * r * r)) : 0;
        }

        /// <summary>
        /// One end of a leg as the trip estimate sees it: a world going round the frame (whose well the
        /// ship climbs out of or falls into, from a parking orbit <see cref="Rp"/> out), or a point at
        /// <see cref="R"/> from the frame's centre, held still (a hyperlane ramp) or in circular orbit.
        /// </summary>
        readonly struct LegEnd
        {
            public readonly int Well;
            public readonly double R, Rp;
            public readonly bool Still, Patched;

            public LegEnd(int well, double r, double rp, bool still, bool patched)
            {
                Well = well;
                R = r;
                Rp = rp;
                Still = still;
                Patched = patched;
            }
        }

        /// <summary>A world (or a parking orbit round the frame's own centre) as a leg end.</summary>
        LegEnd BodyEnd(int frame, int body, Ship ship, double jd)
        {
            bool parkedHere = ship != null && ship.Orbit != null && ship.Orbit.Body == body && !ship.Orbit.Fixed && ship.Orbit.RadiusKm > 0;
            double parking = parkedHere ? ship.Orbit.RadiusKm : NominalParkingKm(body);
            if (body == frame) return new LegEnd(-1, parking, 0, false, false);
            int well = WellOf(frame, body);
            if (well < 0) return new LegEnd(-1, parking, 0, false, false);
            double r = RelativeTo(frame, well, jd).Length;
            double rp = well == body ? parking : RelativeTo(well, body, jd).Length;
            bool patched = catalog[well].Gm > 0 && SoiKm(frame, well, jd) > rp * PatchFactor;
            return new LegEnd(well, r, rp, false, patched);
        }

        /// <summary>A hyperlane ramp (a point held still off its planet) as a leg end in that planet's frame.</summary>
        LegEnd RampEnd(int system) => new LegEnd(-1, Lanes.RampRadiusKm(system), 0, true, false);

        /// <summary>
        /// A flip-and-burn's gravity correction as the estimate sees it (U.SPC-T95): climbing straight out of
        /// each end's well from rest (<see cref="Conics.GravityLossLeaving"/>), plus the frame's own pull
        /// across the flight between two worlds that orbit it.
        /// </summary>
        double GravityLossEstimate(Ship ship, int frame, LegEnd start, LegEnd end, double days)
        {
            double accel = ship.AccelG * 9.80665e-3;
            double loss = 0;
            foreach (var e in new[] { start, end })
                loss += e.Well >= 0 ? Conics.GravityLossLeaving(catalog[e.Well].Gm, e.Rp, accel) : Conics.GravityLossLeaving(catalog[frame].Gm, e.R, accel);
            if (start.Well >= 0 && end.Well >= 0)
            {
                double r = 0.5 * (start.R + end.R);
                if (r > 0) loss += Math.Max(0, catalog[frame].Gm) / (r * r) * days * Astro.SecondsPerDay;
            }
            return loss;
        }

        /// <summary>
        /// A flip-and-burn's gravity correction as flown (U.SPC-T95): the pull of the frame's centre and of
        /// every world on either end's chain, summed along the planned path, ∫ Σ μ/d² dt. A torch spends
        /// that holding its straight line instead of falling off it.
        /// </summary>
        double GravityLoss(Ship ship, int frame, int from, int to)
        {
            var bodies = new List<int>();
            if (catalog[frame].Gm > 0) bodies.Add(frame);
            foreach (int end in new[] { from, to })
                for (int b = end; b >= 0 && b != frame; b = catalog[b].ParentIndex)
                    if (!bodies.Contains(b) && catalog[b].Gm > 0) bodies.Add(b);
            double total = ship.PhaseEndJd - ship.PhaseStartJd;
            if (!(total > 0) || bodies.Count == 0) return 0;
            // Dense at the ends, where the wells are steep and the ship is slow.
            const int samples = 64;
            double loss = 0, prevTau = 0, prevG = 0;
            for (int k = 0; k <= samples; k++)
            {
                double u = (double)k / samples;
                double tau = total * (u < 0.5 ? 2 * u * u : 1 - 2 * (1 - u) * (1 - u));
                double jd = ship.PhaseStartJd + tau;
                var helio = HelioAt(frame, jd) + TransitOffsetAt(ship, tau);
                double g = 0;
                foreach (int b in bodies)
                {
                    double d = Math.Max((helio - HelioAt(b, jd)).Length, catalog[b].RadiusKm);
                    if (d > 0) g += catalog[b].Gm / (d * d);
                }
                if (k > 0) loss += 0.5 * (g + prevG) * (tau - prevTau) * Astro.SecondsPerDay;
                prevTau = tau;
                prevG = g;
            }
            return loss;
        }

        readonly Dictionary<(int, int, int, long, long, long, long, long), (double days, double dv)[]> conicOptions =
            new Dictionary<(int, int, int, long, long, long, long, long), (double days, double dv)[]>();

        /// <summary>
        /// The cheapest transfer orbit for a leg as the trip estimate sees it, priced at
        /// <see cref="LegCost"/>: a Hohmann transfer (waiting for its window when both ends are worlds
        /// going round the frame), or a faster Lambert arc leaving now. False if the frame has no gravity.
        /// </summary>
        bool BestConic(Ship ship, int frame, LegEnd start, LegEnd end, double jd, out double days, out double dv)
        {
            days = dv = 0;
            var options = ConicOptions(frame, start, end, jd);
            if (options == null || options.Length == 0) return false;
            double best = double.PositiveInfinity;
            foreach (var o in options)
            {
                double cost = LegCost(ship, o.days, o.dv);
                if (cost < best)
                {
                    best = cost;
                    days = o.days;
                    dv = o.dv;
                }
            }
            return best < double.PositiveInfinity;
        }

        static long Bucket(double km) => km > 0 ? (long)Math.Round(Math.Log(km) * 200) : 0;

        (double days, double dv)[] ConicOptions(int frame, LegEnd start, LegEnd end, double jd)
        {
            double mu = FrameMu(frame);
            double gm = catalog[frame].Gm;
            if (!(mu > 0) || start.R <= 0 || end.R <= 0) return null;
            bool worlds = start.Well >= 0 && end.Well >= 0;
            long flags = (start.Still ? 1 : 0) | (end.Still ? 2 : 0) | (start.Patched ? 4 : 0) | (end.Patched ? 8 : 0);
            var key = (frame, start.Well, end.Well, Bucket(start.R) * 4096 + Bucket(start.Rp), Bucket(end.R) * 4096 + Bucket(end.Rp),
                flags, worlds ? (long)Math.Floor(jd) : 0, 0L);
            if (conicOptions.TryGetValue(key, out var cached)) return cached;
            if (conicOptions.Count > 20000) conicOptions.Clear();

            const double day = Astro.SecondsPerDay;
            double r1 = start.R, r2 = end.R;
            double hohmann = Conics.HohmannTime(mu, r1, r2);
            double EndBurn(LegEnd e, double vInf, double vTransfer)
            {
                if (e.Patched) return Conics.EscapeBurn(catalog[e.Well].Gm, e.Rp, vInf, Conics.CircularSpeed(catalog[e.Well].Gm, e.Rp));
                return e.Still ? vTransfer : vInf;
            }
            var list = new List<(double, double)>();
            double a = 0.5 * (r1 + r2);
            double vt1 = Math.Sqrt(gm * (2 / r1 - 1 / a)), vt2 = Math.Sqrt(gm * (2 / r2 - 1 / a));
            double vc1 = Math.Sqrt(gm / r1), vc2 = Math.Sqrt(gm / r2);
            double wait = 0;
            if (worlds)
            {
                var p1 = RelativeTo(frame, start.Well, jd);
                var p2 = RelativeTo(frame, end.Well, jd);
                var n = FrameNormal(frame);
                double phase = Math.Atan2(DVec3.Dot(n, DVec3.Cross(p1, p2)), DVec3.Dot(p1, p2));
                wait = Conics.WaitForPhase(phase, Conics.HohmannLeadAngle(mu, r1, r2), Math.Sqrt(mu / (r1 * r1 * r1)), Math.Sqrt(mu / (r2 * r2 * r2)));
                if (double.IsInfinity(wait)) wait = 0;
            }
            list.Add((wait + hohmann, EndBurn(start, Math.Abs(vt1 - vc1), vt1) + EndBurn(end, Math.Abs(vc2 - vt2), vt2)));

            // Faster arcs leaving now: between the worlds where they really are, or between idealised
            // circular orbits a long arc apart (a parking orbit can be phased to suit within hours).
            foreach (double f in new[] { 0.18, 0.3, 0.5, 0.8 })
            {
                double tof = hohmann * f;
                DVec3 a1, a2, w1, w2, normal;
                if (worlds)
                {
                    a1 = RelativeTo(frame, start.Well, jd);
                    w1 = VelocityRelativeTo(frame, start.Well, jd);
                    a2 = RelativeTo(frame, end.Well, jd + tof);
                    w2 = VelocityRelativeTo(frame, end.Well, jd + tof);
                    var h = DVec3.Cross(a1, w1);
                    normal = h.LengthSquared > 0 ? h.Normalized : DVec3.UnitZ;
                }
                else
                {
                    double ang = 150 * Astro.Deg2Rad;
                    a1 = new DVec3(r1, 0, 0);
                    a2 = new DVec3(r2 * Math.Cos(ang), r2 * Math.Sin(ang), 0);
                    w1 = start.Still ? DVec3.Zero : new DVec3(0, vc1 * day, 0);
                    w2 = end.Still ? DVec3.Zero : new DVec3(-Math.Sin(ang), Math.Cos(ang), 0) * (vc2 * day);
                    normal = DVec3.UnitZ;
                }
                if (!Conics.Lambert(a1, a2, tof, mu, normal, out var v1, out var v2)) continue;
                double inf1 = (v1 - w1).Length / day, inf2 = (v2 - w2).Length / day;
                list.Add((tof, EndBurn(start, inf1, inf1) + EndBurn(end, inf2, inf2)));
            }
            var result = list.ToArray();
            conicOptions[key] = result;
            return result;
        }

        /// <summary>Where the first leg of a trip heads: the first lane's ramp, or the destination itself.</summary>
        DVec3 FirstLegTarget(int here, int next, double jd)
        {
            if (Lanes != null)
            {
                int a = Lanes.SystemOf(here), b = Lanes.SystemOf(next);
                var route = a != b ? Lanes.Route(a, b, jd) : null;
                if (route != null) return Lanes.RampHelio(route[0].Lane, a, jd);
            }
            return ephemeris.HelioAt(next, jd);
        }

        public Random Rng => rng;

        public Ship Group(Faction faction, double jd, ShipClass leaderClass, string leaderName, string[] route,
            params (ShipClass cls, string name)[] escorts)
        {
            var leader = AddShip(leaderName, leaderClass, faction, ShipRole.Patrol);
            leader.Route = Bodies(route);
            leader.RouteIndex = 0;
            for (int i = 0; i < escorts.Length; i++)
            {
                var escort = AddShip(escorts[i].name, escorts[i].cls, faction, ShipRole.Escort);
                AddEscort(leader, escort, SlotFor(i));
            }
            Station(leader, leader.Route[0], jd);
            return leader;
        }
    }
}
