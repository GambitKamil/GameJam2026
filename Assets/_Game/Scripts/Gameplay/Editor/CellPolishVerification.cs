using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static partial class CellPolishVerification
{
    private const string Cell = "Assets/_Game/Scenes/Levels/Cell.unity";
    private const string Request = "Library/CellPolishVerify.request";
    private const string OutputKey = "CellPolishVerification.Output";
    private const string RateKey = "CellPolishVerification.Rate";
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static string output;
    private static SideViewPlayer player;
    private static CellEscape escape;
    private static BreadSupply bread;
    private static CellMotion motion;
    private static Transform figure, shoulder, hand, sleeve, nearBoot, farBoot, mouse, rod, gate, key, exit;
    private static Rigidbody body;
    private static Camera view;
    private static Renderer[] keyRenderers;
    private static float keyFloorHeight, keyDropClearance, keyFloorClearance;
    private static Vector3 keyLowestPose;
    private static int phase, lastFrame, frames, errors, editorErrors;
    private static int previousCaptureFramerate;
    private static bool previousRunInBackground;
    private static float began, phaseTime, walkStart, armLength, wakeStep, armStretch, floorGap, floorDepth;
    private static Vector3 previousFigure;
    private static bool wasWaking;
    private static bool active;

    static CellPolishVerification()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Game Jam/Verify Cell Motion")]
    private static void RunFromMenu()
    {
        Run(Path.GetFullPath("Temp/CellPolishVerification"), false);
    }

    public static void RunBatch()
    {
        Run(Path.GetFullPath("Temp/CellPolishVerification"), true);
    }

    private static void Run(string directory, bool build, int rate = DefaultSampleRate)
    {
        Directory.CreateDirectory(directory);
        output = directory;
        SessionState.SetString(OutputKey, output);
        SessionState.SetInt(RateKey, rate);
        EditorSceneManager.OpenScene(Cell);
        CheckSceneReferences();
        if (build)
        {
            BuildReport report = BuildPipeline.BuildPlayer(new[] { Cell },
                Path.Combine(directory, "Cell.exe"), BuildTarget.StandaloneWindows64, BuildOptions.None);
            Record($"Build: {report.summary.result}, errors={report.summary.totalErrors}, warnings={report.summary.totalWarnings}");
            if (report.summary.result != BuildResult.Succeeded)
            {
                SessionState.EraseString(OutputKey);
                return;
            }
        }

        EditorApplication.isPlaying = true;
    }

    private static void Update()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Request))
        {
            string[] request = File.ReadAllLines(Request);
            File.Delete(Request);
            int rate = request.Length > 2 && int.TryParse(request[2], out int value) ? Mathf.Clamp(value, 30, 120) : DefaultSampleRate;
            Run(request[0], request.Length > 1 && request[1] == "build", rate);
            return;
        }

        if (!EditorApplication.isPlaying)
            return;
        output = SessionState.GetString(OutputKey, "");
        if (output.Length == 0 || lastFrame == Time.frameCount)
            return;

        lastFrame = Time.frameCount;
        try
        {
            if (!active)
                Begin();
            Tick();
        }
        catch (Exception exception)
        {
            Finish("FAIL: " + exception);
        }
    }

    private static void CheckSceneReferences()
    {
        int missingScripts = 0, missingMaterials = 0, renderers = 0;
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled)
                    continue;
                renderers++;
                foreach (var material in renderer.sharedMaterials)
                    if (material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                        missingMaterials++;
            }
        }

        Record($"Scene: {renderers} enabled renderers, missing scripts={missingScripts}, missing materials/shaders={missingMaterials}");
    }

    private static void Begin()
    {
        errors = editorErrors = phase = frames = 0;
        wakeStep = armStretch = floorGap = floorDepth = 0f;
        previousCaptureFramerate = Time.captureFramerate;
        SampleRate = SessionState.GetInt(RateKey, DefaultSampleRate);
        Time.captureFramerate = SampleRate;
        previousRunInBackground = Application.runInBackground;
        Application.runInBackground = true;
        wasWaking = false;
        Application.logMessageReceived += OnLog;
        player = Object.FindFirstObjectByType<SideViewPlayer>();
        bread = Object.FindFirstObjectByType<BreadSupply>();
        escape = Object.FindFirstObjectByType<CellEscape>();
        motion = player.GetComponent<CellMotion>();
        body = player.GetComponent<Rigidbody>();
        figure = Field<Transform>(motion, "figure");
        shoulder = Field<Transform>(motion, "shoulder");
        hand = Field<Transform>(motion, "hand");
        sleeve = Field<Transform>(motion, "sleeve");
        nearBoot = Field<Transform>(motion, "nearBoot");
        farBoot = Field<Transform>(motion, "farBoot");
        mouse = Field<Transform>(escape, "mouse");
        rod = Field<Transform>(escape, "rod");
        key = Field<Transform>(escape, "key");
        gate = Field<Transform>(escape, "gate");
        exit = Field<Transform>(escape, "exit");
        view = Camera.main;
        armLength = sleeve.localScale.y;
        began = phaseTime = Time.time;
        walkStart = body.position.x;
        var rat = mouse.GetComponentInChildren<SkinnedMeshRenderer>();
        Record($"Player visual: {figure.childCount} immediate children, {figure.GetComponentsInChildren<SkinnedMeshRenderer>().Length} skinned renderers. Rat: {rat.bones.Length} skin bones, readable mesh={rat.sharedMesh.isReadable}, vertices={rat.sharedMesh.vertexCount}");
        foreach (Transform bone in rat.bones)
            Record($"Rat bone {bone.name}: {rat.transform.InverseTransformPoint(bone.position):F3}");
        InitializeMetrics();
        InitializeGrateProbe();
        keyRenderers = key.GetComponentsInChildren<Renderer>();
        if (!Physics.Raycast(key.position, Vector3.down, out RaycastHit keyFloor, 5f, ~0, QueryTriggerInteraction.Ignore))
            throw new InvalidOperationException("No floor found below the hanging key.");
        keyFloorHeight = keyFloor.point.y;
        Record($"Key support: {keyFloor.collider.name} at Y={keyFloorHeight:F4} m");
        keyDropClearance = keyFloorClearance = float.PositiveInfinity;
        active = true;
    }

    private static void Tick()
    {
        frames++;
        if (sleeve != null)
        {
            SampleMotion();
            SampleGrateInteraction();
            if (key.gameObject.activeInHierarchy)
                SampleKeyClearance();
        }
        if (sleeve != null)
            armStretch = Mathf.Max(armStretch, Mathf.Abs(sleeve.localScale.y / armLength - 1f));
        if (Time.time - began > 55f)
        {
            Finish("FAIL: timeout at step " + phase + ", puzzle stage " + Stage());
            return;
        }

        switch (phase)
        {
            case 0:
                Move(1f);
                if (motion.WakingUp)
                {
                    if (!wasWaking)
                        Capture("01-wake-start");
                    else
                        wakeStep = Mathf.Max(wakeStep, Vector3.Distance(previousFigure, figure.position));
                    CaptureWakeMilestones(Time.time - phaseTime);
                    previousFigure = figure.position;
                    wasWaking = true;
                    return;
                }
                if (!wasWaking && Time.time - phaseTime < 0.3f)
                    return;
                Record($"Wake: maximum consecutive figure displacement={wakeStep:F4} m at {SampleRate} Hz, input root drift={Mathf.Abs(body.position.x - walkStart):F4} m");
                Capture("03-wake-end");
                Next();
                break;
            case 1:
                if (!PushAgainstGrate())
                    return;
                if (!WalkTo(bread.transform.position.x))
                    return;
                if (Time.time - phaseTime < 0.18f)
                    return;
                Call(bread, "TakePieceForThrow");
                if (!bread.IsHoldingPiece)
                    throw new InvalidOperationException("Could not take bread at the table.");
                int remaining = Field<int>(bread, "remaining");
                if ((bool)Call(bread, "TryEatAtTable") || !bread.IsHoldingPiece ||
                    Field<int>(bread, "remaining") != remaining || motion.Busy)
                    throw new InvalidOperationException("Eating consumed bread or started a bite while the hand was occupied.");
                Record("PASS: eating leaves the held bread and remaining portion count intact");
                Next();
                break;
            case 2:
                Move(0f);
                ProbeFloor();
                if (!TickAim())
                    return;
                LaunchBread();
                Next();
                break;
            case 3:
                Move(0f);
                if (Stage() != "KeyBehindGate" || bread.ThrowInProgress)
                    return;
                float keyTilt = Vector3.Angle(key.forward, Vector3.up);
                keyTilt = Mathf.Min(keyTilt, 180f - keyTilt);
                Metric(keyTilt <= 3f, $"Key resting tilt: {keyTilt:F2} degrees");
                Capture("05-air-catch");
                Next();
                break;
            case 4:
                if (!WalkTo(rod.position.x) || motion.Busy)
                    return;
                Call(escape, "Interact");
                Next();
                break;
            case 5:
                if (!Field<bool>(escape, "haveRod") || motion.Busy)
                    return;
                Capture("06-rod");
                if (!WalkTo(gate.position.x - 0.73f))
                    return;
                Call(escape, "Interact");
                Next();
                break;
            case 6:
                if (Stage() != "KeyWithinReach" || motion.Busy)
                    return;
                if (!WalkTo(key.position.x))
                    return;
                Call(escape, "Interact");
                Next();
                break;
            case 7:
                if (Stage() != "GateOpen" || motion.Busy)
                    return;
                if (!WalkTo(exit.position.x - 0.70f))
                    return;
                Call(escape, "Interact");
                Next();
                break;
            case 8:
                if (Stage() == "Complete")
                {
                    Capture("07-exit");
                    Call(escape, "RestartPreview");
                    Next();
                    return;
                }
                if (Stage() == "ExitOpen")
                    Move(1f);
                break;
            case 9:
                var restarted = Object.FindFirstObjectByType<CellEscape>();
                if (restarted == null || restarted == escape)
                    return;
                Record($"Restart stage: {Field<object>(restarted, "stage")}");
                Finish("PASS: puzzle sequence and restart");
                break;
        }
    }

    private static void SampleKeyClearance()
    {
        string stage = Stage();
        if (stage != "MouseJumping" && stage != "KeyBehindGate" && stage != "KeyWithinReach")
            return;

        float bottom = float.PositiveInfinity;
        foreach (Renderer part in keyRenderers)
            bottom = Mathf.Min(bottom, part.bounds.min.y);

        float clearance = bottom - keyFloorHeight;
        if (stage == "MouseJumping")
        {
            if (clearance < keyDropClearance)
                keyLowestPose = key.position;
            keyDropClearance = Mathf.Min(keyDropClearance, clearance);
        }
        else
            keyFloorClearance = Mathf.Min(keyFloorClearance, clearance);
    }

    private static void LaunchBread()
    {
        var path = new List<Vector3>();
        for (int degrees = 0; degrees <= 180; degrees++)
        {
            float radians = (degrees - 90) * Mathf.Deg2Rad;
            Vector3 velocity = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * 8.5f;
            Vector3 origin = motion.ThrowOrigin(velocity);
            if ((int)Call(bread, "Simulate", origin, velocity, path) < 0)
                continue;
            Record($"Bread: found interception with launch angle {degrees - 90} degrees.");
            Call(bread, "Launch", origin, velocity);
            return;
        }

        throw new InvalidOperationException("No bread trajectory reaches the hanging key from the table.");
    }

    private static void Move(float input)
    {
        player.GetType().GetField("moveInput", Instance).SetValue(player, input);
    }

    private static bool WalkTo(float x)
    {
        float distance = x - player.transform.position.x;
        if (Mathf.Abs(distance) < 0.12f)
        {
            Move(0f);
            return true;
        }

        Move(Mathf.Clamp(distance * 2f, -1f, 1f));
        return false;
    }

    private static void Next()
    {
        phase++;
        phaseTime = Time.time;
        Record($"Step {phase}: {Stage()} at {Time.time - began:F2}s");
    }

    private static string Stage() => Field<object>(escape, "stage").ToString();

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Instance).GetValue(target);

    private static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Instance | BindingFlags.Static).Invoke(target, args);

    private static void Capture(string name)
    {
        if (Application.isBatchMode)
            return;
        var previous = RenderTexture.active;
        var target = view.targetTexture;
        var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
        var capture = RenderTexture.GetTemporary(960, 540, 24);
        try
        {
            view.targetTexture = capture;
            view.Render();
            RenderTexture.active = capture;
            image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        }
        finally
        {
            view.targetTexture = target;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(capture);
            Object.Destroy(image);
        }
    }

    private static void OnLog(string condition, string stack, LogType type)
    {
        if (type == LogType.Log)
            return;
        bool search = stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup");
        if (search)
            editorErrors++;
        else
            errors++;
        Record($"{(search ? "Editor startup" : "Gameplay")} {type}: {condition}\n{stack}");
    }

    private static void Finish(string result)
    {
        Record($"Run: {frames} observed frames at {SampleRate} Hz, gameplay warnings/errors={errors}, known editor startup exceptions={editorErrors}, max sleeve length change={armStretch:P1}, idle foot gap={floorGap:F3} m, idle floor penetration={floorDepth:F3} m");
        Record(result);
        if (active)
        {
            ReportGrateProbe();
            Metric(keyDropClearance >= -0.004f && keyFloorClearance >= -0.004f && keyFloorClearance <= 0.012f,
                $"Key floor clearance: drop={keyDropClearance:F4} m, settled and dragged={keyFloorClearance:F4} m, lowest pivot={keyLowestPose:F3}");
            ReportMotionMetrics();
        }
        ResetRun();
        SessionState.EraseString(OutputKey);
        EditorApplication.isPlaying = false;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && active)
        {
            ResetRun();
            SessionState.EraseString(OutputKey);
        }
    }

    private static void ResetRun()
    {
        Time.captureFramerate = previousCaptureFramerate;
        Application.runInBackground = previousRunInBackground;
        active = false;
        Application.logMessageReceived -= OnLog;
    }

    private static void Record(string line) => File.AppendAllText(Path.Combine(output, "verification.log"), line + Environment.NewLine);
}
