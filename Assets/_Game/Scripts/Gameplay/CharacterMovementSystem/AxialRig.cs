using System;
using UnityEngine;

namespace GameJam.CharacterMovement
{
    [Serializable]
    public sealed class AxialRig
    {
        public Transform Pelvis;
        public Transform LumbarLower;
        public Transform LumbarUpper;
        public Transform ThoracicLower;
        public Transform ThoracicUpper;
        public Transform Cervical;
        public Transform Head;

        private Joint pelvis, lumbarLower, lumbarUpper, thoracicLower, thoracicUpper, cervical, head;
        private Vector3 restPelvis;
        private Vector3 pelvisOffset;
        private Vector3 pelvisAngles;
        private Vector3 chestAngles;
        private bool bound;

        public bool Bind()
        {
            bound = Pelvis != null && Pelvis.parent != null &&
                IsBelow(LumbarLower, Pelvis) && IsBelow(LumbarUpper, LumbarLower) &&
                IsBelow(ThoracicLower, LumbarUpper) && IsBelow(ThoracicUpper, ThoracicLower) &&
                IsBelow(Cervical, ThoracicUpper) && IsBelow(Head, Cervical);
            if (!bound)
                return false;

            Quaternion reference = Pelvis.parent.rotation;
            // These are animation limits in the avatar's bind axes: X pitch, Y twist, Z side bend.
            pelvis = new Joint(Pelvis, reference, new Vector3(-12f, -14f, -10f), new Vector3(18f, 14f, 10f));
            lumbarLower = new Joint(LumbarLower, reference, new Vector3(-10f, -5f, -8f), new Vector3(14f, 5f, 8f));
            lumbarUpper = new Joint(LumbarUpper, reference, new Vector3(-10f, -6f, -8f), new Vector3(14f, 6f, 8f));
            thoracicLower = new Joint(ThoracicLower, reference, new Vector3(-10f, -11f, -8f), new Vector3(13f, 11f, 8f));
            thoracicUpper = new Joint(ThoracicUpper, reference, new Vector3(-10f, -13f, -10f), new Vector3(14f, 13f, 10f));
            cervical = new Joint(Cervical, reference, new Vector3(-20f, -30f, -14f), new Vector3(21f, 30f, 14f));
            head = new Joint(Head, reference, new Vector3(-22f, -40f, -16f), new Vector3(25f, 40f, 16f));
            restPelvis = Pelvis.localPosition;
            pelvisOffset = pelvisAngles = chestAngles = Vector3.zero;
            return true;
        }

        public void Pose(in MotionFrame frame, Vector3 supportCenter, float supportWeight,
            Vector3 headInertia, Vector3 chestInertia)
        {
            if (!bound || frame.Root == null || frame.DeltaTime <= 0f)
                return;

            float gait = Mathf.Clamp01(frame.Gait);
            float run = Mathf.Clamp01(frame.Run);
            float load = frame.Grounded ? Mathf.Clamp01(supportWeight) : 0f;
            float push = Mathf.Clamp01(frame.PushWeight);
            float cycle = frame.Phase * 2f * Mathf.PI;
            float lateralStep = Mathf.Sin(cycle) * gait;
            float pelvisTwist = Mathf.Cos(cycle) * gait * Mathf.Lerp(4f, 9f, run);
            float stepLoad = (0.5f + Mathf.Cos(2f * cycle) * 0.5f) * gait * load;
            Vector3 acceleration = Vector3.ClampMagnitude(new Vector3(frame.LocalAcceleration.x, 0f,
                frame.LocalAcceleration.z), 8f);
            Vector3 effort = frame.Root.InverseTransformDirection(frame.PushDirection);
            effort.y = 0f;
            effort = effort.normalized * push;
            Vector3 normal = frame.Root.InverseTransformDirection(frame.GroundNormal);
            float slope = frame.Grounded ? Mathf.Atan2(-normal.z, Mathf.Max(normal.y, 0.1f)) * Mathf.Rad2Deg : 0f;

            Vector3 neutralPelvis = Pelvis.parent.TransformPoint(restPelvis);
            Vector3 support = frame.Root.InverseTransformDirection(supportCenter - neutralPelvis) * load;
            float squat = gait * Mathf.Lerp(0.065f, 0.10f, run) + 0.016f * stepLoad + 0.050f * push;
            var targetOffset = new Vector3(
                Mathf.Clamp(support.x * 0.46f + acceleration.x * 0.008f + effort.x * 0.10f, -0.14f, 0.14f),
                -Mathf.Min(0.16f, squat),
                Mathf.Clamp(support.z * 0.22f + acceleration.z * 0.010f + effort.z * 0.11f, -0.18f, 0.18f));
            pelvisOffset = Vector3.Lerp(pelvisOffset, targetOffset, Response(frame.DeltaTime, 0.045f));
            Pelvis.localPosition = restPelvis + Pelvis.parent.InverseTransformVector(
                frame.Root.TransformDirection(pelvisOffset));

            // Positive pitch moves the upper body forward. Lateral drive tilts it toward the load.
            float pitch = Mathf.Atan2(acceleration.z, 9.81f) * Mathf.Rad2Deg * 0.54f +
                slope * 0.16f + gait * Mathf.Lerp(2f, 8f, run) + effort.z * 22f;
            float roll = -Mathf.Atan2(acceleration.x, 9.81f) * Mathf.Rad2Deg * 0.4f - effort.x * 15f;
            var pelvisTarget = new Vector3(0.38f * pitch - 4f * push, pelvisTwist,
                0.44f * roll + lateralStep * Mathf.Lerp(3f, 5f, run));
            var chestTarget = new Vector3(pitch,
                -pelvisTwist * Mathf.Lerp(0.60f, 0.90f, run) * (1f - 0.55f * push),
                roll - lateralStep * Mathf.Lerp(1.4f, 2.6f, run)) + chestInertia;
            float angularResponse = Response(frame.DeltaTime, 0.03f);
            pelvisAngles = Vector3.Lerp(pelvisAngles, pelvisTarget, angularResponse);
            chestAngles = Vector3.Lerp(chestAngles, chestTarget, angularResponse);

            // Distribute the pelvis-to-thorax change, then superimpose a zero-sum S curve.
            Vector3 pelvisPose = pelvis.Pose(pelvisAngles);
            Vector3 bend = chestAngles - pelvisPose;
            float curve = 3f * gait + 0.3f * Mathf.Abs(pitch) + 7f * push;
            Vector3 thoraxPose = pelvisPose;
            thoraxPose += lumbarLower.Pose(bend * 0.20f + Vector3.right * (-0.35f * curve));
            thoraxPose += lumbarUpper.Pose(bend * 0.22f + Vector3.right * (-0.18f * curve));
            thoraxPose += thoracicLower.Pose(bend * 0.27f + Vector3.right * (0.28f * curve));
            thoraxPose += thoracicUpper.Pose(bend * 0.31f + Vector3.right * (0.25f * curve));

            var gazeBalance = new Vector3(-0.70f * thoraxPose.x, -0.32f * thoraxPose.y,
                -0.77f * thoraxPose.z) + headInertia;
            Vector3 neckPose = cervical.Pose(gazeBalance * 0.48f);
            head.Pose(gazeBalance - neckPose);
        }

        internal void ResetMotion()
        {
            if (!bound)
                return;

            pelvisOffset = pelvisAngles = chestAngles = Vector3.zero;
            Pelvis.localPosition = restPelvis;
            pelvis.Pose(Vector3.zero);
            lumbarLower.Pose(Vector3.zero);
            lumbarUpper.Pose(Vector3.zero);
            thoracicLower.Pose(Vector3.zero);
            thoracicUpper.Pose(Vector3.zero);
            cervical.Pose(Vector3.zero);
            head.Pose(Vector3.zero);
        }

        private static float Response(float dt, float seconds) => 1f - Mathf.Exp(-dt / seconds);

        private static bool IsBelow(Transform joint, Transform ancestor) =>
            joint != null && ancestor != null && joint != ancestor && joint.IsChildOf(ancestor);

        private readonly struct Joint
        {
            private readonly Transform bone;
            private readonly Quaternion rest;
            private readonly Quaternion toReference;
            private readonly Vector3 minimum;
            private readonly Vector3 maximum;

            public Joint(Transform bone, Quaternion reference, Vector3 minimum, Vector3 maximum)
            {
                this.bone = bone;
                this.minimum = minimum;
                this.maximum = maximum;
                rest = bone.localRotation;
                toReference = Quaternion.Inverse(reference) * bone.rotation;
            }

            public Vector3 Pose(Vector3 angles)
            {
                var limited = new Vector3(Mathf.Clamp(angles.x, minimum.x, maximum.x),
                    Mathf.Clamp(angles.y, minimum.y, maximum.y), Mathf.Clamp(angles.z, minimum.z, maximum.z));
                bone.localRotation = rest * Quaternion.Inverse(toReference) * Quaternion.Euler(limited) * toReference;
                return limited;
            }
        }
    }
}
