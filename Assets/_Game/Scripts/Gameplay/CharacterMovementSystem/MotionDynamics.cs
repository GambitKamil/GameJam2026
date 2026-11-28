using UnityEngine;

namespace GameJam.CharacterMovement
{
    internal sealed class MotionDynamics
    {
        private static readonly Vector3 HeadLimit = new Vector3(9f, 5f, 7f);
        private static readonly Vector3 ShoulderLimit = new Vector3(12f, 6f, 9f);

        private Vector3 headAngles, shoulderAngles;
        private Vector3 headVelocity, shoulderVelocity;
        private float measuredSpeed, previousVerticalSpeed;
        private bool groundStateKnown, wasGrounded;

        public float Phase { get; private set; }
        public float Gait { get; private set; }
        public float Run { get; private set; }
        public Vector3 HeadAngles => headAngles;
        public Vector3 ShoulderAngles => shoulderAngles;

        public void Reset()
        {
            Phase = Gait = Run = measuredSpeed = previousVerticalSpeed = 0f;
            headAngles = shoulderAngles = headVelocity = shoulderVelocity = Vector3.zero;
            groundStateKnown = wasGrounded = false;
        }

        public void Step(float dt, Vector3 localDisplacement, Vector3 localVelocity, Vector3 localAcceleration,
            bool grounded, float strideLength, float walkSpeed, float runSpeed)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                return;

            float travel = new Vector2(localDisplacement.x, localDisplacement.z).magnitude;
            bool fullGroundStep = grounded && (!groundStateKnown || wasGrounded);
            if (fullGroundStep)
                Phase = Mathf.Repeat(Phase + travel / Mathf.Max(0.1f, strideLength), 1f);

            // Use the movement achieved by the capsule so a blocked motor does not drive an in-place stride.
            measuredSpeed = Damp(measuredSpeed, fullGroundStep ? travel / dt : 0f, 0.055f, dt);
            float walk = Mathf.Max(0.1f, walkSpeed);
            float run = Mathf.Max(walk + 0.1f, runSpeed);
            Gait = Damp(Gait, grounded ? SmoothRange(0.025f, walk, measuredSpeed) : 0f, 0.080f, dt);
            Run = Damp(Run, grounded ? SmoothRange(walk, run, measuredSpeed) : 0f, 0.110f, dt);

            if (groundStateKnown && wasGrounded != grounded)
            {
                // Inject angular velocity once per transition using the observed vertical speed in m/s.
                float speed = grounded
                    ? Mathf.Clamp(-Mathf.Min(previousVerticalSpeed, localVelocity.y), 0f, 7f)
                    : -Mathf.Clamp(localVelocity.y, 0f, 5f);
                headVelocity.x += speed * 15f;
                shoulderVelocity.x += speed * 22f;
            }
            groundStateKnown = true;
            wasGrounded = grounded;
            previousVerticalSpeed = localVelocity.y;

            Vector3 acceleration = Vector3.ClampMagnitude(localAcceleration, 25f);
            float cycle = 2f * Mathf.PI * Phase;
            float sway = Mathf.Sin(cycle) * Gait;
            float bounce = Mathf.Cos(2f * cycle) * Gait;
            float verticalBias = grounded ? 0f : Mathf.Clamp(-localVelocity.y * 0.42f, -2f, 2f);
            Vector3 headTarget = new Vector3(
                -0.48f * acceleration.z + verticalBias + 0.62f * bounce,
                -0.12f * acceleration.x + 0.38f * sway,
                0.36f * acceleration.x + 0.42f * sway);
            Vector3 shoulderTarget = new Vector3(
                -0.61f * acceleration.z + 1.30f * verticalBias - 0.94f * bounce,
                -0.19f * acceleration.x - 0.72f * sway,
                0.48f * acceleration.x - 0.72f * sway);
            Spring(ref headAngles, ref headVelocity, headTarget, HeadLimit, 15f, dt);
            Spring(ref shoulderAngles, ref shoulderVelocity, shoulderTarget, ShoulderLimit, 12f, dt);
        }

        private static float Damp(float value, float target, float timeConstant, float dt)
            => Mathf.LerpUnclamped(target, value, Mathf.Exp(-dt / timeConstant));

        private static float SmoothRange(float start, float end, float value)
            => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));

        // Exact update of x'' + 2w x' + w²(x - target) = 0 over a constant-target interval.
        private static void Spring(ref Vector3 angle, ref Vector3 velocity, Vector3 target, Vector3 limit, float frequency, float dt)
        {
            Vector3 offset = angle - target;
            Vector3 slope = velocity + frequency * offset;
            float decay = Mathf.Exp(-frequency * dt);
            angle = target + (offset + slope * dt) * decay;
            velocity = (velocity - frequency * slope * dt) * decay;
            Limit(ref angle.x, ref velocity.x, limit.x);
            Limit(ref angle.y, ref velocity.y, limit.y);
            Limit(ref angle.z, ref velocity.z, limit.z);
        }

        private static void Limit(ref float angle, ref float velocity, float limit)
        {
            float limited = Mathf.Clamp(angle, -limit, limit);
            if (limited != angle && velocity * angle > 0f)
                velocity = 0f;
            angle = limited;
        }
    }
}
