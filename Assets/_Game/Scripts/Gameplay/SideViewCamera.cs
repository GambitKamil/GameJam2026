using UnityEngine;

[DefaultExecutionOrder(200)]
public sealed class SideViewCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.22f;
    [SerializeField] private bool constrainHorizontalPosition;
    [SerializeField] private Vector2 horizontalLimits = new Vector2(-0.7f, 0.7f);

    [SerializeField] private float horizontalOffset;
    [SerializeField, Min(0f)] private float lookAhead;
    [SerializeField, Min(0.01f)] private float lookAheadTurnTime = 0.08f;

    [SerializeField] private Transform framingSubject;

    // World-space margins at the movement lane. They keep the bunk, rod and near bars in the opening frame.
    [SerializeField, Min(0.1f)] private float openingLeftMargin = 1.5f;
    [SerializeField, Min(0.1f)] private float openingCorridorFragment = 0.55f;
    // Extra right-side space as a fraction of the closed frame's visible width.
    [SerializeField, Range(0f, 0.25f)] private float gateRevealFraction = 0.125f;
    [SerializeField, Min(0.05f)] private float gateRevealDuration = 0.85f;

    private const float ReferenceAspect = 16f / 9f;
    private Camera view;
    private float baseOrthographicSize;
    private float zoomVelocity;
    private bool usesSubjectFraming;
    private float horizontalVelocity;
    private float verticalVelocity;
    private float direction = 1f;
    private float lookAheadDirection = 1f;
    private float lookAheadVelocity;
    private Rigidbody targetBody;
    private CellMotion openingMotion;
    private Transform cellGate;
    private float openingLeft;
    private float openingRight;
    private float authoredY;
    private Vector3 followPosition;
    private float followSize;
    private bool gateOpened;
    private float revealElapsed;

    private void Awake()
    {
        if (target != null)
            targetBody = target.GetComponent<Rigidbody>();
        view = GetComponent<Camera>();
        if (view == null)
            return;

        followPosition = transform.position;
        authoredY = followPosition.y;
        baseOrthographicSize = followSize = view.orthographicSize;
        usesSubjectFraming = target != null && framingSubject != null && view.orthographic;
        if (target != null && openingMotion != null && cellGate != null && view.orthographic)
        {
            openingLeft = target.position.x - openingLeftMargin;
            openingRight = cellGate.position.x + openingCorridorFragment;
            ApplyOpeningFrame();
        }
        else if (usesSubjectFraming)
        {
            followPosition.x = FramingX(followPosition.x, out followSize);
            ApplyFrame();
        }
    }

    public void ConfigureCellOpening(CellMotion motion, Transform gate)
    {
        openingMotion = motion;
        cellGate = gate;
    }

    public void StopFramingSubject()
    {
        framingSubject = null;
    }

    public void RevealBehindGate()
    {
        gateOpened = true;
    }

    private void LateUpdate()
    {
        if (target == null || view == null)
            return;

        if (openingMotion != null && cellGate != null && openingMotion.WakingUp && view.orthographic)
        {
            // Compose from the current viewport immediately, even if its aspect changed after Awake.
            ApplyOpeningFrame();
            return;
        }

        float delta = Time.deltaTime;
        if (delta <= 0f)
            return;

        if (lookAhead > 0f && targetBody != null && Mathf.Abs(targetBody.linearVelocity.x) > 0.15f)
            direction = Mathf.Sign(targetBody.linearVelocity.x);

        lookAheadDirection = Mathf.SmoothDamp(lookAheadDirection, direction, ref lookAheadVelocity, lookAheadTurnTime);
        float targetX = target.position.x + lookAhead * lookAheadDirection;
        if (constrainHorizontalPosition)
            targetX = Mathf.Clamp(targetX, horizontalLimits.x, horizontalLimits.y);
        targetX += horizontalOffset;

        float targetSize = FollowOrthographicSize();
        if (usesSubjectFraming)
            targetX = FramingX(targetX, out targetSize);
        float targetY = openingMotion != null && view.orthographic ? FloorFramingY(targetSize) : authoredY;

        if (gateOpened && constrainHorizontalPosition && view.orthographic)
            targetX = Mathf.Min(targetX, LastFollowX(targetSize));

        followPosition.x = Mathf.SmoothDamp(followPosition.x, targetX, ref horizontalVelocity, smoothTime);
        followPosition.y = Mathf.SmoothDamp(followPosition.y, targetY, ref verticalVelocity, smoothTime);
        followSize = Mathf.SmoothDamp(followSize, targetSize, ref zoomVelocity, smoothTime);
        if (gateOpened)
            revealElapsed = Mathf.Min(revealElapsed + delta, gateRevealDuration);
        ApplyFrame();
    }

    private void ApplyOpeningFrame()
    {
        Vector3 forward = transform.forward;
        float laneCenter = (openingLeft + openingRight) * 0.5f;
        followPosition.x = laneCenter + followPosition.z * forward.x / forward.z;
        followSize = (openingRight - openingLeft) * 0.5f / LaneHalfWidth(1f, view.aspect);
        followPosition.y = FloorFramingY(followSize);
        horizontalVelocity = verticalVelocity = zoomVelocity = 0f;
        ApplyFrame();
    }

    private void ApplyFrame()
    {
        Vector3 position = followPosition;
        float size = followSize;
        if (gateOpened && view.orthographic)
        {
            float reveal = gateRevealFraction * Mathf.SmoothStep(0f, 1f, revealElapsed / gateRevealDuration);
            float halfWidth = LaneHalfWidth(size, view.aspect);
            if (constrainHorizontalPosition && reveal > 0f)
            {
                // Give the extra space back smoothly as the old right-side camera limit approaches.
                float clearance = Mathf.Max(0f, LastFollowX(size) - position.x);
                reveal *= Mathf.SmoothStep(0f, 1f, clearance / (halfWidth * reveal * 4f));
            }

            // The zoom adds the same width on both sides. This shift pins the left edge.
            position.x += halfWidth * reveal;
            size *= 1f + reveal;
        }

        transform.position = position;
        view.orthographicSize = size;
    }

    private float FollowOrthographicSize()
    {
        if (openingMotion == null || !view.orthographic)
            return baseOrthographicSize;

        // The service bay and the right-hand camera limit are laid out in world width.
        return baseOrthographicSize * ReferenceAspect / Mathf.Max(view.aspect, 0.1f);
    }

    private float FloorFramingY(float size)
    {
        // Lower the center when a wide viewport shortens the shot vertically.
        Vector3 forward = transform.forward;
        float laneUp = transform.up.y - forward.y * transform.up.z / forward.z;
        return authoredY - Mathf.Max(0f, baseOrthographicSize - size) * laneUp;
    }

    private float LastFollowX(float size)
    {
        float wideDisplayAllowance = LaneHalfWidth(baseOrthographicSize, ReferenceAspect) - LaneHalfWidth(size, view.aspect);
        return horizontalLimits.y + horizontalOffset + Mathf.Min(0f, wideDisplayAllowance);
    }

    private float LaneHalfWidth(float size, float aspect)
    {
        Vector3 right = transform.right;
        Vector3 forward = transform.forward;
        float laneRight = right.x - forward.x / forward.z * right.z;
        return size * Mathf.Max(0.1f, aspect) * Mathf.Abs(laneRight);
    }

    private float FramingX(float x, out float size)
    {
        size = FollowOrthographicSize();
        if (framingSubject == null)
            return x;

        Vector3 center = followPosition;
        center.x = x;
        Vector3 right = transform.right;
        float playerX = Vector3.Dot(target.position - center, right);
        float subjectX = Vector3.Dot(framingSubject.position - center, right);
        float left = Mathf.Min(playerX, subjectX);
        float far = Mathf.Max(playerX, subjectX);
        float aspect = Mathf.Max(view.aspect, 0.1f);
        const float margin = 0.88f;
        size = Mathf.Max(size, ((far - left) * 0.5f + margin) / aspect);
        float usable = size * aspect - margin;
        float shift = Mathf.Clamp(0f, far - usable, left + usable);
        return x + shift / right.x;
    }
}
