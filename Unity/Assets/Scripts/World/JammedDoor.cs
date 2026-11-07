using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.World
{
    /// <summary>
    /// Hold Interact (E) near the door to force it. Progress pauses when you let go or walk away,
    /// and blinking skips it forward. When done the door swings open on its hinge edge.
    /// </summary>
    public sealed class JammedDoor : MonoBehaviour, ISkippable
    {
        [SerializeField] WorldTimer forceTimer = new WorldTimer(4f);
        [SerializeField] float reach = 2.5f;
        [SerializeField] float openAngle = 100f;

        [Header("Noise (audible range in metres)")]
        [SerializeField, Min(0f)] float forcingLoudness = 10f;
        [SerializeField, Min(0.1f)] float forcingNoiseInterval = 1f;
        [SerializeField, Min(0f)] float burstOpenLoudness = 20f;

        InputAction _interact;
        Transform _player;
        bool _open, _inReach;
        float _noiseTimer;

        void Awake() => _interact = InputSystem.actions?.FindAction("Player/Interact");

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
            if (_open) return;
            if (_player == null && GameObject.FindWithTag("Player") is { } p) _player = p.transform;
            _inReach = _player != null && Vector3.Distance(_player.position, transform.position) <= reach;

            bool holding = _inReach && _interact != null && _interact.IsPressed();
            if (holding && !forceTimer.IsRunning) forceTimer.Resume();
            else if (!holding && forceTimer.IsRunning) forceTimer.Pause();
            forceTimer.Advance(Time.deltaTime);

            if (holding && (_noiseTimer -= Time.deltaTime) <= 0f)
            {
                Noise.Make(transform.position, forcingLoudness);
                _noiseTimer = forcingNoiseInterval;
            }
        }

        public void Skip(float seconds) => forceTimer.Advance(seconds);

        void Open()
        {
            _open = true;
            Noise.Make(transform.position, burstOpenLoudness);
            // Pivot on the door's left edge (local x = -0.5 on a unit cube scaled to the door).
            var hinge = transform.TransformPoint(new Vector3(-0.5f, 0f, 0f));
            transform.RotateAround(hinge, Vector3.up, openAngle);
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
