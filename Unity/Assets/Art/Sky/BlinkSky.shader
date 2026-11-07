// Procedural skybox for BLINK: horizon/zenith gradient, sun disc + glow, mottled moon disc + halo,
// hashed stars. Every property is driven at runtime by Blink.DayNight.DayNightCycle.
// Plain unlit CG, so it renders as a skybox under URP (same as the built-in Skybox/Procedural).
Shader "Blink/Sky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.01, 0.015, 0.035, 1)
        _HorizonColor ("Horizon", Color) = (0.05, 0.065, 0.1, 1)
        _GroundColor ("Ground", Color) = (0.02, 0.02, 0.025, 1)
        _SunDir ("Sun Direction (towards sun)", Vector) = (0, -1, 0, 0)
        _SunColor ("Sun Colour (alpha = strength)", Color) = (1, 0.9, 0.75, 1)
        _SunSize ("Sun Size (radians)", Range(0.005, 0.2)) = 0.03
        _MoonDir ("Moon Direction (towards moon)", Vector) = (0, 0.5, 0.85, 0)
        _MoonColor ("Moon Colour (alpha = strength)", Color) = (0.85, 0.9, 1, 1)
        _MoonSize ("Moon Size (radians)", Range(0.005, 0.2)) = 0.035
        _StarIntensity ("Stars", Range(0, 3)) = 1
        _StarDensity ("Star Density", Range(0, 0.2)) = 0.06
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _ZenithColor, _HorizonColor, _GroundColor, _SunColor, _MoonColor;
            float4 _SunDir, _MoonDir;
            float _SunSize, _MoonSize, _StarIntensity, _StarDensity;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash(i), hash(i + float3(1,0,0)), f.x),
                                 lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), f.x),
                                 lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            // 1 inside a disc of angular radius `size` around `towards`, soft edge.
            float disc(float3 d, float3 towards, float size)
            {
                float a = acos(clamp(dot(d, towards), -1.0, 1.0));
                return 1.0 - smoothstep(size * 0.85, size, a);
            }

            half4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float above = saturate(h * 30.0 + 0.5); // hide discs/stars under the horizon

                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, sqrt(saturate(h)));
                col = lerp(col, _GroundColor.rgb, saturate(-h * 6.0));

                // Sun: glow + disc.
                float3 sunDir = normalize(_SunDir.xyz);
                float sd = saturate(dot(d, sunDir));
                col += _SunColor.rgb * _SunColor.a * (pow(sd, 8.0) * 0.25 + pow(sd, 200.0) * 0.6);
                col += _SunColor.rgb * _SunColor.a * disc(d, sunDir, _SunSize) * 3.0 * above;

                // Stars (behind the moon).
                float3 sp = d * 220.0;
                float3 cell = floor(sp);
                float r = hash(cell);
                float3 jitter = float3(hash(cell + 11.1), hash(cell + 23.7), hash(cell + 37.3)) - 0.5;
                float dist = length(frac(sp) - 0.5 - jitter * 0.5);
                float star = smoothstep(0.22, 0.0, dist) * step(1.0 - _StarDensity, r);
                float twinkle = 0.65 + 0.35 * sin(_Time.y * (2.0 + r * 5.0) + r * 60.0);
                col += star * twinkle * (0.4 + 0.6 * hash(cell + 5.3)) * _StarIntensity * saturate(h * 5.0);

                // Moon: halo + mottled disc that covers the stars behind it.
                float3 moonDir = normalize(_MoonDir.xyz);
                float md = saturate(dot(d, moonDir));
                float moonA = _MoonColor.a;
                col += _MoonColor.rgb * moonA * (pow(md, 400.0) * 0.35 + pow(md, 30.0) * 0.05) * above;
                float m = disc(d, moonDir, _MoonSize) * above * moonA;
                float mottling = 0.72 + 0.28 * noise(d * 90.0) - 0.15 * noise(d * 25.0 + 4.0);
                col = lerp(col, _MoonColor.rgb * mottling * 1.6, m);

                return half4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
