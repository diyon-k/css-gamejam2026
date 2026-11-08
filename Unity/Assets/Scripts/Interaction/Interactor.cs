using System.Collections.Generic;
using Blink.Player;
using Blink.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Blink.Interaction
{
    /// <summary>
    /// On the Player. Looks along the camera for an <see cref="IInteractable"/> within reach, shows its
    /// prompt (in the <see cref="BlinkPrompts"/> stack) and calls it on an Interact press. Solid colliders block the look; trigger colliders without
    /// an interactable are looked through; something lying inside the thing in focus (a key in an open drawer) wins; a near
    /// miss on something small is caught by a thin aim-assist sphere (<see cref="FindTarget(Vector3, Vector3, float, Transform, RaycastHit[], float)"/>).
    /// The target is outlined by <see cref="InteractHighlight"/>. Idle while <see cref="PlayerController.InputEnabled"/> is off.
    /// While a note is held up it stays the target, so Interact always puts it down, wherever you look.
    ///
    /// On a gamepad, X (West) is both Interact and Reload, as in most shooters: it interacts when something usable is in
    /// focus (this target, a jammed door in reach, getting out of a hiding spot), otherwise it reloads. Each press belongs to
    /// one side from the frame it goes down until it is released (<see cref="SharedPress"/>); reload and the door ask
    /// <see cref="InteractOwns"/> / <see cref="ReloadOwns"/>.
    /// </summary>
    public sealed class Interactor : MonoBehaviour
    {
        [SerializeField] float reach = 2.2f;

        public Inventory Inventory { get; private set; }
        /// <summary>The player's one hand: holds one thing at a time.</summary>
        public Hand Hand { get; private set; }
        public PlayerController Player { get; private set; }
        /// <summary>What the player is looking at this frame, or null.</summary>
        public IInteractable Target { get; private set; }
        /// <summary>The camera transform the look ray comes from.</summary>
        public Transform Eye => _eye;

        /// <summary>The player just used something (picked it up, opened it...). For the hands' reach.</summary>
        public event System.Action Interacted;

        static readonly IComparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));
        readonly RaycastHit[] _hits = new RaycastHit[16];
        Transform _eye;
        InputAction _interact;

        void Awake()
        {
            Inventory = GetComponent<Inventory>();
            Player = GetComponent<PlayerController>();
            _eye = GetComponentInChildren<Camera>() is { } cam ? cam.transform : transform;
            _interact = InputSystem.actions?.FindAction("Player/Interact");
            gameObject.AddComponent<PickupGlint>();
            gameObject.AddComponent<InteractHighlight>();
            Hand = GetComponent<Hand>();
            if (Hand == null) Hand = gameObject.AddComponent<Hand>();
        }

        void OnEnable() => _interact?.Enable();

        static int _focusFrame = -10, _westFrame = -1;
        static SharedPress _west;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _focusFrame = -10;
            _westFrame = -1;
            _west = default;
        }

        /// <summary>Something usable is in focus for Interact this frame (or the last), or the player is hidden and Interact gets out.</summary>
        public static bool HasFocus => _focusFrame >= Time.frameCount - 1 || HidingSpot.Occupant != null;

        /// <summary>Call every frame something outside the look ray offers an interaction (e.g. a jammed door in reach).</summary>
        public static void ReportFocus() => _focusFrame = Time.frameCount;

        /// <summary>Who the current gamepad West (X) press belongs to, decided once when it went down.</summary>
        public static PressOwner GamepadWestOwner
        {
            get
            {
                if (_westFrame != Time.frameCount)
                {
                    _westFrame = Time.frameCount;
                    var pad = Gamepad.current;
                    _west.Step(pad != null && pad.buttonWest.isPressed, HasFocus);
                }
                return _west.Owner;
            }
        }

        /// <summary>True if <paramref name="control"/> is the pad's West button and this press interacts: reload must ignore it.</summary>
        public static bool InteractOwns(InputControl control) => IsGamepadWest(control) && GamepadWestOwner == PressOwner.Interact;

        /// <summary>True if <paramref name="control"/> is the pad's West button and this press reloads: interacting must ignore it.</summary>
        public static bool ReloadOwns(InputControl control) => IsGamepadWest(control) && GamepadWestOwner != PressOwner.Interact;

        static bool IsGamepadWest(InputControl control) =>
            control is ButtonControl button && button.device is Gamepad pad && button == pad.buttonWest;

        void Update()
        {
            bool canAct = (Player == null || Player.InputEnabled) && !GrabQte.Active;
            // A carried prop stays the target so Interact always drops it, wherever it's swinging; a note held up stays
            // the target so Interact always puts it down, wherever you look.
            Target = !canAct ? null : Carryable.Held != null ? Carryable.Held : Readable.Current != null ? Readable.Current : FindTarget();
            bool usable = Target != null && Target.CanInteract(this);
            if (usable) ReportFocus();
            // WasPressedThisFrame fires at the button's press point, so the action's Hold interaction
            // (used by JammedDoor) doesn't delay it: a tap interacts instantly. A pad press that began as a reload doesn't.
            if (usable && _interact != null && _interact.WasPressedThisFrame() && !ReloadOwns(_interact.activeControl))
            {
                Target.Interact(this);
                Interacted?.Invoke();
            }
            ShowPrompt();
        }

        IInteractable FindTarget() => FindTarget(_eye.position, _eye.forward, reach, transform, _hits, AssistRadius);

        /// <summary>Radius of the aim assist: a look that just misses something small (a key) still finds it.</summary>
        public const float AssistRadius = 0.06f;
        /// <summary>How far past the first thing hit the look ray still looks for something lying inside it.</summary>
        const float ContentsDepth = 0.8f;

        /// <summary>
        /// What a look along <paramref name="direction"/> lands on. The first interactable along the ray, unless something
        /// interactable inside it is also on the ray with nothing solid between (a key lying in an open drawer: the key).
        /// Solid colliders block the look; triggers without an interactable are looked through. With nothing on the ray, a
        /// thin sphere (<paramref name="assistRadius"/>) catches a near miss nearer than the solid thing the look landed on,
        /// if nothing solid stands between it and the eye.
        /// <paramref name="ignore"/>'s colliders (the player's own) are skipped.
        /// </summary>
        public static IInteractable FindTarget(Vector3 origin, Vector3 direction, float reach, Transform ignore, RaycastHit[] buffer, float assistRadius)
        {
            int n = Physics.RaycastNonAlloc(origin, direction, buffer, reach, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(buffer, 0, n, ByDistance);
            IInteractable first = null;
            float firstAt = 0f, solidAt = reach;
            for (int i = 0; i < n; i++)
            {
                var col = buffer[i].collider;
                if (ignore != null && col.transform.IsChildOf(ignore)) continue; // our own capsule / gun
                var it = col.GetComponentInParent<IInteractable>();
                if (first == null)
                {
                    if (it != null) { first = it; firstAt = buffer[i].distance; continue; }
                    if (!col.isTrigger) { solidAt = buffer[i].distance; break; }
                    continue;
                }
                // something already in focus: a thing inside it (its child) wins, if nothing solid came first
                if (buffer[i].distance - firstAt > ContentsDepth) break;
                if (it != null && !ReferenceEquals(it, first) && IsInside(it, first)) return it;
                if (ReferenceEquals(it, first) || col.isTrigger) continue;
                break;
            }
            if (first != null || assistRadius <= 0f) return first;
            // the assist only finds things in front of whatever solid the look landed on, never behind or around it
            return AssistTarget(origin, direction, solidAt, ignore, buffer, assistRadius);
        }

        static bool IsInside(IInteractable inner, IInteractable outer) =>
            inner is Component a && outer is Component b && a.transform != b.transform && a.transform.IsChildOf(b.transform);

        static IInteractable AssistTarget(Vector3 origin, Vector3 direction, float reach, Transform ignore, RaycastHit[] buffer, float radius)
        {
            int n = Physics.SphereCastNonAlloc(origin, radius, direction, buffer, reach, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(buffer, 0, n, ByDistance);
            IInteractable found = null;
            Vector3 at = default;
            for (int i = 0; i < n; i++)
            {
                var col = buffer[i].collider;
                if (buffer[i].distance <= 0f || (ignore != null && col.transform.IsChildOf(ignore))) continue;
                if (col.GetComponentInParent<IInteractable>() is { } it) { found = it; at = buffer[i].point; break; }
            }
            if (found == null) return null;
            // only if the eye can see that point: nothing solid (other than the thing itself) in between
            var to = at - origin;
            int m = Physics.RaycastNonAlloc(origin, to.normalized, buffer, Mathf.Max(0f, to.magnitude - 0.01f), ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < m; i++)
            {
                var col = buffer[i].collider;
                if (ignore != null && col.transform.IsChildOf(ignore)) continue;
                if (!ReferenceEquals(col.GetComponentInParent<IInteractable>(), found)) return null;
            }
            return found;
        }

        /// <summary>The target's prompt, bottom-right: "[E] open door", or just the words when it can't be used.</summary>
        void ShowPrompt()
        {
            if (Target == null) return;
            string prompt = Target.Prompt(this);
            if (string.IsNullOrEmpty(prompt)) return;
            bool can = Target.CanInteract(this);
            BlinkPrompts.Show(this, can ? _interact : null, can ? "E" : null, prompt);
        }
    }
}
