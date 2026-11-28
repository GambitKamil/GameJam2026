using System.Collections;
using UnityEngine;

public sealed class KitchenPassageDoor : MonoBehaviour
{
    [SerializeField] private Transform leaf;
    [SerializeField] private Transform handle;
    [SerializeField] private Transform approach;
    [SerializeField] private Transform inside;
    [SerializeField] private float inwardAngle = -88f;
    [SerializeField] private AudioSource latch;

    private Quaternion closed;
    private float blackout;

    private void Awake()
    {
        useGUILayout = false;
        closed = leaf.localRotation;
    }

    internal IEnumerator Leave(SideViewPlayer player)
    {
        player.SetDoorwayWalk(true);
        var actor = player.transform;
        var motion = player.GetComponent<CellMotion>();
        motion.Face(1f);
        yield return WalkTo(actor, approach.position, 1.8f);

        motion.PlayLibraryGesture(CellAnimationLibrary.Motion.Interact, .3f);
        for (float elapsed = 0f; elapsed < .26f; elapsed += Time.deltaTime)
        {
            motion.SetHandTarget(handle.position, Mathf.SmoothStep(0f, 1f, elapsed / .26f));
            yield return null;
        }
        motion.SetHandTarget(handle.position, 1f);
        PlayLatch();
        motion.PlayLibraryGesture(CellAnimationLibrary.Motion.Push, .68f);
        for (float elapsed = 0f; elapsed < .68f; elapsed += Time.deltaTime)
        {
            float t = elapsed / .68f;
            SetOpen(Mathf.SmoothStep(0f, 1f, t));
            motion.SetHandTarget(handle.position, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.28f, .72f, t)));
            yield return null;
        }
        SetOpen(1f);
        motion.EndAction();
        yield return WalkTo(actor, inside.position, 1.85f);
    }

    internal IEnumerator FadeOut()
    {
        while (blackout < 1f)
        {
            blackout = Mathf.MoveTowards(blackout, 1f, Time.deltaTime / .22f);
            yield return null;
        }
    }

    internal IEnumerator Arrive(SideViewPlayer player)
    {
        player.SetDoorwayWalk(true);
        player.GetComponent<CellMotion>().Face(1f);
        var actor = player.transform;
        Vector3 start = inside.position;
        Vector3 end = approach.position;
        // Clear the jamb before curving onto the kitchen's side-view walking lane.
        Vector3 corner = new Vector3(start.x, end.y, end.z);
        actor.SetPositionAndRotation(start, Heading(corner - start));
        SetOpen(1f);
        bool ownFade = GameManager.Instance == null;
        blackout = ownFade ? 1f : 0f;
        float duration = (Vector3.Distance(start, corner) + Vector3.Distance(corner, end)) / 2.3f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = elapsed / duration;
            Vector3 a = Vector3.Lerp(start, corner, t);
            Vector3 b = Vector3.Lerp(corner, end, t);
            actor.SetPositionAndRotation(Vector3.Lerp(a, b, t), Heading(b - a));
            SetOpen(1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.61f, .97f, t)));
            if (ownFade) blackout = 1f - Mathf.Clamp01(elapsed / .24f);
            yield return null;
        }
        SetOpen(0f);
        PlayLatch();
        blackout = 0f;
        actor.SetPositionAndRotation(end, Quaternion.identity);
        Physics.SyncTransforms();
        player.SetDoorwayWalk(false);
    }

    private static IEnumerator WalkTo(Transform actor, Vector3 destination, float speed)
    {
        Vector3 start = actor.position;
        Vector3 delta = destination - start;
        float duration = delta.magnitude / speed;
        Quaternion turn = Heading(delta);
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            actor.SetPositionAndRotation(Vector3.Lerp(start, destination, t),
                Quaternion.RotateTowards(actor.rotation, turn, 285f * Time.deltaTime));
            yield return null;
        }
        actor.SetPositionAndRotation(destination, turn);
    }

    private static Quaternion Heading(Vector3 direction)
    {
        return Quaternion.Euler(0f, -Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg, 0f);
    }

    private void SetOpen(float amount)
    {
        leaf.localRotation = closed * Quaternion.Euler(0f, inwardAngle * amount, 0f);
    }

    private void PlayLatch()
    {
        var audio = AudioManager.Instance;
        if (latch != null && (audio == null || !audio.IsMuted))
            latch.PlayOneShot(latch.clip, audio != null ? audio.SfxVolume : 1f);
    }

    private void OnGUI()
    {
        if (blackout <= 0f || Event.current.type != EventType.Repaint) return;
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, blackout);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
