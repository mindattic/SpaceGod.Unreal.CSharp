using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>What became of an outlaw captain.</summary>
    public enum OutlawFate
    {
        /// <summary>Still out there, still raiding.</summary>
        AtLarge,

        /// <summary>Died with their flagship.</summary>
        Killed,

        /// <summary>Taken alive with their flagship (boarded, or it struck its colours to boarders).</summary>
        Captured,

        /// <summary>Gone without a fight anyone saw: the hull lost outside a battle (a collision, a scuttling).</summary>
        Vanished,

        /// <summary>Taken alive and locked in a captor's brig, not yet handed over (U.SPC-T63): delivered to a lawful
        /// port of a faction that wants them it pays the full bounty; ransomed back, or breaking out, they go free.</summary>
        Held,
    }

    /// <summary>Where a wanted party was last seen: the body (or -1 when nobody can say which), when, by whom, in words.</summary>
    public sealed class Sighting
    {
        public int Body = -1;
        public double Jd;
        public string By;

        /// <summary>"at Mars", "bound for Ceres", "on the Earth-Mars lane", "somewhere in the Belt".</summary>
        public string Where;

        public double DaysAgo(double jd) => Math.Max(0, jd - Jd);
    }

    /// <summary>
    /// A named pirate captain the law has on its books for as long as they live: the flagship they command, the
    /// haven they sail from and a criminal record built from what their band actually did in the sim -- ships
    /// robbed, ships and platforms destroyed, cargoes ransomed -- on top of the crimes the law recorded
    /// (<see cref="LawRecord"/>). One who survives long enough at large becomes a feared name.
    /// </summary>
    public sealed class Outlaw
    {
        public readonly LawRecord Record;

        /// <summary>The flagship they command (the group leader the law's record is keyed by): the last one, while
        /// they are adrift in its pods, ashore or held.</summary>
        public Ship Ship { get; internal set; }

        /// <summary>The haven they sail from, or null.</summary>
        public Settlement Haven { get; internal set; }

        /// <summary>The escape pods they got away in, while those drift (see <see cref="EscapePod.Captain"/>), or null.</summary>
        public EscapePod Pod;

        /// <summary>Free again (ransomed, broken out of a brig, picked up by their own) and back with the raiders,
        /// waiting for a new band to command (see <see cref="Outlaws.Sync"/>).</summary>
        public bool Ashore;

        /// <summary>Ashore only because the flagship is abroad (SettleFates): it clears when the hull comes home.</summary>
        public bool FlagshipAway;

        /// <summary>The ship whose brig holds them, while <see cref="OutlawFate.Held"/>.</summary>
        public Ship HeldBy;

        /// <summary>When the law first had them on its books (their band sailed, or they took it over).</summary>
        public readonly double SinceJd;

        /// <summary>What the record is built from: lawful hulls robbed (ransomed, boarded and taken), hulls and
        /// platforms destroyed, cargoes ransomed, fights fought, and recorded crimes (all of them; the record's
        /// own list keeps only the latest).</summary>
        public int ShipsRobbed, Kills, Ransoms, Battles, CrimeCount;

        public OutlawFate Fate = OutlawFate.AtLarge;
        public double FateJd;

        /// <summary>Who ended their career ("the bounty hunter MV Nemesis", "the Sol Federation"), or null.</summary>
        public string FateBy;

        /// <summary>A feared name: survived <see cref="Outlaws.FearedDays"/> at large with a long record.</summary>
        public bool Feared;
        public double FearedJd;

        public Outlaw(LawRecord record, Ship ship, Settlement haven, double sinceJd)
        {
            Record = record;
            Ship = ship;
            Haven = haven;
            SinceJd = sinceJd;
        }

        public string Name => Record.Name;

        /// <summary>The name as the boards and the news give it: "the feared Captain ..." once it is.</summary>
        public string Title => Feared ? "the feared " + Name : Name;

        public bool AtLarge => Fate == OutlawFate.AtLarge;

        /// <summary>At large and in command of their flagship (not adrift in its pods, nor ashore waiting for a band).</summary>
        public bool Aboard => AtLarge && Pod == null && !Ashore;

        public double DaysAtLarge(double jd) => Math.Max(0, (AtLarge ? jd : FateJd) - SinceJd);

        /// <summary>The record in a line: "3 ships robbed, 2 destroyed, 1 cargo ransomed".</summary>
        public string Deeds()
        {
            var parts = new List<string>();
            if (ShipsRobbed > 0) parts.Add(ShipsRobbed + (ShipsRobbed == 1 ? " ship" : " ships") + " robbed");
            if (Kills > 0) parts.Add(Kills + " destroyed");
            if (Ransoms > 0) parts.Add(Ransoms + (Ransoms == 1 ? " cargo" : " cargoes") + " ransomed");
            if (parts.Count == 0) parts.Add(CrimeCount > 0 ? CrimeCount + (CrimeCount == 1 ? " crime" : " crimes") + " on record" : "no crimes yet on record");
            return string.Join(", ", parts);
        }

        public override string ToString() => Title;
    }

    /// <summary>A bounty hunter's group (a flagship and its escort) and what it has earned.</summary>
    public sealed class BountyHunter
    {
        public readonly Ship Ship;
        public readonly double SinceJd;

        /// <summary>The contract it has taken, or null.</summary>
        public BountyContract Contract;

        /// <summary>Bounties it has collected, credits, and how many.</summary>
        public double Collected;
        public int Takes;

        readonly World world;

        public BountyHunter(Ship ship, double sinceJd, World world)
        {
            Ship = ship;
            Faction = ship.Faction;
            SinceJd = sinceJd;
            this.world = world;
        }

        public string Name => Ship.Name;

        /// <summary>Handed on to another flagship (its own lost, or taken over by the player).</summary>
        public bool Retired;

        /// <summary>Away on a hunt voyage beyond the gates (U.SPC-T585): its row is kept even if the hull is lost abroad,
        /// until the heir comes home and <see cref="Outlaws.OnSuccession"/> hands the row on.</summary>
        public bool Away;

        /// <summary>
        /// Still hunting: not handed on, lost, cast adrift or taken (flying other colours). A flagship through a
        /// gate or under its own cruise drive is the same live hull abroad (U.SPC-T453), never <see cref="Ship.Destroyed"/>
        /// or <see cref="Ship.GateDeparted"/> for it, so this also checks it is still this world's own fleet's hull to
        /// read a position from (<see cref="Fleet.IsPresent"/>) before anything here tries to.
        /// </summary>
        public bool Alive => !Retired && !Ship.Destroyed && !Ship.Derelict && !Ship.GateDeparted && Ship.Faction == Faction && world.Fleet.IsPresent(Ship);

        /// <summary>The colours it was commissioned under (the free traders').</summary>
        public readonly Faction Faction;
    }

    /// <summary>A contract a hunter has taken from the boards: the party it is after, and how long it keeps at it.</summary>
    public sealed class BountyContract
    {
        public LawRecord Target;
        public double TakenJd, UntilJd;
    }

    /// <summary>A kill the player made of a wanted outlaw: the dead rate is paid once they scan the wreck (U.SPC-T63).</summary>
    public sealed class KillClaim
    {
        public Outlaw Outlaw;

        /// <summary>The flagship's wreck, to be scanned for proof (null: there is none, and the claim lapses).</summary>
        public Wreck Wreck;

        public double Jd;
        public string Battle;
    }

    /// <summary>One line on a station's bounty board.</summary>
    public sealed class BountyPosting
    {
        public LawRecord Target;

        /// <summary>The outlaw it is on, or null (the player, or a lawful captain wanted for an assault).</summary>
        public Outlaw Outlaw;

        /// <summary>Who posts it here, and how much; and the price on the target's head across every board.</summary>
        public Faction PostedBy;
        public double Bounty, TotalBounty;

        /// <summary>Where the target was last seen, or null if nobody has.</summary>
        public Sighting LastSeen;

        /// <summary>The hunter that has taken a contract on the target, or null.</summary>
        public BountyHunter HuntedBy;

        public bool IsPlayer => Target.IsPlayer;
    }

    /// <summary>
    /// Named outlaws, the bounty boards and the hunters who work them (U.SPC-T62). Every raider band's captain is
    /// on the law's books from the day the band sails: a name, a flagship and a home haven, and a criminal record
    /// built from what the band actually does -- every lawful hull it robs, destroys or ransoms raises the price on
    /// the captain's head on top of the crime itself (<see cref="Law.Commit"/>). Each lawful port's bounty board
    /// lists everyone its owner has posted a bounty on, with where they were last seen (by a witness: a ship or
    /// station in sensor range; never a haven nobody has found). Bounty hunters -- independent warships the
    /// free traders commission while there is money on the boards -- take contracts on outlaws they can match
    /// (and on the player, when wanted), fly to the target's last-known position, fight it there and collect every
    /// bounty on it. A captain who survives <see cref="FearedDays"/> at large with a long record becomes a feared
    /// name: their bounties rise by half, and crews facing them strike their colours sooner.
    /// </summary>
    public sealed class Outlaws
    {
        readonly World world;
        readonly Random rng;

        /// <summary>Every outlaw ever on the books, oldest first (the dead and taken too: a persistent rogues' gallery).</summary>
        public readonly List<Outlaw> All = new List<Outlaw>();

        /// <summary>Every hunter group commissioned, oldest first.</summary>
        public readonly List<BountyHunter> Hunters = new List<BountyHunter>();

        /// <summary>Where each wanted party (the player included) was last seen.</summary>
        public readonly Dictionary<LawRecord, Sighting> LastSeen = new Dictionary<LawRecord, Sighting>();

        readonly Dictionary<Ship, Outlaw> byShip = new Dictionary<Ship, Outlaw>();
        readonly Dictionary<LawRecord, Outlaw> byRecord = new Dictionary<LawRecord, Outlaw>();
        readonly Dictionary<Ship, BountyHunter> byHunter = new Dictionary<Ship, BountyHunter>();

        // ---------------------------------------------------------------- tuning

        /// <summary>What each deed adds to the bounty a recorded crime posts, credits: a lawful hull or platform
        /// destroyed, a hull robbed (ransomed or taken), and a cargo ransomed on top of that.</summary>
        public const double KillBounty = 40000, RobbedBounty = 30000, RansomBounty = 20000;

        /// <summary>Days at large, and the record (recorded crimes, or kills), that make a feared name.</summary>
        public const double FearedDays = 120;
        public const int FearedCrimes = 4, FearedKills = 6;

        /// <summary>How a feared name's bounties grow (once when it becomes feared, and on every crime after).</summary>
        public const double FearedBountyFactor = 1.5;

        /// <summary>How much of their resolve crews facing a feared name keep (see <see cref="Battle.Dread"/>).</summary>
        public const double FearedResolve = 0.7;

        /// <summary>The smallest price on a head a hunter takes a contract for, credits.</summary>
        public const double MinContract = 100000;

        /// <summary>How long a hunter keeps at a contract, days, and how stale a sighting it will still follow.</summary>
        public const double ContractDays = 60, StaleSightingDays = 30;

        /// <summary>Of its target's strength a hunter group must match to take a contract on it (all of it for a feared name).</summary>
        public const double MatchShare = 0.7;

        /// <summary>How close a hunter comes to the player's wanted ship before it moves on it, km.</summary>
        public const double EngageKm = 250000;

        /// <summary>Chance a day the free traders commission a hunter while a bounty goes unhunted.</summary>
        public const double CommissionChance = 0.05;

        /// <summary>What the free traders keep in their treasury whatever the boards offer: a hunter is fitted out only from above it, credits.</summary>
        public const double GuildReserve = 3e8;

        /// <summary>A hunter this hurt goes to a yard first.</summary>
        public const double RepairDamage = 0.3;

        static readonly string[] HunterNames =
        {
            "Nemesis", "Writ of Seizure", "Long Arm", "Due Process", "Warrant", "Reckoning", "Bail Bond", "Quarry",
            "Manhunter", "Summons", "Last Word", "Price on Your Head",
        };

        double nextDailyJd = double.NaN;
        int hunterNames;

        /// <summary>The player's ship as last flown: where it is (for sightings and hunters), while it is.</summary>
        int pilotBody = -1;
        DVec3 pilotHelio;
        double pilotJd = double.NaN;

        public Outlaws(World world, int seed)
        {
            this.world = world;
            rng = new Random(seed);
        }

        Law Law => world.Law;

        /// <summary>Who pays and crews the hunters: the system's free traders.</summary>
        Faction Guild => world.Independents;

        // ---------------------------------------------------------------- lookups

        /// <summary>The outlaw commanding <paramref name="ship"/> (its group leader), or null.</summary>
        public Outlaw Of(Ship ship) => ship != null && byShip.TryGetValue(ship.Controller, out var o) ? o : null;

        /// <summary>The outlaw a record belongs to, or null.</summary>
        public Outlaw Of(LawRecord r) => r != null && byRecord.TryGetValue(r, out var o) ? o : null;

        /// <summary>The hunter group <paramref name="ship"/> flies in (it leads it or escorts its leader), or null.
        /// A hunter taken as a prize (pressed into pirate service, say) is a hunter no more.</summary>
        public BountyHunter HunterOf(Ship ship) =>
            ship != null && byHunter.TryGetValue(ship.Controller, out var h) && !h.Retired && h.Ship.Faction == Guild ? h : null;

        /// <summary>Whether <paramref name="ship"/> leads or flies in a hunter group.</summary>
        public bool IsHunter(Ship ship) => HunterOf(ship) != null;

        public Sighting SeenLast(LawRecord r) => r != null && LastSeen.TryGetValue(r, out var s) ? s : null;

        // ---------------------------------------------------------------- the clock

        /// <summary>Once a day: new captains on the books, fates settled, sightings, feared names, hunters' contracts and commissions.</summary>
        public void Update(double jd)
        {
            if (double.IsNaN(nextDailyJd))
            {
                Sync(jd);
                nextDailyJd = jd + 1;
                return;
            }
            while (jd >= nextDailyJd)
            {
                Daily(nextDailyJd);
                nextDailyJd += 1;
            }
        }

        void Daily(double jd)
        {
            Sync(jd);
            SettleFates(jd);
            Sightings(jd);
            Fear(jd);
            ReviewContracts(jd);
            Commission(jd);
            RepairHunters();
            SettleCaptives(jd);
            LapseClaims(jd);
        }

        /// <summary>Every raider band's captain goes on the books: its flagship, its haven, its record.</summary>
        public void Sync(double jd)
        {
            if (world.War == null || Law == null) return;
            foreach (var s in world.Fleet.Ships)
            {
                if (s.Destroyed || s.IsEscort || s.Derelict || s.GateDeparted || s.Faction == null || !s.Faction.IsPirate || s == world.PilotedShip) continue;
                if (byShip.ContainsKey(s)) continue;
                // A captain back with the raiders takes the next band that sails -- not one whose own captain the law
                // already has on its books for what the band has done (that captain is the one wanted for it).
                var ashore = All.Find(x => x.AtLarge && x.Ashore);
                var own = Law.FindRecord(s);
                bool known = own != null && (own.Crimes.Count > 0 || own.Wanted || own.Notoriety > 0);
                if (ashore != null && !known)
                {
                    Rebind(ashore, s, jd);
                    continue;
                }
                var o = new Outlaw(Law.RecordOf(s), s, world.War.HomeHaven(s), jd);
                All.Add(o);
                byShip[s] = o;
                byRecord[o.Record] = o;
            }
        }

        /// <summary>Outlaws whose flagship is gone outside any battle (lost in a collision, scuttled) or flies under other colours.
        /// A flagship through a gate or under its own cruise drive (a raid, a move, a smuggling run) is not gone: it is the
        /// same hull carrying on abroad (U.SPC-T453), so <see cref="Ship.Destroyed"/> and <see cref="Ship.GateDeparted"/> stay
        /// false on it; it is simply no longer in this world's own fleet (<see cref="Fleet.IsPresent"/>), which (a hull some
        /// other code marks both ways instead, a scrapping say) reads the same way -- the captain is merely away, ashore
        /// until a band sails again (<see cref="Sync"/>), with their record and bounties intact (U.SPC-T434).</summary>
        void SettleFates(double jd)
        {
            foreach (var o in All)
            {
                if (o.AtLarge && o.Ashore && o.FlagshipAway && o.Ship != null && !o.Ship.GateDeparted && !o.Ship.Destroyed && !o.Ship.Derelict
                    && o.Ship.Faction != null && o.Ship.Faction.IsPirate && world.Fleet.IsPresent(o.Ship)
                    && byShip.TryGetValue(o.Ship, out var back) && back == o)
                {
                    o.Ashore = false;
                    o.FlagshipAway = false;
                }
                if (!o.Aboard) continue;
                if (o.Ship.Faction == null || !o.Ship.Faction.IsPirate) End(o, OutlawFate.Captured, jd, o.Ship.Faction?.Name);
                else if (o.Ship.GateDeparted || !world.Fleet.IsPresent(o.Ship)) { o.Ashore = true; o.FlagshipAway = true; }
                else if (o.Ship.Destroyed || o.Ship.Derelict) End(o, OutlawFate.Vanished, jd, null);
            }
        }

        void End(Outlaw o, OutlawFate fate, double jd, string by)
        {
            if (!o.AtLarge && o.Fate != OutlawFate.Held) return;
            Release(o);
            o.Pod = null;
            o.Ashore = false;
            o.Fate = fate;
            o.FateJd = jd;
            o.FateBy = by;
            string what = fate == OutlawFate.Killed ? "is dead" : fate == OutlawFate.Captured ? "is taken alive" : "has vanished";
            world.Log(jd, $"{Cap(o.Title)} {what}" + (by != null ? " (" + by + ")" : "") + $" after {o.DaysAtLarge(jd):0} days at large: {o.Deeds()}",
                WorldEventKind.Law, -1);
        }

        // ---------------------------------------------------------------- sightings

        /// <summary>
        /// Every outlaw at large (and the player's wanted ship) that a lawful ship or station can see is sighted: the
        /// boards and the hunters learn where. Over a haven nobody has found, the sighting names no rock.
        /// </summary>
        void Sightings(double jd)
        {
            foreach (var o in All)
            {
                if (!o.Aboard || o.Ship.Battle != null) continue;
                var pos = world.Fleet.StateOf(o.Ship, jd).HelioKm;
                // Seen by what the hull itself gives off (a raider lying dark is hard to make out), not a battle's glare.
                string witness = world.Sensors != null ? Law.FindWitness(pos, jd, null, world.Sensors.SignatureOf(o.Ship, jd)) : Law.FindWitness(pos, jd);
                if (witness == null) continue;
                // A flagship merged into another band goes where that band's leader goes.
                Sight(o.Record, Locate(o.Ship.Controller), jd, witness);
            }
            var p = Law.Player;
            if (p.Wanted && world.PilotedShip != null && pilotBody >= 0 && !double.IsNaN(pilotJd) && jd - pilotJd < 1)
            {
                double sig = world.Sensors != null && world.Sensors.PilotSignature > 0 ? world.Sensors.PilotSignature : Signatures.Battle;
                string witness = Law.FindWitness(pilotHelio, jd, null, sig);
                if (witness != null) Sight(p, Hide(pilotBody, "near " + BodyName(pilotBody)), jd, witness);
            }
        }

        /// <summary>Where a ship is, as a board would put it: the body it is at or bound for, or the lane it rides.</summary>
        (int body, string where) Locate(Ship s)
        {
            int body;
            string where;
            switch (s.Phase)
            {
                case ShipPhase.Transit:
                    body = s.DestinationBody;
                    where = "bound for " + BodyName(body);
                    break;
                case ShipPhase.Lane:
                case ShipPhase.Lurk:
                case ShipPhase.Adrift:
                    body = s.LaneLeg.From;
                    where = (s.Phase == ShipPhase.Lane ? "riding the " : s.Stalled ? "stalled off the " : "lurking off the ") + s.LaneLeg.Lane.Name + " lane";
                    break;
                default:
                    body = s.Orbit?.Body ?? -1;
                    where = "at " + BodyName(body);
                    break;
            }
            return Hide(body, where);
        }

        /// <summary>A sighting at a haven nobody has found names no rock.</summary>
        (int body, string where) Hide(int body, string where)
        {
            if (body < 0 || body >= world.Catalog.Count) return (-1, where);
            foreach (var st in world.Economy.At(body))
                if (!st.Discovered)
                {
                    var rock = world.Catalog[body];
                    return (-1, rock.TrojanPoint != LagrangePoint.None ? "somewhere in the Trojans"
                        : rock.Kind == BodyKind.Asteroid ? "somewhere in the Belt" : "somewhere in the dark");
                }
            return (body, where);
        }

        void Sight(LawRecord r, (int body, string where) at, double jd, string by)
        {
            if (!LastSeen.TryGetValue(r, out var s)) LastSeen[r] = s = new Sighting();
            s.Body = at.body;
            s.Where = at.where;
            s.Jd = jd;
            s.By = by;
        }

        string BodyName(int body) => body >= 0 && body < world.Catalog.Count ? world.Catalog[body].Name : "deep space";

        // ---------------------------------------------------------------- crimes and battles

        /// <summary>
        /// The law has booked a crime (<see cref="Law.Committed"/>): a recorded one goes on an outlaw's record, places
        /// the offender where it was done, and costs a feared name half again its bounty.
        /// </summary>
        public void OnCrime(Crime c)
        {
            if (c == null || !c.Recorded) return;
            var o = Of(c.Offender);
            if (o != null)
            {
                o.CrimeCount++;
                if (o.Feared) AddBounty(c.Offender, c, c.Bounty * (FearedBountyFactor - 1));
            }
            var (body, where) = Hide(c.Body, c.Body >= 0 ? "near " + BodyName(c.Body) : c.Detail != null ? "at the " + c.Detail : "somewhere unseen");
            Sight(c.Offender, (body, where), c.Jd, c.Witness);
        }

        static void AddBounty(LawRecord r, Crime c, double extra)
        {
            if (extra <= 0 || c.PostedBy == null) return;
            r.Bounty[c.PostedBy] = r.BountyBy(c.PostedBy) + extra;
            r.BountyUntil[c.PostedBy] = c.Jd + Law.BountyDays;
            c.Bounty += extra;
        }

        /// <summary>
        /// Before a battle starts in earnest: crews facing a feared name lose some of their nerve
        /// (<see cref="Battle.Dread"/>).
        /// </summary>
        public void OnBattleStarted(Battle b)
        {
            if (b == null) return;
            for (int side = 0; side < 2; side++)
            {
                foreach (var u in b.Units)
                {
                    if (u.Side != 1 - side || u.Ship == null) continue;
                    var o = Of(u.Ship);
                    if (o != null && o.AtLarge && o.Feared)
                    {
                        b.Dread[side] = FearedResolve;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// A battle has ended and the law has judged it (<see cref="Law.OnBattleEnded"/>): each outlaw who fought in it
        /// adds what their band did to lawful ships to their record -- hulls robbed, destroyed, cargoes ransomed -- and,
        /// where the law recorded the crime, each deed raises the bounty it posted. An outlaw whose flagship died or
        /// was taken is finished. A fight on a lane places them by its near end.
        /// </summary>
        public void OnBattleEnded(Battle b, double jd)
        {
            if (b == null || b.Units.Count == 0) return;
            var seen = new HashSet<Outlaw>();
            foreach (var u in b.Units)
            {
                if (u.Ship == null || u.Side < 0 || u.Side > 1) continue;
                var o = Of(u.Ship);
                // (One taken alive in this very fight is in a brig already: what the band did still counts.)
                if (o == null || !(o.AtLarge || (o.Fate == OutlawFate.Held && o.FateJd == jd)) || !seen.Add(o)) continue;
                // Sides as fought: a hull taken in the fight has moved to its captor's side since.
                int side = u.FoughtOn;
                int kills = 0, robbed = 0, ransoms = 0;
                foreach (var v in b.Units)
                {
                    if (v.FoughtOn != 1 - side || v.Kind == UnitKind.Fighter || v.Faction == null || v.Faction.IsPirate) continue;
                    if (v.Dead) kills++;
                    else if (v.Captured || v.SurrenderOutcome == SurrenderTerms.TakeShip) robbed++;
                    else if (v.SurrenderOutcome == SurrenderTerms.RansomCargo)
                    {
                        robbed++;
                        ransoms++;
                    }
                }
                o.Battles++;
                o.Kills += kills;
                o.ShipsRobbed += robbed;
                o.Ransoms += ransoms;
                // The crime the law booked for this fight, if anyone saw it: the deeds go on its bounty.
                var rec = o.Record;
                Crime crime = rec.Crimes.Count > 0 ? rec.Crimes[rec.Crimes.Count - 1] : null;
                // Unless the law has already settled every price on them in this fight (paid a hunter, or let them lapse on
                // a captain dead with the ship): a captain still wanted -- at large, in the pods or in a brig -- answers for it.
                if (crime != null && crime.Jd == jd && crime.Detail == b.Name && crime.Recorded && rec.Wanted)
                {
                    double extra = kills * KillBounty + robbed * RobbedBounty + ransoms * RansomBounty;
                    if (o.Feared) extra *= FearedBountyFactor;
                    if (extra > 0)
                    {
                        AddBounty(rec, crime, extra);
                        world.Log(jd, $"{crime.PostedBy.Name} raises the price on {o.Title} to {Cr(rec.BountyBy(crime.PostedBy))} cr: {Deeds(kills, robbed, ransoms)} at {b.Name}",
                            WorldEventKind.Law, -1);
                    }
                    if (b.Site != null) Sight(rec, Hide(b.Site.Hop.From, "on the " + b.Site.Hop.Lane.Name + " lane"), jd, crime.Witness);
                }
            }
            // Outlaws whose flagship went down or was taken in it are finished -- unless they got away in its pods,
            // or were taken alive into a brig.
            foreach (var u in b.Units)
            {
                if (u.Ship == null || !(u.Dead || u.Captured)) continue;
                if (!byShip.TryGetValue(u.Ship, out var o) || !o.AtLarge || o.Ship != u.Ship) continue;
                if (u.Dead && o.Pod != null) continue;
                if (u.Captured && Seize(b, u, o, jd)) continue;
                End(o, u.Dead ? OutlawFate.Killed : OutlawFate.Captured, jd, Victor(b, 1 - u.FoughtOn));
            }
            // Captains in the brigs of ships that fought here: a breached brig lets them go.
            foreach (var o in All.ToArray())
                if (o.Fate == OutlawFate.Held && o.HeldBy != null && b.UnitOf(o.HeldBy) is BattleUnit hu) Breach(o, hu, jd);
        }

        static string Deeds(int kills, int robbed, int ransoms)
        {
            var parts = new List<string>();
            if (kills > 0) parts.Add(kills + (kills == 1 ? " ship destroyed" : " ships destroyed"));
            if (robbed > 0) parts.Add(robbed + (robbed == 1 ? " ship robbed" : " ships robbed"));
            if (ransoms > 0) parts.Add(ransoms + (ransoms == 1 ? " cargo ransomed" : " cargoes ransomed"));
            return string.Join(", ", parts);
        }

        /// <summary>Who won a fight on a side, as the news gives it: a hunter by name, else the side's faction.</summary>
        string Victor(Battle b, int side)
        {
            foreach (var u in b.Units)
                if (u.Side == side && u.Ship != null && IsHunter(u.Ship)) return "the bounty hunter " + HunterOf(u.Ship).Name;
            return side >= 0 && side < 2 ? b.SideLeaders[side]?.Name : null;
        }

        /// <summary>
        /// A wanted party (<paramref name="rec"/>, commanding <paramref name="u"/>) has been killed or taken in a battle:
        /// if bounty hunters fought on the other side, they collect every bounty on it -- in full taken, at
        /// <see cref="DeadShare"/> dead (their scanners on the wreck where it died are the proof) -- each poster paying
        /// its own out of its treasury. True if they did (the bounties are then cleared and the law has nothing more to do).
        /// </summary>
        public bool ClaimByHunter(Battle b, BattleUnit u, LawRecord rec, double jd)
        {
            if (b == null || u == null || rec == null || !rec.Wanted) return false;
            BountyHunter hunter = null;
            foreach (var o in b.Units)
                // Only a hunter still there to scan the wreck or take the prize collects: one lost in the fight claims nothing.
                if (o.Side == 1 - u.FoughtOn && !o.Dead && !o.Captured && o.Ship != null && HunterOf(o.Ship) is BountyHunter h) hunter = h;
            if (hunter == null) return false;
            double total = Pay(rec, u.Dead ? DeadShare : 1, hunter.Ship);
            var outlaw = Of(rec);
            string name = outlaw != null ? outlaw.Title : rec.IsPlayer ? "the " + u.Ship?.Name : rec.Name;
            world.Log(jd, $"The bounty hunter {hunter.Name} collects {Cr(total)} cr on {name}, {(u.Dead ? "dead (the wreck scanned for proof)" : "taken")} at {b.Name}", WorldEventKind.Law, -1);
            if (rec.IsPlayer) Law.Notice($"The bounty hunter {hunter.Name} has collected the {Cr(total)} cr on your head");
            return true;
        }

        // ---------------------------------------------------------------- alive or dead (U.SPC-T63)

        /// <summary>The share of the price on a head paid for the captain dead, on proof of kill (a scanned wreck); alive pays it all.</summary>
        public const double DeadShare = 0.5;

        /// <summary>Chance a captain reaches the pods as their flagship goes: shot apart over minutes, or when the reactor goes.</summary>
        public const double EjectChance = 0.8, EjectChanceReactor = 0.35;

        /// <summary>A holding hull beaten below this share of its hull has its brig breached: the captain gets away.</summary>
        public const double BrigBreachHull = 0.25;

        /// <summary>What the raiders pay to have a captain back: this share of the price on their head, and at least <see cref="MinRansom"/> credits.</summary>
        public const double RansomShare = 0.75, MinRansom = 50000;

        /// <summary>How close the player's ship must come to a wreck to scan it for proof of kill, km.</summary>
        public const double ProofScanKm = 2000;

        /// <summary>Kills the player has made that wait on a scan of the wreck before they pay.</summary>
        public readonly List<KillClaim> Claims = new List<KillClaim>();

        /// <summary>What the price on <paramref name="r"/>'s head pays dead.</summary>
        public static double DeadRate(LawRecord r) => r == null ? 0 : r.TotalBounty * DeadShare;

        /// <summary>What the raiders pay to have <paramref name="o"/> back (see <see cref="RansomShare"/>).</summary>
        public static double RansomFor(Outlaw o) => o == null ? 0 : Math.Max(MinRansom, RansomShare * o.Record.TotalBounty);

        /// <summary>The captains locked in <paramref name="ship"/>'s brig.</summary>
        public List<Outlaw> HeldAboard(Ship ship)
        {
            var list = new List<Outlaw>();
            if (ship != null)
                foreach (var o in All)
                    if (o.Fate == OutlawFate.Held && o.HeldBy == ship) list.Add(o);
            return list;
        }

        /// <summary>The player's kills of <paramref name="o"/> waiting on a scan of the wreck, or null.</summary>
        public KillClaim ClaimOn(Outlaw o) => Claims.Find(c => c.Outlaw == o);

        /// <summary>
        /// The law's side of a battle's end for a wanted party (see <see cref="Law.OnBattleEnded"/>): alive or dead. The
        /// player or a lawful captain wanted for an assault is collected on by hunters (<see cref="ClaimByHunter"/>). An
        /// outlaw's flagship taken: the captain goes into a captor's brig, the bounty paid in full when they are handed
        /// over (<see cref="Deliver"/>). Destroyed: a captain who got away in the pods is still wanted; one who died with
        /// it pays hunters there the dead rate, or the player once they scan the wreck (<see cref="ProveKills"/>). True
        /// if it was settled here and the law has nothing more to do.
        /// </summary>
        public bool ClaimInBattle(Battle b, BattleUnit u, LawRecord rec, double jd)
        {
            if (b == null || u == null || rec == null) return false;
            var o = Of(rec);
            if (o == null || o.Ship != u.Ship || !o.AtLarge) return ClaimByHunter(b, u, rec, jd);
            if (u.Captured) return Seize(b, u, o, jd) || ClaimByHunter(b, u, rec, jd);
            // A captain ashore is not aboard the hull that died: nothing of theirs is lost with it (U.SPC-T579).
            if (o.Ashore) return u.Dead;
            if (!u.Dead) return false;
            if (o.Pod != null) return true;
            if (ClaimByHunter(b, u, rec, jd)) return true;
            if (!rec.Wanted || !PlayerFoughtAgainst(b, u)) return false;
            var wreck = world.War?.Wrecks.Of(u.Ship);
            Claims.Add(new KillClaim { Outlaw = o, Wreck = wreck, Jd = jd, Battle = b.Name });
            Law.Notice($"{Cap(o.Title)} died with the {u.Ship.Name}. Scan the wreck to prove the kill for {Cr(DeadRate(rec))} cr (alive would have paid {Cr(rec.TotalBounty)})");
            return true;
        }

        bool PlayerFoughtAgainst(Battle b, BattleUnit u)
        {
            foreach (var v in b.Units)
                if (v.PlayerFlown && v.Side == 1 - u.FoughtOn && !v.Dead && !v.Captured && v.Ship != null && v.Ship == world.PilotedShip) return true;
            return false;
        }

        /// <summary>
        /// An outlaw's flagship has been taken by boarders: the captain is locked in the brig of a ship on the captors'
        /// side with a berth free (the player's first, then a hunter's, then anyone's). False if none has room.
        /// </summary>
        bool Seize(Battle b, BattleUnit u, Outlaw o, double jd)
        {
            if (o.Fate == OutlawFate.Held) return true;
            var captor = u.Combat?.Interior.Boarder;
            Ship best = null;
            int bestRank = int.MaxValue;
            foreach (var v in b.Units)
            {
                if (v.Side != 1 - u.FoughtOn || v.Ship == null || v.Dead || v.Captured || v.Ship == u.Ship || v.Ship.Destroyed) continue;
                if (v.Faction == null || v.Faction.IsPirate || (captor != null && v.Faction != captor)) continue;
                // A brig already holed (see Breach) holds nobody: the captain would be out of it in the same breath.
                if (Outfitting.BrigRoom(v.Ship) < 1 || (v.Combat != null && v.Combat.HullFraction < BrigBreachHull)) continue;
                int rank = v.Ship == world.PilotedShip ? 0 : IsHunter(v.Ship) ? 1 : 2;
                if (rank < bestRank)
                {
                    bestRank = rank;
                    best = v.Ship;
                }
            }
            return best != null && Hold(o, best, jd, "taken alive with the " + u.Ship.Name + " at " + b.Name);
        }

        /// <summary>Locks <paramref name="o"/> in <paramref name="ship"/>'s brig. False if it has no berth free.</summary>
        public bool Hold(Outlaw o, Ship ship, double jd, string how)
        {
            if (o == null || ship == null || ship.Destroyed || Outfitting.BrigRoom(ship) < 1) return false;
            if (o.Fate != OutlawFate.AtLarge && o.Fate != OutlawFate.Held) return false;
            Release(o);
            Unbind(o);
            o.Fate = OutlawFate.Held;
            o.FateJd = jd;
            o.FateBy = "the " + ship.Name;
            o.HeldBy = ship;
            o.Pod = null;
            o.Ashore = false;
            ship.CaptainsHeld++;
            var rec = o.Record;
            world.Log(jd, $"{Cap(o.Title)} is {how} and locked in the brig of the {ship.Name}" +
                (rec.Wanted ? $": {Cr(rec.TotalBounty)} cr on their head, paid in full when handed over" : ""), WorldEventKind.Law, -1);
            if (ship == world.PilotedShip)
                Law.Notice($"{Cap(o.Title)} is in your brig. Hand them over at a port of {Posters(rec)} for {Cr(rec.TotalBounty)} cr, or ransom them at a haven for {Cr(RansomFor(o))} cr");
            return true;
        }

        static string Posters(LawRecord rec)
        {
            var names = new List<string>();
            foreach (var f in rec.Bounty.Keys) names.Add(f.Name);
            return names.Count == 0 ? "anyone" : string.Join(" or ", names);
        }

        /// <summary>The captain no longer commands their old flagship (taken, or left behind): the hull's books entries go, so its
        /// later loss under another's colours is not read as the captain's death (U.SPC-T579).</summary>
        void Unbind(Outlaw o)
        {
            var old = o.Ship;
            if (old == null) return;
            if (byShip.TryGetValue(old, out var was) && was == o) byShip.Remove(old);
            if (Law.Captains.TryGetValue(old, out var r) && r == o.Record) Law.Captains.Remove(old);
        }

        /// <summary>Lets go of a held captain's berth.</summary>
        static void Release(Outlaw o)
        {
            if (o.HeldBy != null) o.HeldBy.CaptainsHeld = Math.Max(0, o.HeldBy.CaptainsHeld - 1);
            o.HeldBy = null;
        }

        /// <summary>The captain is free and back with the raiders: at large, ashore until a new band sails (<see cref="Sync"/>).</summary>
        void Free(Outlaw o, double jd, string how)
        {
            Release(o);
            Unbind(o);
            o.Fate = OutlawFate.AtLarge;
            o.Pod = null;
            o.Ashore = true;
            o.FlagshipAway = false;
            o.FateBy = null;
            world.Log(jd, $"{Cap(o.Title)} {how}: free, and back with the raiders", WorldEventKind.Law, -1);
        }

        /// <summary>A freed captain takes command of a band that has just sailed: their record, and the law's books, follow them to it.</summary>
        void Rebind(Outlaw o, Ship ship, double jd)
        {
            var old = o.Ship;
            if (old != null && byShip.TryGetValue(old, out var was) && was == o) byShip.Remove(old);
            if (old != null && Law.Captains.TryGetValue(old, out var r) && r == o.Record) Law.Captains.Remove(old);
            o.Record.Captain = ship;
            Law.Captains[ship] = o.Record;
            o.Ship = ship;
            o.Haven = world.War.HomeHaven(ship);
            o.Ashore = false;
            o.FlagshipAway = false;
            byShip[ship] = o;
            world.Log(jd, $"{Cap(o.Title)} is back in command, of the raider {ship.Name}", WorldEventKind.Law, -1);
        }

        /// <summary>Pays out the bounties on <paramref name="rec"/> at <paramref name="share"/> to <paramref name="payee"/>
        /// (a hunter's group, or whoever's ship it is), each poster paying its own; all of them are cleared. Returns the credits.</summary>
        double Pay(LawRecord rec, double share, Ship payee)
        {
            double total = 0;
            foreach (var kv in new List<KeyValuePair<Faction, double>>(rec.Bounty))
            {
                double amount = kv.Value * share;
                kv.Key.Treasury -= amount;
                total += amount;
                Law.ClearBounty(rec, kv.Key);
            }
            var hunter = HunterOf(payee);
            if (hunter != null)
            {
                hunter.Collected += total;
                hunter.Takes++;
                hunter.Ship.Credits += total;
                if (Guild != null) Guild.Treasury += total;
                if (hunter.Contract?.Target == rec) hunter.Contract = null;
            }
            else if (payee?.Faction != null) payee.Faction.Treasury += total;
            return total;
        }

        void Lapse(LawRecord rec, double jd, string why)
        {
            if (!rec.Wanted) return;
            foreach (var f in new List<Faction>(rec.Bounty.Keys)) Law.ClearBounty(rec, f);
            world.Log(jd, $"The bounties on {rec.Name} lapse: {why}", WorldEventKind.Law, -1);
        }

        /// <summary>
        /// Hands a held captain over at <paramref name="port"/>, a lawful port of a faction that posted a bounty on them
        /// (any lawful port, if none stands any more): every bounty on them is paid in full to the holder. Returns the credits.
        /// </summary>
        public double Deliver(Outlaw o, Settlement port, double jd)
        {
            if (o == null || o.Fate != OutlawFate.Held || o.HeldBy == null) return 0;
            if (port == null || port.Fallen || port.Owner == null || port.Owner.IsPirate) return 0;
            var holder = o.HeldBy;
            var rec = o.Record;
            if (rec.Wanted && !rec.WantedBy(port.Owner)) return 0;
            if (world.Economy != null && !world.Economy.CanTrade(holder, port)) return 0;
            double paid = Pay(rec, 1, holder);
            End(o, OutlawFate.Captured, jd, "the " + holder.Name);
            world.Log(jd, $"The {holder.Name} hands {o.Name} over to the {port.Owner.Name} at {port.Name}" + (paid > 0 ? $" for {Cr(paid)} cr, the full price alive" : ""),
                WorldEventKind.Law, port.Body);
            if (holder == world.PilotedShip) Law.Notice($"{Cap(o.Name)} is handed over at {port.Name}" + (paid > 0 ? $": {Cr(paid)} cr, the full price alive" : ""));
            return paid;
        }

        /// <summary>Ransoms a held captain back to the raiders at <paramref name="haven"/>: they pay <see cref="RansomFor"/> and
        /// the captain goes free, still wanted. Returns the credits (0 if the haven will not deal or cannot pay).</summary>
        public double Ransom(Outlaw o, Settlement haven, double jd)
        {
            if (o == null || o.Fate != OutlawFate.Held || o.HeldBy == null) return 0;
            if (haven == null || haven.Fallen || haven.Owner == null || !haven.Owner.IsPirate) return 0;
            var holder = o.HeldBy;
            double ransom = RansomFor(o);
            if (haven.Owner.Treasury < ransom || holder.Faction == null) return 0;
            haven.Owner.Treasury -= ransom;
            holder.Faction.Treasury += ransom;
            Free(o, jd, $"is ransomed back to the {haven.Owner.Name} at {haven.Name} by the {holder.Name} for {Cr(ransom)} cr");
            return ransom;
        }

        /// <summary>
        /// A hull is going down in battle (from <see cref="War"/>, as its people make for <paramref name="pod"/>): an
        /// outlaw commanding it reaches the pods with <see cref="EjectChance"/> (<see cref="EjectChanceReactor"/> when the
        /// reactor went), and a captain in its brig breaks out into them. Either is then adrift, to be tractored in by
        /// whoever finds the pods, or lost with them if nobody does.
        /// </summary>
        public void OnHullLost(Battle b, BattleUnit u, EscapePod pod, bool reactor, double jd) =>
            OnHullLost(b, u, pod, reactor, jd, rng.NextDouble());

        public void OnHullLost(Battle b, BattleUnit u, EscapePod pod, bool reactor, double jd, double roll)
        {
            if (u?.Ship == null) return;
            if (pod != null && pod.Captain == null && byShip.TryGetValue(u.Ship, out var o) && o.Aboard && o.Ship == u.Ship &&
                roll < (reactor ? EjectChanceReactor : EjectChance))
            {
                o.Pod = pod;
                pod.Captain = o;
                world.Log(jd, $"{Cap(o.Title)} ejects from the dying {u.Ship.Name} in its escape pods" +
                    (o.Record.Wanted ? $", {Cr(o.Record.TotalBounty)} cr on their head" : ""), WorldEventKind.Law, -1);
            }
            foreach (var c in All)
            {
                if (c.Fate != OutlawFate.Held || c.HeldBy != u.Ship || pod == null || pod.Captain != null) continue;
                Release(c);
                c.Fate = OutlawFate.AtLarge;
                c.Pod = pod;
                pod.Captain = c;
                world.Log(jd, $"{Cap(c.Title)} breaks out of the brig of the dying {u.Ship.Name} and into its escape pods", WorldEventKind.Law, -1);
            }
        }

        /// <summary>
        /// Pods have been picked up or have fallen silent (<see cref="Crews.Resolved"/>): a captain aboard them is taken by
        /// whoever tractored them in -- into that ship's brig, or as the rescuers' prisoner if it has no berth -- goes
        /// free if raiders hauled them in, or dies when the air runs out (and their bounties lapse with nobody to claim them).
        /// </summary>
        public void OnPodResolved(EscapePod pod, Ship ship)
        {
            var o = pod?.Captain;
            if (o == null || o.Pod != pod) return;
            o.Pod = null;
            double jd = pod.ResolvedJd;
            if (pod.State == PodState.Lost)
            {
                End(o, OutlawFate.Killed, jd, "their pods' air ran out");
                Lapse(o.Record, jd, "they died in the pods");
                return;
            }
            var by = pod.TakenBy;
            if (by != null && by.IsPirate)
            {
                Free(o, jd, $"is hauled in from the pods by the {(ship != null ? ship.Name : by.Name)}");
                return;
            }
            if (ship != null && Hold(o, ship, jd, "tractored in from the pods")) return;
            End(o, OutlawFate.Captured, jd, by?.Name);
            if (by != null && o.Record.WantedBy(by)) Law.ClearBounty(o.Record, by);
            if (ship != null && ship == world.PilotedShip) Law.Notice($"No berth free in your brig: {o.Name} is handed to the {by?.Name} as their prisoner");
        }

        /// <summary>
        /// A captain held aboard a ship that fought (<see cref="OnBattleEnded"/>): the hull lost with them aboard kills
        /// them; taken by raiders, they are freed; by anyone else, they change hands; beaten below
        /// <see cref="BrigBreachHull"/>, the brig is breached and they get away.
        /// </summary>
        void Breach(Outlaw o, BattleUnit hu, double jd)
        {
            var holder = o.HeldBy;
            if (hu.Dead)
            {
                End(o, OutlawFate.Killed, jd, "died in the brig of the " + holder.Name);
                Lapse(o.Record, jd, "they died in a brig");
            }
            else if (hu.Captured)
            {
                var captor = hu.Combat?.Interior.Boarder;
                if (captor == null || captor.IsPirate) Free(o, jd, $"is freed from the brig of the {holder.Name} when the raiders take it");
                else
                {
                    // Lawful boarders find the captain in the brig: theirs, and so is the price on them, in full.
                    double total = 0;
                    foreach (var kv in new List<KeyValuePair<Faction, double>>(o.Record.Bounty))
                    {
                        kv.Key.Treasury -= kv.Value;
                        total += kv.Value;
                        Law.ClearBounty(o.Record, kv.Key);
                    }
                    captor.Treasury += total;
                    End(o, OutlawFate.Captured, jd, captor.Name);
                    if (total > 0) world.Log(jd, $"{captor.Name} collects {Cr(total)} cr on {o.Title}, found in the brig of the {holder.Name}", WorldEventKind.Law, -1);
                }
            }
            else if (hu.Combat != null && hu.Combat.HullFraction < BrigBreachHull)
            {
                Free(o, jd, $"slips out through the breached brig of the {holder.Name}");
                if (holder == world.PilotedShip) Law.Notice($"Your brig is breached: {o.Name} has got away");
            }
        }

        /// <summary>
        /// Daily: captains held aboard ships the player is not flying are handed over when the holder lies at a lawful
        /// port that pays for them; one carried through a gate is gone from this system's books, and one aboard a hull
        /// lost outside a fight dies with it.
        /// </summary>
        void SettleCaptives(double jd)
        {
            foreach (var o in All.ToArray())
            {
                if (o.Fate != OutlawFate.Held || o.HeldBy == null) continue;
                var h = o.HeldBy;
                // The holder itself through a gate or under its own cruise drive (U.SPC-T453) is the same live hull,
                // never flagged GateDeparted; it is simply no longer in this world's own fleet (Fleet.IsPresent).
                if (h.GateDeparted || !world.Fleet.IsPresent(h))
                {
                    End(o, OutlawFate.Captured, jd, "carried out of the system aboard the " + h.Name);
                    continue;
                }
                if (h.Destroyed)
                {
                    End(o, OutlawFate.Killed, jd, "lost with the " + h.Name);
                    Lapse(o.Record, jd, "they were lost in a brig");
                    continue;
                }
                if (h == world.PilotedShip || h.Battle != null) continue;
                var c = h.Controller;
                if (c.Phase != ShipPhase.Parked || c.Orbit == null) continue;
                foreach (var st in world.Economy.At(c.Orbit.Body))
                    if (Deliver(o, st, jd) > 0 || o.Fate != OutlawFate.Held) break;
            }
        }

        /// <summary>The nearest body with a lawful port that pays for <paramref name="rec"/> (any lawful port once nothing is posted) and docks <paramref name="ship"/>, or -1.</summary>
        int NearestPortFor(LawRecord rec, Ship ship, int here, double jd)
        {
            int best = -1;
            double bestKm = double.MaxValue;
            foreach (var st in world.Economy.Settlements)
            {
                if (st.Fallen || st.Owner == null || st.Owner.IsPirate || (rec.Wanted && !rec.WantedBy(st.Owner))) continue;
                if (!world.Economy.CanTrade(ship, st)) continue;
                double km = st.Body == here ? 0 : (world.Ephemeris.HelioAt(st.Body, jd) - world.Ephemeris.HelioAt(here, jd)).Length;
                if (km < bestKm)
                {
                    bestKm = km;
                    best = st.Body;
                }
            }
            return best;
        }

        /// <summary>The player's ship within <see cref="ProofScanKm"/> of a wreck it has a kill claim on scans it: the dead rate is paid.</summary>
        void ProveKills(Ship ship, double jd)
        {
            if (Claims.Count == 0 || world.War == null) return;
            for (int i = Claims.Count - 1; i >= 0; i--)
            {
                var c = Claims[i];
                if (c.Wreck == null || c.Wreck.Expired(jd) || !world.War.Wrecks.Items.Contains(c.Wreck)) continue;
                if ((world.WreckHelio(c.Wreck, jd) - pilotHelio).Length > ProofScanKm) continue;
                Claims.RemoveAt(i);
                var rec = c.Outlaw.Record;
                double paid = Pay(rec, DeadShare, ship);
                world.Log(jd, $"The {ship.Name} scans the wreck of the {c.Wreck.Name}: proof that {c.Outlaw.Name} is dead, and {Cr(paid)} cr in bounties paid", WorldEventKind.Law, -1);
                Law.Notice($"Wreck scanned: the kill of {c.Outlaw.Name} is proven. {Cr(paid)} cr paid, the dead rate");
            }
        }

        /// <summary>Kill claims whose wreck broke up before anyone scanned it lapse, and the bounties with them.</summary>
        void LapseClaims(double jd)
        {
            for (int i = Claims.Count - 1; i >= 0; i--)
            {
                var c = Claims[i];
                if (c.Wreck != null && !c.Wreck.Expired(jd) && world.War != null && world.War.Wrecks.Items.Contains(c.Wreck)) continue;
                Claims.RemoveAt(i);
                Lapse(c.Outlaw.Record, jd, "nobody scanned the wreck to prove the kill");
                Law.Notice($"The wreck of {c.Outlaw.Name}'s flagship is gone unscanned: no proof, no bounty");
            }
        }

        /// <summary>Whether <paramref name="hunter"/> wants <paramref name="target"/>'s captain alive: a hunter group facing a
        /// wanted outlaw's flagship (see <see cref="Battle.WantsAlive"/>).</summary>
        public bool WantsAlive(Ship hunter, Ship target)
        {
            if (!IsHunter(hunter) || target == null) return false;
            return byShip.TryGetValue(target, out var o) && o.Aboard && o.Ship == target && o.Record.Wanted;
        }

        /// <summary>The record of the outlaw whose own flagship <paramref name="ship"/> is, even when it now sails in another's group.</summary>
        public LawRecord RecordCommanding(Ship ship) =>
            ship != null && byShip.TryGetValue(ship, out var o) && o.Ship == ship ? o.Record : null;

        // ---------------------------------------------------------------- feared names

        void Fear(double jd)
        {
            foreach (var o in All)
            {
                if (!o.AtLarge || o.Feared || o.DaysAtLarge(jd) < FearedDays) continue;
                if (o.CrimeCount < FearedCrimes && o.Kills < FearedKills) continue;
                o.Feared = true;
                o.FearedJd = jd;
                o.Record.Notoriety += 25;
                foreach (var f in new List<Faction>(o.Record.Bounty.Keys)) o.Record.Bounty[f] *= FearedBountyFactor;
                world.Log(jd, $"{o.Name} has run free for {o.DaysAtLarge(jd):0} days ({o.Deeds()}): the name is feared on every lane, and the price on it rises to {Cr(o.Record.TotalBounty)} cr",
                    WorldEventKind.Law, -1);
            }
        }

        // ---------------------------------------------------------------- the boards

        /// <summary>
        /// The bounty board at <paramref name="port"/>: everyone its owner has a bounty posted on (outlaws still at
        /// large, the player, a lawful captain wanted for an assault), the biggest first, with where each was last
        /// seen and who is hunting them. A haven keeps no board.
        /// </summary>
        public List<BountyPosting> BoardAt(Settlement port)
        {
            var list = new List<BountyPosting>();
            if (port == null || port.Owner == null || port.Owner.IsPirate || port.Fallen || Law == null) return list;
            foreach (var p in Postings())
                if (p.Target.WantedBy(port.Owner))
                {
                    p.PostedBy = port.Owner;
                    p.Bounty = p.Target.BountyBy(port.Owner);
                    list.Add(p);
                }
            list.Sort((a, b) => b.Bounty.CompareTo(a.Bounty));
            return list;
        }

        /// <summary>Every bounty on the boards system-wide, one posting per target (its total price), the biggest first.</summary>
        public List<BountyPosting> Postings()
        {
            var list = new List<BountyPosting>();
            if (Law == null) return list;
            void Add(LawRecord r)
            {
                if (!r.Wanted) return;
                var o = Of(r);
                if (o != null && !o.AtLarge) return;
                // (A captain adrift in the pods or ashore is wanted all the same.)
                if (!r.IsPlayer && (o == null || o.Aboard) && (r.Captain == null || r.Captain.Destroyed)) return;
                list.Add(new BountyPosting { Target = r, Outlaw = o, Bounty = r.TotalBounty, TotalBounty = r.TotalBounty, LastSeen = SeenLast(r), HuntedBy = HunterOn(r) });
            }
            Add(Law.Player);
            foreach (var r in Law.Captains.Values) Add(r);
            list.Sort((a, b) => b.TotalBounty.CompareTo(a.TotalBounty));
            return list;
        }

        /// <summary>The hunter with a contract on <paramref name="r"/>, or null.</summary>
        public BountyHunter HunterOn(LawRecord r)
        {
            foreach (var h in Hunters)
                if (h.Alive && h.Contract?.Target == r) return h;
            return null;
        }

        /// <summary>A board line in words: who, how much, what they did, where last seen, and who is after them.</summary>
        public static string Describe(BountyPosting p, double jd)
        {
            var o = p.Outlaw;
            string who = o != null ? o.Title : p.IsPlayer ? "YOU" : p.Target.Name;
            string text = who + "  ·  " + Cr(p.Bounty) + " cr";
            if (o != null) text += "  ·  " + o.Deeds() + ", " + o.DaysAtLarge(jd).ToString("0", CultureInfo.InvariantCulture) + " d at large";
            else text += "  ·  " + p.Target.Crimes.Count + (p.Target.Crimes.Count == 1 ? " crime" : " crimes") + " on file";
            var s = p.LastSeen;
            text += s != null ? "  ·  last seen " + s.Where + ", " + Ago(s.DaysAgo(jd)) + (s.By != null ? " by " + s.By : "") : "  ·  not seen";
            if (p.HuntedBy != null) text += "  ·  hunted by the " + p.HuntedBy.Name;
            return text;
        }

        static string Ago(double days) =>
            days < 1 ? "today" : days < 2 ? "yesterday" : days.ToString("0", CultureInfo.InvariantCulture) + " days ago";

        // ---------------------------------------------------------------- hunters

        /// <summary>
        /// While a bounty worth hunting goes unhunted and there are fewer hunter groups than <see cref="World.HunterCap"/>,
        /// the free traders now and then commission one at the biggest lawful shipyard that will dock them: a cruiser
        /// and a destroyer, paid for out of their treasury above <see cref="GuildReserve"/> (their crews sign on in port).
        /// </summary>
        void Commission(double jd)
        {
            if (Guild == null || world.HunterCap <= 0) return;
            int alive = 0;
            // Every hunter still under its faction counts, wherever it is: one away on a hunt abroad (through a gate
            // or under cruise) is still a group, so the guild does not buy a second to stand beside it (U.SPC-T586).
            foreach (var h in Hunters) if (!h.Retired && (!h.Ship.Destroyed || h.Ship.GateDeparted) && !h.Ship.Derelict && h.Ship.Faction == h.Faction) alive++;
            if (alive >= world.HunterCap) return;
            bool unhunted = false;
            foreach (var p in Postings())
                if (p.TotalBounty >= MinContract && p.HuntedBy == null && Huntable(p.Target)) unhunted = true;
            if (!unhunted) return;
            double cost = Economy.HullPrice(ShipClass.Cruiser) + Economy.HullPrice(ShipClass.Destroyer);
            if (Guild.Treasury < cost + GuildReserve || rng.NextDouble() > CommissionChance) return;
            Settlement yard = null;
            foreach (var st in world.Economy.Settlements)
            {
                if (!st.HasShipyard || st.Fallen || st.Owner == null || st.Owner.IsPirate || !world.Diplomacy.CanDock(Guild, st.Owner)) continue;
                if (yard == null || st.Population > yard.Population) yard = st;
            }
            if (yard == null) return;
            // Bought from the yard like any other hull: its owner is paid for them.
            Guild.Treasury -= cost;
            if (yard.Owner != Guild) yard.Owner.Treasury += cost;
            var hunter = Commission(yard, jd);
            world.Log(jd, $"{yard.Name} fits out the bounty hunter {hunter.Name} for the {Guild.Name}: there is money on the boards", WorldEventKind.Launch, yard.Body);
        }

        /// <summary>Puts a new hunter group into service out of <paramref name="yard"/> (a cruiser with a destroyer escort).</summary>
        public BountyHunter Commission(Settlement yard, double jd)
        {
            var fleet = world.Fleet;
            string Next()
            {
                int n = hunterNames++;
                return HunterNames[n % HunterNames.Length] + (n >= HunterNames.Length ? " " + (n / HunterNames.Length + 1) : "");
            }
            var lead = fleet.AddShip(Next(), ShipClass.Cruiser, Guild, ShipRole.Patrol);
            lead.Route = world.PortBodies();
            // A brig, to bring captains in alive for the full price (U.SPC-T63).
            Outfitting.Install(lead, ModuleKind.Brig);
            var escort = fleet.AddShip(Next(), ShipClass.Destroyer, Guild, ShipRole.Escort);
            fleet.AddEscort(lead, escort, Fleet.SlotFor(0));
            // Out into a parking orbit of its own (not the yard's slip, where a new merchant may be sitting).
            fleet.Station(lead, yard.Body, jd);
            var h = new BountyHunter(lead, jd, world);
            Hunters.Add(h);
            byHunter[lead] = h;
            world.NotifyAdded(lead);
            return h;
        }

        /// <summary>A hunter group leaves (or comes home from) a hunt beyond the gates: while away, its row survives the
        /// flagship being lost abroad (U.SPC-T585).</summary>
        public void SetAway(Ship flagship, bool away)
        {
            if (flagship != null && byHunter.TryGetValue(flagship, out var h)) h.Away = away;
        }

        /// <summary>A hunter flagship lost: its escort carries on as the group (see <see cref="War"/>'s succession).</summary>
        public void OnSuccession(Ship dead, Ship heir)
        {
            if (dead == null || heir == null || !byHunter.TryGetValue(dead, out var h)) return;
            var next = new BountyHunter(heir, h.SinceJd, world) { Contract = h.Contract, Collected = h.Collected, Takes = h.Takes };
            byHunter.Remove(dead);
            Hunters.Add(next);
            byHunter[heir] = next;
            h.Contract = null;
            h.Retired = true;
        }

        /// <summary>Contracts lapse when the target is no longer wanted, taken, dead or long lost.</summary>
        void ReviewContracts(double jd)
        {
            foreach (var h in Hunters)
            {
                var c = h.Contract;
                if (c == null) continue;
                if (!h.Alive || !Huntable(c.Target) || jd > c.UntilJd) h.Contract = null;
            }
            PruneHunters();
        }

        /// <summary>Drops hunter rows nothing can use any more: handed on to an heir (<see cref="OnSuccession"/>
        /// already took it out of <see cref="byHunter"/>), or a flagship destroyed with no heir to carry it on.
        /// A hunter gone through a gate keeps its row: it takes it up again when it comes home from a hunt
        /// abroad (see <see cref="InterstellarTraffic"/>). Keeps the roster from growing for ever with retired
        /// rows over a long game (U.SPC-T321).</summary>
        void PruneHunters()
        {
            for (int i = Hunters.Count - 1; i >= 0; i--)
            {
                var h = Hunters[i];
                if (!h.Retired && (!h.Ship.Destroyed || h.Ship.GateDeparted || h.Away)) continue;
                Hunters.RemoveAt(i);
                if (byHunter.TryGetValue(h.Ship, out var cur) && cur == h) byHunter.Remove(h.Ship);
            }
        }

        bool Huntable(LawRecord r)
        {
            if (r == null || !r.Wanted || r.TotalBounty < MinContract * 0.5) return false;
            if (r.IsPlayer) return world.PilotedShip != null && !world.PilotedShip.Destroyed;
            var o = Of(r);
            return o != null && o.Aboard && !o.Ship.Destroyed;
        }

        /// <summary>Hunters idle at a lawful shipyard are patched up there, at the free traders' expense.</summary>
        void RepairHunters()
        {
            foreach (var s in world.Fleet.Ships)
            {
                if (s.Destroyed || s.Battle != null || !IsHunter(s) || s.Combat.Damage <= 0.001) continue;
                var c = s.Controller;
                if (c.Phase != ShipPhase.Parked) continue;
                foreach (var st in world.Economy.At(c.Orbit.Body))
                {
                    if (!st.HasShipyard || st.Fallen || st.Owner == null || st.Owner.IsPirate || !world.Diplomacy.CanDock(Guild, st.Owner)) continue;
                    double share = Math.Min(0.2, s.Combat.Damage);
                    s.Combat.Repair(share);
                    double bill = Economy.HullPrice(s.Spec.Class) * share * 0.3;
                    Guild.Treasury -= bill;
                    st.Owner.Treasury += bill;
                    break;
                }
            }
        }

        /// <summary>
        /// Fleet hook (through <see cref="War.ChooseNext"/>): where a hunter group goes next. Hurt, to the nearest
        /// lawful yard. On a contract, to where its target was last seen (and it waits there for it). Without one,
        /// it takes the best it can match from the boards -- the most money for the fewest days away -- or, with
        /// nothing worth taking, makes the round of the ports.
        /// </summary>
        public int ChooseNext(Ship leader, int here, double jd)
        {
            var h = HunterOf(leader);
            if (h == null) return -1;
            // A captain in the brig goes to the nearest port that will pay for them first.
            var held = All.Find(o => o.Fate == OutlawFate.Held && o.HeldBy != null && o.HeldBy.Controller == leader);
            if (held != null)
            {
                int port = NearestPortFor(held.Record, leader, here, jd);
                if (port >= 0) return port;
            }
            if (leader.Combat.HullDamage > RepairDamage || !leader.Combat.Armed)
            {
                int yard = NearestYard(here, jd);
                if (yard >= 0) return yard;
            }
            if (h.Contract != null && (!Huntable(h.Contract.Target) || Outmatched(h, SeenLast(h.Contract.Target)))) h.Contract = null;
            h.Contract ??= TakeContract(h, here, jd);
            if (h.Contract != null)
            {
                var seen = SeenLast(h.Contract.Target);
                if (seen != null && seen.Body >= 0) return seen.Body;
            }
            // Nothing to chase: the guns' jobs on the contracts boards, a patrol or an escort (U.SPC-T101).
            int job = world.Contracts?.ChooseForHunter(leader, here, jd) ?? -1;
            if (job >= 0) return job;
            var ports = leader.Route;
            if (ports == null || ports.Length == 0) return here;
            return ports[rng.Next(ports.Length)];
        }

        /// <summary>Whether what would fight the hunter where its target was seen (the target's band, a haven's guns,
        /// other raiders there) is now more than the hunter group can take on: it drops the contract.</summary>
        bool Outmatched(BountyHunter h, Sighting seen) =>
            seen != null && seen.Body >= 0 && world.War.DefenceAt(seen.Body, h.Ship.Faction) > world.War.GroupStrength(h.Ship) * 1.2;

        /// <summary>The contract a hunter takes from the boards, or null: a target worth <see cref="MinContract"/>, seen
        /// lately somewhere it can go, nobody else is after, and that it can match (<see cref="MatchShare"/>).</summary>
        public BountyContract TakeContract(BountyHunter h, int here, double jd)
        {
            BountyContract best = null;
            double bestValue = 0;
            double strength = world.War.GroupStrength(h.Ship);
            foreach (var p in Postings())
            {
                if (p.TotalBounty < MinContract || (p.HuntedBy != null && p.HuntedBy != h) || !Huntable(p.Target)) continue;
                var seen = p.LastSeen;
                if (seen == null || seen.Body < 0 || jd - seen.Jd > StaleSightingDays) continue;
                if (p.Outlaw != null)
                {
                    // Its band, and whatever else would fight it where it was seen (a haven's guns, other raiders).
                    double need = world.War.GroupStrength(p.Outlaw.Ship) * (p.Outlaw.Feared ? 1 : MatchShare);
                    need = Math.Max(need, world.War.DefenceAt(seen.Body, h.Ship.Faction) * (p.Outlaw.Feared ? 1 : MatchShare));
                    if (strength < need) continue;
                }
                double days = seen.Body == here ? 0 : world.Fleet.EstimateTravelDays(h.Ship, here, seen.Body, jd);
                double value = p.TotalBounty / (days + 3);
                if (value > bestValue)
                {
                    bestValue = value;
                    best = new BountyContract { Target = p.Target, TakenJd = jd, UntilJd = jd + ContractDays };
                }
            }
            if (best != null)
            {
                var o = Of(best.Target);
                string who = o != null ? o.Title : best.Target.IsPlayer ? "the player's ship" : best.Target.Name;
                world.Log(jd, $"The bounty hunter {h.Name} takes the contract on {who} ({Cr(best.Target.TotalBounty)} cr)", WorldEventKind.Law, -1);
                if (best.Target.IsPlayer) Law.Notice($"The bounty hunter {h.Name} has taken the contract on you: {Cr(best.Target.TotalBounty)} cr on your head");
            }
            return best;
        }

        int NearestYard(int here, double jd)
        {
            int best = -1;
            double bestKm = double.MaxValue;
            foreach (var st in world.Economy.Settlements)
            {
                if (!st.HasShipyard || st.Fallen || st.Owner == null || st.Owner.IsPirate || !world.Diplomacy.CanDock(Guild, st.Owner)) continue;
                double km = st.Body == here ? 0 : (world.Ephemeris.HelioAt(st.Body, jd) - world.Ephemeris.HelioAt(here, jd)).Length;
                if (km < bestKm)
                {
                    bestKm = km;
                    best = st.Body;
                }
            }
            return best;
        }

        /// <summary>
        /// Advances the hunt around the player's ship, flown at <paramref name="offsetKm"/> from <paramref name="body"/>:
        /// where it is (for the next sighting), and a hunter with a contract on it that comes within
        /// <see cref="EngageKm"/> moves on it (<see cref="War.TryStartHunt"/>). Returns that battle, or null.
        /// </summary>
        public Battle UpdatePilot(Ship ship, int body, DVec3 offsetKm, DVec3 forward, double jd)
        {
            if (ship == null || ship.Destroyed || world.War == null) return null;
            pilotBody = body;
            pilotHelio = world.Ephemeris.HelioAt(body, jd) + offsetKm;
            pilotJd = jd;
            ProveKills(ship, jd);
            if (ship.Battle != null || !Law.Player.Wanted) return null;
            foreach (var h in Hunters)
            {
                // A hunter off abroad through a gate or its own cruise drive (U.SPC-T453) reads as not Alive here
                // (Fleet.IsPresent): nothing here gives a position for it to engage from.
                if (!h.Alive || h.Contract?.Target != Law.Player || h.Ship.Battle != null || h.Ship == ship) continue;
                if ((world.Fleet.StateOf(h.Ship, jd).HelioKm - pilotHelio).Length > EngageKm) continue;
                var b = world.War.TryStartHunt(ship, h.Ship, body, offsetKm, forward, jd);
                if (b == null) continue;
                Law.Notice($"Bounty hunter {h.Name}: \"{ship.Name}, there's {Cr(Law.Player.TotalBounty)} on your head. Nothing personal.\"");
                return b;
            }
            return null;
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static string Cr(double v) => v.ToString("N0", CultureInfo.InvariantCulture);
    }
}
