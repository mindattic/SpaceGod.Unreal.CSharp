using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>One allied faction's claim on a hull its marines took together with others (U.SPC-T392), and what it
    /// comes away with once the claims are settled.</summary>
    public sealed class PrizeClaim
    {
        public Faction Faction;

        /// <summary>Its part of the prize, 0 to 1: the fighting strength it committed over everyone's.</summary>
        public double Share;

        /// <summary>Its tonnes of the cargo aboard (the hold is always shared by the claims).</summary>
        public double CargoTonnes;

        /// <summary>Credits it is paid for its part of the hull: by the keeper buying it out, or from the breakers' price.</summary>
        public double HullPaid;
    }

    /// <summary>
    /// How allied factions who boarded one hull together settle the prize (U.SPC-T392, see docs/BOARDING.md "Combined
    /// allied boarding"). Each faction's claim is the fighting strength its marines brought aboard (marines landed times
    /// their quality), over everyone's, whether or not they lived: the ally who took the losses breaking in is not cut out
    /// by the one who walked in after. Then:
    /// <list type="bullet">
    /// <item><b>The hold is salvage, shared.</b> The cargo aboard is split by claim, whoever keeps the hull.</item>
    /// <item><b>The hull is negotiated.</b> The claimants are offered it largest claim first (the first to land breaking a
    /// tie): the first who can buy the others out keeps it, paying each its claim's part of the prize's worth (the hull's
    /// price, by how much of it is left, at <see cref="PrizeValueShare"/>).</item>
    /// <item><b>No one can pay: the breakers.</b> The hull is sold for scrap at <see cref="BreakersShare"/> of its price
    /// and the money is split by claim.</item>
    /// </list>
    /// A boarding by one faction alone is no negotiation: it keeps everything, as it always has.
    /// </summary>
    public sealed class PrizeSettlement
    {
        /// <summary>What a taken hull is worth to the allies who share it, as a part of its price new (by how much of it
        /// is left): the prize-court value a keeper buys its partners out at.</summary>
        public const double PrizeValueShare = 0.5;

        /// <summary>What the breakers pay for a hull nobody could buy, as a part of its price new (the raiders' rate).</summary>
        public const double BreakersShare = 0.25;

        public readonly List<PrizeClaim> Claims = new List<PrizeClaim>();

        /// <summary>The faction that keeps the hull, or null when it goes to the breakers.</summary>
        public Faction Keeper;

        /// <summary>The prize's worth the keeper bought its partners out at, and what the breakers paid (when they did).</summary>
        public double HullValue, BreakersValue;

        public bool SoldToBreakers => Keeper == null;

        /// <summary>The claim of <paramref name="f"/>, or null.</summary>
        public PrizeClaim Of(Faction f)
        {
            foreach (var c in Claims) if (c.Faction == f) return c;
            return null;
        }

        /// <summary>What the keeper pays its partners in all.</summary>
        public double BuyOut
        {
            get
            {
                if (Keeper == null) return 0;
                double n = 0;
                foreach (var c in Claims) if (c.Faction != Keeper) n += c.HullPaid;
                return n;
            }
        }

        /// <summary>
        /// Settles a prize taken by <paramref name="parties"/>: each claim from what its faction committed, the cargo
        /// (<paramref name="cargoTonnes"/>) split by claim, and the hull to the largest claimant that can pay its partners
        /// their parts of <paramref name="hullValue"/> from its <paramref name="treasury"/>, or to the breakers for
        /// <paramref name="breakersValue"/> shared by claim. Pure: the caller books the money and the cargo.
        /// </summary>
        public static PrizeSettlement Negotiate(IReadOnlyList<BoardingParty> parties, double hullValue, double breakersValue, double cargoTonnes, Func<Faction, double> treasury)
        {
            var deal = new PrizeSettlement { HullValue = Math.Max(0, hullValue) };
            if (parties == null || parties.Count == 0) return deal;

            // Strength committed is the claim; failing that (nobody brought any), marines landed; failing that, equal parts.
            double strength = 0, landed = 0;
            foreach (var p in parties)
            {
                strength += Math.Max(0, p.Strength);
                landed += Math.Max(0, p.Landed);
            }
            var order = new List<BoardingParty>(parties);
            foreach (var p in parties)
            {
                double share = strength > 1e-9 ? Math.Max(0, p.Strength) / strength
                    : landed > 1e-9 ? Math.Max(0, p.Landed) / landed
                    : 1.0 / parties.Count;
                deal.Claims.Add(new PrizeClaim { Faction = p.Faction, Share = share, CargoTonnes = Math.Max(0, cargoTonnes) * share });
            }

            // The hull is offered largest claim first; the first aboard breaks a tie.
            order.Sort((a, b) =>
            {
                int byStrength = deal.Of(b.Faction).Share.CompareTo(deal.Of(a.Faction).Share);
                return byStrength != 0 ? byStrength : a.Order.CompareTo(b.Order);
            });
            if (parties.Count == 1)
            {
                deal.Keeper = parties[0].Faction;
                return deal;
            }
            foreach (var p in order)
            {
                var claim = deal.Of(p.Faction);
                double owed = (1 - claim.Share) * deal.HullValue;
                double purse = treasury != null && p.Faction != null ? treasury(p.Faction) : 0;
                if (purse + 1e-6 < owed) continue;
                deal.Keeper = p.Faction;
                foreach (var c in deal.Claims)
                    if (c.Faction != deal.Keeper) c.HullPaid = c.Share * deal.HullValue;
                return deal;
            }

            // Nobody can buy the others out: the hull is scrapped and the price shared.
            deal.BreakersValue = Math.Max(0, breakersValue);
            foreach (var c in deal.Claims) c.HullPaid = c.Share * deal.BreakersValue;
            return deal;
        }
    }
}
