using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Detection
{
    /// <summary>
    /// Fallback that always works: hold the key to "close your eyes".
    /// Tap = blink, hold longer than the hold threshold = eyes shut.
    /// </summary>
    public sealed class KeyboardEyeStateSource : MonoBehaviour, IEyeStateSource
    {
        [SerializeField] Key key = Key.Space;

        public string SourceName => $"Keyboard ({key})";

        // Always active, but only ever reports "closed" while the key is held,
        // so it never overrides the webcam with "open".
        public bool IsActive => true;

        public EyeState Current
        {
            get
            {
                var keyboard = Keyboard.current;
                bool held = keyboard != null && keyboard[key].isPressed;
                return new EyeState(present: true, closed: held);
            }
        }
    }
}
