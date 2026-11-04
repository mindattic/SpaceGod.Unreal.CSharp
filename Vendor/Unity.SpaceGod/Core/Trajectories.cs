using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// What a hull's drive is good for when it plans a trip (U.SPC-T95). A torch (warships, couriers,
    /// breachers, fighters) has the thrust to fly a flip-and-burn anywhere and does, paying to hold its
    /// straight line against gravity. A low-thrust hauler (freighters, ore haulers) flies coasting
    /// transfer orbits, two short burns and a long fall, whenever the time is worth less than the
    /// propellant a flip-and-burn would throw out.
    /// </summary>
    public enum DriveKind
    {
        Torch,
        LowThrust,
    }

    /// <summary>
    /// Two-body orbital mechanics for planning transfers: universal-variable Kepler propagation, a
    /// Lambert solver, Hohmann transfers and their launch windows, and the patched-conic pieces (sphere
    /// of influence, escape and capture burns, the hyperbola a ship flies out of a world's well).
    /// Every function takes any consistent units (km and seconds, or km and days with mu in km³/day²).
    /// </summary>
    public static class Conics
    {
        /// <summary>Stumpff function C(z) = (1 - cos √z) / z (its hyperbolic form for z &lt; 0).</summary>
        public static double StumpffC(double z)
        {
            // 1 - cos x as 2·sin²(x/2): exact even as √z nears 2π, where the Lambert solver works.
            if (z > 1e-6)
            {
                double h = Math.Sin(0.5 * Math.Sqrt(z));
                return 2 * h * h / z;
            }
            if (z < -1e-6)
            {
                double h = Math.Sinh(0.5 * Math.Sqrt(-z));
                return 2 * h * h / -z;
            }
            return 0.5 - z / 24 + z * z / 720;
        }

        /// <summary>Stumpff function S(z) = (√z - sin √z) / z^1.5 (its hyperbolic form for z &lt; 0).</summary>
        public static double StumpffS(double z)
        {
            if (z > 1e-6)
            {
                double s = Math.Sqrt(z);
                return (s - Math.Sin(s)) / (s * s * s);
            }
            if (z < -1e-6)
            {
                double s = Math.Sqrt(-z);
                return (Math.Sinh(s) - s) / (s * s * s);
            }
            return 1.0 / 6 - z / 120 + z * z / 5040;
        }

        /// <summary>
        /// Where a body starting at <paramref name="r0"/> moving at <paramref name="v0"/> is after
        /// <paramref name="dt"/> (negative runs it backwards) under a centre of gravitational parameter
        /// <paramref name="mu"/>: the universal Kepler equation in χ, solved by safeguarded Newton, then
        /// the Lagrange coefficients. Works for ellipses, parabolas and hyperbolas alike.
        /// </summary>
        public static void Propagate(DVec3 r0, DVec3 v0, double mu, double dt, out DVec3 r, out DVec3 v)
        {
            double r0n = r0.Length;
            if (mu <= 0 || r0n <= 0 || dt == 0)
            {
                r = r0 + v0 * dt;
                v = v0;
                return;
            }
            double sqrtMu = Math.Sqrt(mu);
            double vr0 = DVec3.Dot(r0, v0) / r0n;
            double alpha = 2 / r0n - v0.LengthSquared / mu;

            // dt(χ) climbs monotonically with χ (its slope is r/√μ > 0): bracket the root, then Newton
            // inside the bracket, falling back to bisection whenever a step would leave it.
            double Residual(double x, out double slope)
            {
                double z = alpha * x * x;
                double c = StumpffC(z), s = StumpffS(z);
                double f = r0n * vr0 / sqrtMu * x * x * c + (1 - alpha * r0n) * x * x * x * s + r0n * x - sqrtMu * dt;
                slope = r0n * vr0 / sqrtMu * x * (1 - z * s) + (1 - alpha * r0n) * x * x * c + r0n;
                return f;
            }

            double sign = Math.Sign(dt);
            double lo = 0, hi = sign * Math.Max(1e-12, sqrtMu * Math.Abs(dt) / r0n);
            for (int i = 0; i < 200 && sign * Residual(hi, out _) < 0; i++)
            {
                lo = hi;
                hi *= 2;
            }
            if (sign < 0)
            {
                double t = lo;
                lo = hi;
                hi = t;
            }
            double chi = 0.5 * (lo + hi);
            for (int i = 0; i < 100; i++)
            {
                double f = Residual(chi, out double slope);
                if (f < 0) lo = chi;
                else hi = chi;
                double next = slope > 0 ? chi - f / slope : double.NaN;
                if (!(next > lo && next < hi)) next = 0.5 * (lo + hi);
                if (Math.Abs(next - chi) <= 1e-13 * Math.Max(1, Math.Abs(chi))) { chi = next; break; }
                chi = next;
            }

            double zz = alpha * chi * chi;
            double cc = StumpffC(zz), ss = StumpffS(zz);
            double fl = 1 - chi * chi / r0n * cc;
            double gl = dt - chi * chi * chi / sqrtMu * ss;
            r = r0 * fl + v0 * gl;
            double rn = r.Length;
            double fdot = sqrtMu / (rn * r0n) * (alpha * chi * chi * chi * ss - chi);
            double gdot = 1 - chi * chi / rn * cc;
            v = r0 * fdot + v0 * gdot;
        }

        /// <summary>Position after <paramref name="dt"/> (see <see cref="Propagate"/>).</summary>
        public static DVec3 PositionAfter(DVec3 r0, DVec3 v0, double mu, double dt)
        {
            Propagate(r0, v0, mu, dt, out var r, out _);
            return r;
        }

        /// <summary>
        /// Lambert's problem: the orbit that takes a body from <paramref name="r1"/> to
        /// <paramref name="r2"/> in <paramref name="tof"/> about a centre of parameter
        /// <paramref name="mu"/>, going round the way <paramref name="normal"/> makes prograde (the short
        /// way if the turn from r1 to r2 is anticlockwise about it, the long way otherwise), in under one
        /// revolution. Universal variables (Bate, Mueller and White; Curtis algorithm 5.2), the root in z
        /// found by bisection on the monotonic time-of-flight function. Returns false when there is no
        /// such orbit (the ends coincide or lie exactly opposite, or the numbers are not finite).
        /// </summary>
        public static bool Lambert(DVec3 r1, DVec3 r2, double tof, double mu, DVec3 normal, out DVec3 v1, out DVec3 v2)
        {
            v1 = v2 = DVec3.Zero;
            double r1n = r1.Length, r2n = r2.Length;
            if (mu <= 0 || tof <= 0 || r1n <= 0 || r2n <= 0) return false;
            double cosTheta = Math.Max(-1, Math.Min(1, DVec3.Dot(r1, r2) / (r1n * r2n)));
            double theta = Math.Acos(cosTheta);
            if (DVec3.Dot(DVec3.Cross(r1, r2), normal) < 0) theta = Astro.TwoPi - theta;
            double sinTheta = Math.Sin(theta);
            if (1 - cosTheta < 1e-12 || Math.Abs(sinTheta) < 1e-9) return false;
            double a = sinTheta * Math.Sqrt(r1n * r2n / (1 - cosTheta));
            double sqrtMu = Math.Sqrt(mu);

            double Y(double z) => r1n + r2n + a * (z * StumpffS(z) - 1) / Math.Sqrt(StumpffC(z));

            // F(z) = time-of-flight residual, rising with z; y < 0 (no orbit there) counts as too short.
            double F(double z)
            {
                double y = Y(z);
                if (y < 0) return double.NegativeInfinity;
                double c = StumpffC(z);
                return Math.Pow(y / c, 1.5) * StumpffS(z) + a * Math.Sqrt(y) - sqrtMu * tof;
            }

            double hi = Astro.TwoPi * Astro.TwoPi * (1 - 1e-9);
            double lo = -4 * Math.PI * Math.PI;
            for (int i = 0; i < 80 && F(lo) > 0; i++) lo *= 2;
            if (F(lo) > 0 || !(F(hi) > 0)) return false;
            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (F(mid) > 0) hi = mid;
                else lo = mid;
                if (hi - lo < 1e-14 * Math.Max(1, Math.Abs(mid))) break;
            }
            double zr = 0.5 * (lo + hi);
            double yr = Y(zr);
            if (!(yr > 0)) return false;
            double f = 1 - yr / r1n;
            double g = a * Math.Sqrt(yr / mu);
            double gdot = 1 - yr / r2n;
            if (Math.Abs(g) < 1e-300) return false;
            v1 = (r2 - r1 * f) / g;
            v2 = (r2 * gdot - r1) / g;
            return IsFinite(v1) && IsFinite(v2);
        }

        static bool IsFinite(DVec3 v) => !double.IsNaN(v.X + v.Y + v.Z) && !double.IsInfinity(v.X + v.Y + v.Z);

        /// <summary>Circular orbital speed at radius r.</summary>
        public static double CircularSpeed(double mu, double r) => Math.Sqrt(mu / r);

        /// <summary>Escape speed at radius r.</summary>
        public static double EscapeSpeed(double mu, double r) => Math.Sqrt(2 * mu / r);

        /// <summary>Time of flight of the Hohmann transfer between circular orbits r1 and r2: half the ellipse that touches both.</summary>
        public static double HohmannTime(double mu, double r1, double r2)
        {
            double a = 0.5 * (r1 + r2);
            return Math.PI * Math.Sqrt(a * a * a / mu);
        }

        /// <summary>The two burns of a Hohmann transfer from circular r1 to circular r2 (onto the ellipse, then off it).</summary>
        public static void HohmannDeltaV(double mu, double r1, double r2, out double dv1, out double dv2)
        {
            double a = 0.5 * (r1 + r2);
            dv1 = Math.Abs(Math.Sqrt(mu * (2 / r1 - 1 / a)) - CircularSpeed(mu, r1));
            dv2 = Math.Abs(CircularSpeed(mu, r2) - Math.Sqrt(mu * (2 / r2 - 1 / a)));
        }

        /// <summary>How often two bodies with periods p1 and p2 line up the same way again (the window repeat).</summary>
        public static double SynodicPeriod(double p1, double p2) =>
            Math.Abs(p1 - p2) < 1e-12 ? double.PositiveInfinity : Math.Abs(p1 * p2 / (p1 - p2));

        /// <summary>
        /// How far ahead of the departure world (radians, prograde) the arrival world must be when a
        /// Hohmann transfer leaves, so it is at the far apsis as the ship gets there: π minus the arc it
        /// covers in the transfer time. Negative for an inbound transfer (the target trails).
        /// </summary>
        public static double HohmannLeadAngle(double mu, double r1, double r2) =>
            Math.PI - Math.Sqrt(mu / (r2 * r2 * r2)) * HohmannTime(mu, r1, r2);

        /// <summary>
        /// Days (in the units of the rates) until the next Hohmann window, given the target's present
        /// angle ahead of the departure world <paramref name="phase"/> and both mean motions: when the
        /// phase, turning at n2 - n1, comes round to the lead angle.
        /// </summary>
        public static double WaitForPhase(double phase, double lead, double n1, double n2)
        {
            double rate = n2 - n1;
            if (Math.Abs(rate) < 1e-15) return double.PositiveInfinity;
            double gap = rate > 0 ? lead - phase : phase - lead;
            gap %= Astro.TwoPi;
            if (gap < 0) gap += Astro.TwoPi;
            return gap / Math.Abs(rate);
        }

        /// <summary>Laplace's sphere of influence: a·(m/M)^(2/5), from the gravitational parameters.</summary>
        public static double SoiRadius(double aKm, double gm, double gmParent) =>
            gm <= 0 || gmParent <= 0 ? 0 : aKm * Math.Pow(gm / gmParent, 0.4);

        /// <summary>
        /// The burn at periapsis that takes a ship from a parking orbit at radius <paramref name="rPark"/>
        /// moving at <paramref name="vPark"/> onto the escape hyperbola leaving at <paramref name="vInf"/>
        /// (the Oberth effect: deep in the well, a little speed buys a lot of excess). The same burn in
        /// reverse is the capture into orbit on arrival.
        /// </summary>
        public static double EscapeBurn(double mu, double rPark, double vInf, double vPark) =>
            Math.Abs(Math.Sqrt(vInf * vInf + 2 * mu / rPark) - vPark);

        /// <summary>
        /// The delta-v (km/s) a flip-and-burn from rest at <paramref name="rKm"/> straight out of a well
        /// of <paramref name="mu"/> (km³/s²) spends holding its line against gravity, at a thrust of
        /// <paramref name="accelKmS2"/>: ∫ μ/r² dt with r = r0 + a·t²/2, which is (π/4)·μ·√(2 / (a·r0³)).
        /// </summary>
        public static double GravityLossLeaving(double mu, double rKm, double accelKmS2) =>
            mu <= 0 || rKm <= 0 || accelKmS2 <= 0 ? 0 : Math.PI / 4 * mu * Math.Sqrt(2 / (accelKmS2 * rKm * rKm * rKm));
    }

    /// <summary>
    /// A hyperbolic flyby leg about one world: periapsis <see cref="Rp"/>, excess speed
    /// <see cref="VInf"/>, in the plane of <see cref="P"/> (towards periapsis) and <see cref="Q"/>
    /// (the way it moves there). Time is measured from periapsis: positive leaving, negative arriving.
    /// </summary>
    public sealed class Hyperbola
    {
        public double Mu, Rp, VInf, E, A;
        public DVec3 P, Q;

        /// <summary>
        /// The escape leg from periapsis <paramref name="rp"/> whose outgoing asymptote runs along
        /// <paramref name="vInf"/>, turning about <paramref name="normal"/> (the parking orbit's).
        /// </summary>
        public static Hyperbola Departure(double mu, double rp, DVec3 vInf, DVec3 normal) => Build(mu, rp, vInf, normal, true);

        /// <summary>The capture leg arriving along <paramref name="vInf"/> (its incoming excess velocity) down to periapsis <paramref name="rp"/>.</summary>
        public static Hyperbola Arrival(double mu, double rp, DVec3 vInf, DVec3 normal) => Build(mu, rp, vInf, normal, false);

        /// <summary>
        /// The escape or capture leg whose periapsis is exactly <paramref name="periapsis"/> (where the
        /// ship's parking orbit has it when it burns), in the plane of that point and
        /// <paramref name="vInf"/>, moving at periapsis toward v∞'s side. Its asymptote runs along v∞
        /// only when the point is the right angle round from it (θ∞, see <see cref="AsymptoteError"/>);
        /// a planner times the burn for that.
        /// </summary>
        public static Hyperbola Through(double mu, DVec3 periapsis, DVec3 vInf, DVec3 fallbackNormal)
        {
            double rp = periapsis.Length, v = vInf.Length;
            if (mu <= 0 || rp <= 0 || v <= 0) return null;
            var p = periapsis / rp;
            var q = vInf / v - p * DVec3.Dot(vInf / v, p);
            if (q.LengthSquared < 1e-12) q = DVec3.Cross(fallbackNormal, p);
            if (q.LengthSquared < 1e-12) q = DVec3.Cross(Math.Abs(p.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX, p);
            double e = 1 + rp * v * v / mu;
            return new Hyperbola { Mu = mu, Rp = rp, VInf = v, E = e, A = -mu / (v * v), P = p, Q = q.Normalized };
        }

        static Hyperbola Build(double mu, double rp, DVec3 vInf, DVec3 normal, bool leaving)
        {
            double v = vInf.Length;
            if (mu <= 0 || rp <= 0 || v <= 0) return null;
            var vHat = vInf / v;
            var n = normal - vHat * DVec3.Dot(normal, vHat);
            if (n.LengthSquared < 1e-12)
            {
                n = DVec3.Cross(vHat, Math.Abs(vHat.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX);
                n = DVec3.Cross(n, vHat);
            }
            n = n.Normalized;
            var t = DVec3.Cross(n, vHat);
            double e = 1 + rp * v * v / mu;
            double c = -1 / e, s = Math.Sqrt(1 - c * c);
            // Outgoing asymptote cosθ∞·P + sinθ∞·Q = v̂; incoming one -cosθ∞·P + sinθ∞·Q = v̂ (Q = n × P).
            var p = leaving ? vHat * c - t * s : vHat * -c - t * s;
            return new Hyperbola { Mu = mu, Rp = rp, VInf = v, E = e, A = -mu / (v * v), P = p, Q = DVec3.Cross(n, p) };
        }

        /// <summary>Half the turn the leg makes, θ∞ = acos(-1/e): the angle from periapsis to either asymptote.</summary>
        public double AsymptoteAngle => Math.Acos(-1 / E);

        /// <summary>The speed at periapsis, √(v∞² + 2μ/rp).</summary>
        public double PeriapsisSpeed => Math.Sqrt(VInf * VInf + 2 * Mu / Rp);

        /// <summary>
        /// How far (radians) the asymptote of a leg through <paramref name="periapsis"/> misses
        /// <paramref name="vInf"/>: zero when the point lies θ∞ round from v∞, as a well-timed escape burn has it.
        /// </summary>
        public static double AsymptoteError(double mu, DVec3 periapsis, DVec3 vInf, bool leaving)
        {
            double rp = periapsis.Length, v = vInf.Length;
            if (rp <= 0 || v <= 0) return 0;
            double e = 1 + rp * v * v / mu;
            double theta = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(periapsis / rp, vInf / v))));
            return Math.Abs(theta - Math.Acos(-1 / e));
        }

        /// <summary>Mean motion on the hyperbola, √(μ / -a³).</summary>
        public double MeanMotion => Math.Sqrt(Mu / (-A * -A * -A));

        /// <summary>Position relative to the world <paramref name="t"/> from periapsis (hyperbolic Kepler's equation, M = e·sinh F - F).</summary>
        public DVec3 PositionAt(double t)
        {
            double m = MeanMotion * t;
            double f = Math.Abs(m) < 1e-12 ? 0 : Asinh(m / E);
            for (int i = 0; i < 60; i++)
            {
                double g = E * Math.Sinh(f) - f - m;
                double gp = E * Math.Cosh(f) - 1;
                double step = g / gp;
                f -= step;
                if (Math.Abs(step) < 1e-13 * Math.Max(1, Math.Abs(f))) break;
            }
            double aa = -A;
            double x = aa * (E - Math.Cosh(f));
            double y = aa * Math.Sqrt(E * E - 1) * Math.Sinh(f);
            return P * x + Q * y;
        }

        /// <summary>Velocity relative to the world <paramref name="t"/> from periapsis.</summary>
        public DVec3 VelocityAt(double t)
        {
            var p = PositionAt(t);
            double r = p.Length;
            double aa = -A;
            // cosh F from the radius, sinh F from the side of periapsis it is on.
            double coshF = (r / aa + 1) / E;
            double sinhF = Math.Sign(t) * Math.Sqrt(Math.Max(0, coshF * coshF - 1));
            double fDot = MeanMotion / (E * coshF - 1);
            return P * (-aa * sinhF * fDot) + Q * (aa * Math.Sqrt(E * E - 1) * coshF * fDot);
        }

        /// <summary>Time from periapsis to reach radius <paramref name="r"/> on the way out (at least 0).</summary>
        public double TimeToRadius(double r)
        {
            if (r <= Rp) return 0;
            double aa = -A;
            double coshF = (r / aa + 1) / E;
            double f = Math.Log(coshF + Math.Sqrt(Math.Max(0, coshF * coshF - 1)));
            return (E * Math.Sinh(f) - f) / MeanMotion;
        }

        static double Asinh(double x) => Math.Log(x + Math.Sqrt(x * x + 1));
    }

    /// <summary>
    /// A planned coasting transfer between two ends in one frame (the Sun between worlds, a planet
    /// between its moons): a Lambert conic under the frame's gravity, flown from an escape burn to a
    /// capture burn. When an end is a world with a sphere of influence well outside the ship's orbit, it
    /// is a patched conic: the ship climbs out on an escape hyperbola from its parking orbit, hands over
    /// to the frame's conic as it passes the edge of the sphere, and drops in on the target's capture
    /// hyperbola to circularise in its new parking orbit. Times in days, distances in km, velocities in
    /// km/day, mu in km³/day².
    /// </summary>
    public sealed class ConicTransfer
    {
        /// <summary>The frame's gravitational parameter, km³/day².</summary>
        public double Mu;

        /// <summary>
        /// Where (relative to the frame) the frame's conic starts, and how fast: where the ship crosses out
        /// of the departure world's sphere of influence (patched), or the ship itself as it leaves.
        /// </summary>
        public DVec3 R0, V0;

        /// <summary>Where the frame's conic ends and how fast: the edge of the arrival world's sphere, or the arrival point.</summary>
        public DVec3 R1, V1;

        /// <summary>Flight time, days, end to end.</summary>
        public double Days;

        /// <summary>Days spent on the escape hyperbola before the handoff, and on the capture hyperbola after it (0 unpatched).</summary>
        public double ExitDays, EntryDays;

        /// <summary>The escape (or departure) burn and the capture (or circularisation) burn, km/s.</summary>
        public double DepartureBurnKmS, ArrivalBurnKmS;

        /// <summary>The small trims at the sphere-of-influence handoffs that match each hyperbola to the frame's conic, km/s.</summary>
        public double CorrectionBurnKmS;

        /// <summary>How long each end's burn takes at the ship's thrust, days.</summary>
        public double DepartureBurnDays, ArrivalBurnDays;

        /// <summary>Total delta-v of the transfer, km/s.</summary>
        public double DeltaVKmS => DepartureBurnKmS + ArrivalBurnKmS + CorrectionBurnKmS;

        /// <summary>The escape hyperbola out of the departure world's well, or null if the ship starts on the conic.</summary>
        public Hyperbola Escape;

        /// <summary>The capture hyperbola into the arrival world's well, or null if the ship ends on the conic.</summary>
        public Hyperbola Capture;

        /// <summary>The departure world's state (relative to the frame) at departure and the arrival world's at arrival.</summary>
        public DVec3 FromR0, FromV0, ToR1, ToV1;

        /// <summary>The spheres of influence the ship hands over at, km.</summary>
        public double FromSoiKm, ToSoiKm;

        /// <summary>
        /// Position relative to the frame <paramref name="tau"/> days into the transfer: on the escape
        /// hyperbola about the departure world until it crosses out of its sphere, then on the frame's
        /// conic, then on the capture hyperbola about the arrival world (patched conics: each piece starts
        /// where the last left off).
        /// </summary>
        public DVec3 OffsetAt(double tau)
        {
            tau = Math.Max(0, Math.Min(Days, tau));
            if (Escape != null && tau < ExitDays)
                return Conics.PositionAfter(FromR0, FromV0, Mu, tau) + Escape.PositionAt(tau);
            if (Capture != null && tau > Days - EntryDays)
            {
                double t = tau - Days;
                return Conics.PositionAfter(ToR1, ToV1, Mu, t) + Capture.PositionAt(t);
            }
            return Conics.PositionAfter(R0, V0, Mu, tau - ExitDays);
        }
    }
}
