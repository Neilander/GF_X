using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 击杀回复Buff
/// 每击杀一名生物单位，永久增加 maxHpPercent% 最大生命值，并回复 curHpPercent% 当前生命值。
/// 单参重载下 max 与 cur 共用同一百分比，保持旧行为。
/// </summary>
public class OnKillHealBuff : BuffCallback
{
    /// <summary>最大生命值提升百分比（例如 3 表示 +3%）</summary>
    private float _maxHpPercent = 3f;

    /// <summary>当前生命值回复百分比（例如 50 表示 +50% maxHp）。负数表示沿用 _maxHpPercent。</summary>
    private float _curHpPercent = -1f;

    public override void Initialize(BuffData data, MAEntity entity)
    {
        base.Initialize(data, entity);
    }

    /// <summary>
    /// 设置 max 与 cur 的同一百分比（兼容旧接口）。
    /// </summary>
    public void SetHealPercent(float percent)
    {
        _maxHpPercent = percent;
    }

    /// <summary>
    /// 单独设置 cur 回血百分比，max 不变。负数表示沿用 _maxHpPercent。
    /// </summary>
    public void SetCurHealPercent(float percent)
    {
        _curHpPercent = percent;
    }

    private float ResolveCurHealPercent() => _curHpPercent >= 0f ? _curHpPercent : _maxHpPercent;
    
    /// <summary>
    /// Buff叠加时调用
    /// </summary>
    public override void OnAddStack(int oldStack, int newStack)
    {
        base.OnAddStack(oldStack, newStack);
        
        // 获取属性管理器（通过转换为GeneralCreature获取）
        GeneralCreature creature = hostEntity as GeneralCreature;
        if (creature == null)
        {
            return;
        }
        
        CreaturePropertyManager propertyManager = creature.CreaturePropertyManager;
        if (propertyManager == null)
        {
            return;
        }
        
        // 获取最大生命值
        Fix64 maxHealth = propertyManager.GetProperty(CreatureMainProperty.Health);

        // 分别计算 max 提升量与 cur 回复量
        Fix64 maxAddAmount = maxHealth * (_maxHpPercent / 100f);
        Fix64 healAmount = maxHealth * (ResolveCurHealPercent() / 100f);

        // 永久增加最大生命值
        var maxHealthModifier = PropertyDirectAdditiveModifier.Create(maxAddAmount);
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, maxHealthModifier, true);

        // 回复当前生命值（统一走 Heal，自带 Fire 事件，UI 自动刷新）
        creature.Heal(healAmount);
    }

    /// <summary>
    /// 创建击杀回复Buff数据。max 与 cur 共用同一百分比。
    /// </summary>
    public static BuffData CreateOnKillHeal(float percent)
    {
        var buff = new OnKillHealBuff();
        buff.SetHealPercent(percent);

        return BuffData.Create(
            id: "on_kill_heal",
            duration: float.MaxValue,
            isForever: true,
            maxStack: int.MaxValue,
            modules: new List<BuffCallback> { buff }
        );
    }

    /// <summary>
    /// 创建击杀回复Buff数据。max 与 cur 分别使用各自百分比。
    /// 例如 max=3, cur=50 表示每次杀敌 max +3%、当前血 +50% maxHp。
    /// </summary>
    public static BuffData CreateOnKillHeal(float maxHpPercent, float curHpPercent)
    {
        var buff = new OnKillHealBuff();
        buff.SetHealPercent(maxHpPercent);
        buff.SetCurHealPercent(curHpPercent);

        return BuffData.Create(
            id: "on_kill_heal",
            duration: float.MaxValue,
            isForever: true,
            maxStack: int.MaxValue,
            modules: new List<BuffCallback> { buff }
        );
    }


    /// <summary>
    /// 宿主击杀目标时调用
    /// </summary>
    public override void OnKill(MAEntity target)
    {
        base.OnKill(target);
        
        GF.Log($"OnKillHealBuff[宿主ID={hostEntity?.Id}]: 开始处理击杀回复，目标ID={target?.Id}");
        
        // 获取属性管理器（通过转换为GeneralCreature获取）
        GeneralCreature creature = hostEntity as GeneralCreature;
        if (creature == null)
        {
            GF.LogError($"OnKillHealBuff[宿主ID={hostEntity?.Id}]: 宿主不是GeneralCreature类型");
            return;
        }
        
        CreaturePropertyManager propertyManager = creature.CreaturePropertyManager;
        if (propertyManager == null)
        {
            GF.LogError($"OnKillHealBuff[宿主ID={hostEntity?.Id}]: 找不到属性管理器");
            return;
        }
        
        // 获取最大生命值
        Fix64 maxHealth = propertyManager.GetProperty(CreatureMainProperty.Health);
        GF.Log($"OnKillHealBuff[宿主ID={hostEntity?.Id}]: 当前最大生命值: {maxHealth}");

        // 分别计算 max 提升量与 cur 回复量
        Fix64 maxAddAmount = maxHealth * (_maxHpPercent / 100f);
        float curPct = ResolveCurHealPercent();
        Fix64 healAmount = maxHealth * (curPct / 100f);
        GF.Log($"OnKillHealBuff[宿主ID={hostEntity?.Id}]: max +{maxAddAmount} ({_maxHpPercent}%), cur +{healAmount} ({curPct}%)");

        // 永久增加最大生命值
        var maxHealthModifier = PropertyDirectAdditiveModifier.Create(maxAddAmount);
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, maxHealthModifier, true);

        // 回复当前生命值（统一走 Heal，自带 Fire 事件，UI 自动刷新）
        creature.Heal(healAmount);

        // 获取更新后的生命值
        Fix64 newMaxHealth = propertyManager.GetProperty(CreatureMainProperty.Health);
        Fix64 currentHealth = creature.HealthValue;

        // 确保当前生命值不超过最大生命值（边界修正：通常不会触发）
        if (currentHealth > newMaxHealth)
        {
            var currentHealthClampModifier = PropertyDirectAdditiveModifier.Create(newMaxHealth - currentHealth);
            propertyManager.ModifyCurrentProperty(CreatureCurrentProperty.HealthCurrent, currentHealthClampModifier, true);
            currentHealth = newMaxHealth;
        }

        GF.Log($"OnKillHealBuff[宿主ID={hostEntity?.Id}]: 击杀回复完成，新最大生命值: {newMaxHealth}, 当前生命值: {currentHealth}");
    }
    
    /// <summary>
    /// 清理资源
    /// </summary>
    public override void Clear()
    {
        base.Clear();
    }
}
