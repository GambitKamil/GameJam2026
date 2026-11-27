Shader "Game Jam/Weathered Surface"
{
    Properties
    {
        _Color ("Base Color", Color) = (0.34, 0.37, 0.36, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.3
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Wear ("Surface Variation", Range(0, 1)) = 0.6
        _Relief ("Surface Relief", Range(0, 0.08)) = 0.024
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows

        fixed4 _Color;
        half _Glossiness, _Metallic, _Wear, _Relief;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        float lattice(float3 p)
        {
            p = frac(p * float3(0.1031, 0.11369, 0.13787));
            p += dot(p, p.yzx + 19.19);
            return frac((p.x + p.y) * p.z);
        }

        float noise3(float3 p)
        {
            float3 cell = floor(p);
            float3 t = frac(p);
            t = t * t * (3.0 - 2.0 * t);
            float front = lerp(lerp(lattice(cell), lattice(cell + float3(1,0,0)), t.x),
                lerp(lattice(cell + float3(0,1,0)), lattice(cell + float3(1,1,0)), t.x), t.y);
            float back = lerp(lerp(lattice(cell + float3(0,0,1)), lattice(cell + float3(1,0,1)), t.x),
                lerp(lattice(cell + float3(0,1,1)), lattice(cell + float3(1,1,1)), t.x), t.y);
            return lerp(front, back, t.z);
        }

        void surf(Input IN, inout SurfaceOutputStandard output)
        {
            float3 worldPosition = IN.worldPos;
            float mottling = noise3(worldPosition * 2.2);
            float grain = noise3(worldPosition * 17.0);
            float drip = noise3(worldPosition * float3(10.0, 0.95, 10.0));
            float deposits = smoothstep(0.52, 0.83, mottling * 0.66 + drip * 0.34);
            float tint = (mottling - 0.5) * 0.64 + (grain - 0.5) * 0.35 - deposits * 0.53;
            float3 weathered = _Color.rgb * (1 + _Wear * tint);
            weathered = lerp(weathered, weathered * float3(1.12, 1.0, 0.78), _Wear * deposits);

            float3 surfaceNormal = normalize(WorldNormalVector(IN, float3(0, 0, 1)));
            float3 dx = ddx(worldPosition);
            float3 dy = ddy(worldPosition);
            float3 right = cross(dy, surfaceNormal);
            float3 up = cross(surfaceNormal, dx);
            float determinant = dot(dx, right);
            float height = (mottling * 0.72 + grain * 0.28) * _Relief;
            float3 gradient = sign(determinant) * (ddx(height) * right + ddy(height) * up);
            float3 roughNormal = normalize(max(abs(determinant), 1e-7) * surfaceNormal - gradient);
            float3 tangent = normalize(WorldNormalVector(IN, float3(1, 0, 0)));
            float3 bitangent = normalize(WorldNormalVector(IN, float3(0, 1, 0)));

            output.Albedo = weathered;
            output.Normal = float3(dot(roughNormal, tangent), dot(roughNormal, bitangent), dot(roughNormal, surfaceNormal));
            output.Smoothness = saturate(_Glossiness + _Wear * (deposits * 0.42 - grain * 0.22));
            output.Metallic = _Metallic;
        }
        ENDCG
    }
    Fallback "Standard"
}
