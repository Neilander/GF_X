using GameFramework;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

/// <summary>
/// 科技进度（持久化）：只记录“已研究的科技”。
/// 说明：点亮后仅记录，不主动触发任何效果。
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

        var data = TechNodeDataModel.GetNodeData(techId);
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
        if (!UnlockCondition.IsSatisfied(new UnlockCondition(UnlockConditionType.BaseLevel, data.Level.ToString())))
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
                if (!string.IsNullOrWhiteSpace(prereq) && !UnlockCondition.IsSatisfied(new UnlockCondition(UnlockConditionType.Tech, prereq)))
                {
                    failReason = TechResearchFailReason.MissingPrereqTech;
                    return false;
                }
            }
        }

        // 条件 - AND（统一使用 UnlockCondition 判定逻辑）
        if (data.AllConditions != null)
        {
            for (int i = 0; i < data.AllConditions.Length; i++)
            {
                if (!UnlockCondition.IsSatisfied(data.AllConditions[i]))
                {
                    failReason = TechResearchFailReason.ConditionNotMet;
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
                if (UnlockCondition.IsSatisfied(data.AnyConditions[i]))
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
        var dm = GF.DataModel.GetOrCreate<TechProgressDataModel>();
        if (!CanResearch(techId, out var reason))
        {
            GF.Event.Fire(dm, TechResearchFailedEventArgs.Create(techId, reason));
            return false;
        }

        var data = TechNodeDataModel.GetNodeData(techId);

        // 扣除成本（物品）
        if (data.CostMaterial != null)
        {
            ItemCollectionDataModel.ConsumeItems(data.CostMaterial);
        }

        dm.m_UnlockedTechIds.Add(techId);
        dm.m_UnlockOrder.Add(techId);
        dm.m_UnlockTimeTicks[techId] = DateTime.UtcNow.Ticks;
        dm.Save();

        GF.Event.Fire(dm, TechUnlockedEventArgs.Create(techId));
        return true;
    }
}
