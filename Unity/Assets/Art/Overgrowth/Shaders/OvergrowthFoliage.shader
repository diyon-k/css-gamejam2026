// Alpha-cutout, two-sided foliage (ivy, vines, grass, leaf cards, pine branches, treeline cards, cobwebs).
// Wind sway in the vertex shader, weighted by vertex colour A (0 = anchored, 1 = free tip).
// _Time is scaled time, so the sway speeds up with BLINK's fast-forward like any world thing.
// _FogScale < 1 lets far treeline cards keep a silhouette inside dense fog.
Shader "Blink/Overgrowth/Foliage"
{
    Properties
    {
        _BaseMap ("Albedo (A = cutout)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [NoScaleOffset][Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0.15
        _BaseAO ("AO at the anchored end (vertex A = 0)", Range(0,1)) = 0.55
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _WindStrength ("Wind Strength (m)", Float) = 0.05
        _WindSpeed ("Wind Speed", Float) = 1.2
        _Translucency ("Back-light Translucency", Range(0,1)) = 0.35
        _HueVariation ("Tint Variation", Range(0,1)) = 0.25
        _NormalUp ("Bend normals up", Range(0,1)) = 0.3
        _FogScale ("Fog Scale", Range(0,1)) = 1
        _Unlit ("Unlit (far silhouettes)", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff, _Translucency, _HueVariation, _NormalUp, _FogScale, _Unlit, _BumpScale, _Smoothness, _BaseAO;
            float _WindStrength, _WindSpeed;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float4 tangentOS  : TANGENT;
            float2 uv         : TEXCOORD0;
            half4  color      : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float3 WindWS(float3 pws, half weight)
        {
            float3 origin = TransformObjectToWorld(float3(0, 0, 0));
            float phase = dot(pws, float3(0.37, 0.11, 0.23)) + dot(origin, float3(1.7, 0, 2.3));
            float t = _Time.y * _WindSpeed;
            float3 sway = float3(sin(t + phase), 0, cos(t * 0.8 + phase * 1.3)) + 0.35 * float3(sin(t * 2.7 + phase * 3.1), 0, sin(t * 2.3 + phase * 2.1));
            return pws + sway * (_WindStrength * weight);
        }

        half CutAlpha(float2 uv)
        {
            half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
            clip(a - _Cutoff);
            return a;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            AlphaToMask On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                half   fogFactor   : TEXCOORD3;
                half   tint        : TEXCOORD4;
                float4 tangentWS   : TEXCOORD5;
                half   ao          : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 pws = WindWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                o.positionWS = pws;
                o.positionCS = TransformWorldToHClip(pws);
                VertexNormalInputs vn = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.normalWS = vn.normalWS;
                o.tangentWS = float4(vn.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.ao = lerp(_BaseAO, 1.0, v.color.a);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                float3 origin = TransformObjectToWorld(float3(0, 0, 0)) + pws * 0.15;
                o.tint = frac(sin(dot(floor(origin * 2), float3(12.9898, 78.233, 37.719))) * 43758.5453);
                return o;
            }

            half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half alpha = tex.a * _BaseColor.a;
                clip(alpha - _Cutoff);

                half3 albedo = tex.rgb * _BaseColor.rgb;
                albedo *= lerp(1.0 - _HueVariation, 1.0 + _HueVariation * 0.5, i.tint);
                albedo = lerp(albedo, albedo * half3(1.15, 1.0, 0.7), _HueVariation * (1 - i.tint) * 0.6); // some yellowing

                float flip = IS_FRONT_VFACE(face, 1, -1);
                float3 ng = normalize(i.normalWS) * flip;
                float3 t = normalize(i.tangentWS.xyz);
                float3 bt = cross(normalize(i.normalWS), t) * i.tangentWS.w;
                half3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, i.uv), _BumpScale);
                float3 n = normalize(t * tn.x + bt * tn.y + ng * tn.z);
                n = normalize(lerp(n, float3(0, 1, 0), _NormalUp));

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogFactor);
                #if defined(FOG_LINEAR)
                    inputData.fogCoord = lerp(1.0, inputData.fogCoord, _FogScale); // linear: 1 = no fog
                #else
                    inputData.fogCoord *= _FogScale; // exp/exp2: factor scales with distance
                #endif
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo = albedo;
                surf.smoothness = _Smoothness;
                surf.normalTS = half3(0, 0, 1);
                surf.occlusion = i.ao;
                surf.alpha = 1;

                // Light coming through the leaves from behind.
                Light ml = GetMainLight(inputData.shadowCoord);
                half back = saturate(dot(-n, ml.direction)) * 0.6 + saturate(dot(inputData.viewDirectionWS, -ml.direction)) * 0.4;
                surf.emission = albedo * ml.color * ml.shadowAttenuation * back * _Translucency;

                half4 col = UniversalFragmentPBR(inputData, surf);
                col.rgb = lerp(col.rgb, albedo * (inputData.bakedGI + 0.02), _Unlit);
                col.rgb = MixFog(col.rgb, inputData.fogCoord);
                return half4(col.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct SV { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            SV ShadowVert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                SV o;
                float3 pws = WindWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 L = normalize(_LightPosition - pws);
                #else
                    float3 L = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(pws, nws, L)));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 ShadowFrag(SV i) : SV_Target { CutAlpha(i.uv); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DVert
            #pragma fragment DFrag
            #pragma multi_compile_instancing
            struct DV { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            DV DVert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                DV o;
                o.positionCS = TransformWorldToHClip(WindWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 DFrag(DV i) : SV_Target { CutAlpha(i.uv); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_instancing
            struct DNV { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            DNV DNVert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                DNV o;
                o.positionCS = TransformWorldToHClip(WindWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 DNFrag(DNV i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                CutAlpha(i.uv);
                return half4(normalize(i.normalWS) * IS_FRONT_VFACE(face, 1, -1), 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
