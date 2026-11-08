using Blink.Audio;
using Blink.Combat;
using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// A melee weapon lying in the world (golf club, plank, candlestick, or the axe once dropped). Interact puts it
    /// in the melee slot in place of the axe model: it hangs off the axe view model's grip and swings with the axe's
    /// animation (<see cref="MeleeWeapon"/>). It takes the player's one <see cref="Hand"/>: picking one up swaps it for
    /// whatever was held, and a melee item falls from the hands as a physics prop (as with <see cref="PlayerWeapons"/>' Drop key).
    ///
    /// A blunt item (<see cref="KnocksDown"/>) barely hurts: it knocks a zombie flying as a ragdoll and stuns it
    /// (<see cref="Mobs.MobController.KnockDown"/>). The axe isn't blunt and keeps its own damage.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SwingableProp : MonoBehaviour, IInteractable
    {
        // Where the item's grip sits in the axe view model's Grip space (+Y up the haft): at the lower (left) hand, as
        // on the axe, so both hands close on the handle. Higher and the handle ends above the left fist and pokes
        // through it. Keep in step with LeftHandTarget's y in ViewModel_Axe.
        static readonly Vector3 HandPoint = new(0f, -0.14f, 0f);

        [SerializeField] string displayName = "plank";

        [Header("Grip (item's local space)")]
        [Tooltip("The point on the item held in the hand, e.g. the end of the handle.")]
        [SerializeField] Vector3 grip = new(0f, -0.4f, 0f);
        [Tooltip("Local direction from the grip to the striking end.")]
        [SerializeField] Vector3 lengthAxis = Vector3.up;

        [Header("Hit")]
        [Tooltip("Knocks zombies flying instead of cutting them (off for the axe).")]
        [SerializeField] bool knocksDown = true;
        [Tooltip("Barely hurts: these knock zombies away rather than kill them.")]
        [SerializeField, Min(0f)] float damage = 8f;
        [Tooltip("Speed (m/s) a hit gives every limb, flat along your view.")]
        [SerializeField, Min(0f)] float launchSpeed = 0.625f;
        [Tooltip("Upward speed (m/s) on top, so it flies instead of sliding.")]
        [SerializeField, Min(0f)] float launchLift = 0.28125f;
        [Tooltip("Sideways speed (m/s) along the swing (the chop sweeps right to left).")]
        [SerializeField] float launchSideways = 0.15625f;
        [Tooltip("World seconds a zombie lies stunned before getting back up (longer if it's still tumbling).")]
        [SerializeField, Min(0f)] float stunSeconds = 5f;

        [Header("Sound (empty = the axe's)")]
        [SerializeField] SoundCue swingCue;
        [Tooltip("Into a zombie.")]
        [SerializeField] SoundCue hitCue;
        [Tooltip("Into a wall, floor or prop.")]
        [SerializeField] SoundCue surfaceHitCue;

        public string DisplayName => displayName;
        public bool KnocksDown => knocksDown;
        public float Damage => damage;
        public float StunSeconds => stunSeconds;
        public bool IsHeld { get; private set; }
        public SoundCue SwingCue => swingCue;
        public SoundCue HitCue => hitCue;
        public SoundCue SurfaceHitCue => surfaceHitCue;

        Rigidbody _body;
        Collider[] _colliders;
        bool _customPose;
        Vector3 _heldPosition;
        Quaternion _heldRotation = Quaternion.identity;

        // Lazy: the axe is wrapped on an inactive slot, so Awake may not have run when it's first held.
        Rigidbody Body => _body != null ? _body : _body = GetComponent<Rigidbody>();
        Collider[] Colliders => _colliders ??= GetComponentsInChildren<Collider>(true);

        public string Prompt(Interactor who)
        {
            if (Hand.Of(who) is { CanMakeRoom: false }) return "Hands full";
            var inHand = Weapons(who) is { OneHanded: true } ? Hand.Of(who)?.HeldName
                : Weapons(who) is { Axe: { Equipped: { } melee } } ? melee.DisplayName : null;
            return inHand != null ? $"Swap {inHand} for {displayName}" : "Pick up " + displayName;
        }

        public bool CanInteract(Interactor who) =>
            !IsHeld && Carryable.Held == null && Weapons(who) is { Axe: not null } && Hand.Of(who) is not { CanMakeRoom: false };

        public void Interact(Interactor who) => Weapons(who).EquipMelee(this);

        static PlayerWeapons Weapons(Interactor who) => who.GetComponent<PlayerWeapons>();

        /// <summary>The speed a hit gives a zombie's limbs: along the view, up, and across with the swing.</summary>
        public Vector3 LaunchVelocity(Transform eye)
        {
            Vector3 flat = Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
            Vector3 side = Vector3.ProjectOnPlane(-eye.right, Vector3.up).normalized;
            return flat * launchSpeed + side * launchSideways + Vector3.up * launchLift;
        }

        /// <summary>Into the hands: parented to the view model's grip, physics and colliders off.</summary>
        public void Hold(Transform viewModelGrip)
        {
            IsHeld = true;
            Body.isKinematic = true;
            Body.interpolation = RigidbodyInterpolation.None;
            foreach (var col in Colliders) col.enabled = false;
            transform.SetParent(viewModelGrip, false);
            Blink.Player.ViewmodelLighting.SetHeld(gameObject, true);
            if (!_customPose)
            {
                _heldRotation = lengthAxis.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(lengthAxis.normalized, Vector3.up) : Quaternion.identity;
                _heldPosition = HandPoint - _heldRotation * Vector3.Scale(grip, transform.localScale);
            }
            transform.SetLocalPositionAndRotation(_heldPosition, _heldRotation);
        }

        /// <summary>Out of the hands where it is, falling with <paramref name="velocity"/>. Never bumps <paramref name="holder"/>.</summary>
        public void Release(Vector3 velocity, Collider holder)
        {
            IsHeld = false;
            transform.SetParent(null, true);
            Blink.Player.ViewmodelLighting.SetHeld(gameObject, false);
            foreach (var col in Colliders)
            {
                col.enabled = true;
                if (holder != null) Physics.IgnoreCollision(holder, col, true); // dropped inside the player's capsule
            }
            Body.isKinematic = false;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.linearVelocity = velocity;
            Body.angularVelocity = Random.insideUnitSphere * 3f;
        }

        /// <summary>Moved (once released) to just above <paramref name="floorPoint"/>, still, to settle there.</summary>
        public void PlaceAt(Vector3 floorPoint)
        {
            transform.position = floorPoint;
            if (GetComponentInChildren<Renderer>() is { } r) transform.position += Vector3.up * (floorPoint.y - r.bounds.min.y + 0.05f);
            Body.position = transform.position; // interpolated: move the body too, or it snaps back
            Body.linearVelocity = Vector3.zero;
        }

        /// <summary>
        /// Turns a view model's built-in weapon mesh (the axe) into an item, held where it already sits, so it can
        /// be dropped and picked up like any other.
        /// </summary>
        public static SwingableProp Wrap(GameObject mesh, string name, float mass)
        {
            mesh.AddComponent<BoxCollider>(); // fits the mesh
            var body = mesh.AddComponent<Rigidbody>();
            body.mass = mass;
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var item = mesh.AddComponent<SwingableProp>();
            item.displayName = name;
            item.knocksDown = false;
            item._customPose = true;
            mesh.transform.GetLocalPositionAndRotation(out item._heldPosition, out item._heldRotation);
            return item;
        }
    }
}
