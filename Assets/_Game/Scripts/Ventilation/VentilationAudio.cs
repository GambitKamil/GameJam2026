using System.Collections.Generic;
using UnityEngine;

// Placeholder sound effects synthesised at runtime for the cues the project has no recordings for
// (beeps, siren, lamp buzz, steam, footsteps). Swap any of them for a real clip in the Inspector:
// components only fall back to these when their AudioSource has no clip assigned.
public static class VentilationAudio
{
    public enum Cue { LampBuzz, Flicker, Beep, Buzzer, Siren, Steam, SteamLoop, Step, GuardStep, Click, Squeak, VentDrone }

    private const int Rate = 22050;
    private static readonly Dictionary<Cue, AudioClip> Cache = new Dictionary<Cue, AudioClip>();

    public static AudioClip Get(Cue cue)
    {
        if (Cache.TryGetValue(cue, out AudioClip clip) && clip != null)
            return clip;
        clip = Build(cue);
        Cache[cue] = clip;
        return clip;
    }

    // Assigns the placeholder only when the source has no clip of its own.
    public static void Fill(AudioSource source, Cue cue)
    {
        if (source != null && source.clip == null)
            source.clip = Get(cue);
    }

    private static AudioClip Build(Cue cue)
    {
        switch (cue)
        {
            case Cue.LampBuzz: return Make("Lamp buzz", 1f, LampBuzz);
            case Cue.Flicker: return Make("Lamp flicker", 0.35f, Flicker);
            case Cue.Beep: return Make("Lift beep", 0.16f, t => Tone(t, 880f) * Envelope(t, 0.16f, 0.005f, 0.05f) * 0.5f);
            case Cue.Buzzer: return Make("Lift buzzer", 0.5f, t => Square(t, 110f) * Envelope(t, 0.5f, 0.01f, 0.08f) * 0.35f);
            case Cue.Siren: return Make("Alarm siren", 2f, Siren);
            case Cue.Steam: return Make("Steam hiss", 1.6f, t => Noise() * Envelope(t, 1.6f, 0.05f, 1.1f) * 0.45f);
            case Cue.SteamLoop: return Make("Steam loop", 4f, SteamLoop);
            case Cue.Step: return Make("Step", 0.14f, t => (Noise() * 0.5f + Tone(t, 70f)) * Envelope(t, 0.14f, 0.002f, 0.12f) * 0.5f);
            case Cue.GuardStep: return Make("Guard step", 0.24f, t => (Noise() * 0.35f + Tone(t, 52f) * 1.2f) * Envelope(t, 0.24f, 0.003f, 0.2f) * 0.7f);
            case Cue.Click: return Make("Brake click", 0.09f, t => (Noise() * 0.6f + Tone(t, 1900f) * 0.4f) * Envelope(t, 0.09f, 0.001f, 0.07f) * 0.6f);
            case Cue.Squeak: return Make("Wheel squeak", 0.45f, t => Tone(t, 1450f + 260f * Mathf.Sin(t * 19f)) * Envelope(t, 0.45f, 0.04f, 0.25f) * 0.12f);
            default: return Make("Vent drone", 3f, VentDrone);
        }
    }

    private static AudioClip Make(string name, float seconds, System.Func<float, float> sample)
    {
        int count = Mathf.CeilToInt(seconds * Rate);
        var data = new float[count];
        for (int i = 0; i < count; i++)
            data[i] = Mathf.Clamp(sample(i / (float)Rate), -1f, 1f);
        var clip = AudioClip.Create(name, count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float Tone(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);
    private static float Square(float t, float hz) => Mathf.Sign(Tone(t, hz)) * 0.7f + Tone(t, hz * 3f) * 0.3f;
    private static float Noise() => Random.value * 2f - 1f;

    private static float Envelope(float t, float length, float attack, float release)
    {
        float up = attack > 0f ? Mathf.Clamp01(t / attack) : 1f;
        float down = release > 0f ? Mathf.Clamp01((length - t) / release) : 1f;
        return up * down;
    }

    // Loops are built from whole cycles so they repeat without a click.
    private static float LampBuzz(float t) => (Tone(t, 100f) * 0.5f + Tone(t, 200f) * 0.25f + Tone(t, 300f) * 0.12f + Noise() * 0.04f) * 0.35f;

    // Steady soft hiss with a slow breath in it; four seconds, loops without a seam at the noise level.
    private static float SteamLoop(float t) => Noise() * (0.22f + 0.05f * Mathf.Sin(t * Mathf.PI * 0.5f)) * 0.5f;

    private static float VentDrone(float t)
    {
        float rumble = Tone(t, 41f) * 0.5f + Tone(t, 82f) * 0.2f;
        float air = Noise() * (0.2f + 0.08f * Mathf.Sin(t * 2f * Mathf.PI / 3f));
        return (rumble + air) * 0.4f;
    }

    private static float Flicker(float t)
    {
        float crackle = Random.value > 0.92f ? Noise() : Noise() * 0.15f;
        return (crackle + Tone(t, 100f) * 0.3f) * Envelope(t, 0.35f, 0.002f, 0.1f) * 0.5f;
    }

    private static float Siren(float t)
    {
        // Pitch glides 420 -> 760 -> 420 Hz over the two-second loop; the phase is the integral of that pitch.
        float phase = 2f * Mathf.PI * (420f * t + 340f * (t * 0.5f - Mathf.Sin(t * Mathf.PI) / (2f * Mathf.PI)));
        return (Mathf.Sin(phase) * 0.6f + Mathf.Sign(Mathf.Sin(phase)) * 0.15f) * 0.45f;
    }
}
