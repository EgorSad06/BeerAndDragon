// Retro PSX & CRT FX LITE - REEBOO Labs. Unlit PSX Lite shader (URP).
Shader "REEBOO/Retro FX Lite/PSX Unlit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _VertexSnap ("Vertex Snapping", Range(0, 1)) = 1
        _SnapResolution ("Snap Resolution", Float) = 240
        _Affine ("Affine Texture Mapping", Range(0, 1)) = 1
        _Dither ("Dither", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardPSXLite"
            Tags { "LightMode" = "UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PsxlVertex
            #pragma fragment PsxlFragment
            #pragma multi_compile_instancing

            #include "PsxLite.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PsxlVertex
            #pragma fragment PsxlDepthFragment
            #pragma multi_compile_instancing
            #include "PsxLite.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
