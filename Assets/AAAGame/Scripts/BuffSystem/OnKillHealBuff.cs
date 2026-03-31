using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;
using AAAGame.Scripts.Property;

/// <summary>
/// 击杀回复Buff
/// 每击杀一名生物单位，永久增加并回复val1%基础最大生命值（加算）
/// </summary>
public class OnKillHealBuff : BuffCallback
{
    private float _healPercent;

    public OnKillHealBuff(float healPercent)
    {
        _healPercent = healPercent;
    }

    public override void Apply(BuffRuntimeInfo info, string trigger)
    {
        if (trigger == BuffConstant.OnKill)
        {
            var creature = info.Target as GeneralCreature;
            if (creature == null) return;
            
            var propertyManager = creature.CreaturePropertyManager;
            if (propertyManager == null) return;
            
            var maxHealth = propertyManager.GetProperty(CreatureMainProperty.Health);
            var healAmount = maxHealth * (_healPercent / 100f);
            
            var maxHealthModifier = PropertyDirectAdditiveModifier.Create(healAmount);
            propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, maxHealthModifier, true);
            
            var currentHealthModifier = PropertyIrreversibleAdditiveModifier.Create(healAmount);
            propertyManager.ModifyCurrentProperty(CreatureCurrentProperty.HealthCurrent, currentHealthModifier, true);
        }
    }

    public static BuffData CreateOnKillHeal(float val1)
    {
        return new BuffData(
            id: "on_kill_heal",
            maxStack: int.MaxValue,
            isForever: true,
            duration: 0f,
            tickTime: 0f,
            updateStrategy: BuffUpdateEnum.KeepAndAddStack,
            removeStrategy: BuffRemoveEnum.Clear,
            modules: new List<BuffCallback> { new OnKillHealBuff(val1) }
        );
    }
}
