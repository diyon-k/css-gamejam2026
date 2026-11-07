// Story dressing for the House scene: framed family photos on the walls and notes lying around that
// hint at what happened here and how you got here, without spelling it out. Menu: BLINK > Place Story Props.
// Re-running replaces the "Story" group. Also gives the Player an Interactor so the props can be read.
// Spots are (x, z, floor) from the HouseBlockout map; raycasts find the real wall or surface, so the props
// follow the house if it's rebuilt. A spot that finds nothing is skipped with a warning.
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

    [MenuItem("BLINK/Place Story Props")]
    public static void Place() // also callable from HouseBlockout.Build
    {
        var old = GameObject.Find("Story");
        if (old != null) Undo.DestroyObjectImmediate(old);
        Physics.SyncTransforms();
        var root = new GameObject("Story").transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Place Story Props");

        const float Second = 6.4f, Ground = 0f, Basement = -3.2f;

        // ---- Framed photos: from (x, z) at eye height, hung on the first wall in that direction.
        Frame(root, "photo_wedding", "Wedding photo", 2.0f, 8.0f, Second, Vector3.left, // StartBedroom, west wall
            "On the back, in neat ink: 'A. & E., June 1934. Forever is a promise I intend to keep.'");
        Frame(root, "photo_children_decayed", "Photo of two children", 9.3f, 20f, Second, Vector3.forward, // Old Nursery
            "On the back, in a child's hand: 'Tom and me. Summer.'\nThe faces have run, as if the picture got wet. It isn't damp.");
        Frame(root, "photo_family_decayed", "Family portrait", 14.5f, 11.2f, Second, Vector3.forward, // Corridor_Second
            "The Hales, in the front hall. Someone has scratched out every face, slowly, many times over.", 1.6f);
        Frame(root, "photo_staff", "Staff photograph", 22f, 20f, Second, Vector3.forward, // ServantsDorm
            "'Household staff, 1945.'\nThe boy on the end in the leather apron has been circled in ink, pressed hard enough to tear the paper.");

        // ---- Notes: dropped onto whatever is at (x, z), floor or furniture top.
        Note(root, "Delivery docket", 0.3f, 1.88f, Second, 20f, // StartBedroom nightstand
            "RAVEN & SONS COURIERS\nConsignment 0447\n\nCollect: estate of S. Kerr, Lot 14\nDeliver to: Hale House, end of Blackwood Lane. No road past the gate.\nContents: 1 x pocket watch, brass, antique\n\nSignature on receipt: ______________\n\nDriver's note: customer wants it there before midnight. Paid triple. Didn't ask why.");
        Note(root, "Note in shaky handwriting", 6.0f, 6.0f, Second, -35f, // floor, just in front of the bed
            "I am sorry about your head.\nYou would not have stayed otherwise, and you have brought back what was taken from me.\n\nKeep your eyes open for as long as you can bear it. They are only ever still while someone is watching.\n\n— A.");
        Note(root, "Child's drawing", 12f, 18f, Second, 70f, // Old Nursery
            "Papa says if we are very very still the sickness cant find us. Tom says thats babyish.\nI am being the stillest. Mama is too.\n\n(Below it, in crayon: four stick people holding hands, and a tall man with a clock where his face should be.)");
        Note(root, "Torn diary page", 12f, 11.0f, Second, 10f, // Corridor_Second
            "...nine days now without sleep. The clocks in this house have begun to disagree with one another. This morning the hall clock struck thirteen, and he only smiled and said, 'Not yet, my love. Not yet.'\n\nI am frightened of whatever he is building in the tower.\n— E.");
        Note(root, "Kitchen list", 4f, 16f, Ground, -50f, // Kitchen
            "Flour. Lard. Tea. Candles.\n\nThe bread has not gone stale since March. The milk won't turn. The kettle has been on the boil all morning and will not whistle.\nMr Hale says not to worry.\nI have stopped asking anyone the date.\n— Mrs Pell");
        Note(root, "Workshop scrap", 20f, 17f, Basement, 5f, // Workshop
            "The heart is gone. S. took it, the boy I trusted with my tools.\nWithout it the hands will not turn, forward or back. Everything stays as it is.\nWe will all stay as we are.\n\nSomeone will bring it home. Someone always brings things home.");
        Note(root, "Newspaper clipping", 38.5f, 4f, Basement, 30f, // GeneratorRoom, by the exit gate
            "AUCTION NOTICE\nThe effects of the late Mr Samuel Kerr (98), once apprenticed to a clockmaker.\n\nLot 14: brass pocket watch, maker's mark 'A. Hale'. The family say it has never once stopped ticking, though it holds no spring.\n\nSold to an anonymous buyer.");

        // The props need an Interactor on the player to be readable.
        var player = Object.FindAnyObjectByType<PlayerController>();
        if (player != null && player.GetComponent<Interactor>() == null) Undo.AddComponent<Interactor>(player.gameObject);
        else if (player == null) Debug.LogWarning("[StoryProps] No Player in the scene; add an Interactor to it to read the props.");

        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Debug.Log("[StoryProps] Placed photos and notes under 'Story'.");
    }

    const int NotPlayer = ~(1 << 9); // the Player capsule's layer

    static void Frame(Transform root, string photo, string title, float x, float z, float floor, Vector3 dir, string caption, float size = 1f)
    {
        // Slide along the wall (0, +0.5, -0.5, +1 ... 3 m) past windows and doorways until a ray hits wall.
        var side = Vector3.Cross(dir, Vector3.up);
        RaycastHit hit = default;
        bool found = false;
        for (int i = 0; i <= 12 && !found; i++)
        {
            float off = (i + 1) / 2 * 0.5f * (i % 2 == 0 ? -1f : 1f);
            found = Physics.Raycast(new Vector3(x, floor + 1.6f, z) + side * off, dir, out hit, 6f, NotPlayer, QueryTriggerInteraction.Ignore)
                    && Vector3.Dot(hit.normal, dir) < -0.9f; // a wall facing us, not furniture side-on
        }
        if (!found)
        {
            Debug.LogWarning($"[StoryProps] No wall for {photo} from ({x}, {z}) towards {dir}; skipped.");
            return;
        }
        Vector3 wallPoint = hit.point, facing = hit.normal;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Photos + photo + ".png");
        float w = 0.5f * size, h = tex != null ? w * tex.height / tex.width : w * 0.75f;

        var go = new GameObject("Frame_" + photo);
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(wallPoint + facing * 0.02f, Quaternion.LookRotation(facing));

        var border = GameObject.CreatePrimitive(PrimitiveType.Cube);
        border.name = "Border";
        border.transform.SetParent(go.transform, false);
        border.transform.localScale = new Vector3(w + 0.08f, h + 0.08f, 0.04f);
        border.GetComponent<Renderer>().sharedMaterial = Mat("FrameWood", new Color(0.18f, 0.11f, 0.06f), null);

        var pic = GameObject.CreatePrimitive(PrimitiveType.Quad);
        pic.name = "Photo";
        Object.DestroyImmediate(pic.GetComponent<Collider>()); // the border's collider is the target
        pic.transform.SetParent(go.transform, false);
        pic.transform.localPosition = new Vector3(0f, 0f, 0.021f);
        pic.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // quads face -Z
        pic.transform.localScale = new Vector3(w, h, 1f);
        pic.GetComponent<Renderer>().sharedMaterial = Mat(photo, Color.white, tex);

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
