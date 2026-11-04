using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>The crimes a faction's law knows (see <see cref="Law"/>).</summary>
    public enum CrimeKind
    {
        /// <summary>Opening fire on a lawful ship: one whose faction the offender is not at war with, and not wanted there.</summary>
        Assault,

        /// <summary>Robbing or taking a lawful ship (its cargo ransomed, the hull boarded), or any attack by pirates.</summary>
        Piracy,

        /// <summary>Contraband in the hold when a patrol scans it: stolen goods, or a good the faction bans.</summary>
        Smuggling,

        /// <summary>Flying into a port's space while barred from docking there.</summary>
        Trespass,

        /// <summary>Running from a patrol's scan, or from an interdiction once hailed.</summary>
        FleeingScan,
    }

    /// <summary>One crime: who, what, where and whose law it broke, and whether anyone saw it.</summary>
    public sealed class Crime
    {
        public CrimeKind Kind;
        public LawRecord Offender;

        /// <summary>Whose space it was committed in (null for lawless space), and whose ship suffered it.</summary>
        public Faction Jurisdiction, Victim;

        /// <summary>Who posted the bounty: the jurisdiction, or the victim's faction in lawless space.</summary>
        public Faction PostedBy;

        public int Body = -1;
        public double Jd;

        /// <summary>The ship or station that saw it, or null: an unwitnessed crime is never recorded.</summary>
        public string Witness;
        public string Detail;
        public double Bounty;

        public bool Recorded => Witness != null && PostedBy != null;

        public override string ToString() => Law.Describe(Kind) + (Detail != null ? " (" + Detail + ")" : "");
    }

    /// <summary>
    /// A party's standing in the eyes of the law: the player's (one record, whatever hull they fly) or an NPC
    /// captain's (the master of a ship or the leader of a group). Standing with each faction (positive is
    /// trusted, below <see cref="Law.DockMinStanding"/> barred from its ports), notoriety (how widely known a
    /// criminal it is, fading with time) and the bounties posted on it by each faction.
    /// </summary>
    public sealed class LawRecord
    {
        public readonly string Name;

        /// <summary>The captain's ship (the group leader's), or null for the player.</summary>
        public Ship Captain { get; internal set; }
        public bool IsPlayer => Captain == null;

        /// <summary>Standing with each faction (0 neutral; crimes lower it, good deeds raise it).</summary>
        public readonly Dictionary<Faction, double> Standing;

        /// <summary>Bounties posted on this party, credits, by the faction that posted each.</summary>
        public readonly Dictionary<Faction, double> Bounty = new Dictionary<Faction, double>();

        /// <summary>When each bounty lapses if nothing renews it (a crime against the same poster does).</summary>
        public readonly Dictionary<Faction, double> BountyUntil = new Dictionary<Faction, double>();

        /// <summary>The crimes on record, newest last (only the witnessed ones).</summary>
        public readonly List<Crime> Crimes = new List<Crime>();

        /// <summary>How notorious: each recorded crime adds to it, and it halves every <see cref="Law.NotorietyHalfLifeDays"/>.</summary>
        public double Notoriety;

        /// <summary>Crimes nobody saw: they never reached a record.</summary>
        public int Unwitnessed;

        public LawRecord(string name, Ship captain, Dictionary<Faction, double> standing = null)
        {
            Name = name;
            Captain = captain;
            Standing = standing ?? new Dictionary<Faction, double>();
        }

        public double StandingWith(Faction f) => f != null && Standing.TryGetValue(f, out var s) ? s : 0;

        public double BountyBy(Faction f) => f != null && Bounty.TryGetValue(f, out var b) ? b : 0;

        public bool WantedBy(Faction f) => BountyBy(f) > 0;

        public bool Wanted => Bounty.Count > 0;

        public double TotalBounty
        {
            get
            {
                double t = 0;
                foreach (var kv in Bounty) t += kv.Value;
                return t;
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>A patrol's hail of the player's ship: a scan to hold still for, or an order to heave to and pay up.</summary>
    public sealed class PatrolHail
    {
        public Ship Patrol;
        public Faction By;

        /// <summary>True for an interdiction (the player is wanted: pay the bounty or be fired on); false for a routine scan.</summary>
        public bool Interdiction;

        public double StartJd, DueJd;

        /// <summary>The fine an interdiction asks (the bounty the hailing faction has posted).</summary>
        public double Fine;

        public double SecondsLeft(double jd) => Math.Max(0, (DueJd - jd) * Astro.SecondsPerDay);
    }

    /// <summary>
    /// Law, crime and reputation. Each faction has a jurisdiction: the bodies its settlements are on, the
    /// planetary systems whose hyperlane ramps it keeps (see <see cref="World.LaneKeeper"/>) and those lanes;
    /// a pirate haven, and space nobody keeps, are lawless. Attacking a lawful ship, piracy, smuggling
    /// contraband, trespassing and running from a scan are crimes -- recorded only if witnessed by a ship or
    /// station in sensor range (a merchant whose crew all died on an empty lane tells nobody). A recorded
    /// crime raises the offender's notoriety, lowers its standing with the jurisdiction and the victim's
    /// faction, and posts a bounty in that jurisdiction (the victim's own faction posts it in lawless space).
    /// Patrols hail the player's ship: routine scans in their own space (contraband is confiscated and is
    /// smuggling; running is a crime) and, for a wanted ship anywhere, an order to heave to and pay the bounty
    /// or be interdicted (<see cref="War.TryStartInterdiction"/>). Standing gates docking (and so trade),
    /// prices (see <see cref="PriceFactor"/>) and the contracts a faction will offer the player.
    /// </summary>
    public sealed class Law
    {
        readonly World world;

        /// <summary>The player's record: one, whatever hull they fly. Its standing is the event system's
        /// reputation table, so adventures seen through raise the same standing crimes lower.</summary>
        public readonly LawRecord Player;

        /// <summary>NPC captains' records, by the ship they command (a group's leader), made on their first crime.</summary>
        public readonly Dictionary<Ship, LawRecord> Captains = new Dictionary<Ship, LawRecord>();

        /// <summary>Recorded crimes system-wide, newest last.</summary>
        public readonly List<Crime> Recent = new List<Crime>();

        /// <summary>Messages for the player (their own crimes, hails, scans), drained by the HUD (<see cref="TakeNotices"/>).</summary>
        readonly List<string> notices = new List<string>();

        /// <summary>The patrol hail the player's ship is answering, or null.</summary>
        public PatrolHail Hail { get; private set; }

        /// <summary>Raised for every crime, witnessed or not (recorded ones carry their bounty).</summary>
        public event Action<Crime> Committed;

        // ---------------------------------------------------------------- tuning

        /// <summary>How close a patrol comes before it hails the player's ship, km.</summary>
        public double PatrolHailKm = 100000;

        /// <summary>Past this distance from the hailing patrol the player has run from it, km.</summary>
        public double PatrolHoldKm = 180000;

        /// <summary>How long a scan takes to complete, seconds; and how long a hail to heave to waits for the fine.</summary>
        public double ScanSeconds = 20, HailSeconds = 30;

        /// <summary>How long a faction's patrols leave the player be after a scan or a hail, days.</summary>
        public double ScanCooldownDays = 0.5;

        /// <summary>How often trespass in one faction's space is charged again, days.</summary>
        public double TrespassCooldownDays = 1;

        /// <summary>Standing below which a faction's ports refuse to dock the ship (and so to trade).</summary>
        public const double DockMinStanding = -25;

        /// <summary>Standing below which a faction offers no contracts (distress calls, escorts, bounties).</summary>
        public const double ContractMinStanding = -10;

        /// <summary>A bounty this large closes a faction's ports to the ship whatever its standing, credits.</summary>
        public const double DockRefusedBounty = 100000;

        /// <summary>How long a bounty stands with nothing renewing it, days.</summary>
        public const double BountyDays = 180;

        /// <summary>How fast notoriety fades: it halves in this many days.</summary>
        public const double NotorietyHalfLifeDays = 90;

        /// <summary>The share of the standing a crime costs with its jurisdiction that it also costs with the
        /// victim's faction, when that is someone else.</summary>
        public const double VictimStandingShare = 0.6;

        double lastJd = double.NaN;
        readonly Dictionary<Faction, double> scannedUntil = new Dictionary<Faction, double>();
        readonly Dictionary<Faction, double> trespassUntil = new Dictionary<Faction, double>();

        public Law(World world, Dictionary<Faction, double> playerStanding = null)
        {
            this.world = world;
            Player = new LawRecord("you", null, playerStanding);
        }

        /// <summary>What a crime costs a record: notoriety, standing with whose law it broke, and the bounty posted.</summary>
        public static (double notoriety, double standing, double bounty) Severity(CrimeKind k)
        {
            switch (k)
            {
                case CrimeKind.Assault: return (10, 15, 50000);
                case CrimeKind.Piracy: return (20, 25, 150000);
                case CrimeKind.Smuggling: return (5, 8, 20000);
                case CrimeKind.Trespass: return (3, 5, 10000);
                default: return (6, 10, 30000);
            }
        }

        public static string Describe(CrimeKind k)
        {
            switch (k)
            {
                case CrimeKind.Assault: return "assault on a lawful ship";
                case CrimeKind.Piracy: return "piracy";
                case CrimeKind.Smuggling: return "smuggling contraband";
                case CrimeKind.Trespass: return "trespass";
                default: return "fleeing a scan";
            }
        }

        // ---------------------------------------------------------------- records and standing

        /// <summary>The record of whoever commands <paramref name="ship"/>: the player's for the ship they fly,
        /// else its captain's (the group leader's), made the first time it is asked for.</summary>
        public LawRecord RecordOf(Ship ship)
        {
            if (ship == null) return null;
            if (ship == world?.PilotedShip) return Player;
            var captain = ship.Controller;
            if (!Captains.TryGetValue(captain, out var r))
                Captains[captain] = r = new LawRecord(CaptainName(captain), captain);
            return r;
        }

        /// <summary>The record of a ship's captain if one has been opened (never for the player's ship), else null.</summary>
        public LawRecord FindRecord(Ship ship)
        {
            if (ship == null) return null;
            if (ship == world?.PilotedShip) return Player;
            return Captains.TryGetValue(ship.Controller, out var r) ? r : null;
        }

        static string CaptainName(Ship s) => s.Faction != null && s.Faction.IsPirate ? PirateCaptains.NameFor(s) : "the master of the " + s.Name;

        /// <summary>The bounties posted on the captain of <paramref name="ship"/>, credits (0 if none).</summary>
        public double BountyOn(Ship ship) => FindRecord(ship)?.TotalBounty ?? 0;

        /// <summary>Whether <paramref name="r"/> may dock at (and so trade with) <paramref name="s"/>: barred by
        /// standing below <see cref="DockMinStanding"/> with the owner, or by a bounty of <see cref="DockRefusedBounty"/>
        /// or more posted by it. A pirate haven asks no questions.</summary>
        public static bool CanDock(LawRecord r, Settlement s)
        {
            if (r == null || s?.Owner == null || s.Owner.IsPirate) return true;
            return r.StandingWith(s.Owner) >= DockMinStanding && r.BountyBy(s.Owner) < DockRefusedBounty;
        }

        /// <summary>Docking for a ship: its commander's record, if it has one, at <paramref name="s"/>.</summary>
        public bool AllowsDocking(Ship ship, Settlement s) => CanDock(FindRecord(ship), s);

        /// <summary>
        /// How standing moves prices at a faction's markets: what the party pays is multiplied by this and what
        /// it is paid divided by it. Trusted (+50 or better) buys 10% cheaper; distrusted pays up to 30% more
        /// (at -50 or worse); a haven deals with anyone at its own prices.
        /// </summary>
        public static double PriceFactor(LawRecord r, Faction owner)
        {
            if (r == null || owner == null || owner.IsPirate) return 1;
            double s = r.StandingWith(owner);
            return s >= 0 ? 1 - 0.1 * Math.Min(1, s / 50) : 1 + 0.3 * Math.Min(1, -s / 50);
        }

        /// <summary>A ship's price factor at a settlement (its commander's record).</summary>
        public double PriceFactorFor(Ship ship, Settlement s) => PriceFactor(FindRecord(ship), s?.Owner);

        /// <summary>Whether <paramref name="patron"/> offers <paramref name="r"/> its contracts: not while wanted by it,
        /// nor with standing below <see cref="ContractMinStanding"/>.</summary>
        public static bool OffersContracts(LawRecord r, Faction patron)
        {
            if (r == null || patron == null) return true;
            return !r.WantedBy(patron) && r.StandingWith(patron) >= ContractMinStanding;
        }

        static void Adjust(LawRecord r, Faction f, double delta)
        {
            if (f == null || f.IsPirate || delta == 0) return;
            r.Standing[f] = r.StandingWith(f) + delta;
        }

        // ---------------------------------------------------------------- jurisdiction

        /// <summary>
        /// Whose law holds at <paramref name="body"/>: the owner of its biggest lawful settlement, else whoever
        /// keeps its planetary system's lanes (<see cref="World.LaneKeeper"/>); null for lawless space (a haven's
        /// rock with no other settlement, or a system nobody keeps).
        /// </summary>
        public Faction JurisdictionAt(int body)
        {
            var port = PortAuthority(body);
            if (port != null) return port;
            var lanes = world.Fleet.Lanes;
            return lanes != null && body >= 0 ? world.LaneKeeper(lanes.SystemOf(body)) : null;
        }

        /// <summary>The owner of the biggest lawful settlement on <paramref name="body"/>, or null.</summary>
        public Faction PortAuthority(int body)
        {
            Settlement best = null;
            foreach (var s in world.Economy.At(body))
                if (!s.Fallen && s.Owner != null && !s.Owner.IsPirate && (best == null || s.Population > best.Population)) best = s;
            return best?.Owner;
        }

        /// <summary>Whose law holds on a lane: the keeper of the system it was joined from, else of the far end.</summary>
        public Faction JurisdictionOf(LaneHop hop) => world.LaneKeeper(hop.From) ?? world.LaneKeeper(hop.To);

        // ---------------------------------------------------------------- witnesses

        /// <summary>
        /// The first ship or station that could see something as bright as <paramref name="signature"/> (a battle's
        /// weapons fire, by default) at <paramref name="helioKm"/>: a lawful settlement or a lawful ship (not destroyed,
        /// adrift as a hulk, down on a planet, nor the player's own) that <paramref name="exclude"/> lets count, whose
        /// scanners reach that far (see <see cref="Sensors.FindWitness"/>: a station's traffic control sees a fight 3
        /// million km off, a merchant one million, a warship two). Pirates report nothing. Returns its name, or null
        /// when nobody saw.
        /// </summary>
        public string FindWitness(DVec3 helioKm, double jd, Func<Ship, bool> exclude = null, double signature = Signatures.Battle) =>
            (world.Sensors ?? new Sensors(world)).FindWitness(helioKm, jd, signature, exclude);

        // ---------------------------------------------------------------- crimes

        /// <summary>
        /// Books a crime by <paramref name="offender"/>. Unwitnessed (<paramref name="witness"/> null) it goes
        /// no further. Witnessed, it is posted by the jurisdiction (or, in lawless space, by the victim's faction
        /// if it is a lawful one): notoriety up, standing down with the poster (and, by
        /// <see cref="VictimStandingShare"/>, with the victim's faction if that is someone else) and a bounty in
        /// the poster's space, renewed for <see cref="BountyDays"/>.
        /// </summary>
        public Crime Commit(LawRecord offender, CrimeKind kind, Faction jurisdiction, Faction victim, int body, double jd, string witness, string detail = null)
        {
            if (jurisdiction != null && jurisdiction.IsPirate) jurisdiction = null;
            var crime = new Crime
            {
                Kind = kind, Offender = offender, Jurisdiction = jurisdiction, Victim = victim, Body = body, Jd = jd, Witness = witness, Detail = detail,
                PostedBy = jurisdiction ?? (victim != null && !victim.IsPirate ? victim : null),
            };
            if (!crime.Recorded)
            {
                offender.Unwitnessed++;
                Committed?.Invoke(crime);
                return crime;
            }
            var (notoriety, standing, bounty) = Severity(kind);
            var poster = crime.PostedBy;
            offender.Notoriety += notoriety;
            Adjust(offender, poster, -standing);
            if (victim != null && victim != poster) Adjust(offender, victim, -standing * VictimStandingShare);
            offender.Bounty[poster] = offender.BountyBy(poster) + bounty;
            offender.BountyUntil[poster] = jd + BountyDays;
            crime.Bounty = bounty;
            offender.Crimes.Add(crime);
            if (offender.Crimes.Count > 40) offender.Crimes.RemoveAt(0);
            Recent.Add(crime);
            if (Recent.Count > 100) Recent.RemoveAt(0);
            string where = body >= 0 && body < world.Catalog.Count ? " near " + world.Catalog[body].Name : "";
            world.Log(jd, $"{poster.Name} posts {Cr(bounty)} cr on {offender.Name} for {Describe(kind)}{where}, seen by {witness}", WorldEventKind.Law, body);
            if (offender.IsPlayer)
                Notice($"CRIME: {Describe(kind)}, seen by {witness}. {poster.Name} posts a bounty of {Cr(bounty)} cr (now {Cr(offender.BountyBy(poster))})");
            Committed?.Invoke(crime);
            return crime;
        }

        /// <summary>Pays off the bounty <paramref name="f"/> has posted on the player, from the purse of the ship
        /// they fly: the bounty is cleared and standing is restored as far as docking there again.
        /// False (nothing paid) if there is no bounty or the purse cannot cover it.</summary>
        public bool PayFine(Ship ship, Faction f, double jd, out double paid)
        {
            paid = Player.BountyBy(f);
            if (ship?.Faction == null || paid <= 0 || ship.Faction.Treasury < paid) { paid = 0; return false; }
            ship.Faction.Treasury -= paid;
            if (f != ship.Faction) f.Treasury += paid; // a fine to one's own faction is burned as court costs (U.SPC-T389)
            ClearBounty(Player, f);
            if (Player.StandingWith(f) < DockMinStanding) Player.Standing[f] = DockMinStanding;
            if (Hail != null && Hail.By == f) Hail = null;
            scannedUntil[f] = jd + ScanCooldownDays;
            world.Log(jd, $"The {ship.Name} pays {Cr(paid)} cr in fines to {f.Name}", WorldEventKind.Law, ship.Orbit?.Body ?? -1);
            return true;
        }

        internal static void ClearBounty(LawRecord r, Faction f)
        {
            r.Bounty.Remove(f);
            r.BountyUntil.Remove(f);
        }

        /// <summary>Daily bookkeeping: notoriety fades and bounties nothing has renewed lapse.</summary>
        public void Update(double jd)
        {
            if (double.IsNaN(lastJd)) { lastJd = jd; return; }
            double dt = jd - lastJd;
            if (dt <= 0) return;
            lastJd = jd;
            double fade = Math.Pow(0.5, dt / NotorietyHalfLifeDays);
            Fade(Player, fade, jd);
            foreach (var r in Captains.Values) Fade(r, fade, jd);
            PruneCaptains();
        }

        /// <summary>Drops a captain's record once nothing more can come of it: no bounty outstanding, notoriety
        /// faded to nothing, and the flagship it was opened for gone for good (destroyed, or through a gate) --
        /// so nothing will ever look the record up by that ship again. Keeps the books from growing forever over
        /// a long game (U.SPC-T321).</summary>
        void PruneCaptains()
        {
            List<Ship> stale = null;
            foreach (var kv in Captains)
            {
                var ship = kv.Key;
                var r = kv.Value;
                if (r.Bounty.Count > 0 || r.Notoriety > 0.01) continue;
                if (ship != null && !ship.Destroyed && !ship.GateDeparted) continue;
                (stale ??= new List<Ship>()).Add(ship);
            }
            if (stale != null) foreach (var s in stale) Captains.Remove(s);
        }

        static void Fade(LawRecord r, double fade, double jd)
        {
            r.Notoriety *= fade;
            if (r.BountyUntil.Count == 0) return;
            List<Faction> lapsed = null;
            foreach (var kv in r.BountyUntil)
                if (jd >= kv.Value) (lapsed ??= new List<Faction>()).Add(kv.Key);
            if (lapsed != null) foreach (var f in lapsed) ClearBounty(r, f);
        }

        // ---------------------------------------------------------------- battles

        /// <summary>
        /// A battle has ended: whoever attacked lawful ships in it answers for it, if anyone saw. On each side, every
        /// commander (the player for a hull they flew, else each group's captain) on the side that started it whose
        /// foes included lawful units -- not pirates, not a faction it is at war with, not ships wanted in that
        /// jurisdiction -- has committed assault, or piracy if it is a pirate or took or ransomed one of them (pirates
        /// and robbers answer for it whichever side they were on; a lawful ship defending itself does not). A survivor among those it attacked
        /// is a witness; failing that, any ship or station in sensor range. The side enforcing the law in an
        /// interdiction commits nothing, and a wanted ship it stopped is charged only if it destroyed or took one of
        /// the patrol's ships. A wanted commander killed or taken by bounty hunters or the player is theirs to collect on, at
        /// the dead rate for a kill and in full alive (see <see cref="Outlaws.ClaimInBattle"/>); by a faction that posted a bounty on it, it has paid that one
        /// (a dead NPC captain's bounties all lapse).
        /// </summary>
        public void OnBattleEnded(Battle b, double jd)
        {
            if (b == null || b.Units.Count == 0) return;
            var where = BattleHelio(b, jd);
            var juris = b.Site != null ? JurisdictionOf(b.Site.Hop) : JurisdictionAt(b.Body);
            // News of it names no body for a lane fight, nor for one over a haven nobody has found (it would give the rock away).
            int shownBody = b.Site != null || HidesHaven(b.Body) ? -1 : b.Body;
            var inBattle = new HashSet<Ship>();
            foreach (var u in b.Units) if (u.Ship != null) inBattle.Add(u.Ship);

            for (int side = 0; side < 2; side++)
            {
                if (side == b.EnforcingSide) continue;
                // Each commander once: the player (keyed by null) for a hull they flew, else the group's captain.
                var offenders = new Dictionary<object, Faction>();
                foreach (var u in b.Units)
                {
                    if (u.Side != side || u.Kind != UnitKind.Ship || u.Ship == null) continue;
                    // A prize taken by this side is not one of its fighters: its own captain (taken with it) did nothing here.
                    if (u.Captured && u.FoughtOn != side) continue;
                    object key = u.PlayerFlown ? (object)Player : u.Ship.Controller;
                    if (!offenders.ContainsKey(key)) offenders[key] = u.Faction;
                }
                foreach (var kv in offenders)
                {
                    var by = kv.Value;
                    var victims = LawfulVictims(b, 1 - side, by, juris);
                    if (victims.Count == 0) continue;
                    bool harmed = false, robbed = false;
                    foreach (var v in victims)
                    {
                        // A captured hull now sits on its captor's side: taken by this side, it was robbed; taken by
                        // a third party fighting alongside it (pirates who took a merchant the offenders were
                        // defending), it is not this side's doing.
                        bool takenHere = v.Captured && v.Side == side;
                        harmed |= v.Dead || takenHere;
                        robbed |= takenHere || v.SurrenderOutcome == SurrenderTerms.RansomCargo
                                  || (v.SurrenderOutcome == SurrenderTerms.TakeShip && (!v.Captured || takenHere));
                    }
                    bool pirate = by != null && by.IsPirate;
                    // A wanted ship stopped by the law answers only for hurting the patrol; a lawful ship that was
                    // attacked and fought back did nothing wrong (unless it turned robber).
                    // Nor did a third party that only fought alongside the side that started it (a bounty hunter joining
                    // a navy against raiders, with that navy's enemies on the raiders' side), unless anyone was hurt.
                    if (b.EnforcingSide == 1 - side) { if (!harmed) continue; }
                    else if ((side != b.AggressorSide || (by != b.SideLeaders[side] && !harmed)) && !pirate && !robbed) continue;
                    var rec = kv.Key as LawRecord ?? RecordOfCaptain((Ship)kv.Key);
                    // The one they attacked who lived to tell it, else anyone close enough to see.
                    string witness = null;
                    foreach (var v in victims)
                        if (!v.Dead && !v.Captured)
                        {
                            witness = v.Ship != null ? "the " + v.Ship.Name : v.Platform != null ? v.Platform.Name : v.Fort != null ? v.Fort.Name : null;
                            if (witness != null) break;
                        }
                    witness ??= FindWitness(where, jd, s => inBattle.Contains(s));
                    // The side's leader, if its own ships were among those wronged (not a power the offender is at war with, fighting beside them).
                    var leader = b.SideLeaders[1 - side];
                    var victim = leader != null && !leader.IsPirate && victims.Exists(v => v.Faction == leader) ? leader : victims[0].Faction;
                    var kind = pirate || robbed ? CrimeKind.Piracy : CrimeKind.Assault;
                    Commit(rec, kind, juris, victim, shownBody, jd, witness, b.Name);
                }
            }

            // Bounties paid in full: a wanted commander killed or taken by those who posted them.
            foreach (var u in b.Units)
            {
                if (u.Kind != UnitKind.Ship || u.Ship == null || !(u.Dead || u.Captured)) continue;
                bool player = u.PlayerFlown;
                // An escort answers only for a wanted captain of its own aboard (a band merged into another group).
                var rec = player ? Player : u.Ship.IsEscort ? world?.Outlaws?.RecordCommanding(u.Ship) : FindRecord(u.Ship);
                if (rec == null || !rec.Wanted) continue;
                // Alive or dead (see Outlaws.ClaimInBattle): a captain taken goes in a brig to be handed over for the
                // full price, one who got away in the pods is still wanted, and a kill pays hunters (or, on a scanned
                // wreck, the player) the dead rate.
                if (world?.Outlaws != null && world.Outlaws.ClaimInBattle(b, u, rec, jd)) continue;
                if (!player && u.Dead)
                {
                    var claimed = new List<Faction>(rec.Bounty.Keys);
                    foreach (var f in claimed) ClearBounty(rec, f);
                    world.Log(jd, $"{rec.Name} dies with the {u.Ship.Name}: the bounties on them lapse", WorldEventKind.Law, shownBody);
                    continue;
                }
                var takers = new HashSet<Faction>();
                foreach (var o in b.Units)
                    if (o.Side == 1 - u.FoughtOn && o.Faction != null) takers.Add(o.Faction);
                foreach (var f in takers)
                {
                    if (!rec.WantedBy(f)) continue;
                    ClearBounty(rec, f);
                    world.Log(jd, $"{f.Name} collects its bounty on {rec.Name}", WorldEventKind.Law, shownBody);
                    if (player) Notice($"{f.Name} has collected its bounty on you with the {u.Ship.Name}");
                }
            }
        }

        bool HidesHaven(int body)
        {
            foreach (var s in world.Economy.At(body))
                if (!s.Discovered) return true;
            return false;
        }

        /// <summary>The record of the captain commanding a ship in battle: its group leader's, opened if need be.</summary>
        LawRecord RecordOfCaptain(Ship ship)
        {
            var captain = ship.Controller;
            if (!Captains.TryGetValue(captain, out var r))
                Captains[captain] = r = new LawRecord(CaptainName(captain), captain);
            return r;
        }

        /// <summary>The units on <paramref name="side"/> that <paramref name="by"/> had no right to fight: lawful
        /// (not pirates), not at war with it, not of its own faction and not wanted in <paramref name="juris"/>.</summary>
        List<BattleUnit> LawfulVictims(Battle b, int side, Faction by, Faction juris)
        {
            var list = new List<BattleUnit>();
            foreach (var v in b.Units)
            {
                // By the side it fought on: a hull taken by boarders has moved to its captor's side.
                if (v.FoughtOn != side || v.Kind == UnitKind.Fighter || v.Faction == null || v.Faction.IsPirate || v.Faction == by) continue;
                if (by != null && world.Diplomacy.Get(by, v.Faction) == Relation.War) continue;
                var rec = v.PlayerFlown ? Player : v.Ship != null ? FindRecord(v.Ship) : null;
                if (rec != null && (juris != null ? rec.WantedBy(juris) : rec.Wanted)) continue;
                list.Add(v);
            }
            return list;
        }

        /// <summary>Where a battle is fought, heliocentric km: beside its lane, or off its body.</summary>
        public DVec3 BattleHelio(Battle b, double jd)
        {
            var lanes = world.Fleet.Lanes;
            if (b.Site == null || lanes == null) return world.Ephemeris.HelioAt(b.Body, jd) + b.Anchor;
            var hop = b.Site.Hop;
            var a = world.Ephemeris.HelioAt(hop.From, jd) + b.Site.FromOffset;
            var c = world.Ephemeris.HelioAt(hop.To, jd) + b.Site.ToOffset;
            return lanes.CurveKm(hop.Lane, a, c, b.Site.Progress, jd) + b.Site.Side;
        }

        // ---------------------------------------------------------------- the player's ship and the patrols

        /// <summary>The contraband a patrol of <paramref name="f"/> would find in a ship's grid hold: stolen goods
        /// (bought at a pirate haven) and any good <paramref name="f"/> bans (<see cref="Faction.Contraband"/>).</summary>
        public static List<CargoItem> ContrabandIn(Ship ship, Faction f)
        {
            var found = new List<CargoItem>();
            if (ship == null) return found;
            foreach (var item in ship.PersonalHold.Items)
                if (item.Stolen || (f != null && f.Contraband.Contains(item.Good))) found.Add(item);
            return found;
        }

        /// <summary>
        /// Advances the law around the player's ship, flown at <paramref name="offsetKm"/> from <paramref name="body"/>:
        /// trespass while barred from the port it is over; and the patrols. A navy patrol (a group's flagship, not at
        /// war with the ship) within <see cref="PatrolHailKm"/> hails it: if its faction wants the player, to heave to
        /// and pay the bounty within <see cref="HailSeconds"/> or be interdicted; otherwise, in its own jurisdiction,
        /// to hold still for a <see cref="ScanSeconds"/> scan. Running past <see cref="PatrolHoldKm"/> before either is
        /// done is fleeing a scan; a scan that finds contraband confiscates it and charges smuggling.
        /// </summary>
        public void UpdatePilot(Ship ship, int body, DVec3 offsetKm, DVec3 forward, double jd)
        {
            lastJd = double.IsNaN(lastJd) ? jd : Math.Max(lastJd, jd);
            if (ship == null || ship.Destroyed || ship.Battle != null)
            {
                Hail = null;
                return;
            }
            var helio = world.Ephemeris.HelioAt(body, jd) + offsetKm;
            CheckTrespass(ship, body, offsetKm, jd);
            if (Hail != null)
            {
                AnswerHail(ship, body, offsetKm, forward, helio, jd);
                return;
            }
            foreach (var p in world.Fleet.Ships)
            {
                if (!IsPatrol(p, ship)) continue;
                var f = p.Faction;
                if (scannedUntil.TryGetValue(f, out var until) && jd < until) continue;
                bool wanted = Player.WantedBy(f);
                // Scans are routine only in a patrol's own space; and a navy does not stop its own warships.
                if (!wanted && (JurisdictionAt(body) != f || (ship.Faction == f && ship.Spec.IsMilitary))) continue;
                double range = (world.Fleet.StateOf(p, jd).HelioKm - helio).Length;
                if (range > PatrolHailKm) continue;
                // It has to make the ship out first: a hull running silent can slip past a patrol that only holds a blip.
                if (PatrolSees(p, ship, range, jd) < ContactLevel.Class) continue;
                Hail = new PatrolHail
                {
                    Patrol = p, By = f, Interdiction = wanted, StartJd = jd, Fine = Player.BountyBy(f),
                    DueJd = jd + (wanted ? HailSeconds : ScanSeconds) / Astro.SecondsPerDay,
                };
                Notice(wanted
                    ? $"{f.Name} patrol {p.Name}: \"{ship.Name}, you are wanted. Heave to and pay {Cr(Hail.Fine)} cr, or we open fire.\""
                    : $"{f.Name} patrol {p.Name}: \"{ship.Name}, hold your position for a cargo scan.\"");
                return;
            }
        }

        /// <summary>What patrol <paramref name="p"/>'s group makes of the player's <paramref name="ship"/> at <paramref name="rangeKm"/>,
        /// by its signature as the helm last reported it (drive lit or not; silent running or not).</summary>
        ContactLevel PatrolSees(Ship p, Ship ship, double rangeKm, double jd)
        {
            var sensors = world.Sensors;
            if (sensors == null) return ContactLevel.Identified;
            bool burning = ship == world.PilotedShip ? sensors.PilotBurning : sensors.Burning(ship, jd);
            return Sensors.Resolve(sensors.GroupSuite(p), Signatures.Of(ship, burning), rangeKm, Signatures.Transponder(ship));
        }

        bool IsPatrol(Ship p, Ship player) =>
            p != player && !p.Destroyed && !p.GateDeparted && !p.IsEscort && !p.Derelict && p.Battle == null && !p.Planetside
            && p.Spec.IsMilitary && p.Faction != null && p.Faction.IsMilitary && !p.Faction.IsPirate && p.Combat != null && p.Combat.Armed
            && !world.Diplomacy.Hostile(p.Faction, player.Faction);

        void AnswerHail(Ship ship, int body, DVec3 offsetKm, DVec3 forward, DVec3 helio, double jd)
        {
            var h = Hail;
            var p = h.Patrol;
            var f = h.By;
            if (p.Destroyed || p.Battle != null || world.Diplomacy.Hostile(f, ship.Faction))
            {
                Hail = null;
                return;
            }
            if (h.Interdiction && !Player.WantedBy(f))
            {
                Hail = null;
                return;
            }
            double d = (world.Fleet.StateOf(p, jd).HelioKm - helio).Length;
            // Run out of range, or gone dark under its nose: either way the patrol saw it go.
            if (d > PatrolHoldKm || PatrolSees(p, ship, d, jd) == ContactLevel.None)
            {
                Hail = null;
                scannedUntil[f] = jd + ScanCooldownDays;
                Commit(Player, CrimeKind.FleeingScan, f, f, body, jd, "the " + p.Name, h.Interdiction ? "ran from an interdiction" : "ran from a scan");
                return;
            }
            if (jd < h.DueJd) return;
            Hail = null;
            scannedUntil[f] = jd + ScanCooldownDays;
            if (h.Interdiction)
            {
                var b = world.War?.TryStartInterdiction(ship, p, body, offsetKm, forward, jd);
                Notice(b != null ? $"{f.Name} patrol {p.Name} moves to interdict you" : $"{f.Name} patrol {p.Name} lets you go for now");
                return;
            }
            var contraband = ContrabandIn(ship, f);
            if (contraband.Count == 0)
            {
                Notice($"{f.Name} patrol {p.Name}: \"Scan clean. Safe travels, {ship.Name}.\"");
                return;
            }
            var goods = new HashSet<string>();
            foreach (var item in contraband)
            {
                ship.PersonalHold.Remove(item);
                goods.Add(GoodInfo.Of(item.Good).Name.ToLowerInvariant());
            }
            Notice($"{f.Name} patrol {p.Name} finds contraband and confiscates {contraband.Count} crate{(contraband.Count == 1 ? "" : "s")} ({string.Join(", ", goods)})");
            Commit(Player, CrimeKind.Smuggling, JurisdictionAt(body) ?? f, f, body, jd, "the " + p.Name, string.Join(", ", goods));
        }

        /// <summary>Over a port barred to the player (standing too low, not an enemy at war): trespass, seen by the port.</summary>
        void CheckTrespass(Ship ship, int body, DVec3 offsetKm, double jd)
        {
            if (body < 0 || body >= world.Catalog.Count || offsetKm.Length > ScaleModel.NeighbourhoodKm(world.Catalog[body])) return;
            Settlement port = null;
            foreach (var s in world.Economy.At(body))
                if (!s.Fallen && s.Owner != null && !s.Owner.IsPirate && (port == null || s.Population > port.Population)) port = s;
            if (port == null) return;
            var f = port.Owner;
            if (world.Diplomacy.Hostile(f, ship.Faction) || Player.StandingWith(f) >= DockMinStanding) return;
            if (trespassUntil.TryGetValue(f, out var until) && jd < until) return;
            trespassUntil[f] = jd + TrespassCooldownDays;
            Commit(Player, CrimeKind.Trespass, f, f, body, jd, port.Name, "barred from " + port.Name);
        }

        /// <summary>Records a message for the player (shown as a toast by the HUD).</summary>
        public void Notice(string text)
        {
            notices.Add(text);
            if (notices.Count > 20) notices.RemoveAt(0);
        }

        /// <summary>The player's messages since the last call, oldest first.</summary>
        public List<string> TakeNotices()
        {
            var list = new List<string>(notices);
            notices.Clear();
            return list;
        }

        static string Cr(double v) => v.ToString("N0", CultureInfo.InvariantCulture);
    }
}
