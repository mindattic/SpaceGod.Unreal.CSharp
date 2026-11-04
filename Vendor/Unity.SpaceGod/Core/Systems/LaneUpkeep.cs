using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// How a hyperlane wears and what its wear does (U.SPC-T481), every rate a setting in Settings > Simulation. A lane's
    /// condition (<see cref="Hyperlanes.Condition"/>) runs from 1, fully tended, down by <see cref="DecayPerDay"/> a day and
    /// is brought back up by <see cref="TendPerDay"/> a day for each tender working it. Between full repair and
    /// <see cref="FailAt"/> the lane rides ever slower (down to <see cref="SlowestSpeed"/> of its pace) and less reliably (down
    /// to <see cref="WorstReliability"/> of rides going through without a stall of about <see cref="StallDays"/>); at
    /// <see cref="FailAt"/> it fails and carries nothing until tenders bring it back above it.
    /// </summary>
    [SettingsSource]
    public static class LaneCondition
    {
        static string Share(double v) => (v * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

        static string Days(double v) => v.ToString("0.##", CultureInfo.InvariantCulture) + " d";

        /// <summary>How much of its condition a lane loses a day with nobody tending it.</summary>
        public static readonly RangeSetting DecayPerDay = new RangeSetting("lanes.decayPerDay", SettingGroups.Simulation,
            "Lane wear", 0.02, 0, 0.2, 0.005,
            "How much of its condition a hyperlane loses each day nobody tends it (its accelerator rings drifting out of power and " +
            "alignment). At 2% an untended lane fails in a little over a month.",
            Share, "lane lanes hyperlane wear decay condition tender upkeep maintenance");

        /// <summary>How much condition each tender working a lane restores a day.</summary>
        public static readonly RangeSetting TendPerDay = new RangeSetting("lanes.tendPerDay", SettingGroups.Simulation,
            "Lane tending", 0.1, 0, 1, 0.01,
            "How much of a hyperlane's condition each lane tender working it (riding it, or lying over the rings at either end) " +
            "restores a day.", Share, "lane lanes hyperlane tender tending repair upkeep maintenance");

        /// <summary>The condition at or below which a lane fails and carries nothing.</summary>
        public static readonly RangeSetting FailAt = new RangeSetting("lanes.failAt", SettingGroups.Simulation,
            "Lane failure point", 0.25, 0.05, 0.6, 0.05,
            "The condition at or below which a hyperlane fails: its ring gates go dark, nothing can join it, and traffic and trade " +
            "go round it (or fly under their own drives) until tenders bring it back.", Share, "lane lanes hyperlane fail failure condition");

        /// <summary>The share of its full pace a lane rides at just before it fails.</summary>
        public static readonly RangeSetting SlowestSpeed = new RangeSetting("lanes.slowestSpeed", SettingGroups.Simulation,
            "Worn lane speed", 0.4, 0.1, 1, 0.05,
            "The share of its full pace a hyperlane rides at when it is worn right down to its failure point; in between it slows " +
            "in proportion to its wear.", Share, "lane lanes hyperlane speed worn condition");

        /// <summary>The chance a ride goes through without a stall on a lane just before it fails.</summary>
        public static readonly RangeSetting WorstReliability = new RangeSetting("lanes.worstReliability", SettingGroups.Simulation,
            "Worn lane reliability", 0.6, 0, 1, 0.05,
            "The chance a ride goes through without a stall on a hyperlane worn right down to its failure point. A stalled ride " +
            "drops out part-way when the rings slip out of alignment and picks up again a while later.", Share,
            "lane lanes hyperlane reliability stall worn condition");

        /// <summary>How long a stalled ride waits off the lane before it picks up again, days (about: a stall varies by half either way).</summary>
        public static readonly RangeSetting StallDays = new RangeSetting("lanes.stallDays", SettingGroups.Simulation,
            "Lane stall", 0.3, 0, 3, 0.05,
            "About how long (days) a ride stalled by a worn hyperlane's misaligned rings waits beside the lane before it picks up again.",
            Days, "lane lanes hyperlane stall delay worn");

        /// <summary>Whether a lane in condition <paramref name="condition"/> still carries traffic: above <see cref="FailAt"/>.</summary>
        public static bool Carries(double condition) => condition > FailAt.Value;

        /// <summary>How worn a lane in condition <paramref name="condition"/> is, 0 in full repair to 1 at <see cref="FailAt"/> and below.</summary>
        public static double Wear(double condition)
        {
            double span = Math.Max(1e-6, 1 - FailAt.Value);
            return Math.Max(0, Math.Min(1, (1 - condition) / span));
        }

        /// <summary>The share of its full pace a lane in condition <paramref name="condition"/> rides at.</summary>
        public static double SpeedFactor(double condition) => 1 - (1 - SlowestSpeed.Value) * Wear(condition);

        /// <summary>The chance a ride of a lane in condition <paramref name="condition"/> goes through without a stall.</summary>
        public static double Reliability(double condition) => 1 - (1 - WorstReliability.Value) * Wear(condition);
    }

    /// <summary>
    /// The lane tenders of one system and the upkeep of its hyperlanes (U.SPC-T481). Every lane wears
    /// (<see cref="LaneCondition"/>) and is kept up by tenders, unarmed maintenance hulls (<see cref="ShipClass.LaneTender"/>)
    /// that shuttle between its two ramps and lie over the rings at each end. Whoever keeps a lane's ramps
    /// (<see cref="World.LaneKeeper"/>, the owner of the biggest settlement at either end) raises its tenders: at the start
    /// <see cref="TendersPerLane"/> of them, then a new hull from one of its yards whenever one is lost
    /// (<see cref="OrderAt"/>). A lane with none wears out and fails, and routes and trade go round it; so tenders matter in
    /// peace and are a prime mark in war: raiders value them highly (<see cref="TenderValue"/>, see
    /// <see cref="RaidDoctrine.PreyValue"/>) and destroy rather than rob them, every gunner favours them
    /// (<see cref="TenderFocus"/>), and navies at war and raiders go out of their way to fall on them (<see cref="HuntTarget"/>).
    /// </summary>
    [SettingsSource]
    public sealed class LaneUpkeep
    {
        /// <summary>How many tenders a polity keeps on each lane it keeps.</summary>
        public static readonly RangeSetting TendersPerLane = new RangeSetting("lanes.tendersPerLane", SettingGroups.Simulation,
            "Tenders per lane", 1, 0, 4, 1,
            "How many lane tenders a polity keeps working each hyperlane whose ramps it keeps, building a new one at its yards when " +
            "one is lost. At 0 nobody tends the lanes and they all fail in time.", null, "lane lanes hyperlane tender tenders upkeep");

        /// <summary>What a lane tender is worth to raiders, credits.</summary>
        public static readonly RangeSetting TenderValue = new RangeSetting("lanes.tenderValue", SettingGroups.Simulation,
            "Tender bounty", 2e7, 0, 1e8, 1e6,
            "What a lane tender is worth to raiders (credits, against a freighter's hold of about 5 million): they take on more " +
            "guns for one, and destroy it rather than rob it, to cut a system's lanes.",
            v => (v / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + "M cr", "lane tender bounty raiders pirates target value");

        /// <summary>How strongly a hostile gunner favours a lane tender over the other targets in a fight.</summary>
        public static readonly RangeSetting TenderFocus = new RangeSetting("lanes.tenderFocus", SettingGroups.Simulation,
            "Tender targeting", 20, 0, 60, 1,
            "How strongly warships and raiders in a fight favour a lane tender over other targets (as if it were that many km " +
            "nearer than it is).", null, "lane tender target targeting focus battle war");

        /// <summary>How far, in days of travel, a navy at war or a raider band goes to fall on a hostile lane tender.</summary>
        public static readonly RangeSetting HuntDays = new RangeSetting("lanes.huntDays", SettingGroups.Simulation,
            "Tender hunting range", 5, 0, 30, 0.5,
            "How far (days of travel) a navy at war, or a raider band, goes out of its way to fall on a hostile lane tender. At 0 " +
            "nobody hunts them.", Days, "lane tender hunt hunting war raiders navy range");

        /// <summary>The chance a raider band planning its next move goes after a tender in reach rather than its other plans.</summary>
        public static readonly RangeSetting RaiderHuntChance = new RangeSetting("lanes.raiderHuntChance", SettingGroups.Simulation,
            "Raiders hunt tenders", 0.5, 0, 1, 0.05,
            "The chance a raider band planning its next move goes after a hostile lane tender in reach rather than lie in wait or " +
            "raid a port.", v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "lane tender raiders pirates hunt");

        /// <summary>How long a tender lies over the rings at each end of its lane, days (about: it varies by a quarter either way).</summary>
        public static readonly RangeSetting DwellDays = new RangeSetting("lanes.tenderDwellDays", SettingGroups.Simulation,
            "Tender work at the ramps", 1.5, 0.1, 10, 0.1,
            "About how long (days) a lane tender lies over the rings at each end of its lane before riding it back to the other.",
            Days, "lane tender dwell ramp work");

        static string Days(double v) => v.ToString("0.##", CultureInfo.InvariantCulture) + " d";

        /// <summary>The names a polity gives its tenders, numbered on as they run out.</summary>
        public static readonly string[] Names =
        {
            "Steady Hand", "True Line", "Lamplighter", "Ringwright", "Plumb Line", "Long Watch", "Quiet Work", "Linesman",
            "Patient Light", "Fair Passage", "Keeps Faith", "Even Keel", "Bright Rings", "Wayfinder", "Good Order", "Sure Footing",
        };

        readonly World world;
        readonly Random rng;

        /// <summary>The lane each tender works.</summary>
        public readonly Dictionary<Ship, Lane> Tending = new Dictionary<Ship, Lane>();

        /// <summary>Who keeps each lane, as of the last look (see <see cref="OwnerOf"/>).</summary>
        readonly Dictionary<Lane, Faction> owners = new Dictionary<Lane, Faction>();

        double lastJd = double.NaN, ownersJd = double.NaN;
        bool raised;
        int named;

        /// <summary>Raised when a lane fails (true) or is brought back into service (false).</summary>
        public event Action<Lane, bool, double> LaneChanged;

        public LaneUpkeep(World world, int seed)
        {
            this.world = world;
            rng = new Random(seed);
        }

        Hyperlanes Lanes => world.Fleet.Lanes;

        /// <summary>Whether <paramref name="ship"/> is a lane tender.</summary>
        public static bool IsTender(Ship ship) => ship != null && ship.Spec != null && ship.Spec.Class == ShipClass.LaneTender;

        /// <summary>The lane <paramref name="tender"/> works, or null.</summary>
        public Lane LaneOf(Ship tender) => tender != null && Tending.TryGetValue(tender, out var lane) ? lane : null;

        /// <summary>Who keeps <paramref name="lane"/> and raises its tenders: the keeper of its ramps at one end or the other
        /// (<see cref="World.LaneKeeper"/>), never a pirate clan; null when nobody holds either end.</summary>
        public Faction OwnerOf(Lane lane)
        {
            if (lane == null) return null;
            if (owners.TryGetValue(lane, out var f)) return f;
            f = world.LaneKeeper(lane.A) ?? world.LaneKeeper(lane.B);
            if (f != null && f.IsPirate) f = null;
            owners[lane] = f;
            return f;
        }

        /// <summary>Whether <paramref name="ship"/> is still a working hull (not lost, a derelict or gone through a gate).</summary>
        static bool Alive(Ship ship) => ship != null && !ship.Destroyed && !ship.Derelict && !ship.GateDeparted;

        /// <summary>How many working tenders are on <paramref name="lane"/>.</summary>
        public int TendersOn(Lane lane)
        {
            int n = 0;
            foreach (var kv in Tending)
                if (kv.Value == lane && Alive(kv.Key)) n++;
            return n;
        }

        /// <summary>Whether <paramref name="tender"/> is working <paramref name="lane"/> now: riding it (or stalled beside it), or
        /// in orbit at either end, lying over its rings. Not in a fight.</summary>
        public bool Working(Ship tender, Lane lane)
        {
            if (!Alive(tender) || tender.Battle != null || lane == null) return false;
            var lanes = Lanes;
            if (tender.Phase == ShipPhase.Lane || tender.Phase == ShipPhase.Lurk || tender.Phase == ShipPhase.Adrift)
                return tender.LaneLeg.Lane == lane;
            if (tender.Phase != ShipPhase.Parked || tender.Orbit == null || lanes == null) return false;
            int system = lanes.SystemOf(tender.Orbit.Body);
            return system == lane.A || system == lane.B;
        }

        /// <summary>
        /// Wears every lane down for the time gone by, and lets each tender working one bring it back; logs a lane failing or
        /// coming back into service. The first call raises the system's tenders (<see cref="Raise"/>).
        /// </summary>
        public void Update(double jd)
        {
            var lanes = Lanes;
            if (lanes == null) return;
            if (double.IsNaN(ownersJd) || jd - ownersJd >= 1 || jd < ownersJd)
            {
                owners.Clear();
                ownersJd = jd;
            }
            if (!raised)
            {
                raised = true;
                Raise(jd);
            }
            if (double.IsNaN(lastJd) || jd <= lastJd)
            {
                if (double.IsNaN(lastJd)) lastJd = jd;
                return;
            }
            double dt = Math.Min(jd - lastJd, 30);
            lastJd = jd;
            var working = new Dictionary<Lane, int>();
            foreach (var kv in Tending)
                if (Working(kv.Key, kv.Value))
                    working[kv.Value] = (working.TryGetValue(kv.Value, out int n) ? n : 0) + 1;
            foreach (var lane in lanes.Lanes)
            {
                working.TryGetValue(lane, out int tenders);
                double before = lanes.ConditionOf(lane);
                double after = before + (tenders * LaneCondition.TendPerDay.Value - LaneCondition.DecayPerDay.Value) * dt;
                if (after >= 1 && before >= 1) continue;
                bool carried = lanes.Carries(lane);
                lanes.SetCondition(lane, after);
                bool carries = lanes.Carries(lane);
                if (carried == carries) continue;
                if (!carries)
                    world.Log(jd, $"The {lane.Name} lane has failed: its rings have gone dark for want of tending, and traffic goes round it",
                        WorldEventKind.Loss, lane.A);
                else
                    world.Log(jd, $"Tenders have the {lane.Name} lane's rings lit and aligned again: it is back in service", WorldEventKind.Info, lane.A);
                LaneChanged?.Invoke(lane, !carries, jd);
            }
        }

        /// <summary>
        /// Raises <see cref="TendersPerLane"/> tenders for every lane someone keeps, each stationed at one end of its lane.
        /// Returns them.
        /// </summary>
        public List<Ship> Raise(double jd)
        {
            var made = new List<Ship>();
            var lanes = Lanes;
            if (lanes == null) return made;
            foreach (var lane in lanes.Lanes)
            {
                var owner = OwnerOf(lane);
                if (owner == null) continue;
                for (int k = TendersOn(lane); k < TendersPerLane.IntValue; k++)
                    made.Add(Commission(owner, lane, made.Count % 2 == 0 ? lane.A : lane.B, jd, null));
            }
            return made;
        }

        Ship Commission(Faction owner, Lane lane, int body, double jd, ParkingOrbit orbit)
        {
            string name = Names[named % Names.Length] + (named >= Names.Length ? " " + (named / Names.Length + 1) : "");
            named++;
            var ship = world.Fleet.AddShip(name, ShipClass.LaneTender, owner, ShipRole.Tender);
            ship.Ports = new[] { lane.A, lane.B };
            ship.Cargo = "Tending the " + lane.Name + " lane";
            Tending[ship] = lane;
            world.Fleet.Station(ship, body, jd, orbit);
            return ship;
        }

        /// <summary>The lane of <paramref name="owner"/>'s most in need of a tender (fewest against <see cref="TendersPerLane"/>,
        /// then the most worn), or null when every one it keeps has its tenders.</summary>
        public Lane ShortOfTenders(Faction owner)
        {
            var lanes = Lanes;
            if (owner == null || lanes == null) return null;
            Lane best = null;
            int bestShort = 0;
            double bestCondition = double.MaxValue;
            foreach (var lane in lanes.Lanes)
            {
                if (OwnerOf(lane) != owner) continue;
                int shortBy = TendersPerLane.IntValue - TendersOn(lane);
                if (shortBy <= 0) continue;
                double c = lanes.ConditionOf(lane);
                if (shortBy > bestShort || (shortBy == bestShort && c < bestCondition))
                {
                    best = lane;
                    bestShort = shortBy;
                    bestCondition = c;
                }
            }
            return best;
        }

        /// <summary>Yard hook: a lane tender when the yard's owner keeps a lane short of its tenders, else null (the yard
        /// builds what it would have). Lanes come first: everything else rides them.</summary>
        public ShipClass? OrderAt(Settlement yard)
        {
            if (yard?.Owner == null || yard.Owner.IsPirate || !raised) return null;
            return ShortOfTenders(yard.Owner) != null ? ShipClass.LaneTender : (ShipClass?)null;
        }

        /// <summary>A yard has finished a tender: it goes to the lane of its owner's most in need. Null if none is short now.</summary>
        public Ship Launch(Settlement yard, double jd)
        {
            var lane = ShortOfTenders(yard?.Owner);
            if (lane == null) return null;
            var ship = Commission(yard.Owner, lane, yard.Body, jd, world.Infrastructure?.YardOf(yard)?.Orbit);
            world.Log(jd, $"{yard.Name} launches the lane tender {ship.Name} for the upkeep of the {lane.Name} lane", WorldEventKind.Launch, yard.Body);
            return ship;
        }

        /// <summary>
        /// Fleet hook: where a tender goes next. To the other end of its lane; off it (a new hull leaving its yard), to the
        /// nearer end; and with its lane failed, it stays at the end it is at, working the rings until the lane is back. A
        /// tender that has lost its lane (its lane's keeper gone) takes on another of its polity's short of one.
        /// </summary>
        public int ChooseNext(Ship tender, int here, double jd)
        {
            var lanes = Lanes;
            var lane = LaneOf(tender);
            if (lane == null || OwnerOf(lane) != tender.Faction)
            {
                var other = ShortOfTenders(tender.Faction);
                if (other != null)
                {
                    lane = other;
                    Tending[tender] = lane;
                    tender.Ports = new[] { lane.A, lane.B };
                    tender.Cargo = "Tending the " + lane.Name + " lane";
                }
            }
            if (lane == null || lanes == null) return here;
            int system = lanes.SystemOf(here);
            bool atEnd = system == lane.A || system == lane.B;
            if (!lanes.Carries(lane)) return atEnd ? here : Nearer(lane, here, jd);
            if (system == lane.A) return lane.B;
            if (system == lane.B) return lane.A;
            return Nearer(lane, here, jd);
        }

        int Nearer(Lane lane, int here, double jd)
        {
            var e = world.Ephemeris;
            var at = e.HelioAt(here, jd);
            return (e.HelioAt(lane.A, jd) - at).Length <= (e.HelioAt(lane.B, jd) - at).Length ? lane.A : lane.B;
        }

        /// <summary>How long a tender lies over the rings at a ramp.</summary>
        public double Dwell(Ship tender) => DwellDays.Value * (0.75 + rng.NextDouble() * 0.5);

        /// <summary>
        /// Fleet hook (<see cref="Fleet.LaneJoined"/>): a ride just joined stalls part-way with the chance its lane's wear
        /// gives (<see cref="Hyperlanes.Reliability"/>). A hull at the helm is the pilot's to ride, and never stalls.
        /// </summary>
        public void OnLaneJoined(Ship ship, LaneHop hop, double jd)
        {
            var lanes = Lanes;
            if (lanes == null || ship == null || ship == world.PilotedShip) return;
            if (world.Fleet.IsHeld != null && world.Fleet.IsHeld(ship)) return;
            double reliability = lanes.Reliability(hop.Lane);
            if (reliability >= 1 || rng.NextDouble() < reliability) return;
            double days = LaneCondition.StallDays.Value * (0.5 + rng.NextDouble());
            if (days <= 0) return;
            world.Fleet.StallRide(ship, 0.2 + rng.NextDouble() * 0.6, days);
        }

        /// <summary>
        /// Where <paramref name="hunter"/>'s group goes to fall on a hostile lane tender (U.SPC-T481): the place the nearest one
        /// is at, or bound for, within <see cref="HuntDays"/>; -1 when none is in reach. A navy hunts only the tenders of a
        /// power it is at war with; a raider band, anyone's.
        /// </summary>
        public int HuntTarget(Ship hunter, int here, double jd)
        {
            if (hunter == null || HuntDays.Value <= 0) return -1;
            var f = hunter.Faction;
            int best = -1;
            double bestDays = HuntDays.Value;
            foreach (var t in Tending.Keys)
            {
                if (!Alive(t) || t.Battle != null || t.Faction == null || t.Faction == f) continue;
                if (!world.Diplomacy.Hostile(f, t.Faction)) continue;
                if (!f.IsPirate && (t.Faction.IsPirate || world.War == null || !world.War.AtWar(f))) continue;
                int body = t.Phase == ShipPhase.Parked && t.Orbit != null ? t.Orbit.Body : t.DestinationBody;
                if (body < 0 || body == here) continue;
                double days = world.Fleet.EstimateTravelDays(hunter, here, body, jd);
                if (days <= bestDays)
                {
                    bestDays = days;
                    best = body;
                }
            }
            return best;
        }

        /// <summary>Whether a raider band planning its next move goes hunting tenders this time (<see cref="RaiderHuntChance"/>).</summary>
        public bool RaidersHunt() => rng.NextDouble() < RaiderHuntChance.Value;
    }
}
