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
        public void ShutDrawer_ColliderIsTheWholeFrontWithAMargin_ProudOfTheBody()
        {
            var root = Prefab();
            var body = root.GetComponent<BoxCollider>();
            Assert.NotNull(body, "body box collider");
            float bodyFront = body.center.z + body.size.z * 0.5f;
            foreach (var d in Drawers(root))
            {
                var c = d.GetComponent<BoxCollider>();
                Assert.NotNull(c, d.name + " has no BoxCollider");
                Assert.NotNull(d.GetComponent<DrawerCollider>(), d.name + " has no DrawerCollider");
                var b = d.GetComponent<MeshFilter>().sharedMesh.bounds;
                float front = d.localPosition.z + c.center.z + c.size.z * 0.5f;
                Assert.GreaterOrEqual(front, bodyFront + 0.005f, d.name);
                Assert.LessOrEqual(c.size.z, 0.08f, d.name);                 // the front panel, not the inside
                Assert.GreaterOrEqual(c.size.y, b.size.y + 0.015f, d.name);  // its full height plus a margin: easy to hit
                Assert.GreaterOrEqual(c.size.x, b.size.x + 0.015f, d.name);
            }
        }

        [Test]
        public void OpenDrawer_ColliderIsTheWholeDrawer()
        {
            foreach (var d in Drawers(Prefab()))
            {
                var probe = Object.Instantiate(d.gameObject);
                try
                {
                    probe.GetComponent<DrawerCollider>().Apply(true);
                    var c = probe.GetComponent<BoxCollider>();
                    var b = probe.GetComponent<MeshFilter>().sharedMesh.bounds;
                    Assert.That(Vector3.Distance(c.center, b.center), Is.LessThan(1e-3f), d.name);
                    Assert.That(Vector3.Distance(c.size, b.size), Is.LessThan(1e-3f), d.name);
                }
                finally
                {
                    Object.DestroyImmediate(probe);
                }
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
