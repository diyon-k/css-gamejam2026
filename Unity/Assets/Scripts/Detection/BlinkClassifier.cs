namespace Blink.Detection
{
    public enum BlinkEventType
    {
        None,

        /// <summary>Eyes closed briefly and reopened. Fired on reopen.</summary>
        Blink,

        /// <summary>Eyes have stayed closed longer than the hold threshold.</summary>
        ShutBegan,

        /// <summary>Eyes reopened after a <see cref="ShutBegan"/>.</summary>
        ShutEnded,
    }

    public readonly struct BlinkEvent
    {
        public readonly BlinkEventType Type;

        /// <summary>How long the eyes were closed (for Blink and ShutEnded), in seconds.</summary>
        public readonly float ClosedDuration;

        public BlinkEvent(BlinkEventType type, float closedDuration)
        {
            Type = type;
            ClosedDuration = closedDuration;
        }

        public static readonly BlinkEvent None = new(BlinkEventType.None, 0f);
    }

    /// <summary>
    /// Turns a stream of "eyes closed?" samples into blink events. Pure logic, no Unity calls,
    /// so it's unit tested. Feed it unscaled time so fast-forward doesn't distort it.
    /// </summary>
    public sealed class BlinkClassifier
    {
        enum Phase { Open, Closing, Shut }

        /// <summary>Closed for at least this long counts as "eyes shut" rather than a blink.</summary>
        public float HoldThreshold { get; set; }

        /// <summary>Closures shorter than this are treated as detection noise and ignored.</summary>
        public float MinClosedTime { get; set; }

        Phase _phase = Phase.Open;
        float _closedAt;

        public BlinkClassifier(float holdThreshold = 0.2f, float minClosedTime = 0.03f)
        {
            HoldThreshold = holdThreshold;
            MinClosedTime = minClosedTime;
        }

        public bool IsClosed => _phase != Phase.Open;
        public bool IsShut => _phase == Phase.Shut;

        /// <summary>Seconds the eyes have been closed so far (0 when open).</summary>
        public float ClosedDuration(float now) => IsClosed ? now - _closedAt : 0f;

        public BlinkEvent Update(bool closed, float now)
        {
            switch (_phase)
            {
                case Phase.Open when closed:
                    _phase = Phase.Closing;
                    _closedAt = now;
                    return BlinkEvent.None;

                case Phase.Closing when closed && now - _closedAt >= HoldThreshold:
                    _phase = Phase.Shut;
                    return new BlinkEvent(BlinkEventType.ShutBegan, now - _closedAt);

                case Phase.Closing when !closed:
                {
                    _phase = Phase.Open;
                    float duration = now - _closedAt;
                    return duration >= MinClosedTime
                        ? new BlinkEvent(BlinkEventType.Blink, duration)
                        : BlinkEvent.None;
                }

                case Phase.Shut when !closed:
                    _phase = Phase.Open;
                    return new BlinkEvent(BlinkEventType.ShutEnded, now - _closedAt);

                default:
                    return BlinkEvent.None;
            }
        }

        /// <summary>
        /// The face was lost (the player looked away) while the eyes were closed: they now count as open. A
        /// closure still under the hold threshold is dropped without a <see cref="BlinkEventType.Blink"/>, since
        /// nobody saw the eyes reopen (and a head turning away often reads as an eye closing), so it can't skip
        /// time. Eyes already shut still report <see cref="BlinkEventType.ShutEnded"/>, so fast-forward stops.
        /// </summary>
        public BlinkEvent LoseFace(float now)
        {
            switch (_phase)
            {
                case Phase.Closing:
                    _phase = Phase.Open;
                    return BlinkEvent.None;
                case Phase.Shut:
                    _phase = Phase.Open;
                    return new BlinkEvent(BlinkEventType.ShutEnded, now - _closedAt);
                default:
                    return BlinkEvent.None;
            }
        }

        public void Reset() => _phase = Phase.Open;
    }
}
