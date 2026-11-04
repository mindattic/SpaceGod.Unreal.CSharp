using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The session the game ships with (U.SPC-T410, docs/BIBLE.md U.SPC-LAW-1): the <see cref="SeatAuthority"/> in process,
    /// every submitted command queued and applied in order at the start of the next step, and the authority's events
    /// raised straight to presentation. Hot-seat or split-screen play is the same session with more than one player; a
    /// networked session would take its place behind <see cref="ISession"/>.
    ///
    /// <see cref="Step(int)"/> advances on the fixed tick (<see cref="TickSeconds"/>), which is how tests and replays drive
    /// it. The single-player game still runs its flight on frame time scaled by the warp, as it always has, through
    /// <see cref="Advance"/>; that frame-time step is the one rule-4 gap left, and the BIBLE law says so.
    /// </summary>
    public sealed class LocalSession : ISession
    {
        /// <summary>The fixed tick rate the standard defaults to.</summary>
        public const int TicksPerSecond = 30;

        public const double TickSeconds = 1.0 / TicksPerSecond;

        readonly SeatAuthority authority;
        readonly Queue<IGameCommand> pending = new Queue<IGameCommand>();
        readonly List<PlayerId> players;

        LocalSession(int seed, List<PlayerId> players, PlayerId local)
        {
            this.players = players;
            LocalPlayer = local;
            authority = new SeatAuthority(seed, players);
        }

        /// <summary>A session of <paramref name="players"/> players (seats 0 up), this machine playing <paramref name="local"/>.</summary>
        public static LocalSession Create(int seed, int players = 1, int local = 0)
        {
            if (players < 1) throw new ArgumentOutOfRangeException(nameof(players));
            if (local < 0 || local >= players) throw new ArgumentOutOfRangeException(nameof(local));
            var ids = new List<PlayerId>(players);
            for (int i = 0; i < players; i++) ids.Add(new PlayerId(i));
            return new LocalSession(seed, ids, ids[local]);
        }

        public PlayerId LocalPlayer { get; }
        public IReadOnlyList<PlayerId> Players => players;
        public ISessionState State => authority;
        public event Action<IGameEvent> EventRaised;

        /// <summary>Every command applied so far, in order: with the seed, a replay of the session (see <see cref="CommandCodec"/>).</summary>
        public List<IGameCommand> Log { get; } = new List<IGameCommand>();

        /// <summary>Whether to keep <see cref="Log"/>; the shipped game leaves it off so a long session does not grow it.</summary>
        public bool Logging { get; set; }

        public void Submit(IGameCommand command)
        {
            if (command != null) pending.Enqueue(command);
        }

        public int IdOf(IEmbodiable seat) => authority.IdOf(seat);

        /// <summary>
        /// Applies every queued command now, in the order submitted, without advancing anything: what the next step would
        /// do first. The single-player game calls it so a key takes effect in the frame it is pressed.
        /// </summary>
        public void ApplyPending()
        {
            while (pending.Count > 0)
            {
                var c = pending.Dequeue();
                if (Logging) Log.Add(c);
                authority.Apply(c, Raise);
            }
        }

        /// <summary><paramref name="ticks"/> fixed ticks: the queued commands, then each player's controls for one tick each.</summary>
        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                ApplyPending();
                authority.Advance(TickSeconds, Raise, countTick: true);
            }
        }

        /// <summary>
        /// One step of <paramref name="dt"/> seconds of sim time: the queued commands, then each player's controls. The
        /// shipped game's frame-driven flight (see the class remarks); it does not count a tick.
        /// </summary>
        public void Advance(double dt)
        {
            ApplyPending();
            authority.Advance(dt, Raise, countTick: false);
        }

        void Raise(IGameEvent e) => EventRaised?.Invoke(e);
    }
}
