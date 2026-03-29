using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 击杀回复Buff
/// 每击杀一名生物单位，永久增加并回复val1%基础最大生命值（加算）
/// </summary>
public class OnKillHealBuff : BuffCallback
{
    /// <summary>
    /// 回复百分比（例如3表示3%）
    /// </summary>
    private float _healPercent;
    
    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize(BuffData data, MAEntity entity)
    {
        base.Initialize(data, entity);
        
        // 从Buff数据中获取回复百分比
        // 这里简化处理，实际应该从配置中读取
        _healPercent = 3f; // 默认3%
    }
    
    /// <summary>
    /// 设置回复百分比
    /// </summary>
    public void SetHealPercent(float percent)
    {
        _healPercent = percent;
    }
    
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
        
        // 计算回复量：最大生命值 × 回复百分比
        Fix64 healAmount = maxHealth * (_healPercent / 100f);
        
        // 创建加法修改器增加最大生命值（永久增加）
        var maxHealthModifier = PropertyDirectAdditiveModifier.Create(healAmount);
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, maxHealthModifier, true);
        
        // 创建不可逆加法修改器回复当前生命值
        var currentHealthModifier = PropertyIrreversibleAdditiveModifier.Create(healAmount);
        propertyManager.ModifyCurrentProperty(CreatureCurrentProperty.HealthCurrent, currentHealthModifier, true);
    }
    
    /// <summary>
    /// 创建击杀回复Buff数据
    /// </summary>
    public static BuffData CreateOnKillHeal(float val1)
    {
        List<BuffCallback> modules = new List<BuffCallback>();
        OnKillHealBuff buff = new GameObject("OnKillHealBuff").AddComponent<OnKillHealBuff>();
        buff.SetHealPercent(val1);
        modules.Add(buff);
        
        return BuffData.Create(
            id: "on_kill_heal",
            duration: float.MaxValue, // 永久Buff
            isForever: true,
            maxStack: int.MaxValue, // 无限叠加
            modules: modules
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
        
        // 计算回复量：最大生命值 × 回复百分比
        Fix64 healAmount = maxHealth * (_healPercent / 100f);
        GF.Log($"OnKillHealBuff[宿主ID={hostEntity?.Id}]: 回复量: {healAmount} ({_healPercent}%)");
        
        // 创建加法修改器增加最大生命值（永久增加）
        var maxHealthModifier = PropertyDirectAdditiveModifier.Create(healAmount);
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, maxHealthModifier, true);
        
        // 创建不可逆加法修改器回复当前生命值
        var currentHealthModifier = PropertyIrreversibleAdditiveModifier.Create(healAmount);
        propertyManager.ModifyCurrentProperty(CreatureCurrentProperty.HealthCurrent, currentHealthModifier, true);
        
        // 获取更新后的生命值
        Fix64 newMaxHealth = propertyManager.GetProperty(CreatureMainProperty.Health);
        float currentHealth = hostEntity.GetComponent<GeneralCreature>().health;
        
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