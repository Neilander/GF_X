using GameFramework;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

/// <summary>
/// 科技进度（持久化）：只记录“已研究的科技”。
/// 解锁内容（配方/建造/功能）由研究成功后的 Effects 驱动（后续接入）。
/// </summary>
public class TechProgressDataModel : DataModelStorageBase
{
    [JsonProperty]
    private HashSet<string> m_UnlockedTechIds;

    [JsonProperty]
    private List<string> m_UnlockOrder;

    [JsonProperty]
    private Dictionary<string, long> m_UnlockTimeTicks;

    protected override void OnInitialDataModel()
    {
        m_UnlockedTechIds = new HashSet<string>();
        m_UnlockOrder = new List<string>();
        m_UnlockTimeTicks = new Dictionary<string, long>();
    }

    public static bool IsUnlocked(string techId)
    {
        var dm = GF.DataModel.GetOrCreate<TechProgressDataModel>();
        return !string.IsNullOrWhiteSpace(techId) && dm.m_UnlockedTechIds.Contains(techId);
    }

    public static bool CanResearch(string techId, out TechResearchFailReason failReason)
    {
        failReason = TechResearchFailReason.None;
        if (string.IsNullOrWhiteSpace(techId))
        {
            failReason = TechResearchFailReason.InvalidTechId;
            return false;
        }

        var data = TechTreeDataModel.GetNodeData(techId);
        if (data == null)
        {
            failReason = TechResearchFailReason.TechNotFound;
            return false;
        }

        if (IsUnlocked(techId))
        {
            failReason = TechResearchFailReason.AlreadyUnlocked;
            return false;
        }

        // 必备条件：基地等级 >= 节点 Level（由表中 TechLevel 决定）
        var baseDm = GF.DataModel.GetOrCreate<BaseProgressDataModel>();
        if (baseDm.BaseLevel < data.Level)
        {
            failReason = TechResearchFailReason.BaseLevelTooLow;
            return false;
        }

        // 前置科技（推荐同时在表里有 PrereqTechIds + ConditionsAll 里的 HasTech；这里优先用 PrereqTechIds）
        if (data.PrereqTechIds != null)
        {
            for (int i = 0; i < data.PrereqTechIds.Length; i++)
            {
                var prereq = data.PrereqTechIds[i];
                if (!string.IsNullOrWhiteSpace(prereq) && !IsUnlocked(prereq))
                {
                    failReason = TechResearchFailReason.MissingPrereqTech;
                    return false;
                }
            }
        }

        // 条件 - AND（不需要配置 BaseLevelGE；若配置了，也会单独判定，不影响必备等级校验）
        if (data.AllConditions != null)
        {
            for (int i = 0; i < data.AllConditions.Length; i++)
            {
                if (!EvaluateCondition(data.AllConditions[i], out failReason))
                {
                    if (failReason == TechResearchFailReason.None)
                    {
                        failReason = TechResearchFailReason.ConditionNotMet;
                    }
                    return false;
                }
            }
        }

        // 条件 - OR（有配置时要求至少满足一个）
        if (data.AnyConditions != null && data.AnyConditions.Length > 0)
        {
            bool anyOk = false;
            for (int i = 0; i < data.AnyConditions.Length; i++)
            {
                if (EvaluateCondition(data.AnyConditions[i], out _))
                {
                    anyOk = true;
                    break;
                }
            }
            if (!anyOk)
            {
                failReason = TechResearchFailReason.ConditionNotMet;
                return false;
            }
        }

        // 成本检查（物品）
        if (!ItemCollectionDataModel.HasItem(data.CostMaterial))
        {
            failReason = TechResearchFailReason.NotEnoughCost;
            return false;
        }

        return true;
    }

    public static bool Research(string techId)
    {
        if (!CanResearch(techId, out var reason))
        {
            GF.Event.Fire(GF.DataModel.GetOrCreate<TechProgressDataModel>(), TechResearchFailedEventArgs.Create(techId, reason));
            return false;
        }

        var data = TechTreeDataModel.GetNodeData(techId);
        if (data == null)
        {
            GF.Event.Fire(GF.DataModel.GetOrCreate<TechProgressDataModel>(), TechResearchFailedEventArgs.Create(techId, TechResearchFailReason.TechNotFound));
            return false;
        }

        // 扣除成本（物品）
        if (data.CostMaterial != null)
        {
            for (int i = 0; i < data.CostMaterial.Length; i++)
            {
                var cost = data.CostMaterial[i];
                ItemCollectionDataModel.ModifyItemAmount(cost.str, -cost.num);
            }
        }

        var dm = GF.DataModel.GetOrCreate<TechProgressDataModel>();
        dm.m_UnlockedTechIds.Add(techId);
        dm.m_UnlockOrder.Add(techId);
        dm.m_UnlockTimeTicks[techId] = DateTime.UtcNow.Ticks;
        dm.Save();

        GF.Event.Fire(dm, TechUnlockedEventArgs.Create(techId));

        // 应用效果（最小集：仅处理 GrantCapability，作为统一 gate）
        ApplyEffects(data);
        return true;
    }

    private static void ApplyEffects(TechNodeData data)
    {
        if (data.Effects == null) return;
        for (int i = 0; i < data.Effects.Length; i++)
        {
            var eff = data.Effects[i];
            switch (eff.Type)
            {
                case TechEffectType.GrantCapability:
                    CapabilityDataModel.Grant(eff.S1);
                    break;
                    // 其他类型（GrantRecipe/UnlockBuildable/EnableFeature）可在此转写为 capability 或后续实现
            }
        }
    }

    private static bool EvaluateCondition(TechCondition condition, out TechResearchFailReason failReason)
    {
        failReason = TechResearchFailReason.None;

        switch (condition.Type)
        {
            case TechConditionType.BaseLevelGE:
                {
                    var baseDm = GF.DataModel.GetOrCreate<BaseProgressDataModel>();
                    if (baseDm.BaseLevel < condition.I1)
                    {
                        failReason = TechResearchFailReason.BaseLevelTooLow;
                        return false;
                    }
                    return true;
                }
            case TechConditionType.HasTech:
                {
                    if (!IsUnlocked(condition.S1))
                    {
                        failReason = TechResearchFailReason.MissingPrereqTech;
                        return false;
                    }
                    return true;
                }
            case TechConditionType.HasItem:
                {
                    if (!ItemCollectionDataModel.HasItem(condition.S1, condition.I1))
                    {
                        failReason = TechResearchFailReason.ConditionNotMet;
                        return false;
                    }
                    return true;
                }
            case TechConditionType.QuestDone:
            case TechConditionType.PlotFlag:
                {
                    // 预留入口：你做任务/剧情系统后，把这里替换为真实判定即可。
                    failReason = TechResearchFailReason.ConditionNotMet;
                    return false;
                }
            default:
                failReason = TechResearchFailReason.UnknownCondition;
                return false;
        }
    }
}
