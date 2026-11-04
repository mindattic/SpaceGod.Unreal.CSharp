using System;

namespace Unity.SpaceGod.Core
{
    public enum BodyKind
    {
        Star,
        Planet,
        DwarfPlanet,
        Moon,

        /// <summary>The empty centre of mass a multiple star orbits (Alpha Centauri A and B): invisible, the system's root.</summary>
        Barycentre,

        /// <summary>
        /// A large artificial station on its own orbit about a star (game layer): a port and a lane stop. A Void
        /// Base's lone body (<see cref="VoidBaseBuilder"/>) is this kind too: there it is the gravity generator
        /// itself, with no star to orbit at all.
        /// </summary>
        Station,

        /// <summary>A minor body smaller than a dwarf planet: the main belt's Psyche, Lutetia and Ida, Jupiter's Trojans Hektor, Patroclus and Eurybates.</summary>
        Asteroid,
    }

    /// <summary>How sure astronomy is that a body exists.</summary>
    public enum BodyStatus
    {
        Confirmed,

        /// <summary>Seen, but not yet confirmed (Alpha Centauri A's JWST giant).</summary>
        Candidate,

        /// <summary>Claimed, then contested (Proxima c).</summary>
        Disputed,

        /// <summary>Invented for the game layer (stations).</summary>
        Fictional,
    }

    /// <summary>Which plane a body's orbital elements are measured against.</summary>
    public enum OrbitPlane
    {
        /// <summary>J2000 ecliptic (planets, the Moon, minor planets).</summary>
        Ecliptic,

        /// <summary>
        /// The parent's equator, or the moon's Laplace plane when its pole is set (regular satellites).
        /// The node is measured from the plane's ascending node on the ecliptic.
        /// </summary>
        ParentEquator,
    }

    public readonly struct Rgb
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;

        public Rgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }
    }

    /// <summary>Static, physical description of one body. Everything here is real data unless noted.</summary>
    public sealed class BodyDefinition
    {
        public string Name;
        public BodyKind Kind;
        public string ParentName;
        public double RadiusKm;

        /// <summary>Gravitational parameter GM in km³/s².</summary>
        public double Gm;

        public OrbitalElements Elements;
        public OrbitPlane Plane = OrbitPlane.Ecliptic;

        /// <summary>IAU north pole, equatorial J2000. NaN = unknown (falls back to ecliptic north).</summary>
        public double PoleRaDeg = double.NaN;
        public double PoleDecDeg = double.NaN;

        /// <summary>Sidereal rotation period in hours; negative = retrograde about the IAU pole.</summary>
        public double RotationHours;

        public Rgb Color = new Rgb(0.7f, 0.7f, 0.7f);
        public string Texture;

        /// <summary>Fraction of the map's height at each pole with no data (stretched over from the last valid row).</summary>
        public float TexturePoleMargin;

        /// <summary>
        /// The colour map is a single-band (greyscale) mosaic, so it is multiplied by <see cref="Color"/>
        /// (the body's documented average colour). False for genuine colour maps.
        /// </summary>
        public bool TintTexture;

        /// <summary>Tangent-space relief map derived from real elevation data (Resources/Textures), or null.</summary>
        public string NormalMap;

        /// <summary>Drawn with the banded, wind-driven gas giant shader instead of the solid-surface one.</summary>
        public bool GasGiant;

        /// <summary>
        /// How much of the surface an opaque atmosphere hides in visible light (0 = none, 1 = all),
        /// drawn as a layer of <see cref="AtmosphereColor"/> over the map (Titan's haze, Venus's clouds).
        /// </summary>
        public float SurfaceHaze;

        // ---- Orientation (see BodyOrientation)

        /// <summary>IAU prime meridian angle W at J2000 (deg). NaN = unknown, the meridian starts at the equator's node.</summary>
        public double PrimeMeridianDeg = double.NaN;

        /// <summary>IAU dW/dt (deg/day) about the map's north pole. NaN = derived from <see cref="RotationHours"/>.</summary>
        public double PrimeMeridianRateDegPerDay = double.NaN;

        /// <summary>
        /// Synchronous rotator whose 0° meridian faces its parent (the IAU convention for the Moon and
        /// the regular satellites). The meridian is pointed at the parent each frame; libration is not modelled.
        /// </summary>
        public bool FacesParent;

        /// <summary>
        /// The map's north is the opposite of <see cref="Pole"/>. Pluto's maps use the IAU positive
        /// (right-hand) pole, and the Uranian satellites' maps use Uranus's IAU north, which is the
        /// opposite of the spin axis their catalogue pole follows.
        /// </summary>
        public bool MapNorthFlipped;
        public Rgb AtmosphereColor;
        public float AtmosphereStrength;

        /// <summary>Ring system radii (km). Zero = no rings.</summary>
        public double RingInnerKm;
        public double RingOuterKm;

        /// <summary>Pole of a moon's Laplace plane, equatorial J2000. NaN = the parent's equator.</summary>
        public double LaplacePoleRaDeg = double.NaN;
        public double LaplacePoleDecDeg = double.NaN;

        /// <summary>
        /// Game layer: an empty orbit marked out for settlers to build a habitat in (a frontier star's, see
        /// <see cref="StationSpec.HabitatSite"/>), surveyed as a colony site though nothing stands there yet.
        /// </summary>
        public bool HabitatSite;

        public string Summary;

        /// <summary>
        /// A Trojan: the Lagrange point of its parent's planet (<see cref="CoOrbitalWith"/>) whose swarm it
        /// librates about, sharing that planet's orbit 60° ahead (L4) or behind (L5). None otherwise.
        /// </summary>
        public LagrangePoint TrojanPoint;
        public string CoOrbitalWith;

        public BodyStatus Status = BodyStatus.Confirmed;

        /// <summary>Exoplanets: the mass is m sin i (radial velocity alone), a lower limit.</summary>
        public bool MassIsMinimum;

        /// <summary>Exoplanets: where the orbit sits against the host star's habitable zone (see <see cref="HabitableZones"/>).</summary>
        public HabitableZone HabitableZone;

        /// <summary>Exoplanets: year of discovery (0 = not recorded).</summary>
        public int DiscoveryYear;

        /// <summary>Stars: spectral type, when catalogued ("M4.0V", "DA2").</summary>
        public string SpectralType;

        /// <summary>
        /// Multiplies the body's orbit about its parent. A binary star's two members share their
        /// relative orbit, each scaled by the other's share of the mass (with a minus sign for one),
        /// so they circle their barycentre on opposite sides (as JPL's Earth-Moon pair does).
        /// </summary>
        public double OrbitScale = 1;

        /// <summary>Stars: photosphere temperature (K) and luminosity (solar), for colour and lighting.</summary>
        public double TeffK = 5772;
        public double LuminositySolar = 1;

        /// <summary>
        /// Game layer: a star body drawn dark and light-swallowing instead of lit (U.SPC-T372) -- a Void Base's
        /// generator, a "black hole in a bottle": its <see cref="Color"/> is used as-is instead of the usual
        /// blackbody tint, with a tight violet glow standing in for an event horizon instead of a corona.
        /// </summary>
        public bool Singularity;

        // Resolved by BodyCatalog.
        public int Index { get; internal set; } = -1;
        public int ParentIndex { get; internal set; } = -1;
        public DVec3 Pole { get; internal set; } = DVec3.UnitZ;

        /// <summary>
        /// Direction of the spin angular momentum: the IAU pole, flipped for retrograde rotators.
        /// Regular moons orbit in this sense (Uranus's and Pluto's moons go "backwards" about the IAU pole).
        /// </summary>
        public DVec3 SpinAxis => RotationHours < 0 ? -Pole : Pole;

        /// <summary>Sphere of influence radius (km), Laplace's a·(m/M)^0.4, floored at a few body radii.</summary>
        public double SoiKm { get; internal set; } = double.PositiveInfinity;

        /// <summary>Farthest apoapsis of the body's moons (km); zero when it has none.</summary>
        public double MoonReachKm { get; internal set; }

        /// <summary>For a star with a parent (in a multiple system): farthest apoapsis of what orbits it (km).</summary>
        public double SystemReachKm { get; internal set; }

        /// <summary>Closest periapsis of anything orbiting the body (km); zero when nothing does.</summary>
        public double InnermostChildKm { get; internal set; }

        /// <summary>Drawn and listed (the barycentre is neither).</summary>
        public bool IsVisible => Kind != BodyKind.Barycentre;

        public bool HasRings => RingOuterKm > 0;
        public bool IsStar => Kind == BodyKind.Star;

        /// <summary>Its children are laid out with the stellar (AU power-law) mapping: a star or a barycentre.</summary>
        public bool UsesStellarMapping => Kind == BodyKind.Star || Kind == BodyKind.Barycentre;

        public override string ToString() => Name;
    }
}
