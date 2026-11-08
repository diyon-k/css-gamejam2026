using Blink.Effects;
using UnityEngine;

namespace Blink.World
{
    /// <summary>
    /// Planks nailed over a doorway: the child renderers. The <see cref="Breakable"/> on this object takes the chops for
    /// all of them (axe only); when it breaks every plank snaps in two at the chop and both halves fall as loose physics
    /// bodies, broken ends dropping first, knocked away from the axe, and the <see cref="door"/> unlocks.
    /// Placed by BLINK > Bungalow > Place Axe And Barricade.
    /// </summary>
    public sealed class Barricade : MonoBehaviour
    {
        [SerializeField] Door door;
        [Tooltip("Speed the halves are knocked away from the axe, m/s.")]
        [SerializeField, Min(0f)] float knock = 1.8f;
        [Tooltip("How fast the broken ends swing down, rad/s.")]
        [SerializeField, Min(0f)] float droop = 4f;
        [Tooltip("Mass of a whole plank, kg (halves get their share).")]
        [SerializeField, Min(0.1f)] float plankMass = 2f;

        public void Configure(Door barred) => door = barred;

        void Awake()
        {
            if (TryGetComponent(out Breakable breakable)) breakable.Smashed += Smash;
        }

        void Smash(Vector3 point, Vector3 normal)
        {
            foreach (var r in GetComponentsInChildren<MeshRenderer>()) Snap(r.transform, point, normal);
            if (door != null) door.Unlock();
        }

        void Snap(Transform plank, Vector3 point, Vector3 normal)
        {
            // Cube mesh: local x runs -0.5..0.5 along the plank. Never snap right at a nail.
            float at = Mathf.Clamp(plank.InverseTransformPoint(point).x, -0.3f, 0.3f);
            Vector3 push = -normal * knock + Vector3.up * 0.5f;
            Half(plank, -0.5f, at, push, -droop);
            Half(plank, at, 0.5f, push, droop);
        }

        /// <summary>The piece of <paramref name="plank"/> between local x <paramref name="from"/> and <paramref name="to"/>,
        /// falling. <paramref name="spin"/> about the plank's depth axis: negative drops the +x (broken) end of the left half.</summary>
        void Half(Transform plank, float from, float to, Vector3 push, float spin)
        {
            var go = new GameObject(plank.name + "_Half");
            go.transform.SetPositionAndRotation(plank.TransformPoint(new Vector3((from + to) * 0.5f, 0f, 0f)), plank.rotation);
            go.transform.localScale = Vector3.Scale(plank.lossyScale, new Vector3(to - from, 1f, 1f));
            go.AddComponent<MeshFilter>().sharedMesh = plank.GetComponent<MeshFilter>().sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = plank.GetComponent<MeshRenderer>().sharedMaterial;
            go.AddComponent<BoxCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.mass = plankMass * (to - from);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.linearVelocity = push + Random.insideUnitSphere * 0.3f;
            body.angularVelocity = plank.forward * spin + Random.insideUnitSphere * 1.5f;
        }
    }
}
