using UnityEngine;
using UnityEngine.Audio;

namespace Blink.Audio
{
    /// <summary>
    /// Identifies one sound started by <see cref="AudioService"/>, so it can be stopped or faded later.
    /// Goes stale by itself once the voice is reused, so holding on to an old handle is harmless.
    /// </summary>
    public readonly struct AudioHandle
    {
        internal readonly int Index;
        internal readonly int Generation;
        internal readonly bool Is3D;

        internal AudioHandle(int index, int generation, bool is3D)
        {
            Index = index;
            Generation = generation;
            Is3D = is3D;
        }

        /// <summary>False for a sound that never started (no service, no clips, cooldown, no free voice).</summary>
        public bool IsValid => Generation > 0;

        public static readonly AudioHandle None = default;
    }

    /// <summary>
    /// Plays every sound in the game from two small pools of AudioSources (2D and 3D), so nothing creates or
    /// destroys sources at runtime and there is no <c>PlayClipAtPoint</c>. Lives on GameSystems; reach it
    /// through <see cref="Instance"/>. Gameplay code never calls this: small audio components listen to
    /// gameplay events and call <see cref="Play"/>, <see cref="PlayAt"/>, <see cref="PlayAttached"/> or
    /// <see cref="PlayLoop"/> with a <see cref="SoundCue"/>.
    ///
    /// Pitch is never tied to Time.timeScale: fast-forward does not chipmunk the world. The eyes-shut muffle
    /// is done by the mixer's EyesShut snapshot instead (see TimeAudio). Fades run on unscaled time.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour
    {
        public static AudioService Instance { get; private set; }

        [Tooltip("Voices for in-your-head sounds (gun, heartbeat, stingers).")]
        [SerializeField, Min(1)] int voices2D = 8;
        [Tooltip("Voices for sounds placed in the world (zombies, impacts, doors).")]
        [SerializeField, Min(1)] int voices3D = 16;
        [Tooltip("Used when a cue has no mixer group of its own.")]
        [SerializeField] AudioMixerGroup defaultOutput;

        Voice[] _pool2D;
        Voice[] _pool3D;

        // Clears the singleton when entering play mode with domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        /// <summary>Voices currently playing (both pools). For debugging and tests.</summary>
        public int ActiveVoiceCount => CountActive(_pool2D) + CountActive(_pool3D);

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Audio] More than one AudioService in the scene; disabling this one.");
                enabled = false;
                return;
            }
            Instance = this;
            BuildPools();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            Tick(_pool2D, dt);
            Tick(_pool3D, dt);
        }

        /// <summary>A one-shot in the player's head (2D), whatever the cue's spatial blend.</summary>
        public AudioHandle Play(SoundCue cue, float volumeScale = 1f) =>
            StartVoice(cue, false, null, Vector3.zero, false, volumeScale);

        /// <summary>A one-shot at a fixed point in the world, using the cue's 3D settings.</summary>
        public AudioHandle PlayAt(SoundCue cue, Vector3 position, float volumeScale = 1f) =>
            StartVoice(cue, true, null, position, false, volumeScale);

        /// <summary>A one-shot that follows <paramref name="target"/> while it plays (stays put if it's destroyed).</summary>
        public AudioHandle PlayAttached(SoundCue cue, Transform target, float volumeScale = 1f) =>
            target == null ? AudioHandle.None : StartVoice(cue, true, target, target.position, false, volumeScale);

        /// <summary>
        /// A looping sound that plays until <see cref="Stop"/>. With a target it's 3D and follows it (and stops
        /// if the target is destroyed); without one it's 2D.
        /// </summary>
        public AudioHandle PlayLoop(SoundCue cue, Transform target = null, float volumeScale = 1f) =>
            StartVoice(cue, target != null, target, target != null ? target.position : Vector3.zero, true, volumeScale);

        public bool IsPlaying(AudioHandle handle) => Resolve(handle) != null;

        /// <summary>Stops a sound, optionally fading it out over real seconds. Stale handles are ignored.</summary>
        public void Stop(AudioHandle handle, float fadeSeconds = 0f)
        {
            var voice = Resolve(handle);
            if (voice == null) return;
            if (fadeSeconds <= 0f) Release(voice);
            else voice.BeginFade(fadeSeconds);
        }

        /// <summary>Scales a playing sound's volume (on top of the cue's own volume).</summary>
        public void SetVolume(AudioHandle handle, float volumeScale)
        {
            var voice = Resolve(handle);
            if (voice == null) return;
            voice.Scale = Mathf.Max(0f, volumeScale);
            voice.ApplyVolume();
        }

        /// <summary>The source behind a handle, for debugging and tests. Don't keep it: voices are reused.</summary>
        public AudioSource SourceOf(AudioHandle handle) => Resolve(handle)?.Source;

        AudioHandle StartVoice(SoundCue cue, bool is3D, Transform follow, Vector3 position, bool loop, float volumeScale)
        {
            if (cue == null || _pool2D == null) return AudioHandle.None;
            var pool = is3D ? _pool3D : _pool2D;
            int index = FindVoice(pool);
            if (index < 0) return AudioHandle.None;
            if (!cue.TryPick(out var clip, out float volume, out float pitch)) return AudioHandle.None;

            var voice = pool[index];
            if (voice.InUse) voice.Source.Stop(); // stealing the oldest one-shot
            var source = voice.Source;
            cue.ApplySettings(source, force2D: !is3D);
            if (source.outputAudioMixerGroup == null) source.outputAudioMixerGroup = defaultOutput;
            source.clip = clip;
            source.pitch = pitch;
            source.loop = loop;
            source.transform.position = position;

            voice.InUse = true;
            voice.Looping = loop;
            voice.Generation++;
            voice.Follow = follow;
            voice.HasFollow = follow != null;
            voice.BaseVolume = volume;
            voice.Scale = Mathf.Max(0f, volumeScale);
            voice.StartedAt = Time.unscaledTime;
            voice.ClearFade();
            voice.ApplyVolume();
            source.Play();
            return new AudioHandle(index, voice.Generation, is3D);
        }

        Voice Resolve(AudioHandle handle)
        {
            if (!handle.IsValid) return null;
            var pool = handle.Is3D ? _pool3D : _pool2D;
            if (pool == null || handle.Index < 0 || handle.Index >= pool.Length) return null;
            var voice = pool[handle.Index];
            return voice.InUse && voice.Generation == handle.Generation ? voice : null;
        }

        /// <summary>A free voice, else the oldest one-shot to steal, else -1 (everything is a loop).</summary>
        static int FindVoice(Voice[] pool)
        {
            int oldest = -1;
            for (int i = 0; i < pool.Length; i++)
            {
                var v = pool[i];
                if (!v.InUse) return i;
                if (!v.Looping && (oldest < 0 || v.StartedAt < pool[oldest].StartedAt)) oldest = i;
            }
            return oldest;
        }

        static void Tick(Voice[] pool, float dt)
        {
            if (pool == null) return;
            foreach (var voice in pool)
            {
                if (!voice.InUse) continue;
                if (voice.HasFollow)
                {
                    if (voice.Follow != null) voice.Source.transform.position = voice.Follow.position;
                    else if (voice.Looping)
                    {
                        Release(voice); // the thing making the noise is gone
                        continue;
                    }
                }
                if (voice.TickFade(dt))
                {
                    Release(voice);
                    continue;
                }
                if (!voice.Looping && !voice.Source.isPlaying) Release(voice);
            }
        }

        static void Release(Voice voice)
        {
            voice.Source.Stop();
            voice.Source.clip = null;
            voice.InUse = false;
            voice.Looping = false;
            voice.Follow = null;
            voice.HasFollow = false;
            voice.ClearFade();
        }

        static int CountActive(Voice[] pool)
        {
            if (pool == null) return 0;
            int n = 0;
            foreach (var v in pool) if (v.InUse) n++;
            return n;
        }

        void BuildPools()
        {
            _pool2D = BuildPool("Voices 2D", voices2D);
            _pool3D = BuildPool("Voices 3D", voices3D);
        }

        Voice[] BuildPool(string label, int count)
        {
            var root = new GameObject(label).transform;
            root.SetParent(transform, false);
            var pool = new Voice[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Voice " + i);
                go.transform.SetParent(root, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.outputAudioMixerGroup = defaultOutput;
                pool[i] = new Voice { Source = source };
            }
            return pool;
        }

        /// <summary>One pooled AudioSource and what it's currently doing.</summary>
        sealed class Voice
        {
            public AudioSource Source;
            public bool InUse;
            public bool Looping;
            public int Generation;
            public Transform Follow;
            public bool HasFollow;
            public float BaseVolume = 1f;
            public float Scale = 1f;
            public float StartedAt;

            float _fadeSeconds;
            float _fadeElapsed;

            float Fade => _fadeSeconds > 0f ? Mathf.Clamp01(1f - _fadeElapsed / _fadeSeconds) : 1f;

            public void ApplyVolume() => Source.volume = BaseVolume * Scale * Fade;

            public void BeginFade(float seconds)
            {
                if (_fadeSeconds > 0f) return; // already fading out
                _fadeSeconds = seconds;
                _fadeElapsed = 0f;
            }

            public void ClearFade() => _fadeSeconds = _fadeElapsed = 0f;

            /// <summary>Advances a fade-out; true once it has reached silence.</summary>
            public bool TickFade(float dt)
            {
                if (_fadeSeconds <= 0f) return false;
                _fadeElapsed += dt;
                ApplyVolume();
                return _fadeElapsed >= _fadeSeconds;
            }
        }
    }
}
