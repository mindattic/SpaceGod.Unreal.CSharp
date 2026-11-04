using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A station as an <see cref="IEmbodiable"/> (U.SPC-T447): one of a settlement's defence platforms, or one of the forts
    /// guarding a jump gate. It never moves, but it has a place of its own whether or not a fight is going on round it
    /// (<see cref="PoseAt"/>), and taken it lays every gun it has on the player's aim at each gun's own traverse rate, within
    /// each gun's own fire cone, and in a fight fires them all down their barrels on the trigger (every shot a real aimed
    /// one, through <see cref="Battle.FireManned"/>); the second trigger launches whatever craft its hangars hold. Its
    /// launchers and everything else go on under its crew. Handed back, its crew works every gun again from where the
    /// player left it. Its <see cref="MountedTurret"/>s have it as their <see cref="IEmbodiable.Host"/>, so a single gun
    /// seat can move up to the whole station.
    /// </summary>
    public sealed class StationSeat : IEmbodiable
    {
        readonly World world;

        /// <summary>The fight it was found in, if it was found through one: tried first when finding its battle unit.</summary>
        readonly Battle seen;

        /// <summary>The settlement it defends (null for a gate fort).</summary>
        public Settlement Settlement { get; }

        /// <summary>The gate it guards (null for a settlement's platform).</summary>
        public JumpGate Gate { get; }

        /// <summary>The station's own hull: its guns, hangars and damage, kept between fights.</summary>
        public ShipCombat Combat { get; }

        /// <summary>The second trigger as last seen, so a held trigger launches once.</summary>
        bool secondaryWas;

        public StationSeat(World world, Settlement settlement, JumpGate gate, ShipCombat combat, Battle battle = null)
        {
            this.world = world;
            Settlement = settlement;
            Gate = gate;
            Combat = combat;
            seen = battle;
        }

        /// <summary>The world it stands in: a fort's is its gate's system's.</summary>
        World WorldOf => Gate?.Home?.World ?? world;

        /// <summary>Which of its settlement's platforms (or its gate's forts) it is, and how many there are.</summary>
        int Index => Settlement != null ? Settlement.Platforms.IndexOf(Combat) : Gate != null ? Gate.Forts.IndexOf(Combat) : -1;

        int Count => Settlement != null ? Settlement.Platforms.Count : Gate != null ? Gate.Forts.Count : 0;

        public string Name
        {
            get
            {
                string what = Settlement != null ? Settlement.Name + " defence platform" : Gate != null ? Gate.Name + " fort" : "Station";
                int i = Index;
                return Count > 1 && i >= 0 ? what + " " + (i + 1) : what;
            }
        }

        public EmbodimentKind Kind => EmbodimentKind.Station;

        public Faction Faction
        {
            get
            {
                var u = Unit(out _);
                if (u != null) return u.Faction;
                return Settlement != null ? Settlement.Owner : Gate?.Owner;
            }
        }

        /// <summary>A station is mounted on nothing.</summary>
        public IEmbodiable Host => null;

        public bool CanEmbody => Blocker == null;

        public bool Embodied => Combat != null && Combat.StationHeld;

        public string Blocker
        {
            get
            {
                if (Combat == null || (Settlement == null && Gate == null)) return "nothing there";
                var u = Unit(out _);
                if (u != null)
                {
                    if (u.Dead || !Combat.Alive) return "destroyed";
                    if (u.Captured) return "taken as a prize";
                    if (!u.Present) return "gone";
                    return null;
                }
                if (!Combat.Alive || Index < 0) return "destroyed";
                return null;
            }
        }

        /// <summary>
        /// Its place in the fight going on round it, or null when there is none: the unit in a battle still on whose hull is
        /// this station's (the fight it was found through first, then any in its world).
        /// </summary>
        public BattleUnit Unit(out Battle battle)
        {
            battle = null;
            if (Combat == null) return null;
            var u = UnitIn(seen);
            if (u != null)
            {
                battle = seen;
                return u;
            }
            var w = WorldOf;
            if (w?.War == null) return null;
            foreach (var b in w.War.Battles)
            {
                if (b == seen) continue;
                u = UnitIn(b);
                if (u != null)
                {
                    battle = b;
                    return u;
                }
            }
            return null;
        }

        BattleUnit UnitIn(Battle b)
        {
            if (b == null || b.Over) return null;
            foreach (var u in b.Units)
                if (u.Combat == Combat && u.Kind == UnitKind.Platform) return u;
            return null;
        }

        /// <summary>
        /// Where it holds station outside a fight: a settlement's platforms ring its world in the ecliptic at the distance its
        /// fights are fought (<see cref="Battle.AnchorDistanceKm"/>), a gate's forts ring the gate 6 km out (as they stand in a
        /// gate battle), each facing out from what it guards, roof to ecliptic north.
        /// </summary>
        public static (int body, DVec3 offsetKm, DVec3 forward) RestingPlace(World world, Settlement settlement, JumpGate gate, int index, int count)
        {
            double a = Astro.TwoPi * Math.Max(0, index) / Math.Max(1, count);
            var round = new DVec3(Math.Cos(a), Math.Sin(a), 0);
            if (gate != null)
            {
                var w = gate.Home?.World ?? world;
                int root = w != null ? w.Catalog.Root.Index : 0;
                return (root, gate.PositionKm + round * 6, round);
            }
            int body = settlement?.Body ?? 0;
            double r = world != null && body >= 0 && body < world.Catalog.Count ? Battle.AnchorDistanceKm(world.Catalog[body]) : 6;
            return (body, round * r, round);
        }

        public EmbodimentPose PoseAt(double jd)
        {
            var u = Unit(out var b);
            if (u != null)
            {
                u.Basis(out _, out var up);
                return new EmbodimentPose(b.Body, u.Pos + b.Anchor, u.Forward, up);
            }
            var (body, offset, forward) = RestingPlace(world, Settlement, Gate, Index, Count);
            return new EmbodimentPose(body, offset, forward, PilotState.LevelUp(forward));
        }

        /// <summary>Every gun on it the player can lay right now (direct fire, intact).</summary>
        IEnumerable<Subsystem> Guns()
        {
            foreach (var s in Combat.Subsystems)
                if (MountedTurret.IsMannable(s.Def) && !s.Destroyed) yield return s;
        }

        /// <summary>The station's crew stand aside from its guns: they are the player's, still pointing where they were left.</summary>
        public void TakeCharge()
        {
            Combat.StationHeld = true;
            secondaryWas = false;
            var u = Unit(out var b);
            if (u != null) Man(b, u);
        }

        /// <summary>Every gun on <paramref name="u"/> not already the station's is laid by the player from now on (a gun
        /// manned on its own is taken into the whole), sharing the one hand on them all.</summary>
        MountControl Man(Battle b, BattleUnit u)
        {
            MountControl hand = null;
            foreach (var s in Guns())
                if (b.Manned.TryGetValue(s, out var m) && m.ByStation)
                {
                    hand = m;
                    break;
                }
            hand = hand ?? new MountControl { ByStation = true };
            foreach (var s in Guns())
            {
                if (s.Aim.LengthSquared <= 1e-12) s.Aim = MountedTurret.RestAim(u, s.Def);
                b.Manned[s] = hand;
            }
            return hand;
        }

        /// <summary>
        /// One frame at the station: every gun swings toward the aim at its own rate within its own cone (in a fight, by the
        /// battle's own step; out of one, here), the trigger fires them in a fight, and the second trigger, pressed,
        /// launches its craft.
        /// </summary>
        public void Control(in EmbodimentInput input, double dt)
        {
            if (!Embodied) return;
            var u = Unit(out var b);
            if (u != null)
            {
                var hand = Man(b, u);
                if (input.Aim.LengthSquared > 1e-12) hand.Aim = input.Aim.Normalized;
                hand.Trigger = input.Primary;
                if (input.Secondary && !secondaryWas) LaunchCraft();
            }
            else if (input.Aim.LengthSquared > 1e-12) TrainGuns(input.Aim.Normalized, dt);
            secondaryWas = input.Secondary;
        }

        /// <summary>Out of a fight, its guns traverse toward <paramref name="aim"/> at their own rates, each within its own
        /// cone, reckoned from where the station holds station; nothing is fired at.</summary>
        void TrainGuns(DVec3 aim, double dt)
        {
            var pose = PoseAt(0);
            var standIn = new BattleUnit
            {
                Kind = UnitKind.Platform, Combat = Combat, Faction = Faction, LengthKm = Combat.Spec.LengthKm,
                Forward = pose.Forward, Roof = pose.Up,
            };
            foreach (var s in Guns())
            {
                var current = s.Aim.LengthSquared > 1e-12 ? s.Aim.Normalized : MountedTurret.RestAim(standIn, s.Def);
                var want = MountedTurret.Bound(standIn, s.Def, aim);
                s.Aim = PilotFlight.ClampAim(want, current, MountedTurret.TraverseDegS(s.Kind) * Astro.Deg2Rad * dt);
            }
        }

        /// <summary>
        /// Puts every craft fit to fly in its hangars out into the fight round it (the same launch a carrier makes,
        /// <see cref="FighterAI.Launch"/>); returns how many went. None outside a fight, nor from a station with no hangar
        /// left or nothing aboard.
        /// </summary>
        public int LaunchCraft()
        {
            var u = Unit(out var b);
            if (u == null || Combat.Fighters <= 0) return 0;
            int before = b.Units.Count;
            FighterAI.Launch(b, u);
            return b.Units.Count - before;
        }

        /// <summary>The crew takes every gun back and picks its own targets again; the guns point where they were left.</summary>
        public void HandBack()
        {
            if (Combat == null) return;
            Combat.StationHeld = false;
            secondaryWas = false;
            var u = Unit(out var b);
            if (u == null) return;
            foreach (var s in Combat.Subsystems)
                if (b.Manned.TryGetValue(s, out var m) && m.ByStation) b.Manned.Remove(s);
        }

        public override bool Equals(object obj) => obj is StationSeat s && s.Combat == Combat;
        public override int GetHashCode() => Combat?.GetHashCode() ?? 0;
    }
}
