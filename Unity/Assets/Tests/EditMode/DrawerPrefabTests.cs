using System.Linq;
using Blink.Interaction;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Blink.Tests.EditMode
{
    /// <summary>The bungalow chest of drawers (GothicCommode_01) prefab: three drawers that slide open.</summary>
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
                Assert.That(Vector3.Distance(o.slide, new Vector3(0f, 0f, 0.30f)), Is.LessThan(1e-4f), d.name);
            }
        }

        [Test]
        public void DrawerColliders_StandProudOfTheBody()
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
