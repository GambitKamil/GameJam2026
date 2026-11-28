using UnityEngine;

// A leaking pipe joint that vents a puff of steam every few seconds.
public sealed class SteamVent : MonoBehaviour
{
    [SerializeField] private ParticleSystem puff;
    [SerializeField] private AudioSource hiss;
    [SerializeField] private Vector2 intervalSeconds = new Vector2(4f, 8f);
    [SerializeField, Range(1, 60)] private int particles = 22;

    private float next;

    private void Awake()
    {
        VentilationAudio.Fill(hiss, VentilationAudio.Cue.Steam);
        next = Time.time + Random.Range(0.5f, intervalSeconds.y);
    }

    private void Update()
    {
        if (Time.time < next) return;
        next = Time.time + Random.Range(intervalSeconds.x, intervalSeconds.y);
        if (puff != null) puff.Emit(particles);
        if (hiss != null && hiss.clip != null)
        {
            hiss.pitch = Random.Range(0.85f, 1.15f);
            hiss.Play();
        }
    }
}
