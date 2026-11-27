using UnityEngine;

public sealed class SideViewCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.22f;
    [SerializeField] private bool constrainHorizontalPosition;
    [SerializeField] private Vector2 horizontalLimits = new Vector2(-0.7f, 0.7f);

    private float horizontalVelocity;

    private void LateUpdate()
    {
        if (target == null || Time.deltaTime <= 0f)
            return;

        float targetX = constrainHorizontalPosition
            ? Mathf.Clamp(target.position.x, horizontalLimits.x, horizontalLimits.y)
            : target.position.x;
        Vector3 position = transform.position;
        position.x = Mathf.SmoothDamp(
            position.x, targetX, ref horizontalVelocity, smoothTime);
        transform.position = position;
    }
}
