using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>What became of an escape pod cluster.</summary>
    public enum PodState
    {
        /// <summary>Drifting where its hull died, broadcasting, its air running down.</summary>
        Drifting,

        /// <summary>Picked up by its own side or a friendly ship: the crew go home.</summary>
        Rescued,

        /// <summary>Picked up by pirates: the crew are held for ransom.</summary>
        Hostage,

        /// <summary>Picked up by an enemy: the crew are prisoners of war (or, for raiders, in irons).</summary>
        Prisoner,

        /// <summary>Nobody came before the air ran out.</summary>
        Lost,
    }

    /// <summary>How a hull's last moments came, which decides how many of its people reach the pods.</summary>
    public enum AbandonCause
    {
        /// <summary>Shot to pieces: the hull goes over a few minutes, and most who can run for the pods make it.</summary>
        Destroyed,

        /// <summary>The reactor went: seconds, not minutes, and few get out.</summary>
        ReactorLost,

        /// <summary>Scuttled or cast adrift deliberately: the crew are put off in the pods in good order.</summary>
        Scuttled,
    }

    /// <summary>
    /// The pods one hull launched: <see cref="Pods"/> capsules with <see cref="People"/> aboard between them,
    /// drifting as a loose cluster from where the hull died and broadcasting a distress beacon until someone
    /// picks them up or their air runs out. Placed like a <see cref="Wreck"/>: <see cref="Anchor"/> is the
    /// battle's anchor in <see cref="Body"/>'s frame (or beside a lane, <see cref="Site"/>), and
    /// <see cref="LocalAt"/> the cluster's battle-local position, drifting slowly away from the hull.
    /// </summary>
    public sealed class EscapePod
    {
        public int Id;

        /// <summary>The hull the pods came from ("SFS Valiant", "Ceres Hub defence platform").</summary>
        public string Name;

        /// <summary>Whose people are aboard (and who pays to have them back).</summary>
        public Faction Owners;

        public double People;
        public int Pods;

        public int Body;
        public DVec3 Anchor, Local;
        public LaneSite Site;

        /// <summary>The way the cluster drifts from the hull (unit).</summary>
        public DVec3 DriftDir = DVec3.UnitX;

        public double LaunchedJd;

        /// <summary>Days of air the pods carry from launch.</summary>
        public double LifeSupportDays = Crews.LifeSupportDays;

        public PodState State;

        /// <summary>Who picked them up (the rescuers, the pirates, the enemy), or null.</summary>
        public Faction TakenBy;
        public double ResolvedJd;

        /// <summary>Groups pods launched together into one cluster (a battle's ejected fighter pilots), or null.</summary>
        public string MergeKey;

        /// <summary>For raiders' pods: the haven they sail from, which prisoners taken off them may give away.</summary>
        public Settlement Haven;

        /// <summary>A named outlaw captain who got away in these pods (their flagship going down, or a brig they
        /// broke out of), or null: whoever tractors the pods in has them (see <see cref="Outlaws.OnPodResolved"/>).</summary>
        public Outlaw Captain;

        /// <summary>Not escape pods but a Breacher round left drifting after a fight with its team alive inside (U.SPC-T519,
        /// <see cref="DriftingRounds"/>): <see cref="People"/> are its marines, and only a tow unit can bring it in.</summary>
        public bool Breacher;

        /// <summary>For a <see cref="Breacher"/> round: the side its team was fighting, whom pirates who salvage it may
        /// sell the marines to as slaves; or null.</summary>
        public Faction Enemy;

        /// <summary>The cluster drifts this far from the hull at most, km.</summary>
        public const double MaxDriftKm = 300;

        /// <summary>How fast the drift tails off, days (the pods' launch impulse, then nothing).</summary>
        public const double DriftDays = 3;

        public bool Drifting => State == PodState.Drifting;

        public double AirLeftDays(double jd) => Math.Max(0, LaunchedJd + LifeSupportDays - jd);

        /// <summary>How far the cluster has drifted from the hull by <paramref name="jd"/>, km.</summary>
        public double DriftKm(double jd) => MaxDriftKm * (1 - Math.Exp(-Math.Max(0, jd - LaunchedJd) / DriftDays));

        /// <summary>The cluster's battle-local position at <paramref name="jd"/>, km (from the battle anchor).</summary>
        public DVec3 LocalAt(double jd) => Local + DriftDir * DriftKm(jd);

        /// <summary>Its offset from <see cref="Body"/>'s centre at <paramref name="jd"/>, km (for a pod at a body, not a lane).</summary>
        public DVec3 PosAt(double jd) => Anchor + LocalAt(jd);
    }

    /// <summary>People held by another faction: pirates' hostages, or an enemy's prisoners of war.</summary>
    public sealed class Captives
    {
        public Faction Owners, Captors;
        public double People;

        /// <summary>Prisoners of war (released when the war ends) rather than hostages (ransomed).</summary>
        public bool Prisoners;

        /// <summary>Where they are held: a pirate haven (freed if it falls), or null.</summary>
        public Settlement Haven;

        public double SinceJd;

        /// <summary>When the hostages' ransom falls due.</summary>
        public double RansomJd;

        /// <summary>The hull they came off, for the log.</summary>
        public string From;

        /// <summary>Marines salvaged from a drifting Breacher round (U.SPC-T519): their captors may sell them to
        /// <see cref="Buyer"/> as slaves instead of ransoming them.</summary>
        public bool Marines;

        /// <summary>The side the marines were fighting, who would buy them as slaves; or null.</summary>
        public Faction Buyer;
    }

    /// <summary>
    /// Crew survival and the manpower behind every hull. Every crewed hull carries escape pods, one per
    /// <see cref="PodCapacity"/> of its complement; as it dies (or is scuttled) its people make for them
    /// (<see cref="EscapeShare"/>: most when it goes slowly, few when the reactor goes, nearly all when the
    /// crew are put off in good order), and the pods drift and broadcast for <see cref="LifeSupportDays"/>.
    /// Whoever finds them decides their fate: their own side or a friendly ship brings them home (and is
    /// paid <see cref="RescueRewardPerHead"/> by their owners), pirates take them as hostages for ransom,
    /// and an enemy takes them prisoner until the war ends. Every death, in the hull or in a pod nobody
    /// reached, wears down the owners' <see cref="Faction.CrewMorale"/> and is gone from their
    /// <see cref="Faction.Manpower"/> for good; new hulls and refits draw their crews from that pool, which
    /// recruiting from the owners' settlements tops up.
    /// </summary>
    public sealed class Crews
    {
        /// <summary>People per escape pod (a lifeboat-sized capsule).</summary>
        public const double PodCapacity = 20;

        /// <summary>Days of air and water a pod carries.</summary>
        public const double LifeSupportDays = 10;

        /// <summary>Credits per head a pod's owners pay whoever brings their people home.</summary>
        public const double RescueRewardPerHead = 5000;

        /// <summary>Credits per head pirates demand for hostages.</summary>
        public const double RansomPerHead = 60000;

        /// <summary>Credits per head the pirate hunters pay for raiders handed over from their pods.</summary>
        public const double PrisonerBountyPerHead = 20000;

        /// <summary>Morale lost per death (a capital ship's crew lost outright costs a navy a tenth or so).</summary>
        public const double MoraleLossPerDeath = 1 / 20000.0;

        /// <summary>Morale regained per person brought home.</summary>
        public const double MoraleGainPerRescue = 1 / 40000.0;

        /// <summary>Share of the way back to 1 that morale recovers each day.</summary>
        public const double MoraleRecoveryPerDay = 0.02;

        public const double MinMorale = 0.5, MaxMorale = 1.15;

        /// <summary>Spacers recruited a day per person in a faction's settlements.</summary>
        public const double RecruitPerPersonPerDay = 2e-5;

        /// <summary>The trained reserve a faction keeps, and tops up to, as a share of its settlements' people.</summary>
        public const double ReserveShare = 0.004;

        /// <summary>The pirates' reserve (they recruit from the desperate and the wanted, not from a census), and its daily intake.</summary>
        public const double PirateReserve = 6000, PirateRecruitPerDay = 25;

        /// <summary>How often a ship sharing a body with drifting pods finds them, per day: patrols search,
        /// merchants happen on them, raiders hunt for hostages.</summary>
        public const double PatrolFindRate = 3, MerchantFindRate = 0.8, PirateFindRate = 2;

        public readonly List<EscapePod> Pods = new List<EscapePod>();
        public readonly List<Captives> Held = new List<Captives>();

        /// <summary>Raised when a hull launches its pods.</summary>
        public event Action<EscapePod> Launched;

        /// <summary>Raised when pods are picked up (by whom, and the ship, if any) or their air runs out.</summary>
        public event Action<EscapePod, Ship> Resolved;

        /// <summary>Raised when captives go home (ransomed, exchanged, freed) or are lost to their captors; the text says how.</summary>
        public event Action<Captives, string, double> CaptivesReleased;

        /// <summary>The haven a pirate ship sails from (hostages are held there), or null.</summary>
        public Func<Ship, Settlement> HavenOf = s => null;

        /// <summary>Pirates' pods picked up by a navy: the prisoners may give their haven away.</summary>
        public Action<EscapePod, Faction, double> RaidersTaken;

        readonly Random rng;
        int nextId;

        public Crews(int seed = 0) => rng = new Random(seed);

        // ---------------------------------------------------------------- pods

        /// <summary>Pods a hull of this complement carries: one per <see cref="PodCapacity"/> people, at least one.</summary>
        public static int PodsFitted(double complement) => Math.Max(1, (int)Math.Ceiling(Math.Max(0, complement) / PodCapacity - 1e-9));

        /// <summary>Pods launched to carry <paramref name="people"/> off a hull of <paramref name="complement"/>: as many as they fill, no more than it carries.</summary>
        public static int PodsFor(double people, double complement) =>
            Math.Min(PodsFitted(Math.Max(complement, people)), Math.Max(1, (int)Math.Ceiling(people / PodCapacity - 1e-9)));

        /// <summary>The share of the people aboard who reach the pods, for a roll in [0, 1).</summary>
        public static double EscapeShare(AbandonCause cause, double roll)
        {
            roll = Math.Clamp(roll, 0, 1);
            switch (cause)
            {
                case AbandonCause.Scuttled: return 0.97;
                case AbandonCause.ReactorLost: return 0.2 + 0.25 * roll;
                default: return 0.45 + 0.35 * roll;
            }
        }

        /// <summary>
        /// A hull is going: of the <paramref name="aboard"/> people still on it, the share that reaches the pods
        /// (<see cref="EscapeShare"/>) launches in them from <paramref name="local"/> (battle-local km from
        /// <paramref name="anchor"/> at <paramref name="body"/>, or beside a lane at <paramref name="site"/>); the
        /// rest die with it. Returns the pod cluster, or null if nobody got off. Pods sharing a
        /// <paramref name="mergeKey"/> with a cluster still drifting join it (a battle's ejected fighter pilots).
        /// </summary>
        public EscapePod Launch(string name, Faction owners, double aboard, double complement, AbandonCause cause,
            int body, DVec3 anchor, DVec3 local, LaneSite site, double jd, string mergeKey = null, Settlement haven = null)
        {
            if (owners == null || aboard < 0.5) return null;
            double escaped = Math.Round(aboard * EscapeShare(cause, rng.NextDouble()));
            RecordDeaths(owners, aboard - escaped);
            if (escaped < 1) return null;
            if (mergeKey != null)
            {
                var into = Pods.Find(p => p.Drifting && p.MergeKey == mergeKey);
                if (into != null)
                {
                    into.People += escaped;
                    into.Pods += PodsFor(escaped, complement);
                    return into;
                }
            }
            double z = 2 * rng.NextDouble() - 1, a = 2 * Math.PI * rng.NextDouble(), r = Math.Sqrt(1 - z * z);
            var pod = new EscapePod
            {
                Id = nextId++,
                Name = name,
                Owners = owners,
                People = escaped,
                Pods = PodsFor(escaped, complement),
                Body = body,
                Anchor = anchor,
                Local = local,
                Site = site,
                DriftDir = new DVec3(r * Math.Cos(a), r * Math.Sin(a), z),
                LaunchedJd = jd,
                MergeKey = mergeKey,
                Haven = haven,
            };
            Pods.Add(pod);
            Launched?.Invoke(pod);
            return pod;
        }

        /// <summary>
        /// A Breacher round is left drifting after a fight with <paramref name="marines"/> of <paramref name="owners"/> alive
        /// inside (U.SPC-T519, <see cref="DriftingRounds.CastAdrift"/>): at <paramref name="local"/> (battle-local km from
        /// <paramref name="anchor"/> at <paramref name="body"/>, or beside a lane at <paramref name="site"/>), tumbling on along
        /// <paramref name="heading"/>, with <see cref="DriftingRounds.AirDays"/> of air. <paramref name="enemy"/> is the side the
        /// team was fighting. Returns the drifting round, or null with nobody aboard.
        /// </summary>
        public EscapePod Adrift(string name, Faction owners, double marines, Faction enemy, int body, DVec3 anchor, DVec3 local,
            DVec3 heading, LaneSite site, double jd)
        {
            if (owners == null || marines < 0.5) return null;
            var pod = new EscapePod
            {
                Id = nextId++,
                Name = name,
                Owners = owners,
                People = marines,
                Pods = 1,
                Body = body,
                Anchor = anchor,
                Local = local,
                Site = site,
                DriftDir = heading.LengthSquared > 1e-18 ? heading.Normalized : DVec3.UnitX,
                LaunchedJd = jd,
                LifeSupportDays = DriftingRounds.AirDays,
                Breacher = true,
                Enemy = enemy,
            };
            Pods.Add(pod);
            return pod;
        }

        /// <summary>
        /// <paramref name="ship"/> (of <paramref name="rescuer"/>; null for a ship that isn't named) picks up the
        /// pods. Their own side or a friend brings the crew home (paid <see cref="RescueRewardPerHead"/> by the
        /// owners when <paramref name="payRescuer"/>; the player is paid through the adventure instead); pirates
        /// take them as hostages; an enemy takes them prisoner. Returns what became of them.
        /// </summary>
        public PodState Collect(EscapePod pod, Faction rescuer, Ship ship, Diplomacy diplomacy, double jd, bool payRescuer = true)
        {
            if (pod == null || !pod.Drifting || rescuer == null) return pod?.State ?? PodState.Lost;
            pod.TakenBy = rescuer;
            pod.ResolvedJd = jd;
            bool hostile = rescuer != pod.Owners && (diplomacy?.Hostile(rescuer, pod.Owners) ?? (rescuer.IsPirate || pod.Owners.IsPirate));
            if (!hostile)
            {
                pod.State = PodState.Rescued;
                Return(pod.Owners, pod.People);
                if (payRescuer && rescuer != pod.Owners)
                {
                    double reward = Math.Max(0, Math.Min(pod.People * RescueRewardPerHead, pod.Owners.Treasury));
                    pod.Owners.Treasury -= reward;
                    rescuer.Treasury += reward;
                }
            }
            else if (rescuer.IsPirate)
            {
                pod.State = PodState.Hostage;
                var held = Hold(pod.Owners, rescuer, pod.People, false, ship != null ? HavenOf(ship) : null, jd, pod.Name);
                // Marines salvaged from a Breacher round may be sold to the side they were fighting (U.SPC-T519).
                if (held != null && pod.Breacher)
                {
                    held.Marines = true;
                    held.Buyer = pod.Enemy;
                }
            }
            else
            {
                pod.State = PodState.Prisoner;
                if (pod.Owners.IsPirate)
                {
                    // Raiders in irons are lost to the Banner; they may talk.
                    Lose(pod.Owners, pod.People);
                    RaidersTaken?.Invoke(pod, rescuer, jd);
                }
                else Hold(pod.Owners, rescuer, pod.People, true, null, jd, pod.Name);
            }
            Pods.Remove(pod);
            Resolved?.Invoke(pod, ship);
            return pod.State;
        }

        /// <summary>
        /// Every ship sharing a body (parked there) or a lane with drifting pods may find them over
        /// <paramref name="days"/>; the first that does picks them up (<see cref="Collect"/>). The player's ship
        /// and ships in a fight, adrift, derelict or gone never do.
        /// </summary>
        public void Sweep(IReadOnlyList<Ship> ships, Diplomacy diplomacy, Ship piloted, double jd, double days)
        {
            if (Pods.Count == 0 || days <= 0) return;
            foreach (var pod in Pods.ToArray())
            {
                if (!pod.Drifting) continue;
                foreach (var s in ships)
                {
                    if (s == null || s == piloted || s.Destroyed || s.Derelict || s.GateDeparted || s.Battle != null || s.Faction == null) continue;
                    // A drifting Breacher round is towed in, so only a tow unit can take it (U.SPC-T519).
                    if (pod.Breacher && !DriftingRounds.CanTow(s)) continue;
                    bool here = pod.Site == null
                        ? Fleet.IsAt(s, pod.Body)
                        : s.Phase == ShipPhase.Lane && s.LaneLeg.Lane != null && s.LaneLeg.Lane == pod.Site.Hop.Lane;
                    if (!here) continue;
                    double rate = pod.Breacher ? (s.Faction.IsPirate ? DriftingRounds.PirateFindRate : DriftingRounds.TowFindRate)
                        : s.Faction.IsPirate ? PirateFindRate : s.Spec.IsMilitary ? PatrolFindRate : MerchantFindRate;
                    // On a lane a passer-by has only moments to see them.
                    if (pod.Site != null) rate *= 0.25;
                    if (rng.NextDouble() >= 1 - Math.Exp(-rate * days)) continue;
                    Collect(pod, s.Faction, s, diplomacy, jd);
                    break;
                }
            }
        }

        /// <summary>Pods whose air has run out by <paramref name="jd"/> are lost with everyone aboard.</summary>
        public void Expire(double jd)
        {
            for (int i = Pods.Count - 1; i >= 0; i--)
            {
                var pod = Pods[i];
                if (!pod.Drifting || pod.AirLeftDays(jd) > 0) continue;
                pod.State = PodState.Lost;
                pod.ResolvedJd = jd;
                RecordDeaths(pod.Owners, pod.People);
                Pods.RemoveAt(i);
                Resolved?.Invoke(pod, null);
            }
        }

        // ---------------------------------------------------------------- captives

        /// <summary>Takes people captive: pirates' hostages (ransomed after a while) or an enemy's prisoners of war.</summary>
        public Captives Hold(Faction owners, Faction captors, double people, bool prisoners, Settlement haven, double jd, string from)
        {
            if (owners == null || captors == null || people < 0.5) return null;
            var c = new Captives
            {
                Owners = owners, Captors = captors, People = people, Prisoners = prisoners, Haven = haven,
                SinceJd = jd, RansomJd = jd + 8 + 12 * rng.NextDouble(), From = from,
            };
            Held.Add(c);
            return c;
        }

        /// <summary>People held by <paramref name="captors"/> (or by anyone, for null) who belong to <paramref name="owners"/>.</summary>
        public double HeldFrom(Faction owners, Faction captors = null)
        {
            double n = 0;
            foreach (var c in Held)
                if (c.Owners == owners && (captors == null || c.Captors == captors)) n += c.People;
            return n;
        }

        /// <summary>
        /// A day's dealings over captives: hostages whose haven has fallen are freed; a ransom that falls due is
        /// paid (the owners pay <see cref="RansomPerHead"/> and have them back) or, if the owners cannot pay, the
        /// hostages are pressed into the pirates' crews; prisoners go home once their captors are no longer at
        /// war with their owners.
        /// </summary>
        public void Daily(Diplomacy diplomacy, double jd)
        {
            for (int i = Held.Count - 1; i >= 0; i--)
            {
                var c = Held[i];
                string how = null;
                if (c.Haven != null && c.Haven.Fallen)
                {
                    Return(c.Owners, c.People);
                    how = $"{c.People:0} hostages from the {c.From} are freed when {c.Haven.Name} falls";
                }
                else if (c.Prisoners)
                {
                    if (diplomacy != null && diplomacy.Hostile(c.Owners, c.Captors)) continue;
                    Return(c.Owners, c.People);
                    how = $"The {c.Captors.Name} send {c.People:0} prisoners from the {c.From} home to the {c.Owners.Name}";
                }
                else
                {
                    if (jd < c.RansomJd) continue;
                    double ransom = c.People * RansomPerHead;
                    // Marines salvaged from a Breacher round may go to the side they were fighting, as slaves (U.SPC-T519).
                    var buyer = DriftingRounds.Buyer(c, diplomacy, out double price);
                    if (buyer != null && (c.Owners.Treasury < ransom || rng.NextDouble() < DriftingRounds.SellChance))
                    {
                        buyer.Treasury -= price;
                        c.Captors.Treasury += price;
                        Lose(c.Owners, c.People);
                        how = $"The {c.Captors.Name} sell {c.People:0} marines from the {c.From} to the {buyer.Name} as slaves for {price:N0} cr";
                    }
                    else if (c.Owners.Treasury >= ransom)
                    {
                        c.Owners.Treasury -= ransom;
                        c.Captors.Treasury += ransom;
                        Return(c.Owners, c.People);
                        how = $"The {c.Owners.Name} ransom {c.People:0} hostages from the {c.From} for {ransom:N0} cr";
                    }
                    else
                    {
                        Lose(c.Owners, c.People);
                        if (!double.IsPositiveInfinity(c.Captors.Manpower)) c.Captors.Manpower += c.People;
                        how = $"Nobody pays for {c.People:0} hostages from the {c.From}: the {c.Captors.Name} press them into their crews";
                    }
                }
                Held.RemoveAt(i);
                CaptivesReleased?.Invoke(c, how, jd);
            }
        }

        // ---------------------------------------------------------------- manpower and morale

        /// <summary>Crew died: lost to their faction for good, and a blow to the survivors' morale.</summary>
        public static void RecordDeaths(Faction f, double people)
        {
            if (f == null || people <= 0) return;
            Lose(f, people);
            f.CrewMorale = Math.Clamp(f.CrewMorale - people * MoraleLossPerDeath, MinMorale, MaxMorale);
        }

        /// <summary>Crew gone for good without dying in action (pressed into pirate crews, raiders in irons): no hit to morale.</summary>
        public static void Lose(Faction f, double people)
        {
            if (f != null && people > 0) f.CrewLost += people;
        }

        /// <summary>Crew come home (rescued from pods, ransomed, exchanged): back in the pool, and a lift to morale.</summary>
        public static void Return(Faction f, double people)
        {
            if (f == null || people <= 0) return;
            f.CrewRescued += people;
            if (!double.IsPositiveInfinity(f.Manpower)) f.Manpower += people;
            f.CrewMorale = Math.Clamp(f.CrewMorale + people * MoraleGainPerRescue, MinMorale, MaxMorale);
        }

        /// <summary>People a hull of <paramref name="cls"/> needs aboard: its crew and marines.</summary>
        public static double Complement(ShipClass cls)
        {
            var spec = CombatSpec.Of(cls);
            return spec.Crew + spec.Marines;
        }

        /// <summary>Whether <paramref name="f"/> has the hands to crew a new hull of <paramref name="cls"/>.</summary>
        public static bool CanCrew(Faction f, ShipClass cls) => f != null && f.Manpower >= Complement(cls);

        /// <summary>Draws up to <paramref name="people"/> from <paramref name="f"/>'s pool; returns how many it had.</summary>
        public static double Draft(Faction f, double people)
        {
            if (f == null || people <= 0) return 0;
            double got = Math.Min(people, Math.Max(0, f.Manpower));
            if (!double.IsPositiveInfinity(f.Manpower)) f.Manpower -= got;
            return got;
        }

        /// <summary>The reserve a faction keeps for these settlements' people (pirates keep their own).</summary>
        public static double ReserveFor(Faction f, IReadOnlyList<Settlement> settlements)
        {
            if (f.IsPirate) return PirateReserve;
            double people = 0;
            foreach (var s in settlements)
                if (s.Owner == f && !s.Fallen) people += s.Population;
            return people * ReserveShare;
        }

        /// <summary>Gives every faction its starting reserve of trained spacers (see <see cref="ReserveFor"/>).</summary>
        public static void MusterReserves(IReadOnlyList<Faction> factions, IReadOnlyList<Settlement> settlements)
        {
            foreach (var f in factions) f.Manpower = ReserveFor(f, settlements);
        }

        /// <summary>
        /// <paramref name="days"/> of recruiting (each faction from its own settlements' people, the pirates from
        /// whoever will sign, while a haven stands), up to its reserve; and morale recovering toward its baseline.
        /// </summary>
        public static void Recruit(IReadOnlyList<Faction> factions, IReadOnlyList<Settlement> settlements, double days)
        {
            if (days <= 0) return;
            foreach (var f in factions)
            {
                f.CrewMorale += (1 - f.CrewMorale) * (1 - Math.Pow(1 - MoraleRecoveryPerDay, days));
                if (double.IsPositiveInfinity(f.Manpower)) continue;
                double cap = ReserveFor(f, settlements), intake;
                if (f.IsPirate)
                {
                    bool haven = false;
                    foreach (var s in settlements)
                        if (s.Owner == f && s.Kind == SettlementKind.Haven && !s.Fallen) haven = true;
                    intake = haven ? PirateRecruitPerDay * days : 0;
                }
                else
                {
                    double people = 0;
                    foreach (var s in settlements)
                        if (s.Owner == f && !s.Fallen) people += s.Population;
                    intake = people * RecruitPerPersonPerDay * days;
                }
                if (f.Manpower < cap) f.Manpower = Math.Min(cap, f.Manpower + intake);
            }
        }
    }
}
