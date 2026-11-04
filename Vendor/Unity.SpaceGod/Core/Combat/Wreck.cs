using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>One good and how many tonnes of it a wreck's hold still carries for whoever salvages it.</summary>
    public readonly struct SalvageStack
    {
        public readonly Good Good;
        public readonly double Tonnes;

        public SalvageStack(Good good, double tonnes)
        {
            Good = good;
            Tonnes = tonnes;
        }
    }

    /// <summary>
    /// A destroyed hull left behind at the battle site: a slowly tumbling wreck and its drifting
    /// debris field, standing in for the ship until it finally breaks up. <see cref="Pos"/> is an
    /// offset (km, ecliptic) from <see cref="Body"/>'s centre, fixed in its frame -- the same
    /// convention as <see cref="Battle.Anchor"/> and <see cref="BattleUnit.Pos"/>, since a wreck is
    /// created exactly where its unit died and then just sits there.
    /// </summary>
    public sealed class Wreck
    {
        public int Id;
        public string Name;
        public Faction Faction;

        /// <summary>The hull this wreck was: null if a settlement defence platform died rather than a ship.</summary>
        public Ship Ship;

        public int Body;
        public DVec3 Pos;

        /// <summary>The battle's anchor and the unit's battle-local position when it died, km, so the wreck is drawn where the fight was drawn.</summary>
        public DVec3 Anchor, Local;

        /// <summary>Set when the ship died in an ambush beside a hyperlane rather than at a body.</summary>
        public LaneSite Site;

        public double LengthKm;
        public double CreatedJd;

        /// <summary>Days after <see cref="CreatedJd"/> this wreck breaks up and disperses for good.</summary>
        public double LifetimeDays;

        /// <summary>What a salvager still finds aboard: cargo the ship was carrying when it died.</summary>
        public readonly List<SalvageStack> Salvage = new List<SalvageStack>();

        /// <summary>
        /// The pieces the hull broke into when it died (U.SPC-T596, see <see cref="ModularHull.BreakUp"/>): the main hulk
        /// first, then every piece blown off it, each drifting out through the debris field (<see cref="WreckPiece.OffsetKm"/>)
        /// with the alloys and parts it breaks down into aboard (<see cref="HullSalvage.Scrap"/>). Empty for a wreck that was
        /// not a ship with a combat state (a defence platform's, or a bare hull's).
        /// </summary>
        public readonly List<WreckPiece> Pieces = new List<WreckPiece>();

        /// <summary>Breaks <see cref="Ship"/>'s hull up into <see cref="Pieces"/>, the same way every time for this wreck.</summary>
        internal void BreakUp()
        {
            Pieces.Clear();
            var combat = Ship?.Combat;
            if (combat == null) return;
            var hull = combat.Structure;
            var fragments = hull.BreakUp(Id);
            for (int i = 0; i < fragments.Count; i++)
            {
                var piece = new WreckPiece { Index = i, Fragment = fragments[i] };
                piece.Scrap.AddRange(HullSalvage.Scrap(hull, fragments[i], combat.Spec, combat));
                Pieces.Add(piece);
            }
        }

        /// <summary>How fast the debris cloud spreads, in hull lengths per day.</summary>
        public const double DebrisSpreadLengthsPerDay = 0.6;

        /// <summary>The cloud never spreads wider than this many hull lengths, however long the wreck lasts.</summary>
        public const double DebrisMaxLengths = 8;

        /// <summary>Past its lifetime: due to be cleared away the next time <see cref="Wrecks.AdvanceTo"/> runs.</summary>
        public bool Expired(double jd) => jd - CreatedJd > LifetimeDays;

        /// <summary>Bigger hulls scatter more pieces: 6 for a fighter-sized wreck up to 24 for a capital ship.</summary>
        public int DebrisPieces => (int)Math.Clamp(6 + LengthKm * 40, 6, 24);

        /// <summary>A debris piece's radius in hull lengths while the wreck is fresh (as drawn); it shrinks as the wreck fades.</summary>
        public const double DebrisPieceLengths = 0.035;

        /// <summary>How big each piece of debris is at <paramref name="jd"/>, km: what a hull flying through the field can hit (U.SPC-T60).</summary>
        public double DebrisPieceRadiusKm(double jd) => LengthKm * DebrisPieceLengths * Fade(jd);

        /// <summary>Each piece's mass, kt: half the hull it was, shared between the pieces (the rest is gone as gas and dust).</summary>
        public double DebrisPieceMassKt =>
            0.5 * (Ship?.Combat != null ? Collisions.MassOf(Ship.Combat.Spec) : 3500 * Math.Pow(Math.Max(0.005, LengthKm), 3)) / DebrisPieces;

        /// <summary>Piece <paramref name="k"/> as a collision volume, around a wreck centred at <paramref name="centreKm"/>
        /// and moving at <paramref name="velKmS"/> in the caller's frame: pinned, since the field drifts on its own.</summary>
        public CollisionVolume DebrisCollider(int k, double jd, DVec3 centreKm, DVec3 velKmS)
        {
            var c = CollisionVolume.Sphere(centreKm + DebrisOffsetKm(k, jd), velKmS, DebrisPieceRadiusKm(jd), DebrisPieceMassKt);
            c.Pinned = true;
            return c;
        }

        /// <summary>Radius of the drifting debris cloud at <paramref name="jd"/>, km: it starts at the hull and spreads, slowing as it goes.</summary>
        public double DebrisRadiusKm(double jd)
        {
            double days = Math.Max(0, jd - CreatedJd);
            double lengths = 0.5 + (DebrisMaxLengths - 0.5) * (1 - Math.Exp(-days * DebrisSpreadLengthsPerDay / DebrisMaxLengths));
            return LengthKm * lengths;
        }

        /// <summary>1 while the wreck is fresh, fading to 0 over the last fifth of its lifetime as it breaks up.</summary>
        public double Fade(double jd)
        {
            double left = LifetimeDays - (jd - CreatedJd);
            return Math.Clamp(left / (LifetimeDays * 0.2), 0, 1);
        }

        /// <summary>
        /// Where debris piece <paramref name="k"/> has drifted to at <paramref name="jd"/>, km from the wreck:
        /// each piece keeps its own fixed heading and share of the cloud's radius, so the field spreads without
        /// pieces jumping about from frame to frame.
        /// </summary>
        public DVec3 DebrisOffsetKm(int k, double jd)
        {
            uint h = Hash((uint)Id * 7919u + (uint)k * 104729u);
            double u = (h & 0xFFFF) / 65535.0, v = ((h >> 16) & 0xFFFF) / 65535.0;
            double z = 2 * u - 1, a = 2 * Math.PI * v, r = Math.Sqrt(1 - z * z);
            double share = 0.35 + 0.65 * ((Hash(h) & 0xFFFF) / 65535.0);
            return new DVec3(r * Math.Cos(a), r * Math.Sin(a), z) * (DebrisRadiusKm(jd) * share);
        }

        static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }
    }

    /// <summary>
    /// Every lasting wreck in the system: one is left wherever a ship dies in battle, and each is
    /// cleared away once its own lifetime runs out.
    /// </summary>
    public sealed class Wrecks
    {
        /// <summary>Lifetime given to a new wreck when <see cref="Add"/> is not told one explicitly, days.</summary>
        public double DefaultLifetimeDays = 21;

        public readonly List<Wreck> Items = new List<Wreck>();
        int nextId;

        /// <summary>
        /// Leaves a wreck where <paramref name="unit"/> died, carrying over whatever cargo it still
        /// held. <paramref name="body"/>/<paramref name="anchor"/>/<paramref name="site"/> are the battle's own (see
        /// <see cref="Battle.Body"/>/<see cref="Battle.Anchor"/>/<see cref="Battle.Site"/>), so the wreck sits exactly
        /// where the unit's battle-local <see cref="BattleUnit.Pos"/> placed it.
        /// </summary>
        public Wreck Add(BattleUnit unit, int body, DVec3 anchor, double jd, double lifetimeDays = -1, LaneSite site = null)
        {
            var w = new Wreck
            {
                Id = nextId++,
                Name = unit.Name,
                Faction = unit.Faction,
                Ship = unit.Ship,
                Body = body,
                Pos = anchor + unit.Pos,
                Anchor = anchor,
                Local = unit.Pos,
                Site = site,
                LengthKm = unit.LengthKm,
                CreatedJd = jd,
                LifetimeDays = lifetimeDays > 0 ? lifetimeDays : DefaultLifetimeDays,
            };
            if (unit.Ship != null && unit.Ship.HasCargo && unit.Ship.CargoTonnes > 0)
                w.Salvage.Add(new SalvageStack(unit.Ship.CargoGood, unit.Ship.CargoTonnes));
            w.BreakUp();
            Items.Add(w);
            return w;
        }

        /// <summary>
        /// Leaves a wreck where <paramref name="ship"/> broke up outside any battle (in a collision, U.SPC-T60),
        /// <paramref name="offsetKm"/> from <paramref name="body"/>'s centre, fixed in its frame, with its cargo aboard.
        /// </summary>
        public Wreck Add(Ship ship, int body, DVec3 offsetKm, double jd, double lifetimeDays = -1)
        {
            var w = new Wreck
            {
                Id = nextId++,
                Name = ship.Name,
                Faction = ship.Faction,
                Ship = ship,
                Body = body,
                Pos = offsetKm,
                Anchor = offsetKm,
                Local = DVec3.Zero,
                LengthKm = ship.LengthKm,
                CreatedJd = jd,
                LifetimeDays = lifetimeDays > 0 ? lifetimeDays : DefaultLifetimeDays,
            };
            if (ship.HasCargo && ship.CargoTonnes > 0) w.Salvage.Add(new SalvageStack(ship.CargoGood, ship.CargoTonnes));
            w.BreakUp();
            Items.Add(w);
            return w;
        }

        /// <summary>The wreck a destroyed ship left, or null once it has been cleared away (or it never left one).</summary>
        public Wreck Of(Ship ship) => Items.Find(w => w.Ship == ship);

        /// <summary>Clears away every wreck whose lifetime has run out as of <paramref name="jd"/>.</summary>
        public void AdvanceTo(double jd)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
                if (Items[i].Expired(jd)) Items.RemoveAt(i);
        }
    }
}
