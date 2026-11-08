using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.Interaction
{
    /// <summary>
    /// A small object the player can take. Three kinds:
    /// <list type="bullet">
    /// <item>An <b>item</b> (<see cref="itemId"/> set): goes into the player's one <see cref="Hand"/>, dropping whatever was
    /// held, and flies up into view where it stays while held. It's in the <see cref="Inventory"/> while held, so locks
    /// and puzzles can check for it.</item>
    /// <item>The <b>pistol</b> (<see cref="givesPistol"/>): takes the hand too. It flies into the hand and hides, and the
    /// pistol comes out; dropping the pistol puts this back on the floor.</item>
    /// <item>An <b>ammo box</b> (neither): the rounds go in a pocket, not the hand.</item>
    /// </list>
    /// </summary>
    public sealed class Pickup : MonoBehaviour, IInteractable
    {
        public string itemId = "candle";
        public string displayName = "Candle";

        [Header("Gun rewards (leave item id empty for a pure ammo box / pistol)")]
        [Tooltip("Puts the pistol in the hand if the player doesn't have it yet.")]
        public bool givesPistol;
        [Tooltip("Pistol rounds added to the reserve.")]
        [Min(0)] public int pistolRounds;

        [Header("In the hand (items)")]
        [Tooltip("Rotation while held, relative to the view (degrees).")]
        public Vector3 holdEuler = new(0f, -60f, -15f);
        [Tooltip("Size while held, relative to its size in the world.")]
        [Min(0.01f)] public float holdScale = 1f;

        [Tooltip("How long it takes to fly into the hand (real time). The hands' reach peaks at ~0.2 s.")]
        [SerializeField, Min(0.01f)] float flySeconds = 0.25f;
        [Tooltip("Where the pistol and ammo fly to before they hide, in camera space: a little below and in front of the eye.")]
        [SerializeField] Vector3 handOffset = new(0.05f, -0.25f, 0.35f);

        bool _taken;
        Transform _homeParent;
        Vector3 _homePos, _homeScale;
        Quaternion _homeRot;
        readonly List<(Renderer renderer, ShadowCastingMode mode)> _shadows = new();

        /// <summary>In the hand, hidden, or used up: not lying in the world to be taken.</summary>
        public bool Taken => _taken;

        /// <summary>True for a held item (a key, a candle), as opposed to the pistol or an ammo box.</summary>
        public bool IsItem => !string.IsNullOrEmpty(itemId) && !givesPistol;

        bool NeedsHand => IsItem || givesPistol;

        public string Prompt(Interactor who)
        {
            if (AmmoFull) return "Ammo full";
            var hand = Hand.Of(who);
            if (!NeedsHand || hand == null) return "Pick up " + displayName;
            if (!hand.CanMakeRoom) return "Hands full";
            return hand.HeldName is { } held ? $"Swap {held} for {displayName.ToLowerInvariant()}" : "Pick up " + displayName;
        }

        public bool CanInteract(Interactor who) =>
            !_taken && who.Inventory != null && !AmmoFull && (!NeedsHand || Hand.Of(who) is not { CanMakeRoom: false });

        // A pure ammo box is left on the floor while the reserve is full.
        bool AmmoFull => string.IsNullOrEmpty(itemId) && !givesPistol && pistolRounds > 0
            && Combat.PlayerWeapons.Instance is { Pistol: { ReserveFull: true } };

        public void Interact(Interactor who)
        {
            var hand = Hand.Of(who);
            if (NeedsHand && hand != null && !hand.MakeRoom()) return;

            _taken = true;
            _homeParent = transform.parent;
            transform.GetPositionAndRotation(out _homePos, out _homeRot);
            _homeScale = transform.localScale;
            SetColliders(false); // nothing to look at or bump into mid-flight

            if (IsItem)
            {
                who.Inventory.Add(itemId, displayName);
                if (hand != null)
                {
                    hand.Take(this);
                    StartCoroutine(FlyToHold(hand.HoldPoint));
                    return;
                }
            }
            else if ((givesPistol || pistolRounds > 0) && Combat.PlayerWeapons.Instance is { } weapons)
            {
                weapons.GivePistol(givesPistol, pistolRounds, givesPistol ? this : null);
                if (givesPistol) pistolRounds = 0; // dropped and picked up again, it brings the gun, not more ammo
            }
            StartCoroutine(FlyToHandAndHide(who.Eye));
        }

        /// <summary>Up into the hand, where it stays in view (parented to <paramref name="hold"/>).</summary>
        IEnumerator FlyToHold(Transform hold)
        {
            SetShadows(false); // a key held up to the face shouldn't throw a shadow across the room
            Blink.Player.ViewmodelLighting.SetHeld(gameObject, true); // lit by the hands' fill light, not left black in the dark
            Vector3 heldScale = _homeScale * holdScale;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / flySeconds)
            {
                if (hold == null) yield break;
                float e = t * t; // lifts off gently, then snaps into the palm
                transform.SetPositionAndRotation(
                    Vector3.Lerp(_homePos, hold.position, e),
                    Quaternion.Slerp(_homeRot, hold.rotation * Quaternion.Euler(holdEuler), e));
                transform.localScale = Vector3.Lerp(_homeScale, heldScale, e);
                yield return null;
            }
            if (hold == null) yield break;
            transform.SetParent(hold, false);
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(holdEuler));
            transform.localScale = heldScale;
        }

        /// <summary>Into the hand, shrinking away in time with the hands' reach, then hidden (the pistol, ammo).</summary>
        IEnumerator FlyToHandAndHide(Transform eye)
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / flySeconds)
            {
                if (eye == null) break;
                float e = t * t;
                transform.position = Vector3.Lerp(_homePos, eye.TransformPoint(handOffset), e);
                transform.localScale = _homeScale * (1f - Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            gameObject.SetActive(false); // not destroyed: dropping the pistol (or dying with an item) puts it back
        }

        /// <summary>Back in the world, resting on the floor at <paramref name="floorPoint"/>, the way up it lay before.</summary>
        public void PlaceAt(Vector3 floorPoint)
        {
            PutBack(floorPoint, _homeRot);
            if (TryGetBounds(out var b)) transform.position += Vector3.up * (floorPoint.y - b.min.y);
        }

        /// <summary>Back exactly where it was picked up.</summary>
        public void ReturnHome() => PutBack(_homePos, _homeRot);

        /// <summary>Gone for good (a key left in its lock).</summary>
        public void UseUp()
        {
            StopAllCoroutines();
            transform.SetParent(_homeParent, true);
            gameObject.SetActive(false);
            _taken = true;
        }

        void PutBack(Vector3 position, Quaternion rotation)
        {
            StopAllCoroutines();
            transform.SetParent(_homeParent, false);
            gameObject.SetActive(true);
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = _homeScale;
            SetShadows(true);
            Blink.Player.ViewmodelLighting.SetHeld(gameObject, false);
            SetColliders(true);
            _taken = false;
        }

        void SetColliders(bool on)
        {
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = on;
        }

        void SetShadows(bool on)
        {
            if (!on)
            {
                _shadows.Clear();
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    _shadows.Add((r, r.shadowCastingMode));
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
                return;
            }
            foreach (var (r, mode) in _shadows)
                if (r != null) r.shadowCastingMode = mode;
            _shadows.Clear();
        }

        bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
            }
            return any;
        }
    }
}
