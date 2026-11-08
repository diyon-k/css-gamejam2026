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
    /// With a layout they spawn in a room (not the player's, not in sight, at least minPlayerDistance away); without one,
    /// at the Spawn_Z markers by the same rules. <see cref="Restart"/> (player death) removes them all.
    /// </summary>
    public sealed class ClownSpawner : MonoBehaviour, ISkippable
    {
        [SerializeField] GameObject clownPrefab;
        [Tooltip("Raise to 3 once the extension's spawn points are in.")]
        [SerializeField, Min(1)] int maxAlive = 2;
        [Tooltip("World seconds between spawns, and from a death to its replacement.")]
        [SerializeField, Min(0f)] float spawnDelay = 15f;
        [Tooltip("Never spawn closer to the player than this.")]
        [SerializeField, Min(0f)] float minPlayerDistance = 12f;
        [Tooltip("Empty = every Markers/Spawns/Spawn_Z* marker, with the Markers/Routes/Route_<id> in its name as patrol.")]
        [SerializeField] List<Transform> spawnPoints = new();
        [Tooltip("NavMesh agent type the scene is baked for (the prefab's own type is used if not found).")]
        [SerializeField] string agentType = "BungalowZombie";
        [Tooltip("bungalow_layout.json: clowns wander between its rooms. Empty = they walk their Route_ loops instead.")]
        [SerializeField] TextAsset layout;
        [Tooltip("Layout room ids that each get one clown scriptedDelay world seconds in. Replaces the random spawning; empty = random.")]
        [SerializeField] string[] scriptedRooms = { "Corridor", "Kitchen" };
        [SerializeField, Min(0f)] float scriptedDelay = 30f;

        [System.Serializable] class Layout { public Room[] rooms; }
        [System.Serializable] class Room { public string id; public float[] min, max; }

        readonly List<Health> _alive = new();
        Bounds[] _rooms;
        string[] _roomIds;
        bool _scriptedDone;
        bool Scripted => scriptedRooms.Length > 0 && _rooms != null;
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
                _roomIds = new string[rooms.Length];
                for (int i = 0; i < rooms.Length; i++)
                {
                    _rooms[i] = new Bounds();
                _roomIds[i] = rooms[i].id;
                    _rooms[i].SetMinMax(new Vector3(rooms[i].min[0], rooms[i].min[1], rooms[i].min[2]),
                                        new Vector3(rooms[i].max[0], rooms[i].max[1], rooms[i].max[2]));
                }
            }
            if (Scripted) _timer = scriptedDelay;
        }

        void OnEnable() => TimeSkipController.Register(this);
        void OnDisable() => TimeSkipController.Unregister(this);

        void Update() => Tick(Time.deltaTime);
        public void Skip(float seconds) => Tick(seconds);

        void Tick(float seconds)
        {
            if (Scripted)
            {
                if (_scriptedDone || (_timer -= seconds) > 0f) return;
                _scriptedDone = true;
                SpawnScripted();
                return;
            }
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
            _timer = Scripted ? scriptedDelay : 0f;
            _scriptedDone = false;
        }

        void SpawnScripted()
        {
            int? typeId = AgentTypeId(agentType);
            var filter = new NavMeshQueryFilter { agentTypeID = typeId ?? 0, areaMask = NavMesh.AllAreas };
            foreach (var id in scriptedRooms)
            {
                int i = System.Array.IndexOf(_roomIds, id);
                if (i < 0) { Debug.LogWarning($"ClownSpawner: no layout room '{id}'", this); continue; }
                var floor = new Vector3(_rooms[i].center.x, _rooms[i].min.y, _rooms[i].center.z);
                if (NavMesh.SamplePosition(floor, out var hit, 2.5f, filter))
                    SpawnAt(hit.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), null, typeId);
            }
        }

        void Spawn()
        {
            if (clownPrefab == null) return;
            int? typeId = AgentTypeId(agentType);
            var candidates = Candidates(typeId);
            if (candidates.Count == 0) return;
            var (position, rotation, marker) = PickPoint(candidates);
            SpawnAt(position, rotation, marker, typeId);
        }

        void SpawnAt(Vector3 position, Quaternion rotation, Transform marker, int? typeId)
        {
            if (clownPrefab == null) return;
            // built under an inactive holder so the agent type and route are set before its Awake / OnEnable
            if (_holder == null) { _holder = new GameObject("ClownSpawnHolder").transform; _holder.gameObject.SetActive(false); }
            var clown = Instantiate(clownPrefab, position, rotation, _holder);
            if (clown.TryGetComponent<NavMeshAgent>(out var agent) && typeId is int id) agent.agentTypeID = id;
            if (clown.TryGetComponent<MobController>(out var mob))
            {
                if (_rooms != null) mob.SetPatrolRooms(_rooms);
                else if (marker != null && _routes.TryGetValue(marker, out var route)) mob.SetPatrolRoute(route);
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

        /// <summary>With rooms: the middle of every room (on the navmesh), so there's plenty of choice. Without: the Spawn_Z markers.</summary>
        List<(Vector3 pos, Quaternion rot, Transform marker)> Candidates(int? typeId)
        {
            var list = new List<(Vector3, Quaternion, Transform)>();
            if (_rooms == null)
            {
                foreach (var p in spawnPoints) if (p != null) list.Add((p.position, p.rotation, p));
                return list;
            }
            var filter = new NavMeshQueryFilter { agentTypeID = typeId ?? 0, areaMask = NavMesh.AllAreas };
            foreach (var room in _rooms)
            {
                var floor = new Vector3(room.center.x, room.min.y, room.center.z);
                if (NavMesh.SamplePosition(floor, out var hit, 2.5f, filter))
                    list.Add((hit.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), null));
            }
            return list;
        }

        /// <summary>
        /// Random candidate the player can't see, at least minPlayerDistance away and not in the player's room;
        /// failing that, the one farthest from the player.
        /// </summary>
        (Vector3, Quaternion, Transform) PickPoint(List<(Vector3 pos, Quaternion rot, Transform marker)> candidates)
        {
            var cam = Camera.main;
            if (cam == null) return candidates[Random.Range(0, candidates.Count)];
            Vector3 eye = cam.transform.position;
            int playerRoom = RoomOf(eye);
            var ok = new List<(Vector3, Quaternion, Transform)>();
            var farthest = candidates[0];
            foreach (var c in candidates)
            {
                float d = Vector3.Distance(eye, c.pos);
                if (d > Vector3.Distance(eye, farthest.pos)) farthest = c;
                if (d < minPlayerDistance || (playerRoom >= 0 && RoomOf(c.pos + Vector3.up) == playerRoom)) continue;
                // seen = on screen with nothing solid in between
                Vector3 head = c.pos + Vector3.up * 1.5f;
                var v = cam.WorldToViewportPoint(head);
                bool onScreen = v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
                if (onScreen && !Physics.Linecast(eye, head, ~0, QueryTriggerInteraction.Ignore)) continue;
                ok.Add(c);
            }
            return ok.Count > 0 ? ok[Random.Range(0, ok.Count)] : farthest;
        }

        int RoomOf(Vector3 p)
        {
            if (_rooms == null) return -1;
            for (int i = 0; i < _rooms.Length; i++)
            {
                var r = _rooms[i];
                if (p.x >= r.min.x && p.x <= r.max.x && p.z >= r.min.z && p.z <= r.max.z && p.y >= r.min.y - 0.5f && p.y <= r.max.y + 0.5f) return i;
            }
            return -1;
        }
    }
}
