// Retro PSX & CRT FX LITE - REEBOO Labs. Pixelation + scanlines + CRT curvature in one pass.
Shader "Hidden/REEBOO/RetroFxLite/Screen"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off Blend Off
        Pass
        {
            Name "RetroFxLite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _RFXL_Size;   // w, h, 1/w, intensity
            float4 _RFXL_Params; // pixelResolution, scanlines, scanlineCount, curvature

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv0 = input.texcoord;
                float3 original = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv0, 0).rgb;
                float2 res = _RFXL_Size.xy;
                float aspect = res.x / res.y;

                float curv = _RFXL_Params.w;
                float2 p = uv0 * 2.0 - 1.0;
                float2 q = p * (1.0 + curv * float2(p.y * p.y, p.x * p.x));
                float2 uv = q * 0.5 + 0.5;

                float3 col;
                float pixelRes = _RFXL_Params.x;
                if (pixelRes > 0.5)
                {
                    float2 cells = float2(floor(pixelRes * aspect + 0.5), pixelRes);
                    col = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, (floor(uv * cells) + 0.5) / cells, 0).rgb;
                }
                else col = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;

                float scan = _RFXL_Params.y;
                if (scan > 0.0)
                {
                    float count = min(_RFXL_Params.z, res.y * 0.5);
                    float band = 0.5 + 0.5 * cos(6.2831853 * (uv.y * count - 0.5));
                    float aa = saturate((res.y / count - 1.5) / 1.5);
                    col *= lerp(1.0, band, scan * aa) * (1.0 + scan * aa * 0.3);
                }

                float inside = 1.0;
                if (curv > 0.0)
                {
                    float corner = 0.03 + curv * 0.2;
                    float2 qa = abs(q) * float2(aspect, 1.0);
                    float2 d = qa - (float2(aspect, 1.0) - corner);
                    float dist = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - corner;
                    inside = 1.0 - smoothstep(-2.0 / res.y, 2.0 / res.y, dist);
                }
                return float4(lerp(original, col * inside, _RFXL_Size.w), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
