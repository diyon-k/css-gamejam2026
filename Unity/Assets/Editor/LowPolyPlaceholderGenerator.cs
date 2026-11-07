using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blink.EditorTools
{
    /// <summary>
    /// Builds low-poly placeholder art: a rigged humanoid zombie and a pistol.
    /// The zombie uses a Unity Humanoid skeleton in T-pose, so any humanoid animation
    /// (e.g. Mixamo clips imported as Humanoid) retargets onto it.
    /// Re-run from the menu any time; it overwrites the generated assets.
    /// </summary>
    public static class LowPolyPlaceholderGenerator
    {
        const string RootDir = "Assets/Art/Placeholders";
        const string MeshDir = RootDir + "/Meshes";
        const string MatDir = RootDir + "/Materials";
        const string PrefabDir = RootDir + "/Prefabs";
        const string PreviewScenePath = "Assets/Scenes/PlaceholderPreview.unity";

        // Material slots shared by both characters.
        const int Skin = 0, Top = 1, Bottom = 2, Shoes = 3, Eyes = 4;

        [MenuItem("BLINK/Generate Low-Poly Placeholders")]
        public static void GenerateAll()
        {
            EnsureFolder(MeshDir);
            EnsureFolder(MatDir);
            EnsureFolder(PrefabDir);

            var zombie = BuildHumanoid("Zombie", new HumanoidStyle
            {
                Skin = new Color(0.45f, 0.55f, 0.38f),
                Top = new Color(0.35f, 0.30f, 0.25f),
                Bottom = new Color(0.20f, 0.22f, 0.28f),
                Shoes = new Color(0.12f, 0.10f, 0.08f),
                Eyes = new Color(0.9f, 0.04f, 0.02f),
                GlowingEyes = true,
                TornClothes = true,
                LimbScale = 0.85f,
                HeadScale = 1.08f,
                AddCollider = true,
            });

            var pistol = BuildPistol();

            BuildPreviewScene(zombie, pistol);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BLINK] Low-poly placeholders generated in {RootDir}; preview scene at {PreviewScenePath}");
        }

        // ---------------------------------------------------------------- characters

        class HumanoidStyle
        {
            public Color Skin, Top, Bottom, Shoes, Eyes;
            public bool GlowingEyes, TornClothes, AddCollider;
            public float LimbScale = 1f, HeadScale = 1f;
        }

        struct BoneDef
        {
            public string Name, Parent;
            public Vector3 Position; // world position in T-pose
            public BoneDef(string name, string parent, Vector3 position) { Name = name; Parent = parent; Position = position; }
        }

        // Names match Unity's HumanTrait bone names, so the avatar maps them one to one.
        static List<BoneDef> SkeletonDefs()
        {
            var defs = new List<BoneDef>
            {
                new("Hips", null, new Vector3(0f, 1.00f, 0f)),
                new("Spine", "Hips", new Vector3(0f, 1.12f, 0f)),
                new("Chest", "Spine", new Vector3(0f, 1.32f, 0f)),
                new("Neck", "Chest", new Vector3(0f, 1.52f, 0f)),
                new("Head", "Neck", new Vector3(0f, 1.62f, 0f)),
            };
            foreach (var (side, s) in new[] { ("Left", -1f), ("Right", 1f) })
            {
                defs.Add(new(side + "UpperArm", "Chest", new Vector3(s * 0.22f, 1.45f, 0f)));
                defs.Add(new(side + "LowerArm", side + "UpperArm", new Vector3(s * 0.50f, 1.45f, 0f)));
                defs.Add(new(side + "Hand", side + "LowerArm", new Vector3(s * 0.76f, 1.45f, 0f)));
                defs.Add(new(side + "UpperLeg", "Hips", new Vector3(s * 0.10f, 0.95f, 0f)));
                defs.Add(new(side + "LowerLeg", side + "UpperLeg", new Vector3(s * 0.10f, 0.52f, 0f)));
                defs.Add(new(side + "Foot", side + "LowerLeg", new Vector3(s * 0.10f, 0.10f, 0f)));
            }
            return defs;
        }

        static GameObject BuildHumanoid(string name, HumanoidStyle style)
        {
            var root = new GameObject(name);
            var bones = new Dictionary<string, Transform>();
            var boneList = new List<Transform>();
            foreach (var def in SkeletonDefs())
            {
                var t = new GameObject(def.Name).transform;
                t.SetParent(def.Parent == null ? root.transform : bones[def.Parent], false);
                t.position = def.Position;
                bones[def.Name] = t;
                boneList.Add(t);
            }
            int B(string bone) => boneList.IndexOf(bones[bone]);

            float l = style.LimbScale, h = style.HeadScale;
            var mb = new MeshBuilder(5);
            mb.Box(new Vector3(0f, 0.98f, 0f), new Vector3(0.34f, 0.18f, 0.20f), Bottom, B("Hips"));
            mb.Box(new Vector3(0f, 1.18f, 0f), new Vector3(0.32f, 0.20f, 0.19f), style.TornClothes ? Skin : Top, B("Spine"));
            mb.Box(new Vector3(0f, 1.40f, 0f), new Vector3(0.38f, 0.24f, 0.22f), Top, B("Chest"));
            mb.Box(new Vector3(0f, 1.56f, 0f), new Vector3(0.10f, 0.08f, 0.10f), Skin, B("Neck"));

            float headY = 1.60f + 0.13f * h, headZ = 0.01f;
            mb.Box(new Vector3(0f, headY, headZ), new Vector3(0.24f, 0.26f, 0.25f) * h, Skin, B("Head"));
            float eyeZ = headZ + 0.125f * h + 0.004f;
            mb.Box(new Vector3(-0.055f * h, headY + 0.02f * h, eyeZ), new Vector3(0.045f, 0.025f, 0.012f) * h, Eyes, B("Head"));
            mb.Box(new Vector3(0.055f * h, headY + 0.02f * h, eyeZ), new Vector3(0.045f, 0.025f, 0.012f) * h, Eyes, B("Head"));

            foreach (var (side, s) in new[] { ("Left", -1f), ("Right", 1f) })
            {
                bool tornSleeve = style.TornClothes && s < 0f;
                bool tornTrouser = style.TornClothes && s > 0f;
                mb.Box(new Vector3(s * 0.36f, 1.45f, 0f), new Vector3(0.28f, 0.10f * l, 0.10f * l), Top, B(side + "UpperArm"));
                mb.Box(new Vector3(s * 0.63f, 1.45f, 0f), new Vector3(0.26f, 0.085f * l, 0.085f * l), tornSleeve ? Skin : Top, B(side + "LowerArm"));
                mb.Box(new Vector3(s * 0.82f, 1.45f, 0f), new Vector3(0.12f, 0.05f, 0.09f), Skin, B(side + "Hand"));
                mb.Box(new Vector3(s * 0.10f, 0.74f, 0f), new Vector3(0.14f, 0.42f, 0.15f), Bottom, B(side + "UpperLeg"));
                mb.Box(new Vector3(s * 0.10f, 0.31f, 0f), new Vector3(0.12f, 0.42f, 0.12f), tornTrouser ? Skin : Bottom, B(side + "LowerLeg"));
                mb.Box(new Vector3(s * 0.10f, 0.05f, 0.05f), new Vector3(0.12f, 0.10f, 0.26f), Shoes, B(side + "Foot"));
            }

            var body = new GameObject(name + "Body");
            body.transform.SetParent(root.transform, false);

            var mesh = mb.ToMesh(name + "Mesh", skinned: true);
            var bindPoses = new Matrix4x4[boneList.Count];
            for (int i = 0; i < boneList.Count; i++)
                bindPoses[i] = boneList[i].worldToLocalMatrix * body.transform.localToWorldMatrix;
            mesh.bindposes = bindPoses;
            mesh = SaveAsset(mesh, $"{MeshDir}/{name}Mesh.asset");

            var smr = body.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = boneList.ToArray();
            smr.rootBone = bones["Hips"];
            smr.sharedMaterials = new[]
            {
                CreateMaterial($"{name}_Skin", style.Skin),
                CreateMaterial($"{name}_Top", style.Top),
                CreateMaterial($"{name}_Bottom", style.Bottom),
                CreateMaterial($"{name}_Shoes", style.Shoes),
                CreateMaterial($"{name}_Eyes", style.Eyes, smoothness: 0.6f, emission: style.GlowingEyes ? style.Eyes * 2.5f : null),
            };

            var avatar = SaveAsset(BuildHumanAvatar(root), $"{MeshDir}/{name}Avatar.asset");
            var animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;

            if (style.AddCollider)
            {
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 0.9f, 0f);
                capsule.height = 1.8f;
                capsule.radius = 0.3f;
            }

            return SavePrefab(root, $"{PrefabDir}/{name}.prefab");
        }

        static Avatar BuildHumanAvatar(GameObject root)
        {
            var humanNames = new HashSet<string>(HumanTrait.BoneName);
            var human = new List<HumanBone>();
            var skeleton = new List<SkeletonBone>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                skeleton.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
                if (humanNames.Contains(t.name))
                    human.Add(new HumanBone { humanName = t.name, boneName = t.name, limit = new HumanLimit { useDefaultValues = true } });
            }

            var description = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };

            var avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            avatar.name = root.name + "Avatar";
            if (!avatar.isValid || !avatar.isHuman)
                Debug.LogError($"[BLINK] Humanoid avatar for {root.name} is invalid.");
            return avatar;
        }

        // ---------------------------------------------------------------- pistol

        const int Slide = 0, Frame = 1, Sight = 2;

        /// <summary>A ~19 cm pistol pointing along +Z, with a Muzzle child at the barrel tip.</summary>
        static GameObject BuildPistol()
        {
            var mb = new MeshBuilder(3);
            var gripTilt = Quaternion.Euler(15f, 0f, 0f);

            mb.Box(new Vector3(0f, 0.035f, 0.020f), new Vector3(0.030f, 0.030f, 0.190f), Slide);          // slide
            mb.Box(new Vector3(0f, 0.035f, 0.117f), new Vector3(0.014f, 0.014f, 0.006f), Frame);          // barrel bore
            mb.Box(new Vector3(0f, 0.008f, 0.015f), new Vector3(0.028f, 0.025f, 0.170f), Frame);          // frame
            mb.Box(new Vector3(0f, -0.045f, -0.045f), new Vector3(0.028f, 0.100f, 0.045f), gripTilt, Frame); // grip
            mb.Box(new Vector3(0f, -0.096f, -0.058f), new Vector3(0.030f, 0.008f, 0.050f), gripTilt, Slide); // magazine base
            mb.Box(new Vector3(0f, -0.020f, 0.030f), new Vector3(0.008f, 0.006f, 0.050f), Frame);         // trigger guard, bottom
            mb.Box(new Vector3(0f, -0.008f, 0.055f), new Vector3(0.008f, 0.026f, 0.006f), Frame);         // trigger guard, front
            mb.Box(new Vector3(0f, -0.008f, 0.028f), new Vector3(0.005f, 0.018f, 0.005f), Slide);         // trigger
            mb.Box(new Vector3(0f, 0.054f, 0.105f), new Vector3(0.004f, 0.008f, 0.006f), Sight);          // front sight
            mb.Box(new Vector3(0f, 0.054f, -0.065f), new Vector3(0.022f, 0.008f, 0.006f), Slide);         // rear sight

            var mesh = SaveAsset(mb.ToMesh("PistolMesh", skinned: false), $"{MeshDir}/PistolMesh.asset");

            var root = new GameObject("Pistol");
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterials = new[]
            {
                CreateMaterial("Pistol_Slide", new Color(0.22f, 0.23f, 0.25f), smoothness: 0.55f, metallic: 0.8f),
                CreateMaterial("Pistol_Frame", new Color(0.07f, 0.07f, 0.08f), smoothness: 0.3f),
                CreateMaterial("Pistol_Sight", new Color(1f, 0.45f, 0.1f), smoothness: 0.4f, emission: new Color(1f, 0.45f, 0.1f) * 0.6f),
            };

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0f, 0.035f, 0.122f);

            return SavePrefab(root, $"{PrefabDir}/Pistol.prefab");
        }

        // ---------------------------------------------------------------- preview scene

        static void BuildPreviewScene(GameObject zombie, GameObject pistol)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.GetComponent<MeshRenderer>().sharedMaterial = CreateMaterial("Preview_Ground", new Color(0.32f, 0.32f, 0.30f));

            Place(zombie, scene, new Vector3(0.8f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f));
            var gun = Place(pistol, scene, new Vector3(0f, 1.2f, -1.6f), Quaternion.Euler(0f, 90f, 0f));
            gun.transform.localScale = Vector3.one * 3f; // enlarged so it reads in the preview shot

            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.TryGetComponent<Camera>(out var cam))
                {
                    go.transform.position = new Vector3(0f, 1.3f, -4.2f);
                    go.transform.LookAt(new Vector3(0f, 1.0f, 0f));
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
                }
            }

            EditorSceneManager.SaveScene(scene, PreviewScenePath);
            EditorSceneManager.CloseScene(scene, true);
        }

        static GameObject Place(GameObject prefab, Scene scene, Vector3 position, Quaternion rotation)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.SetPositionAndRotation(position, rotation);
            return go;
        }

        // ---------------------------------------------------------------- asset helpers

        /// <summary>Creates the asset, or overwrites an existing one in place so prefab references stay valid.</summary>
        static T SaveAsset<T>(T asset, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return asset;
            }
            EditorUtility.CopySerialized(asset, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(asset);
            return existing;
        }

        static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Material CreateMaterial(string name, Color color, float smoothness = 0.2f, float metallic = 0f, Color? emission = null)
        {
            string path = $"{MatDir}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        // ---------------------------------------------------------------- mesh building

        /// <summary>Builds flat-shaded box meshes; each box can be rigidly skinned to one bone.</summary>
        class MeshBuilder
        {
            static readonly Vector3[] FaceNormals =
                { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

            readonly List<Vector3> _vertices = new();
            readonly List<Vector3> _normals = new();
            readonly List<BoneWeight> _weights = new();
            readonly List<List<int>> _submeshes = new();

            public MeshBuilder(int submeshCount)
            {
                for (int i = 0; i < submeshCount; i++) _submeshes.Add(new List<int>());
            }

            public void Box(Vector3 center, Vector3 size, int submesh, int bone = 0) =>
                Box(center, size, Quaternion.identity, submesh, bone);

            public void Box(Vector3 center, Vector3 size, Quaternion rotation, int submesh, int bone = 0)
            {
                Vector3 half = size * 0.5f;
                foreach (var n in FaceNormals)
                {
                    // u x v = -n gives clockwise (front-facing) winding in Unity.
                    Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up;
                    Vector3 v = Vector3.Cross(u, n);
                    Vector3 hn = Vector3.Scale(n, half), hu = Vector3.Scale(u, half), hv = Vector3.Scale(v, half);
                    Vector3 normal = rotation * n;

                    int start = _vertices.Count;
                    Add(center + rotation * (hn - hu - hv), normal, bone);
                    Add(center + rotation * (hn - hu + hv), normal, bone);
                    Add(center + rotation * (hn + hu + hv), normal, bone);
                    Add(center + rotation * (hn + hu - hv), normal, bone);
                    _submeshes[submesh].AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                }
            }

            void Add(Vector3 position, Vector3 normal, int bone)
            {
                _vertices.Add(position);
                _normals.Add(normal);
                _weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
            }

            public Mesh ToMesh(string name, bool skinned)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.subMeshCount = _submeshes.Count;
                for (int i = 0; i < _submeshes.Count; i++) mesh.SetTriangles(_submeshes[i], i);
                if (skinned) mesh.boneWeights = _weights.ToArray();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
