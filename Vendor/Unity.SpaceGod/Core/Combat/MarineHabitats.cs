using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Marine habitats (U.SPC-T465): where a ship's marines live and train. A habitat is a facility
    /// (<see cref="FacilityKinds.MarineHabitat"/>) fitted into a hull's internal space, and a hull's marine complement is
    /// what its habitats quarter (<see cref="FacilityLayouts.Complement(IEnumerable{FacilitySite}, double)"/>), not a flat
    /// figure per class: each stock design is drawn with the habitats that hold the marines it always had, and a yard fits
    /// more (fifty berths per unit of hull scale, <see cref="BerthsPerScale"/>) or strips them out for something else.
    /// Only hulls big enough for one may carry it (<see cref="FacilityKinds.MarineHulls"/>); fighters, small craft and
    /// satellites repel boarders with their crew alone. The habitat is the barracks compartment
    /// (<see cref="CompartmentKind.Barracks"/>) defenders muster from when the hull is boarded, and drilling in it raises
    /// the marines' quality day by day (<see cref="Interior.Drill"/>, <see cref="Factor"/>), repelling boarders and
    /// boarding alike. What a habitat has ready (<see cref="Ready(Interior)"/>, <see cref="Draw"/>) is what a hull's
    /// assault teams are drawn from. The tunables are in the settings menu's Broadsides group.
    /// </summary>
    [SettingsSource]
    public static class MarineHabitats
    {
        /// <summary>Marines one fitted habitat quarters per unit of hull scale (<see cref="Loadout.Scale"/>): fifty on a destroyer.</summary>
        public const double BerthsPerScale = 50;

        public static readonly RangeSetting DrillDays = new RangeSetting("war.marineDrillDays", SettingGroups.Broadsides,
            "Marine drill time", 180, 10, 720, 10,
            "How many days raw marines drill in their ship's marine habitat to be fully trained. Recruits who make up losses " +
            "start raw and bring the complement's training down with them.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + " days", "marines habitat barracks training drill boarding");

        public static readonly RangeSetting DrillBonus = new RangeSetting("war.marineDrillBonus", SettingGroups.Broadsides,
            "Trained marine edge", 0.3, 0, 1, 0.05,
            "How much better, man for man, fully trained marines fight than raw ones, both repelling boarders and boarding " +
            "from their ship.",
            v => "+" + (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "marines habitat barracks training drill boarding quality");

        /// <summary>The facility kind.</summary>
        public static FacilityKind Kind => FacilityKinds.Of(FacilityKinds.MarineHabitat);

        /// <summary>What marines drilled to <paramref name="training"/> (0 to 1) multiply their quality by.</summary>
        public static double Factor(double training) => 1 + DrillBonus.Value * Math.Max(0, Math.Min(1, training));

        /// <summary>The marines this loadout's facilities quarter: its marine complement.</summary>
        public static double Complement(Loadout l) => l == null ? 0 : FacilityLayouts.Complement(l.Facilities, l.Scale);

        /// <summary>Habitats in the hull.</summary>
        public static int Count(Loadout l) => l?.CountFacility(FacilityKinds.MarineHabitat) ?? 0;

        /// <summary>Whether a hull of this frame could ever carry marines in a habitat (it has one, or room and leave to fit one).</summary>
        public static bool CanCarry(HullFrame frame)
        {
            if (frame == null) return false;
            foreach (var s in frame.Facilities) if (s.Kind == FacilityKinds.MarineHabitat) return true;
            return frame.CanCarry(Kind);
        }

        /// <summary>
        /// Marines in the habitats' barracks ready to be sent out (to board, or as an assault team): none while the hull
        /// is itself being boarded, when every one is needed at home.
        /// </summary>
        public static double Ready(Interior interior)
        {
            if (interior == null || interior.UnderBoarding || interior.Captured) return 0;
            double n = 0;
            foreach (var c in interior.Barracks) n += c.Marines;
            return n;
        }

        /// <summary>Marines the ship's habitats have ready.</summary>
        public static double Ready(Ship ship) => Ready(ship?.Combat?.Interior);

        /// <summary>Whole teams of <paramref name="teamSize"/> the habitats have ready.</summary>
        public static int TeamsReady(Interior interior, double teamSize) => teamSize <= 0 ? 0 : (int)Math.Floor(Ready(interior) / teamSize + 1e-9);

        /// <summary>
        /// Takes up to <paramref name="marines"/> ready marines out of the habitats' barracks, evenly from each (an assault
        /// team leaving); returns how many went. Nothing is drawn while the hull is being boarded.
        /// </summary>
        public static double Draw(Interior interior, double marines)
        {
            double have = Ready(interior);
            if (have <= 0 || marines <= 0) return 0;
            double share = Math.Min(1, marines / have);
            foreach (var c in interior.Barracks) c.Marines -= c.Marines * share;
            return have * share;
        }
    }
}
