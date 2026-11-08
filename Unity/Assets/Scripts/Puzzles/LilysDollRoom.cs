using System.Collections;
using Blink.Audio;
using Blink.Combat;
using Blink.Detection;
using Blink.Effects;
using Blink.Interaction;
using Blink.Player;
using Blink.TimeSkip;
using UnityEngine;

namespace Blink.Puzzles
{
    /// <summary>
    /// P2, Lily's doll (docs/puzzles/LILYS_DOLL_ROOM.md). On the puzzle root, whose trigger <see cref="BoxCollider"/> is the
    /// room. Walk in, take the key from the doll's lap and back out without taking your eyes off her.
    /// <list type="bullet">
    /// <item>Look away for more than <see cref="lookAwayGrace"/>: she leaps onto your face and you die.</item>
    /// <item>Blink once (or shut your eyes): when they open she has lurched closer with a giggle. Blink again: she's on your face.</item>
    /// <item>Eyes held shut past <see cref="shutKillSeconds"/>: on your face.</item>
    /// <item>Leave with the key: done for good. Leave without it: the warning is forgiven.</item>
    /// </list>
    /// The rules live in <see cref="DollRule"/>; this does the looking, moving, sound and the kill. Everything that
    /// must not speed up runs on unscaled time. Placed by BLINK > Place Puzzle: Lily's Doll.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LilysDollRoom : MonoBehaviour, ISkippable
    {
        [Header("Parts")]
        [Tooltip("Root transform of the doll (moves on the lurch and the leap).")]
        public Transform doll;
        [Tooltip("Anchor between the eyes: the look checks aim here, and it ends up in front of the camera.")]
        public Transform face;
        [Tooltip("Turns to keep facing the player while the rules apply.")]
        public Transform head;
        [Tooltip("The key sits here, so it moves with the doll.")]
        public Transform lap;
        public Pickup key;
        public string keyItemId = "key_dorm";
        [Tooltip("Trigger box sized to the room interior, full height. 'In the room' = the player is inside it.")]
        public BoxCollider roomBounds;

        [Header("Rules")]
        [Tooltip("Metres past the room bounds before you count as out (so half a step back through the door isn't).")]
        [SerializeField, Min(0f)] float exitMargin = 0.4f;
        [Tooltip("Half-angle of the 'looking at her' cone, degrees.")]
        [SerializeField, Range(1f, 89f)] float watchAngle = 30f;
        [Tooltip("Real seconds she can be unwatched before she leaps (soaks up camera jolts and a twitchy mouse).")]
        [SerializeField, Min(0f)] float lookAwayGrace = 0.15f;
        [Tooltip("Eyes held shut this long (real seconds, one closure) kills.")]
        [SerializeField, Min(0.36f)] float shutKillSeconds = 1f;

        [Header("Moves")]
        [Tooltip("How far the warning blink brings her towards you, metres.")]
        [SerializeField, Min(0f)] float warningLurch = 1.2f;
        [Tooltip("The lurch never brings her closer than this to the player.")]
        [SerializeField, Min(0f)] float lurchMinDistance = 1.5f;
        [Tooltip("Head tilt once warned, degrees.")]
        [SerializeField] float warnedHeadTilt = 20f;
        [Tooltip("How fast her head follows you (higher = snappier).")]
        [SerializeField, Min(0f)] float headTurnSharpness = 3f;
        [SerializeField, Min(0.01f)] float leapSeconds = 0.25f;
        [Tooltip("Height of the leap's arc, metres.")]
        [SerializeField, Min(0f)] float leapArc = 0.35f;
        [Tooltip("Her face ends this far in front of the camera, metres.")]
        [SerializeField, Min(0.05f)] float faceDistance = 0.25f;
        [Tooltip("Minimum real seconds on your face before you die.")]
        [SerializeField, Min(0f)] float faceHoldSeconds = 0.6f;
        [Tooltip("Cap on the face hold while waiting for closed eyes to open.")]
        [SerializeField, Min(0.1f)] float maxFaceHoldSeconds = 3f;
        [SerializeField, Min(0f)] float joltDegrees = 6f;
        [SerializeField, Min(0.02f)] float joltEvery = 0.1f;

        [Header("Audio (an empty cue is a silent placeholder)")]
        [SerializeField] SoundCue musicCue;
        [SerializeField] SoundCue giggleCue;
        [SerializeField] SoundCue screamCue;
        [SerializeField] SoundCue scratchCue;
        [Tooltip("The music box goes out of tune once she's warned you.")]
        [SerializeField] float warnedMusicPitch = 0.94f;

        [Header("Death")]
        [SerializeField] string respawnPrompt = "Lily wanted to play\nBLINK to wake up";

        public DollState State => _rule != null ? _rule.State : DollState.Dormant;

        DollRule _rule;
        PlayerController _player;
        Transform _cam;
        Camera _camera;
        Inventory _inventory;
        Health _health;
        CameraFeel _cameraFeel;
        GamepadHaptics _haptics;
        BlinkDetector _detector;

        Transform _dollParent, _keyParent;
        Vector3 _dollHomePos, _keyHomePos, _keyHomeScale;
        Quaternion _dollHomeRot, _headRest, _keyHomeRot;
        float _floorY;

        AudioHandle _music, _scratch;
        float _shutBeganAt = -1f;
        bool _returnHome, _killed;
        Coroutine _kill, _hiccup;
        readonly RaycastHit[] _hits = new RaycastHit[16];

        bool Active => State == DollState.Armed || State == DollState.Warned;

        void Awake()
        {
            _rule = new DollRule(lookAwayGrace, shutKillSeconds);
            _floorY = transform.position.y;
            if (doll != null)
            {
                _dollParent = doll.parent;
                doll.GetLocalPositionAndRotation(out _dollHomePos, out _dollHomeRot);
            }
            if (head != null) _headRest = head.localRotation;
            if (key != null)
            {
                _keyParent = key.transform.parent;
                key.transform.GetLocalPositionAndRotation(out _keyHomePos, out _keyHomeRot);
                _keyHomeScale = key.transform.localScale;
                if (!string.IsNullOrEmpty(keyItemId)) key.itemId = keyItemId;
            }
        }

        void OnValidate()
        {
            if (_rule == null) return;
            _rule.LookAwayGrace = lookAwayGrace;
            _rule.ShutKillSeconds = shutKillSeconds;
        }

        void OnEnable()
        {
            TimeSkipController.Register(this);
            WakeUpSequence.Respawned += OnRespawned;
            PuzzleRooms.Add(roomBounds);
        }

        void OnDisable()
        {
            TimeSkipController.Unregister(this);
            WakeUpSequence.Respawned -= OnRespawned;
            PuzzleRooms.Remove(roomBounds);
            if (_detector != null) _detector.EyesShutBegan -= OnEyesShutBegan;
            _detector = null;
            StopSound(ref _music, 0f);
            StopSound(ref _scratch, 0f);
        }

        void Start()
        {
            _player = FindAnyObjectByType<PlayerController>();
            if (_player != null)
            {
                _camera = _player.GetComponentInChildren<Camera>();
                _cam = _camera != null ? _camera.transform : null;
                _inventory = _player.GetComponent<Inventory>();
                _health = _player.GetComponent<Health>();
                _cameraFeel = _player.GetComponentInChildren<CameraFeel>();
                _haptics = _player.GetComponentInChildren<GamepadHaptics>();
            }
            if (doll == null || face == null || roomBounds == null || _cam == null)
            {
                Debug.LogWarning($"[LilysDoll] '{name}' is missing its doll, face, room bounds or the player camera; the puzzle is off.", this);
                enabled = false;
                return;
            }
            _detector = BlinkDetector.Instance;
            if (_detector != null) _detector.EyesShutBegan += OnEyesShutBegan;
            // A vent that comes out in her room (Vent_A into the Bathroom) climbs you out already facing her: you're in
            // the room, so the rules apply the moment the fade lifts.
            foreach (var vent in FindObjectsByType<Vent>())
                for (int k = 0; k < vent.exit.Length && k < vent.lookAt.Length; k++)
                    if (roomBounds.bounds.Contains(vent.exit[k] + Vector3.up * 0.5f)) vent.lookAt[k] = face;
            StartMusic();
        }

        // ------------------------------------------------------------------ every frame

        void Update()
        {
            var state = State;
            if (state == DollState.Killing || state == DollState.Done) return;
            if (WakeUpSequence.Asleep || (_health != null && _health.IsDead)) return;

            bool watched = IsWatched();
            if (state == DollState.Dormant && _returnHome && !watched) SnapHome(); // back on her chair while you weren't looking

            var e = _rule.Tick(InRoom(state), _inventory != null && _inventory.Has(keyItemId), watched, Time.unscaledDeltaTime);
            bool shutKill = false;
            if (e == DollEvent.None && Active && EyesHeldShut(out float closed))
            {
                e = _rule.EyesShut(closed);
                shutKill = e == DollEvent.Kill;
            }
            Handle(e, leap: !shutKill, waitForEyes: shutKill);

            if (Active) TrackHead(Time.unscaledDeltaTime);
        }

        /// <summary>A blink, called while the eyelids cover the screen (only blinks skip the world).</summary>
        public void Skip(float seconds)
        {
            if (!Active || WakeUpSequence.Asleep) return;
            Handle(_rule.Blink(), leap: false, waitForEyes: false);
        }

        void Handle(DollEvent e, bool leap, bool waitForEyes)
        {
            switch (e)
            {
                case DollEvent.Warned:
                    Lurch();
                    break;
                case DollEvent.Kill:
                    _kill = StartCoroutine(KillSequence(leap, waitForEyes));
                    break;
                case DollEvent.Escaped:
                    StopSound(ref _music, 1.5f); // she's just a doll now
                    break;
                case DollEvent.Disarmed:
                    SetMusicPitch(1f);
                    _returnHome = true;
                    break;
            }
        }

        bool InRoom(DollState state)
        {
            var b = roomBounds.bounds;
            // Once inside you're only out a margin past the walls, so dithering in the doorway doesn't count as leaving.
            if (state != DollState.Dormant) b.Expand(new Vector3(2f * exitMargin, 0f, 2f * exitMargin));
            return b.Contains(_player.transform.position + Vector3.up * 0.5f);
        }

        /// <summary>In the view cone, on screen, and nothing in the way.</summary>
        bool IsWatched()
        {
            if (Readable.Current != null) return false; // a note held up to your face is looking away
            Vector3 from = _cam.position, to = face.position - from;
            float distance = to.magnitude;
            if (distance < 1e-3f) return true;
            if (Vector3.Angle(_cam.forward, to) > watchAngle) return false;
            var vp = _camera.WorldToViewportPoint(face.position);
            if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) return false;

            const int notIgnoreRaycast = ~(1 << 2);
            int n = Physics.RaycastNonAlloc(from, to / distance, _hits, distance, notIgnoreRaycast, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var t = _hits[i].collider.transform;
                if (t.IsChildOf(_player.transform) || t.IsChildOf(doll) || (key != null && t.IsChildOf(key.transform))) continue;
                return false;
            }
            return true;
        }

        bool EyesHeldShut(out float closedSeconds)
        {
            closedSeconds = 0f;
            if (_detector == null || !_detector.EyesShut || TimeSkipController.Locked || GrabQte.BlocksInput)
            {
                if (_detector == null || !_detector.EyesShut) _shutBeganAt = -1f;
                return false;
            }
            if (_shutBeganAt < 0f) _shutBeganAt = Time.unscaledTime; // shut before we saw it begin (e.g. walked in eyes shut)
            closedSeconds = _detector.HoldThreshold + (Time.unscaledTime - _shutBeganAt);
            return true;
        }

        void OnEyesShutBegan() => _shutBeganAt = Time.unscaledTime;

        void TrackHead(float dt)
        {
            if (head == null) return;
            Vector3 look = _cam.position - head.position;
            if (look.sqrMagnitude < 1e-4f) return;
            float tilt = State == DollState.Warned ? warnedHeadTilt : 0f;
            var target = Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(0f, 0f, tilt);
            head.rotation = Quaternion.Slerp(head.rotation, target, 1f - Mathf.Exp(-headTurnSharpness * dt));
        }

        // ------------------------------------------------------------------ the warning

        /// <summary>Straight at the player along the floor, never closer than <see cref="lurchMinDistance"/>. Hidden by the lids.</summary>
        void Lurch()
        {
            Vector3 from = doll.position, flat = _player.transform.position - from;
            flat.y = 0f;
            float distance = flat.magnitude;
            Vector3 dir = distance > 1e-3f ? flat / distance : Vector3.ProjectOnPlane(doll.forward, Vector3.up).normalized;
            float step = Mathf.Min(warningLurch, Mathf.Max(0f, distance - lurchMinDistance));
            Vector3 to = from + dir * step;
            to.y = FloorAt(to);
            doll.SetPositionAndRotation(to, Quaternion.LookRotation(dir, Vector3.up));
            if (head != null)
            {
                Vector3 look = _cam.position - head.position;
                if (look.sqrMagnitude > 1e-4f)
                    head.rotation = Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(0f, 0f, warnedHeadTilt);
            }
            PlayAt(giggleCue, doll);
            if (_hiccup != null) StopCoroutine(_hiccup);
            _hiccup = StartCoroutine(MusicHiccup());
        }

        float FloorAt(Vector3 p)
        {
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 1.5f, Vector3.down, _hits, 3f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var t = _hits[i].collider.transform;
                if (t.IsChildOf(doll) || t.IsChildOf(_player.transform)) continue;
                if (_hits[i].point.y > best && _hits[i].point.y < p.y + 1.2f) best = _hits[i].point.y;
            }
            return float.IsNegativeInfinity(best) ? _floorY : best;
        }

        /// <summary>The music box skips a beat and comes back out of tune.</summary>
        IEnumerator MusicHiccup()
        {
            var audio = AudioService.Instance;
            if (audio == null || !audio.IsPlaying(_music)) yield break;
            audio.SetVolume(_music, 0f);
            yield return new WaitForSecondsRealtime(0.4f);
            if (State != DollState.Warned) yield break;
            SetMusicPitch(warnedMusicPitch);
            audio.SetVolume(_music, 1f);
        }

        // ------------------------------------------------------------------ the kill

        IEnumerator KillSequence(bool leap, bool waitForEyes)
        {
            _killed = true;
            WakeUpSequence.NextRespawnPrompt = respawnPrompt;
            _player.InputEnabled = false;
            TimeSkipController.Locked = true;
            if (TimeSkipController.Instance != null) TimeSkipController.Instance.InterruptFastForward();
            Carryable.DropHeld();
            if (PlayerWeapons.Instance != null) PlayerWeapons.Instance.Select(WeaponKind.Hands);
            if (_hiccup != null) StopCoroutine(_hiccup);
            if (head != null) head.localRotation = _headRest * Quaternion.Euler(0f, 0f, warnedHeadTilt);
            Vector3 faceLocal = doll.InverseTransformPoint(face.position);

            if (leap)
            {
                PlayAt(screamCue, doll);
                Vector3 start = face.position;
                for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / leapSeconds)
                {
                    float e = 1f - (1f - t) * (1f - t); // launches hard, settles onto the face
                    Vector3 end = _cam.position + _cam.forward * faceDistance;
                    Vector3 p = Vector3.Lerp(start, end, e) + Vector3.up * (Mathf.Sin(Mathf.PI * t) * leapArc);
                    PoseFaceAt(p, Quaternion.LookRotation(_cam.position - p, Vector3.up), faceLocal);
                    _player.TurnTowards(face.position, 14f, Time.unscaledDeltaTime);
                    yield return null;
                }
            }

            // On your face: parented to the camera so it can't drift, and every jolt shakes it with you.
            PoseFaceAt(_cam.position + _cam.forward * faceDistance, _cam.rotation * Quaternion.Euler(0f, 180f, 0f), faceLocal);
            doll.SetParent(_cam, true);
            StopSound(ref _music, 0f);
            _scratch = PlayLoop(scratchCue, doll);

            float held = 0f, openFor = 0f, nextJolt = 0f;
            while (true)
            {
                float dt = Time.unscaledDeltaTime;
                held += dt;
                bool closed = _detector != null && _detector.EyesClosed;
                openFor = closed ? 0f : openFor + dt;
                if (held >= nextJolt)
                {
                    nextJolt = held + joltEvery;
                    if (_cameraFeel != null) _cameraFeel.AddJolt(joltDegrees, 0.03f);
                    if (_haptics != null) _haptics.Thump(0.8f);
                }
                bool eyesDone = !waitForEyes || openFor >= faceHoldSeconds; // open your eyes onto her
                if ((held >= faceHoldSeconds && eyesDone) || held >= maxFaceHoldSeconds) break;
                yield return null;
            }

            if (_health != null) _health.TakeDamage(_health.Current, face.position, _cam.forward);
            StopSound(ref _scratch, 1.5f);
            _kill = null;
            // WakeUpSequence takes it from here (slump, black, back to bed) and raises Respawned, which resets us.
            if (_health == null || !_health.IsDead)
            {
                Debug.LogWarning("[LilysDoll] The kill didn't kill the player; resetting.", this);
                _player.InputEnabled = true;
                TimeSkipController.Locked = false;
                WakeUpSequence.NextRespawnPrompt = null;
                OnRespawned();
            }
        }

        /// <summary>Moves and turns the doll so its face anchor lands on <paramref name="facePoint"/>.</summary>
        void PoseFaceAt(Vector3 facePoint, Quaternion rotation, Vector3 faceLocal)
        {
            doll.rotation = rotation;
            doll.position += facePoint - doll.TransformPoint(faceLocal);
        }

        // ------------------------------------------------------------------ reset

        void OnRespawned()
        {
            if (State == DollState.Done) return;
            if (_kill != null) StopCoroutine(_kill);
            _kill = null;
            SnapHome();
            if (_killed) ResetKey(); // a death elsewhere leaves a key you dropped where it is
            _killed = false;
            _rule.Reset();
            StopSound(ref _scratch, 0f);
            if (!IsPlaying(_music)) StartMusic();
            SetMusicPitch(1f);
        }

        void SnapHome()
        {
            _returnHome = false;
            doll.SetParent(_dollParent, false);
            doll.SetLocalPositionAndRotation(_dollHomePos, _dollHomeRot);
            if (head != null) head.localRotation = _headRest;
        }

        /// <summary>
        /// The key goes back in her lap. Pickup remembers where it was taken from in world space, which is wrong if
        /// it was taken after a lurch, so put it back by hand (after WakeUpSequence has returned it).
        /// </summary>
        void ResetKey()
        {
            if (key == null || key.Taken || _keyParent == null) return;
            key.transform.SetParent(_keyParent, false);
            key.transform.SetLocalPositionAndRotation(_keyHomePos, _keyHomeRot);
            key.transform.localScale = _keyHomeScale;
        }

        // ------------------------------------------------------------------ audio

        void StartMusic() => _music = PlayLoop(musicCue, doll);

        void SetMusicPitch(float pitch)
        {
            var source = AudioService.Instance != null ? AudioService.Instance.SourceOf(_music) : null;
            if (source != null) source.pitch = pitch;
        }

        static AudioHandle PlayLoop(SoundCue cue, Transform at) =>
            cue != null && AudioService.Instance != null ? AudioService.Instance.PlayLoop(cue, at) : AudioHandle.None;

        static void PlayAt(SoundCue cue, Transform at)
        {
            if (cue != null && AudioService.Instance != null) AudioService.Instance.PlayAttached(cue, at);
        }

        static bool IsPlaying(AudioHandle handle) => AudioService.Instance != null && AudioService.Instance.IsPlaying(handle);

        static void StopSound(ref AudioHandle handle, float fade)
        {
            if (AudioService.Instance != null) AudioService.Instance.Stop(handle, fade);
            handle = AudioHandle.None;
        }

        void OnDrawGizmosSelected()
        {
            if (roomBounds == null) return;
            var b = roomBounds.bounds;
            Gizmos.color = new Color(1f, 0.3f, 0.5f, 0.6f);
            Gizmos.DrawWireCube(b.center, b.size);
            b.Expand(new Vector3(2f * exitMargin, 0f, 2f * exitMargin));
            Gizmos.color = new Color(1f, 0.3f, 0.5f, 0.2f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
