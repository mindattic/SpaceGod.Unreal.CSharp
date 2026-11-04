using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The handful of bodies a Void Base ever has (U.SPC-T372, see <see cref="StarSystem.CreateVoidBase"/>): the
    /// artificial gravity/sun generator at its centre -- a "black hole in a bottle" (see
    /// <see cref="BodyDefinition.Singularity"/>), dark and light-swallowing rather than lit, giving the relay
    /// station around it an orbit and a "down" with no real star or planet anywhere near -- circled by a few tiny,
    /// perfectly ordinary suns that light the place without lighting what sits at its heart. Nothing else is ever
    /// added: nothing could really orbit a machine this light, and nothing is meant to settle here.
    /// </summary>
    public static class VoidBaseBuilder
    {
        /// <summary>The generator's own bulk: a compact machine, nothing like a star's scale.</summary>
        public const double RadiusKm = 50;

        /// <summary>Its catalogue gravitational parameter: just enough to be a body, not to hold anything in orbit for real.</summary>
        public const double Gm = 1e-6;

        /// <summary>How many tiny suns circle the generator.</summary>
        public const int TinySuns = 3;

        /// <summary>How far out they orbit, and how fast: close enough, and quick enough, to read at a glance as the
        /// pretty, misleadingly cheerful ring a lawless relay hides behind.</summary>
        public const double TinySunOrbitKm = RadiusKm * 12;
        public const double TinySunPeriodDays = 5;
        public const double TinySunRadiusKm = 4;
        public const double TinySunTeffK = 5800;

        /// <summary>How much of it the overview frames: just past the tiny suns' own orbit, there being nothing else out here to see.</summary>
        public const double OverviewSpanKm = TinySunOrbitKm * 2.5;

        public static BodyCatalog Build(string name)
        {
            var bodies = new List<BodyDefinition>
            {
                new BodyDefinition
                {
                    Name = name, Kind = BodyKind.Star, RadiusKm = RadiusKm, Gm = Gm, Singularity = true,
                    Status = BodyStatus.Fictional, Color = new Rgb(0.05f, 0.02f, 0.08f),
                    Summary = "The artificial gravity/sun generator at the heart of this relay: a \"black hole in a bottle\", "
                              + "drawn dark and light-swallowing rather than lit, with a tight violet glow standing in for an "
                              + "event horizon. No star, no planet, just the machine -- and the tiny suns it keeps circling it. Fiction.",
                },
            };
            for (int i = 0; i < TinySuns; i++)
            {
                double meanAnomalyDeg = 360.0 * i / TinySuns;
                bodies.Add(new BodyDefinition
                {
                    Name = name + " Ember " + (i + 1), Kind = BodyKind.Star, ParentName = name,
                    RadiusKm = TinySunRadiusKm, Gm = 1e-9,
                    Elements = OrbitalElements.FromVectors(Astro.J2000, TinySunOrbitKm, 0, DVec3.UnitX, DVec3.UnitZ, meanAnomalyDeg, TinySunPeriodDays),
                    TeffK = TinySunTeffK, LuminositySolar = 1e-6, Color = AlphaCentauri.StarColor(TinySunTeffK),
                    Status = BodyStatus.Fictional,
                    Summary = "One of the tiny suns circling the generator, bright and perfectly ordinary against the dark of the thing at their centre. Fiction.",
                });
            }
            return new BodyCatalog(bodies);
        }
    }
}
