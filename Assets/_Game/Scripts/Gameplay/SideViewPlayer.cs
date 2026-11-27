using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class SideViewPlayer : MonoBehaviour
{
    [SerializeField] private float walkSpeed = 3.5f;

    private Rigidbody body;
    private CellMotion cellMotion;
    private float moveInput;

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
        body.constraints |= RigidbodyConstraints.FreezePositionZ
            | RigidbodyConstraints.FreezeRotation;
    }

    private void Update()
    {
        moveInput = CanReadInput ? Input.GetAxisRaw("Horizontal") : 0f;
    }

    private void FixedUpdate()
    {
        Vector3 velocity = body.linearVelocity;
        velocity.x = CanReadInput && (cellMotion == null || !cellMotion.Busy) ? moveInput * walkSpeed : 0f;
        body.linearVelocity = velocity;
    }
}
