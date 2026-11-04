using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Corvettes (U.SPC-T476): system defence and local law enforcement. A corvette (<see cref="ShipClass.Corvette"/>) has no
    /// FTL drive, and puts the weight saved into patrol: deep tanks, a patrol search suite (<see cref="SensorSuite.Patrol"/>)
    /// and a brawler's guns. Every navy stations one for good at each of its ports in a system, the most populous first, up to
    /// <see cref="PerNavy"/>: a planet, moon or station (<see cref="Ship.CorvetteStation"/>). From there it rides the hyper lanes
    /// of its home system on a beat (<see cref="Beat"/>): its station, the navy's other ports round the same planet, and the
    /// navy's port at the far end of each lane out of it, always coming back to its station between runs.
    /// <para>It is the law there. As a navy patrol it hails and scans the player's ship in its navy's space (see
    /// <see cref="Law.UpdatePilot"/>); it defends the ports on its beat; and when its navy has seen pirates or a clan's
    /// smugglers (<see cref="Ship.Runner"/>) at a port on its beat or anywhere round its home planet, it goes to run them down
    /// (<see cref="OrderKind.Hunt"/>), fighting in the law's name where it finds them.</para>
    /// <para>It never leaves that system: no drive, no gate, no carriage (<see cref="FtlAccess.HomeSystem"/>), never merged into
    /// a battle group, given an objective elsewhere or counted as a warship (it flies as a <see cref="Ship.PatrolWing"/>). One
    /// lost is replaced at its navy's expense after <see cref="RebuildDays"/>; one whose port has changed hands is posted to
    /// another of its navy's ports.</para>
    /// </summary>
    [SettingsSource]
    public static class Corvettes
    {
        /// <summary>The settings menu's corvettes per navy (AI): how many a navy keeps in each system it holds ports in.</summary>
        public static readonly RangeSetting PerNavy = new RangeSetting("corvettes.perNavy", SettingGroups.Ai,
            "Corvettes per navy", 3, 0, 8, 1,
            "How many corvettes (system-defence patrol hulls with no FTL drive) a navy stations for good in each system it holds " +
            "ports in, one at each of its ports, the most populous first. They patrol the hyper lanes from there and run down " +
            "smugglers and pirates. 0 keeps none.",
            v => v.ToString("0", CultureInfo.InvariantCulture), "corvette patrol police law smuggler pirate system defence navy");

        /// <summary>The most ports a corvette's beat takes in round its own planet besides its station.</summary>
        public const int HomePorts = 1;

        /// <summary>The most hyper lanes out of its home planetary system a corvette's beat runs down.</summary>
        public const int LanePorts = 2;

        /// <summary>How long a navy goes short of its corvettes in a system before it commissions a new one (days).</summary>
        public const double RebuildDays = 15;

        /// <summary>The damage (share of its hull and systems) past which a corvette leaves its beat for a yard.</summary>
        public const double RepairDamage = 0.3;

        static readonly string[] Names =
        {
            "Constable", "Marshal", "Warden", "Bailiff", "Reeve", "Provost", "Exciseman", "Tidewaiter", "Searcher", "Gauger",
            "Sheriff", "Justiciar", "Assessor", "Inspector", "Watchman", "Beadle",
        };

        /// <summary>Whether <paramref name="f"/> keeps corvettes: a power with a navy, not a pirate clan.</summary>
        public static bool Keeps(Faction f) => f != null && f.IsMilitary && !f.IsPirate;

        /// <summary>How many corvettes a navy holding <paramref name="ports"/> ports in a system keeps there: one a port, up to
        /// <see cref="PerNavy"/>.</summary>
        public static int Wanted(int ports) => Math.Max(0, Math.Min(ports, (int)Math.Round(PerNavy.Value)));

        /// <summary>Whether <paramref name="s"/> is a corvette on its station's books (not a corvette hull the player flies or
        /// bought, which has no station).</summary>
        public static bool Stationed(Ship s) => s != null && s.CorvetteStation >= 0;

        /// <summary><paramref name="f"/>'s stationed corvettes afloat in <paramref name="w"/>.</summary>
        public static List<Ship> Of(World w, Faction f)
        {
            var list = new List<Ship>();
            foreach (var s in w.Fleet.Ships)
                if (Stationed(s) && s.Faction == f && !s.Destroyed && !s.Derelict && w.Fleet.IsPresent(s)) list.Add(s);
            return list;
        }

        /// <summary>
        /// The beat of a corvette of <paramref name="f"/> stationed at <paramref name="station"/> in <paramref name="w"/>: its
        /// station first, then up to <see cref="HomePorts"/> of the navy's other ports round the same planet (its moons and
        /// stations), then, down each hyper lane out of that planetary system, the navy's most populous port at the far end
        /// (up to <see cref="LanePorts"/> lanes). Every body on it is in the corvette's home system.
        /// </summary>
        public static int[] Beat(World w, Faction f, int station)
        {
            var beat = new List<int> { station };
            var lanes = w.Fleet.Lanes;
            if (lanes == null) return beat.ToArray();
            var ports = PatrolWings.Ports(w, f);
            int home = lanes.SystemOf(station);
            int near = 0;
            foreach (int p in ports)
            {
                if (near >= HomePorts) break;
                if (!beat.Contains(p) && lanes.SystemOf(p) == home)
                {
                    beat.Add(p);
                    near++;
                }
            }
            int runs = 0;
            foreach (var lane in lanes.Lanes)
            {
                if (runs >= LanePorts) break;
                if (lane.A != home && lane.B != home) continue;
                int far = lane.Other(home);
                foreach (int p in ports)
                    if (!beat.Contains(p) && lanes.SystemOf(p) == far)
                    {
                        beat.Add(p);
                        runs++;
                        break;
                    }
            }
            return beat.ToArray();
        }

        /// <summary>Whether <paramref name="body"/> is on <paramref name="corvette"/>'s beat, or round its home planet: the
        /// ground it polices.</summary>
        public static bool Polices(World w, Ship corvette, int body)
        {
            if (!Stationed(corvette) || body < 0) return false;
            if (PatrolWings.OnBeat(corvette, body)) return true;
            var lanes = w.Fleet.Lanes;
            return lanes != null && lanes.SystemOf(body) == lanes.SystemOf(corvette.CorvetteStation);
        }

        /// <summary>The next body on <paramref name="corvette"/>'s patrol from <paramref name="here"/>: back to its station
        /// from anywhere else, and from its station out down the next run of its beat in turn.</summary>
        public static int NextOnBeat(Ship corvette, int here)
        {
            int station = corvette.CorvetteStation;
            var beat = corvette.Route;
            if (here != station || beat == null || beat.Length <= 1) return station;
            corvette.RouteIndex = corvette.RouteIndex % (beat.Length - 1) + 1;
            return beat[corvette.RouteIndex];
        }

        /// <summary>
        /// Stations the corvettes of every navy holding ports in <paramref name="w"/> (the system's start: free, as its patrol
        /// wings are), one at each of its most populous ports up to <see cref="PerNavy"/>. Returns them.
        /// </summary>
        public static List<Ship> Raise(World w, double jd)
        {
            var raised = new List<Ship>();
            foreach (var f in new List<Faction>(w.Fleet.Factions))
            {
                if (!Keeps(f)) continue;
                var ports = PatrolWings.Ports(w, f);
                int wanted = Wanted(ports.Count);
                for (int i = 0; i < wanted; i++) raised.Add(Station(w, f, ports[i], jd));
            }
            return raised;
        }

        /// <summary>
        /// Commissions a corvette for <paramref name="f"/> and stations it at <paramref name="station"/> in <paramref name="w"/>,
        /// holding off the port. Added to the system's fleet directly, drawing nothing from its random stream.
        /// </summary>
        public static Ship Station(World w, Faction f, int station, double jd)
        {
            int named = 0;
            if (w.Polities != null)
                foreach (var s in w.Polities.Navy(f))
                    if (Stationed(s)) named++;
            string name = Names[named % Names.Length] + (named >= Names.Length ? " " + (named / Names.Length + 1) : "");
            var ship = new Ship
            {
                Name = f.Prefix.Length > 0 ? f.Prefix + " " + name : name,
                Spec = ShipClassSpec.Of(ShipClass.Corvette),
                Faction = f,
                Role = ShipRole.Patrol,
                Combat = new ShipCombat(CombatSpec.Of(ShipClass.Corvette), ShipClass.Corvette),
                PatrolWing = true,
                CorvetteStation = station,
                Route = Beat(w, f, station),
                RouteIndex = 0,
            };
            w.Fleet.Receive(ship);
            ship.BandId = ship.Id;
            var body = w.Catalog[station];
            double r = Math.Max(body.RadiusKm * 5, body.RadiusKm + 200);
            // Spread round the port by the golden angle, so two corvettes at one port never hold the same spot.
            double a = 2.399963 * (ship.Id % 97);
            var offset = new DVec3(Math.Cos(a), Math.Sin(a), 0.1).Normalized * r;
            Fleet.PinAt(ship, station, offset, new DVec3(-Math.Sin(a), Math.Cos(a), 0), jd);
            w.NotifyAdded(ship);
            return ship;
        }

        /// <summary>
        /// A day's upkeep of <paramref name="w"/>'s corvettes (run by its <see cref="War"/>): one whose port is no longer its
        /// navy's is posted to another of its navy's ports (one without a corvette first), and a navy short of corvettes
        /// there commissions one at its expense for a port without one, once it has been short for <see cref="RebuildDays"/>,
        /// when it can pay and crew it and nobody is fighting at that port. <paramref name="shortSince"/> keeps, per navy, when
        /// it went short.
        /// </summary>
        public static void Upkeep(World w, Dictionary<Faction, double> shortSince, double jd)
        {
            foreach (var f in w.Fleet.Factions)
            {
                if (!Keeps(f)) continue;
                var ports = PatrolWings.Ports(w, f);
                var mine = Of(w, f);
                foreach (var c in mine)
                {
                    if (ports.Count == 0 || ports.Contains(c.CorvetteStation)) continue;
                    Repost(w, c, Unmanned(ports, mine) ?? ports[0]);
                }
                int wanted = Wanted(ports.Count);
                if (mine.Count >= wanted)
                {
                    shortSince.Remove(f);
                    continue;
                }
                if (!shortSince.TryGetValue(f, out double since))
                {
                    shortSince[f] = jd;
                    continue;
                }
                if (jd - since < RebuildDays) continue;
                var port = Unmanned(ports, mine);
                if (port == null) continue;
                double price = Economy.HullPrice(ShipClass.Corvette);
                if (f.Treasury < price || !Crews.CanCrew(f, ShipClass.Corvette) || w.War.BattleAt(port.Value) != null) continue;
                f.Treasury -= price;
                Crews.Draft(f, Crews.Complement(ShipClass.Corvette));
                Station(w, f, port.Value, jd);
                if (mine.Count + 1 >= wanted) shortSince.Remove(f);
                else shortSince[f] = jd;
            }
        }

        /// <summary>The most populous of <paramref name="ports"/> with none of <paramref name="corvettes"/> stationed at it, or null.</summary>
        static int? Unmanned(List<int> ports, List<Ship> corvettes)
        {
            foreach (int p in ports)
            {
                bool manned = false;
                foreach (var c in corvettes)
                    if (c.CorvetteStation == p) manned = true;
                if (!manned) return p;
            }
            return null;
        }

        /// <summary>Posts <paramref name="corvette"/> to <paramref name="station"/>: its new beat starts from there.</summary>
        public static void Repost(World w, Ship corvette, int station)
        {
            corvette.CorvetteStation = station;
            corvette.Route = Beat(w, corvette.Faction, station);
            corvette.RouteIndex = 0;
        }
    }
}
