using UnityEngine;

public sealed class CellAnimationLibrary : MonoBehaviour
{
    public enum Motion { Idle, Walk, CrouchIdle, CrouchWalk, FloorPickup, TablePickup, Interact, Push, Throw, SitStand }

    [SerializeField] private GameObject sourceModel;
    [SerializeField] private AnimationClip[] clips;

    private GameObject rig;
    private Transform pelvis;
    private Transform head;
    private Transform leftFoot;
    private Transform rightFoot;
    private Transform leftHand;
    private Transform leftShoulder;
    private float idleHeight;

    public struct Pose
    {
        public Vector3 NearStride;
        public Vector3 FarStride;
        public float HipHeightDelta;
        public float TorsoPitch;
        public float ArmPitch;
    }

    private void Awake()
    {
        rig = Instantiate(sourceModel);
        rig.name = "Quaternius Cell Animation Source";
        rig.transform.SetParent(transform, false);
        foreach (Renderer renderer in rig.GetComponentsInChildren<Renderer>())
            renderer.enabled = false;

        Transform[] bones = rig.GetComponentsInChildren<Transform>();
        pelvis = System.Array.Find(bones, bone => bone.name == "pelvis");
        head = System.Array.Find(bones, bone => bone.name == "Head");
        leftFoot = System.Array.Find(bones, bone => bone.name == "foot_l");
        rightFoot = System.Array.Find(bones, bone => bone.name == "foot_r");
        leftHand = System.Array.Find(bones, bone => bone.name == "hand_l");
        leftShoulder = System.Array.Find(bones, bone => bone.name == "upperarm_l");
        clips[(int)Motion.Idle].SampleAnimation(rig, 0f);
        idleHeight = Local(pelvis).y;
    }

    public Pose Sample(Motion motion, float normalizedTime)
    {
        AnimationClip clip = clips[(int)motion];
        clip.SampleAnimation(rig, Mathf.Clamp01(normalizedTime) * clip.length);
        Vector3 chest = Local(head) - Local(pelvis);
        Vector3 arm = Local(leftHand) - Local(leftShoulder);
        return new Pose
        {
            NearStride = FootStride(leftFoot),
            FarStride = FootStride(rightFoot),
            HipHeightDelta = Local(pelvis).y - idleHeight,
            TorsoPitch = Mathf.Atan2(chest.z, chest.y) * Mathf.Rad2Deg,
            ArmPitch = Mathf.Atan2(-arm.z, Mathf.Max(-arm.y, 0.04f)) * Mathf.Rad2Deg
        };
    }

    private Vector3 Local(Transform bone) => rig.transform.InverseTransformPoint(bone.position);

    private Vector3 FootStride(Transform foot)
    {
        Vector3 p = Local(foot);
        return new Vector3(-p.z * 0.63f, Mathf.Max(0f, p.y - 0.10f) * 0.62f, 0f);
    }
}
