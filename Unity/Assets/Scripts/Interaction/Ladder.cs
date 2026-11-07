using System.Collections;
using Blink.Combat;
using Blink.Player;
using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// Interact at either end to climb to the other, with input and the CharacterController off, on real time like
    /// all player movement. Points are world-space floor points.
    ///
    /// Up: step on (eased), then hand over hand: the body rises one rung per pull, eased so it moves in steps, rolling
    /// towards the reaching hand and nodding into the pull; then a pull over the top (a little hop and a nod down at
    /// the floor). Down is the same backwards. The view levels out while climbing, the weapon in hand is swapped for
    /// empty hands (and back after), and the hands view model follows <see cref="Rungs"/> and <see cref="Mantle"/>.
    /// </summary>
    public sealed class Ladder : MonoBehaviour, IInteractable
    {
        public Vector3 bottom, top;
        /// <summary>Horizontal direction the climber faces (towards the ladder).</summary>
        public Vector3 facing = Vector3.forward;
        /// <summary>How far in front of <see cref="bottom"/> (towards the ladder) the climber rises.</summary>
        public float inset = 0.1f;
        [Tooltip("Seconds per metre for stepping on and off at either end (stretched to the minimums below).")]
        public float secondsPerMetre = 0.35f;

        [Header("Climb feel (real time)")]
        [Tooltip("Metres per pull; the climb is split into whole pulls of about this height (the rungs are 0.3 m apart).")]
        [SerializeField, Min(0.05f)] float rungSpacing = 0.3f;
        [Tooltip("Seconds per pull (one rung, one hand).")]
        [SerializeField, Min(0.05f)] float rungSeconds = 0.24f;
        [Tooltip("Degrees the view rolls towards the reaching hand at the peak of each pull.")]
        [SerializeField] float pullRoll = 1.5f;
        [Tooltip("Degrees the view nods forward at the peak of each pull.")]
        [SerializeField] float pullPitch = 1.2f;
        [Tooltip("View pitch to settle on while climbing (degrees, + = down; 0 = level).")]
        [SerializeField] float lookPitch = -5f;
        [Tooltip("Shortest time for stepping on at the bottom or off at the bottom.")]
        [SerializeField, Min(0.05f)] float mountSeconds = 0.35f;
        [Tooltip("Shortest time for pulling over the top (or swinging onto the ladder from it).")]
        [SerializeField, Min(0.05f)] float mantleSeconds = 0.6f;
        [Tooltip("Metres the head rises above standing height mid-way over the top.")]
        [SerializeField] float mantleHop = 0.12f;
        [Tooltip("Degrees the view nods down mid-way over the top.")]
        [SerializeField] float mantlePitch = 12f;

        /// <summary>True while anyone is on a ladder.</summary>
        public static bool Climbing => _active != null;
        /// <summary>Rungs climbed so far, eased (stepwise); counts back down on the way down. Each whole rung is one hand's pull.</summary>
        public static float Rungs { get; private set; }
        /// <summary>0 → 1 → 0 while pulling over the top (or swinging on from it): hands push down on the edge.</summary>
        public static float Mantle { get; private set; }

        static Ladder _active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _active = null;
            Rungs = Mantle = 0f;
        }

        bool _climbing;
        PlayerController _player;
        Health _health;
        Quaternion _facing;

        bool AtTop(Interactor who)
        {
            float y = who.transform.position.y;
            return Mathf.Abs(y - top.y) < Mathf.Abs(y - bottom.y);
        }

        public string Prompt(Interactor who) => _climbing ? null : AtTop(who) ? "Climb down" : "Climb up";
        public bool CanInteract(Interactor who) => !_climbing && who.Player != null;
        public void Interact(Interactor who) => StartCoroutine(Climb(who.Player, AtTop(who)));

        void OnDisable()
        {
            if (_active == this) _active = null;
            _climbing = false;
        }

        IEnumerator Climb(PlayerController player, bool down)
        {
            _climbing = true;
            _active = this;
            Rungs = Mantle = 0f;
            _player = player;
            _health = player.GetComponent<Health>();
            var body = player.transform;
            var cc = player.GetComponent<CharacterController>();
            player.InputEnabled = false;
            if (cc != null) cc.enabled = false;
            _facing = Quaternion.LookRotation(facing);
            body.rotation = _facing;

            Carryable.DropHeld(); // can't climb with a chair in your arms
            // Hands free to climb: the weapon is swapped out like the grab QTE does, and comes back at the end.
            var weapons = PlayerWeapons.Instance;
            WeaponKind? stowed = null;
            if (weapons != null && weapons.ActiveKind is var held && held != WeaponKind.Hands && weapons.Select(WeaponKind.Hands))
                stowed = held;

            var low = bottom + facing * inset;
            var high = new Vector3(low.x, top.y, low.z);
            int rungs = Mathf.Max(1, Mathf.RoundToInt((top.y - low.y) / rungSpacing));
            if (down)
            {
                Rungs = rungs;
                yield return Mantling(body, body.position, high);
                yield return Pulls(body, low, high, rungs, true);
                yield return Stepping(body, low, bottom);
            }
            else
            {
                yield return Stepping(body, body.position, low);
                yield return Pulls(body, low, high, rungs, false);
                yield return Mantling(body, high, top);
            }

            body.rotation = _facing;
            Mantle = 0f;
            if (cc != null) cc.enabled = true;
            if (stowed is { } kind && weapons != null) weapons.Select(kind);
            if (Alive) player.InputEnabled = true;
            if (_active == this) _active = null;
            _climbing = false;
        }

        bool Alive => !(_health is { IsDead: true });

        // Real time: 0 while paused (timeScale 0), not sped up by eyes-shut fast-forward.
        static float Dt => PlayerController.RealDeltaTime(Time.deltaTime, Time.timeScale);

        // Step on or off at the bottom: eased, no bob.
        IEnumerator Stepping(Transform body, Vector3 from, Vector3 to)
        {
            float seconds = Mathf.Max(mountSeconds, Vector3.Distance(from, to) * secondsPerMetre);
            float t = 0f;
            while (t < 1f && Alive)
            {
                t = Mathf.Min(1f, t + Dt / seconds);
                body.SetPositionAndRotation(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)), _facing);
                LevelView();
                yield return null;
            }
        }

        // Hand over hand: one eased rise per rung, with a roll towards the reaching hand (left on even rungs) and a nod on each pull.
        IEnumerator Pulls(Transform body, Vector3 low, Vector3 high, int rungs, bool down)
        {
            float seconds = rungs * rungSeconds;
            float t = 0f;
            while (t < 1f && Alive)
            {
                t = Mathf.Min(1f, t + Dt / seconds);
                float p = (down ? 1f - t : t) * rungs;
                int rung = Mathf.Min(Mathf.FloorToInt(p), rungs - 1);
                float f = p - rung;
                Rungs = rung + Mathf.SmoothStep(0f, 1f, f);
                float peak = Mathf.Sin(f * Mathf.PI);
                float side = (rung & 1) == 0 ? 1f : -1f;
                body.SetPositionAndRotation(Vector3.Lerp(low, high, Rungs / rungs),
                    _facing * Quaternion.Euler(pullPitch * peak, 0f, side * pullRoll * peak));
                LevelView();
                yield return null;
            }
        }

        // Over the top: eased across with a hop and a nod down at the floor; the hands push on the edge.
        IEnumerator Mantling(Transform body, Vector3 from, Vector3 to)
        {
            float seconds = Mathf.Max(mantleSeconds, Vector3.Distance(from, to) * secondsPerMetre);
            float t = 0f;
            while (t < 1f && Alive)
            {
                t = Mathf.Min(1f, t + Dt / seconds);
                Mantle = Mathf.Sin(t * Mathf.PI);
                body.SetPositionAndRotation(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)) + Vector3.up * (mantleHop * Mantle),
                    _facing * Quaternion.Euler(mantlePitch * Mantle, 0f, 0f));
                LevelView();
                yield return null;
            }
            Mantle = 0f;
        }

        // Eases the view's pitch towards lookPitch, so the climb isn't spent staring at the floor or ceiling.
        void LevelView() => _player.AddLook(0f, (lookPitch - _player.Pitch) * Mathf.Min(1f, Dt * 5f));
    }
}
