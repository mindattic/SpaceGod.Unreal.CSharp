namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// What the session's authority tells presentation happened (U.SPC-T410): presentation reads state and listens to
    /// these, and never writes state itself. Each names the player it concerns.
    /// </summary>
    public interface IGameEvent
    {
        PlayerId Player { get; }
    }

    /// <summary>The player now holds the seat with this id.</summary>
    public readonly struct SeatTaken : IGameEvent
    {
        public SeatTaken(PlayerId player, int seat)
        {
            Player = player;
            Seat = seat;
        }

        public PlayerId Player { get; }
        public int Seat { get; }
    }

    /// <summary>
    /// The player no longer holds the seat with this id: let go or switched (<see cref="Reason"/> null), or lost by itself
    /// (shot down, the fight over), when <see cref="Reason"/> says why.
    /// </summary>
    public readonly struct SeatReleased : IGameEvent
    {
        public SeatReleased(PlayerId player, int seat, string reason)
        {
            Player = player;
            Seat = seat;
            Reason = reason;
        }

        public PlayerId Player { get; }
        public int Seat { get; }
        public string Reason { get; }
    }

    /// <summary>A command the authority would not carry out, and why (a player not in the session, a seat held by another).</summary>
    public readonly struct CommandRefused : IGameEvent
    {
        public CommandRefused(IGameCommand command, string reason)
        {
            Command = command;
            Reason = reason;
        }

        public PlayerId Player => Command?.Player ?? PlayerId.None;
        public IGameCommand Command { get; }
        public string Reason { get; }
    }
}
