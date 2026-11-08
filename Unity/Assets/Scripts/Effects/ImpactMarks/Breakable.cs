using System;
using Blink.Audio;
using Blink.Visuals;
using Blink.World;
using UnityEngine;

namespace Blink.Effects
{
    /// <summary>
    /// Something a weapon can break: a window pane, a plate, a lamp, a chandelier. Fed by <see cref="ImpactProps"/>
    /// (<see cref="Hit"/>). On the last hit it bursts (<see cref="ImpactParticles"/>), plays its cue and makes noise,
    /// then: with <see cref="lights"/>, puts them out (flicker, flames, glowing bulbs too); with a kinematic
    /// <see cref="Rigidbody"/>, lets it fall (the chandelier); with neither, the part itself disappears (glass, plate).
    /// See docs/ENVIRONMENT_DAMAGE.md.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Breakable : MonoBehaviour
    {
        [SerializeField, Min(1)] int hitsToBreak = 1;
        [Tooltip("Lights put out when it breaks. Empty = the part itself disappears instead (unless it can fall).")]
        [SerializeField] Light[] lights = Array.Empty<Light>();
        [SerializeField] SoundCue breakCue;
        [Tooltip("Particle burst strength (1 = a bullet).")]
        [SerializeField, Min(0f)] float burstStrength = 2.5f;
        [Tooltip("Audible range of the break, metres (glass smashing draws zombies).")]
        [SerializeField, Min(0f)] float breakLoudness = 12f;
        [Tooltip("Only a blade (the axe) counts as a hit: bullets and blunt items just mark it (boarded-up planks).")]
        [SerializeField] bool bladeOnly;

        int _hits;

        public bool Broken { get; private set; }
        public int HitsToBreak => hitsToBreak;
        public bool BladeOnly => bladeOnly;

        /// <summary>Raised once, when it breaks.</summary>
        public event Action Smashed;

        /// <summary>Set up one added at runtime.</summary>
        public void Configure(int hits, Light[] lightsToKill, bool blade = false)
        {
            hitsToBreak = Mathf.Max(1, hits);
            lights = lightsToKill ?? Array.Empty<Light>();
            bladeOnly = blade;
        }

        /// <summary>Hits a material takes to break when auto-attached: glass 1, porcelain 2, anything else 0 (not breakable).</summary>
        public static int AutoHits(string materialName) => SurfaceTable.FromName(materialName) switch
        {
            SurfaceKind.Glass => 1,
            SurfaceKind.Porcelain => 2,
            _ => 0,
        };

        /// <summary>One weapon hit at <paramref name="point"/>. Returns true if this one broke it.</summary>
        public bool Hit(Vector3 point, Vector3 normal)
        {
            if (Broken || ++_hits < hitsToBreak) return false;
            Break(point, normal);
            return true;
        }

        void Break(Vector3 point, Vector3 normal)
        {
            Broken = true;
            bool hasLights = lights != null && lights.Length > 0;
            ImpactParticles.Burst(hasLights ? SurfaceKind.Glass : OwnSurface(), point, normal, burstStrength);
            if (breakCue != null && AudioService.Instance != null) AudioService.Instance.PlayAt(breakCue, point);
            Noise.Make(point, breakLoudness);
            Smashed?.Invoke();

            if (hasLights) PutOut();
            if (TryGetComponent(out Rigidbody body) && body.isKinematic)
            {
                body.isKinematic = false; // the chandelier comes down
                body.WakeUp();
                return;
            }
            if (!hasLights) gameObject.SetActive(false); // the pane / plate is gone (renderers, colliders, its marks)
        }

        void PutOut()
        {
            foreach (var l in lights)
            {
                if (l == null) continue;
                if (l.TryGetComponent(out LightFlicker flicker)) flicker.enabled = false; // it would keep writing intensity
                l.enabled = false;
            }
            foreach (var ps in GetComponentsInChildren<ParticleSystem>())
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                var mat = r.sharedMaterial;
                if (mat == null) continue;
                var n = mat.name;
                // ponytail: flames/bulbs found by material name ("Flame", "Bulb"); add a renderer list if a model names them otherwise.
                if (n.IndexOf("flame", StringComparison.OrdinalIgnoreCase) >= 0) r.enabled = false;
                else if (n.IndexOf("bulb", StringComparison.OrdinalIgnoreCase) >= 0 && mat.HasProperty(EmissionColor))
                    r.material.SetColor(EmissionColor, Color.black); // instanced: the other bulbs stay lit
            }
        }

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        SurfaceKind OwnSurface()
        {
            var r = GetComponentInChildren<Renderer>();
            return r != null && r.sharedMaterial != null ? SurfaceTable.FromName(r.sharedMaterial.name) : SurfaceKind.Glass;
        }
    }
}
