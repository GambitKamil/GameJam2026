using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

internal static partial class CellPolishVerification
{
    private static Camera stepCamera;
    private static Renderer[] rodMeshes;
    private static Renderer[] boots;
    private static Renderer[] keyWallMeshes;
    private static Renderer[] lowerLegMeshes;
    private static float walkingRodOverlap, keyWallOverlap;
    private static string walkingRodPeak, keyWallPeak;
    private static int walkingRodSamples, keyWallSamples;
    private static bool strictSurfaceClearance;
    private static Vector3 initialRodPosition, initialRodScale;
    private static Quaternion initialRodRotation;
    private static float stepOverlap, stepTime, openTime, closedLeft, closedRight, openLeft, openRight;
    private static double previousWallFrame, wallSeconds;
    private static int wallFrames;
    private static bool skippedCapture, bedCaptured, gateCaptured;
    private static float rodMotion, rodRotationMotion, rodScaleMotion;
    private static float shotLeft, shotRight, previousRight, previousRightStep;
    private static float revealPeakStep, revealPeakJerk, revealBacktrack, revealFirstFrameStep;
    private static float resetTime;
    private static int previousVsync;
    private static int previousTargetRate;
    private static bool resetCaptureReady, camClipsWorld, moveDuringReveal, strictComposition;
    private static readonly Vector3[] previousBootPosition = new Vector3[2];
    private static readonly bool[] previousBootSupport = new bool[2];
    private static float wakeFootSlip, wakeFootDepth, comparisonStepTime, approachSettledAt;
    private static bool aimSampleDone, verifyVisibleThrow, forceCloseThrow, approachForThrow, restoreCompositionTiming;


    private static void InitializeComposition()
    {
        rodMeshes = rod.GetComponentsInChildren<Renderer>();
        boots = new[] { nearBoot.GetComponent<Renderer>(), farBoot.GetComponent<Renderer>() };
        lowerLegMeshes = boots.Concat(new[]
        {
            Field<Transform>(Field<object>(motion, "frontLeg"), "shin").GetComponent<Renderer>(),
            Field<Transform>(Field<object>(motion, "backLeg"), "shin").GetComponent<Renderer>()
        }).ToArray();
        Vector3 keyStart = key.position;
        var kit = key.root.Find("Environment Kit");
        keyWallMeshes = kit == null ? Array.Empty<Renderer>() : kit.GetComponentsInChildren<Renderer>()
            .Where(part => part.bounds.max.x >= keyStart.x - 0.5f && part.bounds.min.x <= keyStart.x + 0.5f &&
                part.bounds.min.z > keyStart.z && part.bounds.min.z < keyStart.z + 0.65f && part.bounds.max.y > 0.04f)
            .ToArray();
        Record($"Key drop wall: {keyWallMeshes.Length} mesh bounds, nearest face Z={keyWallMeshes.Select(mesh => mesh.bounds.min.z).DefaultIfEmpty(float.NaN).Min():F4}.");
        strictSurfaceClearance = File.Exists(Path.Combine(output, "surface-strict.txt"));
        walkingRodOverlap = keyWallOverlap = 0f;
        walkingRodSamples = keyWallSamples = 0;
        walkingRodPeak = keyWallPeak = "none";
        initialRodPosition = rod.position;
        initialRodRotation = rod.rotation;
        initialRodScale = rod.lossyScale;
        stepOverlap = stepTime = rodMotion = rodRotationMotion = rodScaleMotion = 0f;
        revealPeakStep = revealPeakJerk = revealBacktrack = revealFirstFrameStep = 0f;
        previousRight = previousRightStep = shotLeft = shotRight = float.NaN;
        resetTime = approachSettledAt = float.NaN;
        aimSampleDone = approachForThrow = false;
        forceCloseThrow = File.Exists(Path.Combine(output, "close-throw.txt"));
        verifyVisibleThrow = File.Exists(Path.Combine(output, "check-key-visibility.txt"));
        resetCaptureReady = camClipsWorld = false;
        moveDuringReveal = File.Exists(Path.Combine(output, "reveal-moving.txt"));
        strictComposition = File.Exists(Path.Combine(output, "composition-strict.txt"));
        comparisonStepTime = ReadCompositionNumber("step-time.txt", 2.4f);
        for (int i = 0; i < previousBootSupport.Length; i++)
            previousBootSupport[i] = false;
        wakeFootSlip = wakeFootDepth = 0f;
        openTime = closedLeft = closedRight = openLeft = openRight = float.NaN;
        bedCaptured = gateCaptured = skippedCapture = false;
        previousWallFrame = wallSeconds = 0.0;
        wallFrames = 0;
        previousVsync = QualitySettings.vSyncCount;
        previousTargetRate = Application.targetFrameRate;
        restoreCompositionTiming = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        view.aspect = ReadCompositionNumber("aspect.txt", 16f / 9f);
        Record($"Composition aspect: {view.aspect:F4}, moving reveal={moveDuringReveal}.");
    }

    private static float ReadCompositionNumber(string filename, float fallback)
    {
        string path = Path.Combine(output, filename);
        return File.Exists(path) && float.TryParse(File.ReadAllText(path),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float requested)
            ? requested : fallback;
    }

    private static void ReleaseComposition()
    {
        if (restoreCompositionTiming)
        {
            QualitySettings.vSyncCount = previousVsync;
            Application.targetFrameRate = previousTargetRate;
            restoreCompositionTiming = false;
        }
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
            rodRotationMotion = Mathf.Max(rodRotationMotion, Quaternion.Angle(rod.rotation, initialRodRotation));
            rodScaleMotion = Mathf.Max(rodScaleMotion, Vector3.Distance(rod.lossyScale, initialRodScale));
            for (int i = 0; i < boots.Length; i++)
            {
                Renderer boot = boots[i];
                float floorGap = boot.bounds.min.y;
                bool supported = floorGap >= -0.005f && floorGap <= 0.008f;
                wakeFootDepth = Mathf.Max(wakeFootDepth, -floorGap);
                Vector3 position = boot.transform.position;
                if (supported && previousBootSupport[i])
                    wakeFootSlip = Mathf.Max(wakeFootSlip, Vector2.Distance(
                        new Vector2(position.x, position.z),
                        new Vector2(previousBootPosition[i].x, previousBootPosition[i].z)));
                previousBootSupport[i] = supported;
                previousBootPosition[i] = position;
            }
            if (!bedCaptured && elapsed >= 0.09f)
            {
                Capture("SHOT-1");
                bedCaptured = true;
                shotLeft = LaneEdge(0);
                shotRight = LaneEdge(1);
                Record($"At bed: position={view.transform.position:F4}, size={view.orthographicSize:F4}, left={shotLeft:F4}, right={shotRight:F4}.");
                RecordShotAnchors("SHOT 1");
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
            if (elapsed >= comparisonStepTime && elapsed < comparisonStepTime + Time.deltaTime)
                CaptureStep("Step-reference");
        }

        SampleSurfaceClearance();

        camClipsWorld |= Physics.CheckSphere(view.transform.position, view.nearClipPlane, ~0, QueryTriggerInteraction.Ignore);

        if (phase == 7 && Stage() == "KeyWithinReach" && motion.Busy)
        {
            closedLeft = LaneEdge(0);
            closedRight = LaneEdge(1);
            previousRight = closedRight;
        }
        if (phase == 7 && Stage() == "GateOpen" && !gateCaptured)
        {
            float right = LaneEdge(1);
            if (!float.IsNaN(previousRight))
            {
                float delta = right - previousRight;
                revealPeakStep = Mathf.Max(revealPeakStep, Mathf.Abs(delta));
                revealBacktrack = Mathf.Max(revealBacktrack, -delta);
                if (!float.IsNaN(previousRightStep))
                    revealPeakJerk = Mathf.Max(revealPeakJerk, Mathf.Abs(delta - previousRightStep));
                else
                    revealFirstFrameStep = Mathf.Abs(delta);
                previousRightStep = delta;
            }
            previousRight = right;
        }
    }

    private static void SampleSurfaceClearance()
    {
        if (key.gameObject.activeInHierarchy && key.parent == null && Stage() == "MouseJumping")
        {
            keyWallSamples++;
            foreach (Renderer part in keyRenderers)
            foreach (Renderer wall in keyWallMeshes)
            {
                float overlap = part.bounds.Intersects(wall.bounds) ? ShapeOverlap(part, wall) : 0f;
                if (overlap > keyWallOverlap)
                {
                    keyWallOverlap = overlap;
                    keyWallPeak = $"{part.name} / {wall.name}, t={Time.time - began:F3}s, key={key.position:F4}";
                }
            }
        }

        if (motion.WakingUp || motion.Busy || Field<bool>(escape, "haveRod") || Field<bool>(escape, "rodInUse"))
            return;

        walkingRodSamples++;
        foreach (Renderer leg in lowerLegMeshes)
        foreach (Renderer shaft in rodMeshes)
        {
            float overlap = leg.bounds.Intersects(shaft.bounds) ? ShapeOverlap(leg, shaft) : 0f;
            if (overlap > walkingRodOverlap)
            {
                walkingRodOverlap = overlap;
                walkingRodPeak = $"{leg.name} / {shaft.name}, t={Time.time - began:F3}s, phase={phase}, actor={player.transform.position:F4}, rod={rod.position:F4}";
            }
        }
    }

    private static bool CaptureGateReveal()
    {
        if (gateCaptured)
            return true;
        if (float.IsNaN(openTime))
            openTime = Time.time;

        Move(moveDuringReveal ? 1f : 0f);
        if (Time.time - openTime < 1.25f)
            return false;

        openLeft = LaneEdge(0);
        openRight = LaneEdge(1);
        Capture("SHOT-2");
        RecordShotAnchors("SHOT 2");
        gateCaptured = true;
        return true;
    }

    private static float LaneEdge(int side)
    {
        var lane = new Plane(Vector3.forward, Vector3.zero);
        Ray ray = view.ViewportPointToRay(new Vector3(side, 0.5f, 0f));
        return lane.Raycast(ray, out float distance) ? ray.GetPoint(distance).x : float.NaN;
    }

    private static bool PreparePlayerThrow()
    {
        if (!aimSampleDone)
        {
            if (!TickAim())
                return false;
            aimSampleDone = true;
            LogKeyVisibility("bread table");
            approachForThrow = verifyVisibleThrow && (forceCloseThrow || !KeyOnScreen());
        }

        // Verify a visible shot from an ordinary traversable spot when the table framing cuts off the key.
        if (approachForThrow)
        {
            if (float.IsNaN(approachSettledAt))
            {
                if (!WalkTo(gate.position.x - 0.72f))
                    return false;
                approachSettledAt = Time.time;
            }
            if (Time.time - approachSettledAt < 0.45f)
                return false;
        }

        LogKeyVisibility("throw");
        if (verifyVisibleThrow)
        {
            Metric(KeyOnScreen(), "The hanging key was visible from a reachable throwing position.");
            Capture("KEY-AIM");
        }
        return true;
    }

    private static bool KeyOnScreen()
    {
        Vector3 point = view.WorldToViewportPoint(key.position);
        return point.z > 0f && point.x >= 0.025f && point.x <= 0.975f && point.y >= 0.025f && point.y <= 0.975f;
    }

    private static void LogKeyVisibility(string moment)
    {
        Vector3 point = view.WorldToViewportPoint(key.position);
        Record($"Key before {moment}: viewport={point:F4}, actor X={player.transform.position.x:F4}, active={key.gameObject.activeInHierarchy}, inside margins={KeyOnScreen()}.");
    }

    private static void RecordShotAnchors(string shot)
    {
        Vector3 hero = view.WorldToViewportPoint(Field<Transform>(motion, "head").position);
        Vector3 grate = view.WorldToViewportPoint(gate.position + Vector3.up * 1.2f);
        Vector3 prop = view.WorldToViewportPoint(rod.position);
        Vector3 keyPoint = view.WorldToViewportPoint(key.position);
        Record($"{shot} viewport anchors: hero={hero:F3}, bars={grate:F3}, rod={prop:F3}, key={keyPoint:F3} (active={key.gameObject.activeInHierarchy}).");
    }

    private static bool CheckCompositionAfterRestart()
    {
        if (resetCaptureReady)
            return true;

        Camera restartedView = Camera.main;
        if (restartedView == null)
            return false;
        if (float.IsNaN(resetTime))
        {
            view = restartedView;
            view.aspect = ReadCompositionNumber("aspect.txt", 16f / 9f);
            resetTime = Time.time;
        }
        if (Time.time - resetTime < 0.09f)
            return false;

        float leftDrift = Mathf.Abs(LaneEdge(0) - shotLeft);
        float rightDrift = Mathf.Abs(LaneEdge(1) - shotRight);
        Record($"Composition reset: left/right drift={leftDrift:F4}/{rightDrift:F4} m at the matching bed beat; aspect={view.aspect:F4}.");
        if (strictComposition)
            Metric(leftDrift <= 0.08f && rightDrift <= 0.08f, "Restart restored the original bed framing.");
        Capture("SHOT-1-restarted");
        resetCaptureReady = true;
        return true;
    }

    private static void ReportComposition()
    {
        float extent = closedRight - closedLeft;
        float percent = (openRight - closedRight) / extent * 100f;
        Record($"Composition: closed left/right={closedLeft:F4}/{closedRight:F4}, open left/right={openLeft:F4}/{openRight:F4}, extra right={percent:F2}% of closed width.");
        Record($"Reveal motion: max right step={revealPeakStep:F4} m/frame, peak second difference={revealPeakJerk:F4} m/frame squared, first frame step={revealFirstFrameStep:F4} m, reverse step={revealBacktrack:F4} m; camera intersected level={camClipsWorld}.");
        Record($"Boot and rod overlap: {stepOverlap:F5} m at {stepTime:F3}s; rod wake travel={rodMotion:F6} m, rotation change={rodRotationMotion:F5} degrees, scale change={rodScaleMotion:F6}; initial rod position={initialRodPosition:F6}, rotation={initialRodRotation:F6}, scale={initialRodScale:F6}.");
        Record($"Wake floor: max consecutive near-floor horizontal boot step={wakeFootSlip:F5} m; max floor penetration={wakeFootDepth:F5} m.");
        if (strictComposition)
        {
            if (!moveDuringReveal)
                Metric(Mathf.Abs(percent) <= 0.25f, $"Opening the gate retained the original camera framing ({percent:F2}% edge change).");
            Metric(revealFirstFrameStep <= extent * 0.02f && !camClipsWorld,
                "Gate reveal began smoothly and camera remained clear of colliders.");
            Metric(stepOverlap <= 0.0001f && rodMotion <= 0.0001f && rodRotationMotion <= 0.01f && rodScaleMotion <= 0.0001f,
                "Bed-exit boots cleared the unchanged rod.");
        }
        string walls = $"Key drop vs wall: {keyWallOverlap:F5} m, {keyWallSamples} samples ({keyWallPeak}).";
        string walking = $"Walking legs vs rod: {walkingRodOverlap:F5} m, {walkingRodSamples} samples ({walkingRodPeak}).";
        if (strictSurfaceClearance)
        {
            Metric(keyWallSamples > 0 && keyWallMeshes.Length > 0 && keyWallOverlap <= 0.0001f, walls);
            Metric(walkingRodSamples > 0 && walkingRodOverlap <= 0.0001f, walking);
        }
        else
        {
            Record(walls);
            Record(walking);
        }
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
