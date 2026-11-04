using System;

namespace Unity.SpaceGod.Core
{
    /// <summary>Which side of a fight aboard a player commands: the party that came aboard, or the ship's own defenders.</summary>
    public enum BoardingRole
    {
        Boarders,
        Defenders,
    }

    /// <summary>
    /// A standing order to one side of a fight aboard (U.SPC-T468, docs/BOARDING.md). <see cref="Auto"/> is the
    /// resolver's own choice and the auto-resolve: boarders push for the bridge and engineering, defenders muster
    /// on the fighting. The other orders change only where people move; every exchange of fire in a compartment is
    /// still the resolver's weighted roll.
    /// </summary>
    public enum BoardingOrder
    {
        Auto,

        /// <summary>Boarders: push for the objective compartment, then press on as the resolver would.
        /// Defenders: muster on the objective (to hold it before the boarders get there, or to retake it).</summary>
        Push,

        /// <summary>Stand fast where they are: boarders dig in on what they hold (waiting for the next wave),
        /// defenders hold their posts instead of mustering.</summary>
        Hold,
    }

    /// <summary>
    /// The player's side of a fight aboard (U.SPC-T468): which fight a battle unit can command, and giving it orders.
    /// The fight itself stays <see cref="Interior.Step"/>; with no orders given it runs exactly as it always has.
    /// </summary>
    public static class BoardingCommand
    {
        /// <summary>
        /// The fight aboard <paramref name="unit"/> can command, and on which side: its own hull's, defending, while
        /// it is being boarded; otherwise the hull its marines are boarding (the one it is latched to first, else any
        /// in the battle its faction is boarding). Null when there is no such fight.
        /// </summary>
        public static (BattleUnit hull, BoardingRole role)? FightFor(Battle battle, BattleUnit unit)
        {
            if (battle == null || unit == null || !unit.Present || unit.Captured) return null;
            if (unit.Combat.Interior.UnderBoarding) return (unit, BoardingRole.Defenders);
            var latched = unit.Latched;
            // Its faction's boarding, alone or (U.SPC-T392) with allies: one party, under one set of orders.
            if (latched != null && latched.Present && latched.Combat.Interior.UnderBoarding && latched.Combat.Interior.IsParty(unit.Faction))
                return (latched, BoardingRole.Boarders);
            foreach (var o in battle.Units)
                if (o != unit && o.Present && o.Combat.Interior.UnderBoarding && o.Combat.Interior.IsParty(unit.Faction))
                    return (o, BoardingRole.Boarders);
            return null;
        }

        /// <summary>The order standing for one side of a fight aboard, and its objective compartment (-1 for none).</summary>
        public static (BoardingOrder order, int objective) OrdersOf(Interior interior, BoardingRole role) =>
            role == BoardingRole.Boarders ? (interior.BoarderOrder, interior.BoarderObjective) : (interior.DefenderOrder, interior.DefenderObjective);

        /// <summary>The next compartment after <paramref name="current"/> to pick as an objective, round the ship.</summary>
        public static int NextObjective(Interior interior, int current) =>
            interior.Compartments.Count == 0 ? -1 : (Math.Max(-1, current) + 1) % interior.Compartments.Count;

        /// <summary>Plain words for an order, as the boarding panel shows it.</summary>
        public static string Label(Interior interior, BoardingRole role)
        {
            var (order, objective) = OrdersOf(interior, role);
            switch (order)
            {
                case BoardingOrder.Push:
                    string where = objective >= 0 && objective < interior.Compartments.Count ? interior.Compartments[objective].Name : "?";
                    return (role == BoardingRole.Boarders ? "push for " : "muster on ") + where;
                case BoardingOrder.Hold:
                    return role == BoardingRole.Boarders ? "dig in where they stand" : "hold their posts";
                default:
                    return role == BoardingRole.Boarders ? "auto (for the bridge and engineering)" : "auto (muster on the fighting)";
            }
        }
    }
}
