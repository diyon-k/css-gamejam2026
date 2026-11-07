using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Blink.Player
{
    /// <summary>
    /// Keeps the first-person hands and weapons on their own rendering layer so the torch (which sits a few
    /// centimetres from them) doesn't blow them out. Viewmodel renderers move to <see cref="Layer"/> only;
    /// lights that should still light them add the bit with <see cref="AddToLight"/>.
    /// </summary>
    public static class ViewmodelLighting
    {
        /// <summary>The "Viewmodel" rendering layer (index 7, named in TagManager).</summary>
        public const int LayerIndex = 7;
        public const uint Layer = 1u << LayerIndex;
        const uint Default = 1u;

        /// <summary>Mask for a viewmodel renderer: gains the Viewmodel bit, loses Default.</summary>
        public static uint HeldMask(uint mask) => (mask | Layer) & ~Default;

        /// <summary>Mask for a world renderer: loses the Viewmodel bit, gains Default.</summary>
        public static uint WorldMask(uint mask) => (mask & ~Layer) | Default;

        /// <summary>Lets <paramref name="light"/> light viewmodel renderers as well as what it already lights.</summary>
        public static void AddToLight(Light light)
        {
            if (light == null) return;
            var data = light.GetUniversalAdditionalLightData();
            data.renderingLayers = (uint)data.renderingLayers | Layer;
        }

        /// <summary>Sets a light to light only the viewmodel.</summary>
        public static void SetViewmodelOnly(Light light)
        {
            light.GetUniversalAdditionalLightData().renderingLayers = Layer;
        }

        /// <summary>Sets a light to light everything except the viewmodel (the torch).</summary>
        public static void RemoveFromLight(Light light)
        {
            var data = light.GetUniversalAdditionalLightData();
            data.renderingLayers = (uint)data.renderingLayers & ~Layer;
        }

        /// <summary>Puts every renderer under <paramref name="root"/> on the viewmodel layer (held) or back on Default (in the world).</summary>
        public static void SetHeld(GameObject root, bool held)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.renderingLayerMask = held ? HeldMask(r.renderingLayerMask) : WorldMask(r.renderingLayerMask);
        }
    }
}
