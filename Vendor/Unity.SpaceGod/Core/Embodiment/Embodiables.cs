using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Where the things the player can embody are found (U.SPC-T18): a picker lists from here rather than
    /// building its own lists, so a new kind registers once and every picker sees it.
    /// </summary>
    public static class Embodiables
    {
        /// <summary>A ship's helm.</summary>
        public static ShipHelm HelmOf(World world, Ship ship) => new ShipHelm(world, ship);

        /// <summary>The cockpit of a launched fighter or bomber (U.SPC-T422).</summary>
        public static FighterSeat FighterOf(Battle battle, BattleUnit unit) => new FighterSeat(battle, unit);

        /// <summary>
        /// Every gun mount on <paramref name="unit"/> the player could take right now (intact, a direct-fire
        /// gun), each with <paramref name="host"/> as what it is mounted on.
        /// </summary>
        public static List<MountedTurret> TurretsOn(Battle battle, BattleUnit unit, IEmbodiable host = null)
        {
            var list = new List<MountedTurret>();
            if (battle == null || unit == null) return list;
            // A station's guns are mounted on the station (U.SPC-T447): a gun seat there moves up to the whole of it.
            host = host ?? StationOf(null, battle, unit);
            foreach (var s in unit.Combat.Subsystems)
            {
                var t = new MountedTurret(battle, unit, s, host);
                if (t.CanEmbody) list.Add(t);
            }
            return list;
        }

        /// <summary>Every gun mount on a ship in a fight, its helm as their host.</summary>
        public static List<MountedTurret> TurretsOn(World world, Ship ship) =>
            ship?.Battle == null ? new List<MountedTurret>() : TurretsOn(ship.Battle, ship.Battle.UnitOf(ship), HelmOf(world, ship));

        /// <summary>
        /// Every gun mount on any battle unit (a ship's battery, a defence platform's, a fort's), hosted by the ship's
        /// helm when it is a ship and <paramref name="world"/> is given, and by the station when it is a platform or a fort.
        /// </summary>
        public static List<MountedTurret> TurretsOn(World world, Battle battle, BattleUnit unit) =>
            TurretsOn(battle, unit, unit?.Ship != null && world != null ? HelmOf(world, unit.Ship) : StationOf(world, battle, unit));

        // ------------------------------------------------------------ stations (U.SPC-T447)

        /// <summary>One of <paramref name="settlement"/>'s defence platforms, as a station the player can take.</summary>
        public static StationSeat StationOf(World world, Settlement settlement, ShipCombat platform) =>
            new StationSeat(world, settlement, null, platform);

        /// <summary>One of the forts guarding <paramref name="gate"/>, as a station the player can take.</summary>
        public static StationSeat FortOf(World world, JumpGate gate, ShipCombat fort) => new StationSeat(world, null, gate, fort);

        /// <summary>The station a platform or fort in a fight is, or null for any other unit.</summary>
        public static StationSeat StationOf(World world, Battle battle, BattleUnit unit)
        {
            if (unit == null || unit.Kind != UnitKind.Platform || (unit.Platform == null && unit.Fort == null)) return null;
            return new StationSeat(world, unit.Platform, unit.Fort, unit.Combat, battle);
        }

        /// <summary>
        /// Every station at <paramref name="body"/> the player could take right now, in or out of a fight: each settlement's
        /// defence platforms there, and out at the system's root the forts of its jump gates.
        /// </summary>
        public static List<StationSeat> StationsAt(World world, int body)
        {
            var list = new List<StationSeat>();
            if (world == null) return list;
            foreach (var st in world.Economy.At(body))
                foreach (var p in st.Platforms)
                    Add(list, StationOf(world, st, p));
            if (body == world.Catalog.Root.Index)
                foreach (var g in world.Gates)
                    foreach (var f in g.Forts)
                        Add(list, FortOf(world, g, f));
            return list;
        }

        /// <summary>Every station in <paramref name="world"/> the player could take right now: every settlement's defence
        /// platforms, then every gate's forts.</summary>
        public static List<StationSeat> StationsIn(World world)
        {
            var list = new List<StationSeat>();
            if (world == null) return list;
            foreach (var st in world.Economy.Settlements)
                foreach (var p in st.Platforms)
                    Add(list, StationOf(world, st, p));
            foreach (var g in world.Gates)
                foreach (var f in g.Forts)
                    Add(list, FortOf(world, g, f));
            return list;
        }

        static void Add(List<StationSeat> list, StationSeat s)
        {
            if (s.CanEmbody) list.Add(s);
        }

        /// <summary>
        /// Every seat in <paramref name="battle"/> the player could take right now, in the battle's own order (U.SPC-T422):
        /// the helm of each ship still fighting, and the cockpit of each fighter and bomber aloft. Gun mounts, many to a
        /// hull, are reached from their hull (<see cref="TurretsOn(World, Battle, BattleUnit)"/>), not listed here.
        /// </summary>
        public static List<IEmbodiable> SeatsIn(World world, Battle battle)
        {
            var list = new List<IEmbodiable>();
            if (battle == null || battle.Over) return list;
            foreach (var u in battle.Units)
            {
                var seat = SeatOf(world, battle, u);
                if (seat != null && seat.CanEmbody) list.Add(seat);
            }
            return list;
        }

        /// <summary>
        /// The seat <paramref name="step"/> places on from <paramref name="current"/> in <paramref name="seats"/>, wrapping
        /// round (the first, or the last going back, when <paramref name="current"/> is not among them); null if there are none.
        /// </summary>
        public static IEmbodiable Cycle(IList<IEmbodiable> seats, IEmbodiable current, int step)
        {
            if (seats == null || seats.Count == 0) return null;
            int at = current == null ? -1 : seats.IndexOf(current);
            if (at < 0) return step >= 0 ? seats[0] : seats[seats.Count - 1];
            int n = seats.Count;
            return seats[((at + step) % n + n) % n];
        }

        /// <summary>
        /// The picker's list (U.SPC-T446): every seat the player could take now on the thing in hand and around the camera,
        /// each once, in this order: the seat of <paramref name="onUnit"/> (or the helm of <paramref name="onShip"/> when it
        /// is not fighting) and every gun mount on it, then every other seat in <paramref name="battle"/> (the fight in view),
        /// then the helm of each of <paramref name="nearby"/> (ships near the camera, nearest first) not already listed. Built
        /// only from the finders above, so a new kind registered there (a station, a satellite) shows up here too.
        /// </summary>
        public static List<IEmbodiable> Pickable(World world, Battle battle, BattleUnit onUnit = null, Ship onShip = null, IEnumerable<Ship> nearby = null)
        {
            var list = new List<IEmbodiable>();
            void Add(IEmbodiable e)
            {
                if (e != null && e.CanEmbody && !list.Contains(e)) list.Add(e);
            }

            if (onUnit == null && onShip?.Battle != null) onUnit = onShip.Battle.UnitOf(onShip);
            if (onUnit != null)
            {
                var unitBattle = onUnit.Ship?.Battle ?? battle;
                if (unitBattle != null && !unitBattle.Over)
                {
                    Add(SeatOf(world, unitBattle, onUnit));
                    foreach (var gun in TurretsOn(world, unitBattle, onUnit)) Add(gun);
                }
            }
            else if (onShip != null && world != null) Add(HelmOf(world, onShip));
            foreach (var s in SeatsIn(world, battle)) Add(s);
            if (nearby != null && world != null)
                foreach (var ship in nearby)
                    if (ship != null) Add(HelmOf(world, ship));
            return list;
        }

        /// <summary>The seat that flies <paramref name="unit"/> itself (a ship's helm, a fighter's cockpit), or null for a
        /// unit nobody flies (a defence platform, a fort: their guns are reached through <see cref="TurretsOn(World, Battle, BattleUnit)"/>,
        /// and the station itself from any of them, or through <see cref="StationOf(World, Battle, BattleUnit)"/>).</summary>
        public static IEmbodiable SeatOf(World world, Battle battle, BattleUnit unit)
        {
            if (unit == null) return null;
            if (unit.Kind == UnitKind.Fighter) return FighterOf(battle, unit);
            if (unit.Ship != null && world != null) return HelmOf(world, unit.Ship);
            return null;
        }
    }
}
