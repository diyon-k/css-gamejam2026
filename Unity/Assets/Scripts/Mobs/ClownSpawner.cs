using System.Collections.Generic;
using Blink.Combat;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.AI;

namespace Blink.Mobs
{
    /// <summary>
    /// Keeps up to <see cref="maxAlive"/> clowns in the house. The first spawns at once, then one more every
    /// <see cref="spawnDelay"/> world seconds (blinks skip it forward) while there's room; a death frees a slot.
    /// Spawn points the player can see or is close to are skipped. <see cref="Restart"/> (player death) removes them all.
    /// </summary>
    public sealed class ClownSpawner : MonoBehaviour, ISkippable
    {
        [SerializeField] GameObject clownPrefab;
        [Tooltip("Raise to 3 once the extension's spawn points are in.")]
        [SerializeField, Min(1)] int maxAlive = 2;
        [Tooltip("World seconds between spawns, and from a death to its replacement.")]
        [SerializeField, Min(0f)] float spawnDelay = 15f;
        [Tooltip("Never spawn closer to the player than this.")]
        [SerializeField, Min(0f)] float minPlayerDistance = 8f;
        [Tooltip("Empty = every Markers/Spawns/Spawn_Z* marker, with the Markers/Routes/Route_<id> in its name as patrol.")]
        [SerializeField] List<Transform> spawnPoints = new();
        [Tooltip("NavMesh agent type the scene is baked for (the prefab's own type is used if not found).")]
        [SerializeField] string agentType = "BungalowZombie";
        [Tooltip("bungalow_layout.json: clowns wander between its rooms. Empty = they walk their Route_ loops instead.")]
        [SerializeField] TextAsset layout;

        [System.Serializable] class Layout { public Room[] rooms; }
        [System.Serializable] class Room { public float[] min, max; }

        readonly List<Health> _alive = new();
        Bounds[] _rooms;
        Transform _holder;
        readonly Dictionary<Transform, Transform> _routes = new();
        float _timer;

        void Awake()
        {
            if (spawnPoints.Count == 0 && GameObject.Find("Markers/Spawns") is { } spawns)
                foreach (Transform m in spawns.transform)
                    if (m.name.StartsWith("Spawn_Z")) spawnPoints.Add(m);
            foreach (var p in spawnPoints)
            {
                int open = p.name.IndexOf('(');
                if (open < 0) continue;
                var id = p.name.Substring(open + 1).TrimEnd(')', ' ');
                if (GameObject.Find("Markers/Routes/Route_" + id) is { } route) _routes[p] = route.transform;
            }
            if (layout != null && JsonUtility.FromJson<Layout>(layout.text)?.rooms is { Length: > 0 } rooms)
            {
                _rooms = new Bounds[rooms.Length];
                for (int i = 0; i < rooms.Length; i++)
                {
                    _rooms[i] = new Bounds();
                    _rooms[i].SetMinMax(new Vector3(rooms[i].min[0], rooms[i].min[1], rooms[i].min[2]),
                                        new Vector3(rooms[i].max[0], rooms[i].max[1], rooms[i].max[2]));
                }
            }
        }

        void OnEnable() => TimeSkipController.Register(this);
        void OnDisable() => TimeSkipController.Unregister(this);

        void Update() => Tick(Time.deltaTime);
        public void Skip(float seconds) => Tick(seconds);

        void Tick(float seconds)
        {
            _alive.RemoveAll(h => h == null || h.IsDead);
            if (_alive.Count >= maxAlive) { _timer = spawnDelay; return; } // full: a death starts a fresh wait
            _timer -= seconds;
            if (_timer > 0f) return;
            _timer = spawnDelay;
            Spawn();
        }

        /// <summary>Player died: clear the house and start again from one clown.</summary>
        public void Restart()
        {
            foreach (var h in _alive) if (h != null) Destroy(h.gameObject);
            _alive.Clear();
            _timer = 0f;
        }

        void Spawn()
        {
            if (clownPrefab == null || spawnPoints.Count == 0) return;
            var point = PickPoint();
            // built under an inactive holder so the agent type and route are set before its Awake / OnEnable
            if (_holder == null) { _holder = new GameObject("ClownSpawnHolder").transform; _holder.gameObject.SetActive(false); }
            var clown = Instantiate(clownPrefab, point.position, point.rotation, _holder);
            if (clown.TryGetComponent<NavMeshAgent>(out var agent) && AgentTypeId(agentType) is int id) agent.agentTypeID = id;
            if (clown.TryGetComponent<MobController>(out var mob))
            {
                if (_rooms != null) mob.SetPatrolRooms(_rooms);
                else if (_routes.TryGetValue(point, out var route)) mob.SetPatrolRoute(route);
            }
            if (clown.TryGetComponent<Health>(out var health)) _alive.Add(health);
            clown.transform.SetParent(null, true);
        }

        static int? AgentTypeId(string name)
        {
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                int id = NavMesh.GetSettingsByIndex(i).agentTypeID;
                if (NavMesh.GetSettingsNameFromID(id) == name) return id;
            }
            return null;
        }

        /// <summary>Random point out of view and far enough away; failing that, the one farthest from the player.</summary>
        Transform PickPoint()
        {
            var cam = Camera.main;
            if (cam == null) return spawnPoints[Random.Range(0, spawnPoints.Count)];
            Vector3 eye = cam.transform.position;
            var ok = new List<Transform>();
            Transform farthest = spawnPoints[0];
            foreach (var p in spawnPoints)
            {
                float d = Vector3.Distance(eye, p.position);
                if (d > Vector3.Distance(eye, farthest.position)) farthest = p;
                // ponytail: frustum test only, no occlusion ray; treats a point behind a wall but on screen as seen
                var v = cam.WorldToViewportPoint(p.position + Vector3.up);
                bool onScreen = v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
                if (d >= minPlayerDistance && !onScreen) ok.Add(p);
            }
            return ok.Count > 0 ? ok[Random.Range(0, ok.Count)] : farthest;
        }
    }
}
