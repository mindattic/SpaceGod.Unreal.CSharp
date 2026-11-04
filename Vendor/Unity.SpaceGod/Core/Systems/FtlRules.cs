using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>What made a gravity field: a body's own mass, or a machine built to throw one.</summary>
    public enum GravityFieldKind
    {
        /// <summary>A star's or a planet's mass: the field is where its pull is above <see cref="FtlRules.WellGravity"/>.</summary>
        Mass,

        /// <summary>A gravity projector: a station (or anything else) throwing a field to pull hulls out of FTL.</summary>
        Projector,
    }

    /// <summary>A sphere an FTL drive will not hold in (U.SPC-T418): a body's well, or a projector's field.</summary>
    public readonly struct GravityField
    {
        public readonly string Name;
        public readonly GravityFieldKind Kind;
        public readonly GalacticPosition Center;
        public readonly double RadiusKm;

        /// <summary>Whose it is (a projector's); null for a body's mass, which pulls on everyone.</summary>
        public readonly Faction Owner;

        public GravityField(string name, GravityFieldKind kind, GalacticPosition center, double radiusKm, Faction owner = null)
        {
            Name = name;
            Kind = kind;
            Center = center;
            RadiusKm = Math.Max(0, radiusKm);
            Owner = owner;
        }

        /// <summary>Whether <paramref name="place"/> is inside it.</summary>
        public bool Contains(GalacticPosition place, double slack = 1) => place.KmFrom(Center).Length < RadiusKm * slack;

        public override string ToString() => Name;
    }

    /// <summary>
    /// A gravity projector out in open space (U.SPC-T418): a field of <see cref="RadiusKm"/> about <see cref="Position"/>
    /// that pulls any hull not its owner's out of FTL at its edge, to cross it under its ordinary drive. Placed with
    /// <see cref="DeepSpace.AddProjector"/>.
    /// </summary>
    public sealed class GravityProjector
    {
        public string Name;
        public Faction Owner;
        public GalacticPosition Position;
        public double RadiusKm;

        /// <summary>Switched off, it pulls nobody out.</summary>
        public bool Active = true;

        public GravityField Field => new GravityField(Name, GravityFieldKind.Projector, Position, RadiusKm, Owner);

        public override string ToString() => Name;
    }

    /// <summary>
    /// The physics and rules of FTL (U.SPC-T418), the same for the helm's own drive and every group crossing between the
    /// stars, every limit data-tunable here and in the settings menu's FTL group:
    ///
    ///  - Gravity wells. A drive cannot engage, and drops out, inside a well: where a body's pull is above
    ///    <see cref="WellGravity"/> (a well's radius is sqrt(GM / g), so a star's is a couple of AU, Jupiter's ten million
    ///    km, Earth's half a million). Arrivals end at the edge of a system (its outer rim, <see cref="CruiseDrives.WellLimitKm"/>)
    ///    and clear of any planet's well, and finish under the ordinary drive.
    ///  - Interdiction. A group under way that runs into a star's well, or a gravity projector's field, on its way is pulled
    ///    out at the edge, crosses it under its ordinary drive at <see cref="CrossingSpeedKmS"/>, and spools again on the far side.
    ///  - Spool-up and cooldown. Each drive charges for its <see cref="CruiseDriveSpec.SpoolDays"/> and, after it drops out,
    ///    cannot charge again for its <see cref="CruiseDriveSpec.CooldownDays"/> (both scaled by the menu).
    ///  - Power and fuel. A charge burns a share of the tanks, and every light-year a little more
    ///    (<see cref="PropellantFor"/>); the helm will not spool without it.
    ///  - Momentum. The hull's own velocity going in is the velocity it comes out with (<see cref="Cruise.CarriedVelocityKmS"/>),
    ///    and the drive flies a straight line: it does not turn, only drops out.
    ///  - Top speed. Each drive's rated speed, times <see cref="CruiseDrives.SpeedScale"/>: at the default a capital
    ///    drive crosses the galaxy in centuries; at the menu's top, in a few years (<see cref="GalaxyCrossingDays"/>).
    /// </summary>
    [SettingsSource]
    public static class FtlRules
    {
        /// <summary>The Sun's gravitational parameter, km³/s².</summary>
        public const double SunGm = 1.32712440018e11;

        /// <summary>The galaxy's width the top speed is measured against, light-years (the disc's diameter).</summary>
        public const double GalaxyWidthLy = 2 * GalacticFrame.DiscRadiusLy;

        public static readonly RangeSetting WellGravity = new RangeSetting("ftl.wellGravity", SettingGroups.Ftl,
            "Gravity well threshold", 1.5, 0.1, 20, 0.1,
            "The pull (mm/s²) above which an FTL drive will not engage and drops out: it sets every star's and planet's well " +
            "(about 2 AU about the Sun, 9 million km about Jupiter, half a million about Earth at the default). Lower makes the wells wider.",
            v => v.ToString("0.0", CultureInfo.InvariantCulture) + " mm/s²", "ftl gravity well threshold planet star interdiction mass");

        public static readonly RangeSetting SpoolScale = new RangeSetting("ftl.spoolScale", SettingGroups.Ftl,
            "Spool-up time", 1, 0, 4, 0.25,
            "How long every FTL drive charges before it engages, as a multiple of its rated spool-up (a capital drive's is six hours).",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "ftl spool charge time drive");

        public static readonly RangeSetting CooldownScale = new RangeSetting("ftl.cooldownScale", SettingGroups.Ftl,
            "Cooldown", 1, 0, 4, 0.25,
            "How long an FTL drive must cool after it drops out before it can charge again, as a multiple of its rated cooldown " +
            "(a capital drive's is twelve hours).",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "ftl cooldown drive recharge");

        public static readonly RangeSetting FuelScale = new RangeSetting("ftl.fuelScale", SettingGroups.Ftl,
            "Fuel cost", 1, 0, 4, 0.25,
            "How much reaction mass an FTL jump burns, as a multiple of the drive's rated cost (a capital drive's charge takes 2% " +
            "of the tanks and each light-year 0.2% more). Zero makes jumps free.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "ftl fuel propellant power cost");

        public static readonly RangeSetting CrossingSpeedKmS = new RangeSetting("ftl.crossingSpeedKmS", SettingGroups.Ftl,
            "Interdicted crossing speed", 3000, 100, 30000, 100,
            "How fast (km/s) a group pulled out of FTL by a star's well or a gravity projector crosses it under its ordinary drive " +
            "before it can spool again on the far side.",
            v => v.ToString("N0", CultureInfo.InvariantCulture) + " km/s", "ftl interdiction crossing speed normal drive");

        // ---------------------------------------------------------------- wells

        /// <summary>The threshold pull, km/s².</summary>
        public static double ThresholdKmS2 => WellGravity.Value * 1e-6;

        /// <summary>How far out a body of <paramref name="gm"/> (km³/s²) pulls harder than the threshold, km: sqrt(GM / g).</summary>
        public static double WellRadiusKm(double gm) => WellRadiusKm(gm, WellGravity.Value);

        /// <summary>As <see cref="WellRadiusKm(double)"/>, for a threshold of <paramref name="mmS2"/> mm/s².</summary>
        public static double WellRadiusKm(double gm, double mmS2) => gm > 0 && mmS2 > 0 ? Math.Sqrt(gm / (mmS2 * 1e-6)) : 0;

        /// <summary>A star's rough mass from its spectral class, in Suns (a plain star on the charts carries no mass of its own).</summary>
        public static double SolarMasses(string spectralType)
        {
            if (string.IsNullOrEmpty(spectralType)) return 1;
            switch (char.ToUpperInvariant(spectralType.TrimStart()[0]))
            {
                case 'O': return 30;
                case 'B': return 6;
                case 'A': return 2;
                case 'F': return 1.3;
                case 'G': return 1;
                case 'K': return 0.7;
                case 'M': return 0.3;
                case 'D': return 0.6;
                case 'L':
                case 'T':
                case 'Y': return 0.05;
                default: return 1;
            }
        }

        /// <summary>The mass (GM, km³/s²) a running system's stars hold together.</summary>
        public static double StarsGm(StarSystem system)
        {
            if (system == null) return 0;
            double gm = 0;
            foreach (var b in system.Catalog.Bodies)
                if (b.Kind == BodyKind.Star) gm += Math.Max(0, b.Gm);
            return gm;
        }

        /// <summary>The well of a running system's stars, about its root.</summary>
        public static GravityField StarWell(StarSystem system) =>
            new GravityField(system.Name, GravityFieldKind.Mass, system.Origin, WellRadiusKm(StarsGm(system)));

        /// <summary>The well of a plain star on the charts.</summary>
        public static GravityField StarWell(NearbyStar star) =>
            new GravityField(star.Name, GravityFieldKind.Mass, star.Origin, WellRadiusKm(SolarMasses(star.SpectralType) * SunGm));

        /// <summary>
        /// The body (other than the system's root, whose well is the outer rim's business) whose well <paramref name="localKm"/>
        /// (km from <paramref name="here"/>'s root, the ephemeris as it stands) is inside, or null: a planet, a moon, or one
        /// of a multiple's stars.
        /// </summary>
        public static BodyDefinition BodyWellAt(StarSystem here, DVec3 localKm)
        {
            int i = BodyWellIndex(here, localKm);
            return i < 0 ? null : here.Catalog[i];
        }

        /// <summary>As <see cref="BodyWellAt"/>, the body's index in the catalogue, or -1.</summary>
        public static int BodyWellIndex(StarSystem here, DVec3 localKm)
        {
            if (here == null) return -1;
            var cat = here.Catalog;
            int best = -1;
            double bestGm = 0;
            for (int i = 1; i < cat.Count; i++)
            {
                var b = cat[i];
                if (!(b.Gm > 0)) continue;
                if (b.Kind != BodyKind.Star && b.Kind != BodyKind.Planet && b.Kind != BodyKind.DwarfPlanet && b.Kind != BodyKind.Moon) continue;
                double r = WellRadiusKm(b.Gm);
                if (r <= b.RadiusKm) continue;
                if ((localKm - here.Ephemeris.Helio(i)).LengthSquared >= r * r) continue;
                // Of two wells it is in (a moon's inside its planet's), the heavier names it.
                if (b.Gm > bestGm)
                {
                    best = i;
                    bestGm = b.Gm;
                }
            }
            return best;
        }

        /// <summary>
        /// <paramref name="localKm"/> moved out of any planet's or moon's well it is in, straight away from that body to just
        /// past its edge (where an arrival drops out, to finish under the ordinary drive).
        /// </summary>
        public static DVec3 ClearOfBodyWells(StarSystem here, DVec3 localKm)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                int i = BodyWellIndex(here, localKm);
                if (i < 0) return localKm;
                var at = here.Ephemeris.Helio(i);
                var off = localKm - at;
                var dir = off.LengthSquared > 0 ? off.Normalized : localKm.LengthSquared > 0 ? localKm.Normalized : DVec3.UnitX;
                localKm = at + dir * (WellRadiusKm(here.Catalog[i].Gm) * 1.001);
            }
            return localKm;
        }

        // ---------------------------------------------------------------- drive costs

        /// <summary>
        /// The reaction mass (tonnes) <paramref name="ship"/> burns for a jump of <paramref name="ly"/> light-years on
        /// <paramref name="drive"/>: the charge's share of its tanks and the per-light-year share, times <see cref="FuelScale"/>.
        /// </summary>
        public static double PropellantFor(CruiseDriveSpec drive, Ship ship, double ly)
        {
            if (drive == null || ship == null || ship.Spec == null) return 0;
            double share = (drive.ChargePropellantShare + drive.PropellantSharePerLy * Math.Max(0, ly)) * FuelScale.Value;
            return Math.Max(0, share) * ship.PropellantCapacity;
        }

        /// <summary>How far (ly) <paramref name="ship"/>'s tanks as they stand take it on <paramref name="drive"/> in one jump.</summary>
        public static double RangeLy(CruiseDriveSpec drive, Ship ship)
        {
            if (drive == null || ship == null) return 0;
            double scale = FuelScale.Value;
            if (scale <= 0 || drive.PropellantSharePerLy <= 0) return double.PositiveInfinity;
            double left = ship.PropellantCapacity > 0 ? ship.Propellant / ship.PropellantCapacity / scale - drive.ChargePropellantShare : 0;
            return Math.Max(0, left / drive.PropellantSharePerLy);
        }

        /// <summary>When <paramref name="ship"/>'s drive is cool enough to charge again.</summary>
        public static double CoolUntil(Ship ship) => ship == null ? 0 : ship.FtlCoolUntilJd;

        /// <summary>Starts <paramref name="ship"/>'s drive cooling at <paramref name="jd"/> (it has just dropped out).</summary>
        public static void StartCooldown(Ship ship, CruiseDriveSpec drive, double jd)
        {
            if (ship == null || drive == null) return;
            ship.FtlCoolUntilJd = Math.Max(ship.FtlCoolUntilJd, jd + drive.CooldownDays);
        }

        /// <summary>Days for <paramref name="drive"/>, at the menu's top speed now, to cross the whole galaxy (no stops).</summary>
        public static double GalaxyCrossingDays(CruiseDriveSpec drive) => CruiseProfile.For(drive, GalaxyWidthLy).TotalDays;

        /// <summary>
        /// The first field among <paramref name="fields"/> the straight run from <paramref name="from"/> to <paramref name="to"/>
        /// enters (not one the run starts inside; one it ends inside is entered on the way), and how far along the run (0..1) it is entered. False if none.
        /// </summary>
        public static bool FirstEntry(GalacticPosition from, GalacticPosition to, IEnumerable<GravityField> fields, out GravityField hit, out double along)
        {
            hit = default;
            along = double.PositiveInfinity;
            var d = to.KmFrom(from);
            double len2 = d.LengthSquared;
            if (len2 <= 0) return false;
            double len = Math.Sqrt(len2);
            foreach (var f in fields)
            {
                if (f.RadiusKm <= 0) continue;
                var c = f.Center.KmFrom(from);
                double r2 = f.RadiusKm * f.RadiusKm;
                if (c.LengthSquared <= r2) continue;
                // |t d - c| = r: the nearer root, if it is on the run.
                double b = DVec3.Dot(c, d) / len2;
                if (b <= 0 || b >= 1 + f.RadiusKm / len) continue;
                double disc = b * b - (c.LengthSquared - r2) / len2;
                if (disc < 0) continue;
                double t = b - Math.Sqrt(disc);
                if (t < 0 || t > 1 || t >= along) continue;
                along = t;
                hit = f;
            }
            return !double.IsPositiveInfinity(along);
        }

        /// <summary>Where a straight run through <paramref name="field"/> entered at <paramref name="entry"/>, heading
        /// <paramref name="dir"/> (unit), comes out the far side.</summary>
        public static GalacticPosition ExitPoint(GravityField field, GalacticPosition entry, DVec3 dir)
        {
            double chord = 2 * DVec3.Dot(field.Center.KmFrom(entry), dir);
            return chord > 0 ? entry.Offset(dir * chord) : entry;
        }
    }
}
