using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AAAGame.Audio;

public class AudioManager : MonoBehaviour {
    public static AudioManager Instance { get; private set; }

    [Header("SFX Pool")]
    [SerializeField] int sfxPoolSize = 16;

    const string K_MASTER = "vol_master";
    const string K_MUSIC  = "vol_music";
    const string K_SFX    = "vol_sfx";

    float masterVol, musicVol, sfxVol;

    readonly Dictionary<AudioCue, AudioSource> persistent = new();
    readonly List<AudioSource> sfxPool = new();
    readonly Dictionary<int, ActiveSfx> activeSfx = new();
    int nextHandleId = 1;

    struct ActiveSfx {
        public AudioSource source;
        public AudioCue cue;
    }

    void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        masterVol = PlayerPrefs.GetFloat(K_MASTER, 1f);
        musicVol  = PlayerPrefs.GetFloat(K_MUSIC, 1f);
        sfxVol    = PlayerPrefs.GetFloat(K_SFX, 1f);

        for (int i = 0; i < sfxPoolSize; i++) {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            sfxPool.Add(src);
        }
    }

    // ===== 播放 =====

    public AudioHandle Play(AudioCue cue) {
        if (cue == null) return AudioHandle.Invalid;
        return cue.category == AudioCategory.SFX
            ? PlaySfxInternal(cue)
            : PlayPersistentInternal(cue);
    }

    public void Stop(AudioCue cue) {
        if (cue == null || !persistent.TryGetValue(cue, out var src)) return;
        if (cue.fadeOut > 0f) StartCoroutine(FadeOutPersistent(cue, src));
        else { Destroy(src); persistent.Remove(cue); }
    }

    public void StopSfx(AudioHandle h, float fadeOut = 0f) {
        if (!h.IsValid || !activeSfx.TryGetValue(h.Id, out var entry)) return;
        if (fadeOut <= 0f) {
            entry.source.Stop();
            activeSfx.Remove(h.Id);
        } else {
            StartCoroutine(FadeOutSfx(h.Id, entry.source, fadeOut));
        }
    }

    public void StopAll() {
        foreach (var kv in persistent) Destroy(kv.Value);
        persistent.Clear();
        foreach (var s in activeSfx.Values) s.source.Stop();
        activeSfx.Clear();
    }

    /// <summary>
    /// 测试音频系统是否正常工作
    /// </summary>
    [ContextMenu("测试音效播放")]
    public void TestPlaySound() {
        Debug.Log("[AudioManager] 手动测试音效播放");
        
        // 创建一个临时的 AudioSource 来测试
        var testSource = gameObject.AddComponent<AudioSource>();
        testSource.volume = 1f;
        testSource.spatialBlend = 0f;
        
        // 尝试播放第一个可用的 SFX
        foreach (var entry in activeSfx.Values) {
            if (entry.cue != null && entry.source.clip != null) {
                testSource.clip = entry.source.clip;
                testSource.Play();
                Debug.Log($"[AudioManager] 测试播放: {entry.cue.name}, 剪辑={entry.source.clip.name}");
                Destroy(testSource, 2f); // 2秒后销毁
                return;
            }
        }
        
        Debug.LogWarning("[AudioManager] 没有找到正在播放的音效进行测试");
        Destroy(testSource);
    }

    public bool IsPlaying(AudioCue cue) =>
        cue != null && persistent.ContainsKey(cue);

    // ===== 音量 =====

    public float Master => masterVol;
    public float Music  => musicVol;
    public float Sfx    => sfxVol;

    public void SetMaster(float v) { masterVol = Mathf.Clamp01(v); Persist(K_MASTER, masterVol); ApplyVolumes(); }
    public void SetMusic (float v) { musicVol  = Mathf.Clamp01(v); Persist(K_MUSIC,  musicVol);  ApplyVolumes(); }
    public void SetSfx   (float v) { sfxVol    = Mathf.Clamp01(v); Persist(K_SFX,    sfxVol);    ApplyVolumes(); }

    static void Persist(string k, float v) {
        PlayerPrefs.SetFloat(k, v);
        PlayerPrefs.Save();
    }

    void ApplyVolumes() {
        foreach (var kv in persistent) {
            var cue = kv.Key;
            kv.Value.volume = cue.volume * LayerVol(cue.category) * masterVol;
        }
        foreach (var entry in activeSfx.Values) {
            entry.source.volume = entry.cue.volume * sfxVol * masterVol;
        }
    }

    float LayerVol(AudioCategory c) => c switch {
        AudioCategory.BGM     => musicVol,
        AudioCategory.Ambient => musicVol,
        _                     => sfxVol,
    };

    // ===== 内部 =====

    AudioHandle PlaySfxInternal(AudioCue cue) {
        var src = AcquireSfxSource();
        if (src == null) {
            Debug.LogWarning($"[AudioManager] SFX池为空，无法播放音效: {cue.name}");
            return AudioHandle.Invalid;
        }

        ConfigureSource(src, cue);
        
        if (src.clip == null) {
            Debug.LogWarning($"[AudioManager] 音频剪辑为空，无法播放音效: {cue.name}");
            return AudioHandle.Invalid;
        }
        
        src.Play();
        
        // 检查是否真的在播放
        if (!src.isPlaying) {
            Debug.LogWarning($"[AudioManager] AudioSource.Play() 调用后 isPlaying={src.isPlaying}, 剪辑长度={src.clip.length:F2}秒");
        } else {
            Debug.Log($"[AudioManager] 开始播放音效: {cue.name}, 剪辑={src.clip.name}, 长度={src.clip.length:F2}秒");
        }

        int id = nextHandleId++;
        activeSfx[id] = new ActiveSfx { source = src, cue = cue };
        if (!cue.loop) StartCoroutine(ReleaseSfxAfter(id, src));
        return new AudioHandle(id);
    }

    AudioHandle PlayPersistentInternal(AudioCue cue) {
        if (persistent.ContainsKey(cue)) return AudioHandle.Invalid;
        var src = gameObject.AddComponent<AudioSource>();
        ConfigureSource(src, cue);
        persistent[cue] = src;

        if (cue.fadeIn > 0f) StartCoroutine(FadeInPersistent(src, cue.fadeIn));
        else src.Play();
        return AudioHandle.Invalid;
    }

    void ConfigureSource(AudioSource src, AudioCue cue) {
        src.clip   = cue.PickClip();
        src.volume = cue.SampleVolume() * LayerVol(cue.category) * masterVol;
        src.pitch  = cue.SamplePitch();
        src.loop   = cue.loop;
        src.spatialBlend = 0f; // 强制2D音效，避免3D音效听不到的问题
        
        // 检查是否有音频剪辑
        if (src.clip == null) {
            Debug.LogWarning($"[AudioManager] AudioCue '{cue.name}' 没有配置音频剪辑！");
        } else {
            Debug.Log($"[AudioManager] 配置音效: {cue.name}, Clip={src.clip.name}, Volume={src.volume:F3}, Loop={src.loop}");
        }
    }

    AudioSource AcquireSfxSource() {
        foreach (var s in sfxPool) if (!s.isPlaying) return s;
        return sfxPool[0]; // fallback: 抢占最旧
    }

    IEnumerator ReleaseSfxAfter(int id, AudioSource src) {
        var clip = src.clip;
        if (clip == null) yield break;
        yield return new WaitForSeconds(clip.length / Mathf.Max(0.01f, src.pitch));
        if (activeSfx.TryGetValue(id, out var e) && e.source == src) {
            activeSfx.Remove(id);
        }
    }

    IEnumerator FadeInPersistent(AudioSource src, float dur) {
        float target = src.volume;
        src.volume = 0f;
        src.Play();
        for (float t = 0; t < dur; t += Time.unscaledDeltaTime) {
            src.volume = Mathf.Lerp(0f, target, t / dur);
            yield return null;
        }
        src.volume = target;
    }

    IEnumerator FadeOutPersistent(AudioCue cue, AudioSource src) {
        float start = src.volume;
        for (float t = 0; t < cue.fadeOut; t += Time.unscaledDeltaTime) {
            src.volume = Mathf.Lerp(start, 0f, t / cue.fadeOut);
            yield return null;
        }
        Destroy(src);
        persistent.Remove(cue);
    }

    IEnumerator FadeOutSfx(int id, AudioSource src, float dur) {
        float start = src.volume;
        for (float t = 0; t < dur; t += Time.unscaledDeltaTime) {
            src.volume = Mathf.Lerp(start, 0f, t / dur);
            yield return null;
        }
        src.Stop();
        activeSfx.Remove(id);
    }
}