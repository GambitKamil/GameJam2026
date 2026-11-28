using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Builds Assets/_Game/Scenes/Levels/Kitchen_Ventilation.unity from scratch.
// Menu: Game Jam / Build Kitchen Ventilation. Rebuilding replaces the scene file.
//
// Route (side view, X to the right, floor y = 0, upper floor y = 0.9):
//   -3.8  freezer door      -> prisoner walks out
//    3.2  freight cart      (safe zone: no camera, lamps cycle 5 lit / 1 warning / 3 dark)
//   16.5  camera 1          (watches the flat end and the ramp)
//   18-24 ramp up 0.9 m     (an unbraked cart rolls back)
//   25.6  lever B (launch)  (blind spot behind the duct)
//   31.0  camera 2          (watches the upper floor and the lift)
//   31.9  lever A (power)
//   33.6-37.0 lift platform -> rises 6.9 m to the upper floor
public static class BuildKitchenVentilationScene
{
    public const string ScenePath = "Assets/_Game/Scenes/Levels/Kitchen_Ventilation.unity";
    public const string SceneName = "Kitchen_Ventilation";
    private const string Corridor = "Assets/_Game/Scenes/Sandbox/Corridor.unity";
    private const string ArtFolder = "Assets/_Game/Art/KitchenVentilation";

    private const float Upper = 0.9f;
    private const float RampStart = 18f, RampEnd = 24f;
    private const float LiftLeft = 33.6f, LiftRight = 37f;
    private const float LiftCenter = (LiftLeft + LiftRight) * 0.5f;
    // A tall, deep hall: high ceilings and the back wall well behind the walking lane.
    private const float LowCeiling = 4.3f, HighCeiling = 5.5f;
    private const float LeftEnd = -5.2f, RightEnd = 40.4f;
    private const float WallZ = 3.0f;
    private const float WallShift = WallZ - 1.7f;           // dressing that used to sit on the old wall
    private const float FrontZ = -1.7f;
    private const float Depth = WallZ + 0.3f - FrontZ;
    private const float DepthCenter = (WallZ + 0.3f + FrontZ) * 0.5f;
    private const float ConsoleZ = 1.15f;                    // control posts beside the lane (levers, lift box)

    private sealed class Kit
    {
        public Material Concrete, Floor, DarkSteel, Steel, Iron, Grate, Foreground, Wash, Haze, Ochre,
            ColdSteel, Frost, Canvas, Trim, Dark, LampGlass, RedLens, GreenLens, AmberLens, Cone, Particle, Spark, Halo, Smoke;
        public Mesh Shade;
        public Font Font;
        public Material Text;
    }

    [MenuItem("Game Jam/Build Kitchen Ventilation")]
    public static void BuildFromMenu()
    {
        string result = Build();
        Debug.Log(result);
    }

    public static string Build()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Stop Play Mode before building the ventilation scene.");

        Kit kit = LoadKit();

        // Reuse the corridor prisoner and camera so controls and animation match the earlier levels.
        Scene reference = EditorSceneManager.OpenScene(Corridor, OpenSceneMode.Single);
        var corridorPlayer = Object.FindFirstObjectByType<SideViewPlayer>();
        var corridorCamera = Camera.main;
        if (corridorPlayer == null || corridorCamera == null)
            throw new InvalidOperationException("Corridor player and camera are required.");
        GameObject hero = Object.Instantiate(corridorPlayer.gameObject);
        GameObject view = Object.Instantiate(corridorCamera.gameObject);
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.MoveGameObjectToScene(hero, scene);
        SceneManager.MoveGameObjectToScene(view, scene);
        SceneManager.SetActiveScene(scene);
        EditorSceneManager.CloseScene(reference, true);

        hero.name = "Prisoner";
        var capsule = hero.GetComponent<CapsuleCollider>();
        float soleOffset = capsule.center.y - capsule.height * 0.5f;
        hero.transform.position = new Vector3(-3.8f, -soleOffset + 0.03f, 0f);
        Set(hero.GetComponent<SideViewPlayer>(), "runMultiplier", 1.6f);
        Set(hero.GetComponent<SideViewPlayer>(), "jumpSpeed", 4.1f);
        var fill = hero.GetComponentsInChildren<Light>().FirstOrDefault(l => l.name == "Prisoner fill");
        if (fill != null)
        {
            fill.intensity = 0.55f;
            fill.range = 2.6f;
            fill.renderMode = LightRenderMode.ForcePixel;
        }

        view.name = "Main Camera";
        view.transform.position = new Vector3(0.8f, 5.2f, -13.6f);
        view.transform.rotation = Quaternion.Euler(12.5f, 0f, 0f);
        var cam = view.GetComponent<Camera>();
        cam.fieldOfView = 28f;
        cam.backgroundColor = new Color(0.012f, 0.015f, 0.02f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        var rig = view.GetComponent<SideViewCamera>();
        Link(rig, "target", hero.transform);
        Link(rig, "framingSubject", null);
        Set(rig, "constrainHorizontalPosition", true);
        SetVector2(rig, "horizontalLimits", new Vector2(LeftEnd + 6f, RightEnd - 6f));
        Set(rig, "followHeight", true);
        Set(rig, "lookAhead", 0.6f);

        ConfigureLighting();

        // ---------------------------------------------------------------- architecture
        var shell = Group("Architecture", null);
        var washes = BuildShell(shell, kit);
        Transform freezerDoor = BuildFreezerExit(shell, kit);
        BuildDressing(Group("Pipes, cables and grates", null), kit);

        // ---------------------------------------------------------------- lamps
        var lampRoot = Group("Steam lights (cycle)", null);
        var audioRootEarly = Group("Sound", null);
        var lamps = new List<Light>();
        var glass = new List<Renderer>();
        var hazes = new List<Renderer>();
        var halos = new List<Renderer>();
        var bulbs = new List<Transform>();
        var bursts = new List<SteamGlow>();
        float[] lampXs = { -0.5f, 4.5f, 9.5f, 14.5f, 19.5f, 25.8f, 30f, 38.8f };
        foreach (float x in lampXs)
        {
            float ceiling = x < RampEnd ? LowCeiling : HighCeiling;
            bool shadows = Mathf.Approximately(x, 14.5f) || Mathf.Approximately(x, 30f);
            bulbs.Add(SteamLight(lampRoot, x, ceiling, shadows, kit, lamps, glass, hazes, halos, bursts, audioRootEarly));
        }
        var audioRoot = audioRootEarly;
        var shaftLights = new GameObject("Shaft lighting cycle").AddComponent<FlickeringLightController>();
        LinkArray(shaftLights, "lamps", lamps.ToArray());
        LinkArray(shaftLights, "lampGlass", glass.ToArray());
        LinkArray(shaftLights, "hazes", hazes.ToArray());
        LinkArray(shaftLights, "washes", washes);
        LinkArray(shaftLights, "halos", halos.ToArray());
        foreach (var burst in bursts)
            Link(burst, "lights", shaftLights);
        Link(shaftLights, "buzz", Source(audioRoot, "Lamp buzz", null, true, 0.16f));
        Link(shaftLights, "flicker", Source(audioRoot, "Lamp flicker", null, false, 0.5f));
        BuildWallDevices(Group("Wall devices", null), kit);

        // Steady practical lights that are not on the shaft circuit.
        Point(shell, "Freezer spill", new Vector3(-4.2f, 2.2f, 0.6f), new Color(0.55f, 0.68f, 0.86f), 0.5f, 3.2f);
        Point(shell, "Upper landing lamp", new Vector3(38.6f, Upper + 6.9f + 2.2f, 0.6f), new Color(0.6f, 0.7f, 0.85f), 0.9f, 5f);

        // ---------------------------------------------------------------- cart
        CartController cart = BuildCart(kit, out CartHidingController hiding, audioRoot);

        // ---------------------------------------------------------------- levers and lift
        ControlPost(31.9f, kit);
        ControlPost(25.6f, kit);
        ControlPost(LiftLeft - 0.75f, kit);
        LeverController leverA = BuildLever("Lever A - Power supply", "Restore lift power", new Vector3(31.9f, Upper + 1.15f, ConsoleZ - 0.22f), false, kit, audioRoot);
        LeverController leverB = BuildLever("Lever B - Lift control", "Start the lift", new Vector3(25.6f, Upper + 1.15f, ConsoleZ - 0.22f), true, kit, audioRoot);
        var lift = BuildLift(kit, audioRoot, out Light powerLamp, out Renderer powerLens, out Light statusLamp, out Renderer statusLens, out Light panelLamp);
        BuildCables(kit);

        // ---------------------------------------------------------------- cameras
        var alarmObject = new GameObject("Alarm");
        var alarm = alarmObject.AddComponent<AlarmController>();
        var cam1 = BuildSecurityCamera("Security camera 1", new Vector3(16.5f, LowCeiling - 0.32f, -0.25f), 0f, 58f, 11f, 0f, 20f, 7.5f, kit);
        var cam2 = BuildSecurityCamera("Security camera 2", new Vector3(31f, HighCeiling - 0.32f, -0.25f), 8f, 58f, 12f, 0.35f, 19f, 8f, kit);
        var cameras = new[] { cam1, cam2 };

        // ---------------------------------------------------------------- steam
        SteamVentAt(new Vector3(6.4f, 2.62f, 1.38f + WallShift), kit, audioRoot);
        SteamVentAt(new Vector3(21.2f, 2.86f, 1.38f + WallShift), kit, audioRoot);
        SteamVentAt(new Vector3(29.6f, 3.72f, 1.38f + WallShift), kit, audioRoot);

        // ---------------------------------------------------------------- systems
        var hud = BuildHud(kit);
        var systems = new GameObject("Ventilation level");
        var actions = systems.AddComponent<VentilationPlayerActions>();
        var checkpoints = systems.AddComponent<CheckpointManager>();
        var elevator = systems.AddComponent<ElevatorController>();
        var transition = systems.AddComponent<SceneTransition>();
        var entrance = new GameObject("Level entrance").AddComponent<LevelEntrance>();
        var vignette = new GameObject("Detection vignette").AddComponent<DetectionVignette>();

        var player = hero.GetComponent<SideViewPlayer>();
        Link(hiding, "player", player);

        Link(actions, "player", player);
        Link(actions, "cart", cart);
        Link(actions, "hiding", hiding);
        LinkArray(actions, "levers", new Object[] { leverA, leverB });
        Link(actions, "hud", hud);
        Link(actions, "freezerDoor", freezerDoor);
        Link(actions, "entrance", entrance);
        Set(actions, "walkOutToX", -1.6f);
        Link(actions, "doorSound", Source(audioRoot, "Freezer door", Clip("Kitchen/FreezerDoor"), false, 0.7f));
        Link(actions, "footsteps", Source(audioRoot, "Footsteps", null, false, 0.5f));

        var markers = Group("Checkpoints", null);
        Transform cp0 = Group("Checkpoint 0 - freezer door", markers, new Vector3(-1.6f, 0f, 0f));
        Transform cp1 = Group("Checkpoint 1 - before the camera", markers, new Vector3(8.5f, 0f, 0f));
        Transform cp2 = Group("Checkpoint 2 - top of the ramp", markers, new Vector3(24.8f, Upper, 0f));
        LinkArray(checkpoints, "checkpoints", new Object[] { cp0, cp1, cp2 });
        Link(checkpoints, "player", player);
        Link(checkpoints, "cart", cart);
        Link(checkpoints, "hiding", hiding);
        Link(checkpoints, "powerLever", leverA);
        Link(checkpoints, "elevator", elevator);
        Link(checkpoints, "shaftLights", shaftLights);
        LinkArray(checkpoints, "cameras", cameras);
        Link(checkpoints, "actions", actions);
        Link(checkpoints, "hud", hud);

        foreach (var c in cameras)
        {
            Link(c, "lights", shaftLights);
            Link(c, "player", player);
            Link(c, "hiding", hiding);
            Link(c, "alarm", alarm);
        }

        var beacons = new[]
        {
            Point(alarmObject.transform, "Alarm beacon low", new Vector3(12f, LowCeiling - 0.25f, 0.9f), new Color(0.9f, 0.1f, 0.06f), 1.6f, 6f),
            Point(alarmObject.transform, "Alarm beacon high", new Vector3(28.5f, HighCeiling - 0.25f, 0.9f), new Color(0.9f, 0.1f, 0.06f), 1.6f, 6f),
            Point(alarmObject.transform, "Alarm beacon lift", new Vector3(LiftCenter, 6.6f, 0.9f), new Color(0.9f, 0.1f, 0.06f), 1.8f, 7f),
        };
        Link(alarm, "checkpoints", checkpoints);
        Link(alarm, "shaftLights", shaftLights);
        Link(alarm, "actions", actions);
        LinkArray(alarm, "beacons", beacons);
        Link(alarm, "siren", Source(audioRoot, "Siren", null, true, 0.45f));
        var guards = Source(audioRoot, "Guards below", null, false, 0.8f);
        guards.transform.position = new Vector3(LiftCenter - 6f, -2f, 0f);
        Link(alarm, "guards", guards);
        Link(alarm, "vignette", vignette);
        LinkArray(alarm, "cameras", cameras);

        Link(elevator, "platform", lift);
        Set(elevator, "platformHalfWidth", (LiftRight - LiftLeft) * 0.5f);
        Link(elevator, "powerLever", leverA);
        Link(elevator, "launchLever", leverB);
        Link(elevator, "shaftLights", shaftLights);
        LinkArray(elevator, "cameras", cameras);
        Link(elevator, "alarm", alarm);
        Link(elevator, "cart", cart);
        Link(elevator, "hiding", hiding);
        Link(elevator, "player", player);
        Link(elevator, "actions", actions);
        Link(elevator, "hud", hud);
        Link(elevator, "transition", transition);
        Link(elevator, "powerLamp", powerLamp);
        Link(elevator, "powerLens", powerLens);
        Link(elevator, "statusLamp", statusLamp);
        Link(elevator, "statusLens", statusLens);
        Link(elevator, "launchPanelLamp", panelLamp);
        Link(elevator, "motor", Source(audioRoot, "Lift motor", Clip("Kitchen/LaundryMotor"), true, 0f));
        Link(elevator, "panel", Source(audioRoot, "Lift panel", null, false, 0.8f));

        Link(transition, "hud", hud);

        // Ventilation hum from the existing freezer compressor loop, pitched down.
        var hum = Source(audioRoot, "Ventilation hum", Clip("Kitchen/FreezerRoom"), true, 0.3f);
        hum.pitch = 0.72f;
        hum.playOnAwake = true;

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("Kitchen_Ventilation scene did not save.");
        AddToBuildSettings();
        return $"Built {ScenePath}";
    }

    // ==================================================================== lighting
    private static void ConfigureLighting()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        // Darker than the corridor recipe on purpose: the shaft is lit by lamp pools, not by ambient.
        RenderSettings.ambientSkyColor = new Color(0.135f, 0.155f, 0.18f);
        RenderSettings.ambientEquatorColor = new Color(0.095f, 0.108f, 0.13f);
        RenderSettings.ambientGroundColor = new Color(0.032f, 0.036f, 0.044f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 10f;
        RenderSettings.fogEndDistance = 28f;
        RenderSettings.fogColor = new Color(0.07f, 0.085f, 0.1f);
        var light = new GameObject("Back light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.55f, 0.68f, 0.86f);
        light.intensity = 0.28f;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(26f, 167f, 0f);
        RenderSettings.sun = light;
        Lightmapping.lightingDataAsset = null;
    }

    // ==================================================================== shell
    private static Renderer[] BuildShell(Transform root, Kit k)
    {
        var washes = new List<Renderer>();
        // Floors are cut into short pieces: each renderer only takes the four nearest lights.
        float[] lowCuts = { LeftEnd, -1f, 3f, 8f, 13f, RampStart };
        for (int i = 0; i < lowCuts.Length - 1; i++)
            Slab(root, "Floor", lowCuts[i], lowCuts[i + 1], -0.6f, 0f, k.Floor);

        // Ramp: one tilted slab from (18, 0) to (24, 0.9) plus a solid base under it.
        float run = RampEnd - RampStart;
        float angle = Mathf.Atan2(Upper, run) * Mathf.Rad2Deg;
        float length = Mathf.Sqrt(run * run + Upper * Upper);
        Vector3 normal = Quaternion.Euler(0f, 0f, angle) * Vector3.up;
        Vector3 mid = new Vector3((RampStart + RampEnd) * 0.5f, Upper * 0.5f, 0f) - normal * 0.25f;
        mid.z = DepthCenter;
        var ramp = Box(root, "Ramp", mid, new Vector3(length + 0.04f, 0.5f, Depth), k.Floor, true);
        ramp.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        Box(root, "Ramp base", new Vector3((RampStart + RampEnd) * 0.5f, -0.3f, DepthCenter), new Vector3(run, 0.6f, Depth), k.Concrete, false);
        Box(root, "Ramp hazard edge", new Vector3((RampStart + RampEnd) * 0.5f, Upper * 0.5f + 0.01f, -1.62f) + Vector3.zero,
            new Vector3(length, 0.04f, 0.08f), k.Ochre, false).transform.rotation = Quaternion.Euler(0f, 0f, angle);

        float[] highCuts = { RampEnd, 29f, LiftLeft };
        for (int i = 0; i < highCuts.Length - 1; i++)
            Slab(root, "Upper floor", highCuts[i], highCuts[i + 1], -0.6f, Upper, k.Floor);
        Slab(root, "Upper floor", LiftRight, RightEnd + 0.4f, -0.6f, Upper, k.Floor);

        // Lift pit under the platform.
        Box(root, "Lift pit floor", new Vector3(LiftCenter, -2.2f, DepthCenter), new Vector3(LiftRight - LiftLeft, 0.3f, Depth), k.Concrete, true);
        Box(root, "Lift pit wall left", new Vector3(LiftLeft - 0.1f, -0.9f, DepthCenter), new Vector3(0.2f, 2.9f, Depth), k.Concrete, true);
        Box(root, "Lift pit wall right", new Vector3(LiftRight + 0.1f, -0.9f, DepthCenter), new Vector3(0.2f, 2.9f, Depth), k.Concrete, true);

        // Ceilings: low over the flat run and ramp, high over the upper floor, open over the lift.
        Box(root, "Low ceiling", new Vector3((LeftEnd + RampEnd) * 0.5f, LowCeiling + 0.2f, DepthCenter), new Vector3(RampEnd - LeftEnd, 0.4f, Depth + 0.2f), k.Concrete, true);
        Box(root, "Ceiling step", new Vector3(RampEnd + 0.1f, (LowCeiling + HighCeiling) * 0.5f + 0.2f, DepthCenter), new Vector3(0.2f, HighCeiling - LowCeiling + 0.4f, Depth + 0.2f), k.Concrete, true);
        Box(root, "High ceiling", new Vector3((RampEnd + LiftLeft - 0.2f) * 0.5f, HighCeiling + 0.2f, DepthCenter), new Vector3(LiftLeft - 0.2f - RampEnd, 0.4f, Depth + 0.2f), k.Concrete, true);
        Box(root, "High ceiling right", new Vector3((LiftRight + 0.2f + RightEnd + 0.4f) * 0.5f, HighCeiling + 0.2f, DepthCenter), new Vector3(RightEnd + 0.2f - LiftRight, 0.4f, Depth + 0.2f), k.Concrete, true);

        // Back wall in pieces, with a dado of darker steel.
        for (float x = LeftEnd; x < RightEnd; x += 4f)
        {
            float to = Mathf.Min(RightEnd, x + 4f);
            float floor = x >= RampEnd ? Upper : 0f;
            float top = x >= RampEnd ? HighCeiling : LowCeiling;
            if (x >= LiftLeft - 0.5f && x < LiftRight + 0.5f) top = 12f;
            Box(root, "Back wall", new Vector3((x + to) * 0.5f, (floor + top) * 0.5f - 0.3f, WallZ + 0.15f), new Vector3(to - x, top - floor + 0.6f, 0.3f), k.Concrete, true);
            Box(root, "Dado", new Vector3((x + to) * 0.5f, floor + 0.45f, WallZ - 0.02f), new Vector3(to - x, 0.9f, 0.05f), k.DarkSteel, false);
        }
        // Back wall continues behind the lift shaft up to the upper floor.
        Box(root, "Shaft back wall", new Vector3(LiftCenter, 6f, WallZ + 0.15f), new Vector3(LiftRight - LiftLeft + 1.4f, 12f, 0.3f), k.Concrete, false);

        // End walls.
        Box(root, "Freezer back wall", new Vector3(LeftEnd - 0.2f, 1.6f, DepthCenter), new Vector3(0.4f, 3.8f, Depth + 0.2f), k.ColdSteel, true);
        Box(root, "Right end wall", new Vector3(RightEnd + 0.2f, 3f, DepthCenter), new Vector3(0.4f, 7f, Depth + 0.2f), k.Concrete, true);

        // Lift shaft frame and the landing it rises to.
        float landing = Upper + 6.9f;
        Box(root, "Shaft column left", new Vector3(LiftLeft - 0.18f, 6f, 1.2f), new Vector3(0.22f, 12f, 0.22f), k.DarkSteel, false);
        Box(root, "Shaft column right", new Vector3(LiftRight + 0.18f, 6f, 1.2f), new Vector3(0.22f, 12f, 0.22f), k.DarkSteel, false);
        Box(root, "Shaft column left front", new Vector3(LiftLeft - 0.18f, 6f, -1.3f), new Vector3(0.22f, 12f, 0.22f), k.DarkSteel, false);
        Box(root, "Shaft column right front", new Vector3(LiftRight + 0.18f, 6f, -1.3f), new Vector3(0.22f, 12f, 0.22f), k.DarkSteel, false);
        for (float y = HighCeiling + 1.2f; y < 11.5f; y += 1.6f)
        {
            Box(root, "Shaft brace", new Vector3(LiftLeft - 0.18f, y, -0.05f), new Vector3(0.08f, 0.08f, 2.5f), k.Iron, false);
            Box(root, "Shaft brace", new Vector3(LiftRight + 0.18f, y, -0.05f), new Vector3(0.08f, 0.08f, 2.5f), k.Iron, false);
        }
        Box(root, "Floor slab above", new Vector3((RampEnd + LiftLeft) * 0.5f, (HighCeiling + landing) * 0.5f + 0.2f, DepthCenter), new Vector3(LiftLeft - RampEnd, landing - HighCeiling, Depth + 0.2f), k.Dark, false);
        Box(root, "Upper landing", new Vector3((LiftRight + RightEnd) * 0.5f + 0.2f, landing - 0.15f, DepthCenter), new Vector3(RightEnd - LiftRight + 0.4f, 0.3f, Depth), k.Concrete, false);
        Box(root, "Upper landing slab", new Vector3((LiftRight + RightEnd) * 0.5f + 0.2f, (HighCeiling + landing) * 0.5f + 0.25f, DepthCenter), new Vector3(RightEnd - LiftRight + 0.4f, landing - HighCeiling - 0.3f, Depth + 0.2f), k.Dark, false);
        Label(root, "LEVEL 5", new Vector3(LiftRight + 0.9f, landing + 1.6f, WallZ - 0.02f), 0.09f, k, new Color(0.55f, 0.6f, 0.66f, 0.35f));
        Label(root, "LIFT  B-2", new Vector3(LiftCenter, HighCeiling - 0.55f, WallZ - 0.02f), 0.075f, k, new Color(0.55f, 0.6f, 0.66f, 0.3f));
        Label(root, "VENT SHAFT 3", new Vector3(2.4f, 2.5f, WallZ - 0.02f), 0.04f, k, new Color(0.55f, 0.6f, 0.66f, 0.28f));

        // Wall washes: lamp spill on the back wall, alternating strong and faint.
        int n = 0;
        for (float x = -2f; x < RightEnd - 1f; x += 4.5f, n++)
        {
            float floor = x >= RampEnd ? Upper : 0f;
            var wash = GameObject.CreatePrimitive(PrimitiveType.Quad);
            wash.name = "Wall wash";
            Object.DestroyImmediate(wash.GetComponent<Collider>());
            wash.transform.position = new Vector3(x, floor + 1.7f, WallZ - 0.04f);
            float s = n % 2 == 0 ? 3.2f : 2.2f;
            wash.transform.localScale = new Vector3(s, s * 0.8f, 1f);
            var r = wash.GetComponent<Renderer>();
            r.sharedMaterial = k.Wash;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            wash.transform.SetParent(root, true);
            washes.Add(r);
        }
        return washes.ToArray();
    }

    private static void Slab(Transform root, string name, float from, float to, float bottom, float top, Material m)
    {
        Box(root, name, new Vector3((from + to) * 0.5f, (bottom + top) * 0.5f, DepthCenter), new Vector3(to - from, top - bottom, Depth), m, true);
    }

    private static Transform BuildFreezerExit(Transform root, Kit k)
    {
        // The freezer the prisoner leaves: a frosted box behind a heavy vertical door.
        var freezer = Group("Freezer exit", root);
        Box(freezer, "Freezer floor frost", new Vector3(-4.2f, 0.01f, 0.6f), new Vector3(1.6f, 0.02f, 1.2f), k.ColdSteel, false);
        Box(freezer, "Door header", new Vector3(-2.75f, (2.72f + LowCeiling) * 0.5f, DepthCenter), new Vector3(0.5f, LowCeiling - 2.72f, Depth), k.DarkSteel, true);
        Box(freezer, "Freezer front wall", new Vector3(-2.75f, LowCeiling * 0.5f, (1.2f + WallZ + 0.3f) * 0.5f), new Vector3(0.5f, LowCeiling, WallZ + 0.3f - 1.2f), k.ColdSteel, true);
        Box(freezer, "Freezer ceiling", new Vector3(-4.2f, LowCeiling - 0.15f, DepthCenter), new Vector3(2.4f, 0.3f, Depth), k.ColdSteel, false);
        Box(freezer, "Door hazard stripe", new Vector3(-2.48f, 2.62f, 0.2f), new Vector3(0.04f, 0.1f, 2.4f), k.Ochre, false);
        var door = Group("Freezer door", freezer, new Vector3(-2.75f, 0f, 0f));
        Box(door, "Insulated door slab", new Vector3(0f, 1.36f, 0f), new Vector3(0.3f, 2.72f, 2.4f), k.ColdSteel, true, true);
        Box(door, "Door rib", new Vector3(0.17f, 0.6f, 0f), new Vector3(0.05f, 0.12f, 2.3f), k.Steel, false, true);
        Box(door, "Door rib", new Vector3(0.17f, 1.4f, 0f), new Vector3(0.05f, 0.12f, 2.3f), k.Steel, false, true);
        Box(door, "Door rib", new Vector3(0.17f, 2.2f, 0f), new Vector3(0.05f, 0.12f, 2.3f), k.Steel, false, true);
        Box(door, "Door handle", new Vector3(0.22f, 1.1f, -0.7f), new Vector3(0.06f, 0.5f, 0.06f), k.Steel, false, true);
        Label(freezer, "COLD STORE", new Vector3(-2.47f, 2.4f, -0.4f), 0.05f, k, new Color(0.6f, 0.66f, 0.72f, 0.3f)).transform.rotation = Quaternion.Euler(0f, -90f, 0f);
        return door;
    }

    private static void BuildDressing(Transform root, Kit k)
    {
        // Long ducts and pipes on the back wall.
        Pipe(root, "Main duct", new Vector3(LeftEnd, 2.85f, 1.25f + WallShift), new Vector3(RampEnd - 0.4f, 2.85f, 1.25f + WallShift), 0.24f, k.Steel);
        Pipe(root, "Main duct high", new Vector3(RampEnd - 0.4f, 2.85f, 1.25f + WallShift), new Vector3(RampEnd + 0.4f, 3.8f, 1.25f + WallShift), 0.24f, k.Steel);
        Pipe(root, "Main duct high", new Vector3(RampEnd + 0.4f, 3.8f, 1.25f + WallShift), new Vector3(LiftLeft - 0.6f, 3.8f, 1.25f + WallShift), 0.24f, k.Steel);
        Pipe(root, "Steam line", new Vector3(LeftEnd, 2.6f, 1.45f + WallShift), new Vector3(RampEnd - 0.6f, 2.6f, 1.45f + WallShift), 0.07f, k.Iron);
        Pipe(root, "Steam line", new Vector3(RampEnd - 0.6f, 2.6f, 1.45f + WallShift), new Vector3(RampEnd + 0.6f, 3.55f, 1.45f + WallShift), 0.07f, k.Iron);
        Pipe(root, "Steam line", new Vector3(RampEnd + 0.6f, 3.55f, 1.45f + WallShift), new Vector3(RightEnd, 3.55f, 1.45f + WallShift), 0.07f, k.Iron);
        Pipe(root, "Cold return", new Vector3(LeftEnd, 0.35f, 1.5f + WallShift), new Vector3(RampStart, 0.35f, 1.5f + WallShift), 0.09f, k.Iron);
        for (float x = -1f; x < RightEnd; x += 3.1f)
        {
            float top = x < RampEnd ? LowCeiling : HighCeiling;
            float floor = x < RampEnd ? 0f : Upper;
            if (x > LiftLeft - 0.6f && x < LiftRight + 0.6f) continue;
            Pipe(root, "Pipe clamp drop", new Vector3(x, top, 1.45f + WallShift), new Vector3(x, floor + 0.2f, 1.45f + WallShift), 0.045f, k.Iron);
            Box(root, "Duct band", new Vector3(x + 1.2f, x < RampEnd ? 2.85f : 3.8f, 1.25f + WallShift), new Vector3(0.06f, 0.56f, 0.56f), k.DarkSteel, false);
        }

        // Wall grates.
        foreach (float x in new[] { 1.5f, 11.2f, 16f, 26.6f, 39.2f })
        {
            float floor = x < RampEnd ? 0f : Upper;
            Box(root, "Vent grate frame", new Vector3(x, floor + 1.35f, WallZ - 0.06f), new Vector3(1.1f, 0.8f, 0.06f), k.DarkSteel, false);
            for (int i = 0; i < 6; i++)
                Box(root, "Vent slat", new Vector3(x, floor + 1.05f + i * 0.12f, WallZ - 0.1f), new Vector3(1f, 0.03f, 0.05f), k.Grate, false);
        }

        // Cable tray along the ceiling.
        Box(root, "Cable tray", new Vector3((LeftEnd + RampEnd) * 0.5f, LowCeiling - 0.12f, 0.95f), new Vector3(RampEnd - LeftEnd, 0.04f, 0.3f), k.Grate, false);
        Box(root, "Cable tray high", new Vector3((RampEnd + LiftLeft) * 0.5f, HighCeiling - 0.12f, 0.95f), new Vector3(LiftLeft - RampEnd, 0.04f, 0.3f), k.Grate, false);
        for (int i = 0; i < 3; i++)
            Pipe(root, "Cable", new Vector3(LeftEnd, LowCeiling - 0.08f, 0.88f + i * 0.07f), new Vector3(RampEnd, LowCeiling - 0.08f, 0.88f + i * 0.07f), 0.018f, k.Dark);

        // The duct box that hangs between camera 2 and lever B: the blind spot.
        Box(root, "Hanging duct box", new Vector3(27.65f, (2.85f + HighCeiling) * 0.5f, (WallZ - 1.2f) * 0.5f), new Vector3(1.3f, HighCeiling - 2.85f, WallZ + 1.2f), k.Steel, true);
        Box(root, "Hanging duct grille", new Vector3(27.65f, 2.86f, -0.6f), new Vector3(1.1f, 0.04f, 0.8f), k.Grate, false);

        // Low clutter near the walls (out of the cart's lane).
        Box(root, "Tool crate", new Vector3(10.6f, 0.3f, 1.15f + WallShift), new Vector3(0.7f, 0.6f, 0.5f), k.DarkSteel, false);
        Box(root, "Drum", new Vector3(23f, 0.9f, 1.2f + WallShift), new Vector3(0.5f, 0.8f, 0.5f), k.Iron, false);
        Box(root, "Junction box", new Vector3(2.7f, 1.6f, WallZ - 0.12f), new Vector3(0.45f, 0.6f, 0.2f), k.DarkSteel, false);

        // Foreground: pipes between the camera and the prisoner. No colliders, no shadows.
        // Thick ones stay away from the levers and the lift, where the player needs to see.
        var front = Group("Foreground pipes", root);
        const float fz = -2.45f;
        void Riser(float x, float radius, bool companion)
        {
            Pipe(front, "Foreground riser", new Vector3(x, -1.2f, fz), new Vector3(x, 8f, fz), radius, k.DarkSteel);
            foreach (float y in new[] { 0.75f, 2.35f })
                Pipe(front, "Riser flange", new Vector3(x, FloorAt(x) + y - 0.04f, fz), new Vector3(x, FloorAt(x) + y + 0.04f, fz), radius + 0.05f, k.Iron);
            if (companion)
                Pipe(front, "Foreground riser thin", new Vector3(x + radius + 0.14f, -1.2f, fz - 0.12f), new Vector3(x + radius + 0.14f, 8f, fz - 0.12f), 0.065f, k.Foreground);
        }
        void Run(float from, float to, float y, float radius)
        {
            Pipe(front, "Foreground run", new Vector3(from, y, fz + 0.1f), new Vector3(to, y, fz + 0.1f), radius, k.DarkSteel);
            for (float x = from + 0.5f; x < to; x += 2.2f)
            {
                Pipe(front, "Run support", new Vector3(x, y, fz + 0.1f), new Vector3(x, -1.2f, fz + 0.1f), 0.035f, k.Foreground);
                Pipe(front, "Run clamp", new Vector3(x - 0.03f, y, fz + 0.1f), new Vector3(x + 0.03f, y, fz + 0.1f), radius + 0.035f, k.Iron);
            }
        }
        Riser(2.3f, 0.24f, true);
        Riser(7.1f, 0.12f, false);
        Riser(10.7f, 0.22f, false);
        Riser(16.9f, 0.26f, true);
        Riser(21.8f, 0.2f, false);
        Riser(28.9f, 0.14f, false);
        Riser(38.6f, 0.24f, true);
        Run(3.2f, 9.2f, 0.55f, 0.13f);
        Run(12.4f, 15.9f, 1.3f, 0.09f);
        Run(37.7f, RightEnd, 1.6f, 0.12f);
        // A valve wheel and a sagging cable in the foreground.
        Pipe(front, "Foreground valve stem", new Vector3(10.7f, 1.45f, fz), new Vector3(10.7f, 1.45f, fz - 0.32f), 0.025f, k.Iron);
        Pipe(front, "Foreground valve wheel", new Vector3(10.7f, 1.45f, fz - 0.32f), new Vector3(10.7f, 1.45f, fz - 0.35f), 0.16f, k.DarkSteel);
        Pipe(front, "Foreground cable", new Vector3(LeftEnd, 3.05f, -2.9f), new Vector3(9f, 2.75f, -2.9f), 0.04f, k.Foreground);
        Pipe(front, "Foreground cable", new Vector3(9f, 2.75f, -2.9f), new Vector3(RightEnd, 3.4f, -2.9f), 0.04f, k.Foreground);
        foreach (Renderer r in front.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    private static float FloorAt(float x) =>
        x < RampStart ? 0f : x < RampEnd ? (x - RampStart) / (RampEnd - RampStart) * Upper : Upper;

    // Ceiling steam main with a side outlet that streams glowing steam. The steam is the light source:
    // a spot under the cloud draws the pool on the floor and a soft light inside the cloud lights the steam.
    // Both follow the shaft cycle; in the dark phase the outlet stops. Returns the outlet mouth.
    private static Transform SteamLight(Transform root, float x, float ceiling, bool shadows, Kit k,
        List<Light> lamps, List<Renderer> glass, List<Renderer> hazes, List<Renderer> plumes,
        List<SteamGlow> bursts, Transform audioRoot)
    {
        var unit = Group("Steam light", root, new Vector3(x, ceiling, 0.25f));
        // Alternate the blowing side so the shaft does not look stamped out.
        float side = ((Mathf.FloorToInt(x / 5f) % 2) + 2) % 2 == 0 ? 1f : -1f;
        float mainY = -0.2f;
        Pipe(unit, "Steam main", new Vector3(-0.85f, mainY, 0f), new Vector3(0.85f, mainY, 0f), 0.11f, k.DarkSteel, true);
        foreach (float fx in new[] { -0.85f, 0.85f })
            Pipe(unit, "Main flange", new Vector3(fx - 0.04f, mainY, 0f), new Vector3(fx + 0.04f, mainY, 0f), 0.15f, k.Iron, true);
        Pipe(unit, "Hanger", new Vector3(-0.5f, 0f, 0f), new Vector3(-0.5f, mainY + 0.1f, 0f), 0.012f, k.Iron, true);
        Pipe(unit, "Hanger", new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, mainY + 0.1f, 0f), 0.012f, k.Iron, true);

        // Drop, elbow, short horizontal nozzle pointing sideways.
        const float nozzleY = -0.48f;
        Pipe(unit, "Drop", new Vector3(0f, mainY, 0f), new Vector3(0f, nozzleY, 0f), 0.07f, k.Steel, true);
        Pipe(unit, "Elbow", new Vector3(0f, nozzleY + 0.05f, 0f), new Vector3(0f, nozzleY - 0.05f, 0f), 0.085f, k.Iron, true);
        Vector3 mouthLocal = new Vector3(side * 0.34f, nozzleY, 0f);
        Pipe(unit, "Nozzle", new Vector3(0f, nozzleY, 0f), mouthLocal, 0.065f, k.Steel, true);
        Pipe(unit, "Nozzle flange", mouthLocal - new Vector3(side * 0.04f, 0f, 0f), mouthLocal, 0.09f, k.Iron, true);
        Pipe(unit, "Valve stem", new Vector3(0f, mainY - 0.12f, 0f), new Vector3(0f, mainY - 0.12f, -0.2f), 0.012f, k.Iron, true);
        Pipe(unit, "Valve wheel", new Vector3(0f, mainY - 0.12f, -0.2f), new Vector3(0f, mainY - 0.12f, -0.22f), 0.07f, k.Ochre, true);

        // Hot glow in the nozzle mouth.
        var mouth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mouth.name = "Nozzle glow";
        Object.DestroyImmediate(mouth.GetComponent<Collider>());
        mouth.transform.SetParent(unit, false);
        mouth.transform.localPosition = mouthLocal + new Vector3(side * 0.003f, 0f, 0f);
        mouth.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        mouth.transform.localScale = new Vector3(0.11f, 0.004f, 0.11f);
        var mouthRenderer = mouth.GetComponent<Renderer>();
        mouthRenderer.sharedMaterial = k.LampGlass;
        mouthRenderer.shadowCastingMode = ShadowCastingMode.Off;
        glass.Add(mouthRenderer);

        // Floor pool where the cloud hangs, a little out from the nozzle.
        Vector3 cloudLocal = mouthLocal + new Vector3(side * 0.7f, -0.25f, 0f);
        float drop = ceiling + cloudLocal.y - FloorAt(x + cloudLocal.x);
        var light = new GameObject("Steam light pool").AddComponent<Light>();
        light.transform.SetParent(unit, false);
        light.transform.localPosition = cloudLocal;
        light.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        light.type = LightType.Spot;
        light.color = new Color(0.86f, 0.9f, 0.97f);
        light.intensity = 4.2f;
        light.range = drop + 1.8f;
        light.spotAngle = 66f;
        light.innerSpotAngle = 30f;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        light.shadowStrength = 0.85f;
        lamps.Add(light);

        // Soft light inside the cloud; SteamGlow drives it with the cycle level.
        var glow = new GameObject("Steam flare").AddComponent<Light>();
        glow.transform.SetParent(unit, false);
        glow.transform.localPosition = cloudLocal + new Vector3(0f, 0f, -0.15f);
        glow.type = LightType.Point;
        glow.color = new Color(0.86f, 0.9f, 0.97f);
        glow.intensity = 0.55f;
        glow.range = 2.8f;
        glow.shadows = LightShadows.None;

        // Steam: a steady stream out of the nozzle that slows, hangs and spreads as a glowing cloud.
        var ps = new GameObject("Glowing steam").AddComponent<ParticleSystem>();
        ps.transform.SetParent(unit, false);
        ps.transform.localPosition = mouthLocal + new Vector3(side * 0.02f, 0f, 0f);
        ps.transform.localRotation = Quaternion.Euler(8f, side > 0f ? 90f : -90f, 0f);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;
        main.duration = 4f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.2f, 4.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.gravityModifier = 0.015f;   // hot steam that still settles: it must not climb through the ceiling
        main.maxParticles = 64;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 12f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 16f;
        shape.radius = 0.05f;
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.3f;
        limit.dampen = 0.065f;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.4f;
        noise.scrollSpeed = 0.2f;
        noise.quality = ParticleSystemNoiseQuality.Low;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 3.4f));
        var spin = ps.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.86f, 0.94f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.55f, 0.6f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = k.Halo;
        renderer.maxParticleSize = 1.2f;
        UseSmokeSheet(ps);
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        plumes.Add(renderer);

        var hiss = Source(audioRoot, "Steam hiss", null, true, 0f);
        hiss.transform.position = unit.TransformPoint(mouthLocal);
        hiss.spatialBlend = 0.55f;
        hiss.maxDistance = 16f;
        var burst = unit.gameObject.AddComponent<SteamGlow>();
        Link(burst, "steam", ps);
        Link(burst, "glow", glow);
        Link(burst, "hiss", hiss);
        bursts.Add(burst);

        // Faint lit vapour column under the cloud.
        var haze = GameObject.CreatePrimitive(PrimitiveType.Quad);
        haze.name = "Steam light haze";
        Object.DestroyImmediate(haze.GetComponent<Collider>());
        haze.transform.SetParent(unit, false);
        float height = drop - 0.1f;
        haze.transform.localPosition = cloudLocal + new Vector3(0f, -0.05f - height * 0.5f, 0.3f);
        haze.transform.localScale = new Vector3(2.1f * height * Mathf.Tan(33f * Mathf.Deg2Rad) + 0.4f, height, 1f);
        var hr = haze.GetComponent<Renderer>();
        hr.sharedMaterial = k.Haze;
        hr.shadowCastingMode = ShadowCastingMode.Off;
        hr.receiveShadows = false;
        hazes.Add(hr);
        return mouth.transform;
    }

    private static void AddSparks(Transform bulb, FlickeringLightController lights, Kit k, Transform audioRoot)
    {
        var ps = new GameObject("Sparks").AddComponent<ParticleSystem>();
        ps.transform.SetParent(bulb.parent, false);
        ps.transform.localPosition = bulb.localPosition + new Vector3(0f, -0.05f, 0f);
        ps.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.65f), new Color(1f, 0.62f, 0.25f));
        main.gravityModifier = 1.1f;
        main.maxParticles = 40;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;
        shape.radius = 0.03f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.45f, 0.15f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes;
        var floor = new GameObject("Spark floor").transform;
        floor.SetParent(bulb.parent, false);
        floor.position = new Vector3(bulb.position.x, FloorAt(bulb.position.x) + 0.01f, 0f);
        collision.SetPlane(0, floor);
        collision.bounce = 0.3f;
        collision.dampen = 0.5f;
        collision.lifetimeLoss = 0.4f;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.09f;
        renderer.lengthScale = 2f;
        renderer.sharedMaterial = k.Spark;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var sparks = bulb.parent.gameObject.AddComponent<LampSparks>();
        Link(sparks, "sparks", ps);
        Link(sparks, "lights", lights);
        var crackle = Source(audioRoot, "Spark crackle", null, false, 0.35f);
        crackle.transform.position = bulb.position;
        crackle.spatialBlend = 0.55f;
        crackle.maxDistance = 16f;
        Link(sparks, "crackle", crackle);
    }

    // Sensors, fuse boxes, gauges and valves on the back wall. LEDs blink by emission only.
    private static void BuildWallDevices(Transform root, Kit k)
    {
        float wall = WallZ - 0.02f;
        var green = new List<Renderer>();
        var amber = new List<Renderer>();

        void FuseBox(float x, float y, int leds)
        {
            var b = Group("Fuse box", root, new Vector3(x, FloorAt(x) + y, wall - 0.1f));
            Box(b, "Fuse box body", Vector3.zero, new Vector3(0.42f, 0.56f, 0.16f), k.DarkSteel, false, true);
            Box(b, "Fuse box door seam", new Vector3(0f, 0f, -0.082f), new Vector3(0.38f, 0.004f, 0.004f), k.Dark, false, true);
            Box(b, "Fuse box latch", new Vector3(0.15f, 0.02f, -0.085f), new Vector3(0.03f, 0.07f, 0.02f), k.Iron, false, true);
            Box(b, "Conduit cap", new Vector3(0f, 0.3f, 0f), new Vector3(0.08f, 0.05f, 0.08f), k.Iron, false, true);
            Pipe(b, "Conduit", new Vector3(0f, 0.3f, 0f), new Vector3(0f, (x < RampEnd ? LowCeiling : HighCeiling) - FloorAt(x) - y, 0f), 0.022f, k.Iron, true);
            for (int i = 0; i < leds; i++)
            {
                var led = Box(b, "LED", new Vector3(-0.13f + i * 0.06f, 0.2f, -0.085f), new Vector3(0.022f, 0.022f, 0.01f), k.GreenLens, false, true);
                (i == leds - 1 ? amber : green).Add(led.GetComponent<Renderer>());
            }
        }

        void Gauge(float x, float y)
        {
            var g = Group("Pressure gauge", root, new Vector3(x, FloorAt(x) + y, wall - 0.12f));
            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "Gauge rim";
            Object.DestroyImmediate(rim.GetComponent<Collider>());
            rim.transform.SetParent(g, false);
            rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rim.transform.localScale = new Vector3(0.2f, 0.025f, 0.2f);
            rim.GetComponent<Renderer>().sharedMaterial = k.Iron;
            var face = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            face.name = "Gauge face";
            Object.DestroyImmediate(face.GetComponent<Collider>());
            face.transform.SetParent(g, false);
            face.transform.localPosition = new Vector3(0f, 0f, -0.028f);
            face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            face.transform.localScale = new Vector3(0.165f, 0.004f, 0.165f);
            face.GetComponent<Renderer>().sharedMaterial = k.Trim;
            var needle = Box(g, "Gauge needle", new Vector3(0.025f, 0.02f, -0.034f), new Vector3(0.07f, 0.008f, 0.004f), k.Dark, false, true);
            needle.transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(20f, 160f));
            Pipe(g, "Gauge stem", new Vector3(0f, -0.1f, 0.03f), new Vector3(0f, -0.28f, 0.03f), 0.015f, k.Iron, true);
        }

        void Sensor(float x, float y)
        {
            var s = Group("Motion sensor", root, new Vector3(x, FloorAt(x) + y, wall - 0.08f));
            Box(s, "Sensor body", Vector3.zero, new Vector3(0.16f, 0.22f, 0.1f), k.Trim, false, true);
            Box(s, "Sensor grille", new Vector3(0f, -0.04f, -0.052f), new Vector3(0.12f, 0.08f, 0.006f), k.Grate, false, true);
            var led = Box(s, "LED", new Vector3(0.05f, 0.08f, -0.053f), new Vector3(0.018f, 0.018f, 0.006f), k.AmberLens, false, true);
            amber.Add(led.GetComponent<Renderer>());
        }

        void Panel(float x)
        {
            var p = Group("Breaker panel", root, new Vector3(x, FloorAt(x) + 1.45f, wall - 0.1f));
            Box(p, "Panel body", Vector3.zero, new Vector3(1.0f, 0.75f, 0.14f), k.DarkSteel, false, true);
            for (int i = 0; i < 6; i++)
            {
                Box(p, "Breaker", new Vector3(-0.38f + i * 0.15f, 0.08f, -0.075f), new Vector3(0.08f, 0.16f, 0.03f), k.Dark, false, true);
                Box(p, "Breaker toggle", new Vector3(-0.38f + i * 0.15f, i % 3 == 0 ? 0.04f : 0.12f, -0.095f), new Vector3(0.03f, 0.04f, 0.02f), k.Trim, false, true);
            }
            var led = Box(p, "LED", new Vector3(0.4f, 0.28f, -0.075f), new Vector3(0.025f, 0.025f, 0.01f), k.GreenLens, false, true);
            green.Add(led.GetComponent<Renderer>());
            Label(p, "DISTRIBUTION  7", p.TransformPoint(new Vector3(0f, -0.25f, -0.075f)), 0.022f, k, new Color(0.62f, 0.66f, 0.7f, 0.3f));
            Pipe(p, "Panel feed", new Vector3(-0.3f, 0.38f, 0f), new Vector3(-0.3f, LowCeiling - FloorAt(x) - 1.45f, 0f), 0.03f, k.Iron, true);
            Pipe(p, "Panel feed", new Vector3(0.3f, 0.38f, 0f), new Vector3(0.3f, LowCeiling - FloorAt(x) - 1.45f, 0f), 0.03f, k.Iron, true);
        }

        void Valve(float x, float y)
        {
            var v = Group("Valve wheel", root, new Vector3(x, y, 1.27f + WallShift));
            Pipe(v, "Valve stem", Vector3.zero, new Vector3(0f, 0f, -0.14f), 0.02f, k.Iron, true);
            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = "Valve rim";
            Object.DestroyImmediate(wheel.GetComponent<Collider>());
            wheel.transform.SetParent(v, false);
            wheel.transform.localPosition = new Vector3(0f, 0f, -0.15f);
            wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            wheel.transform.localScale = new Vector3(0.24f, 0.012f, 0.24f);
            wheel.GetComponent<Renderer>().sharedMaterial = k.Ochre;
        }

        void Warning(float x, float y, string text)
        {
            var w = Group("Warning plate", root, new Vector3(x, FloorAt(x) + y, wall - 0.03f));
            Box(w, "Plate", Vector3.zero, new Vector3(0.42f, 0.22f, 0.015f), k.Ochre, false, true);
            Label(w, text, w.TransformPoint(new Vector3(0f, 0f, -0.012f)), 0.018f, k, new Color(0.08f, 0.07f, 0.05f, 0.85f));
        }

        FuseBox(0.6f, 1.5f, 2);
        Gauge(5.6f, 2.32f);
        Gauge(6.0f, 2.32f);
        Sensor(7.4f, 2.4f);
        Panel(12.8f);
        Warning(12.8f, 0.85f, "HIGH VOLTAGE");
        Sensor(17.7f, 2.45f);
        FuseBox(21.6f, 1.4f, 3);
        Gauge(23.1f, 2.4f);
        Sensor(29.2f, 2.0f);
        FuseBox(38.1f, 1.4f, 2);
        Warning(38.1f, 0.75f, "LIFT MOTOR");
        Valve(3.4f, 2.6f);
        Valve(15.6f, 2.6f);
        Valve(28.4f, 3.55f);

        var greenBlink = root.gameObject.AddComponent<IndicatorBlink>();
        LinkArray(greenBlink, "leds", green.ToArray());
        var amberBlink = root.gameObject.AddComponent<IndicatorBlink>();
        LinkArray(amberBlink, "leds", amber.ToArray());
        Edit(amberBlink, "color", p => p.colorValue = new Color(1f, 0.62f, 0.25f));
        Set(amberBlink, "period", 2.3f);
        Set(amberBlink, "duty", 0.08f);
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            if (r.bounds.size.magnitude < 0.6f) r.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ==================================================================== cart
    private static CartController BuildCart(Kit k, out CartHidingController hiding, Transform audioRoot)
    {
        var root = new GameObject("Freight cart");
        root.transform.position = new Vector3(3.2f, 0f, 0f);
        var t = root.transform;
        var body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.74f, 0f);
        box.size = new Vector3(1.56f, 0.88f, 0.9f);

        Box(t, "Cart floor", new Vector3(0f, 0.32f, 0f), new Vector3(1.5f, 0.05f, 0.86f), k.Steel, false, true);
        Box(t, "Cart side near", new Vector3(0f, 0.74f, -0.43f), new Vector3(1.5f, 0.84f, 0.035f), k.Steel, false, true);
        Box(t, "Cart side far", new Vector3(0f, 0.74f, 0.43f), new Vector3(1.5f, 0.84f, 0.035f), k.Steel, false, true);
        Box(t, "Cart end left", new Vector3(-0.75f, 0.74f, 0f), new Vector3(0.035f, 0.84f, 0.86f), k.Steel, false, true);
        Box(t, "Cart end right", new Vector3(0.75f, 0.74f, 0f), new Vector3(0.035f, 0.84f, 0.86f), k.Steel, false, true);
        for (int i = -2; i <= 2; i++)
            Box(t, "Cart rib", new Vector3(i * 0.33f, 0.74f, -0.455f), new Vector3(0.04f, 0.84f, 0.02f), k.DarkSteel, false, true);
        Box(t, "Cart rim", new Vector3(0f, 1.17f, -0.44f), new Vector3(1.56f, 0.04f, 0.05f), k.DarkSteel, false, true);
        Box(t, "Cart rim", new Vector3(0f, 1.17f, 0.44f), new Vector3(1.56f, 0.04f, 0.05f), k.DarkSteel, false, true);
        Pipe(t, "Handle left", new Vector3(-0.78f, 0.95f, -0.3f), new Vector3(-0.92f, 0.95f, -0.3f), 0.025f, k.Iron, true);
        Pipe(t, "Handle left", new Vector3(-0.78f, 0.95f, 0.3f), new Vector3(-0.92f, 0.95f, 0.3f), 0.025f, k.Iron, true);
        Pipe(t, "Handle bar left", new Vector3(-0.92f, 0.95f, -0.3f), new Vector3(-0.92f, 0.95f, 0.3f), 0.028f, k.Iron, true);
        Pipe(t, "Handle right", new Vector3(0.78f, 0.95f, -0.3f), new Vector3(0.92f, 0.95f, -0.3f), 0.025f, k.Iron, true);
        Pipe(t, "Handle right", new Vector3(0.78f, 0.95f, 0.3f), new Vector3(0.92f, 0.95f, 0.3f), 0.025f, k.Iron, true);
        Pipe(t, "Handle bar right", new Vector3(0.92f, 0.95f, -0.3f), new Vector3(0.92f, 0.95f, 0.3f), 0.028f, k.Iron, true);

        var wheels = new List<Transform>();
        foreach (float x in new[] { -0.55f, 0.55f })
        foreach (float z in new[] { -0.36f, 0.36f })
        {
            var wheel = Group("Wheel", t, new Vector3(x, 0.13f, z));
            var tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tyre.name = "Tyre";
            Object.DestroyImmediate(tyre.GetComponent<Collider>());
            tyre.transform.SetParent(wheel, false);
            tyre.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            tyre.transform.localScale = new Vector3(0.26f, 0.03f, 0.26f);
            tyre.GetComponent<Renderer>().sharedMaterial = k.Dark;
            Box(wheel, "Hub spoke", Vector3.zero, new Vector3(0.22f, 0.03f, 0.07f), k.Iron, false, true);
            Box(wheel, "Hub spoke", Vector3.zero, new Vector3(0.03f, 0.22f, 0.07f), k.Iron, false, true);
            Box(t, "Caster fork", new Vector3(x, 0.24f, z), new Vector3(0.06f, 0.14f, 0.05f), k.DarkSteel, false, true);
            wheels.Add(wheel);
        }

        var pedal = Group("Brake pedal", t, new Vector3(0.68f, 0.25f, -0.47f));
        Box(pedal, "Brake arm", new Vector3(0.08f, 0f, 0f), new Vector3(0.18f, 0.03f, 0.05f), k.Ochre, false, true);
        Box(pedal, "Brake pad", new Vector3(0.17f, 0f, -0.02f), new Vector3(0.06f, 0.05f, 0.09f), k.Ochre, false, true);
        var brakeLamp = Point(t, "Brake lamp", new Vector3(0.79f, 0.45f, -0.5f), new Color(1f, 0.62f, 0.25f), 0.5f, 0.9f);

        var cover = Group("Canvas cover", t);
        Box(cover, "Canvas", new Vector3(0f, 1.2f, 0f), new Vector3(1.48f, 0.08f, 0.84f), k.Canvas, false, true);
        Box(cover, "Canvas bulge", new Vector3(0.1f, 1.27f, 0f), new Vector3(0.8f, 0.12f, 0.6f), k.Canvas, false, true);

        var cart = root.AddComponent<CartController>();
        LinkArray(cart, "wheels", wheels.ToArray());
        Link(cart, "brakePedal", pedal);
        Link(cart, "brakeLamp", brakeLamp);
        Set(cart, "minX", -1.2f);
        Set(cart, "maxX", LiftRight - 0.8f);
        Set(cart, "halfLength", 0.78f);
        Link(cart, "rolling", Source(audioRoot, "Cart wheels", Clip("Kitchen/CrateScrape"), true, 0f));
        Link(cart, "effects", Source(audioRoot, "Cart effects", null, false, 0.8f));

        hiding = root.AddComponent<CartHidingController>();
        Link(hiding, "cart", cart);
        Link(hiding, "cover", cover.gameObject);
        Link(hiding, "rustle", Source(audioRoot, "Cart rustle", Clip("Kitchen/CrateScrape"), false, 0.35f));
        return cart;
    }

    // ==================================================================== levers, lift, cables
    private static LeverController BuildLever(string name, string prompt, Vector3 at, bool spring, Kit k, Transform audioRoot)
    {
        var root = Group(name, null, at);
        Box(root, "Lever plate", new Vector3(0f, 0f, 0.1f), new Vector3(0.42f, 0.62f, 0.06f), k.DarkSteel, false, true);
        Box(root, "Lever slot", new Vector3(0f, 0f, 0.06f), new Vector3(0.06f, 0.46f, 0.02f), k.Dark, false, true);
        var handle = Group("Handle pivot", root, new Vector3(0f, -0.05f, 0.02f));
        Pipe(handle, "Lever stem", Vector3.zero, new Vector3(0f, 0.36f, -0.06f), 0.025f, k.Steel, true);
        var grip = Box(handle, "Lever grip", new Vector3(0f, 0.38f, -0.07f), new Vector3(0.08f, 0.12f, 0.08f), spring ? k.Ochre : k.Ochre, false, true);
        // A warm marker lamp: amber means "something to use" in this game.
        var marker = Point(root, "Lever marker", new Vector3(0f, 0.55f, -0.25f), new Color(1f, 0.66f, 0.32f), spring ? 0f : 0.35f, 1.4f);
        if (spring) Object.DestroyImmediate(marker.gameObject);
        var lever = root.gameObject.AddComponent<LeverController>();
        Set(lever, "displayName", prompt);
        Link(lever, "handle", handle);
        Link(lever, "grip", grip.transform);
        Set(lever, "springReturn", spring);
        Set(lever, "offAngle", 38f);
        Set(lever, "onAngle", -38f);
        Set(lever, "interactRadius", 0.7f);
        var clank = Source(audioRoot, name + " clank", Clip("Kitchen/FreezerDoor"), false, 0.55f);
        clank.transform.position = at;
        Link(lever, "clank", clank);
        return lever;
    }

    private static Rigidbody BuildLift(Kit k, Transform audioRoot, out Light powerLamp, out Renderer powerLens,
        out Light statusLamp, out Renderer statusLens, out Light panelLamp)
    {
        var platform = new GameObject("Lift platform");
        platform.transform.position = new Vector3(LiftCenter, Upper, 0f);
        var body = platform.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        var collider = platform.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, -0.12f, 0f);
        collider.size = new Vector3(LiftRight - LiftLeft, 0.24f, 2.4f);
        var t = platform.transform;
        Box(t, "Deck grating", new Vector3(0f, -0.02f, 0f), new Vector3(LiftRight - LiftLeft - 0.04f, 0.04f, 2.3f), k.Grate, false, true);
        Box(t, "Deck frame", new Vector3(0f, -0.14f, 0f), new Vector3(LiftRight - LiftLeft, 0.2f, 2.4f), k.DarkSteel, false, true);
        Box(t, "Hazard edge left", new Vector3(-(LiftRight - LiftLeft) * 0.5f + 0.06f, 0.005f, 0f), new Vector3(0.1f, 0.03f, 2.3f), k.Ochre, false, true);
        Box(t, "Hazard edge right", new Vector3((LiftRight - LiftLeft) * 0.5f - 0.06f, 0.005f, 0f), new Vector3(0.1f, 0.03f, 2.3f), k.Ochre, false, true);
        Box(t, "Back rail", new Vector3(0f, 1.0f, 1.05f), new Vector3(LiftRight - LiftLeft, 0.05f, 0.05f), k.Iron, false, true);
        Box(t, "Back rail post", new Vector3(-1.6f, 0.5f, 1.05f), new Vector3(0.05f, 1f, 0.05f), k.Iron, false, true);
        Box(t, "Back rail post", new Vector3(1.6f, 0.5f, 1.05f), new Vector3(0.05f, 1f, 0.05f), k.Iron, false, true);
        Pipe(t, "Hoist cable", new Vector3(-1.2f, 0f, 0.9f), new Vector3(-1.2f, 11f, 0.9f), 0.02f, k.Dark, true);
        Pipe(t, "Hoist cable", new Vector3(1.2f, 0f, 0.9f), new Vector3(1.2f, 11f, 0.9f), 0.02f, k.Dark, true);

        // Control box on the shaft wall: green = lift has power, amber = launching.
        var box = Group("Lift control box", null, new Vector3(LiftLeft - 0.75f, Upper + 2.1f, ConsoleZ - 0.18f));
        Box(box, "Control box body", Vector3.zero, new Vector3(0.6f, 0.75f, 0.25f), k.DarkSteel, false, true);
        var green = Box(box, "Power lens", new Vector3(-0.14f, 0.2f, -0.14f), new Vector3(0.09f, 0.09f, 0.04f), k.GreenLens, false, true);
        var amber = Box(box, "Status lens", new Vector3(0.14f, 0.2f, -0.14f), new Vector3(0.09f, 0.09f, 0.04f), k.AmberLens, false, true);
        powerLens = green.GetComponent<Renderer>();
        statusLens = amber.GetComponent<Renderer>();
        powerLamp = Point(box, "Power indicator", new Vector3(-0.14f, 0.2f, -0.35f), new Color(0.3f, 1f, 0.45f), 0.8f, 1.6f);
        statusLamp = Point(box, "Status indicator", new Vector3(0.14f, 0.2f, -0.35f), new Color(1f, 0.62f, 0.25f), 1.1f, 2.2f);
        Label(box, "LIFT", box.TransformPoint(new Vector3(0f, -0.15f, -0.13f)), 0.04f, k, new Color(0.6f, 0.64f, 0.7f, 0.35f));

        // Launch panel lamp next to lever B: lit only once the lift has power.
        panelLamp = Point(null, "Launch panel lamp", new Vector3(25.6f, Upper + 1.85f, ConsoleZ - 0.4f), new Color(1f, 0.62f, 0.25f), 0.7f, 1.6f);
        return body;
    }

    // Steel I-post from floor to ceiling that carries a lever or the lift box beside the lane.
    private static void ControlPost(float x, Kit k)
    {
        var post = Group("Control post", null, new Vector3(x, 0f, ConsoleZ + 0.12f));
        float floor = FloorAt(x), top = x < RampEnd ? LowCeiling : HighCeiling;
        Box(post, "Post web", new Vector3(0f, (floor + top) * 0.5f, 0f), new Vector3(0.08f, top - floor, 0.22f), k.DarkSteel, false, true);
        Box(post, "Post flange", new Vector3(0f, (floor + top) * 0.5f, -0.11f), new Vector3(0.3f, top - floor, 0.03f), k.DarkSteel, false, true);
        Box(post, "Post flange", new Vector3(0f, (floor + top) * 0.5f, 0.11f), new Vector3(0.3f, top - floor, 0.03f), k.DarkSteel, false, true);
        Box(post, "Post foot", new Vector3(0f, floor + 0.03f, 0f), new Vector3(0.45f, 0.06f, 0.4f), k.Iron, false, true);
    }

    private static void BuildCables(Kit k)
    {
        // Power cable: lever A -> control box -> along the ceiling -> lever B panel. Visual hint only.
        var root = Group("Control cables", null);
        float wall = ConsoleZ - 0.1f;
        Pipe(root, "Cable A to box", new Vector3(31.9f, Upper + 1.45f, wall), new Vector3(31.9f, Upper + 2.1f, wall), 0.03f, k.Dark);
        Pipe(root, "Cable A to box", new Vector3(31.9f, Upper + 2.1f, wall), new Vector3(LiftLeft - 1.05f, Upper + 2.1f, wall), 0.03f, k.Dark);
        Pipe(root, "Cable box up", new Vector3(LiftLeft - 0.75f, Upper + 2.48f, wall), new Vector3(LiftLeft - 0.75f, HighCeiling - 0.2f, wall), 0.03f, k.Dark);
        Pipe(root, "Cable along ceiling", new Vector3(LiftLeft - 0.75f, HighCeiling - 0.2f, wall), new Vector3(25.6f, HighCeiling - 0.2f, wall), 0.03f, k.Dark);
        Pipe(root, "Cable down to B", new Vector3(25.6f, HighCeiling - 0.2f, wall), new Vector3(25.6f, Upper + 1.45f, wall), 0.03f, k.Dark);
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ==================================================================== cameras and steam
    private static SecurityCameraController BuildSecurityCamera(string name, Vector3 at, float center, float sweep,
        float seconds, float phase, float fov, float range, Kit k)
    {
        var root = Group(name, null, at);
        Pipe(root, "Mount", new Vector3(0f, 0.32f, 0f), Vector3.zero, 0.03f, k.Iron, true);
        var pivot = Group("Camera pivot", root);
        Box(pivot, "Camera body", new Vector3(0f, -0.16f, 0f), new Vector3(0.16f, 0.32f, 0.16f), k.DarkSteel, false, true);
        Box(pivot, "Camera hood", new Vector3(0f, -0.3f, 0f), new Vector3(0.2f, 0.06f, 0.2f), k.Dark, false, true);
        var lens = Box(pivot, "Tally lens", new Vector3(0.09f, -0.06f, -0.09f), new Vector3(0.04f, 0.04f, 0.02f), k.RedLens, false, true);
        var indicator = Point(pivot, "Tally light", new Vector3(0.1f, -0.06f, -0.2f), new Color(1f, 0.12f, 0.06f), 1.2f, 1.3f);

        var cone = GameObject.CreatePrimitive(PrimitiveType.Quad);
        cone.name = "View cone";
        Object.DestroyImmediate(cone.GetComponent<Collider>());
        cone.transform.SetParent(pivot, false);
        cone.transform.localPosition = new Vector3(0f, -0.33f - range * 0.5f, -0.05f);
        cone.transform.localScale = new Vector3(2f * range * Mathf.Tan(fov * Mathf.Deg2Rad) * 1.05f, range, 1f);
        var coneRenderer = cone.GetComponent<Renderer>();
        coneRenderer.sharedMaterial = k.Cone;
        coneRenderer.shadowCastingMode = ShadowCastingMode.Off;
        coneRenderer.receiveShadows = false;

        var meter = root.gameObject.AddComponent<DetectionMeter>();
        Set(meter, "offset", new Vector3(0f, 0.42f, -0.2f));

        var camera = root.gameObject.AddComponent<SecurityCameraController>();
        Link(camera, "pivot", pivot);
        Set(camera, "centerAngle", center);
        Set(camera, "sweepAngle", sweep);
        Set(camera, "sweepSeconds", seconds);
        Set(camera, "sweepPhase", phase);
        Set(camera, "halfFieldOfView", fov);
        Set(camera, "range", range);
        Link(camera, "meter", meter);
        Link(camera, "viewCone", coneRenderer);
        Link(camera, "indicator", indicator);
        Link(camera, "indicatorLens", lens.GetComponent<Renderer>());
        return camera;
    }

    private static void SteamVentAt(Vector3 at, Kit k, Transform audioRoot)
    {
        var root = Group("Steam leak", null, at);
        Box(root, "Leaking joint", Vector3.zero, new Vector3(0.16f, 0.16f, 0.16f), k.Iron, false, true);
        var ps = new GameObject("Steam").AddComponent<ParticleSystem>();
        ps.transform.SetParent(root, false);
        ps.transform.localRotation = Quaternion.Euler(-60f, 0f, 0f);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startColor = new Color(0.72f, 0.78f, 0.84f, 0.16f);
        main.maxParticles = 60;
        main.gravityModifier = -0.04f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.04f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = k.Smoke;
        UseSmokeSheet(ps);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var vent = root.gameObject.AddComponent<SteamVent>();
        Link(vent, "puff", ps);
        var hiss = Source(audioRoot, "Steam hiss", null, false, 0.35f);
        hiss.transform.position = at;
        hiss.spatialBlend = 0.6f;
        hiss.maxDistance = 14f;
        Link(vent, "hiss", hiss);
    }

    // ==================================================================== HUD
    private static VentilationHud BuildHud(Kit k)
    {
        var root = new GameObject("Ventilation HUD", typeof(Canvas), typeof(CanvasScaler));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var plate = new GameObject("Hint plate", typeof(RectTransform), typeof(Image));
        plate.transform.SetParent(root.transform, false);
        var plateRect = (RectTransform)plate.transform;
        plateRect.anchorMin = plateRect.anchorMax = new Vector2(0.5f, 0f);
        plateRect.pivot = new Vector2(0.5f, 0f);
        plateRect.anchoredPosition = new Vector2(0f, 48f);
        plateRect.sizeDelta = new Vector2(820f, 54f);
        var plateImage = plate.GetComponent<Image>();
        plateImage.color = new Color(0.02f, 0.025f, 0.03f, 0.62f);
        plateImage.raycastTarget = false;
        var hint = MakeText(plate.transform, "Hint", 26, new Color(0.89f, 0.84f, 0.74f), k.Font);

        var fade = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        fade.transform.SetParent(root.transform, false);
        var fadeRect = (RectTransform)fade.transform;
        fadeRect.anchorMin = Vector2.zero;
        fadeRect.anchorMax = Vector2.one;
        fadeRect.offsetMin = fadeRect.offsetMax = Vector2.zero;
        var fadeImage = fade.GetComponent<Image>();
        fadeImage.color = new Color(0f, 0f, 0f, 0f);
        fadeImage.raycastTarget = false;

        var centreHolder = new GameObject("Centre", typeof(RectTransform));
        centreHolder.transform.SetParent(root.transform, false);
        var centreRect = (RectTransform)centreHolder.transform;
        centreRect.anchorMin = centreRect.anchorMax = new Vector2(0.5f, 0.5f);
        centreRect.sizeDelta = new Vector2(900f, 240f);
        var centre = MakeText(centreHolder.transform, "Centre text", 44, new Color(0.78f, 0.82f, 0.86f), k.Font);

        var hud = root.AddComponent<VentilationHud>();
        Link(hud, "hint", hint);
        Link(hud, "fade", fadeImage);
        Link(hud, "centre", centre);
        return hud;
    }

    private static Text MakeText(Transform parent, string name, int size, Color color, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.supportRichText = true;
        text.raycastTarget = false;
        return text;
    }

    // ==================================================================== helpers
    private static Kit LoadKit()
    {
        if (!AssetDatabase.IsValidFolder(ArtFolder))
            AssetDatabase.CreateFolder("Assets/_Game/Art", "KitchenVentilation");
        var kit = new Kit
        {
            Concrete = Mat("Kitchen/Damp concrete"),
            Floor = Mat("Kitchen/Wet quarry floor"),
            DarkSteel = Mat("Corridor/DarkMachineSteel"),
            Steel = Mat("Corridor/BrushedServiceSteel"),
            Iron = Mat("EnvironmentKit/Materials/Iron"),
            Grate = Mat("EnvironmentKit/Materials/GrateSteel"),
            Foreground = Mat("ServicePassage/Foreground silhouette"),
            Wash = Mat("ServicePassage/Wall wash"),
            Haze = Mat("Kitchen/Cool lamp haze"),
            Ochre = Mat("Corridor/SafetyOchre"),
            ColdSteel = Mat("Kitchen/Cold store steel"),
            Frost = Mat("Kitchen/Bleached frost"),
            Canvas = Mat("Kitchen/Grey laundry canvas"),
            Trim = Mat("ServicePassage/Utility trim"),
            Dark = Mat("EnvironmentKit/Materials/Dark"),
            LampGlass = Emissive("Vent lamp glass", new Color(0.7f, 0.76f, 0.82f), new Color(0.75f, 0.85f, 1f) * 1.3f),
            RedLens = Emissive("Vent red lens", new Color(0.35f, 0.05f, 0.04f), new Color(1f, 0.12f, 0.06f) * 0.05f),
            GreenLens = Emissive("Vent green lens", new Color(0.12f, 0.12f, 0.12f), Color.black),
            AmberLens = Emissive("Vent amber lens", new Color(0.12f, 0.12f, 0.12f), Color.black),
            Cone = ConeMaterial(),
            Particle = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat"),
            Spark = SparkMaterial(),
            Halo = HaloMaterial(),
            Smoke = SmokeMaterial(),
            Shade = ShadeMesh(),
            Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
        };
        kit.Text = TextMaterial(kit.Font);
        return kit;
    }

    private static Material Mat(string path)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/_Game/Art/{path}.mat");
        if (m == null) throw new InvalidOperationException($"Missing material Assets/_Game/Art/{path}.mat");
        return m;
    }

    private static Material Emissive(string name, Color albedo, Color emission)
    {
        string path = $"{ArtFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_Color", albedo);
        m.SetFloat("_Glossiness", 0.35f);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor("_EmissionColor", emission);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material TextMaterial(Font font)
    {
        string path = $"{ArtFolder}/Vent world text.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(AssetDatabase.LoadAssetAtPath<Shader>($"{ArtFolder}/WorldText.shader"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.mainTexture = font.material.mainTexture;
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material SparkMaterial()
    {
        string path = $"{ArtFolder}/Vent spark.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Standard Unlit");
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.mainTexture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(1f, 0.85f, 0.6f, 1f));
        EditorUtility.SetDirty(m);
        return m;
    }

    private const int SmokeTiles = 4;

    private static void UseSmokeSheet(ParticleSystem ps)
    {
        var sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = SmokeTiles;
        sheet.numTilesY = SmokeTiles;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, SmokeTiles * SmokeTiles - 1);
        sheet.cycleCount = 1;
    }

    // 4x4 sheet of irregular smoke puffs: fractal noise, ragged edges, wispy holes. White RGB, density in alpha.
    private static Texture2D SmokeSheet()
    {
        string path = $"{ArtFolder}/Vent smoke sheet.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int cell = 128;
        int size = cell * SmokeTiles;
        var pixels = new Color32[size * size];
        var rng = new System.Random(7);
        for (int ty = 0; ty < SmokeTiles; ty++)
        for (int tx = 0; tx < SmokeTiles; tx++)
        {
            float ox = (float)rng.NextDouble() * 100f, oy = (float)rng.NextDouble() * 100f;
            float squash = 0.8f + (float)rng.NextDouble() * 0.4f;
            for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
            {
                float u = (x + 0.5f) / cell * 2f - 1f, v = ((y + 0.5f) / cell * 2f - 1f) * squash;
                float shape = Fbm(u * 1.6f + ox, v * 1.6f + oy, 3);
                float detail = Fbm(u * 4.5f + oy, v * 4.5f + ox, 4);
                float r = Mathf.Sqrt(u * u + v * v) + (shape - 0.5f) * 0.55f;
                float body = Mathf.Clamp01(1f - Mathf.SmoothStep(0.25f, 0.95f, r));
                float density = body * Mathf.Lerp(0.35f, 1.15f, detail);
                density = Mathf.Clamp01(Mathf.Pow(density, 1.4f));
                int px = tx * cell + x, py = ty * cell + y;
                pixels[py * size + px] = new Color32(255, 255, 255, (byte)(density * 255f));
            }
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
        texture.SetPixels32(pixels);
        texture.Apply();
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static float Fbm(float x, float y, int octaves)
    {
        float sum = 0f, amplitude = 0.5f, total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Mathf.PerlinNoise(x, y) * amplitude;
            total += amplitude;
            x = x * 2.03f + 17.1f;
            y = y * 2.03f + 31.7f;
            amplitude *= 0.5f;
        }
        return sum / total;
    }

    // Grey alpha-blended smoke for the wall leaks (they are lit, not glowing).
    private static Material SmokeMaterial()
    {
        string path = $"{ArtFolder}/Vent smoke.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Standard Unlit");
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.mainTexture = SmokeSheet();
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.54f, 0.58f, 0.5f));
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material HaloMaterial()
    {
        string path = $"{ArtFolder}/Vent glowing steam.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Standard Unlit");
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.mainTexture = SmokeSheet();
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.78f, 0.84f, 0.92f, 0.1f));
        EditorUtility.SetDirty(m);
        return m;
    }

    // Open steel cone, both sides, so the shade reads from above and below.
    private static Mesh ShadeMesh()
    {
        string path = $"{ArtFolder}/Vent lamp shade.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool created = mesh == null;
        if (created) mesh = new Mesh { name = "Vent lamp shade" };
        const int sides = 24;
        const float top = 0.06f, bottom = 0.25f, depth = 0.2f;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        for (int pass = 0; pass < 2; pass++)
        {
            int start = vertices.Count;
            float facing = pass == 0 ? 1f : -1f;
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 n = (dir * depth + Vector3.up * (bottom - top)).normalized * facing;
                vertices.Add(dir * top);
                vertices.Add(dir * bottom + Vector3.down * depth);
                normals.Add(n);
                normals.Add(n);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = start + i * 2, b = a + 1, c = a + 2, d = a + 3;
                if (pass == 0) triangles.AddRange(new[] { a, c, b, b, c, d });
                else triangles.AddRange(new[] { a, b, c, b, d, c });
            }
        }
        // Closed top cap.
        int centre = vertices.Count;
        vertices.Add(Vector3.zero);
        normals.Add(Vector3.up);
        for (int i = 0; i <= sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            vertices.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * top);
            normals.Add(Vector3.up);
        }
        for (int i = 0; i < sides; i++)
            triangles.AddRange(new[] { centre, centre + 2 + i, centre + 1 + i });
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        if (created) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static Material ConeMaterial()
    {
        string path = $"{ArtFolder}/Vent camera cone.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Game Jam/Kitchen Lamp Haze"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_Color", new Color(0.42f, 0.5f, 0.58f, 0.12f));
        m.SetFloat("_Spread", 1f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static AudioClip Clip(string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/_Game/Audio/SFX/{path}.wav");
        if (clip == null) throw new InvalidOperationException($"Missing audio Assets/_Game/Audio/SFX/{path}.wav");
        return clip;
    }

    private static AudioSource Source(Transform parent, string name, AudioClip clip, bool loop, float volume)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var s = go.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = loop;
        s.volume = volume;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        return s;
    }

    private static Transform Group(string name, Transform parent, Vector3 position = default)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = parent != null ? parent.TransformPoint(position) : position;
        return go.transform;
    }

    private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material m, bool solid, bool local = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>());
        if (local)
        {
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
        }
        else
        {
            go.transform.position = center;
            go.transform.localScale = size;
            if (parent != null) go.transform.SetParent(parent, true);
        }
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        if (!solid && Mathf.Min(size.x, Mathf.Min(size.y, size.z)) < 0.1f)
            r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    private static void Pipe(Transform parent, string name, Vector3 from, Vector3 to, float radius, Material m, bool local = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        Vector3 a = local && parent != null ? parent.TransformPoint(from) : from;
        Vector3 b = local && parent != null ? parent.TransformPoint(to) : to;
        Vector3 d = b - a;
        go.transform.position = (a + b) * 0.5f;
        go.transform.rotation = d.sqrMagnitude > 0f ? Quaternion.FromToRotation(Vector3.up, d.normalized) : Quaternion.identity;
        go.transform.localScale = new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f);
        if (parent != null) go.transform.SetParent(parent, true);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        if (radius < 0.06f) r.shadowCastingMode = ShadowCastingMode.Off;
    }

    // Position is local to parent when a parent is given, world otherwise.
    private static Light Point(Transform parent, string name, Vector3 at, Color color, float intensity, float range)
    {
        var light = new GameObject(name).AddComponent<Light>();
        if (parent != null)
        {
            light.transform.SetParent(parent, false);
            light.transform.localPosition = at;
        }
        else
            light.transform.position = at;
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        return light;
    }

    private static TextMesh Label(Transform parent, string text, Vector3 at, float size, Kit k, Color color)
    {
        var go = new GameObject("Label " + text);
        go.transform.position = at;
        if (parent != null) go.transform.SetParent(parent, true);
        var label = go.AddComponent<TextMesh>();
        label.text = text;
        label.font = k.Font;
        label.fontSize = 64;
        label.characterSize = size;
        label.anchor = TextAnchor.MiddleCenter;
        label.color = color;
        go.GetComponent<MeshRenderer>().sharedMaterial = k.Text;
        return label;
    }

    private static void Link(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name}.{field} not found");
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void LinkArray(Object target, string field, Object[] values)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name}.{field} not found");
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Set(Object target, string field, float value) => Edit(target, field, p => p.floatValue = value);
    private static void Set(Object target, string field, bool value) => Edit(target, field, p => p.boolValue = value);
    private static void Set(Object target, string field, string value) => Edit(target, field, p => p.stringValue = value);
    private static void Set(Object target, string field, Vector3 value) => Edit(target, field, p => p.vector3Value = value);
    private static void SetVector2(Object target, string field, Vector2 value) => Edit(target, field, p => p.vector2Value = value);

    private static void Edit(Object target, string field, Action<SerializedProperty> apply)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name}.{field} not found");
        apply(p);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == ScenePath))
            return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
