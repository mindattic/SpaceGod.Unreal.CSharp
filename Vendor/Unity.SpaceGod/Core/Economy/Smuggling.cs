using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>Why a crate can only go in by a port's back door.</summary>
    public enum BackDoorReason
    {
        /// <summary>The open market takes it: no need of the back door.</summary>
        None,

        /// <summary>Stolen goods (bought at a pirate haven): a lawful market will not touch them, but a fence will.</summary>
        Stolen,

        /// <summary>A good the port's owner bans (<see cref="Faction.Contraband"/>).</summary>
        Contraband,

        /// <summary>The port is blockaded: nothing is landed but what slips past the pickets.</summary>
        Blockade,

        /// <summary>The port's owner has closed its ports to the ship's flag.</summary>
        Embargo,
    }

    /// <summary>
    /// Smuggling (U.SPC-T426): every lawful port has a back door. What its open market will not take from the player's
    /// hold, or cannot (stolen goods, a good its owner bans, anything at all when the port is blockaded or its owner has
    /// embargoed the ship's flag), its smugglers will, off the books: no port tax, and at a premium over the port's own
    /// price (<see cref="TradeRules.BackDoorPremium"/>; a blockaded or banned good pays most, a fence takes stolen goods
    /// for less than they are worth). The goods land in the port's stores (a blockade run relieves the port). But customs
    /// may be waiting (<see cref="TradeRules.CustomsChance"/>, more under a blockade's pickets, more for a captain with a
    /// price on their head): caught, the crates are seized and the law charges smuggling, with a witness, against the
    /// port's owner (or the blockader).
    /// </summary>
    public sealed class Smuggling
    {
        readonly TradeDesk desk;
        readonly Random rng;

        public Smuggling(TradeDesk desk, int seed)
        {
            this.desk = desk;
            rng = new Random(seed);
        }

        World World => desk.World;

        /// <summary>Whether <paramref name="s"/> has a back door at all: a lawful port that stands (a haven or lawless station
        /// trades in the open, see <see cref="BlackMarket"/>).</summary>
        public static bool HasBackDoor(Settlement s) =>
            s != null && !s.Fallen && s.Owner != null && !s.Owner.IsPirate && s.Kind != SettlementKind.Haven && !s.Lawless;

        /// <summary>Why <paramref name="item"/> would have to go in by <paramref name="s"/>'s back door from <paramref name="ship"/>, or None.</summary>
        public BackDoorReason ReasonFor(Ship ship, Settlement s, CargoItem item)
        {
            if (!HasBackDoor(s) || item == null || ship == null) return BackDoorReason.None;
            if (World.Diplomacy.BlockadedBy(s) != null) return BackDoorReason.Blockade;
            if (World.Diplomacy.IsEmbargoed(s.Owner, ship.Faction)) return BackDoorReason.Embargo;
            if (s.Owner.Contraband.Contains(item.Good)) return BackDoorReason.Contraband;
            if (item.Stolen) return BackDoorReason.Stolen;
            return BackDoorReason.None;
        }

        /// <summary>The worst reason any crate of <paramref name="g"/> in the hold has (a blockade or embargo covers them all), or None.</summary>
        public BackDoorReason ReasonFor(Ship ship, Settlement s, Good g)
        {
            var best = BackDoorReason.None;
            if (ship == null) return best;
            foreach (var item in ship.PersonalHold.Items)
            {
                if (item.Good != g) continue;
                var r = ReasonFor(ship, s, item);
                if (r > best) best = r;
            }
            return best;
        }

        /// <summary>What the back door pays a tonne of <paramref name="g"/> at <paramref name="s"/> for crates going in for <paramref name="reason"/>.</summary>
        public static double PriceFor(Settlement s, Good g, BackDoorReason reason)
        {
            double local = Math.Max(s.Price[(int)g], 0.5 * GoodInfo.Of(g).BasePrice), premium = TradeRules.BackDoorPremium.Value;
            switch (reason)
            {
                case BackDoorReason.Blockade: return local * premium * 1.2;
                case BackDoorReason.Contraband: return Math.Max(local, GoodInfo.Of(g).BasePrice) * premium * 1.3;
                case BackDoorReason.Embargo: return local * premium;
                case BackDoorReason.Stolen: return local * 0.7;
                default: return 0;
            }
        }

        /// <summary>
        /// The chance customs catches a back-door sale of crates going in for <paramref name="reason"/> at <paramref name="s"/>:
        /// the vigilance setting, more under a blockade's pickets, less for a fence, and more again for a captain the
        /// port's owner (or the blockader) wants; at most 95%.
        /// </summary>
        public double CatchChance(Settlement s, BackDoorReason reason)
        {
            double c = TradeRules.CustomsChance.Value;
            switch (reason)
            {
                case BackDoorReason.Blockade: c *= 1.6; break;
                case BackDoorReason.Stolen: c *= 0.75; break;
                case BackDoorReason.None: return 0;
            }
            var law = World.Law;
            var watcher = Watcher(s, reason);
            if (law != null && law.Player.WantedBy(watcher)) c *= 1.5;
            return Math.Max(0, Math.Min(0.95, c));
        }

        /// <summary>Who catches a back-door sale: the blockader under a blockade, else the port's owner.</summary>
        Faction Watcher(Settlement s, BackDoorReason reason) => reason == BackDoorReason.Blockade ? World.Diplomacy.BlockadedBy(s) ?? s.Owner : s.Owner;

        public enum Outcome
        {
            /// <summary>Sold: paid, and the crates are in the port.</summary>
            Sold,

            /// <summary>Customs caught it: the crates are seized and smuggling charged.</summary>
            Caught,

            /// <summary>There is no back door here (a haven, a lawless station, a ruin).</summary>
            NoBackDoor,

            /// <summary>No crate of that good needs the back door (or none is aboard): sell it on the open market.</summary>
            NothingToSmuggle,
        }

        /// <summary>
        /// Sells up to <paramref name="crates"/> crates of <paramref name="g"/> out of <paramref name="ship"/>'s hold (the one
        /// the player flies) at <paramref name="s"/>'s back door: only crates the open market will not take. One roll against
        /// customs for the lot (<paramref name="roll"/> in 0..1 for a given roll; random otherwise). Sold, the ship's purse is
        /// paid off the books and the goods go into the port's stores; caught, the crates are seized and the player charged
        /// with smuggling. Reports the crates and credits in <paramref name="sold"/> and <paramref name="credits"/>.
        /// </summary>
        public Outcome SellBackDoor(Ship ship, Settlement s, Good g, int crates, double jd, out int sold, out double credits, double roll = double.NaN)
        {
            sold = 0;
            credits = 0;
            if (!HasBackDoor(s)) return Outcome.NoBackDoor;
            var lot = new List<CargoItem>();
            var reason = BackDoorReason.None;
            foreach (var item in ship.PersonalHold.Items)
            {
                if (lot.Count >= crates || item.Good != g) continue;
                var r = ReasonFor(ship, s, item);
                if (r == BackDoorReason.None) continue;
                lot.Add(item);
                if (r > reason) reason = r;
            }
            if (lot.Count == 0) return Outcome.NothingToSmuggle;
            if (double.IsNaN(roll)) roll = rng.NextDouble();
            double tonnes = 0;
            foreach (var item in lot)
            {
                ship.PersonalHold.Remove(item);
                tonnes += item.Tonnes;
            }
            if (roll < CatchChance(s, reason))
            {
                var by = Watcher(s, reason);
                desk.TimesCaught++;
                string what = $"{lot.Count} crate{(lot.Count == 1 ? "" : "s")} of {GoodInfo.Of(g).Name.ToLowerInvariant()}";
                World.Law?.Commit(World.Law.Player, CrimeKind.Smuggling, by, by, s.Body, jd, "customs at " + s.Name, what + " through the back door");
                desk.Notice($"Customs at {s.Name} seize {what}: you are charged with smuggling");
                return Outcome.Caught;
            }
            credits = PriceFor(s, g, reason) * tonnes;
            s.Stock[(int)g] += tonnes;
            s.RefreshPrices();
            ship.Faction.Treasury += credits;
            sold = lot.Count;
            desk.Smuggled++;
            desk.SmugglingTakings += credits;
            return Outcome.Sold;
        }

        public static string Describe(BackDoorReason r)
        {
            switch (r)
            {
                case BackDoorReason.Stolen: return "stolen";
                case BackDoorReason.Contraband: return "banned here";
                case BackDoorReason.Blockade: return "blockade run";
                case BackDoorReason.Embargo: return "embargoed";
                default: return "";
            }
        }
    }
}
