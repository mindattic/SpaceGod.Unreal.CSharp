using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>Every traded commodity. Quantities are tonnes; prices are credits per tonne.</summary>
    public enum Good
    {
        Food,
        Water,
        MedicalSupplies,
        HeliumThree,
        NickelIron,
        Copper,
        Zinc,
        Platinum,
        RareEarths,
        Titanium,
        Hydrocarbons,
        Nitrogen,
        Alloys,
        Electronics,
        Machinery,
        Munitions,
    }

    public enum GoodCategory
    {
        Essential,
        Mineral,
        Volatile,
        Industrial,
        Military,
    }

    public sealed class GoodInfo
    {
        public Good Good { get; }
        public string Name { get; }
        public double BasePrice { get; }
        public GoodCategory Category { get; }

        GoodInfo(Good good, string name, double basePrice, GoodCategory category)
        {
            Good = good;
            Name = name;
            BasePrice = basePrice;
            Category = category;
        }

        public static readonly int Count = Enum.GetValues(typeof(Good)).Length;

        static readonly GoodInfo[] All =
        {
            new GoodInfo(Good.Food, "Food", 120, GoodCategory.Essential),
            new GoodInfo(Good.Water, "Water", 30, GoodCategory.Essential),
            new GoodInfo(Good.MedicalSupplies, "Medical supplies", 2500, GoodCategory.Essential),
            new GoodInfo(Good.HeliumThree, "Helium-3", 3000, GoodCategory.Volatile),
            new GoodInfo(Good.NickelIron, "Nickel-iron ore", 40, GoodCategory.Mineral),
            new GoodInfo(Good.Copper, "Copper", 300, GoodCategory.Mineral),
            new GoodInfo(Good.Zinc, "Zinc", 150, GoodCategory.Mineral),
            new GoodInfo(Good.Platinum, "Platinum-group metals", 20000, GoodCategory.Mineral),
            new GoodInfo(Good.RareEarths, "Rare earth elements", 4000, GoodCategory.Mineral),
            new GoodInfo(Good.Titanium, "Titanium", 600, GoodCategory.Mineral),
            new GoodInfo(Good.Hydrocarbons, "Hydrocarbons", 90, GoodCategory.Volatile),
            new GoodInfo(Good.Nitrogen, "Nitrogen", 40, GoodCategory.Volatile),
            new GoodInfo(Good.Alloys, "Refined alloys", 900, GoodCategory.Industrial),
            new GoodInfo(Good.Electronics, "Electronics", 6000, GoodCategory.Industrial),
            new GoodInfo(Good.Machinery, "Machinery", 3500, GoodCategory.Industrial),
            new GoodInfo(Good.Munitions, "Munitions", 5000, GoodCategory.Military),
        };

        public static GoodInfo Of(Good g) => All[(int)g];
    }
}
