using UnityEngine;

[RequireComponent(typeof(Light))]
public sealed class CellLampFlicker : MonoBehaviour
{
    [SerializeField] private Light secondaryGlow;
    [SerializeField, Min(0.5f)] private float minimumInterval = 2.8f;
    [SerializeField, Min(0.5f)] private float maximumInterval = 7.5f;

    private Light lamp;
    private float lampIntensity;
    private float glowIntensity;
    private float seed;
    private float nextDip;
    private float dipStart = -1f;
    private float dipDuration;
    private float dipStrength;

    private void Awake()
    {
        lamp = GetComponent<Light>();
        lampIntensity = lamp.intensity;
        glowIntensity = secondaryGlow != null ? secondaryGlow.intensity : 0f;
        seed = Random.Range(0f, 1000f);
        ScheduleNextDip();
    }

    private void Update()
    {
        float now = Time.time;
        if (now >= nextDip)
        {
            dipStart = now;
            dipDuration = Random.Range(0.16f, 0.46f);
            dipStrength = Random.Range(0.42f, 0.72f);
            ScheduleNextDip();
        }

        float dim = 0f;
        float phase = (now - dipStart) / Mathf.Max(dipDuration, 0.01f);
        if (dipStart >= 0f && phase < 1f)
        {
            float envelope = Mathf.Sin(phase * Mathf.PI);
            dim = dipStrength * envelope * Mathf.Lerp(0.39f, 1f, Mathf.PerlinNoise(seed, now * 46f));
        }

        float output = 0.98f + (Mathf.PerlinNoise(seed, now * 3f) - 0.5f) * 0.055f - dim;
        lamp.intensity = lampIntensity * output;
        if (secondaryGlow != null)
            secondaryGlow.intensity = glowIntensity * output;
    }

    private void ScheduleNextDip()
    {
        nextDip = Time.time + Random.Range(minimumInterval, Mathf.Max(minimumInterval, maximumInterval));
    }
}
