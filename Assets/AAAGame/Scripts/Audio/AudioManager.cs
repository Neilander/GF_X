using System.Collections;
using System.Collections.Generic;
using AAAGame.Audio;
using UnityEngine;
using UnityGameFramework.Runtime;

public class AudioManager : GameFrameworkComponent
{
    public static AudioManager Instance { get; private set; }

    [Header("SFX Pool")]
    [SerializeField] private int sfxPoolSize = 16;

    private const string K_MASTER = "vol_master";
    private const string K_MUSIC = "vol_music";
    private const string K_SFX = "vol_sfx";

    private float masterVol;
    private float musicVol;
    private float sfxVol;

    private readonly Dictionary<AudioCue, AudioSource> persistent = new();
    private readonly List<AudioSource> sfxPool = new();
    private readonly Dictionary<int, ActiveSfx> activeSfx = new();
    private int nextHandleId = 1;

    private struct ActiveSfx
    {
        public AudioSource source;
        public AudioCue cue;
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

        masterVol = PlayerPrefs.GetFloat(K_MASTER, 1f);
        musicVol = PlayerPrefs.GetFloat(K_MUSIC, 1f);
        sfxVol = PlayerPrefs.GetFloat(K_SFX, 1f);

        for (int i = 0; i < sfxPoolSize; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            sfxPool.Add(src);
        }
    }

    public AudioHandle Play(AudioCue cue)
    {
        if (cue == null)
            return AudioHandle.Invalid;

        return cue.category == AudioCategory.SFX
            ? PlaySfxInternal(cue)
            : PlayPersistentInternal(cue);
    }

    public void Stop(AudioCue cue)
    {
        if (cue == null || !persistent.TryGetValue(cue, out var src))
            return;

        if (cue.fadeOut > 0f)
            StartCoroutine(FadeOutPersistent(cue, src));
        else
        {
            Destroy(src);
            persistent.Remove(cue);
        }
    }

    public void StopSfx(AudioHandle h, float fadeOut = 0f)
    {
        if (!h.IsValid || !activeSfx.TryGetValue(h.Id, out var entry))
            return;

        if (fadeOut <= 0f)
        {
            entry.source.Stop();
            activeSfx.Remove(h.Id);
        }
        else
        {
            StartCoroutine(FadeOutSfx(h.Id, entry.source, fadeOut));
        }
    }

    public void StopAll()
    {
        foreach (var kv in persistent)
            Destroy(kv.Value);

        persistent.Clear();

        foreach (var s in activeSfx.Values)
            s.source.Stop();

        activeSfx.Clear();
    }

    [ContextMenu("Test Play Sound")]
    public void TestPlaySound()
    {
        var testSource = gameObject.AddComponent<AudioSource>();
        testSource.volume = 1f;
        testSource.spatialBlend = 0f;

        foreach (var entry in activeSfx.Values)
        {
            if (entry.cue == null || entry.source.clip == null)
                continue;

            testSource.clip = entry.source.clip;
            testSource.Play();
            Debug.Log($"[AudioManager] Test playing: {entry.cue.name}, clip={entry.source.clip.name}");
            Destroy(testSource, 2f);
            return;
        }

        Debug.LogWarning("[AudioManager] No active SFX found for test playback.");
        Destroy(testSource);
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
        foreach (var kv in persistent)
        {
            var cue = kv.Key;
            kv.Value.volume = cue.volume * LayerVol(cue.category) * masterVol;
        }

        foreach (var entry in activeSfx.Values)
            entry.source.volume = entry.cue.volume * sfxVol * masterVol;
    }

    private float LayerVol(AudioCategory c)
    {
        return c switch
        {
            AudioCategory.BGM => musicVol,
            AudioCategory.Ambient => musicVol,
            _ => sfxVol,
        };
    }

    private AudioHandle PlaySfxInternal(AudioCue cue)
    {
        var src = AcquireSfxSource();
        if (src == null)
        {
            Debug.LogWarning($"[AudioManager] SFX pool is empty, cannot play: {cue.name}");
            return AudioHandle.Invalid;
        }

        ConfigureSource(src, cue);

        if (src.clip == null)
        {
            Debug.LogWarning($"[AudioManager] AudioCue has no clip: {cue.name}");
            return AudioHandle.Invalid;
        }

        src.Play();

        if (!src.isPlaying)
            Debug.LogWarning($"[AudioManager] AudioSource.Play finished with isPlaying={src.isPlaying}, clipLength={src.clip.length:F2}s");

        int id = nextHandleId++;
        activeSfx[id] = new ActiveSfx { source = src, cue = cue };
        if (!cue.loop)
            StartCoroutine(ReleaseSfxAfter(id, src));

        return new AudioHandle(id);
    }

    private AudioHandle PlayPersistentInternal(AudioCue cue)
    {
        if (persistent.ContainsKey(cue))
            return AudioHandle.Invalid;

        var src = gameObject.AddComponent<AudioSource>();
        ConfigureSource(src, cue);
        persistent[cue] = src;

        if (cue.fadeIn > 0f)
            StartCoroutine(FadeInPersistent(src, cue.fadeIn));
        else
            src.Play();

        return AudioHandle.Invalid;
    }

    private void ConfigureSource(AudioSource src, AudioCue cue)
    {
        src.clip = cue.PickClip();
        src.volume = cue.SampleVolume() * LayerVol(cue.category) * masterVol;
        src.pitch = cue.SamplePitch();
        src.loop = cue.loop;
        src.spatialBlend = 0f;

        if (src.clip == null)
            Debug.LogWarning($"[AudioManager] AudioCue '{cue.name}' has no configured clip.");
    }

    private AudioSource AcquireSfxSource()
    {
        foreach (var s in sfxPool)
        {
            if (!s.isPlaying)
                return s;
        }

        return sfxPool.Count > 0 ? sfxPool[0] : null;
    }

    private IEnumerator ReleaseSfxAfter(int id, AudioSource src)
    {
        var clip = src.clip;
        if (clip == null)
            yield break;

        yield return new WaitForSeconds(clip.length / Mathf.Max(0.01f, src.pitch));
        if (activeSfx.TryGetValue(id, out var e) && e.source == src)
            activeSfx.Remove(id);
    }

    private IEnumerator FadeInPersistent(AudioSource src, float dur)
    {
        float target = src.volume;
        src.volume = 0f;
        src.Play();
        for (float t = 0; t < dur; t += Time.unscaledDeltaTime)
        {
            src.volume = Mathf.Lerp(0f, target, t / dur);
            yield return null;
        }

        src.volume = target;
    }

    private IEnumerator FadeOutPersistent(AudioCue cue, AudioSource src)
    {
        float start = src.volume;
        for (float t = 0; t < cue.fadeOut; t += Time.unscaledDeltaTime)
        {
            src.volume = Mathf.Lerp(start, 0f, t / cue.fadeOut);
            yield return null;
        }

        Destroy(src);
        persistent.Remove(cue);
    }

    private IEnumerator FadeOutSfx(int id, AudioSource src, float dur)
    {
        float start = src.volume;
        for (float t = 0; t < dur; t += Time.unscaledDeltaTime)
        {
            src.volume = Mathf.Lerp(start, 0f, t / dur);
            yield return null;
        }

        src.Stop();
        activeSfx.Remove(id);
    }
}
