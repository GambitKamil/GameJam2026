using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class CellVisualPolish
{
    private const string Kit = "Assets/_Game/Art/EnvironmentKit";
    private const string MeshPath = Kit + "/Meshes/CellRoundedMeshes.asset";
    private const string FabricPath = Kit + "/Materials/CellCanvas.png";
    private const string CellPath = "Assets/_Game/Scenes/Levels/Cell.unity";
    private static readonly Dictionary<string, float> RoundedProps = new()
    {
        ["Masonry"] = 0.015f, ["Paver"] = 0.004f, ["Skirting"] = 0.012f,
        ["Frame"] = 0.025f, ["Bed Frame"] = 0.03f, ["Bed Foot"] = 0.014f,
        ["Mattress"] = 0.071f, ["Pillow"] = 0.076f, ["Folded Blanket"] = 0.045f,
        ["Blanket Drop"] = 0.018f, ["Steel Worktop"] = 0.02f, ["Steel Tube Leg"] = 0.011f,
        ["Apron"] = 0.009f, ["Foot Plate"] = 0.008f, ["Stool Top"] = 0.028f,
        ["Stool Leg"] = 0.01f, ["Tank"] = 0.043f, ["Tank Lid"] = 0.017f,
        ["Wall Splashback"] = 0.016f, ["Gate Stile"] = 0.009f, ["Gate Rail"] = 0.01f,
        ["Lock Plate"] = 0.012f, ["Boot Near"] = 0.078f, ["Boot Far"] = 0.078f,
        ["Jacket Seam"] = 0.01f,
    };

    [MenuItem("Game Jam/Art/Apply Cell Visual Polish", true)]
    private static bool CanApply() => !EditorApplication.isPlayingOrWillChangePlaymode &&
        SceneManager.GetActiveScene().path == CellPath;

    [MenuItem("Game Jam/Art/Apply Cell Visual Polish")]
    private static void ApplyMenu() => Apply(SceneManager.GetActiveScene());

    public static void Apply(Scene scene)
    {
        if (scene.path != CellPath)
            throw new ArgumentException("Open the Cell scene before applying visual polish.", nameof(scene));

        Transform figure = scene.GetRootGameObjects().Single(root => root.name == "Player")
            .transform.Find("Hero Scale Preview");
        Transform head = RequiredPart(figure, "Head");
        Transform eye = RequiredPart(figure, "Eye");
        Transform hand = RequiredPart(figure, "Hand");
        Transform jacket = RequiredPart(figure, "Jacket");
        Transform sleeve = RequiredPart(figure, "Sleeve");
        Transform bootNear = RequiredPart(figure, "Boot Near");
        Transform bootFar = RequiredPart(figure, "Boot Far");
        Transform legNear = RequiredPart(figure, "Leg Near");
        Transform legFar = RequiredPart(figure, "Leg Far");

        Material skin = LoadMaterial("Skin");
        Material hair = LoadMaterial("Hair");
        Material leather = LoadMaterial("Leather");
        Material seam = LoadMaterial("UniformTrim");
        Material sole = LoadMaterial("BootSole");
        Mesh sphere = head.GetComponent<MeshFilter>().sharedMesh;
        var meshes = AssetDatabase.LoadAllAssetsAtPath(MeshPath).OfType<Mesh>()
            .ToDictionary(mesh => mesh.name);
        var parts = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true));
        int rounded = RoundVisuals(parts, meshes, true);

        Mesh limb = TailorLimb(sphere, meshes);
        foreach (Transform part in new[] { sleeve, legNear, legFar })
        {
            var filter = part.GetComponent<MeshFilter>();
            Undo.RecordObject(filter, "Fit Cell uniform");
            filter.sharedMesh = limb;
        }
        AddDetail(head, "Near Ear", sphere, skin, new Vector3(-0.23f, -0.03f, -0.463f),
            new Vector3(0.175f, 0.24f, 0.19f), -12f);
        AddDetail(head, "Far Ear", sphere, skin, new Vector3(-0.23f, -0.03f, 0.463f),
            new Vector3(0.175f, 0.24f, 0.19f), -12f);
        AddDetail(eye, "Brow", sphere, hair, new Vector3(0f, 0.84f, -0.07f),
            new Vector3(1.22f, 0.39f, 0.72f), -9f);
        AddDetail(hand, "Thumb", sphere, skin, new Vector3(0.34f, 0.1f, -0.04f),
            new Vector3(0.49f, 0.61f, 0.62f), -18f);
        AddDetail(jacket, "Chest Pocket", sphere, seam, new Vector3(0.22f, 0.14f, -0.415f),
            new Vector3(0.26f, 0.24f, 0.09f), -6f);
        AddDetail(sleeve, "Cuff", sphere, seam, new Vector3(0f, -0.408f, 0f),
            new Vector3(0.67f, 0.13f, 0.67f), 0f);
        foreach (Transform boot in new[] { bootNear, bootFar })
        {
            Vector3 size = Vector3.Scale(boot.lossyScale, new Vector3(0.97f, 0.23f, 0.94f));
            AddDetail(boot, "Outsole", RoundedMesh(meshes, size, 0.019f, 3), sole,
                new Vector3(0f, -0.385f, 0f), new Vector3(0.97f, 0.23f, 0.94f), 0f);
            AddDetail(boot, "Toe Leather", sphere, leather, new Vector3(0.23f, 0.15f, 0f),
                new Vector3(0.51f, 0.64f, 0.88f), 0f);
        }

        Light[] lights = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>()).ToArray();
        foreach (Light light in lights.Where(light => light.shadows == LightShadows.Soft))
        {
            Undo.RecordObject(light, "Soften Cell shadows");
            light.shadowStrength = Mathf.Min(light.shadowStrength, 0.64f);
            light.shadowResolution = UnityEngine.Rendering.LightShadowResolution.High;
        }

        if (!lights.Any(light => light.name == "Bunk Bounce"))
        {
            var bounce = new GameObject("Bunk Bounce", typeof(Light)).GetComponent<Light>();
            Undo.RegisterCreatedObjectUndo(bounce.gameObject, "Add Bunk Bounce");
            bounce.transform.position = new Vector3(-5.25f, 1.85f, -1.55f);
            bounce.type = LightType.Point;
            bounce.color = new Color32(255, 211, 174, 255);
            bounce.range = 4.1f;
            bounce.intensity = 0.95f;
            bounce.renderMode = LightRenderMode.ForcePixel;
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Cell Art] Rounded {rounded} cube visuals and fitted details to the animated figure.");
    }

    [MenuItem("Game Jam/Art/Regenerate Cell Material Textures")]
    public static void AuthorMaterials()
    {
        Texture2D fabric = MakeFabric();
        SetMaterial("Orange", new Color32(193, 112, 67, 255), 0.11f, fabric);
        SetMaterial("Skin", new Color32(187, 151, 124, 255), 0.14f);
        SetMaterial("Hair", new Color32(59, 47, 45, 255), 0.21f);
        SetMaterial("Leather", new Color32(65, 53, 46, 255), 0.27f);
        SetMaterial("UniformTrim", new Color32(133, 76, 48, 255), 0.09f, fabric);
        SetMaterial("BootSole", new Color32(43, 40, 39, 255), 0.15f);
        SetMaterial("Cloth", new Color32(105, 110, 95, 255), 0.10f, fabric, new Vector2(3f, 2f));
        SetMaterial("Canvas", new Color32(143, 136, 112, 255), 0.09f, fabric, new Vector2(2f, 2f));
        FinishWeathered("Stone", 0.69f, 0.016f, 0.22f);
        FinishWeathered("StoneLight", 0.68f, 0.015f, 0.21f);
        FinishWeathered("StoneDark", 0.73f, 0.017f, 0.20f);
        FinishWeathered("Floor", 0.62f, 0.007f, 0.20f);
        FinishWeathered("FloorDark", 0.64f, 0.008f, 0.23f);
        FinishWeathered("WornTableSteel", 0.55f, 0.011f, 0.35f);
        AssetDatabase.SaveAssets();
    }

    public static void ApplyPrefabMeshes()
    {
        var meshes = AssetDatabase.LoadAllAssetsAtPath(MeshPath).OfType<Mesh>()
            .ToDictionary(mesh => mesh.name);
        foreach (string name in new[] { "WallPanel", "FloorTile", "CellPartition", "SteelCellTable", "CellToilet", "CellSink" })
        {
            string path = $"{Kit}/{name}.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (RoundVisuals(root.GetComponentsInChildren<MeshFilter>(true), meshes, false) > 0)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        AssetDatabase.SaveAssets();
    }

    private static int RoundVisuals(IEnumerable<MeshFilter> parts, Dictionary<string, Mesh> meshes, bool recordUndo)
    {
        int rounded = 0;
        foreach (MeshFilter part in parts)
        {
            if (part.sharedMesh == null || part.sharedMesh.name != "Cube" ||
                !RoundedProps.TryGetValue(part.name, out float radius))
                continue;

            bool paver = part.name == "Paver";
            bool repeated = paver || part.name == "Masonry";
            // Keep the walking collider fixed and separate the pavers from the slab tint.
            Mesh mesh = RoundedMesh(meshes, part.transform.lossyScale, radius, repeated ? 1 : 3,
                paver ? 0.006f : 0f);
            if (recordUndo)
                Undo.RecordObject(part, "Round Cell visual mesh");
            part.sharedMesh = mesh;
            PrefabUtility.RecordPrefabInstancePropertyModifications(part);
            rounded++;
        }
        return rounded;
    }

    private static void SetMaterial(string name, Color color, float smoothness, Texture2D mainTexture = null,
        Vector2 tile = default)
    {
        string path = $"{Kit}/Materials/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader standard = Shader.Find("Standard");
        if (material == null)
        {
            material = new Material(standard) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = standard;
        material.color = color;
        material.SetFloat("_Glossiness", smoothness);
        material.mainTexture = mainTexture;
        material.mainTextureScale = tile == default ? Vector2.one : tile;
        EditorUtility.SetDirty(material);
    }

    private static void FinishWeathered(string name, float wear, float relief, float smoothness)
    {
        Material material = LoadMaterial(name);
        material.SetFloat("_Wear", wear);
        material.SetFloat("_Relief", relief);
        material.SetFloat("_Glossiness", smoothness);
        EditorUtility.SetDirty(material);
    }

    private static Texture2D MakeFabric()
    {
        const int resolution = 256;
        var pixels = new Color32[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            float u = x * 2f * Mathf.PI / resolution;
            float v = y * 2f * Mathf.PI / resolution;
            float slub = Mathf.Sin(u * 3f + v * 2f) * Mathf.Sin(v * 5f - u) * 0.035f;
            float wear = Mathf.Sin(u * 2f + 0.6f) * Mathf.Cos(v * 3f - u * 2f) * 0.06f;
            float weave = Mathf.Sin(u * 39f + v * 2f) * Mathf.Sin(v * 42f) * 0.035f;
            float twill = Mathf.Cos(u * 19f - v * 21f + Mathf.Sin(v * 4f) * 0.45f) * 0.024f;
            byte value = (byte)Mathf.Clamp(Mathf.RoundToInt((0.89f + slub + wear + weave + twill) * 255), 0, 255);
            pixels[y * resolution + x] = new Color32(value, value, value, 255);
        }

        var texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(FabricPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(FabricPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(FabricPath);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.mipmapEnabled = true;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(FabricPath);
    }

    private static Mesh RoundedMesh(Dictionary<string, Mesh> meshes, Vector3 size, float radius, int steps,
        float lift = 0f)
    {
        size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        var millimeters = new Vector3Int(Mathf.RoundToInt(size.x * 1000f), Mathf.RoundToInt(size.y * 1000f),
            Mathf.RoundToInt(size.z * 1000f));
        string name = $"Rounded {millimeters.x}x{millimeters.y}x{millimeters.z} r{Mathf.RoundToInt(radius * 1000f)} s{steps}";
        if (lift > 0f)
            name += $" lift{Mathf.RoundToInt(lift * 1000f)}";
        if (meshes.TryGetValue(name, out Mesh existing))
            return existing;

        size = (Vector3)millimeters * 0.001f;
        radius = Mathf.Min(radius, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.46f);
        var halfSize = size * 0.5f;
        var core = halfSize - Vector3.one * radius;
        var points = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        int count = 2 * (steps + 1);
        for (int axis = 0; axis < 3; axis++)
        for (int sign = -1; sign <= 1; sign += 2)
        {
            int across = (axis + 1) % 3;
            int up = (axis + 2) % 3;
            int start = points.Count;
            for (int row = 0; row < count; row++)
            for (int col = 0; col < count; col++)
            {
                var point = Vector3.zero;
                point[axis] = sign * halfSize[axis];
                point[across] = SampleEdge(col, steps, halfSize[across], radius);
                point[up] = SampleEdge(row, steps, halfSize[up], radius);
                var inner = new Vector3(Mathf.Clamp(point.x, -core.x, core.x),
                    Mathf.Clamp(point.y, -core.y, core.y), Mathf.Clamp(point.z, -core.z, core.z));
                Vector3 normal = (point - inner).normalized;
                Vector3 surface = inner + normal * radius;
                points.Add(new Vector3(surface.x / size.x, (surface.y + lift) / size.y, surface.z / size.z));
                normals.Add(Vector3.Scale(normal, size).normalized);
                uvs.Add(new Vector2(point[across] / size[across] + 0.5f, point[up] / size[up] + 0.5f));
            }
            for (int row = 0; row < count - 1; row++)
            for (int col = 0; col < count - 1; col++)
            {
                int first = start + row * count + col;
                int second = first + 1;
                int third = first + count;
                int fourth = third + 1;
                triangles.Add(first);
                triangles.Add(sign > 0 ? second : third);
                triangles.Add(sign > 0 ? third : second);
                triangles.Add(second);
                triangles.Add(sign > 0 ? fourth : third);
                triangles.Add(sign > 0 ? third : fourth);
            }
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(points);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        StoreMesh(meshes, mesh);
        return mesh;
    }

    private static Mesh TailorLimb(Mesh sphere, Dictionary<string, Mesh> meshes)
    {
        const string name = "Uniform Sleeve and Leg";
        if (meshes.TryGetValue(name, out Mesh existing))
            return existing;

        Mesh mesh = Object.Instantiate(sphere);
        mesh.name = name;
        Vector3[] points = mesh.vertices;
        Vector3[] normals = mesh.normals;
        const float amount = 0.53f;
        for (int i = 0; i < points.Length; i++)
        {
            float y = points[i].y;
            float stretch = 1f - amount + amount * 0.5f * Mathf.PI * Mathf.Cos(y * Mathf.PI);
            points[i].y = Mathf.Lerp(y, 0.5f * Mathf.Sin(y * Mathf.PI), amount);
            normals[i].y /= stretch;
            normals[i].Normalize();
        }
        mesh.vertices = points;
        mesh.normals = normals;
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        StoreMesh(meshes, mesh);
        return mesh;
    }

    private static void StoreMesh(Dictionary<string, Mesh> meshes, Mesh mesh)
    {
        if (meshes.Count == 0)
            AssetDatabase.CreateAsset(mesh, MeshPath);
        else
            AssetDatabase.AddObjectToAsset(mesh, MeshPath);
        meshes.Add(mesh.name, mesh);
    }

    private static float SampleEdge(int index, int steps, float extent, float radius) => index <= steps
        ? -extent + radius * index / steps
        : extent - radius * (2 * steps + 1 - index) / steps;

    private static void AddDetail(Transform parent, string name, Mesh mesh, Material material,
        Vector3 position, Vector3 scale, float angle)
    {
        if (parent.Find(name) != null)
            return;

        var detail = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { layer = parent.gameObject.layer };
        Undo.RegisterCreatedObjectUndo(detail, "Add Cell art detail");
        detail.transform.SetParent(parent, false);
        detail.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
        detail.transform.localScale = scale;
        detail.GetComponent<MeshFilter>().sharedMesh = mesh;
        detail.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static Transform RequiredPart(Transform parent, string name) => parent != null && parent.Find(name) is Transform part
        ? part : throw new InvalidOperationException($"The active Cell scene is missing its {name} visual.");

    private static Material LoadMaterial(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{Kit}/Materials/{name}.mat");
}
