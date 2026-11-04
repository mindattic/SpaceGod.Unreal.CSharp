using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The one authority over who holds which seat (U.SPC-T410, docs/BIBLE.md U.SPC-LAW-1): each player's own
    /// <see cref="EmbodimentSession"/>, the controls each last submitted, and the stable id of every seat named to it. Only
    /// it writes that state, and only in response to commands and ticks; it references no view, and raises events for
    /// presentation instead. Every player is just an id here: nothing asks which one this machine plays.
    /// </summary>
    public sealed class SeatAuthority : ISessionState
    {
        readonly List<PlayerId> players;
        readonly Dictionary<PlayerId, EmbodimentSession> seats = new Dictionary<PlayerId, EmbodimentSession>();
        readonly Dictionary<PlayerId, EmbodimentInput> controls = new Dictionary<PlayerId, EmbodimentInput>();
        readonly Dictionary<IEmbodiable, int> idOf = new Dictionary<IEmbodiable, int>();
        readonly Dictionary<int, IEmbodiable> byId = new Dictionary<int, IEmbodiable>();
        int nextId = 1;

        public SeatAuthority(int seed, IReadOnlyList<PlayerId> players)
        {
            Seed = seed;
            this.players = new List<PlayerId>(players);
            foreach (var p in this.players) seats[p] = new EmbodimentSession();
        }

        public long Tick { get; private set; }
        public int Seed { get; }
        public IReadOnlyList<PlayerId> Players => players;

        public bool Has(PlayerId player) => !player.IsNone && seats.ContainsKey(player);

        public IEmbodiable SeatOf(PlayerId player) => seats.TryGetValue(player, out var s) ? s.Current : null;

        public int SeatIdOf(PlayerId player)
        {
            var held = SeatOf(player);
            return held != null && idOf.TryGetValue(held, out int id) ? id : 0;
        }

        public PlayerId HolderOf(int seat)
        {
            if (!byId.TryGetValue(seat, out var s)) return PlayerId.None;
            foreach (var p in players)
                if (Equals(seats[p].Current, s)) return p;
            return PlayerId.None;
        }

        public IEmbodiable Seat(int seat) => byId.TryGetValue(seat, out var s) ? s : null;

        public string EndedBecause(PlayerId player) => seats.TryGetValue(player, out var s) ? s.EndedBecause : null;

        /// <summary>The seat's id, given the first time it is named (seats compare by what they control, so a fresh wrapper
        /// round the same gun gets the same id).</summary>
        public int IdOf(IEmbodiable seat)
        {
            if (seat == null) return 0;
            if (idOf.TryGetValue(seat, out int id)) return id;
            id = nextId++;
            idOf[seat] = id;
            byId[id] = seat;
            return id;
        }

        /// <summary>Validates <paramref name="command"/> and carries it out, raising what came of it.</summary>
        public void Apply(IGameCommand command, Action<IGameEvent> raise)
        {
            if (command == null) return;
            var p = command.Player;
            if (!Has(p))
            {
                raise(new CommandRefused(command, p.IsNone ? "no player issued it" : p + " is not in this session"));
                return;
            }
            var session = seats[p];
            switch (command)
            {
                case TakeSeatCommand take:
                    var next = Seat(take.Seat);
                    if (next == null)
                    {
                        raise(new CommandRefused(command, "no seat " + take.Seat));
                        return;
                    }
                    Take(p, session, next, command, raise);
                    return;
                case MoveToHostCommand _:
                    var host = session.Current?.Host;
                    if (host == null)
                    {
                        raise(new CommandRefused(command, "nothing to move up to"));
                        return;
                    }
                    Take(p, session, host, command, raise);
                    return;
                case ReleaseSeatCommand _:
                    int was = SeatIdOf(p);
                    session.Release();
                    controls.Remove(p);
                    if (was != 0) raise(new SeatReleased(p, was, null));
                    return;
                case SeatControlCommand control:
                    if (session.Current == null)
                    {
                        raise(new CommandRefused(command, p + " holds no seat"));
                        return;
                    }
                    controls[p] = control.Input;
                    return;
                default:
                    raise(new CommandRefused(command, "unknown command " + command.GetType().Name));
                    return;
            }
        }

        /// <summary>
        /// <paramref name="p"/> takes <paramref name="next"/> unless another player holds it. As
        /// <see cref="EmbodimentSession.Switch"/>, a seat that cannot be embodied leaves the player holding nothing.
        /// </summary>
        void Take(PlayerId p, EmbodimentSession session, IEmbodiable next, IGameCommand command, Action<IGameEvent> raise)
        {
            int id = IdOf(next);
            var holder = HolderOf(id);
            if (!holder.IsNone && holder != p)
            {
                raise(new CommandRefused(command, next.Name + " is held by " + holder));
                return;
            }
            int was = SeatIdOf(p);
            bool took = session.Switch(next);
            // Retaking the seat already held can fail too (it can no longer be embodied): it is let go all the same.
            if (was != 0 && (was != id || !took)) raise(new SeatReleased(p, was, null));
            if (!took)
            {
                controls.Remove(p);
                raise(new CommandRefused(command, "can't take " + next.Name + ": " + (next.Blocker ?? "out of reach")));
                return;
            }
            if (was != id)
            {
                controls.Remove(p);
                raise(new SeatTaken(p, id));
            }
        }

        /// <summary>
        /// One step of <paramref name="dt"/> seconds: each player's held controls go to their seat, in seat order, and a
        /// seat that can no longer be embodied is handed back and reported lost.
        /// </summary>
        public void Advance(double dt, Action<IGameEvent> raise, bool countTick)
        {
            foreach (var p in players)
            {
                var session = seats[p];
                if (session.Current == null) continue;
                int was = SeatIdOf(p);
                controls.TryGetValue(p, out var input);
                if (!session.Step(input, dt))
                {
                    controls.Remove(p);
                    raise(new SeatReleased(p, was, session.EndedBecause ?? "lost"));
                }
            }
            if (countTick) Tick++;
        }
    }
}
