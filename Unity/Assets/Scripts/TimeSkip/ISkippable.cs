namespace Blink.TimeSkip
{
    /// <summary>
    /// Something that lives in world time and must jump forward when the player blinks.
    /// Implement it, then call <see cref="TimeSkipController.Register"/> in OnEnable and
    /// <see cref="TimeSkipController.Unregister"/> in OnDisable.
    ///
    /// Rule of thumb: whatever your Update does with <c>Time.deltaTime</c>, Skip should do with
    /// <paramref name="seconds"/> in one go (move along a path, advance a timer, etc.).
    /// Eyes-shut fast-forward does NOT call Skip; it just raises Time.timeScale, so normal
    /// Update code with Time.deltaTime already handles it.
    /// </summary>
    public interface ISkippable
    {
        void Skip(float seconds);
    }
}
