namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Something a player wants done (U.SPC-T410, docs/BIBLE.md U.SPC-LAW-1): a small value of plain data and ids, stamped
    /// with the <see cref="PlayerId"/> that issued it and handed to <see cref="ISession.Submit"/>, the one command pipeline.
    /// Input code builds commands and never changes the session's state itself; <see cref="CommandCodec"/> writes each one
    /// as text and reads it back equal.
    /// </summary>
    public interface IGameCommand
    {
        PlayerId Player { get; }
    }

    /// <summary>Take charge of the seat with this id (<see cref="ISession.IdOf"/>), handing back whatever the player held.</summary>
    public readonly struct TakeSeatCommand : IGameCommand
    {
        public TakeSeatCommand(PlayerId player, int seat)
        {
            Player = player;
            Seat = seat;
        }

        public PlayerId Player { get; }
        public int Seat { get; }
    }

    /// <summary>Hand what the player holds back to its AI.</summary>
    public readonly struct ReleaseSeatCommand : IGameCommand
    {
        public ReleaseSeatCommand(PlayerId player) => Player = player;
        public PlayerId Player { get; }
    }

    /// <summary>Move up from a mounted seat to what it is mounted on (a gun to its station, <see cref="EmbodimentSession.MoveToHost"/>).</summary>
    public readonly struct MoveToHostCommand : IGameCommand
    {
        public MoveToHostCommand(PlayerId player) => Player = player;
        public PlayerId Player { get; }
    }

    /// <summary>
    /// The player's controls at the seat they hold (stick, throttle, triggers, already in world terms), held from this tick
    /// until the next one replaces it, as a stick stays where it is put.
    /// </summary>
    public readonly struct SeatControlCommand : IGameCommand
    {
        public SeatControlCommand(PlayerId player, EmbodimentInput input)
        {
            Player = player;
            Input = input;
        }

        public PlayerId Player { get; }
        public EmbodimentInput Input { get; }
    }
}
