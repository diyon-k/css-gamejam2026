using System;
using System.Collections.Generic;

namespace Blink.World
{
    /// <summary>What happened when a key was offered to the <see cref="KeySlots"/>.</summary>
    public enum KeySlotResult
    {
        /// <summary>It went into its slot; more slots are still empty.</summary>
        Inserted,
        /// <summary>It went into the last empty slot: every slot is filled.</summary>
        Completed,
        /// <summary>Its slot is already filled.</summary>
        AlreadyIn,
        /// <summary>No slot takes this key.</summary>
        DoesNotFit,
    }

    /// <summary>
    /// The locks of the <see cref="FrontDoor"/>: one slot per key item id. Each key fills its own slot for good, one at
    /// a time (the player holds one item at a time), and the door opens once every slot is filled. Pure logic, so it's
    /// unit tested.
    /// </summary>
    public sealed class KeySlots
    {
        readonly string[] _ids;
        readonly bool[] _filled;

        public KeySlots(IReadOnlyList<string> itemIds)
        {
            if (itemIds == null || itemIds.Count == 0) throw new ArgumentException("Needs at least one slot.", nameof(itemIds));
            _ids = new string[itemIds.Count];
            for (int i = 0; i < _ids.Length; i++) _ids[i] = itemIds[i] ?? "";
            _filled = new bool[_ids.Length];
        }

        public int Count => _ids.Length;
        public int Filled { get; private set; }
        public int Empty => Count - Filled;
        public bool Complete => Filled == Count;

        public string IdAt(int slot) => _ids[slot];
        public bool IsFilled(int slot) => _filled[slot];

        /// <summary>The empty slot <paramref name="itemId"/> fits, or -1.</summary>
        public int EmptySlotFor(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return -1;
            for (int i = 0; i < _ids.Length; i++)
                if (!_filled[i] && _ids[i] == itemId) return i;
            return -1;
        }

        /// <summary>Some slot (filled or not) takes <paramref name="itemId"/>.</summary>
        public bool Fits(string itemId) => !string.IsNullOrEmpty(itemId) && Array.IndexOf(_ids, itemId) >= 0;

        /// <summary>Puts <paramref name="itemId"/> in its empty slot. <paramref name="slot"/> is where it went, or -1.</summary>
        public KeySlotResult Insert(string itemId, out int slot)
        {
            slot = EmptySlotFor(itemId);
            if (slot < 0) return Fits(itemId) ? KeySlotResult.AlreadyIn : KeySlotResult.DoesNotFit;
            _filled[slot] = true;
            Filled++;
            return Complete ? KeySlotResult.Completed : KeySlotResult.Inserted;
        }

        // ------------------------------------------------------------------ words

        static readonly string[] Words = { "No", "One", "Two", "Three", "Four", "Five", "Six" };

        /// <summary>"One", "Two"... (capitalised), or the digits past six.</summary>
        public static string Number(int n) => n >= 0 && n < Words.Length ? Words[n] : n.ToString();

        /// <summary>
        /// What trying the locked door says: how many keyholes there are and how many are still empty, e.g.
        /// "Locked. Three keyholes, all empty." / "Locked. One key turned, two keyholes still empty."
        /// </summary>
        public static string LockedLine(int filled, int count)
        {
            int empty = count - filled;
            if (filled <= 0) return $"Locked. {Number(count)} keyhole{S(count)}, all empty.";
            if (empty <= 0) return "Unlocked.";
            return $"Locked. {Number(filled)} key{S(filled)} turned, {Number(empty).ToLowerInvariant()} keyhole{S(empty)} still empty.";
        }

        /// <summary>What turning a key says, e.g. "The key turns. Two locks to go." or "The last lock gives."</summary>
        public static string TurnedLine(int filled, int count)
        {
            int left = count - filled;
            return left <= 0 ? "The last lock gives." : $"The key turns. {Number(left)} lock{S(left)} to go.";
        }

        static string S(int n) => n == 1 ? "" : "s";
    }
}
