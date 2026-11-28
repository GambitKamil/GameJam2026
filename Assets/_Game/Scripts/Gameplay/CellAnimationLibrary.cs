using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

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
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable[] samples;
    private int activeClip = -1;

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
        var animator = rig.GetComponent<Animator>();
        if (animator == null)
            animator = rig.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        graph = PlayableGraph.Create("Cell Quaternius poses");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        mixer = AnimationMixerPlayable.Create(graph, clips.Length);
        samples = new AnimationClipPlayable[clips.Length];
        for (int i = 0; i < clips.Length; i++)
        {
            samples[i] = AnimationClipPlayable.Create(graph, clips[i]);
            samples[i].SetSpeed(0);
            graph.Connect(samples[i], 0, mixer, i);
        }

        AnimationPlayableOutput.Create(graph, "Cell animation rig", animator).SetSourcePlayable(mixer);
        graph.Play();
        EvaluateClip((int)Motion.Idle, 0f);
        idleHeight = Local(pelvis).y;
    }

    public Pose Sample(Motion motion, float normalizedTime)
    {
        EvaluateClip((int)motion, normalizedTime);
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

    private void EvaluateClip(int index, float progress)
    {
        if (activeClip != index)
        {
            if (activeClip >= 0)
                mixer.SetInputWeight(activeClip, 0f);
            mixer.SetInputWeight(index, 1f);
            activeClip = index;
        }

        samples[index].SetTime(Mathf.Clamp01(progress) * clips[index].length);
        graph.Evaluate(0f);
    }

    private void OnDestroy()
    {
        if (graph.IsValid())
            graph.Destroy();
    }

    private Vector3 Local(Transform bone) => rig.transform.InverseTransformPoint(bone.position);

    private Vector3 FootStride(Transform foot)
    {
        Vector3 p = Local(foot);
        return new Vector3(-p.z * 0.63f, Mathf.Max(0f, p.y - 0.10f) * 0.62f, 0f);
    }
}
