Shader "Game Jam/Dust in lamplight"
{
    Properties
    {
        _Visibility ("Beam occlusion", 2D) = "white" {}
        _Color ("Color", Color) = (1, 0.88, 0.7, 1)
        _Intensity ("Intensity", Float) = 1
        _NoiseScale ("Noise scale (cells per metre)", Float) = 1.6
        _NoiseStrength ("Noise strength", Range(0, 1)) = 0.45
        _Drift ("Drift speed (metres per second)", Float) = 0.25
    }
    SubShader
    {
        // The march works in the unit box's object space, so batching must not bake the transform away.
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            Cull Front ZTest Always ZWrite Off Blend SrcAlpha One
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define SAMPLES 18

            sampler2D _Visibility;
            fixed4 _Color;
            float _Intensity;
            float _NoiseScale;
            float _NoiseStrength;
            float _Drift;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct Varying
            {
                float4 pos : SV_POSITION;
                float3 local : TEXCOORD0;
                float3 world : TEXCOORD1;
                float4 screen : TEXCOORD2;
            };

            Varying vert(appdata_base v)
            {
                Varying o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.local = v.vertex.xyz;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            // Sine-free hash, stable for large world coordinates.
            float Hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float3 x)
            {
                float3 cell = floor(x);
                float3 f = x - cell;
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash(cell);
                float n100 = Hash(cell + float3(1.0, 0.0, 0.0));
                float n010 = Hash(cell + float3(0.0, 1.0, 0.0));
                float n110 = Hash(cell + float3(1.0, 1.0, 0.0));
                float n001 = Hash(cell + float3(0.0, 0.0, 1.0));
                float n101 = Hash(cell + float3(1.0, 0.0, 1.0));
                float n011 = Hash(cell + float3(0.0, 1.0, 1.0));
                float n111 = Hash(cell + float3(1.0, 1.0, 1.0));
                float lower = lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y);
                float upper = lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y);
                return lerp(lower, upper, f.z);
            }

            // Density multiplier with a mean of about 1. World space, so the haze stays put while the torch sweeps.
            float Haze(float3 world)
            {
                if (_NoiseStrength <= 0.0) return 1.0;
                float t = _Time.y * _Drift;
                // Slightly stretched sideways so the wisps read as layered fog rather than round blobs.
                float3 stretch = float3(0.75, 1.0, 0.75) * _NoiseScale;
                // Two octaves drifting in different directions, so the haze evolves instead of sliding rigidly.
                float broad = ValueNoise((world - t * float3(1.0, 0.12, 0.35)) * stretch);
                float fine = ValueNoise((world - t * float3(-0.55, 0.3, 0.45)) * stretch * 2.31 + float3(13.7, 3.1, 7.9));
                float n = 0.65 * broad + 0.35 * fine;
                float contrast = saturate((n - 0.5) * 3.0 + 0.5);
                return lerp(1.0, 2.0 * contrast, _NoiseStrength);
            }

            fixed4 frag(Varying i) : SV_Target
            {
                // View ray in world space: from the camera position (perspective) or along the camera forward (orthographic).
                bool ortho = unity_OrthoParams.w > 0.5;
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float3 toFragment = i.world - _WorldSpaceCameraPos;
                float3 ray = ortho ? forward : normalize(toFragment);
                float cosine = max(1e-4, dot(ray, forward));

                // Distance along the ray from the camera (orthographic: the camera plane) to this back face.
                float tExit = dot(toFragment, ray);

                // Object-space step per world metre back toward the camera, then the distance to the front face.
                float3 towardCamera = mul((float3x3)unity_WorldToObject, -ray);
                float3 side = step(0.0, towardCamera);
                float3 safe = (side * 2.0 - 1.0) * max(abs(towardCamera), 1e-6);
                float3 lengths = ((side - 0.5) - i.local) / safe;
                float through = max(0.0, min(lengths.x, min(lengths.y, lengths.z)));
                float tEnter = tExit - through;

                // Opaque scene depth converted to distance along the ray.
                float raw = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen));
                float orthoRaw = raw;
                #if defined(UNITY_REVERSED_Z)
                    orthoRaw = 1.0 - raw;
                #endif
                float orthoDepth = lerp(_ProjectionParams.y, _ProjectionParams.z, orthoRaw);
                float perspectiveDepth = LinearEyeDepth(raw);
                float sceneDepth = ortho ? orthoDepth : perspectiveDepth;
                float tOpaque = sceneDepth / cosine;

                // Integrate from the front face (or the near plane, when the camera is inside the box)
                // to the back face or the first opaque surface, whichever is nearer.
                float tStart = max(tEnter, _ProjectionParams.y / cosine);
                float tEnd = min(tExit, tOpaque);
                float span = tEnd - tStart;
                if (span <= 1e-4) return fixed4(0.0, 0.0, 0.0, 0.0);
                float stride = span / SAMPLES;

                // Per-pixel offset of the sample positions turns slicing bands into fine grain.
                float2 pixel = (i.screen.xy / i.screen.w) * _ScreenParams.xy;
                float jitter = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));

                float density = 0.0;
                [loop]
                for (int n = 0; n < SAMPLES; n++)
                {
                    float behind = tExit - (tStart + (n + jitter) * stride);
                    float3 p = i.local + towardCamera * behind;
                    float axial = p.z + 0.5;
                    float2 angle = p.xy / max(0.016, axial);
                    float radius = 2.0 * length(angle);
                    float cone = (1.0 - smoothstep(0.45, 1.0, radius))
                        * pow(saturate(1.0 - axial), 1.35) / (0.35 + axial);
                    [branch]
                    if (cone > 1e-4)
                    {
                        float stop = tex2Dlod(_Visibility, float4(angle + 0.5, 0.0, 0.0)).r;
                        float unblocked = smoothstep(-0.008, 0.016, stop - axial);
                        density += cone * unblocked * Haze(i.world - ray * behind);
                    }
                }
                float amount = density * stride * 0.24 * _Intensity;
                return fixed4(_Color.rgb, saturate(amount) * _Color.a);
            }
            ENDCG
        }
    }
}
