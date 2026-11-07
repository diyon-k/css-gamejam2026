using System.Collections.Generic;
using Blink.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Interaction
{
    /// <summary>
    /// On the Player. Looks along the camera for an <see cref="IInteractable"/> within reach, shows its
    /// prompt and calls it on an Interact press. Solid colliders block the look; trigger colliders without
    /// an interactable are looked through. Idle while <see cref="PlayerController.InputEnabled"/> is off.
    /// </summary>
    public sealed class Interactor : MonoBehaviour
    {
        [SerializeField] float reach = 2.2f;

        public Inventory Inventory { get; private set; }
        public PlayerController Player { get; private set; }
        /// <summary>What the player is looking at this frame, or null.</summary>
        public IInteractable Target { get; private set; }

        static readonly IComparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));
        readonly RaycastHit[] _hits = new RaycastHit[16];
        Transform _eye;
        InputAction _interact;
        GUIStyle _style;

        void Awake()
        {
            Inventory = GetComponent<Inventory>();
            Player = GetComponent<PlayerController>();
            _eye = GetComponentInChildren<Camera>() is { } cam ? cam.transform : transform;
            _interact = InputSystem.actions?.FindAction("Player/Interact");
        }

        void OnEnable() => _interact?.Enable();

        void Update()
        {
            bool canAct = (Player == null || Player.InputEnabled) && !GrabQte.Active;
            Target = canAct ? FindTarget() : null;
            // WasPressedThisFrame fires at the button's press point, so the action's Hold interaction
            // (used by JammedDoor) doesn't delay it: a tap interacts instantly.
            if (Target != null && _interact != null && _interact.WasPressedThisFrame() && Target.CanInteract(this))
                Target.Interact(this);
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

        // ponytail: IMGUI prompt like JammedDoor's, swap for real UI when the HUD exists.
        void OnGUI()
        {
            if (Target == null) return;
            string prompt = Target.Prompt(this);
            if (string.IsNullOrEmpty(prompt)) return;
            _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
            string text = Target.CanInteract(this) ? "E   " + prompt : prompt;
            GUI.Label(new Rect(Screen.width / 2f - 200f, Screen.height * 0.6f, 400f, 30f), text, _style);
        }
    }
}
