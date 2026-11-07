using UnityEngine;

namespace Blink.Combat
{
    /// <summary>
    /// Bare-bones combat HUD on the player: a crosshair that flashes a hit marker (red X on a kill, from any
    /// weapon), the weapon in hand with its ammo and a reload bar (nothing for the hands and the axe), and the
    /// player's health. Drawn with IMGUI so it needs no Canvas; the HUD workstream can replace it with proper UI
    /// driven by the same <see cref="PlayerWeapons"/>/<see cref="Health"/> events.
    /// Timing is unscaled so it doesn't speed up during fast-forward.
    /// </summary>
    public sealed class GunHud : MonoBehaviour
    {
        [Tooltip("Defaults to the PlayerWeapons on this object.")]
        [SerializeField] PlayerWeapons weapons;
        [Tooltip("Defaults to the Health on this object (the player's).")]
        [SerializeField] Health health;

        [Header("Crosshair")]
        [SerializeField] Color crosshairColor = new(1f, 1f, 1f, 0.85f);
        [SerializeField] Color hitColor = new(1f, 1f, 1f, 1f);
        [SerializeField] Color killColor = new(1f, 0.15f, 0.1f, 1f);
        [SerializeField, Min(0.01f)] float markerSeconds = 0.2f;

        float _markerUntil;
        bool _markerWasKill;
        GUIStyle _ammoStyle, _smallStyle, _smallLeftStyle;

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

        void OnRayFired(ShotRay shot) => ShowMarker(shot.Target);

        void OnMeleeHit(MeleeHitInfo hit) => ShowMarker(hit.Target);

        void ShowMarker(IDamageable target)
        {
            if (target == null) return;
            _markerWasKill = target.IsDead; // raised after the damage is applied
            _markerUntil = Time.unscaledTime + markerSeconds;
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();

            if (health == null || !health.IsDead) DrawCrosshair();
            if (weapons != null) DrawWeapon(weapons.ActiveWeapon);
            if (health != null) DrawHealth();
        }

        void DrawCrosshair()
        {
            var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            const float gap = 5f, len = 7f, thick = 2f;

            Fill(new Rect(c.x - thick * 0.5f, c.y - gap - len, thick, len), crosshairColor);
            Fill(new Rect(c.x - thick * 0.5f, c.y + gap, thick, len), crosshairColor);
            Fill(new Rect(c.x - gap - len, c.y - thick * 0.5f, len, thick), crosshairColor);
            Fill(new Rect(c.x + gap, c.y - thick * 0.5f, len, thick), crosshairColor);

            if (Time.unscaledTime >= _markerUntil) return;

            // Hit marker: four short diagonal strokes around the centre.
            var color = _markerWasKill ? killColor : hitColor;
            float size = _markerWasKill ? 9f : 7f;
            var old = GUI.matrix;
            for (int i = 0; i < 4; i++)
            {
                GUIUtility.RotateAroundPivot(45f + 90f * i, c);
                Fill(new Rect(c.x + 6f, c.y - 1f, size, 2f), color);
                GUI.matrix = old;
            }
        }

        void DrawWeapon(IWeaponStatus weapon)
        {
            const float w = 180f, margin = 24f;
            var area = new Rect(Screen.width - w - margin, Screen.height - 70f - margin, w, 70f);
            if (!weapon.UsesAmmo) return; // hands and axe: nothing to count

            GUI.Label(new Rect(area.x, area.y - 18f, w, 18f), weapon.Kind.ToString().ToUpperInvariant(), _smallStyle);
            string ammo = $"{weapon.AmmoLoaded} <size=18>/ {weapon.AmmoReserve}</size>";
            GUI.Label(new Rect(area.x, area.y, w, 42f), ammo, _ammoStyle);

            if (weapon.IsReloading)
            {
                var bar = new Rect(area.x + w - 120f, area.y + 46f, 120f, 6f);
                Fill(bar, new Color(0f, 0f, 0f, 0.5f));
                Fill(new Rect(bar.x, bar.y, bar.width * weapon.ReloadProgress01, bar.height), Color.white);
                GUI.Label(new Rect(area.x, area.y + 52f, w, 18f), "RELOADING", _smallStyle);
            }
            else if (weapon.AmmoLoaded == 0)
            {
                GUI.Label(new Rect(area.x, area.y + 46f, w, 18f), weapon.AmmoReserve > 0 ? "R TO RELOAD" : "NO AMMO", _smallStyle);
            }
        }

        void DrawHealth()
        {
            const float margin = 24f, w = 220f, h = 10f;
            var back = new Rect(margin, Screen.height - margin - h, w, h);
            Fill(back, new Color(0f, 0f, 0f, 0.5f));
            Fill(new Rect(back.x + 1f, back.y + 1f, (w - 2f) * health.Fraction, h - 2f), new Color(0.85f, 0.15f, 0.12f, 0.95f));
            GUI.Label(new Rect(margin, back.y - 22f, w, 20f), health.IsDead ? "DEAD" : $"HP {Mathf.CeilToInt(health.Current)}", _smallLeftStyle);
        }

        void EnsureStyles()
        {
            if (_ammoStyle != null) return;
            _ammoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight, richText = true,
                normal = { textColor = Color.white },
            };
            _smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight,
                normal = { textColor = new Color(1f, 1f, 1f, 0.85f) },
            };
            _smallLeftStyle = new GUIStyle(_smallStyle) { alignment = TextAnchor.UpperLeft };
        }

        static void Fill(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
