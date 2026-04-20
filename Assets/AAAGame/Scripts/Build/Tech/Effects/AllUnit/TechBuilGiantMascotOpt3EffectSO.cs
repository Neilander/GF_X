using System;
using System.Collections.Generic;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// TechID: Tech_Buil_GiantMascot_Opt3
/// ScopeType: AllUnit
/// 策划描述: 资金不多于<val1>时，我方单位攻击速度提升<val2>
/// UniqueValues: 0,30
///
/// 行为:
///   - Activate 只订阅事件，不缓存 Context
///   - 进入 Invade 时判定资金，若 ≤ val1 则注册条件 buff（固定 id）
///   - 进入非 Invade 阶段移除该 buff
/// </summary>
public class TechBuilGiantMascotOpt3EffectSO : TechEffectSO
{
    private const string TechIdConst = "Tech_Buil_GiantMascot_Opt3";
    private const string ConditionalTechId = "GiantMascot_Opt3_conditional";

    private bool m_Subscribed;
    private bool m_Active;

    public override void Activate(TechEffectContext context)
    {
        TryUnsubscribe(IngamePhaseChangedEventArgs.EventId, OnPhaseChanged);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnPhaseChanged);
        m_Subscribed = true;

        Debug.Log($"[GiantMascot_Opt3] Activate 订阅事件完成");
    }

    private static void TryUnsubscribe(int eventId, System.EventHandler<GameEventArgs> handler)
    {
        try
        {
            GF.Event.Unsubscribe(eventId, handler);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[GiantMascot_Opt3] TryUnsubscribe 吞掉异常 eventId={eventId}: {ex.Message}");
        }
    }

    private void OnPhaseChanged(object sender, GameEventArgs e)
    {
        var args = (IngamePhaseChangedEventArgs)e;

        if (args.NewPhase == GamePhase.Invade)
        {
            var techData = TechDataModel.GetTechData(TechIdConst);
            if (techData == null) return;

            Fix64 threshold = (techData.UniqueValues != null && techData.UniqueValues.Length > 0)
                ? techData.UniqueValues[0] : Fix64.Zero;

            int currentCoin = InGameDataModel.GetValue(IngameValueType.Coin);
            if ((Fix64)currentCoin <= threshold)
            {
                ApplyBuff(techData);
            }
            else
            {
                Debug.Log($"[GiantMascot_Opt3] 进入战斗阶段：资金 {currentCoin} > 阈值 {(float)threshold}，不激活");
            }
        }
        else
        {
            RemoveBuff();
        }
    }

    private void ApplyBuff(TechData techData)
    {
        if (m_Active) return;

        var manager = GameEntry.GetComponent<GlobalBuffManager>();
        if (manager == null) return;

        int factionId = EntitySideHelper.PlayerFactionId;
        foreach (UnitType unitType in Enum.GetValues(typeof(UnitType)))
        {
            manager.RegisterUnitBuff(unitType, factionId, ConditionalTechId, this, techData);
        }
        m_Active = true;
        Debug.Log($"[GiantMascot_Opt3] 条件满足，激活全体攻速 buff");
    }

    private void RemoveBuff()
    {
        if (!m_Active) return;

        var manager = GameEntry.GetComponent<GlobalBuffManager>();
        if (manager == null)
        {
            m_Active = false;
            return;
        }

        int factionId = EntitySideHelper.PlayerFactionId;
        foreach (UnitType unitType in Enum.GetValues(typeof(UnitType)))
        {
            manager.UnregisterUnitBuff(unitType, factionId, ConditionalTechId);
        }
        m_Active = false;
        Debug.Log($"[GiantMascot_Opt3] 条件失效或阶段切换，移除全体攻速 buff");
    }

    public override BuffData CreateUnitInitialBuff(TechData techData, UnitType unitType, string techId)
    {
        Fix64 v1 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 1) ? techData.UniqueValues[1] : Fix64.Zero;

        var modules = new List<BuffCallback>
        {
            new AttackSpeedBonusBuff(v1),
        };

        return BuffData.Create(
            id: $"unit_{techId}_{unitType}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
