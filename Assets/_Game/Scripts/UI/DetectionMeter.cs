using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// World-space suspicion ring that floats above a guard's head and always faces the main camera.
/// Set <see cref="Level"/> (0..1) every frame and call <see cref="Spotted"/> when the player is caught.
/// The ring is drawn by a runtime object that is NOT parented to this transform, so the component can
/// sit on the guard, on its head bone or on an empty object without inheriting the guard's turn-around
/// rotation or scale. This transform itself is never moved.
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class DetectionMeter : MonoBehaviour
{
    private const float ShowThreshold = 0.02f;
    private const float TwoPi = Mathf.PI * 2f;
    private const float PopPeakShape = 0.6f;
    // Rows per arc point: outer fringe, outer edge, inner edge, inner fringe.
    private const int Rows = 4;
    private const int DotSegments = 20;

    [Header("Placement")]
    [Tooltip("The guard's head. Empty = this transform.")]
    [SerializeField] private Transform anchor;
    [Tooltip("World-space offset from the anchor.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 0.55f, 0f);

    [Header("Shape (world units)")]
    [Tooltip("Outer diameter of the ring.")]
    [SerializeField, Min(0.01f)] private float diameter = 0.32f;
    [SerializeField, Min(0.001f)] private float ringWidth = 0.028f;
    [SerializeField, Min(0f)] private float dotDiameter = 0.04f;
    [Tooltip("Arc resolution. Read once on Awake.")]
    [SerializeField, Range(8, 256)] private int segments = 64;
    [Tooltip("Anti-aliased edge width in screen pixels.")]
    [SerializeField, Range(0f, 4f)] private float edgeSoftness = 1.25f;

    [Header("Colours")]
    [SerializeField] private Color fillColor = new Color32(208, 57, 43, 255);
    [SerializeField] private Color trackColor = new Color(0.2f, 0.2f, 0.21f, 0.35f);
    [SerializeField] private Color dotColor = new Color32(208, 57, 43, 220);
    [SerializeField] private Color flashColor = new Color(1f, 0.92f, 0.86f, 1f);

    [Header("Motion (seconds)")]
    [Tooltip("How quickly the shown fill eases toward Level (per second).")]
    [SerializeField, Min(0.1f)] private float fillResponse = 10f;
    [SerializeField, Min(0.01f)] private float fadeInTime = 0.15f;
    [SerializeField, Min(0.01f)] private float fadeOutTime = 0.5f;
    [SerializeField, Min(0.01f)] private float popDuration = 0.25f;
    [SerializeField, Min(1f)] private float popScale = 1.35f;
    [SerializeField, Min(0.01f)] private float flashDuration = 0.35f;
    [Tooltip("How long the full ring holds after the pop before it fades out.")]
    [SerializeField, Min(0f)] private float spottedHold = 0.2f;

    [Header("Rendering")]
    [Tooltip("Optional. Needs a vertex-colour shader such as Sprites/Default; used as is. Empty = a Sprites/Default material is created at runtime.")]
    [SerializeField] private Material material;
    [Tooltip("Render queue of the runtime material. Transparent + 50 draws over the fog.")]
    [SerializeField] private int renderQueue = 3050;

    private Mesh mesh;
    private Material ownedMaterial;
    private Material renderMaterial;
    private GameObject ringObject;
    private Transform ringTransform;
    private MeshRenderer ringRenderer;
    private Camera viewCamera;

    private Vector3[] vertices;
    private Color32[] colors;
    private Vector3[] dotDirections;
    private int segmentCount;
    private int arcVertexCount;
    private int dotStart;

    private float level;
    private float shownFill;
    private float visibility;
    private float spottedClock = -1f;
    private bool armed = true;

    /// <summary>0..1: how close the player is to being spotted.</summary>
    public float Level
    {
        get => level;
        set => level = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
    }

    /// <summary>Fills the ring, pops and flashes it, then fades it out. The ring stays hidden afterwards
    /// until Level drops back to zero, so it does not reappear at full while the level resets.</summary>
    public void Spotted()
    {
        spottedClock = 0f;
        shownFill = 1f;
        visibility = 1f;
        armed = false;
    }

    private void Awake()
    {
        segmentCount = Mathf.Clamp(segments, 8, 256);
        BuildMesh();

        renderMaterial = material;
        if (renderMaterial != null) return;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("UI/Default");
        if (shader == null)
        {
            Debug.LogWarning("DetectionMeter: Sprites/Default shader not found, assign a material.", this);
            return;
        }
        ownedMaterial = new Material(shader);
        ownedMaterial.name = "Detection Meter (runtime)";
        ownedMaterial.renderQueue = renderQueue;
        renderMaterial = ownedMaterial;
    }

    private void Start() => EnsureRing();

    private void OnDisable()
    {
        if (ringRenderer != null) ringRenderer.enabled = false;
    }

    private void OnDestroy()
    {
        if (ringObject != null) Destroy(ringObject);
        if (mesh != null) Destroy(mesh);
        if (ownedMaterial != null) Destroy(ownedMaterial);
    }

    private void LateUpdate()
    {
        Advance(Time.deltaTime);

        if (ringObject == null) EnsureRing();
        if (ringRenderer == null) return;

        Camera view = ResolveCamera();
        bool anchorHidden = anchor != null && !anchor.gameObject.activeInHierarchy;
        bool show = visibility > 0f && view != null && !anchorHidden;
        if (ringRenderer.enabled != show) ringRenderer.enabled = show;
        if (!show) return;

        // Align with the camera plane: circles stay round in orthographic and perspective views.
        Vector3 position = (anchor != null ? anchor.position : transform.position) + offset;
        ringTransform.SetPositionAndRotation(position, view.transform.rotation);

        float flash = 0f;
        float scale = 1f;
        if (spottedClock >= 0f)
        {
            flash = 1f - Mathf.Clamp01(spottedClock / Mathf.Max(0.01f, flashDuration));
            flash *= flash;
            if (spottedClock < popDuration)
            {
                // 1 -> popScale -> 1, peaking early (about a third of the way in).
                float t = spottedClock / Mathf.Max(0.01f, popDuration);
                scale = 1f + (popScale - 1f) * Mathf.Sin(Mathf.PI * Mathf.Pow(t, PopPeakShape));
            }
        }

        Color fill = Color.Lerp(fillColor, flashColor, flash);
        Color dot = Color.Lerp(dotColor, flashColor, flash);
        Color track = trackColor;
        fill.a *= visibility;
        dot.a *= visibility;
        track.a *= visibility;
        if (QualitySettings.activeColorSpace == ColorSpace.Linear)
        {
            fill = fill.linear;
            dot = dot.linear;
            track = track.linear;
        }

        float softness = edgeSoftness * WorldPerPixel(view, position);
        WriteGeometry(shownFill, scale, softness, fill, track, dot);
    }

    private void Advance(float dt)
    {
        if (!armed && level <= ShowThreshold) armed = true;

        if (spottedClock >= 0f)
        {
            spottedClock += dt;
            shownFill = 1f;
            if (spottedClock <= popDuration + spottedHold)
            {
                visibility = 1f;
                return;
            }
            visibility = Mathf.MoveTowards(visibility, 0f, dt / Mathf.Max(0.01f, fadeOutTime));
            if (visibility <= 0f)
            {
                spottedClock = -1f;
                shownFill = 0f;
            }
            return;
        }

        bool wanted = armed && level > ShowThreshold;
        float blend = 1f - Mathf.Exp(-fillResponse * dt);
        shownFill = Mathf.Lerp(shownFill, wanted ? level : 0f, blend);
        float fade = wanted ? fadeInTime : fadeOutTime;
        visibility = Mathf.MoveTowards(visibility, wanted ? 1f : 0f, dt / Mathf.Max(0.01f, fade));
    }

    private Camera ResolveCamera()
    {
        if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main;
        return viewCamera;
    }

    private static float WorldPerPixel(Camera view, Vector3 position)
    {
        float pixels = Mathf.Max(1, view.pixelHeight);
        if (view.orthographic) return 2f * view.orthographicSize / pixels;
        Transform viewTransform = view.transform;
        float depth = Mathf.Max(0.01f, Vector3.Dot(position - viewTransform.position, viewTransform.forward));
        return 2f * depth * Mathf.Tan(0.5f * view.fieldOfView * Mathf.Deg2Rad) / pixels;
    }

    private void EnsureRing()
    {
        if (ringObject != null || renderMaterial == null || mesh == null) return;

        ringObject = new GameObject("Detection Meter Ring");
        ringObject.layer = gameObject.layer;
        Scene scene = gameObject.scene;
        if (scene.IsValid() && scene.isLoaded && ringObject.scene != scene)
        {
            try { SceneManager.MoveGameObjectToScene(ringObject, scene); }
            catch (System.ArgumentException) { }
        }

        ringTransform = ringObject.transform;
        ringObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        ringRenderer = ringObject.AddComponent<MeshRenderer>();
        ringRenderer.sharedMaterial = renderMaterial;
        ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
        ringRenderer.receiveShadows = false;
        ringRenderer.lightProbeUsage = LightProbeUsage.Off;
        ringRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        ringRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        ringRenderer.allowOcclusionWhenDynamic = false;
        ringRenderer.enabled = false;
    }

    private void BuildMesh()
    {
        int points = segmentCount + 1;
        arcVertexCount = points * Rows;
        dotStart = arcVertexCount * 2;
        int vertexCount = dotStart + 1 + DotSegments * 2;

        vertices = new Vector3[vertexCount];
        colors = new Color32[vertexCount];
        var uvs = new Vector2[vertexCount];
        for (int i = 0; i < vertexCount; i++) uvs[i] = new Vector2(0.5f, 0.5f);

        dotDirections = new Vector3[DotSegments];
        for (int j = 0; j < DotSegments; j++)
        {
            float angle = TwoPi * j / DotSegments;
            dotDirections[j] = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
        }

        // Two arcs (fill, then track) of segmentCount quads x 3 strips, plus the dot (fan + fringe).
        // Angles run clockwise from 12 o'clock; triangles are wound clockwise as seen from the camera.
        var triangles = new int[2 * segmentCount * (Rows - 1) * 6 + DotSegments * 9];
        int t = 0;
        for (int arc = 0; arc < 2; arc++)
        {
            int first = arc * arcVertexCount;
            for (int i = 0; i < segmentCount; i++)
            {
                for (int k = 0; k < Rows - 1; k++)
                {
                    int a = first + i * Rows + k; // row k, point i
                    int b = a + Rows;             // row k, point i + 1
                    int c = a + 1;                // row k + 1, point i
                    int d = b + 1;                // row k + 1, point i + 1
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                    triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
                }
            }
        }

        int centre = dotStart;
        int inner = dotStart + 1;
        int outer = inner + DotSegments;
        for (int j = 0; j < DotSegments; j++)
        {
            int n = (j + 1) % DotSegments;
            triangles[t++] = centre; triangles[t++] = inner + j; triangles[t++] = inner + n;
            triangles[t++] = outer + j; triangles[t++] = outer + n; triangles[t++] = inner + j;
            triangles[t++] = outer + n; triangles[t++] = inner + n; triangles[t++] = inner + j;
        }

        mesh = new Mesh { name = "Detection Meter Ring" };
        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.colors32 = colors;
        mesh.uv = uvs;
        mesh.triangles = triangles;
    }

    private void WriteGeometry(float fill, float scale, float softness, Color32 fillTint, Color32 trackTint, Color32 dotTint)
    {
        float outerRadius = diameter * 0.5f * scale;
        float width = Mathf.Clamp(ringWidth, 0.001f, diameter * 0.5f) * scale;
        float innerRadius = outerRadius - width;
        float edge = Mathf.Min(softness * 0.5f, width * 0.5f);
        float r0 = outerRadius + edge;
        float r1 = outerRadius - edge;
        float r2 = innerRadius + edge;
        float r3 = Mathf.Max(0f, innerRadius - edge);

        float split = Mathf.Clamp01(fill) * TwoPi;
        WriteArc(0, 0f, split, r0, r1, r2, r3, fillTint);
        WriteArc(arcVertexCount, split, TwoPi, r0, r1, r2, r3, trackTint);

        float dotRadius = dotDiameter * 0.5f * scale;
        float dotEdge = Mathf.Min(softness * 0.5f, dotRadius);
        float solid = dotRadius - dotEdge;
        float fringe = dotRadius + dotEdge;
        Color32 dotClear = dotTint;
        dotClear.a = 0;
        vertices[dotStart] = Vector3.zero;
        colors[dotStart] = dotTint;
        for (int j = 0; j < DotSegments; j++)
        {
            int v = dotStart + 1 + j;
            vertices[v] = dotDirections[j] * solid;
            colors[v] = dotTint;
            vertices[v + DotSegments] = dotDirections[j] * fringe;
            colors[v + DotSegments] = dotClear;
        }

        mesh.vertices = vertices;
        mesh.colors32 = colors;
        float extent = 2f * Mathf.Max(r0, fringe);
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(extent, extent, 0.01f));
    }

    private void WriteArc(int first, float from, float to, float r0, float r1, float r2, float r3, Color32 tint)
    {
        Color32 clear = tint;
        clear.a = 0;
        float step = (to - from) / segmentCount;
        for (int i = 0; i <= segmentCount; i++)
        {
            float angle = from + step * i;
            float x = Mathf.Sin(angle);
            float y = Mathf.Cos(angle);
            int v = first + i * Rows;
            vertices[v] = new Vector3(x * r0, y * r0, 0f);
            vertices[v + 1] = new Vector3(x * r1, y * r1, 0f);
            vertices[v + 2] = new Vector3(x * r2, y * r2, 0f);
            vertices[v + 3] = new Vector3(x * r3, y * r3, 0f);
            colors[v] = clear;
            colors[v + 1] = tint;
            colors[v + 2] = tint;
            colors[v + 3] = clear;
        }
    }
}
