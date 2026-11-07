using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Blink.Audio
{
    /// <summary>
    /// One sound the game can make: a set of interchangeable clips plus how to play them (volume and pitch
    /// ranges, 2D or 3D, distances, mixer group, repeat avoidance, cooldown). Create one with
    /// Assets &gt; Create &gt; BLINK &gt; Sound Cue, drop clips in, and assign it to an audio component.
    /// Played through <see cref="AudioService"/>; the cue itself never owns an AudioSource.
    /// </summary>
    [CreateAssetMenu(menuName = "BLINK/Sound Cue", fileName = "NewSoundCue")]
    public sealed class SoundCue : ScriptableObject
    {
        [Tooltip("Variants to choose from. Empty = the cue is silent (safe placeholder).")]
        [SerializeField] AudioClip[] clips = Array.Empty<AudioClip>();
        [SerializeField] ClipOrder order = ClipOrder.RandomNoRepeat;

        [Header("Level")]
        [Tooltip("Random volume between x and y for each play.")]
        [SerializeField] Vector2 volume = new(0.9f, 1f);
        [Tooltip("Random pitch between x and y for each play (0.95..1.05 = +/-5%). Never tied to Time.timeScale.")]
        [SerializeField] Vector2 pitch = new(0.97f, 1.03f);

        [Header("Space")]
        [Tooltip("0 = 2D (in your head), 1 = fully 3D. Only used by PlayAt / PlayAttached; Play is always 2D.")]
        [SerializeField, Range(0f, 1f)] float spatialBlend = 1f;
        [Tooltip("3D: full volume inside this distance (metres). Logarithmic rolloff beyond it.")]
        [SerializeField, Min(0.01f)] float minDistance = 1.5f;
        [Tooltip("3D: stops getting quieter past this distance (metres).")]
        [SerializeField, Min(0.1f)] float maxDistance = 30f;

        [Header("Routing")]
        [Tooltip("Mixer group (Player/Gun, World/Zombies, Stingers...). Decides what the eyes-shut muffle affects.")]
        [SerializeField] AudioMixerGroup output;
        [Tooltip("Lower plays first when voices run out (0 = most important, 256 = least). Same as AudioSource.priority.")]
        [SerializeField, Range(0, 256)] int priority = 128;

        [Header("Rate")]
        [Tooltip("Minimum real seconds between two plays of this cue anywhere in the game (unscaled). 0 = none.")]
        [SerializeField, Min(0f)] float cooldown;

        [NonSerialized] CuePicker _picker;
        [NonSerialized] CueCooldown _cooldown;

        public AudioClip[] Clips => clips;
        public ClipOrder Order => order;
        public Vector2 Volume => volume;
        public Vector2 Pitch => pitch;
        public float SpatialBlend => spatialBlend;
        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;
        public AudioMixerGroup Output => output;
        public int Priority => priority;
        public float Cooldown => cooldown;
        public bool HasClips => clips != null && Array.Exists(clips, c => c != null);

        void OnEnable()
        {
            _picker = new CuePicker();
            _cooldown = new CueCooldown();
        }

        /// <summary>
        /// Chooses the clip, volume and pitch for one play. Returns false if the cue has no clips or is still
        /// cooling down (the cooldown starts only when this returns true).
        /// </summary>
        public bool TryPick(out AudioClip clip, out float pickedVolume, out float pickedPitch)
        {
            clip = null;
            pickedVolume = pickedPitch = 1f;
            if (clips == null || clips.Length == 0) return false;
            _picker ??= new CuePicker();
            _cooldown ??= new CueCooldown();
            if (!_cooldown.IsReady(Time.unscaledTime, cooldown)) return false;

            int index = _picker.Next(clips.Length, order, UnityEngine.Random.value);
            clip = clips[index];
            if (clip == null)
            {
                // A missing slot: fall back to any assigned clip rather than going silent.
                clip = Array.Find(clips, c => c != null);
                if (clip == null) return false;
            }
            _cooldown.TryConsume(Time.unscaledTime, cooldown);
            pickedVolume = AudioMath.InRange(volume, UnityEngine.Random.value);
            pickedPitch = AudioMath.InRange(pitch, UnityEngine.Random.value);
            return true;
        }

        /// <summary>Copies the cue's routing and 3D settings onto a source (not the clip, volume or pitch).</summary>
        public void ApplySettings(AudioSource source, bool force2D)
        {
            source.outputAudioMixerGroup = output;
            source.priority = priority;
            source.spatialBlend = force2D ? 0f : spatialBlend;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = Mathf.Max(minDistance + 0.01f, maxDistance);
            source.dopplerLevel = 0f; // fast-forward and skips would make Doppler jumps audible
        }

        void OnValidate()
        {
            if (maxDistance < minDistance) maxDistance = minDistance;
        }
    }
}
