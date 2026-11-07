using UnityEngine;

namespace Blink.UI
{
    /// <summary>
    /// Draws <see cref="BlinkPrompts"/>. Creates itself the first time a prompt is shown; no scene setup.
    /// Sits just above the HUD and held notes, below the eyelids.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class BlinkPromptHud : MonoBehaviour
    {
        const int GuiDepth = -10; // lower draws on top: above the HUD and notes (0), under the eyelids (-1000)

        static BlinkPromptHud _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        internal static void Ensure()
        {
            if (_instance != null || !Application.isPlaying) return;
            var go = new GameObject("BlinkPromptHud");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BlinkPromptHud>();
        }

        void OnGUI()
        {
            GUI.depth = GuiDepth;
            if (Event.current.type != EventType.Repaint) return;
            BlinkPrompts.Draw();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
