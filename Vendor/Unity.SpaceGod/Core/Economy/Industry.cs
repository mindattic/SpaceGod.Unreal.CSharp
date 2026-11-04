using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    public enum IndustryKind
    {
        Mine,
        Farm,
        PharmaLab,
        Smelter,
        ElectronicsFab,
        MachineWorks,
        MunitionsPlant,
        Shipyard,
    }

    /// <summary>A production recipe: tonnes per day at level 1. Mines take their output from the site.</summary>
    public sealed class Recipe
    {
        public IndustryKind Kind { get; }
        public string Name { get; }
        public (Good good, double perDay)[] Inputs { get; }
        public (Good good, double perDay)[] Outputs { get; }

        /// <summary>
        /// Per input: true if it is a bulk feedstock that caps output outright; false for a consumable
        /// (fuel, spares) whose shortage only cuts efficiency, down to <see cref="MinorFloor"/>.
        /// </summary>
        public bool[] Limiting { get; }

        public const double MinorFloor = 0.55;

        /// <summary>Workers needed to run one level at full capacity (see Economy/SOURCES.md).</summary>
        public double WorkersPerLevel { get; }

        public Recipe(IndustryKind kind, string name, (Good, double)[] inputs, (Good, double)[] outputs, double workersPerLevel = 0)
        {
            Kind = kind;
            WorkersPerLevel = workersPerLevel;
            Name = name;
            Inputs = inputs;
            Outputs = outputs;
            double total = 0;
            foreach (var (_, rate) in inputs) total += rate;
            Limiting = new bool[inputs.Length];
            for (int i = 0; i < inputs.Length; i++) Limiting[i] = kind != IndustryKind.Mine && inputs[i].Item2 >= total * 0.02;
        }

        static readonly Dictionary<IndustryKind, Recipe> Fixed = new Dictionary<IndustryKind, Recipe>
        {
            [IndustryKind.Farm] = new Recipe(IndustryKind.Farm, "Hydroponic farms",
                new[] { (Good.Water, 700.0), (Good.Nitrogen, 60.0), (Good.HeliumThree, 0.4) },
                new[] { (Good.Food, 420.0) }, 1440),
            [IndustryKind.PharmaLab] = new Recipe(IndustryKind.PharmaLab, "Pharmaceutical labs",
                new[] { (Good.Water, 60.0), (Good.Hydrocarbons, 50.0), (Good.RareEarths, 0.6) },
                new[] { (Good.MedicalSupplies, 22.0) }, 600),
            [IndustryKind.Smelter] = new Recipe(IndustryKind.Smelter, "Smelters",
                new[] { (Good.NickelIron, 2200.0), (Good.Titanium, 90.0), (Good.HeliumThree, 0.8) },
                new[] { (Good.Alloys, 800.0) }, 930),
            [IndustryKind.ElectronicsFab] = new Recipe(IndustryKind.ElectronicsFab, "Electronics fabs",
                new[] { (Good.Copper, 160.0), (Good.Zinc, 40.0), (Good.RareEarths, 9.0), (Good.Platinum, 1.2), (Good.Alloys, 25.0) },
                new[] { (Good.Electronics, 55.0) }, 2500),
            [IndustryKind.MachineWorks] = new Recipe(IndustryKind.MachineWorks, "Machine works",
                new[] { (Good.Alloys, 280.0), (Good.Electronics, 18.0), (Good.Copper, 40.0) },
                new[] { (Good.Machinery, 190.0) }, 1200),
            [IndustryKind.MunitionsPlant] = new Recipe(IndustryKind.MunitionsPlant, "Munitions plants",
                new[] { (Good.Alloys, 90.0), (Good.Hydrocarbons, 60.0), (Good.Electronics, 8.0) },
                new[] { (Good.Munitions, 55.0) }, 900),
            // A yard turns materials into build points (1 per tonne of alloys) rather than a good.
            [IndustryKind.Shipyard] = new Recipe(IndustryKind.Shipyard, "Shipyard",
                new[] { (Good.Alloys, 300.0), (Good.Electronics, 25.0), (Good.Machinery, 40.0) },
                new (Good, double)[0], 4000),
        };

        public static Recipe Of(IndustryKind kind) => Fixed[kind];

        public static Recipe Mine(Good ore, double perDay) =>
            new Recipe(IndustryKind.Mine, GoodInfo.Of(ore).Name + " extraction",
                new[] { (Good.Machinery, perDay * 0.0012), (Good.HeliumThree, perDay * 0.00008) },
                new[] { (ore, perDay) }, perDay * MineWorkersPerTonneDay(ore));

        /// <summary>
        /// Miners per tonne a day of output. Bulk ice, ore and gas: about 12,000 t per worker-year, the
        /// productivity of US coal mining (EIA). Concentrated metals take ten times the labour per tonne and
        /// the trace products (platinum-group metals, rare earths, helium-3 sifted from regolith) a thousand
        /// times: both assumed, scaled to how lean the deposits are.
        /// </summary>
        public static double MineWorkersPerTonneDay(Good ore)
        {
            const double bulk = 365.0 / 12000;
            switch (ore)
            {
                case Good.Copper: case Good.Zinc: case Good.Titanium: return bulk * 10;
                case Good.Platinum: case Good.RareEarths: case Good.HeliumThree: return bulk * 1000;
                default: return bulk;
            }
        }
    }

    /// <summary>One industry at a settlement. Level scales the recipe; it drifts with profitability.</summary>
    public sealed class Facility
    {
        public Recipe Recipe;
        public double Level;
        public double BaseLevel;

        /// <summary>Fraction of capacity achieved over the last tick (inputs, crew or storage permitting).</summary>
        public double Utilisation;

        /// <summary>Share of capacity a shock leaves working (an accident in a mine, a blight in the farms); 1 normally.</summary>
        public double Impairment = 1;

        /// <summary>For a mine: tonnes taken from the current seam, and what it held (0 until <see cref="MarketShocks"/> surveys it).</summary>
        public double Extracted, Reserve;

        /// <summary>Share of its crew it had over the last tick (see <see cref="People.AssignLabour"/>).</summary>
        public double Staffing = 1;

        /// <summary>Workers it needs at its current level.</summary>
        public double Workers => Recipe.WorkersPerLevel * Level;

        /// <summary>Farms, and water and nitrogen (air) extraction: kept running through a capture, and crewed first.</summary>
        public bool LifeSupport => Recipe.Kind == IndustryKind.Farm || (Recipe.Kind == IndustryKind.Mine && (Recipe.Outputs[0].good == Good.Water || Recipe.Outputs[0].good == Good.Nitrogen));

        public Facility(Recipe recipe, double level)
        {
            Recipe = recipe;
            Level = BaseLevel = level;
        }
    }
}
