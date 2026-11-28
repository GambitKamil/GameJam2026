using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class HangingColdGoods : MonoBehaviour
{
    private Quaternion restRotation;
    private float angle;
    private float swingSpeed;
    private float lastContact = -1f;

    private void Awake()
    {
        restRotation = transform.localRotation;
    }

    private void OnTriggerEnter(Collider other)
    {
        Rigidbody body = other.attachedRigidbody;
        if (body == null || !body.TryGetComponent<SideViewPlayer>(out _) || Time.time - lastContact < .32f)
            return;

        float speed = body.linearVelocity.x;
        if (Mathf.Abs(speed) < .12f)
            return;

        lastContact = Time.time;
        swingSpeed = Mathf.Clamp(swingSpeed + speed * 46f, -150f, 150f);
    }

    private void FixedUpdate()
    {
        if (Mathf.Abs(angle) < .03f && Mathf.Abs(swingSpeed) < .08f)
        {
            if (angle != 0f || swingSpeed != 0f)
                transform.localRotation = restRotation;
            angle = swingSpeed = 0f;
            return;
        }

        float step = Time.fixedDeltaTime;
        swingSpeed -= (24f * angle + 3.5f * swingSpeed) * step;
        angle += swingSpeed * step;
        transform.localRotation = restRotation * Quaternion.Euler(0f, angle * .14f, angle);
    }
}
