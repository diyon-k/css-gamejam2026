using System.Collections.Generic;
using Blink.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests.EditMode
{
    /// <summary>
    /// What the look ray picks (Interactor.FindTarget), what the target highlight covers
    /// (InteractHighlight.RenderersFor) and how a drawer's collider changes with it (DrawerCollider).
    /// Built in the test runner's own temporary scene, far from anything else, and destroyed after each test.
    /// </summary>
    public class InteractTargetingTests
    {
        static readonly Vector3 Origin = new Vector3(0f, 5000f, 0f);
        readonly RaycastHit[] _buffer = new RaycastHit[16];
        readonly List<GameObject> _made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _made) if (go != null) Object.DestroyImmediate(go);
            _made.Clear();
        }

        GameObject Box(string name, Vector3 centre, Vector3 size, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            _made.Add(go);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.localScale = size;
            return go;
        }

        IInteractable Look(Vector3 from, Vector3 at, float assist = 0f)
        {
            Physics.SyncTransforms();
            return Interactor.FindTarget(from, (at - from).normalized, 3f, null, _buffer, assist);
        }

        [Test]
        public void ItemInsideAnOpenDrawer_WinsOverTheDrawer()
        {
            // an open drawer: its collider is the whole drawer; the key lies inside it
            var drawer = Box("Drawer", Origin, new Vector3(1f, 0.25f, 0.5f));
            var openable = drawer.AddComponent<Openable>();
            var key = Box("Key", Origin + new Vector3(0f, -0.1f, 0f), new Vector3(0.1f, 0.02f, 0.08f), drawer.transform);
            var pickup = key.AddComponent<Pickup>();
            var eye = Origin + new Vector3(0f, 0.6f, -0.8f);

            Assert.AreSame(pickup, Look(eye, key.transform.position));
            Assert.AreSame(openable, Look(eye, Origin + new Vector3(0.4f, 0f, -0.25f))); // its front corner: the drawer
        }

        [Test]
        public void ItemBehindASolidBody_IsNotReachable()
        {
            // a closed drawer: only its front is in front of the carcass, the key is inside the carcass
            var body = Box("Body", Origin + new Vector3(0f, 0f, 0.3f), new Vector3(1f, 0.6f, 0.6f));
            var front = Box("DrawerFront", Origin, new Vector3(1f, 0.25f, 0.04f));
            var openable = front.AddComponent<Openable>();
            var key = Box("Key", Origin + new Vector3(0f, 0f, 0.25f), new Vector3(0.1f, 0.02f, 0.08f), front.transform);
            key.AddComponent<Pickup>();
            var eye = Origin + new Vector3(0f, 0f, -1f);

            Assert.AreSame(openable, Look(eye, key.transform.position));
        }

        [Test]
        public void NearMiss_OnASmallItem_IsCaughtByTheAimAssist()
        {
            var key = Box("Key", Origin, new Vector3(0.08f, 0.01f, 0.06f));
            var pickup = key.AddComponent<Pickup>();
            var eye = Origin + new Vector3(0f, 0.5f, -1f);
            var miss = Origin + new Vector3(0.07f, 0f, 0f); // 3 cm past its edge

            Assert.IsNull(Look(eye, miss, 0f));
            Assert.AreSame(pickup, Look(eye, miss, Interactor.AssistRadius));
        }

        [Test]
        public void AimAssist_DoesNotReachThroughAWall()
        {
            var key = Box("Key", Origin, new Vector3(0.08f, 0.01f, 0.06f));
            key.AddComponent<Pickup>();
            Box("Wall", Origin + new Vector3(0.09f, 0f, -0.3f), new Vector3(0.04f, 2f, 0.04f));
            var eye = Origin + new Vector3(0.09f, 0f, -1f);

            Assert.IsNull(Look(eye, Origin + new Vector3(0.09f, 0f, 0f), Interactor.AssistRadius));
        }

        [Test]
        public void Highlight_CoversTheTargetsRenderers_NotThoseOfThingsInsideIt()
        {
            var drawer = Box("Drawer", Origin, new Vector3(1f, 0.25f, 0.5f));
            var openable = drawer.AddComponent<Openable>();
            var handle = Box("Handle", Origin + new Vector3(0f, 0f, -0.3f), new Vector3(0.1f, 0.02f, 0.02f), drawer.transform);
            var key = Box("Key", Origin, new Vector3(0.1f, 0.02f, 0.08f), drawer.transform);
            var pickup = key.AddComponent<Pickup>();

            var forDrawer = InteractHighlight.RenderersFor(openable);
            CollectionAssert.AreEquivalent(new[] { drawer.GetComponent<MeshRenderer>(), handle.GetComponent<MeshRenderer>() }, forDrawer);
            CollectionAssert.AreEquivalent(new[] { key.GetComponent<MeshRenderer>() }, InteractHighlight.RenderersFor(pickup));
        }

        [Test]
        public void DrawerCollider_IsTheFrontWhenShut_AndTheWholeDrawerWhenOpen()
        {
            var drawer = Box("Drawer", Origin, Vector3.one);
            var dc = drawer.AddComponent<DrawerCollider>();
            var box = drawer.GetComponent<BoxCollider>();
            dc.Configure(new Vector3(0f, 0f, 0.25f), new Vector3(1f, 0.3f, 0.05f), Vector3.zero, new Vector3(1f, 0.25f, 0.5f));
            Assert.AreEqual(new Vector3(1f, 0.3f, 0.05f), box.size);

            dc.Apply(true);
            Assert.AreEqual(new Vector3(1f, 0.25f, 0.5f), box.size);
            Assert.AreEqual(Vector3.zero, box.center);

            dc.Apply(false);
            Assert.AreEqual(new Vector3(0f, 0f, 0.25f), box.center);
        }
    }
}
