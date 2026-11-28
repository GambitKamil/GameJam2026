using System.Collections;
using UnityEngine;

// The prisoner's context actions in the ventilation shaft. One place decides what E, Q and F do,
// so a single key press never starts two actions:
//   E - pull a lever in reach, otherwise grab / let go of the cart
//   Q - climb into / out of the cart
//   F - set / release the cart brake (from beside it, while holding it or from inside)
// Also plays footsteps and runs the walk out of the freezer at the start.
[DefaultExecutionOrder(150)]
public sealed class VentilationPlayerActions : MonoBehaviour
{
    [SerializeField] private SideViewPlayer player;
    [SerializeField] private CartController cart;
    [SerializeField] private CartHidingController hiding;
    [SerializeField] private LeverController[] levers;
    [SerializeField] private VentilationHud hud;
    [SerializeField, Min(0.2f)] private float cartReach = 0.55f;

    [Header("Opening")]
    [SerializeField] private Transform freezerDoor;
    [SerializeField, Min(0f)] private float doorTravel = 2.7f;
    [SerializeField] private float walkOutToX = -0.6f;
    [SerializeField] private LevelEntrance entrance;
    [SerializeField] private AudioSource doorSound;

    [Header("Footsteps")]
    [SerializeField] private AudioSource footsteps;
    [SerializeField, Min(0.1f)] private float stepSeconds = 0.42f;

    private Rigidbody body;
    private CapsuleCollider capsule;
    private CellMotion motion;
    private bool frozen;
    private bool holding;
    private float gripOffset;
    private float stepClock;
    private Coroutine leverReach;
    private bool pushingBraked;
    private float brakeKnock;

    public bool Holding => holding;

    private void Awake()
    {
        body = player.GetComponent<Rigidbody>();
        capsule = player.GetComponent<CapsuleCollider>();
        motion = player.GetComponent<CellMotion>();
        cart.IgnoreForGround(player.GetComponentsInChildren<Collider>(true));
    }

    private void Start()
    {
        if (freezerDoor != null)
            StartCoroutine(WalkOutOfFreezer());
        else if (entrance != null)
            entrance.CompleteEntrance();
    }

    private IEnumerator WalkOutOfFreezer()
    {
        frozen = true;
        player.SetDoorwayWalk(true);
        Vector3 closed = freezerDoor.position;
        Vector3 open = closed + Vector3.up * doorTravel;
        if (doorSound != null && doorSound.clip != null) doorSound.Play();
        for (float t = 0f; t < 0.9f; t += Time.deltaTime)
        {
            freezerDoor.position = Vector3.Lerp(closed, open, Mathf.SmoothStep(0f, 1f, t / 0.9f));
            yield return null;
        }

        Vector3 start = body.position;
        Vector3 end = new Vector3(walkOutToX, start.y, start.z);
        if (motion != null) motion.Face(1f);
        float duration = Mathf.Abs(end.x - start.x) / 1.6f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            Vector3 p = Vector3.Lerp(start, end, t / duration);
            body.position = p;
            player.transform.position = p;
            yield return null;
        }
        body.position = end;
        player.transform.position = end;

        // The heavy door drops shut behind him.
        if (doorSound != null && doorSound.clip != null)
        {
            doorSound.pitch = 0.7f;
            doorSound.Play();
        }
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            float k = t / 0.35f;
            freezerDoor.position = Vector3.Lerp(open, closed, k * k);
            yield return null;
        }
        freezerDoor.position = closed;
        Physics.SyncTransforms();

        player.SetDoorwayWalk(false);
        frozen = false;
        if (entrance != null) entrance.CompleteEntrance();
    }

    private void Update()
    {
        if (frozen || !SideViewPlayer.CanReadInput)
        {
            hud.SetHint(string.Empty);
            return;
        }

        if (hiding.Busy)
        {
            hud.SetHint(string.Empty);
            return;
        }

        if (hiding.IsHidden)
        {
            if (Pressed(KeyCode.Q)) hiding.Exit();
            else if (Pressed(KeyCode.F)) cart.ToggleBrake();
            hud.SetHint("Q  Climb out     F  " + BrakeWord());
            return;
        }

        LeverController lever = LeverInReach();
        bool nearCart = holding || hiding.CanEnter(cartReach);

        if (Pressed(KeyCode.E))
        {
            if (lever != null && !holding) PullLever(lever);
            else if (holding) Release();
            else if (nearCart && player.IsGrounded) Grab();
        }
        else if (Pressed(KeyCode.Q) && nearCart)
        {
            if (holding) Release();
            hiding.Enter();
        }
        else if (Pressed(KeyCode.F) && nearCart)
        {
            cart.ToggleBrake();
        }

        hud.SetHint(Hint(lever, nearCart));
        Footsteps();
    }

    private string Hint(LeverController lever, bool nearCart)
    {
        if (lever != null && !holding)
            return "E  " + lever.DisplayName;
        if (holding)
            return cart.Braked
                ? (pushingBraked ? "Brake is on  -  F  Release brake" : "F  Release brake     E  Let go")
                : "E  Let go     Q  Hide     F  Brake";
        if (nearCart)
            return "E  Grab     Q  Hide     F  " + BrakeWord();
        return string.Empty;
    }

    // Same physical keys on a Russian layout (E/Q/F type the Cyrillic letters U, Short I and A),
    // where KeyCode can miss them on macOS.
    private static bool Pressed(KeyCode key)
    {
        if (Input.GetKeyDown(key))
            return true;
        string typed = Input.inputString;
        if (string.IsNullOrEmpty(typed))
            return false;
        switch (key)
        {
            case KeyCode.E: return typed.IndexOf('\u0443') >= 0 || typed.IndexOf('\u0423') >= 0;
            case KeyCode.Q: return typed.IndexOf('\u0439') >= 0 || typed.IndexOf('\u0419') >= 0;
            case KeyCode.F: return typed.IndexOf('\u0430') >= 0 || typed.IndexOf('\u0410') >= 0;
            default: return false;
        }
    }

    private string BrakeWord() => cart.Braked ? "Release brake" : "Brake";

    private LeverController LeverInReach()
    {
        if (!player.IsGrounded) return null;
        foreach (LeverController lever in levers)
            if (lever != null && lever.InReach(body.position)) return lever;
        return null;
    }

    private void PullLever(LeverController lever)
    {
        if (!lever.Pull()) return;
        if (motion != null)
        {
            motion.Face(lever.transform.position.x - body.position.x);
            if (leverReach != null) StopCoroutine(leverReach);
            leverReach = StartCoroutine(ReachFor(lever));
        }
    }

    private IEnumerator ReachFor(LeverController lever)
    {
        motion.PlayLibraryGesture(CellAnimationLibrary.Motion.Interact, 0.2f);
        for (float t = 0f; t < 0.45f; t += Time.deltaTime)
        {
            motion.SetHandTarget(lever.Grip, Mathf.Sin(Mathf.Clamp01(t / 0.45f) * Mathf.PI));
            yield return null;
        }
        motion.EndAction();
        leverReach = null;
    }

    private void Grab()
    {
        holding = true;
        cart.Held = true;
        gripOffset = body.position.x - cart.X;
        // Keep a small gap so the hands land on the handle, not inside the cart.
        float side = Mathf.Sign(gripOffset);
        gripOffset = side * Mathf.Max(Mathf.Abs(gripOffset), cart.HalfLength + 0.32f);
        player.JumpAllowed = false;
        SetCartContact(false);
        if (motion != null) motion.Face(-side);
    }

    private void Release()
    {
        if (!holding) return;
        holding = false;
        pushingBraked = false;
        cart.Held = false;
        player.JumpAllowed = true;
        SetCartContact(true);
        if (motion != null) motion.EndAction();
    }

    private void SetCartContact(bool on)
    {
        foreach (Collider c in cart.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(capsule, c, !on);
    }

    private void FixedUpdate()
    {
        if (!holding || body.isKinematic)
            return;

        float dt = Time.fixedDeltaTime;
        Vector3 velocity = body.linearVelocity;
        // Uphill is slower; downhill the cart wants to run away a little.
        bool uphill = velocity.x * cart.Slope > 0.01f;
        float limit = uphill ? cart.UphillSpeed : cart.PushSpeed;
        velocity.x = Mathf.Clamp(velocity.x, -limit, limit);
        pushingBraked = cart.Braked && Mathf.Abs(player.MoveInput) > 0.1f;
        if (cart.Braked)
        {
            velocity.x = 0f;
            // Straining against the brake: a dull knock now and then so it reads as "locked", not broken.
            brakeKnock -= dt;
            if (pushingBraked && brakeKnock <= 0f && doorSound != null && doorSound.clip != null)
            {
                doorSound.pitch = 1.8f;
                doorSound.PlayOneShot(doorSound.clip, 0.18f);
                brakeKnock = 0.9f;
            }
        }

        float wanted = body.position.x + velocity.x * dt - gripOffset;
        float cartX = cart.DriveTo(wanted);
        velocity.x = (cartX + gripOffset - body.position.x) / dt;
        body.linearVelocity = velocity;

        // Falling or being pushed off the slope breaks the grip.
        if (!player.IsGrounded && body.linearVelocity.y < -1.5f)
            Release();
    }

    private void LateUpdate()
    {
        if (holding && motion != null)
            motion.SetHandTarget(cart.Handle(gripOffset < 0f ? -1 : 1), 0.9f);
    }

    private void Footsteps()
    {
        if (footsteps == null || !player.IsGrounded || body.isKinematic)
        {
            stepClock = 0f;
            return;
        }
        float speed = Mathf.Abs(body.linearVelocity.x);
        if (speed < 0.4f)
        {
            stepClock = stepSeconds * 0.6f;
            return;
        }
        float interval = stepSeconds * (player.Running ? 0.72f : player.Crouching ? 1.35f : 1f);
        stepClock += Time.deltaTime;
        if (stepClock < interval) return;
        stepClock = 0f;
        footsteps.pitch = Random.Range(0.85f, 1.1f);
        AudioClip clip = footsteps.clip != null ? footsteps.clip : VentilationAudio.Get(VentilationAudio.Cue.Step);
        footsteps.PlayOneShot(clip, player.Crouching ? 0.25f : player.Running ? 0.75f : 0.5f);
    }

    // Alarms and the lift ride take the controls away.
    public void Freeze(bool on)
    {
        frozen = on;
        if (on)
        {
            Release();
            hud.SetHint(string.Empty);
        }
    }

    public void ResetActions()
    {
        Release();
        if (leverReach != null)
        {
            StopCoroutine(leverReach);
            leverReach = null;
        }
        if (motion != null) motion.EndAction();
    }
}
