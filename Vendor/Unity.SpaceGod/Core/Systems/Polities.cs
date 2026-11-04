using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>What sort of power a faction is (game layer), for the ownership the HUD shows.</summary>
    public enum PolityKind
    {
        /// <summary>A government with a navy: the Sol Federation, the Centauri Accord, a frontier colony's own state.</summary>
        State,

        /// <summary>A mining or industrial concern that owns stations and outposts but keeps no warships of its own.</summary>
        Company,

        /// <summary>Independent shipping: owns hulls, not settlements.</summary>
        FreeTraders,

        /// <summary>Raiders working from their havens, hostile to everyone.</summary>
        PirateClan,
    }

    /// <summary>
    /// The interstellar polities (U.SPC-T91): one faction model shared by every star system. A faction is one
    /// object however many systems it holds settlements in, so it has one treasury, one navy (its warships
    /// wherever they are, in a system's fleet or crossing a gate), one capital and one reserve of spacers; and
    /// there is one diplomacy table for everyone, so a war declared in one system is a war in all of them.
    /// Each system's <see cref="World"/> registers here as it is built and founds (or joins) the polities it
    /// holds through <see cref="World.Polity"/>: the Sol Federation holds Sol and the Barnard's Star frontier,
    /// the Centauri Accord governs Proxima, each frontier colony has its own state, and the pirate clans keep
    /// their own havens. A world built on its own (a test, a single system) gets a private set.
    /// </summary>
    public sealed class Polities
    {
        readonly List<Faction> factions = new List<Faction>();
        readonly List<World> worlds = new List<World>();
        readonly Dictionary<Faction, World> homes = new Dictionary<Faction, World>();

        /// <summary>Who is at war with whom, for every system at once.</summary>
        public Diplomacy Diplomacy { get; } = new Diplomacy();

        /// <summary>The strategy layer (U.SPC-T423): each polity's production, build and research priorities and its fleet groups' objectives.</summary>
        public StrategicCommand Command { get; }

        /// <summary>The diplomacy game (U.SPC-T427): opinions, pacts, proposals, declarations of war and peace between the states.</summary>
        public Statecraft Statecraft { get; }

        public Polities()
        {
            Command = new StrategicCommand(this);
            Statecraft = new Statecraft(this);
        }

        public IReadOnlyList<Faction> Factions => factions;

        /// <summary>The systems' worlds, in the order they were built.</summary>
        public IReadOnlyList<World> Worlds => worlds;

        /// <summary>Ships between systems, inside a gate (they are in no system's fleet until they come out); none if unset.</summary>
        public Func<IEnumerable<Ship>> InTransit;

        internal void Register(World world)
        {
            if (!worlds.Contains(world)) worlds.Add(world);
        }

        /// <summary>The faction called <paramref name="name"/>, or null.</summary>
        public Faction Find(string name)
        {
            foreach (var f in factions)
                if (f.Name == name) return f;
            return null;
        }

        /// <summary>Founds a polity, its home being <paramref name="home"/> (where its recruiting and bookkeeping run).</summary>
        internal void Found(Faction f, World home)
        {
            factions.Add(f);
            homes[f] = home;
        }

        /// <summary>The world that keeps a polity's books (the one that founded it), or null if it is not registered here.</summary>
        public World HomeOf(Faction f) => f != null && homes.TryGetValue(f, out var w) ? w : null;

        /// <summary>Every settlement in every system, in system order.</summary>
        public List<Settlement> Settlements
        {
            get
            {
                var all = new List<Settlement>();
                foreach (var w in worlds) all.AddRange(w.Economy.Settlements);
                return all;
            }
        }

        /// <summary>The settlements <paramref name="f"/> holds, across every system.</summary>
        public IEnumerable<Settlement> SettlementsOf(Faction f)
        {
            foreach (var w in worlds)
                foreach (var s in w.Economy.Settlements)
                    if (s.Owner == f && !s.Fallen) yield return s;
        }

        /// <summary>The world a settlement is in, or null.</summary>
        public World WorldOf(Settlement s)
        {
            foreach (var w in worlds)
                foreach (var x in w.Economy.Settlements)
                    if (x == s) return w;
            return null;
        }

        /// <summary>
        /// Who holds a system: the owner of its most populous standing settlement (pirate havens never count).
        /// Null for a system nobody has settled.
        /// </summary>
        public static Faction HolderOf(World w)
        {
            Settlement best = null;
            foreach (var s in w.Economy.Settlements)
            {
                if (s.Fallen || s.Owner == null || s.Owner.IsPirate) continue;
                if (best == null || s.Population > best.Population) best = s;
            }
            return best?.Owner;
        }

        /// <summary>The systems <paramref name="f"/> holds (see <see cref="HolderOf"/>).</summary>
        public List<World> SystemsHeldBy(Faction f)
        {
            var held = new List<World>();
            foreach (var w in worlds)
                if (HolderOf(w) == f) held.Add(w);
            return held;
        }

        /// <summary>The systems <paramref name="f"/> has any standing settlement in, held or not.</summary>
        public List<World> SystemsWithHoldings(Faction f)
        {
            var list = new List<World>();
            foreach (var w in worlds)
                foreach (var s in w.Economy.Settlements)
                    if (s.Owner == f && !s.Fallen)
                    {
                        list.Add(w);
                        break;
                    }
            return list;
        }

        /// <summary>
        /// The seat of <paramref name="f"/>'s government: the settlement it was founded on, while it holds it;
        /// once that is lost (taken, or fallen) the most populous of its remaining holdings anywhere. Null for a
        /// polity that owns no settlement (free traders).
        /// </summary>
        public Settlement CapitalOf(Faction f)
        {
            if (f == null) return null;
            var c = f.Capital;
            if (c != null && c.Owner == f && !c.Fallen) return c;
            Settlement best = null;
            foreach (var s in SettlementsOf(f))
                if (best == null || s.Population > best.Population || (f.IsPirate && s.Kind == SettlementKind.Haven && best.Kind != SettlementKind.Haven)) best = s;
            if (best != null) f.Capital = best;
            return best;
        }

        /// <summary>The world <paramref name="f"/>'s capital stands in, or null.</summary>
        public World CapitalSystemOf(Faction f)
        {
            var c = CapitalOf(f);
            return c == null ? null : WorldOf(c);
        }

        /// <summary>
        /// <paramref name="f"/>'s one navy: every warship flying its colours, in whichever system's fleet it is or
        /// inside a gate between two of them.
        /// </summary>
        public List<Ship> Navy(Faction f)
        {
            var navy = new List<Ship>();
            foreach (var w in worlds)
                foreach (var s in w.Fleet.Ships)
                    if (!s.Destroyed && s.Faction == f && s.Spec.IsMilitary) navy.Add(s);
            if (InTransit != null)
                foreach (var s in InTransit())
                    if (!s.Destroyed && s.Faction == f && s.Spec.IsMilitary) navy.Add(s);
            return navy;
        }

        /// <summary>How many of <paramref name="f"/>'s supply convoys (freighters, so not in <see cref="Navy"/>) sail in any system or gate.</summary>
        public int ConvoyCount(Faction f)
        {
            int n = 0;
            foreach (var w in worlds)
                foreach (var s in w.Fleet.Ships)
                    if (!s.Destroyed && s.Faction == f && s.Convoy && !s.IsEscort) n++;
            if (InTransit != null)
                foreach (var s in InTransit())
                    if (!s.Destroyed && s.Faction == f && s.Convoy && !s.IsEscort) n++;
            return n;
        }

        /// <summary>Whether <paramref name="f"/> is at war with anyone anywhere (pirates aside: everyone is always at odds with them).</summary>
        public bool AtWar(Faction f)
        {
            foreach (var g in factions)
                if (g != f && !g.IsPirate && Diplomacy.Get(f, g) == Relation.War) return true;
            return false;
        }

        /// <summary>
        /// The polities whose books <paramref name="world"/> keeps (it founded them), out of those present in it:
        /// each recruits and recovers once a day, wherever it holds people, not once in every system it is in.
        /// A faction from no registry (made directly on a fleet) is kept by whichever world it is in.
        /// </summary>
        public List<Faction> KeptBy(World world, IReadOnlyList<Faction> present)
        {
            var kept = new List<Faction>();
            foreach (var f in present)
            {
                var home = HomeOf(f);
                if (home == null || home == world) kept.Add(f);
            }
            return kept;
        }
    }
}
