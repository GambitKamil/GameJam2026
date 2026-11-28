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

            float hash(float2 samplePosition)
            {
                return frac(sin(dot(samplePosition, float2(127.1, 311.7))) * 43758.55);
            }

            float vaporNoise(float2 samplePosition)
            {
                float2 cell = floor(samplePosition);
                float2 offset = frac(samplePosition);
                offset = offset * offset * (3 - 2 * offset);
                return lerp(lerp(hash(cell), hash(cell + float2(1,0)), offset.x),
                    lerp(hash(cell + float2(0,1)), hash(cell + 1), offset.x), offset.y);
            }

            fixed4 frag(Varyings i) : SV_Target
            {
                float2 centered = (i.uv - .5) * 2.0;
                float time = _Time.y;
                float2 flow = i.uv * float2(4.5, 3.0) - float2(time * .25, time * .50);
                float curl = vaporNoise(flow * .63 + float2(-time * .19, 3.0));
                float density = vaporNoise(flow + curl * 1.8) * .65 + vaporNoise(flow * 2.3) * .35;
                centered.x += (curl - .5) * .39;
                float profile = saturate(1 - dot(centered, centered));
                float alpha = smoothstep(.24,.80,density) * profile * profile * i.color.a;
                return fixed4(i.color.rgb, alpha);
            }
            ENDCG
        }
    }
}
