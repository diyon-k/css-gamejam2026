// Visuals: materials, lighting, furniture and props. Owned by the Visuals workstream.
// Runs after layout and interactables; skip anything that overlaps HouseBlockout.Reserved.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class HouseBlockout
{
    // Furnish every room in Rooms (non-interactive props only).
    static void DressRooms(Transform house)
    {
        BuildLighting(Group("Lighting", house));
    }

    // ---------------------------------------------------------------- Lighting + render settings
    static void BuildLighting(Transform p)
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.12f, 0.14f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.05f;
        RenderSettings.fogColor = new Color(0.04f, 0.045f, 0.05f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

        var moon = NewLight("Moonlight", p, LightType.Directional, new Color(0.55f, 0.62f, 0.8f), 0.3f, Vector3.zero);
        moon.transform.rotation = Quaternion.Euler(28, 70, 0); // slants in through the west windows
        RenderSettings.sun = moon;

        var bulb = NewLight("Kitchen_DyingBulb", p, LightType.Point, new Color(1f, 0.72f, 0.45f), 3f, new Vector3(3.5f, 2.5f, 6f));
        bulb.range = 8;
        var stair = NewLight("Stairwell_Faint", p, LightType.Point, new Color(0.6f, 0.65f, 0.8f), 1.5f, new Vector3(12f, 4.5f, 3.5f));
        stair.range = 7;
        var gen = NewLight("Basement_WorkLamp", p, LightType.Point, new Color(1f, 0.35f, 0.2f), 3f, new Vector3(4.5f, -1.0f, 2.0f));
        gen.range = 9;
    }

    static void MakeMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials", "House");
        wood = Mat("DarkWood", new Color(0.16f, 0.11f, 0.075f), 0.15f, 0f);
        plaster = Mat("GrimyPlaster", new Color(0.30f, 0.29f, 0.25f), 0.05f, 0f);
        concrete = Mat("Concrete", new Color(0.20f, 0.20f, 0.19f), 0.1f, 0f);
        rust = Mat("Rust", new Color(0.26f, 0.12f, 0.06f), 0.3f, 0.5f);
        water = Mat("FloodWater", new Color(0.015f, 0.02f, 0.02f), 0.95f, 0f);
        cloth = Mat("StainedCloth", new Color(0.36f, 0.33f, 0.27f), 0.0f, 0f);
    }

    // Reuses the existing asset if present so re-runs keep GUIDs stable.
    static Material Mat(string name, Color c, float smooth, float metal)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        EditorUtility.SetDirty(m);
        return m;
    }

}
