using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.Effects
{
    /// <summary>
    /// One-shot particle bursts for weapon hits on the world: plaster dust, wood splinters, sparks, glass shards,
    /// splashes. Built entirely in code: a handful of pooled, always-playing <see cref="ParticleSystem"/>s (one per
    /// look, shared by every <see cref="SurfaceKind"/>) under a DontDestroyOnLoad root, fed with
    /// <c>Emit(EmitParams)</c> at the hit, so overlapping bursts need no extra systems. Called by
    /// <see cref="ImpactMarks"/>. Runs on scaled time, so time-skip fast-forwards it. See docs/ENVIRONMENT_DAMAGE.md.
    /// </summary>
    public static class ImpactParticles
    {
        // URP's particle shader. BulletTracer.mat uses it with the same transparent keyword, so the variant is in builds.
        // ponytail: relies on that material shipping; move a particle material into Resources if the tracer ever goes.
        const string ShaderName = "Universal Render Pipeline/Particles/Unlit";
        const int MaxParticles = 300;
        const int TextureSize = 32;
        const float VelocityStretch = 0.03f;
        const float SurfaceOffset = 0.02f;

        /// <summary>How a particle looks and moves. Each has one shared system; colour, size and speed come per burst.</summary>
        enum Look
        {
            /// <summary>Soft puff that slows, grows and fades.</summary>
            Dust,
            /// <summary>Small chunks, chips and shards that tumble down.</summary>
            Bits,
            /// <summary>Stretched along velocity, falling: splinters, droplets.</summary>
            Streaks,
            /// <summary>Additive stretched sparks with light gravity.</summary>
            Sparks,
            /// <summary>Additive blink at the hit point.</summary>
            Flash,
            /// <summary>Light bits that drift down slowly: paper, fluff.</summary>
            Flutter,
        }

        static Transform _root;
        static Material _alpha, _additive;
        static ParticleSystem[] _systems;
        static bool _unavailable;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _root = null;
            _alpha = _additive = null;
            _systems = null;
            _unavailable = false;
        }

        /// <summary>One-shot burst at <paramref name="point"/> along <paramref name="normal"/>. strength 1 = a bullet.</summary>
        public static void Burst(SurfaceKind surface, Vector3 point, Vector3 normal, float strength)
        {
            if (!Ready()) return;
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            float s = Mathf.Max(0.1f, strength);
            point += normal * SurfaceOffset;

            switch (surface)
            {
                case SurfaceKind.Wood:
                    Spray(Look.Streaks, Count(6, s), point, normal, 0.7f, 3f, 0.01f, 0.02f, 0.5f, 0.9f,
                        new Color(0.42f, 0.28f, 0.16f), new Color(0.62f, 0.45f, 0.27f));
                    Spray(Look.Dust, Count(2, s), point, normal, 0.6f, 0.7f, 0.06f, 0.11f, 0.5f, 0.8f,
                        new Color(0.6f, 0.5f, 0.38f, 0.3f), new Color(0.7f, 0.6f, 0.48f, 0.25f));
                    break;
                case SurfaceKind.Stone:
                    Spray(Look.Dust, Count(5, s), point, normal, 0.6f, 0.9f, 0.08f, 0.15f, 0.7f, 1.1f,
                        new Color(0.5f, 0.5f, 0.48f, 0.55f), new Color(0.62f, 0.61f, 0.58f, 0.45f));
                    Spray(Look.Bits, Count(3, s), point, normal, 0.8f, 2f, 0.008f, 0.02f, 0.5f, 0.9f,
                        new Color(0.42f, 0.42f, 0.4f), new Color(0.58f, 0.57f, 0.54f));
                    Spray(Look.Sparks, Random.Range(1, 4), point, normal, 0.9f, 4f, 0.01f, 0.015f, 0.1f, 0.25f,
                        new Color(1f, 0.75f, 0.4f), new Color(1f, 0.55f, 0.2f));
                    break;
                case SurfaceKind.Metal:
                    Spray(Look.Sparks, Count(10, s), point, normal, 0.9f, 5f, 0.012f, 0.02f, 0.15f, 0.35f,
                        new Color(1f, 0.7f, 0.3f), new Color(1f, 0.45f, 0.1f));
                    Spray(Look.Flash, 1, point, normal, 0f, 0f, 0.1f * s, 0.14f * s, 0.05f, 0.07f,
                        new Color(1f, 0.85f, 0.55f, 0.8f), new Color(1f, 0.75f, 0.45f, 0.8f));
                    break;
                case SurfaceKind.Glass:
                    Spray(Look.Bits, Count(8, s), point, normal, 1f, 2f, 0.008f, 0.02f, 0.6f, 1f,
                        new Color(0.9f, 0.95f, 1f, 0.85f), new Color(1f, 1f, 1f, 0.45f));
                    break;
                case SurfaceKind.Fabric:
                    Spray(Look.Dust, Count(4, s), point, normal, 0.7f, 0.6f, 0.05f, 0.09f, 0.6f, 1f,
                        new Color(0.82f, 0.8f, 0.76f, 0.4f), new Color(0.72f, 0.68f, 0.62f, 0.35f));
                    Spray(Look.Flutter, Count(3, s), point, normal, 1f, 1.2f, 0.008f, 0.015f, 1f, 1.6f,
                        new Color(0.85f, 0.83f, 0.8f), new Color(0.7f, 0.66f, 0.6f));
                    break;
                case SurfaceKind.Porcelain:
                    Spray(Look.Bits, Count(7, s), point, normal, 0.9f, 2.5f, 0.008f, 0.018f, 0.6f, 1f,
                        new Color(0.96f, 0.96f, 0.94f), new Color(0.88f, 0.88f, 0.85f));
                    break;
                case SurfaceKind.Paper:
                    Spray(Look.Flutter, Count(7, s), point, normal, 1f, 1.5f, 0.015f, 0.03f, 1.2f, 2f,
                        new Color(0.93f, 0.91f, 0.84f), new Color(0.78f, 0.72f, 0.58f));
                    break;
                case SurfaceKind.Water:
                    Spray(Look.Streaks, Count(10, s), point, normal, 0.35f, 2.5f, 0.01f, 0.02f, 0.4f, 0.7f,
                        new Color(0.75f, 0.85f, 0.95f, 0.6f), new Color(0.9f, 0.95f, 1f, 0.45f));
                    Ring(Count(12, s), point, normal, 1.1f, 0.012f, 0.3f, 0.5f, new Color(0.85f, 0.92f, 1f, 0.5f));
                    break;
                default: // Plaster
                    Spray(Look.Dust, Count(4, s), point, normal, 0.6f, 0.8f, 0.08f, 0.16f, 0.6f, 1f,
                        new Color(0.86f, 0.84f, 0.79f, 0.5f), new Color(0.78f, 0.76f, 0.7f, 0.4f));
                    Spray(Look.Bits, Count(5, s), point, normal, 0.8f, 2f, 0.01f, 0.025f, 0.6f, 1f,
                        new Color(0.82f, 0.8f, 0.74f), new Color(0.7f, 0.68f, 0.62f));
                    break;
            }
        }

        static int Count(int bullet, float strength) => Mathf.Max(1, Mathf.RoundToInt(bullet * strength));

        /// <summary>
        /// Emits <paramref name="count"/> particles out of the surface: directions within a cone around
        /// <paramref name="normal"/> (<paramref name="spread"/> 0 = straight out, 1 = wide), speed up to
        /// <paramref name="speed"/>, size, lifetime and colour picked between the given ranges.
        /// </summary>
        static void Spray(Look look, int count, Vector3 point, Vector3 normal, float spread, float speed,
            float minSize, float maxSize, float minLife, float maxLife, Color a, Color b)
        {
            var ps = For(look);
            for (int i = 0; i < count; i++)
            {
                var dir = (normal + Random.insideUnitSphere * spread).normalized;
                EmitOne(ps, look, point, dir * (speed * Random.Range(0.4f, 1f)), Random.Range(minSize, maxSize),
                    Random.Range(minLife, maxLife), Color.Lerp(a, b, Random.value));
            }
        }

        /// <summary>Droplets thrown outward along the surface, for a splash ring.</summary>
        static void Ring(int count, Vector3 point, Vector3 normal, float speed, float size, float minLife, float maxLife, Color color)
        {
            var ps = For(Look.Streaks);
            var tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.99f ? Vector3.up : Vector3.forward).normalized;
            for (int i = 0; i < count; i++)
            {
                var dir = Quaternion.AngleAxis(360f * i / count + Random.Range(-10f, 10f), normal) * tangent + normal * 0.4f;
                EmitOne(ps, Look.Streaks, point, dir * (speed * Random.Range(0.8f, 1.1f)), size,
                    Random.Range(minLife, maxLife), color);
            }
        }

        static void EmitOne(ParticleSystem ps, Look look, Vector3 position, Vector3 velocity, float size, float life, Color color)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = life,
                startColor = color,
                rotation = Random.Range(0f, 360f),
            };
            if (look is Look.Bits or Look.Flutter) e.angularVelocity = Random.Range(-540f, 540f);
            ps.Emit(e, 1);
        }

        // ponytail: MaxParticles per look; past that Unity drops new particles, raise it if heavy fire looks thin.
        static ParticleSystem For(Look look)
        {
            int i = (int)look;
            if (_systems[i] == null) _systems[i] = Create(look);
            return _systems[i];
        }

        /// <summary>Lazily builds the root, texture and materials. False (with one warning) if the shader is missing.</summary>
        static bool Ready()
        {
            if (_unavailable || !Application.isPlaying) return false; // effects only; edit-mode tests skip them
            if (_root != null) return true;

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                _unavailable = true;
                Debug.LogWarning($"[ImpactParticles] Shader '{ShaderName}' not found; no impact particles.");
                return false;
            }

            var go = new GameObject(nameof(ImpactParticles));
            Object.DontDestroyOnLoad(go);
            _root = go.transform;
            _systems = new ParticleSystem[System.Enum.GetValues(typeof(Look)).Length];
            if (_alpha == null)
            {
                var tex = SoftDot();
                _alpha = MakeMaterial(shader, tex, false);
                _additive = MakeMaterial(shader, tex, true);
            }
            return true;
        }

        static ParticleSystem Create(Look look)
        {
            var (additive, stretch, gravity, drag) = look switch
            {
                Look.Dust => (false, 0f, 0f, 4f),
                Look.Bits => (false, 0f, 1f, 0f),
                Look.Streaks => (false, 2f, 1f, 0f),
                Look.Sparks => (true, 1f, 0.6f, 0f),
                Look.Flash => (true, 0f, 0f, 0f),
                _ => (false, 0f, 0.1f, 3f), // Flutter
            };

            var go = new GameObject("Impact " + look);
            go.transform.SetParent(_root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.maxParticles = MaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            // Hold full alpha for most of the life, then fade out.
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = fade;

            if (look == Look.Dust)
            {
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(2.5f, AnimationCurve.Linear(0f, 0.4f, 1f, 1f));
            }

            if (drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = 100f;
                limit.dampen = 0f;
                limit.drag = drag;
                limit.multiplyDragByParticleSize = false;
                limit.multiplyDragByParticleVelocity = false;
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = additive ? _additive : _alpha;
            r.renderMode = stretch > 0f ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            r.lengthScale = Mathf.Max(1f, stretch);
            r.velocityScale = VelocityStretch;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            // ponytail: one always-playing system per look, all at the origin; transparency sorts per system, not per burst.
            ps.Play();
            return ps;
        }

        /// <summary>Transparent URP particle material, alpha-blended or additive.</summary>
        static Material MakeMaterial(Shader shader, Texture2D tex, bool additive)
        {
            var m = new Material(shader) { name = additive ? "ImpactParticles Additive" : "ImpactParticles Alpha" };
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        /// <summary>White dot with a soft edge: small enough to read as a chunk, large enough to read as a puff.</summary>
        static Texture2D SoftDot()
        {
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = "ImpactParticles Dot",
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[TextureSize * TextureSize];
            float half = (TextureSize - 1) * 0.5f;
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float d = new Vector2(x - half, y - half).magnitude / half;
                px[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(1f - d * d) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
