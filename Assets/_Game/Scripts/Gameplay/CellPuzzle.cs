using System;
using System.Collections;
using UnityEngine;

public sealed class CellPuzzle : MonoBehaviour
{
    private enum Step
    {
        BreadOnTable,
        CarryingBread,
        MouseRunning,
        KeyReady,
        CarryingKey,
        DoorOpening,
        DoorOpen,
        Complete
    }

    [SerializeField] private SideViewPlayer player;
    [SerializeField] private Camera levelCamera;
    [SerializeField] private Transform breadOnTable;
    [SerializeField] private Transform breadOnBoard;
    [SerializeField] private Transform board;
    [SerializeField] private Transform mouse;
    [SerializeField] private Transform mouseOnBoard;
    [SerializeField] private Transform key;
    [SerializeField] private Transform keySlideEnd;
    [SerializeField] private Transform keyLanding;
    [SerializeField] private Transform doorHinge;
    [SerializeField] private Transform doorHandle;
    [SerializeField] private Collider doorBlocker;
    [SerializeField] private BoxCollider exitZone;
    [SerializeField, Min(0.1f)] private float interactionReach = 0.9f;
    [SerializeField] private bool showPrototypePrompts = true;

    public event Action BreadPlaced;
    public event Action KeyDropped;
    public event Action DoorOpened;
    public event Action ExitReached;

    private Step step = Step.BreadOnTable;
    private float lockedDoorMessageUntil;

    private void Awake()
    {
        if (player == null || levelCamera == null || breadOnTable == null || breadOnBoard == null ||
            board == null || mouse == null || mouseOnBoard == null || key == null ||
            keySlideEnd == null || keyLanding == null || doorHinge == null || doorHandle == null ||
            doorBlocker == null || exitZone == null)
        {
            Debug.LogError("Cell puzzle has missing scene references.", this);
            enabled = false;
            return;
        }

        breadOnBoard.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!SideViewPlayer.CanReadInput || step == Step.Complete)
            return;

        if (step == Step.DoorOpen && exitZone.bounds.Contains(player.transform.position))
        {
            step = Step.Complete;
            ExitReached?.Invoke();
            var game = GameManager.Instance;
            if (game != null)
                game.CompleteCurrentLevel();
            return;
        }

        if (!Input.GetKeyDown(KeyCode.E))
            return;

        lockedDoorMessageUntil = 0f;
        switch (step)
        {
            case Step.BreadOnTable when Near(breadOnTable):
                step = Step.CarryingBread;
                breadOnTable.gameObject.SetActive(false);
                break;
            case Step.CarryingBread when Near(breadOnBoard):
                step = Step.MouseRunning;
                breadOnBoard.gameObject.SetActive(true);
                BreadPlaced?.Invoke();
                StartCoroutine(RunMouseSequence());
                break;
            case Step.KeyReady when Near(key):
                step = Step.CarryingKey;
                key.gameObject.SetActive(false);
                break;
            case Step.CarryingKey when Near(doorHandle):
                step = Step.DoorOpening;
                StartCoroutine(OpenDoor());
                break;
            case Step.BreadOnTable:
            case Step.CarryingBread:
            case Step.KeyReady:
                if (Near(doorHandle))
                    lockedDoorMessageUntil = Time.time + 1.8f;
                break;
        }
    }

    private IEnumerator RunMouseSequence()
    {
        Vector3 start = mouse.position;
        Vector3 boardPosition = mouseOnBoard.position;
        float duration = 0.95f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float progress = elapsed / duration;
            mouse.position = Vector3.Lerp(start, boardPosition, progress)
                + Vector3.up * (Mathf.Sin(progress * Mathf.PI) * 0.22f);
            yield return null;
        }
        mouse.position = boardPosition;

        var startRotation = board.localRotation;
        var tiltedRotation = startRotation * Quaternion.Euler(0f, 0f, 14f);
        Vector3 localKeyStart = board.InverseTransformPoint(key.position);
        Vector3 localKeyEnd = board.InverseTransformPoint(keySlideEnd.position);
        Vector3 localMouse = board.InverseTransformPoint(mouse.position);
        duration = 0.45f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float progress = elapsed / duration;
            board.localRotation = Quaternion.Slerp(startRotation, tiltedRotation, progress);
            key.position = board.TransformPoint(Vector3.Lerp(localKeyStart, localKeyEnd, progress));
            mouse.position = board.TransformPoint(localMouse);
            yield return null;
        }
        board.localRotation = tiltedRotation;
        mouse.position = board.TransformPoint(localMouse);
        start = board.TransformPoint(localKeyEnd);
        duration = 0.32f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float progress = elapsed / duration;
            key.position = Vector3.Lerp(start, keyLanding.position, progress * progress);
            yield return null;
        }
        key.position = keyLanding.position;
        step = Step.KeyReady;
        KeyDropped?.Invoke();
        var audio = AudioManager.Instance;
        if (audio != null)
            audio.Play(AudioIds.KeyDrop);
    }

    private IEnumerator OpenDoor()
    {
        Quaternion start = doorHinge.localRotation;
        Quaternion end = start * Quaternion.Euler(0f, -92f, 0f);
        const float duration = 0.36f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            doorHinge.localRotation = Quaternion.Slerp(start, end, elapsed / duration);
            yield return null;
        }
        doorHinge.localRotation = end;
        doorBlocker.enabled = false;
        step = Step.DoorOpen;
        DoorOpened?.Invoke();
        var audio = AudioManager.Instance;
        if (audio != null)
            audio.Play(AudioIds.Door);
    }

    private bool Near(Transform target) =>
        Mathf.Abs(player.transform.position.x - target.position.x) <= interactionReach;

    private string Objective()
    {
        if (Time.time < lockedDoorMessageUntil)
            return "The cell door is locked. The key is on the far edge of the board.";

        switch (step)
        {
            case Step.BreadOnTable: return "Pick up the bread by the bunk.";
            case Step.CarryingBread: return "Place the bread on the near end of the board.";
            case Step.MouseRunning: return "Watch the mouse and the board.";
            case Step.KeyReady: return "Pick up the fallen key.";
            case Step.CarryingKey: return "Use the key at the cell door.";
            case Step.DoorOpening: return "The door is opening.";
            case Step.DoorOpen: return "Walk through the door.";
            case Step.Complete: return "Cell complete.";
            default: return "";
        }
    }

    private void OnGUI()
    {
        if (!showPrototypePrompts)
            return;

        float width = Mathf.Min(495f, Screen.width - 32f);
        GUILayout.BeginArea(new Rect(16f, 16f, width, 62f), GUI.skin.box);
        GUILayout.Label(Objective());
        if (step != Step.Complete)
            GUILayout.Label("A / D  Move     E  Interact");
        GUILayout.EndArea();

        if (!SideViewPlayer.CanReadInput)
            return;

        if (step == Step.BreadOnTable && Near(breadOnTable))
            DrawPrompt(breadOnTable, "E - Pick up bread", 0.48f);
        else if (step == Step.CarryingBread && Near(breadOnBoard))
            DrawPrompt(breadOnBoard, "E - Place bread", 0.48f);
        else if (step == Step.KeyReady && Near(key))
            DrawPrompt(key, "E - Pick up key", 0.55f);
        else if (step == Step.CarryingKey && Near(doorHandle))
            DrawPrompt(doorHandle, "E - Unlock door", 0.5f);
        else if ((step == Step.BreadOnTable || step == Step.CarryingBread || step == Step.KeyReady) && Near(doorHandle))
            DrawPrompt(doorHandle, "E - Check door", 0.5f);
    }

    private void DrawPrompt(Transform anchor, string text, float offset)
    {
        Vector3 screen = levelCamera.WorldToScreenPoint(anchor.position + Vector3.up * offset);
        if (screen.z <= 0f)
            return;

        const float width = 140f;
        float x = Mathf.Clamp(screen.x - width * 0.5f, 0f, Mathf.Max(0f, Screen.width - width));
        float y = Mathf.Clamp(Screen.height - screen.y - 24f, 0f, Mathf.Max(0f, Screen.height - 24f));
        GUI.Box(new Rect(x, y, width, 24f), text);
    }
}
