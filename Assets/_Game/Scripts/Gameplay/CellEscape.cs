using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CellEscape : MonoBehaviour
{
    [SerializeField] private SideViewPlayer player;
    [SerializeField] private Transform mouse;
    [SerializeField] private Transform key;
    [SerializeField] private Transform rod;
    [SerializeField] private Transform gate;
    [SerializeField] private Transform exit;
    [SerializeField] private AudioSource sound;

    private enum Stage { FindBread, MouseJumping, KeyBehindGate, KeyWithinReach, HaveKey, GateOpen, ExitOpen, Complete }

    private Stage stage;
    private bool haveRod;
    private bool moving;
    private Camera levelCamera;
    private Vector3 mouseHome;
    private AudioClip clang;
    private AudioClip step;
    private BreadSupply bread;

    private void Awake()
    {
        levelCamera = Camera.main;
        mouseHome = mouse.position;
        clang = MakeSound("Key clang", 0.38f, 840f, 1417f);
        step = MakeSound("Guard boot", 0.18f, 62f, 109f);
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

    public IEnumerator ReactToBread(Transform crumb, bool airborne)
    {
        if (stage != Stage.FindBread)
            yield break;

        if (!airborne)
        {
            Vector3 food = crumb.position;
            yield return Move(mouse, new Vector3(food.x, 0f, food.z), 0.65f);
            yield return new WaitForSeconds(0.18f);
            Destroy(crumb.gameObject);
            yield return Move(mouse, mouseHome, 0.55f);
            yield break;
        }

        stage = Stage.MouseJumping;
        Vector3 launch = new Vector3(key.position.x + 0.65f, 0f, key.position.z - 0.25f);
        yield return Move(mouse, launch, 0.5f);
        yield return Move(mouse, crumb.position - Vector3.up * 0.13f, 0.27f, 0.30f);
        Destroy(crumb.gameObject);
        yield return Move(mouse, key.position - Vector3.up * 0.12f, 0.19f, 0.12f);

        Vector3 landing = new Vector3(gate.position.x + 0.90f, 0.12f, 0.22f);
        StartCoroutine(Move(mouse, launch, 0.35f));
        yield return Move(key, landing, 0.46f, 0.05f);
        sound.PlayOneShot(clang, 0.55f);
        stage = Stage.KeyBehindGate;
        StartCoroutine(GuardApproaches());
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
        yield return Move(mouse, new Vector3(exit.position.x + 0.60f, 0f, mouseHome.z), 0.85f);
        mouse.gameObject.SetActive(false);
    }

    private void Update()
    {
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

    private void Interact()
    {
        if (moving)
            return;

        if (!haveRod && stage <= Stage.KeyBehindGate && Near(rod, 0.75f))
        {
            haveRod = true;
            rod.gameObject.SetActive(false);
            return;
        }

        if (stage == Stage.KeyBehindGate && haveRod && Near(gate, 1f))
            StartCoroutine(PullKey());
        else if (stage == Stage.KeyWithinReach && Near(key, 0.72f))
        {
            key.gameObject.SetActive(false);
            stage = Stage.HaveKey;
        }
        else if (stage == Stage.HaveKey && Near(gate, 1f))
            StartCoroutine(Open(gate, 100f, Stage.GateOpen));
        else if (stage == Stage.GateOpen && Near(exit, 1f))
            StartCoroutine(Open(exit, -100f, Stage.ExitOpen));
    }

    private IEnumerator PullKey()
    {
        moving = true;
        Vector3 hand = new Vector3(gate.position.x - 1.05f, 0.015f, key.position.z);
        rod.position = hand;
        rod.gameObject.SetActive(true);
        yield return Move(rod, hand + Vector3.right * 1.55f, 0.3f);
        StartCoroutine(Move(rod, hand, 0.4f));
        yield return Move(key, new Vector3(gate.position.x - 0.65f, 0.12f, key.position.z), 0.4f);
        rod.gameObject.SetActive(false);
        stage = Stage.KeyWithinReach;
        moving = false;
    }

    private IEnumerator Open(Transform door, float angle, Stage next)
    {
        moving = true;
        Collider blocker = door.GetComponent<Collider>();
        Quaternion closed = door.rotation;
        Quaternion open = Quaternion.AngleAxis(angle, Vector3.up) * closed;
        for (float elapsed = 0f; elapsed < 0.45f; elapsed += Time.deltaTime)
        {
            door.rotation = Quaternion.Slerp(closed, open, elapsed / 0.45f);
            yield return null;
        }

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
        else if (!moving && stage == Stage.HaveKey && Near(gate, 1f))
            prompt = "E - Unlock gate";
        else if (!moving && stage == Stage.KeyWithinReach && Near(key, 0.72f))
            prompt = "E - Pick up key";
        else if (!moving && stage == Stage.KeyBehindGate && Near(gate, 1f))
            prompt = haveRod ? "E - Hook the key" : "The rod is under the bunk.";
        else if (!haveRod && stage <= Stage.KeyBehindGate && Near(rod, 0.75f))
            prompt = "E - Take the rod";
        else if (bread != null && bread.IsHoldingPiece)
            prompt = "Hold left mouse to aim. Release to throw.";
        else if (bread != null && bread.IsNearTable() && stage == Stage.FindBread)
            prompt = bread.HasBread ? "E - Eat bread    Q - Take bread to throw" : "No bread left.  R - Restart";
        else if (bread != null && !bread.HasBread && !bread.ThrowInProgress && stage == Stage.FindBread)
            prompt = "No bread left.  R - Restart";
        else if (stage == Stage.FindBread && Near(gate, 1f))
            prompt = "Locked. Try throwing bread near the key.";

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

    private void OnDestroy()
    {
        Destroy(clang);
        Destroy(step);
    }
}
