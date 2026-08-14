using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;
using UnityEngine;
using AAAGame.Scripts.BuffSystem;

public static class BuildingAbilityIds
{
    public const string SouthernMoon = "Buil_SouthernMoon";
    public const string MeatRack = "Buil_MeatRack";
    public const string BallLauncher = "Buil_BallLauncher";
    public const string Trap = "Buil_Trap";
    public const string SprinklerHead = "Buil_SprinklerHead";
    public const string RoseBush = "Buil_RoseBush";
    public const string Bollard = "Buil_Bollard";
    public const string Pharmacy = "Buil_Pharmacy";
    public const string ThemeStatue = "Buil_ThemeStatue";
    public const string ComplaintsDepartment = "Buil_ComplaintsDepartment";
    public const string SupplyStation = "Buil_SupplyStation";

    public static bool IsBuilding(BuildingData buildingData, string baseIdentifier)
    {
        return buildingData?.Identifier != null
               && buildingData.Identifier.StartsWith(baseIdentifier, StringComparison.Ordinal);
    }

    public static bool HasPermanentNoCollisionCapability(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Building identifier is empty.", nameof(identifier));

        return identifier.StartsWith(Trap, StringComparison.Ordinal)
               || identifier.StartsWith(RoseBush, StringComparison.Ordinal);
    }
}

public sealed class BuildingInvincibleSourceBuff : BuffCallback
{
    private readonly string _sourceId;

    public BuildingInvincibleSourceBuff(string sourceId)
    {
        _sourceId = sourceId;
    }

    public override void OnAdd()
    {
        if (hostEntity is not IBuildingLogicContext building)
            throw new InvalidOperationException("BuildingInvincibleSourceBuff.OnAdd failed: host is not a building logic context.");

        building.SetPermanentInvincibilityByBuff(true);
        building.RegisterInvincibleSource(_sourceId);
    }

    public override void OnRemove()
    {
        if (hostEntity is not IBuildingLogicContext building)
            throw new InvalidOperationException("BuildingInvincibleSourceBuff.OnRemove failed: host is not a building logic context.");

        building.UnregisterInvincibleSource(_sourceId);
        building.SetPermanentInvincibilityByBuff(false);
    }
}

public sealed class BuildingCollisionBlockingBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private bool _applied;

    public override void OnAdd()
    {
        if (!(hostEntity is IBuildingLogicContext building))
            throw new InvalidOperationException($"BuildingCollisionBlockingBuff.OnAdd failed: host is not a building logic context. host={hostEntity?.CharacterKey}.");

        if (_applied)
            return;

        building.SetCollisionBlockingByBuff(false);
        _applied = true;
    }

    public override void OnRemove()
    {
        if (!_applied)
            return;

        _applied = false;
        if (hostEntity is IBuildingLogicContext building)
            building.SetCollisionBlockingByBuff(true);
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(_applied);
    }
}

public sealed class BuildingPermanentStealthBuff : BuffCallback
{
    public override void OnAdd()
    {
        if (!(hostEntity is IBuildingLogicContext building))
            throw new InvalidOperationException($"BuildingPermanentStealthBuff.OnAdd failed: host is not a building logic context. host={hostEntity?.CharacterKey}.");

        building.SetPermanentStealthByBuff(true);
    }

    public override void OnRemove()
    {
        if (hostEntity is IBuildingLogicContext building)
            building.SetPermanentStealthByBuff(false);
    }
}

public sealed class TrapRevealOnFirstTriggerBuff : BuffCallback
{
    public override void OnAttackImpact(IEntityContext target)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (hostEntity is not IBuildingLogicContext building)
            throw new InvalidOperationException("TrapRevealOnFirstTriggerBuff.OnAttackImpact failed: host is not a building logic context.");

        building.SetPermanentStealthByBuff(false);
    }
}

public sealed class AmmoReloadBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 _delaySeconds;
    private Fix64 _timer;

    public AmmoReloadBuff(Fix64 delaySeconds)
    {
        _delaySeconds = Fix64.Max(Fix64.Zero, delaySeconds);
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        if (hostEntity == null || !hostEntity.Alive)
            return;

        WeaponComp weaponComp = hostEntity.WeaponComp;
        if (weaponComp == null || !weaponComp.HasAmmunition || weaponComp.CurrentAmmo > 0)
        {
            _timer = Fix64.Zero;
            return;
        }

        _timer += (Fix64)deltaTime;
        if (_timer < BuildingCombatModifierUtility.ResolveAmmoReloadDelay(hostEntity, _delaySeconds))
            return;

        weaponComp.ReloadFull();
        _timer = Fix64.Zero;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(_timer.RawValue);
    }
}

public sealed class AmmoReloadDelayModifierBuff : BuffCallback
{
    public Fix64 DeltaSeconds { get; }

    public AmmoReloadDelayModifierBuff(Fix64 deltaSeconds)
    {
        DeltaSeconds = deltaSeconds;
    }
}

public sealed class BlindSpotRangeBuff : BuffCallback
{
    public Fix64 GameDistanceDelta { get; }

    public BlindSpotRangeBuff(Fix64 gameDistanceDelta)
    {
        GameDistanceDelta = gameDistanceDelta;
    }

    public override bool CanStartAttack()
    {
        if (hostEntity == null)
            throw new InvalidOperationException("BlindSpotRangeBuff is not initialized.");

        BlindSpotRangeBuff first = null;
        Fix64 gameDistance = Fix64.Zero;
        if (hostEntity.BuffComp is not CharacterBuffComp buffComp)
            throw new InvalidOperationException(
                $"Blind spot building has no CharacterBuffComp. building={hostEntity.CharacterKey}.");

        foreach (BuffCallback module in buffComp.EnumerateAllModules())
        {
            if (module is not BlindSpotRangeBuff blindSpot)
                continue;
            first ??= blindSpot;
            gameDistance += blindSpot.GameDistanceDelta;
        }

        if (!ReferenceEquals(first, this))
            return true;

        IEntityContext target = hostEntity.TargetComp?.CurrentTarget
            ?? throw new InvalidOperationException(
                $"Blind spot building tried to attack without a target. building={hostEntity.CharacterKey}.");
        Fix64 worldDistance = DistanceUnitConverter.ConvertToWorld(Fix64.Max(Fix64.Zero, gameDistance));
        return hostEntity.LogicFrameDistanceToTargetSurfaceFixed(target) >= worldDistance;
    }
}

public sealed class PullOnOutgoingDamageBuff : BuffCallback
{
    private readonly Fix64 _pullLevel;

    public PullOnOutgoingDamageBuff(Fix64 pullLevel)
    {
        _pullLevel = pullLevel;
    }

    public override bool CanStartAttack()
    {
        if (hostEntity == null)
            throw new InvalidOperationException("Pull buff is not initialized.");
        if (hostEntity.DurationMoveEffectComp == null)
            throw new InvalidOperationException($"Pull source has no displacement component. source={hostEntity.LogicEntityId.Value}.");
        return !hostEntity.DurationMoveEffectComp.HasActiveOutgoingPullTether;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (target is not IEntityContext targetEntity)
            return baseDamage;
        if (hostEntity == null)
            throw new InvalidOperationException("Pull buff is not initialized.");
        if (hostEntity.BuffComp is not CharacterBuffComp buffComp)
            throw new InvalidOperationException($"Pull source has no CharacterBuffComp. source={hostEntity.LogicEntityId.Value}.");

        Fix64 totalLevel = Fix64.Zero;
        PullOnOutgoingDamageBuff first = null;
        foreach (BuffCallback module in buffComp.EnumerateAllModules())
        {
            if (module is not PullOnOutgoingDamageBuff pull)
                continue;
            first ??= pull;
            totalLevel += pull._pullLevel;
        }
        if (first == null)
            throw new InvalidOperationException("Pull buff is missing from its host BuffComp.");
        if (!ReferenceEquals(first, this))
            return baseDamage;
        if (targetEntity.DurationMoveEffectComp == null)
            throw new InvalidOperationException($"Pull target has no displacement component. target={targetEntity.LogicEntityId.Value}.");

        targetEntity.DurationMoveEffectComp.TryStartPull(hostEntity.LogicEntityId, totalLevel);
        return baseDamage;
    }
}

public static class BuildingCombatModifierUtility
{
    public static Fix64 ResolveAmmoReloadDelay(IEntityContext host, Fix64 baseDelaySeconds)
    {
        Fix64 result = baseDelaySeconds;
        var buffComp = host?.BuffComp as AAAGame.Scripts.BuffSystem.CharacterBuffComp;
        if (buffComp != null)
        {
            foreach (BuffCallback module in buffComp.EnumerateAllModules())
            {
                if (module is AmmoReloadDelayModifierBuff modifier)
                    result += modifier.DeltaSeconds;
            }
        }

        return Fix64.Max(Fix64.Zero, result);
    }

}

public sealed class PhaseAmmoResetBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private bool _subscribed;

    public override void OnAdd()
    {
        ReloadAmmo();
        Subscribe();
    }

    public override void OnRemove()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;

        LogicPhaseCommandService.PhaseApplied += OnLogicPhaseApplied;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;

        LogicPhaseCommandService.PhaseApplied -= OnLogicPhaseApplied;
        _subscribed = false;
    }

    private void OnLogicPhaseApplied(GamePhase oldPhase, GamePhase newPhase)
    {
        ReloadAmmo();
    }

    private void ReloadAmmo()
    {
        hostEntity?.WeaponComp?.ReloadFull();
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(_subscribed);
    }
}

public sealed class BlindAttackMissBuff : BuffCallback, ILogicDeterministicStateContributor
{
    public Fix64 ChancePercent { get; }
    private readonly DeterministicProgressAccumulator _missProgress = new DeterministicProgressAccumulator();
    public override bool IsNegativeStatus => true;

    public BlindAttackMissBuff(Fix64 chancePercent)
    {
        ChancePercent = chancePercent;
    }

    public bool TryConsumeMiss()
    {
        if (ChancePercent <= Fix64.Zero)
            return false;

        return _missProgress.AdvanceAndConsume(ChancePercent / (Fix64)100);
    }

    public DeterministicProgressSnapshot CaptureProgressSnapshot()
    {
        return _missProgress.CaptureSnapshot();
    }

    public void RestoreProgressSnapshot(DeterministicProgressSnapshot snapshot)
    {
        _missProgress.RestoreSnapshot(snapshot);
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(ChancePercent.RawValue);
        hasher.Add(_missProgress.CaptureSnapshot().ProgressRaw);
    }
}

public static class AttackMissUtility
{
    public static bool ShouldMissAttack(IEntityContext attacker)
    {
        if (!(attacker?.BuffComp is AAAGame.Scripts.BuffSystem.CharacterBuffComp buffComp))
            return false;

        BlindAttackMissBuff strongest = null;
        foreach (BuffCallback module in buffComp.EnumerateAllModules())
        {
            if (module is not BlindAttackMissBuff blind)
                continue;

            if (strongest == null || blind.ChancePercent > strongest.ChancePercent)
                strongest = blind;
        }

        return strongest != null && strongest.TryConsumeMiss();
    }
}

