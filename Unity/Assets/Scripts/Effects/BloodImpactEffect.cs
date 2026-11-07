using System;
using UnityEngine;

namespace Blink.Effects
{
    /// <summary>
    /// One pooled blood effect (the root of a BloodImpact or BloodFloorSplat prefab). The spawner adds it at warm-up
    /// if the prefab doesn't carry it. Places and plays the particle hierarchy, fast-forwards it through time skips and
    /// reports when it has finished (the root's Stop Action must be Callback).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BloodImpactEffect : MonoBehaviour
    {
        const string DropletsChildName = "Droplets";

        /// <summary>Raised from OnParticleSystemStopped, once the whole effect has played out.</summary>
        public event Action<BloodImpactEffect> Stopped;

        public ParticleSystem Particles { get; private set; }

        /// <summary>True while the effect is out of the pool.</summary>
        public bool InUse { get; set; }

        /// <summary>
        /// True once the whole effect has played out. Unlike <c>ParticleSystem.IsAlive</c> this also works on a system
        /// that <c>Simulate</c> left paused at its end.
        /// </summary>
        public bool IsFinished
        {
            get
            {
                Initialise();
                if (Particles == null || !Particles.IsAlive(true)) return true;
                foreach (var ps in _systems)
                {
                    if (ps == null) continue;
                    var main = ps.main;
                    if (!BloodSpray.IsSpent(main.loop, ps.time, main.duration, ps.particleCount)) return false;
                }
                return true;
            }
        }

        Transform _droplets;
        ParticleSystem[] _systems;
        ParticleSystem.Burst[][] _baseBursts;
        float[] _baseRates;
        int[] _baseMaxParticles;
        float _countMultiplier = 1f;
        Vector3 _baseScale = Vector3.one;
        bool _initialised;

        void Awake() => Initialise();

        void Initialise()
        {
            if (_initialised) return;
            _initialised = true;
            Particles = GetComponent<ParticleSystem>();
            _systems = GetComponentsInChildren<ParticleSystem>(true);
            _droplets = transform.Find(DropletsChildName);
            _baseScale = transform.localScale;

            // Remember the authored emission, so a headshot can scale it up and the next hit put it back.
            _baseBursts = new ParticleSystem.Burst[_systems.Length][];
            _baseRates = new float[_systems.Length];
            _baseMaxParticles = new int[_systems.Length];
            for (int i = 0; i < _systems.Length; i++)
            {
                var emission = _systems[i].emission;
                _baseBursts[i] = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(_baseBursts[i]);
                _baseRates[i] = emission.rateOverTimeMultiplier;
                _baseMaxParticles[i] = _systems[i].main.maxParticles;
            }
        }

        /// <summary>
        /// Places the effect and plays it from the start.
        /// </summary>
        /// <param name="rotation">Root rotation (local +Y is the way the splat quads face).</param>
        /// <param name="scale">Multiplier on the prefab's own scale.</param>
        /// <param name="dropletDirection">Which way the droplet cone fires; zero leaves it along the root's +Y.</param>
        /// <param name="countMultiplier">Multiplier on every system's particle count (bursts and rate); 1 = as authored.</param>
        public void Play(Vector3 position, Quaternion rotation, float scale, Vector3 dropletDirection, float countMultiplier = 1f)
        {
            Initialise();
            SetCountMultiplier(countMultiplier);
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = _baseScale * scale;
            if (_droplets != null)
                _droplets.rotation = dropletDirection.sqrMagnitude > 1e-6f ? BloodSpray.FacingRotation(dropletDirection) : rotation;
            Particles.Play(true);
        }

        /// <summary>
        /// Jumps the effect forward by <paramref name="seconds"/> of world time in steps of at most
        /// <paramref name="maxStep"/>, then lets it carry on playing. Returns false if it finished during the jump
        /// (a system that ends inside Simulate may never raise its stop callback, so the caller should release it).
        /// </summary>
        public bool Advance(float seconds, float maxStep)
        {
            int steps = BloodSpray.SkipSteps(seconds, maxStep, out float step);
            if (steps == 0) return !IsFinished;
            for (int i = 0; i < steps; i++) Particles.Simulate(step, true, false, false);
            // Check before Play: playing a system that has finished would restart it.
            if (IsFinished) return false;
            Particles.Play(true);
            return true;
        }

        /// <summary>Stops and clears every particle immediately.</summary>
        public void Clear()
        {
            if (Particles != null) Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void SetCountMultiplier(float multiplier)
        {
            multiplier = Mathf.Max(1f, multiplier);
            if (Mathf.Approximately(multiplier, _countMultiplier)) return;
            _countMultiplier = multiplier;
            for (int i = 0; i < _systems.Length; i++)
            {
                var ps = _systems[i];
                if (ps == null) continue;
                var emission = ps.emission;
                var bursts = _baseBursts[i];
                for (int b = 0; b < bursts.Length; b++)
                {
                    var burst = bursts[b];
                    burst.count = ScaleCurve(burst.count, multiplier);
                    emission.SetBurst(b, burst);
                }
                emission.rateOverTimeMultiplier = _baseRates[i] * multiplier;
                var main = ps.main;
                main.maxParticles = BloodBurst.ScaleCount(_baseMaxParticles[i], multiplier);
            }
        }

        static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float multiplier)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return new ParticleSystem.MinMaxCurve(curve.constant * multiplier);
                case ParticleSystemCurveMode.TwoConstants:
                    return new ParticleSystem.MinMaxCurve(curve.constantMin * multiplier, curve.constantMax * multiplier);
                default:
                    curve.curveMultiplier *= multiplier;
                    return curve;
            }
        }

        void OnParticleSystemStopped() => Stopped?.Invoke(this);
    }
}
