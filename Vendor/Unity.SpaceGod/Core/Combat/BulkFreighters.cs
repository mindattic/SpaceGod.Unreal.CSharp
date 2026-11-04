using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The bulk freighter as the roster has it (U.SPC-T482, docs/SHIP_ROSTER.md): a skeletal truss with its engine at one
    /// end and standard modules clamped along it, cargo pods and fuel spheres, built in space and never landing on a world.
    /// <list type="bullet">
    /// <item>Its hold is its pods. Each <see cref="FacilityKinds.CargoPod"/> is a facility of one section carrying
    /// <see cref="PodTonnes"/> of trade cargo and one row of the grid hold, so its capacity is the pods it carries
    /// (<see cref="Loadout.CapacityTonnes"/>); a yard clamps more pods into its free space or takes them off. The stock
    /// design's <see cref="StockPods"/> carry what its holds did before (<see cref="Economy.CapacityOf"/>).</item>
    /// <item>Its tanks are its spheres: each <see cref="FacilityKinds.FuelSphere"/> holds <see cref="SphereTonnes"/> of
    /// reaction mass, so trading a sphere for a pod is trading range for cargo.</item>
    /// <item>No armour and no weapons: every kilogram goes to cargo. Its design has no gun mount, launcher or mine layer, and
    /// a yard will fit it neither plate nor racks (<see cref="FacilityLayouts.Bars"/>). What guards it is the guns of
    /// others: the fighter wings merchants hire in port (<see cref="MerchantEscorts"/>) and a navy's supply-convoy escorts.</item>
    /// <item>It rides the lanes cheaply: its lane toll is <see cref="LaneTollShare"/> of a ship of its mass
    /// (<see cref="Economy.LaneToll(ShipClass, double)"/>), and off the lanes its drive is weak, so it is slow to speed up
    /// and slow down (its class's low acceleration and its design's slow battle drive).</item>
    /// <item>It never lands: at a ringed world it trades from orbit instead of queueing to go down to the surface
    /// (<see cref="NeverLands"/>).</item>
    /// </list>
    /// Another class is made a truss with <see cref="RegisterTruss"/>.
    /// </summary>
    [SettingsSource]
    public static class BulkFreighters
    {
        /// <summary>Trade cargo one pod carries, tonnes: the stock design's pods carry what a freighter's holds did.</summary>
        public const double PodTonnes = 2500;

        /// <summary>Pods on the stock design: 8 x 2,500 t = 20,000 t, a freighter's hold as before (the economy's throughput unchanged).</summary>
        public const int StockPods = 8;

        /// <summary>Rows of the grid hold each pod gives (a freighter's hold is six cells wide, one row a pod).</summary>
        public const int PodRows = 1;

        /// <summary>Reaction mass one fuel sphere holds, tonnes: the stock design's two hold a freighter's tanks as before.</summary>
        public const double SphereTonnes = 3000;

        public const int StockSpheres = 2;

        /// <summary>An empty pod's and an empty sphere's own mass, tonnes.</summary>
        public const double PodMassTonnes = 150, SphereMassTonnes = 90;

        static readonly HashSet<ShipClass> trusses = new HashSet<ShipClass> { ShipClass.Freighter };

        /// <summary>The classes built as a truss: the ones a pod or a sphere may clamp to.</summary>
        public static ICollection<ShipClass> TrussClasses => trusses;

        /// <summary>Makes every hull of <paramref name="cls"/> a truss (or, with <paramref name="truss"/> false, not one).</summary>
        public static void RegisterTruss(ShipClass cls, bool truss = true)
        {
            if (truss) trusses.Add(cls);
            else trusses.Remove(cls);
        }

        public static bool IsTruss(ShipClass cls) => trusses.Contains(cls);

        /// <summary>Whether a ship is never taken down to a world's surface: a truss is built in space and stays there.</summary>
        public static bool NeverLands(Ship s) => s?.Spec != null && IsTruss(s.Spec.Class);

        public static readonly RangeSetting LaneTollShare = new RangeSetting("economy.freighterLaneToll", SettingGroups.Economy,
            "Bulk freighter lane toll", 0.5, 0.1, 1, 0.05,
            "The share of a hyperlane's toll a bulk freighter pays against any other ship of its mass: freighters are built to ride " +
            "the lanes rather than burn their own fuel, and the lanes' keepers price them as bulk traffic.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "freighter truss lane toll hyperlane bulk cargo");

        /// <summary>The share of a lane's toll a ship of <paramref name="cls"/> pays.</summary>
        public static double LaneTollShareOf(ShipClass cls) => IsTruss(cls) ? LaneTollShare.Value : 1;

        /// <summary>
        /// The stock freighter's facilities, bow to stern: the cockpit, the pod gantry down the spine (the hold boarders
        /// break into: the walkway the pods' hatches open on, since nobody walks into a pod itself), the pods clamped along
        /// the truss, the spheres astern of them and the engine room at the drive.
        /// </summary>
        public static FacilitySite[] StockLayout()
        {
            var list = new List<FacilitySite>
            {
                new FacilitySite(FacilityKinds.Bridge, "Cockpit", 0.43),
                new FacilitySite(FacilityKinds.Hold, "Pod gantry", 0.05),
            };
            for (int i = 0; i < StockPods; i++)
                list.Add(new FacilitySite(FacilityKinds.CargoPod, "Cargo pod " + (i + 1), PodZ(i, StockPods)));
            for (int i = 0; i < StockSpheres; i++)
                list.Add(new FacilitySite(FacilityKinds.FuelSphere, "Fuel sphere " + (i + 1), -0.3));
            list.Add(new FacilitySite(FacilityKinds.Engineering, "Engine room", -0.42));
            // Its few hands' security post off the gantry (marines live in a habitat, U.SPC-T465).
            list.Add(new FacilitySite(FacilityKinds.MarineHabitat, "Security post", 0.05, -0.05, 1, 4));
            return list.ToArray();
        }

        /// <summary>Where along the truss (ship lengths, nose +0.5) the clamp for pod <paramref name="i"/> of
        /// <paramref name="ofPods"/> lies: in pairs, one each side of the spine, from behind the cockpit back to the spheres.</summary>
        public static double PodZ(int i, int ofPods)
        {
            int rows = Math.Max(1, (ofPods + 1) / 2);
            const double front = 0.3, back = -0.2;
            return rows == 1 ? (front + back) / 2 : front - (front - back) * (i / 2) / (rows - 1);
        }

        /// <summary>The cargo pods clamped to a ship's truss (its loadout's, or its design's).</summary>
        public static int Pods(Ship s) => Count(s, FacilityKinds.CargoPod);

        /// <summary>The fuel spheres clamped to a ship's truss.</summary>
        public static int Spheres(Ship s) => Count(s, FacilityKinds.FuelSphere);

        static int Count(Ship s, string kind)
        {
            if (s?.Spec == null) return 0;
            if (s.Loadout != null) return s.Loadout.CountFacility(kind);
            int n = 0;
            foreach (var f in FacilityLayouts.For(s.Spec.Class))
                if (string.Equals(f.Kind, kind, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        /// <summary>How many of its pods are full, as its model shows them: what it carries against what its pods hold,
        /// rounded up (any load at all fills a pod).</summary>
        public static int LoadedPods(Ship s)
        {
            int pods = Pods(s);
            if (pods == 0 || s.CapacityTonnes <= 0) return 0;
            double share = Math.Max(0, Math.Min(1, s.CarriedTonnes / s.CapacityTonnes));
            return share <= 0 ? 0 : Math.Max(1, Math.Min(pods, (int)Math.Ceiling(share * pods - 1e-9)));
        }
    }
}
