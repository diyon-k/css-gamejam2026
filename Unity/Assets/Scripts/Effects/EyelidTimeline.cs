namespace Blink.Effects
{
    /// <summary>
    /// Timing for one eyelid blink: close, stay shut, open. Pure logic (no Unity calls) so it's unit
    /// tested; <see cref="BlinkEyelids"/> feeds it unscaled time and draws <see cref="Closed01"/>.
    /// Triggering again while it's running keeps the eyes shut longer instead of restarting the blink.
    /// </summary>
    public sealed class EyelidTimeline
    {
        public enum Phase { Open, Closing, Shut, Opening }

        public float CloseSeconds { get; set; }
        public float ShutSeconds { get; set; }
        public float OpenSeconds { get; set; }

        public Phase Current { get; private set; } = Phase.Open;

        /// <summary>Linear 0 (open) to 1 (fully covered).</summary>
        public float Linear01 { get; private set; }

        /// <summary>Eased 0..1 for drawing. Same curve both ways, so reversing mid-blink doesn't jump.</summary>
        public float Closed01 => Linear01 * Linear01 * (3f - 2f * Linear01);

        public bool IsCovered => Current == Phase.Shut;

        float _shutLeft;

        public EyelidTimeline(float closeSeconds = 0.07f, float shutSeconds = 0f, float openSeconds = 0.1f)
        {
            CloseSeconds = closeSeconds;
            ShutSeconds = shutSeconds;
            OpenSeconds = openSeconds;
        }

        /// <summary>Starts a blink, or extends the current one. Returns true if the screen is covered now.</summary>
        public bool Trigger()
        {
            switch (Current)
            {
                case Phase.Shut:
                    _shutLeft = ShutSeconds;
                    return true;
                case Phase.Open:
                case Phase.Opening:
                    Current = Phase.Closing;
                    break;
            }
            // Instant close: covered on the spot.
            return CloseSeconds <= 0f && BecomeShut();
        }

        /// <summary>Advances by <paramref name="seconds"/>. Returns true on the call that covers the screen.</summary>
        public bool Tick(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            switch (Current)
            {
                case Phase.Closing:
                    Linear01 = CloseSeconds <= 0f ? 1f : Linear01 + seconds / CloseSeconds;
                    if (Linear01 >= 1f) return BecomeShut();
                    return false;

                case Phase.Shut:
                    _shutLeft -= seconds;
                    if (_shutLeft <= 0f) Current = Phase.Opening;
                    return false;

                case Phase.Opening:
                    Linear01 = OpenSeconds <= 0f ? 0f : Linear01 - seconds / OpenSeconds;
                    if (Linear01 <= 0f)
                    {
                        Linear01 = 0f;
                        Current = Phase.Open;
                    }
                    return false;

                default:
                    return false;
            }
        }

        bool BecomeShut()
        {
            Linear01 = 1f;
            Current = Phase.Shut;
            _shutLeft = ShutSeconds;
            return true;
        }
    }
}
