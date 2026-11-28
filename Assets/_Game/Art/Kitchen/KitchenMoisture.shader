Shader "Game Jam/Kitchen Moisture"
{
    SubShader
    {
        Tags { "Queue"="Transparent-12" "RenderType"="Transparent" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 pos = (input.uv - .5) * 2;
                float distance = length(pos);
                float waves = sin(pos.x * 10 + pos.y * 8) * .071 + cos(pos.x * 9 - pos.y * 16) * .064;
                float water = 1 - smoothstep(.49,.95,distance + waves);
                float edge = smoothstep(.49,.69,distance + waves) * water;
                float glints = smoothstep(.59,.95,sin(7 * pos.x - 3 * pos.y) * .5 + .5);
                half3 tone = lerp(half3(.04,.055,.065), half3(.16,.20,.21), edge * glints);
                return half4(tone, water * .20 + edge * glints * .16);
            }
            ENDCG
        }
    }
}
