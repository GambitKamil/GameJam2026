using UnityEngine;

namespace GameJam.CharacterMovement
{
    public enum HandSide { Left, Right }

    public readonly struct HandContact
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Collider Surface;
        public readonly float Weight, Grip, Push;

        public HandContact(Vector3 position, Quaternion rotation, Collider surface = null,
            float weight = 1f, float grip = 0f, float push = 0f)
        {
            Position = position;
            Rotation = rotation;
            Surface = surface;
            Weight = Mathf.Clamp01(weight);
            Grip = Mathf.Clamp01(grip);
            Push = Mathf.Clamp01(push);
        }
    }

    public struct MotionFrame
    {
        public Transform Root;
        public float DeltaTime, Phase, Gait, Run, PushWeight, StrideLength;
        public bool Grounded;
        public Vector3 LocalVelocity, LocalAcceleration, PushDirection, GroundNormal;

        public Vector3 Forward => Root.forward;
        public Vector3 Right => Root.right;
        public Vector3 Up => Root.up;
        public float Speed => new Vector2(LocalVelocity.x, LocalVelocity.z).magnitude;
    }

    public static class RigMath
    {
        public static void SolveTwoBone(Vector3 origin, Vector3 target, Vector3 poleDirection,
            float upper, float lower, float minBend, float maxBend, out Vector3 joint, out Vector3 endpoint)
        {
            Vector3 delta = target - origin;
            float targetDistance = delta.magnitude;
            Vector3 forward = targetDistance > 0.00001f ? delta / targetDistance : Vector3.down;
            float minDistance = SegmentDistance(upper, lower, maxBend);
            float maxDistance = SegmentDistance(upper, lower, minBend);
            float distance = Mathf.Max(0.00001f, Mathf.Clamp(targetDistance, minDistance, maxDistance));
            Vector3 bend = Perpendicular(poleDirection, forward);
            float along = (upper * upper + distance * distance - lower * lower) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            joint = origin + forward * along + bend * height;
            endpoint = origin + forward * distance;
        }

        public static Quaternion Aim(Vector3 localAxis, Vector3 localPole, Vector3 direction, Vector3 pole)
        {
            if (localAxis.sqrMagnitude <= 0.0000001f || direction.sqrMagnitude <= 0.0000001f)
                return Quaternion.identity;

            localAxis.Normalize();
            direction.Normalize();
            return Quaternion.LookRotation(direction, Perpendicular(pole, direction)) *
                Quaternion.Inverse(Quaternion.LookRotation(localAxis, Perpendicular(localPole, localAxis)));
        }

        private static float SegmentDistance(float a, float b, float bend) =>
            Mathf.Sqrt(Mathf.Max(0f, a * a + b * b + 2f * a * b * Mathf.Cos(bend * Mathf.Deg2Rad)));

        private static Vector3 Perpendicular(Vector3 direction, Vector3 axis)
        {
            Vector3 result = Vector3.ProjectOnPlane(direction, axis);
            if (result.sqrMagnitude < 0.000001f)
                result = Vector3.ProjectOnPlane(Mathf.Abs(axis.y) < 0.8f ? Vector3.up : Vector3.forward, axis);
            return result.normalized;
        }
    }
}
