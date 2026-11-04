using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// What part of a hull's structure a <see cref="HullModule"/> is (U.SPC-T596). Not to be confused with
    /// <see cref="ModuleKind"/>, the parts a yard fits on a hardpoint: a hull module is a piece of the hull itself, the
    /// thing those parts are bolted to, and the piece that comes away when the hull breaks apart.
    /// </summary>
    public enum HullModuleKind
    {
        /// <summary>The foremost keel section: the bow and whatever is mounted in it.</summary>
        Bow,

        /// <summary>A keel section between the bow and the stern.</summary>
        Midsection,

        /// <summary>The aftmost keel section: the drive section.</summary>
        Engineering,

        /// <summary>An outrigger off one side of a keel section, carrying the mounts set out past the hull's own beam
        /// (a cruiser's broadside launchers, a fighter's wing guns). The first thing to be shot off.</summary>
        Sponson,
    }

    /// <summary>
    /// One structural piece of a hull (U.SPC-T596): a section of the keel or a sponson off one, with its own structure
    /// points and the hardpoints mounted on it. Positions are in ship lengths, hull frame (right, up, forward), the same
    /// frame as <see cref="SubsystemDef"/>'s X, Y, Z.
    /// </summary>
    public sealed class HullModule
    {
        public int Id;
        public HullModuleKind Kind;

        /// <summary>Which keel section it is, or is set off (0 the stern, counting forward).</summary>
        public int Section;

        /// <summary>-1 port, +1 starboard for a sponson; 0 for a keel section.</summary>
        public int Side;

        /// <summary>The module's centre and half its size along each axis, ship lengths, hull frame.</summary>
        public DVec3 Centre, HalfExtent;

        /// <summary>Its share of the hull's dry mass (every module's together come to 1).</summary>
        public double MassShare;

        /// <summary>Its structure: how much it can take before it shatters, hit points, and what it has left.</summary>
        public double MaxStructure, Structure;

        /// <summary>The hardpoints mounted on it: indices into <see cref="CombatSpec.Layout"/> and <see cref="ShipCombat.Subsystems"/>.</summary>
        public readonly List<int> Hardpoints = new List<int>();

        /// <summary>It has come away from the hull it was part of (reported once by <see cref="ModularHull.Separate"/>).</summary>
        public bool Detached;

        public bool Shattered => Structure <= 0;

        /// <summary>What is left of its structure, 0 to 1.</summary>
        public double Condition => MaxStructure <= 0 ? 0 : Math.Clamp(Structure / MaxStructure, 0, 1);

        /// <summary>How far a point on the hull lies outside the module's box, ship lengths (0 inside it).</summary>
        public double DistanceTo(DVec3 local)
        {
            double dx = Math.Max(0, Math.Abs(local.X - Centre.X) - HalfExtent.X);
            double dy = Math.Max(0, Math.Abs(local.Y - Centre.Y) - HalfExtent.Y);
            double dz = Math.Max(0, Math.Abs(local.Z - Centre.Z) - HalfExtent.Z);
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    /// <summary>
    /// Where two <see cref="HullModule"/>s are joined (U.SPC-T596): a ring of frames between two keel sections, or a
    /// sponson's root. Shots that land on either module load it; when its strength is gone it fails and the two
    /// modules are no longer held together.
    /// </summary>
    public sealed class HullJoint
    {
        public int A, B;

        /// <summary>Where it is, ship lengths, hull frame.</summary>
        public DVec3 At;

        public double MaxStrength, Strength;

        public bool Broken => Strength <= 0;

        public double Condition => MaxStrength <= 0 ? 0 : Math.Clamp(Strength / MaxStrength, 0, 1);

        public int Other(int module) => module == A ? B : A;

        public bool Joins(int module) => A == module || B == module;
    }

    /// <summary>
    /// A piece of hull that has come away (U.SPC-T596): one or more modules still joined to each other but no longer to
    /// the rest of the ship, with every hardpoint mounted on them. Everything downstream works on this: the hull that
    /// lost it loses those hardpoints (<see cref="ModularHulls.ApplyBreak"/>), a wreck scatters its pieces as drifting
    /// wreckage (<see cref="WreckPiece"/>), and a salvager breaks a piece down or sells its parts (<see cref="HullSalvage"/>).
    /// </summary>
    public sealed class HullFragment
    {
        /// <summary>The modules in it, by <see cref="HullModule.Id"/>.</summary>
        public readonly List<int> Modules = new List<int>();

        /// <summary>The hardpoints mounted on it, indices into <see cref="ShipCombat.Subsystems"/>.</summary>
        public readonly List<int> Hardpoints = new List<int>();

        /// <summary>Its share of the hull's dry mass.</summary>
        public double MassShare;

        /// <summary>Its centre of mass, ship lengths, hull frame.</summary>
        public DVec3 Centroid;

        /// <summary>Every module in it shattered: rubble, worth only its scrap.</summary>
        public bool Shattered;

        /// <summary>It holds the hull's <see cref="ModularHull.Root"/>: the main hulk rather than a piece blown off it.</summary>
        public bool Hulk;
    }

    /// <summary>
    /// A hull as the structure it is built from (U.SPC-T596): its <see cref="HullModule"/>s and the
    /// <see cref="HullJoint"/>s holding them together, the graph damage, wrecks, salvage and trade work on. It is a
    /// second account alongside <see cref="ShipCombat.Hull"/> rather than a replacement for it: the hull points still say
    /// whether a ship lives, the structure says where it is coming apart. Every hull's modules are laid out from its
    /// design (<see cref="For"/>), so no design needs a hand-made breakdown: the keel is cut into sections by length, and
    /// the mounts set out past the hull's beam ride on sponsons. The section that holds the bridge (else the reactor) is
    /// the <see cref="Root"/>: whatever is still joined to it is the ship, anything else has broken away. The design and
    /// what builds on it next are in docs/MODULAR_SHIPS.md.
    /// </summary>
    public sealed class ModularHull
    {
        /// <summary>Hull length, km, per keel section beyond the first two: a destroyer has three, a cruiser five and a
        /// battleship or carrier eight, the most.</summary>
        public const double KmPerSection = 0.4;

        public const int MaxSections = 8;

        /// <summary>A mount further out from the keel than this share of the hull's half-beam rides on a sponson.</summary>
        public const double SponsonReach = 0.6;

        /// <summary>A sponson's share of the hull's mass; the keel sections share the rest.</summary>
        public const double SponsonMassShare = 0.05;

        /// <summary>A module's structure for its share of the hull's mass, times the hull's points: each piece holds out
        /// a little longer than its share of the hull, so a hull is far more often holed than cut apart.</summary>
        public const double StructurePerMass = 1.5;

        /// <summary>A keel joint's strength and a sponson root's, shares of the hull's points.</summary>
        public const double KeelJointShare = 0.2, SponsonJointShare = 0.06;

        /// <summary>The share of a hit on a module that loads the joints around it, shared between them by how near
        /// each is to where it struck.</summary>
        public const double JointLoad = 0.5;

        public readonly List<HullModule> Modules = new List<HullModule>();
        public readonly List<HullJoint> Joints = new List<HullJoint>();

        /// <summary>The module the ship is: the keel section holding its bridge, else its reactor, else its middle one.</summary>
        public int Root;

        /// <summary>How many keel sections a hull of <paramref name="lengthKm"/> is cut into.</summary>
        public static int KeelSections(double lengthKm) => Math.Clamp(2 + (int)Math.Round(Math.Max(0, lengthKm) / KmPerSection), 2, MaxSections);

        /// <summary>The structure of a hull built to <paramref name="spec"/>, whole: its keel sections from stern to
        /// bow, joined end to end, and a sponson off any section with mounts set out past the beam on that side.</summary>
        public static ModularHull For(CombatSpec spec)
        {
            var h = new ModularHull();
            int n = KeelSections(spec.LengthKm);
            double halfBeam = 0.5 * Math.Max(0.02, spec.BeamFraction);
            double sectionLen = 1.0 / n;
            var layout = spec.Layout ?? Array.Empty<SubsystemDef>();

            for (int i = 0; i < n; i++)
            {
                h.Modules.Add(new HullModule
                {
                    Id = i,
                    Kind = i == n - 1 ? HullModuleKind.Bow : i == 0 ? HullModuleKind.Engineering : HullModuleKind.Midsection,
                    Section = i,
                    Centre = new DVec3(0, 0, -0.5 + (i + 0.5) * sectionLen),
                    HalfExtent = new DVec3(halfBeam, halfBeam * 0.6, 0.5 * sectionLen),
                });
            }

            // Each mount goes on the section it sits in, or on a sponson off it when it is set out past the beam.
            var sponsons = new Dictionary<(int section, int side), HullModule>();
            var mounts = new Dictionary<HullModule, List<DVec3>>();
            for (int k = 0; k < layout.Length; k++)
            {
                var d = layout[k];
                int section = Math.Clamp((int)Math.Floor((d.Z + 0.5) * n), 0, n - 1);
                var host = h.Modules[section];
                if (OnSponson(d, halfBeam))
                {
                    int side = d.X > 0 ? 1 : -1;
                    if (!sponsons.TryGetValue((section, side), out host))
                    {
                        host = new HullModule { Id = h.Modules.Count, Kind = HullModuleKind.Sponson, Section = section, Side = side };
                        sponsons[(section, side)] = host;
                        h.Modules.Add(host);
                    }
                }
                host.Hardpoints.Add(k);
                if (!mounts.TryGetValue(host, out var at)) mounts[host] = at = new List<DVec3>();
                at.Add(new DVec3(d.X, d.Y, d.Z));
            }

            // A sponson is laid round its own mounts, out from the keel section's side.
            foreach (var s in sponsons.Values)
            {
                double x = 0, y = 0, z = 0, reach = halfBeam;
                foreach (var p in mounts[s])
                {
                    x += p.X;
                    y += p.Y;
                    z += p.Z;
                    reach = Math.Max(reach, Math.Abs(p.X));
                }
                int c = mounts[s].Count;
                double inner = halfBeam * SponsonReach;
                s.HalfExtent = new DVec3(Math.Max(0.005, 0.5 * (reach - inner)), halfBeam * 0.3, 0.5 * sectionLen * 0.8);
                s.Centre = new DVec3(s.Side * (inner + s.HalfExtent.X), y / c, z / c);
            }

            // Mass: each sponson its share, the keel sections the rest evenly.
            double sponsonShare = sponsons.Count == 0 ? 0 : Math.Min(SponsonMassShare, 0.3 / sponsons.Count);
            double keelShare = (1 - sponsonShare * sponsons.Count) / n;
            foreach (var m in h.Modules)
            {
                m.MassShare = m.Kind == HullModuleKind.Sponson ? sponsonShare : keelShare;
                m.MaxStructure = m.Structure = spec.Hull * m.MassShare * StructurePerMass;
            }

            // Joints: section to section along the keel, and each sponson at its root.
            for (int i = 0; i + 1 < n; i++)
                h.AddJoint(i, i + 1, new DVec3(0, 0, -0.5 + (i + 1) * sectionLen), spec.Hull * KeelJointShare);
            foreach (var s in sponsons.Values)
                h.AddJoint(s.Section, s.Id, new DVec3(s.Side * halfBeam * SponsonReach, s.Centre.Y, s.Centre.Z), spec.Hull * SponsonJointShare);

            h.Root = RootSection(h, layout, n);
            return h;
        }

        /// <summary>Whether a mount rides on a sponson: set out past <see cref="SponsonReach"/> of the half-beam, and not
        /// something only the keel can carry (a buried bridge, a spinal gun, the reactor, a hangar, the drive).</summary>
        static bool OnSponson(SubsystemDef d, double halfBeam)
        {
            if (d.Buried || d.Spinal) return false;
            switch (d.Kind)
            {
                case SubsystemKind.Bridge:
                case SubsystemKind.Reactor:
                case SubsystemKind.Hangar:
                case SubsystemKind.Drill:
                    return false;
            }
            return Math.Abs(d.X) >= halfBeam * SponsonReach;
        }

        static int RootSection(ModularHull h, SubsystemDef[] layout, int n)
        {
            foreach (var want in new[] { SubsystemKind.Bridge, SubsystemKind.Reactor })
                for (int k = 0; k < layout.Length; k++)
                    if (layout[k].Kind == want)
                        foreach (var m in h.Modules)
                            if (m.Hardpoints.Contains(k)) return m.Kind == HullModuleKind.Sponson ? m.Section : m.Id;
            return n / 2;
        }

        void AddJoint(int a, int b, DVec3 at, double strength) =>
            Joints.Add(new HullJoint { A = a, B = b, At = at, MaxStrength = strength, Strength = strength });

        public HullModule Module(int id) => Modules[id];

        /// <summary>The module a point on the hull (ship lengths, hull frame) lies in, or the nearest one to it, among
        /// those not yet come away (the root always is one, so there is always an answer).</summary>
        public HullModule ModuleAt(DVec3 local)
        {
            HullModule best = null;
            double bestD = double.MaxValue;
            foreach (var m in Modules)
            {
                if (m.Detached) continue;
                // Inside a sponson's box beats being inside the keel's, which overlaps it at the root.
                double d = m.DistanceTo(local) - (m.Kind == HullModuleKind.Sponson ? 1e-6 : 0);
                if (d < bestD)
                {
                    bestD = d;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>The module hardpoint <paramref name="subsystem"/> is mounted on.</summary>
        public HullModule ModuleOf(int subsystem)
        {
            foreach (var m in Modules)
                if (m.Hardpoints.Contains(subsystem)) return m;
            return null;
        }

        /// <summary>Whether the module is still held to the root through unbroken joints.</summary>
        public bool Attached(int module) => Reachable()[module];

        /// <summary>
        /// A hit of <paramref name="damage"/> (what got through shields and armour) at a point on the hull, ship lengths,
        /// hull frame: the module it lands on takes it into its structure, and the joints round that module take
        /// <see cref="JointLoad"/> of it, the nearest the most. A module whose structure is gone shatters and lets go of
        /// everything joined to it. Returns the pieces that came away (see <see cref="Separate"/>), empty if none did.
        /// </summary>
        public List<HullFragment> Strike(DVec3 local, double damage)
        {
            var m = ModuleAt(local);
            if (m == null || damage <= 0) return new List<HullFragment>();
            m.Structure -= damage;

            double total = 0;
            foreach (var j in Joints)
                if (!j.Broken && j.Joins(m.Id)) total += Nearness(j, local);
            if (total > 0)
                foreach (var j in Joints)
                    if (!j.Broken && j.Joins(m.Id)) j.Strength -= damage * JointLoad * Nearness(j, local) / total;

            if (m.Shattered)
                foreach (var j in Joints)
                    if (j.Joins(m.Id)) j.Strength = Math.Min(j.Strength, 0);
            return Separate();
        }

        static double Nearness(HullJoint j, DVec3 local) => 1 / (0.05 + DVec3.Distance(j.At, local));

        /// <summary>
        /// Every piece no longer held to the <see cref="Root"/> that has not come away before, each a set of modules still
        /// joined to each other; its modules are marked <see cref="HullModule.Detached"/>, so each piece is reported once.
        /// </summary>
        public List<HullFragment> Separate()
        {
            var reach = Reachable();
            var pieces = new List<HullFragment>();
            var seen = new bool[Modules.Count];
            foreach (var m in Modules)
            {
                if (reach[m.Id] || m.Detached || seen[m.Id]) continue;
                var piece = Gather(m.Id, seen, includeDetached: false);
                foreach (int id in piece.Modules) Modules[id].Detached = true;
                pieces.Add(piece);
            }
            return pieces;
        }

        /// <summary>
        /// A hull destroyed outright breaks up (U.SPC-T596): every joint fails with a chance that grows the more it was
        /// already strained (a quarter for a sound one, all but certain for one nearly gone), fixed by
        /// <paramref name="seed"/> so the same wreck always breaks the same way, and at least one always does. Returns
        /// every piece the hull is now in, the main hulk first (<see cref="HullFragment.Hulk"/>), including any that came
        /// away earlier in the fight. Does nothing to a hull that is a single module but hand it back whole.
        /// </summary>
        public List<HullFragment> BreakUp(int seed)
        {
            uint h = Hash(unchecked((uint)seed * 2654435761u + 0x9E3779B9u));
            bool any = false;
            HullJoint strongest = null;
            foreach (var j in Joints)
            {
                if (j.Broken) { any = true; continue; }
                h = Hash(h);
                double chance = 0.25 + 0.7 * (1 - j.Condition);
                if ((h & 0xFFFF) / 65535.0 < chance)
                {
                    j.Strength = 0;
                    any = true;
                }
                else if (strongest == null || j.Condition < strongest.Condition) strongest = j;
            }
            // A destroyed hull never stays in one piece: the most strained joint left gives.
            if (!any && strongest != null) strongest.Strength = 0;
            Separate();

            var pieces = new List<HullFragment>();
            var seen = new bool[Modules.Count];
            var hulk = Gather(Root, seen, includeDetached: true);
            hulk.Hulk = true;
            pieces.Add(hulk);
            foreach (var m in Modules)
                if (!seen[m.Id]) pieces.Add(Gather(m.Id, seen, includeDetached: true));
            return pieces;
        }

        /// <summary>Mends every module's structure and every joint still holding by <paramref name="share"/> of its full
        /// strength; a broken joint and a module that came away stay lost until a yard rebuilds the hull.</summary>
        public void Repair(double share)
        {
            foreach (var m in Modules)
                if (!m.Detached) m.Structure = Math.Min(m.MaxStructure, Math.Max(0, m.Structure) + m.MaxStructure * share);
            foreach (var j in Joints)
                if (!j.Broken) j.Strength = Math.Min(j.MaxStrength, j.Strength + j.MaxStrength * share);
        }

        /// <summary>Whether every module is still part of the ship, unshattered, and every joint holds.</summary>
        public bool Intact
        {
            get
            {
                foreach (var m in Modules) if (m.Detached || m.Shattered) return false;
                foreach (var j in Joints) if (j.Broken) return false;
                return true;
            }
        }

        bool[] Reachable()
        {
            var reach = new bool[Modules.Count];
            var queue = new Queue<int>();
            reach[Root] = true;
            queue.Enqueue(Root);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                if (Modules[at].Shattered && at != Root) continue;
                foreach (var j in Joints)
                {
                    if (j.Broken || !j.Joins(at)) continue;
                    int next = j.Other(at);
                    if (reach[next]) continue;
                    reach[next] = true;
                    queue.Enqueue(next);
                }
            }
            return reach;
        }

        /// <summary>The modules joined to <paramref name="start"/> through unbroken joints, as one piece.</summary>
        HullFragment Gather(int start, bool[] seen, bool includeDetached)
        {
            var f = new HullFragment { Shattered = true };
            var queue = new Queue<int>();
            seen[start] = true;
            queue.Enqueue(start);
            DVec3 weighted = DVec3.Zero;
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                var m = Modules[at];
                f.Modules.Add(at);
                f.Hardpoints.AddRange(m.Hardpoints);
                f.MassShare += m.MassShare;
                weighted += m.Centre * m.MassShare;
                if (!m.Shattered) f.Shattered = false;
                // A shattered module holds nothing together.
                if (m.Shattered) continue;
                foreach (var j in Joints)
                {
                    if (j.Broken || !j.Joins(at)) continue;
                    int next = j.Other(at);
                    if (seen[next] || (!includeDetached && Modules[next].Detached)) continue;
                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }
            f.Hulk = f.Modules.Contains(Root);
            f.Centroid = f.MassShare > 0 ? weighted / f.MassShare : Modules[start].Centre;
            return f;
        }

        internal static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }
    }

    /// <summary>What a hull that has lost a piece loses with it (U.SPC-T596).</summary>
    public static class ModularHulls
    {
        /// <summary>The share of a lost piece's mass the hull's points lose with it: the rest of the damage that cut it
        /// away was already done by the hits themselves.</summary>
        public const double HullLossPerMass = 0.5;

        /// <summary>
        /// <paramref name="piece"/> has come away from <paramref name="combat"/>'s hull: every hardpoint on it goes with it
        /// (shot away, its shield emitter's field down), and the hull's points lose <see cref="HullLossPerMass"/> of its
        /// share of them, never below one, so cutting a piece off never kills a hull by itself. Returns how many standing
        /// hardpoints were lost.
        /// </summary>
        public static int ApplyBreak(ShipCombat combat, HullFragment piece)
        {
            if (combat == null || piece == null || piece.Hulk) return 0;
            int lost = 0;
            foreach (int k in piece.Hardpoints)
            {
                if (k < 0 || k >= combat.Subsystems.Count) continue;
                var s = combat.Subsystems[k];
                if (!s.Destroyed) lost++;
                s.Hp = 0;
                s.Restoring = 0;
                if (s.Kind == SubsystemKind.ShieldGenerator) s.Shield = 0;
            }
            combat.Hull = Math.Max(Math.Min(combat.Hull, 1), combat.Hull - combat.Spec.Hull * piece.MassShare * HullLossPerMass);
            return lost;
        }
    }

    /// <summary>
    /// What a piece of hull is worth to a salvager (U.SPC-T596): what it breaks down into, and what its sound parts
    /// fetch whole. The structure is mostly alloys; a part a yard fits comes back as its own bill of materials
    /// (<see cref="ModuleSpec.Bill"/>), as much of it as the part's health leaves; a part no yard fits (a bridge, a reactor, a
    /// hangar) as the one good it is mostly made of (<see cref="SalvageSession.ScrapOf"/>).
    /// </summary>
    public static class HullSalvage
    {
        /// <summary>The share of a structure's dry mass that is recovered as alloys: the rest is holed, buckled or gone.</summary>
        public const double StructureRecovery = 0.3;

        /// <summary>The share of a part's bill a breaker gets back when it cuts the part up.</summary>
        public const double PartRecovery = 0.5;

        /// <summary>What a sound part sold whole fetches against its bill at the port's prices: secondhand, uncertified.</summary>
        public const double SecondhandShare = 0.6;

        /// <summary>Tonnes a part no yard fits breaks down into, per hit point it was built with.</summary>
        public const double TonnesPerHp = 0.05;

        /// <summary>
        /// What <paramref name="piece"/> of a hull built to <paramref name="spec"/> breaks down into: its structure's
        /// alloys by what is left of it, and every part still on it by its health (<paramref name="combat"/>'s, or whole when
        /// there is no live state). A shattered piece is rubble and gives only half its structure's worth.
        /// </summary>
        public static List<SalvageStack> Scrap(ModularHull hull, HullFragment piece, CombatSpec spec, ShipCombat combat = null)
        {
            var tonnes = new double[GoodInfo.Count];
            double dry = spec.DryMassTonnes > 0 ? spec.DryMassTonnes : spec.MassKt * 1000 * 0.1;
            foreach (int id in piece.Modules)
            {
                var m = hull.Module(id);
                tonnes[(int)Good.Alloys] += dry * m.MassShare * StructureRecovery * (m.Shattered ? 0.5 : 0.25 + 0.75 * m.Condition);
            }
            foreach (int k in piece.Hardpoints)
            {
                var def = spec.Layout[k];
                double health = combat != null && k < combat.Subsystems.Count ? combat.Subsystems[k].Health : 1;
                if (health <= 0) continue;
                var module = FireCones.ModuleOf(def.Kind);
                if (module.HasValue)
                {
                    var ms = ModuleSpec.Of(module.Value);
                    double scale = Math.Max(0.1, spec.LengthKm / 0.45);
                    for (int i = 0; i < ms.Bill.Length; i++) tonnes[(int)ms.Bill[i].good] += ms.Tonnes(i, scale) * PartRecovery * health;
                }
                else tonnes[(int)SalvageSession.ScrapOf(def.Kind)] += def.MaxHp * TonnesPerHp * health;
            }
            var stacks = new List<SalvageStack>();
            for (int g = 0; g < tonnes.Length; g++)
                if (tonnes[g] > 0) stacks.Add(new SalvageStack((Good)g, tonnes[g]));
            return stacks;
        }

        /// <summary>What <paramref name="stacks"/> sell for at <paramref name="port"/>'s prices, or at base prices with no port.</summary>
        public static double Value(IEnumerable<SalvageStack> stacks, Settlement port = null)
        {
            double total = 0;
            foreach (var s in stacks) total += s.Tonnes * PriceOf(s.Good, port);
            return total;
        }

        /// <summary>What a sound part of <paramref name="kind"/> at <paramref name="health"/> sells for whole at
        /// <paramref name="port"/> (base prices with none), on a hull of <paramref name="lengthKm"/>: its bill at
        /// <see cref="SecondhandShare"/>; 0 for a part no yard fits, which a breaker only cuts up.</summary>
        public static double PartValue(SubsystemKind kind, double health, double lengthKm, Settlement port = null)
        {
            var module = FireCones.ModuleOf(kind);
            if (!module.HasValue || health <= 0) return 0;
            var ms = ModuleSpec.Of(module.Value);
            double scale = Math.Max(0.1, lengthKm / 0.45), total = 0;
            for (int i = 0; i < ms.Bill.Length; i++) total += ms.Tonnes(i, scale) * PriceOf(ms.Bill[i].good, port);
            return total * SecondhandShare * Math.Min(1, health);
        }

        static double PriceOf(Good g, Settlement port) =>
            port != null && port.Price[(int)g] > 0 ? port.Price[(int)g] : GoodInfo.Of(g).BasePrice;
    }

    /// <summary>
    /// One piece of a wreck (U.SPC-T596): the main hulk, or a piece the hull broke into, drifting off through the debris
    /// field with what it breaks down into aboard. A piece starts where it was on the hull and drifts out along its own
    /// heading as the field spreads (<see cref="Wreck.DebrisRadiusKm"/>), so the pieces of a wreck scatter but stay
    /// within its field.
    /// </summary>
    public sealed class WreckPiece
    {
        public int Index;
        public HullFragment Fragment;

        /// <summary>What it still breaks down into for a salvager: it comes off as it is taken.</summary>
        public readonly List<SalvageStack> Scrap = new List<SalvageStack>();

        public bool Hulk => Fragment.Hulk;

        /// <summary>Where it has drifted to at <paramref name="jd"/>, km from the wreck's centre, in the wreck's frame. The
        /// hulk stays at the centre.</summary>
        public DVec3 OffsetKm(Wreck wreck, double jd)
        {
            if (Hulk) return DVec3.Zero;
            var start = Fragment.Centroid * wreck.LengthKm;
            uint h = ModularHull.Hash(unchecked((uint)wreck.Id * 31337u + (uint)Index * 7919u));
            var heading = Fragment.Centroid.LengthSquared > 1e-12 ? Fragment.Centroid.Normalized : DVec3.UnitZ;
            // A little scatter off the line straight out from the hull, fixed per piece.
            double u = (h & 0xFFFF) / 65535.0 - 0.5, v = ((h >> 16) & 0xFFFF) / 65535.0 - 0.5;
            heading = (heading + new DVec3(u, v, u * v) * 0.8).Normalized;
            double share = 0.5 + 0.5 * ((ModularHull.Hash(h) & 0xFFFF) / 65535.0);
            double spread = Math.Max(0, wreck.DebrisRadiusKm(jd) - 0.5 * wreck.LengthKm);
            return start + heading * (spread * share);
        }
    }
}
