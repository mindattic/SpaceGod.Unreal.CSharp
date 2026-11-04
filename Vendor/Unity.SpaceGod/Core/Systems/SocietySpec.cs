using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>Which of a settled system's powers a settlement or ship belongs to.</summary>
    public enum SocietyRole
    {
        /// <summary>The government, with the system's navy.</summary>
        Authority,

        /// <summary>The mining and industrial concern, with no warships of its own.</summary>
        Company,

        /// <summary>The raiders, at their haven.</summary>
        Raiders,
    }

    /// <summary>One of a settled system's factions (game layer).</summary>
    public sealed class FactionSpec
    {
        public string Name, Prefix;
        public Rgb Color;

        /// <summary>Treasury at the nearest frontier, credits; scaled with the system (<see cref="SocietySpec.Scale"/>).</summary>
        public double Treasury = 4e8;

        public double Marines = 1;

        public FactionSpec(string name, string prefix, Rgb color, double treasury = 4e8, double marines = 1)
        {
            Name = name;
            Prefix = prefix;
            Color = color;
            Treasury = treasury;
            Marines = marines;
        }
    }

    /// <summary>
    /// A settlement as the society plans it: where it is, whose, how many people (before the frontier
    /// scaling, see <see cref="SocietySpec.Scale"/>), what it digs and what it makes. Mines and the
    /// factories that feed the system are sized by <see cref="World.CreateSettled"/> to what the
    /// system uses; only the yards and munitions plants, which serve ships rather than people, are
    /// given a level here.
    /// </summary>
    public sealed class SettlementSpec
    {
        public string Name, Body;
        public SettlementKind Kind;
        public SocietyRole Owner;
        public double Population;
        public Good[] Mines = new Good[0];
        public IndustryKind[] Makes = new IndustryKind[0];

        /// <summary>Shipyard and munitions plant levels (at the nearest frontier; scaled like the people).</summary>
        public double Shipyard, Munitions;

        public bool Hidden;

        /// <summary>A lawless station with a black market (U.SPC-T94, see <see cref="Settlement.Lawless"/>).</summary>
        public bool Lawless;

        /// <summary>Why it is where it is (what makes the place plausible).</summary>
        public string Note;
    }

    /// <summary>
    /// A nearby system's society (U.SPC-T90): its powers, settlements, the invented stations they
    /// live on, its hyperlanes and the names of its ships. All of it is fiction, placed on the real
    /// bodies of the system's catalogue entry (or on stations in orbit where none will do).
    /// </summary>
    public sealed class SocietySpec
    {
        /// <summary>The system it settles (<see cref="SystemSpec.Name"/>).</summary>
        public string System;

        /// <summary>The system's distance from Sol, light-years.</summary>
        public double DistanceLy;

        public FactionSpec Authority, Company, Traders, Raiders;

        public readonly List<SettlementSpec> Settlements = new List<SettlementSpec>();

        /// <summary>The invented stations the settlements need, added to the system's bodies.</summary>
        public readonly List<StationSpec> Stations = new List<StationSpec>();

        /// <summary>Hides in the system's belts and discs where a pirate clan could one day found a haven (stations named in
        /// <see cref="Stations"/>, empty at the start; U.SPC-T94).</summary>
        public readonly List<string> HavenSites = new List<string>();

        public readonly List<(string a, string b, double bend)> Lanes = new List<(string, string, double)>();

        public string[] Freighters = new string[0], Haulers = new string[0], Couriers = new string[0];
        public string[] Yachts = new string[0], Prospectors = new string[0];

        /// <summary>Its deep-space explorers (U.SPC-T419): free survey hulls with an FTL drive of their own.</summary>
        public string[] Explorers = new string[0];
        public string[] RaiderNames;

        /// <summary>The navy's ships: leader first, then escorts (the first is a cruiser in a big system, a destroyer in a small one).</summary>
        public string[] Navy = new string[0];

        /// <summary>The companies: name, house mark, home port, flag, whether it is in the ore trade.</summary>
        public readonly List<(string name, string tag, string home, SocietyRole flag, bool mining)> Companies = new List<(string, string, string, SocietyRole, bool)>();

        public string Summary;

        /// <summary>How built up a system this far out is (<see cref="Frontier"/>).</summary>
        public double Scale => Frontier(DistanceLy);

        /// <summary>
        /// How thickly settled a system is at <paramref name="ly"/> from Sol, against Alpha Centauri (1 at its
        /// 4.34 ly): falling with the square of the distance, as the cost of the crossing and of everything
        /// shipped out does, so Barnard's Star at 6 ly is half as built up and the 12 ly frontier an eighth.
        /// </summary>
        public static double Frontier(double ly) => Math.Min(1, Math.Pow(AlphaCentauri.DistanceLy / Math.Max(ly, 1e-9), 2));

        /// <summary>Total people once scaled.</summary>
        public double Population
        {
            get
            {
                double p = 0;
                foreach (var s in Settlements) p += s.Population * Scale;
                return p;
            }
        }

        /// <summary>Adds the society's stations to the system's catalogue entry (once).</summary>
        public void AddStations(SystemSpec spec)
        {
            foreach (var st in Stations)
                if (!spec.Stations.Exists(x => x.Name == st.Name)) spec.Stations.Add(st);
        }

        public SettlementSpec Add(string name, string body, SettlementKind kind, SocietyRole owner, double population, string note,
            Good[] mines = null, IndustryKind[] makes = null, double shipyard = 0, double munitions = 0, bool hidden = false, bool lawless = false)
        {
            var s = new SettlementSpec
            {
                Name = name, Body = body, Kind = kind, Owner = owner, Population = population, Note = note,
                Mines = mines ?? new Good[0], Makes = makes ?? new IndustryKind[0], Shipyard = shipyard, Munitions = munitions, Hidden = hidden,
                Lawless = lawless,
            };
            Settlements.Add(s);
            return s;
        }

        public void Station(string name, string host, double aAu, double meanAnomalyDeg, string summary) =>
            Stations.Add(new StationSpec { Name = name, Host = host, AAu = aAu, MeanAnomalyDeg = meanAnomalyDeg, Summary = "Fiction. " + summary });

        public void Lane(string a, string b, double bend) => Lanes.Add((a, b, bend));

        /// <summary>A hide in a belt or disc, empty until a clan founds a haven there (U.SPC-T94): a station body of its own.</summary>
        public void HavenSite(string name, string host, double aAu, double meanAnomalyDeg, string summary)
        {
            Station(name, host, aAu, meanAnomalyDeg, summary);
            HavenSites.Add(name);
        }
    }

    public sealed partial class World
    {
        /// <summary>
        /// A nearby system settled by <paramref name="society"/> (U.SPC-T90): the same machinery as Sol and Alpha
        /// Centauri, sized to how far out it is. Every settlement grows most of its own food and water; the mines
        /// and factories are then sized so that the system as a whole makes a little more of every good than it
        /// uses (<see cref="BalanceSupply"/>), leaving the traders a living carrying the rest about.
        /// </summary>
        public static World CreateSettled(Ephemeris ephemeris, double jd, int seed, SocietySpec society, Polities polities = null)
        {
            double startJd = jd - 45;
            double scale = society.Scale;
            var w = new World(ephemeris, startJd, seed, polities);
            var fleet = w.Fleet;

            // Each power is an interstellar polity (U.SPC-T91): a frontier colony's own state, or one that already holds
            // other systems (the Sol Federation at Barnard's Star), which this system's holdings then join.
            Faction Make(FactionSpec f, PolityKind kind) => w.Polity(f.Name, f.Prefix, f.Color, kind, f.Treasury * scale, f.Marines);

            var authority = w.Sol = Make(society.Authority, PolityKind.State);
            authority.IsMilitary = true;
            var company = w.Belt = Make(society.Company, PolityKind.Company);
            w.Independents = Make(society.Traders, PolityKind.FreeTraders);
            w.Pirates = society.Raiders != null ? Make(society.Raiders, PolityKind.PirateClan) : w.UnknownRaiders();
            // Rivals, not enemies: the hook for a war later.
            w.RivalA = authority;
            w.RivalB = company;
            Faction Of(SocietyRole r) => r == SocietyRole.Authority ? authority : r == SocietyRole.Company ? company : w.Pirates;

            // ---- settlements
            var e = w.Economy;
            Settlement haven = null;
            foreach (var spec in society.Settlements)
            {
                var s = e.Add(spec.Name, w.Catalog[spec.Body].Index, spec.Kind, Of(spec.Owner), spec.Population * scale);
                s.Hidden = spec.Hidden;
                s.Lawless = spec.Lawless;
                if (spec.Kind == SettlementKind.Haven)
                {
                    haven ??= s;
                    continue;
                }
                // A token of each: BalanceSupply sizes them to the system's needs.
                foreach (var g in spec.Mines) s.Facilities.Add(new Facility(Recipe.Mine(g, 1), 1));
                foreach (var k in spec.Makes) s.Facilities.Add(new Facility(Recipe.Of(k), 0.01));
                if (spec.Shipyard > 0) s.Facilities.Add(new Facility(Recipe.Of(IndustryKind.Shipyard), spec.Shipyard * scale));
                if (spec.Munitions > 0) s.Facilities.Add(new Facility(Recipe.Of(IndustryKind.MunitionsPlant), spec.Munitions * scale));
            }
            void Mine(Settlement s, Good g, double perDay) => s.Facilities.Add(new Facility(Recipe.Mine(g, perDay), 1));
            void Mk(Settlement s, IndustryKind k, double level) => s.Facilities.Add(new Facility(Recipe.Of(k), level));
            foreach (var s in e.Settlements) Subsist(s, Mine, Mk);
            BalanceSupply(e.Settlements, society.System);
            foreach (var s in e.Settlements)
            {
                s.MaxPlatforms = s.Kind == SettlementKind.Haven ? 1 : s.Population >= 100e3 ? 2 : 1;
                for (int i = 0; i < s.MaxPlatforms; i++) s.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            }

            // ---- the fleet's numbers, from how many people it serves
            double people = 0;
            foreach (var s in e.Settlements) people += s.Population;
            int Count(double perShip, int min, string[] names) => Math.Min(names.Length, Math.Max(min, (int)Math.Round(people / perShip)));
            int freighters = Count(25e3, 3, society.Freighters), haulers = Count(70e3, 1, society.Haulers), couriers = Count(120e3, 1, society.Couriers);
            int merchants = freighters + haulers + couriers;
            w.MerchantTarget = (int)Math.Ceiling(merchants * 1.5);
            w.MerchantCap = merchants * 2;
            int navy = Math.Min(society.Navy.Length, people >= 100e3 ? 3 : 2);
            w.NavyCap = navy + 1;
            w.NavyCapAtWar = navy + 3;
            w.PirateGroupCap = haven != null ? 1 : 0;
            w.PirateIncomePerDay = haven != null ? 4e5 * scale : 0;
            w.HunterCap = haven != null ? 1 : 0;
            w.RaiderNames = society.RaiderNames;

            // ---- lanes, structures and rings
            var lanes = new Hyperlanes(ephemeris);
            foreach (var (a, b, bend) in society.Lanes) lanes.Add(a, b, bend);
            w.Wire(startJd, seed, lanes);
            var war = w.War;
            foreach (var s in e.Settlements)
            {
                // A colony on a world goes up and down through a docking ring over its main town.
                if (s.Kind != SettlementKind.Colony || w.Catalog[s.Body].Kind == BodyKind.Station) continue;
                w.Infrastructure.AddRing(e, w.Catalog[s.Body].Name, s.Name + " Ring", s.Name, 0, 0);
            }
            w.Fleet.Rings.AddRange(w.Infrastructure.Rings);

            // ---- ships
            if (navy > 0)
            {
                var patrol = new List<string>();
                foreach (var s in e.Settlements)
                    if (s.Owner == authority || s.Owner == company)
                    {
                        string body = w.Catalog[s.Body].Name;
                        if (!patrol.Contains(body)) patrol.Add(body);
                    }
                var escorts = new List<(ShipClass, string)>();
                for (int i = 1; i < navy; i++) escorts.Add((i == 1 && people >= 100e3 ? ShipClass.Destroyer : ShipClass.Fighter, society.Navy[i]));
                fleet.Group(authority, startJd, people >= 100e3 ? ShipClass.Cruiser : ShipClass.Destroyer, society.Navy[0], patrol.ToArray(), escorts.ToArray());
            }
            string[] Take(string[] names, int n)
            {
                var r = new string[n];
                Array.Copy(names, r, n);
                return r;
            }
            w.BuildMerchants(startJd, Take(society.Freighters, freighters), Take(society.Haulers, haulers), Take(society.Couriers, couriers),
                new[] { w.Independents, company, w.Independents, authority }, i => company);
            // BuildMerchants names later hulls from these lists: keep the whole of each.
            w.merchantNames = society.Freighters;
            w.minerNames = society.Haulers;

            w.Companies.MaxCompanies = Math.Max(society.Companies.Count, 2) + 1;
            var table = new List<(string, string, string, Faction, bool)>();
            foreach (var (name, tag, home, flag, mining) in society.Companies) table.Add((name, tag, home, Of(flag), mining));
            w.FoundCompanies(startJd, table.ToArray());
            w.AddFreeShips(startJd, society.Yachts, society.Prospectors, society.Explorers);
            if (haven != null) war.RaisePirateGroup(startJd, haven.Body);
            foreach (var site in society.HavenSites) w.HavenSites.Add(w.Catalog[site].Index);
            // A system with a navy of its own flies its patrol wings too (U.SPC-T371).
            if (navy > 0) PatrolWings.Raise(w, startJd);
            // And its ports' ordinary traffic: buses, taxis and people's own craft (U.SPC-T375).
            LocalTraffic.Raise(w, startJd);
            w.SeatCapitals();
            w.MusterReserves();

            w.Update(jd);
            return w;
        }

        /// <summary>
        /// Sizes a system's mines and factories so it makes <see cref="SupplyMargin"/> times what it uses of every good
        /// that is used: each good short is made up by raising every facility that makes it in proportion, again and
        /// again, since a bigger smelter wants more ore and a bigger mine more machinery; each counted at the share of
        /// its capacity it can expect to run at (<see cref="ExpectedRunning"/>). Havens fend for themselves
        /// and are left out. A good used but made nowhere is a mistake in the society's plan, and throws.
        /// </summary>
        public static void BalanceSupply(IReadOnlyList<Settlement> settlements, string system)
        {
            var supply = new double[GoodInfo.Count];
            var demand = new double[GoodInfo.Count];
            for (int pass = 0; pass < 60; pass++)
            {
                Array.Clear(supply, 0, supply.Length);
                Array.Clear(demand, 0, demand.Length);
                foreach (var s in settlements)
                {
                    if (s.Kind == SettlementKind.Haven) continue;
                    foreach (var (g, perPerson) in s.PersonNeeds) demand[(int)g] += perPerson * s.Population;
                    foreach (var f in s.Facilities)
                    {
                        double running = ExpectedRunning(s, f);
                        foreach (var (g, rate) in f.Recipe.Inputs) demand[(int)g] += rate * f.Level;
                        foreach (var (g, rate) in f.Recipe.Outputs) supply[(int)g] += rate * f.Level * running;
                    }
                }
                bool changed = false;
                for (int g = 0; g < GoodInfo.Count; g++)
                {
                    if (demand[g] <= 0 || supply[g] >= demand[g] * SupplyMargin * 0.999) continue;
                    if (supply[g] <= 0) throw new InvalidOperationException($"{system} uses {GoodInfo.Of((Good)g).Name} but makes none");
                    double factor = demand[g] * SupplyMargin / supply[g];
                    foreach (var s in settlements)
                    {
                        if (s.Kind == SettlementKind.Haven) continue;
                        foreach (var f in s.Facilities)
                            foreach (var (o, _) in f.Recipe.Outputs)
                                if ((int)o == g)
                                    f.Level = f.BaseLevel = f.Level * factor;
                    }
                    changed = true;
                }
                if (!changed) return;
            }
        }

        /// <summary>
        /// The share of capacity a facility can count on: all of it if every consumable it burns (a mine's machinery
        /// and helium-3, a lab's rare earths) is made where it stands, else <see cref="Recipe.MinorFloor"/>, since in a
        /// thin system those come in dribs and drabs. Life support runs on regardless.
        /// </summary>
        static double ExpectedRunning(Settlement s, Facility f)
        {
            if (f.LifeSupport) return 1;
            var inputs = f.Recipe.Inputs;
            for (int k = 0; k < inputs.Length; k++)
            {
                if (f.Recipe.Limiting[k]) continue;
                bool local = false;
                foreach (var other in s.Facilities)
                    foreach (var (o, _) in other.Recipe.Outputs)
                        local |= o == inputs[k].good;
                if (!local) return Recipe.MinorFloor;
            }
            return 1;
        }

        /// <summary>How much more of every good a settled system makes than it uses: the traders' living, and the slack for the shipyards' and the ships' own wants.</summary>
        public const double SupplyMargin = 1.15;
    }
}
