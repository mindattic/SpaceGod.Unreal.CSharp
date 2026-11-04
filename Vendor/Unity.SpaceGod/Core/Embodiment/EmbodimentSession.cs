namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The one thing the player is embodying, if any (U.SPC-T18). Switching seats is always a hand-back
    /// followed by a take-charge, and a thing that can no longer be embodied (shot away, captured) is handed
    /// back by itself. Works only through <see cref="IEmbodiable"/>: a new kind of controllable thing plugs in
    /// without touching this.
    /// </summary>
    public sealed class EmbodimentSession
    {
        /// <summary>What the player holds now, or null when only watching.</summary>
        public IEmbodiable Current { get; private set; }

        public bool Active => Current != null;

        /// <summary>Why the session last ended by itself (the thing held was shot down, the fight ended: its
        /// <see cref="IEmbodiable.Blocker"/>), or null if the player let go or switched (U.SPC-T422).</summary>
        public string EndedBecause { get; private set; }

        /// <summary>
        /// Hands back whatever is held and takes <paramref name="next"/>. Returns false (holding nothing new,
        /// and with the old thing still handed back) if <paramref name="next"/> cannot be embodied right now.
        /// </summary>
        public bool Switch(IEmbodiable next)
        {
            if (next != null && Equals(next, Current) && Current.Embodied) return true;
            Release();
            EndedBecause = null;
            if (next == null || !next.CanEmbody) return false;
            next.TakeCharge();
            Current = next;
            return true;
        }

        /// <summary>Hands what is held back to its AI.</summary>
        public void Release()
        {
            var was = Current;
            Current = null;
            if (was != null && was.Embodied) was.HandBack();
        }

        /// <summary>
        /// Moves up from a mounted thing to what it is mounted on (a turret to its hull): true if there was
        /// a host to take.
        /// </summary>
        public bool MoveToHost() => Current?.Host != null && Switch(Current.Host);

        /// <summary>
        /// One frame: the player's input goes to what is held, unless it can no longer be embodied, when it is
        /// handed back and the session ends. Returns false once nothing is held.
        /// </summary>
        public bool Step(in EmbodimentInput input, double dt)
        {
            if (Current == null) return false;
            if (!Current.CanEmbody)
            {
                EndedBecause = Current.Blocker;
                Release();
                return false;
            }
            Current.Control(input, dt);
            return true;
        }
    }
}
