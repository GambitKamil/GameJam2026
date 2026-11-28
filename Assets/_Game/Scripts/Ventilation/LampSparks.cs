using UnityEngine;

// Worn-out lamp that now and then spits a few sparks, mostly while the shaft lights flicker.
public sealed class LampSparks : MonoBehaviour
{
    [SerializeField] private ParticleSystem sparks;
    [SerializeField] private AudioSource crackle;
    [SerializeField] private FlickeringLightController lights;
    [SerializeField] private Vector2 intervalSeconds = new Vector2(5f, 12f);
    [SerializeField] private Vector2Int burst = new Vector2Int(6, 14);
    [Tooltip("Chance to spark when the warning flicker starts.")]
    [SerializeField, Range(0f, 1f)] private float warningChance = 0.45f;

    private float next;

    private void Awake()
    {
        VentilationAudio.Fill(crackle, VentilationAudio.Cue.Flicker);
        next = Time.time + Random.Range(intervalSeconds.x, intervalSeconds.y);
    }

    private void OnEnable()
    {
        if (lights != null) lights.PhaseChanged += OnPhase;
    }

    private void OnDisable()
    {
        if (lights != null) lights.PhaseChanged -= OnPhase;
    }

    private void OnPhase(FlickeringLightController.Phase phase)
    {
        if (phase == FlickeringLightController.Phase.Warning && Random.value < warningChance)
            Spark();
    }

    private void Update()
    {
        if (Time.time < next) return;
        next = Time.time + Random.Range(intervalSeconds.x, intervalSeconds.y);
        // A dead lamp does not spark.
        if (lights == null || lights.Level > 0.3f)
            Spark();
    }

    private void Spark()
    {
        if (sparks != null) sparks.Emit(Random.Range(burst.x, burst.y + 1));
        if (crackle != null && crackle.clip != null)
        {
            crackle.pitch = Random.Range(1.1f, 1.6f);
            crackle.Play();
        }
    }
}
