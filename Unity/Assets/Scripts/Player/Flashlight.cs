using System;
using Blink.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Player
{
    /// <summary>
    /// The player's torch: a spot light on the camera, toggled with F (keyboard) or d-pad up (gamepad).
    /// Switching on stutters for a moment like a worn switch (real seconds, so it isn't stretched by the
    /// eyes-shut fast-forward); switching off is instant. Other systems can call <see cref="Set"/>.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class Flashlight : MonoBehaviour
    {
        [SerializeField] bool startOn = true;
        [Tooltip("Real seconds the switch-on stutter lasts. 0 = instant.")]
        [SerializeField, Min(0f)] float switchOnSeconds = 0.14f;
        [Tooltip("Renderers under this are the viewmodel (hands, weapons): the torch skips them. Empty = the parent (the camera).")]
        [SerializeField] Transform viewmodelRoot;
        [Tooltip("Intensity of the dim fill that lights only the viewmodel. Torch 14 on a wall at 2.5 m ≈ 2.2; hands at 0.4 m need 2.2 × 0.4² ≈ 0.36.")]
        [SerializeField, Min(0f)] float viewmodelFillIntensity = 0.4f;
        [SerializeField, Min(0.1f)] float viewmodelFillRange = 2f;
        [Tooltip("Keep an \"[F] Flashlight\" row at the bottom of the prompt stack (d-pad up on a gamepad).")]
        [SerializeField] bool showKeyHint = true;
        [Tooltip("Where the key hint sits in the prompt stack: lower = further down.")]
        [SerializeField] int keyHintOrder = -10;

        /// <summary>Raised with the new state whenever the torch is switched.</summary>
        public event Action<bool> Toggled;

        public bool IsOn { get; private set; }

        /// <summary>The toggle action (keyboard F, gamepad d-pad up), for prompts and glyphs.</summary>
        public InputAction ToggleInput => _toggle;

        Light _light, _fill;
        float _intensity, _switchedAt = float.NegativeInfinity;
        InputAction _toggle;

        void Awake()
        {
            _light = GetComponent<Light>();
            _intensity = _light.intensity;
            IsOn = startOn;
            _toggle = new InputAction("Flashlight", InputActionType.Button);
            _toggle.AddBinding("<Keyboard>/f");
            _toggle.AddBinding("<Gamepad>/dpad/up");
            SetUpViewmodel();
            Apply();
        }

        void Start()
        {
            // Every light that is not baked-only keeps lighting the hands (the baked ones do not touch dynamic objects anyway).
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l != _light && l != _fill && l.bakingOutput.lightmapBakeType != LightmapBakeType.Baked) ViewmodelLighting.AddToLight(l);
        }

        /// <summary>Hands off the torch (Viewmodel layer only), and a dim short-range fill that lights only them.</summary>
        void SetUpViewmodel()
        {
            var root = viewmodelRoot != null ? viewmodelRoot : transform.parent;
            if (root != null) ViewmodelLighting.SetHeld(root.gameObject, true);
            ViewmodelLighting.RemoveFromLight(_light);

            var go = new GameObject("ViewmodelFill");
            go.transform.SetParent(transform, false);
            _fill = go.AddComponent<Light>();
            _fill.type = LightType.Spot;
            _fill.color = _light.color;
            _fill.spotAngle = _light.spotAngle;
            _fill.innerSpotAngle = _light.innerSpotAngle;
            _fill.range = viewmodelFillRange;
            _fill.shadows = LightShadows.None;
            ViewmodelLighting.SetViewmodelOnly(_fill);
        }

        void OnEnable() => _toggle?.Enable();
        void OnDisable() => _toggle?.Disable();
        void OnDestroy() => _toggle?.Dispose();

        void Update()
        {
            if (_toggle.WasPressedThisFrame()) Set(!IsOn);
            Apply();
            if (showKeyHint) BlinkPrompts.Show(this, _toggle, "F", "Flashlight", -1f, keyHintOrder);
        }

        public void Set(bool on)
        {
            if (on == IsOn) return;
            IsOn = on;
            _switchedAt = Time.unscaledTime;
            Apply();
            Toggled?.Invoke(on);
        }

        void Apply()
        {
            float level = FlashlightMath.Level(IsOn, Time.unscaledTime - _switchedAt, switchOnSeconds);
            _light.intensity = _intensity * level;
            _light.enabled = level > 0f;
            if (_fill == null) return;
            _fill.intensity = viewmodelFillIntensity * level;
            _fill.enabled = level > 0f;
        }
    }

    /// <summary>Pure brightness curve for <see cref="Flashlight"/>.</summary>
    public static class FlashlightMath
    {
        /// <summary>
        /// Brightness 0..1. Off is 0. On: a short stutter over <paramref name="duration"/> real seconds
        /// (dim, a near-black drop, a flash, then settling) and 1 afterwards.
        /// </summary>
        public static float Level(bool on, float sinceSwitch, float duration)
        {
            if (!on) return 0f;
            if (duration <= 0f || float.IsNaN(sinceSwitch) || sinceSwitch >= duration || sinceSwitch < 0f) return 1f;
            float t = sinceSwitch / duration;
            if (t < 0.25f) return 0.55f;
            if (t < 0.45f) return 0.08f;
            if (t < 0.6f) return 1f;
            if (t < 0.75f) return 0.6f;
            return 1f;
        }
    }
}
