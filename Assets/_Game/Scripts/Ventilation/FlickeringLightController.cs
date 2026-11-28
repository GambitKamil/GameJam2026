using System;
using UnityEngine;

// Shaft ceiling lamps on a repeating cycle: lit -> warning flicker -> dark.
// Power from the lift lever makes them steady; the story alarm turns them into a slow red pulse.
// The warning is two soft brightness dips, never a strobe (see docs/ART_STYLE.md).
public sealed class FlickeringLightController : MonoBehaviour
{
    public enum Phase { Lit, Warning, Dark, Steady, Alarm }

    [Header("Cycle (seconds)")]
    [SerializeField, Min(0.1f)] private float litSeconds = 5f;
    [SerializeField, Min(0.1f)] private float warningSeconds = 1f;
    [SerializeField, Min(0.1f)] private float darkSeconds = 3f;

    [Header("Lamps")]
    [SerializeField] private Light[] lamps;
    [Tooltip("Emissive lamp glass, dimmed with the lamps.")]
    [SerializeField] private Renderer[] lampGlass;
    [Tooltip("Haze cones under the lamps (Kitchen Lamp Haze shader, _Color alpha).")]
    [SerializeField] private Renderer[] hazes;
    [Tooltip("Lamp spill painted on the walls (Wall wash quads): their colour dims with the lamps.")]
    [SerializeField] private Renderer[] washes;
    [SerializeField, Range(0f, 1.5f)] private float washStrength = 0.55f;
    [Tooltip("Additive glow sprites around the bulbs (_TintColor).")]
    [SerializeField] private Renderer[] halos;
    [SerializeField, Range(0f, 1.5f)] private float hazeStrength = 0.7f;
    [SerializeField, Range(0f, 0.5f)] private float darkLevel = 0.24f;
    [Tooltip("Ambient light kept in the dark phase, as a share of the scene ambient.")]
    [SerializeField, Range(0.2f, 1f)] private float darkAmbient = 0.95f;
    [SerializeField] private Color alarmColor = new Color(0.85f, 0.1f, 0.06f);
    [SerializeField, Min(0.1f)] private float alarmPulseSeconds = 1.1f;

    [Header("Sound")]
    [SerializeField] private AudioSource buzz;
    [SerializeField] private AudioSource flicker;
    [SerializeField, Range(0f, 1f)] private float buzzVolume = 0.16f;

    public event Action<Phase> PhaseChanged;

    public Phase Current { get; private set; } = Phase.Lit;
    // The camera can see in the warning flicker too: it is a warning, not cover.
    public bool IsLit => Current != Phase.Dark;
    public float LitSeconds => litSeconds;
    public float DarkSeconds => darkSeconds;
    public float Level { get; private set; } = 1f;

    private float[] baseIntensity;
    private Color[] baseColor;
    private Color[] glassEmission;
    private Color[] hazeColor;
    private Color[] washColor;
    private Color[] haloColor;
    private MaterialPropertyBlock block;
    private Color ambientSky, ambientEquator, ambientGround;
    private float clock;
    private float alarmClock;
    private float appliedAmbient = -1f;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        baseIntensity = new float[lamps.Length];
        baseColor = new Color[lamps.Length];
        for (int i = 0; i < lamps.Length; i++)
        {
            baseIntensity[i] = lamps[i].intensity;
            baseColor[i] = lamps[i].color;
        }

        glassEmission = new Color[lampGlass.Length];
        for (int i = 0; i < lampGlass.Length; i++)
        {
            Material material = lampGlass[i].sharedMaterial;
            glassEmission[i] = material != null && material.HasProperty("_EmissionColor")
                ? material.GetColor("_EmissionColor") : Color.white;
        }

        hazeColor = new Color[hazes.Length];
        for (int i = 0; i < hazes.Length; i++)
        {
            Material material = hazes[i].sharedMaterial;
            hazeColor[i] = material != null && material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        }

        washColor = new Color[washes.Length];
        for (int i = 0; i < washes.Length; i++)
        {
            Material material = washes[i].sharedMaterial;
            washColor[i] = material != null && material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        }

        haloColor = new Color[halos.Length];
        for (int i = 0; i < halos.Length; i++)
        {
            Material material = halos[i].sharedMaterial;
            haloColor[i] = material != null && material.HasProperty("_TintColor") ? material.GetColor("_TintColor") : Color.white;
        }

        ambientSky = RenderSettings.ambientSkyColor;
        ambientEquator = RenderSettings.ambientEquatorColor;
        ambientGround = RenderSettings.ambientGroundColor;

        VentilationAudio.Fill(buzz, VentilationAudio.Cue.LampBuzz);
        VentilationAudio.Fill(flicker, VentilationAudio.Cue.Flicker);
        if (buzz != null && !buzz.isPlaying)
            buzz.Play();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (Current is Phase.Lit or Phase.Warning or Phase.Dark)
            AdvanceCycle(dt);
        else if (Current == Phase.Alarm)
            alarmClock += dt;

        Level = CurrentLevel();
        Apply(Level, Current == Phase.Alarm);
    }

    private void AdvanceCycle(float dt)
    {
        clock += dt;
        float cycle = litSeconds + warningSeconds + darkSeconds;
        if (clock >= cycle)
            clock -= cycle;

        Phase phase = clock < litSeconds ? Phase.Lit :
            clock < litSeconds + warningSeconds ? Phase.Warning : Phase.Dark;
        SetPhase(phase);
    }

    private float CurrentLevel()
    {
        switch (Current)
        {
            case Phase.Lit:
            case Phase.Steady:
                return 1f;
            case Phase.Warning:
            {
                float t = (clock - litSeconds) / warningSeconds;
                // Two soft dips inside the warning second, then a tired half-glow.
                if (t < 0.12f) return 1f;
                if (t < 0.32f) return 0.22f;
                if (t < 0.55f) return 0.9f;
                if (t < 0.78f) return 0.16f;
                return 0.55f;
            }
            case Phase.Alarm:
                return 0.35f + 0.65f * (0.5f - 0.5f * Mathf.Cos(alarmClock * 2f * Mathf.PI / alarmPulseSeconds));
            default:
                return darkLevel;
        }
    }

    private void Apply(float level, bool alarm)
    {
        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] == null) continue;
            lamps[i].intensity = baseIntensity[i] * level * (alarm ? 1.25f : 1f);
            lamps[i].color = alarm ? alarmColor : baseColor[i];
        }

        for (int i = 0; i < lampGlass.Length; i++)
        {
            if (lampGlass[i] == null) continue;
            Color emission = alarm ? alarmColor * 1.6f : glassEmission[i];
            block.Clear();
            block.SetColor("_EmissionColor", emission * level);
            block.SetColor("_Color", Color.Lerp(new Color(0.08f, 0.09f, 0.1f), alarm ? alarmColor : Color.white, level));
            lampGlass[i].SetPropertyBlock(block);
        }

        for (int i = 0; i < hazes.Length; i++)
        {
            if (hazes[i] == null) continue;
            Color tint = alarm ? new Color(alarmColor.r, alarmColor.g, alarmColor.b, hazeColor[i].a) : hazeColor[i];
            tint.a *= level * hazeStrength;
            block.Clear();
            block.SetColor("_Color", tint);
            hazes[i].SetPropertyBlock(block);
        }

        for (int i = 0; i < washes.Length; i++)
        {
            if (washes[i] == null) continue;
            Color wash = washColor[i];
            float k = Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(level)) * washStrength;
            Color lit = alarm ? new Color(wash.g * 1.6f, wash.g * 0.25f, wash.g * 0.2f, wash.a) : wash;
            block.Clear();
            block.SetColor("_Color", new Color(lit.r * k, lit.g * k, lit.b * k, wash.a));
            washes[i].SetPropertyBlock(block);
        }

        for (int i = 0; i < halos.Length; i++)
        {
            if (halos[i] == null) continue;
            Color glow = alarm ? new Color(alarmColor.r, alarmColor.g, alarmColor.b, haloColor[i].a) : haloColor[i];
            glow.a *= Mathf.Clamp01(level);
            block.Clear();
            block.SetColor("_TintColor", glow);
            halos[i].SetPropertyBlock(block);
        }

        float ambient = Mathf.Lerp(darkAmbient, 1f, Mathf.Clamp01(level));
        if (Mathf.Abs(ambient - appliedAmbient) > 0.01f)
        {
            appliedAmbient = ambient;
            RenderSettings.ambientSkyColor = ambientSky * ambient;
            RenderSettings.ambientEquatorColor = ambientEquator * ambient;
            RenderSettings.ambientGroundColor = ambientGround * ambient;
        }

        if (buzz != null)
            buzz.volume = buzzVolume * Mathf.Clamp01(level) * (alarm ? 0.4f : 1f);
    }

    private void SetPhase(Phase phase)
    {
        if (phase == Current)
            return;
        Current = phase;
        if (phase == Phase.Warning && flicker != null)
        {
            flicker.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
            flicker.Play();
        }
        PhaseChanged?.Invoke(phase);
    }

    // Lift power: steady light while powered, back to the cycle (starting dark) when cut.
    public void SetSteady(bool steady)
    {
        if (Current == Phase.Alarm)
            return;
        if (steady)
        {
            SetPhase(Phase.Steady);
            return;
        }
        RestartCycle(true);
    }

    // Respawn: give the player a fresh dark window to get moving.
    public void RestartCycle(bool startDark)
    {
        clock = startDark ? litSeconds + warningSeconds : 0f;
        Current = Phase.Steady; // force the change event below
        SetPhase(startDark ? Phase.Dark : Phase.Lit);
    }

    public void StartAlarm()
    {
        alarmClock = 0f;
        SetPhase(Phase.Alarm);
    }
}
