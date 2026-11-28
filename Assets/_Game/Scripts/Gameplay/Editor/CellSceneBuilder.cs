using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class CellSceneBuilder
{
    private const string ScenePath = "Assets/_Game/Scenes/Levels/Cell.unity";
    private const string PalettePath = "Assets/_Game/Prefabs/Gameplay/CellBlockout";

    [InitializeOnLoadMethod]
    private static void QueueCreation()
    {
        EditorApplication.delayCall += CreateAfterImport;
    }

    private static void CreateAfterImport()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += CreateAfterImport;
            return;
        }

        CreateCell();
    }

    [MenuItem("Game Jam/Create Cell")]
    public static void CreateCell()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(ScenePath))
            return;

        Scene previous = SceneManager.GetActiveScene();
        Scene level = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(level);
            PopulateCell();
            if (!EditorSceneManager.SaveScene(level, ScenePath))
                throw new InvalidOperationException("Could not save the Cell scene.");
            Debug.Log($"Cell created at {ScenePath}.");
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded)
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(level, true);
            }
        }
    }

    private static void PopulateCell()
    {
        Material concrete = MaterialFor("Concrete", new Color(0.29f, 0.34f, 0.36f));
        Material steel = MaterialFor("Steel", new Color(0.22f, 0.25f, 0.26f));
        Material wood = MaterialFor("Wood", new Color(0.42f, 0.27f, 0.15f));
        Material cloth = MaterialFor("Cloth", new Color(0.42f, 0.47f, 0.41f));
        Material prisonerCloth = MaterialFor("Prisoner", new Color(0.67f, 0.41f, 0.27f));
        Material breadColor = MaterialFor("Bread", new Color(0.78f, 0.54f, 0.27f));
        Material brass = MaterialFor("Brass", new Color(0.82f, 0.63f, 0.26f));

        var architecture = Node("Cell Architecture");
        Box(architecture, "Floor", new Vector3(0.1f, -0.28f, 0.55f),
            new Vector3(14.4f, 0.56f, 3.5f), concrete, solid: true);
        Box(architecture, "Back Wall", new Vector3(0.1f, 2.08f, 2f),
            new Vector3(14.4f, 4.16f, 0.25f), concrete);
        Box(architecture, "Left Wall", new Vector3(-6.8f, 1.6f, 0f),
            new Vector3(0.36f, 3.2f, 2.3f), concrete, solid: true);
        Box(architecture, "Corridor End", new Vector3(7f, 1.6f, 0f),
            new Vector3(0.36f, 3.2f, 2.3f), concrete, solid: true);
        Box(architecture, "Overhead Beam", new Vector3(0.1f, 3.98f, 0.5f),
            new Vector3(14.4f, 0.24f, 3f), steel);
        foreach (float x in new[] { -6.5f, -3.1f, 0.3f, 3.7f, 6.9f })
        {
            Box(architecture, "Wall Rib", new Vector3(x, 1.95f, 1.78f),
                new Vector3(0.17f, 3.9f, 0.33f), steel);
        }

        var bunk = Node("Bunk", new Vector3(-5.55f, 0.38f, 1.19f));
        Box(bunk, "Frame", Vector3.zero, new Vector3(1.85f, 0.18f, 0.62f), steel);
        Box(bunk, "Mattress", new Vector3(0f, 0.17f, 0f),
            new Vector3(1.72f, 0.20f, 0.58f), cloth);
        Box(bunk, "Pillow", new Vector3(-0.6f, 0.29f, 0f),
            new Vector3(0.38f, 0.12f, 0.48f), cloth);

        var table = Node("Bread Table", new Vector3(-3.75f, 0f, 0.70f));
        Box(table, "Top", new Vector3(0f, 0.78f, 0f), new Vector3(0.95f, 0.13f, 0.62f), wood);
        Box(table, "Left Leg", new Vector3(-0.35f, 0.37f, 0f),
            new Vector3(0.13f, 0.74f, 0.52f), steel);
        Box(table, "Right Leg", new Vector3(0.35f, 0.37f, 0f),
            new Vector3(0.13f, 0.74f, 0.52f), steel);
        var breadOnTable = Bread(table, "Bread On Table", new Vector3(-0.06f, 0.95f, 0f), breadColor);

        var board = Node("Tilting Board", new Vector3(2.82f, 1.05f, 0.82f));
        Box(board, "Visual", Vector3.zero, new Vector3(2.55f, 0.12f, 0.46f), wood);
        Box(architecture, "Board Support", new Vector3(2.85f, 0.49f, 1.01f),
            new Vector3(0.23f, 0.98f, 0.31f), steel);
        var breadOnBoard = Bread(board, "Bread On Board", new Vector3(-0.86f, 0.16f, 0f), breadColor);
        breadOnBoard.gameObject.SetActive(false);
        var mouseOnBoard = Node("Mouse At Bread", new Vector3(-0.53f, 0.20f, 0.02f), board);

        var mouse = Node("Mouse", new Vector3(4.74f, 0.13f, 0.85f));
        var mouseVisual = Node("Visual", Vector3.zero, mouse);
        Shape(PrimitiveType.Sphere, mouseVisual, "Body", Vector3.zero,
            new Vector3(0.33f, 0.19f, 0.2f), cloth);
        Shape(PrimitiveType.Sphere, mouseVisual, "Head", new Vector3(-0.17f, 0.025f, 0f),
            new Vector3(0.17f, 0.14f, 0.16f), cloth);
        Box(mouseVisual, "Tail", new Vector3(0.225f, -0.03f, 0.02f),
            new Vector3(0.22f, 0.025f, 0.035f), wood);

        var key = Node("Cell Key", new Vector3(3.96f, 1.19f, 0.81f));
        var keyVisual = Node("Visual", Vector3.zero, key);
        Shape(PrimitiveType.Sphere, keyVisual, "Bow", new Vector3(-0.12f, 0f, 0f),
            new Vector3(0.16f, 0.17f, 0.09f), brass);
        Box(keyVisual, "Stem", new Vector3(0.03f, 0f, 0f),
            new Vector3(0.26f, 0.05f, 0.07f), brass);
        Box(keyVisual, "Tooth", new Vector3(0.14f, -0.038f, 0f),
            new Vector3(0.055f, 0.09f, 0.07f), brass);
        var keySlideEnd = Node("Board Drop Edge", new Vector3(1.65f, 1.19f, 0.81f));
        var keyLanding = Node("Key Landing", new Vector3(1.45f, 0.12f, 0.12f));

        var doorHinge = Node("Cell Door", new Vector3(3.43f, 0f, 1.25f));
        var doorVisual = Node("Visual", Vector3.zero, doorHinge);
        foreach (float z in new[] { -0.12f, -0.65f, -1.08f, -1.51f, -1.94f })
        {
            Box(doorVisual, "Door Bar", new Vector3(0f, 1.42f, z),
                new Vector3(0.22f, 2.83f, 0.10f), steel);
        }
        foreach (float y in new[] { 0.39f, 2.58f })
        {
            Box(doorVisual, "Door Brace", new Vector3(0f, y, -1.04f),
                new Vector3(0.24f, 0.13f, 2.02f), steel);
        }
        var doorHandle = Box(doorVisual, "Door Handle", new Vector3(-0.18f, 1.22f, -1.73f),
            new Vector3(0.23f, 0.12f, 0.11f), brass);
        var doorBlocker = Node("Door Blocker", new Vector3(3.43f, 1.40f, 0.03f))
            .gameObject.AddComponent<BoxCollider>();
        doorBlocker.size = new Vector3(0.28f, 2.8f, 2.55f);

        var exitZone = Node("Exit To Corridor", new Vector3(5.89f, 1.22f, 0f))
            .gameObject.AddComponent<BoxCollider>();
        exitZone.size = new Vector3(1.35f, 2.6f, 1.5f);
        exitZone.isTrigger = true;
        var sign = Node("Corridor Sign", new Vector3(5.88f, 2.28f, 1.82f))
            .gameObject.AddComponent<TextMesh>();
        sign.text = "CORRIDOR";
        sign.anchor = TextAnchor.MiddleCenter;
        sign.alignment = TextAlignment.Center;
        sign.fontSize = 36;
        sign.characterSize = 0.1f;
        sign.color = new Color(0.79f, 0.74f, 0.60f);

        SideViewPlayer player = CreatePlayer(prisonerCloth);
        Camera camera = CreateCamera(player.transform);
        LightCell();
        var puzzle = Node("Cell Puzzle").gameObject.AddComponent<CellPuzzle>();
        var properties = new SerializedObject(puzzle);
        Bind(properties, "player", player);
        Bind(properties, "levelCamera", camera);
        Bind(properties, "breadOnTable", breadOnTable);
        Bind(properties, "breadOnBoard", breadOnBoard);
        Bind(properties, "board", board);
        Bind(properties, "mouse", mouse);
        Bind(properties, "mouseOnBoard", mouseOnBoard);
        Bind(properties, "key", key);
        Bind(properties, "keySlideEnd", keySlideEnd);
        Bind(properties, "keyLanding", keyLanding);
        Bind(properties, "doorHinge", doorHinge);
        Bind(properties, "doorHandle", doorHandle);
        Bind(properties, "doorBlocker", doorBlocker);
        Bind(properties, "exitZone", exitZone);
        properties.ApplyModifiedPropertiesWithoutUndo();
    }

    private static SideViewPlayer CreatePlayer(Material cloth)
    {
        var root = Node("Player", new Vector3(-5.71f, 0.91f, 0f));
        var body = root.gameObject.AddComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        var collider = root.gameObject.AddComponent<CapsuleCollider>();
        collider.height = 1.8f;
        collider.radius = 0.33f;
        var player = root.gameObject.AddComponent<SideViewPlayer>();

        var visual = Node("Visual", Vector3.zero, root);
        Shape(PrimitiveType.Capsule, visual, "Body", new Vector3(0f, -0.21f, 0f),
            new Vector3(0.59f, 0.68f, 0.44f), cloth);
        Shape(PrimitiveType.Sphere, visual, "Head", new Vector3(0f, 0.62f, 0f),
            new Vector3(0.41f, 0.43f, 0.39f), cloth);
        return player;
    }

    private static Camera CreateCamera(Transform player)
    {
        var root = Node("Main Camera", new Vector3(-0.7f, 2.7f, -13f));
        root.rotation = Quaternion.Euler(5.5f, 0f, 0f);
        root.gameObject.tag = "MainCamera";
        var camera = root.gameObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 4.2f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.053f, 0.073f, 0.083f);
        camera.farClipPlane = 36f;
        root.gameObject.AddComponent<AudioListener>();
        var follow = root.gameObject.AddComponent<SideViewCamera>();
        var settings = new SerializedObject(follow);
        Bind(settings, "target", player);
        settings.FindProperty("constrainHorizontalPosition").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return camera;
    }

    private static void LightCell()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.34f, 0.38f, 0.41f);
        var fill = Node("Cold Fill", new Vector3(0f, 4f, -2f));
        fill.rotation = Quaternion.Euler(36f, -18f, 0f);
        var light = fill.gameObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.73f, 0.82f, 0.92f);
        light.intensity = 0.68f;
        PointLight("Bunk Lamp", new Vector3(-3.6f, 2.65f, -0.64f), 5f, 1.65f);
        PointLight("Board Lamp", new Vector3(2.54f, 2.68f, -0.83f), 5.2f, 1.85f);
    }

    private static void PointLight(string name, Vector3 position, float range, float intensity)
    {
        var light = Node(name, position).gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.77f, 0.56f);
        light.range = range;
        light.intensity = intensity;
    }

    private static Transform Bread(Transform parent, string name, Vector3 position, Material material)
    {
        var bread = Node(name, position, parent);
        Shape(PrimitiveType.Sphere, bread, "Visual", Vector3.zero,
            new Vector3(0.40f, 0.2f, 0.28f), material);
        return bread;
    }

    private static Transform Node(string name, Vector3 position = default, Transform parent = null)
    {
        var node = new GameObject(name).transform;
        node.SetParent(parent, false);
        node.localPosition = position;
        return node;
    }

    private static Transform Box(Transform parent, string name, Vector3 position, Vector3 scale,
        Material material, bool solid = false) =>
        Shape(PrimitiveType.Cube, parent, name, position, scale, material, solid);

    private static Transform Shape(PrimitiveType shape, Transform parent, string name, Vector3 position,
        Vector3 scale, Material material, bool solid = false)
    {
        var visual = GameObject.CreatePrimitive(shape);
        visual.name = name;
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = position;
        visual.transform.localScale = scale;
        visual.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid)
            Object.DestroyImmediate(visual.GetComponent<Collider>());
        return visual.transform;
    }

    private static Material MaterialFor(string name, Color color)
    {
        if (!AssetDatabase.IsValidFolder(PalettePath))
            AssetDatabase.CreateFolder("Assets/_Game/Prefabs/Gameplay", "CellBlockout");

        string path = $"{PalettePath}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;

        material = new Material(Shader.Find("Standard")) { color = color };
        material.SetFloat("_Glossiness", 0.14f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void Bind(SerializedObject owner, string field, Object reference)
    {
        owner.FindProperty(field).objectReferenceValue = reference;
    }
}
