using System.Collections;
using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// A small object the player can take into their <see cref="Inventory"/>. It's in the inventory at once,
    /// then flies into the hand and shrinks away in time with the hands' reach before it hides.
    /// </summary>
    public sealed class Pickup : MonoBehaviour, IInteractable
    {
        public string itemId = "candle";
        public string displayName = "Candle";

        [Header("Gun rewards (leave item id empty for a pure ammo box / pistol)")]
        [Tooltip("Puts the pistol in the loadout (and brings it out) if the player doesn't have it yet.")]
        public bool givesPistol;
        [Tooltip("Pistol rounds added to the reserve.")]
        [Min(0)] public int pistolRounds;

        [Tooltip("How long it takes to fly into the hand (real time). The hands' reach peaks at ~0.2 s.")]
        [SerializeField, Min(0.01f)] float flySeconds = 0.25f;
        [Tooltip("Where it ends up, in camera space: a little below and in front of the eye.")]
        [SerializeField] Vector3 handOffset = new(0.05f, -0.25f, 0.35f);

        bool _taken;
        Vector3 _homePos, _homeScale;
        Quaternion _homeRot;

        public bool Taken => _taken;
        public string Prompt(Interactor who) => "Pick up " + displayName;
        public bool CanInteract(Interactor who) => !_taken && who.Inventory != null;

        public void Interact(Interactor who)
        {
            // ponytail: not put back on death (WakeUpSequence restores by inventory id), so ammo can't be farmed by dying.
            if (!string.IsNullOrEmpty(itemId)) who.Inventory.Add(itemId, displayName);
            if ((givesPistol || pistolRounds > 0) && Combat.PlayerWeapons.Instance is { } weapons)
                weapons.GivePistol(givesPistol, pistolRounds);
            _taken = true;
            _homePos = transform.position;
            _homeRot = transform.rotation;
            _homeScale = transform.localScale;
            SetColliders(false); // nothing to look at or bump into mid-flight
            StartCoroutine(FlyToHand(who.Eye));
        }

        IEnumerator FlyToHand(Transform eye)
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / flySeconds)
            {
                if (eye == null) break;
                float e = t * t; // lifts off gently, then snaps into the palm
                transform.position = Vector3.Lerp(_homePos, eye.TransformPoint(handOffset), e);
                transform.localScale = _homeScale * (1f - Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            gameObject.SetActive(false); // not destroyed: dying puts it back (WakeUpSequence); OnDisable resets it
        }

        // Back to where it was picked up, full size, so WakeUpSequence can just reactivate it.
        void OnDisable()
        {
            if (!_taken) return;
            _taken = false;
            transform.SetPositionAndRotation(_homePos, _homeRot);
            transform.localScale = _homeScale;
            SetColliders(true);
        }

        void SetColliders(bool on)
        {
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = on;
        }
    }
}
