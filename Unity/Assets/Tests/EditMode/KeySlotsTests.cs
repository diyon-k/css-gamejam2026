using Blink.World;
using NUnit.Framework;

namespace Blink.Tests
{
    /// <summary>The front door's locks: one key per slot, all slots to open.</summary>
    public sealed class KeySlotsTests
    {
        KeySlots _slots;

        [SetUp]
        public void SetUp() => _slots = new KeySlots(new[] { "key_a", "key_b", "key_c" });

        [Test]
        public void StartsEmpty()
        {
            Assert.AreEqual(3, _slots.Count);
            Assert.AreEqual(0, _slots.Filled);
            Assert.IsFalse(_slots.Complete);
        }

        [Test]
        public void EachKeyFillsItsOwnSlot()
        {
            Assert.AreEqual(KeySlotResult.Inserted, _slots.Insert("key_b", out int slot));
            Assert.AreEqual(1, slot);
            Assert.IsTrue(_slots.IsFilled(1));
            Assert.IsFalse(_slots.IsFilled(0));
            Assert.AreEqual(1, _slots.Filled);
        }

        [Test]
        public void AnyOrderOpensOnTheThird()
        {
            Assert.AreEqual(KeySlotResult.Inserted, _slots.Insert("key_c", out _));
            Assert.AreEqual(KeySlotResult.Inserted, _slots.Insert("key_a", out _));
            Assert.IsFalse(_slots.Complete);
            Assert.AreEqual(KeySlotResult.Completed, _slots.Insert("key_b", out _));
            Assert.IsTrue(_slots.Complete);
        }

        [Test]
        public void WrongKeyDoesNotFit()
        {
            Assert.AreEqual(KeySlotResult.DoesNotFit, _slots.Insert("key_bedroom", out int slot));
            Assert.AreEqual(-1, slot);
            Assert.AreEqual(KeySlotResult.DoesNotFit, _slots.Insert("", out _));
            Assert.AreEqual(KeySlotResult.DoesNotFit, _slots.Insert(null, out _));
            Assert.AreEqual(0, _slots.Filled);
        }

        [Test]
        public void SameKeyTwiceIsAlreadyIn()
        {
            _slots.Insert("key_a", out _);
            Assert.AreEqual(KeySlotResult.AlreadyIn, _slots.Insert("key_a", out int slot));
            Assert.AreEqual(-1, slot);
            Assert.AreEqual(1, _slots.Filled);
        }

        [Test]
        public void DuplicateIdsTakeOneKeyEach()
        {
            var two = new KeySlots(new[] { "key", "key" });
            Assert.AreEqual(KeySlotResult.Inserted, two.Insert("key", out int first));
            Assert.AreEqual(KeySlotResult.Completed, two.Insert("key", out int second));
            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void EmptySlotForSkipsFilled()
        {
            Assert.AreEqual(2, _slots.EmptySlotFor("key_c"));
            _slots.Insert("key_c", out _);
            Assert.AreEqual(-1, _slots.EmptySlotFor("key_c"));
            Assert.IsTrue(_slots.Fits("key_c"));
        }

        [Test]
        public void LockedLineCountsTheEmptyKeyholes()
        {
            Assert.AreEqual("Locked. Three keyholes, all empty.", KeySlots.LockedLine(0, 3));
            Assert.AreEqual("Locked. One key turned, two keyholes still empty.", KeySlots.LockedLine(1, 3));
            Assert.AreEqual("Locked. Two keys turned, one keyhole still empty.", KeySlots.LockedLine(2, 3));
        }

        [Test]
        public void TurnedLineCountsDown()
        {
            Assert.AreEqual("The key turns. Two locks to go.", KeySlots.TurnedLine(1, 3));
            Assert.AreEqual("The key turns. One lock to go.", KeySlots.TurnedLine(2, 3));
            Assert.AreEqual("The last lock gives.", KeySlots.TurnedLine(3, 3));
        }
    }
}
