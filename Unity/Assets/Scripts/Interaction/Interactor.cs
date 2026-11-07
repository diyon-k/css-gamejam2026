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
    /// an interactable are looked through. Idle while <see cref="PlayerController.InputEnabled"/> is off.
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

        IInteractable FindTarget()
        {
            int n = Physics.RaycastNonAlloc(_eye.position, _eye.forward, _hits, reach, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(_hits, 0, n, ByDistance);
            for (int i = 0; i < n; i++)
            {
                var col = _hits[i].collider;
                if (col.transform.IsChildOf(transform)) continue; // our own capsule / gun
                if (col.GetComponentInParent<IInteractable>() is { } it) return it;
                if (!col.isTrigger) return null;
            }
            return null;
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
