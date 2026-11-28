Shader "Game Jam/Kitchen Lamp Haze"
{
    Properties
    {
        _Color ("Tint", Color) = (.4,.48,.56,.12)
        _Spread ("Cone spread", Range(.1,1)) = .74
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _Spread;

            struct Vertex { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct Fragment { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

            Fragment vert(Vertex input)
            {
                Fragment output;
                output.pos = UnityObjectToClipPos(input.pos);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(Fragment input) : SV_Target
            {
                float t = 1 - input.uv.y;
                float width = lerp(.13, .5, saturate(t * _Spread));
                float edge = abs(input.uv.x - .5) / width;
                float cone = (1 - smoothstep(.03,1,edge));
                float fade = smoothstep(0,.08,t) * (1 - smoothstep(.49,1,t));
                float bands = .86 + .14 * sin(17 * input.uv.x + 7 * t + _Time.y * .55);
                return fixed4(_Color.rgb, cone * cone * fade * bands * _Color.a);
            }
            ENDCG
        }
    }
}
