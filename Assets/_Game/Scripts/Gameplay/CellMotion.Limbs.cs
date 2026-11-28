using UnityEngine;

public sealed partial class CellMotion
{
    [SerializeField, Range(0.15f, 0.45f)] private float minimumArmExtension = 0.27f;

    [SerializeField, Range(0f, 30f)] private float maximumHandLean = 24f;

    private static readonly Vector3 ElbowPole = new Vector3(0f, -0.35f, -1f);
    private Transform upperArm;
    private Transform forearm;
    private Mesh sleeveSkin;
    private Vector3 armRestElbow;
    private Vector3 armRestWrist;
    private float armSegmentLength;
    private Quaternion handLean = Quaternion.identity;
    private BoxCollider gateBlocker;
    private Renderer[] gateBodyParts;
    private Bounds[] gateVisualBounds;
    private float gateFront;

    private float ArmMaximumReach => armSegmentLength * 2f;

    private void InitializeArmPose()
    {
        Vector3 wrist = hand.localPosition;
        // A slightly bent elbow preserves the authored relaxed hand position.
        armSegmentLength = wrist.magnitude * 0.535f;
        armRestElbow = SolveLimb(Vector3.zero, wrist, armSegmentLength, minimumArmExtension,
            ElbowPole, out armRestWrist);
        upperArm = new GameObject("Upper Arm Joint").transform;
        upperArm.SetParent(shoulder, false);
        forearm = new GameObject("Elbow Joint").transform;
        forearm.SetParent(shoulder, false);
        forearm.localPosition = armRestElbow;

        var cuff = sleeve.Find("Cuff");
        if (cuff != null)
            cuff.SetParent(forearm, true);
        hand.SetParent(forearm, true);
        BindSleeve();
    }

    private void BindSleeve()
    {
        Mesh source = sleeve.GetComponent<MeshFilter>().sharedMesh;
        if (source == null || !source.isReadable)
        {
            sleeve.SetParent(upperArm, true);
            return;
        }

        // Skin a runtime copy so the authored silhouette, UVs, and shared material stay intact.
        var original = sleeve.GetComponent<MeshRenderer>();
        var skin = new GameObject("Articulated Sleeve").AddComponent<SkinnedMeshRenderer>();
        skin.transform.SetParent(sleeve, false);
        sleeveSkin = Instantiate(source);
        sleeveSkin.name = "Articulated Sleeve Mesh";
        sleeveSkin.bindposes = new[]
        {
            upperArm.worldToLocalMatrix * sleeve.localToWorldMatrix,
            forearm.worldToLocalMatrix * sleeve.localToWorldMatrix
        };

        Vector3[] vertices = source.vertices;
        var weights = new BoneWeight[vertices.Length];
        Matrix4x4 meshToShoulder = shoulder.worldToLocalMatrix * sleeve.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i++)
        {
            float alongArm = Vector3.Dot(meshToShoulder.MultiplyPoint3x4(vertices[i]), armRestWrist)
                / armRestWrist.sqrMagnitude;
            float lowerWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, alongArm));
            weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f - lowerWeight, boneIndex1 = 1, weight1 = lowerWeight };
        }

        sleeveSkin.boneWeights = weights;
        skin.sharedMesh = sleeveSkin;
        skin.rootBone = upperArm;
        skin.bones = new[] { upperArm, forearm };
        skin.sharedMaterials = original.sharedMaterials;
        skin.shadowCastingMode = original.shadowCastingMode;
        skin.receiveShadows = original.receiveShadows;
        skin.lightProbeUsage = original.lightProbeUsage;
        skin.reflectionProbeUsage = original.reflectionProbeUsage;
        skin.updateWhenOffscreen = true;
        original.enabled = false;
    }

    internal Vector3 GripWithinReach(Vector3 start, Vector3 end, Vector3 preferred)
    {
        Vector3 shaft = end - start;
        float length = shaft.magnitude;
        if (length < 0.001f)
            return start;

        Vector3 axis = shaft / length;
        float projection = Vector3.Dot(shoulder.position - start, axis);
        Vector3 nearest = start + axis * projection;
        float chord = Mathf.Sqrt(Mathf.Max(0f, ArmMaximumReach * ArmMaximumReach - (nearest - shoulder.position).sqrMagnitude));
        float minimum = Mathf.Max(0f, projection - chord);
        float maximum = Mathf.Min(length, projection + chord);
        float desired = Vector3.Dot(preferred - start, axis);
        float grip = minimum <= maximum ? Mathf.Clamp(desired, minimum, maximum) : Mathf.Clamp(projection, 0f, length);
        return start + axis * grip;
    }

    private void LeanTowardHandTarget()
    {
        Quaternion targetLean = Quaternion.identity;
        if (handTargetWeight > 0f)
        {
            Vector3 target = figure.InverseTransformPoint(handTarget) - torso.localPosition;
            Vector3 shoulderOffset = torso.localRotation * shoulder.localPosition;
            float distance = target.magnitude;
            float lever = shoulderOffset.magnitude;
            if (distance > 0.001f && lever > 0.001f && (target - shoulderOffset).sqrMagnitude > ArmMaximumReach * ArmMaximumReach)
            {
                float cosine = (distance * distance + lever * lever - ArmMaximumReach * ArmMaximumReach) / (2f * distance * lever);
                float reachableAngle = Mathf.Acos(Mathf.Clamp(cosine, -1f, 1f)) * Mathf.Rad2Deg;
                float restAngle = Vector3.Angle(shoulderOffset, target);
                float leanAngle = Mathf.Clamp(restAngle - reachableAngle, 0f, maximumHandLean) * handTargetWeight;
                targetLean = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(shoulderOffset, target), leanAngle);
            }
        }

        // Move the shoulder over the planted stance before solving the fixed-length reach.
        handLean = Quaternion.RotateTowards(handLean, targetLean, 200f * Time.deltaTime);
        torso.localRotation = handLean * torso.localRotation;
    }

    internal void SetGateBlocker(BoxCollider blocker)
    {
        gateBlocker = blocker;
        if (blocker == null)
            return;

        gateBodyParts = System.Array.FindAll(torso.GetComponentsInChildren<Renderer>(),
            part => !part.transform.IsChildOf(shoulder));

        Transform leaf = blocker.transform;
        Matrix4x4 worldToGate = leaf.worldToLocalMatrix;
        var visibleParts = System.Array.FindAll(leaf.GetComponentsInChildren<Renderer>(), part => part.enabled);
        gateVisualBounds = new Bounds[visibleParts.Length];
        gateFront = blocker.center.x - blocker.size.x * 0.5f;
        for (int i = 0; i < visibleParts.Length; i++)
        {
            Bounds part = BoundsInGateSpace(visibleParts[i], worldToGate);
            part.Expand(0.010f);
            gateVisualBounds[i] = part;
            gateFront = Mathf.Min(gateFront, part.min.x);
        }
    }

    private void LimitTorsoAtGate()
    {
        if (!Busy || gateBlocker == null || !gateBlocker.enabled)
            return;

        Transform leaf = gateBlocker.transform;
        Vector3 actor = leaf.InverseTransformPoint(transform.position);
        if (actor.x > gateFront || gateFront - actor.x > 0.9f
            || Mathf.Abs(actor.z - gateBlocker.center.z) > gateBlocker.size.z * 0.5f + 0.3f)
            return;

        Matrix4x4 worldToGate = leaf.worldToLocalMatrix;
        if (TorsoClearsGate(worldToGate))
            return;

        Quaternion requested = torso.localRotation;
        Quaternion safe = Quaternion.identity;
        torso.localRotation = safe;
        if (!TorsoClearsGate(worldToGate))
        {
            Vector3 away = figure.InverseTransformVector(-leaf.right);
            safe = Quaternion.AngleAxis(24f, Vector3.Cross(Vector3.up, away));
            torso.localRotation = safe;
            if (!TorsoClearsGate(worldToGate))
                return;
        }

        // Find a clear torso rotation without changing the planted feet or where the hand aims.
        float low = 0f;
        float high = 1f;
        for (int i = 0; i < 7; i++)
        {
            float t = (low + high) * 0.5f;
            torso.localRotation = Quaternion.Slerp(safe, requested, t);
            if (TorsoClearsGate(worldToGate))
                low = t;
            else
                high = t;
        }

        torso.localRotation = Quaternion.Slerp(safe, requested, low);
    }

    private bool TorsoClearsGate(Matrix4x4 worldToGate)
    {
        foreach (Renderer part in gateBodyParts)
        {
            if (!part.enabled)
                continue;

            Bounds bodyPart = BoundsInGateSpace(part, worldToGate);
            if (bodyPart.max.x < gateFront)
                continue;

            foreach (Bounds bar in gateVisualBounds)
                if (bar.Intersects(bodyPart))
                    return false;
        }

        return true;
    }

    private static Bounds BoundsInGateSpace(Renderer part, Matrix4x4 worldToGate)
    {
        Bounds bounds = part.localBounds;
        Matrix4x4 rendererToGate = worldToGate * part.localToWorldMatrix;
        Vector3 extents = Absolute(rendererToGate.MultiplyVector(Vector3.right * bounds.extents.x))
            + Absolute(rendererToGate.MultiplyVector(Vector3.up * bounds.extents.y))
            + Absolute(rendererToGate.MultiplyVector(Vector3.forward * bounds.extents.z));
        return new Bounds(rendererToGate.MultiplyPoint3x4(bounds.center), extents * 2f);

        static Vector3 Absolute(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private void AlignHand(Vector3? aimDirection = null, float aimWeight = 0f)
    {
        LimitTorsoAtGate();
        Vector3 requested = armRestWrist;
        if (handTargetWeight > 0f)
            requested = Vector3.Lerp(requested, shoulder.InverseTransformPoint(handTarget), handTargetWeight);
        if (aimWeight > 0f && aimDirection.HasValue)
        {
            Vector3 aim = shoulder.InverseTransformVector(aimDirection.Value).normalized;
            if (aim.sqrMagnitude > 0.5f)
            {
                Vector3 direction = AimWristDirection(requested.normalized, aimDirection.Value, aimWeight);
                requested = direction * Mathf.Lerp(requested.magnitude, ArmMaximumReach, aimWeight);
            }
        }

        Vector3 elbow = SolveLimb(Vector3.zero, requested, armSegmentLength, minimumArmExtension, ElbowPole, out Vector3 wrist);
        upperArm.localRotation = Quaternion.FromToRotation(armRestElbow, elbow);
        forearm.SetLocalPositionAndRotation(elbow, Quaternion.FromToRotation(armRestWrist - armRestElbow, wrist - elbow));
    }

    private void DestroyArmPose()
    {
        if (sleeveSkin != null)
            Destroy(sleeveSkin);
    }

    private static Vector3 SolveLimb(Vector3 root, Vector3 requested, float segmentLength,
        float minimumExtension, Vector3 pole, out Vector3 end)
    {
        Vector3 offset = requested - root;
        float distance = offset.magnitude;
        Vector3 axis = distance > 0.0001f ? offset / distance : Vector3.down;
        float reach = segmentLength * 2f;
        float extension = Mathf.Clamp(distance, reach * minimumExtension, reach);
        end = root + axis * extension;

        Vector3 bend = Vector3.ProjectOnPlane(pole, axis);
        if (bend.sqrMagnitude < 0.0001f)
            bend = Vector3.Cross(axis, Vector3.forward);
        float half = extension * 0.5f;
        float jointHeight = Mathf.Sqrt(Mathf.Max(0f, segmentLength * segmentLength - half * half));
        return root + axis * half + bend.normalized * jointHeight;
    }

    private sealed class LegPose
    {
        private readonly Transform thigh;
        private readonly Transform shin;
        private readonly Transform boot;
        private readonly float segmentLength;

        public Vector3 Hip { get; }
        public Vector3 BootHome { get; }
        public Vector3 AnkleOffset { get; } = new Vector3(0f, 0.075f, 0f);
        public float MaximumReach => segmentLength * 2f;

        public LegPose(Transform leg, Transform shoe)
        {
            thigh = leg;
            boot = shoe;
            BootHome = boot.localPosition;
            Hip = leg.localPosition + Vector3.up * 0.32f;
            segmentLength = Vector3.Distance(Hip, BootHome + AnkleOffset) * 0.52f;
            shin = Instantiate(leg, leg.parent, false);
            shin.name = leg.name + " Shin";
            Vector3 width = leg.localScale;
            const float meshOverlap = 0.055f;
            thigh.localScale = new Vector3(width.x, segmentLength + meshOverlap, width.z);
            shin.localScale = new Vector3(width.x * 0.87f, segmentLength + meshOverlap, width.z * 0.87f);
        }

        public void Pose(float phase, float gait, Vector3 hipOffset, float crouch,
            Vector3 sampledStride = default, float strideBlend = 0f)
        {
            float cycle = Mathf.Repeat(phase / (2f * Mathf.PI), 1f);
            float swing = Mathf.InverseLerp(0.56f, 1f, cycle);
            float travel = cycle <= 0.56f
                ? Mathf.Lerp(0.24f, -0.24f, cycle / 0.56f)
                : Mathf.SmoothStep(-0.24f, 0.24f, swing);
            float step = Mathf.Sin(swing * Mathf.PI) * 0.14f;
            Vector3 stride = new Vector3(travel, step, 0f) * gait * (1f - crouch * 0.75f);
            PoseFoot(hipOffset, BootHome + Vector3.Lerp(stride, sampledStride, strideBlend));
        }

        public void PoseFoot(Vector3 hipOffset, Vector3 footLocalPosition)
        {
            Vector3 hip = Hip + hipOffset;
            Vector3 knee = SolveLimb(hip, footLocalPosition + AnkleOffset, segmentLength, 0.14f, Vector3.right, out Vector3 ankle);
            PlaceSegment(thigh, hip, knee);
            PlaceSegment(shin, knee, ankle);
            boot.localPosition = ankle - AnkleOffset;
        }

        private static void PlaceSegment(Transform segment, Vector3 upper, Vector3 lower)
        {
            segment.SetLocalPositionAndRotation((upper + lower) * 0.5f,
                Quaternion.FromToRotation(Vector3.up, upper - lower));
        }
    }
}
