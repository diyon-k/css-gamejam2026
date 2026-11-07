using System;
using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// A door, cupboard door or drawer that toggles open/closed with a short animation (real time).
    /// Hinged: put this on a pivot at the hinge edge; it turns <see cref="openAngle"/> around local up.
    /// Sliding: moves by <see cref="slide"/> (parent space) when open.
    /// Set <see cref="requiredItemId"/> to lock it until the player carries that item; unlocking is permanent.
    /// </summary>
    public sealed class Openable : MonoBehaviour, IInteractable
    {
        public enum Motion { Hinged, Sliding }

        public Motion motion = Motion.Hinged;
        public string noun = "cupboard";
        public float openAngle = 100f;
        public Vector3 slide = new Vector3(0f, 0f, 0.35f);
        public float seconds = 0.35f;
        public string requiredItemId = "";

        /// <summary>Fired on every toggle with the new state.</summary>
        public event Action<bool> Toggled;
        public bool IsOpen { get; private set; }
        public bool IsLocked => !string.IsNullOrEmpty(requiredItemId);

        Vector3 _closedPos;
        Quaternion _closedRot;
        float _t;

        void Awake()
        {
            _closedPos = transform.localPosition;
            _closedRot = transform.localRotation;
        }

        public string Prompt(Interactor who) =>
            !CanInteract(who) ? $"The {noun} is locked"
            : IsLocked ? $"Unlock {noun}"
            : (IsOpen ? "Close " : "Open ") + noun;

        public bool CanInteract(Interactor who) => !IsLocked || (who.Inventory != null && who.Inventory.Has(requiredItemId));

        public void Interact(Interactor who)
        {
            requiredItemId = "";
            SetOpen(!IsOpen);
        }

        /// <summary>Open or close from code (e.g. a puzzle opening a secret door). Ignores the lock.</summary>
        public void SetOpen(bool open)
        {
            if (open == IsOpen) return;
            IsOpen = open;
            enabled = true;
            Toggled?.Invoke(open);
        }

        void Update()
        {
            float goal = IsOpen ? 1f : 0f;
            _t = Mathf.MoveTowards(_t, goal, Time.unscaledDeltaTime / seconds);
            float e = Mathf.SmoothStep(0f, 1f, _t);
            if (motion == Motion.Hinged) transform.localRotation = _closedRot * Quaternion.Euler(0f, openAngle * e, 0f);
            else transform.localPosition = _closedPos + slide * e;
            if (_t == goal) enabled = false;
        }
    }
}
