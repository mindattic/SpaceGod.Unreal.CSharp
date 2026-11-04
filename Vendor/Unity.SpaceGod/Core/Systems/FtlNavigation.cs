using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A course plotted for the helm's FTL drive (U.SPC-T420): from where the hull is to a star or a beacon, every part of
    /// the trip laid out before it is flown. The run out under the ordinary drive to the departure point beyond the outer
    /// rim (canon, U.SPC-T455: no jump inside a system's gravity wells), any wait for the drive to cool, the charge, the
    /// light-years under FTL, each star's well or gravity projector on the line that would pull the hull out to cross it
    /// under the ordinary drive (U.SPC-T418), the reaction mass all of it burns, and when it drops out at the far end.
    ///
    /// Worked out with the same rules the trip is then flown by (<see cref="FtlRules"/>, <see cref="CruiseProfile"/>,
    /// <see cref="DeepSpace.FieldsAlong"/>), so the ETA and the fuel are the trip's own, not a guess.
    /// </summary>
    public sealed class FtlCourse
    {
        public FtlTarget Target { get; private set; }
        public StarSystem From { get; private set; }
        public Ship Ship { get; private set; }
        public CruiseDriveSpec Drive { get; private set; }

        /// <summary>When it was plotted, and where the hull was then (km from <see cref="From"/>'s root).</summary>
        public double PlottedJd { get; private set; }
        public DVec3 StartKm { get; private set; }

        /// <summary>Where the drive engages: beyond the outer rim on the line to the target, clear of every well (km from the root).</summary>
        public DVec3 DepartureKm { get; private set; }

        /// <summary>The run out to <see cref="DepartureKm"/> under the ordinary drive: how far, how long, its acceleration
        /// (the hull's own, or less to leave the jump its reaction mass) and what it burns.</summary>
        public double OutRunKm { get; private set; }
        public double OutRunDays { get; private set; }
        public double OutRunAccelKmS2 { get; private set; }
        public double OutRunFuelTonnes { get; private set; }

        /// <summary>Days waiting at the departure point for the drive to cool from its last drop-out.</summary>
        public double CoolDays { get; private set; }

        /// <summary>The charge before the first engagement, days.</summary>
        public double SpoolDays { get; private set; }

        /// <summary>Light-years from the departure point to the drop-out at the edge of the far star's well.</summary>
        public double Ly { get; private set; }

        /// <summary>Days under way under FTL, each re-spool after an interdiction included.</summary>
        public double FtlDays { get; private set; }

        /// <summary>The wells and projector fields on the line that would pull the hull out (in order), and the days spent
        /// crossing them under the ordinary drive.</summary>
        public List<string> Interdictions { get; } = new List<string>();
        public double CrossingDays { get; private set; }

        /// <summary>Reaction mass the jump (every leg of it) burns, tonnes.</summary>
        public double JumpFuelTonnes { get; private set; }

        /// <summary>The tanks when plotted, tonnes, and how far one jump on them goes, light-years.</summary>
        public double TanksTonnes { get; private set; }
        public double RangeLy { get; private set; }

        /// <summary>Why the course cannot be flown, or null.</summary>
        public string Refusal { get; private set; }

        public bool Flyable => Refusal == null;

        /// <summary>The whole trip, days: out-run, cooling, charge, FTL and crossings.</summary>
        public double TotalDays => OutRunDays + CoolDays + SpoolDays + FtlDays + CrossingDays;

        /// <summary>When it drops out at the far end.</summary>
        public double EtaJd => PlottedJd + TotalDays;

        /// <summary>Reaction mass the whole trip burns, tonnes.</summary>
        public double FuelTonnes => OutRunFuelTonnes + JumpFuelTonnes;

        /// <summary>Whether the hull needs a run out before it can engage (it is inside the rim, or a well or a field).</summary>
        public bool NeedsOutRun => OutRunKm > 1;

        /// <summary>How far past the rim the departure point is put, so a drift does not fall back inside it.</summary>
        public const double RimMargin = 1.05;

        /// <summary>The most interdictions looked for along a course (a run through a crowded cluster stops looking there).</summary>
        public const int MaxInterdictions = 12;

        FtlCourse() { }

        /// <summary>
        /// Plots the course for <paramref name="ship"/>, at <paramref name="fromKm"/> (km from <paramref name="here"/>'s root),
        /// to <paramref name="target"/> at <paramref name="jd"/>. Always returns a course; one that cannot be flown says why
        /// in <see cref="Refusal"/> (and still carries what could be worked out, such as the distance).
        /// </summary>
        public static FtlCourse Plot(Galaxy galaxy, StarSystem here, Ship ship, DVec3 fromKm, FtlTarget target, double jd)
        {
            var c = new FtlCourse { Target = target, From = here, Ship = ship, StartKm = fromKm, PlottedJd = jd, DepartureKm = fromKm };
            c.Refusal = HelmFtl.Refusal(ship);
            if (c.Refusal == null && here == null) c.Refusal = "Nowhere to plot from";
            if (c.Refusal == null && target == null) c.Refusal = "No star or beacon chosen";
            if (c.Refusal == null && target.System == here) c.Refusal = "No jump within a system: only to another star or beacon";
            if (here != null && target != null)
                c.Ly = here.At(fromKm).DistanceLy(target.Origin);
            if (c.Refusal != null) return c;

            // What lies along the way is charted before the course is (U.SPC-T421): a rogue planet's well is plotted as the trip meets it.
            galaxy?.DeepSpace?.SeedRoute(here.Name, here.Origin, target.Name, target.Origin);
            var drive = c.Drive = CruiseDrives.Of(ship);
            c.TanksTonnes = ship.Propellant;
            c.RangeLy = FtlRules.RangeLy(drive, ship);

            // Out beyond the rim, clear of every well and field, on the line to the target.
            c.DepartureKm = DeparturePoint(galaxy, here, fromKm, target);
            var start = here.At(c.DepartureKm);
            double wellKm = CruiseDrives.WellLimitKm(target.System);
            var arrive = DeepSpace.ArrivalPoint(target.Origin, start, wellKm);
            c.Ly = arrive.DistanceLy(start);

            // The jump, leg by leg: each star's well or projector on the line pulls it out to cross under the ordinary drive.
            c.SpoolDays = drive.SpoolDays;
            var probe = new Cruise { Drive = drive, From = here, To = target.System, ToName = target.Name, Target = arrive, Plan = true };
            probe.Ships.Add(ship);
            var at = start;
            double crossSpeedKmS = Math.Max(1e-6, FtlRules.CrossingSpeedKmS.Value);
            for (int leg = 0; ; leg++)
            {
                double legLy = arrive.DistanceLy(at);
                c.JumpFuelTonnes += FtlRules.PropellantFor(drive, ship, legLy);
                // As the trip itself is flown (FtlInterdiction): not the fields the leg starts in or ends in.
                var fields = new List<GravityField>();
                if (leg < MaxInterdictions && galaxy?.DeepSpace != null)
                    foreach (var f in galaxy.DeepSpace.FieldsAlong(probe, at, arrive))
                        if (!f.Contains(at, 1.001) && !f.Contains(arrive, 1.001)) fields.Add(f);
                if (!FtlRules.FirstEntry(at, arrive, fields, out var field, out double along))
                {
                    c.FtlDays += CruiseProfile.For(drive, legLy).FlightDays;
                    break;
                }
                var entry = GalacticPosition.Lerp(at, arrive, along);
                c.FtlDays += DaysToReach(CruiseProfile.For(drive, legLy), entry.DistanceLy(at));
                var dir = arrive.KmFrom(entry);
                dir = dir.LengthSquared > 0 ? dir.Normalized : DVec3.UnitX;
                var exit = FtlRules.ExitPoint(field, entry, dir);
                if (exit.DistanceKm(entry) > arrive.DistanceKm(entry)) exit = arrive;
                double crossDays = exit.DistanceKm(entry) / crossSpeedKmS / Astro.SecondsPerDay;
                c.CrossingDays += crossDays;
                c.Interdictions.Add(field.Name);
                if (exit.DistanceKm(arrive) <= 1) break;
                // Spooled again on the far side, once the drive has cooled from being pulled out.
                c.FtlDays += drive.SpoolDays + Math.Max(0, drive.CooldownDays - crossDays);
                at = exit;
            }

            // The run out: the hull's own thrust, flip and burn, or gentler so as to burn no more than its share of what the
            // tanks hold past the jump's (the rest is the hull's to fly on with at the far end).
            c.OutRunKm = (c.DepartureKm - fromKm).Length;
            if (c.NeedsOutRun)
            {
                double accel = Math.Max(1e-9, ship.Spec.AccelG * 9.80665e-3);
                double left = ship.Propellant - c.JumpFuelTonnes;
                double spare = left * OutRunShare;
                double dv = Propulsion.BrachistochroneDeltaV(accel, c.OutRunKm);
                if (left <= 0)
                    c.Refusal = "Not enough reaction mass to jump " + c.Ly.ToString("0.00", CultureInfo.InvariantCulture) + " ly: it takes " +
                                c.JumpFuelTonnes.ToString("N0", CultureInfo.InvariantCulture) + " t, the tanks hold " +
                                ship.Propellant.ToString("N0", CultureInfo.InvariantCulture) + " t";
                else if (Propulsion.PropellantFor(ship, dv) > spare)
                {
                    // Gentler, so the run leaves the jump its reaction mass: what the spare buys, spent half out, half braking.
                    double dvSpare = Propulsion.DeltaV(ship.ExhaustVelocityKmS, ship.MassTonnes, Math.Max(1e-6, ship.MassTonnes - spare)) * 0.999;
                    accel = dvSpare * dvSpare / (4 * c.OutRunKm);
                    dv = dvSpare;
                }
                c.OutRunAccelKmS2 = accel;
                c.OutRunDays = OutRunSeconds(c.OutRunKm, accel) / Astro.SecondsPerDay;
                c.OutRunFuelTonnes = Propulsion.PropellantFor(ship, dv);
                if (c.Refusal == null && accel < MinOutRunAccelKmS2)
                    c.Refusal = "Too little reaction mass to fly out beyond the rim and still jump: refuel first";
            }
            if (c.Refusal != null) return c;

            c.CoolDays = Math.Max(0, FtlRules.CoolUntil(ship) - (jd + c.OutRunDays));
            if (c.JumpFuelTonnes > ship.Propellant + 1e-9)
                c.Refusal = "Out of range: " + c.Ly.ToString("0.00", CultureInfo.InvariantCulture) + " ly, the tanks reach " +
                            c.RangeLy.ToString("0.#", CultureInfo.InvariantCulture) + " ly";
            return c;
        }

        /// <summary>The most of the reaction mass left past the jump's that the run out may burn.</summary>
        public const double OutRunShare = 0.5;

        /// <summary>The gentlest run out the autopilot will fly (about a ten-thousandth of a g): below it, a refusal.</summary>
        public const double MinOutRunAccelKmS2 = 1e-6;

        /// <summary>Days into <paramref name="profile"/> (its charge not counted) until <paramref name="ly"/> is covered: where a
        /// run at full speed is pulled out at a field's edge.</summary>
        static double DaysToReach(CruiseProfile profile, double ly)
        {
            double t0 = 0, t1 = profile.FlightDays;
            for (int i = 0; i < 60; i++)
            {
                double mid = (t0 + t1) / 2;
                if (profile.LyAt(profile.SpoolDays + mid) < ly) t0 = mid;
                else t1 = mid;
            }
            return (t0 + t1) / 2;
        }

        /// <summary>Seconds to fly <paramref name="km"/> from rest to rest at <paramref name="accelKmS2"/>, flipping half way.</summary>
        public static double OutRunSeconds(double km, double accelKmS2) => km <= 0 ? 0 : 2 * Math.Sqrt(km / Math.Max(1e-12, accelKmS2));

        /// <summary>
        /// Where a hull at <paramref name="fromKm"/> in <paramref name="here"/> engages for <paramref name="target"/>: where it is,
        /// if it is already clear to jump; otherwise just beyond the outer rim on the line from the star to the target, pushed
        /// clear of any planet's or moon's well and of any gravity projector's field.
        /// </summary>
        public static DVec3 DeparturePoint(Galaxy galaxy, StarSystem here, DVec3 fromKm, FtlTarget target)
        {
            if (HelmFtl.WellRefusal(here, fromKm, galaxy) == null) return fromKm;
            var way = target.Origin.KmFrom(here.Origin);
            var dir = way.LengthSquared > 0 ? way.Normalized : fromKm.LengthSquared > 0 ? fromKm.Normalized : DVec3.UnitX;
            var at = dir * (HelmFtl.RimKm(here) * RimMargin);
            for (int pass = 0; pass < 6; pass++)
            {
                at = FtlRules.ClearOfBodyWells(here, at);
                if (HelmFtl.WellRefusal(here, at, galaxy) == null) break;
                at *= 1.25;
            }
            return at;
        }

        /// <summary>The way from <paramref name="fromKm"/> to the target (unit) and how far, as the helm sees it.</summary>
        public DVec3 Bearing(DVec3 fromKm, out double km) => HelmFtl.Bearing(Target, From, fromKm, out km);

        public override string ToString() => Target?.Name ?? "no course";
    }

    /// <summary>Where the FTL autopilot is in flying a plotted course (U.SPC-T420).</summary>
    public enum FtlAutopilotStage
    {
        Off,

        /// <summary>Flying out to the departure point beyond the rim under the ordinary drive, the clock compressed.</summary>
        OutRun,

        /// <summary>At the departure point, turning the nose onto the target.</summary>
        Aligning,

        /// <summary>Aligned, but the drive is still cooling from its last jump: held, the nose on the target, until it has cooled.</summary>
        Cooling,

        /// <summary>The drive charging (the helm's own <see cref="HelmFtl"/>), the nose held on the target.</summary>
        Spooling,
    }

    /// <summary>What a step of the autopilot came to.</summary>
    public enum FtlAutopilotEvent
    {
        None,

        /// <summary>Out at the departure point: turning onto the target next.</summary>
        Arrived,

        /// <summary>The nose is on the target: the drive can be spooled.</summary>
        Aligned,

        /// <summary>Let go of (a fight, or the course could not be flown on): see <see cref="FtlAutopilot.Reason"/>.</summary>
        Disengaged,
    }

    /// <summary>
    /// The FTL autopilot (U.SPC-T420): given a plotted <see cref="FtlCourse"/>, flies the hull out beyond the rim under its
    /// ordinary drive, turns its nose onto the target, spools the drive and engages it, so a trip to another star is one
    /// key or one click on the galaxy map. The run out is worked out closed-form, flip-and-burn from rest to rest, so where
    /// the hull is never depends on the frame rate and the clock can be run as fast as a long trip needs
    /// (<see cref="FtlNavigation.WarpFor"/>). A fight lets go of it, as does a course gone bad.
    ///
    /// Engine-free: the Runtime helm puts the hull where <see cref="PositionKm"/> says and turns it as <see cref="Turn"/> does.
    /// </summary>
    public sealed class FtlAutopilot
    {
        public FtlAutopilotStage Stage { get; private set; }
        public FtlCourse Course { get; private set; }

        /// <summary>The run out: from, to (km from the root), when it began and ends, at what acceleration.</summary>
        public DVec3 RunFromKm { get; private set; }
        public DVec3 RunToKm { get; private set; }
        public double RunStartJd { get; private set; } = double.NaN;
        public double RunEndJd { get; private set; } = double.NaN;
        public double RunAccelKmS2 { get; private set; }

        /// <summary>Why it last let go, or why it would not engage.</summary>
        public string Reason { get; private set; }

        public bool Active => Stage != FtlAutopilotStage.Off;

        /// <summary>How fast the nose is swung onto the target while aligning, degrees a real second.</summary>
        public const double TurnDegPerSecond = 30;

        /// <summary>
        /// Takes the helm to fly <paramref name="course"/> from <paramref name="fromKm"/> at <paramref name="jd"/>: refused (with
        /// <see cref="Reason"/>) if it cannot be flown or the hull is fighting. The run out's reaction mass is spent now.
        /// </summary>
        public bool Engage(FtlCourse course, DVec3 fromKm, bool inCombat, double jd)
        {
            Reason = course == null ? "No course plotted: pick a star or beacon first" : course.Refusal;
            if (Reason == null && inCombat) Reason = "The autopilot will not fly in a fight";
            if (Reason != null)
            {
                Reset();
                return false;
            }
            Course = course;
            RunFromKm = fromKm;
            RunToKm = course.DepartureKm;
            RunStartJd = jd;
            RunAccelKmS2 = course.OutRunAccelKmS2;
            double km = (RunToKm - RunFromKm).Length;
            if (course.NeedsOutRun && km > 1)
            {
                RunEndJd = jd + FtlCourse.OutRunSeconds(km, RunAccelKmS2) / Astro.SecondsPerDay;
                course.Ship.Propellant = Math.Max(0, course.Ship.Propellant - course.OutRunFuelTonnes);
                Stage = FtlAutopilotStage.OutRun;
            }
            else
            {
                RunEndJd = jd;
                Stage = FtlAutopilotStage.Aligning;
            }
            return true;
        }

        /// <summary>0..1 of the way out.</summary>
        public double RunFraction(double jd)
        {
            if (Stage != FtlAutopilotStage.OutRun) return Stage == FtlAutopilotStage.Off ? 0 : 1;
            double total = RunEndJd - RunStartJd;
            if (!(total > 0)) return 1;
            double t = Math.Max(0, Math.Min(1, (jd - RunStartJd) / total));
            // Flip and burn: half the way accelerating, half braking.
            return t <= 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t);
        }

        /// <summary>Where the hull is at <paramref name="jd"/> (km from the root): on the run out, else where it ended.</summary>
        public DVec3 PositionKm(double jd) => DVec3.Lerp(RunFromKm, RunToKm, RunFraction(jd));

        /// <summary>Its velocity at <paramref name="jd"/> on the run out (km/s against the root); nil at either end.</summary>
        public DVec3 VelocityKmS(double jd)
        {
            if (Stage != FtlAutopilotStage.OutRun) return DVec3.Zero;
            double totalS = (RunEndJd - RunStartJd) * Astro.SecondsPerDay;
            if (!(totalS > 0)) return DVec3.Zero;
            double t = Math.Max(0, Math.Min(1, (jd - RunStartJd) / (RunEndJd - RunStartJd)));
            double dsdt = (t <= 0.5 ? 4 * t : 4 * (1 - t)) / totalS;
            return (RunToKm - RunFromKm) * dsdt;
        }

        /// <summary>The way the run heads (unit), the nose prograde as every hull's is through a transit.</summary>
        public DVec3 RunDirection
        {
            get
            {
                var d = RunToKm - RunFromKm;
                return d.LengthSquared > 0 ? d.Normalized : DVec3.UnitX;
            }
        }

        /// <summary>Days left of the run out.</summary>
        public double RunDaysLeft(double jd) => Stage == FtlAutopilotStage.OutRun ? Math.Max(0, RunEndJd - jd) : 0;

        /// <summary>
        /// A step at <paramref name="jd"/>: a fight lets go of the helm, and the run out, once done, gives way to aligning.
        /// </summary>
        public FtlAutopilotEvent Step(bool inCombat, double jd)
        {
            if (!Active) return FtlAutopilotEvent.None;
            if (inCombat) return Disengage("Autopilot off: a fight", jd);
            if (Stage == FtlAutopilotStage.OutRun && jd >= RunEndJd)
            {
                Stage = FtlAutopilotStage.Aligning;
                return FtlAutopilotEvent.Arrived;
            }
            return FtlAutopilotEvent.None;
        }

        /// <summary>
        /// Aligning: <paramref name="forward"/> turned toward the target by at most <paramref name="maxDeg"/> degrees; once
        /// within <see cref="HelmFtl.AlignDeg"/>, <see cref="FtlAutopilotEvent.Aligned"/>.
        /// </summary>
        public FtlAutopilotEvent Turn(ref DVec3 forward, DVec3 fromKm, double maxDeg)
        {
            if (Stage != FtlAutopilotStage.Aligning) return FtlAutopilotEvent.None;
            var want = Course.Bearing(fromKm, out _);
            forward = TurnToward(forward, want, maxDeg * Astro.Deg2Rad);
            return HelmFtl.OffNoseDeg(Course.Target, Course.From, fromKm, forward) <= HelmFtl.AlignDeg * 0.5 ? FtlAutopilotEvent.Aligned : FtlAutopilotEvent.None;
        }

        /// <summary>The drive is still cooling: hold here, nose on the target, until <see cref="FtlRules.CoolUntil"/>.</summary>
        public void Cooling()
        {
            if (Active) Stage = FtlAutopilotStage.Cooling;
        }

        /// <summary>The drive is spooling for the course: the helm's charge has it from here.</summary>
        public void Spooling()
        {
            if (Active) Stage = FtlAutopilotStage.Spooling;
        }

        /// <summary><paramref name="from"/> (unit) swung toward <paramref name="to"/> by at most <paramref name="maxRad"/>.</summary>
        public static DVec3 TurnToward(DVec3 from, DVec3 to, double maxRad)
        {
            if (from.LengthSquared <= 0) return to.Normalized;
            if (to.LengthSquared <= 0) return from.Normalized;
            var a = from.Normalized;
            var b = to.Normalized;
            double cos = Math.Max(-1, Math.Min(1, DVec3.Dot(a, b)));
            double angle = Math.Acos(cos);
            if (angle <= maxRad) return b;
            var axis = DVec3.Cross(a, b);
            if (axis.LengthSquared < 1e-18) axis = Math.Abs(a.Z) < 0.9 ? DVec3.Cross(a, DVec3.UnitZ) : DVec3.Cross(a, DVec3.UnitX);
            return PilotFlight.RotateAbout(a, axis.Normalized, maxRad).Normalized;
        }

        /// <summary>Lets go of the helm, saying why, giving back the reaction mass of the part of the run out not flown by <paramref name="jd"/>.</summary>
        public FtlAutopilotEvent Disengage(string why, double jd)
        {
            Reason = why;
            Abandon(jd);
            return FtlAutopilotEvent.Disengaged;
        }

        /// <summary>Off, the course forgotten, the unflown share of the run out's reaction mass refunded as of <paramref name="jd"/>.</summary>
        public void Abandon(double jd)
        {
            if (Stage == FtlAutopilotStage.OutRun && Course != null && Course.OutRunFuelTonnes > 0)
            {
                double total = RunEndJd - RunStartJd;
                // Flip and burn: the delta-v, so the propellant, is spent evenly across the run.
                double unflown = total > 0 ? 1 - Math.Max(0, Math.Min(1, (jd - RunStartJd) / total)) : 0;
                Course.Ship.Propellant += Course.OutRunFuelTonnes * unflown;
            }
            Reset();
        }

        /// <summary>Off, the course forgotten.</summary>
        public void Reset()
        {
            Stage = FtlAutopilotStage.Off;
            Course = null;
            RunStartJd = RunEndJd = double.NaN;
            RunAccelKmS2 = 0;
        }
    }

    /// <summary>
    /// Interstellar navigation (U.SPC-T420): the stars and beacons in the order a navigator offers them, and the time
    /// compression a long trip is flown at, tied to the sim clock so everything else in the galaxy moves on with it. Its
    /// tuning is in the settings menu's FTL group.
    /// </summary>
    [SettingsSource]
    public static class FtlNavigation
    {
        public static readonly RangeSetting TripRealSeconds = new RangeSetting("ftl.tripRealSeconds", SettingGroups.Ftl,
            "Long trip compression", 45, 10, 600, 5,
            "About how many real seconds each long stretch of a trip (the run out to the rim, the crossing between the stars) is " +
            "compressed into: the clock is run at the slowest time warp that gets it over that fast. The rest of the galaxy moves on with it; " +
            ", and . still change the pace in flight.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " s", "ftl time compression warp trip cruise autopilot");

        /// <summary>
        /// The time warp (an index into <see cref="SimClock.Levels"/>) that gets <paramref name="days"/> of a trip over in about
        /// <see cref="TripRealSeconds"/> of real time (or <paramref name="realSeconds"/>), never slower than <paramref name="atLeast"/>.
        /// </summary>
        public static int WarpFor(double days, int atLeast = 0, double realSeconds = double.NaN)
        {
            double real = double.IsNaN(realSeconds) ? TripRealSeconds.Value : realSeconds;
            if (double.IsNaN(days) || double.IsInfinity(days) || days <= 0) return Math.Max(0, Math.Min(SimClock.Levels.Length - 1, atLeast));
            return SimClock.WarpToCover(days * Astro.SecondsPerDay, Math.Max(1, real), atLeast);
        }

        /// <summary>Every star and beacon on offer from <paramref name="here"/>, nearest to <paramref name="fromKm"/> first.</summary>
        public static List<FtlTarget> Nearest(Galaxy galaxy, StarSystem here, DVec3 fromKm)
        {
            var list = FtlTargets.All(galaxy, here);
            if (here == null) return list;
            var at = here.At(fromKm);
            var dist = new Dictionary<FtlTarget, double>();
            foreach (var t in list) dist[t] = t.Origin.DistanceKm(at);
            list.Sort((a, b) => dist[a].CompareTo(dist[b]));
            return list;
        }

        /// <summary>The target after (or, with <paramref name="step"/> -1, before) <paramref name="current"/> in
        /// <paramref name="ordered"/>, wrapping round; the first if <paramref name="current"/> is not in it.</summary>
        public static FtlTarget Cycle(IReadOnlyList<FtlTarget> ordered, FtlTarget current, int step)
        {
            if (ordered == null || ordered.Count == 0) return null;
            int i = -1;
            for (int k = 0; k < ordered.Count; k++)
                if (Same(ordered[k], current))
                {
                    i = k;
                    break;
                }
            if (i < 0) return step < 0 ? ordered[ordered.Count - 1] : ordered[0];
            int n = ordered.Count;
            return ordered[((i + step) % n + n) % n];
        }

        /// <summary>Whether two targets are the same place: the same running system, or the same star on the charts by name.</summary>
        public static bool Same(FtlTarget a, FtlTarget b)
        {
            if (a == null || b == null) return a == b;
            if (a.System != null || b.System != null) return a.System == b.System;
            return string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A short description of a trip's length: hours, days or years.</summary>
        public static string Describe(double days)
        {
            var inv = CultureInfo.InvariantCulture;
            if (double.IsNaN(days) || double.IsInfinity(days)) return "never";
            if (days < 1) return (days * 24).ToString("0.0", inv) + " h";
            if (days < 365.25 * 2) return days.ToString("0.0", inv) + " days";
            return (days / 365.25).ToString("0.0", inv) + " years";
        }
    }
}
