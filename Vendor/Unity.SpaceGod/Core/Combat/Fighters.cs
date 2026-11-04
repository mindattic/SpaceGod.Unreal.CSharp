using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    public enum FighterPhase
    {
        Launch,
        Approach,
        AttackRun,
        BreakOff,
        Dogfight,

        /// <summary>Flying home to land on the carrier.</summary>
        Return,
    }

    /// <summary>A fighter's pilot and squadron: who leads, where it flies in the formation, how it flies.</summary>
    public sealed class FighterState
    {
        /// <summary>The carrier that launched it: its squadron is this carrier's.</summary>
        public BattleUnit Carrier;

        /// <summary>Where it lands instead, once its own carrier was lost or gone and it found another friendly carrier or a
        /// station to put in at (U.SPC-T473, see <see cref="StrandedWings"/>); null while it still lands on its own.</summary>
        public BattleUnit Home;

        /// <summary>Where it lands: its own carrier, or the one or station it found when that was lost.</summary>
        public BattleUnit LandsOn => Home ?? Carrier;

        /// <summary>Stranded (U.SPC-T473): its carrier was lost or gone and nowhere friendly on the field could take it, so far.</summary>
        public bool Stranded;

        /// <summary>Seconds until a stranded craft looks again for somewhere to land.</summary>
        public double HavenCheckIn;

        /// <summary>What a boarding shuttle's team drilled to in its carrier's habitats (U.SPC-T486): the factor their
        /// fighting quality is multiplied by (<see cref="Interior.DrillFactor"/>).</summary>
        public double Drill = 1;

        public BattleUnit Lead;
        public int Squadron;
        public int Slot;
        public bool Bomber;

        /// <summary>Its class (U.SPC-T424): what it is built for and how it flies.</summary>
        public FighterRole Role;

        /// <summary>What its squadron's lead has told it to do (U.SPC-T424, see <see cref="Wing"/>).</summary>
        public WingOrder Order;
        public FighterPhase Phase;
        public double PhaseTime;
        /// <summary>Closest the run has come to its target; a run only counts as past once it has actually closed.</summary>
        public double RunClosest = double.MaxValue;

        /// <summary>The pilot: how good a shot, how quick to react (seconds of lag), how tidy in formation (0..1).</summary>
        public double Skill, Reaction, Discipline;

        /// <summary>Where the current run starts and where the break-off heads, battle-local km.</summary>
        public DVec3 RunEntry, BreakPoint;

        public double Seed;

        /// <summary>Its visit to a flight deck (U.SPC-T518, see <see cref="FlightDecks"/>): flying in to land, down on the deck
        /// being turned round, or launching out of the bow; null while it is out in the fight.</summary>
        public DeckVisit Deck;
    }

    /// <summary>The player's stick on a launched craft they have taken (U.SPC-T422, see <see cref="FighterSeat"/>).</summary>
    public sealed class FighterControl
    {
        /// <summary>Where the player wants the nose (unit, ecliptic axes); zero holds the current heading.</summary>
        public DVec3 Aim;

        /// <summary>The share of the craft's top speed it holds along its nose, 0-1.</summary>
        public double Throttle;

        /// <summary>The second trigger, held this step: it fires <see cref="SecondaryGroup"/>.</summary>
        public bool Secondary;

        /// <summary>The guided weapons the second trigger fires (U.SPC-T424, see <see cref="WeaponGroups"/>).</summary>
        public WeaponGroup SecondaryGroup = WeaponGroup.Missiles;

        /// <summary>Strafe and roll (each -1..1), the afterburner, and flight assist switched off (U.SPC-T424).</summary>
        public double StrafeRight, StrafeUp, Roll;
        public bool Boost, FlightAssistOff;

        /// <summary>The craft's roof and spin, carried from step to step by its flight model (U.SPC-T424).</summary>
        public DVec3 Up, AngularVelocity;
    }

    /// <summary>
    /// Carrier fighters. A carrier launches its squadrons (six craft each, of the classes in
    /// <see cref="FighterCraft.SquadronMix"/>: interceptors, heavy fighters, bombers) as it enters a battle. Each
    /// squadron's lead flies attack runs on an enemy capital ship (approach from the side, or for bombers from where its
    /// weak points lie open, dive at it guns firing or torpedo away, pass close, break off, swing round from a new side)
    /// or, for fighters with enemy fighters about, dogfights. Wingmen fly a loose vee on the lead: each pilot has their
    /// own wander, reaction lag and discipline, so formations breathe and stretch the way human-flown ones do, and spread
    /// out on the run; told to (<see cref="Wing"/>), they break off and fight on their own. A lead that dies is replaced
    /// by its senior wingman. Pilots share their reactor as the fight needs (<see cref="PowerDistribution"/>) and throw
    /// flares at missiles closing on them.
    /// </summary>
    public static class FighterAI
    {
        public const double FighterLengthKm = 0.016, BomberLengthKm = 0.024;

        public static readonly CombatSpec Interceptor = FighterCraft.Interceptor.Spec;

        public static readonly CombatSpec BomberSpec = FighterCraft.Bomber.Spec;

        const int SquadronSize = CarrierWings.SquadronSize;

        /// <summary>
        /// A carrier entering battle puts every craft docked aboard that is fit to fly into space (U.SPC-T526): each launches
        /// as itself, on its own hull and with its own pilot, in the squadron its berth is in. A crippled craft stays aboard.
        /// </summary>
        public static void Launch(Battle b, BattleUnit carrier)
        {
            if (carrier == null || carrier.Combat.Fighters <= 0) return;
            int hangars = 0;
            foreach (var s in carrier.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Hangar && !s.Destroyed) hangars++;
            if (hangars == 0) return;
            var spec = carrier.Combat.Spec;
            // Squadron by squadron in berth order; the recon flight, out ahead of the strike (U.SPC-T485, see
            // ElectronicWarfare), is launched last under the squadron number after the carrier's own, so it keeps to itself.
            var squadrons = new SortedDictionary<int, List<Ship>>();
            var recon = new List<Ship>();
            foreach (var craft in carrier.Combat.Hangar)
            {
                if (!CarrierWings.Flyable(craft)) continue;
                CarrierWings.Berth(spec, craft.Craft.Berth, out int q, out _);
                if (craft.Craft.Role == FighterRole.Recon) recon.Add(craft);
                else
                {
                    q = Math.Max(0, q);
                    if (!squadrons.TryGetValue(q, out var list)) squadrons[q] = list = new List<Ship>();
                    list.Add(craft);
                }
            }
            carrier.Basis(out var right, out var up);
            int next = 0;
            foreach (var kv in squadrons)
            {
                BattleUnit lead = null;
                for (int k = 0; k < kv.Value.Count; k++)
                    lead = LaunchCraft(b, carrier, kv.Value[k], kv.Key, k, lead, right, up, FighterPhase.Launch);
                next = kv.Key + 1;
            }
            BattleUnit reconLead = null;
            for (int k = 0; k < recon.Count; k++)
                reconLead = LaunchCraft(b, carrier, recon[k], next, k, reconLead, right, up, FighterPhase.Approach);
        }

        /// <summary>Docked craft <paramref name="hull"/> out of the bays as slot <paramref name="k"/> of squadron
        /// <paramref name="q"/>, under <paramref name="lead"/> (none: it leads), flying on its own hull with its own pilot
        /// (U.SPC-T526); returns the squadron's lead.</summary>
        static BattleUnit LaunchCraft(Battle b, BattleUnit carrier, Ship hull, int q, int k, BattleUnit lead, DVec3 right, DVec3 up, FighterPhase phase)
        {
            var craft = FighterCraft.Of(hull.Craft.Role);
            // Out of the bow and side bays, fanning ahead of the carrier.
            var pos = carrier.PointOf(carrier.Combat.Subsystems.Find(s => s.Kind == SubsystemKind.Hangar && !s.Destroyed).Def)
                      + carrier.Forward * (0.3 + 0.15 * k) + right * ((q - 1) * 0.6 + (k % 2 == 0 ? 0.1 : -0.1) * k) + up * (0.05 * k);
            CarrierWings.Undock(carrier.Combat, hull);
            hull.Faction = carrier.Faction;
            var u = b.NewUnit(UnitKind.Fighter, carrier.Side, carrier.Faction, hull.Combat, craft.LengthKm, pos, carrier.Forward);
            u.CraftHull = hull;
            hull.Craft.Carrier = carrier.Ship;
            u.Vel = carrier.Vel + carrier.Forward * 0.25;
            u.Power = new PowerDistribution();
            var pilot = CarrierWings.PilotOf(hull, b.Rng);
            pilot.Sorties++;
            u.Fighter = new FighterState
            {
                Carrier = carrier, Lead = lead, Squadron = q, Slot = k, Bomber = craft.Role == FighterRole.Bomber, Role = craft.Role, Phase = phase,
                Skill = pilot.Skill, Reaction = pilot.Reaction, Discipline = pilot.Discipline, Seed = pilot.Seed,
            };
            // Out through a flight deck and its bow mouth, in column with the rest of the launch (U.SPC-T518).
            FlightDecks.Catapult(b, carrier, u);
            return lead ?? u;
        }

        public static void Step(Battle b, BattleUnit u, double dt)
        {
            var f = u.Fighter;
            // On the glide path into a flight deck, or being launched out of one, the deck flies it (U.SPC-T518).
            if (FlightDecks.Fly(b, u, dt)) return;
            // A boarding shuttle flies to its prize, not into the fight (U.SPC-T486).
            if (f.Role == FighterRole.AssaultShuttle)
            {
                Shuttle(b, u, dt);
                return;
            }
            // Its drives knocked out (an EMP): it drifts on the way it was going until they come back.
            if (u.Stunned > 0)
            {
                u.Pos += u.Vel * dt;
                return;
            }
            f.PhaseTime += dt;
            // Its carrier lost or gone (U.SPC-T473): it looks for another friendly carrier or a station to land on.
            StrandedWings.Step(b, u, dt);
            var carrier = f.LandsOn;
            // Recalled: the shooting is over, or the carrier is pulling out.
            if (carrier.Present && !carrier.Captured && (b.Mopping || carrier.Fleeing))
            {
                if (f.Phase != FighterPhase.Return) Switch(f, FighterPhase.Return);
                Land(b, u, dt);
                return;
            }
            // Hurt or out of ordnance: home to the flight deck to rearm and repair, then out again (U.SPC-T518).
            if (FlightDecks.WantsService(b, u))
            {
                if (f.Phase != FighterPhase.Return) Switch(f, FighterPhase.Return);
                Land(b, u, dt);
                return;
            }
            // Recalled and then sent back in (the shooting started again): it turns back to the fight.
            if (f.Phase == FighterPhase.Return)
            {
                FlightDecks.Abort(u);
                Switch(f, FighterPhase.Approach);
            }
            // A lead gone home to its deck leaves the squadron to the next in line until it is back.
            if (f.Lead != null && (!f.Lead.Present || FlightDecks.Away(f.Lead))) f.Lead = NewLead(b, u);
            SharePower(b, u);
            if (f.Lead == null) Lead(b, u, dt);
            else Wingman(b, u, dt);
        }

        /// <summary>
        /// A pilot shares its reactor as the fight needs (U.SPC-T424): shields when they are low or something is on its
        /// tail, engines to break off or run home, weapons on the run or in the dogfight, an even share otherwise.
        /// </summary>
        public static void SharePower(Battle b, BattleUnit u)
        {
            var p = u.Power;
            if (p == null) return;
            var f = u.Fighter;
            var lead = f.Lead != null && f.Order == WingOrder.FormUp ? f.Lead.Fighter : f;
            double shields = u.Combat.Spec.Shield > 0 ? u.Combat.Shield / u.Combat.Spec.Shield : 1;
            if (shields < 0.35 || b.MissileInbound(u, Countermeasures.ReachKm) != null) p.Set(1, 4, 1);
            else if (lead.Phase == FighterPhase.Return || lead.Phase == FighterPhase.BreakOff) p.Set(1, 1, 4);
            else if (lead.Phase == FighterPhase.AttackRun || lead.Phase == FighterPhase.Dogfight) p.Set(4, 1, 1);
            else p.Balance();
        }

        /// <summary>The senior surviving wingman takes over the squadron.</summary>
        static BattleUnit NewLead(Battle b, BattleUnit u)
        {
            BattleUnit best = null;
            foreach (var o in b.Units)
            {
                if (!o.Present || o.Kind != UnitKind.Fighter || o.Fighter.Squadron != u.Fighter.Squadron || o.Fighter.Carrier != u.Fighter.Carrier
                    || BoardingShuttles.Is(o) || (o != u && FlightDecks.Away(o))) continue;
                if (best == null || o.Fighter.Slot < best.Fighter.Slot) best = o;
            }
            if (best == null || best == u) return null;
            return best;
        }

        // ---------------------------------------------------------------- the lead

        /// <summary>A target still worth fighting: hostile, present, not struck and not taken.</summary>
        static bool Fair(Battle b, BattleUnit u, BattleUnit t) => t != null && b.Hostile(u, t) && !t.Surrendering && !t.Captured;

        /// <summary>
        /// A craft flying its own runs: a squadron's lead, or a wingman told to fight on its own. With
        /// <paramref name="pinned"/> it fights that target (its lead's, or what is on its lead) instead of choosing.
        /// </summary>
        static void Lead(Battle b, BattleUnit u, double dt, BattleUnit pinned = null)
        {
            var f = u.Fighter;
            var rng = b.Rng;
            // A recon fighter holds off its mark, jamming and painting, instead of flying runs (U.SPC-T485).
            if (f.Role == FighterRole.Recon)
            {
                Recon(b, u, dt, pinned);
                return;
            }
            if (pinned != null)
            {
                if (u.Target != pinned)
                {
                    u.Target = pinned;
                    if (f.Phase == FighterPhase.Dogfight || f.Phase == FighterPhase.AttackRun) Switch(f, FighterPhase.Approach);
                }
            }
            else
            {
                // A target that has struck its colours or changed hands is no longer one.
                bool lost = !Fair(b, u, u.Target);
                if (lost || (f.Phase != FighterPhase.AttackRun && rng.NextDouble() < dt / 20))
                {
                    u.Target = ChooseTarget(b, u);
                    if (f.Phase == FighterPhase.Dogfight || f.Phase == FighterPhase.AttackRun) Switch(f, FighterPhase.Approach);
                }
            }
            var t = u.Target;
            DVec3 goal;
            double speed = u.Combat.Spec.MaxSpeed;

            switch (f.Phase)
            {
                case FighterPhase.Launch:
                    goal = f.Carrier.Pos + f.Carrier.Forward * 4 + DVec3.UnitZ * 0.6;
                    if (f.PhaseTime > 7) Switch(f, FighterPhase.Approach);
                    break;

                case FighterPhase.Approach:
                    if (t == null)
                    {
                        goal = (f.LandsOn.Present ? f.LandsOn.Pos : DVec3.Zero) + new DVec3(Math.Cos(b.Seconds * 0.1 + f.Seed), Math.Sin(b.Seconds * 0.1 + f.Seed), 0.2) * 3;
                        speed *= 0.6;
                        break;
                    }
                    if (t.Kind == UnitKind.Fighter)
                    {
                        Switch(f, FighterPhase.Dogfight);
                        goal = t.Pos;
                        break;
                    }
                    if (f.RunEntry.LengthSquared == 0 || f.PhaseTime < dt * 1.5) f.RunEntry = PickEntry(b, u, t);
                    goal = t.Pos + f.RunEntry;
                    if ((u.Pos - goal).Length < 1.0 || f.PhaseTime > 60) Switch(f, FighterPhase.AttackRun);
                    break;

                case FighterPhase.AttackRun:
                {
                    if (t == null)
                    {
                        Switch(f, FighterPhase.Approach);
                        goal = u.Pos + u.Forward;
                        break;
                    }
                    // Dive at the target, aiming to pass a few hundred metres off its hull.
                    var to = t.Pos - u.Pos;
                    double dist = to.Length;
                    var side = DVec3.Cross(to, DVec3.UnitZ);
                    if (side.LengthSquared < 1e-9) side = DVec3.UnitX;
                    goal = t.Pos + side.Normalized * (t.Radius + 0.35) * Math.Sign(Math.Sin(f.Seed)) + t.Vel * (dist / speed);
                    if (dist < f.RunClosest) f.RunClosest = dist;
                    // Flying away only means 'past' once the run has closed; at its entry 5-7 km out it is also moving outward.
                    bool past = f.RunClosest < t.Radius + 0.8 && DVec3.Dot(u.Vel, to) < 0;
                    if ((past && dist > t.Radius + 0.8) || f.PhaseTime > 45)
                    {
                        var away = (u.Pos - t.Pos).Normalized;
                        f.BreakPoint = t.Pos + (away + DVec3.UnitZ * (rng.NextDouble() - 0.3)).Normalized * (7 + rng.NextDouble() * 3);
                        Switch(f, FighterPhase.BreakOff);
                    }
                    break;
                }

                case FighterPhase.BreakOff:
                    goal = f.BreakPoint;
                    if ((u.Pos - goal).Length < 1.2 || f.PhaseTime > 35)
                    {
                        f.RunEntry = DVec3.Zero;
                        Switch(f, FighterPhase.Approach);
                    }
                    break;

                default:
                {
                    // Dogfight: pursue with lead, jinking a little.
                    if (t == null || t.Kind != UnitKind.Fighter)
                    {
                        Switch(f, FighterPhase.Approach);
                        goal = u.Pos + u.Forward;
                        break;
                    }
                    double dist = (t.Pos - u.Pos).Length;
                    var jink = new DVec3(Math.Sin(b.Seconds * 1.7 + f.Seed), Math.Sin(b.Seconds * 1.3 + f.Seed * 2), Math.Sin(b.Seconds * 0.9 + f.Seed * 3)) * 0.15;
                    goal = t.Pos + t.Vel * Math.Min(dist / speed, 3) + jink;
                    break;
                }
            }
            Steer(b, u, goal, speed, dt);
            Torpedo(b, u);
        }

        /// <summary>
        /// A recon fighter on its own (U.SPC-T485, see <see cref="ElectronicWarfare"/>): it scouts out ahead to its mark (the
        /// capital its bombers are going for, else the one most worth painting, or <paramref name="pinned"/> when its lead has
        /// told it), and holds off it at <see cref="ElectronicWarfare.StandoffKm"/> on the side it came from, edging round it
        /// and matching its drift, where it jams everything near it and paints it for the bombers. It never closes to fight.
        /// </summary>
        static void Recon(Battle b, BattleUnit u, double dt, BattleUnit pinned)
        {
            var f = u.Fighter;
            if (f.Phase != FighterPhase.Approach) Switch(f, FighterPhase.Approach);
            if (pinned != null && pinned.Kind != UnitKind.Fighter) u.Target = pinned;
            else if (!Fair(b, u, u.Target) || b.Rng.NextDouble() < dt / 20) u.Target = ElectronicWarfare.ChooseMark(b, u);
            var t = u.Target;
            double speed = u.Combat.Spec.MaxSpeed;
            if (t == null)
            {
                var home = f.LandsOn.Present ? f.LandsOn.Pos : DVec3.Zero;
                Steer(b, u, home + new DVec3(Math.Cos(b.Seconds * 0.1 + f.Seed), Math.Sin(b.Seconds * 0.1 + f.Seed), 0.2) * 3, speed * 0.6, dt);
                return;
            }
            var from = u.Pos - t.Pos;
            if (from.LengthSquared < 1e-12) from = -t.Forward;
            var dir = from.Normalized;
            var round = DVec3.Cross(dir, DVec3.UnitZ);
            if (round.LengthSquared < 1e-9) round = DVec3.UnitX;
            // Edging round the mark, so its gunners never have a steady line on it.
            dir = (dir + round.Normalized * (0.15 * (Math.Sin(f.Seed) >= 0 ? 1 : -1))).Normalized;
            var station = t.Pos + dir * (ElectronicWarfare.StandoffKm + t.Radius) + t.Vel * 2;
            bool there = (station - u.Pos).Length < 3;
            Steer(b, u, station, speed, dt, there ? t.Vel : (DVec3?)null);
        }

        /// <summary>
        /// What a pilot goes after (U.SPC-T484): the nearest worthwhile hostile, weighed by its craft's class. Fighters are
        /// preferred by <see cref="FighterCraft.HuntsFightersKm"/> (an interceptor's hunt), and more so when one is closing
        /// on a bomber of its own side (<see cref="FighterCraft.ScreensBombers"/>); a bomber never picks a fighter, and goes
        /// first for a capital whose shields facing it are already down, where its torpedoes do their work
        /// (<see cref="StrikeTorpedoes"/>).
        /// </summary>
        public static BattleUnit ChooseTarget(Battle b, BattleUnit u)
        {
            if (u.Fighter.Role == FighterRole.Recon) return ElectronicWarfare.ChooseMark(b, u);
            bool bomber = u.Fighter.Bomber;
            var craft = FighterCraft.Of(u.Fighter.Role);
            BattleUnit best = null;
            double bestScore = double.MaxValue;
            foreach (var o in b.Units)
            {
                if (!b.Hostile(u, o) || o.Surrendering || o.Captured) continue;
                if (o.Kind == UnitKind.Fighter && bomber) continue;
                double d = (o.Pos - u.Pos).Length;
                double score = d;
                if (o.Kind == UnitKind.Fighter)
                {
                    double hunt = craft.HuntsFightersKm;
                    score = d < 9 + hunt ? d - hunt : d + 10;
                    if (craft.ScreensBombers && OnABomber(b, u, o)) score -= ScreenBonusKm;
                }
                else if (o.Ship != null && !o.Ship.Spec.IsMilitary) score += 8;
                else
                {
                    score -= o.Combat.Spec.Hull / 3000.0;
                    if (bomber && WeakPoints.IsCapital(o) && StrikeTorpedoes.ShieldDownToward(o, u.Pos)) score -= BareHullBonusKm;
                    // A capital its own recon fighters are painting is where its torpedoes run truest (U.SPC-T485).
                    if (bomber && ElectronicWarfare.Painted(b, o, u.Side)) score -= ElectronicWarfare.PaintBonusKm;
                }
                score *= 0.8 + b.Rng.NextDouble() * 0.4;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>How much nearer than it is an enemy fighter on one of its own bombers counts for an interceptor, and a
        /// capital with its shields down toward it for a bomber, km.</summary>
        public const double ScreenBonusKm = 8, BareHullBonusKm = 8;

        /// <summary>How near one of <paramref name="u"/>'s side's bombers an enemy fighter must be to count as on it, km.</summary>
        public const double ScreenKm = 4;

        /// <summary>Whether hostile craft <paramref name="o"/> is within <see cref="ScreenKm"/> of a bomber on <paramref name="u"/>'s side.</summary>
        static bool OnABomber(Battle b, BattleUnit u, BattleUnit o)
        {
            foreach (var f in b.Units)
                if (f.Present && f.Kind == UnitKind.Fighter && f.Side == u.Side && f.Fighter.Bomber && (f.Pos - o.Pos).Length < ScreenKm) return true;
            return false;
        }

        /// <summary>A run starts 5-7 km off the target, from a side the squadron picks for itself.</summary>
        static DVec3 PickEntry(Battle b, BattleUnit u, BattleUnit t)
        {
            // A bomber lines its run up on the side a weak point lies open from (U.SPC-T424): under the reactor's vent,
            // astern of the engines, over the shield emitters.
            if (u.Fighter.Bomber && WeakPoints.AiGoesForThem.Value && WeakPoints.Preferred(t) is Subsystem weak)
            {
                var axis = WeakPoints.Axis(t, WeakPoints.SpecOf(weak.Kind));
                var jitter = new DVec3(b.Rng.NextDouble() - 0.5, b.Rng.NextDouble() - 0.5, b.Rng.NextDouble() - 0.5) * 0.5;
                return (axis + jitter).Normalized * (5 + b.Rng.NextDouble() * 2);
            }
            var from = (u.Pos - t.Pos).Normalized;
            var side = DVec3.Cross(from, DVec3.UnitZ);
            if (side.LengthSquared < 1e-9) side = DVec3.UnitX;
            double swing = (b.Rng.NextDouble() - 0.5) * 2.2;
            var dir = (from * Math.Cos(swing) + side.Normalized * Math.Sin(swing) + DVec3.UnitZ * (b.Rng.NextDouble() - 0.4) * 0.6).Normalized;
            return dir * (5 + b.Rng.NextDouble() * 2);
        }

        static void Switch(FighterState f, FighterPhase p)
        {
            f.Phase = p;
            f.PhaseTime = 0;
            f.RunClosest = double.MaxValue;
        }

        static void Torpedo(Battle b, BattleUnit u)
        {
            var f = u.Fighter;
            // Only a lead changes phase; a wingman in the vee flies (and fires on) its lead's run, one fighting on its own its own run.
            var phase = f.Lead != null && FlysInFormation(b, u) ? f.Lead.Fighter.Phase : f.Phase;
            // The lead's own retarget check runs once a tick and can lag a target's death or
            // surrender by a tick (both are resolved later in Battle.Step, after fighters fly);
            // re-check the target's own state here so neither the lead nor a wingman copying its
            // target gets a last torpedo off at a hull that is already struck or already gone.
            if (!f.Bomber || phase != FighterPhase.AttackRun || u.Target == null || u.Target.Kind == UnitKind.Fighter
                || !b.Hostile(u, u.Target) || u.Target.Surrendering || u.Target.Captured) return;
            var to = u.Target.Pos - u.Pos;
            double dist = to.Length;
            if (dist > WeaponSpec.Torpedo.RangeKm || DVec3.Dot(u.Forward, to / dist) < TorpedoBoresightCos) return;
            LaunchTorpedoes(b, u, u.Target);
        }

        /// <summary>How near the nose a torpedo's target must be (cosine): about 25 degrees.</summary>
        public const double TorpedoBoresightCos = 0.9;

        /// <summary>
        /// The hardpoint a torpedo goes for (U.SPC-T424): the weak point the player has picked on the hull they have locked;
        /// for a pilot, the most telling weak point lying open from where it is; else wherever the seeker takes it.
        /// </summary>
        public static Subsystem TorpedoAimPoint(Battle b, BattleUnit u, BattleUnit target)
        {
            if (u.PlayerControlled)
            {
                var picked = u.PlayerSubTarget;
                if (target == u.PlayerLockTarget && picked != null && !picked.Destroyed && target.Combat.Subsystems.Contains(picked)) return picked;
            }
            else if (WeakPoints.AiGoesForThem.Value && WeakPoints.BestFrom(target, u.Pos) is Subsystem open) return open;
            return b.PickSubsystem(target, u);
        }

        /// <summary>Every ready torpedo the craft carries, away at <paramref name="target"/>.</summary>
        static void LaunchTorpedoes(Battle b, BattleUnit u, BattleUnit target)
        {
            foreach (var s in u.Combat.Subsystems)
            {
                if (s.Kind != SubsystemKind.Torpedo || s.Destroyed || s.Ammo <= 0 || s.Cooldown > 0) continue;
                s.Ammo--;
                s.Cooldown = s.Def.Weapon.Cooldown;
                var sub = TorpedoAimPoint(b, u, target);
                b.Missiles.Add(new Missile
                {
                    Pos = u.Pos, Vel = u.Vel, Target = target, TargetSub = sub, Damage = s.Def.Weapon.Damage,
                    Speed = s.Def.Weapon.MissileSpeedKmS + u.Vel.Length, Hp = 25, Life = 30, Side = u.Side, Torpedo = true,
                    Kind = MissileKind.Torpedo, SmallCraft = true,
                });
                b.Emit(FxKind.MissileLaunch, u.Pos, u.Pos + u.Forward, 0, u.Side, u, s.Index);
            }
        }

        // ---------------------------------------------------------------- flown by the player (U.SPC-T422)

        /// <summary>
        /// One step of a launched craft the player has taken (<see cref="FighterSeat"/>), flown by its own class's flight
        /// model (U.SPC-T17, U.SPC-T424; see <see cref="FighterCraft.Model"/>): the nose swings toward the stick at the
        /// craft's own rate and spin-up, flight assist holds the throttle's share of its top speed along the nose (strafing,
        /// rolling and the afterburner as the model allows, or off, drifting on its momentum), and engine power scales its
        /// thrust and top speed. The guns fire down the crosshair through the battle's usual player path; the second
        /// trigger fires the selected group: missiles at the locked target once the lock is made (the battle's own path),
        /// torpedoes at the locked hull, or the hostile hull nearest the nose. Its own drives knocked out, it drifts.
        /// </summary>
        public static void FlyManned(Battle b, BattleUnit u, FighterControl stick, double dt)
        {
            // The lock is its target: wingmen in the vee fire on it, and told to, go after it.
            if (u.PlayerLockTarget != null && !u.PlayerLockTarget.Present)
            {
                u.PlayerLockTarget = null;
                u.PlayerSubTarget = null;
            }
            u.Target = u.PlayerLockTarget;
            u.PlayerSecondaryHeld = stick.Secondary && stick.SecondaryGroup == WeaponGroup.Missiles;
            // Lined up on a friendly flight deck's glide path, the deck takes it in, turns it round and launches it (U.SPC-T518).
            if (FlightDecks.FlyManned(b, u, stick, dt)) return;
            if (u.Stunned > 0)
            {
                u.Pos += u.Vel * dt;
                return;
            }
            var craft = u.Fighter != null ? FighterCraft.Of(u.Fighter.Role) : FighterCraft.Interceptor;
            double engines = PowerDistribution.FactorOf(u, PowerSystem.Engines);
            var hull = HullDynamics.Of(u.Combat.Spec);
            hull = new HullDynamics(hull.MassKt, hull.MainAccel * engines, hull.RetroAccel * engines, hull.LateralAccel * engines, hull.VerticalAccel * engines,
                hull.PitchYawAngularAccel, hull.RollAngularAccel);
            var state = new PilotState
            {
                Pos = u.Pos, Vel = u.Vel, Forward = u.Forward,
                Up = stick.Up.LengthSquared > 1e-12 ? stick.Up : PilotState.LevelUp(u.Forward),
                AngularVelocity = stick.AngularVelocity,
            };
            var input = new PilotInput
            {
                DesiredForward = stick.Aim.LengthSquared > 1e-12 ? stick.Aim.Normalized : DVec3.Zero,
                Throttle = Math.Max(0, Math.Min(1, stick.Throttle)),
                StrafeRight = stick.StrafeRight, StrafeUp = stick.StrafeUp, Roll = stick.Roll,
                Boost = stick.Boost, InCombat = true, FlightAssistOff = stick.FlightAssistOff,
            };
            craft.Model.Step(ref state, u.Combat, hull, craft.Tuning(engines), input, DVec3.Zero, dt);
            u.Pos = state.Pos;
            u.Vel = state.Vel;
            u.Forward = state.Forward;
            stick.Up = state.Up;
            u.Up = state.Up;
            stick.AngularVelocity = state.AngularVelocity;
            if (stick.Secondary && stick.SecondaryGroup == WeaponGroup.Torpedoes && TorpedoTarget(b, u) is BattleUnit target) LaunchTorpedoes(b, u, target);
        }

        /// <summary>The hostile hull (never a fighter, never one that has struck or been taken) a torpedo goes at: the locked
        /// one if it is within torpedo range and the boresight, else the one nearest the nose that is; null if none.</summary>
        public static BattleUnit TorpedoTarget(Battle b, BattleUnit u)
        {
            var locked = u.PlayerLockTarget;
            if (locked != null && locked.Present && locked.Kind != UnitKind.Fighter && Fair(b, u, locked))
            {
                var toLocked = locked.Pos - u.Pos;
                double d = toLocked.Length;
                if (d > 1e-9 && d - locked.Radius <= WeaponSpec.Torpedo.RangeKm && DVec3.Dot(u.Forward, toLocked / d) >= TorpedoBoresightCos) return locked;
            }
            BattleUnit best = null;
            double bestCos = TorpedoBoresightCos;
            foreach (var o in b.Units)
            {
                if (!o.Present || o.Kind == UnitKind.Fighter || !b.Hostile(u, o) || o.Surrendering || o.Captured) continue;
                var to = o.Pos - u.Pos;
                double dist = to.Length;
                if (dist < 1e-9 || dist - o.Radius > WeaponSpec.Torpedo.RangeKm) continue;
                double cos = DVec3.Dot(u.Forward, to / dist);
                if (cos >= bestCos)
                {
                    bestCos = cos;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>Back to the carrier's hangar and aboard; each landing puts a craft back in its hangar. A stranded craft
        /// lands on the carrier or station it found instead (U.SPC-T473): a station has no hangar of a carrier's to count it in.</summary>
        static void Land(Battle b, BattleUnit u, double dt)
        {
            var carrier = u.Fighter.LandsOn;
            // A hull with a flight deck open has it fly the approach (U.SPC-T518): to the gate astern of the deck, holding
            // off while the deck is busy, then cleared onto the glide path and in through the stern mouth.
            var flightDeck = FlightDecks.Assign(u);
            if (flightDeck != null)
            {
                if (FlightDecks.TryFinal(b, u, flightDeck, dt))
                {
                    FlightDecks.Fly(b, u, dt);
                    return;
                }
                var gate = FlightDecks.InboundGoal(b, u, flightDeck);
                double lead = Math.Min(5, (gate - u.Pos).Length / Math.Max(1e-6, u.Combat.Spec.MaxSpeed));
                Steer(b, u, gate + carrier.Vel * lead, u.Combat.Spec.MaxSpeed, dt, carrier.Vel, homing: true);
                return;
            }
            Subsystem bay = null;
            foreach (var s in carrier.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Hangar && !s.Destroyed) bay = s;
            var deck = bay != null ? carrier.PointOf(bay.Def) : carrier.Pos;
            // Line up astern of the bay and come in matched to the carrier.
            var approach = deck - carrier.Forward * 0.4;
            double toDeck = (deck - u.Pos).Length;
            var goal = toDeck < 0.6 ? deck : approach;
            Steer(b, u, goal + carrier.Vel * 2, u.Combat.Spec.MaxSpeed, dt, carrier.Vel, homing: true);
            if (toDeck < Math.Max(0.12, u.Vel.Length * dt * 1.5))
            {
                u.Docked = true;
                TakeAboard(carrier, u);
            }
        }

        /// <summary>A craft back aboard <paramref name="home"/>: a fighter docked in its hangar as itself, damage and pilot and all
        /// (U.SPC-T526; a full hangar, or a station's, has no berth for it); a boarding shuttle docked in its shuttle berths as
        /// itself the same way (U.SPC-T486, U.SPC-T590), and any of its team still aboard back to the barracks.</summary>
        public static void TakeAboard(BattleUnit home, BattleUnit craft)
        {
            if (home?.Combat == null) return;
            if (!BoardingShuttles.Is(craft))
            {
                if (craft.CraftHull != null) CarrierWings.Dock(home.Combat, craft.CraftHull);
                else home.Combat.RestoreFighters(1);
                return;
            }
            if (craft.CraftHull != null) CarrierWings.Dock(home.Combat, craft.CraftHull);
            else home.Combat.RestoreShuttles(1);
            double team = craft.Combat.Interior.TakeMarines(craft.Combat.Interior.Marines);
            if (team > 0) home.Combat.Interior.Embark(0, team);
        }

        // ---------------------------------------------------------------- boarding shuttles (U.SPC-T486)

        /// <summary>
        /// Sends a boarding shuttle out of <paramref name="carrier"/>'s hangar toward <paramref name="target"/> with
        /// <paramref name="team"/> marines aboard, who fight as well as they drilled in the carrier's habitats. The shuttle is
        /// <paramref name="hull"/> (U.SPC-T590), else the first fit to fly docked in the carrier's shuttle berths, undocked to
        /// fly as itself on its own hull with its own pilot; a carrier with none docked sends a new one.
        /// </summary>
        public static BattleUnit LaunchShuttle(Battle b, BattleUnit carrier, BattleUnit target, double team, Ship hull = null)
        {
            var craft = FighterCraft.AssaultShuttle;
            hull ??= CarrierWings.ReadyShuttle(carrier.Combat) ?? CarrierWings.NewCraft(FighterRole.AssaultShuttle, -1);
            CarrierWings.Undock(carrier.Combat, hull);
            hull.Faction = carrier.Faction;
            hull.Craft.Carrier = carrier.Ship;
            Subsystem bay = null;
            foreach (var s in carrier.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Hangar && !s.Destroyed) bay = s;
            var pos = (bay != null ? carrier.PointOf(bay.Def) : carrier.Pos) + carrier.Forward * 0.3;
            var u = b.NewUnit(UnitKind.Fighter, carrier.Side, carrier.Faction, hull.Combat, craft.LengthKm, pos, carrier.Forward);
            u.CraftHull = hull;
            u.Vel = carrier.Vel + carrier.Forward * 0.1;
            u.Power = new PowerDistribution();
            var pilot = CarrierWings.PilotOf(hull, b.Rng);
            pilot.Sorties++;
            u.Fighter = new FighterState
            {
                Carrier = carrier, Squadron = -1, Role = craft.Role, Phase = FighterPhase.Launch, Drill = carrier.Combat.Interior.DrillFactor,
                Skill = pilot.Skill, Reaction = pilot.Reaction, Discipline = pilot.Discipline, Seed = pilot.Seed,
            };
            u.BoardTarget = target;
            if (team > 0) u.Combat.Interior.Embark(0, team);
            FlightDecks.Catapult(b, carrier, u);
            return u;
        }

        /// <summary>
        /// A boarding shuttle's step (U.SPC-T486): latched on, it rides with the hull it is boarding until the fight aboard is
        /// decided (the battle casts it off); otherwise, with a team aboard and a prize it may still board
        /// (<see cref="Battle.ShuttleMayBoard"/>), it flies to it and docks (<see cref="Dock"/>), finding another prize near it
        /// if that one is taken or gone; with none, its team delivered, or its carrier pulling out, it flies home to land.
        /// </summary>
        static void Shuttle(Battle b, BattleUnit u, double dt)
        {
            var f = u.Fighter;
            if (u.Latched != null)
            {
                u.Pos = u.Latched.PointLocal(u.LatchLocal);
                u.Vel = u.Latched.Vel;
                return;
            }
            if (u.Stunned > 0)
            {
                u.Pos += u.Vel * dt;
                return;
            }
            f.PhaseTime += dt;
            StrandedWings.Step(b, u, dt);
            var home = f.LandsOn;
            bool recalled = home.Present && home.Fleeing;
            if (u.BoardTarget != null && (recalled || !b.ShuttleMayBoard(u, u.BoardTarget))) u.BoardTarget = null;
            if (u.BoardTarget == null && !recalled && u.Combat.Interior.Marines >= 0.5) u.BoardTarget = b.ShuttleTarget(u);
            if (u.BoardTarget == null)
            {
                if (f.Phase != FighterPhase.Return) Switch(f, FighterPhase.Return);
                Land(b, u, dt);
                return;
            }
            if (f.Phase != FighterPhase.Approach) Switch(f, FighterPhase.Approach);
            Dock(b, u, u.BoardTarget, dt);
        }

        /// <summary>
        /// Flies a shuttle alongside <paramref name="t"/>: to a point off its flank, slowing as it closes and holding the
        /// hull's own drift, nose turning to the hull's heading at the last. Lying there matched
        /// (<see cref="BoardingShuttles.DockKm"/>, <see cref="BoardingShuttles.MatchKmS"/>) for
        /// <see cref="BoardingShuttles.LatchSeconds"/>, it latches on and its team goes in (<see cref="Battle.ShuttleLatch"/>).
        /// </summary>
        static void Dock(Battle b, BattleUnit u, BattleUnit t, double dt)
        {
            var spec = u.Combat.Spec;
            double engines = PowerDistribution.FactorOf(u, PowerSystem.Engines);
            var dock = u.DockPoint(t, BoardingShuttles.StandOffKm);
            var to = dock - u.Pos;
            double dist = to.Length;
            // Arrive matched: the closer, the slower, on top of the hull's own drift.
            double want = Math.Min(spec.MaxSpeed * engines, dist * 0.05 + 0.002);
            var desired = t.Vel + (dist > 1e-6 ? to / dist : DVec3.Zero) * want;
            var dv = desired - u.Vel;
            double step = spec.Accel * engines * dt;
            u.Vel += dv.Length > step ? dv.Normalized * step : dv;
            u.Pos += u.Vel * dt;
            var face = dist > 0.5 ? to / dist : t.Forward;
            double turn = spec.TurnRateDegS * Astro.Deg2Rad * dt;
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(u.Forward, face))));
            u.Forward = angle <= turn ? face : DVec3.TurnStep(u.Forward, face, turn / angle);

            if (dist < BoardingShuttles.DockKm && (u.Vel - t.Vel).Length < BoardingShuttles.MatchKmS)
            {
                u.LatchTimer += dt;
                if (u.LatchTimer >= BoardingShuttles.LatchSeconds.Value) b.ShuttleLatch(u, t);
            }
            else u.LatchTimer = 0;
        }

        // ---------------------------------------------------------------- wingmen

        /// <summary>V-formation slots behind the lead (km: right, up, forward), alternating sides.</summary>
        static DVec3 SlotOffset(int slot)
        {
            int rank = (slot + 1) / 2;
            double side = slot % 2 == 1 ? 1 : -1;
            return new DVec3(side * 0.16 * rank, 0.03 * rank, -0.14 * rank);
        }

        /// <summary>
        /// What a wingman's order leaves it fighting on its own (U.SPC-T424): its lead's target, whatever is on its lead,
        /// or one of its own; null when it holds the vee.
        /// </summary>
        static bool Independent(Battle b, BattleUnit u, out BattleUnit pinned)
        {
            var f = u.Fighter;
            pinned = null;
            switch (f.Order)
            {
                case WingOrder.AttackMyTarget:
                    pinned = Fair(b, u, f.Lead.Target) ? f.Lead.Target : null;
                    return pinned != null;
                case WingOrder.CoverMe:
                    pinned = Wing.Threat(b, f.Lead);
                    return pinned != null;
                case WingOrder.EngageAtWill:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Whether a wingman is holding the vee on its lead (rather than fighting on its own, as ordered).</summary>
        static bool FlysInFormation(Battle b, BattleUnit u) => u.Fighter.Lead != null && !Independent(b, u, out _);

        /// <summary>Gap to its station inside which a wingman holds the lead's heading (km).</summary>
        const double StationDeadBandKm = 0.03;

        static void Wingman(Battle b, BattleUnit u, double dt)
        {
            var f = u.Fighter;
            var lead = f.Lead;
            var lf = lead.Fighter;
            if (Independent(b, u, out var pinned))
            {
                Lead(b, u, dt, pinned);
                return;
            }
            u.Target = lead.Target;
            // Station: the slot in the lead's frame, stretched on the run, plus this pilot's own wander.
            lead.Basis(out var right, out var up);
            double spread = lf.Phase == FighterPhase.AttackRun ? 2.6 : lf.Phase == FighterPhase.Dogfight ? 4.0 : 1.0;
            spread *= 1.6 - f.Discipline;
            var slot = SlotOffset(f.Slot) * spread;
            double t = b.Seconds;
            var wander = new DVec3(
                Math.Sin(t * 0.37 + f.Seed) + 0.5 * Math.Sin(t * 1.13 + f.Seed * 1.7),
                Math.Sin(t * 0.29 + f.Seed * 2.3) + 0.5 * Math.Sin(t * 0.91 + f.Seed * 0.4),
                Math.Sin(t * 0.21 + f.Seed * 3.1)) * (0.05 * (1.4 - f.Discipline));
            var station = lead.Pos + right * (slot.X + wander.X) + up * (slot.Y + wander.Y) + lead.Forward * (slot.Z + wander.Z);
            // Reaction lag: they fly to where the lead was heading a moment ago, not where it is now.
            station -= lead.Vel * f.Reaction * 0.08;
            // Close on the station itself, the lead's velocity as feed-forward; inside a small dead-band hold the lead's heading
            // rather than pointing the nose at a near-zero gap vector (U.SPC-T559).
            var gap = station - u.Pos;
            var want = lead.Vel + gap * 1.2;
            double maxSpeed = u.Combat.Spec.MaxSpeed * 1.3;
            // Steer scales the requested speed by engine power; undo that here so the feed-forward stays the lead's own speed.
            double engines = Math.Max(1e-6, PowerDistribution.FactorOf(u, PowerSystem.Engines));
            double speed = Math.Min(maxSpeed, want.Length / engines);
            if (gap.Length < StationDeadBandKm || want.Length < 1e-6)
                Steer(b, u, u.Pos + lead.Forward, speed, dt);
            else
                Steer(b, u, u.Pos + want, speed, dt);
            Torpedo(b, u);
        }

        // ---------------------------------------------------------------- flying

        /// <summary>Flies toward a point: the nose turns at the craft's rate and the craft goes where it points.</summary>
        static void Steer(Battle b, BattleUnit u, DVec3 goal, double speed, double dt, DVec3? match = null, bool homing = false)
        {
            var spec = u.Combat.Spec;
            // Power to the engines (U.SPC-T424) is speed and thrust.
            double engines = PowerDistribution.FactorOf(u, PowerSystem.Engines);
            speed *= engines;
            var to = goal - u.Pos;
            double dist = to.Length;
            // Stay inside the battle -- except homing on its carrier, wherever that has drifted to.
            if (!homing && u.Pos.Length > Battle.EscapeKm * 0.8) to = -u.Pos;
            double toLength = to.Length;
            var desiredDir = toLength > 1e-6 ? to / toLength : u.Forward;
            Battle.TurnToward(u, desiredDir, spec.TurnRateDegS, dt);
            // Throttle back when closing on a slot so wingmen do not overshoot.
            double want = speed;
            // Given a velocity to match (a carrier's, a lead's), even a standing one: an unmoving carrier is still closed on gently.
            if (match is DVec3 m) want = Math.Min(speed, m.Length + dist * 1.2);
            var desiredVel = u.Forward * want;
            var dv = desiredVel - u.Vel;
            double step = spec.Accel * engines * dt;
            u.Vel += dv.Length > step ? dv.Normalized * step : dv;
            u.Pos += u.Vel * dt;
        }
    }
}
