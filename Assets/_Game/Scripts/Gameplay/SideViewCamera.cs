using UnityEngine;

public sealed class SideViewCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.22f;

    private float horizontalVelocity;

    private void LateUpdate()
    {
        if (target == null || Time.deltaTime <= 0f)
            return;

        Vector3 position = transform.position;
        position.x = Mathf.SmoothDamp(
            position.x, target.position.x, ref horizontalVelocity, smoothTime);
        transform.position = position;
    }
}