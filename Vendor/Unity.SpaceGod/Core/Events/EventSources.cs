using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// What <see cref="EventDirector"/> reads from the sim to decide what adventures exist: the ships,
    /// the wrecks, how dangerous each lane is, and where things are. <see cref="Of"/> wires it to a live
    /// <see cref="World"/>; tests fill it in by hand, so the spawning rules are checked without an
    /// ephemeris or a whole fleet.
    /// </summary>
    public sealed class EventSources
    {
        public IReadOnlyList<Ship> Ships = Array.Empty<Ship>();
        public IReadOnlyList<Wreck> Wrecks = Array.Empty<Wreck>();

        /// <summary>Escape pods drifting and broadcasting (see <see cref="Crews"/>).</summary>
        public IReadOnlyList<EscapePod> Pods = Array.Empty<EscapePod>();

        /// <summary>Where a pod cluster is now, heliocentric km.</summary>
        public Func<EscapePod, DVec3> PodHelio = p => DVec3.Zero;

        /// <summary>The player's ship takes the pods aboard; returns what became of their people (see <see cref="World.PickUpPods"/>).</summary>
        public Func<EscapePod, Ship, PodState> PickUpPod = (p, s) =>
        {
            p.State = PodState.Rescued;
            p.TakenBy = s?.Faction;
            return p.State;
        };

        /// <summary>Where a ship is now, heliocentric km.</summary>
        public Func<Ship, DVec3> ShipHelio = s => DVec3.Zero;

        /// <summary>Where a wreck is now, heliocentric km.</summary>
        public Func<Wreck, DVec3> WreckHelio = w => DVec3.Zero;

        /// <summary>Where a body is now, heliocentric km.</summary>
        public Func<int, DVec3> BodyHelio = b => DVec3.Zero;

        /// <summary>The body whose neighbourhood a point is in (anomalies are anchored in its frame).</summary>
        public Func<DVec3, int> NearestBody = p => 0;

        /// <summary>A body's name, for the text hooks.</summary>
        public Func<int, string> BodyName = b => "body " + b;

        /// <summary>How dangerous a lane is: its own ambush record (<see cref="Hyperlanes.Danger"/>) plus
        /// the fighting at either end (<see cref="War.Danger"/>).</summary>
        public Func<Lane, double> LaneDanger = l => 0;

        /// <summary>A ship that carries freight for a living (the ones that ask for escorts).</summary>
        public Func<Ship, bool> IsMerchant = s => s.Role == ShipRole.Trader || s.Role == ShipRole.Miner || s.Role == ShipRole.Courier;

        /// <summary>Who pays for pirates dealt with (the Belt Consortium in Sol), or null for nobody.</summary>
        public Faction PirateHunters;

        /// <summary>Whether a patron will offer the player its contracts (standing and bounties, see <see cref="Law.OffersContracts"/>).</summary>
        public Func<Faction, bool> WillHire = f => true;

        /// <summary>The bounties the law has posted on a ship's captain for what they have actually done, credits.</summary>
        public Func<Ship, double> PostedBounty = s => 0;

        /// <summary>How much of the fighting at a lane's two ends counts toward its danger.</summary>
        public const double EndDangerWeight = 0.25;

        /// <summary>Everything a live <see cref="World"/> offers the director at <paramref name="jd"/>.</summary>
        public static EventSources Of(World world, double jd)
        {
            var eph = world.Ephemeris;
            var fleet = world.Fleet;
            var war = world.War;
            var lanes = fleet.Lanes;
            var catalog = world.Catalog;
            return new EventSources
            {
                Ships = fleet.Ships,
                Wrecks = war.Wrecks.Items,
                ShipHelio = s => fleet.StateOf(s, jd).HelioKm,
                BodyHelio = b => eph.HelioAt(b, jd),
                BodyName = b => catalog[b].Name,
                IsMerchant = world.IsTrader,
                PirateHunters = world.Belt,
                WillHire = f => Law.OffersContracts(world.Law.Player, f),
                PostedBounty = world.Law.BountyOn,
                WreckHelio = w => world.WreckHelio(w, jd),
                Pods = war.Crews.Pods,
                PodHelio = p => world.PodHelio(p, jd),
                PickUpPod = (p, s) => world.PickUpPods(p, s, jd),
                LaneDanger = lane =>
                {
                    double d = 0;
                    if (lanes != null) lanes.Danger.TryGetValue(lane, out d);
                    return d + EndDangerWeight * (war.Danger(lane.A) + war.Danger(lane.B));
                },
                NearestBody = p =>
                {
                    int best = 0;
                    double bestD = double.MaxValue;
                    for (int i = 1; i < catalog.Count; i++)
                    {
                        double d = (eph.HelioAt(i, jd) - p).Length;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    return best;
                },
            };
        }
    }

    /// <summary>The names raider captains go by, fixed per hull so a bounty always names the same person.</summary>
    public static class PirateCaptains
    {
        static readonly string[] First =
        {
            "Mara", "Dagny", "Silas", "Orrin", "Vesna", "Tobiah", "Ines", "Kallum", "Rook", "Yara",
            "Bram", "Sefa", "Ludo", "Nkem", "Petra", "Ash", "Coralie", "Jory", "Hollis", "Zev",
        };

        static readonly string[] Last =
        {
            "Voss", "Kade", "Marrow", "Thorne", "Okafor", "Lisk", "Varga", "Cray", "Hale", "Seddon",
            "Brask", "Ivers", "Quell", "Mendez", "Strand", "Tolliver", "Raske", "Ngata", "Fenn", "Ulver",
        };

        static readonly string[] Epithet =
        {
            "", "", "", " \"the Knife\"", " \"Cold Iron\"", " \"Lanebreaker\"", " \"the Widow\"", " \"Red Ledger\"",
        };

        /// <summary>The captain of <paramref name="ship"/>: the same name every time it is asked.</summary>
        public static string NameFor(Ship ship)
        {
            uint h = Hash((uint)ship.Id * 2654435761u ^ (uint)StableHash(ship.Name));
            return "Captain " + First[h % (uint)First.Length] + Epithet[(h >> 8) % (uint)Epithet.Length] + " " + Last[(h >> 16) % (uint)Last.Length];
        }

        static int StableHash(string s)
        {
            if (s == null) return 0;
            unchecked
            {
                int h = 23;
                foreach (char c in s) h = h * 31 + c;
                return h;
            }
        }

        static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }
    }
}
