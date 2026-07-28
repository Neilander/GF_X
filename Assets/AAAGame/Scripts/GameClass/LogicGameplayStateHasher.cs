using System;
using System.Collections.Generic;
using AAAGame.Card;
using AAAGame.Scripts.BuffSystem;

public static class LogicGameplayStateHasher
{
    private static readonly LogicStateHasher s_FrameHasher = new LogicStateHasher();
    private static readonly List<LogicEntityState> s_PendingSpawnStates = new List<LogicEntityState>();
    private static readonly CreatureMainProperty[] s_MainProperties =
    {
        CreatureMainProperty.Def,
        CreatureMainProperty.Health,
        CreatureMainProperty.Speed,
        CreatureMainProperty.CollisionRadius,
        CreatureMainProperty.TurnRate,
        CreatureMainProperty.Sight,
        CreatureMainProperty.StatusResistance,
        CreatureMainProperty.WeightLevel,
    };

    public static ulong ComputeCurrentFrame()
    {
        return ComputeCurrentFrameDigest().GameplayStateHash;
    }

    public static LogicGameplayStateDigest ComputeCurrentFrameDigest()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        if (frame == 0 || !LogicFrameRuntime.IsTimelineRunning || LogicFrameRuntime.IsTicking)
            throw new InvalidOperationException("LogicGameplayStateHasher requires a completed positive logic frame.");
        if (LogicEntityFrameSnapshotService.CapturedFrame != frame
            || MAEntityLogicFrameSystem.LastCompletedFrame != frame
            || LogicAgentCollisionShadowService.LastCompletedFrame != frame
            || LogicDamageEventService.LastCompletedFrame != frame
            || LogicProjectileService.LastCompletedFrame != frame)
        {
            throw new InvalidOperationException(
                $"LogicGameplayStateHasher frame mismatch. frame={frame}, snapshot={LogicEntityFrameSnapshotService.CapturedFrame}, phase={MAEntityLogicFrameSystem.LastCompletedFrame}, movement={LogicAgentCollisionShadowService.LastCompletedFrame}, damage={LogicDamageEventService.LastCompletedFrame}, projectile={LogicProjectileService.LastCompletedFrame}.");
        }

        LogicStateHasher hasher = s_FrameHasher;
        hasher.Reset();
        hasher.Add(0x47414D4553544154UL);
        hasher.Add(frame);
        InGameDataModel.WriteDeterministicState(hasher);
        LogicRewardStateService.WriteDeterministicState(hasher);
        ulong economyHash = hasher.Hash;
        LogicInteractionHoldService.WriteDeterministicState(hasher);
        LogicInteractionTargetStateService.WriteDeterministicState(hasher);
        LogicInteractionAuthorityService.WriteDeterministicState(hasher);
        LogicInteractionCommandService.WriteDeterministicState(hasher);
        LogicCardCommandService.WriteDeterministicState(hasher);
        LogicCardPlacementAuthority.WriteDeterministicState(hasher);
        LogicCardRuntimeState.WriteDeterministicState(hasher);
        LogicSkillSlotCommandService.WriteDeterministicState(hasher);
        LogicPhaseCommandService.WriteDeterministicState(hasher);
        LogicTechEffectCommandService.WriteDeterministicState(hasher);
        ulong commandsHash = hasher.Hash;
        LevelTagRuntime.WriteDeterministicState(hasher);
        GlobalBuffManager.WriteCurrentDeterministicState(hasher);
        BuildingTechRuntimeEffectSO.WriteStaticModifierDeterministicState(hasher);
        LogicBuildingExtraPropsStore.WriteDeterministicState(hasher);
        LogicProductionConditionState.WriteDeterministicState(hasher);
        LogicStrongholdMap.WriteDeterministicState(hasher);
        DefendPhaseRuntime.WriteDeterministicState(hasher);
        ulong worldRulesHash = hasher.Hash;
        AddEntities(hasher, frame);
        ulong entitiesHash = hasher.Hash;
        AddLifecycle(hasher, frame);
        ulong lifecycleHash = hasher.Hash;
        AddObstacles(hasher, frame);
        ulong obstaclesHash = hasher.Hash;
        AddDamageEvents(hasher);
        ulong damageEventsHash = hasher.Hash;
        AddProjectiles(hasher);
        ulong projectilesHash = hasher.Hash;
        LogicNavigationAuthorityDigest navigation =
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigestWithCheckpoints(hasher);
        hasher.Add(LogicEntityIdAllocator.CaptureSnapshot().LastAllocatedValue);
        hasher.Add(LogicPersistentIdAllocator.CaptureSnapshot().LastBuildingInstanceValue);
        return new LogicGameplayStateDigest(
            frame,
            economyHash,
            commandsHash,
            worldRulesHash,
            entitiesHash,
            lifecycleHash,
            obstaclesHash,
            damageEventsHash,
            projectilesHash,
            navigation,
            hasher.Hash,
            hasher.Hash);
    }

    private static void AddEntities(LogicStateHasher hasher, ulong frame)
    {
        LogicEntityFrameSnapshot snapshot = LogicEntityFrameSnapshotService.Current;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        if (snapshot.States.Count != entities.Count)
            throw new InvalidOperationException("LogicGameplayStateHasher entity/snapshot count mismatch.");

        hasher.Add(entities.Count);
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            LogicEntityFrameState state = snapshot.States[i];
            if (entity == null || entity.LogicEntityId != state.EntityId)
                throw new InvalidOperationException($"LogicGameplayStateHasher identity mismatch at index {i}.");

            FixVector2 resolved = LogicAgentCollisionShadowService.GetRequiredResolvedPosition(entity.LogicEntityId, frame);
            hasher.Add(state.EntityId.Value);
            hasher.Add(entity.CharacterKey);
            hasher.Add(entity is LogicEntityState logicState ? logicState.SourceStrongholdId : null);
            hasher.Add(resolved.x.RawValue);
            hasher.Add(resolved.y.RawValue);
            hasher.Add(state.Forward.x.RawValue);
            hasher.Add(state.Forward.y.RawValue);
            hasher.Add(state.CollisionRadius.RawValue);
            hasher.Add((int)state.CombatShape.Kind);
            hasher.Add(state.CombatShape.Center.x.RawValue);
            hasher.Add(state.CombatShape.Center.y.RawValue);
            hasher.Add(state.CombatShape.Radius.RawValue);
            hasher.Add(state.CombatShape.HalfExtents.x.RawValue);
            hasher.Add(state.CombatShape.HalfExtents.y.RawValue);
            hasher.Add((int)entity.Side);
            hasher.Add(entity.Alive);
            hasher.Add(entity.TauntLevel);
            hasher.Add(entity.IsOutOfCombat);
            hasher.Add(entity.OutOfCombatElapsedLogicTime.RawValue);
            if (entity is IHeroLogicContext hero)
                hasher.Add(hero.IsGhostState);
            else
                hasher.Add(false);
            if (entity.TryGetLogicBuilding(out IBuildingLogicContext building))
            {
                hasher.Add(true);
                hasher.Add(building.BuildingInstanceId);
                hasher.Add(building.StrongholdId);
                hasher.Add(building.OwnerFactionId);
                hasher.Add(building.IsDisabled);
                hasher.Add(building.IsPhaseProtected);
                hasher.Add(building.BlocksLogicMovement);
                hasher.Add(building.GetArmyForceWithoutRuntimeRules());
                hasher.Add(building.GetArmyForce());
                hasher.Add(building.GetArmySupplyPerUnit());
                hasher.Add(building.GetArmyOccupiedSupply());
                LogicInteractionOptionService.WriteDeterministicState(hasher, building.InteractionOptions);
            }
            else
            {
                hasher.Add(false);
            }
            if (!(entity is ITargetable targetable))
                throw new InvalidOperationException($"LogicGameplayStateHasher entity {entity.LogicEntityId.Value} is not targetable.");
            hasher.Add(targetable.HealthValue.RawValue);

            for (int propertyIndex = 0; propertyIndex < s_MainProperties.Length; propertyIndex++)
                hasher.Add(entity.GetProperty(s_MainProperties[propertyIndex]).RawValue);

            AddWeapon(hasher, entity.WeaponComp);
            int targetId = entity.TargetComp?.CurrentTarget != null
                           && entity.TargetComp.CurrentTarget.LogicEntityId.IsValid
                ? entity.TargetComp.CurrentTarget.LogicEntityId.Value
                : 0;
            hasher.Add(targetId);
            AddContributor(hasher, entity.TargetComp);
            AddContributor(hasher, entity.Brain);
            AddContributor(hasher, entity.MoveComp);
            AddContributor(hasher, entity.MoveExecutor);
            AddContributor(hasher, entity.DurationMoveEffectComp);
            AddContributor(hasher, entity is ISkillCompHost skillHost ? skillHost.skillComp : null);
            if (entity.AtkComp is DirectAtkComp directAttack)
                AddAttack(hasher, directAttack);
            else
                hasher.Add(0);
            AddBuffs(hasher, entity.BuffComp);
        }
    }

    private static void AddContributor(LogicStateHasher hasher, object value)
    {
        hasher.Add(value != null);
        if (value == null)
            return;
        hasher.Add(value.GetType().FullName);
        if (value is ILogicDeterministicStateContributor contributor)
            contributor.WriteDeterministicState(hasher);
    }

    private static void AddWeapon(LogicStateHasher hasher, WeaponComp weaponComp)
    {
        Weapon weapon = weaponComp?.Data;
        hasher.Add(weapon != null);
        if (weapon == null)
            return;
        hasher.Add((int)weapon.Type);
        for (int statIndex = 0; statIndex <= (int)WeaponStatId.AmmunitionCapacity; statIndex++)
            hasher.Add(weapon.GetStat((WeaponStatId)statIndex).RawValue);
        hasher.Add(weaponComp.CurrentAmmo);
        hasher.Add(weaponComp.MaxAmmo);
    }

    private static void AddAttack(LogicStateHasher hasher, DirectAtkComp directAttack)
    {
        hasher.Add(1);
        directAttack.WriteGameplayDeterministicState(hasher);
    }

    private static void AddBuffs(LogicStateHasher hasher, IBuffComp buffComp)
    {
        if (!(buffComp is CharacterBuffComp characterBuffComp))
        {
            hasher.Add(0);
            return;
        }

        characterBuffComp.WriteGameplayDeterministicState(hasher);
    }

    private static void AddLifecycle(LogicStateHasher hasher, ulong frame)
    {
        LogicEntityStateStore.WriteDeterministicState(hasher);
        AddPendingSpawnStates(hasher);
        hasher.Add(LogicEntityLifecycleService.LastSequence);
        hasher.Add(LogicEntityLifecycleService.AuthorityEntityCount);
        hasher.Add(LogicEntityLifecycleService.ActiveEntityCount);
        IReadOnlyList<LogicEntityLifecycleCommand> commands = LogicEntityLifecycleService.Commands;
        int pendingCount = 0;
        for (int i = 0; i < commands.Count; i++)
        {
            if (commands[i].EffectiveFrame > frame)
                pendingCount++;
        }
        hasher.Add(pendingCount);
        for (int i = 0; i < commands.Count; i++)
        {
            LogicEntityLifecycleCommand command = commands[i];
            if (command.EffectiveFrame <= frame)
                continue;
            hasher.Add(command.EffectiveFrame);
            hasher.Add(command.Sequence);
            hasher.Add((int)command.Kind);
            hasher.Add(command.EntityId.Value);
        }
    }

    private static void AddPendingSpawnStates(LogicStateHasher hasher)
    {
        LogicEntityStateStore.CapturePendingSpawnStates(s_PendingSpawnStates);
        hasher.Add(s_PendingSpawnStates.Count);
        for (int i = 0; i < s_PendingSpawnStates.Count; i++)
        {
            LogicEntityState state = s_PendingSpawnStates[i]
                ?? throw new InvalidOperationException($"LogicGameplayStateHasher pending spawn state {i} is null.");
            state.ValidateReadyForSpawn();
            hasher.Add(state.EntityId.Value);
            hasher.Add(state.IsConfigured);
            hasher.Add(state.NavigationAgentTypeId);
            hasher.Add(state.AllowsZeroCollisionRadius);
            hasher.Add(state.UsesFlowNavigationAgent);
            hasher.Add(state.IsPlayerEntity);
            hasher.Add(state.IsHeroEntity);
            hasher.Add(state.Alive);
            hasher.Add(state.TauntLevel);
            hasher.Add(state.IsOutOfCombat);
            hasher.Add(state.OutOfCombatElapsedLogicTime.RawValue);
            hasher.Add(state.IsGhostState);
            hasher.Add(state.SourceStrongholdId);
            hasher.Add(state.IsPermanentStealth);
            hasher.Add(state.HasPermanentNoAttackCapability);
            hasher.Add(state.BlocksLogicMovement);

            LogicCombatShape combatShape = state.CombatShape;
            hasher.Add((int)combatShape.Kind);
            hasher.Add(combatShape.Center.x.RawValue);
            hasher.Add(combatShape.Center.y.RawValue);
            hasher.Add(combatShape.Radius.RawValue);
            hasher.Add(combatShape.HalfExtents.x.RawValue);
            hasher.Add(combatShape.HalfExtents.y.RawValue);

            hasher.Add(state.IsBuildingEntity);
            if (state.IsBuildingEntity)
            {
                hasher.Add(state.BuildingData?.Identifier);
                hasher.Add(state.BuildingInstanceId);
                hasher.Add(state.StrongholdId);
                hasher.Add(state.OwnerFactionId);
                hasher.Add(state.IsDisabled);
                hasher.Add(state.IsPhaseProtected);
                hasher.Add(state.GetArmyForceWithoutRuntimeRules());
                hasher.Add(state.GetArmyForce());
                hasher.Add(state.GetArmySupplyPerUnit());
                hasher.Add(state.GetArmyOccupiedSupply());
                LogicInteractionOptionService.WriteDeterministicState(hasher, state.InteractionOptions);
                hasher.Add(state.LogicObstacleShapes.Count);
                for (int shapeIndex = 0; shapeIndex < state.LogicObstacleShapes.Count; shapeIndex++)
                {
                    LogicCombatShape shape = state.LogicObstacleShapes[shapeIndex];
                    hasher.Add((int)shape.Kind);
                    hasher.Add(shape.Center.x.RawValue);
                    hasher.Add(shape.Center.y.RawValue);
                    hasher.Add(shape.Radius.RawValue);
                    hasher.Add(shape.HalfExtents.x.RawValue);
                    hasher.Add(shape.HalfExtents.y.RawValue);
                }
            }

            hasher.Add(state.HealthValue.RawValue);
            for (int propertyIndex = 0; propertyIndex < s_MainProperties.Length; propertyIndex++)
                hasher.Add(state.GetProperty(s_MainProperties[propertyIndex]).RawValue);
            AddWeapon(hasher, state.WeaponComp);
            AddContributor(hasher, state.TargetComp);
            AddContributor(hasher, state.Brain);
            AddContributor(hasher, state.MoveComp);
            AddContributor(hasher, state.MoveExecutor);
            AddContributor(hasher, state.DurationMoveEffectComp);
            AddContributor(hasher, state.SkillComp);
            if (state.AtkComp is DirectAtkComp directAttack)
                AddAttack(hasher, directAttack);
            else
                hasher.Add(0);
            AddBuffs(hasher, state.BuffComp);
        }
    }

    private static void AddObstacles(LogicStateHasher hasher, ulong frame)
    {
        LogicObstacleCommandService.WriteDeterministicState(hasher, frame);
    }

    private static void AddDamageEvents(LogicStateHasher hasher)
    {
        IReadOnlyList<LogicHealthEvent> events = LogicDamageEventService.LastOrderedEvents;
        hasher.Add(LogicDamageEventService.LastAssignedSequence);
        hasher.Add(events.Count);
        for (int i = 0; i < events.Count; i++)
        {
            LogicHealthEvent healthEvent = events[i];
            hasher.Add(healthEvent.Sequence);
            hasher.Add((int)healthEvent.Kind);
            hasher.Add(healthEvent.AttackerId.Value);
            hasher.Add(healthEvent.TargetId.Value);
            hasher.Add(healthEvent.Amount.RawValue);
            hasher.Add((int)healthEvent.ModifyType);
            hasher.Add(healthEvent.ApplyDamageHooks);
            hasher.Add(healthEvent.HitIndex);
            hasher.Add(healthEvent.TotalHits);
        }
        hasher.Add(LogicDamageEventService.LastAppliedCount);
        hasher.Add(LogicDamageEventService.LastSkippedDeadTargetCount);
    }

    private static void AddProjectiles(LogicStateHasher hasher)
    {
        LogicProjectileService.WriteActiveDeterministicState(hasher);
    }

}
