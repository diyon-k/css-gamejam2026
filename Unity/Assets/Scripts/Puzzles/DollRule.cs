namespace Blink.Puzzles
{
    public enum DollState { Dormant, Armed, Warned, Killing, Done }

    public enum DollEvent { None, Warned, Kill, Escaped, Disarmed }

    /// <summary>
    /// The rules of Lily's doll (docs/puzzles/LILYS_DOLL_ROOM.md), with no Unity calls so they're unit tested.
    /// <see cref="LilysDollRoom"/> does the geometry (in the room? watched?) and passes booleans in.
    ///
    /// Dormant (outside) → Armed (inside) → Warned (one blink or eyes shut) → Killing (look away, a second blink,
    /// or eyes held shut). Leaving without the key goes back to Dormant and clears the warning; leaving with it is
    /// Done for good.
    /// </summary>
    public sealed class DollRule
    {
        /// <summary>Real seconds the doll can go unwatched before it leaps.</summary>
        public float LookAwayGrace { get; set; }

        /// <summary>Eyes closed this long in total (one closure) kills, even without a warning.</summary>
        public float ShutKillSeconds { get; set; }

        public DollState State { get; private set; } = DollState.Dormant;

        bool Active => State == DollState.Armed || State == DollState.Warned;

        float _unwatched;
        float _lastShut;      // closed seconds at the previous EyesShut call (a smaller value = a new closure)

        public DollRule(float lookAwayGrace, float shutKillSeconds)
        {
            LookAwayGrace = lookAwayGrace;
            ShutKillSeconds = shutKillSeconds;
        }

        /// <summary>Every frame: enter/leave and the look-away timer.</summary>
        public DollEvent Tick(bool inRoom, bool hasKey, bool watched, float realDt)
        {
            if (State == DollState.Dormant)
            {
                if (!inRoom) return DollEvent.None;
                State = DollState.Armed;
                ClearTimers();
            }
            if (!Active) return DollEvent.None;

            if (!inRoom)
            {
                ClearTimers();
                if (hasKey)
                {
                    State = DollState.Done;
                    return DollEvent.Escaped;
                }
                State = DollState.Dormant; // the warning is forgiven
                return DollEvent.Disarmed;
            }

            if (watched)
            {
                _unwatched = 0f;
                return DollEvent.None;
            }
            _unwatched += realDt;
            return _unwatched >= LookAwayGrace ? Kill() : DollEvent.None;
        }

        /// <summary>A quick blink (the eyelids hide whatever happens).</summary>
        public DollEvent Blink()
        {
            if (State == DollState.Armed)
            {
                State = DollState.Warned;
                return DollEvent.Warned;
            }
            return State == DollState.Warned ? Kill() : DollEvent.None;
        }

        /// <summary>
        /// Called every frame while the eyes are held shut, with how long they've been closed. The first call of a
        /// closure counts as a blink; staying shut past <see cref="ShutKillSeconds"/> kills either way.
        /// </summary>
        public DollEvent EyesShut(float closedSeconds)
        {
            if (!Active) return DollEvent.None;
            bool newClosure = closedSeconds < _lastShut || _lastShut <= 0f;
            _lastShut = closedSeconds;

            if (closedSeconds >= ShutKillSeconds) return Kill();
            if (!newClosure) return DollEvent.None;
            if (State == DollState.Warned) return Kill();
            State = DollState.Warned;
            return DollEvent.Warned;
        }

        /// <summary>Back to Dormant with the warning cleared (after a death). Done stays Done.</summary>
        public void Reset()
        {
            if (State == DollState.Done) return;
            State = DollState.Dormant;
            ClearTimers();
        }

        DollEvent Kill()
        {
            State = DollState.Killing;
            ClearTimers();
            return DollEvent.Kill;
        }

        void ClearTimers()
        {
            _unwatched = 0f;
            _lastShut = 0f;
        }
    }
}
