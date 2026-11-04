using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Unity.SpaceGod.Core
{
    /// <summary>Where a craft is in its visit to a flight deck (U.SPC-T518, see <see cref="FlightDecks"/>).</summary>
    public enum DeckStage
    {
        /// <summary>Flying to the deck's approach gate astern of the stern mouth, or holding off it until the deck is clear.</summary>
        Inbound,

        /// <summary>Cleared to land: on the glide path down the deck's axis, guided in through the stern mouth to its spot.</summary>
        Final,

        /// <summary>Down on the deck, inside the hull: refuelling, rearming and being repaired.</summary>
        OnDeck,

        /// <summary>Thrown down the deck and out of the bow mouth, back into the fight.</summary>
        Launching,
    }

    /// <summary>A craft's visit to one flight deck: which, how far along it is, and how far its service has got.</summary>
    public sealed class DeckVisit
    {
        public FlightDeck Deck;
        public DeckStage Stage;

        /// <summary>Where it is along the deck's axis from the deck's middle, km (negative astern, toward the stern mouth).</summary>
        public double Along;

        /// <summary>Where it is off the deck's axis, km in the carrier's frame (X right, Y up; Z unused).</summary>
        public DVec3 Offset;

        /// <summary>Where along the deck it puts down (km from the middle): its parking spot, given when it is cleared.</summary>
        public double SpotAlong;

        /// <summary>How far its turnaround has got, 0-1 (<see cref="FlightDecks.ServiceSeconds"/> for the whole of it).</summary>
        public double Service;

        /// <summary>Its speed down the deck while launching, km/s relative to the carrier.</summary>
        public double Speed;

        /// <summary>The phase it flies on in once it is clear of the bow mouth.</summary>
        public FighterPhase After = FighterPhase.Launch;

        /// <summary>Seconds it has sat on the deck home for good, until it is struck down into the hangar (<see cref="FlightDecks.StowSeconds"/>).</summary>
        public double Stowing;

        /// <summary>Launched again after a turnaround (not with its squadron at the start of the fight): it rejoins whoever leads now.</summary>
        public bool Relaunch;
    }

    /// <summary>
    /// One of a hull's flight decks (U.SPC-T518): a through-hull bay open at both ends, Battlestar Galactica / Star Wars style,
    /// run fore and aft through one of its hangars (<see cref="SubsystemKind.Hangar"/>). Craft come in through the stern mouth,
    /// put down on the deck amidships and go out through the bow mouth. Positions are battle-local km, moving with the hull.
    /// </summary>
    public sealed class FlightDeck
    {
        public readonly BattleUnit Carrier;

        /// <summary>The hangar the deck runs through: shot away, the deck is closed.</summary>
        public readonly Subsystem Bay;

        /// <summary>The deck's middle, and its two mouths along the hull's keel, in ship lengths in the hull's frame (right, up, forward).</summary>
        public readonly DVec3 MiddleLocal;

        /// <summary>The mouths' distance from the deck's middle along its axis, km (stern negative, bow positive).</summary>
        public readonly double SternAlong, BowAlong;

        /// <summary>Port, starboard or centreline, for the HUD.</summary>
        public readonly string Name;

        /// <summary>The craft cleared to land and on the glide path now; one at a time.</summary>
        public BattleUnit Final;

        /// <summary>The craft down on the deck, in the order they landed.</summary>
        public readonly List<BattleUnit> Aboard = new List<BattleUnit>();

        /// <summary>Battle seconds before the launch lane is clear for the next craft.</summary>
        public double NextLaunch;

        public FlightDeck(BattleUnit carrier, Subsystem bay, string name)
        {
            Carrier = carrier;
            Bay = bay;
            Name = name;
            var d = bay.Def;
            MiddleLocal = new DVec3(d.X, d.Y, d.Z);
            double length = Math.Max(carrier.LengthKm, 1e-6);
            double half = Math.Min(FlightDecks.HalfLengthShipLengths, FlightDecks.MaxHalfLengthKm / length);
            SternAlong = (Math.Max(-0.5, d.Z - half) - d.Z) * length;
            BowAlong = (Math.Min(0.5, d.Z + half) - d.Z) * length;
            if (BowAlong - SternAlong < 0.05) BowAlong = SternAlong + 0.05;
        }

        /// <summary>Open for traffic: its hangar stands and its hull is on the field, not taken.</summary>
        public bool Open => !Bay.Destroyed && StrandedWings.CanLandOn(Carrier);

        /// <summary>The deck's middle, battle-local km.</summary>
        public DVec3 Middle => Carrier.PointLocal(MiddleLocal);

        public DVec3 Stern => At(SternAlong, DVec3.Zero);
        public DVec3 Bow => At(BowAlong, DVec3.Zero);

        /// <summary>The approach gate: on the deck's axis, <see cref="FlightDecks.ApproachKm"/> astern of the stern mouth.</summary>
        public DVec3 Gate => At(SternAlong - FlightDecks.ApproachKm, DVec3.Zero);

        /// <summary>A point <paramref name="along"/> km down the deck's axis from its middle, <paramref name="offset"/> (right, up) off it.</summary>
        public DVec3 At(double along, DVec3 offset)
        {
            Carrier.Basis(out var right, out var up);
            return Middle + Carrier.Forward * along + right * offset.X + up * offset.Y;
        }

        /// <summary>Where battle-local <paramref name="pos"/> lies in the deck's frame: along its axis and off it (right, up).</summary>
        public void Frame(DVec3 pos, out double along, out DVec3 offset)
        {
            Carrier.Basis(out var right, out var up);
            var d = pos - Middle;
            along = DVec3.Dot(d, Carrier.Forward);
            offset = new DVec3(DVec3.Dot(d, right), DVec3.Dot(d, up), 0);
        }

        /// <summary>Parking spot <paramref name="i"/> along the deck, km from its middle.</summary>
        public double SpotAlong(int i) => (i - (FlightDecks.Spots - 1) * 0.5) * FlightDecks.SpotSpacingKm;

        /// <summary>Whether <paramref name="craft"/> has to hold off: another is on the glide path, or every spot is taken.</summary>
        public bool Busy(BattleUnit craft)
        {
            // A craft cleared and since shot down, recalled or waved off frees the glide path.
            if (Final != null && Final != craft && (Final.Dead || Final.Fighter?.Deck?.Deck != this || Final.Fighter.Deck.Stage != DeckStage.Final)) Final = null;
            return (Final != null && Final != craft) || Aboard.Count >= FlightDecks.Spots;
        }
    }

    /// <summary>
    /// Flight decks you fly through in person (U.SPC-T518). A carrier, and any other hull with hangars, has a flight deck run
    /// fore and aft through each of its hangars, open at both ends. Its craft are recovered by flying it, not by a count: a
    /// fighter, bomber or shuttle flies to the approach gate astern of a deck, holds off in the pattern while another is on the
    /// glide path or the deck is full, and once cleared is guided in along the deck's axis through the stern mouth to put down
    /// on its spot; there it refuels, rearms and is repaired (<see cref="ServiceSeconds"/>) and is then thrown down the deck and
    /// out of the bow mouth back into the fight. A pilot comes home to do so when hurt or out of ordnance
    /// (<see cref="WantsService"/>), and every craft lands this way when its carrier recalls it. The player can land a craft
    /// they are flying the same way: lined up on a friendly deck's glide path, slow and nose along its axis, the deck takes it
    /// in (boost aborts), turns it round and launches it again; the cockpit readout and the HUD give the guidance.
    /// A carrier's squadrons also launch through their decks at the start of a fight. Each craft is its own hull (U.SPC-T526):
    /// it docks in the hangar as itself as it puts down (<see cref="FighterAI.TakeAboard"/>) and is undocked again as it
    /// launches (<see cref="TakeOff"/>).
    /// </summary>
    [SettingsSource]
    public static class FlightDecks
    {
        public static readonly RangeSetting ServiceSeconds = new RangeSetting("dogfight.deckService", SettingGroups.Dogfighting,
            "Flight deck turnaround", 30, 5, 180, 5,
            "How long (seconds) a craft down on a carrier's flight deck takes to be refuelled, rearmed and repaired before it is " +
            "launched again out of the bow.", v => v.ToString("0", CultureInfo.InvariantCulture) + " s",
            "flight deck carrier land landing rearm refuel repair turnaround hangar");

        public static readonly ToggleSetting AiRearms = new ToggleSetting("dogfight.deckRearm", SettingGroups.Dogfighting,
            "Pilots land to rearm", true,
            "Carrier pilots who are badly hurt or out of torpedoes and missiles fly home mid-fight, land on the flight deck, " +
            "rearm and repair, and launch again. Off, they land only when recalled.",
            "flight deck carrier land rearm repair pilot ai");

        public static readonly RangeSetting ServiceHull = new RangeSetting("dogfight.deckHull", SettingGroups.Dogfighting,
            "Pilots land for repairs below", 0.4, 0.1, 0.9, 0.05,
            "How much of its hull a carrier craft has left before its pilot flies home to land and be repaired.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "flight deck carrier land repair hull damage pilot");

        /// <summary>The render class a hull's flight deck mouths are drawn under (principle 3): only once the hull is big on screen.</summary>
        public static readonly LodClass Lod = RenderLod.Register(new LodClass("deck", "Flight deck", () => 24, LodFar.None, false));

        /// <summary>How long a deck runs either side of its hangar: this many ship lengths, at most <see cref="MaxHalfLengthKm"/>.</summary>
        public const double HalfLengthShipLengths = 0.15, MaxHalfLengthKm = 0.5;

        /// <summary>How far astern of the stern mouth the approach gate lies, km, and how near it a craft must come to be cleared.</summary>
        public const double ApproachKm = 1.2, GateCaptureKm = 0.3;

        /// <summary>Coming in from ahead: how far out from the keel it swings round the hull, in ship lengths of beam plus km clear.</summary>
        public const double BeamShipLengths = 0.15, RoundHullKm = 0.3;

        /// <summary>The holding pattern: how far astern of the gate it circles, and how wide, km.</summary>
        public const double HoldAsternKm = 0.9, HoldRadiusKm = 0.6;

        /// <summary>How long a craft home for good sits on its spot before it is struck down into the hangar, seconds.</summary>
        public const double StowSeconds = 8;

        /// <summary>Craft a deck can turn round at once, and how far apart their spots are along it, km.</summary>
        public const int Spots = 3;
        public const double SpotSpacingKm = 0.08;

        /// <summary>The glide path: the fastest and slowest a craft closes on its spot, km/s relative to the deck, and how quickly
        /// (seconds, e-folding) it is drawn onto the deck's axis.</summary>
        public const double FinalSpeedKmS = 0.12, MinCloseKmS = 0.02, LineUpSeconds = 2.5;

        /// <summary>A launch: seconds between craft out of one deck, how far apart a squadron's column starts down the deck,
        /// and how far clear of the bow mouth a craft is handed back to its pilot, km.</summary>
        public const double LaunchIntervalSeconds = 3, LaunchSpacingKm = 0.07, ExitKm = 0.35;

        /// <summary>The player's capture: how far off the axis, how far round from it (deg), and how fast relative to the
        /// deck (km/s) a craft may be on the glide path for the deck to take it in.</summary>
        public const double CaptureOffAxisKm = 0.15, CaptureAlignDeg = 30, CaptureSpeedKmS = 0.2;

        /// <summary>How far off a friendly deck the cockpit starts giving landing guidance, km.</summary>
        public const double GuideKm = 12;

        static readonly ConditionalWeakTable<BattleUnit, FlightDeck[]> decks = new ConditionalWeakTable<BattleUnit, FlightDeck[]>();
        static readonly FlightDeck[] None = new FlightDeck[0];

        /// <summary>Every flight deck <paramref name="carrier"/> has (one through each of its hangars), open or not; none for a
        /// fighter, a hull without hangars, or a station.</summary>
        public static IReadOnlyList<FlightDeck> Of(BattleUnit carrier)
        {
            if (carrier == null || carrier.Kind != UnitKind.Ship || carrier.Combat == null) return None;
            return decks.GetValue(carrier, Build);
        }

        static FlightDeck[] Build(BattleUnit carrier)
        {
            var list = new List<FlightDeck>();
            foreach (var s in carrier.Combat.Subsystems)
            {
                if (s.Kind != SubsystemKind.Hangar) continue;
                double x = s.Def.X;
                string side = x > 0.02 ? "starboard" : x < -0.02 ? "port" : "centreline";
                list.Add(new FlightDeck(carrier, s, side + " deck"));
            }
            return list.Count == 0 ? None : list.ToArray();
        }

        /// <summary>Whether <paramref name="carrier"/> has a deck open for traffic.</summary>
        public static bool HasOpenDeck(BattleUnit carrier)
        {
            foreach (var d in Of(carrier))
                if (d.Open) return true;
            return false;
        }

        /// <summary>The craft's visit, or null.</summary>
        public static DeckVisit VisitOf(BattleUnit u) => u?.Fighter?.Deck;

        /// <summary>Down on a deck, inside its hull.</summary>
        public static bool OnDeck(BattleUnit u) => VisitOf(u)?.Stage == DeckStage.OnDeck;

        /// <summary>Down on a deck that will launch it again: the deck open and its hull not running.</summary>
        public static bool TurningRound(BattleUnit u)
        {
            var v = VisitOf(u);
            return v != null && v.Stage == DeckStage.OnDeck && v.Deck.Open && !v.Deck.Carrier.Fleeing && !BoardingShuttles.Is(u);
        }

        /// <summary>Flying home to a deck or down on one: its squadron does not follow it as a lead (one launching is all but back).</summary>
        public static bool Away(BattleUnit u) =>
            u?.Fighter != null && ((u.Fighter.Deck != null && u.Fighter.Deck.Stage != DeckStage.Launching) || u.Fighter.Phase == FighterPhase.Return);

        // ---------------------------------------------------------------- the pilot's decision

        /// <summary>
        /// Whether a pilot flies home mid-fight to land, rearm and repair: its home has a deck open and is not running, and
        /// its hull is under <see cref="ServiceHull"/>, or it carried torpedoes or missiles and has none left.
        /// </summary>
        public static bool WantsService(Battle b, BattleUnit u)
        {
            var f = u.Fighter;
            if (f == null || !AiRearms.Value || BoardingShuttles.Is(u)) return false;
            var home = f.LandsOn;
            if (home == null || !home.Present || home.Captured || home.Fleeing || home.Surrendering || !HasOpenDeck(home)) return false;
            var c = u.Combat;
            if (c.HullFraction < ServiceHull.Value) return true;
            return OutOfOrdnance(c);
        }

        /// <summary>It was built with guided rounds (torpedoes, missiles) and every one of them is spent.</summary>
        public static bool OutOfOrdnance(ShipCombat c)
        {
            bool carries = false;
            foreach (var s in c.Subsystems)
            {
                var w = s.Def.Weapon;
                if (w == null || !w.Missile || w.Ammo <= 0 || s.Kind == SubsystemKind.Countermeasures) continue;
                carries = true;
                if (!s.Destroyed && s.Ammo > 0) return false;
            }
            return carries;
        }

        // ---------------------------------------------------------------- inbound

        /// <summary>The deck <paramref name="u"/> is flying to on its home (kept once chosen while it stays open), or null when
        /// its home has none open (a station: it lands the old way).</summary>
        public static FlightDeck Assign(BattleUnit u)
        {
            var f = u.Fighter;
            var home = f?.LandsOn;
            if (home == null) return null;
            var v = f.Deck;
            if (v != null && v.Deck.Carrier == home && v.Deck.Open) return v.Deck;
            FlightDeck best = null;
            double bestScore = double.MaxValue;
            foreach (var d in Of(home))
            {
                if (!d.Open) continue;
                double score = d.Aboard.Count * 2 + (d.Final != null ? 1 : 0) + (d.Gate - u.Pos).Length / 5;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = d;
                }
            }
            if (best == null)
            {
                Abort(u);
                return null;
            }
            if (v != null && v.Deck.Final == u) v.Deck.Final = null;
            f.Deck = new DeckVisit { Deck = best, Stage = DeckStage.Inbound };
            return best;
        }

        /// <summary>Where an inbound craft flies: the approach gate, or round the holding pattern astern of it while the deck is busy.</summary>
        public static DVec3 InboundGoal(Battle b, BattleUnit u, FlightDeck deck)
        {
            var c = deck.Carrier;
            c.Basis(out var right, out var up);
            // Coming in from ahead of the gate it swings round the hull's flank to come up astern, never through the hull.
            var local = c.ToLocal(u.Pos) * c.LengthKm;
            double gateZ = deck.MiddleLocal.Z * c.LengthKm + deck.SternAlong - ApproachKm;
            if (local.Z > gateZ + GateCaptureKm)
            {
                double beam = c.LengthKm * BeamShipLengths + RoundHullKm;
                double side = local.X != 0 ? Math.Sign(local.X) : deck.MiddleLocal.X >= 0 ? 1 : -1;
                return c.Pos + right * (side * beam) + up * (local.Y * 0.5) + c.Forward * gateZ;
            }
            if (!deck.Busy(u)) return deck.Gate;
            double a = b.Seconds * 0.12 + (u.Fighter?.Seed ?? 0);
            return deck.Gate - c.Forward * HoldAsternKm + (right * Math.Cos(a) + up * Math.Sin(a)) * HoldRadiusKm;
        }

        /// <summary>
        /// Clears an inbound craft to land once it is at the gate and the deck is free: it goes onto the glide path
        /// (<see cref="DeckStage.Final"/>) and the deck flies it in from here. True if it was cleared.
        /// </summary>
        public static bool TryFinal(Battle b, BattleUnit u, FlightDeck deck, double dt)
        {
            var v = VisitOf(u);
            if (v == null || v.Deck != deck || v.Stage != DeckStage.Inbound || deck.Busy(u)) return false;
            double rel = (u.Vel - deck.Carrier.Vel).Length;
            if ((deck.Gate - u.Pos).Length > Math.Max(GateCaptureKm, rel * dt * 1.5)) return false;
            ClearToLand(u, deck);
            return true;
        }

        static void ClearToLand(BattleUnit u, FlightDeck deck)
        {
            var v = VisitOf(u);
            v.Stage = DeckStage.Final;
            deck.Frame(u.Pos, out v.Along, out v.Offset);
            v.SpotAlong = deck.SpotAlong(deck.Aboard.Count);
            // Never cleared from inside the bay: it comes in through the stern mouth.
            if (v.Along > deck.SternAlong) v.Along = deck.SternAlong;
            deck.Final = u;
        }

        /// <summary>Calls off a craft's visit (its deck closed, it was recalled to the fight, the player boosted off the glide path).</summary>
        public static void Abort(BattleUnit u)
        {
            var v = VisitOf(u);
            if (v == null) return;
            if (v.Deck.Final == u) v.Deck.Final = null;
            v.Deck.Aboard.Remove(u);
            u.Fighter.Deck = null;
        }

        // ---------------------------------------------------------------- the deck flies it

        /// <summary>
        /// One step of a craft the deck is flying: on the glide path, drawn onto the axis and in through the stern mouth to its
        /// spot, where it puts down; or launching, down the deck and out of the bow mouth. True while the deck has it (its pilot
        /// or the player's stick stand aside); false when it has no such visit, or its deck closed and the visit was called off.
        /// </summary>
        public static bool Fly(Battle b, BattleUnit u, double dt)
        {
            var v = VisitOf(u);
            if (v == null || (v.Stage != DeckStage.Final && v.Stage != DeckStage.Launching)) return false;
            var deck = v.Deck;
            var c = deck.Carrier;
            if (!deck.Open)
            {
                Abort(u);
                return false;
            }
            var spec = u.Combat.Spec;
            if (v.Stage == DeckStage.Final)
            {
                double k = 1 - Math.Exp(-dt / LineUpSeconds);
                v.Offset -= v.Offset * k;
                double close = Math.Min(FinalSpeedKmS, Math.Max(MinCloseKmS, (v.SpotAlong - v.Along) * 0.2));
                v.Along = Math.Min(v.SpotAlong, v.Along + close * dt);
                u.Pos = deck.At(v.Along, v.Offset);
                u.Vel = c.Vel + c.Forward * (v.Along < v.SpotAlong ? close : 0);
                Battle.TurnToward(u, c.Forward, Math.Max(spec.TurnRateDegS, 30), dt);
                if (v.Along >= v.SpotAlong - 1e-6) Touchdown(b, u, v);
                return true;
            }
            // Launching: thrown down the deck at the craft's own thrust, nose along the hull's.
            double top = Math.Max(spec.MaxSpeed, MinCloseKmS);
            v.Speed = Math.Min(top, v.Speed + Math.Max(spec.Accel, 0.02) * dt);
            v.Along += v.Speed * dt;
            v.Offset -= v.Offset * (1 - Math.Exp(-dt / LineUpSeconds));
            u.Pos = deck.At(v.Along, v.Offset);
            u.Vel = c.Vel + c.Forward * v.Speed;
            u.Forward = c.Forward;
            if (v.Along >= deck.BowAlong + ExitKm) ClearOfBow(b, u, v);
            return true;
        }

        /// <summary>Puts a craft down on its spot: inside the hull now (<see cref="BattleUnit.Docked"/>), back in its hangar's
        /// count (<see cref="FighterAI.TakeAboard"/>), and its turnaround begins.</summary>
        static void Touchdown(Battle b, BattleUnit u, DeckVisit v)
        {
            var deck = v.Deck;
            if (deck.Final == u) deck.Final = null;
            v.Stage = DeckStage.OnDeck;
            v.Service = 0;
            v.Offset = DVec3.Zero;
            if (!deck.Aboard.Contains(u)) deck.Aboard.Add(u);
            u.Docked = true;
            u.Vel = deck.Carrier.Vel;
            u.Forward = deck.Carrier.Forward;
            FighterAI.TakeAboard(deck.Carrier, u);
        }

        /// <summary>Clear of the bow mouth: its pilot (or the player) has it again, flying on in <see cref="DeckVisit.After"/>,
        /// back in its squadron's vee behind whoever leads it now.</summary>
        static void ClearOfBow(Battle b, BattleUnit u, DeckVisit v)
        {
            var f = u.Fighter;
            f.Deck = null;
            f.Phase = v.After;
            f.PhaseTime = 0;
            f.RunClosest = double.MaxValue;
            f.RunEntry = DVec3.Zero;
            // A relaunched pilot rejoins its squadron's vee behind whoever leads it now (it leads if nobody does); a squadron
            // launching together at the start of a fight keeps the leads it was launched with.
            if (f.Squadron >= 0 && v.Relaunch && !b.Flown.ContainsKey(u))
            {
                BattleUnit lead = null;
                foreach (var o in b.Units)
                {
                    if (o == u || !o.Present || o.Kind != UnitKind.Fighter || o.Fighter == null || o.Fighter.Carrier != f.Carrier
                        || o.Fighter.Squadron != f.Squadron || o.Fighter.Lead != null || Away(o)) continue;
                    if (lead == null || o.Fighter.Slot < lead.Fighter.Slot) lead = o;
                }
                f.Lead = lead;
            }
            if (b.Flown.TryGetValue(u, out var stick))
            {
                stick.Aim = u.Forward;
                stick.Up = DVec3.Zero;
                stick.AngularVelocity = DVec3.Zero;
                stick.Throttle = Math.Max(stick.Throttle, 0.6);
            }
        }

        // ---------------------------------------------------------------- on deck

        /// <summary>
        /// The decks' traffic each step (from <see cref="Battle"/>'s step): every craft down on a deck rides with its hull on its
        /// spot and is turned round, hull and drive tank first and then its racks and magazines; done, and with the launch lane
        /// clear, it is launched again (<see cref="TakeOff"/>), unless the shooting is over (only the player's craft goes up
        /// again then), its hull is running, or it is a boarding shuttle home for good.
        /// </summary>
        public static void Step(Battle b, double dt)
        {
            for (int i = 0; i < b.Units.Count; i++)
            {
                var u = b.Units[i];
                if (u.Kind != UnitKind.Fighter || u.Dead) continue;
                var v = VisitOf(u);
                if (v == null || v.Stage != DeckStage.OnDeck) continue;
                var deck = v.Deck;
                var c = deck.Carrier;
                if (c.Dead) continue;
                int spot = Math.Max(0, deck.Aboard.IndexOf(u));
                u.Pos = deck.At(deck.SpotAlong(spot), DVec3.Zero);
                u.Vel = c.Vel;
                u.Forward = c.Forward;
                if (!deck.Open || !c.Present) continue;
                v.Service = Math.Min(1, v.Service + dt / Math.Max(1, ServiceSeconds.Value));
                Service(u, v.Service);
                bool player = b.Flown.ContainsKey(u);
                // Home for good (the shooting over, its hull running, a shuttle back with its team, or its berth lost with a bay):
                // struck down off the deck into the hangar, so the spot is free for the next one in.
                if (BoardingShuttles.Is(u) || c.Fleeing || c.Surrendering || (b.Mopping && !player) || !Berthed(c, u))
                {
                    v.Stowing += dt;
                    if (v.Stowing >= StowSeconds)
                    {
                        deck.Aboard.Remove(u);
                        u.Fighter.Deck = null;
                    }
                    continue;
                }
                v.Stowing = 0;
                if (v.Service < 1 || b.Seconds < deck.NextLaunch) continue;
                if (!TakeOff(c, u)) continue;
                deck.NextLaunch = b.Seconds + LaunchIntervalSeconds;
                deck.Aboard.Remove(u);
                u.Docked = false;
                v.Stage = DeckStage.Launching;
                v.Along = deck.SpotAlong(spot);
                v.Speed = 0;
                v.After = FighterPhase.Launch;
                v.Relaunch = true;
            }
        }

        /// <summary>The turnaround <paramref name="share"/> of the way through: hull mended and tank filled that far; once it is
        /// done, every hardpoint mended, every rack and magazine full, shields up and the guns cool.</summary>
        public static void Service(BattleUnit u, double share)
        {
            var c = u.Combat;
            c.Hull = Math.Max(c.Hull, c.Spec.Hull * share);
            c.BoostFraction = Math.Max(c.BoostFraction, share);
            if (share < 1) return;
            c.Hull = c.Spec.Hull;
            c.BoostFraction = 1;
            foreach (var s in c.Subsystems)
            {
                s.Hp = s.Def.MaxHp;
                s.Restoring = 0;
                s.Cooldown = 0;
                if (s.Def.Weapon != null) s.Ammo = s.Def.Weapon.Ammo;
                if (s.Kind == SubsystemKind.ShieldGenerator) s.Shield = s.MaxShield;
            }
            u.Heat = 0;
            u.Overheated = false;
            u.Stunned = 0;
        }

        /// <summary>Whether <paramref name="craft"/>, down on <paramref name="home"/>'s deck, still holds its berth in the hangar
        /// to launch from (U.SPC-T526): a bay shot away loses the craft in it.</summary>
        static bool Berthed(BattleUnit home, BattleUnit craft) =>
            craft.CraftHull != null ? home.Combat.Hangar.Contains(craft.CraftHull) : home.Combat.Fighters > 0;

        /// <summary>
        /// A craft leaves its hangar to launch again, the reverse of <see cref="FighterAI.TakeAboard"/>: its own hull undocked
        /// from <paramref name="home"/>'s hangar (U.SPC-T526, <see cref="CarrierWings.Undock"/>). False when the hangar no
        /// longer holds it (it was lost with a bay shot away, or there was no berth for it): it stays aboard.
        /// </summary>
        public static bool TakeOff(BattleUnit home, BattleUnit craft)
        {
            if (home?.Combat == null || BoardingShuttles.Is(craft)) return false;
            if (craft.CraftHull != null) return CarrierWings.Undock(home.Combat, craft.CraftHull);
            if (home.Combat.Fighters <= 0) return false;
            home.Combat.Fighters--;
            return true;
        }

        /// <summary>
        /// A craft just out of <paramref name="carrier"/>'s hangar at the start of a fight (or a shuttle sent out) goes out
        /// through a flight deck: placed in the deck's launch lane behind any already in it, so a squadron goes out of the bow
        /// in a column. False when the hull has no deck open (it is left where the launch put it).
        /// </summary>
        public static bool Catapult(Battle b, BattleUnit carrier, BattleUnit craft)
        {
            if (craft?.Fighter == null) return false;
            FlightDeck best = null;
            int bestCount = int.MaxValue;
            var counts = new Dictionary<FlightDeck, int>();
            foreach (var o in b.Units)
            {
                var ov = VisitOf(o);
                if (o == craft || ov == null || ov.Stage != DeckStage.Launching || ov.Deck.Carrier != carrier) continue;
                counts[ov.Deck] = (counts.TryGetValue(ov.Deck, out int n) ? n : 0) + 1;
            }
            foreach (var d in Of(carrier))
            {
                if (!d.Open) continue;
                int n = counts.TryGetValue(d, out int m) ? m : 0;
                if (n < bestCount)
                {
                    bestCount = n;
                    best = d;
                }
            }
            if (best == null) return false;
            // In column down the deck from its middle, one spacing apart (a long one backs out of the stern mouth).
            var v = new DeckVisit
            {
                Deck = best, Stage = DeckStage.Launching, After = craft.Fighter.Phase, Along = -bestCount * LaunchSpacingKm,
            };
            craft.Fighter.Deck = v;
            craft.Pos = best.At(v.Along, DVec3.Zero);
            craft.Vel = carrier.Vel;
            craft.Forward = carrier.Forward;
            return true;
        }

        // ---------------------------------------------------------------- the player

        /// <summary>
        /// A craft the player is flying (from <see cref="FighterAI.FlyManned"/>): the deck flies it while it has it (boosting
        /// off the glide path aborts the landing), and a craft lined up on a friendly deck's glide path, slow and nose along its
        /// axis (<see cref="CaptureOffAxisKm"/>, <see cref="CaptureAlignDeg"/>, <see cref="CaptureSpeedKmS"/>), is taken in. True
        /// while the deck has it.
        /// </summary>
        public static bool FlyManned(Battle b, BattleUnit u, FighterControl stick, double dt)
        {
            var v = VisitOf(u);
            if (v != null && v.Stage == DeckStage.Final && stick.Boost)
            {
                Abort(u);
                return false;
            }
            if (v != null && v.Stage == DeckStage.Inbound) Abort(u);
            if (Fly(b, u, dt)) return true;
            var deck = Capturing(b, u);
            if (deck == null) return false;
            var f = u.Fighter;
            if (deck.Carrier != f.Carrier) f.Home = deck.Carrier;
            f.Deck = new DeckVisit { Deck = deck, Stage = DeckStage.Inbound };
            ClearToLand(u, deck);
            return Fly(b, u, dt);
        }

        /// <summary>The friendly deck whose glide path <paramref name="u"/> is lined up on closely enough to be taken in, or null.</summary>
        public static FlightDeck Capturing(Battle b, BattleUnit u)
        {
            foreach (var o in b.Units)
            {
                if (o.Side != u.Side || o.Kind != UnitKind.Ship || o.Fleeing || o.Surrendering) continue;
                foreach (var d in Of(o))
                {
                    if (!d.Open || d.Busy(u)) continue;
                    if (o != u.Fighter.Carrier && o != u.Fighter.Home && StrandedWings.Room(b, o) <= 0) continue;
                    d.Frame(u.Pos, out double along, out var off);
                    if (along < d.SternAlong - ApproachKm - GateCaptureKm || along > d.SternAlong) continue;
                    if (off.Length > CaptureOffAxisKm) continue;
                    if (DVec3.Dot(u.Forward, o.Forward) < Math.Cos(CaptureAlignDeg * Astro.Deg2Rad)) continue;
                    if ((u.Vel - o.Vel).Length > CaptureSpeedKmS) continue;
                    return d;
                }
            }
            return null;
        }

        /// <summary>The deck the cockpit guides <paramref name="u"/> to: the one it is visiting, else the nearest open friendly
        /// one within <see cref="GuideKm"/> (its own carrier's first), or null.</summary>
        public static FlightDeck GuideDeck(Battle b, BattleUnit u)
        {
            var v = VisitOf(u);
            if (v != null) return v.Deck;
            FlightDeck best = null;
            double bestD = GuideKm;
            foreach (var o in b.Units)
            {
                if (o.Side != u.Side || o.Kind != UnitKind.Ship || !o.Present || o.Captured) continue;
                foreach (var d in Of(o))
                {
                    if (!d.Open) continue;
                    double dist = (d.Gate - u.Pos).Length - (o == u.Fighter?.LandsOn ? 2 : 0);
                    if (dist < bestD)
                    {
                        bestD = dist;
                        best = d;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// The cockpit's landing guidance for <paramref name="u"/> (null with no deck near): on a visit, what the deck is doing
        /// with it; otherwise the deck's name and range to its gate, how far off the glide path it is (right/left, up/down), how
        /// far its nose is off the deck's axis, and its closing speed against the most the deck will take it in at.
        /// </summary>
        public static string Guidance(Battle b, BattleUnit u)
        {
            var deck = GuideDeck(b, u);
            if (deck == null) return null;
            var v = VisitOf(u);
            string where = deck.Carrier.Name + " " + deck.Name;
            if (v != null)
            {
                switch (v.Stage)
                {
                    case DeckStage.Final: return "AUTO-LAND " + where + " " + Metres(v.SpotAlong - v.Along) + " to the spot (boost aborts)";
                    case DeckStage.OnDeck:
                        return deck.Open && !deck.Carrier.Fleeing
                            ? "ON DECK " + where + ": refuel, rearm, repair " + Pct(v.Service) + (v.Service >= 1 ? ", launching" : "")
                            : "ON DECK " + where;
                    case DeckStage.Launching: return "LAUNCHING out of the " + where;
                }
            }
            deck.Frame(u.Pos, out double along, out var off);
            double range = (deck.Gate - u.Pos).Length;
            double rel = (u.Vel - deck.Carrier.Vel).Length;
            double nose = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(u.Forward, deck.Carrier.Forward)))) * Astro.Rad2Deg;
            var sb = new System.Text.StringBuilder("DECK ").Append(where).Append(' ');
            sb.Append(range.ToString("0.0", CultureInfo.InvariantCulture)).Append(" km");
            if (along > deck.SternAlong) sb.Append(": come round astern");
            else
            {
                sb.Append(": ").Append(Metres(Math.Abs(off.X))).Append(off.X > 0 ? " right" : " left");
                sb.Append(' ').Append(Metres(Math.Abs(off.Y))).Append(off.Y > 0 ? " high" : " low");
                sb.Append(", nose ").Append(nose.ToString("0", CultureInfo.InvariantCulture)).Append('°');
            }
            sb.Append(", closing ").Append(rel.ToString("0.00", CultureInfo.InvariantCulture)).Append(" km/s");
            if (rel > CaptureSpeedKmS) sb.Append(" (slow to ").Append(CaptureSpeedKmS.ToString("0.0", CultureInfo.InvariantCulture)).Append(')');
            if (deck.Busy(u)) sb.Append(", deck busy: hold");
            return sb.ToString();
        }

        static string Metres(double km) => (km * 1000).ToString("0", CultureInfo.InvariantCulture) + " m";
        static string Pct(double f) => Math.Round(100 * Math.Max(0, Math.Min(1, f))).ToString("0", CultureInfo.InvariantCulture) + "%";
    }
}
