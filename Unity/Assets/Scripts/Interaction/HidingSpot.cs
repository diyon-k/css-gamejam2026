using System.Collections;
using Blink.Combat;
using Blink.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Interaction
{
    /// <summary>
    /// A wardrobe / cupboard you can climb into. Interact: the doors swing open, the player hops in in an arc,
    /// turns to face out and the doors pull to, left ajar to peek through (mouse look, clamped). Interact or Jump
    /// hops back out. While someone is inside, <see cref="Occupant"/> is set and zombies can't see the player.
    /// Put it on the furniture root; local +z faces out of the doors, local y 0 is the floor.
    /// </summary>
    public sealed class HidingSpot : MonoBehaviour, IInteractable
    {
        public string noun = "wardrobe";
        [Tooltip("Where the eye sits inside, local space.")]
        public Vector3 eye = new Vector3(0f, 1.2f, 0.05f);
        [Tooltip("Where you land when you climb out, local floor point.")]
        public Vector3 exit = new Vector3(0f, 0f, 1f);
        public float hopSeconds = 0.45f;
        public float hopHeight = 0.35f;
        [Tooltip("How far each door stays open while you hide, degrees.")]
        public float ajarDegrees = 9f;
        [Tooltip("How far you can turn your head left/right inside.")]
        public float maxYaw = 60f;

        /// <summary>The spot the player is hiding in, or null. Zombies treat the player as unseen while set.</summary>
        public static HidingSpot Occupant { get; private set; }

        Openable[] _doors;
        PlayerController _player;
        CharacterController _cc;
        Health _health;
        Coroutine _move;
        InputAction _look, _interact, _jump;
        float _eyeAboveBody, _bodyAboveFloor;

        void Awake() => _doors = GetComponentsInChildren<Openable>();

        public string Prompt(Interactor who) => CanInteract(who) ? "Hide in " + noun : null;
        public bool CanInteract(Interactor who) => Occupant == null && who.Player != null;
        public void Interact(Interactor who) => _move = StartCoroutine(GetIn(who));

        IEnumerator GetIn(Interactor who)
        {
            _player = who.Player;
            _cc = _player.GetComponent<CharacterController>();
            _health = _player.GetComponent<Health>();
            Occupant = this;
            _player.InputEnabled = false;
            if (_cc != null) _cc.enabled = false;
            if (_health != null) _health.Died += Abandon;

            var actions = InputSystem.actions;
            _look = actions?.FindAction("Player/Look");
            _interact = actions?.FindAction("Player/Interact");
            _jump = actions?.FindAction("Player/Jump");

            var body = _player.transform;
            _eyeAboveBody = who.Eye.position.y - body.position.y;
            _bodyAboveFloor = body.position.y - transform.position.y;

            SetDoors(true);
            yield return Wait(0.25f);
            // Body sinks so the camera ends up at `eye`: no crouch needed, the capsule is off anyway.
            var inside = transform.TransformPoint(eye) - Vector3.up * _eyeAboveBody;
            yield return Hop(body.position, inside, body.rotation, Quaternion.LookRotation(transform.forward));
            _player.AddLook(0f, -_player.Pitch); // level gaze through the gap

            SetDoors(false);
            foreach (var d in _doors) while (d != null && d.enabled) yield return null; // Openable drives the hinge until it's shut
            foreach (var d in _doors)
                if (d != null && d.motion == Openable.Motion.Hinged)
                    d.transform.localRotation *= Quaternion.Euler(0f, Mathf.Sign(d.openAngle) * ajarDegrees, 0f);
            _move = null;
        }

        IEnumerator GetOut()
        {
            var body = _player.transform;
            SetDoors(true);
            yield return Wait(0.25f);
            var outside = transform.TransformPoint(exit) + Vector3.up * _bodyAboveFloor;
            yield return Hop(body.position, outside, body.rotation, body.rotation);
            Release();
        }

        void Update()
        {
            if (Occupant != this || _move != null) return;
            if ((_interact != null && _interact.WasPressedThisFrame()) || (_jump != null && _jump.WasPressedThisFrame()))
            {
                _move = StartCoroutine(GetOut());
                return;
            }
            if (_look == null) return;
            Vector2 delta = _look.ReadValue<Vector2>();
            // ponytail: copies PlayerController's default sensitivities; expose them there if they get tuned.
            delta *= _look.activeControl?.device is Gamepad ? 180f * Time.unscaledDeltaTime : 0.1f;
            float yaw = Vector3.SignedAngle(transform.forward, _player.transform.forward, Vector3.up);
            _player.AddLook(Mathf.Clamp(yaw + delta.x, -maxYaw, maxYaw) - yaw, -delta.y);
        }

        /// <summary>Lerp with a sine arc on top, on real time like all player movement.</summary>
        IEnumerator Hop(Vector3 from, Vector3 to, Quaternion fromRot, Quaternion toRot)
        {
            var body = _player.transform;
            for (float t = 0f; t < 1f; t += PlayerController.RealDeltaTime(Time.deltaTime, Time.timeScale) / hopSeconds)
            {
                float e = Mathf.SmoothStep(0f, 1f, t);
                body.SetPositionAndRotation(Vector3.Lerp(from, to, e) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * hopHeight),
                                            Quaternion.Slerp(fromRot, toRot, e));
                yield return null;
            }
            body.SetPositionAndRotation(to, toRot);
        }

        static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += PlayerController.RealDeltaTime(Time.deltaTime, Time.timeScale)) yield return null;
        }

        void SetDoors(bool open)
        {
            foreach (var d in _doors) if (d != null) d.SetOpen(open);
        }

        void Release()
        {
            if (_health != null) _health.Died -= Abandon;
            if (_cc != null) _cc.enabled = true;
            if (!(_health is { IsDead: true })) _player.InputEnabled = true;
            Occupant = null;
            _move = null;
        }

        // Killed inside somehow: hand the body back to the respawn flow, don't leave zombies blind.
        void Abandon()
        {
            if (_move != null) StopCoroutine(_move);
            Release();
        }

        void OnDisable()
        {
            if (Occupant == this) Abandon();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.TransformPoint(eye), 0.1f);
            Gizmos.DrawWireSphere(transform.TransformPoint(exit), 0.15f);
        }
    }
}
