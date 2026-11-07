using Blink.Effects;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class BreakableTests
    {
        GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void AutoHits_GlassOnce_PorcelainTwice_OthersNever()
        {
            Assert.AreEqual(1, Breakable.AutoHits("GlassLit (Instance)"));
            Assert.AreEqual(1, Breakable.AutoHits("Bulb"));
            Assert.AreEqual(2, Breakable.AutoHits("Porcelain"));
            Assert.AreEqual(0, Breakable.AutoHits("DarkWood"));
            Assert.AreEqual(0, Breakable.AutoHits(null));
        }

        [Test]
        public void IsPaintingName_MatchesCanvasesAndFrames()
        {
            Assert.IsTrue(ImpactProps.IsPaintingName("Canvas_Moss (Instance)"));
            Assert.IsTrue(ImpactProps.IsPaintingName("fancy_picture_frame_01"));
            Assert.IsFalse(ImpactProps.IsPaintingName("Wall_N"));
        }

        [Test]
        public void Pane_BreaksOnTheLastHit_AndDisappears()
        {
            _go = new GameObject("Pane");
            var b = _go.AddComponent<Breakable>();
            b.Configure(2, null);
            Assert.IsFalse(b.Hit(Vector3.zero, Vector3.up));
            Assert.IsTrue(_go.activeSelf);
            Assert.IsTrue(b.Hit(Vector3.zero, Vector3.up));
            Assert.IsFalse(_go.activeSelf);
            Assert.IsFalse(b.Hit(Vector3.zero, Vector3.up)); // already broken
        }

        [Test]
        public void Lamp_PutsOutItsLight_AndStays()
        {
            _go = new GameObject("Lamp");
            var light = new GameObject("PracticalLight").AddComponent<Light>();
            light.transform.SetParent(_go.transform);
            var b = _go.AddComponent<Breakable>();
            b.Configure(1, new[] { light });
            Assert.IsTrue(b.Hit(Vector3.zero, Vector3.up));
            Assert.IsFalse(light.enabled);
            Assert.IsTrue(_go.activeSelf);
        }

        [Test]
        public void Chandelier_WithKinematicBody_Drops()
        {
            _go = new GameObject("Chandelier");
            var body = _go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var b = _go.AddComponent<Breakable>();
            b.Configure(1, null);
            b.Hit(Vector3.zero, Vector3.up);
            Assert.IsFalse(body.isKinematic);
            Assert.IsTrue(_go.activeSelf);
        }
    }
}
