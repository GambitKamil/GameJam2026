using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(AudioSource))]
public sealed class CellMotion : MonoBehaviour
{
    [SerializeField] private Transform figure;
    [SerializeField] private Transform sleeve;
    [SerializeField] private Transform hand;
    [SerializeField] private Transform nearLeg;
    [SerializeField] private Transform farLeg;
    [SerializeField] private Transform nearBoot;
    [SerializeField] private Transform farBoot;
    [SerializeField] private Transform head;
    [SerializeField] private Transform nose;
    [SerializeField] private Transform hair;
    [SerializeField] private Transform eye;
    [SerializeField] private Renderer bunkMattress;
    [SerializeField] private AudioClip[] stoneFootsteps = System.Array.Empty<AudioClip>();

    private Rigidbody body;
    private SideViewPlayer player;
    private CellAnimationLibrary library;
    private CellAnimationLibrary.Motion gesture;
    private float gestureStart;
    private float gestureDuration;
    private Renderer[] wakeRenderers;
    private Bounds wakeBedBounds;
    private float crouchBlend;
    private float previousSpeed;
    private float lean;
    private AudioSource footsteps;
    private AudioClip boot;
    private int nextFootstep;
    private Transform shoulder;
    private Transform torso;
    private LegPose frontLeg;
    private LegPose backLeg;
    private Transform neck;
    private GameObject heldBread;
    private Vector3 rest;
    private float phase;
    private float gait;
    private float armAngle;
    private float bend;
    private float chew;
    private int facing = 1;
    private bool striding;
    private bool carryingRod;
    private Vector3 sleeveRestPosition;
    private Quaternion sleeveRestRotation;
    private Vector3 sleeveRestScale;
    private Vector3 handRestPosition;
    private Vector3 handTarget;
    private float handTargetWeight;
    public int Facing => facing;
    public bool OccupiesHand => heldBread != null || Busy;
    public bool Busy { get; private set; }
    public bool WakingUp { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        player = GetComponent<SideViewPlayer>();
        library = GetComponent<CellAnimationLibrary>();
        footsteps = GetComponent<AudioSource>();
        rest = figure.localPosition;
        shoulder = Pivot("Throw Shoulder", sleeve.position + Vector3.up * 0.11f, sleeve, hand);
        sleeveRestPosition = sleeve.localPosition;
        sleeveRestRotation = sleeve.localRotation;
        sleeveRestScale = sleeve.localScale;
        handRestPosition = hand.localPosition;
        neck = Pivot("Neck", head.position - Vector3.up * 0.14f, head, nose, hair, eye);
        frontLeg = new LegPose(nearLeg, nearBoot);
        backLeg = new LegPose(farLeg, farBoot);
        Vector3 hips = (frontLeg.Hip + backLeg.Hip) * 0.5f;
        torso = Pivot("Torso at Hips", figure.TransformPoint(hips), figure.Find("Jacket"),
            figure.Find("Jacket Seam"), shoulder, neck);
    }

    private IEnumerator Start()
    {
        if (bunkMattress == null)
            yield break;

        Busy = WakingUp = true;
        wakeRenderers = figure.GetComponentsInChildren<Renderer>();
        Bounds mattress = bunkMattress.bounds;
        wakeBedBounds = mattress;
        Transform frame = bunkMattress.transform.parent.Find("Bed Frame");
        if (frame != null && frame.TryGetComponent(out Renderer bedFrame))
            wakeBedBounds.Encapsulate(bedFrame.bounds);

        Vector3 standing = figure.TransformPoint((frontLeg.Hip + backLeg.Hip) * 0.5f);
        Vector3 lying = new Vector3(mattress.center.x, mattress.max.y + 0.12f, mattress.center.z);
        Vector3 side = new Vector3(mattress.center.x - 0.04f, mattress.max.y + 0.18f, mattress.min.z + 0.28f);
        Vector3 seated = new Vector3(standing.x, mattress.max.y + 0.12f, wakeBedBounds.min.z - 0.22f);
        const float seatedCrouch = 0.84f;
        Vector3 planted = standing + WakeHipOffset(seatedCrouch);
        Quaternion lyingRotation = Quaternion.Euler(0f, 0f, 90f);
        Quaternion turnedOnSide = Quaternion.Euler(23f, 13f, 82f);
        Quaternion feetOverEdge = Quaternion.Euler(0f, 82f, 0f);

        PoseWakeUp(lying, lyingRotation, 0.09f, 18f);
        yield return new WaitForSeconds(0.15f);
        yield return MoveWakePose(lying, side, lyingRotation, turnedOnSide, 0.09f, 0.26f, 18f, 47f, 0.32f);
        yield return MoveWakePose(side, seated, turnedOnSide, feetOverEdge, 0.26f, seatedCrouch, 47f, -30f, 0.60f);
        yield return new WaitForSeconds(0.12f);
        yield return MoveWakePose(seated, planted, feetOverEdge, Quaternion.identity, seatedCrouch, seatedCrouch, -30f, 28f, 0.55f);
        PlayFootstep(0.24f);
        yield return RiseFromFootPlant(standing, seatedCrouch);

        figure.localPosition = rest;
        figure.localRotation = Quaternion.identity;
        wakeRenderers = null;
        WakingUp = false;
        EndAction();
    }

    private IEnumerator MoveWakePose(Vector3 from, Vector3 to, Quaternion start, Quaternion finish,
        float startingCrouch, float endingCrouch, float startingArm, float endingArm, float duration)
    {
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            PoseWakeUp(Vector3.Lerp(from, to, t), Quaternion.Slerp(start, finish, t),
                Mathf.Lerp(startingCrouch, endingCrouch, t), Mathf.Lerp(startingArm, endingArm, t));
            yield return null;
        }

        PoseWakeUp(to, finish, endingCrouch, endingArm);
    }

    private IEnumerator RiseFromFootPlant(Vector3 standing, float seatedCrouch)
    {
        const float duration = 0.62f;
        float low = library != null ? library.Sample(CellAnimationLibrary.Motion.SitStand, 0f).HipHeightDelta : 0f;
        float high = library != null ? library.Sample(CellAnimationLibrary.Motion.SitStand, 1f).HipHeightDelta : 1f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = elapsed / duration;
            float rise = Mathf.SmoothStep(0f, 1f, t);
            float pitch = -33f * seatedCrouch * (1f - rise);
            if (library != null)
            {
                var sample = library.Sample(CellAnimationLibrary.Motion.SitStand, t);
                rise = Mathf.InverseLerp(low, high, sample.HipHeightDelta);
                pitch = Mathf.Lerp(pitch, sample.TorsoPitch, Mathf.Sin(t * Mathf.PI));
            }

            float crouch = seatedCrouch * (1f - rise);
            PoseWakeUp(standing + WakeHipOffset(crouch), Quaternion.identity, crouch, 28f * (1f - rise), pitch);
            yield return null;
        }

        PoseWakeUp(standing, Quaternion.identity, 0f, 0f);
    }

    private static Vector3 WakeHipOffset(float crouch) => new Vector3(-0.15f * crouch, -0.43f * crouch, 0f);

    private void PoseWakeUp(Vector3 pelvis, Quaternion rotation, float crouch, float arm, float? torsoPitch = null)
    {
        Vector3 offset = WakeHipOffset(crouch);
        torso.localPosition = (frontLeg.Hip + backLeg.Hip) * 0.5f + offset;
        torso.localRotation = Quaternion.Euler(0f, 0f, torsoPitch ?? -33f * crouch);
        frontLeg.Pose(0f, 0f, offset, crouch);
        backLeg.Pose(Mathf.PI, 0f, offset, crouch);
        shoulder.localRotation = Quaternion.Euler(0f, 0f, arm);
        figure.SetPositionAndRotation(pelvis - rotation * torso.localPosition, rotation);

        float lift = 0f;
        foreach (Renderer part in wakeRenderers)
        {
            if (!part.enabled)
                continue;

            Bounds bounds = part.bounds;
            lift = Mathf.Max(lift, 0.012f - bounds.min.y);
            // Keep body parts above the top of the bed while they cross its footprint.
            float edgeGap = Mathf.Max(0f, wakeBedBounds.min.x - bounds.max.x,
                bounds.min.x - wakeBedBounds.max.x, wakeBedBounds.min.z - bounds.max.z,
                bounds.min.z - wakeBedBounds.max.z);
            float bedSupport = 1f - Mathf.SmoothStep(0f, 1f, edgeGap / 0.22f);
            lift = Mathf.Max(lift, bedSupport * (wakeBedBounds.max.y + 0.012f - bounds.min.y));
        }

        figure.position += Vector3.up * lift;
    }

    private Transform Pivot(string label, Vector3 position, params Transform[] parts)
    {
        var pivot = new GameObject(label).transform;
        pivot.SetParent(figure, false);
        pivot.position = position;
        foreach (Transform part in parts)
            part.SetParent(pivot, true);
        return pivot;
    }

    public void Face(float x)
    {
        if (Mathf.Abs(x) > 0.05f)
            facing = x < 0f ? -1 : 1;
    }

    public void SetCarryingRod(bool carrying)
    {
        carryingRod = carrying;
    }

    public void SetHandTarget(Vector3 target, float weight)
    {
        handTarget = target;
        handTargetWeight = Mathf.Clamp01(weight);
    }

    public Vector3 HandPosition => hand.position;
    public Vector3 ThrowOrigin(Vector3 direction)
    {
        float sign = Mathf.Abs(direction.x) > 0.05f ? Mathf.Sign(direction.x) : facing;
        return transform.position + new Vector3(0.28f * sign, 0.48f, 0f);
    }

    public void Hold(GameObject template)
    {
        PlayLibraryGesture(CellAnimationLibrary.Motion.TablePickup, 0.38f);
        heldBread = Instantiate(template);
        heldBread.name = "Bread In Hand";
        heldBread.transform.localScale *= 0.72f;
        heldBread.SetActive(true);
    }

    public void Release()
    {
        if (heldBread != null)
            Destroy(heldBread);
        heldBread = null;
    }

    public void Eat(GameObject template)
    {
        StartCoroutine(TakeBite(template));
    }

    private IEnumerator TakeBite(GameObject template)
    {
        Busy = true;
        PlayLibraryGesture(CellAnimationLibrary.Motion.TablePickup, 0.68f);
        var bite = Instantiate(template);
        bite.name = "Bread Bite";
        bite.transform.localScale *= 0.62f;
        bite.SetActive(true);
        for (float elapsed = 0; elapsed < 0.68f; elapsed += Time.deltaTime)
        {
            armAngle = 138f * Mathf.Clamp01(Mathf.Min(elapsed / 0.18f, (0.68f - elapsed) / 0.19f));
            chew = elapsed >= 0.21f ? Mathf.Sin(elapsed * 51f) * 8f : 0f;
            bite.SetActive(elapsed < 0.25f);
            bite.transform.position = hand.position;
            yield return null;
        }

        Destroy(bite);
        EndAction();
    }

    public IEnumerator ThrowGesture(Vector3 direction, System.Action release)
    {
        Busy = true;
        Face(direction.x);
        PlayLibraryGesture(CellAnimationLibrary.Motion.Throw, 0.51f);
        yield return Pose(-95f, 5f, 0.17f);
        yield return Pose(105f + Mathf.Clamp(direction.y * 4f, -35f, 35f), 12f, 0.12f);
        Release();
        release();
        yield return Pose(0f, 0f, 0.22f);
        EndAction();
    }

    public IEnumerator Reach(float angle, float crouch, float duration)
    {
        Busy = true;
        yield return Pose(angle, crouch, duration);
    }

    public void PlayLibraryGesture(CellAnimationLibrary.Motion clip, float seconds)
    {
        gesture = clip;
        gestureStart = Time.time;
        gestureDuration = seconds;
    }

    public void EndAction()
    {
        gestureDuration = 0f;
        armAngle = bend = chew = handTargetWeight = 0f;
        Busy = false;
    }

    private IEnumerator Pose(float angle, float crouch, float duration)
    {
        float startArm = armAngle;
        float startBend = bend;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            armAngle = Mathf.Lerp(startArm, angle, t);
            bend = Mathf.Lerp(startBend, crouch, t);
            yield return null;
        }

        armAngle = angle;
        bend = crouch;
    }

    private void LateUpdate()
    {
        if (WakingUp)
            return;

        float x = body.linearVelocity.x;
        float speed = Mathf.Abs(x);
        if (speed > 0.2f && !Busy)
            Face(x);
        gait = Mathf.MoveTowards(gait, Busy ? 0f : Mathf.Clamp01(speed / 3.5f), Time.deltaTime * 10f);
        float previous = phase;
        if (!Busy && speed > 0.04f)
            phase += speed * Time.deltaTime * (2f * Mathf.PI / 0.87f);
        bool walking = gait > 0.19f;
        if (walking && (!striding || Mathf.FloorToInt(previous / Mathf.PI) != Mathf.FloorToInt(phase / Mathf.PI)))
            PlayFootstep(Mathf.Lerp(0.42f, 0.17f, crouchBlend));
        striding = walking;

        figure.localScale = new Vector3(facing, 1f, 1f);
        figure.localPosition = rest;
        float swing = Mathf.Sin(phase) * gait;
        crouchBlend = Mathf.MoveTowards(crouchBlend, !Busy && player.Crouching ? 1f : 0f, Time.deltaTime * 5.5f);
        float crouch = Mathf.Max(Mathf.Clamp01(bend / 36f), crouchBlend * 0.67f);
        float acceleration = (speed - previousSpeed) / Mathf.Max(Time.deltaTime, 0.001f);
        previousSpeed = speed;
        lean = Mathf.MoveTowards(lean, Busy ? 0f : Mathf.Clamp(acceleration * 0.45f, -8f, 9f), 65f * Time.deltaTime);
        float bob = Mathf.Abs(swing) * 0.025f;
        var hipOffset = new Vector3(-0.15f * crouch, bob - 0.43f * crouch, 0f);
        float pitch = -33f * crouch - 5f * gait - lean;
        float restingArm = carryingRod ? 42f - 7f * swing : -26f * swing;
        float arm = Busy ? armAngle : heldBread != null ? 67f : restingArm;
        Vector3 nearStride = Vector3.zero;
        Vector3 farStride = Vector3.zero;
        float strideBlend = 0f;
        if (library != null)
        {
            bool walkingPose = gait > 0.08f;
            bool low = crouchBlend > 0.45f;
            var clip = walkingPose
                ? (low ? CellAnimationLibrary.Motion.CrouchWalk : CellAnimationLibrary.Motion.Walk)
                : (low ? CellAnimationLibrary.Motion.CrouchIdle : CellAnimationLibrary.Motion.Idle);
            float time = walkingPose ? Mathf.Repeat(phase / (2f * Mathf.PI), 1f) : Mathf.Repeat(Time.time * 0.4f, 1f);
            var locomotion = library.Sample(clip, time);
            if (!Busy)
            {
                strideBlend = Mathf.SmoothStep(0f, 1f, gait * 2.5f);
                nearStride = locomotion.NearStride * gait;
                farStride = locomotion.FarStride * gait;
                hipOffset.y = Mathf.Lerp(hipOffset.y, locomotion.HipHeightDelta * 0.82f, 0.64f);
                pitch = Mathf.Lerp(pitch, locomotion.TorsoPitch - lean, 0.65f);
                if (!carryingRod && heldBread == null)
                    arm = Mathf.Lerp(arm, locomotion.ArmPitch, 0.7f);
            }

            float progress = gestureDuration > 0f ? (Time.time - gestureStart) / gestureDuration : 1f;
            if (progress < 1f)
            {
                var action = library.Sample(gesture, progress);
                float weight = Mathf.Sin(progress * Mathf.PI);
                pitch = Mathf.Lerp(pitch, action.TorsoPitch, weight * 0.75f);
                hipOffset.y = Mathf.Lerp(hipOffset.y, action.HipHeightDelta * 0.85f, weight * 0.42f);
                arm = Mathf.Lerp(arm, action.ArmPitch, weight * 0.28f);
            }
        }

        torso.localPosition = (frontLeg.Hip + backLeg.Hip) * 0.5f + hipOffset;
        torso.localRotation = Quaternion.Euler(0f, 0f, pitch);
        frontLeg.Pose(phase, gait, hipOffset, crouch, nearStride, strideBlend);
        backLeg.Pose(phase + Mathf.PI, gait, hipOffset, crouch, farStride, strideBlend);
        shoulder.localRotation = Quaternion.Euler(0f, 0f, arm);
        neck.localRotation = Quaternion.Euler(0f, 0f, chew);
        AlignHand();
        if (heldBread != null)
            heldBread.transform.position = hand.position + new Vector3(facing * 0.045f, 0f, -0.05f);
    }

    private void AlignHand()
    {
        sleeve.localPosition = sleeveRestPosition;
        sleeve.localRotation = sleeveRestRotation;
        sleeve.localScale = sleeveRestScale;
        hand.localPosition = handRestPosition;
        if (handTargetWeight <= 0f)
            return;

        Vector3 root = shoulder.position;
        Vector3 wrist = Vector3.Lerp(hand.position, handTarget, handTargetWeight);
        Vector3 arm = root - wrist;
        sleeve.position = (root + wrist) * 0.5f;
        sleeve.rotation = Quaternion.FromToRotation(Vector3.up, arm);
        sleeve.localScale = new Vector3(sleeveRestScale.x, arm.magnitude, sleeveRestScale.z);
        hand.position = wrist;
    }

    private sealed class LegPose
    {
        private readonly Transform thigh;
        private readonly Transform shin;
        private readonly Transform boot;
        private readonly Vector3 bootHome;
        private readonly Vector3 ankleOffset = new Vector3(0f, 0.075f, 0f);
        private readonly Vector3 width;
        private readonly float length;

        public Vector3 Hip { get; }

        public LegPose(Transform leg, Transform shoe)
        {
            thigh = leg;
            boot = shoe;
            bootHome = boot.localPosition;
            Hip = leg.localPosition + Vector3.up * 0.32f;
            width = leg.localScale;
            length = Vector3.Distance(Hip, bootHome + ankleOffset) * 0.52f;
            shin = Instantiate(leg, leg.parent, false);
            shin.name = leg.name + " Shin";
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
            stride = Vector3.Lerp(stride, sampledStride, strideBlend);
            Vector3 ankle = bootHome + stride + ankleOffset;
            Vector3 hip = Hip + hipOffset;
            Vector3 axis = ankle - hip;
            Vector3 ahead = new Vector3(-axis.y, axis.x, 0f).normalized;
            float kneeTravel = Mathf.Sqrt(Mathf.Max(0f, length * length - axis.sqrMagnitude * 0.25f));
            Vector3 knee = (hip + ankle) * 0.5f + ahead * kneeTravel;

            PlaceSegment(thigh, hip, knee, 1f);
            PlaceSegment(shin, knee, ankle, 0.87f);
            boot.localPosition = bootHome + stride;
        }

        private void PlaceSegment(Transform segment, Vector3 upper, Vector3 lower, float taper)
        {
            segment.localPosition = (upper + lower) * 0.5f;
            segment.localRotation = Quaternion.FromToRotation(Vector3.up, upper - lower);
            segment.localScale = new Vector3(width.x * taper, Vector3.Distance(upper, lower) + 0.055f, width.z * taper);
        }
    }

    private void PlayFootstep(float volume)
    {
        AudioClip clip = null;
        if (stoneFootsteps.Length != 0)
        {
            clip = stoneFootsteps[nextFootstep];
            nextFootstep = (nextFootstep + 1) % stoneFootsteps.Length;
        }

        if (clip == null)
        {
            if (boot == null)
                boot = MakeFootstep();
            clip = boot;
        }

        footsteps.PlayOneShot(clip, volume);
    }

    private static AudioClip MakeFootstep()
    {
        const int sampleRate = 22050;
        var samples = new float[2400];
        var noise = new System.Random(41);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / sampleRate;
            float grit = (float)noise.NextDouble() * 2f - 1f;
            samples[i] = Mathf.Min(t * 650f, 1f) * Mathf.Exp(-37f * t)
                * (Mathf.Sin(t * 2f * Mathf.PI * 76f) * 0.56f + grit * 0.23f);
        }

        var clip = AudioClip.Create("Cell Boot on Stone", samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDestroy()
    {
        Destroy(boot);
        Release();
    }
}
