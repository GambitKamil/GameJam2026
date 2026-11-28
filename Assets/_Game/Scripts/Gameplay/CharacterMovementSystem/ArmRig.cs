using System;
using UnityEngine;

namespace GameJam.CharacterMovement
{
    [Serializable]
    public sealed class ArmRig
    {
        public Transform Clavicle;
        public Transform UpperArm;
        public Transform Forearm;
        public Transform ForearmTwist;
        public Transform Wrist;

        private const float MinimumElbowFlexion = 5f;
        private const float MaximumElbowFlexion = 145f;
        private const float MaximumHumeralRoll = 75f;
        private const float MaximumPronation = 80f;
        private const float ProximalTwistShare = 0.25f;
        private const float MidpointTwistShare = 0.70f;

        private Quaternion clavicleRest;
        private Quaternion upperRest;
        private Quaternion twistRest;
        private Quaternion wristRest;
        private Quaternion upperAnatomy;
        private Quaternion wristAnatomy;
        private Vector3 restWristInClavicle;
        private Vector3 upperAxis, upperPole;
        private Vector3 lowerAxis, lowerPole;
        private float pushLoad, pushLoadVelocity;
        private bool bound;

        public Vector3 ContactError { get; private set; }

        public bool Bind()
        {
            bound = false;
            if (Clavicle == null || UpperArm == null || Forearm == null || ForearmTwist == null || Wrist == null ||
                UpperArm.parent != Clavicle || Forearm.parent != UpperArm ||
                Wrist.parent != Forearm || ForearmTwist.parent != Forearm || Wrist == ForearmTwist ||
                Forearm.localPosition.sqrMagnitude < 0.000001f || Wrist.localPosition.sqrMagnitude < 0.000001f)
                return false;

            clavicleRest = Clavicle.localRotation;
            upperRest = UpperArm.localRotation;
            twistRest = ForearmTwist.localRotation;
            wristRest = Wrist.localRotation;
            restWristInClavicle = Clavicle.InverseTransformPoint(Wrist.position);
            upperAxis = Forearm.localPosition.normalized;
            lowerAxis = Wrist.localPosition.normalized;
            upperPole = LocalFlexionDirection(upperAxis);
            lowerPole = LocalFlexionDirection(lowerAxis);
            upperAnatomy = RigMath.Aim(Vector3.down, Vector3.forward, upperAxis, upperPole);
            wristAnatomy = Quaternion.Inverse(wristRest) *
                RigMath.Aim(Vector3.down, Vector3.forward, lowerAxis, lowerPole);
            pushLoad = pushLoadVelocity = 0f;
            ContactError = Vector3.zero;
            bound = true;
            return true;
        }

        public void Pose(in MotionFrame frame, in HandContact contact, float side, Vector3 shoulderInertia)
        {
            if (!bound)
                return;

            float dt = Mathf.Max(frame.DeltaTime, 0.0001f);
            float weight = Mathf.Clamp01(contact.Weight);
            float requestedLoad = weight * Mathf.Clamp01(contact.Push);
            pushLoad = Mathf.SmoothDamp(pushLoad, requestedLoad, ref pushLoadVelocity, 0.10f, Mathf.Infinity, dt);

            Clavicle.localRotation = clavicleRest;
            Vector3 shoulder = UpperArm.position;
            float swing = Mathf.Sin(frame.Phase * 2f * Mathf.PI) * side * frame.Gait * Mathf.Lerp(21f, 41f, frame.Run);
            float flight = frame.Grounded ? 0f : Mathf.Clamp(frame.LocalVelocity.y * -3f, -16f, 18f);
            Quaternion idlePose = Quaternion.AngleAxis(swing + flight, frame.Right) *
                Quaternion.AngleAxis(side * Mathf.Lerp(5f, 11f, frame.Run), frame.Forward);
            Vector3 idleWrist = shoulder + idlePose * (Clavicle.TransformPoint(restWristInClavicle) - shoulder);
            Vector3 goal = Vector3.Lerp(idleWrist, contact.Position, weight);
            PoseClavicle(frame, goal - shoulder, side, shoulderInertia);

            shoulder = UpperArm.position;
            float upperLength = UpperArm.TransformVector(Forearm.localPosition).magnitude;
            float lowerLength = Forearm.TransformVector(Wrist.localPosition).magnitude;
            Vector3 elbowPole = side * frame.Right * 0.23f - frame.Up * 0.70f - frame.Forward * 0.55f;
            RigMath.SolveTwoBone(shoulder, goal, elbowPole, upperLength, lowerLength,
                MinimumElbowFlexion, MaximumElbowFlexion, out Vector3 elbow, out Vector3 solvedWrist);

            Quaternion shoulderBasis = Clavicle.rotation * upperRest * upperAnatomy;
            Quaternion shoulderSwing = LimitShoulderSwing(shoulderBasis, elbow - shoulder, side);
            Vector3 upperDirection = shoulderBasis * shoulderSwing * Vector3.down;
            Vector3 neutralFlexion = shoulderBasis * shoulderSwing * Vector3.forward;
            Vector3 lowerGoal = solvedWrist - (shoulder + upperDirection * upperLength);
            float bend = Mathf.Clamp(Vector3.Angle(upperDirection, lowerGoal), MinimumElbowFlexion, MaximumElbowFlexion);
            Vector3 desiredFlexion = Vector3.ProjectOnPlane(lowerGoal, upperDirection);
            float humeralRoll = desiredFlexion.sqrMagnitude > 0.000001f
                ? Vector3.SignedAngle(neutralFlexion, desiredFlexion, upperDirection) : 0f;
            humeralRoll = Mathf.Clamp(humeralRoll, -MaximumHumeralRoll, MaximumHumeralRoll);
            Vector3 hinge = Quaternion.AngleAxis(humeralRoll, upperDirection) * neutralFlexion;
            UpperArm.rotation = RigMath.Aim(upperAxis, upperPole, upperDirection, hinge);

            float elbowRadians = bend * Mathf.Deg2Rad;
            Vector3 lowerDirection = upperDirection * Mathf.Cos(elbowRadians) + hinge * Mathf.Sin(elbowRadians);
            Vector3 lowerForward = -upperDirection * Mathf.Sin(elbowRadians) + hinge * Mathf.Cos(elbowRadians);
            Quaternion lowerFrame = RigMath.Aim(lowerAxis, lowerPole, lowerDirection, lowerForward);
            PoseWrist(lowerFrame, lowerDirection, contact.Rotation, weight, side);
            ContactError = weight > 0f ? contact.Position - Wrist.position : Vector3.zero;
        }

        private void PoseClavicle(in MotionFrame frame, Vector3 reach, float side, Vector3 inertia)
        {
            float elevation = Vector3.Angle(-frame.Up, reach);
            float overhead = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(90f, 155f, elevation));
            float forwardReach = Vector3.Dot(reach.normalized, frame.Forward);
            float protraction = Mathf.Clamp(8f * forwardReach + 13f * pushLoad + side * inertia.y * 0.35f, -12f, 20f);
            float lift = Mathf.Clamp(32f * overhead + side * inertia.z * 0.35f, -8f, 32f);
            float tilt = Mathf.Clamp(-8f * overhead + inertia.x * 0.4f, -10f, 10f);
            Clavicle.localRotation = clavicleRest * Quaternion.Euler(tilt, -side * protraction, side * lift);
        }

        private static Quaternion LimitShoulderSwing(Quaternion basis, Vector3 direction, float side)
        {
            Vector3 local = Quaternion.Inverse(basis) * direction.normalized;
            float sagittal = Mathf.Sqrt(local.y * local.y + local.z * local.z);
            float flexion = Mathf.Clamp(Mathf.Atan2(-local.z, -local.y) * Mathf.Rad2Deg, -145f, 52f);
            float elevation = Mathf.Clamp(Mathf.Atan2(side * local.x, sagittal) * Mathf.Rad2Deg, -32f, 82f);
            return Quaternion.AngleAxis(flexion, Vector3.right) * Quaternion.AngleAxis(side * elevation, Vector3.forward);
        }

        private void PoseWrist(Quaternion lowerFrame, Vector3 direction, Quaternion target, float weight, float side)
        {
            Quaternion untwistedWrist = lowerFrame * wristRest;
            bool validTarget = Quaternion.Dot(target, target) > 0.000001f;
            Quaternion desired = validTarget && weight > 0f
                ? Quaternion.Slerp(untwistedWrist, target.normalized, weight)
                : untwistedWrist;
            Quaternion delta = desired * Quaternion.Inverse(untwistedWrist);
            // Project quaternion vector part onto the ulna axis to extract pronation.
            float axial = Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), direction);
            float twist = Mathf.Clamp(Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(axial, delta.w) * Mathf.Rad2Deg),
                -MaximumPronation, MaximumPronation);

            Forearm.rotation = Quaternion.AngleAxis(twist * ProximalTwistShare, direction) * lowerFrame;
            ForearmTwist.localRotation = Quaternion.AngleAxis(twist * (MidpointTwistShare - ProximalTwistShare), lowerAxis) * twistRest;
            Quaternion wristBase = Quaternion.AngleAxis(twist, direction) * untwistedWrist;
            Quaternion anatomicalBase = wristBase * wristAnatomy;
            Quaternion residual = Quaternion.Inverse(anatomicalBase) * desired * wristAnatomy;
            Vector3 angles = residual.eulerAngles;
            float flexion = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x), -65f, 60f);
            float deviation = Mathf.Clamp(side * Mathf.DeltaAngle(0f, angles.z), -18f, 28f);
            float flexionFraction = flexion / (flexion < 0f ? 65f : 60f);
            float deviationFraction = deviation / (deviation < 0f ? 18f : 28f);
            // Coupled motion prevents simultaneous end-range flexion and deviation.
            float range = Mathf.Max(1f, Mathf.Sqrt(flexionFraction * flexionFraction + deviationFraction * deviationFraction));
            Wrist.rotation = anatomicalBase * Quaternion.Euler(flexion / range, 0f, side * deviation / range) *
                Quaternion.Inverse(wristAnatomy);
        }

        private static Vector3 LocalFlexionDirection(Vector3 axis)
        {
            Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, axis);
            return forward.sqrMagnitude >= 0.01f ? forward.normalized : Vector3.ProjectOnPlane(Vector3.right, axis).normalized;
        }
    }
}
