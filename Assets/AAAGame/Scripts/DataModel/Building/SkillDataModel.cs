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

    protected override void OnCreate(RefParams userdata)
    {
        skillDataDic = new();
        foreach (SkillTable row in LogicRuntimeDataTableCache.SkillRows)
        {
            ImportSkillDataRow(row);
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
