using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;
using UnityEngine;

public static class BuildingAbilityIds
{
    public const string SouthernMoon = "Buil_SouthernMoon";
    public const string SortingTable = "Buil_SortingTable";
    public const string MeatRack = "Buil_MeatRack";
    public const string BallLauncher = "Buil_BallLauncher";
    public const string Trap = "Buil_Trap";
    public const string SprinklerHead = "Buil_SprinklerHead";
    public const string Monitor = "Buil_Monitor";
    public const string RoseBush = "Buil_RoseBush";
    public const string Bollard = "Buil_Bollard";
    public const string Pharmacy = "Buil_Pharmacy";
    public const string Restroom = "Buil_Restroom";

    public static bool IsBuilding(BuildingData buildingData, string baseIdentifier)
    {
        return buildingData?.Identifier != null
               && buildingData.Identifier.StartsWith(baseIdentifier, StringComparison.Ordinal);
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
        if (hostEntity == null)
            throw new InvalidOperationException("BuildingInvincibleSourceBuff.OnAdd failed: hostEntity is null.");

        hostEntity.RegisterInvincibleSource(_sourceId);
    }

    public override void OnRemove()
    {
        hostEntity?.UnregisterInvincibleSource(_sourceId);
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

public sealed class RestroomQueueModifierBuff : BuffCallback
{
    public int QueueLimitDelta { get; }
    public Fix64 ReleaseIntervalDelta { get; }

    public RestroomQueueModifierBuff(int queueLimitDelta, Fix64 releaseIntervalDelta)
    {
        QueueLimitDelta = queueLimitDelta;
        ReleaseIntervalDelta = releaseIntervalDelta;
    }
}

public sealed class BlindChanceBonusBuff : BuffCallback
{
    public Fix64 ChancePercentDelta { get; }

    public BlindChanceBonusBuff(Fix64 chancePercentDelta)
    {
        ChancePercentDelta = chancePercentDelta;
    }
}

public sealed class PullOnOutgoingDamageBuff : BuffCallback
{
    private const string PullDistancePerLevelKey = "MeatRackPullDistancePerLevel";
    private const string PullDurationKey = "MeatRackPullDuration";
    private readonly Fix64 _pullLevel;

    public PullOnOutgoingDamageBuff(Fix64 pullLevel)
    {
        _pullLevel = pullLevel;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (_pullLevel <= Fix64.Zero)
            return baseDamage;

        if (hostEntity == null || target is not IEntityContext targetEntity || targetEntity.DurationMoveEffectComp == null)
            return baseDamage;

        FixVector2 direction = LogicEntityFrameSnapshotService.GetRequiredPosition(hostEntity)
                               - LogicEntityFrameSnapshotService.GetRequiredPosition(targetEntity);
        if (FixVector2.SqrMagnitude(direction) == Fix64.Zero)
            return baseDamage;
        direction = direction.GetNormalized();

        Fix64 distance = DistanceUnitConverter.ReadRequiredPositiveFixedConfig(PullDistancePerLevelKey) * _pullLevel;
        Fix64 duration = Fix64.Max(
            Fix64.FromRaw(41),
            DistanceUnitConverter.ReadRequiredPositiveFixedConfig(PullDurationKey));
        Fix64 worldDistance = DistanceUnitConverter.ConvertToWorld(distance);
        FixVector2 speedFixed = direction * (worldDistance / duration);

        targetEntity.AtkComp?.InterruptAttack(AttackInterruptReason.Displacement);
        targetEntity.DurationMoveEffectComp.StartDurationAdditionalMove(duration, speedFixed);
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

    public static int ResolveRestroomQueueLimit(IEntityContext host, int baseQueueLimit)
    {
        int result = baseQueueLimit;
        var buffComp = host?.BuffComp as AAAGame.Scripts.BuffSystem.CharacterBuffComp;
        if (buffComp != null)
        {
            foreach (BuffCallback module in buffComp.EnumerateAllModules())
            {
                if (module is RestroomQueueModifierBuff modifier)
                    result += modifier.QueueLimitDelta;
            }
        }

        return Mathf.Max(1, result);
    }

    public static Fix64 ResolveRestroomReleaseInterval(IEntityContext host, Fix64 baseReleaseInterval)
    {
        Fix64 result = baseReleaseInterval;
        var buffComp = host?.BuffComp as AAAGame.Scripts.BuffSystem.CharacterBuffComp;
        if (buffComp != null)
        {
            foreach (BuffCallback module in buffComp.EnumerateAllModules())
            {
                if (module is RestroomQueueModifierBuff modifier)
                    result += modifier.ReleaseIntervalDelta;
            }
        }

        return Fix64.Max(Fix64.FromRaw(410), result);
    }

    public static Fix64 ResolveBlindPercent(IEntityContext host, Fix64 baseBlindPercent)
    {
        Fix64 result = baseBlindPercent;
        var buffComp = host?.BuffComp as AAAGame.Scripts.BuffSystem.CharacterBuffComp;
        if (buffComp != null)
        {
            foreach (BuffCallback module in buffComp.EnumerateAllModules())
            {
                if (module is BlindChanceBonusBuff modifier)
                    result += modifier.ChancePercentDelta;
            }
        }

        return result > Fix64.Zero ? result : Fix64.Zero;
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

        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnPhaseChanged);
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;

        try
        {
            GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnPhaseChanged);
        }
        catch (GameFrameworkException)
        {
        }

        _subscribed = false;
    }

    private void OnPhaseChanged(object sender, GameEventArgs e)
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

public sealed class RestroomQueueBuff : BuffCallback, ICapability, ILogicDeterministicStateContributor
{
    private const string ActiveControlBuffId = "restroom_queue_control_active";
    private const string HandledBuffPrefix = "restroom_queue_handled";
    private const float DoorOffset = 0.8f;
    private const float SlotSpacing = 0.85f;
    private const float ArriveDistance = 0.08f;
    private const float ScanInterval = 0.1f;

    private readonly int _queueLimit;
    private readonly Fix64 _releaseInterval;
    private readonly List<QueueEntry> _queue = new List<QueueEntry>();
    private Fix64 _releaseTimer;
    private Fix64 _scanTimer;

    public RestroomQueueBuff(int queueLimit, Fix64 releaseInterval)
    {
        _queueLimit = Mathf.Max(1, queueLimit);
        _releaseInterval = Fix64.Max(Fix64.FromRaw(410), releaseInterval);
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        if (!(hostEntity is IBuildingLogicContext building) || !building.Alive || building.IsDisabled)
        {
            ReleaseAll();
            return;
        }

        RemoveInvalidEntries();
        UpdateRelease(deltaTime);
        TryAcquireTargets(deltaTime);
        DriveQueuedTargets(deltaTime);
    }

    public override void OnRemove()
    {
        ReleaseAll();
    }

    public override void OnHostDead()
    {
        ReleaseAll();
    }

    private void UpdateRelease(Fix64 deltaTime)
    {
        if (_queue.Count == 0)
        {
            _releaseTimer = Fix64.Zero;
            return;
        }

        _releaseTimer += deltaTime;
        if (_releaseTimer < GetReleaseInterval())
            return;

        _releaseTimer = Fix64.Zero;
        ReleaseAt(0);
    }

    private void TryAcquireTargets(Fix64 deltaTime)
    {
        if (_queue.Count >= GetQueueLimit())
            return;

        _scanTimer += deltaTime;
        if (_scanTimer < (Fix64)ScanInterval)
            return;

        _scanTimer = Fix64.Zero;
        while (_queue.Count < GetQueueLimit())
        {
            IEntityContext target = FindNearestEligibleTarget();
            if (target == null)
                return;

            AcquireTarget(target);
        }
    }

    private IEntityContext FindNearestEligibleTarget()
    {
        IBuildingLogicContext building = GetBuilding();
        Fix64 range = GetWorldRange(building);
        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new InvalidOperationException("RestroomQueueBuff.FindNearestEligibleTarget failed: EntityRegistry.AllEntities is null.");

        IEntityContext best = null;
        Fix64 bestDistance = range;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null || ReferenceEquals(candidate, building))
                continue;
            if (!IsEligibleTarget(building, candidate))
                continue;

            Fix64 distance = LogicEntityFrameSnapshotService.GetRequiredTargetSurfaceDistance(building, candidate);
            if (distance > range)
                continue;

            if (distance < bestDistance
                || (distance == bestDistance && (best == null || candidate.LogicEntityId < best.LogicEntityId)))
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private bool IsEligibleTarget(IBuildingLogicContext building, IEntityContext candidate)
    {
        if (candidate.IsLogicBuilding())
            return false;
        if (!candidate.IsAttackTargetable())
            return false;
        if (!EntityCombatTeamHelper.IsEnemy(building, candidate))
            return false;
        if (candidate.BuffComp == null)
            throw new InvalidOperationException($"RestroomQueueBuff.IsEligibleTarget failed: target BuffComp is null. target={candidate.CharacterKey}.");
        if (candidate.BuffComp.HasBuff(ActiveControlBuffId))
            return false;

        string handledBuffId = GetHandledBuffId(building);
        if (candidate.BuffComp.HasBuff(handledBuffId))
            return false;

        return true;
    }

    private void AcquireTarget(IEntityContext target)
    {
        if (target == null)
            throw new InvalidOperationException("RestroomQueueBuff.AcquireTarget failed: target is null.");
        if (target.MoveComp == null || target.AtkComp == null || target.MoveExecutor == null)
            throw new InvalidOperationException($"RestroomQueueBuff.AcquireTarget failed: missing movement or attack comp. target={target.CharacterKey}.");
        if (target.BuffComp == null)
            throw new InvalidOperationException($"RestroomQueueBuff.AcquireTarget failed: target BuffComp is null. target={target.CharacterKey}.");

        IBuildingLogicContext building = GetBuilding();
        AddMarkerBuff(target, GetHandledBuffId(building));
        AddMarkerBuff(target, ActiveControlBuffId);

        target.AtkComp.InterruptAttack(AttackInterruptReason.Control);
        target.LockComp(target.AtkComp, this);
        target.LockComp(target.MoveComp, this);
        target.MoveComp.StopMove();
        target.TargetComp?.ClearAggro();

        _queue.Add(new QueueEntry(target));
    }

    private void DriveQueuedTargets(Fix64 deltaTime)
    {
        if (deltaTime <= Fix64.Zero)
            return;

        IBuildingLogicContext building = GetBuilding();
        for (int i = 0; i < _queue.Count; i++)
        {
            IEntityContext target = _queue[i].Target;
            FixVector2 slot = GetQueueSlot(building, i);
            target.MoveComp.SetNavTargetFixed(slot);

            FixVector2 offset = slot - LogicEntityFrameSnapshotService.GetRequiredPosition(target);
            Fix64 distance = FixVector2.Magnitude(offset);
            if (distance <= (Fix64)ArriveDistance)
            {
                target.MoveExecutor.SetOverrideFixed(FixVector2.Zero);
                continue;
            }

            Fix64 speed = DistanceUnitConverter.ConvertToWorld(target.GetProperty(CreatureMainProperty.Speed));
            if (speed <= Fix64.Zero)
                throw new InvalidOperationException($"RestroomQueueBuff.DriveQueuedTargets failed: target speed <= 0. target={target.CharacterKey}.");

            Fix64 maximumSpeed = Fix64.Min(speed, distance / deltaTime);
            target.MoveExecutor.SetOverrideFixed(offset.GetNormalized() * maximumSpeed);
        }
    }

    private FixVector2 GetQueueSlot(IBuildingLogicContext building, int index)
    {
        FixVector2 forward = LogicEntityFrameSnapshotService.GetRequiredForward(building);
        Fix64 buildingRadius = AreaWeaponDamageQuery.GetRequiredRadialExtent(building);
        Fix64 distance = buildingRadius + (Fix64)DoorOffset + (Fix64)SlotSpacing * index;
        return LogicEntityFrameSnapshotService.GetRequiredPosition(building) + forward * distance;
    }

    private void RemoveInvalidEntries()
    {
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            IEntityContext target = _queue[i].Target;
            if (target == null || !target.Alive || target.IsDestroyed())
                ReleaseAt(i);
        }
    }

    private void ReleaseAt(int index)
    {
        if (index < 0 || index >= _queue.Count)
            return;

        QueueEntry entry = _queue[index];
        _queue.RemoveAt(index);
        ReleaseTarget(entry.Target);
    }

    private void ReleaseAll()
    {
        for (int i = _queue.Count - 1; i >= 0; i--)
            ReleaseTarget(_queue[i].Target);

        _queue.Clear();
        _releaseTimer = Fix64.Zero;
        _scanTimer = Fix64.Zero;
    }

    private void ReleaseTarget(IEntityContext target)
    {
        if (target == null)
            return;

        target.BuffComp?.RemoveBuff(ActiveControlBuffId);
        if (target.AtkComp != null)
            target.ResumeComp(target.AtkComp, this);
        if (target.MoveComp != null)
        {
            target.ResumeComp(target.MoveComp, this);
            target.MoveComp.StopMove();
        }
        target.MoveExecutor?.ClearOverride();
    }

    private IBuildingLogicContext GetBuilding()
    {
        if (!(hostEntity is IBuildingLogicContext building))
            throw new InvalidOperationException($"RestroomQueueBuff.GetBuilding failed: host is not a building logic context. host={hostEntity?.CharacterKey}.");
        if (building.BuildingData == null)
            throw new InvalidOperationException("RestroomQueueBuff.GetBuilding failed: BuildingData is null.");

        return building;
    }

    private int GetQueueLimit()
    {
        return BuildingCombatModifierUtility.ResolveRestroomQueueLimit(hostEntity, _queueLimit);
    }

    private Fix64 GetReleaseInterval()
    {
        return BuildingCombatModifierUtility.ResolveRestroomReleaseInterval(hostEntity, _releaseInterval);
    }

    private static Fix64 GetWorldRange(IBuildingLogicContext building)
    {
        WeaponData weaponData = building.BuildingData?.Weapon;
        if (weaponData == null || weaponData.Range <= Fix64.Zero)
            throw new InvalidOperationException($"RestroomQueueBuff.GetWorldRange failed: missing restroom range. building={building.CharacterKey}.");

        return DistanceUnitConverter.ConvertToWorld(weaponData.Range);
    }

    private static string GetHandledBuffId(IBuildingLogicContext building)
    {
        if (building == null)
            throw new InvalidOperationException("RestroomQueueBuff.GetHandledBuffId failed: building is null.");
        if (string.IsNullOrWhiteSpace(building.BuildingInstanceId))
            throw new InvalidOperationException($"RestroomQueueBuff.GetHandledBuffId failed: BuildingInstanceId is empty. building={building.CharacterKey}.");

        int phase = InGameDataModel.GetValue(IngameValueType.Phase);
        return $"{HandledBuffPrefix}_{phase}_{building.BuildingInstanceId}";
    }

    private static void AddMarkerBuff(IEntityContext target, string buffId)
    {
        BuffData buffData = BuffData.Create(
            id: buffId,
            duration: Fix64.Zero,
            isForever: true,
            maxStack: 1,
            modules: new List<BuffCallback>());

        target.BuffComp.AddBuff(buffData, target);
    }

    public void ShutDown()
    {
        ReleaseAll();
    }

    public void Resume()
    {
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(_releaseTimer.RawValue);
        hasher.Add(_scanTimer.RawValue);
        hasher.Add(_queue.Count);
        for (int i = 0; i < _queue.Count; i++)
        {
            IEntityContext target = _queue[i].Target;
            if (target == null || !target.LogicEntityId.IsValid)
                throw new InvalidOperationException($"RestroomQueueBuff contains an invalid target at index {i}.");
            hasher.Add(target.LogicEntityId.Value);
        }
    }

    private readonly struct QueueEntry
    {
        public readonly IEntityContext Target;

        public QueueEntry(IEntityContext target)
        {
            Target = target;
        }
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

public static class MonitorWeaponEffect
{
    private static readonly Fix64 BlindDurationPaddingSeconds = Fix64.FromRaw(410);

    public static void Execute(IEntityContext attacker, IEntityContext mainTarget, WeaponData weaponData)
    {
        if (attacker == null)
            throw new InvalidOperationException("MonitorWeaponEffect.Execute failed: attacker is null.");
        if (mainTarget == null)
            throw new InvalidOperationException($"MonitorWeaponEffect.Execute failed: mainTarget is null. attacker={attacker.CharacterKey}.");
        if (weaponData == null)
            throw new InvalidOperationException($"MonitorWeaponEffect.Execute failed: weaponData is null. attacker={attacker.CharacterKey}.");

        Fix64 blindPercent = ResolveBlindPercent(attacker);
        Fix64 facingConeAngle = ResolveFacingConeAngle(attacker);
        Fix64 radius = DistanceUnitConverter.ConvertToWorld(weaponData.Range);
        var damagedTargets = new HashSet<IEntityContext>();

        TryDamageAndBlind(attacker, mainTarget, weaponData, blindPercent, facingConeAngle, damagedTargets);

        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new InvalidOperationException("MonitorWeaponEffect.Execute failed: EntityRegistry.AllEntities is null.");

        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null || ReferenceEquals(candidate, mainTarget))
                continue;
            if (!candidate.IsAttackTargetable())
                continue;
            if (!EntityCombatTeamHelper.IsEnemy(attacker, candidate))
                continue;

            if (!AreaWeaponDamageQuery.IsWithinCircle(attacker, candidate, radius))
                continue;

            TryDamageAndBlind(attacker, candidate, weaponData, blindPercent, facingConeAngle, damagedTargets);
        }
    }

    private static void TryDamageAndBlind(
        IEntityContext attacker,
        IEntityContext target,
        WeaponData weaponData,
        Fix64 blindPercent,
        Fix64 facingConeAngle,
        HashSet<IEntityContext> damagedTargets)
    {
        if (target == null || damagedTargets.Contains(target))
            return;
        if (!target.IsAttackTargetable())
            return;
        if (!EntityCombatTeamHelper.IsEnemy(attacker, target))
            return;
        if (!MonitorFacingUtility.IsFacingMonitor(target, attacker, facingConeAngle))
            return;

        damagedTargets.Add(target);
        var damage = new Damage(attacker as ITargetable, weaponData.Damage, HealthModifyType.reduce);
        DamageHelper.DoDamage(target as ITargetable, damage, attacker);
        ApplyBlind(
            target,
            blindPercent,
            Fix64.Max(Fix64.FromRaw(410), weaponData.Interval + BlindDurationPaddingSeconds));
    }

    private static void ApplyBlind(IEntityContext target, Fix64 blindPercent, Fix64 duration)
    {
        if (blindPercent <= Fix64.Zero)
            return;

        if (target == null)
            throw new InvalidOperationException("MonitorWeaponEffect.ApplyBlind failed: target is null.");
        if (target.BuffComp == null)
            throw new InvalidOperationException($"MonitorWeaponEffect.ApplyBlind failed: target BuffComp is null. target={target.CharacterKey}.");

        int idValue = Mathf.RoundToInt((float)(blindPercent * (Fix64)1000));
        BuffData buffData = BuffData.Create(
            id: $"debuff_blind_attack_miss_{idValue}",
            duration: duration,
            isForever: false,
            maxStack: 1,
            modules: new List<BuffCallback> { new BlindAttackMissBuff(blindPercent) });

        target.BuffComp.AddBuff(buffData, target);
    }

    private static Fix64 ResolveBlindPercent(IEntityContext attacker)
    {
        Fix64 baseBlindPercent = Fix64.Zero;
        if (attacker is IBuildingLogicContext building && building.BuildingData?.UniqueValues != null && building.BuildingData.UniqueValues.Length > 0)
            baseBlindPercent = building.BuildingData.UniqueValues[0];

        return BuildingCombatModifierUtility.ResolveBlindPercent(attacker, baseBlindPercent);
    }

    public static Fix64 ResolveFacingConeAngle(IEntityContext attacker)
    {
        if (attacker is IBuildingLogicContext building && building.BuildingData?.UniqueValues != null && building.BuildingData.UniqueValues.Length > 1)
            return building.BuildingData.UniqueValues[1];

        return (Fix64)45;
    }
}
