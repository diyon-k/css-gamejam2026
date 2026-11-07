using System;
using Blink.Audio;
using Blink.Interaction;
using Blink.UI;
using UnityEngine;

namespace Blink.World
{
    /// <summary>
    /// The way out: a door with several locks (three on the bungalow's double front door, D1), one key per lock. The keys
    /// come from the puzzle rooms. The player holds one thing at a time, so keys go in one by one: walk up holding a key
    /// that fits and Interact puts it in its keyhole, where it stays and turns. When the last one turns, the leaves swing
    /// open (<see cref="Opened"/>).
    ///
    /// Trying the door without a fitting key rattles it and puts a line in the prompt stack for a few seconds saying how
    /// many keyholes are still empty ("Locked. Three keyholes, all empty."), or that the held key doesn't fit.
    ///
    /// The leaves and lock plates use <see cref="FrontDoorPart"/> to send looks and presses here. Each leaf turns about
    /// its own hinge (world space; a level kit's leaf is pivoted at the world origin). Set up by
    /// BLINK/Bungalow/Set Up Front Door. Lock state is pure logic in <see cref="KeySlots"/>.
    /// </summary>
    public sealed class FrontDoor : MonoBehaviour, IInteractable
    {
        [Serializable]
        public sealed class Lock
        {
            [Tooltip("Item id of the key this lock takes (the puzzle room's reward Pickup).")]
            public string itemId;
            [Tooltip("What the key is called in the prompt, e.g. \"Lily's key\".")]
            public string keyName = "key";
            [Tooltip("Pivot at the keyhole, local +Z pointing out of the door, with the key model under it. Hidden until " +
                     "the key goes in; its pose in the scene is the key pushed home, before it turns.")]
            public Transform keyVisual;
        }

        [Serializable]
        public sealed class Leaf
        {
            public Transform leaf;
            [Tooltip("The hinge, world space (the leaf's pivot may be anywhere).")]
            public Vector3 hinge;
            [Tooltip("Degrees about world up from shut to open.")]
            public float openAngle = 90f;
        }

        public string noun = "front door";
        public Lock[] locks = Array.Empty<Lock>();
        public Leaf[] leaves = Array.Empty<Leaf>();

        [Header("Prompt (real time)")]
        [Tooltip("Real seconds a line (locked / doesn't fit / the key turns) stays up.")]
        [SerializeField, Min(0f)] float messageSeconds = 3f;

        [Header("Keys (real time)")]
        [SerializeField, Min(0.01f)] float keyInsertSeconds = 0.3f;
        [SerializeField, Min(0.01f)] float keyTurnSeconds = 0.3f;
        [Tooltip("How far out of the keyhole a key starts before it slides in.")]
        [SerializeField, Min(0f)] float keyInsertDepth = 0.05f;
        [Tooltip("Turn about the shaft once it's in.")]
        [SerializeField] float keyTurnDegrees = 90f;

        [Header("Opening (real time)")]
        [Tooltip("Wait after the last key turns before the leaves move.")]
        [SerializeField, Min(0f)] float openDelay = 0.8f;
        [SerializeField, Min(0.01f)] float openSeconds = 1.6f;
        [Tooltip("Each further leaf starts this much later, so a double door doesn't open like a lift.")]
        [SerializeField, Min(0f)] float leafStagger = 0.15f;

        [Header("Rattle (locked)")]
        [SerializeField, Min(0.01f)] float rattleSeconds = 0.35f;
        [SerializeField] float rattleHz = 14f;
        [SerializeField] float rattleDegrees = 0.8f;

        [Header("Audio (optional, 3D at the door)")]
        [SerializeField] SoundCue rattleCue;
        [SerializeField] SoundCue keyTurnCue;
        [SerializeField] SoundCue openCue;

        public enum DoorState { Locked, Unlocking, Open }

        public DoorState State { get; private set; }
        /// <summary>Keys in so far.</summary>
        public int KeysIn => _slots != null ? _slots.Filled : 0;
        public int KeysNeeded => locks.Length;

        /// <summary>A key went into lock <c>index</c>.</summary>
        public event Action<int> KeyInserted;
        /// <summary>The last key turned (the leaves move <see cref="openDelay"/> later).</summary>
        public event Action Unlocked;
        /// <summary>The leaves start to swing open.</summary>
        public event Action Opened;

        KeySlots _slots;
        Vector3[] _closedPos;
        Quaternion[] _closedRot;
        Vector3[] _keyHomePos;
        Quaternion[] _keyHomeRot;
        float[] _keyClock; // real seconds since each key went in; negative = not in
        string _line;
        float _message, _rattle, _openClock = -1f, _openIn;

        void Awake()
        {
            var ids = new string[locks.Length];
            for (int i = 0; i < locks.Length; i++) ids[i] = locks[i] != null ? locks[i].itemId : "";
            _slots = ids.Length > 0 ? new KeySlots(ids) : null;

            _closedPos = new Vector3[leaves.Length];
            _closedRot = new Quaternion[leaves.Length];
            for (int i = 0; i < leaves.Length; i++)
                if (leaves[i]?.leaf != null) leaves[i].leaf.GetPositionAndRotation(out _closedPos[i], out _closedRot[i]);

            _keyHomePos = new Vector3[locks.Length];
            _keyHomeRot = new Quaternion[locks.Length];
            _keyClock = new float[locks.Length];
            for (int i = 0; i < locks.Length; i++)
            {
                _keyClock[i] = -1f;
                var key = locks[i]?.keyVisual;
                if (key == null) continue;
                _keyHomePos[i] = key.localPosition;
                _keyHomeRot[i] = key.localRotation;
                key.gameObject.SetActive(false);
            }
            if (_slots == null) State = DoorState.Open; // no locks: nothing to unlock
        }

        // ------------------------------------------------------------------ IInteractable

        public string Prompt(Interactor who)
        {
            if (State != DoorState.Locked || _message > 0f) return null; // the line is showing instead
            int slot = HeldKeySlot(who);
            return slot >= 0 ? $"Put {locks[slot].keyName} in the lock" : "Try " + noun;
        }

        public bool CanInteract(Interactor who) => State == DoorState.Locked;

        public void Interact(Interactor who)
        {
            if (State != DoorState.Locked) return;
            int slot = HeldKeySlot(who);
            if (slot >= 0) PutKeyIn(who, slot);
            else TryLocked(who);
        }

        /// <summary>The empty lock a key the player carries fits (the one in hand first), or -1.</summary>
        int HeldKeySlot(Interactor who)
        {
            if (_slots == null || who == null || who.Inventory == null) return -1;
            if (Hand.Of(who)?.Item is { } held && who.Inventory.Has(held.itemId))
            {
                int inHand = _slots.EmptySlotFor(held.itemId);
                if (inHand >= 0) return inHand;
            }
            for (int i = 0; i < locks.Length; i++)
                if (!_slots.IsFilled(i) && who.Inventory.Has(_slots.IdAt(i))) return i;
            return -1;
        }

        void PutKeyIn(Interactor who, int slot)
        {
            string id = _slots.IdAt(slot);
            var result = _slots.Insert(id, out _);
            // The key stays in its lock: it leaves the hand for good.
            if (Hand.Of(who) is not { } hand || !hand.UseUp(id)) who.Inventory?.Remove(id);

            _keyClock[slot] = 0f;
            if (locks[slot].keyVisual != null)
            {
                locks[slot].keyVisual.gameObject.SetActive(true);
                PoseKey(slot);
            }
            Play(keyTurnCue, KeyPosition(slot));
            Say(KeySlots.TurnedLine(_slots.Filled, _slots.Count));
            KeyInserted?.Invoke(slot);

            if (result != KeySlotResult.Completed) return;
            State = DoorState.Unlocking;
            _openIn = keyInsertSeconds + keyTurnSeconds + openDelay;
            Unlocked?.Invoke();
        }

        void TryLocked(Interactor who)
        {
            _rattle = rattleSeconds;
            Play(rattleCue, transform.position);
            // Holding a key that isn't one of these: say so, so the player doesn't think the door's broken.
            var hand = Hand.Of(who);
            var held = hand != null ? hand.Item : null;
            bool wrongKey = held != null && !_slots.Fits(held.itemId) && held.itemId.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0;
            Say(wrongKey
                ? $"The {hand.HeldName} doesn't fit any of the locks."
                : KeySlots.LockedLine(_slots.Filled, _slots.Count));
        }

        void Say(string line)
        {
            _line = line;
            _message = messageSeconds;
        }

        // ------------------------------------------------------------------ animation

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_message > 0f)
            {
                _message -= dt;
                BlinkPrompts.Show(this, null, _line);
            }

            for (int i = 0; i < _keyClock.Length; i++)
            {
                if (_keyClock[i] < 0f || _keyClock[i] > keyInsertSeconds + keyTurnSeconds) continue;
                _keyClock[i] += dt;
                PoseKey(i);
            }

            if (State == DoorState.Unlocking)
            {
                _openIn -= dt;
                if (_openIn <= 0f)
                {
                    State = DoorState.Open;
                    _openClock = 0f;
                    Play(openCue, transform.position);
                    Opened?.Invoke();
                }
            }

            bool swinging = _openClock >= 0f && _openClock <= openSeconds + leafStagger * leaves.Length;
            if (swinging) _openClock += dt;
            if (_rattle > 0f || swinging)
            {
                _rattle = Mathf.Max(0f, _rattle - dt);
                // Knocks outward only (never into the frame), decaying.
                float knock = Mathf.Abs(Mathf.Sin(_rattle * rattleHz * Mathf.PI)) * (_rattle / rattleSeconds);
                PoseLeaves(knock);
            }
        }

        void PoseLeaves(float knock)
        {
            for (int i = 0; i < leaves.Length; i++)
            {
                var l = leaves[i];
                if (l?.leaf == null) continue;
                float e = _openClock < 0f ? 0f : Openable.Ease((_openClock - i * leafStagger) / openSeconds, 0f);
                float angle = l.openAngle * e + Mathf.Sign(l.openAngle) * rattleDegrees * knock;
                var turn = Quaternion.AngleAxis(angle, Vector3.up);
                l.leaf.SetPositionAndRotation(l.hinge + turn * (_closedPos[i] - l.hinge), turn * _closedRot[i]);
            }
        }

        /// <summary>Slides in along the shaft, then turns about it.</summary>
        void PoseKey(int i)
        {
            var key = locks[i].keyVisual;
            if (key == null) return;
            float t = _keyClock[i];
            float slide = 1f - Mathf.SmoothStep(0f, 1f, t / keyInsertSeconds);
            float turn = Mathf.SmoothStep(0f, 1f, (t - keyInsertSeconds) / keyTurnSeconds) * keyTurnDegrees;
            var rot = _keyHomeRot[i];
            key.localPosition = _keyHomePos[i] + rot * Vector3.forward * (keyInsertDepth * slide);
            key.localRotation = rot * Quaternion.AngleAxis(turn, Vector3.forward);
        }

        Vector3 KeyPosition(int slot) => locks[slot].keyVisual != null ? locks[slot].keyVisual.position : transform.position;

        static void Play(SoundCue cue, Vector3 at)
        {
            if (cue != null && AudioService.Instance != null) AudioService.Instance.PlayAt(cue, at);
        }
    }
}
