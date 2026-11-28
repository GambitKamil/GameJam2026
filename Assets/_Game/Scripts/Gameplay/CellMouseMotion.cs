using UnityEngine;

// The authored rat has only body, head and tail weights. The extra joints are
// bound to a runtime copy of that mesh in its original rest pose.
internal sealed class CellMouseMotion
{
    private const int FirstLegBone = 5;
    private const float ContactFraction = 0.54f;
    private const float FastSpeed = 6.3f;
    private static readonly string[] PawNames = { "Front Left Paw", "Front Right Paw", "Rear Left Paw", "Rear Right Paw" };
    private readonly Transform root;
    private readonly Transform head;
    private readonly Transform tail;
    private readonly Transform body;
    private readonly Transform tailMiddle;
    private readonly Transform tailTip;
    private readonly Quaternion headRest;
    private readonly Quaternion tailRest;
    private readonly Mesh mesh;
    private readonly Vector2 strideLength;
    private readonly Vector2 stepHeight;
    private readonly float turnTime;
    private readonly float tailResponse;
    private readonly RaycastHit[] groundHits = new RaycastHit[12];
    private readonly Paw[] paws;
    private Vector3 previousPosition;
    private Vector3 heading;
    private float yaw, yawVelocity, speed, gait, pitch, pitchVelocity;
    private float tailBaseAngle, tailMiddleAngle, tailTipAngle;
    private float tailBaseVelocity, tailMiddleVelocity, tailTipVelocity;
    private float takeoff, flightProgress, landingCompression;
    private float aerialWeight, airVelocity;
    private bool landingFlight;
    private bool wasAirborne;

    public float NibblePitch { get; set; }
    public bool IsAirborne { get; private set; }
    public float TailTipAngle => tailTipAngle;
    public float TailBaseAngle => tailBaseAngle;

    public CellMouseMotion(Transform root, Vector2 strideLength, Vector2 stepHeight, float turnTime, float tailResponse)
    {
        this.root = root;
        this.strideLength = new Vector2(Mathf.Max(0.05f, strideLength.x), Mathf.Max(0.05f, strideLength.y));
        this.stepHeight = stepHeight;
        this.turnTime = turnTime;
        this.tailResponse = tailResponse;
        previousPosition = root.position;
        heading = root.right;
        yaw = Vector3.SignedAngle(Vector3.right, heading, Vector3.up);
        head = root.Find("Mouse Head");
        tail = root.Find("Mouse Tail");

        var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
        if (renderer != null && renderer.sharedMesh != null && renderer.sharedMesh.isReadable &&
            renderer.sharedMesh.name == "Grey Rat Skinned" && renderer.bones.Length == 3)
        {
            Transform meshTransform = renderer.transform;
            Transform[] originalBones = renderer.bones;
            body = MakeBone("Rat Spine", root, root.InverseTransformPoint(meshTransform.position));
            body.rotation = meshTransform.rotation;
            head = MakeBone("Rat Head Joint", body, meshTransform.InverseTransformPoint(originalBones[1].position));
            tail = MakeBone("Rat Tail Joint", body, meshTransform.InverseTransformPoint(originalBones[2].position));
            tailMiddle = MakeBone("Rat Tail Middle", tail, new Vector3(-0.056f, -0.038f, -0.01f));
            tailTip = MakeBone("Rat Tail Tip", tailMiddle, new Vector3(-0.058f, -0.013f, 0f));
            paws = new Paw[4];
            var bones = new Transform[FirstLegBone + paws.Length * 3];
            bones[0] = body;
            bones[1] = head;
            bones[2] = tail;
            bones[3] = tailMiddle;
            bones[4] = tailTip;
            for (int i = 0; i < paws.Length; i++)
            {
                paws[i] = new Paw(body, i);
                bones[FirstLegBone + i * 3] = paws[i].Hip;
                bones[FirstLegBone + i * 3 + 1] = paws[i].Knee;
                bones[FirstLegBone + i * 3 + 2] = paws[i].Foot;
            }

            mesh = Object.Instantiate(renderer.sharedMesh);
            mesh.name = "Grey Rat Runtime Skin";
            Bind(mesh, bones, meshTransform.localToWorldMatrix);
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
        }
        headRest = head != null ? head.localRotation : Quaternion.identity;
        tailRest = tail != null ? tail.localRotation : Quaternion.identity;
    }

    public void Run(Vector3 direction, float preload = 0f)
    {
        SetHeading(direction);
        takeoff = preload;
        IsAirborne = false;
    }

    public void Jump(Vector3 direction, float progress, bool lands)
    {
        SetHeading(direction);
        takeoff = 0f;
        IsAirborne = true;
        flightProgress = progress;
        landingFlight = lands;
    }

    public void Tick(float dt)
    {
        if (dt <= 0f || !root.gameObject.activeInHierarchy)
            return;

        Vector3 travel = root.position - previousPosition;
        travel.y = 0f;
        previousPosition = root.position;
        float distance = travel.magnitude;
        speed = Mathf.Lerp(speed, distance / dt, 1f - Mathf.Exp(-20f * dt));
        float previousYaw = yaw;
        float targetYaw = Vector3.SignedAngle(Vector3.right, heading, Vector3.up);
        yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, turnTime, 1000f, dt);
        float yawDelta = Mathf.DeltaAngle(previousYaw, yaw);
        Quaternion flat = Quaternion.AngleAxis(yaw, Vector3.up);

        float speedFraction = Mathf.Clamp01(speed / FastSpeed);
        float stride = Mathf.Lerp(strideLength.x, strideLength.y, speedFraction);
        float previousGait = gait;
        float turnTravel = Mathf.Abs(yawDelta) * Mathf.Deg2Rad * 0.13f;
        if (!IsAirborne)
            gait += (distance + turnTravel) / stride;
        float gaitAngle = gait * Mathf.PI * 2f;
        float activity = Mathf.Clamp01((speed + turnTravel / dt) / 0.75f);

        if (wasAirborne && !IsAirborne)
            landingCompression = 1f;
        wasAirborne = IsAirborne;
        aerialWeight = Mathf.SmoothDamp(aerialWeight, IsAirborne ? 1f : 0f, ref airVelocity, 0.045f, Mathf.Infinity, dt);
        landingCompression = Mathf.MoveTowards(landingCompression, 0f, dt / 0.12f);

        float targetPitch = Mathf.Sin(gaitAngle) * 3f * activity - 7f * takeoff;
        if (IsAirborne)
            targetPitch = Mathf.Lerp(21f, landingFlight ? -17f : 4f, flightProgress);
        pitch = Mathf.SmoothDampAngle(pitch, targetPitch, ref pitchVelocity, 0.045f, Mathf.Infinity, dt);
        float roll = Mathf.Sin(gaitAngle) * 4f * activity * (1f - aerialWeight);
        Quaternion pose = flat * Quaternion.Euler(roll, 0f, pitch);
        if (body != null)
        {
            float bob = (0.006f * Mathf.Cos(2f * gaitAngle) - speedFraction * 0.014f) * activity;
            float weightDrop = bob - 0.019f * takeoff - 0.018f * landingCompression * (1f - landingCompression);
            body.SetPositionAndRotation(root.position + Vector3.up * weightDrop * (1f - aerialWeight), pose);
            Vector3 travelDirection = distance > 0.0001f ? travel / distance : flat * Vector3.right;
            foreach (Paw paw in paws)
                UpdatePaw(paw, previousGait, stride, travelDirection, flat, activity, dt);
        }
        else
            root.rotation = pose;

        float headBob = Mathf.Sin(gaitAngle + 0.8f) * 5f * activity;
        float idle = Mathf.Sin(Time.time * 7f) * 2f * (1f - activity);
        if (head != null)
            head.localRotation = headRest * Quaternion.Euler(0f, 0f, NibblePitch + headBob + idle - pitch * 0.26f);

        float sway = Mathf.Sin(gaitAngle - 0.7f) * 13f * activity * (1f - aerialWeight);
        float turnDrag = Mathf.Clamp(yawVelocity * -0.06f, -29f, 29f);
        tailBaseAngle = Mathf.SmoothDampAngle(tailBaseAngle, sway + turnDrag, ref tailBaseVelocity, tailResponse, Mathf.Infinity, dt);
        if (tail != null)
            tail.localRotation = tailRest * Quaternion.Euler(0f, tailBaseAngle, -2f * activity);
        if (tailMiddle != null)
        {
            tailMiddleAngle = Mathf.SmoothDampAngle(tailMiddleAngle, tailBaseAngle * 0.7f, ref tailMiddleVelocity, tailResponse, Mathf.Infinity, dt);
            tailTipAngle = Mathf.SmoothDampAngle(tailTipAngle, tailMiddleAngle * 0.65f, ref tailTipVelocity, tailResponse, Mathf.Infinity, dt);
            tailMiddle.localRotation = Quaternion.Euler(0f, tailMiddleAngle, 0f);
            tailTip.localRotation = Quaternion.Euler(0f, tailTipAngle, 0f);
        }
    }

    public bool TryGetPaw(int index, out Vector3 sole, out bool planted)
    {
        if (paws == null || (uint)index >= (uint)paws.Length)
        {
            sole = default;
            planted = false;
            return false;
        }

        Paw paw = paws[index];
        // The skinned paw vertices below the ankle follow this joint rigidly.
        sole = paw.Foot.TransformPoint(Vector3.down * paw.SoleDepth);
        planted = paw.Planted;
        return true;
    }

    public void Release()
    {
        if (mesh != null)
            Object.Destroy(mesh);
    }

    private void SetHeading(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            heading = direction.normalized;
    }

    private void UpdatePaw(Paw paw, float previousGait, float stride, Vector3 travelDirection,
        Quaternion flat, float activity, float dt)
    {
        float progress = Mathf.Repeat(gait + paw.PhaseOffset, 1f);
        Vector3 home = root.position + flat * paw.RestFoot;
        Vector3 nominalTravel = home - paw.PreviousHome;
        nominalTravel.y = 0f;
        if (nominalTravel.sqrMagnitude > 0.000004f && paw.Initialized)
            travelDirection = nominalTravel.normalized;

        Vector3 foot;
        if (IsAirborne)
        {
            float tuck = Mathf.Sin(flightProgress * Mathf.PI) * (landingFlight ? 1f : 0.85f);
            Vector3 lift = new Vector3((paw.PhaseOffset < 0.5f ? 0.023f : -0.023f) * tuck, (0.025f + 0.055f * tuck), 0f);
            Vector3 flightFoot = body.TransformPoint(paw.RestFoot + lift) - Vector3.up * paw.SoleDepth;
            foot = paw.Initialized ? Vector3.Lerp(paw.Sole, flightFoot, 1f - Mathf.Exp(-36f * dt)) : flightFoot;
            paw.Planted = false;
        }
        else if (activity > 0.08f && progress > ContactFraction)
        {
            float swing = Mathf.InverseLerp(ContactFraction, 1f, progress);
            Vector3 touchdown = OnFloor(home + travelDirection * (stride * (1f - progress + ContactFraction * 0.5f)));
            foot = paw.Initialized ? Vector3.Lerp(paw.Anchor, touchdown, Mathf.SmoothStep(0f, 1f, swing)) : touchdown;
            foot.y += Mathf.Sin(Mathf.PI * swing) * Mathf.Lerp(stepHeight.x, stepHeight.y, activity);
            paw.Planted = false;
        }
        else
        {
            bool touchdown = Mathf.Floor(gait + paw.PhaseOffset) > Mathf.Floor(previousGait + paw.PhaseOffset);
            if (!paw.Initialized || touchdown)
            {
                float lead = ContactFraction * 0.5f - progress;
                paw.Anchor = OnFloor(home + travelDirection * (activity * stride * lead));
                paw.Planted = true;
            }
            else if (!paw.Planted)
            {
                Vector3 settle = OnFloor(activity > 0.08f ? home : paw.Sole);
                foot = Vector3.MoveTowards(paw.Sole, settle, dt * 0.8f);
                paw.Anchor = foot;
                paw.Planted = (foot - settle).sqrMagnitude < 0.000001f;
            }
            foot = paw.Anchor;
        }

        paw.Sole = foot;
        paw.PreviousHome = home;
        paw.Initialized = true;
        paw.Solve(foot + Vector3.up * paw.SoleDepth, flat, body);
    }

    private Vector3 OnFloor(Vector3 position)
    {
        Vector3 origin = new Vector3(position.x, root.position.y + 0.19f, position.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, 0.52f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        float groundY = 0f; // The existing path follows the Cell's floor plane at Y=0.
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.normal.y < 0.7f || hit.distance >= nearest || hit.transform.IsChildOf(root))
                continue;
            nearest = hit.distance;
            groundY = hit.point.y;
        }
        position.y = groundY;
        return position;
    }

    private static Transform MakeBone(string name, Transform parent, Vector3 localPosition)
    {
        var bone = new GameObject(name).transform;
        bone.SetParent(parent, false);
        bone.localPosition = localPosition;
        return bone;
    }

    private static void Bind(Mesh mesh, Transform[] bones, Matrix4x4 meshInWorld)
    {
        var bindPoses = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            bindPoses[i] = bones[i].worldToLocalMatrix * meshInWorld;

        Vector3[] vertices = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];
            BoneWeight original = weights[i];
            BoneWeight weight = default;
            float tailWeight = (original.boneIndex0 == 2 ? original.weight0 : 0f) +
                               (original.boneIndex1 == 2 ? original.weight1 : 0f);
            if (tailWeight > 0f)
            {
                float tailPhase = Mathf.Clamp01((-vertex.x - 0.31f) / 0.16f) * 2f;
                int segment = Mathf.Min(1, Mathf.FloorToInt(tailPhase));
                float blend = tailPhase - segment;
                AddWeight(ref weight, 0, 1f - tailWeight);
                AddWeight(ref weight, 2 + segment, tailWeight * (1f - blend));
                AddWeight(ref weight, 3 + segment, tailWeight * blend);
            }
            else
            {
                bool front = vertex.x > -0.015f;
                int paw = (front ? 0 : 2) + (vertex.z < 0f ? 0 : 1);
                float side = Ease(0.047f, 0.074f, Mathf.Abs(vertex.z));
                float zone = 1f - Ease(0.043f, 0.071f, Mathf.Abs(vertex.x - (front ? 0.099f : -0.138f)));
                float leg = side * zone * (1f - Ease(0.068f, 0.149f, vertex.y));
                float toe = 1f - Ease(0.030f, 0.062f, vertex.y);
                float thigh = Ease(0.062f, 0.113f, vertex.y);
                int hip = FirstLegBone + paw * 3;
                AddWeight(ref weight, original.boneIndex0, original.weight0 * (1f - leg));
                AddWeight(ref weight, original.boneIndex1, original.weight1 * (1f - leg));
                AddWeight(ref weight, hip, leg * thigh);
                AddWeight(ref weight, hip + 1, leg * (1f - thigh - toe));
                AddWeight(ref weight, hip + 2, leg * toe);
            }

            weights[i] = weight;
        }
        mesh.bindposes = bindPoses;
        mesh.boneWeights = weights;
    }

    private static float Ease(float from, float to, float value) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

    private static void AddWeight(ref BoneWeight weight, int joint, float value)
    {
        if (value <= 0f)
            return;
        // The authored vertices use at most two influences. Each transition adds
        // at most two new ones and the legacy Mesh API expects descending weights.
        if (value > weight.weight0)
        {
            (value, weight.weight0) = (weight.weight0, value);
            (joint, weight.boneIndex0) = (weight.boneIndex0, joint);
        }
        if (value > weight.weight1)
        {
            (value, weight.weight1) = (weight.weight1, value);
            (joint, weight.boneIndex1) = (weight.boneIndex1, joint);
        }
        if (value > weight.weight2)
        {
            (value, weight.weight2) = (weight.weight2, value);
            (joint, weight.boneIndex2) = (weight.boneIndex2, joint);
        }
        if (value > weight.weight3)
        {
            weight.weight3 = value;
            weight.boneIndex3 = joint;
        }
    }

    private sealed class Paw
    {
        public readonly Transform Hip;
        public readonly Transform Knee;
        public readonly Transform Foot;
        public readonly Vector3 RestFoot;
        public readonly float PhaseOffset;
        public readonly float SoleDepth;
        private readonly Vector3 upperRest, lowerRest, kneePole;
        private readonly float upperLength, lowerLength;
        public Vector3 Anchor, Sole, PreviousHome;
        public bool Initialized, Planted;

        public Paw(Transform body, int index)
        {
            bool front = index < 2;
            float side = index % 2 == 0 ? -1f : 1f;
            PhaseOffset = (index == 0 || index == 3) ? 0f : 0.5f;
            SoleDepth = front ? 0.007f : 0.006f;
            RestFoot = new Vector3(front ? 0.099f : -0.138f, front ? 0.013f : 0.006f, side * (front ? 0.0915f : 0.0886f));
            var hip = new Vector3(front ? 0.079f : -0.139f, front ? 0.134f : 0.133f, side * 0.062f);
            var knee = new Vector3(front ? 0.041f : -0.087f, 0.069f, side * 0.078f);
            upperRest = knee - hip;
            lowerRest = RestFoot - knee;
            kneePole = knee - (hip + RestFoot) * 0.5f;
            upperLength = upperRest.magnitude;
            lowerLength = lowerRest.magnitude;
            Hip = MakeBone(PawNames[index] + " Hip", body, hip);
            Knee = MakeBone(PawNames[index] + " Knee", Hip, upperRest);
            Foot = MakeBone(PawNames[index], Knee, lowerRest);
        }

        public void Solve(Vector3 target, Quaternion flat, Transform body)
        {
            Vector3 hip = Hip.position;
            Vector3 reach = target - hip;
            if (reach.sqrMagnitude < 0.0000001f)
                reach = Vector3.down * 0.001f;
            Vector3 axis = reach.normalized;
            float length = Mathf.Clamp(reach.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.0001f, upperLength + lowerLength - 0.0001f);
            float along = (upperLength * upperLength - lowerLength * lowerLength + length * length) / (2f * length);
            Vector3 pole = Vector3.ProjectOnPlane(body.TransformDirection(kneePole), axis).normalized;
            Vector3 knee = hip + axis * along + pole * Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 ankle = hip + axis * length;
            Hip.rotation = Quaternion.FromToRotation(body.TransformDirection(upperRest), knee - hip) * body.rotation;
            Knee.SetPositionAndRotation(knee, Quaternion.FromToRotation(body.TransformDirection(lowerRest), ankle - knee) * body.rotation);
            Foot.SetPositionAndRotation(ankle, flat);
        }
    }
}
