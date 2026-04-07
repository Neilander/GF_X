using System.Collections.Generic;
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
        if (_weapons.TryGetValue(weaponPath, out var weapon))
        {
            comp.SetWeaponSO(weapon);
            return;
        }

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
