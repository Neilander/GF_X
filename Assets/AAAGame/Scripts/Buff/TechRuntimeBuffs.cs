using System;
using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;
using UnityEngine;

public sealed class SourceBuildingTechUnitBuffProvider : BuffCallback, ISourceBuildingUnitBuffProvider
{
    private readonly int m_OwnerFactionId;
    private readonly string m_TechId;
    private readonly ITechEffectRuntime m_Effect;
    private readonly TechData m_TechData;
    private readonly Func<IBuildingLogicContext, bool> m_Matches;
    private readonly Func<IBuildingLogicContext, string> m_ResolveTechId;

    public SourceBuildingTechUnitBuffProvider(
        int ownerFactionId,
        string techId,
        ITechEffectRuntime effect,
        TechData techData,
        Func<IBuildingLogicContext, bool> matches,
        Func<IBuildingLogicContext, string> resolveTechId)
    {
        m_OwnerFactionId = ownerFactionId;
        m_TechId = techId;
        m_Effect = effect;
        m_TechData = techData;
        m_Matches = matches;
        m_ResolveTechId = resolveTechId;
    }

    public bool CanProvideUnitBuffs()
    {
        IBuildingLogicContext building = GetHostBuilding();
        return building != null
               && building.OwnerFactionId == m_OwnerFactionId
               && !string.IsNullOrWhiteSpace(m_TechId)
               && m_Effect != null
               && m_TechData != null
               && m_Matches != null
               && m_Matches.Invoke(building);
    }

    public void CreateUnitBuffModules(List<BuffCallback> modules)
    {
        if (modules == null || !CanProvideUnitBuffs())
            return;

        IBuildingLogicContext building = GetHostBuilding();
        string resolvedTechId = m_ResolveTechId != null ? m_ResolveTechId.Invoke(building) : m_TechId;
        if (string.IsNullOrWhiteSpace(resolvedTechId))
            return;

        List<BuffCallback> createdModules = m_Effect.CreateBuildingScopedModules(m_TechData, resolvedTechId);
        if (createdModules == null || createdModules.Count == 0)
            return;

        modules.AddRange(createdModules);
    }

    private IBuildingLogicContext GetHostBuilding()
    {
        return hostEntity as IBuildingLogicContext;
    }
}

public sealed class MainPropertyAdditiveBuff : BuffCallback
{
    private readonly CreatureMainProperty m_Property;
    private readonly Fix64 m_Bonus;
    private IPropertyModifier m_Modifier;

    public MainPropertyAdditiveBuff(CreatureMainProperty property, Fix64 bonus)
    {
        m_Property = property;
        m_Bonus = bonus;
    }

    public override void OnAdd()
    {
        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null || m_Bonus == Fix64.Zero)
            return;

        m_Modifier = PropertyDirectAdditiveModifier.Create(m_Bonus);
        propertyManager.ModifyMainPropertyValueBuff(m_Property, m_Modifier, true);
    }

    public override void OnRemove()
    {
        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null || m_Modifier == null)
            return;

        propertyManager.ModifyMainPropertyValueBuff(m_Property, m_Modifier, false);
        m_Modifier = null;
    }
}

public sealed class MainPropertyPercentBuff : BuffCallback
{
    private readonly CreatureMainProperty m_Property;
    private readonly Fix64 m_Percent;
    private IPropertyModifier m_Modifier;

    public MainPropertyPercentBuff(CreatureMainProperty property, Fix64 percent)
    {
        m_Property = property;
        m_Percent = percent;
    }

    public override void OnAdd()
    {
        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null || m_Percent == Fix64.Zero)
            return;

        m_Modifier = PropertyDirectAdditiveModifier.Create(m_Percent / (Fix64)100);
        propertyManager.ModifyMainPropertyMul(m_Property, NormalBaseValueTp.Buff, m_Modifier, true);
    }

    public override void OnRemove()
    {
        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null || m_Modifier == null)
            return;

        propertyManager.ModifyMainPropertyMul(m_Property, NormalBaseValueTp.Buff, m_Modifier, false);
        m_Modifier = null;
    }
}

public sealed class FirstIncomingDamageReductionBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_ReductionPercent;
    private bool m_Consumed;

    public FirstIncomingDamageReductionBuff(Fix64 reductionPercent)
    {
        m_ReductionPercent = reductionPercent;
    }

    public override Fix64 ModifyIncomingDamage(IEntityContext attacker, Fix64 baseDamage, HealthModifyType modType)
    {
        if (m_Consumed || modType != HealthModifyType.reduce || baseDamage <= Fix64.Zero || m_ReductionPercent <= Fix64.Zero)
            return baseDamage;

        m_Consumed = true;
        Fix64 multiplier = Fix64.One - m_ReductionPercent / (Fix64)100;
        if (multiplier < Fix64.Zero)
            multiplier = Fix64.Zero;

        return baseDamage * multiplier;
    }

    public void WriteDeterministicState(LogicStateHasher hasher) => hasher.Add(m_Consumed);
}

public sealed class OutgoingAttackDebuffBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_AttackDelta;
    private readonly Fix64 m_Duration;
    private ulong m_ApplicationSequence;

    public OutgoingAttackDebuffBuff(Fix64 attackDelta, Fix64 duration)
    {
        m_AttackDelta = attackDelta;
        m_Duration = duration;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (m_AttackDelta == Fix64.Zero || m_Duration <= Fix64.Zero || target is not IEntityContext targetEntity)
            return baseDamage;

        var comp = targetEntity.BuffComp as CharacterBuffComp;
        if (comp == null)
            return baseDamage;

        m_ApplicationSequence = checked(m_ApplicationSequence + 1);
        string buffId = $"tech_attack_debuff_{buffData?.id}_{hostEntity.LogicEntityId.Value}_{targetEntity.LogicEntityId.Value}_{m_ApplicationSequence}";
        comp.AddBuff(BuffData.Create(
            id: buffId,
            duration: m_Duration,
            isForever: false,
            maxStack: 1,
            modules: new List<BuffCallback> { new FlatAttackBonusBuff(m_AttackDelta), new NegativeStatusMarkerBuff() }), targetEntity);

        return baseDamage;
    }

    public void WriteDeterministicState(LogicStateHasher hasher) => hasher.Add(m_ApplicationSequence);
}

public sealed class ConsecutiveSameTargetBonusDamageBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly int m_RequiredHits;
    private readonly Fix64 m_BonusDamage;
    private int m_LastTargetId;
    private int m_ConsecutiveHits;

    public ConsecutiveSameTargetBonusDamageBuff(int requiredHits, Fix64 bonusDamage)
    {
        m_RequiredHits = Math.Max(1, requiredHits);
        m_BonusDamage = bonusDamage;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (m_BonusDamage == Fix64.Zero || target is not IEntityContext entity)
            return baseDamage;

        int targetId = entity.LogicEntityId.Value;
        if (targetId == m_LastTargetId)
            m_ConsecutiveHits++;
        else
        {
            m_LastTargetId = targetId;
            m_ConsecutiveHits = 1;
        }

        if (m_ConsecutiveHits < m_RequiredHits)
            return baseDamage;

        m_ConsecutiveHits = 0;
        return baseDamage + m_BonusDamage;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_LastTargetId);
        hasher.Add(m_ConsecutiveHits);
    }
}

public sealed class MissingHealthAttackSpeedBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private static readonly Fix64 UpdateInterval = Fix64.FromRaw(410);
    private readonly Fix64 m_HealthPerStep;
    private readonly Fix64 m_AttackSpeedPercentPerStep;
    private Fix64 m_Timer;
    private Fix64 m_CurrentFactor = Fix64.One;
    private bool m_Applied;

    public MissingHealthAttackSpeedBuff(Fix64 healthPerStep, Fix64 attackSpeedPercentPerStep)
    {
        m_HealthPerStep = healthPerStep;
        m_AttackSpeedPercentPerStep = attackSpeedPercentPerStep;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += (Fix64)deltaTime;
        if (m_Timer < UpdateInterval)
            return;

        m_Timer = Fix64.Zero;
        Refresh();
    }

    public override void OnRemove()
    {
        ApplyFactor(Fix64.One);
    }

    private void Refresh()
    {
        var creature = hostEntity;
        var propertyManager = creature?.CreatureProperties;
        if (propertyManager == null || m_HealthPerStep <= Fix64.Zero || m_AttackSpeedPercentPerStep == Fix64.Zero)
            return;

        Fix64 maxHealth = propertyManager.GetProperty(CreatureMainProperty.Health);
        Fix64 missing = maxHealth - creature.HealthValue;
        if (missing < Fix64.Zero)
            missing = Fix64.Zero;

        int steps = (int)Fix64.Floor(missing / m_HealthPerStep);
        Fix64 percent = m_AttackSpeedPercentPerStep * (Fix64)steps;
        Fix64 factor = Fix64.One / (Fix64.One + percent / (Fix64)100);
        ApplyFactor(factor);
    }

    private void ApplyFactor(Fix64 factor)
    {
        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null || factor <= Fix64.Zero)
            return;

        if (m_Applied && m_CurrentFactor != Fix64.Zero)
            weapon.ApplyMultiplier(WeaponStatId.Interval, Fix64.One / m_CurrentFactor);

        m_CurrentFactor = factor;
        if (factor != Fix64.One)
        {
            weapon.ApplyMultiplier(WeaponStatId.Interval, factor);
            m_Applied = true;
        }
        else
        {
            m_Applied = false;
        }
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
        hasher.Add(m_CurrentFactor.RawValue);
        hasher.Add(m_Applied);
    }
}

public sealed class StationaryAttackPercentBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private static readonly Fix64 UpdateInterval = Fix64.FromRaw(410);
    private static readonly Fix64 MoveEpsilonSqr = Fix64.FromRaw(1);
    private readonly Fix64 m_RequiredSeconds;
    private readonly Fix64 m_AttackPercent;
    private FixVector2 m_LastPosition;
    private Fix64 m_StationarySeconds;
    private Fix64 m_Timer;
    private bool m_AttackApplied;
    private Fix64 m_AppliedPercentAdd;

    public StationaryAttackPercentBuff(Fix64 requiredSeconds, Fix64 attackPercent)
    {
        m_RequiredSeconds = Fix64.Max(Fix64.Zero, requiredSeconds);
        m_AttackPercent = attackPercent;
    }

    public override void OnAdd()
    {
        if (hostEntity != null)
            m_LastPosition = hostEntity.LogicFramePositionFixed();
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        if (hostEntity == null)
            return;

        m_Timer += (Fix64)deltaTime;
        if (m_Timer < UpdateInterval)
            return;

        Fix64 elapsed = m_Timer;
        m_Timer = Fix64.Zero;
        FixVector2 current = LogicEntityFrameSnapshotService.GetRequiredPosition(hostEntity);
        FixVector2 previous = m_LastPosition;

        if (FixVector2.SqrMagnitude(current - previous) <= MoveEpsilonSqr)
            m_StationarySeconds += elapsed;
        else
            m_StationarySeconds = Fix64.Zero;

        m_LastPosition = current;
        if (m_StationarySeconds >= m_RequiredSeconds)
            ApplyAttack();
        else
            RemoveAttack();
    }

    public override void OnRemove()
    {
        RemoveAttack();
    }

    private void ApplyAttack()
    {
        if (m_AttackApplied || m_AttackPercent == Fix64.Zero)
            return;

        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null)
            return;

        m_AppliedPercentAdd = m_AttackPercent / (Fix64)100;
        weapon.ApplyPercentAdd(WeaponStatId.Atk, m_AppliedPercentAdd);
        m_AttackApplied = true;
    }

    private void RemoveAttack()
    {
        if (!m_AttackApplied)
            return;

        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon != null)
            weapon.ApplyPercentAdd(WeaponStatId.Atk, -m_AppliedPercentAdd);

        m_AttackApplied = false;
        m_AppliedPercentAdd = Fix64.Zero;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_LastPosition.x.RawValue);
        hasher.Add(m_LastPosition.y.RawValue);
        hasher.Add(m_StationarySeconds.RawValue);
        hasher.Add(m_Timer.RawValue);
        hasher.Add(m_AttackApplied);
        hasher.Add(m_AppliedPercentAdd.RawValue);
    }
}

public sealed class IdleNextAttackCriticalBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_RequiredSeconds;
    private Fix64 m_LastAttackTime;

    public IdleNextAttackCriticalBuff(Fix64 requiredSeconds)
    {
        m_RequiredSeconds = Fix64.Max(Fix64.Zero, requiredSeconds);
    }

    public override void OnAdd()
    {
        m_LastAttackTime = LogicFrameRuntime.ElapsedTime;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (LogicFrameRuntime.ElapsedTime - m_LastAttackTime < m_RequiredSeconds)
            return baseDamage;

        m_LastAttackTime = LogicFrameRuntime.ElapsedTime;
        return CriticalDamageUtility.ApplyCriticalDamage(hostEntity, baseDamage);
    }

    public override void OnAttackCompleted(IEntityContext target)
    {
        if (LogicFrameRuntime.ElapsedTime - m_LastAttackTime < m_RequiredSeconds)
            m_LastAttackTime = LogicFrameRuntime.ElapsedTime;
    }

    public void WriteDeterministicState(LogicStateHasher hasher) => hasher.Add(m_LastAttackTime.RawValue);
}

public sealed class LoneUnitBonusBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private static readonly Fix64 UpdateInterval = Fix64.FromRaw(410);
    private readonly Fix64 m_Radius;
    private readonly Fix64 m_AttackSpeedPercent;
    private readonly Fix64 m_DefBonus;
    private Fix64 m_Timer;
    private bool m_Applied;
    private IPropertyModifier m_DefModifier;
    private Fix64 m_AttackSpeedFactor = Fix64.One;

    public LoneUnitBonusBuff(Fix64 radius, Fix64 attackSpeedPercent, Fix64 defBonus)
    {
        m_Radius = radius;
        m_AttackSpeedPercent = attackSpeedPercent;
        m_DefBonus = defBonus;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += (Fix64)deltaTime;
        if (m_Timer < UpdateInterval)
            return;

        m_Timer = Fix64.Zero;
        if (HasNearbyFriendly())
            RemoveBonus();
        else
            ApplyBonus();
    }

    public override void OnRemove()
    {
        RemoveBonus();
    }

    private bool HasNearbyFriendly()
    {
        if (hostEntity == null)
            return false;

        Fix64 radius = DistanceUnitConverter.ConvertToWorld(m_Radius);
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext other = all[i];
            if (other == null || ReferenceEquals(other, hostEntity) || !other.Alive)
                continue;
            if (other.Side != hostEntity.Side)
                continue;
            if (LogicEntityFrameSnapshotService.GetRequiredTargetSurfaceDistance(hostEntity, other) <= radius)
                return true;
        }

        return false;
    }

    private void ApplyBonus()
    {
        if (m_Applied)
            return;

        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon != null && m_AttackSpeedPercent != Fix64.Zero)
        {
            m_AttackSpeedFactor = Fix64.One / (Fix64.One + m_AttackSpeedPercent / (Fix64)100);
            weapon.ApplyMultiplier(WeaponStatId.Interval, m_AttackSpeedFactor);
        }

        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager != null && m_DefBonus != Fix64.Zero)
        {
            m_DefModifier = PropertyDirectAdditiveModifier.Create(m_DefBonus);
            propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Def, m_DefModifier, true);
        }

        m_Applied = true;
    }

    private void RemoveBonus()
    {
        if (!m_Applied)
            return;

        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon != null && m_AttackSpeedFactor != Fix64.Zero && m_AttackSpeedFactor != Fix64.One)
            weapon.ApplyMultiplier(WeaponStatId.Interval, Fix64.One / m_AttackSpeedFactor);

        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager != null && m_DefModifier != null)
            propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Def, m_DefModifier, false);

        m_DefModifier = null;
        m_AttackSpeedFactor = Fix64.One;
        m_Applied = false;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
        hasher.Add(m_Applied);
        hasher.Add(m_AttackSpeedFactor.RawValue);
    }
}

public sealed class NearbyFriendlyCountDefBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private static readonly Fix64 UpdateInterval = Fix64.FromRaw(410);
    private readonly Fix64 m_Radius;
    private readonly int m_FriendsPerStep;
    private readonly Fix64 m_DefPerStep;
    private Fix64 m_Timer;
    private Fix64 m_CurrentBonus;
    private IPropertyModifier m_Modifier;
    private ValueProperty m_Property;

    public NearbyFriendlyCountDefBuff(Fix64 radius, int friendsPerStep, Fix64 defPerStep)
    {
        m_Radius = radius;
        m_FriendsPerStep = Math.Max(1, friendsPerStep);
        m_DefPerStep = defPerStep;
    }

    public override void OnAdd()
    {
        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null)
            return;

        string propertyId = PropertyHelper.ModName(CreatureMainProperty.Def.ToString(), nameof(NormalComputeTp.Value), nameof(NormalBaseValueTp.Buff));
        m_Property = propertyManager.propertyManager.GetValueProperty(propertyId);
        m_Modifier = PropertyDirectAdditiveModifier.Create(() => m_CurrentBonus);
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Def, m_Modifier, true);
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += (Fix64)deltaTime;
        if (m_Timer < UpdateInterval)
            return;

        m_Timer = Fix64.Zero;
        int count = CountNearbyFriends();
        SetBonus((Fix64)(count / m_FriendsPerStep) * m_DefPerStep);
    }

    public override void OnRemove()
    {
        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager != null && m_Modifier != null)
            propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Def, m_Modifier, false);

        m_Modifier = null;
        m_Property = null;
    }

    private int CountNearbyFriends()
    {
        if (hostEntity == null)
            return 0;

        int count = 0;
        Fix64 radius = DistanceUnitConverter.ConvertToWorld(m_Radius);
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext other = all[i];
            if (other == null || ReferenceEquals(other, hostEntity) || !other.Alive)
                continue;
            if (other.Side != hostEntity.Side)
                continue;
            if (LogicEntityFrameSnapshotService.GetRequiredTargetSurfaceDistance(hostEntity, other) <= radius)
                count++;
        }

        return count;
    }

    private void SetBonus(Fix64 value)
    {
        if (m_CurrentBonus == value)
            return;

        m_CurrentBonus = value;
        m_Property?.MakeDirty();
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
        hasher.Add(m_CurrentBonus.RawValue);
    }
}

public sealed class OnKillFlatGrowthBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly int m_KillsPerStep;
    private readonly Fix64 m_AttackPerStep;
    private readonly Fix64 m_HealthPerStep;
    private int m_KillCount;

    public OnKillFlatGrowthBuff(int killsPerStep, Fix64 attackPerStep, Fix64 healthPerStep)
    {
        m_KillsPerStep = Math.Max(1, killsPerStep);
        m_AttackPerStep = attackPerStep;
        m_HealthPerStep = healthPerStep;
    }

    public override void OnKill(IEntityContext target)
    {
        m_KillCount++;
        if (m_KillCount < m_KillsPerStep)
            return;

        m_KillCount = 0;
        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon != null && m_AttackPerStep != Fix64.Zero)
            weapon.ApplyAdditive(WeaponStatId.Atk, m_AttackPerStep);

        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager != null && m_HealthPerStep != Fix64.Zero)
            propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, PropertyDirectAdditiveModifier.Create(m_HealthPerStep), true);
    }

    public void WriteDeterministicState(LogicStateHasher hasher) => hasher.Add(m_KillCount);
}

public sealed class OnDeathHealNearbyAlliesBuff : BuffCallback
{
    private readonly Fix64 m_Radius;
    private readonly Fix64 m_HealPercent;

    public OnDeathHealNearbyAlliesBuff(Fix64 radius, Fix64 healPercent)
    {
        m_Radius = radius;
        m_HealPercent = healPercent;
    }

    public override void OnHostDead()
    {
        if (hostEntity == null || m_HealPercent <= Fix64.Zero)
            return;

        Fix64 radius = DistanceUnitConverter.ConvertToWorld(m_Radius);
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext ally = all[i];
            if (ally == null || ReferenceEquals(ally, hostEntity) || !ally.Alive)
                continue;
            if (ally.Side != hostEntity.Side)
                continue;
            if (LogicEntityFrameSnapshotService.GetRequiredTargetSurfaceDistance(hostEntity, ally) > radius)
                continue;

            Fix64 max = ally.CreatureProperties.GetProperty(CreatureMainProperty.Health);
            ally.Heal(max * m_HealPercent / (Fix64)100);
        }
    }
}

public sealed class OnDeathEnemyAttackDebuffBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_Radius;
    private readonly Fix64 m_AttackDelta;
    private readonly Fix64 m_Duration;
    private ulong m_ApplicationSequence;

    public OnDeathEnemyAttackDebuffBuff(Fix64 radius, Fix64 attackDelta, Fix64 duration)
    {
        m_Radius = radius;
        m_AttackDelta = attackDelta;
        m_Duration = duration;
    }

    public override void OnHostDead()
    {
        if (hostEntity == null || m_AttackDelta == Fix64.Zero || m_Duration <= Fix64.Zero)
            return;

        Fix64 radius = DistanceUnitConverter.ConvertToWorld(m_Radius);
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext enemy = all[i];
            if (enemy == null || !enemy.Alive)
                continue;
            if (!EntityCombatTeamHelper.IsEnemy(hostEntity, enemy))
                continue;
            if (LogicEntityFrameSnapshotService.GetRequiredTargetSurfaceDistance(hostEntity, enemy) > radius)
                continue;

            var comp = enemy.BuffComp as CharacterBuffComp;
            m_ApplicationSequence = checked(m_ApplicationSequence + 1);
            comp?.AddBuff(BuffData.Create(
                id: $"tech_death_attack_debuff_{buffData?.id}_{hostEntity.LogicEntityId.Value}_{enemy.LogicEntityId.Value}_{m_ApplicationSequence}",
                duration: m_Duration,
                isForever: false,
                maxStack: 1,
                modules: new List<BuffCallback> { new FlatAttackBonusBuff(m_AttackDelta), new NegativeStatusMarkerBuff() }), enemy);
        }
    }

    public void WriteDeterministicState(LogicStateHasher hasher) => hasher.Add(m_ApplicationSequence);
}

public sealed class FatalDamageProtectionBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_InvincibleSeconds;
    private bool m_Consumed;

    public FatalDamageProtectionBuff(Fix64 invincibleSeconds)
    {
        m_InvincibleSeconds = Fix64.Max(Fix64.Zero, invincibleSeconds);
    }

    public override Fix64 ModifyIncomingDamage(IEntityContext attacker, Fix64 baseDamage, HealthModifyType modType)
    {
        if (m_Consumed || modType != HealthModifyType.reduce || baseDamage <= Fix64.Zero || hostEntity == null)
            return baseDamage;

        if (hostEntity.HealthValue - baseDamage > Fix64.Zero)
            return baseDamage;

        m_Consumed = true;
        AddTemporaryInvincible(hostEntity);
        Fix64 capped = hostEntity.HealthValue - Fix64.One;
        return capped > Fix64.Zero ? capped : Fix64.Zero;
    }

    private void AddTemporaryInvincible(IEntityContext entity)
    {
        if (m_InvincibleSeconds <= Fix64.Zero)
            return;

        var comp = entity.BuffComp as CharacterBuffComp;
        comp?.AddBuff(BuffData.Create(
            id: $"tech_fatal_invincible_{buffData?.id}_{entity.LogicEntityId.Value}",
            duration: m_InvincibleSeconds,
            isForever: false,
            maxStack: 1,
            modules: new List<BuffCallback> { new TemporaryInvincibleSourceBuff($"tech_fatal_invincible_{buffData?.id}_{entity.LogicEntityId.Value}") }), entity);
    }

    public void WriteDeterministicState(LogicStateHasher hasher) => hasher.Add(m_Consumed);
}

public sealed class TemporaryInvincibleSourceBuff : BuffCallback
{
    private readonly string m_SourceId;

    public TemporaryInvincibleSourceBuff(string sourceId)
    {
        m_SourceId = sourceId;
    }

    public override void OnAdd()
    {
        hostEntity?.RegisterInvincibleSource(m_SourceId);
    }

    public override void OnRemove()
    {
        hostEntity?.UnregisterInvincibleSource(m_SourceId);
    }
}

public sealed class TimedBuffOnSpawnModule : BuffCallback
{
    private readonly Fix64 m_Duration;
    private readonly Func<List<BuffCallback>> m_ModuleFactory;

    public TimedBuffOnSpawnModule(Fix64 duration, Func<List<BuffCallback>> moduleFactory)
    {
        m_Duration = duration;
        m_ModuleFactory = moduleFactory;
    }

    public override void OnAdd()
    {
        if (hostEntity == null || m_ModuleFactory == null || m_Duration <= Fix64.Zero)
            return;

        var comp = hostEntity.BuffComp as CharacterBuffComp;
        comp?.AddBuff(BuffData.Create(
            id: $"tech_timed_spawn_{buffData?.id}_{hostEntity.LogicEntityId.Value}",
            duration: m_Duration,
            isForever: false,
            maxStack: 1,
            modules: m_ModuleFactory(),
            startDurationOnFirstCombat: true), hostEntity);
    }
}
