using System.Linq;
using Blink.Combat;
using Blink.Player;
using Blink.World;
using UnityEngine;

namespace Blink.Mobs
{
    /// <summary>
    /// Holds a mob perfectly still (its <see cref="MobController"/> is switched off, so no moving, sensing or blink lurches)
    /// until the player sees it: one of <see cref="wakeDoors"/> is open (if any are set) and the mob is near the middle of the screen with
    /// nothing solid in between. Being touched or hurt also wakes it. Waking hands it back to its MobController and goes away.
    /// Added by <see cref="ClownSpawner"/> to the corridor lurker; the spawner disables the MobController before it wakes up.
    /// </summary>
    public sealed class DormantUntilSeen : MonoBehaviour
    {
        [Tooltip("Door ids: it can only be noticed once one of these is open. Empty = any time.")]
        [SerializeField] string[] wakeDoors = { "D6" };
        [Tooltip("How far into the screen edges still counts as looking at it (0.2 = the middle 60% across).")]
        [SerializeField, Range(0f, 0.45f)] float screenMargin = 0.2f;
        [SerializeField] float maxDistance = 25f;
        [Tooltip("Wakes if the player gets this close, looking or not.")]
        [SerializeField] float touchRange = 2f;

        static readonly float[] Heights = { 1.5f, 1.0f, 0.4f }; // head, chest, knees: any one in view counts
        readonly RaycastHit[] _hits = new RaycastHit[16];

        MobController _mob;
        Health _health;
        Door[] _doors;

        public void Init(string[] doors) => wakeDoors = doors;

        void Awake()
        {
            _mob = GetComponent<MobController>();
            _health = GetComponent<Health>();
            if (_mob != null) _mob.enabled = false;
        }

        void OnEnable() { if (_health != null) _health.Damaged += OnDamaged; }
        void OnDisable() { if (_health != null) _health.Damaged -= OnDamaged; }

        void Start() => _doors = FindObjectsByType<Door>().Where(d => wakeDoors.Contains(d.doorId)).ToArray();

        // Health raises Damaged before Died, so a killing shot still reaches an awake MobController's death handling.
        void OnDamaged(float amount, Vector3 point) => Wake();

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            float distance = Vector3.Distance(eye, transform.position + Vector3.up); // 3D: not through a floor
            if (distance <= touchRange) { Wake(); return; }
            if (distance > maxDistance) return;
            if (wakeDoors.Length > 0 && !_doors.Any(d => d != null && d.IsOpen)) return;
            foreach (float h in Heights)
                if (Seen(cam, eye, transform.position + Vector3.up * h)) { Wake(); return; }
        }

        bool Seen(Camera cam, Vector3 eye, Vector3 point)
        {
            var v = cam.WorldToViewportPoint(point);
            if (v.z <= 0f || v.x < screenMargin || v.x > 1f - screenMargin || v.y < 0.05f || v.y > 0.95f) return false;
            Vector3 dir = point - eye;
            int n = Physics.RaycastNonAlloc(eye, dir.normalized, _hits, dir.magnitude, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool selfFirst = true; // nothing in the way => seen
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c.GetComponentInParent<PlayerController>() != null) continue; // the player's own colliders / held props
                if (_hits[i].distance < nearest) { nearest = _hits[i].distance; selfFirst = c.transform.IsChildOf(transform); }
            }
            return selfFirst;
        }

        void Wake()
        {
            if (_mob != null) _mob.enabled = true;
            Destroy(this);
        }
    }
}
