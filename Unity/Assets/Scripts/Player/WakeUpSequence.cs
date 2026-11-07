using Blink.Detection;
using Blink.Effects;
using Blink.TimeSkip;
using UnityEngine;

namespace Blink.Player
{
    /// <summary>
    /// Game start. Black screen with "BLINK to begin" and the world frozen. The first blink (or Space)
    /// slowly opens the eyelids on the bedroom while the player sits up from lying on the bed, then hands
    /// over control and starts world time. Lives on GameSystems next to <see cref="BlinkEyelids"/>.
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

        BlinkEyelids _eyelids;
        PlayerController _player;
        Transform _cam;
        Vector3 _camPos;
        Quaternion _camRot;
        BlinkDetector _detector;
        float _startedAt = -1f; // -1 = still waiting for the first blink
        GUIStyle _style;

        void Start()
        {
            _eyelids = FindAnyObjectByType<BlinkEyelids>();
            _player = FindAnyObjectByType<PlayerController>();
            // Pose the pitch pivot above the camera, not the camera: CameraFeel rewrites the camera's own pose every frame.
            var cam = _player != null ? _player.GetComponentInChildren<Camera>() : null;
            _cam = cam == null ? null : cam.transform.parent != _player.transform ? cam.transform.parent : cam.transform;
            if (_cam != null) { _camPos = _cam.localPosition; _camRot = _cam.localRotation; }

            Time.timeScale = 0f;
            TimeSkipController.Locked = true;
            if (_player != null) _player.InputEnabled = false;
            if (_eyelids != null) _eyelids.Hold01 = 1f;
            Pose(0f);

            _detector = BlinkDetector.Instance;
            if (_detector == null) { Begin(); return; }
            _detector.Blinked += Begin;
            _detector.EyesShutEnded += BeginAfterHold;
        }

        void OnDestroy() => Unsubscribe();

        void Unsubscribe()
        {
            if (_detector == null) return;
            _detector.Blinked -= Begin;
            _detector.EyesShutEnded -= BeginAfterHold;
        }

        void BeginAfterHold(float _) => Begin();

        void Begin()
        {
            if (_startedAt >= 0f) return;
            Unsubscribe();
            _startedAt = Time.unscaledTime;
        }

        void Update()
        {
            if (_startedAt < 0f) return;
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
            enabled = false;
        }

        void OnGUI()
        {
            if (_startedAt >= 0f || Event.current.type != EventType.Repaint) return;
            GUI.depth = -1001; // above the eyelids (-1000)
            _style ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Screen.height * 0.05f),
                fontStyle = FontStyle.Bold,
            };
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 2f);
            var old = GUI.color;
            GUI.color = new Color(0.85f, 0.85f, 0.85f, pulse);
            GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), prompt, _style);
            GUI.color = old;
        }
    }
}
