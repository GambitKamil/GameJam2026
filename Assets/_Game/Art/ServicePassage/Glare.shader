Shader "Game Jam/Additive glare"
{
    Properties
    {
        _MainTex ("Texture (alpha = shape)", 2D) = "white" {}
        _Color ("Tint (alpha = strength)", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Color;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct Varying
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            Varying vert(Attributes v)
            {
                Varying o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            half4 frag(Varying i) : SV_Target
            {
                // Vertex colour * texture * tint; alpha scales the added light.
                half4 c = tex2D(_MainTex, i.uv) * i.color * _Color;
                return half4(c.rgb * c.a, 0.0);
            }
            ENDCG
        }
    }
}
