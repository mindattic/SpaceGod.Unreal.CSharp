using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Fighter escorts for the merchant fleet (U.SPC-T275). A freighter in port takes on a wing to see it out, sized by
    /// what its hold is worth and how dangerous the waters it is in and bound for are: none on a cheap run through quiet
    /// space, a light fighter or two as the stakes rise, and on a rich cargo into trouble the usual full wing of a heavy
    /// fighter (a gunship) and two light ones. Its owners pay to take each one on and a daily wage while it flies with
    /// them, out of the company's cash (or its flag's treasury for a hull no company owns), and a wing is only taken on
    /// that the owners can well afford. The escorts fly with the freighter from then on, as any escort does.
    /// </summary>
    public sealed class MerchantEscorts
    {
        /// <summary>Days between looks over the merchants in port.</summary>
        public const double CheckDays = 0.25;

        /// <summary>The most fighter escorts the whole merchant fleet keeps at once, so the system does not fill with them.</summary>
        public const int MaxEscorts = 60;

        /// <summary>What it costs to take on a light and a heavy fighter, and each escort's wage a day, credits.</summary>
        public const double LightHireFee = 60000, HeavyHireFee = 140000, DailyWage = 4000;

        /// <summary>Owners take on a wing only with this many times its cost in hand.</summary>
        const double AffordFactor = 4;

        readonly World world;
        readonly Random rng;
        double nextJd;
        int named;

        /// <summary>Every fighter taken on here and not yet paid off (U.SPC-T288). A gate crossing takes the hull this
        /// tracks (the same object) on into the destination's own fleet (see <see cref="InterstellarTraffic"/>), so
        /// the far system's <see cref="MarkHired"/> has to pick each crossed escort back up there, or it would fly
        /// on untracked and never be paid off once its merchant is gone (U.SPC-T301).</summary>
        readonly HashSet<Ship> hired = new HashSet<Ship>();

        /// <summary>Whether <paramref name="s"/> is a fighter hired here and not yet paid off.</summary>
        public bool IsHired(Ship s) => hired.Contains(s);

        /// <summary>Picks up tracking of <paramref name="s"/> as hired here: how an escort that crossed a gate with its
        /// merchant is still paid off when that merchant is gone, instead of flying on forever once the system that
        /// hired it is left behind (U.SPC-T301).</summary>
        public void MarkHired(Ship s) => hired.Add(s);

        /// <summary>Drops tracking of <paramref name="s"/> here, e.g. because it has crossed a gate into another
        /// system's fleet and is <see cref="MarkHired"/> there instead (U.SPC-T301).</summary>
        public void UnmarkHired(Ship s) => hired.Remove(s);

        public MerchantEscorts(World world, int seed)
        {
            this.world = world;
            rng = new Random(seed);
        }

        /// <summary>
        /// How many escorts a freighter carrying <paramref name="cargoValue"/> credits of goods through waters as
        /// dangerous as <paramref name="danger"/> (0 quiet to 1 a war zone, see <see cref="Contracts.DangerAt"/>) wants:
        /// 0 to 3. The cargo counts by its order of magnitude, from a hundred thousand credits up; danger counts heavily.
        /// </summary>
        public static int Wanted(double cargoValue, double danger)
        {
            double worth = Math.Max(0, Math.Min(2, Math.Log10(Math.Max(1, cargoValue)) - 5));
            double score = worth + 4 * Math.Max(0, danger);
            return score < 0.6 ? 0 : score < 1.4 ? 1 : score < 2.2 ? 2 : 3;
        }

        /// <summary>The wing a freighter wanting <paramref name="count"/> escorts takes on: light fighters for one or two,
        /// a heavy fighter leading two light ones for three.</summary>
        public static ShipClass[] Wing(int count, Random rng)
        {
            ShipClass Light() => rng.NextDouble() < 0.6 ? ShipClass.Fighter : ShipClass.Courier;
            switch (count)
            {
                case 1: return new[] { Light() };
                case 2: return new[] { Light(), Light() };
                case 3: return new[] { ShipClass.Gunship, Light(), Light() };
                default: return new ShipClass[0];
            }
        }

        /// <summary>The hulls a wing is made of.</summary>
        static bool IsFighter(ShipClass c) => c == ShipClass.Fighter || c == ShipClass.Interceptor || c == ShipClass.Courier || c == ShipClass.Gunship;

        public static double HireFee(ShipClass c) => c == ShipClass.Gunship ? HeavyHireFee : LightHireFee;

        /// <summary>Takes wings on for the freighters in port that want them, pays every escort its wage, and pays off
        /// the wings that have outlived their merchants.</summary>
        public void Update(double jd)
        {
            if (jd < nextJd) return;
            double days = nextJd > 0 ? jd - nextJd + CheckDays : CheckDays;
            nextJd = jd + CheckDays;
            PayOffOrphans();

            var wings = new Dictionary<Ship, int>();
            int total = 0;
            foreach (var s in world.Fleet.Ships)
            {
                if (s.Destroyed || !s.IsEscort || s.Leader == null || !world.IsTrader(s.Leader)) continue;
                wings.TryGetValue(s.Leader, out int n);
                wings[s.Leader] = n + 1;
                // The cap is on the fighter wings hired here, not on the older escorts some merchants sail with.
                if (IsFighter(s.Spec.Class)) total++;
            }
            // Wages first: a wing flies for as long as it is paid.
            foreach (var kv in wings) Pay(kv.Key, kv.Value * DailyWage * Math.Min(days, 2));

            // The richest cargoes into the worst waters first: the fleet's escorts go where they are needed most.
            var candidates = new List<(Ship ship, int want, int have, double stake)>();
            foreach (var s in world.Fleet.Ships)
            {
                if (!Wants(s)) continue;
                wings.TryGetValue(s, out int have);
                double value = s.HasCargo ? s.CargoTonnes * GoodInfo.Of(s.CargoGood).BasePrice : 0;
                double danger = Math.Max(DangerAt(s.Orbit.Body), DangerAt(s.DestinationBody));
                int want = Wanted(value, danger);
                if (want > have) candidates.Add((s, want, have, value * (1 + 4 * danger)));
            }
            candidates.Sort((x, y) => y.stake.CompareTo(x.stake));

            var hire = new List<(Ship leader, ShipClass[] wing)>();
            foreach (var (s, want, have, _) in candidates)
            {
                if (total + want - have > MaxEscorts) continue;
                var wing = Wing(want, rng);
                // Only what the freighter has not got yet: the first places of the wing are the ones it already flies with.
                var missing = new ShipClass[want - have];
                Array.Copy(wing, have, missing, 0, missing.Length);
                double cost = 0;
                foreach (var c in missing) cost += HireFee(c);
                if (Purse(s) < cost * AffordFactor) continue;
                hire.Add((s, missing));
                total += missing.Length;
            }
            foreach (var (leader, wing) in hire)
            {
                wings.TryGetValue(leader, out int have);
                foreach (var c in wing)
                {
                    var e = world.Fleet.AddShip(leader.Name + " escort " + (++named), c, leader.Faction, ShipRole.Escort);
                    world.Fleet.AddEscort(leader, e, Fleet.SlotFor(have++));
                    Pay(leader, HireFee(c));
                    world.NotifyAdded(e);
                    hired.Add(e);
                }
            }
        }

        /// <summary>
        /// A hired fighter whose merchant is gone (lost, scrapped, or the wing handed on to its strongest) has nobody to
        /// guard and nobody paying it: at its next port, out of any fight, it is paid off and leaves the fleet (no wreck),
        /// and so does the rest of its wing still flying with it (U.SPC-T288). Until then it flies on as it was.
        /// </summary>
        void PayOffOrphans()
        {
            var gone = new List<Ship>();
            foreach (var s in hired)
            {
                if (s.Destroyed) { gone.Add(s); continue; }
                var lead = s.Leader;
                bool guarding = lead != null && !lead.Destroyed && world.IsTrader(lead);
                // Still with a merchant, or following the one of its wing that is (the wing flies on as a group).
                if (guarding || (lead != null && hired.Contains(lead) && !lead.Destroyed && lead.Leader != null && world.IsTrader(lead.Leader))) continue;
                var head = lead != null && hired.Contains(lead) ? lead : s;
                if (head.Phase != ShipPhase.Parked || head.Battle != null || s.Battle != null || head == world.PilotedShip || s == world.PilotedShip) continue;
                gone.Add(s);
                s.Destroyed = true;
                s.GateDeparted = true;
                // Gone where its wing lay, not parked with no orbit of its own (an escort has none, US-T59).
                if (s.Leader != null) s.TakeMotionOf(s.Leader);
                s.Leader = null;
            }
            foreach (var s in gone) hired.Remove(s);
        }

        /// <summary>A merchant that takes escorts on: a lawful freighter or miner in port, not the player's (neither the
        /// hull at the helm nor one of the player's own fleet, whose escorts are the player's to order).</summary>
        bool Wants(Ship s) =>
            !s.Destroyed && !s.IsEscort && world.IsTrader(s) && s.Phase == ShipPhase.Parked && s.Battle == null && s != world.PilotedShip &&
            !(world.Holdings != null && world.Holdings.Owns(s)) &&
            s.Faction != null && !s.Faction.IsPirate && !s.Derelict && s.Orbit != null &&
            (s.Spec.Class == ShipClass.Freighter || s.Spec.Class == ShipClass.Miner);

        double DangerAt(int body) => world.Contracts != null ? world.Contracts.DangerAt(body) : 0;

        /// <summary>What the freighter's owners have in hand: its company's cash, or its flag's treasury.</summary>
        static double Purse(Ship s) => s.Company != null ? s.Company.Cash : s.Faction.Treasury;

        static void Pay(Ship s, double credits)
        {
            if (s.Company != null) s.Company.Cash -= credits;
            else if (s.Faction != null) s.Faction.Treasury -= credits;
        }
    }
}
