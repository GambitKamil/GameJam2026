Shader "Game Jam/Freezer Frost"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Vertex { float4 position : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Vertex v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.position);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(41.7, 289.1))) * 43758.54); }

            float grain(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                    lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            fixed4 frag(Varyings i) : SV_Target
            {
                float strength = i.color.a;
                float2 uv = i.uv;
                float2 edge = min(uv, 1.0 - uv);
                float inset = min(edge.x * 1.42, edge.y);
                float2 p = uv * float2(1.5, 1);
                float patches = grain(p * 12.0) * 0.65 + grain(p * 33.0) * 0.35;
                float2 m = mul(float2x2(0.866, -0.5, 0.5, 0.866), p);
                float shards = max(grain(m * float2(76, 17)), grain((m + float2(2, 3)) * float2(22, 101)));
                float flake = smoothstep(0.63, 0.88, shards * 0.77 + grain(p * 232) * 0.23);
                float width = (0.035 + strength * 0.205) * (0.54 + patches * 0.92);
                float frost = 1.0 - smoothstep(width * 0.10, width, inset);
                float veil = 1.0 - smoothstep(0, 0.43, length((uv - 0.5) * float2(0.95, 1.1)));
                float3 ice = lerp(float3(0.10, 0.20, 0.30), float3(0.57, 0.76, 0.86), flake);
                float alpha = saturate(strength * (frost * (0.39 + 0.54 * flake) + (1 - veil) * 0.11));
                return fixed4(ice, alpha);
            }
            ENDCG
        }
    }
}
