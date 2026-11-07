using Blink.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.Effects
{
    /// <summary>
    /// Draws a brief, thin tracer streak from the gun's muzzle to where the shot landed (or to the end of its range on
    /// a miss), plus an optional faint smoke wisp at the muzzle. Listens to <see cref="PlayerWeapons.RayFired"/>, so it
    /// can live anywhere; if no hub is assigned it finds the player's at runtime. Uses a small pool of LineRenderers.
    /// Runs on scaled time: it's a world effect, so fast-forward speeds it up like everything else.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BulletTracer : MonoBehaviour
    {
        const int TracerPoints = 6;
        const int SmokePoints = 6;
        const float SearchInterval = 1f;

        [Tooltip("Weapon hub to listen to. If empty, the player's (PlayerWeapons.Instance) is used, searched again if it disappears.")]
        [SerializeField] PlayerWeapons weapons;
        [Tooltip("Camera the player aims from (for the tracer width). If empty, the camera under the hub, then Camera.main.")]
        [SerializeField] Camera aimCamera;

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
        Transform _poolRoot;
        PlayerWeapons _subscribed;
        float _nextSearchTime;

        void Awake() => BuildPool();

        void Start() => FindWeapons();

        void OnEnable()
        {
            if (weapons != null) Subscribe(weapons);
        }

        void OnDisable()
        {
            Unsubscribe();
            if (_slots == null) return;
            foreach (var slot in _slots) slot.Hide();
        }

        void Update()
        {
            if (_subscribed == null && Time.unscaledTime >= _nextSearchTime) FindWeapons();
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

        void OnRayFired(ShotRay shot)
        {
            Vector3 from = shot.Muzzle != null ? shot.Muzzle.position : shot.Ray.origin + shot.Ray.direction * 0.3f;
            Spawn(from, TracerFlight.EndPoint(shot.Hit, shot.Info.point, shot.Ray, shot.Range));
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

        void FindWeapons()
        {
            _nextSearchTime = Time.unscaledTime + SearchInterval;
            if (weapons == null) weapons = PlayerWeapons.Instance != null ? PlayerWeapons.Instance : FindAnyObjectByType<PlayerWeapons>();
            if (weapons != null && isActiveAndEnabled) Subscribe(weapons);
        }

        void Subscribe(PlayerWeapons target)
        {
            if (_subscribed == target) return;
            Unsubscribe();
            _subscribed = target;
            _subscribed.RayFired += OnRayFired;
        }

        void Unsubscribe()
        {
            if (_subscribed != null) _subscribed.RayFired -= OnRayFired;
            _subscribed = null;
        }

        Camera ResolveCamera()
        {
            if (aimCamera != null) return aimCamera;
            if (_subscribed != null) aimCamera = _subscribed.GetComponentInChildren<Camera>();
            return aimCamera != null ? aimCamera : Camera.main;
        }

        void BuildPool()
        {
            _poolRoot = new GameObject("BulletTracers").transform;
            _poolRoot.SetParent(transform, false);
            _slots = new Slot[poolSize];
            for (int i = 0; i < poolSize; i++) _slots[i] = CreateSlot(i);
        }

        Slot CreateSlot(int i) => new()
        {
            Tracer = CreateLine(_poolRoot, "Tracer " + i, tracerMaterial, TracerPoints, false),
            Smoke = CreateLine(_poolRoot, "Smoke " + i, smokeMaterial, SmokePoints, true),
        };

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
