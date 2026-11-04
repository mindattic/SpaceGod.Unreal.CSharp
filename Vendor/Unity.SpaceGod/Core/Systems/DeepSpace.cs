using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A group (flagship first, then its escorts) crossing open space between the stars under its own cruise drive
    /// (U.SPC-T412, see <see cref="CruiseDriveSpec"/>). Like a ship inside a gate it belongs to no system's fleet while it
    /// is out here (the same hulls that left the system behind, a gone marker left in their old slots, see
    /// <see cref="Fleet.LetGo"/>), but unlike a gate passage it is somewhere the whole way: a real place in the
    /// galaxy (<see cref="PositionAt"/>), moving at a real speed, where it can meet other groups and find what lies out
    /// here, and from where it flies into the far system at the edge of its star's well.
    /// </summary>
    public sealed class Cruise
    {
        /// <summary>Flagship first, then its escorts.</summary>
        public readonly List<Ship> Ships = new List<Ship>();

        /// <summary>Which of <see cref="Ships"/> were hired fighters in the system it left (so the far system's escorts take them on).</summary>
        public readonly HashSet<Ship> Hired = new HashSet<Ship>();

        /// <summary>Which of <see cref="Ships"/> ride docked aboard another of them, and aboard which (U.SPC-T419, see
        /// <see cref="FtlCarriage"/>): fighters and small craft with no drive of their own, carried in a capital ship's
        /// hangars, and escorts clamped to a carrier's hull (U.SPC-T473). They come out with the group where it drops out,
        /// and are lost with the hull carrying them.</summary>
        public readonly Dictionary<Ship, Ship> Aboard = new Dictionary<Ship, Ship>();

        /// <summary>The craft docked aboard <paramref name="carrier"/>.</summary>
        public List<Ship> CarriedBy(Ship carrier)
        {
            var list = new List<Ship>();
            foreach (var kv in Aboard)
                if (kv.Value == carrier) list.Add(kv.Key);
            return list;
        }

        /// <summary>The system it set out from.</summary>
        public StarSystem From;

        /// <summary>The system it is bound for, or null when it is bound for a star nobody simulates (see <see cref="ToName"/>).</summary>
        public StarSystem To;

        /// <summary>The name of where it is bound: the system's, or the plain star's.</summary>
        public string ToName;

        /// <summary>The drive it flies on (the flagship's).</summary>
        public CruiseDriveSpec Drive;

        /// <summary>Where it will drop out: the edge of the far star's well, on the side it comes in from.</summary>
        public GalacticPosition Target;

        /// <summary>This leg: where it began, where it ends (the <see cref="Target"/>), when its charge began, and its speed through it.</summary>
        public GalacticPosition Start, End;
        public double LegJd;
        public CruiseProfile Profile;

        /// <summary>Dropped out in open space (to fight, to salvage, or at a plain star it was bound for), until <see cref="HoldUntilJd"/>
        /// (infinite: until it is given somewhere else to go, see <see cref="DeepSpace.Retarget"/>).</summary>
        public bool Holding;
        public double HoldUntilJd;

        /// <summary>When it first engaged, from its home system.</summary>
        public double SetOutJd;

        /// <summary>The news it carries from the system it left (U.SPC-T100), handed over where it arrives.</summary>
        public List<SystemReport> News;

        /// <summary>The player's own hull, jumped here from the helm (U.SPC-T417, see <see cref="HelmFtl"/>): when it drops
        /// out nobody else sends it anywhere, the helm takes it on from there.</summary>
        public bool Piloted;

        /// <summary>Not a group flying but a course being worked out for one (see <see cref="FtlNavigation"/>): it reckons with
        /// only the stars on the charts, so a course never gives away a star nobody has discovered (U.SPC-T456).</summary>
        public bool Plan;

        /// <summary>The hull's own velocity in normal space when it engaged (ecliptic, km/s): FTL carries momentum, so it
        /// comes out with the velocity it went in with (U.SPC-T418, see <see cref="FtlRules"/>).</summary>
        public DVec3 CarriedVelocityKmS;

        /// <summary>Pulled out of FTL by a star's well or a gravity projector (U.SPC-T418): crossing it now under its
        /// ordinary drive, from the edge it was pulled out at to the far side (this leg's <see cref="End"/>), where it spools
        /// again for its <see cref="Target"/>.</summary>
        public bool Crossing;

        /// <summary>What pulled it out last (a star's or a projector's name), or null.</summary>
        public string InterdictedBy;

        public Ship Leader => Ships[0];

        /// <summary>When this leg ends (it drops out at its <see cref="End"/>); infinite while it holds.</summary>
        public double ArriveJd => Holding ? double.PositiveInfinity : LegJd + Profile.TotalDays;

        /// <summary>The way this leg heads, ecliptic (unit).</summary>
        public DVec3 Direction => End.KmFrom(Start).Normalized;

        /// <summary>Under way at <paramref name="jd"/>: past its charge and not yet dropped out.</summary>
        public bool Engaged(double jd) => !Holding && jd >= LegJd + Profile.SpoolDays && jd < ArriveJd;

        /// <summary>Where it is at <paramref name="jd"/>: on its leg's line, by its speed profile, from where it began.</summary>
        public GalacticPosition PositionAt(double jd)
        {
            if (Holding || jd <= LegJd + Profile.SpoolDays) return Start;
            if (jd >= ArriveJd) return End;
            return Start.Offset(Direction * (Profile.LyAt(jd - LegJd) * Astro.LightYearKm));
        }

        /// <summary>Speed at <paramref name="jd"/>, km/s.</summary>
        public double SpeedKmS(double jd) =>
            Holding ? 0 : Profile.SpeedAt(jd - LegJd) * Astro.LightYearKm / Astro.SecondsPerDay;

        /// <summary>Light-years still to go to its <see cref="Target"/>.</summary>
        public double LyToGo(double jd) => Target.DistanceLy(PositionAt(jd));

        /// <summary>
        /// Sets a new leg from <paramref name="from"/> to <see cref="Target"/>, its charge starting at <paramref name="jd"/>
        /// (none if the drive is already <paramref name="charged"/>, as at the helm, U.SPC-T417), once every hull's drive has
        /// cooled from its last drop-out; every hull pays the jump's reaction mass (U.SPC-T418, <see cref="FtlRules.PropellantFor"/>).
        /// </summary>
        internal void SetLeg(GalacticPosition from, double jd, bool charged = false)
        {
            Holding = false;
            Crossing = false;
            HoldUntilJd = double.NaN;
            Start = from;
            End = Target;
            LegJd = jd;
            double ly = Target.DistanceLy(from);
            double cool = 0;
            foreach (var s in Ships)
            {
                cool = Math.Max(cool, FtlRules.CoolUntil(s) - jd);
                s.Propellant -= FtlRules.PropellantFor(Drive, s, ly);
            }
            Profile = charged ? new CruiseProfile(ly, Drive.LyPerDay, Drive.RampDays, 0)
                : new CruiseProfile(ly, Drive.LyPerDay, Drive.RampDays, Drive.SpoolDays + Math.Max(0, cool));
        }

        /// <summary>Pulled out at <paramref name="entry"/> at <paramref name="jd"/>: across the field to <paramref name="exit"/>
        /// under the ordinary drive, at <see cref="FtlRules.CrossingSpeedKmS"/>.</summary>
        internal void SetCrossing(GalacticPosition entry, GalacticPosition exit, double jd)
        {
            Holding = false;
            Crossing = true;
            HoldUntilJd = double.NaN;
            Start = entry;
            End = exit;
            LegJd = jd;
            double lyPerDay = FtlRules.CrossingSpeedKmS.Value * Astro.SecondsPerDay / Astro.LightYearKm;
            Profile = new CruiseProfile(exit.DistanceLy(entry), lyPerDay, 1e-6, 0);
        }

        public override string ToString() => Leader + " bound for " + ToName;
    }

    /// <summary>What lies out between the stars for a passing group to find.</summary>
    public enum DeepSpaceObjectKind
    {
        /// <summary>What is left of a hull destroyed out here.</summary>
        Wreck,

        /// <summary>A hull left drifting whole, its crew gone.</summary>
        Derelict,

        /// <summary>A planet with no star, wandering between them (U.SPC-T421): its own small gravity well pulls a passing
        /// group out of FTL like a star's does (<see cref="DeepSpace.RogueWells"/>).</summary>
        RoguePlanet,

        /// <summary>A cloud of gas and dust light-hours or more across (U.SPC-T421): inside it sensors see only a fraction as
        /// far (<see cref="DeepSpace.SensorRangeKm"/>), so groups slip past each other and wrecks go unseen.</summary>
        Nebula,

        /// <summary>A comet or an iceball adrift between the stars (U.SPC-T421): water a group short of reaction mass can
        /// drop out to mine (<see cref="IceRefuelling"/>).</summary>
        IceBody,

        /// <summary>A power's deep-space relay on the line between two systems (U.SPC-T421): it passes the news between them
        /// when the gates cannot (<see cref="DeepSpace.RelayLagDays"/>).</summary>
        Relay,

        /// <summary>A power's listening post out on an approach (U.SPC-T421): it hears its enemies pass and tells home
        /// (<see cref="DeepSpaceOutposts"/>).</summary>
        ListeningPost,

        /// <summary>A hull adrift calling for help, its crew still aboard (U.SPC-T421, <see cref="DistressCalls"/>), or a
        /// pirate lure dressed up as one.</summary>
        Distress,
    }

    /// <summary>Something in open space between the stars (U.SPC-T412): a wreck or a derelict, where it was left, and what is aboard.</summary>
    public sealed class DeepSpaceObject
    {
        public DeepSpaceObjectKind Kind;
        public string Name;
        public GalacticPosition Position;

        /// <summary>What a salvager takes off it, credits.</summary>
        public double SalvageCredits;

        /// <summary>Whose hull it was.</summary>
        public Faction LostBy;

        /// <summary>When it was left out here.</summary>
        public double SinceJd;

        /// <summary>How far it reaches, km: a nebula's extent (0 for a point-like thing).</summary>
        public double RadiusKm;

        /// <summary>A rogue planet's gravitational parameter, km³/s² (its well is sized from it by <see cref="FtlRules"/>).</summary>
        public double Gm;

        /// <summary>Whose it is: a relay's or a listening post's power.</summary>
        public Faction Owner;

        /// <summary>The system at the end of its route that it reports to (a relay's or a listening post's home).</summary>
        public string HomeName;

        /// <summary>The two stars of the route it lies on (U.SPC-T421), in name order; null for what was left off any route.</summary>
        public string RouteA, RouteB;

        /// <summary>What the first survey of it pays its surveyor, credits (more for one off the gate network), and who made it.</summary>
        public double SurveyCredits;
        public Faction SurveyedBy;

        /// <summary>Water an ice body still holds for a passing group's tanks, tonnes.</summary>
        public double IceTonnes;

        /// <summary>A distress call's crew still alive aboard, until it falls silent at <see cref="ExpiresJd"/>.</summary>
        public int Survivors;
        public double ExpiresJd = double.PositiveInfinity;

        /// <summary>A false distress call: the pirate clan lying in wait at it.</summary>
        public Faction LureBy;

        /// <summary>The leaders a listening post has already heard pass.</summary>
        public HashSet<Ship> Heard;

        /// <summary>Lies off the gate network (on a route no gate link joins).</summary>
        public bool OffNetwork;

        public override string ToString() => Name;
    }

    /// <summary>A line of what has happened out between the stars, for the logs and the HUD.</summary>
    public sealed class DeepSpaceEvent
    {
        public double Jd;
        public string Text;
        public GalacticPosition Where;
    }

    /// <summary>
    /// Something that can happen to the groups out between the stars (U.SPC-T412): asked once a <see cref="DeepSpace.StepDays"/>
    /// step for what happened in it. A new kind of deep-space encounter plugs in by registering a factory in
    /// <see cref="DeepSpace.EncounterKinds"/>, never by editing the loop that runs them.
    /// </summary>
    public interface IDeepSpaceEncounter
    {
        void Step(DeepSpace space, double fromJd, double toJd, Random rng);
    }

    /// <summary>
    /// The open space between the star systems (U.SPC-T412): the groups crossing it under their own cruise drives
    /// (<see cref="Cruise"/>), the wrecks and derelicts left out here, and what happens when they meet. It is not empty
    /// and it does not stop: every step the groups fly on, the encounters (<see cref="IDeepSpaceEncounter"/>) are run,
    /// and a group at the end of its way drops out at the edge of its star's well and flies into that system as any
    /// ship there would, from the far side of the star it came from. A group bound for a star nobody simulates drops
    /// out there and holds in open space until it is sent on (<see cref="ReachedStar"/>, <see cref="Retarget"/>).
    ///
    /// Positions are <see cref="GalacticPosition"/>s, sector and offset, so a group a long way from Sol is placed as
    /// exactly as one next to it.
    /// </summary>
    public sealed partial class DeepSpace
    {
        readonly Galaxy galaxy;
        readonly Random rng;
        readonly List<Cruise> cruises = new List<Cruise>();
        readonly List<DeepSpaceObject> objects = new List<DeepSpaceObject>();
        readonly List<DeepSpaceEvent> events = new List<DeepSpaceEvent>();
        double nextMarkJd = double.NaN;

        /// <summary>The groups out between the stars now, under way or holding.</summary>
        public IReadOnlyList<Cruise> Cruises => cruises;

        /// <summary>The wrecks and derelicts out between the stars.</summary>
        public IReadOnlyList<DeepSpaceObject> Objects => objects;

        /// <summary>What has happened out here lately, oldest first (the last <see cref="MaxEvents"/>).</summary>
        public IReadOnlyList<DeepSpaceEvent> Events => events;

        /// <summary>Every hull out between the stars now (in no system's fleet until it drops out into one).</summary>
        public IEnumerable<Ship> InFlight()
        {
            foreach (var c in cruises)
                foreach (var s in c.Ships)
                    yield return s;
        }

        /// <summary>The encounters run every step: one of each kind in <see cref="EncounterKinds"/>, made when the deep space was.</summary>
        public readonly List<IDeepSpaceEncounter> Encounters = new List<IDeepSpaceEncounter>();

        /// <summary>The kinds of encounter every new deep space runs (the extension point: add a factory to add a kind).</summary>
        public static readonly List<Func<IDeepSpaceEncounter>> EncounterKinds = new List<Func<IDeepSpaceEncounter>>
        {
            () => new FtlInterdiction(),
            () => new CruiseMeeting(),
            () => new WreckSalvage(),
            // Life in deep space (U.SPC-T421): what lies out here, and who is out here with it.
            () => new SurveyFinds(),
            () => new IceRefuelling(),
            () => new DistressCalls(),
            () => new DeepSpaceOutposts(),
            () => new RouteAmbushes(),
        };

        /// <summary>How often (days) the encounters are run: about every hour and a quarter, on a fixed grid, so what
        /// happens never depends on the frame rate.</summary>
        public const double StepDays = 0.05;

        /// <summary>How many lines of <see cref="Events"/> are kept.</summary>
        public const int MaxEvents = 200;

        /// <summary>The most wrecks and derelicts kept: past it, the oldest are lost to the dark.</summary>
        public const int MaxObjects = 400;

        /// <summary>Raised when a group drops out into a simulated system and is in its fleet: (the group, when). The traffic takes it from there.</summary>
        public event Action<Cruise, double> Arrived;

        /// <summary>Raised when a group drops out at a plain star it was bound for and holds there: (the group, when).</summary>
        public event Action<Cruise, double> ReachedStar;

        public DeepSpace(Galaxy galaxy, int seed = 4127)
        {
            this.galaxy = galaxy;
            rng = new Random(seed);
            foreach (var make in EncounterKinds) Encounters.Add(make());
        }

        // ---------------------------------------------------------------- setting out

        /// <summary>
        /// Where a ship in <paramref name="from"/> engages its drive for <paramref name="target"/>, km from the system's root:
        /// at the edge of the star's well, on the line to the target.
        /// </summary>
        public static DVec3 DeparturePointKm(StarSystem from, GalacticPosition target)
        {
            var way = target.KmFrom(from.Origin);
            var dir = way.LengthSquared > 0 ? way.Normalized : DVec3.UnitX;
            return dir * CruiseDrives.WellLimitKm(from);
        }

        /// <summary>Where a group from <paramref name="from"/> drops out at <paramref name="star"/>: the edge of its well
        /// (<paramref name="wellKm"/> out), on the side facing the way it comes in.</summary>
        public static GalacticPosition ArrivalPoint(GalacticPosition star, GalacticPosition from, double wellKm)
        {
            var back = from.KmFrom(star);
            var dir = back.LengthSquared > 0 ? back.Normalized : DVec3.UnitX;
            return star.Offset(dir * wellKm);
        }

        /// <summary>
        /// Puts <paramref name="ships"/> (the same hulls, flagship first, already out of every fleet) into open space at
        /// <paramref name="fromKm"/> in <paramref name="from"/> (km from its root), bound for <paramref name="to"/> on
        /// <paramref name="drive"/>; it charges from <paramref name="jd"/> (or engages at once if already <paramref name="charged"/>).
        /// It drops out at the edge of <paramref name="to"/>'s well on the side facing the way it comes, or, given
        /// <paramref name="dropDir"/> (a way out from the star in its system's frame), there: a jump strike group coming in on
        /// a flank (U.SPC-T474).
        /// </summary>
        public Cruise Launch(IEnumerable<Ship> ships, StarSystem from, DVec3 fromKm, StarSystem to, CruiseDriveSpec drive, double jd,
            List<SystemReport> news = null, bool charged = false, DVec3? dropDir = null)
        {
            var start = from.At(fromKm);
            double wellKm = CruiseDrives.WellLimitKm(to);
            var target = dropDir is DVec3 d && d.LengthSquared > 0 ? to.Origin.Offset(d.Normalized * wellKm) : ArrivalPoint(to.Origin, start, wellKm);
            return Launch(ships, from, start, to, to.Name, target, drive, jd, news, charged);
        }

        /// <summary>As <see cref="Launch(IEnumerable{Ship}, StarSystem, DVec3, StarSystem, CruiseDriveSpec, double, List{SystemReport})"/>, but bound
        /// for a plain star (one nobody simulates yet): it drops out at the edge of its well and holds there.</summary>
        public Cruise LaunchToStar(IEnumerable<Ship> ships, StarSystem from, DVec3 fromKm, NearbyStar star, CruiseDriveSpec drive, double jd,
            List<SystemReport> news = null)
        {
            var start = from.At(fromKm);
            return Launch(ships, from, start, null, star.Name, ArrivalPoint(star.Origin, start, CruiseDrives.WellLimitKm(null)), drive, jd, news, false);
        }

        Cruise Launch(IEnumerable<Ship> ships, StarSystem from, GalacticPosition start, StarSystem to, string toName, GalacticPosition target,
            CruiseDriveSpec drive, double jd, List<SystemReport> news, bool charged)
        {
            if (drive == null) throw new ArgumentNullException(nameof(drive), "A cruise needs a drive");
            var c = new Cruise { From = from, To = to, ToName = toName, Drive = drive, Target = target, SetOutJd = jd, News = news };
            // What lies along the way is there before anyone flies it (U.SPC-T421): the same for every flight of this route.
            if (TryStar(toName, out var star)) SeedRoute(from.Name, from.Origin, toName, star);
            c.Ships.AddRange(ships);
            if (c.Ships.Count == 0) throw new ArgumentException("A cruise needs a ship", nameof(ships));
            foreach (var s in c.Ships)
            {
                s.Destroyed = false;
                s.GateDeparted = false;
            }
            c.SetLeg(start, jd, charged);
            cruises.Add(c);
            Log(jd, $"{c.Leader.Name}{More(c)} engage{(c.Ships.Count > 1 ? "" : "s")} {drive.Name.ToLowerInvariant()}s out of {from.Name} for {toName}, " +
                    $"{c.Profile.Ly.ToString("0.00", CultureInfo.InvariantCulture)} ly, due in {c.Profile.TotalDays.ToString("0.0", CultureInfo.InvariantCulture)} days", start);
            return c;
        }

        static string More(Cruise c) => c.Ships.Count > 1 ? $" and {c.Ships.Count - 1} more" : "";

        /// <summary>Sends <paramref name="c"/> on to <paramref name="to"/> from where it is, its charge beginning at <paramref name="jd"/>.</summary>
        public void Retarget(Cruise c, StarSystem to, double jd)
        {
            var here = c.PositionAt(jd);
            if (TryStar(c.ToName, out var was)) SeedRoute(c.ToName, was, to.Name, to.Origin);
            c.To = to;
            c.ToName = to.Name;
            c.Target = ArrivalPoint(to.Origin, here, CruiseDrives.WellLimitKm(to));
            c.SetLeg(here, jd);
            Log(jd, $"{c.Leader.Name}{More(c)} set{(c.Ships.Count > 1 ? "" : "s")} out from open space for {to.Name}, " +
                    $"{c.Profile.Ly.ToString("0.00", CultureInfo.InvariantCulture)} ly", here);
        }

        /// <summary>Drops <paramref name="c"/> out of its cruise where it is at <paramref name="jd"/>, for <paramref name="days"/> (then on
        /// toward its target again, charging first), or for good with an infinite hold.</summary>
        public void DropOut(Cruise c, double jd, double days)
        {
            var here = c.PositionAt(jd);
            c.Start = c.End = here;
            c.Holding = true;
            c.Crossing = false;
            c.HoldUntilJd = jd + days;
            foreach (var s in c.Ships) FtlRules.StartCooldown(s, c.Drive, jd);
        }

        // ---------------------------------------------------------------- gravity fields (U.SPC-T418)

        readonly List<GravityProjector> projectors = new List<GravityProjector>();

        /// <summary>The gravity projectors out between the stars.</summary>
        public IReadOnlyList<GravityProjector> Projectors => projectors;

        /// <summary>Places <paramref name="p"/> in open space: from now it pulls passing hulls (not its owner's) out of FTL.</summary>
        public GravityProjector AddProjector(GravityProjector p)
        {
            if (p != null && !projectors.Contains(p)) projectors.Add(p);
            return p;
        }

        public void RemoveProjector(GravityProjector p) => projectors.Remove(p);

        /// <summary>A source of fields that pull a group out of FTL: (the deep space, the group, a place on its way, how far
        /// about it to look, km) to the fields there. A new kind of interdiction plugs in by adding one.</summary>
        public delegate IEnumerable<GravityField> FieldSource(DeepSpace space, Cruise c, GalacticPosition near, double km);

        /// <summary>Where interdicting fields come from: the stars' own mass, and the gravity projectors.</summary>
        public static readonly List<FieldSource> FieldSources = new List<FieldSource> { StarWells, ProjectorFields, RogueWells };

        /// <summary>The wells of the stars (running systems and plain ones on the charts) within <paramref name="km"/> of <paramref name="near"/>.</summary>
        public static IEnumerable<GravityField> StarWells(DeepSpace space, Cruise c, GalacticPosition near, double km)
        {
            var g = space.galaxy;
            if (g == null) yield break;
            foreach (var s in g.Systems)
            {
                if (s.Origin.DistanceKm(near) >= km + 200 * Astro.AuKm) continue;
                var f = FtlRules.StarWell(s);
                if (f.RadiusKm > 0 && f.Center.DistanceKm(near) < km + f.RadiusKm) yield return f;
            }
            foreach (var n in g.NearbyStars)
            {
                if (n.Origin.DistanceKm(near) >= km + 200 * Astro.AuKm) continue;
                var f = FtlRules.StarWell(n);
                if (f.Center.DistanceKm(near) < km + f.RadiusKm) yield return f;
            }
            // A star nobody has discovered pulls a group out all the same (U.SPC-T456): it is there, charted or not. Only a
            // course being planned cannot reckon with it.
            if (c != null && c.Plan) yield break;
            foreach (var n in g.Undiscovered)
            {
                if (n.Origin.DistanceKm(near) >= km + 200 * Astro.AuKm) continue;
                var f = FtlRules.StarWell(n);
                if (f.Center.DistanceKm(near) < km + f.RadiusKm) yield return f;
            }
        }

        /// <summary>The fields of the projectors that would pull <paramref name="c"/> out (any but its own side's).</summary>
        public static IEnumerable<GravityField> ProjectorFields(DeepSpace space, Cruise c, GalacticPosition near, double km)
        {
            foreach (var p in space.projectors)
            {
                if (!p.Active || p.RadiusKm <= 0) continue;
                if (p.Owner != null && c.Leader.Faction == p.Owner) continue;
                if (p.Position.DistanceKm(near) < km + p.RadiusKm) yield return p.Field;
            }
        }

        /// <summary>Every field about the run from <paramref name="from"/> to <paramref name="to"/> that would pull <paramref name="c"/> out.</summary>
        public IEnumerable<GravityField> FieldsAlong(Cruise c, GalacticPosition from, GalacticPosition to)
        {
            var mid = GalacticPosition.Lerp(from, to, 0.5);
            double half = to.DistanceKm(from) / 2;
            foreach (var source in FieldSources)
                foreach (var f in source(this, c, mid, half))
                    yield return f;
        }

        /// <summary>
        /// Pulls <paramref name="c"/> out of FTL at <paramref name="entry"/> on the edge of <paramref name="field"/> at
        /// <paramref name="jd"/>: its drives start cooling, and it crosses the field under its ordinary drive, straight on
        /// toward where it was going, to spool again on the far side.
        /// </summary>
        public void Interdict(Cruise c, GravityField field, GalacticPosition entry, double jd)
        {
            var way = c.Target.KmFrom(entry);
            var dir = way.LengthSquared > 0 ? way.Normalized : c.Direction;
            var exit = FtlRules.ExitPoint(field, entry, dir);
            // Never past where it was going.
            if (exit.DistanceKm(entry) > c.Target.DistanceKm(entry)) exit = c.Target;
            foreach (var s in c.Ships) FtlRules.StartCooldown(s, c.Drive, jd);
            c.InterdictedBy = field.Name;
            c.SetCrossing(entry, exit, jd);
            string what = field.Kind == GravityFieldKind.Projector ? "the gravity projector " + field.Name : field.Name + "'s gravity well";
            bool many = c.Ships.Count > 1;
            Log(jd, $"{c.Leader.Name}{More(c)} {(many ? "are" : "is")} pulled out of FTL by {what}, crossing it under {(many ? "their" : "its")} ordinary drive", entry);
            // A star nobody knew was there is found by falling into its well (U.SPC-T456).
            if (field.Kind != GravityFieldKind.Projector) galaxy?.Discover(field.Name, jd, "the " + c.Leader.Name);
        }

        // ---------------------------------------------------------------- the step

        /// <summary>
        /// Everything out between the stars to <paramref name="jd"/>: groups holding go on when their hold is up, groups due
        /// drop out at their stars (at the moment each is due, in that order), and the encounters run on the fixed grid of
        /// <see cref="StepDays"/>.
        /// </summary>
        public void Update(double jd)
        {
            if (double.IsNaN(jd)) return;
            if (double.IsNaN(nextMarkJd) || cruises.Count == 0) nextMarkJd = Mark(jd) + StepDays;
            while (nextMarkJd <= jd)
            {
                double mark = nextMarkJd;
                Due(mark);
                foreach (var e in Encounters) e.Step(this, mark - StepDays, mark, rng);
                nextMarkJd += StepDays;
            }
            Due(jd);
        }

        static double Mark(double jd) => Math.Floor(jd / StepDays + 1e-9) * StepDays;

        /// <summary>Holds that are up, and arrivals due, by <paramref name="jd"/>, in the order they fall.</summary>
        void Due(double jd)
        {
            while (true)
            {
                Cruise next = null;
                double when = double.PositiveInfinity;
                foreach (var c in cruises)
                {
                    double t = c.Holding ? c.HoldUntilJd : c.ArriveJd;
                    if (t <= jd && t < when)
                    {
                        when = t;
                        next = c;
                    }
                }
                if (next == null) return;
                if (next.Holding) next.SetLeg(next.Start, when);
                else if (next.Crossing && next.End.DistanceKm(next.Target) > 1)
                {
                    // Clear of the field on the far side: it spools again for where it was going.
                    next.SetLeg(next.End, when);
                    string many = next.Ships.Count > 1 ? "" : "s";
                    Log(when, $"{next.Leader.Name}{More(next)} clear{many} {next.InterdictedBy}'s pull and spool{many} again for {next.ToName}", next.Start);
                }
                else Drop(next, when);
            }
        }

        /// <summary>At the end of its way: into the far system's fleet, flying in from the well's edge, or holding at a plain star.</summary>
        void Drop(Cruise c, double jd)
        {
            if (c.To == null)
            {
                c.Start = c.End = c.Target;
                c.Holding = true;
                c.Crossing = false;
                c.HoldUntilJd = double.PositiveInfinity;
                foreach (var s in c.Ships) FtlRules.StartCooldown(s, c.Drive, jd);
                Log(jd, $"{c.Leader.Name}{More(c)} drop{(c.Ships.Count > 1 ? "" : "s")} out at {c.ToName}", c.Target);
                ReachedStar?.Invoke(c, jd);
                return;
            }
            cruises.Remove(c);
            var system = c.To;
            var world = system.World;
            var fleet = world.Fleet;
            // At the edge of the system, clear of any planet's well too (U.SPC-T418).
            var at = FtlRules.ClearOfBodyWells(system, system.LocalKm(c.Target));
            // Facing in, toward the star, as it comes out of its cruise.
            var forward = at.LengthSquared > 0 ? (-at).Normalized : DVec3.UnitX;
            var up = Math.Abs(forward.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
            var right = DVec3.Cross(forward, up).Normalized;
            foreach (var s in c.Ships)
            {
                // Still exactly as it was engaging its drive (U.SPC-T453: the same object, never reset mid-flight)
                // until now, dropping out at the edge of this system's well.
                s.ResetForArrival();
                FtlRules.StartCooldown(s, c.Drive, jd);
                s.Orbit = new ParkingOrbit
                {
                    Body = system.Catalog.Root.Index, RadiusKm = 0, Center = at, U = right, V = forward,
                    EpochJd = jd, PhaseAtEpoch = 0, RateRadPerDay = 0, Fixed = true,
                };
                s.Phase = ShipPhase.Parked;
                s.PhaseStartJd = jd;
                s.PhaseEndJd = jd;
                s.DestinationBody = s.Orbit.Body;
                s.Ports = world.FreeShipPorts();
                s.GateKm = null;
                fleet.Receive(s);
                // A fresh id here: the group keeps its own identity from ids this fleet handed out (as at a gate, U.SPC-T363).
                s.BandId = s.Leader == null ? s.Id : s.Leader.BandId;
                if (c.Hired.Contains(s)) world.Escorts?.MarkHired(s);
            }
            world.Log(jd, $"{c.Leader.Name}{More(c)} of {c.Leader.Faction.Name} drop{(c.Ships.Count > 1 ? "" : "s")} out of cruise at the edge of {system.Name}, " +
                          $"in from {c.From?.Name ?? "open space"}", WorldEventKind.Info, system.Catalog.Root.Index);
            // A gate picket near where it drops out sees it and warns its navy if it is hostile (U.SPC-T480).
            GatePickets.OnDropOut(system, c.Ships, at, jd);
            Arrived?.Invoke(c, jd);
            // Nobody sent it here on business: a free ship, it finds its own way in (the player's own, jumped from the helm,
            // is the helm's to fly on from here, U.SPC-T417).
            var leader = c.Leader;
            if (leader.Voyage == null && !leader.Destroyed && !c.Piloted)
            {
                leader.Role = leader.Role == ShipRole.Escort ? ShipRole.Free : leader.Role;
                fleet.ScheduleNext(leader, jd);
            }
            foreach (var s in c.Ships) world.NotifyAdded(s);
        }

        // ---------------------------------------------------------------- what happens out here

        /// <summary>Notes <paramref name="text"/> in the deep-space log.</summary>
        public void Log(double jd, string text, GalacticPosition where)
        {
            events.Add(new DeepSpaceEvent { Jd = jd, Text = text, Where = where });
            if (events.Count > MaxEvents) events.RemoveRange(0, events.Count - MaxEvents);
        }

        /// <summary>Leaves <paramref name="o"/> out here (the oldest wrecks, derelicts and distress calls lost when there are
        /// more than <see cref="MaxObjects"/> of them; what lies along the routes, U.SPC-T421, stays).</summary>
        public void Add(DeepSpaceObject o)
        {
            objects.Add(o);
            if (!DeepSpaceFeatures.Transient(o.Kind)) return;
            int transient = 0;
            foreach (var x in objects)
                if (DeepSpaceFeatures.Transient(x.Kind)) transient++;
            for (int i = 0; i < objects.Count && transient > MaxObjects; i++)
                if (DeepSpaceFeatures.Transient(objects[i].Kind))
                {
                    objects.RemoveAt(i--);
                    transient--;
                }
        }

        /// <summary>Takes <paramref name="o"/> away (salvaged).</summary>
        public void Remove(DeepSpaceObject o) => objects.Remove(o);

        /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> would fight on sight (the one diplomacy table every system shares).</summary>
        public bool Hostile(Faction a, Faction b) => galaxy.Polities.Diplomacy.Hostile(a, b);

        /// <summary>
        /// Destroys <paramref name="ship"/> of <paramref name="c"/> out here at <paramref name="jd"/>, leaving its wreck: the
        /// next hull takes the lead (and the voyage) if it was the flagship, and the group is gone when none is left.
        /// </summary>
        public void Destroy(Cruise c, Ship ship, double jd, string how)
        {
            if (!c.Ships.Contains(ship)) return;
            // Craft docked aboard go down with the hull carrying them (U.SPC-T419).
            foreach (var craft in c.CarriedBy(ship)) Destroy(c, craft, jd, "docked aboard the " + ship.Name);
            int i = c.Ships.IndexOf(ship);
            var where = c.PositionAt(jd);
            c.Ships.RemoveAt(i);
            c.Hired.Remove(ship);
            c.Aboard.Remove(ship);
            ship.Destroyed = true;
            if (ship.Combat != null) ship.Combat.Hull = 0;
            Add(new DeepSpaceObject
            {
                Kind = DeepSpaceObjectKind.Wreck, Name = "the wreck of the " + ship.Name, Position = where.Offset(new DVec3(i * 3, 0, 0)),
                SalvageCredits = WreckValue(ship), LostBy = ship.Faction, SinceJd = jd,
            });
            Log(jd, $"{ship.Name} of {ship.Faction.Name} is destroyed in open space {how}", where);
            if (c.Ships.Count == 0)
            {
                ship.Voyage?.LostWithAllHands();
                cruises.Remove(c);
                return;
            }
            if (i == 0)
            {
                // The next hull flying on its own drive takes command (not a craft docked aboard it, U.SPC-T472), and
                // what the group was out here to do; it leads the group from the front of the line.
                var heir = c.Ships.Find(s => !c.Aboard.ContainsKey(s)) ?? c.Ships[0];
                if (c.Ships[0] != heir)
                {
                    c.Ships.Remove(heir);
                    c.Ships.Insert(0, heir);
                }
                heir.Leader = null;
                heir.Voyage = ship.Voyage;
                heir.Role = ship.Role;
                heir.Ports = ship.Ports;
                foreach (var s in c.Ships)
                    if (s != heir) s.Leader = heir;
                ship.Voyage = null;
            }
        }

        /// <summary>What a salvager gets off a wreck: some of its cargo's worth, and its parts.</summary>
        public static double WreckValue(Ship ship) =>
            WreckPartsCredits * (ship.Spec != null && ship.Spec.IsMilitary ? 3 : 1) + (ship.HasCargo ? Math.Max(0, ship.CargoCost) * 0.4 : 0);

        /// <summary>What a wreck's parts are worth to a salvager, credits (three times as much for a warship's).</summary>
        public const double WreckPartsCredits = 40000;

        /// <summary>
        /// How close (km) the paths of <paramref name="a"/> and <paramref name="b"/> come between <paramref name="fromJd"/> and
        /// <paramref name="toJd"/>: the nearest point of their separation, taken as straight over so short a step.
        /// </summary>
        public static double ClosestKm(Cruise a, Cruise b, double fromJd, double toJd) =>
            Closest(a.PositionAt(fromJd).KmFrom(b.PositionAt(fromJd)), a.PositionAt(toJd).KmFrom(b.PositionAt(toJd)));

        /// <summary>How close (km) <paramref name="c"/> passes <paramref name="place"/> between <paramref name="fromJd"/> and <paramref name="toJd"/>.</summary>
        public static double ClosestKm(Cruise c, GalacticPosition place, double fromJd, double toJd) =>
            Closest(c.PositionAt(fromJd).KmFrom(place), c.PositionAt(toJd).KmFrom(place));

        static double Closest(DVec3 p0, DVec3 p1)
        {
            var d = p1 - p0;
            double len2 = d.LengthSquared;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, -DVec3.Dot(p0, d) / len2)) : 0;
            return (p0 + d * t).Length;
        }

        /// <summary>A group's fighting strength out here: its hulls' together.</summary>
        public static double Strength(Cruise c)
        {
            double s = 0;
            foreach (var ship in c.Ships)
                if (!ship.Destroyed && ship.Combat != null) s += ship.Combat.Strength;
            return s;
        }
    }

    /// <summary>
    /// A group under way whose straight run takes it into a gravity field (U.SPC-T418): a star's well (not the one it
    /// left nor the one it is bound for) or a gravity projector's. It is pulled out at the field's edge, at the moment
    /// it reaches it, and crosses under its ordinary drive (see <see cref="DeepSpace.Interdict"/>).
    /// </summary>
    public sealed class FtlInterdiction : IDeepSpaceEncounter
    {
        public void Step(DeepSpace space, double fromJd, double toJd, Random rng)
        {
            if (space.Cruises.Count == 0) return;
            foreach (var c in new List<Cruise>(space.Cruises))
            {
                if (c.Ships.Count == 0 || c.Holding || c.Crossing) continue;
                double t0 = Math.Max(fromJd, c.LegJd + c.Profile.SpoolDays), t1 = Math.Min(toJd, c.ArriveJd);
                if (!(t1 > t0)) continue;
                var p0 = c.PositionAt(t0);
                var p1 = c.PositionAt(t1);
                List<GravityField> fields = null;
                // Not the fields it set out from or is bound into: those are the ends of its way.
                foreach (var f in space.FieldsAlong(c, p0, p1))
                    if (!f.Contains(c.Start, 1.001) && !f.Contains(c.Target, 1.001)) (fields ??= new List<GravityField>()).Add(f);
                if (fields == null || !FtlRules.FirstEntry(p0, p1, fields, out var hit, out double along)) continue;
                var entry = GalacticPosition.Lerp(p0, p1, along);
                space.Interdict(c, hit, entry, When(c, entry, t0, t1));
            }
        }

        /// <summary>When <paramref name="c"/> reaches <paramref name="at"/> on its leg, between <paramref name="t0"/> and <paramref name="t1"/>.</summary>
        static double When(Cruise c, GalacticPosition at, double t0, double t1)
        {
            double ly = at.DistanceLy(c.Start);
            for (int i = 0; i < 48; i++)
            {
                double mid = (t0 + t1) / 2;
                if (c.Profile.LyAt(mid - c.LegJd) < ly) t0 = mid;
                else t1 = mid;
            }
            return (t0 + t1) / 2;
        }
    }

    /// <summary>
    /// Hostile groups whose paths cross between the stars (U.SPC-T412): a navy and its enemy's ships, or raiders and a
    /// hull worth their while, within <see cref="RangeKm"/> of each other. The stronger hand may pull the other out of
    /// cruise and fight. Raiders who win rob the hold and let the hull go (U.SPC-T374: a hull is worth more taken than
    /// shot apart); a navy that wins leaves the beaten group wrecked out here. The loser's escorts are lost with it.
    /// Either way the winners hold a while (the fight, the boarding) and then go on.
    /// </summary>
    public sealed class CruiseMeeting : IDeepSpaceEncounter
    {
        /// <summary>How near (km) two groups' paths must come for either to pull the other out of cruise: about a light-hour.</summary>
        public const double RangeKm = 1e9;

        /// <summary>How long a fight out here keeps the winners from going on, days.</summary>
        public const double FightDays = 0.25;

        /// <summary>A hand must be at least this share as strong as the other to start a fight.</summary>
        public const double NerveRatio = 0.5;

        public void Step(DeepSpace space, double fromJd, double toJd, Random rng)
        {
            if (space.Cruises.Count < 2) return;
            // Over the groups as they stood at the start of the step: a fight may take one out of the list.
            var list = new List<Cruise>(space.Cruises);
            var done = new HashSet<Cruise>();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var a = list[i];
                    var b = list[j];
                    // One fight a step for any group.
                    if (done.Contains(a) || done.Contains(b) || a.Ships.Count == 0 || b.Ships.Count == 0) continue;
                    if (!(a.Engaged(toJd) || a.Engaged(fromJd)) && !(b.Engaged(toJd) || b.Engaged(fromJd))) continue;
                    if (!space.Hostile(a.Leader.Faction, b.Leader.Faction)) continue;
                    double closest = DeepSpace.ClosestKm(a, b, fromJd, toJd);
                    if (closest > RangeKm) continue;
                    // Inside a nebula sensors see only so far (U.SPC-T421).
                    if (closest > Math.Min(space.SensorRangeKm(a.PositionAt(toJd), RangeKm), space.SensorRangeKm(b.PositionAt(toJd), RangeKm))) continue;
                    if (!Resolve(space, a, b, toJd, rng)) continue;
                    done.Add(a);
                    done.Add(b);
                }
        }

        static bool Aggressive(Ship leader) => leader.Faction.IsPirate || leader.Spec.IsMilitary;

        static bool Loot(Cruise c)
        {
            foreach (var s in c.Ships)
                if (s.HasCargo || s.PlunderTonnes > 0) return true;
            return false;
        }

        /// <summary>Whether a fight happens between <paramref name="a"/> and <paramref name="b"/>, and its outcome.</summary>
        static bool Resolve(DeepSpace space, Cruise a, Cruise b, double jd, Random rng)
        {
            double sa = DeepSpace.Strength(a), sb = DeepSpace.Strength(b);
            // Who would start it: one with guns and the will; of two, the stronger.
            bool aWill = Aggressive(a.Leader) && sa > 0 && sa >= sb * NerveRatio && (!a.Leader.Faction.IsPirate || Loot(b));
            bool bWill = Aggressive(b.Leader) && sb > 0 && sb >= sa * NerveRatio && (!b.Leader.Faction.IsPirate || Loot(a));
            if (!aWill && !bWill) return false;
            var attacker = aWill && (!bWill || sa >= sb) ? a : b;
            var defender = attacker == a ? b : a;
            double sAtt = attacker == a ? sa : sb, sDef = attacker == a ? sb : sa;
            double win = sDef <= 0 ? 1 : sAtt / (sAtt + sDef);
            bool attackerWins = rng.NextDouble() < win;
            var where = attacker.PositionAt(jd);
            space.Log(jd, $"{attacker.Leader.Name} of {attacker.Leader.Faction.Name} pulls {defender.Leader.Name} of {defender.Leader.Faction.Name} out of cruise", where);
            var winner = attackerWins ? attacker : defender;
            var loser = attackerWins ? defender : attacker;
            double sLoser = attackerWins ? sDef : sAtt, sWinner = attackerWins ? sAtt : sDef;
            // The winners pay for it.
            double hurt = sLoser + sWinner > 0 ? 0.35 * sLoser / (sLoser + sWinner) : 0;
            foreach (var s in winner.Ships)
                if (s.Combat != null)
                    s.Combat.Hull = Math.Max(Math.Min(s.Combat.Hull, s.Combat.Spec.Hull * 0.05), s.Combat.Hull - s.Combat.Spec.Hull * hurt);
            if (attackerWins && attacker.Leader.Faction.IsPirate && defender.Leader.Faction != null && !defender.Leader.Spec.IsMilitary)
            {
                // Raiders rob a hull they have run down rather than destroy it.
                double tonnes = 0;
                var robber = attacker.Leader;
                robber.Plunder ??= new double[GoodInfo.Count];
                foreach (var s in defender.Ships)
                {
                    if (s.HasCargo)
                    {
                        robber.Plunder[(int)s.CargoGood] += s.CargoTonnes;
                        tonnes += s.CargoTonnes;
                        s.HasCargo = false;
                        s.CargoTonnes = 0;
                        s.CargoCost = 0;
                        s.Cargo = null;
                    }
                    if (s.Plunder != null)
                        for (int g = 0; g < s.Plunder.Length && g < robber.Plunder.Length; g++)
                        {
                            robber.Plunder[g] += s.Plunder[g];
                            tonnes += s.Plunder[g];
                            s.Plunder[g] = 0;
                        }
                }
                space.Log(jd, $"{robber.Name}'s raiders rob {defender.Leader.Name} of {tonnes.ToString("N0", CultureInfo.InvariantCulture)} t and let it go", where);
                space.DropOut(defender, jd, FightDays);
            }
            else
            {
                foreach (var s in new List<Ship>(loser.Ships)) space.Destroy(loser, s, jd, $"by {winner.Leader.Faction.Name}");
            }
            if (winner.Ships.Count > 0) space.DropOut(winner, jd, FightDays);
            return true;
        }
    }

    /// <summary>
    /// A group passing within <see cref="RangeKm"/> of a wreck or derelict (U.SPC-T412) drops out to strip it, keeps what
    /// it finds (the flagship's purse and its flag's treasury), and goes on after <see cref="SalvageDays"/>. A war fleet
    /// on campaign does not stop for it.
    /// </summary>
    public sealed class WreckSalvage : IDeepSpaceEncounter
    {
        /// <summary>How near (km) a group must pass for its sensors to pick out a wreck: about a light-hour.</summary>
        public const double RangeKm = 1e9;

        /// <summary>How long stripping a wreck takes, days.</summary>
        public const double SalvageDays = 0.5;

        public void Step(DeepSpace space, double fromJd, double toJd, Random rng)
        {
            if (space.Objects.Count == 0) return;
            foreach (var c in new List<Cruise>(space.Cruises))
            {
                if (c.Ships.Count == 0 || !(c.Engaged(toJd) || c.Engaged(fromJd))) continue;
                var leader = c.Leader;
                if (leader.Voyage?.Purpose == VoyagePurpose.Campaign) continue;
                foreach (var o in space.Objects)
                {
                    // Only what is left of a hull is stripped (U.SPC-T421: not a rogue planet or a power's relay), and only one seen.
                    if (o.Kind != DeepSpaceObjectKind.Wreck && o.Kind != DeepSpaceObjectKind.Derelict) continue;
                    if (o.SinceJd >= toJd) continue;
                    double closest = DeepSpace.ClosestKm(c, o.Position, fromJd, toJd);
                    if (closest > RangeKm || closest > space.SensorRangeKm(o.Position, RangeKm)) continue;
                    leader.Credits += o.SalvageCredits;
                    leader.Faction.Treasury += o.SalvageCredits;
                    space.Remove(o);
                    space.Log(toJd, $"{leader.Name} of {leader.Faction.Name} drops out to strip {o.Name}: " +
                                    $"{o.SalvageCredits.ToString("N0", CultureInfo.InvariantCulture)} cr of salvage", o.Position);
                    space.DropOut(c, toJd, SalvageDays);
                    break;
                }
            }
        }
    }
}
