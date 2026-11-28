using UnityEngine;

// A maintenance cart the prisoner pushes along the corridor. It shields a crouching player
// from the patrol torch and can hold the pressure plate down.
[RequireComponent(typeof(BoxCollider))]
public sealed class ServiceCart : MonoBehaviour
{
    [SerializeField] private Transform[] wheels;
    [SerializeField] private float minX = 6f;
    [SerializeField] private float maxX = 30f;
    [SerializeField] private float wheelRadius = 0.07f;
    [SerializeField] private float handleHeight = 0.86f;

    private BoxCollider box;
    private Vector3 home;
    private float lastX;
    private float rattle;

    public float Speed { get; private set; }
    public float X => transform.position.x;
    public float MinX => minX;
    public float MaxX => maxX;
    public float HalfLength => box.size.x * 0.5f * Mathf.Abs(transform.lossyScale.x);
    public Vector3 Home => home;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        home = transform.position;
        lastX = home.x;
    }

    public void IgnorePlayer(Collider playerCollider)
    {
        if (playerCollider != null) Physics.IgnoreCollision(playerCollider, box, true);
    }

    // The handle on the side that faces the player.
    public Vector3 Handle(int side)
    {
        Vector3 p = transform.position;
        return new Vector3(p.x - side * (HalfLength - 0.04f), p.y + handleHeight, p.z - 0.18f);
    }

    public float ClampX(float x) => Mathf.Clamp(x, minX, maxX);

    public void MoveTo(float x)
    {
        Vector3 p = transform.position;
        p.x = ClampX(x);
        transform.position = p;
    }

    public void ResetTo(float x)
    {
        MoveTo(x);
        lastX = transform.position.x;
        Speed = 0f;
    }

    private void LateUpdate()
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float dx = transform.position.x - lastX;
        lastX = transform.position.x;
        Speed = Mathf.Lerp(Speed, Mathf.Abs(dx) / dt, 1f - Mathf.Exp(-dt * 12f));
        if (wheels != null && Mathf.Abs(dx) > 0f)
        {
            float degrees = -dx / wheelRadius * Mathf.Rad2Deg;
            foreach (Transform wheel in wheels)
                if (wheel != null) wheel.Rotate(0f, 0f, degrees, Space.World);
        }

        // A loose cart shivers a little while it rolls.
        rattle = Speed > 0.2f ? rattle + dt * Speed * 9f : 0f;
        transform.localRotation = Quaternion.Euler(0f, transform.localEulerAngles.y,
            Speed > 0.2f ? Mathf.Sin(rattle) * 0.6f : 0f);
    }
}
