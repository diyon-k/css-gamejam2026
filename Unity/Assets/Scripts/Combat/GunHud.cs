using Blink.UI;
using UnityEngine;

namespace Blink.Combat
{
    /// <summary>
    /// The combat HUD on the player, in the <see cref="BlinkTheme"/> style: a tiny soft dot in the centre, a thin ring
    /// that pops around it on a hit from any weapon (red on a kill, larger on a headshot), the weapon in hand with its
    /// ammo bottom-right (shown after shooting, reloading or switching, then fading; kept up while empty), a
    /// "[R] reload" prompt when the magazine is empty, and a health ring bottom-left that only shows after damage or when
    /// low. Drawn with IMGUI; it only listens to <see cref="PlayerWeapons"/> events and reads <see cref="Health"/>.
    /// Timing is unscaled so it doesn't speed up during fast-forward.
    /// </summary>
    public sealed class GunHud : MonoBehaviour
    {
        [Tooltip("Defaults to the PlayerWeapons on this object.")]
        [SerializeField] PlayerWeapons weapons;
        [Tooltip("Defaults to the Health on this object (the player's).")]
        [SerializeField] Health health;

        [Header("Crosshair")]
        [Tooltip("Use BlinkTheme's palette (bone dot and ring, red on a kill). Untick to use the three colours below.")]
        [SerializeField] bool themeColors = true;
        [SerializeField] Color crosshairColor = new(1f, 1f, 1f, 0.85f);
        [SerializeField] Color hitColor = new(1f, 1f, 1f, 1f);
        [SerializeField] Color killColor = new(1f, 0.15f, 0.1f, 1f);
        [Tooltip("How long a plain hit's ring lasts (real seconds); a kill's lasts about twice as long.")]
        [SerializeField, Min(0.01f)] float markerSeconds = 0.2f;

        [Header("Fading readouts (real seconds)")]
        [Tooltip("How long the health ring stays up after a change while health isn't low.")]
        [SerializeField, Min(0f)] float healthLinger = 3f;
        [Tooltip("At or below this fraction the health ring stays up (and turns red).")]
        [SerializeField, Range(0f, 1f)] float lowHealth = 0.35f;

        float _markerAt = float.NegativeInfinity;
        HitKind _markerKind;
        float _ammoAt = float.NegativeInfinity, _ammoAlpha;
        float _healthAt = float.NegativeInfinity, _healthAlpha, _lastFraction = -1f;
        WeaponKind _lastKind;
        int _lastLoaded = -1, _lastReserve = -1;
        bool _lastReloading;

        void Awake()
        {
            if (weapons == null) weapons = GetComponent<PlayerWeapons>();
            if (health == null) health = GetComponent<Health>();
        }

        void OnEnable()
        {
            if (weapons == null) return;
            weapons.RayFired += OnRayFired;
            weapons.MeleeHit += OnMeleeHit;
        }

        void OnDisable()
        {
            if (weapons == null) return;
            weapons.RayFired -= OnRayFired;
            weapons.MeleeHit -= OnMeleeHit;
        }

        void OnRayFired(ShotRay shot) => ShowMarker(shot.Target, shot.IsHead);

        void OnMeleeHit(MeleeHitInfo hit) => ShowMarker(hit.Target, hit.IsHead);

        void ShowMarker(IDamageable target, bool head)
        {
            if (target == null) return;
            // Raised after the damage is applied, so IsDead says whether this hit killed.
            _markerKind = !target.IsDead ? HitKind.Hit : head ? HitKind.Headshot : HitKind.Kill;
            _markerAt = Time.unscaledTime;
        }

        float MarkerSeconds => _markerKind == HitKind.Hit ? Mathf.Max(0.18f, markerSeconds * 1.25f) : markerSeconds * 2.2f;

        void LateUpdate()
        {
            bool alive = health == null || !health.IsDead;
            if (weapons != null) TrackWeapon(weapons.ActiveWeapon, alive);
            if (health != null) TrackHealth();
        }

        void TrackWeapon(IWeaponStatus weapon, bool alive)
        {
            if (weapon == null) return;
            float now = Time.unscaledTime;
            if (weapon.Kind != _lastKind || weapon.AmmoLoaded != _lastLoaded || weapon.AmmoReserve != _lastReserve ||
                weapon.IsReloading != _lastReloading || weapon.IsReloading)
                _ammoAt = now;
            _lastKind = weapon.Kind;
            _lastLoaded = weapon.AmmoLoaded;
            _lastReserve = weapon.AmmoReserve;
            _lastReloading = weapon.IsReloading;

            bool empty = weapon.UsesAmmo && weapon.AmmoLoaded == 0;
            bool show = alive && weapon.Kind != WeaponKind.Hands &&
                        (empty || now - _ammoAt < BlinkTheme.Asset.linger);
            _ammoAlpha = BlinkTheme.StepFade(_ammoAlpha, show, -1f, BlinkTheme.Asset.fadeOut * 2f);

            if (_ammoAlpha > 0f)
                BlinkPrompts.ReserveBottom((weapon.UsesAmmo ? BlinkWidgets.AmmoBlockHeight : BlinkTheme.Px(40f)) * _ammoAlpha);
            // On a pad X is also Interact: while something is in focus X won't reload, so don't offer it.
            bool xInteracts = BlinkInput.UsingGamepad && Interaction.Interactor.HasFocus;
            if (alive && empty && weapon.AmmoReserve > 0 && !weapon.IsReloading && !xInteracts)
                BlinkPrompts.Show(this, weapons != null ? weapons.ReloadInput : null, "R", "Reload", -1f, 20);
        }

        void TrackHealth()
        {
            float f = health.Fraction;
            if (_lastFraction >= 0f && !Mathf.Approximately(f, _lastFraction)) _healthAt = Time.unscaledTime;
            _lastFraction = f;
            bool show = !health.IsDead && (f <= lowHealth || Time.unscaledTime - _healthAt < healthLinger);
            _healthAlpha = BlinkTheme.StepFade(_healthAlpha, show, -1f, BlinkTheme.Asset.fadeOut * 2f);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            if (health == null || !health.IsDead) DrawCrosshair();
            if (weapons != null) DrawWeapon(weapons.ActiveWeapon);
            if (health != null && !health.IsDead) BlinkWidgets.HealthCore(health.Fraction, _healthAlpha, lowHealth);
        }

        void DrawCrosshair()
        {
            var c = BlinkTheme.ScreenCentre;
            if (themeColors) BlinkWidgets.Reticle(c, 1f);
            else BlinkWidgets.Reticle(c, 1f, crosshairColor);

            float t = (Time.unscaledTime - _markerAt) / MarkerSeconds;
            if (t >= 1f) return;
            if (themeColors) BlinkWidgets.HitRing(c, t, _markerKind);
            else BlinkWidgets.HitRing(c, t, _markerKind, _markerKind == HitKind.Hit ? hitColor : killColor);
        }

        void DrawWeapon(IWeaponStatus weapon)
        {
            if (weapon == null || _ammoAlpha <= 0f || weapon.Kind == WeaponKind.Hands) return;
            BlinkWidgets.Ammo(weapon.Kind.ToString(), weapon.UsesAmmo, weapon.AmmoLoaded, weapon.AmmoReserve,
                weapon.IsReloading, weapon.ReloadProgress01, BlinkTheme.EaseOut(_ammoAlpha));
        }
    }
}
