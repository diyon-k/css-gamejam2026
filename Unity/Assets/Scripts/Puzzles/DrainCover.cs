using System;
using System.Collections;
using Blink.Interaction;
using UnityEngine;

namespace Blink.Puzzles
{
    /// <summary>
    /// The rusted cover over the flooded basement's drain (docs/puzzles/FLOODED_BASEMENT.md, "Drain"). Without the
    /// crowbar it only says what's wrong; with it, one press levers it off: it flips over onto the floor beside the
    /// drain and <see cref="Levered"/> fires (the room uses the crowbar up and starts draining). On the cover's root,
    /// with its collider on it or a child. Real time, like doors and pickups.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DrainCover : MonoBehaviour, IInteractable
    {
        public string toolItemId = "crowbar";
        public string stuckPrompt = "Something's wedged over the drain";
        public string leverPrompt = "Lever the cover off";

        [Header("Flip (real time)")]
        [Tooltip("Where it ends up, in the parent's space, relative to where it sits: beside the drain.")]
        [SerializeField] Vector3 landOffset = new(0.55f, 0.01f, 0.1f);
        [Tooltip("It lands upside down and turned a little.")]
        [SerializeField] Vector3 landEuler = new(178f, 35f, 0f);
        [SerializeField, Min(0.05f)] float flipSeconds = 0.45f;
        [SerializeField, Min(0f)] float flipArc = 0.35f;

        /// <summary>The cover came off. Argument: who did it.</summary>
        public event Action<Interactor> Levered;

        /// <summary>Lying beside the drain, not over it.</summary>
        public bool IsOff { get; private set; }

        Vector3 _homePos;
        Quaternion _homeRot;
        bool _homeSaved;
        Coroutine _flip;

        void Awake() => SaveHome();

        void SaveHome()
        {
            if (_homeSaved) return;
            transform.GetLocalPositionAndRotation(out _homePos, out _homeRot);
            _homeSaved = true;
        }

        bool HasTool(Interactor who) => who != null && who.Inventory != null && who.Inventory.Has(toolItemId);

        public string Prompt(Interactor who) => IsOff ? null : HasTool(who) ? leverPrompt : stuckPrompt;

        public bool CanInteract(Interactor who) => !IsOff && HasTool(who);

        public void Interact(Interactor who)
        {
            if (!CanInteract(who)) return;
            IsOff = true;
            SetColliders(false);
            if (_flip != null) StopCoroutine(_flip);
            _flip = StartCoroutine(Flip());
            Levered?.Invoke(who);
        }

        /// <summary>Back over the drain at once (a reset while the screen is black).</summary>
        public void PutBack()
        {
            SaveHome();
            if (_flip != null) StopCoroutine(_flip);
            _flip = null;
            IsOff = false;
            transform.SetLocalPositionAndRotation(_homePos, _homeRot);
            SetColliders(true);
        }

        IEnumerator Flip()
        {
            Vector3 to = _homePos + landOffset;
            Quaternion toRot = _homeRot * Quaternion.Euler(landEuler);
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / flipSeconds)
            {
                float e = 1f - (1f - t) * (1f - t); // wrenched up hard, then falls
                transform.SetLocalPositionAndRotation(
                    Vector3.Lerp(_homePos, to, e) + Vector3.up * (Mathf.Sin(Mathf.PI * t) * flipArc),
                    Quaternion.Slerp(_homeRot, toRot, t));
                yield return null;
            }
            transform.SetLocalPositionAndRotation(to, toRot);
            _flip = null;
        }

        void SetColliders(bool on)
        {
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = on;
        }
    }
}
