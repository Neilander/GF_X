using UnityEngine;
using AAAGame.Audio;

[CreateAssetMenu(fileName = "Cue_", menuName = "Audio/Audio Cue")]
public class AudioCue : ScriptableObject
{
    public AudioCategory category = AudioCategory.SFX;

    [Tooltip("多个 clip 时随机选一个播放")]
    public AudioClip[] clips;

    [Range(0f, 1f)] public float volume = 1f;
    [Range(0f, 0.5f)] public float volumeVariance = 0f;

    [Range(0.5f, 2f)] public float pitch = 1f;
    [Range(0f, 0.5f)] public float pitchVariance = 0f;

    public bool loop = false;

    [Tooltip("仅对 BGM/Ambient 生效")]
    public float fadeIn = 0f;
    public float fadeOut = 0f;

    public AudioClip PickClip()
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }

    public float SampleVolume() =>
        Mathf.Clamp01(volume + Random.Range(-volumeVariance, volumeVariance));

    public float SamplePitch() =>
        pitch + Random.Range(-pitchVariance, pitchVariance);
}
