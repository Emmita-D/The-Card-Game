Shader "Unlit/ProceduralGrid"
{
    Properties
    {
        _FillColor  ("Fill Color",  Color) = (0,0,0,0)
        _LineColor  ("Line Color",  Color) = (0.2,0.9,1,0.6)
        _Thickness  ("Line Thickness", Range(0.001, 0.08)) = 0.02
        _Tiling     ("Tiling (x=cols, y=rows)", Vector) = (8,6,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _FillColor;
            float4 _LineColor;
            float4 _Tiling;     // x=cols, y=rows
            float  _Thickness;
            CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv  = v.uv * _Tiling.xy; // 0..cols/rows
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 f = frac(i.uv);
                float2 d = min(f, 1.0 - f);                 // dist to nearest grid line
                // smooth line mask to avoid aliasing
                float w  = _Thickness;
                float ax = smoothstep(w, 0.0, d.x);
                float ay = smoothstep(w, 0.0, d.y);
                float  m = saturate(ax + ay);

                float4 col = lerp(_FillColor, _LineColor, m);
                return col;
            }
            ENDHLSL
        }
    }
}
