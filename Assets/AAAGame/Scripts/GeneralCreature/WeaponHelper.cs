using System.Collections.Generic;
using System;
using UnityEngine;
using GameFramework.Resource;

public static class WeaponHelper
{
    static Dictionary<string, BaseWeaponSO> _weapons = new();

    /// <summary>
    /// 加载武器 SO 并注入到 DirectAtkComp。
    /// 已缓存则同步注入，否则异步加载后缓存再注入。
    /// </summary>
    public static void LoadWeapon(string weaponPath, DirectAtkComp comp)
    {
        if (string.IsNullOrWhiteSpace(weaponPath))
            throw new InvalidOperationException("WeaponHelper.LoadWeapon failed: weaponPath is empty.");
        if (comp == null)
            throw new InvalidOperationException($"WeaponHelper.LoadWeapon failed: DirectAtkComp is null. path={weaponPath}");

        if (_weapons.TryGetValue(weaponPath, out var weapon))
        {
            comp.SetWeaponSO(weapon);
            return;
        }

#if UNITY_EDITOR
        if (GF.Resource == null && !Application.isPlaying)
        {
            BaseWeaponSO editorWeapon = UnityEditor.AssetDatabase.LoadAssetAtPath<BaseWeaponSO>(weaponPath);
            if (editorWeapon == null)
                throw new InvalidOperationException($"WeaponHelper.LoadWeapon failed: editor asset not found at {weaponPath}.");

            _weapons[weaponPath] = editorWeapon;
            comp.SetWeaponSO(editorWeapon);
            return;
        }
#endif

        if (GF.Resource == null)
            throw new InvalidOperationException($"WeaponHelper.LoadWeapon failed: GF.Resource is null. path={weaponPath}");

        var callback = WrapWithCache(weaponPath);
        GF.Resource.LoadAsset(weaponPath, callback, comp);
    }

    static LoadAssetCallbacks WrapWithCache(string path)
    {
        return new LoadAssetCallbacks(
            (assetName, asset, duration, userData) =>
            {
                if (asset is BaseWeaponSO weapon)
                {
                    if (!_weapons.ContainsKey(path))
                    {
                        _weapons[path] = weapon;
                    }

                    if (userData is DirectAtkComp comp)
                    {
                        comp.SetWeaponSO(weapon);
                    }
                }
            },
            (assetName, status, errorMessage, userData) =>
            {
            }
        );
    }
}
