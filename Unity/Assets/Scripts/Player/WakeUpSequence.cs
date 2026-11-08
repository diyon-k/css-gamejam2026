using System;
using Blink.Combat;
using Blink.Detection;
using Blink.Effects;
using Blink.Interaction;
using Blink.TimeSkip;
using Blink.UI;
using UnityEngine;

namespace Blink.Player
{
    /// <summary>
    /// Game start. Black screen with the title and the world frozen. The title's letters arrive and it blinks now and
    /// then; under it a red segment sweeps with "starting the camera" until the webcam sees a face, then "BLINK to begin"
    /// comes in (blinks don't count until then). The first webcam blink (or
    /// eyes-shut) slowly opens the eyelids on the bedroom while the player sits up from lying on the bed, then hands
    /// over control and starts world time. Lives on GameSystems next to <see cref="BlinkEyelids"/>.
    ///
    /// Also the respawn: when the player dies they slump to the floor as the screen fades to black, then
    /// wake in the same bed the same way. World progress (doors, dead zombies, weapons) stays; an item in
    /// the hand is lost and goes back to where it was picked up.
    /// </summary>
    public sealed class WakeUpSequence : MonoBehaviour
    {
        [Tooltip("Eyelid closure (1 = shut, 0 = open) over real seconds after the first blink. Dips are heavy-eyed flutters.")]
        [SerializeField] AnimationCurve lids = new(
            new Keyframe(0f, 1f), new Keyframe(1.4f, 0.6f), new Keyframe(2.1f, 0.97f),
            new Keyframe(3.4f, 0.35f), new Keyframe(3.9f, 0.7f), new Keyframe(5.2f, 0f));
        [Tooltip("Real seconds from the first blink until the player is sitting up and in control.")]
        [SerializeField, Min(0.1f)] float seconds = 6.5f;
        [Tooltip("Second the player starts sitting up.")]
        [SerializeField, Min(0f)] float sitUpAt = 3f;
        [Tooltip("Camera pitch while lying down (negative looks up at the ceiling).")]
        [SerializeField] float lyingPitch = -70f;
        [Tooltip("Camera roll while lying down, head tipped on the pillow.")]
        [SerializeField] float lyingRoll = 12f;
        [Tooltip("Camera offset while lying down, in the player's local space (e.g. down and sideways onto the mattress).")]
        [SerializeField] Vector3 lyingOffset = new(0f, -0.9f, 0f);
        [SerializeField] string prompt = "BLINK to begin";
        [Tooltip("Large title above a one-line prompt. A two-line prompt uses its first line as the heading instead.")]
        [SerializeField] string title = "BLINK";
        [Tooltip("Real seconds for the prompt screen to fade in, and to fade out once you blink.")]
        [SerializeField, Min(0.01f)] float promptFadeIn = 1.4f;
        [SerializeField, Min(0.01f)] float promptFadeOut = 0.8f;

        [Header("Title motion")]
        [Tooltip("Real seconds for the title's letters to arrive one by one.")]
        [SerializeField, Min(0.01f)] float titleRevealSeconds = 1.8f;
        [Tooltip("Real seconds between the title's own blinks (picked at random in this range).")]
        [SerializeField] Vector2 titleBlinkEvery = new(4f, 8f);
        [Tooltip("Real seconds after the webcam first sees a face before a blink counts, so the prompt is on screen first.")]
        [SerializeField, Min(0f)] float readyGrace = 0.6f;
        [Tooltip("Shown under the title until the webcam is sending.")]
        [SerializeField] string startingStatus = "starting the camera";
        [Tooltip("Shown under the title while the webcam is sending but can't see a face.")]
        [SerializeField] string noFaceStatus = "looking for your face";

        [Header("Death")]
        [Tooltip("Real seconds from dying to fully black.")]
        [SerializeField, Min(0.1f)] float knockSeconds = 2f;
        [Tooltip("Camera drop while slumping to the floor, in the player's local space.")]
        [SerializeField] Vector3 knockOffset = new(0f, -1.2f, 0f);
        [Tooltip("Camera roll at the end of the slump, head on the floor.")]
        [SerializeField] float knockRoll = 75f;
        [SerializeField] string respawnPrompt = "You were knocked out\nBLINK to wake up";

        /// <summary>True from lying down in bed until fully sat up and in control (the HUD hides the crosshair).</summary>
        public static bool Asleep { get; private set; }

        /// <summary>
        /// Raised once the player has been teleported back to bed and healed after a death, while the screen is black.
        /// Carried items are already back where they were found. Puzzles reset themselves here.
        /// </summary>
        public static event Action Respawned;

        /// <summary>Overrides the next respawn screen's text once (e.g. "Lily wanted to play\nBLINK to wake up"), then clears.</summary>
        public static string NextRespawnPrompt { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Asleep = false;
            Respawned = null;
            NextRespawnPrompt = null;
        }

        BlinkEyelids _eyelids;
        PlayerController _player;
        Transform _cam;
        Vector3 _camPos;
        Quaternion _camRot;
        BlinkDetector _detector;
        float _startedAt = -1f; // -1 = still waiting for the first blink
        float _promptAt; // real time the current prompt screen appeared
        Health _health;
        Inventory _inventory;
        Vector3 _bedPos, _diedPos;
        Quaternion _bedRot, _diedRot;
        float _diedAt = -1f; // -1 = alive
        float _readyAt = -1f; // real time the webcam first saw a face on this prompt screen; -1 = still loading
        string _status; // loading line, frozen once ready so it doesn't change while fading out
        float _titleBlinkAt; // real time of the title's next (or current) idle blink

        void Start()
        {
            _eyelids = FindAnyObjectByType<BlinkEyelids>();
            _player = FindAnyObjectByType<PlayerController>();
            // Pose the pitch pivot above the camera, not the camera: CameraFeel rewrites the camera's own pose every frame.
            var cam = _player != null ? _player.GetComponentInChildren<Camera>() : null;
            _cam = cam == null ? null : cam.transform.parent != _player.transform ? cam.transform.parent : cam.transform;
            if (_cam != null) { _camPos = _cam.localPosition; _camRot = _cam.localRotation; }
            if (_player != null)
            {
                _bedPos = _player.transform.position;
                _bedRot = _player.transform.rotation;
                _health = _player.GetComponent<Health>();
                _inventory = _player.GetComponent<Inventory>();
                if (_health != null) _health.Died += OnDied;
            }
            Sleep();
        }

        /// <summary>Lying in bed, eyes shut, world frozen, waiting for a blink.</summary>
        void Sleep()
        {
            _startedAt = -1f;
            _promptAt = Time.unscaledTime;
            _readyAt = -1f;
            _status = startingStatus;
            _titleBlinkAt = _promptAt + titleRevealSeconds + 1.5f;
            enabled = true;
            Time.timeScale = 0f;
            TimeSkipController.Locked = true;
            if (_player != null) _player.InputEnabled = false;
            if (_eyelids != null) _eyelids.Hold01 = 1f;
            ShowHands(false); // nothing in hand until we're up
            Asleep = true;
            Pose(0f);

            _detector = BlinkDetector.Instance;
            if (_detector == null) { _readyAt = Time.unscaledTime - readyGrace; Begin(); return; }
            _detector.Blinked += Begin;
            _detector.EyesShutEnded += BeginAfterHold;
        }

        void OnDestroy()
        {
            Asleep = false;
            Unsubscribe();
            if (_health != null) _health.Died -= OnDied;
        }

        void OnDied()
        {
            _diedAt = Time.unscaledTime;
            enabled = true;
            TimeSkipController.Locked = true;
            Carryable.DropHeld();
            if (_cam != null) { _diedPos = _cam.localPosition; _diedRot = _cam.localRotation; }
        }

        /// <summary>Slump to the floor while the lids close; once black, back to bed.</summary>
        void KnockOut()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _diedAt) / knockSeconds);
            float slump = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t * 1.5f));
            if (_eyelids != null) _eyelids.Hold01 = t * t;
            if (_cam != null)
            {
                _cam.localPosition = _diedPos + knockOffset * slump;
                _cam.localRotation = _diedRot * Quaternion.Euler(0f, 0f, knockRoll * slump);
            }
            if (t < 1f) return;

            _diedAt = -1f;
            prompt = string.IsNullOrEmpty(NextRespawnPrompt) ? respawnPrompt : NextRespawnPrompt;
            NextRespawnPrompt = null;
            DropItems();
            _player.Teleport(_bedPos, _bedRot);
            _health.ResetHealth();
            if (FindAnyObjectByType<Blink.Mobs.ClownSpawner>() is { } spawner) spawner.Restart(); // before the reset: spawned clowns go
            foreach (var mob in FindObjectsByType<Blink.Mobs.MobController>()) mob.ResetToSpawn();
            Respawned?.Invoke();
            if (FindAnyObjectByType<GamepadHaptics>() is { } haptics) haptics.enabled = true; // it switches off on death
            Sleep();
        }

        /// <summary>Empties the hand and inventory and puts each item back where it was picked up.</summary>
        void DropItems()
        {
            if (Hand.Instance != null) Hand.Instance.ReturnItemHome();
            if (_inventory == null) return;
            var pickups = FindObjectsByType<Pickup>(FindObjectsInactive.Include);
            foreach (var id in new System.Collections.Generic.List<string>(_inventory.Items))
            {
                var spot = Array.Find(pickups, p => p.Taken && !p.gameObject.activeSelf && p.itemId == id);
                if (spot != null) spot.ReturnHome();
                _inventory.Remove(id);
            }
        }

        void Unsubscribe()
        {
            if (_detector == null) return;
            _detector.Blinked -= Begin;
            _detector.EyesShutEnded -= BeginAfterHold;
        }

        void BeginAfterHold(float _) => Begin();

        /// <summary>The webcam has seen a face for long enough that "blink to begin" is on screen.</summary>
        bool CanBegin => _readyAt >= 0f && Time.unscaledTime - _readyAt >= readyGrace;

        void Begin()
        {
            if (_startedAt >= 0f || !CanBegin) return;
            Unsubscribe();
            _startedAt = Time.unscaledTime;
        }

        /// <summary>
        /// Loading until the webcam is sending and sees a face, then ready (the prompt comes in). Ready stays put if the
        /// face is lost, but goes back to loading if the webcam stops sending.
        /// </summary>
        void UpdateReadiness()
        {
            if (_detector == null) return;
            if (_readyAt < 0f)
            {
                if (_detector.CameraRunning && _detector.FaceInView) _readyAt = Time.unscaledTime;
                else _status = _detector.CameraRunning ? noFaceStatus : startingStatus;
            }
            else if (!_detector.CameraRunning) _readyAt = -1f;
        }

        void Update()
        {
            if (_diedAt >= 0f) { KnockOut(); return; }
            if (_startedAt < 0f)
            {
                UpdateReadiness();
                if (Time.unscaledTime > _titleBlinkAt + TitleBlinkSeconds)
                    _titleBlinkAt = Time.unscaledTime + UnityEngine.Random.Range(titleBlinkEvery.x, titleBlinkEvery.y);
                return;
            }
            float t = Time.unscaledTime - _startedAt;
            if (_eyelids != null) _eyelids.Hold01 = Mathf.Clamp01(lids.Evaluate(t));
            Pose(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(sitUpAt, seconds, t)));
            if (t >= seconds) Finish();
        }

        /// <summary>0 = lying on the bed looking at the ceiling, 1 = sitting up (the camera's normal pose).</summary>
        void Pose(float up)
        {
            if (_cam == null) return;
            _cam.localPosition = _camPos + lyingOffset * (1f - up);
            _cam.localRotation = _camRot * Quaternion.Euler(lyingPitch * (1f - up), 0f, lyingRoll * (1f - up));
        }

        void Finish()
        {
            Pose(1f);
            if (_eyelids != null) _eyelids.Hold01 = null;
            Time.timeScale = 1f;
            TimeSkipController.Locked = false;
            if (_player != null) _player.InputEnabled = true;
            ShowHands(true);
            Asleep = false;
            enabled = false;
        }

        /// <summary>Shows or hides the view model in hand (hands or weapon) by toggling the weapons holster.</summary>
        static void ShowHands(bool show)
        {
            if (PlayerWeapons.Instance is { Holster: { } holster }) holster.gameObject.SetActive(show);
        }

        const float TitleBlinkSeconds = 0.3f;

        /// <summary>The title's own blink, <paramref name="t"/> real seconds in: 0 open, 1 shut. Close, hold, open.</summary>
        static float TitleLid(float t)
        {
            if (t < 0f || t >= TitleBlinkSeconds) return 0f;
            if (t < 0.08f) return BlinkTheme.EaseInOut(t / 0.08f);
            if (t < 0.14f) return 1f;
            return 1f - BlinkTheme.EaseOut((t - 0.14f) / 0.16f);
        }

        /// <summary>How strongly the loading look shows <paramref name="t"/> real seconds into the prompt screen.</summary>
        static float LoadingIn(float t) => BlinkTheme.EaseInOut((t - 0.8f) / 0.6f);

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _diedAt >= 0f) return;
            float now = Time.unscaledTime;
            float shown = now - _promptAt;
            float alpha = _startedAt < 0f
                ? BlinkTheme.EaseInOut(shown / promptFadeIn)
                : 1f - BlinkTheme.EaseInOut((now - _startedAt) / promptFadeOut);
            if (alpha <= 0f) return;
            GUI.depth = -1001; // above the eyelids (-1000)

            // One line ("BLINK to begin"): the title over it in small caps. Two lines
            // ("You were knocked out" / "BLINK to wake up"): the first line is the heading.
            int nl = prompt.IndexOf('\n');
            bool large = nl < 0;
            string heading = large ? title : prompt.Substring(0, nl).Trim();
            string sub = large ? prompt : prompt.Substring(nl + 1).Replace('\n', ' ').Trim();

            // Letters arrive; then a red segment sweeps under the title with a status line until the webcam sees a face.
            // Ready: the sweep fades, the rule widens out and the prompt comes in a letter at a time. The title blinks
            // now and then by itself, and shuts for good when the player blinks.
            bool ready = _readyAt >= 0f;
            float sinceReady = ready ? now - _readyAt : 0f;
            float loading = ready
                ? LoadingIn(_readyAt - _promptAt) * (1f - BlinkTheme.EaseOut(sinceReady / 0.35f))
                : LoadingIn(shown);
            var motion = new BlinkWidgets.TitleMotion
            {
                reveal = shown / titleRevealSeconds,
                lid = _startedAt >= 0f ? BlinkTheme.EaseInOut((now - _startedAt) / 0.12f) : TitleLid(now - _titleBlinkAt),
                rule = ready ? BlinkTheme.EaseOut((sinceReady - 0.15f) / 0.6f) : 0f,
                loading = loading,
                time = now,
                status = _status,
                statusAlpha = loading * (0.75f + 0.25f * Mathf.Sin(now * 2f)),
                subtitleReveal = ready ? (sinceReady - 0.45f) / 0.9f : 0f,
            };
            float breathe = 0.7f + 0.3f * Mathf.Sin(now * 2f); // ~0.3 Hz
            BlinkWidgets.TitleCard(heading, sub, alpha, breathe, large, motion);
        }
    }
}
