// Hitbox audit for House_Bungalow. Menu: BLINK > Bungalow > Hitbox Audit (or batch: -executeMethod HitboxAudit.Batch).
// 1. Interactables a standing player can't target: from spots on the navmesh around each one, at standing and crouching
//    eye height, the look ray (Interactor.FindTarget, the real one) has to land on it. Flags parts buried inside their
//    carcass box (the kitchen cabinet's drawers were) and things walled in. Things lying in a shut container are
//    expected to fail until it opens; they're listed apart.
// 2. Navmesh leaks through walls: pairs of nearby navmesh points with solid level geometry between them, but a clear
//    straight navmesh line. A clown can walk (or be warped) through there.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Blink.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class HitboxAudit
{
    const float Reach = 2.2f;
    static readonly float[] EyeHeights = { 1.6f, 1.0f };

    [MenuItem("BLINK/Bungalow/Hitbox Audit")]
    public static void Run() => Debug.Log(Report());

    public static void Batch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/House_Bungalow.unity");
        var text = Report();
        System.IO.File.WriteAllText("Logs/HitboxAudit.txt", text);
        Debug.Log(text);
    }

    static string Report()
    {
        Physics.SyncTransforms();
        var sb = new StringBuilder("[HitboxAudit]\n");
        var probe = NavMesh.SamplePosition(new Vector3(9f, 0f, 8f), out _, 5f, NavMesh.AllAreas);
        sb.AppendLine($"navmesh loaded: {probe}");

        // ---- 1. interactables
        var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude).OfType<IInteractable>().Cast<MonoBehaviour>().ToList();
        int ok = 0;
        var blocked = new List<string>();
        var contained = new List<string>();
        var hits = new RaycastHit[16];
        foreach (var it in all)
        {
            var cols = it.GetComponentsInChildren<Collider>().Where(c => c.enabled && c.GetComponentInParent<IInteractable>() == (IInteractable)it).ToArray();
            if (cols.Length == 0) { blocked.Add($"{Path(it.transform)} ({it.GetType().Name}): NO ENABLED COLLIDER"); continue; }
            var b = cols[0].bounds;
            foreach (var c in cols.Skip(1)) b.Encapsulate(c.bounds);

            bool reached = false;
            string firstBlocker = null;
            for (int a = 0; a < 16 && !reached; a++)
            for (float r = 0.5f; r <= 1.7f && !reached; r += 0.4f)
            {
                var dir = Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward;
                var guess = new Vector3(b.center.x, b.min.y, b.center.z) + dir * (r + Mathf.Max(b.extents.x, b.extents.z));
                if (!Stand(guess, out var stand)) continue;
                foreach (var h in EyeHeights)
                {
                    var eye = stand + Vector3.up * h;
                    foreach (var aim in cols.SelectMany(c => new[] { c.bounds.center, c.ClosestPoint(eye) }))
                    {
                        if ((aim - eye).magnitude > Reach) continue;
                        var found = Interactor.FindTarget(eye, (aim - eye).normalized, Reach, null, hits, Interactor.AssistRadius);
                        if (ReferenceEquals(found, it)) { reached = true; break; }
                        if (firstBlocker == null && Physics.Raycast(eye, (aim - eye).normalized, out var hit, Reach))
                            firstBlocker = Path(hit.collider.transform);
                    }
                    if (reached) break;
                }
            }
            if (reached) { ok++; continue; }
            var holder = it.transform.parent != null ? it.transform.parent.GetComponentInParent<Openable>() : null;
            var line = $"{Path(it.transform)} ({it.GetType().Name}) at {b.center:F2}: blocked by {firstBlocker ?? "nothing (no free floor spot within reach)"}";
            (holder != null && !holder.IsOpen ? contained : blocked).Add(line);
        }
        sb.AppendLine($"\n== Interactables: {all.Count}, reachable {ok}, unreachable {blocked.Count}, in a shut container {contained.Count}");
        foreach (var l in blocked) sb.AppendLine("  UNREACHABLE " + l);
        foreach (var l in contained) sb.AppendLine("  (inside, ok if it opens) " + l);

        // ---- 2. navmesh leaks through walls
        var tri = NavMesh.CalculateTriangulation();
        var points = new List<Vector3>();
        var seen = new HashSet<Vector3Int>();
        foreach (var v in tri.vertices)
            if (seen.Add(Vector3Int.RoundToInt(v * 4f))) points.Add(v); // ~25 cm grid
        for (int i = 0; i < tri.indices.Length; i += 3)
        {
            var c = (tri.vertices[tri.indices[i]] + tri.vertices[tri.indices[i + 1]] + tri.vertices[tri.indices[i + 2]]) / 3f;
            if (seen.Add(Vector3Int.RoundToInt(c * 4f))) points.Add(c);
        }
        int solid = ~(LayerMask.GetMask("Ignore Raycast"));
        var leaks = new List<string>();
        var leakCells = new HashSet<Vector3Int>();
        points.Sort((p, q) => p.x.CompareTo(q.x));
        for (int i = 0; i < points.Count; i++)
        for (int j = i + 1; j < points.Count && points[j].x - points[i].x <= 1.2f; j++)
        {
            var a = points[i]; var bpt = points[j];
            if (Mathf.Abs(a.y - bpt.y) > 0.3f) continue;
            float d = Vector3.Distance(a, bpt);
            if (d > 1.2f || d < 0.15f) continue;
            var from = a + Vector3.up * 0.5f; var to = bpt + Vector3.up * 0.5f;
            if (!Physics.Linecast(from, to, out var wall, solid, QueryTriggerInteraction.Ignore)) continue;
            if (wall.collider.attachedRigidbody != null && !wall.collider.attachedRigidbody.isKinematic) continue; // loose props
            if (wall.collider.GetComponentInParent<Blink.World.Door>() != null) continue; // doors: they path through on purpose
            // something solid between, both directions (not just a thin trigger-ish sliver)
            if (!Physics.Linecast(to, from, solid, QueryTriggerInteraction.Ignore)) continue;
            if (NavMesh.Raycast(a, bpt, out _, NavMesh.AllAreas)) continue; // navmesh is cut there: fine
            var cell = Vector3Int.RoundToInt(wall.point);
            if (!leakCells.Add(cell)) continue;
            leaks.Add($"  LEAK {a:F2} -> {bpt:F2} through {Path(wall.collider.transform)} at {wall.point:F2}");
        }
        sb.AppendLine($"\n== Navmesh leaks through solid geometry: {leaks.Count} (points sampled: {points.Count})");
        foreach (var l in leaks.Take(80)) sb.AppendLine(l);

        // ---- 3. settings that matter for clipping
        sb.AppendLine("\n== Agents");
        foreach (var ag in Object.FindObjectsByType<NavMeshAgent>(FindObjectsInactive.Include))
            sb.AppendLine($"  {Path(ag.transform)} radius {ag.radius:F2} height {ag.height:F2}");
        var s = NavMesh.GetSettingsByIndex(0);
        sb.AppendLine($"  bake agent radius {s.agentRadius:F2}");
        return sb.ToString();
    }

    /// <summary>A spot the player's capsule fits, on a floor (named Floor*/Stair*) under <paramref name="guess"/>.</summary>
    static bool Stand(Vector3 guess, out Vector3 stand)
    {
        stand = default;
        // the highest floor (not a furniture top) under the guess, from above the thing down to 3 m below it
        var floors = Physics.RaycastAll(guess + Vector3.up * 2.5f, Vector3.down, 5.5f, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => h.normal.y > 0.8f && h.point.y <= guess.y + 0.3f && (h.collider.name.Contains("Floor") || h.collider.name.Contains("Stair")))
            .OrderByDescending(h => h.point.y).ToArray(); // the storey it's on, not the one above
        if (floors.Length == 0) return false;
        stand = floors[0].point;
        var player = Object.FindAnyObjectByType<Blink.Player.PlayerController>();
        int mask = 0; // what the player's capsule actually collides with (the flood water, for one, it wades through)
        for (int l = 0; l < 32; l++)
            if (player == null || !Physics.GetIgnoreLayerCollision(player.gameObject.layer, l)) mask |= 1 << l;
        return Physics.OverlapCapsule(stand + Vector3.up * 0.35f, stand + Vector3.up * 1.6f, 0.3f, mask, QueryTriggerInteraction.Ignore)
            .All(c => player != null && c.transform.IsChildOf(player.transform));
    }

    static string Path(Transform t)
    {
        var parts = new List<string>();
        for (int i = 0; t != null && i < 4; t = t.parent, i++) parts.Insert(0, t.name);
        return string.Join("/", parts);
    }
}
