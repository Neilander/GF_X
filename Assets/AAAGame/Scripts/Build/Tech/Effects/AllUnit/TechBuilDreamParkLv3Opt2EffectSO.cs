using System;
using System.Collections.Generic;
using AAAGame.Card;
using AAAGame.Scripts.BuffSystem;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// TechID: Tech_Buil_DreamPark_Lv3_Opt2
/// ScopeType: AllUnit
/// 策划描述: 每次丢弃卡牌时，使本次战斗阶段期间我方单位攻击速度+<val1>
/// UniqueValues: 10
///
/// 行为:
///   - Activate 订阅 CardDiscarded / IngamePhaseChanged 事件（只订阅一次，不缓存 Context）
///   - 回调里按需从 GameEntry/TechDataModel 取数据，无跨局残留
///   - 每次丢卡：
///       a) RegisterUnitBuff（给未来出兵的单位）
///       b) 遍历 EntityRegistry 给已在场我方单位立即 AddBuff
///   - 进入 BuildBefore 阶段清理两端：UnregisterUnitBuffByTechPrefix + 场上单位 RemoveBuffsByPrefix
/// </summary>
public class TechBuilDreamParkLv3Opt2EffectSO : TechEffectSO
{
    private const string TechIdConst = "Tech_Buil_DreamPark_Lv3_Opt2";
    private const string IdPrefix = "DreamPark_Lv3_Opt2_discard_";
    private const string FieldBuffPrefix = "field_DreamPark_Lv3_Opt2_discard_";

    private int m_DiscardCounter;
    private bool m_Subscribed;

    public override void Activate(TechEffectContext context)
    {
        Debug.Log($"[DISCARD-BUFF] Activate called. techId={context?.TechId}, faction={context?.OwnerFactionId}, alreadySubscribed={m_Subscribed}");

        // SO 是单例，m_Subscribed 可能跨 Play 残留但实际订阅已被 GF.Event 清掉。
        // 先尝试 Unsubscribe（可能抛 "not exists"，吞掉即可），再 Subscribe 建立干净订阅。
        TryUnsubscribe(CardDiscardedEventArgs.EventId, OnCardDiscarded);
        TryUnsubscribe(IngamePhaseChangedEventArgs.EventId, OnPhaseChanged);
        GF.Event.Subscribe(CardDiscardedEventArgs.EventId, OnCardDiscarded);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnPhaseChanged);
        m_Subscribed = true;
        m_DiscardCounter = 0;

        Debug.Log($"[DISCARD-BUFF] ✓ 订阅 CardDiscardedEventArgs + IngamePhaseChangedEventArgs 完成");
    }

    private static void TryUnsubscribe(int eventId, System.EventHandler<GameEventArgs> handler)
    {
        try
        {
            GF.Event.Unsubscribe(eventId, handler);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DISCARD-BUFF] TryUnsubscribe 吞掉异常 eventId={eventId}: {ex.Message}");
        }
    }

    private void OnCardDiscarded(object sender, GameEventArgs e)
    {
        Debug.Log($"[DISCARD-BUFF] OnCardDiscarded 事件收到");

        var manager = GameEntry.GetComponent<GlobalBuffManager>();
        if (manager == null)
        {
            Debug.LogWarning($"[DISCARD-BUFF] ✗ GlobalBuffManager 不存在，忽略");
            return;
        }

        var techData = TechDataModel.GetTechData(TechIdConst);
        if (techData == null)
        {
            Debug.LogWarning($"[DISCARD-BUFF] ✗ TechData 不存在 ({TechIdConst})，忽略");
            return;
        }

        Fix64 v0 = (techData.UniqueValues != null && techData.UniqueValues.Length > 0)
            ? techData.UniqueValues[0] : Fix64.Zero;
        if (v0 == Fix64.Zero)
        {
            Debug.LogWarning($"[DISCARD-BUFF] ✗ val1 为 0，忽略");
            return;
        }

        int factionId = EntitySideHelper.PlayerFactionId;
        string uniqueTechId = IdPrefix + m_DiscardCounter;
        int counter = m_DiscardCounter;
        m_DiscardCounter++;

        // 1) 未来出兵的注册
        foreach (UnitType unitType in Enum.GetValues(typeof(UnitType)))
        {
            manager.RegisterUnitBuff(unitType, factionId, uniqueTechId, this, techData);
        }
        Debug.Log($"[DISCARD-BUFF] ① RegisterUnitBuff 完成, techId={uniqueTechId}, 覆盖所有 UnitType");

        // 2) 立即给场上我方单位挂 buff
        int fieldCount = ApplyToFieldUnits(counter, v0);
        Debug.Log($"[DISCARD-BUFF] ② ApplyToFieldUnits 完成, 场上单位生效 {fieldCount} 个, 攻速+{(float)v0}%");
    }

    private int ApplyToFieldUnits(int counter, Fix64 percent)
    {
        int count = 0;
        int skipped = 0;
        SideType targetSide = EntitySideHelper.ToSide(EntitySideHelper.PlayerFactionId);

        var all = EntityRegistry.AllEntities;
        Debug.Log($"[DISCARD-BUFF] ApplyToFieldUnits 开始扫描 EntityRegistry.AllEntities, 总数={all.Count}, 目标 side={targetSide}");

        for (int i = 0; i < all.Count; i++)
        {
            var ctx = all[i];
            if (ctx == null) { skipped++; continue; }
            if (!ctx.Alive) { skipped++; continue; }
            if (ctx is not MAEntity ma) { skipped++; continue; }
            if (ma.Side != targetSide) { skipped++; continue; }

            var comp = ma.BuffComp as CharacterBuffComp;
            if (comp == null)
            {
                Debug.LogWarning($"[DISCARD-BUFF] 单位 {ma.CharacterKey} (id={ma.Id}) BuffComp 不是 CharacterBuffComp，跳过");
                skipped++;
                continue;
            }

            string buffId = FieldBuffPrefix + counter + "_" + ma.Id;
            var modules = new List<BuffCallback> { new AttackSpeedBonusBuff(percent) };
            var data = BuffData.Create(buffId, float.MaxValue, true, 1, modules);
            comp.AddBuff(data, ma);
            count++;
            Debug.Log($"[DISCARD-BUFF]   挂 buff 到 {ma.CharacterKey} (id={ma.Id}) side={ma.Side}, buffId={buffId}");
        }
        Debug.Log($"[DISCARD-BUFF] ApplyToFieldUnits 结束: 挂了 {count} 个, 跳过 {skipped} 个");
        return count;
    }

    private void OnPhaseChanged(object sender, GameEventArgs e)
    {
        var args = (IngamePhaseChangedEventArgs)e;
        if (!InGameDataModel.IsBuildPhase(args.NewPhase)) return;

        int factionId = EntitySideHelper.PlayerFactionId;

        // 1) 清 per-unit 桶（未来出兵的注册）
        var manager = GameEntry.GetComponent<GlobalBuffManager>();
        int unregistered = 0;
        if (manager != null)
        {
            unregistered = manager.UnregisterUnitBuffByTechPrefix(factionId, IdPrefix);
        }

        // 2) 清场上单位身上已挂的 buff
        int fieldCleared = 0;
        SideType targetSide = EntitySideHelper.ToSide(factionId);
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            var ctx = all[i];
            if (ctx is not MAEntity ma) continue;
            if (ma.Side != targetSide) continue;

            var comp = ma.BuffComp as CharacterBuffComp;
            if (comp == null) continue;

            fieldCleared += comp.RemoveBuffsByPrefix(FieldBuffPrefix);
        }

        m_DiscardCounter = 0;
        Debug.Log($"[DreamPark_Lv3_Opt2] 进入 BuildBefore 阶段，清除未来注册 {unregistered} 条，场上单位 buff 清除 {fieldCleared} 条");
    }

    public override BuffData CreateUnitInitialBuff(TechData techData, UnitType unitType, string techId)
    {
        Fix64 v0 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 0) ? techData.UniqueValues[0] : Fix64.Zero;

        var modules = new List<BuffCallback>
        {
            new AttackSpeedBonusBuff(v0),
        };

        return BuffData.Create(
            id: $"unit_{techId}_{unitType}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
