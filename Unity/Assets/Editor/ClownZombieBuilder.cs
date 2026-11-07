using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Blink.EditorTools
{
    /// <summary>
    /// BLINK/Zombies/Build White Clown Zombie
    ///   1. Imports Art/Zombies/WhiteClown/WhiteClown.fbx (Mixamo) as a Humanoid with its own avatar, so the
    ///      shared humanoid clips in Art/Zombies/Animations retarget onto it.
    ///   2. Extracts its embedded textures and builds URP Lit materials (remapped onto the model).
    ///   3. Rewrites Prefabs/Zombie.prefab in place (GUID kept, so scenes keep their zombies): the old ZombieGirl
    ///      model is swapped for the clown, keeping AI, health, audio, hit reactions and Zombie.controller. The tuned
    ///      ragdoll on the old bones is re-created on the clown's bones via the humanoid bone map, sized to the new rig.
    ///   4. Puts glowing red eyes (HDR unlit spheres, picked up by Bloom) on the clown's eye bones.
    /// Safe to re-run: once the clown is in, only the materials and eyes are refreshed.
    /// </summary>
    public static class ClownZombieBuilder
    {
        const string Folder = "Assets/Art/Zombies/WhiteClown";
        const string ModelPath = Folder + "/WhiteClown.fbx";
        const string TexturesFolder = Folder + "/Textures";
        const string MaterialsFolder = Folder + "/Materials";
        const string EyeMaterialPath = MaterialsFolder + "/WhiteClown_Eyes.mat";
        const string PrefabPath = "Assets/Prefabs/Zombie.prefab";
        const string ModelChildName = "WhiteClown";
        const string EyeName = "GlowEye";
        static readonly Color EyeColor = new Color(1f, 0.02f, 0.01f) * 16f; // HDR so Bloom catches it hard; much higher burns out to white
        const float EyeDiameter = 0.034f; // metres

        [MenuItem("BLINK/Zombies/Build White Clown Zombie")]
        public static void Build()
        {
            if (AssetImporter.GetAtPath(ModelPath) == null) AssetDatabase.ImportAsset(ModelPath);
            if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
            {
                Debug.LogError($"[ClownZombie] No model at {ModelPath}.");
                return;
            }

            ConfigureImporter(importer);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError("[ClownZombie] Humanoid avatar failed to build; check the rig in the model's Rig tab.");
                return;
            }

            SetupMaterials(importer);
            BuildPrefab(avatar);
        }

        // ---------------------------------------------------------------- import

        static void ConfigureImporter(ModelImporter importer)
        {
            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Human)
            { importer.animationType = ModelImporterAnimationType.Human; changed = true; }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; changed = true; }
            if (importer.importAnimation)
            { importer.importAnimation = false; changed = true; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription)
            { importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription; changed = true; }
            if (importer.materialLocation != ModelImporterMaterialLocation.InPrefab)
            { importer.materialLocation = ModelImporterMaterialLocation.InPrefab; changed = true; }
            if (changed) importer.SaveAndReimport();
        }

        static void SetupMaterials(ModelImporter importer)
        {
            EnsureFolder(TexturesFolder);
            EnsureFolder(MaterialsFolder);

            if (AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder }).Length == 0)
            {
                importer.ExtractTextures(TexturesFolder);
                AssetDatabase.Refresh();
            }

            var texturePaths = AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder })
                .Select(AssetDatabase.GUIDToAssetPath).ToList();
            string normalPath = texturePaths.FirstOrDefault(p => Path.GetFileName(p).ToLowerInvariant().Contains("normal"));
            string diffusePath = texturePaths.FirstOrDefault(p =>
            {
                string n = Path.GetFileName(p).ToLowerInvariant();
                return n.Contains("diffuse") || n.Contains("albedo") || n.Contains("basecolor");
            });

            if (normalPath != null && AssetImporter.GetAtPath(normalPath) is TextureImporter ni
                && ni.textureType != TextureImporterType.NormalMap)
            {
                ni.textureType = TextureImporterType.NormalMap;
                ni.SaveAndReimport();
            }

            var diffuse = diffusePath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(diffusePath) : null;
            var normal = normalPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) : null;
            bool alphaClip = diffusePath != null && AssetImporter.GetAtPath(diffusePath) is TextureImporter di
                             && di.DoesSourceTextureHaveAlpha();
            if (diffuse == null) Debug.LogWarning("[ClownZombie] No diffuse texture found in " + TexturesFolder);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var sourceNames = model.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().ToList();

            bool remapped = false;
            foreach (string name in sourceNames)
            {
                string path = $"{MaterialsFolder}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(lit) { name = name };
                    AssetDatabase.CreateAsset(mat, path);
                }
                else if (mat.shader != lit) mat.shader = lit;

                mat.SetTexture("_BaseMap", diffuse);
                mat.SetTexture("_MainTex", diffuse);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Smoothness", 0.35f);
                mat.SetTexture("_BumpMap", normal);
                if (normal != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
                mat.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
                mat.SetFloat("_Cutoff", 0.4f);
                if (alphaClip) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
                mat.renderQueue = alphaClip ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : -1;
                EditorUtility.SetDirty(mat);

                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                importer.GetExternalObjectMap().TryGetValue(id, out var existing);
                if (existing != mat)
                {
                    importer.AddRemap(id, mat);
                    remapped = true;
                }
            }
            AssetDatabase.SaveAssets();
            if (remapped) importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------- prefab

        static void BuildPrefab(Avatar clownAvatar)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var girlAnimator = root.GetComponentInChildren<Animator>(true);
                if (girlAnimator == null || girlAnimator.avatar == null || !girlAnimator.avatar.isHuman)
                {
                    Debug.LogError("[ClownZombie] Zombie.prefab has no humanoid Animator to copy from.");
                    return;
                }
                if (girlAnimator.avatar == clownAvatar)
                {
                    // Already the clown: just refresh the eyes.
                    AddGlowEyes(HumanBones(clownAvatar, girlAnimator.transform));
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log("[ClownZombie] Zombie.prefab is already the clown; eyes refreshed.");
                    return;
                }
                var girlModel = girlAnimator.transform;

                var clownAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                var clownModel = ((GameObject)PrefabUtility.InstantiatePrefab(clownAsset, root.transform)).transform;
                clownModel.name = ModelChildName;
                clownModel.SetSiblingIndex(girlModel.GetSiblingIndex());
                clownModel.localPosition = girlModel.localPosition;
                clownModel.localRotation = girlModel.localRotation;
                clownModel.localScale = girlModel.localScale;

                var clownAnimator = clownModel.GetComponent<Animator>();
                if (clownAnimator == null) clownAnimator = clownModel.gameObject.AddComponent<Animator>();
                UnityEditorInternal.ComponentUtility.CopyComponent(girlAnimator);
                UnityEditorInternal.ComponentUtility.PasteComponentValues(clownAnimator);
                clownAnimator.avatar = clownAvatar;

                var girlBones = HumanBones(girlAnimator.avatar, girlModel);
                var clownBones = HumanBones(clownAvatar, clownModel);
                CopyLayers(girlModel, clownModel);

                float heightRatio = ModelHeight(clownBones, clownModel) / ModelHeight(girlBones, girlModel);
                int bodies = CopyRagdoll(girlModel, girlBones, clownBones, heightRatio);
                ScaleRootToHeight(root, heightRatio);
                AddGlowEyes(clownBones);

                Object.DestroyImmediate(girlModel.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved)
                {
                    Debug.LogError("[ClownZombie] Failed to save " + PrefabPath);
                    return;
                }
                Debug.Log($"[ClownZombie] Built {PrefabPath}: {bodies} ragdoll bodies, height x{heightRatio:0.00} " +
                          $"vs ZombieGirl, controller '{clownAnimator.runtimeAnimatorController?.name}'.",
                          AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>A small HDR-red unlit sphere on each eye bone, no collider or shadow. Replaces any previous pair.</summary>
        static void AddGlowEyes(Dictionary<string, Transform> bones)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(EyeMaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, EyeMaterialPath);
            }
            mat.SetColor("_BaseColor", EyeColor);
            EditorUtility.SetDirty(mat);

            var sphereMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            foreach (string side in new[] { "LeftEye", "RightEye" })
            {
                if (!bones.TryGetValue(side, out var eyeBone))
                {
                    Debug.LogWarning($"[ClownZombie] No {side} bone on the clown; no glowing eye there.");
                    continue;
                }
                var old = eyeBone.Find(EyeName);
                if (old != null) Object.DestroyImmediate(old.gameObject);

                var eye = new GameObject(EyeName, typeof(MeshFilter), typeof(MeshRenderer));
                eye.layer = eyeBone.gameObject.layer;
                eye.transform.SetParent(eyeBone, false);
                float s = EyeDiameter / Mathf.Max(1e-6f, eyeBone.lossyScale.x);
                eye.transform.localScale = Vector3.one * s;
                eye.GetComponent<MeshFilter>().sharedMesh = sphereMesh;
                var r = eye.GetComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
        }

        /// <summary>Human bone name (e.g. "LeftUpperArm") to the transform it maps to under modelRoot.</summary>
        static Dictionary<string, Transform> HumanBones(Avatar avatar, Transform modelRoot)
        {
            var byName = new Dictionary<string, Transform>();
            foreach (var t in modelRoot.GetComponentsInChildren<Transform>(true))
                byName.TryAdd(t.name, t);

            var map = new Dictionary<string, Transform>();
            foreach (var hb in avatar.humanDescription.human)
                if (byName.TryGetValue(hb.boneName, out var t)) map[hb.humanName] = t;
            return map;
        }

        static float ModelHeight(Dictionary<string, Transform> bones, Transform modelRoot)
        {
            var head = bones.TryGetValue("Head", out var h) ? h : null;
            if (head == null) return 1f;
            return Mathf.Max(0.01f, head.position.y - modelRoot.position.y);
        }

        static void CopyLayers(Transform girlModel, Transform clownModel)
        {
            var layerByName = new Dictionary<string, int>();
            foreach (var t in girlModel.GetComponentsInChildren<Transform>(true))
                layerByName.TryAdd(t.name, t.gameObject.layer);
            var girlSkin = girlModel.GetComponentInChildren<SkinnedMeshRenderer>(true);
            int rendererLayer = girlSkin != null ? girlSkin.gameObject.layer : girlModel.gameObject.layer;

            foreach (var t in clownModel.GetComponentsInChildren<Transform>(true))
            {
                if (layerByName.TryGetValue(t.name, out int layer)) t.gameObject.layer = layer;
                else if (t.GetComponent<Renderer>() != null) t.gameObject.layer = rendererLayer;
                else t.gameObject.layer = girlModel.gameObject.layer;
            }
            clownModel.gameObject.layer = girlModel.gameObject.layer;
        }

        // ---------------------------------------------------------------- ragdoll

        // Limb segments sized by their own bone length; everything else by overall height.
        static readonly Dictionary<string, string> LimbChild = new()
        {
            { "LeftUpperArm", "LeftLowerArm" }, { "RightUpperArm", "RightLowerArm" },
            { "LeftLowerArm", "LeftHand" },     { "RightLowerArm", "RightHand" },
            { "LeftUpperLeg", "LeftLowerLeg" }, { "RightUpperLeg", "RightLowerLeg" },
            { "LeftLowerLeg", "LeftFoot" },     { "RightLowerLeg", "RightFoot" },
        };

        static int CopyRagdoll(Transform girlModel, Dictionary<string, Transform> girlBones,
                               Dictionary<string, Transform> clownBones, float heightRatio)
        {
            var humanOf = girlBones.ToDictionary(kv => kv.Value, kv => kv.Key);
            var bodyMap = new Dictionary<Rigidbody, Rigidbody>();
            var boneMap = new Dictionary<Rigidbody, (Transform src, Transform dst, float scale)>();

            foreach (var rb in girlModel.GetComponentsInChildren<Rigidbody>(true))
            {
                if (!humanOf.TryGetValue(rb.transform, out string human) || !clownBones.TryGetValue(human, out var dst))
                {
                    Debug.LogWarning($"[ClownZombie] Ragdoll bone '{rb.name}' has no humanoid match on the clown; skipped.");
                    continue;
                }
                var src = rb.transform;
                float scale = heightRatio;
                if (LimbChild.TryGetValue(human, out string child)
                    && girlBones.TryGetValue(child, out var gc) && clownBones.TryGetValue(child, out var cc))
                {
                    float g = Vector3.Distance(src.position, gc.position);
                    if (g > 1e-4f) scale = Vector3.Distance(dst.position, cc.position) / g;
                }

                dst.gameObject.layer = src.gameObject.layer;
                var newRb = dst.gameObject.AddComponent<Rigidbody>();
                UnityEditorInternal.ComponentUtility.CopyComponent(rb);
                UnityEditorInternal.ComponentUtility.PasteComponentValues(newRb);
                foreach (var col in src.GetComponents<Collider>()) CopyCollider(col, src, dst, scale);

                bodyMap[rb] = newRb;
                boneMap[rb] = (src, dst, scale);
            }

            foreach (var entry in boneMap)
            {
                var rb = entry.Key;
                var (src, dst, scale) = entry.Value;
                var joint = rb.GetComponent<CharacterJoint>();
                if (joint == null) continue;
                var newJoint = dst.gameObject.AddComponent<CharacterJoint>();
                UnityEditorInternal.ComponentUtility.CopyComponent(joint);
                UnityEditorInternal.ComponentUtility.PasteComponentValues(newJoint);
                newJoint.connectedBody = joint.connectedBody != null && bodyMap.TryGetValue(joint.connectedBody, out var cb) ? cb : null;
                newJoint.axis = MapDirection(joint.axis, src, dst);
                newJoint.swingAxis = MapDirection(joint.swingAxis, src, dst);
                newJoint.anchor = MapOffset(joint.anchor, src, dst, scale);
                newJoint.autoConfigureConnectedAnchor = true;
            }
            return bodyMap.Count;
        }

        static void CopyCollider(Collider col, Transform src, Transform dst, float scale)
        {
            var copy = (Collider)dst.gameObject.AddComponent(col.GetType());
            UnityEditorInternal.ComponentUtility.CopyComponent(col);
            UnityEditorInternal.ComponentUtility.PasteComponentValues(copy);
            float lengthScale = Mathf.Abs(src.lossyScale.x) * scale / Mathf.Max(1e-6f, Mathf.Abs(dst.lossyScale.x));

            switch (col)
            {
                case CapsuleCollider c:
                {
                    var d = (CapsuleCollider)copy;
                    d.center = MapOffset(c.center, src, dst, scale);
                    d.radius = c.radius * lengthScale;
                    d.height = c.height * lengthScale;
                    d.direction = MapAxis(c.direction, src, dst);
                    break;
                }
                case SphereCollider s:
                {
                    var d = (SphereCollider)copy;
                    d.center = MapOffset(s.center, src, dst, scale);
                    d.radius = s.radius * lengthScale;
                    break;
                }
                case BoxCollider b:
                {
                    var d = (BoxCollider)copy;
                    d.center = MapOffset(b.center, src, dst, scale);
                    var size = Vector3.zero;
                    for (int i = 0; i < 3; i++) size[MapAxis(i, src, dst)] = b.size[i] * lengthScale;
                    d.size = size;
                    break;
                }
            }
        }

        /// <summary>A bone-local offset on the source rig, re-expressed in the target bone's space and resized.</summary>
        static Vector3 MapOffset(Vector3 local, Transform src, Transform dst, float scale) =>
            dst.InverseTransformVector(src.TransformVector(local) * scale);

        static Vector3 MapDirection(Vector3 local, Transform src, Transform dst) =>
            dst.InverseTransformDirection(src.TransformDirection(local)).normalized;

        /// <summary>The target bone's local axis (0=X,1=Y,2=Z) closest to the source bone's given local axis.</summary>
        static int MapAxis(int axis, Transform src, Transform dst)
        {
            var v = Vector3.zero;
            v[axis] = 1f;
            var d = MapDirection(v, src, dst);
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
            return ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
        }

        // ---------------------------------------------------------------- root sizing

        /// <summary>Body capsule, nav agent, eye line and mouth follow the clown's height.</summary>
        static void ScaleRootToHeight(GameObject root, float ratio)
        {
            if (Mathf.Abs(ratio - 1f) < 0.02f) return;

            if (root.TryGetComponent(out CapsuleCollider capsule))
            {
                capsule.height *= ratio;
                capsule.center *= ratio;
            }
            if (root.TryGetComponent(out NavMeshAgent agent)) agent.height *= ratio;

            foreach (var behaviour in root.GetComponents<MonoBehaviour>())
            {
                var so = new SerializedObject(behaviour);
                bool dirty = false;
                var eye = so.FindProperty("eyeHeight");
                if (eye != null && eye.propertyType == SerializedPropertyType.Float) { eye.floatValue *= ratio; dirty = true; }
                var mouth = so.FindProperty("mouthOffset");
                if (mouth != null && mouth.propertyType == SerializedPropertyType.Vector3) { mouth.vector3Value *= ratio; dirty = true; }
                if (dirty) so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
