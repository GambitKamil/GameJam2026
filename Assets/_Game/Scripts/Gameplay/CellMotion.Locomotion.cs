using UnityEngine;

public sealed partial class CellMotion
{
    [SerializeField, Range(0.1f, 0.3f)] private float swingTime = 0.16f;
    [SerializeField, Range(0.1f, 0.4f)] private float maximumStrideLead = 0.27f;
    [SerializeField, Range(0.02f, 0.2f)] private float swingClearance = 0.11f;
    [SerializeField, Range(0.06f, 0.24f)] private float turnTime = 0.14f;
    [SerializeField, Min(0f)] private float gateBootClearance = 0.008f;

    private FootContact nearFootContact;
    private FootContact farFootContact;
    private FootContact swingingFoot;
    private FootContact nextFoot;
    private Vector3 previousLocomotionPosition;
    private float motionVelocity;
    private bool hasLocomotionSample;
    private bool hasFootContacts;
    private int visualFacing = 1;
    private int turnTarget = 1;
    private float turnElapsed;
    private bool floorRodActive = true;
    private Vector3 sampledRodPosition;
    private Quaternion sampledRodRotation;

    internal void SetFloorRodActive(bool active) => floorRodActive = active;

    private void InitializeLocomotion()
    {
        nearFootContact = new FootContact(frontLeg, nearBoot);
        farFootContact = new FootContact(backLeg, farBoot);
        nextFoot = nearFootContact;
        turnElapsed = turnTime;
    }

    private float UpdateLocomotion()
    {
        float dt = Time.deltaTime;
        Vector3 position = transform.position;
        float displacement = hasLocomotionSample ? position.x - previousLocomotionPosition.x : 0f;
        hasLocomotionSample = true;
        previousLocomotionPosition = position;

        float signedSpeed = dt > 0f && Mathf.Abs(displacement) < player.WalkSpeed * dt * 3f + 0.02f
            ? displacement / dt : 0f;
        motionVelocity = Mathf.Lerp(motionVelocity, signedSpeed, 1f - Mathf.Exp(-30f * dt));
        if (!Busy && Mathf.Abs(motionVelocity) > 0.2f)
            Face(motionVelocity);

        float speed = Mathf.Abs(motionVelocity);
        float targetGait = Busy || !player.IsGrounded ? 0f : Mathf.Clamp01(speed / player.WalkSpeed);
        gait = Mathf.MoveTowards(gait, targetGait, dt * 10f);
        if (!Busy && player.IsGrounded)
            phase = Mathf.Repeat(phase + Mathf.Abs(displacement) * (2f * Mathf.PI / 0.87f), 2f * Mathf.PI);
        return speed;
    }

    private void UpdateFigureFacing()
    {
        // Keep the near-side silhouette of the mirrored side-view rig. The torso leads the stance pivot.
        if (visualFacing != facing && turnElapsed >= turnTime)
        {
            turnTarget = facing;
            turnElapsed = 0f;
        }

        turnElapsed = Mathf.Min(turnElapsed + Time.deltaTime, turnTime);
        bool passingCenter = turnElapsed >= turnTime * 0.5f;
        if (passingCenter)
            visualFacing = turnTarget;

        float turnWeight = turnElapsed >= turnTime ? 0f : Mathf.Sin(turnElapsed / turnTime * Mathf.PI);
        float yaw = turnWeight * (passingCenter ? -visualFacing : visualFacing) * 15f;
        figure.localScale = new Vector3(visualFacing, 1f, 1f);
        figure.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private CellAnimationLibrary.Pose SampleLocomotion()
    {
        float walking = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.04f, 0.40f, gait));
        float low = Mathf.SmoothStep(0f, 1f, crouchBlend);
        if (low >= 1f)
            return SampleStance(CellAnimationLibrary.Motion.CrouchIdle, CellAnimationLibrary.Motion.CrouchWalk, walking);
        var standing = SampleStance(CellAnimationLibrary.Motion.Idle, CellAnimationLibrary.Motion.Walk, walking);
        if (low <= 0f)
            return standing;
        var crouched = SampleStance(CellAnimationLibrary.Motion.CrouchIdle, CellAnimationLibrary.Motion.CrouchWalk, walking);
        return BlendLocomotion(standing, crouched, low);
    }

    private CellAnimationLibrary.Pose SampleStance(CellAnimationLibrary.Motion idle, CellAnimationLibrary.Motion walk, float walking)
    {
        float cycle = phase / (2f * Mathf.PI);
        if (walking >= 1f)
            return library.Sample(walk, cycle);
        var idlePose = library.Sample(idle, Mathf.Repeat(Time.time * 0.4f, 1f));
        return walking <= 0f ? idlePose : BlendLocomotion(idlePose, library.Sample(walk, cycle), walking);
    }

    private static CellAnimationLibrary.Pose BlendLocomotion(CellAnimationLibrary.Pose from,
        CellAnimationLibrary.Pose to, float weight)
    {
        return new CellAnimationLibrary.Pose
        {
            HipHeightDelta = Mathf.Lerp(from.HipHeightDelta, to.HipHeightDelta, weight),
            TorsoPitch = Mathf.Lerp(from.TorsoPitch, to.TorsoPitch, weight),
            ArmPitch = Mathf.Lerp(from.ArmPitch, to.ArmPitch, weight)
        };
    }

    private Vector3 PoseGroundedFeet(Vector3 hipOffset)
    {
        if (!player.IsGrounded)
        {
            hasFootContacts = false;
            swingingFoot = null;
            nearFootContact.PoseInAir(hipOffset, gait);
            farFootContact.PoseInAir(hipOffset, gait);
            return hipOffset;
        }

        if (!hasFootContacts)
        {
            nearFootContact.Plant(this, nearBoot.position, figure.rotation);
            farFootContact.Plant(this, farBoot.position, figure.rotation);
            hasFootContacts = true;
        }

        if (swingingFoot != null)
            ProgressFootstep();
        if (swingingFoot == null && !Busy && Mathf.Abs(motionVelocity) > 0.10f)
        {
            float direction = Mathf.Sign(motionVelocity);
            FootContact other = OtherFoot(nextFoot);
            float threshold = Mathf.Lerp(0.035f, 0.105f, gait);
            if (Behind(other, direction) > 0.25f && Behind(other, direction) > Behind(nextFoot, direction))
                nextFoot = other;
            if (Behind(nextFoot, direction) >= threshold)
            {
                swingingFoot = nextFoot;
                swingingFoot.BeginSwing();
            }
        }

        FitPlantedStride(ref hipOffset);
        FitPlantedFoot(nearFootContact, ref hipOffset);
        FitPlantedFoot(farFootContact, ref hipOffset);
        nearFootContact.Pose(this, hipOffset);
        farFootContact.Pose(this, hipOffset);
        return hipOffset;
    }

    private FootContact OtherFoot(FootContact foot) => foot == nearFootContact ? farFootContact : nearFootContact;

    private float Behind(FootContact foot, float direction) =>
        (figure.TransformPoint(foot.Leg.BootHome).x - foot.Position.x) * direction;

    private void ProgressFootstep()
    {
        FootContact foot = swingingFoot;
        bool firstSwingFrame = foot.Progress <= 0f;
        foot.Progress += Time.deltaTime / swingTime;
        float t = Mathf.Clamp01(foot.Progress);
        Vector3 landing = figure.TransformPoint(foot.Leg.BootHome);
        float normalizedSpeed = Mathf.Clamp01(Mathf.Abs(motionVelocity) / player.WalkSpeed);
        landing.x += motionVelocity * (1f - t) * swingTime;
        landing.x += Mathf.Sign(motionVelocity) * maximumStrideLead * normalizedSpeed;
        if (Busy)
            landing.x = Mathf.MoveTowards(landing.x, foot.Start.x, 0.06f);

        foot.Goal = firstSwingFrame ? landing : Vector3.Lerp(foot.Goal, landing, 1f - Mathf.Exp(-26f * Time.deltaTime));
        if (floorRodActive && wakeRod != null && (wakeRod.position != sampledRodPosition || wakeRod.rotation != sampledRodRotation))
            CacheWakeRod();
        Vector3 goal = floorRodActive ? RodLanding(foot.Goal, foot.RadiusFor(this)) : foot.Goal;
        foot.FindSupport(this, goal, out Vector3 target, out RaycastHit hit);
        float distance = Mathf.SmoothStep(0f, 1f, t);
        foot.Position = Vector3.Lerp(foot.Start, target, distance);
        foot.Position += Vector3.up * (Mathf.Sin(t * Mathf.PI) * swingClearance * (0.50f + gait * 0.50f));
        if (floorRodActive)
            foot.Position = ClearRodSwing(foot, target, t);
        if (t < 1f)
            return;

        foot.SetContact(target, hit, figure.rotation);
        nextFoot = OtherFoot(foot);
        swingingFoot = null;
        footsteps.PlayOneShot(boot, Mathf.Lerp(0.42f, 0.17f, crouchBlend));
    }

    private Vector3 RodLanding(Vector3 goal, float bootRadius)
    {
        if (wakeRodNormal == Vector3.zero)
            return goal;

        Vector3 offset = goal - wakeRodCenter;
        float along = Vector3.Dot(offset, wakeRodAxis);
        float across = Vector3.Dot(offset, wakeRodNormal);
        float halfLength = wakeRodHalfLength + bootRadius + wakeRodClearance;
        float halfWidth = wakeRodHalfWidth + bootRadius + wakeRodClearance;
        if (Mathf.Abs(along) >= halfLength || Mathf.Abs(across) >= halfWidth)
            return goal;

        // A small sidestep leaves the whole landing footprint on the floor.
        if (halfLength - Mathf.Abs(along) < halfWidth - Mathf.Abs(across))
            return goal + wakeRodAxis * (Mathf.Sign(along) * halfLength - along);
        return goal + wakeRodNormal * (Mathf.Sign(across) * halfWidth - across);
    }

    private Vector3 ClearRodSwing(FootContact foot, Vector3 target, float progress)
    {
        Vector3 point = foot.Position;
        if (wakeRodNormal == Vector3.zero)
            return point;

        Vector3 from = foot.Start - wakeRodCenter;
        Vector3 to = target - wakeRodCenter;
        float enter = 0f;
        float exit = 1f;
        if (!ClipRodSweep(from, to, wakeRodAxis, wakeRodHalfLength + foot.RadiusFor(this), ref enter, ref exit) ||
            !ClipRodSweep(from, to, wakeRodNormal, wakeRodHalfWidth + foot.RadiusFor(this), ref enter, ref exit))
            return point;

        // Raise at the old contact before any horizontal movement into the rod.
        return StepOverRod(foot.Start, target, progress, wakeRodTop + foot.SoleOffset + wakeRodClearance);
    }

    private static bool ClipRodSweep(Vector3 from, Vector3 to, Vector3 axis, float extent, ref float enter, ref float exit)
    {
        float start = Vector3.Dot(from, axis);
        float delta = Vector3.Dot(to, axis) - start;
        if (Mathf.Abs(delta) < 0.0001f)
            return Mathf.Abs(start) <= extent;

        float first = (-extent - start) / delta;
        float second = (extent - start) / delta;
        enter = Mathf.Max(enter, Mathf.Min(first, second));
        exit = Mathf.Min(exit, Mathf.Max(first, second));
        return enter <= exit;
    }

    private Vector3 ConstrainBootLanding(Vector3 point, float bootRadius)
    {
        if (gateBlocker == null || !gateBlocker.enabled || !gateBlocker.gameObject.activeInHierarchy)
            return point;

        Transform leaf = gateBlocker.transform;
        Vector3 center = gateBlocker.center;
        Vector3 halfSize = gateBlocker.size * 0.5f;
        Vector3 localPoint = leaf.InverseTransformPoint(point) - center;
        Vector3 scale = leaf.lossyScale;
        float padding = bootRadius + gateBootClearance;
        if (Mathf.Abs(localPoint.z) > halfSize.z + padding / Mathf.Abs(scale.z))
            return point;

        // Reserve room for the whole boot as its yaw blends through the swing.
        float actorX = leaf.InverseTransformPoint(transform.position).x;
        float side = actorX < center.x ? -1f : 1f;
        float limit = halfSize.x + padding / Mathf.Abs(scale.x);
        if (localPoint.x * side >= limit)
            return point;

        localPoint.x = limit * side;
        return leaf.TransformPoint(localPoint + center);
    }

    private const float PlantedReachMargin = 0.005f;
    private const float MinimumVerticalReachSquared = 0.02f;

    private void FitPlantedStride(ref Vector3 hipOffset)
    {
        float minimumX = float.NegativeInfinity;
        float maximumX = float.PositiveInfinity;
        IncludePlantedReach(nearFootContact, hipOffset.z, ref minimumX, ref maximumX);
        IncludePlantedReach(farFootContact, hipOffset.z, ref minimumX, ref maximumX);

        // A long stance or turn can leave a planted foot beyond the reach of a vertical hip correction.
        if (minimumX <= maximumX)
            hipOffset.x = Mathf.Clamp(hipOffset.x, minimumX, maximumX);
    }

    private void IncludePlantedReach(FootContact foot, float hipZ, ref float minimumX, ref float maximumX)
    {
        if (!foot.Planted)
            return;

        Vector3 hipToAnkle = figure.InverseTransformPoint(foot.Position) + foot.Leg.AnkleOffset - foot.Leg.Hip;
        float depth = hipToAnkle.z - hipZ;
        float reach = foot.Leg.MaximumReach - PlantedReachMargin;
        float across = Mathf.Sqrt(Mathf.Max(0f, reach * reach - depth * depth - MinimumVerticalReachSquared));
        minimumX = Mathf.Max(minimumX, hipToAnkle.x - across);
        maximumX = Mathf.Min(maximumX, hipToAnkle.x + across);
    }

    private void FitPlantedFoot(FootContact foot, ref Vector3 hipOffset)
    {
        if (!foot.Planted)
            return;

        Vector3 ankle = figure.InverseTransformPoint(foot.Position) + foot.Leg.AnkleOffset;
        Vector3 hip = foot.Leg.Hip + hipOffset;
        Vector3 delta = ankle - hip;
        float reach = foot.Leg.MaximumReach - PlantedReachMargin;
        float verticalReach = Mathf.Sqrt(Mathf.Max(MinimumVerticalReachSquared, reach * reach - delta.x * delta.x - delta.z * delta.z));
        hipOffset.y = Mathf.Min(hipOffset.y, ankle.y + verticalReach - foot.Leg.Hip.y);
    }

    private sealed class FootContact
    {
        private readonly Transform bootTransform;
        private readonly float soleOffset;
        private readonly float bootRadius;
        private Quaternion rotation;

        public FootContact(LegPose leg, Transform shoe)
        {
            Leg = leg;
            bootTransform = shoe;
            rotation = shoe.rotation;
            float bottom = shoe.position.y;
            foreach (Renderer renderer in shoe.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds = renderer.bounds;
                Vector3 offset = bounds.center - shoe.position;
                float x = Mathf.Abs(offset.x) + bounds.extents.x;
                float z = Mathf.Abs(offset.z) + bounds.extents.z;
                bootRadius = Mathf.Max(bootRadius, Mathf.Sqrt(x * x + z * z));
                bottom = Mathf.Min(bottom, bounds.min.y);
            }
            soleOffset = shoe.position.y - bottom;
        }

        public LegPose Leg { get; }
        public float RadiusFor(CellMotion motion) => bootRadius * Mathf.Lerp(1f, 1.78f, motion.FootprintScale - 1f);
        public float SoleOffset => soleOffset;
        public Vector3 Position { get; set; }
        public Vector3 Start { get; private set; }
        public Vector3 Goal { get; set; }
        public bool Planted { get; private set; }
        public Collider Support { get; private set; }
        public float Progress { get; set; }

        public void BeginSwing()
        {
            Planted = false;
            Support = null;
            Progress = 0f;
            Start = Position;
        }

        public void Plant(CellMotion motion, Vector3 point, Quaternion bodyRotation)
        {
            FindSupport(motion, point, out Vector3 target, out RaycastHit hit);
            SetContact(target, hit, bodyRotation);
        }

        public void FindSupport(CellMotion motion, Vector3 point, out Vector3 target, out RaycastHit hit)
        {
            point = motion.ConstrainBootLanding(point, RadiusFor(motion));
            Vector3 sole = point - Vector3.up * soleOffset;
            // Foot probes span the boot silhouette offset and small floor height changes.
            bool found = motion.player.TryGetSupport(sole, 0.14f, 0.20f, out hit);
            target = found ? hit.point + Vector3.up * soleOffset : point;
        }

        public void SetContact(Vector3 point, RaycastHit hit, Quaternion bodyRotation)
        {
            Position = point;
            Support = hit.collider;
            Planted = Support != null;
            Quaternion slope = Planted ? Quaternion.FromToRotation(Vector3.up, hit.normal) : Quaternion.identity;
            rotation = slope * bodyRotation;
        }

        public void Pose(CellMotion motion, Vector3 hipOffset)
        {
            Leg.PoseFoot(hipOffset, motion.figure.InverseTransformPoint(Position));
            Quaternion target = Planted ? rotation : motion.figure.rotation;
            bootTransform.rotation = Quaternion.RotateTowards(bootTransform.rotation, target, Time.deltaTime * 540f);
        }

        public void PoseInAir(Vector3 hipOffset, float gait)
        {
            Planted = false;
            Support = null;
            Leg.PoseFoot(hipOffset, Leg.BootHome + Vector3.up * (0.04f + gait * 0.04f));
            bootTransform.localRotation = Quaternion.RotateTowards(bootTransform.localRotation, Quaternion.identity,
                Time.deltaTime * 540f);
        }
    }
}
