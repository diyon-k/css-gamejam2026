using Blink.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.Effects
{
    /// <summary>
    /// Draws a brief, thin tracer streak from the gun's muzzle to where the shot landed (or to the end of the
    /// crosshair ray on a miss), plus an optional faint smoke wisp at the muzzle. Listens to <see cref="Gun.Fired"/>,
    /// so it can live anywhere; if no gun is assigned it finds one at runtime. Uses a small pool of LineRenderers.
    /// Runs on scaled time: it's a world effect, so fast-forward speeds it up like everything else.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BulletTracer : MonoBehaviour
    {
        const int TracerPoints = 6;
        const int SmokePoints = 6;
        const float GunSearchInterval = 1f;

        [Tooltip("Gun to listen to. If empty, the first Gun in the scene is used (searched again if it disappears).")]
        [SerializeField] Gun gun;
        [Tooltip("Camera the gun aims from. If empty, the camera above the gun, then Camera.main.")]
        [SerializeField] Camera aimCamera;
        [Tooltip("Where a missed shot's tracer ends along the crosshair ray. Match the gun's range.")]
        [SerializeField, Min(1f)] float missDistance = 60f;

        [Header("Tracer")]
        [Tooltip("Additive URP particle material (HDR base colour, so it blooms if the volume has bloom).")]
        [SerializeField] Material tracerMaterial;
        [Tooltip("Vertex tint; the material's base colour carries the HDR intensity.")]
        [SerializeField] Color tracerTint = new(1f, 0.82f, 0.58f, 1f);
        [Tooltip("Nominal bullet speed in m/s. The lifetime below wins if they disagree.")]
        [SerializeField, Min(1f)] float speed = 700f;
        [Tooltip("Seconds on screen, clamped so a point-blank shot is still readable and a long one is still quick.")]
        [SerializeField] Vector2 lifetimeRange = new(0.06f, 0.1f);
        [Tooltip("Streak length as a fraction of the shot distance, before the clamp below.")]
        [SerializeField, Range(0f, 1f)] float streakFraction = 0.6f;
        [Tooltip("Shortest and longest streak in metres.")]
        [SerializeField] Vector2 streakLengthRange = new(0.5f, 12f);
        [Tooltip("Width as an angle (radians) so it looks equally thin near and far. 0.001 is about one pixel at 1080p.")]
        [SerializeField, Min(0f)] float angularWidth = 0.0045f;
        [Tooltip("Min and max world width in metres.")]
        [SerializeField] Vector2 widthRange = new(0.003f, 0.08f);
        [Tooltip("Width of the tail relative to the head.")]
        [SerializeField, Range(0f, 1f)] float tailWidth = 0.25f;

        [Header("Smoke")]
        [SerializeField] bool smoke = true;
        [Tooltip("Alpha-blended, preferably lit, so it only shows where the flashlight hits it.")]
        [SerializeField] Material smokeMaterial;
        [SerializeField] Color smokeColor = new(0.35f, 0.35f, 0.35f, 0.09f);
        [SerializeField, Min(0.01f)] float smokeLifetime = 0.45f;
        [Tooltip("How far along the shot the wisp reaches, in metres.")]
        [SerializeField, Min(0f)] float smokeLength = 0.5f;
        [Tooltip("Width at birth and at death, in metres.")]
        [SerializeField] Vector2 smokeWidth = new(0.025f, 0.1f);
        [Tooltip("Upward drift in m/s.")]
        [SerializeField] float smokeRise = 0.25f;

        [Header("Pool")]
        [SerializeField, Min(1)] int poolSize = 4;

        Slot[] _slots;
        int _next;
        Gun _subscribed;
        float _nextSearchTime;

        void Awake() => BuildPool();

        void Start() => FindGun();

        void OnEnable()
        {
            if (gun != null) Subscribe(gun);
        }

        void OnDisable()
        {
            Unsubscribe();
            if (_slots == null) return;
            foreach (var slot in _slots) slot.Hide();
        }

        void Update()
        {
            if (_subscribed == null && Time.unscaledTime >= _nextSearchTime) FindGun();
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            var cam = ResolveCamera();
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            foreach (var slot in _slots)
            {
                if (slot.TracerActive) UpdateTracer(slot, dt, eye);
                if (slot.SmokeActive) UpdateSmoke(slot, dt);
            }
        }

        /// <summary>Spawns a tracer between two points. Public so other weapons (or tests in the editor) can use it.</summary>
        public void Spawn(Vector3 from, Vector3 to)
        {
            Vector3 path = to - from;
            float distance = path.magnitude;
            if (distance < 0.05f || _slots == null) return;

            var slot = _slots[_next];
            _next = (_next + 1) % _slots.Length;

            slot.Start = from;
            slot.Direction = path / distance;
            slot.Flight = TracerFlight.Create(distance, streakFraction, streakLengthRange.x, streakLengthRange.y,
                speed, lifetimeRange.x, lifetimeRange.y);
            slot.TracerActive = tracerMaterial != null;

            slot.SmokeActive = smoke && smokeMaterial != null && smokeLength > 0f;
            slot.SmokeAge = 0f;
            slot.SmokeLength = Mathf.Min(smokeLength, distance);
            slot.SmokeSeed = Random.value * 100f;
        }

        void OnFired(bool hit, RaycastHit info)
        {
            var cam = ResolveCamera();
            if (cam == null) return;
            var aim = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var muzzle = _subscribed != null ? _subscribed.Muzzle : null;
            Vector3 from = muzzle != null ? muzzle.position : aim.origin + aim.direction * 0.3f;
            Spawn(from, TracerFlight.EndPoint(hit, info.point, aim, missDistance));
        }

        void UpdateTracer(Slot slot, float dt, Vector3 eye)
        {
            slot.Flight.Step(dt);
            if (slot.Flight.IsDone)
            {
                slot.TracerActive = false;
                slot.Tracer.enabled = false;
                return;
            }

            slot.Flight.Segment(out float tail, out float head);
            if (head - tail < 0.01f)
            {
                slot.Tracer.enabled = false;
                return;
            }

            for (int i = 0; i < TracerPoints; i++)
            {
                float s = i / (float)(TracerPoints - 1);
                Vector3 p = slot.Start + slot.Direction * Mathf.Lerp(tail, head, s);
                slot.TracerPositions[i] = p;
                float width = TracerFlight.ScreenConstantWidth(Vector3.Distance(eye, p), angularWidth, widthRange.x, widthRange.y);
                slot.WidthKeys[i] = new Keyframe(s, width * Mathf.Lerp(tailWidth, 1f, s));
            }
            slot.Tracer.SetPositions(slot.TracerPositions);
            slot.WidthCurve.keys = slot.WidthKeys;
            slot.Tracer.widthCurve = slot.WidthCurve;

            // Tail fades in from nothing, the head is hottest (whiter). First point is the tail.
            float brightness = tracerTint.a * slot.Flight.Brightness;
            slot.ColorKeys[0] = new GradientColorKey(tracerTint, 0f);
            slot.ColorKeys[1] = new GradientColorKey(Color.Lerp(tracerTint, Color.white, 0.6f), 1f);
            slot.AlphaKeys[0] = new GradientAlphaKey(0f, 0f);
            slot.AlphaKeys[1] = new GradientAlphaKey(0.6f * brightness, 0.4f);
            slot.AlphaKeys[2] = new GradientAlphaKey(brightness, 1f);
            slot.Colors.SetKeys(slot.ColorKeys, slot.AlphaKeys);
            slot.Tracer.colorGradient = slot.Colors;
            slot.Tracer.enabled = true;
        }

        void UpdateSmoke(Slot slot, float dt)
        {
            slot.SmokeAge += Mathf.Max(0f, dt);
            float t = slot.SmokeAge / smokeLifetime;
            if (t >= 1f)
            {
                slot.SmokeActive = false;
                slot.Smoke.enabled = false;
                return;
            }

            Vector3 side = Vector3.Cross(slot.Direction, Vector3.up);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            for (int i = 0; i < SmokePoints; i++)
            {
                float s = i / (float)(SmokePoints - 1);
                float curl = Mathf.Sin(slot.SmokeSeed + s * 6f + slot.SmokeAge * 4f) * 0.04f * t;
                slot.SmokePositions[i] = slot.Start
                    + slot.Direction * (slot.SmokeLength * s)
                    + Vector3.up * (smokeRise * slot.SmokeAge * (0.6f + s))
                    + side * curl;
            }
            slot.Smoke.SetPositions(slot.SmokePositions);
            slot.Smoke.widthMultiplier = Mathf.Lerp(smokeWidth.x, smokeWidth.y, Mathf.Sqrt(t));

            float fade = (1f - t) * (1f - t);
            var near = smokeColor;
            near.a *= fade;
            var far = near;
            far.a = 0f;
            slot.Smoke.startColor = near; // first point is at the muzzle
            slot.Smoke.endColor = far;
            slot.Smoke.enabled = true;
        }

        void FindGun()
        {
            _nextSearchTime = Time.unscaledTime + GunSearchInterval;
            if (gun == null) gun = FindAnyObjectByType<Gun>();
            if (gun != null && isActiveAndEnabled) Subscribe(gun);
        }

        void Subscribe(Gun target)
        {
            if (_subscribed == target) return;
            Unsubscribe();
            _subscribed = target;
            _subscribed.Fired += OnFired;
        }

        void Unsubscribe()
        {
            if (_subscribed != null) _subscribed.Fired -= OnFired;
            _subscribed = null;
        }

        Camera ResolveCamera()
        {
            if (aimCamera != null) return aimCamera;
            if (_subscribed != null) aimCamera = _subscribed.GetComponentInParent<Camera>();
            return aimCamera != null ? aimCamera : Camera.main;
        }

        void BuildPool()
        {
            var root = new GameObject("BulletTracers").transform;
            root.SetParent(transform, false);
            _slots = new Slot[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                _slots[i] = new Slot
                {
                    Tracer = CreateLine(root, "Tracer " + i, tracerMaterial, TracerPoints, false),
                    Smoke = CreateLine(root, "Smoke " + i, smokeMaterial, SmokePoints, true),
                };
            }
        }

        static LineRenderer CreateLine(Transform parent, string name, Material material, int points, bool lit)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = points;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            line.allowOcclusionWhenDynamic = false;
            line.generateLightingData = lit;
            line.enabled = false;
            return line;
        }

        /// <summary>One pooled tracer + smoke wisp.</summary>
        sealed class Slot
        {
            public LineRenderer Tracer;
            public LineRenderer Smoke;
            public readonly Vector3[] TracerPositions = new Vector3[TracerPoints];
            public readonly Vector3[] SmokePositions = new Vector3[SmokePoints];
            public readonly Keyframe[] WidthKeys = new Keyframe[TracerPoints];
            public readonly AnimationCurve WidthCurve = new();
            public readonly Gradient Colors = new();
            public readonly GradientColorKey[] ColorKeys = new GradientColorKey[2];
            public readonly GradientAlphaKey[] AlphaKeys = new GradientAlphaKey[3];

            public Vector3 Start;
            public Vector3 Direction;
            public TracerFlight Flight;
            public bool TracerActive;

            public bool SmokeActive;
            public float SmokeAge;
            public float SmokeLength;
            public float SmokeSeed;

            public void Hide()
            {
                TracerActive = SmokeActive = false;
                if (Tracer != null) Tracer.enabled = false;
                if (Smoke != null) Smoke.enabled = false;
            }
        }
    }
}
