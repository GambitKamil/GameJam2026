using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

internal static partial class CellPolishVerification
{
    private const int DefaultSampleRate = 60;
    private static int SampleRate;
    private const float SupportTolerance = 0.02f;
    private const float SlipTolerance = 0.005f;
    private const float AimTolerance = 5f;
    private const float AimStepTolerance = 45f;
    private const float MouseSupportTolerance = 0.01f;
    private const float MouseSlipTolerance = 0.003f;
    private static readonly Vector3[] AimDirections = { new Vector3(0.8f, 0.6f), new Vector3(-0.8f, 0.6f) };
    private static readonly string[] AimNames = { "right/up", "left/up" };
    private static readonly string[] WakeNames = { "02-wake-sit", "02b-wake-plant", "02c-wake-rise" };
    private static readonly float[] WakeTimes = { 1.15f, 1.83f, 2.25f };
    private static readonly RaycastHit[] supportHits = new RaycastHit[32];
    private static readonly List<Vector2> tailSamples = new List<Vector2>();
    private static readonly ContactProbe[] feet = { new ContactProbe(), new ContactProbe() };
    private static readonly ContactProbe[] paws = { new ContactProbe(), new ContactProbe(), new ContactProbe(), new ContactProbe() };
    private static Transform torso, elbow;
    private static LegProbe nearLegProbe, farLegProbe;
    private static object mouseMotion;
    private static MethodInfo setAim, getPaw, getPawContactSequence;
    private static FieldInfo aimBlend;
    private static PropertyInfo grounded, airborne, tailBase, tailTip;
    private static float armSegment, armDrift, wakeHipStep, wakeSeam, wakeFloorDepth, cameraStep, gripGap;
    private static float aimIssued, aimSettled, lastAimAngle, aimStep;
    private static Vector3 lastHip, lastCamera, lastArmDirection;
    private static string cameraPeak;
    private static int aimPhase, wakeMilestone, supportedFrames, unsupportedFrames, playerPawlessFrames;
    private static int firstDiagonalFrames, secondDiagonalFrames, airborneFrames, metricFailures, armSamples;
    private static string gripPeak;
    private static bool wakingLastFrame, aiming, hasFrame;

    private static void InitializeMetrics()
    {
        armDrift = wakeHipStep = wakeSeam = wakeFloorDepth = cameraStep = gripGap = aimStep = 0f;
        aimPhase = wakeMilestone = supportedFrames = unsupportedFrames = playerPawlessFrames = 0;
        firstDiagonalFrames = secondDiagonalFrames = airborneFrames = metricFailures = armSamples = 0;
        cameraPeak = gripPeak = "none";
        wakingLastFrame = aiming = hasFrame = false;
        torso = Field<Transform>(motion, "torso");
        elbow = OptionalField<Transform>(motion, "forearm");
        armSegment = OptionalField<float>(motion, "armSegmentLength");
        nearLegProbe = new LegProbe(Field<object>(motion, "frontLeg"));
        farLegProbe = new LegProbe(Field<object>(motion, "backLeg"));
        feet[0].Reset(OptionalField<object>(motion, "nearFootContact"));
        feet[1].Reset(OptionalField<object>(motion, "farFootContact"));
        foreach (var paw in paws)
            paw.Reset(null);
        tailSamples.Clear();
        setAim = motion.GetType().GetMethod("SetAim");
        aimBlend = motion.GetType().GetField("aimBlend", Instance);
        grounded = player.GetType().GetProperty("IsGrounded", Instance | BindingFlags.Public);
        mouseMotion = OptionalField<object>(escape, "mouseMotion");
        getPaw = mouseMotion?.GetType().GetMethod("TryGetPaw");
        getPawContactSequence = mouseMotion?.GetType().GetMethod("GetPawContactSequence");
        airborne = mouseMotion?.GetType().GetProperty("IsAirborne");
        tailBase = mouseMotion?.GetType().GetProperty("TailBaseAngle");
        tailTip = mouseMotion?.GetType().GetProperty("TailTipAngle");
    }

    private static T OptionalField<T>(object target, string name)
    {
        return target.GetType().GetField(name, Instance) is FieldInfo field ? (T)field.GetValue(target) : default;
    }

    private static void SampleMotion()
    {
        Vector3 cameraPosition = view.transform.position;
        float cameraDelta = Vector3.Distance(cameraPosition, lastCamera);
        // The verifier overrides Camera.aspect after Awake. Measure motion after that update.
        if (hasFrame && Time.time - began > 1.5f / SampleRate && cameraDelta > cameraStep)
        {
            cameraStep = cameraDelta;
            cameraPeak = $"from {lastCamera:F4} to {cameraPosition:F4} in phase {phase}, {Stage()} at {Time.time - began:F3}s";
        }
        lastCamera = cameraPosition;
        hasFrame = true;

        if (motion.WakingUp)
        {
            if (wakingLastFrame)
                wakeHipStep = Mathf.Max(wakeHipStep, Vector3.Distance(torso.position, lastHip));
            lastHip = torso.position;
            wakeFloorDepth = Mathf.Max(wakeFloorDepth, -SoleGap(nearBoot), -SoleGap(farBoot));
        }
        else if (wakingLastFrame)
            wakeSeam = Vector3.Distance(torso.position, lastHip);
        wakingLastFrame = motion.WakingUp;

        if (elbow != null && armSegment > 0f)
        {
            armSamples++;
            armDrift = Mathf.Max(armDrift, RelativeLength(shoulder.position, elbow.position, armSegment),
                RelativeLength(elbow.position, hand.position, armSegment));
            Debug.DrawLine(shoulder.position, elbow.position, Color.yellow);
            Debug.DrawLine(elbow.position, hand.position, Color.yellow);
        }

        Vector3 offset = torso.localPosition - (nearLegProbe.Hip + farLegProbe.Hip) * 0.5f;
        nearLegProbe.Sample(offset);
        farLegProbe.Sample(offset);
        if (OptionalField<float>(motion, "handTargetWeight") >= 0.95f)
        {
            Vector3 target = Field<Vector3>(motion, "handTarget");
            float error = Vector3.Distance(target, hand.position);
            if (error > gripGap)
            {
                gripGap = error;
                gripPeak = $"{Stage()} at {Time.time - began:F3}s, shoulder={shoulder.position:F3}, wrist={hand.position:F3}, target={target:F3}";
            }
            Debug.DrawLine(hand.position, target, Color.magenta);
        }

        if (!motion.WakingUp)
            SampleFeet();
        if (getPaw != null && mouse.gameObject.activeInHierarchy)
            SamplePaws();
    }

    private static void SampleFeet()
    {
        if (grounded != null && !(bool)grounded.GetValue(player))
        {
            unsupportedFrames++;
            foreach (var foot in feet)
                foot.ClearContact();
            return;
        }

        supportedFrames++;
        bool near = SampleFoot(feet[0], nearBoot);
        bool far = SampleFoot(feet[1], farBoot);
        if (!near && !far)
            playerPawlessFrames++;
    }

    private static bool SampleFoot(ContactProbe contact, Transform boot)
    {
        float gap = SoleGap(boot);
        if (contact.Pose != null)
        {
            contact.Sample(boot.position, (bool)contact.Planted.GetValue(contact.Pose), gap,
                contact.Support?.GetValue(contact.Pose) as Collider);
        }
        return Mathf.Abs(gap) <= SupportTolerance;
    }

    private static float SoleGap(Transform boot)
    {
        Renderer renderer = boot.GetComponent<Renderer>();
        if (renderer == null)
            return float.PositiveInfinity;

        Vector3 sole = new Vector3(boot.position.x, renderer.bounds.min.y, boot.position.z);
        return Gap(sole, player.transform);
    }

    private static float Gap(Vector3 sole, Transform self)
    {
        Vector3 start = sole + Vector3.up * 0.30f;
        int count = Physics.RaycastNonAlloc(start, Vector3.down, supportHits, 0.70f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = supportHits[i];
            if (hit.normal.y >= 0.7f && !hit.transform.IsChildOf(self))
                nearest = Mathf.Min(nearest, hit.distance);
        }

        float gap = nearest - 0.30f;
        if (!float.IsPositiveInfinity(gap))
            Debug.DrawLine(sole, sole - Vector3.up * gap, gap < 0f ? Color.red : Color.cyan);
        return gap;
    }

    private static void ProbeFloor()
    {
        float near = SoleGap(nearBoot);
        float far = SoleGap(farBoot);
        if (!float.IsInfinity(near))
        {
            floorGap = Mathf.Max(floorGap, near);
            floorDepth = Mathf.Max(floorDepth, -near);
        }
        if (!float.IsInfinity(far))
        {
            floorGap = Mathf.Max(floorGap, far);
            floorDepth = Mathf.Max(floorDepth, -far);
        }
    }

    private static void SamplePaws()
    {
        bool flying = (bool)airborne.GetValue(mouseMotion);
        if (flying)
            airborneFrames++;
        int contacts = 0;
        for (int i = 0; i < paws.Length; i++)
        {
            object[] arguments = { i, Vector3.zero, false };
            if (!(bool)getPaw.Invoke(mouseMotion, arguments))
                continue;
            bool planted = (bool)arguments[2] && !flying;
            Vector3 sole = (Vector3)arguments[1];
            int contact = planted && getPawContactSequence != null
                ? (int)getPawContactSequence.Invoke(mouseMotion, new object[] { i })
                : 0;
            paws[i].Sample(sole, planted, planted ? Gap(sole, mouse) : 0f, contactId: contact);
            if (planted)
                contacts |= 1 << i;
        }

        if (contacts == 9)
            firstDiagonalFrames++;
        if (contacts == 6)
            secondDiagonalFrames++;
        tailSamples.Add(new Vector2((float)tailBase.GetValue(mouseMotion), (float)tailTip.GetValue(mouseMotion)));
    }

    private static void CaptureWakeMilestones(float elapsed)
    {
        if (wakeMilestone >= WakeTimes.Length || elapsed < WakeTimes[wakeMilestone])
            return;
        Capture(WakeNames[wakeMilestone]);
        wakeMilestone++;
    }

    private static bool TickAim()
    {
        bool released = aimPhase == AimDirections.Length;
        if (!aiming)
        {
            aimIssued = Time.time;
            aimSettled = -1f;
            lastAimAngle = aimStep = 0f;
            lastArmDirection = hand.position - (elbow != null ? elbow.position : shoulder.position);
            if (released)
                Call(bread, "EndAim");
            setAim?.Invoke(motion, new object[] { released ? Vector3.zero : AimDirections[aimPhase], !released });
            aiming = true;
            return false;
        }

        float elapsed = Time.time - aimIssued;
        Vector3 upper = hand.position - shoulder.position;
        Vector3 forearm = elbow != null ? hand.position - elbow.position : upper;
        aimStep = Mathf.Max(aimStep, Vector3.Angle(lastArmDirection, forearm));
        lastArmDirection = forearm;
        if (released)
        {
            if (aimBlend != null && (float)aimBlend.GetValue(motion) <= 0.005f && aimSettled < 0f)
                aimSettled = elapsed;
            if (elapsed < 0.26f)
                return false;
            Metric(aimSettled >= 0f && aimSettled < 0.25f && aimStep <= AimStepTolerance * DefaultSampleRate / SampleRate,
                $"Aim release: blend-out={TimeLabel(aimSettled)}, maximum forearm step={aimStep:F2} degrees");
            return true;
        }

        Vector3 direction = AimDirections[aimPhase];
        lastAimAngle = Mathf.Max(Vector3.Angle(upper, direction), Vector3.Angle(forearm, direction));
        Debug.DrawLine(shoulder.position, shoulder.position + direction, Color.green);
        if (lastAimAngle <= AimTolerance && aimSettled < 0f)
            aimSettled = elapsed;
        if (elapsed < 0.35f)
            return false;
        Metric(lastAimAngle <= AimTolerance && aimSettled >= 0f && aimSettled < 0.25f && aimStep <= AimStepTolerance * DefaultSampleRate / SampleRate,
            $"Aim {AimNames[aimPhase]}: steady-state arm/forearm error={lastAimAngle:F2} degrees, settle={TimeLabel(aimSettled)}, maximum forearm step={aimStep:F2} degrees");
        DriveBreadAim(direction);
        var guide = Field<LineRenderer>(bread, "preview");
        float gap = Vector3.Distance(guide.GetPosition(0), motion.HandPosition);
        Metric(gap <= 0.001f, $"Aim {AimNames[aimPhase]} guide to hand: {gap:F4} m.");
        Capture(aimPhase == 0 ? "04-aim-right" : "04b-aim-left");
        aimPhase++;
        aiming = false;
        return false;
    }

    private static bool TailLag(out float maxBase, out float maxTip, out float lagSeconds, out float correlation)
    {
        maxBase = maxTip = lagSeconds = correlation = 0f;
        foreach (Vector2 sample in tailSamples)
        {
            maxBase = Mathf.Max(maxBase, Mathf.Abs(sample.x));
            maxTip = Mathf.Max(maxTip, Mathf.Abs(sample.y));
        }

        if (tailSamples.Count < SampleRate || maxBase < 1f || maxTip < 0.25f)
            return false;

        for (int lag = 0; lag <= SampleRate / 2; lag++)
        {
            float dot = 0f, baseEnergy = 0f, tipEnergy = 0f;
            for (int i = lag; i < tailSamples.Count; i++)
            {
                float source = tailSamples[i - lag].x;
                float tip = tailSamples[i].y;
                dot += source * tip;
                baseEnergy += source * source;
                tipEnergy += tip * tip;
            }
            float value = dot / Mathf.Sqrt(Mathf.Max(0.0001f, baseEnergy * tipEnergy));
            if (value > correlation)
            {
                correlation = value;
                lagSeconds = (float)lag / SampleRate;
            }
        }
        return lagSeconds > 0f && correlation >= 0.30f;
    }

    private static void ReportMotionMetrics()
    {
        Metric(errors == 0, $"Gameplay warnings/errors: {errors}");
        float legDrift = Mathf.Max(nearLegProbe.Drift, farLegProbe.Drift);
        Metric(armSamples > 0 && nearLegProbe.Samples > 0 && farLegProbe.Samples > 0 && armDrift <= 0.01f && legDrift <= 0.01f,
            $"Player segment length drift: arm={armDrift:P3}, legs={legDrift:P3}, arm samples={armSamples}, leg samples={nearLegProbe.Samples}/{farLegProbe.Samples}");
        Metric(wakeSeam <= 0.05f && wakeHipStep <= 0.08f * DefaultSampleRate / SampleRate && wakeFloorDepth <= SupportTolerance,
            $"Wake: hip step={wakeHipStep:F4} m, stand seam={wakeSeam:F4} m, boot penetration={wakeFloorDepth:F4} m");
        Record($"General motion: max camera step={cameraStep:F4} m ({cameraPeak}), max grip-to-target gap at >=95% reach weight={gripGap:F4} m ({gripPeak})");
        Metric(grounded != null && supportedFrames > 0 && playerPawlessFrames == 0,
            $"Player support: {supportedFrames} grounded, {unsupportedFrames} airborne, {playerPawlessFrames} grounded without a boot inside 20 mm of support");
        ReportContact("Player near boot", feet[0], false);
        ReportContact("Player far boot", feet[1], false);
        Metric(firstDiagonalFrames > 0 && secondDiagonalFrames > 0 && airborneFrames > 0,
            $"Mouse gait: first diagonal={firstDiagonalFrames} frames, second diagonal={secondDiagonalFrames} frames, airborne={airborneFrames} frames");
        for (int i = 0; i < paws.Length; i++)
            ReportContact($"Mouse paw {i}", paws[i], true);
        bool lag = TailLag(out float maxBase, out float maxTip, out float lagSeconds, out float correlation);
        Metric(lag, $"Mouse tail: peak base={maxBase:F2} degrees, peak tip={maxTip:F2} degrees, lag={lagSeconds:F3}s, correlation={correlation:F2}");
        Record(metricFailures == 0 ? "PASS: motion checks" : $"FAIL: {metricFailures} motion checks");
    }

    private static void ReportContact(string name, ContactProbe probe, bool isMouse)
    {
        bool valid = probe.SupportFrames > 0 && (!isMouse || probe.SwingFrames > 0) &&
            probe.Gap <= (isMouse ? MouseSupportTolerance : SupportTolerance) &&
            probe.Slip <= (isMouse ? MouseSlipTolerance : SlipTolerance);
        Metric(valid, $"{name}: planted={probe.SupportFrames}, swing={probe.SwingFrames}, max sole gap={probe.Gap:F4} m, max consecutive planted slip={probe.Slip:F4} m");
        if ((!valid || isMouse) && probe.SupportFrames > 0)
            Record($"    {name} peaks: gap {probe.GapPeak}, slip {probe.SlipPeak}, skipped frame pairs={probe.SkippedPairs}, distinct adjacent contacts={probe.ReplantPairs}, largest adjacent replant delta={probe.ReplantStep:F4} m");
    }

    private static void Metric(bool passed, string details)
    {
        if (!passed)
            metricFailures++;
        Record($"{(passed ? "PASS" : "FAIL")}: {details}");
    }

    private static string TimeLabel(float seconds) => seconds < 0f ? "not reached" : $"{seconds:F3}s";

    private static float RelativeLength(Vector3 from, Vector3 to, float rest)
    {
        return Mathf.Abs(Vector3.Distance(from, to) / rest - 1f);
    }

    private sealed class ContactProbe
    {
        public object Pose { get; private set; }
        public PropertyInfo Planted { get; private set; }
        public PropertyInfo Support { get; private set; }
        public int SupportFrames { get; private set; }
        public int SwingFrames { get; private set; }
        public float Gap { get; private set; }
        public float Slip { get; private set; }
        public int SkippedPairs { get; private set; }
        public int ReplantPairs { get; private set; }
        public float ReplantStep { get; private set; }
        public string GapPeak { get; private set; }
        public string SlipPeak { get; private set; }
        private int previousFrame;
        private int previousContactId;
        private Vector3 previousPosition;
        private Collider previousSupport;
        private bool previousPlanted;

        public void Reset(object pose)
        {
            Pose = pose;
            Planted = pose?.GetType().GetProperty("Planted");
            Support = pose?.GetType().GetProperty("Support");
            SupportFrames = SwingFrames = SkippedPairs = ReplantPairs = 0;
            Gap = Slip = ReplantStep = 0f;
            GapPeak = SlipPeak = "none";
            previousPlanted = false;
        }

        public void ClearContact() => previousPlanted = false;

        public void Sample(Vector3 current, bool planted, float gap, Collider support = null, int contactId = 0)
        {
            if (planted)
            {
                SupportFrames++;
                if (Mathf.Abs(gap) > Gap)
                {
                    Gap = Mathf.Abs(gap);
                    GapPeak = $"{gap:F4} m at {Time.time - began:F3}s, sole={current:F3} in phase {phase}, {Stage()}";
                }
                if (previousPlanted && previousSupport == support)
                {
                    if (previousFrame + 1 == Time.frameCount)
                    {
                        float slip = Vector3.ProjectOnPlane(current - previousPosition, Vector3.up).magnitude;
                        if (contactId != previousContactId)
                        {
                            ReplantPairs++;
                            ReplantStep = Mathf.Max(ReplantStep, slip);
                        }
                        else if (slip > Slip)
                        {
                            Slip = slip;
                            SlipPeak = $"at {Time.time - began:F3}s, {previousPosition:F3} to {current:F3} in phase {phase}, {Stage()}";
                        }
                    }
                    else
                        SkippedPairs++;
                }
            }
            else
                SwingFrames++;

            previousPosition = current;
            previousFrame = Time.frameCount;
            previousContactId = contactId;
            previousPlanted = planted;
            previousSupport = support;
            Debug.DrawLine(current, current + Vector3.up * 0.05f, planted ? Color.green : Color.yellow);
        }
    }

    private sealed class LegProbe
    {
        public Vector3 Hip { get; }
        public bool Verifiable => segmentLength > 0f;
        public float Drift { get; private set; }
        public int Samples { get; private set; }
        private readonly float segmentLength;
        private readonly Transform thigh, shin, boot;
        private readonly Vector3 ankleOffset;

        public LegProbe(object pose)
        {
            const BindingFlags visible = Instance | BindingFlags.Public;
            var type = pose.GetType();
            Hip = (Vector3)type.GetProperty("Hip", visible).GetValue(pose);
            segmentLength = OptionalField<float>(pose, "segmentLength");
            if (!Verifiable)
                return;
            thigh = Field<Transform>(pose, "thigh");
            shin = Field<Transform>(pose, "shin");
            boot = Field<Transform>(pose, "boot");
            ankleOffset = (Vector3)type.GetProperty("AnkleOffset", visible).GetValue(pose);
        }

        public void Sample(Vector3 offset)
        {
            if (!Verifiable)
                return;

            Samples++;
            Vector3 hip = Hip + offset;
            Vector3 ankle = boot.localPosition + ankleOffset;
            Vector3 upperKnee = thigh.localPosition * 2f - hip;
            Vector3 lowerKnee = shin.localPosition * 2f - ankle;
            Drift = Mathf.Max(Drift, RelativeLength(hip, upperKnee, segmentLength),
                RelativeLength(ankle, lowerKnee, segmentLength), Vector3.Distance(upperKnee, lowerKnee) / segmentLength);
            Debug.DrawLine(figure.TransformPoint(hip), figure.TransformPoint(upperKnee), Color.yellow);
            Debug.DrawLine(figure.TransformPoint(lowerKnee), figure.TransformPoint(ankle), Color.yellow);
        }
    }
}
