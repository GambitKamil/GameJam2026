using UnityEngine;
using UnityEngine.SceneManagement;

// Night corridor: slip past the torch patrol, push the service cart onto the floor plate
// to hold the gate open, and leave through the kitchen door.
[DefaultExecutionOrder(50)]
public sealed class PassageEscape : MonoBehaviour
{
    [SerializeField] private SideViewPlayer player;
    [SerializeField] private PassagePatrol guard;
    [SerializeField] private ServiceCart cart;
    [SerializeField] private PlateGate gate;
    [SerializeField] private Transform exit;
    [SerializeField] private DetectionMeter meter;
    [SerializeField] private DetectionVignette vignette;
    [SerializeField] private float pushSpeed = 2.2f;
    [SerializeField] private float noisySpeed = 1.75f;
    [SerializeField] private float hearing = 10f;
    [SerializeField] private float exposureRise = 1.05f;
    [SerializeField] private float exposureFall = 1.6f;
    [SerializeField] private float suspicious = 0.38f;
    [SerializeField] private float closeRange = 2.4f;

    private CellMotion motion;
    private Rigidbody body;
    private Transform view;
    private Vector3 start;
    private Vector3 checkpoint;
    private float checkpointCart;
    private bool checkpointGate;
    private bool holding;
    private int side = 1;
    private bool escaped;
    private float exposure;
    private float lostTime;
    private float noteTime;
    private float heardCooldown;
    private string note;
    private bool sawStart;
    private bool everHeld;
    private GUIStyle caption;
    private GUIStyle warning;
    private float holdOffset;
    private readonly GUIContent content = new GUIContent();

    private float Grip => cart.HalfLength + 0.34f;

    private void Awake()
    {
        useGUILayout = false;
        motion = player.GetComponent<CellMotion>();
        body = player.GetComponent<Rigidbody>();
        if (Camera.main != null) view = Camera.main.transform;
        start = checkpoint = body.position;
        checkpointCart = cart.X;
        cart.IgnorePlayer(player.GetComponent<Collider>());
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (!SideViewPlayer.CanReadInput) return;
        if (lostTime > 0f)
        {
            lostTime -= dt;
            if (lostTime <= 0f) Restore();
            return;
        }
        if (escaped)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                checkpoint = start;
                checkpointCart = cart.Home.x;
                checkpointGate = false;
                Restore();
            }
            return;
        }

        noteTime -= dt;
        heardCooldown -= dt;
        if (Input.GetKeyDown(KeyCode.E)) ToggleCart();
        if (holding) motion.SetHandTarget(cart.Handle(side), 1f);

        UpdateExposure(dt);
        if (lostTime > 0f) return;

        if (gate.CartOnPlate && !checkpointGate)
        {
            checkpointGate = true;
            checkpoint = new Vector3(Mathf.Min(body.position.x, cart.X - Grip), start.y, start.z);
            checkpointCart = cart.X;
            Note("The gate holds   /   Checkpoint", 1.8f);
        }
        if (!sawStart && body.position.x > start.x + 3f) sawStart = true;
        if (body.position.x >= exit.position.x)
        {
            escaped = true;
            exposure = 0f;
            if (meter != null) meter.Level = 0f;
            if (vignette != null) vignette.Level = 0f;
            Release();
            Freeze(true);
            if (GameManager.Instance != null)
            {
                GameManager.Instance.CompleteCurrentLevel();
            }
            else if (Application.CanStreamedLevelBeLoaded(GameManager.KitchenScene))
            {
                SceneManager.LoadSceneAsync(GameManager.KitchenScene);
                enabled = false;
            }
        }
    }

    private void UpdateExposure(float dt)
    {
        bool crouched = player.Crouching;
        Vector3 target = player.transform.position + new Vector3(0f, crouched ? -0.06f : 0.44f, 0.20f);
        bool lit = guard.LightsPoint(target);
        // Standing right in front of him is never safe, even outside the beam.
        float toPlayer = player.transform.position.x - guard.transform.position.x;
        if (!crouched && Mathf.Abs(toPlayer) < closeRange && toPlayer * guard.Forward.x > 0f) lit = true;
        exposure = Mathf.MoveTowards(exposure, lit ? 1f : 0f, dt * (lit ? exposureRise : exposureFall));

        // He looks harder at a shape that keeps showing in the beam.
        if (lit && exposure >= suspicious && heardCooldown <= 0f)
        {
            guard.Investigate(target);
            heardCooldown = 1.2f;
        }

        // A cart shoved at a walk rattles; pushed slowly from a crouch it stays quiet.
        if (holding && cart.Speed > noisySpeed && heardCooldown <= 0f
            && Mathf.Abs(guard.transform.position.x - cart.X) < hearing)
        {
            guard.Investigate(cart.transform.position + Vector3.up * 0.5f);
            heardCooldown = 2.2f;
            Note("The cart rattles", 1.2f);
        }

        if (meter != null) meter.Level = exposure;
        if (vignette != null) vignette.Level = exposure;
        if (exposure >= 1f) Fail("Spotted");
        else if (body.position.y < -0.15f) Fail("Watch your footing");
    }

    private void FixedUpdate()
    {
        if (!holding || lostTime > 0f || escaped) return;

        // The cart sets the pace and stops at the ends of its track.
        Vector3 velocity = body.linearVelocity;
        velocity.x = Mathf.Clamp(velocity.x, -pushSpeed, pushSpeed);
        // Stop by velocity, not by moving the interpolated body, so the push never jitters at an end.
        float lo = cart.MinX - side * holdOffset;
        float hi = cart.MaxX - side * holdOffset;
        float x = body.position.x;
        float next = x + velocity.x * Time.fixedDeltaTime;
        if (next > hi) velocity.x = Mathf.Min(velocity.x, (hi - x) / Time.fixedDeltaTime);
        if (next < lo) velocity.x = Mathf.Max(velocity.x, (lo - x) / Time.fixedDeltaTime);
        body.linearVelocity = velocity;
    }

    private void LateUpdate()
    {
        if (holding) cart.MoveTo(player.transform.position.x + side * holdOffset);
    }

    private void ToggleCart()
    {
        if (holding)
        {
            Release();
            return;
        }
        if (!NearHandle(out int handleSide)) return;
        side = handleSide;
        // Keep the cart where it stands: a snap to the ideal grip would read as a loud shove.
        holdOffset = Mathf.Abs(cart.X - body.position.x);
        cart.ResetTo(cart.X);
        holding = everHeld = true;
        motion.Face(side);
        motion.SetHandTarget(cart.Handle(side), 1f);
    }

    private bool NearHandle(out int handleSide)
    {
        float dx = cart.X - body.position.x;
        handleSide = dx >= 0f ? 1 : -1;
        float distance = Mathf.Abs(dx);
        return distance > cart.HalfLength + 0.12f && distance < Grip + 0.55f;
    }

    private void Release()
    {
        if (!holding) return;
        holding = false;
        motion.EndAction();
    }

    private void Fail(string reason)
    {
        Release();
        Freeze(true);
        lostTime = 1.1f;
        if (meter != null) meter.Spotted();
        if (AudioManager.Instance != null) AudioManager.Instance.Play(AudioIds.Detected);
        Note(reason + "   /   Back to checkpoint", lostTime);
    }

    private void Restore()
    {
        escaped = false;
        lostTime = exposure = noteTime = 0f;
        heardCooldown = 0.5f;
        if (meter != null) meter.Level = 0f;
        if (vignette != null) vignette.Level = 0f;
        if (view != null) view.position += Vector3.right * (checkpoint.x - body.position.x);
        body.position = checkpoint;
        player.transform.position = checkpoint;
        Freeze(false);
        body.linearVelocity = Vector3.zero;
        motion.EndAction();
        cart.ResetTo(checkpointCart);
        gate.ResetGate(checkpointGate);
        // Send the patrol to the far end so the player gets a fresh window.
        guard.RestartAt(checkpointGate ? cart.MinX : cart.MaxX);
        Physics.SyncTransforms();
    }

    private void Freeze(bool value)
    {
        player.enabled = !value;
        if (!body.isKinematic) body.linearVelocity = Vector3.zero;
        body.isKinematic = value;
    }

    private void Note(string message, float duration) { note = message; noteTime = duration; }

    private string Prompt()
    {
        if (lostTime > 0f) return note;
        if (escaped) return GameManager.Instance == null ? "Corridor cleared     /     R   Play again" : null;
        if (holding && gate.CartOnPlate) return "E   Release cart to pass the gate";
        if (noteTime > 0f) return note;
        if (holding)
            return cart.X > cart.Home.x + 2.5f ? null : "Ctrl   Push quietly     E   Let go";
        if (NearHandle(out _)) return "E   Grab the cart";
        if (!sawStart) return "A / D   Move     Ctrl   Crouch in the shadows     E   Use";
        if (!gate.Pressed && !gate.CartOnPlate && Mathf.Abs(body.position.x - gate.PlateX) < 2.2f)
            return "The plate lifts the gate";
        return null;
    }

    private void OnGUI()
    {
        if (!SideViewPlayer.CanReadInput) return;
        if (caption == null)
        {
            caption = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            caption.normal.textColor = new Color(0.93f, 0.91f, 0.82f);
            warning = new GUIStyle(caption);
            warning.normal.textColor = new Color(1f, 0.75f, 0.42f);
        }
        float scale = Mathf.Clamp(Screen.height / 780f, 0.72f, 1.7f);
        caption.fontSize = warning.fontSize = Mathf.RoundToInt(16f * scale);
        string message = Prompt();
        if (message != null) Label(message, 0.88f, lostTime > 0f ? warning : caption, scale);
    }

    private void Label(string text, float y, GUIStyle style, float scale)
    {
        content.text = text;
        float width = style.CalcSize(content).x + 36f * scale;
        var rect = new Rect((Screen.width - width) * 0.5f, Screen.height * y, width, 34f * scale);
        Color previous = GUI.color;
        GUI.color = new Color(0.02f, 0.025f, 0.03f, 0.82f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
        GUI.Label(rect, text, style);
    }
}
