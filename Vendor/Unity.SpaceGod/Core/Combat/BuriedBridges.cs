using System;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A warship's bridge buried at the centre of its hull (U.SPC-T492). It has no viewports, so nothing of it shows on the
    /// outside (the crew sees out through the ship's sensors and holo displays), and it is protected by the hull's full
    /// thickness: a shot aimed at it spends itself on the hull around it, and only once enough of the hull is shot away does
    /// fire get through to it (deep penetration). Collisions never reach it, and no pod, Breacher round or airlock opens into
    /// it, so boarders take it only by fighting through the spaces around it. A design buries a hardpoint with
    /// <see cref="SubsystemDef.Buried"/> and a space with <see cref="FacilitySite.Buried"/>; this class holds the figures.
    /// </summary>
    public static class BuriedBridges
    {
        public static readonly RangeSetting Depth = new RangeSetting("broadside.bridgeDepth", SettingGroups.Broadsides,
            "Buried bridge depth", 0.5, 0.1, 1, 0.05,
            "How much of a warship's hull must be shot away before fire reaches its bridge, buried at the centre of the hull; " +
            "past that, more of each hit aimed at it gets through the deeper the hull is holed, all of it by the time the hull is gone.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "bridge buried armour depth penetration capital warship");

        /// <summary>The share of a hit aimed at a buried hardpoint that reaches it through what is left of the hull: none while
        /// more than <see cref="Depth"/> of the hull stands, rising to all of it as the hull is shot away. 1 for anything not
        /// buried.</summary>
        public static double Reach(ShipCombat c, Subsystem s)
        {
            if (s == null || !s.Def.Buried) return 1;
            if (c == null) return 0;
            double depth = Math.Max(1e-6, Depth.Value);
            double holed = 1 - c.HullFraction;
            return Math.Max(0, Math.Min(1, (holed - (1 - depth)) / depth));
        }
    }
}
