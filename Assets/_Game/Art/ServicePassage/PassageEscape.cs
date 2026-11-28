using UnityEngine;

[DefaultExecutionOrder(50)]
public sealed class PassageEscape : MonoBehaviour
{
    private enum Stage { FindKey, UnlockCabinet, CarryPlank, Cross, Escaped }
    [SerializeField] private SideViewPlayer player;
    [SerializeField] private PassagePatrol guard;
    [SerializeField] private Transform key;
    [SerializeField] private Transform door;
    [SerializeField] private Transform plank;
    [SerializeField] private Transform crossing;
    [SerializeField] private Transform exit;
    [SerializeField] private float cabinetX;
    [SerializeField] private float nearEdge;

    private CellMotion motion;
    private Rigidbody body;
    private Collider plankCollider;
    private Vector3 storedPosition;
    private Quaternion storedRotation;
    private Vector3 closedDoor;
    private Transform view;
    private Vector3 start;
    private Stage stage;
    private Stage savedStage;
    private Vector3 checkpoint;
    private float exposure;
    private float lostTime;
    private float actionTime;
    private float actionDuration;
    private float noteTime;
    private string note;
    private bool sawStart;
    private bool crossed;
    private Quaternion carryTilt;
    private Vector3 placementStart;
    private Quaternion placementRotation;
    private GUIStyle caption;
    private GUIStyle warning;

    private void Awake()
    {
        motion = player.GetComponent<CellMotion>();
        body = player.GetComponent<Rigidbody>();
        plankCollider = plank.GetComponent<Collider>();
        storedPosition = plank.position;
        storedRotation = plank.rotation;
        closedDoor = door.localPosition;
        if (Camera.main != null) view = Camera.main.transform;
        start = checkpoint = body.position;
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
        if (stage == Stage.Escaped)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                checkpoint = start;
                savedStage = Stage.FindKey;
                Restore();
            }
            return;
        }

        Vector3 target = player.transform.position + new Vector3(0f, player.Crouching ? -0.06f : 0.44f, 0.20f);
        bool lit = guard.LightsPoint(target);
        exposure = Mathf.MoveTowards(exposure, lit ? 1f : 0f, dt * (lit ? 1.65f : 2.3f));
        if (exposure >= 1f || body.position.y < -0.15f)
        {
            Fail(exposure >= 1f ? "Spotted" : "Watch your footing");
            return;
        }

        noteTime -= dt;
        if (actionTime > 0f)
        {
            actionTime = Mathf.Max(0f, actionTime - dt);
            float t = Mathf.SmoothStep(0f, 1f, 1f - actionTime / actionDuration);
            if (stage == Stage.UnlockCabinet) SetDoor(t);
            if (stage == Stage.CarryPlank)
            {
                plank.position = Vector3.Lerp(placementStart, crossing.position, t) + Vector3.up * (0.24f * Mathf.Sin(t * Mathf.PI));
                plank.rotation = Quaternion.Slerp(placementRotation, crossing.rotation, t);
            }
            if (actionTime <= 0f) FinishAction();
            return;
        }

        if (Input.GetKeyDown(KeyCode.E)) TryInteract();
        if (stage == Stage.Cross && !crossed && body.position.x > crossing.position.x + 2.4f)
        {
            crossed = true;
            Save(Stage.Cross, crossing.position.x + 2.6f);
        }
        if (stage == Stage.Cross && body.position.x >= exit.position.x)
        {
            stage = Stage.Escaped;
            Freeze(true);
            if (GameManager.Instance != null) GameManager.Instance.CompleteCurrentLevel();
        }
        if (!sawStart && body.position.x > start.x + 4.5f) sawStart = true;
    }

    private void LateUpdate()
    {
        if (stage != Stage.CarryPlank || actionTime > 0f) return;
        // Keep the board on the camera side of the body, balanced against the forearm.
        carryTilt = Quaternion.Euler(0f, 0f, motion.Facing * 15f);
        plank.SetPositionAndRotation(motion.HandPosition + new Vector3(0f, 0.05f, -0.18f), carryTilt);
    }

    private void TryInteract()
    {
        if (stage == Stage.FindKey && Near(key.position.x, 0.88f))
            BeginAction(key.position, 0.46f, CellAnimationLibrary.Motion.TablePickup);
        else if (stage == Stage.UnlockCabinet && Near(cabinetX, 0.96f))
            BeginAction(new Vector3(cabinetX, 1.10f, 0.5f), 0.78f, CellAnimationLibrary.Motion.Interact);
        else if (stage == Stage.CarryPlank && body.position.x >= nearEdge - 1.18f && body.position.x <= nearEdge + 0.2f)
        {
            placementStart = plank.position;
            placementRotation = plank.rotation;
            BeginAction(new Vector3(nearEdge + 0.2f, 0.23f, 0f), 0.78f, CellAnimationLibrary.Motion.FloorPickup);
        }
    }

    private void BeginAction(Vector3 target, float duration, CellAnimationLibrary.Motion gesture)
    {
        actionDuration = actionTime = duration;
        Freeze(true);
        motion.Face(target.x - body.position.x);
        motion.PlayLibraryGesture(gesture, duration);
        motion.SetHandTarget(target, 1f);
    }

    private void FinishAction()
    {
        Freeze(false);
        motion.EndAction();
        switch (stage)
        {
            case Stage.FindKey:
                stage = Stage.UnlockCabinet;
                key.gameObject.SetActive(false);
                Save(stage, key.position.x);
                break;
            case Stage.UnlockCabinet:
                stage = Stage.CarryPlank;
                SetDoor(1f);
                motion.SetCarryingRod(true);
                Note("Take the plank to the missing floor", 2f);
                break;
            case Stage.CarryPlank:
                stage = Stage.Cross;
                motion.SetCarryingRod(false);
                SetBridge();
                break;
        }
    }

    private void SetDoor(float t) => door.localPosition = closedDoor + Vector3.left * (1.14f * t);

    private void SetBridge()
    {
        plank.SetPositionAndRotation(crossing.position, crossing.rotation);
        plankCollider.enabled = true;
    }

    private void Save(Stage current, float x)
    {
        checkpoint = new Vector3(x, start.y, start.z);
        savedStage = current;
        Note(current == Stage.UnlockCabinet ? "Cabinet key   /   Checkpoint" : "Checkpoint", 1.65f);
    }

    private void Fail(string reason)
    {
        Freeze(true);
        lostTime = 0.88f;
        Note(reason + "   /   Back to checkpoint", lostTime);
    }

    private void Restore()
    {
        stage = savedStage;
        actionTime = lostTime = exposure = noteTime = 0f;
        crossed = stage == Stage.Cross;
        sawStart = stage != Stage.FindKey;
        if (view != null) view.position += Vector3.right * (checkpoint.x - body.position.x);
        body.position = checkpoint;
        player.transform.position = checkpoint;
        Freeze(false);
        body.linearVelocity = Vector3.zero;
        motion.EndAction();
        motion.SetCarryingRod(false);
        key.gameObject.SetActive(stage == Stage.FindKey);
        SetDoor(crossed ? 1f : 0f);
        plankCollider.enabled = crossed;
        if (crossed) SetBridge();
        else plank.SetPositionAndRotation(storedPosition, storedRotation);
        float patrolStart = crossed ? checkpoint.x - 4f : checkpoint.x + 12f;
        guard.RestartAt(stage == Stage.FindKey ? 65f : patrolStart);
        Physics.SyncTransforms();
    }

    private void Freeze(bool value)
    {
        player.enabled = !value;
        if (!body.isKinematic) body.linearVelocity = Vector3.zero;
        body.isKinematic = value;
    }

    private bool Near(float x, float radius) => Mathf.Abs(body.position.x - x) <= radius;
    private void Note(string message, float duration) { note = message; noteTime = duration; }

    private string Prompt()
    {
        if (lostTime > 0f) return note;
        if (stage == Stage.Escaped) return "Corridor cleared     /     R   Play again";
        if (actionTime > 0f) return null;
        if (stage == Stage.FindKey && Near(key.position.x, 1.1f))
            return Near(key.position.x, 0.88f) ? "E   Take the cabinet key" : "A key on the crate";
        if (Near(cabinetX, 0.96f))
        {
            if (stage == Stage.FindKey) return "Locked   /   A plank behind the grille";
            if (stage == Stage.UnlockCabinet) return "E   Unlock and take the plank";
        }
        if (Near(nearEdge, 1.18f) && stage < Stage.Cross)
            return stage == Stage.CarryPlank ? "E   Lay the plank" : "A plank would span this gap";
        if (noteTime > 0f) return note;
        if (!sawStart) return "A / D   Move     Ctrl   Crouch behind cover     E   Use";
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
        if (message != null) Label(message, 0.88f, caption, scale);
        if (exposure > 0.08f && lostTime <= 0f && stage != Stage.Escaped)
            Label("In the light", 0.79f, warning, scale);
    }

    private static void Label(string text, float y, GUIStyle style, float scale)
    {
        float width = style.CalcSize(new GUIContent(text)).x + 36f * scale;
        var rect = new Rect((Screen.width - width) * 0.5f, Screen.height * y, width, 34f * scale);
        Color previous = GUI.color;
        GUI.color = new Color(0.025f, 0.038f, 0.045f, 0.9f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
        GUI.Label(rect, text, style);
    }
}
