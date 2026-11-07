using Blink.Combat;
using Blink.Player;
using Blink.UI;
using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// On the Player (the <see cref="Interactor"/> adds it). The player has one hand and holds one thing at a time: a
    /// small item (<see cref="Pickup"/>: a key, a candle...), the pistol, a melee item (<see cref="SwingableProp"/>) or a
    /// carried prop (<see cref="Carryable"/>). Taking anything drops what was held first, so picking something up is a
    /// swap. G / d-pad down drops what's in hand (<see cref="PlayerWeapons"/> reads the key).
    ///
    /// A held item sits in view, bottom right, on the empty-hands view model (<see cref="HandsViewModel.HoldPoint"/>), so
    /// it sways and reaches with the hands. While held it's in the <see cref="Inventory"/>, so locks and puzzles keep
    /// checking <see cref="Inventory.Has"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Hand : MonoBehaviour
    {
        [Tooltip("Real seconds the drop prompt stays up after taking an item.")]
        [SerializeField, Min(0f)] float dropHintSeconds = 4f;

        /// <summary>The player's hand (the last one enabled).</summary>
        public static Hand Instance { get; private set; }

        /// <summary>The small item in hand, or null (empty, or holding a weapon or prop instead).</summary>
        public Pickup Item { get; private set; }

        Interactor _interactor;
        PlayerWeapons _weapons;
        HandsViewModel _hands;
        Transform _eye;
        float _hintUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        /// <summary><paramref name="who"/>'s hand, or null.</summary>
        public static Hand Of(Interactor who) => who != null ? who.Hand : null;

        void Awake()
        {
            _interactor = GetComponent<Interactor>();
            _weapons = GetComponent<PlayerWeapons>();
            _hands = GetComponentInChildren<HandsViewModel>(true);
            _eye = GetComponentInChildren<Camera>() is { } cam ? cam.transform : transform;
        }

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        bool HoldsWeapon => _weapons != null && _weapons.OneHanded && _weapons.HoldsWeapon;

        /// <summary>Nothing in hand.</summary>
        public bool IsEmpty => Item == null && !HoldsWeapon && Carryable.Held == null;

        /// <summary>What's in hand, lower case ("bedroom key", "pistol"), or null when empty.</summary>
        public string HeldName =>
            Item != null ? Item.displayName.ToLowerInvariant() : HoldsWeapon ? _weapons.HeldWeaponName : null;

        /// <summary>False only when the weapon in hand can't be let go (a pistol the player started with has no pickup to drop).</summary>
        public bool CanMakeRoom => !HoldsWeapon || _weapons.CanDropWeapon;

        /// <summary>Where a held item sits.</summary>
        public Transform HoldPoint => _hands != null ? _hands.HoldPoint : _eye;

        /// <summary>Empties the hand for something new: whatever is held is dropped in front of the player. False if it can't be.</summary>
        public bool MakeRoom()
        {
            if (!CanMakeRoom) return false;
            Carryable.DropHeld();
            if (Item != null) DropItem();
            if (HoldsWeapon) _weapons.DropWeapon();
            return true;
        }

        /// <summary>Called by <see cref="Pickup"/> once the hand is free and the item is on its way up.</summary>
        internal void Take(Pickup item)
        {
            Item = item;
            _hintUntil = Time.unscaledTime + dropHintSeconds;
        }

        /// <summary>Puts the held item down on the floor in front of the player.</summary>
        public void DropItem()
        {
            var item = Release();
            if (item != null) item.PlaceAt(DropPoint(_eye));
        }

        /// <summary>Uses up the held item if it's <paramref name="itemId"/> (a key turned in its lock). False if it isn't held.</summary>
        public bool UseUp(string itemId)
        {
            if (Item == null || Item.itemId != itemId) return false;
            Release().UseUp();
            return true;
        }

        /// <summary>Puts the held item back where it was picked up (when the player dies).</summary>
        public void ReturnItemHome()
        {
            var item = Release();
            if (item != null) item.ReturnHome();
        }

        Pickup Release()
        {
            var item = Item;
            Item = null;
            if (item != null && _interactor != null && _interactor.Inventory != null) _interactor.Inventory.Remove(item.itemId);
            return item;
        }

        static readonly int NotPlayer = ~(1 << 9); // the Player capsule's layer

        /// <summary>Where something dropped lands: on the floor a little in front of <paramref name="eye"/>, short of any wall.</summary>
        public static Vector3 DropPoint(Transform eye)
        {
            var flat = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.ProjectOnPlane(eye.up, Vector3.up).normalized;
            const float Ahead = 0.7f, Clearance = 0.2f;
            float ahead = Physics.Raycast(eye.position, flat, out var wall, Ahead + Clearance, NotPlayer, QueryTriggerInteraction.Ignore)
                ? Mathf.Max(0f, wall.distance - Clearance)
                : Ahead;
            var above = eye.position + flat * ahead;
            return Physics.Raycast(above, Vector3.down, out var floor, 3f, NotPlayer, QueryTriggerInteraction.Ignore)
                ? floor.point
                : above + Vector3.down * 1.5f;
        }

        void Update()
        {
            // The held item keeps out of the way while the hands are on a ladder or holding off a zombie.
            if (_hands != null && Item != null)
            {
                bool show = !Ladder.Climbing && !GrabQte.Active;
                if (HoldPoint.gameObject.activeSelf != show) HoldPoint.gameObject.SetActive(show);
            }
            if (Item != null && Time.unscaledTime < _hintUntil && _weapons != null)
                BlinkPrompts.Show(this, _weapons.DropInput, PlayerWeapons.DropKeyName, "Drop " + Item.displayName.ToLowerInvariant(), order: -1);
        }
    }
}
