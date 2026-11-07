using UnityEngine;

namespace Blink.Audio
{
    /// <summary>
    /// Background music: one looping 2D bed that starts with the scene, fades in, and plays for the whole game,
    /// looping when it reaches the end. Route its cue to the Ambience group so the eyes-shut muffle ducks it
    /// under the heartbeat. Runs on real time (the music ignores fast-forward and skips). Lives on GameSystems.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MusicAudio : MonoBehaviour
    {
        [Tooltip("Looping 2D music bed. Give it a fixed pitch (1..1) so it never plays detuned.")]
        [SerializeField] SoundCue musicCue;
        [Tooltip("Real seconds to fade the music in when it starts.")]
        [SerializeField, Min(0f)] float fadeInSeconds = 4f;

        AudioHandle _handle;
        float _fade;

        /// <summary>True while the music voice is playing. For debugging and tests.</summary>
        public bool IsPlaying => AudioService.Instance != null && AudioService.Instance.IsPlaying(_handle);

        void OnDisable()
        {
            if (AudioService.Instance != null) AudioService.Instance.Stop(_handle, 0.5f);
            _handle = AudioHandle.None;
        }

        void Update()
        {
            var audio = AudioService.Instance;
            if (musicCue == null || audio == null) return;

            if (!audio.IsPlaying(_handle))
            {
                // First start, or the voice was lost: (re)start from silence and fade in.
                _fade = fadeInSeconds > 0f ? 0f : 1f;
                _handle = audio.PlayLoop(musicCue, null, _fade);
                return;
            }

            if (_fade >= 1f) return;
            _fade = Mathf.MoveTowards(_fade, 1f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeInSeconds));
            audio.SetVolume(_handle, _fade);
        }
    }
}
