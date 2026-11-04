using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// What becomes of a Breacher round that fails (U.SPC-T519). A round that misses, or glances off shields still too strong
    /// to drill through, is not the instant loss of its team: it careens on through space, or tumbles away from the shields
    /// (<see cref="Glance"/>), a derelict with the marines still alive inside (<see cref="Missile.Adrift"/>). Point defence
    /// can still shoot it out of the sky, killing the team, though it spends itself on a tumbling round only with nothing
    /// live in reach. Once the fight is over every round still in space is left drifting where it was
    /// (<see cref="CastAdrift"/>), its team breathing the round's own air for <see cref="AirDays"/>: tow units of its own
    /// side (warships, tugs) search for it and bring the marines home, an enemy warship takes them prisoner, and pirates
    /// salvage it for the marines inside, whom they ransom back to their owners or sell as slaves to the side they were
    /// fighting (<see cref="SlavePricePerHead"/>, <see cref="Crews.Daily"/>). A round nobody reaches runs out of air.
    /// </summary>
    public static class DriftingRounds
    {
        /// <summary>Days of air a round carries for its team once it is adrift.</summary>
        public const double AirDays = 6;

        /// <summary>The share of its speed a round keeps as it glances off a shield and tumbles away.</summary>
        public const double GlanceKeep = 0.6;

        /// <summary>How often a tow unit (a warship, or a tug) sharing a body with a drifting round finds it, per day: an
        /// armoured round is towed in, not lifted aboard like an escape pod, so a merchant passing by cannot take it.</summary>
        public const double TowFindRate = 2.5;

        /// <summary>How often a pirate ship sharing a body with a drifting round finds it, per day: raiders comb a
        /// battlefield for marines worth a ransom.</summary>
        public const double PirateFindRate = 2.5;

        /// <summary>Credits per head the side the marines were fighting pays pirates for them as slaves.</summary>
        public const double SlavePricePerHead = 35000;

        /// <summary>The chance pirates holding marines from a round sell them to their enemy as slaves even when the
        /// owners could pay the ransom.</summary>
        public const double SellChance = 0.35;

        /// <summary>
        /// The relative velocity a round keeps as it glances off a shield whose outward face is <paramref name="normal"/>:
        /// what drove it into the shield turned back out, everything slowed to <see cref="GlanceKeep"/>.
        /// </summary>
        public static DVec3 Glance(DVec3 relVel, DVec3 normal)
        {
            if (normal.LengthSquared < 1e-18) return relVel * -GlanceKeep;
            var n = normal.Normalized;
            double into = DVec3.Dot(relVel, n);
            var off = into < 0 ? relVel - n * (2 * into) : relVel;
            return off * GlanceKeep;
        }

        /// <summary>Whether <paramref name="s"/> can tow in a drifting round: a warship, a raider or a tug.</summary>
        public static bool CanTow(Ship s) =>
            s != null && s.Faction != null && (s.Faction.IsPirate || (s.Spec != null && s.Spec.IsMilitary) || s.Role == ShipRole.Tug);

        /// <summary>The round's name for the log: whose bay fired it.</summary>
        public static string NameOf(Missile m) =>
            "Breacher round from the " + (m.Source?.Name ?? (m.Owner != null ? m.Owner.Name + " line" : "fight"));

        /// <summary>Whether <paramref name="m"/> is a Breacher round with a team still alive inside.</summary>
        public static bool Manned(Missile m) => m != null && !m.Gone && m.Kind == MissileKind.Breacher && m.Owner != null && m.Marines >= 0.5;

        /// <summary>
        /// The fight is over: every Breacher round of <paramref name="b"/> still in space with its team alive (adrift, or
        /// still flying at nothing) is left drifting where it is, for <paramref name="crews"/> to see found or lost. The side
        /// the team was fighting is noted, as the buyer pirates may sell them to. Returns the drifting rounds.
        /// </summary>
        public static List<EscapePod> CastAdrift(Battle b, Crews crews, double jd)
        {
            var list = new List<EscapePod>();
            if (b == null || crews == null) return list;
            foreach (var m in b.Missiles)
            {
                if (!Manned(m)) continue;
                var enemy = m.Side == 0 || m.Side == 1 ? b.SideLeaders[1 - m.Side] : null;
                var pod = crews.Adrift(NameOf(m), m.Owner, m.Marines, enemy, b.Body, b.Anchor, m.Pos, m.Vel, b.Site, jd);
                if (pod == null) continue;
                m.Marines = 0;
                m.Gone = true;
                list.Add(pod);
            }
            return list;
        }

        /// <summary>
        /// Who pirates holding marines <paramref name="c"/> would sell them to as slaves, and at what price: the side the
        /// marines were fighting, if it is still hostile to their owners, is no raider itself and can pay. Null if nobody.
        /// </summary>
        public static Faction Buyer(Captives c, Diplomacy diplomacy, out double price)
        {
            price = 0;
            var buyer = c?.Buyer;
            if (buyer == null || !c.Marines || buyer.IsPirate || buyer == c.Owners || buyer == c.Captors) return null;
            if (diplomacy != null && !diplomacy.Hostile(buyer, c.Owners)) return null;
            price = c.People * SlavePricePerHead;
            return buyer.Treasury >= price ? buyer : null;
        }
    }
}
