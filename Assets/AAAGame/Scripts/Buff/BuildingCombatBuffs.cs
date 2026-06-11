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

public sealed class BuildingCollisionBlockingBuff : BuffCallback
{
    private readonly List<ColliderState> _states = new List<ColliderState>();
    private bool _applied;

    public override void OnAdd()
    {
        if (!(hostEntity is BuildingEntity building))
            throw new InvalidOperationException($"BuildingCollisionBlockingBuff.OnAdd failed: host is not BuildingEntity. host={hostEntity?.CharacterKey}.");

        if (_applied)
            return;

        Collider[] colliders = building.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || collider.isTrigger)
                continue;

            _states.Add(new ColliderState(collider, collider.enabled));
            collider.enabled = false;
        }

        _applied = true;
        LevelEntity.RequestRebakeNavMesh();
    }

    public override void OnRemove()
    {
        if (!_applied)
            return;

        for (int i = 0; i < _states.Count; i++)
        {
            ColliderState state = _states[i];
            if (state.Collider != null)
                state.Collider.enabled = state.Enabled;
        }

        _states.Clear();
        _applied = false;
        LevelEntity.RequestRebakeNavMesh();
    }

    private readonly struct ColliderState
    {
        public readonly Collider Collider;
        public readonly bool Enabled;

        public ColliderState(Collider collider, bool enabled)
        {
            Collider = collider;
            Enabled = enabled;
        }
    }
}

public sealed class BuildingPermanentStealthBuff : BuffCallback
{
    public override void OnAdd()
    {
        if (!(hostEntity is BuildingEntity building))
            throw new InvalidOperationException($"BuildingPermanentStealthBuff.OnAdd failed: host is not BuildingEntity. host={hostEntity?.CharacterKey}.");

        building.SetPermanentStealthVisibility(true);
    }

    public override void OnRemove()
    {
        if (hostEntity is BuildingEntity building)
            building.SetPermanentStealthVisibility(false);
    }
}

public sealed class AmmoReloadBuff : BuffCallback
{
    private readonly float _delaySeconds;
    private float _timer;

    public AmmoReloadBuff(float delaySeconds)
    {
        _delaySeconds = Mathf.Max(0f, delaySeconds);
    }

    public override void OnUpdate(float deltaTime)
    {
        if (hostEntity == null || !hostEntity.Alive)
            return;

        WeaponComp weaponComp = hostEntity.weaponComp;
        if (weaponComp == null || !weaponComp.HasAmmunition || weaponComp.CurrentAmmo > 0)
        {
            _timer = 0f;
            return;
        }

        _timer += deltaTime;
        if (_timer < BuildingCombatModifierUtility.ResolveAmmoReloadDelay(hostEntity, _delaySeconds))
            return;

        weaponComp.ReloadFull();
        _timer = 0f;
    }
}

public sealed class AmmoReloadDelayModifierBuff : BuffCallback
{
    public float DeltaSeconds { get; }

    public AmmoReloadDelayModifierBuff(float deltaSeconds)
    {
        DeltaSeconds = deltaSeconds;
    }
}

public sealed class RestroomQueueModifierBuff : BuffCallback
{
    public int QueueLimitDelta { get; }
    public float ReleaseIntervalDelta { get; }

    public RestroomQueueModifierBuff(int queueLimitDelta, float releaseIntervalDelta)
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
    private const float DefaultPullDistancePerLevel = 120f;
    private const float DefaultPullDuration = 0.18f;

    private readonly Fix64 _pullLevel;

    public PullOnOutgoingDamageBuff(Fix64 pullLevel)
    {
        _pullLevel = pullLevel;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (_pullLevel <= Fix64.Zero)
            return baseDamage;

        if (hostEntity == null || target is not MAEntity targetEntity || targetEntity.durationMoveEffectComp == null)
            return baseDamage;

        Vector3 direction = hostEntity.Position - targetEntity.Position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return baseDamage;

        direction.Normalize();

        Fix64 distance = (Fix64)(GF.Config != null
            ? GF.Config.GetFloat(PullDistancePerLevelKey, DefaultPullDistancePerLevel)
            : DefaultPullDistancePerLevel) * _pullLevel;
        float duration = Mathf.Max(0.01f, GF.Config != null
            ? GF.Config.GetFloat(PullDurationKey, DefaultPullDuration)
            : DefaultPullDuration);
        float worldDistance = DistanceUnitConverter.ConvertToWorldFloat(distance);
        Vector3 speed = direction * (worldDistance / duration);

        targetEntity.atkComp?.InterruptAttack(AttackInterruptReason.Displacement);
        targetEntity.durationMoveEffectComp.StartDurationAdditionalMove(duration, speed);
        return baseDamage;
    }
}

public static class BuildingCombatModifierUtility
{
    public static float ResolveAmmoReloadDelay(MAEntity host, float baseDelaySeconds)
    {
        float result = baseDelaySeconds;
        var buffComp = host?.BuffComp as AAAGame.Scripts.BuffSystem.CharacterBuffComp;
        if (buffComp != null)
        {
            foreach (BuffCallback module in buffComp.EnumerateAllModules())
            {
                if (module is AmmoReloadDelayModifierBuff modifier)
                    result += modifier.DeltaSeconds;
            }
        }

        return Mathf.Max(0f, result);
    }

    public static int ResolveRestroomQueueLimit(MAEntity host, int baseQueueLimit)
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

    public static float ResolveRestroomReleaseInterval(MAEntity host, float baseReleaseInterval)
    {
        float result = baseReleaseInterval;
        var buffComp = host?.BuffComp as AAAGame.Scripts.BuffSystem.CharacterBuffComp;
        if (buffComp != null)
        {
            foreach (BuffCallback module in buffComp.EnumerateAllModules())
            {
                if (module is RestroomQueueModifierBuff modifier)
                    result += modifier.ReleaseIntervalDelta;
            }
        }

        return Mathf.Max(0.1f, result);
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

public sealed class PhaseAmmoResetBuff : BuffCallback
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
        hostEntity?.weaponComp?.ReloadFull();
    }
}

public sealed class RestroomQueueBuff : BuffCallback, ICapability
{
    private const string ActiveControlBuffId = "restroom_queue_control_active";
    private const string HandledBuffPrefix = "restroom_queue_handled";
    private const float DoorOffset = 0.8f;
    private const float SlotSpacing = 0.85f;
    private const float ArriveDistance = 0.08f;
    private const float ScanInterval = 0.1f;

    private readonly int _queueLimit;
    private readonly float _releaseInterval;
    private readonly List<QueueEntry> _queue = new List<QueueEntry>();
    private float _releaseTimer;
    private float _scanTimer;

    public RestroomQueueBuff(int queueLimit, float releaseInterval)
    {
        _queueLimit = Mathf.Max(1, queueLimit);
        _releaseInterval = Mathf.Max(0.1f, releaseInterval);
    }

    public override void OnUpdate(float deltaTime)
    {
        if (!(hostEntity is BuildingEntity building) || !building.Alive || building.IsDisabled)
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

    private void UpdateRelease(float deltaTime)
    {
        if (_queue.Count == 0)
        {
            _releaseTimer = 0f;
            return;
        }

        _releaseTimer += deltaTime;
        if (_releaseTimer < GetReleaseInterval())
            return;

        _releaseTimer = 0f;
        ReleaseAt(0);
    }

    private void TryAcquireTargets(float deltaTime)
    {
        if (_queue.Count >= GetQueueLimit())
            return;

        _scanTimer += deltaTime;
        if (_scanTimer < ScanInterval)
            return;

        _scanTimer = 0f;
        while (_queue.Count < GetQueueLimit())
        {
            MAEntity target = FindNearestEligibleTarget();
            if (target == null)
                return;

            AcquireTarget(target);
        }
    }

    private MAEntity FindNearestEligibleTarget()
    {
        BuildingEntity building = GetBuilding();
        float range = GetWorldRange(building);
        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new InvalidOperationException("RestroomQueueBuff.FindNearestEligibleTarget failed: EntityRegistry.AllEntities is null.");

        MAEntity best = null;
        float bestDistance = range;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null || ReferenceEquals(candidate, building))
                continue;
            if (!IsEligibleTarget(building, candidate))
                continue;

            float distance = building.DistanceToTargetSurface(candidate);
            if (distance > range)
                continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (MAEntity)candidate;
            }
        }

        return best;
    }

    private bool IsEligibleTarget(BuildingEntity building, IEntityContext candidate)
    {
        if (!(candidate is MAEntity entity) || candidate is BuildingEntity)
            return false;
        if (!candidate.IsAttackTargetable())
            return false;
        if (!EntityCombatTeamHelper.IsEnemy(building, candidate))
            return false;
        if (entity.BuffComp == null)
            throw new InvalidOperationException($"RestroomQueueBuff.IsEligibleTarget failed: target BuffComp is null. target={entity.CharacterKey}.");
        if (entity.BuffComp.HasBuff(ActiveControlBuffId))
            return false;

        string handledBuffId = GetHandledBuffId(building);
        if (entity.BuffComp.HasBuff(handledBuffId))
            return false;

        return true;
    }

    private void AcquireTarget(MAEntity target)
    {
        if (target == null)
            throw new InvalidOperationException("RestroomQueueBuff.AcquireTarget failed: target is null.");
        if (target.moveComp == null || target.atkComp == null || target.moveExecutor == null)
            throw new InvalidOperationException($"RestroomQueueBuff.AcquireTarget failed: missing movement or attack comp. target={target.CharacterKey}.");
        if (target.BuffComp == null)
            throw new InvalidOperationException($"RestroomQueueBuff.AcquireTarget failed: target BuffComp is null. target={target.CharacterKey}.");

        BuildingEntity building = GetBuilding();
        AddMarkerBuff(target, GetHandledBuffId(building));
        AddMarkerBuff(target, ActiveControlBuffId);

        target.atkComp.InterruptAttack(AttackInterruptReason.Control);
        target.LockComp(target.atkComp, this);
        target.LockComp(target.moveComp, this);
        target.moveComp.StopMove();
        target.targetComp?.ClearAggro();

        _queue.Add(new QueueEntry(target));
    }

    private void DriveQueuedTargets(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        BuildingEntity building = GetBuilding();
        for (int i = 0; i < _queue.Count; i++)
        {
            MAEntity target = _queue[i].Target;
            Vector3 slot = GetQueueSlot(building, i);
            target.moveComp.SetNavTarget(slot);

            Vector3 offset = slot - target.Position;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance <= ArriveDistance)
            {
                target.moveExecutor.SetOverride(Vector3.zero);
                continue;
            }

            float speed = DistanceUnitConverter.ConvertToWorldFloat(target.GetProperty(CreatureMainProperty.Speed));
            if (speed <= 0f)
                throw new InvalidOperationException($"RestroomQueueBuff.DriveQueuedTargets failed: target speed <= 0. target={target.CharacterKey}.");

            Vector3 velocity = offset.normalized * Mathf.Min(speed, distance / deltaTime);
            target.moveExecutor.SetOverride(velocity);
        }
    }

    private Vector3 GetQueueSlot(BuildingEntity building, int index)
    {
        Vector3 forward = building.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        float buildingRadius = AreaWeaponDamageQuery.GetCollisionRadiusWorld(building);
        float distance = buildingRadius + DoorOffset + SlotSpacing * index;
        return building.Position + forward * distance;
    }

    private void RemoveInvalidEntries()
    {
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            MAEntity target = _queue[i].Target;
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
        _releaseTimer = 0f;
        _scanTimer = 0f;
    }

    private void ReleaseTarget(MAEntity target)
    {
        if (target == null)
            return;

        target.BuffComp?.RemoveBuff(ActiveControlBuffId);
        if (target.atkComp != null)
            target.ResumeComp(target.atkComp, this);
        if (target.moveComp != null)
        {
            target.ResumeComp(target.moveComp, this);
            target.moveComp.StopMove();
        }
        target.moveExecutor?.ClearOverride();
    }

    private BuildingEntity GetBuilding()
    {
        if (!(hostEntity is BuildingEntity building))
            throw new InvalidOperationException($"RestroomQueueBuff.GetBuilding failed: host is not BuildingEntity. host={hostEntity?.CharacterKey}.");
        if (building.buildingData == null)
            throw new InvalidOperationException("RestroomQueueBuff.GetBuilding failed: buildingData is null.");

        return building;
    }

    private int GetQueueLimit()
    {
        return BuildingCombatModifierUtility.ResolveRestroomQueueLimit(hostEntity, _queueLimit);
    }

    private float GetReleaseInterval()
    {
        return BuildingCombatModifierUtility.ResolveRestroomReleaseInterval(hostEntity, _releaseInterval);
    }

    private static float GetWorldRange(BuildingEntity building)
    {
        WeaponData weaponData = building.buildingData?.Weapon;
        if (weaponData == null || weaponData.Range <= Fix64.Zero)
            throw new InvalidOperationException($"RestroomQueueBuff.GetWorldRange failed: missing restroom range. building={building.CharacterKey}.");

        return DistanceUnitConverter.ConvertToWorldFloat(weaponData.Range);
    }

    private static string GetHandledBuffId(BuildingEntity building)
    {
        if (building == null)
            throw new InvalidOperationException("RestroomQueueBuff.GetHandledBuffId failed: building is null.");
        if (string.IsNullOrWhiteSpace(building.BuildingInstanceId))
            throw new InvalidOperationException($"RestroomQueueBuff.GetHandledBuffId failed: BuildingInstanceId is empty. building={building.CharacterKey}.");

        int phase = InGameDataModel.GetValue(IngameValueType.Phase);
        return $"{HandledBuffPrefix}_{phase}_{building.BuildingInstanceId}";
    }

    private static void AddMarkerBuff(MAEntity target, string buffId)
    {
        BuffData buffData = BuffData.Create(
            id: buffId,
            duration: float.MaxValue,
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

    private readonly struct QueueEntry
    {
        public readonly MAEntity Target;

        public QueueEntry(MAEntity target)
        {
            Target = target;
        }
    }
}

public sealed class BlindAttackMissBuff : BuffCallback
{
    public Fix64 ChancePercent { get; }
    private Fix64 _progress;
    public override bool IsNegativeStatus => true;

    public BlindAttackMissBuff(Fix64 chancePercent)
    {
        ChancePercent = chancePercent;
    }

    public bool TryConsumeMiss()
    {
        if (ChancePercent <= Fix64.Zero)
            return false;

        _progress += ChancePercent / (Fix64)100;
        if (_progress < Fix64.One)
            return false;

        _progress -= Fix64.One;
        return true;
    }
}

public static class AttackMissUtility
{
    public static bool ShouldMissAttack(IEntityContext attacker)
    {
        if (!(attacker is MAEntity entity) || !(entity.BuffComp is AAAGame.Scripts.BuffSystem.CharacterBuffComp buffComp))
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
    private const float BlindDurationPaddingSeconds = 0.1f;

    public static void Execute(IEntityContext attacker, IEntityContext mainTarget, WeaponData weaponData)
    {
        if (attacker == null)
            throw new InvalidOperationException("MonitorWeaponEffect.Execute failed: attacker is null.");
        if (mainTarget == null)
            throw new InvalidOperationException($"MonitorWeaponEffect.Execute failed: mainTarget is null. attacker={attacker.CharacterKey}.");
        if (weaponData == null)
            throw new InvalidOperationException($"MonitorWeaponEffect.Execute failed: weaponData is null. attacker={attacker.CharacterKey}.");

        Fix64 blindPercent = ResolveBlindPercent(attacker);
        float facingConeAngle = ResolveFacingConeAngle(attacker);
        float radius = DistanceUnitConverter.ConvertToWorldFloat(weaponData.Range);
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

            float reach = radius + AreaWeaponDamageQuery.GetCollisionRadiusWorld(candidate);
            if (AreaWeaponDamageQuery.HorizontalDistance(attacker.Position, candidate.Position) > reach)
                continue;

            TryDamageAndBlind(attacker, candidate, weaponData, blindPercent, facingConeAngle, damagedTargets);
        }
    }

    private static void TryDamageAndBlind(
        IEntityContext attacker,
        IEntityContext target,
        WeaponData weaponData,
        Fix64 blindPercent,
        float facingConeAngle,
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
        ApplyBlind(target, blindPercent, Mathf.Max(0.1f, (float)weaponData.Interval + BlindDurationPaddingSeconds));
    }

    private static void ApplyBlind(IEntityContext target, Fix64 blindPercent, float duration)
    {
        if (blindPercent <= Fix64.Zero)
            return;

        if (!(target is MAEntity targetEntity))
            throw new InvalidOperationException($"MonitorWeaponEffect.ApplyBlind failed: target is not MAEntity. target={target?.CharacterKey}.");
        if (targetEntity.BuffComp == null)
            throw new InvalidOperationException($"MonitorWeaponEffect.ApplyBlind failed: target BuffComp is null. target={target.CharacterKey}.");

        int idValue = Mathf.RoundToInt((float)(blindPercent * (Fix64)1000));
        BuffData buffData = BuffData.Create(
            id: $"debuff_blind_attack_miss_{idValue}",
            duration: duration,
            isForever: false,
            maxStack: 1,
            modules: new List<BuffCallback> { new BlindAttackMissBuff(blindPercent) });

        targetEntity.BuffComp.AddBuff(buffData, targetEntity);
    }

    private static Fix64 ResolveBlindPercent(IEntityContext attacker)
    {
        Fix64 baseBlindPercent = Fix64.Zero;
        if (attacker is BuildingEntity building && building.buildingData?.UniqueValues != null && building.buildingData.UniqueValues.Length > 0)
            baseBlindPercent = building.buildingData.UniqueValues[0];

        return BuildingCombatModifierUtility.ResolveBlindPercent(attacker, baseBlindPercent);
    }

    public static float ResolveFacingConeAngle(IEntityContext attacker)
    {
        if (attacker is BuildingEntity building && building.buildingData?.UniqueValues != null && building.buildingData.UniqueValues.Length > 1)
            return (float)building.buildingData.UniqueValues[1];

        return 45f;
    }
}
