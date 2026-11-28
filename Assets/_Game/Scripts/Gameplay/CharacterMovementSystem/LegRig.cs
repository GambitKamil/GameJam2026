using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.CharacterMovement
{
    [Serializable]
    public sealed class LegRig
    {
        public Transform Hip;
        public Transform Knee;
        public Transform Ankle;
        public Transform Achilles;
        public Transform Heel;
        public Transform Ball;
        public Transform Toe;
        public FootDigitRig[] Digits = Array.Empty<FootDigitRig>();

        [SerializeField, Min(0.1f)] private float probeAbove = 0.55f;
        [SerializeField, Min(0.1f)] private float probeBelow = 0.75f;
        [SerializeField, Range(0f, 60f)] private float maximumSlope = 42f;
        [SerializeField, Min(0.02f)] private float swingClearance = 0.105f;

        private readonly RaycastHit[] hits = new RaycastHit[16];
        private Vector3 restSole, upperAxis, lowerAxis;
        private Vector3 heelOffset, ballOffset, toeOffset;
        private Quaternion hipRest, kneeRest, ankleRest, achillesRest, heelRest, ballRest, toeRest;
        private Quaternion localFootRest, ballInAnkle;
        private float upperLength, lowerLength;
        private Footprint contact, landing;
        private bool ready, swinging, hasTarget;
        private float swingProgress, lastPhase;
        private Vector3 swingStart;
        private Quaternion swingStartRotation, ankleRotation;
        private float heelPitch, achillesPitch, ballPitch, toePitch;

        public Vector3 SupportPosition { get; private set; }
        public float SupportWeight { get; private set; }
        public bool IsPlanted => SupportWeight > 0f;
        public Vector3 AnkleTarget { get; private set; }

        public bool Bind(Transform root)
        {
            ready = root != null && Hip != null && Hip.IsChildOf(root) &&
                Knee != null && Knee.parent == Hip && Ankle != null && Ankle.parent == Knee &&
                Achilles != null && Achilles.parent == Ankle && Heel != null && Heel.parent == Ankle &&
                Ball != null && Ball.parent == Ankle && Toe != null && Toe.parent == Ball;
            if (!ready)
                return false;

            upperLength = Vector3.Distance(Hip.position, Knee.position);
            lowerLength = Vector3.Distance(Knee.position, Ankle.position);
            ready = upperLength > 0.001f && lowerLength > 0.001f;
            if (!ready)
                return false;

            if (Digits != null && Digits.Length > 0)
            {
                if (Digits.Length != 5)
                    return ready = false;
                for (int i = 0; i < Digits.Length; i++)
                    if (Digits[i] == null || !Digits[i].Bind(Ball, i == 0))
                        return ready = false;
            }

            hipRest = Hip.localRotation;
            kneeRest = Knee.localRotation;
            ankleRest = Ankle.localRotation;
            achillesRest = Achilles.localRotation;
            heelRest = Heel.localRotation;
            ballRest = Ball.localRotation;
            toeRest = Toe.localRotation;
            var inverseAnkle = Quaternion.Inverse(Ankle.rotation);
            localFootRest = Quaternion.Inverse(root.rotation) * Ankle.rotation;
            ballInAnkle = inverseAnkle * Ball.rotation;
            upperAxis = Hip.InverseTransformDirection(Knee.position - Hip.position).normalized;
            lowerAxis = Knee.InverseTransformDirection(Ankle.position - Knee.position).normalized;
            heelOffset = inverseAnkle * (Heel.position - Ankle.position);
            ballOffset = inverseAnkle * (Ball.position - Ankle.position);
            toeOffset = inverseAnkle * (Toe.position - Ankle.position);
            restSole = root.InverseTransformPoint(Ankle.position);
            restSole.y = Mathf.Min(root.InverseTransformPoint(Heel.position).y,
                root.InverseTransformPoint(Ball.position).y, root.InverseTransformPoint(Toe.position).y);
            ResetContact();
            return true;
        }

        public void ResetContact()
        {
            contact = landing = default;
            SupportWeight = 0f;
            hasTarget = swinging = false;
            lastPhase = swingProgress = 0f;
        }

        public void Plan(in MotionFrame frame, LayerMask groundMask, float phaseOffset)
        {
            if (!ready)
                return;

            float phase = Mathf.Repeat(frame.Phase + phaseOffset, 1f);
            float stance = Mathf.Lerp(0.62f, 0.44f, frame.Run);
            bool moving = frame.Gait > 0.05f && frame.Speed > 0.05f;
            float phaseAdvance = Mathf.Repeat(phase - lastPhase, 1f);
            lastPhase = phase;
            Vector3 home = frame.Root.TransformPoint(restSole);
            Vector3 velocity = new Vector3(frame.LocalVelocity.x, 0f, frame.LocalVelocity.z);
            Vector3 travel = velocity.sqrMagnitude > 0.0025f
                ? frame.Root.TransformDirection(velocity.normalized)
                : frame.Forward;

            if (!frame.Grounded)
            {
                contact = landing = default;
                swinging = false;
                PlanInAir(frame, home, 0.10f);
                return;
            }

            if (!contact.Valid && !swinging && !FindGround(frame, home, groundMask, out contact))
            {
                PlanInAir(frame, home, 0.035f);
                return;
            }

            bool restepAtIdle = !moving && !swinging && contact.Valid && hasTarget &&
                Vector3.ProjectOnPlane(contact.Position - home, frame.Up).sqrMagnitude > 0.0484f &&
                Vector3.Distance(AnkleTarget, Hip.position) > (upperLength + lowerLength) * 0.98f;
            bool beganSwing = !swinging && (moving && phase >= stance || restepAtIdle);
            if (beganSwing)
            {
                swinging = true;
                swingProgress = 0f;
                swingStart = hasTarget ? AnkleTarget : Ankle.position;
                swingStartRotation = hasTarget ? ankleRotation : Ankle.rotation;
                landing = default;
            }

            if (swinging)
            {
                if (!beganSwing)
                {
                    if (!moving)
                        swingProgress = Mathf.MoveTowards(swingProgress, 1f, frame.DeltaTime / 0.18f);
                    else
                        swingProgress = Mathf.MoveTowards(swingProgress, 1f, phaseAdvance / (1f - stance));
                }

                if (swingProgress < 0.72f || !landing.Valid)
                {
                    // The remaining swing travel and half the next stance put the foot ahead of the pelvis.
                    float lead = moving ? frame.StrideLength * ((1f - stance) * (1f - swingProgress) + stance * 0.5f) : 0f;
                    if (FindGround(frame, home + travel * lead, groundMask, out Footprint next))
                        landing = next;
                    else if (!landing.Valid && FindGround(frame, home, groundMask, out next))
                        landing = next;
                }

                SupportWeight = 0f;
                if (!landing.Valid)
                {
                    PlanInAir(frame, home, 0.035f);
                    return;
                }

                if (swingProgress < 1f)
                {
                    PlanSwing(frame, landing);
                    return;
                }

                contact = landing;
                swinging = false;
            }

            float stanceT = Mathf.Clamp01(phase / stance);
            float gait = Mathf.Clamp01(frame.Gait);
            float strike = -9f * (1f - SmoothRange(0f, 0.24f, stanceT)) * gait;
            float push = 26f * SmoothRange(0.55f, 0.97f, stanceT) * gait;
            float pivot = SmoothRange(0.40f, 0.76f, stanceT);
            SupportPosition = contact.Position;
            SupportWeight = 1f;
            PoseOnPlane(contact, strike + push, pivot, out Vector3 target, out ankleRotation);
            AnkleTarget = target;
            hasTarget = true;
        }

        internal float RequiredPelvisDrop(Vector3 up)
        {
            if (!IsPlanted)
                return 0f;
            Vector3 span = Hip.position - AnkleTarget;
            float height = Vector3.Dot(span, up);
            float reach = upperLength + lowerLength - 0.001f;
            float horizontal = Vector3.ProjectOnPlane(span, up).sqrMagnitude;
            return Mathf.Max(0f, height - Mathf.Sqrt(Mathf.Max(0f, reach * reach - horizontal)));
        }

        public void Solve(in MotionFrame frame)
        {
            if (!ready || !hasTarget)
                return;

            SolvePose(frame);
            if (!IsPlanted)
                return;

            // The ankle limit can differ from the requested foot pitch. Reconcile the
            // actual marker positions against the contact plane without moving the anchor.
            for (int pass = 0; pass < 3; pass++)
            {
                float clearance = SoleClearance();
                if (clearance >= -0.0005f)
                    break;
                AnkleTarget += contact.Normal * (0.0003f - clearance);
                SolvePose(frame);
            }
        }

        private float SoleClearance()
        {
            Vector3 point = contact.Position;
            Vector3 normal = contact.Normal;
            float clearance = Mathf.Min(Vector3.Dot(Heel.position - point, normal),
                Mathf.Min(Vector3.Dot(Ball.position - point, normal), Vector3.Dot(Toe.position - point, normal)));
            if (Digits != null)
                foreach (FootDigitRig digit in Digits)
                    clearance = Mathf.Min(clearance, Vector3.Dot(Ball.TransformPoint(digit.PadOffset) - point, normal));
            return clearance;
        }

        private void SolvePose(in MotionFrame frame)
        {
            // Use the pelvis axes for a stable forward knee pole without inheriting pelvis pitch.
            Vector3 pole = frame.Forward;
            if (Mathf.Abs(Vector3.Dot((AnkleTarget - Hip.position).normalized, pole)) > 0.97f)
                pole = frame.Up;
            RigMath.SolveTwoBone(Hip.position, AnkleTarget, pole, upperLength, lowerLength, 2f, 140f,
                out Vector3 knee, out Vector3 endpoint);

            Quaternion upper = RigMath.Aim(upperAxis, Vector3.forward, knee - Hip.position, pole);
            SetLimited(Hip, upper, hipRest, new Vector3(-105f, -38f, -36f), new Vector3(36f, 38f, 36f));
            Quaternion lower = RigMath.Aim(lowerAxis, Vector3.forward, endpoint - Knee.position, pole);
            SetLimited(Knee, lower, kneeRest, new Vector3(-12f, -8f, -5f), new Vector3(135f, 8f, 5f));
            SetLimited(Ankle, ankleRotation, ankleRest, new Vector3(-36f, -24f, -29f), new Vector3(51f, 24f, 29f));
            Achilles.localRotation = achillesRest * Quaternion.Euler(achillesPitch, 0f, 0f);
            Heel.localRotation = heelRest * Quaternion.Euler(heelPitch, 0f, 0f);
            Ball.localRotation = ballRest * Quaternion.Euler(ballPitch, 0f, 0f);
            Toe.localRotation = toeRest * Quaternion.Euler(toePitch, 0f, 0f);
            if (Digits != null)
                foreach (FootDigitRig digit in Digits)
                    digit.Apply();
        }

        private void PlanSwing(in MotionFrame frame, Footprint goal)
        {
            float progress = swingProgress;
            float t = Mathf.SmoothStep(0f, 1f, progress);
            PoseOnPlane(goal, -9f * frame.Gait, 0f, out Vector3 destination, out Quaternion rotation);
            float arc = Mathf.Sin(Mathf.PI * progress);
            AnkleTarget = Vector3.Lerp(swingStart, destination, t)
                + goal.Normal * (arc * Mathf.Lerp(swingClearance, swingClearance * 1.55f, frame.Run));
            ankleRotation = Quaternion.Slerp(swingStartRotation, rotation, t);
            SetFootJoints(-9f * t * frame.Gait, 0f);
            hasTarget = true;
        }

        private void PlanInAir(in MotionFrame frame, Vector3 home, float tuck)
        {
            Vector3 target = home + frame.Up * (Mathf.Max(-heelOffset.y, -ballOffset.y) + tuck);
            float blend = 1f - Mathf.Exp(-frame.DeltaTime * 15f);
            AnkleTarget = hasTarget ? Vector3.Lerp(AnkleTarget, target, blend) : target;
            Quaternion rotation = frame.Root.rotation * localFootRest;
            ankleRotation = hasTarget ? Quaternion.Slerp(ankleRotation, rotation, blend) : rotation;
            SetFootJoints(0f, 0f);
            SupportWeight = 0f;
            hasTarget = true;
        }

        private void PoseOnPlane(Footprint footprint, float pitch, float pivot, out Vector3 ankle, out Quaternion rotation)
        {
            Vector3 normal = footprint.Normal;
            Quaternion surface = Quaternion.LookRotation(footprint.Forward, normal);
            Quaternion flat = surface * localFootRest;
            rotation = surface * Quaternion.Euler(pitch, 0f, 0f) * localFootRest;
            SetFootJoints(pitch, pivot);

            // Rotate about the ground projection of the heel or forefoot, then lift any lower
            // marker onto the plane. This preserves the contact XZ through the roll.
            Vector3 localPivot = Vector3.Lerp(heelOffset, Vector3.Lerp(ballOffset, toeOffset, pivot * 0.35f), pivot);
            Vector3 planarPivot = Vector3.ProjectOnPlane(flat * localPivot, normal);
            ankle = footprint.Position + planarPivot - rotation * localPivot;
            Quaternion bend = ballInAnkle * Quaternion.Euler(ballPitch, 0f, 0f) * Quaternion.Inverse(ballInAnkle);
            Vector3 distal = ballOffset + bend * (toeOffset - ballOffset);
            float heel = Vector3.Dot(ankle + rotation * heelOffset - footprint.Position, normal);
            float forefoot = Vector3.Dot(ankle + rotation * ballOffset - footprint.Position, normal);
            float toe = Vector3.Dot(ankle + rotation * distal - footprint.Position, normal);
            float lowest = Mathf.Min(heel, Mathf.Min(forefoot, toe));
            if (Digits != null)
            {
                Quaternion ballRotation = ballInAnkle * Quaternion.Euler(ballPitch, 0f, 0f);
                foreach (FootDigitRig digit in Digits)
                {
                    Vector3 pad = ankle + rotation * (ballOffset + ballRotation * digit.PadOffset);
                    lowest = Mathf.Min(lowest, Vector3.Dot(pad - footprint.Position, normal));
                }
            }
            ankle -= normal * lowest;
        }

        private void SetFootJoints(float pitch, float pivot)
        {
            // Achilles and heel are deformation joints. Ball and toe stay in the contact chain.
            achillesPitch = Mathf.Clamp(-0.58f * pitch + 5f * pivot, -20f, 20f);
            heelPitch = Mathf.Clamp(-0.24f * pitch, -12f, 12f);
            ballPitch = Mathf.Clamp(-0.82f * Mathf.Max(0f, pitch), -65f, 30f);
            toePitch = Mathf.Clamp(-0.38f * ballPitch, -36f, 30f);
            if (Digits != null)
                foreach (FootDigitRig digit in Digits)
                    digit.Plan(pitch, ballPitch, pivot);
        }

        private bool FindGround(in MotionFrame frame, Vector3 point, LayerMask mask, out Footprint footprint)
        {
            Vector3 up = frame.Up;
            int count = frame.Root.gameObject.scene.GetPhysicsScene().Raycast(point + up * probeAbove, -up, hits,
                probeAbove + probeBelow, mask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            float slopeCosine = Mathf.Cos(maximumSlope * Mathf.Deg2Rad);
            footprint = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.distance >= nearest || hit.collider.transform.IsChildOf(frame.Root)
                    || Vector3.Dot(hit.normal, up) < slopeCosine)
                    continue;

                nearest = hit.distance;
                footprint = new Footprint(hit.collider, hit.point, hit.normal, frame.Forward);
            }
            return footprint.Valid;
        }

        private static float SmoothRange(float start, float end, float value) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));

        private static void SetLimited(Transform bone, Quaternion world, Quaternion rest, Vector3 min, Vector3 max)
        {
            Quaternion local = Quaternion.Inverse(bone.parent.rotation) * world;
            Quaternion delta = Quaternion.Inverse(rest) * local;
            Vector3 angle = delta.eulerAngles;
            Quaternion limited = ClampAngles(angle, min, max);
            // Unity's equivalent Euler branch puts a hinge flex beyond 90 degrees on the
            // other side of the chart. Choose the closest limited representation.
            if (min.x < -90f || max.x > 90f)
            {
                Quaternion alternate = ClampAngles(new Vector3(180f - angle.x, angle.y + 180f, angle.z + 180f), min, max);
                if (Quaternion.Angle(alternate, delta) < Quaternion.Angle(limited, delta))
                    limited = alternate;
            }
            bone.localRotation = rest * limited;
        }

        private static Quaternion ClampAngles(Vector3 angle, Vector3 min, Vector3 max) =>
            Quaternion.Euler(
                Mathf.Clamp(Mathf.DeltaAngle(0f, angle.x), min.x, max.x),
                Mathf.Clamp(Mathf.DeltaAngle(0f, angle.y), min.y, max.y),
                Mathf.Clamp(Mathf.DeltaAngle(0f, angle.z), min.z, max.z));
        private readonly struct Footprint
        {
            private readonly Collider collider;
            private readonly Transform surface;
            private readonly Vector3 point, normal, forward;

            public Footprint(Collider collider, Vector3 point, Vector3 normal, Vector3 forward)
            {
                this.collider = collider;
                surface = collider.transform;
                this.point = surface.InverseTransformPoint(point);
                this.normal = surface.InverseTransformDirection(normal);
                this.forward = surface.InverseTransformDirection(Vector3.ProjectOnPlane(forward, normal).normalized);
            }

            public bool Valid => collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
            public Vector3 Position => surface.TransformPoint(point);
            public Vector3 Normal => surface.TransformDirection(normal);
            public Vector3 Forward => surface.TransformDirection(forward);
        }
    }

    [Serializable]
    public sealed class FootDigitRig
    {
        public Transform Proximal;
        public Transform Middle;
        public Transform Distal;

        private bool hasMiddle;
        private Quaternion proximalRest, middleRest, distalRest;
        private Quaternion proximalPose, middlePose, distalPose;
        private Vector3 proximalOffset, middleOffset, distalOffset, pad;

        internal Vector3 PadOffset { get; private set; }

        internal bool Bind(Transform ball, bool hallux)
        {
            hasMiddle = !hallux;
            if (Proximal == null || Proximal.parent != ball || Distal == null ||
                (hallux ? Middle != null || Distal.parent != Proximal : Middle == null || Middle.parent != Proximal || Distal.parent != Middle))
                return false;

            proximalRest = Proximal.localRotation;
            if (hasMiddle)
                middleRest = Middle.localRotation;
            distalRest = Distal.localRotation;
            proximalOffset = Proximal.localPosition;
            middleOffset = hasMiddle ? Middle.localPosition : Vector3.zero;
            distalOffset = Distal.localPosition;
            pad = Vector3.forward * Mathf.Clamp(distalOffset.magnitude * 0.72f, 0.009f, 0.035f);
            return true;
        }

        internal void Plan(float pitch, float ballPitch, float rocker)
        {
            float dorsiflexion = -(pitch + ballPitch) + Mathf.Max(0f, -pitch) * 0.3f;
            float proximalPitch = Mathf.Clamp(dorsiflexion, hasMiddle ? -55f : -60f, hasMiddle ? 32f : 30f);
            float load = Mathf.Clamp01(pitch / 26f) * rocker;
            float splay = Mathf.Clamp(proximalOffset.x / 0.055f, -1f, 1f) * 6f * load;
            proximalPose = proximalRest * Quaternion.Euler(proximalPitch, splay, 0f);
            if (hasMiddle)
            {
                float middlePitch = Mathf.Clamp(-0.34f * proximalPitch * load, -25f, 45f);
                middlePose = middleRest * Quaternion.Euler(middlePitch, 0f, 0f);
                distalPose = distalRest * Quaternion.Euler(Mathf.Clamp(middlePitch * 0.62f, -20f, 38f), 0f, 0f);
                PadOffset = proximalOffset + proximalPose * (middleOffset + middlePose * (distalOffset + distalPose * pad));
            }
            else
            {
                float distalPitch = Mathf.Clamp(-0.31f * proximalPitch * load, -28f, 48f);
                distalPose = distalRest * Quaternion.Euler(distalPitch, 0f, 0f);
                PadOffset = proximalOffset + proximalPose * (distalOffset + distalPose * pad);
            }
        }

        internal void Apply()
        {
            Proximal.localRotation = proximalPose;
            if (hasMiddle)
                Middle.localRotation = middlePose;
            Distal.localRotation = distalPose;
        }
    }
}
