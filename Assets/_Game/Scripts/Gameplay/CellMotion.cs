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

    private Rigidbody body;
    private AudioSource footsteps;
    private AudioClip boot;
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

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
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
        boot = MakeFootstep();
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

    public void EndAction()
    {
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
        float x = body.linearVelocity.x;
        float speed = Mathf.Abs(x);
        if (speed > 0.2f && !Busy)
            Face(x);
        gait = Mathf.MoveTowards(gait, Busy ? 0f : Mathf.Clamp01(speed / 3.5f), Time.deltaTime * 10f);
        float previous = phase;
        if (gait > 0.05f)
            phase += Time.deltaTime * (2f + speed * 2.4f);
        bool walking = gait > 0.30f;
        if (walking && (!striding || Mathf.FloorToInt(previous / Mathf.PI) != Mathf.FloorToInt(phase / Mathf.PI)))
            footsteps.PlayOneShot(boot, 0.42f);
        striding = walking;

        figure.localScale = new Vector3(facing, 1f, 1f);
        figure.localPosition = rest;
        float swing = Mathf.Sin(phase) * gait;
        float crouch = Mathf.Clamp01(bend / 36f);
        float bob = Mathf.Abs(swing) * 0.025f;
        var hipOffset = new Vector3(-0.15f * crouch, bob - 0.43f * crouch, 0f);
        torso.localPosition = (frontLeg.Hip + backLeg.Hip) * 0.5f + hipOffset;
        torso.localRotation = Quaternion.Euler(0f, 0f, -33f * crouch - 5f * gait);
        frontLeg.Pose(phase, gait, hipOffset, crouch);
        backLeg.Pose(phase + Mathf.PI, gait, hipOffset, crouch);
        float restingArm = carryingRod ? 42f - 7f * swing : -26f * swing;
        shoulder.localRotation = Quaternion.Euler(0f, 0f, Busy ? armAngle : heldBread != null ? 67f : restingArm);
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

        public void Pose(float phase, float gait, Vector3 hipOffset, float crouch)
        {
            Vector3 stride = new Vector3(
                Mathf.Sin(phase) * 0.23f * gait,
                Mathf.Max(0f, Mathf.Cos(phase)) * 0.12f * gait, 0f) * (1f - crouch);
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
