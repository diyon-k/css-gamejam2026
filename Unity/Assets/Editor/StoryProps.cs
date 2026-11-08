// Story dressing for the bungalow (House_Bungalow): framed family photos on the walls and notes lying around that
// hint at what happened here and how you got here, without spelling it out. Menu: BLINK > Bungalow > Place Story Props.
// Re-running replaces the "Story" group. Also gives the Player an Interactor so the props can be read.
// Spots are (x, z, floor) in bungalow coordinates (bungalow_layout.json / bungalow_props.json); raycasts find the real
// wall or surface. A spot that finds nothing is skipped with a warning.
// The photos hang in the bungalow's own Picture_Landscape frame, with the photo laid over the frame's canvas.
// The opening "Read this first" note is left out here: the start bedroom's Folded note (BungalowStartBedroom) does its job.
// Story bible: docs/STORY_PLAN.md.
using Blink.Interaction;
using Blink.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StoryProps
{
    const string Photos = "Assets/Art/Story/Photos/";
    const string Mats = "Assets/Art/Story/Materials/";
    const string FramePrefab = "Assets/Art/Environment/Bungalow/Props/Prefabs/Picture_Landscape.prefab";
    // M_Atlas_Small cell "canvas_landscape" (bungalow_prop_atlas.json): the frame's canvas has its UVs in here.
    // Widened by 0.003 so vertices exactly on the cell edge count (the neighbouring cells are 0.0078 away).
    static readonly Rect CanvasUV = Rect.MinMaxRect(0.19140625f - 0.003f, 0.56640625f - 0.003f, 0.43359375f + 0.003f, 0.74609375f + 0.003f);

    [MenuItem("BLINK/Bungalow/Place Story Props")]
    public static void Place()
    {
        var old = GameObject.Find("Story");
        if (old != null) Undo.DestroyObjectImmediate(old);
        Physics.SyncTransforms();
        var root = new GameObject("Story").transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Place Story Props");

        const float Ground = 0f, Basement = -2.85f;

        // ---- Framed photos: from (x, z) at height h, hung on the first wall within 1.5 m in that direction.
        Frame(root, "photo_gamejam", "Wedding photo", 7.3f, 18.75f, 1.75f, Vector3.right, // MasterBedroom, E wall over the dresser
            "On the back, in neat ink: 'A. & E., June 1934. Forever is a promise I intend to keep.'");
        Frame(root, "photo_children_decayed", "Photo of two children", 2.0f, 16.2f, 1.6f, Vector3.forward, // Corridor, N wall by D8
            "On the back, in a child's hand: 'Tom and me. Summer.'\nThe faces have run, as if the picture got wet. It isn't damp.");
        Frame(root, "photo_family_decayed", "Family portrait", 10.8f, 11.4f, 1.7f, Vector3.right, // CentralHall walkway E wall, facing the clock
            "The Hales, in the front hall. Someone has scratched out every face, slowly, many times over.", 1.6f);
        Frame(root, "photo_gamejam_2", "Staff photograph", 17.8f, 7.8f, 1.6f, Vector3.back, // Kitchen, S wall by the stove
            "'Household staff, 1945.'\nThe boy on the end in the leather apron has been circled in ink, pressed hard enough to tear the paper.");

        // ---- Notes: dropped onto whatever is at (x, z), floor or furniture top.
        Note(root, "Delivery docket", 4.45f, 6.7f, Ground, 20f, // Bedroom floor, in view as you wake (P0 at 4.8, 7.6)
            "RAVEN & SONS COURIERS\nConsignment 0447\n\nCollect: estate of S. Kerr, Lot 14\nDeliver to: Hale House, end of Blackwood Lane. No road past the gate.\nContents: 1 x pocket watch, brass. Still ticking.\n\nSignature on receipt: ______________\n\nDriver's note: before midnight. The van clock stopped at the gate. Didn't ask why.");
        Note(root, "Child's drawing", 2.0f, 11.4f, Ground, 70f, // Bathroom floor, by Lily's doll, off the D3 -> chair line
            "Dolly only plays when nobody is looking. So I look at her ALL the time and she has to sit.\nTom blinked.\n\n(Below it, in crayon: a girl with enormous round eyes, a doll in a chair, and a tall man with a clock where his face should be.)");
        Note(root, "Torn diary page", 7.5f, 19.05f, Ground, 10f, // MasterBedroom dresser top
            "...nine days without sleep. The clocks quarrel. The hall clock struck thirteen.\n\nHe has hung us all in a row and turned our faces to him, so we will always know where he is.\nHe keeps his cabinet for the moon now. The moon is slow, he says, but never while you're watching it.\n— E.");
        Note(root, "Kitchen list", 15.15f, 10.6f, Ground, -50f, // Kitchen table
            "Flour. Lard. Tea. Candles.\n\nThe kettle's been on all morning and won't boil. Mr Hale says a watched pot never does.\nThe mains are seized again. Left loosens, right binds, and rust gives nothing to a hurry.\n\nI've stopped asking the date.\n— Mrs Pell");
        Note(root, "Workshop scrap", 19.3f, 6.9f, Basement, 5f, // B2 Workshop workbench
            "The heart is gone. S. took it.\nWithout it the hands turn neither way.\n\nDown here the water only rises while no one minds it. Mind it.\nThe gate remembers one moment: ours. Two short of midnight.\n\nSomeone always brings things home.");
        Note(root, "Newspaper clipping", 10.5f, 1.6f, Ground, 30f, // EntranceHall floor by the front door, clear of D1's swing
            "AUCTION NOTICE\nThe effects of the late Mr S. Kerr (98), once apprenticed to a clockmaker.\n\nLot 14: brass pocket watch, maker's mark 'A. H.'. Ticks, though it holds no spring.\n\nSold to an anonymous buyer. Delivery arranged.");

        // The props need an Interactor on the player to be readable.
        var player = Object.FindAnyObjectByType<PlayerController>();
        if (player != null && player.GetComponent<Interactor>() == null) Undo.AddComponent<Interactor>(player.gameObject);
        else if (player == null) Debug.LogWarning("[StoryProps] No Player in the scene; add an Interactor to it to read the props.");

        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Debug.Log("[StoryProps] Placed photos and notes under 'Story'.");
    }

    /// <summary>Batch mode: Unity -batchmode -quit -projectPath Unity -executeMethod StoryProps.Batch</summary>
    public static void Batch()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/House_Bungalow.unity");
        Place();
        var story = GameObject.Find("Story");
        if (story != null)
            foreach (Transform t in story.transform)
                Debug.Log($"[StoryProps] placed {t.name} at {t.position.x:F2}, {t.position.y:F2}, {t.position.z:F2}");
        EditorSceneManager.SaveScene(scene);
    }

    const int NotPlayer = ~(1 << 9); // the Player capsule's layer

    static void Frame(Transform root, string photo, string title, float x, float z, float h, Vector3 dir, string caption, float size = 1f)
    {
        // Slide along the wall (0, +0.5, -0.5, +1 ... 3 m) past windows and doorways until a short ray hits wall.
        var side = Vector3.Cross(dir, Vector3.up);
        RaycastHit hit = default;
        bool found = false;
        for (int i = 0; i <= 12 && !found; i++)
        {
            float off = (i + 1) / 2 * 0.5f * (i % 2 == 0 ? -1f : 1f);
            found = Physics.Raycast(new Vector3(x, h, z) + side * off, dir, out hit, 1.5f, NotPlayer, QueryTriggerInteraction.Ignore)
                    && Vector3.Dot(hit.normal, dir) < -0.9f; // a wall facing us, not furniture side-on
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FramePrefab);
        if (!found || prefab == null)
        {
            Debug.LogWarning($"[StoryProps] No wall (or no {FramePrefab}) for {photo} from ({x}, {z}) towards {dir}; skipped.");
            return;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.gameObject.scene);
        go.name = "Frame_" + photo;
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(hit.point + hit.normal * 0.003f, Quaternion.LookRotation(hit.normal)); // pivot = wall, +z = out
        go.transform.localScale = Vector3.one * size;
        // Not in the lightmap bake: light it from the probes instead of a missing lightmap.
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

        // The canvas: the frame's vertices whose UVs fall in the canvas atlas cell, in the frame's own space.
        var canvas = new Bounds();
        bool any = false;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            var m = mf.sharedMesh;
            if (m == null) continue;
            var v = m.vertices;
            var uv = m.uv;
            for (int k = 0; k < uv.Length; k++)
            {
                if (!CanvasUV.Contains(uv[k])) continue;
                var p = go.transform.InverseTransformPoint(mf.transform.TransformPoint(v[k]));
                if (any) canvas.Encapsulate(p); else { canvas = new Bounds(p, Vector3.zero); any = true; }
            }
        }
        if (!any)
        {
            canvas = new Bounds(new Vector3(0f, 0f, 0.01f), new Vector3(0.45f, 0.33f, 0f)); // ponytail: guessed canvas if the mesh can't be read
            Debug.LogWarning($"[StoryProps] Couldn't find the canvas in {FramePrefab}; {photo} uses a guessed size.");
        }

        // The game jam team photos are 1024px JPGs, mounted on a card to fit the square canvas.
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Photos + photo + ".png") ?? AssetDatabase.LoadAssetAtPath<Texture2D>(Photos + photo + ".jpg");
        var pic = GameObject.CreatePrimitive(PrimitiveType.Quad);
        pic.name = "Photo";
        Object.DestroyImmediate(pic.GetComponent<Collider>()); // the frame's box is the target
        pic.transform.SetParent(go.transform, false);
        pic.transform.localPosition = new Vector3(canvas.center.x, canvas.center.y, canvas.max.z + 0.002f);
        pic.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // quads face -Z
        pic.transform.localScale = new Vector3(canvas.size.x, canvas.size.y, 1f);
        pic.GetComponent<Renderer>().sharedMaterial = PhotoMat(photo, tex, canvas.size.x / canvas.size.y);

        var box = go.AddComponent<BoxCollider>(); // Picture props have no collider; the Interactor's ray needs one
        box.center = new Vector3(canvas.center.x, canvas.center.y, 0.02f);
        box.size = new Vector3(canvas.size.x + 0.12f, canvas.size.y + 0.12f, 0.04f);
        go.AddComponent<Readable>().Setup("Look at", title, caption, tex);
    }

    static void Note(Transform root, string title, float x, float z, float floor, float yaw, string text)
    {
        // Start 1.3 m up: above tables and nightstands, below bed canopies.
        if (!Physics.Raycast(new Vector3(x, floor + 1.3f, z), Vector3.down, out var hit, 2f, NotPlayer, QueryTriggerInteraction.Ignore))
        {
            Debug.LogWarning($"[StoryProps] Nothing to put '{title}' on at ({x}, {z}); skipped.");
            return;
        }
        var pos = hit.point;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Note_" + title.Replace(' ', '_');
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(pos + Vector3.up * 0.002f, Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = new Vector3(0.21f, 0.004f, 0.28f);
        go.GetComponent<Renderer>().sharedMaterial = Mat("Paper", new Color(0.82f, 0.78f, 0.66f), null);
        go.AddComponent<Readable>().Setup("Read", title, text);
    }

    /// <summary>The photo cropped to the canvas shape (centre crop), as its own material so House.unity's isn't changed.</summary>
    static Material PhotoMat(string photo, Texture2D tex, float canvasAspect)
    {
        var m = Mat("Framed_" + photo, Color.white, tex);
        if (tex == null) return m;
        float photoAspect = (float)tex.width / tex.height;
        var scale = photoAspect > canvasAspect ? new Vector2(canvasAspect / photoAspect, 1f) : new Vector2(1f, photoAspect / canvasAspect);
        m.SetTextureScale("_BaseMap", scale);
        m.SetTextureOffset("_BaseMap", (Vector2.one - scale) * 0.5f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Loads or creates a URP Lit material asset so the scene keeps its reference.</summary>
    static Material Mat(string name, Color color, Texture2D tex)
    {
        string path = Mats + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        System.IO.Directory.CreateDirectory(Mats);
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", color);
        if (tex != null) m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Smoothness", 0.15f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
