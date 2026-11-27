using System;
using System.Collections.Generic;
using UnityEngine;

public static class AudioIds
{
    public const string MusicMenu = "music_menu";
    public const string MusicIntro = "music_intro";
    public const string AmbienceCell = "ambience_cell";
    public const string AmbienceCorridor = "ambience_corridor";
    public const string AmbienceKitchen = "ambience_kitchen";
    public const string Footstep = "footstep";
    public const string Door = "door";
    public const string KeyDrop = "key_drop";
    public const string PressurePlate = "pressure_plate";
    public const string GateOpen = "gate_open";
    public const string GateClose = "gate_close";
    public const string CartWheels = "cart_wheels";
    public const string Dishes = "dishes";
    public const string Detected = "detected";
    public const string UiClick = "ui_click";
}

public enum AudioCategory
{
    Music,
    Sfx
}

[Serializable]
public sealed class AudioCue
{
    public string id;
    public AudioClip clip;
    public AudioCategory category = AudioCategory.Sfx;
    public bool loop;

    [Range(0f, 1f)]
    public float volume = 1f;
}

public sealed class AudioManager : MonoBehaviour
{
    private const string MusicVolumeKey = "audio.musicVolume";
    private const string SfxVolumeKey = "audio.sfxVolume";
    private const string MutedKey = "audio.muted";

    public static AudioManager Instance { get; private set; }

    public event Action<float> MusicVolumeChanged;
    public event Action<float> SfxVolumeChanged;
    public event Action<bool> MutedChanged;

    [SerializeField]
    private AudioCue[] cues = Array.Empty<AudioCue>();

    public float MusicVolume { get; private set; } = 1f;
    public float SfxVolume { get; private set; } = 1f;
    public bool IsMuted { get; private set; }

    private readonly Dictionary<string, AudioCue> cueById = new Dictionary<string, AudioCue>();
    private readonly Dictionary<string, AudioSource> loopSourceById = new Dictionary<string, AudioSource>();
    private AudioSource musicSource;
    private AudioSource sfxSource;
    private string currentMusicId;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildCueLookup();
        CreateSources();
        LoadPreferences();
        ApplyVolumes();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Play(string id)
    {
        if (!TryGetCue(id, out AudioCue cue))
        {
            return;
        }

        if (cue.category == AudioCategory.Music)
        {
            PlayMusic(id, cue);
            return;
        }

        if (cue.loop)
        {
            PlayLoop(id, cue);
            return;
        }

        sfxSource.PlayOneShot(cue.clip, cue.volume);
    }

    public void Stop(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        if (id == currentMusicId)
        {
            musicSource.Stop();
            currentMusicId = null;
        }

        if (loopSourceById.TryGetValue(id, out AudioSource source))
        {
            source.Stop();
            Destroy(source.gameObject);
            loopSourceById.Remove(id);
        }
    }

    public void StopAllSfx()
    {
        sfxSource.Stop();

        foreach (AudioSource source in loopSourceById.Values)
        {
            if (source != null)
            {
                Destroy(source.gameObject);
            }
        }

        loopSourceById.Clear();
    }

    public void SetMusicVolume(float volume)
    {
        MusicVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(MusicVolumeKey, MusicVolume);
        ApplyVolumes();
        MusicVolumeChanged?.Invoke(MusicVolume);
    }

    public void SetSfxVolume(float volume)
    {
        SfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(SfxVolumeKey, SfxVolume);
        ApplyVolumes();
        SfxVolumeChanged?.Invoke(SfxVolume);
    }

    public void SetMuted(bool muted)
    {
        IsMuted = muted;
        PlayerPrefs.SetInt(MutedKey, IsMuted ? 1 : 0);
        ApplyVolumes();
        MutedChanged?.Invoke(IsMuted);
    }

    private void BuildCueLookup()
    {
        cueById.Clear();

        foreach (AudioCue cue in cues)
        {
            if (cue == null || string.IsNullOrWhiteSpace(cue.id) || cue.clip == null)
            {
                continue;
            }

            if (!cueById.TryAdd(cue.id, cue))
            {
                Debug.LogWarning($"Duplicate audio cue id '{cue.id}' on {name}.", this);
            }
        }
    }

    private void CreateSources()
    {
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.ignoreListenerPause = true;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
    }

    private void LoadPreferences()
    {
        MusicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
        SfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
        IsMuted = PlayerPrefs.GetInt(MutedKey, 0) == 1;
    }

    private bool TryGetCue(string id, out AudioCue cue)
    {
        cue = null;

        if (string.IsNullOrWhiteSpace(id) || !cueById.TryGetValue(id, out cue))
        {
            Debug.LogWarning($"Audio cue '{id}' is not configured.", this);
            return false;
        }

        return true;
    }

    private void PlayMusic(string id, AudioCue cue)
    {
        if (currentMusicId == id && musicSource.isPlaying)
        {
            return;
        }

        currentMusicId = id;
        musicSource.clip = cue.clip;
        musicSource.loop = cue.loop;
        musicSource.volume = EffectiveMusicVolume(cue.volume);
        musicSource.Play();
    }

    private void PlayLoop(string id, AudioCue cue)
    {
        if (loopSourceById.TryGetValue(id, out AudioSource existingSource) &&
            existingSource != null)
        {
            if (!existingSource.isPlaying)
            {
                existingSource.Play();
            }

            return;
        }

        GameObject sourceObject = new GameObject($"Audio Loop - {id}");
        sourceObject.transform.SetParent(transform);
        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.clip = cue.clip;
        source.loop = true;
        source.playOnAwake = false;
        source.volume = EffectiveSfxVolume(cue.volume);
        source.Play();
        loopSourceById[id] = source;
    }

    private void ApplyVolumes()
    {
        if (musicSource != null)
        {
            float cueVolume = 1f;
            if (!string.IsNullOrEmpty(currentMusicId) && cueById.TryGetValue(currentMusicId, out AudioCue cue))
            {
                cueVolume = cue.volume;
            }

            musicSource.volume = EffectiveMusicVolume(cueVolume);
        }

        if (sfxSource != null)
        {
            sfxSource.volume = EffectiveSfxVolume(1f);
        }

        foreach (KeyValuePair<string, AudioSource> pair in loopSourceById)
        {
            if (pair.Value != null && cueById.TryGetValue(pair.Key, out AudioCue cue))
            {
                pair.Value.volume = EffectiveSfxVolume(cue.volume);
            }
        }
    }

    private float EffectiveMusicVolume(float cueVolume)
    {
        return IsMuted ? 0f : MusicVolume * cueVolume;
    }

    private float EffectiveSfxVolume(float cueVolume)
    {
        return IsMuted ? 0f : SfxVolume * cueVolume;
    }
}
