using Blink.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class InventoryTests
    {
        Inventory _inv;

        [SetUp] public void SetUp() => _inv = new GameObject("Inv").AddComponent<Inventory>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(_inv.gameObject);

        [Test]
        public void AddHasCountRemove_TrackDuplicates()
        {
            Assert.IsFalse(_inv.Has("candle"));
            _inv.Add("candle", "Candle");
            _inv.Add("candle");
            _inv.Add("key_brass", "Brass key");

            Assert.IsTrue(_inv.Has("candle"));
            Assert.AreEqual(2, _inv.Count("candle"));
            Assert.AreEqual("Brass key", _inv.DisplayName("key_brass"));
            Assert.AreEqual("unknown", _inv.DisplayName("unknown"));

            Assert.IsTrue(_inv.Remove("candle"));
            Assert.AreEqual(1, _inv.Count("candle"));
            Assert.IsTrue(_inv.Remove("candle"));
            Assert.IsFalse(_inv.Has("candle"));
            Assert.IsFalse(_inv.Remove("candle"));
        }

        [Test]
        public void Events_FireOnAddAndSuccessfulRemoveOnly()
        {
            string added = null, removed = null;
            int removals = 0;
            _inv.ItemAdded += id => added = id;
            _inv.ItemRemoved += id => { removed = id; removals++; };

            _inv.Add("fork");
            Assert.AreEqual("fork", added);
            _inv.Remove("knife");
            Assert.AreEqual(0, removals);
            _inv.Remove("fork");
            Assert.AreEqual("fork", removed);
            Assert.AreEqual(1, removals);
        }
    }
}
