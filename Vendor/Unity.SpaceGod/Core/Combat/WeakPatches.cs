using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>One shield emitter's patch of hull as its captain reads it (U.SPC-T502, see <see cref="WeakPatches"/>).</summary>
    public readonly struct HullPatch
    {
        /// <summary>The emitter whose field covers the patch (shot away when the patch is holed).</summary>
        public readonly Subsystem Emitter;

        /// <summary>Where on the hull it lies, as a captain names it: "bow", "starboard quarter", "port beam".</summary>
        public readonly string Name;

        /// <summary>Its field, 0 (down) to 1 (full).</summary>
        public readonly double Field;

        /// <summary>Its emitter is shot away: the hull is bare there until damage control brings it back.</summary>
        public readonly bool Holed;

        /// <summary>How weak it is, 0 (a full field over a whole emitter) to 1 (holed or its field down).</summary>
        public readonly double Weakness;

        /// <summary>The share of the fire coming in on the hull that this patch faces, 0 to 1.</summary>
        public readonly double Fire;

        public HullPatch(Subsystem emitter, string name, double field, bool holed, double weakness, double fire)
        {
            Emitter = emitter;
            Name = name;
            Field = field;
            Holed = holed;
            Weakness = weakness;
            Fire = fire;
        }

        /// <summary>Weak enough for the helm to call it out: holed, or at least <see cref="WeakPatches.WeakAt"/> weak.</summary>
        public bool Weak => Holed || Weakness >= WeakPatches.WeakAt;
    }

    /// <summary>Fire coming in on a hull from one hostile: the way to it (unit, battle axes) and how heavy it is.</summary>
    public readonly struct IncomingFire
    {
        public readonly DVec3 Dir;
        public readonly double Weight;

        public IncomingFire(DVec3 dir, double weight)
        {
            Dir = dir;
            Weight = weight;
        }
    }

    /// <summary>
    /// Large ships roll to protect their weak spots (U.SPC-T502, docs/SHIP_ROSTER.md "Shields"). Shields are emitters on the
    /// hull, each covering its own patch of it (U.SPC-T501, <see cref="ShieldEmitters"/>), and capitals fight in full 3D
    /// (U.SPC-T499), so a captain can roll and turn its hull to keep a damaged, unshielded or holed patch away from the
    /// heaviest incoming fire and present fresh shields instead. It weighs where the fire is coming from (every armed hostile,
    /// heavier for its firepower, lighter beyond its reach, heavier with a broadside building on this hull,
    /// <see cref="IncomingFireOn"/>) against which patches face each way (<see cref="Exposure(BattleUnit, IReadOnlyList{IncomingFire})"/>),
    /// and trades that against keeping its own guns bearing (<see cref="Protection"/>): it rolls about the line to its
    /// target, which keeps the target where it lies in every gun's fire cone (U.SPC-T533), or turns its hull to another
    /// heading that brings other guns' cones onto the enemy if that shows it fresher shields for what firepower it gives up,
    /// and weighs too how slanted that attitude lies the hull to the fire, so hits glance off its armour (U.SPC-T534,
    /// <see cref="ArmourAngling"/>). An AI captain does this by doctrine (<see cref="AiRolls"/>); at the helm the player sees
    /// which patches are weak and which faces the fire (<see cref="Readout"/>), and can order the helm assist to roll them away
    /// (<see cref="BroadsideCommand.RollWeakPatches"/>), steering the nose themselves. Every figure is a setting in Broadsides.
    /// </summary>
    [SettingsSource]
    public static class WeakPatches
    {
        // ------------------------------------------------------------ settings

        public static readonly ToggleSetting AiRolls = new ToggleSetting("broadside.aiRollPatches", SettingGroups.Broadsides,
            "AI captains roll weak patches away", true,
            "AI capital ships roll and turn their hulls to keep damaged, unshielded or holed patches of hull away from the heaviest incoming fire, presenting fresh shields instead.",
            "roll weak patch shield emitter holed ai doctrine capital 3d");

        public static readonly RangeSetting Protection = new RangeSetting("broadside.patchProtection", SettingGroups.Broadsides,
            "Weak patch protection", 1, 0, 3, 0.1,
            "How much of its batteries' bearing a captain gives up to keep its weak patches out of the heaviest fire: at 1 it would trade its whole broadside to turn a fully holed face away, at 0 it ignores its patches and lays its guns as before.",
            v => v.ToString("0.#", CultureInfo.InvariantCulture) + "x", "roll weak patch shield emitter holed protect trade batteries bearing capital");

        // ------------------------------------------------------------ constants

        /// <summary>How weak a patch must be for the helm to call it out.</summary>
        public const double WeakAt = 0.5;

        /// <summary>How much a damaged emitter's own wounds count toward its patch's weakness (it is the next to go).</summary>
        public const double EmitterDamageWeight = 0.25;

        /// <summary>How often a captain weighs the fire again, s, how much better a new attitude must be to be worth the
        /// roll, and how strongly it otherwise keeps to its rest (level, for an AI captain; where the helm has it, for the
        /// player).</summary>
        public const double ReconsiderSeconds = 1, Hysteresis = 0.04, RestBias = 0.02;

        /// <summary>The rolls a captain weighs, evenly round the full turn.</summary>
        public const int RollSteps = 12;

        /// <summary>No heading turned to a target: the hull rolls about its bow alone.</summary>
        public const int NoBearing = int.MinValue;

        // ------------------------------------------------------------ who rolls

        /// <summary>A hull with two emitters or more: patches it can choose between.</summary>
        public static bool HasPatches(ShipCombat c)
        {
            int n = 0;
            foreach (var s in c.Subsystems)
                if (s.Kind == SubsystemKind.ShieldGenerator && ++n >= 2) return true;
            return false;
        }

        /// <summary>Whether the battle's captain of <paramref name="u"/> rolls its weak patches away: a capital ship flown by
        /// its crew, with patches to choose between, under the doctrine and with protection on.</summary>
        public static bool Steers(BattleUnit u) =>
            AiRolls.Value && Protection.Value > 0 && Broadsides.Commands(u) && !u.PlayerControlled && HasPatches(u.Combat);

        // ------------------------------------------------------------ the patches

        /// <summary>How weak an emitter's patch is: 1 holed, else its field's shortfall plus a share of the emitter's own
        /// damage, at most 1.</summary>
        public static double Weakness(Subsystem emitter)
        {
            if (emitter.Destroyed) return 1;
            return Math.Min(1, 1 - emitter.ShieldFraction + EmitterDamageWeight * (1 - emitter.Health));
        }

        /// <summary>The name a captain gives the patch around a hardpoint at <paramref name="d"/>: its side (or dorsal,
        /// ventral) and how far along the hull, as "starboard bow", "port beam", "stern".</summary>
        public static string NameOf(CombatSpec spec, SubsystemDef d)
        {
            double r = spec.BeamFraction * 0.5, core = Math.Max(0.05, 0.5 - r);
            string along = d.Z > core * 0.4 ? "bow" : d.Z < -core * 0.4 ? "stern" : null;
            double ax = Math.Abs(d.X), ay = Math.Abs(d.Y);
            string side = Math.Max(ax, ay) > r * 0.5 ? (ax >= ay ? (d.X > 0 ? "starboard" : "port") : (d.Y > 0 ? "dorsal" : "ventral")) : null;
            if (side == null) return along ?? "keel";
            bool beam = side == "starboard" || side == "port";
            if (along == null) return beam ? side + " beam" : side;
            return beam ? side + (along == "bow" ? " bow" : " quarter") : side + " " + along;
        }

        // ------------------------------------------------------------ the fire

        /// <summary>The longest reach of <paramref name="c"/>'s live guns and launchers, km.</summary>
        static double ReachKm(ShipCombat c)
        {
            double reach = 0;
            foreach (var s in c.Subsystems)
            {
                var w = s.Def.Weapon;
                if (w == null || s.Destroyed || w.Supporting) continue;
                reach = Math.Max(reach, w.RangeKm);
            }
            return reach;
        }

        /// <summary>
        /// Where the fire on <paramref name="u"/> is coming from: every armed hostile still fighting, weighted by its
        /// firepower (<see cref="ShipCombat.Strength"/>), falling off with the square of the distance beyond its reach, and
        /// heavier for a broadside it is building on <paramref name="u"/>.
        /// </summary>
        public static List<IncomingFire> IncomingFireOn(Battle b, BattleUnit u)
        {
            var list = new List<IncomingFire>();
            foreach (var o in b.Units)
            {
                if (o == u || !o.Present || !b.Hostile(u, o) || o.Captured || o.Surrendering || o.Stunned > 0) continue;
                var c = o.Combat;
                if (c.Disabled || !c.Armed) continue;
                var to = o.Pos - u.Pos;
                double d = to.Length;
                if (d < 1e-9) continue;
                double w = c.Strength;
                if (w <= 0) continue;
                double reach = ReachKm(c);
                if (reach > 0 && d > reach) w *= reach / d * (reach / d);
                var cmd = o.Broadside;
                if (cmd != null && cmd.ReadyingOn == u) w *= 1 + cmd.ReadyingLoaded;
                list.Add(new IncomingFire(to / d, w));
            }
            return list;
        }

        // ------------------------------------------------------------ exposure

        /// <summary>A sample of the hull's surface (ship lengths, hull frame): where, its outward normal, and the area it stands for.</summary>
        struct Sample
        {
            public DVec3 Point, Normal;
            public double Area;
        }

        /// <summary>The hull's surface sampled evenly, as the capsule <see cref="Collisions.SurfacePoint"/> strikes: rings
        /// along its length and its two rounded ends, each sample standing for its share of the area.</summary>
        static Sample[] SamplesOf(CombatSpec spec)
        {
            const int around = 8, stations = 4;
            double r = spec.BeamFraction * 0.5, core = Math.Max(0, 0.5 - r);
            var list = new List<Sample>(around * stations + 2 * (around + 1));
            double side = 2 * Math.PI * r * 2 * core / (around * stations);
            if (side > 0)
                for (int i = 0; i < stations; i++)
                {
                    double z = -core + (i + 0.5) * 2 * core / stations;
                    for (int k = 0; k < around; k++)
                    {
                        double a = 2 * Math.PI * k / around;
                        var n = new DVec3(Math.Cos(a), Math.Sin(a), 0);
                        list.Add(new Sample { Point = new DVec3(0, 0, z) + n * r, Normal = n, Area = side });
                    }
                }
            double cap = 2 * Math.PI * r * r / (around + 1);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                var end = new DVec3(0, 0, sign * core);
                var tip = new DVec3(0, 0, sign);
                list.Add(new Sample { Point = end + tip * r, Normal = tip, Area = cap });
                double h = Math.Sqrt(0.5);
                for (int k = 0; k < around; k++)
                {
                    double a = 2 * Math.PI * k / around;
                    var n = new DVec3(Math.Cos(a) * h, Math.Sin(a) * h, sign * h);
                    list.Add(new Sample { Point = end + n * r, Normal = n, Area = cap });
                }
            }
            return list.ToArray();
        }

        /// <summary>Each sample's patch (its emitter's index in the hull's hardpoints, -1 for none).</summary>
        static int[] PatchesOf(ShipCombat c, Sample[] samples)
        {
            var patch = new int[samples.Length];
            for (int i = 0; i < samples.Length; i++) patch[i] = c.PatchAt(samples[i].Point)?.Index ?? -1;
            return patch;
        }

        /// <summary>How weak each sample's patch is.</summary>
        static double[] WeaknessOf(ShipCombat c, int[] patch)
        {
            var weak = new double[patch.Length];
            for (int i = 0; i < patch.Length; i++) weak[i] = patch[i] < 0 ? 0 : Weakness(c.Subsystems[patch[i]]);
            return weak;
        }

        /// <summary>The weak share of the face a hull in frame (<paramref name="right"/>, <paramref name="up"/>,
        /// <paramref name="forward"/>) shows the fire, each hostile's by its weight: 0 when every patch the fire can see is
        /// whole, 1 when all are holed.</summary>
        static double Exposure(Sample[] samples, double[] weak, IReadOnlyList<IncomingFire> fire, DVec3 right, DVec3 up, DVec3 forward)
        {
            double total = 0, weights = 0;
            foreach (var f in fire)
            {
                var d = new DVec3(DVec3.Dot(f.Dir, right), DVec3.Dot(f.Dir, up), DVec3.Dot(f.Dir, forward));
                double face = 0, bad = 0;
                for (int i = 0; i < samples.Length; i++)
                {
                    double k = DVec3.Dot(samples[i].Normal, d);
                    if (k <= 0) continue;
                    k *= samples[i].Area;
                    face += k;
                    bad += k * weak[i];
                }
                if (face <= 0) continue;
                total += f.Weight * bad / face;
                weights += f.Weight;
            }
            return weights > 0 ? total / weights : 0;
        }

        /// <summary>The weak share of the face <paramref name="u"/>, as it lies now, shows <paramref name="fire"/>, 0 to 1.</summary>
        public static double Exposure(BattleUnit u, IReadOnlyList<IncomingFire> fire)
        {
            var samples = SamplesOf(u.Combat.Spec);
            var weak = WeaknessOf(u.Combat, PatchesOf(u.Combat, samples));
            u.Basis(out var right, out var up);
            return Exposure(samples, weak, fire, right, up, u.Forward);
        }

        /// <summary>
        /// The helm's readout of <paramref name="u"/>'s patches (U.SPC-T502): each emitter's patch by name, its field,
        /// whether it is holed or weak, and the share of the fire coming in on the hull that it faces as the hull lies now.
        /// </summary>
        public static List<HullPatch> Readout(Battle b, BattleUnit u)
        {
            var c = u.Combat;
            var list = new List<HullPatch>();
            if (!HasPatches(c)) return list;
            var samples = SamplesOf(c.Spec);
            var patch = PatchesOf(c, samples);
            var fire = IncomingFireOn(b, u);
            u.Basis(out var right, out var up);
            var share = new double[c.Subsystems.Count];
            double all = 0;
            foreach (var f in fire)
            {
                var d = new DVec3(DVec3.Dot(f.Dir, right), DVec3.Dot(f.Dir, up), DVec3.Dot(f.Dir, u.Forward));
                for (int i = 0; i < samples.Length; i++)
                {
                    double k = DVec3.Dot(samples[i].Normal, d);
                    if (k <= 0 || patch[i] < 0) continue;
                    k *= samples[i].Area * f.Weight;
                    share[patch[i]] += k;
                    all += k;
                }
            }
            var names = new Dictionary<string, int>();
            foreach (var s in c.Subsystems)
            {
                if (s.Kind != SubsystemKind.ShieldGenerator) continue;
                string name = NameOf(c.Spec, s.Def);
                names.TryGetValue(name, out int seen);
                names[name] = seen + 1;
                if (seen > 0) name += " " + (seen + 1).ToString(CultureInfo.InvariantCulture);
                list.Add(new HullPatch(s, name, s.ShieldFraction, s.Destroyed, Weakness(s), all > 0 ? share[s.Index] / all : 0));
            }
            return list;
        }

        // ------------------------------------------------------------ the attitude

        /// <summary>The roof of a hull with its nose along <paramref name="forward"/> held level (toward ecliptic north), as
        /// <see cref="BattleUnit.Basis"/> holds one with no roll.</summary>
        public static DVec3 LevelRoof(DVec3 forward)
        {
            if (forward.LengthSquared < 1e-18) return DVec3.UnitZ;
            var f = forward.Normalized;
            // Continuous up to the pole (U.SPC-T562); X only when the nose is exactly along it.
            var upRef = DVec3.Cross(f, DVec3.UnitZ).LengthSquared > 1e-12 ? DVec3.UnitZ : DVec3.UnitX;
            var right = DVec3.Cross(f, upRef).Normalized;
            return DVec3.Cross(right, f);
        }

        /// <summary>The frame of one attitude: with the hull turned so a target along <paramref name="toTarget"/> lies
        /// <paramref name="bearing"/> off its bow (<see cref="Battle.BowFor"/>), the level bow for it rolled
        /// <paramref name="rollDeg"/> about the line to the target (so the same guns' cones hold it all the same); with none,
        /// <paramref name="face"/> rolled about itself.</summary>
        static void Frame(DVec3 face, DVec3? toTarget, int bearing, double rollDeg, out DVec3 forward, out DVec3 roof)
        {
            DVec3 f0, axis;
            if (bearing != NoBearing && toTarget is DVec3 t)
            {
                axis = t.Normalized;
                f0 = Battle.BowFor(axis, bearing).Normalized;
            }
            else
            {
                f0 = face.Normalized;
                axis = f0;
            }
            double a = rollDeg * Astro.Deg2Rad;
            forward = PilotFlight.RotateAbout(f0, axis, a).Normalized;
            roof = PilotFlight.RotateAbout(LevelRoof(f0), axis, a).Normalized;
        }

        /// <summary>
        /// The attitude <paramref name="u"/>'s captain wants this step to keep its weak patches out of the heaviest fire,
        /// <paramref name="face"/> being where it would point its bow otherwise. With <paramref name="bearings"/> (the firepower
        /// its guns' fire cones would bring to bear from each heading, by the bearing the target would lie on,
        /// <see cref="Battle.ConeFirepower"/>) and a target along <paramref name="toTarget"/> it weighs every such heading, each
        /// rolled every way about the line to the target; with none, every roll about the bow alone. Each attitude scores the
        /// share of its guns' firepower that bears, less <see cref="Protection"/>
        /// times the weak share of the face it shows the fire (<see cref="IncomingFireOn"/>), less
        /// <see cref="ArmourAngling.AiWeight"/> times the share of the fire's harm its armour would let home at that slant
        /// (<see cref="ArmourAngling.Harm(CombatSpec, DVec3)"/>, U.SPC-T534), less a little for straying from
        /// <paramref name="rest"/> (null: level). The choice is weighed every <see cref="ReconsiderSeconds"/> and changed only
        /// for a better one by <see cref="Hysteresis"/>, so the hull settles instead of hunting.
        /// </summary>
        public static void Orient(Battle b, BattleUnit u, DVec3 face, IReadOnlyDictionary<int, double> bearings, DVec3? toTarget, DVec3? rest, double dt,
            out DVec3 forward, out DVec3 roof, DVec3? actual = null)
        {
            if (face.LengthSquared < 1e-18) face = u.Forward;
            var cmd = Broadsides.Of(u);
            bool aiming = bearings != null && bearings.Count > 0 && toTarget is DVec3 t0 && t0.LengthSquared > 1e-18;
            if (cmd == null || !HasPatches(u.Combat))
            {
                forward = face.Normalized;
                roof = LevelRoof(forward);
                return;
            }
            cmd.RollReconsider -= dt;
            bool valid = cmd.RollChosen && (aiming ? bearings.ContainsKey(cmd.RollBearing) : cmd.RollBearing == NoBearing);
            if (!valid || cmd.RollReconsider <= 0)
            {
                cmd.RollReconsider = ReconsiderSeconds;
                Choose(b, u, cmd, face, aiming ? bearings : null, aiming ? toTarget : null, rest, actual);
            }
            Frame(face, aiming ? toTarget : null, aiming ? cmd.RollBearing : NoBearing, cmd.RollDeg, out forward, out roof);
            // The roof is carried over to the heading the hull actually lies on, to roll it there; the bow is still turned
            // toward the heading it wants, or a hull still coming round would never get there (U.SPC-T534).
            var wanted = forward;
            Transport(actual, ref forward, ref roof);
            forward = wanted;
        }

        /// <summary>A hull still turning toward the heading it wants lies along <paramref name="actual"/>, not along
        /// <paramref name="forward"/>: carries the attitude chosen for the wanted heading over to the actual one (the same roll
        /// about the nose, by the shortest turn between them), so the roll is weighed and ordered for the heading the fire
        /// really meets, and blends into the wanted one as the hull comes round.</summary>
        static void Transport(DVec3? actual, ref DVec3 forward, ref DVec3 roof)
        {
            if (!(actual is DVec3 a) || a.LengthSquared < 1e-18) return;
            a = a.Normalized;
            var axis = DVec3.Cross(forward, a);
            double sin = axis.Length, cos = DVec3.Dot(forward, a);
            if (sin > 1e-9)
            {
                double ang = Math.Atan2(sin, cos);
                roof = PilotFlight.RotateAbout(roof, axis / sin, ang).Normalized;
            }
            else if (cos < 0) return;
            forward = a;
        }

        static double Cost(ShipCombat c, Sample[] samples, double[] weak, IReadOnlyList<IncomingFire> fire, double protect, double slant, DVec3 f, DVec3 up)
        {
            var right = DVec3.Cross(f, up).Normalized;
            double cost = 0;
            if (protect > 0) cost += protect * Exposure(samples, weak, fire, right, up, f);
            if (slant > 0) cost += slant * ArmourAngling.Harm(c.Spec, fire, right, up, f);
            return cost;
        }

        static void Choose(Battle b, BattleUnit u, BroadsideCommand cmd, DVec3 face, IReadOnlyDictionary<int, double> bearings, DVec3? toTarget, DVec3? rest, DVec3? actual)
        {
            var c = u.Combat;
            var fire = IncomingFireOn(b, u);
            var samples = SamplesOf(c.Spec);
            var weak = WeaknessOf(c, PatchesOf(c, samples));
            double protect = Protection.Value;
            // A hull slanted to the fire turns part of every hit aside on its armour (U.SPC-T534): the captain weighs that too.
            double slant = ArmourAngling.Strength.Value > 0 ? ArmourAngling.AiWeight.Value : 0;
            double most = 0;
            if (bearings != null)
                foreach (var kv in bearings)
                    most = Math.Max(most, kv.Value);
            var keys = new List<int>();
            if (bearings != null) keys.AddRange(bearings.Keys);
            else keys.Add(NoBearing);
            double best = double.NegativeInfinity;
            int bestBearing = keys[0];
            double bestDeg = 0;
            double step = 360.0 / RollSteps;
            foreach (int bearing in keys)
            {
                double battery = bearings == null ? 1 : most > 0 ? bearings[bearing] / most : 1;
                // The heading is judged in its own frame (U.SPC-T561), by the best roll it allows there; the roll itself is
                // then ordered for the heading the hull lies on, which a hull still turning has not yet left (U.SPC-T535).
                double ownBest = double.NegativeInfinity, rollBest = double.NegativeInfinity;
                double rollDeg = 0;
                for (int k = 0; k < RollSteps; k++)
                {
                    double deg = k * step;
                    Frame(face, toTarget, bearing, deg, out var f, out var up);
                    double own = battery;
                    if (fire.Count > 0) own -= Cost(c, samples, weak, fire, protect, slant, f, up);
                    double at0 = 0;
                    var at = rest ?? LevelRoof(f);
                    var restUp = at - f * DVec3.Dot(at, f);
                    if (restUp.LengthSquared > 1e-18) at0 = RestBias * (1 - DVec3.Dot(up, restUp.Normalized)) * 0.5;
                    double bonus = cmd.RollChosen && bearing == cmd.RollBearing && Math.Abs(Astro.WrapDegrees360(deg - cmd.RollDeg + 180) - 180) < step * 0.5 ? Hysteresis : 0;
                    double carried = own;
                    if (actual != null && fire.Count > 0)
                    {
                        var f2 = f;
                        var up2 = up;
                        Transport(actual, ref f2, ref up2);
                        carried = battery - Cost(c, samples, weak, fire, protect, slant, f2, up2);
                    }
                    own += bonus - at0;
                    carried += bonus - at0;
                    if (own > ownBest) ownBest = own;
                    if (carried > rollBest + 1e-12)
                    {
                        rollBest = carried;
                        rollDeg = deg;
                    }
                }
                if (ownBest > best + 1e-12)
                {
                    best = ownBest;
                    bestBearing = bearing;
                    bestDeg = rollDeg;
                }
            }
            cmd.RollBearing = bestBearing;
            cmd.RollDeg = bestDeg;
            cmd.RollChosen = true;
        }

        /// <summary>The signed angle, radians, from <paramref name="from"/> to <paramref name="to"/> about the axis
        /// <paramref name="axis"/> (both taken square to it): positive is a right-handed roll about it.</summary>
        public static double RollAngle(DVec3 from, DVec3 to, DVec3 axis)
        {
            var k = axis.Normalized;
            var a = from - k * DVec3.Dot(from, k);
            var c = to - k * DVec3.Dot(to, k);
            if (a.LengthSquared < 1e-18 || c.LengthSquared < 1e-18) return 0;
            return Math.Atan2(DVec3.Dot(DVec3.Cross(a, c), k), DVec3.Dot(a, c));
        }

        /// <summary>Rolls a hull the battle flies toward <paramref name="roof"/> about its nose, no faster than
        /// <paramref name="degPerSec"/>: the roll its captain orders (<see cref="BattleUnit.Roof"/>).</summary>
        public static void RollToward(BattleUnit u, DVec3 roof, double degPerSec, double dt)
        {
            var f = u.Forward;
            var cur = u.Roof.LengthSquared > 1e-12 ? u.Roof : LevelRoof(f);
            cur -= f * DVec3.Dot(cur, f);
            var want = roof - f * DVec3.Dot(roof, f);
            if (want.LengthSquared < 1e-18) return;
            want = want.Normalized;
            if (cur.LengthSquared < 1e-18)
            {
                u.Roof = want;
                return;
            }
            cur = cur.Normalized;
            double angle = RollAngle(cur, want, f);
            double max = Math.Max(0, degPerSec) * Astro.Deg2Rad * dt;
            u.Roof = Math.Abs(angle) <= max ? want : PilotFlight.RotateAbout(cur, f.Normalized, Math.Sign(angle) * max).Normalized;
        }

        /// <summary>
        /// At the helm, with the order given (<see cref="BroadsideCommand.RollWeakPatches"/>): the roll input, -1 to 1, the
        /// helm assist puts on to bring the hull (nose along <paramref name="forward"/>, roof <paramref name="up"/>) round to
        /// the roll that keeps its weak patches out of the heaviest fire. The player steers the nose; the assist only rolls,
        /// at up to <paramref name="rollRateDegS"/>, easing off so it stops on the roll at <paramref name="rollAlpha"/> (rad/s²).
        /// </summary>
        public static double HelmRoll(Battle b, BattleUnit u, DVec3 forward, DVec3 up, double rollRateDegS, double rollAlpha, double dt)
        {
            if (rollRateDegS <= 0 || forward.LengthSquared < 1e-18) return 0;
            Orient(b, u, forward, null, null, up, dt, out _, out var roof);
            double angle = RollAngle(up, roof, forward);
            if (Math.Abs(angle) < 0.5 * Astro.Deg2Rad) return 0;
            double top = rollRateDegS * Astro.Deg2Rad;
            double rate = rollAlpha > 0 ? Math.Min(top, Math.Sqrt(2 * rollAlpha * Math.Abs(angle))) : top;
            return Math.Sign(angle) * rate / top;
        }

        /// <summary>Toggles the player's order to roll the weak patches away; returns whether it is now given.</summary>
        public static bool ToggleOrder(BattleUnit u)
        {
            var cmd = Broadsides.Of(u);
            if (cmd == null) return false;
            cmd.RollWeakPatches = !cmd.RollWeakPatches;
            cmd.RollChosen = false;
            return cmd.RollWeakPatches;
        }
    }
}
