using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.CharacterMovement
{
    [Serializable]
    public sealed class HandRig
    {
        public FingerRig Thumb = new FingerRig();
        public FingerRig Index = new FingerRig();
        public FingerRig Middle = new FingerRig();
        public FingerRig Ring = new FingerRig();
        public FingerRig Little = new FingerRig();

        private readonly Collider[] overlapHits = new Collider[32];

        public bool HasUnresolvedContact { get; private set; }

        public bool Bind()
        {
            HasUnresolvedContact = false;
            return Thumb != null && Index != null && Middle != null && Ring != null && Little != null &&
                Thumb.Bind(true) && Index.Bind(false) && Middle.Bind(false) &&
                Ring.Bind(false) && Little.Bind(false);
        }

        public void Pose(in HandContact contact, float dt)
        {
            var probe = new FingerSurfaceProbe(contact.Weight > 0f ? contact.Surface : null, overlapHits);
            float grip = contact.Grip * contact.Weight;
            HasUnresolvedContact = !Thumb.Pose(grip, dt, probe);
            HasUnresolvedContact |= !Index.Pose(grip, dt, probe);
            HasUnresolvedContact |= !Middle.Pose(grip, dt, probe);
            HasUnresolvedContact |= !Ring.Pose(grip, dt, probe);
            HasUnresolvedContact |= !Little.Pose(grip, dt, probe);
        }

        public bool IsSurfaceClear(Collider surface)
        {
            var probe = new FingerSurfaceProbe(surface, overlapHits);
            return !Thumb.Touches(probe) && !Index.Touches(probe) && !Middle.Touches(probe) &&
                !Ring.Touches(probe) && !Little.Touches(probe);
        }
    }

    [Serializable]
    public sealed class FingerRig
    {
        public Transform Base, Middle, Tip, End;
        public Vector3 CurlAxis = Vector3.left;
        public Vector3 OpposeAxis = Vector3.up;
        [Min(0.001f)] public float Radius = 0.008f;

        private const float CurlSpeed = 4f;
        private const float SearchIncrement = 0.025f;
        private const int RecoverySteps = 20;
        private const int ContactRefinements = 7;
        private const float Clearance = 0.001f;

        private Quaternion baseRest, middleRest, tipRest;
        private Vector3 curlAxis, opposeAxis;
        private bool thumb;

        public float Closure { get; private set; }

        internal bool Bind(bool isThumb)
        {
            if (Base == null || Middle == null || Tip == null || End == null ||
                Middle.parent != Base || Tip.parent != Middle || End.parent != Tip ||
                Middle.localPosition.sqrMagnitude < 0.000001f || Tip.localPosition.sqrMagnitude < 0.000001f ||
                End.localPosition.sqrMagnitude < 0.000001f)
                return false;

            baseRest = Base.localRotation;
            middleRest = Middle.localRotation;
            tipRest = Tip.localRotation;
            curlAxis = CurlAxis.sqrMagnitude > 0.0001f ? CurlAxis.normalized : Vector3.left;
            opposeAxis = OpposeAxis.sqrMagnitude > 0.0001f ? OpposeAxis.normalized : Vector3.up;
            thumb = isThumb;
            Closure = 0f;
            return true;
        }

        internal bool Pose(float target, float dt, in FingerSurfaceProbe probe)
        {
            float current = Closure;
            if (probe.IsActive && Touches(probe) && !TryRecover(current, probe, out current))
            {
                // A wrist target that puts a joint base inside the surface requires arm clearance.
                Apply(0f);
                Closure = 0f;
                return false;
            }

            float next = Mathf.MoveTowards(current, target, CurlSpeed * Mathf.Clamp(dt, 0f, 0.05f));
            if (probe.IsActive)
            {
                // Search along the curl instead of testing only its endpoint after contact.
                int steps = Mathf.CeilToInt(Mathf.Abs(next - current) / SearchIncrement);
                float safe = current;
                for (int step = 1; step <= steps; step++)
                {
                    float candidate = Mathf.Lerp(current, next, step / (float)steps);
                    Apply(candidate);
                    if (Touches(probe))
                    {
                        next = RefineContact(safe, candidate, probe);
                        break;
                    }
                    safe = candidate;
                }
            }

            Apply(next);
            Closure = next;
            return true;
        }

        private bool TryRecover(float current, in FingerSurfaceProbe probe, out float safe)
        {
            // Find a feasible angle if a moved target intersects the previous physical pose.
            for (int step = 1; step <= RecoverySteps; step++)
            {
                float distance = step / (float)RecoverySteps;
                float opened = Mathf.Max(0f, current - distance);
                Apply(opened);
                if (!Touches(probe))
                {
                    safe = RefineContact(opened, current, probe);
                    return true;
                }

                float closed = Mathf.Min(1f, current + distance);
                Apply(closed);
                if (!Touches(probe))
                {
                    safe = RefineContact(closed, current, probe);
                    return true;
                }
            }

            safe = current;
            return false;
        }

        private float RefineContact(float safe, float blocked, in FingerSurfaceProbe probe)
        {
            for (int pass = 0; pass < ContactRefinements; pass++)
            {
                float angleWeight = (safe + blocked) * 0.5f;
                Apply(angleWeight);
                if (Touches(probe))
                    blocked = angleWeight;
                else
                    safe = angleWeight;
            }
            return safe;
        }

        private void Apply(float closure)
        {
            float baseAngle = (thumb ? 38f : 80f) * closure;
            float middleAngle = (thumb ? 52f : 100f) * closure;
            // The distal joints share the middle joint's flexion at a reduced ratio.
            float tipAngle = Mathf.Min(thumb ? 45f : 67f, middleAngle * (thumb ? 0.865385f : 0.67f));
            Quaternion opposition = thumb ? Quaternion.AngleAxis(35f * closure, opposeAxis) : Quaternion.identity;
            Base.localRotation = baseRest * opposition * Quaternion.AngleAxis(baseAngle, curlAxis);
            Middle.localRotation = middleRest * Quaternion.AngleAxis(middleAngle, curlAxis);
            Tip.localRotation = tipRest * Quaternion.AngleAxis(tipAngle, curlAxis);
        }

        internal bool Touches(in FingerSurfaceProbe probe)
        {
            float radius = Mathf.Max(0.001f, Radius);
            return probe.IsActive && (probe.Overlaps(Base.position, Middle.position, radius + Clearance) ||
                probe.Overlaps(Middle.position, Tip.position, radius * 0.90f + Clearance) ||
                probe.Overlaps(Tip.position, End.position, radius * 0.78f + Clearance));
        }
    }

    internal readonly struct FingerSurfaceProbe
    {
        private readonly Collider surface;
        private readonly Collider[] hits;
        private readonly PhysicsScene physicsScene;
        private readonly Bounds surfaceBounds;
        private readonly int layerMask;

        public bool IsActive => surface != null;

        public FingerSurfaceProbe(Collider target, Collider[] hits)
        {
            surface = target != null && target.enabled && target.gameObject.activeInHierarchy &&
                target.gameObject.scene.isLoaded ? target : null;
            this.hits = hits;
            physicsScene = surface != null ? surface.gameObject.scene.GetPhysicsScene() : default;
            surfaceBounds = surface != null ? surface.bounds : default;
            layerMask = surface != null ? 1 << surface.gameObject.layer : 0;
        }

        public bool Overlaps(Vector3 from, Vector3 to, float radius)
        {
            var capsuleBounds = new Bounds((from + to) * 0.5f,
                new Vector3(Mathf.Abs(from.x - to.x), Mathf.Abs(from.y - to.y), Mathf.Abs(from.z - to.z)));
            capsuleBounds.Expand(radius * 2f);
            if (!capsuleBounds.Intersects(surfaceBounds))
                return false;

            int count = physicsScene.OverlapCapsule(from, to, radius, hits, layerMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
                if (hits[i] == surface)
                    return true;

            // A saturated result buffer can omit the chosen surface. Keep the safe curl.
            return count == hits.Length;
        }
    }
}
