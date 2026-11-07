using Blink.Interaction;
using Blink.Player;
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
    ///
    /// On top of the sway, three procedural poses: a reach forward when the player interacts (picks something up,
    /// opens a door), and while a zombie has us (<see cref="GrabQte"/>) a raised, trembling brace against its
    /// chest, a shove on every correct press and, when we break free, a two-handed push (<see cref="GrabShovePose"/>):
    /// pull in, drive out to nearly straight arms as the zombie is pushed over, hold, sink back.
    ///
    /// On a <see cref="Ladder"/> the hands go up onto the rails and climb hand over hand: each hand's IK target is
    /// moved on its own, one reaching up a rung while the other pulls down, following <see cref="Ladder.Rungs"/>;
    /// over the top they push down on the edge (the reach pose, driven by <see cref="Ladder.Mantle"/>).
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

        [Header("Grab an object (on interact)")]
        [Tooltip("Offset at full reach in metres, and the pitch in degrees (+ = tip down).")]
        [SerializeField] Vector3 reachPosition = new(0f, -0.04f, 0.2f);
        [SerializeField] float reachPitch = 18f;
        [SerializeField, Min(0.01f)] float reachSeconds = 0.45f;

        [Header("Grabbed by a zombie (QTE)")]
        [Tooltip("Hands up against the zombie's chest: offset and pitch (- = tip up).")]
        [SerializeField] Vector3 bracePosition = new(0f, 0.12f, 0.08f);
        [SerializeField] float bracePitch = -20f;
        [Tooltip("Tremble while braced: metres and degrees.")]
        [SerializeField] float trembleDistance = 0.008f;
        [SerializeField] float trembleAngle = 2f;
        [Tooltip("Push on each correct press (metres forward). Breaking free plays GrabShovePose instead.")]
        [SerializeField] float shoveDistance = 0.12f;
        [SerializeField, Min(0.01f)] float shoveSeconds = 0.3f;

        [Header("Climbing a ladder")]
        [Tooltip("Hand IK targets (children of the sway pivot), moved apart for hand over hand. Found by name " +
                 "(LeftHandTarget / RightHandTarget) if left empty.")]
        [SerializeField] Transform leftHand, rightHand;
        [Tooltip("Both hands up on the rails: offset in metres and pitch in degrees (- = tip up).")]
        [SerializeField] Vector3 climbPosition = new(0f, 0.14f, 0.08f);
        [SerializeField] float climbPitch = -25f;
        [Tooltip("Metres each hand moves up or down per rung relative to the view (one reaches up as the other pulls down).")]
        [SerializeField] float climbHandTravel = 0.16f;
        [Tooltip("Metres the reaching hand comes off the rung towards us mid-reach.")]
        [SerializeField] float climbHandLift = 0.05f;
        [Tooltip("Seconds to raise the hands onto the ladder, or drop them after.")]
        [SerializeField, Min(0.01f)] float climbBlendSeconds = 0.25f;

        [Header("Held item")]
        [Tooltip("Where a held item (a key, a candle) sits at rest, in camera space: bottom right, by the right hand.")]
        [SerializeField] Vector3 holdPosition = new(0.2f, -0.2f, 0.42f);

        Vector3 _restPosition;
        Quaternion _restRotation = Quaternion.identity;
        bool _initialised;
        Transform _hold;
        float _time;
        Interactor _interactor;
        float _reachAt = float.NegativeInfinity, _shoveAt = float.NegativeInfinity, _breakFreeAt = float.NegativeInfinity;
        float _shoveStrength, _brace, _climb;
        Vector3 _leftRest, _rightRest;

        void Awake()
        {
            EnsureInitialised();
            _interactor = GetComponentInParent<Interactor>();
        }

        void OnEnable()
        {
            if (_interactor != null) _interactor.Interacted += Reach;
            GrabQte.Beat += OnBeat;
            GrabQte.BrokeFree += OnBrokeFree;
            _brace = GrabQte.Active ? 1f : 0f; // swapped in mid-QTE: already braced
            _climb = Ladder.Climbing ? 1f : 0f;
        }

        void OnDisable()
        {
            if (_interactor != null) _interactor.Interacted -= Reach;
            GrabQte.Beat -= OnBeat;
            GrabQte.BrokeFree -= OnBrokeFree;
            _reachAt = _shoveAt = _breakFreeAt = float.NegativeInfinity;
            if (!_initialised) return;
            if (swayPivot != null) swayPivot.SetLocalPositionAndRotation(_restPosition, _restRotation);
            if (leftHand != null) leftHand.localPosition = _leftRest;
            if (rightHand != null) rightHand.localPosition = _rightRest;
        }

        /// <summary>Plays the reach-and-take motion.</summary>
        public void Reach() => _reachAt = _time;

        /// <summary>
        /// Where a held item (<see cref="Hand"/>) sits: a child of the sway pivot placed at <see cref="holdPosition"/> (camera
        /// space) when the pivot is at rest, so the item breathes, reaches and braces with the hands. Created on first use.
        /// </summary>
        public Transform HoldPoint
        {
            get
            {
                if (_hold != null) return _hold;
                EnsureInitialised();
                _hold = new GameObject("HeldItem").transform;
                if (swayPivot == null)
                {
                    _hold.SetParent(transform, false);
                    _hold.localPosition = holdPosition;
                    return _hold;
                }
                // The pose is authored in this root's space (the camera's); express it relative to the pivot's rest pose.
                var parent = swayPivot.parent;
                Vector3 p = parent.InverseTransformPoint(transform.TransformPoint(holdPosition));
                Quaternion r = Quaternion.Inverse(parent.rotation) * transform.rotation;
                _hold.SetParent(swayPivot, false);
                _hold.SetLocalPositionAndRotation(Quaternion.Inverse(_restRotation) * (p - _restPosition), Quaternion.Inverse(_restRotation) * r);
                return _hold;
            }
        }

        /// <summary>Plays a shove; <paramref name="strength"/> 1 = one QTE press.</summary>
        public void Shove(float strength = 1f)
        {
            _shoveAt = _time;
            _shoveStrength = strength;
        }

        void OnBeat() => Shove();
        void OnBrokeFree() => _breakFreeAt = _time; // the two-handed shove that pushes it off (GrabShovePose)

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

            // Grab an object: out and back on a sine.
            float reach = Pulse(_time - _reachAt, reachSeconds * 0.45f, reachSeconds * 0.55f);
            float reachEase = Mathf.SmoothStep(0f, 1f, reach);
            position += reachPosition * reachEase;
            float pitch = reachPitch * reachEase;

            // Grabbed: ease into the brace, tremble, shove.
            _brace = Mathf.MoveTowards(_brace, GrabQte.Active ? 1f : 0f, Mathf.Max(0f, seconds) * 6f);
            float brace = Mathf.SmoothStep(0f, 1f, _brace);
            float shove = Pulse(_time - _shoveAt, shoveSeconds * 0.25f, shoveSeconds * 0.75f) * _shoveStrength;
            // Breaking free: pull in, drive out, hold, sink back; the straining tremble stops once it's going.
            GrabShovePose.Sample(_time - _breakFreeAt, out var breakFree, out float breakFreePitch, out float together);
            float shoving = _time - _breakFreeAt < GrabShovePose.EndSeconds ? 1f : 0f;
            var tremble = new Vector3(Noise(0f), Noise(10f), Noise(20f)) * (brace * (1f - shoving));
            position += bracePosition * brace + tremble * trembleDistance + new Vector3(0f, 0.02f, shoveDistance) * shove + breakFree;
            pitch += bracePitch * brace - 8f * shove + breakFreePitch;
            // Ladder: both hands up on the rails, alternating; over the top, the reach pose pushes down on the edge.
            _climb = Mathf.MoveTowards(_climb, Ladder.Climbing ? 1f : 0f, Mathf.Max(0f, seconds) / climbBlendSeconds);
            float climb = Mathf.SmoothStep(0f, 1f, _climb);
            float mantle = Mathf.SmoothStep(0f, 1f, Ladder.Mantle);
            position += climbPosition * (climb * (1f - mantle)) + reachPosition * mantle;
            pitch += climbPitch * climb * (1f - mantle) + reachPitch * mantle;
            float rungs = Ladder.Rungs;
            if (leftHand != null) leftHand.localPosition = _leftRest + ClimbHandOffset(rungs, 0) * climb + Vector3.right * together;
            if (rightHand != null) rightHand.localPosition = _rightRest + ClimbHandOffset(rungs, 1) * climb + Vector3.left * together;

            rotation *= Quaternion.Euler(pitch + tremble.x * trembleAngle, tremble.y * trembleAngle, tremble.z * trembleAngle);

            swayPivot.SetLocalPositionAndRotation(position, rotation);
        }

        Vector3 ClimbHandOffset(float rungs, int hand)
        {
            float height = ClimbHand(rungs, hand, out float reach);
            return new Vector3(0f, height * climbHandTravel, -climbHandLift * Mathf.Sin(reach * Mathf.PI));
        }

        /// <summary>
        /// Hand over hand: <paramref name="hand"/> 0 (left) reaches up on even rungs and pulls down on odd ones, hand
        /// 1 the other way round. Returns the hand's height, -0.5 (low) to 0.5 (high), in rung travels; continuous
        /// across rungs. <paramref name="reach"/> is 0 → 1 through a reach up (0 while pulling).
        /// </summary>
        public static float ClimbHand(float rungs, int hand, out float reach)
        {
            int rung = Mathf.FloorToInt(rungs);
            float f = rungs - rung;
            bool reaching = ((rung + hand) & 1) == 0;
            reach = reaching ? f : 0f;
            return reaching ? f - 0.5f : 0.5f - f;
        }

        /// <summary>0 → 1 over <paramref name="rise"/> seconds, back to 0 over <paramref name="fall"/>; 0 outside.</summary>
        public static float Pulse(float t, float rise, float fall)
        {
            if (t < 0f || t >= rise + fall) return 0f;
            return t < rise ? Mathf.Sin(t / rise * Mathf.PI * 0.5f) : Mathf.Cos((t - rise) / fall * Mathf.PI * 0.5f);
        }

        // -1..1, fast enough to read as straining arms.
        float Noise(float seed) => Mathf.PerlinNoise(seed, _time * 14f) * 2f - 1f;

        void EnsureInitialised()
        {
            if (_initialised || swayPivot == null) return;
            _initialised = true;
            _restPosition = swayPivot.localPosition;
            _restRotation = swayPivot.localRotation;
            if (leftHand == null) leftHand = swayPivot.Find("LeftHandTarget");
            if (rightHand == null) rightHand = swayPivot.Find("RightHandTarget");
            if (leftHand != null) _leftRest = leftHand.localPosition;
            if (rightHand != null) _rightRest = rightHand.localPosition;
        }
    }
}
