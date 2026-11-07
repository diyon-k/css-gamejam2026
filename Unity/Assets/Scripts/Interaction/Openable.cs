using System;
using Blink.UI;
using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// A door, cupboard door or drawer that toggles open/closed with a short animation (real time).
    /// Hinged: put this on a pivot at the hinge edge; it turns <see cref="openAngle"/> around local up.
    /// Sliding: moves by <see cref="slide"/> (parent space) when open.
    /// Set <see cref="requiredItemId"/> to lock it until the player holds that item; unlocking is permanent and, with
    /// <see cref="consumesKey"/>, uses the key up (it leaves the hand). Trying it while locked without the item rattles it
    /// and says so ("The door is locked").
    /// A door leaf whose mesh pivot isn't on its hinge (a level kit's leaf, pivoted at the world origin) sets
    /// <see cref="useHingePoint"/> and turns about <see cref="hingePoint"/> instead.
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
        [Tooltip("Unlocking uses the key up: it leaves the hand.")]
        public bool consumesKey = true;
        [Tooltip("Real seconds the 'locked' line stays up after trying it without the key.")]
        [SerializeField, Min(0f)] float lockedMessageSeconds = 2.5f;

        [Header("Hinge (optional)")]
        [Tooltip("Hinged: turn about Hinge Point (world up axis) instead of this object's own pivot.")]
        public bool useHingePoint;
        [Tooltip("The hinge, in the parent's space.")]
        public Vector3 hingePoint;

        [Header("Feel (real time)")]
        [Tooltip("Wait before moving, so it starts as the hand arrives.")]
        [SerializeField, Min(0f)] float handDelay = 0.12f;
        [Tooltip("Ease-out-back strength when a hinged door opens: 0 = none, 1 = ~4% swing past then settle.")]
        [SerializeField, Min(0f)] float overshoot = 1f;
        [SerializeField, Min(0.01f)] float rattleSeconds = 0.35f;
        [SerializeField] float rattleHz = 14f;
        [Tooltip("How far a locked one knocks against its latch.")]
        [SerializeField] float rattleDegrees = 1.5f;
        [SerializeField] float rattleMetres = 0.008f;

        /// <summary>Fired on every toggle with the new state.</summary>
        public event Action<bool> Toggled;
        public bool IsOpen { get; private set; }
        public bool IsLocked => !string.IsNullOrEmpty(requiredItemId);

        Vector3 _closedPos, _closedWorldPos, _hingeWorld;
        Quaternion _closedRot, _closedWorldRot;
        float _e, _from, _p = 1f, _wait, _rattle, _message;

        void Awake()
        {
            _closedPos = transform.localPosition;
            _closedRot = transform.localRotation;
            transform.GetPositionAndRotation(out _closedWorldPos, out _closedWorldRot);
            _hingeWorld = transform.parent != null ? transform.parent.TransformPoint(hingePoint) : hingePoint;
        }

        bool HasKey(Interactor who) => !IsLocked || (who.Inventory != null && who.Inventory.Has(requiredItemId));

        public string Prompt(Interactor who) =>
            _message > 0f ? null // "The door is locked" is showing instead
            : !HasKey(who) ? "Open " + noun // try it to find out
            : IsLocked ? $"Unlock {noun}"
            : (IsOpen ? "Close " : "Open ") + noun;

        // Always usable: a locked one rattles, so the hands still reach for it.
        public bool CanInteract(Interactor who) => true;

        public void Interact(Interactor who)
        {
            if (!HasKey(who))
            {
                Rattle();
                _message = lockedMessageSeconds;
                return;
            }
            if (IsLocked)
            {
                string key = requiredItemId;
                requiredItemId = "";
                if (consumesKey && (Hand.Of(who) is not { } hand || !hand.UseUp(key))) who.Inventory?.Remove(key);
            }
            SetOpen(!IsOpen);
        }

        /// <summary>Open or close from code (e.g. a puzzle opening a secret door). Ignores the lock.</summary>
        public void SetOpen(bool open)
        {
            if (open == IsOpen) return;
            IsOpen = open;
            _from = _e;
            _p = 0f;
            _wait = handDelay;
            enabled = true;
            Toggled?.Invoke(open);
        }

        void Rattle()
        {
            _rattle = rattleSeconds;
            _wait = handDelay;
            enabled = true;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_message > 0f)
            {
                _message -= dt;
                BlinkPrompts.Show(this, null, $"The {noun} is locked");
            }
            if (_wait > 0f) { _wait -= dt; return; }

            _p = Mathf.Min(1f, _p + dt / seconds);
            _e = Mathf.LerpUnclamped(_from, IsOpen ? 1f : 0f, Ease(_p, IsOpen && motion == Motion.Hinged ? overshoot : 0f));
            _rattle = Mathf.Max(0f, _rattle - dt);
            // Knocks outward only (never into the frame), decaying.
            float knock = Mathf.Abs(Mathf.Sin(_rattle * rattleHz * Mathf.PI)) * (_rattle / rattleSeconds);

            float angle = openAngle * _e + Mathf.Sign(openAngle) * rattleDegrees * knock;
            if (motion == Motion.Hinged && useHingePoint)
            {
                var turn = Quaternion.AngleAxis(angle, Vector3.up);
                transform.SetPositionAndRotation(_hingeWorld + turn * (_closedWorldPos - _hingeWorld), turn * _closedWorldRot);
            }
            else if (motion == Motion.Hinged)
                transform.localRotation = _closedRot * Quaternion.Euler(0f, angle, 0f);
            else
                transform.localPosition = _closedPos + slide * _e + slide.normalized * (rattleMetres * knock);

            if (_p >= 1f && _rattle <= 0f && _message <= 0f) enabled = false;
        }

        /// <summary>
        /// 0..1 progress to 0..1 pose. overshoot 0 = SmoothStep; above 0 = ease-out-back
        /// (fast start, swings past 1, settles back to exactly 1).
        /// </summary>
        public static float Ease(float p, float overshoot)
        {
            p = Mathf.Clamp01(p);
            if (overshoot <= 0f) return Mathf.SmoothStep(0f, 1f, p);
            float u = p - 1f;
            return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
        }
    }
}
