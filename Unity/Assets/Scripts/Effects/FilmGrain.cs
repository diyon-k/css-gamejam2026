using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Blink.Effects
{
    /// <summary>
    /// Barely-there film grain over the game view (FilmGrain.shader), for a slightly uneasy feel.
    ///
    /// Injects its own URP pass from <see cref="RenderPipelineManager.beginCameraRendering"/>, so it
    /// works with any renderer asset and needs no renderer feature set up. Runs after
    /// post-processing on Game cameras only, under the IMGUI HUD and eyelids, on unscaled time.
    /// Lives on GameSystems; disable the component to turn it off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FilmGrain : MonoBehaviour
    {
        const string ShaderName = "Hidden/Blink/FilmGrain";

        [SerializeField] Shader shader;
        [Tooltip("Grain strength. 0.02 shifts a typical pixel by under 1% brightness.")]
        [SerializeField, Range(0f, 0.15f)] float amount = 0.02f;
        [Tooltip("How many times a second the grain pattern changes.")]
        [SerializeField, Range(1f, 60f)] float framesPerSecond = 24f;
        [SerializeField] bool showInSceneView;

        static readonly int TimeId = Shader.PropertyToID("_GrainTime");
        static readonly int AmountId = Shader.PropertyToID("_GrainAmount");
        static readonly int FpsId = Shader.PropertyToID("_GrainFps");

        Material _material;
        GrainPass _pass;

        void OnEnable()
        {
            if (shader == null) shader = Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning($"[FilmGrain] Shader '{ShaderName}' missing or unsupported; grain is off.");
                enabled = false;
                return;
            }
            _material ??= CoreUtils.CreateEngineMaterial(shader);
            _pass ??= new GrainPass();
            _pass.Material = _material;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

        void OnDestroy() => CoreUtils.Destroy(_material);

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            bool wanted = cam.cameraType == CameraType.Game
                       || (showInSceneView && cam.cameraType == CameraType.SceneView);
            if (!wanted || _material == null || amount <= 0f) return;

            var data = cam.GetUniversalAdditionalCameraData();
            // Overlay cameras draw into their base camera's target, which already gets the pass.
            if (data == null || data.renderType != CameraRenderType.Base || data.scriptableRenderer == null) return;

            _material.SetFloat(TimeId, Time.unscaledTime % 3600f); // keep float precision in long sessions
            _material.SetFloat(AmountId, amount);
            _material.SetFloat(FpsId, framesPerSecond);
            data.scriptableRenderer.EnqueuePass(_pass);
        }

        sealed class GrainPass : ScriptableRenderPass
        {
            public Material Material;

            public GrainPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
                profilingSampler = new ProfilingSampler("Film Grain");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (Material == null || resources.isActiveTargetBackBuffer) return;

                TextureHandle source = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "_FilmGrainColor";
                desc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(desc);

                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, Material, 0), "Film Grain");
                resources.cameraColor = destination;
            }
        }
    }
}
