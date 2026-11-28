using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(100)]
public sealed class CellEscape : MonoBehaviour
{
    [SerializeField] private SideViewPlayer player;
    [SerializeField] private Transform mouse;
    [SerializeField] private Transform key;
    [SerializeField] private Transform rod;
    [SerializeField] private Transform gate;
    [SerializeField] private Transform exit;
    [SerializeField] private AudioSource sound;

    private enum Stage { FindBread, MouseJumping, KeyBehindGate, KeyWithinReach, GateOpen, ExitOpen, Complete }

    private Stage stage;
    private bool haveRod;
    private bool moving;
    private Camera levelCamera;
    private Vector3 mouseHome;
    private AudioClip clang;
    private AudioClip step;
    private AudioClip scrape;
    private AudioClip latch;
    private Transform gateLock;
    private SideViewCamera followCamera;
    private BreadSupply bread;
    private CellMotion motion;
    private Transform mouseHead;
    private Transform mouseTail;
    private Quaternion mouseHeadRest;
    private Quaternion mouseTailRest;
    private float mouseFacing = 1f;
    private Vector3 catchRunStart;
    private Vector3 catchTakeoff;
    private Vector3 catchDestination;
    private float catchTime;
    private float catchJumpTime;
    private bool mouseReacting;
    private Vector3 wanderStart;
    private Vector3 wanderTarget;
    private float wanderTime;
    private float wanderDuration;
    private float nextWander;
    private bool rodInUse;

    private void Awake()
    {
        levelCamera = Camera.main;
        followCamera = levelCamera.GetComponent<SideViewCamera>();
        gateLock = gate.Find("Lock Plate");
        mouseHome = mouse.position;
        motion = player.GetComponent<CellMotion>();
        mouseHead = mouse.Find("Mouse Head");
        mouseTail = mouse.Find("Mouse Tail");
        mouseHeadRest = mouseHead.localRotation;
        mouseTailRest = mouseTail.localRotation;
        nextWander = Time.time + Random.Range(0.45f, 1.2f);
        clang = MakeSound("Key clang", 0.38f, 840f, 1417f);
        step = MakeSound("Guard boot", 0.18f, 62f, 109f);
        latch = MakeSound("Gate latch", 0.12f, 510f, 985f);
        scrape = MakeScrape();
    }

    private void Start()
    {
        bread = FindFirstObjectByType<BreadSupply>();
    }

    public bool HitsKey(Vector3 from, Vector3 to)
    {
        if (stage != Stage.FindBread)
            return false;

        var plane = new Plane(Vector3.forward, from);
        Ray ray = levelCamera.ScreenPointToRay(levelCamera.WorldToScreenPoint(key.position));
        if (!plane.Raycast(ray, out float distance))
            return false;

        Vector3 target = ray.GetPoint(distance);
        Vector3 segment = to - from;
        float projection = Mathf.Clamp01(Vector3.Dot(target - from, segment) / Mathf.Max(segment.sqrMagnitude, 0.00001f));
        return (from + segment * projection - target).sqrMagnitude <= 0.50f * 0.50f;
    }

    public void StartAirCatch(Vector3 intercept, float timeToCatch)
    {
        stage = Stage.MouseJumping;
        catchRunStart = mouse.position;
        catchTakeoff = new Vector3(intercept.x + 0.37f, 0f, intercept.z + 0.27f);
        catchDestination = intercept - Vector3.up * 0.14f + Vector3.right * 0.16f;
        catchTime = timeToCatch;
        catchJumpTime = Mathf.Min(0.33f, timeToCatch * 0.72f);
    }

    public void UpdateAirCatch(float elapsed)
    {
        float runTime = catchTime - catchJumpTime;
        if (elapsed < runTime)
        {
            FaceMouse(catchTakeoff.x - catchRunStart.x);
            SetMouseStride(catchRunStart, catchTakeoff, elapsed, runTime);
            return;
        }

        FaceMouse(catchDestination.x - catchTakeoff.x);
        SetMouseJump(catchTakeoff, catchDestination, Mathf.Clamp01((elapsed - runTime) / catchJumpTime), 0.25f);
    }

    public IEnumerator ReactToBread(Transform crumb, bool airborne)
    {
        if (!airborne)
        {
            if (stage != Stage.FindBread)
                yield break;

            mouseReacting = true;
            wanderDuration = 0f;
            Vector3 food = crumb.position;
            float nearGrate = gate.position.x + 0.58f;
            bool reachable = food.x >= nearGrate;
            Vector3 stop = new Vector3(
                Mathf.Max(nearGrate, food.x + Mathf.Sign(mouse.position.x - food.x) * 0.17f), 0f, food.z);
            yield return RunMouse(stop, RunDuration(stop));
            for (float elapsed = 0f; elapsed < 0.22f; elapsed += Time.deltaTime)
            {
                mouseHead.localRotation = mouseHeadRest * Quaternion.Euler(0f, 0f, Mathf.Sin(elapsed * 57f) * 15f);
                yield return null;
            }

            mouseHead.localRotation = mouseHeadRest;
            if (reachable)
                Destroy(crumb.gameObject);
            Vector3 retreat = new Vector3(Mathf.Clamp(food.x + 0.75f, gate.position.x + 0.9f, exit.position.x - 0.9f), 0f, mouseHome.z);
            yield return RunMouse(retreat, RunDuration(retreat));
            nextWander = Time.time + Random.Range(0.5f, 1.45f);
            mouseReacting = false;
            yield break;
        }

        Destroy(crumb.gameObject);
        Vector3 bump = key.position + Vector3.right * 0.15f - Vector3.up * 0.13f;
        yield return MouseJump(bump, 0.18f, 0.11f);
        Vector3 landing = new Vector3(bump.x + 0.38f, 0f, bump.z - 0.35f);
        StartCoroutine(MouseJump(landing, 0.37f, 0.12f));
        yield return DropKey();
        if (followCamera != null)
            followCamera.StopFramingSubject();
        stage = Stage.KeyBehindGate;
    }

    private IEnumerator DropKey()
    {
        Vector3 hang = key.position;
        const float ground = 0.105f;
        float height = hang.y;
        float speed = 0f;
        float spin = 170f;
        bool firstImpact = true;
        while (height > ground || speed > 0f)
        {
            float dt = Time.deltaTime;
            speed += Physics.gravity.y * dt;
            height += speed * dt;
            if (height <= ground)
            {
                height = ground;
                speed *= -0.34f;
                spin *= 0.4f;
                if (firstImpact)
                {
                    firstImpact = false;
                    sound.PlayOneShot(clang, 0.55f);
                    StartCoroutine(GuardApproaches());
                }
                if (speed < 0.50f)
                    speed = 0f;
            }

            key.position = new Vector3(hang.x, height, hang.z);
            key.Rotate(0f, 0f, spin * dt);
            yield return null;
        }
    }

    private float RunDuration(Vector3 destination)
    {
        return Mathf.Max(0.18f, Vector3.Distance(mouse.position, destination) / 6.3f);
    }

    private IEnumerator RunMouse(Vector3 destination, float duration)
    {
        Vector3 start = mouse.position;
        FaceMouse(destination.x - start.x);
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            SetMouseStride(start, destination, elapsed, duration);
            yield return null;
        }

        mouse.position = destination;
        mouse.rotation = Quaternion.identity;
        mouseTail.localRotation = mouseTailRest;
    }

    private void SetMouseStride(Vector3 start, Vector3 destination, float elapsed, float duration)
    {
        float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
        float gait = elapsed * 49f;
        mouse.position = Vector3.Lerp(start, destination, t) + Vector3.up * (Mathf.Abs(Mathf.Sin(gait)) * 0.052f);
        mouse.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(gait) * 6f);
        mouseTail.localRotation = mouseTailRest * Quaternion.Euler(0f, Mathf.Sin(gait) * 14f, 0f);
    }

    private IEnumerator MouseJump(Vector3 destination, float duration, float height)
    {
        Vector3 start = mouse.position;
        FaceMouse(destination.x - start.x);
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            SetMouseJump(start, destination, elapsed / duration, height);
            yield return null;
        }

        mouse.position = destination;
        mouse.rotation = Quaternion.identity;
    }

    private void SetMouseJump(Vector3 start, Vector3 destination, float t, float height)
    {
        mouse.position = Vector3.Lerp(start, destination, t) + Vector3.up * (4f * height * t * (1f - t));
        mouse.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-24f, 18f, t) * mouseFacing);
        mouseTail.localRotation = mouseTailRest;
    }

    private void FaceMouse(float direction)
    {
        if (Mathf.Abs(direction) < 0.05f)
            return;
        mouseFacing = Mathf.Sign(direction);
        mouse.localScale = new Vector3(-mouseFacing, 1f, 1f);
    }

    private IEnumerator GuardApproaches()
    {
        yield return new WaitForSeconds(0.30f);
        StartCoroutine(Flee());
        for (int i = 0; i < 7; i++)
        {
            sound.PlayOneShot(step, Mathf.Lerp(0.15f, 0.60f, i / 6f));
            yield return new WaitForSeconds(0.39f);
        }
    }

    private IEnumerator Flee()
    {
        Vector3 hideout = new Vector3(exit.position.x + 0.60f, 0f, mouseHome.z);
        yield return RunMouse(hideout, RunDuration(hideout));
        mouse.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (stage == Stage.FindBread && !mouseReacting)
            Wander();
        if (!SideViewPlayer.CanReadInput)
            return;

        if (GameManager.Instance == null && Input.GetKeyDown(KeyCode.R))
        {
            RestartPreview();
            return;
        }

        if (stage == Stage.ExitOpen && player.transform.position.x > exit.position.x + 0.45f)
        {
            stage = Stage.Complete;
            if (GameManager.Instance != null && Application.CanStreamedLevelBeLoaded(GameManager.CorridorScene))
                GameManager.Instance.CompleteCurrentLevel();
            return;
        }

        if (Input.GetKeyDown(KeyCode.E))
            Interact();
    }

    private void Wander()
    {
        if (wanderDuration > 0f)
        {
            wanderTime += Time.deltaTime;
            if (wanderTime < wanderDuration)
            {
                SetMouseStride(wanderStart, wanderTarget, wanderTime, wanderDuration);
                return;
            }

            mouse.position = wanderTarget;
            mouse.rotation = Quaternion.identity;
            wanderDuration = 0f;
            nextWander = Time.time + Random.Range(0.65f, 2.1f);
        }

        mouseHead.localRotation = mouseHeadRest * Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 7f) * 4.5f);
        mouseTail.localRotation = mouseTailRest * Quaternion.Euler(0f, Mathf.Sin(Time.time * 3.1f) * 9f, 0f);
        if (Time.time < nextWander)
            return;

        wanderStart = mouse.position;
        const float groundLane = 0.64f;
        var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, groundLane));
        float nearGrate = gate.position.x + 0.62f;
        float farSide = exit.position.x - 0.75f;
        float preferred = Mathf.Max(player.transform.position.x + 1.5f, nearGrate) + Random.Range(0.1f, 1.3f);
        Ray left = levelCamera.ViewportPointToRay(new Vector3(0.21f, 0.35f, 0f));
        Ray right = levelCamera.ViewportPointToRay(new Vector3(0.78f, 0.35f, 0f));
        if (plane.Raycast(left, out float leftDistance) && plane.Raycast(right, out float rightDistance))
            preferred = Mathf.Clamp(preferred, left.GetPoint(leftDistance).x, right.GetPoint(rightDistance).x);
        float x = Mathf.Clamp(preferred, nearGrate, farSide);
        wanderTarget = new Vector3(x, 0f, Random.Range(0.44f, 0.71f));
        float distance = Vector3.Distance(wanderStart, wanderTarget);
        wanderDuration = Mathf.Max(0.30f, distance / Mathf.Lerp(1.20f, 2.9f, Mathf.Clamp01(distance / 4f)));
        wanderTime = 0f;
        FaceMouse(wanderTarget.x - wanderStart.x);
    }

    private void LateUpdate()
    {
        if (haveRod && !rodInUse)
            CarryRod();
    }

    private void CarryRod()
    {
        float side = motion.Facing;
        Vector3 grip = motion.HandPosition;
        if (motion.OccupiesHand)
            grip = player.transform.position + new Vector3(-side * 0.20f, -0.03f, 0.24f);
        float tilt = motion.OccupiesHand ? 108f : 82f;
        rod.rotation = Quaternion.Euler(0f, 0f, side > 0f ? tilt : 180f - tilt);
        rod.position = grip - rod.TransformVector(new Vector3(-0.27f, 0f, 0f));
    }

    private void Interact()
    {
        if (moving || motion.Busy)
            return;

        if (!haveRod && stage <= Stage.KeyBehindGate && Near(rod, 0.75f))
        {
            StartCoroutine(PickUpRod());
            return;
        }

        if (stage == Stage.KeyBehindGate && haveRod && Near(gate, 1f))
            StartCoroutine(PullKey());
        else if (stage == Stage.KeyWithinReach && Near(key, 0.72f))
            StartCoroutine(PickUpKeyAndOpenGate());
        else if (stage == Stage.GateOpen && Near(exit, 1f))
            StartCoroutine(Open(exit, -100f, Stage.ExitOpen));
    }

    private IEnumerator PickUpRod()
    {
        moving = true;
        rodInUse = true;
        motion.Face(rod.position.x - player.transform.position.x);
        yield return motion.Reach(18f, 36f, 0.30f);
        yield return Move(rod, motion.HandPosition, 0.30f, 0.09f);
        Quaternion floorRotation = rod.rotation;
        StartCoroutine(motion.Reach(65f, 0f, 0.25f));
        for (float elapsed = 0f; elapsed < 0.28f; elapsed += Time.deltaTime)
        {
            rod.position = motion.HandPosition;
            rod.rotation = Quaternion.Slerp(floorRotation, Quaternion.Euler(0f, 0f, 52f), elapsed / 0.28f);
            yield return null;
        }
        haveRod = true;
        rodInUse = false;
        motion.SetCarryingRod(true);
        motion.EndAction();
        moving = false;
    }

    private IEnumerator PickUpKeyAndOpenGate()
    {
        moving = true;
        motion.Face(key.position.x - player.transform.position.x);
        yield return motion.Reach(-25f, 34f, 0.25f);
        yield return Move(key, motion.HandPosition, 0.26f, 0.10f);
        key.gameObject.SetActive(false);
        yield return motion.Reach(54f, 0f, 0.22f);
        for (float elapsed = 0f; elapsed < 0.22f; elapsed += Time.deltaTime)
        {
            motion.SetHandTarget(GateContact(), Mathf.SmoothStep(0f, 1f, elapsed / 0.22f));
            yield return null;
        }

        motion.SetHandTarget(GateContact(), 1f);
        sound.PlayOneShot(latch, 0.62f);
        yield return new WaitForSeconds(0.18f);
        yield return Open(gate, 100f, Stage.GateOpen);
        motion.EndAction();
    }

    private IEnumerator PullKey()
    {
        moving = true;
        rodInUse = true;
        motion.Face(1f);
        yield return motion.Reach(35f, 25f, 0.23f);
        Vector3 grip = motion.HandPosition;
        Vector3 keyOnFloor = key.position;
        Vector3 direction = (keyOnFloor - grip).normalized;
        Vector3 stowed = grip + direction * 0.44f;
        yield return TurnRodToward(stowed, direction);
        yield return SlideHook(stowed, keyOnFloor + Vector3.up * 0.17f, direction, 0.40f);
        yield return SlideHook(keyOnFloor + Vector3.up * 0.17f, keyOnFloor, direction, 0.19f);
        sound.PlayOneShot(latch, 0.32f);
        yield return new WaitForSeconds(0.25f);
        Vector3 retrieved = new Vector3(gate.position.x - 0.65f, keyOnFloor.y, 0.20f);
        Quaternion keyRest = key.rotation;
        const float dragDuration = 0.76f;
        sound.PlayOneShot(scrape, 0.62f);
        for (float elapsed = 0f; elapsed < dragDuration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / dragDuration);
            key.position = Vector3.Lerp(keyOnFloor, retrieved, t);
            key.rotation = keyRest * Quaternion.Euler(0f, 0f, Mathf.Sin(elapsed * 41f) * (1f - t) * 5f);
            PlaceHook(key.position, direction);
            yield return null;
        }

        key.SetPositionAndRotation(retrieved, keyRest);
        PlaceHook(retrieved, direction);
        yield return DiscardRod();
        rodInUse = false;
        stage = Stage.KeyWithinReach;
        motion.EndAction();
        moving = false;
    }

    private IEnumerator TurnRodToward(Vector3 tip, Vector3 direction)
    {
        Vector3 start = rod.position;
        Quaternion startRotation = rod.rotation;
        PlaceHook(tip, direction);
        Vector3 target = rod.position;
        Quaternion targetRotation = rod.rotation;
        yield return BlendRod(start, startRotation, target, targetRotation, 0.27f);
    }

    private IEnumerator DiscardRod()
    {
        haveRod = false;
        motion.SetCarryingRod(false);
        StartCoroutine(motion.Reach(-22f, 4f, 0.30f));
        Vector3 release = new Vector3(gate.position.x - 1.65f, 0.53f, -0.44f);
        Quaternion rotation = Quaternion.Euler(0f, 40f, 11f);
        yield return BlendRod(rod.position, rod.rotation, release, rotation, 0.23f);

        var shaft = rod.gameObject.AddComponent<BoxCollider>();
        shaft.size = new Vector3(0.81f, 0.05f, 0.075f);
        var rodBody = rod.gameObject.AddComponent<Rigidbody>();
        rodBody.mass = 0.85f;
        rodBody.linearDamping = 0.55f;
        rodBody.angularDamping = 1.7f;
        rodBody.interpolation = RigidbodyInterpolation.Interpolate;
        rodBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        Physics.IgnoreCollision(shaft, player.GetComponent<Collider>());
        rodBody.linearVelocity = new Vector3(-0.86f, 0.15f, -0.16f);
        rodBody.angularVelocity = new Vector3(0.35f, 0.7f, -1.5f);
        yield return new WaitForSeconds(0.30f);
    }

    private IEnumerator BlendRod(Vector3 start, Quaternion from, Vector3 end, Quaternion to, float duration)
    {
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            rod.SetPositionAndRotation(Vector3.Lerp(start, end, t), Quaternion.Slerp(from, to, t));
            yield return null;
        }

        rod.SetPositionAndRotation(end, to);
    }

    private IEnumerator SlideHook(Vector3 start, Vector3 finish, Vector3 direction, float duration)
    {
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            PlaceHook(Vector3.Lerp(start, finish, t), direction);
            yield return null;
        }

        PlaceHook(finish, direction);
    }

    private void PlaceHook(Vector3 tip, Vector3 direction)
    {
        rod.rotation = Quaternion.FromToRotation(Vector3.right, direction);
        rod.position = tip - rod.TransformVector(new Vector3(0.40f, 0.11f, 0f));
    }

    private Vector3 GateContact()
    {
        return gateLock.position - gate.right * 0.055f;
    }

    private IEnumerator Open(Transform door, float angle, Stage next)
    {
        moving = true;
        Collider blocker = door.GetComponent<Collider>();
        Quaternion closed = door.rotation;
        Quaternion open = Quaternion.AngleAxis(angle, Vector3.up) * closed;
        const float duration = 0.56f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = elapsed / duration;
            door.rotation = Quaternion.Slerp(closed, open, Mathf.SmoothStep(0f, 1f, t));
            if (door == gate)
                motion.SetHandTarget(GateContact(), 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.24f, 0.63f, t)));
            yield return null;
        }

        if (door == gate)
            motion.SetHandTarget(Vector3.zero, 0f);
        door.rotation = open;
        blocker.enabled = false;
        stage = next;
        moving = false;
    }

    private static IEnumerator Move(Transform target, Vector3 destination, float duration, float arc = 0f)
    {
        Vector3 start = target.position;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = elapsed / duration;
            target.position = Vector3.Lerp(start, destination, t) + Vector3.up * (4f * arc * t * (1f - t));
            yield return null;
        }

        target.position = destination;
    }

    private bool Near(Transform target, float distance)
    {
        return Mathf.Abs(player.transform.position.x - target.position.x) <= distance;
    }

    private static void RestartPreview()
    {
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

    private void OnGUI()
    {
        if (!SideViewPlayer.CanReadInput)
            return;

        string prompt = null;
        if (stage == Stage.Complete)
            prompt = "Cell complete.  R - Restart";
        else if (!moving && stage == Stage.GateOpen && Near(exit, 1f))
            prompt = "E - Open door";
        else if (!moving && stage == Stage.KeyWithinReach && Near(key, 0.72f))
            prompt = "E - Pick up key and unlock gate";
        else if (!moving && stage == Stage.KeyBehindGate && Near(gate, 1f))
            prompt = haveRod ? "E - Hook the key" : "The rod is under the bunk.";
        else if (!haveRod && stage <= Stage.KeyBehindGate && Near(rod, 0.75f))
            prompt = "E - Take the rod    Hold Ctrl - Crouch";
        else if (bread != null && bread.IsHoldingPiece)
            prompt = "Hold left mouse to aim. Release to throw.";
        else if (bread != null && bread.IsNearTable() && stage == Stage.FindBread)
            prompt = bread.HasBread ? "E - Eat bread    Q - Take bread to throw" : "No bread left.  R - Restart";
        else if (bread != null && !bread.HasBread && !bread.ThrowInProgress && stage == Stage.FindBread)
            prompt = "No bread left.  R - Restart";
        else if (stage == Stage.FindBread && Near(gate, 1f))
            prompt = "Locked. Aim at the key until the arc turns green.";

        if (prompt != null)
            GUI.Box(new Rect((Screen.width - 380) * 0.5f, Screen.height - 70, 380, 30), prompt);
    }

    private static AudioClip MakeSound(string title, float duration, float low, float high)
    {
        const int rate = 22050;
        var samples = new float[Mathf.RoundToInt(duration * rate)];
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / rate;
            float envelope = Mathf.Min(t * 800f, 1f) * Mathf.Exp(-8f * t / duration);
            samples[i] = envelope * (Mathf.Sin(t * low * 2f * Mathf.PI) + 0.45f * Mathf.Sin(t * high * 2f * Mathf.PI)) * 0.55f;
        }

        AudioClip clip = AudioClip.Create(title, samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioClip MakeScrape()
    {
        const int rate = 22050;
        var samples = new float[Mathf.RoundToInt(rate * 0.76f)];
        var grit = new System.Random(236);
        float resonance = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / rate;
            float noise = (float)grit.NextDouble() * 2f - 1f;
            resonance = Mathf.Lerp(resonance, noise, 0.22f);
            float envelope = Mathf.Min(t / 0.035f, 1f) * Mathf.Clamp01((0.76f - t) / 0.12f);
            float stutter = 0.62f + Mathf.Sin(t * 107f) * 0.28f;
            samples[i] = envelope * stutter * (resonance * 0.65f + noise * 0.11f + Mathf.Sin(t * 2780f) * 0.065f);
        }

        var clip = AudioClip.Create("Iron key scraping stone", samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDestroy()
    {
        Destroy(clang);
        Destroy(step);
        Destroy(scrape);
        Destroy(latch);
    }
}
