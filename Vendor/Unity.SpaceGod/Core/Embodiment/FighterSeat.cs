using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The cockpit of a launched fighter, heavy fighter or bomber, as an <see cref="IEmbodiable"/> (U.SPC-T422): one of a
    /// carrier's squadron, taken mid-battle and flown by the player (the X-wing run on the dreadnought). Taken, its pilot
    /// lets go and it flies by its own class's flight model (<see cref="FighterAI.FlyManned"/>), and every gunner on the
    /// other side shoots at it for real. The dogfight game (U.SPC-T424) is played from here: the guns on the main trigger
    /// and a selectable weapon group (missiles, torpedoes) on the second; a lock taken on the hostile nearest the
    /// crosshair, and a weak point picked on a locked capital ship for the missiles and torpedoes to go for; flares
    /// against missiles closing in; the reactor's power shared between weapons, shields and engines; and orders to the
    /// squadron's wingmen, who fly on it from the first order. Handed back, its pilot flies on from wherever it was left,
    /// picking a run of its own.
    /// </summary>
    public sealed class FighterSeat : IEmbodiable
    {
        public Battle Battle { get; }
        public BattleUnit Unit { get; }

        public FighterSeat(Battle battle, BattleUnit unit)
        {
            Battle = battle;
            Unit = unit;
        }

        /// <summary>What it is: "interceptor", "fighter", "heavy fighter", "bomber" or "assault shuttle".</summary>
        public string Craft
        {
            get
            {
                var f = Unit.Fighter;
                if (f == null) return "craft";
                return f.Bomber ? "bomber" : FighterCraft.Of(f.Role).Name;
            }
        }

        public string Name
        {
            get
            {
                var f = Unit.Fighter;
                if (f == null) return Unit.Name;
                // A boarding shuttle flies in no squadron (U.SPC-T486).
                if (f.Squadron < 0) return f.Carrier != null ? f.Carrier.Name + " " + Craft : Craft;
                return f.Carrier != null ? f.Carrier.Name + " " + Craft + " " + (f.Squadron + 1) + "-" + (f.Slot + 1) : Craft;
            }
        }

        public EmbodimentKind Kind => EmbodimentKind.Fighter;
        public Faction Faction => Unit.Faction;
        public IEmbodiable Host => null;
        public bool CanEmbody => Blocker == null;
        public bool Embodied => Battle.Flown.ContainsKey(Unit);

        public string Blocker
        {
            get
            {
                if (Unit.Kind != UnitKind.Fighter || Unit.Fighter == null) return "not a launched craft";
                if (Battle.Over) return "the fight is over";
                if (Unit.Dead) return "shot down";
                // Down on a flight deck being turned round to launch again, it is still the player's (U.SPC-T518).
                if (Unit.Docked && !FlightDecks.TurningRound(Unit)) return "back aboard its carrier";
                if (!Unit.Present) return "gone";
                if (Unit.Captured) return "taken as a prize";
                return null;
            }
        }

        public EmbodimentPose PoseAt(double jd)
        {
            var up = Battle.Flown.TryGetValue(Unit, out var stick) && stick.Up.LengthSquared > 1e-12 ? stick.Up : UpOf(Unit);
            return new EmbodimentPose(Battle.Body, Unit.Pos + Battle.Anchor, Unit.Forward, up);
        }

        static DVec3 UpOf(BattleUnit u)
        {
            u.Basis(out _, out var up);
            return up;
        }

        /// <summary>The pilot lets go: the stick is the player's, holding the heading and speed the craft had.</summary>
        public void TakeCharge()
        {
            double top = Unit.Combat.Spec.MaxSpeed;
            var groups = WeaponGroups.Secondaries(Unit);
            Battle.Flown[Unit] = new FighterControl
            {
                Aim = Unit.Forward, Throttle = top > 0 ? Math.Min(1, Unit.Vel.Length / top) : 0, Up = UpOf(Unit),
                SecondaryGroup = groups.Count > 0 ? groups[0] : WeaponGroup.Missiles,
            };
            Unit.PlayerControlled = true;
            Unit.PlayerFireHeld = false;
            Unit.PlayerSecondaryHeld = false;
            Unit.PlayerLockTarget = null;
            Unit.PlayerSubTarget = null;
            Unit.LockProgress = 0;
            Unit.PlayerAim = Unit.Forward;
            if (Unit.Power == null) Unit.Power = new PowerDistribution();
            // Its pilot's share of the reactor was for its pilot's fight; the player starts even.
            Unit.Power.Balance();
        }

        /// <summary>Stick, throttle (-1..1 eases it off or opens it up; it holds where it is left), strafe, roll, boost and triggers.</summary>
        public void Control(in EmbodimentInput input, double dt)
        {
            if (!Battle.Flown.TryGetValue(Unit, out var stick)) return;
            if (input.Aim.LengthSquared > 1e-12) stick.Aim = input.Aim.Normalized;
            stick.Throttle = Math.Max(0, Math.Min(1, stick.Throttle + input.Throttle * ThrottlePerSecond * dt));
            stick.StrafeRight = input.StrafeRight;
            stick.StrafeUp = input.StrafeUp;
            stick.Roll = input.Roll;
            stick.Boost = input.Boost;
            stick.Secondary = input.Secondary;
            Unit.PlayerFireHeld = input.Primary;
            Unit.PlayerAim = stick.Aim;
        }

        /// <summary>How fast a held throttle key opens or closes the throttle: full range in two seconds.</summary>
        public const double ThrottlePerSecond = 0.5;

        /// <summary>Its pilot takes the stick back and flies a run of its own from here.</summary>
        public void HandBack()
        {
            Battle.Flown.Remove(Unit);
            Unit.PlayerControlled = false;
            Unit.PlayerFireHeld = false;
            Unit.PlayerSecondaryHeld = false;
            Unit.PlayerDecoyRequested = false;
            Unit.PlayerLockTarget = null;
            Unit.PlayerSubTarget = null;
            Unit.LockProgress = 0;
            var f = Unit.Fighter;
            if (f == null) return;
            // Nothing snaps back to a stale run: a fresh approach from where it is, on a target it picks now.
            if (f.Phase != FighterPhase.Return)
            {
                f.Phase = FighterPhase.Approach;
                f.PhaseTime = 0;
            }
            f.RunEntry = DVec3.Zero;
            Unit.Target = null;
        }

        FighterControl Stick => Battle.Flown.TryGetValue(Unit, out var s) ? s : null;

        // ------------------------------------------------------------ the dogfight game (U.SPC-T424)

        /// <summary>The group on the second trigger, or null when not held.</summary>
        public WeaponGroup? SecondaryGroup => Stick?.SecondaryGroup;

        /// <summary>Puts the next guided group the craft carries on the second trigger; returns it, or null if it carries none.</summary>
        public WeaponGroup? CycleSecondaryGroup()
        {
            var stick = Stick;
            var groups = WeaponGroups.Secondaries(Unit);
            if (stick == null || groups.Count == 0) return null;
            int i = groups.IndexOf(stick.SecondaryGroup);
            stick.SecondaryGroup = groups[(i + 1) % groups.Count];
            return stick.SecondaryGroup;
        }

        /// <summary>How far round the crosshair a lock may be taken, deg, and how far off, km.</summary>
        public const double LockPickDeg = 30, LockPickKm = 15;

        /// <summary>
        /// Takes a lock on the hostile nearest the crosshair <paramref name="aim"/> (within <see cref="LockPickDeg"/> and
        /// <see cref="LockPickKm"/>); pressed again with that one already locked, the next nearest. A capital ship locked
        /// gets its most telling weak point picked. The lock then builds while the crosshair sits on it. Returns the lock, or null.
        /// </summary>
        public BattleUnit CycleLock(DVec3 aim)
        {
            if (Stick == null) return null;
            var look = aim.LengthSquared > 1e-12 ? aim.Normalized : Unit.Forward;
            var candidates = new List<(BattleUnit unit, double angle)>();
            foreach (var o in Battle.Units)
            {
                if (!Battle.Hostile(Unit, o) || o.Surrendering || o.Captured) continue;
                var to = o.Pos - Unit.Pos;
                double d = to.Length;
                if (d < 1e-9 || d > LockPickKm) continue;
                double angle = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(look, to / d)))) * Astro.Rad2Deg;
                if (angle <= LockPickDeg) candidates.Add((o, angle));
            }
            candidates.Sort((a, b) => a.angle.CompareTo(b.angle));
            BattleUnit pick = null;
            if (candidates.Count > 0)
            {
                int i = candidates.FindIndex(c => c.unit == Unit.PlayerLockTarget);
                pick = candidates[i < 0 ? 0 : (i + 1) % candidates.Count].unit;
            }
            Lock(pick);
            return pick;
        }

        /// <summary>Locks <paramref name="target"/> (null drops the lock); a capital ship gets its most telling weak point picked.</summary>
        public void Lock(BattleUnit target)
        {
            if (Unit.PlayerLockTarget != target) Unit.LockProgress = 0;
            Unit.PlayerLockTarget = target;
            Unit.PlayerSubTarget = target != null ? WeakPoints.Preferred(target) : null;
        }

        /// <summary>The next (or previous) weak point on the locked capital ship; returns it, or null if there is none.</summary>
        public Subsystem CycleWeakPoint(int step = 1)
        {
            var t = Unit.PlayerLockTarget;
            if (t == null || !t.Present) return null;
            Unit.PlayerSubTarget = WeakPoints.Cycle(t, Unit.PlayerSubTarget, step);
            return Unit.PlayerSubTarget;
        }

        /// <summary>Throws a flare-and-chaff cloud astern this step, if the dispenser is ready and has any left.</summary>
        public void DropCountermeasures() => Unit.PlayerDecoyRequested = true;

        /// <summary>Clouds left in the craft's dispensers.</summary>
        public int Countermeasures
        {
            get
            {
                int n = 0;
                foreach (var s in Unit.Combat.Subsystems)
                    if (s.Kind == SubsystemKind.Countermeasures && !s.Destroyed) n += s.Ammo;
                return n;
            }
        }

        /// <summary>Flight assist on or off; returns whether it is now off.</summary>
        public bool ToggleFlightAssist()
        {
            var stick = Stick;
            if (stick == null) return false;
            stick.FlightAssistOff = !stick.FlightAssistOff;
            return stick.FlightAssistOff;
        }

        public bool FlightAssistOff => Stick?.FlightAssistOff ?? false;

        /// <summary>Moves a pip of the reactor's power to <paramref name="s"/>; false if it has as much as it can take.</summary>
        public bool ShiftPower(PowerSystem s) => Unit.Power != null && Unit.Power.Shift(s);

        /// <summary>Back to an even share of power.</summary>
        public void BalancePower() => Unit.Power?.Balance();

        /// <summary>Takes the squadron and gives its wingmen <paramref name="order"/>; returns how many heard it.</summary>
        public int OrderWing(WingOrder order) => Wing.Command(Battle, Unit, order);

        /// <summary>The squadron's wingmen still flying.</summary>
        public int Wingmen => Wing.Members(Battle, Unit).Count;

        /// <summary>A guided round closing on the craft (within flare reach), for the HUD's warning.</summary>
        public bool MissileWarning => Battle.MissileInbound(Unit, global::Unity.SpaceGod.Core.Countermeasures.ReachKm * 1.5) != null;

        /// <summary>
        /// The cockpit's readout for the HUD: the craft, hull and shields, throttle and boost, power, the second trigger's
        /// group and rounds, flares, the lock and its weak point (open or not from here), the wing's order, and warnings.
        /// </summary>
        public string Readout()
        {
            var u = Unit;
            var c = u.Combat;
            var spec = c.Spec;
            var stick = Stick;
            var sb = new StringBuilder();
            sb.Append(Craft.ToUpperInvariant()).Append("  ·  ").Append(Name);
            sb.Append("  ·  hull ").Append(Pct(c.Hull / Math.Max(1, spec.Hull)));
            sb.Append("  shields ").Append(Pct(spec.Shield > 0 ? c.Shield / spec.Shield : 0));
            if (stick != null) sb.Append("  ·  throttle ").Append(Pct(stick.Throttle));
            sb.Append("  boost ").Append(Pct(c.BoostFraction));
            if (stick != null && stick.FlightAssistOff) sb.Append("  ASSIST OFF");
            if (u.Power != null) sb.Append("  ·  power ").Append(u.Power);
            if (stick != null && WeaponGroups.Secondaries(u).Count > 0)
                sb.Append("  ·  ").Append(WeaponGroups.Label(stick.SecondaryGroup)).Append(' ').Append(WeaponGroups.Ammo(u, stick.SecondaryGroup));
            sb.Append("  flares ").Append(Countermeasures);
            var t = u.PlayerLockTarget;
            if (t != null && t.Present)
            {
                sb.Append("  ·  lock ").Append(t.Name).Append(' ').Append((t.Pos - u.Pos).Length.ToString("0.0", CultureInfo.InvariantCulture)).Append(" km ");
                sb.Append(u.LockProgress >= 1 ? "LOCKED" : Pct(u.LockProgress));
                var weak = u.PlayerSubTarget;
                if (weak != null && !weak.Destroyed)
                    sb.Append(" @ ").Append(WeakPoints.Label(weak)).Append(WeakPoints.ExposedFrom(t, weak, u.Pos) ? " (OPEN)" : " (covered)");
            }
            if (Wingmen > 0) sb.Append("  ·  wing ").Append(Wingmen).Append(": ").Append(Wing.Label(u.Fighter.Order));
            if (u.Overheated) sb.Append("  ·  GUNS OVERHEATED");
            if (MissileWarning) sb.Append("  ·  MISSILE");
            // Landing guidance for a friendly flight deck near (U.SPC-T518).
            var deck = FlightDecks.Guidance(Battle, u);
            if (deck != null) sb.Append("  ·  ").Append(deck);
            return sb.ToString();
        }

        static string Pct(double f) => Math.Round(100 * Math.Max(0, Math.Min(1, f))).ToString("0", CultureInfo.InvariantCulture) + "%";

        public override bool Equals(object obj) => obj is FighterSeat s && s.Unit == Unit && s.Battle == Battle;
        public override int GetHashCode() => Unit.GetHashCode();
    }
}
