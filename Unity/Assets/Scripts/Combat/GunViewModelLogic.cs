using UnityEngine;

namespace Blink.Combat
{
    /// <summary>Animator states of the first-person arms and pistol that the gun drives.</summary>
    public enum GunViewClip
    {
        Idle,
        Fire,
        FireLast,
        Reload,
        ReloadEmpty,
    }

    /// <summary>
    /// Pure rules behind <see cref="GunViewModelAnimator"/>: which clip a shot or reload plays, and where
    /// in the reload clip the arms should be for a given reload progress. Kept free of Unity objects so
    /// it can be unit tested.
    /// </summary>
    public static class GunViewModelLogic
    {
        /// <summary>The shot that empties the magazine locks the slide back (FireLast); any other plays Fire.</summary>
        public static GunViewClip FireClip(int ammoAfterShot) =>
            ammoAfterShot <= 0 ? GunViewClip.FireLast : GunViewClip.Fire;

        /// <summary>Reloading from an empty magazine also releases the slide (ReloadEmpty).</summary>
        public static GunViewClip ReloadClip(bool fromEmpty) =>
            fromEmpty ? GunViewClip.ReloadEmpty : GunViewClip.Reload;

        /// <summary>
        /// Maps reload progress onto the reload clip's normalized time (0..1). The first
        /// <paramref name="leadInSeconds"/> of the reload belong to the FireLast clip that is still playing
        /// (the last shot starts the reload on the same frame), so the reload clip covers the rest.
        /// Because this depends only on progress, a time skip that jumps the reload forward jumps the
        /// animation with it.
        /// </summary>
        /// <returns>False while the reload is still inside the lead-in.</returns>
        public static bool TryGetReloadClipTime(float progress01, float reloadSeconds, float leadInSeconds,
            out float normalizedTime)
        {
            progress01 = Mathf.Clamp01(progress01);
            if (reloadSeconds <= 0f)
            {
                normalizedTime = 1f;
                return true;
            }

            float leadIn = Mathf.Clamp(leadInSeconds, 0f, reloadSeconds);
            float elapsed = progress01 * reloadSeconds;
            if (leadIn > 0f && elapsed < leadIn && progress01 < 1f)
            {
                normalizedTime = 0f;
                return false;
            }

            float body = reloadSeconds - leadIn;
            normalizedTime = body <= 0f ? 1f : Mathf.Clamp01((elapsed - leadIn) / body);
            return true;
        }
    }
}
