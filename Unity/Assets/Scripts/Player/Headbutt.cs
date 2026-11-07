using UnityEngine;

namespace Blink.Player
{
    /// <summary>
    /// The camera path for the bare-handed grab kill: the head draws back, slams forward into the zombie's face at
    /// <see cref="ContactSeconds"/>, then eases back to rest by <see cref="TotalSeconds"/>. Offsets are in the camera's
    /// own axes (z forward, positive pitch looks down) on top of its normal pose. Pure so it's unit tested;
    /// <see cref="GrabQte"/> samples it on unscaled time and hands it to <see cref="CameraFeel.SetScriptedOffset"/>.
    /// </summary>
    public static class Headbutt
    {
        /// <summary>Drawing the head back.</summary>
        public const float WindUpSeconds = 0.14f;
        /// <summary>Forehead meets face: the blow lands here.</summary>
        public const float ContactSeconds = 0.22f;
        /// <summary>Back at rest.</summary>
        public const float TotalSeconds = 0.75f;

        public const float PullBack = 0.08f;     // metres back at the top of the wind-up
        public const float Lunge = 0.32f;        // metres forward on contact
        const float WindUpPitch = -9f;           // looks up a little as the head goes back
        const float ContactPitch = 16f;          // and down into the face on the slam
        const float ContactRoll = 4f;

        /// <summary>The camera offset <paramref name="t"/> seconds into the headbutt. Zero before it starts and after it ends.</summary>
        public static void Sample(float t, out Vector3 position, out Vector3 euler)
        {
            float forward, pitch, roll;
            if (t <= 0f || t >= TotalSeconds)
            {
                forward = pitch = roll = 0f;
            }
            else if (t < WindUpSeconds)
            {
                float k = Smooth(t / WindUpSeconds);
                forward = -PullBack * k;
                pitch = WindUpPitch * k;
                roll = 0f;
            }
            else if (t < ContactSeconds)
            {
                // Fast and accelerating into the face: ease-in so the hit lands at full speed.
                float k = (t - WindUpSeconds) / (ContactSeconds - WindUpSeconds);
                k *= k;
                forward = Mathf.Lerp(-PullBack, Lunge, k);
                pitch = Mathf.Lerp(WindUpPitch, ContactPitch, k);
                roll = ContactRoll * k;
            }
            else
            {
                float k = Smooth((t - ContactSeconds) / (TotalSeconds - ContactSeconds));
                forward = Lunge * (1f - k);
                pitch = ContactPitch * (1f - k);
                roll = ContactRoll * (1f - k);
            }
            position = new Vector3(0f, 0f, forward);
            euler = new Vector3(pitch, 0f, roll);
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
