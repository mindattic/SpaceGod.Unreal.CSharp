using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The read-only view of the session's state that presentation reads (U.SPC-T410): who holds which seat. Everything in
    /// it is keyed by <see cref="PlayerId"/> or by a seat's id, never by "the player".
    /// </summary>
    public interface ISessionState
    {
        /// <summary>Fixed ticks stepped so far (<see cref="LocalSession.TickSeconds"/> each).</summary>
        long Tick { get; }

        /// <summary>The seed the session was started with: its rules draw on nothing unseeded.</summary>
        int Seed { get; }

        /// <summary>What <paramref name="player"/> holds now, or null.</summary>
        IEmbodiable SeatOf(PlayerId player);

        /// <summary>The id of what <paramref name="player"/> holds, or 0 for nothing.</summary>
        int SeatIdOf(PlayerId player);

        /// <summary>Who holds the seat with this id, or <see cref="PlayerId.None"/>.</summary>
        PlayerId HolderOf(int seat);

        /// <summary>The seat with this id, or null for one the session was never told of.</summary>
        IEmbodiable Seat(int seat);

        /// <summary>Why <paramref name="player"/>'s seat was last lost by itself, or null (<see cref="EmbodimentSession.EndedBecause"/>).</summary>
        string EndedBecause(PlayerId player);
    }

    /// <summary>
    /// The one seam between the game and whoever runs its rules (U.SPC-T410, per Unity.Multiplayer's multiplayer-ready
    /// standard): input submits commands here, presentation reads <see cref="State"/> and <see cref="EventRaised"/>, and
    /// <see cref="LocalPlayer"/> is the only place that says which player this machine plays. The single-player game runs
    /// on <see cref="LocalSession"/>; a networked session would replace it without touching input, rules or views.
    /// </summary>
    public interface ISession
    {
        /// <summary>The player this machine plays. Only input and presentation ask; the rules never do.</summary>
        PlayerId LocalPlayer { get; }

        IReadOnlyList<PlayerId> Players { get; }

        ISessionState State { get; }

        event Action<IGameEvent> EventRaised;

        /// <summary>The one command pipeline: the authority validates the command and applies it on the next tick.</summary>
        void Submit(IGameCommand command);

        /// <summary>
        /// The stable id the authority gives a seat: a counter, assigned the first time the seat is named and kept for as
        /// long as the session lives, so a command names a seat without holding a reference to it.
        /// </summary>
        int IdOf(IEmbodiable seat);
    }
}
