using System;
using System.Collections;
using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// A cloth sheet over furniture. Interact pulls it off: it slides towards the player and crumples on
    /// the floor, and its colliders turn off so whatever was under it can be reached.
    /// </summary>
    public sealed class DustSheet : MonoBehaviour, IInteractable
    {
        public float seconds = 0.6f;

        public event Action Pulled;
        public bool IsPulled { get; private set; }

        public string Prompt(Interactor who) => IsPulled ? null : "Pull off dust sheet";
        public bool CanInteract(Interactor who) => !IsPulled;

        public void Interact(Interactor who)
        {
            IsPulled = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            StartCoroutine(FallAway(who.transform.position));
            Pulled?.Invoke();
        }

        IEnumerator FallAway(Vector3 towards)
        {
            var from = transform.position;
            var dir = Vector3.ProjectOnPlane(towards - from, Vector3.up).normalized;
            float drop = GetComponentInChildren<Renderer>() is { } r ? r.bounds.extents.y - 0.03f : 0.5f;
            var to = from + dir * 0.8f + Vector3.down * drop;
            var scaleFrom = transform.localScale;
            var scaleTo = new Vector3(scaleFrom.x * 1.1f, Mathf.Min(scaleFrom.y, 0.04f), scaleFrom.z * 1.1f);
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
            {
                float e = t * t; // falls, speeding up
                transform.position = Vector3.Lerp(from, to, e);
                transform.localScale = Vector3.Lerp(scaleFrom, scaleTo, e);
                yield return null;
            }
            transform.position = to;
            transform.localScale = scaleTo;
        }
    }
}
