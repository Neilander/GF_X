using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 击杀回复Buff
/// 每击杀一名生物单位，永久增加并回复val1%基础最大生命值（加算）
/// </summary>
public class OnKillHealBuff : BuffCallback
{
    private float _healPercent;

    public override void Initialize(BuffData data, MAEntity entity)
    {
        base.Initialize(data, entity);
        _healPercent = 3f;
    }

    public void SetHealPercent(float percent)
    {
        _healPercent = percent;
    }

    public override void OnAddStack(int oldStack, int newStack)
    {
        base.OnAddStack(oldStack, newStack);

        GeneralCreature creature = hostEntity as GeneralCreature;
        if (creature == null) return;

        CreaturePropertyManager propertyManager = creature.CreaturePropertyManager;
        if (propertyManager == null) return;

        Fix64 maxHealth = propertyManager.GetProperty(CreatureMainProperty.Health);
        Fix64 healAmount = maxHealth * (_healPercent / 100f);

        var maxHealthModifier = PropertyDirectAdditiveModifier.Create(healAmount);
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, maxHealthModifier, true);

        var currentHealthModifier = PropertyIrreversibleAdditiveModifier.Create(healAmount);
        propertyManager.ModifyCurrentProperty(CreatureCurrentProperty.HealthCurrent, currentHealthModifier, true);
    }

    public static BuffData CreateOnKillHeal(float val1)
    {
        var buff = new OnKillHealBuff();
        buff.SetHealPercent(val1);

        return BuffData.Create(
            id: "on_kill_heal",
            duration: float.MaxValue,
            isForever: true,
            maxStack: int.MaxValue,
            modules: new List<BuffCallback> { buff }
        );
    }

    public override void OnKill(MAEntity target)
    {
        base.OnKill(target);

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

        Fix64 maxHealth = propertyManager.GetProperty(CreatureMainProperty.Health);
        Fix64 healAmount = maxHealth * (_healPercent / 100f);

        var maxHealthModifier = PropertyDirectAdditiveModifier.Create(healAmount);
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, maxHealthModifier, true);

        var currentHealthModifier = PropertyIrreversibleAdditiveModifier.Create(healAmount);
        propertyManager.ModifyCurrentProperty(CreatureCurrentProperty.HealthCurrent, currentHealthModifier, true);

        Fix64 newMaxHealth = propertyManager.GetProperty(CreatureMainProperty.Health);
        float currentHealth = hostEntity.GetComponent<GeneralCreature>().health;
        GF.Log($"OnKillHealBuff[宿主ID={hostEntity?.Id}]: 击杀回复完成，新最大生命值: {newMaxHealth}, 当前生命值: {currentHealth}");
    }
}
