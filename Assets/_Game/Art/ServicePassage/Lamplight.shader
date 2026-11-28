Shader "Game Jam/Dust in lamplight"
{
    Properties { _Visibility ("Beam occlusion", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Front ZTest Always ZWrite Off Blend SrcAlpha One
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _Visibility;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            struct Varying { float4 pos : SV_POSITION; float3 local : TEXCOORD0; float4 screen : TEXCOORD1; float eye : TEXCOORD2; };
            Varying vert(appdata_base v)
            {
                Varying o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.local = v.vertex.xyz;
                o.screen = ComputeScreenPos(o.pos);
                o.eye = -UnityObjectToViewPos(v.vertex).z;
                return o;
            }
            fixed4 frag(Varying i) : SV_Target
            {
                // The back face bounds the integration. March toward the orthographic camera in world metres.
                float3 dir = mul((float3x3)unity_WorldToObject, UNITY_MATRIX_V[2].xyz);
                float3 edge = lerp(-0.5, 0.5, step(0.0, dir));
                float3 lengths = (edge - i.local) / (dir + 1e-6);
                float travel = min(lengths.x, min(lengths.y, lengths.z));
                float depth = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen));
                #if defined(UNITY_REVERSED_Z)
                    depth = 1.0 - depth;
                #endif
                float opaque = lerp(_ProjectionParams.y, _ProjectionParams.z, depth);
                float start = max(0.0, i.eye - opaque);
                float stride = max(0.0, travel - start) / 18.0;
                float density = 0.0;
                for (int n = 0; n < 18; n++)
                {
                    float3 p = i.local + dir * (start + (n + 0.5) * stride);
                    float axial = p.z + 0.5;
                    float2 angle = p.xy / max(0.016, axial);
                    float radius = 2.0 * length(angle);
                    float stop = tex2Dlod(_Visibility, float4(angle + 0.5, 0, 0)).r;
                    float lit = smoothstep(-0.008, 0.016, stop - axial);
                    density += (1.0 - smoothstep(0.45, 1.0, radius)) * lit
                        * pow(saturate(1.0 - axial), 1.35) / (0.35 + axial);
                }
                return fixed4(1.0,0.86,0.64,saturate(density * stride * 0.24));
            }
            ENDCG
        }
    }
}
