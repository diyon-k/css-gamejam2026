using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// Black all round a peek gap, with soft edges: the view from inside a wardrobe, under a bed or through a vent grate.
    /// IMGUI like BlinkEyelids, so no Canvas; call from OnGUI. A zero gap is a fully black screen.
    /// </summary>
    public static class PeekOverlay
    {
        static Texture2D _edge;

        /// <param name="gap">The see-through gap as a share of the screen (width, height). Tall gaps are centred, low ones sit low.</param>
        /// <param name="alpha">0..1 fade of the black.</param>
        public static void Draw(Vector2 gap, float alpha)
        {
            if (alpha <= 0f || Event.current.type != EventType.Repaint) return;
            if (_edge == null)
            {
                _edge = new Texture2D(32, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int i = 0; i < 32; i++) _edge.SetPixel(i, 0, new Color(0f, 0f, 0f, 1f - Mathf.SmoothStep(0f, 1f, i / 31f)));
                _edge.Apply();
            }
            float w = Screen.width, h = Screen.height;
            float gw = w * Mathf.Clamp01(gap.x), gh = h * Mathf.Clamp01(gap.y);
            float x0 = (w - gw) * 0.5f, x1 = x0 + gw;
            // A low gap (under a bed) sits at the bottom of the screen; a tall one is centred.
            float y0 = gap.y >= 1f ? 0f : h * 0.62f - gh * 0.5f, y1 = y0 + gh;
            float soft = Mathf.Min(gw, gh) * 0.35f;

            var old = GUI.color;
            var white = Texture2D.whiteTexture;
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, x0, h), white);
            GUI.DrawTexture(new Rect(x1, 0f, w - x1, h), white);
            GUI.DrawTexture(new Rect(x0, 0f, gw, y0), white);
            GUI.DrawTexture(new Rect(x0, y1, gw, h - y1), white);
            if (soft > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                // Soft inner edges: the texture fades from black (u 0) to clear (u 1).
                GUI.DrawTextureWithTexCoords(new Rect(x0, y0, soft, gh), _edge, new Rect(0f, 0f, 1f, 1f));
                GUI.DrawTextureWithTexCoords(new Rect(x1 - soft, y0, soft, gh), _edge, new Rect(1f, 0f, -1f, 1f));
                if (y0 > 0f)
                {
                    GUIUtility.RotateAroundPivot(90f, new Vector2(x0, y0));
                    GUI.DrawTexture(new Rect(x0, y0 - gw, soft, gw), _edge);
                    GUI.matrix = Matrix4x4.identity;
                    GUIUtility.RotateAroundPivot(-90f, new Vector2(x0, y1));
                    GUI.DrawTexture(new Rect(x0, y1, soft, gw), _edge);
                    GUI.matrix = Matrix4x4.identity;
                }
            }
            GUI.color = old;
        }
    }
}
