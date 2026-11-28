using UnityEngine;

/// <summary>Fits the humanoid skin to the Cell controller's authored pose and contacts.</summary>
public sealed class CellPeasantVisual : MonoBehaviour
{
    [SerializeField] private Animator avatar;
    [SerializeField, Range(0.8f, 1f)] private float scale = 0.94f;

    private Transform referenceFigure, referenceTorso, referenceNeck, referenceElbow, referenceGrip;
    private Transform nearShoe, farShoe;
    private float nearSole, farSole;
    private Transform pelvis, chest, head;
    private Quaternion pelvisRest, chestRest, headRest;
    private Limb rightLeg, leftLeg, rightArm, leftArm;
    private Transform[] fingers;
    private Quaternion[] openFingers, closedFingers;
    private float gripBlend;

    internal Vector3 Grip { get; private set; }
    internal Vector3 HeadPosition => head.position;

    internal void Bind(Transform figure, Transform torso, Transform neck, Transform elbow, Transform grip,
        Transform nearBoot, Transform farBoot, float nearSoleOffset, float farSoleOffset)
    {
        referenceFigure = figure;
        referenceTorso = torso;
        referenceNeck = neck;
        referenceElbow = elbow;
        referenceGrip = grip;
        nearShoe = nearBoot;
        farShoe = farBoot;
        nearSole = nearSoleOffset;
        farSole = farSoleOffset;

        transform.localScale = Vector3.one * scale;
        pelvis = avatar.GetBoneTransform(HumanBodyBones.Hips);
        chest = avatar.GetBoneTransform(HumanBodyBones.UpperChest);
        head = avatar.GetBoneTransform(HumanBodyBones.Head);
        pelvisRest = LocalRotation(pelvis);
        chestRest = LocalRotation(chest);
        headRest = LocalRotation(head);
        rightLeg = MakeLimb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, Vector3.right);
        leftLeg = MakeLimb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, Vector3.right);
        rightArm = MakeLimb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, Vector3.down);
        leftArm = MakeLimb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, Vector3.up);
        BindFingers();
        Grip = referenceGrip.position;

        // The old geometry still supplies the existing bounds probes for boot, bunk and grille.
        foreach (Renderer part in referenceFigure.GetComponentsInChildren<Renderer>(true))
            part.forceRenderingOff = true;
    }

    internal void Apply(float stride, bool gripping, float seated = 0f)
    {
        bool facingRight = referenceFigure.localScale.x > 0f;
        float side = facingRight ? 1f : -1f;
        Quaternion yaw = Quaternion.Euler(0f, facingRight ? 102f : 258f, 0f);
        Quaternion stance = referenceFigure.rotation * yaw;
        transform.SetPositionAndRotation(referenceFigure.position, stance);

        Limb nearLeg = facingRight ? rightLeg : leftLeg;
        Limb farLeg = facingRight ? leftLeg : rightLeg;
        Quaternion nearHeading = nearShoe.rotation * yaw;
        Quaternion farHeading = farShoe.rotation * yaw;
        Vector3 nearAnkle = Ankle(nearShoe, nearSole, nearLeg, nearHeading);
        Vector3 farAnkle = Ankle(farShoe, farSole, farLeg, farHeading);
        Vector3 up = stance * Vector3.up;
        Vector3 forward = stance * Vector3.forward;

        pelvis.SetPositionAndRotation(referenceTorso.position + up * Mathf.Lerp(0.116f, 0.02f, seated), stance * pelvisRest);
        float hipDrop = Mathf.Max(nearLeg.DropForReach(nearAnkle, up), farLeg.DropForReach(farAnkle, up));
        pelvis.position -= up * hipDrop;
        nearLeg.Solve(nearAnkle, forward);
        farLeg.Solve(farAnkle, forward);
        nearLeg.End.rotation = nearHeading * nearLeg.RestEndRotation;
        farLeg.End.rotation = farHeading * farLeg.RestEndRotation;

        Quaternion torso = referenceTorso.localRotation;
        if (!facingRight)
            torso = new Quaternion(torso.x, -torso.y, -torso.z, torso.w);
        Quaternion upperBody = referenceFigure.rotation * torso * yaw;
        chest.rotation = upperBody * chestRest;
        Quaternion neck = referenceNeck.localRotation;
        if (!facingRight)
            neck = new Quaternion(neck.x, -neck.y, -neck.z, neck.w);
        head.rotation = referenceFigure.rotation * torso * neck * yaw * headRest;

        Limb nearArm = facingRight ? rightArm : leftArm;
        Limb farArm = facingRight ? leftArm : rightArm;
        Vector3 towardCamera = referenceFigure.rotation * Vector3.back;
        Vector3 nearDirection = (referenceGrip.position - nearArm.Start.position).normalized;
        const float palmLength = 0.068f;
        Vector3 wrist = referenceGrip.position - nearDirection * palmLength;
        Vector3 nearPole = referenceElbow.position - nearArm.Start.position + towardCamera * 0.16f;
        nearArm.Solve(wrist, nearPole, upperBody);
        // Rotate with the forearm, with the palm centered on the same reach as the bread and rod.
        nearArm.AimHand(referenceGrip.position - nearArm.End.position, towardCamera);
        Grip = nearArm.End.position + nearArm.End.up * palmLength;

        Vector3 farWrist = farArm.Start.position - up * 0.43f - towardCamera * 0.05f - forward * (0.025f + stride * 0.14f);
        farArm.Solve(farWrist, -forward - towardCamera * 0.35f, upperBody);
        farArm.AimHand(-up + forward * 0.08f, -towardCamera);

        gripBlend = Mathf.MoveTowards(gripBlend, gripping ? 1f : 0.35f, Time.deltaTime * 10f);
        for (int i = 0; i < fingers.Length; i++)
        {
            bool isNear = (i < fingers.Length / 2) == facingRight;
            fingers[i].localRotation = Quaternion.Slerp(openFingers[i], closedFingers[i], isNear ? gripBlend : 0.30f);
        }
    }

    private Vector3 Ankle(Transform shoe, float sole, Limb leg, Quaternion heading)
        => shoe.position - shoe.up * sole + heading * (Vector3.up * leg.SoleHeight - Vector3.forward * 0.02f);

    private Quaternion LocalRotation(Transform bone) => Quaternion.Inverse(transform.rotation) * bone.rotation;

    private Limb MakeLimb(HumanBodyBones first, HumanBodyBones middle, HumanBodyBones last, Vector3 fallbackNormal)
        => new Limb(transform, avatar.GetBoneTransform(first), avatar.GetBoneTransform(middle),
            avatar.GetBoneTransform(last), fallbackNormal);

    private void BindFingers()
    {
        fingers = new Transform[24];
        openFingers = new Quaternion[fingers.Length];
        closedFingers = new Quaternion[fingers.Length];
        CacheFingers(HumanBodyBones.RightIndexProximal, 0);
        CacheFingers(HumanBodyBones.LeftIndexProximal, fingers.Length / 2);

        using var handler = new HumanPoseHandler(avatar.avatar, avatar.transform);
        var open = new HumanPose();
        handler.GetHumanPose(ref open);
        var bent = new HumanPose { bodyPosition = open.bodyPosition, bodyRotation = open.bodyRotation,
            muscles = (float[])open.muscles.Clone() };
        for (int i = 0; i < bent.muscles.Length; i++)
        {
            string muscle = HumanTrait.MuscleName[i];
            if (muscle.Contains("Stretched") && !muscle.Contains("Thumb"))
                bent.muscles[i] = -0.44f;
        }
        handler.SetHumanPose(ref bent);
        for (int i = 0; i < fingers.Length; i++)
            closedFingers[i] = fingers[i].localRotation;
        handler.SetHumanPose(ref open);

        void CacheFingers(HumanBodyBones index, int offset)
        {
            for (int i = 0; i < fingers.Length / 2; i++)
            {
                fingers[offset + i] = avatar.GetBoneTransform(index + i);
                openFingers[offset + i] = fingers[offset + i].localRotation;
            }
        }
    }

    private sealed class Limb
    {
        public Transform Start { get; }
        public Transform End { get; }
        public Quaternion RestEndRotation { get; }
        public float SoleHeight { get; }

        private readonly Transform middle;
        private readonly float firstLength, secondLength;
        private readonly Quaternion firstRest, secondRest, upperBind;
        private readonly Vector3 upperBindDirection;
        private readonly Vector3 restNormal;
        private readonly Transform space;

        internal Limb(Transform space, Transform start, Transform middle, Transform end, Vector3 fallbackNormal)
        {
            this.space = space;
            Start = start;
            this.middle = middle;
            End = end;
            Vector3 first = space.InverseTransformVector(middle.position - start.position);
            Vector3 second = space.InverseTransformVector(end.position - middle.position);
            firstLength = Vector3.Distance(start.position, middle.position);
            secondLength = Vector3.Distance(middle.position, end.position);
            Vector3 normal = Vector3.Cross(first, second);
            restNormal = normal.sqrMagnitude > 0.000001f ? normal.normalized : fallbackNormal;
            Quaternion inverse = Quaternion.Inverse(space.rotation);
            upperBind = inverse * start.rotation;
            upperBindDirection = first.normalized;
            firstRest = Quaternion.Inverse(Quaternion.LookRotation(first, restNormal)) * upperBind;
            secondRest = Quaternion.Inverse(Quaternion.LookRotation(second, restNormal)) * inverse * middle.rotation;
            RestEndRotation = inverse * end.rotation;
            SoleHeight = space.InverseTransformPoint(end.position).y * space.lossyScale.y + 0.006f;
        }

        internal float DropForReach(Vector3 target, Vector3 up)
        {
            Vector3 offset = Start.position - target;
            float horizontal = Vector3.ProjectOnPlane(offset, up).sqrMagnitude;
            float reach = firstLength + secondLength - 0.012f;
            float vertical = Mathf.Sqrt(Mathf.Max(0.04f, reach * reach - horizontal));
            return Mathf.Max(0f, Vector3.Dot(offset, up) - vertical);
        }

        internal void Solve(Vector3 goal, Vector3 pole, Quaternion? chestFrame = null)
        {
            Vector3 root = Start.position;
            Vector3 offset = goal - root;
            Vector3 axis = offset.sqrMagnitude > 0.000001f ? offset.normalized : -space.up;
            float distance = Mathf.Clamp(offset.magnitude, Mathf.Abs(firstLength - secondLength) + 0.03f,
                firstLength + secondLength - 0.003f);
            Vector3 bend = Vector3.ProjectOnPlane(pole, axis);
            if (bend.sqrMagnitude < 0.000001f)
                bend = Vector3.Cross(axis, space.TransformDirection(restNormal));
            float projection = (firstLength * firstLength - secondLength * secondLength + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, firstLength * firstLength - projection * projection));
            Vector3 first = axis * projection + bend.normalized * height;
            Vector3 second = axis * distance - first;
            Vector3 normal = Vector3.Cross(first, second);
            Quaternion solvedUpper = Quaternion.LookRotation(first, normal) * firstRest;
            if (chestFrame.HasValue)
            {
                // Keep most of the shirt's authored shoulder twist while the elbow traces the reach.
                Quaternion chest = chestFrame.Value;
                Quaternion relaxedUpper = Quaternion.FromToRotation(chest * upperBindDirection, first) * chest * upperBind;
                solvedUpper = Quaternion.Slerp(relaxedUpper, solvedUpper, 0.36f);
            }
            Start.rotation = solvedUpper;
            middle.rotation = Quaternion.LookRotation(second, normal) * secondRest;
        }

        internal void AimHand(Vector3 fingerDirection, Vector3 palm)
        {
            // The Quaternius hand bones point toward the fingers along local +Y.
            if (fingerDirection.sqrMagnitude < 0.0001f)
                fingerDirection = End.position - middle.position;
            Vector3 across = Vector3.ProjectOnPlane(palm, fingerDirection);
            if (across.sqrMagnitude < 0.0001f)
                across = Vector3.ProjectOnPlane(space.forward, fingerDirection);
            if (across.sqrMagnitude < 0.0001f)
                across = Vector3.ProjectOnPlane(space.right, fingerDirection);
            End.rotation = Quaternion.LookRotation(across, fingerDirection);
        }
    }
}
