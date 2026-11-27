using UnityEngine;

public sealed class SideViewCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.22f;
    [SerializeField] private bool constrainHorizontalPosition;
    [SerializeField] private Vector2 horizontalLimits = new Vector2(-0.7f, 0.7f);

    [SerializeField] private float horizontalOffset;
    [SerializeField, Min(0f)] private float lookAhead;

    private float horizontalVelocity;
    private float direction = 1f;
    private Rigidbody targetBody;

    private void Awake()
    {
        if (target != null)
            targetBody = target.GetComponent<Rigidbody>();
    }

    private void LateUpdate()
    {
        if (target == null || Time.deltaTime <= 0f)
            return;

        if (lookAhead > 0f && targetBody != null && Mathf.Abs(targetBody.linearVelocity.x) > 0.15f)
            direction = Mathf.Sign(targetBody.linearVelocity.x);

        float targetX = target.position.x + lookAhead * direction;
        if (constrainHorizontalPosition)
            targetX = Mathf.Clamp(targetX, horizontalLimits.x, horizontalLimits.y);
        targetX += horizontalOffset;
        Vector3 position = transform.position;
        position.x = Mathf.SmoothDamp(
            position.x, targetX, ref horizontalVelocity, smoothTime);
        transform.position = position;
    }
}
