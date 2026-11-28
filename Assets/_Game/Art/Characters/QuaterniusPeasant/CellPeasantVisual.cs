using System.Collections.Generic;
using UnityEngine;

/// <summary>Fits the humanoid skin to the Cell controller's authored pose and contacts.</summary>
public sealed class CellPeasantVisual : MonoBehaviour
{
    [SerializeField] private Animator avatar;
    private const float BaseScale = 0.94f;

    [SerializeField, Range(0.8f, 2f)] private float scale = BaseScale;

    private Transform referenceFigure, referenceTorso, referenceNeck, referenceElbow, referenceGrip;
    private Transform nearShoe, farShoe;
    private float nearSole, farSole;
    private Transform pelvis, chest, head;
    private Quaternion pelvisRest, chestRest, headRest;
    private Limb rightLeg, leftLeg, rightArm, leftArm;
    private Transform[] fingers;
    private Quaternion[] openFingers, closedFingers;
    private float gripBlend;
    private Renderer[] referenceRenderers;
    private SkinnedMeshRenderer[] bedContactParts;
    private Mesh bedSample;
    private float bedOffset;
    private readonly List<Vector3> bedVertices = new List<Vector3>();

    private float PoseScale => scale / BaseScale;
    internal float FootprintScale => PoseScale;
    internal float AimHeight => 0.48f + (PoseScale - 1f) * 1.29f;

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
        referenceRenderers = referenceFigure.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer part in referenceRenderers)
            part.forceRenderingOff = true;
        bedContactParts = System.Array.FindAll(avatar.GetComponentsInChildren<SkinnedMeshRenderer>(),
            part => part.name == "Male_Peasant_Body" || part.name == "Male_Peasant_Legs");
        bedSample = new Mesh { name = "Prisoner mattress contact" };
    }

    internal void Apply(float armSwing, bool gripping, float seated = 0f, float bedPose = 0f,
        Vector3 requestedGrip = default, float reachWeight = 0f,
        Vector3? nearContact = null, Vector3? farContact = null, Bounds? bedSupport = null, float resting = 0f,
        float? maxGripX = null, bool snapBedSupport = false, float actionWeight = 0f)
    {
        bool facingRight = referenceFigure.localScale.x > 0f;
        float nearHandSide = facingRight ? 1f : -1f;
        Quaternion yaw = Quaternion.Euler(0f, facingRight ? 102f : 258f, 0f);
        Quaternion stance = referenceFigure.rotation * yaw;
        transform.SetPositionAndRotation(referenceFigure.position, stance);

        Limb nearLeg = facingRight ? rightLeg : leftLeg;
        Limb farLeg = facingRight ? leftLeg : rightLeg;
        Quaternion nearHeading = nearShoe.rotation * yaw;
        Quaternion farHeading = farShoe.rotation * yaw;
        Vector3 up = stance * Vector3.up;
        Vector3 forward = stance * Vector3.forward;
        float size = PoseScale;
        float lying = (1f - Mathf.Abs(Vector3.Dot(up, Vector3.up))) * bedPose;
        Vector3 nearAnkle = Ankle(nearShoe, nearContact, nearSole, nearLeg, nearHeading, lying, seated);
        Vector3 farAnkle = Ankle(farShoe, farContact, farSole, farLeg, farHeading, lying, seated);
        if (bedSupport.HasValue && resting > 0f)
        {
            float restingAnkle = bedSupport.Value.max.y + 0.126f * size;
            nearAnkle.y = Mathf.Lerp(nearAnkle.y, restingAnkle, resting);
            farAnkle.y = Mathf.Lerp(farAnkle.y, restingAnkle + 0.025f, resting);
        }

        Vector3 basePelvis = referenceTorso.position + up * Mathf.Lerp(0.116f, 0.02f, seated);
        float freeHeight = (1f - lying) * (1f - seated);
        float scaledHeight = referenceFigure.InverseTransformPoint(basePelvis).y * (size - 1f) * freeHeight;
        pelvis.SetPositionAndRotation(basePelvis + up * scaledHeight, stance * pelvisRest);

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
        float palmLength = 0.068f * size;
        float hipDrop = Mathf.Max(nearLeg.DropForReach(nearAnkle, up), farLeg.DropForReach(farAnkle, up));
        if (reachWeight > 0f && requestedGrip.y < 1.0f)
        {
            // Bring the shoulder down before a low pickup so the visible arm can reach the item.
            float reachDrop = nearArm.DropForReach(requestedGrip + up * palmLength, up);
            float support = Mathf.Max(Vector3.Dot(nearAnkle, up) - nearLeg.SoleHeight,
                Vector3.Dot(farAnkle, up) - farLeg.SoleHeight);
            float availableDrop = Mathf.Max(0f, Vector3.Dot(pelvis.position, up) - support - 0.33f * size);
            hipDrop = Mathf.Max(hipDrop, Mathf.Min(reachDrop * reachWeight, availableDrop));
        }
        pelvis.position -= up * hipDrop;

        nearLeg.Solve(nearAnkle, forward);
        farLeg.Solve(farAnkle, forward);
        nearLeg.End.rotation = nearHeading * nearLeg.RestEndRotation;
        farLeg.End.rotation = farHeading * farLeg.RestEndRotation;

        if (bedSupport is Bounds mattress)
            ApplyBedSupport(mattress, bedPose, snapBedSupport);

        gripBlend = Mathf.MoveTowards(gripBlend, gripping ? 1f : 0f, Time.deltaTime * 10f);
        float nearAction = Mathf.Max(Mathf.Max(actionWeight, reachWeight), Mathf.Max(gripBlend, bedPose));
        Vector3 armForward = upperBody * Vector3.forward;
        Vector3 nearOutward = upperBody * Vector3.right * nearHandSide;
        Vector3 armUp = Vector3.Slerp(up, upperBody * Vector3.up, 0.3f);
        Vector3 grip = ScaleReach(referenceGrip.position, size);
        grip = Vector3.Lerp(grip, requestedGrip, reachWeight);
        if (maxGripX.HasValue)
            grip.x = Mathf.Min(grip.x, maxGripX.Value);
        grip = SupportWakeHand(grip, nearArm.Start.position, bedSupport, seated);
        Vector3 nearDirection = (grip - nearArm.Start.position).normalized;
        Vector3 wrist = grip - nearDirection * palmLength;
        float liftElbow = (0.28f + 2.55f * seated * (1f - seated)) * bedPose;
        Vector3 nearPole = ScaleReach(referenceElbow.position, size) - nearArm.Start.position
            + towardCamera * (0.16f * size) + Vector3.up * liftElbow;
        Vector3 neutralNearPole = -armForward + nearOutward * 0.22f;
        wrist = Vector3.Lerp(RelaxedWrist(nearArm, armUp, armForward, nearOutward, armSwing), wrist, nearAction);
        nearArm.Solve(wrist, Vector3.Lerp(neutralNearPole, nearPole, nearAction), upperBody);
        float brace = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.50f, seated)) * bedPose;
        nearArm.PoseHand(grip - nearArm.End.position, -armForward - nearOutward * 0.74f,
            towardCamera, nearHandSide, nearAction, brace);
        Grip = nearArm.End.position + nearArm.End.up * palmLength;

        Vector3 farOutward = -nearOutward;
        Vector3 farWrist = RelaxedWrist(farArm, armUp, armForward, farOutward, -armSwing);
        Vector3 bedWrist = farArm.Start.position - (up * 0.43f + towardCamera * 0.05f + forward * 0.025f) * size;
        bedWrist = SupportWakeHand(bedWrist, farArm.Start.position, bedSupport, seated);
        Vector3 farPole = Vector3.Lerp(-armForward + farOutward * 0.22f,
            -forward - towardCamera * 0.35f + Vector3.up * liftElbow, bedPose);
        farArm.Solve(Vector3.Lerp(farWrist, bedWrist, bedPose), farPole, upperBody);
        farArm.PoseHand(-up + forward * 0.08f, -armForward - farOutward * 0.74f,
            -towardCamera, -nearHandSide, bedPose, brace);

        for (int i = 0; i < fingers.Length; i++)
        {
            bool isNear = (i < fingers.Length / 2) == facingRight;
            float curl = isNear ? Mathf.Lerp(0.24f, 1f, gripBlend) : 0.24f;
            fingers[i].localRotation = Quaternion.Slerp(openFingers[i], closedFingers[i], curl * (1f - 0.45f * brace));
        }
    }

    private static Vector3 RelaxedWrist(Limb arm, Vector3 up, Vector3 forward, Vector3 outward, float swing)
    {
        Vector3 drop = -up + outward * 0.055f + forward * (0.035f + 0.19f * swing);
        // A near-straight reach leaves a small, natural elbow bend at rest and through the arm swing.
        return arm.Start.position + drop.normalized * (arm.Length * 0.98f);
    }

    private void ApplyBedSupport(Bounds mattress, float weight, bool snap)
    {
        if (TryGetBedSupport(mattress, out float surface))
        {
            const float clothClearance = 0.004f;
            float correction = mattress.max.y + clothClearance - surface;
            if (Mathf.Abs(correction) <= 0.8f)
                bedOffset = snap || correction > bedOffset
                    ? correction
                    : Mathf.MoveTowards(bedOffset, correction, 3.8f * Time.deltaTime);
        }

        // Solve hands in world space after settling the torso so the support does not sink them into the frame.
        transform.position += Vector3.up * (bedOffset * weight);
    }

    private bool TryGetBedSupport(Bounds mattress, out float lowestY)
    {
        lowestY = float.PositiveInfinity;
        foreach (SkinnedMeshRenderer part in bedContactParts)
        {
            // BakeMesh already includes the imported FBX scale in this overload.
            part.BakeMesh(bedSample, false);
            bedSample.GetVertices(bedVertices);
            Matrix4x4 world = Matrix4x4.TRS(part.transform.position, part.transform.rotation, Vector3.one);
            foreach (Vector3 vertex in bedVertices)
            {
                Vector3 point = world.MultiplyPoint3x4(vertex);
                if (point.x >= mattress.min.x && point.x <= mattress.max.x &&
                    point.z >= mattress.min.z && point.z <= mattress.max.z)
                    lowestY = Mathf.Min(lowestY, point.y);
            }
        }
        return float.IsFinite(lowestY);
    }

    private void OnDestroy()
    {
        if (bedSample != null)
            Destroy(bedSample);
        if (referenceRenderers == null)
            return;
        foreach (Renderer part in referenceRenderers)
            if (part != null)
                part.forceRenderingOff = false;
    }

    private Vector3 ScaleReach(Vector3 point, float size)
    {
        Vector3 offset = referenceFigure.InverseTransformVector(point - referenceTorso.position);
        return pelvis.position - referenceFigure.up * (0.116f * size) + referenceFigure.TransformVector(offset * size);
    }

    private static Vector3 SupportWakeHand(Vector3 point, Vector3 shoulder, Bounds? mattress, float seated)
    {
        if (!(mattress is Bounds bed))
            return point;

        // A palm above the rim can brace the body during the swing of the legs.
        float brace = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.50f, seated));
        Vector3 support = new Vector3(Mathf.Clamp(shoulder.x, bed.min.x + 0.10f, bed.max.x - 0.10f),
            bed.max.y + 0.14f, bed.min.z + 0.18f);
        point = Vector3.Lerp(point, support, brace);
        if (point.x >= bed.min.x - 0.06f && point.x <= bed.max.x + 0.06f &&
            point.z >= bed.min.z - 0.20f && point.z <= bed.max.z + 0.06f)
            point.y = Mathf.Max(point.y, bed.max.y + 0.15f);
        return point;
    }

    private Vector3 Ankle(Transform shoe, Vector3? contactTarget, float sole, Limb leg, Quaternion heading,
        float lying, float seated)
    {
        Vector3 contact = (contactTarget ?? shoe.position) - shoe.up * sole;
        Vector3 local = referenceFigure.InverseTransformVector(contact - referenceTorso.position);
        Vector3 laidOut = referenceTorso.position + referenceFigure.TransformVector(local * PoseScale);
        Vector3 lateral = referenceFigure.TransformVector(new Vector3(0f, 0f, local.z * (PoseScale - 1f)));
        Vector3 grounded = contact + lateral * (1f - seated);
        return Vector3.Lerp(grounded, laidOut, lying)
            + heading * (Vector3.up * leg.SoleHeight - Vector3.forward * (0.02f * PoseScale));
    }

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
        public float Length => firstLength + secondLength;

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
            SoleHeight = (space.InverseTransformPoint(end.position).y + 0.011f) * space.lossyScale.y;
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

        internal void PoseHand(Vector3 actionFingers, Vector3 relaxedPalm, Vector3 thumbDirection,
            float palmSide, float actionWeight, float brace)
        {
            Vector3 forearm = (End.position - middle.position).normalized;
            const float wristBendLimit = 55f * Mathf.Deg2Rad;
            Vector3 direction = actionFingers.sqrMagnitude > 0.0001f ? actionFingers.normalized : forearm;
            Vector3 fingers = Vector3.RotateTowards(forearm, direction, wristBendLimit * actionWeight, 0f);

            // On this model +Y points toward the fingers, +Z toward the thumb,
            // and the mirrored palms face +X on the right hand and -X on the left.
            Vector3 actionPalm = Vector3.Cross(fingers, thumbDirection) * palmSide;
            Quaternion relaxed = HandFrame(fingers, relaxedPalm, palmSide);
            Quaternion action = HandFrame(fingers, actionPalm, palmSide);
            Quaternion handPose = Quaternion.Slerp(relaxed, action, actionWeight);
            Vector3 palmDown = Vector3.ProjectOnPlane(Vector3.down, fingers);
            if (brace > 0f && palmDown.sqrMagnitude > 0.0001f)
                handPose = Quaternion.Slerp(handPose, HandFrame(fingers, palmDown, palmSide), brace * palmDown.magnitude);

            // Pronation turns the forearm with the hand. Rotate around the actual bone axis so
            // the grip and elbow solved above remain in place.
            Vector3 forearmPalm = Vector3.ProjectOnPlane(middle.right, forearm);
            Vector3 handPalm = Vector3.ProjectOnPlane(handPose * Vector3.right, forearm);
            if (handPalm.sqrMagnitude > 0.0001f)
                middle.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(forearmPalm, handPalm, forearm), forearm)
                    * middle.rotation;
            End.rotation = handPose;
        }

        private Quaternion HandFrame(Vector3 fingers, Vector3 palm, float palmSide)
        {
            Vector3 across = Vector3.Cross(palm * palmSide, fingers);
            if (across.sqrMagnitude < 0.0001f)
                across = Vector3.ProjectOnPlane(space.forward, fingers);
            if (across.sqrMagnitude < 0.0001f)
                across = Vector3.ProjectOnPlane(space.right, fingers);
            return Quaternion.LookRotation(across, fingers);
        }
    }
}
