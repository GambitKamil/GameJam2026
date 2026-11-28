using UnityEngine;

// Side outlet on the ceiling steam main that streams glowing steam while the shaft lighting is on.
// The cloud and its soft light come up together and hold steady; in the dark phase the outlet stops
// and the hanging steam fades out.
public sealed class SteamGlow : MonoBehaviour
{
    [SerializeField] private ParticleSystem steam;
    [SerializeField] private Light glow;
    [SerializeField] private AudioSource hiss;
    [SerializeField] private FlickeringLightController lights;
    [SerializeField, Range(0f, 1f)] private float hissVolume = 0.12f;

    private float glowIntensity;

    private void Awake()
    {
        VentilationAudio.Fill(hiss, VentilationAudio.Cue.SteamLoop);
        if (glow != null) glowIntensity = glow.intensity;
        if (hiss != null && hiss.clip != null)
        {
            hiss.loop = true;
            hiss.volume = 0f;
            hiss.time = Random.Range(0f, hiss.clip.length);
            hiss.Play();
        }
    }

    private void Update()
    {
        float level = lights != null ? Mathf.Clamp01(lights.Level) : 1f;
        bool on = lights == null || lights.Current != FlickeringLightController.Phase.Dark;
        if (steam != null)
        {
            var emission = steam.emission;
            emission.enabled = on;
        }
        if (glow != null)
            glow.intensity = glowIntensity * level;
        if (hiss != null)
            hiss.volume = Mathf.MoveTowards(hiss.volume, on ? hissVolume : 0f, Time.deltaTime * 0.6f);
    }
}
