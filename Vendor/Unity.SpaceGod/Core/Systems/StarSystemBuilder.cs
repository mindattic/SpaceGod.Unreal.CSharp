using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>A debris belt or disc about one star, in the system's own (J2000 ecliptic) axes.</summary>
    public sealed class DebrisDisc
    {
        public string Name;
        public string HostName;
        public int HostIndex = -1;
        public double InnerKm, OuterKm;

        /// <summary>Unit normal of the disc's plane.</summary>
        public DVec3 Normal;

        public string Summary;
    }

    /// <summary>
    /// Turns a <see cref="SystemSpec"/> (catalogue data) into the bodies of a <see cref="BodyCatalog"/>
    /// and its debris discs, the same way for every system:
    ///  - a single star is the root; a pair at the root circles an invisible barycentre (the root),
    ///    both members sharing their measured visual orbit, scaled by each other's share of the mass;
    ///  - a wide companion (star or pair) orbits the root through the place it is seen today;
    ///  - planets keep their catalogued period, semi-major axis and eccentricity exactly; the plane,
    ///    node and phase are measured where the catalogue has them, assumed (and said so) where not;
    ///  - each planet is flagged against its star's habitable zone (<see cref="HabitableZones"/>).
    /// </summary>
    public static class StarSystemBuilder
    {
        public const double SunGm = 1.32712440018e11;
        public const double SunRadiusKm = 695700;
        public const double EarthGm = 398600.435;
        public const double EarthRadiusKm = 6371.0;

        /// <summary>Above this mass (Earth masses) a planet is drawn as a gas giant.</summary>
        public const double GasGiantMassEarth = 50;

        public sealed class Result
        {
            public readonly List<BodyDefinition> Bodies = new List<BodyDefinition>();
            public readonly List<DebrisDisc> Discs = new List<DebrisDisc>();

            /// <summary>The plane assumed for unmeasured orientations: the root pair's orbit, or the system's reference plane.</summary>
            public DVec3 ReferenceNormal;

            /// <summary>A direction in that plane: the root pair's periastron, or the plane's node.</summary>
            public DVec3 ReferencePeriapsis;
        }

        public static Result Build(SystemSpec spec)
        {
            var r = new Result();
            var stars = new Dictionary<string, StarSpec>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in spec.Stars()) stars[s.Name] = s;

            bool pairRoot = spec.Primary.IsPair;
            if (pairRoot)
            {
                var o = spec.Primary.Orbit;
                OrbitalElements.VisualBinaryGeometry(spec.RaDeg, spec.DecDeg, o.IDeg, o.NodeDeg, o.ArgPeriDeg, out var peri, out var normal);
                r.ReferencePeriapsis = peri;
                r.ReferenceNormal = normal;
            }
            else
            {
                OrbitalElements.VisualBinaryGeometry(spec.RaDeg, spec.DecDeg, spec.ReferenceIDeg, spec.ReferenceNodeDeg, 0, out var peri, out var normal);
                r.ReferencePeriapsis = peri;
                r.ReferenceNormal = normal;
            }

            AddNode(r, spec, spec.Primary, null, null);

            string rootName = r.Bodies[0].Name;
            foreach (var c in spec.Companions) AddCompanion(r, spec, c, rootName);
            foreach (var p in spec.Planets) r.Bodies.Add(Planet(r, spec, p, StarOf(stars, p.Host, p.Name)));
            foreach (var st in spec.Stations) r.Bodies.Add(Station(r, st, StarOf(stars, st.Host, st.Name)));
            foreach (var d in spec.Discs)
            {
                StarOf(stars, d.Host, d.Name);
                var normal = pairRoot && double.IsNaN(d.IDeg) ? r.ReferenceNormal : SkyNormal(spec, double.IsNaN(d.IDeg) ? spec.ReferenceIDeg : d.IDeg, spec.ReferenceNodeDeg);
                r.Discs.Add(new DebrisDisc
                {
                    Name = d.Name, HostName = d.Host, InnerKm = d.InnerAu * Astro.AuKm, OuterKm = d.OuterAu * Astro.AuKm,
                    Normal = normal, Summary = d.Summary,
                });
            }
            return r;
        }

        static StarSpec StarOf(Dictionary<string, StarSpec> stars, string host, string what)
        {
            if (host == null || !stars.TryGetValue(host, out var star))
                throw new InvalidOperationException($"{what}: unknown host star {host}");
            return star;
        }

        // ---------------------------------------------------------------- stars

        /// <summary>Adds a star, or a pair and its barycentre, on <paramref name="orbit"/> about <paramref name="parent"/> (null: the root).</summary>
        static void AddNode(Result r, SystemSpec spec, StellarNode node, string parent, OrbitalElements orbit)
        {
            if (!node.IsPair)
            {
                r.Bodies.Add(Star(node.Star, parent, orbit, 1, r.ReferenceNormal));
                return;
            }

            var o = node.Orbit;
            OrbitalElements.VisualBinaryGeometry(spec.RaDeg, spec.DecDeg, o.IDeg, o.NodeDeg, o.ArgPeriDeg, out var peri, out var normal);
            double period = o.PeriodYears * 365.25;
            double tPeri = Astro.J2000 + (o.PeriastronYear - 2000) * 365.25;
            double mean = Astro.WrapDegrees360((Astro.J2000 - tPeri) / period * 360);
            var relative = OrbitalElements.FromVectors(Astro.J2000, o.AAu * Astro.AuKm, o.E, peri, normal, mean, period);
            double total = node.MassSolar;

            r.Bodies.Add(new BodyDefinition
            {
                Name = node.Name, Kind = BodyKind.Barycentre, ParentName = parent, RadiusKm = 0, Gm = SunGm * total,
                Elements = orbit,
                Summary = node.Summary ?? $"The centre of mass that {node.A.Name} and {node.B.Name} circle every {o.PeriodYears:0.0} years.",
            });
            r.Bodies.Add(Star(node.A, node.Name, relative, -node.B.MassSolar / total, normal));
            r.Bodies.Add(Star(node.B, node.Name, relative, node.A.MassSolar / total, normal));
        }

        static BodyDefinition Star(StarSpec s, string parent, OrbitalElements orbit, double orbitScale, DVec3 pole)
        {
            Astro.RaDecOf(pole, out double ra, out double dec);
            return new BodyDefinition
            {
                Name = s.Name, Kind = BodyKind.Star, ParentName = parent,
                RadiusKm = s.RadiusSolar * SunRadiusKm, Gm = SunGm * s.MassSolar,
                Elements = orbit, OrbitScale = orbitScale,
                PoleRaDeg = ra, PoleDecDeg = dec, RotationHours = s.RotationDays * 24,
                TeffK = s.TeffK, LuminositySolar = s.LuminositySolar, Color = AlphaCentauri.StarColor(s.TeffK),
                SpectralType = s.Spectral,
                Summary = s.Summary,
            };
        }

        /// <summary>
        /// A wide companion on an orbit about the root through where it is seen today (its offset from
        /// the primary at the given mean anomaly). The orbit's plane is the reference plane, tipped to
        /// contain that offset.
        /// </summary>
        static void AddCompanion(Result r, SystemSpec spec, CompanionSpec c, string root)
        {
            var offset = CompanionOffsetKm(spec, c);
            var u = offset.Normalized;
            var normal = (r.ReferenceNormal - u * DVec3.Dot(r.ReferenceNormal, u)).Normalized;
            double ecc = Kepler.SolveEccentricAnomaly(c.MeanAnomalyDeg * Astro.Deg2Rad, c.E);
            double a = offset.Length / (1 - c.E * Math.Cos(ecc));
            double nu = 2 * Math.Atan2(Math.Sqrt(1 + c.E) * Math.Sin(ecc / 2), Math.Sqrt(1 - c.E) * Math.Cos(ecc / 2));
            var peri = u * Math.Cos(nu) - DVec3.Cross(normal, u) * Math.Sin(nu);
            double period = OrbitalElements.PeriodFromGm(a, SunGm * (spec.Primary.MassSolar + c.Node.MassSolar));
            var orbit = OrbitalElements.FromVectors(Astro.J2000, a, c.E, peri, normal, c.MeanAnomalyDeg, period);
            AddNode(r, spec, c.Node, root, orbit);
        }

        /// <summary>A companion's offset from the primary today (km, ecliptic J2000).</summary>
        public static DVec3 CompanionOffsetKm(SystemSpec spec, CompanionSpec c)
        {
            if (!double.IsNaN(c.RaDeg))
            {
                double ly = double.IsNaN(c.DistanceLy) ? spec.DistanceLy : c.DistanceLy;
                return (Astro.DirectionFromRaDec(c.RaDeg, c.DecDeg) * ly - spec.PositionLy) * Astro.LightYearKm;
            }
            double ra = spec.RaDeg * Astro.Deg2Rad, dec = spec.DecDeg * Astro.Deg2Rad, pa = c.PositionAngleDeg * Astro.Deg2Rad;
            var north = new DVec3(-Math.Sin(dec) * Math.Cos(ra), -Math.Sin(dec) * Math.Sin(ra), Math.Cos(dec));
            var east = new DVec3(-Math.Sin(ra), Math.Cos(ra), 0);
            return Astro.EquatorialToEcliptic(north * Math.Cos(pa) + east * Math.Sin(pa)) * (c.SeparationAu * Astro.AuKm);
        }

        // ---------------------------------------------------------------- planets

        static DVec3 SkyNormal(SystemSpec spec, double iDeg, double nodeDeg)
        {
            OrbitalElements.VisualBinaryGeometry(spec.RaDeg, spec.DecDeg, iDeg, nodeDeg, 0, out _, out var normal);
            return normal;
        }

        static BodyDefinition Planet(Result r, SystemSpec spec, PlanetSpec p, StarSpec host)
        {
            DVec3 normal, peri;
            double argPeri = double.IsNaN(p.ArgPeriDeg) ? 0 : p.ArgPeriDeg;
            if ((spec.Primary.IsPair && double.IsNaN(p.IDeg)) || p.TiltFromReferenceDeg != 0)
            {
                // In the root pair's plane (or tilted out of it): periapsis from a fixed direction in the plane.
                normal = r.ReferenceNormal;
                if (p.TiltFromReferenceDeg != 0)
                {
                    var axis = DVec3.Cross(r.ReferenceNormal, r.ReferencePeriapsis).Normalized;
                    double t = p.TiltFromReferenceDeg * Astro.Deg2Rad;
                    normal = (r.ReferenceNormal * Math.Cos(t) + axis * Math.Sin(t)).Normalized;
                }
                var reference = Math.Abs(normal.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
                var p0 = DVec3.Cross(reference, normal).Normalized;
                double w = argPeri * Astro.Deg2Rad;
                peri = p0 * Math.Cos(w) + DVec3.Cross(normal, p0) * Math.Sin(w);
            }
            else
            {
                // On the sky: the catalogued inclination (or the reference one), node and periastron argument.
                double i = double.IsNaN(p.IDeg) ? spec.ReferenceIDeg : p.IDeg;
                double node = double.IsNaN(p.NodeDeg) ? spec.ReferenceNodeDeg : p.NodeDeg;
                OrbitalElements.VisualBinaryGeometry(spec.RaDeg, spec.DecDeg, i, node, argPeri, out peri, out normal);
            }

            double mean = !double.IsNaN(p.MeanAnomalyDeg) ? p.MeanAnomalyDeg
                : !double.IsNaN(p.PeriastronJd) ? Astro.WrapDegrees360((Astro.J2000 - p.PeriastronJd) / p.PeriodDays * 360)
                : NamePhase(p.Name);

            bool gas = p.GasGiant ?? p.MassEarth >= GasGiantMassEarth;
            bool locked = p.TidallyLocked ?? (!gas && p.PeriodDays < 40);
            double flux = HabitableZones.MeanFlux(host.LuminositySolar, p.AAu, p.E);
            var zone = HabitableZones.Classify(host.LuminositySolar, host.TeffK, p.AAu, p.E);
            Astro.RaDecOf(normal, out double poleRa, out double poleDec);

            string summary = p.Summary ?? "";
            if (zone != HabitableZone.None)
                summary += (summary.Length > 0 ? " " : "") + (zone == HabitableZone.Conservative
                    ? "Inside the habitable zone."
                    : "Inside the optimistic habitable zone (not the conservative one).");

            var b = new BodyDefinition
            {
                Name = p.Name, Kind = BodyKind.Planet, ParentName = p.Host,
                RadiusKm = p.RadiusEarth * EarthRadiusKm, Gm = p.MassEarth * EarthGm,
                Elements = OrbitalElements.FromVectors(Astro.J2000, p.AAu * Astro.AuKm, p.E, peri, normal, mean, p.PeriodDays),
                PoleRaDeg = poleRa, PoleDecDeg = poleDec,
                RotationHours = locked ? p.PeriodDays * 24 : !double.IsNaN(p.RotationHours) ? p.RotationHours : gas ? 10 : 24,
                FacesParent = locked,
                Color = p.Color ?? PlanetColor(p.MassEarth, flux),
                GasGiant = gas,
                AtmosphereColor = p.AtmosphereColor,
                AtmosphereStrength = p.AtmosphereStrength,
                Status = p.Status,
                MassIsMinimum = p.MassIsMinimum,
                HabitableZone = zone,
                DiscoveryYear = p.DiscoveryYear,
                Summary = summary,
            };
            if (gas && b.AtmosphereStrength == 0)
            {
                b.AtmosphereColor = new Rgb(b.Color.R * 1.05f, b.Color.G * 1.05f, b.Color.B * 1.05f);
                b.AtmosphereStrength = 0.3f;
            }
            return b;
        }

        /// <summary>A fixed phase (degrees) from the name, for planets whose phase is not catalogued.</summary>
        static double NamePhase(string name)
        {
            uint h = 2166136261;
            foreach (char ch in name) h = (h ^ ch) * 16777619;
            return h % 360;
        }

        /// <summary>
        /// A plausible colour (assumed: none of these worlds has been seen): gas giants tan, Neptune-like
        /// worlds pale blue, rocky worlds from scorched rust to temperate grey to icy white by the light they get.
        /// </summary>
        static Rgb PlanetColor(double massEarth, double flux)
        {
            if (massEarth >= GasGiantMassEarth) return new Rgb(0.8f, 0.72f, 0.6f);
            if (massEarth >= 8) return new Rgb(0.62f, 0.75f, 0.85f);
            if (flux > 4) return new Rgb(0.58f, 0.42f, 0.34f);
            if (flux > 0.25) return new Rgb(0.55f, 0.52f, 0.5f);
            return new Rgb(0.82f, 0.85f, 0.88f);
        }

        // ---------------------------------------------------------------- game layer

        static BodyDefinition Station(Result r, StationSpec s, StarSpec host)
        {
            Astro.RaDecOf(r.ReferenceNormal, out double ra, out double dec);
            return new BodyDefinition
            {
                Name = s.Name, Kind = BodyKind.Station, ParentName = s.Host, RadiusKm = 15, Gm = 1e-7,
                Elements = OrbitalElements.FromVectors(Astro.J2000, s.AAu * Astro.AuKm, 0, r.ReferencePeriapsis, r.ReferenceNormal, s.MeanAnomalyDeg,
                    OrbitalElements.PeriodFromGm(s.AAu * Astro.AuKm, SunGm * host.MassSolar)),
                PoleRaDeg = ra, PoleDecDeg = dec, RotationHours = 2,
                Color = new Rgb(0.75f, 0.76f, 0.8f),
                Status = BodyStatus.Fictional,
                HabitatSite = s.HabitatSite,
                Summary = s.Summary,
            };
        }
    }
}
