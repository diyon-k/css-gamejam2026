using System;
using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// A door, cupboard door or drawer that toggles open/closed with a short animation (real time).
    /// Hinged: put this on a pivot at the hinge edge; it turns <see cref="openAngle"/> around local up.
    /// Sliding: moves by <see cref="slide"/> (parent space) when open.
    /// Set <see cref="requiredItemId"/> to lock it until the player carries that item; unlocking is permanent.
    /// Trying it while locked without the item rattles it instead.
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

        Vector3 _closedPos;
        Quaternion _closedRot;
        float _e, _from, _p = 1f, _wait, _rattle;

        void Awake()
        {
            _closedPos = transform.localPosition;
            _closedRot = transform.localRotation;
        }

        bool HasKey(Interactor who) => !IsLocked || (who.Inventory != null && who.Inventory.Has(requiredItemId));

        public string Prompt(Interactor who) =>
            !HasKey(who) ? $"The {noun} is locked"
            : IsLocked ? $"Unlock {noun}"
            : (IsOpen ? "Close " : "Open ") + noun;

        // Always usable: a locked one rattles, so the hands still reach for it.
        public bool CanInteract(Interactor who) => true;

        public void Interact(Interactor who)
        {
            if (!HasKey(who)) { Rattle(); return; }
            requiredItemId = "";
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
            if (_wait > 0f) { _wait -= dt; return; }

            _p = Mathf.Min(1f, _p + dt / seconds);
            _e = Mathf.LerpUnclamped(_from, IsOpen ? 1f : 0f, Ease(_p, IsOpen && motion == Motion.Hinged ? overshoot : 0f));
            _rattle = Mathf.Max(0f, _rattle - dt);
            // Knocks outward only (never into the frame), decaying.
            float knock = Mathf.Abs(Mathf.Sin(_rattle * rattleHz * Mathf.PI)) * (_rattle / rattleSeconds);

            if (motion == Motion.Hinged)
                transform.localRotation = _closedRot * Quaternion.Euler(0f, openAngle * _e + Mathf.Sign(openAngle) * rattleDegrees * knock, 0f);
            else
                transform.localPosition = _closedPos + slide * _e + slide.normalized * (rattleMetres * knock);

            if (_p >= 1f && _rattle <= 0f) enabled = false;
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
