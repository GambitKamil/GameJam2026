Shader "Game Jam/Kitchen Mist"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Vertex { float4 position : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Vertex v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.position);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;
                float radial = saturate(1.0 - dot(p, p));
                float softness = radial * radial * radial;
                return fixed4(i.color.rgb, i.color.a * softness);
            }
            ENDCG
        }
    }
}
