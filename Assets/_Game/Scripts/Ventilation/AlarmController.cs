using System.Collections;
using UnityEngine;

// Two kinds of alarm:
//  - gameplay: a camera caught the prisoner -> siren, red beacons, back to the last checkpoint;
//  - story: the lift is leaving -> siren, red lamps and guards running in below. Never a loss.
public sealed class AlarmController : MonoBehaviour
{
    [SerializeField] private CheckpointManager checkpoints;
    [SerializeField] private FlickeringLightController shaftLights;
    [SerializeField] private VentilationPlayerActions actions;
    [SerializeField] private Light[] beacons;
    [SerializeField] private AudioSource siren;
    [SerializeField] private AudioSource guards;
    [SerializeField, Min(0.2f)] private float caughtHoldSeconds = 1.1f;
    [SerializeField, Min(0.1f)] private float guardStepSeconds = 0.38f;
    [Tooltip("Screen-edge warning that follows the most alert camera.")]
    [SerializeField] private DetectionVignette vignette;
    [SerializeField] private SecurityCameraController[] cameras;

    public bool Raised { get; private set; }
    public bool StoryAlarm { get; private set; }

    private float[] beaconIntensity;

    private void Awake()
    {
        VentilationAudio.Fill(siren, VentilationAudio.Cue.Siren);
        beaconIntensity = new float[beacons.Length];
        for (int i = 0; i < beacons.Length; i++)
        {
            beaconIntensity[i] = beacons[i].intensity;
            beacons[i].intensity = 0f;
        }
    }

    private void Update()
    {
        if (vignette != null)
        {
            float alert = 0f;
            foreach (SecurityCameraController camera in cameras)
                if (camera != null && camera.Detecting) alert = Mathf.Max(alert, camera.Progress);
            vignette.Level = Raised ? 1f : alert;
        }

        float pulse = 0.5f - 0.5f * Mathf.Cos(Time.time * 2f * Mathf.PI / 1.1f);
        for (int i = 0; i < beacons.Length; i++)
            beacons[i].intensity = Raised || StoryAlarm ? beaconIntensity[i] * (0.25f + 0.75f * pulse) : 0f;
    }

    public void TriggerGameplayAlarm(SecurityCameraController camera)
    {
        if (Raised || StoryAlarm) return;
        StartCoroutine(Caught());
    }

    private IEnumerator Caught()
    {
        Raised = true;
        actions.Freeze(true);
        if (siren != null)
        {
            siren.loop = true;
            siren.volume = 0.5f;
            siren.Play();
        }
        yield return new WaitForSeconds(caughtHoldSeconds);
        yield return checkpoints.Respawn();
        if (siren != null) siren.Stop();
        Raised = false;
    }

    public void StartStoryAlarm()
    {
        if (StoryAlarm) return;
        StoryAlarm = true;
        shaftLights.StartAlarm();
        if (siren != null)
        {
            siren.loop = true;
            siren.volume = 0f;
            siren.Play();
        }
        StartCoroutine(StoryAlarmSound());
    }

    private IEnumerator StoryAlarmSound()
    {
        AudioClip step = VentilationAudio.Get(VentilationAudio.Cue.GuardStep);
        float elapsed = 0f;
        float nextStep = 1.2f;
        while (true)
        {
            elapsed += Time.deltaTime;
            if (siren != null) siren.volume = Mathf.Min(0.42f, elapsed * 0.4f);
            if (guards != null && elapsed >= nextStep)
            {
                // Boots on the stairs below, getting louder and closer together.
                float near = Mathf.Clamp01((elapsed - 1.2f) / 6f);
                guards.pitch = Random.Range(0.9f, 1.05f);
                guards.PlayOneShot(guards.clip != null ? guards.clip : step, Mathf.Lerp(0.15f, 0.9f, near));
                nextStep = elapsed + guardStepSeconds * Mathf.Lerp(1.3f, 0.8f, near) * Random.Range(0.85f, 1.15f);
            }
            yield return null;
        }
    }
}
