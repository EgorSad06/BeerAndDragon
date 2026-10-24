// Цвет PS1 для RetroColorFeature: дымка по глубине (_RetroFogParams: начало, конец, плотность вдали, ступени;
// глубина берётся по сетке виртуального экрана .w строк -- края дымки совпадают с крупными пикселями),
// затем квантование до _RetroColorParams.x уровней на канал в sRGB и дизеринг Байера 4x4 силой .y клетками по .z px.
Shader "Hidden/BeerAndDragon/RetroColor"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off Blend Off
        Pass
        {
            Name "RetroColor"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _RetroColorParams; // levels, dither, cell size (px), virtual lines
            float4 _RetroFogParams;   // start, end, max density, steps
            float4 _RetroFogColor;    // rgb, a -- плотность на небе
            static const float kBayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).rgb;
                int2 q = int2(fmod(floor(input.positionCS.xy / _RetroColorParams.z), 4.0));
                float bayer = (kBayer[q.y * 4 + q.x] + 0.5) / 16.0 - 0.5;

                if (_RetroFogParams.z > 0.0)
                {
                    float2 cells = float2(floor(_RetroColorParams.w * _ScreenParams.x / _ScreenParams.y + 0.5), _RetroColorParams.w);
                    float2 uv = (floor(input.texcoord * cells) + 0.5) / cells;
                    float raw = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool sky = raw <= 0.0;
                #else
                    bool sky = raw >= 1.0;
                #endif
                    float dist = LinearEyeDepth(raw, _ZBufferParams);
                    float f = saturate((dist - _RetroFogParams.x) / (_RetroFogParams.y - _RetroFogParams.x)) * _RetroFogParams.z;
                    if (sky) f = _RetroFogColor.a;
                    if (_RetroFogParams.w > 0.5) f = saturate(floor(f * _RetroFogParams.w + 0.5 + bayer) / _RetroFogParams.w);
                    c = lerp(c, _RetroFogColor.rgb, f);
                }

            #if !defined(UNITY_COLORSPACE_GAMMA)
                c = LinearToSRGB(saturate(c));
            #endif
                if (_RetroColorParams.x > 1.0)
                {
                    float steps = _RetroColorParams.x - 1.0;
                    c = floor(saturate(c) * steps + 0.5 + bayer * _RetroColorParams.y) / steps;
                }
            #if !defined(UNITY_COLORSPACE_GAMMA)
                c = SRGBToLinear(saturate(c));
            #endif
                return float4(c, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
