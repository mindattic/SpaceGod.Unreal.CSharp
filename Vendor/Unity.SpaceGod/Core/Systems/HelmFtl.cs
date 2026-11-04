using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A star the helm's FTL drive can be pointed at (U.SPC-T417): a running system, or a plain star on the charts (built
    /// into a system when a pilot sets out for it). Canon (U.SPC-T455): a jump only ever goes from one star to another,
    /// never to somewhere inside a solar system, whose gravity wells make jumping there impossible.
    /// </summary>
    public sealed class FtlTarget
    {
        public string Name { get; }

        /// <summary>The running system the star is, or null for a plain star on the charts.</summary>
        public StarSystem System { get; }

        /// <summary>Where it is in the galaxy.</summary>
        public GalacticPosition Origin { get; }

        FtlTarget(string name, StarSystem system, GalacticPosition origin)
        {
            Name = name;
            System = system;
            Origin = origin;
        }

        /// <summary>A running system's star.</summary>
        public static FtlTarget OfStar(StarSystem system) => new FtlTarget(system.Name, system, system.Origin);

        /// <summary>A plain star on the charts, one nobody simulates yet.</summary>
        public static FtlTarget OfStar(NearbyStar star) => new FtlTarget(star.Name, null, star.Origin);

        /// <summary>A beacon rather than a star (U.SPC-T420): a void station out in the dark between the stars, which the
        /// gate network relays through, with no star of its own.</summary>
        public bool IsBeacon => System != null && System.IsVoidBase;

        /// <summary>"beacon" or "star", for the navigator's lines.</summary>
        public string KindName => IsBeacon ? "beacon" : "star";

        /// <summary>Where it is, km from <paramref name="here"/>'s root.</summary>
        public DVec3 LocalKm(StarSystem here) => here.LocalKm(Origin);

        public override string ToString() => Name;
    }

    /// <summary>
    /// What the helm can point its FTL drive at from a system (U.SPC-T417): the other stars, running systems and the
    /// charts. A new kind of destination plugs in by adding a source to <see cref="Sources"/>, never by editing the helm.
    /// </summary>
    public static class FtlTargets
    {
        /// <summary>A source of targets: (the galaxy, the system the ship is in) to what it offers.</summary>
        public delegate IEnumerable<FtlTarget> Source(Galaxy galaxy, StarSystem here);

        /// <summary>Where targets come from, in order.</summary>
        public static readonly List<Source> Sources = new List<Source> { Stars };

        /// <summary>Every target offered from <paramref name="here"/>.</summary>
        public static List<FtlTarget> All(Galaxy galaxy, StarSystem here)
        {
            var list = new List<FtlTarget>();
            foreach (var s in Sources)
                foreach (var t in s(galaxy, here))
                    if (t != null && t.System != here) list.Add(t);
            return list;
        }

        /// <summary>The other running systems' stars, and the plain stars on the charts.</summary>
        public static IEnumerable<FtlTarget> Stars(Galaxy galaxy, StarSystem here)
        {
            if (galaxy == null) yield break;
            foreach (var s in galaxy.Systems)
                if (s != here) yield return FtlTarget.OfStar(s);
            foreach (var n in galaxy.NearbyStars)
                if (n.Name != null) yield return FtlTarget.OfStar(n);
        }
    }

    /// <summary>Where the helm's FTL drive stands.</summary>
    public enum FtlStage
    {
        Idle,

        /// <summary>Charging, beyond the rim, nose held on the star.</summary>
        Spooling,
    }

    /// <summary>What a step of the helm's FTL drive came to.</summary>
    public enum FtlEvent
    {
        None,

        /// <summary>The charge was lost (the nose swung off, a fight, back inside the rim, or called off): see <see cref="HelmFtl.AbortReason"/>.</summary>
        Aborted,

        /// <summary>Charged: the drive engages now (<see cref="HelmFtl.EngageInterstellar"/>).</summary>
        Charged,
    }

    /// <summary>
    /// The helm's own FTL drive, I-War 2 style (U.SPC-T417): a carrier, a battleship fitted with a drive or a deep-space
    /// explorer (anything carrying a <see cref="CruiseDriveSpec"/> by the owner's roster, <see cref="FtlRoster"/>, U.SPC-T472)
    /// is pointed at another star, spools its drive while the nose is held on it, engages,
    /// and is handed to <see cref="DeepSpace"/>, which flies it across open space as it does every other group under its
    /// own drive and drops it out at the edge of that star's well.
    ///
    /// Canon and lore (U.SPC-T455): jumping is impossible inside a solar system's gravity wells. A hull must first fly
    /// beyond the system's outer rim (<see cref="RimKm"/>, the same edge the jump gates stand toward and the cruise drives
    /// engage at), and the drive only ever jumps to another star, never to somewhere in the same system. Cruisers, destroyers
    /// and small craft carry no drive and are refused at the helm: the jump gates and lanes are their way, or a berth aboard
    /// a carrier. The
    /// helm's own escorts go with it (U.SPC-T419): those with drives of their own alongside, fighters and small craft
    /// docked aboard as far as the hangars have room (<see cref="FtlCarriage"/>).
    ///
    /// The drive's physics (U.SPC-T418, <see cref="FtlRules"/>): no charge inside a planet's or a moon's well or a gravity
    /// projector's field either, none while the drive cools from its last drop-out, and none without the reaction mass
    /// the jump burns; the hull comes out with the velocity it went in with.
    ///
    /// Engine-free and driven only by the sim clock, so the Runtime helm just feeds it the hull's place and heading.
    /// </summary>
    public sealed class HelmFtl
    {
        /// <summary>How near the nose must be to a star to pick it and start the charge, degrees.</summary>
        public const double AlignDeg = 3;

        /// <summary>How far the nose may wander from the star while charging before the charge is lost, degrees.</summary>
        public const double HoldDeg = 8;

        public FtlStage Stage { get; private set; }
        public FtlTarget Target { get; private set; }
        public CruiseDriveSpec Drive { get; private set; }

        /// <summary>When the charge began and when it is done (the drive's <see cref="CruiseDriveSpec.SpoolDays"/>).</summary>
        public double SpoolStartJd { get; private set; } = double.NaN;
        public double ChargedJd { get; private set; } = double.NaN;

        /// <summary>Why the last charge was lost or the jump refused.</summary>
        public string AbortReason { get; private set; }

        /// <summary>The system's outer rim, km from its root: inside it the gravity wells make a jump impossible.</summary>
        public static double RimKm(StarSystem system) => CruiseDrives.WellLimitKm(system);

        /// <summary>Whether <paramref name="fromKm"/> (km from <paramref name="here"/>'s root) is beyond the outer rim, clear to jump.</summary>
        public static bool BeyondRim(StarSystem here, DVec3 fromKm) => fromKm.Length >= RimKm(here);

        /// <summary>0..1 through the charge.</summary>
        public double SpoolFraction(double jd) =>
            Stage != FtlStage.Spooling ? 0 : ChargedJd <= SpoolStartJd ? 1 : Math.Max(0, Math.Min(1, (jd - SpoolStartJd) / (ChargedJd - SpoolStartJd)));

        /// <summary>Why <paramref name="ship"/> cannot jump under its own power at all, or null if it can.</summary>
        public static string Refusal(Ship ship)
        {
            if (ship == null || ship.Spec == null) return "No ship at the helm";
            if (ship.Destroyed || ship.Derelict) return "The " + ship.Name + " is in no state to jump";
            // Who has a drive of their own is the owner's roster (U.SPC-T472): a cruiser or destroyer has none, a
            // battleship only when fitted with one.
            return FtlRoster.NoDriveReason(ship);
        }

        /// <summary>Why no jump can be made from <paramref name="fromKm"/> in <paramref name="here"/> (inside the rim), or null.</summary>
        public static string RimRefusal(StarSystem here, DVec3 fromKm) =>
            BeyondRim(here, fromKm) ? null
                : "No jump inside " + here.Name + "'s gravity wells: fly out beyond its outer rim, " +
                  (RimKm(here) / Astro.AuKm).ToString("0.##", CultureInfo.InvariantCulture) + " AU from its star (" +
                  (fromKm.Length / Astro.AuKm).ToString("0.##", CultureInfo.InvariantCulture) + " AU now)";

        /// <summary>
        /// Why no jump can be made from <paramref name="fromKm"/> in <paramref name="here"/> for gravity (U.SPC-T418): inside the
        /// outer rim, inside a planet's or a moon's well, or inside a gravity projector's field (<paramref name="galaxy"/>'s).
        /// Null if clear.
        /// </summary>
        public static string WellRefusal(StarSystem here, DVec3 fromKm, Galaxy galaxy = null)
        {
            var rim = RimRefusal(here, fromKm);
            if (rim != null) return rim;
            var body = FtlRules.BodyWellAt(here, fromKm);
            if (body != null) return "No jump inside " + body.Name + "'s gravity well: fly further from it";
            if (galaxy != null)
            {
                var at = here.At(fromKm);
                foreach (var p in galaxy.DeepSpace.Projectors)
                    if (p.Active && p.Field.Contains(at)) return "A gravity projector, " + p.Name + ", holds the drive down here: fly clear of its field";
            }
            return null;
        }

        /// <summary>
        /// Why <paramref name="ship"/>'s drive cannot charge for a jump of <paramref name="km"/> at <paramref name="jd"/> on its
        /// own account (U.SPC-T418): still cooling from its last drop-out, or too little in the tanks. Null if it can.
        /// </summary>
        public static string DriveRefusal(Ship ship, double km, double jd)
        {
            var drive = CruiseDrives.Of(ship);
            if (drive == null) return Refusal(ship);
            double cool = FtlRules.CoolUntil(ship) - jd;
            if (cool > 0)
                return "The FTL drive is cooling from its last jump: " + (cool * 24).ToString("0.0", CultureInfo.InvariantCulture) + " h more";
            double need = FtlRules.PropellantFor(drive, ship, km / Astro.LightYearKm);
            if (need > ship.Propellant + 1e-9)
                return "Not enough reaction mass to jump " + (km / Astro.LightYearKm).ToString("0.00", CultureInfo.InvariantCulture) + " ly: it takes " +
                       need.ToString("N0", CultureInfo.InvariantCulture) + " t, the tanks hold " + ship.Propellant.ToString("N0", CultureInfo.InvariantCulture) +
                       " t (range " + FtlRules.RangeLy(drive, ship).ToString("0.#", CultureInfo.InvariantCulture) + " ly)";
            return null;
        }

        /// <summary>The way to <paramref name="target"/> from <paramref name="fromKm"/> in <paramref name="here"/> (unit), and how far, km.</summary>
        public static DVec3 Bearing(FtlTarget target, StarSystem here, DVec3 fromKm, out double km)
        {
            var d = target.LocalKm(here) - fromKm;
            km = d.Length;
            return km > 0 ? d / km : DVec3.UnitX;
        }

        /// <summary>The angle between <paramref name="forward"/> and the way to <paramref name="target"/>, degrees.</summary>
        public static double OffNoseDeg(FtlTarget target, StarSystem here, DVec3 fromKm, DVec3 forward)
        {
            var dir = Bearing(target, here, fromKm, out double km);
            if (km <= 0 || forward.LengthSquared <= 0) return 180;
            double cos = Math.Max(-1, Math.Min(1, DVec3.Dot(dir, forward.Normalized)));
            return Math.Acos(cos) * Astro.Rad2Deg;
        }

        /// <summary>The star the nose is on: the one nearest dead ahead within <paramref name="toleranceDeg"/> (of two as near, the nearer). Null if none.</summary>
        public static FtlTarget Pointed(IEnumerable<FtlTarget> targets, StarSystem here, DVec3 fromKm, DVec3 forward, double toleranceDeg = AlignDeg)
        {
            FtlTarget best = null;
            double bestDeg = double.MaxValue, bestKm = double.MaxValue;
            foreach (var t in targets)
            {
                Bearing(t, here, fromKm, out double km);
                double deg = OffNoseDeg(t, here, fromKm, forward);
                if (deg > toleranceDeg) continue;
                bool better = deg < bestDeg - 0.1 || (Math.Abs(deg - bestDeg) <= 0.1 && km < bestKm);
                if (!better) continue;
                best = t;
                bestDeg = deg;
                bestKm = km;
            }
            return best;
        }

        /// <summary>
        /// Starts the charge for <paramref name="target"/> at <paramref name="jd"/>: the ship must carry a drive, be out of
        /// any fight, be beyond the system's outer rim and clear of every other well and projector's field (U.SPC-T418), have
        /// its nose on another star, a cool drive and the reaction mass for the jump. False (with <see cref="AbortReason"/>) if not.
        /// </summary>
        public bool Spool(Ship ship, StarSystem here, FtlTarget target, DVec3 fromKm, DVec3 forward, bool inCombat, double jd, Galaxy galaxy = null)
        {
            AbortReason = Refusal(ship);
            if (AbortReason == null && here == null) AbortReason = "Nowhere to jump from";
            if (AbortReason == null) AbortReason = WellRefusal(here, fromKm, galaxy);
            if (AbortReason == null && target == null) AbortReason = "No star ahead to jump to: put the nose on one";
            if (AbortReason == null && target.System == here) AbortReason = "No jump within a system: only to another star";
            if (AbortReason == null && inCombat) AbortReason = "The drive will not spool in a fight";
            if (AbortReason == null && OffNoseDeg(target, here, fromKm, forward) > HoldDeg) AbortReason = "The nose is off " + target.Name + ": line it up first";
            if (AbortReason == null)
            {
                Bearing(target, here, fromKm, out double km);
                AbortReason = DriveRefusal(ship, km, jd);
            }
            if (AbortReason != null) return false;
            Target = target;
            Drive = CruiseDrives.Of(ship);
            Stage = FtlStage.Spooling;
            SpoolStartJd = jd;
            ChargedJd = jd + Drive.SpoolDays;
            return true;
        }

        /// <summary>
        /// A step of the charge at <paramref name="jd"/>, the hull at <paramref name="fromKm"/> heading <paramref name="forward"/>:
        /// lost if the nose wanders, a fight starts or the hull falls back inside the rim; done when its time is up.
        /// </summary>
        public FtlEvent Step(StarSystem here, DVec3 fromKm, DVec3 forward, bool inCombat, double jd, Galaxy galaxy = null)
        {
            if (Stage != FtlStage.Spooling) return FtlEvent.None;
            if (inCombat) return Abort("Charge lost: the drive will not hold in a fight");
            if (!BeyondRim(here, fromKm)) return Abort("Charge lost: back inside " + here.Name + "'s gravity wells");
            var well = WellRefusal(here, fromKm, galaxy);
            if (well != null) return Abort("Charge lost: " + well);
            if (OffNoseDeg(Target, here, fromKm, forward) > HoldDeg) return Abort("Charge lost: the nose swung off " + Target.Name);
            return jd >= ChargedJd ? FtlEvent.Charged : FtlEvent.None;
        }

        /// <summary>Calls the charge off; the reason is kept in <see cref="AbortReason"/>.</summary>
        public FtlEvent Abort(string why)
        {
            AbortReason = why;
            Reset();
            return FtlEvent.Aborted;
        }

        /// <summary>Back to idle.</summary>
        public void Reset()
        {
            Stage = FtlStage.Idle;
            Target = null;
            Drive = null;
            SpoolStartJd = ChargedJd = double.NaN;
        }

        /// <summary>
        /// Charged: <paramref name="ship"/> (at <paramref name="fromKm"/> in <paramref name="here"/>) goes out into open space
        /// under its own drive (<see cref="DeepSpace"/>), already charged, bound for the edge of the star's well. A plain star
        /// on the charts is built into a running system first, so there is somewhere to arrive. The hull itself goes, the same
        /// object (<see cref="Fleet.LetGo"/> leaves a marker in its slot here), never a copy; the cruise is marked
        /// <see cref="Cruise.Piloted"/>, so nobody else gives it orders when it drops out. Null (with
        /// <see cref="AbortReason"/>) if there is nowhere to go.
        /// </summary>
        public Cruise EngageInterstellar(Galaxy galaxy, StarSystem here, Ship ship, DVec3 fromKm, double jd, DVec3 velocityKmS = default) =>
            EngageInterstellar(galaxy, here, ship, fromKm, jd, velocityKmS, out _);

        /// <summary>
        /// The helm's escorts that go with <paramref name="ship"/> through FTL (U.SPC-T419): those with drives of their own,
        /// then the fighters and small craft that fit docked aboard it or them, and the escorts that fit clamped to a carrier's
        /// hull (U.SPC-T473) (<paramref name="aboard"/>: craft to carrier). An escort still fighting or left a hulk stays
        /// behind, as does one with no berth or clamp to ride on.
        /// </summary>
        public static List<Ship> Wing(World world, Ship ship, out Dictionary<Ship, Ship> aboard)
        {
            var going = new List<Ship>();
            var craft = new List<Ship>();
            foreach (var e in world.Fleet.Ships)
            {
                if (e == ship || e.Leader != ship || e.Destroyed || e.Derelict || e.Battle != null) continue;
                if (CruiseDrives.Of(e) != null) going.Add(e);
                else craft.Add(e);
            }
            var carriers = new List<Ship> { ship };
            carriers.AddRange(going);
            aboard = FtlCarriage.Stow(carriers, craft);
            foreach (var e in craft)
                if (aboard.ContainsKey(e)) going.Add(e);
            return going;
        }

        /// <summary>As <see cref="EngageInterstellar(Galaxy, StarSystem, Ship, DVec3, double, DVec3)"/>, saying how many of the helm's
        /// escorts went with it (<paramref name="wing"/>: alongside or docked aboard, U.SPC-T419).</summary>
        public Cruise EngageInterstellar(Galaxy galaxy, StarSystem here, Ship ship, DVec3 fromKm, double jd, DVec3 velocityKmS, out int wing)
        {
            wing = 0;
            if (Stage != FtlStage.Spooling || Target == null) throw new InvalidOperationException("The drive is not charged");
            var to = Target.System ?? galaxy.Realize(Target.Name, jd);
            if (to == null || to == here)
            {
                Abort("Nothing charted at " + Target.Name + " to drop out at");
                return null;
            }
            var world = here.World;
            // Its escorts go too (U.SPC-T419): alongside under their own drives, or docked aboard. Found before the hull
            // leaves the fleet, which would hand them over to the marker it leaves behind.
            var escorts = Wing(world, ship, out var aboard);
            world.War?.Forget(ship);
            world.Economy?.Release(ship);
            // The hull itself goes, not a copy: its place in this fleet keeps a marker so no other id moves.
            if (world.Fleet.LetGo(ship) == null)
            {
                Abort("The " + ship.Name + " is not in " + here.Name + "'s fleet to jump from");
                return null;
            }
            ship.Role = ShipRole.Free;
            var ships = new List<Ship> { ship };
            var hired = new HashSet<Ship>();
            foreach (var e in escorts)
            {
                if (world.Escorts != null && world.Escorts.IsHired(e))
                {
                    hired.Add(e);
                    world.Escorts.UnmarkHired(e);
                }
                world.War?.Forget(e);
                world.Economy?.Release(e);
                if (world.Fleet.LetGo(e) == null) continue;
                e.Leader = ship;
                ships.Add(e);
            }
            wing = ships.Count - 1;
            var news = galaxy.Traffic?.News.Bundle(here, jd);
            var c = galaxy.DeepSpace.Launch(ships, here, fromKm, to, Drive, jd, news, charged: true);
            c.Piloted = true;
            // FTL carries momentum (U.SPC-T418): the hull comes out moving as it went in.
            c.CarriedVelocityKmS = velocityKmS;
            foreach (var h in hired)
                if (ships.Contains(h)) c.Hired.Add(h);
            foreach (var kv in aboard)
                if (ships.Contains(kv.Key)) c.Aboard[kv.Key] = kv.Value;
            // Craft in its hangars, and escorts clamped to its hull (U.SPC-T473).
            int clamped = FtlCarriage.CountClamped(c.Aboard.Keys);
            int docked = c.Aboard.Count - clamped;
            string with = FtlCarriage.Describe(wing - c.Aboard.Count, docked, clamped);
            world.Log(jd, $"{ship.Name} of {ship.Faction.Name} engages its {Drive.Name.ToLowerInvariant()} beyond the rim for {to.Name}, " +
                          $"{c.Profile.Ly.ToString("0.00", CultureInfo.InvariantCulture)} ly under its own power{with}", WorldEventKind.Info, here.Catalog.Root.Index);
            Reset();
            return c;
        }
    }
}
