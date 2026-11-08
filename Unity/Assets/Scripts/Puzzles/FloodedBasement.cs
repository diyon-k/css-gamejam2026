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
using Blink.World;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
    /// <see cref="door"/> (D5) slams shut and locks when the flood starts, and unlocks and swings open when the water's
    /// gone or the room resets after a drowning. The events below are for anything else.
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
        [Tooltip("The room's door (D5): slammed and locked while the water's in.")]
        public Door door;
        [Tooltip("Where the crowbar can be hidden: one per spot.")]
        public Openable[] searchSpots;
        [Tooltip("Where the crowbar lies in each spot (a child of the drawer, so it slides out with it). Same order as Search Spots.")]
        public Transform[] searchAnchors;
        public Pickup crowbar;
        [Tooltip("The Search Spot the crowbar always lies in, there from the start. -1 = a random spot each attempt, hidden until the flood starts.")]
        public int crowbarSpot = -1;
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
        [Tooltip("Rings where the spill off the bath meets the water: placed there, rides the surface while the tap runs.")]
        public ParticleSystem ripples;
        [Tooltip("Blink/FloodWetMark strips on the walls and floor: dark up to the high-water mark, drying after.")]
        public Renderer wetMarks;
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
        [Tooltip("How fast the surface springs to a jump once the eyes open, Hz (higher = snappier).")]
        [SerializeField, Min(0.1f)] float surgeFrequency = 2.2f;
        [Tooltip("Spring damping: 1 = no overshoot, lower sloshes past the new level and back (0.6 ≈ 3 cm on a blink).")]
        [SerializeField, Range(0.1f, 1f)] float surgeDamping = 0.6f;
        [Tooltip("The surge waits this long after the jump (the eyelids opening).")]
        [SerializeField, Min(0f)] float surgeDelay = 0.08f;
        [SerializeField, Min(0f)] float surgeJoltDegrees = 1.5f;
        [Tooltip("Height of the ring wave that rolls out from the bath on each surge, metres.")]
        [SerializeField, Min(0f)] float surgeWaveHeight = 0.05f;
        [Tooltip("Real seconds for the surface to calm after a surge.")]
        [SerializeField, Min(0.05f)] float chopSettle = 1.4f;
        [Tooltip("How deep the drain pulls the surface down over it while draining, metres (never more than 60% of the water).")]
        [SerializeField, Min(0f)] float funnelDepth = 0.28f;
        [Tooltip("Below this depth, metres, the sheet of water breaks up into puddles.")]
        [SerializeField, Min(0.01f)] float puddleDepth = 0.07f;
        [Tooltip("World seconds for the walls and floor to dry once the water's gone.")]
        [SerializeField, Min(1f)] float drySeconds = 35f;
        [Tooltip("Real seconds from water over the eyes to death.")]
        [SerializeField, Min(0f)] float drownDelay = 1.2f;
        [Tooltip("The world goes muffled once the water is this close to the eyes, metres.")]
        [SerializeField, Min(0f)] float muffleWithin = 0.25f;
        [Tooltip("Flat overlay on top of the underwater grade (a URP volume).")]
        [SerializeField] Color underwaterTint = new(0.16f, 0.2f, 0.12f, 0.4f);
        [Tooltip("Water this deep (metres) and over slows the player to Wade Speed.")]
        [SerializeField, Min(0f)] float wadeDepth = 0.9f;
        [SerializeField, Range(0.2f, 1f)] float wadeSpeed = 0.7f;
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
        [SerializeField] SoundCue doorSlamCue;
        [Tooltip("Real seconds the door takes to slam shut.")]
        [SerializeField, Min(0.01f)] float slamSeconds = 0.12f;

        [Header("Death")]
        [SerializeField] string respawnPrompt = "You drowned\nBLINK to wake up";

        [Header("Hooks")]
        public UnityEvent onFloodStarted = new();
        public UnityEvent onSolved = new();
        public UnityEvent onReset = new();

        /// <summary>The player walked in: the tap's running, the door slammed.</summary>
        public event Action FloodStarted;
        /// <summary>The water's gone, the door unlocked and swinging open.</summary>
        public event Action Solved;
        /// <summary>Back to the start after a drowning (screen black), the door open and unlocked again.</summary>
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
        float _shownVel;                 // its spring's velocity, m/s
        float _surgeWait, _holdLevel;    // after a jump the surface holds at the old level till the eyes are open
        float _lastLevel;
        float _chop, _drainFlow, _funnel; // surface roughness, swirl and dip over the drain, 0..1 / metres
        float _waveAge = 100f, _waveAmp; // the ring wave after a surge
        float _wet, _wetTop;              // walls and floor: wetness 0..1 and the high-water mark (world y)
        Vector3 _spill;                   // where the bath spills into the room: ripples and ring waves start here
        int _lastJumps;
        float _dripIn;
        float _tint;                     // underwater tint 0..1
        float _drownedSurface;           // where the surface sits while drowning
        bool _wading;
        int _lastSpot = -1;
        AudioHandle _tapLoop, _gurgle;
        Coroutine _drown, _tapStop, _stutter;
        float[] _floatHome;              // each floater's resting height above the floor
        Quaternion[] _floatTurn;         // and its resting rotation
        Renderer[] _waterRenderers;
        MaterialPropertyBlock _mpb, _wetMpb;
        Volume _underwater;
        VolumeProfile _underwaterProfile;
        LensDistortion _underwaterWobble;

        static readonly int LevelId = Shader.PropertyToID("_Level");
        static readonly int DrainPosId = Shader.PropertyToID("_DrainPos");
        static readonly int DrainFlowId = Shader.PropertyToID("_DrainFlow");
        static readonly int FunnelId = Shader.PropertyToID("_Funnel");
        static readonly int ChopId = Shader.PropertyToID("_Chop");
        static readonly int WaveOriginId = Shader.PropertyToID("_WaveOrigin");
        static readonly int WaveTimeId = Shader.PropertyToID("_WaveTime");
        static readonly int WaveAmpId = Shader.PropertyToID("_WaveAmp");
        static readonly int BreakupId = Shader.PropertyToID("_Breakup");
        static readonly int WetTopId = Shader.PropertyToID("_WetTop");
        static readonly int WetId = Shader.PropertyToID("_Wet");

        void Awake()
        {
            _flood = new FloodLevel(riseRate, blinkJump, shutJumpInterval, drainRate);
            if (roomBounds != null) _flood.MaxLevel = Mathf.Max(0.1f, roomBounds.bounds.size.y - 0.15f);
            _mpb = new MaterialPropertyBlock();
            _wetMpb = new MaterialPropertyBlock();
            if (water != null) _waterRenderers = water.GetComponentsInChildren<Renderer>(true);
            BuildUnderwaterVolume();
        }

        void OnDestroy()
        {
            if (_underwaterProfile != null) Destroy(_underwaterProfile);
        }

        /// <summary>A global grade for under the surface (murky green, dark edges, a slow wobble); its weight follows the tint.</summary>
        void BuildUnderwaterVolume()
        {
            _underwaterProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var grade = _underwaterProfile.Add<ColorAdjustments>();
            grade.colorFilter.Override(new Color(0.55f, 0.72f, 0.45f));
            grade.saturation.Override(-35f);
            grade.postExposure.Override(-1f);
            var vignette = _underwaterProfile.Add<Vignette>();
            vignette.intensity.Override(0.5f);
            vignette.smoothness.Override(0.8f);
            vignette.color.Override(new Color(0.02f, 0.05f, 0.02f));
            _underwaterWobble = _underwaterProfile.Add<LensDistortion>();
            _underwaterWobble.intensity.Override(0f);
            var go = new GameObject("Underwater Volume");
            go.transform.SetParent(transform, false);
            _underwater = go.AddComponent<Volume>();
            _underwater.isGlobal = true;
            _underwater.priority = 60f; // over the rooms' grading and the damage vignette
            _underwater.sharedProfile = _underwaterProfile;
            _underwater.weight = 0f;
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
                _floatTurn = new Quaternion[floaters.Length];
                for (int i = 0; i < floaters.Length; i++)
                    if (floaters[i] != null)
                    {
                        _floatHome[i] = floaters[i].position.y - FloorY;
                        _floatTurn[i] = floaters[i].rotation;
                    }
            }
            _spill = ripples != null ? ripples.transform.position : tap != null ? tap.position : roomBounds.bounds.center;
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
            if (door != null && door.Slam(slamSeconds)) // already shut behind them: it just locks, quietly
            {
                PlayAt(doorSlamCue, door.Centre);
                if (_cameraFeel != null) _cameraFeel.AddJolt(2f, 0.03f);
                if (_haptics != null) _haptics.Thump(0.8f);
            }
            FloodStarted?.Invoke();
            onFloodStarted.Invoke();
        }

        bool FixedCrowbar => crowbarSpot >= 0 && crowbarSpot < searchSpots.Length;

        /// <summary>
        /// The crowbar goes in its fixed spot (<see cref="crowbarSpot"/>), or a random one that isn't the last attempt's,
        /// so it slides out with the drawer.
        /// </summary>
        void HideCrowbar()
        {
            if (Hand.Instance != null && Hand.Instance.Item == crowbar) return; // somehow already in hand: leave it
            if (crowbar.Taken) crowbar.ReturnHome(); // used up on the drain before a death elsewhere: back in play

            int n = searchSpots.Length, pick = FixedCrowbar ? crowbarSpot : Random.Range(0, n);
            if (!FixedCrowbar && n > 1 && pick == _lastSpot) pick = (pick + 1 + Random.Range(0, n - 1)) % n;
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
            SetMuffle(0f);
            SetWading(false);
            ReleaseDoor();
            Solved?.Invoke();
            onSolved.Invoke();
        }

        /// <summary>Unlocks the door and swings it open, away from the player.</summary>
        void ReleaseDoor()
        {
            if (door == null) return;
            door.Unlock();
            door.SetOpen(true, _player != null ? _player.transform.position : (Vector3?)null);
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
            ReleaseDoor();
            WasReset?.Invoke();
            onReset.Invoke();
        }

        /// <summary>Dry, tap dripping, cover on, crowbar put away until the next attempt. Never in view (start, or screen black).</summary>
        void ResetRoom()
        {
            if (_drown != null) StopCoroutine(_drown);
            _drown = null;
            _flood.Reset();
            _shown = _shownVel = _lastLevel = _holdLevel = _surgeWait = 0f;
            _chop = _drainFlow = _funnel = _waveAmp = 0f;
            _waveAge = 100f;
            _wet = 0f;
            _lastJumps = 0;
            _tint = 0f;
            if (ripples != null) ripples.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            SetTapRunning(false);
            StopSound(ref _gurgle, 0f);
            if (whirlpoolFoam != null) whirlpoolFoam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            drain.PutBack();
            SetKeyTakeable(false);
            if (FixedCrowbar) HideCrowbar(); // always there, waiting in its spot
            else if (!(Hand.Instance != null && Hand.Instance.Item == crowbar))
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
            var state = State;
            // A jump (blink, eyes shut) or a blink-drain is a step: the surface holds till the eyes open, then springs to
            // it, overshooting a little and sloshing back. A rise also churns the water and rolls a wave out from the bath.
            if (Mathf.Abs(level - _lastLevel) > 0.05f)
            {
                if (_surgeWait <= 0f) _holdLevel = _lastLevel; // two jumps in one blink: hold at the first
                _surgeWait = surgeDelay;
                bool rise = level > _lastLevel;
                _chop = Mathf.Max(_chop, rise ? 1f : 0.5f);
                if (rise && state == FloodState.Flooding)
                {
                    _waveAge = -surgeDelay;
                    _waveAmp = surgeWaveHeight;
                }
            }
            _lastLevel = level;
            float target = level;
            if (_surgeWait > 0f)
            {
                _surgeWait -= realDt;
                target = _holdLevel;
            }
            Spring(target, realDt);

            float shown = state == FloodState.Drowned ? Mathf.Max(_shown, _drownedSurface) : _shown;
            bool visible = shown > 0.005f;
            if (water.gameObject.activeSelf != visible) water.gameObject.SetActive(visible);
            var p = water.position;
            water.position = new Vector3(p.x, FloorY + shown, p.z);

            // Calm water still stirs while the tap pours in, more while the drain pulls it out.
            float calm = state == FloodState.Flooding ? 0.12f : state == FloodState.Draining ? 0.3f : 0f;
            _chop = Mathf.MoveTowards(_chop, calm, realDt / chopSettle);
            _waveAge += realDt;
            bool pulling = state == FloodState.Draining;
            _drainFlow = Mathf.MoveTowards(_drainFlow, pulling ? 1f : 0f, realDt * 1.5f);
            _funnel = Mathf.MoveTowards(_funnel, pulling ? Mathf.Min(funnelDepth, shown * 0.6f) : 0f, realDt * 0.6f);
            _funnel = Mathf.Min(_funnel, shown * 0.6f);

            if (visible && _waterRenderers != null)
            {
                var d = drain != null ? drain.transform.position : roomBounds.bounds.center;
                _mpb.SetFloat(LevelId, shown);
                _mpb.SetVector(DrainPosId, d);
                _mpb.SetFloat(DrainFlowId, _drainFlow);
                _mpb.SetFloat(FunnelId, _funnel);
                _mpb.SetFloat(ChopId, _chop);
                _mpb.SetVector(WaveOriginId, _spill);
                _mpb.SetFloat(WaveTimeId, Mathf.Max(0f, _waveAge));
                _mpb.SetFloat(WaveAmpId, _waveAge >= 0f ? _waveAmp : 0f);
                _mpb.SetFloat(BreakupId, 1f - Mathf.Clamp01(shown / puddleDepth));
                foreach (var r in _waterRenderers) if (r != null) r.SetPropertyBlock(_mpb);
            }

            ShowWet(shown);
            if (ripples != null)
            {
                bool pouring = state == FloodState.Flooding && runningWater != null && runningWater.activeSelf && shown > 0.03f;
                if (pouring && !ripples.isEmitting) ripples.Play();
                else if (!pouring && ripples.isEmitting) ripples.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                var rp = ripples.transform.position;
                ripples.transform.position = new Vector3(rp.x, FloorY + shown + 0.01f, rp.z);
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
                    bool afloat = shown > _floatHome[i];
                    float bob = afloat ? Mathf.Sin(Time.time * 1.3f + i * 2.1f) * (0.012f + 0.035f * _chop) : 0f;
                    floaters[i].position = new Vector3(f.x, FloorY + Mathf.Max(_floatHome[i], shown) + bob, f.z);
                    float tilt = afloat ? 3f + 12f * _chop : 0f; // rocks, harder after a surge
                    floaters[i].rotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.7f + i) * tilt, 0f,
                        Mathf.Cos(Time.time * 1.3f + i * 1.7f) * tilt) * _floatTurn[i];
                }
            if (whirlpoolFoam != null)
            {
                var f = whirlpoolFoam.transform.position;
                whirlpoolFoam.transform.position = new Vector3(f.x, FloorY + Mathf.Max(0.02f, shown), f.z);
            }
        }

        /// <summary>Damped spring from the surface on screen to <paramref name="target"/> (real time, small fixed steps).</summary>
        void Spring(float target, float realDt)
        {
            float w = 2f * Mathf.PI * surgeFrequency;
            for (float left = Mathf.Min(realDt, 0.25f); left > 0f; left -= 0.02f)
            {
                float dt = Mathf.Min(left, 0.02f);
                _shownVel += (w * w * (target - _shown) - 2f * surgeDamping * w * _shownVel) * dt;
                _shown += _shownVel * dt;
            }
            if (_shown < 0f) _shown = _shownVel = 0f;
        }

        /// <summary>Walls and floor dark up to the highest the water's been, drying off once it's gone.</summary>
        void ShowWet(float shown)
        {
            if (shown > 0.01f)
            {
                _wetTop = _wet > 0f ? Mathf.Max(_wetTop, FloorY + shown) : FloorY + shown;
                _wet = 1f;
            }
            else if (_wet > 0f) _wet = Mathf.Max(0f, _wet - Time.deltaTime / drySeconds);
            if (wetMarks == null) return;
            if (wetMarks.enabled != _wet > 0f) wetMarks.enabled = _wet > 0f;
            _wetMpb.SetFloat(WetTopId, _wetTop);
            _wetMpb.SetFloat(WetId, _wet);
            wetMarks.SetPropertyBlock(_wetMpb);
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
            if (_underwater != null)
            {
                _underwater.weight = _tint;
                if (_tint > 0f) _underwaterWobble.intensity.value = -0.18f + 0.1f * Mathf.Sin(Time.unscaledTime * 2.7f);
            }
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
