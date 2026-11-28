using UnityEngine;

// Drives the skinned Prisoner model from the existing CellMotion / SideViewPlayer state.
// Lives on the model root (next to its Animator) so OnAnimatorIK is called here.
// Runs after every other LateUpdate so it sees the final CellMotion pose of the frame.
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(Animator))]
public sealed class PrisonerAnimatorBridge : MonoBehaviour
{
    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int WalkMulId = Animator.StringToHash("WalkMul");
    static readonly int SneakMulId = Animator.StringToHash("SneakMul");
    static readonly int CrouchId = Animator.StringToHash("Crouch");

    [Tooltip("Ground speed of the Walk clip in m/s. Playback rate = velocity / this, so the feet do not slide.")]
    [SerializeField, Min(0.1f)] private float walkClipSpeed = 1.0f;
    [Tooltip("Ground speed of the SneakWalk clip in m/s.")]
    [SerializeField, Min(0.1f)] private float sneakClipSpeed = 0.6f;
    [SerializeField, Range(0.05f, 1f)] private float minimumPlaybackRate = 0.25f;
    [SerializeField, Min(90f)] private float turnDegreesPerSecond = 900f;
    [Tooltip("Old procedural visuals that are hidden while the Blender model is shown.")]
    [SerializeField] private Transform[] legacyVisuals;
    [Tooltip("Keep the procedural figure for the bunk wake-up until a WakeUp clip is wired in.")]
    [SerializeField] private bool legacyWakeUp = true;

    private Animator animator;
    private CellMotion motion;
    private SideViewPlayer player;
    private Rigidbody body;
    private Renderer[] legacyRenderers = new Renderer[0];
    private Renderer[] modelRenderers = new Renderer[0];
    private Transform breadSocket;
    private Transform keySocket;
    private Transform rodSocketRight;
    private Transform rodSocketLeft;
    private float yaw = 90f;

    public Transform BreadSocket => breadSocket;
    public Transform KeySocket => keySocket;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        motion = GetComponentInParent<CellMotion>();
        player = GetComponentInParent<SideViewPlayer>();
        body = GetComponentInParent<Rigidbody>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (rightHand != null)
        {
            breadSocket = rightHand.Find("Socket_Bread_R");
            keySocket = rightHand.Find("Socket_Key_R");
            rodSocketRight = rightHand.Find("Socket_Rod_R");
        }
        if (leftHand != null)
            rodSocketLeft = leftHand.Find("Socket_Rod_L");
        modelRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void Start()
    {
        // CellMotion builds its runtime sleeve in Awake, so collect the legacy renderers afterwards.
        var found = new System.Collections.Generic.List<Renderer>();
        if (legacyVisuals != null)
            foreach (Transform root in legacyVisuals)
                if (root != null)
                    foreach (Renderer part in root.GetComponentsInChildren<Renderer>(true))
                        if (!part.transform.IsChildOf(transform))
                            found.Add(part);
        legacyRenderers = found.ToArray();
        if (motion != null)
            yaw = motion.Facing > 0 ? 90f : 270f;
        transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private void Update()
    {
        if (motion == null)
            return;

        float vx = body != null ? Mathf.Abs(body.linearVelocity.x) : 0f;
        bool busy = motion.Busy;
        animator.SetFloat(SpeedId, busy ? 0f : vx);
        animator.SetFloat(WalkMulId, Mathf.Max(minimumPlaybackRate, vx / walkClipSpeed));
        animator.SetFloat(SneakMulId, Mathf.Max(minimumPlaybackRate, vx / sneakClipSpeed));
        animator.SetBool(CrouchId, !busy && player != null && player.Crouching);
    }

    private void LateUpdate()
    {
        if (motion == null)
            return;

        bool showLegacy = legacyWakeUp && motion.WakingUp;
        foreach (Renderer part in legacyRenderers)
            if (part != null)
                part.forceRenderingOff = !showLegacy;
        foreach (Renderer part in modelRenderers)
            if (part != null)
                part.forceRenderingOff = showLegacy;
        if (showLegacy)
            return;

        // Turn through the camera side (yaw 180) instead of flipping the scale, so left stays left.
        float target = motion.Facing > 0 ? 90f : 270f;
        yaw = Mathf.MoveTowards(yaw, target, turnDegreesPerSecond * Time.deltaTime);
        transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        if (rodSocketRight == null)
            return;

        // Gameplay reads motion.HandPosition (key pick-up, rod lift, hook, throw origin):
        // keep the hidden legacy hand on the visible fist.
        Vector3 grip = rodSocketRight.position;
        Transform legacyHand = motion.LegacyHand;
        if (legacyHand != null)
            legacyHand.position = grip;

        GameObject bread = motion.HeldBread;
        if (bread != null && breadSocket != null)
            bread.transform.position = breadSocket.position;

        // Plain carrying is purely visual in CellEscape; hook and lift moves stay untouched while Busy.
        Transform rod = motion.CarriedRod;
        if (rod != null && motion.CarryingRod && !motion.Busy && !motion.OccupiesHand)
            rod.position = grip - rod.TransformVector(new Vector3(-0.27f, 0f, 0f));
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (motion == null)
            return;

        float weight = motion.WakingUp ? 0f : motion.HandTargetWeight;
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
        if (weight > 0f)
            animator.SetIKPosition(AvatarIKGoal.RightHand, motion.HandTarget);
    }

    // Two-hand hold: the rod pivot sits in the right fist and its +Z axis points to the left fist.
    public void PlaceRod(Transform rod)
    {
        if (rod == null || rodSocketRight == null || rodSocketLeft == null)
            return;
        Vector3 along = rodSocketLeft.position - rodSocketRight.position;
        if (along.sqrMagnitude < 0.0001f)
            return;
        rod.SetPositionAndRotation(rodSocketRight.position, Quaternion.LookRotation(along, Vector3.up));
    }
}
