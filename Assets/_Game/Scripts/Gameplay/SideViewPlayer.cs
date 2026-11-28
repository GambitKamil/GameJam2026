using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class SideViewPlayer : MonoBehaviour
{
    [SerializeField] private float walkSpeed = 3.5f;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField, Range(0f, 75f)] private float groundSlopeLimit = 45f;
    [SerializeField, Range(0f, 0.12f)] private float groundSnapDistance = 0.065f;

    private readonly RaycastHit[] supportHits = new RaycastHit[12];
    private Rigidbody body;
    private CapsuleCollider capsule;
    private CellMotion cellMotion;
    private float moveInput;
    public bool Crouching { get; private set; }
    internal bool IsGrounded { get; private set; }
    internal float WalkSpeed => walkSpeed;

    internal static bool CanReadInput
    {
        get
        {
            var game = GameManager.Instance;
            return Time.timeScale > 0f && (game == null || game.IsInputEnabled);
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        cellMotion = GetComponent<CellMotion>();
        if (cellMotion != null)
        {
            capsule = GetComponent<CapsuleCollider>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }
        body.constraints |= RigidbodyConstraints.FreezePositionZ
            | RigidbodyConstraints.FreezeRotation;
    }

    private void Update()
    {
        bool canRead = CanReadInput;
        moveInput = canRead ? Input.GetAxisRaw("Horizontal") : 0f;
        Crouching = cellMotion != null && canRead && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
    }

    private void FixedUpdate()
    {
        Vector3 velocity = body.linearVelocity;
        bool movingAllowed = CanReadInput && (cellMotion == null || !cellMotion.Busy);
        float target = movingAllowed ? moveInput * walkSpeed * (Crouching ? 0.44f : 1f) : 0f;
        if (cellMotion == null || !movingAllowed)
            velocity.x = target;
        else
        {
            bool braking = Mathf.Abs(target) < 0.01f || velocity.x * target < 0f;
            velocity.x = Mathf.MoveTowards(velocity.x, target, (braking ? 24f : 15f) * Time.fixedDeltaTime);
        }
        UpdateGrounding(ref velocity);
        body.linearVelocity = velocity;
    }

    private void UpdateGrounding(ref Vector3 velocity)
    {
        if (capsule == null || capsule.direction != 1)
            return;

        Vector3 foot = body.position + body.rotation * (capsule.center - Vector3.up * (capsule.height * 0.5f));
        const float contactAllowance = 0.025f;
        bool wasGrounded = IsGrounded;
        float below = wasGrounded ? Mathf.Max(contactAllowance, groundSnapDistance) : contactAllowance;
        RaycastHit hit = default;
        IsGrounded = velocity.y <= 0.20f && TryGetSupport(foot, 0.10f, below, out hit);
        if (!IsGrounded)
            return;

        float gap = foot.y - hit.point.y;
        if (wasGrounded && gap > contactAllowance)
        {
            // Let PhysX cover a small downward step before the next contact instead of moving the Transform.
            velocity.y = Mathf.Min(velocity.y, -gap / Time.fixedDeltaTime);
        }
    }

    internal bool TryGetSupport(Vector3 sole, float above, float below, out RaycastHit closest)
    {
        int count = Physics.RaycastNonAlloc(sole + Vector3.up * above, Vector3.down, supportHits,
            above + below, groundLayers, QueryTriggerInteraction.Ignore);
        float minDistance = float.PositiveInfinity;
        float minimumUp = Mathf.Cos(groundSlopeLimit * Mathf.Deg2Rad);
        closest = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = supportHits[i];
            Collider surface = hit.collider;
            if (surface == null || hit.rigidbody == body || hit.normal.y < minimumUp
                || Physics.GetIgnoreLayerCollision(gameObject.layer, surface.gameObject.layer)
                || (capsule != null && Physics.GetIgnoreCollision(capsule, surface)) || hit.distance >= minDistance)
                continue;

            closest = hit;
            minDistance = hit.distance;
        }

        return closest.collider != null;
    }
}
