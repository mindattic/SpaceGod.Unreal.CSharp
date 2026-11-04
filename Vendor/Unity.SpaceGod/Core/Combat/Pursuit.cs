using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>One hostile group running the player's ship down (see <see cref="Pursuit"/>).</summary>
    public sealed class Pursuer
    {
        public Ship Leader;
        public readonly List<Ship> Ships = new List<Ship>();

        /// <summary>The group's position and velocity, km and km/s from the pilot's frame body, ecliptic axes.</summary>
        public DVec3 Pos, Vel;

        /// <summary>Seconds since it made contact.</summary>
        public double Seconds;

        /// <summary>Seconds its interdiction EMP has been charging, or below 0 while it is not.</summary>
        public double EmpCharge = -1;

        /// <summary>Seconds until its EMP can charge again.</summary>
        public double EmpReadyIn;

        public Faction Faction => Leader.Faction;
    }

    /// <summary>What happened in a chase this step, for the helm to show and act on.</summary>
    public enum PursuitEvent
    {
        /// <summary>A group has seen the player and turned to run it down: it shows on the scanners.</summary>
        Contact,

        /// <summary>Its EMP is charging: the player has <see cref="Pursuit.EmpChargeSeconds"/> to get out of reach.</summary>
        EmpCharging,

        /// <summary>The EMP went off with the player inside it: drives and cruise down for <see cref="Pursuit.EmpStunSeconds"/>.</summary>
        EmpHit,

        /// <summary>The EMP went off with the player clear of it.</summary>
        EmpMissed,

        /// <summary>The group is close enough to fight: the battle starts here.</summary>
        Engage,

        /// <summary>The group has given the chase up and gone back to its business.</summary>
        GaveUp,
    }

    /// <summary>
    /// Hostiles that see the player's ship and run it down (U.SPC-T273). Every armed hostile group in the system whose
    /// scanners can make out the piloted ship may turn to chase it: it drops off its schedule and shows on the player's
    /// scanners at <see cref="ContactKm"/> on its real bearing, as if it had cruised in, then closes at up to
    /// <see cref="TopSpeedKmS"/> (faster than any hull's cruise, so a chase can be stretched but not outrun for ever).
    /// Within <see cref="EmpRangeKm"/> it charges an interdiction EMP that knocks the player's drives out if it goes off
    /// with the player still in reach, and within <see cref="EngageKm"/> the fight begins where the player is, anywhere
    /// in open space. A chase that runs too long or too far is given up and the group goes back to its schedule.
    /// </summary>
    public sealed class Pursuit
    {
        /// <summary>How far off a chasing group shows up, km, and how far off it gives up.</summary>
        public const double ContactKm = 320, GiveUpKm = 2500;

        /// <summary>A chasing group's top speed, km/s, and its acceleration, km/s².</summary>
        public const double TopSpeedKmS = 6, AccelKmS2 = 0.5;

        /// <summary>How near the EMP reaches when it charges, km (it must still be within a quarter more when it goes off),
        /// how long it charges, how long it knocks the drives out, and how long before it can charge again, seconds.</summary>
        public const double EmpRangeKm = 30, EmpChargeSeconds = 3, EmpStunSeconds = 7, EmpCooldownSeconds = 25;

        /// <summary>How near a chasing group must close to start the fight, km.</summary>
        public const double EngageKm = 15;

        /// <summary>How long a chase goes on before it is given up, seconds.</summary>
        public const double MaxChaseSeconds = 360;

        /// <summary>How far off a hostile can be to notice the player at all, km (its scanners must also make it out).</summary>
        public const double NoticeKm = 2e6;

        /// <summary>The most groups that chase the player at once.</summary>
        public const int MaxGroups = 2;

        /// <summary>Days before a group that has chased the player will do so again.</summary>
        public const double RechaseDays = 0.5;

        /// <summary>Spacing between a chasing group's ships, km.</summary>
        const double FormationKm = 0.6;

        readonly World world;
        readonly Random rng;
        readonly Dictionary<Ship, double> lastChased = new Dictionary<Ship, double>();

        public readonly List<Pursuer> Groups = new List<Pursuer>();

        /// <summary>The body whose frame the chase is reckoned in: the pilot's.</summary>
        public int Body = -1;

        public Pursuit(World world, int seed)
        {
            this.world = world;
            rng = new Random(seed);
        }

        public bool Active => Groups.Count > 0;

        /// <summary>The chasing group <paramref name="s"/> is in, or null.</summary>
        public Pursuer Of(Ship s)
        {
            foreach (var g in Groups)
                if (g.Ships.Contains(s)) return g;
            return null;
        }

        /// <summary>Where <paramref name="s"/> of chasing group <paramref name="g"/> is, km from the frame body: the leader at
        /// the group's position, the rest in loose formation about it.</summary>
        public static DVec3 OffsetOf(Pursuer g, Ship s) => s == g.Leader ? g.Pos : g.Pos + s.FormationSlot * FormationKm;

        /// <summary>Where a chasing ship is, heliocentric km, or null if it is not chasing.</summary>
        public DVec3? HelioOf(Ship s, double jd)
        {
            var g = Of(s);
            if (g == null || Body < 0) return null;
            return world.Ephemeris.HelioAt(Body, jd) + OffsetOf(g, s);
        }

        /// <summary>
        /// Looks for hostile groups that see the piloted ship and turn to run it down; each that does starts at
        /// <see cref="ContactKm"/> on its own bearing. Returns the groups that made contact.
        /// </summary>
        public List<Pursuer> Consider(Ship pilot, int body, DVec3 pilotOffset, DVec3 pilotVel, double jd)
        {
            var found = new List<Pursuer>();
            if (pilot == null || pilot.Destroyed || pilot.Battle != null || Groups.Count >= MaxGroups) return found;
            if (Body != body) ReleaseAll(jd);
            Body = body;
            PruneLastChased(jd);
            var sensors = world.Sensors;
            var pilotHelio = world.Ephemeris.HelioAt(body, jd) + pilotOffset;
            double signature = Signatures.Of(pilot, sensors?.PilotBurning ?? true);
            bool transponder = Signatures.Transponder(pilot);
            foreach (var s in world.Fleet.Ships)
            {
                // Begin adds each new group to Groups as well as found: count it once.
                if (Groups.Count >= MaxGroups) break;
                if (!MayChase(s, pilot, body, jd)) continue;
                var helio = world.Fleet.StateOf(s, jd).HelioKm;
                var to = helio - pilotHelio;
                double d = to.Length;
                if (d > NoticeKm) continue;
                var suite = sensors != null ? sensors.GroupSuite(s) : SensorSuite.Military;
                if (Sensors.Resolve(suite, signature, d, transponder) < ContactLevel.Class) continue;
                if (rng.NextDouble() > ChaseChance(s)) continue;
                var g = Begin(s, pilotOffset + (d > 1e-6 ? to / d : DVec3.UnitX) * ContactKm, pilotVel, jd);
                found.Add(g);
            }
            return found;
        }

        /// <summary>Raiders are the keenest to run a ship down; a navy at war with its flag only somewhat less.</summary>
        static double ChaseChance(Ship s) => s.Faction.IsPirate ? 0.35 : 0.25;

        /// <summary>Whether <paramref name="s"/> may turn to run the pilot down off <paramref name="body"/>: an armed, sound
        /// hostile group free of other business, and commanded to fight there (U.SPC-T371, see <see cref="War.Commanded"/>):
        /// a group passing through, or off its beat, lets a hostile hull go by.</summary>
        bool MayChase(Ship s, Ship pilot, int body, double jd)
        {
            if (s == pilot || s.Destroyed || s.IsEscort || s.Derelict || s.Battle != null || s.Planetside || s.GateDeparted) return false;
            if (s.Voyage != null || s.Phase == ShipPhase.Lane || Of(s) != null) return false;
            if (!(s.Faction.IsPirate || s.Spec.IsMilitary) || !world.Diplomacy.Hostile(s.Faction, pilot.Faction)) return false;
            if (!s.Combat.Armed || s.Combat.HullFraction < 0.5) return false;
            if (world.War != null && !world.War.Commanded(s, body)) return false;
            return !lastChased.TryGetValue(s, out double when) || jd - when >= RechaseDays;
        }

        /// <summary>Drops a ship from the rechase cooldown once its cooldown is over (<see cref="MayChase"/> would let
        /// it chase again anyway) or it is gone for good (destroyed, or through a gate), so the book does not grow
        /// for ever as a long game runs on (U.SPC-T321).</summary>
        void PruneLastChased(double jd)
        {
            List<Ship> gone = null;
            foreach (var kv in lastChased)
                if (kv.Key.Destroyed || kv.Key.GateDeparted || jd - kv.Value >= RechaseDays) (gone ??= new List<Ship>()).Add(kv.Key);
            if (gone != null) foreach (var s in gone) lastChased.Remove(s);
        }

        /// <summary>Puts <paramref name="leader"/>'s group on the chase at <paramref name="at"/> (km from the frame body),
        /// matching the pilot's velocity: the schedule stops moving it (see <see cref="World"/>'s hold).</summary>
        public Pursuer Begin(Ship leader, DVec3 at, DVec3 pilotVel, double jd)
        {
            var g = new Pursuer { Leader = leader, Pos = at, Vel = pilotVel };
            g.Ships.Add(leader);
            foreach (var e in world.Fleet.Ships)
                if (e.Leader == leader && !e.Destroyed && e.Battle == null) g.Ships.Add(e);
            Groups.Add(g);
            lastChased[leader] = jd;
            return g;
        }

        /// <summary>
        /// Every chasing group closes on the pilot (in <paramref name="body"/>'s frame, at <paramref name="pilotOffset"/>,
        /// moving at <paramref name="pilotVel"/>) for <paramref name="dt"/> real seconds: matching its velocity and closing
        /// as fast as it can, slowing to come up alongside rather than overshoot, charging its EMP in reach and setting it
        /// off, and engaging close in. The events, in order.
        /// <para>Run every frame (far more often than <see cref="Consider"/>'s periodic look), so it must notice for
        /// itself when the pilot has moved into another body's frame since the chase began: a stale <see cref="Pursuer.Pos"/>
        /// reckoned from the old frame is meaningless against an offset now reckoned from the new one, so rather than
        /// steer and judge distance on that nonsense, the chase is simply given up, the same as <see cref="Consider"/>
        /// already does when it notices <see cref="Body"/> has changed.</para>
        /// </summary>
        public List<(Pursuer group, PursuitEvent what)> Step(Ship pilot, int body, DVec3 pilotOffset, DVec3 pilotVel, double dt, double jd)
        {
            var events = new List<(Pursuer, PursuitEvent)>();
            if (dt <= 0) return events;
            if (Body != body)
            {
                ReleaseAll(jd);
                Body = body;
                return events;
            }
            for (int i = Groups.Count - 1; i >= 0; i--)
            {
                var g = Groups[i];
                g.Ships.RemoveAll(s => s.Destroyed || s.Battle != null);
                // The leader lost or pulled into another fight (U.SPC-T314): the chase is over, and whoever is left of
                // the group is pinned where the chase had flown it rather than snapping back to a stale schedule spot.
                if (g.Ships.Count == 0 || g.Leader.Destroyed || g.Leader.Battle != null)
                {
                    bool anyLeft = g.Ships.Count > 0;
                    Release(g, jd);
                    if (anyLeft && !g.Leader.Destroyed) events.Add((g, PursuitEvent.GaveUp));
                    continue;
                }
                g.Seconds += dt;
                var rel = pilotOffset - g.Pos;
                double d = rel.Length;
                if (d > GiveUpKm || g.Seconds > MaxChaseSeconds)
                {
                    Release(g, jd);
                    events.Add((g, PursuitEvent.GaveUp));
                    continue;
                }
                // Close as fast as it can and still stop alongside: v² = 2·a·s, over and above the pilot's own velocity.
                double closing = Math.Min(TopSpeedKmS, Math.Sqrt(2 * AccelKmS2 * Math.Max(0, d - EngageKm * 0.5)));
                var want = pilotVel + (d > 1e-9 ? rel / d : DVec3.Zero) * closing;
                if (want.Length > TopSpeedKmS) want = want.Normalized * TopSpeedKmS;
                var dv = want - g.Vel;
                double maxDv = AccelKmS2 * dt;
                g.Vel += dv.Length > maxDv ? dv.Normalized * maxDv : dv;
                g.Pos += g.Vel * dt;
                d = (pilotOffset - g.Pos).Length;

                g.EmpReadyIn = Math.Max(0, g.EmpReadyIn - dt);
                if (g.EmpCharge >= 0)
                {
                    g.EmpCharge += dt;
                    if (g.EmpCharge >= EmpChargeSeconds)
                    {
                        g.EmpCharge = -1;
                        g.EmpReadyIn = EmpCooldownSeconds;
                        events.Add((g, d <= EmpRangeKm * 1.25 ? PursuitEvent.EmpHit : PursuitEvent.EmpMissed));
                    }
                }
                else if (d <= EmpRangeKm && g.EmpReadyIn <= 0)
                {
                    g.EmpCharge = 0;
                    events.Add((g, PursuitEvent.EmpCharging));
                }
                if (d <= EngageKm) events.Add((g, PursuitEvent.Engage));
            }
            return events;
        }

        /// <summary>Takes a group off the chase for the battle that has taken it in (it is not rescheduled).</summary>
        public void Engaged(Pursuer g) => Groups.Remove(g);

        /// <summary>A group gives the chase up: each ship goes back on its schedule from where it is now.</summary>
        public void Release(Pursuer g, double jd)
        {
            Groups.Remove(g);
            var facing = g.Vel.LengthSquared > 1e-12 ? g.Vel.Normalized : DVec3.UnitX;
            foreach (var s in g.Ships)
            {
                if (s.Destroyed || s.Battle != null) continue;
                Fleet.PinAt(s, Body, OffsetOf(g, s), facing, jd);
                if (!s.IsEscort) world.Fleet.ScheduleNext(s, jd);
            }
        }

        /// <summary>Every chase is given up (the pilot has left the frame, the helm, or the system).</summary>
        public void ReleaseAll(double jd)
        {
            for (int i = Groups.Count - 1; i >= 0; i--) Release(Groups[i], jd);
        }
    }
}
