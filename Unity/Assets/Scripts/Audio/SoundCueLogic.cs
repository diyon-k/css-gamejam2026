using UnityEngine;

namespace Blink.Audio
{
    /// <summary>How a <see cref="SoundCue"/> chooses between its clips.</summary>
    public enum ClipOrder
    {
        /// <summary>Any clip, repeats allowed.</summary>
        Random,
        /// <summary>Any clip except the one played last time (when there are at least two).</summary>
        RandomNoRepeat,
        /// <summary>Clips in order, wrapping around (round-robin).</summary>
        RoundRobin,
    }

    /// <summary>
    /// Picks which clip of a cue plays next. Plain class (no Unity objects) so it can be unit tested: pass a
    /// random number in [0, 1) instead of calling <c>Random.value</c> inside.
    /// </summary>
    public sealed class CuePicker
    {
        int _last = -1;

        /// <summary>Index of the clip played most recently, or -1.</summary>
        public int Last => _last;

        /// <summary>Returns the next clip index in [0, count), or -1 if there are no clips.</summary>
        public int Next(int count, ClipOrder order, float random01)
        {
            if (count <= 0) return -1;
            if (count == 1) return _last = 0;
            float r = Mathf.Clamp(random01, 0f, 0.99999f);
            int index;
            switch (order)
            {
                case ClipOrder.RoundRobin:
                    index = (_last + 1) % count;
                    break;
                case ClipOrder.RandomNoRepeat:
                    if (_last < 0 || _last >= count)
                    {
                        index = (int)(r * count);
                        break;
                    }
                    // Pick among the other count - 1 clips, then skip over the last one.
                    index = (int)(r * (count - 1));
                    if (index >= _last) index++;
                    break;
                default:
                    index = (int)(r * count);
                    break;
            }
            return _last = index;
        }

        public void Reset() => _last = -1;
    }

    /// <summary>
    /// A minimum gap between plays, measured on whatever clock you pass in (the audio code passes
    /// <c>Time.unscaledTime</c>, so fast-forward doesn't make sounds repeat 3x as often). A clock that goes
    /// backwards (a new play session with domain reload off) counts as ready rather than blocking forever.
    /// </summary>
    public sealed class CueCooldown
    {
        float _lastTime = float.NegativeInfinity;

        public bool IsReady(float now, float cooldown) =>
            cooldown <= 0f || now < _lastTime || now - _lastTime >= cooldown;

        /// <summary>True (and starts the cooldown) if ready; false if still cooling down.</summary>
        public bool TryConsume(float now, float cooldown)
        {
            if (!IsReady(now, cooldown)) return false;
            _lastTime = now;
            return true;
        }

        public void Reset() => _lastTime = float.NegativeInfinity;
    }

    /// <summary>Small helpers shared by the audio scripts.</summary>
    public static class AudioMath
    {
        /// <summary>A value between range.x and range.y (in either order) at <paramref name="t01"/>.</summary>
        public static float InRange(Vector2 range, float t01) => Mathf.Lerp(range.x, range.y, Mathf.Clamp01(t01));

        /// <summary>Decibels to a linear gain (0 dB = 1, -6 dB is about 0.5).</summary>
        public static float DbToLinear(float db) => Mathf.Pow(10f, db / 20f);
    }
}
