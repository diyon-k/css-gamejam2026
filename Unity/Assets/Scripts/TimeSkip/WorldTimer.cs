using System;
using UnityEngine;

namespace Blink.TimeSkip
{
    /// <summary>
    /// A countdown that runs in world time and also jumps during skips. Use it for anything the
    /// player can speed up by blinking: reloads, healing, forcing a door, charging the generator.
    ///
    /// Usage: call <see cref="Advance"/> with <c>Time.deltaTime</c> in Update and with the skipped
    /// seconds in <see cref="ISkippable.Skip"/>.
    /// </summary>
    [Serializable]
    public sealed class WorldTimer
    {
        [SerializeField, Min(0f)] float duration = 1f;

        float _elapsed;
        bool _running;

        public event Action Completed;

        public WorldTimer() { }
        public WorldTimer(float duration) { this.duration = duration; }

        public float Duration
        {
            get => duration;
            set => duration = Mathf.Max(0f, value);
        }

        public bool IsRunning => _running;
        public bool IsComplete => !_running && _elapsed >= duration && duration > 0f;
        public float Elapsed => _elapsed;
        public float Remaining => Mathf.Max(0f, duration - _elapsed);
        public float Progress01 => duration <= 0f ? 1f : Mathf.Clamp01(_elapsed / duration);

        /// <summary>Starts from zero.</summary>
        public void Start()
        {
            _elapsed = 0f;
            _running = true;
        }

        /// <summary>Pauses without resetting progress (e.g. player stopped forcing the door).</summary>
        public void Pause() => _running = false;

        /// <summary>Continues from where it was paused.</summary>
        public void Resume()
        {
            if (_elapsed < duration) _running = true;
        }

        public void Cancel()
        {
            _running = false;
            _elapsed = 0f;
        }

        /// <summary>Advances the timer. Returns true on the call that completes it.</summary>
        public bool Advance(float seconds)
        {
            if (!_running || seconds <= 0f) return false;
            _elapsed += seconds;
            if (_elapsed < duration) return false;

            _elapsed = duration;
            _running = false;
            Completed?.Invoke();
            return true;
        }
    }
}
