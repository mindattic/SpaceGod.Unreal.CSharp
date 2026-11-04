using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>Where on a route a kind of deep-space feature is put.</summary>
    public enum FeaturePlacement
    {
        /// <summary>Anywhere along the way, a little off the line.</summary>
        Scattered,

        /// <summary>Half way, on the line (a relay between two systems).</summary>
        Midway,

        /// <summary>On a settled end's approach, a tenth of the way out (a listening post).</summary>
        Approach,
    }

    /// <summary>
    /// One kind of thing that lies out between the stars (U.SPC-T421): how many of it a route holds, on the gate network
    /// and off it, where along the route it is put, and what it is made of (<see cref="Make"/>). A new kind plugs in by
    /// registering one with <see cref="DeepSpaceFeatures.Register"/>, never by editing the seeding.
    /// </summary>
    public sealed class DeepSpaceFeatureSpec
    {
        public DeepSpaceObjectKind Kind;

        /// <summary>What it is called in the logs ("rogue planet").</summary>
        public string Noun;

        /// <summary>How many lie along ten light-years of a route the gates join, and of one they do not.</summary>
        public double OnNetworkPer10Ly, OffNetworkPer10Ly;

        public FeaturePlacement Placement;

        /// <summary>The most km off the route's line it lies (a scattered one).</summary>
        public double MaxOffsetKm;

        /// <summary>Owned by a power at a settled end of the route (a relay, a listening post): none is put where there is none.</summary>
        public bool Owned;

        /// <summary>Lost to the dark when too many are left out here (wrecks, derelicts, distress calls).</summary>
        public bool Transient;

        /// <summary>What its first survey pays, credits (times <see cref="DeepSpaceFeatures.OffNetworkSurveyFactor"/> off the network).</summary>
        public double SurveyCredits;

        /// <summary>Fills in what this one is: its name, size, mass, salvage (the route's own numbers, so the same every time).</summary>
        public Action<DeepSpaceObject, Random> Make;
    }

    /// <summary>
    /// The things that lie out between the stars (U.SPC-T421): rogue planets, nebulae, ice bodies, derelicts, and the
    /// powers' relays and listening posts. Every route a group flies (or the helm plots) is seeded with them the first
    /// time, from the route's own name and the galaxy's seed, so the same route always holds the same things. Routes the
    /// gates do not join hold the most, and pay the most to survey.
    /// </summary>
    public static class DeepSpaceFeatures
    {
        /// <summary>Jupiter's gravitational parameter, km³/s² (a rogue planet's mass is reckoned in it).</summary>
        public const double JupiterGm = 1.26686534e8;

        /// <summary>What a first survey off the gate network pays, against one on it.</summary>
        public const double OffNetworkSurveyFactor = 3;

        /// <summary>The longest route seeded, light-years (beyond it the stars are too far apart for anyone to fly it).</summary>
        public const double MaxRouteLy = 60;

        /// <summary>The most of one kind put along one route.</summary>
        public const int MaxPerRoute = 6;

        static readonly string[] Greek = { "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa", "Lambda", "Sigma", "Tau", "Omega" };
        static readonly string[] Wanderers = { "Wanderer", "Nomad", "Drifter", "Orphan", "Vagrant", "Exile", "Stray" };
        static readonly string[] Veils = { "Ash", "Amber", "Cinder", "Grey", "Lantern", "Pale", "Smoke", "Violet", "Widow's", "Shroud" };
        static readonly string[] Ices = { "Frostbody", "Iceball", "Snowdrift", "Rimecomet", "Glacier" };
        static readonly string[] Hulls = { "Cormorant", "Good Hope", "Patience", "Halcyon", "Wayfarer", "Silver Thread", "Long Reach", "Fair Wind", "Kestrel", "Providence" };

        static string Pick(string[] list, Random r) => list[r.Next(list.Length)];

        static readonly Dictionary<DeepSpaceObjectKind, DeepSpaceFeatureSpec> specs = new Dictionary<DeepSpaceObjectKind, DeepSpaceFeatureSpec>();

        static DeepSpaceFeatures()
        {
            Register(new DeepSpaceFeatureSpec
            {
                Kind = DeepSpaceObjectKind.RoguePlanet, Noun = "rogue planet", OnNetworkPer10Ly = 0, OffNetworkPer10Ly = 0.8,
                // Within two of its own wells of the line: about half pull a passing group out.
                MaxOffsetKm = 2 * FtlRules.WellRadiusKm(3 * JupiterGm, 1.5), SurveyCredits = 250000,
                Make = (o, r) =>
                {
                    o.Gm = JupiterGm * (0.05 + 2.95 * r.NextDouble() * r.NextDouble());
                    o.Name = "the rogue planet " + Pick(Wanderers, r) + " " + r.Next(100, 1000).ToString(CultureInfo.InvariantCulture);
                },
            });
            Register(new DeepSpaceFeatureSpec
            {
                Kind = DeepSpaceObjectKind.Nebula, Noun = "nebula", OnNetworkPer10Ly = 0, OffNetworkPer10Ly = 0.4,
                MaxOffsetKm = 5e9, SurveyCredits = 400000,
                Make = (o, r) =>
                {
                    o.RadiusKm = 5e9 + r.NextDouble() * 1.5e10;
                    o.Name = "the " + Pick(Veils, r) + " Veil";
                },
            });
            Register(new DeepSpaceFeatureSpec
            {
                Kind = DeepSpaceObjectKind.IceBody, Noun = "ice body", OnNetworkPer10Ly = 0.8, OffNetworkPer10Ly = 1.2,
                MaxOffsetKm = 8e8, SurveyCredits = 60000,
                Make = (o, r) =>
                {
                    o.IceTonnes = 5000 + r.NextDouble() * 45000;
                    o.Name = Pick(Ices, r) + " " + Pick(Greek, r) + "-" + r.Next(10, 100).ToString(CultureInfo.InvariantCulture);
                },
            });
            Register(new DeepSpaceFeatureSpec
            {
                Kind = DeepSpaceObjectKind.Derelict, Noun = "derelict", OnNetworkPer10Ly = 0.3, OffNetworkPer10Ly = 0.6,
                MaxOffsetKm = 8e8, Transient = true, SurveyCredits = 0,
                Make = (o, r) =>
                {
                    o.SalvageCredits = 50000 + r.NextDouble() * 350000;
                    o.Name = "the derelict " + Pick(Hulls, r);
                },
            });
            Register(new DeepSpaceFeatureSpec
            {
                Kind = DeepSpaceObjectKind.Relay, Noun = "relay", Placement = FeaturePlacement.Midway, Owned = true,
                OnNetworkPer10Ly = double.PositiveInfinity, OffNetworkPer10Ly = double.PositiveInfinity,
                Make = (o, r) => o.Name = "the " + o.HomeName + " deep-space relay " + Pick(Greek, r),
            });
            Register(new DeepSpaceFeatureSpec
            {
                Kind = DeepSpaceObjectKind.ListeningPost, Noun = "listening post", Placement = FeaturePlacement.Approach, Owned = true,
                OnNetworkPer10Ly = double.PositiveInfinity, OffNetworkPer10Ly = double.PositiveInfinity,
                Make = (o, r) => o.Name = "the " + o.Owner.Name + " listening post " + Pick(Greek, r) + " " + r.Next(1, 20).ToString(CultureInfo.InvariantCulture),
            });
            Register(new DeepSpaceFeatureSpec { Kind = DeepSpaceObjectKind.Wreck, Noun = "wreck", Transient = true });
            Register(new DeepSpaceFeatureSpec { Kind = DeepSpaceObjectKind.Distress, Noun = "distress call", Transient = true });
        }

        /// <summary>Adds (or replaces) the kind <paramref name="spec"/> describes; null <paramref name="spec"/> with
        /// <see cref="Unregister"/> takes one away.</summary>
        public static void Register(DeepSpaceFeatureSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            specs[spec.Kind] = spec;
        }

        public static void Unregister(DeepSpaceObjectKind kind) => specs.Remove(kind);

        public static DeepSpaceFeatureSpec Of(DeepSpaceObjectKind kind) => specs.TryGetValue(kind, out var s) ? s : null;

        /// <summary>Every registered kind, in the enum's order (so seeding draws its numbers the same way every time).</summary>
        public static List<DeepSpaceFeatureSpec> All()
        {
            var list = new List<DeepSpaceFeatureSpec>(specs.Values);
            list.Sort((a, b) => a.Kind.CompareTo(b.Kind));
            return list;
        }

        /// <summary>Lost to the dark when there are too many (see <see cref="DeepSpace.Add"/>).</summary>
        public static bool Transient(DeepSpaceObjectKind kind) => Of(kind)?.Transient ?? true;

        /// <summary>A hash of <paramref name="s"/> that is the same in every run (string.GetHashCode is not).</summary>
        public static ulong StableHash(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char ch in s)
            {
                h ^= ch;
                h *= 1099511628211UL;
            }
            return h;
        }
    }

    /// <summary>A route between two stars that has been seeded with what lies along it (U.SPC-T421).</summary>
    public sealed class DeepSpaceRoute
    {
        /// <summary>The two ends, in name order, and where they are.</summary>
        public string A, B;
        public GalacticPosition AtA, AtB;

        /// <summary>A gate link joins the two (so it is charted, and its finds pay less).</summary>
        public bool OnNetwork;

        public double Ly => AtB.DistanceLy(AtA);

        public string Name => A + "–" + B;
    }

    /// <summary>A band lying in wait in open space with a gravity projector on an FTL route (U.SPC-T421).</summary>
    public sealed class DeepSpaceAmbush
    {
        public Cruise Band;
        public GravityProjector Projector;

        /// <summary>When it lifts, and the leg the band was holding on (a fight that sends it off early ends it too).</summary>
        public double UntilJd;
        public double LegJd;

        public string Where;
    }

    public sealed partial class DeepSpace
    {
        /// <summary>
        /// Whether routes are seeded with what lies along them (U.SPC-T421). The default galaxy turns it on; a bare galaxy
        /// built by hand (a test's two empty beacons) keeps open space empty unless it asks.
        /// </summary>
        public bool RouteLife;

        readonly HashSet<string> seededRoutes = new HashSet<string>(StringComparer.Ordinal);
        readonly List<DeepSpaceRoute> routes = new List<DeepSpaceRoute>();
        readonly List<DeepSpaceAmbush> ambushes = new List<DeepSpaceAmbush>();
        readonly HashSet<string> surveyedStars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Numbers for the life out here, apart from the fights' (so they draw as they did before it).</summary>
        readonly Random lifeRng = new Random(9157);

        internal Random LifeRng => lifeRng;
        internal Galaxy Galaxy => galaxy;

        /// <summary>The routes seeded so far.</summary>
        public IReadOnlyList<DeepSpaceRoute> Routes => routes;

        /// <summary>The bands lying in wait on the routes now.</summary>
        public IReadOnlyList<DeepSpaceAmbush> Ambushes => ambushes;

        /// <summary>Souls brought home from distress calls, and finds surveyed, so far.</summary>
        public int Rescued { get; internal set; }
        public int SurveyFindsMade { get; internal set; }

        /// <summary>How far sensors see inside a nebula, as a share of the open-space range.</summary>
        public const double NebulaSensorShare = 0.1;

        /// <summary>The field a band lying in wait throws, km: big enough to pull a passer out within reach of its guns.</summary>
        public const double AmbushFieldKm = 2e8;

        /// <summary>Where a star or a system named <paramref name="name"/> is, if it is one.</summary>
        public bool TryStar(string name, out GalacticPosition at)
        {
            at = default;
            if (name == null || galaxy == null) return false;
            var s = galaxy.Find(name);
            if (s != null)
            {
                at = s.Origin;
                return true;
            }
            foreach (var n in galaxy.NearbyStars)
                if (string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    at = n.Origin;
                    return true;
                }
            return false;
        }

        static string RouteKey(string a, string b) =>
            string.CompareOrdinal(a.ToUpperInvariant(), b.ToUpperInvariant()) <= 0 ? a.ToUpperInvariant() + "|" + b.ToUpperInvariant() : b.ToUpperInvariant() + "|" + a.ToUpperInvariant();

        /// <summary>Whether the route between <paramref name="a"/> and <paramref name="b"/> has been seeded.</summary>
        public bool Seeded(string a, string b) => a != null && b != null && seededRoutes.Contains(RouteKey(a, b));

        /// <summary>
        /// Seeds the route between two stars with what lies along it, once (U.SPC-T421; nothing when <see cref="RouteLife"/>
        /// is off): drawn from the route's names and the galaxy's seed, so it is the same whichever way it is first flown.
        /// Returns whether it seeded anything new.
        /// </summary>
        public bool SeedRoute(string aName, GalacticPosition a, string bName, GalacticPosition b)
        {
            if (!RouteLife || aName == null || bName == null || string.Equals(aName, bName, StringComparison.OrdinalIgnoreCase)) return false;
            string key = RouteKey(aName, bName);
            if (!seededRoutes.Add(key)) return false;
            // Always from the end first in name order, whichever way it is flown.
            if (string.CompareOrdinal(aName.ToUpperInvariant(), bName.ToUpperInvariant()) > 0)
            {
                (aName, bName) = (bName, aName);
                (a, b) = (b, a);
            }
            var route = new DeepSpaceRoute { A = aName, B = bName, AtA = a, AtB = b, OnNetwork = Linked(aName, bName) };
            double ly = route.Ly;
            if (ly < 0.05 || ly > DeepSpaceFeatures.MaxRouteLy) return false;
            routes.Add(route);
            var r = new Random((int)(DeepSpaceFeatures.StableHash(key) ^ (ulong)(galaxy?.Seed ?? 0)) & int.MaxValue);
            foreach (var spec in DeepSpaceFeatures.All())
            {
                if (spec.Make == null) continue;
                if (spec.Owned)
                {
                    PlaceOwned(spec, route, r);
                    continue;
                }
                double expected = Math.Min(DeepSpaceFeatures.MaxPerRoute, (route.OnNetwork ? spec.OnNetworkPer10Ly : spec.OffNetworkPer10Ly) * ly / 10);
                int n = (int)Math.Floor(expected) + (r.NextDouble() < expected - Math.Floor(expected) ? 1 : 0);
                for (int i = 0; i < n; i++) Place(spec, route, 0.12 + 0.76 * r.NextDouble(), spec.MaxOffsetKm * Math.Sqrt(r.NextDouble()), r, null, null);
            }
            return true;
        }

        /// <summary>Every pair of running systems within <see cref="DeepSpaceFeatures.MaxRouteLy"/>, seeded now (the routes the
        /// powers' relays and listening posts already stand on when the galaxy begins).</summary>
        public void SeedSystemRoutes()
        {
            if (galaxy == null) return;
            var list = galaxy.Systems;
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    SeedRoute(list[i].Name, list[i].Origin, list[j].Name, list[j].Origin);
        }

        bool Linked(string a, string b)
        {
            var net = galaxy?.Network;
            if (net == null) return false;
            foreach (var link in net.Links)
            {
                string x = link.A?.Name, y = link.B?.Name;
                if ((string.Equals(x, a, StringComparison.OrdinalIgnoreCase) && string.Equals(y, b, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(x, b, StringComparison.OrdinalIgnoreCase) && string.Equals(y, a, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }

        /// <summary>A settled system's own power (its government's navy), or null.</summary>
        StarSystem SettledEnd(string name, out Faction power)
        {
            power = null;
            var s = galaxy?.Find(name);
            if (s == null || !s.Settled || s.World?.Sol == null) return null;
            power = s.World.Sol;
            return s;
        }

        void PlaceOwned(DeepSpaceFeatureSpec spec, DeepSpaceRoute route, Random r)
        {
            var endA = SettledEnd(route.A, out var powerA);
            var endB = SettledEnd(route.B, out var powerB);
            if (spec.Placement == FeaturePlacement.Midway)
            {
                // A relay joins two running systems, kept by the power of either.
                if (galaxy.Find(route.A) == null || galaxy.Find(route.B) == null || (endA == null && endB == null)) return;
                bool atA = endA != null && (endB == null || r.NextDouble() < 0.5);
                Place(spec, route, 0.5, 0, r, atA ? powerA : powerB, atA ? route.A : route.B);
                return;
            }
            // A listening post on each settled end's approach whose power has a navy to tell.
            if (endA != null && powerA.IsMilitary) Place(spec, route, 0.1, 1e8 * r.NextDouble(), r, powerA, route.A);
            if (endB != null && powerB.IsMilitary) Place(spec, route, 0.9, 1e8 * r.NextDouble(), r, powerB, route.B);
        }

        DeepSpaceObject Place(DeepSpaceFeatureSpec spec, DeepSpaceRoute route, double t, double offKm, Random r, Faction owner, string home)
        {
            var line = route.AtB.KmFrom(route.AtA);
            var dir = line.LengthSquared > 0 ? line.Normalized : DVec3.UnitX;
            var side = DVec3.Cross(dir, Math.Abs(dir.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX).Normalized;
            var up = DVec3.Cross(dir, side);
            double angle = r.NextDouble() * 2 * Math.PI;
            var off = (side * Math.Cos(angle) + up * Math.Sin(angle)) * offKm;
            var o = new DeepSpaceObject
            {
                Kind = spec.Kind, Position = GalacticPosition.Lerp(route.AtA, route.AtB, t).Offset(off), SinceJd = double.NegativeInfinity,
                RouteA = route.A, RouteB = route.B, OffNetwork = !route.OnNetwork, Owner = owner, HomeName = home,
                SurveyCredits = spec.SurveyCredits * (route.OnNetwork ? 1 : DeepSpaceFeatures.OffNetworkSurveyFactor),
            };
            spec.Make(o, r);
            Add(o);
            return o;
        }

        // ---------------------------------------------------------------- what the features do

        /// <summary>The wells of the rogue planets near <paramref name="near"/>: they pull a passing group out of FTL like a star's.</summary>
        public static IEnumerable<GravityField> RogueWells(DeepSpace space, Cruise c, GalacticPosition near, double km)
        {
            foreach (var o in space.objects)
            {
                if (o.Kind != DeepSpaceObjectKind.RoguePlanet || o.Gm <= 0) continue;
                double radius = FtlRules.WellRadiusKm(o.Gm);
                if (o.Position.DistanceKm(near) < km + radius) yield return new GravityField(o.Name, GravityFieldKind.Mass, o.Position, radius);
            }
        }

        /// <summary>The nebula <paramref name="at"/> is inside, or null.</summary>
        public DeepSpaceObject NebulaAt(GalacticPosition at)
        {
            foreach (var o in objects)
                if (o.Kind == DeepSpaceObjectKind.Nebula && o.Position.DistanceKm(at) < o.RadiusKm) return o;
            return null;
        }

        /// <summary>How far sensors at <paramref name="at"/> see, from their open-space range <paramref name="openKm"/>: only
        /// <see cref="NebulaSensorShare"/> of it inside a nebula.</summary>
        public double SensorRangeKm(GalacticPosition at, double openKm) => NebulaAt(at) != null ? openKm * NebulaSensorShare : openKm;

        /// <summary>How slow a deep-space relay chain passes the news against a gate's relay over the same light-years.</summary>
        public const double RelaySlowdown = 2;

        /// <summary>
        /// Days the news takes between two systems by a deep-space relay on the route joining them (U.SPC-T421), not counting
        /// the light time to and from the relay's dishes; infinite when no relay stands there.
        /// </summary>
        public double RelayLagDays(StarSystem a, StarSystem b)
        {
            if (a == null || b == null) return double.PositiveInfinity;
            foreach (var o in objects)
            {
                if (o.Kind != DeepSpaceObjectKind.Relay) continue;
                if ((string.Equals(o.RouteA, a.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(o.RouteB, b.Name, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(o.RouteA, b.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(o.RouteB, a.Name, StringComparison.OrdinalIgnoreCase)))
                    return GateNetwork.TransitDays(b.Origin.DistanceLy(a.Origin)) * RelaySlowdown;
            }
            return double.PositiveInfinity;
        }

        /// <summary>Marks the plain star <paramref name="name"/> surveyed; true the first time (its survey is a find).</summary>
        public bool SurveyStar(string name) => name != null && surveyedStars.Add(name);

        /// <summary>Whether a group's flagship surveys what it passes: a deep-space explorer, or any hull out on a survey.</summary>
        public static bool Surveys(Cruise c) =>
            c.Ships.Count > 0 && (c.Leader.Spec?.Class == ShipClass.Explorer || c.Leader.Voyage?.Purpose == VoyagePurpose.Survey);

        // ---------------------------------------------------------------- ambushes on the routes

        /// <summary>
        /// Sends <paramref name="ships"/> (out of every fleet already, flagship first) from <paramref name="from"/> to a point in
        /// open space, <paramref name="station"/>, named <paramref name="name"/>: it drops out there and holds, as at a plain star.
        /// </summary>
        public Cruise LaunchToPoint(IEnumerable<Ship> ships, StarSystem from, DVec3 fromKm, GalacticPosition station, string name, CruiseDriveSpec drive,
            double jd, List<SystemReport> news = null) =>
            Launch(ships, from, from.At(fromKm), null, name, station, drive, jd, news, false);

        /// <summary>
        /// <paramref name="band"/>, at its station (already given its leg home, starting when the wait is over), lies in wait
        /// there for <paramref name="days"/>: a gravity projector of its own pulls every passer not of its side out of FTL within
        /// reach of its guns (<see cref="CruiseMeeting"/> does the rest).
        /// </summary>
        public DeepSpaceAmbush LieInWait(Cruise band, double jd, double days, string where)
        {
            var leader = band.Leader;
            var p = AddProjector(new GravityProjector
            {
                Name = leader.Faction.Name + " interdictor under " + leader.Name, Owner = leader.Faction, Position = band.PositionAt(jd), RadiusKm = AmbushFieldKm,
            });
            var a = new DeepSpaceAmbush { Band = band, Projector = p, UntilJd = jd + days, LegJd = band.LegJd, Where = where };
            ambushes.Add(a);
            Log(jd, $"{leader.Name} of {leader.Faction.Name} lies in wait {where} with a gravity projector", p.Position);
            return a;
        }

        /// <summary>Lifts the ambushes whose time is up, whose band has gone, or whose band has set off.</summary>
        internal void LiftAmbushes(double jd)
        {
            for (int i = ambushes.Count - 1; i >= 0; i--)
            {
                var a = ambushes[i];
                bool gone = a.Band.Ships.Count == 0 || !cruises.Contains(a.Band);
                // Still at its station on the leg it was holding on (a fight there sends it off on a new one, home).
                if (!gone && jd < a.UntilJd && a.Band.LegJd == a.LegJd) continue;
                RemoveProjector(a.Projector);
                ambushes.RemoveAt(i);
                Log(jd, $"{a.Projector.Name} goes dark {a.Where}", a.Projector.Position);
            }
        }
    }

    /// <summary>
    /// Survey finds (U.SPC-T421): a deep-space explorer, or any hull out on a survey, passing within <see cref="RangeKm"/> of
    /// something nobody has surveyed (a rogue planet, a nebula, an ice body) drops out to look it over for
    /// <see cref="SurveyDays"/>, and its flag is paid for the charts: three times as much off the gate network. The player's
    /// own hull logs it on the way past without stopping.
    /// </summary>
    public sealed class SurveyFinds : IDeepSpaceEncounter
    {
        /// <summary>How near (km) a survey hull's instruments pick a find out: about five light-hours.</summary>
        public const double RangeKm = 5e9;

        public const double SurveyDays = 0.5;

        public void Step(DeepSpace space, double fromJd, double toJd, Random rng)
        {
            if (space.Objects.Count == 0) return;
            foreach (var c in new List<Cruise>(space.Cruises))
            {
                if (!DeepSpace.Surveys(c) || !(c.Engaged(toJd) || c.Engaged(fromJd))) continue;
                foreach (var o in space.Objects)
                {
                    if (o.SurveyCredits <= 0 || o.SurveyedBy != null || o.SinceJd >= toJd) continue;
                    if (DeepSpace.ClosestKm(c, o.Position, fromJd, toJd) - o.RadiusKm > RangeKm) continue;
                    var leader = c.Leader;
                    o.SurveyedBy = leader.Faction;
                    leader.Credits += o.SurveyCredits;
                    leader.Faction.Treasury += o.SurveyCredits;
                    space.SurveyFindsMade++;
                    string noun = DeepSpaceFeatures.Of(o.Kind)?.Noun ?? "find";
                    space.Log(toJd, $"Survey find: {leader.Name} of {leader.Faction.Name} charts {o.Name}, a {noun}{(o.OffNetwork ? " off the gate network" : "")}, " +
                                    $"for {o.SurveyCredits.ToString("N0", CultureInfo.InvariantCulture)} cr", o.Position);
                    if (!c.Piloted) space.DropOut(c, toJd, SurveyDays);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Ice for the tanks (U.SPC-T421): a group with a hull below <see cref="RefuelBelow"/> of its reaction mass, passing
    /// within <see cref="RangeKm"/> of an ice body with water left on it, drops out for <see cref="IceDays"/> to mine it and
    /// fills its tanks as far as the ice goes.
    /// </summary>
    public sealed class IceRefuelling : IDeepSpaceEncounter
    {
        public const double RangeKm = 1e9;
        public const double IceDays = 1;
        public const double RefuelBelow = 0.5;

        public void Step(DeepSpace space, double fromJd, double toJd, Random rng)
        {
            if (space.Objects.Count == 0) return;
            foreach (var c in new List<Cruise>(space.Cruises))
            {
                if (c.Ships.Count == 0 || c.Piloted || !(c.Engaged(toJd) || c.Engaged(fromJd))) continue;
                double short_ = 0;
                bool low = false;
                foreach (var s in c.Ships)
                {
                    if (s.PropellantCapacity <= 0) continue;
                    short_ += s.PropellantCapacity - s.Propellant;
                    if (s.PropellantFraction < RefuelBelow) low = true;
                }
                if (!low) continue;
                foreach (var o in space.Objects)
                {
                    if (o.Kind != DeepSpaceObjectKind.IceBody || o.IceTonnes <= 0) continue;
                    double closest = DeepSpace.ClosestKm(c, o.Position, fromJd, toJd);
                    if (closest > RangeKm || closest > space.SensorRangeKm(o.Position, RangeKm)) continue;
                    double take = Math.Min(o.IceTonnes, short_);
                    double left = take;
                    foreach (var s in c.Ships)
                    {
                        double room = s.PropellantCapacity - s.Propellant;
                        if (room <= 0 || left <= 0) continue;
                        double fill = Math.Min(room, left);
                        s.Propellant += fill;
                        left -= fill;
                    }
                    o.IceTonnes -= take;
                    space.Log(toJd, $"{c.Leader.Name} of {c.Leader.Faction.Name} drops out at {o.Name} to mine " +
                                    $"{take.ToString("N0", CultureInfo.InvariantCulture)} t of ice for its tanks", o.Position);
                    space.DropOut(c, toJd, IceDays);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Distress calls (U.SPC-T421). Now and then a hull goes adrift on a route with its crew aboard and calls for help; a
    /// passing group within <see cref="RangeKm"/> answers it (a war fleet on campaign does not), drops out for
    /// <see cref="RescueDays"/> and is paid by the survivors' rescue bounty. Pirates who hear it strip it instead. Some calls
    /// are lures: a clan lies in wait at them, robs whoever answers or, against a strong enough warship, is beaten off. A
    /// call nobody answers falls silent and leaves a derelict.
    /// </summary>
    public sealed class DistressCalls : IDeepSpaceEncounter
    {
        /// <summary>How many calls go out a day across all the routes seeded.</summary>
        public const double CallsPerDay = 0.1;

        /// <summary>How near (km) a group must pass to hear one and turn aside: about a light-hour.</summary>
        public const double RangeKm = 1e9;

        public const double RescueDays = 1;

        /// <summary>What a rescued soul's bounty pays the rescuer, credits.</summary>
        public const double BountyPerSoul = 4000;

        /// <summary>Share of calls that are pirate lures (when there is a clan to set one).</summary>
        public const double LureShare = 0.2;

        /// <summary>The fighting strength lying in wait at a lure.</summary>
        public const double LureStrength = 60;

        public void Step(DeepSpace space, double fromJd, double toJd, Random rng)
        {
            var r = space.LifeRng;
            if (space.Routes.Count > 0 && r.NextDouble() < CallsPerDay * (toJd - fromJd)) Call(space, toJd, r);
            if (space.Objects.Count == 0) return;
            // Calls that have gone unanswered too long fall silent.
            foreach (var o in space.Objects)
                if (o.Kind == DeepSpaceObjectKind.Distress && o.ExpiresJd <= toJd)
                {
                    o.Kind = DeepSpaceObjectKind.Derelict;
                    o.Survivors = 0;
                    o.Name = o.Name.Replace("the adrift ", "the derelict ");
                    space.Log(o.ExpiresJd, $"The distress call from {o.Name} falls silent", o.Position);
                }
            foreach (var c in new List<Cruise>(space.Cruises))
            {
                if (c.Ships.Count == 0 || c.Piloted || !(c.Engaged(toJd) || c.Engaged(fromJd))) continue;
                if (c.Leader.Voyage?.Purpose == VoyagePurpose.Campaign) continue;
                foreach (var o in new List<DeepSpaceObject>(space.Objects))
                {
                    if (o.Kind != DeepSpaceObjectKind.Distress || o.SinceJd >= toJd) continue;
                    double closest = DeepSpace.ClosestKm(c, o.Position, fromJd, toJd);
                    if (closest > RangeKm || closest > space.SensorRangeKm(o.Position, RangeKm)) continue;
                    // A clan does not answer its own lure.
                    if (o.LureBy != null && c.Leader.Faction == o.LureBy) continue;
                    Answer(space, c, o, toJd);
                    break;
                }
            }
        }

        static void Call(DeepSpace space, double jd, Random r)
        {
            var route = space.Routes[r.Next(space.Routes.Count)];
            var line = route.AtB.KmFrom(route.AtA);
            var dir = line.LengthSquared > 0 ? line.Normalized : DVec3.UnitX;
            var side = DVec3.Cross(dir, Math.Abs(dir.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX).Normalized;
            var at = GalacticPosition.Lerp(route.AtA, route.AtB, 0.15 + 0.7 * r.NextDouble()).Offset(side * (r.NextDouble() * 5e8));
            Faction lure = null;
            if (r.NextDouble() < LureShare && space.Galaxy != null)
            {
                var clans = new List<Faction>();
                foreach (var f in space.Galaxy.Polities.Factions)
                    if (f.IsPirate) clans.Add(f);
                if (clans.Count > 0) lure = clans[r.Next(clans.Count)];
            }
            string[] hulls = { "Mercy", "Tern", "Bright Promise", "Quiet Harbour", "Sparrow", "Northern Light", "Second Chance", "Lodestar" };
            var o = new DeepSpaceObject
            {
                Kind = DeepSpaceObjectKind.Distress, Name = "the adrift " + hulls[r.Next(hulls.Length)], Position = at, SinceJd = jd,
                Survivors = 5 + r.Next(60), ExpiresJd = jd + 15 + r.NextDouble() * 25, SalvageCredits = 40000 + r.NextDouble() * 160000,
                RouteA = route.A, RouteB = route.B, OffNetwork = !route.OnNetwork, LureBy = lure,
            };
            space.Add(o);
            space.Log(jd, $"A distress call goes out from {o.Name} between {route.A} and {route.B}: {o.Survivors} aboard", at);
        }

        static void Answer(DeepSpace space, Cruise c, DeepSpaceObject o, double jd)
        {
            var leader = c.Leader;
            space.Remove(o);
            if (o.LureBy != null)
            {
                double strength = DeepSpace.Strength(c);
                if (leader.Spec.IsMilitary && !leader.Faction.IsPirate && strength > LureStrength)
                {
                    double bounty = 2 * o.SalvageCredits;
                    leader.Credits += bounty;
                    leader.Faction.Treasury += bounty;
                    space.Log(jd, $"{leader.Name} of {leader.Faction.Name} answers {o.Name} and finds a {o.LureBy.Name} lure; it beats the raiders off " +
                                  $"and takes {bounty.ToString("N0", CultureInfo.InvariantCulture)} cr in bounty", o.Position);
                }
                else
                {
                    double taken = 0;
                    foreach (var s in c.Ships)
                    {
                        if (s.HasCargo)
                        {
                            taken += Math.Max(0, s.CargoCost);
                            s.HasCargo = false;
                            s.CargoTonnes = 0;
                            s.CargoCost = 0;
                            s.Cargo = null;
                        }
                        double purse = s.Credits * 0.25;
                        s.Credits -= purse;
                        taken += purse;
                        if (s.Combat != null) s.Combat.Hull = Math.Max(s.Combat.Spec.Hull * 0.2, s.Combat.Hull - s.Combat.Spec.Hull * 0.25);
                    }
                    o.LureBy.Treasury += taken;
                    space.Log(jd, $"{leader.Name} of {leader.Faction.Name} answers {o.Name}: a {o.LureBy.Name} lure; the raiders rob it of " +
                                  $"{taken.ToString("N0", CultureInfo.InvariantCulture)} cr and let it go", o.Position);
                }
                space.DropOut(c, jd, CruiseMeeting.FightDays);
                return;
            }
            if (leader.Faction.IsPirate)
            {
                leader.Credits += o.SalvageCredits;
                leader.Faction.Treasury += o.SalvageCredits;
                space.Log(jd, $"{leader.Name} of {leader.Faction.Name} answers {o.Name} and strips it, " +
                              $"{o.SalvageCredits.ToString("N0", CultureInfo.InvariantCulture)} cr of plunder; its {o.Survivors} are left to the dark", o.Position);
                space.DropOut(c, jd, WreckSalvage.SalvageDays);
                return;
            }
            double reward = BountyPerSoul * o.Survivors;
            leader.Credits += reward;
            leader.Faction.Treasury += reward;
            space.Rescued += o.Survivors;
            space.Log(jd, $"{leader.Name} of {leader.Faction.Name} answers the distress call of {o.Name} and takes off its {o.Survivors} survivors " +
                          $"({reward.ToString("N0", CultureInfo.InvariantCulture)} cr rescue bounty)", o.Position);
            space.DropOut(c, jd, RescueDays);
        }
    }

    /// <summary>
    /// The powers' relays and listening posts out on the routes (U.SPC-T421). A listening post hears an enemy group
    /// pass within <see cref="ListenRangeKm"/> and tells its home system; an enemy group with guns passing within a
    /// light-hour of either knocks it out, leaving a wreck.
    /// </summary>
    public sealed class DeepSpaceOutposts : IDeepSpaceEncounter
    {
        /// <summary>How far (km) a listening post hears a drive: about a light-day.</summary>
        public const double ListenRangeKm = 2.6e10;

        public void Step(DeepSpace space, double fromJd, double toJd, Random rng)
        {
            if (space.Objects.Count == 0 || space.Cruises.Count == 0) return;
            foreach (var o in new List<DeepSpaceObject>(space.Objects))
            {
                if ((o.Kind != DeepSpaceObjectKind.Relay && o.Kind != DeepSpaceObjectKind.ListeningPost) || o.Owner == null) continue;
                foreach (var c in space.Cruises)
                {
                    if (c.Ships.Count == 0 || !(c.Engaged(toJd) || c.Engaged(fromJd))) continue;
                    var leader = c.Leader;
                    if (!space.Hostile(o.Owner, leader.Faction)) continue;
                    double near = DeepSpace.ClosestKm(c, o.Position, fromJd, toJd);
                    if ((leader.Faction.IsPirate || leader.Spec.IsMilitary) && DeepSpace.Strength(c) > 0 && near <= CruiseMeeting.RangeKm)
                    {
                        KnockOut(space, c, o, toJd);
                        break;
                    }
                    if (o.Kind != DeepSpaceObjectKind.ListeningPost || near > ListenRangeKm || near > space.SensorRangeKm(o.Position, ListenRangeKm)) continue;
                    o.Heard ??= new HashSet<Ship>();
                    if (!o.Heard.Add(leader)) continue;
                    string text = $"{o.Name} hears {leader.Name}{(c.Ships.Count > 1 ? $" and {c.Ships.Count - 1} more" : "")} of {leader.Faction.Name} pass, bound for {c.ToName}";
                    space.Log(toJd, text, o.Position);
                    var home = space.Galaxy?.Find(o.HomeName);
                    home?.World.Log(toJd, text, WorldEventKind.Info, home.Catalog.Root.Index);
                }
            }
        }

        static void KnockOut(DeepSpace space, Cruise c, DeepSpaceObject o, double jd)
        {
            var leader = c.Leader;
            space.Remove(o);
            space.Add(new DeepSpaceObject
            {
                Kind = DeepSpaceObjectKind.Wreck, Name = "the wreck of " + o.Name, Position = o.Position, SalvageCredits = DeepSpace.WreckPartsCredits * 2,
                LostBy = o.Owner, SinceJd = jd, RouteA = o.RouteA, RouteB = o.RouteB, OffNetwork = o.OffNetwork,
            });
            string text = $"{leader.Name} of {leader.Faction.Name} knocks out {o.Name}";
            space.Log(jd, text, o.Position);
            var home = space.Galaxy?.Find(o.HomeName);
            home?.World.Log(jd, text, WorldEventKind.Loss, home.Catalog.Root.Index);
            space.DropOut(c, jd, CruiseMeeting.FightDays);
        }
    }

    /// <summary>
    /// The bands lying in wait on the routes (U.SPC-T421): it keeps their gravity projectors up while they hold at their
    /// stations, and takes them down when they go (<see cref="DeepSpace.LieInWait"/>). The fights themselves are the
    /// ordinary meetings out here (<see cref="CruiseMeeting"/>): a passer pulled out by the projector is within reach.
    /// </summary>
    public sealed class RouteAmbushes : IDeepSpaceEncounter
    {
        public void Step(DeepSpace space, double fromJd, double toJd, Random rng)
        {
            if (space.Ambushes.Count > 0) space.LiftAmbushes(toJd);
        }
    }
}
