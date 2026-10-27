// Карта таро на экране смерти (DeathScreen): проявляется из дымки сверху вниз.
// _Reveal 0..1: сначала проступает верх, середина полупрозрачная; потом середина, а низ ещё в дымке; потом вся карта.
// _Soft -- ширина полупрозрачной полосы (доля высоты карты). Край полосы рвёт текстура дыма _FogTex.
Shader "BeerAndDragon/UI/DeathCard"
{
    Properties
    {
        [PerRendererData] _MainTex ("Card", 2D) = "white" {}
        _Color ("Tint", Color) = (0.86, 0.83, 0.75, 1)
        _FogTex ("Fog", 2D) = "gray" {}
        _FogColor ("Fog color", Color) = (0.62, 0.64, 0.68, 1)
        _Reveal ("Reveal", Range(0, 1)) = 0
        _Soft ("Soft band", Range(0.05, 1)) = 0.5
        _T ("Time", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex, _FogTex;
            fixed4 _Color, _FogColor;
            float _Reveal, _Soft, _T;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                float n = tex2D(_FogTex, i.uv * float2(0.7, 1.2) + float2(_T * 0.02, -_T * 0.035)).r;

                float y = 1.0 - i.uv.y;                                  // 0 -- верх карты, 1 -- низ
                float front = _Reveal * (1.0 + _Soft + 0.3) - 0.15;      // при 0 карты нет, при 1 -- видна вся
                float a = saturate((front - y + (n - 0.5) * 0.25) / _Soft);

                // недопроявленная часть ещё в дыму
                c.rgb = lerp(_FogColor.rgb * (0.5 + n * 0.6), c.rgb, smoothstep(0.15, 1.0, a));
                c.a *= a;
                return c;
            }
            ENDCG
        }
    }
}
