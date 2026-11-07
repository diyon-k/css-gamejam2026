using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.Overgrowth
{
    /// <summary>
    /// Draws the far forest with GPU instancing (no GameObjects, no colliders). Batches are spatial chunks
    /// so each one is frustum-culled by its bounds. Filled in by the editor builder.
    /// </summary>
    [ExecuteAlways]
    public class ForestInstancer : MonoBehaviour
    {
        [System.Serializable]
        public class Batch
        {
            public Mesh mesh;
            public Material[] materials;   // one per submesh drawn (fewer = skip trailing submeshes, e.g. a dead tree)
            public bool castShadows;
            public Bounds bounds;
            public List<Matrix4x4> matrices = new List<Matrix4x4>();
        }

        public List<Batch> batches = new List<Batch>();

        void OnEnable() => RenderPipelineManager.beginContextRendering += Draw;
        void OnDisable() => RenderPipelineManager.beginContextRendering -= Draw;

        void Draw(ScriptableRenderContext ctx, List<Camera> cameras)
        {
            int layer = gameObject.layer;
            foreach (var b in batches)
            {
                if (b.mesh == null || b.matrices.Count == 0) continue;
                int subs = Mathf.Min(b.mesh.subMeshCount, b.materials.Length);
                for (int s = 0; s < subs; s++)
                {
                    if (b.materials[s] == null) continue;
                    var rp = new RenderParams(b.materials[s])
                    {
                        worldBounds = b.bounds,
                        layer = layer,
                        receiveShadows = true,
                        shadowCastingMode = b.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    };
                    Graphics.RenderMeshInstanced(rp, b.mesh, s, b.matrices);
                }
            }
        }
    }
}
