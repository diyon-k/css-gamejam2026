using System;
using System.Collections.Generic;
using Blink.Interaction;
using Blink.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blink.Effects
{
    /// <summary>
    /// What world hits do to props (<see cref="ImpactMarks.WorldHit"/>): loose rigidbodies get knocked, melee on metal
    /// clangs (extra <see cref="Noise"/>; gunshots and other melee hits already make theirs), lamps, candles and
    /// chandeliers break and go dark (<see cref="Breakable"/>, added on first hit), and paintings swing crooked on a
    /// damped spring. At scene load it also makes small glass and porcelain objects breakable. Skips whatever the
    /// player is holding. Creates itself after the first scene loads, like <c>CorpseShotReaction</c>.
    /// See docs/ENVIRONMENT_DAMAGE.md, "Interactive props".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ImpactProps : MonoBehaviour
    {
        // ponytail: "is this a small prop or a wall/room?" is a bounds check; tag props explicitly if it misfires.
        const float MaxPropSize = 2.5f;

        public static ImpactProps Instance { get; private set; }

        [Header("Knock (impulse in N·s at the hit point)")]
        [SerializeField, Min(0f)] float bulletImpulse = 4f;
        [SerializeField, Min(0f)] float meleeImpulse = 6f;

        [Header("Noise")]
        [Tooltip("Audible range (m) of a melee blow on metal. Every melee hit already makes 6 m (MeleeWeapon), gunshots 35 m.")]
        [SerializeField, Min(0f)] float metalMeleeLoudness = 14f;

        [Header("Breakables")]
        [Tooltip("At scene load, make small glass/porcelain objects with their own collider breakable.")]
        [SerializeField] bool autoBreakables = true;
        [Tooltip("Hits a lamp, candle or chandelier takes before its light goes out.")]
        [SerializeField, Min(1)] int lightHitsToBreak = 1;

        [Header("Paintings (swing around the top edge, degrees)")]
        [SerializeField, Min(0f)] float paintingKick = 70f;
        [Tooltip("Each hit leaves it this much more crooked at most, either way.")]
        [SerializeField, Min(0f)] float paintingCrook = 4f;
        [SerializeField, Min(0f)] float paintingMaxTilt = 10f;
        [SerializeField, Min(0f)] float paintingStiffness = 60f;
        [SerializeField, Min(0f)] float paintingDamping = 5f;

        sealed class Swing
        {
            public Transform T;
            public Vector3 Pivot, Axis, BasePos;
            public Quaternion BaseRot;
            public float Angle, Velocity, Rest;
            public bool Settled;
        }

        readonly Dictionary<Transform, Swing> _swings = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null || FindAnyObjectByType<ImpactProps>() != null) return;
            var go = new GameObject(nameof(ImpactProps));
            DontDestroyOnLoad(go);
            go.AddComponent<ImpactProps>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnEnable()
        {
            if (Instance != this) return; // a duplicate, being destroyed
            ImpactMarks.WorldHit += OnWorldHit;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            ImpactMarks.WorldHit -= OnWorldHit;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // The first scene loaded before the bootstrap ran, so it's done here; later ones in OnSceneLoaded.
        void Start()
        {
            if (autoBreakables) AttachBreakables();
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _swings.Clear();
            if (autoBreakables) AttachBreakables();
        }

        void OnWorldHit(WorldImpact hit)
        {
            var c = hit.Collider;
            if (c == null || IsHeld(c)) return;

            if (hit.Melee && hit.Surface == SurfaceKind.Metal) Noise.Make(hit.Point, metalMeleeLoudness);

            // Break first, so a chandelier released by this hit also gets the knock.
            var breakable = c.GetComponentInParent<Breakable>();
            if (breakable == null) breakable = AddLightBreakable(c);
            if (breakable != null)
            {
                if (!breakable.BladeOnly || (hit.Melee && hit.Tool == MeleeTool.Blade)) breakable.Hit(hit.Point, hit.Normal);
            }
            else if (PaintingOf(c) is { } painting) Kick(painting, hit);

            var body = c.attachedRigidbody;
            if (body != null && !body.isKinematic && body.gameObject.activeInHierarchy)
                body.AddForceAtPosition(hit.Direction * (hit.Melee ? meleeImpulse : bulletImpulse), hit.Point, ForceMode.Impulse);
        }

        static bool IsHeld(Collider c)
        {
            var carry = c.GetComponentInParent<Carryable>();
            if (carry != null && Carryable.Held == carry) return true;
            var swing = c.GetComponentInParent<SwingableProp>();
            return swing != null && swing.IsHeld;
        }

        /// <summary>
        /// Makes the small prop around <paramref name="c"/> breakable if it carries a light: the collider's object or,
        /// for a collider on one part of a prop, its parent. Null if neither is a small lit prop.
        /// </summary>
        Breakable AddLightBreakable(Collider c)
        {
            // ponytail: looks one level up only (the house props are a group with one box collider and a light child).
            var t = c.transform;
            for (int up = 0; up < 2 && t != null; up++, t = t.parent)
            {
                var found = t.GetComponentsInChildren<Light>();
                if (found.Length == 0 || SizeOf(t) >= MaxPropSize) continue;
                var b = t.gameObject.AddComponent<Breakable>();
                b.Configure(lightHitsToBreak, found);
                return b;
            }
            return null;
        }

        /// <summary>The painting <paramref name="c"/> belongs to (the highest ancestor named like one), or null.</summary>
        static Transform PaintingOf(Collider c)
        {
            if (c.attachedRigidbody != null) return null;
            Transform found = null;
            for (var t = c.transform; t != null; t = t.parent)
                if (IsPaintingName(t.name) && SizeOf(t) < MaxPropSize) found = t;
            if (found == null && c.TryGetComponent(out Renderer r) && r.sharedMaterial != null
                && IsPaintingName(r.sharedMaterial.name) && SizeOf(c.transform) < MaxPropSize)
                found = c.transform;
            return found;
        }

        /// <summary>True for names like "Canvas_Moss", "PaintingCanvas", "fancy_picture_frame_01".</summary>
        public static bool IsPaintingName(string name) =>
            name.IndexOf("canvas", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("painting", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("picture", StringComparison.OrdinalIgnoreCase) >= 0;

        // Largest side of everything rendered under t (its collider if nothing is).
        static float SizeOf(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            Bounds b;
            if (rs.Length > 0)
            {
                b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
            }
            else if (t.TryGetComponent(out Collider c)) b = c.bounds;
            else return 0f;
            return Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        }

        // ---- Paintings ----

        void Kick(Transform painting, WorldImpact hit)
        {
            if (!_swings.TryGetValue(painting, out var s))
            {
                var axis = Vector3.ProjectOnPlane(hit.Normal, Vector3.up); // out of the wall
                if (axis.sqrMagnitude < 1e-4f) return; // hit on its top or bottom edge
                var rs = painting.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) return;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                s = new Swing
                {
                    T = painting,
                    Pivot = new Vector3(b.center.x, b.max.y, b.center.z), // the nail
                    Axis = axis.normalized,
                    BasePos = painting.position,
                    BaseRot = painting.rotation,
                };
                _swings.Add(painting, s);
            }
            float sign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            s.Velocity += sign * paintingKick * (hit.Melee ? 1.5f : 1f);
            s.Rest = Mathf.Clamp(s.Rest + UnityEngine.Random.Range(-paintingCrook, paintingCrook), -paintingMaxTilt, paintingMaxTilt);
            s.Settled = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _swings.Count == 0) return;
            foreach (var s in _swings.Values)
            {
                if (s.Settled || s.T == null) continue;
                s.Velocity += (-paintingStiffness * (s.Angle - s.Rest) - paintingDamping * s.Velocity) * dt;
                s.Angle = Mathf.Clamp(s.Angle + s.Velocity * dt, -paintingMaxTilt * 2f, paintingMaxTilt * 2f);
                if (Mathf.Abs(s.Velocity) < 0.05f && Mathf.Abs(s.Angle - s.Rest) < 0.05f)
                {
                    s.Angle = s.Rest;
                    s.Settled = true;
                }
                var q = Quaternion.AngleAxis(s.Angle, s.Axis);
                s.T.SetPositionAndRotation(s.Pivot + q * (s.BasePos - s.Pivot), q * s.BaseRot);
            }
        }

        // ---- Auto breakables ----

        /// <summary>
        /// Adds a <see cref="Breakable"/> to every small renderer with a glass/porcelain material and its own collider
        /// (<see cref="Breakable.AutoHits"/>). Returns how many were added.
        /// </summary>
        public int AttachBreakables()
        {
            int added = 0;
            foreach (var r in FindObjectsByType<Renderer>())
            {
                var mat = r.sharedMaterial;
                if (mat == null) continue;
                int hits = Breakable.AutoHits(mat.name);
                // ponytail: own collider + under MaxPropSize keeps whole walls with a window material out; a Breakable
                // placed by hand covers anything this misses.
                if (hits == 0 || !r.TryGetComponent(out Collider _) || r.GetComponentInParent<Breakable>() != null) continue;
                var size = r.bounds.size;
                if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) >= MaxPropSize) continue;
                r.gameObject.AddComponent<Breakable>().Configure(hits, null);
                added++;
            }
            return added;
        }
    }
}
