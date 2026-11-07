// BLINK Bungalow kit wear (used by SG_BungalowLit.shadergraph through a Custom Function node, file mode).
// Generated alongside the graph by tools/textures/bungalow_shadergraph.py; edit both together.
//
// Inputs follow URP Lit: tiling scan on UV0 (Base / Normal / Mask share the Base Map tiling & offset),
// Mask = MetallicSmoothness packing (R metallic, G AO, A smoothness), and a shared kit wear atlas on "UV2" = Unity mesh.uv2
// = TEXCOORD1 (Shader Graph channel UV1): the same non-overlapping layout the lightmapper uses (tools/blender/bungalow_wear.py):
//   R = baked AO, G = edge wear, B = dirt (cavity x up-facing), A = polish (nosings, handrail tops, door edges).
// An unassigned texture slot falls back to Unity's 4x4 default texture; the graph detects that through the
// texel size and treats the slot as neutral (no mask / no wear), so kit materials work before the atlas exists.
#ifndef BLINK_BUNGALOW_WEAR_INCLUDED
#define BLINK_BUNGALOW_WEAR_INCLUDED

void BungalowWear_float(
    UnityTexture2D BaseMap, UnityTexture2D BumpMap, UnityTexture2D MaskMap, UnityTexture2D WearMap,
    float2 UV0, float2 UV2,
    float4 BaseColor, float BumpScale, float Metallic, float Smoothness, float OcclusionStrength,
    float WearAOStrength,
    float EdgeWearStrength, float4 EdgeWearColor, float EdgeWearLighten,
    float DirtStrength, float4 DirtColor, float DirtRoughen,
    float PolishStrength, float PolishSmoothness,
    out float3 Albedo, out float3 NormalTS, out float MetallicOut, out float SmoothnessOut, out float Occlusion)
{
    float2 uv = BaseMap.GetTransformedUV(UV0);

    float4 base = SAMPLE_TEXTURE2D(BaseMap.tex, BaseMap.samplerstate, uv) * BaseColor;
    float4 nrm = SAMPLE_TEXTURE2D(BumpMap.tex, BumpMap.samplerstate, uv);
    NormalTS = UnpackNormalScale(nrm, BumpScale);

    // Mask (MetallicSmoothness + AO in G). Unassigned -> metallic = Metallic, AO = 1, smoothness = Smoothness.
    float hasMask = step(8.0, MaskMap.texelSize.z);
    float4 mask = SAMPLE_TEXTURE2D(MaskMap.tex, MaskMap.samplerstate, uv);
    mask = lerp(float4(1.0, 1.0, 0.0, 1.0), mask, hasMask);
    float metallic = mask.r * Metallic;
    float smooth = mask.a * Smoothness;
    float ao = lerp(1.0, mask.g, OcclusionStrength);

    // Kit wear atlas on UV2. Unassigned -> neutral (AO 1, no edge wear, no dirt, no polish).
    float hasWear = step(8.0, WearMap.texelSize.z);
    float4 wear = SAMPLE_TEXTURE2D(WearMap.tex, WearMap.samplerstate, UV2);
    wear = lerp(float4(1.0, 0.0, 0.0, 0.0), wear, hasWear);

    float3 albedo = base.rgb;

    // Edge wear: rubbed paint / varnish. The base lightens and shifts towards the tint (alpha = tint amount).
    float e = saturate(wear.g * EdgeWearStrength);
    float3 exposed = lerp(saturate(albedo * (1.0 + EdgeWearLighten)), EdgeWearColor.rgb, EdgeWearColor.a);
    albedo = lerp(albedo, exposed, e);

    // Dirt: grime in cavities and on up-facing surfaces. Darkens (multiplies towards DirtColor) and roughens.
    float d = saturate(wear.b * DirtStrength);
    albedo *= lerp(float3(1.0, 1.0, 1.0), DirtColor.rgb, d);
    smooth *= lerp(1.0, 1.0 - DirtRoughen, d);

    // Polish: hand- and foot-worn areas get smoother (and a touch richer/darker).
    float p = saturate(wear.a * PolishStrength);
    smooth = lerp(smooth, max(smooth, PolishSmoothness), p);
    albedo *= lerp(1.0, 0.9, p);

    // Baked AO from the atlas multiplies into occlusion.
    ao *= lerp(1.0, wear.r, saturate(WearAOStrength));

    Albedo = albedo;
    MetallicOut = metallic;
    SmoothnessOut = saturate(smooth);
    Occlusion = ao;
}

// Half-precision variant so the node also compiles if the graph precision is ever switched to half.
void BungalowWear_half(
    UnityTexture2D BaseMap, UnityTexture2D BumpMap, UnityTexture2D MaskMap, UnityTexture2D WearMap,
    half2 UV0, half2 UV2,
    half4 BaseColor, half BumpScale, half Metallic, half Smoothness, half OcclusionStrength,
    half WearAOStrength,
    half EdgeWearStrength, half4 EdgeWearColor, half EdgeWearLighten,
    half DirtStrength, half4 DirtColor, half DirtRoughen,
    half PolishStrength, half PolishSmoothness,
    out half3 Albedo, out half3 NormalTS, out half MetallicOut, out half SmoothnessOut, out half Occlusion)
{
    float3 a, n; float m, s, o;
    BungalowWear_float(BaseMap, BumpMap, MaskMap, WearMap, UV0, UV2, BaseColor, BumpScale, Metallic, Smoothness,
        OcclusionStrength, WearAOStrength, EdgeWearStrength, EdgeWearColor, EdgeWearLighten, DirtStrength, DirtColor,
        DirtRoughen, PolishStrength, PolishSmoothness, a, n, m, s, o);
    Albedo = a; NormalTS = n; MetallicOut = m; SmoothnessOut = s; Occlusion = o;
}
#endif
