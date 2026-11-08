// The flooded basement's water (FloodedBasement). A subdivided grid moved by the script: crossing ripples, a slosh and a
// ring wave after each surge, a funnel and a swirl of foam over the open drain, clearer water round the drain, colour
// that deepens with depth (scene depth), refraction of the floor (opaque texture), white foam where it meets walls and
// props, and puddles as the last few centimetres go. Lit by URP (main + Forward+ lights, probes, reflections).
// Both PC depth and opaque textures are on (PC_RPAsset); without them it falls back to flat murk.
// Per-renderer values come from FloodedBasement through a MaterialPropertyBlock: _Level, _DrainPos, _DrainFlow,
// _Funnel, _Chop, _WaveOrigin, _WaveTime, _WaveAmp, _Breakup. Their defaults suit a still surface (the bath).
Shader "Blink/FloodWater"
{
    Properties
    {
        [NoScaleOffset] _NormalMap ("Ripple normal (RGB)", 2D) = "bump" {}
        _NormalTiling ("Ripple tiling (per metre)", Float) = 0.6
        _NormalStrength ("Ripple strength", Range(0, 2)) = 0.45
        _ShallowColor ("Shallow tint", Color) = (0.92, 0.9, 0.78, 1)
        _DeepColor ("Deep colour (lit)", Color) = (0.05, 0.065, 0.035, 1)
        _Absorb ("Absorption per metre (RGB)", Vector) = (2.6, 2.1, 2.9, 0)
        _DrainClarity ("Murk at the drain (x)", Range(0.05, 1)) = 0.35
        _ClearRadius ("Clear radius round the drain (m)", Float) = 1.4
        _Refraction ("Refraction", Range(0, 0.05)) = 0.006
        _Reflection ("Screen reflection", Range(0, 1)) = 0.8
        _Smoothness ("Smoothness", Range(0, 1)) = 0.93
        _FoamColor ("Foam", Color) = (0.72, 0.7, 0.6, 1)
        _FoamDepth ("Edge foam depth (m)", Float) = 0.07
        _SwirlRadius ("Swirl radius (m)", Float) = 1.3

        _Level ("Level above the floor (script)", Float) = 1
        _DrainPos ("Drain (script)", Vector) = (0, -1000, 0, 0)
        _DrainFlow ("Drain flow 0..1 (script)", Float) = 0
        _Funnel ("Funnel depth (script)", Float) = 0
        _Chop ("Chop 0..1 (script)", Float) = 0
        _WaveOrigin ("Wave origin (script)", Vector) = (0, 0, 0, 0)
        _WaveTime ("Wave age, s (script)", Float) = 100
        _WaveAmp ("Wave height (script)", Float) = 0
        _Breakup ("Puddle breakup 0..1 (script)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off // the eyes end up just under it
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor;
                float4 _Absorb, _DrainPos, _WaveOrigin;
                float _NormalTiling, _NormalStrength, _DrainClarity, _ClearRadius, _Refraction, _Reflection, _Smoothness, _FoamDepth, _SwirlRadius;
                float _Level, _DrainFlow, _Funnel, _Chop, _WaveTime, _WaveAmp, _Breakup;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            // ------------------------------------------------------------ shape

            /// Height offset (metres) at world xz: chop, the surge's ring wave and the drain funnel.
            float Height(float2 xz)
            {
                float t = _Time.y;
                float h = _Chop * 0.018 * (sin(xz.x * 3.1 + t * 2.3) + sin(xz.y * 2.7 - t * 1.9) + 0.5 * sin((xz.x + xz.y) * 5.3 + t * 3.7));
                float r = distance(xz, _WaveOrigin.xz);
                float front = r - _WaveTime * 2.2;
                h += _WaveAmp * sin(front * 8.0) * exp(-front * front * 3.0) * exp(-_WaveTime * 0.9);
                float d = distance(xz, _DrainPos.xz);
                h -= _Funnel * exp(-d * d * 4.5);
                return h;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                const float e = 0.05;
                float hx = Height(p.xz + float2(e, 0)) - Height(p.xz - float2(e, 0));
                float hz = Height(p.xz + float2(0, e)) - Height(p.xz - float2(0, e));
                p.y += Height(p.xz);
                o.positionWS = p;
                o.normalWS = normalize(float3(-hx / (2 * e), 1, -hz / (2 * e)));
                o.positionCS = TransformWorldToHClip(p);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // ------------------------------------------------------------ surface

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            float Fbm(float2 p) { return Noise(p) * 0.6 + Noise(p * 2.3 + 17.0) * 0.3 + Noise(p * 5.1 + 3.0) * 0.1; }

            float2 Ripple(float2 uv, float lod)
            {
                float3 n = SAMPLE_TEXTURE2D_LOD(_NormalMap, sampler_NormalMap, uv, lod).rgb * 2 - 1;
                return n.xy;
            }

            float2 Ripple(float2 uv) { return SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv).rgb.xy * 2 - 1; }

            half4 PbrLit(float3 posWS, float3 n, float3 viewWS, float2 screenUV, float4 positionCS, half3 albedo, half smoothness)
            {
                InputData d = (InputData)0;
                d.positionWS = posWS;
                d.positionCS = positionCS;
                d.normalWS = n;
                d.viewDirectionWS = viewWS;
                d.shadowCoord = TransformWorldToShadowCoord(posWS);
                d.fogCoord = 0;
                d.bakedGI = SampleSH(n);
                d.normalizedScreenSpaceUV = screenUV;
                d.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.alpha = 1;
                s.metallic = 0;
                s.smoothness = smoothness;
                s.occlusion = 1;
                return UniversalFragmentPBR(d, s);
            }

            /// Cheap screen-space reflection: marches the reflected ray a few steps through the depth texture and returns
            /// the opaque colour where it passes behind something (a = found, faded towards the screen edges).
            half4 ScreenReflection(float3 posWS, float3 dir)
            {
                // Rays coming back towards the camera only find what's in front of them (false hits): skip them.
                if (dot(dir, -GetWorldSpaceNormalizeViewDir(posWS)) < 0) return 0;
                float3 p = posWS;
                float stepLen = 0.18;
                [unroll] for (int k = 0; k < 10; k++)
                {
                    p += dir * stepLen;
                    stepLen *= 1.35;
                    float4 cs = TransformWorldToHClip(p);
                    float4 sp = ComputeScreenPos(cs); // same convention as the surface's own screen UV
                    float2 uv = sp.xy / sp.w;
                    if (any(uv < 0) || any(uv > 1)) return 0;
                    float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    float ray = cs.w;
                    if (ray > scene && ray - scene < min(stepLen * 1.5, 0.35))
                    {
                        float2 e = min(uv, 1 - uv);
                        return half4(SampleSceneColor(uv), saturate(min(e.x, e.y) * 8));
                    }
                }
                return 0;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                bool above = IS_FRONT_VFACE(face, true, false);
                float t = _Time.y;
                float3 posWS = i.positionWS;
                float3 viewWS = SafeNormalize(GetWorldSpaceViewDir(posWS));
                float2 screenUV = i.screenPos.xy / i.screenPos.w;

                // Ripples: two layers crossing, rougher while the water's churned up.
                float2 uv = posWS.xz * _NormalTiling;
                float2 r1 = Ripple(uv + t * float2(0.031, 0.019));
                float2 r2 = Ripple(uv * 1.73 + float2(0.37, 0.61) + t * float2(-0.024, 0.033));
                float2 ripple = (r1 + r2) * _NormalStrength * (1 + _Chop * 2.5);

                // The drain: a log-polar swirl spiralling in, foam arms and a dark eye.
                float2 toDrain = posWS.xz - _DrainPos.xz;
                float dr = length(toDrain);
                float swirl = _DrainFlow * (1 - smoothstep(0.0, _SwirlRadius, dr));
                float theta = atan2(toDrain.y, toDrain.x);
                float foam = 0;
                if (swirl > 0.001)
                {
                    float2 polar = float2(theta * (3 / PI) + t * 1.4 / (dr + 0.3), log(dr + 0.05) * 1.2 + t * 0.9);
                    ripple = lerp(ripple, Ripple(polar, 0) * 1.6, swirl);
                    float arms = pow(saturate(0.5 + 0.5 * sin(4 * theta + 11 * sqrt(dr) + t * 7)), 4);
                    foam += arms * swirl * smoothstep(0.06, 0.35, dr) * 0.85;
                }

                float3 n = normalize(i.normalWS + float3(ripple.x, 0, ripple.y));
                if (!above) n = -n;

                // How much water the eye looks through to the floor (or whatever's in it).
                float surfEye = i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float thick = max(0, sceneEye - surfEye);

                float2 refrUV = screenUV + ripple * _Refraction * saturate(thick * 4);
                float refrEye = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams);
                if (refrEye < surfEye) refrUV = screenUV; // something in front of the water: don't drag it in
                else thick = max(0, refrEye - surfEye);
                half3 behind = SampleSceneColor(refrUV);

                // Murk: absorbed per metre, much clearer round the drain so you can see what's on it.
                float clear = 1 - smoothstep(_ClearRadius * 0.45, _ClearRadius, distance(posWS.xz, _DrainPos.xz));
                float murk = lerp(1, _DrainClarity, clear);
                float3 trans = exp(-thick * _Absorb.rgb * murk);
                float transAvg = dot(trans, 1.0 / 3);

                half4 white = PbrLit(posWS, n, viewWS, screenUV, i.positionCS, half3(1, 1, 1), 0);
                half4 spec = PbrLit(posWS, n, viewWS, screenUV, i.positionCS, half3(0, 0, 0), _Smoothness);

                // Foam where the water meets anything, broken up, heavier while it's churning.
                float edge = 1 - saturate(thick / max(0.001, _FoamDepth * (1 + _Chop)));
                float speckle = Fbm(posWS.xz * 9 + t * float2(0.3, -0.2) + ripple * 2);
                foam += smoothstep(0.45, 0.85, edge * (0.5 + speckle)) * 0.55;
                foam += _Chop * smoothstep(0.62, 0.8, speckle) * 0.4;
                foam = saturate(foam);

                half3 col = behind * trans * _ShallowColor.rgb + white.rgb * _DeepColor.rgb * (1 - transAvg);
                col *= lerp(1, 0.25, swirl * (1 - smoothstep(0.0, 0.22, dr))); // the eye of the whirlpool
                col += spec.rgb;
                // The lit room mirrored in it: what makes it read as water from eye height.
                float fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(n, viewWS)), 5);
                half4 refl = above ? ScreenReflection(posWS, reflect(-viewWS, n)) : 0;
                col = lerp(col, refl.rgb * 0.85, saturate(refl.a * _Reflection * fresnel * 1.5));
                col = lerp(col, white.rgb * _FoamColor.rgb, foam);

                // Soft where it touches things; puddles as the last of it goes (or first comes in).
                float alpha = saturate(thick / 0.012);
                float puddle = Fbm(posWS.xz * 1.6);
                float b = _Breakup * 1.2 - 0.1; // 0: all water, 1: none
                alpha *= smoothstep(b - 0.06, b + 0.06, puddle);

                if (!above)
                {
                    // From underneath: the bright skin of the surface through murk.
                    col = lerp(behind * _ShallowColor.rgb * 0.5, white.rgb * _DeepColor.rgb * 3, 0.6) + spec.rgb * 0.3;
                    alpha = 0.92;
                }

                col = MixFog(col, i.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
