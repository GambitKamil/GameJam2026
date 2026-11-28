Shader "Hidden/Game Jam/Kitchen Grade"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            fixed4 frag(v2f_img input) : SV_Target
            {
                float3 source = tex2D(_MainTex, input.uv).rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    source = LinearToGammaSpace(source);
                #endif
                float luma = Luminance(source);
                float preserveWarmLights = smoothstep(.17, .54, luma) * saturate((source.r - source.b) * 3.8);
                float3 tint = lerp(float3(.88, .97, 1.10), float3(1.035, .985, .93), preserveWarmLights);
                float3 graded = lerp(luma.xxx, source, .70 + preserveWarmLights * .24) * tint;
                float3 contrasted = graded * (0.83 + 0.30 * smoothstep(.07, .48, luma));
                float2 edge = (input.uv - float2(.5, .46)) * float2(1.12, 1.0);
                contrasted *= 1.0 - .24 * smoothstep(.30, .69, length(edge));
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    contrasted = GammaToLinearSpace(contrasted);
                #endif
                return fixed4(contrasted, 1);
            }
            ENDCG
        }
    }
}
