using GameFramework;
using GameFramework.Event;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 建筑数据模型类, 预读各建筑数据
/// </summary>
public class BuildingDataModel : DataModelBase
{
    private Dictionary<string, BuildingData> buildingDataDic;

    protected override void OnCreate(RefParams userdata)
    {
        buildingDataDic = new();
        var buildingTb = GF.DataTable.GetDataTable<BuildingTable>();
        foreach (var row in buildingTb.GetAllDataRows())
        {
            ImportBuildingDataRow(row);
        }
    }

    protected override void OnRelease() { }

    public static BuildingData GetBuildingData(string buildingIdentifier)
    {
        var buildingDataModel = GF.DataModel.GetDataModel<BuildingDataModel>();
        if (buildingDataModel.buildingDataDic.TryGetValue(buildingIdentifier, out var building)) return building;
        return null;
    }

    public static bool TryResolvePresetIdentifier(string identifier, out string resolvedIdentifier)
    {
        resolvedIdentifier = null;
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        identifier = identifier.Trim();
        if (GetBuildingData(identifier) != null)
        {
            resolvedIdentifier = identifier;
            return true;
        }

        string levelOneIdentifier = identifier + "_Lv1";
        if (GetBuildingData(levelOneIdentifier) != null)
        {
            resolvedIdentifier = levelOneIdentifier;
            return true;
        }

        string levelZeroIdentifier = identifier + "_Lv0";
        if (GetBuildingData(levelZeroIdentifier) != null)
        {
            resolvedIdentifier = levelZeroIdentifier;
            return true;
        }

        return false;
    }

    public static IEnumerable<BuildingData> GetAllBuildingData()
    {
        var buildingDataModel = GF.DataModel.GetDataModel<BuildingDataModel>();
        return buildingDataModel.buildingDataDic.Values;
    }


    public static string GetUpgradeID(string identifier)
    {
        // 升级ID格式约定：在原ID基础上替换等级数字，如 "Buil_Prod_Lv2" 的升级ID为 "Buil_Prod_Lv3"。若该升级ID在字典中不存在，则返回 null。
        int lastLvIndex = identifier.LastIndexOf("Lv", StringComparison.Ordinal);
        if (lastLvIndex < 0 || lastLvIndex + 2 >= identifier.Length)
            return null;

        string prefix = identifier.Substring(0, lastLvIndex + 2); // 包含 "Lv"
        string lvStr = identifier.Substring(lastLvIndex + 2);
        if (!int.TryParse(lvStr, out int lv))
            return null;

        int nextLv = lv + 1;
        string nextIdentifier = prefix + nextLv;
        return BuildingDataModel.GetBuildingData(nextIdentifier) != null ? nextIdentifier : null;
    }

    private void ImportBuildingDataRow(BuildingTable row)
    {
        if (row.Identifier.Substring(row.Identifier.Length - 3) == "Lv0")
        {
            int production = ResolveProduction(row, 1);
            BuildingData building = new(row.Identifier,
                                row.Type,
                                row.Archetype,
                                row.Lv1PrefabPath,
                                row.NameKey,
                                row.DescKey,
                                0,
                                0,
                                Fix64.One,
                                null,
                                Fix64.Zero,
                                row.UniqueValues,
                                row.UnitID,
                                production,
                                null);
            buildingDataDic[building.Identifier] = building;
        }
        else
        {
            int maxLv = row.Type == BuilType.Tech ? 1 : 3;
            for (int lv = 1; lv <= maxLv; lv++)
            {
                WeaponData weaponData = ResolveWeaponData(row, lv);

                BuildingData building = new(row.Identifier + "_Lv" + lv,
                                    row.Type,
                                    row.Archetype,
                                    ResolvePrefabPath(row, lv),
                                    row.NameKey,
                                    row.DescKey,
                                    lv,
                                    lv == 1 ? row.Lv1Cost : lv == 2 ? row.Lv2Cost : row.Lv3Cost,
                                    ResolveHP(row, lv),
                                    weaponData,
                                    ResolveDef(row, lv),
                                    row.UniqueValues,
                                    row.UnitID,
                                    ResolveProduction(row, lv),
                                    lv == 3 ? null :
                                    row.Type == BuilType.Base || row.Type == BuilType.Army || row.Type == BuilType.Def ?
                                    lv == 1 ? new string[] { row.Tech1ID, row.Tech2ID } : new string[] { row.Tech3ID, row.Tech4ID } :
                                    row.Type == BuilType.Prod ? lv == 1 ? new string[] { row.Tech1ID } : new string[] { row.Tech2ID } :
                                    row.Type == BuilType.Tech ? new string[] { row.Tech1ID, row.Tech2ID, row.Tech3ID, row.Tech4ID } : null);
                buildingDataDic[building.Identifier] = building;
            }
        }
    }

    private static string ResolvePrefabPath(BuildingTable row, int lv)
    {
        if (lv <= 1) return row.Lv1PrefabPath;
        if (lv == 2) return PickString(row.Lv2PrefabPath, row.Lv1PrefabPath);

        string lv2 = PickString(row.Lv2PrefabPath, row.Lv1PrefabPath);
        return PickString(row.Lv3PrefabPath, lv2);
    }

    private static Fix64 ResolveHP(BuildingTable row, int lv)
    {
        return PickLevelValue(lv, row.Lv1HP, row.Lv2HP, row.Lv3HP);
    }

    private static Fix64 ResolveDef(BuildingTable row, int lv)
    {
        return PickLevelValue(lv, row.Lv1Def, row.Lv2Def, row.Lv3Def);
    }

    private static int ResolveProduction(BuildingTable row, int lv)
    {
        return PickLevelValue(lv, row.Lv1Production, row.Lv2Production, row.Lv3Production);
    }

    private static WeaponData ResolveWeaponData(BuildingTable row, int lv)
    {
        WeaponData lv1 = new WeaponData(
            row.Lv1Weapon1Type,
            row.Lv1Weapon1Atk,
            row.Lv1Weapon1Interval,
            row.Lv1Weapon1Range,
            row.Lv1Weapon1ProjectileSpeed,
            row.Lv1Weapon1WindUp,
            row.Lv1Weapon1WindDown,
            row.Lv1Weapon1SplashRadius,
            row.Lv1Weapon1SplitAngle,
            row.Lv1Weapon1SplitDist,
            row.Lv1Weapon1ProjectileCount,
            row.Lv1Weapon1AmmunitionCapacity,
            row.Lv1Weapon1UniqueValues);

        WeaponData resolved = lv1;
        if (lv >= 2)
        {
            resolved = WithFallback(
                resolved,
                row.Lv2Weapon1Type,
                row.Lv2Weapon1Atk,
                row.Lv2Weapon1Interval,
                row.Lv2Weapon1Range,
                row.Lv2Weapon1ProjectileSpeed,
                row.Lv2Weapon1WindUp,
                row.Lv2Weapon1WindDown,
                row.Lv2Weapon1SplashRadius,
                row.Lv2Weapon1SplitAngle,
                row.Lv2Weapon1SplitDist,
                row.Lv2Weapon1ProjectileCount,
                row.Lv2Weapon1AmmunitionCapacity,
                row.Lv2Weapon1UniqueValues);
        }

        if (lv >= 3)
        {
            resolved = WithFallback(
                resolved,
                row.Lv3Weapon1Type,
                row.Lv3Weapon1Atk,
                row.Lv3Weapon1Interval,
                row.Lv3Weapon1Range,
                row.Lv3Weapon1ProjectileSpeed,
                row.Lv3Weapon1WindUp,
                row.Lv3Weapon1WindDown,
                row.Lv3Weapon1SplashRadius,
                row.Lv3Weapon1SplitAngle,
                row.Lv3Weapon1SplitDist,
                row.Lv3Weapon1ProjectileCount,
                row.Lv3Weapon1AmmunitionCapacity,
                row.Lv3Weapon1UniqueValues);
        }

        if (row.Identifier == BuildingAbilityIds.Restroom && resolved.Range > Fix64.Zero)
        {
            return new WeaponData(
                WeaponType.Special,
                Fix64.Zero,
                resolved.Interval,
                resolved.Range,
                resolved.ProjectileSpeed,
                resolved.WindUp,
                resolved.WindDown,
                resolved.SplashRadius,
                resolved.SplitAngle,
                resolved.SplitDist,
                resolved.ProjectileCount,
                resolved.AmmunitionCapacity,
                resolved.UniqueValues);
        }

        return resolved.Atk > Fix64.Zero ? resolved : null;
    }

    private static WeaponData WithFallback(
        WeaponData previous,
        WeaponType type,
        Fix64 atk,
        Fix64 interval,
        Fix64 range,
        Fix64 projectileSpeed,
        Fix64 windUp,
        Fix64 windDown,
        Fix64 splashRadius,
        Fix64 splitAngle,
        Fix64 splitDist,
        Fix64 projectileCount,
        Fix64 ammunitionCapacity,
        Fix64[] uniqueValues)
    {
        return new WeaponData(
            type != default ? type : previous.Type,
            PickValue(atk, previous.Atk),
            PickValue(interval, previous.Interval),
            PickValue(range, previous.Range),
            PickValue(projectileSpeed, previous.ProjectileSpeed),
            PickValue(windUp, previous.WindUp),
            PickValue(windDown, previous.WindDown),
            PickValue(splashRadius, previous.SplashRadius),
            PickValue(splitAngle, previous.SplitAngle),
            PickValue(splitDist, previous.SplitDist),
            PickValue(projectileCount, previous.ProjectileCount),
            PickValue(ammunitionCapacity, previous.AmmunitionCapacity),
            uniqueValues != null && uniqueValues.Length > 0 ? uniqueValues : previous.UniqueValues);
    }

    private static Fix64 PickLevelValue(int lv, Fix64 lv1, Fix64 lv2, Fix64 lv3)
    {
        if (lv <= 1) return lv1;
        if (lv == 2) return PickValue(lv2, lv1);

        Fix64 resolvedLv2 = PickValue(lv2, lv1);
        return PickValue(lv3, resolvedLv2);
    }

    private static int PickLevelValue(int lv, int lv1, int lv2, int lv3)
    {
        if (lv <= 1) return lv1;
        if (lv == 2) return lv2 != 0 ? lv2 : lv1;

        int resolvedLv2 = lv2 != 0 ? lv2 : lv1;
        return lv3 != 0 ? lv3 : resolvedLv2;
    }

    private static Fix64 PickValue(Fix64 value, Fix64 fallback)
    {
        return value != Fix64.Zero ? value : fallback;
    }

    private static string PickString(string value, string fallback)
    {
        return !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }
}
