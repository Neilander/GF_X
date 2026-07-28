using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 击杀回复Buff
/// 每击杀一名生物单位，回复最大生命值一定百分比的当前生命值。
/// </summary>
public class OnKillHealBuff : BuffCallback, ILogicDeterministicStateContributor
{
    /// <summary>当前生命值回复百分比（例如 50 表示回复 50% 最大生命值）</summary>
    private Fix64 _curHpPercent = (Fix64)3;

    public override void Initialize(BuffData data, IEntityContext entity)
    {
        base.Initialize(data, entity);
    }

    /// <summary>
    /// 设置 max 与 cur 的同一百分比（兼容旧接口）。
    /// </summary>
    public void SetHealPercent(float percent)
    {
        SetHealPercent((Fix64)percent);
    }

    public void SetHealPercent(Fix64 percent)
    {
        _curHpPercent = percent;
    }

    /// <summary>
    /// 单独设置 cur 回血百分比。
    /// </summary>
    public void SetCurHealPercent(float percent)
    {
        SetCurHealPercent((Fix64)percent);
    }

    public void SetCurHealPercent(Fix64 percent)
    {
        _curHpPercent = percent;
    }

    private Fix64 ResolveCurHealPercent() => _curHpPercent;
    
    /// <summary>
    /// 创建击杀回复Buff数据。
    /// </summary>
    public static BuffData CreateOnKillHeal(float percent)
    {
        return CreateOnKillHeal((Fix64)percent);
    }

    public static BuffData CreateOnKillHeal(Fix64 percent)
    {
        var buff = new OnKillHealBuff();
        buff.SetHealPercent(percent);

        return BuffData.Create(
            id: "on_kill_heal",
            duration: Fix64.Zero,
            isForever: true,
            maxStack: int.MaxValue,
            modules: new List<BuffCallback> { buff }
        );
    }

    /// <summary>
    /// 创建击杀回复Buff数据。兼容旧双参接口，仅使用 curHpPercent 作为回血比例。
    /// </summary>
    public static BuffData CreateOnKillHeal(float maxHpPercent, float curHpPercent)
    {
        return CreateOnKillHeal((Fix64)maxHpPercent, (Fix64)curHpPercent);
    }

    public static BuffData CreateOnKillHeal(Fix64 maxHpPercent, Fix64 curHpPercent)
    {
        var buff = new OnKillHealBuff();
        buff.SetHealPercent(maxHpPercent);
        buff.SetCurHealPercent(curHpPercent);

        return BuffData.Create(
            id: "on_kill_heal",
            duration: Fix64.Zero,
            isForever: true,
            maxStack: int.MaxValue,
            modules: new List<BuffCallback> { buff }
        );
    }


    /// <summary>
    /// 宿主击杀目标时调用
    /// </summary>
    public override void OnKill(IEntityContext target)
    {
        base.OnKill(target);
        
        GF.Log($"OnKillHealBuff[宿主ID={hostEntity?.LogicEntityId.Value}]: 开始处理击杀回复，目标ID={target?.LogicEntityId.Value}");
        
        // 获取属性管理器（通过转换为GeneralCreature获取）
        if (hostEntity == null)
        {
            GF.LogError("OnKillHealBuff: 宿主为空");
            return;
        }

        CreaturePropertyManager propertyManager = hostEntity.CreatureProperties;
        if (propertyManager == null)
        {
            GF.LogError($"OnKillHealBuff[宿主ID={hostEntity.LogicEntityId.Value}]: 找不到属性管理器");
            return;
        }
        
        // 获取最大生命值
        Fix64 maxHealth = propertyManager.GetProperty(CreatureMainProperty.Health);
        GF.Log($"OnKillHealBuff[宿主ID={hostEntity.LogicEntityId.Value}]: 当前最大生命值: {maxHealth}");

        Fix64 curPct = ResolveCurHealPercent();
        Fix64 healAmount = maxHealth * curPct / (Fix64)100;
        GF.Log($"OnKillHealBuff[宿主ID={hostEntity.LogicEntityId.Value}]: 回复生命值 +{healAmount} ({curPct}% 最大生命值)");

        // 回复当前生命值（统一走 Heal，自带 Fire 事件，UI 自动刷新）
        hostEntity.Heal(healAmount);

        Fix64 currentHealth = hostEntity.HealthValue;
        Fix64 afterMaxHealth = propertyManager.GetProperty(CreatureMainProperty.Health);
        GF.Log($"OnKillHealBuff[宿主ID={hostEntity.LogicEntityId.Value}]: 击杀回复完成，最大生命值: {afterMaxHealth}, 当前生命值: {currentHealth}");
    }
    
    /// <summary>
    /// 清理资源
    /// </summary>
    public override void Clear()
    {
        base.Clear();
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(_curHpPercent.RawValue);
    }
}
