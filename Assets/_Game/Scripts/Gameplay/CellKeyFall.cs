using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
internal sealed class CellKeyFall : MonoBehaviour
{
    // Keep a small gap between the rotating key and the visible wall face.
    private const float WallSkin = 0.008f;
    private const float ReleaseSpeed = 0.34f;

    private Renderer[] parts;
    private BoxCollider[] shapes;
    private Rigidbody body;
    private PhysicsMaterial impactMaterial;
    private float supportHeight;
    private float floorHeight;
    private bool hasFloor;
    private float wallLimit;

    public bool HitFloor { get; private set; }

    public Rigidbody Begin(float bounce, Vector3 spin, Renderer wallFace)
    {
        hasFloor = Physics.Raycast(transform.position, Vector3.down, out RaycastHit floor, 5f, ~0, QueryTriggerInteraction.Ignore);
        floorHeight = floor.point.y;
        parts = GetComponentsInChildren<MeshRenderer>();
        wallLimit = wallFace != null ? wallFace.bounds.min.z - WallSkin : float.PositiveInfinity;
        impactMaterial = new PhysicsMaterial("Key Impact")
        {
            dynamicFriction = 0.42f,
            staticFriction = 0.5f,
            bounciness = bounce,
            bounceCombine = PhysicsMaterialCombine.Maximum
        };

        // Use each piece of the authored key so the ring and teeth can hit the floor independently.
        shapes = new BoxCollider[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            Renderer part = parts[i];
            if (!part.enabled)
                continue;

            Bounds bounds = part.localBounds;
            var shape = part.gameObject.AddComponent<BoxCollider>();
            shapes[i] = shape;
            shape.center = bounds.center;
            shape.size = bounds.size;
            shape.contactOffset = 0.002f;
            shape.sharedMaterial = impactMaterial;
        }

        body = GetComponent<Rigidbody>();
        body.mass = 0.12f;
        body.linearDamping = 0.15f;
        body.angularDamping = 0.75f;
        body.solverIterations = 10;
        body.solverVelocityIterations = 4;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        // Release from the wall into the corridor with the same lateral hook alignment.
        body.constraints = RigidbodyConstraints.FreezePositionX;
        if (wallFace == null)
            body.constraints |= RigidbodyConstraints.FreezePositionZ;
        body.linearVelocity = wallFace == null ? Vector3.zero : Vector3.back * ReleaseSpeed;
        body.angularVelocity = transform.TransformDirection(spin);
        return body;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (HitFloor)
            return;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (contact.normal.y < 0.6f)
                continue;

            MarkFloorImpact(contact.point.y);
            return;
        }
    }

    public void ResolveFloorContact()
    {
        ResolveWallContact();
        if (!hasFloor)
            return;

        float penetration = floorHeight - LowestPart();
        if (penetration < 0.0005f)
            return;

        // Rotation at impact can drive a tooth through the floor between physics steps.
        Vector3 corrected = body.position + Vector3.up * penetration;
        body.position = corrected;
        transform.position = corrected;
        Vector3 velocity = body.linearVelocity;
        if (velocity.y < 0f)
        {
            velocity.y = velocity.y < -0.5f ? -velocity.y * impactMaterial.bounciness : 0f;
            body.linearVelocity = velocity;
        }

        if (!HitFloor)
            MarkFloorImpact(floorHeight);
    }

    private void ResolveWallContact()
    {
        if (float.IsPositiveInfinity(wallLimit))
            return;

        float rear = float.NegativeInfinity;
        foreach (Renderer part in parts)
            if (part.enabled)
                rear = Mathf.Max(rear, part.bounds.max.z);

        float penetration = rear - wallLimit;
        if (penetration <= 0f)
            return;

        Vector3 corrected = transform.position - Vector3.forward * penetration;
        if (!body.isKinematic)
        {
            body.position = corrected;
            Vector3 velocity = body.linearVelocity;
            velocity.z = Mathf.Min(0f, velocity.z);
            body.linearVelocity = velocity;
        }
        transform.position = corrected;
    }

    private void MarkFloorImpact(float height)
    {
        HitFloor = true;
        supportHeight = height;
        body.angularDamping = 3.5f;
    }

    private float LowestPart()
    {
        float bottom = float.PositiveInfinity;
        foreach (Renderer part in parts)
            if (part.enabled)
                bottom = Mathf.Min(bottom, part.bounds.min.y);
        return bottom;
    }

    public IEnumerator Finish()
    {
        Vector3 position = body.position;
        Quaternion rotation = body.rotation;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
        body.detectCollisions = false;
        foreach (BoxCollider shape in shapes)
            if (shape != null)
                shape.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (!HitFloor)
            yield break;

        Vector3 normal = rotation * Vector3.forward;
        Vector3 faceUp = Vector3.Dot(normal, Vector3.up) >= 0f ? Vector3.up : Vector3.down;
        Quaternion flat = Quaternion.FromToRotation(normal, faceUp) * rotation;
        float gap = Mathf.Max(0f, LowestPart() - supportHeight);
        float duration = Mathf.Min(Quaternion.Angle(rotation, flat) / 360f, 0.22f);
        // The narrow edge can hold briefly during the bounce, then the key tips onto its face.
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            PoseOnFloor(position, Quaternion.Slerp(rotation, flat, t), Mathf.Lerp(gap, 0f, t));
            yield return null;
        }

        PoseOnFloor(position, flat, 0f);
    }

    private void PoseOnFloor(Vector3 position, Quaternion rotation, float gap)
    {
        transform.SetPositionAndRotation(position, rotation);
        transform.position += Vector3.up * (supportHeight + gap - LowestPart());
        ResolveWallContact();
    }

    private void OnDestroy()
    {
        Destroy(impactMaterial);
    }
}
