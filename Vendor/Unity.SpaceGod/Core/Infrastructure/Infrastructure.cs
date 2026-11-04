using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    public enum StructureKind
    {
        /// <summary>An orbital shipyard: an open frame with the hull being built in its cradle.</summary>
        Shipyard,

        /// <summary>A factory station (foundry, fab, works, arsenal, labs) with its radiators.</summary>
        Factory,

        /// <summary>A spinning habitat ring for a sizeable settlement's orbital population.</summary>
        Habitat,

        /// <summary>A crewed station that really flies (the ISS, Tiangong).</summary>
        CrewedStation,

        /// <summary>A space telescope (Hubble, JWST).</summary>
        Telescope,

        /// <summary>A robotic orbiter (Mars Reconnaissance Orbiter, LRO...).</summary>
        Probe,

        /// <summary>A propellant depot: tank farms parked where ships leaving a world pass (game layer).</summary>
        Depot,

        /// <summary>A communications relay (Queqiao over the Moon's far side, the game layer's signal stations).</summary>
        Relay,

        /// <summary>A crewed transfer station, the gateway between a world and its neighbours (game layer).</summary>
        Gateway,
    }

    /// <summary>
    /// A permanent thing in orbit: game-layer industry made physical (a settlement's shipyard and
    /// factories, its habitat), or a real spacecraft. It holds a circular orbit about its body, or
    /// sits at one of the Lagrange points of its body and what that orbits (<see cref="Core.Lagrange"/>),
    /// moving with the pair so it stays co-orbital.
    /// </summary>
    public sealed class OrbitalStructure
    {
        public string Name;
        public StructureKind Kind;
        public int Body;
        public ParkingOrbit Orbit;
        public Settlement Settlement;
        public IndustryKind Industry;

        Faction owner;

        /// <summary>Its settlement's owner, whoever that is now (a port taken in a siege takes its yards
        /// and stations with it); its own for a structure with no settlement, such as a real spacecraft.</summary>
        public Faction Owner
        {
            get => Settlement?.Owner ?? owner;
            set => owner = value;
        }

        /// <summary>Real size (km), for drawing at the ship scale.</summary>
        public double LengthKm;

        /// <summary>
        /// Sits at this Lagrange point of <see cref="Body"/> and what it orbits (JWST at Sun-Earth L2, a
        /// depot at Earth-Moon L1) instead of an orbit about the body; None for an ordinary orbit.
        /// </summary>
        public LagrangePoint Lagrange;

        /// <summary>At a Lagrange point rather than in orbit about its body.</summary>
        public bool AtLagrange => Lagrange != LagrangePoint.None;

        /// <summary>A real spacecraft (geometry from published orbits; phases illustrative).</summary>
        public bool Real;
        public string Note;

        public DVec3 HelioAt(Ephemeris eph, double jd)
        {
            if (AtLagrange) return Core.Lagrange.HelioAt(eph, Body, Lagrange, jd);
            return eph.HelioAt(Body, jd) + Orbit.OffsetAt(jd);
        }

        /// <summary>The same, from the ephemeris' current frame.</summary>
        public DVec3 Helio(Ephemeris eph)
        {
            if (AtLagrange) return Core.Lagrange.HelioAt(eph, Body, Lagrange, eph.Jd);
            return eph.Helio(Body) + Orbit.OffsetAt(eph.Jd);
        }

        /// <summary>A yard's progress on the hull in its cradle (0..1), or 0.</summary>
        public double SlipProgress =>
            Kind == StructureKind.Shipyard && Settlement?.OnSlip is ShipClass c ? Math.Min(1, Settlement.BuildPoints / Economy.HullCost(c)) : 0;

        public override string ToString() => Name;
    }

    /// <summary>
    /// A satellite constellation drawn as a cloud of points: shells of circular orbits in evenly
    /// spaced planes (a Walker pattern). Plane counts, altitudes and inclinations are the real ones;
    /// where each satellite is along its orbit is illustrative (there are no live orbital elements).
    /// </summary>
    public sealed class Constellation
    {
        public string Name;
        public int Body;
        public Rgb Color;
        public readonly List<Shell> Shells = new List<Shell>();

        public readonly struct Shell
        {
            public readonly double RadiusKm, IncDeg;
            public readonly int Planes, PerPlane;

            public Shell(double radiusKm, double incDeg, int planes, int perPlane)
            {
                RadiusKm = radiusKm;
                IncDeg = incDeg;
                Planes = planes;
                PerPlane = perPlane;
            }
        }

        public int Count
        {
            get
            {
                int n = 0;
                foreach (var s in Shells) n += s.Planes * s.PerPlane;
                return n;
            }
        }

        /// <summary>
        /// The satellites flying on a state of their own (U.SPC-T448), by index: one the player has taken, or one its AI is
        /// still flying back to its slot. Every other satellite is exactly in its slot.
        /// </summary>
        public readonly Dictionary<int, Satellite> Flying = new Dictionary<int, Satellite>();

        /// <summary>A satellite's name: the constellation's and its number in it.</summary>
        public string NameOf(int index) => Name + " " + (index + 1);

        /// <summary>
        /// Every satellite's offset from its body (km, ecliptic) at a date: its slot, or for one flying on its own
        /// (<see cref="Flying"/>) where its own state has it.
        /// </summary>
        public void Offsets(BodyDefinition body, double jd, DVec3[] output)
        {
            var axis = body.SpinAxis;
            var node0 = BodyOrientation.EquatorNode(axis);
            var east0 = DVec3.Cross(axis, node0);
            int i = 0;
            int shellIndex = 0;
            foreach (var s in Shells)
            {
                double rate = Kepler.CircularRate(body.Gm, s.RadiusKm);
                for (int p = 0; p < s.Planes; p++)
                {
                    PlaneAxes(axis, node0, east0, s, shellIndex, p, out var n, out var v);
                    for (int k = 0; k < s.PerPlane && i < output.Length; k++, i++)
                    {
                        double phase = Phase(s, p, k, rate, jd);
                        output[i] = (n * Math.Cos(phase) + v * Math.Sin(phase)) * s.RadiusKm;
                    }
                }
                shellIndex++;
            }
            foreach (var f in Flying)
                if (f.Key >= 0 && f.Key < output.Length) output[f.Key] = f.Value.OffsetKm;
        }

        /// <summary>
        /// Where satellite <paramref name="index"/>'s slot is at a date (km from the body's centre) and how fast it moves
        /// along it (km/s), both ecliptic: the same circular orbits <see cref="Offsets"/> places the cloud on. False (and
        /// zeros) for an index past the last satellite.
        /// </summary>
        public bool SlotAt(BodyDefinition body, int index, double jd, out DVec3 offsetKm, out DVec3 velocityKmS)
        {
            offsetKm = velocityKmS = DVec3.Zero;
            if (index < 0) return false;
            var axis = body.SpinAxis;
            var node0 = BodyOrientation.EquatorNode(axis);
            var east0 = DVec3.Cross(axis, node0);
            int first = 0, shellIndex = 0;
            foreach (var s in Shells)
            {
                int count = s.Planes * s.PerPlane;
                if (index < first + count)
                {
                    int local = index - first, p = local / s.PerPlane, k = local % s.PerPlane;
                    double rate = Kepler.CircularRate(body.Gm, s.RadiusKm);
                    PlaneAxes(axis, node0, east0, s, shellIndex, p, out var n, out var v);
                    double phase = Phase(s, p, k, rate, jd);
                    double c = Math.Cos(phase), sn = Math.Sin(phase);
                    offsetKm = (n * c + v * sn) * s.RadiusKm;
                    velocityKmS = (v * c - n * sn) * (s.RadiusKm * rate / Astro.SecondsPerDay);
                    return true;
                }
                first += count;
                shellIndex++;
            }
            return false;
        }

        /// <summary>
        /// Satellite <paramref name="index"/> flying on its own state from <paramref name="jd"/>, starting from its slot at
        /// its working attitude (or the one already flying).
        /// </summary>
        public Satellite Detach(BodyDefinition body, int index, double jd)
        {
            if (Flying.TryGetValue(index, out var sat)) return sat;
            sat = new Satellite(this, body, index, jd);
            Flying[index] = sat;
            return sat;
        }

        /// <summary>Steps every satellite flying on its own on to <paramref name="jd"/>, letting go of those back on station.</summary>
        public void Step(BodyDefinition body, double jd)
        {
            if (Flying.Count == 0) return;
            List<int> settled = null;
            foreach (var f in Flying)
            {
                f.Value.AdvanceTo(jd);
                if (f.Value.OnStation && !f.Value.Held) (settled ??= new List<int>()).Add(f.Key);
            }
            if (settled != null)
                foreach (var k in settled)
                    Flying.Remove(k);
        }

        static void PlaneAxes(DVec3 axis, DVec3 node0, DVec3 east0, Shell s, int shellIndex, int p, out DVec3 n, out DVec3 v)
        {
            double inc = s.IncDeg * Astro.Deg2Rad;
            double node = Astro.TwoPi * p / s.Planes + shellIndex * 0.37;
            n = node0 * Math.Cos(node) + east0 * Math.Sin(node);
            var inPlane = DVec3.Cross(axis, n);
            v = inPlane * Math.Cos(inc) + axis * Math.Sin(inc);
        }

        static double Phase(Shell s, int p, int k, double rate, double jd) =>
            Astro.TwoPi * (k + 0.5 * (p % 2)) / s.PerPlane + rate * (jd - Astro.J2000);
    }

    /// <summary>
    /// A docking ring: the one way down to a populous world through its atmosphere. A powered
    /// station holds position directly over a city at the planet's stationary-orbit altitude (or three
    /// radii out where it has none), turning with the ground (fiction: it keeps station by thrust,
    /// since only the equator is truly geostationary). Ships queue at it, are cleared down one at a
    /// time, descend to the city, land, and climb back to it to leave.
    /// </summary>
    public sealed class DockingRing
    {
        public int Id;
        public string Name;
        public string City;
        public int Body;
        public double LatDeg, LonDeg;

        /// <summary>From the body's centre, km.</summary>
        public double RadiusKm;

        /// <summary>The body's radius and where its atmosphere starts (km above the surface).</summary>
        public double SurfaceKm, EntryInterfaceKm;

        /// <summary>One ship is cleared to descend per interval (days).</summary>
        public double EntryIntervalDays = 30.0 / 1440;

        /// <summary>When the next ship may begin its descent.</summary>
        public double NextEntryJd = double.MinValue;

        /// <summary>Hours from the ring to the ground (and back).</summary>
        public double DescentDays;

        public Settlement Settlement;

        Faction owner;

        /// <summary>The city's settlement's owner, whoever that is now (it changes hands with the port).</summary>
        public Faction Owner
        {
            get => Settlement?.Owner ?? owner;
            set => owner = value;
        }

        /// <summary>How far out along the holding line arriving ships wait (km past the ring).</summary>
        public double HoldKm => Math.Max(RadiusKm * 0.05, SurfaceKm * 0.3);

        readonly BodyDefinition body;

        /// <summary>For a world that keeps one face to its parent: the ephemeris, to find that face.</summary>
        public Ephemeris Ephemeris;

        double lockedEpoch = double.NaN, lockedW0, lockedRate;

        public DockingRing(BodyDefinition body)
        {
            this.body = body;
            Body = body.Index;
            SurfaceKm = body.RadiusKm;
        }

        /// <summary>
        /// A body-fixed point over the ring's city at a distance from the body's centre, as a circle
        /// about the spin axis that turns with the planet (so flight planning can intercept it).
        /// <paramref name="lonOffsetDeg"/> moves it east along the holding line's parallel.
        /// </summary>
        public ParkingOrbit Point(double radiusKm, double lonOffsetDeg = 0)
        {
            var north = BodyOrientation.MapNorth(body);
            var node = BodyOrientation.EquatorNode(north);
            var east = DVec3.Cross(north, node);
            double lat = LatDeg * Astro.Deg2Rad;
            double w0 = double.IsNaN(body.PrimeMeridianDeg) ? 0 : body.PrimeMeridianDeg;
            double epoch = Astro.J2000;
            double rate = BodyOrientation.RateDegPerDay(body) * Astro.Deg2Rad;
            if (body.FacesParent && Ephemeris != null && body.ParentIndex >= 0)
            {
                // Longitude 0 faces the parent (as the surface is drawn). Its direction is taken once
                // and carried on at the mean orbital rate, so the point is the same whenever it is asked
                // for (a ship planned days ahead meets it exactly); the orbit's slight eccentricity swings
                // the true sub-parent point a degree or two either side.
                if (double.IsNaN(lockedEpoch))
                {
                    // At the ephemeris' own date, or -- asked before it has one (a world still being built) --
                    // at J2000: the cached frame is all NaN until the first update, which left the ring nowhere.
                    lockedEpoch = double.IsNaN(Ephemeris.Jd) ? Astro.J2000 : Ephemeris.Jd;
                    const double h = 1e-3;
                    DVec3 RelativeAt(double jd) => Ephemeris.HelioAt(body.Index, jd) - Ephemeris.HelioAt(body.ParentIndex, jd);
                    var relative = RelativeAt(lockedEpoch);
                    var motion = RelativeAt(lockedEpoch + h) - RelativeAt(lockedEpoch - h);
                    var toParent = -relative;
                    var p = toParent - north * DVec3.Dot(toParent, north);
                    lockedW0 = Math.Atan2(DVec3.Dot(p, east), DVec3.Dot(p, node)) * Astro.Rad2Deg;
                    double sense = DVec3.Dot(DVec3.Cross(relative, motion), north) >= 0 ? 1 : -1;
                    lockedRate = sense * Math.Abs(body.Elements.At(lockedEpoch).MeanMotion);
                }
                epoch = lockedEpoch;
                w0 = lockedW0;
                rate = lockedRate;
            }
            return new ParkingOrbit
            {
                Body = Body,
                Center = north * (radiusKm * Math.Sin(lat)),
                RadiusKm = radiusKm * Math.Cos(lat),
                U = node,
                V = east,
                EpochJd = epoch,
                PhaseAtEpoch = (w0 + LonDeg + lonOffsetDeg) * Astro.Deg2Rad,
                RateRadPerDay = rate,
                Fixed = true,
            };
        }

        /// <summary>The ring itself.</summary>
        public ParkingOrbit RingPoint => Point(RadiusKm);

        /// <summary>
        /// Where a ship arrives to wait its turn: off the top of the line, spread east or west and up or
        /// down by ship (golden-ratio steps, so no two ships share a spot).
        /// </summary>
        public ParkingOrbit HoldPoint(Ship ship)
        {
            double f = ship.Id * 0.6180339887498949 % 1;
            double g = ship.Id * 0.7548776662466927 % 1;
            return Point(RadiusKm + HoldKm * (0.85 + 0.3 * g), (f - 0.5) * 1.2);
        }

        /// <summary>Unit vector from the body's centre up through the ring at a date.</summary>
        public DVec3 Up(double jd) => Point(1).OffsetAt(jd);

        public override string ToString() => Name;
    }

    /// <summary>Everything a system has in orbit, and the docking rings over its populous worlds.</summary>
    public sealed class Infrastructure
    {
        public readonly List<OrbitalStructure> Structures = new List<OrbitalStructure>();
        public readonly List<Constellation> Constellations = new List<Constellation>();
        public readonly List<DockingRing> Rings = new List<DockingRing>();

        readonly BodyCatalog catalog;
        readonly Ephemeris ephemeris;

        public Infrastructure(BodyCatalog catalog, Ephemeris ephemeris = null)
        {
            this.catalog = catalog;
            this.ephemeris = ephemeris;
        }

        /// <summary>
        /// Steps every constellation satellite flying on its own (U.SPC-T448: taken by the player, or its AI bringing it
        /// back to its slot) on to <paramref name="jd"/>.
        /// </summary>
        public void UpdateSatellites(double jd)
        {
            foreach (var c in Constellations)
                if (c.Flying.Count > 0) c.Step(catalog[c.Body], jd);
        }

        public OrbitalStructure YardOf(Settlement s)
        {
            foreach (var st in Structures)
                if (st.Kind == StructureKind.Shipyard && st.Settlement == s) return st;
            return null;
        }

        public IEnumerable<DockingRing> RingsAt(int body)
        {
            foreach (var r in Rings)
                if (r.Body == body) yield return r;
        }

        // ---------------------------------------------------------------- building

        static string FacilityName(IndustryKind k)
        {
            switch (k)
            {
                case IndustryKind.Smelter: return "Foundry";
                case IndustryKind.ElectronicsFab: return "Fab";
                case IndustryKind.MachineWorks: return "Works";
                case IndustryKind.MunitionsPlant: return "Arsenal";
                case IndustryKind.PharmaLab: return "Labs";
                case IndustryKind.Shipyard: return "Yards";
                default: return k.ToString();
            }
        }

        /// <summary>
        /// Each settlement's industry, put in orbit of its world: the shipyard as a yard, each factory
        /// as a station, and a habitat for the big ones. Mines and farms stay on the ground.
        /// </summary>
        public void AddSettlementStructures(Economy economy, int seed)
        {
            var rng = new Random(seed);
            var perBody = new Dictionary<int, int>();
            foreach (var s in economy.Settlements)
            {
                if (s.Kind == SettlementKind.Haven) continue;
                var b = catalog[s.Body];
                var list = new List<(StructureKind kind, IndustryKind industry, string name, double km)>();
                foreach (var f in s.Facilities)
                {
                    var k = f.Recipe.Kind;
                    if (k == IndustryKind.Mine || k == IndustryKind.Farm) continue;
                    if (k == IndustryKind.Shipyard) list.Add((StructureKind.Shipyard, k, s.Name + " " + FacilityName(k), 3.5));
                    else list.Add((StructureKind.Factory, k, s.Name + " " + FacilityName(k), 1.6));
                }
                if (s.Population >= 100e3 && b.Kind != BodyKind.Station) list.Add((StructureKind.Habitat, IndustryKind.Farm, s.Name + " Habitat", 2.2));

                foreach (var (kind, industry, name, km) in list)
                {
                    perBody.TryGetValue(s.Body, out int n);
                    perBody[s.Body] = n + 1;
                    // Low, near-equatorial orbits, a layer apart; free stations get a loose cluster around them.
                    double radii = b.Kind == BodyKind.Station ? 3.0 + n * 0.9 : 1.32 + n * 0.11;
                    var orbit = EquatorialOrbit(b, b.RadiusKm * radii, rng.NextDouble() * 12 - 6, rng.NextDouble() * 360, rng.NextDouble() * 360);
                    Structures.Add(new OrbitalStructure
                    {
                        Name = name, Kind = kind, Body = s.Body, Orbit = orbit, Owner = s.Owner, Settlement = s, Industry = industry, LengthKm = km,
                        Note = kind == StructureKind.Shipyard ? "Orbital shipyard (game layer)" : kind == StructureKind.Habitat ? "Orbital habitat (game layer)" : "Factory station (game layer)",
                    });
                }
            }
        }

        /// <summary>
        /// A circular orbit about a body's equator: <paramref name="incDeg"/> from it, its node
        /// <paramref name="nodeDeg"/> round from the equator's node, prograde with the spin.
        /// </summary>
        public static ParkingOrbit EquatorialOrbit(BodyDefinition b, double radiusKm, double incDeg, double nodeDeg, double phaseDeg, double epochJd = Astro.J2000)
        {
            var axis = b.SpinAxis;
            var node0 = BodyOrientation.EquatorNode(axis);
            var east0 = DVec3.Cross(axis, node0);
            double node = nodeDeg * Astro.Deg2Rad, inc = incDeg * Astro.Deg2Rad;
            var n = node0 * Math.Cos(node) + east0 * Math.Sin(node);
            var v = DVec3.Cross(axis, n) * Math.Cos(inc) + axis * Math.Sin(inc);
            return new ParkingOrbit
            {
                Body = b.Index, RadiusKm = radiusKm, U = n, V = v, EpochJd = epochJd, PhaseAtEpoch = phaseDeg * Astro.Deg2Rad,
                RateRadPerDay = Kepler.CircularRate(Math.Max(b.Gm, 1e-12), radiusKm),
            };
        }

        void Real(string name, StructureKind kind, string body, double altitudeKm, double incDeg, double nodeDeg, double phaseDeg, double km, string note)
        {
            var b = catalog[body];
            Structures.Add(new OrbitalStructure
            {
                Name = name, Kind = kind, Body = b.Index, Orbit = EquatorialOrbit(b, b.RadiusKm + altitudeKm, incDeg, nodeDeg, phaseDeg),
                LengthKm = km, Real = true, Note = note,
            });
        }

        /// <summary>
        /// Sol's real spacecraft and constellations (orbits from published altitudes and
        /// inclinations, circular; positions along them illustrative). See Core/Systems/SOURCES.md.
        /// </summary>
        public void AddSolSpacecraft()
        {
            Real("ISS", StructureKind.CrewedStation, "Earth", 420, 51.64, 40, 0, 0.109, "International Space Station: ~420 km, 51.64°, crewed since 2000.");
            Real("Tiangong", StructureKind.CrewedStation, "Earth", 385, 41.47, 170, 120, 0.055, "China's space station: ~385 km, 41.5°.");
            Real("Hubble", StructureKind.Telescope, "Earth", 530, 28.47, 290, 230, 0.0132, "Hubble Space Telescope: ~530 km, 28.5°, since 1990.");
            AtPoint("JWST", StructureKind.Telescope, "Earth", LagrangePoint.L2, 0.021, null,
                "James Webb Space Telescope, at the Sun-Earth L2 point 1.5 million km beyond Earth (its halo orbit is not drawn).");
            AtPoint("Euclid", StructureKind.Telescope, "Earth", LagrangePoint.L2, 0.0045, null,
                "ESA's dark-universe survey telescope, at Sun-Earth L2 since 2023 (halo orbit not drawn).");
            AtPoint("SOHO", StructureKind.Probe, "Earth", LagrangePoint.L1, 0.0095, null,
                "Solar and Heliospheric Observatory, watching the Sun from Sun-Earth L1 since 1996 (halo orbit not drawn).");
            AtPoint("DSCOVR", StructureKind.Probe, "Earth", LagrangePoint.L1, 0.0018, null,
                "Deep Space Climate Observatory: solar-wind warning and the EPIC camera's whole-Earth view, at Sun-Earth L1 since 2015.");
            AtPoint("Queqiao", StructureKind.Relay, "Moon", LagrangePoint.L2, 0.0042, null,
                "China's relay for the Moon's far side (Chang'e 4), on a halo orbit about Earth-Moon L2 since 2018 (drawn at the point).");
            Real("Lunar Reconnaissance Orbiter", StructureKind.Probe, "Moon", 100, 90, 30, 0, 0.004, "LRO: polar lunar orbit (drawn circular at 100 km).");
            Real("Mars Reconnaissance Orbiter", StructureKind.Probe, "Mars", 300, 93, 60, 0, 0.0137, "MRO: ~300 km sun-synchronous polar orbit.");
            Real("Mars Odyssey", StructureKind.Probe, "Mars", 400, 93.2, 150, 90, 0.0057, "2001 Mars Odyssey: ~400 km polar orbit.");
            Real("MAVEN", StructureKind.Probe, "Mars", 3000, 75, 250, 200, 0.0114, "MAVEN: 150 × 6,200 km ellipse at 75° (drawn circular at its mean).");
            Real("Mars Express", StructureKind.Probe, "Mars", 5000, 86.3, 330, 300, 0.012, "Mars Express: 300 × 10,000 km polar ellipse (drawn circular).");
            Real("ExoMars TGO", StructureKind.Probe, "Mars", 400, 74, 20, 45, 0.0175, "Trace Gas Orbiter: ~400 km, 74°.");

            int earth = catalog["Earth"].Index;
            double re = catalog[earth].RadiusKm;
            var gps = new Constellation { Name = "GPS", Body = earth, Color = new Rgb(0.55f, 0.95f, 0.6f) };
            gps.Shells.Add(new Constellation.Shell(26560, 55, 6, 5));
            var galileo = new Constellation { Name = "Galileo", Body = earth, Color = new Rgb(0.45f, 0.7f, 1f) };
            galileo.Shells.Add(new Constellation.Shell(29600, 56, 3, 9));
            var glonass = new Constellation { Name = "GLONASS", Body = earth, Color = new Rgb(1f, 0.55f, 0.5f) };
            glonass.Shells.Add(new Constellation.Shell(25510, 64.8, 3, 8));
            var beidou = new Constellation { Name = "BeiDou", Body = earth, Color = new Rgb(1f, 0.85f, 0.4f) };
            beidou.Shells.Add(new Constellation.Shell(27900, 55, 3, 8));
            var starlink = new Constellation { Name = "Starlink", Body = earth, Color = new Rgb(0.3f, 0.33f, 0.38f) };
            starlink.Shells.Add(new Constellation.Shell(re + 550, 53.0, 72, 22));
            starlink.Shells.Add(new Constellation.Shell(re + 540, 53.2, 72, 22));
            starlink.Shells.Add(new Constellation.Shell(re + 570, 70.0, 36, 20));
            starlink.Shells.Add(new Constellation.Shell(re + 560, 97.6, 6, 58));
            starlink.Shells.Add(new Constellation.Shell(re + 530, 43.0, 28, 40));
            var geo = new Constellation { Name = "Geostationary belt", Body = earth, Color = new Rgb(0.95f, 0.75f, 0.95f) };
            geo.Shells.Add(new Constellation.Shell(42164, 0.05, 1, 540));
            Constellations.AddRange(new[] { starlink, gps, galileo, glonass, beidou, geo });
        }

        /// <summary>Puts a structure at a Lagrange point of a body and what it orbits (a real spacecraft when it has no owner).</summary>
        public OrbitalStructure AtPoint(string name, StructureKind kind, string body, LagrangePoint point, double km, Faction owner, string note)
        {
            var st = new OrbitalStructure
            {
                Name = name, Kind = kind, Body = catalog[body].Index, Lagrange = point, LengthKm = km, Owner = owner, Real = owner == null, Note = note,
            };
            Structures.Add(st);
            return st;
        }

        /// <summary>
        /// Sol's game-layer infrastructure at the Lagrange points, where it belongs: a depot and a
        /// gateway at Earth-Moon L1 (the cheapest stop between Earth and the Moon), a depot at Earth-Moon
        /// L2 for ships bound out of cislunar space, a storm-warning relay at Sun-Earth L1 (an hour of
        /// warning before the solar wind arrives), a depot at Sun-Earth L2 for outbound traffic, and at
        /// Jupiter's L4 and L5 the Trojan camps' depot and relay, co-orbital with Jupiter 60 degrees ahead
        /// and behind. The inner points are <paramref name="inner"/>'s, the Trojan camps <paramref name="trojans"/>'.
        /// </summary>
        public void AddSolLagrangeStations(Faction inner, Faction trojans)
        {
            AtPoint("Cislunar Depot", StructureKind.Depot, "Moon", LagrangePoint.L1, 1.2, inner,
                "Propellant depot at Earth-Moon L1, the low-energy waypoint between Earth and the Moon (game layer).");
            AtPoint("L1 Gateway", StructureKind.Gateway, "Moon", LagrangePoint.L1, 2.0, inner,
                "Transfer station at Earth-Moon L1, where Earth-Moon traffic changes ships (game layer).");
            AtPoint("Farside Depot", StructureKind.Depot, "Moon", LagrangePoint.L2, 1.0, inner,
                "Propellant depot at Earth-Moon L2, beyond the Moon, for ships leaving cislunar space (game layer).");
            AtPoint("Sunward Relay", StructureKind.Relay, "Earth", LagrangePoint.L1, 0.4, inner,
                "Signal station at Sun-Earth L1: an hour's warning of solar storms, relayed across the system (game layer).");
            AtPoint("Outbound Depot", StructureKind.Depot, "Earth", LagrangePoint.L2, 1.4, inner,
                "Propellant depot at Sun-Earth L2, the departure point for the outer system (game layer).");
            AtPoint("Greek Camp Depot", StructureKind.Depot, "Jupiter", LagrangePoint.L4, 1.2, trojans,
                "Propellant depot at Sun-Jupiter L4, 60 degrees ahead of Jupiter, serving the Greek camp's miners (game layer).");
            AtPoint("Trojan Camp Relay", StructureKind.Relay, "Jupiter", LagrangePoint.L5, 0.4, trojans,
                "Signal station at Sun-Jupiter L5, 60 degrees behind Jupiter, the only ears in the Trojan camp (game layer).");
        }

        // ---------------------------------------------------------------- docking rings

        /// <summary>
        /// Radius of the body's stationary orbit (km from its centre), where a satellite keeps pace with
        /// the ground; or three radii out where that lies beyond the body's neighbourhood (slow
        /// rotators such as Venus, tidally locked worlds).
        /// </summary>
        public static double RingRadiusKm(BodyDefinition b)
        {
            double periodS = Math.Abs(b.RotationHours) * 3600;
            double stationary = periodS > 0 ? Math.Pow(b.Gm * periodS * periodS / (4 * Math.PI * Math.PI), 1.0 / 3) : double.MaxValue;
            double limit = ScaleModel.NeighbourhoodRadii * 0.85 * b.RadiusKm;
            return stationary < limit ? stationary : b.RadiusKm * 3;
        }

        static double EntryInterface(string body)
        {
            switch (body)
            {
                case "Venus": return 250;
                case "Mars": return 125;
                case "Titan": return 600;
                default: return 120;
            }
        }

        /// <summary>Adds a ring over a site; it belongs to the world's largest settlement.</summary>
        public DockingRing AddRing(Economy economy, string body, string name, string city, double latDeg, double lonDeg)
        {
            var b = catalog[body];
            Settlement owner = null;
            foreach (var s in economy.At(b.Index))
                if (owner == null || s.Population > owner.Population) owner = s;
            var ring = new DockingRing(b)
            {
                Ephemeris = ephemeris,
                Id = Rings.Count, Name = name, City = city, LatDeg = latDeg, LonDeg = lonDeg,
                RadiusKm = RingRadiusKm(b), EntryInterfaceKm = EntryInterface(body), Settlement = owner, Owner = owner?.Owner,
            };
            // About 3.5 hours down from geostationary altitude; proportionally less from closer in.
            ring.DescentDays = 3.5 / 24 * Math.Max(0.25, (ring.RadiusKm - b.RadiusKm) / 35793.0);
            Rings.Add(ring);
            return ring;
        }

        /// <summary>
        /// How many rings a world's population warrants: one per order of magnitude past 100,000
        /// (⌈log₁₀(population / 10⁵)⌉, one to seven).
        /// </summary>
        public static int RingsFor(double population) =>
            Math.Max(1, Math.Min(7, (int)Math.Ceiling(Math.Log10(Math.Max(population, 1) / 1e5))));

        public void AddSolRings(Economy economy)
        {
            // Earth: one over each great city (seven, as many as its population warrants).
            AddRing(economy, "Earth", "New York Ring", "New York", 40.7128, -74.0060);
            AddRing(economy, "Earth", "Paris Ring", "Paris", 48.8566, 2.3522);
            AddRing(economy, "Earth", "Beijing Ring", "Beijing", 39.9042, 116.4074);
            AddRing(economy, "Earth", "Tokyo Ring", "Tokyo", 35.6762, 139.6503);
            AddRing(economy, "Earth", "Sydney Ring", "Sydney", -33.8688, 151.2093);
            AddRing(economy, "Earth", "Johannesburg Ring", "Johannesburg", -26.2041, 28.0473);
            AddRing(economy, "Earth", "Moscow Ring", "Moscow", 55.7558, 37.6173);
            // Mars (1.5 million: two), over Jezero crater and Pavonis Mons on the Tharsis bulge.
            AddRing(economy, "Mars", "Jezero Gate", "Jezero", 18.38, 77.58);
            AddRing(economy, "Mars", "Tharsis Gate", "Pavonis", 1.48, -112.96);
            // Venus's aerostat and Titan's port: one each.
            AddRing(economy, "Venus", "Aphrodite Gate", "Aphrodite Aerostat", -5, 105);
            AddRing(economy, "Titan", "Kraken Gate", "Kraken Port", 68, 50);
        }

        public void AddCentauriRings(Economy economy)
        {
            // Proxima b is tidally locked: the colony lies along the terminator, at dawn and dusk.
            AddRing(economy, "Proxima b", "Dawn Gate", "New Kentaurus (dawn)", 10, 90);
            AddRing(economy, "Proxima b", "Dusk Gate", "New Kentaurus (dusk)", -10, -90);
        }
    }
}
