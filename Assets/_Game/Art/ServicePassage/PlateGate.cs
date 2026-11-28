using UnityEngine;

// A floor plate that lifts the service gate while something heavy stands on it.
// The prisoner can test it with his own weight; only the cart keeps it down when he walks away.
public sealed class PlateGate : MonoBehaviour
{
    [SerializeField] private Transform plate;
    [SerializeField] private Transform tread;
    [SerializeField] private Transform leaf;
    [SerializeField] private BoxCollider blocker;
    [SerializeField] private Transform player;
    [SerializeField] private ServiceCart cart;
    [SerializeField] private float plateHalfWidth = 0.85f;
    [SerializeField] private float treadDepth = 0.03f;
    [SerializeField] private float liftHeight = 2.35f;
    [SerializeField] private float liftSpeed = 1.5f;
    [SerializeField] private float dropSpeed = 2.6f;

    private Vector3 treadRest;
    private Vector3 leafRest;
    private float open;
    private bool wasPressed;
    private bool wasMoving;

    public bool Pressed { get; private set; }
    public bool CartOnPlate { get; private set; }
    public float Openness => open;
    public float PlateX => plate.position.x;
    public float GateX => leaf.position.x;

    private void Awake()
    {
        treadRest = tread.localPosition;
        leafRest = leaf.localPosition;
    }

    public void ResetGate(bool cartHolds)
    {
        open = cartHolds ? 1f : 0f;
        Apply();
    }

    private void Update()
    {
        float plateX = plate.position.x;
        CartOnPlate = cart != null && Mathf.Abs(cart.X - plateX) <= plateHalfWidth - cart.HalfLength * 0.45f;
        bool standing = player != null && Mathf.Abs(player.position.x - plateX) <= plateHalfWidth - 0.1f
            && player.position.y < plate.position.y + 1.2f;
        Pressed = CartOnPlate || standing;
        if (Pressed != wasPressed)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.Play(AudioIds.PressurePlate);
            wasPressed = Pressed;
        }

        // Never close on the prisoner standing under the leaf.
        bool inDoorway = player != null && Mathf.Abs(player.position.x - leaf.position.x) < 0.75f;
        float goal = Pressed || (inDoorway && open > 0.05f) ? 1f : 0f;
        float before = open;
        open = Mathf.MoveTowards(open, goal, Time.deltaTime * (goal > open ? liftSpeed : dropSpeed) / liftHeight);
        bool moving = !Mathf.Approximately(before, open);
        if (moving && !wasMoving && AudioManager.Instance != null)
            AudioManager.Instance.Play(goal > before ? AudioIds.GateOpen : AudioIds.GateClose);
        wasMoving = moving;
        Apply();
    }

    private void Apply()
    {
        tread.localPosition = treadRest + Vector3.down * (Pressed ? treadDepth : 0f);
        float eased = open * open * (3f - 2f * open);
        leaf.localPosition = leafRest + Vector3.up * (liftHeight * eased);
        if (blocker != null) blocker.enabled = open < 0.72f;
    }
}
