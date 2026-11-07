using Blink.TimeSkip;
using UnityEngine;

namespace Blink.Detection
{
    /// <summary>Corner readout of eye state, source and time scale. Toggle with F1. Debug only.</summary>
    public sealed class BlinkDebugOverlay : MonoBehaviour
    {
        [SerializeField] bool visible = true;

        GUIStyle _style;

        void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible) return;
            var detector = BlinkDetector.Instance;
            if (detector == null) return;

            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14 };
            var skip = TimeSkipController.Instance;
            string eyes = detector.EyesShut ? "SHUT" : detector.EyesClosed ? "closed" : "open";
            string text =
                $"Eyes: {eyes}\n" +
                $"Source: {detector.ActiveSourceName}\n" +
                $"Blinks: {detector.BlinkCount}\n" +
                $"Since last blink: {detector.SecondsSinceLastBlink:0.0} s\n" +
                $"Time scale: {Time.timeScale:0.0}x" +
                (skip != null ? $"\nWorld time skipped: {skip.TotalSkippedSeconds:0.0} s" : "") +
                "\n[F1] hide";
            GUI.Box(new Rect(12, 12, 260, 136), text, _style);
        }
    }
}
