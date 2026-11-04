using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Maps true (km) geometry into scene units, blending between two presentations:
    ///
    ///  - Realistic (Blend = 0): one linear scale for everything, 1 unit = 1000 km. The planets are
    ///    specks; Neptune is 4.5 million units out.
    ///  - Gameplay (Blend = 1): radii and distances are each compressed by power laws so the system
    ///    is navigable and the planets read as worlds rather than dots.
    ///
    /// Every mapping is radial about a parent (directions are never changed, only distances), so
    /// orbit shapes, phases and alignments stay true in both modes. Each mapping also sends a
    /// parent's surface to its displayed surface (x = 1 → 1 display radius), so things in low orbit
    /// stay outside the body at every blend. Intermediate blends interpolate in log space, which
    /// reads as a smooth zoom across the factor-of-40 differences.
    /// </summary>
    public sealed class ScaleModel
    {
        public const double KmPerUnit = 1000.0;

        // Gameplay radii: Earth = 255 units, others by (R / R_earth)^0.45.
        public const double GameEarthRadius = 255.0;
        public const double GameRadiusExponent = 0.45;

        // Gameplay heliocentric distance: 20000 units per AU^0.55 from the solar surface.
        public const double GameAuScale = 20000.0;
        public const double GameAuExponent = 0.55;

        // Gameplay distance around a planet: R_display · (1 + c·(x - 1)^p), x in body radii.
        public const double GameLocalC = 1.17;
        public const double GameLocalP = 0.44;

        // Gameplay ship length: 12 units per km^0.5 (a 3 km carrier is ~21 units against Earth's 255 radius).
        public const double GameShipScale = 12.0;
        public const double GameShipExponent = 0.5;

        const double EarthRadiusKm = 6371.0;

        double blend = 1.0;

        /// <summary>0 = realistic, 1 = gameplay.</summary>
        public double Blend
        {
            get => blend;
            set => blend = Math.Max(0, Math.Min(1, value));
        }

        public static double LogLerp(double a, double b, double t)
        {
            if (a <= 0 || b <= 0) return a + (b - a) * t;
            return Math.Exp(Math.Log(a) + (Math.Log(b) - Math.Log(a)) * t);
        }

        public static double RealisticRadius(double radiusKm) => radiusKm / KmPerUnit;

        public static double GameplayRadius(double radiusKm) =>
            GameEarthRadius * Math.Pow(radiusKm / EarthRadiusKm, GameRadiusExponent);

        public double DisplayRadius(double radiusKm) =>
            LogLerp(RealisticRadius(radiusKm), GameplayRadius(radiusKm), blend);

        public double DisplayRadius(BodyDefinition body) => DisplayRadius(body.RadiusKm);

        public double ShipLength(double lengthKm) =>
            LogLerp(lengthKm / KmPerUnit, GameShipScale * Math.Pow(lengthKm, GameShipExponent), blend);

        /// <summary>Scene distance from the centre of <paramref name="body"/> for a true distance in km.</summary>
        public double LocalDistance(BodyDefinition body, double distanceKm) =>
            LogLerp(distanceKm / KmPerUnit, GameplayLocalDistance(body, distanceKm), blend);

        public static double GameplayLocalDistance(BodyDefinition body, double distanceKm)
        {
            double r = body.RadiusKm;
            double rDisplay = GameplayRadius(r);
            if (distanceKm <= r) return r > 0 ? rDisplay * distanceKm / r : 0;

            if (body.UsesStellarMapping)
            {
                return rDisplay + GameAuScale *
                    (Math.Pow(distanceKm / Astro.AuKm, GameAuExponent) - Math.Pow(r / Astro.AuKm, GameAuExponent));
            }

            double x = distanceKm / r;
            return rDisplay * (1.0 + GameLocalC * Math.Pow(x - 1.0, GameLocalP));
        }

        /// <summary>Inverse of <see cref="LocalDistance"/> (monotonic, so a bisection in log space is exact enough).</summary>
        public double InverseLocalDistance(BodyDefinition body, double sceneDistance)
        {
            if (sceneDistance <= 0) return 0;
            double lo = Math.Log(Math.Max(body.RadiusKm, 1) * 1e-6);
            double hi = Math.Log(1e15);
            for (int i = 0; i < 90; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (LocalDistance(body, Math.Exp(mid)) < sceneDistance) lo = mid;
                else hi = mid;
            }
            return Math.Exp(0.5 * (lo + hi));
        }

        /// <summary>d(scene distance)/d(true km) at a true distance; used to convert camera motion.</summary>
        public double LocalDistanceDerivative(BodyDefinition body, double distanceKm)
        {
            double h = Math.Max(distanceKm * 1e-5, 1e-6);
            double lo = Math.Max(distanceKm - h, 0);
            return (LocalDistance(body, distanceKm + h) - LocalDistance(body, lo)) / (distanceKm + h - lo);
        }

        /// <summary>Scene offset (Unity axes) from a body for a true ecliptic offset from it.</summary>
        public DVec3 LocalOffset(BodyDefinition body, DVec3 trueOffsetKm)
        {
            double d = trueOffsetKm.Length;
            if (d <= 0) return DVec3.Zero;
            return trueOffsetKm.SwapYZ * (LocalDistance(body, d) / d);
        }

        /// <summary>Inverse of <see cref="LocalOffset"/>: a scene offset (Unity axes) to a true ecliptic offset (km).</summary>
        public DVec3 InverseLocalOffset(BodyDefinition body, DVec3 sceneOffset)
        {
            double s = sceneOffset.Length;
            if (s <= 0) return DVec3.Zero;
            return sceneOffset.SwapYZ * (InverseLocalDistance(body, s) / s);
        }

        /// <summary>
        /// Lays every body out in scene space (Unity axes, Sun at the origin) from the ephemeris.
        /// Each body is placed relative to its parent's already-placed position.
        /// </summary>
        public void Layout(Ephemeris ephemeris, DVec3[] scenePositions)
        {
            var catalog = ephemeris.Catalog;
            for (int i = 0; i < catalog.Count; i++)
            {
                var b = catalog[i];
                if (b.ParentIndex < 0)
                {
                    scenePositions[i] = DVec3.Zero;
                    continue;
                }
                scenePositions[i] = scenePositions[b.ParentIndex] + LocalOffset(catalog[b.ParentIndex], ephemeris.Relative(i));
            }
        }

        /// <summary>
        /// Out to this many body radii, a point is placed with the body's own local mapping, exactly
        /// as parked ships are. Parking orbits go out to 7 radii.
        /// </summary>
        public const double NeighbourhoodRadii = 8.0;

        /// <summary>A planet's neighbourhood also reaches this far past its outermost moon, which is laid out locally.</summary>
        public const double NeighbourhoodMoonMargin = 1.25;

        /// <summary>How far (km) from a body points are placed with its own local mapping.</summary>
        public static double NeighbourhoodKm(BodyDefinition body) =>
            Math.Max(body.RadiusKm * NeighbourhoodRadii, Math.Max(body.MoonReachKm, body.SystemReachKm) * NeighbourhoodMoonMargin);

        /// <summary>
        /// Scene position (Unity axes) of a true heliocentric point (km), seen from the neighbourhood
        /// of <paramref name="body"/>: the Sun's mapping, corrected by each body from the top of
        /// <paramref name="body"/>'s chain down (see <see cref="MapThrough"/>).
        /// </summary>
        /// <param name="scene">Body scene positions from <see cref="Layout"/>, for the ephemeris' current date.</param>
        public DVec3 MapNear(Ephemeris ephemeris, DVec3[] scene, int body, DVec3 helioKm)
        {
            var chain = new List<int>();
            AddChain(ephemeris.Catalog, body, chain);
            return MapThrough(ephemeris, scene, chain, chain.Count, helioKm);
        }

        /// <summary>
        /// The Sun's mapping of a true heliocentric point, then each body's neighbourhood correction in
        /// order. Within a body's neighbourhood (<see cref="NeighbourhoodKm"/>) its correction is its
        /// own local mapping, exactly as parked ships and its moons are placed; beyond, it eases back
        /// into the mapping so far, staying at least as far out as the neighbourhood edge so a point
        /// never dips into the drawn body. The last body in the list wins near it. At realistic scale
        /// every mapping is the same linear one, so this is just km / KmPerUnit.
        /// </summary>
        DVec3 MapThrough(Ephemeris ephemeris, DVec3[] scene, List<int> bodies, int count, DVec3 helioKm)
        {
            if (count == 0) return scene[0] + LocalOffset(ephemeris.Catalog.Root, helioKm - ephemeris.Helio(0));

            int body = bodies[count - 1];
            var b = ephemeris.Catalog[body];
            var r = helioKm - ephemeris.Helio(body);
            double distance = r.Length;
            double edgeKm = NeighbourhoodKm(b);
            if (distance <= edgeKm) return scene[body] + LocalOffset(b, r);

            // Local and outer mappings disagree by c at the edge; the gap closes as the outer-map
            // separation s grows by 2|c| past the edge, at half rate, so distance never turns back.
            var toOuter = MapThrough(ephemeris, scene, bodies, count - 1, helioKm) - scene[body];
            double s = toOuter.Length;
            var edge = ephemeris.Helio(body) + r * (edgeKm / distance);
            double s1 = (MapThrough(ephemeris, scene, bodies, count - 1, edge) - scene[body]).Length;
            double l1 = LocalDistance(b, edgeKm);
            double c = l1 - s1;
            if (Math.Abs(c) < 1e-9 * Math.Max(1, l1)) return scene[body] + toOuter;

            double k = Math.Max(0, Math.Min(1, 1 - (s - s1) / (2 * Math.Abs(c))));
            if (k <= 0) return scene[body] + toOuter;
            double d = Math.Max(s + c * k, l1);
            var dir = DVec3.Lerp(toOuter / Math.Max(s, 1e-12), r.SwapYZ / distance, k).Normalized;
            return scene[body] + dir * d;
        }

        /// <summary>Appends <paramref name="body"/> and its ancestors below the Sun, outermost first, skipping any already listed.</summary>
        static void AddChain(BodyCatalog catalog, int body, List<int> into)
        {
            if (body <= 0) return;
            AddChain(catalog, catalog[body].ParentIndex, into);
            if (!into.Contains(body)) into.Add(body);
        }

        /// <summary>
        /// Scene position of a transiting ship at a fraction of its path: its true position, mapped
        /// with the neighbourhood corrections of both ends and the planets they orbit. The origin's
        /// correction is applied last at departure and the destination's by arrival, crossfading
        /// between, so the ship leaves and joins its parking slots exactly and stays out of both
        /// drawn bodies. In open space every correction has eased out, so between worlds the ship
        /// flies its true path as the scale model draws it.
        /// </summary>
        public DVec3 TransitScene(Ephemeris ephemeris, DVec3[] scene, Ship ship, double progress, out DVec3 travelDirection)
        {
            var catalog = ephemeris.Catalog;
            var originLast = new List<int>();
            AddChain(catalog, ship.Orbit.Body, originLast);
            AddChain(catalog, ship.OriginBody, originLast);
            MoveLast(originLast, ship.OriginBody);
            var destinationLast = new List<int>();
            AddChain(catalog, ship.OriginBody, destinationLast);
            AddChain(catalog, ship.Orbit.Body, destinationLast);
            MoveLast(destinationLast, ship.Orbit.Body);

            DVec3 At(double u)
            {
                var helio = ephemeris.Helio(ship.FrameBody) + Fleet.TransitOffset(ship, u);
                var leaving = MapThrough(ephemeris, scene, originLast, originLast.Count, helio);
                var arriving = MapThrough(ephemeris, scene, destinationLast, destinationLast.Count, helio);
                return DVec3.Lerp(leaving, arriving, Astro.SmoothStep(0, 1, u));
            }

            var here = At(progress);
            const double step = 1e-4;
            var ahead = progress + step <= 1 ? At(progress + step) - here : here - At(progress - step);
            travelDirection = ahead.LengthSquared > 0 ? ahead.Normalized : DVec3.UnitX;
            return here;
        }

        /// <summary>The end body's correction must be applied last, even when it is an ancestor of the other end's.</summary>
        static void MoveLast(List<int> chain, int body)
        {
            if (body <= 0 || !chain.Remove(body)) return;
            chain.Add(body);
        }

        /// <summary>
        /// Scene position of a ship riding a hyperlane: the lane's curve drawn between its two ramps as
        /// the scale model places them (each in its planet's neighbourhood), so the ship meets the
        /// burns at either end exactly. The Sun keep-out and bow are scaled into scene units.
        /// </summary>
        public DVec3 LaneScene(Ephemeris ephemeris, DVec3[] scene, Hyperlanes lanes, Ship ship, double progress, out DVec3 travelDirection)
        {
            var hop = ship.LaneLeg;
            var aKm = ephemeris.Helio(hop.From) + ship.LaneFromOffset;
            var bKm = ephemeris.Helio(hop.To) + ship.LaneToOffset;
            return LaneCurveScene(ephemeris, scene, lanes, hop.Lane, hop.From, aKm, hop.To, bKm, progress, out travelDirection, ship.LaneStar, ship.LaneShape);
        }

        /// <summary>
        /// A point on a lane drawn in scene space between ramp points given in true km (each mapped in
        /// its planet's neighbourhood), bowed from the lane's hub and swung round the same star as the
        /// true-km curve, with the keep-out scaled into scene units by that star's own mapping.
        /// </summary>
        public DVec3 LaneCurveScene(Ephemeris ephemeris, DVec3[] scene, Hyperlanes lanes, Lane lane, int from, DVec3 aKm, int to, DVec3 bKm,
            double progress, out DVec3 travelDirection, int star = -1, int shape = -1)
        {
            var a = MapNear(ephemeris, scene, from, aKm);
            var b = MapNear(ephemeris, scene, to, bKm);
            if (star < 0) star = lanes.KeepOutStar(aKm, bKm, ephemeris.Jd);
            if (shape < 0) shape = lanes.ShapeOf(lane, aKm, bKm, star, ephemeris.Jd);
            double keepOut = LocalDistance(ephemeris.Catalog[star], lanes.KeepOutKm(star));
            var up = DVec3.UnitY;
            travelDirection = Hyperlanes.CurveTangent(a, b, lane.Bend, scene[lane.Hub], scene[star], keepOut, progress, up, shape);
            return Hyperlanes.Curve(a, b, lane.Bend, scene[lane.Hub], scene[star], keepOut, progress, up, shape);
        }

        /// <summary>Scene radius of a body's sphere of influence, used for camera reference frames.</summary>
        public double SceneSoi(BodyDefinition body) =>
            double.IsInfinity(body.SoiKm) ? double.PositiveInfinity : LocalDistance(body, body.SoiKm);
    }
}
