using Blink.Effects;
using UnityEngine;

namespace Blink.World
{
    /// <summary>
    /// Planks nailed over a doorway. Each child <see cref="Breakable"/> is one plank (axe only, a few chops each); the
    /// <see cref="door"/> stays locked until the last one is gone. Placed by BLINK > Bungalow > Place Axe And Barricade.
    /// </summary>
    public sealed class Barricade : MonoBehaviour
    {
        [SerializeField] Door door;

        int _left;

        public void Configure(Door barred) => door = barred;

        void Awake()
        {
            var planks = GetComponentsInChildren<Breakable>();
            _left = planks.Length;
            foreach (var p in planks) p.Smashed += OnSmashed;
        }

        void OnSmashed()
        {
            if (--_left == 0 && door != null) door.Unlock();
        }
    }
}
