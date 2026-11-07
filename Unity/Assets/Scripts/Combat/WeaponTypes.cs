using UnityEngine;

namespace Blink.Combat
{
    /// <summary>The player's weapon slots, in the default key order (1 = hands, 2 = pistol, 3 = axe).</summary>
    public enum WeaponKind
    {
        /// <summary>Empty hands: no attack. Interacting (doors) works with any slot.</summary>
        Hands,
        Pistol,
        Axe,
    }

    /// <summary>
    /// What the HUD and camera need to know about the weapon in hand, whatever it is.
    /// <see cref="PlayerWeapons.ActiveWeapon"/> returns one.
    /// </summary>
    public interface IWeaponStatus
    {
        WeaponKind Kind { get; }

        /// <summary>False for the hands and the axe: there is no ammo to show.</summary>
        bool UsesAmmo { get; }

        /// <summary>Rounds in the magazine.</summary>
        int AmmoLoaded { get; }

        /// <summary>Most rounds the weapon holds at once.</summary>
        int AmmoCapacity { get; }

        /// <summary>Spare rounds carried.</summary>
        int AmmoReserve { get; }

        bool IsReloading { get; }

        /// <summary>0..1 through the reload.</summary>
        float ReloadProgress01 { get; }
    }

    /// <summary>
    /// One hitscan ray of a shot (the pistol fires one per shot).
    /// Raised by <see cref="PlayerWeapons.RayFired"/> after the damage was applied, so <see cref="Target"/>'s
    /// <see cref="IDamageable.IsDead"/> already says whether this ray killed it.
    /// </summary>
    public readonly struct ShotRay
    {
        public readonly WeaponKind Weapon;

        /// <summary>The ray the shot travelled along (from the camera).</summary>
        public readonly Ray Ray;

        /// <summary>Furthest the ray reaches, in metres.</summary>
        public readonly float Range;

        public readonly bool Hit;

        /// <summary>Hit info. Valid only if <see cref="Hit"/>.</summary>
        public readonly RaycastHit Info;

        /// <summary>Barrel tip for tracers and the muzzle flash. May be null.</summary>
        public readonly Transform Muzzle;

        public ShotRay(WeaponKind weapon, Ray ray, float range, bool hit, RaycastHit info, Transform muzzle)
        {
            Weapon = weapon;
            Ray = ray;
            Range = range;
            Hit = hit;
            Info = info;
            Muzzle = muzzle;
        }

        /// <summary>Where the ray stopped: the hit point, or the end of its range on a miss.</summary>
        public Vector3 EndPoint => Hit ? Info.point : Ray.origin + Ray.direction.normalized * Range;

        /// <summary>The damageable thing that was hit, or null (a miss, or a wall).</summary>
        public IDamageable Target => Hit && Info.collider != null ? Info.collider.GetComponentInParent<IDamageable>() : null;

        /// <summary>True if the ray hit a head hitbox (a one-shot kill).</summary>
        public bool IsHead => Hit && Hitbox.IsHeadCollider(Info.collider);
    }

    /// <summary>
    /// A melee strike that connected. Raised by <see cref="PlayerWeapons.MeleeHit"/> after the damage was applied.
    /// <see cref="Target"/> is null when the blade hit a wall or prop instead of something that can be hurt.
    /// </summary>
    public readonly struct MeleeHitInfo
    {
        public readonly WeaponKind Weapon;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;

        /// <summary>Which way the blow travelled (unit length).</summary>
        public readonly Vector3 Direction;

        public readonly Collider Collider;

        /// <summary>What took the damage, or null for a wall or prop.</summary>
        public readonly IDamageable Target;

        /// <summary>True if this hit killed <see cref="Target"/>.</summary>
        public readonly bool Killed;

        public MeleeHitInfo(WeaponKind weapon, Vector3 point, Vector3 normal, Vector3 direction, Collider collider,
            IDamageable target, bool killed)
        {
            Weapon = weapon;
            Point = point;
            Normal = normal;
            Direction = direction;
            Collider = collider;
            Target = target;
            Killed = killed;
        }

        /// <summary>True if the blow landed on a head hitbox.</summary>
        public bool IsHead => Target != null && Hitbox.IsHeadCollider(Collider);
    }
}
