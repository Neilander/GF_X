using System.Collections;
using System.Collections.Generic;
using AAAGame.Audio;
using GameFramework;
using GameFramework.Sound;
using UnityEngine;
using UnityGameFramework.Runtime;

public class AudioManager : GameFrameworkComponent
{
    public static AudioManager Instance { get; private set; }
    public const int DefaultSfxAgentCount = 16;

    private const string K_MASTER = "vol_master";
    private const string K_MUSIC = "vol_music";
    private const string K_SFX = "vol_sfx";

    private float masterVol;
    private float musicVol;
    private float sfxVol;

    private readonly Dictionary<AudioCue, int> persistent = new();
    private readonly Dictionary<int, ActiveSfx> activeSfx = new();
    private readonly HashSet<int> pausedGameplaySfx = new();
    private AudioCueLibrary cueLibrary;
    private bool wasGamePaused;

    private struct ActiveSfx
    {
        public AudioCue cue;
        public AudioClip clip;
        public float pitch;
    }

    protected override void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        base.Awake();
        Instance = this;
        cueLibrary = GetComponent<AudioCueLibrary>();

        masterVol = PlayerPrefs.GetFloat(K_MASTER, 1f);
        musicVol = PlayerPrefs.GetFloat(K_MUSIC, 1f);
        sfxVol = PlayerPrefs.GetFloat(K_SFX, 1f);
    }

    private void Update()
    {
        bool isGamePaused = LogicTimeControlService.IsPaused;
        if (isGamePaused == wasGamePaused)
        {
            return;
        }

        wasGamePaused = isGamePaused;
        if (isGamePaused)
        {
            PauseActiveGameplaySfx();
        }
        else
        {
            ResumeActiveGameplaySfx();
        }
    }

    public AudioHandle Play(AudioCue cue)
    {
        if (cue == null)
        {
            return AudioHandle.Invalid;
        }

        return cue.category == AudioCategory.SFX
            ? PlaySfxInternal(cue)
            : PlayPersistentInternal(cue);
    }

    public AudioHandle Play(string cueKey)
    {
        return Play(GetCue(cueKey));
    }

    public AudioHandle PlayClip(AudioClip clip, Vector3 worldPosition)
    {
        if (clip == null)
        {
            Debug.LogWarning("[AudioManager] AudioClip is null.");
            return AudioHandle.Invalid;
        }

        PlaySoundParams playParams = PlaySoundParams.Create();
        playParams.VolumeInSoundGroup = 1f;
        playParams.Pitch = 1f;
        playParams.SpatialBlend = 0f;

        int serialId = PlaySound(clip, Const.SoundGroup.Sound.ToString(), playParams, worldPosition);
        if (serialId <= 0)
        {
            return AudioHandle.Invalid;
        }

        activeSfx[serialId] = new ActiveSfx { cue = null, clip = clip, pitch = 1f };
        StartCoroutine(ReleaseSfxAfter(serialId, clip, 1f));
        PauseNewSfxIfGamePaused(serialId);
        return new AudioHandle(serialId);
    }

    public AudioCue GetCue(string cueKey)
    {
        if (cueLibrary == null)
        {
            cueLibrary = GetComponent<AudioCueLibrary>();
        }

        if (cueLibrary == null)
        {
            Debug.LogError("[AudioManager] Missing AudioCueLibrary component.");
            return null;
        }

        return cueLibrary.Get(cueKey);
    }

    public void Stop(AudioCue cue)
    {
        if (cue == null || !persistent.TryGetValue(cue, out int serialId))
        {
            return;
        }

        StopSoundSafe(serialId, cue.fadeOut);
        persistent.Remove(cue);
        pausedGameplaySfx.Remove(serialId);
    }

    public void StopSfx(AudioHandle h, float fadeOut = 0f)
    {
        if (!h.IsValid || !activeSfx.ContainsKey(h.Id))
        {
            return;
        }

        StopSoundSafe(h.Id, fadeOut);
        activeSfx.Remove(h.Id);
        pausedGameplaySfx.Remove(h.Id);
    }

    public void StopAll()
    {
        foreach (int serialId in persistent.Values)
        {
            StopSoundSafe(serialId, 0f);
        }

        StopAllSfx();
        persistent.Clear();
    }

    public void StopAllSfx()
    {
        foreach (int serialId in activeSfx.Keys)
        {
            StopSoundSafe(serialId, 0f);
        }

        activeSfx.Clear();
        pausedGameplaySfx.Clear();
    }

    [ContextMenu("Test Play Sound")]
    public void TestPlaySound()
    {
        foreach (var entry in activeSfx.Values)
        {
            if (entry.cue == null)
            {
                continue;
            }

            Play(entry.cue);
            Debug.Log($"[AudioManager] Test playing: {entry.cue.name}");
            return;
        }

        Debug.LogWarning("[AudioManager] No active SFX found for test playback.");
    }

    public bool IsPlaying(AudioCue cue)
    {
        return cue != null && persistent.ContainsKey(cue);
    }

    public float Master => masterVol;
    public float Music => musicVol;
    public float Sfx => sfxVol;

    public void SetMaster(float v)
    {
        masterVol = Mathf.Clamp01(v);
        Persist(K_MASTER, masterVol);
        ApplyVolumes();
    }

    public void SetMusic(float v)
    {
        musicVol = Mathf.Clamp01(v);
        Persist(K_MUSIC, musicVol);
        ApplyVolumes();
    }

    public void SetSfx(float v)
    {
        sfxVol = Mathf.Clamp01(v);
        Persist(K_SFX, sfxVol);
        ApplyVolumes();
    }

    private static void Persist(string k, float v)
    {
        PlayerPrefs.SetFloat(k, v);
        PlayerPrefs.Save();
    }

    private void ApplyVolumes()
    {
        SetSoundGroupVolume(Const.SoundGroup.Music, musicVol * masterVol);
        SetSoundGroupVolume(Const.SoundGroup.Sound, sfxVol * masterVol);
    }

    private static void SetSoundGroupVolume(Const.SoundGroup group, float volume)
    {
        if (GF.Sound == null || !GF.Sound.HasSoundGroup(group.ToString()))
        {
            return;
        }

        GF.Sound.GetSoundGroup(group.ToString()).Volume = Mathf.Clamp01(volume);
    }

    private AudioHandle PlaySfxInternal(AudioCue cue)
    {
        AudioClip clip = cue.PickClip();
        if (clip == null)
        {
            Debug.LogWarning($"[AudioManager] AudioCue has no clip: {cue.name}");
            return AudioHandle.Invalid;
        }

        PlaySoundParams playParams = CreatePlaySoundParams(cue, out float pitch);
        int serialId = PlaySound(clip, Const.SoundGroup.Sound.ToString(), playParams, Vector3.zero);
        if (serialId <= 0)
        {
            return AudioHandle.Invalid;
        }

        activeSfx[serialId] = new ActiveSfx { cue = cue, clip = clip, pitch = pitch };
        if (!cue.loop)
        {
            StartCoroutine(ReleaseSfxAfter(serialId, clip, pitch));
        }

        PauseNewSfxIfGamePaused(serialId);
        return new AudioHandle(serialId);
    }

    private AudioHandle PlayPersistentInternal(AudioCue cue)
    {
        if (persistent.ContainsKey(cue))
        {
            return AudioHandle.Invalid;
        }

        AudioClip clip = cue.PickClip();
        if (clip == null)
        {
            Debug.LogWarning($"[AudioManager] AudioCue has no clip: {cue.name}");
            return AudioHandle.Invalid;
        }

        PlaySoundParams playParams = CreatePlaySoundParams(cue, out _);
        int serialId = PlaySound(clip, Const.SoundGroup.Music.ToString(), playParams, Vector3.zero);
        if (serialId <= 0)
        {
            return AudioHandle.Invalid;
        }

        persistent[cue] = serialId;
        return new AudioHandle(serialId);
    }

    private static PlaySoundParams CreatePlaySoundParams(AudioCue cue, out float pitch)
    {
        pitch = cue.SamplePitch();
        PlaySoundParams playParams = PlaySoundParams.Create();
        playParams.Loop = cue.loop;
        playParams.VolumeInSoundGroup = cue.SampleVolume();
        playParams.FadeInSeconds = cue.category == AudioCategory.SFX ? 0f : cue.fadeIn;
        playParams.Pitch = pitch;
        playParams.SpatialBlend = 0f;
        return playParams;
    }

    private static int PlaySound(AudioClip clip, string groupName, PlaySoundParams playParams, Vector3 worldPosition)
    {
        if (GF.Sound == null)
        {
            Debug.LogError("[AudioManager] GF.Sound is invalid.");
            ReferencePool.Release(playParams);
            return 0;
        }

        try
        {
            return GF.Sound.PlaySound(clip, groupName, 0, playParams, worldPosition);
        }
        catch (GameFrameworkException ex)
        {
            ReferencePool.Release(playParams);
            Debug.LogError($"[AudioManager] Play sound failed. clip={(clip != null ? clip.name : "null")}, group={groupName}, error={ex.Message}");
            return 0;
        }
    }

    private static void StopSoundSafe(int serialId, float fadeOut)
    {
        if (GF.Sound == null || serialId <= 0)
        {
            return;
        }

        try
        {
            if (fadeOut > 0f)
            {
                GF.Sound.StopSound(serialId, fadeOut);
            }
            else
            {
                GF.Sound.StopSound(serialId);
            }
        }
        catch (GameFrameworkException)
        {
        }
    }

    private IEnumerator ReleaseSfxAfter(int serialId, AudioClip clip, float pitch)
    {
        if (clip == null)
        {
            yield break;
        }

        float remaining = clip.length / Mathf.Max(0.01f, pitch);
        while (remaining > 0f)
        {
            if (!pausedGameplaySfx.Contains(serialId))
            {
                remaining -= Time.unscaledDeltaTime;
            }

            yield return null;
        }

        activeSfx.Remove(serialId);
        pausedGameplaySfx.Remove(serialId);
    }

    private void PauseNewSfxIfGamePaused(int serialId)
    {
        if (!LogicTimeControlService.IsPaused)
        {
            return;
        }

        pausedGameplaySfx.Add(serialId);
        StartCoroutine(PauseSoundWhenLoaded(serialId));
    }

    private IEnumerator PauseSoundWhenLoaded(int serialId)
    {
        while (GF.Sound != null && GF.Sound.IsLoadingSound(serialId))
        {
            yield return null;
        }

        if (LogicTimeControlService.IsPaused && activeSfx.ContainsKey(serialId))
        {
            PauseSoundSafe(serialId);
        }
    }

    private void PauseActiveGameplaySfx()
    {
        foreach (var kv in activeSfx)
        {
            pausedGameplaySfx.Add(kv.Key);
            StartCoroutine(PauseSoundWhenLoaded(kv.Key));
        }
    }

    private void ResumeActiveGameplaySfx()
    {
        foreach (int serialId in pausedGameplaySfx)
        {
            if (!activeSfx.ContainsKey(serialId))
            {
                continue;
            }

            ResumeSoundSafe(serialId);
        }

        pausedGameplaySfx.Clear();
    }

    private static void PauseSoundSafe(int serialId)
    {
        if (GF.Sound == null || serialId <= 0 || GF.Sound.IsLoadingSound(serialId))
        {
            return;
        }

        try
        {
            GF.Sound.PauseSound(serialId, 0f);
        }
        catch (GameFrameworkException)
        {
        }
    }

    private static void ResumeSoundSafe(int serialId)
    {
        if (GF.Sound == null || serialId <= 0 || GF.Sound.IsLoadingSound(serialId))
        {
            return;
        }

        try
        {
            GF.Sound.ResumeSound(serialId, 0f);
        }
        catch (GameFrameworkException)
        {
        }
    }
}
