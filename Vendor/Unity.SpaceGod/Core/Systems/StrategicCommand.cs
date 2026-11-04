using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>What a polity's command sends a fleet group to do (U.SPC-T423, see <see cref="StrategicCommand"/>).</summary>
    public enum ObjectiveKind
    {
        /// <summary>Take an enemy port (or every enemy port in a system): break its defences and besiege it until it falls, or storm a
        /// raider haven. Once it is taken the group holds it.</summary>
        Take,

        /// <summary>Hold a port or place: stay there and fight anything hostile that comes.</summary>
        Hold,

        /// <summary>Blockade an enemy port: lie over it so no ship may dock, and fight what comes to break it.</summary>
        Blockade,

        /// <summary>Shadow one ship (a freighter, a convoy, the player's own hull) wherever it goes, across the gates too.</summary>
        Escort,

        /// <summary>Visit every world of a system in turn, then stand down.</summary>
        Explore,
    }

    /// <summary>How many of a polity's groups one command reaches: one squadron, every group in a system, or the whole navy.</summary>
    public enum CommandScope
    {
        Group,
        System,
        Polity,
    }

    /// <summary>An objective a polity's command has given one of its fleet groups (U.SPC-T423).</summary>
    public sealed class FleetObjective
    {
        public ObjectiveKind Kind;

        /// <summary>The system it is to be carried out in (its world).</summary>
        public World Where;

        /// <summary>The port or place, or -1 for the whole system (Take: every enemy port there; Explore: every world).</summary>
        public int Body = -1;

        /// <summary>The ship an Escort shadows.</summary>
        public Ship Charge;

        public double SinceJd;

        /// <summary>Worlds an Explore has been to.</summary>
        public readonly List<int> Visited = new List<int>();

        /// <summary>The campaign a Take in another system at war was handed to (see <see cref="InterstellarWars.Launch"/>), or null.</summary>
        public Campaign Campaign;
    }

    /// <summary>
    /// What a polity's industry is pointed at (U.SPC-T423): the industries it favours grow on thinner margins and are kept
    /// up through a glut, so a war economy turns out alloys, electronics and munitions and a civil one food and medicine.
    /// A new focus is one <see cref="Register"/> call.
    /// </summary>
    public sealed class ProductionFocus
    {
        static readonly List<ProductionFocus> all = new List<ProductionFocus>();

        public static IReadOnlyList<ProductionFocus> All => all;

        public readonly string Name, Description;
        readonly IndustryKind[] favoured;

        ProductionFocus(string name, string description, IndustryKind[] favoured)
        {
            Name = name;
            Description = description;
            this.favoured = favoured ?? new IndustryKind[0];
        }

        public static ProductionFocus Register(string name, string description, params IndustryKind[] favoured)
        {
            var f = new ProductionFocus(name, description, favoured);
            all.Add(f);
            return f;
        }

        public bool Favours(IndustryKind k) => Array.IndexOf(favoured, k) >= 0;

        public static ProductionFocus Find(string name) => all.Find(f => f.Name == name);

        public static readonly ProductionFocus Balanced = Register("Balanced", "Every industry follows its own market");

        public static readonly ProductionFocus War = Register("War industry", "Smelters, fabs, machine works and munitions plants first",
            IndustryKind.Smelter, IndustryKind.ElectronicsFab, IndustryKind.MachineWorks, IndustryKind.MunitionsPlant);

        public static readonly ProductionFocus Civil = Register("Civil", "Farms and pharmaceutical labs first: feed and heal the people",
            IndustryKind.Farm, IndustryKind.PharmaLab);

        public static readonly ProductionFocus Extraction = Register("Extraction", "Mines first: raw ore, ice and gas for export", IndustryKind.Mine);

        public override string ToString() => Name;
    }

    /// <summary>
    /// What a polity's yards lay down for its navy (U.SPC-T423): the order of hulls they cycle through and how big a navy
    /// they keep (a share of the system's cap). A plan with no hulls leaves the slips to the merchant fleet. A new plan is
    /// one <see cref="Register"/> call.
    /// </summary>
    public sealed class BuildPlan
    {
        static readonly List<BuildPlan> all = new List<BuildPlan>();

        public static IReadOnlyList<BuildPlan> All => all;

        public readonly string Name, Description;

        /// <summary>The hulls laid down in turn; empty for none.</summary>
        public readonly ShipClass[] Cycle;

        /// <summary>The navy kept, as a share of the system's cap (<see cref="World.NavyCap"/>).</summary>
        public readonly double NavyShare;

        /// <summary>
        /// Heavy equipment the plan wants in every group its yards build up (U.SPC-T507): facility kind ids, each fitted to a
        /// newly launched hull able to take it whose group does not carry one yet (see <see cref="War.LaunchWarship"/>). Lane
        /// security fits a field projector (<see cref="FacilityKinds.FieldProjector"/>), making a lane interdictor of a
        /// destroyer or cruiser rather than laying down a class of its own. Empty for most plans.
        /// </summary>
        public string[] GroupFittings { get; private set; } = new string[0];

        BuildPlan(string name, string description, ShipClass[] cycle, double navyShare)
        {
            Name = name;
            Description = description;
            Cycle = cycle ?? new ShipClass[0];
            NavyShare = navyShare;
        }

        public static BuildPlan Register(string name, string description, double navyShare, params ShipClass[] cycle)
        {
            var p = new BuildPlan(name, description, cycle, navyShare);
            all.Add(p);
            return p;
        }

        public static BuildPlan Find(string name) => all.Find(p => p.Name == name);

        /// <summary>This plan, with <paramref name="facilities"/> fitted to one hull of every group it builds up.</summary>
        public BuildPlan WithGroupFittings(params string[] facilities)
        {
            GroupFittings = facilities ?? new string[0];
            return this;
        }

        /// <summary>What every navy builds unless told otherwise: a command cruiser among them to lead a group (U.SPC-T477).</summary>
        public static readonly BuildPlan Balanced = Register("Balanced fleet", "Destroyers, cruisers, a battleship, a command cruiser and a carrier in turn", 1,
            ShipClass.Destroyer, ShipClass.Cruiser, ShipClass.Destroyer, ShipClass.Battleship, ShipClass.Destroyer, ShipClass.CommandCruiser, ShipClass.Carrier);

        public static readonly BuildPlan BattleLine = Register("Battle line", "Battleships and cruisers, with a command cruiser to lead them: the heaviest guns", 1,
            ShipClass.Battleship, ShipClass.Cruiser, ShipClass.Battleship, ShipClass.Destroyer, ShipClass.CommandCruiser);

        /// <summary>Carriers, their screens and the frigates that ride clamped to their hulls (U.SPC-T475).</summary>
        public static readonly BuildPlan Carriers = Register("Carrier groups", "Carriers, their screens and frigates", 1,
            ShipClass.Carrier, ShipClass.Destroyer, ShipClass.Frigate, ShipClass.Cruiser, ShipClass.Destroyer, ShipClass.Frigate);

        /// <summary>Strike flotillas (U.SPC-T475): frigates that alpha-strike the enemy's capitals, with destroyers to screen them.</summary>
        public static readonly BuildPlan Strike = Register("Strike flotillas", "Frigates and their destroyers: alpha strikes on the enemy's capitals", 1,
            ShipClass.Frigate, ShipClass.Destroyer, ShipClass.Frigate, ShipClass.Frigate, ShipClass.Cruiser);

        public static readonly BuildPlan Screens = Register("Screens", "Destroyers: cheap, quick hulls in numbers", 1,
            ShipClass.Destroyer, ShipClass.Destroyer, ShipClass.Cruiser);

        /// <summary>Lane security (U.SPC-T479): destroyers and cruisers, one hull of every group fitted with a field projector
        /// as its lane interdictor (U.SPC-T507), the rest to fight what it pulls out: groups that hold pickets on the
        /// hyperlanes raiders work.</summary>
        public static readonly BuildPlan LaneSecurity = Register("Lane security", "Destroyers and cruisers, each group with a lane interdictor's field projector: pickets on the hyperlanes", 1,
            ShipClass.Destroyer, ShipClass.Destroyer, ShipClass.Cruiser, ShipClass.Destroyer).WithGroupFittings(FacilityKinds.FieldProjector);

        public static readonly BuildPlan Mobilise = Register("Mobilise", "A balanced fleet half again as big", 1.5,
            ShipClass.Destroyer, ShipClass.Cruiser, ShipClass.Destroyer, ShipClass.Battleship, ShipClass.Destroyer, ShipClass.CommandCruiser, ShipClass.Carrier);

        public static readonly BuildPlan Merchant = Register("Merchant marine", "No new warships: the slips go to freighters and haulers", 0);

        public override string ToString() => Name;
    }

    /// <summary>
    /// A line of research a polity can put its people to (U.SPC-T423): each level costs more than the last, and what it
    /// does is its own (<see cref="OnLevel"/>, or read by the code it improves through <see cref="StrategicCommand.Level"/>).
    /// A new track is one <see cref="Register"/> call.
    /// </summary>
    public sealed class ResearchTrack
    {
        static readonly List<ResearchTrack> all = new List<ResearchTrack>();

        public static IReadOnlyList<ResearchTrack> All => all;

        public readonly string Name, Description;

        /// <summary>Points the first level costs; level n costs n times this.</summary>
        public readonly double BaseCost;

        public readonly int MaxLevel;

        /// <summary>What reaching a level does to the polity at once (null: its effect is read where it applies).</summary>
        public readonly Action<Faction, int> OnLevel;

        ResearchTrack(string name, string description, double baseCost, int maxLevel, Action<Faction, int> onLevel)
        {
            Name = name;
            Description = description;
            BaseCost = baseCost;
            MaxLevel = maxLevel;
            OnLevel = onLevel;
        }

        public static ResearchTrack Register(string name, string description, double baseCost, int maxLevel, Action<Faction, int> onLevel = null)
        {
            var t = new ResearchTrack(name, description, baseCost, maxLevel, onLevel);
            all.Add(t);
            return t;
        }

        public static ResearchTrack Find(string name) => all.Find(t => t.Name == name);

        /// <summary>Points the next level after <paramref name="level"/> costs.</summary>
        public double CostOf(int level) => BaseCost * (level + 1);

        /// <summary>Marine quality per level.</summary>
        public const double MarinesPerLevel = 0.1;

        /// <summary>Navy cap per level of shipyards (a share of it).</summary>
        public const double YardsPerLevel = 0.1;

        /// <summary>Extra facility growth a review per level of industry, for the favoured industries.</summary>
        public const double IndustryPerLevel = 0.005;

        public static readonly ResearchTrack Marines = Register("Marine training", "Better marines, man for man, in boarding and on the ground", 2000, 5,
            (f, level) => f.MarineQuality += MarinesPerLevel);

        public static readonly ResearchTrack Shipyards = Register("Shipyards", "A bigger navy for the same yards", 3000, 5);

        public static readonly ResearchTrack Industry = Register("Industry", "The favoured industries grow faster", 2500, 5);

        public override string ToString() => Name;
    }

    /// <summary>One polity's standing directives (U.SPC-T423): its production, build and research priorities and its research so far.</summary>
    public sealed class PolityDirectives
    {
        public readonly Faction Faction;
        public ProductionFocus Production = ProductionFocus.Balanced;
        public BuildPlan Build = BuildPlan.Balanced;

        /// <summary>What its people are researching, or null for nothing.</summary>
        public ResearchTrack Research;

        /// <summary>Points put into the next level of <see cref="Research"/>.</summary>
        public double ResearchPoints;

        public readonly Dictionary<ResearchTrack, int> Levels = new Dictionary<ResearchTrack, int>();

        public PolityDirectives(Faction faction) => Faction = faction;

        public int Level(ResearchTrack t) => t != null && Levels.TryGetValue(t, out int n) ? n : 0;
    }

    /// <summary>One fleet group in a polity's command report: where it is, what it is doing and what it was told to do.</summary>
    public readonly struct GroupLine
    {
        public readonly Ship Leader;
        public readonly World Where;
        public readonly string System, Order, Objective;
        public readonly int Hulls;
        public readonly double Strength;

        public GroupLine(Ship leader, World where, string system, string order, string objective, int hulls, double strength)
        {
            Leader = leader;
            Where = where;
            System = system;
            Order = order;
            Objective = objective;
            Hulls = hulls;
            Strength = strength;
        }
    }

    /// <summary>A polity at a glance (U.SPC-T423): its holdings, people and money, its navy and its wars, as the strategy layer shows it.</summary>
    public sealed class PolityReport
    {
        public Faction Faction;
        public int Systems, Ports, Warships, Groups, Commanded, Campaigns;

        /// <summary>Its groups by kind of force (U.SPC-T474, see <see cref="FleetDoctrine"/>): gate fleets and jump strike groups.</summary>
        public int GateFleets, StrikeGroups;
        public double Population, Treasury, Strength, Unrest, MarineQuality, Morale, CrewLost;
        public readonly List<Faction> AtWarWith = new List<Faction>();
        public readonly List<GroupLine> Fleet = new List<GroupLine>();
    }

    /// <summary>
    /// The strategy layer (U.SPC-T423): a polity commanded from the galaxy map. Its production focus steers which of its
    /// industries grow (<see cref="ProductionFocus"/>), its build plan what its yards lay down and how big a navy they keep
    /// (<see cref="BuildPlan"/>), and its research (<see cref="ResearchTrack"/>) accrues from its people day by day. Its fleet
    /// groups can be given objectives (take, hold, blockade, escort, explore) one squadron at a time, every group in a
    /// system, or the whole navy at once; the faction command layer (<see cref="War"/>, U.SPC-T374) carries them out ahead of
    /// its own plans, refitting first when a group is hurt, and sends a group whose objective lies in another system there
    /// through the gates (a Take in a system it is at war with goes as a campaign, <see cref="InterstellarWars"/>). Every
    /// polity runs as before until it is given a directive: these are the player's levers, not a second AI.
    /// </summary>
    [SettingsSource]
    public sealed class StrategicCommand
    {
        readonly Polities polities;
        readonly Dictionary<Faction, PolityDirectives> directives = new Dictionary<Faction, PolityDirectives>();
        readonly Dictionary<Ship, FleetObjective> objectives = new Dictionary<Ship, FleetObjective>();
        double nextDailyJd = double.NaN;

        /// <summary>The galaxy whose gates and wars carry a group to another system (null for a system on its own).</summary>
        public Galaxy Galaxy;

        /// <summary>The polity the player commands from the galaxy map, or null.</summary>
        public Faction Player;

        /// <summary>The settings menu's research pace (Civilizations): 1 is the standard pace, 0 stops research.</summary>
        public static readonly RangeSetting ResearchPace = new RangeSetting("strategy.researchPace", SettingGroups.Civilizations,
            "Research pace", 1, 0, 10, 0.25,
            "How fast a polity's people turn out research toward its chosen line (the strategy layer, galaxy map COMMAND).",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "research technology strategy polity");

        /// <summary>Research points a day a polity of a million people turns out at the standard pace (it grows as the square root of its people).</summary>
        public const double PointsPerMillion = 10;

        public StrategicCommand(Polities polities) => this.polities = polities;

        // ---------------------------------------------------------------- directives

        /// <summary><paramref name="f"/>'s directives (made the first time they are asked for).</summary>
        public PolityDirectives Of(Faction f)
        {
            if (f == null) return null;
            if (!directives.TryGetValue(f, out var d)) directives[f] = d = new PolityDirectives(f);
            return d;
        }

        /// <summary><paramref name="f"/>'s directives, or null if it has never been given any.</summary>
        public PolityDirectives Find(Faction f) => f != null && directives.TryGetValue(f, out var d) ? d : null;

        public void SetProduction(Faction f, ProductionFocus focus) => Of(f).Production = focus ?? ProductionFocus.Balanced;

        public void SetBuild(Faction f, BuildPlan plan) => Of(f).Build = plan ?? BuildPlan.Balanced;

        /// <summary>Puts <paramref name="f"/>'s people to <paramref name="track"/> (null: nothing). Points toward another track's next level are lost.</summary>
        public void SetResearch(Faction f, ResearchTrack track)
        {
            var d = Of(f);
            if (d.Research == track) return;
            d.Research = track;
            d.ResearchPoints = 0;
        }

        /// <summary>The level <paramref name="f"/> has reached in <paramref name="track"/>.</summary>
        public int Level(Faction f, ResearchTrack track) => Find(f)?.Level(track) ?? 0;

        /// <summary>What <paramref name="f"/>'s yards build (the balanced fleet unless told otherwise).</summary>
        public BuildPlan BuildPlanOf(Faction f) => Find(f)?.Build ?? BuildPlan.Balanced;

        /// <summary>The navy <paramref name="f"/> keeps, as a share of a system's cap: its build plan's share, raised by its shipyards research.</summary>
        public double NavyShare(Faction f) => BuildPlanOf(f).NavyShare * (1 + ResearchTrack.YardsPerLevel * Level(f, ResearchTrack.Shipyards));

        /// <summary>
        /// Economy hook: whether <paramref name="owner"/>'s production focus favours <paramref name="kind"/> (it grows on thinner
        /// margins and is kept up through a glut) and the extra growth its industry research gives a favoured industry.
        /// </summary>
        public (bool Favoured, double Bonus) Priority(Faction owner, IndustryKind kind)
        {
            var d = Find(owner);
            if (d == null || !d.Production.Favours(kind)) return (false, 0);
            return (true, ResearchTrack.IndustryPerLevel * d.Level(ResearchTrack.Industry));
        }

        // ---------------------------------------------------------------- objectives

        /// <summary>The objective <paramref name="leader"/>'s group holds, or null.</summary>
        public FleetObjective ObjectiveOf(Ship leader) => leader != null && objectives.TryGetValue(leader, out var o) ? o : null;

        /// <summary>Whether <paramref name="leader"/>'s group has an objective (in <paramref name="where"/>, if given).</summary>
        public bool Holds(Ship leader, World where = null) => ObjectiveOf(leader) is FleetObjective o && (where == null || o.Where == where);

        public int ObjectiveCount => objectives.Count;

        /// <summary>The groups <paramref name="f"/> commands at <paramref name="scope"/>: <paramref name="group"/>'s alone, every
        /// one of its groups in <paramref name="system"/>, or every one it has anywhere (not those inside a gate).</summary>
        public List<(Ship Leader, World Where)> Groups(Faction f, CommandScope scope, Ship group = null, World system = null)
        {
            var list = new List<(Ship, World)>();
            if (scope == CommandScope.Group)
            {
                var leader = group?.Controller;
                var w = WorldOf(leader);
                if (leader != null && w != null && Commandable(w, leader, f)) list.Add((leader, w));
                return list;
            }
            foreach (var w in polities.Worlds)
            {
                if (scope == CommandScope.System && w != system) continue;
                foreach (var s in w.Fleet.Ships)
                    if (Commandable(w, s, f)) list.Add((s, w));
            }
            return list;
        }

        /// <summary>A group <paramref name="f"/>'s command can give an objective: its own navy group's flagship, flying (not the helm's,
        /// not a supply convoy, not a corvette stationed for good at its port, U.SPC-T476).</summary>
        static bool Commandable(World w, Ship s, Faction f) =>
            !s.Destroyed && !s.IsEscort && !s.Derelict && s.Faction == f && s.Spec.IsMilitary && !s.Convoy && !Corvettes.Stationed(s) && s != w.PilotedShip &&
            (w.Holdings == null || !w.Holdings.Owns(s));

        /// <summary>The world <paramref name="ship"/> is in, or null (inside a gate, or gone).</summary>
        public World WorldOf(Ship ship)
        {
            if (ship == null) return null;
            foreach (var w in polities.Worlds)
                if (w.Fleet.IsPresent(ship)) return w;
            return null;
        }

        /// <summary>The star system <paramref name="w"/> is, when there is a galaxy.</summary>
        public StarSystem SystemOf(World w)
        {
            if (Galaxy == null || w == null) return null;
            foreach (var s in Galaxy.Systems)
                if (s.World == w) return s;
            return null;
        }

        string NameOf(World w) => SystemOf(w)?.Name ?? "the system";

        /// <summary>
        /// Gives <paramref name="kind"/> in <paramref name="target"/> to <paramref name="f"/>'s groups at <paramref name="scope"/>
        /// (see <see cref="Groups"/>). <paramref name="body"/> picks the port or place (-1: command picks: every enemy port for a
        /// Take, the busiest enemy port for a Blockade, its own biggest port for a Hold, every world for an Explore);
        /// <paramref name="charge"/> is the ship an Escort shadows (null: its own biggest merchant hull there). Returns null and
        /// how many groups took it, or why it cannot be given.
        /// </summary>
        public string Order(Faction f, CommandScope scope, Ship group, World system, ObjectiveKind kind, World target, double jd, out int given,
            int body = -1, Ship charge = null)
        {
            given = 0;
            if (f == null || target == null) return "No polity or no system to command";
            var why = Resolve(f, kind, target, ref body, ref charge);
            if (why != null) return why;
            var groups = Groups(f, scope, group, system);
            if (groups.Count == 0) return "The " + f.Name + " has no fleet group there to send";
            Ship firstGiven = null;
            foreach (var (leader, where) in groups)
            {
                if (kind == ObjectiveKind.Escort && leader == charge) continue;
                var o = new FleetObjective { Kind = kind, Where = target, Body = body, Charge = charge, SinceJd = jd };
                objectives[leader] = o;
                firstGiven ??= leader;
                given++;
                if (where == target)
                {
                    // In port: it takes the order up now, not when its stay is up.
                    if (leader.Phase == ShipPhase.Parked && leader.Battle == null && leader.Voyage == null) where.Fleet.ScheduleNext(leader, jd);
                }
                else Dispatch(leader, where, o, jd);
            }
            if (given == 0) return "No group there could be given it";

            target.Log(jd, $"The {f.Name} orders {(given == 1 ? "the " + firstGiven.Name : given + " groups")} to {Describe(kind, target, body, charge).ToLowerInvariant()}",
                WorldEventKind.Diplomacy, body >= 0 ? body : -1);
            return null;
        }

        /// <summary>Clears every objective of <paramref name="f"/>'s groups at <paramref name="scope"/>: they go back to their own command's plans.</summary>
        public int Clear(Faction f, CommandScope scope, Ship group = null, World system = null)
        {
            int n = 0;
            foreach (var (leader, _) in Groups(f, scope, group, system))
                if (objectives.Remove(leader)) n++;
            return n;
        }

        string Resolve(Faction f, ObjectiveKind kind, World w, ref int body, ref Ship charge)
        {
            var dip = polities.Diplomacy;
            switch (kind)
            {
                case ObjectiveKind.Take:
                    if (body >= 0)
                    {
                        foreach (var s in w.Economy.At(body))
                            if (Enemy(f, s)) return null;
                        return "Nothing at " + w.Catalog[body].Name + " is held by an enemy of the " + f.Name;
                    }
                    foreach (var s in w.Economy.Settlements)
                        if (Enemy(f, s) && s.Kind != SettlementKind.Haven) return null;
                    return "The " + f.Name + " is at war with nobody holding a port in " + NameOf(w);
                case ObjectiveKind.Blockade:
                {
                    if (body >= 0)
                    {
                        foreach (var s in w.Economy.At(body))
                            if (Blockadable(f, s)) return null;
                        return "Nobody hostile to the " + f.Name + " keeps a port at " + w.Catalog[body].Name;
                    }
                    Settlement best = null;
                    foreach (var s in w.Economy.Settlements)
                        if (Blockadable(f, s) && (best == null || s.Population > best.Population)) best = s;
                    if (best == null) return "Nobody hostile to the " + f.Name + " keeps a port in " + NameOf(w);
                    body = best.Body;
                    return null;
                }
                case ObjectiveKind.Hold:
                {
                    if (body >= 0) return null;
                    Settlement own = null, friendly = null;
                    foreach (var s in w.Economy.Settlements)
                    {
                        if (s.Fallen || s.Kind == SettlementKind.Haven) continue;
                        if (s.Owner == f && (own == null || s.Population > own.Population)) own = s;
                        else if (!dip.Hostile(f, s.Owner) && (friendly == null || s.Population > friendly.Population)) friendly = s;
                    }
                    body = (own ?? friendly)?.Body ?? w.Catalog.Root.Index;
                    return null;
                }
                case ObjectiveKind.Escort:
                {
                    if (charge == null)
                    {
                        foreach (var s in w.Fleet.Ships)
                            if (!s.Destroyed && !s.IsEscort && !s.Derelict && s.Faction == f && w.IsTrader(s) &&
                                (charge == null || s.CapacityTonnes > charge.CapacityTonnes))
                                charge = s;
                        if (charge == null) return "The " + f.Name + " has no merchant hull in " + NameOf(w) + " to escort";
                    }
                    if (charge.Destroyed || dip.Hostile(f, charge.Faction)) return "The " + charge.Name + " is no ship of a friend";
                    return null;
                }
                default:
                    return null;
            }
        }

        /// <summary>Whether <paramref name="f"/> may take <paramref name="s"/>: a standing port of a state it is at war with, or a raider haven.</summary>
        public bool Enemy(Faction f, Settlement s) =>
            s != null && !s.Fallen && s.Owner != null && s.Owner != f &&
            (s.Owner.IsPirate ? s.Kind == SettlementKind.Haven : polities.Diplomacy.Get(f, s.Owner) == Relation.War);

        /// <summary>Whether <paramref name="f"/> may blockade <paramref name="s"/>: a standing port (not a haven) of someone hostile to it.</summary>
        public bool Blockadable(Faction f, Settlement s) =>
            s != null && !s.Fallen && s.Kind != SettlementKind.Haven && s.Owner != null && s.Owner != f && !s.Owner.IsPirate && polities.Diplomacy.Hostile(f, s.Owner);

        /// <summary>Ends <paramref name="leader"/>'s objective, logging why: the group goes back to its own command's plans.</summary>
        public void Complete(Ship leader, World where, double jd, string why)
        {
            if (leader == null || !objectives.Remove(leader)) return;
            if (why != null) where?.Log(jd, why, WorldEventKind.Diplomacy);
        }

        /// <summary>A Take achieved: the group holds what it took.</summary>
        public void Taken(Ship leader, World where, int body, double jd)
        {
            var o = ObjectiveOf(leader);
            if (o == null) return;
            o.Kind = ObjectiveKind.Hold;
            o.Body = body >= 0 ? body : where.Catalog.Root.Index;
            o.Campaign = null;
            where.Log(jd, $"The {leader.Name}'s group has taken its objective in {NameOf(where)} and holds {where.Catalog[o.Body].Name}", WorldEventKind.Capture, o.Body);
        }

        /// <summary>A flagship lost (or handed over): its heir carries the group's objective on.</summary>
        public void OnSuccession(Ship dead, Ship heir)
        {
            if (dead == null || heir == null || !objectives.TryGetValue(dead, out var o)) return;
            objectives.Remove(dead);
            objectives[heir] = o;
        }

        // ---------------------------------------------------------------- the day

        /// <summary>Once a day: research accrues, objectives whose group is gone lapse, and groups bound for another system are sent.</summary>
        public void Update(double jd)
        {
            if (double.IsNaN(nextDailyJd)) nextDailyJd = jd;
            while (jd >= nextDailyJd)
            {
                Daily(nextDailyJd);
                nextDailyJd += 1;
            }
        }

        void Daily(double jd)
        {
            foreach (var d in directives.Values) Research(d, jd);
            List<Ship> gone = null;
            foreach (var kv in objectives)
            {
                var leader = kv.Key;
                var o = kv.Value;
                if (leader.Destroyed || leader.IsEscort || leader.Derelict || !leader.Spec.IsMilitary)
                {
                    (gone ??= new List<Ship>()).Add(leader);
                    continue;
                }
                var where = WorldOf(leader);
                if (where == null) continue;
                if (o.Kind == ObjectiveKind.Escort)
                {
                    var charge = o.Charge;
                    if (charge == null || charge.Destroyed || charge.Derelict || polities.Diplomacy.Hostile(leader.Faction, charge.Faction))
                    {
                        (gone ??= new List<Ship>()).Add(leader);
                        where.Log(jd, $"The {leader.Name}'s group has lost its charge, the {charge?.Name ?? "ship"}, and stands down", WorldEventKind.Info);
                        continue;
                    }
                    // A charge gone through the gates is followed to where it now is.
                    var chargeIn = WorldOf(charge.Controller);
                    if (chargeIn != null) o.Where = chargeIn;
                }
                if (o.Campaign != null)
                {
                    if (o.Campaign.Active) continue;
                    // The campaign it was handed to is over: what it took, it holds.
                    o.Campaign = null;
                    if (o.Kind == ObjectiveKind.Take && !AnyEnemy(leader.Faction, o.Where, o.Body))
                    {
                        Taken(leader, o.Where, o.Body, jd);
                        continue;
                    }
                    (gone ??= new List<Ship>()).Add(leader);
                    continue;
                }
                if (where != o.Where) Dispatch(leader, where, o, jd);
            }
            if (gone != null)
                foreach (var s in gone) objectives.Remove(s);
        }

        bool AnyEnemy(Faction f, World w, int body)
        {
            foreach (var s in w.Economy.Settlements)
                if ((body < 0 || s.Body == body) && Enemy(f, s) && (body >= 0 || s.Kind != SettlementKind.Haven)) return true;
            return false;
        }

        void Research(PolityDirectives d, double jd)
        {
            var track = d.Research;
            if (track == null) return;
            int level = d.Level(track);
            if (level >= track.MaxLevel) return;
            double people = 0;
            foreach (var s in polities.SettlementsOf(d.Faction)) people += s.Population;
            d.ResearchPoints += ResearchPace.Value * PointsPerMillion * Math.Sqrt(Math.Max(0, people) / 1e6);
            double cost = track.CostOf(level);
            if (d.ResearchPoints < cost) return;
            d.ResearchPoints -= cost;
            d.Levels[track] = level + 1;
            track.OnLevel?.Invoke(d.Faction, level + 1);
            var home = polities.CapitalSystemOf(d.Faction) ?? polities.HomeOf(d.Faction);
            home?.Log(jd, $"The {d.Faction.Name} reaches {track.Name.ToLowerInvariant()} level {level + 1}: {track.Description.ToLowerInvariant()}", WorldEventKind.Info);
            if (level + 1 >= track.MaxLevel) d.ResearchPoints = 0;
        }

        /// <summary>Sends a group whose objective lies in another system there: as a campaign if it is to take a system it is at
        /// war over, else through the open gates (or under its own drive).</summary>
        void Dispatch(Ship leader, World from, FleetObjective o, double jd)
        {
            var traffic = Galaxy?.Traffic;
            var fromSystem = SystemOf(from);
            var toSystem = SystemOf(o.Where);
            if (traffic == null || fromSystem == null || toSystem == null) return;
            // Already on its way there under this command: it goes on; on its way somewhere else: turned about.
            if (leader.Voyage != null)
            {
                if (leader.Voyage.Purpose != VoyagePurpose.Directed || leader.Voyage.Destination == toSystem) return;
                traffic.Redirect(fromSystem, leader, toSystem, jd);
                return;
            }
            if (!InterstellarTraffic.Idle(from, leader)) return;
            if (o.Kind == ObjectiveKind.Take && Galaxy.Wars != null)
            {
                var c = Galaxy.Wars.Launch(leader, fromSystem, toSystem, jd);
                if (c != null)
                {
                    o.Campaign = c;
                    return;
                }
            }
            if (traffic.SendDirected(leader, fromSystem, toSystem, jd) != null)
                from.Log(jd, $"The {leader.Name} leads its group out for {toSystem.Name} on the {leader.Faction.Name}'s orders", WorldEventKind.Info, from.Catalog.Root.Index);
        }

        // ---------------------------------------------------------------- the picture

        /// <summary>What <paramref name="kind"/> in <paramref name="w"/> means, in words.</summary>
        public string Describe(ObjectiveKind kind, World w, int body, Ship charge)
        {
            string place = body >= 0 ? w.Catalog[body].Name : NameOf(w);
            switch (kind)
            {
                case ObjectiveKind.Take: return body >= 0 ? "Take " + place : "Take every enemy port in " + place;
                case ObjectiveKind.Hold: return "Hold " + place;
                case ObjectiveKind.Blockade: return "Blockade " + place;
                case ObjectiveKind.Escort: return "Escort the " + (charge?.Name ?? "convoy");
                default: return body >= 0 ? "Explore " + place : "Explore " + place;
            }
        }

        public string Describe(FleetObjective o) => o == null ? "" :
            Describe(o.Kind, o.Where, o.Body, o.Charge) + (o.Campaign != null && o.Campaign.Active ? " (on campaign)" : "");

        /// <summary>What a group's current order is, in words.</summary>
        public static string DescribeOrder(GroupOrder o, BodyCatalog catalog)
        {
            if (o == null) return "awaiting orders";
            string where = o.Body >= 0 && catalog != null && o.Body < catalog.Count ? catalog[o.Body].Name : "";
            switch (o.Kind)
            {
                case OrderKind.Ambush: return "lying in wait on the lanes";
                case OrderKind.Attack: return "attacking " + where;
                case OrderKind.Defend: return "holding " + where;
                case OrderKind.Siege: return "besieging " + where;
                case OrderKind.Repair: return "refitting at " + where;
                case OrderKind.Raid: return "raiding at " + where;
                case OrderKind.Hunt: return "hunting raiders at " + where;
                case OrderKind.Assault: return "storming " + where;
                case OrderKind.GateWatch: return "lying off the gates";
                case OrderKind.HoldGate: return "holding the gates";
                case OrderKind.Blockade: return "blockading " + where;
                case OrderKind.Escort: return "escorting, at " + where;
                case OrderKind.Explore: return "exploring " + where;
                default: return "patrolling, next " + where;
            }
        }

        /// <summary><paramref name="f"/> at a glance: what it holds, its people, money and navy, its wars, and every group's orders.</summary>
        public PolityReport Report(Faction f, double jd)
        {
            var r = new PolityReport { Faction = f };
            if (f == null) return r;
            r.Treasury = f.Treasury;
            r.MarineQuality = f.MarineQuality;
            r.Morale = f.CrewMorale;
            r.CrewLost = f.CrewLost;
            r.Systems = polities.SystemsHeldBy(f).Count;
            double unrestWeight = 0;
            foreach (var s in polities.SettlementsOf(f))
            {
                if (s.Kind == SettlementKind.Haven && !f.IsPirate) continue;
                r.Ports++;
                r.Population += s.Population;
                r.Unrest += s.Unrest * s.Population;
                unrestWeight += s.Population;
            }
            r.Unrest = unrestWeight > 0 ? r.Unrest / unrestWeight : 0;
            foreach (var g in polities.Factions)
                if (g != f && !g.IsPirate && polities.Diplomacy.Get(f, g) == Relation.War) r.AtWarWith.Add(g);
            foreach (var w in polities.Worlds)
            {
                string name = NameOf(w);
                foreach (var s in w.Fleet.Ships)
                {
                    if (s.Destroyed || s.Faction != f || !s.Spec.IsMilitary) continue;
                    r.Warships++;
                    r.Strength += s.Combat.Strength;
                    if (s.IsEscort || s.Derelict) continue;
                    r.Groups++;
                    int hulls = 1;
                    double strength = s.Combat.Strength;
                    foreach (var e in w.Fleet.Ships)
                        if (e.Leader == s && !e.Destroyed)
                        {
                            hulls++;
                            strength += e.Combat.Strength;
                        }
                    var o = ObjectiveOf(s);
                    if (o != null) r.Commanded++;
                    if (!s.PatrolWing && s.PicketGate == null)
                    {
                        if (FleetDoctrine.IsStrikeGroup(w, s)) r.StrikeGroups++;
                        else r.GateFleets++;
                    }
                    string order = s.Voyage != null ? "bound for " + (s.Voyage.Destination?.Name ?? "another system") : DescribeOrder(w.War?.OrderOf(s), w.Catalog);
                    r.Fleet.Add(new GroupLine(s, w, name, order, Describe(o), hulls, strength));
                }
            }
            if (Galaxy?.Wars != null)
                foreach (var c in Galaxy.Wars.Campaigns)
                    if (c.Active && c.Faction == f) r.Campaigns++;
            return r;
        }
    }
}
