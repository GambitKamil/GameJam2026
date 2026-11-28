using UnityEngine;

public sealed partial class CellMotion
{
    [SerializeField, Range(0.08f, 0.22f)] private float aimBlendSeconds = 0.16f;
    [SerializeField, Min(720f)] private float aimTurnDegreesPerSecond = 1440f;

    private bool aimRequested;
    private float requestedAimAngle;
    private float aimAngle;
    private float aimBlend;

    public void SetAim(Vector3 direction, bool active)
    {
        direction.z = 0f;
        aimRequested = active && direction.sqrMagnitude > 0.000001f;
        if (!aimRequested)
            return;

        requestedAimAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        if (aimBlend <= 0f)
            aimAngle = requestedAimAngle;
    }

    private void ApplyAimPose()
    {
        bool aiming = aimRequested && !Busy;
        float delta = Time.deltaTime;
        aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, delta / aimBlendSeconds);
        if (aimBlend <= 0f)
        {
            AlignHand();
            return;
        }

        // An angular rate picks the same short arc across the -180/+180 seam.
        aimAngle = Mathf.MoveTowardsAngle(aimAngle, requestedAimAngle, aimTurnDegreesPerSecond * delta);
        float radians = aimAngle * Mathf.Deg2Rad;
        var direction = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
        float weight = Mathf.SmoothStep(0f, 1f, aimBlend);
        float relativeSide = direction.x * facing;
        float lookingBack = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.85f, -relativeSide));
        // Turning toward the camera keeps the stance and the near shoulder coherent across aim reversals.
        torso.localRotation *= Quaternion.Euler(0f, 29f * lookingBack * weight, -8f * relativeSide * weight);
        neck.localRotation *= Quaternion.Euler(0f, 50f * lookingBack * weight, 18f * direction.y * weight);
        AlignHand(direction, weight);
    }

    private float ThrowArmSide(Vector3 direction)
    {
        return Mathf.Abs(direction.x) > 0.05f ? Mathf.Sign(direction.x) * facing : 1f;
    }
}
