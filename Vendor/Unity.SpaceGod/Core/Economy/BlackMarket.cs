using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The black markets (U.SPC-T94): where stolen goods are sold, at prices of their own rather than the open board's.
    /// Every haven is one, for its clan's loot (kept in its <see cref="Settlement.Stock"/>; what it holds over its own
    /// needs is loot waiting to move on, and its fences pay half the going rate for it, see <c>Economy.Fence</c>); so is
    /// a lawless station (<see cref="Settlement.Lawless"/>), whose middlemen buy stolen goods into a store of their own
    /// (<see cref="Settlement.BlackStock"/>) and pass them on into the station's open stores a little at a time.
    ///
    /// A black market's price for a good starts from its base price, cut by what the market can get away with paying
    /// (<see cref="HavenShare"/> at a haven, <see cref="StationShare"/> at a station, which can launder it), and rises
    /// as the market runs short of it and falls as it fills: double at an empty market, the share itself when it holds
    /// as much as it can move in <see cref="AppetiteDays"/>, and on down to a quarter of that when glutted. So a clan's
    /// smugglers carry loot from havens that are full of it to stations that are short.
    /// </summary>
    public static class BlackMarket
    {
        /// <summary>What a haven's fences pay for loot, as a share of the base price, when they hold a normal amount.</summary>
        public const double HavenShare = 0.5;

        /// <summary>What a lawless station's middlemen pay for stolen goods, as a share of the base price, at a normal holding.</summary>
        public const double StationShare = 0.8;

        /// <summary>Days of a good's flow through the place a black market can move: holding that much, it pays its share.</summary>
        public const double AppetiteDays = 15;

        /// <summary>The least a black market expects to move of any good, tonnes.</summary>
        public const double MinAppetiteTonnes = 500;

        /// <summary>Share of a lawless station's black-market store passed into its open stores a day.</summary>
        public const double LaunderPerDay = 0.04;

        /// <summary>Whether <paramref name="s"/> runs a black market: a standing haven, or a lawless station.</summary>
        public static bool Takes(Settlement s) => s != null && !s.Fallen && (s.Kind == SettlementKind.Haven || s.Lawless);

        /// <summary>Stolen goods of <paramref name="g"/> it holds, tonnes: a haven's stock over its own needs, a station's black store.</summary>
        public static double Held(Settlement s, Good g) =>
            s.Kind == SettlementKind.Haven ? Math.Max(0, s.Stock[(int)g] - s.Target(g)) : s.BlackStock[(int)g];

        /// <summary>How much of <paramref name="g"/> the market can move, tonnes (see <see cref="AppetiteDays"/>).</summary>
        public static double Appetite(Settlement s, Good g) => Math.Max(s.Flow(g) * AppetiteDays, MinAppetiteTonnes);

        /// <summary>What it pays a tonne for <paramref name="g"/> holding <paramref name="held"/> tonnes of it.</summary>
        public static double PriceAt(Settlement s, Good g, double held)
        {
            double share = s.Kind == SettlementKind.Haven ? HavenShare : StationShare;
            double scarcity = 2 / (1 + Math.Max(0, held) / Appetite(s, g));
            return GoodInfo.Of(g).BasePrice * share * Math.Max(0.25, scarcity);
        }

        /// <summary>What it pays a tonne for <paramref name="g"/> now.</summary>
        public static double Price(Settlement s, Good g) => PriceAt(s, g, Held(s, g));

        /// <summary>Credits for selling it <paramref name="tonnes"/> of <paramref name="g"/>, the price walking down as it fills.</summary>
        public static double SaleValue(Settlement s, Good g, double tonnes)
        {
            const int steps = 6;
            double value = 0, held = Held(s, g);
            for (int k = 0; k < steps; k++)
            {
                double chunk = tonnes / steps;
                value += PriceAt(s, g, held + chunk * 0.5) * chunk;
                held += chunk;
            }
            return value;
        }

        /// <summary>
        /// Sells it <paramref name="tonnes"/> of stolen <paramref name="g"/>: the goods go into its store (a haven's stock, a
        /// station's black store) and the price is paid out of its owner's treasury. Returns the credits paid.
        /// </summary>
        public static double Sell(Settlement s, Good g, double tonnes)
        {
            if (!Takes(s) || tonnes <= 0) return 0;
            double value = SaleValue(s, g, tonnes);
            if (s.Kind == SettlementKind.Haven) s.Stock[(int)g] += tonnes;
            else s.BlackStock[(int)g] += tonnes;
            if (s.Owner != null) s.Owner.Treasury -= value;
            s.TradeVolumeCredits += value;
            return value;
        }

        /// <summary>A lawless station's middlemen pass part of their store into the station's open stores (they paid for it).</summary>
        public static void Launder(Settlement s, double dt)
        {
            double k = 1 - Math.Exp(-LaunderPerDay * dt);
            for (int i = 0; i < GoodInfo.Count; i++)
            {
                double moved = s.BlackStock[i] * k;
                if (moved <= 0) continue;
                s.BlackStock[i] -= moved;
                s.Stock[i] += moved;
            }
        }

        /// <summary>The value at base prices of the loot a haven holds over its own needs, credits.</summary>
        public static double LootValue(Settlement haven)
        {
            double v = 0;
            for (int i = 0; i < GoodInfo.Count; i++) v += Held(haven, (Good)i) * GoodInfo.Of((Good)i).BasePrice;
            return v;
        }
    }
}
