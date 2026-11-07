using System;

namespace Blink.TimeSkip
{
    /// <summary>
    /// A screen transition that hides a blink skip, e.g. eyelids closing. Set it as
    /// <see cref="TimeSkipController.Transition"/> and every blink calls <see cref="Play"/> instead of
    /// skipping straight away: the world jumps when <paramref name="onCovered"/> runs, so the player
    /// sees the zombies closer when the transition clears. Without one, blinks skip instantly.
    /// </summary>
    public interface IBlinkTransition
    {
        /// <summary>
        /// Start (or extend) the transition and call <paramref name="onCovered"/> exactly once, as soon
        /// as the screen is fully covered (immediately if it already is). Must still call it if the
        /// transition is disabled part-way, so no skip is lost.
        /// </summary>
        void Play(Action onCovered);
    }
}
