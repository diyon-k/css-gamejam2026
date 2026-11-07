// Story dressing for the House scene: framed family photos on the walls and notes lying around that
// hint at what happened here and how you got here, without spelling it out. Menu: BLINK > Place Story Props.
// Re-running replaces the "Story" group. Also gives the Player an Interactor so the props can be read.
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
    static void Place()
    {
        var old = GameObject.Find("Story");
        if (old != null) Undo.DestroyObjectImmediate(old);
        var root = new GameObject("Story").transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Place Story Props");

        // ---- Framed photos. Position is the wall surface point, facing = direction out of the wall.
        Frame(root, "photo_wedding", "Wedding photo", new Vector3(0.12f, 4.6f, 6.0f), Vector3.right,
            "On the back, in neat ink: 'A. & E., June 1934. Forever is a promise I intend to keep.'");
        Frame(root, "photo_children_decayed", "Photo of two children", new Vector3(8.0f, 4.5f, 9.88f), Vector3.back,
            "On the back, in a child's hand: 'Tom and me. Summer.'\nThe faces have run, as if the picture got wet. It isn't damp.");
        Frame(root, "photo_family_decayed", "Family portrait", new Vector3(11.0f, 4.3f, 4.88f), Vector3.back,
            "The Hales, in the front hall. Someone has scratched out every face, slowly, many times over.", 1.6f);
        Frame(root, "photo_staff", "Staff photograph", new Vector3(5.0f, 1.6f, 0.12f), Vector3.forward,
            "'Household staff, 1945.'\nThe boy on the end in the leather apron has been circled in ink, pressed hard enough to tear the paper.");

        // ---- Notes. Position is where the paper lies (on a floor or top surface).
        Note(root, "Delivery docket", new Vector3(0.45f, 3.605f, 6.2f), 20f,
            "RAVEN & SONS COURIERS\nConsignment 0447\n\nCollect: estate of S. Kerr, Lot 14\nDeliver to: Hale House, end of Blackwood Lane. No road past the gate.\nContents: 1 x pocket watch, brass, antique\n\nSignature on receipt: ______________\n\nDriver's note: customer wants it there before midnight. Paid triple. Didn't ask why.");
        Note(root, "Note in shaky handwriting", new Vector3(2.9f, 3.005f, 6.6f), -35f,
            "I am sorry about your head.\nYou would not have stayed otherwise, and you have brought back what was taken from me.\n\nKeep your eyes open for as long as you can bear it. They are only ever still while someone is watching.\n\n— A.");
        Note(root, "Child's drawing", new Vector3(9.2f, 3.005f, 8.0f), 70f,
            "Papa says if we are very very still the sickness cant find us. Tom says thats babyish.\nI am being the stillest. Mama is too.\n\n(Below it, in crayon: four stick people holding hands, and a tall man with a clock where his face should be.)");
        Note(root, "Torn diary page", new Vector3(3.2f, 0.005f, 1.5f), 10f,
            "...nine days now without sleep. The clocks in this house have begun to disagree with one another. This morning the hall clock struck thirteen, and he only smiled and said, 'Not yet, my love. Not yet.'\n\nI am frightened of whatever he is building in the tower.\n— E.");
        Note(root, "Kitchen list", new Vector3(2.6f, 0.005f, 4.2f), -50f,
            "Flour. Lard. Tea. Candles.\n\nThe bread has not gone stale since March. The milk won't turn. The kettle has been on the boil all morning and will not whistle.\nMr Hale says not to worry.\nI have stopped asking anyone the date.\n— Mrs Pell");
        Note(root, "Workshop scrap", new Vector3(3.2f, -1.795f, 0.8f), 5f,
            "The heart is gone. S. took it, the boy I trusted with my tools.\nWithout it the hands will not turn, forward or back. Everything stays as it is.\nWe will all stay as we are.\n\nSomeone will bring it home. Someone always brings things home.");
        Note(root, "Newspaper clipping", new Vector3(0.8f, -2.995f, 3.0f), 30f,
            "AUCTION NOTICE\nThe effects of the late Mr Samuel Kerr (98), once apprenticed to a clockmaker.\n\nLot 14: brass pocket watch, maker's mark 'A. Hale'. The family say it has never once stopped ticking, though it holds no spring.\n\nSold to an anonymous buyer.");

        // The props need an Interactor on the player to be readable.
        var player = Object.FindAnyObjectByType<PlayerController>();
        if (player != null && player.GetComponent<Interactor>() == null) Undo.AddComponent<Interactor>(player.gameObject);
        else if (player == null) Debug.LogWarning("[StoryProps] No Player in the scene; add an Interactor to it to read the props.");

        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Debug.Log("[StoryProps] Placed photos and notes under 'Story'.");
    }

    static void Frame(Transform root, string photo, string title, Vector3 wallPoint, Vector3 facing, string caption, float size = 1f)
    {
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

    static void Note(Transform root, string title, Vector3 pos, float yaw, string text)
    {
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
