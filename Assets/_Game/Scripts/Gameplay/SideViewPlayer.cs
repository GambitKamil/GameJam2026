using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class SideViewPlayer : MonoBehaviour
{
    [SerializeField] private float walkSpeed = 3.5f;

    private Rigidbody body;
    private float moveInput;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.constraints |= RigidbodyConstraints.FreezePositionZ
            | RigidbodyConstraints.FreezeRotation;
    }

    private void Update()
    {
        moveInput = Input.GetAxisRaw("Horizontal");
    }

    private void FixedUpdate()
    {
        Vector3 velocity = body.linearVelocity;
        velocity.x = moveInput * walkSpeed;
        body.linearVelocity = velocity;
    }
}