using System;
using UnityEngine;

public abstract class SkillEffectSO : ScriptableObject
{
    [Header("基础信息")]
    public string skillId;

    public string SkillId => skillId;

    protected static SkillRuntimeInfo GetRuntimeInfo(string requiredSkillId)
    {
        var skills = SkillRuntimeDataModel.GetUnlockedSkills();
        for (int i = 0; i < skills.Count; i++)
        {
            SkillData data = skills[i].Data;
            if (data != null && string.Equals(data.Identifier, requiredSkillId, StringComparison.Ordinal))
                return skills[i];
        }

        throw new InvalidOperationException($"Skill runtime info not found. skillId={requiredSkillId}");
    }

    protected SkillRuntimeInfo GetRuntimeInfo()
    {
        if (string.IsNullOrWhiteSpace(skillId))
            throw new InvalidOperationException($"{GetType().Name} missing skillId. asset={name}");

        return GetRuntimeInfo(skillId);
    }

    protected Fix64 GetValue(int index)
    {
        SkillRuntimeInfo info = GetRuntimeInfo();
        SkillData data = info.Data;
        if (data == null)
            throw new InvalidOperationException($"SkillData missing. skillId={skillId}");

        return data.GetUniqueValue(index, info.Level);
    }

    protected Fix64 GetCastDistanceValue()
    {
        SkillRuntimeInfo info = GetRuntimeInfo();
        SkillData data = info.Data;
        if (data == null)
            throw new InvalidOperationException($"SkillData missing. skillId={skillId}");

        return data.GetCastDistance(info.Level);
    }

    protected float GetCastDistanceWorld()
    {
        return (float)GetCastDistanceWorldFixed();
    }

    protected Fix64 GetCastDistanceWorldFixed()
    {
        Fix64 value = GetCastDistanceValue();
        return value > Fix64.Zero ? (value) : Fix64.Zero;
    }

    protected Fix64 GetAreaRangeValue()
    {
        SkillRuntimeInfo info = GetRuntimeInfo();
        SkillData data = info.Data;
        if (data == null)
            throw new InvalidOperationException($"SkillData missing. skillId={skillId}");

        return data.GetAreaRange(info.Level);
    }

    protected float GetAreaRangeWorld()
    {
        return (float)GetAreaRangeWorldFixed();
    }

    protected Fix64 GetAreaRangeWorldFixed()
    {
        Fix64 value = GetAreaRangeValue();
        return value > Fix64.Zero ? (value) : Fix64.Zero;
    }

    protected Fix64 GetDurationLogicTime()
    {
        SkillRuntimeInfo info = GetRuntimeInfo();
        SkillData data = info.Data;
        if (data == null)
            throw new InvalidOperationException($"SkillData missing. skillId={skillId}");

        return data.GetDuration(info.Level);
    }
}
