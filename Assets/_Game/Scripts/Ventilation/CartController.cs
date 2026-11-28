using UnityEngine;

// Old four-wheeled freight cart. Kinematic body that moves along the side-view lane, sits on whatever
// floor is under its wheels (flat, ramp or the lift platform) and tilts with it.
// Left alone on a slope without the brake it rolls back down under gravity.
[RequireComponent(typeof(Rigidbody))]
public sealed class CartController : MonoBehaviour
{
    [Header("Travel")]
    [SerializeField] private float minX = 1f;
    [SerializeField] private float maxX = 36f;
    [SerializeField, Min(0.1f)] private float pushSpeed = 2.3f;
    [SerializeField, Min(0.1f)] private float uphillSpeed = 1.5f;
    [SerializeField, Min(0f)] private float rollingFriction = 0.55f;
    [SerializeField, Min(0f)] private float gravityScale = 1f;
    [SerializeField, Min(0.2f)] private float wheelBase = 1.1f;
    [SerializeField, Min(0.3f)] private float halfLength = 0.78f;
    [SerializeField, Min(0.3f)] private float handleHeight = 0.95f;
    [SerializeField] private LayerMask groundLayers = Physics.DefaultRaycastLayers;

    [Header("Parts")]
    [SerializeField] private Transform[] wheels;
    [SerializeField, Min(0.02f)] private float wheelRadius = 0.13f;
    [SerializeField] private Transform brakePedal;
    [SerializeField] private Light brakeLamp;

    [Header("Sound")]
    [SerializeField] private AudioSource rolling;
    [SerializeField] private AudioSource effects;
    [SerializeField, Range(0f, 1f)] private float rollingVolume = 0.35f;

    public bool Braked { get; private set; }
    public float Velocity { get; private set; }
    public float X => CurrentPosition.x;
    public float HalfLength => halfLength;
    public float PushSpeed => pushSpeed;
    public float UphillSpeed => uphillSpeed;
    public float MinX => minX;
    public float MaxX => maxX;
    // Positive when the floor rises toward +X.
    public float Slope { get; private set; }
    public bool Held { get; set; }

    private Rigidbody body;
    private Collider[] ownColliders;
    private Collider[] ignored = new Collider[0];
    private readonly RaycastHit[] hits = new RaycastHit[12];
    private Quaternion[] wheelRest;
    private float wheelTurn;
    private float lastX;
    private float squeakCooldown;
    private Quaternion pedalRest;
    private Vector3 target;
    private bool hasTarget;
    private bool teleport;
    private float brakeLampIntensity;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        ownColliders = GetComponentsInChildren<Collider>(true);
        wheelRest = new Quaternion[wheels.Length];
        for (int i = 0; i < wheels.Length; i++)
            wheelRest[i] = wheels[i].localRotation;
        if (brakePedal != null) pedalRest = brakePedal.localRotation;
        if (brakeLamp != null)
        {
            brakeLampIntensity = brakeLamp.intensity;
            brakeLamp.intensity = 0f;
        }
        lastX = transform.position.x;
        teleport = true;
        Settle(transform.position.x);
        teleport = false;
        hasTarget = true;
        if (rolling != null && rolling.clip != null)
        {
            rolling.loop = true;
            rolling.volume = 0f;
            rolling.Play();
        }
    }

    // Colliders the ground probe should look through (the prisoner).
    public void IgnoreForGround(Collider[] colliders) => ignored = colliders ?? new Collider[0];

    public Vector3 Handle(int side)
    {
        // side = -1: player on the left of the cart.
        Vector3 local = new Vector3(side * (halfLength + 0.02f), handleHeight, -0.25f);
        return transform.TransformPoint(local);
    }

    public Vector3 Seat => transform.TransformPoint(new Vector3(0f, 0.45f, 0f));

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (!Held)
        {
            if (Braked)
                Velocity = 0f;
            else
            {
                // Rolling on an incline: a = -g sin(theta) cos(theta) along X, minus rolling friction.
                float theta = Mathf.Atan(Slope);
                float pull = -Physics.gravity.magnitude * gravityScale * Mathf.Sin(theta) * Mathf.Cos(theta);
                Velocity += pull * dt;
                float friction = rollingFriction * dt;
                Velocity = Mathf.Abs(Velocity) <= friction ? 0f : Velocity - Mathf.Sign(Velocity) * friction;
            }

            if (Mathf.Abs(Velocity) > 0f)
            {
                float x = CurrentPosition.x + Velocity * dt;
                float clamped = Mathf.Clamp(x, minX, maxX);
                if (!Mathf.Approximately(x, clamped))
                    Velocity = 0f;
                Settle(clamped);
            }
            else
            {
                // Still follow the floor vertically (the lift platform moves under it).
                Settle(CurrentPosition.x);
            }
        }

        float moved = CurrentPosition.x - lastX;
        lastX = CurrentPosition.x;
        if (Held)
            Velocity = moved / dt;
        SpinWheels(moved);
        UpdateSound(Mathf.Abs(moved) / dt, dt);
    }

    // Move toward x while the prisoner pushes or pulls. Returns where the cart actually is.
    public float DriveTo(float x)
    {
        if (Braked)
            return CurrentPosition.x;
        float clamped = Mathf.Clamp(x, minX, maxX);
        Settle(clamped);
        return clamped;
    }

    // Snap height and tilt to the floor at x.
    private void Settle(float x)
    {
        float y = CurrentPosition.y;
        float back = GroundAt(x - wheelBase * 0.5f, y);
        float front = GroundAt(x + wheelBase * 0.5f, y);
        float center = Mathf.Max((back + front) * 0.5f, GroundAt(x, y));
        Slope = (front - back) / wheelBase;
        float tilt = Mathf.Atan(Slope) * Mathf.Rad2Deg;
        Vector3 position = new Vector3(x, center, transform.position.z);
        Quaternion rotation = Quaternion.Euler(0f, 0f, tilt);
        if (body != null && Application.isPlaying && !teleport)
        {
            body.MovePosition(position);
            body.MoveRotation(rotation);
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
            }
        }
        target = position;
    }

    // Where the cart is (or is being moved to this physics step).
    public Vector3 CurrentPosition => Application.isPlaying && hasTarget ? target : transform.position;

    private float GroundAt(float x, float currentY)
    {
        Vector3 origin = new Vector3(x, currentY + 1.4f, transform.position.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 4f, groundLayers, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i].collider;
            if (Contains(ownColliders, c) || Contains(ignored, c) || hits[i].normal.y < 0.6f)
                continue;
            best = Mathf.Max(best, hits[i].point.y);
        }
        return float.IsNegativeInfinity(best) ? currentY : best;
    }

    private static bool Contains(Collider[] list, Collider c)
    {
        foreach (Collider item in list)
            if (item == c) return true;
        return false;
    }

    private void SpinWheels(float moved)
    {
        wheelTurn += moved / wheelRadius * Mathf.Rad2Deg;
        for (int i = 0; i < wheels.Length; i++)
            wheels[i].localRotation = wheelRest[i] * Quaternion.Euler(0f, 0f, -wheelTurn);
    }

    private void UpdateSound(float speed, float dt)
    {
        if (rolling != null)
        {
            float target = Mathf.Clamp01(speed / pushSpeed) * rollingVolume;
            rolling.volume = Mathf.MoveTowards(rolling.volume, target, dt * 3f);
            rolling.pitch = 0.75f + Mathf.Clamp01(speed / pushSpeed) * 0.35f;
        }

        squeakCooldown -= dt;
        if (effects != null && speed > 0.4f && squeakCooldown <= 0f && Random.value < dt * 0.6f)
        {
            effects.pitch = Random.Range(0.85f, 1.15f);
            effects.PlayOneShot(VentilationAudio.Get(VentilationAudio.Cue.Squeak), 0.8f);
            squeakCooldown = 1.5f;
        }
    }

    public void ToggleBrake() => SetBrake(!Braked);

    public void SetBrake(bool on)
    {
        if (on == Braked)
            return;
        Braked = on;
        if (on) Velocity = 0f;
        if (brakePedal != null)
            brakePedal.localRotation = pedalRest * Quaternion.Euler(0f, 0f, on ? -35f : 0f);
        if (brakeLamp != null)
            brakeLamp.intensity = on ? brakeLampIntensity : 0f;
        if (effects != null)
        {
            effects.pitch = on ? 0.9f : 1.15f;
            effects.PlayOneShot(VentilationAudio.Get(VentilationAudio.Cue.Click), 1f);
        }
    }

    public void Restore(float x, float y, bool braked)
    {
        Velocity = 0f;
        Held = false;
        SetBrake(braked);
        teleport = true;
        transform.position = new Vector3(x, y + 0.05f, transform.position.z);
        target = transform.position;
        Settle(x);
        teleport = false;
        lastX = x;
    }
}
