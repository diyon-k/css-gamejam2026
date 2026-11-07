using UnityEngine;

namespace Blink.Player
{
    /// <summary>
    /// The two-handed shove that breaks you free of a grab (<see cref="GrabQte"/>), as an offset for the hands' view
    /// model (<see cref="Combat.HandsViewModel"/>) on top of its brace: the elbows bend and the hands pull in to the
    /// chest, drive out to nearly straight arms at <see cref="ContactSeconds"/> (when the zombie is flung off), hold
    /// there while it goes, then sink back; on the way out they come together toward the middle, palms tipped up.
    /// Camera-space metres (z forward), degrees of pitch (+ tips the hands down) and metres each hand moves toward the
    /// middle (- spreads them). Pure so it's unit tested; sampled on unscaled time.
    /// </summary>
    public static class GrabShovePose
    {
        public const float WindUpSeconds = 0.1f;
        /// <summary>Full reach: the hands meet the zombie's chest and it goes.</summary>
        public const float ContactSeconds = 0.18f;
        public const float HoldUntil = 0.42f;
        public const float EndSeconds = 0.9f;

        // Keyed on these times and eased between them. The brace already has the arms most of the way out, so the
        // pull-in is what makes the thrust read; the thrust stops short of locking the elbows (IK reach).
        static readonly float[] Times = { 0f, WindUpSeconds, ContactSeconds, HoldUntil, EndSeconds };
        static readonly Vector3[] Offsets =
        {
            Vector3.zero, new(0f, -0.04f, -0.15f), new(0f, 0f, 0.07f), new(0f, -0.01f, 0.06f), Vector3.zero,
        };
        static readonly float[] Pitches = { 0f, 10f, -14f, -11f, 0f };
        static readonly float[] Together = { 0f, -0.02f, 0.05f, 0.045f, 0f };

        public static void Sample(float t, out Vector3 offset, out float pitch) => Sample(t, out offset, out pitch, out _);

        public static void Sample(float t, out Vector3 offset, out float pitch, out float together)
        {
            offset = Vector3.zero;
            pitch = together = 0f;
            if (t <= 0f || t >= EndSeconds) return;
            int i = 1;
            while (t > Times[i]) i++;
            float k = Mathf.SmoothStep(0f, 1f, (t - Times[i - 1]) / (Times[i] - Times[i - 1]));
            offset = Vector3.Lerp(Offsets[i - 1], Offsets[i], k);
            pitch = Mathf.Lerp(Pitches[i - 1], Pitches[i], k);
            together = Mathf.Lerp(Together[i - 1], Together[i], k);
        }
    }
}
