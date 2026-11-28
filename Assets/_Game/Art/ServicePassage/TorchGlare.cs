using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Camera-facing additive glare at the torch lens: a soft round glow plus a thin horizontal streak.
// It flares only when the torch points at the camera; a faint core always marks the lens as a light source.
public sealed class TorchGlare : MonoBehaviour
{
    [Tooltip("Torch transform. The beam points along its forward axis.")]
    [SerializeField] private Transform lens;
    [Tooltip("Game Jam/Additive glare. Must be assigned so the shader is included in builds.")]
    [SerializeField] private Shader shader;
    [SerializeField] private float size = 0.9f;
    [SerializeField] private Color core = new Color(1f, 0.93f, 0.8f, 1f);
    [SerializeField] private Color streak = new Color(0.55f, 0.7f, 1f, 1f);
    [Tooltip("Distance from the lens transform to the glass, along lens.forward.")]
    [SerializeField] private float lensOffset = 0.14f;
    [Tooltip("Glow strength while the torch points away from the camera.")]
    [SerializeField, Range(0f, 1f)] private float constantCore = 0.15f;
    [Tooltip("Exponent on the facing term. Higher means only a direct look at the camera flares.")]
    [SerializeField] private float facingPower = 6f;
    [SerializeField] private float brightness = 1.2f;
    [Tooltip("Streak width as a multiple of size. Its height is a sixth of the width.")]
    [SerializeField] private float streakLength = 3.5f;
    [SerializeField] private LayerMask occluders = Physics.DefaultRaycastLayers;

    private const float OcclusionInterval = 0.1f;
    private const float FadeSpeed = 6f;
    private const float StreakAspect = 6f;
    private const float PullTowardCamera = 0.12f;
    private const float LinecastClearance = 0.05f;

    private int colorId;
    private Mesh quad;
    private Texture2D glowTexture;
    private Texture2D streakTexture;
    private Material glowMaterial;
    private Material streakMaterial;
    private GameObject glowObject;
    private GameObject streakObject;
    private MeshRenderer glowRenderer;
    private MeshRenderer streakRenderer;
    private Transform glowTransform;
    private Transform streakTransform;
    private float visibility;
    private float targetVisibility;
    private float nextOcclusionCheck;
    private bool occlusionKnown;
    private bool ready;

    private void Awake()
    {
        if (lens == null) lens = transform;
        Shader glareShader = shader;
#if UNITY_EDITOR
        if (glareShader == null) glareShader = Shader.Find("Game Jam/Additive glare");
#endif
        if (glareShader == null)
        {
            Debug.LogWarning("TorchGlare: assign the 'Game Jam/Additive glare' shader.", this);
            enabled = false;
            return;
        }

        colorId = Shader.PropertyToID("_Color");
        quad = BuildQuad();
        glowTexture = BuildGlowTexture(64);
        streakTexture = BuildStreakTexture(128, 32);
        glowMaterial = BuildMaterial(glareShader, glowTexture, "Torch glare glow");
        streakMaterial = BuildMaterial(glareShader, streakTexture, "Torch glare streak");
        glowRenderer = BuildQuadObject("Torch glare glow", glowMaterial, out glowObject);
        streakRenderer = BuildQuadObject("Torch glare streak", streakMaterial, out streakObject);
        glowTransform = glowObject.transform;
        streakTransform = streakObject.transform;
        ready = true;
    }

    private void OnDisable()
    {
        Show(glowRenderer, false);
        Show(streakRenderer, false);
    }

    private void LateUpdate()
    {
        if (!ready) return;
        Camera view = Camera.main;
        if (view == null || lens == null)
        {
            Show(glowRenderer, false);
            Show(streakRenderer, false);
            return;
        }

        Transform eye = view.transform;
        Vector3 forward = eye.forward;
        Vector3 beam = lens.forward;
        Vector3 origin = lens.position + beam * lensOffset;
        Vector3 toCamera;
        Vector3 cameraPoint;
        if (view.orthographic)
        {
            // Parallel rays: the lens is seen along -forward, from its point on the camera plane.
            toCamera = -forward;
            cameraPoint = origin - forward * Vector3.Dot(origin - eye.position, forward);
        }
        else
        {
            cameraPoint = eye.position;
            Vector3 offset = cameraPoint - origin;
            float distance = offset.magnitude;
            toCamera = distance > 1e-4f ? offset / distance : -forward;
        }

        if (Time.time >= nextOcclusionCheck)
        {
            nextOcclusionCheck = Time.time + OcclusionInterval;
            bool blocked = Physics.Linecast(origin + toCamera * LinecastClearance, cameraPoint,
                occluders, QueryTriggerInteraction.Ignore);
            targetVisibility = blocked ? 0f : 1f;
            if (!occlusionKnown)
            {
                visibility = targetVisibility;
                occlusionKnown = true;
            }
        }
        visibility = Mathf.MoveTowards(visibility, targetVisibility, FadeSpeed * Time.deltaTime);

        float facing = Mathf.Pow(Mathf.Clamp01(Vector3.Dot(beam, toCamera)), facingPower);
        float glowStrength = visibility * brightness * Mathf.Lerp(constantCore, 1f, facing);
        float streakStrength = visibility * brightness * facing;

        // Screen-aligned billboard (camera rotation), so the streak stays horizontal on screen
        // for both orthographic and perspective cameras. Pulled toward the camera to clear the torch body.
        Vector3 position = origin + toCamera * PullTowardCamera;
        Quaternion rotation = eye.rotation;
        float glowSize = size * Mathf.Lerp(0.35f, 1f, facing);
        float streakWidth = size * streakLength * Mathf.Lerp(0.5f, 1f, facing);
        Place(glowRenderer, glowTransform, glowMaterial, position, rotation,
            new Vector3(glowSize, glowSize, 1f), core, glowStrength);
        Place(streakRenderer, streakTransform, streakMaterial, position, rotation,
            new Vector3(streakWidth, streakWidth / StreakAspect, 1f), streak, streakStrength);
    }

    private void Place(MeshRenderer target, Transform holder, Material material, Vector3 position,
        Quaternion rotation, Vector3 scale, Color tint, float strength)
    {
        bool shown = strength > 0.002f;
        Show(target, shown);
        if (!shown) return;
        holder.SetPositionAndRotation(position, rotation);
        holder.localScale = scale;
        material.SetColor(colorId, new Color(tint.r, tint.g, tint.b, tint.a * strength));
    }

    private static void Show(MeshRenderer target, bool shown)
    {
        if (target != null && target.enabled != shown) target.enabled = shown;
    }

    private static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "Torch glare quad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        return mesh;
    }

    // Soft halo with a small hot centre. Shape lives in alpha; colour comes from the material tint.
    private static Texture2D BuildGlowTexture(int resolution)
    {
        var pixels = new Color32[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            float u = (x + 0.5f) / resolution * 2f - 1f;
            float v = (y + 0.5f) / resolution * 2f - 1f;
            float r = Mathf.Sqrt(u * u + v * v);
            float halo = Mathf.Clamp01(1f - r);
            float alpha = 0.6f * halo * halo + 0.4f * Mathf.Exp(-r * r * 28f);
            pixels[y * resolution + x] = new Color32(255, 255, 255, ToByte(alpha));
        }
        return BuildTexture(resolution, resolution, pixels, "Torch glare glow");
    }

    // Long horizontal gaussian, tight vertical falloff; both reach zero at the quad edge.
    private static Texture2D BuildStreakTexture(int width, int height)
    {
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width * 2f - 1f;
            float v = (y + 0.5f) / height * 2f - 1f;
            float along = Mathf.Exp(-u * u / (2f * 0.3f * 0.3f)) * (1f - u * u);
            float across = Mathf.Exp(-v * v / (2f * 0.18f * 0.18f)) * (1f - v * v);
            pixels[y * width + x] = new Color32(255, 255, 255, ToByte(along * across));
        }
        return BuildTexture(width, height, pixels, "Torch glare streak");
    }

    private static Texture2D BuildTexture(int width, int height, Color32[] pixels, string label)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
        {
            name = label,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    private static byte ToByte(float value)
    {
        return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
    }

    private Material BuildMaterial(Shader glareShader, Texture texture, string label)
    {
        var material = new Material(glareShader) { name = label };
        material.mainTexture = texture;
        material.SetColor(colorId, Color.clear);
        return material;
    }

    private MeshRenderer BuildQuadObject(string label, Material material, out GameObject holder)
    {
        // A root object, so the lens hierarchy's scale and rotation never distort the billboard.
        holder = new GameObject(label);
        holder.layer = gameObject.layer;
        if (gameObject.scene.IsValid() && holder.scene != gameObject.scene)
            SceneManager.MoveGameObjectToScene(holder, gameObject.scene);
        holder.AddComponent<MeshFilter>().sharedMesh = quad;
        var meshRenderer = holder.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        meshRenderer.enabled = false;
        return meshRenderer;
    }

    private void OnDestroy()
    {
        Release(glowObject);
        Release(streakObject);
        Release(glowMaterial);
        Release(streakMaterial);
        Release(glowTexture);
        Release(streakTexture);
        Release(quad);
    }

    private static void Release(Object target)
    {
        if (target != null) Destroy(target);
    }
}
