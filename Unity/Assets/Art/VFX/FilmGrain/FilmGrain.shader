// Very faint film grain over the whole frame. Applied after post-processing by FilmGrain.cs, which
// sets the properties below each frame (so the material needs no asset).
Shader "Hidden/Blink/FilmGrain"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "Grain"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _GrainTime;   // unscaled seconds
            float _GrainAmount; // grain amplitude in display space
            float _GrainFps;    // how often the grain pattern changes

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 col = max(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).rgb, 0.0);

                // Work in display space so the grain is equally faint in darks and lights.
                #if !defined(UNITY_COLORSPACE_GAMMA)
                col = LinearToSRGB(col);
                #endif

                float frame = floor(_GrainTime * _GrainFps);
                float2 seed = input.positionCS.xy + frac(frame * 0.6180339) * 1024.0;
                // Sum of two hashes: a softer, film-like (triangular) distribution instead of flat static.
                float n = Hash12(seed) + Hash12(seed + 71.3) - 1.0;
                col = max(col + n * _GrainAmount, 0.0);

                #if !defined(UNITY_COLORSPACE_GAMMA)
                col = SRGBToLinear(col);
                #endif
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
