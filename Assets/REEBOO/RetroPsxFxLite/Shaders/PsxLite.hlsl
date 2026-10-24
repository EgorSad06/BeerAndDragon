// Retro PSX & CRT FX LITE - REEBOO Labs. PSX Lite surface: vertex snapping, affine mapping, dither (15-bit colour).
// Lit = per-vertex main light + ambient. Full version adds: additional lights, shadows, fog, draw distance, colour depth,
// point-filter look, emission, surface types, scene-wide settings and a material converter.
#ifndef REEBOO_RFXL_PSX_INCLUDED
#define REEBOO_RFXL_PSX_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#if defined(PSXL_LIT)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _SnapResolution;
    half _VertexSnap;
    half _Affine;
    half _Dither;
CBUFFER_END

static const float kPsxlBayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

float4 PsxlSnap(float4 positionCS)
{
    float res = max(_SnapResolution, 16.0);
    if (_VertexSnap > 0.0 && positionCS.w > 0.0)
    {
        float2 grid = float2(res * _ScreenParams.x / max(_ScreenParams.y, 1.0), res) * 0.5;
        float2 ndc = positionCS.xy / positionCS.w;
        positionCS.xy = lerp(ndc, round(ndc * grid) / grid, _VertexSnap) * positionCS.w;
    }
    return positionCS;
}

struct PsxlAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct PsxlVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    float3 uvAffine   : TEXCOORD1;
    half3  light      : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

PsxlVaryings PsxlVertex(PsxlAttributes input)
{
    PsxlVaryings o = (PsxlVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float4 positionCS = PsxlSnap(TransformWorldToHClip(positionWS));
    o.positionCS = positionCS;
    o.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    o.uvAffine = float3(o.uv * positionCS.w, positionCS.w);
#if defined(PSXL_LIT)
    half3 n = (half3)TransformObjectToWorldNormal(input.normalOS);
    Light l = GetMainLight();
    o.light = l.color * (l.distanceAttenuation * saturate(dot(n, l.direction))) + max(half3(0, 0, 0), EvaluateAmbientProbeSRGB(n));
#else
    o.light = 1;
#endif
    return o;
}

half4 PsxlFragment(PsxlVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float2 uv = lerp(input.uv, input.uvAffine.xy / max(input.uvAffine.z, 1e-5), _Affine);
    half4 albedo = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv, ddx(input.uv), ddy(input.uv)) * _BaseColor;
    half3 c = albedo.rgb * input.light;
#if !defined(UNITY_COLORSPACE_GAMMA)
    c = LinearToSRGB(saturate(c));
#endif
    int2 q = int2(fmod(floor(input.positionCS.xy / max(1.0, floor(_ScreenParams.y / max(_SnapResolution, 16.0) + 0.5))), 4.0));
    float t = ((kPsxlBayer[q.y * 4 + q.x] + 0.5) / 16.0 - 0.5) * _Dither;
    c = floor(saturate(c) * 31.0 + 0.5 + t) / 31.0;
#if !defined(UNITY_COLORSPACE_GAMMA)
    c = SRGBToLinear(saturate(c));
#endif
    return half4(c, 1.0);
}

half PsxlDepthFragment(PsxlVaryings input) : SV_Target
{
    return input.positionCS.z;
}

#endif
