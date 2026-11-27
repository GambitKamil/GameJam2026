Shader "Game Jam/Window Haze"
{
    Properties
    {
        _Color ("Tint", Color) = (0.62, 0.75, 0.79, 1)
        _Intensity ("Intensity", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            half _Intensity;

            Varyings vert(appdata_base input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.texcoord.xy;
                return output;
            }

            fixed4 frag(Varyings input) : SV_Target
            {
                float across = sin(saturate(input.uv.x) * UNITY_PI);
                across *= across;
                float along = smoothstep(0.01, 0.16, input.uv.y) * (1 - smoothstep(0.61, 1, input.uv.y));
                float strata = 0.76 + 0.24 * sin(input.uv.x * 31 + input.uv.y * 3);
                return fixed4(_Color.rgb * _Intensity * across * along * strata, 0);
            }
            ENDCG
        }
    }
}
