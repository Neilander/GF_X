using System;
using System.Collections.Generic;
using AAAGame.Audio;
using UnityEngine;

public class AudioCueLibrary : MonoBehaviour
{
    public static AudioCueLibrary Instance { get; private set; }

    [Serializable]
    public struct Entry
    {
        public string key;
        public AudioCue cue;
    }

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    private readonly Dictionary<string, AudioCue> cueByKey = new();

    private void Awake()
    {
        Instance = this;
        RebuildCache();
    }

    public bool TryGet(string key, out AudioCue cue)
    {
        if (cueByKey.Count == 0 && entries.Length > 0)
            RebuildCache();

        return cueByKey.TryGetValue(key, out cue);
    }

    public AudioCue Get(string key)
    {
        if (TryGet(key, out var cue))
            return cue;

        Debug.LogError($"[AudioCueLibrary] Missing cue key: {key}");
        return null;
    }

    private void RebuildCache()
    {
        cueByKey.Clear();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.key) || entry.cue == null)
                continue;

            cueByKey[entry.key] = entry.cue;
        }
    }
}
