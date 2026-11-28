using UnityEngine;

// Ceiling CCTV camera that slowly sweeps from side to side in the side-view plane.
// It sees the prisoner only when the shaft lamps are lit, he is inside the view cone and range,
// nothing solid is between them and he is not hidden in the cart. Seen for detectSeconds -> alarm.
public sealed class SecurityCameraController : MonoBehaviour
{
    [Header("Sweep")]
    [Tooltip("Rotates around Z. 0 deg looks straight down, positive turns toward +X.")]
    [SerializeField] private Transform pivot;
    [SerializeField] private float centerAngle;
    [SerializeField, Range(0f, 85f)] private float sweepAngle = 40f;
    [SerializeField, Min(1f)] private float sweepSeconds = 9f;
    [SerializeField, Min(0f)] private float sweepPhase;

    [Header("Detection")]
    [SerializeField, Range(3f, 45f)] private float halfFieldOfView = 16f;
    [SerializeField, Min(1f)] private float range = 8f;
    [Tooltip("Range multiplier while the prisoner crouches.")]
    [SerializeField, Range(0.3f, 1f)] private float crouchRange = 0.72f;
    [SerializeField, Min(0.1f)] private float detectSeconds = 1.5f;
    [Tooltip("How fast suspicion drains per second while unseen. Low values make repeated sweeps add up.")]
    [SerializeField, Min(0.01f)] private float coolDownRate = 0.2f;
    [SerializeField] private LayerMask occluders = Physics.DefaultRaycastLayers;

    [Header("Links")]
    [SerializeField] private FlickeringLightController lights;
    [SerializeField] private SideViewPlayer player;
    [SerializeField] private CartHidingController hiding;
    [SerializeField] private AlarmController alarm;
    [SerializeField] private DetectionMeter meter;

    [Header("Look")]
    [SerializeField] private Renderer viewCone;
    [SerializeField] private Light indicator;
    [SerializeField] private Renderer indicatorLens;
    [SerializeField] private Color coneColor = new Color(0.42f, 0.5f, 0.58f, 0.12f);
    [SerializeField] private Color spottedColor = new Color(0.8f, 0.12f, 0.08f, 0.2f);

    public float Progress { get; private set; }
    public bool SeesPlayer { get; private set; }
    public bool Detecting { get; set; } = true;

    private Rigidbody body;
    private Collider[] playerColliders;
    private MaterialPropertyBlock block;
    private float clock;
    private float angle;
    private Transform tracked;
    private float indicatorIntensity;
    private readonly RaycastHit[] hits = new RaycastHit[8];

    public Vector3 Eye => pivot.position;
    public Vector3 Forward => Quaternion.Euler(0f, 0f, angle) * Vector3.down;

    private void Awake()
    {
        body = player.GetComponent<Rigidbody>();
        playerColliders = player.GetComponentsInChildren<Collider>(true);
        block = new MaterialPropertyBlock();
        clock = sweepPhase * sweepSeconds;
        if (indicator != null)
        {
            indicatorIntensity = indicator.intensity;
            indicator.intensity = 0f;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        if (tracked != null)
        {
            // Cinematic: swing round to stare at a target (the rising lift).
            Vector3 to = tracked.position - pivot.position;
            float wanted = Mathf.Atan2(to.x, -to.y) * Mathf.Rad2Deg;
            angle = Mathf.MoveTowardsAngle(angle, wanted, 35f * dt);
        }
        else
        {
            clock += dt;
            angle = centerAngle + sweepAngle * Mathf.Sin(clock * 2f * Mathf.PI / sweepSeconds);
        }
        pivot.localRotation = Quaternion.Euler(0f, 0f, angle);

        SeesPlayer = Detecting && CanSeePlayer();
        if (SeesPlayer)
            Progress = Mathf.Min(1f, Progress + dt / detectSeconds);
        else
            Progress = Mathf.Max(0f, Progress - dt * coolDownRate);

        if (meter != null)
            meter.Level = Detecting ? Progress : 0f;

        if (Detecting && Progress >= 1f && alarm != null)
        {
            if (meter != null) meter.Spotted();
            alarm.TriggerGameplayAlarm(this);
        }

        UpdateLook();
    }

    private bool CanSeePlayer()
    {
        if (lights != null && !lights.IsLit)
            return false;
        if (hiding != null && hiding.IsHidden)
            return false;
        if (!player.gameObject.activeInHierarchy)
            return false;

        bool crouching = player.Crouching;
        Vector3 target = body.position + Vector3.up * (crouching ? -0.05f : 0.35f);
        Vector3 to = target - pivot.position;
        float distance = to.magnitude;
        if (distance > range * (crouching ? crouchRange : 1f))
            return false;
        if (Vector3.Angle(Forward, to) > halfFieldOfView)
            return false;

        // Anything solid in between that is not the prisoner blocks the view.
        int count = Physics.RaycastNonAlloc(pivot.position, to / distance, hits, distance - 0.05f,
            occluders, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!IsPlayer(hits[i].collider))
                return false;
        }
        return true;
    }

    private bool IsPlayer(Collider collider)
    {
        foreach (Collider own in playerColliders)
            if (own == collider) return true;
        return false;
    }

    private void UpdateLook()
    {
        bool alert = Detecting && Progress > 0.01f;
        if (viewCone != null)
        {
            float lit = lights == null ? 1f : Mathf.Clamp01(lights.Level);
            Color tint = Color.Lerp(coneColor, spottedColor, alert ? 0.4f + 0.6f * Progress : 0f);
            tint.a *= Detecting ? Mathf.Lerp(0.25f, 1f, lit) : 0.5f;
            block.Clear();
            block.SetColor("_Color", tint);
            viewCone.SetPropertyBlock(block);
        }

        // Red tally light: off while idle, on while the camera has him, quickening as the alarm nears.
        float blink = alert ? (Progress > 0.6f ? Mathf.PingPong(Time.time * 4f, 1f) : 1f) : 0f;
        if (indicator != null)
            indicator.intensity = indicatorIntensity * blink;
        if (indicatorLens != null)
        {
            block.Clear();
            block.SetColor("_EmissionColor", new Color(1f, 0.12f, 0.06f) * (0.05f + 2.2f * blink));
            indicatorLens.SetPropertyBlock(block);
        }
    }

    public void ResetDetection()
    {
        Progress = 0f;
        SeesPlayer = false;
        if (meter != null) meter.Level = 0f;
    }

    // Final shot: stop detecting and turn toward the target.
    public void Track(Transform target)
    {
        tracked = target;
        Detecting = false;
        ResetDetection();
    }
}
