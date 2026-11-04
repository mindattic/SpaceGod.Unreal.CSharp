using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The dynamic event system: watches the sim around the player and turns what is really happening
    /// there into timed, location-anchored adventures (<see cref="Opportunity"/>). Nothing is scripted:
    /// a distress call is a ship actually under attack, or actually drifting with dead drives after an
    /// interdiction pulse; a derelict is a hull pirates really stripped and left (<see cref="Ship.Derelict"/>);
    /// a wreck is one a battle really left (<see cref="Wreck"/>); an ambush is raiders really lurking off a
    /// lane with a record of trouble; an escort request is a merchant really riding a dangerous lane; a
    /// bounty names the captain of a real raider group. Only anomalies, rare, are rolled.
    ///
    /// Each opportunity is offered while it is within reach (<see cref="SpawnRadiusKm"/>, wider for
    /// bounties), never twice at once, rested a while after it ends, and capped at <see cref="MaxActive"/>
    /// at a time. The player sees it through by flying there (within <see cref="ArriveKm"/>) and, for a
    /// fight, staying until the sim settles it; ignored, it fails when it expires or when the situation
    /// resolves without them.
    /// </summary>
    public sealed class EventDirector
    {
        /// <summary>How far from the player something can be and still be offered, km (0.3 AU).</summary>
        public double SpawnRadiusKm = 0.3 * Astro.AuKm;

        /// <summary>Bounties travel further by word of mouth: captains this far away are still posted, km.</summary>
        public double BountyRadiusKm = 2 * Astro.AuKm;

        /// <summary>How close the player's ship must come to be on the scene, km.</summary>
        public double ArriveKm = 150000;

        /// <summary>How close the player's ship must come to drifting escape pods to take them aboard, km.</summary>
        public double PodPickupKm = 50;

        /// <summary>Credits per person brought home from escape pods, paid by their owners.</summary>
        public double PodRescueCredits = 8000;

        /// <summary>Standing with the owners for bringing their people home from the pods.</summary>
        public double PodRescueReputation = 5;

        /// <summary>The most opportunities open at once.</summary>
        public int MaxActive = 5;

        /// <summary>How often (sim days, about three minutes) the sim is scanned for new opportunities, so a fight
        /// lasting minutes is still caught at real time; resolutions are checked every update.</summary>
        public double ScanIntervalDays = 0.002;

        /// <summary>Lane danger (see <see cref="EventSources.LaneDanger"/>) at which raiders lurking there are reported.</summary>
        public double AmbushDanger = 2;

        /// <summary>Lane danger at which a merchant riding it asks for an escort.</summary>
        public double EscortDanger = 1;

        /// <summary>An escort is only worth asking for with at least this long left on the ride, days.</summary>
        public double MinEscortDays = 0.1;

        /// <summary>Anomalies turn up this often near the player, per day (rare by design).</summary>
        public double AnomalyRatePerDay = 0.15;

        /// <summary>How many ended opportunities are kept for the HUD's history.</summary>
        public int HistoryLength = 40;

        public readonly List<Opportunity> Active = new List<Opportunity>();
        public readonly List<Opportunity> History = new List<Opportunity>();

        /// <summary>The player's standing with each faction, raised by opportunities seen through.</summary>
        public readonly Dictionary<Faction, double> Reputation = new Dictionary<Faction, double>();

        public event Action<Opportunity> Spawned;
        public event Action<Opportunity> Resolved;

        readonly Random rng;
        readonly Dictionary<string, double> restUntil = new Dictionary<string, double>();
        double nextScanJd = double.NegativeInfinity;
        double lastAnomalyRollJd = double.NaN;
        int nextId;

        public EventDirector(int seed = 0) => rng = new Random(seed);

        public double ReputationWith(Faction f) => f != null && Reputation.TryGetValue(f, out var r) ? r : 0;

        /// <summary>
        /// Advances the director to <paramref name="jd"/>: refreshes every open opportunity's position,
        /// marks those the player's ship (<paramref name="playerShip"/>, null when only watching) has
        /// reached, settles any that are won, lost or expired, and -- every <see cref="ScanIntervalDays"/>
        /// -- offers new ones from what is happening near <paramref name="playerHelio"/>.
        /// </summary>
        public void Update(double jd, EventSources src, DVec3 playerHelio, Ship playerShip)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var op = Active[i];
                Refresh(op, src);
                // Arriving counts only from another ship: taking the helm of the very ship it is about isn't
                // seeing it through (its spot is the player's own, so it would read as arrived at once).
                // Nor from a hull of the very raiders a bounty or ambush is on (one of their own escorts, taken
                // over in formation): nobody collects on their own band. That is the specific flagship-and-
                // escorts group the opportunity is about (Ship.Band), not the whole pirate clan -- a raider
                // from an unrelated band of the same clan still collects (U.SPC-T363). Band, not Leader/
                // Controller, survives EnterPiloting clearing an escort's Leader the moment it is taken.
                bool sameBand = op.Ship != null && playerShip != null && op.Ship.Band == playerShip.Band
                    && (op.Kind == OpportunityKind.Bounty || op.Kind == OpportunityKind.PirateAmbush);
                if (playerShip != null && !playerShip.Destroyed && op.Ship != playerShip && !sameBand && (op.HelioKm - playerHelio).Length <= ArriveKm)
                {
                    op.Engaged = true;
                    op.EngagedBy = playerShip;
                    op.EngagedFaction = playerShip.Faction;
                }
                Settle(op, jd, src, playerShip, playerHelio);
            }
            if (jd < nextScanJd) return;
            nextScanJd = jd + ScanIntervalDays;
            Scan(jd, src, playerHelio, playerShip);
        }

        // ---------------------------------------------------------------- spawning

        void Scan(double jd, EventSources src, DVec3 player, Ship playerShip)
        {
            PruneRest(jd);
            var found = new List<(Opportunity op, double distance)>();
            bool bountyOpen = Active.Exists(o => o.Kind == OpportunityKind.Bounty);
            bool anomalyOpen = Active.Exists(o => o.Kind == OpportunityKind.Anomaly);

            foreach (var s in src.Ships)
            {
                if (s == null || s.Destroyed || s.GateDeparted || s == playerShip || s.IsEscort || s.Faction == null) continue;
                var pos = src.ShipHelio(s);
                double d = (pos - player).Length;
                Opportunity op = null;
                if (s.Faction.IsPirate)
                {
                    if (d <= SpawnRadiusKm && s.Ambushing && !s.Derelict && LaneOf(s) is Lane lurkLane && src.LaneDanger(lurkLane) >= AmbushDanger)
                        op = Ambush(s, lurkLane, jd, src);
                    else if (!bountyOpen && d <= BountyRadiusKm && !s.Derelict && s.Spec.IsMilitary)
                        op = BountyOn(s, jd, src);
                }
                else if (d <= SpawnRadiusKm)
                {
                    if (s.Derelict) op = DerelictOf(s, jd);
                    else if (s.Battle != null && !s.Battle.Over && !s.Spec.IsMilitary) op = DistressUnderAttack(s, jd, src);
                    else if (s.OutOfPropellant && s.Battle == null) op = DistressDry(s, jd, src);
                    else if (s.Phase == ShipPhase.Adrift && s.Battle == null) op = DistressAdrift(s, jd, src);
                    else if (s.Phase == ShipPhase.Lane && s.Battle == null && src.IsMerchant(s) && s.LaneLeg.Lane is Lane lane
                             && src.LaneDanger(lane) >= EscortDanger && s.PhaseEndJd - jd >= MinEscortDays)
                        op = Escort(s, lane, jd, src);
                }
                if (op == null) continue;
                op.HelioKm = pos;
                found.Add((op, d));
            }

            foreach (var w in src.Wrecks)
            {
                if (w == null || w.Ship == null || w.Expired(jd) || w.CreatedJd + w.LifetimeDays - jd < 0.25) continue;
                var pos = src.WreckHelio(w);
                double d = (pos - player).Length;
                if (d > SpawnRadiusKm) continue;
                var op = WreckOf(w, jd);
                op.HelioKm = pos;
                found.Add((op, d));
            }

            foreach (var pod in src.Pods)
            {
                if (pod == null || !pod.Drifting || pod.AirLeftDays(jd) < 0.1) continue;
                var pos = src.PodHelio(pod);
                double d = (pos - player).Length;
                if (d > SpawnRadiusKm) continue;
                var op = PodsOf(pod, jd, src);
                op.HelioKm = pos;
                found.Add((op, d));
            }

            if (!anomalyOpen && AnomalyRatePerDay > 0)
            {
                double dt = double.IsNaN(lastAnomalyRollJd) ? ScanIntervalDays : Math.Min(1, Math.Max(0, jd - lastAnomalyRollJd));
                if (rng.NextDouble() < 1 - Math.Exp(-AnomalyRatePerDay * dt))
                {
                    var op = AnomalyNear(player, jd, src);
                    found.Add((op, (op.HelioKm - player).Length));
                }
            }
            lastAnomalyRollJd = jd;

            // Lives at stake first, then fights, then salvage; nearest first within each.
            found.Sort((a, b) =>
            {
                int c = Priority(a.op.Kind).CompareTo(Priority(b.op.Kind));
                return c != 0 ? c : a.distance.CompareTo(b.distance);
            });
            foreach (var (op, _) in found)
            {
                if (Active.Count >= MaxActive) break;
                if (IsOpen(op.SourceKey) || IsResting(op.SourceKey, jd)) continue;
                // A faction that wants the player, or no longer trusts them, offers them no work.
                if (op.Patron != null && !src.WillHire(op.Patron)) continue;
                if (op.Kind == OpportunityKind.Bounty && Active.Exists(o => o.Kind == OpportunityKind.Bounty)) continue;
                op.Id = nextId++;
                op.SpawnJd = jd;
                op.State = OpportunityState.Active;
                Active.Add(op);
                Spawned?.Invoke(op);
            }
        }

        static int Priority(OpportunityKind k)
        {
            switch (k)
            {
                case OpportunityKind.Distress: return 0;
                case OpportunityKind.EscapePods: return 0;
                case OpportunityKind.ConvoyEscort: return 1;
                case OpportunityKind.PirateAmbush: return 2;
                case OpportunityKind.Bounty: return 3;
                case OpportunityKind.Derelict: return 4;
                case OpportunityKind.Wreck: return 5;
                default: return 6;
            }
        }

        bool IsOpen(string key) => Active.Exists(o => o.SourceKey == key);

        bool IsResting(string key, double jd) => restUntil.TryGetValue(key, out var until) && jd < until;

        /// <summary>Drops a rested source once its rest is over: <see cref="IsResting"/> would say no to it anyway,
        /// so nothing is lost keeping the book from growing forever over a long game (U.SPC-T321). Most sources
        /// (a wreck, a pod, an anomaly) never repeat, so without this their rest entries would simply pile up.</summary>
        void PruneRest(double jd)
        {
            List<string> done = null;
            foreach (var kv in restUntil)
                if (jd >= kv.Value) (done ??= new List<string>()).Add(kv.Key);
            if (done != null) foreach (var k in done) restUntil.Remove(k);
        }

        /// <summary>How long a source is left alone after its opportunity ends, days.</summary>
        static double RestDays(OpportunityKind k)
        {
            switch (k)
            {
                case OpportunityKind.Distress: return 1;
                case OpportunityKind.ConvoyEscort: return 2;
                case OpportunityKind.PirateAmbush: return 4;
                case OpportunityKind.Bounty: return 30;
                default: return 1000; // a derelict or a wreck is only worth one look
            }
        }

        static Lane LaneOf(Ship s) => s.LaneLeg.Lane ?? s.LurkLane;

        static string Key(string kind, Ship s) => kind + ":ship:" + s.Id;

        static double CargoValue(Ship s) => s.HasCargo && s.CargoTonnes > 0 ? s.CargoTonnes * GoodInfo.Of(s.CargoGood).BasePrice : 0;

        static double RoundThousand(double v) => Math.Round(v / 1000) * 1000;

        static string Cr(double v) => v.ToString("N0", CultureInfo.InvariantCulture);

        Opportunity DistressUnderAttack(Ship s, double jd, EventSources src)
        {
            string where = src.BodyName(s.Battle.Body);
            return new Opportunity
            {
                Kind = OpportunityKind.Distress,
                SourceKey = Key("distress", s),
                Title = "Distress: " + s.Name,
                Hook = "Mayday, mayday: " + s.Name + " under attack " + (s.Battle.Site != null ? "beside the lane" : "off " + where) + ", shields failing. Any ship, please respond!",
                ExpiresJd = jd + 0.25,
                Ship = s,
                Battle = s.Battle,
                UnderAttack = true,
                Patron = s.Faction,
                Reward = new OpportunityReward { Credits = RoundThousand(100000 + 0.02 * CargoValue(s)), ReputationWith = s.Faction, Reputation = 6 },
            };
        }

        Opportunity DistressAdrift(Ship s, double jd, EventSources src)
        {
            string lane = s.LaneLeg.Lane != null ? " off the " + s.LaneLeg.Lane.Name + " lane" : "";
            return new Opportunity
            {
                Kind = OpportunityKind.Distress,
                SourceKey = Key("distress", s),
                Title = "Adrift: " + s.Name,
                Hook = s.Name + " here, pulled out of the lane" + lane + " by an interdiction pulse. Drives are dead and we're drifting. Requesting assistance.",
                ExpiresJd = jd + 1,
                Ship = s,
                Patron = s.Faction,
                Reward = new OpportunityReward { Credits = 60000, ReputationWith = s.Faction, Reputation = 3 },
            };
        }

        /// <summary>The share of a dry ship's tanks a pilot answering its call pumps across (as much as they can spare).</summary>
        public double DryTanksTransfer = 0.3;

        Opportunity DistressDry(Ship s, double jd, EventSources src)
        {
            string near = s.Orbit != null ? " off " + src.BodyName(s.Orbit.Body) : "";
            return new Opportunity
            {
                Kind = OpportunityKind.Distress,
                SourceKey = Key("distress", s),
                Title = "Dry tanks: " + s.Name,
                Hook = s.Name + " here, drifting" + near + " with our tanks run dry. The tanker is still a way out. Can anyone spare reaction mass?",
                ExpiresJd = Math.Max(jd + 0.1, s.PhaseEndJd),
                Ship = s,
                Patron = s.Faction,
                DryTanks = true,
                Reward = new OpportunityReward { Credits = 50000, ReputationWith = s.Faction, Reputation = 3 },
            };
        }

        Opportunity DerelictOf(Ship s, double jd)
        {
            // What it pays for is reporting and working the hulk (the salvage registry's award); what is in
            // its hold the player takes for themselves once latched on (see SalvageSession).
            var reward = new OpportunityReward { Credits = s.Spec.IsMilitary ? 180000 : 60000 };
            string aboard = s.Combat != null && s.Combat.Interior.Survivors + s.Combat.Interior.Marines > 0.5 ? " Sensors read movement aboard." : "";
            return new Opportunity
            {
                Kind = OpportunityKind.Derelict,
                SourceKey = Key("derelict", s),
                Title = "Derelict: " + s.Name,
                Hook = "No transponder answer from " + s.Name + ". Hull dark, airlocks open: boarded and left to drift. " + HoldReads(s.Salvage) + aboard +
                       " Match velocity and latch on before it breaks up.",
                ExpiresJd = Math.Min(jd + 5, s.DerelictSinceJd + 25),
                Ship = s,
                Reward = reward,
            };
        }

        Opportunity WreckOf(Wreck w, double jd)
        {
            return new Opportunity
            {
                Kind = OpportunityKind.Wreck,
                SourceKey = "wreck:" + w.Id,
                Title = "Wreck: " + w.Name,
                Hook = "The wreck of " + w.Name + " is still tumbling where it died. " + HoldReads(w.Salvage) + " Latch on and strip it.",
                ExpiresJd = Math.Min(jd + 6, w.CreatedJd + w.LifetimeDays),
                Wreck = w,
                Reward = new OpportunityReward { Credits = 25000 },
            };
        }

        /// <summary>What a hulk's scanned hold shows, for its hook.</summary>
        Opportunity PodsOf(EscapePod pod, double jd, EventSources src)
        {
            string where = pod.Site != null ? "beside the " + pod.Site.Hop.Lane.Name + " lane" : "off " + src.BodyName(pod.Body);
            string pods = pod.Pods == 1 ? "one escape pod" : pod.Pods + " escape pods";
            string captainAboard = pod.Captain != null
                ? " " + pod.Captain.Name + " is aboard" + (pod.Captain.Record.Wanted ? ", " + Cr(pod.Captain.Record.TotalBounty) + " cr on their head, paid in full alive: tractor them into your brig." : ".")
                : "";
            string hook = pod.Owners.IsPirate
                ? pods + " from the raider " + pod.Name + " drifting " + where + ", " + Cr(pod.People) + " aboard. The pirate hunters pay for raiders handed over."
                : "Beacon: " + pods + " from the " + pod.Name + " drifting " + where + " with " + Cr(pod.People) + " " + pod.Owners.Name + " crew aboard and "
                  + pod.AirLeftDays(jd).ToString("0", CultureInfo.InvariantCulture) + " days of air. Any ship, please pick us up.";
            var reward = pod.Owners.IsPirate
                ? new OpportunityReward { Credits = RoundThousand(pod.People * Crews.PrisonerBountyPerHead), ReputationWith = src.PirateHunters, Reputation = 3 }
                : new OpportunityReward { Credits = RoundThousand(pod.People * PodRescueCredits), ReputationWith = pod.Owners, Reputation = PodRescueReputation };
            return new Opportunity
            {
                Kind = OpportunityKind.EscapePods,
                SourceKey = "pods:" + pod.Id,
                Title = "Escape pods: " + pod.Name,
                Hook = hook + captainAboard,
                ExpiresJd = pod.LaunchedJd + pod.LifeSupportDays,
                Pod = pod,
                Patron = pod.Owners.IsPirate ? src.PirateHunters : pod.Owners,
                Reward = reward,
            };
        }

        static string HoldReads(IReadOnlyList<SalvageStack> salvage)
        {
            foreach (var stack in salvage)
                if (stack.Tonnes > 0) return "Its hold still reads " + GoodInfo.Of(stack.Good).Name.ToLowerInvariant() + ".";
            return "Scrap, at least.";
        }

        Opportunity Ambush(Ship s, Lane lane, double jd, EventSources src)
        {
            double until = s.PhaseEndJd > jd ? Math.Min(jd + 2, s.PhaseEndJd) : jd + 2;
            return new Opportunity
            {
                Kind = OpportunityKind.PirateAmbush,
                SourceKey = "ambush:lane:" + lane.Id,
                Title = "Ambush: " + lane.Name + " lane",
                Hook = "Traffic warning: " + s.Faction.Name + " raiders are lying in wait off the " + lane.Name + " lane. Break them before they spring it.",
                ExpiresJd = until,
                Ship = s,
                Lane = lane,
                Patron = src.PirateHunters,
                Reward = new OpportunityReward { Credits = 300000, ReputationWith = src.PirateHunters, Reputation = 8 },
            };
        }

        Opportunity Escort(Ship s, Lane lane, double jd, EventSources src)
        {
            return new Opportunity
            {
                Kind = OpportunityKind.ConvoyEscort,
                SourceKey = Key("escort", s),
                Title = "Escort: " + s.Name,
                Hook = s.Name + " is crossing the " + lane.Name + " lane" + (s.HasCargo ? " with " + GoodInfo.Of(s.CargoGood).Name.ToLowerInvariant() : "") +
                       " and there have been ambushes. Paying for an escort to the far ramp.",
                ExpiresJd = s.PhaseEndJd + 0.05,
                Ship = s,
                Lane = lane,
                Patron = s.Faction,
                Reward = new OpportunityReward { Credits = RoundThousand(80000 + 0.03 * CargoValue(s)), ReputationWith = s.Faction, Reputation = 5 },
            };
        }

        Opportunity BountyOn(Ship s, double jd, EventSources src)
        {
            string captain = PirateCaptains.NameFor(s);
            string payer = src.PirateHunters != null ? src.PirateHunters.Name : "Every port in the system";
            // The hunters' standing price on a raider captain. Whatever the law has posted for what they have actually
            // done is paid by the law on top (U.SPC-T63): in full for the captain alive, half on a scanned wreck.
            double credits = 650000;
            double posted = RoundThousand(src.PostedBounty(s));
            return new Opportunity
            {
                Kind = OpportunityKind.Bounty,
                SourceKey = Key("bounty", s),
                Title = "Bounty: " + captain,
                Hook = payer + " posts " + Cr(credits) + " cr for " + captain + ", commanding the raider " + s.Name + ". Dead or taken."
                    + (posted > 0 ? " The law's " + Cr(posted) + " cr on their head pays in full alive (board the disabled flagship), half dead on a scanned wreck." : ""),
                ExpiresJd = jd + 12,
                Ship = s,
                Captain = captain,
                Patron = src.PirateHunters,
                Reward = new OpportunityReward { Credits = credits, ReputationWith = src.PirateHunters, Reputation = 10 },
            };
        }

        static readonly (string title, string hook, double credits, Good? good)[] Anomalies =
        {
            ("Ghost transponder", "A transponder code nobody has registered in forty years, pinging from empty space near {0}.", 120000, null),
            ("Venting ice fragment", "A comet fragment is venting near {0}; the spectra say volatiles worth scooping.", 60000, Good.HeliumThree),
            ("Lensing ripple", "Something is bending starlight near {0}. Survey offices pay well for readings like this.", 220000, null),
            ("Silent pod", "A sealed cargo pod is drifting near {0}, no markings, no owner on record.", 40000, Good.Platinum),
            ("Old probe", "A pre-lane survey probe is still tumbling near {0}, its data core intact.", 150000, Good.Electronics),
        };

        Opportunity AnomalyNear(DVec3 player, double jd, EventSources src)
        {
            var a = Anomalies[rng.Next(Anomalies.Length)];
            int body = src.NearestBody(player);
            // Somewhere a short trip from the player: a random direction, a tenth to half the spawn radius away.
            double z = 2 * rng.NextDouble() - 1, t = 2 * Math.PI * rng.NextDouble(), r = Math.Sqrt(1 - z * z);
            var dir = new DVec3(r * Math.Cos(t), r * Math.Sin(t), z);
            var spot = player + dir * (SpawnRadiusKm * (0.1 + 0.4 * rng.NextDouble()));
            var bodyHelio = src.BodyHelio(body);
            var reward = new OpportunityReward { Credits = a.credits };
            if (a.good.HasValue) { reward.CargoGood = a.good.Value; reward.CargoCrates = 1; }
            return new Opportunity
            {
                Kind = OpportunityKind.Anomaly,
                SourceKey = "anomaly:" + nextId + ":" + jd.ToString("0.000", CultureInfo.InvariantCulture),
                Title = a.title,
                Hook = string.Format(CultureInfo.InvariantCulture, a.hook, src.BodyName(body)),
                ExpiresJd = jd + 3,
                Body = body,
                OffsetKm = spot - bodyHelio,
                HelioKm = spot,
                Reward = reward,
            };
        }

        // ---------------------------------------------------------------- following and settling

        static void Refresh(Opportunity op, EventSources src)
        {
            if (op.Ship != null && !op.Ship.Destroyed) op.HelioKm = src.ShipHelio(op.Ship);
            else if (op.Wreck != null) op.HelioKm = src.WreckHelio(op.Wreck);
            else if (op.Pod != null) op.HelioKm = src.PodHelio(op.Pod);
            else if (op.Body >= 0) op.HelioKm = src.BodyHelio(op.Body) + op.OffsetKm;
        }

        void Settle(Opportunity op, double jd, EventSources src, Ship player, DVec3 playerHelio)
        {
            var s = op.Ship;
            switch (op.Kind)
            {
                case OpportunityKind.Distress:
                    if (s.Destroyed) { Fail(op, jd, s.Name + " was destroyed."); return; }
                    if (s.Derelict || s.Faction != op.Patron) { Fail(op, jd, s.Name + " was taken."); return; }
                    if (op.UnderAttack)
                    {
                        if (s.Battle != op.Battle || op.Battle.Over)
                        {
                            if (op.Engaged) Complete(op, jd, player, s.Name + " made it out with your help.");
                            else Fail(op, jd, s.Name + " fought clear without you.");
                            return;
                        }
                    }
                    else if (op.DryTanks)
                    {
                        if (op.Engaged && player != null && !player.Destroyed && s.OutOfPropellant)
                        {
                            double moved = Propulsion.Transfer(player, s, s.PropellantCapacity * DryTanksTransfer);
                            if (moved > 0)
                            {
                                // Enough to get going on: it does not wait for the tanker.
                                if (s.PropellantFraction >= 0.25) s.PhaseEndJd = Math.Min(s.PhaseEndJd, jd);
                                Complete(op, jd, player, "You pumped " + Cr(moved) + " t of reaction mass across to " + s.Name + ".");
                                return;
                            }
                        }
                        if (!s.OutOfPropellant) { Fail(op, jd, "A tanker reached " + s.Name + " first."); return; }
                    }
                    else
                    {
                        if (op.Engaged) { Complete(op, jd, player, "You got " + s.Name + " underway again."); return; }
                        if (s.Phase != ShipPhase.Adrift) { Fail(op, jd, s.Name + " got its drives back and went on without you."); return; }
                    }
                    break;

                // Coming within sight of a hulk is not seeing it through: that takes latching on and working
                // it (HulkWorked), so these only fail here.
                case OpportunityKind.Derelict:
                    if (s.Destroyed || !s.Derelict) { Fail(op, jd, s.Name + " broke up before anyone reached it."); return; }
                    break;

                case OpportunityKind.Wreck:
                    if (!Contains(src.Wrecks, op.Wreck) || op.Wreck.Expired(jd)) { Fail(op, jd, "The wreck of " + op.Wreck.Name + " broke up."); return; }
                    break;

                case OpportunityKind.PirateAmbush:
                    if (s.Destroyed || s.Derelict || !s.Faction.IsPirate)
                    {
                        if (op.Engaged) Complete(op, jd, player, "The raiders off the " + op.Lane.Name + " lane are broken.");
                        else Fail(op, jd, "Someone else dealt with the raiders off the " + op.Lane.Name + " lane.");
                        return;
                    }
                    if (!op.Engaged && !s.Ambushing) { Fail(op, jd, "The raiders off the " + op.Lane.Name + " lane moved on."); return; }
                    break;

                case OpportunityKind.ConvoyEscort:
                    if (s.Destroyed) { Fail(op, jd, s.Name + " was destroyed on the lane."); return; }
                    if (s.Derelict || s.Faction != op.Patron) { Fail(op, jd, s.Name + " was taken on the lane."); return; }
                    if (s.Phase != ShipPhase.Lane && s.Phase != ShipPhase.Adrift && s.Battle == null)
                    {
                        if (op.Engaged) Complete(op, jd, player, "You saw " + s.Name + " safely across the " + op.Lane.Name + " lane.");
                        else Fail(op, jd, s.Name + " made the crossing without you.");
                        return;
                    }
                    break;

                case OpportunityKind.Bounty:
                    if (s.Destroyed || s.Derelict || !s.Faction.IsPirate)
                    {
                        if (op.Engaged) Complete(op, jd, player, op.Captain + " is finished. The bounty is yours.");
                        else Fail(op, jd, op.Captain + " fell to someone else.");
                        return;
                    }
                    break;

                case OpportunityKind.EscapePods:
                {
                    var pod = op.Pod;
                    if (!pod.Drifting)
                    {
                        string by = pod.TakenBy != null ? "the " + pod.TakenBy.Name : "someone";
                        Fail(op, jd, pod.State switch
                        {
                            PodState.Lost => "The pods from the " + pod.Name + " fell silent before anyone reached them.",
                            PodState.Hostage => "Raiders hauled in the pods from the " + pod.Name + " first.",
                            PodState.Prisoner => Cap(by) + " took the crew of the " + pod.Name + " prisoner first.",
                            _ => Cap(by) + " picked up the pods from the " + pod.Name + " first.",
                        });
                        return;
                    }
                    if (op.Engaged && player != null && !player.Destroyed && (op.HelioKm - playerHelio).Length <= PodPickupKm)
                    {
                        double people = pod.People;
                        var fate = src.PickUpPod(pod, player);
                        string aboard = Cr(people) + " " + (pod.Owners.IsPirate ? "raiders" : pod.Owners.Name + " crew");
                        if (fate == PodState.Rescued && !pod.Owners.IsPirate)
                            Complete(op, jd, player, "You took " + aboard + " aboard from the " + pod.Name + "'s pods.");
                        else if (fate == PodState.Prisoner && pod.Owners.IsPirate)
                            Complete(op, jd, player, "You hauled in " + aboard + " from the " + pod.Name + "'s pods for the pirate hunters.");
                        else
                        {
                            // Raiders picking up their own, or crews taken as hostages or prisoners: nobody pays a
                            // rescuer for that (a ransom, if any, comes later).
                            op.Reward = new OpportunityReward();
                            string what = fate == PodState.Rescued ? "You took " + aboard + " aboard"
                                : fate == PodState.Hostage ? "You hold " + aboard + " for ransom"
                                : "You took " + aboard + " prisoner";
                            Complete(op, jd, player, what + " from the " + pod.Name + "'s pods.");
                        }
                        return;
                    }
                    break;
                }

                case OpportunityKind.Anomaly:
                    if (op.Engaged) { Complete(op, jd, player, "You logged the " + op.Title.ToLowerInvariant() + "."); return; }
                    break;
            }
            if (jd >= op.ExpiresJd) Fail(op, jd, "Expired: " + op.Title + ".");
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static bool Contains(IReadOnlyList<Wreck> list, Wreck w)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == w) return true;
            return false;
        }

        /// <summary>Credits per survivor brought out of a derelict, paid by the ship's owners.</summary>
        public double SurvivorRescueCredits = 15000;

        /// <summary>Standing with a derelict's owners for bringing its survivors out.</summary>
        public double SurvivorRescueReputation = 4;

        /// <summary>
        /// The player latched on to a hulk and got something out of it (<see cref="SalvageSession.Worked"/>):
        /// the derelict or wreck opportunity on it, if one is open, is seen through -- by that ship, whether or
        /// not it was seen arriving -- and paid. Survivors brought out add their owners' thanks to the reward.
        /// Returns the opportunity completed, or null if none was open on it.
        /// </summary>
        public Opportunity HulkWorked(SalvageSession session, double jd)
        {
            if (session == null || !session.Worked) return null;
            var op = Active.Find(o => session.Derelict != null
                ? o.Kind == OpportunityKind.Derelict && o.Ship == session.Derelict
                : o.Kind == OpportunityKind.Wreck && o.Wreck == session.Wreck);
            if (op == null) return null;
            var player = session.Player;
            op.Engaged = true;
            op.EngagedBy = player;
            op.EngagedFaction = player.Faction;
            // Whoever the hull belonged to when the clamps took hold (a claimed one has changed hands since).
            var owners = session.Owners;
            if (session.SurvivorsRescued >= 0.5 && owners != null && !owners.IsPirate && owners != player.Faction)
            {
                double thanks = RoundThousand(session.SurvivorsRescued * SurvivorRescueCredits);
                op.Reward.Credits += thanks;
                // Only their owners' thanks come out of the owners' treasury; the registry's award is new money.
                op.Reward.PatronShare = thanks;
                op.Reward.ReputationWith = owners;
                op.Reward.Reputation += SurvivorRescueReputation;
            }
            Complete(op, jd, player, "You " + session.Summary() + " aboard " + session.Name + ".");
            return op;
        }

        void Complete(Opportunity op, double jd, Ship player, string how)
        {
            Pay(op, player);
            End(op, OpportunityState.Completed, jd, how + " Reward: " + op.Reward + ".");
        }

        void Fail(Opportunity op, double jd, string how) => End(op, OpportunityState.Failed, jd, how);

        void End(Opportunity op, OpportunityState state, double jd, string how)
        {
            op.State = state;
            op.ResolvedJd = jd;
            op.Outcome = how;
            Active.Remove(op);
            History.Add(op);
            if (History.Count > HistoryLength) History.RemoveRange(0, History.Count - HistoryLength);
            restUntil[op.SourceKey] = jd + RestDays(op.Kind);
            Resolved?.Invoke(op);
        }

        /// <summary>Credits go to the player's purse (their faction's treasury, as the market board shows it),
        /// cargo into their grid hold as far as it fits, and standing to the patron.</summary>
        void Pay(Opportunity op, Ship player)
        {
            var r = op.Reward;
            // Paid to whoever saw it through, as they were when they came: not whoever holds the helm
            // when it resolves (another ship, another faction), nor the captor of a ship since lost.
            var purse = op.EngagedFaction ?? player?.Faction;
            // A patron pays from its own treasury, capped at what it holds (as Crews' pod rescue does); the
            // reward is rewritten to what actually moved. Patronless rewards stay new money (U.SPC-T391).
            var patron = r.ReputationWith;
            if (patron != null && patron != purse && r.Credits > 0)
            {
                double owed = Math.Min(r.Credits, r.PatronShare ?? r.Credits);
                double paid = Math.Max(0, Math.Min(owed, patron.Treasury));
                patron.Treasury -= paid;
                r.Credits = op.Reward.Credits = r.Credits - owed + paid;
            }
            if (purse != null) purse.Treasury += r.Credits;
            // Cargo into the hold of the ship that came, while it still exists and is still theirs; else
            // into the ship they fly now, if it is their own.
            Ship hold = op.EngagedBy != null && !op.EngagedBy.Destroyed && op.EngagedBy.Faction == purse ? op.EngagedBy
                : player != null && !player.Destroyed && player.Faction == purse ? player : null;
            if (hold == null) op.Reward.CargoCrates = 0;
            else if (r.CargoCrates > 0)
            {
                player = hold;
                int stored = 0;
                for (int i = 0; i < r.CargoCrates; i++)
                    if (player.PersonalHold.AddFirstFit(r.CargoGood) != null) stored++;
                op.Reward.CargoCrates = stored;
            }
            if (r.ReputationWith != null && r.Reputation != 0)
                Reputation[r.ReputationWith] = ReputationWith(r.ReputationWith) + r.Reputation;
        }
    }
}
