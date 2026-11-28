using System;
using System.Collections;
using UnityEngine;

public sealed partial class CellMotion
{
    [SerializeField, Min(0.1f)] private float wakeDurationScale = 1f;

    private Vector3 wakeHips;
    private Vector3 wakeNearHome;
    private Vector3 wakeFarHome;

    private IEnumerator Start()
    {
        if (bunkMattress == null)
            yield break;

        Busy = WakingUp = true;
        wakeHips = (frontLeg.Hip + backLeg.Hip) * 0.5f;
        wakeNearHome = nearBoot.localPosition;
        wakeFarHome = farBoot.localPosition;
        Bounds mattress = bunkMattress.bounds;
        Vector3 standing = transform.TransformPoint(rest + wakeHips);
        Vector3 nearFoot = transform.TransformPoint(rest + wakeNearHome);
        Vector3 farFoot = transform.TransformPoint(rest + wakeFarHome);
        Vector3 parkedFarFoot = farFoot + Vector3.forward * 0.24f;
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
        yield return new WaitForSeconds(0.15f * wakeDurationScale);

        var rolled = new Vector3(standing.x, Mathf.Max(lying.y, mattress.max.y + 0.26f), mattress.min.z + 0.27f);
        Quaternion rolledRotation = Quaternion.Euler(0f, 20f, 72f);
        Vector3 nearTucked = wakeNearHome + new Vector3(0.24f, 0.04f, 0f);
        Vector3 farTucked = wakeFarHome + new Vector3(0.06f, 0.04f, 0f);
        yield return MoveWake(0.42f, t => PoseWakeLocal(
            Vector3.Lerp(lying, rolled, t), Quaternion.Slerp(lyingRotation, rolledRotation, t),
            tuck * (1f - t), Mathf.Lerp(-3f, -16f, t), Mathf.Lerp(18f, 40f, t),
            Vector3.Lerp(wakeNearHome, nearTucked, t), Vector3.Lerp(wakeFarHome, farTucked, t)));

        Vector3 nearRolled = nearBoot.position;
        Vector3 farRolled = farBoot.position;
        Quaternion seatedRotation = Quaternion.Euler(0f, 82f, 0f);
        var seated = new Vector3(standing.x, mattress.max.y + 0.17f, mattress.min.z + 0.025f);
        var nearDangling = new Vector3(seated.x - 0.12f, nearFoot.y + 0.25f, mattress.min.z - 0.39f);
        var farDangling = new Vector3(seated.x + 0.12f, farFoot.y + 0.27f, mattress.min.z - 0.30f);
        yield return MoveWake(0.65f, t =>
        {
            Quaternion rotation = Quaternion.Slerp(rolledRotation, seatedRotation, t);
            PoseWakeWorld(Vector3.Lerp(rolled, seated, t), rotation, Mathf.Lerp(-16f, -10f, t),
                Mathf.Lerp(40f, -24f, t),
                ClearWakeBed(nearRolled, nearDangling, mattress, t),
                ClearWakeBed(farRolled, farDangling, mattress, t),
                rotation, rotation);
        });

        Quaternion plantedRotation = Quaternion.Euler(0f, 59f, 0f);
        yield return MoveWake(0.59f, t =>
        {
            float farProgress = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.78f, t));
            Quaternion rotation = Quaternion.Slerp(seatedRotation, plantedRotation, t);
            SetWakeCapsule(capsule, capsuleHeight, capsuleCenter, t * 0.28f);
            PoseWakeWorld(Vector3.Lerp(seated, weightTransfer, t), rotation, Mathf.Lerp(-10f, -31f, t),
                Mathf.Lerp(-24f, 28f, t),
                WakeFootArc(nearDangling, nearFoot, t, 0.045f),
                WakeFootArc(farDangling, parkedFarFoot, farProgress, 0.034f),
                Quaternion.Slerp(seatedRotation, Quaternion.identity, t),
                Quaternion.Slerp(seatedRotation, plantedRotation, farProgress));
        });
        footsteps.PlayOneShot(boot, 0.24f);

        Quaternion midTurn = Quaternion.Euler(0f, 28f, 0f);
        yield return MoveWake(0.34f, t =>
        {
            SetWakeCapsule(capsule, capsuleHeight, capsuleCenter, Mathf.Lerp(0.28f, 0.67f, t));
            PoseWakeWorld(Vector3.Lerp(weightTransfer, halfRise, t),
                Quaternion.Slerp(plantedRotation, midTurn, t), Mathf.Lerp(-31f, -14f, t), Mathf.Lerp(28f, 15f, t),
                nearFoot, parkedFarFoot, Quaternion.identity, plantedRotation);
        });

        yield return MoveWake(0.38f, t =>
        {
            // The near foot bears the weight while the trailing foot steps onto the movement lane.
            float step = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.82f, t));
            Vector3 liveStanding = transform.TransformPoint(rest + wakeHips);
            Vector3 liveFarFoot = transform.TransformPoint(rest + wakeFarHome);
            float pitch = Mathf.Lerp(-14f, 0f, t);
            if (library != null)
            {
                var sample = library.Sample(CellAnimationLibrary.Motion.SitStand, t);
                pitch = Mathf.Lerp(pitch, Mathf.Clamp(sample.TorsoPitch, -24f, 3f), 0.35f * Mathf.Sin(t * Mathf.PI));
            }

            SetWakeCapsule(capsule, capsuleHeight, capsuleCenter, Mathf.Lerp(0.67f, 1f, t));
            PoseWakeWorld(Vector3.Lerp(halfRise, liveStanding, t), Quaternion.Slerp(midTurn, Quaternion.identity, t),
                pitch, Mathf.Lerp(15f, 0f, t), nearFoot, WakeFootArc(parkedFarFoot, liveFarFoot, step, 0.075f),
                Quaternion.identity, Quaternion.Slerp(plantedRotation, Quaternion.identity, step));
        });

        WakingUp = false;
        EndAction();
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
        Vector3 nearTarget, Vector3 farTarget, Quaternion nearRotation, Quaternion farRotation)
    {
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

    private static Vector3 ClearWakeBed(Vector3 from, Vector3 to, Bounds mattress, float t)
    {
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
