// Цвет PS1 для RetroColorFeature: квантование до _RetroColorParams.x уровней на канал в sRGB
// и дизеринг Байера 4x4 силой .y клетками по .z пикселей экрана.
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

            float4 _RetroColorParams; // levels, dither, cell size (px)
            static const float kBayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).rgb;
            #if !defined(UNITY_COLORSPACE_GAMMA)
                c = LinearToSRGB(saturate(c));
            #endif
                float steps = max(_RetroColorParams.x - 1.0, 1.0);
                int2 q = int2(fmod(floor(input.positionCS.xy / _RetroColorParams.z), 4.0));
                float t = ((kBayer[q.y * 4 + q.x] + 0.5) / 16.0 - 0.5) * _RetroColorParams.y;
                c = floor(saturate(c) * steps + 0.5 + t) / steps;
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
