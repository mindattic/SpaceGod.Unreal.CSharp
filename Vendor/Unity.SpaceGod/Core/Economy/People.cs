using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The people side of the economy: what a person uses a day, how many of them work, who staffs
    /// which facility, what they earn and pay in tax, and where they move to. Every figure is listed,
    /// with its source or the reason it is assumed, in Economy/SOURCES.md.
    /// </summary>
    public static class People
    {
        // ---------------------------------------------------------------- needs

        /// <summary>Food eaten per person a day, kg (BVAD ISS planning value 2.39 kg packaged, less 0.43 kg packaging and 0.20 kg never opened; Ewert and Stromgren 2019).</summary>
        public const double FoodKg = 1.76;

        /// <summary>Metabolic oxygen per person a day, kg (Ewert and Stromgren 2019, 82 kg astronaut exercising 90 minutes a day).</summary>
        public const double OxygenKg = 0.89;

        /// <summary>Water a day per person the Sabatier reactor gets back from exhaled CO2, kg (Ewert and Stromgren 2019).</summary>
        public const double SabatierWaterKg = 0.47;

        /// <summary>Cabin air lost to leaks per person a day, kg: the ISS design allowance of 0.23 kg/day spread over its crew of six.</summary>
        public const double LeakAirKg = 0.23 / 6;

        /// <summary>Nitrogen's share of breathing air by mass (dry air: 75.5% N2, 23.1% O2).</summary>
        public const double AirNitrogenShare = 0.755, AirOxygenShare = 0.231;

        /// <summary>Medical supplies per person a day, kg: between the clinical share (~7 g) and the whole (~47 g) of US hospital waste per head.</summary>
        public const double MedicalKg = 0.020;

        /// <summary>Water each person draws from the loop a day, kg: the ISS-like habitat budget (5.03 kg, Ewert and Stromgren 2019), or the UN's 50 litres for basic domestic needs in a colony with showers and laundry.</summary>
        public static double GrossWaterKg(SettlementKind kind) => kind == SettlementKind.Colony ? 50 : 5.03;

        /// <summary>Share of that water the loop recovers: 98% with the ISS brine processor (2023), 93.5% before it; a haven's patched kit is assumed to manage 85%.</summary>
        public static double WaterRecovery(SettlementKind kind)
        {
            switch (kind)
            {
                case SettlementKind.Station: return 0.98;
                case SettlementKind.Haven: return 0.85;
                default: return 0.935;
            }
        }

        /// <summary>
        /// Water lost per person a day, kg: what the loop does not recover, plus the water electrolysed
        /// for oxygen (metabolic need and leaked air, 9 kg of water per 8 kg of O2) less what the Sabatier
        /// reactor returns.
        /// </summary>
        public static double WaterLossKg(SettlementKind kind)
        {
            double oxygen = OxygenKg + LeakAirKg * AirOxygenShare;
            return GrossWaterKg(kind) * (1 - WaterRecovery(kind)) + Math.Max(0, oxygen * 9.0 / 8.0 - SabatierWaterKg);
        }

        /// <summary>Nitrogen bled in a day per person to replace leaked air, kg.</summary>
        public const double NitrogenKg = LeakAirKg * AirNitrogenShare;

        static readonly (Good good, double perPersonDay)[][] needs = BuildNeeds();

        static (Good good, double perPersonDay)[][] BuildNeeds()
        {
            var all = new (Good, double)[Enum.GetValues(typeof(SettlementKind)).Length][];
            foreach (SettlementKind kind in Enum.GetValues(typeof(SettlementKind)))
                all[(int)kind] = new[]
                {
                    (Good.Food, FoodKg / 1000),
                    (Good.Water, WaterLossKg(kind) / 1000),
                    (Good.Nitrogen, NitrogenKg / 1000),
                    (Good.MedicalSupplies, MedicalKg / 1000),
                    // The habitat's own upkeep (fusion fuel, spares, controls), shared per head: assumed.
                    (Good.HeliumThree, 0.0000025),
                    (Good.Machinery, 0.000008),
                    (Good.Electronics, 0.0000015),
                };
            return all;
        }

        /// <summary>Daily per-person needs in tonnes for a kind of settlement (built once, read without locking).</summary>
        public static (Good good, double perPersonDay)[] NeedsOf(SettlementKind kind) => needs[(int)kind];

        /// <summary>A person's daily need of <paramref name="g"/> at a kind of settlement, tonnes (0 if it is not a need).</summary>
        public static double NeedOf(SettlementKind kind, Good g)
        {
            foreach (var (good, perDay) in NeedsOf(kind))
                if (good == g) return perDay;
            return 0;
        }

        // ---------------------------------------------------------------- growth

        /// <summary>Natural increase of a fed, watered and doctored settlement, per day (4%/yr, a frontier boom a little above today's fastest-growing countries).</summary>
        public const double GrowthPerDay = 0.00011;

        /// <summary>Extra decline per day with no medicine at all.</summary>
        public const double SicknessPerDay = 0.0002;

        /// <summary>Decline per day per unit of unmet food or water below the 98% line.</summary>
        public const double FaminePerDay = 0.004;

        /// <summary>A settlement never shrinks below this share of its founding population (a hard core stays), nor grows past <see cref="MaxGrowth"/> times it.</summary>
        public const double MinShare = 0.25, MaxGrowth = 4;

        /// <summary>Population growth rate per day from how well needs were met this tick.</summary>
        public static double GrowthRate(double sustenance, double health) =>
            sustenance >= 0.98
                ? GrowthPerDay * (0.5 + 0.5 * health) - (1 - health) * SicknessPerDay
                : (sustenance - 0.98) * FaminePerDay;

        /// <summary>Days over which <see cref="Settlement.Wellbeing"/> follows what people actually got.</summary>
        public const double WellbeingDays = 30;

        // ---------------------------------------------------------------- labour

        /// <summary>Share of everyone who is in the labour force (ILO: about 3.6 billion workers of 8.1 billion people, 2023).</summary>
        public const double LabourForceShare = 0.45;

        /// <summary>Share of the labour force free for the facilities; the rest run services (ILO: services are about half of world employment).</summary>
        public const double IndustrialShare = 0.5;

        /// <summary>A facility with less than this share of its crew cannot run at all.</summary>
        public const double MinimumCrew = 0.25;

        /// <summary>Share of a facility's value added paid as wages (ILO: the global labour income share is about 52%).</summary>
        public const double LabourShare = 0.52;

        /// <summary>Tax the owner takes on wages (OECD average tax wedge on labour, about 35%).</summary>
        public const double WageTaxRate = 0.35;

        /// <summary>A service job pays this share of the system's average facility wage (assumed).</summary>
        public const double ServiceWageShare = 0.5;

        /// <summary>Workers available to the facilities at a settlement.</summary>
        public static double Labour(Settlement s) => s.Workforce * IndustrialShare;

        /// <summary>
        /// Shares the labour out among the facilities: life support (farms and water) is crewed first,
        /// then everything else in proportion. Sets each facility's <see cref="Facility.Staffing"/> and the
        /// settlement's overall <see cref="Settlement.Staffing"/>.
        /// </summary>
        public static void AssignLabour(Settlement s)
        {
            double labour = Labour(s), lifeJobs = 0, otherJobs = 0;
            foreach (var f in s.Facilities)
                if (f.LifeSupport) lifeJobs += f.Workers;
                else otherJobs += f.Workers;
            double life = lifeJobs > 0 ? Math.Min(1, labour / lifeJobs) : 1;
            double left = Math.Max(0, labour - lifeJobs);
            double other = otherJobs > 0 ? Math.Min(1, left / otherJobs) : 1;
            foreach (var f in s.Facilities) f.Staffing = f.LifeSupport ? life : other;
            double jobs = lifeJobs + otherJobs;
            s.Staffing = jobs > 0 ? Math.Min(1, labour / jobs) : 1;
        }

        /// <summary>How much of its capacity a facility can run with its crew: in proportion, and not at all below <see cref="MinimumCrew"/>.</summary>
        public static double CrewFactor(double staffing) => staffing < MinimumCrew ? 0 : Math.Min(1, staffing);

        /// <summary>Value added a day by a facility at local prices at full capacity: outputs less inputs (never negative).</summary>
        public static double ValueAdded(Settlement s, Facility f)
        {
            double v = 0;
            foreach (var (g, rate) in f.Recipe.Outputs) v += rate * s.Price[(int)g];
            foreach (var (g, rate) in f.Recipe.Inputs) v -= rate * s.Price[(int)g];
            // A yard's product is hulls, not a good: count its materials as worth half again once built.
            if (f.Recipe.Kind == IndustryKind.Shipyard) v = -v * 0.5;
            return Math.Max(0, v) * f.Level;
        }

        /// <summary>The facility wage a day per filled job at <paramref name="s"/> (0 with nobody at work).</summary>
        public static double Wage(Settlement s)
        {
            double employed = 0;
            foreach (var f in s.Facilities) employed += f.Workers * Math.Min(1, f.Staffing);
            return employed > 0 ? s.WageBill / employed : 0;
        }

        /// <summary>
        /// Expected daily earnings of a member of the labour force (Harris and Todaro 1970): the facility
        /// wage times the chance of holding a facility job, the rest at the service wage.
        /// </summary>
        public static double ExpectedEarnings(Settlement s, double serviceWage)
        {
            double labour = Labour(s);
            double employed = 0;
            foreach (var f in s.Facilities) employed += f.Workers * Math.Min(1, f.Staffing);
            double chance = labour > 0 ? Math.Min(1, employed / labour) : 0;
            return chance * Wage(s) + (1 - chance) * serviceWage;
        }

        // ---------------------------------------------------------------- migration

        /// <summary>Migration rate: the share of a population a day that moves for a full unit gap in attraction, before weighting by destination size.</summary>
        public const double MigrationPerDay = 0.002;

        /// <summary>The most people a settlement holds as <see cref="Settlement.Overflow"/>, as a share of its population: past it, those waiting give up.</summary>
        public const double OverflowShare = 0.05;

        /// <summary>A settlement past this share of its cap is crowded (see <see cref="Crowding"/>).</summary>
        public const double CrowdedShare = 0.6;

        /// <summary>
        /// How crowded <paramref name="s"/> is, 0..1: 0 up to <see cref="CrowdedShare"/> of its cap, rising to 1 at the
        /// cap. That share of its growth would rather leave than squeeze in, and is counted as <see cref="Settlement.Overflow"/>.
        /// </summary>
        public static double Crowding(Settlement s)
        {
            double cap = s.InitialPopulation * MaxGrowth;
            if (cap <= 0) return 0;
            return Math.Max(0, Math.Min(1, (s.Population / cap - CrowdedShare) / (1 - CrowdedShare)));
        }

        /// <summary>Counts <paramref name="people"/> more at <paramref name="s"/> with no room (U.SPC-T98), up to <see cref="OverflowShare"/> of its people.</summary>
        public static void AddOverflow(Settlement s, double people)
        {
            if (people <= 0 || s.Kind == SettlementKind.Haven) return;
            s.Overflow = Math.Min(s.Overflow + people, s.Population * OverflowShare);
        }

        /// <summary>Gaps in attraction smaller than this move nobody: the cost and bother of moving.</summary>
        public const double MigrationThreshold = 0.05;

        /// <summary>
        /// A place's pull on migrants: its living standard (wellbeing, with medicine) times how its
        /// expected earnings compare with the system's, damped so that hunger matters more than pay.
        /// </summary>
        public static double Attraction(Settlement s, double meanEarnings)
        {
            double living = s.Wellbeing * (0.6 + 0.4 * Math.Min(1, s.HealthWellbeing));
            double pay = meanEarnings > 0 && s.Earnings > 0 ? Math.Pow(s.Earnings / meanEarnings, 0.25) : 1;
            return living * Math.Max(0.8, Math.Min(1.25, pay));
        }

        /// <summary>Whether people can move between two settlements: neither empty, not at war, pirates only among pirates.</summary>
        public static bool CanMigrate(Diplomacy diplomacy, Settlement a, Settlement b)
        {
            if (a == b || a.Fallen || b.Fallen) return false;
            bool ah = a.Kind == SettlementKind.Haven, bh = b.Kind == SettlementKind.Haven;
            if (ah || bh) return ah && bh && a.Owner == b.Owner;
            return diplomacy == null || !diplomacy.Hostile(a.Owner, b.Owner);
        }

        /// <summary>
        /// Moves people over <paramref name="days"/> from where life is worse to where it is better:
        /// from each settlement toward every reachable one with a clearly higher pull, weighted by the
        /// destination's share of the reachable population (bigger places draw more). Nobody leaves a
        /// place below its floor or arrives at one already at its cap. Sets each settlement's
        /// <see cref="Settlement.Earnings"/>, <see cref="Settlement.Attraction"/> and
        /// <see cref="Settlement.NetMigration"/>.
        /// </summary>
        public static void Migrate(IList<Settlement> settlements, Diplomacy diplomacy, double days)
        {
            int n = settlements.Count;
            if (n == 0) return;

            // The service wage: half the system's average facility wage, weighted by who earns it.
            double wageSum = 0, workers = 0;
            foreach (var s in settlements)
            {
                if (s.Fallen) continue;
                double employed = 0;
                foreach (var f in s.Facilities) employed += f.Workers * Math.Min(1, f.Staffing);
                wageSum += s.WageBill;
                workers += employed;
            }
            double serviceWage = workers > 0 ? wageSum / workers * ServiceWageShare : 0;

            double earnSum = 0, people = 0;
            foreach (var s in settlements)
            {
                s.NetMigration = 0;
                if (s.Fallen || s.Kind == SettlementKind.Haven) { s.Earnings = 0; continue; }
                s.Earnings = ExpectedEarnings(s, serviceWage);
                earnSum += s.Earnings * s.Population;
                people += s.Population;
            }
            double meanEarnings = people > 0 ? earnSum / people : 0;
            foreach (var s in settlements) s.Attraction = s.Fallen ? 0 : Attraction(s, meanEarnings);
            if (days <= 0) return;

            var moves = new double[n, n];
            var leaving = new double[n];
            for (int i = 0; i < n; i++)
            {
                var from = settlements[i];
                if (from.Fallen || from.Population <= 0) continue;
                double reach = 0;
                for (int j = 0; j < n; j++)
                    if (CanMigrate(diplomacy, from, settlements[j])) reach += settlements[j].Population;
                if (reach <= 0) continue;
                double rate = 1 - Math.Exp(-MigrationPerDay * days);
                for (int j = 0; j < n; j++)
                {
                    var to = settlements[j];
                    if (!CanMigrate(diplomacy, from, to)) continue;
                    double gap = to.Attraction - from.Attraction - MigrationThreshold;
                    if (gap <= 0) continue;
                    double m = from.Population * rate * Math.Min(1, gap) * (to.Population / reach);
                    moves[i, j] = m;
                    leaving[i] += m;
                }
                // Those who already left on contract passage were part of this flow: that many fewer go on their own.
                if (from.Emigrated > 0 && leaving[i] > 0)
                {
                    double counted = Math.Min(from.Emigrated, leaving[i]);
                    from.Emigrated -= counted;
                    double k = (leaving[i] - counted) / leaving[i];
                    for (int j = 0; j < n; j++) moves[i, j] *= k;
                    leaving[i] -= counted;
                }
                // Nobody leaves below the floor.
                double spare = Math.Max(0, from.Population - from.InitialPopulation * MinShare);
                if (leaving[i] > spare)
                {
                    double k = spare / leaving[i];
                    for (int j = 0; j < n; j++) moves[i, j] *= k;
                }
            }
            for (int j = 0; j < n; j++)
            {
                var to = settlements[j];
                double arriving = 0;
                for (int i = 0; i < n; i++) arriving += moves[i, j];
                if (arriving <= 0) continue;
                // ... and nobody arrives past the cap: the rest stay home.
                double room = Math.Max(0, to.InitialPopulation * MaxGrowth - to.Population);
                if (arriving > room)
                {
                    double k = room / arriving;
                    for (int i = 0; i < n; i++)
                    {
                        // Those turned away still want to leave: people with nowhere to go (U.SPC-T98).
                        AddOverflow(settlements[i], moves[i, j] * (1 - k));
                        moves[i, j] *= k;
                    }
                }
            }
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    double m = moves[i, j];
                    if (m <= 0) continue;
                    settlements[i].Population -= m;
                    settlements[j].Population += m;
                    settlements[i].NetMigration -= m / days;
                    settlements[j].NetMigration += m / days;
                }
        }
    }
}
