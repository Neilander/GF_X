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
    private static UnitWeaponSOOverrideConfig s_Config;

    public static string GetWeaponSOPath(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey))
        {
            return string.Empty;
        }

        if (s_Config == null)
        {
            s_Config = Resources.Load<UnitWeaponSOOverrideConfig>(ResourcePath);
        }

        if (s_Config == null)
        {
            return string.Empty;
        }

        return s_Config.TryGetWeaponSOPath(characterKey, out var path) ? path : string.Empty;
    }
}
