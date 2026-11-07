using System;
using UnityEngine;

namespace Blink.Detection
{
    /// <summary>
    /// The UDP message the Python sidecar sends to 127.0.0.1:5065 about 30 times a second:
    /// <c>{"t": 1731000000123, "present": true, "closed": false}</c>.
    /// The sidecar applies its own open/close hysteresis; Unity only does edge detection.
    /// </summary>
    [Serializable]
    public struct BlinkPacket
    {
        /// <summary>Sender timestamp in milliseconds (informational).</summary>
        public double t;

        /// <summary>A face was found in the frame.</summary>
        public bool present;

        /// <summary>The eyes are closed.</summary>
        public bool closed;

        public EyeState ToEyeState() => new(present, closed);

        /// <summary>Parses one JSON packet. Safe to call from a background thread.</summary>
        public static bool TryParse(string json, out BlinkPacket packet)
        {
            packet = default;
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '{') return false;
            try
            {
                packet = JsonUtility.FromJson<BlinkPacket>(json);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
