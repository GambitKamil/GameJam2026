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
    private Transform frontHip;
    private Transform backHip;
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
    public int Facing => facing;
    public bool OccupiesHand => heldBread != null || Busy;
    public bool Busy { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        footsteps = GetComponent<AudioSource>();
        rest = figure.localPosition;
        shoulder = Pivot("Throw Shoulder", sleeve.position + Vector3.up * 0.11f, sleeve, hand);
        // The model has one visible arm. Keep that arm on the camera side.
        frontHip = Pivot("Near Hip", nearLeg.position + Vector3.up * 0.31f, nearLeg, nearBoot);
        backHip = Pivot("Far Hip", farLeg.position + Vector3.up * 0.31f, farLeg, farBoot);
        neck = Pivot("Neck", head.position - Vector3.up * 0.14f, head, nose, hair, eye);
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
        armAngle = bend = chew = 0f;
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
        float swing = Mathf.Sin(phase) * gait;
        figure.localPosition = rest + Vector3.up * (Mathf.Abs(swing) * 0.055f - bend * 0.008f);
        figure.localRotation = Quaternion.Euler(0f, 0f, -bend * facing - 5f * gait * facing);
        frontHip.localRotation = Quaternion.Euler(0f, 0f, 34f * swing);
        backHip.localRotation = Quaternion.Euler(0f, 0f, -34f * swing);
        float restingArm = carryingRod ? 42f - 7f * swing : -26f * swing;
        shoulder.localRotation = Quaternion.Euler(0f, 0f, Busy ? armAngle : heldBread != null ? 67f : restingArm);
        neck.localRotation = Quaternion.Euler(0f, 0f, chew);
        if (heldBread != null)
            heldBread.transform.position = hand.position + new Vector3(facing * 0.045f, 0f, -0.05f);
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
