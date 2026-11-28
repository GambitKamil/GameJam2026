using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.CharacterMovement
{
    [DefaultExecutionOrder(80)]
    [DisallowMultipleComponent]
    public sealed class CharacterMotionController : MonoBehaviour
    {
        [SerializeField] private Transform rigRoot;
        [SerializeField] private Rigidbody motionBody;
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private AxialRig axial = new AxialRig();
        [SerializeField] private LegRig leftLeg = new LegRig();
        [SerializeField] private LegRig rightLeg = new LegRig();
        [SerializeField] private ArmRig leftArm = new ArmRig();
        [SerializeField] private ArmRig rightArm = new ArmRig();
        [SerializeField] private HandRig leftHand = new HandRig();
        [SerializeField] private HandRig rightHand = new HandRig();
        [SerializeField, Min(0.1f)] private float walkStride = 0.98f;
        [SerializeField, Min(0.1f)] private float runStride = 1.42f;
        [SerializeField, Min(0.1f)] private float walkSpeed = 1.55f;
        [SerializeField, Min(0.1f)] private float runSpeed = 2.85f;
        [SerializeField, Min(0f)] private float pushForce = 220f;
        [SerializeField, Range(0f, 1f)] private float pushRecoil = 0.12f;

        private readonly RaycastHit[] groundHits = new RaycastHit[12];
        private readonly MotionDynamics dynamics = new MotionDynamics();
        private HandContact leftContact, rightContact;
        private Vector3 lastPosition, lastVelocity, suppliedVelocity, suppliedNormal;
        private bool initialized, suppliedMotion, suppliedGrounded, grounded;
        private float supportDrop;

        public bool IsReady => initialized;

        public void SetMotion(Vector3 worldVelocity, bool isGrounded, Vector3 surfaceNormal)
        {
            suppliedMotion = true;
            suppliedVelocity = worldVelocity;
            suppliedGrounded = isGrounded;
            suppliedNormal = surfaceNormal;
        }

        public void UseMotorMotion() => suppliedMotion = false;

        public void SetHandContact(HandSide side, in HandContact contact)
        {
            if (side == HandSide.Left)
                leftContact = contact;
            else
                rightContact = contact;
        }

        public void ClearHandContact(HandSide side) => SetHandContact(side, default);

        private void Awake()
        {
            if (rigRoot == null)
                rigRoot = transform;
            if (motionBody == null)
                motionBody = rigRoot.GetComponent<Rigidbody>();
            lastPosition = rigRoot.position;
            lastVelocity = motionBody == null ? Vector3.zero : motionBody.linearVelocity;
            initialized = axial.Bind() && leftLeg.Bind(rigRoot) && rightLeg.Bind(rigRoot) &&
                leftArm.Bind() && rightArm.Bind() && leftHand.Bind() && rightHand.Bind();
            if (!initialized)
                Debug.LogError("Character motion requires complete axial, limb, and finger bindings.", this);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (!initialized || dt <= 0f)
                return;

            Vector3 displacement = rigRoot.position - lastPosition;
            if (displacement.sqrMagnitude > 2.25f)
            {
                dynamics.Reset();
                axial.ResetMotion();
                supportDrop = 0f;
                leftLeg.ResetContact();
                rightLeg.ResetContact();
                displacement = Vector3.zero;
            }
            lastPosition = rigRoot.position;
            Vector3 velocity = suppliedMotion ? suppliedVelocity :
                motionBody != null ? motionBody.linearVelocity : displacement / dt;
            Vector3 acceleration = (velocity - lastVelocity) / dt;
            lastVelocity = velocity;
            Vector3 normal = suppliedNormal;
            grounded = suppliedMotion ? suppliedGrounded : ProbeGround(velocity, out normal);
            if (normal.sqrMagnitude < 0.01f)
                normal = rigRoot.up;

            Vector3 localVelocity = rigRoot.InverseTransformDirection(velocity);
            Vector3 localAcceleration = rigRoot.InverseTransformDirection(acceleration);
            Vector3 localDisplacement = rigRoot.InverseTransformDirection(displacement);
            float speed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
            float stride = Mathf.Lerp(walkStride, runStride, Mathf.InverseLerp(walkSpeed, runSpeed, speed));
            dynamics.Step(dt, localDisplacement, localVelocity, localAcceleration, grounded, stride, walkSpeed, runSpeed);
            Vector3 push = PushDirection(leftContact) * leftContact.Push * leftContact.Weight +
                PushDirection(rightContact) * rightContact.Push * rightContact.Weight;
            var frame = new MotionFrame
            {
                Root = rigRoot, DeltaTime = dt, Phase = dynamics.Phase, Gait = dynamics.Gait, Run = dynamics.Run,
                Grounded = grounded, LocalVelocity = localVelocity, LocalAcceleration = localAcceleration,
                GroundNormal = normal.normalized, StrideLength = stride,
                PushWeight = grounded ? Mathf.Clamp01(push.magnitude) : 0f, PushDirection = push.normalized
            };

            leftLeg.Plan(frame, groundLayers, 0f);
            rightLeg.Plan(frame, groundLayers, 0.5f);
            float support = leftLeg.SupportWeight + rightLeg.SupportWeight;
            Vector3 supportCenter = support > 0.0001f
                ? (leftLeg.SupportPosition * leftLeg.SupportWeight + rightLeg.SupportPosition * rightLeg.SupportWeight) / support
                : rigRoot.position;
            axial.Pose(frame, supportCenter, Mathf.Clamp01(support), dynamics.HeadAngles, dynamics.ShoulderAngles);
            // Bring the pelvis into range of both fixed support anchors before solving the legs.
            float desiredDrop = Mathf.Min(0.12f, Mathf.Max(leftLeg.RequiredPelvisDrop(frame.Up), rightLeg.RequiredPelvisDrop(frame.Up)));
            supportDrop = Mathf.Max(desiredDrop, Mathf.MoveTowards(supportDrop, desiredDrop, dt * 0.85f));
            axial.Pelvis.position -= frame.Up * supportDrop;
            leftLeg.Solve(frame);
            rightLeg.Solve(frame);
            PoseHand(frame, leftArm, leftHand, leftContact, -1f);
            PoseHand(frame, rightArm, rightHand, rightContact, 1f);
        }

        private void PoseHand(MotionFrame frame, ArmRig arm, HandRig hand, in HandContact contact, float side)
        {
            arm.Pose(frame, contact, side, dynamics.ShoulderAngles);
            hand.Pose(contact, frame.DeltaTime);
            if (!hand.HasUnresolvedContact || contact.Surface == null)
                return;

            // A finger base has no safe curl when the wrist target embeds it. Pull back the wrist.
            Vector3 away = (arm.UpperArm.position - contact.Position).normalized;
            frame.DeltaTime = 0f;
            for (int attempt = 1; attempt <= 4 && hand.HasUnresolvedContact; attempt++)
            {
                var cleared = new HandContact(contact.Position + away * (0.025f * attempt), contact.Rotation,
                    contact.Surface, contact.Weight, contact.Grip, contact.Push);
                arm.Pose(frame, cleared, side, dynamics.ShoulderAngles);
                hand.Pose(cleared, 0f);
            }
        }

        private bool ProbeGround(Vector3 velocity, out Vector3 normal)
        {
            normal = rigRoot.up;
            if (Vector3.Dot(velocity, normal) > 0.35f)
                return false;
            int count = rigRoot.gameObject.scene.GetPhysicsScene().Raycast(rigRoot.position + normal * 0.12f, -normal, groundHits,
                0.28f, groundLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                if (hit.transform.IsChildOf(rigRoot) || (motionBody != null && hit.rigidbody == motionBody) ||
                    Vector3.Dot(hit.normal, rigRoot.up) < 0.60f || hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                normal = hit.normal;
            }
            return !float.IsPositiveInfinity(nearest);
        }

        private Vector3 PushDirection(in HandContact contact) => contact.Weight > 0f && contact.Push > 0f
            ? Vector3.ProjectOnPlane(contact.Position - rigRoot.position, rigRoot.up).normalized : Vector3.zero;

        private void FixedUpdate()
        {
            if (!initialized || !grounded || pushForce <= 0f)
                return;

            float load = Mathf.Max(1f, leftContact.Weight * leftContact.Push + rightContact.Weight * rightContact.Push);
            ApplyPush(leftContact, leftArm.Wrist.position, load);
            ApplyPush(rightContact, rightArm.Wrist.position, load);
        }

        private void ApplyPush(in HandContact contact, Vector3 wristPosition, float load)
        {
            Collider surface = contact.Surface;
            if (surface == null || !surface.enabled || !surface.gameObject.activeInHierarchy || !surface.gameObject.scene.isLoaded ||
                contact.Weight <= 0f || contact.Push <= 0f || (wristPosition - contact.Position).sqrMagnitude > 0.0121f)
                return;

            Rigidbody target = surface.attachedRigidbody;
            if (target == null || target.isKinematic || target == motionBody ||
                (surface.ClosestPoint(contact.Position) - contact.Position).sqrMagnitude > 0.0064f ||
                (surface.ClosestPoint(wristPosition) - wristPosition).sqrMagnitude > 0.0049f)
                return;

            Vector3 direction = PushDirection(contact);
            if (Vector3.Dot(target.worldCenterOfMass - contact.Position, direction) <= 0f)
                return;
            Vector3 force = direction * (Mathf.Min(pushForce, target.mass * 7f) * contact.Weight * contact.Push / load);
            target.AddForceAtPosition(force, contact.Position, ForceMode.Force);
            if (motionBody != null && !motionBody.isKinematic && pushRecoil > 0f)
                motionBody.AddForce(-force * pushRecoil, ForceMode.Force);
        }
    }
}
