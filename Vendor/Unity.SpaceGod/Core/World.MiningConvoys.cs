using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    public sealed partial class World
    {
        // ---------------------------------------------------------------- the raw-material convoys' freighters (U.SPC-T523)

        /// <summary>Names the belt works give their convoy freighters, in turn.</summary>
        static readonly string[] WorksFreighterNames = { "Ore Wain", "Slag Queen", "Belt Drayman", "Iron Carter", "Lodestone", "Bulk Venture", "Smelter's Due", "Heavy Mercy" };

        int worksFreightersNamed;

        /// <summary>The escort classes a works' freighter flies with, in turn: a gunship leading, then a fighter.</summary>
        static readonly ShipClass[] WorksEscortClasses = { ShipClass.Gunship, ShipClass.Fighter };

        /// <summary>
        /// Gives <paramref name="op"/> a real hull for each of its convoy freighters (U.SPC-T523): every one a
        /// <see cref="ShipClass.Freighter"/> in this world's fleet, owned by the works' faction, lying at its refinery until
        /// the works loads it, each with <see cref="MiningOperation.EscortsPerFreighter"/> of its owner's warships flying
        /// with it in formation as its escort. Returns the freighters.
        /// </summary>
        public List<Ship> RaiseFreighters(MiningOperation op, double jd)
        {
            op.Freighters ??= new List<Ship>();
            var made = new List<Ship>();
            int home = op.RefineryBody, to = op.Destination?.Body ?? op.RefineryBody;
            while (op.Freighters.Count < op.ConvoyFreighters)
            {
                string name = WorksFreighterNames[worksFreightersNamed % WorksFreighterNames.Length] +
                              (worksFreightersNamed >= WorksFreighterNames.Length ? " " + (worksFreightersNamed / WorksFreighterNames.Length + 1) : "");
                worksFreightersNamed++;
                var ship = Fleet.AddShip(name, ShipClass.Freighter, op.Owner, ShipRole.Hauler);
                ship.Ports = home == to ? new[] { home } : new[] { home, to };
                ship.CapacityTonnes = Economy.CapacityOf(ShipClass.Freighter);
                ship.Cargo = "Empty, waiting on the separators";
                Fleet.Station(ship, home, jd);
                for (int i = 0; i < op.EscortsPerFreighter; i++)
                {
                    var e = Fleet.AddShip(name + " escort " + (i + 1), WorksEscortClasses[i % WorksEscortClasses.Length], op.Owner, ShipRole.Escort);
                    Fleet.AddEscort(ship, e, Fleet.SlotFor(i));
                }
                op.Freighters.Add(ship);
                made.Add(ship);
            }
            op.ConvoyFreighters = op.Freighters.Count;
            return made;
        }

        int worksEscortsRaised;

        /// <summary>
        /// The works make good the escorts its freighters have lost (U.SPC-T592): a freighter lying idle and empty at its
        /// refinery that flies with fewer warships than <see cref="MiningOperation.EscortsPerFreighter"/> (some lost in a
        /// fight, left adrift or taken) is given fresh ones, in the classes the works first sent, before it loads again.
        /// Returns how many were built.
        /// </summary>
        public int ReplaceWorksEscorts(double jd)
        {
            if (Mining == null) return 0;
            int built = 0;
            foreach (var op in Mining.Operations)
            {
                if (op.Freighters == null || op.EscortsPerFreighter <= 0) continue;
                foreach (var f in op.Freighters)
                {
                    if (f.Destroyed || f.Derelict || f.HasCargo || f.Phase != ShipPhase.Parked || f.Orbit == null || f.Orbit.Body != op.RefineryBody) continue;
                    if (!FreighterFree(f)) continue;
                    int have = 0;
                    foreach (var s in Fleet.Ships)
                        if (s.Leader == f && !s.Destroyed && !s.Derelict && s.Faction == op.Owner) have++;
                    for (; have < op.EscortsPerFreighter; have++)
                    {
                        var e = Fleet.AddShip($"{f.Name} escort {++worksEscortsRaised}", WorksEscortClasses[have % WorksEscortClasses.Length], op.Owner, ShipRole.Escort);
                        Fleet.AddEscort(f, e, Fleet.SlotFor(have));
                        built++;
                    }
                }
            }
            return built;
        }

        /// <summary>Whether a works' freighter lying at its refinery may sail: not at the player's helm, not fighting, not held.</summary>
        bool FreighterFree(Ship freighter) => freighter != PilotedShip && freighter.Battle == null && (Fleet.IsHeld == null || !Fleet.IsHeld(freighter));

        /// <summary>The works has loaded a freighter with a convoy: it sets out at once for the convoy's warehouse, its escorts with it.</summary>
        void FreighterSailed(MaterialConvoy convoy, double jd)
        {
            var f = convoy.Freighter;
            double now = double.IsNaN(Jd) ? jd : Math.Max(jd, Jd);
            f.Cargo = $"{convoy.Tonnes:N0} t of separated elements for the {convoy.To.Name}";
            Fleet.ScheduleNext(f, now);
        }

        /// <summary>A works' freighter has arrived somewhere: at its convoy's warehouse its hold is emptied there.</summary>
        void FreighterArrived(Ship freighter, double jd) => Mining?.FreighterArrived(freighter, jd);

        /// <summary>A works' freighter has brought its convoy in: the news, and home empty to its refinery.</summary>
        void FreighterDelivered(MaterialConvoy convoy, double jd)
        {
            var f = convoy.Freighter;
            f.Cargo = "Empty, back to the refinery";
            Log(jd, $"The freighter {f.Name} brings the {convoy.Name} in to the {convoy.To.Name}: {convoy.Tonnes:N0} t of raw materials",
                WorldEventKind.Economy, convoy.To.Body);
        }
    }
}
