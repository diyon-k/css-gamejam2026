using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.Interaction
{
    /// <summary>
    /// Added by the <see cref="Interactor"/>. Outlines what the player is looking at (the drawer, item, door or note that
    /// Interact would use) with a soft warm rim and outline (<c>Blink/InteractHighlight</c>, material
    /// Resources/InteractHighlight). Each of the target's mesh renderers gets an overlay copy of its mesh as a child;
    /// renderers that belong to something else inside it (a key in a drawer) are left out. Nothing while a prop is carried
    /// or a note is held up. Statically batched renderers are skipped (their mesh is the combined batch).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractHighlight : MonoBehaviour
    {
        const string MaterialResource = "InteractHighlight";
        const string ProxyName = "__InteractHighlight";
        static readonly int PulseId = Shader.PropertyToID("_Pulse");

        Interactor _interactor;
        Material _material;
        IInteractable _current;
        readonly List<GameObject> _proxies = new List<GameObject>();

        void Awake()
        {
            _interactor = GetComponent<Interactor>();
            var source = Resources.Load<Material>(MaterialResource);
            if (source != null) _material = new Material(source) { hideFlags = HideFlags.DontSave };
        }

        void LateUpdate()
        {
            var target = _interactor != null ? _interactor.Target : null;
            if (target != null && (ReferenceEquals(target, Carryable.Held) || ReferenceEquals(target, Readable.Current))) target = null;
            if (target is Object o && o == null) target = null; // destroyed (picked up)
            if (!ReferenceEquals(target, _current))
            {
                Clear();
                _current = target;
                if (target != null && _material != null) Build(target);
            }
            if (_material != null) _material.SetFloat(PulseId, 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 4f));
        }

        void OnDisable()
        {
            Clear();
            _current = null;
        }

        void OnDestroy()
        {
            Clear();
            if (_material != null) Destroy(_material);
        }

        void Build(IInteractable target)
        {
            foreach (var r in RenderersFor(target))
            {
                if (r.isPartOfStaticBatch) continue;
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                var go = new GameObject(ProxyName) { hideFlags = HideFlags.DontSave, layer = r.gameObject.layer };
                go.transform.SetParent(r.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
                for (int i = 0; i < mats.Length; i++) mats[i] = _material;
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.allowOcclusionWhenDynamic = false;
                _proxies.Add(go);
            }
        }

        void Clear()
        {
            foreach (var p in _proxies)
                if (p != null) Destroy(p);
            _proxies.Clear();
        }

        /// <summary>
        /// The mesh renderers that show <paramref name="target"/>: its own and its children's, except those that belong to
        /// another interactable inside it (whose nearest interactable parent isn't the target) and earlier highlight copies.
        /// </summary>
        public static List<MeshRenderer> RenderersFor(IInteractable target)
        {
            var list = new List<MeshRenderer>();
            if (!(target is Component c) || c == null) return list;
            foreach (var r in c.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || r.forceRenderingOff || r.gameObject.name == ProxyName) continue;
                if (!ReferenceEquals(r.GetComponentInParent<IInteractable>(), target)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                list.Add(r);
            }
            return list;
        }
    }
}
