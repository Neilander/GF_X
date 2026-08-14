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
/// 技能数据模型类, 预读各技能数据
/// </summary>
public class SkillDataModel : DataModelBase
{
    private Dictionary<string, SkillData> skillDataDic;
    private Dictionary<string, Archetype> skillIndustryDic;

    protected override void OnCreate(RefParams userdata)
    {
        skillDataDic = new();
        foreach (SkillTable row in LogicRuntimeDataTableCache.SkillRows)
        {
            ImportSkillDataRow(row);
        }

        skillIndustryDic = BuildSkillIndustryMap(LogicRuntimeDataTableCache.BuildingRows);
        foreach (string skillId in skillDataDic.Keys)
        {
            if (!skillIndustryDic.ContainsKey(skillId))
                throw new InvalidOperationException($"Skill has no building industry mapping. skillId={skillId}");
        }
    }

    protected override void OnRelease() { }

    public static SkillData GetSkillData(string skillIdentifier)
    {
        var skillDataModel = GF.DataModel.GetDataModel<SkillDataModel>();
        if (skillDataModel.skillDataDic.TryGetValue(skillIdentifier, out var skill)) return skill;
        return null;
    }

    public static string GetSkillDesc(string skillIdentifier, int level = 1)
    {
        var skillData = GetSkillData(skillIdentifier);
        return skillData != null ? skillData.GetFormattedDesc(level) : string.Empty;
    }

    public static Archetype GetIndustryRequired(string skillIdentifier)
    {
        if (string.IsNullOrWhiteSpace(skillIdentifier))
            throw new ArgumentException("Skill identifier is required.", nameof(skillIdentifier));

        SkillDataModel model = GF.DataModel.GetDataModel<SkillDataModel>()
                               ?? throw new InvalidOperationException("SkillDataModel is not available.");
        if (!model.skillIndustryDic.TryGetValue(skillIdentifier, out Archetype industry))
            throw new InvalidOperationException($"Skill has no building industry mapping. skillId={skillIdentifier}");
        return industry;
    }

    public static int GetIndustryOrderIndexRequired(string skillIdentifier)
    {
        return CareerConfigRuntime.GetArchetypeOrderIndexRequired(GetIndustryRequired(skillIdentifier));
    }

    internal static Dictionary<string, Archetype> BuildSkillIndustryMap(IEnumerable<BuildingTable> rows)
    {
        if (rows == null)
            throw new ArgumentNullException(nameof(rows));

        var result = new Dictionary<string, Archetype>(StringComparer.Ordinal);
        foreach (BuildingTable row in rows)
        {
            if (row == null)
                throw new InvalidOperationException("BuildingTable contains a null row.");

            RegisterSkillTech(result, row, row.Tech1ScopeType, row.Tech1SkillID, 1);
            RegisterSkillTech(result, row, row.Tech2ScopeType, row.Tech2SkillID, 2);
            RegisterSkillTech(result, row, row.Tech3ScopeType, row.Tech3SkillID, 3);
            RegisterSkillTech(result, row, row.Tech4ScopeType, row.Tech4SkillID, 4);
        }

        return result;
    }

    private static void RegisterSkillTech(
        Dictionary<string, Archetype> result,
        BuildingTable row,
        TechScopeType scopeType,
        string skillId,
        int techSlot)
    {
        if (scopeType != TechScopeType.Skill)
            return;
        if (string.IsNullOrWhiteSpace(skillId))
            throw new InvalidOperationException($"Skill tech has empty SkillID. building={row.Identifier}, slot={techSlot}");
        if (row.Archetype == Archetype.None || row.Archetype == Archetype.Common)
            throw new InvalidOperationException($"Skill tech requires a dedicated industry. building={row.Identifier}, skill={skillId}");

        if (result.TryGetValue(skillId, out Archetype existing) && existing != row.Archetype)
        {
            throw new InvalidOperationException(
                $"Skill is mapped to multiple industries. skill={skillId}, first={existing}, second={row.Archetype}");
        }

        result[skillId] = row.Archetype;
    }

    private void ImportSkillDataRow(SkillTable row)
    {
        if (string.IsNullOrEmpty(row.Identifier)) return;
        SkillData skill = new(row.Identifier,
                              row.Lv1UniqueValues,
                              row.Lv1CastRange,
                              row.Lv1EffectRadius,
                              row.Lv1Duration,
                              row.Lv1UsageCount,
                              row.UpgradeIncrementUniqueValues,
                              row.UpgradeIncrementCastRange,
                              row.UpgradeIncrementEffectRadius,
                              row.UpgradeIncrementDuration,
                              row.UpgradeIncrementUsageCount,
                              row.Lv1Cooldown,
                              row.UpgradeDecrementCooldown,
                              row.Type,
                              row.NameKey,
                              row.DescKey,
                              row.SpritePath);
        skillDataDic[skill.Identifier] = skill;
    }
}
