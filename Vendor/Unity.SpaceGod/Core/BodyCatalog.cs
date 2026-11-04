using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The bodies of the simulation, with their real physical and orbital data.
    ///
    /// Sources:
    ///  - Planets: JPL "Keplerian Elements for Approximate Positions of the Major Planets", table 1
    ///    (valid 1800-2050), E. M. Standish. Earth's entry is the Earth-Moon barycentre.
    ///  - Eris: JPL Small-Body Database osculating elements, epoch JD 2459000.5.
    ///  - Ceres: mean elements least-squares fitted to JPL Horizons positions every 30 days over 2000-2050.
    ///  - Psyche, Lutetia and Ida (main-belt asteroids, game-layer pirate havens), and the Jupiter Trojans
    ///    Hektor, Patroclus and Eurybates (mining grounds and a haven): JPL Small-Body Database
    ///    osculating elements, epoch JD 2461200.5; sizes, GM and spin periods from the SBDB physical
    ///    parameters (see Systems/SOURCES.md).
    ///  - The Moon: principal ELP-2000/82 terms (Meeus ch. 47), see LunarTheory; its catalogue elements
    ///    are the mean orbit, used only to draw its path.
    ///  - Other moons: JPL satellite mean elements (ssd.jpl.nasa.gov/sats/elem, epoch J2000), in each
    ///    moon's Laplace plane or its parent's equator, refined by a least-squares fit to JPL Horizons
    ///    positions sampled every 73 days over 2000-2050. Across that span every moon stays within
    ///    0.5 degrees of Horizons along its orbit, except Miranda (1.7, strongly perturbed by its
    ///    neighbours). Phobos was refitted 2026-10 with its tidal decay (a quadratic mean-longitude term):
    ///    within 0.07 degrees. Nodes are measured from the reference plane's ascending node on
    ///    the ecliptic, not on the ICRF equator as in JPL's table.
    ///  - Radii, GM, poles and rotation: IAU WGCCRE report and NASA planetary fact sheets.
    /// </summary>
    public sealed class BodyCatalog
    {
        public IReadOnlyList<BodyDefinition> Bodies => bodies;
        public BodyDefinition Sun => bodies[0];
        public int Count => bodies.Count;

        readonly List<BodyDefinition> bodies;
        readonly Dictionary<string, BodyDefinition> byName = new Dictionary<string, BodyDefinition>(StringComparer.OrdinalIgnoreCase);
        readonly List<int>[] children;

        public BodyCatalog(IEnumerable<BodyDefinition> definitions)
        {
            bodies = new List<BodyDefinition>(definitions);
            for (int i = 0; i < bodies.Count; i++)
            {
                bodies[i].Index = i;
                byName[bodies[i].Name] = bodies[i];
            }

            children = new List<int>[bodies.Count];
            for (int i = 0; i < bodies.Count; i++) children[i] = new List<int>();

            foreach (var b in bodies)
            {
                if (b.ParentName != null)
                {
                    if (!byName.TryGetValue(b.ParentName, out var parent))
                        throw new InvalidOperationException($"{b.Name}: unknown parent {b.ParentName}");
                    if (parent.Index > b.Index)
                        throw new InvalidOperationException($"{b.Name}: parent {parent.Name} must be listed first");
                    b.ParentIndex = parent.Index;
                    children[parent.Index].Add(b.Index);
                }

                if (!double.IsNaN(b.PoleRaDeg))
                    b.Pole = Astro.PoleFromRaDec(b.PoleRaDeg, b.PoleDecDeg);
                else if (b.Plane == OrbitPlane.ParentEquator && b.ParentIndex >= 0)
                    b.Pole = bodies[b.ParentIndex].SpinAxis; // synchronous moon: spins about its orbit normal
                else
                    b.Pole = DVec3.UnitZ;
            }

            foreach (var b in bodies)
            {
                if (b.ParentIndex < 0) continue;
                var parent = bodies[b.ParentIndex];
                double apoapsis = b.Elements.AKm * Math.Abs(b.OrbitScale) * (1 + b.Elements.E);
                double periapsis = b.Elements.AKm * Math.Abs(b.OrbitScale) * (1 - b.Elements.E);
                double soi = b.Elements.AKm * Math.Pow(Math.Max(b.Gm, 0) / parent.Gm, 0.4);
                b.SoiKm = Math.Max(soi, b.RadiusKm * 3.0);
                if (!parent.UsesStellarMapping)
                    parent.MoonReachKm = Math.Max(parent.MoonReachKm, apoapsis);
                else if (parent.ParentIndex >= 0)
                    parent.SystemReachKm = Math.Max(parent.SystemReachKm, apoapsis);
                parent.InnermostChildKm = parent.InnermostChildKm > 0 ? Math.Min(parent.InnermostChildKm, periapsis) : periapsis;
            }

            // A star in a multiple system rules the space out past what orbits it; Laplace's formula
            // against the pair's barycentre would put that boundary absurdly far out.
            foreach (var b in bodies)
                if (b.IsStar && b.ParentIndex >= 0)
                    b.SoiKm = Math.Max(b.SystemReachKm * 1.5, b.RadiusKm * 20);
        }

        /// <summary>The root of the system: the Sun, or the barycentre a multiple star orbits.</summary>
        public BodyDefinition Root => bodies[0];

        double outerEdgeKm = double.NaN;

        /// <summary>A companion star on an orbit wider than this (Proxima, 13,000 AU out) is a system of its own, not part of the
        /// root's reach: it and what orbits it are left out of <see cref="OuterEdgeKm"/>.</summary>
        const double WideCompanionKm = 1000 * Astro.AuKm;

        /// <summary>
        /// How far out the system's own bodies reach from its root, km (U.SPC-T515): the far side of the outermost
        /// planet's, asteroid's or station's orbit (dwarf planets of the far Kuiper reaches and moons are not counted; a
        /// moon stays within its planet's reach), with a tenth of margin; never less than the root's own radius.
        /// Where the system's jump gates sit (see <see cref="Galaxy.GateDistanceKm"/>).
        /// </summary>
        public double OuterEdgeKm
        {
            get
            {
                if (!double.IsNaN(outerEdgeKm)) return outerEdgeKm;
                double reach = bodies[0].RadiusKm;
                foreach (var b in bodies)
                {
                    if (b.ParentIndex < 0 || b.Kind == BodyKind.Moon || b.Kind == BodyKind.DwarfPlanet) continue;
                    double far = 0;
                    bool wide = false;
                    for (var c = b; c.ParentIndex >= 0; c = bodies[c.ParentIndex])
                    {
                        double apo = c.Elements.AKm * Math.Abs(c.OrbitScale) * (1 + c.Elements.E);
                        wide |= apo > WideCompanionKm;
                        far += apo;
                    }
                    if (!wide) reach = Math.Max(reach, far * 1.1);
                }
                return outerEdgeKm = reach;
            }
        }

        public BodyDefinition this[int index] => bodies[index];
        public BodyDefinition this[string name] => byName[name];
        public bool TryGet(string name, out BodyDefinition body) => byName.TryGetValue(name, out body);
        public IReadOnlyList<int> ChildrenOf(int index) => children[index];

        public static BodyCatalog CreateSolSystem() => new BodyCatalog(SolDefinitions());

        public static BodyCatalog CreateAlphaCentauri() => new BodyCatalog(AlphaCentauri.Definitions());

        static IEnumerable<BodyDefinition> SolDefinitions()
        {
            const double sbdbEpoch = 2459000.5;

            yield return new BodyDefinition
            {
                Name = "Sun", Kind = BodyKind.Star, RadiusKm = 695700, Gm = 1.32712440018e11,
                PoleRaDeg = 286.13, PoleDecDeg = 63.87, RotationHours = 609.12,
                PrimeMeridianDeg = 84.176, PrimeMeridianRateDegPerDay = 14.1844,
                Color = new Rgb(1f, 0.86f, 0.6f),
                Summary = "G2V main-sequence star. 99.86% of the system's mass.",
            };
            yield return new BodyDefinition
            {
                Name = "Mercury", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 2439.7, Gm = 22031.8,
                Elements = OrbitalElements.FromJpl(0.38709927, 0.20563593, 7.00497902, 252.25032350, 77.45779628, 48.33076593,
                    0.00000037, 0.00001906, -0.00594749, 149472.67411175, 0.16047689, -0.12534081),
                PoleRaDeg = 281.01, PoleDecDeg = 61.42, RotationHours = 1407.6,
                PrimeMeridianDeg = 329.5988, PrimeMeridianRateDegPerDay = 6.1385108,
                Color = new Rgb(0.6f, 0.58f, 0.55f), Texture = "mercury_color", TexturePoleMargin = 0.035f, NormalMap = "mercury_normal",
                Summary = "Smallest planet; a 3:2 spin-orbit resonance gives it a 176-day solar day.",
            };
            yield return new BodyDefinition
            {
                Name = "Venus", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 6051.8, Gm = 324858.6,
                Elements = OrbitalElements.FromJpl(0.72333566, 0.00677672, 3.39467605, 181.97909950, 131.60246718, 76.67984255,
                    0.00000390, -0.00004107, -0.00078890, 58517.81538729, 0.00268329, -0.27769418),
                PoleRaDeg = 272.76, PoleDecDeg = 67.16, RotationHours = -5832.6,
                PrimeMeridianDeg = 160.20, PrimeMeridianRateDegPerDay = -1.4813688,
                // Visible light never reaches the surface: what an eye sees is the nearly featureless
                // cloud deck, drawn procedurally (see Planet.shader).
                Color = new Rgb(0.93f, 0.86f, 0.68f), SurfaceHaze = 1f,
                AtmosphereColor = new Rgb(1f, 0.88f, 0.6f), AtmosphereStrength = 0.7f,
                Summary = "Spins backwards under a 92-bar CO2 atmosphere. Its clouds hide the surface from every visible-light camera.",
            };
            yield return new BodyDefinition
            {
                Name = "Earth", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 6371.0, Gm = 398600.435,
                Elements = OrbitalElements.FromJpl(1.00000261, 0.01671123, -0.00001531, 100.46457166, 102.93768193, 0.0,
                    0.00000562, -0.00004392, -0.01294668, 35999.37244981, 0.32327364, 0.0),
                PoleRaDeg = 0, PoleDecDeg = 90, RotationHours = 23.9344696,
                PrimeMeridianDeg = 190.147, PrimeMeridianRateDegPerDay = 360.9856235,
                Color = new Rgb(0.3f, 0.45f, 0.7f), Texture = "earth_color",
                AtmosphereColor = new Rgb(0.4f, 0.65f, 1f), AtmosphereStrength = 0.65f,
                Summary = "Home. Elements are for the Earth-Moon barycentre; Earth is offset from it by the Moon.",
            };
            yield return new BodyDefinition
            {
                Name = "Moon", Kind = BodyKind.Moon, ParentName = "Earth", RadiusKm = 1737.4, Gm = 4902.8,
                // Mean elements at J2000: L = 218.3165, perigee longitude 83.3532, node 125.0445.
                Elements = OrbitalElements.FromClassical(Astro.J2000, 384400, 0.0549, 5.145, 125.0445, 83.3532 - 125.0445,
                    218.3165 - 83.3532, 27.321661, -360.0 / 6798.38, 360.0 / 3232.6),
                PoleRaDeg = 269.99, PoleDecDeg = 66.54, RotationHours = 655.72, FacesParent = true,
                Color = new Rgb(0.62f, 0.61f, 0.6f), Texture = "moon_color", NormalMap = "moon_normal",
                Summary = "Tidally locked. Slowly receding from Earth at 3.8 cm a year.",
            };
            yield return new BodyDefinition
            {
                Name = "Mars", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 3389.5, Gm = 42828.37,
                Elements = OrbitalElements.FromJpl(1.52371034, 0.09339410, 1.84969142, -4.55343205, -23.94362959, 49.55953891,
                    0.00001847, 0.00007882, -0.00813131, 19140.30268499, 0.44441088, -0.29257343),
                PoleRaDeg = 317.68, PoleDecDeg = 52.89, RotationHours = 24.6229,
                PrimeMeridianDeg = 176.630, PrimeMeridianRateDegPerDay = 350.89198226,
                Color = new Rgb(0.75f, 0.45f, 0.3f), Texture = "mars_color", NormalMap = "mars_normal",
                AtmosphereColor = new Rgb(0.95f, 0.6f, 0.4f), AtmosphereStrength = 0.2f,
                Summary = "Home of Olympus Mons and Valles Marineris.",
            };
            // Phobos spirals in under Mars's tides: its mean longitude gains 1.26e-3 deg/yr² (fitted, matching
            // the published secular acceleration). The node regresses and periapsis advances with Mars's J2.
            var phobos = Moon("Phobos", "Mars", 11.27, 7.087e-4, 9374.92, 0.015125, 1.07438, 128.2222, 216.4756, 189.5918, 0.3189101053, -0.4357808, 0.4351860, 317.7, 52.9, new Rgb(0.42f, 0.38f, 0.35f));
            phobos.Elements = OrbitalElements.FromClassical(Astro.J2000, 9374.92, 0.015125, 1.07438, 128.2222, 216.4756, 189.5918, 0.3189101053,
                -0.4357808, 0.4351860, 1.2614e-3);
            yield return WithMap(phobos, "phobos_color", true);
            yield return WithMap(Moon("Deimos", "Mars", 6.2, 9.6e-5, 23457, 0.0, 1.7878, 13.753, 352.1363, 212.2813, 1.26244093, -0.018163, -0.036847, 316.6, 53.5, new Rgb(0.55f, 0.5f, 0.45f)), "deimos_color", true);

            yield return new BodyDefinition
            {
                Name = "Ceres", Kind = BodyKind.DwarfPlanet, ParentName = "Sun", RadiusKm = 469.7, Gm = 62.6284,
                // Mean elements fitted to JPL Horizons over 2000-2050 (within 0.5° along its orbit; Jupiter
                // perturbs it). The SBDB osculating set once used here had the wrong mean anomaly for its epoch.
                Elements = OrbitalElements.FromClassical(Astro.J2000, 413958905.2, 0.0777927, 10.589118, 80.537317, 72.967047,
                    7.312862, 1681.58004, -3.7765e-5, 1.0614e-5),
                PoleRaDeg = 291.42, PoleDecDeg = 66.76, RotationHours = 9.074,
                PrimeMeridianDeg = 170.650, PrimeMeridianRateDegPerDay = 952.1532,
                Color = new Rgb(0.5f, 0.49f, 0.47f), Texture = "ceres_color", TintTexture = true,
                Summary = "Largest object in the asteroid belt. Bright salt deposits in Occator crater.",
            };

            yield return new BodyDefinition
            {
                Name = "Jupiter", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 69911, Gm = 1.26686534e8,
                Elements = OrbitalElements.FromJpl(5.20288700, 0.04838624, 1.30439695, 34.39644051, 14.72847983, 100.47390909,
                    -0.00011607, -0.00013253, -0.00183714, 3034.74612775, 0.21252668, 0.20469106),
                PoleRaDeg = 268.06, PoleDecDeg = 64.50, RotationHours = 9.925,
                PrimeMeridianDeg = 284.95, PrimeMeridianRateDegPerDay = 870.536, // System III
                Color = new Rgb(0.8f, 0.7f, 0.58f), Texture = "jupiter_color", TexturePoleMargin = 0.06f, // Hubble map ends near ±80°
                GasGiant = true,
                AtmosphereColor = new Rgb(0.9f, 0.8f, 0.65f), AtmosphereStrength = 0.3f,
                Summary = "Two and a half times the mass of every other planet combined.",
            };
            yield return WithMap(Moon("Io", "Jupiter", 1821.6, 5959.9, 421800, 0.00413, 0.0, 20.0402, 49.0252, 330.8831, 1.76913776, 0, -0.73951, 268.1, 64.5, new Rgb(0.93f, 0.83f, 0.45f)), "io_color", false);
            yield return WithMap(Moon("Europa", "Jupiter", 1560.8, 3202.7, 671100, 0.00928, 0.4643, 204.4438, 44.6561, 345.4036, 3.55118104, -0.032732, -0.739518, 268.1, 64.5, new Rgb(0.86f, 0.8f, 0.72f)), "europa_color", true);
            yield return WithMap(Moon("Ganymede", "Jupiter", 2634.1, 9887.8, 1070400, 0.00194, 0.2299, 59.8522, 246.3051, 295.4604, 7.15455318, -0.005846, 0.005568, 268.2, 64.6, new Rgb(0.6f, 0.57f, 0.52f)), "ganymede_color", false);
            yield return WithMap(Moon("Callisto", "Jupiter", 2410.3, 7179.3, 1882700, 0.00727, 0.2197, 314.8165, 55.8575, 86.0337, 16.68901692, -0.001697, 0.001773, 268.7, 64.8, new Rgb(0.42f, 0.39f, 0.35f)), "callisto_color", true);

            yield return new BodyDefinition
            {
                Name = "Saturn", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 58232, Gm = 3.7931187e7,
                Elements = OrbitalElements.FromJpl(9.53667594, 0.05386179, 2.48599187, 49.95424423, 92.59887831, 113.66242448,
                    -0.00125060, -0.00050991, 0.00193609, 1222.49362201, -0.41897216, -0.28867794),
                PoleRaDeg = 40.59, PoleDecDeg = 83.54, RotationHours = 10.656,
                PrimeMeridianDeg = 38.90, PrimeMeridianRateDegPerDay = 810.7939024,
                Color = new Rgb(0.85f, 0.77f, 0.6f), Texture = "saturn_color", GasGiant = true,
                AtmosphereColor = new Rgb(0.95f, 0.85f, 0.6f), AtmosphereStrength = 0.3f,
                // C ring inner edge to A ring outer edge.
                RingInnerKm = 74658, RingOuterKm = 136775,
                Summary = "Less dense than water. Its main rings are ~10 m thick and 270,000 km across.",
            };
            yield return WithMap(Moon("Enceladus", "Saturn", 252.1, 7.21, 238400, 0.00476, 0.0, 320.0881, 171.4837, 10.7909, 1.3702181, 0, 0.337987, 40.6, 83.5, new Rgb(0.95f, 0.95f, 0.97f)), "enceladus_color", true);
            var titan = Moon("Titan", "Saturn", 2574.7, 8978.1, 1221900, 0.02869, 0.3472, 339.0229, 185.0478, 163.5777, 15.9454481, -0.001294, 0.001419, 36.4, 84.0, new Rgb(0.85f, 0.62f, 0.32f));
            titan.AtmosphereColor = new Rgb(0.9f, 0.65f, 0.3f);
            titan.AtmosphereStrength = 0.9f;
            // Cassini ISS 938 nm surface map, under the haze that hides it in visible light.
            WithMap(titan, "titan_surface", false);
            titan.SurfaceHaze = 0.82f;
            titan.Summary = "Only moon with a thick atmosphere; methane lakes at its poles.";
            yield return titan;

            yield return new BodyDefinition
            {
                Name = "Uranus", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 25362, Gm = 5.793939e6,
                Elements = OrbitalElements.FromJpl(19.18916464, 0.04725744, 0.77263783, 313.23810451, 170.95427630, 74.01692503,
                    -0.00196176, -0.00004397, -0.00242939, 428.48202785, 0.40805281, 0.04240589),
                PoleRaDeg = 257.31, PoleDecDeg = -15.18, RotationHours = -17.24,
                PrimeMeridianDeg = 203.81, PrimeMeridianRateDegPerDay = -501.1600928,
                Color = new Rgb(0.65f, 0.85f, 0.9f), Texture = "uranus_color", GasGiant = true,
                AtmosphereColor = new Rgb(0.6f, 0.9f, 0.95f), AtmosphereStrength = 0.4f,
                Summary = "Rolls around the Sun on its side, axial tilt 98 degrees.",
            };
            yield return WithMap(Moon("Miranda", "Uranus", 235.8, 4.4, 129846, 0.00129, 4.427, 95.7866, 151.4373, 76.6609, 1.41347955, -0.055411, 0.055218, new Rgb(0.7f, 0.7f, 0.72f)), "miranda_color", true, mapNorthFlipped: true);
            yield return WithMap(Moon("Titania", "Uranus", 788.4, 228.2, 436298, 0.00158, 0.0645, 331.0516, 249.1247, 56.3287, 8.70586893, 0.00373, 0.003818, new Rgb(0.68f, 0.63f, 0.6f)), "titania_color", true, mapNorthFlipped: true);
            yield return WithMap(Moon("Oberon", "Uranus", 761.4, 205.3, 583511, 0.00153, 0.2035, 33.7756, 134.2823, 179.436, 13.46323712, -0.001675, 0.002236, new Rgb(0.6f, 0.55f, 0.53f)), "oberon_color", true, mapNorthFlipped: true);

            yield return new BodyDefinition
            {
                Name = "Neptune", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 24622, Gm = 6.836529e6,
                Elements = OrbitalElements.FromJpl(30.06992276, 0.00859048, 1.77004347, -55.12002969, 44.96476227, 131.78422574,
                    0.00026291, 0.00005105, 0.00035372, 218.45945325, -0.32241464, -0.00508664),
                PoleRaDeg = 299.36, PoleDecDeg = 43.46, RotationHours = 16.11,
                PrimeMeridianDeg = 249.978, PrimeMeridianRateDegPerDay = 541.1397757,
                Color = new Rgb(0.35f, 0.5f, 0.9f), Texture = "neptune_color", GasGiant = true,
                AtmosphereColor = new Rgb(0.4f, 0.6f, 1f), AtmosphereStrength = 0.4f,
                Summary = "Fastest winds in the system, over 2,000 km/h.",
            };
            var triton = Moon("Triton", "Neptune", 1353.4, 1427.6, 354800, 0.00013, 157.2335, 152.2806, 76.2454, 341.4831, 5.87657447, 0.001458, 0.004035, 299.8, 43.1, new Rgb(0.85f, 0.8f, 0.78f));
            triton.RotationHours = -triton.RotationHours;
            triton.Summary = "Orbits backwards: a captured Kuiper belt object.";
            WithMap(triton, "triton_color", true);
            yield return triton;

            yield return new BodyDefinition
            {
                Name = "Pluto", Kind = BodyKind.DwarfPlanet, ParentName = "Sun", RadiusKm = 1188.3, Gm = 869.6,
                Elements = OrbitalElements.FromJpl(39.48211675, 0.24882730, 17.14001206, 238.92903833, 224.06891629, 110.30393684,
                    -0.00031596, 0.00005170, 0.00004818, 145.20780515, -0.04062942, -0.01183482),
                // IAU gives Pluto's positive (right-hand) pole, RA 132.99 Dec -6.16. Its antipode is the
                // north pole in the planets' convention, about which Pluto, like Uranus, spins backwards.
                PoleRaDeg = 312.99, PoleDecDeg = 6.16, RotationHours = -153.2935,
                // New Horizons maps use the positive pole as north, with W = 302.695 + 56.3625225 d about it.
                MapNorthFlipped = true, PrimeMeridianDeg = 302.695, PrimeMeridianRateDegPerDay = 56.3625225,
                Color = new Rgb(0.8f, 0.68f, 0.55f), Texture = "pluto_color", TintTexture = true,
                Summary = "Its eccentric orbit dips inside Neptune's for 20 years of every 248.",
            };
            yield return WithMap(Moon("Charon", "Pluto", 606, 105.88, 19595.8, 0.00016, 0.0861, 28.2176, 144.2491, 148.7655, 6.387222074, 0.000156, 0.000002, new Rgb(0.6f, 0.58f, 0.56f)), "charon_color", true);

            yield return new BodyDefinition
            {
                Name = "Eris", Kind = BodyKind.DwarfPlanet, ParentName = "Sun", RadiusKm = 1163, Gm = 1108,
                Elements = OrbitalElements.FromClassical(sbdbEpoch, 67.864 * Astro.AuKm, 0.43607, 44.040, 35.951, 151.639,
                    205.989, OrbitalElements.PeriodFromGm(67.864 * Astro.AuKm, 1.32712440018e11)),
                RotationHours = 378.9,
                Color = new Rgb(0.92f, 0.92f, 0.9f),
                Summary = "Most massive known dwarf planet; its discovery got Pluto reclassified.",
            };

            // Three real main-belt asteroids, appended after everything else so no other body's index moves.
            // Each hides a pirate haven in the game layer (World.BuildSettlements).
            yield return Asteroid("Psyche", 111, 1.601, 2.925720466462538, 0.1349324738201893, 3.098749116151128, 149.9753859305033,
                230.0326782748359, 79.76939505329617, 4.196, new Rgb(0.6f, 0.58f, 0.55f),
                "Metal-rich M-type asteroid, perhaps an exposed planetary core; NASA's Psyche probe arrives in 2029.");
            yield return Asteroid("Lutetia", 49, 0.1134, 2.434430742712361, 0.1647703512177245, 3.064452891064415, 80.83855721319993,
                249.8802780888828, 298.7726414605465, 8.1655, new Rgb(0.52f, 0.5f, 0.48f),
                "Primitive 100 km asteroid, photographed by Rosetta as it flew past in July 2010.");
            yield return Asteroid("Ida", 16, 0.00275, 2.863348031699649, 0.04610962795708528, 1.130363094271507, 323.5366609419851,
                113.2571826848795, 49.64769088009876, 4.634, new Rgb(0.62f, 0.56f, 0.48f),
                "Koronis-family asteroid; Galileo found the first asteroid moon, Dactyl, orbiting it in 1993.");

            // Three real Jupiter Trojans, appended after the belt asteroids so no other index moves: two of
            // the Greek camp 60° ahead of Jupiter (L4) and one of the Trojan camp 60° behind (L5). Mining
            // grounds and a hidden haven in the game layer (World.BuildSettlements).
            yield return Trojan(Asteroid("Hektor", 112.5, 0.527, 5.276986092673823, 0.02434489234275647, 18.14715224028094, 342.8018513679027,
                181.288859678094, 24.91385327109803, 6.924, new Rgb(0.47f, 0.4f, 0.35f),
                "Largest Jupiter Trojan, a 225 km contact binary leading Jupiter at L4, with a small moon, Skamandrios."), LagrangePoint.L4);
            yield return Trojan(Asteroid("Patroclus", 70.2, 0.091, 5.205975173988769, 0.1391467916238868, 22.06359071054631, 44.34968792774898,
                308.8377281363527, 58.67543160284279, 102.8, new Rgb(0.44f, 0.39f, 0.35f),
                "Twin Trojan at L5: Patroclus and Menoetius, two 110 km bodies circling each other every 4.3 days; Lucy flies by in 2033."), LagrangePoint.L5);
            yield return Trojan(Asteroid("Eurybates", 31.9, 0.0091, 5.217371617810976, 0.09059867172297777, 8.05147293527498, 43.5587275998936,
                28.69968222483612, 125.7480299363769, 8.711, new Rgb(0.42f, 0.4f, 0.38f),
                "Grey Trojan at L4, the largest of a collisional family, with a 1 km moon, Queta; Lucy flies past in August 2027."), LagrangePoint.L4);
        }

        static BodyDefinition Trojan(BodyDefinition body, LagrangePoint point)
        {
            body.TrojanPoint = point;
            body.CoOrbitalWith = "Jupiter";
            return body;
        }

        /// <summary>
        /// A minor planet on its JPL Small-Body Database osculating orbit (heliocentric ecliptic
        /// J2000, epoch JD 2461200.5), with its period from Kepler's third law.
        /// </summary>
        static BodyDefinition Asteroid(string name, double radiusKm, double gm, double aAu, double e, double iDeg, double nodeDeg,
            double argPeriDeg, double meanAnomalyDeg, double rotationHours, Rgb color, string summary)
        {
            const double epoch = 2461200.5;
            return new BodyDefinition
            {
                Name = name, Kind = BodyKind.Asteroid, ParentName = "Sun", RadiusKm = radiusKm, Gm = gm,
                Elements = OrbitalElements.FromClassical(epoch, aAu * Astro.AuKm, e, iDeg, nodeDeg, argPeriDeg, meanAnomalyDeg,
                    OrbitalElements.PeriodFromGm(aAu * Astro.AuKm, 1.32712440018e11)),
                RotationHours = rotationHours,
                Color = color,
                Summary = summary,
            };
        }

        /// <summary>
        /// A regular satellite with synchronous rotation. Elements are at J2000, in the Laplace plane
        /// whose pole is given (RA/Dec, equatorial J2000), or in the parent's equator when it is not.
        /// Rates are degrees per day.
        /// </summary>
        static BodyDefinition Moon(string name, string parent, double radiusKm, double gm, double aKm, double e, double iDeg,
            double nodeDeg, double argPeriDeg, double meanAnomalyDeg, double periodDays, double nodeRate, double varpiRate,
            double laplaceRaDeg, double laplaceDecDeg, Rgb color)
        {
            return new BodyDefinition
            {
                Name = name, Kind = BodyKind.Moon, ParentName = parent, RadiusKm = radiusKm, Gm = gm,
                Elements = OrbitalElements.FromClassical(Astro.J2000, aKm, e, iDeg, nodeDeg, argPeriDeg, meanAnomalyDeg, periodDays,
                    nodeRate, varpiRate),
                Plane = OrbitPlane.ParentEquator,
                LaplacePoleRaDeg = laplaceRaDeg,
                LaplacePoleDecDeg = laplaceDecDeg,
                RotationHours = periodDays * 24.0,
                FacesParent = true,
                Color = color,
            };
        }

        /// <summary>
        /// Attaches a colour map (Resources/Textures; sources in SOURCES.md). Single-band mosaics are
        /// tinted with the body's catalogue colour.
        /// </summary>
        static BodyDefinition WithMap(BodyDefinition body, string texture, bool tint, bool mapNorthFlipped = false)
        {
            body.Texture = texture;
            body.TintTexture = tint;
            body.MapNorthFlipped = mapNorthFlipped;
            return body;
        }

        static BodyDefinition Moon(string name, string parent, double radiusKm, double gm, double aKm, double e, double iDeg,
            double nodeDeg, double argPeriDeg, double meanAnomalyDeg, double periodDays, double nodeRate, double varpiRate, Rgb color) =>
            Moon(name, parent, radiusKm, gm, aKm, e, iDeg, nodeDeg, argPeriDeg, meanAnomalyDeg, periodDays, nodeRate, varpiRate,
                double.NaN, double.NaN, color);
    }
}
