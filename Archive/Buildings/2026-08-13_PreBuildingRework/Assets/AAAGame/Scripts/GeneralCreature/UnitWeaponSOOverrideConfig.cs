using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class UnitWeaponSOOverrideEntry
{
    public string characterKey;
    public string weaponSOPath;
}

public class UnitWeaponSOOverrideConfig : ScriptableObject
{
    [SerializeField] private List<UnitWeaponSOOverrideEntry> _entries = new();

    private Dictionary<string, string> _cache;

    public List<UnitWeaponSOOverrideEntry> Entries => _entries;

    public bool TryGetWeaponSOPath(string characterKey, out string weaponSOPath)
    {
        weaponSOPath = string.Empty;
        if (string.IsNullOrWhiteSpace(characterKey))
        {
            return false;
        }

        EnsureCache();
        return _cache.TryGetValue(characterKey, out weaponSOPath) && !string.IsNullOrWhiteSpace(weaponSOPath);
    }

    public void MarkDirty()
    {
        _cache = null;
    }

    private void EnsureCache()
    {
        if (_cache != null)
        {
            return;
        }

        _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_entries == null)
        {
            return;
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.characterKey))
            {
                continue;
            }

            _cache[entry.characterKey] = entry.weaponSOPath;
        }
    }
}

public static class UnitWeaponSOOverrideResolver
{
    private const string ResourcePath = "UnitWeaponSOOverrideConfig";
    private static readonly Dictionary<string, string> s_PathsByCharacterKey =
        new(StringComparer.Ordinal);
    private static bool s_RuntimeMappingsPrepared;

    public static void PrepareRuntimeMappings()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("Weapon presentation overrides cannot be prepared during a logic frame.");

        UnitWeaponSOOverrideConfig config = Resources.Load<UnitWeaponSOOverrideConfig>(ResourcePath);
        s_PathsByCharacterKey.Clear();
        if (config != null)
        {
            IReadOnlyList<UnitWeaponSOOverrideEntry> entries = config.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                UnitWeaponSOOverrideEntry entry = entries[i]
                                                  ?? throw new InvalidOperationException(
                                                      $"Weapon presentation override entry is null. index={i}.");
                if (string.IsNullOrWhiteSpace(entry.characterKey))
                    throw new InvalidOperationException($"Weapon presentation override has an empty character key. index={i}.");
                if (!s_PathsByCharacterKey.TryAdd(entry.characterKey, entry.weaponSOPath ?? string.Empty))
                    throw new InvalidOperationException(
                        $"Weapon presentation override has duplicate character key '{entry.characterKey}'.");
            }
        }

        s_RuntimeMappingsPrepared = true;
    }

    public static string GetWeaponSOPath(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey))
            return string.Empty;
        if (!s_RuntimeMappingsPrepared)
            throw new InvalidOperationException("Weapon presentation override mappings were not prepared.");

        return s_PathsByCharacterKey.TryGetValue(characterKey, out string path)
               && !string.IsNullOrWhiteSpace(path)
            ? path
            : string.Empty;
    }
}
