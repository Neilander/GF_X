using System;
using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;

public static class LogicGameplayStateHasher
{
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
        ulong frame = LogicFrameRuntime.CurrentFrame;
        if (frame == 0 || !LogicFrameRuntime.IsTimelineRunning || LogicFrameRuntime.IsTicking)
            throw new InvalidOperationException("LogicGameplayStateHasher requires a completed positive logic frame.");
        if (LogicEntityFrameSnapshotService.CapturedFrame != frame
            || MAEntityLogicFrameSystem.LastCompletedFrame != frame
            || LogicAgentCollisionShadowService.LastCompletedFrame != frame
            || LogicDamageEventService.LastCompletedFrame != frame)
        {
            throw new InvalidOperationException(
                $"LogicGameplayStateHasher frame mismatch. frame={frame}, snapshot={LogicEntityFrameSnapshotService.CapturedFrame}, phase={MAEntityLogicFrameSystem.LastCompletedFrame}, movement={LogicAgentCollisionShadowService.LastCompletedFrame}, damage={LogicDamageEventService.LastCompletedFrame}.");
        }

        var hasher = new LogicStateHasher();
        hasher.Add(0x47414D4553544154UL);
        hasher.Add(frame);
        AddEntities(hasher, frame);
        AddLifecycle(hasher, frame);
        AddObstacles(hasher, frame);
        AddDamageEvents(hasher);
        AddProjectiles(hasher);
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(hasher);
        hasher.Add(LogicEntityIdAllocator.CaptureSnapshot().LastAllocatedValue);
        hasher.Add(LogicPersistentIdAllocator.CaptureSnapshot().LastBuildingInstanceValue);
        return hasher.Hash;
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
            if (entity is MAEntity maEntity)
            {
                hasher.Add(maEntity.IsOutOfCombat);
                hasher.Add(maEntity.OutOfCombatElapsedLogicTime.RawValue);
            }
            else
            {
                hasher.Add(false);
                hasher.Add(0L);
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
            if (entity.AtkComp is DirectAtkComp directAttack)
                AddAttack(hasher, directAttack.CaptureDeterministicState());
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

    private static void AddAttack(LogicStateHasher hasher, DirectAttackDeterministicState state)
    {
        hasher.Add(1);
        hasher.Add((int)state.State);
        hasher.Add(state.AttackCount);
        hasher.Add(state.ActiveWeaponIndex);
        hasher.Add(state.HasSchedule);
        hasher.Add(state.StartFrame);
        hasher.Add(state.HitFrame);
        hasher.Add(state.RecoveryEndFrame);
        hasher.Add(state.ReadyFrame);
        hasher.Add(state.HitCommitted);
        hasher.Add(state.RecoveryCommitted);
        hasher.Add(state.HasAttackStartFrame);
        hasher.Add(state.LastAttackStartFrame);
        hasher.Add(state.MovementLockedByThisAttack);
        hasher.Add(state.LockedTargetId);
        hasher.Add(state.LockedTargetCount);
        for (int i = 0; i < state.LockedTargetIds.Length; i++)
            hasher.Add(state.LockedTargetIds[i]);
    }

    private static void AddBuffs(LogicStateHasher hasher, IBuffComp buffComp)
    {
        if (!(buffComp is CharacterBuffComp characterBuffComp))
        {
            hasher.Add(0);
            return;
        }

        IReadOnlyList<CharacterBuffDeterministicState> states = characterBuffComp.CaptureDeterministicStates();
        hasher.Add(states.Count);
        for (int i = 0; i < states.Count; i++)
        {
            CharacterBuffDeterministicState state = states[i];
            hasher.Add(state.Id);
            hasher.Add(state.Duration.RawValue);
            hasher.Add(state.RemainingTime.RawValue);
            hasher.Add(state.IsForever);
            hasher.Add(state.CurrentStack);
            hasher.Add(state.MaxStack);
            hasher.Add(state.ModuleTypeNames.Length);
            for (int moduleIndex = 0; moduleIndex < state.ModuleTypeNames.Length; moduleIndex++)
                hasher.Add(state.ModuleTypeNames[moduleIndex]);
            hasher.Add(state.BlindProgressRaw);
        }
        characterBuffComp.WriteDeterministicState(hasher);
    }

    private static void AddLifecycle(LogicStateHasher hasher, ulong frame)
    {
        hasher.Add(LogicEntityLifecycleService.RequestedEntityCount);
        hasher.Add(LogicEntityLifecycleService.BoundViewCount);
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
            hasher.Add(command.ViewEntityId);
        }
    }

    private static void AddObstacles(LogicStateHasher hasher, ulong frame)
    {
        var active = new Dictionary<int, LogicObstacleCommand>();
        var pending = new List<LogicObstacleCommand>();
        IReadOnlyList<LogicObstacleCommand> history = LogicObstacleCommandService.History;
        for (int i = 0; i < history.Count; i++)
        {
            LogicObstacleCommand command = history[i];
            if (command.EffectiveFrame > frame)
            {
                pending.Add(command);
                continue;
            }
            if (command.Kind == LogicObstacleCommandKind.Remove)
                active.Remove(command.StableObstacleId);
            else
                active[command.StableObstacleId] = command;
        }

        var activeCommands = new List<LogicObstacleCommand>(active.Values);
        activeCommands.Sort(CompareObstacleCommands);
        pending.Sort(ComparePendingObstacleCommands);
        hasher.Add(activeCommands.Count);
        for (int i = 0; i < activeCommands.Count; i++)
            AddObstacleCommand(hasher, activeCommands[i], false);
        hasher.Add(pending.Count);
        for (int i = 0; i < pending.Count; i++)
            AddObstacleCommand(hasher, pending[i], true);
    }

    private static void AddDamageEvents(LogicStateHasher hasher)
    {
        IReadOnlyList<LogicHealthEvent> events = LogicDamageEventService.LastOrderedEvents;
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
        IReadOnlyList<LogicProjectileDeterministicState> projectiles = LogicProjectileService.CaptureActiveStates();
        hasher.Add(projectiles.Count);
        for (int i = 0; i < projectiles.Count; i++)
        {
            LogicProjectileDeterministicState projectile = projectiles[i];
            hasher.Add(projectile.Id);
            hasher.Add(projectile.AttackerId.Value);
            hasher.Add(projectile.TargetId.Value);
            hasher.Add(projectile.Position.x.RawValue);
            hasher.Add(projectile.Position.y.RawValue);
            hasher.Add(projectile.Speed.RawValue);
        }
    }

    private static void AddObstacleCommand(LogicStateHasher hasher, LogicObstacleCommand command, bool includeTimeline)
    {
        if (includeTimeline)
        {
            hasher.Add(command.EffectiveFrame);
            hasher.Add(command.Sequence);
        }
        hasher.Add((int)command.Kind);
        hasher.Add(command.StableObstacleId);
        hasher.Add(command.Center.x.RawValue);
        hasher.Add(command.Center.y.RawValue);
        hasher.Add(command.HalfExtents.x.RawValue);
        hasher.Add(command.HalfExtents.y.RawValue);
        hasher.Add(command.Radius.RawValue);
    }

    private static int CompareObstacleCommands(LogicObstacleCommand left, LogicObstacleCommand right)
    {
        return left.StableObstacleId.CompareTo(right.StableObstacleId);
    }

    private static int ComparePendingObstacleCommands(LogicObstacleCommand left, LogicObstacleCommand right)
    {
        int result = left.EffectiveFrame.CompareTo(right.EffectiveFrame);
        if (result != 0)
            return result;
        result = left.StableObstacleId.CompareTo(right.StableObstacleId);
        return result != 0 ? result : left.Sequence.CompareTo(right.Sequence);
    }
}
