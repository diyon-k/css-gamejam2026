namespace Blink.Detection
{
    /// <summary>What a single eye-state source reports for one moment.</summary>
    public readonly struct EyeState
    {
        /// <summary>Whether a face is visible to the source at all.</summary>
        public readonly bool Present;

        /// <summary>Whether the eyes are closed.</summary>
        public readonly bool Closed;

        public EyeState(bool present, bool closed)
        {
            Present = present;
            Closed = closed;
        }

        public static readonly EyeState Open = new(true, false);
    }

    /// <summary>
    /// Anything that can report eye state: the webcam over UDP, the keyboard fallback, a test script.
    /// Add a source by putting a component that implements this next to <see cref="BlinkDetector"/>.
    /// </summary>
    public interface IEyeStateSource
    {
        /// <summary>False when the source has nothing to say (e.g. no UDP packets recently).</summary>
        bool IsActive { get; }

        EyeState Current { get; }

        /// <summary>Short label for debug overlays.</summary>
        string SourceName { get; }
    }
}
