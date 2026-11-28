using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// One-shot visual pass on the Cell scene. Safe to run again: it sets absolute values and replaces
// what it added before. It only changes looks; the key, rod, colliders and scripts keep working.
//   - darker, more cinematic light (fills down, pools kept, dark haze)
//   - a bunch of worn prison keys instead of the single big gold key
//   - a rope knotted from bed sheets instead of the hooked steel rod
public static class CellCinematicPass
{
    private const string CellScene = "Assets/_Game/Scenes/Levels/Cell.unity";
    private const string ArtFolder = "Assets/_Game/Art/CellDressing";
    private const string KeyBunch = "Key bunch";
    private const string SheetRope = "Sheet rope";

    [MenuItem("Game Jam/Cell cinematic pass")]
    public static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Stop Play Mode first.");
        var scene = EditorSceneManager.OpenScene(CellScene, OpenSceneMode.Single);
        var escape = Object.FindFirstObjectByType<CellEscape>();
        if (escape == null) throw new InvalidOperationException("CellEscape not found.");
        var data = new SerializedObject(escape);
        var key = (Transform)data.FindProperty("key").objectReferenceValue;
        var rod = (Transform)data.FindProperty("rod").objectReferenceValue;

        Light();
        Framing();
        Keys(key);
        Rope(rod);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Cell cinematic pass applied.";
    }

    // ------------------------------------------------------------------ light
    private static void Light()
    {
        void Set(string name, float intensity)
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l.name == name) l.intensity = intensity;
        }
        // Few strong pools, dark between them; the fills only keep silhouettes readable.
        Set("Cold Fill", 0.06f);
        Set("Bunk Bounce", 0.22f);
        Set("Grate Side Light", 1.05f);
        Set("Fixture Light", 0.45f);
        Set("Door Pool", 2.0f);
        Set("Key and Service Wall Light", 0.8f);
        Set("Bed Pool", 1.55f);
        Set("Soft White Light", 2.0f);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.07f, 0.078f, 0.09f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.035f, 0.045f, 0.05f);
        RenderSettings.fogStartDistance = 14f;
        RenderSettings.fogEndDistance = 36f;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            cam.backgroundColor = new Color(0.018f, 0.024f, 0.026f);
    }

    // ------------------------------------------------------------------ camera
    // Wider shot and a quicker follow that leads the walk, so the prisoner never runs into the frame edge.
    private static void Framing()
    {
        var rig = Object.FindFirstObjectByType<SideViewCamera>();
        if (rig == null) return;
        var cam = rig.GetComponent<Camera>();
        // Frame exactly from the front edge of the floor to the underside of the ceiling slab:
        // wider than the original 1.94 shot, but no ceiling band on top and no void under the floor.
        const float size = 2.28f, originalSize = 1.94f;
        const float originalY = 5.51f, originalZ = -15.58f;
        const float lower = 0.22f;   // metres along the camera's up axis
        if (cam != null && cam.orthographic)
        {
            cam.orthographicSize = size;
            Vector3 p = rig.transform.position;
            Vector3 basePosition = new Vector3(p.x, originalY, originalZ);
            rig.transform.position = basePosition - rig.transform.up * lower;
        }
        float widen = cam != null ? (size - originalSize) * cam.aspect : 0.6f;
        var data = new SerializedObject(rig);
        data.FindProperty("smoothTime").floatValue = 0.11f;
        data.FindProperty("lookAhead").floatValue = 1.25f;
        data.FindProperty("lookAheadTurnTime").floatValue = 0.22f;
        data.FindProperty("horizontalLimits").vector2Value = new Vector2(-4.70f + widen, 5.05f - widen);
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ keys
    private static void Keys(Transform key)
    {
        Transform hanging = key.Find("Hanging Key") ?? key;
        RemoveOld(hanging, KeyBunch);
        foreach (Renderer r in hanging.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        Material iron = Load("EnvironmentKit/Materials/Iron");
        Material brass = Worn("Cell aged brass", new Color(0.34f, 0.27f, 0.15f), 0.55f, 0.3f);
        Material steel = Worn("Cell dull key steel", new Color(0.3f, 0.31f, 0.32f), 0.7f, 0.35f);

        var bunch = new GameObject(KeyBunch).transform;
        bunch.SetParent(hanging, false);

        // Split iron ring, the one the rope loop catches.
        const int links = 16;
        const float ringRadius = 0.085f;
        for (int i = 0; i < links; i++)
        {
            float a = i * Mathf.PI * 2f / links;
            var link = Primitive(PrimitiveType.Cylinder, bunch, "Ring wire", iron);
            link.localPosition = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * ringRadius;
            link.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg);
            link.localScale = new Vector3(0.014f, 0.018f, 0.014f);
        }

        // Five keys for five different cells, hanging off the lower half of the ring (lower in the world).
        var rng = new System.Random(5);
        Vector3 down = hanging.InverseTransformDirection(Vector3.down);
        float downAngle = Mathf.Atan2(down.y, down.x) * Mathf.Rad2Deg;
        float[] angles = { downAngle - 55f, downAngle - 27f, downAngle, downAngle + 27f, downAngle + 55f };
        for (int k = 0; k < angles.Length; k++)
        {
            float a = angles[k] * Mathf.Deg2Rad;
            Vector3 onRing = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * ringRadius;
            var holder = new GameObject("Cell key " + (k + 1)).transform;
            holder.SetParent(bunch, false);
            holder.localPosition = onRing;
            // Keys hang roughly downward, fanned a little, some turned edge-on.
            float hang = downAngle + (angles[k] - downAngle) * 0.35f + (float)(rng.NextDouble() * 10 - 5);
            holder.localRotation = Quaternion.Euler((float)(rng.NextDouble() * 50 - 25), (k % 2) * 35f, hang);
            float length = 0.13f + (float)rng.NextDouble() * 0.07f;
            Material m = k == 1 || k == 3 ? brass : k == 2 ? steel : iron;

            var bow = Primitive(PrimitiveType.Cylinder, holder, "Key bow", m);
            bow.localPosition = new Vector3(0.028f, 0f, 0f);
            bow.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bow.localScale = new Vector3(0.05f, 0.005f, 0.05f);
            var shaft = Primitive(PrimitiveType.Cube, holder, "Key shaft", m);
            shaft.localPosition = new Vector3(0.05f + length * 0.5f, 0f, 0f);
            shaft.localScale = new Vector3(length, 0.011f, 0.008f);
            int teeth = 2 + rng.Next(2);
            for (int t = 0; t < teeth; t++)
            {
                var tooth = Primitive(PrimitiveType.Cube, holder, "Key bit", m);
                float h = 0.016f + (float)rng.NextDouble() * 0.014f;
                tooth.localPosition = new Vector3(0.05f + length - 0.012f - t * 0.018f, -0.0055f - h * 0.5f, 0f);
                tooth.localScale = new Vector3(0.012f, h, 0.008f);
            }
        }
        // Small paper tag with a cell number, almost unreadable.
        var tag = Primitive(PrimitiveType.Cube, bunch, "Key tag", Load("EnvironmentKit/Materials/Canvas"));
        tag.localPosition = new Vector3(ringRadius * 0.7f, ringRadius * 0.75f, 0f);
        tag.localRotation = Quaternion.Euler(0f, 0f, 35f);
        tag.localScale = new Vector3(0.035f, 0.05f, 0.004f);
    }

    // ------------------------------------------------------------------ rope
    private static void Rope(Transform rod)
    {
        RemoveOld(rod, SheetRope);
        foreach (Renderer r in rod.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        Material sheet = Load("EnvironmentKit/Materials/Canvas");
        var rope = new GameObject(SheetRope).transform;
        rope.SetParent(rod, false);
        // Cancel the rod's stretched scale so the rope keeps round proportions.
        Vector3 s = rod.localScale;
        rope.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);

        // The old rod ran from local x -0.40 to +0.33 (times the stretch) with the hook at +0.37.
        float start = -0.40f * s.x, end = 0.33f * s.x;
        // Core of rolled sheet: the longest part, so the wake-up foot placement still finds the rope.
        var core = Primitive(PrimitiveType.Capsule, rope, "Rolled sheet core", sheet);
        core.localPosition = new Vector3((start + end) * 0.5f, 0.03f, 0f);
        core.localRotation = Quaternion.Euler(0f, 0f, 90f);
        core.localScale = new Vector3(0.042f, (end - start) * 0.5f, 0.036f);
        const float segment = 0.15f;
        var rng = new System.Random(3);
        int index = 0;
        for (float x = start; x < end; x += segment * 0.82f, index++)
        {
            var part = Primitive(PrimitiveType.Capsule, rope, "Twisted sheet", sheet);
            float wobble = Mathf.Sin(index * 1.7f) * 0.008f;
            part.localPosition = new Vector3(x + segment * 0.5f, 0.03f + wobble, Mathf.Cos(index * 1.3f) * 0.006f);
            part.localRotation = Quaternion.Euler(index * 37f % 360f, 0f, 90f + (float)(rng.NextDouble() * 10 - 5));
            float thick = index % 3 == 0 ? 0.064f : 0.052f;
            part.localScale = new Vector3(thick, segment * 0.62f, thick * 0.85f);
        }

        // Knots where the sheets were tied together.
        foreach (float k in new[] { 0.22f, 0.5f, 0.78f })
        {
            var knot = Primitive(PrimitiveType.Sphere, rope, "Knot", sheet);
            knot.localPosition = new Vector3(Mathf.Lerp(start, end, k), 0.035f, 0f);
            knot.localRotation = Quaternion.Euler(0f, 30f * k, 20f);
            knot.localScale = new Vector3(0.11f, 0.08f, 0.09f);
            var tail = Primitive(PrimitiveType.Capsule, rope, "Knot tail", sheet);
            tail.localPosition = knot.localPosition + new Vector3(0.03f, 0.025f, -0.03f);
            tail.localRotation = Quaternion.Euler(20f, 40f, 55f);
            tail.localScale = new Vector3(0.03f, 0.05f, 0.02f);
        }

        // Slip loop at the far end, where the hook used to be: it catches the key ring.
        Vector3 loop = new Vector3(0.37f * s.x, 0.1f, 0f);
        const int loopParts = 10;
        for (int i = 0; i < loopParts; i++)
        {
            float a = i * Mathf.PI * 2f / loopParts;
            var part = Primitive(PrimitiveType.Capsule, rope, "Loop", sheet);
            part.localPosition = loop + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.085f;
            part.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg);
            part.localScale = new Vector3(0.038f, 0.034f, 0.034f);
        }
        // Frayed end at the other side.
        for (int i = 0; i < 3; i++)
        {
            var fray = Primitive(PrimitiveType.Capsule, rope, "Frayed end", sheet);
            fray.localPosition = new Vector3(start - 0.04f, 0.03f + (i - 1) * 0.012f, (i - 1) * 0.015f);
            fray.localRotation = Quaternion.Euler(0f, (i - 1) * 25f, 90f + (i - 1) * 18f);
            fray.localScale = new Vector3(0.014f, 0.05f, 0.01f);
        }
    }

    // ------------------------------------------------------------------ helpers
    private static void RemoveOld(Transform parent, string name)
    {
        foreach (Transform child in parent.Cast<Transform>().Where(c => c.name == name).ToArray())
            Object.DestroyImmediate(child.gameObject);
    }

    private static Transform Primitive(PrimitiveType type, Transform parent, string name, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.On;
        return go.transform;
    }

    private static Material Load(string path)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/_Game/Art/{path}.mat");
        if (m == null) throw new InvalidOperationException($"Missing material {path}");
        return m;
    }

    private static Material Worn(string name, Color color, float metallic, float smoothness)
    {
        string path = $"{ArtFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_Color", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }
}
