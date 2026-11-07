using System.Collections.Generic;
using Blink.World;
using UnityEngine;

namespace Blink.Audio
{
    /// <summary>
    /// Sounds of the level's interactive objects. For now, every <see cref="JammedDoor"/>: a creaking strain
    /// loop at the door while you force it (<see cref="JammedDoor.ForcingStarted"/> /
    /// <see cref="JammedDoor.ForcingStopped"/>) and a crash when it bursts (<see cref="JammedDoor.Opened"/>).
    /// Doors are found at runtime (rescanned now and then, so doors in additively loaded rooms work too).
    /// Lives on GameSystems.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldAudio : MonoBehaviour
    {
        [Header("Jammed door")]
        [Tooltip("Looping strain/creak while the player forces the door (3D, at the door).")]
        [SerializeField] SoundCue doorForcingLoopCue;
        [Tooltip("Real seconds to fade the creak out when the player lets go.")]
        [SerializeField, Min(0f)] float doorForcingFadeOut = 0.2f;
        [Tooltip("The door gives way (3D, at the door).")]
        [SerializeField] SoundCue doorBurstCue;
        [Tooltip("Real seconds between scans for new doors.")]
        [SerializeField, Min(0.1f)] float rescanInterval = 2f;

        readonly Dictionary<JammedDoor, DoorListener> _doors = new();
        readonly List<JammedDoor> _gone = new();
        float _nextScanTime;

        void OnEnable() => _nextScanTime = 0f;

        void OnDisable()
        {
            foreach (var listener in _doors.Values) listener.Detach();
            _doors.Clear();
        }

        void Update()
        {
            if (Time.unscaledTime < _nextScanTime) return;
            _nextScanTime = Time.unscaledTime + rescanInterval;
            Scan();
        }

        void Scan()
        {
            _gone.Clear();
            foreach (var door in _doors.Keys) if (door == null) _gone.Add(door);
            foreach (var door in _gone)
            {
                _doors[door].Detach();
                _doors.Remove(door);
            }
            foreach (var door in FindObjectsByType<JammedDoor>(FindObjectsSortMode.None))
                if (!_doors.ContainsKey(door)) _doors.Add(door, new DoorListener(this, door));
        }

        /// <summary>Holds one door's subscriptions and its creak loop handle.</summary>
        sealed class DoorListener
        {
            readonly WorldAudio _owner;
            readonly JammedDoor _door;
            AudioHandle _creak;

            public DoorListener(WorldAudio owner, JammedDoor door)
            {
                _owner = owner;
                _door = door;
                door.ForcingStarted += OnForcingStarted;
                door.ForcingStopped += OnForcingStopped;
                door.Opened += OnOpened;
            }

            public void Detach()
            {
                StopCreak(0f);
                if (_door == null) return;
                _door.ForcingStarted -= OnForcingStarted;
                _door.ForcingStopped -= OnForcingStopped;
                _door.Opened -= OnOpened;
            }

            void OnForcingStarted()
            {
                var audio = AudioService.Instance;
                if (audio == null || _owner.doorForcingLoopCue == null) return;
                StopCreak(0f); // still fading out from a moment ago: restart rather than leave it dying
                _creak = audio.PlayLoop(_owner.doorForcingLoopCue, _door.transform);
            }

            void OnForcingStopped() => StopCreak(_owner.doorForcingFadeOut);

            void OnOpened()
            {
                StopCreak(0f); // cut the strain dead under the crash
                var audio = AudioService.Instance;
                if (audio != null && _owner.doorBurstCue != null) audio.PlayAt(_owner.doorBurstCue, _door.transform.position);
            }

            /// <summary>Keeps the handle while it fades, so a later hard stop can still cut it.</summary>
            void StopCreak(float fade)
            {
                var audio = AudioService.Instance;
                if (audio != null) audio.Stop(_creak, fade);
            }
        }
    }
}
