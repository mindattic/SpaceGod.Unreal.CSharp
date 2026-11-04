using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>How much an observer knows about a contact (U.SPC-T66).</summary>
    public enum ContactLevel
    {
        /// <summary>Not detected at all.</summary>
        None,

        /// <summary>Something is there: a heat or EM source, nothing more.</summary>
        Blip,

        /// <summary>Its hull class is resolved (a destroyer, a freighter), but not whose it is or its name.</summary>
        Class,

        /// <summary>Identified: name and faction (by transponder, or by a close enough look).</summary>
        Identified,
    }

    /// <summary>
    /// What a ship gives off, in signature units (1 = a 9,000 t freighter's hull coasting with its
    /// transponder off). Waste heat scales with hull surface (mass^(2/3)); active systems (fire-control
    /// radar, shields up) add EM; a drive burn adds a plume that scales with thrust; weapons fire is very
    /// bright. Silent running dims the hull's heat to a tenth and kills the EM, but cannot hide a burn.
    /// A hulk is cold; a ship on the ground is hidden from space altogether. Detection range goes as the
    /// square root of signature (inverse-square fall-off): see <see cref="Sensors.Resolve"/>.
    /// </summary>
    public static class Signatures
    {
        /// <summary>The hull mass whose coasting heat is one signature unit, tonnes (a freighter).</summary>
        public const double ReferenceMassTonnes = 9000;

        /// <summary>What a burn adds for a reference hull at 0.1 g, scaled by thrust (mass times acceleration).</summary>
        public const double DriveFactor = 40;

        /// <summary>A warship's active systems (fire control, shields up) and a civilian's lesser ones.</summary>
        public const double MilitaryEm = 1, CivilEm = 0.2;

        /// <summary>Guns, lasers and missile motors in a fight.</summary>
        public const double WeaponsFire = 24;

        /// <summary>What silent running leaves of a hull's heat (EM goes to nothing).</summary>
        public const double SilentFactor = 0.08;

        /// <summary>What a derelict hulk still gives off of its hull heat.</summary>
        public const double HulkFactor = 0.03;

        /// <summary>A battle seen from outside it (weapons fire over a hull): the reference for witnesses.</summary>
        public const double Battle = 25;

        /// <summary>A hidden haven: reactors and docks buried in a rock, but still warm.</summary>
        public const double Haven = 25;

        /// <summary>
        /// Running dark: the ship's own choice (<see cref="Ship.SilentRunning"/>), or raiders lying in wait
        /// beside a lane, who always do.
        /// </summary>
        public static bool Silent(Ship s) => s != null && (s.SilentRunning || (s.Faction != null && s.Faction.IsPirate && s.Controller.Ambushing));

        /// <summary>Squawking its identity: lawful ships do, unless running silent; pirates and hulks never.</summary>
        public static bool Transponder(Ship s) =>
            s != null && s.Faction != null && !s.Faction.IsPirate && !s.Derelict && !Silent(s);

        /// <summary>The signature of <paramref name="s"/> now: <paramref name="burning"/> under its drive,
        /// <paramref name="firing"/> its weapons (by default, whenever it is in a battle).</summary>
        public static double Of(Ship s, bool burning, bool? firing = null)
        {
            if (s == null || s.Destroyed || s.GateDeparted || s.Controller.Planetside) return 0;
            double mass = Math.Max(1, s.DryMassTonnes);
            double ratio = mass / ReferenceMassTonnes;
            double hull = Math.Pow(ratio, 2.0 / 3.0);
            if (s.Derelict) return hull * HulkFactor;
            bool silent = Silent(s);
            double em = silent ? 0 : s.Spec.IsMilitary && (s.Combat == null || s.Combat.Armed) ? MilitaryEm : CivilEm;
            if (silent) hull *= SilentFactor;
            double drive = burning ? DriveFactor * ratio * s.Spec.AccelG / 0.1 : 0;
            double weapons = (firing ?? s.Battle != null) ? WeaponsFire : 0;
            return hull + em + drive + weapons;
        }
    }

    /// <summary>A scanner fit: how far it sees a one-unit signature, km, and how finely it resolves what it sees.</summary>
    public readonly struct SensorSuite
    {
        public readonly double RangeKm;
        public readonly double Resolution;

        public SensorSuite(double rangeKm, double resolution)
        {
            RangeKm = rangeKm;
            Resolution = resolution;
        }

        /// <summary>A civilian hull's navigation sensors, a warship's search and fire-control suite, and a
        /// settlement's traffic control.</summary>
        public static readonly SensorSuite Civil = new SensorSuite(2e5, 1), Military = new SensorSuite(4e5, 1.5), Station = new SensorSuite(6e5, 2);

        /// <summary>What each scanner suite fitted (<see cref="ModuleKind.Scanner"/>) adds: half the base range again, and resolution.</summary>
        public const double ScannerRangeShare = 0.5, ScannerResolution = 0.5;

        /// <summary>A gate picket's long-range search suite (U.SPC-T480): six times a warship's reach and twice its
        /// resolution, so it makes out a raider lying dark off its gate and an arrival's class as it comes through.</summary>
        public static readonly SensorSuite Picket = new SensorSuite(2.4e6, 3);

        /// <summary>A corvette's patrol search suite (U.SPC-T476): twice a warship's reach and a finer look, to pick out the
        /// smugglers and raiders working its beat (and a hull running silent past it) before they are gone.</summary>
        public static readonly SensorSuite Patrol = new SensorSuite(8e5, 2.25);

        /// <summary>Hull classes built round a suite of their own rather than the civil or military base (the extension
        /// point: register a class's suite, see <see cref="RegisterBase"/>).</summary>
        static readonly Dictionary<ShipClass, SensorSuite> baseByClass = new Dictionary<ShipClass, SensorSuite>
        {
            [ShipClass.GatePicket] = Picket,
            [ShipClass.Corvette] = Patrol,
        };

        /// <summary>Gives hulls of <paramref name="cls"/> <paramref name="suite"/> as their base suite.</summary>
        public static void RegisterBase(ShipClass cls, SensorSuite suite) => baseByClass[cls] = suite;

        /// <summary>The suite a hull of <paramref name="cls"/> is built with, before any scanner is fitted.</summary>
        public static SensorSuite BaseOf(ShipClass cls, bool military) =>
            baseByClass.TryGetValue(cls, out var b) ? b : military ? Military : Civil;

        /// <summary>What a command ship's sensor array (<see cref="FacilityKinds.SensorArray"/>, U.SPC-T477) is worth in
        /// scanner suites.</summary>
        public const double ArrayScanners = 3;

        /// <summary>The suite <paramref name="s"/> carries: its hull's base, more with each scanner fitted, much more
        /// with a sensor array inside it, and more again with recon fighters aboard to scout ahead (U.SPC-T485).</summary>
        public static SensorSuite Of(Ship s)
        {
            if (s == null) return Civil;
            var b = s.Spec == null ? Civil : BaseOf(s.Spec.Class, s.Spec.IsMilitary);
            double n = (s.Loadout?.Count(ModuleKind.Scanner) ?? 0) + ArrayScanners * FleetCoordination.Count(s, FacilityKinds.SensorArray)
                       + ElectronicWarfare.SensorScanners * ElectronicWarfare.ReconAboard(s);
            return new SensorSuite(b.RangeKm * (1 + ScannerRangeShare * n), b.Resolution + ScannerResolution * n);
        }

        /// <summary>The better of two suites, term by term (a group sees with its best scanners).</summary>
        public static SensorSuite Best(SensorSuite a, SensorSuite b) => new SensorSuite(Math.Max(a.RangeKm, b.RangeKm), Math.Max(a.Resolution, b.Resolution));

        /// <summary>How far this suite sees a signature, km.</summary>
        public double ReachKm(double signature) => signature > 0 ? RangeKm * Math.Sqrt(signature) : 0;
    }

    /// <summary>
    /// One look at a contact (U.SPC-T100): when the sweep was made, when the light it saw had left the contact (as long
    /// before as light takes to cross the gap), where the contact was then, where the observer was, and the world it was
    /// parked at (-1 in flight). Word of it goes on from the observer at light speed (<see cref="ArrivesAt"/>).
    /// </summary>
    public readonly struct ContactSighting
    {
        public readonly double ObservedJd, SeenJd;
        public readonly DVec3 HelioKm, ObserverHelioKm;
        public readonly int Body;
        public readonly ContactLevel Level;

        public ContactSighting(double observedJd, double seenJd, DVec3 helio, DVec3 observer, int body, ContactLevel level)
        {
            ObservedJd = observedJd;
            SeenJd = seenJd;
            HelioKm = helio;
            ObserverHelioKm = observer;
            Body = body;
            Level = level;
        }

        /// <summary>The light delay the observer saw it with, seconds.</summary>
        public double LagSeconds => (ObservedJd - SeenJd) * Astro.SecondsPerDay;

        /// <summary>When word of this sighting, radioed on from the observer, reaches <paramref name="where"/>.</summary>
        public double ArrivesAt(DVec3 where) => ObservedJd + LightSpeed.DelayDays(ObserverHelioKm, where);
    }

    /// <summary>One track in a picture: what is (or was last) known of a ship, and where it was seen.</summary>
    public sealed class Contact
    {
        public Ship Ship;

        /// <summary>The best the track has been resolved to (held while it is cold, until it is lost).</summary>
        public ContactLevel Level;

        /// <summary>What the last sweep saw of it: <see cref="ContactLevel.None"/> once it has gone cold.</summary>
        public ContactLevel Now;

        public double FirstSeenJd, LastSeenJd;

        /// <summary>Where it was last seen, heliocentric km (where it was when the light left it, not where it is now),
        /// and how bright it was.</summary>
        public DVec3 LastHelioKm;
        public double Signature;

        /// <summary>When the light of the last look left it: <see cref="LastSeenJd"/> less the light time across the gap.</summary>
        public double SeenJd;

        /// <summary>The light delay of the last look, seconds (U.SPC-T100).</summary>
        public double LagSeconds => (LastSeenJd - SeenJd) * Astro.SecondsPerDay;

        /// <summary>The latest looks at it, oldest first (at most <see cref="ContactBook.SightingsKept"/>): what a fleet
        /// far from the observers knows is an older one, until word of the newer has crossed to it.</summary>
        public readonly List<ContactSighting> Sightings = new List<ContactSighting>();

        /// <summary>Not seen on the last sweep: a track going stale, lost after the picture's hold time.</summary>
        public bool Cold => Now == ContactLevel.None;

        /// <summary>What an observer would call it: "unknown contact", its class, or its name.</summary>
        public string Label => Describe(Ship, Level);

        public static string Describe(Ship s, ContactLevel level)
        {
            switch (level)
            {
                case ContactLevel.Identified: return s.Name;
                case ContactLevel.Class: return "unidentified " + s.Spec.DisplayName.ToLowerInvariant();
                case ContactLevel.Blip: return "unknown contact";
                default: return "nothing";
            }
        }
    }

    /// <summary>A picture: every contact one observer (a faction's ships and stations, or the player's own ship) holds.</summary>
    public sealed class ContactBook
    {
        readonly Dictionary<Ship, Contact> contacts = new Dictionary<Ship, Contact>();

        public int Count => contacts.Count;
        public IEnumerable<Contact> Contacts => contacts.Values;

        public Contact Of(Ship s) => s != null && contacts.TryGetValue(s, out var c) ? c : null;

        /// <summary>How many looks each track keeps: at a sweep every 0.1 day, more than two days of them, longer
        /// than light takes across any system.</summary>
        public const int SightingsKept = 24;

        /// <summary>
        /// What is known of <paramref name="s"/> at <paramref name="where"/> at <paramref name="jd"/> (U.SPC-T100): the
        /// newest look whose word, radioed on from the observer at light speed, has had time to get there; null if none has.
        /// </summary>
        public ContactSighting? KnownAt(Ship s, DVec3 where, double jd)
        {
            var c = Of(s);
            if (c == null) return null;
            for (int i = c.Sightings.Count - 1; i >= 0; i--)
                if (c.Sightings[i].ArrivesAt(where) <= jd) return c.Sightings[i];
            return null;
        }

        /// <summary>What is known of <paramref name="s"/>: its track's level, or nothing.</summary>
        public ContactLevel LevelOf(Ship s) => Of(s)?.Level ?? ContactLevel.None;

        /// <summary>Counts the live (not cold) tracks at <paramref name="level"/>.</summary>
        public int CountLive(ContactLevel level)
        {
            int n = 0;
            foreach (var c in contacts.Values)
                if (!c.Cold && c.Level == level) n++;
            return n;
        }

        /// <summary>A sweep saw <paramref name="s"/> at <paramref name="level"/> (None: did not see it).
        /// A track keeps what it had resolved while it is held, so a known ship that goes dark and is
        /// picked up again as a blip is still known.</summary>
        internal void Observe(Ship s, ContactLevel level, DVec3 helio, double signature, double jd) =>
            Observe(s, level, helio, signature, jd, helio, jd, -1);

        /// <summary>A look from <paramref name="observer"/> that saw <paramref name="s"/> at <paramref name="helio"/> by light
        /// that left it at <paramref name="seenJd"/>, parked at <paramref name="body"/> (-1 in flight).</summary>
        internal void Observe(Ship s, ContactLevel level, DVec3 helio, double signature, double jd, DVec3 observer, double seenJd, int body)
        {
            contacts.TryGetValue(s, out var c);
            if (level == ContactLevel.None)
            {
                if (c != null) c.Now = ContactLevel.None;
                return;
            }
            if (c == null) contacts[s] = c = new Contact { Ship = s, FirstSeenJd = jd };
            c.Now = level;
            if (level > c.Level) c.Level = level;
            c.LastSeenJd = jd;
            c.SeenJd = seenJd;
            c.LastHelioKm = helio;
            c.Signature = signature;
            if (c.Sightings.Count >= SightingsKept) c.Sightings.RemoveAt(0);
            c.Sightings.Add(new ContactSighting(jd, seenJd, helio, observer, body, level));
        }

        /// <summary>Drops the tracks cold for longer than <paramref name="holdDays"/>, and the ships gone for good.
        /// Returns the ships lost.</summary>
        internal List<Ship> Expire(double jd, double holdDays)
        {
            List<Ship> lost = null;
            foreach (var kv in contacts)
                // (A ship gone through a jump gate is never swept again, so its track would never go cold.)
                if (kv.Key.Destroyed || kv.Key.GateDeparted || (kv.Value.Cold && jd - kv.Value.LastSeenJd > holdDays)) (lost ??= new List<Ship>()).Add(kv.Key);
            if (lost != null) foreach (var s in lost) contacts.Remove(s);
            return lost;
        }

        public void Clear() => contacts.Clear();
    }

    /// <summary>
    /// Sensors, signatures and stealth (U.SPC-T66). Nobody, player or AI, simply knows where another ship is:
    /// every faction holds a picture built by its ships' and stations' scanners (<see cref="PictureOf"/>), swept
    /// every <see cref="SweepDays"/>, and the player's piloted ship holds its own (<see cref="Player"/>). A contact
    /// starts as an unknown blip, resolves to its hull class and then its identity as it closes (or at once, if it
    /// squawks a transponder), and is lost once it has been cold (out of reach) for the picture's hold time. What
    /// the pictures hold drives the rest: navies hunt only raiders they have seen and track them home to hidden
    /// havens, patrols hail only a ship they can make out (so silent running slips contraband past them), crimes
    /// are witnessed only by those who could see them, and a lane ambush catches only victims who did not spot
    /// the raiders lying dark beside it.
    /// </summary>
    public sealed class Sensors
    {
        readonly World world;
        readonly Dictionary<Faction, ContactBook> pictures = new Dictionary<Faction, ContactBook>();

        /// <summary>What the player's piloted ship's own scanners hold.</summary>
        public readonly ContactBook Player = new ContactBook();

        /// <summary>How often the factions' pictures are swept, days.</summary>
        public double SweepDays = 0.1;

        /// <summary>How long a faction's picture holds a cold track before it is lost, days.</summary>
        public double FactionHoldDays = 0.5;

        /// <summary>How often the player's picture is swept, and how long it holds a cold track, seconds.</summary>
        public double PlayerSweepSeconds = 1, PlayerHoldSeconds = 300;

        /// <summary>Resolve thresholds: how many times inside its detection reach a contact must be (divided by the
        /// suite's resolution) for its class, and for its identity, to be made out.</summary>
        public const double ClassQuality = 2.5, IdentifyQuality = 6;

        double nextSweepJd = double.NegativeInfinity, nextPlayerSweepJd = double.NegativeInfinity, lastSweepJd = double.NegativeInfinity;

        /// <summary>The piloted ship's own position and whether its drive is lit, as last reported by the helm.</summary>
        Ship pilotShip;
        DVec3 pilotHelio;
        bool pilotBurning;

        /// <summary>Raised when a sweep loses a track: (observer, ship). The observer is null for the player's picture.</summary>
        public event Action<Faction, Ship> Lost;

        public Sensors(World world) => this.world = world;

        /// <summary>The picture <paramref name="f"/>'s ships and stations share.</summary>
        public ContactBook PictureOf(Faction f)
        {
            if (f == null) return null;
            if (!pictures.TryGetValue(f, out var book)) pictures[f] = book = new ContactBook();
            return book;
        }

        // ---------------------------------------------------------------- detection

        /// <summary>
        /// What a suite makes of a signature at a distance: nothing beyond its reach (<see cref="SensorSuite.ReachKm"/>),
        /// else a blip, the class once it is <see cref="ClassQuality"/>/resolution times inside that reach, the identity
        /// at <see cref="IdentifyQuality"/>/resolution -- or straight away from a transponder.
        /// </summary>
        public static ContactLevel Resolve(SensorSuite suite, double signature, double distanceKm, bool transponder)
        {
            double reach = suite.ReachKm(signature);
            if (reach <= 0 || distanceKm > reach) return ContactLevel.None;
            if (transponder) return ContactLevel.Identified;
            double q = reach / Math.Max(distanceKm, 1e-3);
            double res = Math.Max(suite.Resolution, 1e-6);
            if (q >= IdentifyQuality / res) return ContactLevel.Identified;
            if (q >= ClassQuality / res) return ContactLevel.Class;
            return ContactLevel.Blip;
        }

        /// <summary>Whether the drive of <paramref name="s"/> is lit at <paramref name="jd"/>: the helm's word for the piloted ship,
        /// else its schedule (a burn between worlds; riding a lane or holding station is not one).</summary>
        public bool Burning(Ship s, double jd) => Burning(s, jd, out _);

        bool Burning(Ship s, double jd, out DVec3 helio)
        {
            if (s == pilotShip && s == world.PilotedShip)
            {
                helio = pilotHelio;
                return pilotBurning;
            }
            // A group running the player down is where the chase has it, its drive lit (U.SPC-T273).
            var chasing = world.Pursuit != null && world.Pursuit.Active ? world.Pursuit.HelioOf(s, jd) : null;
            if (chasing != null)
            {
                helio = chasing.Value;
                return true;
            }
            var st = world.Fleet.StateOf(s, jd);
            helio = st.HelioKm;
            var c = s.Controller;
            return st.Burning && c.Phase == ShipPhase.Transit;
        }

        /// <summary>Where <paramref name="s"/> is now: the helm's own reckoning for the piloted ship, else its schedule.</summary>
        public DVec3 HelioOf(Ship s, double jd)
        {
            Burning(s, jd, out var helio);
            return helio;
        }

        /// <summary>
        /// Records a look from <paramref name="observer"/> at <paramref name="s"/> in <paramref name="book"/> at
        /// <paramref name="jd"/>: the light it saw left the ship as long ago as light takes across the gap, so the track
        /// puts it where it was then (U.SPC-T100), at the world it was parked at, if any.
        /// </summary>
        void Record(ContactBook book, Ship s, ContactLevel level, DVec3 helioNow, DVec3 observer, double signature, double jd)
        {
            if (level == ContactLevel.None)
            {
                book.Observe(s, level, helioNow, signature, jd);
                return;
            }
            double lag = LightSpeed.DelayDays(helioNow, observer);
            double seen = jd - lag;
            var helio = helioNow;
            var c = s.Controller;
            if (lag > 0 && !(c == pilotShip && c == world.PilotedShip)) helio = world.Fleet.StateOf(c, seen).HelioKm;
            book.Observe(s, level, helio, signature, jd, observer, seen, ParkedAt(c));
        }

        /// <summary>The world <paramref name="s"/> is parked at (or down on), or -1 in flight.</summary>
        static int ParkedAt(Ship s) => (s.Phase == ShipPhase.Parked || s.Phase == ShipPhase.Landed) && s.Orbit != null ? s.Orbit.Body : -1;

        /// <summary>
        /// What <paramref name="f"/> knows of <paramref name="target"/> at <paramref name="where"/> at <paramref name="jd"/>
        /// (U.SPC-T100): the newest sighting in its picture whose word has crossed from the observer at light speed. A
        /// fleet across the system acts on a look minutes or hours old, and the ship may have moved on since. Its own
        /// ships it knows as they are.
        /// </summary>
        public ContactSighting? KnownAt(Faction f, Ship target, DVec3 where, double jd)
        {
            if (f == null || target == null) return null;
            if (target.Faction == f)
            {
                var h = HelioOf(target, jd);
                return new ContactSighting(jd, jd, h, h, ParkedAt(target.Controller), ContactLevel.Identified);
            }
            return PictureOf(f).KnownAt(target, where, jd);
        }

        /// <summary>The signature of <paramref name="s"/> at <paramref name="jd"/>.</summary>
        public double SignatureOf(Ship s, double jd) => Signatures.Of(s, Burning(s, jd));

        /// <summary>What the scanners of <paramref name="observer"/> (its group's best, for a flagship) make of
        /// <paramref name="target"/> right now.</summary>
        public ContactLevel Detect(Ship observer, Ship target, double jd)
        {
            if (observer == null || target == null || observer == target) return ContactLevel.None;
            double d = (HelioOf(observer, jd) - HelioOf(target, jd)).Length;
            return Resolve(GroupSuite(observer), SignatureOf(target, jd), d, Signatures.Transponder(target));
        }

        /// <summary>The best scanners in the group <paramref name="s"/> belongs to.</summary>
        public SensorSuite GroupSuite(Ship s)
        {
            var leader = s.Controller;
            var suite = SensorSuite.Of(leader);
            foreach (var e in world.Fleet.Ships)
                if (e.Leader == leader && !e.Destroyed) suite = SensorSuite.Best(suite, SensorSuite.Of(e));
            return suite;
        }

        /// <summary>
        /// What <paramref name="f"/> knows of <paramref name="target"/>: its picture's track if the last sweep saw it,
        /// else a look now from every ship and station it has (recorded in its picture), so nothing is missed between sweeps.
        /// </summary>
        public ContactLevel Knows(Faction f, Ship target, double jd)
        {
            if (f == null || target == null || target.Destroyed) return ContactLevel.None;
            if (target.Faction == f) return ContactLevel.Identified;
            var book = PictureOf(f);
            var c = book.Of(target);
            if (c != null && !c.Cold && c.LastSeenJd >= lastSweepJd && jd - c.LastSeenJd <= SweepDays * 1.5) return c.Level;
            bool burning = Burning(target, jd, out var helio);
            double sig = Signatures.Of(target, burning);
            bool squawk = Signatures.Transponder(target);
            var best = ContactLevel.None;
            var by = helio;
            double byKm2 = double.MaxValue;
            foreach (var o in Observers(jd))
            {
                if (o.Faction != f) continue;
                double d2 = (o.Helio - helio).LengthSquared;
                var level = Resolve(o.Suite, sig, Math.Sqrt(d2), squawk);
                // The best look, and of those the nearest: the one with the least light delay.
                if (level != ContactLevel.None && (level > best || (level == best && d2 < byKm2)))
                {
                    best = level;
                    by = o.Helio;
                    byKm2 = d2;
                }
            }
            if (best != ContactLevel.None) Record(book, target, best, helio, by, sig, jd);
            return book.LevelOf(target);
        }

        // ---------------------------------------------------------------- sweeps

        readonly struct Observer
        {
            public readonly Faction Faction;
            public readonly DVec3 Helio;
            public readonly SensorSuite Suite;

            public Observer(Faction f, DVec3 helio, SensorSuite suite)
            {
                Faction = f;
                Helio = helio;
                Suite = suite;
            }
        }

        readonly struct Target
        {
            public readonly Ship Ship;
            public readonly DVec3 Helio;
            public readonly double Signature, Root;
            public readonly bool Transponder;

            public Target(Ship s, DVec3 helio, double sig, bool squawk)
            {
                Ship = s;
                Helio = helio;
                Signature = sig;
                Root = Math.Sqrt(sig);
                Transponder = squawk;
            }
        }

        /// <summary>Every ship and station that can look: group flagships (with their escorts' best scanners),
        /// lone hulls, and every settlement with people and an owner.</summary>
        List<Observer> Observers(double jd)
        {
            var list = new List<Observer>();
            var suites = new Dictionary<Ship, SensorSuite>();
            foreach (var s in world.Fleet.Ships)
            {
                if (!Looks(s)) continue;
                var c = s.Controller;
                var suite = SensorSuite.Of(s);
                suites[c] = suites.TryGetValue(c, out var had) ? SensorSuite.Best(had, suite) : suite;
            }
            foreach (var kv in suites)
                if (Looks(kv.Key)) list.Add(new Observer(kv.Key.Faction, HelioOf(kv.Key, jd), kv.Value));
            foreach (var st in world.Economy.Settlements)
                if (!st.Fallen && st.Owner != null && st.Population > 0)
                    list.Add(new Observer(st.Owner, world.Ephemeris.HelioAt(st.Body, jd), SensorSuite.Station));
            return list;
        }

        static bool Looks(Ship s) => !s.Destroyed && !s.GateDeparted && !s.Derelict && s.Faction != null && !s.Controller.Planetside;

        /// <summary>Sweeps the factions' pictures when one is due.</summary>
        public void Update(double jd)
        {
            if (jd < nextSweepJd) return;
            Sweep(jd);
            nextSweepJd = jd + SweepDays;
        }

        /// <summary>Every faction's ships and stations look at every ship not their own, and the pictures are updated:
        /// tracks seen are refreshed, unseen ones go cold and, past <see cref="FactionHoldDays"/>, are lost. Then the
        /// navies follow any raider they hold on its way into, or parked at, a hidden haven: that gives the haven away.</summary>
        public void Sweep(double jd)
        {
            lastSweepJd = jd;
            var observers = Observers(jd);
            var factions = new HashSet<Faction>();
            // Each faction's own lookers, so a target is tried against them alone and not every faction's.
            var lookers = new Dictionary<Faction, List<Observer>>();
            foreach (var o in observers)
            {
                factions.Add(o.Faction);
                if (!lookers.TryGetValue(o.Faction, out var mine)) lookers[o.Faction] = mine = new List<Observer>();
                mine.Add(o);
            }
            // A faction left with nobody looking still lets its old tracks go cold and be lost.
            factions.UnionWith(pictures.Keys);
            var targets = new List<Target>();
            var stateOfLeader = new Dictionary<Ship, (DVec3 helio, bool burning)>();
            foreach (var s in world.Fleet.Ships)
            {
                if (s.Destroyed || s.GateDeparted || s.Faction == null) continue;
                var c = s.Controller;
                if (!stateOfLeader.TryGetValue(c, out var st))
                {
                    bool b = Burning(c, jd, out var h);
                    stateOfLeader[c] = st = (h, b);
                }
                double sig = Signatures.Of(s, st.burning);
                targets.Add(new Target(s, st.helio, sig, Signatures.Transponder(s)));
            }
            foreach (var f in factions)
            {
                var book = PictureOf(f);
                lookers.TryGetValue(f, out var mineList);
                foreach (var t in targets)
                {
                    if (t.Ship.Faction == f) continue;
                    var best = ContactLevel.None;
                    var by = t.Helio;
                    double byKm2 = double.MaxValue;
                    if (t.Signature > 0 && mineList != null)
                        foreach (var o in mineList)
                        {
                            double reach = o.Suite.RangeKm * t.Root;
                            double d2 = (o.Helio - t.Helio).LengthSquared;
                            if (d2 > reach * reach) continue;
                            var level = Resolve(o.Suite, t.Signature, Math.Sqrt(d2), t.Transponder);
                            // The best look, and of those the nearest: the one with the least light delay.
                            if (level != ContactLevel.None && (level > best || (level == best && d2 < byKm2)))
                            {
                                best = level;
                                by = o.Helio;
                                byKm2 = d2;
                            }
                        }
                    Record(book, t.Ship, best, t.Helio, by, t.Signature, jd);
                }
                var lost = book.Expire(jd, FactionHoldDays);
                if (lost != null && Lost != null) foreach (var s in lost) Lost(f, s);
            }
            TrackHome(jd);
        }

        /// <summary>A navy that holds a raider (by class at least: a warship running without a transponder) parked at,
        /// or burning in towards, a hidden haven's rock has followed pirate traffic home: the haven is found.</summary>
        void TrackHome(double jd)
        {
            var war = world.War;
            if (war == null) return;
            var hidden = new Dictionary<int, Settlement>();
            foreach (var st in world.Economy.Settlements)
                if (st.Hidden && !st.Fallen) hidden[st.Body] = st;
            if (hidden.Count == 0) return;
            foreach (var kv in pictures)
            {
                var f = kv.Key;
                if (!f.IsMilitary || f.IsPirate) continue;
                foreach (var c in kv.Value.Contacts)
                {
                    if (c.Cold || c.Level < ContactLevel.Class) continue;
                    var raider = c.Ship.Controller;
                    if (raider.Faction == null || !raider.Faction.IsPirate) continue;
                    int body = raider.Phase == ShipPhase.Parked ? raider.Orbit.Body : raider.Phase == ShipPhase.Transit ? raider.DestinationBody : -1;
                    if (body < 0 || !hidden.TryGetValue(body, out var haven) || haven.KnownTo.Contains(f)) continue;
                    string who = c.Level == ContactLevel.Identified ? "the " + raider.Name : "an unidentified " + raider.Spec.DisplayName.ToLowerInvariant();
                    war.Discover(haven, f, jd, raider.Phase == ShipPhase.Parked
                        ? $"its sensors hold {who} parked there"
                        : $"its sensors track {who} burning in to it");
                }
            }
        }

        /// <summary>
        /// The helm reports the piloted ship's own position and whether its drive is lit (the schedule does not
        /// know), and its own picture is swept every <see cref="PlayerSweepSeconds"/>: every other ship in reach
        /// of its scanners, resolved by how bright and how close it is; tracks cold past <see cref="PlayerHoldSeconds"/>
        /// are lost. Null (not piloting) clears the picture.
        /// </summary>
        public void UpdatePilot(Ship ship, DVec3 helioKm, bool burning, double jd)
        {
            if (ship == null || ship.Destroyed)
            {
                pilotShip = null;
                Player.Clear();
                return;
            }
            if (ship != pilotShip) Player.Clear();
            pilotShip = ship;
            pilotHelio = helioKm;
            pilotBurning = burning;
            if (jd < nextPlayerSweepJd && jd >= nextPlayerSweepJd - PlayerSweepSeconds * 2 / Astro.SecondsPerDay) return;
            nextPlayerSweepJd = jd + PlayerSweepSeconds / Astro.SecondsPerDay;
            var suite = SensorSuite.Of(ship);
            var seen = new Dictionary<Ship, (DVec3 helio, bool burning)>();
            foreach (var s in world.Fleet.Ships)
            {
                if (s == ship || s.Destroyed || s.GateDeparted || s.Faction == null) continue;
                var c = s.Controller;
                if (!seen.TryGetValue(c, out var st))
                {
                    bool b = Burning(c, jd, out var h);
                    seen[c] = st = (h, b);
                }
                double sig = Signatures.Of(s, st.burning);
                Record(Player, s, Resolve(suite, sig, (st.helio - helioKm).Length, Signatures.Transponder(s)), st.helio, helioKm, sig, jd);
            }
            var lost = Player.Expire(jd, PlayerHoldSeconds / Astro.SecondsPerDay);
            if (lost != null && Lost != null) foreach (var s in lost) Lost(null, s);
        }

        /// <summary>The piloted ship's own signature now, as the helm last reported it.</summary>
        public double PilotSignature => pilotShip != null ? Signatures.Of(pilotShip, pilotBurning) : 0;

        /// <summary>Whether the helm last reported the piloted ship's drive lit.</summary>
        public bool PilotBurning => pilotShip != null && pilotBurning;

        // ---------------------------------------------------------------- witnesses and havens

        /// <summary>
        /// The first lawful station or ship (not pirates, hulks, those on the ground, nor whatever <paramref name="exclude"/>
        /// rules out) whose scanners reach a <paramref name="signature"/> at <paramref name="helioKm"/>; null if none does.
        /// </summary>
        public string FindWitness(DVec3 helioKm, double jd, double signature, Func<Ship, bool> exclude = null)
        {
            double root = Math.Sqrt(Math.Max(0, signature));
            foreach (var s in world.Economy.Settlements)
            {
                if (s.Fallen || s.Owner == null || s.Owner.IsPirate || s.Population <= 0) continue;
                if ((world.Ephemeris.HelioAt(s.Body, jd) - helioKm).Length <= SensorSuite.Station.RangeKm * root) return s.Name;
            }
            foreach (var ship in world.Fleet.Ships)
            {
                if (ship.Destroyed || ship.GateDeparted || ship.Derelict || ship.Faction == null || ship.Faction.IsPirate || ship.Controller.Planetside) continue;
                if (ship == world.PilotedShip || (exclude != null && exclude(ship))) continue;
                if ((world.Fleet.StateOf(ship, jd).HelioKm - helioKm).Length <= SensorSuite.Of(ship).RangeKm * root) return "the " + ship.Name;
            }
            return null;
        }

        /// <summary>How far <paramref name="s"/>'s scanners pick out a hidden haven (<see cref="Signatures.Haven"/>), km.</summary>
        public static double HavenReachKm(Ship s) => SensorSuite.Of(s).ReachKm(Signatures.Haven);
    }
}
