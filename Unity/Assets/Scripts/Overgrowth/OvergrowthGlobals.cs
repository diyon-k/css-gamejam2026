using UnityEngine;

namespace Blink.Overgrowth
{
    /// <summary>
    /// Pushes the roof-hole list to the Overgrowth/Surface shader (moss + damp spread around holes).
    /// Globals don't survive a scene reload, so this sits in the scene and sets them on enable.
    /// </summary>
    [ExecuteAlways]
    public class OvergrowthGlobals : MonoBehaviour
    {
        public const int MaxHoles = 16;
        static readonly int HolesId = Shader.PropertyToID("_OgHoles");
        static readonly int CountId = Shader.PropertyToID("_OgHoleCount");

        [Tooltip("xyz = hole centre (world), w = moss/damp radius in metres")]
        public Vector4[] holes = new Vector4[0];

        void OnEnable()
        {
            var arr = new Vector4[MaxHoles]; // fixed size: the first SetGlobalVectorArray call fixes the array length
            int n = Mathf.Min(holes.Length, MaxHoles);
            System.Array.Copy(holes, arr, n);
            Shader.SetGlobalVectorArray(HolesId, arr);
            Shader.SetGlobalFloat(CountId, n);
        }

        void OnValidate() => OnEnable();
    }
}
