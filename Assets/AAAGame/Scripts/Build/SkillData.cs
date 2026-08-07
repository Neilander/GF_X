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
/// 技能数据格式类
/// </summary>
public class SkillData
{
    public string Identifier { get; protected set; }
    public Fix64[] Lv1UniqueValues { get; protected set; }
    public Fix64 Lv1CastDistance { get; protected set; }
    public Fix64 Lv1AreaRange { get; protected set; }
    public Fix64 Lv1Duration { get; protected set; }
    public int Lv1UsageCount { get; protected set; }
    public Fix64[] UpgradeIncrementUniqueValues { get; protected set; }
    public Fix64 UpgradeIncrementCastDistance { get; protected set; }
    public Fix64 UpgradeIncrementAreaRange { get; protected set; }
    public Fix64 UpgradeIncrementDuration { get; protected set; }
    public int UpgradeIncrementUsageCount { get; protected set; }
    public Fix64 Lv1Cooldown { get; protected set; }
    public Fix64 UpgradeDecrementCooldown { get; protected set; }
    public SkillType Type { get; protected set; }
    public string NameKey { get; protected set; }
    public string DescKey { get; protected set; }
    public string SpritePath { get; protected set; }

    public SkillData(string identifier,
        Fix64[] lv1UniqueValues,
        Fix64 lv1CastDistance,
        Fix64 lv1AreaRange,
        Fix64 lv1Duration,
        int lv1UsageCount,
        Fix64[] upgradeIncrementUniqueValues,
        Fix64 upgradeIncrementCastDistance,
        Fix64 upgradeIncrementAreaRange,
        Fix64 upgradeIncrementDuration,
        int upgradeIncrementUsageCount,
        Fix64 lv1Cooldown,
        Fix64 upgradeDecrementCooldown,
        SkillType type,
        string nameKey,
        string descKey,
        string spritePath)
    {
        Identifier = identifier;
        Lv1UniqueValues = lv1UniqueValues != null ? (Fix64[])lv1UniqueValues.Clone() : null;
        Lv1CastDistance = lv1CastDistance;
        Lv1AreaRange = lv1AreaRange;
        Lv1Duration = lv1Duration;
        Lv1UsageCount = lv1UsageCount;
        UpgradeIncrementUniqueValues = upgradeIncrementUniqueValues != null
            ? (Fix64[])upgradeIncrementUniqueValues.Clone()
            : null;
        UpgradeIncrementCastDistance = upgradeIncrementCastDistance;
        UpgradeIncrementAreaRange = upgradeIncrementAreaRange;
        UpgradeIncrementDuration = upgradeIncrementDuration;
        UpgradeIncrementUsageCount = upgradeIncrementUsageCount;
        Lv1Cooldown = lv1Cooldown;
        UpgradeDecrementCooldown = upgradeDecrementCooldown;
        Type = type;
        NameKey = nameKey;
        DescKey = descKey;
        SpritePath = spritePath;
    }

    public Fix64 GetUniqueValue(int index, int level)
    {
        if (Lv1UniqueValues == null || index < 0 || index >= Lv1UniqueValues.Length)
            throw new InvalidOperationException($"Skill unique value missing. skillId={Identifier}, index={index}");

        Fix64 increment = UpgradeIncrementUniqueValues != null && index < UpgradeIncrementUniqueValues.Length
            ? UpgradeIncrementUniqueValues[index]
            : Fix64.Zero;
        return Lv1UniqueValues[index] + increment * (level - 1);
    }

    public Fix64 GetCastDistance(int level)
    {
        return Lv1CastDistance + UpgradeIncrementCastDistance * (level - 1);
    }

    public Fix64 GetAreaRange(int level)
    {
        return Lv1AreaRange + UpgradeIncrementAreaRange * (level - 1);
    }

    public Fix64 GetDuration(int level)
    {
        return Lv1Duration + UpgradeIncrementDuration * (level - 1);
    }

    public Fix64 GetCooldown(int level)
    {
        Fix64 cooldown = Lv1Cooldown - UpgradeDecrementCooldown * (level - 1);
        return cooldown > Fix64.Zero ? cooldown : Fix64.Zero;
    }
}
