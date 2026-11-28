using System.Collections;
using UnityEngine;

// Checkpoints are markers along the shaft. Walking past one saves where the prisoner, the cart,
// its brake and the power lever are; getting caught puts all of them back.
public sealed class CheckpointManager : MonoBehaviour
{
    [Tooltip("Spawn markers in route order (left to right).")]
    [SerializeField] private Transform[] checkpoints;
    [SerializeField] private SideViewPlayer player;
    [SerializeField] private CartController cart;
    [SerializeField] private CartHidingController hiding;
    [SerializeField] private LeverController powerLever;
    [SerializeField] private ElevatorController elevator;
    [SerializeField] private FlickeringLightController shaftLights;
    [SerializeField] private SecurityCameraController[] cameras;
    [SerializeField] private VentilationPlayerActions actions;
    [SerializeField] private VentilationHud hud;
    [SerializeField, Min(0.05f)] private float fadeOutSeconds = 0.35f;
    [SerializeField, Min(0.05f)] private float fadeInSeconds = 0.6f;

    private struct Snapshot
    {
        public Vector3 Spawn;
        public float CartX, CartY;
        public bool Braked, Power;
    }

    public int Current { get; private set; }
    private Snapshot saved;
    private Rigidbody body;
    private CapsuleCollider capsule;

    private void Awake()
    {
        body = player.GetComponent<Rigidbody>();
        capsule = player.GetComponent<CapsuleCollider>();
    }

    private void Start() => Save(0);

    private void Update()
    {
        int next = Current + 1;
        if (next < checkpoints.Length && body.position.x >= checkpoints[next].position.x && !hiding.IsHidden)
            Save(next);
    }

    private void Save(int index)
    {
        Current = index;
        saved = new Snapshot
        {
            Spawn = checkpoints[index].position,
            CartX = cart.X,
            CartY = cart.CurrentPosition.y,
            Braked = cart.Braked,
            Power = powerLever.IsOn,
        };
    }

    public IEnumerator Respawn()
    {
        actions.Freeze(true);
        yield return hud.FadeTo(1f, fadeOutSeconds);

        actions.ResetActions();
        hiding.ForceOut();
        cart.Restore(saved.CartX, saved.CartY, saved.Braked);
        powerLever.SetState(saved.Power);
        elevator.ResetToBottom(saved.Power);
        shaftLights.RestartCycle(true);
        if (saved.Power) shaftLights.SetSteady(true);
        foreach (SecurityCameraController camera in cameras)
            camera.ResetDetection();

        float soleOffset = capsule.center.y - capsule.height * 0.5f;
        Vector3 spawn = saved.Spawn;
        spawn.y = spawn.y - soleOffset + 0.03f;
        player.SetDoorwayWalk(true);
        body.position = spawn;
        player.transform.position = spawn;
        Physics.SyncTransforms();
        player.SetDoorwayWalk(false);
        var motion = player.GetComponent<CellMotion>();
        if (motion != null) motion.ResetFooting();

        yield return new WaitForSeconds(0.25f);
        yield return hud.FadeTo(0f, fadeInSeconds);
        actions.Freeze(false);
    }
}
