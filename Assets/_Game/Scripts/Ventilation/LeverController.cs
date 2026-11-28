using System;
using System.Collections;
using UnityEngine;

// Wall lever. A toggle lever stays where it is thrown (power supply);
// a spring lever snaps back after each pull (lift launch).
public sealed class LeverController : MonoBehaviour
{
    [SerializeField] private string displayName = "Lever";
    [SerializeField] private Transform handle;
    [Tooltip("Point the hand reaches for.")]
    [SerializeField] private Transform grip;
    [SerializeField] private float offAngle = 40f;
    [SerializeField] private float onAngle = -40f;
    [SerializeField] private bool springReturn;
    [SerializeField, Min(0.05f)] private float throwSeconds = 0.3f;
    [SerializeField, Min(0.2f)] private float interactRadius = 0.8f;
    [SerializeField] private AudioSource clank;

    public event Action<LeverController, bool> Switched;

    public string DisplayName => displayName;
    public bool IsOn { get; private set; }
    public bool Locked { get; set; }
    public bool Moving => motion != null;
    public Vector3 Grip => grip != null ? grip.position : handle.position;
    public float InteractRadius => interactRadius;

    private Coroutine motion;
    private Quaternion rest;

    private void Awake()
    {
        rest = handle.localRotation;
        Pose(offAngle);
    }

    public bool InReach(Vector3 position) =>
        Mathf.Abs(position.x - transform.position.x) <= interactRadius &&
        Mathf.Abs(position.y - transform.position.y) < 2.2f;

    // Returns false when the lever will not move (locked or already moving).
    public bool Pull()
    {
        if (Locked || motion != null)
        {
            if (clank != null && clank.clip != null)
            {
                clank.pitch = 1.6f;
                clank.PlayOneShot(clank.clip, 0.35f);
            }
            return false;
        }
        motion = StartCoroutine(Throw());
        return true;
    }

    private IEnumerator Throw()
    {
        bool target = springReturn || !IsOn;
        float from = IsOn ? onAngle : offAngle;
        float to = target ? onAngle : offAngle;
        if (clank != null && clank.clip != null)
        {
            clank.pitch = target ? 1.2f : 1f;
            clank.Play();
        }
        for (float t = 0f; t < throwSeconds; t += Time.deltaTime)
        {
            Pose(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / throwSeconds)));
            yield return null;
        }
        Pose(to);
        IsOn = target;
        Switched?.Invoke(this, target);

        if (springReturn)
        {
            yield return new WaitForSeconds(0.35f);
            for (float t = 0f; t < throwSeconds; t += Time.deltaTime)
            {
                Pose(Mathf.Lerp(onAngle, offAngle, t / throwSeconds));
                yield return null;
            }
            Pose(offAngle);
            IsOn = false;
        }
        motion = null;
    }

    private void Pose(float angle) => handle.localRotation = rest * Quaternion.Euler(0f, 0f, angle);

    // Checkpoint restore: no sound, no event.
    public void SetState(bool on)
    {
        if (motion != null)
        {
            StopCoroutine(motion);
            motion = null;
        }
        IsOn = on && !springReturn;
        Locked = false;
        Pose(IsOn ? onAngle : offAngle);
    }
}
