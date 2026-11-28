Shader "Game Jam/Kitchen Worn Surface"
{
    Properties
    {
        _Color ("Surface", Color) = (.32,.35,.36,1)
        _Stain ("Stains", Color) = (.13,.15,.16,1)
        _Metallic ("Metallic", Range(0,1)) = .1
        _Glossiness ("Smoothness", Range(0,1)) = .22
        _Wear ("Wear", Range(0,1)) = .4
        _Tiles ("Floor tile seams", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows

        fixed4 _Color, _Stain;
        half _Metallic, _Glossiness, _Wear, _Tiles;
        struct Input { float3 worldPos; };

        float hash(float3 pos)
        {
            pos = frac(pos * .3183099 + float3(.11,.26,.19)) * 17.0;
            return frac(pos.x * pos.y * pos.z * (pos.x + pos.y + pos.z));
        }

        float noise(float3 pos)
        {
            float3 base = floor(pos);
            float3 blend = frac(pos);
            blend = blend * blend * (3.0 - 2.0 * blend);
            return lerp(
                lerp(lerp(hash(base), hash(base + float3(1,0,0)), blend.x),
                     lerp(hash(base + float3(0,1,0)), hash(base + float3(1,1,0)), blend.x), blend.y),
                lerp(lerp(hash(base + float3(0,0,1)), hash(base + float3(1,0,1)), blend.x),
                     lerp(hash(base + float3(0,1,1)), hash(base + 1), blend.x), blend.y), blend.z);
        }

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            float3 pos = input.worldPos;
            float broad = noise(pos * 2.7);
            float grit = noise(pos * 64);
            float streaks = noise(pos * float3(10,1.1,10));
            float staining = smoothstep(.28,.86,broad * .57 + streaks * .43) * _Wear;
            float scuff = smoothstep(.57,.81,noise(pos * float3(2.5,32,21))) * grit;
            float2 grid = pos.xz / .94;
            float2 edge = abs(frac(grid) - .5);
            float seam = smoothstep(.476,.489,max(edge.x,edge.y)) * _Tiles;
            float wet = smoothstep(.51,.79,noise(pos * 1.3) * .72 + broad * .28);
            float tile = 1 + (hash(float3(floor(grid), 1)) - .5) * .28 * _Tiles;
            output.Albedo = lerp(_Color.rgb * (.89 + .16 * grit + .13 * scuff) * tile, _Stain.rgb, staining);
            output.Albedo *= 1 - seam * .59;
            output.Metallic = _Metallic * (1 - staining * .76 - seam * .56);
            output.Smoothness = saturate(_Glossiness + wet * (.12 + .17 * _Tiles) - staining * .33 - seam * .12);
            output.Occlusion = 1 - staining * .24 - seam * .18;
            output.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
