using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// A drawer's box collider follows its <see cref="Openable"/>: while shut it is the drawer's front (with a margin, so
    /// it's easy to aim at), while open it is the whole drawer, so the open drawer is easy to close again. Anything lying
    /// inside still wins the look ray (<see cref="Interactor.FindTarget"/>). Set up by the prop prefab build.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class DrawerCollider : MonoBehaviour
    {
        [SerializeField] Vector3 closedCenter, closedSize = Vector3.one, openCenter, openSize = Vector3.one;

        Openable _openable;
        BoxCollider _box;

        /// <summary>Editor setup: the shut and open boxes (local space). Applies the shut one.</summary>
        public void Configure(Vector3 shutCenter, Vector3 shutSize, Vector3 openBoxCenter, Vector3 openBoxSize)
        {
            closedCenter = shutCenter;
            closedSize = shutSize;
            openCenter = openBoxCenter;
            openSize = openBoxSize;
            Apply(false);
        }

        public void Apply(bool open)
        {
            if (_box == null) _box = GetComponent<BoxCollider>();
            _box.center = open ? openCenter : closedCenter;
            _box.size = open ? openSize : closedSize;
        }

        void Awake()
        {
            _openable = GetComponent<Openable>();
            if (_openable == null) return;
            _openable.Toggled += Apply;
            Apply(_openable.IsOpen);
        }

        void OnDestroy()
        {
            if (_openable != null) _openable.Toggled -= Apply;
        }
    }
}
