using System.Collections;
using Blink.Combat;
using Blink.Player;
using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// Interact at either end to climb to the other. The player is moved bottom -> straight up -> top
    /// (or back) with its CharacterController and input off, on real time like all player movement.
    /// Points are world-space floor points.
    /// </summary>
    public sealed class Ladder : MonoBehaviour, IInteractable
    {
        public Vector3 bottom, top;
        /// <summary>Horizontal direction the climber faces (towards the ladder).</summary>
        public Vector3 facing = Vector3.forward;
        /// <summary>How far in front of <see cref="bottom"/> (towards the ladder) the climber rises.</summary>
        public float inset = 0.1f;
        public float secondsPerMetre = 0.35f;

        bool _climbing;

        bool AtTop(Interactor who)
        {
            float y = who.transform.position.y;
            return Mathf.Abs(y - top.y) < Mathf.Abs(y - bottom.y);
        }

        public string Prompt(Interactor who) => _climbing ? null : AtTop(who) ? "Climb down" : "Climb up";
        public bool CanInteract(Interactor who) => !_climbing && who.Player != null;
        public void Interact(Interactor who) => StartCoroutine(Climb(who.Player, AtTop(who)));

        IEnumerator Climb(PlayerController player, bool down)
        {
            _climbing = true;
            var body = player.transform;
            var cc = player.GetComponent<CharacterController>();
            player.InputEnabled = false;
            if (cc != null) cc.enabled = false;
            body.rotation = Quaternion.LookRotation(facing);

            var low = bottom + facing * inset;
            var high = new Vector3(low.x, top.y, low.z);
            var path = down ? new[] { top, high, low, bottom } : new[] { bottom, low, high, top };
            var pos = body.position;
            foreach (var target in path)
            {
                float seconds = Mathf.Max(0.05f, Vector3.Distance(pos, target) * secondsPerMetre);
                // Real time: 0 while paused (timeScale 0), not sped up by eyes-shut fast-forward.
                for (float t = 0f; t < 1f; t += PlayerController.RealDeltaTime(Time.deltaTime, Time.timeScale) / seconds)
                {
                    body.position = Vector3.Lerp(pos, target, t);
                    yield return null;
                }
                body.position = pos = target;
            }

            if (cc != null) cc.enabled = true;
            if (!(player.GetComponent<Health>() is { IsDead: true })) player.InputEnabled = true;
            _climbing = false;
        }
    }
}
