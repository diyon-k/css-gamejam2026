using System;
using Blink.Interaction;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.World
{
    /// <summary>
    /// Hold Interact (E) near the door to force it. Progress pauses when you let go or walk away,
    /// and blinking skips it forward. While held it strains against the frame; when done it bursts open
    /// on its hinge edge, swinging past and settling back (real time).
    /// </summary>
    public sealed class JammedDoor : MonoBehaviour, ISkippable
    {
        [SerializeField] WorldTimer forceTimer = new WorldTimer(4f);
        [SerializeField] float reach = 2.5f;
        [SerializeField] float openAngle = 100f;

        [Header("Feel (real time)")]
        [Tooltip("Strain judder at the start of forcing; grows to strainMaxDegrees as it nears giving way.")]
        [SerializeField] float strainMinDegrees = 0.6f;
        [SerializeField] float strainMaxDegrees = 3f;
        [SerializeField] float strainHz = 12f;
        [SerializeField, Min(0.01f)] float burstSeconds = 0.45f;
        [Tooltip("Ease-out-back strength of the burst (see Openable.Ease).")]
        [SerializeField, Min(0f)] float burstOvershoot = 1.6f;

        /// <summary>Fired once when the door gives way.</summary>
        public event System.Action Opened;
        public bool IsForcing => forceTimer.IsRunning;
        public float Progress01 => forceTimer.Progress01;

        [Header("Noise (audible range in metres)")]
        [SerializeField, Min(0f)] float forcingLoudness = 10f;
        [SerializeField, Min(0.1f)] float forcingNoiseInterval = 1f;
        [SerializeField, Min(0f)] float burstOpenLoudness = 20f;

        /// <summary>The player started holding Interact on the door (for the creak loop).</summary>
        public event Action ForcingStarted;
        /// <summary>The player let go or walked away, or the door burst open (raised before <see cref="Opened"/>).</summary>
        public event Action ForcingStopped;

        InputAction _interact;
        Transform _player;
        bool _open, _inReach, _forcing;
        float _noiseTimer, _swing;
        Vector3 _hinge, _closedPos;
        Quaternion _closedRot;

        void Awake()
        {
            _interact = InputSystem.actions?.FindAction("Player/Interact");
            _closedPos = transform.position;
            _closedRot = transform.rotation;
            // Pivot on the door's left edge (local x = -0.5 on a unit cube scaled to the door).
            _hinge = transform.TransformPoint(new Vector3(-0.5f, 0f, 0f));
        }

        void OnEnable()
        {
            _interact?.Enable();
            forceTimer.Completed += Open;
            TimeSkipController.Register(this);
        }

        void OnDisable()
        {
            forceTimer.Completed -= Open;
            TimeSkipController.Unregister(this);
        }

        void Update()
        {
            if (_open)
            {
                if (_swing < 1f)
                {
                    _swing = Mathf.Min(1f, _swing + Time.unscaledDeltaTime / burstSeconds);
                    Pose(openAngle * Openable.Ease(_swing, burstOvershoot));
                }
                return;
            }
            if (_player == null && GameObject.FindWithTag("Player") is { } p) _player = p.transform;
            _inReach = _player != null && Vector3.Distance(_player.position, transform.position) <= reach;

            bool holding = _inReach && _interact != null && _interact.IsPressed();
            if (holding && !forceTimer.IsRunning) forceTimer.Resume();
            else if (!holding && forceTimer.IsRunning) forceTimer.Pause();
            SetForcing(holding);
            forceTimer.Advance(Time.deltaTime);
            if (_open) return; // gave way this frame

            // Shoved against the jamb: judders toward open, harder as it nears giving way.
            Pose(!holding ? 0f
                : Mathf.Lerp(strainMinDegrees, strainMaxDegrees, Progress01) * Mathf.PerlinNoise(Time.unscaledTime * strainHz, 0f));

            if (holding && (_noiseTimer -= Time.deltaTime) <= 0f)
            {
                Noise.Make(transform.position, forcingLoudness);
                _noiseTimer = forcingNoiseInterval;
            }
        }

        public void Skip(float seconds) => forceTimer.Advance(seconds);

        void SetForcing(bool forcing)
        {
            if (forcing == _forcing) return;
            _forcing = forcing;
            (forcing ? ForcingStarted : ForcingStopped)?.Invoke();
        }

        void Open()
        {
            _open = true;
            SetForcing(false);
            Noise.Make(transform.position, burstOpenLoudness);
            Opened?.Invoke();
        }

        /// <summary>Turn the door <paramref name="degrees"/> about its hinge from closed.</summary>
        void Pose(float degrees)
        {
            var q = Quaternion.AngleAxis(degrees, Vector3.up);
            transform.SetPositionAndRotation(_hinge + q * (_closedPos - _hinge), q * _closedRot);
        }

        // ponytail: IMGUI prompt, swap for real UI when the HUD exists.
        void OnGUI()
        {
            if (_open || !_inReach) return;
            string text = forceTimer.Elapsed > 0f
                ? $"Forcing door... {forceTimer.Progress01:P0}"
                : "Hold E to force the door";
            var r = new Rect(Screen.width / 2f - 150f, Screen.height * 0.7f, 300f, 30f);
            GUI.Label(r, text, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 });
        }
    }
}
