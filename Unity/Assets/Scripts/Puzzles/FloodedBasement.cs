using System;
using System.Collections;
using Blink.Audio;
using Blink.Combat;
using Blink.Detection;
using Blink.Effects;
using Blink.Interaction;
using Blink.Player;
using Blink.TimeSkip;
using Blink.Visuals;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;

namespace Blink.Puzzles
{
    /// <summary>
    /// P6, the flooded basement (docs/puzzles/FLOODED_BASEMENT.md). On the puzzle root, whose trigger
    /// <see cref="BoxCollider"/> is the room. Walk in past <see cref="slamDepth"/> and the bath tap starts running; the
    /// water rises slowly, and jumps with every blink (hidden behind the eyelids) and every half second the eyes are held
    /// shut. Search the cupboards and drawers for the crowbar (a different one each attempt), lever the cover off the
    /// drain and the water spirals away (blinks now help), revealing the workshop key. Water at eye height drowns you.
    /// <para>
    /// The rules live in <see cref="FloodLevel"/>; this does the room: the trigger, the crowbar, the water plane and its
    /// surge, the tap, the whirlpool, the sound, the drowning and the reset on respawn.
    /// </para>
    /// <para>
    /// <b>The door is not handled here.</b> Hook it to <see cref="FloodStarted"/> / <see cref="onFloodStarted"/> (slam and
    /// lock), <see cref="Solved"/> / <see cref="onSolved"/> (unlock, swing open) and <see cref="WasReset"/> /
    /// <see cref="onReset"/> (open and unlocked again after a drowning).
    /// </para>
    /// Placed by BLINK > Place Puzzle: Flooded Basement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FloodedBasement : MonoBehaviour, ISkippable
    {
        public const string CrowbarId = "crowbar";
        const float MaxRealStep = 0.1f;

        [Header("Parts")]
        [Tooltip("Trigger box sized to the room interior, full height. Its bottom is the floor.")]
        public BoxCollider roomBounds;
        [Tooltip("At the doorway, on the floor, facing into the room.")]
        public Transform doorway;
        [Tooltip("Where the crowbar can be hidden: one per spot.")]
        public Openable[] searchSpots;
        [Tooltip("Where the crowbar lies in each spot (a child of the drawer, so it slides out with it). Same order as Search Spots.")]
        public Transform[] searchAnchors;
        public Pickup crowbar;
        public DrainCover drain;
        [Tooltip("key_workshop, in the drain's grate basket under the cover. Can be picked up once the water's gone.")]
        public Pickup rewardKey;
        [Tooltip("The water surface (two-sided, no collider). Its y follows the level.")]
        public Transform water;

        [Header("Effects (all optional)")]
        [Tooltip("The bath tap's mouth: drips and running-water sounds come from here.")]
        public Transform tap;
        [Tooltip("The running stream and the sheet spilling over the bath's rim: on while flooding.")]
        public GameObject runningWater;
        public ParticleSystem splash;
        [Tooltip("Emits one drop per drip while the room waits.")]
        public ParticleSystem drips;
        [Tooltip("Spins over the drain, at the water surface, while draining.")]
        public Transform whirlpool;
        public ParticleSystem whirlpoolFoam;
        [Tooltip("Bare bulb: stutters when the flood starts.")]
        public Light bulb;
        [Tooltip("Rubbish already floating in the room: rides up on the water (stays where it is until the water passes it).")]
        public Transform[] floaters;

        [Header("Rules")]
        [Tooltip("Metres past the doorway before the flood starts.")]
        [SerializeField, Min(0f)] float slamDepth = 1.5f;
        [Tooltip("Metres per world second (scaled: eyes-shut fast-forward speeds it up).")]
        [SerializeField, Min(0f)] float riseRate = 0.012f;
        [Tooltip("Metres per blink, and per Shut Jump Interval with the eyes held shut.")]
        [SerializeField, Min(0f)] float blinkJump = 0.32f;
        [Tooltip("Real seconds of eyes held shut per jump.")]
        [SerializeField, Min(0.05f)] float shutJumpInterval = 0.5f;
        [Tooltip("Metres per world second once the drain is open.")]
        [SerializeField, Min(0.01f)] float drainRate = 0.35f;
        [Tooltip("You drown this far below the camera.")]
        [SerializeField, Min(0f)] float drownMargin = 0.05f;

        [Header("Feel")]
        [Tooltip("Real seconds the surface takes to surge up to a jump once the eyes open.")]
        [SerializeField, Min(0.01f)] float surgeSeconds = 0.25f;
        [Tooltip("The surge waits this long after the jump (the eyelids opening).")]
        [SerializeField, Min(0f)] float surgeDelay = 0.08f;
        [SerializeField, Min(0f)] float surgeJoltDegrees = 1.5f;
        [Tooltip("Real seconds from water over the eyes to death.")]
        [SerializeField, Min(0f)] float drownDelay = 1.2f;
        [Tooltip("The world goes muffled once the water is this close to the eyes, metres.")]
        [SerializeField, Min(0f)] float muffleWithin = 0.25f;
        [SerializeField] Color underwaterTint = new(0.16f, 0.2f, 0.12f, 0.72f);
        [Tooltip("Water this deep (metres) and over slows the player to Wade Speed.")]
        [SerializeField, Min(0f)] float wadeDepth = 0.9f;
        [SerializeField, Range(0.2f, 1f)] float wadeSpeed = 0.7f;
        [Tooltip("Respawn just outside the door, this far back from it, while the flood is on.")]
        [SerializeField] bool useCheckpoint = true;
        [SerializeField, Min(0.3f)] float checkpointBack = 1.2f;
        [Tooltip("Surface texture drift, UV per world second.")]
        [SerializeField] Vector2 waterScroll = new(0.015f, 0.01f);
        [SerializeField, Min(0f)] float whirlpoolSpin = 220f;

        [Header("Audio (an empty cue is a silent placeholder)")]
        [SerializeField] SoundCue dripCue;
        [Tooltip("World seconds between drips while the room waits.")]
        [SerializeField, Min(0.1f)] float dripEvery = 1.5f;
        [SerializeField] SoundCue tapRunCue;
        [Tooltip("The tap splutters and stops once the drain's open.")]
        [SerializeField] SoundCue tapStopCue;
        [SerializeField] SoundCue surgeCue;
        [SerializeField] SoundCue gurgleCue;
        [SerializeField] SoundCue coverClangCue;
        [SerializeField] SoundCue drownCue;

        [Header("Death")]
        [SerializeField] string respawnPrompt = "You drowned\nBLINK to wake up";

        [Header("Hooks (the door goes here)")]
        public UnityEvent onFloodStarted = new();
        public UnityEvent onSolved = new();
        public UnityEvent onReset = new();

        /// <summary>The player walked in: the tap's running. Slam and lock the door here.</summary>
        public event Action FloodStarted;
        /// <summary>The water's gone. Unlock the door and let it swing open here.</summary>
        public event Action Solved;
        /// <summary>Back to the start after a drowning (screen black). The door should be open and unlocked again.</summary>
        public event Action WasReset;

        public FloodState State => _flood != null ? _flood.State : FloodState.Idle;
        /// <summary>Metres of water above the floor (the real level; the surface on screen eases after a jump).</summary>
        public float Level => _flood != null ? _flood.Level : 0f;
        public float FloorY => roomBounds != null ? roomBounds.bounds.min.y : transform.position.y;

        FloodLevel _flood;
        PlayerController _player;
        Transform _cam;
        Health _health;
        CameraFeel _cameraFeel;
        GamepadHaptics _haptics;
        BlinkDetector _detector;
        AudioLowPassFilter _muffle;

        float _shown;                    // the surface's level on screen
        float _surgeFrom, _surgeT = 1f, _surgeWait;
        float _lastLevel;
        int _lastJumps;
        float _dripIn;
        float _tint;                     // underwater tint 0..1
        float _drownedSurface;           // where the surface sits while drowning
        bool _wading;
        bool _ownsCheckpoint;            // the respawn checkpoint outside the door is ours
        int _lastSpot = -1;
        AudioHandle _tapLoop, _gurgle;
        Coroutine _drown, _tapStop, _stutter;
        float[] _floatHome;              // each floater's resting height above the floor
        Renderer[] _waterRenderers;
        MaterialPropertyBlock _mpb;
        Vector4 _waterST = new(1f, 1f, 0f, 0f);
        static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        void Awake()
        {
            _flood = new FloodLevel(riseRate, blinkJump, shutJumpInterval, drainRate);
            if (roomBounds != null) _flood.MaxLevel = Mathf.Max(0.1f, roomBounds.bounds.size.y - 0.15f);
            if (water != null)
            {
                _waterRenderers = water.GetComponentsInChildren<Renderer>(true);
                _mpb = new MaterialPropertyBlock();
                if (_waterRenderers.Length > 0 && _waterRenderers[0].sharedMaterial is { } m && m.HasProperty(BaseMapId))
                {
                    var s = m.GetTextureScale(BaseMapId);
                    _waterST = new Vector4(s.x, s.y, 0f, 0f);
                }
            }
        }

        void OnValidate()
        {
            if (_flood == null) return;
            _flood.RiseRate = riseRate;
            _flood.BlinkJump = blinkJump;
            _flood.ShutJumpInterval = shutJumpInterval;
            _flood.DrainRate = drainRate;
        }

        void OnEnable()
        {
            TimeSkipController.Register(this);
            WakeUpSequence.Respawned += OnRespawned;
            if (drain != null) drain.Levered += OnDrainLevered;
            PuzzleRooms.Add(roomBounds);
        }

        void OnDisable()
        {
            TimeSkipController.Unregister(this);
            WakeUpSequence.Respawned -= OnRespawned;
            if (drain != null) drain.Levered -= OnDrainLevered;
            PuzzleRooms.Remove(roomBounds);
            StopSound(ref _tapLoop, 0f);
            StopSound(ref _gurgle, 0f);
            SetMuffle(0f);
            SetWading(false);
        }

        void OnDestroy()
        {
            if (_health != null) _health.Died -= OnPlayerDied;
            ReleaseCheckpoint();
        }

        void Start()
        {
            _player = FindAnyObjectByType<PlayerController>();
            if (_player != null)
            {
                var camera = _player.GetComponentInChildren<Camera>();
                _cam = camera != null ? camera.transform : null;
                _health = _player.GetComponent<Health>();
                _cameraFeel = _player.GetComponentInChildren<CameraFeel>();
                _haptics = _player.GetComponentInChildren<GamepadHaptics>();
                if (_health != null) _health.Died += OnPlayerDied;
            }
            if (roomBounds == null || doorway == null || water == null || drain == null || crowbar == null || _cam == null
                || searchSpots == null || searchSpots.Length == 0)
            {
                Debug.LogWarning($"[FloodedBasement] '{name}' is missing its room bounds, doorway, water, drain, crowbar, " +
                                 "search spots or the player camera; the puzzle is off.", this);
                enabled = false;
                return;
            }
            if (searchAnchors == null || searchAnchors.Length != searchSpots.Length)
                Debug.LogWarning("[FloodedBasement] Search Anchors should match Search Spots one for one; spots without an anchor hold the crowbar at their pivot.", this);
            _detector = BlinkDetector.Instance;
            if (_player.GetComponentInChildren<AudioListener>() is { } listener)
            {
                _muffle = listener.GetComponent<AudioLowPassFilter>();
                if (_muffle == null)
                {
                    _muffle = listener.gameObject.AddComponent<AudioLowPassFilter>();
                    _muffle.enabled = false;
                }
            }
            crowbar.itemId = CrowbarId;
            if (floaters != null)
            {
                _floatHome = new float[floaters.Length];
                for (int i = 0; i < floaters.Length; i++)
                    if (floaters[i] != null) _floatHome[i] = floaters[i].position.y - FloorY;
            }
            ResetRoom();
        }

        // ------------------------------------------------------------------ every frame

        void Update()
        {
            var state = State;
            bool alive = _health == null || !_health.IsDead;
            if (state == FloodState.Idle)
            {
                if (alive && !WakeUpSequence.Asleep && PastTheDoorway()) BeginFlood();
                else Drip();
            }
            else if ((state == FloodState.Flooding || state == FloodState.Draining) && alive && !WakeUpSequence.Asleep)
            {
                // Real time capped per frame: a hitch with the eyes shut mustn't land a burst of surges at once.
                _flood.Tick(Time.deltaTime, Mathf.Min(Time.unscaledDeltaTime, MaxRealStep), EyesHeldShut(), DrownHeight());
                if (_flood.JumpCount != _lastJumps)
                {
                    _lastJumps = _flood.JumpCount;
                    Surged();
                }
                if (_flood.State == FloodState.Drowned) _drown = StartCoroutine(Drown());
                else if (_flood.State == FloodState.Solved) OnSolved();
            }

            ShowWater(Time.unscaledDeltaTime);
            Feel();
        }

        /// <summary>A blink, called while the eyelids cover the screen (only blinks skip the world): the water jumps, or drains.</summary>
        public void Skip(float seconds)
        {
            if (WakeUpSequence.Asleep || _flood == null) return;
            _flood.Blink(seconds);
        }

        bool InRoom()
        {
            if (_player == null) return false;
            return roomBounds.bounds.Contains(_player.transform.position + Vector3.up * 0.5f);
        }

        bool PastTheDoorway() =>
            InRoom() && Vector3.Dot(_player.transform.position - doorway.position, doorway.forward) >= slamDepth;

        /// <summary>Eyes' height above the floor, read live (crouching drowns you sooner). Out of the room the water can't reach them.</summary>
        float DrownHeight() => InRoom() ? _cam.position.y - FloorY - drownMargin : float.PositiveInfinity;

        bool EyesHeldShut() =>
            _detector != null && _detector.EyesShut && !TimeSkipController.Locked && !GrabQte.BlocksInput;

        // ------------------------------------------------------------------ the flood

        void BeginFlood()
        {
            _flood.Start();
            _lastJumps = 0;
            _lastLevel = 0f;
            HideCrowbar();
            foreach (var spot in searchSpots)
                if (spot != null) spot.SetOpen(false);
            SetTapRunning(true);
            if (bulb != null)
            {
                if (_stutter != null) StopCoroutine(_stutter);
                _stutter = StartCoroutine(StutterBulb());
            }
            if (useCheckpoint)
            {
                var into = Vector3.ProjectOnPlane(doorway.forward, Vector3.up).normalized;
                WakeUpSequence.SetCheckpoint(doorway.position - into * checkpointBack, Quaternion.LookRotation(into, Vector3.up));
                _ownsCheckpoint = true;
            }
            FloodStarted?.Invoke();
            onFloodStarted.Invoke();
        }

        /// <summary>The crowbar goes in a random spot, never the last attempt's, so it slides out with the drawer.</summary>
        void HideCrowbar()
        {
            if (Hand.Instance != null && Hand.Instance.Item == crowbar) return; // somehow already in hand: leave it
            if (crowbar.Taken) crowbar.ReturnHome(); // used up on the drain before a death elsewhere: back in play

            int n = searchSpots.Length, pick = Random.Range(0, n);
            if (n > 1 && pick == _lastSpot) pick = (pick + 1 + Random.Range(0, n - 1)) % n;
            _lastSpot = pick;
            var anchor = searchAnchors != null && pick < searchAnchors.Length && searchAnchors[pick] != null
                ? searchAnchors[pick] : searchSpots[pick].transform;
            crowbar.transform.SetParent(anchor, false);
            crowbar.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            crowbar.gameObject.SetActive(true);
        }

        void Surged()
        {
            if (State != FloodState.Flooding) return;
            PlayAt(surgeCue, SurfacePointNearPlayer());
            if (_cameraFeel != null) _cameraFeel.AddJolt(surgeJoltDegrees, 0.02f);
            if (_haptics != null) _haptics.Thump(0.35f);
        }

        Vector3 SurfacePointNearPlayer()
        {
            var p = _player != null ? _player.transform.position : roomBounds.bounds.center;
            return new Vector3(p.x, FloorY + Level, p.z);
        }

        void Drip()
        {
            _dripIn -= Time.deltaTime;
            if (_dripIn > 0f) return;
            _dripIn = dripEvery * Random.Range(0.85f, 1.15f);
            if (drips != null) drips.Emit(1);
            if (tap != null) PlayAt(dripCue, tap.position);
        }

        void SetTapRunning(bool on)
        {
            if (_tapStop != null) StopCoroutine(_tapStop);
            _tapStop = null;
            if (runningWater != null) runningWater.SetActive(on);
            if (splash != null)
            {
                if (on) splash.Play();
                else splash.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (on && !IsPlaying(_tapLoop) && tap != null) _tapLoop = PlayLoop(tapRunCue, tap);
            if (!on) StopSound(ref _tapLoop, 0.3f);
        }

        /// <summary>The tap splutters a few times and dies.</summary>
        IEnumerator TapSplutter()
        {
            if (tap != null) PlayAt(tapStopCue, tap.position);
            StopSound(ref _tapLoop, 0.6f);
            if (splash != null) splash.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (float wait in new[] { 0.12f, 0.08f, 0.2f, 0.1f, 0.3f, 0.12f })
            {
                if (runningWater != null) runningWater.SetActive(!runningWater.activeSelf);
                yield return new WaitForSecondsRealtime(wait);
            }
            if (runningWater != null) runningWater.SetActive(false);
            _tapStop = null;
        }

        IEnumerator StutterBulb()
        {
            var flicker = bulb.GetComponent<LightFlicker>();
            float baseIntensity = bulb.intensity;
            if (flicker != null) flicker.enabled = false;
            foreach (float on in new[] { 0.05f, 0.6f, 0.02f, 1f, 0.1f, 0.8f, 1f })
            {
                bulb.intensity = baseIntensity * on;
                yield return new WaitForSecondsRealtime(Random.Range(0.04f, 0.12f));
            }
            bulb.intensity = baseIntensity;
            if (flicker != null) flicker.enabled = true;
            _stutter = null;
        }

        // ------------------------------------------------------------------ the drain

        void OnDrainLevered(Interactor who)
        {
            if (State != FloodState.Flooding)
            {
                drain.PutBack(); // only while the water's coming in (can't happen with the crowbar hidden till then)
                return;
            }
            if (Hand.Of(who) is not { } hand || !hand.UseUp(CrowbarId)) who.Inventory?.Remove(CrowbarId);
            PlayAt(coverClangCue, drain.transform.position);
            if (_cameraFeel != null) _cameraFeel.AddJolt(2.5f, 0.03f);
            if (_haptics != null) _haptics.Thump(0.7f);
            _flood.OpenDrain();
            if (_tapStop != null) StopCoroutine(_tapStop);
            _tapStop = StartCoroutine(TapSplutter());
            _gurgle = PlayLoop(gurgleCue, drain.transform);
            if (whirlpoolFoam != null) whirlpoolFoam.Play();
            if (_flood.State == FloodState.Solved) OnSolved(); // levered with almost no water in
        }

        void OnSolved()
        {
            StopSound(ref _gurgle, 1.2f);
            if (whirlpoolFoam != null) whirlpoolFoam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            SetKeyTakeable(true);
            ReleaseCheckpoint();
            SetMuffle(0f);
            SetWading(false);
            Solved?.Invoke();
            onSolved.Invoke();
        }

        void ReleaseCheckpoint()
        {
            if (!_ownsCheckpoint) return;
            _ownsCheckpoint = false;
            WakeUpSequence.ClearCheckpoint();
        }

        /// <summary>
        /// Before the respawn teleport: the checkpoint is only for retrying this room. Dying anywhere else (wandered off
        /// mid-flood, or later) goes back to the bed.
        /// </summary>
        void OnPlayerDied()
        {
            if (!InRoom()) ReleaseCheckpoint();
        }

        void SetKeyTakeable(bool on)
        {
            if (rewardKey == null || rewardKey.Taken) return;
            foreach (var c in rewardKey.GetComponentsInChildren<Collider>(true)) c.enabled = on;
        }

        // ------------------------------------------------------------------ drowning

        IEnumerator Drown()
        {
            _drownedSurface = _cam.position.y - FloorY + 0.12f; // just over the eyes, wherever the slump takes the camera
            WakeUpSequence.NextRespawnPrompt = respawnPrompt;
            _player.InputEnabled = false;
            TimeSkipController.Locked = true;
            if (TimeSkipController.Instance != null) TimeSkipController.Instance.InterruptFastForward();
            Carryable.DropHeld();
            SetTapRunning(false);
            PlayAt(drownCue, _cam.position);
            if (_haptics != null) _haptics.Thump(1f);
            yield return new WaitForSecondsRealtime(drownDelay);

            _drown = null;
            if (_health != null) _health.TakeDamage(_health.Current, _cam.position, _cam.forward);
            // WakeUpSequence takes it from here (slump, black, wake up) and raises Respawned, which resets the room.
            if (_health == null || !_health.IsDead)
            {
                Debug.LogWarning("[FloodedBasement] Drowning didn't kill the player; resetting.", this);
                _player.InputEnabled = true;
                TimeSkipController.Locked = false;
                WakeUpSequence.NextRespawnPrompt = null;
                OnRespawned();
            }
        }

        // ------------------------------------------------------------------ reset

        void OnRespawned()
        {
            if (State == FloodState.Solved) return;
            ResetRoom();
            WasReset?.Invoke();
            onReset.Invoke();
        }

        /// <summary>Dry, tap dripping, cover on, crowbar put away until the next attempt. Never in view (start, or screen black).</summary>
        void ResetRoom()
        {
            if (_drown != null) StopCoroutine(_drown);
            _drown = null;
            _flood.Reset();
            _shown = _lastLevel = 0f;
            _surgeT = 1f;
            _lastJumps = 0;
            _tint = 0f;
            SetTapRunning(false);
            StopSound(ref _gurgle, 0f);
            if (whirlpoolFoam != null) whirlpoolFoam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            drain.PutBack();
            SetKeyTakeable(false);
            if (!(Hand.Instance != null && Hand.Instance.Item == crowbar))
            {
                if (crowbar.Taken) crowbar.ReturnHome();
                crowbar.gameObject.SetActive(false); // placed when the next flood starts
            }
            SetMuffle(0f);
            SetWading(false);
            ShowWater(0f);
        }

        // ------------------------------------------------------------------ the surface

        void ShowWater(float realDt)
        {
            float level = Level;
            // A jump (blink, eyes shut) or a blink-drain is a step: ease the surface to it once the eyes open.
            if (Mathf.Abs(level - _lastLevel) > 0.05f)
            {
                _surgeFrom = _shown;
                _surgeT = 0f;
                _surgeWait = surgeDelay;
            }
            _lastLevel = level;
            if (_surgeT < 1f)
            {
                if (_surgeWait > 0f) _surgeWait -= realDt;
                else _surgeT = Mathf.Min(1f, _surgeT + realDt / surgeSeconds);
                float e = 1f - Mathf.Pow(1f - _surgeT, 3f); // rushes up, settles
                _shown = Mathf.LerpUnclamped(_surgeFrom, level, e);
            }
            else _shown = level;

            float shown = State == FloodState.Drowned ? Mathf.Max(_shown, _drownedSurface) : _shown;
            bool visible = shown > 0.005f;
            if (water.gameObject.activeSelf != visible) water.gameObject.SetActive(visible);
            var p = water.position;
            water.position = new Vector3(p.x, FloorY + shown, p.z);

            if (visible && _waterRenderers != null)
            {
                Vector2 offset = waterScroll * Time.time;
                _mpb.SetVector(BaseMapST, new Vector4(_waterST.x, _waterST.y, offset.x % 1f, offset.y % 1f));
                foreach (var r in _waterRenderers) if (r != null) r.SetPropertyBlock(_mpb);
            }

            if (whirlpool != null)
            {
                bool spinning = State == FloodState.Draining && shown > 0.02f;
                if (whirlpool.gameObject.activeSelf != spinning) whirlpool.gameObject.SetActive(spinning);
                if (spinning)
                {
                    var w = whirlpool.position;
                    whirlpool.position = new Vector3(w.x, FloorY + shown + 0.004f, w.z);
                    whirlpool.Rotate(0f, -whirlpoolSpin * Time.deltaTime, 0f, Space.World);
                }
            }
            if (_floatHome != null)
                for (int i = 0; i < floaters.Length; i++)
                {
                    if (floaters[i] == null) continue;
                    var f = floaters[i].position;
                    float bob = shown > _floatHome[i] ? Mathf.Sin(Time.time * 1.3f + i * 2.1f) * 0.012f : 0f;
                    floaters[i].position = new Vector3(f.x, FloorY + Mathf.Max(_floatHome[i], shown) + bob, f.z);
                }
            if (whirlpoolFoam != null)
            {
                var f = whirlpoolFoam.transform.position;
                whirlpoolFoam.transform.position = new Vector3(f.x, FloorY + Mathf.Max(0.02f, shown), f.z);
            }
        }

        // ------------------------------------------------------------------ how it feels

        void Feel()
        {
            var state = State;
            bool live = (state == FloodState.Flooding || state == FloodState.Drowned) && InRoom();
            float eyes = live ? _cam.position.y - FloorY : float.PositiveInfinity;
            float surface = state == FloodState.Drowned ? eyes + 1f : _shown;
            float gap = eyes - surface;

            // Underwater: tint the view. Near the eyes: muffle the world.
            float targetTint = live && gap < 0f ? 1f : 0f;
            _tint = Mathf.MoveTowards(_tint, targetTint, Time.unscaledDeltaTime * 4f);
            SetMuffle(!live ? 0f : gap <= 0f ? 1f : 1f - Mathf.Clamp01(gap / Mathf.Max(0.01f, muffleWithin)));
            SetWading(InRoom() && (state == FloodState.Flooding || state == FloodState.Draining) && _shown >= wadeDepth);
        }

        void SetMuffle(float amount01)
        {
            if (_muffle == null) return;
            bool on = amount01 > 0.001f;
            if (_muffle.enabled != on) _muffle.enabled = on;
            if (on) _muffle.cutoffFrequency = Mathf.Lerp(22000f, 650f, Mathf.Pow(amount01, 0.5f));
        }

        void SetWading(bool on)
        {
            if (on == _wading || _player == null) return;
            _wading = on;
            _player.SpeedMultiplier = on ? wadeSpeed : 1f;
        }

        void OnGUI()
        {
            if (_tint <= 0f || Event.current.type != EventType.Repaint) return;
            GUI.depth = -500; // over the HUD, under the eyelids (-1000)
            var old = GUI.color;
            GUI.color = new Color(underwaterTint.r, underwaterTint.g, underwaterTint.b, underwaterTint.a * _tint);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // ------------------------------------------------------------------ audio

        static AudioHandle PlayLoop(SoundCue cue, Transform at) =>
            cue != null && AudioService.Instance != null ? AudioService.Instance.PlayLoop(cue, at) : AudioHandle.None;

        static void PlayAt(SoundCue cue, Vector3 at)
        {
            if (cue != null && AudioService.Instance != null) AudioService.Instance.PlayAt(cue, at);
        }

        static bool IsPlaying(AudioHandle handle) => AudioService.Instance != null && AudioService.Instance.IsPlaying(handle);

        static void StopSound(ref AudioHandle handle, float fade)
        {
            if (AudioService.Instance != null) AudioService.Instance.Stop(handle, fade);
            handle = AudioHandle.None;
        }

        void OnDrawGizmosSelected()
        {
            if (roomBounds != null)
            {
                var b = roomBounds.bounds;
                Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.6f);
                Gizmos.DrawWireCube(b.center, b.size);
            }
            if (doorway != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.8f);
                Vector3 slam = doorway.position + doorway.forward * slamDepth;
                Gizmos.DrawLine(doorway.position, slam);
                Gizmos.DrawWireSphere(slam, 0.15f);
            }
        }
    }
}
