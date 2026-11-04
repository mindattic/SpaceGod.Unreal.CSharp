using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    public enum Relation
    {
        Peace,
        Ceasefire,
        War,
    }

    /// <summary>One faction closing its ports to another's ships (see <see cref="Diplomacy.DeclareEmbargo"/>).</summary>
    public sealed class Embargo
    {
        public Faction By, Against;
        public double SinceJd, UntilJd;

        /// <summary>Why it was declared, for the news ("running munitions to the Outer Reach Compact").</summary>
        public string Reason;
    }

    /// <summary>
    /// The peace that ends an interstellar war (U.SPC-T92): who signed it, who came out ahead, the systems the loser
    /// cedes (every settlement and gate it held there goes to the winner; whatever else either side had occupied goes
    /// back), and how long the two keep the peace before either may go to war with the other again.
    /// </summary>
    public sealed class Treaty
    {
        public Faction A, B;

        /// <summary>The side that gains systems by it; null for a white peace that cedes nothing.</summary>
        public Faction Winner;

        public double Jd;

        /// <summary>No new war between the two before this date.</summary>
        public double TruceUntilJd;

        /// <summary>The systems ceded to the <see cref="Winner"/>, by name.</summary>
        public readonly List<string> Ceded = new List<string>();

        /// <summary>The terms, for the news.</summary>
        public string Terms;

        public Faction Loser => Winner == null ? null : Winner == A ? B : A;

        public bool Between(Faction x, Faction y) => (A == x && B == y) || (A == y && B == x);
    }

    /// <summary>
    /// Who is at war with whom. Pirates are hostile to everyone and everyone to them. Besides war and
    /// peace a faction can declare an embargo (its ports refuse another faction's ships) and a besieger
    /// can declare a blockade of a port (nobody docks there until it is lifted).
    /// </summary>
    public sealed class Diplomacy
    {
        readonly Dictionary<(Faction, Faction), Relation> relations = new Dictionary<(Faction, Faction), Relation>();
        readonly List<Embargo> embargoes = new List<Embargo>();
        readonly Dictionary<Settlement, Faction> blockades = new Dictionary<Settlement, Faction>();

        static (Faction, Faction) Key(Faction a, Faction b) =>
            string.CompareOrdinal(a.Name, b.Name) <= 0 ? (a, b) : (b, a);

        public Relation Get(Faction a, Faction b)
        {
            if (a == b) return Relation.Peace;
            return relations.TryGetValue(Key(a, b), out var r) ? r : Relation.Peace;
        }

        public void Set(Faction a, Faction b, Relation r) => relations[Key(a, b)] = r;

        // ---------------------------------------------------------------- treaties

        readonly List<Treaty> treaties = new List<Treaty>();

        /// <summary>Every peace signed so far, oldest first (U.SPC-T92).</summary>
        public IReadOnlyList<Treaty> Treaties => treaties;

        /// <summary>
        /// Ends the war between the treaty's two sides (U.SPC-T92): they are at peace from <see cref="Treaty.Jd"/>, and the
        /// treaty goes on the record. Handing over what it cedes is for whoever holds the systems (see <see cref="InterstellarWars"/>).
        /// </summary>
        public Treaty Sign(Treaty t)
        {
            Set(t.A, t.B, Relation.Peace);
            treaties.Add(t);
            return t;
        }

        /// <summary>Whether a treaty still binds <paramref name="a"/> and <paramref name="b"/> to keep the peace at <paramref name="jd"/>.</summary>
        public bool InTruce(Faction a, Faction b, double jd)
        {
            foreach (var t in treaties)
                if (t.Between(a, b) && jd < t.TruceUntilJd) return true;
            return false;
        }

        public bool Hostile(Faction a, Faction b)
        {
            if (a == null || b == null || a == b) return false;
            if (a.IsPirate || b.IsPirate) return true;
            return Get(a, b) == Relation.War;
        }

        /// <summary>A ship of <paramref name="ship"/> may trade at a port owned by <paramref name="port"/>.</summary>
        public bool CanDock(Faction ship, Faction port)
        {
            if (ship == port) return true;
            if (port.IsPirate) return ship.IsPirate;
            if (ship.IsPirate) return false;
            if (IsEmbargoed(port, ship)) return false;
            return Get(ship, port) != Relation.War;
        }

        /// <summary>A ship of <paramref name="ship"/> may trade at <paramref name="port"/>: its owner lets it in and no blockade shuts it.</summary>
        public bool CanDockAt(Faction ship, Settlement port) => !blockades.ContainsKey(port) && CanDock(ship, port.Owner);

        // ---------------------------------------------------------------- embargoes

        public IReadOnlyList<Embargo> Embargoes => embargoes;

        /// <summary>Whether <paramref name="by"/>'s ports are closed to <paramref name="against"/>'s ships.</summary>
        public bool IsEmbargoed(Faction by, Faction against)
        {
            foreach (var e in embargoes)
                if (e.By == by && e.Against == against) return true;
            return false;
        }

        /// <summary>
        /// <paramref name="by"/> closes its ports to <paramref name="against"/> until <paramref name="untilJd"/>
        /// (or until lifted). Returns the embargo, or null if one is already in force.
        /// </summary>
        public Embargo DeclareEmbargo(Faction by, Faction against, double jd, double untilJd, string reason)
        {
            if (by == null || against == null || by == against || IsEmbargoed(by, against)) return null;
            var e = new Embargo { By = by, Against = against, SinceJd = jd, UntilJd = untilJd, Reason = reason };
            embargoes.Add(e);
            return e;
        }

        public bool LiftEmbargo(Embargo e) => embargoes.Remove(e);

        // ---------------------------------------------------------------- blockades

        public IReadOnlyDictionary<Settlement, Faction> Blockades => blockades;

        /// <summary>The faction blockading <paramref name="port"/>, or null.</summary>
        public Faction BlockadedBy(Settlement port) => port != null && blockades.TryGetValue(port, out var f) ? f : null;

        /// <summary><paramref name="by"/> declares <paramref name="port"/> blockaded: no ship docks there. False if it already was.</summary>
        public bool DeclareBlockade(Settlement port, Faction by)
        {
            if (port == null || by == null || blockades.ContainsKey(port)) return false;
            blockades[port] = by;
            return true;
        }

        public bool LiftBlockade(Settlement port) => port != null && blockades.Remove(port);
    }
}
