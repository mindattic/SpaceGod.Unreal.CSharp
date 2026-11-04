using System;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A player, by seat (U.SPC-T410, per Unity.Multiplayer's multiplayer-ready standard, docs/BIBLE.md U.SPC-LAW-1): the
    /// only way the session's rules know who did something. Seat 0 is the first player; <see cref="None"/> is nobody.
    /// Which seat this machine plays is answered only by <see cref="ISession.LocalPlayer"/>, never by the rules.
    /// </summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public PlayerId(int seat) => Seat = seat;

        /// <summary>The seat index, 0 up; negative for <see cref="None"/>.</summary>
        public int Seat { get; }

        /// <summary>No player: what a command carries when nobody is behind it, and always refused.</summary>
        public static PlayerId None => new PlayerId(-1);

        public bool IsNone => Seat < 0;

        public bool Equals(PlayerId other) => Seat == other.Seat;
        public override bool Equals(object obj) => obj is PlayerId p && Equals(p);
        public override int GetHashCode() => Seat;
        public static bool operator ==(PlayerId a, PlayerId b) => a.Seat == b.Seat;
        public static bool operator !=(PlayerId a, PlayerId b) => a.Seat != b.Seat;
        public override string ToString() => IsNone ? "nobody" : "player " + (Seat + 1).ToString(CultureInfo.InvariantCulture);
    }
}
