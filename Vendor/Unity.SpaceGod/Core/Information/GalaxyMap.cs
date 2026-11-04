using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>How thick the raiders are about a system, as far as the news has it.</summary>
    public enum PirateActivity
    {
        None,

        /// <summary>A few raider hulls about, or a haven known, or a raid or two through the gates.</summary>
        Low,

        /// <summary>Raiders in numbers, havens, raids coming through: a system to cross with an escort.</summary>
        High,
    }

    /// <summary>One polity's share of a system on the map: the standing markets it holds there and their people.</summary>
    public sealed class MapHolding
    {
        public Faction Faction;
        public double Population;
        public int Ports;
    }

    /// <summary>
    /// One star system as the galaxy map shows it (U.SPC-T103): only what the player knows of it, from the newest report
    /// the system they are in holds (<see cref="Newswire.Known"/>), and as old as that report; the system they are in is
    /// seen as it is.
    /// </summary>
    public sealed class MapSystem
    {
        public StarSystem System;

        /// <summary>The system the player is in: seen as it is now.</summary>
        public bool Here;

        /// <summary>What the map knows it by: a live look for <see cref="Here"/>, else the newest report held of it; null when no
        /// news of it has ever come (an unsettled system: its star and its gates are on the charts, nothing more).</summary>
        public SystemReport Report;

        public bool Known => Report != null;

        /// <summary>How old what is known of it is, days (0 here, NaN when nothing is).</summary>
        public double AgeDays = double.NaN;

        /// <summary>Who held it, as last heard; null if nobody (or nothing is known).</summary>
        public Faction Holder;

        /// <summary>Every polity with standing markets there, the biggest first: the territory the map colours it by.</summary>
        public readonly List<MapHolding> Holdings = new List<MapHolding>();

        /// <summary>Markets on the last report.</summary>
        public int Markets;

        /// <summary>Armed raider hulls there and havens known there, as last heard, and raids that came in through its gates in the
        /// <see cref="GalaxyMap.FlowWindowDays"/> before that.</summary>
        public int PirateShips, Havens, Raids;

        public PirateActivity Pirates;

        /// <summary>Its ports under a declared blockade or a siege, as last heard.</summary>
        public readonly List<PortThreat> Threats = new List<PortThreat>();

        /// <summary>Two polities at war both hold markets there: the front runs through the system itself.</summary>
        public bool Contested;

        /// <summary>Who is fighting over it, when <see cref="Contested"/>.</summary>
        public FactionPair ContestedBy;

        public override string ToString() => System.Name;
    }

    /// <summary>
    /// One gate link as the galaxy map shows it (U.SPC-T103): the traffic logged through it in the
    /// <see cref="GalaxyMap.FlowWindowDays"/> up to the freshest news of either end, whether either end is shut, who holds
    /// each, and whether it is a war front. A link still being built has no <see cref="Link"/> yet.
    /// </summary>
    public sealed class MapLink
    {
        /// <summary>The working link; null for a gate still under construction.</summary>
        public GateLink Link;

        public StarSystem A, B;

        /// <summary>Up to when the traffic through it is known: the newer of its two ends' news (NaN when neither is known).</summary>
        public double KnownAsOfJd = double.NaN;

        public bool Known => !double.IsNaN(KnownAsOfJd);

        /// <summary>Crossings (a group counts once) in the window, either way, and by what for.</summary>
        public int Crossings, Trade, Couriers, Raids, Navy, Surveys;

        /// <summary>Crossings from <see cref="A"/> to <see cref="B"/>, and back.</summary>
        public int TowardB, TowardA;

        /// <summary>Cargo carried through it in the window, tonnes.</summary>
        public double Tonnes;

        /// <summary>The traffic as a share of the busiest link's on the map (0..1).</summary>
        public double Flow;

        /// <summary>Each end shut (under blockade, or its ring out of action), as last heard of that end.</summary>
        public bool ShutAtA, ShutAtB;

        /// <summary>Each end's ring damaged out of action in a war (U.SPC-T97), as last heard of that end: shut to everyone until rebuilt.</summary>
        public bool DownAtA, DownAtB;

        public bool Down => DownAtA || DownAtB;

        public bool Shut => ShutAtA || ShutAtB;

        /// <summary>Who holds each end's gate, as last heard (null where unknown or nobody).</summary>
        public Faction OwnerA, OwnerB;

        /// <summary>The systems at its ends are held by polities at war with each other.</summary>
        public bool WarFront;

        /// <summary>The two sides, when <see cref="WarFront"/>.</summary>
        public FactionPair Front;

        /// <summary>A gate still being built (see <see cref="GateWork"/>), how far along, and by whom.</summary>
        public bool UnderConstruction;
        public double Progress;
        public Faction Builder;

        public StarSystem Other(StarSystem s) => s == A ? B : s == B ? A : null;

        public override string ToString() => A.Name + " - " + B.Name;
    }

    /// <summary>A gate link being built between two systems, for the map to show (fed from whatever builds them).</summary>
    public sealed class GateWork
    {
        public StarSystem A, B;

        /// <summary>How far along, 0..1.</summary>
        public double Progress;

        public Faction Builder;
    }

    /// <summary>The galaxy map's whole picture at one moment, from one system's point of view (see <see cref="GalaxyMap.Build"/>).</summary>
    public sealed class GalaxyMapModel
    {
        public StarSystem Here;
        public double Jd;
        public readonly List<MapSystem> Systems = new List<MapSystem>();
        public readonly List<MapLink> Links = new List<MapLink>();

        /// <summary>The wars the player knows of: every pair of polities at war, as the newest news has it.</summary>
        public readonly List<FactionPair> Wars = new List<FactionPair>();

        /// <summary>The most crossings any link on the map has in the window (what <see cref="MapLink.Flow"/> is a share of).</summary>
        public int BusiestCrossings;

        public MapSystem Of(StarSystem s)
        {
            foreach (var m in Systems)
                if (m.System == s) return m;
            return null;
        }

        public MapLink Between(StarSystem a, StarSystem b)
        {
            foreach (var l in Links)
                if ((l.A == a && l.B == b) || (l.A == b && l.B == a)) return l;
            return null;
        }

        /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> are at war, as far as the player knows.</summary>
        public bool AtWar(Faction a, Faction b)
        {
            if (a == null || b == null || a == b) return false;
            foreach (var w in Wars)
                if (w.Involves(a) && w.Involves(b)) return true;
            return false;
        }

        /// <summary>The links that are war fronts.</summary>
        public IEnumerable<MapLink> Fronts()
        {
            foreach (var l in Links)
                if (l.WarFront) yield return l;
        }
    }

    /// <summary>
    /// The interstellar map's picture (U.SPC-T103), engine-free: every simulated system with the gate network between
    /// them, each system coloured by the polity that holds it, the traffic through each gate, the war fronts and
    /// blockades, the raiders, and any gates being built -- limited to what the player knows. The system the player is in
    /// is seen as it is; every other only by the newest report that system holds of it (<see cref="Newswire.Known"/>),
    /// so a siege far off shows only once word of it has come through the gates, and a system never heard from shows
    /// only as a star on the charts. A link's traffic is what its gates' owners logged
    /// (<see cref="InterstellarTraffic.CrossingLog"/>) up to the freshest news of either end.
    /// </summary>
    public static class GalaxyMap
    {
        /// <summary>The window traffic and raids are counted over, days.</summary>
        public const double FlowWindowDays = 60;

        /// <summary>
        /// The map as the player in <paramref name="here"/> knows it at <paramref name="jd"/>. <paramref name="works"/> are gates
        /// under construction to show (none if null).
        /// </summary>
        public static GalaxyMapModel Build(Galaxy galaxy, StarSystem here, double jd, IEnumerable<GateWork> works = null)
        {
            var model = new GalaxyMapModel { Here = here, Jd = jd };
            var news = galaxy.Traffic?.News;
            var log = galaxy.Traffic?.CrossingLog;
            SystemReport newest = null;

            foreach (var s in galaxy.Systems)
            {
                var m = new MapSystem { System = s, Here = s == here };
                m.Report = m.Here ? SystemReport.Take(s, jd) : news?.Known(here, s);
                if (m.Report != null)
                {
                    m.AgeDays = m.Here ? 0 : Math.Max(0, jd - m.Report.AsOfJd);
                    if (newest == null || m.Report.AsOfJd > newest.AsOfJd) newest = m.Report;
                }
                model.Systems.Add(m);
            }
            if (newest != null) model.Wars.AddRange(newest.Wars);

            foreach (var m in model.Systems)
            {
                var r = m.Report;
                if (r == null) continue;
                m.Holder = r.Holder;
                m.Markets = r.Markets.Count;
                foreach (var st in r.Markets)
                {
                    if (st.Fallen || st.Owner == null || st.Owner.IsPirate) continue;
                    MapHolding h = null;
                    foreach (var x in m.Holdings)
                        if (x.Faction == st.Owner) h = x;
                    if (h == null) m.Holdings.Add(h = new MapHolding { Faction = st.Owner });
                    h.Population += st.Population;
                    h.Ports++;
                }
                m.Holdings.Sort((a, b) => b.Population.CompareTo(a.Population));
                for (int i = 0; i < m.Holdings.Count && !m.Contested; i++)
                    for (int j = i + 1; j < m.Holdings.Count; j++)
                        if (model.AtWar(m.Holdings[i].Faction, m.Holdings[j].Faction))
                        {
                            m.Contested = true;
                            m.ContestedBy = new FactionPair(m.Holdings[i].Faction, m.Holdings[j].Faction);
                            break;
                        }
                m.Threats.AddRange(r.PortThreats);
                m.PirateShips = r.PirateShips;
                m.Havens = r.Havens;
                if (log != null)
                    foreach (var c in log)
                        if (c.Purpose == VoyagePurpose.Raid && c.Jd <= r.AsOfJd && c.Jd > r.AsOfJd - FlowWindowDays && c.Link.Other(c.From) == m.System)
                            m.Raids++;
                int score = m.PirateShips + 4 * m.Havens + 3 * m.Raids;
                m.Pirates = score == 0 ? PirateActivity.None : score < 8 ? PirateActivity.Low : PirateActivity.High;
            }

            if (galaxy.Network != null)
                foreach (var link in galaxy.Network.Links)
                    model.Links.Add(LinkOf(model, link, log));
            if (works != null)
                foreach (var w in works)
                {
                    if (w?.A == null || w.B == null || model.Between(w.A, w.B) != null) continue;
                    var a = model.Of(w.A);
                    var b = model.Of(w.B);
                    // A gate being built is news of its two ends: shown once either is known.
                    if (a == null || b == null || (!a.Known && !b.Known)) continue;
                    model.Links.Add(new MapLink
                    {
                        A = w.A, B = w.B, UnderConstruction = true, Progress = Math.Max(0, Math.Min(1, w.Progress)), Builder = w.Builder,
                        KnownAsOfJd = Math.Max(a.Known ? a.Report.AsOfJd : double.NegativeInfinity, b.Known ? b.Report.AsOfJd : double.NegativeInfinity),
                    });
                }

            foreach (var l in model.Links) model.BusiestCrossings = Math.Max(model.BusiestCrossings, l.Crossings);
            foreach (var l in model.Links) l.Flow = model.BusiestCrossings > 0 ? (double)l.Crossings / model.BusiestCrossings : 0;
            return model;
        }

        static MapLink LinkOf(GalaxyMapModel model, GateLink link, IReadOnlyList<GateCrossing> log)
        {
            var l = new MapLink { Link = link, A = link.A, B = link.B };
            var a = model.Of(link.A);
            var b = model.Of(link.B);
            double asOf = double.NegativeInfinity;
            if (a != null && a.Known)
            {
                asOf = Math.Max(asOf, a.Report.AsOfJd);
                Gate(a.Report, link.AtA, model.Jd, out l.ShutAtA, out l.DownAtA, out l.OwnerA);
            }
            if (b != null && b.Known)
            {
                asOf = Math.Max(asOf, b.Report.AsOfJd);
                Gate(b.Report, link.AtB, model.Jd, out l.ShutAtB, out l.DownAtB, out l.OwnerB);
            }
            if (double.IsNegativeInfinity(asOf)) return l;
            l.KnownAsOfJd = asOf;

            if (log != null)
                foreach (var c in log)
                {
                    if (c.Link != link || c.Jd > asOf || c.Jd <= asOf - FlowWindowDays) continue;
                    l.Crossings++;
                    if (c.From == link.A) l.TowardB++;
                    else l.TowardA++;
                    l.Tonnes += c.Tonnes;
                    switch (c.Purpose)
                    {
                        case VoyagePurpose.Trade: l.Trade++; break;
                        case VoyagePurpose.Courier: l.Couriers++; break;
                        case VoyagePurpose.Raid: l.Raids++; break;
                        case VoyagePurpose.Expedition: l.Navy++; break;
                        case VoyagePurpose.Survey: l.Surveys++; break;
                    }
                }

            var ha = a?.Holder;
            var hb = b?.Holder;
            if (model.AtWar(ha, hb))
            {
                l.WarFront = true;
                l.Front = new FactionPair(ha, hb);
            }
            return l;
        }

        static void Gate(SystemReport r, JumpGate gate, double jd, out bool shut, out bool down, out Faction owner)
        {
            shut = down = false;
            owner = null;
            foreach (var g in r.Gates)
                if (g.Gate == gate)
                {
                    down = g.Down;
                    shut = g.Shut(jd) || down;
                    owner = g.Owner;
                    return;
                }
        }
    }
}
