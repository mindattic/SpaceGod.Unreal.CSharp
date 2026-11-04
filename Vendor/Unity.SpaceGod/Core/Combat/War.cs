using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    public enum OrderKind
    {
        Patrol,

        /// <summary>Lurk beside a hyperlane and pull passing ships out of it (pirates).</summary>
        Ambush,
        Attack,
        Defend,
        Siege,
        Repair,
        Raid,
        Hunt,

        /// <summary>Storm a pirate haven: break its garrison, then board and take it or destroy it.</summary>
        Assault,

        /// <summary>Lie in wait off a jump gate, where traffic bunches up going in and coming out (pirates; U.SPC-T94).</summary>
        GateWatch,

        /// <summary>Hold one of the navy's own jump gates against raiders lying in wait off it: take station on its approach
        /// and fight them there (U.SPC-T374).</summary>
        HoldGate,

        /// <summary>Lie over an enemy port so no ship may dock there, on its polity's orders (U.SPC-T423, see <see cref="StrategicCommand"/>).</summary>
        Blockade,

        /// <summary>Shadow a ship its polity has given it to guard (U.SPC-T423).</summary>
        Escort,

        /// <summary>Visit every world of a system in turn, on its polity's orders (U.SPC-T423).</summary>
        Explore,
    }

    /// <summary>
    /// An armed interdictor a lane rider's scanners have picked up ahead (see <see cref="War.ThreatAhead"/>): who, how many
    /// seconds of the ride until its pulse can reach the rider, how far ahead along the lane it is, and whether the rider
    /// has made out its hull well enough to be braced for the pulse (<see cref="War.Forewarned"/>).
    /// </summary>
    public readonly struct LaneThreat
    {
        public readonly Ship Lurker;
        public readonly double SecondsToPulse, DistanceKm;
        public readonly bool Braced;

        public LaneThreat(Ship lurker, double secondsToPulse, double distanceKm, bool braced)
        {
            Lurker = lurker;
            SecondsToPulse = secondsToPulse;
            DistanceKm = distanceKm;
            Braced = braced;
        }
    }

    /// <summary>What a fleet group (a flagship and its escorts) is doing, and where.</summary>
    public sealed class GroupOrder
    {
        public OrderKind Kind;
        public int Body = -1;
        public double SinceJd;
    }

    /// <summary>
    /// The wars. Fleet groups take orders (patrol home, attack a weak enemy port, defend one under
    /// attack, besiege, refit at a yard), pirates raid busy ports with thin defences, and a battle
    /// starts whenever hostile forces meet at a body: groups, merchants or a settlement's defence
    /// platforms. Outcomes feed back: losses are permanent, yards rebuild from the markets (alloys,
    /// electronics, munitions), a port held without defenders for three days changes hands, and
    /// traders steer clear of where the fighting has been.
    /// </summary>
    public sealed class War
    {
        readonly World world;
        public readonly List<Battle> Battles = new List<Battle>();
        public readonly List<Battle> Ended = new List<Battle>();

        /// <summary>Lasting wrecks left where ships have died, cleared away once each has run its lifetime.</summary>
        public readonly Wrecks Wrecks = new Wrecks();

        /// <summary>Escape pods drifting where hulls died, the hostages and prisoners taken from them, and every faction's manpower.</summary>
        public readonly Crews Crews;
        public readonly Dictionary<Ship, GroupOrder> Orders = new Dictionary<Ship, GroupOrder>();

        /// <summary>
        /// The order each group held before its current one (U.SPC-T371): the one that brought it where it lies now. A group
        /// is handed its next order as it arrives (to leave on when its stay is up), so while it lies at its objective its
        /// current order already points on; it is this one that still commands it to fight where it is (see <see cref="Commanded"/>).
        /// </summary>
        public readonly Dictionary<Ship, GroupOrder> PriorOrders = new Dictionary<Ship, GroupOrder>();
        readonly Dictionary<int, double> danger = new Dictionary<int, double>();
        readonly Dictionary<Settlement, double> siegeDays = new Dictionary<Settlement, double>();

        /// <summary>Ports with a hostile besieging group parked over them and none of the owner's warships there.</summary>
        readonly Dictionary<Settlement, Faction> besiegers = new Dictionary<Settlement, Faction>();
        readonly Dictionary<Faction, int> losses = new Dictionary<Faction, int>();
        readonly Dictionary<int, int> battleCount = new Dictionary<int, int>();
        readonly Dictionary<Ship, double> retreating = new Dictionary<Ship, double>();
        readonly Dictionary<Ship, (Lane lane, double at)> laneSeen = new Dictionary<Ship, (Lane, double)>();
        readonly Dictionary<Ship, double> pulseReady = new Dictionary<Ship, double>();
        readonly Dictionary<BattleUnit, Faction> captures = new Dictionary<BattleUnit, Faction>();
        readonly Dictionary<Settlement, double> assaultDays = new Dictionary<Settlement, double>();

        /// <summary>An interdiction pulse pulls out every ship within this distance along the lane.</summary>
        public const double PulseRadiusKm = 600000;

        /// <summary>
        /// What a raider's pulse emitter gives off as it sits charged beside a lane, in signature units: far brighter than the
        /// dark hull it is fitted to, so a rider's scanners pick up the field ahead (as a blip) long before the hull itself,
        /// at <see cref="SensorSuite.ReachKm"/> of this (U.SPC-T42).
        /// </summary>
        public const double EmitterSignature = 400;

        /// <summary>The chance a hull surging through the lane (<see cref="Fleet.SurgeRide"/>) is past and gone before the pulse's shell closes on it.</summary>
        public double SurgeSlipChance = 0.6;

        readonly List<string> pilotNotices = new List<string>();

        /// <summary>What the war has to tell the player about the lane they ride (a pulse ridden out, slipped, or that caught them), taken once.</summary>
        public List<string> TakePilotNotices()
        {
            var list = new List<string>(pilotNotices);
            pilotNotices.Clear();
            return list;
        }
        readonly Dictionary<int, double> lastBattleEndJd = new Dictionary<int, double>();
        readonly Random rng;

        double lastJd, nextDailyJd;
        double warStartJd, ceasefireUntilJd = -1;
        int nextBattleId, builtWarships;

        /// <summary>Raised when a battle begins (the app may want to slow time or point the camera).</summary>
        public event Action<Battle> BattleStarted;

        /// <summary>Raised when a pirate clan takes loot or a prize here: the clan, what it was worth (credits) and the date
        /// (U.SPC-T94; the clans' victims put a price on them, see <see cref="PirateClans"/>).</summary>
        public event Action<Faction, double, double> Plundered;

        /// <summary>
        /// Raised when a pirate clan destroys a merchant or civilian hull here outright rather than robbing it (U.SPC-T374):
        /// the clan, the heat it draws (credits the robbed states add to the price on its head, see
        /// <see cref="RaidDoctrine.DestroyHeat"/>) and the date. A wreck draws far more than a clean robbery.
        /// </summary>
        public event Action<Faction, double, double> Heat;

        /// <summary>The heat each clan has drawn here by destroying hulls it could have robbed (credits, all time; U.SPC-T374).</summary>
        public readonly Dictionary<Faction, double> HeatDrawn = new Dictionary<Faction, double>();

        /// <summary>The chance a raider group, deciding where to go next, lies in wait off its system's busiest gate instead.</summary>
        public const double GateWatchChance = 0.2;

        /// <summary>A gate must have seen at least this much traffic lately (<see cref="JumpGate.Traffic"/>) to be worth watching.</summary>
        public const double MinGateTraffic = 2;

        /// <summary>Raiders lying off a gate fall on ships that come within this of them (km): everyone going in or coming out.</summary>
        public const double GateAmbushRangeKm = 3000;

        /// <summary>A fight off a system's root (out at a jump gate, not over a world) takes in only those this close to it (km).</summary>
        public const double GateFightRangeKm = 20000;

        /// <summary>Raised as a battle is resolved, before its survivors go back to their schedules: every unit's
        /// fate (dead, fled, taken, ransomed) is final. The law judges what was done in it (see <see cref="Law.OnBattleEnded"/>).</summary>
        public event Action<Battle, double> BattleEnded;

        public War(World world, double jd, int seed)
        {
            this.world = world;
            rng = new Random(seed);
            lastJd = jd;
            nextDailyJd = jd + 1;
            warStartJd = jd;
            nextPodSweepJd = jd;
            Crews = new Crews(unchecked(seed * 31 + 7)) { HavenOf = HomeHaven };
            Crews.RaidersTaken = (pod, by, when) =>
            {
                if (by.IsMilitary && pod.Haven != null) Discover(pod.Haven, by, when, $"raiders picked up from the {pod.Name}'s escape pods give it away");
            };
            Crews.Launched += pod => world.Log(pod.LaunchedJd,
                $"The {pod.Name} launches {pod.Pods} escape pod{(pod.Pods == 1 ? "" : "s")}: {pod.People:0} {pod.Owners.Name} crew adrift and broadcasting", WorldEventKind.Loss, PodBody(pod));
            Crews.Resolved += (pod, ship) =>
            {
                string by = ship != null ? "the " + ship.Name : pod.TakenBy != null ? "the " + pod.TakenBy.Name : "nobody";
                string text = pod.Breacher ? pod.State switch
                {
                    // A drifting Breacher round (U.SPC-T519).
                    PodState.Rescued => $"{Cap(by)} tows in the {pod.Name}: {pod.People:0} {pod.Owners.Name} marines home safe",
                    PodState.Hostage => $"{Cap(by)} salvages the {pod.Name} and holds its {pod.People:0} marines for ransom or sale",
                    PodState.Prisoner => $"{Cap(by)} tows in the {pod.Name} and takes its {pod.People:0} marines prisoner",
                    _ => $"The {pod.Name} falls silent: its air ran out with {pod.People:0} marines inside",
                } : pod.State switch
                {
                    PodState.Rescued => $"{Cap(by)} picks up the {pod.Name}'s escape pods: {pod.People:0} crew safe",
                    PodState.Hostage => $"{Cap(by)} hauls in the {pod.Name}'s escape pods and holds {pod.People:0} crew for ransom",
                    PodState.Prisoner => $"{Cap(by)} picks up the {pod.Name}'s escape pods and takes {pod.People:0} prisoners",
                    _ => $"The {pod.Name}'s escape pods fall silent: their air ran out with {pod.People:0} aboard",
                };
                world.Log(pod.ResolvedJd, text, pod.State == PodState.Lost ? WorldEventKind.Loss : WorldEventKind.Info, PodBody(pod));
            };
            Crews.CaptivesReleased += (c, how, when) => world.Log(when, how, WorldEventKind.Info, c.Haven?.Body ?? -1);
        }

        static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>The body a pod cluster's log line is filed under: where it drifts, or none on a lane.</summary>
        static int PodBody(EscapePod pod) => pod.Site == null ? pod.Body : -1;

        /// <summary>How often ships near drifting pods get a chance to find them, days.</summary>
        public const double PodSweepDays = 0.02;
        double nextPodSweepJd;

        Fleet Fleet => world.Fleet;
        Economy Economy => world.Economy;
        Diplomacy Diplomacy => world.Diplomacy;
        BodyCatalog Catalog => world.Catalog;

        public double Danger(int body) => danger.TryGetValue(body, out var d) ? d : 0;

        public int Losses(Faction f) => losses.TryGetValue(f, out var n) ? n : 0;

        /// <summary>The battle being fought at <paramref name="body"/>, or null. An ambush out on a hyperlane
        /// is filed under the system root but fought out on the lane, so it is at no body.</summary>
        public Battle BattleAt(int body)
        {
            foreach (var b in Battles)
                if (b.Body == body && b.Site == null) return b;
            return null;
        }

        public GroupOrder OrderOf(Ship leader) => Orders.TryGetValue(leader, out var o) ? o : null;

        // ---------------------------------------------------------------- per frame

        public void Update(double jd)
        {
            double seconds = (jd - lastJd) * Astro.SecondsPerDay;
            lastJd = jd;
            for (int i = Battles.Count - 1; i >= 0; i--)
            {
                var b = Battles[i];
                if (b.Fresh)
                {
                    b.Fresh = false;
                    continue;
                }
                b.Advance(seconds, world.Coarse);
                // The player who has left the fight (flown out of it, or let go for the cargo) is free of it
                // now, not when the rest have finished: free to cruise, take the gate or engage elsewhere.
                foreach (var u in b.Units)
                    if (u.Fled && u.PlayerControlled && u.Ship != null && u.Ship.Battle == b) u.Ship.Battle = null;
                if (b.Over) Resolve(b, jd);
            }
            Interdictions(jd);
            GateAmbushes(jd);
            if (jd >= nextPodSweepJd)
            {
                double days = Math.Min(1, jd - nextPodSweepJd + PodSweepDays);
                Crews.Sweep(Fleet.Ships, Diplomacy, world.PilotedShip, jd, days);
                Crews.Expire(jd);
                nextPodSweepJd = jd + PodSweepDays;
            }
            while (jd >= nextDailyJd)
            {
                Daily(nextDailyJd);
                nextDailyJd += 1;
            }
        }

        // ---------------------------------------------------------------- lane ambushes

        /// <summary>
        /// Each lurking raider watches its lane. A ship that passes its station since the last look
        /// (so a fast pass between frames still counts) sets off the pulse: every ship near that
        /// point of the lane is thrown out of it, its drives and shields knocked out for a minute or
        /// two, and the raiders close in.
        /// </summary>
        void Interdictions(double jd)
        {
            var lanes = Fleet.Lanes;
            if (lanes == null) return;
            var seen = new Dictionary<Ship, (Lane lane, double at)>();
            foreach (var v in Fleet.Ships)
            {
                // The player's ship is never a lurker, but riding a lane at the helm (U.SPC-T41) it is a rider like any
                // other, and a pulse throws it out (U.SPC-T42): a piloted hull is in the lane phase only while it rides.
                if (v.Destroyed || v.IsEscort || v.Phase != ShipPhase.Lane || v.Battle != null) continue;
                double p = Fleet.LaneProgress(v, jd);
                seen[v] = (v.LaneLeg.Lane, v.LaneLeg.From == v.LaneLeg.Lane.A ? p : 1 - p);
            }
            foreach (var lurker in Fleet.Ships)
            {
                if (!Lurking(lurker)) continue;
                if (pulseReady.TryGetValue(lurker, out double ready) && jd < ready) continue;
                var lane = lurker.LaneLeg.Lane;
                double station = lurker.LaneLeg.From == lane.A ? lurker.LaneHold : 1 - lurker.LaneHold;
                double window = PulseRadiusKm / Math.Max(lurker.LaneLengthKm, 1);
                var victims = new List<Ship>();
                foreach (var kv in seen)
                {
                    var v = kv.Key;
                    // Already caught by another lurker's pulse this pass: it is fighting there.
                    if (v.LaneLeg.Lane != lane || !Targets(lurker, v) || v.Derelict || v.Battle != null) continue;
                    double now = kv.Value.at;
                    // Last seen on another lane (it rode one out and another in since the last look): no pass of this one yet.
                    double before = laneSeen.TryGetValue(v, out var b) && b.lane == lane ? b.at : now;
                    if ((before - station) * (now - station) <= 0 || Math.Abs(now - station) < window) victims.Add(v);
                }
                if (victims.Count > 0 && WorthPulsing(lurker, victims)) Interdict(lurker, station, victims, jd);
            }
            laneSeen.Clear();
            foreach (var kv in seen) laneSeen[kv.Key] = kv.Value;
        }

        /// <summary>
        /// Whether a lurker sets its pulse off on <paramref name="victims"/> passing its station (U.SPC-T374): it sizes them
        /// up first (<see cref="BestMark"/>), and lets a merchant or civilian hull ride on when none of them is worth the guns
        /// riding with them or the heat (a warship passing alone it pulls out as before, if the guns about are few enough).
        /// </summary>
        bool WorthPulsing(Ship lurker, List<Ship> victims)
        {
            // A navy's lane picket (U.SPC-T479) pulls out every raider or enemy it can take on: the guns riding with them no
            // more than its own group's, with a margin for the stun the pulse leaves them under.
            if (!lurker.Faction.IsPirate)
            {
                double hostile = 0;
                foreach (var v in victims) hostile += GroupStrength(v);
                return hostile <= GroupStrength(lurker) * PicketNerve;
            }
            var marks = new List<Ship>();
            double guns = 0;
            foreach (var v in victims)
            {
                if (v.Spec.IsMilitary || v.Faction.IsPirate) guns += GroupStrength(v);
                else
                {
                    marks.Add(v);
                    // Its escorts ride the lane with it.
                    guns += GroupStrength(v) - v.Combat.Strength;
                }
            }
            if (marks.Count == 0) return true;
            double band = GroupStrength(lurker);
            return BestMark(marks, guns, band) != null || (guns > 0 && guns <= band * RaidDoctrine.BaseNerve);
        }

        /// <summary>How much more strength a navy's lane picket will pull out of a lane than its own group has (the pulse
        /// leaves them stunned and shieldless for the first minute or two).</summary>
        public const double PicketNerve = 1.5;

        /// <summary>
        /// A group lying in wait beside its lane, pulse at the ready: a raider band, or a navy's lane picket (U.SPC-T479),
        /// that still carries a working field projector (see <see cref="LaneInterdictors"/>; a band whose projector was lost or stripped
        /// lies there with nothing to pulse). Not one in a fight, at the helm, or off running the player down (U.SPC-T273),
        /// which has left its station however its schedule still reads.
        /// </summary>
        bool Lurking(Ship lurker) =>
            !lurker.Destroyed && !lurker.IsEscort && lurker.Ambushing && lurker.Battle == null &&
            lurker != world.PilotedShip && (world.Pursuit == null || world.Pursuit.Of(lurker) == null) &&
            LaneInterdictors.Fielded(lurker, Fleet.Ships);

        /// <summary>
        /// Whether <paramref name="lurker"/>'s pulse is meant for <paramref name="rider"/> (U.SPC-T479): a raider's for any
        /// ship but another raider's, a navy picket's for raiders, its enemies at war, and the player's hull when that navy
        /// has a price on the player's head.
        /// </summary>
        public bool Targets(Ship lurker, Ship rider)
        {
            if (lurker == null || rider == null || rider.Faction == null || lurker.Faction == null) return false;
            if (lurker.Faction.IsPirate) return !rider.Faction.IsPirate;
            if (rider.Faction == lurker.Faction) return false;
            if (Diplomacy != null && Diplomacy.Hostile(lurker.Faction, rider.Faction)) return true;
            return rider == world.PilotedShip && world.Law != null && world.Law.Player.WantedBy(lurker.Faction);
        }

        void Interdict(Ship lurker, double station, List<Ship> victims, double jd)
        {
            var lane = lurker.LaneLeg.Lane;
            bool picket = !lurker.Faction.IsPirate;
            var emitter = LaneInterdictors.EmitterOf(lurker, Fleet.Ships) ?? lurker;
            pulseReady[lurker] = jd + LaneInterdictors.RechargeDays.Value;
            // A pilot surging through may be past before the shell closes: the pulse goes off, and misses.
            var pilot = world.PilotedShip;
            if (pilot != null && victims.Contains(pilot) && pilot.LaneSurged && rng.NextDouble() < SurgeSlipChance)
            {
                victims.Remove(pilot);
                pilotNotices.Add($"Through! The {emitter.Name}'s pulse goes off astern as you surge clear of the {lane.Name} lane's interdiction field");
                world.Log(jd, $"The {pilot.Name} surges through an interdiction pulse on the {lane.Name} lane", WorldEventKind.Battle, lane.A);
                if (victims.Count == 0) return;
            }
            foreach (var v in victims)
            {
                double at = v.LaneLeg.From == lane.A ? station : 1 - station;
                var side = lurker.LaneHoldSide * (0.8 + rng.NextDouble() * 0.4);
                Fleet.PullOut(v, jd, at, side);
            }
            var helio = Fleet.LanePoint(lurker, jd, lurker.LaneHold) + lurker.LaneHoldSide;
            var b = new Battle(nextBattleId++, 0, (picket ? "Interdiction on the " : "Ambush on the ") + lane.Name + " lane", helio, jd, rng.Next())
            {
                // A navy's picket stops raiders and enemies on the lane in the law's name, as a patrol interdicting a
                // wanted ship does (U.SPC-T479).
                EnforcingSide = picket ? 0 : -1,
                Site = new LaneSite
                {
                    Hop = lurker.LaneLeg, FromOffset = lurker.LaneFromOffset, ToOffset = lurker.LaneToOffset, Progress = lurker.LaneHold, Side = lurker.LaneHoldSide,
                },
            };
            Hook(b);
            var approach = DVec3.Cross(DVec3.UnitZ, lurker.LaneHoldSide);
            b.ApproachAxis = approach.LengthSquared > 0 ? approach.Normalized : DVec3.UnitX;
            Join(b, lurker, 0, false);
            foreach (var v in victims) Join(b, v, 1, false);
            // The pilot's hull, caught riding at the helm, is flown in the fight that follows: placed with the others the
            // pulse caught, but its pose is the helm's from here (U.SPC-T42).
            if (pilot != null && victims.Contains(pilot) && pilot.Battle == null)
            {
                var spot = Spot(b, 1) + new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, (rng.NextDouble() - 0.5) * 0.4) * 6;
                var unit = b.AddShip(pilot, 1, spot, -spot.Normalized);
                unit.PlayerControlled = true;
            }
            // The pulse comes from the group's interdictor (U.SPC-T479), wherever it flies in the formation.
            BattleUnit source = b.UnitOf(emitter) ?? b.UnitOf(lurker);
            // A victim whose scanners made the raiders out lying dark beside the lane as it closed (by class at least,
            // at the lurk's stand-off from the lane line) was braced for the pulse: drives and shields ride it out.
            var warned = new HashSet<Ship>();
            foreach (var v in victims)
                if (Forewarned(v, lurker, jd)) warned.Add(v.Controller);
            foreach (var u in b.Units)
            {
                if (u.Side != 1) continue;
                // A fighter rides with the carrier that launched it: a braced carrier's wings are braced too.
                var owner = u.Ship ?? u.Fighter?.Carrier?.Ship;
                if (owner != null && warned.Contains(owner.Controller)) continue;
                u.Stunned = 60 + rng.NextDouble() * 60;
                u.Combat.ZeroAllShields();
            }
            foreach (var v in warned)
                world.Log(jd, $"The {v.Name} spotted the raiders lying dark off the {lane.Name} lane and was braced for the pulse", WorldEventKind.Battle, lane.A);
            if (pilot != null && pilot.Battle == b)
            {
                var pu = b.UnitOf(pilot);
                string closing = picket ? $"{lurker.Faction.Name} warships closing" : "Raiders closing";
                pilotNotices.Add(pu != null && pu.Stunned > 0
                    ? $"INTERDICTED: the {emitter.Name}'s pulse throws you out of the {lane.Name} lane, drives and shields dead for {pu.Stunned:0} s. {closing}"
                    : $"INTERDICTED: thrown out of the {lane.Name} lane, but braced for the pulse: drives and shields held. {closing}");
            }
            if (source != null) b.Emit(FxKind.Pulse, source.Pos, source.Pos, 30, 0, source, -1);
            Battles.Add(b);
            string pulled = $"{victims.Count} ship{(victims.Count == 1 ? "" : "s")} pulled out";
            if (picket)
                // The law's own picket: a fight on the lane, but no raiders' ambush to steer the traffic round.
                world.Log(jd, $"The {lurker.Faction.Name} lane picket {lurker.Name} interdicts the {lane.Name} lane: {pulled}", WorldEventKind.Battle, lane.A);
            else
            {
                lanes0().Danger.TryGetValue(lane, out double danger);
                lanes0().SetDanger(lane, danger + 3);
                world.Log(jd, $"{lurker.Faction.Name} raiders ({lurker.Name}) spring an ambush on the {lane.Name} lane: {pulled}", WorldEventKind.Battle, lane.A);
                BumpDanger(lane.A, 1);
                BumpDanger(lane.B, 1);
            }
            BattleStarted?.Invoke(b);
        }

        /// <summary>
        /// Whether <paramref name="victim"/>'s group made out <paramref name="lurker"/> (by class at least) as it passed:
        /// the raider lies dark (<see cref="Signatures.Silent"/>) at its stand-off from the lane line, so a merchant's
        /// navigation sensors see only a blip there, if that, while a warship's suite, or a merchant with a scanner suite
        /// fitted, picks out a hull.
        /// </summary>
        public bool Forewarned(Ship victim, Ship lurker, double jd)
        {
            var sensors = world.Sensors;
            if (sensors == null || victim == null || lurker == null) return false;
            double pass = Math.Max(lurker.LaneHoldSide.Length, 1);
            return Sensors.Resolve(sensors.GroupSuite(victim), Signatures.Of(lurker, false, false), pass, Signatures.Transponder(lurker)) >= ContactLevel.Class;
        }

        /// <summary>
        /// What a lane rider's scanners make of the nearest armed interdictor ahead on its lane (U.SPC-T42), or null if
        /// there is none it can see, it has passed them all, or its ride ends short of every pulse. The emitter's field is
        /// picked up at <see cref="SensorSuite.ReachKm"/> of <see cref="EmitterSignature"/> (better scanners see it sooner),
        /// and the countdown is the ride's own time to the edge of the pulse's reach, at its present pace.
        /// </summary>
        public LaneThreat? ThreatAhead(Ship rider, double jd)
        {
            if (rider == null || rider.Destroyed || rider.Phase != ShipPhase.Lane || rider.Battle != null || Fleet.Lanes == null) return null;
            var lane = rider.LaneLeg.Lane;
            bool forward = rider.LaneLeg.From == lane.A;
            double now = Fleet.LaneProgress(rider, jd);
            var suite = world.Sensors != null ? world.Sensors.GroupSuite(rider) : SensorSuite.Of(rider);
            double reach = suite.ReachKm(EmitterSignature);
            LaneThreat? best = null;
            foreach (var lurker in Fleet.Ships)
            {
                if (!lurker.Ambushing || lurker.LaneLeg.Lane != lane || !Targets(lurker, rider) || !Lurking(lurker)) continue;
                if (pulseReady.TryGetValue(lurker, out double ready) && ready > jd) continue;
                // The lurker's station and the pulse's reach, as fractions of the rider's own leg.
                double stationA = lurker.LaneLeg.From == lane.A ? lurker.LaneHold : 1 - lurker.LaneHold;
                double station = forward ? stationA : 1 - stationA;
                double window = PulseRadiusKm / Math.Max(lurker.LaneLengthKm, 1);
                if (now >= station + window) continue;
                double edge = station - window;
                if (rider.LaneEndProgress < edge) continue;
                double km = Math.Max(0, station - now) * rider.LaneLengthKm;
                if (km > reach) continue;
                double pulseJd = now >= edge ? jd : RideReaches(rider, edge, jd);
                double seconds = (pulseJd - jd) * Astro.SecondsPerDay;
                if (best == null || seconds < best.Value.SecondsToPulse)
                    best = new LaneThreat(lurker, seconds, km, Forewarned(rider, lurker, jd));
            }
            return best;
        }

        /// <summary>When a rider's progress along its leg reaches <paramref name="progress"/> (at its present pace), by bisection.</summary>
        static double RideReaches(Ship rider, double progress, double jd)
        {
            double lo = jd, hi = Math.Max(jd, rider.PhaseEndJd);
            if (Fleet.LaneProgress(rider, hi) < progress) return hi;
            for (int i = 0; i < 50; i++)
            {
                double mid = (lo + hi) * 0.5;
                if (Fleet.LaneProgress(rider, mid) < progress) lo = mid;
                else hi = mid;
            }
            return hi;
        }

        Hyperlanes lanes0() => Fleet.Lanes;

        void Hook(Battle b)
        {
            b.UnitDestroyed = OnUnitDestroyed;
            // Bounty hunters board, and hold their fire on a disabled quarry to take its captain alive (U.SPC-T63).
            b.Boards = u => u.Ship != null && Hunter(u.Ship);
            b.WantsAlive = (u, o) => u.Ship != null && o.Ship != null && world.Outlaws != null && world.Outlaws.WantsAlive(u.Ship, o.Ship);
            b.Launch = FighterAI.Launch;
            b.FighterStep = FighterAI.Step;
            b.UnitCaptured = (battle, u, captor) =>
            {
                captures[u] = captor;
                // The player's ship taken: it is the captor's prize now, no longer the player's to fly
                // (the Runtime sees PilotedShip go and ends the session).
                if (u.Ship != null && u.Ship == world.PilotedShip)
                {
                    world.PilotedShip = null;
                    u.PlayerControlled = false;
                }
                // Allies who boarded together take it together (U.SPC-T392): the log names them all.
                string by = u.Combat.Interior.Joint ? u.Combat.Interior.BoarderNames : captor.Name;
                if (u.Ship != null) world.Log(battle.StartJd + battle.Seconds / Astro.SecondsPerDay,
                    $"{battle.Name}: {by} marines take the {u.Ship.Name}", WorldEventKind.Capture, Shown(battle));
            };
            b.UnitSurrendered += (battle, u) =>
            {
                if (u.Ship != null) world.Log(battle.StartJd + battle.Seconds / Astro.SecondsPerDay,
                    $"{battle.Name}: the {u.Ship.Name} strikes its colours and hails for terms", WorldEventKind.Battle, Shown(battle));
            };
            b.SurrenderDecided += OnSurrenderDecided;
            b.CargoDemanded += (battle, u, pirates) =>
            {
                if (u.Ship != null) world.Log(battle.StartJd + battle.Seconds / Astro.SecondsPerDay,
                    $"{battle.Name}: {pirates.Name} demand the {u.Ship.Name}'s cargo", WorldEventKind.Battle, Shown(battle));
            };
        }

        /// <summary>
        /// Books a captor's terms for a hull that struck its colours (or a player who paid off pirates):
        /// ransomed cargo goes to the captor, a released hull carries on; a ship to be taken waits for the
        /// boarders and is booked by the capture hook like any other prize.
        /// </summary>
        void OnSurrenderDecided(Battle b, BattleUnit u, Faction captor, SurrenderTerms terms)
        {
            var ship = u.Ship;
            if (ship == null) return;
            double jd = b.StartJd + b.Seconds / Astro.SecondsPerDay;
            string who = captor != null ? captor.Name : "its foes";
            switch (terms)
            {
                case SurrenderTerms.RansomCargo:
                    if (ship.HasCargo)
                    {
                        if (captor != null && captor.IsPirate) Loot(ship, captor, b);
                        else
                        {
                            if (captor != null) captor.Treasury += ship.CargoTonnes * GoodInfo.Of(ship.CargoGood).BasePrice * 0.8;
                            Economy.Release(ship);
                            ship.HasCargo = false;
                            ship.CargoDestination = null;
                            ship.Cargo = null;
                        }
                    }
                    world.Log(jd, captor != null && captor.IsPirate
                        ? $"{b.Name}: the {ship.Name} jettisons its cargo, {who} drones collect it, and it is let go"
                        : $"{b.Name}: the {ship.Name} hands its cargo to {who} and is let go", WorldEventKind.Capture, Shown(b));
                    break;
                case SurrenderTerms.LetGo:
                    world.Log(jd, $"{b.Name}: {who} let the {ship.Name} go", WorldEventKind.Battle, Shown(b));
                    break;
                case SurrenderTerms.TakeShip:
                    world.Log(jd, $"{b.Name}: {who} send boarders to take the {ship.Name}", WorldEventKind.Capture, Shown(b));
                    break;
            }
        }

        /// <summary>The lane with the most merchant traffic, preferring those that start at the raiders' own system.</summary>
        Lane BusiestLane(int system)
        {
            var lanes = Fleet.Lanes;
            Lane best = null;
            double bestScore = 0;
            foreach (var lane in lanes.Lanes)
            {
                double traffic = 0;
                foreach (var s in Fleet.Ships)
                {
                    if (s.Destroyed || !world.IsTrader(s)) continue;
                    if (s.Phase == ShipPhase.Lane && s.LaneLeg.Lane == lane) traffic += 1;
                    if (s.Journey != null)
                        foreach (var hop in s.Journey) if (hop.Lane == lane) traffic += 0.5;
                }
                double score = traffic * (lane.A == system || lane.B == system ? 2 : 1) * (0.7 + rng.NextDouble() * 0.6);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = lane;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- prizes

        /// <summary>
        /// What boarders do with a ship they have taken. Pirates keep warships they can crew, strip
        /// merchant holds and mostly leave the hull adrift (or sell it to the breakers at Blackwater,
        /// or scuttle it); navies take serviceable enemy hulls as prizes and scuttle pirates.
        /// </summary>
        void DisposeOfPrize(Battle b, BattleUnit u, Faction captor, double jd)
        {
            var ship = u.Ship;
            if (ship == null || ship.Destroyed)
            {
                if (u.Kind == UnitKind.Platform) StrikeOff(u);
                return;
            }
            // Captors with a brig lock up some of its people first, for bounty or ransom in port (U.SPC-T65).
            var captors = new List<Ship>();
            foreach (var cu in b.Units)
                if (cu.Ship != null && !cu.Dead && cu.Ship != ship && cu.Ship.Faction == captor) captors.Add(cu.Ship);
            double prisoners = Outfitting.TakePrisoners(ship, captors);
            string how;
            double hull = ship.Combat.HullFraction;
            bool warship = ship.Spec.IsMilitary;
            if (captor.IsPirate)
            {
                // Looting draws nothing from the dice, so the roll can be made first: a hull about to be cast
                // adrift is stripped in a hurry, and some of its cargo is left aboard for a salvager.
                double roll = rng.NextDouble();
                bool adrift = !warship && roll < 0.55;
                var good = ship.CargoGood;
                double left = 0;
                if (ship.HasCargo)
                {
                    if (adrift)
                    {
                        left = ship.CargoTonnes * SalvageSession.LeftAboardShare;
                        // The whole load it was bringing is off the port's books, not just the part looted.
                        Economy.Release(ship);
                        ship.CargoDestination = null;
                        ship.CargoTonnes -= left;
                    }
                    Loot(ship, captor, b);
                }
                if (warship && hull >= 0.3) how = Steal(ship, captor, b, jd) ? $"pressed into {captor.Name} service" : "scuttled";
                else if (warship) { Scuttle(ship, b, u, jd); how = "scuttled, its crew put off in the pods"; }
                else if (adrift) { LeaveAdrift(ship, b, u, captor, good, left, jd); how = "stripped and left adrift"; }
                else if (roll < 0.85)
                {
                    captor.Treasury += Economy.HullPrice(ship.Spec.Class) * 0.25;
                    Plundered?.Invoke(captor, Economy.HullPrice(ship.Spec.Class), jd);
                    // Its crew go with it, as hostages to be ransomed.
                    TakeCrewCaptive(ship, captor, jd);
                    Detach(ship);
                    ship.Destroyed = true;
                    // A hull lost to its owners like any other.
                    losses.TryGetValue(ship.Faction, out int lost);
                    losses[ship.Faction] = lost + 1;
                    how = $"taken to {HavenName(captor)} and sold to the breakers";
                }
                else { Scuttle(ship, b, u, jd); how = "scuttled, its crew put off in the pods"; }
            }
            else if (ship.Faction.IsPirate || hull < 0.3)
            {
                // A raider's surviving crew, taken off before it is scuttled, give away where they sail from.
                if (ship.Faction.IsPirate && HomeHaven(ship.Controller) is Settlement home)
                    Discover(home, captor, jd, $"prisoners taken off the {ship.Name} give it away");
                if (ship.Faction.IsPirate)
                {
                    TakeCrewCaptive(ship, captor, jd);
                    Scuttle(ship);
                    how = "scuttled";
                }
                else
                {
                    Scuttle(ship, b, u, jd);
                    how = "scuttled, its crew put off in the pods";
                }
            }
            else
            {
                if (ship.HasCargo)
                {
                    captor.Treasury += ship.CargoTonnes * GoodInfo.Of(ship.CargoGood).BasePrice * 0.8;
                    Economy.Release(ship);
                    ship.HasCargo = false;
                    ship.CargoDestination = null;
                    ship.Cargo = null;
                }
                how = Steal(ship, captor, b, jd) ? "taken as a prize" : "scuttled";
            }
            world.Log(jd, $"The {ship.Name} is {how} by the {captor.Name}" + (prisoners >= 1 ? $", {prisoners:0} of its crew taken prisoner" : ""), WorldEventKind.Capture, Shown(b));
        }

        /// <summary>
        /// A hull taken by allied factions boarding together (U.SPC-T392) is settled between them before anyone disposes
        /// of it (<see cref="PrizeSettlement.Negotiate"/>): each partner's part of the cargo is paid out to it (raiders' into
        /// their haven, a lawful faction's at the prize price), and the keeper buys its partners out of the hull from its
        /// treasury, or, when none of them can, the hull goes to the breakers and the price is shared. Returns the faction
        /// that keeps the hull, to dispose of it as a lone captor would (what is left of the hold its own share), or null
        /// when it was sold. A boarding by one faction alone returns its captor untouched.
        /// </summary>
        Faction SettleJointPrize(Battle b, BattleUnit u, Faction captor, double jd)
        {
            var ship = u.Ship;
            var inside = u.Combat?.Interior;
            if (ship == null || ship.Destroyed || inside == null || !inside.Joint) return captor;
            double price = Economy.HullPrice(ship.Spec.Class);
            var good = ship.CargoGood;
            var deal = PrizeSettlement.Negotiate(inside.Parties, price * ship.Combat.HullFraction * PrizeSettlement.PrizeValueShare,
                price * PrizeSettlement.BreakersShare, ship.HasCargo ? ship.CargoTonnes : 0, f => f.Treasury);
            string allies = inside.BoarderNames;
            var parts = new List<string>();
            foreach (var claim in deal.Claims)
            {
                // The keeper's part of the hold stays aboard for it; everyone else's is paid out now.
                if (claim.Faction != deal.Keeper && claim.CargoTonnes > 0 && ship.HasCargo)
                {
                    double tonnes = Math.Min(claim.CargoTonnes, ship.CargoTonnes);
                    if (claim.Faction.IsPirate) DeliverLoot(claim.Faction, good, tonnes, b);
                    else claim.Faction.Treasury += tonnes * GoodInfo.Of(good).BasePrice * 0.8;
                    ship.CargoTonnes -= tonnes;
                }
                if (claim.Faction != deal.Keeper && claim.HullPaid > 0)
                {
                    claim.Faction.Treasury += claim.HullPaid;
                    if (deal.Keeper != null) deal.Keeper.Treasury -= claim.HullPaid;
                }
                parts.Add($"{claim.Faction.Name} {claim.Share * 100:0}%");
            }
            if (ship.HasCargo && ship.CargoTonnes <= 1e-6)
            {
                Economy.Release(ship);
                ship.HasCargo = false;
                ship.CargoDestination = null;
                ship.Cargo = null;
            }
            string claims = string.Join(", ", parts);
            if (deal.Keeper != null)
            {
                world.Log(jd, $"The {allies} settle the {ship.Name} between them ({claims}): the {deal.Keeper.Name} keep the hull, buying out their partners for {deal.BuyOut:N0} cr, and the hold is shared",
                    WorldEventKind.Capture, Shown(b));
                return deal.Keeper;
            }
            // Nobody could buy the others out: the hull goes to the breakers, its people as the largest claim's prisoners.
            PrizeClaim lead = null;
            foreach (var claim in deal.Claims)
                if (lead == null || claim.Share > lead.Share) lead = claim;
            if (ship.HasCargo)
            {
                Economy.Release(ship);
                ship.HasCargo = false;
                ship.CargoDestination = null;
                ship.Cargo = null;
            }
            TakeCrewCaptive(ship, lead?.Faction ?? captor, jd);
            Detach(ship);
            ship.Destroyed = true;
            losses.TryGetValue(ship.Faction, out int lost);
            losses[ship.Faction] = lost + 1;
            world.Log(jd, $"The {allies} cannot agree who keeps the {ship.Name} ({claims}): it is sold to the breakers for {deal.BreakersValue:N0} cr, shared between them with the hold",
                WorldEventKind.Capture, Shown(b));
            return null;
        }

        void Loot(Ship ship, Faction clan, Battle b)
        {
            Economy.Release(ship);
            double value = ship.CargoTonnes * GoodInfo.Of(ship.CargoGood).BasePrice;
            DeliverLoot(clan, ship.CargoGood, ship.CargoTonnes, b);
            clan.Treasury += value * 0.3;
            Plundered?.Invoke(clan, value, lastJd);
            ship.HasCargo = false;
            ship.CargoDestination = null;
            ship.Cargo = null;
        }

        /// <summary>
        /// A hull broken up in a collision outside any battle (U.SPC-T60): it is lost like a scuttled ship,
        /// leaves a wreck where it struck (<paramref name="offsetKm"/> from <paramref name="body"/>, with whatever
        /// it carried still aboard to salvage) and the loss goes in the log. A ship already gone, or in a battle
        /// (whose own <see cref="Battle.ApplyImpact"/> settles it there), is left alone; true if it was lost here.
        /// </summary>
        public bool LoseInCollision(Ship ship, int body, DVec3 offsetKm, double jd, string what)
        {
            if (ship == null || ship.Destroyed || ship.Battle != null) return false;
            var wreck = Wrecks.Add(ship, body, offsetKm, jd);
            Scuttle(ship);
            world.Log(jd, $"The {ship.Name} breaks up in a collision with {what}", WorldEventKind.Loss, body);
            return true;
        }

        /// <summary>
        /// Sends a hull to the bottom. Given the battle and its unit there, whoever is still aboard is put off in
        /// the escape pods first, which drift where it was scuttled (see <see cref="Crews"/>).
        /// </summary>
        void Scuttle(Ship ship, Battle b = null, BattleUnit u = null, double jd = 0)
        {
            if (b != null && u != null && ship.Combat != null)
            {
                var inside = ship.Combat.Interior;
                double aboard = inside.Crew + inside.Marines;
                Crews.Launch(ship.Name, ship.Faction, aboard, Crews.Complement(ship.Spec.Class), AbandonCause.Scuttled,
                    b.Body, b.Anchor, u.Pos, b.Site, jd, haven: ship.Faction.IsPirate ? HomeHaven(ship.Controller) : null);
                inside.TakeCrew(inside.Crew);
                inside.TakeMarines(inside.Marines);
            }
            // Its group (if it led one) gets a new flagship, so the escorts are not left frozen on a wreck.
            Detach(ship);
            if (ship.HasCargo) Economy.Release(ship);
            ship.HasCargo = false;
            ship.Destroyed = true;
            losses.TryGetValue(ship.Faction, out int n);
            losses[ship.Faction] = n + 1;
        }

        /// <summary>
        /// The raiders cast the hull off where they took it: it drifts there, fixed in the fight's own frame
        /// (so a salvager can come alongside, match it and latch on), with what they left of its cargo, its
        /// stores, and perhaps some of its crew hiding aboard or some of the raiders still stripping it (see
        /// <see cref="SalvageSession.Strand"/>).
        /// </summary>
        void LeaveAdrift(Ship ship, Battle b, BattleUnit u, Faction raiders, Good good, double leftTonnes, double jd)
        {
            Detach(ship);
            ship.Derelict = true;
            ship.DerelictSinceJd = jd;
            ship.DerelictBy = raiders;
            ship.Cargo = "Derelict: boarded, stripped and abandoned";
            Fleet.PinAt(ship, b.Body, b.Anchor + u.Pos, u.Forward, jd);
            // A hull lost to its owners like any other.
            losses.TryGetValue(ship.Faction, out int lost);
            losses[ship.Faction] = lost + 1;
            double aboard = ship.Combat != null ? ship.Combat.Interior.Crew + ship.Combat.Interior.Marines : 0;
            SalvageSession.Strand(ship, raiders, good, leftTonnes, jd, aboard);
            // Those who did not hide aboard are put off in the pods, to be hauled in for ransom or found by someone kinder.
            double hid = ship.Combat?.Interior.Survivors ?? 0;
            if (aboard - hid >= 0.5)
                Crews.Launch(ship.Name, ship.Faction, aboard - hid, Crews.Complement(ship.Spec.Class), AbandonCause.Scuttled, b.Body, b.Anchor, u.Pos, b.Site, jd);
        }

        /// <summary>
        /// The people left aboard a hull its captors are keeping or selling become theirs to hold: pirates' hostages
        /// (held at their haven for ransom), an enemy navy's prisoners of war, or -- raiders taken by anyone -- lost
        /// to the Banner in irons.
        /// </summary>
        void TakeCrewCaptive(Ship ship, Faction captor, double jd)
        {
            if (ship.Combat == null) return;
            var inside = ship.Combat.Interior;
            double people = inside.Crew + inside.Marines;
            if (people < 0.5) return;
            inside.TakeCrew(inside.Crew);
            inside.TakeMarines(inside.Marines);
            if (ship.Faction.IsPirate) Crews.Lose(ship.Faction, people);
            else if (captor.IsPirate) Crews.Hold(ship.Faction, captor, people, false, LiveHavenOf(captor), jd, ship.Name);
            else Crews.Hold(ship.Faction, captor, people, true, null, jd, ship.Name);
        }

        /// <summary>The prize changes hands: a prize crew takes over and it joins the captor (a group of theirs in the fight, if there is room).</summary>
        bool Steal(Ship ship, Faction captor, Battle b, double jd)
        {
            TakeCrewCaptive(ship, captor, jd);
            Detach(ship);
            var old = ship.Faction;
            losses.TryGetValue(old, out int n);
            losses[old] = n + 1;
            ship.Faction = captor;
            // Whoever sat in its brig goes free with the taking: nobody's to hand over for a bounty any more.
            ship.Prisoners = 0;
            ship.PrisonerBounty = 0;
            if (captor.Prefix.Length > 0 && ship.Name.StartsWith(old.Prefix + " ")) ship.Name = captor.Prefix + ship.Name.Substring(old.Prefix.Length);
            ship.Combat.Interior.HandOver(ship.Combat.Spec.Crew * 0.3);
            Ship group = null;
            foreach (var u in b.Units)
            {
                if (u.Ship == null || u.Dead || u.Ship == ship || u.Ship.Faction != captor) continue;
                var leader = u.Ship.Controller;
                if (!leader.Destroyed && leader.Faction == captor && !leader.IsEscort && leader != world.PilotedShip) group = leader;
            }
            if (ship.Spec.IsMilitary && group != null && ship.Phase == group.Phase && ship.Orbit?.Body == group.Orbit?.Body)
            {
                int index = 0;
                foreach (var e in Fleet.Ships) if (e.Leader == group && !e.Destroyed) index++;
                if (index < 7)
                {
                    // The first slot no living escort holds: the lost and the cut loose leave gaps.
                    Fleet.AddEscort(group, ship, Fleet.SlotFor(FreeSlot(group)));
                    return true;
                }
            }
            if (ship.Spec.IsMilitary)
            {
                ship.Role = ShipRole.Patrol;
                ship.Route = group?.Route ?? (captor.IsPirate && HavenBody(captor) >= 0 ? new[] { HavenBody(captor) } : OwnPorts(captor, ship.Orbit.Body));
            }
            return true;
        }

        /// <summary>A faction's own settlement bodies (a navy's prize patrols those), or the given body if it has none.</summary>
        int[] OwnPorts(Faction f, int fallback)
        {
            var own = new List<int>();
            foreach (var s in Economy.Settlements)
                if (s.Owner == f && !own.Contains(s.Body)) own.Add(s.Body);
            return own.Count > 0 ? own.ToArray() : new[] { fallback };
        }

        /// <summary>A ship leaves its group (taken, or abandoned): it keeps the group's motion as its own, and the group gets a new flagship if it was the old one.</summary>
        void Detach(Ship ship)
        {
            if (!ship.IsEscort)
            {
                Succession(ship);
                return;
            }
            var lead = ship.Leader;
            ship.Leader = null;
            ship.Phase = lead.Phase;
            ship.Orbit = OwnSpot(lead.Orbit, ship, lead);
            ship.DepartureOrbit = lead.DepartureOrbit;
            ship.DestinationBody = lead.DestinationBody;
            ship.OriginBody = lead.OriginBody;
            ship.PhaseStartJd = lead.PhaseStartJd;
            ship.PhaseEndJd = lead.PhaseEndJd;
            ship.FrameBody = lead.FrameBody;
            ship.Path = lead.Path;
            ship.PathCumulative = lead.PathCumulative;
            ship.PathLength = lead.PathLength;
            ship.AccelKmPerDay2 = lead.AccelKmPerDay2;
            ship.Conic = lead.Conic;
            ship.GravityLossKmS = lead.GravityLossKmS;
            ship.DepartureVelocity = lead.DepartureVelocity;
            ship.ArrivalVelocity = lead.ArrivalVelocity;
            ship.Journey = lead.Journey;
            ship.JourneyIndex = lead.JourneyIndex;
            ship.FinalBody = lead.FinalBody;
            ship.LaneLeg = lead.LaneLeg;
            ship.LaneFromOffset = lead.LaneFromOffset;
            ship.LaneToOffset = lead.LaneToOffset;
            ship.LaneLengthKm = lead.LaneLengthKm;
            ship.LaneStartProgress = lead.LaneStartProgress;
            ship.LaneEndProgress = lead.LaneEndProgress;
            ship.LaneHold = lead.LaneHold;
            ship.LaneHoldSide = lead.LaneHoldSide;
            CopyRest(lead, ship);
        }

        /// <summary>
        /// A ship leaving its group keeps the group's orbit, but a little way along it (where it was
        /// flying in formation), not on top of its old flagship.
        /// </summary>
        static ParkingOrbit OwnSpot(ParkingOrbit orbit, Ship ship, Ship lead)
        {
            if (orbit == null || orbit.Fixed || orbit.RadiusKm <= 0) return orbit;
            double gap = Fleet.SafeDistanceKm(ship, lead) * 1.5;
            return new ParkingOrbit
            {
                Body = orbit.Body, RadiusKm = orbit.RadiusKm, U = orbit.U, V = orbit.V, Center = orbit.Center,
                EpochJd = orbit.EpochJd, PhaseAtEpoch = orbit.PhaseAtEpoch - gap / orbit.RadiusKm, RateRadPerDay = orbit.RateRadPerDay,
            };
        }

        /// <summary>The rest of a ship's motion state that a group's escorts and heirs take over with it (lane shape, ring).</summary>
        static void CopyRest(Ship from, Ship to)
        {
            to.LaneStar = from.LaneStar;
            to.LaneShape = from.LaneShape;
            to.Ring = from.Ring;
            to.HoldPoint = from.HoldPoint;
        }

        /// <summary>Moves whatever plunder <paramref name="from"/> is carrying onto <paramref name="to"/>'s hold
        /// (U.SPC-T308): used when a band merges into another, or a new flagship takes its place, so a clan's loot
        /// is not left stranded on a hull that no longer runs the group's schedule.</summary>
        static void MovePlunder(Ship from, Ship to)
        {
            if (from.Plunder == null) return;
            to.Plunder ??= new double[GoodInfo.Count];
            for (int i = 0; i < GoodInfo.Count; i++) to.Plunder[i] += from.Plunder[i];
            from.Plunder = null;
        }

        // ---------------------------------------------------------------- meeting the enemy

        /// <summary>A ship has arrived at a body: fight if anyone there is hostile, or join a fight already on.</summary>
        public void OnArrived(Ship ship, double jd)
        {
            // On the ground (or on the way down or up) a ship is out of the fight.
            if (ship.Destroyed || ship.Battle != null || ship.Planetside) return;
            // Local traffic neither joins a fight nor draws one (U.SPC-T375).
            if (ship.Controller.Role == ShipRole.Commuter) return;
            int body = ship.Orbit.Body;
            // Raiders home with plunder put it in the haven's stores (U.SPC-T94).
            if (ship.Faction.IsPirate) UnloadPlunder(ship.Controller, body);
            // A clan's smuggler docks at a lawless station unchallenged: its guns look the other way (U.SPC-T94).
            if (ship.Runner)
                foreach (var st in Economy.At(body))
                    if (st.Lawless && !st.Fallen) return;
            // Anyone who turns up at a hidden haven's rock sees it.
            if (!ship.Faction.IsPirate)
                foreach (var st in Economy.At(body))
                    if (st.Hidden) Discover(st, ship.Faction, jd, $"the {ship.Name} comes upon it");
            // Off the system's root (at a jump gate's approach) only what is near it counts (U.SPC-T94).
            DVec3? at = body == Catalog.Root.Index ? ship.Controller.Orbit.OffsetAt(jd) : (DVec3?)null;
            var existing = BattleAt(body);
            if (existing != null && at is DVec3 here && (existing.Anchor - here).Length > GateFightRangeKm) existing = null;
            if (existing != null)
            {
                // Join the side it can fight beside: against one side's leader and not the other's.
                bool h0 = Diplomacy.Hostile(ship.Faction, existing.SideLeaders[0]);
                bool h1 = Diplomacy.Hostile(ship.Faction, existing.SideLeaders[1]);
                int side = h0 && !h1 ? 1 : h1 && !h0 ? 0 : -1;
                if (side >= 0) Join(existing, ship, side, true);
                return;
            }
            if (TryStartBattle(body, ship.Faction, jd, at) != null) return;
            // Not fit to pick a fight itself: one waiting here that is falls on it instead, unless it is pulling back
            // from a fight it has just lost (let go while it gets clear, see TryStartBattle).
            if (Withdrawing(ship.Controller, jd)) return;
            foreach (var s in PresentGroups(body, at, jd))
                if (s.Battle == null && (s.Spec.IsMilitary || s.Faction.IsPirate) && Diplomacy.Hostile(s.Faction, ship.Faction) && Willing(s.Controller, jd) && s.Combat.Armed && Commanded(s, body))
                {
                    TryStartBattle(body, s.Faction, jd, at);
                    return;
                }
        }

        /// <summary>A player flies a piloted ship (excluded from every other gathering of "ships present" at a
        /// body, see <see cref="PresentGroups"/>) into range of a hostile: starts a fight there if one isn't
        /// already on, or joins the one that is, then places the piloted ship into it at its own actual flown
        /// position -- not a formation slot -- since the Runtime pilot loop, not this battle, owns its pose
        /// from here on (see <see cref="BattleUnit.PlayerControlled"/>). <paramref name="body"/> is the body the
        /// pilot is actually flying beside (the frame <paramref name="offsetFromBodyKm"/> is measured from), not
        /// the ship's scheduled orbit, which for a hull taken over mid-transit is still its far-off destination.
        /// Returns null (no-op) if nothing hostile and willing to fight is actually present.</summary>
        public Battle TryStartPlayerBattle(Ship ship, int body, DVec3 offsetFromBodyKm, DVec3 forward, double jd)
        {
            if (ship.Destroyed || ship.Battle != null || ship.Planetside) return null;
            // Only in the body's own neighbourhood (the frame body never changes mid-flight, however far the
            // pilot goes), and only into a fight it has actually reached: one placed beyond the battle's
            // edge would be counted as escaped on the very next step. A fight it starts is fought where it is.
            if (offsetFromBodyKm.Length > ScaleModel.NeighbourhoodKm(Catalog[body])) return null;
            var b = BattleAt(body);
            if (b != null && (offsetFromBodyKm - b.Anchor).Length > Battle.EscapeKm * 0.8) return null;
            // Already left this one: not back in until it is over.
            if (b != null && b.UnitOf(ship) != null) return null;
            b ??= TryStartBattle(body, ship.Faction, jd, offsetFromBodyKm, true);
            if (b == null) return null;
            bool h0 = Diplomacy.Hostile(ship.Faction, b.SideLeaders[0]);
            bool h1 = Diplomacy.Hostile(ship.Faction, b.SideLeaders[1]);
            int side = h0 && !h1 ? 1 : h1 && !h0 ? 0 : -1;
            if (side < 0) return null;
            var unit = b.AddShip(ship, side, offsetFromBodyKm - b.Anchor, forward);
            unit.PlayerControlled = true;
            return b;
        }

        /// <summary>
        /// The player opens fire on <paramref name="target"/>, a ship their own faction has no quarrel with (an
        /// enemy's is just <see cref="TryStartPlayerBattle"/>): a fight starts where the player is, the player alone on
        /// one side and the target's group, its faction's other ships here and its platforms over this world on the
        /// other. If a fight is already on here with the target in it, the player joins against it. Attacking a lawful
        /// ship is a crime, judged when the fight ends (see <see cref="Law.OnBattleEnded"/>). The target must hold orbit at
        /// this body (parked or queued at its ring) and the player be in its neighbourhood. Null if not.
        /// </summary>
        public Battle TryStartPlayerAttack(Ship ship, Ship target, int body, DVec3 offsetFromBodyKm, DVec3 forward, double jd)
        {
            if (ship == null || target == null || ship == target || ship.Destroyed || ship.Battle != null || ship.Planetside) return null;
            if (target.Destroyed || target.GateDeparted || target.Derelict || target.Planetside) return null;
            // An enemy is fought as usual where anyone there is armed and willing; an enemy merchant alone, with no
            // escort or platforms to start that fight, is attacked as any other ship the player opens fire on.
            if (Diplomacy.Hostile(ship.Faction, target.Faction))
            {
                var fight = TryStartPlayerBattle(ship, body, offsetFromBodyKm, forward, jd);
                if (fight != null || ship.Battle != null) return fight;
            }
            var neighbourhood = ScaleModel.NeighbourhoodKm(Catalog[body]);
            if (offsetFromBodyKm.Length > neighbourhood) return null;
            var existing = BattleAt(body);
            if (existing != null)
            {
                var tu = existing.UnitOf(target);
                if (tu == null || target.Battle != existing || existing.UnitOf(ship) != null) return null;
                if ((offsetFromBodyKm - existing.Anchor).Length > Battle.EscapeKm * 0.8) return null;
                var joined = existing.AddShip(ship, 1 - tu.Side, offsetFromBodyKm - existing.Anchor, forward);
                joined.PlayerControlled = true;
                return existing;
            }
            if (target.Battle != null) return null;
            var leader = target.Controller;
            // Only a ship holding orbit here (parked, or waiting on a docking ring): one in flight keeps its own schedule.
            if (leader.Destroyed || leader.Derelict || leader.Battle != null || leader == world.PilotedShip || !Fleet.IsAt(leader, body)) return null;

            // Out at the root (a jump gate) only those near the fight join it, not the target's ships at every gate
            // (as TryStartBattle, U.SPC-T94).
            var present = PresentGroups(body, body == Catalog.Root.Index ? offsetFromBodyKm : (DVec3?)null, jd);
            var b = NewBattle(body, jd, present, offsetFromBodyKm);
            var unit = b.AddShip(ship, 0, DVec3.Zero, forward);
            unit.PlayerControlled = true;
            Join(b, leader, 1, false);
            foreach (var s in present)
                if (s.Faction == target.Faction && s.Battle == null) Join(b, s, 1, false);
            foreach (var st in Economy.At(body))
            {
                if (st.Owner != target.Faction) continue;
                for (int i = 0; i < st.Platforms.Count; i++)
                {
                    double a = Astro.TwoPi * i / Math.Max(1, st.Platforms.Count);
                    b.AddPlatform(st, st.Platforms[i], 1, Spot(b, 1) * 0.4 + new DVec3(Math.Cos(a), Math.Sin(a), 0.1) * 6);
                }
            }
            Battles.Add(b);
            world.Log(jd, $"{b.Name}: the {ship.Name} opens fire on the {target.Faction.Name} {target.Spec.DisplayName.ToLowerInvariant()} {target.Name}", WorldEventKind.Battle, Shown(b));
            BumpDanger(body, 1);
            BattleStarted?.Invoke(b);
            return b;
        }

        /// <summary>
        /// A patrol interdicts the player's wanted ship (see <see cref="Law.UpdatePilot"/>): a fight off
        /// <paramref name="body"/> where the player is, the patrol's group on the side enforcing the law
        /// (<see cref="Battle.EnforcingSide"/>). Null if either is already fighting, a battle is already on here,
        /// or the player is not in this body's neighbourhood.
        /// </summary>
        public Battle TryStartInterdiction(Ship ship, Ship patrol, int body, DVec3 offsetFromBodyKm, DVec3 forward, double jd) =>
            Enforce(ship, patrol, body, offsetFromBodyKm, forward, jd, "Interdiction off ",
                (leader, b) => $"{b.Name}: the {leader.Faction.Name} patrol {leader.Name} moves on the wanted {ship.Name}");

        /// <summary>
        /// A bounty hunter with a contract on the player's wanted ship has caught up with it (see
        /// <see cref="Outlaws.UpdatePilot"/>): a fight where the player is, the hunter's group enforcing the law as
        /// a patrol would. Null under the same conditions as <see cref="TryStartInterdiction"/>.
        /// </summary>
        public Battle TryStartHunt(Ship ship, Ship hunter, int body, DVec3 offsetFromBodyKm, DVec3 forward, double jd) =>
            Enforce(ship, hunter, body, offsetFromBodyKm, forward, jd, "Bounty hunt off ",
                (leader, b) => $"{b.Name}: the bounty hunter {leader.Name} closes on the wanted {ship.Name}");

        /// <summary>
        /// A group running the player's ship down has caught it (U.SPC-T273): the fight is where the player is, anywhere
        /// in open space, with the group's ships where the chase had them about it (no more than
        /// <see cref="PursuitStartKm"/> off). Joins the player's own fight if one is on. Null if the player is not the
        /// group's to fight.
        /// </summary>
        public Battle TryStartPursuit(Ship ship, Pursuer g, int body, DVec3 offsetFromBodyKm, DVec3 forward, double jd)
        {
            if (ship == null || ship.Destroyed || ship.Planetside || g == null || g.Leader.Destroyed) return null;
            var b = ship.Battle;
            int side;
            if (b != null)
            {
                var mine = b.UnitOf(ship);
                if (mine == null) return null;
                side = 1 - mine.Side;
            }
            else
            {
                b = new Battle(nextBattleId++, body, "Run down off " + Catalog[body].Name, offsetFromBodyKm, jd, rng.Next()) { AggressorSide = 1 };
                Hook(b);
                var approach = g.Pos - offsetFromBodyKm;
                b.ApproachAxis = approach.LengthSquared > 1e-9 ? approach.Normalized : DVec3.UnitX;
                var unit = b.AddShip(ship, 0, DVec3.Zero, forward);
                unit.PlayerControlled = true;
                side = 1;
                Battles.Add(b);
                world.Log(jd, $"{b.Name}: the {g.Faction.Name} {g.Leader.Name} runs down the {ship.Name}", WorldEventKind.Battle, Shown(b));
                BattleStarted?.Invoke(b);
            }
            foreach (var s in g.Ships)
            {
                if (s.Destroyed || s.Battle != null) continue;
                var at = Pursuit.OffsetOf(g, s) - b.Anchor;
                if (at.Length > PursuitStartKm) at = at.Normalized * PursuitStartKm;
                var face = (b.UnitOf(ship)?.Pos ?? DVec3.Zero) - at;
                b.AddShip(s, side, at, face.LengthSquared > 1e-12 ? face : DVec3.UnitX);
            }
            return b;
        }

        /// <summary>The furthest a chasing ship starts from the anchor of the fight it catches the player in, km.</summary>
        const double PursuitStartKm = 20;

        Battle Enforce(Ship ship, Ship patrol, int body, DVec3 offsetFromBodyKm, DVec3 forward, double jd, string name, Func<Ship, Battle, string> news)
        {
            if (ship == null || patrol == null || ship.Destroyed || ship.Battle != null || ship.Planetside) return null;
            var leader = patrol.Controller;
            if (leader.Destroyed || leader.Battle != null || leader.Derelict || leader == world.PilotedShip) return null;
            if (offsetFromBodyKm.Length > ScaleModel.NeighbourhoodKm(Catalog[body]) || BattleAt(body) != null) return null;
            var b = new Battle(nextBattleId++, body, name + Catalog[body].Name, offsetFromBodyKm, jd, rng.Next()) { EnforcingSide = 1, AggressorSide = 1 };
            Hook(b);
            var approach = DVec3.Cross(offsetFromBodyKm.LengthSquared > 1e-9 ? offsetFromBodyKm.Normalized : DVec3.UnitX, DVec3.UnitZ);
            b.ApproachAxis = approach.LengthSquared > 1e-6 ? approach.Normalized : DVec3.UnitX;
            var unit = b.AddShip(ship, 0, DVec3.Zero, forward);
            unit.PlayerControlled = true;
            Join(b, leader, 1, false);
            Battles.Add(b);
            world.Log(jd, news(leader, b), WorldEventKind.Law, Shown(b));
            BattleStarted?.Invoke(b);
            return b;
        }

        /// <summary>
        /// Starts a battle at <paramref name="body"/> if <paramref name="aggressor"/> has a quarrel with anyone there and
        /// is fit to pick it: one of its own warships or raiders there armed and willing, or the other side's platforms
        /// (whose guns open on whoever comes into range). Where only the other side would fight, it is theirs to start
        /// (see <see cref="OnArrived"/> and the daily sweep), with them as the aggressor, not the arrival of a hull that
        /// can only run (U.SPC-T290). <paramref name="byPlayer"/>: for
        /// the player's own ship, which is never one of the hulls counted here (see <see cref="TryStartPlayerBattle"/>).
        /// </summary>
        public Battle TryStartBattle(int body, Faction aggressor, double jd, DVec3? anchor = null) => TryStartBattle(body, aggressor, jd, anchor, false);

        Battle TryStartBattle(int body, Faction aggressor, double jd, DVec3? anchor, bool byPlayer)
        {
            // Off the system's root (out at a jump gate) a fight takes in only those near where it starts, not every ship
            // at every gate (U.SPC-T94).
            DVec3? near = null;
            if (body == Catalog.Root.Index) near = anchor ?? SpotOf(aggressor, body, jd);
            var present = PresentGroups(body, near, jd);
            var hostile = new HashSet<Faction>();
            bool armed = aggressor.IsMilitary || aggressor.IsPirate;
            foreach (var s in present)
                // A group withdrawing from a fight it lost is let go while it gets clear: no quarrel to pick with it
                // (it is still swept in if a fight starts here over someone else).
                if (Diplomacy.Hostile(aggressor, s.Faction) && !Withdrawing(s.Controller, jd))
                {
                    hostile.Add(s.Faction);
                    armed |= s.Spec.IsMilitary || s.Faction.IsPirate;
                }
            foreach (var st in Economy.At(body))
                if (st.Platforms.Count > 0 && Diplomacy.Hostile(aggressor, st.Owner))
                {
                    hostile.Add(st.Owner);
                    armed = true;
                }
            if (hostile.Count == 0 || !armed) return null;
            // Raiders falling on a port's merchants size them up first (U.SPC-T374): with no guns there to fight, they
            // spring only on a mark worth the heat (a civilian hull is left alone unless it is worth a great deal).
            if (aggressor.IsPirate && !byPlayer && !WorthAFight(aggressor, body, present, jd)) return null;

            // A fight needs someone fit and willing to start it: a healthy armed warship or a platform.
            bool aggressorArmed = false, defenderArmed = false, aggressorFresh = false;
            foreach (var s in present)
            {
                bool military = (s.Spec.IsMilitary || s.Faction.IsPirate) && Willing(s.Controller, jd) && s.Combat.Armed;
                // Only a group commanded to fight here opens the fight (U.SPC-T371); one merely passing through, or off its
                // beat, leaves the hostiles it meets alone. The player's own fight is the player's to start.
                if (s.Faction == aggressor && military && (byPlayer || Commanded(s, body))) aggressorArmed = true;
                if (s.Faction == aggressor && !Withdrawing(s.Controller, jd)) aggressorFresh = true;
                if (hostile.Contains(s.Faction) && military) defenderArmed = true;
            }
            bool platforms = false;
            foreach (var st in Economy.At(body))
                if (st.Platforms.Count > 0 && hostile.Contains(st.Owner)) platforms = true;
            // Platforms open on a hostile hull come into range, but not again on one that has only just run from them.
            if (!aggressorArmed && !(platforms && (aggressorFresh || byPlayer)) && !(byPlayer && defenderArmed)) return null;

            var b = NewBattle(body, jd, present, anchor ?? near);
            foreach (var s in present)
            {
                int side = SideFor(s.Faction, aggressor, hostile);
                if (side >= 0) Join(b, s, side, false);
            }
            foreach (var st in Economy.At(body))
            {
                int side = SideFor(st.Owner, aggressor, hostile);
                if (side < 0) continue;
                for (int i = 0; i < st.Platforms.Count; i++)
                {
                    double a = Astro.TwoPi * i / Math.Max(1, st.Platforms.Count);
                    var pos = Spot(b, side) * 0.4 + new DVec3(Math.Cos(a), Math.Sin(a), 0.1) * 6;
                    b.AddPlatform(st, st.Platforms[i], side, pos);
                }
            }
            Battles.Add(b);
            world.Log(jd, $"{b.Name}: {Describe(b, 0)} engage {Describe(b, 1)}", WorldEventKind.Battle, Shown(b));
            BumpDanger(body, 1.5);
            BattleStarted?.Invoke(b);
            return b;
        }

        /// <summary>
        /// Whether raiders of <paramref name="clan"/> at <paramref name="body"/> have anything there worth starting a fight
        /// over (U.SPC-T374): any hostile warship or platform to fight (the commanded-combat rules and the raid orders decide
        /// those), or else a merchant or civilian hull among <paramref name="present"/> worth the heat (<see cref="BestMark"/>).
        /// </summary>
        bool WorthAFight(Faction clan, int body, List<Ship> present, double jd)
        {
            foreach (var st in Economy.At(body))
                if (st.Platforms.Count > 0 && Diplomacy.Hostile(clan, st.Owner)) return true;
            var marks = new List<Ship>();
            double band = 0;
            foreach (var s in present)
            {
                if (s.Faction == clan)
                {
                    if (Willing(s.Controller, jd)) band += s.Combat.Strength;
                    continue;
                }
                if (!Diplomacy.Hostile(clan, s.Faction) || Withdrawing(s.Controller, jd)) continue;
                if (s.Spec.IsMilitary || s.Faction.IsPirate) return true;
                if (!s.IsEscort) marks.Add(s);
            }
            return marks.Count == 0 || BestMark(marks, 0, Math.Max(band, 1e-6)) != null;
        }

        /// <summary>A defence platform or gate fort lost (or taken) is struck off its settlement's or gate's list.</summary>
        static void StrikeOff(BattleUnit u)
        {
            u.Platform?.Platforms.Remove(u.Combat);
            u.Fort?.Forts.Remove(u.Combat);
        }

        /// <summary>How near a gate (km) a ship holding beside it counts as at it, for a gate battle (U.SPC-T92).</summary>
        public const double GateReachKm = 2000;

        readonly Dictionary<JumpGate, int> gateBattles = new Dictionary<JumpGate, int>();

        /// <summary>
        /// A gate battle (U.SPC-T92): <paramref name="attacker"/>'s war fleet, holding beside <paramref name="gate"/> (an end
        /// of it in this system), engages the gate's forts and whatever enemy warships hold there with them. Fought where the
        /// gate stands, off the system's root, like any other battle. Null if there is nothing there to fight, the fleet is
        /// in no shape to start it, or a fight is already on at the root.
        /// </summary>
        public Battle StartGateBattle(JumpGate gate, Faction attacker, double jd)
        {
            int root = Catalog.Root.Index;
            if (gate == null || gate.Home?.World != world || BattleAt(root) != null) return null;
            var present = new List<Ship>();
            foreach (var s in PresentGroups(root))
            {
                var o = s.Controller.Orbit;
                if (o != null && (o.OffsetAt(jd) - gate.PositionKm).Length <= GateReachKm) present.Add(s);
            }
            var hostile = new HashSet<Faction>();
            if (gate.Forts.Count > 0 && Diplomacy.Hostile(attacker, gate.Owner)) hostile.Add(gate.Owner);
            bool armed = false;
            foreach (var s in present)
            {
                bool military = (s.Spec.IsMilitary || s.Faction.IsPirate) && s.Combat.Armed;
                if (s.Faction == attacker && military && Willing(s.Controller, jd)) armed = true;
                else if (military && !s.Faction.IsPirate && Diplomacy.Hostile(attacker, s.Faction)) hostile.Add(s.Faction);
            }
            if (!armed || hostile.Count == 0) return null;

            gateBattles.TryGetValue(gate, out int n);
            gateBattles[gate] = n + 1;
            string name = (n == 0 ? "" : Ordinal(n + 1) + " ") + "Battle of the " + gate.Name;
            var b = new Battle(nextBattleId++, root, name, gate.PositionKm, jd, rng.Next());
            Hook(b);
            var approach = DVec3.Cross(gate.PositionKm.Normalized, DVec3.UnitZ);
            b.ApproachAxis = approach.LengthSquared > 1e-6 ? approach.Normalized : DVec3.UnitX;
            b.AggressorSide = 0;
            foreach (var s in present)
            {
                if (s.Faction.IsPirate) continue;
                int side = SideFor(s.Faction, attacker, hostile);
                if (side >= 0) Join(b, s, side, false);
            }
            if (hostile.Contains(gate.Owner))
                for (int i = 0; i < gate.Forts.Count; i++)
                {
                    double a = Astro.TwoPi * i / Math.Max(1, gate.Forts.Count);
                    var pos = Spot(b, 1) * 0.4 + new DVec3(Math.Cos(a), Math.Sin(a), 0.1) * 6;
                    b.AddFort(gate, gate.Forts[i], 1, pos);
                }
            Battles.Add(b);
            world.Log(jd, $"{b.Name}: {Describe(b, 0)} storm the gate against {Describe(b, 1)}", WorldEventKind.Battle, root);
            BumpDanger(root, 1.5);
            BattleStarted?.Invoke(b);
            return b;
        }

        /// <summary>Fit to pick a fight: not withdrawing and not badly hurt.</summary>
        bool Willing(Ship leader, double jd) =>
            !Withdrawing(leader, jd) && leader.Combat.HullDamage < 0.45 && leader.Combat.Armed;

        /// <summary>Pulling back after a fight it ran from or lost (see <see cref="Resolve"/>).</summary>
        bool Withdrawing(Ship leader, double jd) => retreating.TryGetValue(leader, out double until) && jd < until;

        int SideFor(Faction f, Faction aggressor, HashSet<Faction> hostile)
        {
            if (f == aggressor) return 0;
            if (hostile.Contains(f)) return 1;
            if (Diplomacy.Hostile(f, aggressor)) return 1;
            foreach (var h in hostile)
                if (Diplomacy.Hostile(f, h)) return 0;
            return -1;
        }

        /// <summary>Ships parked at a body and free to fight (escorts included), not already in a battle; given
        /// <paramref name="near"/>, only those within <see cref="GateFightRangeKm"/> of that point off the body.</summary>
        List<Ship> PresentGroups(int body, DVec3? near = null, double jd = 0)
        {
            var list = new List<Ship>();
            foreach (var s in Fleet.Ships)
            {
                // A ship under direct player control isn't swept into a fight the AI decides to start.
                if (s.Destroyed || s.Battle != null || s == world.PilotedShip) continue;
                var c = s.Controller;
                // A stripped, abandoned hulk is not a fighting group.
                if (c.Destroyed || s.Derelict || c.Derelict || !Fleet.IsAt(c, body)) continue;
                // Buses, taxis and people's own craft are no combatants (U.SPC-T375): they stand clear of a fight.
                if (c.Role == ShipRole.Commuter) continue;
                if (near is DVec3 at && (c.Orbit.OffsetAt(jd) - at).Length > GateFightRangeKm) continue;
                list.Add(s);
            }
            return list;
        }

        /// <summary>Where <paramref name="f"/>'s first group parked at <paramref name="body"/> is (km off it), or null.</summary>
        DVec3? SpotOf(Faction f, int body, double jd)
        {
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && !s.IsEscort && s.Faction == f && s != world.PilotedShip && Fleet.IsAt(s, body)) return s.Orbit.OffsetAt(jd);
            return null;
        }

        Battle NewBattle(int body, double jd, List<Ship> present, DVec3? at = null)
        {
            var b0 = Catalog[body];
            DVec3 dir = DVec3.Zero;
            foreach (var s in present)
                if (s.Controller.Orbit != null) dir += s.Controller.Orbit.OffsetAt(jd).Normalized;
            if (dir.LengthSquared < 1e-6) dir = new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, 0.2);
            var anchor = at ?? dir.Normalized * Battle.AnchorDistanceKm(b0);
            // A fight over a haven nobody has found yet must not name its rock: it is a skirmish
            // somewhere in the belt (counted apart, so the first named battle there is still the first).
            bool concealed = HidesHaven(body);
            string place = !concealed ? b0.Name : b0.TrojanPoint != LagrangePoint.None ? "the Trojans" : b0.Kind == BodyKind.Asteroid ? "the Belt" : "the Dark";
            int key = concealed ? (b0.Kind == BodyKind.Asteroid ? ConcealedBelt : ConcealedDark) : body;
            // A fight out at a jump gate is named for the gate (U.SPC-T94).
            if (body == Catalog.Root.Index && at is DVec3 spot && GateNear(spot) is JumpGate gate)
            {
                place = "the " + gate.Name;
                key = GateKey - world.Gates.IndexOf(gate);
            }
            battleCount.TryGetValue(key, out int n);
            battleCount[key] = n + 1;
            string kind = concealed ? "Skirmish in " : "Battle of ";
            string name = (n == 0 ? kind : Ordinal(n + 1) + " " + kind) + place;
            var b = new Battle(nextBattleId++, body, name, anchor, jd, rng.Next());
            Hook(b);
            var approach = DVec3.Cross(anchor.Normalized, DVec3.UnitZ);
            if (approach.LengthSquared < 1e-6) approach = DVec3.UnitX;
            b.ApproachAxis = approach.Normalized;
            return b;
        }

        // battleCount keys for concealed skirmishes and fights at gates (body indices are never negative).
        const int ConcealedBelt = -1, ConcealedDark = -2, GateKey = -10;

        /// <summary>The gate whose approach <paramref name="offsetKm"/> (off the system root) is on, or null.</summary>
        JumpGate GateNear(DVec3 offsetKm)
        {
            JumpGate best = null;
            double bestKm = GateFightRangeKm;
            foreach (var g in world.Gates)
            {
                double km = (g.PositionKm - offsetKm).Length;
                if (km < bestKm)
                {
                    bestKm = km;
                    best = g;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- gate ambushes

        /// <summary>
        /// Raiders lying in wait off a jump gate (U.SPC-T94, <see cref="Ship.StakeoutKm"/>) fall on whatever comes within
        /// <see cref="GateAmbushRangeKm"/> of them: ships held at the gate waiting to go through, ships just come out of it,
        /// merchants and navies alike -- unless the warships there are more than the band can take on, when it lets them be.
        /// Run every update, and by the gate traffic as ships go in and come out, so a quick passage between coarse steps is
        /// still caught.
        /// </summary>
        public void GateAmbushes(double jd)
        {
            if (world.Gates.Count == 0) return;
            int root = Catalog.Root.Index;
            if (BattleAt(root) != null || (lastBattleEndJd.TryGetValue(root, out double ended) && jd - ended < 0.1)) return;
            foreach (var lurker in Fleet.Ships)
            {
                if (lurker.Destroyed || lurker.IsEscort || !lurker.Faction.IsPirate || lurker.Runner || lurker.StakeoutKm == null) continue;
                if (lurker.Battle != null || lurker == world.PilotedShip || !Fleet.IsAt(lurker, root) || !Willing(lurker, jd)) continue;
                var at = lurker.Orbit.OffsetAt(jd);
                var marks = new List<Ship>();
                double guns = 0;
                Ship picket = null;
                foreach (var s in Fleet.Ships)
                {
                    if (s.Destroyed || s.Battle != null || s == world.PilotedShip || s.Derelict || !Fleet.IsAt(s.Controller, root)) continue;
                    if (!Diplomacy.Hostile(lurker.Faction, s.Faction) || (s.Controller.Orbit.OffsetAt(jd) - at).Length > GateAmbushRangeKm) continue;
                    if (s.Spec.IsMilitary || s.Faction.IsPirate) guns += s.Combat.Strength;
                    else if (!s.IsEscort) marks.Add(s);
                    if (picket == null && RaidDoctrine.IsPicket(s)) picket = s;
                }
                // The band sizes up what is there first (U.SPC-T374): the best mark worth the guns about it; with none, a gate
                // picket that would give it away (U.SPC-T480), or the warships alone, if they are few enough to take on.
                double band = GroupStrength(lurker);
                var prey = BestMark(marks, guns, band);
                bool silence = picket != null && guns <= band * RaidDoctrine.PicketNerve;
                if (prey == null && !silence && (guns <= 0 || guns > band * RaidDoctrine.BaseNerve)) continue;
                var b = TryStartBattle(root, lurker.Faction, jd, at);
                if (b == null) continue;
                var gate = GateNear(at);
                if (gate != null) gate.Ambushes++;
                world.Log(jd, $"{lurker.Faction.Name} raiders ({lurker.Name}) lying off the {gate?.Name ?? "gates"} fall on " +
                    (silence ? $"the picket {picket.Name}" : prey != null ? $"the {prey.Name}" : "the warships coming through"), WorldEventKind.Battle, root);
                return;
            }
        }

        /// <summary>
        /// A raider band's threat assessment (U.SPC-T374, see <see cref="RaidDoctrine"/>): of <paramref name="marks"/> (the
        /// merchant and civilian hulls in reach), the one most worth springing on with <paramref name="guns"/> of hostile
        /// strength about them and the band's own <paramref name="band"/>, or null when none is worth the risk or the heat.
        /// </summary>
        public Ship BestMark(List<Ship> marks, double guns, double band)
        {
            Ship best = null;
            double bestScore = double.NegativeInfinity;
            foreach (var s in marks)
            {
                double value = RaidDoctrine.PreyValue(s, world.Contracts);
                double heat = RaidDoctrine.Heat(s, world.Contracts);
                if (!RaidDoctrine.WorthTaking(value, heat, guns, band)) continue;
                double score = RaidDoctrine.Score(value, heat, guns, band);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>
        /// The gate a raider band of <paramref name="clan"/> would lie in wait off: the busiest (most traffic lately, see
        /// <see cref="JumpGate.Traffic"/>), or null when none has seen <see cref="MinGateTraffic"/>.
        /// </summary>
        public JumpGate BusiestGate(Faction clan, double jd)
        {
            JumpGate best = null;
            double bestScore = 0;
            foreach (var g in world.Gates)
            {
                if (g.Traffic < MinGateTraffic || !g.Working) continue;
                double score = g.Traffic * (0.7 + rng.NextDouble() * 0.6);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = g;
                }
            }
            return best;
        }

        /// <summary>Sends a raider band to lie in wait off <paramref name="gate"/> until <paramref name="untilJd"/>.</summary>
        public void StakeOut(Ship leader, JumpGate gate, double untilJd)
        {
            // Each band holds its own spot on the approach, a little off the line in.
            var axis = gate.PositionKm.Normalized;
            var side = DVec3.Cross(axis, Math.Abs(axis.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX).Normalized;
            leader.StakeoutKm = gate.ApproachKm + side * ((rng.NextDouble() - 0.5) * 400);
            leader.StakeoutUntilJd = untilJd;
        }

        /// <summary>Whether <paramref name="body"/> hosts a hidden haven nobody has found yet.</summary>
        bool HidesHaven(int body)
        {
            foreach (var st in Economy.At(body))
                if (!st.Discovered) return true;
            return false;
        }

        /// <summary>The body a battle's news names: none while it is fought over an undiscovered haven.</summary>
        int Shown(Battle b) => HidesHaven(b.Body) ? -1 : b.Body;

        /// <summary>Where a side's line starts: the two sides open about 26 km apart.</summary>
        static DVec3 Spot(Battle b, int side) => b.ApproachAxis * (side == 0 ? 13 : -13);

        void Join(Battle b, Ship ship, int side, bool late)
        {
            // A ship under direct player control enters a fight only through TryStartPlayerBattle.
            if (ship.Battle != null || ship.Destroyed || ship == world.PilotedShip) return;
            var groupCentre = late ? b.ApproachAxis * (side == 0 ? 30 : -30) : Spot(b, side);
            var jitter = new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, (rng.NextDouble() - 0.5) * 0.4) * 6;
            var face = -groupCentre;
            if (ship.IsEscort)
            {
                var leaderUnit = b.UnitOf(ship.Leader);
                if (leaderUnit == null && ship.Leader.Battle == null) Join(b, ship.Leader, side, late);
                if (ship.Battle != null) return;
                leaderUnit = b.UnitOf(ship.Leader);
                if (leaderUnit != null)
                {
                    var slot = ship.FormationSlot * (ship.Leader.LengthKm * 1.3 + 0.8);
                    leaderUnit.Basis(out var r, out var up);
                    var u = b.AddShip(ship, side, leaderUnit.Pos + r * slot.X + up * slot.Y + leaderUnit.Forward * slot.Z, leaderUnit.Forward);
                    u.Leader = leaderUnit;
                    u.Slot = slot;
                    return;
                }
            }
            b.AddShip(ship, side, groupCentre + jitter, face);
            foreach (var e in Fleet.Ships)
                if (e.Leader == ship && !e.Destroyed && e.Battle == null) Join(b, e, side, late);
        }

        void OnUnitDestroyed(Battle b, BattleUnit u)
        {
            double jd = b.StartJd + b.Seconds / Astro.SecondsPerDay;
            if (u.Ship != null)
            {
                // A merchant or civilian hull raiders destroy rather than rob draws heat on their clan (U.SPC-T374), worked
                // out before the hull is struck off (its jobs and cargo still on it).
                var raiders = u.Side == 0 || u.Side == 1 ? b.SideLeaders[1 - u.Side] : null;
                double heat = raiders != null && RaidDoctrine.Robs(raiders, u.Ship) ? RaidDoctrine.DestroyHeat(u.Ship, world.Contracts) : 0;
                u.Ship.Destroyed = true;
                losses.TryGetValue(u.Faction, out int n);
                losses[u.Faction] = n + 1;
                Wrecks.Add(u, b.Body, b.Anchor, jd, site: b.Site);
                if (heat > 0)
                {
                    HeatDrawn.TryGetValue(raiders, out double drawn);
                    HeatDrawn[raiders] = drawn + heat;
                    Heat?.Invoke(raiders, heat, jd);
                }
            }
            AbandonShip(b, u, jd);
            BumpDanger(b.Body, u.Ship != null && !u.Ship.Spec.IsMilitary ? 2.0 : 0.6);
        }

        /// <summary>
        /// A hull is going down in battle: whoever is still aboard runs for the escape pods (fewer make it if the
        /// reactor went), which launch from where it died and drift there broadcasting (see <see cref="Crews"/>).
        /// A battle's ejected fighter pilots drift together, one cluster to a side.
        /// </summary>
        void AbandonShip(Battle b, BattleUnit u, double jd)
        {
            var c = u.Combat;
            if (c == null) return;
            double aboard = c.Interior.Crew + c.Interior.Marines;
            if (aboard < 0.5) return;
            bool reactor = false;
            foreach (var sub in c.Subsystems)
                if (sub.Kind == SubsystemKind.Reactor && sub.Destroyed) reactor = true;
            var owners = u.Ship?.Faction ?? u.Faction;
            string name = u.Kind == UnitKind.Fighter ? owners.Name + " fighter pilots" : u.Name;
            string merge = u.Kind == UnitKind.Fighter ? $"battle:{b.Id}:fighters:{owners.Name}" : null;
            var haven = owners.IsPirate && u.Ship != null ? HomeHaven(u.Ship) : null;
            var pod = Crews.Launch(name, owners, aboard, c.Spec.Crew + c.Spec.Marines, reactor ? AbandonCause.ReactorLost : AbandonCause.Destroyed,
                b.Body, b.Anchor, u.Pos, b.Site, jd, merge, haven);
            // An outlaw captain may reach the pods; a captain in its brig breaks out into them (U.SPC-T63).
            if (u.Kind != UnitKind.Fighter) world.Outlaws?.OnHullLost(b, u, pod, reactor, jd);
            c.Interior.TakeCrew(c.Interior.Crew);
            c.Interior.TakeMarines(c.Interior.Marines);
        }

        // ---------------------------------------------------------------- after the battle

        void Resolve(Battle b, double jd)
        {
            Battles.Remove(b);
            Ended.Add(b);
            if (Ended.Count > 30) Ended.RemoveAt(0);
            BattleEnded?.Invoke(b, jd);
            // Breacher rounds still in space with their teams alive are left drifting, for a tow to bring in or pirates to
            // salvage (U.SPC-T519).
            foreach (var pod in DriftingRounds.CastAdrift(b, Crews, jd))
                world.Log(jd, $"A {pod.Name} is left drifting after the fight at {b.Name}: {pod.People:0} {pod.Owners.Name} marines " +
                    "alive inside, waiting on a tow", WorldEventKind.Loss, PodBody(pod));

            var lost = new int[2];
            var fled = new int[2];
            var lootTonnes = 0.0;
            var lootValue = 0.0;
            Faction lootClan = null;
            // Ships already let go of this battle (a pilot who left it) have moved on: whatever they are
            // doing now -- flying, another fight -- is not this battle's to reset.
            var left = new HashSet<BattleUnit>();
            foreach (var u in b.Units)
                if (u.Ship != null && u.Ship.Battle != b) left.Add(u);
            foreach (var u in b.Units)
            {
                if (u.Side < 0 || u.Side > 1) continue;
                if (u.Dead && u.Kind != UnitKind.Fighter) lost[u.Side]++;
                if (u.Fled) fled[u.Side]++;
                if (u.Kind == UnitKind.Platform && u.Dead) StrikeOff(u);
                // Boarders still fighting aboard anything that wasn't taken (a platform too) are gone with the battle.
                if (!u.Captured && !left.Contains(u)) u.Combat.Interior.BoardersGone();
                if (u.Ship == null || left.Contains(u)) continue;
                var ship = u.Ship;
                ship.Battle = null;
                if (u.Dead)
                {
                    if (ship.HasCargo)
                    {
                        Economy.Release(ship);
                        if (b.SideLeaders[1 - u.Side] != null && b.SideLeaders[1 - u.Side].IsPirate)
                        {
                            lootClan = b.SideLeaders[1 - u.Side];
                            lootTonnes += ship.CargoTonnes * 0.6;
                            lootValue += ship.CargoTonnes * 0.6 * GoodInfo.Of(ship.CargoGood).BasePrice;
                            DeliverLoot(lootClan, ship.CargoGood, ship.CargoTonnes * 0.6, b);
                            // What they hauled off is no longer in the wreck to salvage.
                            if (Wrecks.Of(ship) is Wreck wreck)
                                for (int i = 0; i < wreck.Salvage.Count; i++)
                                    if (wreck.Salvage[i].Good == ship.CargoGood)
                                        wreck.Salvage[i] = new SalvageStack(ship.CargoGood, wreck.Salvage[i].Tonnes * 0.4);
                        }
                        ship.HasCargo = false;
                    }
                    ship.CargoDestination = null;
                    ship.Cargo = null;
                }
            }
            foreach (var u in b.Units)
                if (u.Ship != null && u.Dead) Succession(u.Ship);
            foreach (var u in b.Units)
            {
                if (!captures.TryGetValue(u, out var captor)) continue;
                captures.Remove(u);
                if (!u.Captured || u.Dead) continue;
                // A hull allies took together is theirs to settle first (U.SPC-T392): whoever keeps it disposes of it.
                var keeper = SettleJointPrize(b, u, captor, jd);
                if (keeper != null) DisposeOfPrize(b, u, keeper, jd);
            }

            // Survivors go back to their own schedules; those that ran, or lost, withdraw to refit.
            foreach (var u in b.Units)
            {
                if (u.Ship == null || u.Dead || u.Ship.IsEscort || u.Ship.Destroyed || u.Ship.Derelict) continue;
                // The player's ship stays the player's: its schedule is set again only on release.
                if (u.Ship == world.PilotedShip || left.Contains(u)) continue;
                bool beaten = u.Fled || (b.Winner >= 0 && u.Side != b.Winner);
                // Pulled out of a lane: back in and on their way. Raiders who won take the loot home;
                // beaten ones run.
                if (u.Ship.Phase == ShipPhase.Adrift)
                {
                    Fleet.ResumeLane(u.Ship, jd);
                    continue;
                }
                if (u.Ship.Phase == ShipPhase.Lurk)
                {
                    if (beaten || b.Winner == u.Side)
                    {
                        retreating[u.Ship] = jd + (beaten ? 6 : 4);
                        Fleet.ResumeLane(u.Ship, jd);
                    }
                    continue;
                }
                if (beaten) retreating[u.Ship] = jd + 6;
                // The beaten leave at once; everyone else keeps their plans unless the fight overran them.
                if (beaten || u.Ship.PhaseEndJd <= jd || !(u.Ship.Spec.IsMilitary || u.Ship.Faction.IsPirate)) Fleet.ScheduleNext(u.Ship, jd);
            }
            // A navy that beats raiders off may follow the survivors back to a hidden haven.
            var victor = b.Winner >= 0 ? b.SideLeaders[b.Winner] : null;
            if (victor != null && !victor.IsPirate)
                foreach (var u in b.Units)
                {
                    if (u.Ship == null || u.Dead || u.Ship.IsEscort || !u.Ship.Faction.IsPirate || u.Side == b.Winner) continue;
                    if (rng.NextDouble() < TrailChance) TrailHome(u.Ship, victor, jd);
                }
            lastBattleEndJd[b.Body] = jd;
            RecoverWings(b, jd);

            string winner = b.Winner >= 0 ? Describe(b, b.Winner) + " hold the field" : "both sides break off";
            string text = $"{b.Name} ends: {winner}. Losses {Short(b, 0)} {lost[0]}, {Short(b, 1)} {lost[1]}";
            if (lootTonnes > 0) text += $"; pirates take {lootTonnes:N0} t of cargo";
            world.Log(jd, text, WorldEventKind.Battle, Shown(b));
            if (lootValue > 0 && lootClan != null)
            {
                lootClan.Treasury += lootValue * 0.3;
                Plundered?.Invoke(lootClan, lootValue, jd);
            }
        }

        /// <summary>
        /// The craft still flying when the fight ends (U.SPC-T473, see <see cref="StrandedWings"/>): each is taken back aboard
        /// the carrier it lands on, if that is still on the field; one whose carrier was lost or gone lands on another
        /// friendly carrier with room, or a station on the field, or puts in at a friendly port at the world the fight was
        /// over; failing all of those it is lost, its pilot ejecting into the pods.
        /// </summary>
        void RecoverWings(Battle b, double jd)
        {
            int lost = 0;
            var putIn = new Dictionary<string, int>();
            void PutIn(string at) => putIn[at] = (putIn.TryGetValue(at, out int n) ? n : 0) + 1;
            foreach (var u in b.Units)
            {
                if (u.Kind != UnitKind.Fighter || u.Fighter == null) continue;
                // Already landed on the carrier or station it found when its own was lost.
                if (u.Docked && u.Fighter.Home != null)
                {
                    StrandHull(u, u.Fighter.Home);
                    PutIn(u.Fighter.Home.Name);
                    continue;
                }
                if (!u.Present) continue;
                var home = u.Fighter.LandsOn;
                if (!StrandedWings.CanLandOn(home))
                {
                    home = StrandedWings.Haven(b, u);
                    if (home != null) u.Fighter.Home = home;
                }
                if (home != null)
                {
                    FighterAI.TakeAboard(home, u);
                    u.Docked = true;
                    StrandHull(u, home);
                    if (u.Fighter.Home != null) PutIn(home.Name);
                    continue;
                }
                var port = StrandedWings.Port(b, u.Faction, Economy, Diplomacy);
                if (port != null)
                {
                    u.Fled = true;
                    if (u.CraftHull != null) CarrierWings.Strand(port, u.CraftHull);
                    PutIn(port.Name);
                    continue;
                }
                // Nowhere to land: the craft is lost and its pilot ejects.
                u.Dead = true;
                lost++;
                AbandonShip(b, u, jd);
            }
            if (putIn.Count == 0 && lost == 0) return;
            var parts = new List<string>();
            foreach (var kv in putIn) parts.Add($"{kv.Value} put in at {kv.Key}");
            if (lost > 0) parts.Add($"{lost} lost with nowhere to land, their pilots ejecting");
            world.Log(jd, $"{b.Name}: craft stranded by the loss of their carriers: " + string.Join(", ", parts), WorldEventKind.Battle, Shown(b));
        }

        /// <summary>A carrier craft that landed on a station keeps its own hull there (U.SPC-T591), held until its carrier calls;
        /// one docked in a hangar needs nothing.</summary>
        static void StrandHull(BattleUnit u, BattleUnit home)
        {
            var hull = u.CraftHull;
            if (hull == null || home.Platform == null || CarrierWings.DockedIn(home.Combat, hull)) return;
            CarrierWings.Strand(home.Platform, hull);
        }

        /// <summary>
        /// Loot taken by <paramref name="clan"/> (U.SPC-T94): into the clan's own haven in this system, if it has one standing;
        /// else into the hold of its band in the fight, to carry home through the gates; else it is lost.
        /// </summary>
        void DeliverLoot(Faction clan, Good g, double tonnes, Battle b)
        {
            if (tonnes <= 0) return;
            var haven = LiveHavenOf(clan);
            if (haven != null)
            {
                haven.Stock[(int)g] += tonnes;
                return;
            }
            if (b == null) return;
            foreach (var u in b.Units)
            {
                var c = u.Ship?.Controller;
                if (c == null || u.Dead || c.Destroyed || c.Faction != clan || c.Runner) continue;
                c.Plunder ??= new double[GoodInfo.Count];
                c.Plunder[(int)g] += tonnes;
                return;
            }
        }

        /// <summary>A standing haven of <paramref name="clan"/>'s in this system, or null.</summary>
        public Settlement LiveHavenOf(Faction clan)
        {
            foreach (var s in Economy.Settlements)
                if (s.Kind == SettlementKind.Haven && !s.Fallen && s.Owner == clan) return s;
            return null;
        }

        /// <summary>A raider band at a standing haven of its own clan unloads its plunder into the haven's stores.</summary>
        public void UnloadPlunder(Ship ship, int body)
        {
            if (ship?.Plunder == null || ship.Runner) return;
            foreach (var st in Economy.At(body))
            {
                if (st.Kind != SettlementKind.Haven || st.Fallen || st.Owner != ship.Faction) continue;
                for (int i = 0; i < GoodInfo.Count; i++) st.Stock[i] += ship.Plunder[i];
                ship.Plunder = null;
                return;
            }
        }

        /// <summary>When a flagship is lost its strongest surviving escort takes the group.</summary>
        /// <summary>
        /// The player takes the helm of a group's flagship: its escorts carry on under the strongest of
        /// them (as when a flagship is lost) rather than holding station on a hull that no longer runs
        /// the group's schedule -- frozen at the spot piloting began, or left behind for good if it jumps
        /// through the gate. Also used when a flagship crosses a jump gate without some of its escorts
        /// (still fighting, or left hulks): those stay here under the strongest of them (U.SPC-T310).
        /// <paramref name="keepHunter"/> leaves a bounty hunter's place on the boards with the old
        /// flagship (a hunt voyage takes it up again when it comes home) rather than handing it on.
        /// </summary>
        public void HandOverCommand(Ship leader, bool keepHunter = false)
        {
            if (leader == null || leader.IsEscort) return;
            Succession(leader, keepHunter);
        }

        /// <summary>
        /// A ship gone from this system for good without a successor taking its place (through a jump gate or its own
        /// cruise drive, the same hull living on in another system's fleet): its orders and per-ship bookkeeping here
        /// go with it (U.SPC-T311).
        /// </summary>
        public void Forget(Ship ship)
        {
            if (ship == null) return;
            Orders.Remove(ship);
            PriorOrders.Remove(ship);
            retreating.Remove(ship);
            laneSeen.Remove(ship);
            pulseReady.Remove(ship);
        }

        /// <summary>
        /// Drops a ship from the retreat and lane-ambush cooldowns once its cooldown is over or it is gone for good
        /// (destroyed, or through a gate without <see cref="Forget"/> being called for it: a ship lost to the derelict
        /// decay in <see cref="Daily"/>, a scuttling, a prize's disposal, and every other way a hull leaves the fleet
        /// never visits <see cref="Forget"/>), so the books do not grow for ever as a long game runs on (U.SPC-T321's
        /// fix for <see cref="Pursuit"/>'s own lastChased, never applied here).
        /// </summary>
        void PruneTracking(double jd)
        {
            List<Ship> gone = null;
            foreach (var kv in retreating)
                if (kv.Key.Destroyed || kv.Key.GateDeparted || jd >= kv.Value) (gone ??= new List<Ship>()).Add(kv.Key);
            if (gone != null) foreach (var s in gone) retreating.Remove(s);
            gone = null;
            foreach (var kv in pulseReady)
                if (kv.Key.Destroyed || kv.Key.GateDeparted || jd >= kv.Value) (gone ??= new List<Ship>()).Add(kv.Key);
            if (gone != null) foreach (var s in gone) pulseReady.Remove(s);
            // Orders has its own cleanup on a leader's death in battle (Succession), but a ship that retires through a
            // gate while it still holds one (GateTransit's instant crossing of the player's own flown ship never calls
            // Forget, unlike InterstellarTraffic's AI crossing) would otherwise sit in this book forever too.
            gone = null;
            foreach (var kv in Orders)
                if (kv.Key.Destroyed || kv.Key.GateDeparted) (gone ??= new List<Ship>()).Add(kv.Key);
            if (gone != null) foreach (var s in gone) Orders.Remove(s);
            gone = null;
            foreach (var kv in PriorOrders)
                if (kv.Key.Destroyed || kv.Key.GateDeparted) (gone ??= new List<Ship>()).Add(kv.Key);
            if (gone != null) foreach (var s in gone) PriorOrders.Remove(s);
        }

        /// <summary>The group <paramref name="dead"/> led goes on under an heir: <paramref name="chosen"/> if given (one of its
        /// escorts), else a command ship among them (U.SPC-T477), else the biggest.</summary>
        void Succession(Ship dead, bool keepHunter = false, Ship chosen = null)
        {
            if (dead.IsEscort) return;
            Ship heir = chosen != null && chosen.Leader == dead && !chosen.Destroyed ? chosen : null;
            bool command = false;
            if (heir == null)
                foreach (var s in Fleet.Ships)
                {
                    if (s.Leader != dead || s.Destroyed) continue;
                    bool c = FleetCoordination.IsCommandShip(s);
                    if (heir == null || (c && !command) || (c == command && s.Combat.Spec.Hull > heir.Combat.Spec.Hull))
                    {
                        heir = s;
                        command = c;
                    }
                }
            if (heir == null)
            {
                // A hunt group wiped out abroad: nobody comes home to inherit the row (U.SPC-T588).
                if (dead.Destroyed) dead.Voyage?.LostWithAllHands();
                Orders.Remove(dead);
                PriorOrders.Remove(dead);
                return;
            }
            heir.Leader = null;
            // A merchant's hired wing (U.SPC-T275) has no patrol route to take over: it goes its own way.
            heir.Role = dead.Route != null && dead.Route.Length > 0 ? ShipRole.Patrol : ShipRole.Free;
            heir.Route = dead.Route;
            heir.RouteIndex = dead.RouteIndex;
            heir.Phase = dead.Phase;
            // The old flagship is not always gone (a capture leaves it flying on under new colours): take
            // over a little way along its orbit rather than sharing its exact spot with it indefinitely.
            heir.Orbit = OwnSpot(dead.Orbit, heir, dead);
            heir.DepartureOrbit = dead.DepartureOrbit;
            heir.DestinationBody = dead.DestinationBody;
            heir.OriginBody = dead.OriginBody;
            heir.PhaseStartJd = dead.PhaseStartJd;
            heir.PhaseEndJd = dead.PhaseEndJd;
            heir.FrameBody = dead.FrameBody;
            heir.Path = dead.Path;
            heir.PathCumulative = dead.PathCumulative;
            heir.PathLength = dead.PathLength;
            heir.AccelKmPerDay2 = dead.AccelKmPerDay2;
            heir.Conic = dead.Conic;
            heir.GravityLossKmS = dead.GravityLossKmS;
            heir.DepartureVelocity = dead.DepartureVelocity;
            heir.ArrivalVelocity = dead.ArrivalVelocity;
            heir.Journey = dead.Journey;
            heir.JourneyIndex = dead.JourneyIndex;
            heir.FinalBody = dead.FinalBody;
            heir.LaneLeg = dead.LaneLeg;
            heir.LaneFromOffset = dead.LaneFromOffset;
            heir.LaneToOffset = dead.LaneToOffset;
            heir.LaneLengthKm = dead.LaneLengthKm;
            heir.LaneStartProgress = dead.LaneStartProgress;
            heir.LaneEndProgress = dead.LaneEndProgress;
            heir.LaneHold = dead.LaneHold;
            heir.LaneHoldSide = dead.LaneHoldSide;
            // A band staked out off a gate keeps its watch under the new flagship (GateAmbushes looks only at a
            // leader's stake-out); the old one, if it flies on, keeps none.
            heir.StakeoutKm = dead.StakeoutKm;
            heir.StakeoutUntilJd = dead.StakeoutUntilJd;
            dead.StakeoutKm = null;
            CopyRest(dead, heir);
            // The old flagship's hold of plunder goes on under the new one, not stranded on a hull that no
            // longer runs the group's schedule (U.SPC-T308). A flagship shot out from under the band takes its
            // hold down with it.
            if (!dead.Destroyed) MovePlunder(dead, heir);
            // An interstellar voyage goes on under the new flagship; one held at its gate goes again.
            heir.Voyage = dead.Voyage;
            heir.GateKm = dead.GateKm;
            if (heir.Voyage != null) heir.Voyage.AtGate = false;
            dead.Voyage = null;
            dead.GateKm = null;
            foreach (var s in Fleet.Ships)
                if (s.Leader == dead && s != heir) s.Leader = heir;
            if (Orders.TryGetValue(dead, out var o)) Orders[heir] = o;
            // The objective its polity's command gave the group goes on under the heir (U.SPC-T423).
            world.Polities?.Command.OnSuccession(dead, heir);
            if (PriorOrders.TryGetValue(dead, out var prior)) PriorOrders[heir] = prior;
            Orders.Remove(dead);
            PriorOrders.Remove(dead);
            if (!keepHunter) world.Outlaws?.OnSuccession(dead, heir);
        }

        // ---------------------------------------------------------------- orders

        /// <summary>Fleet hook: where a warship or raider group goes next (its own body to hold), or -1.</summary>
        public int ChooseNext(Ship leader, int here, double jd)
        {
            // Bounty hunters go where their contracts take them (U.SPC-T62).
            if (Hunter(leader)) return world.Outlaws.ChooseNext(leader, here, jd);
            // A navy's supply convoy is led by a freighter but takes its orders from the navy (U.SPC-T375).
            if (!leader.Spec.IsMilitary && !leader.Faction.IsPirate && !leader.Convoy) return -1;
            var order = Decide(leader, here, jd);
            if (!Orders.TryGetValue(leader, out var current) || current.Kind != order.Kind || current.Body != order.Body)
            {
                if (current != null) PriorOrders[leader] = current;
                order.SinceJd = jd;
                Orders[leader] = order;
            }
            return order.Body;
        }

        public double Dwell(Ship ship, double normal)
        {
            if (retreating.TryGetValue(ship, out double until) && lastJd < until) return 0.05;
            if (!Orders.TryGetValue(ship, out var o)) return normal;
            switch (o.Kind)
            {
                case OrderKind.Repair:
                case OrderKind.Siege:
                case OrderKind.Assault:
                case OrderKind.Defend: return 1.0;
                case OrderKind.Raid: return 1.5 + rng.NextDouble();
                case OrderKind.GateWatch:
                case OrderKind.HoldGate: return 0.5;
                case OrderKind.Blockade: return 1.0;
                case OrderKind.Escort: return 0.1;
                case OrderKind.Explore: return 0.3;
                default: return normal;
            }
        }

        GroupOrder Decide(Ship leader, int here, double jd)
        {
            var f = leader.Faction;
            bool withdrawing = retreating.TryGetValue(leader, out double until) && jd < until;
            if (f.IsPirate) return DecidePirate(leader, here, jd, withdrawing);
            // A fresh plan: a lane picket planned earlier and not yet reached no longer stands (U.SPC-T479).
            leader.LurkLane = null;
            if (leader.PicketGate != null) return DecidePicket(leader, here, jd, withdrawing);
            if (Corvettes.Stationed(leader)) return DecideCorvette(leader, here, jd, withdrawing);
            if (leader.PatrolWing) return DecideWing(leader, here, jd, withdrawing);
            // A fresh plan: a gate it was holding is held again only if it is still the order (U.SPC-T374).
            var held = leader.StakeoutKm;
            leader.StakeoutKm = null;

            if (withdrawing || GroupDamage(leader) > 0.22 || MissilesLow(leader) || !leader.Combat.Armed || HangarsLow(leader) || MarinesLow(leader))
            {
                int yard = NearestYard(f, here, jd);
                if (yard >= 0) return new GroupOrder { Kind = OrderKind.Repair, Body = yard };
            }

            // Its polity's command has given it an objective (U.SPC-T423): that comes before its own plans.
            if (Directed(leader, here, jd) is GroupOrder directed) return directed;

            // Defend: an own port with enemies at it.
            int threatened = -1;
            double bestDays = double.MaxValue;
            foreach (var s in Economy.Settlements)
            {
                if (s.Owner != f) continue;
                if (!EnemiesAt(s.Body, f)) continue;
                double days = s.Body == here ? 0 : Fleet.EstimateTravelDays(leader, here, s.Body, jd);
                if (days < bestDays && days < 40)
                {
                    bestDays = days;
                    threatened = s.Body;
                }
            }
            if (threatened >= 0) return new GroupOrder { Kind = OrderKind.Defend, Body = threatened };

            // A gate picket has warned of hostiles come in at one of its own gates (U.SPC-T480): command sends a group
            // strong enough to meet them there while they are still at the gate, before they are loose in the system.
            if (!IsHomeGuard(leader) && GateWarned(leader, here, jd, held) != null)
                return new GroupOrder { Kind = OrderKind.HoldGate, Body = Catalog.Root.Index };

            // At a haven it found, with the garrison broken: stay and finish the job.
            foreach (var s in Economy.At(here))
                if (Assailable(s, f) && s.Platforms.Count == 0)
                    return new GroupOrder { Kind = OrderKind.Assault, Body = here };

            // Besieging: stay until the port falls or defenders show up.
            foreach (var s in Economy.At(here))
                if (Diplomacy.Hostile(f, s.Owner) && !s.Owner.IsPirate && s.Platforms.Count == 0 && s.DaysSinceCapture > CaptureGraceDays && AtWar(f))
                    return new GroupOrder { Kind = OrderKind.Siege, Body = here };

            bool atWar = AtWar(f);
            // Cut the enemy's lanes (U.SPC-T481): fall on a lane tender of a power it is at war with, if one is in reach.
            if (atWar && !IsHomeGuard(leader) && world.LaneUpkeep != null)
            {
                int tender = world.LaneUpkeep.HuntTarget(leader, here, jd);
                if (tender >= 0) return new GroupOrder { Kind = OrderKind.Hunt, Body = tender };
            }
            if (atWar && !IsHomeGuard(leader))
            {
                double strength = GroupStrength(leader);
                int target = -1;
                double best = 0;
                foreach (var s in Economy.Settlements)
                {
                    if (!Diplomacy.Hostile(f, s.Owner) || s.Owner.IsPirate) continue;
                    double defence = DefenceAt(s.Body, f);
                    if (strength < defence * 1.25) continue;
                    double days = Fleet.EstimateTravelDays(leader, here, s.Body, jd);
                    double value = (s.Population / 1e5 + 2) * (s.HasShipyard ? 2 : 1) / (defence + 200) / (days + 5);
                    if (value > best)
                    {
                        best = value;
                        target = s.Body;
                    }
                }
                if (target >= 0) return new GroupOrder { Kind = OrderKind.Attack, Body = target };
            }

            // Hunt pirates working an own port -- that its ships or stations have seen there (U.SPC-T66).
            var at = world.Sensors?.HelioOf(leader, jd) ?? default;
            foreach (var s in Economy.Settlements)
                if (s.Owner == f && s.Body != here && PiratesSeenAt(s.Body, f, at, jd))
                    return new GroupOrder { Kind = OrderKind.Hunt, Body = s.Body };

            // Hold one of its own jump gates that raiders are known to be lying off (U.SPC-T374): command sends a group
            // strong enough to clear them, which takes station on the gate's approach and fights them there.
            if (!IsHomeGuard(leader) && GateToHold(leader, here, at, jd, held) != null)
                return new GroupOrder { Kind = OrderKind.HoldGate, Body = Catalog.Root.Index };

            // Strike a hidden haven this navy has found, if the group can take its garrison.
            if (!IsHomeGuard(leader) && AssaultTarget(leader, here, jd) is Settlement mark)
                return new GroupOrder { Kind = OrderKind.Assault, Body = mark.Body };

            // A group with a field projector fitted to one of its hulls may hold a picket on a troubled lane out of the system (U.SPC-T479, U.SPC-T507).
            if (!IsHomeGuard(leader) && LanePicket(leader, here, PatrolWings.Ports(world, f), false) is GroupOrder picket) return picket;

            return new GroupOrder { Kind = OrderKind.Patrol, Body = NextPatrol(leader, here) };
        }

        /// <summary>
        /// The order that carries out the objective <paramref name="leader"/>'s polity has given its group (U.SPC-T423, see
        /// <see cref="StrategicCommand"/>), or null when it has none (or has just finished it) and its own command plans as
        /// before. Take: storm a haven, besiege an enemy port whose defences are broken, else attack it; taken, it holds it.
        /// Hold: defend the place. Blockade: lie over the port. Escort: go where its charge is or is bound. Explore: on to the
        /// nearest world not yet visited, standing down when every one has been. An objective in another system keeps the
        /// group in port here until command sends it there.
        /// </summary>
        GroupOrder Directed(Ship leader, int here, double jd)
        {
            var command = world.Polities?.Command;
            var o = command?.ObjectiveOf(leader);
            if (o == null) return null;
            var f = leader.Faction;
            if (o.Where != world) return new GroupOrder { Kind = OrderKind.Patrol, Body = here };
            switch (o.Kind)
            {
                case ObjectiveKind.Take:
                {
                    Settlement target = null;
                    double bestDays = double.MaxValue;
                    foreach (var s in Economy.Settlements)
                    {
                        if (!command.Enemy(f, s) || (o.Body >= 0 ? s.Body != o.Body : s.Kind == SettlementKind.Haven)) continue;
                        double days = s.Body == here ? 0 : Fleet.EstimateTravelDays(leader, here, s.Body, jd);
                        if (days < bestDays)
                        {
                            bestDays = days;
                            target = s;
                        }
                    }
                    if (target == null)
                    {
                        // Nothing left there held by an enemy: taken (or the war is over and it was given back); it holds the place.
                        int held = o.Body;
                        if (held < 0)
                            foreach (var s in Economy.Settlements)
                                if (s.Owner == f && !s.Fallen && (held < 0 || s.Population > PopulationAt(held, f))) held = s.Body;
                        if (held < 0 || !OwnsAt(held, f))
                        {
                            // Not taken by it: the war that made it an enemy's is over.
                            command.Complete(leader, world, jd, $"The {leader.Name}'s group stands down: nothing there is an enemy's to take now");
                            return null;
                        }
                        command.Taken(leader, world, held, jd);
                        return new GroupOrder { Kind = OrderKind.Defend, Body = command.ObjectiveOf(leader).Body };
                    }
                    if (target.Kind == SettlementKind.Haven) return new GroupOrder { Kind = OrderKind.Assault, Body = target.Body };
                    if (target.Body == here && target.Platforms.Count == 0 && target.DaysSinceCapture > CaptureGraceDays)
                        return new GroupOrder { Kind = OrderKind.Siege, Body = here };
                    return new GroupOrder { Kind = OrderKind.Attack, Body = target.Body };
                }
                case ObjectiveKind.Hold:
                    return new GroupOrder { Kind = OrderKind.Defend, Body = o.Body >= 0 ? o.Body : here };
                case ObjectiveKind.Blockade:
                {
                    foreach (var s in Economy.At(o.Body))
                        if (command.Blockadable(f, s)) return new GroupOrder { Kind = OrderKind.Blockade, Body = o.Body };
                    command.Complete(leader, world, jd, $"The {leader.Name}'s group lifts its blockade of {Catalog[o.Body].Name}: the port is no enemy's now");
                    return null;
                }
                case ObjectiveKind.Escort:
                {
                    var charge = o.Charge?.Controller;
                    if (charge == null || charge.Destroyed || !Fleet.IsPresent(charge)) return new GroupOrder { Kind = OrderKind.Patrol, Body = here };
                    int bound = BoundFor(charge);
                    return new GroupOrder { Kind = OrderKind.Escort, Body = bound >= 0 ? bound : here };
                }
                case ObjectiveKind.Explore:
                {
                    var worlds = new List<int>();
                    if (o.Body >= 0) worlds.Add(o.Body);
                    else
                    {
                        foreach (int b in world.FreeShipPorts())
                            if (!worlds.Contains(b)) worlds.Add(b);
                        foreach (var s in Economy.Settlements)
                            if (!worlds.Contains(s.Body)) worlds.Add(s.Body);
                    }
                    if (worlds.Contains(here) && !o.Visited.Contains(here)) o.Visited.Add(here);
                    int next = -1;
                    double nearest = double.MaxValue;
                    var from = world.Ephemeris.HelioAt(here, jd);
                    foreach (int b in worlds)
                    {
                        if (o.Visited.Contains(b)) continue;
                        double d = (world.Ephemeris.HelioAt(b, jd) - from).Length;
                        if (d < nearest)
                        {
                            nearest = d;
                            next = b;
                        }
                    }
                    if (next < 0)
                    {
                        command.Complete(leader, world, jd, $"The {leader.Name}'s group has explored every world it was sent to ({o.Visited.Count}) and stands down");
                        return null;
                    }
                    return new GroupOrder { Kind = OrderKind.Explore, Body = next };
                }
            }
            return null;
        }

        bool OwnsAt(int body, Faction f)
        {
            foreach (var s in Economy.At(body))
                if (s.Owner == f && !s.Fallen) return true;
            return false;
        }

        double PopulationAt(int body, Faction f)
        {
            double p = 0;
            foreach (var s in Economy.At(body))
                if (s.Owner == f && !s.Fallen) p += s.Population;
            return p;
        }

        /// <summary>Where a ship is or is going: the fight it is in, its trip's end, or the body it holds at.</summary>
        static int BoundFor(Ship c)
        {
            if (c.Battle != null) return c.Battle.Body;
            if (c.Phase == ShipPhase.Transit || c.Phase == ShipPhase.Lane || c.Phase == ShipPhase.Lurk || c.Phase == ShipPhase.Adrift)
                return c.FinalBody >= 0 && c.Journey != null ? c.FinalBody : c.DestinationBody;
            if ((c.Phase == ShipPhase.Queued || c.Planetside) && c.Ring != null) return c.Ring.Body;
            return c.Orbit?.Body ?? -1;
        }

        /// <summary>How long a navy group holds a gate it was sent to (days on station after it gets there) before command
        /// looks again (U.SPC-T374).</summary>
        public const double HoldGateDays = 3;

        /// <summary>
        /// The jump gate command sends navy group <paramref name="leader"/> to hold (U.SPC-T374), or null: one of its own
        /// navy's gates here with raiders known to be lying off it (in its picture, see <see cref="Sensors"/>; every band
        /// staked out there when there are no sensors), which the group is strong enough to clear and can reach within a
        /// month. Already holding one (<paramref name="held"/>, its station on the approach) it stays on it while the watch
        /// lasts. Sets the group's station on the approach (<see cref="Ship.StakeoutKm"/>), where the fight is fought.
        /// </summary>
        JumpGate GateToHold(Ship leader, int here, DVec3 at, double jd, DVec3? held)
        {
            var f = leader.Faction;
            int root = Catalog.Root.Index;
            if (held is DVec3 spot && jd < leader.StakeoutUntilJd && GateNear(spot) is JumpGate holding && holding.Owner == f)
            {
                leader.StakeoutKm = spot;
                return holding;
            }
            if (world.Gates.Count == 0) return null;
            double strength = GroupStrength(leader);
            JumpGate best = null;
            double bestBand = 0;
            foreach (var g in world.Gates)
            {
                if (g.Owner != f) continue;
                double band = RaidersOffGate(g, f, at, jd);
                if (band <= 0 || strength < band * 1.25) continue;
                if (band > bestBand)
                {
                    bestBand = band;
                    best = g;
                }
            }
            if (best == null) return null;
            double days = here == root ? 0 : Fleet.EstimateTravelDays(leader, here, root, jd);
            if (days > 30) return null;
            StakeOut(leader, best, jd + days + HoldGateDays);
            return best;
        }

        /// <summary>The warnings this system's gate pickets have given (U.SPC-T480, see <see cref="GatePickets"/>), oldest
        /// first (the last <see cref="GatePickets.WarningsKept"/>).</summary>
        public readonly List<PicketWarning> PicketWarnings = new List<PicketWarning>();

        /// <summary>
        /// The strength of the hostile groups <paramref name="f"/>'s pickets at <paramref name="gate"/> have warned of in the
        /// last <see cref="GatePickets.WarningDays"/> that are still there (U.SPC-T480): parked off the system root within
        /// <see cref="GateFightRangeKm"/> of the gate. Raiders staked out off it are left to <see cref="RaidersOffGate"/>.
        /// </summary>
        public double WarnedOffGate(JumpGate gate, Faction f, double jd)
        {
            int root = Catalog.Root.Index;
            double total = 0;
            var counted = new HashSet<Ship>();
            foreach (var w in PicketWarnings)
            {
                if (w.Gate != gate || w.Owner != f || jd - w.Jd > GatePickets.WarningDays || jd < w.Jd) continue;
                var s = w.Intruder?.Controller;
                if (s == null || s.Destroyed || !Fleet.IsPresent(s) || !counted.Add(s) || !Diplomacy.Hostile(f, s.Faction)) continue;
                if (!Fleet.IsAt(s, root) || s.Orbit == null || (s.Orbit.OffsetAt(jd) - gate.PositionKm).Length > GateFightRangeKm) continue;
                if (s.Faction.IsPirate && s.StakeoutKm is DVec3 spot && GateNear(spot) == gate) continue;
                total += GroupStrength(s);
            }
            return total;
        }

        /// <summary>
        /// The gate command sends navy group <paramref name="leader"/> to hold on a picket's warning (U.SPC-T480), or null:
        /// one of its navy's own gates here whose pickets have warned of hostiles still at it (<see cref="WarnedOffGate"/>),
        /// the gravest such threat the group, with the gate's forts beside it, is strong enough to meet, within a month's
        /// flight. Takes station on the gate's approach (<see cref="Ship.StakeoutKm"/>), keeping a station it already held there.
        /// </summary>
        JumpGate GateWarned(Ship leader, int here, double jd, DVec3? held)
        {
            var f = leader.Faction;
            if (world.Gates.Count == 0 || PicketWarnings.Count == 0) return null;
            double strength = GroupStrength(leader);
            JumpGate best = null;
            double bestThreat = 0;
            foreach (var g in world.Gates)
            {
                if (g.Owner != f) continue;
                double threat = WarnedOffGate(g, f, jd);
                if (threat <= 0) continue;
                double forts = 0;
                foreach (var fort in g.Forts) forts += fort.Strength;
                if (strength + forts < threat * 1.25) continue;
                if (threat > bestThreat)
                {
                    bestThreat = threat;
                    best = g;
                }
            }
            if (best == null) return null;
            int root = Catalog.Root.Index;
            if (held is DVec3 spot && GateNear(spot) == best)
            {
                leader.StakeoutKm = spot;
                leader.StakeoutUntilJd = Math.Max(leader.StakeoutUntilJd, jd + HoldGateDays);
                return best;
            }
            double days = here == root ? 0 : Fleet.EstimateTravelDays(leader, here, root, jd);
            if (days > 30) return null;
            StakeOut(leader, best, jd + days + HoldGateDays);
            return best;
        }

        /// <summary>
        /// A gate picket's orders (U.SPC-T480, see <see cref="GatePickets"/>): to a yard when it is badly hurt or has run from
        /// a fight, else out to its post off its gate and holding it. Never to fight: it is a lookout, not a warship, and
        /// holding a patrol order (not an objective) it opens no fight of its own (see <see cref="Commanded"/>).
        /// </summary>
        GroupOrder DecidePicket(Ship leader, int here, double jd, bool withdrawing)
        {
            if (withdrawing || GroupDamage(leader) > GatePickets.RepairDamage)
            {
                int yard = NearestYard(leader.Faction, here, jd);
                if (yard >= 0) return new GroupOrder { Kind = OrderKind.Repair, Body = yard };
            }
            return new GroupOrder { Kind = OrderKind.Patrol, Body = Catalog.Root.Index };
        }

        /// <summary>
        /// A corvette's orders (U.SPC-T476, see <see cref="Corvettes"/>): to a yard when it is hurt or out of missiles; to
        /// defend the port on its beat nearest to it that has enemies at it; to run down the pirates or smugglers its navy has
        /// seen at a port on its beat or anywhere round its home planet, the nearest first; otherwise on round its beat, back
        /// to its station between runs down the lanes. It takes no objective from its polity's command: it is stationed for
        /// good, and never leaves its home system.
        /// </summary>
        GroupOrder DecideCorvette(Ship leader, int here, double jd, bool withdrawing)
        {
            var f = leader.Faction;
            if (withdrawing || GroupDamage(leader) > Corvettes.RepairDamage || MissilesLow(leader) || !leader.Combat.Armed)
            {
                int yard = NearestYard(f, here, jd);
                if (yard >= 0) return new GroupOrder { Kind = OrderKind.Repair, Body = yard };
            }
            var beat = leader.Route ?? new[] { leader.CorvetteStation };
            int threatened = -1;
            double bestDays = double.MaxValue;
            foreach (int body in beat)
            {
                if (!EnemiesAt(body, f)) continue;
                double days = body == here ? 0 : Fleet.EstimateTravelDays(leader, here, body, jd);
                if (days < bestDays)
                {
                    bestDays = days;
                    threatened = body;
                }
            }
            if (threatened >= 0) return new GroupOrder { Kind = OrderKind.Defend, Body = threatened };
            // The pirates and smugglers working its ground, as its navy's picture has them: the nearest first.
            var at = world.Sensors?.HelioOf(leader, jd) ?? default;
            var ground = new List<int>(beat);
            foreach (var s in Economy.Settlements)
                if (!ground.Contains(s.Body) && Corvettes.Polices(world, leader, s.Body)) ground.Add(s.Body);
            int mark = -1;
            bestDays = double.MaxValue;
            foreach (int body in ground)
            {
                if (body == here || !PiratesSeenAt(body, f, at, jd)) continue;
                double days = Fleet.EstimateTravelDays(leader, here, body, jd);
                if (days < bestDays)
                {
                    bestDays = days;
                    mark = body;
                }
            }
            if (mark >= 0) return new GroupOrder { Kind = OrderKind.Hunt, Body = mark };
            return new GroupOrder { Kind = OrderKind.Patrol, Body = Corvettes.NextOnBeat(leader, here) };
        }

        /// <summary>When each navy here went short of its corvettes (U.SPC-T476, see <see cref="Corvettes.Upkeep"/>).</summary>
        public readonly Dictionary<Faction, double> CorvettesShortSince = new Dictionary<Faction, double>();

        /// <summary>The strength of the raider bands navy <paramref name="f"/> (looking from <paramref name="at"/>) knows to be
        /// lying in wait off <paramref name="gate"/>, or 0.</summary>
        double RaidersOffGate(JumpGate gate, Faction f, DVec3 at, double jd)
        {
            int root = Catalog.Root.Index;
            var sensors = world.Sensors;
            double total = 0;
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || s.IsEscort || s.Faction == null || !s.Faction.IsPirate || s.Runner || s.StakeoutKm == null) continue;
                if (!Diplomacy.Hostile(f, s.Faction) || GateNear(s.StakeoutKm.Value) != gate) continue;
                if (sensors != null && !(sensors.KnownAt(f, s, at, jd) is ContactSighting seen && seen.Body == root && seen.Level >= ContactLevel.Class)) continue;
                total += GroupStrength(s);
            }
            return total;
        }

        /// <summary>
        /// A patrol wing's orders (U.SPC-T371, see <see cref="PatrolWings"/>): to a yard when it is hurt or out of missiles;
        /// to defend the port on its beat nearest to it that has enemies at it; to hunt raiders seen working a port on its
        /// beat; otherwise on round its beat. Never to strike at an enemy port: that is the battle groups' business.
        /// </summary>
        GroupOrder DecideWing(Ship leader, int here, double jd, bool withdrawing)
        {
            var f = leader.Faction;
            // A supply convoy (U.SPC-T375) goes on round its run, to a yard when hurt: its stores, not the fighting, are its business.
            if (leader.Convoy)
            {
                if (withdrawing || GroupDamage(leader) > 0.22)
                {
                    int dock = NearestYard(f, here, jd);
                    if (dock >= 0) return new GroupOrder { Kind = OrderKind.Repair, Body = dock };
                }
                return new GroupOrder { Kind = OrderKind.Patrol, Body = PatrolWings.NextOnBeat(leader, here) };
            }
            if (withdrawing || GroupDamage(leader) > 0.22 || MissilesLow(leader) || !leader.Combat.Armed)
            {
                int yard = NearestYard(f, here, jd);
                if (yard >= 0) return new GroupOrder { Kind = OrderKind.Repair, Body = yard };
            }
            // A wing too can be given an objective by its polity's command, off its beat (U.SPC-T423).
            if (Directed(leader, here, jd) is GroupOrder directed) return directed;
            var beat = leader.Route ?? new int[0];
            int threatened = -1;
            double bestDays = double.MaxValue;
            foreach (int body in beat)
            {
                if (!EnemiesAt(body, f)) continue;
                double days = body == here ? 0 : Fleet.EstimateTravelDays(leader, here, body, jd);
                if (days < bestDays)
                {
                    bestDays = days;
                    threatened = body;
                }
            }
            if (threatened >= 0) return new GroupOrder { Kind = OrderKind.Defend, Body = threatened };
            var at = world.Sensors?.HelioOf(leader, jd) ?? default;
            foreach (int body in beat)
                if (body != here && PiratesSeenAt(body, f, at, jd))
                    return new GroupOrder { Kind = OrderKind.Hunt, Body = body };
            // A lane picket wing (U.SPC-T479) lies in wait now and then on a lane between the ports of its beat.
            if (LanePicket(leader, here, beat, true) is GroupOrder picket) return picket;
            return new GroupOrder { Kind = OrderKind.Patrol, Body = PatrolWings.NextOnBeat(leader, here) };
        }

        /// <summary>
        /// A navy group's lane picket (U.SPC-T479): with a field projector fitted to a hull of the group (see <see cref="LaneInterdictors"/>)
        /// and nothing more pressing, now and then (<see cref="LaneInterdictors.PicketChance"/>) it lies in wait part-way along
        /// the most troubled hyperlane out of its system, where raiders have struck lately, bound for one of
        /// <paramref name="ports"/> at its far end, to pull the raiders and enemies riding it out into a fight (see
        /// <see cref="Targets"/>). A wing (<paramref name="keepToPorts"/>) takes only a lane that ends at one of them; another
        /// group, failing that, makes for the far end itself. Null when it does not.
        /// </summary>
        GroupOrder LanePicket(Ship leader, int here, IEnumerable<int> ports, bool keepToPorts)
        {
            var lanes = Fleet.Lanes;
            if (lanes == null || !LaneInterdictors.Fielded(leader, Fleet.Ships)) return null;
            if (rng.NextDouble() >= LaneInterdictors.PicketChance.Value) return null;
            int system = lanes.SystemOf(here);
            Lane best = null;
            int bound = -1;
            double bestScore = 0;
            foreach (var lane in lanes.Lanes)
            {
                if (lane.A != system && lane.B != system) continue;
                int far = lane.Other(system), to = -1;
                if (ports != null)
                    foreach (int p in ports)
                        if (lanes.SystemOf(p) == far) { to = p; break; }
                if (to < 0)
                {
                    if (keepToPorts) continue;
                    to = far;
                }
                lanes.Danger.TryGetValue(lane, out double danger);
                double score = (danger + 0.25) * (0.7 + rng.NextDouble() * 0.6);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = lane;
                    bound = to;
                }
            }
            if (best == null) return null;
            leader.LurkLane = best;
            leader.LurkAt = 0.3 + rng.NextDouble() * 0.4;
            leader.LurkDays = 2 + rng.NextDouble() * 2;
            return new GroupOrder { Kind = OrderKind.Ambush, Body = bound };
        }

        /// <summary>
        /// Whether the group <paramref name="ship"/> flies with is commanded to fight at <paramref name="body"/>
        /// (U.SPC-T371): combat is ordered, not picked with whatever hostile happens to be in reach. A group may open a
        /// fight (or turn to run the player down) only where its orders put it: at its own ports, which it always defends;
        /// on its patrol beat; or at the objective of the order it holds (the port it attacks, defends, besieges, assaults
        /// or hunts raiders at, the hunting ground it raids, the gate it watches). A group holding no orders yet (and a
        /// bounty hunter, which follows its contract) keeps the old rule and fights wherever it meets a hostile; a patrol
        /// wing never does, keeping to its beat. Defending is never ordered away: a group set upon is still swept into the
        /// fight, and platforms still open on a hostile come into range.
        /// </summary>
        public bool Commanded(Ship ship, int body)
        {
            if (ship == null) return false;
            var leader = ship.Controller;
            if (Hunter(leader)) return true;
            // A band staked out off a gate is there to fall on what comes through (see GateAmbushes).
            if (leader.StakeoutKm != null && body == Catalog.Root.Index) return true;
            foreach (var st in Economy.At(body))
                if (st.Owner == leader.Faction && !st.Fallen) return true;
            bool ordered = Orders.TryGetValue(leader, out var o);
            PriorOrders.TryGetValue(leader, out var prior);
            // Never ordered anywhere yet: the old rule, for all but a wing.
            if (!ordered) return !leader.PatrolWing || PatrolWings.OnBeat(leader, body);
            if (Objective(o, body) || Objective(prior, body)) return true;
            // On its beat a group patrols, and fights for it, unless it was sent away on other business (a wing always).
            return PatrolWings.OnBeat(leader, body) && (leader.PatrolWing || o.Kind == OrderKind.Patrol || prior == null || prior.Kind == OrderKind.Patrol);
        }

        /// <summary>Whether <paramref name="o"/> sends its group to fight at <paramref name="body"/>: any order but a patrol
        /// or a run to the yard, aimed there.</summary>
        static bool Objective(GroupOrder o, int body) =>
            o != null && o.Body == body && o.Kind != OrderKind.Patrol && o.Kind != OrderKind.Repair;

        GroupOrder DecidePirate(Ship leader, int here, double jd, bool withdrawing)
        {
            // A fresh plan: an ambush planned earlier and not yet reached no longer stands.
            leader.LurkLane = null;
            var watch = leader.StakeoutKm;
            leader.StakeoutKm = null;
            var home = HomeHaven(leader);
            // A group whose haven has been taken or destroyed is out of the game: it lies low where it is,
            // no raids and no ambushes, until the haven is rebuilt.
            if (home != null && home.Fallen) return new GroupOrder { Kind = OrderKind.Patrol, Body = here };
            home ??= LiveHavenOf(leader.Faction) ?? FirstLiveHaven();
            int haven = home?.Body ?? -1;
            if (withdrawing || GroupDamage(leader) > 0.3 || MissilesLow(leader) || !leader.Combat.Armed)
                return new GroupOrder { Kind = OrderKind.Repair, Body = haven >= 0 ? haven : here };

            // Home is under attack: back to defend it.
            if (haven >= 0 && EnemiesAt(haven, leader.Faction) && (haven == here || Fleet.EstimateTravelDays(leader, here, haven, jd) < 30))
                return new GroupOrder { Kind = OrderKind.Defend, Body = haven };

            // A hold full of plunder goes home to the haven's stores first (U.SPC-T94).
            if (leader.PlunderTonnes > 0 && home != null && home.Owner == leader.Faction)
            {
                if (haven == here) UnloadPlunder(leader, here);
                else return new GroupOrder { Kind = OrderKind.Repair, Body = haven };
            }

            // Lying in wait off a jump gate (U.SPC-T94): hold there till the watch is up; or, now and then, go and lie off
            // the busiest gate, where traffic bunches up going in and coming out.
            int root = Catalog.Root.Index;
            if (watch is DVec3 spot && jd < leader.StakeoutUntilJd)
            {
                leader.StakeoutKm = spot;
                return new GroupOrder { Kind = OrderKind.GateWatch, Body = root };
            }
            if (here != root && world.Gates.Count > 0 && rng.NextDouble() < GateWatchChance && BusiestGate(leader.Faction, jd) is JumpGate gate)
            {
                double days = Fleet.EstimateTravelDays(leader, here, root, jd);
                if (days < 30)
                {
                    StakeOut(leader, gate, jd + days + 2 + rng.NextDouble() * 2);
                    return new GroupOrder { Kind = OrderKind.GateWatch, Body = root };
                }
            }

            // Go after a lane tender in reach (U.SPC-T481): worth a fat purse to whoever wants the lanes cut.
            if (world.LaneUpkeep != null && world.LaneUpkeep.RaidersHunt())
            {
                int tender = world.LaneUpkeep.HuntTarget(leader, here, jd);
                if (tender >= 0) return new GroupOrder { Kind = OrderKind.Raid, Body = tender };
            }

            // Lie in wait beside a busy hyperlane: only with an interdictor in the band to pull its marks out (U.SPC-T479).
            var lanes = Fleet.Lanes;
            if (lanes != null && LaneInterdictors.Fielded(leader, Fleet.Ships) && rng.NextDouble() < 0.55)
            {
                int system = lanes.SystemOf(here);
                var lane = BusiestLane(system);
                if (lane != null)
                {
                    if (lane.A == system || lane.B == system)
                    {
                        leader.LurkLane = lane;
                        leader.LurkAt = 0.3 + rng.NextDouble() * 0.4;
                        leader.LurkDays = 2.5 + rng.NextDouble() * 2;
                        return new GroupOrder { Kind = OrderKind.Ambush, Body = lane.Other(system) };
                    }
                    double toA = (world.Ephemeris.HelioAt(lane.A, jd) - world.Ephemeris.HelioAt(here, jd)).Length;
                    double toB = (world.Ephemeris.HelioAt(lane.B, jd) - world.Ephemeris.HelioAt(here, jd)).Length;
                    return new GroupOrder { Kind = OrderKind.Ambush, Body = toA < toB ? lane.A : lane.B };
                }
            }

            // Stay on a hunting ground a while if it has traffic and no warships.
            if (Orders.TryGetValue(leader, out var o) && o.Kind == OrderKind.Raid && o.Body == here && jd - o.SinceJd < 4 && DefenceAt(here, leader.Faction) < GroupStrength(leader))
                return new GroupOrder { Kind = OrderKind.Raid, Body = here };

            int target = -1;
            double best = 0;
            foreach (var s in Economy.Settlements)
            {
                if (s.Kind == SettlementKind.Haven || s.Body == here) continue;
                double traffic = 1;
                foreach (var t in Fleet.Ships)
                    if (!t.Destroyed && world.IsTrader(t) && (t.DestinationBody == s.Body || (t.Orbit != null && t.Orbit.Body == s.Body))) traffic++;
                double defence = DefenceAt(s.Body, leader.Faction);
                // Raiders take chances against a port's guns, but prefer the soft ones.
                if (defence > GroupStrength(leader) * 1.6) continue;
                double days = Fleet.EstimateTravelDays(leader, here, s.Body, jd);
                double value = traffic / (defence + GroupStrength(leader) * 0.3 + 100) / (days + 3) * (0.7 + rng.NextDouble() * 0.6);
                if (value > best)
                {
                    best = value;
                    target = s.Body;
                }
            }
            if (target < 0) target = haven >= 0 && haven != here ? haven : NextPatrol(leader, here);
            return new GroupOrder { Kind = OrderKind.Raid, Body = target };
        }

        /// <summary>Whether <paramref name="ship"/> flies in a bounty hunter group (see <see cref="Outlaws"/>).</summary>
        bool Hunter(Ship ship) => world.Outlaws != null && world.Outlaws.IsHunter(ship);

        /// <summary>The navy's oldest surviving group, which never leaves home.</summary>
        public bool IsHomeGuard(Ship leader)
        {
            // Each navy keeps its oldest surviving group at home: one it still commands (not the hull the player
            // is flying, nor a derelict), or every other group would be free to leave. A patrol wing is never it: it keeps
            // its beat whatever (U.SPC-T371).
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && !s.IsEscort && !s.Derelict && !s.PatrolWing && s != world.PilotedShip && s.Faction == leader.Faction && s.Spec.IsMilitary)
                    return s == leader;
            return false;
        }

        int NextPatrol(Ship leader, int here)
        {
            var own = new List<int>();
            foreach (var s in Economy.Settlements)
                if (s.Owner == leader.Faction && !own.Contains(s.Body)) own.Add(s.Body);
            if (own.Count == 0 && leader.Route != null) own.AddRange(leader.Route);
            own.Remove(here);
            if (own.Count == 0) return here;
            return own[rng.Next(own.Count)];
        }

        int NearestYard(Faction f, int here, double jd)
        {
            int best = -1;
            double bestDist = double.MaxValue;
            foreach (var s in Economy.Settlements)
            {
                if (s.Owner != f || (!s.HasShipyard && s.Kind != SettlementKind.Haven)) continue;
                double d = s.Body == here ? 0 : (world.Ephemeris.HelioAt(s.Body, jd) - world.Ephemeris.HelioAt(here, jd)).Length;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = s.Body;
                }
            }
            return best;
        }

        int HavenBody(Faction clan) => LiveHavenOf(clan)?.Body ?? -1;

        string HavenName(Faction clan = null) => (clan != null ? LiveHavenOf(clan) : null)?.Name ?? FirstLiveHaven()?.Name ?? "a pirate haven";

        /// <summary>The first haven still standing (not taken or destroyed), or null.</summary>
        Settlement FirstLiveHaven()
        {
            foreach (var s in Economy.Settlements)
                if (s.Kind == SettlementKind.Haven && !s.Fallen) return s;
            return null;
        }

        /// <summary>The haven a raider group sails from (the first stop on its route, if a haven is there), or null.</summary>
        public Settlement HomeHaven(Ship ship)
        {
            var route = ship?.Controller.Route;
            if (route == null || route.Length == 0) return null;
            foreach (var s in Economy.At(route[0]))
                if (s.Kind == SettlementKind.Haven) return s;
            return null;
        }

        // ---------------------------------------------------------------- hidden havens

        /// <summary>How far a ship's scanners pick out a hidden haven: its reactors, its traffic, its guns.</summary>
        public const double ScanRangeKm = 2e6;

        /// <summary>Chance a navy that wins a fight follows each beaten raider group home.</summary>
        public const double TrailChance = 0.3;

        /// <summary>Days an assaulting group must hold a haven whose garrison is broken before it is boarded or levelled.</summary>
        public const double HavenAssaultDays = 1;

        /// <summary>Days the pirates take to rebuild a haven that was taken or destroyed.</summary>
        public const double HavenRebuildDays = 180;

        /// <summary>
        /// <paramref name="by"/> finds a hidden haven (if it had not already): it is logged, goes on the
        /// map, and that faction's navy may now strike it. Returns whether this was news to it.
        /// </summary>
        public bool Discover(Settlement st, Faction by, double jd, string how)
        {
            if (st == null || !st.Hidden || by == null || by.IsPirate || st.KnownTo.Contains(by)) return false;
            bool first = st.KnownTo.Count == 0;
            st.KnownTo.Add(by);
            string text = first
                ? $"The {by.Name} find a hidden {st.Owner.Name} haven, {st.Name}, on {Catalog[st.Body].Name}: {how}"
                : $"The {by.Name} learn where {st.Name} ({Catalog[st.Body].Name}) lies: {how}";
            world.Log(jd, text, WorldEventKind.Info, st.Body);
            return true;
        }

        /// <summary>A ship of <paramref name="by"/> sweeps its scanners from a heliocentric position: every hidden
        /// haven within <paramref name="rangeKm"/> (<see cref="ScanRangeKm"/>, more with scanner suites fitted) is found. Returns the havens found this time.</summary>
        public List<Settlement> ScanFrom(Faction by, DVec3 helioKm, double jd, string who = null, double rangeKm = ScanRangeKm)
        {
            var found = new List<Settlement>();
            if (by == null || by.IsPirate) return found;
            foreach (var st in Economy.Settlements)
            {
                if (!st.Hidden || st.KnownTo.Contains(by)) continue;
                if ((world.Ephemeris.HelioAt(st.Body, jd) - helioKm).Length > rangeKm) continue;
                if (Discover(st, by, jd, (who != null ? "the " + who + "'s" : "a ship's") + " scanners pick it out")) found.Add(st);
            }
            return found;
        }

        /// <summary>A raider beaten by <paramref name="tracker"/> is followed back to its haven, if that haven is hidden.</summary>
        public bool TrailHome(Ship raider, Faction tracker, double jd)
        {
            var home = HomeHaven(raider);
            if (home == null || tracker == null || tracker.IsPirate) return false;
            return Discover(home, tracker, jd, $"{tracker.Name} ships follow the beaten {raider.Name} home");
        }

        /// <summary>Every navy's warships sweep their scanners once a day.</summary>
        void Scans(double jd)
        {
            bool anyHidden = false;
            foreach (var st in Economy.Settlements) anyHidden |= st.Hidden;
            if (!anyHidden) return;
            // A group sweeps as far as the best scanners in it reach.
            var reach = new Dictionary<Ship, double>();
            foreach (var e in Fleet.Ships)
                if (e.IsEscort && !e.Destroyed && e.Loadout != null)
                {
                    reach.TryGetValue(e.Leader, out double r);
                    reach[e.Leader] = Math.Max(r, Outfitting.ScanRangeKm(e));
                }
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || s.IsEscort || s.Derelict || s == world.PilotedShip || !s.Spec.IsMilitary || s.Faction.IsPirate) continue;
                reach.TryGetValue(s, out double escorts);
                ScanFrom(s.Faction, Fleet.StateOf(s, jd).HelioKm, jd, s.Name, Math.Max(escorts, Outfitting.ScanRangeKm(s)));
            }
        }

        /// <summary>A haven <paramref name="f"/>'s navy would strike on its own: a hidden one it has found, still
        /// standing. (Blackwater, the haven everyone knows of over Deimos, is an open secret the navies leave
        /// be; the player can still go in.)</summary>
        bool Assailable(Settlement s, Faction f) =>
            s.Kind == SettlementKind.Haven && !s.Fallen && s.Hidden && s.KnownTo.Contains(f) && Diplomacy.Hostile(f, s.Owner);

        /// <summary>The nearest found haven the group is strong enough to storm (half again its garrison and any raiders there), or null.</summary>
        Settlement AssaultTarget(Ship leader, int here, double jd)
        {
            var f = leader.Faction;
            double strength = GroupStrength(leader);
            Settlement best = null;
            double bestDays = 40;
            foreach (var s in Economy.Settlements)
            {
                if (!Assailable(s, f) || strength < DefenceAt(s.Body, f) * 1.5) continue;
                double days = s.Body == here ? 0 : Fleet.EstimateTravelDays(leader, here, s.Body, jd);
                if (days < bestDays)
                {
                    bestDays = days;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>Whether the ship's group carries marines to board with.</summary>
        public bool HasMarines(Ship ship)
        {
            var leader = ship.Controller;
            if (!leader.Destroyed && leader.Combat.MarinesAvailable > 0) return true;
            foreach (var e in Fleet.Ships)
                if (e.Leader == leader && !e.Destroyed && e.Combat.MarinesAvailable > 0) return true;
            return false;
        }

        /// <summary>
        /// A haven is open to be boarded or destroyed by <paramref name="ship"/>: still standing, hostile,
        /// its garrison platforms all gone, no fight going on there and no raiders parked over it.
        /// </summary>
        public bool CanAssault(Settlement st, Ship ship) =>
            st != null && ship != null && !ship.Destroyed && st.Kind == SettlementKind.Haven && !st.Fallen && st.Platforms.Count == 0 &&
            BattleAt(st.Body) == null && !PiratesAt(st.Body) && Diplomacy.Hostile(ship.Faction, st.Owner);

        /// <summary>
        /// The player's (or anyone's) assault on a haven whose defences are broken: board and take it
        /// (needs marines in the group; the stolen goods are seized) or destroy it (needs guns or
        /// marines). Either way it is out of action, and its raiders lie low, until the pirates rebuild
        /// it. Returns false if it can't be done.
        /// </summary>
        public bool AssaultHaven(Settlement st, Ship ship, bool capture, double jd)
        {
            if (!CanAssault(st, ship)) return false;
            if (capture ? !HasMarines(ship) : !ship.Controller.Combat.Armed && !HasMarines(ship)) return false;
            FallHaven(st, ship.Faction, capture, jd);
            return true;
        }

        void FallHaven(Settlement st, Faction by, bool capture, double jd)
        {
            double value = 0;
            for (int i = 0; i < GoodInfo.Count; i++)
            {
                value += Math.Max(0, st.Stock[i]) * GoodInfo.Of((Good)i).BasePrice;
                st.Stock[i] = 0;
            }
            if (capture) by.Treasury += value * 0.6;
            st.Platforms.Clear();
            st.Population = 0;
            st.Fallen = true;
            st.RebuildJd = jd + HavenRebuildDays;
            if (st.Hidden) st.KnownTo.Add(by);
            assaultDays.Remove(st);
            string where = $"{st.Name} ({Catalog[st.Body].Name})";
            world.Log(jd, capture
                ? $"The {by.Name} board and take {where}, seizing {value / 1e6:N0} M of stolen goods; its raiders scatter"
                : $"The {by.Name} destroy {where}; its raiders scatter", WorldEventKind.Capture, st.Body);
            BumpDanger(st.Body, 1);
        }

        /// <summary>
        /// Navies storm the havens they have found: once the garrison's platforms are gone and no raiders
        /// hold the rock, an assaulting group boards it (if it has marines) or levels it after
        /// <see cref="HavenAssaultDays"/>. Fallen havens rise again after <see cref="HavenRebuildDays"/>.
        /// </summary>
        void HavenAssaults(double jd)
        {
            foreach (var st in Economy.Settlements)
            {
                if (st.Kind != SettlementKind.Haven) continue;
                if (st.Fallen)
                {
                    if (jd >= st.RebuildJd) RebuildHaven(st, jd);
                    continue;
                }
                Ship assailant = null;
                if (st.Platforms.Count == 0 && BattleAt(st.Body) == null && !PiratesAt(st.Body))
                    foreach (var s in Fleet.Ships)
                    {
                        if (s.Destroyed || s.IsEscort || !s.Spec.IsMilitary || s.Faction.IsPirate || s == world.PilotedShip) continue;
                        var c = s.Controller;
                        if (c.Phase != ShipPhase.Parked || c.Orbit.Body != st.Body || !Diplomacy.Hostile(s.Faction, st.Owner)) continue;
                        if (!Orders.TryGetValue(s, out var o) || o.Kind != OrderKind.Assault) continue;
                        if (assailant == null || GroupStrength(s) > GroupStrength(assailant)) assailant = s;
                    }
                if (assailant == null)
                {
                    assaultDays.Remove(st);
                    continue;
                }
                assaultDays.TryGetValue(st, out double days);
                days += 1;
                assaultDays[st] = days;
                if (days >= HavenAssaultDays) FallHaven(st, assailant.Faction, HasMarines(assailant), jd);
            }
        }

        void RebuildHaven(Settlement st, double jd)
        {
            st.Fallen = false;
            st.Population = st.InitialPopulation;
            while (st.Platforms.Count < st.MaxPlatforms) st.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            world.Log(jd, $"The {st.Owner.Name} have rebuilt {st.Name} ({Catalog[st.Body].Name})", WorldEventKind.Info, st.Body);
        }

        /// <summary>Days an assaulting group has held a broken haven (see <see cref="HavenAssaultDays"/>).</summary>
        public double AssaultDays(Settlement s) => assaultDays.TryGetValue(s, out var d) ? d : 0;

        public bool AtWar(Faction f)
        {
            foreach (var g in Fleet.Factions)
                if (g != f && !g.IsPirate && Diplomacy.Get(f, g) == Relation.War) return true;
            return false;
        }

        public bool EnemiesAt(int body, Faction f)
        {
            if (BattleAt(body) is Battle b && (b.SideLeaders[0] == f || b.SideLeaders[1] == f)) return true;
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || s.IsEscort || !(s.Spec.IsMilitary || s.Faction.IsPirate)) continue;
                if (!Diplomacy.Hostile(f, s.Faction)) continue;
                var c = s.Controller;
                if (c.Phase == ShipPhase.Parked && c.Orbit.Body == body) return true;
            }
            return false;
        }

        /// <summary>Warships of a navy at war with <paramref name="f"/> (not raiders) parked at <paramref name="body"/>.</summary>
        bool EnemyNavyAt(int body, Faction f)
        {
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || s.IsEscort || !s.Spec.IsMilitary || s.Faction.IsPirate || !Diplomacy.Hostile(f, s.Faction)) continue;
                var c = s.Controller;
                if (c.Phase == ShipPhase.Parked && c.Orbit != null && c.Orbit.Body == body) return true;
            }
            return false;
        }

        bool PiratesAt(int body)
        {
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && !s.IsEscort && s.Faction.IsPirate && !s.Runner && s.Phase == ShipPhase.Parked && s.Orbit.Body == body) return true;
            return false;
        }

        /// <summary>
        /// Raiders <paramref name="f"/>'s picture holds parked at <paramref name="body"/> (by class at least: a warship
        /// with no transponder over its port), as the word has reached a group at <paramref name="where"/> (U.SPC-T100):
        /// a sighting crosses the system at light speed, so a group far off acts on a look minutes or hours old, sails
        /// for raiders that may have gone, and does not yet know of ones that have just come. What a navy can act on,
        /// rather than what is there.
        /// </summary>
        bool PiratesSeenAt(int body, Faction f, DVec3 where, double jd)
        {
            var sensors = world.Sensors;
            if (sensors == null)
            {
                foreach (var s in Fleet.Ships)
                    if (!s.Destroyed && !s.IsEscort && s.Faction.IsPirate && s.Phase == ShipPhase.Parked && s.Orbit.Body == body) return true;
                return false;
            }
            foreach (var c in sensors.PictureOf(f).Contacts)
            {
                var s = c.Ship;
                if (s.Destroyed || s.IsEscort || s.Faction == null || !s.Faction.IsPirate) continue;
                if (sensors.KnownAt(f, s, where, jd) is ContactSighting seen && seen.Body == body && seen.Level >= ContactLevel.Class) return true;
            }
            return false;
        }

        /// <summary>Whether a navy group at <paramref name="where"/> has word of raiders working its own port at
        /// <paramref name="body"/> (see <see cref="PiratesSeenAt"/>).</summary>
        public bool KnowsOfRaidersAt(int body, Faction f, DVec3 where, double jd) => PiratesSeenAt(body, f, where, jd);

        /// <summary>Strength defending a body against <paramref name="attacker"/>: hostile platforms and parked warships.</summary>
        public double DefenceAt(int body, Faction attacker)
        {
            double total = 0;
            foreach (var st in Economy.At(body))
                if (Diplomacy.Hostile(attacker, st.Owner))
                    foreach (var p in st.Platforms) total += p.Strength;
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || !(s.Spec.IsMilitary || s.Faction.IsPirate) || !Diplomacy.Hostile(attacker, s.Faction)) continue;
                var c = s.Controller;
                if (c.Phase == ShipPhase.Parked && c.Orbit.Body == body) total += s.Combat.Strength;
            }
            return total;
        }

        public double GroupStrength(Ship leader)
        {
            double total = leader.Combat.Strength;
            foreach (var s in Fleet.Ships)
                if (s.Leader == leader && !s.Destroyed) total += s.Combat.Strength;
            return total;
        }

        double GroupDamage(Ship leader)
        {
            double sum = leader.Combat.HullDamage;
            int n = 1;
            foreach (var s in Fleet.Ships)
                if (s.Leader == leader && !s.Destroyed)
                {
                    sum += s.Combat.HullDamage;
                    n++;
                }
            return sum / n;
        }

        /// <summary>A carrier that has lost half its fighters goes back to base to replace them.</summary>
        bool HangarsLow(Ship leader)
        {
            int have = 0, full = 0;
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || (s != leader && s.Leader != leader)) continue;
                have += s.Combat.Fighters;
                full += s.Combat.Spec.HangarCraft;
            }
            return full > 0 && have < full * 0.5;
        }

        /// <summary>A group that has spent most of its marines on boarding goes back for more.</summary>
        bool MarinesLow(Ship leader)
        {
            double have = 0, full = 0;
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || (s != leader && s.Leader != leader)) continue;
                have += s.Combat.Interior.Marines;
                full += s.Combat.Spec.Marines;
            }
            return full > 0 && have < full * 0.35;
        }

        bool MissilesLow(Ship leader)
        {
            int have = 0, full = 0;
            foreach (var sub in leader.Combat.Subsystems)
                if (sub.Def.Weapon != null && sub.Def.Weapon.Missile && !sub.Destroyed)
                {
                    have += sub.Ammo;
                    full += sub.Def.Weapon.Ammo;
                }
            return full > 0 && have < full * 0.3;
        }

        // ---------------------------------------------------------------- daily

        void Daily(double jd)
        {
            foreach (var key in new List<int>(danger.Keys)) danger[key] *= Math.Exp(-1.0 / 20);
            foreach (var s in Economy.Settlements) Array.Clear(s.ExtraDemand, 0, s.ExtraDemand.Length);
            PruneTracking(jd);

            foreach (var s in Fleet.Ships)
                if (s.Derelict && !s.Destroyed && jd - s.DerelictSinceJd > 25) s.Destroyed = true;
            Wrecks.AdvanceTo(jd);
            Crews.Daily(Diplomacy, jd);
            // A polity recruits once a day from every settlement it holds in any system, by the world that keeps its books.
            var polities = world.Polities;
            Crews.Recruit(polities.KeptBy(world, Fleet.Factions), polities.Worlds.Count > 1 ? polities.Settlements : Economy.Settlements, 1);
            if (Fleet.Lanes != null)
                foreach (var lane in new List<Lane>(Fleet.Lanes.Danger.Keys)) Fleet.Lanes.SetDanger(lane, Fleet.Lanes.Danger[lane] * 0.85);
            MergeGroups(jd);
            PayUpkeep();
            DrillMarines();
            Refit(jd);
            Sieges(jd);
            Scans(jd);
            HavenAssaults(jd);
            RebuildPlatforms(jd);
            // The navies' corvettes: reposted when their ports change hands, and replaced when lost (U.SPC-T476).
            Corvettes.Upkeep(world, CorvettesShortSince, jd);
            PirateRecruiting(jd);
            DiplomacyCycle(jd);

            // Groups that met at a body without a fight (a war resumed, say) fight now, if they are fit to.
            foreach (var s in Fleet.Ships)
            {
                // Not the player's ship: its fights start only where the player engages (TryStartPlayerBattle).
                if (s.Destroyed || s.IsEscort || s.Battle != null || s.Phase != ShipPhase.Parked || s == world.PilotedShip) continue;
                if (!(s.Spec.IsMilitary || s.Faction.IsPirate) || s.Runner || !Willing(s, jd)) continue;
                int body = s.Orbit.Body;
                if (lastBattleEndJd.TryGetValue(body, out double ended) && jd - ended < 0.5) continue;
                if (BattleAt(body) == null) TryStartBattle(body, s.Faction, jd, body == Catalog.Root.Index ? s.Orbit.OffsetAt(jd) : (DVec3?)null);
            }
        }

        /// <summary>
        /// Navies concentrate: groups of one faction parked at the same body (and not fighting) merge
        /// into the strongest of them, up to eight hulls, so single new warships become battle groups
        /// instead of patrolling alone. The home guard is never absorbed into or swollen. At the system
        /// root, "the same body" covers every jump gate's approach, so two groups must also be within
        /// <see cref="GateFightRangeKm"/> of each other there: a raider band parked off one gate is left
        /// alone by one lurking at another, AU away (U.SPC-T307).
        /// </summary>
        void MergeGroups(double jd)
        {
            int root = Catalog.Root.Index;
            var groups = new List<Ship>();
            foreach (var s in Fleet.Ships)
                // The player's ship is flown by the player: never handed to an AI flagship, nor one itself.
                // Nor a group on an interstellar voyage: it keeps to its own business.
                // Nor bounty hunters: each works its own contract.
                // Nor a pirate clan's smugglers: they carry loot, not guns, and never raid (see Ship.Runner).
                // Nor a patrol wing: it keeps to its own beat (U.SPC-T371).
                if (!s.Destroyed && !s.IsEscort && s.Battle == null && s != world.PilotedShip && (s.Spec.IsMilitary || s.Faction.IsPirate) && !s.Runner && !s.PatrolWing && s.Phase == ShipPhase.Parked && s.Voyage == null && !Hunter(s))
                    groups.Add(s);
            groups.Sort((a, b) => GroupStrength(b).CompareTo(GroupStrength(a)));
            for (int i = 0; i < groups.Count; i++)
            {
                var big = groups[i];
                if (big.Destroyed || big.IsEscort || IsHomeGuard(big)) continue;
                for (int j = i + 1; j < groups.Count; j++)
                {
                    var small = groups[j];
                    if (small.IsEscort || small.Faction != big.Faction || small.Orbit.Body != big.Orbit.Body || IsHomeGuard(small)) continue;
                    if (big.Orbit.Body == root && (big.Orbit.OffsetAt(jd) - small.Orbit.OffsetAt(jd)).Length > GateFightRangeKm) continue;
                    int size = 1, joining = 1;
                    foreach (var e in Fleet.Ships)
                    {
                        if (e.Destroyed) continue;
                        if (e.Leader == big) size++;
                        if (e.Leader == small) joining++;
                    }
                    if (size + joining > 8) continue;
                    var members = new List<Ship> { small };
                    foreach (var e in Fleet.Ships)
                        if (e.Leader == small && !e.Destroyed) members.Add(e);
                    // The joining band's own plunder comes along into the flagship it now follows (U.SPC-T308).
                    MovePlunder(small, big);
                    foreach (var m in members)
                    {
                        Fleet.AddEscort(big, m, Fleet.SlotFor(FreeSlot(big)));
                        size++;
                    }
                    Orders.Remove(small);
                    PriorOrders.Remove(small);
                    retreating.Remove(small);
                }
            }
        }

        /// <summary>The first formation slot on <paramref name="leader"/> no living escort holds: a ship merged in
        /// never sits on top of one already there (escorts lost or cut loose leave gaps among the slots).</summary>
        int FreeSlot(Ship leader)
        {
            for (int k = 0; ; k++)
            {
                var at = Fleet.SlotFor(k);
                bool taken = false;
                foreach (var e in Fleet.Ships)
                    if (!e.Destroyed && e.Leader == leader && (e.FormationSlot - at).LengthSquared < 1e-9)
                    {
                        taken = true;
                        break;
                    }
                if (!taken) return k;
            }
        }

        /// <summary>
        /// A day's running costs out of each navy's treasury: crews, fuel and maintenance for every
        /// warship it has in service and every defence platform over its ports
        /// (<see cref="Economy.UpkeepPerYear"/> of the hull's price a year). Pirates live off plunder.
        /// </summary>
        void PayUpkeep()
        {
            const double perDay = Economy.UpkeepPerYear / 365.25;
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || s.Derelict || !s.Spec.IsMilitary || s.Faction.IsPirate) continue;
                s.Faction.Treasury -= Economy.HullPrice(s.Spec.Class) * perDay;
            }
            foreach (var st in Economy.Settlements)
                if (!st.Owner.IsPirate && st.Platforms.Count > 0) st.Owner.Treasury -= st.Platforms.Count * PlatformPrice * perDay;
        }

        /// <summary>Credits a port's owner pays to raise one defence platform (besides its steel and electronics).</summary>
        public const double PlatformPrice = 3e7;

        /// <summary>A day's drill for the marines of every hull out of battle that has a marine habitat (U.SPC-T465).</summary>
        void DrillMarines()
        {
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || s.Derelict || s.Battle != null || s.Combat == null) continue;
                s.Combat.Interior.Drill(1);
            }
        }

        /// <summary>
        /// Warships at a friendly yard repair and rearm, buying alloys, electronics and munitions from its
        /// market. A navy whose treasury is empty cannot pay the yard: its ships stay as they are.
        /// </summary>
        void Refit(double jd)
        {
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || s.Battle != null || !(s.Spec.IsMilitary || s.Faction.IsPirate)) continue;
                var c = s.Controller;
                if (c.Phase != ShipPhase.Parked) continue;
                Settlement yard = null, port = null;
                foreach (var st in Economy.At(c.Orbit.Body))
                {
                    if (st.Owner != s.Faction || st.Fallen) continue;
                    port = st;
                    if (st.HasShipyard || st.Kind == SettlementKind.Haven) yard = st;
                }
                // Strays that set down here with no hangar to take them rejoin their carrier now it has called (U.SPC-T591).
                foreach (var st in Economy.At(c.Orbit.Body))
                    if (st.Strays.Count > 0 && !st.Fallen && s.Combat.Hangar != null) CarrierWings.Rejoin(st, s);
                // Prisoners in the brig go ashore at any port of the owner's, for their bounty or ransom (U.SPC-T65).
                if (port != null && s.Prisoners > 0 && s != world.PilotedShip) Outfitting.HandOverPrisoners(Economy, s, port);
                // In a port of its own a hull is in supply again: tanks, magazines and stores (U.SPC-T93).
                if (port != null && s.Voyage?.Campaign == null) s.Combat.Readiness = 1;
                if (s.Faction.Treasury <= 0) continue;
                if (yard == null) continue;

                double scale = s.LengthKm;
                double damage = s.Combat.Damage;
                if (damage > 0.001)
                {
                    double share = Math.Min(0.15, damage);
                    double alloys = 900 * scale * share / 0.15, electronics = 60 * scale * share / 0.15;
                    double have = Math.Min(1, Math.Min(yard.Stock[(int)Good.Alloys] / Math.Max(alloys, 1e-9), yard.Stock[(int)Good.Electronics] / Math.Max(electronics, 1e-9)));
                    yard.ExtraDemand[(int)Good.Alloys] += alloys;
                    yard.ExtraDemand[(int)Good.Electronics] += electronics;
                    if (yard.Kind == SettlementKind.Haven) have = 0.5;
                    yard.Stock[(int)Good.Alloys] -= Math.Min(yard.Stock[(int)Good.Alloys], alloys * have);
                    yard.Stock[(int)Good.Electronics] -= Math.Min(yard.Stock[(int)Good.Electronics], electronics * have);
                    s.Faction.Treasury -= (alloys * yard.Price[(int)Good.Alloys] + electronics * yard.Price[(int)Good.Electronics]) * have;
                    if (have > 0) s.Combat.Repair(share * have);
                }
                int missing = 0;
                foreach (var sub in s.Combat.Subsystems)
                    if (sub.Def.Weapon != null && sub.Def.Weapon.Missile) missing += sub.Def.Weapon.Ammo - sub.Ammo;
                if (missing > 0)
                {
                    double tonnes = missing * 1.5;
                    yard.ExtraDemand[(int)Good.Munitions] += tonnes;
                    if (yard.Stock[(int)Good.Munitions] >= tonnes || yard.Kind == SettlementKind.Haven)
                    {
                        yard.Stock[(int)Good.Munitions] = Math.Max(0, yard.Stock[(int)Good.Munitions] - tonnes);
                        s.Faction.Treasury -= tonnes * yard.Price[(int)Good.Munitions];
                        foreach (var sub in s.Combat.Subsystems)
                            if (sub.Def.Weapon != null && sub.Def.Weapon.Missile) sub.Ammo = sub.Def.Weapon.Ammo;
                    }
                }
                // Marines lost boarding (and crew lost to breaches) are replaced from the faction's trained
                // reserve, as far as it goes; the wounded go to the yard's hospitals.
                var inside = s.Combat.Interior;
                double marinesShort = s.Combat.Spec.Marines - inside.Marines;
                double crewShort = Math.Max(0, s.Combat.Spec.Crew - inside.Crew);
                if ((marinesShort > s.Combat.Spec.Marines * 0.1 || crewShort > s.Combat.Spec.Crew * 0.1) && !inside.UnderBoarding)
                {
                    double marines = Crews.Draft(s.Faction, Math.Max(0, marinesShort));
                    double crew = Crews.Draft(s.Faction, crewShort);
                    double medical = marines * 0.05;
                    yard.ExtraDemand[(int)Good.MedicalSupplies] += medical;
                    yard.Stock[(int)Good.MedicalSupplies] = Math.Max(0, yard.Stock[(int)Good.MedicalSupplies] - medical);
                    s.Faction.Treasury -= marines * 40000;
                    if (marines + crew > 0) inside.Muster(inside.Crew + crew, inside.Marines + marines);
                }
                int fighters = s.Combat.Spec.HangarCraft - s.Combat.Fighters;
                if (fighters > 0 && yard.Stock[(int)Good.Electronics] > 30)
                {
                    int add = Math.Min(fighters, 4);
                    yard.Stock[(int)Good.Electronics] -= 6 * add;
                    yard.Stock[(int)Good.Alloys] = Math.Max(0, yard.Stock[(int)Good.Alloys] - 40 * add);
                    s.Faction.Treasury -= add * 2.5e6;
                    s.Combat.RestoreFighters(add);
                }
                // The craft that came back hurt are mended whole in the yard's shops (U.SPC-T526): each is its own hull.
                int hurt = CarrierWings.Damaged(s.Combat);
                if (hurt > 0 && yard.Stock[(int)Good.Alloys] > 10 * hurt)
                {
                    yard.Stock[(int)Good.Alloys] -= 10 * hurt;
                    yard.Stock[(int)Good.Electronics] = Math.Max(0, yard.Stock[(int)Good.Electronics] - 2 * hurt);
                    s.Faction.Treasury -= hurt * 4e5;
                    CarrierWings.MendAboard(s.Combat);
                }
                // Boarding shuttles lost are built anew (U.SPC-T486).
                int shuttles = s.Combat.Spec.Shuttles - s.Combat.Shuttles;
                if (shuttles > 0 && yard.Stock[(int)Good.Electronics] > 30)
                {
                    yard.Stock[(int)Good.Electronics] -= 4 * shuttles;
                    yard.Stock[(int)Good.Alloys] = Math.Max(0, yard.Stock[(int)Good.Alloys] - 60 * shuttles);
                    s.Faction.Treasury -= shuttles * 3e6;
                    s.Combat.RestoreShuttles(shuttles);
                }
                // A sound, rearmed hull of a flush owner takes an upgrade now and then (U.SPC-T65); the player fits their own.
                if (s != world.PilotedShip && s.Combat.Damage <= 0.001 && missing == 0 && rng.NextDouble() < AiOutfitChance)
                    Outfitting.AiRefit(Economy, s, yard, world.Polities?.Command?.BuildPlanOf(s.Faction));
            }
        }

        /// <summary>Chance a day a warship idle at its own yard looks at an upgrade.</summary>
        public const double AiOutfitChance = 0.05;

        /// <summary>Days a captured port is garrisoned and cannot be taken again.</summary>
        public const double CaptureGraceDays = 90;

        /// <summary>Days of uncontested siege a port holds out: four, plus one per 200,000 people, up to twenty.</summary>
        public static double SiegeNeeded(Settlement s) => Math.Min(20, 4 + s.Population / 2e5);

        /// <summary>
        /// A port with no platforms left, blockaded by a besieging group with nobody defending it,
        /// changes hands after <see cref="SiegeNeeded"/> days. A fresh capture is garrisoned for
        /// <see cref="CaptureGraceDays"/>.
        /// </summary>
        void Sieges(double jd)
        {
            foreach (var st in Economy.Settlements)
            {
                if (st.Kind == SettlementKind.Haven) continue;
                Faction besieger = null, blockader = null;
                bool defended = st.Platforms.Count > 0 || BattleAt(st.Body) != null || st.DaysSinceCapture < CaptureGraceDays, guarded = false;
                foreach (var s in Fleet.Ships)
                {
                    if (s.Destroyed || s.IsEscort || !s.Spec.IsMilitary || s.Faction.IsPirate) continue;
                    var c = s.Controller;
                    if (c.Phase != ShipPhase.Parked || c.Orbit.Body != st.Body) continue;
                    if (Diplomacy.Hostile(s.Faction, st.Owner) && Orders.TryGetValue(s, out var o) && o.Kind == OrderKind.Siege) besieger = s.Faction;
                    // A group sent to blockade it holds the space over it as a siege does, but lands nobody (U.SPC-T423).
                    else if (Diplomacy.Hostile(s.Faction, st.Owner) && Orders.TryGetValue(s, out var b) && b.Kind == OrderKind.Blockade) blockader = s.Faction;
                    else if (s.Faction == st.Owner) defended = guarded = true;
                }
                if ((besieger ?? blockader) is Faction holder && !guarded) besiegers[st] = holder;
                else besiegers.Remove(st);
                if (besieger == null || defended)
                {
                    siegeDays.Remove(st);
                    continue;
                }
                siegeDays.TryGetValue(st, out double days);
                days += 1;
                siegeDays[st] = days;
                if (days >= SiegeNeeded(st))
                {
                    var old = st.Owner;
                    Economy.Capture(st, besieger);
                    // Taken back by the power it was occupied from: liberated, no longer occupied (U.SPC-T297).
                    // Taken from anyone else: occupied from them, the same bookkeeping a campaign's marines leave
                    // (see InterstellarWars.Occupy) -- without it the war's peace never counts this port's system
                    // as ceded, and its people never rise to retake it (U.SPC-T362).
                    if (st.OccupiedFrom == besieger) st.OccupiedFrom = null;
                    else st.OccupiedFrom ??= old;
                    siegeDays.Remove(st);
                    besiegers.Remove(st);
                    RevokeBookings();
                    world.Log(jd, $"{st.Name} ({Catalog[st.Body].Name}) falls to the {besieger.Name}, taken from the {old.Name}", WorldEventKind.Capture, st.Body);
                    BumpDanger(st.Body, 1);
                }
            }
        }

        /// <summary>Traders bound for a port that is now closed to them (it changed hands, or a war resumed) look elsewhere.</summary>
        public void RevokeBookings()
        {
            foreach (var t in Fleet.Ships)
            {
                if (t.Destroyed || t.CargoDestination == null || Economy.CanTrade(t, t.CargoDestination)) continue;
                Economy.Release(t);
                t.CargoDestination = null;
                t.Cargo = Economy.CargoText(t);
            }
        }

        public double SiegeDays(Settlement s) => siegeDays.TryGetValue(s, out var d) ? d : 0;

        /// <summary>The faction whose besieging group holds the space over <paramref name="s"/> with none of the owner's
        /// warships there to contest it (it may still be fighting the platforms), or null. Such a group blockades the port.</summary>
        public Faction Besieger(Settlement s) => besiegers.TryGetValue(s, out var f) ? f : null;

        void RebuildPlatforms(double jd)
        {
            foreach (var st in Economy.Settlements)
            {
                if (st.Fallen) continue;
                // Not mid-fight: a daily refit would refill shields and ammunition (and end a boarding) in battle.
                if (BattleAt(st.Body) != null) continue;
                // Nor with an enemy navy holding the space overhead (U.SPC-T92): nothing goes up past its guns.
                if (EnemyNavyAt(st.Body, st.Owner)) continue;
                foreach (var p in st.Platforms)
                    if (p.Damage > 0) { p.Repair(0.05); p.FreeResupply(); }
                if (st.Platforms.Count >= st.MaxPlatforms) continue;
                // A new platform every couple of weeks while the port can afford the steel.
                if (rng.NextDouble() > 0.07) continue;
                const double alloys = 3000, electronics = 200;
                st.ExtraDemand[(int)Good.Alloys] += alloys / 14;
                st.ExtraDemand[(int)Good.Electronics] += electronics / 14;
                if (st.Stock[(int)Good.Alloys] < alloys || st.Stock[(int)Good.Electronics] < electronics) continue;
                // ... and the owner can pay for it: a broke owner leaves its ports bare.
                if (st.Owner.Treasury < PlatformPrice) continue;
                st.Stock[(int)Good.Alloys] -= alloys;
                st.Stock[(int)Good.Electronics] -= electronics;
                st.Owner.Treasury -= PlatformPrice;
                st.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            }
        }

        static readonly string[] RaiderNames =
        {
            "Gallows Bird", "Red Meridian", "Widow's Due", "Cutthroat Margin", "No Quarter", "Bad Debt", "Scavenger", "Hollow Crown",
            "Ill Wind", "Knife Edge", "Grudge", "Salvage Rights", "Last Laugh", "Black Ledger",
        };

        int raidersRaised;

        public void RaisePirateGroup(double jd, int body)
        {
            var pirates = world.Pirates;
            var names = world.RaiderNames ?? RaiderNames;
            string Name()
            {
                int n = raidersRaised++;
                return names[n % names.Length] + (n >= names.Length ? " " + (n / names.Length + 1) : "");
            }
            var leader = Fleet.AddShip(Name(), raidersRaised % 3 == 0 ? ShipClass.Cruiser : ShipClass.Destroyer, pirates, ShipRole.Patrol);
            // Raiders fit phaser arrays as their taste and their plunder allow, from their haven's slips (U.SPC-T505).
            var slips = LiveHavenOf(pirates);
            FleetPatterns.Commission(leader, slips, null);
            leader.Route = new[] { body };
            // Every raider band takes its prizes with Breacher rounds fired from its leader's Breacher bay (U.SPC-T466, U.SPC-T521),
            // a destroyer fitting a second habitat to supply the teams.
            BreacherBays.Equip(leader);
            int escorts = 1 + rng.Next(2);
            for (int i = 0; i < escorts; i++)
            {
                var e = Fleet.AddShip(Name(), ShipClass.Destroyer, pirates, ShipRole.Escort);
                FleetPatterns.Commission(e, slips, null);
                Fleet.AddEscort(leader, e, Fleet.SlotFor(i));
            }
            // Every raider band keeps a lane interdictor to pull its marks out of the hyperlanes (U.SPC-T479): a destroyer with
            // a field projector fitted into its free space (U.SPC-T507). No pulse without one.
            var interdictor = Fleet.AddShip(Name(), ShipClass.Destroyer, pirates, ShipRole.Escort);
            LaneInterdictors.Fit(interdictor);
            Fleet.AddEscort(leader, interdictor, Fleet.SlotFor(escorts));
            Fleet.Station(leader, body, jd);
            double hands = Crews.Complement(leader.Spec.Class) + (escorts + 1) * Crews.Complement(ShipClass.Destroyer);
            Crews.Draft(pirates, hands);
            world.NotifyAdded(leader);
        }

        /// <summary>The most hands a new raider band can need (a cruiser, two destroyers and the destroyer fitted as its lane interdictor).</summary>
        static double BandComplement => Crews.Complement(ShipClass.Cruiser) + 3 * Crews.Complement(ShipClass.Destroyer);

        void PirateRecruiting(double jd)
        {
            if (LiveHavenOf(world.Pirates) == null) return;
            int groups = 0;
            var based = new Dictionary<Settlement, int>();
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && !s.IsEscort && s.Faction.IsPirate && !s.Runner)
                {
                    groups++;
                    if (HomeHaven(s) is Settlement h) based[h] = (based.TryGetValue(h, out int n) ? n : 0) + 1;
                }
            // Smuggling and protection rackets keep the Banner afloat between prizes.
            world.Pirates.Treasury += world.PirateIncomePerDay;
            if (groups >= world.PirateGroupCap || world.Pirates.Treasury < 5e7 || rng.NextDouble() > 0.08) return;
            // A new band needs hands: hostages pressed into service, and whoever else signs on.
            if (world.Pirates.Manpower < BandComplement) return;
            // The new band sails from the standing haven with the fewest of its own.
            Settlement from = null;
            int fewest = int.MaxValue;
            foreach (var st in Economy.Settlements)
            {
                if (st.Kind != SettlementKind.Haven || st.Fallen || st.Owner != world.Pirates) continue;
                int n = based.TryGetValue(st, out int k) ? k : 0;
                if (n < fewest)
                {
                    fewest = n;
                    from = st;
                }
            }
            world.Pirates.Treasury -= 4e7;
            RaisePirateGroup(jd, from.Body);
            world.Log(jd, $"A new {world.Pirates.Name} raider group sails from {(from.Discovered ? from.Name : "somewhere in the dark")}", WorldEventKind.Info, from.Discovered ? from.Body : -1);
        }

        /// <summary>A navy at or below this many warships, and outnumbered three to one, is beaten (see <see cref="Beaten"/>).</summary>
        public const int BeatenNavyShips = 4;

        /// <summary>Warships <paramref name="f"/> still has afloat.</summary>
        public int Warships(Faction f)
        {
            int n = 0;
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && s.Faction == f && s.Spec.IsMilitary) n++;
            return n;
        }

        /// <summary>
        /// Whether <paramref name="f"/> has lost its war with <paramref name="enemy"/> for now: after real losses its
        /// navy is down to a handful of hulls outnumbered three to one, or its last yard is under siege by a stronger
        /// navy. A beaten power sues for peace and rebuilds instead of being annihilated.
        /// </summary>
        public bool Beaten(Faction f, Faction enemy)
        {
            int own = Warships(f), them = Warships(enemy);
            if (own >= them) return false;
            // Its yards all under the enemy's guns: beaten however few hulls it has lost since the war last flared up
            // (the count starts again then, and a siege needs only days to take a yard).
            int free = 0;
            bool besieged = false;
            foreach (var st in Economy.Settlements)
            {
                if (st.Owner != f || !st.HasShipyard) continue;
                if (besiegers.TryGetValue(st, out var by) && by == enemy) besieged = true;
                else free++;
            }
            if (besieged && free == 0) return true;
            return Losses(f) > 2 && own <= BeatenNavyShips && own * 3 < them;
        }

        /// <summary>Wars wind down to ceasefires when both sides have bled, and flare up again months later.</summary>
        void DiplomacyCycle(double jd)
        {
            var a = world.RivalA;
            var b = world.RivalB;
            if (a == null || b == null) return;
            var r = Diplomacy.Get(a, b);
            if (r == Relation.War)
            {
                // A navy that has been beaten sues for peace rather than fight on to the last hull and the last yard.
                var loser = Beaten(a, b) ? a : Beaten(b, a) ? b : null;
                if (loser != null)
                {
                    var winner = loser == a ? b : a;
                    Diplomacy.Set(a, b, Relation.Ceasefire);
                    ceasefireUntilJd = jd + 120 + rng.NextDouble() * 120;
                    world.Log(jd, $"The {loser.Name}, its navy broken, sues for peace: ceasefire with the {winner.Name}", WorldEventKind.Diplomacy);
                }
                else if (jd - warStartJd > 240 && Losses(a) > 2 && Losses(b) > 2 && rng.NextDouble() < 0.012)
                {
                    Diplomacy.Set(a, b, Relation.Ceasefire);
                    ceasefireUntilJd = jd + 90 + rng.NextDouble() * 120;
                    world.Log(jd, $"Ceasefire between the {a.Name} and the {b.Name}", WorldEventKind.Diplomacy);
                }
            }
            else if (r == Relation.Ceasefire && jd > ceasefireUntilJd)
            {
                // Neither side goes back to war while one of them is still too weak to fight: it rebuilds first.
                if (Beaten(a, b) || Beaten(b, a))
                {
                    ceasefireUntilJd = jd + 30;
                    return;
                }
                Diplomacy.Set(a, b, Relation.War);
                RevokeBookings();
                warStartJd = jd;
                losses[a] = 0;
                losses[b] = 0;
                world.Log(jd, $"The ceasefire collapses: the {a.Name} and the {b.Name} are at war again", WorldEventKind.Diplomacy);
            }
        }

        // ---------------------------------------------------------------- shipbuilding

        /// <summary>What a navy's yard lays down next, or null to leave it to the merchant fleet.</summary>
        public ShipClass? ChooseWarship(Settlement yard)
        {
            var f = yard.Owner;
            if (!f.IsMilitary) return null;
            // The patrol wings are a standing force of their own, not counted against the battle fleet's cap (U.SPC-T371).
            int count = 0;
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && s.Faction == f && s.Spec.IsMilitary && !s.PatrolWing) count++;
            // What it lays down, and how big a navy it keeps, are its polity's build plan (U.SPC-T423; the balanced fleet unless told otherwise).
            var command = world.Polities?.Command;
            var plan = command?.BuildPlanOf(f) ?? BuildPlan.Balanced;
            if (plan.Cycle.Length == 0) return null;
            int cap = AtWar(f) ? world.NavyCapAtWar : world.NavyCap;
            double share = command?.NavyShare(f) ?? 1;
            if (share != 1) cap = (int)Math.Round(cap * share);
            if (count >= cap) return null;
            // At war, a gate breaker to take the enemy's gates (U.SPC-T478), when the yard has its keel's worth to hand.
            var breaker = ShipClass.GateBreaker;
            if (yard.BuildPoints >= Economy.HullCost(breaker) && f.Treasury >= Economy.HullPrice(breaker) && Crews.CanCrew(f, breaker))
            {
                int breakers = 0;
                foreach (var s in Fleet.Ships)
                    if (!s.Destroyed && s.Faction == f && GateBreakers.IsClass(s.Spec.Class)) breakers++;
                if (GateBreakers.Wanted(f, AtWar(f), breakers)) return breaker;
            }
            return plan.Cycle[(builtWarships + yard.Id) % plan.Cycle.Length];
        }

        static readonly string[] HullNames =
        {
            "Valiant", "Bastion", "Sentinel", "Corvus", "Tenacity", "Ardent", "Harrier", "Rampart", "Vigil", "Onslaught",
            "Meridian", "Paladin", "Warden", "Sabre", "Gauntlet", "Thunderer", "Steadfast", "Lodestar", "Zenith", "Implacable",
        };

        public Ship LaunchWarship(Settlement yard, ShipClass cls, double jd)
        {
            builtWarships++;
            string name = HullNames[builtWarships % HullNames.Length] + (builtWarships >= HullNames.Length ? " " + (builtWarships / HullNames.Length + 1) : "");
            // Reinforce an under-strength group refitting here, or form a new one.
            Ship group = null;
            foreach (var s in Fleet.Ships)
            {
                // Never the player's ship: escorts would follow a schedule the player isn't flying.
                if (s.Destroyed || s.IsEscort || s.Faction != yard.Owner || !s.Spec.IsMilitary || s.Battle != null || s == world.PilotedShip || s.PatrolWing) continue;
                if (s.Phase != ShipPhase.Parked || s.Orbit.Body != yard.Body) continue;
                int escorts = 0;
                foreach (var e in Fleet.Ships)
                    if (e.Leader == s && !e.Destroyed) escorts++;
                if (escorts < 5 && s.Combat.Spec.Hull >= CombatSpec.Of(cls).Hull) group = s;
            }
            // A new command ship (U.SPC-T477) goes to lead a group here that has none, whatever its flagship's size.
            bool command = FleetCoordination.IsCommandClass(cls);
            if (command)
            {
                Ship led = null;
                foreach (var s in Fleet.Ships)
                {
                    if (s.Destroyed || s.IsEscort || s.Faction != yard.Owner || !s.Spec.IsMilitary || s.Battle != null || s == world.PilotedShip || s.PatrolWing) continue;
                    if (s.Phase != ShipPhase.Parked || s.Orbit.Body != yard.Body || GroupHasCommandShip(s)) continue;
                    if (led == null || s.Combat.Spec.Hull > led.Combat.Spec.Hull) led = s;
                }
                if (led != null) group = led;
            }
            Ship ship;
            // The new hull is built to its navy's patterns, by its doctrine, its wealth and what this yard can build (U.SPC-T505).
            var plan = world.Polities?.Command?.BuildPlanOf(yard.Owner);
            if (group != null)
            {
                ship = Fleet.AddShip(name, cls, yard.Owner, ShipRole.Escort);
                FleetPatterns.Commission(ship, yard, plan);
                Fleet.AddEscort(group, ship, Fleet.SlotFor(FreeSlot(group)));
                if (command && !GroupHasCommandShip(group, ship)) TakeCommand(group, ship);
            }
            else
            {
                ship = Fleet.AddShip(name, cls, yard.Owner, ShipRole.Patrol);
                FleetPatterns.Commission(ship, yard, plan);
                var own = new List<int>();
                foreach (var s in Economy.Settlements)
                    if (s.Owner == yard.Owner && !own.Contains(s.Body)) own.Add(s.Body);
                ship.Route = own.ToArray();
                world.Launch(ship, yard, jd);
            }
            // The heavy equipment its polity's build plan wants in every group, fitted to this hull if its group carries none
            // yet and it can take it (U.SPC-T507: lane security's field projector makes it the group's lane interdictor).
            FitGroupEquipment(ship, group ?? ship, world.Polities?.Command?.BuildPlanOf(yard.Owner) ?? BuildPlan.Balanced);
            string joins = group == null ? "" : ship.IsEscort ? $", joining {group.Name}" : $", taking command of {group.Name}'s group";
            world.Log(jd, $"{yard.Name} commissions the {ShipClassSpec.Of(cls).DisplayName.ToLowerInvariant()} {ship.Name}" + joins, WorldEventKind.Launch, yard.Body);
            return ship;
        }

        /// <summary>Fits <paramref name="ship"/>, newly launched into the group led by <paramref name="leader"/>, with each
        /// facility <paramref name="plan"/> wants in every group (<see cref="BuildPlan.GroupFittings"/>) that no hull of the
        /// group carries yet, as far as it can take them (no yard bill: the hull's price covers it).</summary>
        void FitGroupEquipment(Ship ship, Ship leader, BuildPlan plan)
        {
            if (plan?.GroupFittings == null) return;
            foreach (var id in plan.GroupFittings)
            {
                var kind = FacilityKinds.Of(id);
                if (kind == null || GroupCarries(leader, id)) continue;
                Outfitting.Install(ship, kind);
            }
        }

        /// <summary>Whether a hull of the group led by <paramref name="leader"/> (the leader or a live escort) carries a
        /// facility of kind <paramref name="id"/>.</summary>
        bool GroupCarries(Ship leader, string id)
        {
            if (FleetCoordination.Count(leader, id) > 0) return true;
            foreach (var s in Fleet.Ships)
                if (s.Leader == leader && !s.Destroyed && FleetCoordination.Count(s, id) > 0) return true;
            return false;
        }

        /// <summary>Whether the group <paramref name="leader"/> leads carries a command ship (U.SPC-T477): the leader or a
        /// living escort, but not <paramref name="except"/>.</summary>
        bool GroupHasCommandShip(Ship leader, Ship except = null)
        {
            if (leader != except && FleetCoordination.IsCommandShip(leader)) return true;
            foreach (var e in Fleet.Ships)
                if (e.Leader == leader && e != except && !e.Destroyed && FleetCoordination.IsCommandShip(e)) return true;
            return false;
        }

        /// <summary>
        /// <paramref name="flag"/>, one of <paramref name="leader"/>'s escorts, takes command of its group (U.SPC-T477): the
        /// fleet's senior officers go aboard the command ship, which takes over the group's schedule, orders and objective
        /// as an heir does (see <see cref="Succession"/>), and the old flagship falls in as one of its escorts, in the
        /// station the command ship held.
        /// </summary>
        public void TakeCommand(Ship leader, Ship flag)
        {
            if (leader == null || flag == null || flag.Leader != leader || leader.IsEscort || leader.Destroyed || flag.Destroyed) return;
            var station = flag.FormationSlot;
            Succession(leader, true, flag);
            if (flag.Leader != null) return;
            Fleet.AddEscort(flag, leader, station);
            leader.Route = null;
        }

        // ---------------------------------------------------------------- helpers

        void BumpDanger(int body, double amount)
        {
            danger.TryGetValue(body, out double d);
            danger[body] = d + amount;
        }

        string Describe(Battle b, int side)
        {
            var f = b.SideLeaders[side];
            if (f == null) return "nobody";
            Ship flag = null;
            int count = 0;
            foreach (var u in b.Units)
            {
                if (u.Side != side || u.Kind == UnitKind.Fighter) continue;
                count++;
                if (u.Ship != null && (flag == null || u.Ship.Combat.Spec.Hull > flag.Combat.Spec.Hull)) flag = u.Ship;
            }
            var owner = flag?.Faction ?? f;
            string who = owner.IsPirate ? owner.Name + " raiders" : owner.Name;
            return flag != null ? $"{who} ({flag.Name}, {count} ships)" : $"{who} defences";
        }

        static string Short(Battle b, int side)
        {
            var f = b.SideLeaders[side];
            if (f != null && f.IsPirate) return "pirates";
            return f?.Prefix is string p && p.Length > 0 ? p : side == 0 ? "attackers" : "defenders";
        }

        /// <summary>The ordinal a repeat battle is named with: Second, Third, then 4th, ... 21st, 22nd, 23rd, 111th.</summary>
        public static string Ordinal(int n)
        {
            if (n == 2) return "Second";
            if (n == 3) return "Third";
            int tens = n % 100;
            string suffix = tens >= 11 && tens <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return n + suffix;
        }
    }
}
