// World-space triplanar PBR for the house blockout (scaled cubes have no usable UVs).
//   Sides -> _Side (interior) or _Facade (vertex colour R = 1 marks an exterior-facing face)
//   +Y    -> _Top (floors), -Y -> _Bottom (ceilings)
// Decay on top: large-scale grime, damp streaks rising from the floor (vertex colour B),
// moss from _MossAmount + vertex colour G + global roof holes (_OgHoles, set by Blink.Overgrowth.OvergrowthGlobals).
Shader "Blink/Overgrowth/Surface"
{
    Properties
    {
        [NoScaleOffset] _SideMap ("Side Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _SideNormal ("Side Normal", 2D) = "bump" {}
        [NoScaleOffset] _SideArm ("Side ARM (R AO, G rough, B metal)", 2D) = "white" {}
        _SideTint ("Side Tint", Color) = (1,1,1,1)
        _SideScale ("Side metres per tile", Float) = 2

        [NoScaleOffset] _FacadeMap ("Facade Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _FacadeNormal ("Facade Normal", 2D) = "bump" {}
        [NoScaleOffset] _FacadeArm ("Facade ARM (R AO, G rough, B metal)", 2D) = "white" {}
        _FacadeTint ("Facade Tint", Color) = (1,1,1,1)
        _FacadeScale ("Facade metres per tile", Float) = 2.5

        [NoScaleOffset] _TopMap ("Top Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _TopNormal ("Top Normal", 2D) = "bump" {}
        [NoScaleOffset] _TopArm ("Top ARM (R AO, G rough, B metal)", 2D) = "white" {}
        _TopTint ("Top Tint", Color) = (1,1,1,1)
        _TopScale ("Top metres per tile", Float) = 2

        [NoScaleOffset] _BottomMap ("Bottom Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _BottomNormal ("Bottom Normal", 2D) = "bump" {}
        [NoScaleOffset] _BottomArm ("Bottom ARM (R AO, G rough, B metal)", 2D) = "white" {}
        _BottomTint ("Bottom Tint", Color) = (1,1,1,1)
        _BottomScale ("Bottom metres per tile", Float) = 2

        [NoScaleOffset] _MossMap ("Moss Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _MossNormal ("Moss Normal", 2D) = "bump" {}
        [NoScaleOffset] _MossArm ("Moss ARM (R AO, G rough, B metal)", 2D) = "white" {}
        _MossTint ("Moss Tint", Color) = (0.8,0.85,0.7,1)
        _MossScale ("Moss metres per tile", Float) = 1.5
        _MossAmount ("Moss base amount", Range(0,1)) = 0.1
        _MossUp ("Moss on up-facing", Range(0,1)) = 0.2

        [NoScaleOffset] _NoiseMap ("Noise (R low, G mid, B high, A streaks)", 2D) = "gray" {}
        _Grime ("Grime", Range(0,1)) = 0.45
        _Damp ("Damp", Range(0,1)) = 0.6
        _SmoothScale ("Smoothness scale (x (1 - rough))", Range(0,2)) = 1
        _AOStrength ("AO strength", Range(0,1)) = 1
        _NormalStrength ("Normal Strength", Range(0,2)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _SideTint, _FacadeTint, _TopTint, _BottomTint, _MossTint;
            float _SideScale, _FacadeScale, _TopScale, _BottomScale, _MossScale;
            half _MossAmount, _MossUp, _Grime, _Damp, _SmoothScale, _AOStrength, _NormalStrength;
        CBUFFER_END

        // Globals (not per material): roof holes as xyz centre + w radius.
        float4 _OgHoles[16];
        float _OgHoleCount;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            half4  color      : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

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

            TEXTURE2D(_SideMap);   SAMPLER(sampler_SideMap);
            TEXTURE2D(_SideNormal);   TEXTURE2D(_SideArm);
            TEXTURE2D(_FacadeMap); TEXTURE2D(_FacadeNormal); TEXTURE2D(_FacadeArm);
            TEXTURE2D(_TopMap);    TEXTURE2D(_TopNormal);    TEXTURE2D(_TopArm);
            TEXTURE2D(_BottomMap); TEXTURE2D(_BottomNormal); TEXTURE2D(_BottomArm);
            TEXTURE2D(_MossMap);   TEXTURE2D(_MossNormal);   TEXTURE2D(_MossArm);
            TEXTURE2D(_NoiseMap);  SAMPLER(sampler_NoiseMap);

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                half4  color       : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                half   fogFactor   : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.shadowCoord = GetShadowCoord(p);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // One planar sample (albedo, tangent normal, ARM) with explicit gradients, safe inside branches.
            void Planar(TEXTURE2D_PARAM(albedoTex, smp), TEXTURE2D(normalTex), TEXTURE2D(armTex), float2 uv, float2 dx, float2 dy,
                        out half3 albedo, out half3 tn, out half3 arm)
            {
                albedo = SAMPLE_TEXTURE2D_GRAD(albedoTex, smp, uv, dx, dy).rgb;
                tn = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(normalTex, smp, uv, dx, dy));
                arm = SAMPLE_TEXTURE2D_GRAD(armTex, smp, uv, dx, dy).rgb;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 P = i.positionWS;
                float3 N = normalize(i.normalWS);
                half ext = i.color.r, mossV = i.color.g, damp = i.color.b;

                float3 w = pow(abs(N), 6);
                w /= (w.x + w.y + w.z);
                float3 s = step(0, N) * 2 - 1;

                // Projections (u mirrored on negative faces so textures read the same way round).
                float2 uvX = float2(P.z * s.x, P.y);
                float2 uvY = float2(P.x * s.y, P.z);
                float2 uvZ = float2(-P.x * s.z, P.y);
                float2 dXx = ddx(uvX), dXy = ddy(uvX), dYx = ddx(uvY), dYy = ddy(uvY), dZx = ddx(uvZ), dZy = ddy(uvZ);

                half3 aX = 0, aY = 0, aZ = 0, nX = half3(0,0,1), nY = half3(0,0,1), nZ = half3(0,0,1), mX = 1, mY = 1, mZ = 1;
                bool facade = ext > 0.5;
                float sideScale = facade ? _FacadeScale : _SideScale;
                half3 sideTint = facade ? _FacadeTint.rgb : _SideTint.rgb;
                if (w.x > 0.01)
                {
                    float k = 1.0 / sideScale;
                    if (facade) Planar(TEXTURE2D_ARGS(_FacadeMap, sampler_SideMap), _FacadeNormal, _FacadeArm, uvX * k, dXx * k, dXy * k, aX, nX, mX);
                    else        Planar(TEXTURE2D_ARGS(_SideMap, sampler_SideMap), _SideNormal, _SideArm, uvX * k, dXx * k, dXy * k, aX, nX, mX);
                    aX *= sideTint;
                }
                if (w.z > 0.01)
                {
                    float k = 1.0 / sideScale;
                    if (facade) Planar(TEXTURE2D_ARGS(_FacadeMap, sampler_SideMap), _FacadeNormal, _FacadeArm, uvZ * k, dZx * k, dZy * k, aZ, nZ, mZ);
                    else        Planar(TEXTURE2D_ARGS(_SideMap, sampler_SideMap), _SideNormal, _SideArm, uvZ * k, dZx * k, dZy * k, aZ, nZ, mZ);
                    aZ *= sideTint;
                }
                if (w.y > 0.01)
                {
                    if (N.y >= 0)
                    {
                        float k = 1.0 / _TopScale;
                        Planar(TEXTURE2D_ARGS(_TopMap, sampler_SideMap), _TopNormal, _TopArm, uvY * k, dYx * k, dYy * k, aY, nY, mY);
                        aY *= _TopTint.rgb;
                    }
                    else
                    {
                        float k = 1.0 / _BottomScale;
                        Planar(TEXTURE2D_ARGS(_BottomMap, sampler_SideMap), _BottomNormal, _BottomArm, uvY * k, dYx * k, dYy * k, aY, nY, mY);
                        aY *= _BottomTint.rgb;
                    }
                }

                half3 albedo = aX * w.x + aY * w.y + aZ * w.z;
                half3 arm = mX * w.x + mY * w.y + mZ * w.z; // R AO, G roughness, B metal

                // Whiteout-blended triplanar normals (Ben Golus).
                nX.xy *= _NormalStrength; nY.xy *= _NormalStrength; nZ.xy *= _NormalStrength;
                nX.x *= s.x; nY.x *= s.y; nZ.x *= -s.z;
                half3 tX = half3(nX.xy + N.zy, abs(nX.z) * N.x);
                half3 tY = half3(nY.xy + N.xz, abs(nY.z) * N.y);
                half3 tZ = half3(nZ.xy + N.xy, abs(nZ.z) * N.z);
                half3 nrm = normalize(tX.zyx * w.x + tY.xzy * w.y + tZ.xyz * w.z);

                // ---- decay masks from the dominant projection
                float2 uvD = w.y > max(w.x, w.z) ? P.xz : (w.x > w.z ? P.zy : P.xy);
                half nLow  = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, uvD * 0.045).r;
                half nMid  = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, uvD * 0.17).g;
                half nHigh = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, uvD * 0.6).b;
                half nStreak = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, uvD * float2(0.5, 0.08)).a;

                // Roof holes: moss and wet below/around them.
                half hole = 0;
                // Compile-time bound: an unset/garbage global must never become an unbounded GPU loop.
                int count = (int)clamp(_OgHoleCount, 0.0, 16.0);
                for (int h = 0; h < 16; h++)
                {
                    if (h >= count) break;
                    float3 d = P - _OgHoles[h].xyz;
                    d.y = d.y > 0 ? d.y * 2.0 : d.y * 0.35; // reaches further down than up
                    hole = max(hole, saturate(1.0 - length(d) / _OgHoles[h].w));
                }

                // Grime: big soft blotches, darker into corners of the noise.
                albedo *= lerp(1.0 - _Grime, 1.0, smoothstep(0.2, 0.8, nLow));
                albedo *= lerp(0.85, 1.05, nHigh);

                // Damp: streaks rising from the floor (B is 1 at a wall's base, 0 at its top).
                half wet = saturate((damp * _Damp * 1.6 + hole * 0.8) - (1.0 - nStreak) * 0.9 - (1.0 - nMid) * 0.3);
                wet = smoothstep(0.0, 0.35, wet);
                albedo = lerp(albedo, albedo * half3(0.42, 0.42, 0.36), wet);

                // Moss.
                half up = saturate(N.y);
                half m = saturate(_MossAmount + mossV + hole * 1.1 + up * _MossUp + ext * damp * 0.35);
                half mossMask = smoothstep(1.0 - m, 1.0 - m + 0.18, nMid * 0.65 + nHigh * 0.35);
                if (mossMask > 0.001)
                {
                    float k = 1.0 / _MossScale;
                    half3 ma = SAMPLE_TEXTURE2D(_MossMap, sampler_SideMap, uvD * k).rgb * _MossTint.rgb;
                    half3 mn = UnpackNormal(SAMPLE_TEXTURE2D(_MossNormal, sampler_SideMap, uvD * k));
                    half3 marm = SAMPLE_TEXTURE2D(_MossArm, sampler_SideMap, uvD * k).rgb;
                    albedo = lerp(albedo, ma, mossMask);
                    arm = lerp(arm, marm, mossMask);
                    nrm = normalize(lerp(nrm, normalize(N + half3(mn.x, 0, mn.y) * 0.5), mossMask * 0.8));
                }

                InputData inputData = (InputData)0;
                inputData.positionWS = P;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = nrm;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(P);
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    inputData.shadowCoord = i.shadowCoord;
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(P, 1.0), i.fogFactor);
                inputData.bakedGI = SampleSH(nrm);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo = albedo;
                surf.metallic = arm.b;
                surf.smoothness = saturate((1.0 - arm.g) * _SmoothScale * lerp(0.8, 1.2, nHigh) + wet * 0.45);
                surf.normalTS = half3(0, 0, 1);
                surf.occlusion = lerp(1.0, arm.r, _AOStrength) * lerp(1.0, 0.75, (1.0 - nLow) * _Grime);
                surf.alpha = 1;

                half4 col = UniversalFragmentPBR(inputData, surf);
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
            #pragma fragment NullFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 ShadowVert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pws = TransformObjectToWorld(v.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 L = normalize(_LightPosition - pws);
                #else
                    float3 L = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(pws, nws, L)));
            }
            half4 NullFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment NullFrag
            #pragma multi_compile_instancing
            float4 DepthVert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformObjectToHClip(v.positionOS.xyz);
            }
            half4 NullFrag() : SV_Target { return 0; }
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
            struct DNVaryings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            DNVaryings DNVert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                DNVaryings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 DNFrag(DNVaryings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    Fallback Off
}
