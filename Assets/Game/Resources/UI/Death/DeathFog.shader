// Могильная дымка экрана смерти (DeathScreen): два слоя текстуры дыма (DeathFog.png) плывут навстречу,
// видны только по углам; _Amount 0..1 -- насколько дымка наползла к центру. _T -- время (unscaled, из скрипта).
Shader "BeerAndDragon/UI/DeathFog"
{
    Properties
    {
        [PerRendererData] _MainTex ("Fog", 2D) = "white" {}
        _Color ("Tint", Color) = (0.62, 0.64, 0.68, 1)
        _Amount ("Amount", Range(0, 1)) = 0
        _T ("Time", Float) = 0
        _Aspect ("Screen aspect", Float) = 1.7778
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

            sampler2D _MainTex;
            fixed4 _Color;
            float _Amount, _T, _Aspect;

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
                float2 a = float2(_Aspect, 1.0);
                float n1 = tex2D(_MainTex, i.uv * a * 0.45 + float2(_T * 0.021, _T * 0.009)).r;
                float n2 = tex2D(_MainTex, i.uv * a * 0.8 + float2(-_T * 0.013, _T * 0.017) + 0.37).r;
                float n = n1 * 0.6 + n2 * 0.4;

                // 0 -- центр экрана, 1 -- угол; край дымки рвёт сам дым
                float r = length(abs(i.uv * 2.0 - 1.0)) * 0.7071 + (n - 0.5) * 0.35;
                // середина края экрана -- r = 0.71: туда дымка лишь заглядывает, гуще всего в углах
                float lo = lerp(1.2, 0.6, _Amount) + sin(_T * 0.7) * 0.02;   // чуть «дышит»
                float m = smoothstep(lo, lo + 0.4, r);

                fixed4 c = i.color;
                c.rgb *= 0.65 + n * 0.6;
                c.a *= saturate(m * (0.35 + n * 0.9));
                return c;
            }
            ENDCG
        }
    }
}
