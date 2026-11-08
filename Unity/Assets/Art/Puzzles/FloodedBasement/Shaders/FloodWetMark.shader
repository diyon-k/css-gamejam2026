// Wet walls and floor after the flooded basement's water (FloodedBasement): darkens (multiplies) everything under the
// highest the water reached, with a darker tide line at the top, and dries off in patches as _Wet falls to 0.
// Drawn on thin strips just in front of the walls and a sheet just over the floor. _WetTop (world y) and _Wet (0..1)
// come from the script through a MaterialPropertyBlock.
Shader "Blink/FloodWetMark"
{
    Properties
    {
        _WetTint ("Wet tint (multiply)", Color) = (0.52, 0.5, 0.46, 1)
        _LineTint ("Tide line (multiply)", Color) = (0.42, 0.4, 0.35, 1)
        _WetTop ("High-water mark, world y (script)", Float) = -1000
        _Wet ("Wetness 0..1 (script)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WetMark"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Offset -1, -1
            Blend DstColor Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WetTint, _LineTint;
                float _WetTop, _Wet;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Wet <= 0.001) return half4(1, 1, 1, 1);
                float3 p = i.positionWS;
                // Walls and floor share one noise: xz on the floor, a wall's length and height on the wall.
                float2 q = p.xz + p.y * float2(0.71, 0.53);
                float n = Noise(q * 6.3) * 0.6 + Noise(q * 23 + 5) * 0.4;
                float damp = _Wet * lerp(0.8, 1.0, n);                              // a little uneven
                float below = saturate((_WetTop - p.y) / 0.02 + 0.6 * (n - 0.5));   // ragged top edge
                float tide = exp(-pow((p.y - _WetTop + 0.006 + 0.012 * (n - 0.5)) / 0.007, 2)) * sqrt(_Wet); // the stain lasts longest
                half3 c = lerp(half3(1, 1, 1), _WetTint.rgb, below * damp);
                c *= lerp(half3(1, 1, 1), _LineTint.rgb, saturate(tide));
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
