using System.Linq;
using Blink.Interaction;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Blink.Tests.EditMode
{
    /// <summary>
    /// The bungalow chest of drawers (GothicCommode_01): three Blender-split drawers set up by
    /// BungalowPropsPlacer.SetupDrawers. (The kitchen cabinets' drawers come from BungalowInteractables.)
    /// </summary>
    public class DrawerPrefabTests
    {
        const string PrefabPath = "Assets/Art/Environment/Bungalow/Props/Prefabs/GothicCommode_01.prefab";

        static GameObject Prefab()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.NotNull(root, PrefabPath);
            return root;
        }

        static Transform[] Drawers(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("_Drawer")).OrderBy(t => t.name).ToArray();

        [Test]
        public void Commode_HasThreeSlidingDrawers()
        {
            var drawers = Drawers(Prefab());
            Assert.AreEqual(3, drawers.Length);
            foreach (var d in drawers)
            {
                var o = d.GetComponent<Openable>();
                Assert.NotNull(o, d.name + " has no Openable");
                Assert.AreEqual(Openable.Motion.Sliding, o.motion, d.name);
                Assert.AreEqual("drawer", o.noun, d.name);
                // straight out of the front, at most 0.30 m and at most 60% of the drawer's depth (0.30 m here)
                float depth = d.GetComponent<MeshFilter>().sharedMesh.bounds.size.z;
                Assert.That(o.slide.x, Is.EqualTo(0f).Within(1e-4f), d.name);
                Assert.That(o.slide.y, Is.EqualTo(0f).Within(1e-4f), d.name);
                Assert.That(o.slide.z, Is.EqualTo(Mathf.Min(0.30f, 0.6f * depth)).Within(1e-3f), d.name);
                Assert.That(o.slide.z, Is.EqualTo(0.30f).Within(1e-4f), d.name);
            }
        }

        [Test]
        public void DrawerColliders_AreLowFrontPanelsProudOfTheBody()
        {
            var root = Prefab();
            var body = root.GetComponent<BoxCollider>();
            Assert.NotNull(body, "body box collider");
            float bodyFront = body.center.z + body.size.z * 0.5f;
            foreach (var d in Drawers(root))
            {
                var c = d.GetComponent<BoxCollider>();
                Assert.NotNull(c, d.name + " has no BoxCollider");
                float front = d.localPosition.z + c.center.z + c.size.z * 0.5f;
                Assert.GreaterOrEqual(front, bodyFront + 0.005f, d.name);
                // only the front panel: the inside stays clear, so the look ray reaches what's in the drawer
                Assert.LessOrEqual(c.size.z, 0.08f, d.name);
                // and only its lower part: looking down into an open drawer from ~1 m passes over the panel's top
                var b = d.GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.LessOrEqual(c.center.y + c.size.y * 0.5f, b.max.y - 0.3f * b.size.y + 1e-3f, d.name);
                Assert.That(c.center.y - c.size.y * 0.5f, Is.EqualTo(b.min.y).Within(1e-3f), d.name);
            }
        }

        [Test]
        public void Drawers_AreNotLightmapStatic()
        {
            foreach (var d in Drawers(Prefab()))
            {
                Assert.AreEqual(0, (int)(GameObjectUtility.GetStaticEditorFlags(d.gameObject) & StaticEditorFlags.ContributeGI), d.name);
                Assert.AreEqual(ReceiveGI.LightProbes, d.GetComponent<MeshRenderer>().receiveGI, d.name);
            }
        }
    }
}
