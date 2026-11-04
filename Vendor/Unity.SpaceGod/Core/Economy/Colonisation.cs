using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>Where a settlement stands in its life (U.SPC-T98).</summary>
    public enum ColonyStage
    {
        /// <summary>One of the system's settlements from the start of the game.</summary>
        Established,

        /// <summary>Founded in play by a colony ship: a small foothold living off its starter goods, its farms and its ice.</summary>
        Outpost,

        /// <summary>A founded outpost that has grown into a colony (<see cref="Colonisation.ColonyPopulation"/>, <see cref="Colonisation.ColonyJobs"/>).</summary>
        Colony,

        /// <summary>A founded settlement that failed: its last people have gone, its facilities stand empty.</summary>
        Ruin,
    }

    /// <summary>
    /// A world a faction could settle: where it sits against its star's habitable zone, what can be dug there
    /// and how many people it could hold. Surveyed from the catalogue (<see cref="Survey"/>); the resources and
    /// capacity are game-layer judgements from the body's kind, size and sunlight, set out there.
    /// </summary>
    public sealed class ColonySite
    {
        public int Body;
        public string Name;
        public HabitableZone Zone;
        public Good[] Resources = new Good[0];

        /// <summary>The most people the site's habitat can be built out for (the base population times <see cref="People.MaxGrowth"/>).</summary>
        public double Capacity;

        /// <summary>Starlight at the site, Earth = 1.</summary>
        public double Flux;

        /// <summary>An empty orbit marked out for a habitat (<see cref="BodyDefinition.HabitatSite"/>) rather than a world to land on.</summary>
        public bool Habitat;

        public bool Has(Good g) => Array.IndexOf(Resources, g) >= 0;

        /// <summary>
        /// People can live here: in the habitable zone, or with ice to melt for water (nobody founds a colony
        /// that would drink only what is shipped in).
        /// </summary>
        public bool Viable => Zone != HabitableZone.None || Has(Good.Water);

        /// <summary>
        /// How much a faction wants the site (0 if it cannot be lived on): the habitable zone counts most,
        /// then what there is to dig and how far the habitat can grow.
        /// </summary>
        public double Score
        {
            get
            {
                if (!Viable || Capacity <= 0) return 0;
                double zone = Zone == HabitableZone.Conservative ? 3 : Zone == HabitableZone.Optimistic ? 2 : 1;
                double worth = 0;
                foreach (var g in Resources) worth += Math.Log10(10 + GoodInfo.Of(g).BasePrice);
                return zone * (1 + 0.25 * worth) * Math.Log10(Capacity);
            }
        }

        /// <summary>
        /// Surveys body <paramref name="index"/> of a catalogue as a site, or null if nobody could found a
        /// settlement on it (a star, a station, a gas giant, or a body with no star to orbit). The zone is
        /// Kopparapu's (<see cref="HabitableZones"/>) for the orbit of the body (or of the planet it circles)
        /// about its star. What can be dug, assumed from the sunlight and size: worlds in the zone have water
        /// (and, if big enough to hold an atmosphere, nitrogen and titanium); hot ones bare metal; cold ones ices
        /// (water, and on the bigger ones nitrogen and hydrocarbons); small bodies of the warm inner system
        /// metals and the precious ones. A site with water but no nitrogen lives on nitrogen shipped in.
        /// </summary>
        public static ColonySite Survey(BodyCatalog catalog, int index)
        {
            var b = catalog[index];
            if (b.Kind == BodyKind.Star || b.Kind == BodyKind.Barycentre || b.GasGiant) return null;
            if (b.Kind == BodyKind.Station && !b.HabitatSite) return null;
            // Climb to the body whose parent is a star: its orbit sets the sunlight.
            var orbiter = b;
            BodyDefinition star = null;
            while (orbiter.ParentIndex >= 0)
            {
                var parent = catalog[orbiter.ParentIndex];
                if (parent.Kind == BodyKind.Star)
                {
                    star = parent;
                    break;
                }
                orbiter = parent;
            }
            if (star == null || orbiter.Elements == null) return null;
            double aAu = orbiter.Elements.AKm / Astro.AuKm, e = Math.Min(0.95, orbiter.Elements.E);
            if (aAu <= 0) return null;
            var site = new ColonySite
            {
                Body = index,
                Name = b.Name,
                Zone = HabitableZones.Classify(star.LuminositySolar, star.TeffK, aAu, e),
                Flux = HabitableZones.MeanFlux(star.LuminositySolar, aAu, e),
            };
            if (b.HabitatSite)
            {
                // An orbit marked out for a habitat (U.SPC-T293): nothing to dig, but water and nitrogen from captured comets,
                // as the frontier's ice stations have them (assumed); room for more people the more sunlight there is to farm by.
                site.Habitat = true;
                site.Resources = new[] { Good.Water, Good.Nitrogen };
                site.Capacity = site.Zone == HabitableZone.Conservative ? 3e5 : site.Zone == HabitableZone.Optimistic ? 2e5 : 1e5;
                return site;
            }
            bool small = b.Kind == BodyKind.Asteroid || b.RadiusKm < 200;
            double inner = HabitableZones.RunawayFlux(star.TeffK), outer = HabitableZones.MaxGreenhouseFlux(star.TeffK);
            var list = new List<Good>();
            if (site.Zone != HabitableZone.None)
            {
                list.Add(Good.Water);
                if (!small)
                {
                    list.Add(Good.Nitrogen);
                    list.Add(Good.Titanium);
                }
            }
            else if (site.Flux > inner)
            {
                list.Add(Good.NickelIron);
                list.Add(Good.Titanium);
                list.Add(Good.Copper);
                if (small) list.Add(Good.Platinum);
            }
            else if (site.Flux > outer * 0.25 && small)
            {
                // The warm inner belt: dry metal rock.
                list.Add(Good.NickelIron);
                list.Add(Good.Platinum);
                list.Add(Good.RareEarths);
            }
            else
            {
                // Cold: ices. Only a world big enough to have held on to them has nitrogen and hydrocarbons too.
                list.Add(Good.Water);
                if (!small)
                {
                    list.Add(Good.Nitrogen);
                    list.Add(Good.Hydrocarbons);
                }
            }
            site.Resources = list.ToArray();
            site.Capacity = site.Zone == HabitableZone.Conservative ? 2e6
                : site.Zone == HabitableZone.Optimistic ? 1e6
                : b.RadiusKm >= 1000 ? 4e5
                : b.RadiusKm >= 200 ? 1.5e5
                : 5e4;
            return site;
        }

        public override string ToString() => Name;
    }

    /// <summary>A colony ship under way: the colonists it carries, its starter goods, and where it is bound.</summary>
    public sealed class ColonyShip
    {
        public string Name;
        public Faction Owner;

        /// <summary>The port it sailed from, where the colonists gathered.</summary>
        public Settlement From;

        /// <summary>The site it goes to found a settlement on; or, for a ship taking more people out to a young one, its body.</summary>
        public ColonySite Site;

        /// <summary>The young settlement it takes more people to, or null for a founding voyage.</summary>
        public Settlement Reinforcing;

        public double Colonists;

        /// <summary>Starter goods in the hold, tonnes, by good.</summary>
        public readonly double[] Cargo = new double[GoodInfo.Count];

        public double DepartJd, ArriveJd;

        /// <summary>
        /// For a voyage through the gates to another system (U.SPC-T293): that system's colonisation and markets, and
        /// its name; null for a voyage within the system it sailed from.
        /// </summary>
        public Colonisation Into;
        public Economy IntoEconomy;
        public string Where;

        public bool Abroad => Into != null;

        /// <summary>
        /// The colony ship's own hull (U.SPC-T524): a freighter fitted out for the voyage (<see cref="ShipRole.Colonist"/>), a real
        /// ship in its system's fleet that flies from the port to the site, through the gates for a site abroad, and can be
        /// fought, boarded or lost on the way. The colonists land only when it gets there. Null for a voyage reckoned by its
        /// timetable alone (a <see cref="Colonisation"/> with no fleet to raise hulls in, see <see cref="Colonisation.Embark"/>).
        /// </summary>
        public Ship Hull;

        /// <summary>The colonisation that sent it (whose <see cref="Colonisation.Voyages"/> it is in).</summary>
        public Colonisation Of;

        /// <summary>Its hull is carrying the colonists back to <see cref="From"/>: the site was taken, or the settlement lost, while they were on their way.</summary>
        public bool Returning;

        /// <summary>The body the hull is bound for now: the site (or the young settlement), or home to <see cref="From"/> when <see cref="Returning"/>.</summary>
        public int Body => Returning && From != null ? From.Body : Reinforcing?.Body ?? Site.Body;

        public override string ToString() => Name;
    }

    /// <summary>What a colony ship's hull reaching a body came to (U.SPC-T524, <see cref="Colonisation.HullArrived"/>).</summary>
    public enum ColonyLanding
    {
        /// <summary>Not where its voyage is bound: nothing happens.</summary>
        None,

        /// <summary>Its colonists founded a settlement on the site: the hull is broken up for the new habitat.</summary>
        Founded,

        /// <summary>Its colonists joined the young settlement it was sent to: the hull goes back to trading.</summary>
        Reinforced,

        /// <summary>The site was taken (or the settlement lost) while they were on their way: the hull carries them home.</summary>
        TurnedBack,

        /// <summary>Home again with the colonists it could not land: they rejoin their port, and the hull goes back to trading.</summary>
        Returned,
    }

    /// <summary>A free site in another system that a colony ship could reach through the gates, and how long it would take (U.SPC-T293).</summary>
    public sealed class ForeignSite
    {
        public ColonySite Site;

        /// <summary>The system's colonisation and markets, and its name.</summary>
        public Colonisation Into;
        public Economy Economy;
        public string Where;

        /// <summary>Days from the port to the site: out to the gate, through the gates, and in to the site.</summary>
        public double Days;
    }

    /// <summary>
    /// Colonisation (U.SPC-T98): factions found new settlements as their people run out of room. Nothing
    /// here runs on a timer: the pressure is the population model's own. A settlement at its population cap
    /// (<see cref="People.MaxGrowth"/> times its founding people) can neither grow nor take in migrants, and
    /// both the growth it cannot take and the migrants turned away from full places are counted as its
    /// <see cref="Settlement.Overflow"/>: people with nowhere to go (a crowded place, past <see cref="People.CrowdedShare"/>
    /// of its cap, counts a rising share of its growth the same way). Once a faction has a ship's worth of them
    /// (<see cref="PartySize"/>) and can pay for the voyage, it sends a colony ship from its most crowded port
    /// with those colonists and starter goods: to a young settlement of its own that has room, or else to
    /// the best free site in reach (the habitable zone first, then what there is to dig), which may be in another
    /// system newly opened to settlement, through the gates (<see cref="Abroad"/>, U.SPC-T293). The colony ship is a real
    /// hull in the fleet (U.SPC-T524, <see cref="Embark"/>): it flies the voyage itself, can be fought, boarded and lost on
    /// the way, and its colonists land only when it actually gets there (<see cref="HullArrived"/>). On landing the
    /// colonists found an <see cref="ColonyStage.Outpost"/> with farms and ice mines to feed itself and a
    /// mine on the site's best ore.
    ///
    /// A founded settlement then lives by the same markets and population model as any other. One that is
    /// fed and full is built out (<see cref="ExpandFactor"/>, paid by its owner) and its life support with
    /// it; past <see cref="ColonyPopulation"/> people and <see cref="ColonyJobs"/> facility jobs it is a
    /// <see cref="ColonyStage.Colony"/>. One that starves or sickens shrinks to its hard core (the
    /// population floor), at which its last people give up and leave for the owner's biggest settlement:
    /// it is a <see cref="ColonyStage.Ruin"/>, and after <see cref="RuinYears"/> its site is free again.
    /// </summary>
    public sealed class Colonisation
    {
        /// <summary>Every site in the system, occupied or not (whether one is free is checked when it is wanted).</summary>
        public readonly List<ColonySite> Sites = new List<ColonySite>();

        /// <summary>Colony ships under way that sailed from here (to this system or, through the gates, another).</summary>
        public readonly List<ColonyShip> Voyages = new List<ColonyShip>();

        /// <summary>Colony ships under way from other systems to a site here (U.SPC-T293); they land through their own system's review.</summary>
        public readonly List<ColonyShip> Incoming = new List<ColonyShip>();

        /// <summary>
        /// The free sites in other systems open to settlement that a faction's colonists at a port could reach through
        /// the gates (U.SPC-T293); null for a system on its own.
        /// </summary>
        public Func<Faction, Settlement, double, IEnumerable<ForeignSite>> Abroad;

        /// <summary>
        /// What a site in a system nobody has settled yet is worth against one at home: a whole system to hold, its gate
        /// and its markets the founder's (assumed).
        /// </summary>
        public const double NewSystemWorth = 2;

        /// <summary>Days a colony ship takes from a port to a body at a date; null for <see cref="DefaultTravelDays"/>.</summary>
        public Func<Settlement, int, double, double> TravelDays;

        /// <summary>A colony ship has sailed.</summary>
        public Action<ColonyShip> Launched;

        /// <summary>A colony ship has landed at a young settlement with more people (or turned back, the site taken: settlement null).</summary>
        public Action<ColonyShip, Settlement> Landed;

        /// <summary>
        /// Raises the hull for a colony ship that has just sailed (U.SPC-T524): a real ship in this system's fleet at its port,
        /// fitted out for the voyage (<see cref="Ship.Colony"/>), or null for none (the voyage is then reckoned by its timetable).
        /// </summary>
        public Func<ColonyShip, double, Ship> Embark;

        /// <summary>Sends a colony ship's hull bound for another system off through the gates (U.SPC-T524); false if it cannot go.</summary>
        public Func<ColonyShip, double, bool> SailAbroad;

        /// <summary>A colony ship's hull is done with its voyage (or never got away): it goes back to ordinary work where it is.</summary>
        public Action<Ship> Refit;

        /// <summary>
        /// A colony ship has been lost on the way with its colonists (U.SPC-T524): its hull destroyed, taken by the faction given,
        /// or (neither) never heard from again.
        /// </summary>
        public Action<ColonyShip, Faction> Lost;

        /// <summary>A colony ship that found its site taken has brought its colonists home (U.SPC-T524).</summary>
        public Action<ColonyShip> Returned;

        /// <summary>Days past its reckoned arrival after which a colony ship's hull, wherever it is, is given up for lost.</summary>
        public const double OverdueDays = 730;

        /// <summary>A new outpost has been founded.</summary>
        public Action<Settlement> Founded;

        /// <summary>A founded settlement's habitat has been built out for more people.</summary>
        public Action<Settlement> Expanded;

        /// <summary>An outpost has grown into a colony.</summary>
        public Action<Settlement> Promoted;

        /// <summary>A founded settlement has failed and been abandoned.</summary>
        public Action<Settlement> Ruined;

        /// <summary>Colonists a colony ship carries (a party the size of a small town; assumed).</summary>
        public const double PartySize = 5000;

        /// <summary>What a founding voyage costs its faction, credits: the ship's charter and a prefabricated habitat (assumed).</summary>
        public const double FoundingCost = 1.5e8;

        /// <summary>What a voyage taking more people to a young settlement costs, credits (the charter alone; assumed).</summary>
        public const double ReinforceCost = 4e7;

        /// <summary>Days of food, water and air the colonists' starter goods hold, and of medicine and the habitat's spares.</summary>
        public const double StarterDays = 180, MedicineDays = 365;

        /// <summary>Days a voyage takes when nobody has said how far it is.</summary>
        public const double DefaultTravelDays = 60;

        /// <summary>An outpost becomes a colony with this many people and this many facility jobs.</summary>
        public const double ColonyPopulation = 25000, ColonyJobs = 1000;

        /// <summary>A founded settlement this full (of its cap), and this well, is built out by <see cref="ExpandFactor"/>.</summary>
        public const double ExpandAt = 0.75, ExpandWellbeing = 0.9, ExpandFactor = 1.5;

        /// <summary>Share of its industrial labour a founded settlement's first mine employs, and each mine opened as it is built out (assumed).</summary>
        public const double FirstMineShare = 0.25, NextMineShare = 0.15;

        /// <summary>Building out a habitat, credits per person of new base population (assumed).</summary>
        public const double ExpansionCostPerPerson = 4000;

        /// <summary>A young settlement takes more colonists while it is this well and has room for half a party.</summary>
        public const double ReinforceWellbeing = 0.85;

        /// <summary>A founded settlement at its floor that has been living this badly is abandoned.</summary>
        public const double FailWellbeing = 0.8;

        /// <summary>Years before a ruin's site may be settled again.</summary>
        public const double RuinYears = 5;

        /// <summary>Share of their own people's needs the founders' farms and ice mines are built to cover.</summary>
        public const double SelfSufficiency = 1.0;

        static readonly string[] ShipNames = { "Pathfinder", "Homestead", "Pioneer", "Wayfarer", "Providence", "Arcadia", "Endeavour", "Threshold", "Hearth", "New Dawn" };

        int launched;

        /// <summary>Surveys every body of a catalogue (<see cref="ColonySite.Survey"/>).</summary>
        public void SurveyAll(BodyCatalog catalog)
        {
            Sites.Clear();
            for (int i = 0; i < catalog.Count; i++)
            {
                var site = ColonySite.Survey(catalog, i);
                if (site != null) Sites.Add(site);
            }
        }

        /// <summary>Called at each market review: ships land, young settlements grow or fail, crowded factions send ships.</summary>
        public void Review(Economy economy, double jd)
        {
            Land(economy, jd);
            Tend(economy, jd);
            Launch(economy, jd);
        }

        // ---------------------------------------------------------------- pressure

        /// <summary>People with no room across <paramref name="f"/>'s settlements (<see cref="Settlement.Overflow"/>).</summary>
        public static double Pressure(Economy economy, Faction f)
        {
            double sum = 0;
            foreach (var s in economy.Settlements)
                if (s.Owner == f && Lives(s) && s.Kind != SettlementKind.Haven) sum += s.Overflow;
            return sum;
        }

        static bool Lives(Settlement s) => !s.Fallen && s.Stage != ColonyStage.Ruin;

        public static bool IsFounded(Settlement s) => s.Stage == ColonyStage.Outpost || s.Stage == ColonyStage.Colony;

        /// <summary>The live settlement on <paramref name="body"/>, if any (a fresh ruin counts: its site is not yet free).</summary>
        public static bool Occupied(Economy economy, int body, double jd)
        {
            foreach (var s in economy.Settlements)
            {
                if (s.Body != body) continue;
                if (Lives(s)) return true;
                if (s.Stage == ColonyStage.Ruin && jd - s.RuinedJd < RuinYears * 365.25) return true;
            }
            return false;
        }

        /// <summary>A colony ship is on its way to <paramref name="body"/> here (one bound for another system does not count).</summary>
        public bool Bound(int body)
        {
            foreach (var v in Voyages)
                if (!v.Abroad && v.Body == body) return true;
            foreach (var v in Incoming)
                if (v.Body == body) return true;
            return false;
        }

        /// <summary>Whether any colony ship from another system is on its way here.</summary>
        public bool Awaited => Incoming.Count > 0;

        bool Sailing(Faction f)
        {
            foreach (var v in Voyages)
                if (v.Owner == f) return true;
            return false;
        }

        double Days(Settlement from, int body, double jd)
        {
            if (from.Body == body) return 0;
            double d = TravelDays != null ? TravelDays(from, body, jd) : DefaultTravelDays;
            return double.IsNaN(d) || d < 0 ? DefaultTravelDays : d;
        }

        // ---------------------------------------------------------------- launching

        void Launch(Economy economy, double jd)
        {
            var factions = new List<Faction>();
            foreach (var s in economy.Settlements)
                if (s.Owner != null && !s.Owner.IsPirate && s.Kind != SettlementKind.Haven && Lives(s) && !factions.Contains(s.Owner)) factions.Add(s.Owner);
            foreach (var f in factions) TryLaunch(economy, f, jd);
        }

        /// <summary>
        /// Sends a colony ship for <paramref name="f"/> if it has a party's worth of people with no room, nothing
        /// already under way, a place to send them and the money to do it. Returns the ship, or null.
        /// </summary>
        public ColonyShip TryLaunch(Economy economy, Faction f, double jd)
        {
            if (f == null || f.IsPirate || Sailing(f)) return null;
            double pool = Pressure(economy, f);
            if (pool < PartySize) return null;

            // The colonists gather at the most crowded port.
            Settlement from = null;
            foreach (var s in economy.Settlements)
                if (s.Owner == f && Lives(s) && s.Kind != SettlementKind.Haven && (from == null || s.Overflow > from.Overflow)) from = s;
            if (from == null) return null;

            // A young settlement of its own with room and doing well takes them first ...
            Settlement reinforce = null;
            double bestDays = double.MaxValue;
            foreach (var s in economy.Settlements)
            {
                if (s.Owner != f || s == from || !Lives(s) || !IsFounded(s) || s.Wellbeing < ReinforceWellbeing || Bound(s.Body)) continue;
                double room = s.InitialPopulation * People.MaxGrowth - s.Population;
                if (room < PartySize * 0.5) continue;
                double d = Days(from, s.Body, jd);
                if (d < bestDays)
                {
                    bestDays = d;
                    reinforce = s;
                }
            }

            ColonySite site = null;
            ForeignSite abroad = null;
            double cost;
            if (reinforce != null) cost = ReinforceCost;
            else
            {
                // ... or else the best free site in reach: in this system, or through the gates in one newly opened to settlement.
                double best = 0;
                foreach (var c in Sites)
                {
                    double score = c.Score;
                    if (score <= 0 || Occupied(economy, c.Body, jd) || Bound(c.Body)) continue;
                    double d = Days(from, c.Body, jd);
                    double value = score / (1 + d / 200);
                    if (value > best)
                    {
                        best = value;
                        site = c;
                        bestDays = d;
                    }
                }
                if (Abroad != null)
                    foreach (var x in Abroad(f, from, jd))
                    {
                        double score = x.Site.Score;
                        if (score <= 0 || double.IsNaN(x.Days) || x.Days < 0) continue;
                        double value = score * NewSystemWorth / (1 + x.Days / 200);
                        if (value > best)
                        {
                            best = value;
                            site = x.Site;
                            abroad = x;
                            bestDays = x.Days;
                        }
                    }
                if (site == null) return null;
                cost = FoundingCost;
            }
            if (f.Treasury < cost) return null;

            var ship = new ColonyShip
            {
                Name = (string.IsNullOrEmpty(f.Prefix) ? "" : f.Prefix + " ") + ShipNames[launched % ShipNames.Length] + (launched >= ShipNames.Length ? " " + (launched / ShipNames.Length + 1) : ""),
                Owner = f,
                From = from,
                Site = site ?? SiteAt(reinforce.Body),
                Reinforcing = reinforce,
                DepartJd = jd,
                ArriveJd = jd + bestDays,
                Into = abroad?.Into,
                IntoEconomy = abroad?.Economy,
                Where = abroad?.Where,
            };

            // The colonists come from every crowded place in turn, most crowded first, none below its floor.
            double party = Math.Min(PartySize, pool), taken = 0;
            var crowded = new List<Settlement>();
            foreach (var s in economy.Settlements)
                if (s.Owner == f && Lives(s) && s.Kind != SettlementKind.Haven && s.Overflow > 0) crowded.Add(s);
            crowded.Sort((a, b) => b.Overflow.CompareTo(a.Overflow));
            foreach (var s in crowded)
            {
                double take = Math.Min(Math.Min(s.Overflow, party - taken), Math.Max(0, s.Population - s.InitialPopulation * People.MinShare));
                if (take <= 0) continue;
                s.Population -= take;
                s.Overflow -= take;
                taken += take;
                if (taken >= party) break;
            }
            if (taken <= 0) return null;
            ship.Colonists = taken;
            launched++;

            // Starter goods: what they eat, drink and breathe for the first months, medicine and spares for a year,
            // bought from the port's market (what it can spare) by the faction.
            var kind = SettlementKind.MiningOutpost;
            double spent = cost;
            foreach (var (g, perDay) in People.NeedsOf(kind))
            {
                double days = GoodInfo.Of(g).Category == GoodCategory.Essential && g != Good.MedicalSupplies && g != Good.Nitrogen ? StarterDays : MedicineDays;
                if (g == Good.Nitrogen) days = StarterDays;
                double want = perDay * taken * days;
                double tonnes = Math.Min(want, from.Available(g));
                if (tonnes <= 0) continue;
                from.Stock[(int)g] -= tonnes;
                ship.Cargo[(int)g] += tonnes;
                spent += tonnes * from.Price[(int)g];
            }
            f.Treasury -= spent;
            ship.Of = this;
            Voyages.Add(ship);
            ship.Into?.Incoming.Add(ship);
            // Its own hull (U.SPC-T524): a freighter fitted out at the port, which flies the voyage itself (through the gates
            // for a site abroad). Colonists for a settlement on the very body they sail from have no voyage to fly.
            if (Embark != null && (ship.Abroad || from.Body != ship.Body))
            {
                ship.Hull = Embark(ship, jd);
                if (ship.Hull != null && ship.Abroad && (SailAbroad == null || !SailAbroad(ship, jd)))
                {
                    // No way through the gates for it after all: the hull goes back to work, and the voyage is reckoned as before.
                    var hull = ship.Hull;
                    ship.Hull = null;
                    hull.Colony = null;
                    Refit?.Invoke(hull);
                }
            }
            Launched?.Invoke(ship);
            return ship;
        }

        /// <summary>The colonisation whose system a colony ship's hull must be in to reach <see cref="ColonyShip.Body"/>.</summary>
        public static Colonisation Bearing(ColonyShip v) => v.Returning || !v.Abroad ? v.Of : v.Into;

        /// <summary>
        /// Where a colony ship's hull in the system of <paramref name="here"/> goes next (U.SPC-T524): the body its voyage is bound
        /// for, -1 to hold where it is (it is not in that system), or null if it is on no voyage under way.
        /// </summary>
        public static int? HullDestination(Ship hull, Colonisation here)
        {
            var v = hull?.Colony;
            if (v?.Of == null || !v.Of.Voyages.Contains(v)) return null;
            return Bearing(v) == here ? v.Body : -1;
        }

        /// <summary>
        /// A colony ship's hull has reached <paramref name="hull"/>'s body in the system of <paramref name="here"/> (its markets
        /// <paramref name="economy"/>) (U.SPC-T524): where its voyage is bound, the colonists land, found their settlement or join
        /// the one they were sent to (<see cref="Arrive"/>); with the site taken, they stay aboard and the hull turns for home;
        /// home again, they rejoin their port. Says what came of it, for the hull's world to see to the hull.
        /// </summary>
        public ColonyLanding HullArrived(Ship hull, Colonisation here, Economy economy, double jd)
        {
            var v = hull?.Colony;
            if (v == null || v.Of != this || !Voyages.Contains(v) || Bearing(v) != here || hull.Orbit == null || hull.Orbit.Body != v.Body)
                return ColonyLanding.None;
            Voyages.Remove(v);
            if (v.Returning)
            {
                if (v.From != null && Lives(v.From))
                {
                    v.From.Population += v.Colonists;
                    v.From.Overflow += v.Colonists;
                    Unload(v, v.From);
                }
                hull.Colony = null;
                Returned?.Invoke(v);
                return ColonyLanding.Returned;
            }
            Arrive(economy, v, jd);
            if (v.Returning) return ColonyLanding.TurnedBack;
            hull.Colony = null;
            return v.Reinforcing != null ? ColonyLanding.Reinforced : ColonyLanding.Founded;
        }

        /// <summary>Turns a colony ship for home with its colonists still aboard (U.SPC-T524): its hull carries them back to <see cref="ColonyShip.From"/>.</summary>
        public void TurnBack(ColonyShip v)
        {
            if (v == null || v.Returning) return;
            v.Returning = true;
            v.Into?.Incoming.Remove(v);
        }

        /// <summary>
        /// Checks on a colony ship's hull at a market review (U.SPC-T524): destroyed, or taken by another faction, it is lost with
        /// its colonists and their goods; long overdue, it is given up. True if the voyage is over.
        /// </summary>
        bool HullLost(ColonyShip v, double jd)
        {
            var h = v.Hull;
            Faction by = null;
            if (h.Destroyed && !h.Scrapped) { }
            else if (h.Faction != v.Owner) by = h.Faction;
            else if (jd <= v.ArriveJd + OverdueDays) return false;
            Voyages.Remove(v);
            v.Into?.Incoming.Remove(v);
            if (h.Colony == v) h.Colony = null;
            Array.Clear(v.Cargo, 0, v.Cargo.Length);
            if (!h.Destroyed)
            {
                // Taken: its captors do with it as they will. Given up: whatever is left of it goes back to work where it is.
                if (by != null) { if (h.Role == ShipRole.Colonist) h.Role = ShipRole.Free; }
                else Refit?.Invoke(h);
            }
            Lost?.Invoke(v, by);
            return true;
        }

        ColonySite SiteAt(int body)
        {
            foreach (var s in Sites)
                if (s.Body == body) return s;
            return new ColonySite { Body = body, Name = "body " + body, Capacity = 0 };
        }

        // ---------------------------------------------------------------- landing

        void Land(Economy economy, double jd)
        {
            for (int i = 0; i < Voyages.Count; i++)
            {
                var v = Voyages[i];
                // A real hull lands its colonists when it gets there (HullArrived), not by the timetable (U.SPC-T524).
                if (v.Hull != null)
                {
                    if (HullLost(v, jd)) i--;
                    continue;
                }
                if (jd < v.ArriveJd) continue;
                Voyages.RemoveAt(i--);
                Arrive(economy, v, jd);
            }
        }

        /// <summary>Lands a colony ship: its people join the young settlement it was sent to, or found one on its site.</summary>
        public Settlement Arrive(Economy economy, ColonyShip v, double jd)
        {
            var target = v.Reinforcing;
            if (target != null)
            {
                if (Lives(target) && target.Owner == v.Owner)
                {
                    double room = Math.Max(0, target.InitialPopulation * People.MaxGrowth - target.Population);
                    double settle = Math.Min(room, v.Colonists);
                    target.Population += settle;
                    target.Overflow += v.Colonists - settle;
                    Unload(v, target);
                    Provision(target);
                    Landed?.Invoke(v, target);
                    return target;
                }
            }
            else
            {
                // A ship from another system founds in that system's markets, and its news is that system's.
                var there = v.IntoEconomy ?? economy;
                var into = v.Into ?? this;
                v.Into?.Incoming.Remove(v);
                if (!Occupied(there, v.Site.Body, jd))
                {
                    var s = Found(there, v.Owner, v.Site, v.Colonists, jd);
                    Unload(v, s);
                    there.Open(s);
                    into.Founded?.Invoke(s);
                    return s;
                }
            }
            // The site was taken or the settlement lost while they were on their way: they go home. A real hull (U.SPC-T524)
            // carries them back itself, and they rejoin their port when it gets there (HullArrived).
            if (v.Hull != null && !v.Hull.Destroyed && !v.Returning && v.From != null && Lives(v.From))
            {
                v.Returning = true;
                v.Into?.Incoming.Remove(v);
                if (!Voyages.Contains(v)) Voyages.Add(v);
                Landed?.Invoke(v, null);
                return null;
            }
            if (v.From != null && Lives(v.From))
            {
                v.From.Population += v.Colonists;
                v.From.Overflow += v.Colonists;
                Unload(v, v.From);
            }
            Landed?.Invoke(v, null);
            return null;
        }

        static void Unload(ColonyShip v, Settlement s)
        {
            for (int i = 0; i < GoodInfo.Count; i++)
            {
                s.Stock[i] += v.Cargo[i];
                v.Cargo[i] = 0;
            }
        }

        /// <summary>Founds an outpost of <paramref name="people"/> for <paramref name="owner"/> on <paramref name="site"/>.</summary>
        public static Settlement Found(Economy economy, Faction owner, ColonySite site, double people, double jd)
        {
            int ruins = 0;
            foreach (var x in economy.Settlements)
                if (x.Body == site.Body) ruins++;
            // A habitat is called after its site ("Ross 154 Habitat"); a world's first settlement is its landing;
            // each later re-founding is numbered by how many have stood here before it, so the second settlement
            // on a site is "New X 2", not an unnumbered "New X" that then jumps straight to "New X 3" (U.SPC-T320).
            string name = ruins == 0 ? (site.Habitat ? site.Name : site.Name + " Landing") : "New " + site.Name + " " + (ruins + 1);
            var s = economy.Add(name, site.Body, SettlementKind.MiningOutpost, owner, people);
            s.Stage = ColonyStage.Outpost;
            s.FoundedJd = jd;
            s.Site = site;
            s.DaysSinceCapture = 9999;
            Provision(s);
            // A first mine on the site's most valuable ore, sized to a quarter of the colonists' industrial hands.
            Good? ore = null;
            foreach (var g in site.Resources)
                if (g != Good.Water && g != Good.Nitrogen && (ore == null || GoodInfo.Of(g).BasePrice > GoodInfo.Of(ore.Value).BasePrice)) ore = g;
            if (ore != null) AddMine(s, ore.Value, People.Labour(s) * FirstMineShare / Recipe.MineWorkersPerTonneDay(ore.Value));
            return s;
        }

        static void AddMine(Settlement s, Good ore, double perDay)
        {
            if (perDay <= 0) return;
            s.Facilities.Add(new Facility(Recipe.Mine(ore, perDay), 1));
        }

        /// <summary>
        /// Builds a founded settlement's life support up to its people's needs (<see cref="SelfSufficiency"/>):
        /// farms for its food, and mines for its water and air where the site has the ice. A site without them
        /// lives on what it is shipped.
        /// </summary>
        public static void Provision(Settlement s)
        {
            var site = s.Site;
            var farmRecipe = Recipe.Of(IndustryKind.Farm);
            Facility farm = null, water = null, nitrogen = null;
            double food = 0, waterNet = 0, nitrogenNet = 0;
            foreach (var f in s.Facilities)
            {
                if (f.Recipe.Kind == IndustryKind.Farm) farm = f;
                if (f.Recipe.Kind == IndustryKind.Mine && f.Recipe.Outputs[0].good == Good.Water) water = f;
                if (f.Recipe.Kind == IndustryKind.Mine && f.Recipe.Outputs[0].good == Good.Nitrogen) nitrogen = f;
                foreach (var (g, rate) in f.Recipe.Outputs)
                {
                    if (g == Good.Food) food += rate * f.BaseLevel;
                    if (g == Good.Water) waterNet += rate * f.BaseLevel;
                    if (g == Good.Nitrogen) nitrogenNet += rate * f.BaseLevel;
                }
                foreach (var (g, rate) in f.Recipe.Inputs)
                {
                    if (g == Good.Water) waterNet -= rate * f.BaseLevel;
                    if (g == Good.Nitrogen) nitrogenNet -= rate * f.BaseLevel;
                }
            }
            double foodNeed = s.Population * People.NeedOf(s.Kind, Good.Food) * SelfSufficiency;
            if (food < foodNeed)
            {
                double add = (foodNeed - food) / farmRecipe.Outputs[0].perDay;
                if (farm == null) s.Facilities.Add(farm = new Facility(farmRecipe, add));
                else Grow(farm, add);
                waterNet -= farmRecipe.Inputs[0].perDay * add;
                nitrogenNet -= farmRecipe.Inputs[1].perDay * add;
            }
            double waterShort = s.Population * People.NeedOf(s.Kind, Good.Water) * SelfSufficiency - waterNet;
            if (site != null && site.Has(Good.Water) && waterShort > 0) MineMore(s, ref water, Good.Water, waterShort * 1.05);
            double nitrogenShort = s.Population * People.NeedOf(s.Kind, Good.Nitrogen) * SelfSufficiency - nitrogenNet;
            if (site != null && site.Has(Good.Nitrogen) && nitrogenShort > 0) MineMore(s, ref nitrogen, Good.Nitrogen, nitrogenShort);
        }

        static void Grow(Facility f, double addLevel)
        {
            double k = (f.BaseLevel + addLevel) / f.BaseLevel;
            f.BaseLevel *= k;
            f.Level *= k;
        }

        /// <summary>Adds <paramref name="perDay"/> tonnes a day to a mine (whose recipe is per tonne of its first build).</summary>
        static void MineMore(Settlement s, ref Facility mine, Good ore, double perDay)
        {
            if (mine == null)
            {
                s.Facilities.Add(mine = new Facility(Recipe.Mine(ore, perDay), 1));
                return;
            }
            double unit = mine.Recipe.Outputs[0].perDay;
            Grow(mine, perDay / unit);
        }

        // ---------------------------------------------------------------- growth and failure

        void Tend(Economy economy, double jd)
        {
            // Settlements are added as ships land: walk a copy.
            foreach (var s in economy.Settlements.ToArray())
            {
                if (!IsFounded(s) || s.Fallen) continue;
                if (Failing(s)) Abandon(economy, s, jd);
                else
                {
                    // The settlers build farms and dig ice as their numbers grow.
                    Provision(s);
                    TryExpand(s);
                    TryPromote(s);
                }
            }
        }

        /// <summary>A founded settlement fails when hunger or sickness has worn it down to its hard core (the population floor).</summary>
        public static bool Failing(Settlement s) =>
            IsFounded(s) && !s.Fallen && s.Population <= s.InitialPopulation * People.MinShare * 1.02 && s.Wellbeing < FailWellbeing;

        /// <summary>Builds out a fed, full founded settlement's habitat and life support, if its owner can pay and the site has room.</summary>
        public bool TryExpand(Settlement s)
        {
            if (!IsFounded(s) || s.Fallen || s.Owner == null) return false;
            double cap = s.InitialPopulation * People.MaxGrowth;
            if (s.Population < cap * ExpandAt || s.Wellbeing < ExpandWellbeing) return false;
            double siteCap = s.Site != null && s.Site.Capacity > 0 ? s.Site.Capacity : cap;
            double newBase = Math.Min(s.InitialPopulation * ExpandFactor, siteCap / People.MaxGrowth);
            if (newBase <= s.InitialPopulation * 1.01) return false;
            double cost = (newBase - s.InitialPopulation) * ExpansionCostPerPerson;
            if (s.Owner.Treasury < cost) return false;
            s.Owner.Treasury -= cost;
            double old = s.InitialPopulation;
            s.InitialPopulation = newBase;
            // The works grow with the habitat, and a mine is opened on the next ore the site has (life support
            // follows the people as they come, see Provision).
            double grow = newBase / old - 1;
            foreach (var f in s.Facilities)
                if (!f.LifeSupport) Grow(f, f.BaseLevel * grow);
            if (s.Site != null)
                foreach (var g in s.Site.Resources)
                {
                    if (g == Good.Water || g == Good.Nitrogen || HasMine(s, g)) continue;
                    AddMine(s, g, People.Labour(s) * NextMineShare / Recipe.MineWorkersPerTonneDay(g));
                    break;
                }
            Expanded?.Invoke(s);
            return true;
        }

        static bool HasMine(Settlement s, Good g)
        {
            foreach (var f in s.Facilities)
                if (f.Recipe.Kind == IndustryKind.Mine && f.Recipe.Outputs[0].good == g) return true;
            return false;
        }

        /// <summary>An outpost with <see cref="ColonyPopulation"/> people and <see cref="ColonyJobs"/> facility jobs becomes a colony.</summary>
        public bool TryPromote(Settlement s)
        {
            if (s.Stage != ColonyStage.Outpost || s.Fallen) return false;
            if (s.Population < ColonyPopulation || s.Jobs < ColonyJobs) return false;
            s.Stage = ColonyStage.Colony;
            s.Kind = SettlementKind.Colony;
            // A colony's people use more water than an outpost's (showers and laundry): its ice mines grow to match.
            Provision(s);
            Promoted?.Invoke(s);
            return true;
        }

        /// <summary>
        /// Abandons a failed settlement: its last people leave for the owner's biggest live settlement (or any that
        /// will have them), and what is left is a ruin with nobody in it, no market and no work. A frontier outpost
        /// that is the only settlement in its system falls back to its owner's seat of government, wherever that
        /// stands (<see cref="Faction.Capital"/>), rather than simply losing its last settlers (U.SPC-T319).
        /// </summary>
        public void Abandon(Economy economy, Settlement s, double jd)
        {
            Settlement refuge = null;
            foreach (var x in economy.Settlements)
            {
                if (x == s || !Lives(x) || x.Kind == SettlementKind.Haven) continue;
                bool own = x.Owner == s.Owner;
                bool ok = own || economy.Diplomacy == null || !economy.Diplomacy.Hostile(x.Owner, s.Owner);
                if (!ok) continue;
                if (refuge == null || (own && refuge.Owner != s.Owner) || (own == (refuge.Owner == s.Owner) && x.Population > refuge.Population)) refuge = x;
            }
            if (refuge == null)
            {
                var capital = s.Owner?.Capital;
                if (capital != null && capital != s && Lives(capital)) refuge = capital;
            }
            if (refuge != null) refuge.Population += s.Population;
            s.Population = 0;
            s.Overflow = 0;
            s.Stage = ColonyStage.Ruin;
            s.Fallen = true;
            s.RebuildJd = double.PositiveInfinity;
            s.RuinedJd = jd;
            s.Facilities.Clear();
            s.Platforms.Clear();
            s.MaxPlatforms = 0;
            Array.Clear(s.Stock, 0, s.Stock.Length);
            Array.Clear(s.Demand, 0, s.Demand.Length);
            Array.Clear(s.Supply, 0, s.Supply.Length);
            Array.Clear(s.Inbound, 0, s.Inbound.Length);
            Ruined?.Invoke(s);
        }
    }
}
