using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class SideViewPlayer : MonoBehaviour
{
    [SerializeField] private float walkSpeed = 3.5f;

    private Rigidbody body;
    private CellMotion cellMotion;
    private float moveInput;
    public bool Crouching { get; private set; }

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
        body.linearVelocity = velocity;
    }
}
