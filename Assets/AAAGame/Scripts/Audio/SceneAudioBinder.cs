using UnityEngine;

// 挂在每个场景的根节点上，自动播放/停止该场景的 BGM 和环境音
public class SceneAudioBinder : MonoBehaviour
{
    public AudioCue bgm;
    public AudioCue ambient;

    void OnEnable()
    {
        var am = AudioManager.Instance;
        if (am == null) return;
        if (bgm)     am.Play(bgm);
        if (ambient) am.Play(ambient);
    }

    void OnDisable()
    {
        var am = AudioManager.Instance;
        if (am == null) return;
        if (bgm)     am.Stop(bgm);
        if (ambient) am.Stop(ambient);
    }
}