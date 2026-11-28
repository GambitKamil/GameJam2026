using UnityEngine;

/// <summary>
/// Full-screen soft vignette whose darkness and slight red tint rise with <see cref="Level"/> (0 = invisible).
/// </summary>
[DisallowMultipleComponent]
public sealed class DetectionVignette : MonoBehaviour
{
    private const int TextureSize = 128;
    private const float DrawThreshold = 0.01f;
    // Normalised distance from the screen centre (1 = edge midpoints) where the shade starts and peaks.
    private const float ClearRadius = 0.4f;
    private const float FullRadius = 1f;

    [Tooltip("Edge darkness at Level 1.")]
    [SerializeField, Range(0f, 1f)] private float maxDarkness = 0.55f;
    [SerializeField] private Color tint = new Color(0.6f, 0.06f, 0.04f, 1f);
    [Tooltip("How far the shade moves from black toward the tint at Level 1.")]
    [SerializeField, Range(0f, 1f)] private float tintStrength = 0.3f;
    [Tooltip("How quickly the shown level eases toward Level (per second). 0 = instant.")]
    [SerializeField, Min(0f)] private float response = 6f;
    [Tooltip("IMGUI depth. Higher values draw behind other OnGUI elements such as prompts.")]
    [SerializeField] private int guiDepth = 10;

    private Texture2D gradient;
    private float level;
    private float shown;

    /// <summary>0..1: vignette strength.</summary>
    public float Level
    {
        get => level;
        set => level = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
    }

    private void Awake()
    {
        useGUILayout = false;
        gradient = BuildGradient();
    }

    private void Update()
    {
        shown = response > 0f ? Mathf.Lerp(shown, level, 1f - Mathf.Exp(-response * Time.deltaTime)) : level;
    }

    private void OnGUI()
    {
        GUI.depth = guiDepth;
        if (gradient == null || shown < DrawThreshold || Event.current.type != EventType.Repaint) return;

        float tintAmount = tintStrength * shown;
        Color previous = GUI.color;
        GUI.color = new Color(tint.r * tintAmount, tint.g * tintAmount, tint.b * tintAmount, maxDarkness * shown);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), gradient, ScaleMode.StretchToFill, true);
        GUI.color = previous;
    }

    private void OnDestroy()
    {
        if (gradient != null) Destroy(gradient);
    }

    private static Texture2D BuildGradient()
    {
        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            name = "Detection Vignette (runtime)",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color32[TextureSize * TextureSize];
        float half = (TextureSize - 1) * 0.5f;
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float u = (x - half) / half;
                float v = (y - half) / half;
                float distance = Mathf.Sqrt(u * u + v * v);
                float shade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ClearRadius, FullRadius, distance));
                shade *= shade;
                pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(shade * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
