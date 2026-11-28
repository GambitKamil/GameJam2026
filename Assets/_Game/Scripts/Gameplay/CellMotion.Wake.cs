using System;
using System.Collections;
using UnityEngine;

public sealed partial class CellMotion
{
    [SerializeField, Min(0.1f)] private float wakeDurationScale = 1f;

    [SerializeField, Min(0.005f)] private float wakeRodClearance = 0.012f;

    private Transform wakeRod;
    private Vector3 wakeRodCenter;
    private Vector3 wakeRodAxis;
    private Vector3 wakeRodNormal;
    private float wakeRodHalfWidth;
    private float wakeRodHalfLength;
    private float wakeRodTop;

    internal void SetWakeRod(Transform rod)
    {
        wakeRod = rod;
    }

    private SideViewCamera openingCamera;
    private Bounds wakeMattress;
    private float wakeBedWeight;
    private float wakeRestBlend;
    private float seatedBlend;
    private Vector3 wakeHips;
    private Vector3 wakeNearHome;
    private Vector3 wakeFarHome;

    private IEnumerator Start()
    {
        if (bunkMattress == null)
            yield break;

        Busy = WakingUp = true;
        if (peasantVisual != null && Camera.main != null)
            openingCamera = Camera.main.GetComponent<SideViewCamera>();
        wakeHips = (frontLeg.Hip + backLeg.Hip) * 0.5f;
        wakeNearHome = nearBoot.localPosition;
        wakeFarHome = farBoot.localPosition;
        CacheWakeRod();
        Bounds mattress = bunkMattress.bounds;
        wakeMattress = mattress;
        // The cushion rounds away from its top plane right at the rim.
        wakeMattress.Expand(new Vector3(-0.04f, 0f, -0.025f));
        Vector3 standing = transform.TransformPoint(rest + wakeHips);
        Vector3 nearFoot = WakePlant(transform.TransformPoint(rest + wakeNearHome), nearBoot, nearFootContact);
        Vector3 farHome = transform.TransformPoint(rest + wakeFarHome);
        Vector3 farFoot = WakePlant(farHome, farBoot, farFootContact);
        Vector3 parkedFarFoot = WakePlant(farHome + Vector3.forward * 0.24f, farBoot, farFootContact);
        float farSoleDepth = farBoot.position.y - farBoot.GetComponent<Renderer>().bounds.min.y;
        Vector3 weightTransfer = standing + new Vector3(0f, -0.24f, 0.26f);
        Vector3 halfRise = standing + new Vector3(0f, -0.105f, 0.13f);

        TryGetComponent(out CapsuleCollider capsule);
        float capsuleHeight = capsule == null ? 0f : capsule.height;
        Vector3 capsuleCenter = capsule == null ? Vector3.zero : capsule.center;
        SetWakeCapsule(capsule, capsuleHeight, capsuleCenter, 0f);

        // Find the resting height once. The following motion is driven by the hips and each foot.
        var lying = new Vector3(mattress.center.x, mattress.max.y + 0.12f, mattress.center.z);
        Quaternion lyingRotation = Quaternion.Euler(0f, 0f, 90f);
        Vector3 tuck = new Vector3(-0.0135f, -0.0387f, 0f);
        PoseWakeLocal(lying, lyingRotation, tuck, -3f, 18f, wakeNearHome, wakeFarHome);
        float bottom = float.PositiveInfinity;
        foreach (Renderer part in figure.GetComponentsInChildren<Renderer>())
            if (part.enabled)
                bottom = Mathf.Min(bottom, part.bounds.min.y);
        if (!float.IsPositiveInfinity(bottom))
            lying.y += Mathf.Max(0f, mattress.max.y + 0.012f - bottom);
        PoseWakeLocal(lying, lyingRotation, tuck, -3f, 18f, wakeNearHome, wakeFarHome);
        wakeBedWeight = 1f;
        wakeRestBlend = 1f;
        ApplyWakeVisual(true);
        yield return new WaitForSeconds(0.15f * wakeDurationScale);

        var rolled = new Vector3(standing.x, Mathf.Max(lying.y, mattress.max.y + 0.26f), mattress.min.z + 0.27f);
        Quaternion rolledRotation = Quaternion.Euler(0f, 20f, 72f);
        Vector3 nearTucked = wakeNearHome + new Vector3(0.24f, 0.04f, 0f);
        Vector3 farTucked = wakeFarHome + new Vector3(0.06f, 0.04f, 0f);
        yield return MoveWake(0.42f, t =>
        {
            wakeRestBlend = 1f - t;
            PoseWakeLocal(Vector3.Lerp(lying, rolled, t), Quaternion.Slerp(lyingRotation, rolledRotation, t),
                tuck * (1f - t), Mathf.Lerp(-3f, -16f, t), Mathf.Lerp(18f, 40f, t),
                Vector3.Lerp(wakeNearHome, nearTucked, t), Vector3.Lerp(wakeFarHome, farTucked, t));
        });

        Vector3 nearRolled = nearBoot.position;
        Vector3 farRolled = farBoot.position;
        Quaternion seatedRotation = Quaternion.Euler(0f, 82f, 0f);
        var seated = new Vector3(standing.x, mattress.max.y + 0.17f, mattress.min.z + 0.025f);
        bool enlargedBoots = peasantVisual != null;
        float nearRimClearance = enlargedBoots ? 0.49f : 0.39f;
        float farRimClearance = enlargedBoots ? 0.44f : 0.30f;
        var nearDangling = new Vector3(seated.x - 0.12f, nearFoot.y + 0.25f, mattress.min.z - nearRimClearance);
        var farDangling = new Vector3(seated.x + 0.12f, farFoot.y + 0.27f, mattress.min.z - farRimClearance);
        yield return MoveWake(0.65f, t =>
        {
            seatedBlend = t;
            Quaternion rotation = Quaternion.Slerp(rolledRotation, seatedRotation, t);
            PoseWakeWorld(Vector3.Lerp(rolled, seated, t), rotation, Mathf.Lerp(-16f, -10f, t),
                Mathf.Lerp(40f, -24f, t),
                ClearWakeBed(nearRolled, nearDangling, mattress, t, enlargedBoots),
                ClearWakeBed(farRolled, farDangling, mattress, t, enlargedBoots),
                rotation, rotation);
        });

        Quaternion plantedRotation = Quaternion.Euler(0f, 59f, 0f);
        yield return MoveWake(0.59f, t =>
        {
            seatedBlend = 1f - t;
            // Hand over the weight from the mattress before the second boot takes the floor.
            wakeBedWeight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.78f, t));
            float farProgress = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.78f, t));
            Quaternion rotation = Quaternion.Slerp(seatedRotation, plantedRotation, t);
            SetWakeCapsule(capsule, capsuleHeight, capsuleCenter, t * 0.28f);
            PoseWakeWorld(Vector3.Lerp(seated, weightTransfer, t), rotation, Mathf.Lerp(-10f, -31f, t),
                Mathf.Lerp(-24f, 28f, t),
                WakeFootArc(nearDangling, nearFoot, t, 0.045f),
                WakeStepAcrossRod(farDangling, parkedFarFoot, farProgress, 0.034f, farSoleDepth),
                Quaternion.Slerp(seatedRotation, Quaternion.identity, t),
                Quaternion.Slerp(seatedRotation, plantedRotation, farProgress), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.75f, t)));
        });
        footsteps.PlayOneShot(boot, 0.24f);

        Quaternion midTurn = Quaternion.Euler(0f, 28f, 0f);
        yield return MoveWake(0.34f, t =>
        {
            SetWakeCapsule(capsule, capsuleHeight, capsuleCenter, Mathf.Lerp(0.28f, 0.67f, t));
            PoseWakeWorld(Vector3.Lerp(weightTransfer, halfRise, t),
                Quaternion.Slerp(plantedRotation, midTurn, t), Mathf.Lerp(-31f, -14f, t), Mathf.Lerp(28f, 15f, t),
                nearFoot, parkedFarFoot, Quaternion.identity, plantedRotation, 1f);
        });

        yield return MoveWake(0.38f, t =>
        {
            // The near foot bears the weight while the trailing foot steps onto the movement lane.
            float step = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.82f, t));
            Vector3 liveStanding = transform.TransformPoint(rest + wakeHips);
            Vector3 liveFarFoot = wakeRodNormal == Vector3.zero ? transform.TransformPoint(rest + wakeFarHome) : farFoot;
            float pitch = Mathf.Lerp(-14f, 0f, t);
            if (library != null)
            {
                var sample = library.Sample(CellAnimationLibrary.Motion.SitStand, t);
                pitch = Mathf.Lerp(pitch, Mathf.Clamp(sample.TorsoPitch, -24f, 3f), 0.35f * Mathf.Sin(t * Mathf.PI));
            }

            SetWakeCapsule(capsule, capsuleHeight, capsuleCenter, Mathf.Lerp(0.67f, 1f, t));
            PoseWakeWorld(Vector3.Lerp(halfRise, liveStanding, t), Quaternion.Slerp(midTurn, Quaternion.identity, t),
                pitch, Mathf.Lerp(15f, 0f, t), nearFoot, WakeStepAcrossRod(parkedFarFoot, liveFarFoot, step, 0.075f, farSoleDepth),
                Quaternion.identity, Quaternion.Slerp(plantedRotation, Quaternion.identity, step), 1f);
        });

        if (wakeRodNormal != Vector3.zero && player.IsGrounded)
        {
            nearFootContact.Plant(this, nearBoot.position, Quaternion.identity);
            farFootContact.Plant(this, farBoot.position, Quaternion.identity);
            hasFootContacts = true;
        }

        openingCamera?.FrameOpening(null);
        WakingUp = false;
        EndAction();
    }

    private void ApplyWakeVisual(bool snapCamera = false)
    {
        if (peasantVisual == null)
            return;

        Bounds? support = wakeBedWeight > 0f ? wakeMattress : null;
        peasantVisual.Apply(0f, false, seatedBlend, bedPose: wakeBedWeight,
            bedSupport: support, resting: wakeRestBlend, snapBedSupport: snapCamera);
        Vector3 framingPoint = peasantVisual.HeadPosition - Vector3.right * (0.16f * wakeRestBlend);
        openingCamera?.FrameOpening(framingPoint, snapCamera);
    }

    private IEnumerator MoveWake(float duration, Action<float> sample)
    {
        duration *= wakeDurationScale;
        for (float elapsed = Time.deltaTime; elapsed < duration; elapsed += Time.deltaTime)
        {
            sample(Mathf.SmoothStep(0f, 1f, elapsed / duration));
            yield return null;
        }

        sample(1f);
    }

    private void PoseWakeWorld(Vector3 pelvis, Quaternion rotation, float pitch, float arm,
        Vector3 nearTarget, Vector3 farTarget, Quaternion nearRotation, Quaternion farRotation, float plantedWeight = 0f)
    {
        if (wakeRodNormal != Vector3.zero && plantedWeight > 0f)
            pelvis.y -= plantedWeight * Mathf.Max(WakePlantHipDrop(frontLeg, pelvis, rotation, nearTarget),
                WakePlantHipDrop(backLeg, pelvis, rotation, farTarget));
        PlaceWakeHips(pelvis, rotation, Vector3.zero, pitch, arm);
        PoseWakeLegs(Vector3.zero, figure.InverseTransformPoint(nearTarget), figure.InverseTransformPoint(farTarget));
        nearBoot.rotation = nearRotation;
        farBoot.rotation = farRotation;
    }

    private void PoseWakeLocal(Vector3 pelvis, Quaternion rotation, Vector3 hipOffset, float pitch, float arm,
        Vector3 nearTarget, Vector3 farTarget)
    {
        PlaceWakeHips(pelvis, rotation, hipOffset, pitch, arm);
        PoseWakeLegs(hipOffset, nearTarget, farTarget);
    }

    private void PlaceWakeHips(Vector3 pelvis, Quaternion rotation, Vector3 hipOffset, float pitch, float arm)
    {
        torso.localPosition = wakeHips + hipOffset;
        torso.localRotation = Quaternion.Euler(0f, 0f, pitch);
        shoulder.localRotation = Quaternion.Euler(0f, 0f, arm);
        figure.SetPositionAndRotation(pelvis - rotation * torso.localPosition, rotation);
    }

    private void PoseWakeLegs(Vector3 hipOffset, Vector3 nearTarget, Vector3 farTarget)
    {
        frontLeg.Pose(0f, 0f, hipOffset, 0f, nearTarget - wakeNearHome, 1f);
        backLeg.Pose(0f, 0f, hipOffset, 0f, farTarget - wakeFarHome, 1f);
    }

    private void CacheWakeRod()
    {
        if (wakeRod == null)
            return;

        Renderer shaft = null;
        foreach (Renderer part in wakeRod.GetComponentsInChildren<Renderer>())
            if (part.enabled && (shaft == null || part.bounds.size.sqrMagnitude > shaft.bounds.size.sqrMagnitude))
                shaft = part;
        if (shaft == null)
            return;

        sampledRodPosition = wakeRod.position;
        sampledRodRotation = wakeRod.rotation;
        wakeRodAxis = Vector3.ProjectOnPlane(wakeRod.right, Vector3.up).normalized;
        wakeRodNormal = Vector3.Cross(wakeRodAxis, Vector3.up);
        wakeRodCenter = shaft.bounds.center;
        wakeRodHalfWidth = WakeExtent(shaft, wakeRodNormal);
        wakeRodHalfLength = WakeExtent(shaft, wakeRodAxis);
        wakeRodTop = shaft.bounds.max.y;
    }

    private static float WakeExtent(Renderer part, Vector3 axis)
    {
        Vector3 localAxis = part.localToWorldMatrix.transpose.MultiplyVector(axis);
        var absolute = new Vector3(Mathf.Abs(localAxis.x), Mathf.Abs(localAxis.y), Mathf.Abs(localAxis.z));
        return Vector3.Dot(part.localBounds.extents, absolute);
    }

    private Vector3 WakePlant(Vector3 desired, Transform shoe, FootContact contact)
    {
        if (wakeRodNormal == Vector3.zero)
            return desired;

        float radius = 0f;
        foreach (Renderer part in shoe.GetComponentsInChildren<Renderer>())
        {
            if (!part.enabled)
                continue;
            Vector3 edge = part.bounds.extents;
            Vector3 center = part.bounds.center - shoe.position;
            radius = Mathf.Max(radius, new Vector2(Mathf.Abs(center.x) + edge.x, Mathf.Abs(center.z) + edge.z).magnitude);
        }

        Vector3 offset = desired - wakeRodCenter;
        float across = Vector3.Dot(offset, wakeRodNormal);
        float margin = wakeRodHalfWidth + radius + wakeRodClearance;
        // The shoelaces can rotate during the step. Use the boot's full horizontal envelope.
        if (Mathf.Abs(Vector3.Dot(offset, wakeRodAxis)) <= wakeRodHalfLength + radius && Mathf.Abs(across) < margin)
            desired += wakeRodNormal * (Mathf.Sign(across) * margin - across);

        contact.FindSupport(this, desired, out Vector3 landed, out _);
        return landed;
    }

    private Vector3 WakeStepAcrossRod(Vector3 from, Vector3 to, float t, float lift, float soleDepth)
    {
        float startSide = Vector3.Dot(from - wakeRodCenter, wakeRodNormal);
        float endSide = Vector3.Dot(to - wakeRodCenter, wakeRodNormal);
        if (startSide * endSide >= 0f)
            return WakeFootArc(from, to, t, lift);

        return StepOverRod(from, to, t, wakeRodTop + soleDepth + wakeRodClearance);
    }

    private static Vector3 StepOverRod(Vector3 from, Vector3 to, float t, float clearance)
    {
        Vector3 raisedFrom = new Vector3(from.x, Mathf.Max(from.y, clearance), from.z);
        Vector3 raisedTo = new Vector3(to.x, Mathf.Max(to.y, clearance), to.z);
        // Clear the shaft before crossing it, then lower the boot once the sole is past it.
        const float liftedAt = 0.24f;
        const float overAt = 0.72f;
        if (t < liftedAt)
            return Vector3.Lerp(from, raisedFrom, Mathf.SmoothStep(0f, 1f, t / liftedAt));
        if (t > overAt)
            return Vector3.Lerp(raisedTo, to, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(overAt, 1f, t)));
        return Vector3.Lerp(raisedFrom, raisedTo, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(liftedAt, overAt, t)));
    }

    private float WakePlantHipDrop(LegPose leg, Vector3 pelvis, Quaternion rotation, Vector3 foot)
    {
        Vector3 hip = pelvis + rotation * (leg.Hip - wakeHips);
        Vector3 ankle = foot + rotation * leg.AnkleOffset;
        Vector3 delta = hip - ankle;
        float reach = leg.MaximumReach - 0.005f;
        float verticalReach = Mathf.Sqrt(Mathf.Max(0.02f, reach * reach - delta.x * delta.x - delta.z * delta.z));
        return Mathf.Max(0f, delta.y - verticalReach);
    }

    private static Vector3 ClearWakeBed(Vector3 from, Vector3 to, Bounds mattress, float t, bool enlargedBoot)
    {
        if (enlargedBoot)
        {
            // Lift, carry the heel across the whole rim, then let the boot hang from the knee.
            const float liftUntil = 0.27f;
            const float pastRim = 0.78f;
            float liftedY = Mathf.Max(from.y, mattress.max.y + 0.29f);
            var lifted = new Vector3(from.x, liftedY, from.z);
            var clearRim = new Vector3(to.x, liftedY, to.z);
            if (t < liftUntil)
                return Vector3.Lerp(from, lifted, Mathf.SmoothStep(0f, 1f, t / liftUntil));
            if (t < pastRim)
                return Vector3.Lerp(lifted, clearRim, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(liftUntil, pastRim, t)));
            return Vector3.Lerp(clearRim, to, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(pastRim, 1f, t)));
        }

        // Keep the boot over the mattress until its heel has crossed the frame's front edge.
        var clear = new Vector3(to.x, mattress.max.y + 0.155f, mattress.min.z - 0.26f);
        const float clearancePhase = 0.65f;
        if (t <= clearancePhase)
            return WakeFootArc(from, clear, Mathf.SmoothStep(0f, 1f, t / clearancePhase), 0.05f);
        return Vector3.Lerp(clear, to, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(clearancePhase, 1f, t)));
    }

    private static Vector3 WakeFootArc(Vector3 from, Vector3 to, float t, float lift)
    {
        Vector3 point = Vector3.Lerp(from, to, t);
        point.y += 4f * lift * t * (1f - t);
        return point;
    }

    private static void SetWakeCapsule(CapsuleCollider capsule, float originalHeight, Vector3 originalCenter, float expansion)
    {
        if (capsule == null || capsule.direction != 1)
            return;

        // The simulation stays in the empty lane. Keep the capsule's floor contact fixed as it grows.
        float height = Mathf.Lerp(capsule.radius * 2f, originalHeight, expansion);
        capsule.center = originalCenter - Vector3.up * ((originalHeight - height) * 0.5f);
        capsule.height = height;
    }
}
