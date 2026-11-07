using UnityEngine;

namespace Blink.Combat
{
    /// <summary>
    /// The empty-handed view model (<c>Prefabs/Weapons/ViewModel_Hands</c>): our arms held low and relaxed at the
    /// bottom of the screen, with a slow breathing sway. Visual only: there is no attack. Interacting (doors) works
    /// whatever is in hand.
    ///
    /// Same rig as the other view models: the arms are an instance of ArmsBeretta.fbx (pistol hidden), each hand
    /// pulled to a target by an Animation Rigging two-bone IK, and the Animator only holds a relaxed finger pose.
    /// Both targets hang off <see cref="swayPivot"/>, which this moves; the pivot carries a RigTransform so the
    /// IK reads this frame's pose. The sway is the player's body, so it runs on unscaled time like the camera.
    /// Parent the root to the camera at local (0, 0, 0): the pose is authored in camera space.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HandsViewModel : MonoBehaviour
    {
        [Tooltip("Parent of both hand IK targets. Swayed around its rest pose.")]
        [SerializeField] Transform swayPivot;

        [Header("Idle sway (unscaled time)")]
        [Tooltip("Peak offset in metres (x right, y up, z forward).")]
        [SerializeField] Vector3 swayPosition = new(0.004f, 0.007f, 0.003f);
        [Tooltip("Peak rotation in degrees.")]
        [SerializeField] Vector3 swayRotation = new(1.2f, 0.8f, 1f);
        [Tooltip("Breaths per second.")]
        [SerializeField, Min(0f)] float swayFrequency = 0.3f;

        Vector3 _restPosition;
        Quaternion _restRotation = Quaternion.identity;
        bool _initialised;
        float _time;

        void Awake() => EnsureInitialised();

        void OnDisable()
        {
            if (_initialised && swayPivot != null) swayPivot.SetLocalPositionAndRotation(_restPosition, _restRotation);
        }

        void Update() => Step(Time.unscaledDeltaTime);

        /// <summary>Advances the sway by <paramref name="seconds"/> and poses the pivot (handy in edit mode).</summary>
        public void Step(float seconds)
        {
            EnsureInitialised();
            if (swayPivot == null) return;
            _time += Mathf.Max(0f, seconds);

            // Two incommensurate sines per axis read as breathing rather than a loop (as on the axe).
            float w = Mathf.PI * 2f * swayFrequency * _time;
            var wave = new Vector3(Mathf.Sin(w * 0.5f + 0.4f), Mathf.Sin(w), Mathf.Sin(w * 0.77f + 2.1f));
            Vector3 position = _restPosition + Vector3.Scale(swayPosition, wave);
            Quaternion rotation = _restRotation * Quaternion.Euler(Vector3.Scale(swayRotation, new Vector3(wave.y, wave.x, wave.z)));
            swayPivot.SetLocalPositionAndRotation(position, rotation);
        }

        void EnsureInitialised()
        {
            if (_initialised || swayPivot == null) return;
            _initialised = true;
            _restPosition = swayPivot.localPosition;
            _restRotation = swayPivot.localRotation;
        }
    }
}
