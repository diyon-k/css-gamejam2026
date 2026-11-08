// What the player is looking at (InteractHighlight): a warm outline (inverted hull, pushed out along the normals) and a
// soft additive rim on the surface. Drawn on an overlay copy of the target's mesh. Values are small because the room
// post volumes expose the frame up 3-4 stops. _Pulse is driven by InteractHighlight (0.6..1).
Shader "Blink/InteractHighlight"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.85, 0.6, 1)
        _Outline ("Outline width (m)", Float) = 0.004
        _OutlineAlpha ("Outline opacity", Range(0, 1)) = 0.55
        _OutlineBrightness ("Outline brightness", Float) = 0.35
        _Rim ("Rim strength", Float) = 0.12
        _RimPower ("Rim power", Float) = 2.5
        _Fill ("Surface fill", Float) = 0.015
        _Pulse ("Pulse", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Outline;
            half _OutlineAlpha, _OutlineBrightness, _Rim, _RimPower, _Fill, _Pulse;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        ENDHLSL

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 nWS = normalize(TransformObjectToWorldNormal(i.normalOS));
                o.positionCS = TransformWorldToHClip(posWS + nWS * _Outline);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(_Color.rgb * _OutlineBrightness, _OutlineAlpha * _Pulse);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Rim"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half fresnel = pow(1.0h - saturate(dot(normalize(i.normalWS), normalize(i.viewWS))), _RimPower);
                return half4(_Color.rgb * (fresnel * _Rim + _Fill) * _Pulse, 1);
            }
            ENDHLSL
        }
    }
}
