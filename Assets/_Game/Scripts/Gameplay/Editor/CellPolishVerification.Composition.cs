using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

internal static partial class CellPolishVerification
{
    private static Camera stepCamera;
    private static Renderer[] rodMeshes;
    private static Renderer[] boots;
    private static Vector3 initialRodPosition, initialRodScale;
    private static Quaternion initialRodRotation;
    private static float stepOverlap, stepTime, openTime, closedLeft, closedRight, openLeft, openRight;
    private static double previousWallFrame, wallSeconds;
    private static int wallFrames;
    private static bool skippedCapture, bedCaptured, gateCaptured;
    private static float rodMotion;

    private static void InitializeComposition()
    {
        rodMeshes = rod.GetComponentsInChildren<Renderer>();
        boots = new[] { nearBoot.GetComponent<Renderer>(), farBoot.GetComponent<Renderer>() };
        initialRodPosition = rod.position;
        initialRodRotation = rod.rotation;
        initialRodScale = rod.lossyScale;
        stepOverlap = stepTime = rodMotion = 0f;
        openTime = closedLeft = closedRight = openLeft = openRight = float.NaN;
        bedCaptured = gateCaptured = skippedCapture = false;
        previousWallFrame = wallSeconds = 0.0;
        wallFrames = 0;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        string path = Path.Combine(output, "aspect.txt");
        view.aspect = File.Exists(path) && float.TryParse(File.ReadAllText(path),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float requested)
            ? requested : 16f / 9f;
        Record($"Composition aspect: {view.aspect:F4}.");
    }

    private static void SampleComposition()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (previousWallFrame > 0.0 && !skippedCapture && phase > 0)
        {
            wallSeconds += now - previousWallFrame;
            wallFrames++;
        }
        skippedCapture = false;
        previousWallFrame = now;

        if (motion.WakingUp)
        {
            float elapsed = Time.time - began;
            rodMotion = Mathf.Max(rodMotion, Vector3.Distance(rod.position, initialRodPosition));
            if (!bedCaptured && elapsed >= 0.09f)
            {
                Capture("SHOT-1");
                bedCaptured = true;
                Record($"At bed: position={view.transform.position:F4}, size={view.orthographicSize:F4}, left={LaneEdge(0):F4}, right={LaneEdge(1):F4}.");
            }

            float overlap = 0f;
            foreach (Renderer boot in boots)
                foreach (Renderer shaft in rodMeshes)
                    if (boot != null && shaft.enabled && boot.bounds.Intersects(shaft.bounds))
                        overlap = Mathf.Max(overlap, ShapeOverlap(boot, shaft));

            if (overlap > stepOverlap + 0.0001f)
            {
                stepOverlap = overlap;
                stepTime = elapsed;
                CaptureStep("Step-peak");
            }
            float stepFrame = 1.83f;
            string stepRequest = Path.Combine(output, "step-time.txt");
            if (File.Exists(stepRequest) && float.TryParse(File.ReadAllText(stepRequest),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float requested))
                stepFrame = requested;

            if (elapsed >= stepFrame && elapsed < stepFrame + Time.deltaTime)
                CaptureStep("Step-reference");
        }

        if (phase == 7 && Stage() == "KeyWithinReach" && motion.Busy)
        {
            closedLeft = LaneEdge(0);
            closedRight = LaneEdge(1);
        }
    }

    private static bool CaptureGateReveal()
    {
        if (gateCaptured)
            return true;
        if (float.IsNaN(openTime))
            openTime = Time.time;

        Move(0f);
        if (Time.time - openTime < 1.25f)
            return false;

        openLeft = LaneEdge(0);
        openRight = LaneEdge(1);
        Capture("SHOT-2");
        gateCaptured = true;
        return true;
    }

    private static float LaneEdge(int side)
    {
        var lane = new Plane(Vector3.forward, Vector3.zero);
        Ray ray = view.ViewportPointToRay(new Vector3(side, 0.5f, 0f));
        return lane.Raycast(ray, out float distance) ? ray.GetPoint(distance).x : float.NaN;
    }

    private static void ReportComposition()
    {
        float extent = closedRight - closedLeft;
        float percent = (openRight - closedRight) / extent * 100f;
        Record($"Composition: closed left/right={closedLeft:F4}/{closedRight:F4}, open left/right={openLeft:F4}/{openRight:F4}, extra right={percent:F2}% of closed width.");
        Record($"Boot and rod overlap: {stepOverlap:F5} m at {stepTime:F3}s; rod travel during wake={rodMotion:F6} m; initial rod position={initialRodPosition:F6}, rotation={initialRodRotation:F6}, scale={initialRodScale:F6}.");
        double ms = wallFrames > 0 ? wallSeconds * 1000.0 / wallFrames : 0.0;
        Record($"Frame wall time: {ms:F3} ms mean, {1000.0 / ms:F2} FPS, {wallFrames} uncapped editor frames, excluding direct capture frames.");
    }

    private static void CaptureStep(string prefix)
    {
        skippedCapture = true;
        if (stepCamera == null)
        {
            var probe = new GameObject("Step Verification Camera");
            probe.hideFlags = HideFlags.HideAndDontSave;
            stepCamera = probe.AddComponent<Camera>();
            stepCamera.enabled = false;
        }

        stepCamera.CopyFrom(view);
        stepCamera.enabled = false;
        stepCamera.orthographic = true;
        stepCamera.orthographicSize = 0.91f;
        stepCamera.aspect = 16f / 9f;
        Vector3 center = rod.position + new Vector3(0.06f, 0.38f, 0.15f);
        stepCamera.transform.SetPositionAndRotation(center - Vector3.forward * 4f, Quaternion.identity);
        Capture(prefix + "-side", stepCamera);
        stepCamera.transform.SetPositionAndRotation(center + Vector3.up * 3f,
            Quaternion.LookRotation(Vector3.down, Vector3.forward));
        Capture(prefix + "-top", stepCamera);
    }

    private static float ShapeOverlap(Renderer first, Renderer second)
    {
        Bounds a = first.localBounds;
        Bounds b = second.localBounds;
        Transform at = first.transform;
        Transform bt = second.transform;
        Vector3 delta = bt.TransformPoint(b.center) - at.TransformPoint(a.center);
        Vector3 ascale = Vector3.Scale(a.extents, Abs(at.lossyScale));
        Vector3 bscale = Vector3.Scale(b.extents, Abs(bt.lossyScale));
        Vector3 ax = at.right, ay = at.up, az = at.forward;
        Vector3 bx = bt.right, by = bt.up, bz = bt.forward;
        var aa = new[] { ax, ay, az };
        var ba = new[] { bx, by, bz };
        float depth = float.PositiveInfinity;
        for (int i = 0; i < 3; i++)
        {
            Test(aa[i]);
            Test(ba[i]);
            for (int j = 0; j < 3; j++)
                Test(Vector3.Cross(aa[i], ba[j]));
        }
        return Mathf.Max(0f, depth);

        void Test(Vector3 axis)
        {
            if (axis.sqrMagnitude < 0.000001f)
                return;
            axis.Normalize();
            float size = ascale.x * Mathf.Abs(Vector3.Dot(ax, axis))
                + ascale.y * Mathf.Abs(Vector3.Dot(ay, axis))
                + ascale.z * Mathf.Abs(Vector3.Dot(az, axis))
                + bscale.x * Mathf.Abs(Vector3.Dot(bx, axis))
                + bscale.y * Mathf.Abs(Vector3.Dot(by, axis))
                + bscale.z * Mathf.Abs(Vector3.Dot(bz, axis));
            depth = Mathf.Min(depth, size - Mathf.Abs(Vector3.Dot(delta, axis)));
        }

        static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
