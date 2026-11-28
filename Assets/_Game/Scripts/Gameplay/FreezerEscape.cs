using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(150)]
public sealed class FreezerEscape : MonoBehaviour
{
    private enum Phase { Kitchen, Trapped, DoorOpen, Frozen, Escaped }

    [SerializeField] private SideViewPlayer player;
    [SerializeField] private KitchenPassageDoor entranceDoor;
    [SerializeField] private Transform freezerDoor;
    [SerializeField] private Transform brokenRelease;
    [SerializeField] private Transform leverHandle;
    [SerializeField] private BoxCollider shortCrate;
    [SerializeField] private BoxCollider tallCrate;
    [SerializeField] private BoxCollider shelf;
    [SerializeField] private Light leverLight;
    [SerializeField] private Transform cookArm;
    [SerializeField] private ParticleSystem breath;
    [SerializeField] private AudioSource kitchenSound;
    [SerializeField] private AudioSource freezerSound;
    [SerializeField] private AudioSource mechanismSound;
    [SerializeField] private AudioSource crateSound;
    [SerializeField] private UnityEngine.UI.RawImage frost;
    [SerializeField] private UnityEngine.UI.Text clockLabel;
    [SerializeField] private UnityEngine.UI.Text hintLabel;

    [SerializeField, Min(8f)] private float secondsBeforeFreezing = 46f;
    [SerializeField, Range(0.2f, 1f)] private float minimumWalkSpeed = 0.48f;
    [SerializeField, Min(1f)] private float doorTravel = 2.8f;
    [SerializeField, Min(0.3f)] private float maximumClimb = 0.77f;
    [SerializeField, Min(0.5f)] private float crateSpeed = 2.05f;

    private Rigidbody body;
    private CapsuleCollider capsule;
    private CellMotion motion;
    private Transform torso;
    private BoxCollider[] ledges;
    private BoxCollider heldCrate;
    private Phase phase;
    private float remaining;
    private float cold;
    private float closedDoorHeight;
    private float shortCrateStart;
    private float tallCrateStart;
    private float gripOffset;
    private float feedbackUntil;
    private string feedback;
    private Coroutine climb;
    private Coroutine leverPull;
    private bool resetting;
    private bool arriving;

    private bool ColdActive => phase is Phase.Trapped or Phase.DoorOpen;
    private float SoleOffset => capsule.center.y - capsule.height * 0.5f;
    private float Feet => body.position.y + SoleOffset;
    private float Doorway => freezerDoor.position.x;

    private void Awake()
    {
        body = player.GetComponent<Rigidbody>();
        capsule = player.GetComponent<CapsuleCollider>();
        motion = player.GetComponent<CellMotion>();
        ledges = new[] { shortCrate, tallCrate, shelf };
        shortCrateStart = shortCrate.transform.position.x;
        tallCrateStart = tallCrate.transform.position.x;
        closedDoorHeight = freezerDoor.position.y;
        remaining = secondsBeforeFreezing;
        SetDoorHeight(closedDoorHeight + doorTravel);
        freezerSound.volume = 0f;
        crateSound.volume = 0f;
    }

    private void Start()
    {
        Transform[] playerParts = player.GetComponentsInChildren<Transform>();
        torso = System.Array.Find(playerParts, part => part.name == "Torso at Hips");
        if (!arriving)
            entranceDoor.GetComponent<LevelEntrance>().CompleteEntrance();
    }

    internal void EnterFromCorridor()
    {
        StartCoroutine(WalkIntoKitchen());
    }

    private IEnumerator WalkIntoKitchen()
    {
        arriving = true;
        yield return entranceDoor.Arrive(player);
        arriving = false;
        entranceDoor.GetComponent<LevelEntrance>().CompleteEntrance();
    }

    private void Update()
    {
        if (resetting || arriving)
            return;

        if (Input.GetKeyDown(KeyCode.R) && phase != Phase.Kitchen && GameManager.Instance == null)
        {
            Restart();
            return;
        }

        if (SideViewPlayer.CanReadInput)
        {
            Advance(Time.deltaTime);
            if (phase == Phase.Trapped && climb == null && leverPull == null)
            {
                if (Input.GetKeyDown(KeyCode.E))
                    Use();
                if (heldCrate == null && Input.GetKeyDown(KeyCode.Space))
                    TryClimb();
            }
        }

        UpdatePresentation();
    }

    private void Advance(float delta)
    {
        if (phase == Phase.Kitchen && body.position.x >= Doorway + 0.68f)
        {
            phase = Phase.Trapped;
            mechanismSound.Play();
            Show("Door locked   /   Find the emergency release", 2.5f);
        }

        if (phase == Phase.DoorOpen && body.position.x <= Doorway - 0.65f)
        {
            phase = Phase.Escaped;
            SetPlayerActive(false);
            if (GameManager.Instance != null)
                GameManager.Instance.CompleteCurrentLevel();
            return;
        }

        if (!ColdActive)
            return;

        remaining = Mathf.Max(0f, remaining - delta);
        cold = 1f - remaining / secondsBeforeFreezing;
        float desiredHeight = closedDoorHeight + (phase == Phase.DoorOpen ? doorTravel : 0f);
        SetDoorHeight(Mathf.MoveTowards(freezerDoor.position.y, desiredHeight, delta * 3.5f));
        if (remaining > 0f)
            return;

        phase = Phase.Frozen;
        if (climb != null)
            StopCoroutine(climb);
        if (leverPull != null)
            StopCoroutine(leverPull);
        ReleaseCrate();
        motion.EndAction();
        SetPlayerActive(false);
    }

    private void FixedUpdate()
    {
        if (!ColdActive || body.isKinematic || !SideViewPlayer.CanReadInput)
            return;

        Vector3 velocity = body.linearVelocity;
        float multiplier = Mathf.Lerp(1f, minimumWalkSpeed, Mathf.SmoothStep(0f, 1f, cold));
        float limit = (heldCrate == null ? player.WalkSpeed : crateSpeed) * multiplier;
        velocity.x = Mathf.Clamp(velocity.x, -limit, limit);
        if (heldCrate != null)
        {
            float step = Time.fixedDeltaTime;
            float desiredCrateX = body.position.x + velocity.x * step - gripOffset;
            float crateX = MoveCrate(desiredCrateX);
            velocity.x = (crateX + gripOffset - body.position.x) / step;
        }
        body.linearVelocity = velocity;
    }

    private void LateUpdate()
    {
        if (Time.deltaTime <= 0f)
            return;

        if (torso != null && Phase.Kitchen != phase && Phase.Escaped != phase)
        {
            float shiver = phase == Phase.Frozen ? -9f :
                (Mathf.Sin(Time.time * 42f) + 0.32f * Mathf.Sin(Time.time * 67f)) * Mathf.Lerp(0.55f, 2.8f, cold);
            torso.localRotation *= Quaternion.Euler(0f, shiver * 0.6f, shiver);
        }

        if (heldCrate != null)
        {
            Bounds box = heldCrate.bounds;
            float handX = box.center.x + Mathf.Sign(gripOffset) * (box.extents.x - 0.04f);
            motion.SetHandTarget(new Vector3(handX, box.max.y - 0.08f, body.position.z - 0.14f), 0.92f);
        }

        if (cookArm != null)
        {
            float beat = Time.time * 2.2f;
            cookArm.localRotation = Quaternion.Euler(7f * Mathf.Sin(beat), 9f * Mathf.Cos(beat), 13f * Mathf.Sin(beat * 1.6f));
        }
    }

    private void Use()
    {
        if (heldCrate != null)
        {
            ReleaseCrate();
            return;
        }

        if (CanPullLever())
        {
            leverPull = StartCoroutine(PullLever());
            return;
        }

        if (NearBrokenRelease())
        {
            Show("Release jammed   /   The emergency lever is above the shelves", 3.2f);
            return;
        }

        BoxCollider target = CrateInReach();
        if (target == null)
            return;

        heldCrate = target;
        gripOffset = body.position.x - target.bounds.center.x;
        motion.Face(-gripOffset);
        Physics.IgnoreCollision(capsule, heldCrate, true);
    }

    private float MoveCrate(float desired)
    {
        float separation = shortCrate.bounds.extents.x + tallCrate.bounds.extents.x + 0.055f;
        float tallLimit = shelf.bounds.min.x - tallCrate.bounds.extents.x - 0.12f;
        if (heldCrate == shortCrate)
        {
            desired = Mathf.Clamp(desired, shortCrateStart - 1.15f, tallLimit - separation);
            // The low crate can push the tall one ahead of it when approached from the left.
            if (gripOffset < 0f && desired + separation > tallCrate.transform.position.x)
                PlaceCrate(tallCrate, desired + separation);
            desired = Mathf.Min(desired, tallCrate.transform.position.x - separation);
        }
        else
        {
            float lowerLimit = Mathf.Max(tallCrateStart - 1.6f, shortCrate.transform.position.x + separation);
            desired = Mathf.Clamp(desired, lowerLimit, tallLimit);
        }

        PlaceCrate(heldCrate, desired);
        Physics.SyncTransforms();
        return desired;
    }

    private static void PlaceCrate(BoxCollider crate, float x)
    {
        Vector3 position = crate.transform.position;
        position.x = x;
        crate.transform.position = position;
    }

    private void ReleaseCrate()
    {
        if (heldCrate == null)
            return;

        Physics.IgnoreCollision(capsule, heldCrate, false);
        heldCrate = null;
        motion.EndAction();
    }

    private BoxCollider CrateInReach()
    {
        if (!player.IsGrounded || Feet > 0.16f)
            return null;

        BoxCollider closest = null;
        float closestGap = 0.60f;
        foreach (BoxCollider crate in ledges)
        {
            if (crate == shelf)
                continue;
            Bounds box = crate.bounds;
            float distance = Mathf.Abs(body.position.x - box.center.x) - box.extents.x;
            if (distance < 0.16f || distance >= closestGap)
                continue;

            closest = crate;
            closestGap = distance;
        }
        return closest;
    }

    private bool TryFindLedge(out Vector3 landing, out Vector3 handhold)
    {
        landing = handhold = default;
        if (!player.IsGrounded)
            return false;

        Bounds chosen = default;
        float best = 0.73f;
        foreach (BoxCollider ledge in ledges)
        {
            Bounds box = ledge.bounds;
            float rise = box.max.y - Feet;
            float gap = Mathf.Abs(body.position.x - box.center.x) - box.extents.x;
            if (rise < 0.13f || rise > maximumClimb || gap < -0.035f || gap >= best)
                continue;

            chosen = box;
            best = gap;
        }
        if (chosen.size == Vector3.zero)
            return false;

        bool fromLeft = body.position.x < chosen.center.x;
        float edge = fromLeft ? chosen.min.x : chosen.max.x;
        float inset = Mathf.Min(0.34f, chosen.extents.x * 0.8f) * (fromLeft ? 1 : -1);
        landing = new Vector3(edge + inset, chosen.max.y - SoleOffset + 0.025f, body.position.z);
        handhold = new Vector3(edge + inset * 0.3f, chosen.max.y, body.position.z - 0.15f);
        return true;
    }

    private void TryClimb()
    {
        if (TryFindLedge(out Vector3 landing, out Vector3 handhold))
            climb = StartCoroutine(Climb(landing, handhold));
    }

    private IEnumerator Climb(Vector3 landing, Vector3 handhold)
    {
        Vector3 start = body.position;
        SetPlayerActive(false);
        body.detectCollisions = false;
        motion.Face(landing.x - start.x);
        motion.SetHandTarget(handhold, 1f);
        float duration = Mathf.Lerp(0.49f, 0.88f, cold);
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float progress = elapsed / duration;
            float raise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.74f));
            float across = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.37f, 1f, progress));
            body.position = new Vector3(Mathf.Lerp(start.x, landing.x, across),
                Mathf.Lerp(start.y, landing.y, raise) + Mathf.Sin(progress * Mathf.PI) * 0.06f, start.z);
            yield return null;
        }
        body.position = landing;
        body.detectCollisions = true;
        SetPlayerActive(true);
        motion.EndAction();
        climb = null;
    }

    private IEnumerator PullLever()
    {
        SetPlayerActive(false);
        motion.Face(leverHandle.position.x - body.position.x);
        motion.SetHandTarget(leverHandle.TransformPoint(Vector3.up * 0.26f), 1f);
        Quaternion start = leverHandle.localRotation;
        Quaternion pulled = start * Quaternion.Euler(0f, 0f, -75f);
        const float duration = 0.42f;
        mechanismSound.Play();
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            leverHandle.localRotation = Quaternion.Slerp(start, pulled, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            yield return null;
        }
        leverHandle.localRotation = pulled;
        leverLight.color = new Color(1f, 0.74f, 0.43f);
        leverLight.intensity *= 0.55f;
        phase = Phase.DoorOpen;
        Show("Door open   /   Back to the kitchen", 3f);
        motion.EndAction();
        SetPlayerActive(true);
        leverPull = null;
    }

    private bool CanPullLever() => Mathf.Abs(body.position.x - leverHandle.position.x) < 0.85f &&
        Feet >= shelf.bounds.max.y - 0.20f;

    private bool NearBrokenRelease() => Mathf.Abs(body.position.x - brokenRelease.position.x) < 0.84f;

    private void SetPlayerActive(bool active)
    {
        if (!body.isKinematic)
            body.linearVelocity = Vector3.zero;
        body.isKinematic = !active;
        player.enabled = active;
    }

    private void SetDoorHeight(float height)
    {
        Vector3 position = freezerDoor.position;
        position.y = height;
        freezerDoor.position = position;
    }

    private void Show(string message, float duration)
    {
        feedback = message;
        feedbackUntil = Time.time + duration;
    }

    private string Hint()
    {
        if (phase == Phase.Frozen)
            return "TOO COLD   /   R  Try again";
        if (phase == Phase.Escaped)
            return GameManager.Instance == null ? "FREEZER ESCAPED   /   R  Replay" : string.Empty;
        if (phase == Phase.Kitchen)
            return body.position.x < Doorway - 6f ? "A / D  Move" : string.Empty;
        if (Time.time < feedbackUntil)
            return feedback;
        if (phase == Phase.DoorOpen)
            return "←  Back to the kitchen";
        if (climb != null || leverPull != null)
            return string.Empty;
        if (heldCrate != null)
            return CratesAtShelf() ? "E  Let go, then SPACE  Climb" : "A / D  Move crates toward the shelves";
        if (CanPullLever())
            return "E  Pull emergency lever";
        if (NearBrokenRelease())
            return "E  Check the door release";
        if (TryFindLedge(out _, out _))
        {
            if (Feet < 0.16f && !CratesAtShelf() && CrateInReach() != null)
                return "E  Move the crates toward the shelves";
            return "SPACE  Climb";
        }
        return CrateInReach() != null ? "E  Move the crates toward the shelves" : "Find the emergency lever above the shelves";
    }

    private bool CratesAtShelf()
    {
        Bounds tall = tallCrate.bounds;
        return shelf.bounds.min.x - tall.max.x < 0.24f && tall.min.x - shortCrate.bounds.max.x < 0.24f;
    }

    private void UpdatePresentation()
    {
        float targetFrost = ColdActive ? 0.14f + cold * 0.78f : phase == Phase.Frozen ? 1f : 0f;
        Color tint = frost.color;
        tint.a = Mathf.MoveTowards(tint.a, targetFrost, Time.deltaTime * 1.6f);
        frost.color = tint;
        int seconds = Mathf.CeilToInt(remaining);
        string clock = ColdActive ? $"COLD   {seconds / 60:00}:{seconds % 60:00}" : string.Empty;
        if (clockLabel.text != clock)
            clockLabel.text = clock;
        clockLabel.color = seconds <= 10 ? new Color(0.94f, 0.69f, 0.47f) : new Color(0.73f, 0.84f, 0.91f);
        clockLabel.transform.parent.gameObject.SetActive(clock.Length != 0);
        string hint = Hint();
        if (hintLabel.text != hint)
            hintLabel.text = hint;
        hintLabel.transform.parent.gameObject.SetActive(hint.Length != 0);

        var audio = AudioManager.Instance;
        float volume = audio == null ? 1f : audio.SfxVolume;
        bool muted = audio != null && audio.IsMuted;
        kitchenSound.mute = freezerSound.mute = crateSound.mute = mechanismSound.mute = muted;
        mechanismSound.volume = 0.6f * volume;
        float freezerMix = phase is Phase.Kitchen or Phase.Escaped ? 0f : 1f;
        freezerSound.volume = Mathf.MoveTowards(freezerSound.volume, freezerMix * 0.29f * volume, Time.unscaledDeltaTime * 0.65f);
        kitchenSound.volume = Mathf.MoveTowards(kitchenSound.volume, (1f - freezerMix) * 0.3f * volume, Time.unscaledDeltaTime * 0.85f);
        float slide = heldCrate != null && !body.isKinematic ? Mathf.Abs(body.linearVelocity.x) : 0f;
        crateSound.volume = Mathf.MoveTowards(crateSound.volume,
            Mathf.Clamp01(slide / crateSpeed) * 0.25f * volume, Time.unscaledDeltaTime * 2.8f);
        if (breath != null)
        {
            var emission = breath.emission;
            emission.enabled = ColdActive;
            breath.transform.localPosition = new Vector3(motion.Facing * 0.17f, 0.68f, -0.15f);
        }
    }

    private void Restart()
    {
        resetting = true;
        Scene scene = SceneManager.GetActiveScene();
        #if UNITY_EDITOR
        if (scene.buildIndex < 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
            return;
        }
        #endif
        SceneManager.LoadScene(scene.buildIndex);
    }
}
