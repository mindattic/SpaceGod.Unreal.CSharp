using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A planned trip over the jump gates (U.SPC-T516): the gates to take in order (<see cref="GateNetwork.Route"/>), one per
    /// leg, never a jump straight to the far system. Each leg is flown across the system the ship is in to that
    /// system's gate for the next link, then through it; <see cref="Passed"/> moves on to the next leg (or replans if the
    /// ship went through some other gate) and <see cref="Next"/> is the gate the HUD and map point at.
    /// </summary>
    public sealed class TravelCourse
    {
        readonly List<JumpGate> gates;
        int index;

        public StarSystem From { get; }
        public StarSystem Destination { get; }

        /// <summary>Every gate on the course, in order, the ones already taken included.</summary>
        public IReadOnlyList<JumpGate> Gates => gates;

        /// <summary>How many gates have been taken so far.</summary>
        public int Taken => index;

        public int Legs => gates.Count;
        public bool Done => index >= gates.Count;

        /// <summary>The gate to fly to and go through next, or null when the course is run.</summary>
        public JumpGate Next => index < gates.Count ? gates[index] : null;

        /// <summary>The system the next gate is in: the one the ship should be crossing now.</summary>
        public StarSystem Here => Next?.Home;

        /// <summary>The system the next gate leads to.</summary>
        public StarSystem NextSystem => Next?.Partner?.Home;

        TravelCourse(StarSystem from, StarSystem destination, List<JumpGate> route)
        {
            From = from;
            Destination = destination;
            gates = route;
        }

        /// <summary>
        /// Plans the quickest way from <paramref name="from"/> to <paramref name="to"/> for <paramref name="traveller"/>
        /// (null: any working gate, the view's own way). Null when there is none or the two are the same system.
        /// </summary>
        public static TravelCourse Plan(GateNetwork network, StarSystem from, StarSystem to, Faction traveller = null, double jd = double.NegativeInfinity)
        {
            if (network == null || from == null || to == null || from == to) return null;
            var route = network.Route(from, to, traveller, jd);
            if (route == null || route.Count == 0) return null;
            return new TravelCourse(from, to, route);
        }

        /// <summary>
        /// Records that the ship has gone through <paramref name="gate"/>. On course it moves to the next leg; through any
        /// other gate the course is replanned from where the ship now is (<paramref name="network"/>). Returns false when the
        /// course is over (arrived, or no way on from here).
        /// </summary>
        public bool Passed(JumpGate gate, GateNetwork network, Faction traveller, double jd)
        {
            if (Done) return false;
            if (gate == Next)
            {
                index++;
                return !Done;
            }
            var now = gate?.Partner?.Home;
            var replanned = now == null || now == Destination ? null : network?.Route(now, Destination, traveller, jd);
            if (replanned == null || replanned.Count == 0)
            {
                index = gates.Count;
                return false;
            }
            gates.Clear();
            gates.AddRange(replanned);
            index = 0;
            return true;
        }

        /// <summary>Brings the course up to a hull that has taken <paramref name="taken"/> gates of it (a crew flying it by its voyage).</summary>
        public void SyncTo(int taken) => index = Math.Max(index, Math.Min(taken, gates.Count));

        /// <summary>The course in a line: the systems in order, the next leg marked.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(gates.Count > 0 ? gates[0].Home.Name : From.Name);
            for (int i = 0; i < gates.Count; i++) sb.Append(" > ").Append(gates[i].Partner.Home.Name);
            if (Next != null)
                sb.Append("   ·   LEG ").Append(index + 1).Append(" OF ").Append(gates.Count).Append(": ").Append(Next.Name);
            return sb.ToString();
        }
    }
}
