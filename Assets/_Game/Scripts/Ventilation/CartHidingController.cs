using System.Collections;
using UnityEngine;

// Lets the prisoner climb into the cart and pull the canvas over himself.
// While hidden he rides with the cart and cameras cannot see him.
public sealed class CartHidingController : MonoBehaviour
{
    [SerializeField] private CartController cart;
    [SerializeField] private SideViewPlayer player;
    [Tooltip("Canvas cover shown over the cart while someone is inside.")]
    [SerializeField] private GameObject cover;
    [SerializeField, Min(0.05f)] private float climbSeconds = 0.35f;
    [SerializeField, Min(0.1f)] private float exitClearance = 0.5f;
    [SerializeField] private AudioSource rustle;

    public bool IsHidden { get; private set; }
    public bool Busy => routine != null;

    private Rigidbody body;
    private CapsuleCollider capsule;
    private CellMotion motion;
    // Only the parts this script switched off; the animation rig inside the prisoner stays hidden.
    private readonly System.Collections.Generic.List<Renderer> hiddenParts = new System.Collections.Generic.List<Renderer>();
    private Coroutine routine;
    private int entrySide = -1;

    private float SoleOffset => capsule.center.y - capsule.height * 0.5f;

    private void Awake()
    {
        body = player.GetComponent<Rigidbody>();
        capsule = player.GetComponent<CapsuleCollider>();
        motion = player.GetComponent<CellMotion>();
        if (cover != null) cover.SetActive(false);
    }

    public bool CanEnter(float reach)
    {
        if (IsHidden || Busy || !player.IsGrounded)
            return false;
        float gap = Mathf.Abs(body.position.x - cart.X) - cart.HalfLength;
        return gap < reach;
    }

    public void Enter()
    {
        if (IsHidden || Busy) return;
        entrySide = body.position.x < cart.X ? -1 : 1;
        routine = StartCoroutine(ClimbIn());
    }

    public void Exit()
    {
        if (!IsHidden || Busy) return;
        routine = StartCoroutine(ClimbOut());
    }

    private IEnumerator ClimbIn()
    {
        player.SetDoorwayWalk(true);
        if (motion != null) motion.Face(cart.X - body.position.x);
        Vector3 start = body.position;
        for (float t = 0f; t < climbSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / climbSeconds);
            Vector3 seat = SeatPosition();
            Vector3 p = Vector3.Lerp(start, seat, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.45f;
            Place(p);
            yield return null;
        }
        SetVisible(false);
        IsHidden = true;
        if (cover != null) cover.SetActive(true);
        Play(0.9f);
        routine = null;
    }

    private IEnumerator ClimbOut()
    {
        float x = ExitX();
        Vector3 from = SeatPosition();
        float ground = GroundBelow(x, from.y + 1f);
        Vector3 to = new Vector3(x, ground - SoleOffset + 0.03f, from.z);
        if (cover != null) cover.SetActive(false);
        IsHidden = false;
        SetVisible(true);
        Play(1.1f);
        for (float t = 0f; t < climbSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / climbSeconds);
            Place(Vector3.Lerp(SeatPosition(), to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.4f);
            yield return null;
        }
        Place(to);
        player.SetDoorwayWalk(false);
        if (motion != null)
        {
            motion.EndAction();
            motion.ResetFooting();
        }
        routine = null;
    }

    private void LateUpdate()
    {
        if (IsHidden && !Busy)
            Place(SeatPosition());
    }

    private void FixedUpdate()
    {
        if (IsHidden && !Busy)
            Place(SeatPosition());
    }

    private Vector3 SeatPosition()
    {
        Vector3 seat = cart.Seat;
        return new Vector3(seat.x, seat.y - SoleOffset - 0.35f, body.position.z);
    }

    private void Place(Vector3 p)
    {
        body.position = p;
        player.transform.position = p;
    }

    // Climb out on the side he came in from, unless something blocks it.
    private float ExitX()
    {
        float preferred = cart.X + entrySide * (cart.HalfLength + exitClearance);
        if (SideIsClear(preferred)) return preferred;
        float other = cart.X - entrySide * (cart.HalfLength + exitClearance);
        return SideIsClear(other) ? other : preferred;
    }

    private bool SideIsClear(float x)
    {
        if (x < cart.MinX - 1.5f || x > cart.MaxX + 6f) return false;
        float ground = GroundBelow(x, cart.Seat.y + 1f);
        if (float.IsNegativeInfinity(ground) || ground < cart.Seat.y - 1.5f) return false;
        Vector3 bottom = new Vector3(x, ground + capsule.radius + 0.05f, body.position.z);
        Vector3 top = bottom + Vector3.up * Mathf.Max(0f, capsule.height - capsule.radius * 2f - 0.1f);
        foreach (Collider hit in Physics.OverlapCapsule(bottom, top, capsule.radius * 0.9f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.attachedRigidbody == body) continue;
            return false;
        }
        return true;
    }

    private static float GroundBelow(float x, float fromY)
    {
        return Physics.Raycast(new Vector3(x, fromY + 1f, 0f), Vector3.down, out RaycastHit hit, 6f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point.y : float.NegativeInfinity;
    }

    private void SetVisible(bool visible)
    {
        if (!visible)
        {
            hiddenParts.Clear();
            foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                r.enabled = false;
                hiddenParts.Add(r);
            }
            return;
        }

        foreach (Renderer r in hiddenParts)
            if (r != null) r.enabled = true;
        hiddenParts.Clear();
    }

    private void Play(float pitch)
    {
        if (rustle == null || rustle.clip == null) return;
        rustle.pitch = pitch;
        rustle.Play();
    }

    // Respawn: drop out of the cart immediately, wherever the checkpoint puts him.
    public void ForceOut()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        if (cover != null) cover.SetActive(false);
        IsHidden = false;
        SetVisible(true);
        if (motion != null) motion.ResetFooting();
    }
}
