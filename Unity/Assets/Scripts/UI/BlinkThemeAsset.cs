using UnityEngine;

namespace Blink.UI
{
    /// <summary>
    /// The tunable half of <see cref="BlinkTheme"/>: fonts, palette, sizes and timings for every piece of in-game UI.
    /// The live one is <c>Assets/Art/UI/Resources/BlinkTheme.asset</c>, loaded automatically; without it the
    /// theme falls back to these defaults and an OS serif. Edit it in Play mode to retune the HUD live.
    /// </summary>
    [CreateAssetMenu(menuName = "Blink/UI Theme", fileName = "BlinkTheme")]
    public sealed class BlinkThemeAsset : ScriptableObject
    {
        [Header("Fonts (SIL OFL, see CREDITS.md)")]
        [Tooltip("Titles and headings: letterspaced small caps. Cormorant SC Medium.")]
        public Font display;
        [Tooltip("HUD labels and prompts: small caps with a little more weight. Cormorant SC SemiBold.")]
        public Font caps;
        [Tooltip("Readable text (notes). EB Garamond Regular.")]
        public Font body;
        [Tooltip("HUD numbers and key letters. EB Garamond Medium.")]
        public Font numbers;

        [Header("Palette")]
        [Tooltip("Primary text and strokes. Never pure white.")]
        public Color bone = new(0.902f, 0.878f, 0.831f, 1f);      // #E6E0D4
        [Tooltip("Secondary text: weapon names, reserve ammo, the day.")]
        public Color muted = new(0.58f, 0.565f, 0.53f, 1f);       // #948F87
        [Tooltip("The one accent: kills, danger, the title rule. Use sparingly.")]
        public Color accent = new(0.639f, 0.075f, 0.106f, 1f);    // #A3131B
        [Tooltip("Near-black for backdrops and key-cap fills (alpha is applied per use).")]
        public Color ink = new(0.04f, 0.035f, 0.035f, 1f);
        [Tooltip("Soft text shadow and dark gradients behind HUD text.")]
        public Color shadow = new(0f, 0f, 0f, 0.55f);
        [Tooltip("Note paper, centre.")]
        public Color paper = new(0.90f, 0.86f, 0.76f, 1f);
        [Tooltip("Note paper, toward the aged edges.")]
        public Color paperEdge = new(0.71f, 0.62f, 0.47f, 1f);
        [Tooltip("Ink on paper.")]
        public Color paperInk = new(0.17f, 0.13f, 0.09f, 1f);

        [Header("Scale")]
        [Tooltip("Everything is authored for 1080p and scales with screen height; this multiplies on top.")]
        [Range(0.5f, 2f)] public float uiScale = 1f;
        [Tooltip("HUD type never shrinks below this fraction of its 1080p size (keeps 720p legible).")]
        [Range(0.5f, 1f)] public float minTextScale = 0.8f;
        [Tooltip("Distance of HUD elements from the screen edges, in 1080p pixels.")]
        public float margin = 48f;

        [Header("Type sizes (1080p pixels)")]
        public float titleSize = 120f;
        public float headingSize = 52f;
        public float subtitleSize = 30f;
        public float promptSize = 28f;
        public float smallCapsSize = 24f;
        public float bodySize = 30f;
        public float numberSize = 46f;
        public float keySize = 21f;
        [Tooltip("Extra space between letters of letterspaced text, as a fraction of the font size.")]
        public float tracking = 0.14f;

        [Header("Reticle and hit ring (1080p pixels)")]
        public float dotRadius = 2.5f;
        public float hitRingRadius = 10f;
        public float hitRingThickness = 1.6f;
        [Tooltip("Headshots: radius and thickness multiplier.")]
        public float headshotScale = 1.3f;

        [Header("Motion (real seconds)")]
        [Tooltip("Default fade for HUD elements appearing.")]
        public float fadeIn = 0.15f;
        [Tooltip("Default fade for HUD elements going away.")]
        public float fadeOut = 0.3f;
        [Tooltip("How long ammo and similar transient readouts linger before fading.")]
        public float linger = 2.5f;
    }
}
