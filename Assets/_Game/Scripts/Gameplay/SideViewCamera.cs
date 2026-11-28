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

    private Camera view;
    private float baseOrthographicSize;
    private float zoomVelocity;
    private bool usesSubjectFraming;
    private float horizontalVelocity;
    private float direction = 1f;
    private float lookAheadDirection = 1f;
    private float lookAheadVelocity;
    private Rigidbody targetBody;

    private void Awake()
    {
        if (target != null)
            targetBody = target.GetComponent<Rigidbody>();
        view = GetComponent<Camera>();
        if (view == null)
            return;
        baseOrthographicSize = view.orthographicSize;
        usesSubjectFraming = target != null && framingSubject != null && view.orthographic;
        if (usesSubjectFraming)
        {
            Vector3 position = transform.position;
            position.x = FramingX(position.x, out float size);
            transform.position = position;
            view.orthographicSize = size;
        }
    }

    public void StopFramingSubject()
    {
        framingSubject = null;
    }

    private void LateUpdate()
    {
        if (target == null || Time.deltaTime <= 0f)
            return;

        if (lookAhead > 0f && targetBody != null && Mathf.Abs(targetBody.linearVelocity.x) > 0.15f)
            direction = Mathf.Sign(targetBody.linearVelocity.x);

        lookAheadDirection = Mathf.SmoothDamp(lookAheadDirection, direction, ref lookAheadVelocity, lookAheadTurnTime);
        float targetX = target.position.x + lookAhead * lookAheadDirection;
        if (constrainHorizontalPosition)
            targetX = Mathf.Clamp(targetX, horizontalLimits.x, horizontalLimits.y);
        targetX += horizontalOffset;
        if (usesSubjectFraming)
        {
            targetX = FramingX(targetX, out float size);
            view.orthographicSize = Mathf.SmoothDamp(view.orthographicSize, size, ref zoomVelocity, smoothTime);
        }
        Vector3 position = transform.position;
        position.x = Mathf.SmoothDamp(
            position.x, targetX, ref horizontalVelocity, smoothTime);
        transform.position = position;
    }

    private float FramingX(float x, out float size)
    {
        size = baseOrthographicSize;
        if (framingSubject == null)
            return x;

        Vector3 center = transform.position;
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
